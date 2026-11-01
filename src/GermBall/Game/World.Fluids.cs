using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Core;
using GermBall.Render;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>
/// Liquids. Acid pools are wave-simulated height fields that sit inside their craters: the level is set below the lowest
/// point of the crater rim and the surface only covers ground that is under that level and connected to the pit, so a pool
/// can never hang in the air. Anything moving through a pool, shots, blasts and drips make ripples that spread and bounce.
/// Germs bleed: hits spray drops of their fluid (each kind of germ its own colour), which fly, land, and become puddles
/// that merge, creep downhill on slopes, and slowly dry up; drops that land in a pool ripple it instead.
/// </summary>
public sealed partial class World
{
    // ------------------------------------------------------------------ pools
    sealed class LiquidPool
    {
        public float x0, z0, cell, level; public int n;          // grid origin, cell size, surface height, corners per side
        public float[] u, v; public bool[] wet;                    // wave height and speed per corner; corners under the surface
        public Geometry geo; public Mesh mesh; public Crater d; public float radius;
        public bool acid = true;
    }
    readonly List<LiquidPool> POOLS = new();

    /// <summary>A pool in a crater: the level is under the lowest rim point, the surface covers only the flooded pit.</summary>
    LiquidPool MakePool(Crater d, Material mat)
    {
        // the lowest point on the way out of the pit, ring by ring: the water can rise no higher than that
        float spill = float.MaxValue;
        for (int k = 0; k < 48; k++)
        {
            float a = k / 48f * TAU, highest = float.MinValue;
            for (float rr = 0; rr <= d.R + 12; rr += 1) highest = MathF.Max(highest, TerrainH(d.X + MathF.Cos(a) * rr, d.Z + MathF.Sin(a) * rr));
            spill = MathF.Min(spill, highest);
        }
        float bottom = TerrainH(d.X, d.Z), level = MathF.Min(spill - 0.35f, bottom + d.D * 0.55f);
        if (level <= bottom + 0.3f) return null;   // too shallow a dip to hold anything
        float cell = 0.9f, half = d.R + 10; int n = (int)(half * 2 / cell) + 1;
        var P = new LiquidPool { x0 = d.X - half, z0 = d.Z - half, cell = cell, n = n, level = level, d = d, radius = d.R, u = new float[n * n], v = new float[n * n], wet = new bool[n * n] };
        // flood from the centre: only ground below the level and connected to the pit gets water
        var q = new Queue<int>(); int c0 = (n / 2) * n + n / 2; P.wet[c0] = true; q.Enqueue(c0);
        while (q.Count > 0)
        {
            int id = q.Dequeue(), ix = id % n, iz = id / n;
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int jx = ix + dx, jz = iz + dz; if (jx < 0 || jz < 0 || jx >= n || jz >= n) continue;
                int j = jz * n + jx; if (P.wet[j]) continue;
                if (TerrainH(P.x0 + jx * cell, P.z0 + jz * cell) < level - 0.02f) { P.wet[j] = true; q.Enqueue(j); }
            }
        }
        // the surface: every quad touching a flooded corner (dry corners sit under the bank, so the shoreline meets the ground)
        var pos = new float[n * n * 3]; var nor = new float[n * n * 3]; var idx = new List<uint>();
        for (int iz = 0; iz < n; iz++) for (int ix = 0; ix < n; ix++) { int i = iz * n + ix; pos[i * 3] = P.x0 + ix * cell; pos[i * 3 + 1] = level; pos[i * 3 + 2] = P.z0 + iz * cell; nor[i * 3 + 1] = 1; }
        for (int iz = 0; iz < n - 1; iz++) for (int ix = 0; ix < n - 1; ix++)
            {
                uint a = (uint)(iz * n + ix), b = a + 1, c = a + (uint)n, e = c + 1;
                if (!(P.wet[a] || P.wet[b] || P.wet[c] || P.wet[e])) continue;
                idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(b); idx.Add(c); idx.Add(e);
            }
        if (idx.Count == 0) return null;
        P.geo = new Geometry { Position = pos, Normal = nor, Index = idx.ToArray() };
        P.mesh = new Mesh(P.geo, mat) { RenderOrder = 1, FrustumCulled = false }; scene.Add(P.mesh);
        POOLS.Add(P); return P;
    }

    LiquidPool PoolAt(float x, float z, float y, float above = 0.4f)
    {
        foreach (var P in POOLS)
        {
            int ix = (int)MathF.Round((x - P.x0) / P.cell), iz = (int)MathF.Round((z - P.z0) / P.cell);
            if (ix < 0 || iz < 0 || ix >= P.n || iz >= P.n || !P.wet[iz * P.n + ix]) continue;
            if (y < P.level + above) return P;
        }
        return null;
    }
    /// <summary>Disturbs whatever pool is at a point: a ripple of the given strength and radius (metres).</summary>
    public void Splash(float x, float z, float y, float strength, float radius = 1)
    {
        var P = PoolAt(x, z, y, 1.2f); if (P == null) return;
        int r = Math.Max(1, (int)(radius / P.cell)), cx = (int)MathF.Round((x - P.x0) / P.cell), cz = (int)MathF.Round((z - P.z0) / P.cell);
        for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
            {
                int ix = cx + dx, iz = cz + dz; if (ix < 0 || iz < 0 || ix >= P.n || iz >= P.n) continue;
                float f = 1 - MathF.Sqrt(dx * dx + dz * dz) / (r + 1); if (f <= 0) continue;
                P.v[iz * P.n + ix] -= strength * f;
            }
    }

    void StepPools(float dt)
    {
        foreach (var P in POOLS)
        {
            int n = P.n; var u = P.u; var v = P.v; var wet = P.wet;
            // a little life on the surface: a bubble here and there
            if (R() < dt * 5) { int i = (int)(R() * n * n); if (wet[i]) v[i] += 0.6f + R(); }
            const float C2 = 60, DAMP = 0.6f;
            for (int iz = 1; iz < n - 1; iz++)
                for (int ix = 1; ix < n - 1; ix++)
                {
                    int i = iz * n + ix; if (!wet[i]) continue;
                    // neighbours on dry ground reflect the wave (they count as the same height)
                    float l = wet[i - 1] ? u[i - 1] : u[i], r = wet[i + 1] ? u[i + 1] : u[i], b = wet[i - n] ? u[i - n] : u[i], f = wet[i + n] ? u[i + n] : u[i];
                    v[i] += ((l + r + b + f) * 0.25f - u[i]) * C2 * dt; v[i] *= 1 - DAMP * dt;
                }
            for (int i = 0; i < u.Length; i++) { if (!wet[i]) continue; u[i] += v[i] * dt; u[i] = Clamp(u[i], -0.6f, 0.6f); u[i] *= 1 - 0.15f * dt; }
        }
        // bodies wading through make waves; things in acid burn
        foreach (var A in actors)
        {
            var G = A.P; if (!G.alive || A.dormant || A.hidden) continue;
            float sp = Hypot(G.vx, G.vz); if (sp < 1.5f && G.ground) continue;
            var P = PoolAt(G.x, G.z, G.y, 0.3f); if (P == null) continue;
            if (R() < dt * 20) Splash(G.x, G.z, G.y, MathF.Min(3, sp * 0.25f + (G.ground ? 0 : MathF.Abs(G.vy) * 0.3f)), MathF.Max(1, G.R * 1.5f));
        }
    }

    void UpdatePoolMeshes(float t)
    {
        foreach (var P in POOLS)
        {
            int n = P.n; var pos = P.geo.Position; var nor = P.geo.Normal; var u = P.u;
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    int i = iz * n + ix;
                    // dry corners stay just under the surface (under the bank) so the shoreline never shows a gap
                    pos[i * 3 + 1] = P.wet[i] ? P.level + u[i] + 0.04f * MathF.Sin(t * 1.7f + ix * 0.6f) * MathF.Sin(t * 1.3f + iz * 0.5f) : P.level - 0.05f;
                }
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    int i = iz * n + ix;
                    float hl = pos[(iz * n + Math.Max(0, ix - 1)) * 3 + 1], hr = pos[(iz * n + Math.Min(n - 1, ix + 1)) * 3 + 1];
                    float hb = pos[(Math.Max(0, iz - 1) * n + ix) * 3 + 1], hf = pos[(Math.Min(n - 1, iz + 1) * n + ix) * 3 + 1];
                    float nx = hl - hr, nz = hb - hf, ny = 2 * P.cell, l = 1 / MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                    nor[i * 3] = nx * l; nor[i * 3 + 1] = ny * l; nor[i * 3 + 2] = nz * l;
                }
            P.geo.Version++;
        }
    }

    // ------------------------------------------------------------------ drops and puddles
    struct Drop { public float x, y, z, vx, vy, vz, s; public Vector3 col; }
    sealed class Puddle { public float x, y, z, vol, vx, vz, age; public Vector3 col; public bool onTerrain; }
    readonly List<Drop> DROPS = new(); readonly List<Puddle> PUDDLES = new();
    InstancedMesh dropMesh, puddleMesh;
    const int MAX_DROPS = 700, MAX_PUDDLES = 320;

    static readonly Dictionary<string, uint> FLUID_COL = new() { ["small"] = 0xc8e83a, ["normal"] = 0x5ad04a, ["assassin"] = 0xa04aff, ["brute"] = 0xff8a2a, ["mech"] = 0x3ab0ff };
    Vector3 FluidOf(Actor B) => C(FLUID_COL.TryGetValue(TYPES[B.type].Mimic ?? B.type, out var c) ? c : 0x8ad03au);

    /// <summary>Spray n drops of a germ's fluid from a point, thrown roughly along (dx, dz) and up.</summary>
    public void Bleed(Actor B, float x, float y, float z, int n, float dx = 0, float dz = 0, float force = 3)
    {
        if (B == null || B.team == "hero") return;   // the medicine doesn't bleed: the germs do
        var col = FluidOf(B); float l = Hypot(dx, dz); if (l > 0) { dx /= l; dz /= l; }
        for (int i = 0; i < n && DROPS.Count < MAX_DROPS; i++)
        {
            float a = R() * TAU, sp = force * (0.4f + R());
            DROPS.Add(new Drop { x = x, y = y, z = z, vx = MathF.Cos(a) * sp * 0.6f + dx * sp, vy = 1.5f + R() * force, vz = MathF.Sin(a) * sp * 0.6f + dz * sp, s = 0.05f + R() * 0.07f, col = col });
        }
    }

    void AddPuddle(float x, float y, float z, float vol, Vector3 col)
    {
        foreach (var p in PUDDLES)
        {   // merge into a puddle it lands in (the colours mix)
            float r = PuddleR(p.vol);
            if (MathF.Abs(p.x - x) < r && MathF.Abs(p.z - z) < r && MathF.Abs(p.y - y) < 0.4f) { p.col = Vector3.Lerp(p.col, col, vol / (p.vol + vol)); p.vol = MathF.Min(p.vol + vol, 6); p.age = MathF.Min(p.age, 4); return; }
        }
        if (PUDDLES.Count >= MAX_PUDDLES) PUDDLES.RemoveAt(0);
        PUDDLES.Add(new Puddle { x = x, y = y, z = z, vol = vol, col = col, onTerrain = MathF.Abs(TerrainH(x, z) - y) < 0.15f });
    }
    static float PuddleR(float vol) => 0.18f + MathF.Sqrt(vol) * 0.55f;

    void StepFluids(float dt)
    {
        StepPools(dt);
        for (int i = DROPS.Count - 1; i >= 0; i--)
        {
            var d = DROPS[i];
            d.vy -= 18 * dt; d.vx *= 1 - 0.4f * dt; d.vz *= 1 - 0.4f * dt;
            d.x += d.vx * dt; d.y += d.vy * dt; d.z += d.vz * dt;
            float g = GroundAt(d.x, d.z, d.y + 0.5f, true).h;
            if (d.y <= g)
            {
                if (PoolAt(d.x, d.z, d.y, 0.3f) != null) Splash(d.x, d.z, d.y, 0.8f, 0.6f);
                else AddPuddle(d.x, g + 0.015f, d.z, d.s * d.s * 18, d.col);
                DROPS.RemoveAt(i); continue;
            }
            DROPS[i] = d;
        }
        for (int i = PUDDLES.Count - 1; i >= 0; i--)
        {
            var p = PUDDLES[i]; p.age += dt;
            if (p.onTerrain && p.vol > 0.05f)
            {   // on a slope a puddle creeps downhill (bigger ones run faster), leaving a little behind as it goes
                var ts = TerrainSample(p.x, p.z); float slope = Hypot(ts.gx, ts.gz);
                p.vx += -ts.gx * 9 * dt * MathF.Min(1, p.vol * 2); p.vz += -ts.gz * 9 * dt * MathF.Min(1, p.vol * 2);
                float fr = slope < 0.08f ? 6 : 2.5f; p.vx *= MathF.Max(0, 1 - fr * dt); p.vz *= MathF.Max(0, 1 - fr * dt);
                p.x += p.vx * dt; p.z += p.vz * dt; p.y = TerrainH(p.x, p.z) + 0.015f;
                float mv = Hypot(p.vx, p.vz); if (mv > 0.2f) p.vol = MathF.Max(0.02f, p.vol - mv * dt * 0.02f);
                var pool = PoolAt(p.x, p.z, p.y, 0.2f); if (pool != null) { Splash(p.x, p.z, p.y, 0.5f); PUDDLES.RemoveAt(i); continue; }
            }
            if (p.age > 35) p.vol -= dt * 0.03f;   // drying up
            if (p.vol <= 0.01f) PUDDLES.RemoveAt(i);
        }
    }

    void UpdateFluidMeshes(float t)
    {
        if (dropMesh == null)
        {
            var glossy = new Material { Kind = MatKind.Phong, Color = Vector3.One, Shininess = 120, Specular = new Vector3(0.6f) };
            dropMesh = scene.Add(new InstancedMesh(Geo.Sphere(1, 6, 5), glossy, MAX_DROPS) { Count = 0 });
            puddleMesh = scene.Add(new InstancedMesh(Geo.Cylinder(1, 1, 1, 14), new Material { Kind = MatKind.Phong, Color = Vector3.One, Shininess = 140, Specular = new Vector3(0.7f), Transparent = true, Opacity = 0.85f, DepthWrite = false }, MAX_PUDDLES) { Count = 0, RenderOrder = 1 });
        }
        int nd = 0;
        foreach (var d in DROPS)
        {   // stretched along its flight
            var vel = new Vector3(d.vx, d.vy, d.vz); float sp = vel.Length();
            var q = sp > 0.5f ? QuatFromTo(Vector3.UnitY, vel / sp) : Quaternion.Identity;
            dropMesh.Matrices[nd] = Matrix4x4.CreateScale(d.s, d.s * (1 + MathF.Min(2.5f, sp * 0.12f)), d.s) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(d.x, d.y, d.z);
            dropMesh.SetColor(nd, d.col); nd++;
        }
        dropMesh.Count = nd; dropMesh.Touch();
        int np = 0;
        foreach (var p in PUDDLES)
        {
            float r = PuddleR(p.vol), wob = 1 + 0.03f * MathF.Sin(t * 3 + p.x);
            float sx = r * wob * (1 + MathF.Min(0.8f, Hypot(p.vx, p.vz) * 0.4f)), sz = r / wob;
            float yaw = MathF.Atan2(p.vx, p.vz);
            puddleMesh.Matrices[np] = Matrix4x4.CreateScale(sz, 0.02f, sx) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(p.x, p.y, p.z);
            puddleMesh.SetColor(np, p.col * 0.8f); np++;
        }
        puddleMesh.Count = np; puddleMesh.Touch();
    }
    static Quaternion QuatFromTo(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b); if (d > 0.9999f) return Quaternion.Identity; if (d < -0.9999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        var ax = Vector3.Cross(a, b); return Quaternion.Normalize(new Quaternion(ax, 1 + d));
    }
}

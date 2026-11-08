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
            const float C2 = 22, DAMP = 1.1f;   // thick acid: slow, heavy, rounded waves
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

    // ------------------------------------------------------------------ gel
    // Germ fluid is a gel: it comes out in soft globs that stretch as they fly, splat and jiggle when they land, settle into
    // glossy domes, slump into neighbours they touch (merging into bigger blobs), stay joined to close neighbours by thin
    // strands, creep slowly downhill, wobble when stepped in, and eventually dry up.
    sealed class Gel
    {
        public float x, y, z, vx, vy, vz, vol, sq, sqv, age, wob; public Vector3 col; public bool down, onTerrain;
        public float R => 0.07f + MathF.Pow(vol, 1 / 3f) * 0.5f;
    }
    readonly List<Gel> GELS = new();
    InstancedMesh gelMesh, strandMesh;
    const int MAX_GELS = 420;

    static readonly Dictionary<string, uint> FLUID_COL = new() { ["small"] = 0xc8e83a, ["normal"] = 0x5ad04a, ["assassin"] = 0xa04aff, ["brute"] = 0xff8a2a, ["mech"] = 0x3ab0ff };
    Vector3 FluidOf(Actor B) => C(FLUID_COL.TryGetValue(TYPES[B.type].Mimic ?? B.type, out var c) ? c : 0x8ad03au);

    /// <summary>A germ is hit: n globs of its gel (a few big, more small) thrown roughly along (dx, dz) and up.</summary>
    public void Bleed(Actor B, float x, float y, float z, int n, float dx = 0, float dz = 0, float force = 3)
    {
        if (B == null || B.team == "hero") return;   // the medicine doesn't bleed: the germs do
        var col = FluidOf(B); float l = Hypot(dx, dz); if (l > 0) { dx /= l; dz /= l; }
        int globs = Math.Max(1, (n + 2) / 3);
        for (int i = 0; i < globs; i++)
        {
            if (GELS.Count >= MAX_GELS) GELS.RemoveAt(0);
            float a = R() * TAU, sp = force * (0.3f + R() * 0.8f), big = i == 0 ? 0.03f + R() * 0.03f : 0.004f + R() * 0.012f;
            GELS.Add(new Gel { x = x, y = y, z = z, vx = MathF.Cos(a) * sp * 0.5f + dx * sp, vy = 1.2f + R() * force * 0.8f, vz = MathF.Sin(a) * sp * 0.5f + dz * sp, vol = big, col = col * (0.85f + R() * 0.3f), wob = R() * 6 });
        }
    }

    void StepFluids(float dt)
    {
        StepPools(dt);
        for (int i = GELS.Count - 1; i >= 0; i--)
        {
            var g = GELS[i]; g.age += dt;
            // the jiggle: a springy squash that rings down
            g.sqv += (-g.sq * 90 - g.sqv * 7) * dt; g.sq += g.sqv * dt; g.sq = Clamp(g.sq, -0.6f, 0.8f);
            if (!g.down)
            {
                g.vy -= 16 * dt; g.vx *= 1 - 0.6f * dt; g.vz *= 1 - 0.6f * dt;
                g.x += g.vx * dt; g.y += g.vy * dt; g.z += g.vz * dt;
                float gr = GroundAt(g.x, g.z, g.y + 0.5f, true).h;
                if (g.y - g.R * 0.3f <= gr)
                {
                    if (PoolAt(g.x, g.z, g.y, 0.3f) != null) { Splash(g.x, g.z, g.y, 0.6f + g.vol * 20, 0.8f); GELS.RemoveAt(i); continue; }
                    // splat: squash flat on impact (harder hits squash more), then settle as a dome
                    g.down = true; g.y = gr; g.sqv -= MathF.Min(6, -g.vy * 0.6f); g.vx *= 0.15f; g.vz *= 0.15f; g.vy = 0;
                    g.onTerrain = MathF.Abs(TerrainH(g.x, g.z) - gr) < 0.15f;
                    // landing on another blob: it slumps into it
                    foreach (var o in GELS) if (o != g && o.down && Hypot(o.x - g.x, o.z - g.z) < (o.R + g.R) * 0.75f && MathF.Abs(o.y - g.y) < 0.4f) { Merge(o, g); GELS.RemoveAt(i); goto next; }
                }
            }
            else
            {
                if (g.onTerrain)
                {   // a thick gel creeps downhill (heavier blobs a bit faster) and stops on the flat
                    var ts = TerrainSample(g.x, g.z); float slope = Hypot(ts.gx, ts.gz);
                    float k = slope > 0.12f ? 1.8f * MathF.Min(1, g.vol * 12) : 0;
                    g.vx += -ts.gx * k * dt; g.vz += -ts.gz * k * dt; g.vx *= MathF.Max(0, 1 - 3 * dt); g.vz *= MathF.Max(0, 1 - 3 * dt);
                    g.x += g.vx * dt; g.z += g.vz * dt; g.y = TerrainH(g.x, g.z);
                    if (PoolAt(g.x, g.z, g.y, 0.2f) != null) { Splash(g.x, g.z, g.y, 0.5f); GELS.RemoveAt(i); continue; }
                }
                // stepped in: a squash and a shove
                foreach (var A in actors)
                {
                    var G = A.P; if (!G.alive || A.dormant) continue;
                    float dx = g.x - G.x, dz = g.z - G.z, rr = G.R + g.R; if (MathF.Abs(dx) > rr || MathF.Abs(dz) > rr || MathF.Abs(G.y - g.y) > 1) continue;
                    float d = MathF.Sqrt(dx * dx + dz * dz); if (d > rr) continue;
                    float sp = Hypot(G.vx, G.vz); if (sp > 0.5f && g.sq > -0.3f) g.sqv -= sp * 0.4f;
                    if (d > 0.01f && g.onTerrain) { g.vx += dx / d * sp * 0.3f * dt * 10; g.vz += dz / d * sp * 0.3f * dt * 10; }
                }
                if (g.age > 40) g.vol -= dt * 0.0012f;   // drying up
                if (g.vol <= 0.0015f) { GELS.RemoveAt(i); continue; }
            }
            next:;
        }
        // settled neighbours that touch slump together into one bigger blob (a few checks a step)
        for (int n = 0, tries = 0; tries < 40 && GELS.Count > 1; tries++)
        {
            var a = GELS[(int)(R() * GELS.Count)]; if (!a.down) continue;
            foreach (var b in GELS)
                if (b != a && b.down && Hypot(b.x - a.x, b.z - a.z) < (a.R + b.R) * 0.6f && MathF.Abs(b.y - a.y) < 0.3f) { Merge(a, b); GELS.Remove(b); n++; break; }
            if (n > 3) break;
        }
    }
    void Merge(Gel into, Gel g)
    {
        float t = g.vol / (into.vol + g.vol);
        into.x += (g.x - into.x) * t; into.z += (g.z - into.z) * t; into.col = Vector3.Lerp(into.col, g.col, t);
        into.vol = MathF.Min(into.vol + g.vol, 0.5f); into.sqv -= 2.5f; into.age = MathF.Min(into.age, 10);
    }

    void UpdateFluidMeshes(float t)
    {
        if (gelMesh == null)
        {   // glossy and a little translucent, lit from inside by its own colour
            var gel = new Material { Kind = MatKind.Phong, Color = Vector3.One, Shininess = 160, Specular = new Vector3(0.9f), Emissive = new Vector3(0.12f), Transparent = true, Opacity = 0.86f, DepthWrite = true };
            gelMesh = scene.Add(new InstancedMesh(Geo.Sphere(1, 14, 10), gel, MAX_GELS) { Count = 0, RenderOrder = 1 });
            strandMesh = scene.Add(new InstancedMesh(Geo.Cylinder(1, 1, 1, 8), gel, 200) { Count = 0, RenderOrder = 1 });
        }
        int ng = 0, ns = 0;
        for (int i = 0; i < GELS.Count; i++)
        {
            var g = GELS[i]; float r = g.R, wob = 1 + 0.05f * MathF.Sin(t * 5 + g.wob) * MathF.Min(1, MathF.Abs(g.sq) * 4 + 0.2f);
            Matrix4x4 M;
            if (!g.down)
            {   // a flying glob stretches along its path and wobbles
                var vel = new Vector3(g.vx, g.vy, g.vz); float sp = vel.Length(); var q = sp > 0.5f ? QuatFromTo(Vector3.UnitY, vel / sp) : Quaternion.Identity;
                float st = 1 + MathF.Min(1.2f, sp * 0.08f);
                M = Matrix4x4.CreateScale(r / MathF.Sqrt(st) * wob, r * st * (1 + g.sq * 0.5f), r / MathF.Sqrt(st) / wob) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(g.x, g.y, g.z);
            }
            else
            {   // a settled dome: squash spreads it wider as it flattens
                float h = MathF.Max(0.15f, 0.55f + g.sq * 0.6f), spread = 1 + (0.55f - h) * 0.5f;
                float yaw = MathF.Atan2(g.vx, g.vz);
                M = Matrix4x4.CreateScale(r * spread * wob, r * h, r * spread / wob) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(g.x, g.y - r * h * 0.25f, g.z);
            }
            gelMesh.Matrices[ng] = M; gelMesh.SetColor(ng, g.col); ng++;
            // strands to close settled neighbours: thin bridges of gel, thinner the further apart
            if (g.down && ns < 200)
                for (int j = i + 1; j < GELS.Count && ns < 200; j++)
                {
                    var o = GELS[j]; if (!o.down) continue;
                    float dx = o.x - g.x, dz = o.z - g.z, d = MathF.Sqrt(dx * dx + dz * dz), reach = (g.R + o.R) * 1.7f;
                    if (d > reach || d < 0.05f || MathF.Abs(o.y - g.y) > 0.5f) continue;
                    float th = MathF.Min(g.R, o.R) * 0.35f * (1 - d / reach) + 0.01f;
                    var a = new Vector3(g.x, g.y + g.R * 0.12f, g.z); var b = new Vector3(o.x, o.y + o.R * 0.12f, o.z); var dir = b - a; float len = dir.Length();
                    var q = QuatFromTo(Vector3.UnitY, dir / len);
                    strandMesh.Matrices[ns] = Matrix4x4.CreateScale(th, len, th * 0.6f) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation((a + b) * 0.5f);
                    strandMesh.SetColor(ns, Vector3.Lerp(g.col, o.col, 0.5f)); ns++;
                }
        }
        gelMesh.Count = ng; gelMesh.Touch(); strandMesh.Count = ns; strandMesh.Touch();
    }
    static Quaternion QuatFromTo(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b); if (d > 0.9999f) return Quaternion.Identity; if (d < -0.9999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        var ax = Vector3.Cross(a, b); return Quaternion.Normalize(new Quaternion(ax, 1 + d));
    }
}

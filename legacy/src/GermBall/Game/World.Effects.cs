using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Core;
using GermBall.Render;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed partial class World
{
    // =====================================================================
    // Sprite effects, DOOM style: little hand-made-looking pixel animations (fireballs, smoke, sparks, muzzle flashes,
    // plasma rings, ichor), drawn at low resolution with an ordered dither and blown up with no smoothing
    // =====================================================================
    static readonly float[] BAYER = new[] { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 }.Select(v => (v + 0.5f) / 16).ToArray();
    static readonly Dictionary<string, uint[]> SPR_PAL = new()
    {
        ["fire"] = new uint[] { 0x2a1030, 0x5a1a4a, 0xa0202a, 0xe0401a, 0xff7a1a, 0xffb21a, 0xffe66a, 0xffffff },
        ["smoke"] = new uint[] { 0x2b2140, 0x4a3b63, 0x6e5f8c, 0x9a8ab8, 0xcfc2e6 },
        ["spark"] = new uint[] { 0x7a0a2a, 0xff2638, 0xff8a3a, 0xffe14a, 0xffffff },
        ["plasma"] = new uint[] { 0x0a3a5a, 0x1a8aa8, 0x3cffd8, 0xb8fff0, 0xffffff },
        ["goo"] = new uint[] { 0x3a0a3a, 0x8a1a7a, 0xd23ab8, 0xff7ae0, 0xffd0f4 },
        ["spore"] = new uint[] { 0x8a6a1a, 0xd8b83a, 0xfff27a, 0xffffe0 },
    };
    static float SprNoise(float x, float y, float s) => VNoise(x * 0.23f + s * 13.1f, y * 0.23f - s * 7.7f) * 0.6f + VNoise(x * 0.5f + s * 3.3f, y * 0.5f + s) * 0.4f;
    sealed record SprAnim(int N, string Pal, int Size, Func<float, float, float, float> F);
    // each animation: frames drawn by f(px, py, t) -> heat 0..1, or -1 for clear
    static readonly Dictionary<string, SprAnim> SPR_ANIMS = new()
    {
        ["boom"] = new(8, "fire", 32, (x, y, t) =>
        {
            float r = 4 + 12 * MathF.Sqrt(t), d = Hypot(x, y) / r + SprNoise(x, y, t * 3) * 0.32f;
            if (d > 1) return -1;
            if (t > 0.55f && SprNoise(x * 1.4f, y * 1.4f, 9) * 0.5f + 0.5f < (t - 0.55f) * 2.2f) return -1;   // the cloud breaks up into holes
            return Clamp((1 - d) * (1.35f - t * 1.25f) + SprNoise(x, y, 4 + t) * 0.25f, 0, 1);
        }),
        ["smoke"] = new(6, "smoke", 24, (x, y, t) =>
        {
            float r = 5 + 6 * t, d = Hypot(x, y) / r + SprNoise(x, y, 2 + t) * 0.35f;
            if (d > 1 || (t > 0.4f && SprNoise(x * 1.7f, y * 1.7f, 5) * 0.5f + 0.5f < (t - 0.4f) * 1.6f)) return -1;
            return Clamp(0.9f - d * 0.6f - t * 0.35f + (y < 0 ? 0.15f : 0), 0, 1);
        }),
        ["spark"] = new(4, "spark", 24, (x, y, t) =>
        {
            float a = MathF.Atan2(y, x), d = Hypot(x, y), rays = MathF.Pow(MathF.Abs(MathF.Cos(a * 4 + t)), 18), R2 = (3 + 8 * (1 - t)) * (0.35f + rays * 0.9f);
            if (d > R2) return -1;
            return Clamp(1.1f - d / R2 - t * 0.5f, 0, 1);
        }),
        ["flash"] = new(3, "fire", 24, (x, y, t) =>
        {
            float a = MathF.Atan2(y, x), d = Hypot(x, y), rays = MathF.Pow(MathF.Abs(MathF.Cos(a * 3 + 0.5f)), 10), R2 = (4 + 6 * rays) * (1 - t * 0.5f);
            if (d > R2) return -1;
            return Clamp(1.2f - d / R2 * 0.8f - t * 0.4f, 0.25f, 1);
        }),
        ["plasma"] = new(6, "plasma", 32, (x, y, t) =>
        {
            float d = Hypot(x, y), r = 3 + 12 * t, w = 2.5f + 2 * (1 - t);
            float ring = 1 - MathF.Abs(d - r) / w, core = 1 - d / (5 * (1 - t) + 0.01f);
            float v = MathF.Max(ring, core) + SprNoise(x, y, t * 5) * 0.2f;
            return v <= 0.05f ? -1 : Clamp(v - t * 0.3f, 0, 1);
        }),
        ["goo"] = new(5, "goo", 24, (x, y, t) =>
        {
            float v = -1;
            for (int k = 0; k < 7; k++) { float a = k * 0.9f + 0.3f, dist = t * 10 * (0.6f + (k % 3) * 0.2f), cx = MathF.Cos(a) * dist, cy = MathF.Sin(a) * dist + t * t * 6, r = 3.2f * (1 - t * 0.6f); float d = Hypot(x - cx, y - cy) / r; if (d < 1) v = MathF.Max(v, 1 - d); }
            float c = Hypot(x, y) / (4 * (1 - t) + 0.01f); if (c < 1) v = MathF.Max(v, 1 - c);
            return v;
        }),
        ["spore"] = new(1, "spore", 8, (x, y, _) => { float d = Hypot(x, y) / 3.6f; return d > 1 ? -1 : 1 - d; }),
    };
    readonly Dictionary<string, Texture> SPR_TEX = new();
    sealed class Spr { public string kind = "smoke"; public float t, life = 1, size = 1, grow, vx, vy, vz, grav, x, y, z, rot; public bool on; }
    const int SPR_N = 600;   // the browser pooled 260; there is room for more here
    readonly Spr[] sprites = Enumerable.Range(0, SPR_N).Select(_ => new Spr()).ToArray();
    int sprIdx;
    readonly Dictionary<string, InstancedMesh[]> sprMesh = new();
    void InitSprites()
    {
        var quad = Geo.Plane(1, 1);
        foreach (var (k, A) in SPR_ANIMS)
        {
            int S = A.Size; var pal = SPR_PAL[A.Pal]; var tex = new Texture(S * A.N, S) { Nearest = true, Mipmaps = false };
            for (int fi = 0; fi < A.N; fi++)
            {
                float t = A.N > 1 ? (float)fi / (A.N - 1) : 0;
                for (int py = 0; py < S; py++) for (int px = 0; px < S; px++)
                    {
                        float v = A.F(px - S / 2f + 0.5f, -(py - S / 2f + 0.5f), t); if (v < 0) continue;
                        float th = BAYER[(py & 3) * 4 + (px & 3)]; int idx = (int)Clamp(MathF.Floor(v * (pal.Length - 1) + th), 0, pal.Length - 1); uint c = pal[idx]; int o = (py * S * A.N + fi * S + px) * 4;
                        tex.Pixels[o] = (byte)(c >> 16); tex.Pixels[o + 1] = (byte)(c >> 8); tex.Pixels[o + 2] = (byte)c; tex.Pixels[o + 3] = 255;
                    }
            }
            SPR_TEX[k] = tex;
            var frames = new InstancedMesh[A.N];
            for (int fi = 0; fi < A.N; fi++)
            {
                var m = new Material { Kind = MatKind.Basic, Map = tex, Transparent = true, AlphaTest = 0.5f, DepthWrite = false, Side = Side.Double, MapRepeat = new Vector2(1f / A.N, 1), MapOffset = new Vector2((float)fi / A.N, 0) };
                frames[fi] = new InstancedMesh(quad, m, 64) { RenderOrder = 2 }; frames[fi].Count = 0; scene.Add(frames[fi]);
            }
            sprMesh[k] = frames;
        }
    }
    // spawn an effect: kind, position, world size, life in seconds, velocity
    public void Fx(string kind, float x, float y, float z, float size, float life, float vx = 0, float vy = 0, float vz = 0, float grav = 0, float grow = 0)
    {
        if (!SPR_ANIMS.ContainsKey(kind)) return;
        Spr p = null;
        for (int k = 0; k < 8; k++) { var q = sprites[(sprIdx + k) % SPR_N]; if (!q.on) { p = q; sprIdx = (sprIdx + k + 1) % SPR_N; break; } }
        if (p == null) { p = sprites[sprIdx]; sprIdx = (sprIdx + 1) % SPR_N; }
        p.kind = kind; p.t = 0; p.life = life; p.size = size; p.grow = grow; p.vx = vx; p.vy = vy; p.vz = vz; p.grav = grav; p.x = x; p.y = y; p.z = z; p.on = true;
        p.rot = R() < 0.5f ? 0 : PI / 2 * MathF.Floor(R() * 4);
    }
    void StepSprites(float fdt)
    {
        foreach (var p in sprites)
        {
            if (!p.on) continue;
            p.t += fdt; float k = p.t / p.life;
            if (k >= 1) { p.on = false; continue; }
            p.vy -= p.grav * fdt; p.x += p.vx * fdt; p.y += p.vy * fdt; p.z += p.vz * fdt;
        }
    }
    /// <summary>Writes every live sprite as a camera-facing quad into its frame's instanced mesh.</summary>
    void DrawSprites(Camera cam)
    {
        foreach (var fr in sprMesh.Values) foreach (var m in fr) m.Count = 0;
        var camRot = cam.Rotation;
        foreach (var p in sprites)
        {
            if (!p.on) continue;
            var fr = sprMesh[p.kind]; float k = p.t / p.life; int f = Math.Min(fr.Length - 1, (int)MathF.Floor(k * fr.Length));
            var m = fr[f]; if (m.Count >= m.Capacity) m.EnsureCapacity(m.Count + 1);
            float sz = p.size * (1 + p.grow * k);
            m.Matrices[m.Count++] = Matrix4x4.CreateScale(sz) * Matrix4x4.CreateRotationZ(p.rot) * Matrix4x4.CreateFromQuaternion(camRot) * Matrix4x4.CreateTranslation(p.x, p.y, p.z);
        }
        foreach (var fr in sprMesh.Values) foreach (var m in fr) m.Touch();
    }
    // the old effect calls, now sprites
    public void Puff(float x, float y, float z, float spread, float up, float size)
        => Fx("smoke", x + (R() - 0.5f) * spread * 0.2f, y, z + (R() - 0.5f) * spread * 0.2f, size * 3.2f + 0.3f, 0.55f + R() * 0.4f, (R() - 0.5f) * spread, up * R(), (R() - 0.5f) * spread, -0.6f, 0.6f);
    public void HitSpark(float x, float y, float z, float k = 1) => Fx("spark", x, y, z, 0.45f + 0.35f * k, 0.16f);
    void GooBurst(float x, float y, float z) => Fx("goo", x, y, z, 1.1f, 0.35f, 0, 0.6f, 0);
    void MuzzleFlash(float x, float y, float z, float k = 1) => Fx("flash", x, y, z, 0.5f * k, 0.07f);
    public void Boom(float x, float y, float z, float R2, string color = null)
    {
        bool plasma = color == "plasma";
        Fx(plasma ? "plasma" : "boom", x, y, z, R2 * 2.1f, 0.55f);
        if (!plasma) for (int i = 0; i < 4; i++) { float a = R() * 6.28f, d = R2 * 0.45f; Fx("boom", x + MathF.Cos(a) * d, y + R() * R2 * 0.5f, z + MathF.Sin(a) * d, R2 * (0.9f + R() * 0.5f), 0.45f + R() * 0.25f, 0, 1.5f, 0); }
        for (int i = 0; i < 6; i++) Puff(x, y + R2 * 0.3f, z, R2 * 1.4f, R2 * 0.8f, 0.35f + R() * 0.3f);
    }

    // ---------------------------------------------------------------- instanced draw lists (the original's IM / inst)
    public sealed class Inst { public InstancedMesh mesh; public int n; }
    public readonly Dictionary<string, Inst> IM = new();
    void MkInst(string key, Geometry geo, Material mat, int max, bool shadow)
    {
        var m = new InstancedMesh(geo, mat, max) { CastShadow = shadow, ReceiveShadow = shadow }; m.Count = 0; scene.Add(m);
        IM[key] = new Inst { mesh = m };
    }
    /// <summary>Adds one instance (position, quaternion, scale) to a draw list. Lists grow on demand: no fixed browser cap.</summary>
    public void InstAdd(string key, float x, float y, float z, float qx, float qy, float qz, float qw, float sx, float sy, float sz)
    {
        if (!IM.TryGetValue(key, out var I)) return;
        var m = I.mesh; if (I.n >= m.Capacity) m.EnsureCapacity(I.n + 1);
        float x2 = qx + qx, y2 = qy + qy, z2 = qz + qz, xx = qx * x2, xy = qx * y2, xz = qx * z2, yy = qy * y2, yz = qy * z2, zz = qz * z2, wx = qw * x2, wy = qw * y2, wz = qw * z2;
        m.Matrices[I.n++] = new Matrix4x4(
            (1 - (yy + zz)) * sx, (xy + wz) * sx, (xz - wy) * sx, 0,
            (xy - wz) * sy, (1 - (xx + zz)) * sy, (yz + wx) * sy, 0,
            (xz + wy) * sz, (yz - wx) * sz, (1 - (xx + yy)) * sz, 0,
            x, y, z, 1);
    }
    void InstBegin() { foreach (var I in IM.Values) I.n = 0; }
    void InstEnd() { foreach (var I in IM.Values) { I.mesh.Count = I.n; I.mesh.Touch(); } }

    // ---------------------------------------------------------------- light flying scraps
    /// <summary>Flying pieces at once; the oldest go first (the browser shrank this under load; the setting is the cap here).</summary>
    public int CHIP_MAX => Math.Max(50, chipMaxDyn);
    int stepNo;
    // a light particle (no collisions but the ground): rubble, shed plates, chunks of membrane
    Chip AddChip(float x, float y, float z, float vx, float vy, float vz, Q4 q, float sx, float sy, float sz, string mk, float life)
    {
        var c = new Chip { pos = new V3(x, y, z), pp = new V3(x, y, z), vel = new V3(vx, vy, vz), q = q.Clone(), pq = q.Clone(), w = new V3((R() - 0.5f) * 8, (R() - 0.5f) * 8, (R() - 0.5f) * 8), r = MathF.Min(sx, MathF.Min(sy, sz)) * 0.5f, mk = mk, ell = new V3(sx, sy, sz), life = life, g = float.NaN };
        CHIPS.Add(c); while (CHIPS.Count > CHIP_MAX * 1.5f) CHIPS.RemoveAt(0);
        return c;
    }
    void StepChips(float dt)
    {
        float crowd = CHIPS.Count > CHIP_MAX ? 2 : 1;
        for (int i = CHIPS.Count - 1; i >= 0; i--)
        {
            var c = CHIPS[i]; var p = c.pos; c.life -= dt * crowd;
            if (c.life <= 0 || p.y < -50) { CHIPS.RemoveAt(i); continue; }
            c.pp.Copy(p); c.pq.Copy(c.q);
            if (c.rest > 0.4f) continue;   // settled: it just lies there until it fades
            c.vel.y -= PGRAV * dt; p.x += c.vel.x * dt; p.y += c.vel.y * dt; p.z += c.vel.z * dt;
            QIntegrate(c.q, c.w.x, c.w.y, c.w.z, dt);
            float g = ((stepNo + i) & 1) != 0 || float.IsNaN(c.g) ? (c.g = GroundAt(p.x, p.z, p.y + 0.6f, true).h) : c.g;
            if (p.y - c.r < g)
            {
                p.y = g + c.r; if (c.vel.y < 0) c.vel.y *= -0.32f; c.vel.x *= 0.72f; c.vel.z *= 0.72f; c.w.x *= 0.7f; c.w.y *= 0.7f; c.w.z *= 0.7f;
                if (MathF.Abs(c.vel.y) < 0.6f && c.vel.x * c.vel.x + c.vel.z * c.vel.z < 0.3f) c.rest += dt; else c.rest = 0;
            }
        }
    }
}

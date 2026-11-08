using System;
using System.Collections.Generic;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed class Spire { public float X, Z, H, W, Lean; }

public sealed partial class World
{
    readonly List<Spire> SPIRES = new();
    float[] heights;

    void InitTerrain()
    {
        // alien spires: needle peaks scattered away from the pads
        var r = Rng(LAYOUT.SpireSeed);
        for (int k = 0; k < 900 && SPIRES.Count < LAYOUT.Spires; k++)
        {
            float lim = LAYOUT.SpireR != 0 ? LAYOUT.SpireR : 320, x = (r() * 2 - 1) * lim, z = (r() * 2 - 1) * lim;
            bool ok = true;
            foreach (var p in PADS) if (Hypot(x - p.X, z - p.Z) < p.R + (p.Fall != 0 ? p.Fall : 22) + (LAYOUT.SpireR != 0 ? 3 : 10)) ok = false;
            if (LAYOUT.SpireR != 0 && Hypot(x, z) > LAYOUT.SpireR) ok = false;
            foreach (var c in CRATERS) if (Hypot(x - c.X, z - c.Z) < c.R + 14) ok = false;
            if (Hypot(x - MOG.X, z - MOG.Z) < MOG.R + 8) ok = false;
            foreach (var c in CHANNELS) if (ChanDist(c, x, z).D < c.W + 26) ok = false;
            foreach (var s in SPIRES) if (Hypot(x - s.X, z - s.Z) < (LAYOUT.SpireR != 0 ? 12 : 22)) ok = false;
            if (ok) SPIRES.Add(new Spire { X = x, Z = z, H = 10 + r() * 22, W = 2.4f + r() * 2.2f, Lean = r() * 6.28f });
        }
        // pads sit at the natural height of the land around them, so towns crown hills instead of sinking into pits
        foreach (var p in PADS)
        {
            float sum = 0; int n = 0;
            for (int k = 0; k < 16; k++) { float a = k / 16f * TAU; foreach (var rr in new[] { 0f, 0.6f, 1.1f }) { sum += BaseH(p.X + MathF.Cos(a) * p.R * rr, p.Z + MathF.Sin(a) * p.R * rr); n++; } }
            p.H = MathF.Round(sum / n * 0.85f + p.H * 0.15f);
        }
        heights = new float[N * N];
        // the browser built this on the main thread; here every row is generated in parallel
        System.Threading.Tasks.Parallel.For(0, N, j => { for (int i = 0; i < N; i++) heights[j * N + i] = RawHeight(-HALF + i * CELL, -HALF + j * CELL); });
    }

    float BaseH(float x, float z)
    {
        if (LAYOUT.Organic)
        {
            // folded flesh: long soft rugae ridges whose direction drifts, over slow swells and small papillae
            float X = x + LAYOUT.Ox, Z = z + LAYOUT.Oz, fl = LAYOUT.Flesh != 0 ? LAYOUT.Flesh : 1;
            float h = 6 + Fbm(X * 0.006f, Z * 0.006f, 3) * 16 * fl;
            if (LAYOUT.Flesh > 1) { float rn = 1 - MathF.Abs(VNoise(X * 0.007f + 3.3f, Z * 0.009f - 8.1f)); h += rn * rn * rn * 22; }
            float a = Fbm(X * 0.004f + 3, Z * 0.004f - 1, 2) * 6, u = (X * MathF.Cos(a) + Z * MathF.Sin(a)) * 0.11f + Fbm(X * 0.02f, Z * 0.02f, 2) * 4;
            h += MathF.Pow(MathF.Abs(MathF.Sin(u)), 0.6f) * 2.4f;
            h += VNoise(X * 0.15f, Z * 0.15f) * 0.5f;
            return h;
        }
        float A = LAYOUT.Amp; x = x * LAYOUT.Fs + LAYOUT.Ox; z = z * LAYOUT.Fs + LAYOUT.Oz;
        float hh = (Fbm(x * 0.0048f, z * 0.0048f, 4) * 30 + Fbm(x * 0.019f + 7.7f, z * 0.019f - 3.1f, 2) * 4.5f) * A + 10;
        // ridged mountain ranges: sharp crests and deep valleys
        float r1 = 1 - MathF.Abs(VNoise(x * 0.008f + 3.3f, z * 0.011f - 8.1f)); hh += r1 * r1 * r1 * 30 * A;
        float r2 = 1 - MathF.Abs(VNoise(x * 0.017f - 13.1f, z * 0.013f + 4.4f)); hh += r2 * r2 * 8 * A;
        // mesas: flat-topped plateaus behind cliffs
        float ms = VNoise(x * 0.0062f + 90, z * 0.0062f - 40); if (ms > 0.22f) hh += Smooth(0.22f, 0.3f, ms) * 20 * A;
        // sinkholes
        float sk = VNoise(x * 0.012f - 50, z * 0.012f + 70); if (sk > 0.55f) hh -= Smooth(0.55f, 0.75f, sk) * 14 * A;
        // terraces in patches
        float tm = Smooth(0.2f, 0.45f, VNoise(x * 0.005f + 40, z * 0.005f - 20));
        if (tm > 0) { const float st = 4; float q = MathF.Floor(hh / st), f = hh / st - q; hh = Lerp(hh, (q + Smooth(0.75f, 1, f)) * st, tm); }
        return hh;
    }

    float RawHeight(float x, float z)
    {
        float h = BaseH(x, z);
        foreach (var hl in LAYOUT.Hills) h += hl[2] * Gauss(x - hl[0], z - hl[1], hl[3]);
        if (LAYOUT.Bowl != null) { float d = Hypot(x, z); h += LAYOUT.Bowl.H * Smooth(LAYOUT.Bowl.R, LAYOUT.Bowl.R + 70, d); }
        foreach (var s in SPIRES)
        {
            float dx = x - s.X - MathF.Cos(s.Lean) * 0.08f * s.H, dz = z - s.Z - MathF.Sin(s.Lean) * 0.08f * s.H, d2 = dx * dx + dz * dz;
            if (d2 < 900) h += s.H * MathF.Exp(-d2 / (2 * s.W * s.W)) + s.H * 0.25f * MathF.Exp(-d2 / (2 * 36));
        }
        float md = Hypot(x - MOG.X, z - MOG.Z);
        if (md < MOG.R) h += 2.2f * MathF.Sin(x * 0.22f) * MathF.Sin(z * 0.22f) * (1 - Smooth(MOG.R * 0.6f, MOG.R, md));
        foreach (var d in CRATERS)
        {
            float rr = Hypot(x - d.X, z - d.Z); if (rr > d.R + 12) continue;
            float rim = BaseH(d.X, d.Z) + 2.5f;
            h = Lerp(rim - d.D * (1 - MathF.Min(1, (rr / d.R) * (rr / d.R))), h, Smooth(d.R, d.R + 12, rr));
        }
        foreach (var c in CHANNELS)
        {
            var q = ChanDist(c, x, z);
            if (q.D < c.W + 24) h = Lerp(c.Floor(q.S / c.L, q.S, c.L) + ChanProfile(c, q.D), h, Smooth(c.W + 5, c.W + 22, q.D));
            h = Lerp(c.Top, h, Smooth(12, 22, Hypot(x - c.Pts[0].x, z - c.Pts[0].z)));
        }
        if (LAYOUT.Organic)
        {
            // organs rise out of the floor as smooth mounds; intestines lie half buried
            foreach (var o in LAYOUT.Organs)
            {
                float dx = x - o.X, dz = z - o.Z; if (MathF.Abs(dx) > o.W * 3 || MathF.Abs(dz) > o.W * 3) continue;
                float c = MathF.Cos(o.Ang), s = MathF.Sin(o.Ang), u = dx * c + dz * s, v = -dx * s + dz * c, el = o.Kind == "lung" || o.Kind == "liver" ? 0.6f : o.Kind == "kidney" ? 0.55f : 1;
                h += o.H * MathF.Exp(-(u * u + v * v / (el * el)) / (2 * o.W * o.W * 0.45f));
            }
            float hb = h;
            foreach (var g in LAYOUT.Guts)
            {
                if (x < g.MinX || x > g.MaxX || z < g.MinZ || z > g.MaxZ) continue;
                float d = 1e9f; var P = g.Pts;
                for (int i = 0; i < P.Count - 1; i++)
                {
                    float ax = P[i].x, az = P[i].z, bx = P[i + 1].x - ax, bz = P[i + 1].z - az, t = Clamp(((x - ax) * bx + (z - az) * bz) / (bx * bx + bz * bz), 0, 1);
                    d = MathF.Min(d, Hypot(x - ax - bx * t, z - az - bz * t));
                }
                if (d < g.R) h = MathF.Max(h, hb + MathF.Sqrt(1 - (d / g.R) * (d / g.R)) * g.R * 0.85f);
            }
        }
        foreach (var p in PADS) h = Lerp(p.H, h, Smooth(p.R, p.R + (p.Fall != 0 ? p.Fall : 22), Hypot(x - p.X, z - p.Z)));
        float e = MathF.Max(MathF.Abs(x), MathF.Abs(z)) / HALF;
        if (e > 0.86f) h += ((e - 0.86f) / 0.14f) * ((e - 0.86f) / 0.14f) * 60;
        return h;
    }

    float Hc(int i, int j) { i = i < 0 ? 0 : i > SEG ? SEG : i; j = j < 0 ? 0 : j > SEG ? SEG : j; return heights[j * N + i]; }

    public struct TerrainS { public float h, gx, gz; }
    /// <summary>Height and gradient at (x, z): bicubic Catmull-Rom through the height grid.</summary>
    public TerrainS TerrainSample(float x, float z)
    {
        float gx = Clamp((x + HALF) / CELL, 0, SEG - 1e-6f), gz = Clamp((z + HALF) / CELL, 0, SEG - 1e-6f);
        int i = (int)MathF.Floor(gx), j = (int)MathF.Floor(gz); float tx = gx - i, tz = gz - j;
        Span<float> RW = stackalloc float[4], RD = stackalloc float[4];
        for (int k = 0; k < 4; k++)
        {
            float p0 = Hc(i - 1, j + k - 1), p1 = Hc(i, j + k - 1), p2 = Hc(i + 1, j + k - 1), p3 = Hc(i + 2, j + k - 1);
            RW[k] = CR(p0, p1, p2, p3, tx); RD[k] = CRD(p0, p1, p2, p3, tx);
        }
        return new TerrainS { h = CR(RW[0], RW[1], RW[2], RW[3], tz), gz = CRD(RW[0], RW[1], RW[2], RW[3], tz) / CELL, gx = CR(RD[0], RD[1], RD[2], RD[3], tz) / CELL };
    }
    public float TerrainH(float x, float z) => TerrainSample(x, z).h;
}

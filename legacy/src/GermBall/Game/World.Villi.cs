using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Core;
using GermBall.Render;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>
/// Villi: soft fingers carpeting the floor in patches. Each one is two jointed segments on springs, simulated on the CPU:
/// a slowly turning current with gusts that roll across the patches, a whip in the tips (the upper segment lags the
/// lower), bodies pushing through (harder the faster they move, squashing the villi they stand on), and blasts sending a
/// shockwave ring rippling out. Only the villi near the camera are simulated; the rest hold their last pose.
/// </summary>
public sealed partial class World
{
    const int VSEG = 5;   // segments per villus
    sealed class VilliField
    {
        public int N; public float[] x, y, z, h, s, ph;          // base, height, thickness, phase
        public float[] bx, bz, vx, vz, sq;                         // per villus × segment: bend (radians, as a vector), its speed; squash per villus
        public InstancedMesh[] seg = new InstancedMesh[VSEG];
    }
    VilliField VF;
    readonly List<(float x, float z, float t0, float str)> villiWaves = new();

    void BuildVilli(List<float[]> list, Material _)
    {
        var f = VF = new VilliField { N = list.Count };
        int n = f.N;
        f.x = new float[n]; f.y = new float[n]; f.z = new float[n]; f.h = new float[n]; f.s = new float[n]; f.ph = new float[n]; f.sq = new float[n];
        f.bx = new float[n * VSEG]; f.bz = new float[n * VSEG]; f.vx = new float[n * VSEG]; f.vz = new float[n * VSEG];
        for (int i = 0; i < n; i++) { var e = list[i]; f.x[i] = e[0]; f.y[i] = e[1]; f.z[i] = e[2]; f.h[i] = e[3] * 1.5f; f.s[i] = e[3] * 0.5f; f.ph[i] = e[4]; }
        // each segment a slightly tapered cylinder with a rounded joint on top (so the chain bends without gaps); the last one ends in a
        // soft round tip. Colour runs from a deep root to a pale, faintly glowing tip.
        uint[] cols = { 0xc04a68, 0xd85a7a, 0xec7090, 0xfa8aa6, 0xffb2c6 };
        for (int k = 0; k < VSEG; k++)
        {
            bool tip = k == VSEG - 1;
            var g = MergeGeo((Geo.Cylinder(0.86f, 1, 1, 9), Mtx(0, 0.5f, 0, 1, 1, 1)), (Geo.Sphere(tip ? 0.95f : 0.93f, 9, 6), Mtx(0, 1, 0, 1, tip ? 0.8f : 0.55f, 1)));
            var mat = new Material { Kind = MatKind.Phong, Color = C(cols[k]), Shininess = 60, Specular = C(0x553344), Emissive = tip ? C(0x401020) : Vector3.Zero };
            f.seg[k] = new InstancedMesh(g, mat, Math.Max(1, n)) { ReceiveShadow = true, Count = n };
            scene.Add(f.seg[k]);
        }
        for (int i = 0; i < n; i++) VilliMatrices(i);
        foreach (var m in f.seg) m.Touch();
    }

    /// <summary>A blast or a heavy landing: a ring that ripples out through the villi.</summary>
    public void VilliImpulse(float x, float z, float strength) { if (VF != null && villiWaves.Count < 12) villiWaves.Add((x, z, clockT, strength)); }

    static Quaternion Tilt(float bx, float bz)
    {
        float a = MathF.Sqrt(bx * bx + bz * bz); if (a < 1e-5f) return Quaternion.Identity;
        return Quaternion.CreateFromAxisAngle(new Vector3(bz / a, 0, -bx / a), a);   // leans the +Y axis toward (bx, bz)
    }

    void VilliMatrices(int i)
    {
        var f = VF; float sl = f.h[i] / VSEG * (1 - f.sq[i] * 0.35f), r = f.s[i] * 0.22f;
        var pos = new Vector3(f.x[i], f.y[i], f.z[i]); float ax = 0, az = 0;
        for (int k = 0; k < VSEG; k++)
        {
            int j = i * VSEG + k; ax += f.bx[j]; az += f.bz[j];      // each segment's lean adds to its parent's: the villus curves
            var q = Tilt(ax, az); float rk = r * (1 - k * 0.13f);
            f.seg[k].Matrices[i] = Matrix4x4.CreateScale(rk, sl, rk) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(pos);
            pos += Vector3.Transform(new Vector3(0, sl, 0), q);
        }
    }

    void StepVilli(float t, float fdt, Vector3 cam)
    {
        var f = VF; if (f == null || fdt <= 0 || !f.seg[0].Visible) return;
        float dt = MathF.Min(fdt, 1 / 30f);
        float wa = t * 0.07f + MathF.Sin(t * 0.13f) * 0.8f, wdx = MathF.Cos(wa), wdz = MathF.Sin(wa);
        villiWaves.RemoveAll(w => t - w.t0 > 2.5f);
        int n = Math.Min(f.N, f.seg[0].Count);
        for (int i = 0; i < n; i++)
        {
            float x = f.x[i], z = f.z[i];
            if (MathF.Abs(x - cam.X) + MathF.Abs(z - cam.Z) > 95) continue;
            // the current: gusts roll across the floor as travelling waves, with a little flutter on top
            float along = x * wdx + z * wdz, gust = 0.5f + 0.5f * MathF.Sin(along * 0.09f - t * 1.6f + f.ph[i] * 0.15f);
            float flick = MathF.Sin(x * 0.37f + t * 2.6f + f.ph[i]) * MathF.Sin(z * 0.29f - t * 2.1f) * 0.1f;
            float g3 = gust * gust * gust, fx = wdx * (0.05f + 0.22f * g3) + flick * wdz, fz = wdz * (0.05f + 0.22f * g3) - flick * wdx, squash = 0;
            foreach (var p in PU)
            {   // bodies pushing through
                float dx = x - p[0], dz = z - p[2], rr = p[3] * 1.35f;
                if (MathF.Abs(dx) > rr || MathF.Abs(dz) > rr || MathF.Abs(f.y[i] - (p[1] - p[3] * 0.5f)) > 3.5f) continue;
                float d = MathF.Sqrt(dx * dx + dz * dz); if (d > rr) continue;
                float k = 1 - d / rr, push = k * k * (2.2f + p[4] * 0.5f) / MathF.Max(0.15f, d);
                fx += dx * push; fz += dz * push; squash = MathF.Max(squash, k * k);
            }
            foreach (var w in villiWaves)
            {   // shockwave rings
                float dx = x - w.x, dz = z - w.z, d = MathF.Sqrt(dx * dx + dz * dz) + 1e-3f, front = (t - w.t0) * 22, band = d - front;
                if (MathF.Abs(band) > 2.5f) continue;
                float k = (1 - MathF.Abs(band) / 2.5f) * w.str * MathF.Max(0, 1 - front / 45) * 6;
                fx += dx / d * k; fz += dz / d * k;
            }
            // the chain: stiff at the root, softer toward the tip (which also feels the push more), each segment dragged late
            // by its parent's swing, so motion travels up the villus and the tip whips
            float pvx = 0, pvz = 0;
            for (int k = 0; k < VSEG; k++)
            {
                int j = i * VSEG + k; float u = k / (float)(VSEG - 1);
                float K = 34 - 20 * u, C = 3.2f, w = 0.9f + 1.1f * u;
                float ax = (fx * w * 2.4f / VSEG - f.bx[j]) * K - f.vx[j] * C - pvx * (0.6f + u * 0.4f) * K / 34;
                float az = (fz * w * 2.4f / VSEG - f.bz[j]) * K - f.vz[j] * C - pvz * (0.6f + u * 0.4f) * K / 34;
                f.vx[j] += ax * dt; f.vz[j] += az * dt; f.bx[j] += f.vx[j] * dt; f.bz[j] += f.vz[j] * dt;
                float m = MathF.Sqrt(f.bx[j] * f.bx[j] + f.bz[j] * f.bz[j]), lim = 0.42f; if (m > lim) { f.bx[j] *= lim / m; f.bz[j] *= lim / m; }
                pvx = f.vx[j]; pvz = f.vz[j];
            }
            f.sq[i] += (squash - f.sq[i]) * MathF.Min(1, dt * 10);
            VilliMatrices(i);
        }
        foreach (var m in f.seg) m.Touch();
    }
}

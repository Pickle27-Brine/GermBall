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
    sealed class VilliField
    {
        public int N; public float[] x, y, z, h, s, ph;     // base, height, thickness, phase
        public float[] b1x, b1z, v1x, v1z, b2x, b2z, v2x, v2z, sq;   // bend of each segment (radians, as a vector), velocities, squash
        public InstancedMesh lower, upper;
    }
    VilliField VF;
    readonly List<(float x, float z, float t0, float str)> villiWaves = new();

    void BuildVilli(List<float[]> list, Material mat)
    {
        var f = VF = new VilliField { N = list.Count };
        int n = f.N;
        f.x = new float[n]; f.y = new float[n]; f.z = new float[n]; f.h = new float[n]; f.s = new float[n]; f.ph = new float[n];
        f.b1x = new float[n]; f.b1z = new float[n]; f.v1x = new float[n]; f.v1z = new float[n]; f.b2x = new float[n]; f.b2z = new float[n]; f.v2x = new float[n]; f.v2z = new float[n]; f.sq = new float[n];
        for (int i = 0; i < n; i++) { var e = list[i]; f.x[i] = e[0]; f.y[i] = e[1]; f.z[i] = e[2]; f.h[i] = e[3] * 1.6f; f.s[i] = e[3] * 0.9f; f.ph[i] = e[4]; }
        // the lower segment tapers from the root; the upper ends in the soft round tip
        var lowerGeo = Geo.Cylinder(0.19f, 0.24f, 1, 7).Translate(0, 0.5f, 0);
        var upperGeo = MergeGeo((Geo.Cylinder(0.15f, 0.19f, 1, 7), Mtx(0, 0.5f, 0, 1, 1, 1)), (Geo.Sphere(0.19f, 8, 6), Mtx(0, 1, 0, 1, 0.55f, 1)));
        f.lower = new InstancedMesh(lowerGeo, mat, Math.Max(1, n)) { ReceiveShadow = true, Count = n };
        f.upper = new InstancedMesh(upperGeo, mat, Math.Max(1, n)) { ReceiveShadow = true, Count = n };
        scene.Add(f.lower); scene.Add(f.upper);
        for (int i = 0; i < n; i++) VilliMatrices(i);
        f.lower.Touch(); f.upper.Touch();
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
        var f = VF; float hl = f.h[i] * 0.5f * (1 - f.sq[i] * 0.35f), s = f.s[i];
        var q1 = Tilt(f.b1x[i], f.b1z[i]); var q2 = Tilt(f.b1x[i] + f.b2x[i], f.b1z[i] + f.b2z[i]);
        var basePos = new Vector3(f.x[i], f.y[i], f.z[i]);
        f.lower.Matrices[i] = Matrix4x4.CreateScale(s, hl, s) * Matrix4x4.CreateFromQuaternion(q1) * Matrix4x4.CreateTranslation(basePos);
        var joint = basePos + Vector3.Transform(new Vector3(0, hl, 0), q1);
        f.upper.Matrices[i] = Matrix4x4.CreateScale(s, hl, s) * Matrix4x4.CreateFromQuaternion(q2) * Matrix4x4.CreateTranslation(joint);
    }

    void StepVilli(float t, float fdt, Vector3 cam)
    {
        var f = VF; if (f == null || fdt <= 0 || !f.lower.Visible) return;
        float dt = MathF.Min(fdt, 1 / 30f);
        // the current: its direction turns slowly; gusts roll across the floor as travelling waves
        float wa = t * 0.07f + MathF.Sin(t * 0.13f) * 0.8f, wdx = MathF.Cos(wa), wdz = MathF.Sin(wa);
        villiWaves.RemoveAll(w => t - w.t0 > 2.5f);
        int n = Math.Min(f.N, f.lower.Count);
        for (int i = 0; i < n; i++)
        {
            float x = f.x[i], z = f.z[i];
            if (MathF.Abs(x - cam.X) + MathF.Abs(z - cam.Z) > 95) continue;
            float along = x * wdx + z * wdz, gust = 0.5f + 0.5f * MathF.Sin(along * 0.09f - t * 1.6f + f.ph[i] * 0.15f);
            float flick = MathF.Sin(x * 0.37f + t * 2.6f + f.ph[i]) * MathF.Sin(z * 0.29f - t * 2.1f) * 0.12f;
            float fx = wdx * (0.06f + 0.26f * gust * gust * gust) + flick * wdz, fz = wdz * (0.06f + 0.26f * gust * gust * gust) - flick * wdx;
            float squash = 0;
            // bodies pushing through
            foreach (var p in PU)
            {
                float dx = x - p[0], dz = z - p[2], rr = p[3] * 1.35f;
                if (MathF.Abs(dx) > rr || MathF.Abs(dz) > rr || MathF.Abs(f.y[i] - (p[1] - p[3] * 0.5f)) > 3.5f) continue;
                float d = MathF.Sqrt(dx * dx + dz * dz); if (d > rr) continue;
                float k = 1 - d / rr, push = k * k * (2.2f + p[4] * 0.5f) / MathF.Max(0.15f, d);
                fx += dx * push; fz += dz * push; squash = MathF.Max(squash, k * k);
            }
            // shockwave rings
            foreach (var w in villiWaves)
            {
                float dx = x - w.x, dz = z - w.z, d = MathF.Sqrt(dx * dx + dz * dz) + 1e-3f, front = (t - w.t0) * 22, band = d - front;
                if (MathF.Abs(band) > 2.5f) continue;
                float k = (1 - MathF.Abs(band) / 2.5f) * w.str * MathF.Max(0, 1 - front / 45) * 6;
                fx += dx / d * k; fz += dz / d * k;
            }
            // the lower segment: a springy root, a little underdamped so it wobbles back
            float a1x = fx * 9 - f.b1x[i] * 26 - f.v1x[i] * 3.6f, a1z = fz * 9 - f.b1z[i] * 26 - f.v1z[i] * 3.6f;
            f.v1x[i] += a1x * dt; f.v1z[i] += a1z * dt; f.b1x[i] += f.v1x[i] * dt; f.b1z[i] += f.v1z[i] * dt;
            float m1 = MathF.Sqrt(f.b1x[i] * f.b1x[i] + f.b1z[i] * f.b1z[i]); if (m1 > 1.1f) { f.b1x[i] *= 1.1f / m1; f.b1z[i] *= 1.1f / m1; }
            // the upper segment follows the lower one's motion late, so the tip whips
            float a2x = (f.b1x[i] * 0.6f + fx * 3 - f.b2x[i]) * 42 - f.v2x[i] * 4.5f - f.v1x[i] * 2.2f, a2z = (f.b1z[i] * 0.6f + fz * 3 - f.b2z[i]) * 42 - f.v2z[i] * 4.5f - f.v1z[i] * 2.2f;
            f.v2x[i] += a2x * dt; f.v2z[i] += a2z * dt; f.b2x[i] += f.v2x[i] * dt; f.b2z[i] += f.v2z[i] * dt;
            float m2 = MathF.Sqrt(f.b2x[i] * f.b2x[i] + f.b2z[i] * f.b2z[i]); if (m2 > 1.2f) { f.b2x[i] *= 1.2f / m2; f.b2z[i] *= 1.2f / m2; }
            f.sq[i] += (squash - f.sq[i]) * MathF.Min(1, dt * 10);
            VilliMatrices(i);
        }
        f.lower.Touch(); f.upper.Touch();
    }
}

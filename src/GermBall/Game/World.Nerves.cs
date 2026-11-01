using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Core;
using GermBall.Render;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>
/// Buildings are cells, and the cells are wired together like a nervous system: every building grows dendrites from its
/// roof down into the ground, ending in glowing synapse bulbs, and nearby buildings are joined by myelinated axons
/// (beaded tubes) that arch between them. Signals pulse along the nerves. All of it is rooted in the ground or the
/// buildings: nothing floats.
/// </summary>
public sealed partial class World
{
    readonly List<(Material m, Vector3 e, float ph)> nerveMats = new();
    Material NerveMat(uint col, uint glow, float ph)
    {
        var m = new Material { Kind = MatKind.Phong, Color = C(col), Emissive = C(glow) * 0.4f, Shininess = 70, Specular = C(0x664466) };
        nerveMats.Add((m, C(glow), ph)); return m;
    }
    Mesh NerveTube(IList<Vector3> pts, float rad, Material mat, int seg)
    {
        var m = new Mesh(Geo.Tube(pts, seg, rad, 7), mat) { CastShadow = true, ReceiveShadow = true }; scene.Add(m); return m;
    }

    /// <summary>Collision for the parts of a nerve near the ground, so nothing you can walk into is a ghost.</summary>
    void SolidNerve(IList<Vector3> pts, float rad)
    {
        for (int i = 0; i <= 16; i++)
        {
            var q = Geo.CatmullRom(pts, i / 16f, false); float g = GroundY(q.X, q.Z);
            if (q.Y - rad - g < 2.2f && !InsideBuilding(q.X, q.Z, 0)) AddStatic(new Static { Kind = CYLK, X = q.X, Z = q.Z, R = rad + 0.12f, Bottom = g - 1, Top = q.Y + rad });
        }
    }
    void BuildNerves()
    {
        var r = Rng(4242);
        var dend = NerveMat(0xd8a8ff, 0xb070ff, 0); var axon = NerveMat(0xf2d8ff, 0x7affef, 1.7f); var myelin = NerveMat(0xfff4e0, 0xffd27a, 0.6f);
        var bulbMat = NerveMat(0xffb0e8, 0xff6ad5, 2.4f);
        var bulbs = new List<Matrix4x4>(); var beads = new List<Matrix4x4>();
        foreach (var B in BLDG)
        {
            float top = B.y + B.h + 0.3f, hw = B.w / 2, hd = B.d / 2;
            // dendrites: from the roof corners and edges, curving down into the ground, branching once
            int n = 3 + (int)(r() * 3);
            for (int k = 0; k < n; k++)
            {
                float a = r() * TAU, sx = B.x + MathF.Cos(a) * hw * 0.85f, sz = B.z + MathF.Sin(a) * hd * 0.85f;
                float reach = 6 + r() * 10, ex = sx + MathF.Cos(a) * reach, ez = sz + MathF.Sin(a) * reach, ey = GroundY(ex, ez);
                if (InsideBuilding(ex, ez, 1.5f) || PADS.Any(p => p.Kind == "base" && Hypot(ex - p.X, ez - p.Z) < p.R)) continue;   // never into another building or the base
                var pts = new List<Vector3> { new(sx, top - 0.4f, sz), new(sx + MathF.Cos(a) * reach * 0.3f, top + 1.2f, sz + MathF.Sin(a) * reach * 0.3f), new(sx + MathF.Cos(a) * reach * 0.7f, (top + ey) * 0.5f, sz + MathF.Sin(a) * reach * 0.7f), new(ex, ey + 0.2f, ez) };
                NerveTube(pts, 0.28f, dend, 22); SolidNerve(pts, 0.28f);
                bulbs.Add(Matrix4x4.CreateScale(0.7f) * Matrix4x4.CreateTranslation(ex, ey + 0.45f, ez));
                AddStatic(new Static { Kind = CYLK, X = ex, Z = ez, R = 0.75f, Bottom = ey - 1, Top = ey + 1.15f });
                // a branch off it
                var mid = pts[2]; float b = a + (r() < 0.5f ? 0.9f : -0.9f), bl = 4 + r() * 5, bx = mid.X + MathF.Cos(b) * bl, bz = mid.Z + MathF.Sin(b) * bl, by = GroundY(bx, bz);
                if (InsideBuilding(bx, bz, 1.5f)) continue;
                var br = new List<Vector3> { mid, new((mid.X + bx) / 2, (mid.Y + by) / 2 + 1, (mid.Z + bz) / 2), new(bx, by + 0.15f, bz) };
                NerveTube(br, 0.16f, dend, 12); SolidNerve(br, 0.16f);
                bulbs.Add(Matrix4x4.CreateScale(0.45f) * Matrix4x4.CreateTranslation(bx, by + 0.3f, bz));
                AddStatic(new Static { Kind = CYLK, X = bx, Z = bz, R = 0.5f, Bottom = by - 1, Top = by + 0.75f });
            }
        }
        // axons between neighbouring cells: an arch from roof to roof, beaded with myelin
        for (int i = 0; i < BLDG.Count; i++)
        {
            int links = 0;
            for (int j = i + 1; j < BLDG.Count && links < 2; j++)
            {
                var A = BLDG[i]; var Bb = BLDG[j]; float d = Hypot(A.x - Bb.x, A.z - Bb.z);
                if (d < 12 || d > 46) continue;
                links++;
                var p0 = new Vector3(A.x, A.y + A.h + 0.2f, A.z); var p1 = new Vector3(Bb.x, Bb.y + Bb.h + 0.2f, Bb.z);
                float lift = 3 + d * 0.12f; var m = (p0 + p1) * 0.5f + new Vector3(0, lift, 0);
                var pts = new List<Vector3> { p0, Vector3.Lerp(p0, m, 0.5f) + new Vector3(0, lift * 0.3f, 0), m, Vector3.Lerp(m, p1, 0.5f) + new Vector3(0, lift * 0.3f, 0), p1 };
                NerveTube(pts, 0.22f, axon, 36);
                // myelin sheath beads along it
                for (int k = 1; k < 9; k++)
                {
                    float u = k / 9f; var q = Geo.CatmullRom(pts, u, false); var q2 = Geo.CatmullRom(pts, MathF.Min(1, u + 0.01f), false);
                    var dir = Vector3.Normalize(q2 - q); var qq = new Q4(); QFromY(dir.X, dir.Y, dir.Z, qq);
                    beads.Add(Matrix4x4.CreateScale(0.42f, 0.9f, 0.42f) * Matrix4x4.CreateFromQuaternion(new Quaternion(qq.x, qq.y, qq.z, qq.w)) * Matrix4x4.CreateTranslation(q));
                }
            }
        }
        if (bulbs.Count > 0) { var im = new InstancedMesh(Geo.Sphere(1, 12, 9), bulbMat, bulbs.Count) { CastShadow = true }; for (int i = 0; i < bulbs.Count; i++) im.Matrices[i] = bulbs[i]; im.Touch(); scene.Add(im); }
        if (beads.Count > 0) { var im = new InstancedMesh(Geo.Sphere(1, 10, 8), myelin, beads.Count) { CastShadow = true }; for (int i = 0; i < beads.Count; i++) im.Matrices[i] = beads[i]; im.Touch(); scene.Add(im); }
    }

    /// <summary>Signals pulsing through the nerves.</summary>
    void UpdateNerves(float t)
    {
        foreach (var (m, e, ph) in nerveMats) { float k = 0.25f + 0.75f * MathF.Pow(0.5f + 0.5f * MathF.Sin(t * 2.6f + ph), 6); m.Emissive = e * k; }
    }
}

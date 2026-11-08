using System;
using System.Collections.Generic;
using System.Numerics;

namespace GermBall.Engine;

/// <summary>The built-in shapes. Every one is unit-sized and centred on the origin: the cube is 1 m on a side,
/// the sphere and cylinder are 1 m across and 1 m tall. Draw items scale them.</summary>
public enum MeshId { Cube, Sphere, Cylinder, Cone, BallStriped, Count }

public sealed class Mesh
{
    public readonly List<Vector3> Pos = new(), Nrm = new(), Col = new();
    public readonly List<uint> Idx = new();

    uint V(Vector3 p, Vector3 n, Vector3 c) { Pos.Add(p); Nrm.Add(n); Col.Add(c); return (uint)(Pos.Count - 1); }
    void Tri(uint a, uint b, uint c) { Idx.Add(a); Idx.Add(b); Idx.Add(c); }

    static readonly Mesh[] all = new Mesh[(int)MeshId.Count];
    public static Mesh Get(MeshId id) => all[(int)id] ??= Build(id);

    static Mesh Build(MeshId id) => id switch
    {
        MeshId.Cube => Cube(),
        MeshId.Sphere => Sphere(16, 24, false),
        MeshId.Cylinder => Cylinder(20, false),
        MeshId.Cone => Cylinder(20, true),
        MeshId.BallStriped => Sphere(16, 24, true),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    static Mesh Cube()
    {
        var m = new Mesh(); var w = Vector3.One;
        Vector3[] n = { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
        foreach (var f in n)
        {
            // two axes spanning the face, chosen so (u × v) = f (counter-clockwise seen from outside)
            var u = MathF.Abs(f.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var v = Vector3.Cross(f, u);
            var c = f * 0.5f;
            uint a = m.V(c - u * 0.5f - v * 0.5f, f, w), b = m.V(c + u * 0.5f - v * 0.5f, f, w),
                 cc = m.V(c + u * 0.5f + v * 0.5f, f, w), d = m.V(c - u * 0.5f + v * 0.5f, f, w);
            m.Tri(a, b, cc); m.Tri(a, cc, d);
        }
        return m;
    }

    static Mesh Sphere(int rings, int segs, bool striped)
    {
        var m = new Mesh();
        for (int r = 0; r <= rings; r++)
        {
            float phi = MathF.PI * r / rings; float y = MathF.Cos(phi), s = MathF.Sin(phi);
            for (int k = 0; k <= segs; k++)
            {
                float th = 2 * MathF.PI * k / segs;
                var n = new Vector3(s * MathF.Cos(th), y, s * MathF.Sin(th));
                var col = Vector3.One;
                if (striped && (MathF.Abs(y) < 0.18f || (k % (segs / 2)) == 0 || (k % (segs / 2)) == 1)) col = new Vector3(0.25f, 0.25f, 0.3f);
                m.V(n * 0.5f, n, col);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int k = 0; k < segs; k++)
            {
                uint a = (uint)(r * (segs + 1) + k), b = a + (uint)(segs + 1);
                m.Tri(a, a + 1, b); m.Tri(a + 1, b + 1, b);
            }
        return m;
    }

    static Mesh Cylinder(int segs, bool cone)
    {
        var m = new Mesh(); var w = Vector3.One;
        float slope = cone ? 1f : 0f;   // the cone's side normals tilt up (radius 0.5 over height 1)
        for (int k = 0; k < segs; k++)
        {
            float a0 = 2 * MathF.PI * k / segs, a1 = 2 * MathF.PI * (k + 1) / segs;
            var d0 = new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0)); var d1 = new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1));
            var n0 = Vector3.Normalize(d0 + Vector3.UnitY * slope * 0.5f); var n1 = Vector3.Normalize(d1 + Vector3.UnitY * slope * 0.5f);
            float topR = cone ? 0.001f : 0.5f;
            uint a = m.V(d0 * 0.5f - Vector3.UnitY * 0.5f, n0, w), b = m.V(d1 * 0.5f - Vector3.UnitY * 0.5f, n1, w),
                 c = m.V(d1 * topR + Vector3.UnitY * 0.5f, n1, w), d = m.V(d0 * topR + Vector3.UnitY * 0.5f, n0, w);
            m.Tri(a, c, b); m.Tri(a, d, c);
            // caps
            uint cb = m.V(-Vector3.UnitY * 0.5f, -Vector3.UnitY, w), b0 = m.V(d0 * 0.5f - Vector3.UnitY * 0.5f, -Vector3.UnitY, w), b1 = m.V(d1 * 0.5f - Vector3.UnitY * 0.5f, -Vector3.UnitY, w);
            m.Tri(cb, b0, b1);
            if (!cone)
            {
                uint ct = m.V(Vector3.UnitY * 0.5f, Vector3.UnitY, w), t0 = m.V(d0 * 0.5f + Vector3.UnitY * 0.5f, Vector3.UnitY, w), t1 = m.V(d1 * 0.5f + Vector3.UnitY * 0.5f, Vector3.UnitY, w);
                m.Tri(ct, t1, t0);
            }
        }
        return m;
    }
}

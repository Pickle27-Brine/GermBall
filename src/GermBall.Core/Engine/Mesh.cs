using System;
using System.Collections.Generic;
using System.Numerics;

namespace GermBall.Engine;

/// <summary>The built-in shapes. Every one is unit-sized and centred on the origin: the cube is 1 m on a side,
/// the sphere and cylinder are 1 m across and 1 m tall. Draw items scale them.</summary>
public enum MeshId { Cube, Sphere, Cylinder, Cone, BallStriped, Count }

public sealed class Mesh
{
    /// <summary>Bumped whenever the vertices change (renderers re-upload).</summary>
    public int Version;
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

/// <summary>
/// Shapes with a taper (one end narrower) and a curve (bent along their length, toward +X), built at their final size.
/// Cached by shape, size, taper and curve, so dragging a slider only builds a few meshes.
/// </summary>
public static class DeformedMeshes
{
    static readonly Dictionary<string, Mesh> cache = new();

    /// <summary><paramref name="type"/>: Box, Sphere, Capsule, Cylinder, Cone or Ball. Size is the full size (Y is the length).
    /// Taper -1..1: positive narrows the top (+Y) end, negative the bottom. Curve -1..1: how far the top end bends sideways, in lengths.</summary>
    public static Mesh Get(string type, Vector3 size, float taper, float curve)
    {
        size = new Vector3(Q(size.X, 0.005f), Q(size.Y, 0.005f), Q(size.Z, 0.005f));
        taper = Math.Clamp(Q(taper, 0.01f), -1, 1); curve = Math.Clamp(Q(curve, 0.01f), -2, 2);
        string key = $"{type}|{size.X}|{size.Y}|{size.Z}|{taper}|{curve}";
        if (cache.TryGetValue(key, out var m)) return m;
        if (cache.Count > 600) cache.Clear();
        m = Build(type, size, taper, curve);
        cache[key] = m;
        return m;
    }
    static float Q(float v, float step) => MathF.Round(v / step) * step;

    static Mesh Build(string type, Vector3 size, float taper, float curve)
    {
        var m = type switch
        {
            "Box" => Box(size),
            "Sphere" or "Ball" => Lathe(size, 24, 20, y => MathF.Sqrt(MathF.Max(0, 1 - y * y * 4)), true, type == "Ball"),
            "Cylinder" => Lathe(size, 24, 12, y => 1, false, false),
            "Cone" => Lathe(size, 24, 12, y => MathF.Max(0.004f, 0.5f - y), false, false),
            "Capsule" => Capsule(size),
            _ => Box(size),
        };
        // taper and bend, measured along the length
        float ymin = -size.Y / 2, len = MathF.Max(1e-4f, size.Y);
        float top = 1 - MathF.Max(0, taper), bottom = 1 + MathF.Min(0, taper);
        for (int i = 0; i < m.Pos.Count; i++)
        {
            var p = m.Pos[i]; float t = (p.Y - ymin) / len;
            float k = bottom + (top - bottom) * t;
            p.X *= k; p.Z *= k;
            p.X += curve * len * t * t;
            m.Pos[i] = p;
        }
        RecomputeNormals(m);
        return m;
    }

    static void RecomputeNormals(Mesh m)
    {
        var n = new Vector3[m.Pos.Count];
        for (int i = 0; i < m.Idx.Count; i += 3)
        {
            int a = (int)m.Idx[i], b = (int)m.Idx[i + 1], c = (int)m.Idx[i + 2];
            var f = Vector3.Cross(m.Pos[b] - m.Pos[a], m.Pos[c] - m.Pos[a]);
            n[a] += f; n[b] += f; n[c] += f;
        }
        // keep normals pointing the way the original ones did (winding differs between generators)
        for (int i = 0; i < n.Length; i++)
        {
            var v = n[i].LengthSquared() > 1e-12f ? Vector3.Normalize(n[i]) : m.Nrm[i];
            if (Vector3.Dot(v, m.Nrm[i]) < 0) v = -v;
            m.Nrm[i] = v;
        }
    }

    /// <summary>A box with every side cut into strips along Y, so it can bend.</summary>
    static Mesh Box(Vector3 s)
    {
        var m = new Mesh(); const int seg = 10; var h = s / 2;
        void Face(Vector3 n, Vector3 u, Vector3 v)   // u across, v along Y (or across for the caps)
        {
            int baseI = m.Pos.Count; bool alongY = MathF.Abs(n.Y) < 0.5f; int rows = alongY ? seg : 1;
            for (int r = 0; r <= rows; r++)
                for (int c = 0; c <= 1; c++)
                {
                    float fu = c * 2 - 1, fv = r / (float)rows * 2 - 1;
                    var p = n * h + u * h * fu + v * h * fv;
                    m.Pos.Add(p); m.Nrm.Add(n); m.Col.Add(Vector3.One);
                }
            for (int r = 0; r < rows; r++)
            {
                uint a = (uint)(baseI + r * 2), b = a + 1, c2 = a + 2, d = a + 3;
                m.Idx.Add(a); m.Idx.Add(b); m.Idx.Add(d); m.Idx.Add(a); m.Idx.Add(d); m.Idx.Add(c2);
            }
        }
        Face(Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY); Face(-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY);
        Face(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY); Face(-Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
        Face(Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ); Face(-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);
        return m;
    }

    /// <summary>A surface of revolution around Y: radius(y) for y in -0.5..0.5 (radius 0.5 = full width), with caps if open.</summary>
    static Mesh Lathe(Vector3 s, int segs, int rings, Func<float, float> radius, bool closed, bool striped)
    {
        var m = new Mesh();
        for (int r = 0; r <= rings; r++)
        {
            float y = closed ? -0.5f + (1 - MathF.Cos(MathF.PI * r / rings)) / 2 : -0.5f + r / (float)rings;
            float rad = radius(y) * 0.5f;   // radius() is 1 at full width
            for (int k = 0; k <= segs; k++)
            {
                float a = MathF.Tau * k / segs;
                var p = new Vector3(MathF.Cos(a) * rad * s.X, y * s.Y, MathF.Sin(a) * rad * s.Z);
                var n = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
                if (closed) n = Vector3.Normalize(new Vector3(MathF.Cos(a) * rad, y, MathF.Sin(a) * rad));
                m.Pos.Add(p); m.Nrm.Add(n);
                bool dark = striped && (MathF.Abs(y) < 0.09f || (k % (segs / 2)) == 0 || (k % (segs / 2)) == 1);
                m.Col.Add(dark ? new Vector3(0.25f, 0.25f, 0.3f) : Vector3.One);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int k = 0; k < segs; k++)
            {
                uint a = (uint)(r * (segs + 1) + k), b = a + (uint)(segs + 1);
                m.Idx.Add(a); m.Idx.Add(b); m.Idx.Add(a + 1); m.Idx.Add(a + 1); m.Idx.Add(b); m.Idx.Add(b + 1);
            }
        if (!closed)
        {
            foreach (float y in new[] { -0.5f, 0.5f })
            {
                float rad = radius(y) * 0.5f;
                if (rad < 0.004f) continue;
                var n = new Vector3(0, MathF.Sign(y), 0);
                uint c = (uint)m.Pos.Count; m.Pos.Add(new Vector3(0, y * s.Y, 0)); m.Nrm.Add(n); m.Col.Add(Vector3.One);
                for (int k = 0; k <= segs; k++)
                {
                    float a = MathF.Tau * k / segs;
                    m.Pos.Add(new Vector3(MathF.Cos(a) * rad * s.X, y * s.Y, MathF.Sin(a) * rad * s.Z)); m.Nrm.Add(n); m.Col.Add(Vector3.One);
                }
                for (int k = 0; k < segs; k++) { m.Idx.Add(c); m.Idx.Add(c + 1 + (uint)k); m.Idx.Add(c + 2 + (uint)k); }
            }
        }
        return m;
    }

    /// <summary>A capsule as one smooth surface: hemispheres of radius size.X/2 on a cylinder, total length size.Y.</summary>
    static Mesh Capsule(Vector3 s)
    {
        float r = s.X / 2, half = MathF.Max(s.Y / 2, r), seg = half - r;
        var m = new Mesh(); const int segs = 24, capRings = 8, bodyRings = 10;
        var profile = new List<(float y, float rad)>();
        for (int i = 0; i <= capRings; i++) { float a = -MathF.PI / 2 + MathF.PI / 2 * i / capRings; profile.Add((-seg + MathF.Sin(a) * r, MathF.Cos(a))); }
        for (int i = 1; i < bodyRings; i++) profile.Add((-seg + 2 * seg * i / bodyRings, 1));
        for (int i = 0; i <= capRings; i++) { float a = MathF.PI / 2 * i / capRings; profile.Add((seg + MathF.Sin(a) * r, MathF.Cos(a))); }
        foreach (var (y, rad) in profile)
            for (int k = 0; k <= segs; k++)
            {
                float a = MathF.Tau * k / segs;
                m.Pos.Add(new Vector3(MathF.Cos(a) * rad * s.X / 2, y, MathF.Sin(a) * rad * s.Z / 2));
                float ny = MathF.Abs(y) > seg ? (y - MathF.Sign(y) * seg) / MathF.Max(r, 1e-4f) : 0;
                m.Nrm.Add(Vector3.Normalize(new Vector3(MathF.Cos(a) * rad, ny, MathF.Sin(a) * rad) + new Vector3(0, 1e-5f, 0)));
                m.Col.Add(Vector3.One);
            }
        int rows = profile.Count - 1;
        for (int i = 0; i < rows; i++)
            for (int k = 0; k < segs; k++)
            {
                uint a = (uint)(i * (segs + 1) + k), b = a + (uint)(segs + 1);
                m.Idx.Add(a); m.Idx.Add(b); m.Idx.Add(a + 1); m.Idx.Add(a + 1); m.Idx.Add(b); m.Idx.Add(b + 1);
            }
        return m;
    }
}

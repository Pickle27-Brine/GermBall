using System;
using System.Collections.Generic;
using System.Numerics;

namespace GermBall.Render;

/// <summary>Primitive builders with the same parameters, orientation and UV layout as the three.js ones the game was written against.</summary>
public static class Geo
{
    static Geometry Make(List<float> p, List<float> n, List<float> uv, List<uint> idx)
        => new() { Position = p.ToArray(), Normal = n.ToArray(), Uv = uv?.ToArray(), Index = idx?.ToArray() };

    public static Geometry Box(float w = 1, float h = 1, float d = 1)
    {
        var p = new List<float>(); var n = new List<float>(); var uv = new List<float>(); var idx = new List<uint>();
        void Face(Vector3 o, Vector3 u, Vector3 v, Vector3 nn)
        {
            uint b = (uint)(p.Count / 3);
            for (int j = 0; j <= 1; j++) for (int i = 0; i <= 1; i++)
                {
                    var q = o + u * (i - 0.5f) * 2 + v * (0.5f - j) * 2;
                    p.Add(q.X); p.Add(q.Y); p.Add(q.Z); n.Add(nn.X); n.Add(nn.Y); n.Add(nn.Z); uv.Add(i); uv.Add(1 - j);
                }
            idx.Add(b); idx.Add(b + 2); idx.Add(b + 1); idx.Add(b + 2); idx.Add(b + 3); idx.Add(b + 1);
        }
        float x = w / 2, y = h / 2, z = d / 2;
        Face(new(x, 0, 0), new(0, 0, -z), new(0, y, 0), Vector3.UnitX);
        Face(new(-x, 0, 0), new(0, 0, z), new(0, y, 0), -Vector3.UnitX);
        Face(new(0, y, 0), new(x, 0, 0), new(0, 0, -z), Vector3.UnitY);
        Face(new(0, -y, 0), new(x, 0, 0), new(0, 0, z), -Vector3.UnitY);
        Face(new(0, 0, z), new(x, 0, 0), new(0, y, 0), Vector3.UnitZ);
        Face(new(0, 0, -z), new(-x, 0, 0), new(0, y, 0), -Vector3.UnitZ);
        return Make(p, n, uv, idx);
    }

    public static Geometry Sphere(float r = 1, int ws = 32, int hs = 16, float phiStart = 0, float phiLen = MathF.PI * 2, float thetaStart = 0, float thetaLen = MathF.PI)
    {
        ws = Math.Max(3, ws); hs = Math.Max(2, hs);
        var p = new List<float>(); var n = new List<float>(); var uv = new List<float>(); var idx = new List<uint>();
        float thetaEnd = MathF.Min(thetaStart + thetaLen, MathF.PI);
        var grid = new uint[hs + 1, ws + 1]; uint k = 0;
        for (int iy = 0; iy <= hs; iy++)
        {
            float v = (float)iy / hs, uOff = iy == 0 && thetaStart == 0 ? 0.5f / ws : iy == hs && thetaEnd == MathF.PI ? -0.5f / ws : 0;
            for (int ix = 0; ix <= ws; ix++)
            {
                float u = (float)ix / ws;
                float x = -r * MathF.Cos(phiStart + u * phiLen) * MathF.Sin(thetaStart + v * thetaLen), y = r * MathF.Cos(thetaStart + v * thetaLen), z = r * MathF.Sin(phiStart + u * phiLen) * MathF.Sin(thetaStart + v * thetaLen);
                p.Add(x); p.Add(y); p.Add(z); var nn = Vector3.Normalize(new Vector3(x, y, z)); n.Add(nn.X); n.Add(nn.Y); n.Add(nn.Z); uv.Add(u + uOff); uv.Add(1 - v);
                grid[iy, ix] = k++;
            }
        }
        for (int iy = 0; iy < hs; iy++) for (int ix = 0; ix < ws; ix++)
            {
                uint a = grid[iy, ix + 1], b = grid[iy, ix], c = grid[iy + 1, ix], d = grid[iy + 1, ix + 1];
                if (iy != 0 || thetaStart > 0) { idx.Add(a); idx.Add(b); idx.Add(d); }
                if (iy != hs - 1 || thetaEnd < MathF.PI) { idx.Add(b); idx.Add(c); idx.Add(d); }
            }
        return Make(p, n, uv, idx);
    }

    /// <summary>Polyhedron subdivided like three.js (flat faces, non-indexed).</summary>
    static Geometry Polyhedron(float[] verts, int[] faces, float r, int detail)
    {
        var tris = new List<Vector3>();
        Vector3 V(int i) => new(verts[i * 3], verts[i * 3 + 1], verts[i * 3 + 2]);
        for (int f = 0; f < faces.Length; f += 3)
        {
            Vector3 a = V(faces[f]), b = V(faces[f + 1]), c = V(faces[f + 2]); int cols = detail + 1;
            var g = new Vector3[cols + 1][];
            for (int i = 0; i <= cols; i++)
            {
                var aj = Vector3.Lerp(a, c, (float)i / cols); var bj = Vector3.Lerp(b, c, (float)i / cols); int rows = cols - i;
                g[i] = new Vector3[rows + 1];
                for (int j = 0; j <= rows; j++) g[i][j] = j == 0 && i == cols ? aj : Vector3.Lerp(aj, bj, rows == 0 ? 0 : (float)j / rows);
            }
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < 2 * (cols - i) - 1; j++)
                {
                    int kk = j / 2;
                    if (j % 2 == 0) { tris.Add(g[i][kk + 1]); tris.Add(g[i + 1][kk]); tris.Add(g[i][kk]); }
                    else { tris.Add(g[i][kk + 1]); tris.Add(g[i + 1][kk + 1]); tris.Add(g[i + 1][kk]); }
                }
        }
        var geo = new Geometry { Position = new float[tris.Count * 3], Normal = new float[tris.Count * 3], Uv = new float[tris.Count * 2] };
        for (int i = 0; i < tris.Count; i++)
        {
            var q = Vector3.Normalize(tris[i]) * r; geo.Position[i * 3] = q.X; geo.Position[i * 3 + 1] = q.Y; geo.Position[i * 3 + 2] = q.Z;
            geo.Uv[i * 2] = MathF.Atan2(q.Z, -q.X) / (2 * MathF.PI) + 0.5f; geo.Uv[i * 2 + 1] = MathF.Asin(Math.Clamp(q.Y / r, -1, 1)) / MathF.PI + 0.5f;
        }
        // three's polyhedra with detail 0 are faceted; with more detail they get smooth normals
        if (detail == 0) return geo.Flat();
        for (int i = 0; i < tris.Count; i++) { var q = Vector3.Normalize(geo.P(i)); geo.Normal[i * 3] = q.X; geo.Normal[i * 3 + 1] = q.Y; geo.Normal[i * 3 + 2] = q.Z; }
        return geo;
    }
    public static Geometry Icosahedron(float r = 1, int detail = 0)
    {
        float t = (1 + MathF.Sqrt(5)) / 2;
        float[] v = { -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, 0, 0, -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, t, 0, -1, t, 0, 1, -t, 0, -1, -t, 0, 1 };
        int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        return Polyhedron(v, f, r, detail);
    }
    public static Geometry Octahedron(float r = 1, int detail = 0)
    {
        float[] v = { 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1 };
        int[] f = { 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 2, 5, 1, 5, 3, 1, 3, 4, 1, 4, 2 };
        return Polyhedron(v, f, r, detail);
    }
    public static Geometry Dodecahedron(float r = 1, int detail = 0)
    {
        float t = (1 + MathF.Sqrt(5)) / 2, q = 1 / t;
        float[] v = { -1, -1, -1, -1, -1, 1, -1, 1, -1, -1, 1, 1, 1, -1, -1, 1, -1, 1, 1, 1, -1, 1, 1, 1, 0, -q, -t, 0, -q, t, 0, q, -t, 0, q, t, -q, -t, 0, -q, t, 0, q, -t, 0, q, t, 0, -t, 0, -q, t, 0, -q, -t, 0, q, t, 0, q };
        int[] f = { 3, 11, 7, 3, 7, 15, 3, 15, 13, 7, 19, 17, 7, 17, 6, 7, 6, 15, 17, 4, 8, 17, 8, 10, 17, 10, 6, 8, 0, 16, 8, 16, 2, 8, 2, 10, 0, 12, 1, 0, 1, 18, 0, 18, 16, 6, 10, 2, 6, 2, 13, 6, 13, 15, 2, 16, 18, 2, 18, 3, 2, 3, 13, 18, 1, 9, 18, 9, 11, 18, 11, 3, 4, 14, 12, 4, 12, 0, 4, 0, 8, 11, 9, 5, 11, 5, 19, 11, 19, 7, 19, 5, 14, 19, 14, 4, 19, 4, 17, 1, 12, 14, 1, 14, 5, 1, 5, 9 };
        return Polyhedron(v, f, r, detail);
    }

    public static Geometry Cylinder(float rt = 1, float rb = 1, float h = 1, int radial = 8, int hseg = 1, bool open = false, float thetaStart = 0, float thetaLen = MathF.PI * 2)
    {
        var p = new List<float>(); var n = new List<float>(); var uv = new List<float>(); var idx = new List<uint>();
        float half = h / 2, slope = (rb - rt) / h; var grid = new uint[hseg + 1, radial + 1]; uint k = 0;
        for (int y = 0; y <= hseg; y++)
        {
            float v = (float)y / hseg, rr = v * (rb - rt) + rt;
            for (int x = 0; x <= radial; x++)
            {
                float u = (float)x / radial, th = u * thetaLen + thetaStart, s = MathF.Sin(th), c = MathF.Cos(th);
                p.Add(rr * s); p.Add(-v * h + half); p.Add(rr * c);
                var nn = Vector3.Normalize(new Vector3(s, slope, c)); n.Add(nn.X); n.Add(nn.Y); n.Add(nn.Z); uv.Add(u); uv.Add(1 - v);
                grid[y, x] = k++;
            }
        }
        for (int x = 0; x < radial; x++) for (int y = 0; y < hseg; y++)
            {
                uint a = grid[y, x], b = grid[y + 1, x], c = grid[y + 1, x + 1], d = grid[y, x + 1];
                idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(b); idx.Add(c); idx.Add(d);
            }
        if (!open)
        {
            void Cap(bool top)
            {
                float rr = top ? rt : rb, sign = top ? 1 : -1; if (rr <= 0) return;
                uint centerStart = k;
                for (int x = 1; x <= radial; x++) { p.Add(0); p.Add(half * sign); p.Add(0); n.Add(0); n.Add(sign); n.Add(0); uv.Add(0.5f); uv.Add(0.5f); k++; }
                uint centerEnd = k;
                for (int x = 0; x <= radial; x++)
                {
                    float u = (float)x / radial, th = u * thetaLen + thetaStart, c = MathF.Cos(th), s = MathF.Sin(th);
                    p.Add(rr * s); p.Add(half * sign); p.Add(rr * c); n.Add(0); n.Add(sign); n.Add(0); uv.Add(c * 0.5f + 0.5f); uv.Add(s * 0.5f * sign + 0.5f); k++;
                }
                for (int x = 0; x < radial; x++)
                {
                    uint c = centerStart + (uint)x, i = centerEnd + (uint)x;
                    if (top) { idx.Add(i); idx.Add(i + 1); idx.Add(c); } else { idx.Add(i + 1); idx.Add(i); idx.Add(c); }
                }
            }
            Cap(true); Cap(false);
        }
        return Make(p, n, uv, idx);
    }
    public static Geometry Cone(float r = 1, float h = 1, int radial = 8, int hseg = 1, bool open = false) => Cylinder(0, r, h, radial, hseg, open);

    public static Geometry Plane(float w = 1, float h = 1, int ws = 1, int hs = 1)
    {
        var p = new List<float>(); var n = new List<float>(); var uv = new List<float>(); var idx = new List<uint>();
        for (int iy = 0; iy <= hs; iy++) for (int ix = 0; ix <= ws; ix++)
            { p.Add(ix * w / ws - w / 2); p.Add(-(iy * h / hs - h / 2)); p.Add(0); n.Add(0); n.Add(0); n.Add(1); uv.Add((float)ix / ws); uv.Add(1 - (float)iy / hs); }
        for (int iy = 0; iy < hs; iy++) for (int ix = 0; ix < ws; ix++)
            {
                uint a = (uint)(ix + (ws + 1) * iy), b = (uint)(ix + (ws + 1) * (iy + 1)), c = (uint)(ix + 1 + (ws + 1) * (iy + 1)), d = (uint)(ix + 1 + (ws + 1) * iy);
                idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(b); idx.Add(c); idx.Add(d);
            }
        return Make(p, n, uv, idx);
    }

    public static Geometry Circle(float r = 1, int segs = 8, float thetaStart = 0, float thetaLen = MathF.PI * 2)
    {
        var p = new List<float> { 0, 0, 0 }; var n = new List<float> { 0, 0, 1 }; var uv = new List<float> { 0.5f, 0.5f }; var idx = new List<uint>();
        for (int s = 0; s <= segs; s++)
        {
            float a = thetaStart + (float)s / segs * thetaLen; p.Add(r * MathF.Cos(a)); p.Add(r * MathF.Sin(a)); p.Add(0); n.Add(0); n.Add(0); n.Add(1);
            uv.Add((MathF.Cos(a) + 1) / 2); uv.Add((MathF.Sin(a) + 1) / 2);
        }
        for (uint i = 1; i <= segs; i++) { idx.Add(i); idx.Add(i + 1); idx.Add(0); }
        return Make(p, n, uv, idx);
    }

    public static Geometry Ring(float inner = 0.5f, float outer = 1, int segs = 32, int phiSegs = 1, float thetaStart = 0, float thetaLen = MathF.PI * 2)
    {
        var p = new List<float>(); var n = new List<float>(); var uv = new List<float>(); var idx = new List<uint>();
        float rr = inner, step = (outer - inner) / phiSegs;
        for (int j = 0; j <= phiSegs; j++)
        {
            for (int i = 0; i <= segs; i++)
            {
                float a = thetaStart + (float)i / segs * thetaLen; float x = rr * MathF.Cos(a), y = rr * MathF.Sin(a);
                p.Add(x); p.Add(y); p.Add(0); n.Add(0); n.Add(0); n.Add(1); uv.Add((x / outer + 1) / 2); uv.Add((y / outer + 1) / 2);
            }
            rr += step;
        }
        for (int j = 0; j < phiSegs; j++)
        {
            int ts = j * (segs + 1);
            for (int i = 0; i < segs; i++)
            {
                uint a = (uint)(ts + i), b = (uint)(ts + i + segs + 1), c = (uint)(ts + i + segs + 2), d = (uint)(ts + i + 1);
                idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(b); idx.Add(c); idx.Add(d);
            }
        }
        return Make(p, n, uv, idx);
    }

    public static Geometry Torus(float r = 1, float tube = 0.4f, int radial = 8, int tubular = 6, float arc = MathF.PI * 2)
    {
        var p = new List<float>(); var n = new List<float>(); var uv = new List<float>(); var idx = new List<uint>();
        for (int j = 0; j <= radial; j++) for (int i = 0; i <= tubular; i++)
            {
                float u = (float)i / tubular * arc, v = (float)j / radial * MathF.PI * 2;
                float x = (r + tube * MathF.Cos(v)) * MathF.Cos(u), y = (r + tube * MathF.Cos(v)) * MathF.Sin(u), z = tube * MathF.Sin(v);
                p.Add(x); p.Add(y); p.Add(z);
                var c = new Vector3(r * MathF.Cos(u), r * MathF.Sin(u), 0); var nn = Vector3.Normalize(new Vector3(x, y, z) - c); n.Add(nn.X); n.Add(nn.Y); n.Add(nn.Z);
                uv.Add((float)i / tubular); uv.Add((float)j / radial);
            }
        for (int j = 1; j <= radial; j++) for (int i = 1; i <= tubular; i++)
            {
                uint a = (uint)((tubular + 1) * j + i - 1), b = (uint)((tubular + 1) * (j - 1) + i - 1), c = (uint)((tubular + 1) * (j - 1) + i), d = (uint)((tubular + 1) * j + i);
                idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(b); idx.Add(c); idx.Add(d);
            }
        return Make(p, n, uv, idx);
    }

    /// <summary>A profile (x = radius, y = height) spun round the Y axis.</summary>
    public static Geometry Lathe(IList<Vector2> pts, int segs = 12, float phiStart = 0, float phiLen = MathF.PI * 2)
    {
        var p = new List<float>(); var uv = new List<float>(); var idx = new List<uint>();
        for (int i = 0; i <= segs; i++)
        {
            float phi = phiStart + (float)i / segs * phiLen, s = MathF.Sin(phi), c = MathF.Cos(phi);
            for (int j = 0; j < pts.Count; j++) { p.Add(pts[j].X * s); p.Add(pts[j].Y); p.Add(pts[j].X * c); uv.Add((float)i / segs); uv.Add((float)j / (pts.Count - 1)); }
        }
        for (int i = 0; i < segs; i++) for (int j = 0; j < pts.Count - 1; j++)
            {
                uint b = (uint)(j + i * pts.Count), a = b, bb = (uint)(b + pts.Count), c = (uint)(b + pts.Count + 1), d = (uint)(b + 1);
                idx.Add(a); idx.Add(bb); idx.Add(d); idx.Add(c); idx.Add(d); idx.Add(bb);
            }
        var g = new Geometry { Position = p.ToArray(), Uv = uv.ToArray(), Index = idx.ToArray() }; g.ComputeNormals(); return g;
    }

    /// <summary>A tube along a polyline (Catmull-Rom smoothed), with rotation-minimising frames.</summary>
    public static Geometry Tube(IList<Vector3> path, int tubular = 64, float radius = 1, int radial = 8, bool closed = false, Func<float, float> radiusAt = null)
    {
        // three.js samples the curve by arc length (getPointAt) and builds Frenet frames the same way as here
        const int DIV = 200; var len = new float[DIV + 1]; var last = CatmullRom(path, 0, closed);
        for (int i = 1; i <= DIV; i++) { var c = CatmullRom(path, (float)i / DIV, closed); len[i] = len[i - 1] + Vector3.Distance(c, last); last = c; }
        float UtoT(float u)
        {
            float target = u * len[DIV]; int lo = 0, hi = DIV;
            while (lo <= hi) { int mid = (lo + hi) / 2; if (len[mid] < target) lo = mid + 1; else if (len[mid] > target) hi = mid - 1; else { hi = mid; break; } }
            int i = Math.Max(0, hi); if (len[i] == target || i >= DIV) return (float)i / DIV;
            float seg = len[i + 1] - len[i]; return (i + (seg > 0 ? (target - len[i]) / seg : 0)) / DIV;
        }
        var pts = new Vector3[tubular + 1]; var tan = new Vector3[tubular + 1];
        for (int i = 0; i <= tubular; i++)
        {
            float u = (float)i / tubular, t = UtoT(u); pts[i] = CatmullRom(path, t, closed);
            float t1 = Math.Max(0, t - 1e-4f), t2 = Math.Min(1, t + 1e-4f); tan[i] = Vector3.Normalize(CatmullRom(path, t2, closed) - CatmullRom(path, t1, closed));
        }
        var nrm = new Vector3[tubular + 1]; var bin = new Vector3[tubular + 1];
        { float tx = MathF.Abs(tan[0].X), ty = MathF.Abs(tan[0].Y), tz = MathF.Abs(tan[0].Z), mn = float.MaxValue; var nn = Vector3.UnitX;
          if (tx <= mn) { mn = tx; nn = Vector3.UnitX; } if (ty <= mn) { mn = ty; nn = Vector3.UnitY; } if (tz <= mn) nn = Vector3.UnitZ;
          var vec = Vector3.Normalize(Vector3.Cross(tan[0], nn)); nrm[0] = Vector3.Cross(tan[0], vec); bin[0] = Vector3.Cross(tan[0], nrm[0]); }
        for (int i = 1; i <= tubular; i++)
        {
            nrm[i] = nrm[i - 1]; bin[i] = bin[i - 1];
            var vec = Vector3.Cross(tan[i - 1], tan[i]);
            if (vec.Length() > 1e-7f)
            {
                vec = Vector3.Normalize(vec); float th = MathF.Acos(Math.Clamp(Vector3.Dot(tan[i - 1], tan[i]), -1, 1));
                nrm[i] = Vector3.Transform(nrm[i], Matrix4x4.CreateFromAxisAngle(vec, th));
            }
            bin[i] = Vector3.Cross(tan[i], nrm[i]);
        }
        var p = new List<float>(); var n = new List<float>(); var uv = new List<float>(); var idx = new List<uint>();
        for (int i = 0; i <= tubular; i++)
        {
            float rr = radiusAt?.Invoke((float)i / tubular) ?? radius;
            for (int j = 0; j <= radial; j++)
            {
                float v = (float)j / radial * MathF.PI * 2, s = MathF.Sin(v), c = -MathF.Cos(v);
                var nn = Vector3.Normalize(c * nrm[i] + s * bin[i]); var q = pts[i] + nn * rr;
                p.Add(q.X); p.Add(q.Y); p.Add(q.Z); n.Add(nn.X); n.Add(nn.Y); n.Add(nn.Z); uv.Add((float)i / tubular); uv.Add((float)j / radial);
            }
        }
        for (int j = 1; j <= tubular; j++) for (int i = 1; i <= radial; i++)
            {
                uint a = (uint)((radial + 1) * (j - 1) + (i - 1)), b = (uint)((radial + 1) * j + (i - 1)), c = (uint)((radial + 1) * j + i), d = (uint)((radial + 1) * (j - 1) + i);
                idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(b); idx.Add(c); idx.Add(d);
            }
        return Make(p, n, uv, idx);
    }

    /// <summary>Centripetal-free uniform Catmull-Rom through points (three.js CatmullRomCurve3, "catmullrom" type, tension 0.5).</summary>
    public static Vector3 CatmullRom(IList<Vector3> pts, float t, bool closed)
    {
        int l = pts.Count; float pp = (l - (closed ? 0 : 1)) * t; int ip = (int)MathF.Floor(pp); float w = pp - ip;
        if (closed) ip += ip > 0 ? 0 : ((int)MathF.Floor(MathF.Abs(ip) / l) + 1) * l;
        else if (w == 0 && ip == l - 1) { ip = l - 2; w = 1; }
        Vector3 P(int i) => closed ? pts[((i % l) + l) % l] : pts[Math.Clamp(i, 0, l - 1)];
        Vector3 p0 = closed || ip > 0 ? P(ip - 1) : 2 * pts[0] - pts[1 % l], p1 = P(ip), p2 = P(ip + 1), p3 = closed || ip + 2 < l ? P(ip + 2) : 2 * pts[l - 1] - pts[Math.Max(0, l - 2)];
        float t2 = w * w, t3 = t2 * w;
        Vector3 v0 = (p2 - p0) * 0.5f, v1 = (p3 - p1) * 0.5f;
        return (2 * p1 - 2 * p2 + v0 + v1) * t3 + (-3 * p1 + 3 * p2 - 2 * v0 - v1) * t2 + v0 * w + p1;
    }

    /// <summary>Edge lines of a geometry (for wireframe overlays): every triangle edge once.</summary>
    public static Geometry Wireframe(Geometry g)
    {
        var seen = new HashSet<(int, int)>(); var p = new List<float>();
        int nt = g.Index?.Length ?? g.VertexCount;
        int I(int i) => g.Index != null ? (int)g.Index[i] : i;
        for (int i = 0; i + 2 < nt; i += 3)
            for (int e = 0; e < 3; e++)
            {
                int a = I(i + e), b = I(i + (e + 1) % 3); var key = a < b ? (a, b) : (b, a);
                if (!seen.Add(key)) continue;
                var pa = g.P(a); var pb = g.P(b); p.Add(pa.X); p.Add(pa.Y); p.Add(pa.Z); p.Add(pb.X); p.Add(pb.Y); p.Add(pb.Z);
            }
        return new Geometry { Position = p.ToArray() };
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using GermBall.Engine;

namespace GermBall.Tests;

/// <summary>
/// A software rasteriser for <see cref="DrawList"/>: the same lighting, grid, fog and blending rules as the D3D11 shader,
/// so screenshots can be checked on any machine. Slow and simple on purpose.
/// </summary>
public sealed class SoftRenderer
{
    public readonly int W, H;
    readonly Vector3[] color; readonly float[] depth;
    Vector4 vp; bool noDepth;

    public SoftRenderer(int w, int h) { W = w; H = h; color = new Vector3[w * h]; depth = new float[w * h]; }

    public void Render(DrawList dl)
    {
        // sky: fog colour at the horizon, sky colour overhead (the D3D renderer draws the same gradient)
        vp = dl.ViewportOr(W, H);
        for (int y = 0; y < H; y++)
        {
            var c = Vector3.Lerp(dl.SkyColor * 0.85f, dl.FogColor, MathF.Pow(MathX.Clamp01((y - vp.Y) / vp.W), 0.6f));
            for (int x = 0; x < W; x++) { color[y * W + x] = y >= vp.Y && y < vp.Y + vp.W && x >= vp.X && x < vp.X + vp.Z ? c : dl.FogColor; depth[y * W + x] = 1; }
        }
        var viewProj = dl.View * dl.Proj;
        var opaque = new List<DrawItem>(); var transp = new List<(DrawItem it, float d)>(); var top = new List<DrawItem>();
        foreach (var it in dl.Items)
        {
            if ((it.Flags & DrawFlags.OnTop) != 0) top.Add(it);
            else if (it.Color.W >= 0.999f) opaque.Add(it);
            else transp.Add((it, Vector3.DistanceSquared(it.World.Translation, dl.CamPos)));
        }
        transp.Sort((a, b) => b.d.CompareTo(a.d));
        foreach (var it in opaque) DrawItem(dl, it, viewProj, false);
        foreach (var (it, _) in transp) DrawItem(dl, it, viewProj, true);
        noDepth = true;
        foreach (var it in top) DrawItem(dl, it, viewProj, true);
        noDepth = false;
        foreach (var q in dl.Overlay.Quads) Quad(q);
    }

    struct Vtx { public Vector4 Clip; public Vector3 World, Normal, Col; }

    void DrawItem(DrawList dl, DrawItem it, Matrix4x4 vp, bool blend)
    {
        var mesh = Mesh.Get(it.Mesh);
        var m = it.World;
        var s2 = new Vector3(new Vector3(m.M11, m.M12, m.M13).LengthSquared(), new Vector3(m.M21, m.M22, m.M23).LengthSquared(), new Vector3(m.M31, m.M32, m.M33).LengthSquared());
        var verts = new Vtx[mesh.Pos.Count];
        for (int i = 0; i < verts.Length; i++)
        {
            var wp = Vector3.Transform(mesh.Pos[i], m);
            var n = Vector3.Normalize(Vector3.TransformNormal(mesh.Nrm[i] / s2, m));
            verts[i] = new Vtx { World = wp, Normal = n, Col = mesh.Col[i], Clip = Vector4.Transform(new Vector4(wp, 1), vp) };
        }
        var poly = new List<Vtx>(8); var tmp = new List<Vtx>(8);
        for (int t = 0; t < mesh.Idx.Count; t += 3)
        {
            poly.Clear(); poly.Add(verts[mesh.Idx[t]]); poly.Add(verts[mesh.Idx[t + 1]]); poly.Add(verts[mesh.Idx[t + 2]]);
            // clip against the near plane (z >= 0 in D3D clip space)
            tmp.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                bool ain = a.Clip.Z >= 0, bin = b.Clip.Z >= 0;
                if (ain) tmp.Add(a);
                if (ain != bin)
                {
                    float k = a.Clip.Z / (a.Clip.Z - b.Clip.Z);
                    tmp.Add(new Vtx { Clip = Vector4.Lerp(a.Clip, b.Clip, k), World = Vector3.Lerp(a.World, b.World, k), Normal = Vector3.Lerp(a.Normal, b.Normal, k), Col = Vector3.Lerp(a.Col, b.Col, k) });
                }
            }
            for (int i = 1; i + 1 < tmp.Count; i++) Tri(dl, it, tmp[0], tmp[i], tmp[i + 1], blend);
        }
    }

    void Tri(DrawList dl, DrawItem it, Vtx a, Vtx b, Vtx c, bool blend)
    {
        Vector3 S(Vtx v) => new(vp.X + (v.Clip.X / v.Clip.W * 0.5f + 0.5f) * vp.Z, vp.Y + (0.5f - v.Clip.Y / v.Clip.W * 0.5f) * vp.W, v.Clip.Z / v.Clip.W);
        var p0 = S(a); var p1 = S(b); var p2 = S(c);
        float area = (p1.X - p0.X) * (p2.Y - p0.Y) - (p2.X - p0.X) * (p1.Y - p0.Y);
        if (MathF.Abs(area) < 1e-6f) return;
        int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X)))), x1 = Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
        int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)))), y1 = Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
        float iw0 = 1 / a.Clip.W, iw1 = 1 / b.Clip.W, iw2 = 1 / c.Clip.W;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((p1.X - px) * (p2.Y - py) - (p2.X - px) * (p1.Y - py)) / area;
                float w1 = ((p2.X - px) * (p0.Y - py) - (p0.X - px) * (p2.Y - py)) / area;
                float w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float z = w0 * p0.Z + w1 * p1.Z + w2 * p2.Z;
                int idx = y * W + x;
                if (z < 0 || (!noDepth && z > depth[idx])) continue;
                if (x < vp.X || x >= vp.X + vp.Z || y < vp.Y || y >= vp.Y + vp.W) continue;
                float q0 = w0 * iw0, q1 = w1 * iw1, q2 = w2 * iw2, qs = q0 + q1 + q2;
                q0 /= qs; q1 /= qs; q2 /= qs;
                var wp = a.World * q0 + b.World * q1 + c.World * q2;
                var n = Vector3.Normalize(a.Normal * q0 + b.Normal * q1 + c.Normal * q2);
                var vc = a.Col * q0 + b.Col * q1 + c.Col * q2;
                var col = Shade(dl, it, wp, n, vc);
                if (blend) color[idx] = Vector3.Lerp(color[idx], col, it.Color.W);
                else { color[idx] = col; depth[idx] = z; }
            }
    }

    /// <summary>Mirrors PS in the D3D11 shader.</summary>
    public static Vector3 Shade(DrawList dl, DrawItem it, Vector3 wp, Vector3 n, Vector3 vc)
    {
        var albedo = new Vector3(it.Color.X, it.Color.Y, it.Color.Z) * vc;
        Vector3 col;
        if ((it.Flags & DrawFlags.Unlit) != 0) col = albedo;
        else
        {
            if ((it.Flags & DrawFlags.Grid) != 0) albedo *= GridShade(wp, n);
            var hemi = Vector3.Lerp(dl.GroundColor, dl.SkyColor, n.Y * 0.5f + 0.5f);
            float sun = MathF.Max(0, Vector3.Dot(n, -dl.SunDir));
            col = albedo * (hemi * 0.75f + new Vector3(1f, 0.96f, 0.88f) * sun * 0.75f);
        }
        float dist = Vector3.Distance(wp, dl.CamPos);
        float fog = MathX.Clamp01((dist - dl.FogStart) / (dl.FogEnd - dl.FogStart));
        return Vector3.Lerp(col, dl.FogColor, fog);
    }

    public static float GridShade(Vector3 wp, Vector3 n)
    {
        var an = Vector3.Abs(n);
        Vector2 uv = an.Y >= an.X && an.Y >= an.Z ? new(wp.X, wp.Z) : an.X >= an.Z ? new(wp.Z, wp.Y) : new(wp.X, wp.Y);
        float Line(float v, float period, float width) { float f = v / period - MathF.Floor(v / period); return MathF.Min(f, 1 - f) * period < width ? 1 : 0; }
        float major = MathF.Max(Line(uv.X, 5, 0.04f), Line(uv.Y, 5, 0.04f));
        float minor = MathF.Max(Line(uv.X, 1, 0.02f), Line(uv.Y, 1, 0.02f));
        return 1 - MathF.Max(major * 0.28f, minor * 0.14f);
    }

    void Quad(Overlay.Quad q)
    {
        int x0 = Math.Max(0, (int)MathF.Round(q.X)), y0 = Math.Max(0, (int)MathF.Round(q.Y));
        int x1 = Math.Min(W, (int)MathF.Round(q.X + q.W)), y1 = Math.Min(H, (int)MathF.Round(q.Y + q.H));
        var c = new Vector3(q.Color.X, q.Color.Y, q.Color.Z);
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) color[y * W + x] = Vector3.Lerp(color[y * W + x], c, q.Color.W);
    }

    public void SavePng(string path)
    {
        var raw = new byte[(W * 3 + 1) * H];
        for (int y = 0; y < H; y++)
        {
            int o = y * (W * 3 + 1); raw[o] = 0;
            for (int x = 0; x < W; x++)
            {
                var c = color[y * W + x];
                raw[o + 1 + x * 3] = (byte)(MathX.Clamp01(c.X) * 255 + 0.5f);
                raw[o + 2 + x * 3] = (byte)(MathX.Clamp01(c.Y) * 255 + 0.5f);
                raw[o + 3 + x * 3] = (byte)(MathX.Clamp01(c.Z) * 255 + 0.5f);
            }
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw);
        using var f = File.Create(path);
        f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var hdr = new byte[13];
        WriteBE(hdr, 0, (uint)W); WriteBE(hdr, 4, (uint)H); hdr[8] = 8; hdr[9] = 2;
        Chunk(f, "IHDR", hdr); Chunk(f, "IDAT", ms.ToArray()); Chunk(f, "IEND", Array.Empty<byte>());
    }
    static void WriteBE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, (uint)data.Length); s.Write(len);
        var td = new byte[4 + data.Length]; for (int i = 0; i < 4; i++) td[i] = (byte)type[i]; data.CopyTo(td, 4);
        s.Write(td);
        var crc = new byte[4]; WriteBE(crc, 0, Crc(td)); s.Write(crc);
    }
    static uint[] crcTable;
    static uint Crc(byte[] d)
    {
        if (crcTable == null)
        {
            crcTable = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; crcTable[n] = c; }
        }
        uint r = 0xFFFFFFFFu; foreach (var b in d) r = crcTable[(r ^ b) & 0xFF] ^ (r >> 8);
        return r ^ 0xFFFFFFFFu;
    }
}

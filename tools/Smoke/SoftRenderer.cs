using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Render;
using Vortice.Mathematics;

namespace Smoke;

/// <summary>
/// A software stand-in for the Direct3D renderer, for looking at frames on machines without Windows. It follows the same
/// conventions as the GPU path (row-vector matrices, right-handed view, D3D 0..1 depth, counter-clockwise front faces,
/// hemisphere + sun lighting, linear fog) so geometry, winding and layout problems show up the same way. No shadows.
/// </summary>
public sealed class SoftRenderer : IRenderer
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float RenderScale { get; set; } = 1;
    public bool VSync { get; set; }
    public int Triangles;
    Vector3[] col; float[] depth;

    public SoftRenderer(int w, int h) { Resize(w, h); }
    public void Resize(int w, int h) { Width = w; Height = h; col = new Vector3[w * h]; depth = new float[w * h]; }
    public void Present() { }

    public byte[] Capture(out int w, out int h)
    {
        w = Width; h = Height; var b = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { var c = Vector3.Clamp(col[i], Vector3.Zero, Vector3.One); b[i * 4] = (byte)(c.X * 255); b[i * 4 + 1] = (byte)(c.Y * 255); b[i * 4 + 2] = (byte)(c.Z * 255); b[i * 4 + 3] = 255; }
        return b;
    }

    // ---------------------------------------------------------------- 3D
    struct Item { public Node Node; public Geometry Geo; public Material Mat; public Matrix4x4 World; public float Depth; public int Kind; }
    Scene sc; Matrix4x4 view, vp; Vector3 camPos; float tanHalf;

    public void Render(Scene scene, Camera cam)
    {
        if (scene == null || cam == null) { Array.Fill(col, new Vector3(0x12 / 255f, 0x06 / 255f, 0x1f / 255f)); Array.Fill(depth, 1); return; }
        sc = scene; cam.Aspect = (float)Width / Height; scene.UpdateWorld(Matrix4x4.Identity);
        view = cam.View; vp = view * cam.Proj; camPos = cam.Position; tanHalf = MathF.Tan(cam.Fov * MathF.PI / 360);
        Array.Fill(col, scene.Background); Array.Fill(depth, 1); Triangles = 0;
        var opq = new List<Item>(); var tr = new List<Item>();
        void Collect(Node n)
        {
            if (!n.Visible) return;
            Item? it = n switch
            {
                InstancedMesh im when im.Geo != null && im.Mat != null && im.Count > 0 => new Item { Node = im, Geo = im.Geo, Mat = im.Mat, World = im.World, Kind = 1 },
                InstancedMesh => null,   // empty: draws nothing (mirrors the renderer)
                Mesh m when m.Geo != null && m.Mat != null => new Item { Node = m, Geo = m.Geo, Mat = m.Mat, World = m.World, Kind = 0 },
                Points p when p.Geo != null && p.Mat != null => new Item { Node = p, Geo = p.Geo, Mat = p.Mat, World = p.World, Kind = 2 },
                Lines l when l.Geo != null && l.Mat != null => new Item { Node = l, Geo = l.Geo, Mat = l.Mat, World = l.World, Kind = 3 },
                _ => null
            };
            if (it is Item i)
            {
                i.Depth = -Vector3.Transform(new Vector3(i.World.M41, i.World.M42, i.World.M43), view).Z;
                if (i.Mat.Transparent || i.Mat.Blending == Blend.Additive) tr.Add(i); else opq.Add(i);
            }
            foreach (var c in n.Children) Collect(c);
        }
        Collect(scene);
        opq.Sort((a, b) => a.Node.RenderOrder != b.Node.RenderOrder ? a.Node.RenderOrder.CompareTo(b.Node.RenderOrder) : a.Depth.CompareTo(b.Depth));
        tr.Sort((a, b) => a.Node.RenderOrder != b.Node.RenderOrder ? a.Node.RenderOrder.CompareTo(b.Node.RenderOrder) : b.Depth.CompareTo(a.Depth));
        foreach (var it in opq) Draw(it);
        foreach (var it in tr) Draw(it);
    }

    void Draw(Item it)
    {
        var m = it.Mat; if (m.Shader == "sky") return;
        if (it.Kind == 2) { DrawPoints(it); return; }
        if (it.Kind == 3) { DrawLines(it); return; }
        var g = it.Geo; int nv = g.VertexCount; if (nv == 0) return;
        int count = g.Index != null ? (g.DrawCount < 0 ? g.Index.Length - g.DrawStart : Math.Min(g.DrawCount, g.Index.Length - g.DrawStart)) : (g.DrawCount < 0 ? nv - g.DrawStart : Math.Min(g.DrawCount, nv - g.DrawStart));
        if (count <= 0) return;
        int inst = 1; Matrix4x4[] mats = null;
        if (it.Kind == 1) { var im = (InstancedMesh)it.Node; inst = Math.Min(im.Count, im.Capacity); mats = im.Matrices; }
        var clip = new Vector4[nv]; var lit = new Vector4[nv]; var uv = new Vector2[nv];
        for (int k = 0; k < inst; k++)
        {
            var M = mats != null ? mats[k] * it.World : it.World;
            var icol = (it.Kind == 1 && ((InstancedMesh)it.Node).Colors is { } cs) ? cs[k] : Vector3.One;
            for (int i = 0; i < nv; i++)
            {
                var p = new Vector3(g.Position[i * 3], g.Position[i * 3 + 1], g.Position[i * 3 + 2]);
                var wp = Vector3.Transform(p, M); clip[i] = Vector4.Transform(new Vector4(wp, 1), vp);
                var n = g.Normal != null ? Vector3.TransformNormal(new Vector3(g.Normal[i * 3], g.Normal[i * 3 + 1], g.Normal[i * 3 + 2]), M) : Vector3.UnitY;
                n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
                var vc = (m.VertexColors && g.Color != null ? new Vector3(g.Color[i * 3], g.Color[i * 3 + 1], g.Color[i * 3 + 2]) : Vector3.One) * icol;
                float vz = -Vector3.Transform(wp, view).Z;
                lit[i] = Shade(m, n, wp, vc, vz);
                uv[i] = g.Uv != null ? new Vector2(g.Uv[i * 2], g.Uv[i * 2 + 1]) * m.MapRepeat + m.MapOffset : Vector2.Zero;
            }
            for (int t = 0; t + 2 < count; t += 3)
            {
                int a, b, c;
                if (g.Index != null) { a = (int)g.Index[g.DrawStart + t]; b = (int)g.Index[g.DrawStart + t + 1]; c = (int)g.Index[g.DrawStart + t + 2]; }
                else { a = g.DrawStart + t; b = a + 1; c = a + 2; }
                Tri(m, clip[a], clip[b], clip[c], lit[a], lit[b], lit[c], uv[a], uv[b], uv[c]);
            }
        }
    }

    // per-vertex lighting (the GPU does it per pixel; close enough to judge shapes and colours)
    Vector4 Shade(Material m, Vector3 n, Vector3 wp, Vector3 vc, float viewZ)
    {
        var bse = m.Color * vc; Vector3 c = bse;
        if (m.Shader == "roof") c = new Vector3(0.55f, 0.12f, 0.18f);
        else if (m.Kind == MatKind.Lambert || m.Kind == MatKind.Phong)
        {
            float hemi = 0.5f * n.Y + 0.5f; var light = Vector3.Lerp(sc.HemiGround * sc.HemiIntensity, sc.HemiSky * sc.HemiIntensity, hemi);
            var sd = Vector3.Normalize(sc.SunDir); float nl = MathF.Max(Vector3.Dot(n, sd), 0);
            light += sc.SunColor * sc.SunIntensity * nl;
            c = bse * light + m.Emissive;
        }
        if (m.Fog && sc.FogFar > sc.FogNear) { float f = Math.Clamp((viewZ - sc.FogNear) / (sc.FogFar - sc.FogNear), 0, 1); f = f * f * (3 - 2 * f); c = Vector3.Lerp(c, sc.FogColor, f); }
        return new Vector4(c, m.Opacity);
    }

    struct V { public Vector4 P; public Vector4 C; public Vector2 T; }
    static V Lerp(V a, V b, float t) => new() { P = Vector4.Lerp(a.P, b.P, t), C = Vector4.Lerp(a.C, b.C, t), T = Vector2.Lerp(a.T, b.T, t) };

    void Tri(Material m, Vector4 p0, Vector4 p1, Vector4 p2, Vector4 c0, Vector4 c1, Vector4 c2, Vector2 t0, Vector2 t1, Vector2 t2)
    {
        // clip against the near plane (z >= 0 in D3D clip space)
        Span<V> inp = stackalloc V[4]; Span<V> outp = stackalloc V[4]; int n = 0;
        inp[0] = new V { P = p0, C = c0, T = t0 }; inp[1] = new V { P = p1, C = c1, T = t1 }; inp[2] = new V { P = p2, C = c2, T = t2 };
        for (int i = 0; i < 3; i++)
        {
            var a = inp[i]; var b = inp[(i + 1) % 3]; bool ain = a.P.Z >= 0, bin = b.P.Z >= 0;
            if (ain) outp[n++] = a;
            if (ain != bin) { float t = a.P.Z / (a.P.Z - b.P.Z); outp[n++] = Lerp(a, b, t); }
        }
        if (n < 3) return;
        for (int i = 1; i + 1 < n; i++) Raster(m, outp[0], outp[i], outp[i + 1]);
    }

    void Raster(Material m, V a, V b, V c)
    {
        if (a.P.W <= 1e-5f || b.P.W <= 1e-5f || c.P.W <= 1e-5f) return;
        float W = Width, H = Height;
        Vector3 S(Vector4 p) => new((p.X / p.W + 1) * 0.5f * W, (1 - p.Y / p.W) * 0.5f * H, p.Z / p.W);
        var s0 = S(a.P); var s1 = S(b.P); var s2 = S(c.P);
        float area = (s1.X - s0.X) * (s2.Y - s0.Y) - (s2.X - s0.X) * (s1.Y - s0.Y);
        if (MathF.Abs(area) < 1e-6f) return;
        bool front = area < 0;   // counter-clockwise as seen on screen (y down)
        if (m.Side == Side.Front && !front) return;
        if (m.Side == Side.Back && front) return;
        Triangles++;
        int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(s0.X, MathF.Min(s1.X, s2.X)))), x1 = Math.Min(Width - 1, (int)MathF.Ceiling(MathF.Max(s0.X, MathF.Max(s1.X, s2.X))));
        int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(s0.Y, MathF.Min(s1.Y, s2.Y)))), y1 = Math.Min(Height - 1, (int)MathF.Ceiling(MathF.Max(s0.Y, MathF.Max(s1.Y, s2.Y))));
        if (x0 > x1 || y0 > y1) return;
        float iw0 = 1 / a.P.W, iw1 = 1 / b.P.W, iw2 = 1 / c.P.W; float inv = 1 / area;
        var tex = m.Map; bool add = m.Blending == Blend.Additive, blend = m.Transparent || add;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((s1.X - px) * (s2.Y - py) - (s2.X - px) * (s1.Y - py)) * inv;
                float w1 = ((s2.X - px) * (s0.Y - py) - (s0.X - px) * (s2.Y - py)) * inv;
                float w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float z = w0 * s0.Z + w1 * s1.Z + w2 * s2.Z; if (z < 0 || z > 1) continue;
                int o = y * Width + x;
                if (m.DepthTest && z > depth[o]) continue;
                float pw0 = w0 * iw0, pw1 = w1 * iw1, pw2 = w2 * iw2, ps = 1 / (pw0 + pw1 + pw2);
                var cc = (a.C * pw0 + b.C * pw1 + c.C * pw2) * ps;
                if (tex != null)
                {
                    var t = (a.T * pw0 + b.T * pw1 + c.T * pw2) * ps; var tc = Sample(tex, t.X, t.Y);
                    cc *= tc;
                }
                if (cc.W < m.AlphaTest) continue;
                var rgb = new Vector3(cc.X, cc.Y, cc.Z);
                if (add) col[o] += rgb * cc.W; else if (blend) col[o] = Vector3.Lerp(col[o], rgb, Math.Clamp(cc.W, 0, 1)); else col[o] = rgb;
                if (m.DepthWrite && !blend) depth[o] = z; else if (m.DepthWrite && cc.W > 0.5f) depth[o] = z;
            }
    }

    static Vector4 Sample(Texture t, float u, float v)
    {
        if (t.Repeat) { u -= MathF.Floor(u); v -= MathF.Floor(v); } else { u = Math.Clamp(u, 0, 1); v = Math.Clamp(v, 0, 1); }
        int x = Math.Clamp((int)(u * t.Width), 0, t.Width - 1), y = Math.Clamp((int)(v * t.Height), 0, t.Height - 1), o = (y * t.Width + x) * 4;
        return new Vector4(t.Pixels[o] / 255f, t.Pixels[o + 1] / 255f, t.Pixels[o + 2] / 255f, t.Pixels[o + 3] / 255f);
    }

    void DrawPoints(Item it)
    {
        var g = it.Geo; var m = it.Mat; int n = g.VertexCount;
        for (int i = 0; i < n; i++)
        {
            var wp = Vector3.Transform(new Vector3(g.Position[i * 3], g.Position[i * 3 + 1], g.Position[i * 3 + 2]), it.World);
            var cp = Vector4.Transform(new Vector4(wp, 1), vp); if (cp.W <= 0.05f || cp.Z < 0) continue;
            float sx = (cp.X / cp.W + 1) * 0.5f * Width, sy = (1 - cp.Y / cp.W) * 0.5f * Height, z = cp.Z / cp.W;
            float half = m.SizeAttenuation ? m.Size * 0.5f / (cp.W * tanHalf) * Height * 0.5f : m.Size * 0.5f;
            var c = Shade(new Material { Kind = MatKind.Basic, Color = m.Color, Fog = m.Fog, Opacity = m.Opacity }, Vector3.UnitY, wp, g.Color != null ? new Vector3(g.Color[i * 3], g.Color[i * 3 + 1], g.Color[i * 3 + 2]) : Vector3.One, cp.W);
            for (int y = (int)(sy - half); y <= (int)(sy + half); y++) for (int x = (int)(sx - half); x <= (int)(sx + half); x++)
                {
                    if (x < 0 || y < 0 || x >= Width || y >= Height) continue; int o = y * Width + x;
                    if (m.DepthTest && z > depth[o]) continue;
                    var cc = c; if (m.Map != null) cc *= Sample(m.Map, (x - sx + half) / (2 * half + 1e-3f), (y - sy + half) / (2 * half + 1e-3f));
                    if (cc.W < m.AlphaTest) continue;
                    var rgb = new Vector3(cc.X, cc.Y, cc.Z);
                    if (m.Blending == Blend.Additive) col[o] += rgb * cc.W; else col[o] = Vector3.Lerp(col[o], rgb, Math.Clamp(cc.W, 0, 1));
                }
        }
    }

    void DrawLines(Item it)
    {
        var g = it.Geo; var m = it.Mat; var L = (Lines)it.Node; int n = g.DrawCount < 0 ? g.VertexCount : Math.Min(g.DrawCount, g.VertexCount);
        int step = L.Strip ? 1 : 2;
        for (int i = 0; i + 1 < n; i += step)
        {
            var a = Vector4.Transform(new Vector4(Vector3.Transform(new Vector3(g.Position[i * 3], g.Position[i * 3 + 1], g.Position[i * 3 + 2]), it.World), 1), vp);
            var b = Vector4.Transform(new Vector4(Vector3.Transform(new Vector3(g.Position[i * 3 + 3], g.Position[i * 3 + 4], g.Position[i * 3 + 5]), it.World), 1), vp);
            if (a.W <= 0.05f || b.W <= 0.05f) continue;
            float ax = (a.X / a.W + 1) * 0.5f * Width, ay = (1 - a.Y / a.W) * 0.5f * Height, bx = (b.X / b.W + 1) * 0.5f * Width, by = (1 - b.Y / b.W) * 0.5f * Height;
            int steps = (int)MathF.Max(MathF.Abs(bx - ax), MathF.Abs(by - ay)) + 1; if (steps > 4000) continue;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps; int x = (int)(ax + (bx - ax) * t), y = (int)(ay + (by - ay) * t); if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                int o = y * Width + x; float z = (a.Z / a.W) * (1 - t) + (b.Z / b.W) * t; if (m.DepthTest && z > depth[o]) continue;
                if (m.Blending == Blend.Additive) col[o] += m.Color * m.Opacity; else col[o] = Vector3.Lerp(col[o], m.Color, m.Opacity);
            }
        }
    }

    // ---------------------------------------------------------------- 2D
    public void DrawUI(List<(Texture tex, int start, int count, Rect? clip)> batches, Renderer.UIVert[] v, int nverts)
    {
        foreach (var (tex, start, count, clip) in batches)
        {
            var c = clip ?? new Rect(0, 0, Width, Height);
            int cx0 = Math.Max(0, (int)c.X), cy0 = Math.Max(0, (int)c.Y), cx1 = Math.Min(Width, (int)(c.X + c.Width)), cy1 = Math.Min(Height, (int)(c.Y + c.Height));
            for (int i = start; i + 2 < start + count; i += 3) UiTri(tex, v[i], v[i + 1], v[i + 2], cx0, cy0, cx1, cy1);
        }
    }
    void UiTri(Texture tex, Renderer.UIVert a, Renderer.UIVert b, Renderer.UIVert c, int cx0, int cy0, int cx1, int cy1)
    {
        float area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y); if (MathF.Abs(area) < 1e-6f) return; float inv = 1 / area;
        int x0 = Math.Max(cx0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)))), x1 = Math.Min(cx1 - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int y0 = Math.Max(cy0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)))), y1 = Math.Min(cy1 - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((b.X - px) * (c.Y - py) - (c.X - px) * (b.Y - py)) * inv, w1 = ((c.X - px) * (a.Y - py) - (a.X - px) * (c.Y - py)) * inv, w2 = 1 - w0 - w1;
                if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                var cc = new Vector4(a.R * w0 + b.R * w1 + c.R * w2, a.G * w0 + b.G * w1 + c.G * w2, a.B * w0 + b.B * w1 + c.B * w2, a.A * w0 + b.A * w1 + c.A * w2);
                if (tex != null) cc *= SampleLinear(tex, a.U * w0 + b.U * w1 + c.U * w2, a.V * w0 + b.V * w1 + c.V * w2);
                int o = y * Width + x; col[o] = Vector3.Lerp(col[o], new Vector3(cc.X, cc.Y, cc.Z), Math.Clamp(cc.W, 0, 1));
            }
    }
    static Vector4 SampleLinear(Texture t, float u, float v)
    {
        if (t.Nearest) return Sample(t, u, v);
        float x = Math.Clamp(u * t.Width - 0.5f, 0, t.Width - 1), y = Math.Clamp(v * t.Height - 0.5f, 0, t.Height - 1);
        int ix = (int)x, iy = (int)y, jx = Math.Min(ix + 1, t.Width - 1), jy = Math.Min(iy + 1, t.Height - 1); float fx = x - ix, fy = y - iy;
        Vector4 P(int xx, int yy) { int o = (yy * t.Width + xx) * 4; return new Vector4(t.Pixels[o], t.Pixels[o + 1], t.Pixels[o + 2], t.Pixels[o + 3]) / 255f; }
        return Vector4.Lerp(Vector4.Lerp(P(ix, iy), P(jx, iy), fx), Vector4.Lerp(P(ix, jy), P(jx, jy), fx), fy);
    }
}

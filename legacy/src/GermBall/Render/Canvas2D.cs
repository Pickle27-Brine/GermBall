using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace GermBall.Render;

/// <summary>
/// A small software 2D canvas with the parts of the HTML canvas API the game used for its textures, the face builder and
/// the radar: paths (lines, arcs, ellipses, quadratic and cubic curves), anti-aliased non-zero fills, round-capped strokes,
/// transforms, gradients, global alpha and image copies. Pixels are straight-alpha RGBA.
/// </summary>
public sealed class Canvas2D
{
    public readonly int W, H;
    public readonly float[] Px;   // rgba, 0..1, straight alpha
    public Canvas2D(int w, int h) { W = w; H = h; Px = new float[w * h * 4]; }

    // ---- state
    public object FillStyle = "#000", StrokeStyle = "#000";
    public float LineWidth = 1, GlobalAlpha = 1;
    public bool RoundCap = false;
    public bool Additive;   // "lighter" composite
    Matrix3x2 xf = Matrix3x2.Identity;
    readonly Stack<(Matrix3x2, object, object, float, float, bool, bool)> stack = new();
    public void Save() => stack.Push((xf, FillStyle, StrokeStyle, LineWidth, GlobalAlpha, RoundCap, Additive));
    public void Restore() { if (stack.Count > 0) (xf, FillStyle, StrokeStyle, LineWidth, GlobalAlpha, RoundCap, Additive) = stack.Pop(); }
    public void Translate(float x, float y) => xf = Matrix3x2.CreateTranslation(x, y) * xf;
    public void Rotate(float a) => xf = Matrix3x2.CreateRotation(a) * xf;
    public void ScaleBy(float x, float y) => xf = Matrix3x2.CreateScale(x, y) * xf;
    public void SetTransform(float a, float b, float c, float d, float e, float f) => xf = new Matrix3x2(a, b, c, d, e, f);
    public void ResetTransform() => xf = Matrix3x2.Identity;

    // ---- colours
    public static Vector4 Parse(string s)
    {
        s = s.Trim();
        if (s.StartsWith('#'))
        {
            var h = s[1..];
            if (h.Length == 3) h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
            if (h.Length == 4) h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}{h[3]}{h[3]}";
            uint v = uint.Parse(h, NumberStyles.HexNumber);
            return h.Length == 8 ? new Vector4(((v >> 24) & 255) / 255f, ((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f)
                                 : new Vector4(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1);
        }
        if (s.StartsWith("rgb"))
        {
            int a = s.IndexOf('('), b = s.IndexOf(')'); var parts = s[(a + 1)..b].Split(',');
            float P(int i) => float.Parse(parts[i].Trim().TrimEnd('%'), CultureInfo.InvariantCulture);
            return new Vector4(P(0) / 255, P(1) / 255, P(2) / 255, parts.Length > 3 ? P(3) : 1);
        }
        if (s.StartsWith("hsl"))
        {
            int a = s.IndexOf('('), b = s.IndexOf(')'); var parts = s[(a + 1)..b].Split(',');
            float P(int i) => float.Parse(parts[i].Trim().TrimEnd('%').Replace("deg", ""), CultureInfo.InvariantCulture);
            uint c = Core.M.Hsl(P(0) / 360, P(1) / 100, P(2) / 100);
            return new Vector4(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f, parts.Length > 3 ? P(3) : 1);
        }
        return s switch { "white" => Vector4.One, "black" => new Vector4(0, 0, 0, 1), "transparent" => Vector4.Zero, _ => new Vector4(0, 0, 0, 1) };
    }
    public static Vector4 Hex(uint c, float a = 1) => new(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f, a);

    public sealed class Gradient
    {
        public bool Radial; public Vector2 P0, P1; public float R0, R1;
        public readonly List<(float t, Vector4 c)> Stops = new();
        public Gradient Add(float t, string c) { Stops.Add((t, Parse(c))); Stops.Sort((a, b) => a.t.CompareTo(b.t)); return this; }
        public Gradient Add(float t, Vector4 c) { Stops.Add((t, c)); Stops.Sort((a, b) => a.t.CompareTo(b.t)); return this; }
        public Vector4 At(float t)
        {
            if (Stops.Count == 0) return Vector4.Zero;
            if (t <= Stops[0].t) return Stops[0].c;
            for (int i = 1; i < Stops.Count; i++) if (t <= Stops[i].t) { var (ta, ca) = Stops[i - 1]; var (tb, cb) = Stops[i]; return Vector4.Lerp(ca, cb, tb > ta ? (t - ta) / (tb - ta) : 1); }
            return Stops[^1].c;
        }
    }
    public Gradient LinearGradient(float x0, float y0, float x1, float y1) => new() { P0 = Vector2.Transform(new(x0, y0), xf), P1 = Vector2.Transform(new(x1, y1), xf) };
    public Gradient RadialGradient(float x0, float y0, float r0, float x1, float y1, float r1) => new() { Radial = true, P0 = Vector2.Transform(new(x0, y0), xf), P1 = Vector2.Transform(new(x1, y1), xf), R0 = r0 * Scale, R1 = r1 * Scale };
    float Scale => MathF.Sqrt(MathF.Abs(xf.M11 * xf.M22 - xf.M12 * xf.M21));

    Vector4 StyleAt(object style, float x, float y)
    {
        switch (style)
        {
            case string s: return Parse(s);
            case Vector4 v: return v;
            case uint u: return Hex(u);
            case Gradient g:
                if (!g.Radial) { var d = g.P1 - g.P0; float l2 = d.LengthSquared(); return g.At(l2 > 0 ? Vector2.Dot(new Vector2(x, y) - g.P0, d) / l2 : 0); }
                { float dist = Vector2.Distance(new Vector2(x, y), g.P1); return g.At(g.R1 > g.R0 ? (dist - g.R0) / (g.R1 - g.R0) : 0); }
        }
        return new Vector4(0, 0, 0, 1);
    }

    // ---- paths: lists of polylines in device space
    readonly List<List<Vector2>> path = new();
    List<Vector2> cur;
    Vector2 last;
    public void BeginPath() { path.Clear(); cur = null; }
    public void MoveTo(float x, float y) { cur = new List<Vector2>(); path.Add(cur); last = new(x, y); cur.Add(Vector2.Transform(last, xf)); }
    public void LineTo(float x, float y) { if (cur == null) { MoveTo(x, y); return; } last = new(x, y); cur.Add(Vector2.Transform(last, xf)); }
    public void ClosePath() { if (cur != null && cur.Count > 0) { cur.Add(cur[0]); var p0 = cur[0]; cur = new List<Vector2> { p0 }; path.Add(cur); } }
    int Steps(float len) => Math.Clamp((int)(len * Scale / 3) + 4, 4, 128);
    public void QuadraticCurveTo(float cx, float cy, float x, float y)
    {
        if (cur == null) MoveTo(cx, cy);
        var p0 = last; var c = new Vector2(cx, cy); var p1 = new Vector2(x, y); int n = Steps(Vector2.Distance(p0, c) + Vector2.Distance(c, p1));
        for (int i = 1; i <= n; i++) { float t = (float)i / n, u = 1 - t; cur.Add(Vector2.Transform(u * u * p0 + 2 * u * t * c + t * t * p1, xf)); }
        last = p1;
    }
    public void BezierCurveTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
    {
        if (cur == null) MoveTo(c1x, c1y);
        var p0 = last; var a = new Vector2(c1x, c1y); var b = new Vector2(c2x, c2y); var p1 = new Vector2(x, y);
        int n = Steps(Vector2.Distance(p0, a) + Vector2.Distance(a, b) + Vector2.Distance(b, p1));
        for (int i = 1; i <= n; i++) { float t = (float)i / n, u = 1 - t; cur.Add(Vector2.Transform(u * u * u * p0 + 3 * u * u * t * a + 3 * u * t * t * b + t * t * t * p1, xf)); }
        last = p1;
    }
    public void Arc(float x, float y, float r, float a0, float a1, bool ccw = false) => Ellipse(x, y, r, r, 0, a0, a1, ccw);
    public void Ellipse(float x, float y, float rx, float ry, float rot, float a0, float a1, bool ccw = false)
    {
        float sweep = a1 - a0;
        if (!ccw) { if (sweep < 0) sweep = sweep % (MathF.PI * 2) + MathF.PI * 2; if (a1 - a0 >= MathF.PI * 2) sweep = MathF.PI * 2; }
        else { if (sweep > 0) sweep = sweep % (MathF.PI * 2) - MathF.PI * 2; if (a0 - a1 >= MathF.PI * 2) sweep = -MathF.PI * 2; }
        int n = Math.Clamp((int)(MathF.Abs(sweep) * MathF.Max(rx, ry) * Scale / 2) + 8, 8, 256);
        float cr = MathF.Cos(rot), sr = MathF.Sin(rot);
        for (int i = 0; i <= n; i++)
        {
            float a = a0 + sweep * i / n, ex = MathF.Cos(a) * rx, ey = MathF.Sin(a) * ry;
            float px = x + ex * cr - ey * sr, py = y + ex * sr + ey * cr;
            if (i == 0 && cur == null) MoveTo(px, py); else LineTo(px, py);
        }
    }
    public void Rect(float x, float y, float w, float h) { MoveTo(x, y); LineTo(x + w, y); LineTo(x + w, y + h); LineTo(x, y + h); ClosePath(); }

    public void Fill() => FillPolys(path, FillStyle);
    public void Stroke()
    {
        var polys = new List<List<Vector2>>();
        float hw = LineWidth * Scale / 2;
        foreach (var pl in path)
        {
            for (int i = 1; i < pl.Count; i++) AddSegment(polys, pl[i - 1], pl[i], hw);
            if (pl.Count > 0 && (RoundCap || hw > 1.2f)) foreach (var p in pl) AddDisc(polys, p, hw);
        }
        FillPolys(polys, StrokeStyle);
    }
    static void AddSegment(List<List<Vector2>> polys, Vector2 a, Vector2 b, float hw)
    {
        var d = b - a; float l = d.Length(); if (l < 1e-5f) return; var n = new Vector2(-d.Y, d.X) / l * hw;
        polys.Add(Orient(new List<Vector2> { a + n, b + n, b - n, a - n, a + n }));
    }
    /// <summary>All stroke pieces wind the same way, so non-zero filling unions them instead of cancelling overlaps.</summary>
    static List<Vector2> Orient(List<Vector2> pl)
    {
        float area = 0; for (int i = 0; i < pl.Count; i++) { var a = pl[i]; var b = pl[(i + 1) % pl.Count]; area += a.X * b.Y - b.X * a.Y; }
        if (area < 0) pl.Reverse();
        return pl;
    }
    static void AddDisc(List<List<Vector2>> polys, Vector2 c, float r)
    {
        int n = Math.Clamp((int)(r * 2) + 6, 6, 48); var pl = new List<Vector2>(n + 1);
        for (int i = 0; i <= n; i++) { float a = -i * MathF.PI * 2 / n; pl.Add(c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r); }
        polys.Add(Orient(pl));
    }

    public void FillRect(float x, float y, float w, float h)
    {
        if (xf.M12 == 0 && xf.M21 == 0 && FillStyle is string or Vector4 or uint)
        {
            var p0 = Vector2.Transform(new(x, y), xf); var p1 = Vector2.Transform(new(x + w, y + h), xf);
            BlendRect(MathF.Min(p0.X, p1.X), MathF.Min(p0.Y, p1.Y), MathF.Max(p0.X, p1.X), MathF.Max(p0.Y, p1.Y), StyleAt(FillStyle, 0, 0));
            return;
        }
        var save = new List<List<Vector2>>(path); var sc = cur; BeginPath(); Rect(x, y, w, h); Fill(); path.Clear(); path.AddRange(save); cur = sc;
    }
    public void StrokeRect(float x, float y, float w, float h) { var save = new List<List<Vector2>>(path); BeginPath(); Rect(x, y, w, h); Stroke(); path.Clear(); path.AddRange(save); }
    public void ClearRect(float x, float y, float w, float h)
    {
        var p0 = Vector2.Transform(new(x, y), xf); var p1 = Vector2.Transform(new(x + w, y + h), xf);
        int x0 = Math.Max(0, (int)MathF.Min(p0.X, p1.X)), x1 = Math.Min(W, (int)MathF.Ceiling(MathF.Max(p0.X, p1.X))), y0 = Math.Max(0, (int)MathF.Min(p0.Y, p1.Y)), y1 = Math.Min(H, (int)MathF.Ceiling(MathF.Max(p0.Y, p1.Y)));
        for (int yy = y0; yy < y1; yy++) Array.Clear(Px, (yy * W + x0) * 4, (x1 - x0) * 4);
    }
    public void Clear() => Array.Clear(Px);

    void BlendRect(float fx0, float fy0, float fx1, float fy1, Vector4 c)
    {
        int x0 = Math.Max(0, (int)MathF.Floor(fx0)), x1 = Math.Min(W, (int)MathF.Ceiling(fx1)), y0 = Math.Max(0, (int)MathF.Floor(fy0)), y1 = Math.Min(H, (int)MathF.Ceiling(fy1));
        for (int y = y0; y < y1; y++)
        {
            float cy = MathF.Min(fy1, y + 1) - MathF.Max(fy0, y);
            for (int x = x0; x < x1; x++) { float cx = MathF.Min(fx1, x + 1) - MathF.Max(fx0, x); Blend(x, y, c, cx * cy); }
        }
    }

    public void Blend(int x, int y, Vector4 c, float cov)
    {
        if ((uint)x >= W || (uint)y >= H) return;
        float a = c.W * cov * GlobalAlpha; if (a <= 0) return;
        int i = (y * W + x) * 4;
        if (Additive) { Px[i] = MathF.Min(1, Px[i] + c.X * a); Px[i + 1] = MathF.Min(1, Px[i + 1] + c.Y * a); Px[i + 2] = MathF.Min(1, Px[i + 2] + c.Z * a); Px[i + 3] = MathF.Min(1, Px[i + 3] + a); return; }
        float da = Px[i + 3], oa = a + da * (1 - a);
        if (oa <= 0) return;
        Px[i] = (c.X * a + Px[i] * da * (1 - a)) / oa; Px[i + 1] = (c.Y * a + Px[i + 1] * da * (1 - a)) / oa; Px[i + 2] = (c.Z * a + Px[i + 2] * da * (1 - a)) / oa; Px[i + 3] = oa;
    }

    // ---- scanline polygon fill: 5 sub-scanlines per pixel with exact horizontal coverage, non-zero winding
    float[] covRow = Array.Empty<float>();
    void FillPolys(List<List<Vector2>> polys, object style)
    {
        float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
        var edges = new List<(float x0, float y0, float x1, float y1, int dir)>();
        foreach (var pl in polys)
        {
            if (pl.Count < 2) continue;
            for (int i = 0; i < pl.Count; i++)
            {
                var a = pl[i]; var b = pl[(i + 1) % pl.Count];
                minY = MathF.Min(minY, a.Y); maxY = MathF.Max(maxY, a.Y); minX = MathF.Min(minX, a.X); maxX = MathF.Max(maxX, a.X);
                if (a.Y == b.Y) continue;
                edges.Add(a.Y < b.Y ? (a.X, a.Y, b.X, b.Y, 1) : (b.X, b.Y, a.X, a.Y, -1));
            }
        }
        if (edges.Count == 0) return;
        int py0 = Math.Max(0, (int)MathF.Floor(minY)), py1 = Math.Min(H - 1, (int)MathF.Ceiling(maxY));
        int px0 = Math.Max(0, (int)MathF.Floor(minX)), px1 = Math.Min(W - 1, (int)MathF.Ceiling(maxX));
        if (px1 < px0 || py1 < py0) return;
        if (covRow.Length < W + 2) covRow = new float[W + 2];
        const int SUB = 5;
        var xs = new List<(float x, int dir)>();
        bool solid = style is string or Vector4 or uint; Vector4 sc = solid ? StyleAt(style, 0, 0) : default;
        for (int y = py0; y <= py1; y++)
        {
            Array.Clear(covRow, px0, px1 - px0 + 2);
            for (int s = 0; s < SUB; s++)
            {
                float sy = y + (s + 0.5f) / SUB;
                xs.Clear();
                foreach (var e in edges) if (sy >= e.y0 && sy < e.y1) xs.Add((e.x0 + (sy - e.y0) / (e.y1 - e.y0) * (e.x1 - e.x0), e.dir));
                if (xs.Count < 2) continue;
                xs.Sort((a, b) => a.x.CompareTo(b.x));
                int wind = 0;
                for (int k = 0; k < xs.Count - 1; k++)
                {
                    wind += xs[k].dir;
                    if (wind == 0) continue;
                    float xa = Math.Clamp(xs[k].x, px0, px1 + 1), xb = Math.Clamp(xs[k + 1].x, px0, px1 + 1);
                    if (xb <= xa) continue;
                    int ia = (int)xa, ib = (int)xb;
                    if (ia == ib) { covRow[ia] += (xb - xa) / SUB; continue; }
                    covRow[ia] += (ia + 1 - xa) / SUB;
                    for (int x = ia + 1; x < ib; x++) covRow[x] += 1f / SUB;
                    if (ib <= px1) covRow[ib] += (xb - ib) / SUB;
                }
            }
            for (int x = px0; x <= px1; x++)
            {
                float cv = covRow[x]; if (cv <= 0.001f) continue;
                Blend(x, y, solid ? sc : StyleAt(style, x + 0.5f, y + 0.5f), MathF.Min(1, cv));
            }
        }
    }

    // ---- images
    /// <summary>Draws another canvas with the current transform (nearest sampling, alpha blended).</summary>
    public void DrawImage(Canvas2D src, float dx, float dy, float dw = -1, float dh = -1)
    {
        if (dw < 0) dw = src.W; if (dh < 0) dh = src.H;
        Matrix3x2.Invert(Matrix3x2.CreateScale(dw / src.W, dh / src.H) * Matrix3x2.CreateTranslation(dx, dy) * xf, out var inv);
        var c0 = Vector2.Transform(new(dx, dy), xf); var c1 = Vector2.Transform(new(dx + dw, dy), xf); var c2 = Vector2.Transform(new(dx, dy + dh), xf); var c3 = Vector2.Transform(new(dx + dw, dy + dh), xf);
        int x0 = Math.Max(0, (int)MathF.Min(MathF.Min(c0.X, c1.X), MathF.Min(c2.X, c3.X))), x1 = Math.Min(W - 1, (int)MathF.Max(MathF.Max(c0.X, c1.X), MathF.Max(c2.X, c3.X)));
        int y0 = Math.Max(0, (int)MathF.Min(MathF.Min(c0.Y, c1.Y), MathF.Min(c2.Y, c3.Y))), y1 = Math.Min(H - 1, (int)MathF.Max(MathF.Max(c0.Y, c1.Y), MathF.Max(c2.Y, c3.Y)));
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                var s = Vector2.Transform(new Vector2(x + 0.5f, y + 0.5f), inv); int sx = (int)MathF.Floor(s.X), sy = (int)MathF.Floor(s.Y);
                if ((uint)sx >= src.W || (uint)sy >= src.H) continue;
                int i = (sy * src.W + sx) * 4; Blend(x, y, new Vector4(src.Px[i], src.Px[i + 1], src.Px[i + 2], src.Px[i + 3]), 1);
            }
    }

    /// <summary>Text, rendered through the GDI font atlas.</summary>
    public void FillText(string text, float x, float y, UiFont font, string align = "left", string baseline = "alphabetic")
    {
        if (font == null || string.IsNullOrEmpty(text)) return;
        float w = font.Measure(text);
        if (align == "center") x -= w / 2; else if (align == "right") x -= w;
        y -= baseline switch { "middle" => font.Ascent * 0.5f + font.Size * 0.15f, "top" => 0, _ => font.Ascent };
        var c = StyleAt(FillStyle, x, y);
        font.Raster(text, (gx, gy, a) => { var p = Vector2.Transform(new Vector2(x + gx, y + gy), xf); Blend((int)p.X, (int)p.Y, c, a); });
    }

    public void CopyTo(byte[] rgba)
    {
        for (int i = 0; i < W * H * 4; i++) rgba[i] = (byte)(Math.Clamp(Px[i], 0, 1) * 255 + 0.5f);
    }
    public Texture ToTexture(bool repeat = false) => Texture.From(this, repeat);
    public void Update(Texture t) { CopyTo(t.Pixels); t.Version++; }
}

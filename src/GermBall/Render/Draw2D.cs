using System;
using System.Collections.Generic;
using System.Numerics;
using Vortice.Mathematics;

namespace GermBall.Render;

/// <summary>Immediate-mode 2D drawing for the HUD and menus, batched into a few draw calls per frame.</summary>
public sealed class Draw2D
{
    Renderer.UIVert[] v = new Renderer.UIVert[16384];
    int n;
    readonly List<(Texture tex, int start, int count, Rect? clip)> batches = new();
    Texture curTex; Rect? curClip; int batchStart;
    readonly Stack<Rect?> clips = new();
    public float Alpha = 1;
    public UiFont Display, DisplayBig, DisplayHuge, Mono, MonoSmall, DisplaySmall, DisplayMid, DisplayNum, DisplayTitle;
    public float Scale = 1;   // UI scale for high-DPI screens

    public void Begin() { n = 0; batches.Clear(); curTex = null; curClip = null; batchStart = 0; clips.Clear(); Alpha = 1; }
    public void End(IRenderer r) { Flush(); r.DrawUI(batches, v, n); }

    void Flush() { if (n > batchStart) batches.Add((curTex, batchStart, n - batchStart, curClip)); batchStart = n; }
    void Use(Texture t) { if (t != curTex) { Flush(); curTex = t; } }
    public void PushClip(float x, float y, float w, float h)
    {
        Flush(); clips.Push(curClip);
        var r = new Rect(x * Scale, y * Scale, w * Scale, h * Scale);
        if (curClip is Rect c) { float x0 = MathF.Max(c.X, r.X), y0 = MathF.Max(c.Y, r.Y), x1 = MathF.Min(c.X + c.Width, r.X + r.Width), y1 = MathF.Min(c.Y + c.Height, r.Y + r.Height); r = new Rect(x0, y0, MathF.Max(0, x1 - x0), MathF.Max(0, y1 - y0)); }
        curClip = r;
    }
    public void PopClip() { Flush(); curClip = clips.Count > 0 ? clips.Pop() : null; }

    void Vert(float x, float y, float u, float vv, Vector4 c)
    {
        if (n >= v.Length) Array.Resize(ref v, v.Length * 2);
        v[n++] = new Renderer.UIVert { X = x * Scale, Y = y * Scale, U = u, V = vv, R = c.X, G = c.Y, B = c.Z, A = c.W * Alpha };
    }
    public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 col, Texture t = null, float u0 = 0, float v0 = 0, float u1 = 1, float v1 = 1)
    {
        Use(t);
        Vert(a.X, a.Y, u0, v0, col); Vert(b.X, b.Y, u1, v0, col); Vert(c.X, c.Y, u1, v1, col);
        Vert(a.X, a.Y, u0, v0, col); Vert(c.X, c.Y, u1, v1, col); Vert(d.X, d.Y, u0, v1, col);
    }
    public void Tri(Vector2 a, Vector2 b, Vector2 c, Vector4 col) { Use(null); Vert(a.X, a.Y, 0, 0, col); Vert(b.X, b.Y, 0, 0, col); Vert(c.X, c.Y, 0, 0, col); }
    public void Rect(float x, float y, float w, float h, Vector4 c) => Quad(new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h), c);
    public void Image(Texture t, float x, float y, float w, float h, Vector4? tint = null, float u0 = 0, float v0 = 0, float u1 = 1, float v1 = 1)
        => Quad(new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h), tint ?? Vector4.One, t, u0, v0, u1, v1);
    public void Frame(float x, float y, float w, float h, Vector4 c, float t = 1)
    {
        // lines at least one real pixel thick, snapped to the pixel grid: at UI scales under 1 a thin edge could fall between
        // pixel centres and vanish (buttons lost their bottom border at 720p)
        float sc = MathF.Max(0.01f, Scale); t = MathF.Max(t, 1 / sc);
        float x0 = MathF.Round(x * sc) / sc, y0 = MathF.Round(y * sc) / sc, x1 = MathF.Round((x + w) * sc) / sc, y1 = MathF.Round((y + h) * sc) / sc;
        Rect(x0, y0, x1 - x0, t, c); Rect(x0, y1 - t, x1 - x0, t, c); Rect(x0, y0 + t, t, y1 - y0 - 2 * t, c); Rect(x1 - t, y0 + t, t, y1 - y0 - 2 * t, c);
    }
    public void Line(Vector2 a, Vector2 b, Vector4 c, float w = 1)
    {
        var d = b - a; float l = d.Length(); if (l < 1e-4f) return; var nn = new Vector2(-d.Y, d.X) / l * (w / 2);
        Quad(a + nn, b + nn, b - nn, a - nn, c);
    }
    public void Circle(float cx, float cy, float r, Vector4 c, int segs = 24)
    {
        for (int i = 0; i < segs; i++) { float a0 = i * MathF.Tau / segs, a1 = (i + 1) * MathF.Tau / segs; Tri(new(cx, cy), new(cx + MathF.Cos(a0) * r, cy + MathF.Sin(a0) * r), new(cx + MathF.Cos(a1) * r, cy + MathF.Sin(a1) * r), c); }
    }
    public void Ring(float cx, float cy, float r, Vector4 c, float w = 1, int segs = 32)
    {
        for (int i = 0; i < segs; i++) { float a0 = i * MathF.Tau / segs, a1 = (i + 1) * MathF.Tau / segs; Line(new(cx + MathF.Cos(a0) * r, cy + MathF.Sin(a0) * r), new(cx + MathF.Cos(a1) * r, cy + MathF.Sin(a1) * r), c, w); }
    }
    /// <summary>A parallelogram bar skewed like the original's CSS skewX(-18deg) meters.</summary>
    public void Skew(float x, float y, float w, float h, Vector4 c, float skew = 0.325f)
    {
        float s = h * skew; Quad(new(x + s, y), new(x + w + s, y), new(x + w, y + h), new(x, y + h), c);
    }

    public float Text(string s, float x, float y, UiFont f, Vector4 c, string align = "left", float spacing = 0, bool shadow = false, Vector4? shadowCol = null)
    {
        if (string.IsNullOrEmpty(s) || f == null) return 0;
        float w = f.Measure(s, spacing);
        if (align == "center") x -= w / 2; else if (align == "right") x -= w;
        x = MathF.Round(x * Scale) / Scale; y = MathF.Round(y * Scale) / Scale;
        if (shadow) DrawRun(s, x + 2, y + 2, f, shadowCol ?? new Vector4(0, 0, 0, 0.6f), spacing);
        DrawRun(s, x, y, f, c, spacing);
        return w;
    }
    void DrawRun(string s, float x, float y, UiFont f, Vector4 c, float spacing)
    {
        var at = f.Atlas; float iw = 1f / at.Width, ih = 1f / at.Height;
        foreach (var ch in s)
        {
            if (!f.TryGlyph(ch, out var g)) continue;
            if (ch != ' ') Image(at, x, y, g.W / f.Px, g.H / f.Px, c, g.X * iw, g.Y * ih, (g.X + g.W) * iw, (g.Y + g.H) * ih);
            x += g.Advance / f.Px + spacing;
        }
    }
    /// <summary>Word-wrapped text; returns the height used.</summary>
    public float Wrap(string s, float x, float y, float width, UiFont f, Vector4 c, float lineGap = 1.35f)
    {
        float lh = f.Size * lineGap, yy = y;
        foreach (var para in s.Split('\n'))
        {
            var words = para.Split(' '); string line = "";
            foreach (var wd in words)
            {
                var t = line.Length == 0 ? wd : line + " " + wd;
                if (f.Measure(t) > width && line.Length > 0) { Text(line, x, yy, f, c); yy += lh; line = wd; } else line = t;
            }
            Text(line, x, yy, f, c); yy += lh;
        }
        return yy - y;
    }
    public static Vector4 Col(uint hex, float a = 1) => new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, a);
}

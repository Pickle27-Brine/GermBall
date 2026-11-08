using System;
using System.Collections.Generic;
using System.Numerics;

namespace GermBall.Engine;

[Flags]
public enum DrawFlags
{
    None = 0,
    /// <summary>Overlay a 1 m world-space grid (floors and test geometry, so distances and heights can be read at a glance).</summary>
    Grid = 1,
    /// <summary>Ignore lighting: the colour is drawn as is (effects, markers, zone volumes).</summary>
    Unlit = 2,
    /// <summary>Drawn last, over everything, ignoring depth (editor gizmos, bones, selection outlines).</summary>
    OnTop = 4,
}

public struct DrawItem
{
    public Matrix4x4 World;
    public Vector4 Color;   // alpha below 1 draws in the transparent pass
    public MeshId Mesh;
    public DrawFlags Flags;
    /// <summary>A mesh of its own (tapered and curved shapes) instead of a built-in one.</summary>
    public Mesh Custom;
    public Mesh Geometry => Custom ?? GermBall.Engine.Mesh.Get(Mesh);
}

/// <summary>Everything a renderer needs for one frame: the camera, the lighting, the 3D items and the 2D overlay.
/// The game fills it; the D3D11 renderer and the test's software renderer both consume it.</summary>
public sealed class DrawList
{
    public Matrix4x4 View, Proj;
    public Vector3 CamPos;
    public Vector3 SunDir = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.3f));
    public Vector3 SkyColor = new(0.55f, 0.66f, 0.78f), GroundColor = new(0.30f, 0.28f, 0.26f), FogColor = new(0.62f, 0.70f, 0.80f);
    public float FogStart = 80, FogEnd = 260;
    /// <summary>The 3D view's rectangle in window pixels (x, y, width, height); zero width means the whole window.
    /// The 2D overlay always covers the whole window.</summary>
    public Vector4 Viewport;
    public Vector4 ViewportOr(int w, int h) => Viewport.Z > 0 && Viewport.W > 0 ? Viewport : new Vector4(0, 0, w, h);
    public readonly List<DrawItem> Items = new();
    public readonly Overlay Overlay = new();

    public void Clear() { Items.Clear(); Overlay.Clear(); }

    /// <summary>A ray from the camera through a window pixel (for picking in the editors).</summary>
    public (Vector3 origin, Vector3 dir) ScreenRay(float x, float y, int w, int h)
    {
        var vp = ViewportOr(w, h);
        float nx = (x - vp.X) / vp.Z * 2 - 1, ny = 1 - (y - vp.Y) / vp.W * 2;
        Matrix4x4.Invert(View * Proj, out var inv);
        var a = Vector4.Transform(new Vector4(nx, ny, 0, 1), inv); var b = Vector4.Transform(new Vector4(nx, ny, 1, 1), inv);
        var pa = new Vector3(a.X, a.Y, a.Z) / a.W; var pb = new Vector3(b.X, b.Y, b.Z) / b.W;
        return (pa, Vector3.Normalize(pb - pa));
    }

    public void Add(MeshId mesh, Matrix4x4 world, Vector4 color, DrawFlags flags = DrawFlags.None) =>
        Items.Add(new DrawItem { Mesh = mesh, World = world, Color = color, Flags = flags });
    public void AddCustom(Mesh mesh, Matrix4x4 world, Vector4 color, DrawFlags flags = DrawFlags.None) =>
        Items.Add(new DrawItem { Mesh = MeshId.Count, Custom = mesh, World = world, Color = color, Flags = flags });
    public void Box(Vector3 center, Vector3 size, Quaternion rot, Vector4 color, DrawFlags flags = DrawFlags.None) =>
        Add(MeshId.Cube, MathX.Trs(center, rot, size), color, flags);
    public void Box(Vector3 center, Vector3 size, Vector4 color, DrawFlags flags = DrawFlags.None) =>
        Add(MeshId.Cube, MathX.Ts(center, size), color, flags);
    public void Sphere(Vector3 center, float radius, Vector4 color, DrawFlags flags = DrawFlags.None) =>
        Add(MeshId.Sphere, MathX.Ts(center, new Vector3(radius * 2)), color, flags);
    /// <summary>A cylinder (or cone) from point a to point b.</summary>
    public void Rod(Vector3 a, Vector3 b, float radius, Vector4 color, DrawFlags flags = DrawFlags.None, MeshId mesh = MeshId.Cylinder)
    {
        var d = b - a; float len = d.Length(); if (len < 1e-5f) return;
        Add(mesh, MathX.Trs((a + b) * 0.5f, MathX.FromUpTo(d), new Vector3(radius * 2, len, radius * 2)), color, flags);
    }

    /// <summary>World position → pixel position (false when behind the camera).</summary>
    public bool Project(Vector3 p, int w, int h, out Vector2 screen, out float depth)
    {
        var c = Vector4.Transform(new Vector4(p, 1), View * Proj);
        screen = default; depth = c.W;
        if (c.W < 0.05f) return false;
        var vp = ViewportOr(w, h);
        screen = new Vector2(vp.X + (c.X / c.W * 0.5f + 0.5f) * vp.Z, vp.Y + (0.5f - c.Y / c.W * 0.5f) * vp.W);
        return true;
    }
}

/// <summary>2D overlay in pixels (origin top-left): solid rectangles only. Text is built from rectangles with a 5×7 pixel font,
/// so a renderer needs no textures or font files at all.</summary>
public sealed class Overlay
{
    public struct Quad { public float X, Y, W, H; public Vector4 Color; }
    public readonly List<Quad> Quads = new();
    public void Clear() { Quads.Clear(); Clip = null; }

    /// <summary>When set (x, y, width, height), rectangles are clipped to it (scrolling panels).</summary>
    public Vector4? Clip;
    public void Rect(float x, float y, float w, float h, Vector4 c)
    {
        if (Clip is Vector4 cl)
        {
            float x1 = MathF.Min(x + w, cl.X + cl.Z), y1 = MathF.Min(y + h, cl.Y + cl.W);
            x = MathF.Max(x, cl.X); y = MathF.Max(y, cl.Y); w = x1 - x; h = y1 - y;
        }
        if (w > 0 && h > 0 && c.W > 0) Quads.Add(new Quad { X = x, Y = y, W = w, H = h, Color = c });
    }
    public void Frame(float x, float y, float w, float h, float t, Vector4 c)
    { Rect(x, y, w, t, c); Rect(x, y + h - t, w, t, c); Rect(x, y + t, t, h - 2 * t, c); Rect(x + w - t, y + t, t, h - 2 * t, c); }

    public static float TextWidth(string s, float scale) => s.Length == 0 ? 0 : (s.Length * 6 - 1) * scale;
    public static float LineHeight(float scale) => 10 * scale;

    /// <summary>Draws text with its top-left at (x, y). Each glyph pixel is <paramref name="scale"/> pixels square. Returns the width.</summary>
    public float Text(float x, float y, string s, float scale, Vector4 c)
    {
        x = MathF.Round(x); y = MathF.Round(y);
        float cx = x;
        foreach (char ch in s)
        {
            if (ch == '\n') { y += LineHeight(scale); cx = x; continue; }
            var g = Font5x7.Glyph(ch);
            for (int row = 0; row < 7; row++)
            {
                // merge runs of lit pixels in a row into one rectangle
                int col = 0;
                while (col < 5)
                {
                    if ((g[col] >> row & 1) == 0) { col++; continue; }
                    int start = col; while (col < 5 && (g[col] >> row & 1) != 0) col++;
                    Rect(cx + start * scale, y + row * scale, (col - start) * scale, scale, c);
                }
            }
            cx += 6 * scale;
        }
        return cx - x - scale;
    }

    /// <summary>Text with a 1-pixel drop shadow, readable over any background.</summary>
    public float TextShadow(float x, float y, string s, float scale, Vector4 c)
    {
        Text(x + scale, y + scale, s, scale, new Vector4(0, 0, 0, c.W * 0.75f));
        return Text(x, y, s, scale, c);
    }
    public void TextCentered(float cx, float y, string s, float scale, Vector4 c) => TextShadow(cx - TextWidth(s, scale) / 2, y, s, scale, c);
}

/// <summary>A classic 5×7 pixel font for ASCII 32–126. Five column bytes per glyph, bit 0 at the top.</summary>
public static class Font5x7
{
    static readonly byte[] data =
    {
        0x00,0x00,0x00,0x00,0x00, 0x00,0x00,0x5F,0x00,0x00, 0x00,0x07,0x00,0x07,0x00, 0x14,0x7F,0x14,0x7F,0x14, // space ! " #
        0x24,0x2A,0x7F,0x2A,0x12, 0x23,0x13,0x08,0x64,0x62, 0x36,0x49,0x56,0x20,0x50, 0x00,0x00,0x07,0x00,0x00, // $ % & '
        0x00,0x1C,0x22,0x41,0x00, 0x00,0x41,0x22,0x1C,0x00, 0x2A,0x1C,0x7F,0x1C,0x2A, 0x08,0x08,0x3E,0x08,0x08, // ( ) * +
        0x00,0x50,0x30,0x00,0x00, 0x08,0x08,0x08,0x08,0x08, 0x00,0x60,0x60,0x00,0x00, 0x20,0x10,0x08,0x04,0x02, // , - . /
        0x3E,0x51,0x49,0x45,0x3E, 0x00,0x42,0x7F,0x40,0x00, 0x42,0x61,0x51,0x49,0x46, 0x21,0x41,0x45,0x4B,0x31, // 0 1 2 3
        0x18,0x14,0x12,0x7F,0x10, 0x27,0x45,0x45,0x45,0x39, 0x3C,0x4A,0x49,0x49,0x30, 0x01,0x71,0x09,0x05,0x03, // 4 5 6 7
        0x36,0x49,0x49,0x49,0x36, 0x06,0x49,0x49,0x29,0x1E, 0x00,0x36,0x36,0x00,0x00, 0x00,0x56,0x36,0x00,0x00, // 8 9 : ;
        0x08,0x14,0x22,0x41,0x00, 0x14,0x14,0x14,0x14,0x14, 0x00,0x41,0x22,0x14,0x08, 0x02,0x01,0x51,0x09,0x06, // < = > ?
        0x32,0x49,0x79,0x41,0x3E, 0x7E,0x11,0x11,0x11,0x7E, 0x7F,0x49,0x49,0x49,0x36, 0x3E,0x41,0x41,0x41,0x22, // @ A B C
        0x7F,0x41,0x41,0x22,0x1C, 0x7F,0x49,0x49,0x49,0x41, 0x7F,0x09,0x09,0x09,0x01, 0x3E,0x41,0x49,0x49,0x7A, // D E F G
        0x7F,0x08,0x08,0x08,0x7F, 0x00,0x41,0x7F,0x41,0x00, 0x20,0x40,0x41,0x3F,0x01, 0x7F,0x08,0x14,0x22,0x41, // H I J K
        0x7F,0x40,0x40,0x40,0x40, 0x7F,0x02,0x0C,0x02,0x7F, 0x7F,0x04,0x08,0x10,0x7F, 0x3E,0x41,0x41,0x41,0x3E, // L M N O
        0x7F,0x09,0x09,0x09,0x06, 0x3E,0x41,0x51,0x21,0x5E, 0x7F,0x09,0x19,0x29,0x46, 0x46,0x49,0x49,0x49,0x31, // P Q R S
        0x01,0x01,0x7F,0x01,0x01, 0x3F,0x40,0x40,0x40,0x3F, 0x1F,0x20,0x40,0x20,0x1F, 0x3F,0x40,0x38,0x40,0x3F, // T U V W
        0x63,0x14,0x08,0x14,0x63, 0x07,0x08,0x70,0x08,0x07, 0x61,0x51,0x49,0x45,0x43, 0x00,0x7F,0x41,0x41,0x00, // X Y Z [
        0x02,0x04,0x08,0x10,0x20, 0x00,0x41,0x41,0x7F,0x00, 0x04,0x02,0x01,0x02,0x04, 0x40,0x40,0x40,0x40,0x40, // \ ] ^ _
        0x00,0x01,0x02,0x04,0x00, 0x20,0x54,0x54,0x54,0x78, 0x7F,0x48,0x44,0x44,0x38, 0x38,0x44,0x44,0x44,0x20, // ` a b c
        0x38,0x44,0x44,0x48,0x7F, 0x38,0x54,0x54,0x54,0x18, 0x08,0x7E,0x09,0x01,0x02, 0x0C,0x52,0x52,0x52,0x3E, // d e f g
        0x7F,0x08,0x04,0x04,0x78, 0x00,0x44,0x7D,0x40,0x00, 0x20,0x40,0x44,0x3D,0x00, 0x7F,0x10,0x28,0x44,0x00, // h i j k
        0x00,0x41,0x7F,0x40,0x00, 0x7C,0x04,0x18,0x04,0x78, 0x7C,0x08,0x04,0x04,0x78, 0x38,0x44,0x44,0x44,0x38, // l m n o
        0x7C,0x14,0x14,0x14,0x08, 0x08,0x14,0x14,0x18,0x7C, 0x7C,0x08,0x04,0x04,0x08, 0x48,0x54,0x54,0x54,0x20, // p q r s
        0x04,0x3F,0x44,0x40,0x20, 0x3C,0x40,0x40,0x20,0x7C, 0x1C,0x20,0x40,0x20,0x1C, 0x3C,0x40,0x30,0x40,0x3C, // t u v w
        0x44,0x28,0x10,0x28,0x44, 0x0C,0x50,0x50,0x50,0x3C, 0x44,0x64,0x54,0x4C,0x44, 0x00,0x08,0x36,0x41,0x00, // x y z {
        0x00,0x00,0x7F,0x00,0x00, 0x00,0x41,0x36,0x08,0x00, 0x10,0x08,0x08,0x10,0x08,                           // | } ~
    };
    static readonly byte[] unknown = { 0x7F, 0x41, 0x41, 0x41, 0x7F };

    public static ReadOnlySpan<byte> Glyph(char c)
    {
        int i = c - 32;
        if (i < 0 || i >= 95) return unknown;
        return new ReadOnlySpan<byte>(data, i * 5, 5);
    }
}

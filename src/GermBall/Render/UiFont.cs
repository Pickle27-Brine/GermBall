using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GermBall.Render;

/// <summary>
/// A font baked into a texture atlas with Windows GDI. The original used Saira Condensed and Share Tech Mono from Google Fonts;
/// here the display face is Bahnschrift (condensed, ships with Windows 10/11) and the mono face Consolas, both always present.
/// </summary>
public sealed unsafe class UiFont
{
    public readonly float Size, Ascent, LineHeight;
    public readonly Texture Atlas;
    readonly Dictionary<char, Glyph> glyphs = new();
    public struct Glyph { public int X, Y, W, H; public float Advance; }

    const string Chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·—–…←↑→↓°×±éèàüöä▲▼◆●○■□★♪";

    [DllImport("gdi32", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [DllImport("gdi32")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32")] static extern uint SetTextColor(IntPtr dc, uint c);
    [DllImport("gdi32")] static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32", CharSet = CharSet.Unicode)] static extern bool TextOutW(IntPtr dc, int x, int y, string s, int n);
    [DllImport("gdi32", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32W(IntPtr dc, string s, int n, out SIZE sz);
    [DllImport("gdi32", CharSet = CharSet.Unicode)] static extern bool GetTextMetricsW(IntPtr dc, out TEXTMETRIC tm);
    [DllImport("gdi32")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TEXTMETRIC
    {
        public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading, tmAveCharWidth, tmMaxCharWidth, tmWeight, tmOverhang, tmDigitizedAspectX, tmDigitizedAspectY;
        public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar; public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; public int colors; }

    public UiFont(string face, int px, int weight = 400)
    {
        Size = px;
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        IntPtr font = CreateFontW(-px, 0, 0, 0, weight, 0, 0, 0, 1 /*DEFAULT_CHARSET*/, 0, 0, 4 /*ANTIALIASED_QUALITY*/, 0, face);
        SelectObject(dc, font);
        GetTextMetricsW(dc, out var tm);
        Ascent = tm.tmAscent; LineHeight = tm.tmHeight + tm.tmExternalLeading;
        int cell = tm.tmHeight + 4, cols = 16, rows = (Chars.Length + cols - 1) / cols;
        int maxW = 0; var adv = new Dictionary<char, int>();
        foreach (var ch in Chars) { GetTextExtentPoint32W(dc, ch.ToString(), 1, out var sz); adv[ch] = sz.cx; maxW = Math.Max(maxW, sz.cx); }
        int cw = maxW + 4, W = NextPow2(cw * cols), H = NextPow2(cell * rows);
        var bmi = new BITMAPINFO { biSize = 40, biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32 };
        IntPtr bmp = CreateDIBSection(dc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        SelectObject(dc, bmp); SetTextColor(dc, 0xFFFFFF); SetBkMode(dc, 1);
        for (int i = 0; i < Chars.Length; i++)
        {
            int x = (i % cols) * cw + 2, y = (i / cols) * cell + 2; char ch = Chars[i];
            TextOutW(dc, x, y, ch.ToString(), 1);
            glyphs[ch] = new Glyph { X = x, Y = y, W = adv[ch], H = tm.tmHeight, Advance = adv[ch] };
        }
        Atlas = new Texture(W, H) { Nearest = false, Mipmaps = false };
        byte* src = (byte*)bits;
        for (int i = 0; i < W * H; i++)
        {
            byte a = Math.Max(src[i * 4], Math.Max(src[i * 4 + 1], src[i * 4 + 2]));
            Atlas.Pixels[i * 4] = Atlas.Pixels[i * 4 + 1] = Atlas.Pixels[i * 4 + 2] = 255; Atlas.Pixels[i * 4 + 3] = a;
        }
        DeleteObject(bmp); DeleteObject(font); DeleteDC(dc);
    }
    static int NextPow2(int v) { int p = 64; while (p < v) p *= 2; return p; }

    public bool TryGlyph(char c, out Glyph g) => glyphs.TryGetValue(c, out g) || glyphs.TryGetValue('?', out g);
    public float Measure(string s, float spacing = 0)
    {
        float w = 0; foreach (var c in s) if (TryGlyph(c, out var g)) w += g.Advance + spacing;
        return s.Length > 0 ? w - spacing : 0;
    }

    /// <summary>Rasterises text into a callback (x, y relative to the top-left of the line, coverage 0..1), for software canvases.</summary>
    public void Raster(string s, Action<int, int, float> put)
    {
        float x = 0;
        foreach (var c in s)
        {
            if (!TryGlyph(c, out var g)) continue;
            for (int yy = 0; yy < g.H; yy++) for (int xx = 0; xx < g.W; xx++)
                {
                    byte a = Atlas.Pixels[((g.Y + yy) * Atlas.Width + g.X + xx) * 4 + 3];
                    if (a > 0) put((int)x + xx, yy, a / 255f);
                }
            x += g.Advance;
        }
    }
}

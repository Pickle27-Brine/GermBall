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
    /// <summary>Atlas pixels per logical pixel (fonts are baked oversampled so scaled-up UI stays sharp).</summary>
    public float Px = 1;
    public static int Oversample = 2;
    public readonly Texture Atlas;
    readonly Dictionary<char, Glyph> glyphs = new();
    public struct Glyph { public int X, Y, W, H; public float Advance; }

    const string Chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·—–…←↑→↓°×±éèàüöä▲▼◆●○■□★♪∞♥✓✕•’‘“”⚠";

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
    [DllImport("gdi32", CharSet = CharSet.Unicode)] static extern int AddFontResourceExW(string path, uint fl, IntPtr res);
    [DllImport("gdi32", CharSet = CharSet.Unicode)] static extern uint GetGlyphIndicesW(IntPtr dc, string s, int n, ushort[] idx, uint flags);
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

    /// <summary>Overrides how fonts are made (the headless tools bake with Pillow instead of GDI).</summary>
    public static Func<string, int, int, UiFont> Factory;
    /// <summary>Registers the game's own fonts (data/fonts/*.ttf) for this process only, so they work without being installed.</summary>
    public static void LoadPrivate(string dir)
    {
        if (!OperatingSystem.IsWindows() || !System.IO.Directory.Exists(dir)) return;
        foreach (var f in System.IO.Directory.GetFiles(dir, "*.ttf")) try { AddFontResourceExW(f, 0x10 /*FR_PRIVATE*/, IntPtr.Zero); } catch { }
    }
    /// <summary>Symbols drawn from this font when the chosen face doesn't have them (arrows, hearts, ticks...).</summary>
    public const string FallbackFace = "Segoe UI Symbol";
    public static UiFont Create(string face, int px, int weight = 400) => Factory?.Invoke(face, px, weight) ?? new UiFont(face, px, weight);

    /// <summary>A font from a pre-baked atlas.</summary>
    public UiFont(float size, float ascent, float lineHeight, Texture atlas, Dictionary<char, Glyph> g, float px = 1)
    {
        Size = size; Ascent = ascent; LineHeight = lineHeight; Atlas = atlas; glyphs = g; Px = px;
    }

    public UiFont(string face, int logicalPx, int weight = 400)
    {
        Px = Oversample; int px = logicalPx * Oversample;
        Size = logicalPx;
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        IntPtr font = CreateFontW(-px, 0, 0, 0, weight, 0, 0, 0, 1 /*DEFAULT_CHARSET*/, 0, 0, 4 /*ANTIALIASED_QUALITY*/, 0, face);
        SelectObject(dc, font);
        GetTextMetricsW(dc, out var tm);
        Ascent = tm.tmAscent / Px; LineHeight = (tm.tmHeight + tm.tmExternalLeading) / Px;
        int cell = tm.tmHeight + 4, cols = 16, rows = (Chars.Length + cols - 1) / cols;
        int maxW = 0; var adv = new Dictionary<char, int>();
        // which characters this face lacks: those are measured and drawn with the fallback symbol font
        var gi = new ushort[Chars.Length]; GetGlyphIndicesW(dc, Chars, Chars.Length, gi, 1 /*GGI_MARK_NONEXISTING_GLYPHS*/);
        IntPtr fb = CreateFontW(-px, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 4, 0, FallbackFace);
        var missing = new HashSet<char>(); for (int i = 0; i < Chars.Length; i++) if (gi[i] == 0xFFFF && Chars[i] != ' ') missing.Add(Chars[i]);
        foreach (var ch in Chars)
        {
            if (missing.Contains(ch)) SelectObject(dc, fb);
            GetTextExtentPoint32W(dc, ch.ToString(), 1, out var sz); adv[ch] = sz.cx; maxW = Math.Max(maxW, sz.cx);
            if (missing.Contains(ch)) SelectObject(dc, font);
        }
        int cw = maxW + 4, W = NextPow2(cw * cols), H = NextPow2(cell * rows);
        var bmi = new BITMAPINFO { biSize = 40, biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32 };
        IntPtr bmp = CreateDIBSection(dc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        SelectObject(dc, bmp); SetTextColor(dc, 0xFFFFFF); SetBkMode(dc, 1);
        for (int i = 0; i < Chars.Length; i++)
        {
            int x = (i % cols) * cw + 2, y = (i / cols) * cell + 2; char ch = Chars[i];
            if (missing.Contains(ch)) { SelectObject(dc, fb); TextOutW(dc, x, y, ch.ToString(), 1); SelectObject(dc, font); }
            else TextOutW(dc, x, y, ch.ToString(), 1);
            glyphs[ch] = new Glyph { X = x, Y = y, W = adv[ch], H = tm.tmHeight, Advance = adv[ch] };
        }
        Atlas = new Texture(W, H) { Nearest = false, Mipmaps = false };
        byte* src = (byte*)bits;
        for (int i = 0; i < W * H; i++)
        {
            byte a = Math.Max(src[i * 4], Math.Max(src[i * 4 + 1], src[i * 4 + 2]));
            Atlas.Pixels[i * 4] = Atlas.Pixels[i * 4 + 1] = Atlas.Pixels[i * 4 + 2] = 255; Atlas.Pixels[i * 4 + 3] = a;
        }
        DeleteObject(bmp); DeleteObject(font); DeleteObject(fb); DeleteDC(dc);
    }
    static int NextPow2(int v) { int p = 64; while (p < v) p *= 2; return p; }

    public bool TryGlyph(char c, out Glyph g) => glyphs.TryGetValue(c, out g) || glyphs.TryGetValue('?', out g);
    public float Measure(string s, float spacing = 0)
    {
        float w = 0; foreach (var c in s) if (TryGlyph(c, out var g)) w += g.Advance / Px + spacing;
        return s.Length > 0 ? w - spacing : 0;
    }

    /// <summary>Rasterises text into a callback (x, y relative to the top-left of the line, coverage 0..1), for software canvases.</summary>
    public void Raster(string s, Action<int, int, float> put)
    {
        float x = 0; int k = Math.Max(1, (int)MathF.Round(Px));
        foreach (var c in s)
        {
            if (!TryGlyph(c, out var g)) continue;
            int gw = g.W / k, gh = g.H / k;
            for (int yy = 0; yy < gh; yy++) for (int xx = 0; xx < gw; xx++)
                {
                    float a = 0;   // box-filter the oversampled atlas down to logical pixels
                    for (int sy = 0; sy < k; sy++) for (int sx = 0; sx < k; sx++) a += Atlas.Pixels[((g.Y + yy * k + sy) * Atlas.Width + g.X + xx * k + sx) * 4 + 3];
                    a /= 255f * k * k;
                    if (a > 0) put((int)x + xx, yy, a);
                }
            x += g.Advance / Px;
        }
    }
}

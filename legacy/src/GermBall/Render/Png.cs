using System;
using System.IO;
using System.IO.Compression;

namespace GermBall.Render;

/// <summary>Loads non-interlaced 8-bit PNGs (grey, RGB, RGBA, grey+alpha, palette) into a <see cref="Texture"/>, so images in data\ can be swapped by players.</summary>
public static class Png
{
    public static Texture Load(string path)
    {
        try
        {
            var b = File.ReadAllBytes(path); int p = 8, w = 0, h = 0, ct = 0, depth = 0, interlace = 0; var idat = new MemoryStream(); byte[] pal = null, trns = null;
            static int BE(byte[] a, int i) => (a[i] << 24) | (a[i + 1] << 16) | (a[i + 2] << 8) | a[i + 3];
            while (p + 8 <= b.Length)
            {
                int len = BE(b, p); string type = System.Text.Encoding.ASCII.GetString(b, p + 4, 4); int d = p + 8;
                if (type == "IHDR") { w = BE(b, d); h = BE(b, d + 4); depth = b[d + 8]; ct = b[d + 9]; interlace = b[d + 12]; }
                else if (type == "PLTE") { pal = new byte[len]; Array.Copy(b, d, pal, 0, len); }
                else if (type == "tRNS") { trns = new byte[len]; Array.Copy(b, d, trns, 0, len); }
                else if (type == "IDAT") idat.Write(b, d, len);
                else if (type == "IEND") break;
                p = d + len + 4;
            }
            if (depth != 8 || interlace != 0) throw new NotSupportedException("only 8-bit non-interlaced PNGs");
            int bpp = ct switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException("colour type " + ct) };
            idat.Position = 0; using var z = new ZLibStream(idat, CompressionMode.Decompress); var raw = new MemoryStream(); z.CopyTo(raw); var data = raw.ToArray();
            int stride = w * bpp; var cur = new byte[stride]; var prev = new byte[stride]; var t = new Texture(w, h) { Nearest = false };
            for (int y = 0; y < h; y++)
            {
                int o = y * (stride + 1), f = data[o];
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? cur[x - bpp] : 0, up = prev[x], c = x >= bpp ? prev[x - bpp] : 0, v = data[o + 1 + x];
                    int r = f switch { 1 => v + a, 2 => v + up, 3 => v + ((a + up) >> 1), 4 => v + Paeth(a, up, c), _ => v };
                    cur[x] = (byte)r;
                }
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4; byte R, G, B, A = 255;
                    switch (ct)
                    {
                        case 0: R = G = B = cur[x]; break;
                        case 2: R = cur[x * 3]; G = cur[x * 3 + 1]; B = cur[x * 3 + 2]; break;
                        case 3: { int k = cur[x]; R = pal[k * 3]; G = pal[k * 3 + 1]; B = pal[k * 3 + 2]; if (trns != null && k < trns.Length) A = trns[k]; break; }
                        case 4: R = G = B = cur[x * 2]; A = cur[x * 2 + 1]; break;
                        default: R = cur[x * 4]; G = cur[x * 4 + 1]; B = cur[x * 4 + 2]; A = cur[x * 4 + 3]; break;
                    }
                    t.Pixels[i] = R; t.Pixels[i + 1] = G; t.Pixels[i + 2] = B; t.Pixels[i + 3] = A;
                }
                (prev, cur) = (cur, prev);
            }
            return t;
        }
        catch (Exception e) { Platform.Log.Warn("PNG " + path + ": " + e.Message); return null; }
    }
    static int Paeth(int a, int b, int c) { int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
}

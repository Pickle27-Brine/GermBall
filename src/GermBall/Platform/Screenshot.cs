using System;
using System.IO;
using System.IO.Compression;
using GermBall.Render;

namespace GermBall.Platform;

/// <summary>F12 saves a PNG to Documents\My Games\GermBall\screenshots (or wherever paths.json points).</summary>
public static class Screenshot
{
    static bool pending;
    public static void Take(IRenderer r) => pending = true;
    /// <summary>Called by screens after the 3D pass when a shot is pending.</summary>
    public static void Flush(IRenderer r)
    {
        if (!pending) return; pending = false;
        try
        {
            var px = r.Capture(out int w, out int h);
            var file = Path.Combine(Paths.Screenshots, $"germball-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            File.WriteAllBytes(file, Png(px, w, h));
            Log.Info("Screenshot saved: " + file);
        }
        catch (Exception e) { Log.Warn("Screenshot failed: " + e.Message); }
    }

    public static byte[] Png(byte[] rgba, int w, int h)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            var len = BitConverter.GetBytes(data.Length); Array.Reverse(len); ms.Write(len);
            var t = System.Text.Encoding.ASCII.GetBytes(type); ms.Write(t); ms.Write(data);
            uint crc = Crc(t, 0xFFFFFFFF); crc = Crc(data, crc) ^ 0xFFFFFFFF;
            var c = BitConverter.GetBytes(crc); Array.Reverse(c); ms.Write(c);
        }
        var ihdr = new byte[13]; Be(ihdr, 0, w); Be(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 6;
        Chunk("IHDR", ihdr);
        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Fastest, true))
                for (int y = 0; y < h; y++) { z.WriteByte(0); for (int x = 0; x < w; x++) { int i = (y * w + x) * 4; z.WriteByte(rgba[i]); z.WriteByte(rgba[i + 1]); z.WriteByte(rgba[i + 2]); z.WriteByte(255); } }
            Chunk("IDAT", raw.ToArray());
        }
        Chunk("IEND", Array.Empty<byte>());
        return ms.ToArray();
    }
    static void Be(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static uint[] table;
    static uint Crc(byte[] d, uint c)
    {
        if (table == null) { table = new uint[256]; for (uint n = 0; n < 256; n++) { uint x = n; for (int k = 0; k < 8; k++) x = (x & 1) != 0 ? 0xEDB88320 ^ (x >> 1) : x >> 1; table[n] = x; } }
        foreach (var b in d) c = table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed class BootConfig
{
    public string Mode = "sandbox";   // sandbox | waves
    public string Map = "classic";    // random | classic | leviathan | bowl | city | spires | gut
    public int Seed = 1;
    public GermCustom Custom;
}

public sealed class Pad { public float X, Z, R, H, Fall; public string Kind, Name; }
public sealed class Crater { public float X, Z, R, D; public bool Acid; }
public sealed class Organ { public string Kind; public float X, Z, H, W, Ang, Ph; }
public sealed class Gut { public List<(float x, float z)> Pts; public float R, MinX, MaxX, MinZ, MaxZ; }
public sealed class Mogul { public float X, Z, R; }
public sealed class Bowl { public float R, H; }

public sealed class ChannelDef
{
    public List<(float x, float z)> Ctrl; public float W, Flat, Wall; public Func<float, float, float, float> Floor; public bool Blood;
}
public sealed class Channel
{
    public List<(float x, float z)> Pts; public float[] S; public float L, MinX, MaxX, MinZ, MaxZ, W, Flat, Wall, Top; public Func<float, float, float, float> Floor; public bool Blood;
}

public sealed class Layout
{
    public List<Pad> Pads = new(); public List<Crater> Craters = new(); public Mogul Mog; public List<object> Chans = new(); public List<float[]> Hills = new();
    public float Ox, Oz, Fs = 1, Amp = 1, Hue, Flesh; public int Sky, Spires = 34, SpireSeed = 404; public float SpireR; public Bowl Bowl;
    public bool Organic, Colonies; public List<Organ> Organs; public List<Gut> Guts; public List<object> Acid;
}

public sealed partial class World
{
    public const float HALF = 360; public const int SEG = 240; public const float CELL = HALF * 2 / SEG; public const int N = SEG + 1;
    public readonly BootConfig CFG;
    public static readonly (string key, string name, string blurb)[] WAVE_MAPS =
    {
        ("bowl", "SPORE BOWL", "A glowing basin ringed by four ruined hamlets. Nowhere to hide for long."),
        ("city", "GLASS CITY", "A dense grid of towers and alleys. Fights happen around corners."),
        ("spires", "SPIRE GARDEN", "Needle peaks, two abandoned forts and a canyon cutting through."),
        ("gut", "THE GULLET", "A churning stomach chamber inside the leviathan: an acid pit in the middle, ribs arching overhead, cells drifting past."),
    };

    Layout LAYOUT; List<Pad> PADS; List<Crater> CRATERS; Mogul MOG; Pad HOMEPAD; float SPAWNX, SPAWNZ; List<Channel> CHANNELS;

    static Func<float> Rng(int seed) { var m = new Mulberry32(seed); return () => (float)m.Next(); }

    // the leviathan: the inside of something enormous and alive. Organs rise out of a floor of folded flesh,
    // intestines coil across it, veins pulse, ribs arch overhead, and cells, bacteria and viruses drift through the air
    static Layout OrganicLayout(Layout L, Func<float> r, bool arena)
    {
        L.Organic = true; L.Colonies = true; L.Sky = 0; L.Hue = 0; L.Amp = 1; L.Fs = 1; L.Ox = r() * 500; L.Oz = r() * 500; L.Spires = arena ? 6 : 16; L.SpireSeed = (int)MathF.Floor(r() * 1e6f);
        L.Organs = new(); L.Guts = new(); L.Acid = new();
        float lim = arena ? 60 : 270;
        bool Free(float x, float z, float R)
        {
            if (MathF.Abs(x) > lim || MathF.Abs(z) > lim) return false;
            foreach (var p in L.Pads) if (Hypot(x - p.X, z - p.Z) < p.R + R + 18) return false;
            foreach (var o in L.Organs) if (Hypot(x - o.X, z - o.Z) < o.W * 1.6f + R) return false;
            foreach (var c in L.Craters) if (Hypot(x - c.X, z - c.Z) < c.R + R + 10) return false;
            return true;
        }
        (float, float)? Spot(float R) { for (int k = 0; k < 300; k++) { float x = (r() * 2 - 1) * lim, z = (r() * 2 - 1) * lim; if (Free(x, z, R)) return (x, z); } return null; }
        if (arena)
        {
            L.Pads.Add(new Pad { X = 0, Z = 40, R = 15, H = 8, Kind = "base", Name = "FIELD STATION", Fall = 12 });
            L.Craters.Add(new Crater { X = 0, Z = -14, R = 16, D = 7, Acid = true });
            L.Bowl = new Bowl { R = 76, H = 40 }; L.SpireR = 70;
            foreach (var (kind, x, z) in new[] { ("heart", -44f, -30f), ("lung", 46f, -26f), ("liver", -40f, 40f), ("kidney", 42f, 44f) }) L.Organs.Add(MakeOrgan(kind, x, z, r));
            L.Guts.Add(GutPath(r, -60, 10, 8)); L.Guts.Add(GutPath(r, 60, 4, 8));
        }
        else
        {
            L.Pads.Add(new Pad { X = 0, Z = 190, R = 30, H = 8, Kind = "base", Name = "FIELD STATION" });
            var T = new[] { ("MARROW HOLLOW", -150f, 60f), ("LYMPH NODE", 150f, 80f), ("CELL GARDEN", 30f, -40f) };
            var O = new[] { ("PARASITE NEST", -200f, -170f), ("TUMOR CAMP", 200f, -150f), ("SPLEEN CAMP", -40f, -230f) };
            foreach (var (n, x, z) in T) L.Pads.Add(new Pad { X = x + (r() - 0.5f) * 30, Z = z + (r() - 0.5f) * 30, R = 34 + r() * 8, H = 6, Kind = "town", Name = n, Fall = 26 });
            foreach (var (n, x, z) in O) L.Pads.Add(new Pad { X = x + (r() - 0.5f) * 30, Z = z + (r() - 0.5f) * 30, R = 40, H = 8, Kind = "outpost", Name = n, Fall = 28 });
            L.Craters.Add(new Crater { X = 120, Z = -40, R = 28, D = 9, Acid = true }); L.Craters.Add(new Crater { X = -100, Z = -60, R = 18, D = 7, Acid = true });
            foreach (var kind in new[] { "heart", "lung", "lung", "liver", "kidney", "kidney", "stomach", "brain" }) { var p = Spot(kind == "liver" ? 30 : 24); if (p is var (px, pz)) L.Organs.Add(MakeOrgan(kind, px, pz, r)); }
            for (int i = 0; i < 6; i++) { var p = Spot(10); if (p is var (px, pz)) L.Guts.Add(GutPath(r, px, pz, 16 + (int)MathF.Floor(r() * 10))); }
            // an artery trench winding down the middle, with blood in it, routed point by point keeping well clear of every pad
            var ctrl = new List<(float, float)>(); float pzz = 0;
            for (float x = -300; x <= 300; x += 100)
            {
                float best = pzz, bs = 1e9f;
                for (float z = -280; z <= 280; z += 8)
                {
                    float sc = MathF.Abs(z - pzz) * (ctrl.Count > 0 ? 1 : 0.1f);
                    foreach (var p in L.Pads) { float d = Hypot(x - p.X, z - p.Z) - p.R - 30; if (d < 0) sc += 1000 - d * 20; }
                    foreach (var c in L.Craters) if (Hypot(x - c.X, z - c.Z) < c.R + 20) sc += 500;
                    if (sc < bs) { bs = sc; best = z; }
                }
                ctrl.Add((x, best)); pzz = best;
            }
            L.Chans.Add(new ChannelDef { Ctrl = ctrl, W = 9, Flat = 3, Wall = 5, Floor = (_, _, _) => 1, Blood = true });
        }
        return L;
    }

    static Organ MakeOrgan(string kind, float x, float z, Func<float> r)
    {
        var S = kind switch { "heart" => (16f, 17f), "lung" => (12f, 22f), "liver" => (9f, 28f), "kidney" => (7f, 11f), "stomach" => (10f, 24f), _ => (13f, 19f) };
        return new Organ { Kind = kind, X = x, Z = z, H = S.Item1 * (0.85f + r() * 0.3f), W = S.Item2 * (0.9f + r() * 0.2f), Ang = r() * 6.28f, Ph = r() * 6.28f };
    }

    // an intestine: a coiling tube that wanders over the floor, half sunk into it
    static Gut GutPath(Func<float> r, float x, float z, int n)
    {
        var pts = new List<(float, float)> { (x, z) }; float a = r() * 6.28f;
        for (int i = 0; i < n; i++) { a += (r() - 0.5f) * 1.6f; x += MathF.Cos(a) * 7; z += MathF.Sin(a) * 7; pts.Add((x, z)); }
        float minX = 1e9f, maxX = -1e9f, minZ = 1e9f, maxZ = -1e9f;
        foreach (var (px, pz) in pts) { minX = MathF.Min(minX, px); maxX = MathF.Max(maxX, px); minZ = MathF.Min(minZ, pz); maxZ = MathF.Max(maxZ, pz); }
        return new Gut { Pts = pts, R = 3 + r() * 1.2f, MinX = minX - 6, MaxX = maxX + 6, MinZ = minZ - 6, MaxZ = maxZ + 6 };
    }

    static Layout MakeLayout(BootConfig cfg)
    {
        var L = new Layout();
        void PadA(float x, float z, float r, float h, string kind, string name, float fall = 0) => L.Pads.Add(new Pad { X = x, Z = z, R = r, H = h, Kind = kind, Name = name, Fall = fall });
        if (cfg.Map == "leviathan" || cfg.Map == "gut") return OrganicLayout(L, Rng(cfg.Seed * 31 + 7), cfg.Mode != "sandbox");
        if (cfg.Mode == "sandbox" && cfg.Map == "classic")
        {
            PadA(0, 40, 30, 6, "base", "HOME BASE"); PadA(0, -48, 42, 7, "town", "GLASS TOWN"); PadA(10, -205, 46, 12, "outpost", "NORTH CAMP");
            PadA(215, -40, 42, 24, "outpost", "MESA CAMP", 34); PadA(-30, 212, 42, 0, "outpost", "CRATER CAMP", 30); PadA(-222, -62, 42, 16, "outpost", "WEST CAMP");
            PadA(150, 150, 38, 5, "town", "SPORE TOWN"); PadA(-165, -215, 36, 10, "town", "RUST TOWN"); PadA(-140, 95, 30, 6, "town", "BONE TOWN");
            L.Craters = new() { new() { X = -95, Z = -10, R = 16, D = 6 }, new() { X = 95, Z = -110, R = 20, D = 8 }, new() { X = 70, Z = 255, R = 22, D = 7 }, new() { X = -250, Z = 160, R = 18, D = 7 }, new() { X = 265, Z = 95, R = 16, D = 6 }, new() { X = -95, Z = -130, R = 14, D = 5 } };
            L.Mog = new Mogul { X = 95, Z = 60, R = 40 };
            L.Chans = new() { "canyon", "gully" };
            L.Hills = new() { new[] { 290f, -290, 48, 60 }, new[] { -300f, -300, 40, 50 }, new[] { 295f, 280, 44, 55 }, new[] { -290f, 300, 30, 50 } };
            return L;
        }
        var r = Rng(cfg.Seed * 7919 + 13);
        L.Ox = r() * 900; L.Oz = r() * 900; L.Fs = 0.8f + r() * 0.5f; L.Amp = 0.75f + r() * 0.7f; L.Hue = r(); L.Sky = (int)MathF.Floor(r() * 4); L.SpireSeed = (int)MathF.Floor(r() * 1e6f);
        for (int i = 0; i < 4; i++) { float a = i / 4f * 6.28f + r(); L.Hills.Add(new[] { MathF.Cos(a) * (250 + r() * 60), MathF.Sin(a) * (250 + r() * 60), 25 + r() * 35, 45 + r() * 25 }); }
        bool Far(float x, float z, float rad) { foreach (var p in L.Pads) if (Hypot(x - p.X, z - p.Z) < p.R + rad + 40) return false; return true; }
        string[] NAMES = { "GLASS", "SPORE", "RUST", "BONE", "HUSK", "PRISM", "WEEP", "CHROME", "ASH", "LULL", "MOTH", "VEIL" }; var used = new HashSet<string>();
        string Nm() { string k; do k = NAMES[(int)MathF.Floor(r() * NAMES.Length)]; while (used.Contains(k) && used.Count < NAMES.Length); used.Add(k); return k; }
        void Place(int n, string kind, float rmin, float rmax, float lim, string suffix)
        {
            for (int k = 0, made = 0; k < 400 && made < n; k++)
            {
                float R = rmin + r() * (rmax - rmin), x = (r() * 2 - 1) * lim, z = (r() * 2 - 1) * lim;
                if (!Far(x, z, R)) continue; PadA(x, z, R, 2 + r() * 18, kind, Nm() + suffix, 22 + r() * 14); made++;
            }
        }
        if (cfg.Mode == "sandbox")
        {
            PadA((r() * 2 - 1) * 140, (r() * 2 - 1) * 140, 30, 6 + r() * 8, "base", "HOME BASE");
            Place(3 + (int)MathF.Floor(r() * 3), "town", 30, 46, 250, " TOWN");
            Place(3 + (int)MathF.Floor(r() * 3), "outpost", 38, 46, 260, " CAMP");
            for (int k = 0; k < 60 && L.Craters.Count < 4 + r() * 5; k++) { float x = (r() * 2 - 1) * 290, z = (r() * 2 - 1) * 290, R = 12 + r() * 12; if (Far(x, z, R)) L.Craters.Add(new Crater { X = x, Z = z, R = R, D = 4 + r() * 5 }); }
            for (int k = 0; k < 40; k++) { float x = (r() * 2 - 1) * 240, z = (r() * 2 - 1) * 240; if (Far(x, z, 40)) { L.Mog = new Mogul { X = x, Z = z, R = 30 + r() * 15 }; break; } }
            int nc = (int)MathF.Floor(r() * 3);
            for (int c = 0; c < nc; c++)
            {
                float a = r() * 6.28f; var ctrl = new List<(float, float)>(); float x = MathF.Cos(a) * 300, z = MathF.Sin(a) * 300, ha = a + PI + (r() - 0.5f);
                for (int k = 0; k < 5; k++) { ctrl.Add((Clamp(x, -310, 310), Clamp(z, -310, 310))); x += MathF.Cos(ha) * 140; z += MathF.Sin(ha) * 140; ha += (r() - 0.5f) * 1.2f; }
                float f0 = 30 + r() * 30, f1 = 5 + r() * 10;
                L.Chans.Add(new ChannelDef { Ctrl = ctrl, W = 7 + r() * 5, Flat = 1.5f + r() * 3, Wall = 5 + r() * 3, Floor = (u, _, _) => Lerp(f0, f1, u) });
            }
            L.Spires = 20 + (int)MathF.Floor(r() * 35);
            return L;
        }
        // wave arenas: the base in the middle, a ring of cover, steep walls all round
        var m = cfg.Map;
        if (m == "bowl")
        {
            PadA(0, 0, 17, 8, "base", "HOME BASE", 14);
            string[] dir = { "NORTH", "EAST", "SOUTH", "WEST" };
            for (int i = 0; i < 4; i++) { float a = i / 4f * 6.28f + 0.6f; PadA(MathF.Cos(a) * 44, MathF.Sin(a) * 44, 15, 7 + i * 0.5f, "town", dir[i] + " HAMLET", 12); }
            L.Craters = new() { new() { X = 30, Z = -24, R = 7, D = 3 }, new() { X = -32, Z = 26, R = 8, D = 3 } };
            L.Bowl = new Bowl { R = 74, H = 34 }; L.Spires = 8; L.SpireR = 70; L.Hue = 0.12f; L.Sky = 1; L.Amp = 0.6f;
        }
        else if (m == "city")
        {
            PadA(0, 0, 52, 8, "town", "GLASS CITY", 16); PadA(0, 0, 13, 8, "base", "HOME BASE", 8);
            L.Bowl = new Bowl { R = 70, H = 40 }; L.Spires = 0; L.Hue = 0.55f; L.Sky = 2; L.Amp = 0.5f;
        }
        else
        {
            PadA(0, 0, 16, 10, "base", "HOME BASE", 12);
            PadA(-46, -22, 24, 11, "outpost", "OLD FORT A", 14); PadA(46, -22, 24, 11, "outpost", "OLD FORT B", 14);
            L.Chans.Add(new ChannelDef { Ctrl = new() { (-90, 52), (-40, 34), (0, 46), (40, 34), (90, 52) }, W = 6, Flat = 2.5f, Wall = 4, Floor = (_, _, _) => 6 });
            L.Bowl = new Bowl { R = 78, H = 38 }; L.Spires = 22; L.SpireR = 72; L.Hue = 0.8f; L.Sky = 3; L.Amp = 0.8f;
        }
        return L;
    }

    // every map is microscopic now: the land is folded flesh, organs rise out of it, and things drift in the haze
    static Layout OrganicDress(Layout L, BootConfig cfg)
    {
        if (L.Organs != null) return L;
        var r = Rng(cfg.Seed * 131 + (cfg.Map ?? "").Length * 17 + 5); bool arena = L.Bowl != null; float lim = arena ? 62 : 280;
        L.Organic = true; L.Organs = new(); L.Guts = new(); L.Acid = new(); L.Flesh = arena ? 0.7f : 1.25f;
        bool Free(float x, float z, float R)
        {
            if (MathF.Abs(x) > lim || MathF.Abs(z) > lim) return false; if (arena && Hypot(x, z) > L.Bowl.R - R) return false;
            foreach (var p in L.Pads) if (Hypot(x - p.X, z - p.Z) < p.R + R + 16) return false;
            foreach (var o in L.Organs) if (Hypot(x - o.X, z - o.Z) < o.W * 1.6f + R) return false;
            foreach (var c in L.Craters) if (Hypot(x - c.X, z - c.Z) < c.R + R + 8) return false;
            return true;
        }
        (float, float)? Spot(float R) { for (int k = 0; k < 300; k++) { float x = (r() * 2 - 1) * lim, z = (r() * 2 - 1) * lim; if (Free(x, z, R)) return (x, z); } return null; }
        var kinds = arena ? new[] { "heart", "lung", "kidney" } : new[] { "heart", "lung", "lung", "liver", "kidney", "stomach", "brain" };
        foreach (var k in kinds) { var p = Spot(arena ? 14 : 24); if (p is var (px, pz)) { var o = MakeOrgan(k, px, pz, r); if (arena) { o.W *= 0.6f; o.H *= 0.6f; } L.Organs.Add(o); } }
        for (int i = 0; i < (arena ? 2 : 5); i++) { var p = Spot(8); if (p is var (px, pz)) L.Guts.Add(GutPath(r, px, pz, arena ? 7 : 14)); }
        if (L.Craters.Count > 0) L.Craters[0].Acid = true;
        return L;
    }

    static Channel MakeChannel(ChannelDef o)
    {
        var ctrl = o.Ctrl; int n = ctrl.Count; var pts = new List<(float x, float z)>();
        for (int i = 0; i < n - 1; i++)
        {
            var p0 = ctrl[Math.Max(0, i - 1)]; var p1 = ctrl[i]; var p2 = ctrl[i + 1]; var p3 = ctrl[Math.Min(n - 1, i + 2)]; int S = n == 2 ? 1 : 16;
            for (int k = 0; k < S; k++) { float t = (float)k / S; pts.Add((CR(p0.x, p1.x, p2.x, p3.x, t), CR(p0.z, p1.z, p2.z, p3.z, t))); }
        }
        pts.Add(ctrl[n - 1]);
        var s = new float[pts.Count]; float minX = 1e9f, maxX = -1e9f, minZ = 1e9f, maxZ = -1e9f;
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) s[i] = s[i - 1] + Hypot(pts[i].x - pts[i - 1].x, pts[i].z - pts[i - 1].z);
            minX = MathF.Min(minX, pts[i].x); maxX = MathF.Max(maxX, pts[i].x); minZ = MathF.Min(minZ, pts[i].z); maxZ = MathF.Max(maxZ, pts[i].z);
        }
        var c = new Channel { Pts = pts, S = s, L = s[^1], MinX = minX - 45, MaxX = maxX + 45, MinZ = minZ - 45, MaxZ = maxZ + 45, W = o.W, Flat = o.Flat, Wall = o.Wall, Floor = o.Floor, Blood = o.Blood };
        c.Top = c.Floor(0, 0, c.L);
        return c;
    }

    struct ChanQ { public float D, S, T; public int I; }
    static ChanQ ChanDist(Channel c, float x, float z)
    {
        var q = new ChanQ { D = 1e9f };
        if (x < c.MinX || x > c.MaxX || z < c.MinZ || z > c.MaxZ) return q;
        var p = c.Pts;
        for (int i = 0; i < p.Count - 1; i++)
        {
            float ax = p[i].x, az = p[i].z, dx = p[i + 1].x - ax, dz = p[i + 1].z - az, L2 = dx * dx + dz * dz;
            float t = Clamp(((x - ax) * dx + (z - az) * dz) / L2, 0, 1), d = Hypot(x - ax - dx * t, z - az - dz * t);
            if (d < q.D) { q.D = d; q.S = c.S[i] + (c.S[i + 1] - c.S[i]) * t; q.I = i; q.T = t; }
        }
        return q;
    }
    static float ChanProfile(Channel c, float d)
    {
        if (d <= c.Flat) return 0; float u = MathF.Min(1, (d - c.Flat) / (c.W - c.Flat));
        return c.Wall * u * u + MathF.Max(0, d - c.W) * 2 * c.Wall / (c.W - c.Flat);
    }
    // a winding halfpipe canyon down the west side and a serpent gully in the north-east
    static Channel Canyon() => MakeChannel(new ChannelDef
    {
        Ctrl = new() { (-300, -290), (-305, -150), (-290, 0), (-300, 150), (-290, 290) }, W = 10, Flat = 4, Wall = 7,
        Floor = (u, s, L) => Lerp(40, 10, u) + 2.5f * MathF.Sin(s / 36 * PI * 2) * Smooth(15, 40, s) * (1 - Smooth(L - 40, L - 15, s)),
    });
    static Channel Gully() => MakeChannel(new ChannelDef { Ctrl = new() { (300, -300), (240, -250), (270, -190), (205, -150), (150, -120) }, W = 8, Flat = 1.5f, Wall = 6, Floor = (u, _, _) => Lerp(60, 9, MathF.Pow(u, 0.9f)) });
    static float RectDist(float x, float z, float x0, float x1, float z0, float z1) { float dx = MathF.Max(MathF.Max(x0 - x, 0), x - x1), dz = MathF.Max(MathF.Max(z0 - z, 0), z - z1); return Hypot(dx, dz); }

    void InitLayout()
    {
        LAYOUT = OrganicDress(MakeLayout(CFG), CFG);
        PADS = LAYOUT.Pads; CRATERS = LAYOUT.Craters; MOG = LAYOUT.Mog ?? new Mogul { X = 9999, Z = 9999, R = 1 };
        HOMEPAD = PADS.First(p => p.Kind == "base");
        SPAWNX = HOMEPAD.X; SPAWNZ = HOMEPAD.Z + 6;
        CHANNELS = LAYOUT.Chans.Select(c => c is string s ? (s == "canyon" ? Canyon() : Gully()) : MakeChannel((ChannelDef)c)).ToList();
    }
}

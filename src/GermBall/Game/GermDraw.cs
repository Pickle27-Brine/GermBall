using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>emit(shape, tone, x, y, z, qx, qy, qz, qw, sx, sy, sz).
/// shapes: Z sphere, I icosahedron, P cone (apex +Y), B box, C cylinder, T ring (in XZ).
/// tones: a body, b second colour, c glow accent, e eye, k pupil, d armor, f hit flash.</summary>
public delegate void Emit(char shape, char tone, float x, float y, float z, float qx, float qy, float qz, float qw, float sx, float sy, float sz);

public sealed class Deco
{
    public string K; public int N; public string From; public float Len; public int Every;
    public Deco Clone() => (Deco)MemberwiseClone();
    public Deco(string k, int n = 0, string from = null, float len = 0, int every = 0) { K = k; N = n; From = from; Len = len; Every = every; }
}

public sealed class EyeSpec
{
    public int N; public float Size, Spread, Y; public bool Slit, Visor;
    public EyeSpec Clone() => (EyeSpec)MemberwiseClone();
}

public sealed class SpeciesColors
{
    public uint A, B, C, E, D = 0xf7f4ff;
    public SpeciesColors Clone() => (SpeciesColors)MemberwiseClone();
    public uint Get(char t) => t switch { 'a' => A, 'b' => B, 'c' => C, 'e' => E, 'd' => D, _ => A };
}

public sealed class FaceShape
{
    public string S = "circle"; public float X = 0.5f, Y = 0.5f, W = 0.15f, H = 1, R; public string C = "#111111";
    public FaceShape Clone() => (FaceShape)MemberwiseClone();
}

/// <summary>A body type. Every germ in the game is drawn from one of these.</summary>
public sealed class Species
{
    public string Key, Name, Head;
    public float[] Hs, Torso, Arm, Leg, Foot;
    public float Neck, Seg = 1, Wave, Hand, Bend, TLen = 1, Hump;
    public bool Facet;
    public EyeSpec Eyes;
    public List<Deco> Deco;
    public SpeciesColors Col;
    public string ArmorStyle = "chitin";
    public List<FaceShape> Face = new();
    // caches
    internal bool BandCached; internal Deco Band; internal int ClawsCached = -1;

    public Species Clone()
    {
        var s = (Species)MemberwiseClone();
        s.Hs = (float[])Hs.Clone(); s.Torso = (float[])Torso.Clone(); s.Arm = (float[])Arm.Clone(); s.Leg = (float[])Leg.Clone(); s.Foot = (float[])Foot.Clone();
        s.Eyes = Eyes.Clone(); s.Deco = Deco.Select(d => d.Clone()).ToList(); s.Col = Col.Clone(); s.Face = Face.Select(f => f.Clone()).ToList();
        s.BandCached = false; s.Band = null; s.ClawsCached = -1;
        return s;
    }
}

public sealed class BodyShape
{
    public float Head = 1, Torso = 1, Width = 1, Belly = 0, Limbs = 1, Hands = 1, Feet = 1, Bend = 1, Neck = 1, Hump = 0, Segments = 1;
    public BodyShape Clone() => (BodyShape)MemberwiseClone();
}

public sealed class EyeCustom { public int N = -1; public float Size = 1, Spread = 1; public EyeCustom Clone() => (EyeCustom)MemberwiseClone(); }

/// <summary>The player's germ customization (saved to custom.json).</summary>
public sealed class GermCustom
{
    public string Preset = "phage", Head = "";
    public BodyShape Body = new();
    public Dictionary<string, uint> Col;   // overrides for a/b/c/e, or null
    public EyeCustom Eyes = new();
    public List<FaceShape> Face = new();
    public List<string> Acc = new();
    public string Armor = "chitin";
    public uint ArmorCol = 0xf7f4ff;
    public GermCustom Clone()
    {
        var c = (GermCustom)MemberwiseClone();
        c.Body = Body.Clone(); c.Col = Col == null ? null : new Dictionary<string, uint>(Col); c.Eyes = Eyes.Clone();
        c.Face = Face.Select(f => f.Clone()).ToList(); c.Acc = new List<string>(Acc); return c;
    }
}

public sealed class ArmorPieceSpec { public int Part; public float Y0, Y1, Phi, Arc, Sc; public ArmorPieceSpec(int part, float y0, float y1, float phi, float arc, float sc) { Part = part; Y0 = y0; Y1 = y1; Phi = phi; Arc = arc; Sc = sc; } }

/// <summary>Per-draw options: { t, fdt, lod, has, flash, curl, ball, st, s }.</summary>
public sealed class DrawOpts
{
    public float T, Fdt, S = 1, Curl; public int Lod = 2;
    public bool[] Has; public float[] Flash; public float[] St;
    public bool HasBall; public float BallX, BallY, BallZ, BallR; public Q4 BallQ = new();
}

/// <summary>
/// Builds a microbe body out of simple shapes from a skeleton pose. The torso, neck and every limb are long
/// chains of overlapping segments laid along bent curves, so the bodies are sleek with no gaps, and the curves
/// bend further than real joints could (rubber-hose noodles that lag and whip on springs). Shared by the game
/// and the customization preview.
/// Pose layout: 15 parts x 13 floats: pos(3) quat(4) radii(3: rx, half-length, rz) axis(3).
/// </summary>
public static class GermDraw
{
    static Species Sp(string key, string name, string head, float[] hs, float neck, float[] torso, float seg, float[] arm, float hand, float[] leg, float[] foot, float bend,
        EyeSpec eyes, Deco[] deco, uint a, uint b, uint c, uint e, bool facet = false, float wave = 0)
        => new() { Key = key, Name = name, Head = head, Hs = hs, Neck = neck, Torso = torso, Seg = seg, Arm = arm, Hand = hand, Leg = leg, Foot = foot, Bend = bend, Eyes = eyes, Deco = deco.ToList(), Col = new SpeciesColors { A = a, B = b, C = c, E = e }, Facet = facet, Wave = wave };
    static EyeSpec E(int n, float size, float spread, float y, bool slit = false, bool visor = false) => new() { N = n, Size = size, Spread = spread, Y = y, Slit = slit, Visor = visor };

    public static readonly Dictionary<string, Species> SPECIES = new()
    {
        ["coccus"] = Sp("coccus", "Coccus", "sphere", new[] { 1.60f, 1.54f, 1.54f }, 0.2f, new[] { 1.03f, 0.95f, 0.91f }, 1, new[] { 0.75f, 0.6f }, 0.75f, new[] { 0.85f, 0.7f }, new[] { 1.1f, 0.75f, 1.5f }, 1.6f, E(2, 0.2f, 0.36f, 0.12f), new[] { new Deco("cilia", 18) }, 0xff8fb8, 0xd04a80, 0xffe14a, 0xffffff),
        ["bacillus"] = Sp("bacillus", "Bacillus", "rod", new[] { 1.22f, 1.60f, 1.22f }, 0.6f, new[] { 0.76f, 0.76f, 0.80f }, 1, new[] { 0.7f, 0.55f }, 0.6f, new[] { 0.75f, 0.6f }, new[] { 1f, 0.7f, 1.6f }, 1.45f, E(2, 0.12f, 0.3f, 0.15f), new[] { new Deco("flagella", 2, "back", 1.6f), new Deco("bands", every: 3) }, 0x8fd16a, 0x3e7a2a, 0xc4ff3a, 0xfff6a0),
        ["amoeba"] = Sp("amoeba", "Amoeba", "blob", new[] { 1.47f, 1.28f, 1.41f }, 0.1f, new[] { 1.06f, 1.14f, 1.10f }, 1, new[] { 1.05f, 0.8f }, 1.0f, new[] { 0.95f, 0.85f }, new[] { 1.2f, 0.8f, 1.4f }, 1.9f, E(3, 0.11f, 0.3f, 0.05f), new[] { new Deco("lumps", 6), new Deco("nucleus") }, 0x8f7aff, 0x4a3aa8, 0xffd23a, 0xff6a3a),
        ["spirillum"] = Sp("spirillum", "Spirillum", "cone", new[] { 1.02f, 1.73f, 1.02f }, 0.8f, new[] { 0.53f, 0.47f, 0.55f }, 1.4f, new[] { 0.5f, 0.42f }, 0.5f, new[] { 0.6f, 0.48f }, new[] { 0.8f, 0.6f, 1.6f }, 2.2f, E(2, 0.1f, 0.24f, 0.1f, slit: true), new[] { new Deco("flagella", 1, "pelvis", 2.2f) }, 0x3a6a7a, 0x12222a, 0xff2a4a, 0xff2a4a, wave: 0.35f),
        ["tardigrade"] = Sp("tardigrade", "Tardigrade", "snout", new[] { 1.41f, 1.28f, 1.60f }, 0.15f, new[] { 1.03f, 1.10f, 1.03f }, 1, new[] { 1.0f, 0.9f }, 0.9f, new[] { 1.1f, 1.0f }, new[] { 1.2f, 0.8f, 1.3f }, 1.3f, E(2, 0.08f, 0.42f, 0.25f), new[] { new Deco("bands", every: 2), new Deco("claws") }, 0xb9a48a, 0x7a6650, 0xff9a2a, 0x221810),
        ["diatom"] = Sp("diatom", "Diatom", "disc", new[] { 1.47f, 0.90f, 1.47f }, 0.5f, new[] { 0.80f, 0.68f, 0.87f }, 1, new[] { 0.65f, 0.5f }, 0.6f, new[] { 0.75f, 0.6f }, new[] { 1f, 0.6f, 1.5f }, 1.4f, E(1, 0.22f, 0, 0.05f, visor: true), new[] { new Deco("crest"), new Deco("ring") }, 0xbfefff, 0x1f8fd0, 0x7affef, 0xffffff, facet: true),
        ["copepod"] = Sp("copepod", "Copepod", "shield", new[] { 1.73f, 1.28f, 1.66f }, 0.15f, new[] { 1.14f, 1.03f, 1.22f }, 1, new[] { 0.95f, 0.8f }, 0.95f, new[] { 0.95f, 0.8f }, new[] { 1.2f, 0.8f, 1.5f }, 1.35f, E(1, 0.24f, 0, 0.12f), new[] { new Deco("antennae", len: 2.4f), new Deco("bands", every: 2), new Deco("plates") }, 0xc9d98a, 0x56692c, 0xffb21c, 0xff2a2a),
        ["medusa"] = Sp("medusa", "Medusa", "bell", new[] { 1.73f, 1.28f, 1.73f }, 0.4f, new[] { 0.61f, 0.53f, 0.65f }, 1.2f, new[] { 0.55f, 0.42f }, 0.5f, new[] { 0.6f, 0.48f }, new[] { 0.8f, 0.6f, 1.4f }, 2.0f, E(2, 0.1f, 0.3f, -0.1f), new[] { new Deco("tentacles", 6, len: 1.4f) }, 0xf4e8ff, 0xb03aa8, 0xff4fd8, 0xff7ae6),
        ["phage"] = Sp("phage", "Phage", "capsid", new[] { 1.66f, 1.86f, 1.66f }, 0.9f, new[] { 0.57f, 0.47f, 0.59f }, 1, new[] { 0.62f, 0.5f }, 0.6f, new[] { 0.7f, 0.55f }, new[] { 0.9f, 0.6f, 1.5f }, 1.4f, E(2, 0.14f, 0.32f, 0.0f), new[] { new Deco("collar") }, 0xf4f1ff, 0x6a6a8a, 0xff7a3c, 0x7affef),
        ["virion"] = Sp("virion", "Virion", "sphere", new[] { 1.54f, 1.54f, 1.54f }, 0.3f, new[] { 0.80f, 0.76f, 0.84f }, 1, new[] { 0.7f, 0.55f }, 0.65f, new[] { 0.75f, 0.6f }, new[] { 1f, 0.7f, 1.5f }, 1.6f, E(2, 0.13f, 0.32f, 0.08f), new[] { new Deco("spikes", 18) }, 0xff6a5a, 0x9a2a3a, 0xffd23a, 0xffffff),
        ["rotifer"] = Sp("rotifer", "Rotifer", "wheel", new[] { 1.41f, 1.15f, 1.41f }, 0.7f, new[] { 0.72f, 0.84f, 0.68f }, 1, new[] { 0.62f, 0.5f }, 0.6f, new[] { 0.7f, 0.55f }, new[] { 0.9f, 0.6f, 1.6f }, 1.6f, E(2, 0.1f, 0.26f, 0.2f), new[] { new Deco("crown"), new Deco("flagella", 1, "pelvis", 1.0f) }, 0xffd6a0, 0xb87a3a, 0x7affef, 0x2a1a10),
        ["volvox"] = Sp("volvox", "Volvox", "sphere", new[] { 1.60f, 1.60f, 1.60f }, 0.2f, new[] { 0.99f, 0.99f, 0.99f }, 1, new[] { 0.7f, 0.6f }, 0.7f, new[] { 0.8f, 0.65f }, new[] { 1f, 0.7f, 1.4f }, 1.7f, E(2, 0.14f, 0.34f, 0.1f), new[] { new Deco("dots", 14) }, 0x7ad99a, 0x2a8a5a, 0xd6ff6a, 0xffffff),
        ["radiolarian"] = Sp("radiolarian", "Radiolarian", "star", new[] { 1.47f, 1.47f, 1.47f }, 0.4f, new[] { 0.72f, 0.65f, 0.76f }, 1, new[] { 0.6f, 0.5f }, 0.55f, new[] { 0.7f, 0.55f }, new[] { 0.9f, 0.6f, 1.5f }, 1.5f, E(2, 0.11f, 0.3f, 0.05f), new[] { new Deco("spines", 10) }, 0xfff2c4, 0xc8a050, 0xffe14a, 0x3affc0, facet: true),
        ["paramecium"] = Sp("paramecium", "Paramecium", "slipper", new[] { 1.28f, 1.79f, 1.41f }, 0.4f, new[] { 0.84f, 0.76f, 0.87f }, 1, new[] { 0.65f, 0.5f }, 0.6f, new[] { 0.7f, 0.55f }, new[] { 0.9f, 0.6f, 1.5f }, 1.6f, E(2, 0.12f, 0.28f, 0.15f), new[] { new Deco("cilia", 26) }, 0xa8e0ff, 0x3a7ab8, 0xffffff, 0x2a1a5a),
        ["euglena"] = Sp("euglena", "Euglena", "drop", new[] { 1.28f, 1.73f, 1.28f }, 0.5f, new[] { 0.72f, 0.65f, 0.72f }, 1.2f, new[] { 0.6f, 0.48f }, 0.55f, new[] { 0.7f, 0.55f }, new[] { 0.9f, 0.6f, 1.5f }, 1.7f, E(1, 0.16f, 0, 0.25f), new[] { new Deco("flagella", 1, "head", 2.0f), new Deco("nucleus") }, 0x6ad97a, 0x2a7a3a, 0xff3a2a, 0xff3a2a),
    };

    /// <summary>Which species each kind of actor is.</summary>
    public static readonly Dictionary<string, string> TYPE_SPECIES = new()
    {
        ["small"] = "coccus", ["normal"] = "bacillus", ["brute"] = "amoeba", ["assassin"] = "spirillum", ["mech"] = "tardigrade", ["hero"] = "phage", ["ally"] = "diatom", ["ally2"] = "copepod", ["ally3"] = "medusa",
        ["mhero"] = "phage", ["mally"] = "diatom", ["mally2"] = "copepod", ["mally3"] = "medusa",
    };

    public static GermCustom DefaultCustom() => new();

    /// <summary>Accessories you can add in the customization menu.</summary>
    public static readonly Dictionary<string, Deco> ACCESSORY = new()
    {
        ["flagellum"] = new("flagella", 1, "back", 1.8f), ["twinFlagella"] = new("flagella", 2, "back", 1.4f), ["headWhip"] = new("flagella", 1, "head", 1.6f),
        ["cilia"] = new("cilia", 22), ["spikes"] = new("spikes", 16), ["antennae"] = new("antennae", len: 2.0f), ["crown"] = new("crown"), ["halo"] = new("halo"), ["nucleus"] = new("nucleus"),
        ["tentacles"] = new("tentacles", 5, len: 1.2f), ["horn"] = new("horn"), ["fins"] = new("fins"), ["pollen"] = new("pollen"), ["sporeSacks"] = new("sacks"), ["vacuolePack"] = new("pack"), ["tail"] = new("flagella", 1, "pelvis", 1.6f),
        ["mohawk"] = new("mohawk"), ["dots"] = new("dots", 12), ["lumps"] = new("lumps", 5), ["bands"] = new("bands", every: 2), ["crest"] = new("crest"), ["collar"] = new("collar"), ["stalkEyes"] = new("stalkEyes"), ["plates"] = new("plates"),
    };
    public static readonly string[] ARMOR_STYLES = { "chitin", "capsid", "spines", "glass", "slime", "shell", "coral", "scales", "cilia", "bubbles", "frustule", "barnacles", "rings", "plaque", "pearls", "pollen", "none" };
    public static readonly string[] HEAD_SHAPES = { "sphere", "rod", "capsid", "disc", "bell", "cone", "snout", "shield", "wheel", "star", "blob", "slipper", "drop", "cube" };
    public static readonly string[] FACE_SHAPES = { "circle", "oval", "square", "triangle", "star", "heart", "crescent", "line", "zigzag", "ring", "cross", "drop" };

    /// <summary>The player's armor layout (the same plates the game gives the hero), for previews.</summary>
    public static readonly ArmorPieceSpec[] HERO_ARMOR = new float[][]
    {
        new[]{1f, 0.15f, 0.96f, 0, 150, 1.1f}, new[]{1f, -0.9f, -0.62f, 0, 120, 1.09f}, new[]{1f, -0.58f, -0.3f, 0, 126, 1.09f}, new[]{1f, -0.26f, 0.1f, 0, 136, 1.09f}, new[]{1f, -0.3f, 0.9f, 180, 140, 1.1f}, new[]{2f, -0.6f, 0.55f, 0, 140, 1.12f}, new[]{0f, 0.0f, 1.0f, 180, 250, 1.12f},
        new[]{3f, -1.0f, -0.45f, 0, 360, 1.25f}, new[]{6f, -1.0f, -0.45f, 0, 360, 1.25f}, new[]{3f, -0.42f, 0.08f, -90, 200, 1.15f}, new[]{6f, -0.42f, 0.08f, 90, 200, 1.15f}, new[]{4f, -0.55f, 0.75f, -90, 220, 1.14f}, new[]{7f, -0.55f, 0.75f, 90, 220, 1.14f},
        new[]{9f, -0.85f, 0.5f, 0, 150, 1.12f}, new[]{12f, -0.85f, 0.5f, 0, 150, 1.12f}, new[]{10f, -0.75f, 0.85f, 0, 170, 1.12f}, new[]{13f, -0.75f, 0.85f, 0, 170, 1.12f},
    }.Select(a => new ArmorPieceSpec((int)a[0], a[1], a[2], a[3], a[4], a[5])).ToArray();

    /// <summary>A full species object for a customization.</summary>
    public static Species SpeciesFromCustom(GermCustom c)
    {
        c ??= DefaultCustom();
        var b = c.Body ?? new BodyShape();
        var baseSp = SPECIES.TryGetValue(c.Preset ?? "phage", out var bs) ? bs : SPECIES["phage"];
        var sp = baseSp.Clone();
        if (c.Col != null) foreach (var (k, v) in c.Col) switch (k) { case "a": sp.Col.A = v; break; case "b": sp.Col.B = v; break; case "c": sp.Col.C = v; break; case "e": sp.Col.E = v; break; }
        if (!string.IsNullOrEmpty(c.Head)) sp.Head = c.Head;
        for (int i = 0; i < 3; i++) sp.Hs[i] *= b.Head;
        sp.Torso = new[] { sp.Torso[0] * b.Width * (1 + b.Belly * 0.15f), sp.Torso[1] * b.Width * (1 + b.Belly * 0.45f), sp.Torso[2] * b.Width };
        sp.TLen = b.Torso; sp.Hump = b.Hump;
        for (int i = 0; i < 2; i++) { sp.Arm[i] *= b.Limbs; sp.Leg[i] *= b.Limbs; }
        sp.Hand *= b.Hands; for (int i = 0; i < 3; i++) sp.Foot[i] *= b.Feet;
        sp.Bend = 1 + (sp.Bend - 1) * b.Bend; sp.Neck *= b.Neck; sp.Seg = (sp.Seg == 0 ? 1 : sp.Seg) * b.Segments;
        var e = c.Eyes ?? new EyeCustom();
        if (e.N >= 0) sp.Eyes.N = e.N; sp.Eyes.Size *= e.Size; sp.Eyes.Spread *= e.Spread;
        foreach (var k in c.Acc ?? new()) sp.Deco.Add(ACCESSORY.TryGetValue(k, out var d) ? d.Clone() : new Deco(k));
        sp.ArmorStyle = string.IsNullOrEmpty(c.Armor) ? "chitin" : c.Armor; sp.Col.D = c.ArmorCol;
        sp.Face = (c.Face ?? new()).Select(f => f.Clone()).ToList();
        return sp;
    }

    // ---- temporaries (single-threaded drawing)
    sealed class Frame { public V3 f = new(), r = new(), u = new(); }
    static readonly Q4 PQ = new(), TQ = new(), TQ2 = new(), MQX = new();
    static readonly V3 TV = new(), TV2 = new(), A0 = new(), A1 = new(), B0 = new(), B1 = new(), CC = new();
    static readonly Frame HF = new(), CF = new();

    static Q4 Part(float[] ps, int i, Q4 q) { int k = i * 13; q.x = ps[k + 3]; q.y = ps[k + 4]; q.z = ps[k + 5]; q.w = ps[k + 6]; return q; }
    static void Ends(float[] ps, int i, V3 a, V3 b, float k0 = 0.92f)
    {
        int k = i * 13; float h = ps[k + 8] * k0;
        a.x = ps[k] - ps[k + 10] * h; a.y = ps[k + 1] - ps[k + 11] * h; a.z = ps[k + 2] - ps[k + 12] * h;
        b.x = ps[k] + ps[k + 10] * h; b.y = ps[k + 1] + ps[k + 11] * h; b.z = ps[k + 2] + ps[k + 12] * h;
    }
    static float D2(V3 a, float x, float y, float z) => (a.x - x) * (a.x - x) + (a.y - y) * (a.y - y) + (a.z - z) * (a.z - z);
    static void Swap(V3 a, V3 b) { float tx = a.x, ty = a.y, tz = a.z; a.x = b.x; a.y = b.y; a.z = b.z; b.x = tx; b.y = ty; b.z = tz; }

    /// <summary>A chain of segments along a quadratic curve from p0 to p2, bowed through c (already exaggerated).</summary>
    static void Chain(Emit emit, char shape, Func<int, int, char> tone, V3 p0, V3 c, V3 p2, Func<float, float> prof, float r0, float r1, float bulge, float density, float wave, float waveR, Frame fr, float t, int minN = 4, int maxN = 22)
    {
        float len = Hypot(c.x - p0.x, c.y - p0.y, c.z - p0.z) + Hypot(p2.x - c.x, p2.y - c.y, p2.z - c.z);
        if (prof != null) { r0 = prof(0.1f); r1 = prof(0.9f); }
        float rAvg = MathF.Max(0.01f, (r0 + r1) / 2);
        int n = (int)Clamp(MathF.Ceiling(len / (rAvg * 0.48f) * density), minN == 0 ? 4 : minN, maxN == 0 ? 22 : maxN);
        float step = len / n;
        for (int i = 0; i <= n; i++)
        {
            float u = (float)i / n, a = (1 - u) * (1 - u), b = 2 * u * (1 - u), e = u * u;
            float x = a * p0.x + b * c.x + e * p2.x, y = a * p0.y + b * c.y + e * p2.y, z = a * p0.z + b * c.z + e * p2.z;
            float tx = 2 * (1 - u) * (c.x - p0.x) + 2 * u * (p2.x - c.x), ty = 2 * (1 - u) * (c.y - p0.y) + 2 * u * (p2.y - c.y), tz = 2 * (1 - u) * (c.z - p0.z) + 2 * u * (p2.z - c.z);
            if (wave != 0 && fr != null)
            {
                float w = MathF.Sin(u * PI * 4 + t * 3) * waveR * MathF.Sin(u * PI), w2 = MathF.Cos(u * PI * 4 + t * 3) * waveR * MathF.Sin(u * PI);
                x += fr.r.x * w + fr.f.x * w2; y += fr.r.y * w + fr.f.y * w2; z += fr.r.z * w + fr.f.z * w2;
            }
            float r = prof != null ? prof(u) : Lerp(r0, r1, u) + bulge * MathF.Sin(u * PI);
            QFromY(tx, ty, tz, PQ);
            emit(shape, tone(i, n), x, y, z, PQ.x, PQ.y, PQ.z, PQ.w, r, MathF.Max(r, step * 0.7f), r);
        }
    }
    static void Chain(Emit emit, char shape, char tone, V3 p0, V3 c, V3 p2, float r0, float r1, float bulge, float density, int minN, int maxN, float t)
        => Chain(emit, shape, (_, _) => tone, p0, c, p2, null, r0, r1, bulge, density, 0, 0, null, t, minN, maxN);

    /// <summary>The bowed control point for a limb, on a spring so the limb lags, overshoots and whips.</summary>
    static V3 Bowed(V3 p0, V3 p1, V3 p2, float bend, float[] st, int si, float fdt, V3 o)
    {
        float mx = (p0.x + p2.x) / 2, my = (p0.y + p2.y) / 2, mz = (p0.z + p2.z) / 2;
        float cx = 2 * p1.x - mx, cy = 2 * p1.y - my, cz = 2 * p1.z - mz;
        float tx = mx + (cx - mx) * bend - p0.x, ty = my + (cy - my) * bend - p0.y, tz = mz + (cz - mz) * bend - p0.z;
        if (st != null && fdt > 0)
        {
            int k = si * 7;
            if (st[k + 6] == 0 || MathF.Abs(st[k] - tx) + MathF.Abs(st[k + 1] - ty) + MathF.Abs(st[k + 2] - tz) > 3) { st[k] = tx; st[k + 1] = ty; st[k + 2] = tz; st[k + 3] = st[k + 4] = st[k + 5] = 0; st[k + 6] = 1; }
            st[k + 3] += ((tx - st[k]) * 210 - st[k + 3] * 15) * fdt; st[k] += st[k + 3] * fdt;
            st[k + 4] += ((ty - st[k + 1]) * 210 - st[k + 4] * 15) * fdt; st[k + 1] += st[k + 4] * fdt;
            st[k + 5] += ((tz - st[k + 2]) * 210 - st[k + 5] * 15) * fdt; st[k + 2] += st[k + 5] * fdt;
            tx = st[k]; ty = st[k + 1]; tz = st[k + 2];
        }
        o.x = p0.x + tx; o.y = p0.y + ty; o.z = p0.z + tz; return o;
    }
    // NOTE: the original packed 6 floats per spring slot but used offset +6 as an "initialised" flag, which overlapped
    // the next slot; slots here are 7 wide so they never alias. Callers allocate NewSpringState().
    public static float[] NewSpringState() => new float[7 * 8];

    /// <summary>A part's local frame: forward (-Z), right (+X), up (+Y).</summary>
    static Frame At(float[] ps, int i, Frame F) { Part(ps, i, TQ); QRot(TQ, 0, 0, -1, F.f); QRot(TQ, 1, 0, 0, F.r); QRot(TQ, 0, 1, 0, F.u); return F; }

    /// <summary>The whole body.</summary>
    public static void Draw(Species sp, float[] ps, DrawOpts o, Emit emit)
    {
        var has = o.Has; var fl = o.Flash; float t = o.T, s = o.S == 0 ? 1 : o.S; int lod = o.Lod;
        float dens = (sp.Seg == 0 ? 1 : sp.Seg) * (lod >= 2 ? 1 : lod == 1 ? 0.6f : 0.35f);
        char facet = sp.Facet ? 'I' : 'Z';
        char Tone(int i, char b) => fl != null && fl[i] > 0 ? 'f' : b;
        // curled into a ball: one round body, spots that roll with it, and the species' bristles
        if (o.Curl > 0.6f && o.HasBall)
        {
            var q = o.BallQ; float bx = o.BallX, by = o.BallY, bz = o.BallZ, br = o.BallR;
            emit(facet, Tone(1, 'a'), bx, by, bz, q.x, q.y, q.z, q.w, br, br, br);
            for (int k = 0; k < 8; k++)
            {
                float a = k * 2.399f, y = 1 - (k + 0.5f) / 4, rr = MathF.Sqrt(MathF.Max(0, 1 - y * y));
                QRot(q, MathF.Cos(a) * rr, y, MathF.Sin(a) * rr, TV);
                emit('Z', k % 3 != 0 ? 'b' : 'c', bx + TV.x * br * 0.9f, by + TV.y * br * 0.9f, bz + TV.z * br * 0.9f, q.x, q.y, q.z, q.w, br * 0.22f, br * 0.22f, br * 0.22f);
            }
            if (sp.Deco.Any(d => d.K == "spikes" || d.K == "spines" || d.K == "cilia"))
                for (int k = 0; k < 14; k++)
                {
                    float y = 1 - (k + 0.5f) / 7, rr = MathF.Sqrt(MathF.Max(0, 1 - y * y)), a = k * 2.399f;
                    QRot(q, MathF.Cos(a) * rr, y, MathF.Sin(a) * rr, TV); QFromY(TV.x, TV.y, TV.z, TQ);
                    emit('P', 'b', bx + TV.x * br * 1.1f, by + TV.y * br * 1.1f, bz + TV.z * br * 1.1f, TQ.x, TQ.y, TQ.z, TQ.w, br * 0.12f, br * 0.35f, br * 0.12f);
                }
            return;
        }
        var st = o.St; float fdt = o.Fdt;
        // ---- torso: from low on the pelvis, bowed through the chest, up to the shoulders
        const int k1 = 13, k2 = 26;
        if (has[1] || has[2])
        {
            float pr = (ps[k2 + 7] + ps[k2 + 9]) / 2, cr = (ps[k1 + 7] + ps[k1 + 9]) / 2, tl = sp.TLen == 0 ? 1 : sp.TLen;
            if (has[2]) { A0.x = ps[k2] - ps[k2 + 10] * ps[k2 + 8] * 0.55f; A0.y = ps[k2 + 1] - ps[k2 + 11] * ps[k2 + 8] * 0.55f; A0.z = ps[k2 + 2] - ps[k2 + 12] * ps[k2 + 8] * 0.55f; }
            else { A0.x = ps[k1] - ps[k1 + 10] * ps[k1 + 8]; A0.y = ps[k1 + 1] - ps[k1 + 11] * ps[k1 + 8]; A0.z = ps[k1 + 2] - ps[k1 + 12] * ps[k1 + 8]; }
            float top = has[1] ? 1 : 0.2f;
            A1.x = ps[k1] + ps[k1 + 10] * ps[k1 + 8] * 0.62f * tl * top; A1.y = ps[k1 + 1] + ps[k1 + 11] * ps[k1 + 8] * 0.62f * tl * top; A1.z = ps[k1 + 2] + ps[k1 + 12] * ps[k1 + 8] * 0.62f * tl * top;
            if (!has[1]) { A1.x = ps[k2] + ps[k2 + 10] * ps[k2 + 8]; A1.y = ps[k2 + 1] + ps[k2 + 11] * ps[k2 + 8]; A1.z = ps[k2 + 2] + ps[k2 + 12] * ps[k2 + 8]; }
            CC.x = has[1] ? ps[k1] : (A0.x + A1.x) / 2; CC.y = has[1] ? ps[k1 + 1] - ps[k1 + 11] * ps[k1 + 8] * 0.2f : (A0.y + A1.y) / 2; CC.z = has[1] ? ps[k1 + 2] : (A0.z + A1.z) / 2;
            At(ps, 1, CF);
            if (sp.Hump != 0) { CC.x -= CF.f.x * sp.Hump * cr * 0.6f; CC.y -= CF.f.y * sp.Hump * cr * 0.6f; CC.z -= CF.f.z * sp.Hump * cr * 0.6f; }
            Bowed(A0, CC, A1, 1.15f, st, 4, fdt, B0);
            float ra = sp.Torso[0], rb = sp.Torso[1], rc = sp.Torso[2];
            if (!sp.BandCached) { sp.Band = sp.Deco.FirstOrDefault(d => d.K == "bands"); sp.BandCached = true; }
            var band = sp.Band;
            Func<int, int, char> tn = band != null
                ? (i, _) => i % Math.Max(1, band.Every) == 0 ? Tone(1, 'b') : Tone(i < 4 ? 2 : 1, 'a')
                : (i, _) => Tone(i < 3 ? 2 : 1, 'a');
            float Prof(float u) => u < 0.45f ? Lerp(pr * ra, cr * rb, SmoothU(u / 0.45f))
                : u < 0.82f ? Lerp(cr * rb, cr * rc, SmoothU((u - 0.45f) / 0.37f))
                : cr * rc * MathF.Sqrt(MathF.Max(0.12f, 1 - MathF.Pow((u - 0.82f) / 0.2f, 2)));
            Chain(emit, facet, tn, A0, B0, A1, Prof, 0, 0, 0, dens * 1.25f, sp.Wave != 0 ? 1 : 0, cr * sp.Wave, CF, t, 8, 28);
        }
        // ---- neck and head
        At(ps, 0, HF);
        float hrx = ps[7], hry = ps[8];
        if (has[0])
        {
            if (has[1] && sp.Neck > 0.05f)
            {
                A0.x = ps[k1] + ps[k1 + 10] * ps[k1 + 8] * 0.5f; A0.y = ps[k1 + 1] + ps[k1 + 11] * ps[k1 + 8] * 0.5f; A0.z = ps[k1 + 2] + ps[k1 + 12] * ps[k1 + 8] * 0.5f;
                A1.x = ps[0] - HF.u.x * hry * 0.3f; A1.y = ps[1] - HF.u.y * hry * 0.3f; A1.z = ps[2] - HF.u.z * hry * 0.3f;
                CC.x = (A0.x + A1.x) / 2; CC.y = (A0.y + A1.y) / 2; CC.z = (A0.z + A1.z) / 2;
                float nr = MathF.Max(hrx * 0.38f, ps[k1 + 7] * 0.26f) * (0.75f + 0.25f * MathF.Min(1, sp.Neck));
                Chain(emit, facet, Tone(0, 'a'), A0, CC, A1, nr * 1.2f, nr, 0, dens, 3, 8, t);
            }
            Head(sp, ps, emit, Tone(0, 'a'));
            Eyes(sp, ps, emit, t);
        }
        // ---- arms and legs: chains through elbow and knee, bowed and springy
        float bend = sp.Bend == 0 ? 1.5f : sp.Bend;
        for (int li = 0; li < 4; li++)
        {
            int up = li switch { 0 => 3, 1 => 6, 2 => 9, _ => 12 }, lo = up + 1, end = up + 2, root = li < 2 ? 1 : 2, si = li;
            bool isLeg = li >= 2; var rr = isLeg ? sp.Leg : sp.Arm;
            if (!has[up]) continue;
            int kr = root * 13;
            Ends(ps, up, A0, A1);
            if (D2(A1, ps[kr], ps[kr + 1], ps[kr + 2]) < D2(A0, ps[kr], ps[kr + 1], ps[kr + 2])) Swap(A0, A1);
            float r0 = ps[up * 13 + 7] * rr[0], r1 = (has[lo] ? ps[lo * 13 + 7] : ps[up * 13 + 7]) * rr[1];
            if (has[lo])
            {
                Ends(ps, lo, B0, B1);
                if (D2(B1, A1.x, A1.y, A1.z) < D2(B0, A1.x, A1.y, A1.z)) Swap(B0, B1);
                Bowed(A0, A1, B1, bend, st, si, fdt, CC);
                char tn = fl != null && (fl[up] > 0 || fl[lo] > 0) ? 'f' : isLeg ? 'b' : 'a';
                Chain(emit, facet, tn, A0, CC, B1, r0, r1 * 0.85f, (r0 + r1) * 0.06f, dens, 6, 26, t);
                if (has[end])
                {
                    int ke = end * 13;
                    if (isLeg)
                    {
                        Part(ps, end, TQ); QRot(TQ, 0, 0, -1, TV); var f = sp.Foot; float fr = r1;
                        emit(facet, Tone(end, 'b'), B1.x + TV.x * fr * 0.9f, B1.y - fr * 0.25f, B1.z + TV.z * fr * 0.9f, TQ.x, TQ.y, TQ.z, TQ.w, fr * f[0], fr * f[1], fr * f[2]);
                        if (sp.ClawsCached < 0) sp.ClawsCached = sp.Deco.Any(d => d.K == "claws") ? 1 : 0;
                        if (sp.ClawsCached == 1)
                            for (int sx = -1; sx <= 1; sx++)
                            {
                                QAxis(1, 0, 0, -1.4f, TQ2); QMul(TQ, TQ2, TQ2); QRot(TQ, sx * fr * 0.5f, 0, -fr * f[2] * 1.05f, TV2);
                                emit('P', 'c', B1.x + TV.x * fr * 0.9f + TV2.x, B1.y - fr * 0.3f + TV2.y, B1.z + TV.z * fr * 0.9f + TV2.z, TQ2.x, TQ2.y, TQ2.z, TQ2.w, fr * 0.18f, fr * 0.6f, fr * 0.18f);
                            }
                    }
                    else
                    {
                        float hr = ps[ke + 7] * sp.Hand * 1.15f, dx = B1.x - CC.x, dy = B1.y - CC.y, dz = B1.z - CC.z, dl = Hypot(dx, dy, dz); if (dl == 0) dl = 1;
                        QFromY(dx, dy, dz, TQ);
                        emit(facet, Tone(end, 'a'), B1.x + dx / dl * hr * 0.7f, B1.y + dy / dl * hr * 0.7f, B1.z + dz / dl * hr * 0.7f, TQ.x, TQ.y, TQ.z, TQ.w, hr, hr * 1.15f, hr * 0.8f);
                        Part(ps, end, TQ2); QRot(TQ2, si == 0 ? hr * 0.8f : -hr * 0.8f, hr * 0.2f, -hr * 0.3f, TV);   // the thumb nub
                        emit('Z', Tone(end, 'a'), B1.x + dx / dl * hr * 0.5f + TV.x, B1.y + dy / dl * hr * 0.5f + TV.y, B1.z + dz / dl * hr * 0.5f + TV.z, TQ.x, TQ.y, TQ.z, TQ.w, hr * 0.4f, hr * 0.55f, hr * 0.4f);
                    }
                }
            }
            else
            {
                CC.x = (A0.x + A1.x) / 2; CC.y = (A0.y + A1.y) / 2; CC.z = (A0.z + A1.z) / 2;
                Chain(emit, facet, Tone(up, isLeg ? 'b' : 'a'), A0, CC, A1, r0, r0 * 0.8f, 0, dens, 4, 14, t);
            }
        }
        // ---- decorations
        if (lod >= 1) foreach (var d in sp.Deco) DecoDraw(sp, d, ps, has, emit, t, s);
    }

    static Q4 MulX(Q4 q, float a) { QAxis(1, 0, 0, a, MQX); return QMul(q, MQX, MQX); }

    // ---- heads
    static void Head(Species sp, float[] ps, Emit emit, char tn)
    {
        float hrx = ps[7], hry = ps[8], hrz = ps[9], x = ps[0], y = ps[1], z = ps[2]; var hs = sp.Hs;
        Part(ps, 0, TQ); var q = TQ;
        float sx = hrx * hs[0], sy = hry * hs[1], sz = hrz * hs[2];
        void Put(char sh, char tone, float lx, float ly, float lz, float a, float b, float c, Q4 qq = null)
        {
            QRot(q, lx, ly, lz, TV2); qq ??= q;
            emit(sh, tone, x + TV2.x, y + TV2.y, z + TV2.z, qq.x, qq.y, qq.z, qq.w, a, b, c);
        }
        switch (sp.Head)
        {
            case "rod": for (int i = -1; i <= 1; i++) Put('Z', tn, 0, i * sy * 0.45f, 0, sx * 0.85f, sy * 0.6f, sz * 0.85f); break;
            case "capsid": Put('I', tn, 0, sy * 0.1f, 0, sx, sy, sz); Put('Z', 'c', 0, sy * 0.1f, 0, sx * 0.55f, sy * 0.55f, sz * 0.55f); break;
            case "disc": Put('I', tn, 0, 0, 0, sx * 1.1f, sy * 0.75f, sz * 1.1f); Put('T', 'c', 0, 0, 0, sx * 0.95f, sy * 0.8f, sz * 0.95f); break;
            case "bell": Put('Z', tn, 0, sy * 0.25f, 0, sx * 1.15f, sy * 0.9f, sz * 1.15f); Put('T', 'b', 0, -sy * 0.3f, 0, sx * 1.05f, sy * 1.2f, sz * 1.05f); break;
            case "cone": Put('Z', tn, 0, -sy * 0.2f, 0, sx, sy * 0.8f, sz); Put('P', tn, 0, sy * 0.55f, sz * 0.25f, sx * 0.9f, sy * 1.2f, sz * 0.9f); break;
            case "snout":
                Put('Z', tn, 0, 0, 0, sx, sy, sz);
                for (int i = 1; i <= 3; i++) Put('Z', 'b', 0, -sy * 0.1f, -sz * (0.75f + i * 0.16f), sx * (0.42f - i * 0.05f), sy * (0.42f - i * 0.05f), sz * 0.25f);
                Put('T', 'c', 0, -sy * 0.1f, -sz * 1.25f, sx * 0.25f, sy * 0.4f, sz * 0.25f, MulX(q, PI / 2)); break;
            case "shield": Put('Z', tn, 0, 0, 0, sx, sy * 0.85f, sz); Put('Z', 'b', 0, sy * 0.3f, sz * 0.15f, sx * 1.1f, sy * 0.45f, sz * 1.15f); break;
            case "wheel": Put('Z', tn, 0, 0, 0, sx, sy, sz); break;
            case "star":
                Put('I', tn, 0, 0, 0, sx, sy, sz);
                for (int k = 0; k < 6; k++) { float a = k / 6f * TAU; QFromY(MathF.Cos(a), 0.2f, MathF.Sin(a), TQ2); QMul(q, TQ2, TQ2); Put('P', 'b', MathF.Cos(a) * sx * 1.05f, sy * 0.2f, MathF.Sin(a) * sz * 1.05f, sx * 0.25f, sy * 0.7f, sz * 0.25f, TQ2); }
                break;
            case "blob": Put('Z', tn, 0, 0, 0, sx, sy, sz); Put('Z', tn, sx * 0.35f, sy * 0.35f, sz * 0.2f, sx * 0.55f, sy * 0.5f, sz * 0.55f); break;
            case "slipper": Put('Z', tn, 0, sy * 0.1f, 0, sx, sy, sz * 0.9f); Put('Z', 'b', 0, -sy * 0.15f, -sz * 0.55f, sx * 0.45f, sy * 0.5f, sz * 0.3f); break;
            case "drop": Put('Z', tn, 0, -sy * 0.15f, 0, sx, sy * 0.8f, sz); Put('P', tn, 0, sy * 0.65f, 0, sx * 0.75f, sy * 0.9f, sz * 0.75f); break;
            case "cube": Put('B', tn, 0, 0, 0, sx * 1.6f, sy * 1.6f, sz * 1.6f); break;
            default: Put(sp.Facet ? 'I' : 'Z', tn, 0, 0, 0, sx, sy, sz); break;
        }
    }

    // ---- eyes: glowing domes with pupils, a visor, or a single nauplius eye
    static void Eyes(Species sp, float[] ps, Emit emit, float t)
    {
        var E = sp.Eyes; if (E == null || E.N == 0) return;
        Part(ps, 0, TQ); var q = TQ; float hx = ps[7] * sp.Hs[0], hy = ps[8] * sp.Hs[1], hz = ps[9] * sp.Hs[2], x = ps[0], y = ps[1], z = ps[2];
        if (E.Visor) { QRot(q, 0, E.Y * hy, -hz * 0.98f, TV); emit('Z', 'e', x + TV.x, y + TV.y, z + TV.z, q.x, q.y, q.z, q.w, hx * 0.8f, hy * 0.18f, hz * 0.18f); return; }
        float bl = (t * 0.37f + ps[0] * 0.01f) % 4; if (bl < 0) bl += 0; // JS % keeps sign; negative values never blink, same as the original
        float blink = bl >= 0 && bl < 0.08f ? 0.15f : 1;
        for (int i = 0; i < E.N; i++)
        {
            float u = E.N == 1 ? 0 : ((float)i / (E.N - 1) - 0.5f) * 2, ex = u * E.Spread * hx, ey = E.Y * hy + (E.N > 2 ? -MathF.Abs(u) * 0.1f * hy : 0);
            float zz = MathF.Sqrt(MathF.Max(0.05f, 1 - (ex / hx) * (ex / hx) - (ey / hy) * (ey / hy))) * hz;
            float es = E.Size * MathF.Min(hx, hy) * 2;
            QRot(q, ex, ey, -zz * 0.96f, TV); emit('Z', 'e', x + TV.x, y + TV.y, z + TV.z, q.x, q.y, q.z, q.w, es, es * (E.Slit ? 0.35f : 1) * blink, es * 0.6f);
            QRot(q, ex, ey, -zz * 0.96f - es * 0.45f, TV); emit('Z', 'k', x + TV.x, y + TV.y, z + TV.z, q.x, q.y, q.z, q.w, es * 0.45f, es * 0.45f * (E.Slit ? 0.4f : 1) * blink, es * 0.3f);
        }
    }

    // ---- decorations, attached to the chest, pelvis or head frames
    static void DecoDraw(Species sp, Deco d, float[] ps, bool[] has, Emit emit, float t, float s)
    {
        float cr = ps[13 + 7], hr = ps[7] * sp.Hs[0];
        switch (d.K)
        {
            case "flagella":
            case "tentacles":
                {
                    string from = d.K == "tentacles" ? "head" : d.From; int pi = from == "head" ? 0 : from == "pelvis" ? 2 : 1; if (!has[pi]) return;
                    At(ps, pi, CF); float x = ps[pi * 13], y = ps[pi * 13 + 1], z = ps[pi * 13 + 2]; int n = d.N == 0 ? 1 : d.N; float L = (d.Len == 0 ? 1.5f : d.Len) * s * (d.K == "tentacles" ? 0.8f : 1);
                    for (int j = 0; j < n; j++)
                    {
                        float side = n == 1 ? 0 : ((float)j / (n - 1) - 0.5f) * 1.6f, a = (float)j / n * TAU;
                        float bx, by, bz;
                        if (d.K == "tentacles")
                        {
                            float rr = hr * 0.7f;
                            bx = x + CF.r.x * MathF.Cos(a) * rr + CF.f.x * MathF.Sin(a) * rr - CF.u.x * ps[8] * 0.3f; by = y + CF.r.y * MathF.Cos(a) * rr - CF.u.y * ps[8] * 0.4f; bz = z + CF.r.z * MathF.Cos(a) * rr + CF.f.z * MathF.Sin(a) * rr - CF.u.z * ps[8] * 0.3f;
                        }
                        else
                        {
                            bool hd = from == "head"; float off = hd ? ps[8] * 0.9f : from == "pelvis" ? ps[26 + 8] * 0.3f : cr * 0.9f;
                            bx = x - CF.f.x * (hd ? 0 : off) + CF.r.x * side * cr * 0.4f + (hd ? CF.u.x * off : 0); by = y - CF.f.y * (hd ? 0 : off) + (hd ? CF.u.y * off : 0); bz = z - CF.f.z * (hd ? 0 : off) + CF.r.z * side * cr * 0.4f + (hd ? CF.u.z * off : 0);
                        }
                        float d0, d1, d2;
                        if (d.K == "tentacles") { d0 = -CF.u.x; d1 = -CF.u.y; d2 = -CF.u.z; }
                        else if (from == "head") { d0 = CF.u.x * 0.6f + CF.f.x * 0.5f; d1 = CF.u.y * 0.6f + 0.2f; d2 = CF.u.z * 0.6f + CF.f.z * 0.5f; }
                        else { d0 = -CF.f.x; d1 = -CF.f.y - 0.35f; d2 = -CF.f.z; }
                        float ph = t * 7 + j * 1.7f, sw = MathF.Sin(ph) * 0.35f * L;
                        A0.Set(bx, by, bz);
                        CC.x = bx + d0 * L * 0.5f + CF.r.x * sw; CC.y = by + d1 * L * 0.5f; CC.z = bz + d2 * L * 0.5f + CF.r.z * sw;
                        A1.x = bx + d0 * L + CF.r.x * -sw * 0.7f; A1.y = by + d1 * L - (d.K == "tentacles" ? 0 : 0.15f * L); A1.z = bz + d2 * L + CF.r.z * -sw * 0.7f;
                        float r0 = d.K == "tentacles" ? hr * 0.12f : cr * 0.12f;
                        Chain(emit, 'Z', d.K == "tentacles" ? 'b' : 'c', A0, CC, A1, r0, r0 * 0.3f, 0, 0.8f, 5, 14, t);
                    }
                    break;
                }
            case "cilia":
                {
                    if (!has[1]) return; At(ps, 1, CF); float x = ps[13], y = ps[14], z = ps[15]; int n = d.N == 0 ? 18 : d.N;
                    for (int j = 0; j < n; j++)
                    {
                        float a = (float)j / n * TAU, h = ((j * 7) % 5) / 5f - 0.5f, wv = MathF.Sin(t * 9 + j * 0.9f) * 0.4f;
                        float ox = MathF.Cos(a), oz = MathF.Sin(a), nx = CF.r.x * ox + CF.f.x * oz, ny = CF.r.y * ox + CF.f.y * oz + 0.25f + wv * 0.3f, nz = CF.r.z * ox + CF.f.z * oz;
                        QFromY(nx, ny, nz, TQ); float R = cr * (sp.Torso[1] == 0 ? 1 : sp.Torso[1]) * 1.02f;
                        emit('P', 'b', x + nx * R + CF.u.x * h * ps[13 + 8], y + ny * R * 0.4f + CF.u.y * h * ps[13 + 8] * 1.4f, z + nz * R + CF.u.z * h * ps[13 + 8], TQ.x, TQ.y, TQ.z, TQ.w, cr * 0.07f, cr * 0.55f, cr * 0.07f);
                    }
                    break;
                }
            case "spikes":
            case "spines":
                {
                    int pi = d.K == "spikes" ? 0 : 1; if (!has[pi]) return; Part(ps, pi, TQ2);
                    float x = ps[pi * 13], y = ps[pi * 13 + 1], z = ps[pi * 13 + 2]; int n = d.N == 0 ? 14 : d.N; float R = pi != 0 ? cr * 1.05f : hr * 1.02f, L = pi != 0 ? cr * 1.4f : hr * 0.75f;
                    for (int j = 0; j < n; j++)
                    {
                        float yy = 1 - (j + 0.5f) / n * 2, rr = MathF.Sqrt(1 - yy * yy), a = j * 2.399f;
                        QRot(TQ2, MathF.Cos(a) * rr, yy, MathF.Sin(a) * rr, TV); QFromY(TV.x, TV.y, TV.z, TQ);
                        emit('C', 'b', x + TV.x * (R + L * 0.3f), y + TV.y * (R + L * 0.3f), z + TV.z * (R + L * 0.3f), TQ.x, TQ.y, TQ.z, TQ.w, L * 0.06f, L * 0.6f, L * 0.06f);
                        emit(d.K == "spikes" ? 'Z' : 'P', 'c', x + TV.x * (R + L * 0.65f), y + TV.y * (R + L * 0.65f), z + TV.z * (R + L * 0.65f), TQ.x, TQ.y, TQ.z, TQ.w, L * 0.14f, L * (d.K == "spikes" ? 0.14f : 0.4f), L * 0.14f);
                    }
                    break;
                }
            case "lumps":
                {
                    if (!has[1]) return; At(ps, 1, CF); float x = ps[13], y = ps[14], z = ps[15]; int n = d.N == 0 ? 5 : d.N;
                    for (int j = 0; j < n; j++)
                    {
                        float a = (float)j / n * TAU + 0.5f, jig = 1 + 0.12f * MathF.Sin(t * 4 + j * 2), R = cr * 0.95f;
                        emit('Z', j % 2 != 0 ? 'a' : 'b', x + (CF.r.x * MathF.Cos(a) + CF.f.x * MathF.Sin(a)) * R, y + ((j % 3) - 1) * cr * 0.5f, z + (CF.r.z * MathF.Cos(a) + CF.f.z * MathF.Sin(a)) * R, 0, 0, 0, 1, cr * 0.42f * jig, cr * 0.38f * jig, cr * 0.42f * jig);
                    }
                    break;
                }
            case "nucleus":
                {
                    if (!has[1]) return; At(ps, 1, CF); float x = ps[13], y = ps[14], z = ps[15], p = 1 + 0.08f * MathF.Sin(t * 3);
                    emit('Z', 'c', x + CF.f.x * cr * 0.75f, y + cr * 0.15f, z + CF.f.z * cr * 0.75f, 0, 0, 0, 1, cr * 0.38f * p, cr * 0.38f * p, cr * 0.2f * p); break;
                }
            case "antennae":
            case "stalkEyes":
                {
                    if (!has[0]) return; At(ps, 0, CF); float x = ps[0], y = ps[1], z = ps[2], L = (d.Len == 0 ? 1.6f : d.Len) * s * (d.K == "stalkEyes" ? 0.35f : 1);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        A0.x = x + CF.r.x * side * hr * 0.5f + CF.u.x * ps[8] * 0.6f; A0.y = y + CF.u.y * ps[8] * 0.7f; A0.z = z + CF.r.z * side * hr * 0.5f + CF.u.z * ps[8] * 0.6f;
                        float sw = MathF.Sin(t * 2.5f + side) * 0.08f * L;
                        CC.x = A0.x + CF.r.x * side * L * 0.35f + CF.u.x * L * 0.4f; CC.y = A0.y + L * 0.45f; CC.z = A0.z + CF.r.z * side * L * 0.35f + CF.u.z * L * 0.4f - CF.f.z * L * 0.1f;
                        A1.x = A0.x + CF.r.x * side * L * 0.8f - CF.f.x * L * 0.4f + sw; A1.y = A0.y + L * (d.K == "stalkEyes" ? 0.8f : 0.5f); A1.z = A0.z + CF.r.z * side * L * 0.8f - CF.f.z * L * 0.4f + sw;
                        Chain(emit, 'Z', 'b', A0, CC, A1, hr * 0.08f, hr * 0.04f, 0, 0.8f, 5, 16, t);
                        if (d.K == "stalkEyes")
                        {
                            emit('Z', 'e', A1.x, A1.y, A1.z, 0, 0, 0, 1, hr * 0.22f, hr * 0.22f, hr * 0.22f);
                            emit('Z', 'k', A1.x + CF.f.x * hr * 0.15f, A1.y, A1.z + CF.f.z * hr * 0.15f, 0, 0, 0, 1, hr * 0.1f, hr * 0.1f, hr * 0.1f);
                        }
                    }
                    break;
                }
            case "crest": case "mohawk": case "horn": case "crown": case "halo": case "pollen": case "fins":
                {
                    if (!has[0] && d.K != "fins") return; Part(ps, 0, TQ2); float x = ps[0], y = ps[1], z = ps[2], hy = ps[8] * sp.Hs[1];
                    void Put(char sh, char tn, float lx, float ly, float lz, float a, float b, float c, float rx = 0)
                    {
                        QRot(TQ2, lx, ly, lz, TV); var q = TQ2; if (rx != 0) { QAxis(1, 0, 0, rx, TQ); q = QMul(TQ2, TQ, TQ); }
                        emit(sh, tn, x + TV.x, y + TV.y, z + TV.z, q.x, q.y, q.z, q.w, a, b, c);
                    }
                    if (d.K == "crest") Put('P', 'c', 0, hy * 0.9f, hr * 0.3f, hr * 0.12f, hy * 0.9f, hr * 0.7f, -0.6f);
                    if (d.K == "mohawk") for (int j = 0; j < 6; j++) Put('P', 'c', 0, hy * (0.85f - MathF.Abs(j - 2.5f) * 0.04f), hr * (-0.6f + j * 0.25f), hr * 0.1f, hy * (0.5f + MathF.Sin(t * 6 + j) * 0.05f), hr * 0.12f, -0.3f + j * 0.15f);
                    if (d.K == "horn") Put('P', 'c', 0, hy * 0.6f, -hr * 0.6f, hr * 0.2f, hy * 0.9f, hr * 0.2f, -0.7f);
                    if (d.K == "crown") for (int j = 0; j < 8; j++) { float a = j / 8f * TAU, wv = MathF.Sin(t * 10 + j) * 0.2f; Put('P', 'c', MathF.Cos(a) * hr * 0.75f, hy * 0.85f, MathF.Sin(a) * hr * 0.75f, hr * 0.1f, hy * (0.45f + wv * 0.3f), hr * 0.1f, wv); }
                    if (d.K == "halo") Put('T', 'c', 0, hy * 1.45f + MathF.Sin(t * 2) * hy * 0.05f, 0, hr * 0.8f, hy * 2, hr * 0.8f);
                    if (d.K == "pollen") for (int j = 0; j < 7; j++) { float a = j * 2.399f, yy = 0.5f + (j % 3) * 0.2f; Put('I', 'b', MathF.Cos(a) * hr * 0.7f, hy * yy, MathF.Sin(a) * hr * 0.7f, hr * 0.28f, hr * 0.28f, hr * 0.28f); }
                    if (d.K == "fins" && has[1])
                    {
                        Part(ps, 1, TQ2); float cx2 = ps[13], cy2 = ps[14], cz2 = ps[15];
                        for (int side = -1; side <= 1; side += 2) { QRot(TQ2, side * cr * 0.9f, cr * 0.4f, cr * 0.5f, TV); QAxis(0, 0, 1, side * -0.9f, TQ); QMul(TQ2, TQ, TQ); emit('P', 'c', cx2 + TV.x, cy2 + TV.y, cz2 + TV.z, TQ.x, TQ.y, TQ.z, TQ.w, cr * 0.1f, cr * 1.2f, cr * 0.5f); }
                    }
                    break;
                }
            case "ring":
            case "collar":
                {
                    if (!has[1]) return; Part(ps, 1, TQ2); float x = ps[13], y = ps[14], z = ps[15];
                    QRot(TQ2, 0, ps[13 + 8] * (d.K == "collar" ? 0.85f : 0.1f), 0, TV);
                    emit('T', 'c', x + TV.x, y + TV.y, z + TV.z, TQ2.x, TQ2.y, TQ2.z, TQ2.w, cr * 0.92f, cr * 1.1f, cr * 0.92f); break;
                }
            case "dots":
                {
                    if (!has[1] && !has[0]) return; int pi = has[0] ? 0 : 1; Part(ps, pi, TQ2);
                    float x = ps[pi * 13], y = ps[pi * 13 + 1], z = ps[pi * 13 + 2], R = pi != 0 ? cr : hr; int n = d.N == 0 ? 12 : d.N;
                    for (int j = 0; j < n; j++)
                    {
                        float yy = 1 - (j + 0.5f) / n * 2, rr = MathF.Sqrt(1 - yy * yy), a = j * 2.399f;
                        QRot(TQ2, MathF.Cos(a) * rr * R, yy * R * (pi != 0 ? 1 : ps[8] * sp.Hs[1] / hr), MathF.Sin(a) * rr * R, TV);
                        emit('Z', 'c', x + TV.x, y + TV.y, z + TV.z, 0, 0, 0, 1, R * 0.12f, R * 0.12f, R * 0.12f);
                    }
                    break;
                }
            case "plates":
                {
                    if (!has[1]) return; Part(ps, 1, TQ2); float x = ps[13], y = ps[14], z = ps[15], hy1 = ps[13 + 8];
                    for (int j = 0; j < 4; j++) { QRot(TQ2, 0, hy1 * (0.7f - j * 0.45f), cr * 0.75f, TV); emit('Z', 'b', x + TV.x, y + TV.y, z + TV.z, TQ2.x, TQ2.y, TQ2.z, TQ2.w, cr * 1.05f, hy1 * 0.22f, cr * 0.5f); }
                    break;
                }
            case "sacks":
            case "pack":
                {
                    if (!has[1]) return; Part(ps, 1, TQ2); float x = ps[13], y = ps[14], z = ps[15];
                    if (d.K == "pack") { QRot(TQ2, 0, 0, cr * 1.1f, TV); emit('Z', 'c', x + TV.x, y + TV.y, z + TV.z, TQ2.x, TQ2.y, TQ2.z, TQ2.w, cr * 0.75f, cr * 0.9f, cr * 0.5f); }
                    else for (int side = -1; side <= 1; side += 2) { QRot(TQ2, side * cr * 0.55f, -cr * 0.3f, cr * 0.9f, TV); emit('Z', 'b', x + TV.x, y + TV.y, z + TV.z, 0, 0, 0, 1, cr * 0.4f, cr * 0.5f, cr * 0.4f); }
                    break;
                }
        }
    }

    // ---- armor: each plate is a patch of scales hugging its part, in the chosen style
    public static readonly Dictionary<string, (char shape, float a, float b, float c)> ARMOR_SHAPE = new()
    {
        ["chitin"] = ('Z', 1.05f, 0.16f, 0.9f), ["capsid"] = ('I', 0.9f, 0.4f, 0.9f), ["spines"] = ('P', 0.32f, 1.0f, 0.32f), ["glass"] = ('B', 0.95f, 0.12f, 0.95f), ["slime"] = ('Z', 0.9f, 0.35f, 0.9f),
        ["shell"] = ('Z', 1.1f, 0.45f, 1.0f), ["coral"] = ('I', 0.6f, 0.6f, 0.6f), ["scales"] = ('Z', 0.7f, 0.18f, 0.95f), ["cilia"] = ('P', 0.14f, 0.9f, 0.14f),
        ["bubbles"] = ('Z', 0.75f, 0.75f, 0.75f), ["frustule"] = ('C', 0.95f, 0.22f, 0.95f), ["barnacles"] = ('P', 0.7f, 0.55f, 0.7f), ["rings"] = ('T', 0.75f, 0.9f, 0.75f), ["plaque"] = ('B', 0.95f, 0.32f, 0.7f),
        ["pearls"] = ('Z', 0.5f, 0.5f, 0.5f), ["pollen"] = ('I', 0.62f, 0.62f, 0.62f),
    };

    public static void ArmorPiece(string style, ArmorPieceSpec p, float[] ps, Emit emit, char tone, float t)
    {
        if (!ARMOR_SHAPE.TryGetValue(style, out var S)) return;
        int k = p.Part * 13; float rx = ps[k + 7] * p.Sc, ry = ps[k + 8], rz = ps[k + 9] * p.Sc;
        Part(ps, p.Part, TQ2);
        float size = MathF.Min(rx, rz) * 0.95f + 0.02f, arcR = p.Arc * PI / 180;
        int rows = Math.Max(1, (int)MathF.Round((p.Y1 - p.Y0) * ry / size)), cols = Math.Max(1, (int)MathF.Round(arcR * (rx + rz) / 2 / size));
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float yy = Lerp(p.Y0, p.Y1, (r + 0.5f) / rows) * ry, a = p.Phi * PI / 180 + (cols == 1 ? 0 : ((c + 0.5f) / cols - 0.5f) * arcR) + (r % 2 != 0 ? 0.5f * arcR / cols : 0);
                float nx = MathF.Sin(a), nz = -MathF.Cos(a);
                QRot(TQ2, nx * rx * 0.98f, yy, nz * rz * 0.98f, TV); QRot(TQ2, nx, 0, nz, TV2); QFromY(TV2.x, TV2.y, TV2.z, TQ);
                float wob = style == "slime" || style == "bubbles" ? 1 + 0.08f * MathF.Sin(t * 3 + r + c) : style == "cilia" ? 1 + 0.2f * MathF.Sin(t * 8 + c) : 1;
                emit(S.shape, tone, ps[k] + TV.x, ps[k + 1] + TV.y, ps[k + 2] + TV.z, TQ.x, TQ.y, TQ.z, TQ.w, size * S.a * wob, size * S.b * wob * (style == "spines" || style == "cilia" ? 1.6f : 1), size * S.c * wob);
            }
    }

    // ---- a standing skeleton for previews (the same 15-part layout the game uses)
    public static float[] RestPose(float s, float t)
    {
        var ps = new float[15 * 13]; float sw = MathF.Sin(t * 1.6f) * 0.18f, br = MathF.Sin(t * 2.2f) * 0.012f;
        void Set(int i, float[] a, float[] b, float r, float[] fwd)
        {
            int k = i * 13; float dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2], L = Hypot(dx, dy, dz); if (L == 0) L = 1;
            ps[k] = (a[0] + b[0]) / 2 * s; ps[k + 1] = (a[1] + b[1]) / 2 * s; ps[k + 2] = (a[2] + b[2]) / 2 * s;
            float yx = dx / L, yy = dy / L, yz = dz / L, fx = fwd[0], fy = fwd[1], fz = fwd[2], dd = fx * yx + fy * yy + fz * yz;
            fx -= yx * dd; fy -= yy * dd; fz -= yz * dd; float fl = Hypot(fx, fy, fz); if (fl == 0) fl = 1; fx /= fl; fy /= fl; fz /= fl;
            float zx = -fx, zy = -fy, zz = -fz, xx = yy * zz - yz * zy, xy = yz * zx - yx * zz, xz = yx * zy - yy * zx;
            QFromBasis(xx, xy, xz, yx, yy, yz, zx, zy, zz, TQ);
            ps[k + 3] = TQ.x; ps[k + 4] = TQ.y; ps[k + 5] = TQ.z; ps[k + 6] = TQ.w; ps[k + 7] = r * s; ps[k + 8] = L / 2 * s; ps[k + 9] = r * s; ps[k + 10] = yx; ps[k + 11] = yy; ps[k + 12] = yz;
        }
        float[] F = { 0, 0, -1 };
        Set(2, new[] { 0, 0.86f, 0 }, new[] { 0, 1.08f, 0 }, 0.255f, F); Set(1, new[] { 0, 1.06f + br, 0 }, new[] { 0, 1.62f + br, 0 }, 0.3f, F);
        Set(0, new[] { 0, 1.62f + br, 0 }, new[] { 0, 1.98f + br, 0 }, 0.18f, F); ps[7] = 0.18f * s; ps[8] = 0.18f * s; ps[9] = 0.18f * s;
        for (int side = -1; side <= 1; side += 2)
        {
            int o = side < 0 ? 0 : 3;
            float[] sh = { side * 0.3f, 1.54f + br, 0 }, el = { side * (0.36f + MathF.Abs(sw) * 0.1f), 1.22f + br, 0.05f + sw * side * 0.3f }, wr = { side * 0.4f, 0.95f, -0.06f + sw * side * 0.6f };
            Set(3 + o, sh, el, 0.12f, F); Set(4 + o, el, wr, 0.11f, F); Set(5 + o, wr, new[] { wr[0], wr[1] - 0.12f, wr[2] }, 0.11f, F);
            float[] hp = { side * 0.12f, 0.9f, 0 }, kn = { side * 0.13f, 0.5f, -0.05f - sw * side * 0.15f }, an = { side * 0.13f, 0.09f, sw * side * 0.15f };
            Set(9 + o, hp, kn, 0.15f, F); Set(10 + o, kn, an, 0.13f, F);
            int k = (11 + o) * 13; Set(11 + o, new[] { an[0], 0.04f, an[2] }, new[] { an[0], 0.12f, an[2] - 0.001f }, 0.06f, F); ps[k + 7] = 0.12f * s; ps[k + 8] = 0.07f * s; ps[k + 9] = 0.17f * s;
        }
        return ps;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using GermBall.Render;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>How one body segment of a medicine hero is built.</summary>
public sealed class SegStyle
{
    /// <summary>capsule, pill, tablet, disc, cylinder, box, sphere, cone, vial, cap, helmet, plunger, bottle.</summary>
    public string Shape = "capsule";
    public float Thick = 1, Len = 1;
    /// <summary>Main tone and second tone (a body, b second colour, c glow, d armor colour, e visor/eye, k dark).</summary>
    public char Tone = 'a', Tone2 = 'b';
    public bool TwoTone;
    /// <summary>-0.6..0.6: positive makes the top end wider than the bottom (a heroic V), negative the reverse.</summary>
    public float Taper;
    /// <summary>Surface detail: none, score, rivets, vents, stripe, rings, ridges.</summary>
    public string Detail = "none";
    /// <summary>Heads only: the visor (default = the shape's own, slit, wrap, dual, mono, none) and what's on top (none, stub, spike, dish, twin).</summary>
    public string Visor = "default", Antenna = "none";
    /// <summary>Anchor offset for a limb root (head, upper arms, thighs): moves the whole limb, in units of the torso's radius,
    /// in the body frame (x right, y up, z back).</summary>
    public float OX, OY, OZ;
    public SegStyle Clone() => (SegStyle)MemberwiseClone();
}

/// <summary>An extra shape fixed to a segment (shoulder pads, needles, caps, fins...). Offsets are in units of the segment's radius.</summary>
public sealed class Attach
{
    public int Part = 1; public string Shape = "sphere";
    public float X, Y, Z, Rx, Ry, Rz, Sx = 0.5f, Sy = 0.5f, Sz = 0.5f;
    public char Tone = 'c';
    public Attach Clone() => (Attach)MemberwiseClone();
}

/// <summary>A medicine hero: colours, one style per segment (15 segments, the same layout as the skeleton) and attachments.</summary>
public sealed class MedSpec
{
    public string Key, Name, Blurb;
    public SpeciesColors Col = new();
    /// <summary>16 segments: the skeleton's 15 parts plus the abdomen (index 15), drawn from the torso bone below the chest.</summary>
    public SegStyle[] Seg = new SegStyle[16];
    /// <summary>Proportions: overall height and arm/leg length multipliers.</summary>
    public float Height = 1, ArmLen = 1, LegLen = 1;
    public List<Attach> Att = new();
    public string ArmorStyle = "plate";
    /// <summary>Chest/shoulder width multiplier: heroes are drawn broad-shouldered and narrow-waisted.</summary>
    public float Bulk = 1;
    public MedSpec Clone()
    {
        var m = (MedSpec)MemberwiseClone(); m.Col = Col.Clone(); m.Seg = Seg.Select(s => s?.Clone()).ToArray(); m.Att = Att.Select(a => a.Clone()).ToList(); return m;
    }
}

/// <summary>
/// The heroes are medicine: a gelcap, a scored tablet, a pill bottle and a syringe, plus the player's own design. Every body
/// segment is one clean solid (a capsule, a tablet, a vial...) instead of a chain of blobs, so silhouettes read at a glance
/// and nothing wobbles or clips. Uses the same pose layout and emit callback as GermDraw.
/// </summary>
public static class MedDraw
{
    // ------------------------------------------------------------------ the archetypes
    static SegStyle S(string shape, float thick = 1, float len = 1, char tone = 'a', char tone2 = 'b', bool two = false) => new() { Shape = shape, Thick = thick, Len = len, Tone = tone, Tone2 = tone2, TwoTone = two };
    static Attach At(int part, string shape, float x, float y, float z, float sx, float sy, float sz, char tone, float rx = 0, float ry = 0, float rz = 0) => new() { Part = part, Shape = shape, X = x, Y = y, Z = z, Sx = sx, Sy = sy, Sz = sz, Tone = tone, Rx = rx, Ry = ry, Rz = rz };
    static SegStyle[] Body(SegStyle head, SegStyle torso, SegStyle pelvis, SegStyle uarm, SegStyle farm, SegStyle hand, SegStyle thigh, SegStyle shin, SegStyle foot)
        => new[] { head, torso, pelvis, uarm, farm, hand, uarm.Clone(), farm.Clone(), hand.Clone(), thigh, shin, foot, thigh.Clone(), shin.Clone(), foot.Clone(), new SegStyle { Shape = "none", Thick = 0.92f, Tone = torso.Tone2, Tone2 = torso.Tone } };
    static List<Attach> Mirror(params Attach[] a)
    {   // attachments on a left part get a mirrored twin on the right part
        var o = new List<Attach>();
        foreach (var x in a)
        {
            o.Add(x);
            int mp = x.Part switch { 3 => 6, 4 => 7, 5 => 8, 9 => 12, 10 => 13, 11 => 14, _ => -1 };
            if (mp >= 0) { var m = x.Clone(); m.Part = mp; m.X = -x.X; m.Ry = -x.Ry; m.Rz = -x.Rz; o.Add(m); }
        }
        return o;
    }

    public static readonly Dictionary<string, MedSpec> PRESETS = new()
    {
        // the hero: a red-and-white gelcap. Capsule helmet with a visor, pill-half pauldrons, a glowing medical cross
        ["gelcap"] = new MedSpec
        {
            Key = "gelcap", Name = "Gelcap", Blurb = "Two-tone capsule hero", Bulk = 1.15f,
            Col = new SpeciesColors { A = 0xe8303c, B = 0xf4f1ec, C = 0x7affef, E = 0x7affef, D = 0xdfe6ee },
            Seg = Body(S("helmet", 0.95f, 1, 'a', 'b'), S("capsule", 0.95f, 1.1f, 'a', 'b', true), S("capsule", 0.95f, 1, 'b'), S("capsule", 1, 1, 'a'), S("capsule", 0.95f, 1, 'b'), S("sphere", 1.15f, 1, 'b'),
                S("capsule", 1, 1, 'b'), S("capsule", 0.95f, 1, 'a'), S("boot", 1, 1, 'a', 'b')),
            Att = Mirror(At(3, "pauldron", -0.25f, 0.95f, 0, 1.45f, 1.05f, 1.45f, 'a', rz: 18), At(1, "cross", 0, 0.25f, -1.0f, 0.55f, 0.55f, 0.12f, 'c')),
        },
        // the marksman: a scored white tablet. Disc head with a slit visor, tablet torso with the score line, blue trim
        ["tablet"] = new MedSpec
        {
            Key = "tablet", Name = "Tablet", Blurb = "Scored disc marksman", Bulk = 1.05f,
            Col = new SpeciesColors { A = 0xf2f4f8, B = 0x2f6fd8, C = 0x5fd8ff, E = 0x5fd8ff, D = 0x9fd4ff },
            Seg = Body(S("tablethead", 1.1f), S("tablet", 1.1f, 1.05f), S("capsule", 0.85f, 1, 'b'), S("pill", 0.85f, 1.05f, 'a'), S("pill", 0.8f, 1.05f, 'b'), S("sphere", 1, 1, 'a'),
                S("pill", 0.85f, 1.05f, 'a'), S("pill", 0.8f, 1.05f, 'b'), S("boot", 0.9f, 1, 'b', 'a')),
            Att = Mirror(At(3, "disc", 0, 1.0f, 0, 1.6f, 0.35f, 1.6f, 'b'), At(0, "box", 0, 0.1f, -1.0f, 1.3f, 0.12f, 0.1f, 'e')),
        },
        // the tank: an amber pill bottle with a white child-proof cap and a label, huge fists
        ["bottle"] = new MedSpec
        {
            Key = "bottle", Name = "Pill Bottle", Blurb = "Child-proof tank", Bulk = 1.3f,
            Col = new SpeciesColors { A = 0xd8862a, B = 0xf6f2e8, C = 0xff4848, E = 0x9effa0, D = 0xbfc6cc },
            Seg = Body(S("cap", 1.15f, 1, 'b'), S("bottle", 1.3f, 1.05f, 'a', 'b'), S("cylinder", 1.15f, 1, 'a'), S("capsule", 1.25f, 1, 'b'), S("capsule", 1.25f, 1, 'a'), S("sphere", 1.6f, 1, 'b'),
                S("capsule", 1.2f, 0.95f, 'a'), S("capsule", 1.2f, 0.95f, 'b'), S("boot", 1.25f, 1, 'b', 'a')),
            Att = Mirror(At(3, "pauldron", -0.25f, 0.9f, 0, 1.5f, 1.15f, 1.5f, 'b', rz: 18)),
        },
        // the scout: a syringe. Plunger head, a vial torso full of glowing serum, a needle on the back
        ["syringe"] = new MedSpec
        {
            Key = "syringe", Name = "Syringe", Blurb = "Needle-quick scout", Bulk = 0.95f,
            Col = new SpeciesColors { A = 0xe9f2f6, B = 0x8a96a8, C = 0xff5ce1, E = 0xff5ce1, D = 0xd0d8e0 },
            Seg = Body(S("plunger", 0.9f), S("vial", 0.85f, 1.1f, 'a', 'c'), S("cylinder", 0.75f, 1, 'b'), S("pill", 0.7f, 1.05f, 'a'), S("pill", 0.65f, 1.1f, 'b'), S("sphere", 0.85f, 1, 'a'),
                S("pill", 0.7f, 1.1f, 'a'), S("pill", 0.65f, 1.1f, 'b'), S("boot", 0.8f, 1, 'b', 'a')),
            Att = Mirror(At(1, "needle", 0, -0.3f, 1.25f, 0.35f, 3.2f, 0.35f, 'd', rx: -160), At(3, "disc", 0, 0.95f, 0, 1.4f, 0.25f, 1.4f, 'b')),
        },
        // extra looks for the player
        ["caplet"] = new MedSpec
        {
            Key = "caplet", Name = "Caplet", Blurb = "Coated, sleek and fast", Bulk = 1.0f,
            Col = new SpeciesColors { A = 0x3ad08a, B = 0x0f4a34, C = 0xe8ff6a, E = 0xe8ff6a, D = 0xc8f0d8 },
            Seg = Body(S("helmet", 0.95f), S("pill", 1, 1.1f, 'a'), S("pill", 0.85f, 1, 'b'), S("pill", 0.85f, 1.05f, 'a'), S("pill", 0.8f, 1.05f, 'b'), S("sphere", 1, 1, 'a'),
                S("pill", 0.9f, 1.05f, 'a'), S("pill", 0.85f, 1.05f, 'b'), S("boot", 0.95f, 1, 'b', 'a')),
            Att = Mirror(At(0, "fin", 0, 0.9f, 0.2f, 0.15f, 0.9f, 1.1f, 'c')),
        },
        ["lozenge"] = new MedSpec
        {
            Key = "lozenge", Name = "Lozenge", Blurb = "Chunky cough drop", Bulk = 1.2f,
            Col = new SpeciesColors { A = 0xb02a5a, B = 0xffb8d4, C = 0xffe08a, E = 0xffe08a, D = 0xf0d0e0 },
            Seg = Body(S("sphere", 1.15f), S("disc", 1.25f, 1), S("disc", 1.1f, 1, 'b'), S("capsule", 1.1f, 0.95f, 'a'), S("capsule", 1.05f, 0.95f, 'b'), S("sphere", 1.3f, 1, 'a'),
                S("capsule", 1.1f, 0.95f, 'a'), S("capsule", 1.05f, 0.95f, 'b'), S("boot", 1.1f, 1, 'b', 'a')),
            Att = Mirror(At(0, "box", 0, 0.05f, -0.95f, 1.2f, 0.18f, 0.12f, 'e')),
        },
        ["gummy"] = new MedSpec
        {
            Key = "gummy", Name = "Gummy Vitamin", Blurb = "Bouncy and bright", Bulk = 1.1f,
            Col = new SpeciesColors { A = 0xff8a1a, B = 0xffd23a, C = 0xff3ad0, E = 0x2a1a0a, D = 0xfff0b0 },
            Seg = Body(S("sphere", 1.2f), S("sphere", 1.1f, 1.1f), S("sphere", 1, 1, 'b'), S("capsule", 1.05f), S("capsule", 1, 1, 'b'), S("sphere", 1.2f, 1, 'a'),
                S("capsule", 1.05f), S("capsule", 1, 1, 'b'), S("sphere", 1.2f, 1, 'b')),
            Att = Mirror(At(0, "sphere", -0.55f, 0.85f, 0, 0.45f, 0.45f, 0.45f, 'a'), At(0, "sphere", 0.32f, 0.2f, -0.85f, 0.22f, 0.22f, 0.12f, 'e'), At(0, "sphere", -0.32f, 0.2f, -0.85f, 0.22f, 0.22f, 0.12f, 'e')),
        },
        // the player-piloted battle mecha: a white-and-red med-mech
        ["medmech"] = new MedSpec
        {
            Key = "medmech", Name = "Med-Mech", Blurb = "", Bulk = 1.25f,
            Col = new SpeciesColors { A = 0xe9edf2, B = 0xd02a36, C = 0x2aa8ff, E = 0x2aa8ff, D = 0x9aa4b0 },
            Seg = Body(S("helmet", 1.1f), S("bottle", 1.2f, 1, 'a', 'b'), S("box", 1.1f, 1, 'b'), S("capsule", 1.2f), S("cylinder", 1.15f, 1, 'b'), S("box", 1.4f, 1, 'a'),
                S("cylinder", 1.15f, 1, 'a'), S("cylinder", 1.1f, 1, 'b'), S("boot", 1.3f, 1.1f, 'b', 'a')),
            Att = Mirror(At(3, "pauldron", -0.25f, 0.9f, 0, 1.6f, 1.15f, 1.6f, 'b', rz: 18), At(1, "cross", 0, 0.25f, -1.05f, 0.5f, 0.5f, 0.1f, 'c')),
        },
    };
    /// <summary>
    /// The heroic standard every design starts from: broad shoulders and a V-shaped chest, a narrow waist, long legs, a smaller
    /// head, limbs that taper away from the body (shoulders and thighs full, wrists and ankles lean) with gauntlets at the wrist.
    /// </summary>
    static MedDraw()
    {
        foreach (var (k, sp) in PRESETS)
        {
            bool stocky = k is "bottle" or "gummy";
            sp.Bulk = MathF.Max(sp.Bulk, stocky ? 1.4f : 1.32f) + (k == "medmech" ? 0.05f : 0);
            sp.LegLen = stocky ? 1.06f : 1.13f; sp.ArmLen = 1.05f; sp.Height = k == "gummy" ? 0.98f : 1.04f;
            var seg = sp.Seg;
            seg[0].Thick *= 0.88f;                                                    // a smaller head reads as heroic
            seg[1].Taper += 0.15f;                                                     // more V in the chest
            if (!stocky) { seg[2].Thick *= 0.84f; seg[2].Taper += 0.3f; }              // a narrow, wedge-shaped waist
            foreach (int a in new[] { 3, 6 }) { seg[a].Thick *= 1.12f; seg[a].Taper += 0.22f; }   // full shoulders/upper arms
            foreach (int a in new[] { 4, 7 }) seg[a].Taper -= 0.12f;                  // forearms flare into gauntlets at the wrist
            foreach (int a in new[] { 9, 12 }) { seg[a].Thick *= 1.06f; seg[a].Taper += 0.26f; }  // thighs full at the hip
            foreach (int a in new[] { 10, 13 }) seg[a].Taper += 0.18f;                // calves under the knee, lean at the ankle
        }
    }

    /// <summary>The presets offered for the player (the companions' and the mecha's looks aren't in the customizer list twice).</summary>
    public static readonly string[] PLAYER_PRESETS = { "gelcap", "tablet", "bottle", "syringe", "caplet", "lozenge", "gummy" };
    public static readonly string[] SHAPES = { "capsule", "pill", "cylinder", "box", "hex", "sphere", "cone", "disc", "tablet", "vial", "bottle" };
    public static readonly string[] CORE_SHAPES = { "capsule", "pill", "trapezoid", "cylinder", "box", "hex", "sphere", "disc", "tablet", "vial", "bottle" };
    public static readonly string[] ABDOMEN_SHAPES = { "none", "capsule", "pill", "trapezoid", "cylinder", "box", "hex", "sphere", "ribbed" };
    public static readonly string[] HEAD_SHAPES = { "helmet", "tablethead", "cap", "plunger", "sphere", "capsule", "box", "hex", "trapezoid", "cone" };
    public static readonly string[] HAND_SHAPES = { "sphere", "capsule", "box", "cylinder", "claw", "mitt" };
    public static readonly string[] FOOT_SHAPES = { "boot", "capsule", "box", "sphere", "cylinder", "wedge" };
    public static readonly string[] DETAILS = { "none", "score", "rivets", "vents", "stripe", "rings", "ridges" };
    public static readonly string[] VISORS = { "default", "slit", "wrap", "dual", "mono", "none" };
    public static readonly string[] ANTENNAS = { "none", "stub", "spike", "dish", "twin" };
    public static string[] ShapesFor(int i) => WithModels(i == 0 ? HEAD_SHAPES : i is 5 or 8 ? HAND_SHAPES : i is 11 or 14 ? FOOT_SHAPES : i is 1 or 2 ? CORE_SHAPES : i == 15 ? ABDOMEN_SHAPES : SHAPES);
    /// <summary>The list plus the player's own FBX models (as "model:name").</summary>
    public static string[] WithModels(string[] a) => ModelLibrary.Names.Count == 0 ? a : a.Concat(ModelLibrary.Names.Select(n => "model:" + n)).ToArray();
    static bool Model(string shape, out char key) { key = '\0'; return shape != null && shape.StartsWith("model:") && ModelLibrary.Keys.TryGetValue(shape[6..], out key); }
    public static readonly string[] ATTACH_SHAPES = { "sphere", "capsule", "cone", "box", "ring", "disc", "cylinder", "pauldron", "fin", "needle", "cross" };
    public static readonly string[] ARMOR_STYLES = { "plate", "shell", "angular", "ring", "studs", "none" };
    public static readonly string[] PART_NAMES = { "Head", "Chest", "Waist", "Left upper arm", "Left forearm", "Left hand", "Right upper arm", "Right forearm", "Right hand", "Left thigh", "Left shin", "Left foot", "Right thigh", "Right shin", "Right foot", "Abdomen" };
    public static int MirrorPart(int p) => p switch { 3 => 6, 4 => 7, 5 => 8, 6 => 3, 7 => 4, 8 => 5, 9 => 12, 10 => 13, 11 => 14, 12 => 9, 13 => 10, 14 => 11, _ => p };

    // ------------------------------------------------------------------ drawing
    static readonly Q4 PQ = new(), LQ = new(), WQ = new(), TQ = new(), FQ = new();
    static readonly V3 TV = new();
    static float[] ps; static int pk; static Emit em; static char flashTone;
    static MedSpec cur;
    /// <summary>Offset of the current virtual segment inside its bone (the chest and abdomen share the torso bone), in the bone's frame.</summary>
    static float sox, soy, soz;
    /// <summary>Per-segment stretch across (x) and through (z): broad shoulders widen the chest without making it deep.</summary>
    static float skx = 1, skz = 1;

    // the chest takes the top 58% of the torso bone and the abdomen the rest, unless the abdomen is merged into the chest
    const float CHEST_C = 0.42f, CHEST_H = 0.58f, ABD_C = -0.58f, ABD_H = 0.42f;
    public static bool Merged(MedSpec sp) => sp.Seg.Length < 16 || sp.Seg[15] == null || sp.Seg[15].Shape == "none";
    /// <summary>The skeleton bone a segment is drawn from (the abdomen rides on the torso bone).</summary>
    public static int Bone(int i) => i == 15 ? 1 : i;

    static void Part(int i)
    {
        int b = Bone(i); pk = b * 13; PQ.Set(ps[pk + 3], ps[pk + 4], ps[pk + 5], ps[pk + 6]); sox = soy = soz = 0;
        skx = skz = 1;
        if (cur != null && (i == 1 || i == 15)) { skx = cur.Bulk; skz = MathF.Max(0.8f, 1 - (cur.Bulk - 1) * 0.25f); }   // wide and shallow: a heroic chest
        if (cur != null && (i == 1 || i == 15) && !Merged(cur))
        {
            float Lt = ps[13 + 8] * (cur.Seg[1]?.Len ?? 1); soy = (i == 1 ? CHEST_C : ABD_C) * Lt;
            if (i == 15) { var a = cur.Seg[15]; float tr = (ps[13 + 7] + ps[13 + 9]) * 0.5f * (cur.Seg[1]?.Thick ?? 1) * cur.Bulk; sox = a.OX * tr; soy += a.OY * tr; soz = a.OZ * tr; }
        }
    }
    /// <summary>Emits a shape positioned in the current segment's local frame, optionally with a local rotation.</summary>
    static void Put(char sh, char tone, float lx, float ly, float lz, float sx, float sy, float sz, Q4 local = null)
    {
        QRot(PQ, lx * skx + sox, ly + soy, lz * skz + soz, TV);
        var q = local != null ? QMul(PQ, local, WQ) : PQ;
        if (skx != 1 || skz != 1)
        {   // stretch the shape's size along the segment's x and z (for a shape turned about x, its own z is the segment's y)
            bool turnedX = local != null && MathF.Abs(local.x) > 0.6f && MathF.Abs(local.y) < 0.2f && MathF.Abs(local.z) < 0.2f;
            sx *= skx; if (turnedX) sy *= skz; else sz *= skz;
        }
        em(sh, flashTone != 0 ? flashTone : tone, ps[pk] + TV.x, ps[pk + 1] + TV.y, ps[pk + 2] + TV.z, q.x, q.y, q.z, q.w, sx, sy, sz);
    }
    static Q4 Rx90 => QAxis(1, 0, 0, PI / 2, LQ);
    static Q4 Flip => QAxis(1, 0, 0, PI, FQ);

    /// <summary>A frustum (or cylinder) from radius rt at the top to rb at the bottom: picks the nearest of the shared frustum
    /// shapes (ratios 1, 0.86, 0.72, 0.5) and turns it over when the bottom is the wider end.</summary>
    static void Frustum(char tone, float ly, float rt, float rb, float h)
    {
        bool flip = rb > rt; float big = MathF.Max(rt, rb), q = MathF.Min(rt, rb) / MathF.Max(1e-4f, big);
        char sh = q > 0.93f ? 'C' : q > 0.79f ? 'H' : q > 0.61f ? 'F' : 'G';
        if (sh == 'C') { Put('C', tone, 0, ly, 0, big, h, big); return; }
        Put(sh, tone, 0, ly, 0, big, h, big, flip ? Flip : null);
    }
    /// <summary>A block that narrows from wTop to wBot (width), depth d: a trapezoid when they differ.</summary>
    static void Block(char tone, float ly, float wTop, float wBot, float h, float d)
    {
        bool flip = wBot > wTop; float big = MathF.Max(wTop, wBot), q = MathF.Min(wTop, wBot) / MathF.Max(1e-4f, big);
        char sh = q > 0.92f ? 'B' : q > 0.72f ? 'Q' : 'K';
        Put(sh, tone, 0, ly, 0, big, h, d, flip ? Flip : null);
    }

    /// <summary>A capsule whose ends can differ (taper), squashing into an ellipsoid when it's shorter than it is wide.</summary>
    static void Capsule(char tA, char tB, float r, float L, bool two, float taper = 0, float ly = 0)
    {
        float rt = r * (1 + taper * 0.5f), rb = r * (1 - taper * 0.5f), core = MathF.Max(0, L - (rt + rb) * 0.5f);
        if (core <= 0.001f) { Put('Z', tA, 0, ly, 0, (rt + rb) * 0.5f, L, (rt + rb) * 0.5f); if (two) Put('T', 'c', 0, ly, 0, (rt + rb) * 0.51f, L * 0.4f, (rt + rb) * 0.51f); return; }
        float ht = MathF.Min(rt, L), hb = MathF.Min(rb, L);
        if (two)
        {
            float rm = (rt + rb) * 0.5f;
            Frustum(tA, ly + core * 0.5f, rt, rm, core); Frustum(tB, ly - core * 0.5f, rm, rb, core);
            Put('Z', tA, 0, ly + core, 0, rt, ht, rt); Put('Z', tB, 0, ly - core, 0, rb, hb, rb);
            Put('T', 'c', 0, ly, 0, rm * 1.03f, rm * 0.9f, rm * 1.03f);   // the seam where the halves join
        }
        else
        {
            Frustum(tA, ly, rt, rb, core * 2);
            Put('Z', tA, 0, ly + core, 0, rt, ht, rt); Put('Z', tA, 0, ly - core, 0, rb, hb, rb);
        }
    }

    public static void Draw(MedSpec sp, float[] pose, DrawOpts o, Emit emit, bool anchored = false)
    {
        if (!anchored) pose = Anchored(sp, pose);
        ps = pose; em = emit; cur = sp; var has = o.Has; var fl = o.Flash;
        if (o.Curl > 0.6f && o.HasBall)
        {   // curled up: one pill-shaped ball, two-toned, the seam rolling with it
            var q = o.BallQ; float br = o.BallR;
            emit('Z', 'a', o.BallX, o.BallY, o.BallZ, q.x, q.y, q.z, q.w, br, br, br);
            QRot(q, 0, -br * 0.38f, 0, TV);
            emit('Z', sp.Seg[1]?.TwoTone == true ? 'b' : 'a', o.BallX + TV.x, o.BallY + TV.y, o.BallZ + TV.z, q.x, q.y, q.z, q.w, br * 0.93f, br * 0.66f, br * 0.93f);
            emit('T', 'c', o.BallX, o.BallY, o.BallZ, q.x, q.y, q.z, q.w, br * 1.02f, br * 1.1f, br * 1.02f);
            QRot(q, 0, 0, -br * 0.97f, TV); emit('Z', 'e', o.BallX + TV.x, o.BallY + TV.y, o.BallZ + TV.z, q.x, q.y, q.z, q.w, br * 0.18f, br * 0.18f, br * 0.08f);
            return;
        }
        for (int i = 0; i < 16; i++)
        {
            int b = Bone(i); if (!has[b] || (i == 15 && Merged(sp))) continue;
            flashTone = fl != null && fl[b] > 0 ? 'f' : '\0';
            DrawSeg(sp, i, sp.Seg[i] ?? new SegStyle());
        }
        flashTone = '\0';
        foreach (var a in sp.Att) { int b = Bone(a.Part); if (a.Part >= 0 && a.Part < 16 && has[b] && !(a.Part == 15 && Merged(sp))) { flashTone = fl != null && fl[b] > 0 ? 'f' : '\0'; DrawAttach(a, sp); } }
        flashTone = '\0';
    }

    /// <summary>The visible radius and half-length of a segment (also used to place armor and attachments).</summary>
    public static (float r, float L) Dims(float[] pose, int i, SegStyle st, MedSpec sp)
    {
        int k = Bone(i) * 13; float avg = (pose[k + 7] + pose[k + 9]) * 0.5f;
        if (i == 1 || i == 15)
        {
            float Lt = pose[k + 8] * (sp.Seg[1]?.Len ?? 1);
            if (i == 1 && Merged(sp)) return (avg * st.Thick, MathF.Max(Lt, avg * st.Thick));
            if (i == 1) return (avg * st.Thick, Lt * CHEST_H);
            return (avg * st.Thick * 0.85f, Lt * ABD_H * st.Len);
        }
        float r = avg * st.Thick, L = pose[k + 8] * st.Len;
        return (r, MathF.Max(L, r));
    }

    /// <summary>World position and orientation of a segment's centre (the abdomen and the split chest sit inside the torso bone).</summary>
    public static void SegFrame(MedSpec sp, float[] pose, int i, out float x, out float y, out float z, out float qx, out float qy, out float qz, out float qw)
    {
        var ops = ps; var oc = cur; ps = pose; cur = sp; Part(i);
        QRot(PQ, sox, soy, soz, TV); x = pose[pk] + TV.x; y = pose[pk + 1] + TV.y; z = pose[pk + 2] + TV.z; qx = PQ.x; qy = PQ.y; qz = PQ.z; qw = PQ.w;
        ps = ops; cur = oc;
    }

    /// <summary>How far the front surface of a shape is from its axis (details and visors sit on it).</summary>
    static float FrontDepth(string shape, float r, float L) => shape switch
    {
        "tablet" or "tablethead" => r * 0.58f, "box" or "trapezoid" or "mitt" => r * 0.78f, "hex" => r * 1.0f, "sphere" => r * 1.05f, "bottle" or "cap" => r * 1.1f, "disc" => MathF.Max(r, L) * 1.1f, _ => r,
    };

    static void DrawSeg(MedSpec sp, int i, SegStyle st)
    {
        Part(i); var (r, L) = Dims(ps, i, st, sp); char a = st.Tone, b = st.Tone2;
        bool core = i is 1 or 2 or 15;
        // taper: positive = wider at the top. Limb bones run toward the hand/foot along +y, so for them "top" (toward the body) is -y
        float tp = i is >= 3 and <= 14 ? -st.Taper : st.Taper;
        float te = Math.Clamp(tp + (i == 1 && st.Shape is "capsule" or "pill" ? 0.35f : 0), -0.8f, 0.8f);   // the chest has a built-in heroic V
        float rt = r * (1 + te * 0.5f), rb = r * (1 - te * 0.5f);
        bool ownVisor = st.Visor == "default";
        switch (st.Shape)
        {
            case "capsule": Capsule(a, b, r, L * (i == 1 ? 1.05f : 1), st.TwoTone, te); break;
            case "pill": Capsule(a, b, r * 0.94f, L * 1.02f, false, te); break;
            case "sphere": Put('Z', a, 0, 0, 0, r * 1.05f, core ? L * 1.05f : MathF.Max(r, L * 0.8f), r * 1.05f); break;
            case "cylinder":
                Frustum(a, 0, rt, rb, L * 2);
                Put('T', b, 0, L * 0.92f, 0, rt * 1.03f, r * 0.5f, rt * 1.03f); Put('T', b, 0, -L * 0.92f, 0, rb * 1.03f, r * 0.5f, rb * 1.03f); break;
            case "box": Block(a, 0, r * 1.8f * (1 + te * 0.5f), r * 1.8f * (1 - te * 0.5f), L * 2, r * 1.6f); break;
            case "trapezoid":
                {   // a block wide at the top and narrow at the bottom (the taper control pushes it further), with a trim along the top edge
                    float wt = r * 2.1f * (1 + MathF.Max(0, te) * 0.3f), wb = r * 1.3f * (1 - MathF.Min(0, te) * 0.6f);
                    Block(a, 0, wt, wb, L * 2, r * 1.5f);
                    Put('B', b, 0, L * 0.88f, -r * 0.76f, wt * 0.92f, L * 0.16f, r * 0.06f);
                    break;
                }
            case "hex": Put(MathF.Abs(te) > 0.2f ? 'W' : 'X', a, 0, 0, 0, r * 2.05f, L * 2, r * 2.05f, te < -0.2f ? Flip : null); Put('X', b, 0, L * 0.96f, 0, r * 1.7f, r * 0.12f, r * 1.7f); break;
            case "cone": Put('P', a, 0, 0, 0, r * 1.2f, L * 2, r * 1.2f, te < 0 ? Flip : null); break;
            case "disc": Put('C', a, 0, 0, 0, MathF.Max(r, L) * 1.1f, r * 0.9f, MathF.Max(r, L) * 1.1f); Put('T', b, 0, 0, 0, MathF.Max(r, L) * 1.12f, r * 0.6f, MathF.Max(r, L) * 1.12f); break;
            case "ribbed":
                {   // an armoured gut: a core with three bands stacked down it
                    Frustum(a, 0, rt * 0.94f, rb * 0.94f, L * 2);
                    for (int m = -1; m <= 1; m++) { float rr = r * (1 + te * 0.5f * m * 0.62f) * 0.9f; Put('T', b, 0, m * L * 0.62f, 0, rr, r * 0.5f, rr); }
                    break;
                }
            case "tablet":
                {   // a round tablet facing forward, with the score line across it
                    float R = MathF.Max(r * 1.15f, L * 1.02f), th = r * 1.1f;
                    Put('C', a, 0, 0, 0, R, th, R, Rx90);
                    Put('T', b, 0, 0, 0, R * 1.01f, th * 0.55f, R * 1.01f, Rx90);
                    Put('B', 'k', 0, 0, -th * 0.5f, R * 0.08f, R * 1.6f, 0.02f);
                    Put('B', b, 0, R * 0.55f, -th * 0.48f, R * 0.5f, R * 0.08f, 0.03f);
                    break;
                }
            case "vial":
                {   // a syringe barrel: clear casing, a window of glowing serum, measuring marks
                    Put('C', a, 0, 0, 0, r, L * 2, r);
                    Put('B', b, 0, 0, -r * 0.82f, r * 0.9f, L * 1.5f, r * 0.4f);
                    Put('B', 'c', 0, -L * 0.15f, -r * 0.9f, r * 0.75f, L * 1.0f, r * 0.35f);
                    for (int m = 0; m < 5; m++) Put('B', 'k', r * 0.2f, -L * 0.6f + m * L * 0.3f, -r * 1.02f, r * 0.45f, 0.012f + r * 0.03f, 0.02f);
                    Put('T', b, 0, L, 0, r * 1.15f, r * 0.6f, r * 1.15f); Put('C', b, 0, -L * 1.02f, 0, r * 0.6f, r * 0.3f, r * 0.6f);
                    break;
                }
            case "bottle":
                {   // a pill bottle: a wide cylinder with a paper label and a red cross on it
                    Put('C', a, 0, 0, 0, r * 1.05f, L * 2, r * 1.05f);
                    Put('C', b, 0, -L * 0.05f, 0, r * 1.08f, L * 1.1f, r * 1.08f);
                    Put('B', 'c', 0, -L * 0.05f, -r * 1.08f, r * 0.42f, r * 0.13f, 0.03f); Put('B', 'c', 0, -L * 0.05f, -r * 1.08f, r * 0.13f, r * 0.42f, 0.03f);
                    Put('T', b, 0, L * 0.95f, 0, r * 0.95f, r * 0.5f, r * 0.95f);
                    break;
                }
            case "helmet":
                Capsule(a, b, r, L * 1.08f, true);
                if (ownVisor) Put('Z', 'e', 0, L * 0.12f, -r * 0.52f, r * 0.88f, r * 0.34f, r * 0.55f);
                break;
            case "tablethead":
                {
                    float R = MathF.Max(r * 1.15f, L), th = r * 1.15f;
                    Put('C', a, 0, 0, 0, R, th, R, Rx90); Put('T', b, 0, 0, 0, R * 1.01f, th * 0.5f, R * 1.01f, Rx90);
                    if (ownVisor) Put('B', 'e', 0, R * 0.08f, -th * 0.52f, R * 1.25f, R * 0.18f, 0.03f);
                    break;
                }
            case "cap":
                {   // a child-proof cap: ridged rim, two eye lights
                    Put('C', a, 0, 0, 0, r * 1.08f, L * 1.7f, r * 1.08f);
                    for (int m = 0; m < 14; m++)
                    {
                        float ang = m / 14f * TAU; QAxis(0, 1, 0, -ang, TQ);
                        Put('B', b, MathF.Sin(ang) * r * 1.1f, 0, -MathF.Cos(ang) * r * 1.1f, r * 0.12f, L * 1.55f, r * 0.08f, TQ);
                    }
                    if (ownVisor) { Put('Z', 'e', -r * 0.38f, L * 0.1f, -r * 1.08f, r * 0.2f, r * 0.2f, r * 0.08f); Put('Z', 'e', r * 0.38f, L * 0.1f, -r * 1.08f, r * 0.2f, r * 0.2f, r * 0.08f); }
                    break;
                }
            case "plunger":
                Put('C', b, 0, -L * 0.2f, 0, r * 0.45f, L * 1.2f, r * 0.45f);
                Put('C', a, 0, L * 0.45f, 0, r * 1.3f, r * 0.35f, r * 1.3f);
                Put('Z', a, 0, -L * 0.05f, 0, r * 0.95f, r * 0.8f, r * 0.95f);
                if (ownVisor) Put('B', 'e', 0, -L * 0.02f, -r * 0.82f, r * 1.2f, r * 0.2f, r * 0.12f);
                break;
            case "boot":
                Put('Z', a, 0, 0, -r * 0.35f, r * 1.05f, MathF.Max(r * 0.6f, L * 0.75f), r * 1.7f);
                Put('B', b, 0, -L * 0.55f, -r * 0.35f, r * 1.85f, r * 0.3f, r * 3.0f);
                break;
            case "wedge":
                {   // a wedge sole: deep at the heel, thin at the toe
                    QAxis(1, 0, 0, PI / 2, TQ);
                    Put('K', a, 0, -L * 0.05f, -r * 0.3f, r * 1.9f, r * 3.2f, L * 1.3f, TQ);
                    Put('B', b, 0, -L * 0.62f, -r * 0.3f, r * 1.95f, r * 0.22f, r * 3.25f);
                    break;
                }
            case "claw":
                {   // a palm with three talons
                    Put('Z', a, 0, 0, 0, r * 0.85f, r * 0.75f, r * 0.85f);
                    for (int m = -1; m <= 1; m++) { QAxis(0, 0, 1, m * 0.3f, TQ); Put('P', b, m * r * 0.4f, -r * 0.85f, -r * 0.2f, r * 0.2f, r * 0.9f, r * 0.2f, QMul(TQ, Flip, TQ)); }
                    break;
                }
            case "mitt": Put('Z', a, 0, 0, 0, r * 1.15f, r * 0.85f, r * 1.3f); Put('Z', b, -r * 0.75f, r * 0.1f, -r * 0.2f, r * 0.35f, r * 0.5f, r * 0.35f); break;
            default:
                if (Model(st.Shape, out char mk)) Put(mk, a, 0, 0, 0, r * 1.05f, L, r * 1.05f);   // the player's own model, fitted to the segment
                else Capsule(a, b, r, L, false, te);
                break;
        }
        float fd = FrontDepth(st.Shape, r, L);
        switch (st.Detail)
        {
            case "score": Put('B', 'k', 0, 0, -fd * 0.99f, r * 1.3f, 0.02f + r * 0.035f, 0.02f); break;
            case "rivets": foreach (float ux in new[] { -0.45f, 0.45f }) foreach (float vy in new[] { -0.5f, 0.5f }) Put('Z', 'd', ux * r, vy * L, -fd * 0.96f, r * 0.09f, r * 0.09f, r * 0.06f); break;
            case "vents": for (int m = -1; m <= 1; m++) Put('B', 'k', 0, m * L * 0.28f, -fd * 0.99f, r * 0.6f, 0.015f + r * 0.05f, 0.025f); break;
            case "stripe": Put('B', 'c', 0, 0, -fd * 0.99f, r * 0.16f, L * 1.6f, 0.03f); break;
            case "rings": foreach (float vy in new[] { -0.55f, 0.55f }) { float rr = r * (1 + te * 0.5f * vy); Put('T', 'c', 0, vy * L, 0, rr * 1.04f, r * 0.35f, rr * 1.04f); } break;
            case "ridges": for (int m = -1; m <= 1; m++) { float rr = r * (1 + te * 0.5f * m * 0.6f); Put('T', b, 0, m * L * 0.6f, 0, rr * 1.03f, r * 0.55f, rr * 1.03f); } break;
        }
        if (i == 0)
        {
            float top = st.Shape is "tablethead" ? MathF.Max(r * 1.15f, L) : st.Shape is "cap" ? L * 0.85f : L;
            switch (st.Visor)
            {
                case "slit": Put('B', 'e', 0, L * 0.1f, -fd * 0.98f, r * 1.3f, r * 0.16f, 0.04f); break;
                case "wrap": Put('Z', 'e', 0, L * 0.1f, -fd * 0.52f, r * 0.9f, r * 0.32f, r * 0.55f); break;
                case "dual": foreach (float ux in new[] { -0.36f, 0.36f }) Put('Z', 'e', ux * r, L * 0.12f, -fd * 0.95f, r * 0.2f, r * 0.2f, r * 0.08f); break;
                case "mono": Put('Z', 'e', 0, L * 0.1f, -fd * 0.96f, r * 0.34f, r * 0.34f, r * 0.14f); Put('T', 'd', 0, L * 0.1f, -fd * 0.97f, r * 0.42f, r * 0.42f, r * 0.42f, Rx90); break;
            }
            switch (st.Antenna)
            {
                case "stub": Put('C', 'd', 0, top + r * 0.15f, 0, r * 0.08f, r * 0.35f, r * 0.08f); Put('Z', 'c', 0, top + r * 0.36f, 0, r * 0.11f, r * 0.11f, r * 0.11f); break;
                case "spike": Put('P', 'd', 0, top + r * 0.3f, 0, r * 0.13f, r * 0.75f, r * 0.13f); break;
                case "dish": Put('C', 'd', r * 0.3f, top + r * 0.15f, 0, r * 0.05f, r * 0.4f, r * 0.05f); QAxis(0, 0, 1, -0.5f, TQ); Put('C', 'd', r * 0.38f, top + r * 0.38f, 0, r * 0.32f, r * 0.05f, r * 0.32f, TQ); Put('Z', 'c', r * 0.38f, top + r * 0.42f, 0, r * 0.07f, r * 0.07f, r * 0.07f); break;
                case "twin":
                    foreach (float sd in new[] { -1f, 1f }) { QAxis(0, 0, 1, -sd * 0.35f, TQ); Put('C', 'd', sd * r * 0.5f, top + r * 0.1f, 0, r * 0.06f, r * 0.5f, r * 0.06f, TQ); Put('Z', 'c', sd * r * 0.62f, top + r * 0.36f, 0, r * 0.1f, r * 0.1f, r * 0.1f); }
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ proportions, anchors and anti-clipping
    /// <summary>The chain each anchor carries with it: the chest takes the head and both arms along.</summary>
    public static readonly int[][] CHAINS = { new[] { 1, 0, 3, 4, 5, 6, 7, 8 }, new[] { 0 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 }, new[] { 2 }, new[] { 9, 10, 11 }, new[] { 12, 13, 14 } };
    /// <summary>The anchor segment that moves part i: chest, abdomen, waist, head, an arm root or a leg root.</summary>
    public static int RootOf(int i) => i switch { 0 => 0, 1 => 1, 2 => 2, 15 => 15, 3 or 4 or 5 => 3, 6 or 7 or 8 => 6, 9 or 10 or 11 => 9, 12 or 13 or 14 => 12, _ => -1 };
    static float[] anch = new float[15 * 13];
    static readonly V3 PJ = new(), PV = new();
    // limb bones (arms and legs) are turned over: their local +y runs toward the hand or foot, so the joint nearer the body is at -y
    static void Prox(float[] p, int i, V3 o) { int k = i * 13; QRot(Q4Of(p, k, TQ), 0, i >= 3 ? -p[k + 8] : p[k + 8], 0, o); o.x += p[k]; o.y += p[k + 1]; o.z += p[k + 2]; }
    static Q4 Q4Of(float[] p, int k, Q4 q) { q.Set(p[k + 3], p[k + 4], p[k + 5], p[k + 6]); return q; }
    static void ScaleChain(float[] p, V3 pivot, float s, params int[] parts)
    {
        foreach (int i in parts) { int k = i * 13; p[k] = pivot.x + (p[k] - pivot.x) * s; p[k + 1] = pivot.y + (p[k + 1] - pivot.y) * s; p[k + 2] = pivot.z + (p[k + 2] - pivot.z) * s; if (i is not (5 or 8 or 11 or 14)) p[k + 8] *= s; }
    }

    /// <summary>
    /// The drawn pose: the skeleton's pose with the hero's proportions (leg and arm length, height), anchors moved, and the
    /// limbs pushed out of the chest and apart from each other so the body never clips through itself. Shared scratch.
    /// </summary>
    public static float[] Anchored(MedSpec sp, float[] pose)
    {
        if (pose.Length < 15 * 13) return pose;
        if (anch.Length < pose.Length) anch = new float[pose.Length];
        var p = anch; Array.Copy(pose, p, pose.Length);
        float footMin(float[] q) => MathF.Min(q[11 * 13 + 1] - q[11 * 13 + 8], q[14 * 13 + 1] - q[14 * 13 + 8]);
        float ground = footMin(pose);
        // legs: longer legs lift the whole body so the feet stay planted
        if (MathF.Abs(sp.LegLen - 1) > 1e-3f)
        {
            float hipY = 0;
            foreach (var (t, sh, ft) in new[] { (9, 10, 11), (12, 13, 14) }) { Prox(pose, t, PJ); hipY += PJ.y * 0.5f; ScaleChain(p, PJ, sp.LegLen, t, sh, ft); }
            float lift = ground - footMin(p);
            for (int i = 0; i < 15; i++) p[i * 13 + 1] += lift;
        }
        if (MathF.Abs(sp.ArmLen - 1) > 1e-3f)
            foreach (var (u, f, h) in new[] { (3, 4, 5), (6, 7, 8) }) { Prox(p, u, PJ); ScaleChain(p, PJ, sp.ArmLen, u, f, h); }
        // anchors
        float tr = (pose[13 + 7] + pose[13 + 9]) * 0.5f * (sp.Seg[1]?.Thick ?? 1) * sp.Bulk;
        foreach (var c in CHAINS)
        {
            var st = sp.Seg[c[0]]; if (st == null || (st.OX == 0 && st.OY == 0 && st.OZ == 0)) continue;
            int bk = (c[0] is >= 9 or 2 ? 2 : 1) * 13;
            Q4Of(p, bk, PQ); QRot(PQ, st.OX * tr, st.OY * tr, st.OZ * tr, TV);
            foreach (int i in c) { int k = i * 13; p[k] += TV.x; p[k + 1] += TV.y; p[k + 2] += TV.z; }
        }
        // anti-clipping: arms out of the chest, thighs and shins apart
        {
            int tk = 13; Q4Of(p, tk, PQ); float qx = PQ.x, qy = PQ.y, qz = PQ.z, qw = PQ.w;
            var inv = new Q4(); inv.Set(-qx, -qy, -qz, qw);
            float cr = tr * (sp.Seg[1]?.Shape is "box" or "trapezoid" ? 1.05f : 0.95f), cl = p[tk + 8] * (sp.Seg[1]?.Len ?? 1);
            for (int i = 3; i <= 8; i++)
            {
                int k = i * 13; float pr = (p[k + 7] + p[k + 9]) * 0.5f * (sp.Seg[i]?.Thick ?? 1);
                QRot(inv, p[k] - p[tk], p[k + 1] - p[tk + 1], p[k + 2] - p[tk + 2], PV);
                if (MathF.Abs(PV.y) > cl + pr) continue;
                float rad = MathF.Sqrt(PV.x * PV.x + PV.z * PV.z), lim = cr + pr * 0.85f;
                if (rad >= lim) continue;
                float side = i <= 5 ? -1 : 1;   // left arm parts go to -x, right to +x when there's no direction to push along
                float dx = rad > 1e-3f ? PV.x / rad : side, dz = rad > 1e-3f ? PV.z / rad : 0;
                if (dx * side < -0.2f) { dx = side * 0.6f; dz = MathF.Sign(PV.z == 0 ? -1 : PV.z) * 0.8f; }   // never push an arm through to the far side
                PV.x = dx * lim; PV.z = dz * lim;
                QRot(PQ, PV.x, PV.y, PV.z, TV); p[k] = p[tk] + TV.x; p[k + 1] = p[tk + 1] + TV.y; p[k + 2] = p[tk + 2] + TV.z;
            }
            foreach (var (l, r) in new[] { (9, 12), (10, 13) })
            {
                int a = l * 13, b = r * 13; float need = ((p[a + 7] + p[a + 9]) * 0.5f * (sp.Seg[l]?.Thick ?? 1) + (p[b + 7] + p[b + 9]) * 0.5f * (sp.Seg[r]?.Thick ?? 1)) * 0.92f;
                float dx = p[b] - p[a], dz = p[b + 2] - p[a + 2], d = MathF.Sqrt(dx * dx + dz * dz);
                if (d >= need || d < 1e-4f) continue;
                float push = (need - d) * 0.5f / d; p[a] -= dx * push; p[a + 2] -= dz * push; p[b] += dx * push; p[b + 2] += dz * push;
            }
        }
        // height: everything scales about the feet
        if (MathF.Abs(sp.Height - 1) > 1e-3f)
        {
            float g = footMin(p), h = sp.Height, cx = p[2 * 13], cz = p[2 * 13 + 2];
            for (int i = 0; i < 15; i++) { int k = i * 13; p[k] = cx + (p[k] - cx) * h; p[k + 1] = g + (p[k + 1] - g) * h; p[k + 2] = cz + (p[k + 2] - cz) * h; p[k + 7] *= h; p[k + 8] *= h; p[k + 9] *= h; }
        }
        return p;
    }

    static readonly Q4 AQ = new(), XQ = new(), YQ = new(), YQ2 = new(), ZQ = new();
    static readonly V3 TV2 = new();
    static void DrawAttach(Attach at, MedSpec sp)
    {
        Part(at.Part); var st = sp.Seg[at.Part] ?? new SegStyle(); var (r, L) = Dims(ps, at.Part, st, sp);
        float lx = at.X * r, ly = at.Y * L, lz = at.Z * r, sx = at.Sx * r, sy = at.Sy * r, sz = at.Sz * r;
        QEuler(at.Rx * PI / 180, at.Ry * PI / 180, at.Rz * PI / 180, AQ);
        switch (at.Shape)
        {
            case "sphere": Put('Z', at.Tone, lx, ly, lz, sx, sy, sz, AQ); break;
            case "capsule": Put('C', at.Tone, lx, ly, lz, sx * 0.5f, sy, sz * 0.5f, AQ); break;
            case "cone": Put('P', at.Tone, lx, ly, lz, sx, sy, sz, AQ); break;
            case "box": Put('B', at.Tone, lx, ly, lz, sx, sy, sz, AQ); break;
            case "ring": Put('T', at.Tone, lx, ly, lz, sx, sy, sz, AQ); break;
            case "disc": case "cylinder": Put('C', at.Tone, lx, ly, lz, sx, sy, sz, AQ); break;
            case "pauldron": Put('Z', at.Tone, lx, ly, lz, sx, sy, sz, AQ); Put('T', 'c', lx, ly - sy * 0.35f, lz, sx * 0.97f, MathF.Max(sy * 0.25f, sx * 0.8f), sz * 0.97f, AQ); break;   // a round trim band, never a paper-thin disc
            case "fin": Put('B', at.Tone, lx, ly, lz, sx, sy, sz, AQ); break;
            case "needle": Put('C', at.Tone, lx, ly, lz, sx * 0.5f, sy * 0.55f, sz * 0.5f, AQ); QRot(AQ, 0, -sy * 0.75f, 0, TV); Put('P', 'k', lx + TV.x, ly + TV.y, lz + TV.z, sx * 0.18f, sy * 0.9f, sz * 0.18f, QMul(AQ, QAxis(1, 0, 0, PI, TQ), TQ)); break;
            case "cross": Put('B', at.Tone, lx, ly, lz, sx, sy * 0.33f, sz, AQ); Put('B', at.Tone, lx, ly, lz, sx * 0.33f, sy, sz, AQ); break;
            default:
                if (Model(at.Shape, out char mk)) Put(mk, at.Tone, lx, ly, lz, sx, sy, sz, AQ);
                else Put('Z', at.Tone, lx, ly, lz, sx, sy, sz, AQ);
                break;
        }
    }

    /// <summary>
    /// One armour piece. Plates are real curved shells that wrap the segment at its own radius (following its taper), with
    /// domed caps on the helmet, shoulders and knees. "plate": smooth shells with a raised centre plate and a glowing edge;
    /// "shell": overlapping chitin bands like an armadillo's; "angular": faceted plates with spikes. Flat-fronted segments
    /// (box, trapezoid, tablet, hex) get flat plates. "ring", "studs" and "none" stay minimal.
    /// </summary>
    public static void Armor(MedSpec sp, float[] pose, int part, float y0, float y1, float phi, string style, char tone, Emit emit, bool anchored = false)
    {
        ps = anchored ? pose : Anchored(sp, pose); em = emit; cur = sp; flashTone = '\0'; Part(part);
        var st = sp.Seg[part] ?? new SegStyle(); var (r, L) = Dims(ps, part, st, sp);
        // limb bones are turned over (local +y runs toward the hand/foot, local x is mirrored): flip the band and the side to match
        bool limb = part >= 3 && part != 15;
        if (limb) { (y0, y1) = (-y1, -y0); phi = -phi; }
        float a = phi * PI / 180; QAxis(0, 1, 0, -a, AQ);
        char d = tone, g = tone == 'f' ? 'f' : 'c', k = tone == 'f' ? 'f' : 'k';
        float nx = MathF.Sin(a), nz = -MathF.Cos(a);
        switch (style)
        {
            case "none": return;
            case "ring": Put('T', d, 0, (y0 + y1) * 0.5f * L + 0.15f * L, 0, r * 1.12f, r * 0.7f, r * 1.12f); Put('T', d, 0, (y0 + y1) * 0.5f * L - 0.15f * L, 0, r * 1.12f, r * 0.7f, r * 1.12f); return;
            case "studs": for (int m = -1; m <= 1; m++) Put('Z', d, nx * r, (y0 + y1) * 0.5f * L + m * 0.25f * L, nz * r, r * 0.28f, r * 0.28f, r * 0.2f, AQ); return;
        }
        bool chitin = style == "shell", sharp = style == "angular";
        char plate = sharp ? 'J' : 'U';
        float te = Math.Clamp((limb ? -st.Taper : st.Taper) + (part == 1 && st.Shape is "capsule" or "pill" ? 0.35f : 0), -0.8f, 0.8f);
        float RadAt(float y) => r * (1 + te * 0.5f * Math.Clamp(y / MathF.Max(1e-4f, L), -1, 1));
        bool flat = st.Shape is "box" or "trapezoid" or "tablet" or "tablethead" or "hex" or "disc" or "cap";
        float ya = y0 * L, yb = y1 * L, H = yb - ya;

        // a curved plate wrapping the segment from y=lo to y=hi, facing the way of q (a cone-shaped one when the segment tapers)
        void Wrap(char sh, char tn, float lo, float hi, float grow, Q4 q)
        {
            float rt = RadAt(hi), rb = RadAt(lo), big = MathF.Max(rt, rb) * grow, ym = (lo + hi) * 0.5f;
            if (sh != 'V' && sh != 'O' && MathF.Abs(rt - rb) > 0.12f * MathF.Max(rt, rb))
            {   // the conical plate is wide at its top; turn it over (about the facing axis) when the segment is wider below
                if (rb > rt) { QAxis(0, 0, 1, PI, TQ); Put('A', tn, 0, ym, 0, big, hi - lo, big, QMul(q, TQ, YQ2)); }
                else Put('A', tn, 0, ym, 0, big, hi - lo, big, q);
                return;
            }
            Put(sh, tn, 0, ym, 0, big, hi - lo, big, q);
        }
        // the straight part of the segment, where a curved plate can sit flush (rounded ends get domed scutes instead)
        float core = st.Shape is "cylinder" or "vial" or "bottle" or "hex" ? L * 0.95f : st.Shape is "sphere" or "disc" or "tablet" ? 0 : MathF.Max(0, L - r * 0.85f);
        // a domed plate facing out of the segment (the cap shell turned to face forward), shaped to the segment's curve
        void Scute(char tn, float ym, float halfH, float halfW, float grow, Q4 q)
        {
            QAxis(1, 0, 0, -PI / 2, TQ); QMul(q, TQ, YQ);
            float depth = MathF.Max(RadAt(ym - halfH * 0.8f), RadAt(ym + halfH * 0.8f)) * grow;   // clear of the widest point it covers
            Put('S', tn, 0, ym, 0, halfW * grow, depth, halfH * grow, YQ);
        }
        void Plate(char sh, char tn, float lo, float hi, float grow, Q4 q)
        {
            float l2 = MathF.Max(lo, -core), h2 = MathF.Min(hi, core);
            if (h2 - l2 >= MathF.Max(r * 0.55f, (hi - lo) * 0.4f)) Wrap(sh, tn, l2, h2, grow, q);
            else Scute(tn, (lo + hi) * 0.5f, MathF.Max((hi - lo) * 0.5f, r * 0.5f), RadAt((lo + hi) * 0.5f) * 0.95f, grow, q);
        }
        void Spike(float u, float v, float w, float len)
        {   // a cone pointing straight out of the plate's face
            QAxis(1, 0, 0, -PI / 2, TQ); QMul(AQ, TQ, YQ);
            QRot(AQ, u, v, -(w + len * 0.45f), TV2); Put('P', d, TV2.x, TV2.y, TV2.z, len * 0.26f, len, len * 0.26f, YQ);
        }
        void FlatPlate(float lo, float hi, float wd)
        {   // for flat-fronted segments: a slab with a bevelled rim and a glow bar
            float fd = FrontDepth(st.Shape, r, L), t = r * 0.12f, ym = (lo + hi) * 0.5f;
            QRot(AQ, 0, ym, -(fd + t * 0.5f), TV2); Put('B', d, TV2.x, TV2.y, TV2.z, wd, hi - lo, t, AQ);
            QRot(AQ, 0, ym, -(fd + t * 1.1f), TV2); Put('B', d, TV2.x, TV2.y, TV2.z, wd * 0.62f, (hi - lo) * 0.8f, t * 0.6f, AQ);
            QRot(AQ, 0, lo + (hi - lo) * 0.12f, -(fd + t * 1.05f), TV2); Put('B', g, TV2.x, TV2.y, TV2.z, wd * 0.8f, r * 0.06f, t * 0.4f, AQ);
        }

        if (part == 1 || part == 15)
        {   // chest (front) or back plate
            bool front = MathF.Abs(phi) < 90;
            if (flat) { FlatPlate(ya, yb, r * 1.5f); if (sharp && front) { Spike(-r * 0.5f, yb - H * 0.15f, r, r * 0.3f); Spike(r * 0.5f, yb - H * 0.15f, r, r * 0.3f); } return; }
            float ym = (ya + yb) * 0.5f, hh = MathF.Min(MathF.Max(H * 0.55f, r * 0.55f), r * 0.85f), hw = MathF.Min(RadAt(ym) * 0.9f, r * 1.05f);
            if (chitin)
                for (int m = 0; m < 3; m++) Scute(d, ym + hh * 0.45f - m * hh * 0.45f, hh * 0.55f, hw * (1 - m * 0.06f), 1.08f + m * 0.025f, AQ);   // overlapping bands, each a little proud of the one above
            else
            {
                Scute(d, ym, hh, hw, 1.05f, AQ);                                                      // the breastplate, following the chest's curve
                Scute(d, ym + hh * 0.05f, hh * 0.85f, hw * 0.34f, 1.11f, AQ);                          // a raised keel down the middle
                if (front)
                {   // a glowing core set into the keel: a lit disc in a dark ring
                    QAxis(1, 0, 0, PI / 2, TQ); QMul(AQ, TQ, ZQ);
                    QRot(AQ, 0, ym + hh * 0.3f, -RadAt(ym) * 1.13f, TV2); Put('C', k, TV2.x, TV2.y, TV2.z, r * 0.2f, r * 0.08f, r * 0.2f, ZQ);
                    QRot(AQ, 0, ym + hh * 0.3f, -RadAt(ym) * 1.17f, TV2); Put('C', g, TV2.x, TV2.y, TV2.z, r * 0.13f, r * 0.05f, r * 0.13f, ZQ);
                }
                else for (int m = -1; m <= 1; m += 2) { QRot(AQ, m * hw * 0.55f, ym, -RadAt(ym) * 0.98f, TV2); Put('B', k, TV2.x, TV2.y, TV2.z, r * 0.07f, hh * 0.9f, r * 0.1f, AQ); }   // vents on the back
                if (sharp && front) { Spike(-hw * 0.62f, ym + hh * 0.45f, RadAt(ym) * 0.9f, r * 0.32f); Spike(hw * 0.62f, ym + hh * 0.45f, RadAt(ym) * 0.9f, r * 0.32f); }
                if (sharp && !front) for (int m = 0; m < 3; m++) Spike(0, ym - hh * 0.5f + m * hh * 0.5f, RadAt(ym) * 1.1f, r * 0.26f);
            }
        }
        else if (part == 2)
        {   // belt: a band all the way round (open at the back), a lit buckle
            Wrap('O', d, ya + H * 0.3f, yb - H * 0.25f, 1.07f, AQ);
            float fd = flat ? FrontDepth(st.Shape, r, L) : RadAt(0) * 1.07f;
            QRot(AQ, 0, (ya + yb) * 0.5f, -(fd + r * 0.05f), TV2); Put('B', d, TV2.x, TV2.y, TV2.z, r * 0.45f, H * 0.42f, r * 0.12f, AQ);
            QRot(AQ, 0, (ya + yb) * 0.5f, -(fd + r * 0.11f), TV2); Put('B', g, TV2.x, TV2.y, TV2.z, r * 0.25f, H * 0.18f, r * 0.05f, AQ);
        }
        else if (part == 0)
        {   // helmet: a domed cap over the top, a brow plate, a glowing rim
            float top = st.Shape is "tablethead" ? MathF.Max(r * 1.15f, L) : st.Shape is "helmet" ? L * 1.08f : st.Shape is "sphere" ? MathF.Max(r * 1.05f, L * 0.8f) : L;
            float capR = r * 1.2f, cy = top - capR + r * 0.08f;
            QAxis(0, 1, 0, 0, TQ);
            Put('S', d, 0, cy, 0, capR, capR * 1.02f, capR);
            Put('T', g, 0, cy + capR * 0.48f, 0, capR * 0.885f, capR * 0.3f, capR * 0.885f);
            if (chitin) Put('S', d, 0, cy + capR * 0.12f, capR * 0.12f, capR * 0.82f, capR * 0.9f, capR * 0.82f);
            QAxis(0, 1, 0, 0, YQ2); Wrap('V', d, cy + capR * 0.38f, cy + capR * 0.6f, 1.14f, YQ2);   // the brow
            if (sharp) Put('P', d, 0, top + r * 0.15f, r * 0.1f, r * 0.14f, r * 0.55f, r * 0.14f);
        }
        else if (part is 3 or 6)
        {   // shoulders: a domed pauldron over the joint, tipped outward, a plate under it, a glowing rim
            // in the limb's frame, outward is local +x for the left arm and -x for the right, and up is local -y
            float s2 = part == 3 ? 1 : -1, capR = r * 1.58f, cyS = -L * 0.92f;   // centred on the shoulder joint, so the dome wraps over the top of it (and over any pauldron there)
            QAxis(0, 0, 1, PI + s2 * 0.32f, ZQ);                                  // the dome faces up and a little outward
            Put('S', d, s2 * r * 0.15f, cyS, 0, capR, capR, capR, ZQ);
            QRot(ZQ, 0, capR * 0.45f, 0, TV2); Put('T', g, s2 * r * 0.15f + TV2.x, cyS + TV2.y, TV2.z, capR * 0.885f, capR * 0.3f, capR * 0.885f, ZQ);
            if (chitin) { QRot(ZQ, 0, -capR * 0.3f, 0, TV2); Put('S', d, s2 * r * 0.15f + TV2.x, cyS + TV2.y, TV2.z, capR * 0.94f, capR * 0.94f, capR * 0.94f, ZQ); }
            else Plate(plate, d, ya + H * 0.45f, yb, 1.1f, AQ);
            if (sharp) { QAxis(0, 0, 1, PI + s2 * 1.0f, TQ); QRot(TQ, 0, capR * 0.85f, 0, TV2); Put('P', d, s2 * r * 0.15f + TV2.x, cyS + TV2.y, TV2.z, r * 0.17f, r * 0.7f, r * 0.17f, TQ); }
        }
        else if (part is 4 or 7 or 9 or 12)
        {   // bracers and thigh plates: a curved plate on the outside / front, a glowing band at the lower edge
            float lo0 = MathF.Max(ya, -core), hi0 = MathF.Min(yb, core), H0 = MathF.Max(0.01f, hi0 - lo0);
            if (chitin) for (int m = 0; m < 3; m++) { float hi = hi0 - H0 * m / 3.2f, lo = hi - H0 / 2.6f; Plate('U', d, lo, hi, 1.07f + m * 0.025f, AQ); }
            else { Plate(plate, d, lo0, hi0, 1.08f, AQ); Plate('V', d, lo0 + H0 * 0.2f, hi0 - H0 * 0.1f, 1.13f, AQ); }
            if (H0 > r * 0.55f) Wrap('U', g, hi0 - H0 * 0.09f, hi0, 1.115f, AQ);   // at the wrist / knee end
            if (sharp) Spike(0, (ya + yb) * 0.5f, RadAt((ya + yb) * 0.5f) * 1.12f, r * 0.34f);
        }
        else if (part is 10 or 13)
        {   // greaves: a curved shin plate, a domed knee guard facing forward, a glowing strip
            float lo1 = MathF.Max(ya * 0.8f, -core), hi1 = MathF.Min(yb, core), H1 = MathF.Max(0.01f, hi1 - lo1);   // leave the knee (at -y) for the knee guard
            if (chitin) for (int m = 0; m < 3; m++) { float hi = hi1 - H1 * m / 3.4f, lo = hi - H1 / 2.8f; Plate('U', d, lo, hi, 1.07f + m * 0.025f, AQ); }
            else Plate(plate, d, lo1, hi1, 1.08f, AQ);
            QAxis(1, 0, 0, -PI / 2, TQ); QMul(AQ, TQ, ZQ);
            float kr = r * 1.0f; QRot(AQ, 0, -L * 0.9f, -r * 0.1f, TV2);
            Put('S', d, TV2.x, TV2.y, TV2.z, kr, kr * 0.95f, kr, ZQ);
            QRot(AQ, 0, (lo1 + hi1) * 0.5f, -RadAt(0) * 1.1f, TV2); Put('B', g, TV2.x, TV2.y, TV2.z, r * 0.12f, H1 * 0.7f, r * 0.05f, AQ);
            if (sharp) Spike(0, -L * 0.9f, r * 0.9f, r * 0.4f);
        }
        else Plate(plate, d, ya, yb, 1.08f, AQ);
    }

    /// <summary>A copy of a preset with the player's per-segment overrides and attachments applied.</summary>
    public static MedSpec FromCustom(GermCustom c)
    {
        var bse = PRESETS.TryGetValue(c?.Preset ?? "", out var p) ? p : PRESETS["gelcap"];
        var sp = bse.Clone(); sp.Key = "custom";
        if (c == null) return sp;
        if (c.Col != null) foreach (var (k, v) in c.Col) switch (k) { case "a": sp.Col.A = v; break; case "b": sp.Col.B = v; break; case "c": sp.Col.C = v; break; case "e": sp.Col.E = v; break; }
        sp.Col.D = c.ArmorCol;
        if (c.Seg != null) foreach (var (i, s) in c.Seg) if (i >= 0 && i < 16 && s != null) sp.Seg[i] = s.Clone();
        sp.Height = bse.Height * Math.Clamp(c.Height, 0.8f, 1.2f); sp.ArmLen = bse.ArmLen * Math.Clamp(c.ArmLen, 0.75f, 1.3f); sp.LegLen = bse.LegLen * Math.Clamp(c.LegLen, 0.75f, 1.3f);
        if (c.Att != null) sp.Att = c.Att.Select(a => a.Clone()).ToList();
        if (c.Bulk > 0) sp.Bulk = c.Bulk;
        sp.ArmorStyle = string.IsNullOrEmpty(c.Armor) || !ARMOR_STYLES.Contains(c.Armor) ? "plate" : c.Armor;
        return sp;
    }
}

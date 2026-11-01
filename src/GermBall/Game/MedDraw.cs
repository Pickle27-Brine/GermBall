using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
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
    public SegStyle[] Seg = new SegStyle[15];
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
        => new[] { head, torso, pelvis, uarm, farm, hand, uarm.Clone(), farm.Clone(), hand.Clone(), thigh, shin, foot, thigh.Clone(), shin.Clone(), foot.Clone() };
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
    /// <summary>The presets offered for the player (the companions' and the mecha's looks aren't in the customizer list twice).</summary>
    public static readonly string[] PLAYER_PRESETS = { "gelcap", "tablet", "bottle", "syringe", "caplet", "lozenge", "gummy" };
    public static readonly string[] SHAPES = { "capsule", "pill", "tablet", "disc", "cylinder", "box", "sphere", "cone", "vial", "bottle" };
    public static readonly string[] HEAD_SHAPES = { "helmet", "tablethead", "cap", "plunger", "sphere", "capsule", "box", "cone" };
    public static readonly string[] HAND_SHAPES = { "sphere", "capsule", "box", "cylinder" };
    public static readonly string[] FOOT_SHAPES = { "boot", "capsule", "box", "sphere", "cylinder" };
    public static readonly string[] ATTACH_SHAPES = { "sphere", "capsule", "cone", "box", "ring", "disc", "cylinder", "pauldron", "fin", "needle", "cross" };
    public static readonly string[] ARMOR_STYLES = { "plate", "shell", "angular", "ring", "studs", "none" };
    public static readonly string[] PART_NAMES = { "Head", "Torso", "Pelvis", "Left upper arm", "Left forearm", "Left hand", "Right upper arm", "Right forearm", "Right hand", "Left thigh", "Left shin", "Left foot", "Right thigh", "Right shin", "Right foot" };
    public static int MirrorPart(int p) => p switch { 3 => 6, 4 => 7, 5 => 8, 6 => 3, 7 => 4, 8 => 5, 9 => 12, 10 => 13, 11 => 14, 12 => 9, 13 => 10, 14 => 11, _ => p };

    // ------------------------------------------------------------------ drawing
    static readonly Q4 PQ = new(), LQ = new(), WQ = new(), TQ = new();
    static readonly V3 TV = new();
    static float[] ps; static int pk; static Emit em; static char flashTone;

    static void Part(int i) { pk = i * 13; PQ.Set(ps[pk + 3], ps[pk + 4], ps[pk + 5], ps[pk + 6]); }
    /// <summary>Emits a shape positioned in the current part's local frame, optionally with a local rotation.</summary>
    static void Put(char sh, char tone, float lx, float ly, float lz, float sx, float sy, float sz, Q4 local = null)
    {
        QRot(PQ, lx, ly, lz, TV);
        var q = local != null ? QMul(PQ, local, WQ) : PQ;
        em(sh, flashTone != 0 ? flashTone : tone, ps[pk] + TV.x, ps[pk + 1] + TV.y, ps[pk + 2] + TV.z, q.x, q.y, q.z, q.w, sx, sy, sz);
    }
    static Q4 Rx90 => QAxis(1, 0, 0, PI / 2, LQ);

    /// <summary>A tapered capsule (wide at the top, narrow at the bottom): the heroic V of a chest. 'F' is a frustum, top radius 1, bottom 0.72.</summary>
    static void Taper(char tA, char tB, float r, float L, bool two)
    {
        float rt = r * 1.12f, rb = rt * 0.72f, core = MathF.Max(0, L - (rt + rb) * 0.5f);
        if (two)
        {   // the top half is the frustum's upper part, the bottom half its lower: drawn as two frustums meeting at the seam
            float rm = (rt + rb) * 0.5f;
            Put('F', tA, 0, core * 0.5f, 0, rt, core, rt * 1.0f); Put('F', tB, 0, -core * 0.5f, 0, rm, core, rm);
            Put('Z', tA, 0, core, 0, rt, rt * 0.9f, rt * 0.85f); Put('Z', tB, 0, -core, 0, rb, rb, rb);
            Put('T', 'c', 0, 0, 0, rm * 1.04f, rm * 0.9f, rm * 1.04f);
        }
        else { Put('F', tA, 0, 0, 0, rt, core * 2, rt); Put('Z', tA, 0, core, 0, rt, rt * 0.9f, rt * 0.85f); Put('Z', tA, 0, -core, 0, rb, rb, rb); }
    }

    static void Capsule(char tA, char tB, float r, float L, bool two, float ly = 0)
    {
        float core = MathF.Max(0, L - r);
        if (two)
        {
            if (core > 0) { Put('C', tA, 0, ly + core / 2, 0, r, core, r); Put('C', tB, 0, ly - core / 2, 0, r, core, r); }
            Put('Z', tA, 0, ly + core, 0, r, r, r); Put('Z', tB, 0, ly - core, 0, r, r, r);
            Put('T', 'c', 0, ly, 0, r * 1.02f, r * 0.9f, r * 1.02f);   // the seam where the halves join
        }
        else
        {
            if (core > 0) Put('C', tA, 0, ly, 0, r, core * 2, r);
            Put('Z', tA, 0, ly + core, 0, r, r, r); Put('Z', tA, 0, ly - core, 0, r, r, r);
        }
    }

    public static void Draw(MedSpec sp, float[] pose, DrawOpts o, Emit emit)
    {
        ps = pose; em = emit; var has = o.Has; var fl = o.Flash;
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
        for (int i = 0; i < 15; i++)
        {
            if (!has[i]) continue;
            flashTone = fl != null && fl[i] > 0 ? 'f' : '\0';
            DrawSeg(sp, i, sp.Seg[i] ?? new SegStyle());
        }
        flashTone = '\0';
        foreach (var a in sp.Att) if (a.Part >= 0 && a.Part < 15 && has[a.Part]) { flashTone = fl != null && fl[a.Part] > 0 ? 'f' : '\0'; DrawAttach(a, sp); }
        flashTone = '\0';
    }

    /// <summary>The visible radius and half-length of a segment (also used to place armor and attachments).</summary>
    public static (float r, float L) Dims(float[] pose, int i, SegStyle st, MedSpec sp)
    {
        int k = i * 13; float r = (pose[k + 7] + pose[k + 9]) * 0.5f * st.Thick, L = pose[k + 8] * st.Len;
        if (i == 1) r *= sp.Bulk;
        return (r, MathF.Max(L, r));
    }

    static void DrawSeg(MedSpec sp, int i, SegStyle st)
    {
        Part(i); var (r, L) = Dims(ps, i, st, sp); char a = st.Tone, b = st.Tone2;
        switch (st.Shape)
        {
            case "capsule": if (i == 1) Taper(a, b, r, L * 1.05f, st.TwoTone); else Capsule(a, b, r, L, st.TwoTone); break;
            case "pill": if (i == 1) Taper(a, b, r * 0.95f, L * 1.05f, false); else Capsule(a, b, r * 0.92f, L * 1.02f, false); break;
            case "sphere": Put('Z', a, 0, 0, 0, r * 1.05f, MathF.Max(r, L * 0.8f), r * 1.05f); break;
            case "cylinder": Put('C', a, 0, 0, 0, r, L * 2, r); Put('T', b, 0, L * 0.92f, 0, r * 1.02f, r * 0.5f, r * 1.02f); Put('T', b, 0, -L * 0.92f, 0, r * 1.02f, r * 0.5f, r * 1.02f); break;
            case "box": Put('B', a, 0, 0, 0, r * 1.8f, L * 2, r * 1.6f); break;
            case "cone": Put('P', a, 0, 0, 0, r * 1.2f, L * 2, r * 1.2f); break;
            case "disc": Put('C', a, 0, 0, 0, MathF.Max(r, L) * 1.1f, r * 0.9f, MathF.Max(r, L) * 1.1f); Put('T', b, 0, 0, 0, MathF.Max(r, L) * 1.12f, r * 0.6f, MathF.Max(r, L) * 1.12f); break;
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
                {   // a capsule helmet with a wrap-around visor
                    Capsule(a, b, r * 1.0f, L * 1.08f, true);
                    Put('Z', 'e', 0, L * 0.12f, -r * 0.52f, r * 0.88f, r * 0.34f, r * 0.55f);
                    break;
                }
            case "tablethead":
                {
                    float R = MathF.Max(r * 1.15f, L), th = r * 1.15f;
                    Put('C', a, 0, 0, 0, R, th, R, Rx90); Put('T', b, 0, 0, 0, R * 1.01f, th * 0.5f, R * 1.01f, Rx90);
                    Put('B', 'e', 0, R * 0.08f, -th * 0.52f, R * 1.25f, R * 0.18f, 0.03f);
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
                    Put('Z', 'e', -r * 0.38f, L * 0.1f, -r * 1.08f, r * 0.2f, r * 0.2f, r * 0.08f); Put('Z', 'e', r * 0.38f, L * 0.1f, -r * 1.08f, r * 0.2f, r * 0.2f, r * 0.08f);
                    break;
                }
            case "plunger":
                {   // the plunger's thumb press on a short rod, with a visor
                    Put('C', b, 0, -L * 0.2f, 0, r * 0.45f, L * 1.2f, r * 0.45f);
                    Put('C', a, 0, L * 0.45f, 0, r * 1.3f, r * 0.35f, r * 1.3f);
                    Put('Z', a, 0, -L * 0.05f, 0, r * 0.95f, r * 0.8f, r * 0.95f);
                    Put('B', 'e', 0, -L * 0.02f, -r * 0.82f, r * 1.2f, r * 0.2f, r * 0.12f);
                    break;
                }
            case "boot":
                {   // a rounded boot, longer front to back
                    Put('Z', a, 0, 0, -r * 0.35f, r * 1.05f, MathF.Max(r * 0.6f, L * 0.75f), r * 1.7f);
                    Put('B', b, 0, -L * 0.55f, -r * 0.35f, r * 1.85f, r * 0.3f, r * 3.0f);
                    break;
                }
            default: Capsule(a, b, r, L, false); break;
        }
    }

    static readonly Q4 AQ = new();
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
            case "pauldron": Put('Z', at.Tone, lx, ly, lz, sx, sy, sz, AQ); Put('T', 'c', lx, ly - sy * 0.35f, lz, sx * 0.97f, sy * 0.25f, sz * 0.97f, AQ); break;
            case "fin": Put('B', at.Tone, lx, ly, lz, sx, sy, sz, AQ); break;
            case "needle": Put('C', at.Tone, lx, ly, lz, sx * 0.5f, sy * 0.55f, sz * 0.5f, AQ); QRot(AQ, 0, -sy * 0.75f, 0, TV); Put('P', 'k', lx + TV.x, ly + TV.y, lz + TV.z, sx * 0.18f, sy * 0.9f, sz * 0.18f, QMul(AQ, QAxis(1, 0, 0, PI, TQ), TQ)); break;
            case "cross": Put('B', at.Tone, lx, ly, lz, sx, sy * 0.33f, sz, AQ); Put('B', at.Tone, lx, ly, lz, sx * 0.33f, sy, sz, AQ); break;
            default: Put('Z', at.Tone, lx, ly, lz, sx, sy, sz, AQ); break;
        }
    }

    /// <summary>One armor plate: a single thin piece on the side of its segment given by phi, covering y0..y1 of it.
    /// Plates sit just off the surface and never cover more than about half the segment, so the body shows through.</summary>
    public static void Armor(MedSpec sp, float[] pose, int part, float y0, float y1, float phi, string style, char tone, Emit emit)
    {
        ps = pose; em = emit; flashTone = '\0'; Part(part);
        var st = sp.Seg[part] ?? new SegStyle(); var (r, L) = Dims(ps, part, st, sp);
        bool flatFront = st.Shape is "tablet" or "tablethead";
        float yc = (y0 + y1) * 0.5f * L, hh = MathF.Max(r * 0.35f, (y1 - y0) * 0.5f * L * 0.85f), a = phi * PI / 180;
        float nx = MathF.Sin(a), nz = -MathF.Cos(a), off = flatFront ? MathF.Max(r * 1.15f, L) * 0.55f : r * (part == 1 ? 1.08f : 0.97f);
        hh *= 0.75f;   // slim plates: they cover a band of the segment, never the whole thing
        QAxis(0, 1, 0, -a, AQ);
        switch (style)
        {
            case "none": return;
            case "shell": Put('Z', tone, nx * off, yc, nz * off, r * 0.75f, hh * 1.05f, r * 0.3f, AQ); break;
            case "angular": Put('B', tone, nx * off, yc, nz * off, r * 1.0f, hh * 1.5f, r * 0.14f, AQ); break;
            case "ring": Put('T', tone, 0, yc + hh * 0.4f, 0, r * 1.12f, r * 0.7f, r * 1.12f); Put('T', tone, 0, yc - hh * 0.4f, 0, r * 1.12f, r * 0.7f, r * 1.12f); break;
            case "studs": for (int m = -1; m <= 1; m++) Put('Z', tone, nx * off, yc + m * hh * 0.6f, nz * off, r * 0.3f, r * 0.3f, r * 0.2f, AQ); break;
            default: Put('Z', tone, nx * off, yc, nz * off, r * 0.6f, hh, r * 0.2f, AQ); break;
        }
    }

    /// <summary>A copy of a preset with the player's per-segment overrides and attachments applied.</summary>
    public static MedSpec FromCustom(GermCustom c)
    {
        var bse = PRESETS.TryGetValue(c?.Preset ?? "", out var p) ? p : PRESETS["gelcap"];
        var sp = bse.Clone(); sp.Key = "custom";
        if (c == null) return sp;
        if (c.Col != null) foreach (var (k, v) in c.Col) switch (k) { case "a": sp.Col.A = v; break; case "b": sp.Col.B = v; break; case "c": sp.Col.C = v; break; case "e": sp.Col.E = v; break; }
        sp.Col.D = c.ArmorCol;
        if (c.Seg != null) foreach (var (i, s) in c.Seg) if (i >= 0 && i < 15 && s != null) sp.Seg[i] = s.Clone();
        if (c.Att != null) sp.Att = c.Att.Select(a => a.Clone()).ToList();
        if (c.Bulk > 0) sp.Bulk = c.Bulk;
        sp.ArmorStyle = string.IsNullOrEmpty(c.Armor) || !ARMOR_STYLES.Contains(c.Armor) ? "plate" : c.Armor;
        return sp;
    }
}

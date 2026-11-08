using System;
using System.Collections.Generic;
using System.Numerics;

namespace GermBall.Content;

/// <summary>
/// The built-in entities: the gelcap hero and the four germs. They are written to <c>content/entities</c> as the starting
/// point for the Entity Editor, and used directly whenever a file is missing.
/// </summary>
public static class DefaultEntities
{
    public static readonly string[] Names = { "hero", "coccus", "bacillus", "brute", "titan" };

    public static EntityDef Get(string name) => name switch
    {
        "hero" => Hero(),
        "coccus" => Coccus(),
        "bacillus" => Bacillus(),
        "brute" => Brute(),
        "titan" => Titan(),
        _ => null,
    };

    /// <summary>The standard humanoid rig (16 bones). These names are what the procedural motion and ragdolls look for.</summary>
    public static List<BoneDef> HumanoidBones(float pelvisH = 0.98f, float torso = 0.40f, float neck = 0.13f, float headUp = 0.16f,
        float shoulderW = 0.25f, float hipW = 0.11f, float thigh = 0.47f, float shin = 0.46f, float upper = 0.29f, float fore = 0.28f, bool abdomen = false)
    {
        var b = new List<BoneDef>();
        void B(string n, string p, float x, float y, float z) => b.Add(new BoneDef { Name = n, Parent = p, Offset = new Vector3(x, y, z) });
        B("pelvis", null, 0, pelvisH, 0);
        if (abdomen) { B("abdomen", "pelvis", 0, torso / 2, 0); B("chest", "abdomen", 0, torso / 2, 0); }
        else B("chest", "pelvis", 0, torso, 0);
        B("neck", "chest", 0, neck, 0);
        B("head", "neck", 0, headUp, 0);
        foreach (var (s, sg) in new[] { ("L", -1f), ("R", 1f) })
        {
            B("shoulder." + s, "chest", sg * shoulderW, -0.02f, 0);
            B("elbow." + s, "shoulder." + s, 0, -upper, 0);
            B("hand." + s, "elbow." + s, 0, -fore, 0);
            B("hip." + s, "pelvis", sg * hipW, -0.04f, 0);
            B("knee." + s, "hip." + s, 0, -thigh, 0);
            B("foot." + s, "knee." + s, 0, -shin, 0);
        }
        return b;
    }

    static ShapeDef S(string name, string bone, string type, Vector3 pos, Vector3 size, string color, Vector3 rot = default, bool unlit = false) =>
        new() { Name = name, Bone = bone, Type = type, Pos = pos, Size = size, Color = color, Rot = rot, Unlit = unlit };
    static Vector3 V(float x, float y, float z) => new(x, y, z);
    static Vector3 U(float s) => new(s);

    /// <summary>A capsule along a bone from its joint to <paramref name="toChild"/> (an offset straight down or up).</summary>
    static ShapeDef Limb(string name, string bone, float length, float radius, string color, float dir = -1) =>
        S(name, bone, "Capsule", V(0, dir * length / 2, 0), V(radius * 2, length + radius * 2, radius * 2), color);

    // ------------------------------------------------------------------ the hero
    const string Red = "#E8303C", White = "#F4F1EC", Cyan = "#7AFFEF", Boot = "#3A3F48";

    public static EntityDef Hero()
    {
        var e = new EntityDef { Name = "hero", Kind = "player", CurlStyle = "ball", BallColor = Red, Bones = HumanoidBones(abdomen: true) };
        e.Stats = new EntityStats { Hp = 100, Scale = 1, Radius = 0.4f, Height = 1.8f, ContactDamage = 0, DashesToFloor = 1 };
        e.Turn = new TurnSettings { Enabled = true };   // the upper body faces the camera, the legs follow
        var s = e.Shapes;
        foreach (var (side, sg) in new[] { ("L", -1f), ("R", 1f) })
        {
            s.Add(Limb("thigh." + side, "hip." + side, 0.47f, 0.085f, White));
            s.Add(Limb("shin." + side, "knee." + side, 0.46f, 0.072f, Red));
            s.Add(S("boot." + side, "foot." + side, "Box", V(0, 0.02f, -0.05f), V(0.13f, 0.11f, 0.27f), Boot));
            s.Add(S("toe." + side, "foot." + side, "Box", V(0, -0.005f, -0.16f), V(0.12f, 0.06f, 0.07f), White));
            s.Add(S("pauldron." + side, "shoulder." + side, "Sphere", V(sg * 0.03f, 0.03f, 0), U(0.21f), Red));
            s.Add(Limb("upperarm." + side, "shoulder." + side, 0.29f, 0.063f, Red));
            s.Add(Limb("forearm." + side, "elbow." + side, 0.28f, 0.057f, White));
            s.Add(S("hand." + side, "hand." + side, "Sphere", V(0, 0, 0), U(0.136f), White));
        }
        s.Add(S("hips", "pelvis", "Capsule", V(0, -0.04f, 0), V(0.22f, 0.44f, 0.22f), White, V(0, 0, 90)));
        s.Add(S("waist", "pelvis", "Capsule", V(0, 0.09f, 0), V(0.24f, 0.34f, 0.22f), White));
        // the torso in two segments: a narrow red abdomen, then the broad chest
        var ab = S("abdomen", "abdomen", "Capsule", V(0, 0.07f, 0), V(0.27f, 0.42f, 0.24f), Red); ab.Taper = -0.12f; s.Add(ab);
        var ch = S("ribcage", "chest", "Capsule", V(0, -0.06f, 0), V(0.32f, 0.36f, 0.27f), Red); ch.Taper = -0.15f; s.Add(ch);
        s.Add(S("chest", "chest", "Capsule", V(0, -0.05f, 0), V(0.26f, 0.76f, 0.26f), Red, V(0, 0, 90)));
        s.Add(S("cross.h", "chest", "Box", V(0, -0.06f, -0.135f), V(0.12f, 0.035f, 0.02f), Cyan, unlit: true));
        s.Add(S("cross.v", "chest", "Box", V(0, -0.06f, -0.135f), V(0.035f, 0.12f, 0.02f), Cyan, unlit: true));
        s.Add(S("neck", "neck", "Cylinder", V(0, 0.07f, 0), V(0.1f, 0.18f, 0.1f), White));
        s.Add(S("helmet.low", "head", "Capsule", V(0, -0.0125f, 0), V(0.25f, 0.295f, 0.25f), White));
        s.Add(S("helmet.cap", "head", "Sphere", V(0, 0.035f, 0), U(0.244f), Red));
        s.Add(S("helmet.band", "head", "Cylinder", V(0, 0.0175f, 0), V(0.246f, 0.035f, 0.246f), Red));
        s.Add(S("visor", "head", "Box", V(0, 0.005f, -0.105f), V(0.2f, 0.055f, 0.05f), Cyan, unlit: true));

        HeroAnimation(e);
        return e;
    }

    // ------------------------------------------------------------------ keyframed clips
    // Angles in degrees on the standard rig: +X swings a thigh or upper arm forward; -X bends a knee; +X bends an elbow
    // forward; -X on the abdomen or chest leans forward; -Z takes a left arm out to the side (+Z a right arm).
    sealed class ClipBuilder
    {
        public readonly ClipDef Clip;
        public ClipBuilder(string name, float length, bool loop) { Clip = new ClipDef { Name = name, Length = length, Loop = loop }; }
        public ClipBuilder R(string bone, float t, float x, float y = 0, float z = 0) { Key(bone, t).Rot = new Vector3(x, y, z); return this; }
        public ClipBuilder P(string bone, float t, float x, float y, float z) { Key(bone, t).Pos = new Vector3(x, y, z); return this; }
        KeyDef Key(string bone, float t)
        {
            var tr = Clip.Track(bone); if (tr == null) { tr = new TrackDef { Bone = bone }; Clip.Tracks.Add(tr); }
            var k = tr.Keys.Find(x => MathF.Abs(x.T - t) < 1e-4f);
            if (k == null) { k = new KeyDef { T = t }; tr.Keys.Add(k); tr.Keys.Sort((p, q) => p.T.CompareTo(q.T)); }
            return k;
        }
    }

    static void HeroAnimation(EntityDef e)
    {
        var idle = new ClipBuilder("idle", 2.4f, true)
            .R("chest", 0, 0).R("chest", 1.2f, -2).R("abdomen", 0, 0).R("abdomen", 1.2f, -1)
            .R("head", 0, 0).R("head", 1.2f, 2, 3)
            .R("shoulder.L", 0, 2, 0, -6).R("shoulder.L", 1.2f, 4, 0, -8).R("shoulder.R", 0, 2, 0, 6).R("shoulder.R", 1.2f, 4, 0, 8)
            .R("elbow.L", 0, 12).R("elbow.R", 0, 12)
            .R("hip.L", 0, 2, 0, -2).R("hip.R", 0, 2, 0, 2);
        // one stride each way in half a second; the state plays it faster as you run faster so the feet keep up
        var run = new ClipBuilder("run", 0.5f, true)
            .R("hip.L", 0, 40).R("hip.L", 0.25f, -35).R("hip.R", 0, -35).R("hip.R", 0.25f, 40)
            .R("knee.L", 0, -15).R("knee.L", 0.125f, -25).R("knee.L", 0.25f, -60).R("knee.L", 0.375f, -105).R("knee.L", 0.45f, -70)
            .R("knee.R", 0, -60).R("knee.R", 0.125f, -105).R("knee.R", 0.2f, -70).R("knee.R", 0.25f, -15).R("knee.R", 0.375f, -25)
            .R("foot.L", 0, 15).R("foot.L", 0.25f, -20).R("foot.R", 0, -20).R("foot.R", 0.25f, 15)
            .R("shoulder.L", 0, -30, 0, -8).R("shoulder.L", 0.25f, 35, 0, -8).R("shoulder.R", 0, 35, 0, 8).R("shoulder.R", 0.25f, -30, 0, 8)
            .R("elbow.L", 0, 80).R("elbow.L", 0.25f, 70).R("elbow.R", 0, 70).R("elbow.R", 0.25f, 80)
            .R("abdomen", 0, -8, 6).R("abdomen", 0.25f, -8, -6).R("chest", 0, -6, -8).R("chest", 0.25f, -6, 8)
            .R("head", 0, 8)
            .P("pelvis", 0, 0, -0.04f, 0).P("pelvis", 0.125f, 0, 0, 0).P("pelvis", 0.25f, 0, -0.04f, 0).P("pelvis", 0.375f, 0, 0, 0);
        // stepping round on the spot, when the legs catch up with the upper body
        var turn = new ClipBuilder("turn", 0.5f, true)
            .R("hip.L", 0, 0).R("hip.L", 0.125f, 28).R("hip.L", 0.25f, 0).R("knee.L", 0, 0).R("knee.L", 0.125f, -50).R("knee.L", 0.25f, 0)
            .R("hip.R", 0.25f, 0).R("hip.R", 0.375f, 28).R("hip.R", 0.5f, 0).R("knee.R", 0.25f, 0).R("knee.R", 0.375f, -50).R("knee.R", 0.5f, 0)
            .R("foot.L", 0.125f, -15).R("foot.L", 0.25f, 0).R("foot.R", 0.375f, -15).R("foot.R", 0.5f, 0)
            .P("pelvis", 0, 0, 0, 0).P("pelvis", 0.125f, 0, -0.02f, 0).P("pelvis", 0.25f, 0, 0, 0).P("pelvis", 0.375f, 0, -0.02f, 0);
        var skid = new ClipBuilder("skid", 0.4f, false)
            .R("abdomen", 0, 12).R("chest", 0, 6).R("hip.R", 0, 35).R("knee.R", 0, -10).R("hip.L", 0, -15).R("knee.L", 0, -35)
            .R("shoulder.L", 0, 20, 0, -35).R("shoulder.R", 0, 20, 0, 35).R("elbow.L", 0, 30).R("elbow.R", 0, 30);
        var fall = new ClipBuilder("fall", 0.8f, true)
            .R("hip.L", 0, 55).R("hip.L", 0.4f, 50).R("knee.L", 0, -90).R("knee.L", 0.4f, -80)
            .R("hip.R", 0, -10).R("hip.R", 0.4f, -5).R("knee.R", 0, -40).R("knee.R", 0.4f, -50)
            .R("shoulder.L", 0, 40, 0, -40).R("shoulder.L", 0.4f, 45, 0, -50).R("shoulder.R", 0, 40, 0, 40).R("shoulder.R", 0.4f, 45, 0, 50)
            .R("elbow.L", 0, 40).R("elbow.R", 0, 40);
        var hover = new ClipBuilder("hover", 1.2f, true)
            .R("hip.L", 0, -8).R("hip.L", 0.6f, -12).R("hip.R", 0, -12).R("hip.R", 0.6f, -8)
            .R("knee.L", 0, -25).R("knee.R", 0, -30).R("foot.L", 0, -25).R("foot.R", 0, -25)
            .P("pelvis", 0, 0, 0, 0).P("pelvis", 0.6f, 0, 0.03f, 0);
        // aiming: arms forward, blended up and down with the aim
        var aim = new ClipBuilder("aim", 1, true).R("shoulder.R", 0, 88, 0, -8).R("elbow.R", 0, 4).R("shoulder.L", 0, 75, 0, 25).R("elbow.L", 0, 35);
        var aimUp = new ClipBuilder("aim_up", 1, true).R("shoulder.R", 0, 140, 0, -8).R("elbow.R", 0, 4).R("shoulder.L", 0, 125, 0, 25).R("elbow.L", 0, 35);
        var aimDown = new ClipBuilder("aim_down", 1, true).R("shoulder.R", 0, 40, 0, -8).R("elbow.R", 0, 4).R("shoulder.L", 0, 30, 0, 25).R("elbow.L", 0, 35);
        var punch = new ClipBuilder("punch", 0.25f, false)
            .R("shoulder.R", 0, 40).R("shoulder.R", 0.08f, 88, 0, -6).R("shoulder.R", 0.25f, 40)
            .R("elbow.R", 0, 100).R("elbow.R", 0.08f, 0).R("elbow.R", 0.25f, 90);
        var reload = new ClipBuilder("reload", 0.6f, true)
            .R("shoulder.R", 0, 45, 0, -25).R("shoulder.L", 0, 50, 0, 25).R("shoulder.L", 0.3f, 40, 0, 30)
            .R("elbow.R", 0, 100).R("elbow.L", 0, 95).R("elbow.L", 0.3f, 110);
        foreach (var c in new[] { idle, run, turn, skid, fall, hover, aim, aimUp, aimDown, punch, reload }) e.Clips.Add(c.Clip);
        e.Clips.Add(Flinch());

        e.States.Add(new StateClip { State = "idle", Clip = "idle", Fade = 0.25f });
        e.States.Add(new StateClip { State = "run", Clip = "run", Fade = 0.15f, SpeedParam = "speed", SpeedRef = 5.2f, MinRate = 0.5f });
        e.States.Add(new StateClip { State = "turn", Clip = "turn", Fade = 0.1f });
        e.States.Add(new StateClip { State = "skid", Clip = "skid", Fade = 0.1f });
        e.States.Add(new StateClip { State = "jump", Clip = "fall", Fade = 0.15f });
        e.States.Add(new StateClip { State = "fall", Clip = "fall", Fade = 0.2f });
        e.States.Add(new StateClip { State = "hover", Clip = "hover", Fade = 0.2f });

        var upper = new List<string> { "shoulder.L", "shoulder.R" };
        e.Layers.Add(new LayerDef { Name = "Body", Source = "state" });
        e.Layers.Add(new LayerDef { Name = "Aim", Source = "blend", Param = "aimPitch", Mask = new List<string>(upper), ActiveIn = { "aim", "fire", "hover" }, Fade = 0.12f,
            Blend = { new BlendPoint { Clip = "aim_down", Value = -60 }, new BlendPoint { Clip = "aim", Value = 0 }, new BlendPoint { Clip = "aim_up", Value = 60 } } });
        e.Layers.Add(new LayerDef { Name = "Punch", Source = "clip", Clip = "punch", Mask = { "shoulder.R" }, ActiveIn = { "punch" }, Fade = 0.04f });
        e.Layers.Add(new LayerDef { Name = "Reload", Source = "clip", Clip = "reload", Mask = new List<string>(upper), ActiveIn = { "reload" }, Fade = 0.1f });
        e.Layers.Add(new LayerDef { Name = "Flinch", Source = "clip", Clip = "flinch", Mode = "additive", ActiveIn = { "hurt" }, Fade = 0.04f });
        e.Layers.Add(new LayerDef { Name = "Foot IK", Source = "footik" });
    }

    /// <summary>A quick recoil of the chest and head, played additively when hurt.</summary>
    static ClipDef Flinch()
    {
        var c = new ClipDef { Name = "flinch", Length = 0.35f, Loop = false };
        c.Tracks.Add(new TrackDef { Bone = "chest", Keys = { new KeyDef { T = 0 }, new KeyDef { T = 0.08f, Rot = V(-18, 0, 4) }, new KeyDef { T = 0.35f } } });
        c.Tracks.Add(new TrackDef { Bone = "head", Keys = { new KeyDef { T = 0 }, new KeyDef { T = 0.1f, Rot = V(-12, 6, 0) }, new KeyDef { T = 0.35f } } });
        return c;
    }

    /// <summary>A slow throb of the body (scale), for germs.</summary>
    static ClipDef Pulse(string bone, float amount, float length)
    {
        var c = new ClipDef { Name = "pulse", Length = length, Loop = true };
        c.Tracks.Add(new TrackDef { Bone = bone, Keys = { new KeyDef { T = 0 }, new KeyDef { T = length / 2, Scale = new Vector3(1 + amount, 1 - amount * 0.6f, 1 + amount) } } });
        return c;
    }

    // ------------------------------------------------------------------ the germs
    static EntityDef Germ(string name, float scale, float hp, float contact, int dashes, string col, string dark)
    {
        var e = new EntityDef { Name = name, Kind = "enemy", Bones = HumanoidBones(), CurlStyle = "pose", BallColor = col };
        e.Stats = new EntityStats { Hp = hp, Scale = scale, ContactDamage = contact, DashesToFloor = dashes, Radius = 0.42f, Height = 1.8f };
        foreach (var side in new[] { "L", "R" })
        {
            e.Shapes.Add(Limb("thigh." + side, "hip." + side, 0.47f, 0.09f, dark));
            e.Shapes.Add(Limb("shin." + side, "knee." + side, 0.46f, 0.08f, dark));
            e.Shapes.Add(Limb("upperarm." + side, "shoulder." + side, 0.29f, 0.07f, dark));
            e.Shapes.Add(Limb("forearm." + side, "elbow." + side, 0.28f, 0.06f, dark));
            e.Shapes.Add(S("hand." + side, "hand." + side, "Sphere", Vector3.Zero, U(0.17f), col));
        }
        // a menacing sway with the arms up, a wobblier one when staggered
        var idle = new ClipBuilder("idle", 3.2f, true)
            .R("pelvis", 0, 0, 0, 3).R("pelvis", 1.6f, 0, 0, -3).R("chest", 0, 2).R("chest", 0.8f, -2).R("chest", 1.6f, 2).R("chest", 2.4f, -2)
            .R("shoulder.L", 0, 40, 0, -25).R("shoulder.L", 1.6f, 50, 0, -30).R("shoulder.R", 0, 40, 0, 25).R("shoulder.R", 1.6f, 50, 0, 30)
            .R("elbow.L", 0, 50).R("elbow.R", 0, 50).R("hip.L", 0, 0, 0, -3).R("hip.R", 0, 0, 0, 3);
        var stagger = new ClipBuilder("stagger", 0.6f, true)
            .R("pelvis", 0, 0, 0, 10).R("pelvis", 0.3f, 0, 0, -10).R("chest", 0, 12, 0, -8).R("chest", 0.3f, 8, 0, 8)
            .R("shoulder.L", 0, 20, 0, -60).R("shoulder.R", 0, 20, 0, 60).R("elbow.L", 0, 30).R("elbow.R", 0, 30);
        e.Clips.Add(idle.Clip); e.Clips.Add(stagger.Clip);
        e.States.Add(new StateClip { State = "idle", Clip = "idle", Fade = 0.3f });
        e.States.Add(new StateClip { State = "stagger", Clip = "stagger", Fade = 0.1f });
        e.Layers.Add(new LayerDef { Name = "Body", Source = "state" });
        e.Layers.Add(new LayerDef { Name = "Pulse", Source = "clip", Clip = "pulse", Mode = "additive", Mask = { "pelvis" }, MaskChildren = false });
        e.Layers.Add(new LayerDef { Name = "Foot IK", Source = "footik" });
        // arms and legs come off (only to big hits, and only a couple of them)
        e.Limbs = new LimbSettings { Enabled = true, MinHit = 18, MaxLost = 2 };
        foreach (var b in new[] { "shoulder.L", "shoulder.R", "hip.L", "hip.R" }) e.Limbs.Parts.Add(new LimbDef { Bone = b, Hp = MathF.Round(MathF.Max(hp * 0.4f, 2 * e.Limbs.MinHit)) });   // never one ordinary hit
        return e;
    }
    static void Eyes(EntityDef e, string bone, Vector3 at, float r, float gap)
    {
        foreach (float sg in new[] { -1f, 1f })
        {
            e.Shapes.Add(S(sg < 0 ? "eye.L" : "eye.R", bone, "Sphere", at + V(sg * gap, 0, 0), U(r * 2), "#FFFFFF"));
            e.Shapes.Add(S(sg < 0 ? "pupil.L" : "pupil.R", bone, "Sphere", at + V(sg * gap, 0, -r * 0.6f), U(r), "#101010"));
        }
    }

    public static EntityDef Coccus()
    {
        var e = Germ("coccus", 0.92f, 45, 8, 1, "#C060D8", "#6A2A80");
        var body = V(0, 0.22f, 0);
        e.Shapes.Add(S("body", "pelvis", "Sphere", body, U(0.72f), "#C060D8"));
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6;
            e.Shapes.Add(S("bump" + i, "pelvis", "Sphere", body + V(MathF.Cos(a), MathF.Sin(a) * 0.8f, -0.1f) * 0.33f, U(0.18f), "#6A2A80"));
        }
        Eyes(e, "pelvis", body + V(0, 0.12f, -0.3f), 0.07f, 0.1f);
        e.Clips.Add(Pulse("pelvis", 0.05f, 1.6f));
        return e;
    }

    public static EntityDef Bacillus()
    {
        var e = Germ("bacillus", 1.2f, 100, 12, 1, "#70C840", "#2E6A1E");
        e.Shapes.Add(S("body", "pelvis", "Capsule", V(0, 0.32f, 0), V(0.48f, 0.74f + 0.48f, 0.48f), "#70C840"));
        for (int i = -1; i <= 1; i++)
            e.Shapes.Add(S("flagellum" + (i + 1), "pelvis", "Cylinder", V(i * 0.12f, -0.1f, 0.4f), V(0.05f, 0.5f, 0.05f), "#2E6A1E", V(70, i * 25, 0)));
        Eyes(e, "head", V(0, 0, -0.2f), 0.07f, 0.1f);
        e.Clips.Add(Pulse("pelvis", 0.03f, 2.2f));
        return e;
    }

    public static EntityDef Brute()
    {
        var e = Germ("brute", 1.68f, 320, 20, 2, "#E88A30", "#8A4410");
        e.Shapes.Add(S("body", "pelvis", "Sphere", V(0, 0.27f, 0), U(0.9f), "#E88A30"));
        e.Shapes.Add(S("pseudopod.L", "shoulder.L", "Sphere", V(0, 0.05f, 0), U(0.48f), "#E88A30"));
        e.Shapes.Add(S("pseudopod.R", "shoulder.R", "Sphere", V(0, 0.05f, 0), U(0.48f), "#E88A30"));
        e.Shapes.Add(S("belly", "pelvis", "Sphere", V(0, 0, -0.15f), U(0.6f), "#8A4410"));
        Eyes(e, "pelvis", V(0, 0.42f, -0.42f), 0.06f, 0.13f);
        e.Clips.Add(Pulse("pelvis", 0.06f, 2.6f));
        return e;
    }

    public static EntityDef Titan()
    {
        var e = Germ("titan", 2.7f, 900, 30, 4, "#A8B4C4", "#505C6C");
        e.Shapes.Add(S("capsid", "head", "Sphere", V(0, 0.12f, 0), U(0.64f), "#A8B4C4"));
        e.Shapes.Add(S("core", "head", "Sphere", V(0, 0.12f, -0.2f), U(0.24f), "#FF4050", unlit: true));
        e.Shapes.Add(S("collar", "neck", "Cylinder", V(0, 0, 0), V(0.36f, 0.04f, 0.36f), "#505C6C"));
        e.Shapes.Add(S("tail", "pelvis", "Capsule", V(0, 0.27f, 0), V(0.18f, 0.72f, 0.18f), "#505C6C"));
        e.Shapes.Add(S("baseplate", "pelvis", "Cylinder", V(0, 0, 0), V(0.44f, 0.04f, 0.44f), "#505C6C"));
        e.Clips.Add(Pulse("head", 0.04f, 3f));
        e.Layers[1].Mask = new List<string> { "head" };
        e.Limbs.Parts.RemoveAll(l => l.Bone.StartsWith("shoulder"));   // the titan's arms are thin; only its legs come off
        return e;
    }
}

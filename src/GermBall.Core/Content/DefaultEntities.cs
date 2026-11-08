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
        float shoulderW = 0.25f, float hipW = 0.11f, float thigh = 0.47f, float shin = 0.46f, float upper = 0.29f, float fore = 0.28f)
    {
        var b = new List<BoneDef>();
        void B(string n, string p, float x, float y, float z) => b.Add(new BoneDef { Name = n, Parent = p, Offset = new Vector3(x, y, z) });
        B("pelvis", null, 0, pelvisH, 0);
        B("chest", "pelvis", 0, torso, 0);
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
        var e = new EntityDef { Name = "hero", Kind = "player", CurlStyle = "ball", BallColor = Red, Bones = HumanoidBones() };
        e.Stats = new EntityStats { Hp = 100, Scale = 1, Radius = 0.4f, Height = 1.8f, ContactDamage = 0, DashesToFloor = 1 };
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
        s.Add(S("waist", "pelvis", "Capsule", V(0, 0.11f, 0), V(0.24f, 0.38f, 0.24f), White));
        s.Add(S("torso", "pelvis", "Capsule", V(0, 0.28f, 0), V(0.3f, 0.5f, 0.3f), Red));
        s.Add(S("chest", "chest", "Capsule", V(0, -0.05f, 0), V(0.26f, 0.76f, 0.26f), Red, V(0, 0, 90)));
        s.Add(S("cross.h", "chest", "Box", V(0, -0.06f, -0.135f), V(0.12f, 0.035f, 0.02f), Cyan, unlit: true));
        s.Add(S("cross.v", "chest", "Box", V(0, -0.06f, -0.135f), V(0.035f, 0.12f, 0.02f), Cyan, unlit: true));
        s.Add(S("neck", "neck", "Cylinder", V(0, 0.07f, 0), V(0.1f, 0.18f, 0.1f), White));
        s.Add(S("helmet.low", "head", "Capsule", V(0, -0.0125f, 0), V(0.25f, 0.295f, 0.25f), White));
        s.Add(S("helmet.cap", "head", "Sphere", V(0, 0.035f, 0), U(0.244f), Red));
        s.Add(S("helmet.band", "head", "Cylinder", V(0, 0.0175f, 0), V(0.246f, 0.035f, 0.246f), Red));
        s.Add(S("visor", "head", "Box", V(0, 0.005f, -0.105f), V(0.2f, 0.055f, 0.05f), Cyan, unlit: true));

        // a sample clip and the layer stack: procedural motion underneath, state clips added on top
        e.Clips.Add(Flinch());
        e.Layers.Add(new LayerDef { Name = "Locomotion", Source = "procedural", Mode = "override" });
        e.Layers.Add(new LayerDef { Name = "Reactions", Source = "state", Mode = "additive" });
        e.States.Add(new StateClip { State = "hurt", Clip = "flinch", Fade = 0.05f });
        return e;
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
        e.Layers.Add(new LayerDef { Name = "Idle", Source = "procedural", Mode = "override" });
        e.Layers.Add(new LayerDef { Name = "Pulse", Source = "clip", Clip = "pulse", Mode = "additive", Mask = { "pelvis" }, MaskChildren = false });
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
        return e;
    }
}

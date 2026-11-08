using System;
using System.Collections.Generic;
using System.Numerics;

namespace GermBall.Content;

/// <summary>
/// Everything about one kind of entity (the hero, a germ, a prop): its rig, the shapes that make up its model, its
/// animation clips, the layer stack that blends them, which clip plays for each game state, and its gameplay numbers.
/// Saved as <c>content/entities/&lt;name&gt;.entity.json</c>; the Entity Editor writes these and the game reads them.
/// </summary>
public sealed class EntityDef
{
    public int Version = 1;
    public string Name = "entity";
    /// <summary>"player", "enemy" or "prop".</summary>
    public string Kind = "enemy";
    public EntityStats Stats = new();
    /// <summary>The rig. Bones with no parent hang off the entity's origin (the point between its feet).</summary>
    public List<BoneDef> Bones = new();
    public List<ShapeDef> Shapes = new();
    public List<ClipDef> Clips = new();
    /// <summary>The animation pipeline: layers evaluated bottom to top.</summary>
    public List<LayerDef> Layers = new();
    /// <summary>Which clip each game state plays on "state" layers.</summary>
    public List<StateClip> States = new();
    /// <summary>How the entity looks curled up: "ball" (a striped ball) or "pose" (its own shapes, spun as a ball).</summary>
    public string CurlStyle = "ball";
    public string BallColor = "#E8303C";
    /// <summary>Limbs that can be shot off.</summary>
    public LimbSettings Limbs = new();
    /// <summary>How the upper body turns ahead of the legs.</summary>
    public TurnSettings Turn = new();

    public BoneDef Bone(string name) => Bones.Find(b => b.Name == name);
    public ClipDef Clip(string name) => string.IsNullOrEmpty(name) ? null : Clips.Find(c => c.Name == name);
}

public sealed class EntityStats
{
    public float Hp = 100;
    /// <summary>Uniform size multiplier for the whole entity (rig, shapes and collision).</summary>
    public float Scale = 1;
    public float ContactDamage = 10;
    /// <summary>How many jump dashes knock it into a ragdoll.</summary>
    public int DashesToFloor = 1;
    /// <summary>Collision cylinder at scale 1.</summary>
    public float Radius = 0.42f, Height = 1.8f;
}

public sealed class BoneDef
{
    public string Name = "bone";
    public string Parent;
    /// <summary>Where this joint sits relative to its parent joint, in the parent's frame at rest (metres, Y up, -Z forward).</summary>
    public Vector3 Offset;
}

public static class ShapeTypes
{
    public static readonly string[] All = { "Box", "Sphere", "Capsule", "Cylinder", "Cone", "Ball" };
}

public sealed class ShapeDef
{
    public string Name = "shape";
    public string Bone;
    /// <summary>Box, Sphere, Capsule, Cylinder, Cone or Ball (striped sphere).</summary>
    public string Type = "Box";
    /// <summary>Position and rotation (degrees: pitch X, yaw Y, roll Z) in the bone's frame.</summary>
    public Vector3 Pos, Rot;
    /// <summary>Full size along each axis. Capsules, cylinders and cones run along Y.</summary>
    public Vector3 Size = new(0.2f);
    public string Color = "#FFFFFF";
    /// <summary>-1..1: positive narrows the top (+Y) end, negative the bottom (a limb thinning toward the wrist).</summary>
    public float Taper;
    /// <summary>-1..1: bends the shape along its length toward +X; the top end moves this many lengths sideways (rotate the shape to bend another way).</summary>
    public float Curve;
    /// <summary>Drawn at full brightness, ignoring light (eyes, glows).</summary>
    public bool Unlit;
    public bool Hidden;
}

public sealed class ClipDef
{
    public string Name = "clip";
    public float Length = 1;
    public bool Loop = true;
    public List<TrackDef> Tracks = new();
    public TrackDef Track(string bone) => Tracks.Find(t => t.Bone == bone);
}

public sealed class TrackDef
{
    public string Bone;
    /// <summary>Kept sorted by time.</summary>
    public List<KeyDef> Keys = new();
}

public sealed class KeyDef
{
    public float T;
    /// <summary>Bone rotation in degrees (pitch X, yaw Y, roll Z), relative to rest.</summary>
    public Vector3 Rot;
    /// <summary>Bone translation from its rest position.</summary>
    public Vector3 Pos;
    /// <summary>Scale applied to the bone's shapes (squash and stretch).</summary>
    public Vector3 Scale = Vector3.One;
    /// <summary>How to get from this key to the next: "smooth", "linear" or "step".</summary>
    public string Ease = "smooth";
    public KeyDef Clone() => (KeyDef)MemberwiseClone();
}

public static class LayerSources
{
    /// <summary>state: the clip the current game state maps to (States), crossfaded.
    /// clip: one clip on the layer's own clock. blend: mixes clips by a parameter (aim angle, speed).
    /// footik: plants the feet on the ground (stairs, slopes) after everything else; the only procedural motion.</summary>
    public static readonly string[] All = { "state", "clip", "blend", "footik" };
    public static readonly string[] Modes = { "override", "additive" };
    /// <summary>Values the game feeds the animator: horizontal speed (m/s), aim pitch (degrees, up positive), vertical speed (m/s).</summary>
    public static readonly string[] Params = { "speed", "aimPitch", "vspeed" };
}

public sealed class BlendPoint
{
    public string Clip; public float Value;
}

public sealed class LayerDef
{
    public string Name = "layer";
    public string Source = "clip";
    public string Clip;
    /// <summary>"override" replaces what's below (by weight), "additive" adds on top of it.</summary>
    public string Mode = "override";
    public float Weight = 1, Speed = 1;
    /// <summary>Bones this layer affects (empty: all of them).</summary>
    public List<string> Mask = new();
    /// <summary>A masked bone brings its children along.</summary>
    public bool MaskChildren = true;
    public bool Enabled = true;
    /// <summary>Game states the layer plays in (empty: always). It fades in and out over <see cref="Fade"/> seconds.</summary>
    public List<string> ActiveIn = new();
    public float Fade = 0.15f;
    /// <summary>Playback rate follows a parameter: rate = param / SpeedRef (e.g. a run clip that matches the running speed).</summary>
    public string SpeedParam;
    public float SpeedRef = 1;
    /// <summary>blend layers: the parameter, and the clips placed along it.</summary>
    public string Param = "aimPitch";
    public List<BlendPoint> Blend = new();
}

public sealed class StateClip
{
    public string State;
    public string Clip;
    /// <summary>Crossfade time into this clip.</summary>
    public float Fade = 0.15f;
    /// <summary>Playback rate follows a parameter (rate = param / SpeedRef); a run clip keeps its feet planted at any speed.</summary>
    public string SpeedParam;
    public float SpeedRef = 1;
    public float MinRate = 0.3f;
}

/// <summary>Destructible limbs: hits on a limb wear it down, and a big enough hit takes it off.
/// Deliberately hard to trigger: only hits of at least <see cref="MinHit"/> count, and only a few limbs can go.</summary>
/// <summary>The upper body faces where the entity looks; the hips and legs follow behind.
/// Standing still, the feet stay put until the twist passes <see cref="MaxTwist"/>, then the legs step round.</summary>
public sealed class TurnSettings
{
    public bool Enabled;
    /// <summary>Degrees the upper body can turn before the legs follow (standing still).</summary>
    public float MaxTwist = 50;
    /// <summary>How quickly the legs catch up when they step round (per second; higher is snappier).</summary>
    public float StepSpeed = 7;
    /// <summary>How quickly the legs follow while moving or in the air.</summary>
    public float MoveSpeed = 12;
    /// <summary>How much of the twist the chest takes (the rest goes to the abdomen, if there is one).</summary>
    public float ChestShare = 0.6f;
}

public sealed class LimbSettings
{
    public bool Enabled;
    /// <summary>Hits smaller than this never hurt a limb.</summary>
    public float MinHit = 18;
    /// <summary>How many limbs it can lose before it's just killed.</summary>
    public int MaxLost = 2;
    /// <summary>Seconds a severed limb lies around.</summary>
    public float DebrisLife = 6;
    public List<LimbDef> Parts = new();
}

public sealed class LimbDef
{
    /// <summary>The bone where the limb comes off; everything below it goes with it.</summary>
    public string Bone;
    public float Hp = 40;
    /// <summary>Losing it kills (a head).</summary>
    public bool Critical;
}

/// <summary>The game states an entity can be in, for state layers.</summary>
public static class AnimStates
{
    public static readonly string[] All = { "idle", "turn", "run", "skid", "jump", "fall", "curl", "spin", "hover", "aim", "fire", "punch", "reload", "hurt", "stagger", "down", "dead" };
}

/// <summary>Friendly names for the standard bones (a bone is the segment from its joint to its child's).</summary>
public static class BoneNames
{
    public static string Friendly(string bone) => bone switch
    {
        "pelvis" => "Hips", "abdomen" => "Abdomen", "chest" => "Chest", "neck" => "Neck", "head" => "Head",
        "shoulder.L" => "Upper arm L", "elbow.L" => "Forearm L", "hand.L" => "Hand L",
        "shoulder.R" => "Upper arm R", "elbow.R" => "Forearm R", "hand.R" => "Hand R",
        "hip.L" => "Thigh L", "knee.L" => "Shin L", "foot.L" => "Foot L",
        "hip.R" => "Thigh R", "knee.R" => "Shin R", "foot.R" => "Foot R",
        null => "(origin)",
        _ => bone,
    };
}

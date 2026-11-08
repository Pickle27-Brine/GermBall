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
    /// <summary>procedural: the game's built-in motion (run cycle, IK, aim; idle sway for enemies).
    /// clip: one clip, looping on the layer's own clock. state: the clip the current game state maps to, crossfaded.</summary>
    public static readonly string[] All = { "procedural", "clip", "state" };
    public static readonly string[] Modes = { "override", "additive" };
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
}

public sealed class StateClip
{
    public string State;
    public string Clip;
    /// <summary>Crossfade time into this clip.</summary>
    public float Fade = 0.15f;
}

/// <summary>The game states an entity can be in, for state layers.</summary>
public static class AnimStates
{
    public static readonly string[] All = { "idle", "run", "skid", "jump", "fall", "curl", "spin", "hover", "aim", "fire", "punch", "reload", "hurt", "stagger", "down", "dead" };
}

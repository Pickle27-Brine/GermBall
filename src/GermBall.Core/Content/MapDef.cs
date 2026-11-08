using System;
using System.Collections.Generic;
using System.Numerics;

namespace GermBall.Content;

/// <summary>
/// A map: where the player starts, and a flat list of objects (solids, ramps, zones, spawns, signs, landmarks).
/// Saved as <c>content/maps/&lt;name&gt;.map.json</c>; the Map Editor writes these and the game reads them.
/// </summary>
public sealed class MapDef
{
    public int Version = 1;
    public string Name = "map";
    public Vector3 Spawn = new(0, 0.1f, 0);
    public float SpawnYaw;
    /// <summary>Anything below this height dies.</summary>
    public float KillY = -40;
    public List<MapObject> Objects = new();
}

public static class MapTypes
{
    public const string Box = "box", Ramp = "ramp", Zone = "zone", Germ = "germ", Dummy = "dummy", Ball = "ball", Pickup = "pickup", Sign = "sign", Landmark = "landmark";
    public static readonly string[] All = { Box, Ramp, Zone, Germ, Dummy, Ball, Pickup, Sign, Landmark };
}

/// <summary>One thing in a map. Which fields matter depends on <see cref="Type"/>.</summary>
public sealed class MapObject
{
    public string Type = MapTypes.Box;
    /// <summary>Optional label (landmark names, and handy in the editor's list).</summary>
    public string Name = "";
    /// <summary>box: centre. ramp: where it leaves the ground. zone: centre. spawns, signs, landmarks: the point itself (feet).</summary>
    public Vector3 Pos;
    /// <summary>box: rotation in degrees (pitch X, yaw Y, roll Z).</summary>
    public Vector3 Rot;
    /// <summary>box and zone: full size.</summary>
    public Vector3 Size = Vector3.One;
    public string Color = "#8A8F96";
    /// <summary>box: draw the 1 m grid.</summary>
    public bool Grid = true;

    // zone
    /// <summary>Area, Damage, Heal, Kill, JumpPad, Boost, LowGravity, Updraft or Checkpoint.</summary>
    public string Zone = "Area";
    public float Power;
    /// <summary>Jump pads: launch velocity. Boost pads: push direction.</summary>
    public Vector3 Dir;

    // ramp (and germs, landmarks: Yaw)
    public float Yaw, Angle = 20, Height = 2, Width = 3, Landing = 3;

    // spawns
    /// <summary>germ: which entity (content/entities/&lt;Entity&gt;.entity.json).</summary>
    public string Entity = "coccus";
    /// <summary>dummy: the other end of its rail (when Speed &gt; 0).</summary>
    public Vector3 RailEnd;
    public float Speed;
    public bool Heavy;
    /// <summary>ball: radius.</summary>
    public float Radius = 0.5f;
    /// <summary>pickup: Health, Ammo, Rifle or Lobber.</summary>
    public string Pickup = "Health";

    // sign
    public string Text = "";
    public float Range = 45;

    public MapObject Clone() => (MapObject)MemberwiseClone();
}

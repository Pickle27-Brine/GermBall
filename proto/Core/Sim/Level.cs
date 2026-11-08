using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Proto.Engine;

namespace GermBall.Proto.Sim;

public sealed class Solid { public Obb Box; public Vector4 Color; public bool Grid = true; }

public enum ZoneKind { Area, Damage, Heal, Kill, JumpPad, Boost, LowGravity, Updraft, Checkpoint }

/// <summary>An axis-aligned trigger volume. Area zones only name the region you're in; the rest act on whoever stands in them.</summary>
public sealed class Zone
{
    public string Name; public ZoneKind Kind; public Vector3 Min, Max;
    /// <summary>Damage or healing per second, boost speed, gravity scale or updraft acceleration, by kind. Jump pads keep their launch velocity in Dir.</summary>
    public float Power; public Vector3 Dir;
    public bool Contains(Vector3 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;
    public Vector3 Center => (Min + Max) * 0.5f;
}

public sealed class Landmark { public string Name; public Vector3 Pos; public float Yaw; }
public sealed class Label3D { public Vector3 Pos; public string Text; public float Range = 45; }

public enum DummyKind { Standard, Heavy }
public sealed class DummySpawn { public Vector3 Pos, RailEnd; public float Speed; public DummyKind Kind; }
public sealed class PropSpawn { public Vector3 Pos; public float Radius; public Vector4 Color; }
public enum PickupKind { Health, Ammo, Rifle, Lobber }
public sealed class PickupSpawn { public Vector3 Pos; public PickupKind Kind; }

/// <summary>A level: static boxes, trigger zones, signs, teleport landmarks and where things spawn.</summary>
public sealed class Level
{
    public string Name = "Debug";
    public readonly StaticWorld World = new();
    public readonly List<Solid> Solids = new();
    public readonly List<Zone> Zones = new();
    public readonly List<Landmark> Landmarks = new();
    public readonly List<Label3D> Labels = new();
    public readonly List<DummySpawn> Dummies = new();
    public readonly List<PropSpawn> Props = new();
    public readonly List<PickupSpawn> Pickups = new();
    public Vector3 Spawn; public float SpawnYaw;
    /// <summary>Anything that falls below this is dead.</summary>
    public float KillY = -40;

    // ---------------------------------------------------------------- building blocks
    public Solid Box(Vector3 center, Vector3 size, Quaternion rot, Vector4 color, bool grid = true)
    {
        var s = new Solid { Box = new Obb(center, size * 0.5f, rot), Color = color, Grid = grid };
        Solids.Add(s); World.Add(s.Box); return s;
    }
    public Solid Box(Vector3 center, Vector3 size, Vector4 color, bool grid = true) => Box(center, size, Quaternion.Identity, color, grid);
    /// <summary>A box from its ground footprint: x and z of the centre, the footprint size, and the height of its top above y = 0.</summary>
    public Solid Block(float x, float z, float sx, float sz, float top, Vector4 color, float bottom = 0) =>
        Box(new Vector3(x, (top + bottom) / 2, z), new Vector3(sx, top - bottom, sz), color);

    /// <summary>A ramp that starts at ground level at <paramref name="start"/> and climbs <paramref name="height"/> metres at
    /// <paramref name="angleDeg"/> degrees toward <paramref name="yaw"/>, with a landing at the top.</summary>
    public void Ramp(Vector3 start, float yaw, float angleDeg, float height, float width, Vector4 color, float landing = 3)
    {
        float a = angleDeg * MathX.Deg, run = height / MathF.Tan(a), slopeLen = height / MathF.Sin(a);
        const float thick = 0.6f;
        var fwd = MathX.Forward(yaw, 0);
        var rot = Quaternion.CreateFromYawPitchRoll(yaw, a, 0);
        var up = Vector3.Transform(Vector3.UnitY, rot);
        var mid = start + fwd * (run / 2) + Vector3.UnitY * (height / 2) - up * (thick / 2);
        Box(mid, new Vector3(width, thick, slopeLen), rot, color);
        if (landing > 0)
        {
            var lc = start + fwd * (run + landing / 2);
            var size = MathF.Abs(fwd.X) > 0.5f ? new Vector3(landing, height, width) : new Vector3(width, height, landing);
            Box(new Vector3(lc.X, height / 2, lc.Z), size, color);
        }
    }

    public Zone AddZone(string name, ZoneKind kind, Vector3 min, Vector3 max, float power = 0, Vector3 dir = default)
    {
        var z = new Zone { Name = name, Kind = kind, Min = min, Max = max, Power = power, Dir = dir };
        Zones.Add(z); return z;
    }
    /// <summary>A 2 × 2 m pad that throws you onto a ledge whose top is <paramref name="top"/> metres up and centred at <paramref name="land"/>.
    /// The launch velocity is stored in the zone's Dir.</summary>
    public Zone JumpPad(string name, Vector3 pad, float top, Vector3 land)
    {
        float v = MathF.Sqrt(2 * Player.Gravity * (top + 1.6f)), tUp = v / Player.Gravity, tDown = MathF.Sqrt(2 * 1.6f / Player.Gravity);
        var h = MathX.Flat(land - pad) / (tUp + tDown);
        return AddZone(name, ZoneKind.JumpPad, pad - new Vector3(1, 0.5f, 1), pad + new Vector3(1, 0.4f, 1), v, new Vector3(h.X, v, h.Z));
    }
    public void Sign(Vector3 pos, string text, float range = 45) => Labels.Add(new Label3D { Pos = pos, Text = text, Range = range });
    public void Mark(string name, Vector3 pos, float yaw) => Landmarks.Add(new Landmark { Name = name, Pos = pos, Yaw = yaw });

    // ---------------------------------------------------------------- the debug level
    static readonly Vector4 FloorCol = MathX.Rgb(0x8A8F96), WallCol = MathX.Rgb(0x5E6670),
        MoveCol = MathX.Rgb(0x6F8FB8), CombatCol = MathX.Rgb(0xB89A6F), PhysCol = MathX.Rgb(0x9A7FB8), HazCol = MathX.Rgb(0x6FB8A8), HubCol = MathX.Rgb(0xC8C8C8);

    /// <summary>
    /// A flat 240 m square with simple test geometry, one area per system:
    /// north for movement (steps, ramps, stairs, gaps, walls, a crawl tunnel), east for combat (dummies at known ranges),
    /// west for physics (balls, a slope, a skate bowl), south for zones (acid, healing, a pit, jump pads, low gravity, an updraft, a boost lane).
    /// North is -Z. Every solid shows a 1 m grid.
    /// </summary>
    public static Level BuildDebug()
    {
        var L = new Level { Name = "Debug Level" };
        const float E = 120;   // half the floor size

        // floor, 4 m thick so the pit has walls, with a 12 × 8 m hole for the pit (x -6..6, z 44..52)
        L.Box(new Vector3(0, -2, (-E + 44) / 2), new Vector3(2 * E, 4, E + 44), FloorCol);
        L.Box(new Vector3(0, -2, (52 + E) / 2), new Vector3(2 * E, 4, E - 52), FloorCol);
        L.Box(new Vector3((-E - 6) / 2, -2, 48), new Vector3(E - 6, 4, 8), FloorCol);
        L.Box(new Vector3((6 + E) / 2, -2, 48), new Vector3(E - 6, 4, 8), FloorCol);
        // perimeter walls
        L.Box(new Vector3(0, 3, -E - 0.5f), new Vector3(2 * E + 2, 6, 1), WallCol);
        L.Box(new Vector3(0, 3, E + 0.5f), new Vector3(2 * E + 2, 6, 1), WallCol);
        L.Box(new Vector3(-E - 0.5f, 3, 0), new Vector3(1, 6, 2 * E), WallCol);
        L.Box(new Vector3(E + 0.5f, 3, 0), new Vector3(1, 6, 2 * E), WallCol);

        // ---- hub
        L.Spawn = new Vector3(0, 0.1f, 0); L.SpawnYaw = 0;
        L.Block(0, 0, 4, 4, 0.05f, HubCol);
        L.AddZone("Hub", ZoneKind.Area, new Vector3(-10, -1, -10), new Vector3(10, 30, 10));
        L.Sign(new Vector3(0, 3.2f, -8), "MOVEMENT  (north)");
        L.Sign(new Vector3(8, 3.2f, 0), "COMBAT  (east)");
        L.Sign(new Vector3(-8, 3.2f, 0), "PHYSICS  (west)");
        L.Sign(new Vector3(0, 3.2f, 8), "ZONES  (south)");
        L.Mark("Hub", L.Spawn, 0);

        BuildMovement(L);
        BuildCombat(L);
        BuildPhysics(L);
        BuildZones(L);
        return L;
    }

    static void BuildMovement(Level L)
    {
        L.AddZone("Movement", ZoneKind.Area, new Vector3(-50, -1, -120), new Vector3(50, 60, -12));

        // step heights: the controller steps up 0.45 m; anything taller has to be jumped
        float[] steps = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.8f };
        for (int i = 0; i < steps.Length; i++)
        {
            float x = -15 + i * 5;
            L.Block(x, -21.5f, 3, 3, steps[i], MoveCol);
            L.Sign(new Vector3(x, steps[i] + 1.2f, -21.5f), $"{steps[i]:0.0} m", 25);
        }
        L.Sign(new Vector3(0, 3.4f, -17), "STEP HEIGHTS");
        L.Mark("Steps", new Vector3(0, 0.1f, -15), 0);

        // ramps: 2.5 m tall at each angle (walkable up to 50 degrees)
        float[] angles = { 10, 20, 30, 40, 45, 50, 55, 60 };
        for (int i = 0; i < angles.Length; i++)
        {
            float x = -17.5f + i * 5;
            L.Ramp(new Vector3(x, 0, -30), 0, angles[i], 2.5f, 3, MoveCol);
            L.Sign(new Vector3(x, 1.6f, -29), $"{angles[i]:0} deg", 25);
        }
        L.Sign(new Vector3(0, 4, -27), "RAMP ANGLES");
        L.Mark("Ramps", new Vector3(0, 0.1f, -25), 0);

        // stairs: 0.25 m rises climb, 0.5 m rises don't
        void Stairs(float x, float rise, int n, string label)
        {
            const float run = 0.45f;
            for (int i = 0; i < n; i++) L.Block(x, -30 - i * run - run / 2, 3, run, rise * (i + 1), MoveCol);
            float top = rise * n, end = -30 - n * run;
            L.Block(x, end - 1.5f, 3, 3, top, MoveCol);
            L.Sign(new Vector3(x, 1.8f, -29), label, 25);
        }
        Stairs(-30, 0.25f, 12, "STAIRS 0.25");
        Stairs(-25, 0.5f, 6, "STAIRS 0.5");
        L.Mark("Stairs", new Vector3(-27.5f, 0.1f, -24), 0);

        // walls to jump onto
        float[] walls = { 1, 1.5f, 2, 2.5f, 3, 4 };
        for (int i = 0; i < walls.Length; i++)
        {
            float x = 26 + i * 4;
            L.Block(x, -32, 3, 2, walls[i], MoveCol);
            L.Sign(new Vector3(x, walls[i] + 1.2f, -31), $"{walls[i]:0.0} m", 25);
        }
        L.Sign(new Vector3(36, 5.5f, -30), "WALL HEIGHTS");
        L.Mark("Walls", new Vector3(36, 0.1f, -24), 0);

        // gap jumps between 1 m platforms, 3 m deep
        float z = -50;
        L.Block(0, z - 2, 4, 4, 1, MoveCol); z -= 4;
        float[] gaps = { 2, 3, 4, 5, 6, 7, 8 };
        foreach (var g in gaps)
        {
            L.Sign(new Vector3(0, 2.4f, z - g / 2), $"{g:0} m gap", 25);
            z -= g;
            L.Block(0, z - 1.5f, 4, 3, 1, MoveCol); z -= 3;
        }
        L.Sign(new Vector3(0, 4, -47), "GAP JUMPS");
        L.Mark("Gaps", new Vector3(0, 1.1f, -51), 0);

        // crawl tunnel: 1.1 m of headroom, only passable rolled up
        L.Block(-26.75f, -60, 0.5f, 10, 2, MoveCol);
        L.Block(-23.25f, -60, 0.5f, 10, 2, MoveCol);
        L.Block(-25, -60, 4, 10, 1.6f, MoveCol, 1.1f);
        L.Sign(new Vector3(-25, 3, -54), "CRAWL TUNNEL (roll)");
        L.Mark("Crawl tunnel", new Vector3(-25, 0.1f, -50), 0);

        // a tall tower of stepped ledges for drops and ground pounds
        float[] ledges = { 2, 4, 8, 16 };
        for (int i = 0; i < ledges.Length; i++)
        {
            float x = 26 + i * 6;
            L.Block(x, -60, 4, 4, ledges[i], MoveCol);
            L.Sign(new Vector3(x, ledges[i] + 1.2f, -57.5f), $"{ledges[i]:0} m", 40);
        }
        L.Sign(new Vector3(35, 3, -53), "DROP HEIGHTS");
        L.Mark("Drops", new Vector3(35, 0.1f, -50), 0);
        // jump pads to reach them
        for (int i = 0; i < ledges.Length; i++)
        {
            float x = 26 + i * 6;
            L.JumpPad($"Pad to {ledges[i]:0} m", new Vector3(x, 0, -55), ledges[i], new Vector3(x, 0, -60));
        }
    }

    static void BuildCombat(Level L)
    {
        L.AddZone("Combat range", ZoneKind.Area, new Vector3(12, -1, -30), new Vector3(120, 60, 22));
        // firing line and armory
        L.Block(20, 0, 0.3f, 24, 0.05f, MathX.Rgb(0xE0C040));
        L.Sign(new Vector3(20, 2.5f, -10), "FIRING LINE", 30);
        L.Sign(new Vector3(15, 2.5f, 0), "ARMORY", 25);
        L.Pickups.Add(new PickupSpawn { Pos = new Vector3(14, 0.6f, -4), Kind = PickupKind.Rifle });
        L.Pickups.Add(new PickupSpawn { Pos = new Vector3(14, 0.6f, 4), Kind = PickupKind.Lobber });
        L.Pickups.Add(new PickupSpawn { Pos = new Vector3(16, 0.6f, -6), Kind = PickupKind.Ammo });
        L.Pickups.Add(new PickupSpawn { Pos = new Vector3(16, 0.6f, 6), Kind = PickupKind.Ammo });
        L.Pickups.Add(new PickupSpawn { Pos = new Vector3(12, 0.6f, 8), Kind = PickupKind.Health });
        L.Mark("Combat range", new Vector3(17, 0.1f, 0), MathF.PI / 2 * -1);

        // dummies at known ranges from the firing line
        float[] ranges = { 10, 20, 35, 50, 75 };
        for (int i = 0; i < ranges.Length; i++)
        {
            float x = 20 + ranges[i], z = -12 + i * 6;
            L.Dummies.Add(new DummySpawn { Pos = new Vector3(x, 0, z) });
            L.Sign(new Vector3(x, 3.2f, z), $"{ranges[i]:0} m", 90);
            L.Block(x, z, 4, 0.2f, 0.05f, CombatCol);
        }
        // movers on rails, and a heavy
        L.Dummies.Add(new DummySpawn { Pos = new Vector3(45, 0, -14), RailEnd = new Vector3(45, 0, 14), Speed = 3 });
        L.Dummies.Add(new DummySpawn { Pos = new Vector3(70, 0, 14), RailEnd = new Vector3(70, 0, -14), Speed = 7 });
        L.Block(45, 0, 0.3f, 28, 0.04f, CombatCol);
        L.Block(70, 0, 0.3f, 28, 0.04f, CombatCol);
        L.Sign(new Vector3(45, 3.2f, 16), "MOVER 3 m/s", 90);
        L.Sign(new Vector3(70, 3.2f, 16), "MOVER 7 m/s", 90);
        L.Dummies.Add(new DummySpawn { Pos = new Vector3(32, 0, 18), Kind = DummyKind.Heavy });
        L.Sign(new Vector3(32, 4, 18), "HEAVY 400 HP", 60);
        // waist-high cover and a pillar
        L.Block(24, -5, 1, 3, 1.1f, CombatCol);
        L.Block(24, 5, 1, 3, 1.1f, CombatCol);
        L.Block(28, 0, 1.5f, 1.5f, 4, CombatCol);
        // backstop
        L.Block(112, 0, 2, 50, 10, WallCol);
    }

    static void BuildPhysics(Level L)
    {
        L.AddZone("Physics", ZoneKind.Area, new Vector3(-120, -1, -20), new Vector3(-12, 60, 30));
        var cols = new[] { MathX.Rgb(0xE05050), MathX.Rgb(0x50A0E0), MathX.Rgb(0xE0C050), MathX.Rgb(0x60C060), MathX.Rgb(0xC060C0) };
        float[] radii = { 0.3f, 0.45f, 0.6f, 0.9f, 1.3f };
        for (int i = 0; i < radii.Length; i++)
            for (int k = 0; k < 2; k++)
                L.Props.Add(new PropSpawn { Pos = new Vector3(-22 - i * 3.5f, radii[i] + 0.05f, -4 + k * 8), Radius = radii[i], Color = cols[i] });
        L.Sign(new Vector3(-28, 3.5f, 0), "BALLS (shoot, punch, push)");
        L.Mark("Physics", new Vector3(-16, 0.1f, 0), MathF.PI / 2);

        // a long 12 degree slope with balls at the top
        L.Ramp(new Vector3(-44, 0, 22), MathF.PI / 2, 12, 4, 6, PhysCol, 4);
        float topX = -44 - 4 / MathF.Tan(12 * MathX.Deg) - 2;
        for (int i = 0; i < 3; i++)
            L.Props.Add(new PropSpawn { Pos = new Vector3(topX, 4.7f, 20 + i * 2), Radius = 0.5f, Color = cols[i] });
        L.Sign(new Vector3(-50, 4, 18), "ROLLING SLOPE");

        // skate bowl: a 12 m flat ringed by 25 degree banks, for rolling momentum
        var c = new Vector3(-80, 0, -2);
        const float flat = 6, h = 3;
        float run = h / MathF.Tan(25 * MathX.Deg);
        L.Ramp(c + new Vector3(0, 0, -flat), 0, 25, h, 2 * flat, PhysCol, 2);
        L.Ramp(c + new Vector3(0, 0, flat), MathF.PI, 25, h, 2 * flat, PhysCol, 2);
        L.Ramp(c + new Vector3(flat, 0, 0), -MathF.PI / 2, 25, h, 2 * flat, PhysCol, 2);
        L.Ramp(c + new Vector3(-flat, 0, 0), MathF.PI / 2, 25, h, 2 * flat, PhysCol, 2);
        // fill the corners so the rim is continuous
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                L.Block(c.X + sx * (flat + run / 2 + 1), c.Z + sz * (flat + run / 2 + 1), run + 2, run + 2, h, PhysCol);
        L.Sign(c + new Vector3(0, 4.5f, 0), "SKATE BOWL");
        L.Mark("Skate bowl", c + new Vector3(0, 0.1f, 4), 0);
    }

    static void BuildZones(Level L)
    {
        L.AddZone("Zones", ZoneKind.Area, new Vector3(-60, -50, 12), new Vector3(60, 60, 120));
        L.Mark("Zones", new Vector3(0, 0.1f, 15), MathF.PI);

        // acid and healing
        L.AddZone("Acid (20/s)", ZoneKind.Damage, new Vector3(-22, -0.5f, 20), new Vector3(-10, 0.5f, 28), 20);
        L.Sign(new Vector3(-16, 2, 24), "ACID 20/s", 30);
        L.AddZone("Heal pad (30/s)", ZoneKind.Heal, new Vector3(-6, -0.5f, 22), new Vector3(-2, 0.5f, 26), 30);
        L.Sign(new Vector3(-4, 2, 24), "HEAL 30/s", 30);

        // the pit (a hole in the floor) with a balance beam across, and a checkpoint on the far side
        L.AddZone("Pit", ZoneKind.Kill, new Vector3(-6, -40, 44), new Vector3(6, -6, 52));
        L.Box(new Vector3(0, -0.2f, 48), new Vector3(0.6f, 0.4f, 8), HazCol);
        L.Sign(new Vector3(0, 2.5f, 42), "PIT  (beam 0.6 m)", 30);
        L.AddZone("Checkpoint", ZoneKind.Checkpoint, new Vector3(-3, -0.5f, 55), new Vector3(3, 3, 58));
        L.Block(0, 56.5f, 6, 3, 0.05f, MathX.Rgb(0x40E080));
        L.Sign(new Vector3(0, 2.5f, 56.5f), "CHECKPOINT", 30);
        L.Mark("Pit", new Vector3(0, 0.1f, 38), MathF.PI);

        // jump pads in a row, with platforms to land on
        float[] pads = { 3, 6, 10 };
        for (int i = 0; i < pads.Length; i++)
        {
            float x = 14 + i * 7;
            L.JumpPad($"Jump pad {pads[i]:0} m", new Vector3(x, 0, 26), pads[i], new Vector3(x, 0, 33));
            L.Block(x, 33, 4, 4, pads[i], HazCol);
            L.Sign(new Vector3(x, 1.8f, 23.5f), $"PAD {pads[i]:0} m", 30);
        }
        L.Mark("Jump pads", new Vector3(21, 0.1f, 22.5f), MathF.PI);

        // low gravity room with tall platforms
        L.AddZone("Low gravity (30%)", ZoneKind.LowGravity, new Vector3(20, -0.5f, 60), new Vector3(44, 40, 84), 0.3f);
        L.Block(28, 72, 4, 4, 6, HazCol);
        L.Block(36, 76, 4, 4, 11, HazCol);
        L.Sign(new Vector3(32, 3, 58), "LOW GRAVITY", 40);
        L.Mark("Low gravity", new Vector3(32, 0.1f, 62), MathF.PI);

        // updraft column
        L.AddZone("Updraft", ZoneKind.Updraft, new Vector3(-34, -0.5f, 60), new Vector3(-26, 30, 68), 50);
        L.Block(-30, 64, 8, 8, 0.05f, MathX.Rgb(0x80C0FF));
        L.Block(-30, 76, 6, 6, 14, HazCol);
        L.Sign(new Vector3(-30, 2.5f, 58), "UPDRAFT", 40);
        L.Mark("Updraft", new Vector3(-30, 0.1f, 56), MathF.PI);

        // boost lane, heading west
        for (int i = 0; i < 2; i++)
        {
            float x = -8 - i * 24;
            L.AddZone("Boost pad", ZoneKind.Boost, new Vector3(x - 2, -0.5f, 88), new Vector3(x + 2, 0.4f, 92), 28, new Vector3(-1, 0, 0));
        }
        L.Block(-35, 90, 60, 4, 0.03f, MathX.Rgb(0x707880));
        L.Sign(new Vector3(-6, 2.5f, 86), "BOOST LANE", 40);
        L.Mark("Boost lane", new Vector3(0, 0.1f, 90), MathF.PI / 2);
    }
}

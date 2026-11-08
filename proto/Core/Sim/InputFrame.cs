using System;
using System.Numerics;

namespace GermBall.Proto.Sim;

[Flags]
public enum Btn : uint
{
    None = 0,
    Jump = 1 << 0, Roll = 1 << 1, Fire = 1 << 2, Aim = 1 << 3, Punch = 1 << 4, Reload = 1 << 5,
    Weapon1 = 1 << 6, Weapon2 = 1 << 7, Weapon3 = 1 << 8, WeaponNext = 1 << 9, WeaponPrev = 1 << 10,
    // debug and menu
    Help = 1 << 16, Debug = 1 << 17, Respawn = 1 << 18, NextZone = 1 << 19, Noclip = 1 << 20,
    SlowMo = 1 << 21, God = 1 << 22, ShowZones = 1 << 23, Pause = 1 << 24, PrevZone = 1 << 25, Up = 1 << 26, Down = 1 << 27,
}

/// <summary>One frame of input, already mapped from keyboard, mouse or controller by the platform layer.</summary>
public struct InputFrame
{
    /// <summary>Movement stick: X right, Y forward, length up to 1.</summary>
    public Vector2 Move;
    /// <summary>Camera turn this frame in radians: X turns right, Y looks up.</summary>
    public Vector2 Look;
    /// <summary>Buttons held right now.</summary>
    public Btn Held;
    /// <summary>Buttons that went down this frame.</summary>
    public Btn Pressed;
    public bool Has(Btn b) => (Held & b) != 0;
    public bool Hit(Btn b) => (Pressed & b) != 0;
}

using System;
using System.Numerics;

namespace GermBall.Proto.Engine;

/// <summary>Small math helpers on top of System.Numerics (right-handed, Y up, metres, radians).</summary>
public static class MathX
{
    public const float Deg = MathF.PI / 180f;
    public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    public static float Clamp01(float v) => Clamp(v, 0, 1);
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
    public static float FlatLen(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>Moves <paramref name="v"/> toward <paramref name="target"/> by at most <paramref name="maxDelta"/>.</summary>
    public static Vector3 MoveToward(Vector3 v, Vector3 target, float maxDelta)
    {
        var d = target - v; float len = d.Length();
        return len <= maxDelta || len < 1e-6f ? target : v + d / len * maxDelta;
    }

    /// <summary>Forward vector for a yaw (0 = -Z, positive turns left/counter-clockwise seen from above) and pitch (positive looks up).</summary>
    public static Vector3 Forward(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp);
    }
    /// <summary>Right vector on the ground plane for a yaw.</summary>
    public static Vector3 Right(float yaw) => new(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
    /// <summary>The yaw that faces along a horizontal direction.</summary>
    public static float YawOf(Vector3 dir) => MathF.Atan2(-dir.X, -dir.Z);

    public static float AngleWrap(float a)
    {
        while (a > MathF.PI) a -= 2 * MathF.PI;
        while (a < -MathF.PI) a += 2 * MathF.PI;
        return a;
    }
    public static float TurnToward(float from, float to, float maxStep)
    {
        float d = AngleWrap(to - from);
        return from + Clamp(d, -maxStep, maxStep);
    }

    public static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>World matrix: scale, then rotate, then translate (row-vector convention, as System.Numerics uses).</summary>
    public static Matrix4x4 Trs(Vector3 pos, Quaternion rot, Vector3 scale) =>
        Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
    public static Matrix4x4 Ts(Vector3 pos, Vector3 scale) => Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(pos);

    /// <summary>A rotation that turns +Y onto <paramref name="dir"/> (for cylinders drawn between two points).</summary>
    public static Quaternion FromUpTo(Vector3 dir)
    {
        dir = Vector3.Normalize(dir);
        float d = Vector3.Dot(Vector3.UnitY, dir);
        if (d > 0.99999f) return Quaternion.Identity;
        if (d < -0.99999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(d));
    }

    public static Vector4 Rgb(uint hex, float a = 1) => new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, a);
}

/// <summary>A small deterministic RNG (xorshift), so tests and replays are repeatable.</summary>
public sealed class Rng
{
    uint s;
    public Rng(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }
    public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
    public float Float() => (Next() >> 8) / 16777216f;
    public float Range(float a, float b) => a + (b - a) * Float();
    public Vector3 InSphere() { Vector3 v; do v = new Vector3(Range(-1, 1), Range(-1, 1), Range(-1, 1)); while (v.LengthSquared() > 1); return v; }
}

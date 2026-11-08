using System;
using System.Numerics;
using GermBall.Engine;

namespace GermBall.Sim;

public enum GermKind { Coccus, Bacillus, Brute, Titan }

public sealed class GermSpawn { public Vector3 Pos; public float Yaw; public GermKind Kind; }

/// <summary>Per-kind numbers, from 1.0's germ types (scale, toughness, how many jump dashes floor them).</summary>
public sealed class GermType
{
    public string Name; public float Scale, Hp, ContactDamage; public int DashesToFloor; public Vector4 Color, Dark;
    public static GermType Of(GermKind k) => k switch
    {
        GermKind.Coccus => Coccus, GermKind.Bacillus => Bacillus, GermKind.Brute => Brute, _ => Titan,
    };
    public static readonly GermType Coccus = new() { Name = "coccus", Scale = 0.92f, Hp = 45, ContactDamage = 8, DashesToFloor = 1, Color = MathX.Rgb(0xC060D8), Dark = MathX.Rgb(0x6A2A80) };
    public static readonly GermType Bacillus = new() { Name = "bacillus", Scale = 1.2f, Hp = 100, ContactDamage = 12, DashesToFloor = 1, Color = MathX.Rgb(0x70C840), Dark = MathX.Rgb(0x2E6A1E) };
    public static readonly GermType Brute = new() { Name = "amoeba brute", Scale = 1.68f, Hp = 320, ContactDamage = 20, DashesToFloor = 2, Color = MathX.Rgb(0xE88A30), Dark = MathX.Rgb(0x8A4410) };
    public static readonly GermType Titan = new() { Name = "phage titan", Scale = 2.7f, Hp = 900, ContactDamage = 30, DashesToFloor = 4, Color = MathX.Rgb(0xA8B4C4), Dark = MathX.Rgb(0x505C6C) };
}

/// <summary>
/// A static germ: no AI. It stands where it spawned and sways, hurts you if you walk into it, and takes damage from shots,
/// punches, blasts, pounds, fast rolls and jump dashes. Enough hits throw it into a ragdoll; it gets back up if it survives,
/// and a dead one is replaced a few seconds later.
/// </summary>
public sealed class Germ
{
    public readonly GermSpawn Spawn; public readonly GermType Type; public readonly Rig Rig;
    public Vector3 Pos; public float Yaw, Hp;
    public bool Alive = true;
    public Ragdoll Ragdoll; public float RagdollT;   // time left on the ground before getting up
    public float GetUpT;                            // > 0: blending from the ragdoll back to standing
    public float DeadT, HitFlash, StaggerT, HitCooldown;
    public int DashHits; public float LastDashHit = -99;
    public readonly Pose Pose = new();
    readonly Pose getUpFrom = new(), stand = new();
    readonly float seed;

    public Germ(GermSpawn s)
    {
        Spawn = s; Type = GermType.Of(s.Kind); Rig = new Rig(Type.Scale);
        seed = (s.Pos.X * 12.9898f + s.Pos.Z * 78.233f) % 6.28f;
        Reset();
    }

    public void Reset()
    {
        Pos = Spawn.Pos; Yaw = Spawn.Yaw; Hp = Type.Hp; Alive = true; Ragdoll = null; DeadT = 0; DashHits = 0; StaggerT = 0; GetUpT = 0;
        Rig.Standing(Pose, Pos, Yaw, seed, 0.06f);
    }

    public bool Standing => Alive && Ragdoll == null;
    public float Radius => 0.42f * Type.Scale;
    public float Height => 1.8f * Type.Scale;
    /// <summary>The middle of the body, wherever it is.</summary>
    public Vector3 Center => Ragdoll != null ? Ragdoll.Center : Pos + new Vector3(0, Height * 0.5f, 0);

    public void Update(Game g, float dt)
    {
        HitFlash -= dt; StaggerT -= dt; HitCooldown -= dt;
        if (Ragdoll != null)
        {
            Ragdoll.Step(g.Level.World, dt, Player.Gravity);
            Pose.CopyFrom(Ragdoll.Pos);
            if (!Ragdoll.Finite() || Ragdoll.Center.Y < g.Level.KillY) { Kill(g, Vector3.Zero, false); Ragdoll = null; DeadT = 0.01f; }
            if (Alive)
            {
                RagdollT -= dt;
                if (RagdollT <= 0 && Ragdoll != null) GetUp(g);
            }
        }
        if (!Alive)
        {
            DeadT -= dt;
            if (DeadT <= 0) { Reset(); g.Effects.Burst(Pos + new Vector3(0, Height * 0.5f, 0), Type.Color, 16, 3); }
            return;
        }
        if (Ragdoll != null) return;
        float t = g.Time + seed;
        Rig.Standing(stand, Pos, Yaw, t, StaggerT > 0 ? 0.25f : 0.06f, armsUp: 0.35f + 0.15f * MathF.Sin(t * 1.7f));
        if (GetUpT > 0)
        {
            GetUpT -= dt;
            Pose.Lerp(stand, getUpFrom, MathX.Clamp01(GetUpT / 0.45f));
        }
        else Pose.CopyFrom(stand);
    }

    /// <summary>Knocks it over with this velocity; it lies there for <paramref name="time"/> seconds.</summary>
    public void Topple(Vector3 velocity, float time)
    {
        if (Ragdoll == null)
        {
            var spin = new Vector3(velocity.Z, 0, -velocity.X) * 0.25f;
            Ragdoll = new Ragdoll(Pose, velocity, Type.Scale, spin);
        }
        else Ragdoll.Push(velocity * 0.5f, Game.DT);
        RagdollT = MathF.Max(RagdollT, time);
    }

    void GetUp(Game g)
    {
        // stand where the pelvis came to rest, on whatever floor is below it
        var c = Ragdoll.Center;
        var hit = g.Level.World.Raycast(c + new Vector3(0, 0.5f, 0), -Vector3.UnitY, 6);
        Pos = hit.Hit ? hit.Point : new Vector3(c.X, Spawn.Pos.Y, c.Z);
        getUpFrom.CopyFrom(Ragdoll.Pos);
        Ragdoll = null; GetUpT = 0.45f; DashHits = 0;
    }

    public void Kill(Game g, Vector3 fling, bool topple = true)
    {
        if (!Alive) return;
        Alive = false; Hp = 0; DeadT = 6;
        if (topple) Topple(fling + new Vector3(0, 3, 0), 99);
        g.Effects.Burst(Center, Type.Color, 24, 6);
    }

    /// <summary>Is the point (with a radius) touching the standing body?</summary>
    public bool Touches(Vector3 p, float r, out Vector3 normal)
    {
        normal = default;
        if (!Standing || p.Y + r < Pos.Y || p.Y - r > Pos.Y + Height) return false;
        var d = MathX.Flat(p - Pos); float dist = d.Length();
        if (dist >= Radius + r) return false;
        normal = dist > 1e-4f ? d / dist : Vector3.UnitX;
        return true;
    }
}

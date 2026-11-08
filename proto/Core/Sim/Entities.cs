using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Proto.Engine;

namespace GermBall.Proto.Sim;

/// <summary>A target dummy: an upright cylinder with a head. No AI: it stands still or slides along a rail, wobbles when hit,
/// and comes back three seconds after it's destroyed.</summary>
public sealed class Dummy
{
    public DummySpawn Spawn;
    public Vector3 Pos, PrevPos;
    public float Hp, MaxHp, Radius, Height, HeadSize, RespawnT, HitFlash, RailT;
    public int RailDir = 1;
    public bool Alive = true;
    public Vector2 Wobble, WobbleVel;   // tilt (x, z) in radians-ish
    public Vector4 BaseColor;

    public Dummy(DummySpawn s)
    {
        Spawn = s; Pos = PrevPos = s.Pos;
        bool heavy = s.Kind == DummyKind.Heavy;
        MaxHp = Hp = heavy ? 400 : 100; Radius = heavy ? 0.7f : 0.42f; Height = heavy ? 2.4f : 1.85f; HeadSize = heavy ? 0.6f : 0.45f;
        BaseColor = heavy ? MathX.Rgb(0x8A5A40) : MathX.Rgb(0xD8B070);
    }

    public void Update(float dt)
    {
        PrevPos = Pos;
        HitFlash -= dt;
        // spring back upright
        WobbleVel += (-Wobble * 60 - WobbleVel * 6) * dt;
        Wobble += WobbleVel * dt;
        if (!Alive)
        {
            RespawnT -= dt;
            if (RespawnT <= 0) { Alive = true; Hp = MaxHp; Wobble = WobbleVel = Vector2.Zero; }
        }
        if (Spawn.Speed > 0)
        {
            float len = Vector3.Distance(Spawn.Pos, Spawn.RailEnd);
            RailT += RailDir * Spawn.Speed * dt / len;
            if (RailT > 1) { RailT = 1; RailDir = -1; }
            if (RailT < 0) { RailT = 0; RailDir = 1; }
            Pos = Vector3.Lerp(Spawn.Pos, Spawn.RailEnd, RailT);
        }
    }
}

/// <summary>A physics ball: gravity, bounce, rolling friction, and pushes from the player, other balls, shots and blasts.</summary>
public sealed class Prop
{
    public PropSpawn Spawn;
    public Vector3 Pos, PrevPos, Vel; public float Radius, Mass; public Vector4 Color;
    public Quaternion Rot = Quaternion.Identity, PrevRot = Quaternion.Identity;
    public bool Grounded;
    readonly List<Contact> contacts = new();

    public Prop(PropSpawn s) { Spawn = s; Radius = s.Radius; Color = s.Color; Mass = MathF.Pow(Radius / 0.5f, 3) * 8; Reset(); }
    public void Reset() { Pos = PrevPos = Spawn.Pos; Vel = Vector3.Zero; Rot = PrevRot = Quaternion.Identity; }

    public void Update(Level level, float dt)
    {
        PrevPos = Pos; PrevRot = Rot;
        Vel.Y -= Player.Gravity * dt;
        foreach (var z in level.Zones)
        {
            if (!z.Contains(Pos)) continue;
            if (z.Kind == ZoneKind.LowGravity) Vel.Y += Player.Gravity * (1 - z.Power) * dt;
            if (z.Kind == ZoneKind.Updraft) Vel.Y += z.Power * (1 - MathX.Clamp01((Pos.Y - z.Min.Y) / (z.Max.Y - z.Min.Y))) * dt;
            if (z.Kind == ZoneKind.JumpPad && Vel.Y < 1) Vel = z.Dir;
        }
        float dist = Vel.Length() * dt; int steps = Math.Clamp((int)MathF.Ceiling(dist / (Radius * 0.5f)), 1, 12);
        Grounded = false;
        for (int i = 0; i < steps; i++)
        {
            Pos += Vel * (dt / steps);
            contacts.Clear();
            level.World.ResolveSphere(ref Pos, Radius, contacts, 3);
            foreach (var c in contacts)
            {
                float vn = Vector3.Dot(Vel, c.Normal);
                if (vn < 0) Vel -= c.Normal * vn * (vn < -2 ? 1.4f : 1f);   // bounce off hard hits, settle on soft ones
                if (c.Normal.Y > 0.6f) Grounded = true;
            }
        }
        if (Grounded)
        {
            // rolling resistance
            float sp = Vel.Length();
            if (sp > 0) Vel *= MathF.Max(0, sp - 1.2f * dt) / sp;
            var axis = Vector3.Cross(Vector3.UnitY, Vel); float len = axis.Length();
            if (len > 1e-4f) Rot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis / len, len / Radius * dt) * Rot);
        }
        if (Pos.Y < level.KillY || !MathX.Finite(Pos) || !MathX.Finite(Vel)) Reset();
    }
}

public sealed class Pickup
{
    public PickupSpawn Spawn; public bool Active = true; public float RespawnT, Spin;
    public Pickup(PickupSpawn s) { Spawn = s; }
    public Vector3 Pos => Spawn.Pos;
}

public sealed class Projectile
{
    public Vector3 Pos, PrevPos, Vel; public WeaponDef Def; public float Life; public bool Dead;
    public const float Radius = 0.14f;
}

/// <summary>Short-lived visuals: particles, tracers, flashes and floating damage numbers.</summary>
public sealed class Effects
{
    public struct Particle { public Vector3 Pos, Vel; public float Life, MaxLife, Size; public Vector4 Color; }
    public struct TracerFx { public Vector3 A, B; public float Life; public Vector4 Color; }
    public struct FlashFx { public Vector3 Pos; public float Radius, Life, MaxLife; public Vector4 Color; }
    public struct NumberFx { public Vector3 Pos; public string Text; public float Life; public bool Crit; }

    public readonly List<Particle> Particles = new();
    public readonly List<TracerFx> Tracers = new();
    public readonly List<FlashFx> Flashes = new();
    public readonly List<NumberFx> Numbers = new();
    readonly Rng rng = new(7);
    public const int MaxParticles = 600;

    public void Burst(Vector3 at, Vector4 color, int count, float speed)
    {
        for (int i = 0; i < count && Particles.Count < MaxParticles; i++)
        {
            float life = rng.Range(0.3f, 0.8f);
            Particles.Add(new Particle { Pos = at, Vel = rng.InSphere() * speed + new Vector3(0, speed * 0.4f, 0), Life = life, MaxLife = life, Size = rng.Range(0.06f, 0.14f), Color = color });
        }
    }
    public void Tracer(Vector3 a, Vector3 b, Vector4 c) => Tracers.Add(new TracerFx { A = a, B = b, Life = 0.06f, Color = c });
    public void Flash(Vector3 at, float r, Vector4 c, float life) => Flashes.Add(new FlashFx { Pos = at, Radius = r, Life = life, MaxLife = life, Color = c });
    public void Number(Vector3 at, float dmg, bool crit) => Numbers.Add(new NumberFx { Pos = at, Text = MathF.Round(dmg).ToString("0"), Life = 0.9f, Crit = crit });

    public void Update(float dt)
    {
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i]; p.Life -= dt; p.Vel.Y -= 14 * dt; p.Pos += p.Vel * dt;
            if (p.Pos.Y < 0.03f && p.Vel.Y < 0) { p.Pos.Y = 0.03f; p.Vel *= new Vector3(0.6f, -0.3f, 0.6f); }
            if (p.Life <= 0) Particles.RemoveAt(i); else Particles[i] = p;
        }
        for (int i = Tracers.Count - 1; i >= 0; i--) { var t = Tracers[i]; t.Life -= dt; if (t.Life <= 0) Tracers.RemoveAt(i); else Tracers[i] = t; }
        for (int i = Flashes.Count - 1; i >= 0; i--) { var f = Flashes[i]; f.Life -= dt; if (f.Life <= 0) Flashes.RemoveAt(i); else Flashes[i] = f; }
        for (int i = Numbers.Count - 1; i >= 0; i--) { var n = Numbers[i]; n.Life -= dt; n.Pos.Y += 1.2f * dt; if (n.Life <= 0) Numbers.RemoveAt(i); else Numbers[i] = n; }
    }

    public void Clear() { Particles.Clear(); Tracers.Clear(); Flashes.Clear(); Numbers.Clear(); }
}

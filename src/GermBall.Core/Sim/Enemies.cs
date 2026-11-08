using System;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;

namespace GermBall.Sim;

/// <summary>Where a germ stands, which way it faces, and which entity it is (content/entities/&lt;Entity&gt;.entity.json).</summary>
public sealed class GermSpawn { public Vector3 Pos; public float Yaw; public string Entity = "coccus"; }

/// <summary>
/// A static germ: no AI. It stands where it spawned and sways, hurts you if you walk into it, and takes damage from shots,
/// punches, blasts, pounds, fast rolls and jump dashes. Enough hits throw it into a ragdoll; it gets back up if it survives,
/// and a dead one is replaced a few seconds later. Its look, rig, animation and numbers come from its entity definition.
/// </summary>
public sealed class Germ
{
    public readonly GermSpawn Spawn; public readonly EntityDef Def; public readonly EntityView View;
    public Vector3 Pos; public float Yaw, Hp;
    public bool Alive = true;
    public Ragdoll Ragdoll; public float RagdollT;   // time left on the ground before getting up
    public float GetUpT;                            // > 0: blending from the ragdoll back to standing
    public float DeadT, HitFlash, StaggerT, HitCooldown;
    public int DashHits; public float LastDashHit = -99;
    /// <summary>Health left in each limb of <see cref="EntityDef.Limbs"/>, and which are gone.</summary>
    public float[] LimbHp = Array.Empty<float>(); public bool[] LimbLost = Array.Empty<bool>();
    public int LimbsLost;
    /// <summary>Lost a leg: it can't get back up.</summary>
    public bool Legless;
    /// <summary>World-space humanoid joints of the current pose (for ragdolls and hits).</summary>
    public readonly Pose Pose = new();
    readonly Pose getUpFrom = new(), blend = new();
    readonly AnimContext ctx = new();
    readonly float seed;

    public Germ(GermSpawn s, EntityDef def)
    {
        Spawn = s; Def = def; View = new EntityView(def);
        seed = MathF.Abs(s.Pos.X * 12.9898f + s.Pos.Z * 78.233f) % 6.28f;
        Reset();
    }

    public string Name => Def.Name;
    public float Scale => Def.Stats.Scale;
    public int DashesToFloor => Math.Max(1, Def.Stats.DashesToFloor);
    public float MaxHp => Def.Stats.Hp;
    public bool CanRagdoll => View.Sk.Humanoid;

    public void Reset()
    {
        Pos = Spawn.Pos; Yaw = Spawn.Yaw; Hp = MaxHp; Alive = true; Ragdoll = null; DeadT = 0; DashHits = 0; StaggerT = 0; GetUpT = 0;
        int n = Def.Limbs?.Parts.Count ?? 0;
        LimbHp = new float[n]; LimbLost = new bool[n]; LimbsLost = 0; Legless = false; View.HiddenBones.Clear();
        for (int i = 0; i < n; i++) LimbHp[i] = Def.Limbs.Parts[i].Hp;
        Animate(0, 0);
    }

    public bool Standing => Alive && Ragdoll == null;
    public float Radius => Def.Stats.Radius * Scale;
    public float Height => Def.Stats.Height * Scale;
    /// <summary>The middle of the body, wherever it is.</summary>
    public Vector3 Center => Ragdoll != null ? Ragdoll.Center : Pos + new Vector3(0, Height * 0.5f, 0);

    public void Update(Game g, float dt)
    {
        HitFlash -= dt; StaggerT -= dt; HitCooldown -= dt;
        if (Ragdoll != null)
        {
            Ragdoll.Step(g.Level.World, dt, Player.Gravity);
            if (!Ragdoll.Finite() || Ragdoll.Center.Y < g.Level.KillY) { Ragdoll = null; Alive = false; Hp = 0; DeadT = 0.01f; }
            else if (Alive)
            {
                RagdollT -= dt;
                if (RagdollT <= 0 && !Legless) GetUp(g);
            }
        }
        if (!Alive && Ragdoll == null)
        {
            DeadT -= dt;
            if (DeadT <= 0) { Reset(); g.Effects.Burst(Pos + new Vector3(0, Height * 0.5f, 0), ColorHex.Parse(Def.BallColor), 16, 3); }
            return;
        }
        if (!Alive) { DeadT -= dt; if (DeadT <= 0) { Reset(); return; } }
        ctx.GroundAt ??= q => { var h = g.Level.World.Raycast(q + new Vector3(0, 0.6f, 0), -Vector3.UnitY, 1.2f); return h.Hit && h.Normal.Y > 0.5f ? h.Point.Y : float.NaN; };
        Animate(g.Time + seed, dt);
    }

    /// <summary>Evaluates the entity's layer stack: standing (with its idle), on the ground (ragdoll), or getting up.</summary>
    void Animate(float t, float dt)
    {
        ctx.Time = t; ctx.Dt = dt; ctx.Feet = Pos; ctx.Yaw = Yaw; ctx.Grounded = true; ctx.Speed = 0;
        ctx.Sway = StaggerT > 0 ? 0.25f : 0.06f; ctx.ArmsUp = 0.35f + 0.15f * MathF.Sin(t * 1.7f);
        ctx.State = !Alive ? "dead" : Ragdoll != null ? "down" : StaggerT > 0 ? "stagger" : HitFlash > 0 ? "hurt" : "idle";
        ctx.Move = "idle";
        ctx.Joints = null;
        View.Update(ctx);
        if (Ragdoll != null) ctx.Joints = Ragdoll.Pos;
        else if (GetUpT > 0 && CanRagdoll)
        {
            // stand up: the standing pose, blended in from where the ragdoll lay
            GetUpT -= dt;
            View.Evaluate(ctx);
            View.Anim.WorldJoints(View.Pose, blend);
            blend.Lerp(blend, getUpFrom, MathX.Clamp01(GetUpT / 0.45f));
            ctx.Joints = blend;
        }
        View.Evaluate(ctx);
        if (CanRagdoll) View.Anim.WorldJoints(View.Pose, Pose);
    }

    /// <summary>Knocks it over with this velocity; it lies there for <paramref name="time"/> seconds.
    /// Entities without a humanoid rig can't ragdoll, so they only stagger.</summary>
    public void Topple(Vector3 velocity, float time)
    {
        if (!CanRagdoll) { StaggerT = MathF.Max(StaggerT, 0.6f); return; }
        if (Ragdoll == null)
        {
            var spin = new Vector3(velocity.Z, 0, -velocity.X) * 0.25f;
            Ragdoll = new Ragdoll(Pose, velocity, Scale, spin);
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

    public void Kill(Game g, Vector3 fling)
    {
        if (!Alive) return;
        Alive = false; Hp = 0; DeadT = 6;
        Topple(fling + new Vector3(0, 3, 0), 99);
        g.Effects.Burst(Center, ColorHex.Parse(Def.BallColor), 24, 6);
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

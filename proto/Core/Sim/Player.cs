using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Proto.Engine;

namespace GermBall.Proto.Sim;

/// <summary>
/// The hero's body and movement. <see cref="Pos"/> is the point between the feet.
/// <para>Walking uses a floating capsule: two spheres whose bottom sits <see cref="StepHeight"/> above the feet, so steps and
/// slopes never snag the body. Five short rays down from the hips find the ground and keep the feet on it. Body contacts only
/// push sideways or down; the rays alone decide what is floor.</para>
/// <para>Rolled up, the hero is one sphere of <see cref="BallRadius"/> that rolls with real slope gravity and momentum.</para>
/// </summary>
public sealed class Player
{
    // ---- tuning (metres, seconds)
    public const float BallRadius = 0.5f, BodyRadius = 0.4f, Height = 1.8f;
    public const float Gravity = 22f, StepHeight = 0.45f, MaxSlopeDeg = 52f, SnapDistance = 0.4f;
    public const float WalkSpeed = 7f, AimWalkSpeed = 4.5f, GroundAccel = 60f, AirAccel = 20f;
    public const float JumpSpeed = 8.5f, CoyoteTime = 0.1f, JumpBuffer = 0.12f;
    public const float DashSpeed = 16f, DashTime = 0.18f, DashLift = 2.5f;
    public const float RollAccel = 16f, RollFriction = 1.5f, RollMaxSpeed = 24f, RollBrake = 10f;
    public const float PoundSpeed = 32f;
    public const float MaxHp = 100, MaxShield = 50, ShieldDelay = 3, ShieldRegen = 25;
    public static readonly float WalkableCos = MathF.Cos(MaxSlopeDeg * MathX.Deg);
    static readonly Vector3 LowSphere = new(0, StepHeight + BodyRadius, 0), HighSphere = new(0, Height - BodyRadius, 0);

    // ---- state
    public Vector3 Pos, PrevPos, Vel;
    public float Yaw, PrevYaw;
    public bool Grounded; public Vector3 GroundNormal = Vector3.UnitY;
    public bool Rolling; public Quaternion BallRot = Quaternion.Identity, PrevBallRot = Quaternion.Identity;
    public float Hp = MaxHp, Shield = MaxShield, LastHurt = -99;
    public bool Alive = true; public float DeadT;
    public bool Noclip, God;
    public float CoyoteT, JumpBufferT, DashT; public bool DashUsed, Pounding;
    public float FacingLockT;   // > 0: face the camera (just fired or punched)
    public float PadCooldown;
    // stats for the debug overlay
    public float LastLandSpeed, JumpPeakY, LastJumpHeight, JumpStartY; public bool InAir;
    public string LastEvent = "";

    readonly List<Contact> contacts = new();

    public Vector3 BallCenter => Pos + new Vector3(0, BallRadius, 0);
    /// <summary>Where shots and punches come from.</summary>
    public Vector3 Chest => Pos + new Vector3(0, Rolling ? BallRadius : 1.25f, 0);
    /// <summary>The centre of mass, for explosions and pushes.</summary>
    public Vector3 Center => Pos + new Vector3(0, Rolling ? BallRadius : Height / 2, 0);

    public void Place(Vector3 feet, float yaw)
    {
        Pos = PrevPos = feet; Vel = Vector3.Zero; Yaw = PrevYaw = yaw;
        Grounded = false; DashT = 0; DashUsed = false; Pounding = false; Rolling = false;
        StartAir();
    }

    public void Hurt(float amount, Vector3 knock, Game g, string cause)
    {
        Vel += knock; if (knock.Y > 0.5f) Grounded = false;
        if (!Alive || God || Noclip) return;
        LastHurt = g.Time;
        float s = MathF.Min(Shield, amount); Shield -= s; amount -= s;
        Hp -= amount;
        if (Hp <= 0) Die(cause);
    }
    public void Die(string cause)
    {
        if (!Alive) return;
        Hp = 0; Alive = false; DeadT = 2; LastEvent = "Killed by " + cause; Vel = Vector3.Zero;
    }
    public void Heal(float amount) { if (Alive) Hp = MathF.Min(MaxHp, Hp + amount); }

    // ---------------------------------------------------------------- update
    public void Update(Game g, in InputFrame input, float dt)
    {
        PrevPos = Pos; PrevYaw = Yaw; PrevBallRot = BallRot;
        if (!Alive)
        {
            DeadT -= dt;
            if (DeadT <= 0) g.RespawnPlayer();
            return;
        }
        if (g.Time - LastHurt > ShieldDelay) Shield = MathF.Min(MaxShield, Shield + ShieldRegen * dt);

        var camRight = MathX.Right(g.CamYaw);
        var wish = MathX.Forward(g.CamYaw, 0) * input.Move.Y + camRight * input.Move.X;
        if (wish.LengthSquared() > 1) wish = Vector3.Normalize(wish);

        if (Noclip)
        {
            var fly = MathX.Forward(g.CamYaw, g.CamPitch) * input.Move.Y + camRight * input.Move.X;
            if (input.Has(Btn.Jump)) fly.Y += 1;
            if (input.Has(Btn.Roll)) fly.Y -= 1;
            Vel = fly * (input.Has(Btn.Aim) ? 6 : 22);
            Pos += Vel * dt; Grounded = false; Yaw = g.CamYaw; Rolling = false;
            return;
        }

        // ---- roll in and out (stay rolled while there's no headroom)
        if (input.Has(Btn.Roll)) Rolling = true;
        else if (Rolling && !Pounding && !BodyBlocked(g, Pos)) Rolling = false;

        // ---- zones that act on the body
        float gravScale = 1; bool lifted = false;
        PadCooldown -= dt;
        foreach (var z in g.Level.Zones)
        {
            if (!z.Contains(Pos + new Vector3(0, 0.05f, 0))) continue;
            switch (z.Kind)
            {
                case ZoneKind.LowGravity: gravScale = MathF.Min(gravScale, z.Power); break;
                case ZoneKind.Updraft:
                    {
                        float k = 1 - MathX.Clamp01((Pos.Y - z.Min.Y) / (z.Max.Y - z.Min.Y));
                        Vel.Y += z.Power * k * dt;
                        if (z.Power * k > Gravity * gravScale && Vel.Y > 0) lifted = true;
                        break;
                    }
                case ZoneKind.JumpPad:
                    if (PadCooldown <= 0)
                    {
                        Vel = z.Dir; lifted = true; DashUsed = false; Pounding = false; DashT = 0; PadCooldown = 0.4f;
                        StartAir(); g.Effects.Burst(Pos, MathX.Rgb(0xFFA040), 14, 6);
                        LastEvent = z.Name;
                    }
                    break;
                case ZoneKind.Boost:
                    {
                        var d = Vector3.Normalize(z.Dir); float along = Vector3.Dot(Vel, d);
                        if (along < z.Power) Vel += d * (z.Power - along);
                        break;
                    }
            }
        }
        if (lifted) Grounded = false;

        // ---- jump, air dash, ground pound
        if (input.Hit(Btn.Jump)) JumpBufferT = JumpBuffer; else JumpBufferT -= dt;
        CoyoteT = Grounded ? CoyoteTime : CoyoteT - dt;
        FacingLockT -= dt;
        bool jumped = false;
        if (JumpBufferT > 0 && CoyoteT > 0 && !Pounding)
        {
            Vel.Y = JumpSpeed; Grounded = false; CoyoteT = 0; JumpBufferT = 0; jumped = true;
            StartAir();
        }
        else if (input.Hit(Btn.Jump) && !Grounded && CoyoteT <= 0 && !DashUsed && !Pounding)
        {
            var dir = wish.LengthSquared() > 0.01f ? Vector3.Normalize(wish) : MathX.Forward(Yaw, 0);
            Vel = dir * MathF.Max(DashSpeed, MathX.FlatLen(Vel)) + new Vector3(0, DashLift, 0);
            DashT = DashTime; DashUsed = true; JumpBufferT = 0;
            g.Effects.Burst(Center, MathX.Rgb(0x80E0FF), 10, 4);
        }
        if (input.Hit(Btn.Punch) && Rolling && !Grounded && !Pounding)
        {
            Pounding = true; DashT = 0;
        }

        // ---- steering
        if (Pounding) { Vel = new Vector3(0, -PoundSpeed, 0); }
        else if (DashT > 0) { DashT -= dt; }
        else if (Grounded && !Rolling)
        {
            float speed = input.Has(Btn.Aim) ? AimWalkSpeed : WalkSpeed;
            var h = MathX.MoveToward(MathX.Flat(Vel), wish * speed, GroundAccel * dt);
            Vel = h;   // the ground probe keeps the feet on slopes and steps
        }
        else if (Grounded && Rolling)
        {
            var n = GroundNormal;
            var gt = new Vector3(0, -Gravity * gravScale, 0); gt -= n * Vector3.Dot(gt, n);   // gravity along the slope
            var push = wish - n * Vector3.Dot(wish, n);
            Vel += (gt + push * RollAccel) * dt;
            Vel -= n * Vector3.Dot(Vel, n);
            float sp = Vel.Length();
            if (sp > 0.001f)
            {
                bool braking = Vector3.Dot(push, Vel) < -0.2f * sp;
                float decel = (braking ? RollBrake : RollFriction) + (sp > RollMaxSpeed ? (sp - RollMaxSpeed) * 2 : 0);
                Vel *= MathF.Max(0, sp - decel * dt) / sp;
            }
        }
        else if (wish.LengthSquared() > 0.0001f)
        {
            // air control steers, but only adds speed up to walking pace (momentum from pads and boosts is kept)
            var h = MathX.Flat(Vel); float cap = MathF.Max(h.Length(), WalkSpeed);
            h += wish * AirAccel * dt;
            if (h.Length() > cap) h *= cap / h.Length();
            Vel.X = h.X; Vel.Z = h.Z;
        }

        if (!Grounded && DashT <= 0 && !Pounding) Vel.Y -= Gravity * gravScale * dt;
        if (Vel.Y < -60) Vel.Y = -60;

        // ---- move and collide
        bool wasGrounded = Grounded;
        float startY = Pos.Y;
        float dist = Vel.Length() * dt; int steps = Math.Clamp((int)MathF.Ceiling(dist / 0.2f), 1, 16);
        bool ground = false; var gn = Vector3.UnitY;
        for (int i = 0; i < steps; i++)
        {
            Pos += Vel * (dt / steps);
            if (Rolling) CollideBall(g, ref Pos, ref Vel, ref ground, ref gn);
            else CollideBody(g, ref Pos, ref Vel);
        }
        if (!Rolling && Vel.Y <= 0.01f && !lifted)
        {
            // the ground probe: from above the step height (or from where the feet were, if falling fast) down to the snap distance
            float above = MathF.Max(StepHeight + 0.05f, startY - Pos.Y + 0.05f);
            float below = wasGrounded && !jumped ? SnapDistance : 0.02f;
            if (Probe(g, Pos, above, below, out float gy, out var pn))
            {
                Pos.Y = gy; ground = true; gn = pn;
            }
        }

        bool landed = ground && !wasGrounded;
        if (landed) LastLandSpeed = -MathF.Min(0, Vel.Y);
        Grounded = ground && !lifted;
        GroundNormal = Grounded ? gn : Vector3.UnitY;
        if (Grounded)
        {
            DashUsed = false; DashT = 0;
            if (!Rolling) Vel.Y = 0;
            if (InAir) { LastJumpHeight = JumpPeakY - JumpStartY; InAir = false; }
            if (Pounding) { Pounding = false; g.GroundPound(Pos); }
        }
        else
        {
            if (!InAir) StartAir();
            JumpPeakY = MathF.Max(JumpPeakY, Pos.Y);
        }

        // ---- facing
        if (FacingLockT > 0 || input.Has(Btn.Aim)) Yaw = MathX.TurnToward(Yaw, g.CamYaw, 30 * dt);
        else if (wish.LengthSquared() > 0.01f) Yaw = MathX.TurnToward(Yaw, MathX.YawOf(wish), 12 * dt);

        // ---- the ball's spin follows its travel
        if (Rolling)
        {
            var axis = Vector3.Cross(Vector3.UnitY, Vel); float len = axis.Length();
            if (len > 1e-4f) BallRot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis / len, len / BallRadius * dt) * BallRot);
        }

        if (!MathX.Finite(Pos) || !MathX.Finite(Vel)) { LastEvent = "NaN guard: respawned"; g.RespawnPlayer(); }
    }

    void StartAir() { InAir = true; JumpStartY = JumpPeakY = Pos.Y; }

    /// <summary>Would the standing body overlap the level at these feet?</summary>
    public bool BodyBlocked(Game g, Vector3 feet) =>
        g.Level.World.SphereOverlaps(feet + LowSphere, BodyRadius * 0.95f) || g.Level.World.SphereOverlaps(feet + HighSphere, BodyRadius * 0.95f);

    /// <summary>Walking body: pushes out sideways (and down from ceilings) but never up; the probe owns the floor.</summary>
    void CollideBody(Game g, ref Vector3 feet, ref Vector3 vel)
    {
        var world = g.Level.World;
        for (int it = 0; it < 4; it++)
        {
            bool any = false;
            for (int s = 0; s < 2; s++)
            {
                var c = feet + (s == 0 ? LowSphere : HighSphere);
                foreach (var b in world.Boxes)
                {
                    if (!b.SphereContact(c, BodyRadius, out var n, out float depth)) continue;
                    var push = n * (depth + 1e-4f);
                    if (n.Y > 0) { push.Y = 0; n.Y = 0; float l = n.Length(); if (l < 1e-4f) continue; n /= l; }
                    feet += push; c += push; any = true;
                    float vn = Vector3.Dot(vel, n); if (vn < 0) vel -= n * vn;
                }
            }
            if (!any) break;
        }
    }

    /// <summary>Rolled up: one sphere, floor is any contact flatter than the slope limit.</summary>
    void CollideBall(Game g, ref Vector3 feet, ref Vector3 vel, ref bool ground, ref Vector3 gn)
    {
        var c = feet + new Vector3(0, BallRadius, 0);
        contacts.Clear();
        g.Level.World.ResolveSphere(ref c, BallRadius, contacts, 4);
        feet = c - new Vector3(0, BallRadius, 0);
        foreach (var ct in contacts)
        {
            var n = ct.Normal; float vn = Vector3.Dot(vel, n);
            if (vn < 0) vel -= n * vn;
            if (n.Y >= WalkableCos) { if (!ground || n.Y > gn.Y) gn = n; ground = true; }
        }
        // resting on the floor without a fresh contact this substep still counts as ground
        if (!ground && vel.Y <= 0.01f && Probe(g, feet, 0.05f, 0.03f, out _, out var pn, centreOnly: true)) { ground = true; gn = pn; }
    }

    static readonly Vector2[] probeOffsets = { new(0, 0), new(0.3f, 0), new(-0.3f, 0), new(0, 0.3f), new(0, -0.3f) };

    /// <summary>Casts down around the feet. Finds the highest walkable surface between <paramref name="above"/> metres above the feet
    /// and <paramref name="below"/> metres below them (never more than a step above).</summary>
    public bool Probe(Game g, Vector3 feet, float above, float below, out float groundY, out Vector3 normal, bool centreOnly = false)
    {
        groundY = float.MinValue; normal = Vector3.UnitY; bool found = false;
        float maxY = feet.Y + StepHeight + 0.001f, minY = feet.Y - below;
        int n = centreOnly ? 1 : probeOffsets.Length;
        for (int i = 0; i < n; i++)
        {
            var o = new Vector3(feet.X + probeOffsets[i].X, feet.Y + above, feet.Z + probeOffsets[i].Y);
            var hit = g.Level.World.Raycast(o, -Vector3.UnitY, above + below);
            if (!hit.Hit || hit.Normal.Y < WalkableCos) continue;
            float y = hit.Point.Y;
            if (y > maxY || y < minY) continue;
            // the centre ray's slope wins ties, so slopes read true at their edges
            if (!found || y > groundY + 0.01f || (i == 0 && y > groundY - 0.01f)) { if (y > groundY) groundY = y; normal = hit.Normal; found = true; }
        }
        return found;
    }
}

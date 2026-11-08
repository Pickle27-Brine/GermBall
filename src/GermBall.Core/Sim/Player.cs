using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Engine;

namespace GermBall.Sim;

/// <summary>
/// The hero's body and movement. <see cref="Pos"/> is the point between the feet.
/// <para>Walking uses a floating capsule: two spheres whose bottom sits <see cref="StepHeight"/> above the feet, so steps and
/// slopes never snag the body. Five short rays down from the hips find the ground and keep the feet on it. Body contacts only
/// push sideways or down; the rays alone decide what is floor.</para>
/// <para>Curled up (every jump, dash and pound, or rolling on the ground), the hero is one sphere of <see cref="BallRadius"/>
/// with slope gravity and momentum. Running has inertia: speed builds and bleeds off slowly, reversing skids.</para>
/// </summary>
public sealed class Player
{
    // ---- tuning (metres, seconds). Movement numbers are 1.0's (Genesis-style speeds scaled to metres).
    public const float BallRadius = 0.5f, BodyRadius = 0.4f, Height = 1.8f;
    public const float Gravity = 22f, MaxFall = 27f, StepHeight = 0.45f, MaxSlopeDeg = 52f, SnapDistance = 0.4f;
    // running: slow build-up, coasting, skids, wide turns at speed, slopes push back
    public const float TopSpeed = 9.8f, Accel = 3.8f, Friction = 4.7f, SkidDecel = 50f, SkidKick = 0.84f, AimSpeedScale = 0.55f;
    public const float TurnRateSlow = 14f, TurnRateFast = 3.2f, SlopeWalk = 12.6f;
    // jumping: hold for full height, let go early for a hop; every jump curls into a ball
    public const float JumpSpeed = 10.9f, JumpCut = 6.7f, AirAccel = 9.45f, CoyoteTime = 0.1f, JumpBuffer = 0.12f;
    public const float AirDragPerFrame = 31f / 32f;   // near the top of a jump, per 1/60 s
    // rolling: no push on the flat, only steering, braking and slopes
    public const float RollFriction = 2.36f, RollBrake = 12.6f, RollTurn = 1.7f, RollMin = 1.73f, UnrollSpeed = 0.84f, RollCap = 31.8f;
    public const float SlopeRollUp = 7.9f, SlopeRollDown = 31.5f;
    // spin dash: press roll while (nearly) still, tap jump to rev, let go of roll to launch
    public const float SpinDashBase = 20.2f, SpinDashPerRev = 1.26f, MaxRev = 8;
    public const float DashSpeed = 18f, DashLift = 2.2f, DashTime = 0.4f, DashAssist = 0.15f;
    public const float PoundSpeed = 34f, PoundWindup = 0.13f, PoundMinHeight = 0.8f;
    public const float HoverFuelMax = 2.5f;
    public const float MaxHp = 100, MaxShield = 50, ShieldDelay = 3, ShieldRegen = 25;
    public static readonly float WalkableCos = MathF.Cos(MaxSlopeDeg * MathX.Deg);
    static readonly Vector3 LowSphere = new(0, StepHeight + BodyRadius, 0), HighSphere = new(0, Height - BodyRadius, 0);

    // ---- state
    public Vector3 Pos, PrevPos, Vel;
    public float Yaw, PrevYaw;
    public bool Grounded; public Vector3 GroundNormal = Vector3.UnitY;
    /// <summary>Curled into a ball: from a jump, a dash, a pound, or rolling on the ground.</summary>
    public bool Rolling; public Quaternion BallRot = Quaternion.Identity, PrevBallRot = Quaternion.Identity;
    public bool Jumping, JumpRolled, Skidding, Charging, Hovering; public float Rev, HoverFuel = HoverFuelMax;
    public float Hp = MaxHp, Shield = MaxShield, LastHurt = -99;
    public bool Alive = true; public float DeadT;
    public bool Noclip, God;
    public float CoyoteT, JumpBufferT, DashT; public bool DashUsed;
    /// <summary>0 none, 1 winding up in the air, 2 slamming down.</summary>
    public int PoundPhase; public float PoundT;
    public bool Pounding => PoundPhase > 0;
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
        Grounded = false; DashT = 0; DashUsed = false; PoundPhase = 0; Rolling = false; Jumping = false; Charging = false; Hovering = false;
        HoverFuel = HoverFuelMax; Rev = 0;
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
        Hp = 0; Alive = false; DeadT = 2; LastEvent = "Killed by " + cause;   // Vel is kept: the ragdoll starts with it
    }
    /// <summary>Thrown back into the air by a hit that connected (a dash into a germ): curled, with the dash available again.</summary>
    public void Bounce(Vector3 vel)
    {
        Vel = vel; Grounded = false; Rolling = true; Jumping = true; JumpRolled = false;
        DashUsed = false; DashT = 0; PoundPhase = 0; Hovering = false; CoyoteT = 0;
        StartAir();
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
        float m = MathF.Min(1, wish.Length());                  // how hard the stick is pushed
        var wd = m > 0.001f ? wish / wish.Length() : Vector3.Zero;   // which way, on the ground plane
        bool rollHeld = input.Has(Btn.Roll), jumpHeld = input.Has(Btn.Jump), aimHeld = input.Has(Btn.Aim);

        if (Noclip)
        {
            var fly = MathX.Forward(g.CamYaw, g.CamPitch) * input.Move.Y + camRight * input.Move.X;
            if (jumpHeld) fly.Y += 1;
            if (rollHeld) fly.Y -= 1;
            Vel = fly * (aimHeld ? 6 : 22);
            Pos += Vel * dt; Grounded = false; Yaw = g.CamYaw; Rolling = false; Charging = false;
            return;
        }

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
                        Vel = z.Dir; lifted = true; DashUsed = false; PoundPhase = 0; DashT = 0; Charging = false; Jumping = false; PadCooldown = 0.4f;
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

        if (input.Hit(Btn.Jump)) JumpBufferT = JumpBuffer; else JumpBufferT -= dt;
        CoyoteT = Grounded ? CoyoteTime : CoyoteT - dt;
        FacingLockT -= dt; DashT -= dt;
        bool jumped = false, jumpUsed = false;

        // ---- roll button on the ground: curl up, stand up, or (when nearly still) start a spin dash
        if (input.Hit(Btn.Roll) && Grounded && !Charging)
        {
            if (Rolling) TryUncurl(g);
            else if (Vel.Length() >= RollMin) Rolling = true;
            else { Rolling = true; Charging = true; Rev = 0; }
        }
        if (Charging)
        {
            if (!Grounded) Charging = false;
            else if (rollHeld)
            {
                Rev = MathF.Min(MaxRev, Rev * MathF.Pow(31f / 32f, dt * 60) + 9 * dt);
                if (input.Hit(Btn.Jump)) { Rev = MathF.Min(MaxRev, Rev + 2); jumpUsed = true; JumpBufferT = 0; g.Effects.Burst(Pos + new Vector3(0, 0.1f, 0), MathX.Rgb(0xE0E0E0), 3, 2); }
                Vel *= MathF.Exp(-12 * dt);
            }
            else
            {
                // launch along the facing, flattened onto the ground
                var f = MathX.Forward(Yaw, 0); var n = GroundNormal;
                var dir = Vector3.Normalize(f - n * Vector3.Dot(f, n));
                Vel = dir * (SpinDashBase + Rev * SpinDashPerRev);
                Charging = false; Rev = 0; LastEvent = "Spin dash";
                g.Effects.Burst(Pos + new Vector3(0, 0.1f, 0), MathX.Rgb(0xE0E0E0), 12, 5);
            }
        }

        Skidding = false;
        if (Grounded && !Charging)
        {
            var n = GroundNormal;
            if (Rolling) RollOnGround(n, wd, m, rollHeld, g, dt);
            else RunOnGround(n, wd, m, aimHeld, dt);

            if (JumpBufferT > 0 && CoyoteT > 0 && !jumpUsed)
            {
                // jumps leave along the ground normal, and curl into a ball
                Vel += n * JumpSpeed;
                Grounded = false; CoyoteT = 0; JumpBufferT = 0; jumped = true;
                Jumping = true; JumpRolled = Rolling; Rolling = true;
                StartAir();
            }
        }
        else if (!Grounded)
        {
            // a late jump (coyote time) right after running off a ledge
            if (JumpBufferT > 0 && CoyoteT > 0 && !Jumping && PoundPhase == 0)
            {
                Vel.Y = JumpSpeed; CoyoteT = 0; JumpBufferT = 0; jumped = true;
                Jumping = true; JumpRolled = Rolling; Rolling = true;
                StartAir();
            }
            AirControl(g, input, wd, m, jumpHeld, aimHeld, gravScale, dt);
        }

        // ---- move and collide (curled up: one sphere; standing or hovering: the floating capsule)
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
            if (Probe(g, Pos, above, below, out float gy, out var pn)) { Pos.Y = gy; ground = true; gn = pn; }
        }
        if (jumped || lifted) ground = false;

        bool landed = ground && !wasGrounded;
        Grounded = ground;
        GroundNormal = Grounded ? gn : Vector3.UnitY;
        if (landed) Land(g, rollHeld);
        if (Grounded)
        {
            DashUsed = false;
            if (!Rolling) Vel.Y = 0;
        }
        else
        {
            if (!InAir) StartAir();
            JumpPeakY = MathF.Max(JumpPeakY, Pos.Y);
        }

        // ---- facing: locked to the camera, which always sits directly behind the hero (look turns the hero)
        Yaw = g.CamYaw;

        // ---- the ball's spin follows its travel (and whirls in place while revving)
        if (Charging)
            BallRot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(MathX.Right(Yaw), -(22 + Rev * 6) * dt) * BallRot);
        else if (Rolling)
        {
            var axis = Vector3.Cross(Vector3.UnitY, Vel); float len = axis.Length();
            if (len > 1e-4f) BallRot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis / len, len / BallRadius * dt) * BallRot);
        }

        if (!MathX.Finite(Pos) || !MathX.Finite(Vel)) { LastEvent = "NaN guard: respawned"; g.RespawnPlayer(); }
    }

    /// <summary>Running: speed builds slowly, coasts down when you let go, skids when you reverse, and turns wider the faster you go.</summary>
    void RunOnGround(Vector3 n, Vector3 wd, float m, bool aim, float dt)
    {
        var h = MathX.Flat(Vel); float sp = h.Length();
        float slopeSin = MathF.Sqrt(MathF.Max(0, 1 - n.Y * n.Y));
        // slopes slow you going up and speed you going down (not on flat stair treads)
        if (sp > 0.05f || slopeSin > 0.13f || m > 0.1f) h += new Vector3(n.X * n.Y, 0, n.Z * n.Y) * SlopeWalk * dt;
        sp = h.Length();
        if (m > 0.1f)
        {
            if (sp < 0.3f) h = wd * (sp + Accel * dt);
            else
            {
                var dir = h / sp; float c = Vector3.Dot(dir, wd);
                if (c < -0.4f && sp > 1.5f)
                {
                    Skidding = true; sp = MathF.Max(0, sp - SkidDecel * dt);
                    h = sp < 0.05f ? wd * SkidKick : dir * sp;
                }
                else
                {
                    dir = TurnToward(dir, wd, MathX.Lerp(TurnRateSlow, TurnRateFast, MathF.Min(1, sp / TopSpeed)) * m * dt);
                    float cap = TopSpeed * m * (aim ? AimSpeedScale : 1);
                    if (sp < cap) sp = MathF.Min(cap, sp + Accel * dt);
                    else if (aim || sp < TopSpeed * 1.05f) sp = MathF.Max(cap, sp - SkidDecel * 0.2f * dt);
                    h = dir * sp;
                }
            }
        }
        else if (sp > 0) h *= MathF.Max(0, sp - Friction * dt) / sp;
        Vel = h;   // the ground probe keeps the feet on slopes and steps
    }

    /// <summary>Rolling: no push on the flat. Slopes speed you up going down; the stick only steers and brakes.</summary>
    void RollOnGround(Vector3 n, Vector3 wd, float m, bool rollHeld, Game g, float dt)
    {
        Vel -= n * Vector3.Dot(Vel, n);
        var gt = new Vector3(n.X * n.Y, n.Y * n.Y - 1, n.Z * n.Y);   // downhill, scaled by the slope's sine
        float slopeSin = gt.Length();
        Vel += gt * (Vector3.Dot(Vel, gt) >= 0 ? SlopeRollDown : SlopeRollUp) * dt;
        float sp = Vel.Length();
        if (m > 0.1f && sp > 0.5f)
        {
            var w = wd - n * Vector3.Dot(wd, n); if (w.LengthSquared() > 1e-6f) w = Vector3.Normalize(w);
            float c = Vector3.Dot(Vel / sp, w);
            if (c < -0.5f) sp = MathF.Max(0, sp - RollBrake * m * dt);
            else Vel = TurnToward(Vel / sp, w, RollTurn * m * dt) * sp;
            if (sp > 0) Vel = Vector3.Normalize(Vel) * sp;
        }
        sp = Vel.Length();
        if (sp > 0) Vel *= MathF.Max(0, sp - RollFriction * dt) / sp;
        if (Vel.Length() > RollCap) Vel = Vector3.Normalize(Vel) * RollCap;
        if (Vel.Length() < UnrollSpeed && slopeSin < 0.25f && !rollHeld) TryUncurl(g);
    }

    void AirControl(Game g, in InputFrame input, Vector3 wd, float m, bool jumpHeld, bool aimHeld, float gravScale, float dt)
    {
        // air dash: the second jump press
        if (input.Hit(Btn.Jump) && !DashUsed && CoyoteT <= 0 && !Charging && PoundPhase == 0)
        {
            var dir = m > 0.1f ? wd : MathX.Forward(Yaw, 0);
            float along = Vector3.Dot(Vel, dir);
            float vy = MathF.Max(Vel.Y, DashLift), sp = MathF.Max(DashSpeed, along + 5);
            // 1.0's dash assist: bend a little toward the best germ ahead, and toward its height
            var tgt = g.DashTarget(Pos, dir);
            if (tgt != null)
            {
                var e = tgt.Center - (Pos + new Vector3(0, BallRadius, 0)); float hd = MathX.FlatLen(e);
                var to = MathX.Flat(e) / hd;
                dir = Vector3.Normalize(dir + (to - dir) * DashAssist);
                float t = hd / sp, vyI = e.Y / t + 0.5f * Gravity * t;
                vy = MathX.Lerp(vy, MathX.Clamp(vyI, -8, 14), DashAssist);
            }
            Vel = dir * sp; Vel.Y = vy;
            DashUsed = true; DashT = DashTime; Rolling = true; Jumping = true; JumpRolled = false; HoverFuel = 0; Hovering = false;
            Yaw = MathX.YawOf(dir); JumpBufferT = 0;
            g.Effects.Burst(Center, MathX.Rgb(0x80E0FF), 10, 4);
        }
        // ground pound: punch while curled up, high enough off the ground
        if (input.Hit(Btn.Punch) && Rolling && PoundPhase == 0 && !g.Level.World.Raycast(Pos + new Vector3(0, 0.05f, 0), -Vector3.UnitY, PoundMinHeight).Hit)
        {
            PoundPhase = 1; PoundT = PoundWindup; Vel.X *= 0.15f; Vel.Z *= 0.15f; Vel.Y = 3; DashT = 0; Hovering = false;
        }
        if (PoundPhase == 1)
        {
            PoundT -= dt; Vel.Y *= MathF.Exp(-8 * dt);
            if (PoundT <= 0) { PoundPhase = 2; Vel.Y = -PoundSpeed; }
        }
        else if (PoundPhase == 2) { Vel.Y = -PoundSpeed; float k = MathF.Exp(-3 * dt); Vel.X *= k; Vel.Z *= k; }

        // hover: hold aim in the air to uncurl, hang and shoot (until the fuel runs out)
        Hovering = aimHeld && HoverFuel > 0 && !Charging && PoundPhase == 0 && DashT <= 0;
        if (Hovering)
        {
            if (Rolling) TryUncurl(g);
            if (Rolling) Hovering = false;
        }
        if (Hovering)
        {
            HoverFuel -= dt; Vel.Y += (-0.6f - Vel.Y) * MathF.Min(1, dt * 7);
            float k = MathF.Exp(-2.4f * dt); Vel.X *= k; Vel.Z *= k;
        }
        else if (PoundPhase != 2) Vel.Y = MathF.Max(Vel.Y - Gravity * gravScale * dt, -MaxFall);

        // steering: adds speed toward the stick up to running pace, never takes momentum away
        if (m > 0.1f && PoundPhase == 0)
        {
            float along = Vector3.Dot(Vel, wd);
            if (along < TopSpeed)
            {
                float na = MathF.Min(TopSpeed, along + AirAccel * m * (Jumping && JumpRolled ? 0.5f : 1) * dt);
                Vel += wd * (na - along);
            }
        }
        // a little drag around the top of a jump, and a short hop if jump is let go early
        if (Jumping && Vel.Y > 0 && Vel.Y < JumpCut && DashT <= 0) { float k = MathF.Pow(AirDragPerFrame, dt * 60); Vel.X *= k; Vel.Z *= k; }
        if (Jumping && !jumpHeld && Vel.Y > JumpCut && DashT <= 0) Vel.Y = JumpCut;
    }

    void Land(Game g, bool rollHeld)
    {
        LastLandSpeed = -MathF.Min(0, Vel.Y);
        if (InAir) { LastJumpHeight = JumpPeakY - JumpStartY; InAir = false; }
        DashUsed = false; HoverFuel = HoverFuelMax; Hovering = false;
        if (PoundPhase == 2) { PoundPhase = 0; g.GroundPound(Pos); Rolling = rollHeld; if (!Rolling) Rolling = !CanStand(g); }
        PoundPhase = 0;
        if (Jumping)
        {
            Jumping = false;
            // land on your feet, unless roll is held and you're still moving
            bool keep = rollHeld && Vel.Length() >= RollMin;
            if (!keep && Rolling) TryUncurl(g);
        }
    }

    bool CanStand(Game g) => !BodyBlocked(g, Pos);
    void TryUncurl(Game g) { if (CanStand(g)) Rolling = false; }

    /// <summary>Rotates unit vector <paramref name="from"/> toward unit vector <paramref name="to"/> by at most <paramref name="maxAngle"/> radians.</summary>
    static Vector3 TurnToward(Vector3 from, Vector3 to, float maxAngle)
    {
        float c = MathX.Clamp(Vector3.Dot(from, to), -1, 1), ang = MathF.Acos(c);
        if (ang <= maxAngle || ang < 1e-5f) return to;
        var axis = Vector3.Cross(from, to);
        if (axis.LengthSquared() < 1e-10f) axis = Vector3.UnitY;
        return Vector3.Transform(from, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), maxAngle));
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

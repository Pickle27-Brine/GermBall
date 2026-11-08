using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;

namespace GermBall.Sim;

/// <summary>The humanoid hero, the germs, and everything they do to each other: contact damage, fast-roll hits,
/// damaging jump dashes, and ragdolls.</summary>
public sealed partial class Game
{
    public readonly List<Germ> Germs = new();
    /// <summary>Severed limbs lying around.</summary>
    public readonly List<LimbDebris> Debris = new();
    /// <summary>The hero's entity (content/entities/hero.entity.json): model, rig and animation layers.</summary>
    public EntityView Hero;
    /// <summary>The hero's humanoid joints in world space, from the last evaluated pose.</summary>
    public readonly Pose HeroPose = new();
    public readonly AnimContext HeroCtx = new();
    /// <summary>The hero's body after death, until respawn.</summary>
    public Ragdoll PlayerRagdoll;
    public float AttackHitT;   // brief flash on the HUD when an attack connects

    public bool AimHeld => aimHeld;

    // 1.0's numbers for body attacks
    public const float RollAttackSpeed = 7f, DashBounceUp = 11f, DashBounceBack = 4f, DashCountWindow = 8f;

    /// <summary>The standing germ a dash in this direction should lean toward (within 22 m, roughly ahead).</summary>
    public Germ DashTarget(Vector3 from, Vector3 dir)
    {
        Germ best = null; float bs = float.MinValue;
        foreach (var gm in Germs)
        {
            if (!gm.Standing) continue;
            var e = MathX.Flat(gm.Pos - from); float hd = e.Length();
            if (hd < 1.2f || hd > 22) continue;
            float c = Vector3.Dot(e / hd, dir); if (c < 0.8f) continue;
            float sc = c * 2 - hd / 15; if (sc > bs) { bs = sc; best = gm; }
        }
        return best;
    }

    void InitBodies()
    {
        Hero = new EntityView(Store.Entity(HeroName));
        foreach (var s in Level.Germs) Germs.Add(new Germ(s, Store.Entity(s.Entity)));
    }

    /// <summary>What the hero's animation layers need to know this frame.</summary>
    public void FillHeroContext(AnimContext c, Vector3 feet, float yaw, float dt)
    {
        var p = Player; var w = Weapon;
        c.Time = Time; c.Dt = dt; c.Feet = feet; c.Yaw = yaw; c.LegYaw = HeroLegYaw;
        c.Speed = MathX.FlatLen(p.Vel); c.MoveDir = c.Speed > 0.01f ? MathX.Flat(p.Vel) / c.Speed : MathX.Forward(yaw, 0);
        c.Grounded = p.Grounded; c.Rolling = p.Rolling; c.Skidding = p.Skidding; c.Hovering = p.Hovering; c.Charging = p.Charging;
        c.Armed = w != null && p.Alive; c.TwoHanded = w != null && w.Def != WeaponDef.Pistol;
        c.Aiming = aimHeld || p.FacingLockT > 0 || p.Hovering;
        c.AimDir = MathX.Forward(CamYaw, CamPitch);
        c.AimPitch = CamPitch / MathX.Deg; c.VSpeed = p.Vel.Y;
        c.PunchT = MathF.Max(0, PunchT); c.ReloadT = MathF.Max(0, ReloadT);
        c.State = HeroState(); c.Move = HeroMove();
        c.GroundAt = groundAt ??= q => { var h = Level.World.Raycast(q + new Vector3(0, 0.6f, 0), -Vector3.UnitY, 1.2f); return h.Hit && h.Normal.Y > 0.5f ? h.Point.Y : float.NaN; };
        c.Joints = null; c.Spin = Quaternion.Identity;
    }
    Func<Vector3, float> groundAt;

    /// <summary>The hero's movement, underneath any action (state layers fall back to it).</summary>
    public string HeroMove()
    {
        var p = Player;
        if (!p.Alive) return "dead";
        if (p.Charging) return "spin";
        if (p.Rolling) return "curl";
        if (p.Hovering) return "hover";
        if (!p.Grounded) return p.Vel.Y > 0 ? "jump" : "fall";
        if (p.Skidding) return "skid";
        return MathX.FlatLen(p.Vel) > 0.3f ? "run" : legsStepping ? "turn" : "idle";
    }

    /// <summary>The hero's game state, for state animation layers.</summary>
    public string HeroState()
    {
        var p = Player;
        if (!p.Alive) return "dead";
        if (Time - p.LastHurt < 0.3f) return "hurt";
        if (p.Charging) return "spin";
        if (p.Rolling) return "curl";
        if (p.Hovering) return "hover";
        if (PunchT > 0) return "punch";
        if (ReloadT > 0) return "reload";
        if (FireCooldown > 0 && p.FacingLockT > 0.6f) return "fire";
        if (aimHeld) return "aim";
        if (!p.Grounded) return p.Vel.Y > 0 ? "jump" : "fall";
        if (p.Skidding) return "skid";
        return MathX.FlatLen(p.Vel) > 0.3f ? "run" : legsStepping ? "turn" : "idle";
    }

    /// <summary>Which way the hero's hips and legs face. The upper body faces the camera (<see cref="Player.Yaw"/>);
    /// the legs follow behind, and standing still they only step round once the twist gets too big.</summary>
    public float HeroLegYaw = float.NaN;
    bool legsStepping;

    void TurnLegs(float dt)
    {
        var p = Player; var T = Hero.Def.Turn;
        if (!T.Enabled || !p.Alive || p.Rolling || p.Charging || float.IsNaN(HeroLegYaw)) { HeroLegYaw = p.Yaw; legsStepping = false; return; }
        float twist = MathX.AngleWrap(p.Yaw - HeroLegYaw), max = T.MaxTwist * MathX.Deg;
        float speed = MathX.FlatLen(p.Vel), rate;
        float target = p.Yaw;
        if (p.Grounded && speed > 0.3f)
        {
            // running: the legs run the way you're going (within the twist limit; backwards they face forward)
            float off = MathX.AngleWrap(MathX.YawOf(MathX.Flat(p.Vel)) - p.Yaw);
            if (MathF.Abs(off) < 110 * MathX.Deg) target = p.Yaw + Math.Clamp(off, -max, max);
            rate = T.MoveSpeed; legsStepping = false;
        }
        else if (p.Grounded)
        {
            // standing: feet stay planted until the twist is too much, then the legs step round to face forward
            if (MathF.Abs(twist) > max) legsStepping = true;
            if (legsStepping && MathF.Abs(twist) < 3 * MathX.Deg) legsStepping = false;
            rate = legsStepping ? T.StepSpeed : 0;
        }
        else { rate = T.MoveSpeed; legsStepping = false; }
        HeroLegYaw += MathX.AngleWrap(target - HeroLegYaw) * (1 - MathF.Exp(-rate * dt));
        // never twist further than the limit plus a little, however fast the camera turns
        float hard = max + 25 * MathX.Deg;
        twist = MathX.AngleWrap(p.Yaw - HeroLegYaw);
        if (MathF.Abs(twist) > hard) HeroLegYaw = p.Yaw - MathF.CopySign(hard, twist);
        HeroLegYaw = MathX.AngleWrap(HeroLegYaw);
    }
    /// <summary>The legs are stepping round to catch up with the upper body.</summary>
    public bool LegsStepping => legsStepping;

    void TickBodies(float dt)
    {
        TurnLegs(dt);
        FillHeroContext(HeroCtx, Player.Pos, Player.Yaw, dt);
        Hero.Update(HeroCtx);
        foreach (var gm in Germs) gm.Update(this, dt);
        if (Player.Alive && !Player.Noclip) GermContacts();

        if (!Player.Alive && PlayerRagdoll == null)
        {
            // the hero goes limp with whatever speed it had
            if (Hero.Sk.Humanoid)
            {
                FillHeroContext(HeroCtx, Player.Pos, Player.Yaw, 0);
                HeroCtx.Rolling = false; HeroCtx.Grounded = true;
                Hero.Evaluate(HeroCtx); Hero.Anim.WorldJoints(Hero.Pose, HeroPose);
                var v = Player.Vel + new Vector3(0, 2, 0);
                PlayerRagdoll = new Ragdoll(HeroPose, v, Hero.Scale, new Vector3(v.Z, 0, -v.X) * 0.2f + new Vector3(0, 0, 1.5f));
            }
        }
        if (PlayerRagdoll != null) PlayerRagdoll.Step(Level.World, dt, Player.Gravity);
        foreach (var d in Debris) d.Update(Level.World, dt, Player.Gravity);
        Debris.RemoveAll(d => d.Dead || d.Pos.Y < Level.KillY);
        AttackHitT -= dt;
    }

    /// <summary>Touching a standing germ: a jump dash or pound floors it (eventually), a fast roll bowls into it, and anything else hurts you.</summary>
    void GermContacts()
    {
        var p = Player;
        float hs = p.Vel.Length();
        bool dashing = !p.Grounded && p.Rolling && (p.DashT > 0 || p.PoundPhase == 2);
        bool rollAttack = p.Rolling && hs > RollAttackSpeed;
        foreach (var gm in Germs)
        {
            if (!gm.Standing) continue;
            // the player's body as a few spheres
            bool touch = false; Vector3 n = default;
            if (p.Rolling) touch = gm.Touches(p.BallCenter, Player.BallRadius, out n);
            else for (float y = 0.4f; y <= 1.5f && !touch; y += 0.55f) touch = gm.Touches(p.Pos + new Vector3(0, y, 0), Player.BodyRadius, out n);
            if (!touch) continue;
            n = -n;   // from the player toward the germ
            if (dashing) { DashHit(gm, n, hs); continue; }
            if (rollAttack && gm.HitCooldown <= 0) { RollHit(gm, n, hs); continue; }
            // plain contact: shove the player off and hurt them (once in a while)
            var flat = MathX.Flat(p.Pos - gm.Pos); float d = flat.Length(), min = gm.Radius + (p.Rolling ? Player.BallRadius : Player.BodyRadius);
            if (d < min) { var dir = d > 1e-4f ? flat / d : -n; p.Pos += dir * (min - d); float vn = Vector3.Dot(p.Vel, dir); if (vn < 0) p.Vel -= dir * vn; }
            if (gm.HitCooldown <= 0)
            {
                gm.HitCooldown = 0.8f;
                p.Hurt(gm.Def.Stats.ContactDamage, -n * 8 + new Vector3(0, 5, 0), this, gm.Name);
                Effects.Burst(p.Center, ColorHex.Parse(gm.Def.BallColor), 8, 3);
            }
        }
    }

    /// <summary>A jump dash (or ground pound) into a germ. Small germs go down in one, brutes in two, the titan in four;
    /// each hit bounces you up with your dash back, so you can chain into the next one.</summary>
    void DashHit(Germ gm, Vector3 n, float speed)
    {
        var p = Player; int need = gm.DashesToFloor;
        if (Time - gm.LastDashHit > DashCountWindow) gm.DashHits = 0;
        gm.DashHits++; gm.LastDashHit = Time;
        float dmg = (need >= 4 ? 30 : need == 2 ? 40 : 45) * 0.6f * MathF.Max(1, speed / 25);
        var at = gm.Center;
        DamageGerm(gm, dmg, n * 6, at, false);
        if (gm.Alive && gm.Standing)
        {
            if (gm.DashHits >= need)
            {
                gm.DashHits = 0;
                float fling = need >= 4 ? 0.35f : need == 2 ? 0.55f : 0.8f;
                gm.Topple(n * speed * fling + new Vector3(0, 6 + speed * 0.12f, 0), need >= 4 ? 2.2f : 1.6f);
                if (need > 1) Say(need >= 4 ? "THE GIANT GOES DOWN" : "BRUTE FLOORED");
            }
            else { gm.StaggerT = need >= 4 ? 0.35f : 0.6f; if (need > 1) Say($"DASH {gm.DashHits}/{need}"); }
        }
        p.Bounce(-MathX.Flat(n) * DashBounceBack + new Vector3(0, DashBounceUp, 0));
        Effects.Burst(at, MathX.Rgb(0xFFFFFF), 12, 6);
        AttackHitT = 0.15f; HitMarkerT = 0.2f;
    }

    void RollHit(Germ gm, Vector3 n, float speed)
    {
        var p = Player;
        gm.HitCooldown = 0.4f;
        DamageGerm(gm, 10 + (speed - RollAttackSpeed) * 1.6f, n * speed * 0.3f, gm.Center, false);
        if (gm.Alive && gm.Standing)
        {
            if (gm.DashesToFloor == 1) gm.Topple(n * speed * 0.5f + new Vector3(0, 4, 0), 0.8f + MathF.Min(1, speed * 0.04f));
            else gm.StaggerT = 0.5f;
        }
        p.Vel -= MathX.Flat(n) * speed * 0.45f;   // the roller loses some speed into the victim
        Effects.Burst(gm.Center, MathX.Rgb(0xFFFFFF), 6, 4);
        AttackHitT = 0.1f; HitMarkerT = 0.15f;
    }

    /// <summary>Damage a germ from any source. Killing blows (and big pushes on small germs) throw it into a ragdoll.
    /// <paramref name="limbHit"/>: a direct hit (shot or punch) that can wear down the limb it lands on.</summary>
    public void DamageGerm(Germ gm, float dmg, Vector3 push, Vector3 at, bool crit, bool limbHit = false)
    {
        if (!gm.Alive) { gm.Ragdoll?.Push(push * 0.3f, DT); return; }
        if (limbHit && LimbHit(gm, dmg, push, at)) return;
        gm.Hp -= dmg; gm.HitFlash = 0.12f;
        damageLog.Enqueue((Time, dmg));
        Effects.Number(at, dmg, crit);
        HitMarkerT = MathF.Max(HitMarkerT, 0.15f); HitMarkerKill = false;
        if (gm.Ragdoll != null) gm.Ragdoll.PushJoint(gm.Ragdoll.Nearest(at), push * 0.5f, DT);
        if (gm.Hp <= 0)
        {
            gm.Kill(this, push * 1.2f);
            HitMarkerKill = true; HitMarkerT = 0.3f;
        }
    }

    // ---------------------------------------------------------------- limbs
    /// <summary>A direct hit near a limb wears it down; at zero it comes off. Only hits of at least the entity's minimum count,
    /// and only so many limbs can go. Returns true if the hit killed the germ outright (a critical limb).</summary>
    bool LimbHit(Germ gm, float dmg, Vector3 push, Vector3 at)
    {
        var L = gm.Def.Limbs;
        if (L == null || !L.Enabled || L.Parts.Count == 0 || dmg < L.MinHit || gm.LimbsLost >= L.MaxLost) return false;
        int limb = NearestLimb(gm, at); if (limb < 0) return false;
        gm.LimbHp[limb] -= dmg;
        if (gm.LimbHp[limb] > 0) return false;
        return Sever(gm, limb, push);
    }

    /// <summary>The limb (not yet lost) whose bones run closest to a point, within reach of the body's surface.</summary>
    public static int NearestLimb(Germ gm, Vector3 at)
    {
        var v = gm.View; var sk = v.Sk; float best = 0.38f * gm.Scale; int bi = -1;
        // how close the hit is to the rest of the body (bones in no limb): limbs only take hits clearly nearer to them
        float body = float.MaxValue;
        for (int b = 0; b < sk.Count; b++)
        {
            bool inLimb = false;
            foreach (var p in gm.Def.Limbs.Parts) { int lb = sk.IndexOf(p.Bone); if (lb >= 0 && sk.IsDescendant(b, lb)) { inLimb = true; break; } }
            if (inLimb) continue;
            int c = sk.FirstChild(b);
            body = MathF.Min(body, SegmentDistance(at, v.Pose.Joint(b), c >= 0 ? v.Pose.Joint(c) : v.Pose.Joint(b)));
        }
        for (int i = 0; i < gm.Def.Limbs.Parts.Count; i++)
        {
            if (gm.LimbLost[i]) continue;
            int lb = sk.IndexOf(gm.Def.Limbs.Parts[i].Bone); if (lb < 0) continue;
            for (int b = 0; b < sk.Count; b++)
            {
                if (!sk.IsDescendant(b, lb)) continue;
                int c = sk.FirstChild(b);
                var a0 = v.Pose.Joint(b); var a1 = c >= 0 ? v.Pose.Joint(c) : a0;
                float d = SegmentDistance(at, a0, a1);
                if (d < best && d < body * 0.8f) { best = d; bi = i; }
            }
        }
        return bi;
    }
    static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a; float l = ab.LengthSquared(); float t = l < 1e-8f ? 0 : Math.Clamp(Vector3.Dot(p - a, ab) / l, 0, 1);
        return Vector3.Distance(p, a + ab * t);
    }

    /// <summary>Takes a limb off: it flies away as debris, the body stops drawing it; a leg leaves it down for good, a critical limb kills.</summary>
    public bool Sever(Germ gm, int limb, Vector3 push)
    {
        var part = gm.Def.Limbs.Parts[limb]; int lb = gm.View.Sk.IndexOf(part.Bone); if (lb < 0) return false;
        gm.LimbLost[limb] = true; gm.LimbsLost++;
        var vel = push * 0.6f + new Vector3(0, 4, 0) + Rng.InSphere() * 2;
        Debris.Add(new LimbDebris(gm.View, lb, vel, gm.Def.Limbs.DebrisLife));
        gm.View.HiddenBones.Add(lb);
        Effects.Burst(gm.View.Pose.Joint(lb), ColorHex.Parse(gm.Def.BallColor), 18, 4);
        Say($"{gm.Name}: {BoneNames.Friendly(part.Bone).ToLowerInvariant()} off");
        if (part.Critical) { gm.Kill(this, push); HitMarkerKill = true; HitMarkerT = 0.3f; return true; }
        if (part.Bone.StartsWith("hip") || part.Bone.StartsWith("knee") || part.Bone.StartsWith("foot"))
        {
            gm.Legless = true;
            if (gm.Standing) gm.Topple(push * 0.5f + new Vector3(0, 2, 0), 99);
        }
        else gm.StaggerT = MathF.Max(gm.StaggerT, 0.6f);
        return false;
    }

    /// <summary>Shots, punches and blasts: a big enough push knocks small germs over.</summary>
    void HitGerm(Germ gm, float dmg, Vector3 push, Vector3 at, bool crit, bool canTopple, bool limbHit = false)
    {
        DamageGerm(gm, dmg, push, at, crit, limbHit);
        if (canTopple && gm.Alive && gm.Standing && gm.DashesToFloor == 1) gm.Topple(push + new Vector3(0, 3, 0), 1.2f);
        else if (gm.Alive && gm.Standing) gm.StaggerT = MathF.Max(gm.StaggerT, 0.3f);
    }

    RayHit RaycastGerms(Vector3 o, Vector3 d, float limit, RayHit best)
    {
        foreach (var gm in Germs)
        {
            if (gm.Ragdoll != null)
            {
                if (gm.Ragdoll.Raycast(o, d, limit, out float t, out int j) && t < limit)
                { limit = t; best = new RayHit { Hit = true, T = t, Normal = -d, Other = gm }; }
            }
            else if (gm.Alive && Shapes.RayCylinder(o, d, gm.Pos, gm.Radius, gm.Height, limit, out float t, out var n) && t < limit)
            { limit = t; best = new RayHit { Hit = true, T = t, Normal = n, Other = gm }; }
        }
        return best;
    }
}

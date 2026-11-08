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
        c.Time = Time; c.Dt = dt; c.Feet = feet; c.Yaw = yaw;
        c.Speed = MathX.FlatLen(p.Vel); c.MoveDir = c.Speed > 0.01f ? MathX.Flat(p.Vel) / c.Speed : MathX.Forward(yaw, 0);
        c.Grounded = p.Grounded; c.Rolling = p.Rolling; c.Skidding = p.Skidding; c.Hovering = p.Hovering; c.Charging = p.Charging;
        c.Armed = w != null && p.Alive; c.TwoHanded = w != null && w.Def != WeaponDef.Pistol;
        c.Aiming = aimHeld || p.FacingLockT > 0 || p.Hovering;
        c.AimDir = MathX.Forward(CamYaw, CamPitch);
        c.PunchT = MathF.Max(0, PunchT); c.ReloadT = MathF.Max(0, ReloadT);
        c.State = HeroState();
        c.GroundAt = groundAt ??= q => { var h = Level.World.Raycast(q + new Vector3(0, 0.6f, 0), -Vector3.UnitY, 1.2f); return h.Hit && h.Normal.Y > 0.5f ? h.Point.Y : float.NaN; };
        c.Joints = null; c.Spin = Quaternion.Identity;
    }
    Func<Vector3, float> groundAt;

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
        return MathX.FlatLen(p.Vel) > 0.3f ? "run" : "idle";
    }

    void TickBodies(float dt)
    {
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

    /// <summary>Damage a germ from any source. Killing blows (and big pushes on small germs) throw it into a ragdoll.</summary>
    public void DamageGerm(Germ gm, float dmg, Vector3 push, Vector3 at, bool crit)
    {
        if (!gm.Alive) { gm.Ragdoll?.Push(push * 0.3f, DT); return; }
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

    /// <summary>Shots, punches and blasts: a big enough push knocks small germs over.</summary>
    void HitGerm(Germ gm, float dmg, Vector3 push, Vector3 at, bool crit, bool canTopple)
    {
        DamageGerm(gm, dmg, push, at, crit);
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

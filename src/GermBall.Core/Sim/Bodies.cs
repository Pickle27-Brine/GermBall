using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Engine;

namespace GermBall.Sim;

/// <summary>The humanoid hero, the germs, and everything they do to each other: contact damage, fast-roll hits,
/// damaging jump dashes, and ragdolls.</summary>
public sealed partial class Game
{
    public readonly List<Germ> Germs = new();
    public readonly HeroAnim Anim = new();
    public readonly Pose HeroPose = new();
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
        foreach (var s in Level.Germs) Germs.Add(new Germ(s));
    }

    void TickBodies(float dt)
    {
        Anim.Update(Player, dt);
        foreach (var gm in Germs) gm.Update(this, dt);
        if (Player.Alive && !Player.Noclip) GermContacts();

        if (!Player.Alive && PlayerRagdoll == null)
        {
            // the hero goes limp with whatever speed it had
            var pose = new Pose();
            if (Player.Rolling) Anim.Rig.Standing(pose, Player.Pos, Player.Yaw, Time, 0);
            else Anim.Build(pose, this, Player.Pos, Player.Yaw);
            var v = Player.Vel + new Vector3(0, 2, 0);
            PlayerRagdoll = new Ragdoll(pose, v, 1, new Vector3(v.Z, 0, -v.X) * 0.2f + new Vector3(0, 0, 1.5f));
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
                p.Hurt(gm.Type.ContactDamage, -n * 8 + new Vector3(0, 5, 0), this, gm.Type.Name);
                Effects.Burst(p.Center, gm.Type.Color, 8, 3);
            }
        }
    }

    /// <summary>A jump dash (or ground pound) into a germ. Small germs go down in one, brutes in two, the titan in four;
    /// each hit bounces you up with your dash back, so you can chain into the next one.</summary>
    void DashHit(Germ gm, Vector3 n, float speed)
    {
        var p = Player; int need = gm.Type.DashesToFloor;
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
            if (gm.Type.DashesToFloor == 1) gm.Topple(n * speed * 0.5f + new Vector3(0, 4, 0), 0.8f + MathF.Min(1, speed * 0.04f));
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
        if (canTopple && gm.Alive && gm.Standing && gm.Type.DashesToFloor == 1) gm.Topple(push + new Vector3(0, 3, 0), 1.2f);
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

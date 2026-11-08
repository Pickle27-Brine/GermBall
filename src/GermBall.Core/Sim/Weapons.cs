using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;

namespace GermBall.Sim;

public sealed class WeaponDef
{
    public string Name; public bool Auto, Projectile;
    public float Damage, Interval, Reload, Spread, Range = 300, Knock;
    public int Mag, ReserveMax;   // ReserveMax < 0: endless reserve
    public float ProjectileSpeed, BlastRadius;
    public Vector4 Color; public Vector3 Size;

    public static readonly WeaponDef Pistol = new()
    {
        Name = "PISTOL", Damage = 20, Interval = 0.16f, Reload = 1.1f, Spread = 0.004f, Mag = 12, ReserveMax = -1, Knock = 3,
        Color = MathX.Rgb(0x404850), Size = new Vector3(0.1f, 0.16f, 0.32f),
    };
    public static readonly WeaponDef Rifle = new()
    {
        Name = "RIFLE", Auto = true, Damage = 9, Interval = 0.09f, Reload = 1.6f, Spread = 0.012f, Mag = 30, ReserveMax = 180, Knock = 1.5f,
        Color = MathX.Rgb(0x3A4A3A), Size = new Vector3(0.1f, 0.16f, 0.8f),
    };
    public static readonly WeaponDef Lobber = new()
    {
        Name = "LOBBER", Projectile = true, Damage = 70, Interval = 0.7f, Reload = 2f, Spread = 0, Mag = 4, ReserveMax = 16,
        ProjectileSpeed = 26, BlastRadius = 4, Knock = 10,
        Color = MathX.Rgb(0x50803A), Size = new Vector3(0.22f, 0.22f, 0.6f),
    };
}

public sealed class WeaponSlot
{
    public WeaponDef Def; public int Mag, Reserve;
    public WeaponSlot(WeaponDef d) { Def = d; Mag = d.Mag; Reserve = d.ReserveMax < 0 ? 0 : d.Mag * 2; }
    public bool Endless => Def.ReserveMax < 0;
}

/// <summary>Weapons, melee and the damage they deal. Part of the game rather than the player because it touches everything.</summary>
public sealed partial class Game
{
    public readonly List<WeaponSlot> Weapons = new();
    public int WeaponIndex;
    public WeaponSlot Weapon => Weapons.Count > 0 ? Weapons[WeaponIndex] : null;
    public float FireCooldown, ReloadT, PunchCooldown, PunchT, HitMarkerT, SwitchT; public bool HitMarkerKill;
    public int ShotsFired, ShotsHit;
    // damage dealt in the last few seconds, for the DPS readout
    readonly Queue<(float t, float dmg)> damageLog = new();
    public float RecentDps { get { float s = 0; foreach (var e in damageLog) s += e.dmg; return s / DpsWindow; } }
    const float DpsWindow = 5;

    public const float PunchDamage = 35, PunchRange = 1.8f, PunchCooldownTime = 0.45f;

    void UpdateWeapons(in InputFrame input, float dt)
    {
        FireCooldown -= dt; PunchCooldown -= dt; PunchT -= dt; HitMarkerT -= dt; SwitchT -= dt;
        while (damageLog.Count > 0 && damageLog.Peek().t < Time - DpsWindow) damageLog.Dequeue();
        var p = Player;
        if (!p.Alive || p.Noclip) return;

        // switching
        int want = -1;
        if (input.Hit(Btn.Weapon1)) want = 0;
        if (input.Hit(Btn.Weapon2)) want = 1;
        if (input.Hit(Btn.Weapon3)) want = 2;
        if (input.Hit(Btn.WeaponNext)) want = (WeaponIndex + 1) % Weapons.Count;
        if (input.Hit(Btn.WeaponPrev)) want = (WeaponIndex + Weapons.Count - 1) % Weapons.Count;
        if (want >= 0 && want < Weapons.Count && want != WeaponIndex) { WeaponIndex = want; ReloadT = 0; SwitchT = 0.25f; FireCooldown = MathF.Max(FireCooldown, 0.25f); }

        var w = Weapon; if (w == null) return;

        // reloading
        if (ReloadT > 0)
        {
            ReloadT -= dt;
            if (ReloadT <= 0)
            {
                int need = w.Def.Mag - w.Mag, take = w.Endless ? need : Math.Min(need, w.Reserve);
                w.Mag += take; if (!w.Endless) w.Reserve -= take;
            }
        }
        bool canReload = w.Mag < w.Def.Mag && (w.Endless || w.Reserve > 0) && ReloadT <= 0;
        if (input.Hit(Btn.Reload) && canReload) ReloadT = w.Def.Reload;

        // melee (rolled up, punch is the ground pound instead)
        if (input.Hit(Btn.Punch) && !p.Rolling && PunchCooldown <= 0) Punch();

        // firing (not while rolled up)
        bool trigger = w.Def.Auto ? input.Has(Btn.Fire) : input.Hit(Btn.Fire);
        if (trigger && !p.Rolling && FireCooldown <= 0 && ReloadT <= 0 && PunchT <= 0)
        {
            if (w.Mag <= 0) { if (canReload) ReloadT = w.Def.Reload; }
            else
            {
                Fire(w);
                if (w.Mag == 0 && (w.Endless || w.Reserve > 0)) ReloadT = w.Def.Reload;
            }
        }
    }

    public Vector3 Muzzle()
    {
        var p = Player; var fwd = MathX.Forward(p.Yaw, 0);
        return p.Chest + MathX.Right(p.Yaw) * 0.42f + fwd * 0.45f;
    }

    /// <summary>Where the crosshair points: a ray from the camera, starting at the player so things behind them don't count.</summary>
    public Vector3 AimPoint(out RayHit hit)
    {
        var fwd = CamForward;
        float skip = MathF.Max(0, Vector3.Dot(Player.Chest - CamPos, fwd));
        hit = RaycastAll(CamPos + fwd * skip, fwd, 400);
        return hit.Hit ? hit.Point : CamPos + fwd * (skip + 400);
    }

    void Fire(WeaponSlot w)
    {
        var d = w.Def; var p = Player;
        w.Mag--; FireCooldown = d.Interval; ShotsFired++;
        p.FacingLockT = 0.8f; p.Yaw = CamYaw;
        var muzzle = Muzzle();
        var target = AimPoint(out _);
        var dir = Vector3.Normalize(target - muzzle);
        if (d.Spread > 0)
        {
            var r = Rng.InSphere() * d.Spread; dir = Vector3.Normalize(dir + r);
        }
        Effects.Flash(muzzle + dir * 0.2f, 0.18f, MathX.Rgb(0xFFE080), 0.05f);
        if (d.Projectile)
        {
            Projectiles.Add(new Projectile { Pos = muzzle, PrevPos = muzzle, Vel = dir * d.ProjectileSpeed + new Vector3(0, 2, 0), Def = d, Life = 3 });
            return;
        }
        var hit = RaycastAll(muzzle, dir, d.Range);
        var end = hit.Hit ? hit.Point : muzzle + dir * d.Range;
        Effects.Tracer(muzzle, end, MathX.Rgb(0xFFF0A0));
        if (!hit.Hit) return;
        ApplyHit(hit, dir, d.Damage, d.Knock);
    }

    void ApplyHit(RayHit hit, Vector3 dir, float damage, float knock)
    {
        switch (hit.Other)
        {
            case Dummy dm:
                {
                    bool head = hit.Point.Y > dm.Pos.Y + dm.Height - dm.HeadSize;
                    float dmg = head ? damage * 2 : damage;
                    ShotsHit++;
                    DamageDummy(dm, dmg, dir * knock, hit.Point, head);
                    Effects.Burst(hit.Point, head ? MathX.Rgb(0xFFE040) : MathX.Rgb(0xFF8060), 6, 3);
                    break;
                }
            case Germ gm:
                {
                    bool head = gm.Standing && hit.Point.Y > gm.Pos.Y + gm.Height * 0.8f;
                    ShotsHit++;
                    HitGerm(gm, head ? damage * 2 : damage, dir * knock, hit.Point, head, false);
                    Effects.Burst(hit.Point, ColorHex.Parse(gm.Def.BallColor), 6, 3);
                    break;
                }
            case Prop pr:
                pr.Vel += dir * knock * 2.5f / pr.Mass;
                Effects.Burst(hit.Point, pr.Color, 5, 3);
                ShotsHit++;
                break;
            default:
                Effects.Burst(hit.Point + hit.Normal * 0.05f, MathX.Rgb(0xD0D0D0), 4, 2.5f);
                break;
        }
    }

    public void DamageDummy(Dummy dm, float dmg, Vector3 push, Vector3 at, bool crit)
    {
        if (!dm.Alive) return;
        dm.Hp -= dmg; dm.HitFlash = 0.12f;
        dm.WobbleVel += new Vector2(push.X, push.Z) * 0.6f;
        damageLog.Enqueue((Time, dmg));
        Effects.Number(at, dmg, crit);
        HitMarkerT = 0.15f; HitMarkerKill = false;
        if (dm.Hp <= 0)
        {
            dm.Alive = false; dm.RespawnT = 3; HitMarkerKill = true; HitMarkerT = 0.3f;
            Effects.Burst(dm.Pos + new Vector3(0, dm.Height * 0.6f, 0), dm.BaseColor, 30, 7);
        }
    }

    void Punch()
    {
        var p = Player;
        PunchCooldown = PunchCooldownTime; PunchT = 0.2f;
        p.FacingLockT = 0.6f; p.Yaw = CamYaw;
        var fwd = MathX.Forward(CamYaw, 0);
        var at = p.Chest + fwd * (PunchRange * 0.6f);
        bool any = false;
        foreach (var dm in Dummies)
        {
            if (!dm.Alive) continue;
            var c = dm.Pos + new Vector3(0, MathX.Clamp(at.Y - dm.Pos.Y, 0, dm.Height), 0);
            if (Vector3.Distance(c, at) < PunchRange * 0.6f + dm.Radius) { DamageDummy(dm, PunchDamage, fwd * 6, c + (at - c) * 0.5f, false); any = true; }
        }
        foreach (var gm in Germs)
        {
            if (!gm.Alive) continue;
            var c = gm.Ragdoll != null ? gm.Ragdoll.Pos.P[gm.Ragdoll.Nearest(at)] : gm.Pos + new Vector3(0, MathX.Clamp(at.Y - gm.Pos.Y, 0, gm.Height), 0);
            if (Vector3.Distance(c, at) < PunchRange * 0.6f + (gm.Ragdoll != null ? 0.2f : gm.Radius)) { HitGerm(gm, PunchDamage, fwd * 7 + new Vector3(0, 2, 0), c + (at - c) * 0.5f, false, true); any = true; }
        }
        foreach (var pr in Props)
        {
            if (Vector3.Distance(pr.Pos, at) < PunchRange * 0.6f + pr.Radius) { pr.Vel += (fwd * 9 + new Vector3(0, 3, 0)) / MathF.Max(0.6f, pr.Mass); any = true; }
        }
        Effects.Flash(at, any ? 0.5f : 0.3f, MathX.Rgb(0xFFFFFF), 0.08f);
    }

    /// <summary>Damage and push in a radius: dummies, balls and the player (who takes a quarter, and gets a rocket jump).</summary>
    public void Explode(Vector3 at, float radius, float damage, float knock, bool hurtsPlayer)
    {
        Effects.Flash(at, radius, MathX.Rgb(0xFFA040), 0.25f);
        Effects.Burst(at, MathX.Rgb(0xFF8030), 30, 9);
        foreach (var dm in Dummies)
        {
            if (!dm.Alive) continue;
            var c = dm.Pos + new Vector3(0, MathX.Clamp(at.Y - dm.Pos.Y, 0, dm.Height), 0);
            float d = Vector3.Distance(c, at) - dm.Radius;
            if (d >= radius) continue;
            float k = 1 - MathX.Clamp01(d / radius) * 0.6f;
            var dir = MathX.Flat(c - at); if (dir.LengthSquared() > 1e-4f) dir = Vector3.Normalize(dir);
            DamageDummy(dm, damage * k, dir * knock, c, false);
        }
        foreach (var gm in Germs)
        {
            if (!gm.Alive && gm.Ragdoll == null) continue;
            var c = gm.Center; var v = c - at; float d = MathF.Max(0, v.Length() - gm.Radius);
            if (d >= radius) continue;
            float k = 1 - MathX.Clamp01(d / radius);
            var dir = v.LengthSquared() > 1e-4f ? Vector3.Normalize(v) : Vector3.UnitY;
            HitGerm(gm, damage * (1 - (1 - k) * 0.6f), (dir + new Vector3(0, 0.6f, 0)) * knock * k, c, false, k > 0.35f);
        }
        foreach (var pr in Props)
        {
            var v = pr.Pos - at; float d = v.Length() - pr.Radius;
            if (d >= radius) continue;
            var dir = v.LengthSquared() > 1e-4f ? Vector3.Normalize(v) : Vector3.UnitY;
            pr.Vel += (dir + new Vector3(0, 0.5f, 0)) * knock * 1.6f * (1 - MathX.Clamp01(d / radius)) / MathF.Pow(pr.Mass, 0.5f);
        }
        if (hurtsPlayer && Player.Alive)
        {
            var v = Player.Center - at; float d = v.Length();
            if (d < radius + 0.5f)
            {
                float k = 1 - MathX.Clamp01(d / (radius + 0.5f));
                var dir = d > 1e-3f ? v / d : Vector3.UnitY;
                Player.Hurt(damage * 0.25f * k, (dir * 12 + new Vector3(0, 7, 0)) * k, this, "own blast");
            }
        }
    }

    public void GroundPound(Vector3 at)
    {
        Explode(at + new Vector3(0, 0.3f, 0), 4.5f, 40, 9, false);
        Player.LastEvent = "Ground pound";
    }

    /// <summary>One ray against the level, the dummies and the balls.</summary>
    public RayHit RaycastAll(Vector3 o, Vector3 d, float max)
    {
        var best = Level.World.Raycast(o, d, max);
        float limit = best.Hit ? best.T : max;
        foreach (var dm in Dummies)
        {
            if (!dm.Alive) continue;
            if (Shapes.RayCylinder(o, d, dm.Pos, dm.Radius, dm.Height, limit, out float t, out var n) && t < limit)
            { limit = t; best = new RayHit { Hit = true, T = t, Normal = n, Other = dm }; }
        }
        best = RaycastGerms(o, d, limit, best); if (best.Hit) limit = best.T;
        foreach (var pr in Props)
        {
            if (Shapes.RaySphere(o, d, pr.Pos, pr.Radius, limit, out float t) && t < limit)
            { limit = t; var pt = o + d * t; best = new RayHit { Hit = true, T = t, Normal = Vector3.Normalize(pt - pr.Pos), Other = pr }; }
        }
        if (best.Hit) best.Point = o + d * best.T;
        return best;
    }

    public void GiveWeapon(WeaponDef d)
    {
        for (int i = 0; i < Weapons.Count; i++)
            if (Weapons[i].Def == d) { Weapons[i].Reserve = Math.Min(d.ReserveMax, Weapons[i].Reserve + d.Mag * 2); return; }
        Weapons.Add(new WeaponSlot(d));
        WeaponIndex = Weapons.Count - 1; ReloadT = 0; SwitchT = 0.25f;
    }
}

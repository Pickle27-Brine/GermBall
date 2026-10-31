using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed partial class World
{
    // ----- picking up and throwing (props, debris, and men smaller than you)
    readonly List<Body> QPK = new();
    bool TryPickUp(Actor A)
    {
        if (ArmsOf(A) == 0 || P.rolling || P.grabbed != null) return false;
        var T = TYPES[A.type]; float fx = -MathF.Sin(P.facing), fz = -MathF.Cos(P.facing), reach = 1.5f * P.s + 0.4f;
        Body best = null; Actor bestA = null; float bs = 1e9f;
        QueryBodies(P.x - reach - 1.5f, P.x + reach + 1.5f, P.z - reach - 1.5f, P.z + reach + 1.5f, QPK);
        foreach (var b in QPK)
        {
            if (b.Held != null || b.M > T.Carry || b.Fixed || b.Actor != null) continue;
            float dx = b.Pos.x - P.x, dz = b.Pos.z - P.z, d = Hypot(dx, dz) - b.Bound * 0.6f;
            if (d > reach || b.Pos.y < P.y - 0.8f || b.Pos.y > P.y + P.H) continue;
            float hd = Hypot(dx, dz); float c = (dx * fx + dz * fz) / (hd == 0 ? 1 : hd); if (c < 0.1f && d > 0.4f) continue;
            float sc = d - c; if (sc < bs) { bs = sc; best = b; }
        }
        foreach (var B in actors)
        {
            if (B == A || B.dormant || !T.LiftMen.Contains(B.type) || B.P.holdA == A || !CanGrab(A, B)) continue;
            float dx = B.P.x - P.x, dz = B.P.z - P.z, d = Hypot(dx, dz) - B.P.R;
            if (d > reach || MathF.Abs(B.P.y - P.y) > 1.2f * P.s) continue;
            float hd = Hypot(dx, dz); float c = (dx * fx + dz * fz) / (hd == 0 ? 1 : hd); if (c < 0.1f && d > 0.4f) continue;
            float sc = d - c - 0.3f; if (sc < bs) { bs = sc; best = null; bestA = B; }
        }
        if (bestA != null && GrabActor(A, bestA)) return true;
        if (best != null)
        {
            best.Held = A; best.NoGrav = true; Wake(best); P.hold = best; best.ThrownT = 0;
            A.anim.holdK = 0; Sfx("grab", P.x, P.y, P.z); return true;
        }
        return false;
    }
    static bool CanGrab(Actor A, Actor B) { var G = B.P; return G.alive && !G.rolling && G.stunT <= 0 && G.downT <= 0 && G.grabbed == null && LegsOf(A) > 0 && ArmsOf(A) > 0; }
    bool GrabActor(Actor A, Actor B)
    {
        if (!CanGrab(A, B)) return false;
        var G = B.P;
        if (G.hold != null || G.holdA != null) DropHeld(B);
        A.P.holdA = B; G.grabbed = A; G.rolling = false; G.charging = false; G.ground = false; G.struggle = 0; G.hover = false; G.dashT = 0; G.pound = 0;
        Sfx("grab", G.x, G.y, G.z); if (B.human) { Rumble(0.9f, 1, 260); ShowHint("GRABBED · MASH JUMP TO BREAK FREE"); }
        B.brain?.OnGrabbed(A);
        return true;
    }
    static V3 HoldPoint(Actor A, V3 o)
    {
        var P = A.P; float fx = -MathF.Sin(P.facing), fz = -MathF.Cos(P.facing), wind = A.anim.throwT > 0 ? 0 : 1;
        o.x = P.x + fx * 0.1f * P.s; o.z = P.z + fz * 0.1f * P.s; o.y = P.y + P.H * (P.rolling ? 0.6f : 1.04f) + 0.05f - 0.15f * (1 - wind);
        return o;
    }
    readonly V3 HPT = new();
    void CarryBody(Actor A)
    {
        var b = P.hold;
        if (b == null || b.Removed) { P.hold = null; return; }
        HoldPoint(A, HPT); HPT.y += b.Kind == SPH ? b.R : MathF.Max(b.He.x, MathF.Max(b.He.y, b.He.z)) * 0.7f;
        b.Vel.x = (HPT.x - b.Pos.x) * 18 + P.vx; b.Vel.y = (HPT.y - b.Pos.y) * 18 + P.vy; b.Vel.z = (HPT.z - b.Pos.z) * 18 + P.vz;
        b.W.x *= 0.8f; b.W.y *= 0.8f; b.W.z *= 0.8f; b.Idle = 0; if (b.Sleep) Wake(b);
        if (b.Spear) AimQuat(-MathF.Sin(P.facing), 0, -MathF.Cos(P.facing), b.Q);
    }
    void CarryActor(Actor A)
    {
        var B = P.holdA;
        if (B == null || !B.P.alive || B.P.grabbed != A) { P.holdA = null; return; }
        HoldPoint(A, HPT); var G = B.P;
        G.x = HPT.x; G.y = HPT.y - G.H * 0.1f; G.z = HPT.z; G.vx = P.vx; G.vy = P.vy; G.vz = P.vz; G.ground = false; G.facing = P.facing + PI / 2;
    }
    void DropHeld(Actor A)
    {
        var P = A.P;
        if (P.hold != null) { var b = P.hold; P.hold = null; b.Held = null; b.NoGrav = false; b.IgnA = A; b.IgnT = 0.35f; }
        if (P.holdA != null) { var G = P.holdA.P; P.holdA = null; G.grabbed = null; G.grabCD = 1.5f; G.vy = 3; G.ground = false; }
    }
    // throw at whatever the thrower is aiming at: the crosshair for people, the brain's target for AI
    readonly V3 THR = new();
    struct ThrowV { public float vx, vy, vz; }
    ThrowV ThrowVel(Actor A, float charge)
    {
        var P = A.P; bool heavy = P.hold != null && P.hold.M > 150, man = P.holdA != null;
        float vmax = (TYPES[A.type].Big ? 36 : A.type == "brute" ? 32 : A.type == "small" ? 18 : 27) * (heavy && !TYPES[A.type].Big ? 0.7f : man ? 0.85f : 1);
        float sp = Lerp(0.3f, 1, charge) * vmax;
        float dx, dy, dz;
        if (A.AIM.ok) { dx = A.AIM.fx; dy = A.AIM.fy; dz = A.AIM.fz; } else { dx = -MathF.Sin(P.facing); dy = 0; dz = -MathF.Cos(P.facing); }
        dy += 0.12f + 0.12f * (1 - charge); float l = Hypot(dx, dy, dz); dx /= l; dy /= l; dz /= l;
        return new ThrowV { vx = dx * sp + P.vx * 0.5f, vy = dy * sp + MathF.Max(0, P.vy) * 0.5f, vz = dz * sp + P.vz * 0.5f };
    }
    void ThrowHeld(Actor A, float? charge = null)
    {
        var P = A.P;
        if (A == ME) StyleEvent("throw", A);
        float tx, ty, tz;
        if (A.IN.throwAt != null) { tx = A.IN.throwAt.x; ty = A.IN.throwAt.y; tz = A.IN.throwAt.z; }
        else if (A.AIM.ok) { float d = MathF.Min(60, AimDistance(60)); tx = A.AIM.px + A.AIM.fx * d; ty = A.AIM.py + A.AIM.fy * d; tz = A.AIM.pz + A.AIM.fz * d; }
        else { tx = P.x - MathF.Sin(P.facing) * 15; ty = P.y + 1; tz = P.z - MathF.Cos(P.facing) * 15; }
        HoldPoint(A, THR);
        bool heavy = P.hold != null && P.hold.M > 150; float vt = (TYPES[A.type].Big ? 30 : A.type == "brute" ? 26 : A.type == "small" ? 15 : 21) * (heavy && !TYPES[A.type].Big ? 0.8f : 1);
        float dd = Hypot(tx - THR.x, tz - THR.z), t = Clamp(dd / vt, 0.22f, 1.6f);
        float vx = (tx - THR.x) / t, vz = (tz - THR.z) / t, vy = (ty - THR.y) / t + 0.5f * PGRAV * t;
        float sp = Hypot(vx, vy, vz), cap = vt * 1.5f; if (sp > cap) { vx *= cap / sp; vy *= cap / sp; vz *= cap / sp; }
        if (charge != null && A.IN.throwAt == null) { var TVv = ThrowVel(A, charge.Value); vx = TVv.vx - P.vx * 0.5f; vy = TVv.vy; vz = TVv.vz - P.vz * 0.5f; }
        A.anim.throwT = 0.32f;
        if (P.hold != null)
        {
            var b = P.hold; DropHeld(A);
            b.Vel.x = vx + P.vx * 0.5f; b.Vel.y = vy; b.Vel.z = vz + P.vz * 0.5f;
            if (b.Spear) { AimQuat(vx, vy, vz, b.Q); b.W.Set(0, 0, 0); }
            else { float k = heavy ? 2 : 7; b.W.x = (R() - 0.5f) * k; b.W.y = (R() - 0.5f) * k; b.W.z = (R() - 0.5f) * k; }
            if (b.Bomb && !(b.Fuse > 0)) b.Fuse = 1.6f;
            b.ThrownT = 2.5f; b.ThrownBy = A;
        }
        else if (P.holdA != null)
        {
            var B = P.holdA; var G = B.P; DropHeld(A);
            G.vx = vx + P.vx * 0.5f; G.vy = vy; G.vz = vz + P.vz * 0.5f; G.flungT = 2.2f; G.flungBy = A; G.grabCD = 2; G.stunT = MathF.Max(G.stunT, 0.4f);
            G.x = THR.x; G.y = THR.y; G.z = THR.z;
        }
        Sfx("throw", P.x, P.y, P.z); Noise(P.x, P.z, 18, A);
    }
    /// <summary>Local -Z along (dx, dy, dz), local Y kept as close to world up as possible.</summary>
    static Q4 AimQuat(float dx, float dy, float dz, Q4 o)
    {
        float l = Hypot(dx, dy, dz); if (l == 0) l = 1; float zx = -dx / l, zy = -dy / l, zz = -dz / l;
        float xx = zz, xy = 0, xz = -zx, xl = Hypot(xx, xz); if (xl < 1e-4f) { xx = 1; xz = 0; } else { xx /= xl; xz /= xl; }
        float yx = zy * xz - zz * xy, yy = zz * xx - zx * xz, yz = zx * xy - zy * xx;
        return QFromBasis(xx, xy, xz, yx, yy, yz, zx, zy, zz, o);
    }

    // ----- ground pound shockwave: knocks men over, flings props
    void GroundPound(Actor A)
    {
        var P = A.P; float R2 = 6 * P.s, x = P.x, y = P.y, z = P.z;
        for (int i = 0; i < 18; i++) { float a = i / 18f * 6.28f; Puff(x + MathF.Cos(a) * 0.8f, y + 0.1f, z + MathF.Sin(a) * 0.8f, 8, 2.5f, 0.2f); }
        Sfx("pound", x, y, z); Rumble(1, 1, 300); Noise(x, z, 40, A);
        if (A.human) cam.shake = MathF.Max(cam.shake, 0.7f);
        QueryBodies(x - R2, x + R2, z - R2, z + R2, QPK);
        foreach (var b in QPK)
        {
            float dx = b.Pos.x - x, dy = b.Pos.y - y, dz = b.Pos.z - z, d = Hypot(dx, dy, dz); if (d > R2 || b.Held != null || b.Actor != null) continue;
            float dd = d == 0 ? 1 : d, k = (1 - d / R2) * MathF.Min(b.M, 300) * 9; Wake(b); IMP.Set(0, 0, 0);
            ApplyImpulse(b, dx / dd * k * 0.6f, k, dz / dd * k * 0.6f, IMP);
            if (b.Bomb && !(b.Fuse > 0)) b.Fuse = 0.8f;
        }
        foreach (var B in actors)
        {
            if (B == A || !B.P.alive || B.P.grabbed != null) continue;
            var G = B.P; float dx = G.x - x, dz = G.z - z, d = Hypot(dx, dz);
            if (d > R2 || MathF.Abs(G.y - y) > 2.5f) continue;
            float dd = d == 0 ? 1 : d, k = 1 - d / R2; bool heavy = B.type == "brute" && A.type != "brute";
            G.vx += dx / dd * 9 * k * (heavy ? 0.4f : 1); G.vz += dz / dd * 9 * k * (heavy ? 0.4f : 1); G.vy = MathF.Max(G.vy, 7 * k * (heavy ? 0.4f : 1)); G.ground = false;
            if (B.team != A.team) { DamageActor(B, 22 * k * (A.type == "brute" ? 1.6f : 1), new Dmg { src = A, kind = "blast", legs = true }); if (!heavy) G.stunT = MathF.Max(G.stunT, 1.1f * k + 0.3f); }
        }
    }
}

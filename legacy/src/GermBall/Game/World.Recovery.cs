using System;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed partial class PState
{
    /// <summary>Recovery: seconds spent locked (knocked down, ragdolled, grabbed, flattened, stunned) recently; decays while free.</summary>
    public float lockT;
    /// <summary>Knockdowns in a row (each one lands shorter); resets after a while on your feet.</summary>
    public int chainN; public float chainT;
    /// <summary>Guard after recovering: no knockdowns, grabs or flattening, and melee lands softer.</summary>
    public float guardT;
    public float techT;
}

/// <summary>
/// Recovery, so no enemy combo can hold you down for long:
/// - every knockdown in a chain is shorter than the last (hitstun decay);
/// - after a moment down, jump or roll techs out of it (a recovery flip with a short guard);
/// - if you spend too long locked anyway, you break out automatically with a shockwave that shoves attackers away;
/// - while guarded you can't be knocked down, grabbed or flattened again, and melee hits land at half strength.
/// Companions get the same rules, and tech out on their own.
/// </summary>
public sealed partial class World
{
    const float TECH_MIN = 0.35f, BREAKOUT_LOCK = 2.0f, GUARD_TECH = 0.7f, GUARD_BREAKOUT = 1.4f;

    static bool Locked(PState G) => G.ragdoll != null || G.grabbed != null || G.stunT > 0 || G.flatT > 0 || G.flungT > 0;

    /// <summary>Scales a knockdown's length for heroes by how many they've taken in a row.</summary>
    public float StunScale(Actor B)
    {
        if (B.team != "hero") return 1;
        var G = B.P; float k = 1f / (1 + 0.55f * G.chainN);
        G.chainN = Math.Min(G.chainN + 1, 6); G.chainT = 2.5f;
        return k;
    }
    public static bool Guarded(Actor B) => B.team == "hero" && B.P.guardT > 0;

    /// <summary>Runs at the start of each hero's step. Returns true if the actor recovered this step.</summary>
    bool RecoveryStep(Actor A, float dt, bool techPress)
    {
        var G = A.P; if (A.team != "hero" || !G.alive) return false;
        if (G.guardT > 0) G.guardT -= dt;
        bool locked = Locked(G);
        if (!locked)
        {
            G.lockT = MathF.Max(0, G.lockT - dt * 0.6f); G.techT = 0;
            if (G.chainT > 0) { G.chainT -= dt; if (G.chainT <= 0) G.chainN = 0; }
            return false;
        }
        G.lockT += dt; G.techT += dt;
        // AI companions tech out by themselves after a beat
        if (!A.human && G.techT > 0.8f) techPress = true;
        if (techPress && G.techT > TECH_MIN && (G.ragdoll != null || G.stunT > 0 || G.flatT > 0)) { Recover(A, false); return true; }
        if (G.lockT > BREAKOUT_LOCK) { Recover(A, true); return true; }
        return false;
    }

    void Recover(Actor A, bool breakout)
    {
        var G = A.P;
        if (G.ragdoll != null) EndRagdoll(A);
        if (G.grabbed != null) { var H = G.grabbed; if (H.P.holdA == A) DropHeld(H); H.P.stunT = MathF.Max(H.P.stunT, 0.6f); }
        G.stunT = 0; G.flatT = 0; G.flungT = 0; G.downT = 0; A.anim.down = 0; A.animFlatT = 0;
        G.lockT = 0; G.techT = 0; G.chainN = 0;
        G.guardT = breakout ? GUARD_BREAKOUT : GUARD_TECH;
        // a recovery flip: up and away from whoever is closest
        Actor near = null; float nd = 6;
        foreach (var B in actors) { if (B.team == A.team || !B.P.alive || B.dormant) continue; float d = Hypot(B.P.x - G.x, B.P.z - G.z); if (d < nd) { nd = d; near = B; } }
        float ax = 0, az = 0; if (near != null) { ax = G.x - near.P.x; az = G.z - near.P.z; float l = Hypot(ax, az); if (l > 0) { ax /= l; az /= l; } }
        G.vx = ax * 6; G.vz = az * 6; G.vy = breakout ? 9 : 7; G.ground = false; G.rolling = true; G.jumping = true; G.dashUsed = false; G.hoverFuel = 2.5f;
        if (breakout)
        {   // the shockwave: everyone close gets shoved off you
            foreach (var B in actors.ToArray())
            {
                if (B.team == A.team || !B.P.alive || B.dormant) continue;
                float dx = B.P.x - G.x, dz = B.P.z - G.z, d = Hypot(dx, dz); if (d > 5 || d < 1e-3f) continue;
                float k = (1 - d / 5);
                DamageActor(B, 6 * k + 2, new Dmg { src = A, kind = "melee", fx = dx, fz = dz, knock = 9 * k + 3, up = 4 });
                if (B.P.alive && B.type != "mech") Knockdown(B, 0.7f, dx / d * 8 * k, 4, dz / d * 8 * k);
            }
            for (int i = 0; i < 16; i++) Puff(G.x, G.y + 0.8f, G.z, 7, 1.5f, 0.18f);
            Sfx("zapbig", G.x, G.y + 1, G.z, 0.9f);
            if (A.human) { ShowHint("BREAKOUT"); cam.shake = MathF.Max(cam.shake, 0.5f); Rumble(1, 0.8f, 260); }
        }
        else
        {
            for (int i = 0; i < 6; i++) Puff(G.x, G.y + 0.3f, G.z, 3, 1, 0.12f);
            Sfx("dash", G.x, G.y, G.z, 0.7f);
            if (A.human) Rumble(0.4f, 0.6f, 120);
        }
    }
}

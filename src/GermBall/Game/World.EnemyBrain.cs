using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed partial class World
{
    // one step of an enemy's thinking
    void EnemyBrain(Actor A, float dt)
    {
        var G = A.P; var B = A.brain; var I = A.IN; var S = A.squad;
        I.mx = I.mz = 0; I.yaw = 0; I.fire = false; I.ads = false; I.throwAt = null; I.use = false;
        if (B.seq == null) { I.jump = false; I.roll = false; I.grab = false; I.pound = false; }
        B.aiming = false; B.sightT = 0;
        if (B.hurtT > 0) B.hurtT -= dt; if (B.alertT > 0) B.alertT -= dt;
        B.poundCD -= dt; B.dashCD -= dt; B.throwCD -= dt; B.punchCD -= dt; B.fearT -= dt; B.phaseT += dt; B.passT -= dt;
        if (G.grabbed != null) { B.mash -= dt; if (B.mash <= 0) { B.mash = A.type == "small" ? 0.3f : 0.16f; I.jump = !I.jump; } return; }
        if (G.stunT > 0 || G.flungT > 0 || G.ragdoll != null) return;
        if (aiFrozen) { I.aimYaw = G.facing; return; }
        // perception, a few times a second
        B.think -= dt;
        if (B.think <= 0)
        {
            B.think = 0.18f + R() * 0.12f;
            B.seeA = null;
            foreach (var H in Heroes()) if (CanSee(A, H)) { B.seeA = H; if (S != null) AlertSquad(S, H, H.P.x, H.P.z); break; }
        }
        bool armed = HasAmmo(A);
        // dry and nobody to fight right now: go and get ammo or a gun
        if (!armed && A.type != "brute" && G.vehicle == null && CanShoot(A) && !(S != null && S.state == "combat"))
        {
            if (B.ammoTgt == null || !items.Contains(B.ammoTgt)) B.ammoTgt = NearestAmmo(A, 70);
            if (B.ammoTgt != null) { B.wantAmmo = true; B.mode = "resupply"; var v0 = Steer(A, B.ammoTgt.x, B.ammoTgt.z, 0.9f, dt); I.mx = v0.Item1; I.mz = -v0.Item2; I.aimYaw = G.facing; RunSeq(A, dt); return; }
        }
        var K = S?.know != null && simT - S.know.t < 14 ? S.know : null;
        var T = B.seeA ?? (K?.A != null && K.A.P.alive ? K.A : null);
        float gx = G.x, gz = G.z, speed = 0;
        // ---- out of combat: hold the formation slot along the patrol
        if (S == null || S.state != "combat" || T == null || simT < S.engageT0 + (ENGAGE_DELAY.TryGetValue(A.type, out var ed) ? ed : 0) + (B.alertT > 0 ? B.alertT : 0))
        {
            if (S != null)
            {
                float fx = MathF.Sin(S.heading), fz = MathF.Cos(S.heading); var sl = SlotFor(S, A, A.slotI, A.slotN);
                gx = S.x + fz * sl.Item1 + fx * sl.Item2; gz = S.z - fx * sl.Item1 + fz * sl.Item2;
                float d = Hypot(gx - G.x, gz - G.z);
                speed = S.state == "combat" ? 0.55f : Clamp(d / 3, 0.25f, 0.9f) * (S.state == "investigate" ? (S.kind == "wave" ? 1.1f : 0.7f) : 0.4f);
                if (d < (G.vehicle != null ? 3 : 0.6f)) speed = 0;
                if (S.state == "combat" && T != null) { AimAt(A, T, AIM_ERR.TryGetValue(A.type, out var ae) ? ae : 0.4f, dt); B.aiming = true; }
                else I.aimYaw = MathF.Atan2(-fx, -fz);
            }
            var v1 = Steer(A, gx, gz, speed, dt); I.mx = v1.Item1; I.mz = -v1.Item2;
            RunSeq(A, dt); return;
        }
        // ---- combat
        var t = T.P; float dist = Hypot(t.x - G.x, t.z - G.z), health = HealthOf(A);
        B.tgtA = T;
        bool seeIt = B.seeA == T;
        if (seeIt) B.unseenT = 0; else B.unseenT += dt;
        float dd0 = dist == 0 ? 1 : dist, ux = (t.x - G.x) / dd0, uz = (t.z - G.z) / dd0;
        // troopers fall back once when badly hurt, then make a last stand
        if (!B.retreated && health < 0.38f && A.type == "normal" && G.vehicle == null)
        {
            B.retreated = true; B.mode = "retreat"; B.retreatT = 5 + R() * 3; B.cover = PickCover(A, T, 14, 40, 30);
            var home = S?.home;
            B.retreatTo = B.cover != null ? null : home != null && Hypot(home.x - G.x, home.z - G.z) < 60 ? (home.x, home.z) : (G.x - ux * 16, G.z - uz * 16);
        }
        // when the squad is thinned out and a brute is still standing, troopers fall back behind him and regroup
        if (A.type == "normal" && !B.regrouped && B.mode == "fight" && S != null && SquadStrength(S) < 0.5f && G.vehicle == null)
        {
            var br = S.members.FirstOrDefault(O => O.type == "brute" && O.P.alive);
            if (br != null)
            {
                B.regrouped = true; B.mode = "retreat"; B.regroup = true; B.retreatT = 3 + R() * 1.5f; if (B.cover != null && B.cover.by == A) B.cover.by = null; B.cover = null;
                float bx = br.P.x - t.x, bz = br.P.z - t.z, bd = Hypot(bx, bz); if (bd == 0) bd = 1;
                B.retreatTo = (br.P.x + bx / bd * 7 + (R() - 0.5f) * 6, br.P.z + bz / bd * 7 + (R() - 0.5f) * 6);
            }
        }
        if (B.mode == "retreat") { B.retreatT -= dt; if (B.retreatT <= 0 || dist < 5) { B.mode = "fight"; if (!B.regroup) B.lastStand = true; B.regroup = false; } }
        if (!armed && A.type != "brute" && B.mode != "retreat" && G.vehicle == null) { var it = NearestAmmo(A, 45); if (it != null) { B.mode = "resupply"; B.ammoTgt = it; } }
        if (B.mode == "resupply" && (B.ammoTgt == null || !items.Contains(B.ammoTgt) || armed)) { B.mode = "fight"; B.ammoTgt = null; }
        B.wantAmmo = A.type != "brute" && (B.mode == "resupply" || !armed);
        // brutes wait at the back until their squad has been thinned out, or until you come to them
        if (A.type == "brute" && !B.provoked && (G.berserk || S == null || B.hurtT > 0 || dist < 10 || SquadStrength(S) <= 0.45f)) { B.provoked = true; if (!G.berserk) Sfx("roar", G.x, G.y + 2, G.z, 0.7f); }
        bool strike = A.type == "assassin" && B.phase == "strike";
        AiPickWeapon(A, dist, dt, strike && dist < 6);
        float d2 = AimAt(A, T, (AIM_ERR.TryGetValue(A.type, out var ae2) ? ae2 : 0.4f) * B.aimK * (B.lastStand ? 0.8f : 1), dt);
        B.aiming = seeIt && CanShoot(A) && G.hold == null && G.holdA == null && !G.berserk;
        bool canFire = true; float shootChance = 1;
        if (G.holdA != null)
        {   // holding a man up: a moment of menace, then fling him (at his friend, if there is one)
            B.holdT += dt; speed = 0;
            if (B.holdT > 1.1f && B.seq == null)
            {
                var other = Heroes().FirstOrDefault(h => h != G.holdA && Hypot(h.P.x - G.x, h.P.z - G.z) < 30);
                float f = G.facing;
                I.throwAt = other != null ? new V3(other.P.x, other.P.y + 1, other.P.z) : new V3(G.x - MathF.Sin(f) * 22, G.y + 4, G.z - MathF.Cos(f) * 22);
                StartSeq(B, "grab"); B.holdT = 0;
            }
        }
        else if (G.hold != null)
        {
            B.holdT = 0;
            gx = t.x; gz = t.z; speed = dist > 9 ? 0.7f : 0.2f;
            if (dist < 32 && B.throwCD < -0.6f && B.seq == null) { I.throwAt = new V3(t.x + t.vx * 0.4f, t.y + t.H * 0.4f, t.z + t.vz * 0.4f); StartSeq(B, "grab"); B.throwCD = 5 + R() * 4; }
        }
        else if (G.vehicle != null && G.vehicle.kind == "chair")
        {
            // hover chairs hold a middle distance and slide from side to side while the beams play
            if (B.strafe == 0 || R() < dt * 0.3f) B.strafe = R() < 0.5f ? -1 : 1;
            float dx = G.x - t.x, dz = G.z - t.z, dl = Hypot(dx, dz); if (dl == 0) dl = 1; const float keep = 15;
            gx = t.x + dx / dl * keep - dz / dl * 7 * B.strafe; gz = t.z + dz / dl * keep + dx / dl * 7 * B.strafe; speed = 0.75f;
            // and bob up for a height advantage, then drop down again
            if (B.wantAlt == null || R() < dt * 0.15f) B.wantAlt = 0.6f + R() * 5;
            I.up = G.vehicle.alt < B.wantAlt - 0.3f; I.down = G.vehicle.alt > B.wantAlt + 0.3f;
        }
        else if (G.vehicle != null)
        {
            // bikers: run them down, overshoot, swing round and come again
            if (B.passT > 0) { gx = G.x - MathF.Sin(G.vehicle.yaw) * 12; gz = G.z - MathF.Cos(G.vehicle.yaw) * 12; }
            else { gx = t.x + t.vx * 0.5f; gz = t.z + t.vz * 0.5f; if (dist < 4.5f) B.passT = 1.2f + R() * 0.8f; }
            speed = 1;
        }
        else if (B.mode == "retreat")
        {
            if (B.cover != null) { gx = B.cover.x; gz = B.cover.z; } else if (B.retreatTo is var (rx, rz)) { gx = rx; gz = rz; }
            speed = 1; shootChance = 0.5f;
        }
        else if (B.mode == "resupply" && B.ammoTgt != null) { gx = B.ammoTgt.x; gz = B.ammoTgt.z; speed = 1; }
        else if (A.type == "brute")
        {
            if (G.berserk)
            {
                // armor gone: head down, charge straight through, arms windmilling
                gx = t.x + t.vx * 0.35f; gz = t.z + t.vz * 0.35f; speed = 1; canFire = false;
                if (dist < 2.3f + t.R && B.punchCD <= 0) { A.anim.punchT = 0.3f; B.punchCD = 0.55f; MeleeHit(A, T, 26, true); }
            }
            else if (!B.provoked)
            {
                // hang back behind the squad: watch, lob heavy shots, throw what is to hand
                float sx = S?.x ?? G.x, sz = S?.z ?? G.z, ds = Hypot(sx - t.x, sz - t.z); if (ds == 0) ds = 1; float back = MathF.Max(ds + 8, 26);
                gx = t.x + (sx - t.x) / ds * back + (A.slotI - (A.slotN - 1) / 2f) * 4 * (sz - t.z) / ds; gz = t.z + (sz - t.z) / ds * back - (A.slotI - (A.slotN - 1) / 2f) * 4 * (sx - t.x) / ds;
                speed = Hypot(gx - G.x, gz - G.z) > 2 ? 0.55f : 0;
                if (CurDef(A).Type == "melee") canFire = false;
                if (B.pick == null && B.throwCD <= 0 && dist < 30 && ArmsOf(A) > 0) { B.pick = NearProp(A, 8, 40, 1600); B.pickT = 4; if (B.pick == null) B.throwCD = 3; }
            }
            else
            {
                // walk straight at you, carry something big to throw, swing, grab or flatten you up close
                gx = t.x; gz = t.z; speed = dist > 3 ? 0.9f : 0.3f;
                bool liftable = TYPES["brute"].LiftMen.Contains(T.type);
                if (B.pick == null && B.throwCD <= 0 && dist > 9 && ArmsOf(A) > 0) { B.pick = NearProp(A, 10, 40, 1600); B.pickT = 4; if (B.pick == null) B.throwCD = 2; }
                if (dist < 2.6f + t.R && t.grabCD <= 0 && B.seq == null && B.punchCD <= 0) { B.punchCD = 1.4f; if (liftable && R() < 0.35f && GrabActor(A, T)) canFire = false; }
                else if (dist < 6 && G.ground && B.poundCD <= 0 && B.seq == null) { StartSeq(B, "pound"); B.poundCD = 6 + R() * 4; }
            }
        }
        else if (A.type == "mech")
        {
            // large mechs stride to a firing range, rake you with the heavy gun, tear up something huge and hurl it
            B.strafeT -= dt; if (B.strafeT <= 0) { B.strafeT = 2.5f + R() * 2.5f; B.strafe = R() < 0.5f ? -1 : 1; }
            float mx = -uz * B.strafe * 0.45f, mz = ux * B.strafe * 0.45f; const float want = 24;
            if (dist > want + 4) { mx += ux; mz += uz; } else if (dist < want - 8) { mx -= ux * 0.7f; mz -= uz * 0.7f; }
            gx = G.x + mx * 5; gz = G.z + mz * 5; speed = 0.85f; shootChance = 1;
            if (B.pick == null && G.hold == null && B.throwCD <= 0 && dist > 10 && dist < 50 && ArmsOf(A) > 0 && B.seq == null) { B.pick = NearProp(A, 16, 300, 12000) ?? MechRipUp(A); B.pickT = 5; B.throwCD = 0; }
            if (dist < 4.5f + t.R && B.punchCD <= 0) { A.anim.punchT = 0.4f; B.punchCD = 2.2f; MeleeHit(A, T, 34, true); }
        }
        else if (A.type == "small")
        {
            // the little ones are cowards: they fan out thin in front of the big men, and run back behind them when it gets hot
            var anc = AnchorOf(A);
            if (anc != null && (B.hurtT > 1.05f || dist < 6.5f || (health < 0.55f && R() < dt * 0.5f))) { if (B.fearT <= 0) B.fearT = 1.6f + R() * 2.2f; }
            if (anc != null && B.fearT > 0)
            {
                float ax = anc.P.x, az = anc.P.z, da = Hypot(ax - t.x, az - t.z); if (da == 0) da = 1; float px = -(az - t.z) / da, pz = (ax - t.x) / da;
                gx = ax + (ax - t.x) / da * 2.8f + px * B.side * 1.8f; gz = az + (az - t.z) / da * 2.8f + pz * B.side * 1.8f; speed = 1; shootChance = 0.3f;
            }
            else if (anc == null)
            {
                // nobody big left: scatter, firing wildly; only cornered do they bite
                if (dist < 3.5f) { gx = t.x; gz = t.z; speed = 1; if (dist < 1.6f + t.R && B.punchCD <= 0) { A.anim.punchT = 0.3f; B.punchCD = 0.9f; MeleeHit(A, T, 8); } }
                else
                {
                    B.strafeT -= dt; if (B.strafeT <= 0) { B.strafeT = 0.8f + R() * 1.5f; B.strafe = R() < 0.5f ? -1 : 1; }
                    const float want = 20; gx = G.x + (-uz * B.strafe * 0.9f + (dist < want ? -ux : ux * 0.5f)) * 4; gz = G.z + (ux * B.strafe * 0.9f + (dist < want ? -uz : uz * 0.5f)) * 4; speed = 1; shootChance = 0.6f;
                }
            }
            else
            {
                float ax = anc.P.x, az = anc.P.z, ang = MathF.Atan2(ax - t.x, az - t.z) + B.spreadA, want = 12 + B.spreadR;
                B.strafeT -= dt; if (B.strafeT <= 0) { B.strafeT = 1 + R() * 1.5f; B.strafe = R() < 0.5f ? -1 : 1; }
                gx = t.x + MathF.Sin(ang) * want + MathF.Cos(ang) * B.strafe * 2; gz = t.z + MathF.Cos(ang) * want - MathF.Sin(ang) * B.strafe * 2;
                speed = Clamp(Hypot(gx - G.x, gz - G.z) / 4, 0.35f, 1);
                if (dist > 7 && dist < 14 && B.dashCD <= 0 && G.ground && B.seq == null && R() < 0.3f) { StartSeq(B, "roll"); B.rollT = 1 + R() * 0.6f; B.dashCD = 9 + R() * 6; }
                else if (B.dashCD <= 0) B.dashCD = 3;
            }
            if (B.seq == "roll") { gx = t.x; gz = t.z; speed = 1; }
        }
        else if (A.type == "assassin")
        {
            // assassins: circle round to your blind side peppering you with darts, dash in with the knife, then melt away
            float f = t.facing;
            if (B.phase == "flank")
            {
                float ang = f + (PI - 0.9f) * B.side; const float r = 12;
                gx = t.x + MathF.Sin(ang) * r; gz = t.z + MathF.Cos(ang) * r; speed = 1;
                bool behind = (ux * -MathF.Sin(f) + uz * -MathF.Cos(f)) > 0.2f;   // we're behind them
                if (dist < 15 && B.dashCD <= 0 && G.ground && B.seq == null && (behind || B.phaseT > 5)) { B.phase = "strike"; B.phaseT = 0; StartSeq(B, "dash"); B.dashCD = 5 + R() * 3; }
            }
            else if (B.phase == "strike")
            {
                gx = t.x + t.vx * 0.3f; gz = t.z + t.vz * 0.3f; speed = 1;
                if (B.phaseT > 2.6f || (A.W.swingT > 0 && A.W.swingHit != 0)) { B.phase = "fade"; B.phaseT = 0; }
            }
            else
            {
                gx = G.x - ux * 5 + -uz * B.side * 4; gz = G.z - uz * 5 + ux * B.side * 4; speed = 1; shootChance = 0.4f;
                if (B.phaseT > 1.4f + R() * 0.02f) { B.phase = "flank"; B.phaseT = 0; B.side = -B.side; }
            }
        }
        else
        {
            // troopers: press forward, but from cover to cover, peeking out to shoot and ducking back in
            var w = CurWpn(A); float ideal = Clamp(w != null ? (IDEAL.TryGetValue(w.id, out var iv) ? iv : 16) : 6, 6, 38);
            B.coverT -= dt;
            if (B.cover != null)
            {
                float cvx = t.x - B.cover.x, cvz = t.z - B.cover.z;
                if ((cvx * B.cover.nx + cvz * B.cover.nz) > -0.2f * Hypot(cvx, cvz)) B.coverT = 0;   // flanked: find another
            }
            if (B.unseenT > 2.5f && K != null && !B.lastStand)
            {   // lost sight of them: go after them
                if (B.cover != null && B.cover.by == A) B.cover.by = null; B.cover = null;
                gx = K.x; gz = K.z; speed = 0.95f;
            }
            else
            {
                if (!B.lastStand && (B.cover == null || B.coverT <= 0))
                {
                    bool hurt = B.hurtT > 0; float minD = MathF.Max(5, ideal * 0.45f), maxD = hurt ? dist + 8 : MathF.Max(minD + 5, MathF.Min(dist - 2, ideal * 1.4f));
                    var prev = B.cover; B.cover = PickCover(A, T, minD, maxD, hurt ? 14 : B.role != 0 ? 40 : 24, hurt ? 0 : B.role);
                    if (B.cover == null && prev != null && prev.by == A) B.cover = prev;   // nothing further forward: stay put a while longer
                    B.coverT = B.cover != null ? 3.5f + R() * 3.5f : 1.2f; if (B.cover != prev) { B.coverIn = 0; B.peek = R() < 0.5f ? -1 : 1; }
                }
                if (B.cover != null && !B.lastStand)
                {
                    var c = B.cover; float dc = Hypot(c.x - G.x, c.z - G.z);
                    if (dc < 1.4f) B.coverIn += dt;
                    bool outp = B.coverIn > 0.4f && (B.coverIn % 3) < 1.7f;   // peek out and shoot, then duck back
                    gx = c.x + (outp ? -c.nz * B.peek * 1.3f : c.nx * 0.3f); gz = c.z + (outp ? c.nx * B.peek * 1.3f : c.nz * 0.3f); speed = dc > 2 ? 1 : 0.6f;
                    if (!outp && dc < 2) canFire = false;
                    if (B.hurtT > 1) B.coverIn = 1.75f;   // just hit: duck
                }
                else
                {
                    float want = B.lastStand || !armed ? 1.5f : ideal;
                    B.strafeT -= dt; if (B.strafeT <= 0) { B.strafeT = 1 + R() * 1.6f; B.strafe = R() < 0.5f ? -1 : 1; }
                    float mx = -uz * B.strafe * 0.7f, mz = ux * B.strafe * 0.7f;
                    if (dist > want + 3) { mx += ux; mz += uz; } else if (dist < want - 3) { mx -= ux * 0.6f; mz -= uz * 0.6f; }
                    gx = G.x + mx * 3; gz = G.z + mz * 3; speed = 1;
                }
            }
            if (B.pick == null && B.throwCD <= 0 && dist > 8 && dist < 26 && ArmsOf(A) > 0 && B.cover == null) { B.pick = NearProp(A, 7, 3, 40); B.pickT = 3; if (B.pick == null) B.throwCD = 3; }
            if (dist > 6 && dist < 13 && B.dashCD <= 0 && G.ground && B.seq == null && (B.lastStand || !armed)) { StartSeq(B, "dash"); B.dashCD = 6 + R() * 4; }
            if (!armed && MeleeSlot(A) < 0 && dist < 1.8f + t.R && B.punchCD <= 0) { A.anim.punchT = 0.3f; B.punchCD = 1; MeleeHit(A, T, 14); }
        }
        if (B.suppT > 0)
        {
            B.suppT -= dt;
            if (A.type == "small" && AnchorOf(A) != null && B.fearT <= 0) B.fearT = 1.2f + R();
            else if (A.type == "assassin" && B.phase == "flank" && B.phaseT > 1) { B.phase = "fade"; B.phaseT = 0; }
            else if (A.type == "normal" && !B.lastStand && B.mode == "fight") { if (B.cover != null) B.coverIn = MathF.Max(B.coverIn, 1.75f + 3 * MathF.Floor(B.coverIn / 3)); else if (B.coverT > 0.3f) B.coverT = 0.3f; }
        }
        // going for a prop to throw
        if (B.pick != null && G.hold == null && G.holdA == null && B.mode != "retreat" && G.vehicle == null)
        {
            B.pickT -= dt;
            if (B.pickT <= 0 || B.pick.Held != null || B.pick.Removed) { B.pick = null; B.throwCD = 3; }
            else
            {
                gx = B.pick.Pos.x; gz = B.pick.Pos.z; speed = 1;
                float pd = Hypot(gx - G.x, gz - G.z);
                if (pd < 1.5f * G.s + B.pick.Bound * 0.6f && B.seq == null)
                {
                    G.facing = MathF.Atan2(-(gx - G.x), -(gz - G.z)); I.aimYaw = G.facing;
                    if (WithActor(A, () => TryPickUp(A))) { B.pick = null; B.throwCD = 0.7f; } else B.pickT -= 0.5f;
                }
            }
        }
        if (B.seq == "dash" || B.seq == "roll") { gx = t.x; gz = t.z; speed = 1; }
        ApplyOrders(A, T, ref gx, ref gz, ref speed);
        var v = Steer(A, gx, gz, speed, dt); I.mx = v.Item1; I.mz = -v.Item2;
        // the trigger: shooting needs a turn (a squad token) unless they're cornered, plus a moment to react
        if (seeIt) { if (B.seenT <= 0) B.reactT = (A.type == "small" ? 0.8f : 0.5f) + R() * 0.7f; B.seenT = 1.5f; } else B.seenT -= dt;
        if (B.reactT > 0) { B.reactT -= dt; canFire = false; }
        if (B.suppT > 0 && A.type != "brute" && !B.lastStand) canFire = false;
        bool melee = CurDef(A).Type == "melee";
        if (canFire && !melee && !AiToken(A, dt) && d2 > 9 && !B.lastStand && A.W.charge <= 0) canFire = false;
        if (canFire && G.hold == null && G.holdA == null && CanShoot(A) && d2 < 70)
        {
            bool want = AiTrigger(A, T, d2, seeIt, dt);
            I.fire = want && (shootChance >= 1 || A.W.charge > 0 || R() < shootChance * 4 * dt + (B.burst > 0 ? 0.9f : 0));
        }
        else if (A.W.charge > 0) I.fire = false;
        RunSeq(A, dt);
    }
    void MeleeHit(Actor A, Actor T, float dmg, bool bash = false)
    {
        var G = A.P; var t = T.P; float dx = t.x - G.x, dz = t.z - G.z;
        if (Hypot(dx, dz) > G.R + t.R + 1.1f * G.s || MathF.Abs(t.y - G.y) > 1.5f) return;
        DamageActor(T, dmg * (A.type == "brute" ? 1.6f : 1), new Dmg { src = A, kind = "melee", fx = dx, fz = dz, knock = bash ? 17 : A.type == "brute" ? 11 : 6, up = bash ? 8 : 4 });
        if (bash) { float l = Hypot(dx, dz); if (l == 0) l = 1; Knockdown(T, 1.1f, dx / l * 15, 8, dz / l * 15); }
        Sfx("punch", t.x, t.y + 1, t.z);
    }
    // ----- squads: move the formation along its route, track what the squad knows
    void StepSquads(float dt)
    {
        var H = HUMAN.P;
        for (int si = squads.Count - 1; si >= 0; si--)
        {
            var S = squads[si];
            if (S.members.Count == 0)
            {
                if (S.dropping) continue;   // still on its way in by dropship
                if (S.kind == "wave" || S.kind == "debug") { squads.RemoveAt(si); continue; }
                S.respawnT -= dt;
                if (S.respawnT <= 0 && SET.density > 0)
                {
                    float far = Hypot(H.x - S.route[0].x, H.z - S.route[0].z);
                    // reinforcements: usually flown in by dropship (you get to watch), otherwise they walk in from off-screen
                    if (far > 45 && S.kind != "garrison" && R() < 0.7f) { S.x = S.route[0].x; S.z = S.route[0].z; S.wpi = 0; S.state = "patrol"; S.know = null; S.dropping = true; S.ship = CallDropship(S, S.x, S.z, S.comp); }
                    else if (far > 110) { S.x = S.route[0].x; S.z = S.route[0].z; S.wpi = 0; S.state = "patrol"; S.know = null; SpawnSquadMembers(S); }
                    else S.respawnT = 5;
                }
                continue;
            }
            StepRanks(S, dt);
            float near = 1e9f; foreach (var A in S.members) near = MathF.Min(near, NearestHeroDist(A.P));
            S.dormant = S.kind != "wave" && near > 135 && S.state != "combat";
            foreach (var A in S.members) { A.dormant = S.dormant; A.lod = near > 70 && S.state != "combat" ? 1 : 0; }
            if (S.dormant) continue;
            if (S.state == "combat")
            {
                if (S.know == null || simT - S.know.t > 14 || !S.know.A.P.alive) { S.state = "investigate"; S.investigate = S.know != null ? (S.know.x, S.know.z) : null; S.searchT = 10; S.know = null; }
                else
                {   // the formation point drifts toward the fight so stragglers follow
                    float k = MathF.Min(1, dt * 0.4f); S.x += (S.know.x - S.x) * k * 0.3f; S.z += (S.know.z - S.z) * k * 0.3f; S.heading = MathF.Atan2(S.know.x - S.x, S.know.z - S.z);
                }
                continue;
            }
            float tx, tz, sp = 2.1f;
            if (S.state == "investigate" && S.investigate is var (ix, iz) && S.investigate != null)
            {
                tx = ix; tz = iz; sp = S.kind == "wave" ? 4.6f : 3.2f;
                if (Hypot(tx - S.x, tz - S.z) < 3) { S.searchT -= dt; S.heading += dt * 0.6f; if (S.searchT <= 0) { S.state = "patrol"; S.investigate = null; } continue; }
            }
            else
            {
                var wp = S.route[S.wpi % S.route.Count]; tx = wp.x; tz = wp.z;
                if (Hypot(tx - S.x, tz - S.z) < 2) { S.wait -= dt; if (S.wait <= 0) { S.wpi++; S.wait = 2 + R() * 3; } continue; }
            }
            // don't let the formation point run off without the men
            float lag = 0; foreach (var A in S.members) lag = MathF.Max(lag, Hypot(A.P.x - S.x, A.P.z - S.z));
            if (lag > 14) sp *= 0.3f;
            float dx = tx - S.x, dz = tz - S.z;
            S.heading = TurnAngle(S.heading, MathF.Atan2(dx, dz), dt * 1.5f);
            S.x += MathF.Sin(S.heading) * sp * dt; S.z += MathF.Cos(S.heading) * sp * dt;
        }
    }
    float NearestHeroDist(PState p) { float d = 1e9f; foreach (var A in actors) if (A.team == "hero" && A.P.alive) d = MathF.Min(d, Hypot(A.P.x - p.x, A.P.z - p.z)); return d; }

    // =====================================================================
    // Wave mode: squads come in from gates on the arena rim, hunting you in formation.
    // Clear a wave for a breather, a shield refill and fresh stock.
    // =====================================================================
    public sealed class WaveState { public bool on; public int n; public string state = "pre"; public float t = 9; public int score, kills; public List<(float x, float z)> gates = new(); public bool over; public float huntT; public int best; }
    public readonly WaveState WAVES = new();
    static readonly Dictionary<string, int> WAVE_PTS = new() { ["small"] = 50, ["normal"] = 150, ["assassin"] = 250, ["brute"] = 600, ["mech"] = 2000 };
    void InitWaves()
    {
        WAVES.on = CFG.Mode == "waves";
        WAVES.best = Saves.Best(CFG.Map);
        if (!WAVES.on) return;
        float Rr = LAYOUT.Bowl != null ? ArenaR - 3 : 130;   // they come in through the membrane at the edge of the arena
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * TAU, x = MathF.Cos(a) * Rr, z = MathF.Sin(a) * Rr; int k = 0;
            while (InsideStatic(x, z, GroundY(x, z), 2.5f) && k++ < 12) { x *= 0.96f; z *= 0.96f; x += (R() - 0.5f) * 6; z += (R() - 0.5f) * 6; }
            WAVES.gates.Add((x, z));
        }
    }
    SquadComp WaveComp(int n, float share)
    {
        float d = MathF.Max(0.5f, SET.density), k = d * share;
        return new SquadComp
        {
            small = (int)MathF.Round((3 + 2 * n) * k), normal = Math.Max(1, (int)MathF.Round((1 + 0.9f * n) * k)),
            assassin = n >= 2 ? (int)MathF.Round(n / 2 * k) : 0,
            brute = SET.brutes && n >= 3 ? Math.Max(share >= 0.5f ? 1 : 0, Math.Min(3, (int)MathF.Round((n - 1) / 2 * share))) : 0,
            bikes = n >= 4 && share >= 0.4f ? Math.Min(2, (n - 2) / 3) : 0,
            mech = n >= 5 && share >= 0.3f ? (n >= 9 ? 2 : 1) * (share >= 0.5f || R() < 0.5f ? 1 : 0) : 0,
        };
    }
    void StartWave()
    {
        WAVES.n++; WAVES.state = "fight";
        int n = WAVES.n; bool bossWave = n % 5 == 0; int groups = bossWave ? Math.Min(2, 1 + n / 10) : Math.Min(4, 1 + n / 3); var H = HUMAN.P;
        // gates far from you, spread around: the far side of the rim from you first
        var gates = WAVES.gates.OrderBy(g => -Hypot(g.x - H.x, g.z - H.z)).Take(Math.Max(groups * 2, 6)).ToList();
        for (int i = gates.Count - 1; i > 0; i--) { int j = (int)MathF.Floor(R() * (i + 1)); (gates[i], gates[j]) = (gates[j], gates[i]); }
        for (int g = 0; g < groups; g++)
        {
            var Gt = gates[(g * 2) % gates.Count];
            // from wave 3 a group or two come by dropship, set down between the rim and the middle
            bool ship = n >= 3 && (g == 0 || R() < 0.3f);
            var comp = WaveComp(n, (bossWave ? 0.35f : 1) / groups);   // boss waves bring far fewer ordinary enemies
            if (bossWave) { comp.mech = 0; comp.bikes = 0; }
            if (n >= 2 && R() < 0.5f) comp.chairs = 1 + (n >= 6 ? 1 : 0);
            var S = MakeSquad(new Place { x = Gt.x, z = Gt.z }, "wave", new() { (Gt.x, Gt.z) }, comp, ship ? (Lerp(Gt.x, 0, 0.45f), Lerp(Gt.z, 0, 0.45f)) : null);
            S.heading = MathF.Atan2(H.x - Gt.x, H.z - Gt.z);
            S.state = "investigate"; S.investigate = (0, 0); S.searchT = 30; S.toMid = true;
        }
        if (bossWave)
        {
            // the bosses walk in from the rim gates on the far side
            int nb = 1 + n / 10;
            for (int b = 0; b < nb; b++) { var Gt = gates[(b * 2 + 1) % gates.Count]; SpawnBoss(Lerp(Gt.x, 0, 0.15f), Lerp(Gt.z, 0, 0.15f), "wave"); }
            var sp = ALLIES.FirstOrDefault(a => a.P.alive); if (sp != null) AllySay(sp, "bossWave", 5);
            Sfx("alert", H.x, H.y + 2, H.z, 1); ShowHint("⚠ BOSS WAVE " + n + " · " + nb + (nb > 1 ? " MIMICS" : " MIMIC") + " AND THEIR ESCORT");
            return;
        }
        Sfx("alert", H.x, H.y + 2, H.z, 1); ShowHint("WAVE " + n + " · INCOMING FROM " + groups + (groups > 1 ? " SIDES" : " SIDE"));
    }
    int WaveEnemiesLeft() { int n = 0; foreach (var S in squads) if (S.kind == "wave") n += S.members.Count + (S.dropping ? 1 : 0); return n; }
    void StepWaves(float dt)
    {
        if (!WAVES.on || WAVES.over) return;
        if (WAVES.state != "fight") { WAVES.t -= dt; if (WAVES.t <= 0) StartWave(); return; }
        // squads that haven't found you yet keep closing in on where you are
        WAVES.huntT -= dt;
        if (WAVES.huntT <= 0)
        {
            WAVES.huntT = 3; var H = HUMAN.P;
            foreach (var S in squads)
                if (S.kind == "wave" && S.state != "combat")
                {
                    // they come down off the rim to the middle of the arena, then hunt for you
                    if (S.toMid && Hypot(S.x, S.z) < 14) S.toMid = false;
                    S.state = "investigate"; S.searchT = 30;
                    S.investigate = S.toMid ? (0, 0) : (H.x + (R() - 0.5f) * 12, H.z + (R() - 0.5f) * 12);
                }
        }
        if (WaveEnemiesLeft() == 0)
        {
            WAVES.state = "break"; WAVES.t = 14; WAVES.score += WAVES.n * 250;
            foreach (var A in actors) if (A.team == "hero" && A.P.alive) { A.P.shield = 100; foreach (var a in A.armor) a.hp = MathF.Max(a.hp, a.max * 0.5f); }
            foreach (var s in stashes) if (s.item == null) s.t = MathF.Min(s.t, 0.5f);
            Sfx("powerup", HUMAN.P.x, HUMAN.P.y + 1, HUMAN.P.z, 1); ShowHint("WAVE " + WAVES.n + " CLEARED · SHIELD RESTORED · STASHES RESTOCKED");
            var sp = ALLIES.FirstOrDefault(a => a.P.alive); if (sp != null) AllySay(sp, "waveClear", 5);
        }
    }
    public string GameOverText;
    void WaveGameOver()
    {
        if (WAVES.over) return;
        WAVES.over = true;
        if (WAVES.score > WAVES.best) { WAVES.best = WAVES.score; Saves.SetBest(CFG.Map, WAVES.best); }
        var mapName = WAVE_MAPS.FirstOrDefault(m => m.key == CFG.Map).name ?? "the arena";
        GameOverText = "You fell on wave " + WAVES.n + " of " + mapName + " with " + WAVES.kills + " kills. Score " + WAVES.score + " · best " + WAVES.best + ".";
        OnGameOver?.Invoke();
    }
    public Action OnGameOver;
    public void RestartWaves()
    {
        foreach (var S in squads) foreach (var A in S.members.ToArray()) RemoveActor(A);
        squads.Clear();
        vehicles.RemoveAll(v => v.team == "enemy");
        WAVES.n = 0; WAVES.state = "pre"; WAVES.t = 7; WAVES.score = 0; WAVES.kills = 0; WAVES.over = false;
        foreach (var A in actors.ToArray()) if (A.team == "hero") RespawnActor(A);
    }
}

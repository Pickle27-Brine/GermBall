using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>Damage options (the original's "o" object).</summary>
public sealed class Dmg
{
    public Actor src; public string kind = "bullet"; public float knock, fx, fz, up; public int part = -1; public bool legs, charged;
    public bool hasHit; public float hx, hy, hz;
    public Dmg Hit(float x, float y, float z) { hasHit = true; hx = x; hy = y; hz = z; return this; }
}
public sealed class ArmorPlate { public int part; public string spec, mk; public float sc, hp, max, flash, y0, y1, phi, arc; }
public sealed class Ragdoll { public Body[] bodies = new Body[World.NPART]; public List<Joint> joints = new(); public float t, minT; public object grp; }
/// <summary>A light flying scrap (no rigid body): parts of a burst germ, shed armor.</summary>
public sealed class Chip { public V3 pos, pp, vel, w, ell; public Q4 q, pq; public float r, rest, life; public string mk; }

public sealed partial class Actor
{
    public string lastHurtKind; public Actor lastTarget; public float lastTargetT; public bool[] lost; public Actor pilotBy, mech; public bool pilot;
    public Body lastProp; public float lastPropT;
    public float hitByVeh; public bool hidden; public float needleT; public Actor needleSrc; public float flatMax, animFlatT, animFlatMax, animSwingDur; public string animSwingStyle; public bool animSwingFin;
}

public sealed partial class World
{
    // =====================================================================
    // Damage: shields (heroes only), then the part that was hit. Parts come off
    // as physics debris; losing the head, chest or hips shatters the whole man
    // =====================================================================
    static readonly float[] PART_W = { 7, 34, 12, 6, 5, 2, 6, 5, 2, 7, 5, 2, 7, 5, 2 };
    static readonly int[] LEG_PARTS = { 9, 10, 11, 12, 13, 14, 2 };
    static int PickPart(Actor B, bool legs)
    {
        float tot = 0, r;
        if (legs) { foreach (var p in LEG_PARTS) if (B.has[p]) tot += PART_W[p]; r = R() * tot; foreach (var p in LEG_PARTS) if (B.has[p]) { r -= PART_W[p]; if (r <= 0) return p; } }
        tot = 0; for (int i = 0; i < NPART; i++) if (B.has[i]) tot += PART_W[i];
        r = R() * tot;
        for (int i = 0; i < NPART; i++) if (B.has[i]) { r -= PART_W[i]; if (r <= 0) return i; }
        return 1;
    }
    static readonly Dictionary<int, string> PART_NAMES = new() { [3] = "LEFT ARM", [4] = "LEFT FOREARM", [5] = "LEFT HAND", [6] = "RIGHT ARM", [7] = "RIGHT FOREARM", [8] = "RIGHT HAND", [9] = "LEFT LEG", [10] = "LEFT SHIN", [11] = "LEFT FOOT", [12] = "RIGHT LEG", [13] = "RIGHT SHIN", [14] = "RIGHT FOOT" };

    public float DamageActor(Actor B, float dmg, Dmg o = null)
    {
        o ??= new Dmg();
        var G = B.P; if (!G.alive || !(dmg > 0)) return 0;
        var src = o.src; string kind = o.kind ?? "bullet";
        if (src != null && src != B && src.team == B.team && (kind == "bullet" || kind == "melee")) return 0;
        // knockback happens whatever the damage
        if (o.knock != 0) { float l = Hypot(o.fx, o.fz); if (l == 0) l = 1; G.vx += o.fx / l * o.knock; G.vz += o.fz / l * o.knock; if (o.up != 0) { G.vy = MathF.Max(G.vy, o.up); G.ground = false; } }
        if (B.team == "hero") { if (src == null || src.team != "hero") dmg *= ENEMY_DMG * SET.dmgTaken; }
        else if (src != null && src.team == "hero") dmg *= 2 * SET.dmgDealt;
        if (G.rolling)
        {   // curled up: hits glance off the shell
            if (B.team == "hero") { if (kind != "blast") { if (kind == "bullet") Sfx("ping", G.x, G.y, G.z, 0.4f); return 0; } dmg *= 0.5f; }
            else dmg *= kind == "blast" ? 0.6f : 0.35f;
        }
        if (!(dmg > 0.01f)) return 0;
        G.lastHit = simT; B.lastHurt = simT; B.lastHurtBy = src; B.lastHurtKind = kind;
        if (src != null && (src == HUMAN || src == ME) && B.team == "enemy") { HUMAN.lastTarget = B; HUMAN.lastTargetT = simT; }   // companions focus on what you shoot
        if (B.team == "hero" && G.shield > 0)
        {
            float a = MathF.Min(G.shield, dmg); G.shield -= a; dmg -= a;
            if (B.human) { Rumble(0.6f, 0.8f, 120); HudHurt(0.5f); }
            if (dmg <= 0.01f) { Sfx("shield", G.x, G.y + 1, G.z, 0.6f); return a; }
        }
        int p = o.part >= 0 && B.has[o.part] ? o.part : PickPart(B, o.legs);
        // armor plates on that part soak the hit until they break (charged plasma strips them fast)
        if (B.armor.Count > 0)
        {
            float strip = o.charged ? 3 : kind == "blast" ? 1.4f : 1;
            foreach (var pc in B.armor.ToArray())
            {
                if (pc.part != p || pc.hp <= 0) continue;
                float a = MathF.Min(pc.hp, dmg * strip); pc.hp -= a; dmg -= a / strip; pc.flash = 0.15f;
                if (o.hasHit) HitSpark(o.hx, o.hy, o.hz, 0.6f);
                Sfx("clank", G.x, G.y + 1, G.z, 0.6f);
                if (pc.hp <= 0) PopArmor(B, pc, o);
                CheckBerserk(B);
                if (dmg <= 0.01f) { if (B.brain != null) BrainHurt(B, src, a); return a; }
            }
        }
        B.hp[p] -= dmg; B.flash[p] = 0.16f; B.anim.hurtT = 0.25f;
        ChipPieces(B, p, dmg, o); BodyKick(B, o, dmg, p);
        if (o.hasHit) HitSpark(o.hx, o.hy, o.hz, MathF.Min(2, 0.7f + dmg / 25));
        else if (B.poseT >= 0) { int k = p * 13; HitSpark(B.pose[k], B.pose[k + 1], B.pose[k + 2], MathF.Min(2, 0.7f + dmg / 25)); }
        if (B.human) { Rumble(0.8f, 1, 200); HudHurt(1); }
        Sfx(B.team == "hero" ? "hurt" : "hit", G.x, G.y + 1, G.z, MathF.Min(1, 0.4f + dmg / 30));
        if (B.brain != null) BrainHurt(B, src, dmg);
        if (B.hp[p] <= 0)
        {
            if (PDEF[p].Crit || TYPES[B.type].Fragile) { KillActor(B, o); return dmg; }
            SeverPart(B, p, o);
        }
        else if (dmg > B.hpMax[1] * 0.35f && B.type != "brute") Knockdown(B, 0.45f, o.fx * 0.02f, 1, o.fz * 0.02f, true);
        return dmg;
    }
    void ImpactHurt(float speed)
    {   // the current actor slammed into the world after being thrown
        var A = CUR;
        for (int i = 0; i < 6; i++) Puff(P.x, P.y + 0.5f, P.z, 3, 2, 0.14f);
        Sfx("thud", P.x, P.y, P.z, 1);
        if (P.rolling) return;
        DamageActor(A, 6 + speed * 1.6f, new Dmg { src = P.flungBy, kind = "impact" });
        if (P.alive) Knockdown(A, 0.9f, P.vx * 0.3f, 2, P.vz * 0.3f);
    }
    void SeverPart(Actor B, int p, Dmg o)
    {
        if (!B.has[p]) return;
        if (p == PART.pelvis)
        {   // the hips hold: the legs come off instead, and further blows land on the chest
            B.hp[p] = 1;
            foreach (var k in PDEF[p].Kids) if (B.has[k]) SeverPart(B, k, o);
            B.hp[1] -= 10; if (B.hp[1] <= 0) KillActor(B, o);
            return;
        }
        var chain = new List<int> { p };
        for (int i = 0; i < chain.Count; i++) foreach (var k in PDEF[chain[i]].Kids) if (B.has[k]) chain.Add(k);
        PosePartsNow(B);
        foreach (var i in chain) { BlowPieces(B, i, o, 1.2f); SpawnDebris(B, i, o, 1); B.has[i] = false; }
        var G = B.P; Sfx("sever", G.x, G.y + 1, G.z);
        if (B.poseT >= 0) { int k = p * 13; GooBurst(B.pose[k], B.pose[k + 1], B.pose[k + 2]); }
        for (int i = 0; i < 6; i++) Puff(G.x, G.y + G.H * 0.6f, G.z, 3, 2, 0.08f);
        if (ArmsOf(B) == 0 || (!B.has[PART.handR] && !B.has[PART.handL])) DropHeld(B);
        if (B.human && PART_NAMES.TryGetValue(p, out var nm)) ShowHint("LOST " + nm);
        if (LegsOf(B) == 0) G.STEP = 0.3f;
    }
    // everything left of him flies apart
    public void KillActor(Actor B, Dmg o = null)
    {
        o ??= new Dmg();
        var G = B.P; if (!G.alive) return;
        if (B.pilot) UnboardMecha(B, true);
        PosePartsNow(B);
        G.alive = false; G.deadT = 0; DropHeld(B); if (G.vehicle != null) Dismount(B, true);
        foreach (var pc in B.armor) if (pc.hp > 0) PopArmor(B, pc, o, 2);
        if (G.grabbed != null) { var H = G.grabbed; if (H.P.holdA == B) H.P.holdA = null; G.grabbed = null; }
        for (int i = 0; i < NPART; i++) if (B.has[i]) { BlowPieces(B, i, o, 2); SpawnDebris(B, i, o, 2.2f, true); B.has[i] = false; }
        if (G.ragdoll != null) { foreach (var j in G.ragdoll.joints) joints.Remove(j); G.ragdoll = null; }
        Sfx("shatter", G.x, G.y + 1, G.z); for (int i = 0; i < 10; i++) Puff(G.x, G.y + G.H * 0.5f, G.z, 4, 3, 0.14f);
        if (B.human) { ShowHint("SHATTERED"); StyleEvent("death", B); Rumble(1, 1, 500); cam.shake = MathF.Max(cam.shake, 0.5f); }
        if (B.team == "enemy") { DropLoot(B); if (B.squad != null) SquadLost(B); CommsKill(B); }
        B.brain?.OnDeath();
    }
    void RespawnActor(Actor A)
    {
        var G = A.P;
        if (G.ragdoll != null) EndRagdoll(A);
        if (G.vehicle != null) Dismount(A, true);
        if (A.team == "enemy") { if (G.alive) { G.alive = false; G.deadT = 0; for (int i = 0; i < NPART; i++) A.has[i] = false; if (A.squad != null) SquadLost(A); } return; }
        for (int i = 0; i < NPART; i++) { A.has[i] = true; A.hp[i] = A.hpMax[i]; A.flash[i] = 0; }
        foreach (var pc in A.armor) pc.hp = pc.max;
        A.lost = null;
        G.alive = true; G.deadT = 0; G.shield = 100; G.STEP = MathF.Max(0.35f, 0.45f * G.s);
        float x = SPAWNX, z = SPAWNZ, f = PI;
        if (!A.human)
        {
            var H = HUMAN.P; x = H.x + MathF.Sin(H.facing) * 2.5f + MathF.Cos(H.facing) * 1.2f; z = H.z + MathF.Cos(H.facing) * 2.5f - MathF.Sin(H.facing) * 1.2f; f = H.facing;
            if (!H.alive) { x = SPAWNX + 2; z = SPAWNZ + 2; }
        }
        G.x = x; G.z = z; G.y = GroundAt(x, z, 999, true).h + 0.05f; G.vx = G.vy = G.vz = 0; G.px = G.x; G.py = G.y; G.pz = G.z;
        G.ground = true; G.rolling = G.jumping = G.charging = false; G.rev = 0; G.ctrlLock = 0; G.gBody = null; G.nx = 0; G.ny = 1; G.nz = 0;
        G.downT = 0; G.hoverFuel = 2.5f; G.dashUsed = false; G.dashT = 0; G.grabbed = null; G.flungT = 0; G.stunT = 0; G.pound = 0; G.hold = null; G.holdA = null; G.facing = f;
        A.W.charge = 0; A.W.reload = 0; A.W.beam = null; foreach (var w in A.W.slots) if (w != null) { w.heat = 0; w.over = false; }
        if (A.human) { cam.yaw = f; cam.pitch = SET.fp ? 0.05f : 0.36f; cam.tx = G.x; cam.ty = G.y + 2; cam.tz = G.z; cam.cur = cam.dist; }
        for (int i = 0; i < 8; i++) Puff(G.x, G.y + 0.8f, G.z, 2, 2, 0.12f);
    }

    // ----- debris: severed parts are ordinary physics props (throwable, too)
    public readonly List<Body> debris = new();
    public readonly List<Chip> CHIPS = new();
    Body SpawnDebris(Actor B, int i, Dmg o, float burst, bool cheap = false)
    {
        if (cheap && B.P.ragdoll == null && GERM.ContainsKey(B.type))
        {   // a body bursting apart: its parts fly as light particles, not rigid bodies
            var G0 = B.P; int k0 = i * 13; var ps0 = B.pose; float x0 = ps0[k0], y0 = ps0[k0 + 1], z0 = ps0[k0 + 2];
            if (!(float.IsFinite(x0) && float.IsFinite(y0))) { x0 = G0.x; y0 = G0.y + G0.H * 0.5f; z0 = G0.z; }
            float rx0 = ps0[k0 + 7] != 0 ? ps0[k0 + 7] : 0.1f, ry0 = ps0[k0 + 8] != 0 ? ps0[k0 + 8] : 0.1f, rz0 = ps0[k0 + 9] != 0 ? ps0[k0 + 9] : 0.1f;
            float dx0 = x0 - G0.x, dz0 = z0 - G0.z, dl0 = Hypot(dx0, dz0); if (dl0 == 0) dl0 = 1; float bk0 = 1.5f * burst;
            var c = new Chip
            {
                pos = new V3(x0, y0, z0), pp = new V3(x0, y0, z0),
                vel = new V3(G0.vx * 0.6f + dx0 / dl0 * bk0 + (R() - 0.5f) * 2 * burst + o.fx * 0.08f, MathF.Max(0, G0.vy * 0.5f) + 1.5f + R() * 2 * burst, G0.vz * 0.6f + dz0 / dl0 * bk0 + (R() - 0.5f) * 2 * burst + o.fz * 0.08f),
                q = new Q4(ps0[k0 + 3], ps0[k0 + 4], ps0[k0 + 5], ps0[k0 + 6]), pq = new Q4(ps0[k0 + 3], ps0[k0 + 4], ps0[k0 + 5], ps0[k0 + 6]),
                w = new V3((R() - 0.5f) * 10, (R() - 0.5f) * 10, (R() - 0.5f) * 10), r = MathF.Min(rx0, rz0), mk = CoreKey(B.type, i), ell = new V3(rx0, ry0 * 0.9f, rz0), life = 4 + R() * 2,
            };
            CHIPS.Add(c); while (CHIPS.Count > CHIP_MAX * 1.5f) CHIPS.RemoveAt(0);
            return null;
        }
        if (B.P.ragdoll != null) { var rb = DetachRagdollPart(B, i); if (rb != null) { rb.Vel.x += o.fx * 0.05f; rb.Vel.y += 2 * burst; rb.Vel.z += o.fz * 0.05f; return rb; } }
        var G = B.P; int k = i * 13; var ps = B.pose;
        float x = ps[k], y = ps[k + 1], z = ps[k + 2];
        if (!(float.IsFinite(x) && float.IsFinite(y))) { x = G.x; y = G.y + G.H * 0.5f; z = G.z; }
        float rx = ps[k + 7] != 0 ? ps[k + 7] : 0.1f, ry = ps[k + 8] != 0 ? ps[k + 8] : 0.1f, rz = ps[k + 9] != 0 ? ps[k + 9] : 0.1f;
        bool round = i == 0 || i == 5 || i == 8 || i == 11 || i == 14;
        float m = MathF.Max(1.5f, 950 * 4.19f * rx * ry * rz);
        var b = round ? MakeBody(SPH, x, y, z, m, r: (rx + ry + rz) / 3, e: 0.25f, mu: 0.7f, roll: 1.2f) : MakeBody(BOXB, x, y, z, m, he: new V3(rx * 0.8f, ry * 0.85f, rz * 0.8f), e: 0.15f, mu: 0.7f);
        b.Q.Set(ps[k + 3], ps[k + 4], ps[k + 5], ps[k + 6]); QNorm(b.Q); b.Pq.Copy(b.Q);
        float dx = x - G.x, dz = z - G.z, dl = Hypot(dx, dz); if (dl == 0) dl = 1; float bk = 1.5f * burst;
        b.Vel.x = G.vx * 0.6f + dx / dl * bk + (R() - 0.5f) * 2 * burst + o.fx * 0.08f; b.Vel.y = MathF.Max(0, G.vy * 0.5f) + 1.5f + R() * 2 * burst; b.Vel.z = G.vz * 0.6f + dz / dl * bk + (R() - 0.5f) * 2 * burst + o.fz * 0.08f;
        b.W.x = (R() - 0.5f) * 10; b.W.y = (R() - 0.5f) * 10; b.W.z = (R() - 0.5f) * 10;
        b.IsDebris = true; b.Mk = CoreKey(B.type, i); b.Ell = new V3(rx, ry, rz); b.Life = 6 + R() * 2; b.IgnA = B; b.IgnT = 0.2f;
        debris.Add(b);
        while (debris.Count > CHIP_MAX) RemoveDebrisBody(debris[0]);
        return b;
    }
    public void RemoveDebrisBody(Body b)
    {
        if (b.Removed) { debris.Remove(b); return; }
        RemoveBody(b);
        foreach (var A in actors) { if (A.P.gBody == b) A.P.gBody = null; if (A.P.hold == b) A.P.hold = null; if (A.W.lockBody == b) A.W.lockBody = null; }
        if (holoBody == b) holoBody = null;
        debris.Remove(b);
        OnBodyRemoved(b);
    }
    void StepDebris(float dt)
    {
        float crowd = debris.Count > CHIP_MAX * 0.6f ? 2.5f : debris.Count > CHIP_MAX * 0.3f ? 1.5f : 1;   // a crowded field clears faster
        for (int i = debris.Count - 1; i >= 0; i--)
        {
            if (i >= debris.Count) continue;
            var b = debris[i]; if (b.Held != null) continue; b.Life -= dt * crowd; b.Age += dt;
            if (b.Life <= 0 || b.Pos.y < -30) { RemoveDebrisBody(b); continue; }
            // settled scraps go to sleep early so piles of rubble cost nothing
            if (!b.Sleep && b.Age > 2 && b.Nc > 0 && Hypot(b.Vel.x, b.Vel.y, b.Vel.z) < 1.2f && Hypot(b.W.x, b.W.y, b.W.z) < 3)
            { b.Lazy += dt; if (b.Lazy > 0.3f) { b.Sleep = true; b.Vel.Set(0, 0, 0); b.W.Set(0, 0, 0); b.VertsOk = false; } }
            else b.Lazy = 0;
        }
    }

    // ----- actors in the contact solve: capsules of spheres (one ball when curled)
    readonly float[,] ASPH = new float[3, 2]; readonly List<Body> QAV = new();
    void ActorsVsProps()
    {
        foreach (var A in actors)
        {
            if (!A.inSim) continue;
            var P = A.P; var PB = A.body;
            int ns;
            if (P.rolling) { ns = 1; ASPH[0, 0] = P.BR; ASPH[0, 1] = P.BR; }
            else { float R2 = P.R; ns = 3; ASPH[0, 0] = R2; ASPH[0, 1] = R2; ASPH[1, 0] = P.H * 0.5f; ASPH[1, 1] = R2 * 1.1f; ASPH[2, 0] = P.H - R2; ASPH[2, 1] = R2; }
            float bse = P.y, m = P.mass * (P.rolling ? 5 : 1);
            PB.M = m; PB.Im = 1 / m; PB.E = P.rolling ? 0.3f : 0.02f;
            PB.Pos.x = P.x; PB.Pos.y = bse + P.H * 0.45f; PB.Pos.z = P.z;
            PB.Vel.x = P.vx; PB.Vel.y = P.vy; PB.Vel.z = P.vz; PB.Nc = 0;
            QueryBodies(P.x - 1.6f, P.x + 1.6f, P.z - 1.6f, P.z + 1.6f, QAV);
            A.NearProp = QAV.Count > 0;
            foreach (var b in QAV)
            {
                if (b.Held == A || (b.IgnA == A && b.IgnT > 0)) continue;
                if (b.Pos.y - b.Bound > bse + P.H + 0.2f || b.Pos.y + b.Bound < bse - 0.3f) continue;
                for (int k = 0; k < ns; k++)
                {
                    long key = (PB.Id * 8192L + 4096 + b.Id) * 4 + k;
                    var c = b.Kind == SPH ? SphereSphere(PB, P.x, bse + ASPH[k, 0], P.z, ASPH[k, 1], b, key) : SphereBox(PB, P.x, bse + ASPH[k, 0], P.z, ASPH[k, 1], b, key);
                    if (c == null) continue;
                    float vn = Dot(PB.Vel, c.N);
                    if (b.Sleep && vn < -1.2f) Wake(b);
                    if (vn < -6) { for (int q = 0; q < 3; q++) Puff(c.P.x, c.P.y, c.P.z, 3, 2, 0.1f); if (A.human) Rumble(MathF.Min(1, -vn / 22), 0.6f, 110); Sfx("bonk", c.P.x, c.P.y, c.P.z, MathF.Min(1, -vn / 20)); }
                    float rv = Dot(b.Vel, c.N) - vn;
                    if (b.Held != null) continue;
                    if ((b.ThrownT > 0 && b.ThrownBy != A && rv > 4) || (b.M > 120 && rv > 8 && !b.Sleep)) { ThrownHit(A, b, rv); b.ThrownT = 0; }
                    if (b.Bomb && vn < -6 && !(b.Fuse > 0)) b.Fuse = 1.2f;
                    if (b.LastTouchT != simT) { b.LastTouch = A; b.LastTouchT = simT; }
                }
            }
        }
    }
    void ThrownHit(Actor A, Body b, float rv)
    {
        var G = A.P;
        if (A.lastProp == b && simT - A.lastPropT < 0.4f) return;
        A.lastProp = b; A.lastPropT = simT;
        float dmg = Clamp(MathF.Sqrt(b.M) * rv * (b.Spear ? 0.7f : 0.32f), 3, 400);
        float knock = MathF.Min(14, b.M * rv / (G.mass * 2 + 1));
        DamageActor(A, dmg, new Dmg { src = b.ThrownBy, kind = "impact", fx = b.Vel.x, fz = b.Vel.z, knock = knock, up = knock * 0.3f, part = b.Spear ? PickPart(A, false) : -1 });
        if (b.Bomb) b.Fuse = MathF.Min(b.Fuse > 0 ? b.Fuse : 9, 0.04f);
        if (knock > 6 && A.type != "brute") G.stunT = MathF.Max(G.stunT, 0.7f);
    }
    void ActorsWriteback(float dt)
    {
        foreach (var A in actors)
        {
            var P = A.P; var PB = A.body;
            if (A.inSim && PB.Nc > 0)
            {
                P.vx = PB.Vel.x; P.vy = PB.Vel.y; P.vz = PB.Vel.z;
                P.x += PB.Pv.x * dt; P.z += PB.Pv.z * dt; if (!P.ground) P.y += PB.Pv.y * dt;
            }
            PB.Pv.Set(0, 0, 0);
        }
    }

    // ----- men against men: shove apart, roll into each other, get hit by thrown men
    readonly Dictionary<int, List<Actor>> agrid = new();
    void ActorsCollide()
    {
        foreach (var a in agrid.Values) a.Clear();
        foreach (var A in actors)
        {
            if (!A.P.alive || A.dormant) continue;
            int k = (FloorI(A.P.x / 4) + 512) * 1024 + FloorI(A.P.z / 4) + 512;
            if (!agrid.TryGetValue(k, out var a)) agrid[k] = a = new List<Actor>(); a.Add(A);
        }
        foreach (var A in actors)
        {
            if (!A.P.alive || A.dormant || A.P.grabbed != null) continue;
            int ci = FloorI(A.P.x / 4), cj = FloorI(A.P.z / 4);
            for (int i = ci - 1; i <= ci + 1; i++) for (int j = cj - 1; j <= cj + 1; j++)
                {
                    if (!agrid.TryGetValue((i + 512) * 1024 + j + 512, out var a)) continue;
                    foreach (var B in a)
                    {
                        if (B == A || !(A.uid < B.uid) || B.P.grabbed != null || A.P.holdA == B || B.P.holdA == A) continue;
                        CollideMen(A, B);
                    }
                }
        }
    }
    static bool Atk(PState Q, float v) => (Q.rolling && v > 7) || (Q.berserk && v > 3.5f) || (Q.vehicle != null && v > 6);
    void CollideMen(Actor A, Actor B)
    {
        var a = A.P; var b = B.P; float ra = a.rolling ? a.BR : a.R, rb = b.rolling ? b.BR : b.R;
        float dx = b.x - a.x, dz = b.z - a.z, d = Hypot(dx, dz), min = ra + rb;
        if (d >= min || d < 1e-5f) return;
        if (a.y > b.y + b.H * 0.9f || b.y > a.y + a.H * 0.9f) return;
        float nx = dx / d, nz = dz / d, ma = a.mass * (a.rolling ? 5 : 1), mb = b.mass * (b.rolling ? 5 : 1), wa = mb / (ma + mb), wb = ma / (ma + mb), o = min - d;
        a.x -= nx * o * wa; a.z -= nz * o * wa; b.x += nx * o * wb; b.z += nz * o * wb;
        float va = a.vx * nx + a.vz * nz, vb = b.vx * nx + b.vz * nz, rel = va - vb;
        if (rel <= 0) return;
        // a fast roll, a dash, or a thrown body: this is an attack
        Actor hitter = Atk(a, va) || (a.flungT > 0 && rel > 6) ? A : Atk(b, -vb) || (b.flungT > 0 && rel > 6) ? B : null;
        if (hitter != null)
        {
            var victim = hitter == A ? B : A; var H = hitter.P; var Vp = victim.P; float dir = hitter == A ? 1 : -1;
            float speed = rel; bool big = Vp.mass > H.mass * 2.5f;
            if (H.flungT > 0)
            {
                DamageActor(victim, 8 + speed * 1.2f, new Dmg { src = H.flungBy, kind = "impact", fx = nx * dir, fz = nz * dir, knock = big ? 2 : speed * 0.5f, up = 3 });
                DamageActor(hitter, 4 + speed * 0.6f, new Dmg { src = H.flungBy, kind = "impact" }); H.flungT = 0;
                if (!big) Knockdown(victim, 0.9f, nx * dir * speed * 0.5f, 3, nz * dir * speed * 0.5f);
            }
            else if (hitter.team != victim.team)
            {
                if (H.berserk)
                {   // a berserk brute plows through you
                    if (hitter.brain != null && hitter.brain.bashCD > 0) return; if (hitter.brain != null) hitter.brain.bashCD = 0.6f;
                    DamageActor(victim, 30, new Dmg { src = hitter, kind = "melee", fx = nx * dir, fz = nz * dir, knock = 4, up = 3 });
                    Knockdown(victim, 1.4f, nx * dir * 17, 8, nz * dir * 17);
                    Sfx("slam", Vp.x, Vp.y + 1, Vp.z, 1); if (victim.human) cam.shake = MathF.Max(cam.shake, 0.6f);
                    return;
                }
                if (victim.type == "assassin" && (H.rolling || H.vehicle != null)) KillActor(victim, new Dmg { fx = nx * dir * speed, fz = nz * dir * speed });
                else
                {
                    float vk = H.vehicle != null ? 1.5f : 1;
                    DamageActor(victim, (10 + (speed - 7) * 1.6f * (H.homingT > 0 || H.dashT > 0 ? 1.4f : 1)) * vk, new Dmg { src = hitter, kind = "melee", fx = nx * dir, fz = nz * dir, knock = big ? 1.5f : speed * 0.3f, up = big ? 0 : 2, legs = true });
                    if (!big) Knockdown(victim, 0.8f + MathF.Min(1, speed * 0.04f), nx * dir * speed * 0.5f, 4, nz * dir * speed * 0.5f);
                    else H.stunT = MathF.Max(H.stunT, 0.3f);
                }
            }
            // the hitter loses some of its speed into the victim
            H.vx -= nx * dir * speed * 0.45f; H.vz -= nz * dir * speed * 0.45f; H.homingT = 0;
            Sfx("slam", (a.x + b.x) / 2, a.y + 0.8f, (a.z + b.z) / 2, MathF.Min(1, speed / 20)); if (hitter.human) { Rumble(0.9f, 1, 160); cam.shake = MathF.Max(cam.shake, 0.25f); }
            for (int i = 0; i < 6; i++) Puff((a.x + b.x) / 2, a.y + 0.8f, (a.z + b.z) / 2, 4, 2, 0.12f);
            return;
        }
        float jj = rel * 1.1f;   // inelastic: cancel the closing speed by mass
        a.vx -= nx * jj * wa; a.vz -= nz * jj * wa; b.vx += nx * jj * wb; b.vz += nz * jj * wb;
    }

    // =====================================================================
    // Armor: curved shells hugging the body: bands of the same tapered profiles the parts are made of, a little larger.
    // [part, profile, from y, to y, centre angle (deg, 0 = front, -90 = outward on a limb), arc (deg), scale, hp]
    // =====================================================================
    sealed record ArmorDef(int Part, string Prof, float Y0, float Y1, float Phi, float Arc, float Sc, float Hp);
    static readonly Dictionary<string, ArmorDef[]> ARMOR_DEF = BuildArmorDef();
    static Dictionary<string, ArmorDef[]> BuildArmorDef()
    {
        ArmorDef A(int p, string pr, float y0, float y1, float phi, float arc, float sc, float hp) => new(p, pr, y0, y1, phi, arc, sc, hp);
        var hero = new[] { A(1, "V", 0.15f, 0.96f, 0, 150, 1.1f, 25), A(1, "V", -0.9f, -0.62f, 0, 120, 1.09f, 10), A(1, "V", -0.58f, -0.3f, 0, 126, 1.09f, 10), A(1, "V", -0.26f, 0.1f, 0, 136, 1.09f, 10), A(1, "V", -0.3f, 0.9f, 180, 140, 1.1f, 18),
            A(2, "G", -0.6f, 0.55f, 0, 140, 1.12f, 12), A(0, "H", 0.0f, 1.0f, 180, 250, 1.12f, 15),
            A(3, "L", -1.0f, -0.45f, 0, 360, 1.25f, 12), A(3, "L", -0.42f, 0.08f, -90, 200, 1.15f, 8), A(4, "C", -0.55f, 0.75f, -90, 220, 1.14f, 8),
            A(9, "L", -0.85f, 0.5f, 0, 150, 1.12f, 10), A(10, "C", -0.75f, 0.85f, 0, 170, 1.12f, 10) };
        var brute = new[] { A(1, "V", -0.1f, 1.0f, 0, 200, 1.22f, 60), A(1, "V", -0.95f, -0.12f, 0, 180, 1.2f, 40), A(1, "V", -0.5f, 1.0f, 180, 200, 1.22f, 40), A(0, "H", 0.1f, 1.0f, 180, 300, 1.22f, 40), A(2, "G", -0.8f, 0.6f, 0, 220, 1.2f, 40),
            A(3, "L", -1.0f, -0.2f, 0, 360, 1.35f, 40), A(3, "L", -0.2f, 0.5f, -90, 220, 1.25f, 25), A(4, "C", -0.8f, 0.8f, -90, 260, 1.25f, 25), A(9, "L", -0.9f, 0.6f, 0, 200, 1.2f, 30), A(10, "C", -0.85f, 0.85f, 0, 220, 1.2f, 30) };
        var d = new Dictionary<string, ArmorDef[]> { ["hero"] = hero, ["brute"] = brute, ["ally"] = hero };
        d["ally2"] = hero.Select(x => x with { Sc = x.Sc + 0.06f, Hp = MathF.Round(x.Hp * 1.8f) }).ToArray();   // Brick's plates are thicker
        d["ally3"] = hero.Where(x => (x.Part == 1 && x.Phi == 0) || x.Part == 0 || x.Part == 4).ToArray();      // Wisp travels light
        foreach (var (m, of) in MIMIC_OF) d[m] = d[of].Select(x => x with { Hp = MathF.Round(x.Hp * 1.8f) }).ToArray();
        return d;
    }
    static readonly Dictionary<int, int> ARMOR_LIMB_MIRROR = new() { [3] = 6, [4] = 7, [9] = 12, [10] = 13 };
    static string ArmorKey(string prof, float y0, float y1, float phi, float arc) => $"{prof}_{y0}_{y1}_{phi}_{arc}";
    void AddArmor(Actor A)
    {
        A.armor = new(); A.armorMax = 0;
        if (!ARMOR_DEF.TryGetValue(A.type, out var def)) return;
        float k = A.type == "brute" ? 0.65f : 1;
        foreach (var d in def)
        {
            var sides = ARMOR_LIMB_MIRROR.TryGetValue(d.Part, out var mir) ? new[] { (d.Part, d.Phi), (mir, -d.Phi) } : new[] { (d.Part, d.Phi) };
            foreach (var (pt, ph) in sides)
            {
                float hp = d.Hp * k; string spec = ArmorKey(d.Prof, d.Y0, d.Y1, ph, d.Arc);
                A.armor.Add(new ArmorPlate { part = pt, spec = spec, sc = d.Sc, hp = hp, max = hp, mk = "AR" + A.type + spec, y0 = d.Y0, y1 = d.Y1, phi = ph, arc = d.Arc }); A.armorMax += hp;
            }
        }
    }
    // where a shell sits in the world right now (from the pose)
    struct ArmorW { public float x, y, z, sx, sy, sz, bx, by, bz; public Q4 q; }
    readonly V3 AWV = new(), AWO = new();
    ArmorW ArmorWorld(Actor B, ArmorPlate pc)
    {
        int k = pc.part * 13; var ps = B.pose; float sc = pc.sc, rx = ps[k + 7] * sc, ry = ps[k + 8] * (sc * 0.5f + 0.5f), rz = ps[k + 9] * sc;
        var q = new Q4(ps[k + 3], ps[k + 4], ps[k + 5], ps[k + 6]);
        // the browser looked up each shell's mesh centre; the germ armor is drawn as scales hugging the part, centred on it
        AWV.Set(0, 0, 0); QRot(q, AWV, AWO);
        return new ArmorW { x = ps[k] + AWO.x, y = ps[k + 1] + AWO.y, z = ps[k + 2] + AWO.z, q = q, sx = rx, sy = ry, sz = rz, bx = rx * 2, by = ry * (pc.y1 - pc.y0), bz = rz * 2 };
    }
    void PopArmor(Actor B, ArmorPlate pc, Dmg o = null, float burst = 1.4f)
    {
        o ??= new Dmg();
        pc.hp = 0;
        if (B.poseT < 0) PosePartsNow(B);
        var w = ArmorWorld(B, pc); var G = B.P;
        if (GERM.TryGetValue(B.type, out var gs))
        {   // shed plates fly as light particles
            float dx = w.x - B.P.x, dz = w.z - B.P.z, dl = Hypot(dx, dz); if (dl == 0) dl = 1;
            char shape = GermDraw.ARMOR_SHAPE.TryGetValue(gs.ArmorStyle, out var sh) ? sh.shape : 'Z';
            AddChip(w.x, w.y, w.z, B.P.vx * 0.5f + dx / dl * 3 * burst + o.fx * 0.05f, 3 + R() * 3 * burst, B.P.vz * 0.5f + dz / dl * 3 * burst + o.fz * 0.05f, w.q, w.sx * 0.7f, w.sy * 0.3f, w.sz * 0.7f, GermKey(B.type, shape, 'd'), 5 + R() * 2);
            Sfx("clank", w.x, w.y, w.z, 1); for (int i = 0; i < 3; i++) Puff(w.x, w.y, w.z, 2, 2, 0.1f);
            return;
        }
        float hx = MathF.Max(0.04f, w.bx / 2), hy = MathF.Max(0.04f, w.by / 2), hz = MathF.Max(0.04f, w.bz / 2);
        var b = MakeBody(BOXB, w.x, w.y, w.z, MathF.Max(2, 300 * hx * hy * hz), he: new V3(hx, hy, hz), e: 0.25f, mu: 0.6f);
        b.Q.Copy(w.q); b.Pq.Copy(b.Q);
        float dx2 = w.x - G.x, dz2 = w.z - G.z, dl2 = Hypot(dx2, dz2); if (dl2 == 0) dl2 = 1;
        b.Vel.x = G.vx * 0.5f + dx2 / dl2 * 3 * burst + o.fx * 0.05f; b.Vel.y = 3 + R() * 3 * burst; b.Vel.z = G.vz * 0.5f + dz2 / dl2 * 3 * burst + o.fz * 0.05f;
        b.W.x = (R() - 0.5f) * 12; b.W.y = (R() - 0.5f) * 12; b.W.z = (R() - 0.5f) * 12;
        b.IsDebris = true; b.Mk = pc.mk; b.Ell = new V3(w.sx, w.sy, w.sz); b.Life = 5 + R() * 2; b.IgnA = B; b.IgnT = 0.25f;
        debris.Add(b); while (debris.Count > CHIP_MAX) RemoveDebrisBody(debris[0]);
        Sfx("clank", w.x, w.y, w.z, 1); for (int i = 0; i < 3; i++) Puff(w.x, w.y, w.z, 2, 2, 0.1f);
    }
    static float ArmorLeft(Actor B) { float s = 0; foreach (var pc in B.armor) s += MathF.Max(0, pc.hp); return s; }
    void CheckBerserk(Actor B)
    {
        if (B.type != "brute" || B.P.berserk || !B.P.alive) return;
        if (ArmorLeft(B) > B.armorMax * 0.3f) return;
        foreach (var pc in B.armor) if (pc.hp > 0) PopArmor(B, pc, new Dmg(), 2.5f);
        B.P.berserk = true; B.P.stunT = 0;
        for (int i = 0; i < 3; i++) if (B.W.slots[i] != null) DropWeapon(B, i);
        Sfx("roar", B.P.x, B.P.y + 2, B.P.z, 1);
        for (int i = 0; i < 12; i++) Puff(B.P.x, B.P.y + B.P.H * 0.6f, B.P.z, 5, 3, 0.16f);
        if (B.brain != null) { B.brain.mode = "fight"; B.brain.bashCD = 0; }
        if (B.squad != null) AlertSquad(B.squad, HUMAN, HUMAN.P.x, HUMAN.P.z);
    }

    // =====================================================================
    // Knockdowns. Heroes go fully limp: a physics ragdoll of their parts,
    // jointed at the shoulders, elbows, hips and knees. Enemies just fall.
    // =====================================================================
    public void Knockdown(Actor B, float t, float vx, float vy, float vz, bool light = false)
    {
        var G = B.P; if (!G.alive || G.vehicle != null) return;   // riders stay seated: only a crash throws them off
        if (B.team == "hero" && G.grabbed == null && !light) { StartRagdoll(B, vx, vy, vz, t); return; }
        G.stunT = MathF.Max(G.stunT, t); G.vx += vx; G.vz += vz; if (vy != 0) { G.vy = MathF.Max(G.vy, vy); G.ground = false; }
    }
    static readonly int[][] RAG_LINKS = { new[] { 0, 1 }, new[] { 3, 1 }, new[] { 6, 1 }, new[] { 1, 2 }, new[] { 4, 3 }, new[] { 5, 4 }, new[] { 7, 6 }, new[] { 8, 7 }, new[] { 9, 2 }, new[] { 10, 9 }, new[] { 11, 10 }, new[] { 12, 2 }, new[] { 13, 12 }, new[] { 14, 13 } };
    // how far each child may swing from its rest angle (radians): neck, shoulders, waist, elbows, wrists, hips, knees, ankles
    static readonly Dictionary<int, float> RAG_LIM = new() { [0] = 0.7f, [3] = 1.6f, [6] = 1.6f, [1] = 0.55f, [4] = 1.4f, [7] = 1.4f, [5] = 0.8f, [8] = 0.8f, [9] = 1.3f, [12] = 1.3f, [10] = 1.35f, [13] = 1.35f, [11] = 0.6f, [14] = 0.6f };
    readonly Q4 RMT = new();
    // muscle tone and joint limits: angular springs pull each part toward its rest angle, hard past its limit
    void RagdollMuscles(float dt)
    {
        foreach (var A in actors)
        {
            var R2 = A.P.ragdoll; if (R2 == null) continue;
            float fade = MathF.Max(0.35f, 1 - R2.t * 0.25f);   // the tone eases off as he settles, never to nothing
            foreach (var j in R2.joints)
            {
                if (j.Qrel == null) continue;
                var c = j.A; var p = j.B;
                var t = QMul(p.Q, j.Qrel, RMT); var cq = c.Q;
                float ex = cq.w * -t.x + cq.x * t.w + cq.y * -t.z - cq.z * -t.y, ey = cq.w * -t.y - cq.x * -t.z + cq.y * t.w + cq.z * -t.x, ez = cq.w * -t.z + cq.x * -t.y - cq.y * -t.x + cq.z * t.w, ew = cq.w * t.w + cq.x * t.x + cq.y * t.y + cq.z * t.z;
                if (ew < 0) { ex = -ex; ey = -ey; ez = -ez; ew = -ew; }
                float s = Hypot(ex, ey, ez); if (s < 1e-6f) continue;
                float ang = 2 * MathF.Atan2(s, ew), ax = ex / s, ay = ey / s, az = ez / s;
                float rate = ang * j.Tone * fade + MathF.Max(0, ang - j.Lim) * 14;
                float mc = c.M, mp = p.M, kc = mp / (mc + mp), kp = mc / (mc + mp), f = dt * 60 * dt;
                c.W.x -= ax * rate * kc * f; c.W.y -= ay * rate * kc * f; c.W.z -= az * rate * kc * f;
                p.W.x += ax * rate * kp * f; p.W.y += ay * rate * kp * f; p.W.z += az * rate * kp * f;
                // damp the relative spin a little
                float dx = c.W.x - p.W.x, dy = c.W.y - p.W.y, dz = c.W.z - p.W.z, dmp = MathF.Min(1, 4 * dt);
                c.W.x -= dx * dmp * kc; c.W.y -= dy * dmp * kc; c.W.z -= dz * dmp * kc; p.W.x += dx * dmp * kp; p.W.y += dy * dmp * kp; p.W.z += dz * dmp * kp;
                Wake(c); Wake(p);
            }
        }
    }
    readonly V3[] RJ = { new(), new(), new(), new() }; readonly Q4 RQI = new();
    static void PartEnds(Actor B, int i, V3 a, V3 b)
    {
        int k = i * 13; var ps = B.pose; float h = MathF.Max(0, ps[k + 8] - (ps[k + 7] + ps[k + 9]) / 2) * 0.9f + 0.02f;
        a.x = ps[k] - ps[k + 10] * h; a.y = ps[k + 1] - ps[k + 11] * h; a.z = ps[k + 2] - ps[k + 12] * h; b.x = ps[k] + ps[k + 10] * h; b.y = ps[k + 1] + ps[k + 11] * h; b.z = ps[k + 2] + ps[k + 12] * h;
    }
    void StartRagdoll(Actor B, float vx, float vy, float vz, float t)
    {
        var G = B.P;
        if (G.ragdoll != null) { var pb = G.ragdoll.bodies[2] ?? G.ragdoll.bodies[1]; if (pb != null) { pb.Vel.x += vx; pb.Vel.y += vy; pb.Vel.z += vz; } G.ragdoll.minT = MathF.Max(G.ragdoll.minT, G.ragdoll.t + t); return; }
        DropHeld(B); if (G.vehicle != null) Dismount(B, true);
        PosePartsNow(B);
        var R2 = new Ragdoll { minT = t, grp = new object() };
        for (int i = 0; i < NPART; i++)
        {
            if (!B.has[i]) continue;
            int k = i * 13; var ps = B.pose; float rx = ps[k + 7], ry = ps[k + 8], rz = ps[k + 9];
            bool round = i == 0 || i == 5 || i == 8 || i == 11 || i == 14;
            float m = MathF.Max(2, 950 * 4.19f * rx * ry * rz);
            var b = round ? MakeBody(SPH, ps[k], ps[k + 1], ps[k + 2], m, r: (rx + ry + rz) / 3, e: 0.1f, mu: 0.8f, roll: 2) : MakeBody(BOXB, ps[k], ps[k + 1], ps[k + 2], m, he: new V3(rx * 0.75f, ry * 0.8f, rz * 0.75f), e: 0.05f, mu: 0.8f);
            b.Q.Set(ps[k + 3], ps[k + 4], ps[k + 5], ps[k + 6]); QNorm(b.Q); b.Pq.Copy(b.Q);
            float k2 = i == 1 || i == 2 ? 1 : 0.8f;
            b.Vel.x = G.vx + vx * k2; b.Vel.y = G.vy + vy * k2; b.Vel.z = G.vz + vz * k2;
            b.Grp = R2.grp; b.RagdollOf = B; b.Part = i; b.Mk = CoreKey(B.type, i); b.Ell = new V3(rx, ry, rz);
            R2.bodies[i] = b;
        }
        foreach (var lk in RAG_LINKS)
        {
            int c = lk[0], p = lk[1]; var bc = R2.bodies[c]; var bp = R2.bodies[p]; if (bc == null || bp == null) continue;
            PartEnds(B, c, RJ[0], RJ[1]); PartEnds(B, p, RJ[2], RJ[3]);
            float best = 1e9f, jx = 0, jy = 0, jz = 0;
            foreach (var e1 in new[] { RJ[0], RJ[1] }) foreach (var e2 in new[] { RJ[2], RJ[3] }) { float d = Hypot(e1.x - e2.x, e1.y - e2.y, e1.z - e2.z); if (d < best) { best = d; jx = (e1.x + e2.x) / 2; jy = (e1.y + e2.y) / 2; jz = (e1.z + e2.z) / 2; } }
            if (c == 3 || c == 6 || c == 0)
            {
                PartEnds(B, c, RJ[0], RJ[1]);
                var e1 = Hypot(RJ[0].x - bp.Pos.x, RJ[0].y - bp.Pos.y, RJ[0].z - bp.Pos.z) < Hypot(RJ[1].x - bp.Pos.x, RJ[1].y - bp.Pos.y, RJ[1].z - bp.Pos.z) ? RJ[0] : RJ[1];
                jx = e1.x; jy = e1.y; jz = e1.z;
            }
            var la = new V3(jx - bc.Pos.x, jy - bc.Pos.y, jz - bc.Pos.z); var lb = new V3(jx - bp.Pos.x, jy - bp.Pos.y, jz - bp.Pos.z);
            QRot(bc.Q, la, la, true); QRot(bp.Q, lb, lb, true);
            var J = AddJoint(bc, bp, new[] { la.x, la.y, la.z }, new[] { lb.x, lb.y, lb.z });
            // rest pose of the joint, how far it may bend, and how much muscle tone holds it
            J.Qrel = QMul(new Q4(-bp.Q.x, -bp.Q.y, -bp.Q.z, bp.Q.w), bc.Q, new Q4()); J.Lim = RAG_LIM.TryGetValue(c, out var lim) ? lim : 1; J.Tone = c == 0 || c == 1 ? 5 : 2.5f;
            R2.joints.Add(J);
        }
        // parts collide with each other unless they share a joint or already overlap
        var list = R2.bodies.Where(b => b != null).ToList();
        foreach (var a in list) a.RagCol = new HashSet<Body>();
        for (int i = 0; i < list.Count; i++) for (int k = i + 1; k < list.Count; k++)
            {
                var a = list[i]; var b = list[k];
                if (RAG_LINKS.Any(l => (l[0] == a.Part && l[1] == b.Part) || (l[0] == b.Part && l[1] == a.Part))) continue;
                if (Hypot(a.Pos.x - b.Pos.x, a.Pos.y - b.Pos.y, a.Pos.z - b.Pos.z) < (a.Bound + b.Bound) * 0.62f) continue;
                a.RagCol.Add(b); b.RagCol.Add(a);
            }
        G.ragdoll = R2; G.rolling = false; G.charging = false; G.hover = false; G.pound = 0; G.stunT = MathF.Max(G.stunT, t);
        if (B.human) Rumble(1, 1, 250);
    }
    static Body RagdollRoot(Ragdoll R2) => R2.bodies[2] ?? R2.bodies[1] ?? R2.bodies.FirstOrDefault(b => b != null);
    void RagdollFollow(Actor A, float dt)
    {
        var G = A.P; var R2 = G.ragdoll; var pb = RagdollRoot(R2);
        R2.t += dt; G.stunT = MathF.Max(G.stunT, 0.05f);
        if (pb == null) { EndRagdoll(A); return; }
        G.x = pb.Pos.x; G.z = pb.Pos.z; G.vx = pb.Vel.x; G.vy = pb.Vel.y; G.vz = pb.Vel.z;
        G.y = MathF.Min(pb.Pos.y, GroundAt(pb.Pos.x, pb.Pos.z, pb.Pos.y + 0.5f, true).h);
        float sp = 0; foreach (var b in R2.bodies) if (b != null) sp = MathF.Max(sp, Hypot(b.Vel.x, b.Vel.y, b.Vel.z));
        if ((R2.t > R2.minT && sp < 1.6f) || R2.t > R2.minT + 3.5f) EndRagdoll(A);
    }
    void EndRagdoll(Actor A)
    {
        var G = A.P; var R2 = G.ragdoll; if (R2 == null) return;
        var tb = R2.bodies[1];
        if (tb != null) { AWV.Set(0, 0, -1); QRot(tb.Q, AWV, AWO); if (Hypot(AWO.x, AWO.z) > 0.2f) G.facing = MathF.Atan2(-AWO.x, -AWO.z); }
        foreach (var j in R2.joints) joints.Remove(j);
        foreach (var b in R2.bodies) if (b != null && b.RagdollOf == A) { RemoveBody(b); OnBodyRemoved(b); }
        G.ragdoll = null; G.ground = true; G.vx = G.vz = G.vy = 0; G.y = GroundAt(G.x, G.z, G.y + 1, true).h; G.px = G.x; G.py = G.y; G.pz = G.z;
        A.anim.down = 1; G.stunT = 0.5f;
    }
    // a part leaving the ragdoll (cut off, or he died) becomes ordinary debris
    Body DetachRagdollPart(Actor B, int i)
    {
        var R2 = B.P.ragdoll; if (R2 == null || R2.bodies[i] == null) return null;
        var b = R2.bodies[i]; R2.bodies[i] = null;
        for (int k = R2.joints.Count - 1; k >= 0; k--) { var j = R2.joints[k]; if (j.A == b || j.B == b) { joints.Remove(j); R2.joints.RemoveAt(k); } }
        b.RagdollOf = null; b.Grp = null; b.IsDebris = true; b.Life = 6; debris.Add(b); while (debris.Count > CHIP_MAX) RemoveDebrisBody(debris[0]);
        return b;
    }
    void PoseFromRagdoll(Actor A, float alpha)
    {
        var R2 = A.P.ragdoll; var ps = A.pose;
        for (int i = 0; i < NPART; i++)
        {
            var b = R2.bodies[i]; if (b == null || !A.has[i]) continue;
            int k = i * 13; QNlerp(b.Pq, b.Q, alpha, RQI);
            ps[k] = Lerp(b.Pp.x, b.Pos.x, alpha); ps[k + 1] = Lerp(b.Pp.y, b.Pos.y, alpha); ps[k + 2] = Lerp(b.Pp.z, b.Pos.z, alpha);
            ps[k + 3] = RQI.x; ps[k + 4] = RQI.y; ps[k + 5] = RQI.z; ps[k + 6] = RQI.w;
            ps[k + 7] = b.Ell.x; ps[k + 8] = b.Ell.y; ps[k + 9] = b.Ell.z;
            AWV.Set(0, 1, 0); QRot(RQI, AWV, AWO); ps[k + 10] = AWO.x; ps[k + 11] = AWO.y; ps[k + 12] = AWO.z;
        }
        A.poseT = clockT;
    }
}

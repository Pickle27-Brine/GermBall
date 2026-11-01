using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed class SquadComp { public int small, normal, assassin, brute, mech, bikes, chairs; public SquadComp Clone() => (SquadComp)MemberwiseClone(); }
public sealed class Knowledge { public Actor A; public float x, z, y, t; }
public sealed partial class Squad
{
    public int id; public Place home; public string kind; public List<(float x, float z)> route; public SquadComp comp; public int wpi; public List<Actor> members = new();
    public float x, z, heading; public string state = "patrol"; public Knowledge know; public (float x, float z)? investigate; public float searchT, engageT0, respawnT, wait; public bool dormant;
    public bool dropping, toMid, boss; public Vehicle ship, bike; public int size0, tokN;
}

/// <summary>An AI's working memory (the original's brain object).</summary>
public sealed partial class Brain
{
    public string mode = "fight"; public float think; public Actor seeA; public CoverPt cover; public float coverT, coverIn, peek = 1; public Actor tgtA; public bool aiming; public float alertT, hurtT, retreatT; public bool retreated, lastStand;
    public float stuckT, sideT, side, avoid, avoidT, strafe = 1, strafeT; public string seq; public float seqT, rollT, poundCD, dashCD, throwCD, punchCD;
    public float errX, errY, errZ; public Body pick; public float pickT; public bool wantAmmo; public float ammoT, gx, gz, run, mash;
    public Action onDeath; public Action<Actor> onGrabbed;
    public float swT, burst, patT, mark, sightT, aimK = 1, chargeGoal;
    public float fearT, spreadA, spreadR; public bool provoked; public float unseenT; public string phase = "flank"; public float phaseT, passT, sep;
    public float suppT, tokT, tokCD, bashCD; public int role; public bool rider; public Actor threat; public Item ammoTgt;
    public float holdT, seenT, reactT; public float? wantAlt; public bool regrouped, regroup; public (float x, float z)? retreatTo; public bool toMid;
    // companions
    public float lowT, sayCD, regroupT; public object extra;
    public void OnGrabbed(Actor by) => onGrabbed?.Invoke(by);
    public void OnDeath() => onDeath?.Invoke();
}

public sealed partial class Actor { public int slotI, slotN; }

public sealed partial class World
{
    // =====================================================================
    // AI: squads with formations (the small men step in first, troopers behind, brutes in pairs or trios at the back),
    // patrols, vision cones and hearing, cover, and men who mind their health and ammo: they fall back to cover or
    // resupply, but they come back, and they fight to the end when cornered.
    // =====================================================================
    public readonly List<Squad> squads = new();
    int squadIds; public bool aiFrozen;
    static readonly Dictionary<string, (float range, float cos)> VIS = new() { ["small"] = (40, 0.45f), ["normal"] = (58, 0.5f), ["brute"] = (48, 0.42f), ["assassin"] = (62, 0.3f) };
    static readonly Dictionary<string, float> ENGAGE_DELAY = new() { ["small"] = 0, ["normal"] = 1.2f, ["brute"] = 0, ["assassin"] = 0.6f, ["mech"] = 0.8f };
    static readonly Dictionary<string, float> AIM_ERR = new() { ["small"] = 0.85f, ["normal"] = 0.25f, ["brute"] = 0.6f, ["assassin"] = 0.3f, ["mech"] = 0.45f };
    // the range each weapon likes to be fired from
    static readonly Dictionary<string, float> IDEAL = new() { ["pistol"] = 14, ["revolver"] = 18, ["smg"] = 10, ["rifle"] = 20, ["hunting"] = 32, ["sniper"] = 45, ["shotgun"] = 7, ["rocket"] = 24, ["bow"] = 22, ["knives"] = 11, ["darts"] = 15, ["needler"] = 13, ["plasma"] = 13, ["laserpistol"] = 18, ["armcannon"] = 20, ["chargecannon"] = 28 };
    // firing patterns: [min, max] of a burst (seconds held for automatics, shots for the rest), [min, max] pause after it
    static readonly Dictionary<string, float[]> PATTERN = new()
    {
        ["hmg"] = new[] { 1.2f, 2.4f, 0.8f, 1.6f }, ["pistol"] = new[] { 2f, 3, 0.6f, 1.4f }, ["revolver"] = new[] { 1f, 2, 1, 1.8f }, ["smg"] = new[] { 0.5f, 1.4f, 0.7f, 1.5f }, ["rifle"] = new[] { 0.25f, 0.45f, 0.45f, 0.9f }, ["needler"] = new[] { 0.6f, 1.1f, 0.8f, 1.4f }, ["shotgun"] = new[] { 1f, 1, 0.7f, 1.3f },
        ["rocket"] = new[] { 1f, 1, 2.6f, 4 }, ["knives"] = new[] { 1f, 2, 0.9f, 1.8f }, ["darts"] = new[] { 3f, 3, 1.1f, 2 }, ["laserpistol"] = new[] { 3f, 4, 0.7f, 1.2f }, ["armcannon"] = new[] { 0.6f, 1, 1.1f, 1.6f }, ["hunting"] = new[] { 1f, 1, 1, 1 }, ["sniper"] = new[] { 1f, 1, 1, 1 },
    };
    SquadComp SquadComposition(string kind)
    {
        float d = SET.density, r = R(); bool g = kind == "garrison";
        int small = (int)MathF.Round((g ? 5 : 6) * d), normal = Math.Max(d > 0 ? 1 : 0, (int)MathF.Round(((g ? 4 : 3) + (R() < 0.5f ? 1 : 0)) * d));
        int brute = 0, assassin = 0;
        if (SET.brutes && d > 0 && (!g || r < 0.5f)) brute = d < 1 ? 1 : r < 0.55f ? 2 : 3;
        if (d > 0 && R() < (g ? 0.35f : 0.65f)) assassin = d >= 1 && R() < 0.4f ? 2 : 1;
        return new SquadComp { small = small, normal = normal, assassin = assassin, brute = brute };
    }
    public Squad MakeSquad(Place home, string kind, List<(float x, float z)> route, SquadComp comp = null, (float x, float z)? drop = null)
    {
        var S = new Squad { id = ++squadIds, home = home, kind = kind, route = route, comp = comp, x = route[0].x, z = route[0].z };
        squads.Add(S);
        if (drop is var (dx, dz)) { S.dropping = true; S.ship = CallDropship(S, dx, dz, comp); }
        else SpawnSquadMembers(S);
        return S;
    }
    static (float, float) SlotFor(Squad S, Actor A, int i, int n) => FormationSlot(S, A, i, n, SlotBasic(S, A, i, n));
    static (float, float) SlotBasic(Squad S, Actor A, int i, int n)
    {   // formation offsets in the squad frame: +z forward
        if (A.type == "small") { const int per = 5; int r = i / per, c = i % per, w = Math.Min(per, n - r * per); return ((c - (w - 1) / 2f) * 2.6f, 6.5f - r * 2.2f); }
        if (A.type == "normal") return ((i - (n - 1) / 2f) * 3.0f, 0.5f);
        if (A.type == "assassin") return ((i % 2 != 0 ? 1 : -1) * (9 + i * 2), 2);
        if (A.type == "mech") return ((i - (n - 1) / 2f) * 9, -11);
        return ((i - (n - 1) / 2f) * 3.6f, -5);
    }
    void SpawnSquadMembers(Squad S)
    {
        var c = S.comp ?? SquadComposition(S.kind);
        // a ceiling on how many germs fight at once: a squad that would go over it comes in thinner
        int alive = actors.Count(A => A.team == "enemy" && A.P.alive);
        int room = Math.Max(0, SET.enemyCap - alive), want = c.small + c.normal + c.assassin + c.brute + c.mech;
        if (want > room && want > 0)
        {
            float k = (float)room / want; c = c.Clone();
            c.small = (int)MathF.Floor(c.small * k); c.normal = (int)MathF.Floor(c.normal * k + (room > 0 ? 0.99f : 0)); c.assassin = (int)MathF.Floor(c.assassin * k); c.brute = (int)MathF.Floor(c.brute * k); c.mech = (int)MathF.Floor(c.mech * k);
        }
        S.members.Clear(); S.size0 = c.small + c.normal + c.assassin;
        float fx = MathF.Sin(S.heading), fz = MathF.Cos(S.heading);
        foreach (var (type, n) in new[] { ("small", c.small), ("normal", c.normal), ("assassin", c.assassin), ("brute", c.brute), ("mech", c.mech) })
            for (int i = 0; i < n; i++)
            {
                var A = MakeActor(type, S.x, S.z, false, MathF.Atan2(-fx, -fz));
                A.squad = S; A.brain = MakeBrain(A); A.slotI = i; A.slotN = n;
                var sl = SlotFor(S, A, i, n);
                float x = S.x + fz * sl.Item1 + fx * sl.Item2, z = S.z - fx * sl.Item1 + fz * sl.Item2;
                bool ok = !InsideStatic(x, z, GroundY(x, z), A.P.R + 0.2f);
                A.P.x = ok ? x : S.x + (R() - 0.5f) * 3; A.P.z = ok ? z : S.z + (R() - 0.5f) * 3; A.P.y = GroundY(A.P.x, A.P.z) + 0.02f; A.P.px = A.P.x; A.P.py = A.P.y; A.P.pz = A.P.z;
                S.members.Add(A);
            }
        AssignRanks(S);
        // roaming squads often bring a motorbike for one of the troopers
        int nb = S.comp != null ? S.comp.bikes : S.kind != "garrison" && R() < 0.55f ? 1 : 0;
        for (int bi = 0; bi < nb; bi++)
        {
            var Rr = S.members.FirstOrDefault(A => A.type == "normal" && A.P.vehicle == null);
            if (Rr == null) break;
            if (S.comp != null) { Mount(Rr, MakeVehicle("bike", Rr.P.x, Rr.P.z, Rr.P.facing, "enemy")); Rr.brain.rider = true; continue; }
            if (S.bike != null && S.bike.dead) { vehicles.Remove(S.bike); S.bike = null; }
            if (S.bike != null && S.bike.rider != null) S.bike = null;
            if (S.bike == null) S.bike = MakeVehicle("bike", Rr.P.x, Rr.P.z, Rr.P.facing, "enemy");
            else { var Vh = S.bike; Vh.x = Vh.px = Rr.P.x; Vh.z = Vh.pz = Rr.P.z; Vh.y = Vh.py = GroundAt(Vh.x, Vh.z, 999, true).h; Vh.yaw = Rr.P.facing; Vh.speed = 0; }
            Mount(Rr, S.bike); Rr.brain.rider = true;
        }
        // and some troopers come floating in on hover chairs
        int nc = S.comp != null ? S.comp.chairs : S.kind != "garrison" && R() < 0.4f ? 1 : 0;
        for (int ci = 0; ci < nc; ci++)
        {
            var Rr = S.members.FirstOrDefault(A => A.type == "normal" && A.P.vehicle == null); if (Rr == null) break;
            var Cc = MakeVehicle("chair", Rr.P.x, Rr.P.z, Rr.P.facing, "enemy"); Cc.y = Cc.py = Rr.P.y + 0.6f; Mount(Rr, Cc); Rr.brain.rider = true;
        }
    }
    void InitSquads()
    {
        foreach (var O in OUTPOSTS)
        {
            MakeSquad(O, "garrison", O.waypoints);
            // the patrol walks the outside ring and out to the nearest town and back
            Place nt = null; float nd = 1e9f; foreach (var T in TOWNS) { float d = Hypot(T.x - O.x, T.z - O.z); if (d < nd) { nd = d; nt = T; } }
            var route = O.outside.Take(4).ToList();
            if (nt != null) { route.Add((Lerp(O.x, nt.x, 0.5f), Lerp(O.z, nt.z, 0.5f))); route.Add((nt.x + 2, nt.z + 2)); route.Add((Lerp(O.x, nt.x, 0.5f), Lerp(O.z, nt.z, 0.5f))); }
            route.AddRange(O.outside.Skip(4));
            MakeSquad(O, "patrol", route);
        }
        // a roaming squad between the towns
        var tr = TOWNS.Select(T => (T.x, T.z)).ToList();
        if (tr.Count > 1) { var rt = tr.ToList(); rt.AddRange(tr.Skip(1).Take(tr.Count - 2).Reverse()); MakeSquad(TOWNS[0], "roam", rt); }
    }
    void ClearSquads() { foreach (var S in squads) foreach (var A in S.members.ToArray()) RemoveActor(A); squads.Clear(); }
    void RemoveActor(Actor A)
    {
        DropHeld(A); if (A.P.grabbed != null) { var H = A.P.grabbed; if (H.P.holdA == A) H.P.holdA = null; }
        actors.Remove(A);
    }
    public void ApplyDensity()
    {
        if (WAVES.on) return;   // in wave mode density scales the next waves instead
        ClearSquads();
        actors.RemoveAll(a => a.team == "enemy");
        if (SET.density > 0) InitSquads();
    }
    void SquadLost(Actor A)
    {
        var S = A.squad; if (S == null) return;
        S.members.Remove(A);
        if (A.brain != null && A.brain.tokT > 0) S.tokN = Math.Max(0, S.tokN - 1);
        if (WAVES.on) { WAVES.kills++; WAVES.score += WAVE_PTS.TryGetValue(A.type, out var pts) ? pts : 50; }
        if (S.members.Count == 0) S.respawnT = 75;
    }
    // ----- hearing: gunfire, explosions, ground pounds, thrown things
    void Noise(float x, float z, float radius, Actor src)
    {
        if (src == null || src.team != "hero") return;
        foreach (var S in squads)
        {
            if (S.members.Count == 0 || S.state == "combat") continue;
            float d = Hypot(S.x - x, S.z - z); if (d > radius + 20) continue;
            bool heard = S.members.Any(A => Hypot(A.P.x - x, A.P.z - z) < radius);
            if (!heard) continue;
            if (d < radius * 0.45f) AlertSquad(S, src, src.P.x, src.P.z);
            else { S.state = "investigate"; S.investigate = (x, z); S.searchT = 9; }
        }
    }
    void AlertSquad(Squad S, Actor T, float x, float z)
    {
        bool fresh = S.state != "combat";
        S.know = new Knowledge { A = T, x = x, z = z, y = T.P.y, t = simT };
        if (fresh)
        {
            S.state = "combat"; S.engageT0 = simT; S.tokN = 0; int nf = 0; int tr = S.members.Count(A => A.type == "normal");
            foreach (var A in S.members)
                if (A.brain != null)
                {
                    A.brain.alertT = 0.9f + R() * 0.9f; A.brain.tokT = 0;
                    A.brain.role = A.type == "normal" && nf < (tr >= 4 ? 2 : tr >= 2 ? 1 : 0) ? (++nf % 2 != 0 ? 1 : -1) : 0;
                }
            var m0 = S.members.FirstOrDefault();
            Sfx("alert", m0?.P.x ?? x, m0 != null ? m0.P.y + 2 : 10, m0?.P.z ?? z, 0.9f);
        }
    }
    void BrainHurt(Actor B, Actor src, float dmg)
    {
        var br = B.brain; if (br == null) return;
        br.hurtT = 1.2f;
        if (B.squad != null && src != null && src.team == "hero") AlertSquad(B.squad, src, src.P.x, src.P.z);
        if (B.team == "hero" && src != null && src.team == "enemy") br.threat = src;
    }
    // ----- individual brains
    Brain MakeBrain(Actor A) => new()
    {
        think = R() * 0.3f, side = R() < 0.5f ? -1 : 1, poundCD = 3 + R() * 3, dashCD = 2 + R() * 3, throwCD = 4 + R() * 4, gx = A.P.x, gz = A.P.z, patT = R(),
        spreadA = (R() * 2 - 1) * 1.25f, spreadR = R() * 6, sep = A.type == "small" ? 3.4f : 2.2f,
    };
    List<Actor> Heroes() => actors.Where(A => A.team == "hero" && A.P.alive && !A.dormant).ToList();
    bool CanSee(Actor A, Actor T)
    {
        var G = A.P; var t = T.P; float dx = t.x - G.x, dz = t.z - G.z, dy = t.y - G.y, d = Hypot(dx, dz);
        var vis = VIS.TryGetValue(A.type, out var v) ? v : VIS["normal"];
        if (d > vis.range || MathF.Abs(dy) > 18) return false;
        if (d > 7) { float c = MathF.Max(dx * -MathF.Sin(G.facing) + dz * -MathF.Cos(G.facing), A.brain != null && A.brain.aiming ? dx * -MathF.Sin(A.IN.aimYaw) + dz * -MathF.Cos(A.IN.aimYaw) : -1) / d; if (c < vis.cos) return false; }
        if (t.rolling && d > vis.range * 0.75f) return false;
        return LosClear(G.x, G.y + G.H * 0.92f, G.z, t.x, t.y + t.H * 0.6f, t.z, 1.5f);
    }
    // steering: walk toward (gx, gz) around static boxes, keeping apart from neighbours
    (float, float) Steer(Actor A, float gx, float gz, float speed, float dt)
    {
        var G = A.P; var B = A.brain;
        float fgx = gx, fgz = gz;
        if (speed > 0.05f) (gx, gz) = NavGoal(A, gx, gz, dt);   // round obstacles by a planned route when the way is blocked
        float dx = gx - G.x, dz = gz - G.z, dl = Hypot(dx, dz);
        if (dl < 0.4f) { if (Hypot(fgx - G.x, fgz - G.z) < 0.4f) return (0, 0); dx = fgx - G.x; dz = fgz - G.z; dl = Hypot(dx, dz); }
        dx /= dl; dz /= dl;
        // probe ahead; swing to the side that is free
        float probe = MathF.Min(dl, 2.2f + Hypot(G.vx, G.vz) * 0.25f), pad = G.R + 0.15f;
        B.avoidT -= dt;
        if (B.avoidT <= 0)
        {
            B.avoid = 0;
            if (InsideStatic(G.x + dx * probe, G.z + dz * probe, G.y, pad) || InsideStatic(G.x + dx * probe * 0.5f, G.z + dz * probe * 0.5f, G.y, pad))
            {
                foreach (float a in new[] { 0.5f, -0.5f, 1.0f, -1.0f, 1.5f, -1.5f, 2.2f, -2.2f })
                {
                    float aa = a * (B.side != 0 ? B.side : 1), c = MathF.Cos(aa), s = MathF.Sin(aa), ex = dx * c - dz * s, ez = dx * s + dz * c;
                    if (!InsideStatic(G.x + ex * probe, G.z + ez * probe, G.y, pad) && !InsideStatic(G.x + ex * probe * 0.5f, G.z + ez * probe * 0.5f, G.y, pad)) { B.avoid = aa; break; }
                }
                B.avoidT = 0.5f;
            }
        }
        if (B.avoid != 0) { float c = MathF.Cos(B.avoid), s = MathF.Sin(B.avoid), ex = dx * c - dz * s, ez = dx * s + dz * c; dx = ex; dz = ez; }
        // separation
        float sx = 0, sz = 0;
        foreach (var O in A.squad != null ? A.squad.members : actors)
        {
            if (O == A || !O.P.alive) continue;
            float ox = G.x - O.P.x, oz = G.z - O.P.z, d = Hypot(ox, oz), r = (G.R + O.P.R) * (B.sep != 0 ? B.sep : 2.2f);
            if (d < r && d > 1e-3f) { sx += ox / d * (r - d) / r; sz += oz / d * (r - d) / r; }
        }
        dx += sx * 0.8f; dz += sz * 0.8f;
        float l = Hypot(dx, dz); if (l == 0) l = 1;
        // stuck: hop, then try the other side for a moment
        float sp = Hypot(G.vx, G.vz);
        if (speed > 0.3f && G.ground && sp < 0.6f && !G.rolling) B.stuckT += dt; else B.stuckT = MathF.Max(0, B.stuckT - dt);
        if (B.stuckT > 0.7f && B.seq == null) { StartSeq(B, "hop"); B.side = -(B.side != 0 ? B.side : 1); B.stuckT = 0; B.avoidT = 0; }
        float k = MathF.Min(1, dl / 1.5f + 0.3f) * speed;
        return (dx / l * k, dz / l * k);
    }
    // timed button sequences: hop, jump-dash, ground pound, roll charge
    static void StartSeq(Brain B, string name) { B.seq = name; B.seqT = 0; }
    static void RunSeq(Actor A, float dt)
    {
        var B = A.brain; var G = A.P; var I = A.IN; if (B.seq == null) return;
        B.seqT += dt; float t = B.seqT;
        switch (B.seq)
        {
            case "hop": I.jump = t < 0.25f; if (t > 0.5f) B.seq = null; break;
            case "dash": I.jump = t < 0.14f || (t > 0.3f && t < 0.4f); if (t > 0.45f) B.seq = null; break;
            case "pound": I.jump = t < 0.3f; if (t > 0.42f && t < 0.5f && !G.ground) I.pound = true; if (t > 0.6f) B.seq = null; break;
            case "roll": I.roll = t < 0.06f || (t > B.rollT && t < B.rollT + 0.06f); if (t > B.rollT + 0.1f) B.seq = null; break;
            case "grab": I.grab = t < 0.05f; if (t > 0.1f) B.seq = null; break;
        }
    }
    float AimAt(Actor A, Actor T, float err, float dt)
    {
        var G = A.P; var B = A.brain; var t = T.P; var def = CurDef(A);
        B.errX += ((R() - 0.5f) * 2 * err - B.errX) * MathF.Min(1, dt * 3); B.errY += ((R() - 0.5f) * err - B.errY) * MathF.Min(1, dt * 3); B.errZ += ((R() - 0.5f) * 2 * err - B.errZ) * MathF.Min(1, dt * 3);
        float ex = G.x, ey = G.y + G.H * 0.82f, ez = G.z, d = Hypot(t.x - ex, t.z - ez);
        float spd = def.Type == "melee" || def.Type == "beam" ? 1e9f : def.Type == "bow" ? def.Speed2 * 0.8f : def.Speed != 0 ? def.Speed : 80, lead = MathF.Min(1.6f, d / spd);
        float ty = def.Proj == "rocket" ? t.y + 0.35f : t.y + t.H * (t.rolling ? 0.3f : 0.6f);
        if (def.Grav != 0) ty += 0.5f * def.Grav * lead * lead;
        float tx = t.x + t.vx * lead + B.errX, tz = t.z + t.vz * lead + B.errZ; ty += B.errY;
        float fx = tx - ex, fy = ty - ey, fz = tz - ez, fl = Hypot(fx, fy, fz); if (fl == 0) fl = 1;
        var AI = A.AIM; AI.px = ex; AI.py = ey; AI.pz = ez; AI.fx = fx / fl; AI.fy = fy / fl; AI.fz = fz / fl; AI.ok = true;
        A.IN.aimYaw = MathF.Atan2(-fx, -fz);
        return d;
    }
    // pick the weapon that suits the range
    void AiPickWeapon(Actor A, float dist, float dt, bool forceMelee = false)
    {
        var B = A.brain; var W = A.W; B.swT -= dt;
        if (B.swT > 0 || W.swingT > 0 || W.charge > 0) return;
        int best = -1; float bs = -1e9f;
        for (int i = 0; i < 3; i++)
        {
            var w = W.slots[i]; if (w == null) continue; var d = WPN[w.id];
            float sc;
            if (d.Type == "melee") sc = forceMelee ? 100 : dist < d.Range + 1.8f ? 50 : -10;
            else
            {
                if (!(w.ammo > 0 || w.clip > 0) || w.over) continue;
                sc = 20 - MathF.Abs(dist - (IDEAL.TryGetValue(w.id, out var id) ? id : 16)) * 0.6f;
                if (d.Range != 0 && dist > d.Range * 1.4f) sc -= 25;
                if (d.Radius != 0 && dist < d.Radius + 2) sc -= 40;
            }
            if (i == W.cur) sc += 4;
            if (sc > bs) { bs = sc; best = i; }
        }
        if (best >= 0 && best != W.cur) { SwitchWeapon(A, best); B.swT = 1.1f; B.burst = 0; B.mark = 0; B.chargeGoal = 0; }
    }
    // pull the trigger the way the weapon wants: bursts, taps, a held charge, or a slow lined-up shot
    bool AiTrigger(Actor A, Actor T, float dist, bool seeIt, float dt)
    {
        var B = A.brain; var W = A.W; var w = CurWpn(A); var def = CurDef(A); var G = A.P;
        B.sightT = 0; B.aimK = 1;
        bool facing = MathF.Abs(WrapAngle(G.facing - A.IN.aimYaw)) < 1.25f;   // the upper body twists to aim
        if (def.Type == "melee") return dist < def.Range + T.P.R + 0.3f && MathF.Abs(WrapAngle(G.facing - A.IN.aimYaw)) < 1;
        if (w == null || W.reload > 0) return false;
        if (def.Type == "bow" || def.Type == "charge")
        {
            if (W.charge > 0 && (!seeIt || W.charge >= B.chargeGoal)) { B.chargeGoal = 0; B.patT = 0.5f + R() * (A.type == "small" ? 1.2f : 0.8f); return false; }   // let go
            if (W.charge > 0) return true;
            if (!seeIt || !facing || B.patT > 0 || (def.Range != 0 && dist > def.Range * 1.25f)) { B.patT -= dt; return false; }
            B.chargeGoal = def.Type == "bow" ? 0.55f + R() * 0.45f : R() < (A.type == "small" ? 0.22f : 0.85f) ? 1 : 0.08f;
            return true;
        }
        if (!seeIt || !facing) { B.burst = 0; B.mark = MathF.Min(B.mark, 0); return false; }
        if (def.Range != 0 && dist > def.Range * 1.25f) return false;
        if (def.Radius != 0 && (dist < def.Radius + 1.5f || FriendNear(A, T.P.x, T.P.z, def.Radius + 1))) return false;
        if (def.AimTime != 0)
        {   // marksmen stand still and line up: a red sight line gives them away
            if (G.vx * G.vx + G.vz * G.vz > 6) { B.mark = MathF.Min(B.mark, 0); return false; }
            B.mark += dt; float need = def.AimTime * 1.7f;
            if (B.mark > 0) { B.sightT = 1 - MathF.Min(1, B.mark / need); B.aimK = Lerp(1, 0.15f, MathF.Min(1, B.mark / need)); }
            if (B.mark >= need && W.cd <= 0) { B.mark = -(0.8f + R() * 1.2f); return true; }
            return false;
        }
        var pat = PATTERN.TryGetValue(w.id, out var pt) ? pt : new[] { 1f, 2, 0.6f, 1.2f }; bool auto = def.Auto || def.Type == "beam";
        if (B.burst > 0)
        {
            if (auto) B.burst -= dt; else if (W.since < dt * 1.5f) B.burst--;
            if (B.burst <= 0) { B.patT = pat[2] + R() * (pat[3] - pat[2]); return false; }
            return true;
        }
        B.patT -= dt;
        if (B.patT > 0) return false;
        B.burst = auto ? pat[0] + R() * (pat[1] - pat[0]) : pat[0] + MathF.Floor(R() * (pat[1] - pat[0] + 1));
        if (A.type == "small" && auto) B.burst *= 1.4f;   // the little ones spray
        return true;
    }
    CoverPt PickCover(Actor A, Actor T, float minD, float maxD, float near, float flank = 0)
    {
        CoverPt best = null; float bs = 1e9f; var G = A.P; var t = T.P; var S = A.squad;
        float want = flank != 0 && S != null ? MathF.Atan2(S.x - t.x, S.z - t.z) + flank * 1.35f : 0;
        foreach (var c in coverPts)
        {
            if (c.by != null && c.by != A && c.by.P.alive) continue;
            float dself = Hypot(c.x - G.x, c.z - G.z); if (dself > near) continue;
            float tx = t.x - c.x, tz = t.z - c.z, dt = Hypot(tx, tz);
            if (dt < minD || dt > maxD) continue;
            if ((tx * c.nx + tz * c.nz) / dt > -0.35f) continue;   // the wall has to be between us
            float sc = dself + MathF.Abs(dt - (minD + maxD) / 2) * 0.4f;
            if (flank != 0) sc += MathF.Abs(WrapAngle(MathF.Atan2(c.x - t.x, c.z - t.z) - want)) * 14;
            if (sc < bs) { bs = sc; best = c; }
        }
        if (best != null) { if (A.brain.cover != null && A.brain.cover.by == A) A.brain.cover.by = null; best.by = A; }
        return best;
    }
    Item NearestAmmo(Actor A, float maxD)
    {
        Item best = null; float bd = maxD; var G = A.P;
        foreach (var it in items) if (it.kind == "ammo" || it.kind == "weapon") { float d = Hypot(it.x - G.x, it.z - G.z); if (d < bd) { bd = d; best = it; } }
        return best;
    }
    // the nearest big man in the squad: where the little ones run to
    static Actor AnchorOf(Actor A)
    {
        Actor best = null; float bd = 45; var G = A.P;
        if (A.squad != null) foreach (var O in A.squad.members) { if (O == A || !O.P.alive || (O.type != "normal" && O.type != "brute")) continue; float d = Hypot(O.P.x - G.x, O.P.z - G.z); if (d < bd) { bd = d; best = O; } }
        return best;
    }
    bool FriendNear(Actor A, float x, float z, float r) { foreach (var O in actors) if (O != A && O.team == A.team && O.P.alive && !O.dormant && MathF.Abs(O.P.x - x) < r && MathF.Abs(O.P.z - z) < r && Hypot(O.P.x - x, O.P.z - z) < r) return true; return false; }
    // attack tokens: only a few men in a squad shoot at once; the rest move, hold and wait their turn
    static bool AiToken(Actor A, float dt)
    {
        var S = A.squad; var B = A.brain; if (S == null) return true;
        if (B.tokT > 0) { B.tokT -= dt; if (B.tokT <= 0) { S.tokN = Math.Max(0, S.tokN - 1); B.tokCD = 1.5f + R() * 2.5f; } return B.tokT > 0; }
        B.tokCD -= dt; if (B.tokCD > 0) return false;
        int max = Math.Max(2, (int)MathF.Round(S.members.Count * 0.38f));
        if (S.tokN < max) { S.tokN++; B.tokT = 2.5f + R() * 2.5f; return true; }
        return false;
    }
    // your shots whizzing past make them keep their heads down
    void SuppressTick()
    {
        for (int i = 0; i < shots.Count; i++)
        {
            var s = shots[i]; if (s.team != "hero" || (stepNo + i) % 3 != 0) continue;
            foreach (var A in actors)
            {
                if (A.team != "enemy" || A.brain == null || A.dormant || !A.P.alive) continue;
                var G = A.P; if (MathF.Abs(G.x - s.x) > 3.5f || MathF.Abs(G.z - s.z) > 3.5f || MathF.Abs(G.y + 1 - s.y) > 3) continue;
                if (!(A.brain.suppT > 0)) A.brain.suppT = 1.4f + R() * 0.6f;
            }
        }
    }
    static float SquadStrength(Squad S) { if (S == null || S.size0 == 0) return 0; int n = S.members.Count(A => A.type != "brute" && A.P.alive); return (float)n / S.size0; }
    // a mech with nothing to throw tears a boulder or a container out of the ground in front of it
    Body MechRipUp(Actor A)
    {
        var G = A.P; float fx = -MathF.Sin(G.facing), fz = -MathF.Cos(G.facing), x = G.x + fx * 3.2f, z = G.z + fz * 3.2f;
        if (InsideStatic(x, z, GroundY(x, z), 2)) return null;
        var b = R() < 0.55f ? MakeProp("boulder", x, GroundY(x, z) - 1.2f, z) : MakeProp("container", x, GroundY(x, z) - 1, z, G.facing + PI / 2);
        b.Prop.kind = "ripped";
        for (int i = 0; i < 10; i++) Puff(x + (R() - 0.5f) * 3, GroundY(x, z) + 0.3f, z + (R() - 0.5f) * 3, 2.5f, 3, 0.4f);
        Sfx("slam", x, G.y, z, 1); Noise(x, z, 60, A);
        return b;
    }
    Body NearProp(Actor A, float maxD, float minM, float maxM)
    {
        Body best = null; float bd = maxD; var G = A.P;
        QueryBodies(G.x - maxD, G.x + maxD, G.z - maxD, G.z + maxD, QPK);
        foreach (var b in QPK) { if (b.Held != null || b.M < minM || b.M > maxM || b.ThrownT > 0 || b.Fixed || b.Actor != null) continue; float d = Hypot(b.Pos.x - G.x, b.Pos.z - G.z); if (d < bd && MathF.Abs(b.Pos.y - G.y) < 1.5f) { bd = d; best = b; } }
        return best;
    }
}

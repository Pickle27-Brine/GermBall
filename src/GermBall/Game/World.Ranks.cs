using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed partial class Actor
{
    /// <summary>Rank in its squad: 0 minor, 1 major, 2 ultra (the squad leader is the highest rank standing).</summary>
    public int rank;
}
public sealed partial class Squad
{
    public Actor leader; public string order = "advance", formation = "wedge"; public float orderT, leaderLostT; public Actor flanker;
}

/// <summary>
/// Ranks and formations, in the spirit of Halo's covenant squads:
/// - every squad has minors, majors and (with a brute or a mech along) an ultra; the highest rank alive leads;
/// - on patrol the squad walks a wedge behind its leader; in a fight it fans into a firing line with the leader held back;
/// - the leader gives orders every few seconds from how the fight is going: push when you're hurt, reloading or exposed,
///   hold and use cover when the squad is thinned out, send a flanker wide round your side otherwise;
/// - kill the leader and morale breaks: the little cocci scatter in panic and the troopers fall back to cover, until
///   the next in rank takes over with a roar.
/// Majors and ultras wear a glowing crest so you can pick out who to shoot first.
/// </summary>
public sealed partial class World
{
    void AssignRanks(Squad S)
    {
        foreach (var A in S.members) A.rank = 0;
        var ultra = S.members.FirstOrDefault(a => a.type == "mech") ?? S.members.FirstOrDefault(a => a.type == "brute");
        if (ultra != null) ultra.rank = 2;
        int majors = 0;
        foreach (var A in S.members.Where(a => a.type == "normal")) if (majors++ < Math.Max(1, S.members.Count(a => a.type == "normal") / 3)) A.rank = 1;
        int sm = 0; foreach (var A in S.members.Where(a => a.type == "small")) if (++sm % 5 == 0) A.rank = 1;
        foreach (var A in S.members.Where(a => a.type == "assassin")) A.rank = 1;
        S.leader = PickLeader(S);
    }
    static Actor PickLeader(Squad S) => S.members.Where(a => a.P.alive && a.type != "assassin").OrderByDescending(a => a.rank).ThenByDescending(a => a.type == "normal" ? 1 : 0).FirstOrDefault();

    void StepRanks(Squad S, float dt)
    {
        if (S.members.Count == 0) return;
        if (S.leader == null && S.leaderLostT <= 0) S.leader = PickLeader(S);
        // the leader went down: morale breaks for a few seconds
        if (S.leader != null && (!S.leader.P.alive || !S.members.Contains(S.leader)))
        {
            S.leader = null; S.leaderLostT = 5 + R() * 2;
            foreach (var A in S.members)
            {
                if (A.brain == null || !A.P.alive) continue;
                if (A.type == "small") { A.brain.fearT = MathF.Max(A.brain.fearT, 3 + R() * 3); A.brain.side = R() < 0.5f ? -1 : 1; }
                else if (A.type == "normal" && A.brain.mode == "fight") { A.brain.mode = "retreat"; A.brain.retreatT = 2.5f + R() * 2; A.brain.retreatTo = null; A.brain.aimK = 1.6f; }
            }
            var c = S.members.FirstOrDefault(a => a.P.alive);
            if (c != null) Sfx("alert", c.P.x, c.P.y + 1, c.P.z, 0.7f);
        }
        if (S.leaderLostT > 0)
        {
            S.leaderLostT -= dt;
            if (S.leaderLostT <= 0)
            {   // the next in line steps up
                var L = PickLeader(S);
                if (L != null) { L.rank = Math.Max(L.rank, 1); S.leader = L; Sfx("roar", L.P.x, L.P.y + 1.5f, L.P.z, 0.6f); foreach (var A in S.members) if (A.brain != null) A.brain.aimK = 1; }
            }
            return;
        }
        S.formation = S.state == "combat" ? "line" : S.state == "investigate" ? "line" : "wedge";
        // orders, in a fight
        if (S.state != "combat" || S.leader == null) { S.order = "advance"; S.flanker = null; return; }
        S.orderT -= dt; if (S.orderT > 0) return;
        S.orderT = 2.5f + R() * 1.8f;
        var T = S.know?.A ?? HUMAN; var t = T.P;
        float strength = SquadStrength(S); var tw = CurWpn(T);
        bool exposed = t.shield < 40 || (T.W.reload > 0) || (tw != null && WPN[tw.id].Energy && tw.over) || !t.alive;
        string prev = S.order;
        S.order = exposed && strength > 0.35f ? "push" : strength < 0.4f ? "hold" : R() < 0.45f ? "flank" : "advance";
        if (S.order == "flank")
        {   // one trooper or assassin swings wide
            S.flanker = S.members.Where(a => a.P.alive && a != S.leader && (a.type == "normal" || a.type == "assassin") && a.P.vehicle == null).OrderBy(_ => R()).FirstOrDefault();
            if (S.flanker?.brain != null) S.flanker.brain.side = R() < 0.5f ? -1 : 1;
        }
        else S.flanker = null;
        if (S.order != prev && S.leader != null && S.order == "push") Sfx("roar", S.leader.P.x, S.leader.P.y + 1.5f, S.leader.P.z, 0.45f);
    }

    /// <summary>How the leader's orders bend a member's goal and pace (applied after the member has picked its own goal).</summary>
    void ApplyOrders(Actor A, Actor T, ref float gx, ref float gz, ref float speed)
    {
        var S = A.squad; if (S == null || S.state != "combat" || T == null || A.P.vehicle != null) return;
        var G = A.P; var t = T.P; float dx = t.x - G.x, dz = t.z - G.z, d = Hypot(dx, dz); if (d < 1e-3f) return; dx /= d; dz /= d;
        if (S.leaderLostT > 0) return;   // no orders while the squad is leaderless
        if (A == S.leader && A.type == "normal")
        {   // leaders hang back behind their men
            gx -= dx * 4; gz -= dz * 4;
        }
        switch (S.order)
        {
            case "push":
                if (A.type is "small" or "normal") { gx = Lerp(gx, t.x - dx * 6, 0.35f); gz = Lerp(gz, t.z - dz * 6, 0.35f); speed = MathF.Min(1, speed * 1.25f + 0.1f); if (A.brain != null) A.brain.aimK = 0.85f; }
                break;
            case "hold":
                speed *= 0.6f;
                break;
            case "flank":
                if (A == S.flanker)
                {
                    float side = A.brain?.side is float sd && sd != 0 ? sd : 1;
                    gx = t.x - dx * 9 + -dz * side * 14; gz = t.z - dz * 9 + dx * side * 14; speed = 1;
                }
                break;
        }
    }

    /// <summary>Formation slots, by formation: a wedge behind the leader on patrol, a firing line in a fight.</summary>
    static (float, float) FormationSlot(Squad S, Actor A, int i, int n, (float, float) basic)
    {
        if (S == null) return basic;
        if (A == S.leader) return S.formation == "wedge" ? (0, 8) : (0, -4);
        if (S.formation == "wedge" && (A.type == "small" || A.type == "normal"))
        {   // a V opening backward from the leader
            int k = (A.type == "normal" ? 0 : 4) + i; int row = k / 2 + 1; float side = (k % 2 == 0) ? -1 : 1;
            return (side * row * 2.4f, 8 - row * 2.6f);
        }
        return basic;
    }

    /// <summary>The crest that marks a major (gold) or an ultra (white-hot).</summary>
    void DrawRank(Actor A)
    {
        if (A.team != "enemy" || A.rank <= 0 || !A.has[0] || A.P.curl > 0.5f) return;
        var ps = A.pose; float s = A.P.s, r = ps[7];
        string key = A.rank >= 2 ? "rankU" : "rankM";
        if (!IM.ContainsKey(key)) MkInst(key, Render.Geo.Octahedron(1, 0), BasicM(A.rank >= 2 ? 0xfff6e0u : 0xffc83au), 64, false);
        float bob = MathF.Sin(clockT * 3 + A.uid) * 0.03f * s;
        InstAdd(key, ps[0] + ps[10] * (ps[8] + r * 0.6f), ps[1] + ps[11] * (ps[8] + r * 0.6f) + r * 0.9f + bob, ps[2] + ps[12] * (ps[8] + r * 0.6f), 0, MathF.Sin(clockT), 0, MathF.Cos(clockT), r * 0.35f, r * 0.55f, r * 0.35f);
    }
}

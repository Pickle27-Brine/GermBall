using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>The briefing card's Debug tab: spawn enemies, vehicles and objects in front of you, tuning, heal.</summary>
public sealed partial class World
{
    (float, float) Ahead(float d) { float f = P.facing; return (P.x - MathF.Sin(f) * d, P.z - MathF.Cos(f) * d); }

    public string DebugEnemy(string k)
    {
        if (k == "clear") { foreach (var S in squads) { foreach (var A in S.members.ToArray()) RemoveActor(A); S.members.Clear(); S.respawnT = 20; } return "All enemies cleared. Squads regroup after a while, away from you."; }
        if (k == "freeze") { aiFrozen = !aiFrozen; return aiFrozen ? "Enemies stand still and hold fire." : "Enemies are active again."; }
        var (x, z) = Ahead(k == "squad" ? 30 : 10);
        if (k == "boss") { var (bx, bz) = Ahead(18); var Bo = SpawnBoss(bx, bz, "debug"); return "A boss: " + TYPES[Bo.type].Label + ". It fights the way you do."; }
        if (k == "dropship") { var (dx, dz) = Ahead(26); MakeSquad(null, "debug", new() { (dx, dz) }, new SquadComp { small = 4, normal = 3, brute = 1 }, (dx, dz)); return "A dropship is inbound with a squad. It can be shot down: it's tough, but every panel comes off."; }
        var comp = k == "squad" ? new SquadComp { small = 6, normal = 4, assassin = 1, brute = 2, chairs = 1 } : k == "chairman" ? new SquadComp { normal = 1, chairs = 1 } : k == "mech" ? new SquadComp { mech = 1 }
            : new SquadComp { small = k == "small" ? 4 : 0, normal = k == "normal" ? 1 : 0, assassin = k == "assassin" ? 1 : 0, brute = k == "brute" ? 1 : 0 };
        var Sq = new Squad { id = ++squadIds, kind = "debug", route = new() { (x, z) }, x = x, z = z, heading = P.facing + PI, state = "patrol", respawnT = 1e9f };
        float fx = MathF.Sin(Sq.heading), fz = MathF.Cos(Sq.heading);
        Sq.size0 = comp.small + comp.normal + comp.assassin;
        foreach (var (type, n) in new[] { ("small", comp.small), ("normal", comp.normal), ("assassin", comp.assassin), ("brute", comp.brute), ("mech", comp.mech) })
            for (int i = 0; i < n; i++)
            {
                var A = MakeActor(type, x, z, false, MathF.Atan2(-fx, -fz)); A.squad = Sq; A.brain = MakeBrain(A); A.slotI = i; A.slotN = n;
                var sl = SlotFor(Sq, A, i, n); A.P.x = x + fz * sl.Item1 + fx * sl.Item2; A.P.z = z - fx * sl.Item1 + fz * sl.Item2; A.P.y = GroundY(A.P.x, A.P.z) + 0.05f; A.P.px = A.P.x; A.P.py = A.P.y; A.P.pz = A.P.z;
                Sq.members.Add(A);
            }
        for (int i = 0; i < comp.chairs; i++) { var Rr = Sq.members.FirstOrDefault(A => A.type == "normal" && A.P.vehicle == null); if (Rr != null) { var C = MakeVehicle("chair", Rr.P.x, Rr.P.z, Rr.P.facing, "enemy"); Mount(Rr, C); Rr.brain.rider = true; } }
        squads.Add(Sq);
        return k == "squad" ? "A squad is forming up ahead of you: cocci in front, amoeba brutes at the back, a bacillus in a hover chair." : k == "chairman" ? "A bacillus in a hover chair, in front of you." : "Spawned " + (comp.small > 1 ? comp.small + " cocci" : "a " + TYPES[k].Label) + " in front of you.";
    }

    public string DebugVehicle(string k)
    {
        var (x, z) = Ahead(k == "rover" || k == "mecha" ? 9 : 5);
        if (k == "mecha") { SpawnMecha(x, z, P.facing); return "A battle mecha, waiting for a pilot. Walk up to it and press interact."; }
        var Vh = MakeVehicle(k, x, z, P.facing, "neutral"); Vh.home = (x, z, P.facing);
        return k switch { "bike" => "A chopper: jump twice to jump-dash, tip it with the stick in the air, and land on its wheels.", "rover" => "A rover: roll button boosts, the trigger fires the turret.", _ => "A hover chair: slides any way you push, the trigger fires the armrest beams." };
    }

    public static readonly (string key, string name)[] DEBUG_OBJECTS = { ("crate", "Spore pod"), ("ball", "Volvox ball"), ("rock", "Calcified nodule"), ("spear", "Stinger"), ("bomb", "Toxin vesicle"), ("block", "Chitin block"), ("steelball", "Pearl"), ("girder", "Bone"), ("ammo", "Ammo"), ("repair", "Repair kit"), ("homing", "Homing dash charges"), ("weapon", "Random weapon") };
    public string DebugObject(string k)
    {
        var (x, z) = Ahead(k == "girder" ? 5 : 4); string name = DEBUG_OBJECTS.First(o => o.key == k).name;
        if (ITEM.ContainsKey(k)) SpawnItem(k, x, GroundY(x, z) + 1.2f, z, vy: 0, life: 120, wid: k == "weapon" ? WPN_IDS[(int)(R() * WPN_IDS.Length)] : null);
        else { var o = PlaceProp(k, x, z, 1.2f); o.Prop.debug = true; o.Home.x = x; o.Home.z = z; }
        return "Dropped " + (k == "homing" ? "homing dash charges" : "a " + name.ToLowerInvariant()) + " in front of you.";
    }

    public string DebugHeal()
    {
        foreach (var A in actors.ToArray()) if (A.team == "hero")
            {
                if (!A.P.alive) RespawnActor(A);
                A.P.shield = 100; for (int i = 0; i < NPART; i++) { A.has[i] = true; A.hp[i] = A.hpMax[i]; }
                foreach (var w in A.W.slots) if (w != null) { w.heat = 0; w.over = false; if (float.IsFinite(w.ammo)) { w.ammo = WPN[w.id].Max; w.clip = WPN[w.id].Clip; } }
                foreach (var a in A.armor) a.hp = a.max;
            }
        return "Body rebuilt, shield, ammo and heat reset.";
    }
    public void ApplyTuningPublic() => ApplyTuning();
    public void ApplyDecorPublic() => ApplyDecor();
}

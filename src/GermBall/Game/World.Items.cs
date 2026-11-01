using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed class Item
{
    public string kind, wid; public float x, y, z, vy, t, life, rot; public float? ammo; public Stash stash; public bool rack;
    public object vis;
    /// <summary>Loose pickups are real rigid bodies: they drop, tumble and come to rest instead of hovering and spinning.</summary>
    public Body body;
}
public sealed class Stash { public float x, y, z, t, every, rot; public string[] kinds; public (string, float)[] pool; public Item item; public bool rack; }

public sealed partial class World
{
    // =====================================================================
    // Pickups: weapons, ammo, repair kits and homing-dash charges.
    // Dropped by enemies, mounted on racks, and refilled at stashes
    // =====================================================================
    public readonly List<Item> items = new(); public readonly List<Stash> stashes = new();
    public static readonly Dictionary<string, (uint color, string label)> ITEM = new()
    {
        ["ammo"] = (0xc6ff4a, "AMMO"), ["repair"] = (0x7dff9c, "REPAIRED"), ["homing"] = (0xff5ce1, "HOMING DASH +3"), ["weapon"] = (0xffe66d, ""),
    };
    public Item SpawnItem(string kind, float x, float y, float z, float vy = 4, string wid = null, float? ammo = null, float life = 45, Stash stash = null, bool rack = false, float rot = 0)
    {
        var it = new Item { kind = kind, x = x, y = y, z = z, vy = vy, t = R() * 6, life = life, stash = stash, wid = wid, ammo = ammo, rack = rack, rot = rot };
        if (stash == null && !rack)
        {
            Body b = kind == "weapon" ? MakeBody(BOXB, x, y, z, 8, new V3(0.11f, 0.09f, 0.42f), e: 0.15f, mu: 0.7f, yaw: R() * TAU)
                : kind == "ammo" ? MakeBody(BOXB, x, y, z, 6, new V3(0.25f, 0.18f, 0.25f), e: 0.15f, mu: 0.8f, yaw: R() * TAU)
                : MakeBody(SPH, x, y, z, 4, r: 0.3f, e: 0.3f, mu: 0.6f, roll: 1.2f);
            b.Vel.Set((R() - 0.5f) * 2, vy, (R() - 0.5f) * 2); b.W.Set((R() - 0.5f) * 6, (R() - 0.5f) * 6, (R() - 0.5f) * 6);
            b.Prop = new Prop { kind = "item" }; b.Tag = it; it.body = b;
        }
        else if (stash != null) it.y = stash.y + 0.35f;   // sitting on its stash, not hovering over it
        items.Add(it); return it;
    }
    void RemoveItem(Item it)
    {
        if (it.stash != null) { it.stash.item = null; it.stash.t = it.stash.every; }
        if (it.body != null) RemoveBody(it.body);
        items.Remove(it);
    }
    void DropLoot(Actor B)
    {
        var G = B.P; var drops = new List<(string, Weapon)>();
        foreach (var w in B.W.slots) if (w != null && R() < 0.8f) drops.Add(("weapon", w));
        if (R() < (B.type == "brute" ? 0.8f : B.type == "normal" ? 0.4f : 0.2f)) drops.Add(("ammo", null));
        if (R() < (B.type == "brute" ? 0.4f : 0.08f)) drops.Add(("repair", null));
        if (R() < (B.type == "brute" ? 0.35f : B.type == "assassin" ? 0.3f : 0.06f)) drops.Add(("homing", null));
        for (int i = 0; i < drops.Count; i++)
        {
            var (k, w) = drops[i];
            SpawnItem(k, G.x + (i - drops.Count / 2f) * 0.7f, G.y + 1, G.z, vy: 5, wid: w?.id, ammo: w != null && float.IsFinite(w.ammo) ? w.ammo + w.clip : null);
        }
    }
    bool GiveItem(Actor A, Item it, bool interact)
    {
        var W = A.W; var P = A.P;
        if (it.kind == "weapon")
        {
            // walking over a weapon you already carry takes its ammo; otherwise it takes a button press
            int have = Array.FindIndex(W.slots, w => w != null && w.id == it.wid);
            if (have >= 0)
            {
                var w = W.slots[have];
                if (!float.IsFinite(w.ammo)) { if (interact) { SwitchWeapon(A, have); return true; } return false; }
                if (w.ammo >= WPN[it.wid].Max && !interact) return false;
                w.ammo = MathF.Min(WPN[it.wid].Max, w.ammo + (it.ammo ?? WPN[it.wid].Ammo)); return true;
            }
            if (!interact) { if (A.team == "enemy" && Array.IndexOf(W.slots, null) >= 0) { GiveWeapon(A, it.wid, it.ammo); return true; } return false; }
            GiveWeapon(A, it.wid, it.ammo); return true;
        }
        if (it.kind == "ammo")
        {
            bool took = false;
            foreach (var w in W.slots) if (w != null && float.IsFinite(w.ammo) && WPN[w.id].Type != "melee" && w.ammo < WPN[w.id].Max) { w.ammo = MathF.Min(WPN[w.id].Max, w.ammo + MathF.Ceiling(WPN[w.id].Ammo * 0.6f)); took = true; }
            return took;
        }
        if (A.team == "enemy") return false;
        if (it.kind == "repair")
        {
            bool need = P.shield < 100; for (int i = 0; i < NPART; i++) if (!A.has[i] || A.hp[i] < A.hpMax[i]) need = true; foreach (var a in A.armor) if (a.hp < a.max) need = true;
            if (!need) return false;
            for (int i = 0; i < NPART; i++) { A.has[i] = true; A.hp[i] = A.hpMax[i]; }
            foreach (var a in A.armor) a.hp = a.max; P.shield = 100; P.STEP = MathF.Max(0.35f, 0.45f * P.s);
        }
        else if (it.kind == "homing") { if (P.homing >= 6) return false; P.homing = Math.Min(6, P.homing + 3); }
        return true;
    }
    static string ItemLabel(Item it) => it.kind == "weapon" ? WPN[it.wid].Name : ITEM[it.kind].label;
    /// <summary>The nearest thing the actor could take with the interact button: an Item, a Vehicle, or a mecha Actor.</summary>
    public object NearestInteract(Actor A, float maxD = 2.3f)
    {
        var G = A.P; object best = null; float bd = maxD;
        foreach (var it in items) { if (it.kind != "weapon") continue; float d = Hypot(it.x - G.x, it.z - G.z); if (d < bd && MathF.Abs(it.y - G.y - 0.6f) < 2) { bd = d; best = it; } }
        foreach (var Vh in vehicles) { if (Vh.dead || Vh.rider != null || Vh.kind == "dropship") continue; float d = Hypot(Vh.x - G.x, Vh.z - G.z) - Vh.def.HitR; if (d < bd) { bd = d; best = Vh; } }
        if (A.human) foreach (var M in actors) { if (M.type != "mecha" || M.pilotBy != null || !M.P.alive || M == A) continue; float d = Hypot(M.P.x - G.x, M.P.z - G.z) - M.P.R - 0.6f; if (d < bd) { bd = d; best = M; } }
        return best;
    }
    bool Interact(Actor A)
    {
        var P = A.P;
        if (A.pilot) { UnboardMecha(A); return true; }
        if (P.vehicle != null) { Dismount(A); return true; }
        var t = NearestInteract(A); if (t == null) return false;
        if (t is Vehicle v) { Mount(A, v); return true; }
        if (t is Actor m) return BoardMecha(A, m);
        var it = (Item)t;
        if (GiveItem(A, it, true)) { if (A.human) { ShowHint(it.kind == "weapon" ? WPN[it.wid].Name : ITEM[it.kind].label); Sfx("pickup", it.x, it.y, it.z); } RemoveItem(it); return true; }
        return false;
    }
    void StepItems(float dt)
    {
        foreach (var s in stashes)
            if (s.item == null)
            {
                s.t -= dt;
                if (s.t <= 0)
                {
                    var k = s.kinds[(int)MathF.Floor(R() * s.kinds.Length)];
                    s.item = k == "weapon" ? SpawnItem("weapon", s.x, s.y, s.z, vy: 0, life: float.PositiveInfinity, stash: s, wid: s.pool != null ? PickW(s.pool) : WPN_IDS[(int)MathF.Floor(R() * WPN_IDS.Length)], rack: s.rack, rot: s.rot)
                        : SpawnItem(k, s.x, s.y, s.z, vy: 0, life: float.PositiveInfinity, stash: s);
                }
            }
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (i >= items.Count) continue;
            var it = items[i]; it.t += dt; it.life -= dt;
            if (it.life <= 0) { RemoveItem(it); continue; }
            if (it.body != null)
            {
                if (it.body.Removed || it.body.Pos.y < -200) { it.body = null; RemoveItem(it); continue; }
                it.x = it.body.Pos.x; it.y = it.body.Pos.y; it.z = it.body.Pos.z;
            }
            if (it.body?.Held != null) continue;   // someone is carrying it to throw
            foreach (var A in actors)
            {
                if (!A.P.alive || A.dormant) continue;
                if (A.team == "enemy" && !(A.brain != null && A.brain.wantAmmo && (it.kind == "ammo" || it.kind == "weapon"))) continue;
                var G = A.P; if (MathF.Abs(G.x - it.x) > 1.8f || MathF.Abs(G.z - it.z) > 1.8f || it.y < G.y - 1 || it.y > G.y + G.H + 0.5f) continue;
                if (Hypot(G.x - it.x, G.z - it.z) > 1.4f + G.R) continue;
                if (GiveItem(A, it, false))
                {
                    if (A.human) { ShowHint(ItemLabel(it) + (it.kind == "weapon" ? " AMMO" : "")); Sfx(it.kind == "homing" ? "powerup" : it.kind == "repair" ? "repair" : "pickup", it.x, it.y, it.z); }
                    else Sfx("pickup", it.x, it.y, it.z, 0.4f);
                    RemoveItem(it); break;
                }
            }
        }
    }
}

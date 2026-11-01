using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>A weapon type. cls: crosshair (0 cross, 1 ring, 2 chevron, 3 melee). type: gun | bow | charge | beam | melee.</summary>
public sealed class WeaponDef
{
    public string Id, Name, Type, Proj, Snd; public int Cls, Pellets, Pierce; public bool Auto, Energy, Beam2, Squash, Sever;
    public float Dmg, Dmg2, Rate, Ammo, Max, Clip, Reload, Spread, Speed, Speed2, Knock, Zoom, AimTime, Range, Radius, Blast, Grav, ChargeT, Heat, Heat2, Home, Life, Dps, Arc, Up;
}
public sealed class Weapon { public string id; public float ammo, clip, heat; public bool over; }
public sealed class BeamFx { public float x0, y0, z0, x1, y1, z1, t; }
public sealed class WState
{
    public Weapon[] slots = new Weapon[3]; public int cur; public float cd, since = 9, aimT; public Actor lockA; public Body lockBody; public float flash, hitT, empty, charge, reload, reloadMax, punchQ, pumpT, switchT; public bool shell; public int pat; public float patT, swingT; public int swingHit;
    public BeamFx beam; public float beamAcc, laserT; public int combo; public float swingDur; public WeaponDef swingDef; public bool trig;
}
public sealed class Shot
{
    public string team, kind; public Actor owner; public WeaponDef def; public float x, y, z, vx, vy, vz, life, dmg, radius, blast, grav, home, age; public int pierce;
    public List<object> hitList; public Actor target; public bool charged;
}
public sealed class BeamLine { public float x0, y0, z0, x1, y1, z1, r, t, life; public uint color; }

public sealed partial class World
{
    static WeaponDef Wd(string id, string name, string type, int cls, Action<WeaponDef> f) { var d = new WeaponDef { Id = id, Name = name, Type = type, Cls = cls }; f(d); return d; }
    public static readonly Dictionary<string, WeaponDef> WPN = new WeaponDef[]
    {
        Wd("pistol", "SPORE PISTOL", "gun", 0, d => { d.Dmg = 9; d.Rate = 5; d.Ammo = 36; d.Max = 72; d.Clip = 12; d.Reload = 1.3f; d.Spread = 0.02f; d.Speed = 120; d.Proj = "bullet"; d.Snd = "shot"; }),
        Wd("revolver", "CYST REVOLVER", "gun", 0, d => { d.Dmg = 24; d.Rate = 1.8f; d.Ammo = 18; d.Max = 36; d.Clip = 6; d.Reload = 1.8f; d.Spread = 0.008f; d.Speed = 150; d.Knock = 3; d.Proj = "bullet"; d.Snd = "boomshot"; }),
        Wd("smg", "CILIA SMG", "gun", 0, d => { d.Auto = true; d.Dmg = 5; d.Rate = 14; d.Ammo = 120; d.Max = 240; d.Clip = 30; d.Reload = 1.5f; d.Spread = 0.07f; d.Speed = 110; d.Proj = "bullet"; d.Snd = "shot"; }),
        Wd("rifle", "FLAGELLUM RIFLE", "gun", 0, d => { d.Auto = true; d.Dmg = 8; d.Rate = 9; d.Ammo = 90; d.Max = 180; d.Clip = 30; d.Reload = 1.8f; d.Spread = 0.028f; d.Speed = 150; d.Proj = "bullet"; d.Snd = "eshot2"; }),
        Wd("hunting", "STALK RIFLE", "gun", 0, d => { d.Dmg = 32; d.Rate = 1.1f; d.Ammo = 16; d.Max = 32; d.Clip = 4; d.Reload = 2.2f; d.Spread = 0.004f; d.Speed = 220; d.Zoom = 0.55f; d.Proj = "bullet"; d.Snd = "boomshot"; d.AimTime = 0.9f; }),
        Wd("sniper", "SPICULE SNIPER", "gun", 0, d => { d.Dmg = 70; d.Rate = 0.6f; d.Ammo = 8; d.Max = 16; d.Clip = 4; d.Reload = 2.6f; d.Spread = 0; d.Speed = 320; d.Zoom = 0.28f; d.Pierce = 2; d.Proj = "bullet"; d.Snd = "boomshot"; d.AimTime = 1.4f; }),
        Wd("shotgun", "SPORE BURSTER", "gun", 1, d => { d.Dmg = 7; d.Pellets = 9; d.Rate = 1.1f; d.Ammo = 18; d.Max = 36; d.Clip = 6; d.Reload = 2.2f; d.Spread = 0.13f; d.Speed = 90; d.Knock = 2; d.Proj = "pellet"; d.Snd = "boomshot"; d.Range = 14; }),
        Wd("rocket", "VESICLE LAUNCHER", "gun", 2, d => { d.Dmg = 70; d.Radius = 6; d.Blast = 1500; d.Rate = 0.8f; d.Ammo = 5; d.Max = 10; d.Clip = 1; d.Reload = 2.2f; d.Spread = 0.01f; d.Speed = 40; d.Proj = "rocket"; d.Snd = "missile"; }),
        Wd("bow", "TENDRIL BOW", "bow", 2, d => { d.Dmg = 16; d.Dmg2 = 50; d.Speed = 26; d.Speed2 = 72; d.Ammo = 20; d.Max = 40; d.Clip = 1; d.Reload = 0.4f; d.Grav = 14; d.ChargeT = 0.8f; d.Proj = "arrow"; d.Snd = "throw"; }),
        Wd("knives", "TOXIN BARBS", "gun", 2, d => { d.Dmg = 26; d.Rate = 3; d.Ammo = 12; d.Max = 24; d.Clip = 3; d.Reload = 0.9f; d.Spread = 0.01f; d.Speed = 46; d.Grav = 10; d.Proj = "knife"; d.Snd = "throw"; }),
        Wd("darts", "NEMATOCYST DARTS", "gun", 0, d => { d.Dmg = 11; d.Rate = 4; d.Ammo = 30; d.Max = 60; d.Clip = 3; d.Reload = 1.0f; d.Spread = 0.015f; d.Speed = 85; d.Grav = 3; d.Proj = "dart"; d.Snd = "dart"; }),
        Wd("needler", "STINGER SWARM", "gun", 1, d => { d.Auto = true; d.Dmg = 4; d.Rate = 9; d.Ammo = 60; d.Max = 120; d.Clip = 20; d.Reload = 2; d.Spread = 0.06f; d.Speed = 45; d.Home = 0.6f; d.Proj = "needle"; d.Snd = "needle"; }),
        Wd("plasma", "PLASMID PISTOL", "charge", 1, d => { d.Energy = true; d.Heat = 0.07f; d.Dmg = 9; d.Dmg2 = 42; d.Rate = 5; d.Speed = 60; d.Speed2 = 45; d.ChargeT = 1.0f; d.Heat2 = 0.55f; d.Proj = "plasma"; d.Snd = "plasma"; }),
        Wd("laserpistol", "BIOLUME PISTOL", "gun", 0, d => { d.Energy = true; d.Heat = 0.065f; d.Dmg = 11; d.Rate = 6; d.Spread = 0.008f; d.Speed = 420; d.Proj = "laser"; d.Snd = "laser"; }),
        Wd("armcannon", "ENZYME BEAM", "beam", 1, d => { d.Energy = true; d.Heat = 0.34f; d.Dps = 55; d.Range = 80; d.Snd = "beam"; }),
        Wd("chargecannon", "MITOCHONDRIA CANNON", "charge", 2, d => { d.Dmg = 18; d.Dmg2 = 130; d.Ammo = 8; d.Max = 16; d.Clip = 1; d.Reload = 0.6f; d.Speed = 260; d.ChargeT = 1.3f; d.Beam2 = true; d.Pierce = 4; d.Proj = "laser"; d.Snd = "zap"; }),
        Wd("bat", "BONE CLUB", "melee", 3, d => { d.Dmg = 22; d.Rate = 1.7f; d.Range = 1.9f; d.Arc = 1.4f; d.Knock = 15; d.Up = 6; d.Snd = "bat"; }),
        Wd("hammer", "SQUASH HAMMER", "melee", 3, d => { d.Dmg = 44; d.Rate = 0.75f; d.Range = 2.1f; d.Arc = 1.2f; d.Knock = 10; d.Up = 9; d.Squash = true; d.Snd = "boing"; }),
        Wd("sledge", "CALCIUM MAUL", "melee", 3, d => { d.Dmg = 36; d.Rate = 0.9f; d.Range = 2.0f; d.Arc = 1.1f; d.Knock = 12; d.Up = 4; d.Sever = true; d.Snd = "slam"; }),
        Wd("knife", "CHITIN SHIV", "melee", 3, d => { d.Dmg = 16; d.Rate = 3; d.Range = 1.3f; d.Arc = 1.0f; d.Knock = 2; d.Up = 0; d.Snd = "punch"; }),
        Wd("hmg", "CILIA GATLING", "gun", 0, d => { d.Auto = true; d.Dmg = 11; d.Rate = 10; d.Ammo = 400; d.Max = 800; d.Clip = 80; d.Reload = 2.4f; d.Spread = 0.055f; d.Speed = 170; d.Knock = 2.5f; d.Proj = "bullet"; d.Snd = "boomshot"; }),
        Wd("mechmg", "WATER-BEAR CHAINGUN", "gun", 0, d => { d.Auto = true; d.Energy = true; d.Heat = 0.022f; d.Dmg = 11; d.Rate = 15; d.Spread = 0.035f; d.Speed = 200; d.Knock = 2; d.Proj = "bullet"; d.Snd = "eshot2"; }),
        Wd("mechhoming", "SEEKER SPORES", "gun", 2, d => { d.Auto = true; d.Energy = true; d.Heat = 0.15f; d.Dmg = 50; d.Radius = 4.5f; d.Blast = 1500; d.Rate = 3.2f; d.Spread = 0.18f; d.Speed = 30; d.Home = 1.8f; d.Life = 4; d.Proj = "rocket"; d.Snd = "missile"; }),
        Wd("mechbomb", "SPORE MORTAR", "gun", 2, d => { d.Auto = true; d.Energy = true; d.Heat = 0.24f; d.Dmg = 70; d.Radius = 6.5f; d.Blast = 2400; d.Rate = 1.3f; d.Spread = 0.02f; d.Speed = 36; d.Grav = 16; d.Life = 5; d.Proj = "rocket"; d.Snd = "boomshot"; }),
        Wd("punch", "PUNCH", "melee", 3, d => { d.Dmg = 15; d.Rate = 3; d.Range = 1.35f; d.Arc = 1.0f; d.Knock = 7; d.Up = 2.5f; d.Snd = "punch"; }),
        Wd("fists", "PSEUDOPODS", "melee", 3, d => { d.Dmg = 8; d.Rate = 2.4f; d.Range = 1.2f; d.Arc = 1.1f; d.Knock = 5; d.Up = 2; d.Snd = "punch"; }),
    }.ToDictionary(d => d.Id);
    public static readonly string[] WPN_IDS = WPN.Keys.Where(k => k != "fists" && k != "punch").ToArray();
    static readonly Dictionary<string, (string, float)[]> LOADOUT = new()
    {
        ["small"] = new[] { ("pistol", 40f), ("smg", 30f), ("plasma", 15f), ("needler", 15f) },
        ["normal"] = new[] { ("rifle", 34f), ("shotgun", 18f), ("hunting", 14f), ("laserpistol", 10f), ("bow", 9f), ("revolver", 8f), ("rocket", 4f), ("sniper", 3f) },
        ["brute"] = new[] { ("hammer", 40f), ("sledge", 40f), ("bat", 20f) },
        ["bruteRanged"] = new[] { ("rocket", 50f), ("chargecannon", 50f) },
        ["assassin"] = new[] { ("darts", 100f) },
    };
    static string PickW((string, float)[] list) { float t = 0; foreach (var (_, w) in list) t += w; float r = R() * t; foreach (var (k, w) in list) { r -= w; if (r <= 0) return k; } return list[0].Item1; }
    static Weapon MakeWeapon(string id, float? ammo)
    {
        var d = WPN[id];
        if (d.Energy || d.Type == "melee" || d.Type == "beam") return new Weapon { id = id, ammo = float.PositiveInfinity, clip = d.Clip };
        // a found weapon comes loaded: a magazine in it, the rest stored
        float total = ammo ?? d.Ammo + d.Clip, inMag = MathF.Min(d.Clip, total);
        return new Weapon { id = id, ammo = MathF.Min(d.Max, total - inMag), clip = inMag };
    }
    static WState MakeW() => new();
    static Weapon CurWpn(Actor A) => A.W.slots[A.W.cur];
    static WeaponDef CurDef(Actor A) { var w = CurWpn(A); return w != null ? WPN[w.id] : WPN["fists"]; }
    public int GiveWeapon(Actor A, string id, float? ammo = null, bool select = true)
    {
        var W = A.W;
        for (int i = 0; i < 3; i++) if (W.slots[i] != null && W.slots[i].id == id) { if (float.IsFinite(W.slots[i].ammo)) W.slots[i].ammo = MathF.Min(WPN[id].Max, W.slots[i].ammo + (ammo ?? WPN[id].Ammo)); return i; }
        int s = Array.IndexOf(W.slots, null);
        if (s < 0) { s = W.cur; DropWeapon(A, s); }
        W.slots[s] = MakeWeapon(id, ammo); if (select) { W.cur = s; W.cd = MathF.Max(W.cd, 0.25f); W.charge = 0; }
        return s;
    }
    void DropWeapon(Actor A, int i)
    {
        var w = A.W.slots[i]; if (w == null) return;
        A.W.slots[i] = null;
        var G = A.P; SpawnItem("weapon", G.x - MathF.Sin(G.facing) * 0.8f, G.y + G.H * 0.6f, G.z - MathF.Cos(G.facing) * 0.8f, vy: 3, wid: w.id, ammo: (float.IsFinite(w.ammo) ? w.ammo : 0) + w.clip, life: 60);
    }
    static bool HasAmmo(Actor A) { foreach (var w in A.W.slots) if (w != null && WPN[w.id].Type != "melee" && (w.ammo > 0 || w.clip > 0)) return true; return false; }
    static int RangedSlot(Actor A) { for (int i = 0; i < 3; i++) { var w = A.W.slots[i]; if (w != null && WPN[w.id].Type != "melee" && (w.ammo > 0 || w.clip > 0)) return i; } return -1; }
    static int MeleeSlot(Actor A) { for (int i = 0; i < 3; i++) { var w = A.W.slots[i]; if (w != null && WPN[w.id].Type == "melee") return i; } return -1; }

    public readonly List<Shot> shots = new(); public readonly List<BeamLine> beams = new();
    readonly List<Static> QHs = new(); readonly List<Body> QHb = new();
    readonly V3 MUZ = new(), IMP = new();
    Static SOLID_HIT;   // the static block SolidAt last hit
    /// <summary>A marker meaning "the world" (terrain or statics) was hit.</summary>
    public static readonly object SOLID_WORLD = new();
    /// <summary>null: empty; SOLID_WORLD: terrain or statics; otherwise the Body that was hit (ign: skip things this actor holds).</summary>
    object SolidAt(float x, float y, float z, Actor ign = null)
    {
        SOLID_HIT = null;
        if (y <= TerrainH(x, z)) return SOLID_WORLD;
        QueryStatics(x - 0.2f, x + 0.2f, z - 0.2f, z + 0.2f, QHs);
        foreach (var o in QHs)
        {
            if (o.Kind == BOXK) { if (x > o.MinX && x < o.MaxX && y > o.MinY && y < o.MaxY && z > o.MinZ && z < o.MaxZ) { SOLID_HIT = o; return SOLID_WORLD; } }
            else if (o.Kind == CYLK) { if (y > o.Bottom && y < o.Top && Hypot(x - o.X, z - o.Z) < o.R) return SOLID_WORLD; }
            else if (x > o.MinX && x < o.MaxX && z > o.MinZ && z < o.MaxZ && y > o.MinY && y < o.Fn(x, z)) return SOLID_WORLD;
        }
        QueryBodies(x - 0.1f, x + 0.1f, z - 0.1f, z + 0.1f, QHb);
        foreach (var b in QHb)
        {
            if (ign != null && b.Held == ign) continue;
            if (b.RagdollOf != null || b.Actor != null) continue;
            float m = b.Bound + 0.1f;
            if (MathF.Abs(x - b.Pos.x) > m || MathF.Abs(y - b.Pos.y) > m || MathF.Abs(z - b.Pos.z) > m) continue;
            if (b.Kind == SPH) { if (Hypot(x - b.Pos.x, y - b.Pos.y, z - b.Pos.z) < b.R + 0.08f) return b; }
            else
            {
                LOC.x = x - b.Pos.x; LOC.y = y - b.Pos.y; LOC.z = z - b.Pos.z; QRot(b.Q, LOC, LOC, true);
                if (MathF.Abs(LOC.x) < b.He.x + 0.08f && MathF.Abs(LOC.y) < b.He.y + 0.08f && MathF.Abs(LOC.z) < b.He.z + 0.08f) return b;
            }
        }
        return null;
    }
    bool WorldBlocked(float x, float y, float z)
    {   // statics and terrain only (cheap line of sight)
        if (y <= TerrainH(x, z)) return true;
        QueryStatics(x - 0.1f, x + 0.1f, z - 0.1f, z + 0.1f, QHs);
        foreach (var o in QHs) if (o.Kind == BOXK && x > o.MinX && x < o.MaxX && y > o.MinY && y < o.MaxY && z > o.MinZ && z < o.MaxZ) return true;
        return false;
    }
    bool LosClear(float x0, float y0, float z0, float x1, float y1, float z1, float step = 1)
    {
        float dx = x1 - x0, dy = y1 - y0, dz = z1 - z0, L = Hypot(dx, dy, dz); int n = (int)MathF.Ceiling(L / step);
        for (int i = 1; i < n; i++) { float u = (float)i / n; if (WorldBlocked(x0 + dx * u, y0 + dy * u, z0 + dz * u)) return false; }
        return true;
    }
    // ray / segment against the round parts of men (capsules along each part's long axis)
    struct HitQ { public Actor A; public int part; public float t; public Vehicle veh; public float hx, hy, hz; }
    HitQ HITQ; float SSs;
    float SegSegT(float px, float py, float pz, float ux, float uy, float uz, float qx, float qy, float qz, float vx, float vy, float vz)
    {
        float wx = px - qx, wy = py - qy, wz = pz - qz, a = ux * ux + uy * uy + uz * uz, b = ux * vx + uy * vy + uz * vz, c = vx * vx + vy * vy + vz * vz, d = ux * wx + uy * wy + uz * wz, e = vx * wx + vy * wy + vz * wz;
        float D = a * c - b * b, s, t;
        if (c < 1e-9f) { s = a > 1e-9f ? Clamp(-d / a, 0, 1) : 0; t = 0; }
        else if (D < 1e-9f) { s = 0; t = Clamp(e / c, 0, 1); }
        else { s = Clamp((b * e - c * d) / D, 0, 1); t = c > 1e-9f ? Clamp((b * s + e) / c, 0, 1) : 0; s = a > 1e-9f ? Clamp((b * t - d) / a, 0, 1) : 0; }
        float dx = wx + ux * s - vx * t, dy = wy + uy * s - vy * t, dz = wz + uz * s - vz * t;
        SSs = s; return dx * dx + dy * dy + dz * dz;
    }
    /// <summary>The first man or machine (not on "team") the segment passes through; details in HITQ.</summary>
    object HitMen(float x0, float y0, float z0, float x1, float y1, float z1, string team, float pad = 0, List<object> skip = null)
    {
        HITQ = new HitQ { part = -1, t = 2 };
        float ux = x1 - x0, uy = y1 - y0, uz = z1 - z0;
        foreach (var B in actors)
        {
            if (!B.P.alive || B.team == team || B.dormant || (skip != null && skip.Contains(B))) continue;
            var G = B.P; float cx = G.x, cy = G.y + G.H * 0.5f, cz = G.z, rr = G.H * 0.7f + pad;
            if (SegSegT(x0, y0, z0, ux, uy, uz, cx, cy, cz, 0, 0, 0) > rr * rr) continue;
            if (B.poseT < 0) PosePartsNow(B);
            var ps = B.pose;
            for (int i = 0; i < NPART; i++)
            {
                if (!B.has[i]) continue;
                int k = i * 13; float r = (ps[k + 7] + ps[k + 9]) * 0.5f + pad, h = MathF.Max(0, ps[k + 8] - r);
                float ax = ps[k] - ps[k + 10] * h, ay = ps[k + 1] - ps[k + 11] * h, az = ps[k + 2] - ps[k + 12] * h;
                float d2 = SegSegT(x0, y0, z0, ux, uy, uz, ax, ay, az, ps[k + 10] * 2 * h, ps[k + 11] * 2 * h, ps[k + 12] * 2 * h);
                if (d2 < r * r * 1.1f && SSs < HITQ.t) { HITQ.t = SSs; HITQ.A = B; HITQ.part = i; }
            }
        }
        // vehicles: a capsule along each machine's length
        foreach (var Vh in vehicles)
        {
            if (Vh.dead || (Vh.rider != null && Vh.rider.team == team) || (Vh.team == team && Vh.rider == null)) continue;
            var D = Vh.def; var f = Vfwd(Vh); float L = D.HitL, cy = Vh.y + D.HitY;
            float d2 = SegSegT(x0, y0, z0, ux, uy, uz, Vh.x - f.x * L, cy - f.y * L, Vh.z - f.z * L, f.x * 2 * L, f.y * 2 * L, f.z * 2 * L);
            float r = D.HitR + pad;
            if (d2 < r * r && SSs < HITQ.t) { HITQ.t = SSs; HITQ.A = null; HITQ.part = -1; HITQ.veh = Vh; }
        }
        if (HITQ.veh != null) { HITQ.hx = x0 + ux * HITQ.t; HITQ.hy = y0 + uy * HITQ.t; HITQ.hz = z0 + uz * HITQ.t; }
        return (object)HITQ.A ?? HITQ.veh;
    }
    float AimDistance(float maxD)
    {
        float d = 0.6f;
        while (d < maxD)
        {
            if (SolidAt(AIM.px + AIM.fx * d, AIM.py + AIM.fy * d, AIM.pz + AIM.fz * d, CUR) != null) break;
            d += d < 30 ? 0.4f : d < 90 ? 0.9f : 1.8f;
        }
        d = MathF.Min(d, maxD);
        if (HitMen(AIM.px + AIM.fx * 0.6f, AIM.py + AIM.fy * 0.6f, AIM.pz + AIM.fz * 0.6f, AIM.px + AIM.fx * d, AIM.py + AIM.fy * d, AIM.pz + AIM.fz * d, CUR != null ? CUR.team : "hero", 0.05f) != null) d = 0.6f + (d - 0.6f) * HITQ.t;
        return d;
    }
    V3 Muzzle(V3 o)
    {
        var A = CUR; int k = PART.handR * 13; var ps = A.pose;
        if (A.poseT >= 0 && A.has[PART.handR] && !P.rolling) { o.x = ps[k] + P.vx * 0.016f; o.y = ps[k + 1]; o.z = ps[k + 2] + P.vz * 0.016f; return o; }
        float f = P.facing, fx = -MathF.Sin(f), fz = -MathF.Cos(f), rx = MathF.Cos(f), rz = -MathF.Sin(f);
        o.x = P.x + rx * 0.25f * P.s + fx * 0.5f * P.s; o.y = P.y + P.H * (P.rolling ? 0.5f : 0.75f); o.z = P.z + rz * 0.25f * P.s + fz * 0.5f * P.s;
        return o;
    }
    Actor FindLock()
    {
        if (!AIM.ok) return null;
        Actor best = null; float bestA = 0.3f;
        foreach (var B in actors)
        {
            if (!B.P.alive || B.team == CUR.team || B.dormant) continue;
            float dx = B.P.x - AIM.px, dy = B.P.y + B.P.H * 0.6f - AIM.py, dz = B.P.z - AIM.pz, d = Hypot(dx, dy, dz);
            if (d < 3 || d > 140) continue;
            float a = MathF.Acos(Clamp((dx * AIM.fx + dy * AIM.fy + dz * AIM.fz) / d, -1, 1));
            if (a < bestA) { bestA = a; best = B; }
        }
        return best;
    }
    // the direction the current actor's shot leaves in: through the crosshair for people, along AIM for brains
    readonly V3 SDIR = new();
    V3 ShotDir()
    {
        Muzzle(MUZ);
        float dx = AIM.ok ? AIM.fx : -MathF.Sin(P.facing), dy = AIM.ok ? AIM.fy : 0, dz = AIM.ok ? AIM.fz : -MathF.Cos(P.facing);
        if (CUR.human && AIM.ok)
        {
            float d = AimDistance(260), tx = AIM.px + AIM.fx * d, ty = AIM.py + AIM.fy * d, tz = AIM.pz + AIM.fz * d;
            float ex = tx - MUZ.x, ey = ty - MUZ.y, ez = tz - MUZ.z, l = Hypot(ex, ey, ez); if (l == 0) l = 1; ex /= l; ey /= l; ez /= l;
            if (ex * AIM.fx + ey * AIM.fy + ez * AIM.fz > 0.3f) { dx = ex; dy = ey; dz = ez; }
        }
        SDIR.x = dx; SDIR.y = dy; SDIR.z = dz; return SDIR;
    }
    void SpawnShot(WeaponDef def, string proj, float dx, float dy, float dz, float speed, float dmg, bool charged = false, float? home = null, Actor target = null)
    {
        shots.Add(new Shot
        {
            team = CUR.team, owner = CUR, kind = proj, def = def, x = MUZ.x, y = MUZ.y, z = MUZ.z, vx = dx * speed + P.vx * 0.3f, vy = dy * speed, vz = dz * speed + P.vz * 0.3f,
            life = def.Life != 0 ? def.Life : def.Grav != 0 ? 4 : 2.2f, dmg = dmg, radius = def.Radius, blast = def.Blast, grav = def.Grav, home = home ?? def.Home, pierce = def.Pierce, target = target, charged = charged,
        });
    }
    static (float, float, float) SpreadDir(V3 d, float spread)
    {
        float dx = d.x + (R() - 0.5f) * spread, dy = d.y + (R() - 0.5f) * spread * 0.7f, dz = d.z + (R() - 0.5f) * spread, l = Hypot(dx, dy, dz); if (l == 0) l = 1;
        return (dx / l, dy / l, dz / l);
    }
    // one trigger pull of the current weapon (charge: 0..1 for bows and charged shots)
    void FireWeapon(WeaponDef def, Weapon w, float charge)
    {
        var A = CUR; var d = ShotDir(); bool enemy = A.team == "enemy";
        RecoilKick(A, def);
        float sp = def.Spread * (enemy ? (A.brain != null && A.type == "small" ? 2.2f : 1.2f) : 1) * (P.ads ? 0.5f : 1);
        if (def.Type == "bow")
        {
            float s = Lerp(def.Speed, def.Speed2, charge), dm = Lerp(def.Dmg, def.Dmg2, charge); var (x, y, z) = SpreadDir(d, 0.01f + 0.03f * (1 - charge));
            SpawnShot(def, "arrow", x, y + 0.02f, z, s, dm);
        }
        else if (def.Type == "charge" && charge >= 0.98f)
        {
            if (def.Beam2) FireBeamShot(def, d, def.Dmg2, true);
            else { var (x, y, z) = SpreadDir(d, 0.01f); SpawnShot(def, def.Proj, x, y, z, def.Speed2, def.Dmg2, true, 1.2f, A.human ? FindLock() : A.brain?.tgtA); }
        }
        else if (def.Type == "charge" && def.Beam2) { var (x, y, z) = SpreadDir(d, 0.01f); SpawnShot(def, def.Proj, x, y, z, def.Speed, def.Dmg); }
        else
        {
            int n = def.Pellets != 0 ? def.Pellets : 1;
            for (int k = 0; k < n; k++) { var (x, y, z) = SpreadDir(d, sp); SpawnShot(def, def.Proj, x, y, z, def.Speed, def.Dmg, false, null, def.Home != 0 ? (A.human ? FindLock() : A.brain?.tgtA) : null); }
        }
        W.flash = 1; A.anim.aimK = 1;
        if (def.Type != "bow") MuzzleFlash(MUZ.x + d.x * 0.45f * P.s, MUZ.y + d.y * 0.45f * P.s, MUZ.z + d.z * 0.45f * P.s, def.Dmg > 30 || def.Pellets != 0 ? 1.6f : def.Energy ? 0.8f : 1);
        Sfx(def.Snd ?? "shot", MUZ.x, MUZ.y, MUZ.z, enemy ? 0.7f : 1);
        Noise(MUZ.x, MUZ.z, enemy ? 25 : def.Proj == "dart" || def.Proj == "arrow" ? 15 : 45, A);
        if (A.human) Rumble(def.Dmg > 20 ? 0.5f : 0.08f, def.Dmg > 20 ? 0.8f : 0.3f, def.Dmg > 20 ? 120 : 40);
        if (def.Knock != 0 && def.Proj != "arrow") { P.vx -= d.x * def.Knock * 0.4f; P.vz -= d.z * def.Knock * 0.4f; }
        if (def.Proj == "rocket") { P.vx -= d.x * 1.5f; P.vz -= d.z * 1.5f; }
    }
    // hitscan laser for the charged cannon: pierces a few men, stops at walls
    void FireBeamShot(WeaponDef def, V3 d, float dmg, bool big)
    {
        var A = CUR; float x = MUZ.x, y = MUZ.y, z = MUZ.z, end = 120;
        for (float t = 0.5f; t < 120; t += 0.6f) if (WorldBlocked(x + d.x * t, y + d.y * t, z + d.z * t)) { end = t; break; }
        var skip = new List<object>();
        for (int n = 0; n < Math.Max(1, def.Pierce); n++)
        {
            var who = HitMen(x, y, z, x + d.x * end, y + d.y * end, z + d.z * end, A.team, 0.12f, skip);
            if (who == null) break;
            if (HITQ.veh != null) { DamageVehicle(HITQ.veh, dmg, A, HITQ.hx, HITQ.hy, HITQ.hz); break; }
            skip.Add(who);
            float hx = x + d.x * end * HITQ.t, hy = y + d.y * end * HITQ.t, hz = z + d.z * end * HITQ.t;
            DamageActor((Actor)who, dmg, new Dmg { src = A, kind = "beam", part = HITQ.part, fx = d.x, fz = d.z, knock = big ? 9 : 2, up = big ? 3 : 0 }.Hit(hx, hy, hz));
        }
        AddBeam(x, y, z, x + d.x * end, y + d.y * end, z + d.z * end, big ? 0.22f : 0.06f, A.team == "hero" ? 0x5ff2ffu : 0xff3048u, big ? 0.35f : 0.08f);
        if (big) { Sfx("zapbig", x, y, z); if (A.human) cam.shake = MathF.Max(cam.shake, 0.3f); }
    }
    void AddBeam(float x0, float y0, float z0, float x1, float y1, float z1, float r, uint color, float life) => beams.Add(new BeamLine { x0 = x0, y0 = y0, z0 = z0, x1 = x1, y1 = y1, z1 = z1, r = r, color = color, life = life });
    // continuous laser (arm cannon): a hitscan every step, damage applied in small ticks
    void BeamTick(WeaponDef def, float dt)
    {
        var A = CUR; var d = ShotDir(); float end = def.Range;
        for (float t = 0.5f; t < def.Range; t += 0.5f) if (SolidAt(MUZ.x + d.x * t, MUZ.y + d.y * t, MUZ.z + d.z * t, A) != null) { end = t; break; }
        var who = HitMen(MUZ.x, MUZ.y, MUZ.z, MUZ.x + d.x * end, MUZ.y + d.y * end, MUZ.z + d.z * end, A.team, 0.05f);
        if (who != null) end *= HITQ.t;
        W.beam = new BeamFx { x0 = MUZ.x, y0 = MUZ.y, z0 = MUZ.z, x1 = MUZ.x + d.x * end, y1 = MUZ.y + d.y * end, z1 = MUZ.z + d.z * end, t = 0.06f };
        W.beamAcc += dt;
        if (W.beamAcc >= 0.1f)
        {
            float k = W.beamAcc; W.beamAcc = 0;
            if (who != null && HITQ.veh != null) DamageVehicle(HITQ.veh, def.Dps * k, A);
            else if (who is Actor wa) DamageActor(wa, def.Dps * k, new Dmg { src = A, kind = "beam", part = HITQ.part, knock = 0.5f, fx = d.x, fz = d.z }.Hit(W.beam.x1, W.beam.y1, W.beam.z1));
            else if (SolidAt(W.beam.x1, W.beam.y1, W.beam.z1) is Body b) { Wake(b); IMP.Set(0, 0, 0); ApplyImpulse(b, d.x * 300 * k, d.y * 300 * k, d.z * 300 * k, IMP); if (b.Bomb && !(b.Fuse > 0)) b.Fuse = 0.3f; }
            if (R() < 0.5f) Puff(W.beam.x1, W.beam.y1, W.beam.z1, 1.5f, 1.5f, 0.07f);
            Sfx("beam", MUZ.x, MUZ.y, MUZ.z, 0.5f);
        }
    }

    // melee: swings come in three-hit combos. Each has a wind-up, a strike that lands partway through and a follow-through;
    // the third is a finisher. The swing steps you in toward the nearest enemy in front, and a landed hit freezes the action
    // for an instant so it feels solid.
    sealed record MeleeFeel(float Dur, string[] Combo, float Lunge, float Stop);
    static readonly Dictionary<string, MeleeFeel> MELEE_FEEL = new()
    {
        ["fists"] = new(0.26f, new[] { "jab", "hook", "upper" }, 3, 0.04f), ["punch"] = new(0.22f, new[] { "jab", "hook", "upper" }, 3.5f, 0.05f), ["knife"] = new(0.24f, new[] { "slash", "slash2", "stab" }, 4.5f, 0.04f),
        ["bat"] = new(0.38f, new[] { "sweep", "sweep2", "chop" }, 3.5f, 0.07f), ["hammer"] = new(0.52f, new[] { "chop", "sweep", "slam" }, 2.6f, 0.12f),
        ["sledge"] = new(0.5f, new[] { "chop", "sweep", "slam" }, 2.6f, 0.1f),
    };
    static MeleeFeel GetFeel(WeaponDef def) => MELEE_FEEL.TryGetValue(def.Id, out var f) ? f : MELEE_FEEL["fists"];
    public float HITSTOP;
    void MeleeSwing(WeaponDef def)
    {
        var F = GetFeel(def); var A = CUR;
        W.combo = W.since < F.Dur + 0.5f && W.swingDef == def ? (W.combo + 1) % F.Combo.Length : 0;
        bool fin = W.combo == F.Combo.Length - 1; float dur = F.Dur * (fin ? 1.25f : 1);
        W.swingT = dur; W.swingDur = dur; W.swingHit = 0; W.swingDef = def; W.since = 0;
        A.anim.swingT = dur; A.animSwingDur = dur; A.animSwingStyle = F.Combo[W.combo]; A.animSwingFin = fin;
        // soft lock: turn to the nearest enemy in front and step in
        float reach = def.Range * (0.8f + 0.2f * P.s) + P.R + 3;
        Actor T = null; float bd = reach;
        foreach (var B in actors)
        {
            if (B == A || !B.P.alive || B.team == A.team || B.dormant) continue;
            float dx = B.P.x - P.x, dz = B.P.z - P.z, d = Hypot(dx, dz);
            if (d < bd && (dx * -MathF.Sin(P.facing) + dz * -MathF.Cos(P.facing)) / (d == 0 ? 1 : d) > 0.25f && MathF.Abs(B.P.y - P.y) < 2) { bd = d; T = B; }
        }
        if (T != null) P.facing = MathF.Atan2(-(T.P.x - P.x), -(T.P.z - P.z));
        if (P.ground) { float k = F.Lunge * (fin ? 1.4f : 1) * (T != null ? MathF.Min(1, (bd - 1) / 2.5f) : 0.5f); P.vx += -MathF.Sin(P.facing) * k; P.vz += -MathF.Cos(P.facing) * k; }
        if (fin && A.animSwingStyle == "slam" && P.ground) { P.vy = 4.5f; P.ground = false; }   // the hammer finisher hops up to slam down
        Sfx("whoosh", P.x, P.y + 1, P.z, fin ? 0.9f : 0.6f);
    }
    void MeleeResolve(WeaponDef def)
    {
        var A = CUR; float fx = -MathF.Sin(P.facing), fz = -MathF.Cos(P.facing); var F = GetFeel(def); bool fin = A.animSwingFin; string style = A.animSwingStyle;
        float reach = def.Range * (0.8f + 0.2f * P.s) * (fin ? 1.2f : 1) + P.R, arc = style == "sweep" || style == "sweep2" || style == "slash" || style == "slash2" ? def.Arc * 1.35f : def.Arc;
        float dmgK = fin ? 1.6f : 1, knockK = fin ? 1.5f : 0.8f;
        bool any = false;
        foreach (var B in actors.ToArray())
        {
            if (B == A || !B.P.alive || B.team == A.team || B.dormant) continue;
            var G = B.P; float dx = G.x - P.x, dz = G.z - P.z, d = Hypot(dx, dz) - G.R;
            if (d > reach || MathF.Abs(G.y - P.y) > P.H * 0.9f) continue;
            float hd = Hypot(dx, dz); if (hd == 0) hd = 1;
            if ((dx * fx + dz * fz) / hd < MathF.Cos(arc)) continue;
            bool head = R() < (def.Squash ? 0.5f : style == "upper" || style == "chop" ? 0.35f : 0.2f), heavy = B.type == "brute" || TYPES[B.type].Big || TYPES[B.type].Boss;
            // swings push sideways the way they travel; uppercuts and slams lift
            float side = style == "sweep" || style == "slash" || style == "hook" ? 1 : style == "sweep2" || style == "slash2" ? -1 : 0;
            float kx = dx / hd + fz * side * 0.8f, kz = dz / hd - fx * side * 0.8f;
            if (def.Squash)
            {
                // the cartoon hammer: no knock back at all. It flattens them into a pancake where they stand
                DamageActor(B, def.Dmg * dmgK, new Dmg { src = A, kind = "melee", part = head && B.has[0] ? 0 : -1, fx = dx, fz = dz });
                if (B.P.alive) Flatten(B, fin ? 3.2f : 2.4f);
                if (A == ME) StyleEvent("pancake", B);
            }
            else
            {
                DamageActor(B, def.Dmg * dmgK, new Dmg { src = A, kind = "melee", part = head && B.has[0] ? 0 : -1, fx = kx, fz = kz, knock = def.Knock * knockK * (heavy ? 0.4f : 1), up = (def.Up + (style == "upper" || style == "slam" ? 6 : 0)) * (heavy ? 0.4f : 1) });
                if ((def.Knock >= 10 || fin) && !heavy && B.P.alive) Knockdown(B, fin ? 1.4f : 1.1f, kx * def.Knock * knockK, def.Up + (fin ? 4 : 0), kz * def.Knock * knockK);
            }
            for (int i = 0; i < NPART; i++) B.flash[i] = MathF.Max(B.flash[i], 0.06f);
            HitSpark(G.x, G.y + G.H * 0.6f, G.z, fin ? 2 : 1.2f);
            if (A == ME || B == ME || A.human) HITSTOP = MathF.Max(HITSTOP, F.Stop * (fin ? 1.8f : 1));
            any = true;
        }
        foreach (var Vh in vehicles.ToArray()) { if (Vh.dead || Vh.rider == A) continue; if (Hypot(Vh.x - P.x, Vh.z - P.z) < reach + 0.8f) { DamageVehicle(Vh, def.Dmg, A); any = true; } }
        QueryBodies(P.x + fx * reach * 0.6f - reach, P.x + fx * reach * 0.6f + reach, P.z + fz * reach * 0.6f - reach, P.z + fz * reach * 0.6f + reach, QPK);
        foreach (var b in QPK)
        {
            if (b.Held != null || b.Actor != null) continue; float dx = b.Pos.x - P.x, dz = b.Pos.z - P.z, d = Hypot(dx, dz);
            if (d > reach + b.Bound || (dx * fx + dz * fz) / (d == 0 ? 1 : d) < 0.2f) continue;
            Wake(b); IMP.Set(0, 0, 0); float f = def.Knock * 30 * MathF.Min(1, b.M / 30 + 0.2f);
            ApplyImpulse(b, fx * f, f * 0.4f, fz * f, IMP); b.ThrownT = 1.2f; b.ThrownBy = A; any = true;
            if (b.Bomb && !(b.Fuse > 0)) b.Fuse = 0.8f;
        }
        if (def.Dmg >= 20 || A.type == "brute" || A.type == "mech") BashAt(P.x + fx * reach, P.y + P.H * 0.5f, P.z + fz * reach, def.Dmg * (def.Sever || def.Squash ? 4 : 2) * (A.type == "mech" ? 3 : 1), fx, fz);
        if (def.Squash || style == "slam")
        {
            for (int i = 0; i < (fin ? 14 : 6); i++) Puff(P.x + fx * reach, P.y + 0.2f, P.z + fz * reach, fin ? 6 : 4, 1.5f, 0.18f);
            // a finishing slam sends a shock ring out: anyone nearby is flattened (hammer) or bounced (sledge)
            if (fin)
                foreach (var B in actors.ToArray())
                {
                    if (B == A || !B.P.alive || B.team == A.team || B.dormant) continue;
                    float cx = P.x + fx * reach, cz = P.z + fz * reach, d = Hypot(B.P.x - cx, B.P.z - cz);
                    if (d > 4.2f || MathF.Abs(B.P.y - P.y) > 1.5f) continue;
                    DamageActor(B, def.Dmg * 0.5f, new Dmg { src = A, kind = "blast", fx = B.P.x - cx, fz = B.P.z - cz, knock = def.Squash ? 0 : 6, up = def.Squash ? 0 : 7 });
                    if (def.Squash && B.P.alive) Flatten(B, 2.2f);
                }
        }
        if (any) { Sfx(def.Snd, P.x + fx, P.y + 1, P.z + fz, fin ? 1 : 0.8f); if (A.human) { Rumble(fin ? 1 : 0.7f, 1, fin ? 260 : 140); cam.shake = MathF.Max(cam.shake, fin ? 0.45f : 0.22f); } }
        else if (A.human && fin) cam.shake = MathF.Max(cam.shake, 0.15f);
    }
    // squashed flat: stuck to the spot and harmless until it springs back up with a boing
    void Flatten(Actor B, float t)
    {
        if (Guarded(B)) return;
        var G = B.P; if (B.type == "mech" || B.type == "mecha") return;
        float k = B.type == "brute" || TYPES[B.type].Boss ? 0.55f : 1;
        G.flatT = MathF.Max(G.flatT, t * k); B.flatMax = G.flatT; G.ctrlLock = MathF.Max(G.ctrlLock, G.flatT); G.vx *= 0.1f; G.vz *= 0.1f; G.vy = MathF.Min(G.vy, 0);
        G.rolling = false; G.charging = false; if (G.hold != null || G.holdA != null) DropHeld(B);
        B.animFlatT = G.flatT; B.animFlatMax = G.flatT;
        Sfx("boing", G.x, G.y + 0.3f, G.z, 1);
        for (int i = 0; i < 8; i++) Puff(G.x, G.y + 0.1f, G.z, 2.5f, 0.3f, 0.12f);
    }
    // the per-step weapon handler for the current actor
    /// <summary>Hand weapons in vehicles: the blood cell and the virus fire their own mounted guns instead, and on the bike you
    /// can only use what works with one hand on the bars (sidearms, the shotgun, melee).</summary>
    static bool HandWeaponOk(Vehicle v, WeaponDef d)
    {
        if (v == null) return true;
        if (v.kind != "bike") return false;
        string g = GripOf(d); return g is "onehand" or "shotgun" or "melee";
    }
    void ActorWeapons(float dt, bool held)
    {
        var A = CUR;
        W.cd -= dt; W.since += dt; W.aimT = MathF.Max(0, W.aimT - dt); W.flash = MathF.Max(0, W.flash - dt * 12); W.hitT = MathF.Max(0, W.hitT - dt); W.empty = MathF.Max(0, W.empty - dt);
        if (W.beam != null) { W.beam.t -= dt; if (W.beam.t <= 0) W.beam = null; }
        foreach (var sw in W.slots)
            if (sw != null && WPN[sw.id].Energy)
            {   // energy weapons cool down whether held or not
                float cool = sw.over ? 0.6f : (sw == CurWpn(A) && W.since < 0.2f) ? 0.03f : 0.45f;
                sw.heat = MathF.Max(0, sw.heat - cool * dt); if (sw.over && sw.heat <= 0.3f) { sw.over = false; if (A.human) Sfx("cooled", P.x, P.y, P.z, 0.5f); }
            }
        var w = CurWpn(A); var def = CurDef(A);
        if (P.fireBlock) { if (!held) P.fireBlock = false; held = false; }   // the trigger pull that threw something doesn't also shoot
        if (P.flatT > 0) { P.flatT -= dt; if (P.flatT <= 0) { Sfx("boing", P.x, P.y + 0.5f, P.z, 0.7f); P.vy = 5; P.ground = false; } }
        bool able = !(P.flatT > 0) && P.alive && (!P.rolling || P.hover) && P.hold == null && P.holdA == null && P.grabbed == null && P.stunT <= 0 && P.ragdoll == null && (def.Type == "melee" ? ArmsOf(A) > 0 : CanShoot(A)) && !P.berserk && HandWeaponOk(P.vehicle, def);
        // swings in progress
        if (W.swingT > 0) { W.swingT -= dt; if (W.swingHit == 0 && W.swingT < W.swingDur * 0.55f) { W.swingHit = 1; MeleeResolve(W.swingDef); } }
        // the punch: a quick jab with the off hand, whatever you're holding (it interrupts a reload)
        if (W.punchQ > 0)
        {
            W.punchQ -= dt;
            if (W.swingT <= 0 && P.alive && ArmsOf(A) > 0 && P.stunT <= 0 && P.ragdoll == null)
            {
                W.punchQ = 0; W.reload = 0; var pd = WPN["punch"];
                if (W.swingDef != pd) W.combo = -1;
                MeleeSwing(pd); W.cd = MathF.Max(W.cd, W.swingDur * 0.9f); W.trig = true;
            }
        }
        // magazines: reload by hand (X / R), or automatically when you pull the trigger on an empty one
        bool canReload = w != null && def.Clip > 0 && !def.Energy && def.Type != "melee" && def.Type != "beam" && w.clip < def.Clip && w.ammo > 0;
        if (IN.reload && W.reload <= 0 && canReload && W.swingT <= 0) StartReload(A, w, def);
        // the AI tops up its magazine in a lull
        else if (!A.human && !held && canReload && W.reload <= 0 && w.clip < def.Clip * 0.5f && W.since > 1.2f) StartReload(A, w, def);
        W.pumpT = MathF.Max(0, W.pumpT - dt); W.switchT = MathF.Max(0, W.switchT - dt);
        if (W.reload > 0)
        {
            W.reload -= dt;
            if (W.reload <= 0 && w != null)
            {
                if (W.shell)
                {   // one shell in; keep going until full or out, then pump
                    w.clip += 1; w.ammo -= 1; if (A.human) Sfx("click", P.x, P.y, P.z, 0.35f);
                    if (w.clip < def.Clip && w.ammo >= 1) W.reload = W.reloadMax;
                    else { W.shell = false; W.pumpT = 0.48f; W.cd = MathF.Max(W.cd, 0.35f); if (A.human) Sfx("reload", P.x, P.y, P.z, 0.5f); }
                }
                else { float n = MathF.Min(def.Clip - w.clip, w.ammo); w.clip += n; w.ammo -= n; if (A.human) Sfx("cooled", P.x, P.y, P.z, 0.4f); }
            }
            // pulling the trigger with shells already in stops the loading: pump and fire
            if (W.reload > 0 && W.shell && held && w != null && w.clip >= 1) { W.reload = 0; W.shell = false; W.pumpT = 0.48f; W.cd = MathF.Max(W.cd, 0.3f); }
            else return;
        }
        if (!able) { W.charge = 0; return; }
        if (held) W.aimT = 0.9f;
        if (def.Type == "melee") { if (held && W.cd <= 0 && W.swingT <= 0) { MeleeSwing(def); W.cd = MathF.Max(W.swingDur * 0.92f, 0.7f / def.Rate); } return; }
        if (def.Type == "beam")
        {
            if (held && !w.over)
            {
                BeamTick(def, dt); W.since = 0; w.heat += def.Heat * dt;
                if (w.heat >= 1) { w.heat = 1; w.over = true; W.beam = null; if (A.human) { Sfx("overheat", P.x, P.y, P.z); ShowHint("OVERHEATED"); } }
            }
            else if (held && w.over) W.empty = 0.3f;
            return;
        }
        bool useClip = def.Clip != 0 && !def.Energy, loaded = def.Energy ? !w.over : useClip ? w.clip > 0 : w.ammo > 0;
        if (useClip && w.clip <= 0 && (held || A.team == "enemy")) { if (w.ammo > 0) StartReload(A, w, def); else if (held && W.cd <= 0) { W.empty = 0.4f; W.cd = 0.3f; if (A.human) { Sfx("click", P.x, P.y, P.z, 0.5f); ShowHint("NO AMMO"); } } return; }
        void Spend(float heatK)
        {
            if (def.Energy) { w.heat += heatK; if (w.heat >= 1) { w.heat = 1; w.over = true; if (A.human) { Sfx("overheat", P.x, P.y, P.z); ShowHint("OVERHEATED"); } } }
            else if (useClip) w.clip--; else w.ammo--;
        }
        if (def.Type == "bow" || def.Type == "charge")
        {   // hold to draw or charge, release to fire
            if (held && loaded && W.cd <= 0) { W.charge = MathF.Min(1, W.charge + dt / def.ChargeT); if (def.Type == "charge" && W.charge > 0.2f && R() < 0.3f) Puff(MUZ.x, MUZ.y, MUZ.z, 0.4f, 0.4f, 0.05f); }
            else if (!held && W.charge > 0)
            {
                float c = W.charge; W.charge = 0;
                if (def.Type == "bow" && c < 0.15f) return;
                FireWeapon(def, w, c); Spend(def.Type == "charge" && c >= 0.98f ? def.Heat2 : def.Heat); W.since = 0; W.cd = def.Type == "bow" ? 0.35f : 1 / (def.Rate != 0 ? def.Rate : 2);
            }
            else if (held && !loaded) { W.empty = 0.3f; if (A.human && W.cd <= 0) { Sfx("click", P.x, P.y, P.z, 0.5f); W.cd = 0.3f; } }
            return;
        }
        if (!held) { W.trig = false; return; }
        if (W.cd > 0) return;
        if (!def.Auto && W.trig && A.human) return;   // semi-auto: one shot per pull for people
        W.trig = true;
        if (!loaded) { W.empty = 0.4f; W.cd = 0.3f; if (A.human) Sfx("click", P.x, P.y, P.z, 0.5f); return; }
        FireWeapon(def, w, 0); Spend(def.Heat); W.since = 0; W.cd = 1 / def.Rate * (A.team == "enemy" ? 1.45f : 1);
    }
    void StartReload(Actor A, Weapon w, WeaponDef def)
    {
        // heroes reload faster than the germs (the germs' slowness is their weakness)
        float t = def.Reload * (A.team == "hero" ? 0.85f : 1);
        // the shotgun loads one shell at a time (and can be interrupted between shells); everything else swaps a magazine
        A.W.shell = def.Id == "shotgun";
        if (A.W.shell) t = Clamp(t / MathF.Max(1, def.Clip) * 1.4f, 0.34f, 0.6f);
        A.W.reload = t; A.W.reloadMax = t; A.W.charge = 0;
        Sfx("reload", A.P.x, A.P.y, A.P.z, A.human ? 0.7f : 0.5f);
    }
    public void SwitchWeapon(Actor A, int i)
    {
        var W = A.W; i = ((i % 3) + 3) % 3;
        W.cur = i; W.cd = MathF.Max(W.cd, 0.2f); W.charge = 0; W.reload = 0; W.shell = false; W.switchT = 0.32f;   // the new weapon comes up from low
        if (A.human) { var w = W.slots[i]; ShowHint(w != null ? WPN[w.id].Name + (float.IsFinite(w.ammo) ? " · " + w.clip + " / " + w.ammo : "") : "PSEUDOPODS"); Sfx("switch", A.P.x, A.P.y, A.P.z, 0.4f); }
    }
    public void CycleWeapon(Actor A, int d)
    {
        var W = A.W;
        for (int k = 1; k <= 3; k++) { int i = ((W.cur + d * k) % 3 + 3) % 3; if (W.slots[i] != null) { SwitchWeapon(A, i); return; } }
        SwitchWeapon(A, W.cur);
    }
    public void Explode(float x, float y, float z, float R2, float blast, float dmg, Actor src)
    {
        BlastChunks(x, y, z, R2 * 0.9f, dmg);
        QueryBodies(x - R2 - 2, x + R2 + 2, z - R2 - 2, z + R2 + 2, QPK);
        foreach (var b in QPK.ToArray())
        {
            if (b.Held != null || b.Actor != null) continue;
            float dx = b.Pos.x - x, dy = b.Pos.y - y, dz = b.Pos.z - z, d = Hypot(dx, dy, dz);
            if (d > R2 + b.Bound) continue;
            float dd = d == 0 ? 1 : d, f = blast * (1 - Clamp((d - b.Bound * 0.5f) / R2, 0, 1)) * MathF.Min(1, b.M / 40 + 0.3f), ux = dx / dd, uy = dy / dd + 0.6f, uz = dz / dd, ul = Hypot(ux, uy, uz);
            Wake(b);
            IMP.x = (R() - 0.5f) * b.Bound * 0.6f; IMP.y = (R() - 0.5f) * b.Bound * 0.6f; IMP.z = (R() - 0.5f) * b.Bound * 0.6f;
            ApplyImpulse(b, ux / ul * f, uy / ul * f, uz / ul * f, IMP);
            if (b.Bomb && d < R2) b.Fuse = MathF.Min(b.Fuse > 0 ? b.Fuse : 9, 0.12f + R() * 0.12f);
        }
        foreach (var Vh in vehicles.ToArray()) { if (Vh.dead) continue; float d = Hypot(Vh.x - x, Vh.y - y, Vh.z - z); if (d < R2 + Vh.def.HitL) DamageVehicle(Vh, dmg * (1 - d / (R2 + Vh.def.HitL)) * 1.5f, src, x, y, z); }
        foreach (var B in actors.ToArray())
        {
            var G = B.P; if (!G.alive || B.dormant) continue;
            float px = G.x - x, py = G.y + G.H * 0.5f - y, pz = G.z - z, pd = Hypot(px, py, pz);
            if (pd > R2 + G.R) continue;
            float pdd = pd == 0 ? 1 : pd, k = Clamp(1 - pd / R2, 0, 1), knock = 15 * k * (B.type == "brute" ? 0.35f : 1);
            if (B.team == "hero" && src != null && src.team == "hero")
            {   // your own blasts: rocket jumps, no damage
                G.vx += px / pdd * knock * 0.8f; G.vz += pz / pdd * knock * 0.8f; G.vy = MathF.Max(G.vy, (py / pdd + 0.8f) * knock * 0.8f); if (G.vy > 1.5f) { G.ground = false; G.dashUsed = false; }
                continue;
            }
            float d = dmg * k * (src != null && src.team == "enemy" && B.team == "enemy" ? 0.3f : 1);
            DamageActor(B, d * 0.6f, new Dmg { src = src, kind = "blast", fx = px, fz = pz, knock = knock, up = 5 * k });
            if (G.alive && d > 12) DamageActor(B, d * 0.5f, new Dmg { src = src, kind = "blast" });
            if (G.alive && k > 0.4f && B.type != "brute") Knockdown(B, 0.8f * k + 0.4f, px / pdd * knock, 5 * k, pz / pdd * knock);
        }
        Boom(x, y, z, R2);
        Sfx("boom", x, y, z, MathF.Min(1, R2 / 6)); Noise(x, z, 70, src);
        float hd = Hypot(x - HUMAN.P.x, z - HUMAN.P.z);
        cam.shake = MathF.Max(cam.shake, (R2 > 5 ? 0.9f : 0.45f) * Clamp(1 - hd / 70, 0, 1));
        if (hd < 40) Rumble(Clamp(1 - hd / 40, 0, 1), 0.8f, 220);
    }
    // needles stick, then pop; seven in one man set them all off at once
    void StickNeedle(Actor B, Actor src)
    {
        B.needles++; B.needleT = 0.7f; B.needleSrc = src;
        if (B.needles >= 7) { var G = B.P; B.needles = 0; Explode(G.x, G.y + G.H * 0.55f, G.z, 3, 500, 60, src); Sfx("supercombine", G.x, G.y + 1, G.z); }
    }
    void StepNeedles(float dt)
    {
        foreach (var B in actors.ToArray()) if (B.needles > 0) { B.needleT -= dt; if (B.needleT <= 0 && B.P.alive) { DamageActor(B, B.needles * 3, new Dmg { src = B.needleSrc, kind = "blast" }); B.needles = 0; } }
    }
    void StepShots(float dt)
    {
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            if (i >= shots.Count) continue;
            var s = shots[i];
            s.life -= dt; s.age += dt;
            if (s.home != 0 && s.target != null && s.age > 0.08f && s.target.P.alive)
            {
                var T = s.target; float sp0 = Hypot(s.vx, s.vy, s.vz), ux = s.vx / sp0, uy = s.vy / sp0, uz = s.vz / sp0;
                float tx = T.P.x - s.x, ty = T.P.y + T.P.H * 0.6f - s.y, tz = T.P.z - s.z, tl = Hypot(tx, ty, tz); if (tl == 0) tl = 1; float k0 = MathF.Min(1, s.home * 4 * dt);
                ux += (tx / tl - ux) * k0; uy += (ty / tl - uy) * k0; uz += (tz / tl - uz) * k0; float l = Hypot(ux, uy, uz);
                s.vx = ux / l * sp0; s.vy = uy / l * sp0; s.vz = uz / l * sp0;
            }
            s.vy -= s.grav * dt;
            float ox = s.x, oy = s.y, oz = s.z;
            float sp = Hypot(s.vx, s.vy, s.vz); int n = Math.Max(1, (int)MathF.Ceiling(sp * dt / 0.5f));
            object hit = null;
            for (int k = 0; k < n && hit == null; k++) { s.x += s.vx * dt / n; s.y += s.vy * dt / n; s.z += s.vz * dt / n; hit = SolidAt(s.x, s.y, s.z); }
            var man = HitMen(ox, oy, oz, s.x, s.y, s.z, s.team, s.team == "enemy" ? 0.1f : 0, s.hitList);
            if (man != null)
            {
                float hx = ox + (s.x - ox) * HITQ.t, hy = oy + (s.y - oy) * HITQ.t, hz = oz + (s.z - oz) * HITQ.t;
                if (HITQ.veh != null) { if (s.radius != 0) Explode(hx, hy, hz, s.radius, s.blast, s.dmg, s.owner); else DamageVehicle(HITQ.veh, s.dmg, s.owner, HITQ.hx, HITQ.hy, HITQ.hz); }
                else if (s.radius != 0) Explode(hx, hy, hz, s.radius, s.blast, s.dmg, s.owner);
                else
                {
                    bool head = HITQ.part == PART.head; var ma = (Actor)man;
                    DamageActor(ma, s.dmg * (head && s.team == "hero" ? 1.6f : 1), new Dmg { src = s.owner, kind = s.kind == "plasma" ? "plasma" : "bullet", part = HITQ.part, fx = s.vx, fz = s.vz, knock = ((s.def?.Knock ?? 0) != 0 ? s.def.Knock : 0.6f) * (s.team == "hero" ? 1 : 0.5f) + (s.charged ? 4 : 0), charged = s.charged }.Hit(hx, hy, hz));
                    if (s.kind == "needle") StickNeedle(ma, s.owner);
                }
                if (s.owner?.W != null) s.owner.W.hitT = 0.15f;
                if (s.pierce > 0 && HITQ.veh == null) { s.pierce--; (s.hitList ??= new()).Add(man); s.x = hx + s.vx * 0.01f; s.y = hy + s.vy * 0.01f; s.z = hz + s.vz * 0.01f; continue; }
                shots.Remove(s); continue;
            }
            if (hit != null || s.life <= 0)
            {
                if (hit is Body hb)
                {
                    if (hb.Bomb && !(hb.Fuse > 0)) hb.Fuse = 0.45f;
                    if (s.radius == 0) { float sp2 = Hypot(s.vx, s.vy, s.vz); if (sp2 == 0) sp2 = 1; float f = MathF.Min(200, s.dmg * 6); IMP.x = s.x - hb.Pos.x; IMP.y = s.y - hb.Pos.y; IMP.z = s.z - hb.Pos.z; Wake(hb); ApplyImpulse(hb, s.vx / sp2 * f, s.vy / sp2 * f, s.vz / sp2 * f, IMP); }
                }
                if (s.radius != 0) Explode(s.x, s.y, s.z, s.radius, s.blast, s.dmg, s.owner);
                else if (hit != null)
                {
                    for (int q = 0; q < 2; q++) Puff(s.x, s.y, s.z, 2.5f, 2, 0.07f); if (R() < 0.3f) Sfx("ric", s.x, s.y, s.z, 0.3f);
                    if (hit == SOLID_WORLD && SOLID_HIT?.Chunk != null) DamageChunk(SOLID_HIT.Chunk, s.dmg * (s.team == "hero" ? 1.2f : 0.6f) * (s.charged ? 3 : 1), s.vx, s.vy, s.vz);
                }
                shots.Remove(s);
            }
            else if ((s.kind == "rocket" || s.kind == "plasma" || s.kind == "needle") && R() < 0.5f) Puff(s.x, s.y, s.z, 0.3f, 0.3f, 0.09f);
        }
        StepNeedles(dt);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Core;
using GermBall.Render;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed class Building { public int id; public float x, z, w, d, h, y; public int style, floors; public List<Chunk> chunks = new(); public Dictionary<string, Chunk> map = new(); }
public sealed class Chunk
{
    public Static o; public Building B; public string mat, kind, key; public float hp; public bool alive = true; public float x, y, z, sx, sy, sz; public int idx = -1;
    public Static ramp; public List<Chunk> flight;
}
public sealed class Place { public float x, z, r, y; public string name; public List<(float x, float z)> waypoints = new(); public List<Squad> squads = new(); public List<(float x, float z)> outside; }
public sealed class BigCell { public float x, z, y, r, h, ph; }
public sealed class Bone { public float x, z, y, a, L, r; }
public sealed class Prop { public string kind; public bool heavy, rock; public Node vis; public Mesh lit; }

public sealed partial class World
{
    // =====================================================================
    // Buildings: hollow, multi-storey, and breakable. Walls are a grid of panels with doors and windows, floors are tiled
    // slabs with a stairwell, and each storey has a staircase. Every panel, tile and stair is its own static block with
    // hit points: shots chip at them, blasts and heavy blows smash them, and anything left without support falls in after.
    // =====================================================================
    const float FH = 3.6f, WT = 0.42f, BSCALE = 2.0f; static readonly float[] ROWH = { 2.45f, FH - 2.45f };
    public readonly List<Building> BLDG = new();
    readonly List<Chunk> CHUNKS = new(); readonly Dictionary<string, List<Chunk>> CH_BY_MAT = new(); readonly Queue<Chunk> chunkQueue = new();
    int bldgIds; readonly HashSet<string> chunkDirty = new();
    Chunk AddChunk(Building B, float x0, float y0, float z0, float x1, float y1, float z1, string mat, string kind, string key, float hp, bool visualOnly = false)
    {
        var ob = new Static { Kind = BOXK, MinX = x0, MaxX = x1, MinY = y0, MaxY = y1, MinZ = z0, MaxZ = z1 }; var o = visualOnly ? ob : AddStatic(ob);
        var c = new Chunk { o = o, B = B, mat = mat, kind = kind, key = key, hp = hp, x = (x0 + x1) / 2, y = (y0 + y1) / 2, z = (z0 + z1) / 2, sx = x1 - x0, sy = y1 - y0, sz = z1 - z0 };
        o.Chunk = c; CHUNKS.Add(c); if (!CH_BY_MAT.TryGetValue(mat, out var l)) CH_BY_MAT[mat] = l = new(); l.Add(c);
        if (key != null) B.map[key] = c;
        B.chunks.Add(c);
        return c;
    }
    Building MakeBuilding(float x, float z, float w, float d, float h, float y, int style)
    {
        w *= BSCALE; d *= BSCALE; h *= BSCALE;
        int floors = Math.Max(1, (int)MathF.Round(h / FH)); float H = floors * FH; string wall = style == 3 ? "tower" : new[] { "wallA", "wallB", "wallC" }[style % 3];
        var B = new Building { id = ++bldgIds, x = x, z = z, w = w, d = d, h = H, y = y, style = style, floors = floors };
        BLDG.Add(B);
        float x0 = x - w / 2, x1 = x + w / 2, z0 = z - d / 2, z1 = z + d / 2; var r = Rng(B.id * 131 + 7);
        // a footing under it all, so the floor is flat whatever the ground does
        AddBox(x0 - 0.2f, y - 1.5f, z0 - 0.2f, x1 + 0.2f, y + 0.05f, z1 + 0.2f, "dark");
        // walls: four sides of panels; corners always solid; a door on every side; windows scattered
        for (int side = 0; side < 4; side++)
        {
            bool alongX = side < 2; float L = alongX ? w : d; int ncol = Math.Max(3, (int)MathF.Round(L / 3)); float cw = L / ncol; int door = ncol / 2 + (r() < 0.5f && ncol > 4 ? -1 : 0);
            for (int f = 0; f < floors; f++) for (int row = 0; row < 2; row++) for (int c = 0; c < ncol; c++)
                    {
                        float ya = y + 0.05f + f * FH + (row != 0 ? ROWH[0] : 0), yb = y + 0.05f + f * FH + (row != 0 ? FH : ROWH[0]);
                        bool corner = c == 0 || c == ncol - 1;
                        float top = yb;
                        if (!corner && row == 0)
                        {
                            if (f == 0 && c == door) continue;   // the doorway
                            if ((c + f + side) % 2 == 1 && r() < 0.75f) top = ya + 1.0f;   // a window: just the sill
                        }
                        float a0 = (alongX ? x0 : z0) + c * cw, a1 = a0 + cw;
                        string key = side + ":" + f + ":" + row + ":" + c;
                        if (alongX) { float zz = side == 0 ? z0 : z1 - WT; AddChunk(B, a0, ya, zz, a1, top, zz + WT, wall, "wall", key, 170); }
                        else { float xx = side == 2 ? x0 : x1 - WT; AddChunk(B, xx, ya, a0, xx + WT, top, a1, wall, "wall", key, 170); }
                    }
        }
        // floors and the roof, tiled; each storey's stairwell is cut where the flight from below comes up.
        // Flights alternate between two strips side by side, so one never runs under the next.
        float sw = 1.6f, sl = MathF.Min(d - 2 * WT - 1.4f, 6.2f), hz0 = z0 + WT + 0.9f, hz1 = hz0 + sl;
        float StripX(int l) => x0 + WT + 0.2f + (l % 2) * (sw + 0.25f);
        for (int l = 1; l <= floors; l++)
        {
            float ly = y + 0.05f + l * FH, hx0 = StripX(l - 1), hx1 = hx0 + sw; bool hole = l < floors || floors > 1, cut = hole && floors > 1;
            var xs = new List<float> { x0 + WT, hx0, hx1 }; var zs = new List<float> { z0 + WT, hz0, hz1 };
            for (float v = x0 + WT + 4; v < x1 - WT - 0.5f; v += 4) xs.Add(v); for (float v = z0 + WT + 4; v < z1 - WT - 0.5f; v += 4) zs.Add(v);
            xs.Add(x1 - WT); zs.Add(z1 - WT);
            var X = xs.Select(v => MathF.Round(v * 1000) / 1000).Distinct().Where(v => v >= x0 + WT - 1e-3f && v <= x1 - WT + 1e-3f).OrderBy(v => v).ToList();
            var Z = zs.Select(v => MathF.Round(v * 1000) / 1000).Distinct().Where(v => v >= z0 + WT - 1e-3f && v <= z1 - WT + 1e-3f).OrderBy(v => v).ToList();
            for (int i = 0; i < X.Count - 1; i++) for (int j = 0; j < Z.Count - 1; j++)
                {
                    float cx = (X[i] + X[i + 1]) / 2, cz = (Z[j] + Z[j + 1]) / 2;
                    if (cut && l < floors + 1 && cx > hx0 && cx < hx1 && cz > hz0 && cz < hz1 && l <= floors - (l == floors ? 1 : 0)) continue;
                    if (X[i + 1] - X[i] < 0.05f || Z[j + 1] - Z[j] < 0.05f) continue;
                    AddChunk(B, X[i], ly - 0.3f, Z[j], X[i + 1], ly, Z[j + 1], "dark", "slab", null, 220);
                }
            // parapet on the roof
            if (l == floors) foreach (var a in new[] { new[] { x0, z0, x1, z0 + 0.3f }, new[] { x0, z1 - 0.3f, x1, z1 }, new[] { x0, z0, x0 + 0.3f, z1 }, new[] { x1 - 0.3f, z0, x1, z1 } }) AddChunk(B, a[0], ly, a[1], a[2], ly + 0.7f, a[3], wall, "rail", null, 90);
        }
        // stairs: a smooth ramp underfoot, drawn as twelve steps, up through the stairwell each storey
        for (int l = 0; l < floors - 1; l++)
        {
            float bse = y + 0.05f + l * FH; const int n = 12; float rise = FH / n, run = sl / n, hx0 = StripX(l), hx1 = hx0 + sw;
            float lhz0 = hz0, lsl = sl;
            var ramp = AddStatic(new Static { Kind = PATCHK, MinX = hx0, MaxX = hx1, MinZ = hz0, MaxZ = hz0 + sl, MinY = bse - 0.2f, Tag = "stair", Fn = (px, pz) => bse + Clamp((pz - lhz0) / lsl, 0, 1) * FH });
            var flight = new List<Chunk>();
            for (int i = 0; i < n; i++) { var c = AddChunk(B, hx0, bse + i * rise, hz0 + i * run, hx1, bse + (i + 1) * rise, hz0 + (i + 1) * run, "dark", "stair", null, 200, true); c.ramp = ramp; c.flight = flight; flight.Add(c); }
        }
        // an inner wall with a doorway splits the bigger floors
        if (w > 11 && d > 11)
            for (int f = 0; f < floors; f++)
            {
                float ya = y + 0.05f + f * FH, zc = z + 1.5f, gap0 = x - 1.0f, gap1 = x + 1.0f;
                foreach (var (a0, a1) in new[] { (x0 + WT + 2 * sw + 0.6f, gap0), (gap1, x1 - WT) }) if (a1 - a0 > 0.5f) AddChunk(B, a0, ya, zc - 0.15f, a1, ya + FH - 0.3f, zc + 0.15f, wall, "inner", null, 120);
            }
        return B;
    }
    // one instanced mesh per material holds every chunk; a smashed chunk's instance is zeroed
    readonly Dictionary<string, InstancedMesh> CH_MESH = new();
    static readonly Geometry boxGeo = Geo.Box(1, 1, 1);
    void FlushChunks()
    {
        foreach (var (mat, list) in CH_BY_MAT)
        {
            var m = new InstancedMesh(boxGeo, SMAT[mat], list.Count) { CastShadow = true, ReceiveShadow = true };
            for (int i = 0; i < list.Count; i++) { var c = list[i]; c.idx = i; m.Matrices[i] = Matrix4x4.CreateScale(c.sx, c.sy, c.sz) * Matrix4x4.CreateTranslation(c.x, c.y, c.z); }
            m.Touch(); scene.Add(m); CH_MESH[mat] = m;
        }
    }
    // damage: hits wear a chunk down; when it goes, it breaks into rubble and its neighbours are checked for support
    void DamageChunk(Chunk c, float dmg, float fx, float fy, float fz)
    {
        if (c == null || !c.alive) return;
        c.hp -= dmg;
        if (c.hp <= 0) DestroyChunk(c, fx, fy, fz);
    }
    static readonly Q4 IDQ = new();
    void DestroyChunk(Chunk c, float fx, float fy, float fz)
    {
        if (!c.alive) return;
        c.alive = false; RemoveStatic(c.o);
        if (c.flight != null && c.flight.Count(o => o.alive) < c.flight.Count / 2f) { RemoveStatic(c.ramp); foreach (var o in c.flight.ToArray()) if (o.alive) DestroyChunk(o, 0, -1, 0); }
        if (CH_MESH.TryGetValue(c.mat, out var m) && c.idx >= 0) { m.Matrices[c.idx] = new Matrix4x4(); chunkDirty.Add(c.mat); }
        // rubble: a few broken pieces of it
        int n = c.kind == "stair" || c.kind == "rail" ? 1 : 2 + (R() < 0.5f ? 1 : 0); float fl = Hypot(fx, fy, fz); if (fl == 0) fl = 1;
        for (int i = 0; i < n; i++)
        {
            float sx = MathF.Max(0.25f, c.sx * (0.35f + R() * 0.3f)), sy = MathF.Max(0.25f, c.sy * (0.35f + R() * 0.3f)), sz = MathF.Max(0.25f, c.sz * (0.35f + R() * 0.3f));
            float k = 3 + R() * 4;
            AddChip(c.x + (R() - 0.5f) * c.sx * 0.5f, c.y + (R() - 0.5f) * c.sy * 0.5f, c.z + (R() - 0.5f) * c.sz * 0.5f, fx / fl * k + (R() - 0.5f) * 3, fy / fl * k * 0.5f + R() * 2, fz / fl * k + (R() - 0.5f) * 3, IDQ, sx, sy, sz, RubbleKey(c.mat), 6 + R() * 3);
        }
        for (int i = 0; i < 3; i++) Puff(c.x, c.y, c.z, 3, 2, 0.3f);
        Sfx(R() < 0.5f ? "slam" : "thud", c.x, c.y, c.z, 0.9f);
        // wake whatever was resting nearby
        QueryBodies(c.x - 3, c.x + 3, c.z - 3, c.z + 3, QPK); foreach (var b in QPK) Wake(b);
        chunkQueue.Enqueue(c);
    }
    string RubbleKey(string mat) { string k = "RB" + mat; if (!IM.ContainsKey(k)) MkInst(k, boxGeo, SMAT[mat], 900, true); return k; }
    // support: a wall panel needs the panel under it, or both its neighbours; a floor tile needs a wall near it below
    static bool Alive(Chunk c) => c != null && c.alive;
    bool Supported(Chunk c)
    {
        var B = c.B;
        if (c.kind == "wall" && c.key != null)
        {
            var k = c.key.Split(':').Select(int.Parse).ToArray(); int side = k[0], f = k[1], row = k[2], col = k[3];
            if (f == 0 && row == 0) return true;
            bool hasBelow = B.map.TryGetValue(row == 1 ? $"{side}:{f}:0:{col}" : $"{side}:{f - 1}:1:{col}", out var below);
            if (hasBelow && below.alive) return true;
            if (f == 0 && row == 1 && !hasBelow) return true;   // over the doorway or a window: the lintel spans it
            if (!hasBelow && row == 1) { B.map.TryGetValue($"{side}:{f}:1:{col - 1}", out var l1); B.map.TryGetValue($"{side}:{f}:1:{col + 1}", out var r1); return Alive(l1) || Alive(r1); }
            B.map.TryGetValue($"{side}:{f}:{row}:{col - 1}", out var l); B.map.TryGetValue($"{side}:{f}:{row}:{col + 1}", out var rr);
            return Alive(l) && Alive(rr);
        }
        if (c.kind == "slab" || c.kind == "rail" || c.kind == "inner")
        {
            int near = 0;
            foreach (var o in B.chunks) if (o.alive && o.kind == "wall" && MathF.Abs(o.y - c.y) < FH && o.y < c.y && Hypot(o.x - c.x, o.z - c.z) < 6) { near++; if (near >= 2) return true; }
            return false;
        }
        return true;
    }
    void StepChunks()
    {
        // a few collapse checks per step, so a building comes down over a second or two instead of all at once
        for (int n = 0; n < 24 && chunkQueue.Count > 0; n++)
        {
            var c = chunkQueue.Dequeue(); var B = c.B;
            foreach (var o in B.chunks.ToArray())
            {
                if (!o.alive) continue;
                if (MathF.Abs(o.x - c.x) > 4 || MathF.Abs(o.z - c.z) > 4 || o.y < c.y - 0.5f || o.y > c.y + FH * 1.2f) continue;
                if (!Supported(o)) DestroyChunk(o, 0, -1, 0);
            }
        }
        foreach (var mat in chunkDirty) if (CH_MESH.TryGetValue(mat, out var m)) m.Touch();
        chunkDirty.Clear();
    }
    readonly List<Static> QCH = new();
    // blasts smash every chunk in reach
    void BlastChunks(float x, float y, float z, float R2, float dmg)
    {
        QueryStatics(x - R2, x + R2, z - R2, z + R2, QCH);
        foreach (var o in QCH.ToArray())
        {
            var c = o.Chunk; if (c == null || !c.alive) continue;
            float qx = Clamp(x, o.MinX, o.MaxX), qy = Clamp(y, o.MinY, o.MaxY), qz = Clamp(z, o.MinZ, o.MaxZ), d = Hypot(qx - x, qy - y, qz - z);
            if (d < R2) DamageChunk(c, dmg * 4 * (1 - d / R2) + 30, c.x - x, c.y - y, c.z - z);
        }
    }
    // a heavy blow at a point (melee, rams): the chunk there takes it
    void BashAt(float x, float y, float z, float dmg, float fx, float fz)
    {
        QueryStatics(x - 0.6f, x + 0.6f, z - 0.6f, z + 0.6f, QCH);
        foreach (var o in QCH.ToArray())
        {
            var c = o.Chunk; if (c == null || !c.alive) continue;
            if (x > o.MinX - 0.6f && x < o.MaxX + 0.6f && y > o.MinY - 0.3f && y < o.MaxY + 0.3f && z > o.MinZ - 0.6f && z < o.MaxZ + 0.6f) DamageChunk(c, dmg, fx, 0.3f, fz);
        }
    }

    // ---- the layout
    public readonly List<Place> OUTPOSTS = new(), TOWNS = new(); public readonly List<BigCell> BIGCELLS = new(); public readonly List<Bone> BONES = new();
    readonly List<(float x, float z, float yaw, string kind)> PARKED = new();
    void BuildLayout()
    {
        var r = Rng(2024);
        foreach (var p in PADS)
        {
            float y = p.H, R2 = p.R;
            if (p.Kind == "base")
            {
                MakeBuilding(p.X - 14, p.Z - 6, 8, 10, 6, y, 0); MakeBuilding(p.X + 14, p.Z - 4, 9, 8, 5, y, 1);
                CoverWall(p.X - 4, p.Z - 16, 6, true, y, 1.15f, "cover"); CoverWall(p.X + 6, p.Z - 18, 5, true, y, 1.15f, "cover");
                StashAt(p.X - 6, p.Z + 8, y, new[] { "ammo" }); StashAt(p.X + 6, p.Z + 8, y, new[] { "repair" }); StashAt(p.X, p.Z + 14, y, new[] { "homing" });
                var b0 = BLDG[^2]; var b1 = BLDG[^1];
                RackOn(b0, 0, "base"); RackOn(b0, 2, "base"); RackOn(b1, 0, "base"); RackOn(b1, 3, "base");
                PARKED.Add((p.X - 4, p.Z + 22, 0.3f, "bike")); PARKED.Add((p.X + 4, p.Z + 23, -0.2f, "rover")); PARKED.Add((p.X + 11, p.Z + 21, 0, "chair"));
                continue;
            }
            if (p.Kind == "town" && LAYOUT.Colonies)
            {
                // inside the leviathan, "towns" are colonies of giant cells: domed membranes to fight around
                var T = new Place { x = p.X, z = p.Z, r = R2, y = y, name = p.Name };
                for (int k = 0; k < 16; k++)
                {
                    float a = r() * 6.28f, d = 6 + r() * (R2 - 9), cx = p.X + MathF.Cos(a) * d, cz = p.Z + MathF.Sin(a) * d, crr = 1.8f + r() * 2.6f;
                    if (BIGCELLS.Any(c => Hypot(c.x - cx, c.z - cz) < c.r + crr + 3)) continue;
                    BIGCELLS.Add(new BigCell { x = cx, z = cz, y = y, r = crr, h = crr * (1.1f + r() * 0.5f), ph = r() * 6.28f }); AddStatic(new Static { Kind = CYLK, X = cx, Z = cz, R = crr * 0.95f, Bottom = y - 1, Top = y + crr * 1.3f });
                    for (int q = 0; q < 4; q++) { float b = q / 4f * 6.28f + a; coverPts.Add(new CoverPt { x = cx + MathF.Cos(b) * (crr + 0.8f), z = cz + MathF.Sin(b) * (crr + 0.8f), nx = MathF.Cos(b), nz = MathF.Sin(b) }); }
                }
                for (int i = 0; i < 6; i++) { float a = i / 6f * 6.28f + 0.4f; T.waypoints.Add((p.X + MathF.Cos(a) * (R2 - 4), p.Z + MathF.Sin(a) * (R2 - 4))); }
                StashAt(p.X + 3.5f, p.Z + 3.5f, y, new[] { "ammo", "ammo", "repair", "weapon" }, RACK_POOLS["town"]);
                PARKED.Add((p.X + R2 * 0.6f, p.Z, r() * 6.28f, r() < 0.5f ? "chair" : "rover"));
                TOWNS.Add(T); continue;
            }
            if (p.Kind == "town")
            {
                var T = new Place { x = p.X, z = p.Z, r = R2, y = y, name = p.Name };
                // a loose grid of blocks with 7 m streets
                bool small = R2 < 30; float cell = small ? 14 : R2 > 50 ? 21 : 24, bk = small ? 0.56f : 1; int nb0 = BLDG.Count;
                int span = Math.Max(1, (int)MathF.Floor((R2 - (small ? 4 : 6)) / cell));
                var plaza = PADS.FirstOrDefault(q => q != p && q.Kind == "base" && Hypot(q.X - p.X, q.Z - p.Z) < R2);   // a base inside the town keeps its square clear
                for (int gi = -span; gi <= span; gi++) for (int gj = -span; gj <= span; gj++)
                    {
                        float cx = p.X + gi * cell + (r() - 0.5f) * 2, cz = p.Z + gj * cell + (r() - 0.5f) * 2;
                        if (Hypot(cx - p.X, cz - p.Z) > R2 - (small ? 3 : 6)) continue;
                        if (plaza != null && Hypot(cx - plaza.X, cz - plaza.Z) < plaza.R + 4) continue;
                        float roll = r();
                        if (gi == 0 && gj == 0) { Arch(cx, cz, 5, r() < 0.5f, y); continue; }
                        if (roll < 0.62f) MakeBuilding(cx, cz, (5 + r() * 3) * bk, (5 + r() * 3) * bk, (3.5f + r() * 6) * (small ? 0.8f : 1), y, (int)MathF.Floor(r() * 4));
                        else if (roll < 0.8f) { CoverWall(cx, cz, 4 + r() * 2, r() < 0.5f, y); CrateStack(cx + 2.5f, cz + 2.5f, y, 1 + (int)MathF.Floor(r() * 3)); }
                        else T.waypoints.Add((cx, cz));
                    }
                for (int i = 0; i < 4; i++) { float a = i / 4f * 6.28f + 0.4f; T.waypoints.Add((p.X + MathF.Cos(a) * (R2 - 3), p.Z + MathF.Sin(a) * (R2 - 3))); }
                StashAt(p.X + 3.5f, p.Z + 3.5f, y, new[] { "ammo", "ammo", "repair", "weapon" }, RACK_POOLS["town"]);
                if (r() < 0.7f) StashAt(p.X - 8, p.Z - 3.5f, y, new[] { "homing", "ammo" });
                for (int k = nb0, n = 0; k < BLDG.Count && n < 3; k++) if (r() < 0.5f) { RackOn(BLDG[k], (int)MathF.Floor(r() * 4), "town"); n++; }
                if (T.waypoints.Count > 0) { float q = r(); PARKED.Add((T.waypoints[0].x, T.waypoints[0].z, r() * 6.28f, q < 0.5f ? "bike" : q < 0.8f ? "rover" : "chair")); }
                TOWNS.Add(T); continue;
            }
            // enemy outpost: a walled square with gates, a watch tower, bunkers, low cover inside
            var O = new Place { x = p.X, z = p.Z, r = R2, y = y, name = p.Name };
            float hs = R2 - 6, wt = 0.7f, wh = 2.5f, gate = 7;
            foreach (float s in new[] { -1f, 1f })
            {
                // north/south walls along x, with a gate in the middle
                AddBox(p.X - hs, y - 0.4f, p.Z + s * hs - wt / 2, p.X - gate / 2, y + wh, p.Z + s * hs + wt / 2, "fence");
                AddBox(p.X + gate / 2, y - 0.4f, p.Z + s * hs - wt / 2, p.X + hs, y + wh, p.Z + s * hs + wt / 2, "fence");
                AddBox(p.X - hs, y - 0.4f, p.Z - hs, p.X - hs + 0.01f, y, p.Z - hs + 0.01f, "fence", false);
                // east/west walls along z
                AddBox(p.X + s * hs - wt / 2, y - 0.4f, p.Z - hs, p.X + s * hs + wt / 2, y + wh, p.Z - gate / 2, "fence");
                AddBox(p.X + s * hs - wt / 2, y - 0.4f, p.Z + gate / 2, p.X + s * hs + wt / 2, y + wh, p.Z + hs, "fence");
                foreach (float q in new[] { -1f, 1f }) AddBox(p.X + s * hs - 1, y - 0.4f, p.Z + q * hs - 1, p.X + s * hs + 1, y + 4.2f, p.Z + q * hs + 1, "dark");   // corner posts
            }
            MakeBuilding(p.X, p.Z, 6, 6, 11, y, 3);                     // watch tower
            MakeBuilding(p.X - hs * 0.55f, p.Z - hs * 0.5f, 7, 4.5f, 3.2f, y, 1); MakeBuilding(p.X + hs * 0.55f, p.Z + hs * 0.5f, 7, 4.5f, 3.2f, y, 2);
            RackOn(BLDG[^2], 0, "outpost"); RackOn(BLDG[^1], 1, "outpost");
            PARKED.Add((p.X + hs + 15, p.Z + 4, PI / 2, "rover"));
            for (int i = 0; i < 9; i++)
            {
                float a = r() * 6.28f, d = 8 + r() * (hs - 11), cx = p.X + MathF.Cos(a) * d, cz = p.Z + MathF.Sin(a) * d;
                if (MathF.Abs(cx - p.X) < 6.5f && MathF.Abs(cz - p.Z) < 6.5f) continue;
                if (r() < 0.6f) CoverWall(cx, cz, 3.5f + r() * 2.5f, r() < 0.5f, y); else CrateStack(cx, cz, y, 1 + (int)MathF.Floor(r() * 3));
            }
            // cover outside each gate, too
            foreach (var (gx, gz, ax) in new[] { (0f, -1f, true), (0f, 1f, true), (-1f, 0f, false), (1f, 0f, false) }) CoverWall(p.X + gx * (hs + 9), p.Z + gz * (hs + 9), 5, ax, y);
            StashAt(p.X + 6.5f, p.Z - 1, y, new[] { "ammo", "weapon" }, RACK_POOLS["outpost"]); StashAt(p.X - 6.5f, p.Z + 1, y, new[] { "ammo" });
            for (int i = 0; i < 6; i++) { float a = i / 6f * 6.28f; O.waypoints.Add((p.X + MathF.Cos(a) * (hs - 4), p.Z + MathF.Sin(a) * (hs - 4))); }
            O.outside = new(); for (int i = 0; i < 8; i++) { float a = i / 8f * 6.28f + 0.2f; O.outside.Add((p.X + MathF.Cos(a) * (R2 + 14), p.Z + MathF.Sin(a) * (R2 + 14))); }
            OUTPOSTS.Add(O);
        }
        // ruins in the open: broken walls and pillars for cover between places
        float ruinR = LAYOUT.Bowl != null ? LAYOUT.Bowl.R - 10 : 300;
        for (int i = 0; i < (LAYOUT.Bowl != null ? 30 : 46); i++)
        {
            float x = (r() * 2 - 1) * ruinR, z = (r() * 2 - 1) * ruinR;
            bool ok = true; foreach (var p in PADS) if (Hypot(x - p.X, z - p.Z) < p.R + 12) ok = false;
            foreach (var s in SPIRES) if (Hypot(x - s.X, z - s.Z) < 9) ok = false;
            foreach (var c in CHANNELS) if (ChanDist(c, x, z).D < c.W + 6) ok = false;
            if (!ok) continue;
            var ts = TerrainSample(x, z); float y = ts.h, slope = Hypot(ts.gx, ts.gz); if (slope > 0.35f) continue;
            if (LAYOUT.Organic)
            {   // loose bones lying in the flesh: cover, and something to climb on
                float a = r() * 3.14f, L = 5 + r() * 7, rr = 0.55f + r() * 0.35f, ca = MathF.Cos(a), sa = MathF.Sin(a);
                BONES.Add(new Bone { x = x, z = z, y = y + rr * 0.6f, a = a, L = L, r = rr });
                for (int k = -2; k <= 2; k++) { float px = x + ca * L * k / 5, pz = z + sa * L * k / 5; AddStatic(new Static { Kind = CYLK, X = px, Z = pz, R = rr * 1.15f, Bottom = y - 1, Top = y + rr * 1.5f }); }
                AddCoverPts(x - rr, z - rr, x + rr, z + rr);
                if (r() < 0.15f) StashAt(x + 2, z + 2, y, new[] { "weapon", "ammo" }, RACK_POOLS["town"]);
                continue;
            }
            if (r() < 0.12f) { CoverWall(x, z, 4, true, y); StashAt(x + 1, z + 1.6f, y, new[] { "weapon", "ammo" }, RACK_POOLS["town"]); }   // an abandoned stash behind a wall
            else if (r() < 0.55f) CoverWall(x, z, 3 + r() * 4, r() < 0.5f, y, 1.3f + r() * 0.4f, r() < 0.5f ? "cover" : "wallB");
            else { float hh = 2 + r() * 4; AddBox(x - 0.8f, y - 1, z - 0.8f, x + 0.8f, y + hh, z + 0.8f, new[] { "wallA", "wallB", "wallC" }[(int)MathF.Floor(r() * 3)]); AddCoverPts(x - 0.8f, z - 0.8f, x + 0.8f, z + 0.8f); }
        }
        FlushMerged();
        FlushChunks();
        for (int i = coverPts.Count - 1; i >= 0; i--) { var c = coverPts[i]; c.y = GroundAt(c.x, c.z, 999, true).h; if (InsideStatic(c.x, c.z, c.y, 0.3f)) coverPts.RemoveAt(i); }
    }
    // a man-sized collision probe: is (x, z) inside a static box?
    public bool InsideStatic(float x, float z, float y, float pad)
    {
        QueryStatics(x - pad, x + pad, z - pad, z + pad, QHs);
        foreach (var o in QHs) if (o.Kind == BOXK && x > o.MinX - pad && x < o.MaxX + pad && z > o.MinZ - pad && z < o.MaxZ + pad && o.MaxY > y + 0.5f && o.MinY < y + 1.6f) return true;
        return false;
    }
    public float GroundY(float x, float z) => GroundAt(x, z, 999, true).h;

    // =====================================================================
    // Physics props: spore pods, calcified nodules, stingers, toxin vesicles, pearls and bones
    // =====================================================================
    Material rockMat, shaftMat, tipMat, bombMat, bombLit, bombDim, podCore, crateMat, blockMat, steelMat, girderMat, ballMat; Material[] containerMats;
    Geometry podGeo, boulderGeo, ballGeo, spearGeoS, spearGeoT, bombLitGeo;
    void InitPropMats()
    {
        rockMat = Lam(0xf0e2c8); shaftMat = Phong(0xb07a4a, 20); tipMat = Phong(0xfff2d0, 120, 0xffffff);
        bombMat = Phong(0x4adf3a, 110, 0xffffff); bombMat.Transparent = true; bombMat.Opacity = 0.85f; bombLit = BasicM(0xc4ff3a); bombDim = BasicM(0x2a4a10);
        podGeo = Geo.Icosahedron(1, 2); podCore = BasicM(0x6a8a2a);
        crateMat = Phong(0xd8f090, 60); crateMat.Map = texCrate; blockMat = Lam(0xffe0a8, texHazard); steelMat = Phong(0xf8f2ff, 140, 0xffffff); girderMat = Phong(0xf2e4c8, 40); ballMat = Phong(0x7ad99a, 70);
        boulderGeo = Geo.Icosahedron(1, 1);
        for (int i = 0; i < boulderGeo.VertexCount; i++)
        {
            float x = boulderGeo.Position[i * 3], y = boulderGeo.Position[i * 3 + 1], z = boulderGeo.Position[i * 3 + 2], k = 0.85f + Hash2((int)MathF.Round(x * 30) + 3, (int)MathF.Round(y * 30) * 17 + (int)MathF.Round(z * 30)) * 0.25f;
            boulderGeo.Position[i * 3] = x * k; boulderGeo.Position[i * 3 + 1] = y * k; boulderGeo.Position[i * 3 + 2] = z * k;
        }
        boulderGeo = boulderGeo.Flat();
        ballGeo = Geo.Sphere(1, 14, 10); spearGeoS = Geo.Cylinder(0.022f, 0.022f, 1.62f, 6); spearGeoT = Geo.Cone(0.05f, 0.2f, 6); bombLitGeo = Geo.Sphere(0.09f, 8, 6);
        containerMats = new uint[] { 0xd86a7a, 0x6ab8c8, 0xe8c070, 0x7ac88a, 0xb07ac8 }.Select(c => Toon(Lam(c, texPanel), 4)).ToArray();   // diatom frustules
    }
    Body PropBody(Node vis, int kind, float x, float y, float z, float m, V3 he = null, float r = 0, float e = 0.2f, float mu = 0.6f, float roll = 0.6f, float yaw = 0)
    {
        foreach (var n in Walk(vis)) if (n is Mesh mm) { mm.CastShadow = true; mm.ReceiveShadow = true; }
        scene.Add(vis);
        var b = MakeBody(kind, x, y, z, m, he, r, e, mu, roll, yaw); b.Prop = new Prop { vis = vis }; return b;
    }
    static IEnumerable<Node> Walk(Node n) { yield return n; foreach (var c in n.Children) foreach (var d in Walk(c)) yield return d; }
    Body AddBoxBody(float x, float y, float z, float sx, float sy, float sz, Material mat, float density, float yaw = 0, float e = 0.12f, float mu = 0.6f)
    {
        var mesh = new Mesh(boxGeo, mat) { Scale = new Vector3(sx, sy, sz) };
        var g = new Node(); g.Add(mesh);
        return PropBody(g, BOXB, x, y + sy / 2, z, density * sx * sy * sz, he: new V3(sx / 2, sy / 2, sz / 2), e: e, mu: mu, yaw: yaw);
    }
    Body AddSphereBody(float x, float y, float z, float r, float m, Material mat, Geometry geo, float roll, float e)
    {
        var mesh = new Mesh(geo, mat) { Scale = new Vector3(r) }; var g = new Node(); g.Add(mesh);
        return PropBody(g, SPH, x, y + r, z, m, r: r, e: e, mu: 0.8f, roll: roll);
    }
    Body MakeProp(string kind, float x, float y, float z, float? yawIn = null)
    {
        switch (kind)
        {
            case "crate":
                {   // a spore pod: a bulging membrane round a dark core (a box underneath, for stacking)
                    float s = 1.1f + R() * 0.4f; var g = new Node();
                    g.Add(new Mesh(podGeo, crateMat) { Scale = new Vector3(s * 0.64f, s * 0.6f, s * 0.64f) });
                    g.Add(new Mesh(ballGeo, podCore) { Scale = new Vector3(s * 0.18f), Position = new Vector3(0, s * 0.05f, 0) });
                    return PropBody(g, BOXB, x, y + s / 2, z, 45 * s * s * s, he: new V3(s / 2, s / 2, s / 2), e: 0.15f, mu: 0.6f, yaw: yawIn ?? R());
                }
            case "ball": return AddSphereBody(x, y, z, 0.8f, 20, ballMat, ballGeo, 0.9f, 0.45f);
            case "rock": { float r = 0.28f + R() * 0.22f; var b = AddSphereBody(x, y, z, r, MathF.Round(260 * r * r * r * 4) + 4, rockMat, boulderGeo, 1.6f, 0.2f); b.Prop.rock = true; return b; }
            case "spear":
                {
                    var g = new Node(); var sh = new Mesh(spearGeoS, shaftMat); sh.SetEuler(PI / 2, 0, 0); g.Add(sh);
                    var tip = new Mesh(spearGeoT, tipMat); tip.SetEuler(-PI / 2, 0, 0); tip.Position = new Vector3(0, 0, -0.9f); g.Add(tip);
                    var b = PropBody(g, BOXB, x, y + 0.04f, z, 5, he: new V3(0.035f, 0.035f, 0.9f), e: 0.1f, mu: 0.5f, yaw: yawIn ?? R() * 6.28f); b.Spear = true; return b;
                }
            case "bomb":
                {
                    var g = new Node(); g.Add(new Mesh(ballGeo, bombMat) { Scale = new Vector3(0.4f) });
                    var lit = new Mesh(bombLitGeo, bombDim) { Position = new Vector3(0, 0.43f, 0) }; g.Add(lit);
                    var b = PropBody(g, SPH, x, y + 0.4f, z, 14, r: 0.4f, e: 0.3f, mu: 0.7f, roll: 1.4f); b.Bomb = true; b.Fuse = 0; b.Prop.lit = lit; return b;
                }
            case "block": { var b = AddBoxBody(x, y, z, 1.9f, 1.9f, 1.9f, blockMat, 300, yawIn ?? R() * 6.28f, 0.05f, 0.8f); b.Prop.heavy = true; return b; }
            case "steelball": { var b = AddSphereBody(x, y, z, 1.1f, 1600, steelMat, ballGeo, 0.35f, 0.15f); b.Prop.heavy = true; return b; }
            // huge things the large mechs hurl
            case "boulder": { float r = 1.5f + R() * 0.5f; var b = AddSphereBody(x, y, z, r, MathF.Round(900 * r * r * r), rockMat, boulderGeo, 1.2f, 0.1f); b.Prop.heavy = true; b.Prop.rock = true; return b; }
            case "container": { var b = AddBoxBody(x, y, z, 2.4f, 2.4f, 5.6f, containerMats[(int)(R() * containerMats.Length)], 260, yawIn ?? R() * 6.28f, 0.05f, 0.7f); b.Prop.heavy = true; return b; }
            case "girder":
                {   // a long bone: shaft and knobbly ends
                    var g = new Node(); var sh = new Mesh(spearGeoS, girderMat) { Scale = new Vector3(6, 2.4f, 6) }; sh.SetEuler(PI / 2, 0, 0); g.Add(sh);
                    foreach (float e in new[] { -1f, 1f }) foreach (float k in new[] { -1f, 1f }) g.Add(new Mesh(ballGeo, girderMat) { Scale = new Vector3(0.23f), Position = new Vector3(k * 0.12f, 0, e * 2.02f) });
                    var b = PropBody(g, BOXB, x, y + 0.225f, z, 2400 * 0.45f * 0.45f * 4.5f, he: new V3(0.225f, 0.225f, 2.25f), e: 0.05f, mu: 0.7f, yaw: yawIn ?? R() * 6.28f); b.Prop.heavy = true; return b;
                }
        }
        throw new ArgumentException("unknown prop " + kind);
    }
    public Body PlaceProp(string kind, float x, float z, float extraY = 0)
    {
        int tries = 0; while (InsideStatic(x, z, GroundY(x, z), 0.8f) && tries++ < 8) { x += (R() - 0.5f) * 4; z += (R() - 0.5f) * 4; }
        var b = MakeProp(kind, x, GroundY(x, z) + extraY, z); b.Prop.kind = kind; return b;
    }
    void PlaceProps()
    {
        var r = Rng(77);
        foreach (var O in OUTPOSTS)
        {
            for (int i = 0; i < 4; i++) PlaceProp("crate", O.x + (r() - 0.5f) * 30, O.z + (r() - 0.5f) * 30);
            for (int i = 0; i < 5; i++) PlaceProp("rock", O.x + (r() - 0.5f) * 34, O.z + (r() - 0.5f) * 34, 0.05f);
            for (int i = 0; i < 2; i++) PlaceProp("spear", O.x + (r() - 0.5f) * 26, O.z + (r() - 0.5f) * 26, 0.02f);
            for (int i = 0; i < 2; i++) PlaceProp("bomb", O.x + (r() - 0.5f) * 26, O.z + (r() - 0.5f) * 26);
            PlaceProp("block", O.x + 12, O.z + 6); PlaceProp("block", O.x - 10, O.z - 12); PlaceProp("steelball", O.x - 6, O.z + 12, 0.05f); PlaceProp("girder", O.x + 9, O.z - 10, 0.02f);
        }
        foreach (var T in TOWNS)
        {
            for (int i = 0; i < 4; i++) PlaceProp("crate", T.x + (r() - 0.5f) * 30, T.z + (r() - 0.5f) * 30);
            for (int i = 0; i < 4; i++) PlaceProp("rock", T.x + (r() - 0.5f) * 30, T.z + (r() - 0.5f) * 30, 0.05f);
            PlaceProp("bomb", T.x + (r() - 0.5f) * 20, T.z + (r() - 0.5f) * 20); PlaceProp("spear", T.x + (r() - 0.5f) * 20, T.z + (r() - 0.5f) * 20, 0.02f);
            PlaceProp("block", T.x + (r() - 0.5f) * 24, T.z + (r() - 0.5f) * 24);
        }
        float rockR = LAYOUT.Bowl != null ? LAYOUT.Bowl.R - 20 : 260;
        for (int i = 0; i < 26; i++) { float a = r() * 6.28f, d = 20 + r() * rockR; PlaceProp("rock", MathF.Cos(a) * d, MathF.Sin(a) * d, 0.05f); }
        float bx = HOMEPAD.X, bz = HOMEPAD.Z;
        foreach (var (x, z) in new[] { (6f, -10f), (-8f, -12f), (10f, -16f) }) PlaceProp("crate", bx + x, bz + z);
        PlaceProp("ball", bx - 10, bz - 6, 0.1f); PlaceProp("ball", bx + 12, bz - 4, 0.1f); PlaceProp("bomb", bx + 4, bz - 18);
        if (LAYOUT.Bowl != null) for (int i = 0; i < 10; i++) { float a = r() * 6.28f, d = 40 + r() * (LAYOUT.Bowl.R - 60); PlaceProp(new[] { "crate", "block", "bomb", "steelball", "girder", "crate" }[i % 6], MathF.Cos(a) * d, MathF.Sin(a) * d, 0.05f); }
        foreach (var (x, z, yaw, kind) in PARKED)
        {
            float pad = kind == "rover" ? 2.6f : 1.2f, px = x, pz = z; int k = 0;
            while (InsideStatic(px, pz, GroundY(px, pz), pad) && k++ < 14) { px += (R() - 0.5f) * 6; pz += (R() - 0.5f) * 6; }
            MakeVehicle(kind ?? "bike", px, pz, yaw, "neutral");
        }
        for (int i = 0; i < 300; i++) { BuildBodyGrid(); StepBodies(DT); }   // settle before the first frame
        foreach (var b in bodies) { b.Pp.Copy(b.Pos); b.Pq.Copy(b.Q); }
    }
}

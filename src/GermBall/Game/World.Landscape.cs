using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Core;
using GermBall.Render;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed class CoverPt { public float x, y, z, nx, nz; public Actor by; }

public sealed partial class World
{
    // ---------------------------------------------------------------- render setup
    public readonly Scene scene = new();
    static Vector3 C(string hex) { var v = Canvas2D.Parse(hex); return new Vector3(v.X, v.Y, v.Z); }
    static Vector3 C(uint hex) => Material.Hex(hex);
    static Material Lam(uint c, Texture map = null, bool vc = false) => new() { Kind = MatKind.Lambert, Color = C(c), Map = map, VertexColors = vc };
    static Material Phong(uint c, float shin = 30, uint spec = 0x2a2f36) => new() { Kind = MatKind.Phong, Color = C(c), Shininess = shin, Specular = C(spec) };
    static Material BasicM(uint c) => new() { Kind = MatKind.Basic, Color = C(c) };
    static Material Toon(Material m, float steps = 4) { m.Toon = steps; return m; }
    static Texture CanvasTex(int w, int h, Action<Canvas2D, int, int> draw, bool repeat)
    {
        var c = new Canvas2D(w, h); draw(c, w, h); var t = c.ToTexture(repeat); t.Nearest = true; return t;
    }

    // sky themes: horizon (also the fog), zenith, nebula palette phases, sun tint
    public sealed record SkyTheme(string Hor, string Zen, float[] Pal, uint Planet, uint Ring, string Far, string Near, uint Eye);
    static readonly SkyTheme[] SKY_THEMES =
    {
        new("#d6a4e6", "#140a3a", new[] { 0.0f, 0.33f, 0.67f }, 0xffb36b, 0xffe0f5, "#5a3a8a", "#3a2266", 0xff4fd8),
        new("#ffb38a", "#2a0636", new[] { 0.05f, 0.15f, 0.45f }, 0x8ff7ff, 0xffd0a0, "#8a3a5a", "#4a1a3a", 0x7affef),
        new("#8ad8ff", "#08103a", new[] { 0.55f, 0.65f, 0.95f }, 0xff7ae0, 0xc4f2ff, "#3a5a9a", "#1a2a5a", 0xffe14a),
        new("#b6ff9a", "#1a0830", new[] { 0.3f, 0.5f, 0.0f }, 0xffe66d, 0xd0ffb0, "#3a7a5a", "#1a3a3a", 0xff3048),
    };
    // inside the leviathan: no sky, a dark crimson haze that closes in
    static readonly SkyTheme ORGANIC_THEME = new("#3c0a16", "#120206", new[] { 0.0f, 0.04f, 0.08f }, 0xff6060, 0xffb0b0, "#4a0c1a", "#2a0610", 0xffd040);
    SkyTheme THEME; Vector3 HORIZON;

    void InitScene()
    {
        THEME = LAYOUT.Organic ? ORGANIC_THEME : SKY_THEMES[LAYOUT.Sky % SKY_THEMES.Length];
        HORIZON = C(THEME.Hor);
        scene.FogColor = HORIZON; scene.Background = HORIZON;
        // no browser memory ceiling: the haze sits further out and the draw distance follows the setting
        if (LAYOUT.Organic) { scene.FogNear = 30; scene.FogFar = 300; } else { scene.FogNear = 130; scene.FogFar = 680; }
        if (LAYOUT.Organic) { scene.HemiSky = C(0xffb8c0); scene.HemiGround = C(0x4a0c1e); scene.HemiIntensity = 0.9f; }
        else { scene.HemiSky = C(0xffd6f4); scene.HemiGround = C(0x3b2766); scene.HemiIntensity = 0.78f; }
        scene.SunDir = Vector3.Normalize(new Vector3(-0.35f, 0.8f, 0.45f));
        scene.SunColor = C(LAYOUT.Organic ? 0xffc8b8u : 0xfff1e0u); scene.SunIntensity = LAYOUT.Organic ? 0.6f : 0.85f;
        scene.ShadowHalf = 38; scene.ShadowNear = 1; scene.ShadowFar = 260;
        ApplyGraphics();
    }
    // graphics settings: shadows and decoration density (draw distance and debris are read where they're used)
    public void ApplyGraphics()
    {
        int sh = SET.shadows; scene.Shadows = sh > 0;
        // the browser capped shadow maps at 2048; natively the setting can go to 4096
        scene.ShadowMapSize = Math.Clamp(SET.shadowRes, 1024, 4096) >> (sh >= 2 ? 0 : 1);
        if (sh >= 2) scene.ShadowHalf = 52; else scene.ShadowHalf = 38;
        ApplyDecor();
    }

    static readonly Dictionary<string, string> ORGAN_COL = new() { ["heart"] = "#9a1024", ["lung"] = "#ff9eb2", ["liver"] = "#6a1420", ["kidney"] = "#8a2232", ["stomach"] = "#e88a90", ["brain"] = "#f0b8c4" };
    // terrain: multicolour gradient bands, contour lines, glowing lichen and checked plazas
    static readonly Vector3[] PAL = new[] { "#2b1055", "#7a1fa2", "#ff3cac", "#ff9a3c", "#ffe66d", "#3cffb4", "#1fb5ff", "#4a35d6" }.Select(C).ToArray();
    Vector3 Palette(float u)
    {
        u += LAYOUT.Hue; u = ((u % 1) + 1) % 1; float f = u * PAL.Length; int i = (int)MathF.Floor(f); float t = f - i;
        return Vector3.Lerp(PAL[i % PAL.Length], PAL[(i + 1) % PAL.Length], t * t * (3 - 2 * t));
    }
    Mesh terrainMesh;
    void BuildTerrainMesh()
    {
        var pos = new float[N * N * 3]; var nor = new float[N * N * 3]; var col = new float[N * N * 3];
        Vector3 ROCK = C("#3a2659"), LICHEN = C("#c8ff3a"), PLAZA_A = C("#d9ccff"), PLAZA_B = C("#b7a6f2"), GLOW = C("#3dffe0"), CANY = C("#ff6ad5");
        System.Threading.Tasks.Parallel.For(0, N, j =>
        {
            for (int i = 0; i < N; i++)
            {
                int k = (j * N + i) * 3; float x = -HALF + i * CELL, z = -HALF + j * CELL; var t = TerrainSample(x, z); float h = heights[j * N + i], nl = Hypot(t.gx, 1, t.gz), ny = 1 / nl;
                pos[k] = x; pos[k + 1] = h; pos[k + 2] = z; nor[k] = -t.gx / nl; nor[k + 1] = ny; nor[k + 2] = -t.gz / nl;
                float n1 = VNoise(x * 0.006f, z * 0.006f), n2 = VNoise(x * 0.021f + 5, z * 0.021f - 3);
                Vector3 c;
                if (LAYOUT.Organic)
                {
                    c = OrganicColor(x, z, h, ny);
                    foreach (var p in PADS)
                    {
                        float d = Hypot(x - p.X, z - p.Z); if (d > p.R + 2) continue; bool chk = ((FloorI(x / 6) + FloorI(z / 6)) & 1) != 0;
                        var c2 = C(p.Kind == "base" ? (chk ? "#b9f4ff" : "#8ddff2") : p.Kind == "outpost" ? (chk ? "#3a2a2e" : "#4a3036") : (chk ? "#f2b8c4" : "#e6a0b0"));
                        c = Vector3.Lerp(c, c2, Smooth(p.R + 2, p.R - 3, d) * (p.Kind == "town" ? 0.4f : 0.85f));
                    }
                }
                else
                {
                    c = Palette(0.55f + 0.33f * n1 + h * 0.011f + 0.12f * n2);
                    c = Vector3.Lerp(c, Vector3.One, 0.12f);
                    float band = ((h * 0.4f) % 1 + 1) % 1; if (band < 0.12f) c *= 0.7f;   // contour lines
                    if (VNoise(x * 0.13f + 11, z * 0.13f - 7) > 0.5f) c = Vector3.Lerp(c, LICHEN, 0.55f);
                    c = Vector3.Lerp(c, ROCK, Smooth(0.86f, 0.6f, ny));
                    foreach (var d in CRATERS) { float r = Hypot(x - d.X, z - d.Z); if (r < d.R) c = Vector3.Lerp(c, GLOW, 0.5f * (1 - r / d.R)); }
                    foreach (var ch in CHANNELS) { var q = ChanDist(ch, x, z); if (q.D < ch.W + 1) c = Vector3.Lerp(c, CANY, Smooth(ch.W + 1, ch.W - 3, q.D) * (0.5f + 0.3f * MathF.Sin(q.S * 0.3f))); }
                    foreach (var p in PADS)
                    {
                        float d = Hypot(x - p.X, z - p.Z); if (d > p.R + 2) continue;
                        bool chk = ((FloorI(x / 6) + FloorI(z / 6)) & 1) != 0;
                        var bse = p.Kind == "outpost" ? C(chk ? "#5b2a4f" : "#7a3a63") : p.Kind == "base" ? C(chk ? "#b9f4ff" : "#8ddff2") : (chk ? PLAZA_A : PLAZA_B);
                        c = Vector3.Lerp(c, bse, Smooth(p.R + 2, p.R - 3, d) * 0.85f);
                    }
                }
                float v = 0.94f + Hash2(i, j) * 0.1f; col[k] = c.X * v; col[k + 1] = c.Y * v; col[k + 2] = c.Z * v;
            }
        });
        var idx = new uint[SEG * SEG * 6]; int qi = 0;
        for (int j = 0; j < SEG; j++) for (int i = 0; i < SEG; i++) { uint a = (uint)(j * N + i), b = a + 1, c3 = a + N, d = c3 + 1; idx[qi++] = a; idx[qi++] = c3; idx[qi++] = b; idx[qi++] = b; idx[qi++] = c3; idx[qi++] = d; }
        var g = new Geometry { Position = pos, Normal = nor, Color = col, Index = idx };
        terrainMesh = new Mesh(g, Lam(0xffffff, vc: true)) { ReceiveShadow = true, FrustumCulled = false };
        scene.Add(terrainMesh);
        if (!LAYOUT.Organic)
        {   // distant mountains: tall crystalline cones fading into the haze
            var cg = Geo.Cone(1, 1, 7, 2); var r = Rng(9); var mats = new[] { "#6a3fb0", "#8f3f9e", "#3f5fb0", "#b0507a" }.Select(c => new Material { Color = C(c) }).ToArray();
            for (int i = 0; i < 54; i++)
            {
                float ang = (i / 54f) * TAU + r() * 0.1f, dist = 520 + r() * 150; var mesh = new Mesh(cg, mats[i % 4]);
                float s = 50 + r() * 70; mesh.Scale = new Vector3(s, 120 + r() * 200, s);
                mesh.Position = new Vector3(MathF.Cos(ang) * dist, 60, MathF.Sin(ang) * dist); mesh.SetEuler(0, r() * 6, 0); scene.Add(mesh);
            }
        }
    }
    // flesh: pink on the folds, deep crimson in the creases, dark vein traceries, pale fatty patches, organs in their own colours
    Vector3 OrganicColor(float x, float z, float h, float ny)
    {
        float X = x + LAYOUT.Ox, Z = z + LAYOUT.Oz;
        var c = Vector3.Lerp(C("#d8566a"), C("#ff9aa6"), Clamp((h - 8) * 0.06f + VNoise(X * 0.03f, Z * 0.03f) * 0.3f, 0, 1));
        c = Vector3.Lerp(c, C("#5a0a1e"), Smooth(0.95f, 0.7f, ny) * 0.7f);
        float vn = MathF.Abs(VNoise(X * 0.045f + 3, Z * 0.045f - 7)); if (vn < 0.035f) c = Vector3.Lerp(c, C("#3a1460"), (1 - vn / 0.035f) * 0.75f);   // veins under the surface
        float ft = Fbm(X * 0.009f - 30 + 0.6f * Fbm(X * 0.02f, Z * 0.02f, 2), Z * 0.009f + 9, 3); if (ft > 0.38f) c = Vector3.Lerp(c, C("#ffd6a0"), Smooth(0.38f, 0.55f, ft) * 0.5f);   // fat
        foreach (var o in LAYOUT.Organs) { float d = Hypot(x - o.X, z - o.Z); if (d < o.W * 1.4f) c = Vector3.Lerp(c, C(ORGAN_COL[o.Kind]), Smooth(o.W * 1.4f, o.W * 0.5f, d) * 0.85f); }
        foreach (var g in LAYOUT.Guts) if (x > g.MinX && x < g.MaxX && z > g.MinZ && z < g.MaxZ && h > BaseH(x, z) + 0.5f) c = Vector3.Lerp(c, C("#f0a0b0"), 0.7f);
        foreach (var d in CRATERS) { float r = Hypot(x - d.X, z - d.Z); if (r < d.R + 3) c = Vector3.Lerp(c, C("#6a7a10"), Smooth(d.R + 3, d.R - 4, r) * 0.8f); }
        foreach (var ch in CHANNELS) { var q = ChanDist(ch, x, z); if (q.D < ch.W + 1) c = Vector3.Lerp(c, C("#7a0010"), Smooth(ch.W + 1, ch.W - 2, q.D) * 0.85f); }
        return c;
    }

    // =====================================================================
    // Structures: every static box goes into one merged mesh per material
    // =====================================================================
    readonly Dictionary<string, List<float[]>> MERGE = new();
    Texture texPanel, texWindows, texHazard, texCrate;
    Dictionary<string, Material> SMAT;
    void InitStructureMats()
    {
        // structures are grown, not built: cell-wall honeycomb, pores, chitin stripes, membrane pods
        texPanel = CanvasTex(64, 64, (g, w, h) =>
        {
            g.FillStyle = "#ffffff"; g.FillRect(0, 0, w, h); g.StrokeStyle = "#cfc0d8"; g.LineWidth = 3;
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                {
                    float cx = x * 24 + (y % 2) * 12 - 4, cy = y * 22 + 8; g.BeginPath();
                    for (int k = 0; k < 6; k++) { float a = k / 6f * TAU; g.LineTo(cx + MathF.Cos(a) * 12, cy + MathF.Sin(a) * 11); }
                    g.ClosePath(); g.Stroke(); g.FillStyle = "#e9dcef"; g.BeginPath(); g.Arc(cx + 2, cy + 1, 3, 0, 7); g.Fill();
                }
        }, true);
        texWindows = CanvasTex(64, 64, (g, w, h) =>
        {
            g.FillStyle = "#3a1830"; g.FillRect(0, 0, w, h);
            var r = Rng(5);
            for (int y = 8; y < h; y += 16) for (int x = 6; x < w; x += 14) { g.FillStyle = r() < 0.6f ? "#ffb0e0" : r() < 0.5f ? "#9affd0" : "#5a2a48"; g.BeginPath(); g.Ellipse(x + 3, y + 3, 4, 5, 0, 0, 7); g.Fill(); }
        }, true);
        texHazard = CanvasTex(64, 64, (g, w, h) =>
        {
            g.FillStyle = "#fff"; g.FillRect(0, 0, w, h); g.FillStyle = "#8a5a48";
            for (int i = 0; i < 4; i++) { g.BeginPath(); g.Ellipse(32, i * 16 + 8, 34, 4, 0, 0, 7); g.Fill(); }
        }, true);
        texCrate = CanvasTex(64, 64, (g, w, h) =>
        {
            g.FillStyle = "#fff"; g.FillRect(0, 0, w, h); g.FillStyle = "#c8e890"; for (int i = 0; i < 9; i++) { g.BeginPath(); g.Arc(8 + (i % 3) * 24, 8 + (i / 3) * 24, 7, 0, 7); g.Fill(); }
            g.FillStyle = "#6a8a3a"; g.BeginPath(); g.Arc(32, 32, 9, 0, 7); g.Fill();
        }, false);
        SMAT = new()
        {
            ["wallA"] = Lam(0x9fe8c8, texPanel), ["wallB"] = Lam(0xf2a8c0, texPanel), ["wallC"] = Lam(0xffdca8, texPanel),
            ["tower"] = Lam(0xffffff, texWindows), ["dark"] = Lam(0x5a2440), ["trim"] = BasicM(0x7affef), ["trimHot"] = BasicM(0xff4fd8),
            ["cover"] = Lam(0xf0e0c8, texHazard), ["crate"] = Lam(0xd8f0a0, texCrate), ["stash"] = new Material { Kind = MatKind.Phong, Color = C(0x6dffb0), Emissive = C(0x1e8a55), Shininess = 80 },
            ["fence"] = Lam(0x8a4a48),
        };
    }
    void AddBox(float x0, float y0, float z0, float x1, float y1, float z1, string mat, bool collide = true)
    {
        if (collide) AddStatic(new Static { Kind = BOXK, MinX = x0, MaxX = x1, MinY = y0, MaxY = y1, MinZ = z0, MaxZ = z1 });
        if (!MERGE.TryGetValue(mat, out var l)) MERGE[mat] = l = new();
        l.Add(new[] { x0, y0, z0, x1, y1, z1 });
    }
    void FlushMerged()
    {
        const float T = 3;   // texture tile size in metres
        foreach (var (key, list) in MERGE)
        {
            int n = list.Count * 36; var pos = new float[n * 3]; var nor = new float[n * 3]; var uv = new float[n * 2]; int v = 0;
            void Quad(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, float dx, float dy, float dz, float nx, float ny, float nz, float uw, float vh)
            {
                float[][] Pp = { new[] { ax, ay, az, 0, 0 }, new[] { bx, by, bz, uw, 0 }, new[] { cx, cy, cz, uw, vh }, new[] { ax, ay, az, 0, 0 }, new[] { cx, cy, cz, uw, vh }, new[] { dx, dy, dz, 0, vh } };
                foreach (var p in Pp) { pos[v * 3] = p[0]; pos[v * 3 + 1] = p[1]; pos[v * 3 + 2] = p[2]; nor[v * 3] = nx; nor[v * 3 + 1] = ny; nor[v * 3 + 2] = nz; uv[v * 2] = p[3]; uv[v * 2 + 1] = p[4]; v++; }
            }
            foreach (var b in list)
            {
                float x0 = b[0], y0 = b[1], z0 = b[2], x1 = b[3], y1 = b[4], z1 = b[5], w = (x1 - x0) / T, h = (y1 - y0) / T, d = (z1 - z0) / T;
                Quad(x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1, 0, 0, 1, w, h);   // +z
                Quad(x1, y0, z0, x0, y0, z0, x0, y1, z0, x1, y1, z0, 0, 0, -1, w, h);  // -z
                Quad(x1, y0, z1, x1, y0, z0, x1, y1, z0, x1, y1, z1, 1, 0, 0, d, h);   // +x
                Quad(x0, y0, z0, x0, y0, z1, x0, y1, z1, x0, y1, z0, -1, 0, 0, d, h);  // -x
                Quad(x0, y1, z1, x1, y1, z1, x1, y1, z0, x0, y1, z0, 0, 1, 0, w, d);   // top
                Quad(x0, y0, z0, x1, y0, z0, x1, y0, z1, x0, y0, z1, 0, -1, 0, w, d);  // bottom
            }
            var g = new Geometry { Position = pos, Normal = nor, Uv = uv };
            var m = new Mesh(g, SMAT[key]) { CastShadow = key != "trim" && key != "trimHot", ReceiveShadow = true }; scene.Add(m);
        }
        MERGE.Clear();
    }
    // cover points: places to crouch behind low walls, each with the direction it faces away from the wall
    public readonly List<CoverPt> coverPts = new();
    void AddCoverPts(float x0, float z0, float x1, float z1)
    {
        float lx = x1 - x0, lz = z1 - z0;
        if (lx >= lz) for (float x = x0 + 0.8f; x <= x1 - 0.5f; x += 2) { coverPts.Add(new CoverPt { x = x, z = z0 - 0.75f, nx = 0, nz = -1 }); coverPts.Add(new CoverPt { x = x, z = z1 + 0.75f, nx = 0, nz = 1 }); }
        else for (float z = z0 + 0.8f; z <= z1 - 0.5f; z += 2) { coverPts.Add(new CoverPt { x = x0 - 0.75f, z = z, nx = -1, nz = 0 }); coverPts.Add(new CoverPt { x = x1 + 0.75f, z = z, nx = 1, nz = 0 }); }
    }
    void CoverWall(float x, float z, float len, bool alongX, float y, float h = 1.4f, string mat = "cover")
    {
        const float t = 0.45f; float x0 = alongX ? x - len / 2 : x - t / 2, x1 = alongX ? x + len / 2 : x + t / 2, z0 = alongX ? z - t / 2 : z - len / 2, z1 = alongX ? z + t / 2 : z + len / 2;
        AddBox(x0, y - 0.3f, z0, x1, y + h, z1, mat); AddCoverPts(x0, z0, x1, z1);
    }
    void CrateStack(float x, float z, float y, int n = 2)
    {
        AddBox(x - 0.65f, y - 0.2f, z - 0.65f, x + 0.65f, y + 1.1f, z + 0.65f, "crate");
        if (n > 1) AddBox(x + 0.75f, y - 0.2f, z - 0.55f, x + 1.95f, y + 1.0f, z + 0.65f, "crate");
        if (n > 2) AddBox(x + 0.1f, y + 1.1f, z - 0.5f, x + 1.2f, y + 2.1f, z + 0.6f, "crate");
        AddCoverPts(x - 0.65f, z - 0.65f, x + (n > 1 ? 1.95f : 0.65f), z + 0.65f);
    }
    void Arch(float x, float z, float span, bool alongX, float y, float h = 4.2f)
    {
        const float p = 0.9f;
        if (alongX) { AddBox(x - span / 2 - p, y - 0.4f, z - p / 2, x - span / 2, y + h, z + p / 2, "wallB"); AddBox(x + span / 2, y - 0.4f, z - p / 2, x + span / 2 + p, y + h, z + p / 2, "wallB"); AddBox(x - span / 2 - p, y + h, z - p / 2, x + span / 2 + p, y + h + 0.8f, z + p / 2, "dark"); }
        else { AddBox(x - p / 2, y - 0.4f, z - span / 2 - p, x + p / 2, y + h, z - span / 2, "wallB"); AddBox(x - p / 2, y - 0.4f, z + span / 2, x + p / 2, y + h, z + span / 2 + p, "wallB"); AddBox(x - p / 2, y + h, z - span / 2 - p, x + p / 2, y + h + 0.8f, z + span / 2 + p, "dark"); }
    }
    void StashAt(float x, float z, float y, string[] kinds, (string, float)[] pool = null)
    {
        AddBox(x - 0.7f, y - 0.3f, z - 0.5f, x + 0.7f, y + 0.55f, z + 0.5f, "stash");
        stashes.Add(new Stash { x = x, z = z, y = y + 0.55f, kinds = kinds, pool = pool, every = 30 });
    }
    // weapon racks on building walls: a dark board with one weapon hanging on it, restocked after a while
    static readonly Dictionary<string, (string, float)[]> RACK_POOLS = new()
    {
        ["base"] = new[] { ("pistol", 20f), ("smg", 20f), ("shotgun", 18f), ("rifle", 16f), ("bow", 10f), ("bat", 10f), ("revolver", 8f), ("knives", 8f), ("laserpistol", 8f) },
        ["town"] = new[] { ("rifle", 14f), ("shotgun", 12f), ("hunting", 12f), ("revolver", 10f), ("bow", 10f), ("needler", 10f), ("plasma", 10f), ("hammer", 8f), ("sledge", 8f), ("bat", 8f), ("knives", 8f), ("sniper", 5f), ("rocket", 5f), ("armcannon", 5f) },
        ["outpost"] = new[] { ("rocket", 16f), ("sniper", 14f), ("chargecannon", 14f), ("armcannon", 14f), ("hammer", 12f), ("sledge", 10f), ("needler", 10f), ("plasma", 10f) },
    };
    void RackOn(Building B, int side, string pool)
    {
        // side 0..3: +z, -z, +x, -x face of the building
        float hw = B.w / 2, hd = B.d / 2, y = B.y; const float t = 0.12f;
        float x = B.x, z = B.z, rot = 0, bx0, bx1, bz0, bz1;
        if (side < 2) { float s = side != 0 ? -1 : 1; z = B.z + s * (hd + 0.32f); rot = PI / 2; bx0 = x - 0.95f; bx1 = x + 0.95f; bz0 = B.z + s * hd + (s > 0 ? 0 : -t); bz1 = bz0 + t; }
        else { float s = side == 2 ? 1 : -1; x = B.x + s * (hw + 0.32f); rot = 0; bz0 = z - 0.95f; bz1 = z + 0.95f; bx0 = B.x + s * hw + (s > 0 ? 0 : -t); bx1 = bx0 + t; }
        AddBox(bx0, y + 0.65f, bz0, bx1, y + 1.75f, bz1, "dark", false);
        stashes.Add(new Stash { x = x, z = z, y = y + 0.6f, kinds = new[] { "weapon" }, pool = RACK_POOLS[pool], rack = true, rot = rot, every = 55 });
    }
}

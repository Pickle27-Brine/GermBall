using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Core;
using GermBall.Render;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed partial class World
{
    // =====================================================================
    // The leviathan's insides: a fleshy cavern roof, ribs and a spine overhead, beating and breathing organs, coiled
    // intestines, veins and hanging vessels, villi swaying on the floor, acid pits, giant cells, loose bones, and red cells,
    // white cells, platelets, bacteria, viruses, phages and antibodies drifting through the air.
    // (Every map is organic since the "microscopic" update; the old alien flora is no longer built.)
    // =====================================================================
    sealed class OrgAnim { public string kind; public Node G; public float ph; }
    sealed class Floater
    {
        public string kind; public Stream S; public float u, w, h, s = 1, sp, spin, ax, flat, x, y, z, a, ph, d, cx, cz, vx, vz, rx, ry;
        public Floater of; public bool hasF; public float fx, fy, fz, gx, gy, gz;
    }
    sealed class Stream { public float a, y, off, sp; }
    sealed class FloaterSet { public InstancedMesh im; public List<Floater> L; }
    sealed class AcidPool { public Crater d; public float level; public Mesh m; }
    sealed class OrgState
    {
        public List<OrgAnim> anim = new(); public List<FloaterSet> floaters = new(); public List<(Material m, Vector3 baseE)> veinMats = new(); public List<AcidPool> acid = new();
        public (Node G, Organ o)? heart; public bool built; public InstancedMesh villi, nodules; public int villiN; public Material villiMat; public float LIM; public Mesh roof, blood;
    }
    readonly OrgState ORG = new();
    float roofRX, roofRY, roofPY;
    float RoofY(float x, float z) => roofPY + roofRY * MathF.Sqrt(MathF.Max(0, 1 - (x * x + z * z) / (roofRX * roofRX)));

    static Quaternion QE(float x, float y, float z) { var q = new Q4(); QEuler(x, y, z, q); return new Quaternion(q.x, q.y, q.z, q.w); }
    static Matrix4x4 Mtx(float x, float y, float z, float sx, float sy, float sz, float rx = 0, float ry = 0, float rz = 0)
        => Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateFromQuaternion(QE(rx, ry, rz)) * Matrix4x4.CreateTranslation(x, y, z);
    // merge several posed geometries into one (position + normal), so a virus or a phage is one instanced shape
    static Geometry MergeGeo(params (Geometry g, Matrix4x4 m)[] parts) => Geometry.Merge(parts.Select(p => p.g.Clone().Apply(p.m)).ToList());

    void BuildLeviathan()
    {
        if (!LAYOUT.Organic) return;
        ORG.built = true;
        var r = Rng(LAYOUT.SpireSeed + 77); bool arena = LAYOUT.Bowl != null; float LIM = arena ? 86 : 330;
        Material Wet(uint c, float shin = 70, uint spec = 0x6a3040, uint emissive = 0) { var m = Toon(Phong(c, shin, spec), 5); m.Emissive = C(emissive); return m; }
        Material Matte(uint c) => Toon(Lam(c), 5);
        Material Glow(uint c, float op = 0) { var m = BasicM(c); if (op > 0) { m.Transparent = true; m.Opacity = op; m.DepthWrite = false; } return m; }
        Mesh Add(Mesh m, bool shadow = false) { m.CastShadow = shadow; m.ReceiveShadow = true; scene.Add(m); return m; }
        float Gy(float x, float z) => TerrainH(x, z);
        Mesh Tube(IList<float[]> pts, float rad, Material mat, int seg = 0, bool closed = false, bool solid = true)
        {
            var vp = pts.Select(p => new Vector3(p[0], p[1], p[2])).ToList();
            if (solid && rad > 0.12f)
            {   // wherever the tube runs within reach of the floor it's solid (sampled along its length)
                float len = 0; for (int q = 1; q < vp.Count; q++) len += Vector3.Distance(vp[q - 1], vp[q]);
                int ns = Math.Clamp((int)(len / MathF.Max(0.25f, rad * 0.7f)), 6, 600);   // samples closer than the radius: no gaps between them
                for (int q = 0; q <= ns; q++)
                {
                    var c = Geo.CatmullRom(vp, q / (float)ns, closed);
                    // the ground around the footprint, not just under the centre: on a slope the uphill side is closer
                    float g0 = Gy(c.X, c.Z), gHi = g0, gLo = g0;
                    for (int k = 0; k < 4; k++) { float gg = Gy(c.X + (k == 0 ? rad : k == 1 ? -rad : 0), c.Z + (k == 2 ? rad : k == 3 ? -rad : 0)); gHi = MathF.Max(gHi, gg); gLo = MathF.Min(gLo, gg); }
                    if (c.Y - rad < gHi + 2.6f && c.Y + rad > gLo - 0.5f) AddStatic(new Static { Kind = CYLK, X = c.X, Z = c.Z, R = rad + 0.1f, Bottom = gLo - 2, Top = c.Y + rad });
                }
            }
            return Add(new Mesh(Geo.Tube(vp, seg > 0 ? seg : pts.Count * 6, rad, 8, closed), mat), true);
        }

        // ---- the roof: a squashed dome of flesh, veined and glowing faintly, fading into the haze
        roofRX = arena ? 140 : 560; roofRY = arena ? 70 : 180; roofPY = arena ? 10 : -10;
        var roofMat = new Material { Shader = "roof", Side = Side.Back, Fog = false, U1 = new Vector4(C(THEME.Hor), 0) };
        ORG.roof = new Mesh(Geo.Sphere(1, 72, 36), roofMat) { Scale = new Vector3(roofRX, roofRY, roofRX), Position = new Vector3(0, roofPY, 0), RenderOrder = -1, FrustumCulled = false };
        scene.Add(ORG.roof);

        // (no bones: the ribs and vertebrae that arched overhead are gone, like the loose bones on the floor)

        // ---- organs
        var S = Geo.Sphere(1, 28, 18);
        foreach (var o in LAYOUT.Organs)
        {
            float y0 = Gy(o.X, o.Z); var G = new Node { Position = new Vector3(o.X, y0 - o.H * 0.55f, o.Z) }; G.SetEuler(0, -o.Ang, 0); scene.Add(G);
            float ca0 = MathF.Cos(-o.Ang), sa0 = MathF.Sin(-o.Ang);
            Mesh Piece(Geometry geo, Material mat, float x, float y, float z, float sx, float sy, float sz, float rx = 0, float ry = 0, float rz = 0, bool solid = true)
            {
                var m = new Mesh(geo, mat) { Position = new Vector3(x, y, z), Scale = new Vector3(sx, sy, sz), CastShadow = true, ReceiveShadow = true }; m.SetEuler(rx, ry, rz); G.Add(m);
                if (solid)
                {   // collision shaped to the piece: its centre in the world, a dome as wide and tall as the piece (turned pieces use their largest size)
                    float wx = o.X + x * ca0 + z * sa0, wz = o.Z - x * sa0 + z * ca0, wy = y0 - o.H * 0.55f + y;
                    bool turned = MathF.Abs(rx) + MathF.Abs(rz) > 0.3f;
                    float across = turned ? MathF.Max(sx, MathF.Max(sy, sz)) * 0.85f : MathF.Max(sx, sz), up = turned ? across : sy;
                    SolidDome(wx, wy, wz, across, up, Gy(wx, wz));
                }
                return m;
            }
            float w = o.W, h = o.H;
            void Solid(IList<float[]> pts, float rad)
            {   // the low parts of a vessel rising out of the floor get collision too
                for (int q = 0; q < pts.Count; q++) { var p = pts[q]; float g = Gy(p[0], p[2]); if (p[1] - g < 3) AddStatic(new Static { Kind = CYLK, X = p[0], Z = p[2], R = rad + 0.2f, Bottom = g - 2, Top = p[1] + rad }); }
            }
            if (o.Kind == "heart")
            {
                var m = Wet(0x9a1024); var fat = Wet(0xffd8a0, 30);
                Piece(S, m, 0, h * 0.55f, 0, w * 0.62f, h * 0.95f, w * 0.55f, 0, 0, 0.35f);
                Piece(S, m, w * 0.28f, h * 0.75f, w * 0.1f, w * 0.4f, h * 0.6f, w * 0.42f);
                Piece(S, fat, -w * 0.2f, h * 1.0f, -w * 0.1f, w * 0.3f, h * 0.25f, w * 0.3f);
                var art = Wet(0xc0182c); var ven = Wet(0x3a2a8a);
                float top = RoofY(o.X, o.Z);
                Tube(new[] { new[] { o.X, y0 + h * 0.3f, o.Z }, new[] { o.X + 4, y0 + h * 0.9f, o.Z }, new[] { o.X + 10, y0 + h * 1.6f, o.Z - 4 }, new[] { o.X + 14, (y0 + top) / 2, o.Z - 10 }, new[] { o.X + 8, top - 2, o.Z - 18 } }, w * 0.14f, art, 60);
                Tube(new[] { new[] { o.X - 3, y0 + h * 0.3f, o.Z + 2 }, new[] { o.X - 7, y0 + h * 1.2f, o.Z + 6 }, new[] { o.X - 16, (y0 + top) / 2, o.Z + 10 }, new[] { o.X - 22, top - 2, o.Z + 12 } }, w * 0.1f, ven, 60);
                ORG.heart = (G, o);
                for (int k = 0; k < 4; k++) { float a = k * 1.6f; Solid(new[] { new[] { o.X + MathF.Cos(a) * w * 0.6f, y0 + h * 0.1f, o.Z + MathF.Sin(a) * w * 0.6f } }, 0.35f); Tube(new[] { new[] { o.X + MathF.Cos(a) * w * 0.6f, y0 + h * 0.1f, o.Z + MathF.Sin(a) * w * 0.6f }, new[] { o.X + MathF.Cos(a + 0.4f) * w * 0.4f, y0 + h * 0.4f, o.Z + MathF.Sin(a + 0.4f) * w * 0.4f }, new[] { o.X + MathF.Cos(a + 0.6f) * w * 0.15f, y0 + h * 0.7f, o.Z + MathF.Sin(a + 0.6f) * w * 0.15f } }, 0.35f, art, 20); }
            }
            else if (o.Kind == "lung")
            {
                var m = Matte(0xff9eb2); var dk = Matte(0xd06a84); var geo = Geo.Icosahedron(1, 1);
                for (int k = 0; k < 46; k++) { float u = r() * 2 - 1, v = r() * 2 - 1, t = r(); Piece(geo, k % 5 != 0 ? m : dk, u * w * 0.7f, h * (0.3f + t * 0.9f), v * w * 1.1f, 3 + r() * 4, 3 + r() * 4, 3 + r() * 4, r() * 3, r() * 3, 0); }
                float top = RoofY(o.X, o.Z);
                Tube(new[] { new[] { o.X, y0 + h * 0.6f, o.Z }, new[] { o.X + 2, y0 + h * 1.3f, o.Z + 3 }, new[] { o.X, (y0 + top) / 2, o.Z + 6 }, new[] { o.X - 3, top - 2, o.Z + 4 } }, 1.6f, Wet(0xf0c8d0), 50);
                ORG.anim.Add(new OrgAnim { kind = "breathe", G = G, ph = o.Ph });
            }
            else if (o.Kind == "liver")
            {
                var m = Wet(0x6a1420, 90);
                Piece(S, m, -w * 0.25f, h * 0.6f, 0, w * 0.75f, h * 0.8f, w * 0.5f, 0, 0, 0.15f); Piece(S, m, w * 0.45f, h * 0.45f, w * 0.1f, w * 0.45f, h * 0.6f, w * 0.35f, 0, 0, -0.2f);
                Piece(S, Wet(0x4a6a20), w * 0.1f, h * 0.25f, w * 0.42f, w * 0.12f, h * 0.2f, w * 0.12f, solid: false);   // gallbladder
            }
            else if (o.Kind == "kidney")
            {
                var m = Wet(0x8a2232, 80);
                Piece(S, m, 0, h * 0.6f, -w * 0.25f, w * 0.5f, h * 0.9f, w * 0.42f, 0.4f, 0, 0); Piece(S, m, 0, h * 0.6f, w * 0.25f, w * 0.5f, h * 0.9f, w * 0.42f, -0.4f, 0, 0);
                var ureter = new[] { new[] { o.X, y0 + h * 0.5f, o.Z }, new[] { o.X + w, y0 + 1, o.Z + w * 0.5f }, new[] { o.X + w * 2.5f, y0 + 0.5f, o.Z + w } };
                Tube(ureter, 0.7f, Wet(0xf2d0b0), 20); Solid(ureter, 0.7f);
            }
            else if (o.Kind == "stomach")
            {
                var m = Wet(0xe88a90);
                Piece(S, m, 0, h * 0.6f, 0, w * 0.9f, h * 0.85f, w * 0.6f, 0, 0, 0.25f);
                var fold = Matte(0xc0505c); for (int k = 0; k < 7; k++) Piece(S, fold, -w * 0.6f + k * w * 0.2f, h * 1.25f, 0, w * 0.06f, h * 0.12f, w * 0.5f, solid: false);
                ORG.anim.Add(new OrgAnim { kind = "churn", G = G, ph = o.Ph });
            }
            else
            {   // a brain, of something else that lives in here
                var m = Matte(0xf0b8c4); var dk = Matte(0xd28a9a);
                Piece(S, m, 0, h * 0.6f, 0, w * 0.8f, h * 0.8f, w * 0.6f);
                var g2 = Geo.Sphere(1, 10, 8);
                for (int k = 0; k < 70; k++) { float a = r() * 6.28f, e = r() * 1.2f; Piece(g2, k % 3 != 0 ? m : dk, MathF.Cos(a) * MathF.Cos(e) * w * 0.78f, h * 0.6f + MathF.Sin(e) * h * 0.78f, MathF.Sin(a) * MathF.Cos(e) * w * 0.58f, 1.2f + r() * 1.5f, 0.8f, 4 + r() * 4, r() * 3, a, 0, solid: false); }
                ORG.anim.Add(new OrgAnim { kind = "throb", G = G, ph = o.Ph });
            }
        }
        // ---- intestines: coiled tubes lying half in the floor, banded
        var gutMat = Wet(0xf2a0b0); var gutBand = Matte(0xc86a80);
        foreach (var g in LAYOUT.Guts)
        {
            var pts = g.Pts.Select(p => new[] { p.x, Gy(p.x, p.z) - g.R * 0.6f, p.z }).ToList();
            Tube(pts, g.R, gutMat, pts.Count * 8);   // solid: the raised floor under them doesn't reach their full height
            var tg = Geo.Torus(g.R * 1.01f, 0.25f, 6, 18);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var pos = new Vector3(pts[i][0], pts[i][1] + g.R * 0.15f, pts[i][2]); var tgt = new Vector3(pts[i + 1][0], pts[i + 1][1] + g.R * 0.15f, pts[i + 1][2]);
                var t = new Mesh(tg, gutBand) { Position = pos }; t.Rotation = LookRot(pos, tgt); Add(t);
            }
        }
        // ---- veins along the floor, branching; vessels hanging from the roof
        var artMat = Wet(0xc0182c, emissive: 0x500008); var venMat = Wet(0x3a3aa0, emissive: 0x100830);
        ORG.veinMats.Add((artMat, artMat.Emissive)); ORG.veinMats.Add((venMat, venMat.Emissive));
        void Vein(float x, float z, float a, int len, float rad, Material mat, int depth)
        {
            var pts = new List<float[]>(); for (int k = 0; k <= len; k++) { pts.Add(new[] { x, Gy(x, z) - rad * 0.78f, z }); /* sunk nearly flush: ground markings you walk over */ a += (r() - 0.5f) * 0.7f; x += MathF.Cos(a) * 4; z += MathF.Sin(a) * 4; }
            if (pts.Count > 2) Tube(pts, rad, mat, pts.Count * 4);   // solid where they rise out of the floor: low enough to step over, but nothing passes through
            if (depth < 2) for (int b = 0; b < 2; b++) { int k = 2 + (int)MathF.Floor(r() * (len - 2)); if (k < pts.Count) Vein(pts[k][0], pts[k][2], a + (r() < 0.5f ? 1 : -1) * (0.6f + r() * 0.6f), Math.Max(3, len >> 1), rad * 0.6f, mat, depth + 1); }
        }
        for (int i = 0; i < (arena ? 8 : 26); i++) Vein((r() * 2 - 1) * LIM * 0.9f, (r() * 2 - 1) * LIM * 0.9f, r() * 6.28f, 8 + (int)MathF.Floor(r() * 10), 0.45f + r() * 0.4f, i % 2 != 0 ? artMat : venMat, 0);
        for (int i = 0; i < (arena ? 6 : 18); i++)
        {
            float x = (r() * 2 - 1) * LIM * 0.85f, z = (r() * 2 - 1) * LIM * 0.85f; if (PADS.Any(p => Hypot(x - p.X, z - p.Z) < p.R + 6)) continue;
            float top = RoofY(x, z), y0 = Gy(x, z), rad = 0.7f + r() * 0.9f, sw = 4 + r() * 6, a = r() * 6.28f;
            Tube(new[] { new[] { x, y0 - 1, z }, new[] { x + MathF.Cos(a) * sw * 0.3f, y0 + (top - y0) * 0.3f, z + MathF.Sin(a) * sw * 0.3f }, new[] { x + MathF.Cos(a) * sw, y0 + (top - y0) * 0.7f, z + MathF.Sin(a) * sw }, new[] { x + MathF.Cos(a) * sw * 1.4f, top + 2, z + MathF.Sin(a) * sw * 1.4f } }, rad, i % 2 != 0 ? artMat : venMat, 40);
        }
        // mucus strands hanging from the roof
        // (the mucus strands that hung from the roof are gone: they passed through everything)
        // ---- giant cells in the colonies: a membrane, a nucleus inside, organelles
        var memb = Toon(Phong(0xf0c8ff, 120, 0xffffff), 4); memb.Transparent = true; memb.Opacity = 0.55f; memb.DepthWrite = false;
        var nuc = Matte(0x5a2a8a); var mito = Matte(0xff9040); var s24 = Geo.Sphere(1, 24, 16); var s14 = Geo.Sphere(1, 14, 10); var s8 = Geo.Sphere(1, 8, 6);
        foreach (var c in BIGCELLS)
        {
            var G = new Node { Position = new Vector3(c.x, c.y + c.h * 0.4f, c.z) }; scene.Add(G);
            G.Add(new Mesh(s24, memb) { Scale = new Vector3(c.r, c.h, c.r), RenderOrder = 1 });
            G.Add(new Mesh(s14, nuc) { Scale = new Vector3(c.r * 0.4f), Position = new Vector3(c.r * 0.15f, c.h * 0.1f, 0) });
            for (int k = 0; k < 4; k++) { var mm = new Mesh(s8, mito) { Scale = new Vector3(c.r * 0.08f, c.r * 0.08f, c.r * 0.22f), Position = new Vector3((r() - 0.5f) * c.r * 1.1f, (r() - 0.3f) * c.h * 0.8f, (r() - 0.5f) * c.r * 1.1f) }; mm.SetEuler(r() * 3, r() * 3, 0); G.Add(mm); }
            ORG.anim.Add(new OrgAnim { kind = "cell", G = G, ph = c.ph });
        }
        // ---- cysts: glossy domes swelling out of the floor, with a few glowing pores
        {
            var cystMat = Wet(0xd86a8a, 90, 0x8a4a5a); var poreMat = BasicM(0xffd0a0); var s20 = Geo.Sphere(1, 20, 12);
            foreach (var c in CYSTS)
            {
                var G = new Node { Position = new Vector3(c.x, c.y, c.z) }; scene.Add(G);
                G.Add(new Mesh(s20, cystMat) { Scale = new Vector3(c.r, c.h, c.r), CastShadow = true, ReceiveShadow = true });
                for (int k = 0; k < 4; k++) { float a = c.ph + k * 1.7f, e = 0.35f + (k % 2) * 0.3f; G.Add(new Mesh(s8, poreMat) { Scale = new Vector3(c.r * 0.07f), Position = new Vector3(MathF.Cos(a) * MathF.Cos(e) * c.r, MathF.Sin(e) * c.h, MathF.Sin(a) * MathF.Cos(e) * c.r) }); }
            }
        }
        // ---- acid in the stomach pits: a glowing surface that heaves, bubbles, and burns
        var acidMat = new Material { Kind = MatKind.Phong, Color = C(0x9aff3a), Emissive = C(0x3a7a00), Transparent = true, Opacity = 0.75f, Shininess = 120, DepthWrite = false };
        foreach (var d in CRATERS)
            if (d.Acid)
            {
                var P = MakePool(d, acidMat);   // a simulated surface that sits inside the pit (see World.Fluids)
                if (P != null) ORG.acid.Add(new AcidPool { d = d, level = P.level, m = P.mesh });
            }
        // blood in the artery trench
        foreach (var ch in CHANNELS)
            if (ch.Blood)
            {
                int n = ch.Pts.Count; var pos = new float[n * 2 * 3]; var idx = new List<uint>();
                for (int i = 0; i < n; i++)
                {
                    var (x, z) = ch.Pts[i]; var (nx2, nz2) = ch.Pts[Math.Min(n - 1, i + 1)]; var (px2, pz2) = ch.Pts[Math.Max(0, i - 1)];
                    float dx = nx2 - px2, dz = nz2 - pz2, l = Hypot(dx, dz); if (l == 0) l = 1; float ox = -dz / l * (ch.W - 1), oz = dx / l * (ch.W - 1), y = ch.Floor(0, 0, ch.L) + 1.1f;
                    pos[i * 6] = x + ox; pos[i * 6 + 1] = y; pos[i * 6 + 2] = z + oz; pos[i * 6 + 3] = x - ox; pos[i * 6 + 4] = y; pos[i * 6 + 5] = z - oz;
                    if (i < n - 1) { uint a = (uint)(i * 2); idx.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 }); }
                }
                var g = new Geometry { Position = pos, Index = idx.ToArray() }; g.ComputeNormals();
                ORG.blood = new Mesh(g, new Material { Kind = MatKind.Phong, Color = C(0x8a0010), Emissive = C(0x300004), Shininess = 140, Side = Side.Double, Transparent = true, Opacity = 0.9f }); scene.Add(ORG.blood);
            }
        // ---- villi: soft fingers carpeting the floor in patches, swaying in the current
        {
            var vm = Matte(0xff8aa0);
            var list = new List<float[]>();
            for (int p = 0; p < (arena ? 14 : 60); p++)
            {
                float cx = (r() * 2 - 1) * LIM * 0.9f, cz = (r() * 2 - 1) * LIM * 0.9f; if (PADS.Any(q => Hypot(cx - q.X, cz - q.Z) < q.R + 4)) continue;
                for (int k = 0; k < 60; k++) { float a = r() * 6.28f, d = MathF.Sqrt(r()) * 9, x = cx + MathF.Cos(a) * d, z = cz + MathF.Sin(a) * d; if (InsideStatic(x, z, Gy(x, z), 0.3f)) continue; list.Add(new[] { x, Gy(x, z) - 0.1f, z, 0.8f + r() * 1.4f, r() * 6 }); }
            }
            BuildVilli(list, vm); ORG.villi = VF.lower; ORG.villiN = list.Count;   // simulated: see World.Villi
        }
        // glowing nodules dotting the floor
        {
            int n = arena ? 60 : 260; var nm = new InstancedMesh(s8, BasicM(0xffffff), n); uint[] cols = { 0xfff070, 0x7affef, 0xff7ae6 };
            for (int i = 0; i < n; i++) { float x = (r() * 2 - 1) * LIM, z = (r() * 2 - 1) * LIM, s = 0.2f + r() * 0.45f; { var ts = TerrainSample(x, z); if (Hypot(ts.gx, ts.gz) > 0.25f) s = 0; /* none on steep ground: they'd stick out of the slope */ } nm.Matrices[i] = Mtx(x, Gy(x, z) - s * 0.3f, z, s, s * 0.45f, s); nm.SetColor(i, C(cols[i % 3])); }   // sunk: flush lights in the floor
            nm.Touch(); scene.Add(nm); ORG.nodules = nm;
        }
        // ---- the drifting things: red cells in streams, white cells patrolling, platelets, bacteria, viruses, phages, antibodies
        List<Floater> Fl(Geometry geo, Material mat, int n, Func<int, Floater> mk)
        {
            var im = new InstancedMesh(geo, mat, n) { CastShadow = false }; scene.Add(im);
            var L = new List<Floater>(); for (int i = 0; i < n; i++) L.Add(mk(i)); ORG.floaters.Add(new FloaterSet { im = im, L = L }); return L;
        }
        // (the browser build filled the air with drifting red cells, white cells, platelets, viruses, phages and antibodies;
        //  floating decor is gone now: only things on the floor or attached to the walls remain)
        // (the crawling bacteria colonies are gone too: decor that walks through you reads as a bug. The germs that matter fight back.)
        ORG.LIM = LIM;
    }
    /// <summary>Rotation that turns an object's +Z toward the target (three.js Object3D.lookAt for non-cameras).</summary>
    static Quaternion LookRot(Vector3 from, Vector3 to)
    {
        var dir = to - from; if (dir.LengthSquared() < 1e-10f) return Quaternion.Identity; dir = Vector3.Normalize(dir);
        var up = MathF.Abs(dir.Y) > 0.999f ? Vector3.UnitZ : Vector3.UnitY;
        var x = Vector3.Normalize(Vector3.Cross(up, dir)); var y = Vector3.Cross(dir, x);
        var m = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, dir.X, dir.Y, dir.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
    // decoration density from the settings: off, some (half the drifters and villi) or full
    void ApplyDecor()
    {
        if (!ORG.built) return;
        int d = SET.decor;
        foreach (var F in ORG.floaters) { F.im.Visible = d > 0; F.im.Count = d >= 2 ? F.L.Count : (F.L.Count + 1) / 2; }
        if (VF != null) { VF.lower.Visible = VF.upper.Visible = d > 0; VF.lower.Count = VF.upper.Count = d >= 2 ? ORG.villiN : (ORG.villiN + 1) / 2; }
        if (ORG.nodules != null) ORG.nodules.Visible = d > 0;
    }
    // ---- per frame: beats, breaths, currents
    float heartBeatT;
    readonly List<float[]> PU = new();
    void UpdateOrganic(float t, float fdt, Vector3 camPos)
    {
        if (!ORG.built) return;
        if (ORG.roof != null) ORG.roof.Mat.U0 = new Vector4(t, 0, 0, 0);
        static float Beat(float u) { float f = u % 1; return MathF.Exp(-((f - 0.05f) * (f - 0.05f)) / 0.002f) + 0.6f * MathF.Exp(-((f - 0.25f) * (f - 0.25f)) / 0.002f); }
        if (ORG.heart is var (HG, ho))
        {
            float b = Beat(t * 1.1f); HG.Scale = new Vector3(1 + 0.06f * b, 1 + 0.08f * b, 1 + 0.06f * b);
            // a heartbeat you can hear, louder the closer you are
            float ph = (t * 1.1f) % 1; if (ph < heartBeatT) Sfx("thud", ho.X, TerrainH(ho.X, ho.Z) + 6, ho.Z, 1);
            heartBeatT = ph;
        }
        foreach (var a in ORG.anim)
        {
            if (a.kind == "breathe") { float k = 1 + 0.07f * MathF.Sin(t * 1.3f + a.ph); a.G.Scale = new Vector3(k, 1 + 0.1f * MathF.Sin(t * 1.3f + a.ph), k); }
            else if (a.kind == "churn") a.G.Scale = new Vector3(1 + 0.04f * MathF.Sin(t * 0.9f + a.ph), 1 + 0.03f * MathF.Sin(t * 1.4f + a.ph), 1 + 0.05f * MathF.Sin(t * 0.7f + a.ph + 1));
            else if (a.kind == "throb") { float k = 1 + 0.02f * MathF.Sin(t * 2.5f + a.ph); a.G.Scale = new Vector3(k); }
            else if (a.kind == "cell") { a.G.Scale = new Vector3(1 + 0.05f * MathF.Sin(t * 1.7f + a.ph), 1 + 0.06f * MathF.Sin(t * 1.3f + a.ph), 1 + 0.05f * MathF.Cos(t * 1.5f + a.ph)); a.G.SetEuler(0, t * 0.1f + a.ph, 0); }
        }
        float pulse = 0.5f + 0.5f * Beat(t * 1.1f);
        foreach (var (m, e) in ORG.veinMats) m.Emissive = e * (0.4f + 0.9f * pulse);
        if (fdt > 0) { UpdatePoolMeshes(t); UpdateFluidMeshes(t); }
        // the drifters (only those near the camera are moved; the rest keep their last place)
        float cx = camPos.X, cz = camPos.Z, LIM = ORG.LIM;
        // who's pushing through the decor: the camera (so nothing ever sits in your face), you, and the fighters nearest you
        PU.Clear(); PU.Add(new[] { camPos.X, camPos.Y, camPos.Z, 2.2f, 0 });
        var me = ME?.P;
        if (me != null)
        {
            var near = new List<(float, Actor)>();
            foreach (var A in actors) { var G = A.P; if (!G.alive || A.dormant || A.hidden) continue; float d = MathF.Abs(G.x - me.x) + MathF.Abs(G.z - me.z); if (d < 40) near.Add((d, A)); }
            near.Sort((p, q) => p.Item1.CompareTo(q.Item1));
            for (int k = 0; k < Math.Min(6, near.Count); k++) { var G = near[k].Item2.P; float rr = (G.vehicle != null ? 2.4f : 1) * MathF.Max(0.8f, G.R * 1.6f); PU.Add(new[] { G.x, G.y + G.H * 0.5f, G.z, rr, Hypot(G.vx, G.vz) }); }
        }
        StepVilli(t, fdt, camPos);
        float fd = MathF.Min(fdt, 1 / 20f); var E = new Q4();
        foreach (var F in ORG.floaters)
        {
            var im = F.im; var L = F.L; if (!im.Visible) continue;
            for (int i = 0, n = Math.Min(L.Count, im.Count); i < n; i++)
            {
                var o = L[i]; float x, y, z, sx = o.s, sy = sx, sz = sx, ex = 0, ey = 0, ez = 0;
                if (o.kind == "rbc")
                {
                    var Ss = o.S; float u = ((o.u + t * Ss.sp) % (LIM * 2.4f)) - LIM * 1.2f, ca = MathF.Cos(Ss.a), sa = MathF.Sin(Ss.a);
                    x = ca * u - sa * (Ss.off + o.w + MathF.Sin(t * 0.5f + o.sp) * 3); z = sa * u + ca * (Ss.off + o.w + MathF.Sin(t * 0.4f + o.sp) * 3);
                    y = MathF.Max(TerrainH(x, z) + 1.5f, Ss.y + o.h + MathF.Sin(t * 0.7f + o.sp) * 2);
                    ex = t * o.spin + o.ax; ey = t * o.spin * 0.6f; ez = o.ax; if (o.flat != 0) sy *= o.flat;
                }
                else if (o.kind == "wbc")
                {
                    if (R() < fdt * 0.2f) o.a += (R() - 0.5f) * 2;
                    o.x += MathF.Cos(o.a) * fdt * 1.6f; o.z += MathF.Sin(o.a) * fdt * 1.6f; if (MathF.Abs(o.x) > LIM * 0.85f || MathF.Abs(o.z) > LIM * 0.85f) o.a += PI;
                    x = o.x; z = o.z; y = TerrainH(x, z) + o.y + MathF.Sin(t + o.ph) * 0.6f;
                    sx = o.s * (1 + 0.12f * MathF.Sin(t * 2 + o.ph)); sy = o.s * (1 + 0.12f * MathF.Cos(t * 1.7f + o.ph)); sz = o.s * (1 + 0.1f * MathF.Sin(t * 2.3f + o.ph + 1));
                    ey = t * 0.2f + o.ph;
                }
                else if (o.kind == "nuc") { var w = o.of; x = w.x; z = w.z; y = TerrainH(x, z) + w.y + MathF.Sin(t + w.ph) * 0.6f; sx = sy = sz = w.s * 0.8f; ex = t * 0.3f; ey = t * 0.2f; }
                else if (o.kind == "bac")
                {
                    o.a += MathF.Sin(t * 0.7f + o.ph) * fdt * 0.8f; float d = o.d + MathF.Sin(t * o.sp * 0.3f + o.ph) * 3;
                    x = o.cx + MathF.Cos(o.a + t * o.sp * 0.1f) * d; z = o.cz + MathF.Sin(o.a + t * o.sp * 0.1f) * d; y = TerrainH(x, z) + 0.4f * o.s;
                    ey = -(o.a + t * o.sp * 0.1f) + MathF.Sin(t * 6 + o.ph) * 0.25f;
                }
                else if (o.kind == "vir")
                {
                    o.x += o.vx * fdt; o.z += o.vz * fdt; if (MathF.Abs(o.x) > LIM) o.vx = -o.vx; if (MathF.Abs(o.z) > LIM) o.vz = -o.vz;
                    x = o.x; z = o.z; y = MathF.Max(TerrainH(x, z) + 2, o.y + MathF.Sin(t * 0.5f + o.ph) * 3); ex = t * 0.3f + o.rx; ey = t * 0.2f + o.ry;
                }
                else
                {   // phage: land, sit, hop off
                    float c = (t * 0.15f + o.ph) % 1, up = c < 0.6f ? 0 : MathF.Sin((c - 0.6f) / 0.4f * PI) * 6;
                    x = o.cx + MathF.Cos(o.a) * o.d; z = o.cz + MathF.Sin(o.a) * o.d; y = TerrainH(x, z) + 0.3f + up; ey = o.a; ez = MathF.Sin(t * 2 + o.ph) * 0.1f * (up > 0 ? 1 : 0);
                }
                if (MathF.Abs(x - cx) > 260 || MathF.Abs(z - cz) > 260) y = -500;
                else if (o.kind == "nuc") { x += o.of.fx; y += o.of.fy; z += o.of.fz; }
                else if (MathF.Abs(x - cx) < 70 && MathF.Abs(z - cz) < 70)
                {
                    // gentle contact: drifting cells get nudged by whatever passes and spring back; things on the floor scuttle out of the way
                    bool ground = o.kind == "bac" || o.kind == "phage"; float rad = (o.kind == "wbc" ? 1.0f : 1.3f) * sx;
                    foreach (var p in PU)
                    {
                        float dx = x + o.fx - p[0], dy = ground ? 0 : y + o.fy - p[1], dz = z + o.fz - p[2];
                        float Rr = ground ? rad + p[3] + 2.6f : rad + p[3], dd = dx * dx + dy * dy + dz * dz; if (dd > Rr * Rr) continue;
                        float dl = MathF.Sqrt(dd); if (dl == 0) dl = 0.01f; float pen = (Rr - dl) / Rr, kk = (ground ? 26 : 14) * pen + p[4] * pen * 1.5f;
                        o.gx += dx / dl * kk * fd; o.gy += dy / dl * kk * fd * 0.7f; o.gz += dz / dl * kk * fd;
                    }
                    float ks = ground ? 1.2f : 2.2f, damp = MathF.Exp(-2.6f * fd);
                    o.gx = (o.gx - o.fx * ks * fd) * damp; o.gy = (o.gy - o.fy * ks * fd) * damp; o.gz = (o.gz - o.fz * ks * fd) * damp;
                    o.fx += o.gx * fd; o.fy += o.gy * fd; o.fz += o.gz * fd;
                    float mm = Hypot(o.fx, o.fz), cap = ground ? 9 : 6; if (mm > cap) { o.fx *= cap / mm; o.fz *= cap / mm; }
                    x += o.fx; z += o.fz;
                    if (ground) { y = TerrainH(x, z) + (o.kind == "bac" ? 0.4f * o.s : y - TerrainH(x - o.fx, z - o.fz)); if (o.kind == "bac" && (MathF.Abs(o.gx) + MathF.Abs(o.gz) > 0.4f)) ey = MathF.Atan2(-o.gx, -o.gz); }
                    else y = MathF.Max(TerrainH(x, z) + 0.6f * sx, y + o.fy);
                }
                QEuler(ex, ey, ez, E);
                im.Matrices[i] = Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateFromQuaternion(new Quaternion(E.x, E.y, E.z, E.w)) * Matrix4x4.CreateTranslation(x, y, z);
            }
            im.Touch();
        }
    }
    // ---- per step: the acid burns, and bubbles
    float acidTick;
    void StepOrganic(float dt)
    {
        StepFluids(dt);
        if (!ORG.built) return;
        acidTick -= dt; bool tick = acidTick <= 0; if (tick) acidTick = 0.4f;
        foreach (var A in ORG.acid)
        {
            if (R() < dt * 6) { float a = R() * 6.28f, d = R() * A.d.R; Puff(A.d.X + MathF.Cos(a) * d, A.level + 0.2f, A.d.Z + MathF.Sin(a) * d, 0.3f, 1.2f, 0.18f); }
            if (!tick) continue;
            foreach (var B in actors.ToArray())
            {
                var G = B.P; if (!G.alive || B.dormant || B.hidden) continue;
                if (PoolAt(G.x, G.z, G.y, 0.25f) != null && Hypot(G.x - A.d.X, G.z - A.d.Z) < A.d.R + 12) { DamageActor(B, B.team == "hero" ? 7 : 14, new Dmg { kind = "blast" }); if (B.human) ShowHint("ACID! GET OUT"); }
            }
        }
    }
}

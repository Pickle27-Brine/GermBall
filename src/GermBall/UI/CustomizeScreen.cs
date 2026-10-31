using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Core;
using GermBall.Game;
using GermBall.Platform;
using GermBall.Render;

namespace GermBall.UI;

/// <summary>
/// Build your germ: species presets, body presets and a body creator, colours, head and eyes, a face builder
/// (shapes you place, drag, size and turn), accessories and armor, with a live 3D preview. Looks only.
/// Saved to Documents\My Games\GermBall\custom.json and handed to the game at boot; germs can also be
/// exported to and imported from .germ.json files anywhere on disk.
/// </summary>
public sealed class CustomizeScreen
{
    readonly App app; readonly Gui gui = new();
    public GermCustom C; public bool Done;
    Species SP;
    // preview
    readonly Scene scene = new(); readonly Camera cam = new() { Fov = 32, Near = 0.05f, Far = 50 };
    readonly Dictionary<string, InstancedMesh> MESH = new(); readonly Dictionary<char, Geometry> GEO;
    Mesh faceMesh; readonly Canvas2D faceCv = new(256, 256); Texture faceTex; Texture faceEditTex; readonly Canvas2D faceEdit = new(256, 256);
    readonly bool[] has = Enumerable.Repeat(true, 15).ToArray(); readonly float[] st = GermDraw.NewSpringState(); readonly DrawOpts opt;
    float yaw = 0.5f, pitch = 0.12f, dist = 5.2f, ty = 1.05f, T; bool spin = true; (float dist, float ty)? want;
    bool dragView; float lastMx, lastMy;
    string tab = "species"; int sel = -1; float scroll; string colorOpen; string status = "";
    int dragFace = -1; float dragDx, dragDy;

    static readonly (string key, string label)[] TABS = { ("species", "Species"), ("body", "Body"), ("colors", "Colours"), ("head", "Head & eyes"), ("face", "Face"), ("extras", "Accessories"), ("armor", "Armor") };
    static readonly Dictionary<string, string> BLURB = new() { ["coccus"] = "Round, bristly, bouncy", ["bacillus"] = "Rod-headed, whip tails", ["amoeba"] = "Lumpy shape-shifter", ["spirillum"] = "Corkscrew hunter", ["tardigrade"] = "Chunky water bear", ["diatom"] = "Glass disc and visor", ["copepod"] = "Shielded, one red eye", ["medusa"] = "Jelly bell and tendrils", ["phage"] = "Crystal capsid head", ["virion"] = "Spiked crown ball", ["rotifer"] = "Spinning wheel head", ["volvox"] = "Speckled green sphere", ["radiolarian"] = "Glass star skeleton", ["paramecium"] = "Ciliated slipper", ["euglena"] = "Eyespot and flagellum" };
    static readonly Dictionary<string, string> HEAD_NAME = new() { ["sphere"] = "Sphere", ["rod"] = "Rod", ["capsid"] = "Capsid", ["disc"] = "Disc", ["bell"] = "Bell", ["cone"] = "Cone", ["snout"] = "Snout", ["shield"] = "Shield", ["wheel"] = "Wheel", ["star"] = "Star", ["blob"] = "Blob", ["slipper"] = "Slipper", ["drop"] = "Droplet", ["cube"] = "Cube" };
    static readonly (string name, Action<BodyShape> set)[] BODY_PRESETS =
    {
        ("Standard", b => { }), ("Slim", b => { b.Width = 0.8f; b.Limbs = 1.05f; b.Hands = 0.85f; }), ("Stocky", b => { b.Width = 1.3f; b.Torso = 0.9f; b.Limbs = 0.85f; b.Hands = 1.2f; b.Feet = 1.2f; }),
        ("Lanky", b => { b.Torso = 1.2f; b.Limbs = 1.35f; b.Width = 0.85f; b.Neck = 1.4f; }), ("Chibi", b => { b.Head = 1.45f; b.Torso = 0.8f; b.Limbs = 0.75f; b.Width = 1.05f; b.Feet = 1.2f; }),
        ("Hulk", b => { b.Width = 1.45f; b.Hands = 1.6f; b.Limbs = 1.15f; b.Torso = 1.1f; b.Head = 0.8f; b.Hump = 0.5f; }), ("Noodle", b => { b.Width = 0.7f; b.Limbs = 1.4f; b.Bend = 2; b.Segments = 1.3f; b.Neck = 2; }),
        ("Pear", b => { b.Belly = 1.3f; b.Width = 1.1f; b.Hands = 0.9f; }), ("Hunchback", b => { b.Hump = 1; b.Neck = 0.6f; b.Torso = 1.05f; }), ("Stubby", b => { b.Limbs = 0.65f; b.Feet = 1.4f; b.Torso = 0.85f; b.Width = 1.2f; }),
        ("Bobblehead", b => { b.Head = 1.6f; b.Neck = 2.2f; b.Width = 0.8f; }), ("Wiggly", b => { b.Bend = 2; b.Segments = 1.4f; b.Limbs = 1.1f; }), ("Big mitts", b => { b.Hands = 1.8f; b.Feet = 1.6f; }),
        ("Tiny", b => { b.Head = 0.8f; b.Torso = 0.8f; b.Limbs = 0.8f; b.Width = 0.8f; }),
    };
    static readonly (string key, string label, float min, float max)[] SLIDERS = { ("head", "Head size", 0.6f, 1.7f), ("torso", "Torso length", 0.7f, 1.4f), ("width", "Width", 0.6f, 1.5f), ("belly", "Belly", 0, 1.5f), ("hump", "Hump", 0, 1.2f), ("neck", "Neck", 0, 2.5f), ("limbs", "Limb length", 0.6f, 1.5f), ("hands", "Hands", 0.5f, 1.8f), ("feet", "Feet", 0.5f, 1.8f), ("bend", "Bendiness", 0, 2.2f), ("segments", "Segment density", 0.6f, 1.6f) };
    static ref float BodyRef(BodyShape b, string k)
    {
        switch (k) { case "head": return ref b.Head; case "torso": return ref b.Torso; case "width": return ref b.Width; case "belly": return ref b.Belly; case "hump": return ref b.Hump; case "neck": return ref b.Neck; case "limbs": return ref b.Limbs; case "hands": return ref b.Hands; case "feet": return ref b.Feet; case "bend": return ref b.Bend; default: return ref b.Segments; }
    }
    static readonly (string key, string label)[] COLORS = { ("a", "Membrane"), ("b", "Second colour"), ("c", "Glow"), ("e", "Eyes") };
    static readonly (string name, uint[] p)[] PALETTES =
    {
        ("Species", null), ("Bubblegum", new uint[] { 0xff9ad0, 0xc04a90, 0xfff06a, 0xffffff }), ("Toxic", new uint[] { 0x9aff4a, 0x2a6a1a, 0xff3af0, 0x111111 }), ("Ice", new uint[] { 0xd8f6ff, 0x3a8ad0, 0x7affef, 0x0a2a4a }),
        ("Ember", new uint[] { 0xff7a3a, 0x8a1a10, 0xffe03a, 0xfff0a0 }), ("Bruise", new uint[] { 0x8a5ac8, 0x2a1a5a, 0x7aff9a, 0xffe0ff }), ("Bile", new uint[] { 0xd8e05a, 0x6a6a1a, 0xff6a2a, 0x2a1a0a }),
        ("Plasma", new uint[] { 0x3a1a6a, 0x150a2a, 0x3affff, 0xff3ad0 }), ("Coral", new uint[] { 0xff8a7a, 0xffd0a0, 0x3ad0c0, 0x1a2a3a }), ("Ghost", new uint[] { 0xf4f0ff, 0xc0b8e0, 0xb0a0ff, 0x2a2040 }),
        ("Blood", new uint[] { 0xc8102a, 0x5a0610, 0xffd0d8, 0xffffff }), ("Mint", new uint[] { 0xa8ffd8, 0x2aa880, 0xffffff, 0x1a3a2a }), ("Midnight", new uint[] { 0x1a2a4a, 0x0a0a1a, 0xffd03a, 0xffd03a }),
    };
    static readonly Dictionary<string, string> ACC_NAME = new() { ["flagellum"] = "Flagellum", ["twinFlagella"] = "Twin flagella", ["headWhip"] = "Head whip", ["cilia"] = "Cilia coat", ["spikes"] = "Spike proteins", ["antennae"] = "Antennae", ["crown"] = "Corona", ["halo"] = "Halo", ["nucleus"] = "Nucleus", ["tentacles"] = "Tentacles", ["horn"] = "Horn", ["fins"] = "Fins", ["pollen"] = "Pollen burrs", ["sporeSacks"] = "Spore sacks", ["vacuolePack"] = "Vacuole pack", ["tail"] = "Tail", ["mohawk"] = "Pili mohawk", ["dots"] = "Chloroplast dots", ["lumps"] = "Lumps", ["bands"] = "Bands", ["crest"] = "Crest", ["collar"] = "Collar", ["stalkEyes"] = "Stalk eyes", ["plates"] = "Plates" };
    static readonly Dictionary<string, string> ARMOR_NAME = new() { ["chitin"] = "Chitin plates", ["capsid"] = "Capsid facets", ["spines"] = "Spines", ["glass"] = "Silica glass", ["slime"] = "Slime coat", ["shell"] = "Clam shell", ["coral"] = "Coral knobs", ["scales"] = "Scales", ["cilia"] = "Cilia fur", ["bubbles"] = "Bubble wrap", ["frustule"] = "Frustule discs", ["barnacles"] = "Barnacles", ["rings"] = "Ring mail", ["plaque"] = "Plaque slabs", ["pearls"] = "Pearl studs", ["pollen"] = "Pollen husk", ["none"] = "None" };
    static FaceShape F(string s, float x, float y, float w, string c, float r = 0, float h = 1) => new() { S = s, X = x, Y = y, W = w, C = c, R = r, H = h };
    static readonly (string name, FaceShape[] f)[] FACES =
    {
        ("Smile", new[] { F("circle", 0.38f, 0.42f, 0.1f, "#111111"), F("circle", 0.62f, 0.42f, 0.1f, "#111111"), F("crescent", 0.5f, 0.55f, 0.28f, "#111111") }),
        ("Grumpy", new[] { F("line", 0.37f, 0.36f, 0.16f, "#111111", 20), F("line", 0.63f, 0.36f, 0.16f, "#111111", -20), F("circle", 0.38f, 0.45f, 0.08f, "#111111"), F("circle", 0.62f, 0.45f, 0.08f, "#111111"), F("zigzag", 0.5f, 0.66f, 0.26f, "#111111") }),
        ("Derp", new[] { F("ring", 0.36f, 0.42f, 0.16f, "#111111"), F("circle", 0.66f, 0.4f, 0.07f, "#111111"), F("oval", 0.52f, 0.66f, 0.12f, "#d02a4a") }),
        ("Cutie", new[] { F("heart", 0.37f, 0.44f, 0.12f, "#ff3a8a"), F("heart", 0.63f, 0.44f, 0.12f, "#ff3a8a"), F("oval", 0.3f, 0.58f, 0.08f, "#ff9ac0", 0, 0.5f), F("oval", 0.7f, 0.58f, 0.08f, "#ff9ac0", 0, 0.5f), F("crescent", 0.5f, 0.57f, 0.12f, "#111111") }),
        ("Menace", new[] { F("triangle", 0.38f, 0.42f, 0.14f, "#ff2a2a", 160), F("triangle", 0.62f, 0.42f, 0.14f, "#ff2a2a", 200), F("zigzag", 0.5f, 0.64f, 0.34f, "#ffffff", 0, 1.5f) }),
        ("Stars", new[] { F("star", 0.37f, 0.43f, 0.16f, "#ffd23a"), F("star", 0.63f, 0.43f, 0.16f, "#ffd23a"), F("oval", 0.5f, 0.64f, 0.1f, "#111111") }),
        ("Cyclops", new[] { F("circle", 0.5f, 0.42f, 0.24f, "#ffffff"), F("circle", 0.5f, 0.42f, 0.12f, "#111111"), F("line", 0.5f, 0.66f, 0.2f, "#111111") }),
        ("Sleepy", new[] { F("crescent", 0.37f, 0.4f, 0.13f, "#111111", 180), F("crescent", 0.63f, 0.4f, 0.13f, "#111111", 180), F("ring", 0.5f, 0.63f, 0.08f, "#111111") }),
    };

    public CustomizeScreen(App app, GermCustom start)
    {
        this.app = app; C = start.Clone();
        scene.Background = Material.Hex(0x1a0618); scene.FogNear = 1000; scene.FogFar = 2000; scene.Shadows = false;
        scene.HemiSky = Material.Hex(0xffe8f4); scene.HemiGround = Material.Hex(0x40142a); scene.HemiIntensity = 0.85f;
        scene.SunDir = Vector3.Normalize(new Vector3(2, 4, 3)); scene.SunIntensity = 0.85f; scene.SunColor = Vector3.One;
        var dish = new Mesh(Geo.Cylinder(1.1f, 1.25f, 0.12f, 40), new Material { Kind = MatKind.Phong, Color = Material.Hex(0x5a1a40), Emissive = Material.Hex(0x2a0818), Shininess = 80, Fog = false }) { Position = new Vector3(0, -0.06f, 0) };
        scene.Add(dish);
        GEO = new() { ['Z'] = Geo.Sphere(1, 18, 12), ['I'] = Geo.Icosahedron(1, 1), ['B'] = Geo.Box(1, 1, 1), ['P'] = Geo.Cone(1, 1, 10), ['C'] = Geo.Cylinder(1, 1, 1, 12), ['T'] = Geo.Torus(1, 0.18f, 8, 24).RotateX(MathF.PI / 2) };
        opt = new DrawOpts { T = 0, Fdt = 1 / 60f, Lod = 2, Has = has, St = st, S = 1 };
        // the face cap, the same shape the game paints over the front of the head
        var g = Geo.Sphere(1, 24, 16, MathF.PI * 0.25f, MathF.PI * 0.5f, MathF.PI * 0.15f, MathF.PI * 0.7f);
        for (int i = 0; i < g.VertexCount; i++) { float x = g.Position[i * 3], y = g.Position[i * 3 + 1]; g.Uv[i * 2] = 0.5f + x * 0.62f; g.Uv[i * 2 + 1] = 0.5f + y * 0.62f; }
        faceTex = new Texture(256, 256) { Nearest = false };
        faceMesh = scene.Add(new Mesh(g, new Material { Kind = MatKind.Lambert, Map = faceTex, Transparent = true, AlphaTest = 0.35f, PolygonOffset = true, Fog = false }) { FrustumCulled = false });
        faceEditTex = new Texture(256, 256) { Nearest = false };
        Changed();
    }

    void Save() { Paths.Save(Paths.Custom, C); }
    void Changed()
    {
        SP = GermDraw.SpeciesFromCustom(C);
        foreach (var (k, m) in MESH) Recolor(m, k[1]);
        faceCv.Clear(); FaceArt.Draw(faceCv, 256, C.Face); faceCv.CopyTo(faceTex.Pixels); faceTex.Version++;
        PaintEdit();
        Save();
    }
    Material MatFor(char tone)
    {
        uint col = tone == 'k' ? 0x0a0510u : tone == 'd' ? C.ArmorCol : SP.Col.Get(tone);
        if (tone == 'e' || tone == 'k') return new Material { Kind = MatKind.Basic, Color = Material.Hex(col), Fog = false };
        if (tone == 'c') return new Material { Kind = MatKind.Phong, Color = Material.Hex(col), Emissive = Material.Hex(col) * 0.55f, Shininess = 90, Fog = false };
        return new Material { Kind = MatKind.Phong, Color = Material.Hex(col), Shininess = tone == 'd' ? 90 : 55, Specular = Material.Hex(tone == 'd' ? 0xaaaaccu : 0x553344u), Fog = false };
    }
    void Recolor(InstancedMesh m, char t)
    {
        uint col = t == 'k' ? 0x0a0510u : t == 'd' ? C.ArmorCol : SP.Col.Get(t);
        m.Mat.Color = Material.Hex(col); if (t == 'c') m.Mat.Emissive = Material.Hex(col) * 0.55f;
    }
    void Emit(char sh, char tone, float x, float y, float z, float qx, float qy, float qz, float qw, float sx, float sy, float sz)
    {
        if (tone == 'f') tone = 'a';
        string k = new(new[] { sh, tone });
        if (!MESH.TryGetValue(k, out var m)) { m = new InstancedMesh(GEO[sh], MatFor(tone), 256); m.Count = 0; MESH[k] = m; scene.Add(m); }
        m.EnsureCapacity(m.Count + 1);
        m.Matrices[m.Count++] = Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateFromQuaternion(new Quaternion(qx, qy, qz, qw)) * Matrix4x4.CreateTranslation(x, y, z);
    }

    float PanelW => Math.Clamp(app.Renderer.Width * 0.36f, 320, 480);

    public void Update(float dt)
    {
        T += dt;
        float Wd = app.Renderer.Width, Ht = app.Renderer.Height, pw = PanelW;
        // orbit with the mouse over the preview, wheel to zoom
        bool overPrev = Input.MouseX < Wd - pw;
        if (Input.Pressed("Mouse0") && overPrev && Input.MouseY < Ht - 60) { dragView = true; lastMx = Input.MouseX; lastMy = Input.MouseY; }
        if (!Input.Down("Mouse0")) dragView = false;
        if (dragView) { yaw -= (Input.MouseX - lastMx) * 0.01f; pitch = Math.Clamp(pitch + (Input.MouseY - lastMy) * 0.006f, -0.4f, 1.1f); lastMx = Input.MouseX; lastMy = Input.MouseY; }
        if (Input.Wheel != 0 && overPrev) { float d = Math.Clamp((want?.dist ?? dist) * (Input.Wheel < 0 ? 1.1f : 0.9f), 1.4f, 9); want = (d, want?.ty ?? ty); }
        var gp = Gamepad.First; if (gp != null && MathF.Abs(gp.Axes[2]) > 0.2f) { yaw -= gp.Axes[2] * dt * 2.5f; spin = false; }
        if (spin && !dragView) yaw += dt * 0.45f;
        if (want is { } wv) { float k = 1 - MathF.Exp(-dt * 6); dist += (wv.dist - dist) * k; ty += (wv.ty - ty) * k; }
        // centre the germ in the preview area left of the panel by sliding the camera sideways
        var pos = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch) * dist, ty + MathF.Sin(pitch) * dist, MathF.Cos(yaw) * MathF.Cos(pitch) * dist);
        var target = new Vector3(0, ty, 0); var fwd = Vector3.Normalize(target - pos); var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        float aspect = Wd / Math.Max(1, Ht), halfW = MathF.Tan(cam.Fov * MathF.PI / 360) * aspect * Vector3.Distance(pos, target), shift = pw / 2 / (Wd / 2) * halfW;
        cam.Position = pos + right * shift; cam.LookAt(target + right * shift);
        foreach (var m in MESH.Values) m.Count = 0;
        var ps = GermDraw.RestPose(1, T); opt.T = T; opt.Fdt = dt;
        GermDraw.Draw(SP, ps, opt, Emit);
        foreach (var p in GermDraw.HERO_ARMOR) GermDraw.ArmorPiece(SP.ArmorStyle, p, ps, Emit, 'd', T);
        foreach (var m in MESH.Values) m.Touch();
        faceMesh.Visible = C.Face.Count > 0;
        if (faceMesh.Visible) { float k2 = SP.Head == "cube" ? 0.85f : 1.06f; faceMesh.Position = new Vector3(ps[0], ps[1], ps[2]); faceMesh.Rotation = new Quaternion(ps[3], ps[4], ps[5], ps[6]); faceMesh.Scale = new Vector3(ps[7] * SP.Hs[0] * k2, ps[8] * SP.Hs[1] * k2, ps[9] * SP.Hs[2] * k2); }
    }
    public void Render(float dt) => app.Renderer.Render(scene, cam);

    void SetCam(float d, float y) => want = (d, y);
    void Close() { Save(); Done = true; }

    // ---------------------------------------------------------------- the panel
    public void DrawUI(Draw2D d, float dt)
    {
        float Wd = app.Renderer.Width, Ht = app.Renderer.Height, pw = PanelW, px = Wd - pw;
        gui.Begin(d, dt);
        if (gui.Back) { if (colorOpen != null) colorOpen = null; else { Close(); return; } }
        // the name and the preview bar
        string nm = (GermDraw.SPECIES.TryGetValue(C.Preset, out var spc) ? spc.Name : "Germ").ToUpperInvariant();
        d.Text(nm, 22, 16, d.DisplayBig, Draw2D.Col(0xfff4fd), "left", 2.5f, true, Draw2D.Col(0xff3cac));
        {
            float bx = px / 2 - 130, by = Ht - 18 - 24;
            if (gui.Chip("spin", bx, by, "Spin", spin)) spin = !spin; bx += gui.lastW + 6;
            if (gui.Chip("facecam", bx, by, "Face")) { SetCam(1.9f, 1.75f); spin = false; yaw = 0; } bx += gui.lastW + 6;
            if (gui.Chip("bodycam", bx, by, "Whole body")) SetCam(5.2f, 1.05f);
        }
        if (status.Length > 0) d.Text(status, 22, 58, d.MonoSmall, Pal.Warn);
        // panel
        d.Rect(px, 0, pw, Ht, Pal.Panel); d.Rect(px, 0, 1, Ht, Pal.HudDim);
        float ix = px + 18, iw = pw - 36, y = 18;
        d.Text("YOUR GERM", ix, y, d.DisplayBig, Pal.White, "left", 2.5f); y += 44;
        // tabs (wrapping)
        float tx = ix; var tf = d.DisplaySmall;
        foreach (var (k, lab) in TABS)
        {
            float tw = tf.Measure(lab.ToUpperInvariant(), 1.3f) + 20; if (tx + tw > ix + iw) { tx = ix; y += 32; }
            bool on = tab == k, hov = gui.Hover(tx, y, tw, 28);
            if (gui.Hit("ct:" + k, tx, y, tw, 28)) { if (tab == "face" && k != "face") SetCam(5.2f, 1.05f); tab = k; sel = -1; scroll = 0; colorOpen = null; if (k == "face") { SetCam(1.9f, 1.75f); spin = false; yaw = 0; } }
            d.Rect(tx, y, tw, 28, on ? Draw2D.Col(0xffb347, 0.1f) : hov ? Pal.Chip : new Vector4(0)); d.Frame(tx, y, tw, 28, on ? Pal.Warn : Pal.HudDim);
            d.Text(lab.ToUpperInvariant(), tx + tw / 2, y + 7, tf, on ? Pal.Warn : Pal.Text, "center", 1.3f);
            if (gui.IsFocused("ct:" + k)) d.Frame(tx - 3, y - 3, tw + 6, 34, Pal.White, 2);
            tx += tw + 4;
        }
        y += 40;
        float footH = 50, top = y, viewH = Ht - top - footH - 8;
        gui.PushClip(px + 1, top, pw - 2, viewH);
        float contentH = tab switch { "species" => TabSpecies(d, ix, top - scroll, iw), "body" => TabBody(d, ix, top - scroll, iw), "colors" => TabColors(d, ix, top - scroll, iw), "head" => TabHead(d, ix, top - scroll, iw), "face" => TabFace(d, ix, top - scroll, iw), "extras" => TabExtras(d, ix, top - scroll, iw), _ => TabArmor(d, ix, top - scroll, iw) };
        gui.PopClip();
        gui.ScrollPanel(ref scroll, top, viewH, contentH);
        // footer
        float fy = Ht - footH + 6, fx = ix;
        if (gui.Chip("random", fx, fy + 8, "Randomize")) Randomize(); fx += gui.lastW + 5;
        if (gui.Chip("reset", fx, fy + 8, "Reset", false, Draw2D.Col(0xff9ae8))) { C = GermDraw.DefaultCustom(); sel = -1; Changed(); } fx += gui.lastW + 5;
        if (gui.Chip("export", fx, fy + 8, "Export…")) Export(); fx += gui.lastW + 5;
        if (gui.Chip("import", fx, fy + 8, "Import…")) Import();
        float dw = d.DisplayMid.Measure("SAVE AND BACK", 3) + 30;
        if (gui.Big("done", ix + iw - dw, fy, dw, 40, "Save and back")) Close();
        if (gui.Focus == null && gui.NavUsed) gui.Focus = "ct:" + tab;
    }

    // ---- export / import: a germ is a small JSON file you can share
    void Export()
    {
        var path = FileDialogs.Save(app.Window.Handle, "Export germ", "Germ (*.germ.json)|*.germ.json", Paths.Germs, (C.Preset ?? "germ") + ".germ.json");
        if (path == null) return;
        Paths.Save(path, C); status = "Saved " + System.IO.Path.GetFileName(path);
    }
    void Import()
    {
        var path = FileDialogs.Open(app.Window.Handle, "Import germ", "Germ (*.germ.json;*.json)|*.germ.json;*.json", Paths.Germs);
        if (path == null) return;
        var g = Paths.Load<GermCustom>(path);
        if (g == null || !GermDraw.SPECIES.ContainsKey(g.Preset ?? "")) { status = "That file isn't a germ"; return; }
        g.Body ??= new BodyShape(); g.Eyes ??= new EyeCustom(); g.Face ??= new(); g.Acc ??= new();
        C = g; sel = -1; Changed(); status = "Loaded " + System.IO.Path.GetFileName(path);
    }

    float Csub(Draw2D d, string s, float x, float y) { d.Text(s.ToUpperInvariant(), x, y + 12, d.DisplaySmall, Pal.Hud, "left", 2); return 32; }
    float Note(Draw2D d, string s, float x, float y, float w) => d.Wrap(s, x, y + 10, w, d.MonoSmall, Pal.Mute, 1.5f) + 14;
    float CRow(Draw2D d, string id, string label, ref float v, float min, float max, float step, float x, float y, float w, Func<float, string> fmt = null, float labW = 130)
    {
        d.Text(label, x, y + 4, d.MonoSmall, Draw2D.Col(0xc8d8e0));
        v = gui.Slider(id, x + labW, y + 1, w - labW - 52, v, min, max, step, out bool ch, Pal.Hot);
        d.Text((fmt ?? (q => q.ToString("0.00")))(v), x + w, y + 4, d.MonoSmall, Pal.Hud, "right");
        if (ch) Changed();
        return 28;
    }
    // colours: a swatch that opens hue / saturation / lightness sliders
    float ColorRow(Draw2D d, string id, string label, uint val, Action<uint> set, float x, float y, float w, float labW = 130)
    {
        d.Text(label, x, y + 6, d.MonoSmall, Draw2D.Col(0xc8d8e0));
        bool open = colorOpen == id;
        if (gui.Hit("col:" + id, x + labW, y, 64, 26)) colorOpen = open ? null : id;
        d.Rect(x + labW, y, 64, 26, Draw2D.Col(val)); d.Frame(x + labW, y, 64, 26, open ? Pal.Warn : Pal.HudDim);
        if (gui.IsFocused("col:" + id)) d.Frame(x + labW - 3, y - 3, 70, 32, Pal.White, 2);
        d.Text("#" + val.ToString("x6"), x + labW + 72, y + 6, d.MonoSmall, Pal.Mute);
        float h = 32;
        if (open)
        {
            var (H, S, L) = ToHsl(val); float H0 = H, S0 = S, L0 = L;
            H = gui.Slider(id + ":h", x + 40, y + h, w - 40, H, 0, 1, 0.005f, out _, Draw2D.Col(M.Hsl(H, 1, 0.5f))); d.Text("Hue", x, y + h + 3, d.MonoSmall, Pal.Mute); h += 26;
            S = gui.Slider(id + ":s", x + 40, y + h, w - 40, S, 0, 1, 0.01f, out _, Pal.Hot); d.Text("Sat", x, y + h + 3, d.MonoSmall, Pal.Mute); h += 26;
            L = gui.Slider(id + ":l", x + 40, y + h, w - 40, L, 0, 1, 0.01f, out _, Pal.White); d.Text("Light", x, y + h + 3, d.MonoSmall, Pal.Mute); h += 30;
            if (H != H0 || S != S0 || L != L0) { set(M.Hsl(H, S, L)); Changed(); }
        }
        return h;
    }
    static (float h, float s, float l) ToHsl(uint c)
    {
        float r = ((c >> 16) & 255) / 255f, g = ((c >> 8) & 255) / 255f, b = (c & 255) / 255f, mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), l = (mx + mn) / 2;
        if (mx == mn) return (0, 0, l);
        float dd = mx - mn, s = l > 0.5f ? dd / (2 - mx - mn) : dd / (mx + mn), h = mx == r ? (g - b) / dd + (g < b ? 6 : 0) : mx == g ? (b - r) / dd + 2 : (r - g) / dd + 4;
        return (h / 6, s, l);
    }

    float TabSpecies(Draw2D d, float x, float y, float w)
    {
        float y0 = y; int cols = Math.Max(1, (int)((w + 6) / 136)); float cw = (w - (cols - 1) * 6) / cols; int i = 0;
        foreach (var (k, s) in GermDraw.SPECIES)
        {
            float cx = x + (i % cols) * (cw + 6), cy = y + (i / cols) * 62; bool on = C.Preset == k, hov = gui.Hover(cx, cy, cw, 56);
            if (gui.Hit("sp:" + k, cx, cy, cw, 56)) { C.Preset = k; C.Col = null; Changed(); }
            d.Rect(cx, cy, cw, 56, on ? Pal.ChipOn : hov ? Pal.A(Pal.BtnHover, 0.5f) : Pal.Chip); d.Frame(cx, cy, cw, 56, on ? Pal.Warn : Pal.HudDim);
            float sw = cw - 16; d.Rect(cx + 8, cy + 7, sw * 0.5f, 6, Draw2D.Col(s.Col.A)); d.Rect(cx + 8 + sw * 0.5f, cy + 7, sw * 0.3f, 6, Draw2D.Col(s.Col.B)); d.Rect(cx + 8 + sw * 0.8f, cy + 7, sw * 0.2f, 6, Draw2D.Col(s.Col.C));
            d.Text(s.Name.ToUpperInvariant(), cx + 8, cy + 17, d.Display, Pal.Hud, "left", 1.4f);
            d.Text(BLURB.GetValueOrDefault(k, ""), cx + 8, cy + 37, d.MonoSmall, Pal.Mute);
            if (gui.IsFocused("sp:" + k)) d.Frame(cx - 3, cy - 3, cw + 6, 62, Pal.White, 2);
            i++;
        }
        y += (i + cols - 1) / cols * 62;
        y += Note(d, "Species sets the starting shape and colours. Everything else builds on top of it.", x, y, w);
        return y - y0;
    }
    float TabBody(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        y += Csub(d, "Body type", x, y);
        if ((r = gui.Chips("bp", x, y, w, BODY_PRESETS.Select(b => b.name).ToList(), i => false, out h)) >= 0) { C.Body = new BodyShape(); BODY_PRESETS[r].set(C.Body); Changed(); }
        y += h; y += Csub(d, "Body creator", x, y);
        foreach (var (k, lab, a, b) in SLIDERS) y += CRow(d, "b:" + k, lab, ref BodyRef(C.Body, k), a, b, 0.05f, x, y, w);
        y += Note(d, "Body shape is purely cosmetic: it never changes speed, strength or health.", x, y, w);
        return y - y0;
    }
    float TabColors(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        y += Csub(d, "Palettes", x, y);
        if ((r = gui.Chips("pal", x, y, w, PALETTES.Select(p => p.name).ToList(), i => false, out h)) >= 0)
        {
            var p = PALETTES[r].p; C.Col = p == null ? null : new Dictionary<string, uint> { ["a"] = p[0], ["b"] = p[1], ["c"] = p[2], ["e"] = p[3] }; Changed();
        }
        y += h; y += Csub(d, "Pick your own", x, y);
        var bse = GermDraw.SpeciesFromCustom(C).Col;
        foreach (var (k, lab) in COLORS)
        {
            string kk = k;
            y += ColorRow(d, "c" + k, lab, bse.Get(k[0]), v =>
            {
                var cur = GermDraw.SpeciesFromCustom(C).Col;
                C.Col ??= new Dictionary<string, uint> { ["a"] = cur.A, ["b"] = cur.B, ["c"] = cur.C, ["e"] = cur.E };
                C.Col[kk] = v; C.Col.Remove("d");
            }, x, y, w);
        }
        y += ColorRow(d, "carmor", "Armor", C.ArmorCol, v => C.ArmorCol = v, x, y, w);
        return y - y0;
    }
    float TabHead(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        y += Csub(d, "Head shape", x, y);
        var heads = new List<string> { "Species default" }; heads.AddRange(GermDraw.HEAD_SHAPES.Select(s => HEAD_NAME.GetValueOrDefault(s, s)));
        if ((r = gui.Chips("head", x, y, w, heads, i => i == 0 ? string.IsNullOrEmpty(C.Head) : C.Head == GermDraw.HEAD_SHAPES[i - 1], out h)) >= 0) { C.Head = r == 0 ? "" : GermDraw.HEAD_SHAPES[r - 1]; Changed(); }
        y += h; y += Csub(d, "Eyes", x, y);
        var eyes = new List<string> { "Species default", "None", "1", "2", "3", "4", "5", "6" };
        if ((r = gui.Chips("eyes", x, y, w, eyes, i => i == 0 ? C.Eyes.N < 0 : C.Eyes.N == i - 1, out h)) >= 0) { C.Eyes.N = r == 0 ? -1 : r - 1; Changed(); }
        y += h + 8;
        y += CRow(d, "esize", "Eye size", ref C.Eyes.Size, 0.4f, 2.2f, 0.05f, x, y, w);
        y += CRow(d, "espread", "Eye spread", ref C.Eyes.Spread, 0.3f, 1.8f, 0.05f, x, y, w);
        return y - y0;
    }
    void PaintEdit()
    {
        var g = faceEdit; g.Clear();
        g.FillStyle = "#" + SP.Col.A.ToString("x6"); g.BeginPath(); g.Arc(128, 128, 124, 0, 7); g.Fill();
        var tmp = new Canvas2D(256, 256); FaceArt.Draw(tmp, 256, C.Face);
        for (int i = 0; i < tmp.Px.Length; i += 4)
        {
            float a = tmp.Px[i + 3]; if (a <= 0) continue;
            for (int k = 0; k < 3; k++) g.Px[i + k] = g.Px[i + k] * (1 - a) + tmp.Px[i + k] * a;
            g.Px[i + 3] = a + g.Px[i + 3] * (1 - a);
        }
        g.CopyTo(faceEditTex.Pixels); faceEditTex.Version++;
    }
    float TabFace(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        bool narrow = w < 420; float fs = narrow ? Math.Min(w, 220) : 200;
        float fx = x, fy = y;
        d.Image(faceEditTex, fx, fy, fs, fs);
        // select and drag shapes on the face
        float mx = (gui.Mx - fx) / fs, my = (gui.My - fy) / fs;
        if (gui.Hover(fx, fy, fs, fs) && Input.Pressed("Mouse0"))
        {
            int hit = -1;
            for (int i = C.Face.Count - 1; i >= 0; i--) { var f = C.Face[i]; float rr = (f.W > 0 ? f.W : 0.15f) * 0.6f * Math.Max(1, f.H) + 0.02f; if (MathF.Sqrt((f.X - mx) * (f.X - mx) + (f.Y - my) * (f.Y - my)) < rr) { hit = i; break; } }
            sel = hit; if (hit >= 0) { dragFace = hit; dragDx = C.Face[hit].X - mx; dragDy = C.Face[hit].Y - my; }
        }
        if (dragFace >= 0 && dragFace < C.Face.Count)
        {
            if (Input.Down("Mouse0")) { C.Face[dragFace].X = Math.Clamp(mx + dragDx, 0.02f, 0.98f); C.Face[dragFace].Y = Math.Clamp(my + dragDy, 0.02f, 0.98f); faceCv.Clear(); FaceArt.Draw(faceCv, 256, C.Face); faceCv.CopyTo(faceTex.Pixels); faceTex.Version++; PaintEdit(); }
            else { dragFace = -1; Changed(); }
        }
        if (sel >= 0 && sel < C.Face.Count)
        {
            var f = C.Face[sel]; float rr = (f.W > 0 ? f.W : 0.15f) * fs * 0.65f * Math.Max(1, f.H);
            float sx = fx + f.X * fs - rr, sy = fy + f.Y * fs - rr;
            for (float t = 0; t < rr * 2; t += 9) { float l = Math.Min(5, rr * 2 - t); d.Rect(sx + t, sy, l, 2, Pal.Hud); d.Rect(sx + t, sy + rr * 2 - 2, l, 2, Pal.Hud); d.Rect(sx, sy + t, 2, l, Pal.Hud); d.Rect(sx + rr * 2 - 2, sy + t, 2, l, Pal.Hud); }
        }
        float sideX = narrow ? x : x + fs + 12, sideW = narrow ? w : w - fs - 12, sy0 = narrow ? y + fs + 8 : y; y = sy0;
        y += Csub(d, "Add a shape", sideX, y);
        if ((r = gui.Chips("addf", sideX, y, sideW, GermDraw.FACE_SHAPES, i => false, out h)) >= 0)
        {
            C.Face.Add(new FaceShape { S = GermDraw.FACE_SHAPES[r], X = 0.5f, Y = 0.5f, W = 0.14f, H = 1, R = 0, C = sel >= 0 && sel < C.Face.Count ? C.Face[sel].C : "#111111" }); sel = C.Face.Count - 1; Changed();
        }
        y += h;
        if (sel >= 0 && sel < C.Face.Count)
        {
            var f = C.Face[sel];
            y += Csub(d, "Selected: " + f.S, sideX, y);
            uint fc = uint.TryParse((f.C ?? "#111111").TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out var u) ? u : 0x111111;
            y += ColorRow(d, "fcol", "Colour", fc, v => f.C = "#" + v.ToString("x6"), sideX, y, sideW, 64);
            float fw = f.W > 0 ? f.W : 0.15f; y += CRow(d, "fsize", "Size", ref fw, 0.03f, 0.6f, 0.01f, sideX, y, sideW, null, 64); f.W = fw;
            float fh = f.H > 0 ? f.H : 1; y += CRow(d, "fstretch", "Stretch", ref fh, 0.3f, 3, 0.05f, sideX, y, sideW, null, 64); f.H = fh;
            y += CRow(d, "fturn", "Turn", ref f.R, -180, 180, 5, sideX, y, sideW, v => MathF.Round(v) + "°", 64);
            string[] acts = { "Duplicate", "Mirror copy", "To front", "Delete" };
            if ((r = gui.Chips("fact", sideX, y, sideW, acts, i => false, out h)) >= 0)
            {
                if (r == 0) { var c = f.Clone(); c.X = Math.Min(0.95f, f.X + 0.08f); C.Face.Add(c); sel = C.Face.Count - 1; }
                else if (r == 1) { var c = f.Clone(); c.X = 1 - f.X; c.R = -f.R; C.Face.Add(c); sel = C.Face.Count - 1; }
                else if (r == 2) { C.Face.RemoveAt(sel); C.Face.Add(f); sel = C.Face.Count - 1; }
                else { C.Face.RemoveAt(sel); sel = -1; }
                Changed();
            }
            y += h;
        }
        else y += Note(d, "Click a shape on the face to select it, drag to move it. These are painted on the front of your head, on top of the eyes picked under Head (set eyes to None for a face-only look).", sideX, y, sideW);
        y += Csub(d, "Ready-made faces", sideX, y);
        var names = FACES.Select(fc => fc.name).Append("Clear").ToList();
        if ((r = gui.Chips("rface", sideX, y, sideW, names, i => false, out h)) >= 0) { C.Face = r < FACES.Length ? FACES[r].f.Select(s => s.Clone()).ToList() : new(); sel = -1; Changed(); }
        y += h;
        return Math.Max(y, narrow ? y : y0 + fs) - y0 + 8;
    }
    float TabExtras(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        y += Csub(d, "Accessories · pick as many as you like", x, y);
        var keys = GermDraw.ACCESSORY.Keys.ToList();
        if ((r = gui.Chips("acc", x, y, w, keys.Select(k => ACC_NAME.GetValueOrDefault(k, k)).ToList(), i => C.Acc.Contains(keys[i]), out h)) >= 0) { var k = keys[r]; if (!C.Acc.Remove(k)) C.Acc.Add(k); Changed(); }
        y += h + 8;
        if (gui.Chip("accnone", x, y, "Remove all", false, Draw2D.Col(0xff9ae8))) { C.Acc.Clear(); Changed(); }
        return y + 30 - y0;
    }
    float TabArmor(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        y += Csub(d, "Armor style", x, y);
        if ((r = gui.Chips("arm", x, y, w, GermDraw.ARMOR_STYLES.Select(k => ARMOR_NAME.GetValueOrDefault(k, k)).ToList(), i => C.Armor == GermDraw.ARMOR_STYLES[i], out h)) >= 0) { C.Armor = GermDraw.ARMOR_STYLES[r]; Changed(); }
        y += h + 8;
        y += ColorRow(d, "armc", "Armor colour", C.ArmorCol, v => C.ArmorCol = v, x, y, w);
        y += Note(d, "Every style protects the same: shots chip plates off whatever they look like.", x, y, w);
        return y - y0;
    }

    // a coherent random germ
    void Randomize()
    {
        var rnd = new Random(); T Pick<T>(IList<T> a) => a[rnd.Next(a.Count)]; float R() => (float)rnd.NextDouble();
        C.Preset = Pick(GermDraw.SPECIES.Keys.ToList()); C.Head = R() < 0.4f ? Pick(GermDraw.HEAD_SHAPES) : "";
        C.Body = new BodyShape(); foreach (var (k, _, a, b) in SLIDERS) if (R() < 0.6f) BodyRef(C.Body, k) = MathF.Round((a + R() * (b - a) * 0.8f + (b - a) * 0.1f) * 100) / 100;
        float hh = R();
        C.Col = R() < 0.25f ? null : new Dictionary<string, uint> { ["a"] = M.Hsl(hh, 0.6f, 0.65f), ["b"] = M.Hsl(hh + 0.05f, 0.55f, 0.32f), ["c"] = M.Hsl(hh + 0.45f, 0.9f, 0.6f), ["e"] = R() < 0.5f ? 0xffffff : M.Hsl(hh + 0.5f, 0.9f, 0.5f) };
        C.Eyes = new EyeCustom { N = R() < 0.6f ? -1 : rnd.Next(5), Size = MathF.Round((0.7f + R() * 0.9f) * 100) / 100, Spread = MathF.Round((0.7f + R() * 0.6f) * 100) / 100 };
        C.Acc = new(); var ak = GermDraw.ACCESSORY.Keys.ToList(); for (int i = rnd.Next(4); i > 0; i--) { var k = Pick(ak); if (!C.Acc.Contains(k)) C.Acc.Add(k); }
        C.Armor = Pick(GermDraw.ARMOR_STYLES); C.ArmorCol = M.Hsl(hh + 0.15f, 0.4f, 0.82f);
        C.Face = R() < 0.5f ? Pick(FACES).f.Select(s => s.Clone()).ToList() : new();
        sel = -1; Changed();
    }
}

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
/// Build your hero. Pick a medicine preset, then change any segment on its own (shape, thickness, length, colours, two-tone),
/// add attachments and move them around on the model with the mouse (or a controller stick), recolour, and pick an armor style.
/// Looks only. Saved to Documents\My Games\GermBall\custom.json; designs can be exported and imported as .germ.json files.
/// </summary>
public sealed class CustomizeScreen
{
    readonly App app; readonly Gui gui = new();
    public GermCustom C; public bool Done;
    MedSpec SP;
    readonly Scene scene = new(); readonly Camera cam = new() { Fov = 32, Near = 0.05f, Far = 50 };
    readonly Dictionary<string, InstancedMesh> MESH = new(); readonly Dictionary<char, Geometry> GEO;
    readonly InstancedMesh selMesh;
    readonly bool[] has = Enumerable.Repeat(true, 15).ToArray(); readonly float[] st = GermDraw.NewSpringState(); readonly DrawOpts opt;
    float yaw = 0.5f, pitch = 0.12f, dist = 5.2f, ty = 1.05f, T; bool spin = true; (float dist, float ty)? want;
    float[] pose = GermDraw.RestPose(1, 0);
    string dragMode = "size";
    string tab = "preset"; int seg = 1, att = -1; bool mirror = true; float scroll; string colorOpen; string status = "";
    bool dragView, dragAtt, dragSeg, dragAnchor; float lastMx, lastMy;
    /// <summary>The pose with limb anchors applied: what is actually drawn, so picking and dragging use it.</summary>
    float[] apose = GermDraw.RestPose(1, 0);

    static readonly (string key, string label)[] TABS = { ("preset", "Preset"), ("build", "Build"), ("segments", "Segments"), ("attach", "Attachments"), ("colors", "Colours"), ("armor", "Armor") };
    static readonly (string name, uint[] p)[] PALETTES =
    {
        ("Preset", null), ("Classic", new uint[] { 0xe8303c, 0xf4f1ec, 0x7affef, 0x7affef }), ("Antibiotic", new uint[] { 0x2a8af0, 0xffd23a, 0xffffff, 0xffffff }), ("Cherry", new uint[] { 0xb0102a, 0xff8aa0, 0xffe08a, 0xffe08a }),
        ("Mint", new uint[] { 0xa8ffd8, 0x2aa880, 0xffffff, 0x1a3a2a }), ("Midnight", new uint[] { 0x1a2a4a, 0x0a0a1a, 0xffd03a, 0xffd03a }), ("Vitamin C", new uint[] { 0xff8a1a, 0xfff0c0, 0xff3a6a, 0xffffff }),
        ("Charcoal", new uint[] { 0x2a2a2e, 0x6a6a72, 0x7affef, 0x7affef }), ("Lavender", new uint[] { 0xb8a0ff, 0xf0e8ff, 0xff7ae6, 0xff7ae6 }), ("Iron", new uint[] { 0x8a2a1a, 0xd0c0b0, 0xffb347, 0xffb347 }),
    };
    static readonly (char tone, string label)[] TONES = { ('a', "Body"), ('b', "Second"), ('c', "Glow"), ('d', "Armor"), ('e', "Visor"), ('k', "Dark") };

    public CustomizeScreen(App app, GermCustom start)
    {
        this.app = app; C = start.Clone();
        if (!MedDraw.PRESETS.ContainsKey(C.Preset ?? "") || !MedDraw.PLAYER_PRESETS.Contains(C.Preset)) { C.Preset = "gelcap"; C.Seg = new(); C.Att = null; }
        C.Seg ??= new();
        scene.Background = Material.Hex(0x1a0618); scene.FogNear = 1000; scene.FogFar = 2000; scene.Shadows = false;
        scene.HemiSky = Material.Hex(0xffe8f4); scene.HemiGround = Material.Hex(0x40142a); scene.HemiIntensity = 0.85f;
        scene.SunDir = Vector3.Normalize(new Vector3(2, 4, 3)); scene.SunIntensity = 0.85f; scene.SunColor = Vector3.One;
        scene.Add(new Mesh(Geo.Cylinder(1.1f, 1.25f, 0.12f, 40), new Material { Kind = MatKind.Phong, Color = Material.Hex(0x5a1a40), Emissive = Material.Hex(0x2a0818), Shininess = 80, Fog = false }) { Position = new Vector3(0, -0.06f, 0) });
        GEO = Geo.PartShapes(true);
        selMesh = scene.Add(new InstancedMesh(Geo.Sphere(1, 16, 10), new Material { Kind = MatKind.Basic, Color = Material.Hex(0x7affef), Transparent = true, Opacity = 0.22f, Blending = Blend.Additive, DepthWrite = false, Fog = false }, 4) { Count = 0, RenderOrder = 4 });
        opt = new DrawOpts { T = 0, Fdt = 1 / 60f, Lod = 2, Has = has, St = st, S = 1 };
        Changed();
    }

    void Save() => Paths.Save(Paths.Custom, C);
    void Changed() { SP = MedDraw.FromCustom(C); foreach (var (k, m) in MESH) Recolor(m, k[1]); Save(); }
    uint ToneCol(char t) => t == 'k' ? 0x0a0510u : SP.Col.Get(t);
    Material MatFor(char tone)
    {
        uint col = ToneCol(tone);
        if (tone == 'e' || tone == 'k') return new Material { Kind = MatKind.Basic, Color = Material.Hex(col), Fog = false };
        if (tone == 'c') return new Material { Kind = MatKind.Phong, Color = Material.Hex(col), Emissive = Material.Hex(col) * 0.55f, Shininess = 90, Fog = false };
        return new Material { Kind = MatKind.Phong, Color = Material.Hex(col), Shininess = tone == 'd' ? 90 : 55, Specular = Material.Hex(tone == 'd' ? 0xaaaaccu : 0x553344u), Fog = false };
    }
    void Recolor(InstancedMesh m, char t) { var col = Material.Hex(ToneCol(t)); m.Mat.Color = col; if (t == 'c') m.Mat.Emissive = col * 0.55f; }
    void Emit(char sh, char tone, float x, float y, float z, float qx, float qy, float qz, float qw, float sx, float sy, float sz)
    {
        if (tone == 'f') tone = 'a';
        string k = new(new[] { sh, tone });
        if (!MESH.TryGetValue(k, out var m)) { m = new InstancedMesh(GEO[sh], MatFor(tone), 256); m.Count = 0; MESH[k] = m; scene.Add(m); }
        m.EnsureCapacity(m.Count + 1);
        m.Matrices[m.Count++] = Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateFromQuaternion(new Quaternion(qx, qy, qz, qw)) * Matrix4x4.CreateTranslation(x, y, z);
    }

    List<Attach> Atts { get { if (C.Att == null) C.Att = MedDraw.PRESETS[C.Preset].Att.Select(a => a.Clone()).ToList(); return C.Att; } }
    SegStyle SegOf(int i) => C.Seg.TryGetValue(i, out var s) && s != null ? s : MedDraw.PRESETS[C.Preset].Seg[i];
    SegStyle EditSeg(int i) { if (!C.Seg.TryGetValue(i, out var s) || s == null) C.Seg[i] = s = MedDraw.PRESETS[C.Preset].Seg[i].Clone(); return s; }
    void ApplyMirror(int i) { int m = MedDraw.MirrorPart(i); if (mirror && m != i) { var c = C.Seg[i].Clone(); c.OX = -c.OX; C.Seg[m] = c; } }

    float PanelW => Math.Clamp(app.UiW * 0.38f, 340, 500);
    Vector2 ScreenOf(Vector3 p) { var v = cam.Project(p); return new Vector2((v.X + 1) / 2 * app.UiW, (1 - v.Y) / 2 * app.UiH); }
    Vector3 PartCenter(int i) { MedDraw.SegFrame(SP, apose, i, out float x, out float y, out float z, out _, out _, out _, out _); return new Vector3(x, y, z); }
    Quaternion PartRot(int i) { MedDraw.SegFrame(SP, apose, i, out _, out _, out _, out float qx, out float qy, out float qz, out float qw); return new Quaternion(qx, qy, qz, qw); }
    Vector3 AttachWorld(Attach a)
    {
        var (r, L) = MedDraw.Dims(apose, a.Part, SP.Seg[a.Part] ?? new SegStyle(), SP);
        return PartCenter(a.Part) + Vector3.Transform(new Vector3(a.X * r, a.Y * L, a.Z * r), PartRot(a.Part));
    }

    public void Update(float dt)
    {
        T += dt;
        float Wd = app.UiW, Ht = app.UiH, pw = PanelW;
        float umx = Input.MouseX / app.UI.Scale, umy = Input.MouseY / app.UI.Scale;
        bool overPrev = umx < Wd - pw && umy < Ht - 60;
        if (Input.Pressed("Mouse0") && overPrev)
        {
            var m = new Vector2(umx, umy); float best = 34; int bestA = -1, bestS = -1;
            for (int i = 0; i < Atts.Count; i++) { float d = Vector2.Distance(ScreenOf(AttachWorld(Atts[i])), m); if (d < best) { best = d; bestA = i; } }
            if (bestA < 0) for (int i = 0; i < 16; i++) { if (i == 15 && MedDraw.Merged(SP)) continue; float d = Vector2.Distance(ScreenOf(PartCenter(i)), m); if (d < best + 10) { best = d; bestS = i; } }
            if (bestA >= 0) { att = bestA; seg = Atts[bestA].Part; tab = "attach"; dragAtt = true; spin = false; }
            else if (bestS >= 0) { seg = bestS; att = -1; if (tab != "segments") tab = "segments"; if (dragMode == "anchor" && MedDraw.RootOf(bestS) >= 0) dragAnchor = true; else dragSeg = true; spin = false; }
            else dragView = true;
            lastMx = umx; lastMy = umy;
        }
        if (Input.Pressed("Mouse2") && overPrev) { dragView = true; lastMx = umx; lastMy = umy; }
        if (!Input.Down("Mouse0") && !Input.Down("Mouse2")) { if (dragAtt || dragSeg || dragAnchor) Changed(); dragView = dragAtt = dragSeg = dragAnchor = false; }
        float dx = umx - lastMx, dy = umy - lastMy; lastMx = umx; lastMy = umy;
        if (dragView) { yaw -= dx * 0.01f; pitch = Math.Clamp(pitch + dy * 0.006f, -0.4f, 1.1f); }
        else if (dragAtt && att >= 0 && att < Atts.Count) MoveAttach(Atts[att], dx, dy, 0);
        else if (dragAnchor && (dx != 0 || dy != 0)) MoveAnchor(seg, dx, dy, 0);
        else if (dragSeg && (dx != 0 || dy != 0))
        {   // drag a segment: sideways for thickness, up and down for length
            var s = EditSeg(seg); s.Thick = Math.Clamp(s.Thick + dx * 0.006f, 0.4f, 2.2f); s.Len = Math.Clamp(s.Len - dy * 0.006f, 0.4f, 2.2f); ApplyMirror(seg); SP = MedDraw.FromCustom(C);
        }
        if (Input.Wheel != 0 && overPrev) { float d = Math.Clamp((want?.dist ?? dist) * (Input.Wheel < 0 ? 1.1f : 0.9f), 1.4f, 9); want = (d, want?.ty ?? ty); }
        var gp = Gamepad.First;
        if (gp != null)
        {
            if (MathF.Abs(gp.Axes[2]) > 0.2f) { yaw -= gp.Axes[2] * dt * 2.5f; spin = false; }
            if (MathF.Abs(gp.Axes[3]) > 0.2f) pitch = Math.Clamp(pitch + gp.Axes[3] * dt * 1.5f, -0.4f, 1.1f);
            if (tab == "segments" && MedDraw.RootOf(seg) >= 0 && gp.B(10))
            {   // hold the left stick in to move the selected limb's anchor; LB/RB for depth
                float mx = MathF.Abs(gp.Axes[0]) > 0.25f ? gp.Axes[0] : 0, my = MathF.Abs(gp.Axes[1]) > 0.25f ? gp.Axes[1] : 0, mz = (gp.B(5) ? 1 : 0) - (gp.B(4) ? 1 : 0);
                if (mx != 0 || my != 0 || mz != 0) { MoveAnchor(seg, mx * dt * 160, my * dt * 160, mz * dt * 0.6f); Changed(); }
            }
            if (tab == "attach" && att >= 0 && att < Atts.Count)
            {
                var a = Atts[att]; float mx = MathF.Abs(gp.Axes[0]) > 0.25f ? gp.Axes[0] : 0, my = MathF.Abs(gp.Axes[1]) > 0.25f ? gp.Axes[1] : 0, mz = (gp.B(5) ? 1 : 0) - (gp.B(4) ? 1 : 0);
                if (gp.B(10) && (mx != 0 || my != 0 || mz != 0)) { MoveAttach(a, mx * dt * 220, my * dt * 220, mz * dt * 2); Changed(); }   // hold the left stick in to move
                float sc = gp.Value[7] - gp.Value[6]; if (MathF.Abs(sc) > 0.1f) { float k = 1 + sc * dt * 1.2f; a.Sx = Math.Clamp(a.Sx * k, 0.05f, 4); a.Sy = Math.Clamp(a.Sy * k, 0.05f, 6); a.Sz = Math.Clamp(a.Sz * k, 0.05f, 4); Changed(); }
            }
        }
        if (spin && !dragView) yaw += dt * 0.45f;
        if (want is { } wv) { float k = 1 - MathF.Exp(-dt * 6); dist += (wv.dist - dist) * k; ty += (wv.ty - ty) * k; }
        var pos = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch) * dist, ty + MathF.Sin(pitch) * dist, MathF.Cos(yaw) * MathF.Cos(pitch) * dist);
        var target = new Vector3(0, ty, 0); var fwd = Vector3.Normalize(target - pos); var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        float aspect = app.Renderer.Width / (float)Math.Max(1, app.Renderer.Height), halfW = MathF.Tan(cam.Fov * MathF.PI / 360) * aspect * Vector3.Distance(pos, target), shift = pw / 2 / (Wd / 2) * halfW;
        cam.Position = pos + right * shift; cam.LookAt(target + right * shift); cam.Aspect = aspect;
        foreach (var m in MESH.Values) m.Count = 0;
        pose = GermDraw.RestPose(1, T); opt.T = T; opt.Fdt = dt;
        apose = (float[])MedDraw.Anchored(SP, pose).Clone();
        MedDraw.Draw(SP, pose, opt, Emit);
        foreach (var p in GermDraw.HERO_ARMOR) MedDraw.Armor(SP, pose, p.Part, p.Y0, p.Y1, p.Phi, SP.ArmorStyle, 'd', Emit);
        foreach (var m in MESH.Values) m.Touch();
        selMesh.Count = 0;
        if (tab == "segments" || tab == "attach")
        {
            float pulse = 1 + 0.06f * MathF.Sin(T * 6);
            if (tab == "attach" && att >= 0 && att < Atts.Count)
            {
                var a = Atts[att]; var w = AttachWorld(a); var (r, _) = MedDraw.Dims(apose, a.Part, SP.Seg[a.Part] ?? new SegStyle(), SP);
                float s = r * MathF.Max(a.Sx, MathF.Max(a.Sy, a.Sz)) * 1.4f * pulse; selMesh.Matrices[selMesh.Count++] = Matrix4x4.CreateScale(s) * Matrix4x4.CreateTranslation(w);
            }
            else if (tab == "segments")
            {
                foreach (int i in mirror && MedDraw.MirrorPart(seg) != seg ? new[] { seg, MedDraw.MirrorPart(seg) } : new[] { seg })
                {
                    var (r, L) = MedDraw.Dims(apose, i, SP.Seg[i] ?? new SegStyle(), SP); var q = PartRot(i);
                    selMesh.Matrices[selMesh.Count++] = Matrix4x4.CreateScale(r * 1.5f * pulse, L * 1.25f * pulse, r * 1.5f * pulse) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(PartCenter(i));
                }
            }
        }
        selMesh.Touch();
    }

    /// <summary>Moves an attachment by a screen-space drag (pixels) plus a depth nudge, converted into its segment's frame.</summary>
    void MoveAttach(Attach a, float dxPx, float dyPx, float dz)
    {
        var (r, L) = MedDraw.Dims(apose, a.Part, SP.Seg[a.Part] ?? new SegStyle(), SP);
        var q = PartRot(a.Part);
        float wpp = 2 * MathF.Tan(cam.Fov * MathF.PI / 360) * Vector3.Distance(cam.Position, AttachWorld(a)) / app.UiH;
        var camR = Vector3.Transform(Vector3.UnitX, cam.Rotation); var camU = Vector3.Transform(Vector3.UnitY, cam.Rotation); var camF = Vector3.Transform(-Vector3.UnitZ, cam.Rotation);
        var dw = camR * dxPx * wpp - camU * dyPx * wpp + camF * dz * r;
        var dl = Vector3.Transform(dw, Quaternion.Inverse(q));
        a.X = Math.Clamp(a.X + dl.X / r, -4, 4); a.Y = Math.Clamp(a.Y + dl.Y / L, -2, 2); a.Z = Math.Clamp(a.Z + dl.Z / r, -4, 4);
        SP = MedDraw.FromCustom(C);
    }

    /// <summary>Moves a limb's anchor (where it joins the body) by a screen-space drag plus a depth nudge. Units: the torso's radius.</summary>
    void MoveAnchor(int part, float dxPx, float dyPx, float dz)
    {
        int root = MedDraw.RootOf(part); if (root < 0) return;
        int bk = (root is >= 9 and < 15 or 2 ? 2 : 1) * 13;
        var q = new Quaternion(apose[bk + 3], apose[bk + 4], apose[bk + 5], apose[bk + 6]);
        var (tr, _) = MedDraw.Dims(apose, 1, SP.Seg[1] ?? new SegStyle(), SP);
        float wpp = 2 * MathF.Tan(cam.Fov * MathF.PI / 360) * Vector3.Distance(cam.Position, PartCenter(root)) / app.UiH;
        var camR = Vector3.Transform(Vector3.UnitX, cam.Rotation); var camU = Vector3.Transform(Vector3.UnitY, cam.Rotation); var camF = Vector3.Transform(-Vector3.UnitZ, cam.Rotation);
        var dl = Vector3.Transform(camR * dxPx * wpp - camU * dyPx * wpp + camF * dz * tr, Quaternion.Inverse(q)) / MathF.Max(0.01f, tr);
        var s = EditSeg(root);
        s.OX = Math.Clamp(s.OX + dl.X, -1.2f, 1.2f); s.OY = Math.Clamp(s.OY + dl.Y, -1.2f, 1.2f); s.OZ = Math.Clamp(s.OZ + dl.Z, -1.2f, 1.2f);
        ApplyMirror(root); SP = MedDraw.FromCustom(C);
    }

    public void Render(float dt) => app.Renderer.Render(scene, cam);
    void SetCam(float d, float y) => want = (d, y);
    void Close() { Save(); Done = true; }

    public void DrawUI(Draw2D d, float dt)
    {
        float Wd = app.UiW, Ht = app.UiH, pw = PanelW, px = Wd - pw;
        gui.Begin(d, dt);
        if (gui.Back) { if (colorOpen != null) colorOpen = null; else { Close(); return; } }
        string nm = (MedDraw.PRESETS.TryGetValue(C.Preset, out var pre) ? pre.Name : "Hero").ToUpperInvariant();
        d.Text(nm, 22, 16, d.DisplayBig, Draw2D.Col(0xfff4fd), "left", 2.5f, true, Draw2D.Col(0xff3cac));
        string hint = tab == "attach" ? "Click an attachment to select it, drag to move it (controller: hold L3 + stick, LB/RB for depth, triggers for size)" : tab == "segments" ? (dragMode == "anchor" ? "Click a limb and drag it to move where it joins the body (controller: hold L3 + stick, LB/RB for depth)" : "Click a segment to select it; drag it sideways for thickness, up and down for length") : "Drag to turn · wheel to zoom";
        d.Wrap(hint, 22, 56, px - 44, d.MonoSmall, Pal.Mute, 1.3f);
        if (status.Length > 0) d.Text(status, 22, 92, d.MonoSmall, Pal.Warn);
        {
            float bx = px / 2 - 130, by = Ht - 18 - 24;
            if (gui.Chip("spin", bx, by, "Spin", spin)) spin = !spin; bx += gui.lastW + 6;
            if (gui.Chip("headcam", bx, by, "Head")) { SetCam(1.9f, 1.75f); spin = false; } bx += gui.lastW + 6;
            if (gui.Chip("bodycam", bx, by, "Whole body")) SetCam(5.2f, 1.05f);
        }
        d.Rect(px, 0, pw, Ht, Pal.Panel); d.Rect(px, 0, 1, Ht, Pal.HudDim);
        float ix = px + 18, iw = pw - 36, y = 18;
        d.Text("YOUR HERO", ix, y, d.DisplayBig, Pal.White, "left", 2.5f); y += 44;
        float tx = ix; var tf = d.DisplaySmall;
        foreach (var (k, lab) in TABS)
        {
            float tw = tf.Measure(lab.ToUpperInvariant(), 1.3f) + 20; if (tx + tw > ix + iw) { tx = ix; y += 32; }
            bool on = tab == k, hov = gui.Hover(tx, y, tw, 28);
            if (gui.Hit("ct:" + k, tx, y, tw, 28)) { tab = k; scroll = 0; colorOpen = null; }
            d.Rect(tx, y, tw, 28, on ? Draw2D.Col(0xffb347, 0.1f) : hov ? Pal.Chip : new Vector4(0)); d.Frame(tx, y, tw, 28, on ? Pal.Warn : Pal.HudDim);
            d.Text(lab.ToUpperInvariant(), tx + tw / 2, y + 7, tf, on ? Pal.Warn : Pal.Text, "center", 1.3f);
            if (gui.IsFocused("ct:" + k)) d.Frame(tx - 3, y - 3, tw + 6, 34, Pal.White, 2);
            tx += tw + 4;
        }
        y += 40;
        float footH = 92, top = y, viewH = Ht - top - footH - 8;
        gui.PushClip(px + 1, top, pw - 2, viewH);
        float contentH = tab switch { "preset" => TabPreset(d, ix, top - scroll, iw), "build" => TabBuild(d, ix, top - scroll, iw), "segments" => TabSegments(d, ix, top - scroll, iw), "attach" => TabAttach(d, ix, top - scroll, iw), "colors" => TabColors(d, ix, top - scroll, iw), _ => TabArmor(d, ix, top - scroll, iw) };
        gui.PopClip();
        gui.ScrollPanel(ref scroll, top, viewH, contentH);
        float fy = Ht - footH + 4, fx = ix;
        d.Rect(px + 1, fy - 6, pw - 2, 1, Pal.HudFaint);
        if (gui.Chip("random", fx, fy, "Randomize")) Randomize(); fx += gui.lastW + 5;
        if (gui.Chip("reset", fx, fy, "Reset", false, Draw2D.Col(0xff9ae8))) { C = new GermCustom { Preset = C.Preset }; att = -1; Changed(); } fx += gui.lastW + 5;
        if (gui.Chip("export", fx, fy, "Export…")) Export(); fx += gui.lastW + 5;
        if (gui.Chip("import", fx, fy, "Import…")) Import();
        if (gui.Big("done", ix, fy + 32, iw, 40, "Save and back")) Close();
        if (gui.Focus == null && gui.NavUsed) gui.Focus = "ct:" + tab;
    }

    void Export()
    {
        var path = FileDialogs.Save(app.Window.Handle, "Export hero", "Hero (*.germ.json)|*.germ.json", Paths.Germs, (C.Preset ?? "hero") + ".germ.json");
        if (path == null) return;
        Paths.Save(path, C); status = "Saved " + System.IO.Path.GetFileName(path);
    }
    void Import()
    {
        var path = FileDialogs.Open(app.Window.Handle, "Import hero", "Hero (*.germ.json;*.json)|*.germ.json;*.json", Paths.Germs);
        if (path == null) return;
        var g = Paths.Load<GermCustom>(path);
        if (g == null || !MedDraw.PRESETS.ContainsKey(g.Preset ?? "")) { status = "That file isn't a hero design"; return; }
        g.Seg ??= new(); C = g; att = -1; Changed(); status = "Loaded " + System.IO.Path.GetFileName(path);
    }

    float Csub(Draw2D d, string s, float x, float y) { d.Text(s.ToUpperInvariant(), x, y + 12, d.DisplaySmall, Pal.Hud, "left", 2); return 32; }
    float Note(Draw2D d, string s, float x, float y, float w) => d.Wrap(s, x, y + 10, w, d.MonoSmall, Pal.Mute, 1.5f) + 14;
    float CRow(Draw2D d, string id, string label, ref float v, float min, float max, float step, float x, float y, float w, Func<float, string> fmt = null, float labW = 110)
    {
        d.Text(label, x, y + 4, d.MonoSmall, Draw2D.Col(0xc8d8e0));
        v = gui.Slider(id, x + labW, y + 1, w - labW - 52, v, min, max, step, out bool ch, Pal.Hot);
        d.Text((fmt ?? (q => q.ToString("0.00")))(v), x + w, y + 4, d.MonoSmall, Pal.Hud, "right");
        if (ch) Changed();
        return 28;
    }
    float ColorRow(Draw2D d, string id, string label, uint val, Action<uint> set, float x, float y, float w, float labW = 110)
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
            H = gui.Slider(id + ":h", x + 50, y + h, w - 50, H, 0, 1, 0.005f, out _, Draw2D.Col(M.Hsl(H, 1, 0.5f))); d.Text("Hue", x, y + h + 3, d.MonoSmall, Pal.Mute); h += 26;
            S = gui.Slider(id + ":s", x + 50, y + h, w - 50, S, 0, 1, 0.01f, out _, Pal.Hot); d.Text("Sat", x, y + h + 3, d.MonoSmall, Pal.Mute); h += 26;
            L = gui.Slider(id + ":l", x + 50, y + h, w - 50, L, 0, 1, 0.01f, out _, Pal.White); d.Text("Light", x, y + h + 3, d.MonoSmall, Pal.Mute); h += 30;
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
    static string Fit(string s, UiFont f, float w) { if (f.Measure(s) <= w) return s; while (s.Length > 1 && f.Measure(s + "…") > w) s = s[..^1]; return s + "…"; }

    float TabPreset(Draw2D d, float x, float y, float w)
    {
        float y0 = y; int cols = Math.Max(1, (int)((w + 6) / 150)); float cw = (w - (cols - 1) * 6) / cols; int i = 0;
        foreach (var k in MedDraw.PLAYER_PRESETS)
        {
            var s = MedDraw.PRESETS[k];
            float cx = x + (i % cols) * (cw + 6), cy = y + (i / cols) * 62; bool on = C.Preset == k, hov = gui.Hover(cx, cy, cw, 56);
            if (gui.Hit("pr:" + k, cx, cy, cw, 56)) { C = new GermCustom { Preset = k, ArmorCol = C.ArmorCol, Armor = C.Armor }; att = -1; Changed(); }
            d.Rect(cx, cy, cw, 56, on ? Pal.ChipOn : hov ? Pal.A(Pal.BtnHover, 0.5f) : Pal.Chip); d.Frame(cx, cy, cw, 56, on ? Pal.Warn : Pal.HudDim);
            float sw = cw - 16; d.Rect(cx + 8, cy + 7, sw * 0.5f, 6, Draw2D.Col(s.Col.A)); d.Rect(cx + 8 + sw * 0.5f, cy + 7, sw * 0.3f, 6, Draw2D.Col(s.Col.B)); d.Rect(cx + 8 + sw * 0.8f, cy + 7, sw * 0.2f, 6, Draw2D.Col(s.Col.C));
            d.Text(Fit(s.Name.ToUpperInvariant(), d.Display, cw - 16), cx + 8, cy + 17, d.Display, Pal.Hud, "left", 1.4f);
            d.Text(Fit(s.Blurb, d.MonoSmall, cw - 16), cx + 8, cy + 37, d.MonoSmall, Pal.Mute);
            if (gui.IsFocused("pr:" + k)) d.Frame(cx - 3, cy - 3, cw + 6, 62, Pal.White, 2);
            i++;
        }
        y += (i + cols - 1) / cols * 62;
        y += Note(d, "A preset sets every segment, the attachments and the colours. Change any of it under Segments, Attachments and Colours. Looks only: nothing here changes speed, strength or health.", x, y, w);
        return y - y0;
    }

    static readonly string[] SEG_SHORT = { "Head", "Chest", "Waist", "L arm", "L forearm", "L hand", "R arm", "R forearm", "R hand", "L thigh", "L shin", "L foot", "R thigh", "R shin", "R foot", "Abdomen" };
    // the order the chips are shown in: head and the three core segments first
    static readonly int[] SEG_ORDER = { 0, 1, 15, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };

    float TabBuild(Draw2D d, float x, float y, float w)
    {
        float y0 = y;
        y += Csub(d, "Proportions", x, y);
        float hgt = C.Height, arm = C.ArmLen, leg = C.LegLen;
        float b0 = C.Bulk > 0 ? C.Bulk : MedDraw.PRESETS[C.Preset].Bulk, bulk = b0;
        y += CRow(d, "bh", "Height", ref hgt, 0.8f, 1.2f, 0.01f, x, y, w);
        y += CRow(d, "ba", "Arm length", ref arm, 0.75f, 1.3f, 0.01f, x, y, w);
        y += CRow(d, "bl", "Leg length", ref leg, 0.75f, 1.3f, 0.01f, x, y, w);
        y += CRow(d, "bulk", "Shoulders", ref bulk, 0.7f, 1.6f, 0.05f, x, y, w);
        if (hgt != C.Height || arm != C.ArmLen || leg != C.LegLen || MathF.Abs(bulk - b0) > 1e-4f) { C.Height = hgt; C.ArmLen = arm; C.LegLen = leg; if (MathF.Abs(bulk - b0) > 1e-4f) C.Bulk = bulk; Changed(); }
        if (gui.Chip("breset", x, y, "Reset proportions", false, Draw2D.Col(0xff9ae8))) { C.Height = C.ArmLen = C.LegLen = 1; C.Bulk = 0; Changed(); }
        y += 32;
        y += Note(d, "Height scales the whole hero; longer legs lift the body so the feet stay planted. Arms and legs are kept clear of the chest and of each other automatically. The game plays the same at any size.", x, y, w);
        return y - y0;
    }

    float TabSegments(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        y += Csub(d, "Segment", x, y);
        var labels = SEG_ORDER.Select(i => SEG_SHORT[i]).ToList();
        if ((r = gui.Chips("segs", x, y, w, labels, k => SEG_ORDER[k] == seg || (mirror && MedDraw.MirrorPart(seg) == SEG_ORDER[k]), out h)) >= 0) { seg = SEG_ORDER[r]; att = -1; }
        y += h + 6;
        if (gui.Chip("mirror", x, y, "Mirror left/right", mirror)) mirror = !mirror;
        if (gui.Chip("segreset", x + gui.lastW + 6, y, "Reset segment", false, Draw2D.Col(0xff9ae8))) { C.Seg.Remove(seg); if (mirror) C.Seg.Remove(MedDraw.MirrorPart(seg)); Changed(); }
        y += 32;
        var cur = SegOf(seg);
        y += Csub(d, MedDraw.PART_NAMES[seg] + " · shape", x, y);
        if (seg == 15 && cur.Shape == "none") y += Note(d, "Merged into the chest. Pick a shape to split the torso into chest, abdomen and waist.", x, y, w);
        var shapes = MedDraw.ShapesFor(seg);
        var shapeLabels = shapes.Select(sh => sh == "none" ? "merged" : sh).ToList();
        if ((r = gui.Chips("shape", x, y, w, shapeLabels, i => cur.Shape == shapes[i], out h)) >= 0) { EditSeg(seg).Shape = shapes[r]; ApplyMirror(seg); Changed(); }
        y += h + 8;
        if (!(seg == 15 && cur.Shape == "none"))
        {
            var es = SegOf(seg); float th = es.Thick, ln = es.Len, tp = es.Taper;
            y += CRow(d, "sth", "Thickness", ref th, 0.4f, 2.2f, 0.05f, x, y, w);
            y += CRow(d, "sln", "Length", ref ln, 0.4f, 2.2f, 0.05f, x, y, w);
            y += CRow(d, "stp", "Taper", ref tp, -0.6f, 0.6f, 0.05f, x, y, w, v => v > 0.02f ? "wide top" : v < -0.02f ? "wide base" : "even");
            if (th != es.Thick || ln != es.Len || tp != es.Taper) { var e = EditSeg(seg); e.Thick = th; e.Len = ln; e.Taper = tp; ApplyMirror(seg); Changed(); }
            y += Csub(d, "Detail", x, y);
            if ((r = gui.Chips("detail", x, y, w, MedDraw.DETAILS, i => (SegOf(seg).Detail ?? "none") == MedDraw.DETAILS[i], out h)) >= 0) { EditSeg(seg).Detail = MedDraw.DETAILS[r]; ApplyMirror(seg); Changed(); }
            y += h + 8;
            if (seg == 0)
            {
                y += Csub(d, "Visor", x, y);
                if ((r = gui.Chips("visor", x, y, w, MedDraw.VISORS, i => (SegOf(0).Visor ?? "default") == MedDraw.VISORS[i], out h)) >= 0) { EditSeg(0).Visor = MedDraw.VISORS[r]; Changed(); }
                y += h + 8;
                y += Csub(d, "On top", x, y);
                if ((r = gui.Chips("antenna", x, y, w, MedDraw.ANTENNAS, i => (SegOf(0).Antenna ?? "none") == MedDraw.ANTENNAS[i], out h)) >= 0) { EditSeg(0).Antenna = MedDraw.ANTENNAS[r]; Changed(); }
                y += h + 8;
            }
            y += Csub(d, "Colour", x, y);
            if ((r = gui.Chips("tone", x, y, w, TONES.Select(t => t.label).ToList(), i => SegOf(seg).Tone == TONES[i].tone, out h)) >= 0) { EditSeg(seg).Tone = TONES[r].tone; ApplyMirror(seg); Changed(); }
            y += h + 8;
            if (gui.Chip("two", x, y, "Two-tone halves", SegOf(seg).TwoTone)) { var e = EditSeg(seg); e.TwoTone = !e.TwoTone; ApplyMirror(seg); Changed(); }
            y += 32;
            if (SegOf(seg).TwoTone || SegOf(seg).Shape is "tablet" or "disc" or "cylinder" or "vial" or "bottle" or "boot" or "trapezoid" or "hex" or "ribbed" or "wedge" or "claw" or "mitt" || SegOf(seg).Detail == "ridges")
            {
                y += Csub(d, "Second colour", x, y);
                if ((r = gui.Chips("tone2", x, y, w, TONES.Select(t => t.label).ToList(), i => SegOf(seg).Tone2 == TONES[i].tone, out h)) >= 0) { EditSeg(seg).Tone2 = TONES[r].tone; ApplyMirror(seg); Changed(); }
                y += h + 8;
            }
        }
        int root = MedDraw.RootOf(seg);
        if (root >= 0 && !(root == 15 && SegOf(15).Shape == "none"))
        {
            y += Csub(d, "Position · " + MedDraw.PART_NAMES[root], x, y);
            if (gui.Chip("dsize", x, y, "Drag resizes", dragMode == "size")) dragMode = "size";
            if (gui.Chip("danchor", x + gui.lastW + 6, y, "Drag moves it", dragMode == "anchor")) dragMode = "anchor";
            y += 32;
            var rs = SegOf(root); float ax = rs.OX, ay = rs.OY, az = rs.OZ;
            y += CRow(d, "aox", "Side", ref ax, -1.2f, 1.2f, 0.05f, x, y, w);
            y += CRow(d, "aoy", "Height", ref ay, -1.2f, 1.2f, 0.05f, x, y, w);
            y += CRow(d, "aoz", "Depth", ref az, -1.2f, 1.2f, 0.05f, x, y, w);
            if (ax != rs.OX || ay != rs.OY || az != rs.OZ) { var e = EditSeg(root); e.OX = ax; e.OY = ay; e.OZ = az; ApplyMirror(root); Changed(); }
            if (gui.Chip("areset", x, y, "Reset position", false, Draw2D.Col(0xff9ae8))) { var e = EditSeg(root); e.OX = e.OY = e.OZ = 0; ApplyMirror(root); Changed(); }
            y += 32;
        }
        y += Note(d, root == 1 ? "Moving the chest takes the head and arms with it." : "Tip: click a segment on the model to select it, then drag it to resize it or move it (controller: hold L3 + stick to move it).", x, y, w);
        return y - y0;
    }

    float TabAttach(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        y += Csub(d, "Add to " + MedDraw.PART_NAMES[seg].ToLowerInvariant(), x, y);
        if ((r = gui.Chips("addatt", x, y, w, MedDraw.ATTACH_SHAPES, i => false, out h)) >= 0)
        {
            var a = new Attach { Part = seg, Shape = MedDraw.ATTACH_SHAPES[r], Y = 0.3f, Z = -1.1f, Sx = 0.5f, Sy = 0.5f, Sz = 0.5f, Tone = 'c' };
            if (a.Shape is "needle" or "cone" or "capsule") { a.Sy = 1.6f; a.Sx = a.Sz = 0.4f; } else if (a.Shape is "fin") { a.Sx = 0.15f; a.Sy = 0.9f; a.Sz = 1.0f; a.Z = 0.6f; a.Y = 0.8f; } else if (a.Shape is "disc" or "ring") a.Sy = 0.25f;
            Atts.Add(a); att = Atts.Count - 1; Changed();
        }
        y += h + 6;
        y += Csub(d, "Attachments", x, y);
        var names = Atts.Select((a, i) => (i + 1) + " " + a.Shape + " · " + MedDraw.PART_NAMES[a.Part].Replace("Left ", "L ").Replace("Right ", "R ").ToLowerInvariant()).ToList();
        if (names.Count == 0) y += Note(d, "None yet: add one above. It goes on the selected segment (pick one on the model or under Segments).", x, y, w);
        else { if ((r = gui.Chips("atts", x, y, w, names, i => i == att, out h)) >= 0) { att = r; seg = Atts[r].Part; } y += h + 8; }
        if (att >= 0 && att < Atts.Count)
        {
            var a = Atts[att];
            y += Csub(d, "Selected · drag it on the model to move it", x, y);
            if ((r = gui.Chips("atone", x, y, w, TONES.Select(t => t.label).ToList(), i => a.Tone == TONES[i].tone, out h)) >= 0) { a.Tone = TONES[r].tone; Changed(); }
            y += h + 8;
            float sx = a.Sx, sy = a.Sy, sz = a.Sz, rx = a.Rx, ry = a.Ry, rz = a.Rz, px = a.X, py = a.Y, pz = a.Z;
            y += CRow(d, "asx", "Width", ref sx, 0.05f, 4, 0.01f, x, y, w); y += CRow(d, "asy", "Height", ref sy, 0.05f, 6, 0.01f, x, y, w); y += CRow(d, "asz", "Depth", ref sz, 0.05f, 4, 0.01f, x, y, w);
            y += CRow(d, "arx", "Tilt", ref rx, -180, 180, 5, x, y, w, v => MathF.Round(v) + "°"); y += CRow(d, "ary", "Turn", ref ry, -180, 180, 5, x, y, w, v => MathF.Round(v) + "°"); y += CRow(d, "arz", "Roll", ref rz, -180, 180, 5, x, y, w, v => MathF.Round(v) + "°");
            y += CRow(d, "apx", "Side", ref px, -4, 4, 0.02f, x, y, w); y += CRow(d, "apy", "Up", ref py, -2, 2, 0.02f, x, y, w); y += CRow(d, "apz", "Forward", ref pz, -4, 4, 0.02f, x, y, w);
            a.Sx = sx; a.Sy = sy; a.Sz = sz; a.Rx = rx; a.Ry = ry; a.Rz = rz; a.X = px; a.Y = py; a.Z = pz; SP = MedDraw.FromCustom(C);
            string[] acts = { "Duplicate", "Mirror copy", "Delete" };
            if ((r = gui.Chips("aact", x, y, w, acts, i => false, out h)) >= 0)
            {
                if (r == 0) { var c = a.Clone(); c.X += 0.3f; Atts.Add(c); att = Atts.Count - 1; }
                else if (r == 1) { var c = a.Clone(); c.Part = MedDraw.MirrorPart(a.Part); c.X = -a.X; c.Ry = -a.Ry; c.Rz = -a.Rz; Atts.Add(c); att = Atts.Count - 1; }
                else { Atts.RemoveAt(att); att = -1; }
                Changed();
            }
            y += h + 8;
        }
        if (gui.Chip("attclear", x, y, "Remove all", false, Draw2D.Col(0xff9ae8))) { C.Att = new(); att = -1; Changed(); }
        return y + 32 - y0;
    }

    float TabColors(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        y += Csub(d, "Palettes", x, y);
        if ((r = gui.Chips("pal", x, y, w, PALETTES.Select(p => p.name).ToList(), i => false, out h)) >= 0)
        { var p = PALETTES[r].p; C.Col = p == null ? null : new Dictionary<string, uint> { ["a"] = p[0], ["b"] = p[1], ["c"] = p[2], ["e"] = p[3] }; Changed(); }
        y += h + 6; y += Csub(d, "Pick your own", x, y);
        foreach (var (k, lab) in new[] { ("a", "Body"), ("b", "Second colour"), ("c", "Glow"), ("e", "Visor") })
        {
            string kk = k;
            y += ColorRow(d, "c" + k, lab, SP.Col.Get(k[0]), v =>
            {
                var cur = MedDraw.FromCustom(C).Col;
                C.Col ??= new Dictionary<string, uint> { ["a"] = cur.A, ["b"] = cur.B, ["c"] = cur.C, ["e"] = cur.E };
                C.Col[kk] = v;
            }, x, y, w);
        }
        y += ColorRow(d, "carmor", "Armor", C.ArmorCol, v => C.ArmorCol = v, x, y, w);
        return y - y0;
    }
    float TabArmor(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        y += Csub(d, "Armor style", x, y);
        string[] names = { "Plates", "Shells", "Angular", "Rings", "Studs", "None" };
        if ((r = gui.Chips("arm", x, y, w, names, i => (C.Armor ?? "plate") == MedDraw.ARMOR_STYLES[i], out h)) >= 0) { C.Armor = MedDraw.ARMOR_STYLES[r]; Changed(); }
        y += h + 8;
        y += ColorRow(d, "armc", "Armor colour", C.ArmorCol, v => C.ArmorCol = v, x, y, w);
        y += Note(d, "A medic's kit: V-cut breastplate, vial-carrying back plate, crest helmet, stepped pauldrons, bracers, tassets and greaves. Plates are flat panels, Shells the same kit rounded, Angular adds spikes. Every style protects the same: shots chip pieces off whatever they look like.", x, y, w);
        return y - y0;
    }

    void Randomize()
    {
        var rnd = new Random(); T Pick<T>(IList<T> a) => a[rnd.Next(a.Count)]; float R() => (float)rnd.NextDouble();
        C = new GermCustom { Preset = Pick(MedDraw.PLAYER_PRESETS) };
        float hh = R();
        if (R() < 0.75f) C.Col = new Dictionary<string, uint> { ["a"] = M.Hsl(hh, 0.65f, 0.55f), ["b"] = M.Hsl(hh + 0.08f, 0.3f, 0.9f), ["c"] = M.Hsl(hh + 0.45f, 0.9f, 0.6f), ["e"] = M.Hsl(hh + 0.5f, 0.9f, 0.65f) };
        C.Armor = Pick(MedDraw.ARMOR_STYLES); C.ArmorCol = M.Hsl(hh + 0.15f, 0.25f, 0.82f);
        if (R() < 0.5f) { var s = MedDraw.PRESETS[C.Preset].Seg[1].Clone(); s.Shape = Pick(new[] { "capsule", "tablet", "bottle", "vial", "pill" }); C.Seg[1] = s; }
        att = -1; Changed();
    }
}

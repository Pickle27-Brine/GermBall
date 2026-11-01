using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using GermBall;
using GermBall.Game;
using GermBall.Platform;
using GermBall.Render;
using GermBall.UI;

namespace Smoke;

/// <summary>Renders the screens and a few in-game views to PNGs with the software renderer.</summary>
public static class Shots
{
    static readonly string Scratch = Path.Combine(Path.GetTempPath(), "germball-fonts");
    static UiFont Bake(string face, int px, int weight)
    {
        Directory.CreateDirectory(Scratch);
        bool mono = face.Contains("Consolas"); bool cond = face.Contains("Condensed") && !face.Contains("Semi");
        string ttf = mono ? "/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf" : weight >= 600 ? "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf" : "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf";
        float xs = mono ? 0.92f : cond ? 0.74f : 0.86f;
        int bpx = px * UiFont.Oversample;
        string key = $"{Path.GetFileNameWithoutExtension(ttf)}-{bpx}-{xs}", png = Path.Combine(Scratch, key + ".png"), js = Path.Combine(Scratch, key + ".json");
        if (!File.Exists(js))
        {
            var script = Path.Combine(AppContext.BaseDirectory, "bakefont.py");
            var p = Process.Start(new ProcessStartInfo("python3", $"\"{script}\" \"{ttf}\" {bpx} {xs.ToString(System.Globalization.CultureInfo.InvariantCulture)} \"{png}\" \"{js}\"") { UseShellExecute = false });
            p.WaitForExit();
        }
        var atlas = Png.Load(png);
        // the baked atlas is greyscale coverage: make it white with coverage in alpha, like the GDI path
        for (int i = 0; i < atlas.Pixels.Length; i += 4) { byte a = atlas.Pixels[i]; atlas.Pixels[i] = atlas.Pixels[i + 1] = atlas.Pixels[i + 2] = 255; atlas.Pixels[i + 3] = a; }
        atlas.Nearest = false; atlas.Mipmaps = false;
        using var doc = JsonDocument.Parse(File.ReadAllText(js)); var r = doc.RootElement; var g = new Dictionary<char, UiFont.Glyph>();
        foreach (var e in r.GetProperty("glyphs").EnumerateObject())
        {
            var a = e.Value; g[e.Name[0]] = new UiFont.Glyph { X = a[0].GetInt32(), Y = a[1].GetInt32(), W = a[2].GetInt32(), H = a[3].GetInt32(), Advance = (float)a[4].GetDouble() };
        }
        float k = UiFont.Oversample;
        return new UiFont(px, r.GetProperty("ascent").GetSingle() / k, r.GetProperty("lineHeight").GetSingle() / k, atlas, g, k);
    }

    static App app; static SoftRenderer sr; static string outDir;
    static void Frame(IScreen s, float dt = 1 / 60f, string name = null)
    {
        s.Update(dt); s.Render(dt);
        app.UpdateUiScale(); app.UI.Begin(); s.DrawUI(app.UI, dt); app.UI.End(sr);
        Input.EndFrame();
        if (name != null) { var px = sr.Capture(out int w, out int h); File.WriteAllBytes(Path.Combine(outDir, name + ".png"), Screenshot.Png(px, w, h)); Console.WriteLine("shot " + name + $" ({sr.Triangles} tris)"); }
    }

    public static void Run(string dir, int w, int h, string only)
    {
        outDir = dir; Directory.CreateDirectory(dir);
        UiFont.Factory = Bake;
        sr = new SoftRenderer(w, h);
        app = new App(Window.Headless(w, h), sr);
        bool Want(string n) => only == null || only.Split(',').Contains(n);
        if (Want("title")) { var t = new TitleScreen(app); for (int i = 0; i < 3; i++) Frame(t); Frame(t, name: "title"); }
        if (Want("custom")) { var c = new CustomizeScreen(app, GermDraw.DefaultCustom()); for (int i = 0; i < 30; i++) Frame(new Wrap(c)); Frame(new Wrap(c), name: "custom"); }
        if (Want("lineup")) { Lineup.Run(sr, Path.Combine(dir, "lineup-heroes.png"), false); Lineup.Run(sr, Path.Combine(dir, "lineup-heroes-back.png"), false, 200); Lineup.Run(sr, Path.Combine(dir, "lineup-heroes-bare.png"), false, 25, false); Lineup.Run(sr, Path.Combine(dir, "lineup-germs.png"), true); }
        if (Want("heroes"))
            foreach (var k in MedDraw.PRESETS.Keys)
            {
                var c = new CustomizeScreen(app, new GermCustom { Preset = k }); c.C.Preset = k; typeof(CustomizeScreen).GetMethod("Changed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(c, null);
                for (int i = 0; i < 6; i++) Frame(new Wrap(c), 0.5f); Frame(new Wrap(c), name: "hero-" + k);
            }
        foreach (var (mode, map) in new[] { ("sandbox", "random"), ("waves", "bowl"), ("sandbox", "leviathan") })
        {
            string tag = mode + "-" + map;
            if (!Want(tag) && !Want("game")) continue;
            var world = new World(new BootConfig { Mode = mode, Map = map, Seed = 12345, Custom = GermDraw.DefaultCustom() }, new Settings { res = 0, companion = true, companion2 = true, companion3 = true }, World.DefaultBinds());
            var gs = new GameScreen(app, world);
            for (int i = 0; i < 5; i++) Frame(gs);
            Frame(gs, name: tag + "-brief");
            world.playing = true;
            for (int i = 0; i < 120; i++) Frame(gs);
            Frame(gs, name: tag + "-hud");
            world.DebugEnemy("squad"); world.DebugVehicle("rover"); world.DebugVehicle("chair"); world.DebugVehicle("bike");
            for (int i = 0; i < 240; i++) { Input.KeyEvent("KeyW", i % 120 < 60); Frame(gs); }
            Input.KeyEvent("KeyW", false);
            Frame(gs, name: tag + "-fight");
            Diagnose(world);
            {   // the same frame without additive effects, and without opaque meshes, to find stray geometry
                var hidden = new List<Node>();
                void Hide(Node n, bool additive) { if (n is Mesh m && m.Mat != null && (m.Mat.Blending == Blend.Additive || m.Mat.Transparent) == additive && n.Visible) { n.Visible = false; hidden.Add(n); } if (n is Lines l && additive && n.Visible) { n.Visible = false; hidden.Add(n); } foreach (var c in n.Children) Hide(c, additive); }
                Hide(world.scene, true); sr.Render(world.scene, world.camera); { var px = sr.Capture(out int ww, out int hh); File.WriteAllBytes(Path.Combine(outDir, tag + "-fight-noadd.png"), Screenshot.Png(px, ww, hh)); }
                foreach (var n in hidden) n.Visible = true;
            }
            // close-up of the hero and an overhead
            world.SetFreeCam(true); world.FREECAM.x = world.HUMAN.P.x + 3; world.FREECAM.y = world.HUMAN.P.y + 2; world.FREECAM.z = world.HUMAN.P.z + 3; world.FREECAM.yaw = MathF.PI / 4; world.FREECAM.pitch = -0.3f; world.FREECAM.fov = 50;
            for (int i = 0; i < 10; i++) Frame(gs);
            Frame(gs, name: tag + "-closeup");
            world.FREECAM.y = world.HUMAN.P.y + 120; world.FREECAM.pitch = -1.45f; world.FREECAM.fov = 70;
            for (int i = 0; i < 20; i++) Frame(gs);
            Frame(gs, name: tag + "-overhead");
            world.SetFreeCam(false);
        }
    }
    /// <summary>Lists instances with absurd transforms (huge, NaN) and names the draw list they came from.</summary>
    static void Diagnose(World w)
    {
        var names = w.IM as System.Collections.IDictionary;
        var byMesh = new Dictionary<object, string>();
        if (names != null) foreach (System.Collections.DictionaryEntry e in names) { var v = e.Value; var f = v.GetType().GetField("m") ?? v.GetType().GetField("im") ?? v.GetType().GetField("Mesh"); var mesh = f?.GetValue(v) ?? v; byMesh[mesh] = (string)e.Key; }
        void Walk(Node n)
        {
            if (n is InstancedMesh im)
                for (int i = 0; i < Math.Min(im.Count, im.Capacity); i++)
                {
                    var m = im.Matrices[i]; float sx = new System.Numerics.Vector3(m.M11, m.M12, m.M13).Length(), sy = new System.Numerics.Vector3(m.M21, m.M22, m.M23).Length(), sz = new System.Numerics.Vector3(m.M31, m.M32, m.M33).Length();
                    bool bad = !float.IsFinite(sx + sy + sz + m.M41 + m.M42 + m.M43) || MathF.Max(sx, MathF.Max(sy, sz)) > 60;
                    if (bad) { Console.WriteLine($"  BAD instance in '{(byMesh.TryGetValue(im, out var nm) ? nm : im.Mat?.Color.ToString())}' #{i}: scale {sx:0.0},{sy:0.0},{sz:0.0} at {m.M41:0.0},{m.M42:0.0},{m.M43:0.0}"); break; }
                }
            foreach (var c in n.Children) Walk(c);
        }
        Walk(w.scene);
    }
    sealed class Wrap : IScreen
    {
        readonly CustomizeScreen c; public Wrap(CustomizeScreen c) { this.c = c; }
        public void Update(float dt) => c.Update(dt); public void Render(float dt) => c.Render(dt); public void DrawUI(Draw2D d, float dt) => c.DrawUI(d, dt);
    }
}

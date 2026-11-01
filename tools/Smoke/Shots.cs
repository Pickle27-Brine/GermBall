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
    sealed class Wrap : IScreen
    {
        readonly CustomizeScreen c; public Wrap(CustomizeScreen c) { this.c = c; }
        public void Update(float dt) => c.Update(dt); public void Render(float dt) => c.Render(dt); public void DrawUI(Draw2D d, float dt) => c.DrawUI(d, dt);
    }
}

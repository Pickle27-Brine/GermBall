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
        if (Want("custom"))
        {
            var c = new CustomizeScreen(app, GermDraw.DefaultCustom()); for (int i = 0; i < 30; i++) Frame(new Wrap(c)); Frame(new Wrap(c), name: "custom");
            foreach (var tb in new[] { "segments", "attach", "colors", "armor" })
            {
                typeof(CustomizeScreen).GetField("tab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(c, tb);
                if (tb == "attach") typeof(CustomizeScreen).GetField("att", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(c, 0);
                for (int i = 0; i < 4; i++) Frame(new Wrap(c)); Frame(new Wrap(c), name: "custom-" + tb);
            }
        }
        if (Want("lineup")) { Lineup.Run(sr, Path.Combine(dir, "lineup-heroes.png"), false); Lineup.Run(sr, Path.Combine(dir, "lineup-heroes-back.png"), false, 200); Lineup.Run(sr, Path.Combine(dir, "lineup-heroes-bare.png"), false, 25, false); Lineup.Run(sr, Path.Combine(dir, "lineup-germs.png"), true); }
        if (Want("vehicles"))
        {
            var world = new World(new BootConfig { Mode = "waves", Map = "bowl", Seed = 7, Custom = GermDraw.DefaultCustom() }, new Settings { res = 0, density = 0 }, World.DefaultBinds());
            var gs = new GameScreen(app, world); world.playing = true;
            var H = world.HUMAN.P; float fx = -MathF.Sin(H.facing), fz = -MathF.Cos(H.facing);
            var rov = world.MakeVehicle("rover", H.x + fx * 7 - fz * 3, H.z + fz * 7 + fx * 3, H.facing);
            var ch = world.MakeVehicle("chair", H.x + fx * 6 + fz * 3, H.z + fz * 6 - fx * 3, H.facing);
            var ch2 = world.MakeVehicle("chair", H.x + fx * 10 + fz * 1, H.z + fz * 10 - fx * 1, H.facing); ch2.alt = 4;
            for (int i = 0; i < 90; i++) { ch2.y = world.GroundY(ch2.x, ch2.z) + 3.5f; ch2.py = ch2.y; Frame(gs); }
            world.SetFreeCam(true); world.FREECAM.x = H.x - fx * 2 + fz * 4; world.FREECAM.y = H.y + 3; world.FREECAM.z = H.z - fz * 2 - fx * 4; world.FREECAM.yaw = H.facing - 0.4f; world.FREECAM.pitch = -0.25f; world.FREECAM.fov = 55;
            for (int i = 0; i < 12; i++) { ch2.y = world.GroundY(ch2.x, ch2.z) + 3.5f; ch2.py = ch2.y; Frame(gs); }
            Frame(gs, name: "vehicles");
        }
        if (Want("weapons"))
        {
            var world = new World(new BootConfig { Mode = "waves", Map = "bowl", Seed = 7, Custom = GermDraw.DefaultCustom() }, new Settings { res = 0, density = 0 }, World.DefaultBinds());
            var gs = new GameScreen(app, world); world.playing = true; var H = world.HUMAN;
            foreach (var (id, aimOn, tag2) in new[] { ("pistol", true, ""), ("rifle", true, ""), ("shotgun", true, ""), ("sniper", true, ""), ("hmg", true, ""), ("rocket", true, ""), ("bow", true, ""), ("armcannon", true, ""), ("rifle", false, "-ready"), ("rifle", true, "-reload") })
            {
                world.GiveWeapon(H, id); Input.KeyEvent("Mouse2", aimOn);
                if (id == "bow") Input.KeyEvent("Mouse0", true);
                for (int i = 0; i < 40; i++) { if (tag2 == "-reload" && i == 25) { H.W.slots[H.W.cur].clip = 1; Input.KeyEvent("KeyR", true); } if (i == 27) Input.KeyEvent("KeyR", false); Frame(gs); }
                float fx = -MathF.Sin(H.P.facing), fz = -MathF.Cos(H.P.facing);
                world.SetFreeCam(true); world.FREECAM.x = H.P.x + fz * 2.8f + fx * 1.2f; world.FREECAM.y = H.P.y + 1.4f; world.FREECAM.z = H.P.z - fx * 2.8f + fz * 1.2f;
                world.FREECAM.yaw = MathF.Atan2(-(H.P.x - world.FREECAM.x), -(H.P.z - world.FREECAM.z)); world.FREECAM.pitch = -0.08f; world.FREECAM.fov = 45;
                for (int i = 0; i < 3; i++) Frame(gs);
                Frame(gs, name: "wpn-" + id + tag2);
                world.SetFreeCam(false); Input.KeyEvent("Mouse0", false); Input.KeyEvent("Mouse2", false);
            }
        }
        if (Want("maps"))
        {   // the title screen's backdrops: a slow oblique flyover still of every map
            foreach (var (mode, map, seed) in new[] { ("sandbox", "random", 12345), ("sandbox", "classic", 1), ("sandbox", "leviathan", 1), ("waves", "bowl", 7), ("waves", "city", 7), ("waves", "spires", 7), ("waves", "gut", 7) })
            {
                var world = new World(new BootConfig { Mode = mode, Map = map, Seed = seed, Custom = GermDraw.DefaultCustom() }, new Settings { res = 0, density = 1, drawDist = 400 }, World.DefaultBinds());
                var gs = new GameScreen(app, world); world.playing = true;
                for (int i = 0; i < 30; i++) Frame(gs);
                var H = world.HUMAN.P; bool arena = mode == "waves";
                world.SetFreeCam(true); float dist = arena ? 70 : 110;
                world.FREECAM.x = H.x + dist * 0.6f; world.FREECAM.z = H.z + dist * 0.6f; world.FREECAM.y = H.y + (arena ? 55 : 85);
                world.FREECAM.yaw = MathF.Atan2(-(H.x - world.FREECAM.x), -(H.z - world.FREECAM.z)); world.FREECAM.pitch = -0.62f; world.FREECAM.fov = 60;
                for (int i = 0; i < 8; i++) Frame(gs);
                world.scene.Shadows = false; world.camera.Far = 3500;
                sr.Render(world.scene, world.camera); var px = sr.Capture(out int ww, out int hh);
                Directory.CreateDirectory(Path.Combine(dir, "maps")); File.WriteAllBytes(Path.Combine(dir, "maps", map + ".png"), Screenshot.Png(px, ww, hh)); Console.WriteLine("map " + map);
            }
        }
        if (Want("interior"))
        {   // inside the biggest building: the ground floor and the floor above, looking across the rooms
            var world = new World(new BootConfig { Mode = "waves", Map = "city", Seed = 7, Custom = GermDraw.DefaultCustom() }, new Settings { res = 0, density = 1 }, World.DefaultBinds());
            var gs = new GameScreen(app, world); world.playing = true;
            for (int i = 0; i < 10; i++) Frame(gs);
            var B = world.BLDG.OrderByDescending(b => b.w * b.d * (b.floors > 1 ? 2 : 1)).First();
            Console.WriteLine($"interior: building {B.w:0.0} x {B.d:0.0}, {B.floors} floors");
            foreach (var mm in new[] { ("waves", "city"), ("sandbox", "classic"), ("sandbox", "random") })
            {
                var w2 = mm.Item2 == "city" ? world : new World(new BootConfig { Mode = mm.Item1, Map = mm.Item2, Seed = 12345, Custom = GermDraw.DefaultCustom() }, new Settings { res = 0 }, World.DefaultBinds());
                int ov = 0; for (int i = 0; i < w2.BLDG.Count; i++) for (int j = i + 1; j < w2.BLDG.Count; j++) { var p = w2.BLDG[i]; var q = w2.BLDG[j]; if (MathF.Abs(p.x - q.x) < (p.w + q.w) / 2 && MathF.Abs(p.z - q.z) < (p.d + q.d) / 2) { ov++; Console.WriteLine($"  overlap {mm.Item2}: {p.x:0},{p.z:0} {p.w:0}x{p.d:0} vs {q.x:0},{q.z:0} {q.w:0}x{q.d:0}"); } }
                Console.WriteLine($"buildings {mm.Item2}: {w2.BLDG.Count}, overlapping pairs {ov}, sizes {string.Join(" ", w2.BLDG.Select(b => $"{b.w:0}x{b.d:0}x{b.floors}"))}");
            }
            world.SetFreeCam(true);
            for (int f = 0; f < Math.Min(2, B.floors); f++)
                foreach (var (cx, cz, yaw) in new[] { (-0.38f, -0.38f, MathF.PI * 1.25f), (0.38f, 0.38f, MathF.PI * 0.25f) })
                {
                    world.FREECAM.x = B.x + cx * B.w; world.FREECAM.z = B.z + cz * B.d; world.FREECAM.y = B.y + 1.7f + f * 3.6f; world.FREECAM.yaw = yaw; world.FREECAM.pitch = -0.12f; world.FREECAM.fov = 80;
                    for (int i = 0; i < 4; i++) Frame(gs);
                    Frame(gs, name: $"interior-f{f}-{(cx < 0 ? "a" : "b")}");
                }
            world.FREECAM.x = B.x; world.FREECAM.z = B.z + B.d * 0.15f; world.FREECAM.y = B.y + B.h + 14; world.FREECAM.pitch = -1.5f; world.FREECAM.fov = 70;
            for (int i = 0; i < 4; i++) Frame(gs);
            Frame(gs, name: "interior-roof");
            // a plan view of the ground floor: the near plane slices the building just under the first ceiling
            world.scene.Shadows = false; float near0 = world.camera.Near; world.camera.Near = B.h + 14 - 3.3f;
            sr.Render(world.scene, world.camera); { var px = sr.Capture(out int ww, out int hh); File.WriteAllBytes(Path.Combine(outDir, "interior-plan.png"), Screenshot.Png(px, ww, hh)); }
            world.camera.Near = near0;
            world.SetFreeCam(false);
        }
        if (Want("cast")) { Lineup.Run(sr, Path.Combine(dir, "cast-raw.png"), false, 18, false); }
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

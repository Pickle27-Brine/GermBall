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
        // the game's bundled fonts (data/fonts) by their GDI face names; anything else falls back to Liberation
        string fd = Path.Combine(AppContext.BaseDirectory, "data", "fonts"); if (!Directory.Exists(fd)) fd = Path.GetFullPath("data/fonts");
        string own = face switch
        {
            "Saira ExtraCondensed Black" => "SairaExtraCondensed-Black.ttf", "Saira ExtraCondensed ExtraBold" => "SairaExtraCondensed-ExtraBold.ttf",
            "Chakra Petch SemiBold" => "ChakraPetch-SemiBold.ttf", "Chakra Petch" => "ChakraPetch-Bold.ttf",
            "Space Mono" => weight >= 700 ? "SpaceMono-Bold.ttf" : "SpaceMono-Regular.ttf", _ => null,
        };
        bool mono = face.Contains("Consolas"); bool cond = face.Contains("Condensed") && !face.Contains("Semi");
        string ttf = own != null && File.Exists(Path.Combine(fd, own)) ? Path.Combine(fd, own) : mono ? "/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf" : weight >= 600 ? "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf" : "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf";
        float xs = own != null ? 1f : mono ? 0.92f : cond ? 0.74f : 0.86f;
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
            {   // regression: change options mid-session; shapes that fall out of use must draw nothing (they once drew at unit size)
                var c0 = new CustomizeScreen(app, GermDraw.DefaultCustom()); for (int i = 0; i < 10; i++) Frame(new Wrap(c0));
                var fl = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                c0.C.Seg[1] = new SegStyle { Shape = "cylinder", Tone = 'b' }; typeof(CustomizeScreen).GetMethod("Changed", fl).Invoke(c0, null);
                for (int i = 0; i < 6; i++) Frame(new Wrap(c0)); Frame(new Wrap(c0), name: "custom-changed");
            }
            {   // the body system: a split torso with new shapes, taper, details, visor and antenna, and proportions
                var fl = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                foreach (var (nm, setup) in new (string, Action<GermCustom>)[] {
                    ("body-a", c => { c.Seg[1] = new SegStyle { Shape = "trapezoid", Thick = 1.0f, Tone = 'a', Tone2 = 'c', Detail = "vents" }; c.Seg[15] = new SegStyle { Shape = "ribbed", Thick = 0.95f, Tone = 'b', Tone2 = 'a' }; c.Seg[2] = new SegStyle { Shape = "trapezoid", Thick = 1.0f, Taper = -0.3f, Tone = 'a', Tone2 = 'b' };
                        c.Seg[0] = new SegStyle { Shape = "hex", Visor = "mono", Antenna = "dish", Tone = 'b' }; c.Height = 1.1f; c.LegLen = 1.2f; c.ArmLen = 0.95f; c.Att = new(); }),
                    ("body-b", c => { c.Seg[1] = new SegStyle { Shape = "capsule", Taper = 0.5f, Tone = 'a', Detail = "stripe" }; c.Seg[15] = new SegStyle { Shape = "hex", Thick = 0.8f, Tone = 'b' }; c.Seg[0] = new SegStyle { Shape = "helmet", Visor = "dual", Antenna = "twin", Tone = 'a', Tone2 = 'b', TwoTone = true };
                        foreach (int i in new[] { 3, 6 }) c.Seg[i] = new SegStyle { Shape = "cylinder", Taper = 0.4f, Detail = "rings", Tone = 'a', Tone2 = 'b' }; foreach (int i in new[] { 5, 8 }) c.Seg[i] = new SegStyle { Shape = "claw", Tone = 'b', Tone2 = 'd' }; foreach (int i in new[] { 11, 14 }) c.Seg[i] = new SegStyle { Shape = "wedge", Tone = 'a', Tone2 = 'k' };
                        c.Height = 0.85f; c.ArmLen = 1.25f; c.LegLen = 0.85f; c.Bulk = 1.5f; c.Att = new(); }),
                })
                {
                    var cc = GermDraw.DefaultCustom(); setup(cc);
                    var cb = new CustomizeScreen(app, cc); typeof(CustomizeScreen).GetMethod("Changed", fl).Invoke(cb, null); typeof(CustomizeScreen).GetField("spin", fl).SetValue(cb, false); typeof(CustomizeScreen).GetField("tab", fl).SetValue(cb, "build");
                    foreach (var (yaw, v) in new[] { (MathF.PI + 0.35f, "front"), (MathF.PI * 0.5f, "side") })
                    {
                        typeof(CustomizeScreen).GetField("yaw", fl).SetValue(cb, yaw);
                        for (int i = 0; i < 8; i++) Frame(new Wrap(cb)); Frame(new Wrap(cb), name: $"{nm}-{v}");
                    }
                }
            }
            foreach (var style in MedDraw.ARMOR_STYLES)
            {   // every armor style, front and back
                var ca = new CustomizeScreen(app, GermDraw.DefaultCustom()); ca.C.Armor = style;
                var fl = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(CustomizeScreen).GetMethod("Changed", fl).Invoke(ca, null); typeof(CustomizeScreen).GetField("tab", fl).SetValue(ca, "armor"); typeof(CustomizeScreen).GetField("spin", fl).SetValue(ca, false);
                foreach (var (yaw, nm) in new[] { (MathF.PI, "front"), (0f, "back") })
                {
                    typeof(CustomizeScreen).GetField("yaw", fl).SetValue(ca, yaw);
                    for (int i = 0; i < 8; i++) Frame(new Wrap(ca)); Frame(new Wrap(ca), name: $"armor-{style}-{nm}");
                }
            }
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
        if (Want("fluids"))
        {   // the acid pool's surface after a splash, and germs bleeding beside it
            var world = new World(new BootConfig { Mode = "waves", Map = "bowl", Seed = 7, Custom = GermDraw.DefaultCustom() }, new Settings { res = 0, density = 1 }, World.DefaultBinds());
            var gs = new GameScreen(app, world); world.playing = true;
            for (int i = 0; i < 10; i++) Frame(gs);
            var pools = (System.Collections.IList)typeof(World).GetField("POOLS", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(world);
            Console.WriteLine($"fluids: {pools.Count} pool(s)");
            var craters = (List<Crater>)typeof(World).GetField("CRATERS", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(world);
            var crater = craters.FirstOrDefault(c => c.Acid);
            float px = crater?.X ?? 0, pz = crater?.Z ?? 0, gy = world.TerrainH(px, pz);
            world.Splash(px, pz, gy + 2, 6, 3); world.Splash(px + 5, pz + 3, gy + 2, 4, 2);
            world.DebugEnemy("squad");
            int hit = 0;
            foreach (var A in world.actors) if (A.team == "enemy" && A.P.alive && hit < 6) { A.P.x = px + 12 + hit * 1.5f; A.P.z = pz + 6; A.P.y = world.GroundY(A.P.x, A.P.z); world.DamageActor(A, 30, new Dmg { kind = "blast" }); hit++; }
            for (int i = 0; i < 40; i++) Frame(gs);
            world.SetFreeCam(true); world.FREECAM.x = px + 22; world.FREECAM.z = pz + 22; world.FREECAM.y = gy + 12; world.FREECAM.yaw = MathF.Atan2(22, 22); world.FREECAM.pitch = -0.45f; world.FREECAM.fov = 60;
            for (int i = 0; i < 6; i++) Frame(gs);
            Frame(gs, name: "fluids");
            float cxg = px + 16, czg = pz + 6, cyg = world.TerrainH(cxg, czg);
            world.FREECAM.x = cxg + 4; world.FREECAM.z = czg + 4; world.FREECAM.y = cyg + 2.2f; world.FREECAM.yaw = MathF.Atan2(4, 4); world.FREECAM.pitch = -0.35f; world.FREECAM.fov = 55;
            for (int i = 0; i < 4; i++) Frame(gs);
            Frame(gs, name: "fluids-close");
            world.SetFreeCam(false);
        }
        if (Want("fbx"))
        {   // round trip: export a hero as FBX, read it back, and load it as a model shape on a fresh hero's head and chest
            var c = new CustomizeScreen(app, GermDraw.DefaultCustom()); Frame(new Wrap(c));
            var parts = c.FbxParts(); string fdir = Path.Combine(dir, "models"); Directory.CreateDirectory(fdir); string f = Path.Combine(fdir, "roundtrip.fbx");
            Fbx.Save(f, parts);
            var (pos, idx) = Fbx.Load(f);
            Console.WriteLine($"fbx: wrote {parts.Count} parts, {parts.Sum(p => p.Pos.Count / 3)} vertices, {new FileInfo(f).Length} bytes; read back {pos.Length / 3} vertices, {idx.Length / 3} triangles");
            ModelLibrary.Load(fdir); Console.WriteLine("fbx: model shapes " + string.Join(", ", ModelLibrary.Names) + (ModelLibrary.Errors.Count > 0 ? " errors " + string.Join("; ", ModelLibrary.Errors) : ""));
        }
        if (Want("villi"))
        {   // a villi patch up close, after it has swayed a while and someone has walked through it
            var world = new World(new BootConfig { Mode = "waves", Map = "bowl", Seed = 7, Custom = GermDraw.DefaultCustom() }, new Settings { res = 0, density = 1 }, World.DefaultBinds());
            var gs = new GameScreen(app, world); world.playing = true;
            var vfo = typeof(World).GetField("VF", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(world);
            var vx = (float[])vfo.GetType().GetField("x").GetValue(vfo); var vz = (float[])vfo.GetType().GetField("z").GetValue(vfo); var vy = (float[])vfo.GetType().GetField("y").GetValue(vfo);
            int pick = vx.Length / 3; float px = vx[pick], pz = vz[pick], py = vy[pick];
            world.HUMAN.P.x = px + 1.5f; world.HUMAN.P.z = pz; world.HUMAN.P.y = py + 1; world.HUMAN.P.px = world.HUMAN.P.x; world.HUMAN.P.pz = pz;
            for (int i = 0; i < 90; i++) Frame(gs);
            world.SetFreeCam(true); world.FREECAM.x = px + 6; world.FREECAM.z = pz + 6; world.FREECAM.y = py + 2.6f; world.FREECAM.yaw = MathF.Atan2(6, 6); world.FREECAM.pitch = -0.2f; world.FREECAM.fov = 55;
            for (int i = 0; i < 6; i++) Frame(gs);
            Frame(gs, name: "villi");
            world.SetFreeCam(false);
        }
        if (Want("collide"))
        {   // the collision audit: every static mesh's surface in the band a body can reach (0.3..2.2 m above the ground under it)
            // is checked against the collision world; anything you'd phase through is listed, worst first
            foreach (var (mode, map) in new[] { ("sandbox", "random"), ("sandbox", "classic"), ("sandbox", "leviathan"), ("waves", "bowl"), ("waves", "city"), ("waves", "spires"), ("waves", "gut") })
            {
                var world = new World(new BootConfig { Mode = mode, Map = map, Seed = 12345, Custom = GermDraw.DefaultCustom() }, new Settings { res = 0, density = 1 }, World.DefaultBinds());
                var stats = new Dictionary<string, (int bad, int all, float x, float y, float z)>();
                var vfo = typeof(World).GetField("VF", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(world);
                var villiMeshes = new HashSet<object>(vfo == null ? Array.Empty<object>() : ((Array)vfo.GetType().GetField("seg").GetValue(vfo)).Cast<object>());   // foliage on purpose
                void Probe(string key, System.Numerics.Matrix4x4 M, Geometry g)
                {
                    int nv = g.VertexCount, step = Math.Max(1, nv / 60);
                    for (int i = 0; i < nv; i += step)
                    {
                        var p = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(g.Position[i * 3], g.Position[i * 3 + 1], g.Position[i * 3 + 2]), M);
                        if (MathF.Abs(p.X) > 340 || MathF.Abs(p.Z) > 340) continue;
                        float gy = world.TerrainH(p.X, p.Z), h = p.Y - gy; if (h < 0.3f || h > 2.2f) continue;
                        // pull the sample a little toward the mesh's centre: a surface point sits exactly on the edge of a matching solid
                        if (g.BoundRadius < 0) g.ComputeBounds();
                        var c = System.Numerics.Vector3.Transform(g.BoundCenter, M); var dc = c - p; float dl = dc.Length();
                        var q = dl > 1e-4f ? p + dc / dl * MathF.Min(0.15f, dl * 0.5f) : p;   // 15 cm inward: a surface point sits exactly on the edge of a matching solid
                        bool ok = world.SolidAt(q.X, q.Y, q.Z, 0.35f);
                        var e = stats.TryGetValue(key, out var v) ? v : (bad: 0, all: 0, x: 0f, y: 0f, z: 0f);
                        stats[key] = (e.bad + (ok ? 0 : 1), e.all + 1, ok ? e.x : p.X, ok ? e.y : p.Y, ok ? e.z : p.Z);
                    }
                }
                void Walk(Node n)
                {
                    if (!n.Visible) return;
                    if (n is InstancedMesh im) { if (im.Geo != null && !villiMeshes.Contains(im)) { string k = $"inst v{im.Geo.VertexCount} col{im.Mat?.Color} n{im.Count}"; for (int i = 0; i < Math.Min(im.Count, im.Capacity); i++) Probe(k, im.Matrices[i] * im.World, im.Geo); } }
                    else if (n is Mesh m && m.Geo != null && !(m.Mat?.Transparent ?? false)) Probe($"mesh v{m.Geo.VertexCount} col{m.Mat?.Color}", m.World, m.Geo);
                    foreach (var ch in n.Children) Walk(ch);
                }
                world.scene.UpdateWorld(System.Numerics.Matrix4x4.Identity);
                Walk(world.scene);
                int tb = stats.Values.Sum(v => v.bad), ta = stats.Values.Sum(v => v.all);
                Console.WriteLine($"COLLIDE {mode}/{map}: {tb}/{ta} reachable surface samples have no collision");
                foreach (var kv in stats.Where(k => k.Value.bad > 3).OrderByDescending(k => k.Value.bad).Take(14)) Console.WriteLine($"   {kv.Value.bad,5}/{kv.Value.all,-5} {kv.Key}  e.g. at {kv.Value.x:0.0},{kv.Value.y:0.0},{kv.Value.z:0.0}");
            }
        }
        if (Want("cast")) { Lineup.Run(sr, Path.Combine(dir, "cast-raw.png"), false, 18, false); }
        if (Want("heroes"))
            foreach (var k in MedDraw.PRESETS.Keys)
            {
                var c = new CustomizeScreen(app, new GermCustom { Preset = k }); c.C.Preset = k; typeof(CustomizeScreen).GetMethod("Changed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(c, null);
                { var fl2 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; typeof(CustomizeScreen).GetField("spin", fl2).SetValue(c, false); typeof(CustomizeScreen).GetField("yaw", fl2).SetValue(c, MathF.PI + 0.35f); }
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

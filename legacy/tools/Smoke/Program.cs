using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GermBall.Game;
using GermBall.Platform;

// Usage: Smoke <repo>/data [seconds-per-map]   |   Smoke <repo>/data --shots <outdir> [w h] [only]
if (args.Length > 1 && args[0] == "--geo")
{
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    string A(float[] a) => a == null ? "null" : "[" + string.Join(",", a.Select(v => v.ToString("R", inv))) + "]";
    string D(GermBall.Render.Geometry g) => "{\"p\":" + A(g.Position) + ",\"n\":" + A(g.Normal) + ",\"uv\":" + A(g.Uv) + ",\"i\":" + (g.Index == null ? "null" : "[" + string.Join(",", g.Index) + "]") + "}";
    var G = new System.Collections.Generic.Dictionary<string, GermBall.Render.Geometry>
    {
        ["box"] = GermBall.Render.Geo.Box(1, 2, 3), ["sphere"] = GermBall.Render.Geo.Sphere(1, 18, 12), ["spherePart"] = GermBall.Render.Geo.Sphere(1, 24, 16, MathF.PI * 0.25f, MathF.PI * 0.5f, MathF.PI * 0.15f, MathF.PI * 0.7f),
        ["ico0"] = GermBall.Render.Geo.Icosahedron(1, 0), ["ico1"] = GermBall.Render.Geo.Icosahedron(1, 1), ["ico2"] = GermBall.Render.Geo.Icosahedron(1, 2), ["oct"] = GermBall.Render.Geo.Octahedron(1, 0), ["dod"] = GermBall.Render.Geo.Dodecahedron(1),
        ["cone"] = GermBall.Render.Geo.Cone(1, 1, 10), ["cyl"] = GermBall.Render.Geo.Cylinder(0.5f, 1, 2, 12, 2), ["cylOpen"] = GermBall.Render.Geo.Cylinder(1, 1, 1, 8, 1, true),
        ["plane"] = GermBall.Render.Geo.Plane(2, 3, 2, 3), ["circle"] = GermBall.Render.Geo.Circle(1, 12), ["ring"] = GermBall.Render.Geo.Ring(0.5f, 1, 16), ["torus"] = GermBall.Render.Geo.Torus(1, 0.18f, 8, 24),
        ["lathe"] = GermBall.Render.Geo.Lathe(new[] { new System.Numerics.Vector2(0, 0), new System.Numerics.Vector2(1, 0.5f), new System.Numerics.Vector2(0.5f, 1) }, 8),
        ["tube"] = GermBall.Render.Geo.Tube(new[] { new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(1, 1, 0), new System.Numerics.Vector3(2, 0, 1), new System.Numerics.Vector3(3, 1, 1) }, 8, 0.2f, 5, false),
    };
    File.WriteAllText(args[1], "{" + string.Join(",", G.Select(kv => "\"" + kv.Key + "\":" + D(kv.Value))) + "}"); return 0;
}
if (args.Length > 1 && args[0] == "--gd")
{
    var sb = new System.Text.StringBuilder("{");
    bool first = true;
    foreach (var (k, sp0) in GermDraw.SPECIES)
    {
        var sp = sp0.Clone(); var o = new DrawOpts { T = 0.5f, Fdt = 1 / 60f, Lod = 2, Has = Enumerable.Repeat(true, 15).ToArray(), St = GermDraw.NewSpringState(), S = 1 };
        var ps = GermDraw.RestPose(1, 0.5f); var em = new System.Collections.Generic.List<string>();
        GermDraw.Draw(sp, ps, o, (sh, tone, x, y, z, qx, qy, qz, qw, sx, sy, sz) => em.Add(string.Join(",", new[] { "\"" + sh + "\"", "\"" + tone + "\"" }.Concat(new[] { x, y, z, qx, qy, qz, qw, sx, sy, sz }.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))))));
        sb.Append((first ? "" : ",") + "\"" + k + "\":{\"pose\":[" + string.Join(",", ps.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "],\"em\":[" + string.Join(",", em.Select(e => "[" + e + "]")) + "]}"); first = false;
    }
    File.WriteAllText(args[1], sb.Append("}").ToString()); return 0;
}
if (args.Length > 2 && args[1] == "--shots")
{
    var sv = Path.Combine(Path.GetTempPath(), "germball-smoke"); Directory.CreateDirectory(sv);
    Paths.Init(new[] { "--data", Path.GetFullPath(args[0]), "--saves", sv }); Log.Init();
    Smoke.Shots.Run(args[2], args.Length > 4 ? int.Parse(args[3]) : 960, args.Length > 4 ? int.Parse(args[4]) : 540, args.Length > 5 ? args[5] : null);
    return 0;
}
string data = args.Length > 0 ? args[0] : "data"; float secs = args.Length > 1 ? float.Parse(args[1]) : 20;
var saves = Path.Combine(Path.GetTempPath(), "germball-smoke"); Directory.CreateDirectory(saves);
Paths.Init(new[] { "--data", Path.GetFullPath(data), "--saves", saves }); Log.Init();
int fails = 0;

// ---- sound effects and music render without NaNs and stay in range
Sound.Init();
Console.WriteLine($"sfx: {Sound.Buf.Count} buffers, peak {Sound.Buf.Values.Max(b => b.Max(MathF.Abs)):0.00}");
var mus = Music.Load(Path.Combine(Paths.Data, "music"));
Console.WriteLine($"music: {mus.Songs.Count} songs");
{
    var buf = new float[512 * 2]; var sw = Stopwatch.StartNew(); int blocks = (int)(48000 * 60 / 512);
    double peak = 0, sum = 0; long n = 0; bool nan = false; int songsSeen = 0; mus.OnSong = s => songsSeen++;
    for (int i = 0; i < blocks; i++)
    {
        Array.Clear(buf); mus.Mix(buf, 512);
        foreach (var v in buf) { if (float.IsNaN(v) || float.IsInfinity(v)) nan = true; peak = Math.Max(peak, Math.Abs(v)); sum += v * v; n++; }
        if (i % (blocks / 6) == 0) mus.NextSong();
    }
    double rt = sw.Elapsed.TotalSeconds;
    Console.WriteLine($"music: 60 s rendered in {rt:0.00} s ({60 / rt:0}x real time), peak {peak:0.00}, rms {Math.Sqrt(sum / n):0.000}, songs started {songsSeen}, NaN {nan}");
    if (nan || peak > 4) { Console.WriteLine("FAIL music"); fails++; }
}

// ---- every map builds and runs
var maps = new (string mode, string map, int seed)[] { ("sandbox", "random", 12345), ("sandbox", "classic", 1), ("sandbox", "leviathan", 1), ("waves", "bowl", 7), ("waves", "city", 7), ("waves", "spires", 7), ("waves", "gut", 7), ("sandbox", "random", 777) };
foreach (var (mode, map, seed) in maps)
{
    var set = new Settings { companion = true, companion2 = true, companion3 = true, density = 1.5f };
    var sw = Stopwatch.StartNew(); World w;
    try { w = new World(new BootConfig { Mode = mode, Map = map, Seed = seed, Custom = GermDraw.DefaultCustom() }, set, World.DefaultBinds()); }
    catch (Exception e) { Console.WriteLine($"FAIL build {mode}/{map}: {e}"); fails++; continue; }
    double build = sw.Elapsed.TotalSeconds;
    w.playing = true; int frames = (int)(secs * 60); string phase = "run";
    var seen = new System.Collections.Generic.HashSet<Actor>(); int heroDeaths = 0; bool wasAlive = true; int maxDebris = 0, partsLost = 0;
    try
    {
        sw.Restart();
        for (int f = 0; f < frames; f++)
        {
            // drive the hero: run forward, jump now and then, fire, roll, grab
            Input.KeyEvent("KeyW", (f / 90) % 3 != 2); Input.KeyEvent("Space", f % 70 < 3); Input.KeyEvent("Mouse0", (f / 40) % 2 == 0);
            Input.KeyEvent("KeyC", f % 300 > 280); Input.KeyEvent("KeyE", f % 200 > 190); Input.KeyEvent("KeyG", f % 500 == 250);
            if (f % 120 == 60) Input.KeyEvent("Digit" + (1 + (f / 120) % 3), true);
            if (f == frames / 4) { phase = "debug spawns"; foreach (var k in new[] { "squad", "mech", "boss", "dropship", "chairman", "brute", "assassin" }) w.DebugEnemy(k); foreach (var v in new[] { "bike", "rover", "chair", "mecha" }) w.DebugVehicle(v); foreach (var o in World.DEBUG_OBJECTS) w.DebugObject(o.key); phase = "run"; }
            if (f == frames / 2) { w.DebugHeal(); w.BackToBase(); }
            w.cam.yaw += 0.01f;
            w.Frame(1 / 60f, 16 / 9f);
            Input.EndFrame();
            foreach (var a in w.actors) if (a.team == "enemy") seen.Add(a);
            if (wasAlive && !w.HUMAN.P.alive) heroDeaths++; wasAlive = w.HUMAN.P.alive;
            maxDebris = Math.Max(maxDebris, w.debris.Count);
        }
        double run = sw.Elapsed.TotalSeconds;
        int alive = w.actors.Count(a => a.P.alive), enemies = w.actors.Count(a => a.team == "enemy");
        int gone = seen.Count(a => !w.actors.Contains(a) || !a.P.alive), hurt = seen.Count(a => a.has.Any(h => !h));
        Console.WriteLine($"   enemies seen {seen.Count}, dead/removed {gone}, missing parts {hurt}, hero deaths {heroDeaths}, max debris {maxDebris}, kills {w.WAVES.kills}, score {w.WAVES.score}");
        Console.WriteLine($"{mode}/{map}: built in {build:0.00} s, {frames} frames in {run:0.00} s ({frames / run:0} fps sim+pose), actors {w.actors.Count} ({enemies} enemy, {alive} alive), bodies {w.bodies.Count}, shots {w.shots.Count}, hero shield {w.HUMAN.P.shield:0}, wave {w.WAVES.n}");
    }
    catch (Exception e) { Console.WriteLine($"FAIL {mode}/{map} during {phase} at t={w.clockT:0.00}: {e}"); fails++; }
    foreach (var k in new[] { "KeyW", "Space", "Mouse0", "KeyC", "KeyE", "KeyG" }) Input.KeyEvent(k, false);
}
Console.WriteLine(fails == 0 ? "SMOKE OK" : $"SMOKE FAILED ({fails})");
return fails == 0 ? 0 : 1;

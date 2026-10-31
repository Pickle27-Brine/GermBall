using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GermBall.Game;
using GermBall.Platform;

// Usage: Smoke <repo>/data [seconds-per-map]
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

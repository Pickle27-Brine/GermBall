using System;
using System.Diagnostics;
using System.IO;
using GermBall.Content;
using GermBall.Engine;
using GermBall.Sim;

namespace GermBall.Win;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Germ Ball runs on Windows 10 or 11."); return 1; }
        try { Run(args); return 0; }
        catch (Exception e)
        {
            var log = Path.Combine(AppContext.BaseDirectory, "crash.txt");
            try { File.WriteAllText(log, e.ToString()); } catch { }
            Win32.MessageBoxW(IntPtr.Zero, "Germ Ball hit an error and has to close.\n\n" + e.Message + "\n\nDetails: " + log, "Germ Ball " + Game.Version, 0x10);
            return 2;
        }
    }

    static void Run(string[] args)
    {
        Win32.timeBeginPeriod(1);
        var window = new Window("Germ Ball " + Game.Version, 1600, 900);
        using var renderer = new Renderer(window) { VSync = Array.IndexOf(args, "--novsync") < 0 };
        // content: entities and maps from the content folder next to the exe (--content to point elsewhere);
        // the map is --map <name or path>, else "debug", else the built-in debug level
        string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var store = ContentStore.Find(Arg("--content"));
        var map = store.Map(Arg("--map") ?? "debug");
        var level = map != null ? Level.FromMap(map) : Level.BuildDebug();
        // --spawn <entity>: put one in front of the player (the Entity Editor's "Test in game")
        var spawnName = Arg("--spawn");
        if (spawnName != null) level.Germ(level.Spawn + MathX.Forward(level.SpawnYaw, 0) * 6, level.SpawnYaw / MathX.Deg + 180, spawnName);
        // --hero <entity>: play as another entity
        var game = new Game(level, store, Arg("--hero") ?? "hero");
        // 1.0's music, from content/music (--nomusic to start without it)
        using var audio = new AudioOut();
        var music = MusicLoader.Start(audio, store, Array.IndexOf(args, "--nomusic") < 0);
        game.Music = music;
        var dl = new DrawList();
        var clock = Stopwatch.StartNew();
        double last = 0, fpsT = 0; int frames = 0;

        while (window.Pump())
        {
            double now = clock.Elapsed.TotalSeconds; float dt = (float)Math.Min(0.1, now - last); last = now;
            if (window.Resized) { renderer.Resize(window.Width, window.Height); window.Resized = false; }

            // the mouse is captured for look while playing; Esc (pause) or losing focus frees it, a click takes it back
            bool wantCapture = window.Focused && !game.Paused, justCaptured = false;
            if (!wantCapture) Input.Capture(window, false);
            else if (!Input.Captured && Input.Clicked) { Input.Capture(window, true); justCaptured = true; }

            var input = Input.Read(dt, Input.Captured, now);
            if (justCaptured) { input.Pressed &= ~Btn.Fire; input.Held &= ~Btn.Fire; }   // the click that grabs the mouse doesn't shoot
            game.Frame(dt, input);
            if (game.Paused) Input.Capture(window, false);

            game.Draw(dl, renderer.Width, renderer.Height);
            if (!Input.Captured && !game.Paused && window.Focused)
                dl.Overlay.TextCentered(renderer.Width / 2f, renderer.Height * 0.6f, "Click to play", Math.Max(1, MathF.Round(renderer.Height / 360f)), new System.Numerics.Vector4(1, 1, 1, 0.9f));
            renderer.Render(dl);
            renderer.Present();
            Input.EndFrame();

            frames++; fpsT += dt;
            if (fpsT >= 0.5) { game.Fps = (float)(frames / fpsT); frames = 0; fpsT = 0; }
        }
        Input.Capture(window, false);
        Win32.timeEndPeriod(1);
    }
}

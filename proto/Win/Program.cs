using System;
using System.Diagnostics;
using System.IO;
using GermBall.Proto.Engine;
using GermBall.Proto.Sim;

namespace GermBall.Proto.Win;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("The Germ Ball prototype runs on Windows 10 or 11."); return 1; }
        try { Run(args); return 0; }
        catch (Exception e)
        {
            var log = Path.Combine(AppContext.BaseDirectory, "crash.txt");
            try { File.WriteAllText(log, e.ToString()); } catch { }
            Win32.MessageBoxW(IntPtr.Zero, "The prototype hit an error and has to close.\n\n" + e.Message + "\n\nDetails: " + log, "Germ Ball Prototype", 0x10);
            return 2;
        }
    }

    static void Run(string[] args)
    {
        Win32.timeBeginPeriod(1);
        var window = new Window("Germ Ball Prototype", 1600, 900);
        using var renderer = new Renderer(window) { VSync = Array.IndexOf(args, "--novsync") < 0 };
        var game = new Game(Level.BuildDebug());
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

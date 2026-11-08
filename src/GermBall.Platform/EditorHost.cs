using System;
using System.Diagnostics;
using System.IO;
using GermBall.Engine;

namespace GermBall.Win;

/// <summary>Runs an editor in a window: pumps messages, feeds it the mouse and keyboard, renders its draw list.</summary>
public static class EditorHost
{
    public static void Run(string title, Action<UiInput, float, int, int, DrawList> frame, Func<bool> confirmClose = null)
    {
        Win32.timeBeginPeriod(1);
        var window = new Window(title, 1600, 900);
        using var renderer = new Renderer(window);
        var dl = new DrawList(); var ui = new UiInput();
        var clock = Stopwatch.StartNew(); double last = 0;
        while (window.Pump())
        {
            double now = clock.Elapsed.TotalSeconds; float dt = (float)Math.Min(0.1, now - last); last = now;
            if (window.Resized) { renderer.Resize(window.Width, window.Height); window.Resized = false; }
            Input.ReadUi(ui);
            frame(ui, dt, renderer.Width, renderer.Height, dl);
            renderer.Render(dl);
            renderer.Present();
            Input.EndFrame();
        }
        Win32.timeEndPeriod(1);
    }

    /// <summary>Starts GermBall.exe from the same folder as this program (where the release zip puts it).</summary>
    public static bool LaunchGame(string args, out string error)
    {
        error = null;
        var exe = Path.Combine(AppContext.BaseDirectory, "GermBall.exe");
        if (!File.Exists(exe)) { error = "GermBall.exe isn't next to this editor"; return false; }
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false });
            return true;
        }
        catch (Exception e) { error = e.Message; return false; }
    }

    public static int Crash(string app, Exception e)
    {
        var log = Path.Combine(AppContext.BaseDirectory, app + "-crash.txt");
        try { File.WriteAllText(log, e.ToString()); } catch { }
        Win32.MessageBoxW(IntPtr.Zero, $"{app} hit an error and has to close.\n\n{e.Message}\n\nDetails: {log}", app, 0x10);
        return 2;
    }
}

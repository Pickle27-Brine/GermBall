using System;
using System.Diagnostics;
using GermBall.Platform;
using GermBall.Render;

namespace GermBall;

/// <summary>The window, the device, the clock and the screens. One fixed 120 Hz simulation step, rendering as fast as the display allows.</summary>
public sealed class App : IDisposable
{
    public static App Current { get; private set; }
    public readonly Window Window;
    public readonly Renderer Renderer;
    public readonly Audio Audio;
    public readonly Draw2D UI = new();
    public IScreen Screen;
    readonly Stopwatch clock = Stopwatch.StartNew();
    public double Now => clock.Elapsed.TotalSeconds;
    public float Fps { get; private set; }

    public App(string[] args)
    {
        Current = this;
        Win32.timeBeginPeriod(1);
        Window = new Window("Germ Ball", 1600, 900);
        Renderer = new Renderer(Window);
        Audio = new Audio();
        UI.Display = new UiFont("Bahnschrift SemiCondensed", 16, 700);
        UI.DisplayBig = new UiFont("Bahnschrift SemiCondensed", 32, 800);
        UI.DisplayHuge = new UiFont("Bahnschrift Condensed", 112, 800);
        UI.Mono = new UiFont("Consolas", 14);
        UI.MonoSmall = new UiFont("Consolas", 12);
        Screen = Game.Boot.CreateTitle(this);
    }

    public void Run()
    {
        double last = Now, fpsT = 0; int frames = 0;
        while (Window.Pump())
        {
            double now = Now; float dt = (float)Math.Min(0.1, now - last); last = now;
            if (Window.Resized) { Renderer.Resize(Window.Width, Window.Height); Window.Resized = false; }
            Gamepad.Poll(now); Gamepad.Update(dt);
            if (Input.Pressed("F12")) Screenshot.Take(Renderer);
            Screen.Update(dt);
            Screen.Render(dt);
            UI.Begin(); Screen.DrawUI(UI, dt); UI.End(Renderer);
            Renderer.Present();
            Input.EndFrame();
            frames++; fpsT += dt; if (fpsT >= 0.5) { Fps = (float)(frames / fpsT); frames = 0; fpsT = 0; }
        }
    }

    public void Dispose()
    {
        Input.Lock(Window, false);
        Audio?.Dispose(); Renderer?.Dispose();
        Win32.timeEndPeriod(1);
    }
}

public interface IScreen
{
    void Update(float dt);
    void Render(float dt);
    void DrawUI(Draw2D ui, float dt);
}

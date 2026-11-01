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
    public readonly IRenderer Renderer;
    public readonly Audio Audio;
    public readonly Draw2D UI = new();
    public IScreen Screen;
    readonly Stopwatch clock = Stopwatch.StartNew();
    public double Now => clock.Elapsed.TotalSeconds;
    public float Fps { get; private set; }
    /// <summary>UI layout size in logical units: the UI is laid out for a 900-unit-tall screen and scaled to the window.</summary>
    public float UiW => Renderer.Width / UI.Scale;
    public float UiH => Renderer.Height / UI.Scale;
    public void UpdateUiScale() => UI.Scale = Math.Clamp(Renderer.Height / 900f, 0.55f, 4f);

    public App(string[] args) : this(new Window("Germ Ball", 1600, 900), null) { }

    /// <summary>Builds the app around a window and renderer; tools pass a headless window and a software renderer.</summary>
    public App(Window window, IRenderer renderer)
    {
        Current = this;
        if (!window.IsHeadless) Win32.timeBeginPeriod(1);
        Window = window;
        Renderer = renderer ?? new Renderer(Window);
        Audio = new Audio();
        UI.Display = UiFont.Create("Bahnschrift SemiCondensed", 16, 700);
        UI.DisplayBig = UiFont.Create("Bahnschrift SemiCondensed", 32, 800);
        UI.DisplayHuge = UiFont.Create("Bahnschrift Condensed", 112, 800);
        UI.Mono = UiFont.Create("Consolas", 14);
        UI.MonoSmall = UiFont.Create("Consolas", 12);
        UI.DisplaySmall = UiFont.Create("Bahnschrift SemiCondensed", 12, 700);
        UI.DisplayMid = UiFont.Create("Bahnschrift SemiCondensed", 20, 800);
        UI.DisplayNum = UiFont.Create("Bahnschrift SemiCondensed", 44, 800);
        UI.DisplayTitle = UiFont.Create("Bahnschrift Condensed", 60, 800);
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
            UpdateUiScale(); UI.Begin(); Screen.DrawUI(UI, dt); UI.End(Renderer);
            Screenshot.Flush(Renderer);
            Renderer.Present();
            Input.EndFrame();
            frames++; fpsT += dt; if (fpsT >= 0.5) { Fps = (float)(frames / fpsT); frames = 0; fpsT = 0; }
        }
    }

    public void Dispose()
    {
        Input.Lock(Window, false);
        Audio?.Dispose(); (Renderer as IDisposable)?.Dispose();
        if (!Window.IsHeadless) Win32.timeEndPeriod(1);
    }
}

public interface IScreen
{
    void Update(float dt);
    void Render(float dt);
    void DrawUI(Draw2D ui, float dt);
}

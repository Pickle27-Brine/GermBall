using System;
using System.IO;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;
using GermBall.Tools.EntityEditor;

namespace GermBall.Tests;

/// <summary>Drives the editors headlessly: scripted mouse and keyboard, frames rendered through the software rasteriser.</summary>
public sealed class EditorDriver
{
    public readonly UiInput In = new();
    public readonly DrawList Dl = new();
    public readonly int W, H;
    readonly Action<UiInput, float, DrawList> frame;
    public EditorDriver(int w, int h, Action<UiInput, float, DrawList> frame) { W = w; H = h; this.frame = frame; }

    public void Step(int n = 1)
    {
        for (int i = 0; i < n; i++) { frame(In, 1 / 60f, Dl); In.ClearFrame(); }
    }
    public void MoveTo(float x, float y) { In.MouseDX = x - In.MouseX; In.MouseDY = y - In.MouseY; In.MouseX = x; In.MouseY = y; }
    public void Click(float x, float y, int button = 0)
    {
        MoveTo(x, y); Step();
        In.Down[button] = true; In.Pressed[button] = true; Step();
        In.Down[button] = false; In.Released[button] = true; Step();
    }
    /// <summary>Press and release within one frame (a fast click on a real mouse).</summary>
    public void QuickClick(float x, float y)
    {
        MoveTo(x, y); Step();
        In.Pressed[0] = true; In.Released[0] = true; In.Down[0] = false; Step();
    }
    public void Drag(float x0, float y0, float x1, float y1, int steps = 8, int button = 0)
    {
        MoveTo(x0, y0); Step();
        In.Down[button] = true; In.Pressed[button] = true; Step();
        for (int i = 1; i <= steps; i++) { MoveTo(x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps); Step(); }
        In.Down[button] = false; In.Released[button] = true; Step();
    }
    public void Type(string s) { In.Typed = s; Step(); }
    public void Key(int vk, bool ctrl = false) { In.Ctrl = ctrl; In.KeysPressed.Add(vk); Step(); In.Ctrl = false; }
    public void Shot(string path)
    {
        var sr = new SoftRenderer(W, H); sr.Render(Dl); sr.SavePng(path);
        Console.WriteLine("  shot  " + path);
    }
}

/// <summary>Clicking widgets by their label, through the real interface.</summary>
public static class GuiDriving
{
    public static Vector4 Find(GermBall.Tools.Ui.Gui ui, EditorDriver d, string label)
    {
        ui.RecordRects = true; d.Step();
        if (!ui.Rects.TryGetValue(label, out var r)) throw new Exception($"no widget \"{label}\" on screen");
        // scroll its panel until it's in view
        for (int i = 0; i < 60 && (r.Y + r.W > d.H - 50 || r.Y < 70); i++)
        {
            d.MoveTo(r.X + 4, d.H / 2f); d.In.Wheel = r.Y < 70 ? 1 : -1; d.Step();
            ui.Rects.TryGetValue(label, out r);
        }
        return r;
    }
    public static void ClickLabel(GermBall.Tools.Ui.Gui ui, EditorDriver d, string label)
    {
        var r = Find(ui, d, label);
        d.Click(r.X + r.Z / 2, r.Y + r.W / 2);
    }
    /// <summary>Opens a combo by its label and picks an option (index into the list, after the "none" row if it has one).</summary>
    public static void PickCombo(GermBall.Tools.Ui.Gui ui, EditorDriver d, string label, int option, bool hasNone = false)
    {
        var r = Find(ui, d, label);
        // scroll it into the top half, so its list opens downward
        for (int i = 0; i < 40 && r.Y > d.H * 0.5f; i++)
        {
            float y0 = r.Y;
            d.MoveTo(r.X + 4, d.H / 2f); d.In.Wheel = -1; d.Step();
            ui.Rects.TryGetValue(label, out r);
            if (r.Y >= y0) break;
        }
        d.Click(r.X + r.Z / 2, r.Y + r.W / 2);
        d.Step();
        // the list opens below, or above when there's no room below
        var list = ui.OpenListRect;
        float y = list.W > 0 ? list.Y + (option + (hasNone ? 1 : 0) + 0.5f) * ui.RowH : r.Y + r.W + 1 + (option + (hasNone ? 1 : 0) + 0.5f) * ui.RowH;
        d.Click(r.X + r.Z / 2, y);
    }
    public static void TypeInto(GermBall.Tools.Ui.Gui ui, EditorDriver d, string label, string text)
    {
        var r = Find(ui, d, label);
        d.Click(r.X + r.Z / 2, r.Y + r.W / 2);
        for (int i = 0; i < 40; i++) d.Key(GermBall.Engine.Keys.Backspace);
        d.Type(text);
        d.Key(GermBall.Engine.Keys.Enter);
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Platform;
using GermBall.Render;

namespace GermBall.UI;

/// <summary>Colours from the original style sheet.</summary>
public static class Pal
{
    public static readonly Vector4 Hud = Draw2D.Col(0x7affef), HudDim = Draw2D.Col(0x7affef, 0.35f), HudFaint = Draw2D.Col(0x7affef, 0.12f), Warn = Draw2D.Col(0xffb347), Hot = Draw2D.Col(0xff4fd8),
        Ink = Draw2D.Col(0xe9f4fa), Panel = new(26 / 255f, 12 / 255f, 46 / 255f, 0.86f), Text = Draw2D.Col(0xb8cbd6), Text2 = Draw2D.Col(0xd4e2ea), Mute = Draw2D.Col(0x8ea3b0), Mute2 = Draw2D.Col(0x9fb3bf),
        Black = new(0, 0, 0, 1), White = new(1, 1, 1, 1), Btn = Draw2D.Col(0x8fe3ff, 0.12f), BtnHover = Draw2D.Col(0x8fe3ff, 0.24f), Chip = Draw2D.Col(0x8fe3ff, 0.05f), ChipOn = Draw2D.Col(0xffb347, 0.12f),
        Danger = Draw2D.Col(0xff8a6a);
    public static Vector4 A(Vector4 c, float a) => new(c.X, c.Y, c.Z, c.W * a);
}

/// <summary>
/// A small immediate-mode GUI for the menus: every widget is drawn and hit-tested in the same call. Mouse hover and click
/// work as in the browser; a controller or the arrow keys move a focus highlight between widgets (nearest in the pushed
/// direction, as the original's padNav did), left/right nudge sliders and A or Enter presses.
/// </summary>
public sealed class Gui
{
    public Draw2D D; public float Mx, My; bool click, clickConsumed; public bool MouseDown;
    readonly List<(string id, Rect r)> cur = new(), prev = new();
    public string Focus; string dragId; public bool NavUsed;
    float navT; string navDir = ""; int navHeld;
    public float ScrollY; Rect? clip;
    bool activate; int nudge;
    public bool Back, TabNext, TabPrev;   // B / Esc, RB / LB this frame
    bool prevA, prevB, prevLb, prevRb, prevStart; public bool StartHit;

    /// <summary>Treat the pad's buttons as already held, so a press that opened this menu isn't read again as a menu press.</summary>
    public void SyncPad()
    {
        var p = Gamepad.First;
        prevA = p != null && p.B(0); prevB = p != null && p.B(1); prevLb = p != null && p.B(4); prevRb = p != null && p.B(5); prevStart = p != null && p.B(9);
    }

    public float T;
    public void Begin(Draw2D d, float dt)
    {
        T += dt;
        D = d; prev.Clear(); prev.AddRange(cur); cur.Clear();
        Mx = Input.MouseX / d.Scale; My = Input.MouseY / d.Scale; click = Input.Pressed("Mouse0"); MouseDown = Input.Down("Mouse0"); clickConsumed = false;
        if (!MouseDown) dragId = null;
        if (Input.MouseDX != 0 || Input.MouseDY != 0 || click) NavUsed = false;
        // directional navigation from the pad's stick and D-pad, or the arrow keys
        var p = Gamepad.First; string dir = "";
        if (p != null)
        {   // the stick has to be pushed well over (and let back past a lower mark) to count, the dominant axis wins: no drifting
            // or diagonal double-steps; the D-pad is exact
            float lx = p.Axes[0], ly = p.Axes[1], on = navDir == "" ? 0.65f : 0.35f;
            bool vert = MathF.Abs(ly) >= MathF.Abs(lx);
            if (p.B(12)) dir = "up"; else if (p.B(13)) dir = "down"; else if (p.B(14)) dir = "left"; else if (p.B(15)) dir = "right";
            else if (vert && ly < -on) dir = "up"; else if (vert && ly > on) dir = "down"; else if (!vert && lx < -on) dir = "left"; else if (!vert && lx > on) dir = "right";
        }
        bool a = p != null && p.B(0), b = p != null && p.B(1), lb = p != null && p.B(4), rb = p != null && p.B(5), st = p != null && p.B(9);
        activate = (a && !prevA) || Input.Pressed("Enter") || Input.Pressed("NumpadEnter");
        Back = (b && !prevB) || Input.Pressed("Escape"); TabNext = rb && !prevRb; TabPrev = lb && !prevLb; StartHit = st && !prevStart;
        prevA = a; prevB = b; prevLb = lb; prevRb = rb; prevStart = st;
        if (Input.Pressed("ArrowUp")) Nav("up"); else if (Input.Pressed("ArrowDown")) Nav("down");
        nudge = 0;
        if (Input.Pressed("ArrowLeft")) { if (!Nudge(-1)) Nav("left"); } else if (Input.Pressed("ArrowRight")) { if (!Nudge(1)) Nav("right"); }
        if (Input.Pressed("Tab")) Nav(Input.Down("ShiftLeft") ? "up" : "down");
        if (dir != "")
        {
            // one step on the push, then a pause before it repeats, then a steady (not frantic) repeat; it speeds up a little when held a long time
            if (dir != navDir || navT <= 0) { if ((dir == "left" || dir == "right") && Nudge(dir == "left" ? -1 : 1)) { } else Nav(dir); navHeld = dir == navDir ? navHeld + 1 : 0; navT = dir == navDir ? (navHeld > 6 ? 0.13f : 0.2f) : 0.45f; }
            navT -= dt;
        }
        else navT = 0;
        navDir = dir;
        if (activate && Focus == null && prev.Count > 0) { Focus = prev[0].id; activate = false; NavUsed = true; }
    }
    bool Nudge(int d) { if (Focus != null && Focus.StartsWith("sl:")) { nudge = d; NavUsed = true; return true; } return false; }

    /// <summary>Moves focus to the nearest widget in a direction (first widget when nothing is focused).</summary>
    public void Nav(string dir)
    {
        NavUsed = true;
        if (prev.Count == 0) return;
        int fi = prev.FindIndex(e => e.id == Focus);
        if (fi < 0) { Focus = prev[0].id; return; }
        var f = prev[fi].r; float fx = f.X + f.W / 2, fy = f.Y + f.H / 2; string best = null; float bd = float.MaxValue;
        foreach (var (id, r) in prev)
        {
            if (id == Focus) continue;
            float cx = r.X + r.W / 2, cy = r.Y + r.H / 2, dx = cx - fx, dy = cy - fy;
            bool ok = dir switch { "up" => dy < -2, "down" => dy > 2, "left" => dx < -2, _ => dx > 2 };
            if (!ok) continue;
            float main = dir == "up" || dir == "down" ? MathF.Abs(dy) : MathF.Abs(dx), side = dir == "up" || dir == "down" ? MathF.Abs(dx) : MathF.Abs(dy);
            float d = main + side * 2.5f; if (d < bd) { bd = d; best = id; }
        }
        if (best != null) Focus = best;
    }
    public void FocusFirst() { if (prev.Count > 0) Focus = prev[0].id; }
    public bool IsFocused(string id) => Focus == id && NavUsed;
    public Rect? FocusRect { get { foreach (var e in prev) if (e.id == Focus) return e.r; return null; } }

    public void PushClip(float x, float y, float w, float h) { D.PushClip(x, y, w, h); clip = new Rect(x, y, w, h); }
    public void PopClip() { D.PopClip(); clip = null; }
    bool InClip(float x, float y) => clip is not Rect c || (x >= c.X && y >= c.Y && x < c.X + c.W && y < c.Y + c.H);

    public bool Hover(float x, float y, float w, float h) => Mx >= x && My >= y && Mx < x + w && My < y + h && InClip(Mx, My);
    /// <summary>Registers a focusable rectangle and reports whether it was pressed this frame (mouse or A/Enter).</summary>
    public bool Hit(string id, float x, float y, float w, float h)
    {
        bool visible = clip is not Rect c || (y + h > c.Y && y < c.Y + c.H);
        if (visible) cur.Add((id, new Rect(x, y, w, h)));
        bool hov = Hover(x, y, w, h);
        if (hov && click && !clickConsumed) { clickConsumed = true; Focus = id; return true; }
        if (Focus == id && activate) { activate = false; return true; }
        return false;
    }
    void FocusRing(string id, float x, float y, float w, float h) { if (IsFocused(id)) D.Frame(x - 3, y - 3, w + 6, h + 6, Pal.White, 2); }

    /// <summary>A big call-to-action button (Deploy / Generate / Start waves).</summary>
    /// <summary>A main button: a cut-corner plate with an accent spine down its left edge, a light sweep across it on
    /// hover, and the label tracked out. It glows when focused or pressed.</summary>
    public bool Big(string id, float x, float y, float w, float h, string label, Vector4? col = null, Vector4? bg = null)
    {
        var c = col ?? Pal.Hud; bool hov = Hover(x, y, w, h), hit = Hit(id, x, y, w, h), foc = Focus == id && NavUsed, hot = hov || foc;
        float cut = MathF.Min(12, h * 0.32f);
        var fill = bg ?? (hot ? Pal.A(Pal.BtnHover, 1) : Pal.Btn);
        if (bg.HasValue && hot) fill = new Vector4(MathF.Min(1, fill.X * 1.15f), MathF.Min(1, fill.Y * 1.15f), MathF.Min(1, fill.Z * 1.15f), MathF.Min(1, fill.W + 0.15f));
        CutPanel(x, y, w, h, cut, fill, hot ? c : Pal.A(c, 0.75f));
        D.Rect(x + 3, y + cut * 0.6f, 3, h - cut * 1.2f, c);                                         // the spine
        if (hot)
        {   // a soft band of light sweeping across
            float sw = (T * 0.9f % 1.6f) - 0.3f, bx = x + w * sw;
            for (int k = 0; k < 6; k++) { float xx = bx + k * 7, ww = 7; if (xx > x + 8 && xx + ww < x + w - cut) D.Rect(xx, y + 2, ww, h - 4, Pal.A(c, 0.05f * (k < 3 ? k + 1 : 6 - k))); }
        }
        if (MouseDown && hov) D.Rect(x + 6, y + h - 3, w - 12 - cut, 2, c);                         // pressed: a lit base line
        var f = h >= 36 ? D.DisplayMid : D.Display;
        string L = label.ToUpperInvariant(); float sp = h >= 36 ? 4 : 2.5f;
        while (sp > 0.5f && f.Measure(L, sp) > w - 28) sp -= 0.5f;                                   // never let a label run past its button
        D.Text(L, x + w / 2 + 2, y + (h - f.LineHeight) / 2, f, c, "center", sp);
        FocusRing(id, x, y, w, h); return hit;
    }
    /// <summary>A small chip button; on = pressed state (aria-pressed).</summary>
    public bool Chip(string id, float x, float y, string label, bool on = false, Vector4? col = null, float? width = null)
    {
        var f = D.MonoSmall; float w = width ?? f.Measure(label) + 18, h = 24; bool hov = Hover(x, y, w, h), hit = Hit(id, x, y, w, h);
        D.Rect(x, y, w, h, on ? Pal.ChipOn : hov ? Pal.A(Pal.BtnHover, 0.6f) : Pal.Chip); D.Frame(x, y, w, h, on ? Pal.Warn : col.HasValue ? Pal.A(col.Value, 0.6f) : Pal.HudDim);
        D.Text(label, x + w / 2, y + (h - f.LineHeight) / 2, f, on ? Pal.Warn : col ?? Pal.Text2, "center");
        FocusRing(id, x, y, w, h); lastW = w; return hit;
    }
    public float lastW;
    /// <summary>A row of chips laid out with wrapping; returns the index clicked or -1. Height used comes back in h.</summary>
    public int Chips(string id, float x, float y, float maxW, IList<string> labels, Func<int, bool> on, out float h, float gap = 5)
    {
        float cx = x, cy = y; int hit = -1; var f = D.MonoSmall;
        for (int i = 0; i < labels.Count; i++)
        {
            float w = f.Measure(labels[i]) + 18; if (cx + w > x + maxW && cx > x) { cx = x; cy += 24 + gap; }
            if (Chip(id + ":" + i, cx, cy, labels[i], on(i))) hit = i;
            cx += w + gap;
        }
        h = cy + 24 - y; return hit;
    }
    /// <summary>A segmented choice (the settings' Off/Low/High groups). Returns the index picked or -1.</summary>
    public int Seg(string id, float x, float y, IList<string> labels, int selected)
    {
        var f = D.MonoSmall; float cx = x; int hit = -1;
        for (int i = 0; i < labels.Count; i++)
        {
            float w = f.Measure(labels[i]) + 16, h = 24; string bid = id + ":" + i; bool on = i == selected, hov = Hover(cx, y, w, h);
            if (Hit(bid, cx, y, w, h)) hit = i;
            D.Rect(cx, y, w, h, on ? Pal.ChipOn : hov ? Pal.A(Pal.BtnHover, 0.6f) : Pal.Chip); D.Frame(cx, y, w, h, on ? Pal.Warn : Pal.HudDim);
            D.Text(labels[i], cx + w / 2, y + (h - f.LineHeight) / 2, f, on ? Pal.Warn : Pal.Text2, "center");
            FocusRing(bid, cx, y, w, h); cx += w - 1;
        }
        return hit;
    }
    /// <summary>A slider. Returns the (possibly changed) value; changed is set when it moved.</summary>
    public float Slider(string id, float x, float y, float w, float v, float min, float max, float step, out bool changed, Vector4? accent = null)
    {
        string sid = "sl:" + id; float h = 20; changed = false;
        bool hov = Hover(x, y - 2, w, h + 4);
        cur.Add((sid, new Rect(x, y, w, h)));
        if (hov && click && !clickConsumed) { clickConsumed = true; dragId = sid; Focus = sid; }
        float nv = v;
        if (dragId == sid && MouseDown) nv = min + Math.Clamp((Mx - x) / w, 0, 1) * (max - min);
        if (Focus == sid && nudge != 0) { nv = v + nudge * step; nudge = 0; }
        if (Focus == sid && Gamepad.First is { } gp) { }   // stick nudges arrive through Nudge()
        if (step > 0) nv = MathF.Round((nv - min) / step) * step + min;
        nv = Math.Clamp(nv, min, max);
        if (MathF.Abs(nv - v) > 1e-6f) changed = true;
        var c = accent ?? Pal.Hud; float k = (nv - min) / (max - min);
        D.Rect(x, y + h / 2 - 2, w, 4, Pal.HudFaint); D.Rect(x, y + h / 2 - 2, w * k, 4, Pal.A(c, 0.8f));
        D.Rect(x + w * k - 5, y + 2, 10, h - 4, c);
        FocusRing(sid, x, y, w, h);
        return nv;
    }
    /// <summary>A tab button underlined when selected.</summary>
    public bool Tab(string id, float x, float y, string label, bool on, out float w)
    {
        var f = D.Display; w = f.Measure(label.ToUpperInvariant(), 2.5f) + 20; float h = 30; bool hov = Hover(x, y, w, h), hit = Hit(id, x, y, w, h);
        D.Text(label.ToUpperInvariant(), x + 10, y + 6, f, on ? Pal.Hud : hov ? Pal.Text : Pal.Mute2, "left", 2.5f);
        if (on) { D.Rect(x, y + h - 2, w, 2, Pal.Hud); D.Tri(new Vector2(x + w / 2 - 5, y + h - 2), new Vector2(x + w / 2 + 5, y + h - 2), new Vector2(x + w / 2, y + h - 7), Pal.Hud); }
        else if (hov) D.Rect(x + 6, y + h - 2, w - 12, 1, Pal.HudDim);
        FocusRing(id, x, y, w, h); return hit;
    }
    /// <summary>A panel with the original's clipped corners (polygon clip-path), drawn as a fill plus border.</summary>
    public void CutPanel(float x, float y, float w, float h, float cut, Vector4 fill, Vector4 border)
    {
        var a = new Vector2(x, y); var b = new Vector2(x + w - cut, y); var c = new Vector2(x + w, y + cut); var d = new Vector2(x + w, y + h); var e = new Vector2(x + cut, y + h); var f = new Vector2(x, y + h - cut);
        var m = new Vector2(x + w / 2, y + h / 2);
        D.Tri(m, a, b, fill); D.Tri(m, b, c, fill); D.Tri(m, c, d, fill); D.Tri(m, d, e, fill); D.Tri(m, e, f, fill); D.Tri(m, f, a, fill);
        // a faint sheen at the top, fading down
        for (int k = 0; k < 6; k++) { float yy = y + 1 + k * MathF.Min(6, h / 14), hh = MathF.Min(6, h / 14); if (yy + hh > y + h - cut) break; D.Rect(x + 1, yy, w - cut - 2, hh, new Vector4(1, 1, 1, 0.035f * (6 - k) / 6)); }
        D.Line(a, b, border); D.Line(b, c, border); D.Line(c, d, border); D.Line(d, e, border); D.Line(e, f, border); D.Line(f, a, border);
        if (h > 60 && w > 120)
        {   // big panels: an inset hairline and bright corner brackets
            var hl = Pal.A(border, 0.35f); float i = 4;
            D.Line(new Vector2(x + i, y + i), new Vector2(x + w - cut - i * 0.6f, y + i), hl); D.Line(new Vector2(x + i, y + i), new Vector2(x + i, y + h - cut - i * 0.6f), hl);
            var br = Pal.A(border, 1.6f); float bl = MathF.Min(22, w * 0.1f);
            D.Rect(x - 1, y - 1, bl, 2, br); D.Rect(x - 1, y - 1, 2, bl, br);
            D.Rect(x + w - bl + 1, y + h - 1, bl, 2, br); D.Rect(x + w - 1, y + h - bl + 1, 2, bl, br);
        }
    }
    /// <summary>Scrolls a clipped panel with the wheel and keeps the focused widget in view.</summary>
    public void ScrollPanel(ref float scroll, float top, float viewH, float contentH)
    {
        if (Input.Wheel != 0 && Hover(0, top, 1e5f, viewH)) scroll -= Input.Wheel * 60;
        if (NavUsed && FocusRect is Rect fr && fr.Y + scroll >= top - 400 && fr.Y + scroll < top + contentH + 400)
        {
            // FocusRect is in last frame's screen space (already scrolled)
            if (fr.Y < top + 8) scroll -= top + 8 - fr.Y;
            else if (fr.Y + fr.H > top + viewH - 8) scroll += fr.Y + fr.H - (top + viewH - 8);
        }
        scroll = Math.Clamp(scroll, 0, MathF.Max(0, contentH - viewH));
    }
}

public struct Rect { public float X, Y, W, H; public Rect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; } }

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;

namespace GermBall.Tools.Ui;

/// <summary>
/// A small immediate-mode GUI for the editors, drawn as rectangles and the 5×7 pixel font on the overlay.
/// Panels scroll and clip; widgets lay out top to bottom (or side by side after <see cref="SameLine"/>).
/// Labels can carry a hidden id after "##" ("X##pos" shows "X").
/// Every slider and drag can be right-clicked to type an exact value.
/// </summary>
public sealed class Gui
{
    public UiInput In; public Overlay O; public int W, H;
    /// <summary>Pixel size of one font dot. 2 at 1080p.</summary>
    public float S = 2;
    public float RowH => 13 * S;
    public float Pad => 4 * S;
    public float Gap => 3 * S;
    public float CharW => 6 * S;

    // theme
    public static readonly Vector4 PanelBg = new(0.09f, 0.10f, 0.13f, 0.97f), TitleBg = new(0.15f, 0.17f, 0.22f, 1), WidgetBg = new(0.19f, 0.21f, 0.26f, 1),
        HoverBg = new(0.26f, 0.29f, 0.36f, 1), ActiveBg = new(0.34f, 0.38f, 0.47f, 1), Accent = new(0.98f, 0.66f, 0.22f, 1), AccentDim = new(0.55f, 0.38f, 0.14f, 1),
        TextCol = new(0.93f, 0.94f, 0.96f, 1), Dim = new(0.62f, 0.65f, 0.72f, 1), Selected = new(0.22f, 0.36f, 0.58f, 1), Danger = new(0.75f, 0.25f, 0.25f, 1);
    static readonly Vector4[] AxisCol = { new(0.85f, 0.32f, 0.32f, 1), new(0.36f, 0.78f, 0.36f, 1), new(0.36f, 0.55f, 0.92f, 1) };

    string active, focus;
    sealed class PanelState { public float Scroll, ContentH; }
    readonly Dictionary<string, PanelState> panels = new();
    readonly Dictionary<string, int> idCount = new();
    readonly List<Vector4> uiRects = new();
    List<Vector4> lastUiRects = new();
    Vector4 popupRect, lastPopupRect;
    /// <summary>Where the open drop-down list was drawn last frame (tests click its rows).</summary>
    public Vector4 OpenListRect => lastPopupRect;
    Action popup;   // drawn last, on top
    string openPopup;

    // layout
    float cx, cy, cw, lastX2, lastY, lastH; bool sameLine;
    Vector4 clip; string panelId = ""; PanelState ps; float panelTop;

    /// <summary>True while the mouse is over any panel or popup (the 3D view should ignore it).</summary>
    public bool MouseOverUi { get; private set; }
    /// <summary>A text box has the keyboard (editors should skip their shortcuts).</summary>
    public bool TextFocused => focus != null;
    /// <summary>Something changed this frame (for undo).</summary>
    public bool Changed;
    /// <summary>A drag or slider is being held (undo waits until it's let go).</summary>
    public bool Dragging => active != null && In.Down[0];
    public string Tooltip;
    /// <summary>Tests: where each widget was drawn this frame, by its shown label (so scripts can click "Save").</summary>
    public readonly Dictionary<string, Vector4> Rects = new();
    public bool RecordRects;
    void Rec(string label, Vector4 r)
    {
        if (!RecordRects) return;
        Rects.TryAdd(Shown(label), r);                   // the first one drawn, by label
        Rects[panelId + "/" + Shown(label)] = r;        // or qualified by panel: "left/Name"
    }

    public void Begin(UiInput input, Overlay o, int w, int h)
    {
        In = input; O = o; W = w; H = h; Changed = false; Tooltip = null; Rects.Clear();
        idCount.Clear(); uiRects.Clear();
        lastPopupRect = popupRect; popupRect = default;
        ClickAwayCommits();
        MouseOverUi = Inside(lastPopupRect, In.MouseX, In.MouseY);
        foreach (var r in lastUiRects) if (Inside(r, In.MouseX, In.MouseY)) MouseOverUi = true;
    }

    public void End()
    {
        O.Clip = null;
        if (popup != null) { var p = popup; popup = null; p(); }
        else openPopup = null;
        // a click anywhere outside the open popup closes it
        if (openPopup != null && In.Pressed[0] && !Inside(popupRect, In.MouseX, In.MouseY) && !Inside(lastPopupRect, In.MouseX, In.MouseY) && popupOpenedFrame != frame) openPopup = null;
        if (Tooltip != null)
        {
            float tw = Overlay.TextWidth(Tooltip, S) + 2 * Pad, tx = MathF.Min(In.MouseX + 14, W - tw - 2), ty = In.MouseY + 18;
            O.Rect(tx, ty, tw, RowH, new Vector4(0, 0, 0, 0.85f)); O.Text(tx + Pad, ty + (RowH - 7 * S) / 2, Tooltip, S, TextCol);
        }
        if (!In.Down[0] && !In.Down[1]) active = null;
        lastUiRects = new List<Vector4>(uiRects);
        frame++;
    }
    int frame, popupOpenedFrame = -1;

    static bool Inside(Vector4 r, float x, float y) => r.Z > 0 && x >= r.X && y >= r.Y && x < r.X + r.Z && y < r.Y + r.W;
    bool Hover(Vector4 r) =>
        Inside(r, In.MouseX, In.MouseY) && Inside(clip, In.MouseX, In.MouseY) && !Inside(lastPopupRect, In.MouseX, In.MouseY) && !(openPopup != null && Inside(popupRect, In.MouseX, In.MouseY));

    string Id(string label)
    {
        string key = panelId + "/" + label;
        idCount.TryGetValue(key, out int n); idCount[key] = n + 1;
        return n == 0 ? key : key + "#" + n;
    }
    static string Shown(string label) { int i = label.IndexOf("##", StringComparison.Ordinal); return i >= 0 ? label[..i] : label; }

    // ---------------------------------------------------------------- panels and layout
    /// <summary>A rectangle of UI that blocks the 3D view (top bars, status bars).</summary>
    public void Block(Vector4 r) => uiRects.Add(r);

    public void BeginPanel(string id, float x, float y, float w, float h, string title = null)
    {
        panelId = id;
        if (!panels.TryGetValue(id, out ps)) panels[id] = ps = new PanelState();
        var r = new Vector4(x, y, w, h); uiRects.Add(r);
        O.Clip = null;
        O.Rect(x, y, w, h, PanelBg);
        float top = y;
        if (title != null)
        {
            O.Rect(x, y, w, RowH + Gap, TitleBg);
            O.Text(x + Pad, y + (RowH + Gap - 7 * S) / 2, title.ToUpperInvariant(), S, Accent);
            top += RowH + Gap;
        }
        clip = new Vector4(x, top, w, y + h - top);
        // the wheel scrolls the panel under the mouse
        if (Inside(clip, In.MouseX, In.MouseY) && openPopup == null && In.Wheel != 0) ps.Scroll -= In.Wheel * RowH * 3;
        ps.Scroll = Math.Clamp(ps.Scroll, 0, MathF.Max(0, ps.ContentH - clip.W + Pad));
        O.Clip = clip;
        cx = x + Pad; cw = w - 2 * Pad - 3 * S; cy = top + Pad - ps.Scroll; panelTop = cy;
        sameLine = false;
    }

    public void EndPanel()
    {
        ps.ContentH = cy - panelTop + ps.Scroll * 0 + Pad;
        // a thin scrollbar when the content is taller than the panel
        if (ps.ContentH > clip.W)
        {
            float frac = clip.W / ps.ContentH, bh = MathF.Max(8 * S, clip.W * frac), by = clip.Y + (clip.W - bh) * (ps.Scroll / MathF.Max(1, ps.ContentH - clip.W + Pad));
            O.Clip = null;
            O.Rect(clip.X + clip.Z - 2 * S, by, 2 * S, bh, Dim with { W = 0.5f });
        }
        O.Clip = null; panelId = ""; clip = new Vector4(0, 0, W, H);
    }

    public void ScrollToTop(string id) { if (panels.TryGetValue(id, out var p)) p.Scroll = 0; }

    public void SameLine() => sameLine = true;
    Vector4 lastPlaced;
    /// <summary>A tooltip for the widget just drawn (instead of a line of help text under it).</summary>
    public void Hint(string text) { if (Hover(lastPlaced)) Tooltip = text; }

    /// <summary>A button-like drop-down that runs an action instead of holding a value (toolbar menus: New, Open...).
    /// Returns the picked option, or null.</summary>
    public string Menu(string label, IList<string> options, float width)
    {
        string v = null;
        var r = Place(width, RowH); Rec(label, r);
        return ComboCore(Id("##menu" + label), r, ref v, options, null, label) ? v : null;
    }
    public float Width => cw;
    /// <summary>Width of each of <paramref name="n"/> items sharing a row.</summary>
    public float Split(int n) => (cw - Gap * (n - 1)) / n;
    public void Space(float h = -1) { cy += h < 0 ? Gap * 2 : h; }

    public Vector4 Place(float w, float h)
    {
        float x, y;
        if (sameLine) { x = lastX2 + Gap; y = lastY; sameLine = false; cy = MathF.Max(cy, y + h + Gap); }
        else { x = cx; y = cy; cy = y + h + Gap; }
        if (w <= 0) w = cx + cw - x;
        lastX2 = x + w; lastY = y; lastH = h;
        lastPlaced = new Vector4(x, y, w, h);
        return lastPlaced;
    }
    bool Visible(Vector4 r) => r.Y + r.W >= clip.Y && r.Y <= clip.Y + clip.W;

    void TextIn(Vector4 r, string s, Vector4 c, bool center = false, float inset = -1)
    {
        if (inset < 0) inset = Pad;
        int max = Math.Max(0, (int)((r.Z - 2 * inset + S) / CharW));
        if (s.Length > max) s = max > 1 ? s[..(max - 1)] + "~" : "";
        float tx = center ? r.X + (r.Z - Overlay.TextWidth(s, S)) / 2 : r.X + inset;
        O.Text(tx, r.Y + (r.W - 7 * S) / 2, s, S, c);
    }

    /// <summary>A full-width row with the label on the left; returns the rectangle left for the control.</summary>
    Vector4 LabeledRow(string label, out Vector4 labelRect, float frac = 0.45f)
    {
        var r = Place(0, RowH);
        float lw = MathF.Min(r.Z * frac, 22 * CharW);
        labelRect = new Vector4(r.X, r.Y, lw, r.W);
        Rec(label, new Vector4(r.X + lw, r.Y, r.Z - lw, r.W));
        TextIn(labelRect, Shown(label), Dim, false, 0);
        return new Vector4(r.X + lw, r.Y, r.Z - lw, r.W);
    }

    // ---------------------------------------------------------------- widgets
    public void Label(string text, Vector4? color = null)
    {
        var r = Place(0, RowH);
        if (Visible(r)) TextIn(r, text, color ?? TextCol, false, 0);
    }
    public void Heading(string text)
    {
        Space(Gap);
        var r = Place(0, RowH);
        if (Visible(r)) { TextIn(r, text.ToUpperInvariant(), Accent, false, 0); O.Rect(r.X, r.Y + r.W - S, r.Z, S, AccentDim); }
    }
    public void Separator() { var r = Place(0, S * 3); O.Rect(r.X, r.Y + S, r.Z, S, WidgetBg); }

    public bool Button(string label, float w = 0, bool enabled = true, Vector4? color = null)
    {
        string id = Id(label);
        var r = Place(w <= 0 && !sameLine ? 0 : (w <= 0 ? Overlay.TextWidth(Shown(label), S) + 2 * Pad : w), RowH);
        Rec(label, r);
        bool hover = enabled && Hover(r);
        if (hover && In.Pressed[0]) active = id;
        bool clicked = hover && In.Released[0] && active == id;
        if (Visible(r))
        {
            var bg = !enabled ? WidgetBg with { W = 0.5f } : active == id && In.Down[0] ? ActiveBg : hover ? HoverBg : color ?? WidgetBg;
            O.Rect(r.X, r.Y, r.Z, r.W, bg);
            TextIn(r, Shown(label), enabled ? TextCol : Dim, true);
        }
        return clicked;
    }

    /// <summary>A button that stays lit while <paramref name="on"/>.</summary>
    public bool Toggle(string label, bool on, float w = 0) => Button(label, w, true, on ? Selected : null);

    public bool Checkbox(string label, ref bool v)
    {
        string id = Id(label);
        var r = Place(0, RowH);
        Rec(label, r);
        bool hover = Hover(r);
        if (hover && In.Pressed[0]) active = id;
        bool clicked = hover && In.Released[0] && active == id;
        if (clicked) { v = !v; Changed = true; }
        if (Visible(r))
        {
            float b = RowH - 4 * S;
            O.Rect(r.X, r.Y + 2 * S, b, b, hover ? HoverBg : WidgetBg);
            if (v) O.Rect(r.X + 2 * S, r.Y + 4 * S, b - 4 * S, b - 4 * S, Accent);
            TextIn(new Vector4(r.X + b + Gap, r.Y, r.Z - b - Gap, r.W), Shown(label), TextCol, false, 0);
        }
        return clicked;
    }

    /// <summary>A labelled slider between <paramref name="min"/> and <paramref name="max"/>. Right-click to type a value.</summary>
    public bool Slider(string label, ref float v, float min, float max, string fmt = "0.00")
    {
        string id = Id(label);
        var r = LabeledRow(label, out _);
        return SliderIn(id, r, ref v, min, max, fmt);
    }
    public bool SliderInt(string label, ref int v, int min, int max)
    {
        float f = v; bool c = Slider(label, ref f, min, max, "0");
        if (c) v = (int)MathF.Round(f);
        return c;
    }

    bool SliderIn(string id, Vector4 r, ref float v, float min, float max, string fmt)
    {
        if (NumberEdit(id, r, ref v, fmt, out bool busy)) return true;
        if (busy) return false;
        bool hover = Hover(r), changed = false;
        if (hover && In.Pressed[0]) Grab(id);
        // drag to set (once the mouse has moved a few pixels); a plain click types instead
        if (active == id && In.Down[0] && DragStarted())
        {
            float t = MathX.Clamp01((In.MouseX - r.X) / r.Z), nv = min + (max - min) * t;
            if (In.Shift) nv = MathF.Round(nv / ((max - min) / 20)) * ((max - min) / 20);   // shift: snap to twentieths
            if (nv != v) { v = nv; changed = true; Changed = true; }
        }
        ClickToType(id, hover, v);
        if (Visible(r))
        {
            O.Rect(r.X, r.Y, r.Z, r.W, active == id ? ActiveBg : hover ? HoverBg : WidgetBg);
            float f = max > min ? MathX.Clamp01((v - min) / (max - min)) : 0;
            O.Rect(r.X, r.Y, r.Z * f, r.W, AccentDim);
            TextIn(r, v.ToString(fmt, CultureInfo.InvariantCulture), TextCol, true);
        }
        if (hover) Tooltip ??= "drag to change (shift snaps), click to type";
        return changed;
    }

    /// <summary>An unbounded number you drag left and right (shift: fine, ctrl: coarse). Click to type a value.</summary>
    public bool Drag(string label, ref float v, float speed = 0.01f, string fmt = "0.000")
    {
        string id = Id(label);
        var r = LabeledRow(label, out _);
        return DragIn(id, r, ref v, speed, fmt, null);
    }

    bool DragIn(string id, Vector4 r, ref float v, float speed, string fmt, Vector4? tint)
    {
        if (NumberEdit(id, r, ref v, fmt, out bool busy)) return true;
        if (busy) return false;
        bool hover = Hover(r), changed = false;
        if (hover && In.Pressed[0]) Grab(id);
        if (active == id && In.Down[0] && DragStarted() && In.MouseDX != 0)
        {
            v += In.MouseDX * speed * (In.Shift ? 0.1f : 1) * (In.Ctrl ? 10 : 1);
            changed = true; Changed = true;
        }
        ClickToType(id, hover, v);
        if (Visible(r))
        {
            O.Rect(r.X, r.Y, r.Z, r.W, active == id ? ActiveBg : hover ? HoverBg : WidgetBg);
            if (tint is Vector4 t) O.Rect(r.X, r.Y, 2 * S, r.W, t);
            TextIn(r, v.ToString(fmt, CultureInfo.InvariantCulture), TextCol, true, 2 * S);
        }
        if (hover) Tooltip ??= "drag to change (shift fine, ctrl coarse), click to type";
        return changed;
    }

    // a press on a number: dragging changes it, releasing without moving types into it
    float grabX, grabY; bool grabMoved;
    void Grab(string id) { active = id; grabX = In.MouseX; grabY = In.MouseY; grabMoved = false; }
    bool DragStarted()
    {
        if (!grabMoved && (MathF.Abs(In.MouseX - grabX) > 3 || MathF.Abs(In.MouseY - grabY) > 3)) grabMoved = true;
        return grabMoved;
    }
    void ClickToType(string id, bool hover, float v)
    {
        if (active == id && In.Released[0] && hover && !grabMoved) StartEdit(id, v.ToString("0.####", CultureInfo.InvariantCulture));
    }

    /// <summary>Three drags in a row, tinted X red, Y green, Z blue.</summary>
    public bool Vec3(string label, ref Vector3 v, float speed = 0.01f, string fmt = "0.000")
    {
        string id = Id(label);
        var row = LabeledRow(label, out _, 0.28f);   // three numbers need the room
        float w = (row.Z - 2 * Gap) / 3; bool c = false;
        float x = v.X, y = v.Y, z = v.Z;
        c |= DragIn(id + ".x", new Vector4(row.X, row.Y, w, row.W), ref x, speed, fmt, AxisCol[0]);
        c |= DragIn(id + ".y", new Vector4(row.X + w + Gap, row.Y, w, row.W), ref y, speed, fmt, AxisCol[1]);
        c |= DragIn(id + ".z", new Vector4(row.X + 2 * (w + Gap), row.Y, w, row.W), ref z, speed, fmt, AxisCol[2]);
        if (c) v = new Vector3(x, y, z);
        return c;
    }

    // ---------------------------------------------------------------- typing
    // One field has the keyboard at a time. Its text is edited in editBuf with a caret; typing first replaces the whole
    // value (it starts selected). Enter or Tab commits, Escape cancels, clicking anywhere else commits.
    string editBuf = ""; int caret; bool selectAll;
    Vector4 focusRect;
    string pendingCommit, pendingValue;

    void StartEdit(string id, string text)
    {
        if (focus != null && focus != id) CommitFocusAway();
        focus = id; editBuf = text ?? ""; caret = editBuf.Length; selectAll = true; active = null;
    }
    void CommitFocusAway() { pendingCommit = focus; pendingValue = editBuf; focus = null; }

    /// <summary>Called from <see cref="Begin"/>: a click outside the field being typed in commits it before anything else is drawn,
    /// so the value lands even when the click changes what's on screen.</summary>
    void ClickAwayCommits()
    {
        if (focus != null && (In.Pressed[0] || In.Pressed[1]) && !Inside(focusRect, In.MouseX, In.MouseY)) CommitFocusAway();
    }

    /// <summary>Applies typed characters and editing keys. Returns true on Enter or Tab.</summary>
    bool EditKeys(out bool cancel)
    {
        cancel = In.Key(Keys.Escape);
        caret = Math.Clamp(caret, 0, editBuf.Length);
        if (In.Ctrl && In.Key('A')) selectAll = true;
        if (In.Typed.Length > 0)
        {
            if (selectAll) { editBuf = ""; caret = 0; selectAll = false; }
            editBuf = editBuf.Insert(caret, In.Typed); caret += In.Typed.Length;
        }
        if (In.Key(Keys.Backspace))
        {
            if (selectAll) { editBuf = ""; caret = 0; selectAll = false; }
            else if (caret > 0) { editBuf = editBuf.Remove(caret - 1, 1); caret--; }
        }
        if (In.Key(Keys.Delete))
        {
            if (selectAll) { editBuf = ""; caret = 0; selectAll = false; }
            else if (caret < editBuf.Length) editBuf = editBuf.Remove(caret, 1);
        }
        if (In.Key(Keys.Left)) { caret = selectAll ? 0 : Math.Max(0, caret - 1); selectAll = false; }
        if (In.Key(Keys.Right)) { caret = selectAll ? editBuf.Length : Math.Min(editBuf.Length, caret + 1); selectAll = false; }
        if (In.Key(Keys.Home)) { caret = 0; selectAll = false; }
        if (In.Key(Keys.End)) { caret = editBuf.Length; selectAll = false; }
        return In.Key(Keys.Enter) || In.Key(Keys.Tab);
    }

    /// <summary>Draws the text being edited: the selection highlighted, or a blinking caret; scrolled to keep the caret in view.</summary>
    void DrawEditing(Vector4 r)
    {
        focusRect = r;
        O.Rect(r.X, r.Y, r.Z, r.W, ActiveBg);
        O.Frame(r.X, r.Y, r.Z, r.W, S, Accent);
        int max = Math.Max(1, (int)((r.Z - 2 * Pad) / CharW));
        int start = Math.Max(0, caret - max + 1);
        string shown = editBuf.Substring(start, Math.Min(max, editBuf.Length - start));
        float tx = r.X + Pad, ty = r.Y + (r.W - 7 * S) / 2;
        if (selectAll && editBuf.Length > 0) O.Rect(tx - S, ty - S, Overlay.TextWidth(shown, S) + 2 * S, 9 * S, Selected);
        O.Text(tx, ty, shown, S, TextCol);
        if (!selectAll && (frame / 30) % 2 == 0) O.Rect(tx + (caret - start) * CharW - S, ty - S, S, 9 * S, Accent);
    }

    /// <summary>A number being typed into (after a click). Returns true when a new value is committed.</summary>
    bool NumberEdit(string id, Vector4 r, ref float v, string fmt, out bool busy)
    {
        busy = false;
        bool hover = Hover(r);
        if (hover && (In.Pressed[1] || (In.Pressed[0] && In.Ctrl) || (In.DoubleClick && In.Pressed[0])) && focus != id)
            StartEdit(id, v.ToString("0.####", CultureInfo.InvariantCulture));
        string text = null;
        if (pendingCommit == id) { pendingCommit = null; text = pendingValue; }
        else if (focus == id)
        {
            bool commit = EditKeys(out bool cancel);
            if (Visible(r)) DrawEditing(r);
            busy = true;
            if (cancel) { focus = null; return false; }
            if (!commit) return false;   // still typing
            focus = null; text = editBuf;
        }
        if (text == null) return false;
        if (float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float nv) && float.IsFinite(nv) && nv != v) { v = nv; Changed = true; return true; }
        return false;
    }

    /// <summary>A text box. Returns true when the edit is committed (Enter or clicking away) with a new value.</summary>
    public bool TextField(string label, ref string value, string placeholder = "")
    {
        string id = Id(label);
        var r = string.IsNullOrEmpty(Shown(label)) ? Place(0, RowH) : LabeledRow(label, out _);
        return TextBox(id, r, ref value, placeholder);
    }

    bool TextBox(string id, Vector4 r, ref string value, string placeholder)
    {
        bool hover = Hover(r);
        if (hover && In.Pressed[0] && focus != id) StartEdit(id, value ?? "");
        string result = null;
        if (focus == id)
        {
            bool enter = EditKeys(out bool cancel);
            if (cancel) focus = null;
            else if (enter) { focus = null; result = editBuf; }
        }
        else if (pendingCommit == id) { pendingCommit = null; result = pendingValue; }
        if (Visible(r))
        {
            if (focus == id) DrawEditing(r);
            else
            {
                O.Rect(r.X, r.Y, r.Z, r.W, hover ? HoverBg : WidgetBg);
                TextIn(r, string.IsNullOrEmpty(value) ? placeholder : value, string.IsNullOrEmpty(value) ? Dim : TextCol);
            }
        }
        if (result != null && result != value) { value = result; Changed = true; return true; }
        return false;
    }

    /// <summary>A drop-down list. Returns true when a different option is picked.</summary>
    public bool Combo(string label, ref string value, IList<string> options, string none = null)
    {
        string id = Id(label);
        var r = string.IsNullOrEmpty(Shown(label)) ? Place(0, RowH) : LabeledRow(label, out _);
        return ComboCore(id, r, ref value, options, none);
    }

    /// <summary>A combo of a fixed width, for toolbars (follows <see cref="SameLine"/>).</summary>
    public bool ComboW(string label, ref string value, IList<string> options, float width, string none = null)
    {
        string id = Id(label);
        var r = Place(width, RowH); Rec(label, r);
        return ComboCore(id, r, ref value, options, none);
    }

    bool ComboCore(string id, Vector4 r, ref string value, IList<string> options, string none, string shown = null)
    {
        bool hover = Hover(r);
        if (hover && In.Pressed[0]) { openPopup = openPopup == id ? null : id; popupOpenedFrame = frame; }
        if (Visible(r))
        {
            O.Rect(r.X, r.Y, r.Z, r.W, openPopup == id ? ActiveBg : hover ? HoverBg : WidgetBg);
            TextIn(new Vector4(r.X, r.Y, r.Z - CharW, r.W), shown ?? (string.IsNullOrEmpty(value) ? (none ?? "-") : value), shown != null ? TextCol : string.IsNullOrEmpty(value) ? Dim : TextCol);
            O.Text(r.X + r.Z - CharW - S, r.Y + (r.W - 7 * S) / 2, "v", S, Dim);
        }
        if (openPopup != id) return false;
        // the list itself is drawn at the end of the frame, over everything
        string picked = null; bool pickedNone = false;
        var opts = new List<string>(options); string cur = value;
        float rowH = RowH, listH = MathF.Min((opts.Count + (none != null ? 1 : 0)) * rowH, H * 0.6f);
        float px = r.X, py = r.Y + r.W + 1, pw = MathF.Max(r.Z, 14 * CharW);
        if (py + listH > H) py = MathF.Max(0, r.Y - listH - 1);
        if (px + pw > W) px = W - pw;
        // selection is resolved this frame from last frame's layout
        int count = opts.Count + (none != null ? 1 : 0);
        float scroll = popupScroll.TryGetValue(id, out var sc) ? sc : 0;
        var listRect = new Vector4(px, py, pw, listH);
        if (Inside(listRect, In.MouseX, In.MouseY))
        {
            if (In.Wheel != 0) { scroll = Math.Clamp(scroll - In.Wheel * rowH * 3, 0, MathF.Max(0, count * rowH - listH)); popupScroll[id] = scroll; }
            int idx = (int)((In.MouseY - py + scroll) / rowH);
            if (In.Released[0] && frame != popupOpenedFrame && idx >= 0 && idx < count)
            {
                if (none != null && idx == 0) pickedNone = true; else picked = opts[idx - (none != null ? 1 : 0)];
                openPopup = null;
            }
        }
        popupRect = listRect;
        popup = () =>
        {
            O.Clip = null;
            O.Rect(px - S, py - S, pw + 2 * S, listH + 2 * S, Accent);
            O.Rect(px, py, pw, listH, TitleBg);
            O.Clip = listRect;
            for (int i = 0; i < count; i++)
            {
                float y = py + i * rowH - scroll; if (y + rowH < py || y > py + listH) continue;
                string text = none != null && i == 0 ? none : opts[i - (none != null ? 1 : 0)];
                var rr = new Vector4(px, y, pw, rowH);
                bool h = Inside(rr, In.MouseX, In.MouseY) && Inside(listRect, In.MouseX, In.MouseY);
                if (h) O.Rect(rr.X, rr.Y, rr.Z, rr.W, HoverBg);
                else if (text == cur || (none != null && i == 0 && string.IsNullOrEmpty(cur))) O.Rect(rr.X, rr.Y, rr.Z, rr.W, Selected);
                TextIn(rr, text, i == 0 && none != null ? Dim : TextCol);
            }
            O.Clip = null;
        };
        if (pickedNone && !string.IsNullOrEmpty(value)) { value = null; Changed = true; return true; }
        if (picked != null && picked != value) { value = picked; Changed = true; return true; }
        return false;
    }
    readonly Dictionary<string, float> popupScroll = new();

    public bool ComboIndex(string label, ref int index, IList<string> options)
    {
        string v = index >= 0 && index < options.Count ? options[index] : null;
        if (!Combo(label, ref v, options)) return false;
        index = options.IndexOf(v); return true;
    }

    /// <summary>A row in a list. Returns true when clicked.</summary>
    public bool Selectable(string text, bool selected, float indent = 0, Vector4? color = null)
    {
        string id = Id(text);
        var r = Place(0, RowH);
        Rec(text, r);
        bool hover = Hover(r);
        if (hover && In.Pressed[0]) active = id;
        bool clicked = hover && In.Released[0] && active == id;
        if (Visible(r))
        {
            if (selected) O.Rect(r.X, r.Y, r.Z, r.W, Selected);
            else if (hover) O.Rect(r.X, r.Y, r.Z, r.W, HoverBg);
            TextIn(new Vector4(r.X + indent, r.Y, r.Z - indent, r.W), Shown(text), color ?? TextCol, false, S * 2);
        }
        return clicked;
    }

    /// <summary>A collapsible section header.</summary>
    public bool Header(string text, ref bool open)
    {
        string id = Id(text);
        Space(S);
        var r = Place(0, RowH); Rec(text, r);
        bool hover = Hover(r);
        if (hover && In.Pressed[0]) active = id;
        if (hover && In.Released[0] && active == id) open = !open;
        if (Visible(r))
        {
            O.Rect(r.X, r.Y, r.Z, r.W, hover ? HoverBg : TitleBg);
            TextIn(r, (open ? "- " : "+ ") + Shown(text).ToUpperInvariant(), Accent);
        }
        return open;
    }

    public bool Tabs(ref int sel, params string[] names)
    {
        float w = Split(names.Length); bool changed = false;
        for (int i = 0; i < names.Length; i++)
        {
            if (i > 0) SameLine();
            if (Toggle(names[i] + "##tab", sel == i, w) && sel != i) { sel = i; changed = true; }
        }
        Space(S);
        return changed;
    }

    /// <summary>Colour as hue, saturation and value sliders, with a swatch and the hex code (which can be typed).</summary>
    public bool Color(string label, ref string hex)
    {
        var c = ColorHex.Parse(hex);
        var hsv = ToHsv(new Vector3(c.X, c.Y, c.Z));
        string id = Id(label);
        var row = LabeledRow(label, out _);
        // swatch + hex text
        float sw = row.W * 1.6f;
        O.Rect(row.X, row.Y, sw, row.W, new Vector4(c.X, c.Y, c.Z, 1));
        O.Frame(row.X, row.Y, sw, row.W, S, Dim);
        string h2 = hex;
        if (TextBox(id + ".hex", new Vector4(row.X + sw + Gap, row.Y, row.Z - sw - Gap, row.W), ref h2, "#RRGGBB"))
        {
            var t = h2.Trim().TrimStart('#');
            if (t.Length == 6 && uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) { hex = "#" + t.ToUpperInvariant(); return true; }
        }
        float hue = hsv.X, sat = hsv.Y, val = hsv.Z; bool c2 = false;
        c2 |= Slider("  hue##" + id, ref hue, 0, 360, "0");
        c2 |= Slider("  saturation##" + id, ref sat, 0, 1);
        c2 |= Slider("  brightness##" + id, ref val, 0, 1);
        if (c2) { hex = ColorHex.Format(FromHsv(new Vector3(hue, sat, val))); return true; }
        return false;
    }

    public static Vector3 ToHsv(Vector3 c)
    {
        float max = MathF.Max(c.X, MathF.Max(c.Y, c.Z)), min = MathF.Min(c.X, MathF.Min(c.Y, c.Z)), d = max - min, h = 0;
        if (d > 1e-5f)
        {
            if (max == c.X) h = 60 * (((c.Y - c.Z) / d) % 6);
            else if (max == c.Y) h = 60 * ((c.Z - c.X) / d + 2);
            else h = 60 * ((c.X - c.Y) / d + 4);
        }
        if (h < 0) h += 360;
        return new Vector3(h, max > 1e-5f ? d / max : 0, max);
    }
    public static Vector3 FromHsv(Vector3 hsv)
    {
        float h = (hsv.X % 360 + 360) % 360, s = hsv.Y, v = hsv.Z, c = v * s, x = c * (1 - MathF.Abs((h / 60) % 2 - 1)), m = v - c;
        Vector3 r = h < 60 ? new(c, x, 0) : h < 120 ? new(x, c, 0) : h < 180 ? new(0, c, x) : h < 240 ? new(0, x, c) : h < 300 ? new(x, 0, c) : new(c, 0, x);
        return r + new Vector3(m);
    }

    /// <summary>A bar of text across the given rectangle (status lines).</summary>
    public void Bar(Vector4 r, string text, Vector4? color = null)
    {
        uiRects.Add(r);
        O.Clip = null;
        O.Rect(r.X, r.Y, r.Z, r.W, TitleBg);
        TextIn(r, text, color ?? Dim);
    }

    /// <summary>Lays widgets out inside an arbitrary rectangle (toolbars).</summary>
    public void BeginArea(string id, Vector4 r, Vector4? bg = null)
    {
        panelId = id; uiRects.Add(r);
        O.Clip = null;
        if (bg != null) O.Rect(r.X, r.Y, r.Z, r.W, bg.Value);
        clip = r; cx = r.X + Pad; cw = r.Z - 2 * Pad; cy = r.Y + (r.W - RowH) / 2; sameLine = false;
        if (!panels.TryGetValue(id, out ps)) panels[id] = ps = new PanelState();
    }
    public void EndArea() { panelId = ""; clip = new Vector4(0, 0, W, H); }

    /// <summary>Raw access for custom widgets (the timeline): the next rectangle of the given height, and whether it's hovered.</summary>
    public Vector4 Custom(float h, out bool hover) { var r = Place(0, h); hover = Hover(r); return r; }
    public bool IsActive(string id) => active == id;
    public void SetActive(string id) => active = id;
    public bool HoverRect(Vector4 r) => Hover(r);
}

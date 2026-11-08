using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Game;
using GermBall.Platform;
using GermBall.Render;

namespace GermBall.UI;

/// <summary>
/// A running world: the briefing / pause card with its tabs (Controls, Settings, Keys, Debug), the game-over card,
/// and while playing, the simulation, camera and HUD.
/// </summary>
public sealed class GameScreen : IScreen
{
    readonly App app; readonly World wd_; readonly Gui gui = new();
    Settings SET => wd_.SET;
    bool over, everDeployed;
    int tab; float scroll; string waiting; int waitingI; string note = "";
    static readonly string[] TABS = { "Controls", "Settings", "Keys", "Debug" };

    public GameScreen(App app, World world)
    {
        this.app = app; wd_ = world;
        wd_.OnGameOver = () => { over = true; Pause(); };
        Sound.Init();
        if (Music.Instance == null) { var m = Music.Load(System.IO.Path.Combine(Paths.Data, "music")); app.Audio.Add(m); }
        Music.Instance.OnSong = name => { if (wd_.playing) wd_.ShowHint("♪ " + name.ToUpperInvariant()); };
        ApplyGraphics(); ApplyAudio();
        Input.Lock(app.Window, false);
    }

    // ---------------------------------------------------------------- state changes
    void Deploy()
    {
        everDeployed = true; wd_.playing = true; Input.Lock(app.Window, true); wd_.padArmed = false;
        Speech.Cancel();
    }
    void Pause() { wd_.playing = false; Input.Lock(app.Window, false); waiting = null; gui.Focus = null; gui.SyncPad(); }
    void QuitToTitle()
    {
        Pause(); Speech.Cancel();
        app.Screen = new TitleScreen(app);
    }
    void ApplyGraphics()
    {
        var r = app.Renderer;
        r.RenderScale = SET.res > 0 ? Math.Min(1, SET.res / (float)Math.Max(1, app.Window.Height)) : 1;
        wd_.scene.Shadows = SET.shadows > 0; wd_.scene.ShadowMapSize = SET.shadows >= 2 ? Math.Max(2048, SET.shadowRes) : 1024;
        r.VSync = SET.vsync;
        if (app.Window.Fullscreen != SET.fullscreen) app.Window.SetFullscreen(SET.fullscreen);
        wd_.ApplyDecorPublic();
    }
    void ApplyAudio()
    {
        var a = app.Audio; a.Master = 1; a.SfxVolume = SET.volume;
        a.MusicVolume = SET.music * (Speech.Talking != null ? 0.55f : 1);
    }
    void Save() => Prefs.SaveSettings();

    // ---------------------------------------------------------------- frame
    public void Update(float dt)
    {
        if (wd_.playing)
        {
            if (Input.Pressed("Escape") || !Input.Focused) Pause();
            else if (wd_.pad.on && wd_.pad.start && !wd_.pad.pstart) Pause();
            if (!wd_.playing) { gui.NavUsed = wd_.padActive; }
        }
        ApplyAudio();
        if (app.Window.Fullscreen != SET.fullscreen) { SET.fullscreen = app.Window.Fullscreen; Save(); }   // F11 / Alt+Enter
        wd_.Frame(dt, app.Renderer.Width / (float)Math.Max(1, app.Renderer.Height));
    }
    public void Render(float dt) => app.Renderer.Render(wd_.scene, wd_.camera);

    public void DrawUI(Draw2D d, float dt)
    {
        float Wd = app.UiW, Ht = app.UiH;
        if (wd_.playing) { wd_.DrawHUD(d, Wd, Ht, wd_.clockT, dt); return; }
        gui.Begin(d, dt);
        if (over) { DrawOver(d, Wd, Ht); return; }
        DrawBrief(d, Wd, Ht, dt);
    }

    // ---------------------------------------------------------------- game over
    void DrawOver(Draw2D d, float Wd, float Ht)
    {
        d.Rect(0, 0, Wd, Ht, new Vector4(20 / 255f, 2 / 255f, 20 / 255f, 0.7f));
        float cw = Math.Min(420, Wd - 32), x = Wd / 2 - cw / 2, txtH = 60, ch = 24 + 50 + txtH + 18 + 40 + 24, y = Ht / 2 - ch / 2;
        d.Rect(x, y, cw, ch, Pal.Panel); d.Frame(x, y, cw, ch, Pal.Hot);
        d.Text("DISSOLVED", Wd / 2, y + 24, d.DisplayBig, Pal.Hot, "center", 3);
        d.Wrap(wd_.GameOverText ?? "", x + 28, y + 74, cw - 56, d.MonoSmall, Pal.Text2, 1.5f);
        float bw = (cw - 56 - 8) / 2, by = y + ch - 24 - 40;
        if (gui.Big("again", x + 28, by, bw, 40, "Again") || gui.StartHit) { wd_.RestartWaves(); over = false; Deploy(); }
        if (gui.Big("otitle", x + 28 + bw + 8, by, bw, 40, "Title")) QuitToTitle();
        if (gui.Focus == null && wd_.padActive) gui.FocusFirst();
    }

    // ---------------------------------------------------------------- the briefing card
    void DrawBrief(Draw2D d, float Wd, float Ht, float dt)
    {
        d.Rect(0, 0, Wd, Ht, new Vector4(16 / 255f, 4 / 255f, 30 / 255f, 0.6f));
        if (gui.Back && everDeployed && waiting == null) { Deploy(); return; }
        if (gui.StartHit) { Deploy(); return; }
        if (gui.TabNext || gui.TabPrev) { tab = (tab + (gui.TabNext ? 1 : 3)) % 4; scroll = 0; }
        var (eyebrow, title, text) = wd_.Briefing();
        float cw = Math.Min(560, Wd - 32), pad = 26, x = Wd / 2 - cw / 2, iw = cw - pad * 2;
        float pTextH = tab == 0 ? MeasureWrap(d, text, iw, d.MonoSmall, 1.55f) + 16 : 0;
        float headH = 18 + 64 + pTextH + 44, footH = 16 + 44 + 8 + 32 + 28;
        float panelH = Math.Min(Math.Min(Ht * 0.52f, 460), Ht - 32 - pad * 2 - headH - footH); panelH = Math.Max(120, panelH);
        float ch = pad * 2 + headH + panelH + footH, y = Math.Max(16, Ht / 2 - ch / 2);
        gui.CutPanel(x, y, cw, ch, 22, Pal.Panel, Pal.HudDim);
        float cy = y + pad, ix = x + pad;
        d.Text(eyebrow.ToUpperInvariant(), ix, cy, d.MonoSmall, Pal.Hud, "left", 2.6f); cy += 18;
        d.Text(title.ToUpperInvariant(), ix, cy, d.DisplayTitle, Pal.Ink, "left", 2); cy += 64;
        if (tab == 0) { cy += 8; d.Wrap(text, ix, cy, iw, d.MonoSmall, Pal.Text, 1.55f); cy += pTextH - 8; }
        // tabs
        cy += 8; float tx = ix;
        for (int i = 0; i < 4; i++) { if (gui.Tab("tab" + i, tx, cy, TABS[i], tab == i, out float tw)) { tab = i; scroll = 0; } tx += tw + 4; }
        d.Rect(ix, cy + 30, iw, 1, Pal.HudFaint); cy += 44;
        // the panel, scrolled and clipped
        float top = cy; gui.PushClip(ix - 6, top, iw + 12, panelH);
        float contentH = tab switch { 0 => PanelControls(d, ix, top - scroll, iw), 1 => PanelSettings(d, ix, top - scroll, iw), 2 => PanelKeys(d, ix, top - scroll, iw), _ => PanelDebug(d, ix, top - scroll, iw) };
        gui.PopClip();
        if (contentH - scroll > panelH + 2)
        {   // more below: fade the last rows out instead of slicing them at the edge
            for (int k = 0; k < 8; k++) d.Rect(ix - 6, top + panelH - 26 + k * 3.25f, iw + 12, 3.25f, Pal.A(Pal.Panel, (k + 1) / 8f));
        }
        gui.ScrollPanel(ref scroll, top, panelH, contentH);
        if (contentH > panelH) { float k = panelH / contentH; d.Rect(ix + iw + 4, top + scroll / contentH * panelH, 3, panelH * k, Pal.HudDim); }
        cy = top + panelH + 16;
        if (gui.Big("deploy", ix, cy, iw, 44, everDeployed ? "Resume" : "Deploy")) { Deploy(); return; }
        cy += 52;
        if (gui.Big("quit", ix, cy, iw, 32, "Quit to title", Pal.Danger, new Vector4(0, 0, 0, 0))) { QuitToTitle(); return; }
        cy += 40;
        string ps = wd_.pad.on ? "Controller connected. Press A to deploy." : "No controller detected. Connect one and press any button.";
        d.Text(ps, Wd / 2, cy, d.MonoSmall, wd_.pad.on ? Pal.Hud : Pal.Mute2, "center");
        if (gui.Focus == null && gui.NavUsed) gui.Focus = "deploy";
        // key capture for the Keys tab
        if (waiting != null)
        {
            string k = Input.LastPressed; Input.LastPressed = null;
            if (k != null && k != "Mouse0-click")
            {
                if (k == "Escape") { }
                else if (k == "Backspace" || k == "Delete") { wd_.BINDS[waiting][waitingI] = ""; Prefs.SaveBinds(); }
                else { wd_.BINDS[waiting][waitingI] = k; Prefs.SaveBinds(); }
                waiting = null;
            }
        }
    }
    static float MeasureWrap(Draw2D d, string s, float width, UiFont f, float gap)
    {
        float lh = f.Size * gap, h = 0;
        foreach (var para in s.Split('\n'))
        {
            string line = ""; h += lh;
            foreach (var wd in para.Split(' ')) { var t = line.Length == 0 ? wd : line + " " + wd; if (f.Measure(t) > width && line.Length > 0) { h += lh; line = wd; } else line = t; }
        }
        return h;
    }

    // ---- Controls
    static readonly (string, string)[] KEYS_HELP =
    {
        ("W A S D", "Move. Reverse at speed to skid"), ("Mouse", "Orbit camera (look, in first person)"), ("Space", "Jump. Again in the air: dash. With homing charges the dash locks on, flies fast and bursts small germs outright"),
        ("Left click / F", "Fire or swing. Melee swings chain into a three-hit combo ending in a finisher, and step you in toward the nearest enemy; the squash hammer flattens germs like pancakes. Bows and charge weapons: hold, then release. Energy weapons overheat"),
        ("1 2 3 / wheel / P", "Switch weapon (three slots; an empty slot is your fists)"), ("G", "Interact: take a weapon (swaps out the one in hand when full), get in or out of a vehicle or the Med-Mech"), ("R", "Reload. Guns hold a magazine; spare magazines are stored (shown as pips on the weapon slot)"),
        ("Q / mouse 4", "Punch, whatever you're holding (not while rolling). Curled into a ball in the air: ground pound"),
        ("C / Shift", "Roll. From a standstill, hold to charge a spin dash"),
        ("E / right click", "Pick up the thing or small germ in front of you (any germ below a brute once it has lost both legs). Then hold this, or the fire button, to wind up a throw (watch the meter and the arc) and release to throw at the crosshair"),
        ("Right mouse", "Aim down sights. In the air you hover and can still shoot; dashing ends the hover"),
        ("Vehicles", "Hand weapons are stowed in the blood cell and the virus (they have their own guns); on the platelet bike only sidearms, the shotgun and melee work. Platelet bike: Space jumps, Space again in the air jump-dashes, W/S pitch and A/D roll it in the air; land on the wheels or you're thrown off. Blood cell: Shift boosts (5 s cooldown), its cannon follows your aim and fires with the trigger. Virus: slides any way you push, Space / E rises and C / Shift sinks (rebind under Keys), fires its spike beams"),
        ("V", "Free camera on/off (also in Debug)"), ("B", "Back to base"), ("M", "Next song"), ("Esc", "Pause"), ("F11 / Alt+Enter", "Fullscreen"), ("F12", "Screenshot (saved under Documents\\My Games\\GermBall\\screenshots)"),
    };
    static readonly (string, string)[] PAD_HELP =
    {
        ("Left stick / D-pad", "Move"), ("Right stick", "Orbit camera. Click it to recenter"), ("A", "Jump. Again in the air to dash"), ("RT / LT", "Fire / aim down sights (hover in the air)"),
        ("B", "Roll and spin dash"), ("X", "Tap: reload. Hold: interact (take a weapon, get in or out of a vehicle or the Med-Mech)"),
        ("On vehicles", "Platelet bike: A jumps, A again in the air jump-dashes, the stick pitches and rolls it in the air. Blood cell: B boosts, RT fires the cannon. Virus: RB rises, LB sinks, RT fires the beams"),
        ("RB", "Punch. Curled into a ball in the air: ground pound"), ("LB", "Pick up; hold to wind up a throw, release to throw"), ("Y", "Next weapon"), ("View / Menu", "Back to base / pause"),
        ("Free cam", "Left stick fly, right stick look, RT/LT zoom in/out, RB/LB focal in/out, A/X up/down, click the right stick to reset, View to leave"),
        ("In menus", "D-pad or stick to move, left/right on a slider to adjust, A to press, B to resume, LB/RB to switch tabs"),
    };
    float DefList(Draw2D d, float x, float y, float w, (string, string)[] rows)
    {
        float y0 = y, dtW = 0; var f = d.MonoSmall;
        foreach (var (a, _) in rows) dtW = Math.Max(dtW, f.Measure(a));
        dtW = Math.Min(dtW, 150);
        foreach (var (a, b) in rows)
        {
            d.Text(a, x, y, f, Pal.Hud);
            float h = d.Wrap(b, x + dtW + 14, y, w - dtW - 14, f, Pal.Text2, 1.4f);
            y += Math.Max(h, 17) + 6;
        }
        return y - y0;
    }
    float PanelControls(Draw2D d, float x, float y, float w)
    {
        float y0 = y;
        y += DefList(d, x, y, w, KEYS_HELP) + 8;
        d.Rect(x, y, w, 1, Pal.HudFaint); y += 12;
        d.Text("CONTROLLER · XBOX LAYOUT", x, y, d.DisplaySmall, Pal.Hud, "left", 2.3f); y += 22;
        y += DefList(d, x, y, w, PAD_HELP);
        // a pad user can still scroll this text with the stick: give it a focusable anchor at the bottom
        gui.Hit("ctl-end", x, y - 10, 1, 1);
        return y - y0;
    }

    // ---- Settings
    float rowY, rowX, rowW;
    void Sub(Draw2D d, string s) { rowY += 14; d.Text(s.ToUpperInvariant(), rowX, rowY, d.DisplaySmall, Pal.Hud, "left", 2); rowY += 20; }
    void RowStart(Draw2D d, string label) { d.Text(label, rowX, rowY + 5, d.MonoSmall, Pal.Hud); }
    void RowEnd(Draw2D d, string small = null)
    {
        float h = 30;
        if (small != null) { h += d.Wrap(small, rowX + 142, rowY + 28, rowW - 142, d.MonoSmall, Pal.Mute, 1.3f) + 2; }
        rowY += h; d.Rect(rowX, rowY, rowW, 1, Pal.HudFaint); rowY += 6;
    }
    bool SliderRow(Draw2D d, string id, string label, ref float v, float min, float max, float step, Func<float, string> fmt, string small = null)
    {
        RowStart(d, label);
        float sx = rowX + 142, sw = rowW - 142 - 56;
        v = gui.Slider(id, sx, rowY + 2, sw, v, min, max, step, out bool ch);
        d.Text(fmt(v), rowX + rowW, rowY + 5, d.MonoSmall, Pal.Hud, "right");
        RowEnd(d, small); return ch;
    }
    int SegRow(Draw2D d, string id, string label, string[] opts, int sel, string small = null)
    {
        RowStart(d, label); int r = gui.Seg(id, rowX + 142, rowY, opts, sel); RowEnd(d, small); return r;
    }
    static string X2(float v) => "×" + v.ToString("0.00");
    static string Pct(float v) => MathF.Round(v * 100) + "%";
    static readonly float[] DENSITY = { 0, 0.5f, 1, 1.5f, 2 };
    float PanelSettings(Draw2D d, float x, float y, float w)
    {
        rowX = x; rowY = y; rowW = w; float y0 = y; int r;
        Sub(d, "Aim and camera");
        if (SliderRow(d, "sens", "Mouse sensitivity", ref SET.sens, 0.2f, 3, 0.05f, X2)) Save();
        if (SliderRow(d, "padSens", "Controller look", ref SET.padSens, 0.3f, 2.5f, 0.05f, X2)) Save();
        if (SliderRow(d, "adsSens", "Aiming sensitivity", ref SET.adsSens, 0.15f, 1.2f, 0.05f, X2, "Multiplier while aiming down sights")) Save();
        if ((r = SegRow(d, "invert", "Invert look", new[] { "Off", "On" }, SET.invertY ? 1 : 0)) >= 0) { SET.invertY = r == 1; Save(); }
        Sub(d, "Graphics");
        if ((r = SegRow(d, "quality", "Preset", new[] { "Low", "Medium", "High", "Ultra" }, -1, "Sets everything below; tweak after")) >= 0)
        {
            (SET.res, SET.shadows, SET.drawDist, SET.decor, SET.debrisMax, SET.enemyCap) = r switch
            {
                0 => (360, 0, 110f, 1, 120, 45), 1 => (480, 1, 170f, 2, 240, 70), 2 => (720, 2, 240f, 2, 360, 90), _ => (0, 2, 280f, 2, 420, 120)
            };
            ApplyGraphics(); Save();
        }
        int[] RES = { 360, 480, 720, 1080, 0 };
        if ((r = SegRow(d, "res", "Resolution", new[] { "360p", "480p", "720p", "1080p", "Native" }, Array.IndexOf(RES, SET.res), "Lower lines keep the original chunky pixel look")) >= 0) { SET.res = RES[r]; ApplyGraphics(); Save(); }
        if ((r = SegRow(d, "shadows", "Shadows", new[] { "Off", "Low", "High" }, SET.shadows)) >= 0) { SET.shadows = r; ApplyGraphics(); Save(); }
        if ((r = SegRow(d, "decor", "Decorations", new[] { "Off", "Some", "Full" }, SET.decor, "Drifting cells, viruses and villi")) >= 0) { SET.decor = r; ApplyGraphics(); Save(); }
        if (SliderRow(d, "draw", "Draw distance", ref SET.drawDist, 80, 400, 10, v => MathF.Round(v) + " m")) Save();
        float deb = SET.debrisMax; if (SliderRow(d, "debris", "Debris pieces", ref deb, 60, 800, 20, v => MathF.Round(v).ToString())) { SET.debrisMax = (int)deb; wd_.chipMaxDyn = SET.debrisMax; Save(); }
        float cap = SET.enemyCap; if (SliderRow(d, "cap", "Max enemies", ref cap, 30, 200, 5, v => MathF.Round(v).ToString(), "Lower keeps busy fights smooth")) { SET.enemyCap = (int)cap; Save(); }
        if ((r = SegRow(d, "display", "Display", new[] { "Window", "Fullscreen" }, SET.fullscreen ? 1 : 0, "Also F11 or Alt+Enter")) >= 0) { SET.fullscreen = r == 1; ApplyGraphics(); Save(); }
        if ((r = SegRow(d, "vsync", "VSync", new[] { "Off", "On" }, SET.vsync ? 1 : 0, "Off renders as fast as the machine allows")) >= 0) { SET.vsync = r == 1; ApplyGraphics(); Save(); }
        Sub(d, "Game");
        if ((r = SegRow(d, "density", "Enemy density", new[] { "Off", "Low", "Normal", "High", "Swarm" }, Array.IndexOf(DENSITY, SET.density))) >= 0) { SET.density = DENSITY[r]; wd_.ApplyDensity(); Save(); }
        if ((r = SegRow(d, "brutes", "Brutes", new[] { "On", "Off" }, SET.brutes ? 0 : 1)) >= 0) { SET.brutes = r == 0; wd_.ApplyDensity(); Save(); }
        if (SliderRow(d, "fov", "Field of view", ref SET.fov, 50, 110, 1, v => MathF.Round(v) + "°")) Save();
        if ((r = SegRow(d, "cam", "Camera", new[] { "Third person", "First person" }, SET.fp ? 1 : 0)) >= 0) { SET.fp = r == 1; wd_.cam.pitch = SET.fp ? 0.05f : 0.32f; Save(); }
        if (SliderRow(d, "vol", "Sound", ref SET.volume, 0, 1, 0.05f, Pct)) Save();
        if (SliderRow(d, "music", "Music", ref SET.music, 0, 1, 0.05f, Pct, "FM music on shuffle (or M for the next song)")) Save();
        if (gui.Chip("skip", rowX + 142, rowY - 4, "Next song")) Music.Instance?.NextSong();
        rowY += 26;
        if (SliderRow(d, "voice", "Companion voices", ref SET.voice, 0, 1, 0.05f, Pct, Speech.Available ? "Windows speech voices" : "No Windows speech voices found; subtitles only")) Save();
        if ((r = SegRow(d, "subs", "Voice subtitles", new[] { "Off", "On" }, SET.subs ? 1 : 0)) >= 0) { SET.subs = r == 1; Save(); }
        foreach (var (key, type, name, blurb) in new[] { ("companion", "ally", "Tab", "Companion · marksman: hangs back in cover, picks off snipers and whatever you're shooting"), ("companion2", "ally2", "Cap", "Companion · tank: wades in, body-blocks for you, smashes groups, throws heavy things"), ("companion3", "ally3", "Jab", "Companion · scout: flanks, hunts assassins, watches your back") })
        {
            bool on = key == "companion" ? SET.companion : key == "companion2" ? SET.companion2 : SET.companion3;
            if ((r = SegRow(d, key, name, new[] { "Off", "On" }, on ? 1 : 0, blurb)) >= 0)
            {
                bool v = r == 1; if (key == "companion") SET.companion = v; else if (key == "companion2") SET.companion2 = v; else SET.companion3 = v;
                if (v) wd_.SpawnCompanion(type); else wd_.RemoveCompanion(type); Save();
            }
        }
        Sub(d, "Files");
        d.Wrap("Saves, settings, key bindings and your germ live in " + Paths.Saves + ". Game data (music, songs) lives in " + Paths.Data + ".", rowX, rowY, rowW, d.MonoSmall, Pal.Mute, 1.35f);
        rowY += 48;
        float cx = rowX;
        if (gui.Chip("osaves", cx, rowY, "Open saves folder")) Paths.Reveal(Paths.Saves); cx += gui.lastW + 6;
        if (gui.Chip("odata", cx, rowY, "Open data folder")) Paths.Reveal(Paths.Data); cx += gui.lastW + 6;
        if (gui.Chip("oshots", cx, rowY, "Open screenshots")) Paths.Reveal(Paths.Screenshots);
        rowY += 34;
        return rowY - y0;
    }

    // ---- Keys
    float PanelKeys(Draw2D d, float x, float y, float w)
    {
        float y0 = y;
        y += d.Wrap("Click a key slot, then press a key or mouse button. Backspace clears the slot, Esc cancels. Controller buttons stay as listed under Controls.", x, y, w, d.MonoSmall, Pal.Text, 1.4f) + 10;
        var used = new Dictionary<string, int>(); foreach (var b in wd_.BINDS.Values) foreach (var k in b) if (!string.IsNullOrEmpty(k)) used[k] = used.GetValueOrDefault(k) + 1;
        foreach (var (key, name, _, _) in World.BIND_DEF)
        {
            d.Text(name, x, y + 5, d.MonoSmall, Pal.Text2);
            for (int i = 0; i < 2; i++)
            {
                string code = wd_.BINDS[key][i]; bool wait = waiting == key && waitingI == i;
                string lab = wait ? ((int)(wd_.clockT * 4) % 2 == 0 ? "press a key…" : "") : KeyName(code);
                float bx = x + w - 2 * 112 + i * 112;
                var col = wait ? Pal.Warn : (!string.IsNullOrEmpty(code) && used.GetValueOrDefault(code) > 1 && !(key is "up" or "down" or "jump" or "grab" or "roll")) ? Pal.Hot : Pal.Hud;
                if (gui.Chip("bind:" + key + ":" + i, bx, y, lab, wait, col, 104)) { waiting = key; waitingI = i; Input.LastPressed = null; }
            }
            y += 30;
        }
        y += 6;
        if (gui.Chip("bindReset", x, y, "Reset keys to defaults")) { foreach (var b in World.BIND_DEF) wd_.BINDS[b.key] = new[] { b.a, b.b }; Prefs.SaveBinds(); }
        y += 34;
        return y - y0;
    }
    static string KeyName(string c)
    {
        if (string.IsNullOrEmpty(c)) return "—";
        if (c.StartsWith("Mouse")) return c switch { "Mouse0" => "Left click", "Mouse1" => "Middle click", "Mouse2" => "Right click", "Mouse3" => "Mouse 4", "Mouse4" => "Mouse 5", _ => c };
        return c switch { "ArrowUp" => "↑", "ArrowDown" => "↓", "ArrowLeft" => "←", "ArrowRight" => "→", "ShiftLeft" => "L Shift", "ShiftRight" => "R Shift", "ControlLeft" => "L Ctrl", "ControlRight" => "R Ctrl", "AltLeft" => "L Alt", "AltRight" => "R Alt", _ => c.StartsWith("Key") ? c[3..] : c.StartsWith("Digit") ? c[5..] : c };
    }

    // ---- Debug
    float PanelDebug(Draw2D d, float x, float y, float w)
    {
        float y0 = y, h; int r;
        void Sub2(string s) { y += 10; d.Text(s.ToUpperInvariant(), x, y, d.DisplaySmall, Pal.Hud, "left", 2); y += 20; }
        Sub2("Spawn enemy · in front of you");
        string[] ek = { "small", "normal", "assassin", "brute", "mech", "boss", "chairman", "squad", "dropship", "clear", "freeze" };
        string[] en = { "4 cocci", "Bacillus trooper", "Spirillum assassin", "Amoeba brute", "Phage titan", "Boss (mimic)", "Virus rider", "Full squad", "Spore carrier drop", "Clear all", "Freeze AI: " + (wd_.aiFrozen ? "on" : "off") };
        if ((r = gui.Chips("dbgE", x, y, w, en, i => false, out h)) >= 0) note = wd_.DebugEnemy(ek[r]); y += h;
        Sub2("Spawn vehicle · parked in front of you");
        string[] vk = { "bike", "rover", "chair", "mecha" };
        if ((r = gui.Chips("dbgV", x, y, w, new[] { "Platelet bike", "Blood cell", "Virus", "Med-Mech" }, i => false, out h)) >= 0) note = wd_.DebugVehicle(vk[r]); y += h;
        Sub2("Camera");
        if (gui.Chip("freecam", x, y, "Free cam")) { Deploy(); wd_.SetFreeCam(true); }
        y += 30; y += d.Wrap("Free cam: left stick or WASD to fly, right stick or mouse to look, RT / LT (or wheel, + / −) zoom in and out, RB / LB (or ] / [) focal length in and out, A / X (or Space / C) up and down, click the right stick (or R) to reset, Back or V to leave.", x, y, w, d.MonoSmall, Pal.Mute, 1.3f) + 4;
        Sub2("Spawn object");
        if ((r = gui.Chips("dbgO", x, y, w, World.DEBUG_OBJECTS.Select(o => o.name).ToList(), i => false, out h)) >= 0) note = wd_.DebugObject(World.DEBUG_OBJECTS[r].key); y += h;
        Sub2("Tuning");
        rowX = x; rowY = y; rowW = w;
        bool t = false;
        t |= SliderRow(d, "dspeed", "Player speed", ref SET.speed, 0.5f, 2, 0.05f, X2);
        t |= SliderRow(d, "dtaken", "Damage taken", ref SET.dmgTaken, 0, 3, 0.05f, X2, "0 is god mode");
        t |= SliderRow(d, "ddealt", "Damage dealt", ref SET.dmgDealt, 0.25f, 4, 0.05f, X2, "How easily enemy parts come off");
        t |= SliderRow(d, "dgrav", "Gravity", ref SET.grav, 0.25f, 2, 0.05f, X2);
        t |= SliderRow(d, "dtime", "Time scale", ref SET.timeScale, 0.2f, 1.5f, 0.05f, X2);
        if (t) wd_.ApplyTuningPublic();
        y = rowY + 6;
        float cx = x;
        if (gui.Chip("dreset", cx, y, "Reset tuning")) { SET.speed = SET.dmgTaken = SET.dmgDealt = SET.grav = SET.timeScale = 1; wd_.ApplyTuningPublic(); note = "Tuning back to defaults."; }
        cx += gui.lastW + 6;
        if (gui.Chip("dheal", cx, y, "Refill shield and ammo")) note = wd_.DebugHeal();
        y += 34;
        if (note.Length > 0) y += d.Wrap(note, x, y, w, d.MonoSmall, Pal.Warn, 1.35f) + 6;
        return y - y0;
    }
}

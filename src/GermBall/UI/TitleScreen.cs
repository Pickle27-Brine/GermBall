using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using GermBall.Game;
using GermBall.Platform;
using GermBall.Render;

namespace GermBall.UI;

/// <summary>
/// The boarding screen. Behind it, pre-rendered flyovers of the maps drift and crossfade (the one you're pointing at comes
/// forward); the medicine cast stands along the bottom; George Lopez looks up a fun fact every time you flip him.
/// Pick a mode and a map, and the world is generated on a worker thread while the screen stays live.
/// </summary>
public sealed class TitleScreen : IScreen
{
    sealed class BootPrefs { public string smap = "random", wmap = "bowl", seed = ""; }
    static readonly (string key, string name, string blurb)[] SMAPS = World.SANDBOX_MAPS;
    static readonly (string key, string name, string blurb)[] WMAPS = World.WAVE_MAPS;

    readonly App app; readonly Gui gui = new(); readonly BootPrefs boot;
    string loading = ""; Task<World> building; bool seedFocus; bool lopezFlip; float lopezK; float T;
    static Texture vigTex, lopezTex, castTex; static readonly Dictionary<string, Texture> mapTex = new();
    public static GermCustom Custom;
    CustomizeScreen custom;
    // backdrop: which map still is showing, and the one fading in
    string bgCur, bgNext; float bgFade = 1, bgT, bgCycle;
    static readonly string[] BG_ORDER = { "random", "bowl", "leviathan", "city", "classic", "spires", "gut" };
    // George
    string fact = "Flip me for a fun fact."; Task<string> factTask; float factT;

    public TitleScreen(App app)
    {
        this.app = app;
        boot = Paths.Load<BootPrefs>(Paths.Boot) ?? new BootPrefs();
        if (string.IsNullOrWhiteSpace(boot.seed)) boot.seed = new Random().Next(99999).ToString();
        Custom ??= Paths.Load<GermCustom>(Paths.Custom) ?? GermDraw.DefaultCustom();
        Input.Lock(app.Window, false);
        vigTex ??= MakeVignette();
        lopezTex ??= Png.Load(Path.Combine(Paths.Data, "ui", "lopez.png"));
        castTex ??= Png.Load(Path.Combine(Paths.Data, "ui", "cast.png"));
        foreach (var k in BG_ORDER) if (!mapTex.ContainsKey(k)) { var t = Png.Load(Path.Combine(Paths.Data, "ui", "maps", k + ".png")); if (t != null) mapTex[k] = t; }
        bgCur = mapTex.ContainsKey(boot.smap) ? boot.smap : BG_ORDER[0];
        Sound.Init();
        if (Music.Instance == null) { var m = Music.Load(Path.Combine(Paths.Data, "music")); app.Audio.Add(m); }
        var S = Prefs.Settings; app.Audio.Master = 1; app.Audio.SfxVolume = S.volume; app.Audio.MusicVolume = S.music;
        if (app.Window.Fullscreen != S.fullscreen) app.Window.SetFullscreen(S.fullscreen);
        app.Renderer.VSync = S.vsync;
    }

    static Texture MakeVignette()
    {
        const int N = 128; var t = new Texture(N, N) { Nearest = false, Mipmaps = false };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x - N / 2f) / (N / 2f), dy = (y - N / 2f) / (N / 2f), d = MathF.Sqrt(dx * dx + dy * dy) / 1.414f;
                float k = Math.Clamp((d - 0.25f) / 0.6f, 0, 1); int o = (y * N + x) * 4;
                t.Pixels[o] = 10; t.Pixels[o + 1] = 2; t.Pixels[o + 2] = 20; t.Pixels[o + 3] = (byte)(k * 0.9f * 255);
            }
        return t;
    }

    void Start(BootConfig cfg)
    {
        if (building != null) return;
        cfg.Custom = Custom.Clone();
        Paths.Save(Paths.Boot, boot);
        loading = "GENERATING WORLD…";
        var set = Prefs.Settings; var binds = Prefs.Binds;
        building = Task.Run(() => new World(cfg, set, binds));
    }
    int SeedNum()
    {
        var v = boot.seed.Trim(); if (int.TryParse(v, out int h)) return Math.Abs(h) == 0 ? 1 : Math.Abs(h);
        h = 0; foreach (char ch in v) h = unchecked(h * 31 + ch); return Math.Abs(h) == 0 ? 1 : Math.Abs(h);
    }
    void ShowMap(string key) { if (key == null || !mapTex.ContainsKey(key) || key == bgCur || key == bgNext) return; bgNext = key; bgFade = 0; }
    void FlipGeorge()
    {
        lopezFlip = !lopezFlip; Sound.Ui("boing", 0.6f);
        if (factTask == null || factTask.IsCompleted) { fact = "Thinking…"; factTask = FunFacts.Next(); }
    }

    public void Update(float dt)
    {
        T += dt;
        if (custom != null) { custom.Update(dt); if (custom.Done) { Custom = custom.C; custom = null; gui.Focus = "openCustom"; } return; }
        if (building != null && building.IsCompleted)
        {
            if (building.IsFaulted)
            {
                var e = building.Exception?.InnerException ?? building.Exception; Log.Error(e?.ToString());
                loading = "COULD NOT BUILD THE WORLD: " + e?.Message; building = null;
            }
            else { app.Screen = new GameScreen(app, building.Result); return; }
        }
        if (seedFocus && building == null)
        {
            foreach (char ch in Input.Typed.ToString()) if (!char.IsControl(ch) && boot.seed.Length < 16) boot.seed += ch;
            if (Input.Pressed("Backspace") && boot.seed.Length > 0) boot.seed = boot.seed[..^1];
        }
        lopezK += ((lopezFlip ? 1 : 0) - lopezK) * Math.Min(1, dt * 9);
        if (factTask != null && factTask.IsCompleted) { fact = factTask.Result; factTask = null; factT = 0; }
        factT += dt;
        // the backdrop: crossfade, and drift through the maps on its own when you leave it alone
        bgT += dt; bgCycle += dt;
        if (bgNext != null) { bgFade += dt * 1.2f; if (bgFade >= 1) { bgCur = bgNext; bgNext = null; bgT = 0; } }
        else if (bgCycle > 9) { bgCycle = 0; int i = Array.IndexOf(BG_ORDER, bgCur); ShowMap(BG_ORDER[(i + 1) % BG_ORDER.Length]); }
    }
    public void Render(float dt) { if (custom != null) custom.Render(dt); else app.Renderer.Render(null, null); }

    void DrawMapStill(Draw2D d, string key, float Wd, float Ht, float alpha, float t)
    {
        if (key == null || !mapTex.TryGetValue(key, out var tex)) return;
        // a slow push-in and pan (Ken Burns), cover-fitted to the window
        float aspect = tex.Width / (float)tex.Height, w = MathF.Max(Wd, Ht * aspect), h = w / aspect; if (h < Ht) { h = Ht; w = h * aspect; }
        float z = 1.06f + 0.06f * (t / 9f), px = MathF.Sin(t * 0.07f) * 0.03f, py = MathF.Cos(t * 0.05f) * 0.02f;
        w *= z; h *= z; float x = (Wd - w) / 2 + px * Wd, y = (Ht - h) / 2 + py * Ht;
        d.Alpha = alpha; d.Image(tex, x, y, w, h); d.Alpha = 1;
    }

    public void DrawUI(Draw2D d, float dt)
    {
        if (custom != null) { custom.DrawUI(d, dt); return; }
        float Wd = app.UiW, Ht = app.UiH;
        gui.Begin(d, dt);
        // ---- backdrop
        d.Rect(0, 0, Wd, Ht, Draw2D.Col(0x12061f));
        DrawMapStill(d, bgCur, Wd, Ht, 1, bgT + 9);
        if (bgNext != null) DrawMapStill(d, bgNext, Wd, Ht, Math.Clamp(bgFade, 0, 1), bgFade * 1.2f);
        d.Rect(0, 0, Wd, Ht, new Vector4(26 / 255f, 6 / 255f, 30 / 255f, 0.38f));
        // a dark wash on the left for the panels
        for (int i = 0; i < 24; i++) { float k = 1 - i / 24f; d.Rect(i * Wd * 0.03f, 0, Wd * 0.03f, Ht, new Vector4(14 / 255f, 4 / 255f, 22 / 255f, 0.55f * k)); }
        for (float sl = 0; sl < Ht; sl += 4) d.Rect(0, sl, Wd, 1.5f, new Vector4(0, 0, 0, 0.22f));
        d.Image(vigTex, 0, 0, Wd, Ht);
        // ---- the cast along the bottom right, breathing
        if (castTex != null)
        {
            float cw = MathF.Min(Wd * 0.5f, 700), ch = cw * castTex.Height / castTex.Width, cx = Wd - cw - 24, cy = Ht - ch - 14 + MathF.Sin(T * 1.3f) * 2;
            d.Alpha = 0.95f; d.Image(castTex, cx, cy, cw, ch); d.Alpha = 1;
        }
        bool busy = building != null;
        // ---- left column
        float x = 40, colW = MathF.Min(560, Wd * 0.46f), y = Math.Max(24, Ht * 0.06f);
        var lf = d.DisplayHuge; float glitch = (T % 4.3f) < 0.09f ? 6 : 0;
        d.Text("GERM BALL", x + 4 + glitch, y + 4, lf, Draw2D.Col(0xff3cac), "left", 2);
        d.Text("GERM BALL", x - 3 - glitch, y - 2, lf, Draw2D.Col(0x1fb5ff), "left", 2);
        d.Text("GERM BALL", x, y, lf, Draw2D.Col(0xfff4fd), "left", 2);
        y += lf.LineHeight * 0.92f;
        d.Text("MEDICINE VS GERMS · ROLL, SHOOT, SQUASH", x, y + 10, d.MonoSmall, Pal.Hud, "left", 3);
        y += 36;
        float bw = colW;
        if (gui.Big("openCustom", x, y, bw, 42, "Build your hero", Pal.White, new Vector4(1, 60 / 255f, 172 / 255f, 0.55f)) && !busy) { custom = new CustomizeScreen(app, Custom); return; }
        y += 54;
        float modeH = 392, half = (colW - 12) / 2;
        bool two = colW >= 520;
        float mw = two ? half : colW;
        ModeSandbox(d, x, y, mw, modeH, busy);
        if (two) ModeWaves(d, x + mw + 12, y, mw, modeH, busy); else ModeWaves(d, x, y + modeH + 12, mw, modeH, busy);
        y += (two ? modeH : modeH * 2 + 12) + 16;
        if (loading.Length > 0) d.Text(loading, x, y, d.DisplayMid, loading.StartsWith("COULD") ? Pal.Hot : Pal.Hud, "left", 5);
        d.Text("Mouse and keyboard or a controller · F11 fullscreen · Esc quits", x, Ht - 22, d.MonoSmall, Pal.Mute, "left", 1);
        // ---- George, top right, with a fact
        DrawGeorge(d, Wd, Ht);
        if (gui.Focus == null && gui.NavUsed) gui.Focus = "goSandbox";
        if (Input.Pressed("Escape") && !seedFocus && !busy) app.Window.Close();
        if (seedFocus && (Input.Pressed("Escape") || Input.Pressed("Enter"))) seedFocus = false;
    }

    void DrawGeorge(Draw2D d, float Wd, float Ht)
    {
        if (lopezTex == null) return;
        float lw = 64 + 6, lh = 64f * lopezTex.Height / lopezTex.Width + 6, lx = Wd - lw - 28, ly = 24;
        if (gui.Hit("lopez", lx, ly, lw, lh)) FlipGeorge();
        float k = 1 - 2 * lopezK, mx = lx + lw / 2, hw = MathF.Abs(k) * 32;
        d.Rect(lx, ly, lw, lh, Pal.White); d.Frame(lx, ly, lw, lh, Pal.HudDim);
        float u0 = k >= 0 ? 0 : 1, u1 = k >= 0 ? 1 : 0;
        d.Image(lopezTex, mx - hw, ly + 3, hw * 2, lh - 6, null, u0, 0, u1, 1);
        if (gui.IsFocused("lopez")) d.Frame(lx - 3, ly - 3, lw + 6, lh + 6, Pal.White, 2);
        // the speech bubble to his left
        var f = d.MonoSmall; float bwid = MathF.Min(300, Wd * 0.32f), pad = 10;
        string shown = factTask != null ? "Thinking…" : fact;
        float th = MeasureWrap(f, shown, bwid - pad * 2) + 22, bx = lx - bwid - 14, by = ly + 6;
        float pop = Math.Clamp(factT * 6, 0, 1);
        d.Alpha = 0.3f + 0.7f * pop;
        d.Rect(bx, by, bwid, th, new Vector4(0.96f, 0.95f, 1, 0.94f));
        d.Tri(new Vector2(bx + bwid, by + 14), new Vector2(bx + bwid + 12, by + 20), new Vector2(bx + bwid, by + 26), new Vector4(0.96f, 0.95f, 1, 0.94f));
        d.Text("GEORGE SAYS", bx + pad, by + 6, d.DisplaySmall, Draw2D.Col(0xd0306a), "left", 2);
        d.Wrap(shown, bx + pad, by + 22, bwid - pad * 2, f, Draw2D.Col(0x1a0c2e), 1.35f);
        d.Alpha = 1;
    }
    static float MeasureWrap(UiFont f, string s, float width)
    {
        float lh = f.Size * 1.35f, h = lh; string line = "";
        foreach (var wd in s.Split(' ')) { var t = line.Length == 0 ? wd : line + " " + wd; if (f.Measure(t) > width && line.Length > 0) { h += lh; line = wd; } else line = t; }
        return h;
    }

    void ModeSandbox(Draw2D d, float x, float y, float w, float h, bool busy)
    {
        gui.CutPanel(x, y, w, h, 16, Pal.Panel, Pal.HudDim);
        float ix = x + 16, iw = w - 32, cy = y + 16;
        d.Text("SANDBOX", ix, cy, d.DisplayBig, Pal.White, "left", 2.5f); cy += 40;
        cy += d.Wrap("Roam a living body. Germ squads patrol, garrison and roam between their colonies.", ix, cy, iw, d.MonoSmall, Pal.Text, 1.45f) + 6;
        foreach (var (k, n, b) in SMAPS) { if (Opt(d, "s:" + k, ix, cy, iw, n, b, boot.smap == k) && !busy) boot.smap = k; cy += 48; }
        bool on = boot.smap == "random"; float a = on ? 1 : 0.4f;
        d.Text("Seed", ix, cy + 6, d.MonoSmall, Pal.A(Pal.Mute, a));
        float fx = ix + 40, fw = Math.Min(110, iw - 110);
        if (gui.Hit("seed", fx, cy, fw, 26) && on && !busy) seedFocus = true;
        if (gui.MouseDown && !gui.Hover(fx, cy, fw, 26)) seedFocus = false;
        d.Rect(fx, cy, fw, 26, new Vector4(0, 0, 0, 0.35f)); d.Frame(fx, cy, fw, 26, seedFocus ? Pal.Warn : Pal.HudDim);
        d.Text(boot.seed + (seedFocus && ((int)(T * 2) & 1) == 0 ? "_" : ""), fx + 7, cy + 6, d.Mono, Pal.A(Pal.Warn, a));
        if (gui.Chip("reroll", fx + fw + 6, cy + 1, "Reroll") && on && !busy) boot.seed = new Random().Next(99999).ToString();
        if (gui.IsFocused("seed") && gui.NavUsed && on) seedFocus = true;
        if (gui.Big("goSandbox", ix, y + h - 16 - 42, iw, 42, "Generate") && !busy) Start(new BootConfig { Mode = "sandbox", Map = boot.smap, Seed = SeedNum() });
    }
    void ModeWaves(Draw2D d, float x, float y, float w, float h, bool busy)
    {
        gui.CutPanel(x, y, w, h, 16, Pal.Panel, Pal.HudDim);
        float ix = x + 16, iw = w - 32, cy = y + 16;
        d.Text("WAVES", ix, cy, d.DisplayBig, Pal.White, "left", 2.5f); cy += 40;
        cy += d.Wrap("Hold the base while bigger squads come at you.", ix, cy, iw, d.MonoSmall, Pal.Text, 1.45f) + 6;
        foreach (var (k, n, b) in WMAPS)
        {
            int best = Saves.Best(k);
            if (Opt(d, "w:" + k, ix, cy, iw, n, best > 0 ? "Best " + best + " · " + b : b, boot.wmap == k) && !busy) boot.wmap = k;
            cy += 48;
        }
        if (gui.Big("goWaves", ix, y + h - 16 - 42, iw, 42, "Start waves") && !busy) Start(new BootConfig { Mode = "waves", Map = boot.wmap, Seed = 7 });
    }
    bool Opt(Draw2D d, string id, float x, float y, float w, string name, string blurb, bool on)
    {
        float h = 42; bool hov = gui.Hover(x, y, w, h), hit = gui.Hit(id, x, y, w, h);
        if (hov || gui.IsFocused(id)) ShowMap(id[2..]);
        d.Rect(x, y, w, h, on ? Pal.ChipOn : hov ? Draw2D.Col(0x8fe3ff, 0.14f) : Pal.Chip); d.Frame(x, y, w, h, on ? Pal.Warn : Pal.HudDim);
        d.Text(name.ToUpperInvariant(), x + 10, y + 4, d.Display, on ? Pal.Warn : Pal.Hud, "left", 2);
        string bl = blurb; var f = d.MonoSmall; while (bl.Length > 4 && f.Measure(bl) > w - 20) bl = bl[..^2].TrimEnd() + "…";
        if (bl.EndsWith("……")) bl = bl[..^1];
        d.Text(bl, x + 10, y + 24, f, Pal.Mute);
        if (gui.IsFocused(id)) d.Frame(x - 3, y - 3, w + 6, h + 6, Pal.White, 2);
        return hit;
    }
}

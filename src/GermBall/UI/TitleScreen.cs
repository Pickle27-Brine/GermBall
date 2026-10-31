using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using GermBall.Game;
using GermBall.Platform;
using GermBall.Render;

namespace GermBall.UI;

/// <summary>The boarding screen: pick a mode and a map, then the world is generated (on a worker thread, so the screen stays live).</summary>
public sealed class TitleScreen : IScreen
{
    sealed class BootPrefs { public string smap = "random", wmap = "bowl", seed = ""; }
    static readonly (string key, string name, string blurb)[] SMAPS =
    {
        ("random", "Random world", "Generated from a seed: terrain, towns, camps, canyons, colours"),
        ("classic", "Classic range", "The original hand-laid map"),
        ("leviathan", "Inside the leviathan", "The guts of something enormous and alive: organs, acid, ribs, cells"),
    };
    static readonly (string key, string name, string blurb)[] WMAPS =
    {
        ("bowl", "Spore Bowl", "A glowing basin ringed by four ruined hamlets"), ("city", "Glass City", "Dense towers and alleys; fights around corners"),
        ("spires", "Spire Garden", "Needle peaks, two old forts and a canyon"), ("gut", "The Gullet", "A stomach chamber inside the leviathan, round an acid pit"),
    };

    readonly App app; readonly Gui gui = new(); readonly BootPrefs boot;
    string loading = ""; Task<World> building; bool seedFocus; bool lopezFlip; float lopezK;
    static Texture bgTex, vigTex, lopezTex; float T;
    public static GermCustom Custom;
    CustomizeScreen custom;

    public TitleScreen(App app)
    {
        this.app = app;
        boot = Paths.Load<BootPrefs>(Paths.Boot) ?? new BootPrefs();
        if (string.IsNullOrWhiteSpace(boot.seed)) boot.seed = new Random().Next(99999).ToString();
        Custom ??= Paths.Load<GermCustom>(Paths.Custom) ?? GermDraw.DefaultCustom();
        Input.Lock(app.Window, false);
        bgTex ??= MakeBackground(); vigTex ??= MakeVignette();
        lopezTex ??= Png.Load(Path.Combine(Paths.Data, "ui", "lopez.png"));
        Sound.Init();
        if (Music.Instance == null) { var m = Music.Load(Path.Combine(Paths.Data, "music")); app.Audio.Add(m); }
        var S = Prefs.Settings; app.Audio.Master = 1; app.Audio.SfxVolume = S.volume; app.Audio.MusicVolume = S.music;
        if (app.Window.Fullscreen != S.fullscreen) app.Window.SetFullscreen(S.fullscreen);
        app.Renderer.VSync = S.vsync;
    }

    // the conic rainbow behind the title, pre-blurred by being tiny and linearly filtered
    static Texture MakeBackground()
    {
        uint[] stops = { 0xff3cac, 0x7a1fa2, 0x1fb5ff, 0x3cffb4, 0xffe66d, 0xff9a3c, 0xff3cac };
        const int N = 64; var t = new Texture(N, N) { Nearest = false, Mipmaps = false };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float a = MathF.Atan2(x - N / 2f + 0.5f, -(y - N / 2f + 0.5f)); if (a < 0) a += MathF.Tau;
                float u = a / MathF.Tau * 6; int i = Math.Min(5, (int)u); float f = u - i;
                var c0 = Material.Hex(stops[i]); var c1 = Material.Hex(stops[i + 1]); var c = Vector3.Lerp(c0, c1, f);
                // saturate(1.3)
                float l = (c.X + c.Y + c.Z) / 3; c = Vector3.Clamp(new Vector3(l) + (c - new Vector3(l)) * 1.3f, Vector3.Zero, Vector3.One);
                int o = (y * N + x) * 4; t.Pixels[o] = (byte)(c.X * 255); t.Pixels[o + 1] = (byte)(c.Y * 255); t.Pixels[o + 2] = (byte)(c.Z * 255); t.Pixels[o + 3] = 255;
            }
        return t;
    }
    static Texture MakeVignette()
    {
        const int N = 128; var t = new Texture(N, N) { Nearest = false, Mipmaps = false };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x - N / 2f) / (N / 2f), dy = (y - N / 2f) / (N / 2f), d = MathF.Sqrt(dx * dx + dy * dy) / 1.414f;
                float k = Math.Clamp((d - 0.3f) / 0.5f, 0, 1); int o = (y * N + x) * 4;
                t.Pixels[o] = 10; t.Pixels[o + 1] = 2; t.Pixels[o + 2] = 20; t.Pixels[o + 3] = (byte)(k * 0.92f * 255);
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
    }
    public void Render(float dt) { if (custom != null) custom.Render(dt); else app.Renderer.Render(null, null); }

    public void DrawUI(Draw2D d, float dt)
    {
        if (custom != null) { custom.DrawUI(d, dt); return; }
        float Wd = app.Renderer.Width, Ht = app.Renderer.Height;
        gui.Begin(d, dt);
        // background: spinning blurred rainbow, scanlines, vignette
        d.Rect(0, 0, Wd, Ht, Draw2D.Col(0x12061f));
        {
            float a = T / 38 * MathF.Tau, R = MathF.Max(Wd, Ht) * 1.2f, cx = Wd / 2, cy = Ht * 0.55f;
            Vector2 C(float k) => new(cx + MathF.Cos(a + k) * R, cy + MathF.Sin(a + k) * R);
            d.Alpha = 0.55f; d.Quad(C(-MathF.PI * 0.75f), C(-MathF.PI * 0.25f), C(MathF.PI * 0.25f), C(MathF.PI * 0.75f), Vector4.One, bgTex, 0.2f, 0.2f, 0.8f, 0.8f); d.Alpha = 1;
            d.Rect(0, 0, Wd, Ht, new Vector4(18 / 255f, 6 / 255f, 31 / 255f, 0.25f));
            for (float sl = 0; sl < Ht; sl += 4) d.Rect(0, sl, Wd, 2, new Vector4(0, 0, 0, 0.32f));
            d.Image(vigTex, 0, 0, Wd, Ht);
        }
        float cw = Math.Min(980, Wd - 32), x = Wd / 2 - cw / 2;
        bool two = cw >= 600; float modeW = two ? (cw - 16) / 2 : cw;
        float modeH = 380, contentH = 120 + 40 + 72 + (two ? modeH : modeH * 2 + 16) + 80;
        float y = Math.Max(24, Ht / 2 - contentH / 2);
        // logo
        var lf = d.DisplayHuge;
        d.Text("GERM BALL", x + 4, y + 4, lf, Draw2D.Col(0xff3cac), "left", 2);
        d.Text("GERM BALL", x - 3, y - 2, lf, Draw2D.Col(0x1fb5ff), "left", 2);
        d.Text("GERM BALL", x, y, lf, Draw2D.Col(0xfff4fd), "left", 2);
        y += lf.LineHeight * 0.92f;
        d.Text("A MICROSCOPIC WAR · DESTRUCTIBLE MICROBES · ROLL, SHOOT, SQUASH", x, y + 14, d.MonoSmall, Pal.Hud, "left", 2.8f);
        y += 48;
        // customize + George
        bool busy = building != null;
        float bx = x, bw = d.Display.Measure("CUSTOMIZE YOUR GERM", 2.5f) + 44;
        if (gui.Big("openCustom", bx, y, bw, 44, "Customize your germ", Pal.White, new Vector4(1, 60 / 255f, 172 / 255f, 0.55f)) && !busy) { custom = new CustomizeScreen(app, Custom); return; }
        if (lopezTex != null)
        {
            float lw = 58 + 6, lh = 58f * lopezTex.Height / lopezTex.Width + 6, lx = bx + bw + 14, ly = y + 22 - lh / 2;
            if (gui.Hit("lopez", lx, ly, lw, lh)) lopezFlip = !lopezFlip;
            float k = 1 - 2 * lopezK, mx = lx + lw / 2;   // scaleX(-1) flip
            d.Rect(lx, ly, lw, lh, Pal.White); d.Frame(lx, ly, lw, lh, Pal.HudDim);
            float u0 = k >= 0 ? 0 : 1, u1 = k >= 0 ? 1 : 0, hw = MathF.Abs(k) * 29;
            d.Image(lopezTex, mx - hw, ly + 3, hw * 2, lh - 6, null, u0, 0, u1, 1);
            if (gui.IsFocused("lopez")) d.Frame(lx - 3, ly - 3, lw + 6, lh + 6, Pal.White, 2);
        }
        y += 44 + 18;
        // the two modes
        float my = y;
        ModeSandbox(d, x, my, modeW, modeH, busy);
        if (two) ModeWaves(d, x + modeW + 16, my, modeW, modeH, busy); else ModeWaves(d, x, my + modeH + 16, modeW, modeH, busy);
        y = my + (two ? modeH : modeH * 2 + 16) + 22;
        if (loading.Length > 0) d.Text(loading, Wd / 2, y, d.DisplayMid, loading.StartsWith("COULD") ? Pal.Hot : Pal.Hud, "center", 6);
        d.Text("Mouse and keyboard or a controller (D-pad and A on this screen) · F11 fullscreen · Esc quits", x, y + 40, d.MonoSmall, Pal.Mute, "left", 1);
        if (gui.Focus == null && gui.NavUsed) gui.Focus = "goSandbox";
        if (Input.Pressed("Escape") && !seedFocus && !busy) app.Window.Close();
        if (seedFocus && (Input.Pressed("Escape") || Input.Pressed("Enter"))) seedFocus = false;
    }

    void ModeSandbox(Draw2D d, float x, float y, float w, float h, bool busy)
    {
        gui.CutPanel(x, y, w, h, 18, Pal.Panel, Pal.HudDim);
        float ix = x + 20, iw = w - 40, cy = y + 20;
        d.Text("SANDBOX", ix, cy, d.DisplayBig, Pal.White, "left", 2.5f); cy += 42;
        cy += d.Wrap("Roam a living world of colonies and parasite nests. Germ squads patrol, garrison and roam; take them on however you like.", ix, cy, iw, d.MonoSmall, Pal.Text, 1.5f) + 6;
        foreach (var (k, n, b) in SMAPS) { if (Opt(d, "s:" + k, ix, cy, iw, n, b, boot.smap == k) && !busy) boot.smap = k; cy += 50; }
        // seed row
        bool on = boot.smap == "random"; float a = on ? 1 : 0.4f;
        d.Text("Seed", ix, cy + 6, d.MonoSmall, Pal.A(Pal.Mute, a));
        float fx = ix + 40, fw = 110;
        if (gui.Hit("seed", fx, cy, fw, 26) && on && !busy) seedFocus = true;
        if (gui.MouseDown && !gui.Hover(fx, cy, fw, 26)) seedFocus = false;
        d.Rect(fx, cy, fw, 26, new Vector4(0, 0, 0, 0.35f)); d.Frame(fx, cy, fw, 26, seedFocus ? Pal.Warn : Pal.HudDim);
        d.Text(boot.seed + (seedFocus && ((int)(T * 2) & 1) == 0 ? "_" : ""), fx + 7, cy + 6, d.Mono, Pal.A(Pal.Warn, a));
        if (gui.Chip("reroll", fx + fw + 6, cy + 1, "Reroll") && on && !busy) boot.seed = new Random().Next(99999).ToString();
        if (gui.IsFocused("seed") && gui.NavUsed && on) seedFocus = true;
        if (gui.Big("goSandbox", ix, y + h - 20 - 44, iw, 44, "Generate") && !busy) Start(new BootConfig { Mode = "sandbox", Map = boot.smap, Seed = SeedNum() });
    }
    void ModeWaves(Draw2D d, float x, float y, float w, float h, bool busy)
    {
        gui.CutPanel(x, y, w, h, 18, Pal.Panel, Pal.HudDim);
        float ix = x + 20, iw = w - 40, cy = y + 20;
        d.Text("WAVES", ix, cy, d.DisplayBig, Pal.White, "left", 2.5f); cy += 42;
        cy += d.Wrap("Hold the base in a closed arena while bigger and stranger waves of squads come at you. Racks and stashes restock between waves.", ix, cy, iw, d.MonoSmall, Pal.Text, 1.5f) + 6;
        foreach (var (k, n, b) in WMAPS)
        {
            int best = Saves.Best(k);
            if (Opt(d, "w:" + k, ix, cy, iw, n, b + (best > 0 ? " · best " + best : ""), boot.wmap == k) && !busy) boot.wmap = k;
            cy += 50;
        }
        if (gui.Big("goWaves", ix, y + h - 20 - 44, iw, 44, "Start waves") && !busy) Start(new BootConfig { Mode = "waves", Map = boot.wmap, Seed = 7 });
    }
    bool Opt(Draw2D d, string id, float x, float y, float w, string name, string blurb, bool on)
    {
        float h = 44; bool hov = gui.Hover(x, y, w, h), hit = gui.Hit(id, x, y, w, h);
        d.Rect(x, y, w, h, on ? Pal.ChipOn : hov ? Draw2D.Col(0x8fe3ff, 0.14f) : Pal.Chip); d.Frame(x, y, w, h, on ? Pal.Warn : Pal.HudDim);
        d.Text(name.ToUpperInvariant(), x + 10, y + 5, d.Display, on ? Pal.Warn : Pal.Hud, "left", 2);
        d.Text(blurb, x + 10, y + 26, d.MonoSmall, Pal.Mute);
        if (gui.IsFocused(id)) d.Frame(x - 3, y - 3, w + 6, h + 6, Pal.White, 2);
        return hit;
    }
}

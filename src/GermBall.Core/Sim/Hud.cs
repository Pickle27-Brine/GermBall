using System;
using System.Numerics;
using GermBall.Engine;

namespace GermBall.Sim;

public sealed partial class Game
{
    static readonly Vector4 White = new(1, 1, 1, 1), Dim = new(1, 1, 1, 0.6f), Panel = new(0.05f, 0.07f, 0.1f, 0.72f),
        Accent = MathX.Rgb(0xFFD040), Green = MathX.Rgb(0x60E070), Blue = MathX.Rgb(0x50B0FF), Red = MathX.Rgb(0xFF5050);

    public static readonly string[] HelpLines =
    {
        "MOVE        WASD / left stick",
        "LOOK        mouse / right stick",
        "JUMP        Space / A   (hold: higher; again: dash attack)",
        "ROLL        Shift or C / B   (hold to stay curled)",
        "SPIN DASH   roll when still, tap jump, let go",
        "POUND       Q / RB while curled in the air",
        "HOVER       aim while in the air",
        "FIRE        left click / RT",
        "AIM         right click / LT",
        "PUNCH       Q or mouse 4 / RB",
        "RELOAD      R / X",
        "WEAPON      1 2 3, wheel / Y",
        "",
        "F1  help         F2  debug readout",
        "F3  zone boxes   F4  noclip",
        "F5  respawn      F6/F7  teleport next/prev",
        "F8  god mode     F9  slow motion",
        "Esc pause        F11 fullscreen",
        "",
        "MUSIC  N next song   B previous",
        "       L loop song   M pause",
    };

    void DrawHud(DrawList dl, Overlay o, int w, int h)
    {
        float s = MathF.Max(1, MathF.Round(h / 360f));   // pixel scale of the font
        float lh = Overlay.LineHeight(s);

        DrawWorldLabels(dl, o, w, h, s);

        // crosshair and hit marker
        var p = Player;
        if (p.Alive && !p.Noclip)
        {
            float cx = w / 2f, cy = h / 2f, gap = 4 * s, len = 5 * s, t = MathF.Max(1, s * 0.75f);
            var cc = new Vector4(1, 1, 1, p.Rolling ? 0.3f : 0.9f);   // faint while curled up (no shooting)
            o.Rect(cx - t / 2, cy - gap - len, t, len, cc); o.Rect(cx - t / 2, cy + gap, t, len, cc);
            o.Rect(cx - gap - len, cy - t / 2, len, t, cc); o.Rect(cx + gap, cy - t / 2, len, t, cc);
            if (HitMarkerT > 0)
            {
                var hc = HitMarkerKill ? Red : White;
                for (int i = 0; i < 4; i++)
                {
                    float d = (6 + i) * s;
                    o.Rect(cx - d - s, cy - d - s, 2 * s, 2 * s, hc); o.Rect(cx + d - s, cy - d - s, 2 * s, 2 * s, hc);
                    o.Rect(cx - d - s, cy + d - s, 2 * s, 2 * s, hc); o.Rect(cx + d - s, cy + d - s, 2 * s, 2 * s, hc);
                }
            }
        }

        // health and shield, bottom left
        {
            float x = 12 * s, y = h - 34 * s, bw = 110 * s, bh = 7 * s;
            o.Rect(x - 4 * s, y - 4 * s, bw + 8 * s, 30 * s, Panel);
            o.Rect(x, y, bw, bh, new Vector4(1, 1, 1, 0.12f));
            o.Rect(x, y, bw * MathX.Clamp01(p.Shield / Player.MaxShield), bh, Blue);
            o.Rect(x, y + bh + 3 * s, bw, bh, new Vector4(1, 1, 1, 0.12f));
            o.Rect(x, y + bh + 3 * s, bw * MathX.Clamp01(p.Hp / Player.MaxHp), bh, p.Hp < 30 ? Red : Green);
            o.TextShadow(x, y + 2 * bh + 6 * s, $"HP {MathF.Ceiling(p.Hp):0}  SHIELD {MathF.Ceiling(p.Shield):0}", s * 0.5f > 1 ? MathF.Round(s * 0.5f) : 1, Dim);
        }

        // weapon, bottom right
        var wpn = Weapon;
        if (wpn != null)
        {
            string ammo = wpn.Endless ? $"{wpn.Mag} | --" : $"{wpn.Mag} | {wpn.Reserve}";
            float bw = 120 * s, x = w - bw - 12 * s, y = h - 34 * s;
            o.Rect(x - 4 * s, y - 4 * s, bw + 8 * s, 30 * s, Panel);
            o.TextShadow(x, y, wpn.Def.Name, s, Accent);
            float aw = Overlay.TextWidth(ammo, s * 1.5f);
            o.TextShadow(x + bw - aw, y - 1 * s, ammo, s * 1.5f, wpn.Mag == 0 ? Red : White);
            if (ReloadT > 0)
            {
                float f = 1 - ReloadT / wpn.Def.Reload;
                o.Rect(x, y + 14 * s, bw, 3 * s, new Vector4(1, 1, 1, 0.15f));
                o.Rect(x, y + 14 * s, bw * f, 3 * s, Accent);
                o.TextShadow(x, y + 19 * s, "RELOADING", MathF.Max(1, MathF.Round(s * 0.5f)), Dim);
            }
            else
            {
                // owned weapons
                float sx = x;
                for (int i = 0; i < Weapons.Count; i++)
                {
                    string label = $"{i + 1} {Weapons[i].Def.Name}";
                    float sc = MathF.Max(1, MathF.Round(s * 0.5f));
                    sx += o.TextShadow(sx, y + 17 * s, label, sc, i == WeaponIndex ? White : new Vector4(1, 1, 1, 0.35f)) + 5 * sc;
                }
            }
        }

        // area banner and active zones, top centre
        if (Area != null && AreaBannerT > 0)
        {
            float k = MathX.Clamp01(AreaBannerT / 0.5f);
            o.TextCentered(w / 2f, 14 * s, Area.Name.ToUpperInvariant(), s * 2, new Vector4(1, 1, 1, k));
        }
        {
            float y = 38 * s;
            foreach (var z in ActiveZones)
            {
                if (z.Kind == ZoneKind.Checkpoint) continue;
                o.TextCentered(w / 2f, y, z.Name.ToUpperInvariant(), s, ZoneColor(z.Kind) with { W = 1 });
                y += lh;
            }
        }
        if (ToastT > 0) o.TextCentered(w / 2f, h * 0.68f, Toast, s, new Vector4(1, 1, 1, MathX.Clamp01(ToastT / 0.4f)));

        // title, top left
        o.TextShadow(10 * s, 8 * s, "GERM BALL " + Version.ToUpperInvariant(), s, Accent);
        o.TextShadow(10 * s, 8 * s + lh, Level.Name + (ShowHelp ? "" : "   F1 help"), MathF.Max(1, MathF.Round(s * 0.5f)) , Dim);
        float statusY = 8 * s + lh * 1.8f;
        string flags = (p.God ? "GOD  " : "") + (p.Noclip ? "NOCLIP  " : "") + (SlowMo ? "SLOW-MO  " : "");
        if (flags.Length > 0) { o.TextShadow(10 * s, statusY, flags, s, Red); statusY += lh; }

        if (ShowHelp) DrawPanel(o, 10 * s, statusY + 4 * s, HelpLines, MathF.Max(1, MathF.Round(s * 0.5f)));
        float musicH = DrawMusic(o, w, s);
        if (ShowDebug) DrawDebug(dl, o, w, h, s, musicH);

        if (!p.Alive && !Paused)
        {
            o.Rect(0, h * 0.42f, w, 36 * s, new Vector4(0.3f, 0, 0, 0.55f));
            o.TextCentered(w / 2f, h * 0.42f + 6 * s, "DOWN", s * 2, White);
            o.TextCentered(w / 2f, h * 0.42f + 24 * s, p.LastEvent, s, Dim);
        }
        if (Paused)
        {
            o.Rect(0, 0, w, h, new Vector4(0, 0, 0, 0.45f));
            o.TextCentered(w / 2f, h * 0.3f, "PAUSED", s * 3, White);
            o.TextCentered(w / 2f, h * 0.3f + 30 * s, "Esc to resume", s, Dim);
            if (Music != null) o.TextCentered(w / 2f, h * 0.3f + 44 * s, "Music:  N next   B previous   L loop   M pause", s, Dim);
        }
    }

    static void DrawPanel(Overlay o, float x, float y, string[] lines, float s)
    {
        float lh = Overlay.LineHeight(s), wMax = 0;
        foreach (var l in lines) wMax = MathF.Max(wMax, Overlay.TextWidth(l, s));
        o.Rect(x, y, wMax + 12 * s, lines.Length * lh + 10 * s, Panel);
        for (int i = 0; i < lines.Length; i++) o.Text(x + 6 * s, y + 6 * s + i * lh, lines[i], s, White);
    }

    void DrawWorldLabels(DrawList dl, Overlay o, int w, int h, float s)
    {
        float sc = MathF.Max(1, MathF.Round(s * 0.75f));
        foreach (var lb in Level.Labels)
        {
            float d = Vector3.Distance(lb.Pos, CamPos);
            if (d > lb.Range) continue;
            if (!dl.Project(lb.Pos, w, h, out var sp, out _)) continue;
            float alpha = MathX.Clamp01((lb.Range - d) / 8);
            // signs behind walls stay readable but faint
            var toLabel = lb.Pos - CamPos;
            if (Level.World.Raycast(CamPos, toLabel / d, d - 0.3f).Hit) alpha *= 0.3f;
            o.TextCentered(sp.X, sp.Y, lb.Text, d > 30 && sc > 1 ? sc - 1 : sc, new Vector4(1, 1, 1, alpha));
        }
        foreach (var n in Effects.Numbers)
        {
            if (!dl.Project(n.Pos, w, h, out var sp, out _)) continue;
            o.TextCentered(sp.X, sp.Y, n.Text, n.Crit ? s * 1.5f : s, (n.Crit ? Accent : White) with { W = MathX.Clamp01(n.Life / 0.3f) });
        }
    }

    /// <summary>The music player, top right: the song, a progress bar, loop and pause marks (the keys are on the help panel and the pause screen).
    /// Returns its height, so the debug panel sits below it.</summary>
    float DrawMusic(Overlay o, int w, float s)
    {
        var m = Music; if (m == null || m.Songs.Count == 0) return 0;
        string song = m.Current;
        if (song != lastSong) { lastSong = song; songShowT = 4; }
        float sc = MathF.Max(1, MathF.Round(s * 0.5f)), lh = Overlay.LineHeight(sc);
        bool big = songShowT > 0 || Paused;
        string line = (song.Length > 0 ? song.ToUpperInvariant() : "...") + (m.Loop ? "  [LOOP]" : "") + (m.Paused ? "  [PAUSED]" : "");
        string keys = "N next  B prev  L loop  M pause";
        float tw = MathF.Max(Overlay.TextWidth("MUSIC  " + line, sc), big ? Overlay.TextWidth(keys, sc) : 0) + 12 * sc;
        float x = w - tw - 10 * s, y = 8 * s, ph = (big ? 3 : 2) * lh + 8 * sc;
        o.Rect(x, y, tw, ph, Panel);
        o.Text(x + 6 * sc, y + 4 * sc, "MUSIC", sc, Accent);
        o.Text(x + 6 * sc + Overlay.TextWidth("MUSIC  ", sc), y + 4 * sc, line, sc, m.Paused ? Dim : White);
        float by = y + 4 * sc + lh + sc, bw = tw - 12 * sc;
        o.Rect(x + 6 * sc, by, bw, 2 * sc, new Vector4(1, 1, 1, 0.15f));
        o.Rect(x + 6 * sc, by, bw * m.Progress, 2 * sc, Accent);
        if (big) o.Text(x + 6 * sc, by + lh * 0.6f, keys, sc, Dim);
        return ph + 4 * s;
    }

    void DrawDebug(DrawList dl, Overlay o, int w, int h, float s, float top = 0)
    {
        var p = Player;
        float slope = MathF.Acos(MathX.Clamp(p.GroundNormal.Y, -1, 1)) / MathX.Deg;
        string state = !p.Alive ? "dead" : p.Noclip ? "noclip" : p.Charging ? $"spin dash rev {p.Rev:0.0}" : p.Pounding ? "pounding" : p.Hovering ? $"hover {p.HoverFuel:0.0}s" : p.DashT > 0 ? "dashing" : p.Rolling ? (p.Grounded ? "rolling" : "curled (air)") : p.Grounded ? (p.Skidding ? "skidding" : "running") : "airborne";
        var lines = new[]
        {
            $"FPS {Fps:0}   sim 120 Hz{(SlowMo ? " x0.25" : "")}",
            $"pos   {p.Pos.X,7:0.00} {p.Pos.Y,7:0.00} {p.Pos.Z,7:0.00}",
            $"vel   {p.Vel.X,7:0.00} {p.Vel.Y,7:0.00} {p.Vel.Z,7:0.00}",
            $"speed {MathX.FlatLen(p.Vel),5:0.00} h  {p.Vel.Y,6:0.00} v",
            $"state {state}",
            $"ground {(p.Grounded ? "yes" : "no")}  slope {slope:0.0} deg",
            $"last jump {p.LastJumpHeight:0.00} m  land {p.LastLandSpeed:0.0} m/s",
            $"dash {(p.DashUsed ? "used" : "ready")}  coyote {MathF.Max(0, p.CoyoteT):0.00}",
            $"cam yaw {CamYaw / MathX.Deg:0} pitch {CamPitch / MathX.Deg:0}",
            $"area {(Area?.Name ?? "-")}",
            $"dps (5s) {RecentDps:0.0}   hits {ShotsHit}/{ShotsFired}",
            $"items {dl.Items.Count}  fx {Effects.Particles.Count}  proj {Projectiles.Count}",
            $"germs {Germs.FindAll(x => x.Standing).Count} up  {Germs.FindAll(x => x.Ragdoll != null).Count} ragdolled  {Germs.FindAll(x => !x.Alive).Count} dead",
            $"event {p.LastEvent}",
        };
        float sc = MathF.Max(1, MathF.Round(s * 0.5f));
        float wMax = 0; foreach (var l in lines) wMax = MathF.Max(wMax, Overlay.TextWidth(l, sc));
        DrawPanel(o, w - wMax - 22 * sc, 8 * s + top, lines, sc);
    }
}

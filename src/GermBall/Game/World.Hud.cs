using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Core;
using GermBall.Render;
using GermBall.UI;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>
/// The HUD, drawn immediate-mode in the same places the browser version's DOM elements sat: compass at the top,
/// shield top right, body diagram top left, tracker bottom left, speed bottom right, weapons bottom centre.
/// </summary>
public sealed partial class World
{
    static readonly (float cx, float cy, float rx, float ry)[] FIG =
    {
        (30, 10, 7, 8), (30, 31, 11, 13), (30, 50, 9, 6), (15, 27, 3.5f, 9), (12, 43, 3, 8), (11, 54, 3.2f, 3.2f), (45, 27, 3.5f, 9), (48, 43, 3, 8), (49, 54, 3.2f, 3.2f),
        (24, 66, 4.5f, 11), (23, 86, 3.8f, 10), (22, 99, 5, 3), (36, 66, 4.5f, 11), (37, 86, 3.8f, 10), (38, 99, 5, 3),
    };
    static readonly string[] COMPASS = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    List<(float x, float z, Vector4 c, float s, float ring)> landmarks;

    static void Ellipse(Draw2D d, float cx, float cy, float rx, float ry, Vector4 fill, Vector4 stroke, int segs = 18)
    {
        for (int i = 0; i < segs; i++)
        {
            float a0 = i * TAU / segs, a1 = (i + 1) * TAU / segs;
            var p0 = new Vector2(cx + MathF.Cos(a0) * rx, cy + MathF.Sin(a0) * ry); var p1 = new Vector2(cx + MathF.Cos(a1) * rx, cy + MathF.Sin(a1) * ry);
            if (fill.W > 0) d.Tri(new Vector2(cx, cy), p0, p1, fill);
            d.Line(p0, p1, stroke, 1);
        }
    }
    static void Diamond(Draw2D d, float cx, float cy, float s, Vector4 c) => d.Quad(new(cx, cy - s * 1.41f), new(cx + s * 1.41f, cy), new(cx, cy + s * 1.41f), new(cx - s * 1.41f, cy), c);
    static Vector4 Css(uint hex, float a = 1) => Draw2D.Col(hex, a);

    public void DrawHUD(Draw2D d, float Wd, float Ht, float t, float fdt)
    {
        var hud = Pal.Hud; float sp = Hypot(P.vx, P.vy, P.vz);
        // the hurt flash: an orange glow pressing in from the edges
        if (hurtA > 0.01f)
            for (int i = 0; i < 14; i++) { float k = (1 - i / 14f); d.Frame(i * 9, i * 9, Wd - i * 18, Ht - i * 18, Css(0xff963c, 0.6f * hurtA * k * k * 0.5f), 9); }

        // ---- compass
        {
            float CW = 320, x0 = Wd / 2 - CW / 2, y0 = 14, heading = ((-cam.yaw * 180 / PI) % 360 + 360) % 360;
            for (int dd = -80; dd <= 80; dd += 5)
            {
                float deg = MathF.Round(heading / 5) * 5 + dd, x = CW / 2 + (deg - heading) * 1.9f; if (x < 6 || x > CW - 6) continue;
                int n = (((int)deg % 360) + 360) % 360; bool major = n % 45 == 0;
                float a = 1 - MathF.Abs(x - CW / 2) / (CW / 2) * 0.8f;
                d.Rect(x0 + x - 0.5f, y0 + (major ? 8 : n % 15 == 0 ? 12 : 15), 1, major ? 10 : n % 15 == 0 ? 6 : 3, Pal.A(Css(0x7affef, 0.85f), a));
                if (major) d.Text(COMPASS[n / 45], x0 + x, y0 + 19, d.MonoSmall, Pal.A(hud, a), "center");
            }
            d.Tri(new(x0 + CW / 2 - 5, y0), new(x0 + CW / 2 + 5, y0), new(x0 + CW / 2, y0 + 6), hud);
        }

        // ---- shield, top right
        {
            float w = MathF.Min(260, Wd * 0.42f), x = Wd - 18 - w, y = 16;
            d.Skew(x, y, w, 14, Pal.HudFaint);
            float fillW = (w - 6) * Clamp(P.shield / 100, 0, 1); var c = P.shield < 35 ? Pal.Warn : hud;
            for (float sx = 0; sx < fillW; sx += 11) d.Skew(x + 3 + sx, y + 2, MathF.Min(9, fillW - sx), 10, c);
            d.Text(P.alive ? "SHIELD " + MathF.Round(P.shield) : "SHATTERED", Wd - 18, y + 19, d.DisplaySmall, hud, "right", 2);
        }
        // ---- companions, under the shield
        {
            float w = MathF.Min(160, Wd * 0.3f), x = Wd - 18 - w, y = 64;
            foreach (var (tp, C) in COMP)
            {
                var A = ALLIES.FirstOrDefault(a => a.type == tp); if (A == null) continue;
                var col = Css(C.css);
                d.Skew(x, y, w, 7, Pal.HudFaint); d.Skew(x + 1, y + 1, (w - 2) * Clamp(A.P.shield / 100, 0, 1), 5, col);
                string lab = C.name + " · " + (!A.P.alive ? "DOWN" : A.P.grabbed != null ? "GRABBED" : A.brain != null && A.brainState != null ? A.brainState + " " + MathF.Round(HealthOf(A) * 100) + "%" : MathF.Round(HealthOf(A) * 100) + "%");
                d.Text(lab.ToUpperInvariant(), Wd - 18, y + 10, d.DisplaySmall, col, "right", 1.8f);
                y += 34;
            }
        }
        // ---- the body diagram, top left
        {
            float x = 18, y = 64, k = 46f / 60;
            for (int i = 0; i < NPART; i++)
            {
                float hk = ME.has[i] ? Clamp(ME.hp[i] / ME.hpMax[i], 0, 1) : -1; var (cx, cy, rx, ry) = FIG[i];
                Vector4 fill, stroke;
                if (hk < 0) { fill = default; stroke = Css(0xff4fd8, 0.35f); }
                else if (hk < 0.35f) { fill = Css(0xff4fd8, 0.7f); stroke = Pal.Hot; }
                else if (hk < 0.7f) { fill = Css(0xffb347, 0.6f); stroke = Pal.Warn; }
                else { fill = Css(0x7affef, 0.55f); stroke = hud; }
                Ellipse(d, x + cx * k, y + cy * k, rx * k, ry * k, fill, stroke);
            }
            d.Text("BODY", x, y + 82, d.DisplaySmall, hud, "left", 1.8f);
        }

        // ---- boss bar
        {
            Actor best = null; float bd = 90; foreach (var A in BOSS.list) { float dd = Hypot(A.P.x - ME.P.x, A.P.z - ME.P.z); if (dd < bd) { bd = dd; best = A; } }
            if (best != null && playing)
            {
                float w = MathF.Min(460, Wd * 0.7f), x = Wd / 2 - w / 2, y = 18; var red = Css(0xff4a4a);
                d.Text(BossName(best), Wd / 2, y, d.DisplaySmall, red, "center", 4);
                d.Skew(x, y + 20, w, 8, Css(0xff4a4a, 0.25f)); d.Skew(x + 1, y + 21, (w - 2) * Clamp(BossHealth(best), 0, 1), 6, Css(0xff2a3a));
            }
        }
        // ---- wave counter
        if (WAVES.on)
        {
            float y = 48; var pink = Css(0xff3cac);
            string big = WAVES.state == "fight" ? "WAVE " + WAVES.n : (WAVES.n > 0 ? "WAVE " + (WAVES.n + 1) : "FIRST WAVE") + " IN " + MathF.Ceiling(MathF.Max(0, WAVES.t));
            string small = WAVES.state == "fight" ? WaveEnemiesLeft() + " LEFT · SCORE " + WAVES.score : "SCORE " + WAVES.score;
            d.Text(big, Wd / 2, y, d.DisplayBig, Pal.White, "center", 3, true, pink);
            d.Text(small, Wd / 2, y + 40, d.Display, hud, "center", 3);
        }

        // ---- tracker (radar), bottom left
        {
            const float R = 75; float ox = 18, oy = Ht - 16 - 18 - 150;
            float cx = ox + R, cy = oy + R;
            d.Circle(cx, cy, R - 2, new Vector4(30 / 255f, 10 / 255f, 50 / 255f, 0.5f), 40);
            d.PushClip(ox, oy, 150, 150);
            foreach (var rr in new[] { R * 0.33f, R * 0.66f }) d.Ring(cx, cy, rr, Css(0x7affef, 0.25f), 1, 36);
            d.Rect(cx - 0.5f, oy + 2, 1, 146, Css(0x7affef, 0.25f)); d.Rect(ox + 2, cy - 0.5f, 146, 1, Css(0x7affef, 0.25f));
            float sweep = (t * 1.2f) % 1; d.Ring(cx, cy, MathF.Max(1, sweep * R), Css(0x7affef, 0.5f * (1 - sweep)), 2, 40);
            float fx = -MathF.Sin(cam.yaw), fz = -MathF.Cos(cam.yaw), rx = MathF.Cos(cam.yaw), rz = -MathF.Sin(cam.yaw), scale = (R - 6) / 120;
            (float, float) ToR(float x, float z) { float dx = x - P.x, dz = z - P.z; return (dx * rx + dz * rz, dx * fx + dz * fz); }
            landmarks ??= PADS.Select(p => (p.X, p.Z, p.Kind == "outpost" ? Css(0xff4fd8) : p.Kind == "base" ? Css(0x7affef) : Css(0xffe66d), p.Kind == "outpost" ? 6f : 5f, 0f))
                .Concat(CRATERS.Select(c => (c.X, c.Z, Css(0x7affef, 0.4f), 3f, c.R))).ToList();
            foreach (var l in landmarks)
            {
                var (rg, fw) = ToR(l.x, l.z); float dist = Hypot(fw, rg), px = rg * scale, py = -fw * scale, lim = R - 8;
                if (l.ring > 0 && dist * scale < R + l.ring * scale) d.Ring(cx + px, cy + py, l.ring * scale, Css(0x7affef, 0.3f), 1, 28);
                if (dist * scale > lim) { px *= lim / (dist * scale); py *= lim / (dist * scale); }
                Diamond(d, cx + px, cy + py, l.s * 0.72f, l.c);
            }
            foreach (var it in items) { var (rg, fw) = ToR(it.x, it.z); if (Hypot(fw, rg) * scale > R - 4) continue; d.Rect(cx + rg * scale - 2, cy - fw * scale - 2, 4, 4, Css(ITEM.TryGetValue(it.kind, out var idef) ? idef.color : 0xffffff)); }
            foreach (var A in actors)
            {
                if (A == HUMAN || A == ME || A.hidden || !A.P.alive) continue;
                var (rg, fw) = ToR(A.P.x, A.P.z); if (Hypot(fw, rg) * scale > R - 5) continue;
                string st = A.squad?.state ?? "";
                var c = A.team == "hero" ? Css(0x7affef) : st == "combat" ? Css(0xff3d6e) : st == "investigate" ? Css(0xffb347) : Css(0xff78be, 0.55f);
                float rr = A.type == "brute" ? 4.5f : A.type == "small" ? 2.3f : 3.2f;
                d.Circle(cx + rg * scale, cy - fw * scale, rr, c, 12);
            }
            d.PopClip();
            d.Ring(cx, cy, R - 2, Css(0x7affef, 0.6f), 1, 48);
            float ang = cam.yaw - P.facing, ca = MathF.Cos(ang), sa = MathF.Sin(ang);
            Vector2 Rot(float x, float y) => new(cx + x * ca - y * sa, cy + x * sa + y * ca);
            d.Tri(Rot(0, -6), Rot(4, 4), Rot(-4, 4), Pal.Warn);
            d.Text("TRACKER · 120 M", cx, oy + 152, d.DisplaySmall, hud, "center", 1.7f);
        }

        // ---- comms: companion lines, bottom left above the tracker
        {
            float x = 18, w = MathF.Min(420, Wd * 0.6f), y = Ht - 150 - 18;
            var f = d.MonoSmall;
            for (int i = COMMS.lines.Count - 1; i >= 0; i--)
            {
                var L = COMMS.lines[i]; float age = clockT - L.t, a = age > 5 ? MathF.Max(0, 1 - (age - 5) / 0.8f) : 1; if (a <= 0) continue;
                var col = Css(L.css); string who = L.who.ToUpperInvariant();
                float ww = d.DisplaySmall.Measure(who, 2) + 10, lines = MathF.Ceiling((ww + f.Measure(L.text)) / (w - 16)), h = 8 + lines * 16;
                y -= h + 4;
                d.Rect(x, y, w, h, new Vector4(8 / 255f, 14 / 255f, 24 / 255f, 0.62f * a)); d.Rect(x, y, 3, h, Pal.A(col, a));
                d.Text(who, x + 11, y + 4, d.DisplaySmall, Pal.A(col, a), "left", 2);
                d.Wrap(L.text, x + 11 + ww, y + 4, w - 22 - ww, f, Pal.A(Pal.Ink, a), 1.33f);
            }
        }

        // ---- speedometer, bottom right
        {
            float rx = Wd - 18, y = Ht - 18 - 100;
            d.Text(sp.ToString("0.0"), rx, y, d.DisplayNum, hud, "right");
            d.Text("M/S", rx, y + 42, d.MonoSmall, hud, "right", 2);
            string state = !P.alive ? "SHATTERED" : P.vehicle != null ? "RIDING" : P.ragdoll != null ? "TUMBLING" : P.grabbed != null ? "GRABBED" : P.flungT > 0 ? "THROWN" : P.stunT > 0 ? "KNOCKED DOWN" : P.pound != 0 ? "GROUND POUND" : P.charging ? "SPIN DASH" : P.homingT > 0 ? "HOMING DASH" : P.dashT > 0 ? "AIR DASH" : P.hover ? "HOVER" : P.hold != null || P.holdA != null ? "CARRYING" : !P.ground ? "AIRBORNE" : P.rolling ? "ROLLING" : P.skid ? "SKIDDING" : sp > TOP * 0.98f ? "TOP SPEED" : sp > 0.3f ? "RUNNING" : "STANDING";
            d.Text(state, rx, y + 58, d.DisplaySmall, P.rolling || P.skid ? Pal.Warn : hud, "right", 2.6f);
            float gx = rx - 160, gy = y + 78;
            d.Frame(gx, gy, 160, 6, Pal.HudDim); d.Rect(gx + 1, gy + 1, 158 * MathF.Min(1, sp / ROLL_CAP), 4, hud);
            d.Rect(gx + 160 * (TOP / ROLL_CAP), gy - 3, 1, 12, Pal.Warn);
            d.Rect(gx, gy + 10, 160 * Clamp(P.rev / 8, 0, 1), 4, Pal.Warn);
        }

        // ---- crosshair
        var cw = CurWpn(ME); var cd = CurDef(ME);
        {
            float cx = Wd / 2, cy = Ht / 2, sc = (W.hitT > 0 ? (P.ads ? 1.1f : 1.35f) : 1) * (P.ads ? 0.7f : 1);
            bool empty = (cw != null && ((cd.Energy && cw.over) || (!cd.Energy && float.IsFinite(cw.ammo) && cw.ammo < 1 && cw.clip < 1))) || !CanShoot(ME);
            float a = empty ? 0.35f : 1; var blue = Pal.A(Css(0x8fe3ff), a);
            Vector2 Pt(float x, float y) => new(cx + x * sc, cy + y * sc);
            switch (cd.Cls)
            {
                case 1:
                    for (int i = 0; i < 4; i++) { float a0 = i * TAU / 4 + 0.15f, a1 = a0 + 1.1f; for (int k = 0; k < 6; k++) { float b0 = Lerp(a0, a1, k / 6f), b1 = Lerp(a0, a1, (k + 1) / 6f); d.Line(Pt(MathF.Cos(b0) * 11, MathF.Sin(b0) * 11), Pt(MathF.Cos(b1) * 11, MathF.Sin(b1) * 11), blue, 2); } }
                    d.Circle(cx, cy, 1.6f * sc, blue, 8); break;
                case 2:
                    { var o = Pal.A(Pal.Warn, a); d.Line(Pt(-12, -4), Pt(0, 6), o, 2); d.Line(Pt(0, 6), Pt(12, -4), o, 2); d.Line(Pt(-7, 10), Pt(7, 10), o, 2); d.Circle(cx, cy - 4 * sc, 1.6f * sc, o, 8); break; }
                case 3:
                    { var o = Pal.A(Css(0xff8a6a), a); for (int k = 0; k < 8; k++) { float b0 = -2.48f + k * 1.74f / 8, b1 = b0 + 1.74f / 8; d.Line(Pt(MathF.Cos(b0) * 13, MathF.Sin(b0) * 13 + 0.5f), Pt(MathF.Cos(b1) * 13, MathF.Sin(b1) * 13 + 0.5f), o, 2); } d.Circle(cx, cy, 2 * sc, o, 8); break; }
                default:
                    d.Line(Pt(0, -14), Pt(0, -6), blue, 2); d.Line(Pt(0, 6), Pt(0, 14), blue, 2); d.Line(Pt(-14, 0), Pt(-6, 0), blue, 2); d.Line(Pt(6, 0), Pt(14, 0), blue, 2);
                    d.Circle(cx, cy, 1.6f * sc, blue, 8); break;
            }
        }
        // ---- homing lock
        W.lockA = cd.Home > 0 ? WithActor(ME, () => FindLock()) : null;
        if (W.lockA != null)
        {
            var L = W.lockA.P; var v = camera.Project(new Vector3(L.x, L.y + L.H * 0.6f, L.z));
            if (v.Z < 1 && MathF.Abs(v.X) < 1 && MathF.Abs(v.Y) < 1)
            {
                float x = (v.X + 1) / 2 * Wd, y = (1 - v.Y) / 2 * Ht, s = 23;
                var a = new Vector2(x, y - s * 1.41f); var b = new Vector2(x + s * 1.41f, y); var c = new Vector2(x, y + s * 1.41f); var e = new Vector2(x - s * 1.41f, y);
                d.Line(a, b, Pal.Warn, 2); d.Line(b, c, Pal.Warn, 2); d.Line(c, e, Pal.Warn, 2); d.Line(e, a, Pal.Warn, 2);
            }
        }
        // ---- throw / charge meter
        {
            bool carrying = (P.hold != null || P.holdA != null) && P.alive, charging = !carrying && W.charge > 0;
            if (carrying || charging)
            {
                float x = Wd / 2 - 60, y = Ht / 2 + 34, k; string lab; var lime = Css(0xc8ff3a);
                if (carrying) { k = P.charge; lab = P.charge > 0 ? (P.charge >= 1 ? "FULL POWER" : "WINDING UP") : "HOLD TO THROW"; }
                else { k = W.charge; lab = W.charge >= 0.98f ? (cd.Type == "bow" ? "FULL DRAW" : "CHARGED") : cd.Type == "bow" ? "DRAWING" : "CHARGING"; }
                d.Text(lab, Wd / 2, y, d.DisplaySmall, lime, "center", 2.2f);
                d.Frame(x, y + 16, 120, 8, Css(0xc8ff3a, 0.6f));
                var c0 = lime; var c1 = Css(0xffe66d); var c2 = Css(0xff4fd8); float fw = 116 * Clamp(k, 0, 1);
                for (int i = 0; i < (int)fw; i += 2) { float u = i / 116f; var c = u < 0.7f ? Vector4.Lerp(c0, c1, u / 0.7f) : Vector4.Lerp(c1, c2, (u - 0.7f) / 0.3f); d.Rect(x + 2 + i, y + 18, 2, 4, c); }
            }
        }
        // ---- weapon slots, bottom centre
        {
            bool dashOn = P.homing > 0; int n = 3 + (dashOn ? 1 : 0); float sw = 132 + 16, gap = 8, total = 3 * sw + (dashOn ? 96 + gap : 0) + gap * 2, x = Wd / 2 - total / 2, y = Ht - 18 - 52;
            for (int k = 0; k < 3; k++)
            {
                var w = W.slots[k]; var def = w != null ? WPN[w.id] : null; bool on = k == W.cur;
                var col = w != null && w.over ? (((int)(clockT * 6.6f) & 1) == 0 ? Pal.Hot : Pal.A(Pal.Hot, 0.4f)) : on ? Pal.Warn : hud;
                float a = def == null && !on ? 0.3f : on || (w != null && w.over) ? 1 : 0.6f;
                d.Rect(x, y, sw, 52, new Vector4(10 / 255f, 22 / 255f, 34 / 255f, 0.35f * a)); d.Frame(x, y, sw, 52, Pal.A(on ? Pal.Warn : w != null && w.over ? Pal.Hot : Pal.HudDim, a));
                string title = (k + 1) + " · " + (def != null ? def.Name : "PSEUDOPODS");
                while (title.Length > 4 && d.DisplaySmall.Measure(title, 1.7f) > sw - 14) title = title[..^2] + "…";
                d.Text(title, x + 7, y + 4, d.DisplaySmall, Pal.A(col, a), "left", 1.7f);
                if (def != null)
                {
                    string big; float bar; Vector4? grad = null;
                    if (def.Energy) { big = w.over ? "OVERHEAT" : w.heat > 0.75f ? "HOT" : "∞"; bar = w.heat; grad = Css(0xffe66d); }
                    else if (def.Type == "melee") { big = "MELEE"; bar = 0; }
                    else if (def.Clip > 0)
                    {   // the magazine in the gun, then the stored ones
                        big = (on && W.reload > 0 ? "RELOAD" : MathF.Floor(w.clip).ToString()) + " | " + MathF.Floor(w.ammo);
                        bar = on && W.reload > 0 && W.reloadMax > 0 ? 1 - W.reload / W.reloadMax : w.clip / def.Clip;
                        int mags = (int)MathF.Ceiling(w.ammo / def.Clip);
                        for (int mi = 0; mi < Math.Min(mags, 8); mi++) d.Rect(x + sw - 10 - mi * 6, y + 25, 4, 10, Pal.A(col, a * 0.85f));
                        if (mags > 8) d.Text("+", x + sw - 10 - 8 * 6 - 6, y + 22, d.MonoSmall, Pal.A(col, a));
                    }
                    else { big = MathF.Floor(w.ammo) + " / " + def.Max; bar = w.ammo / def.Max; }
                    d.Text(big, x + 7, y + 19, d.Display, Pal.A(col, a));
                    d.Rect(x + 7, y + 43, sw - 14, 3, Css(0x8fe3ff, 0.15f));
                    if (grad.HasValue) { float fw = (sw - 14) * Clamp(bar, 0, 1); for (int i = 0; i < (int)fw; i += 2) { float u = i / (sw - 14); var c = u < 0.6f ? Vector4.Lerp(hud, grad.Value, u / 0.6f) : Vector4.Lerp(grad.Value, Pal.Hot, (u - 0.6f) / 0.4f); d.Rect(x + 7 + i, y + 43, 2, 3, c); } }
                    else d.Rect(x + 7, y + 43, (sw - 14) * Clamp(bar, 0, 1), 3, Pal.A(col, a));
                }
                x += sw + gap;
            }
            if (dashOn)
            {
                var pink = Css(0xff5ce1); d.Rect(x, y, 96, 52, new Vector4(10 / 255f, 22 / 255f, 34 / 255f, 0.35f)); d.Frame(x, y, 96, 52, pink);
                d.Text("HOMING DASH", x + 7, y + 4, d.DisplaySmall, pink, "left", 1.2f); d.Text(new string('◆', Math.Min(P.homing, 8)), x + 7, y + 20, d.Display, pink, "left", 1.6f);
            }
        }
        // ---- interact prompt
        {
            object near = P.alive && P.hold == null && P.holdA == null ? (ME.pilot ? ME : P.vehicle != null ? P.vehicle : WithActor(ME, () => NearestInteract(ME))) : null;
            if (near != null)
            {
                string key = padActive ? "HOLD X" : Platform.Input.Label(BINDS["use"][0]);
                string txt = near == ME ? "CLIMB OUT" : near is Actor M && M.type == "mecha" ? "PILOT BATTLE MECHA" : near == P.vehicle ? "GET OFF"
                    : near is Vehicle Vh ? (Vh.kind == "bike" ? "RIDE CHOPPER" : Vh.kind == "rover" ? "DRIVE ROVER" : "BOARD HOVER CHAIR")
                    : near is Item it ? (W.slots.Any(w => w != null && w.id == it.wid) ? "TAKE AMMO · " : W.slots.All(w => w != null) ? "SWAP FOR " : "TAKE ") + ItemLabel(it) : "";
                var f = d.MonoSmall; float kw = f.Measure(key) + 10, tw = f.Measure(txt, 1.4f), w = kw + 6 + tw + 18, x = Wd / 2 - w / 2, y = Ht / 2 + 46;
                d.Rect(x, y, w, 22, new Vector4(10 / 255f, 22 / 255f, 34 / 255f, 0.6f)); d.Frame(x, y, w, 22, Pal.Warn);
                d.Frame(x + 9, y + 3, kw, 16, Pal.Warn); if (padActive && xHeldT > 0) d.Rect(x + 9, y + 3, kw * Math.Min(1, xHeldT / HOLD_USE), 16, Pal.A(Pal.Warn, 0.35f));
                d.Text(key, x + 9 + kw / 2, y + 4, f, Pal.Warn, "center");
                d.Text(txt, x + 9 + kw + 6, y + 4, f, Pal.Warn, "left", 1.4f);
            }
        }
        // ---- recovery: how to get up, and how close an automatic breakout is
        if (P.alive && Locked(P) && P.techT > 0.15f)
        {
            float bw = 180, bx = Wd / 2 - bw / 2, by = Ht / 2 + 70, k = Math.Clamp(P.lockT / BREAKOUT_LOCK, 0, 1);
            string msg = P.grabbed != null ? (padActive ? "MASH A TO BREAK FREE" : "MASH JUMP TO BREAK FREE") : P.techT >= TECH_MIN ? (padActive ? "A / B TO RECOVER" : "JUMP / ROLL TO RECOVER") : "…";
            d.Text(msg, Wd / 2, by, d.DisplaySmall, Pal.Warn, "center", 2);
            d.Frame(bx, by + 16, bw, 6, Pal.A(Pal.Warn, 0.6f)); d.Rect(bx + 1, by + 17, (bw - 2) * k, 4, k > 0.8f ? Pal.Hot : Pal.Warn);
        }
        if (P.guardT > 0 && ((int)(clockT * 20) & 1) == 0) d.Frame(Wd / 2 - 26, Ht / 2 - 26, 52, 52, Pal.A(Pal.Hud, 0.5f), 2);
        // ---- hint line
        if (hintT > 0 && hintText.Length > 0)
        {
            float a = MathF.Min(1, hintT / 0.6f); var f = d.MonoSmall; float w = f.Measure(hintText, 1.2f) + 20, x = Wd / 2 - w / 2, y = Ht - 110 - 22;
            d.Rect(x, y, w, 22, new Vector4(10 / 255f, 22 / 255f, 34 / 255f, 0.6f * a)); d.Text(hintText, Wd / 2, y + 4, f, Pal.A(hud, a), "center", 1.2f);
        }
    }
}

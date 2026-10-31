using System;
using System.Collections.Generic;
using GermBall.Core;
using GermBall.Platform;

namespace GermBall.Game;

/// <summary>
/// The chiptune sound effects: every effect is synthesised once at startup into a sample buffer (sample-and-hold,
/// quantised to a few bits), exactly as the browser version's make() recipes did, then played through the mixer
/// with distance attenuation and stereo pan around the listener.
/// </summary>
public static class Sound
{
    public static readonly Dictionary<string, float[]> Buf = new();
    static readonly Dictionary<string, double> last = new();
    static readonly Random rand = new();
    public static bool Ready;
    /// <summary>The listener: camera position and its right vector on the ground plane.</summary>
    public static float LX, LY, LZ, LRx = 1, LRz = 0;
    static double clock;   // seconds, advanced by the game loop

    sealed class St { public double ph, ph2, ph3, lp, g; }
    delegate double Fn(double t, St st);

    public static void Tick(double dt) => clock += dt;

    public static void Init()
    {
        if (Ready) return;
        double sr = Audio.Rate; var rnd = new Mulberry32(99);
        double env(double t, double a, double d) => t < a ? t / a : Math.Exp(-(t - a) / d);
        double fr(double p) => p - Math.Floor(p);
        double sq(double p) => fr(p) < 0.5 ? 1 : -1;
        double tri(double p) => 1 - 4 * Math.Abs(fr(p) - 0.5);
        double saw(double p) => 2 * fr(p) - 1;
        double noise() => rnd.Next() * 2 - 1;
        double osc(St st, double f, int key = 0)
        {
            switch (key) { case 1: return st.ph2 += f / sr; case 2: return st.ph3 += f / sr; default: return st.ph += f / sr; }
        }
        double lowpass(St st, double x, double k) => st.lp += (x - st.lp) * k;
        const double TP = 6.283;
        void make(string name, double dur, Fn fn, int hold = 3, int bits = 5, double gain = 0.5)
        {
            int n = Math.Max(1, (int)Math.Floor(dur * sr)); var d = new float[n]; double q = Math.Pow(2, bits - 1), held = 0;
            var st = new St();
            for (int i = 0; i < n; i++)
            {
                if (i % hold == 0) held = Math.Round(Math.Clamp(fn(i / sr, st) * gain, -1, 1) * q) / q;
                d[i] = (float)held;
            }
            Buf[name] = d;
        }
        double[] homingN = { 523, 659, 784, 1047 }, pickupN = { 660, 880, 1320 }, powerN = { 392, 523, 659, 784, 1047, 1319 }, superN = { 880, 660, 440, 330 };
        make("shot", 0.09, (t, st) => sq(osc(st, 1500 * Math.Exp(-t * 28))) * env(t, 0.002, 0.03) + noise() * 0.3 * env(t, 0.001, 0.015), 4, 4, 0.35);
        make("eshot", 0.12, (t, st) => tri(osc(st, 700 * Math.Exp(-t * 14) + 120 * Math.Sin(t * 300))) * env(t, 0.003, 0.05), 6, 4, 0.4);
        make("eshot2", 0.1, (t, st) => sq(osc(st, 480 * Math.Exp(-t * 10))) * env(t, 0.002, 0.035) + noise() * 0.2 * env(t, 0.001, 0.02), 5, 4, 0.35);
        make("missile", 0.45, (t, st) => lowpass(st, noise(), 0.05 + t * 0.4) * 2 * env(t, 0.02, 0.2) + tri(osc(st, 200 + t * 900, 1)) * 0.3 * env(t, 0.01, 0.15), 3, 4, 0.5);
        make("lob", 0.3, (t, st) => sq(osc(st, 140 + t * 400)) * env(t, 0.005, 0.1) + noise() * 0.3 * env(t, 0.001, 0.04), 6, 4, 0.45);
        make("boom", 1.1, (t, st) => lowpass(st, noise(), 0.12 * Math.Exp(-t * 2) + 0.01) * 3 * env(t, 0.004, 0.35) + Math.Sin(osc(st, 70 * Math.Exp(-t * 3), 1) * TP) * env(t, 0.002, 0.3), 10, 3, 0.8);
        make("jump", 0.14, (t, st) => sq(osc(st, 260 + t * 5200)) * env(t, 0.004, 0.06), 5, 4, 0.25);
        make("dash", 0.3, (t, st) => (lowpass(st, noise(), 0.2) * 1.5 + sq(osc(st, 180 + t * 3000, 1)) * 0.4) * env(t, 0.01, 0.12), 4, 4, 0.4);
        make("homing", 0.4, (t, st) => sq(osc(st, homingN[Math.Min(3, (int)Math.Floor(t * 12))] * (1 + t))) * env(t, 0.005, 0.18) + noise() * 0.2 * env(t, 0.01, 0.1), 4, 4, 0.35);
        make("poundup", 0.14, (t, st) => tri(osc(st, 400 + t * 9000)) * env(t, 0.003, 0.08), 5, 4, 0.3);
        make("pound", 0.7, (t, st) => Math.Sin(osc(st, 110 * Math.Exp(-t * 5)) * TP) * env(t, 0.002, 0.2) * 1.2 + lowpass(st, noise(), 0.08) * 2.5 * env(t, 0.002, 0.15), 8, 3, 0.75);
        make("land", 0.12, (t, st) => lowpass(st, noise(), 0.1) * 3 * env(t, 0.002, 0.04), 8, 3, 0.4);
        make("thud", 0.2, (t, st) => Math.Sin(osc(st, 90 * Math.Exp(-t * 8)) * TP) * env(t, 0.002, 0.07) + lowpass(st, noise(), 0.15) * 2 * env(t, 0.001, 0.03), 8, 3, 0.55);
        make("bonk", 0.1, (t, st) => tri(osc(st, 240 * Math.Exp(-t * 9))) * env(t, 0.002, 0.04), 6, 4, 0.35);
        make("hit", 0.08, (t, st) => (noise() * 0.8 + sq(osc(st, 900 - t * 4000)) * 0.6) * env(t, 0.001, 0.025), 6, 3, 0.35);
        make("hurt", 0.25, (t, st) => saw(osc(st, 520 * Math.Exp(-t * 6))) * env(t, 0.005, 0.1) + noise() * 0.3 * env(t, 0.001, 0.05), 5, 4, 0.45);
        make("shield", 0.18, (t, st) => Math.Sin(osc(st, 1800 + 600 * Math.Sin(t * 90)) * TP) * env(t, 0.002, 0.07), 4, 4, 0.3);
        make("ping", 0.16, (t, st) => Math.Sin(osc(st, 2600 + 900 * Math.Sin(t * 220)) * TP) * env(t, 0.001, 0.05), 3, 4, 0.25);
        make("sever", 0.25, (t, st) => sq(osc(st, 220 * Math.Exp(-t * 7))) * 0.5 * env(t, 0.002, 0.06) + lowpass(st, noise(), 0.3 * Math.Exp(-t * 6) + 0.02) * 2 * env(t, 0.002, 0.08), 6, 3, 0.5);
        make("shatter", 0.7, (t, st) => { if (rnd.Next() < 0.004) st.g = 1500 + rnd.Next() * 3000; return Math.Sin(osc(st, st.g) * TP) * env(t, 0.001, 0.25) * (0.5 + 0.5 * Math.Sin(t * 90)) + noise() * 0.4 * env(t, 0.001, 0.06); }, 3, 4, 0.5);
        make("grab", 0.3, (t, st) => sq(osc(st, 110 + 40 * Math.Sin(t * 60))) * env(t, 0.01, 0.12), 6, 3, 0.4);
        make("throw", 0.3, (t, st) => lowpass(st, noise(), 0.03 + t * 0.6) * 3 * env(t, 0.06, 0.08), 4, 4, 0.4);
        make("alert", 0.45, (t, st) => tri(osc(st, 380 + t * 500 + 180 * Math.Sin(t * 50))) * env(t, 0.01, 0.2), 7, 3, 0.4);
        make("stomp", 0.25, (t, st) => Math.Sin(osc(st, 62 * Math.Exp(-t * 4)) * TP) * env(t, 0.002, 0.09) + lowpass(st, noise(), 0.05) * env(t, 0.001, 0.05), 10, 3, 0.6);
        make("pickup", 0.22, (t, st) => sq(osc(st, pickupN[Math.Min(2, (int)Math.Floor(t * 14))])) * env(t, 0.002, 0.12), 4, 4, 0.3);
        make("powerup", 0.5, (t, st) => tri(osc(st, powerN[Math.Min(5, (int)Math.Floor(t * 14))])) * env(t, 0.002, 0.3), 3, 4, 0.4);
        make("repair", 0.45, (t, st) => (tri(osc(st, 440)) + tri(osc(st, 554, 1)) + tri(osc(st, 659, 2))) / 3 * env(t, 0.01, 0.25), 4, 4, 0.5);
        make("switch", 0.06, (t, st) => sq(osc(st, 1200)) * env(t, 0.001, 0.02), 4, 3, 0.25);
        make("click", 0.04, (t, st) => noise() * env(t, 0.001, 0.008), 3, 3, 0.3);
        make("overheat", 0.6, (t, st) => sq(osc(st, 1300 * Math.Exp(-t * 4) + 80 * Math.Sin(t * 70))) * env(t, 0.005, 0.25) + noise() * 0.25 * env(t, 0.05, 0.25), 5, 3, 0.4);
        make("cooled", 0.16, (t, st) => sq(osc(st, t < 0.08 ? 700 : 1050)) * env(t, 0.002, 0.07), 4, 4, 0.25);
        make("reload", 0.25, (t, st) => noise() * (env(t, 0.001, 0.01) + (t > 0.14 ? env(t - 0.14, 0.001, 0.012) : 0)), 3, 3, 0.4);
        make("rev", 0.12, (t, st) => sq(osc(st, 300 + t * 3000)) * env(t, 0.002, 0.05), 5, 4, 0.25);
        make("launch", 0.45, (t, st) => (lowpass(st, noise(), 0.25) * 2 + sq(osc(st, 160 + t * 1800, 1)) * 0.5) * env(t, 0.005, 0.18), 4, 4, 0.5);
        make("slam", 0.3, (t, st) => Math.Sin(osc(st, 140 * Math.Exp(-t * 7)) * TP) * env(t, 0.001, 0.08) + noise() * 0.8 * env(t, 0.001, 0.04), 7, 3, 0.6);
        make("punch", 0.12, (t, st) => lowpass(st, noise(), 0.3) * 2.5 * env(t, 0.001, 0.03) + Math.Sin(osc(st, 180) * TP) * env(t, 0.001, 0.04), 6, 3, 0.5);
        make("ric", 0.12, (t, st) => tri(osc(st, 3000 * Math.Exp(-t * 10))) * env(t, 0.001, 0.05), 3, 4, 0.2);
        make("boomshot", 0.22, (t, st) => sq(osc(st, 520 * Math.Exp(-t * 18))) * env(t, 0.002, 0.05) + lowpass(st, noise(), 0.25) * 2.4 * env(t, 0.001, 0.07), 6, 3, 0.55);
        make("dart", 0.08, (t, st) => (noise() * 0.5 + tri(osc(st, 2400 - t * 9000)) * 0.6) * env(t, 0.001, 0.025), 3, 4, 0.3);
        make("needle", 0.09, (t, st) => sq(osc(st, 3200 + 1400 * Math.Sin(t * 400))) * env(t, 0.001, 0.03), 3, 4, 0.22);
        make("plasma", 0.18, (t, st) => tri(osc(st, 300 + 900 * Math.Exp(-t * 20) + 60 * Math.Sin(t * 500))) * env(t, 0.002, 0.08), 5, 4, 0.4);
        make("laser", 0.12, (t, st) => sq(osc(st, 2200 * Math.Exp(-t * 22) + 300)) * env(t, 0.001, 0.05), 3, 4, 0.3);
        make("beam", 0.25, (t, st) => (sq(osc(st, 160 + 30 * Math.Sin(t * 180))) * 0.6 + tri(osc(st, 960, 1)) * 0.4) * env(t, 0.01, 0.4), 4, 4, 0.3);
        make("zap", 0.3, (t, st) => sq(osc(st, 1800 * Math.Exp(-t * 9) + 200)) * env(t, 0.002, 0.12) + noise() * 0.3 * env(t, 0.001, 0.05), 3, 4, 0.4);
        make("zapbig", 0.7, (t, st) => (sq(osc(st, 900 * Math.Exp(-t * 4) + 80)) * 0.7 + lowpass(st, noise(), 0.2) * 1.6) * env(t, 0.002, 0.3), 4, 3, 0.6);
        make("bat", 0.16, (t, st) => tri(osc(st, 900 * Math.Exp(-t * 14))) * env(t, 0.001, 0.05) + lowpass(st, noise(), 0.5) * 1.4 * env(t, 0.001, 0.02), 4, 4, 0.55);
        make("boing", 0.5, (t, st) => sq(osc(st, 180 + 120 * Math.Sin(t * 60) * Math.Exp(-t * 5))) * env(t, 0.002, 0.2), 5, 4, 0.5);
        make("whoosh", 0.2, (t, st) => lowpass(st, noise(), 0.05 + Math.Sin(t / 0.2 * Math.PI) * 0.25) * 3 * Math.Sin(t / 0.2 * Math.PI), 3, 4, 0.35);
        make("supercombine", 0.9, (t, st) => (sq(osc(st, superN[Math.Min(3, (int)Math.Floor(t * 18))])) * 0.4 + lowpass(st, noise(), 0.15) * 2) * env(t, 0.002, 0.35), 4, 3, 0.7);
        make("clank", 0.2, (t, st) => (tri(osc(st, 1300)) * 0.6 + tri(osc(st, 1830, 1)) * 0.5) * env(t, 0.001, 0.06) + noise() * 0.4 * env(t, 0.001, 0.015), 3, 4, 0.4);
        make("roar", 0.9, (t, st) => (saw(osc(st, 85 + 25 * Math.Sin(t * 30))) * 0.7 + lowpass(st, noise(), 0.12) * 1.4) * env(t, 0.06, 0.4), 7, 3, 0.65);
        make("engine", 0.6, (t, st) => sq(osc(st, 55 + t * 140 + 12 * Math.Sin(t * 80))) * env(t, 0.03, 0.3) + lowpass(st, noise(), 0.06) * env(t, 0.01, 0.2), 8, 3, 0.45);
        Ready = true;
    }

    static int voices; static readonly Queue<double> ends = new();

    /// <summary>A positional effect (the browser's sfx()): quiet with distance, panned around the listener.</summary>
    public static void Play(string name, float x, float y, float z, float vol = 1)
    {
        var a = Audio.Instance; if (!Ready || a == null || !a.Ok || !Buf.TryGetValue(name, out var buf)) return;
        if (last.TryGetValue(name, out var lt) && clock - lt < 0.035) return;
        while (ends.Count > 0 && ends.Peek() <= clock) { ends.Dequeue(); voices--; }
        float dx = x - LX, dy = y - LY, dz = z - LZ, d2 = dx * dx + dy * dy + dz * dz, g = vol / (1 + d2 / 300);
        if (g < 0.015f || voices > 26) return;
        last[name] = clock;
        float rate = 0.9f + (float)rand.NextDouble() * 0.2f, d = MathF.Sqrt(d2); if (d == 0) d = 1;
        float pan = Math.Clamp((dx * LRx + dz * LRz) / d, -1, 1) * 0.8f;
        a.Play(buf, g, rate, pan);
        voices++; ends.Enqueue(clock + buf.Length / (double)Audio.Rate / rate);
    }

    /// <summary>A non-positional effect for menus.</summary>
    public static void Ui(string name, float vol = 1) { if (Ready && Buf.TryGetValue(name, out var b)) Audio.Instance?.Play(b, vol, 1, 0); }
}

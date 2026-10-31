using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GermBall.Core;
using GermBall.Platform;

namespace GermBall.Game;

// =====================================================================
// Music: the little FM sound chip from the browser version (two- and three-operator voices), 8-bit PCM drums and
// per-song effect racks, rendered sample by sample on the mixer thread instead of as Web Audio node graphs.
// The songs and patches live in data/music/*.json so they can be edited or extended without rebuilding.
// =====================================================================

public sealed class Patch
{
    public string car = "sine", mod = "sine"; public double ratio = 1, index, idxEnd, idxT = 0.1, a = 0.01, d = 0.2, s, r = 0.1, gain = 0.1, sub;
    public double op3Ratio, op3Index; public bool op3;
    public double cut, cutEnv, q, detune, breath, wob, wobDepth; public double[] vib, unison, trem;
}

public sealed class Song
{
    public string name = "", chain = "clean"; public double gain = 0.8, bpm = 120, swing; public int key = 40, steps = 16, seed = 1, lift, loops = 2, sweepLow;
    public int[] scale = { 0, 2, 3, 5, 7, 8, 10 }, roots = { 0 }; public bool sweep, poly;
    public List<(int n, string parts)> form = new();
    public Dictionary<string, string> drums = new();
    public sealed class Part
    {
        public string patch, grid, alt, vowels; public int[] notes, altNotes; public int oct, len = 1, harm; public double density = 0.4, span = 6;
        public bool ring, bend, slide, cluster, harmAlways, harmSet;
    }
    public Part bass, lead, bell, counter, arp, pad, stab, glass, choir, wail;
    public List<List<(int s, int deg, int len, double acc)>> motifs = new();
    public Mulberry32 r; public int bars;
}

public sealed class Music : Audio.IGenerator
{
    public bool IsMusic => true;
    const double SR = Audio.Rate;
    public static Music Instance;
    public readonly List<Song> Songs = new();
    readonly Dictionary<string, Patch> P = new();
    readonly Random rand = new();
    /// <summary>Raised (on the mixer thread) when a new song starts; the game shows its name.</summary>
    public Action<string> OnSong;
    public volatile bool Enabled = true;

    // ---------- loading ----------
    public static Music Load(string dir)
    {
        var m = new Music();
        try
        {
            using (var pd = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "patches.json"))))
                foreach (var p in pd.RootElement.EnumerateObject()) m.P[p.Name] = ParsePatch(p.Value);
            using (var sd = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "songs.json"))))
                foreach (var s in sd.RootElement.EnumerateArray()) m.Songs.Add(ParseSong(s));
        }
        catch (Exception e) { Log.Warn("music data failed to load: " + e.Message); }
        foreach (var S in m.Songs)
        {
            var r = new Mulberry32(S.seed); var L = S.lead ?? S.bell; double den = L?.density ?? 0.4, span = L?.span ?? 6;
            S.motifs.Add(MakeMotif(r, S.steps, den, span)); S.motifs.Add(MakeMotif(r, S.steps, den * 1.2, span));
            S.motifs.Add(MakeMotif(r, S.steps, den * 1.1, span + 1)); S.motifs.Add(MakeMotif(r, S.steps, den * 1.3, span));
            S.r = r; S.bars = 0; foreach (var f in S.form) S.bars += f.n;
        }
        m.MakeDrums();
        var nz = new float[(int)SR * 3]; var rr = new Random(5); for (int i = 0; i < nz.Length; i++) nz[i] = (float)(rr.NextDouble() * 2 - 1); m.noise = nz;
        Instance = m;
        return m;
    }

    static double D(JsonElement e, string k, double def = 0) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
    static string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static bool B(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
    static double[] Arr(JsonElement e, string k) { if (!e.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.Array) return null; var o = new List<double>(); foreach (var x in v.EnumerateArray()) o.Add(x.GetDouble()); return o.ToArray(); }
    static int[] IArr(JsonElement e, string k) { var a = Arr(e, k); if (a == null) return null; var o = new int[a.Length]; for (int i = 0; i < a.Length; i++) o[i] = (int)a[i]; return o; }

    static Patch ParsePatch(JsonElement e)
    {
        var p = new Patch
        {
            car = Str(e, "car") ?? "sine", mod = Str(e, "mod") ?? "sine", ratio = D(e, "ratio", 1), index = D(e, "index"), idxEnd = D(e, "idxEnd"), idxT = D(e, "idxT", 0.1),
            a = D(e, "a", 0.01), d = D(e, "d", 0.2), s = D(e, "s"), r = D(e, "r", 0.1), gain = D(e, "gain", 0.1), sub = D(e, "sub"), cut = D(e, "cut"), cutEnv = D(e, "cutEnv"), q = D(e, "q"),
            detune = D(e, "detune"), breath = D(e, "breath"), wob = D(e, "wob"), wobDepth = D(e, "wobDepth"), vib = Arr(e, "vib"), unison = Arr(e, "unison"), trem = Arr(e, "trem")
        };
        if (e.TryGetProperty("op3", out var o3) && o3.ValueKind == JsonValueKind.Object) { p.op3 = true; p.op3Ratio = D(o3, "ratio"); p.op3Index = D(o3, "index"); }
        return p;
    }

    static Song.Part ParsePart(JsonElement s, string k)
    {
        if (!s.TryGetProperty(k, out var e) || e.ValueKind != JsonValueKind.Object) return null;
        var p = new Song.Part { patch = Str(e, "patch"), grid = Str(e, "grid"), vowels = Str(e, "vowels"), notes = IArr(e, "notes"), oct = (int)D(e, "oct"), len = (int)D(e, "len", 1),
            density = D(e, "density", 0.4), span = D(e, "span", 6), ring = B(e, "ring"), bend = B(e, "bend"), slide = B(e, "slide"), cluster = B(e, "cluster"), harmAlways = B(e, "harmAlways") };
        if (e.TryGetProperty("alt", out var alt)) { if (alt.ValueKind == JsonValueKind.String) p.alt = alt.GetString(); else if (alt.ValueKind == JsonValueKind.Array) p.altNotes = IArr(e, "alt"); }
        if (e.TryGetProperty("harm", out var h) && h.ValueKind == JsonValueKind.Number) { p.harm = h.GetInt32(); p.harmSet = p.harm != 0; }
        return p;
    }

    static Song ParseSong(JsonElement e)
    {
        var S = new Song
        {
            name = Str(e, "name") ?? "?", chain = Str(e, "chain") ?? "clean", gain = D(e, "gain", 0.8), bpm = D(e, "bpm", 120), swing = D(e, "swing"), key = (int)D(e, "key", 40),
            steps = (int)D(e, "steps", 16), seed = (int)D(e, "seed", 1), lift = (int)D(e, "lift"), loops = (int)D(e, "loops", 2), sweep = B(e, "sweep"), sweepLow = (int)D(e, "sweepLow"), poly = B(e, "poly"),
            scale = IArr(e, "scale") ?? new[] { 0, 2, 3, 5, 7, 8, 10 }, roots = IArr(e, "roots") ?? new[] { 0 }
        };
        if (e.TryGetProperty("form", out var f)) foreach (var x in f.EnumerateArray()) S.form.Add((x[0].GetInt32(), x[1].GetString()));
        if (e.TryGetProperty("drums", out var dr)) foreach (var x in dr.EnumerateObject()) S.drums[x.Name] = x.Value.GetString();
        S.bass = ParsePart(e, "bass"); S.lead = ParsePart(e, "lead"); S.bell = ParsePart(e, "bell"); S.counter = ParsePart(e, "counter"); S.arp = ParsePart(e, "arp");
        S.pad = ParsePart(e, "pad"); S.stab = ParsePart(e, "stab"); S.glass = ParsePart(e, "glass"); S.choir = ParsePart(e, "choir"); S.wail = ParsePart(e, "wail");
        if (S.bass != null && S.bass.altNotes == null && S.bass.alt == null) { }
        return S;
    }

    static List<(int, int, int, double)> MakeMotif(Mulberry32 r, int steps, double density, double span)
    {
        var notes = new List<(int, int, int, double)>(); int deg = (int)Math.Floor(r.Next() * 4), held = 0;
        for (int i = 0; i < steps * 2; i++)
        {
            if (held > 0) { held--; continue; }
            bool strong = i % 4 == 0;
            if (r.Next() < density * (strong ? 1.5 : 0.8))
            {
                deg = (int)Math.Clamp(deg + Math.Floor(r.Next() * 5) - 2, -2, span);
                int len = r.Next() < 0.3 ? 4 : r.Next() < 0.5 ? 2 : 1;
                notes.Add((i, deg, len, strong ? 1 : 0.75)); held = len - 1;
            }
        }
        return notes;
    }

    // ---------- drums (8-bit PCM at 11 kHz) ----------
    readonly Dictionary<string, float[]> drumBuf = new();
    float[] noise;
    sealed class DS { public double ph, lp, lp2; }
    void MakeDrums()
    {
        int hold = Math.Max(1, (int)Math.Round(SR / 11025)); var r = new Mulberry32(77);
        double nz() => r.Next() * 2 - 1;
        float[] pcm(double dur, Func<double, DS, double> fn, int bits = 8)
        {
            int n = (int)Math.Floor(dur * SR); var d = new float[n]; double q = Math.Pow(2, bits - 1), held = 0; var st = new DS();
            for (int i = 0; i < n; i++) { if (i % hold == 0) held = Math.Round(Math.Clamp(fn(i / SR, st), -1, 1) * q) / q; d[i] = (float)held; }
            return d;
        }
        double fr(double x) => x - Math.Floor(x);
        drumBuf["kick"] = pcm(0.42, (t, s) => { s.ph += (48 + 130 * Math.Exp(-t * 32)) / SR * hold; return Math.Sin(s.ph * 6.283) * Math.Exp(-t * 7) * 1.2 + (t < 0.006 ? nz() * 0.6 : 0); });
        drumBuf["snare"] = pcm(0.3, (t, s) => { s.ph += 185 / SR * hold; s.lp += (nz() - s.lp) * 0.55; return s.lp * 0.9 * Math.Exp(-t * 13) + Math.Sin(s.ph * 6.283) * 0.5 * Math.Exp(-t * 22); });
        drumBuf["hat"] = pcm(0.07, (t, s) => { double n = nz(), h = n - s.lp; s.lp = n; return h * 0.45 * Math.Exp(-t * 60); });
        drumBuf["ohat"] = pcm(0.3, (t, s) => { double n = nz(), h = n - s.lp; s.lp = n; return h * 0.35 * Math.Exp(-t * 11); });
        drumBuf["clap"] = pcm(0.25, (t, s) => { s.lp += (nz() - s.lp) * 0.7; double e = t < 0.01 ? 1 : t < 0.02 ? 0.3 : t < 0.03 ? 1 : t < 0.04 ? 0.3 : Math.Exp(-(t - 0.04) * 18); return s.lp * e; });
        drumBuf["tom"] = pcm(0.35, (t, s) => { s.ph += (90 + 90 * Math.Exp(-t * 14)) / SR * hold; return Math.Sin(s.ph * 6.283) * Math.Exp(-t * 9); });
        drumBuf["rim"] = pcm(0.05, (t, s) => { s.ph += 1700 / SR * hold; return (Math.Sign(Math.Sin(s.ph * 6.283)) * 0.5 + nz() * 0.3) * Math.Exp(-t * 90); });
        drumBuf["crash"] = pcm(1.4, (t, s) => { double n = nz(), h = n - s.lp; s.lp = n; s.lp2 += (h - s.lp2) * 0.6; return s.lp2 * 0.45 * Math.Exp(-t * 2.6); }, 7);
        drumBuf["zap"] = pcm(0.18, (t, s) => { s.ph += (2400 * Math.Exp(-t * 30) + 120) / SR * hold; return (fr(s.ph) < 0.5 ? 0.5 : -0.5) * Math.Exp(-t * 16); }, 6);
        drumBuf["kick2"] = pcm(0.9, (t, s) => { s.ph += (38 + 70 * Math.Exp(-t * 18)) / SR * hold; return Math.Tanh(Math.Sin(s.ph * 6.283) * 1.6) * Math.Exp(-t * 3.2) + (t < 0.004 ? nz() * 0.5 : 0); });
        drumBuf["snap"] = pcm(0.12, (t, s) => { s.lp += (nz() - s.lp) * 0.8; double h = s.lp - s.lp2; s.lp2 = s.lp; return h * (t < 0.004 ? 1.2 : Math.Exp(-t * 45)); });
        drumBuf["cowbell"] = pcm(0.3, (t, s) => { s.ph += 540 / SR * hold; s.lp += 800 / SR * hold; return ((fr(s.ph) < 0.5 ? 0.3 : -0.3) + (fr(s.lp) < 0.5 ? 0.25 : -0.25)) * Math.Exp(-t * 14); }, 6);
        drumBuf["heart"] = pcm(0.5, (t, s) => { s.ph += (52 + 30 * Math.Exp(-t * 20)) / SR * hold; double e = Math.Exp(-t * 14) + (t > 0.18 ? Math.Exp(-(t - 0.18) * 16) * 0.7 : 0); return Math.Sin(s.ph * 6.283) * e; });
    }

    // ---------- DSP building blocks ----------
    /// <summary>RBJ biquad. Web Audio treats lowpass/highpass Q in dB; bandpass/peaking Q is linear, as here.</summary>
    sealed class Biquad
    {
        public string Type; public double F, Q = 1, Gain; double b0, b1, b2, a1, a2, x1, x2, y1, y2, lf = -1, lq = -1;
        public Biquad(string type, double f, double q = 1, double gain = 0) { Type = type; F = f; Q = q; Gain = gain; }
        void Coef()
        {
            double f = Math.Clamp(F, 10, SR * 0.49), w = 2 * Math.PI * f / SR, cw = Math.Cos(w), sw = Math.Sin(w), A = Math.Pow(10, Gain / 40);
            double Ql = Type == "lowpass" || Type == "highpass" ? Math.Pow(10, Q / 20) : Math.Max(0.0001, Q), al = sw / (2 * Ql), a0;
            switch (Type)
            {
                case "highpass": b0 = (1 + cw) / 2; b1 = -(1 + cw); b2 = b0; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break;
                case "bandpass": b0 = al; b1 = 0; b2 = -al; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break;
                case "peaking": b0 = 1 + al * A; b1 = -2 * cw; b2 = 1 - al * A; a0 = 1 + al / A; a1 = -2 * cw; a2 = 1 - al / A; break;
                case "highshelf":
                    {
                        double sA = Math.Sqrt(A), al2 = sw / 2 * Math.Sqrt(2);
                        b0 = A * ((A + 1) + (A - 1) * cw + 2 * sA * al2); b1 = -2 * A * ((A - 1) + (A + 1) * cw); b2 = A * ((A + 1) + (A - 1) * cw - 2 * sA * al2);
                        a0 = (A + 1) - (A - 1) * cw + 2 * sA * al2; a1 = 2 * ((A - 1) - (A + 1) * cw); a2 = (A + 1) - (A - 1) * cw - 2 * sA * al2; break;
                    }
                default: b0 = (1 - cw) / 2; b1 = 1 - cw; b2 = b0; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break;
            }
            b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0; lf = F; lq = Q;
        }
        public double Run(double x)
        {
            if (F != lf || Q != lq) Coef();
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y;
            if (double.IsNaN(y) || Math.Abs(y) > 1e6) { y = 0; x1 = x2 = y1 = y2 = 0; }
            return y;
        }
    }
    static double Wave(string type, double ph)
    {
        double p = ph - Math.Floor(ph);
        switch (type) { case "square": return p < 0.5 ? 1 : -1; case "sawtooth": return 2 * p - 1; case "triangle": return 1 - 4 * Math.Abs(p - 0.5); default: return Math.Sin(p * 2 * Math.PI); }
    }
    /// <summary>A setTargetAtTime-style envelope stage: exponential approach.</summary>
    static double Approach(double v, double target, double tc) => target + (v - target) * Math.Exp(-1 / (tc * SR));

    // ---------- voices ----------
    abstract class Voice
    {
        public double Start; public int Dest; public Chain Ch;   // Dest: 0 bus, 1 send, 2 ring
        public abstract bool Render(double t, out double s);     // t: seconds since start; false when finished
    }

    sealed class Fm : Voice
    {
        public Patch Pt; public double f, dur, vel, bend, det, end, stop, pk;
        double phC, phM, ph3, phV, phW, phT, amp = 0.0001, sub = 0.0001; double[] phU; Biquad flt, bp; int ni; Music mus;
        public Fm(Music m, Patch p, double f, double dur, double vel, double bend)
        {
            mus = m; Pt = p; this.f = f; this.dur = dur; this.vel = vel; this.bend = bend;
            det = p.detune != 0 ? (m.rand.NextDouble() - 0.5) * p.detune : 0;
            pk = p.gain * vel; end = Math.Max(dur, p.a + 0.01); stop = end + p.r * 3 + 0.05;
            if (p.cut > 0) flt = new Biquad("lowpass", p.cut, p.q != 0 ? p.q : 1);
            if (p.unison != null) phU = new double[p.unison.Length];
            if (p.breath > 0) { bp = new Biquad("bandpass", f * 2, 3); ni = m.rand.Next(m.noise.Length); }
        }
        static double ExpRamp(double a, double b, double t, double T) => t >= T ? b : a * Math.Pow(b / a, t / T);
        public override bool Render(double t, out double o)
        {
            o = 0; if (t >= stop) return false;
            var p = Pt;
            double fb = bend != 0 ? ExpRamp(f, Math.Max(20, f * bend), t, dur) : f, bendR = fb / f;
            double I0 = p.index * f * p.ratio, I1 = Math.Max(0.01, p.idxEnd * f * p.ratio), mg = I0 <= 0 ? 0 : ExpRamp(I0, I1, t, p.idxT);
            double fm = f * p.ratio * bendR;
            if (p.op3) { double g3a = p.op3Index * f * p.op3Ratio, g3 = g3a <= 0 ? 0 : ExpRamp(g3a, 1, t, p.idxT + 0.1); ph3 += f * p.op3Ratio / SR; fm += Math.Sin(ph3 * 2 * Math.PI) * g3; }
            phM += fm / SR; double modOut = Wave(p.mod, phM) * mg;
            double fc = fb * Math.Pow(2, det / 1200) + modOut;
            if (p.vib != null) { phV += p.vib[0] / SR; fc += Math.Sin(phV * 2 * Math.PI) * f * p.vib[1] * Math.Min(1, t / Math.Min(0.4, dur)); }
            phC += fc / SR; double sig = Wave(p.car, phC);
            if (phU != null) for (int k = 0; k < phU.Length; k++) { phU[k] += (fb * Math.Pow(2, p.unison[k] / 1200) + modOut) / SR; sig += Wave(p.car, phU[k]); }
            if (flt != null)
            {
                double cf = p.cutEnv != 0 ? ExpRamp(p.cut + p.cutEnv * vel, p.cut, t, Math.Max(0.05, p.d)) : p.cut;
                if (p.wob != 0) { phW += p.wob / SR; cf += Math.Sin(phW * 2 * Math.PI) * (p.wobDepth != 0 ? p.wobDepth : 1000); }
                flt.F = Math.Max(20, cf); sig = flt.Run(sig);
            }
            if (bp != null) { sig += bp.Run(mus.noise[ni]) * p.breath; ni = (ni + 1) % mus.noise.Length; }
            // amplitude envelope
            if (t < p.a) amp = 0.0001 + (pk - 0.0001) * t / p.a;
            else if (t < end) amp = Approach(amp, pk * Math.Max(0.0001, p.s), p.d / 3 + 0.001);
            else amp = Approach(amp, 0.0001, p.r / 3 + 0.001);
            o = sig * amp;
            if (p.trem != null) { phT += p.trem[0] / SR; o *= 1 - p.trem[1] + Math.Sin(phT * 2 * Math.PI) * p.trem[1]; }
            if (p.sub > 0)
            {
                if (t < 0.005) sub = 0.0001 + (pk * p.sub - 0.0001) * t / 0.005; else if (t < end) sub = pk * p.sub; else sub = Approach(sub, 0.0001, p.r / 3 + 0.01);
                o += Math.Sin(SubPh(fb)) * sub;
            }
            return true;
        }
        double subPh; double SubPh(double fb) { subPh += fb / 2 / SR; return subPh * 2 * Math.PI; }
    }

    sealed class Sample : Voice
    {
        public float[] Data; public double Rate = 1, Gain = 1;
        public override bool Render(double t, out double s)
        {
            double pos = t * SR * Rate; int i = (int)pos; if (i >= Data.Length - 1) { s = 0; return false; }
            double fr = pos - i; s = (Data[i] + (Data[i + 1] - Data[i]) * fr) * Gain; return true;
        }
    }

    static readonly Dictionary<char, double[]> VOWELS = new() { ['a'] = new double[] { 730, 1090, 2440 }, ['o'] = new double[] { 570, 840, 2410 }, ['u'] = new double[] { 300, 870, 2240 }, ['e'] = new double[] { 530, 1840, 2480 }, ['i'] = new double[] { 270, 2290, 3010 } };
    sealed class Choir : Voice
    {
        public double f, dur, pk; public bool whisper; double ph, phV, vr, amp = 0.0001; int ni; Biquad[] bp = new Biquad[3]; Music m;
        public Choir(Music m, double f, double dur, double vel, char vowel, bool whisper)
        {
            this.m = m; this.f = f; this.dur = dur; this.whisper = whisper; pk = (whisper ? 0.5 : 0.35) * vel; vr = 4.6 + m.rand.NextDouble();
            var fm = VOWELS.TryGetValue(vowel, out var v) ? v : VOWELS['a'];
            for (int i = 0; i < 3; i++) bp[i] = new Biquad("bandpass", fm[i], whisper ? 6 : 9);
            ni = m.rand.Next(m.noise.Length);
        }
        static readonly double[] G = { 1, 0.6, 0.35 };
        public override bool Render(double t, out double o)
        {
            o = 0; if (t > dur + 1.6) return false;
            double src;
            if (whisper) { src = m.noise[ni]; ni = (ni + 1) % m.noise.Length; }
            else { phV += vr / SR; ph += (f + Math.Sin(phV * 2 * Math.PI) * f * 0.012) / SR; src = Wave("sawtooth", ph); }
            double sum = 0; for (int i = 0; i < 3; i++) sum += bp[i].Run(src) * G[i];
            double a = Math.Min(0.6, dur * 0.4);
            if (t < a) amp = 0.0001 + (pk - 0.0001) * t / a; else if (t >= dur) amp = Approach(amp, 0.0001, 0.35); else amp = pk;
            o = sum * amp; return true;
        }
    }

    sealed class Riser : Voice
    {
        public double dur; public bool up; int ni; Biquad bp = new("bandpass", 300, 6); Music m;
        public Riser(Music m, double dur, bool up) { this.m = m; this.dur = dur; this.up = up; ni = m.rand.Next(m.noise.Length); }
        public override bool Render(double t, out double o)
        {
            o = 0; if (t > dur + 0.05) return false;
            double a = up ? 300 : 4000, b = up ? 6000 : 200; bp.F = a * Math.Pow(b / a, Math.Min(1, t / dur));
            double g = t < dur * 0.95 ? 0.0001 * Math.Pow(0.5 / 0.0001, t / (dur * 0.95)) : 0.5 * Math.Max(0, 1 - (t - dur * 0.95) / (dur * 0.05));
            o = bp.Run(m.noise[ni]) * g; ni = (ni + 1) % m.noise.Length; return true;
        }
    }

    sealed class Drone : Voice
    {
        public double f, dur; double p1, p2;
        public override bool Render(double t, out double o)
        {
            o = 0; if (t > dur + 0.05) return false;
            double g = t < dur * 0.3 ? 0.0001 + (0.16 - 0.0001) * t / (dur * 0.3) : 0.16 + (0.0001 - 0.16) * Math.Min(1, (t - dur * 0.3) / (dur * 0.7));
            p1 += f / SR; p2 += f * 1.003 / SR; o = (Math.Sin(p1 * 2 * Math.PI) + Wave("triangle", p2)) * g; return true;
        }
    }

    sealed class Blip : Voice
    {
        public double f, dd; double ph;
        public override bool Render(double t, out double o)
        {
            o = 0; if (t > dd + 0.02) return false;
            double g = t < 0.003 ? 0.06 * t / 0.003 : t < dd ? 0.06 : 0.06 * Math.Max(0, 1 - (t - dd) / 0.004);
            ph += f / SR; o = Math.Sin(ph * 2 * Math.PI) * g; return true;
        }
    }

    // ---------- effect racks ----------
    sealed class Delay
    {
        readonly double[] buf; int w;
        public Delay(double maxSec) { buf = new double[(int)(maxSec * SR) + 4]; }
        public void Write(double x) { buf[w] = x; w = (w + 1) % buf.Length; }
        public double Read(double sec)
        {
            double d = Math.Clamp(sec * SR, 1, buf.Length - 3), p = w - d; while (p < 0) p += buf.Length;
            int i = (int)p; double fr = p - i; return buf[i % buf.Length] * (1 - fr) + buf[(i + 1) % buf.Length] * fr;
        }
    }
    /// <summary>A Schroeder/Freeverb-style stereo reverb standing in for the browser's noise-impulse convolver.</summary>
    sealed class Reverb
    {
        readonly double[][] comb = new double[8][]; readonly int[] ci = new int[8]; readonly double[] cs = new double[8];
        readonly double[][] ap = new double[4][]; readonly int[] ai = new int[4]; readonly double fb, damp = 0.25;
        static readonly int[] CT = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 }, AT = { 556, 441, 341, 225 };
        public Reverb(double secs, double decay)
        {
            double sc = SR / 44100.0; for (int i = 0; i < 8; i++) comb[i] = new double[(int)(CT[i] * sc) + (i % 2) * 23];
            for (int i = 0; i < 4; i++) ap[i] = new double[(int)(AT[i] * sc)];
            fb = Math.Clamp(0.7 + secs * 0.06 - decay * 0.02, 0.6, 0.95);
        }
        public void Run(double x, out double l, out double r)
        {
            l = 0; r = 0; x *= 0.12;
            for (int i = 0; i < 8; i++) { var b = comb[i]; double y = b[ci[i]]; cs[i] = y * (1 - damp) + cs[i] * damp; b[ci[i]] = x + cs[i] * fb; ci[i] = (ci[i] + 1) % b.Length; if (i % 2 == 0) l += y; else r += y; }
            for (int i = 0; i < 4; i++) { var b = ap[i]; double y; y = b[ai[i]]; double vl = l; b[ai[i]] = vl + y * 0.5; l = y - vl * 0.5; ai[i] = (ai[i] + 1) % b.Length; }
            double m = (l + r) * 0.5; l = l * 0.8 + m * 0.2; r = r * 0.8 + m * 0.2; r = r + (l - r) * 0.1;
        }
    }

    sealed class Chain
    {
        public Song S; public string kind; public double outGain = 0.0001, target, fadeT, sendGain;
        public double bus, send, ring, ringF; double ringPh;
        public bool dying; public double dieT;
        Reverb rv; Biquad lp, bp, hp, pk, lp2, hs, sweep; Delay comb, wob, e1, e2; double combMs, combFb, combRate, combDepth, combPh;
        double wobRate, wobDepth, wobPh, echoT, echoFb, echoWet, lfoPh, lfoRate, lfoDepth, lfoBase, fadePh; double crush, drive; bool driveOn, crushOn;
        double e1v, e2v; public double CombTarget = -1; int hissI; Biquad hissHp; Music m;
        public double SweepTarget = 14000, SweepTc = 0.1;

        public Chain(Music m, Song S)
        {
            this.m = m; this.S = S; kind = S.chain; target = S.gain;
            bool big = kind == "cathedral";
            rv = new Reverb(big ? 4.5 : 2.2, big ? 1.6 : 3); sendGain = big ? 0.7 : kind == "eye" || kind == "carousel" ? 0.4 : 0.22;
            double beat = 60 / S.bpm;
            void Comb(double ms, double fb, double rate, double depth) { comb = new Delay(0.1); combMs = ms; combFb = fb; combRate = rate; combDepth = depth; }
            void Echo(double t, double fb, double wet) { e1 = new Delay(2); e2 = new Delay(2); echoT = t; echoFb = fb; echoWet = wet; }
            void Wob(double rate, double depth) { wob = new Delay(0.2); wobRate = rate; wobDepth = depth; }
            switch (kind)
            {
                case "radio": driveOn = true; drive = 2.6; hp = new Biquad("highpass", 320, 0); pk = new Biquad("peaking", 1150, 1, 6); lp = new Biquad("lowpass", 2900, 1.4); Wob(0.55, 0.0016); break;
                case "comb": driveOn = true; drive = 1.2; Comb(7, 0.45, 0.09, 4); CombTarget = 0.42; break;
                case "cathedral": lp = new Biquad("lowpass", 2400, 1); lfoRate = 0.05; lfoDepth = 1400; lfoBase = 2400; Comb(13, 0.45, 0.03, 6); break;
                case "spiral": Echo(beat * 0.75, 0.42, 0.45); bp = new Biquad("peaking", 900, 2, 8); lfoRate = 0.13; lfoDepth = 700; lfoBase = 900; break;
                case "eye": Wob(0.31, 0.004); crushOn = true; crush = 14; driveOn = true; drive = 1.4; break;
                case "carousel": Wob(0.9, 0.0025); Echo(beat * 0.5, 0.3, 0.3); break;
                case "industrial": driveOn = true; drive = 2.4; crushOn = true; crush = 10; Comb(4, 0.55, 0.21, 2.5); CombTarget = 0.42; break;
                case "dub": Echo(beat * 0.75, 0.62, 0.6); lp = new Biquad("lowpass", 2200, 1); lfoRate = 0.04; lfoDepth = 1200; lfoBase = 2200; break;
                case "underwater": lp = new Biquad("lowpass", 1400, 3); lfoRate = 0.11; lfoDepth = 700; lfoBase = 1400; Wob(0.6, 0.003); break;
                case "tape": Wob(0.45, 0.0022); driveOn = true; drive = 1.8; hs = new Biquad("highshelf", 5000, 1, -9); hissHp = new Biquad("highpass", 6000, 0); break;
                case "shimmer": Echo(beat * 0.5, 0.5, 0.4); hp = new Biquad("highpass", 900, 0); sendGain = 0.55; break;
                case "vapor": Wob(0.22, 0.005); lp = new Biquad("lowpass", 3200, 1); Echo(beat, 0.35, 0.3); sendGain = 0.6; break;
                default: kind = "clean"; Echo(beat * 0.75, 0.25, 0.25); break;
            }
            if (S.sweep) sweep = new Biquad("lowpass", 14000, 1.6);
        }
        static double Drv(double x, double k) => Math.Tanh(Math.Clamp(x, -1, 1) * k) / Math.Tanh(k);
        static double Crs(double x, double lv) => Math.Round(Math.Clamp(x, -1, 1) * lv) / lv;
        double WobRun(double x)
        {
            wobPh += wobRate / SR; double d = 0.03 + Math.Sin(wobPh * 2 * Math.PI) * wobDepth + Math.Sin(wobPh * 7.3 * 2 * Math.PI) * wobDepth * 0.15;
            wob.Write(x); return wob.Read(d);
        }
        double CombRun(double x)
        {
            if (CombTarget >= 0) combFb = Approach(combFb, CombTarget, 0.8);
            combPh += combRate / SR; double d = combMs / 1000 + Math.Sin(combPh * 2 * Math.PI) * combDepth / 1000;
            double y = comb.Read(d); comb.Write(x + y * combFb); return x + y;
        }
        void EchoRun(double x, out double l, out double r)
        {
            double y1 = e1.Read(echoT), y2 = e2.Read(echoT);
            e1.Write(x + y2 * echoFb); e2.Write(y1 * echoFb);
            // pan -0.8 / +0.8 (equal power)
            l = x + (y1 * 0.95 + y2 * 0.22) * echoWet; r = x + (y1 * 0.22 + y2 * 0.95) * echoWet;
        }
        double Lfo() { lfoPh += lfoRate / SR; return lfoBase + Math.Sin(lfoPh * 2 * Math.PI) * lfoDepth; }

        /// <summary>Processes one sample of this song's buses into stereo output.</summary>
        public void Run(out double L, out double R)
        {
            double b = bus, sd = send;
            if (sweep != null) { sweep.F = Approach(sweep.F, SweepTarget, SweepTc); b = sweep.Run(b); }
            if (ring != 0) { ringPh += ringF / SR; b += ring * Math.Sin(ringPh * 2 * Math.PI); }
            bus = send = ring = 0;
            double l = 0, r = 0, toSend = 0;
            switch (kind)
            {
                case "radio": { double x = hp.Run(Drv(b, drive)); x = lp.Run(pk.Run(x)); x = WobRun(x); fadePh += 0.07 / SR; x *= 0.85 + Math.Sin(fadePh * 2 * Math.PI) * 0.15; l = r = x; toSend = b; break; }
                case "comb": { double x = CombRun(Drv(b, drive)); l = r = x; toSend = b; break; }
                case "cathedral": { lp.F = Lfo(); double x = lp.Run(b); l = r = x; toSend = x + CombRun(b); break; }
                case "spiral": { bp.F = Lfo(); EchoRun(bp.Run(b), out l, out r); toSend = b; break; }
                case "eye": { double x = Drv(Crs(WobRun(b), crush), drive); l = r = x; toSend = x; break; }
                case "carousel": { double w = WobRun(b); EchoRun(w, out l, out r); toSend = w; break; }
                case "industrial": { double x = CombRun(Crs(Drv(b, drive), crush)); l = r = x; toSend = b; break; }
                case "dub": { lp.F = Lfo(); double x = lp.Run(b); EchoRun(x, out var el, out var er); l = x + (el - x); r = x + (er - x); toSend = (el + er) * 0.5 - x; break; }
                case "underwater": { lp.F = Lfo(); double x = lp.Run(WobRun(b)); l = r = x; toSend = x; break; }
                case "tape": { double x = hs.Run(Drv(WobRun(b), drive)); double h = hissHp.Run(m.noise[hissI]) * 0.012; hissI = (hissI + 1) % m.noise.Length; l = r = x + h; toSend = x; break; }
                case "shimmer": { EchoRun(hp.Run(b), out var el, out var er); l = b; r = b; toSend = b + (el + er) * 0.5; break; }
                case "vapor": { double x = lp.Run(WobRun(b)); EchoRun(x, out l, out r); toSend = x; break; }
                default: EchoRun(b, out l, out r); toSend = b; break;
            }
            rv.Run((sd + toSend) * sendGain, out var rl, out var rr);
            // the song's output fades in over 2.5 s (exponential), and out over 1.5 s when the next song takes over
            if (!dying) { fadeT += 1 / SR; outGain = fadeT >= 2.5 ? target : 0.0001 * Math.Pow(target / 0.0001, fadeT / 2.5); }
            else { dieT += 1 / SR; outGain = Math.Max(0.0001, outGain * Math.Pow(0.0001 / Math.Max(0.0002, target), 1 / (1.5 * SR))); }
            L = (l + rl * 2.5) * outGain; R = (r + rr * 2.5) * outGain;
        }
    }

    // ---------- scheduling ----------
    readonly List<Voice> active = new(), pending = new();
    readonly List<Chain> chains = new();
    Chain fx; Song song; List<Song> order = new(); int oi = -1, step, bar; double tNext, now;
    double compEnv = 1;

    static double Mtof(double n) => 440 * Math.Pow(2, (n - 69) / 12);

    void Schedule(Voice v, double t, int dest) { v.Start = t; v.Dest = dest; v.Ch = fx; pending.Add(v); }
    void FmNote(double t, double f, double dur, double vel, Patch p, int dest, double bend = 0) { if (p != null) Schedule(new Fm(this, p, f, dur, vel, bend), t, dest); }
    void Drum(string name, double t, double vel, int dest, double rate = 0) { if (drumBuf.TryGetValue(name, out var b)) Schedule(new Sample { Data = b, Gain = vel, Rate = rate > 0 ? rate * Audio.Rate / SR : 1 }, t, dest); }
    Patch Pa(string n) => n != null && P.TryGetValue(n, out var p) ? p : null;

    public void NextSong()
    {
        if (Songs.Count == 0) return;
        if (oi < 0 || oi >= order.Count - 1)
        {
            var lastS = song; order = new List<Song>(Songs);
            for (int i = order.Count - 1; i > 0; i--) { int j = rand.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            if (order.Count > 1 && order[0] == lastS) { order.Add(order[0]); order.RemoveAt(0); }
            oi = -1;
        }
        if (fx != null) { fx.dying = true; }
        song = order[++oi]; fx = new Chain(this, song); chains.Add(fx);
        step = 0; bar = 0; tNext = now + 0.25;
        OnSong?.Invoke(song.name);
    }

    (string parts, int pos, int len, int pass, int bar, string next, bool finale)? SectionAt(Song S, int b0)
    {
        int pass = b0 / Math.Max(1, S.bars); if (pass >= S.loops) return null;
        int b = b0 % S.bars;
        for (int si = 0; si < S.form.Count; si++)
        {
            var (n, parts) = S.form[si];
            if (b < n)
            {
                var next = si + 1 < S.form.Count ? S.form[si + 1] : S.form[0];
                return (pass > 0 && parts.Contains("bass") ? parts + " counter shaker" : parts, b, n, pass, b0 % S.bars, next.parts, pass == S.loops - 1 && si == S.form.Count - 1);
            }
            b -= n;
        }
        return null;
    }

    static readonly string[] DRUM_PARTS = { "kick", "kick2", "snare", "clap", "snap", "hat", "ohat", "ride", "zap", "heart", "tom", "conga", "rim", "cowbell", "shaker" };
    static double GridHit(string g, int i) { if (string.IsNullOrEmpty(g)) return 0; char c = g[((i % g.Length) + g.Length) % g.Length]; return c == 'x' ? 1 : c == 'o' ? 0.55 : 0; }

    int[] ChordOf(Song S, int root)
    {
        var sc = S.scale; int rm = ((root % 12) + 12) % 12, i = Array.IndexOf(sc, rm);
        if (i < 0) return new[] { root, root + 3, root + 7, root + 10 };
        var o = new int[4]; for (int k = 0; k < 4; k++) { int j = i + k * 2; o[k] = sc[j % sc.Length] + 12 * (j / sc.Length) + (root - rm); }
        return o;
    }
    int NearestScale(Song S, int n) { int best = n, bd = 99; for (int d = -2; d <= 2; d++) { int mm = ((n + d) % 12 + 12) % 12; if (Array.IndexOf(S.scale, mm) >= 0 && Math.Abs(d) < bd) { bd = Math.Abs(d); best = n + d; } } return best; }
    int MelodyNote(Song S, int key, int root, int[] ch, int oct, int deg)
    {
        int bs = key - root + ch[0] + oct, L = S.scale.Length, idx = ((deg % L) + L) % L, oc = (int)Math.Floor(deg / (double)L);
        return NearestScale(S, bs + S.scale[idx] + 12 * oc);
    }

    void PlayStep(Song S, (string parts, int pos, int len, int pass, int bar, string next, bool finale) sec, double t, int i, int bar, double len)
    {
        var F = fx; string on = sec.parts; bool has(string k) => on.Contains(k); var r = S.r; int pass = sec.pass;
        int lift = pass > 0 ? S.lift : 0, K = S.key + lift, root = S.roots[bar % S.roots.Length], key = K + root; var ch = ChordOf(S, root); var Dm = S.drums;
        bool last = sec.pos == sec.len - 1; double barLen = len * S.steps;
        int gi = S.poly ? bar * S.steps + i : i;
        foreach (var k in DRUM_PARTS)
        {
            if (!has(k) || !Dm.TryGetValue(k, out var grid)) continue;
            double v = GridHit(grid, k == "kick" || k == "snare" ? i : gi);
            if (last && i >= S.steps - 4 && (k == "snare" || k == "clap")) v = Math.Max(v, 0.6 + 0.1 * (i - S.steps + 4));
            if (v == 0) continue;
            string buf = k == "shaker" ? "hat" : k == "conga" ? "tom" : k == "ride" ? "ohat" : k;
            double rate = k == "shaker" ? 1.7 + r.Next() * 0.2 : k == "rim" ? 1 + 0.1 * (i % 3) : k == "conga" ? new[] { 1.9, 2.4, 1.6 }[gi % 3] : k == "ride" ? 1.8 : 0;
            Drum(buf, t, v * (k == "shaker" ? 0.22 : k == "hat" || k == "ohat" ? 0.5 : k == "ride" ? 0.3 : k == "kick" || k == "heart" || k == "kick2" ? 0.95 : k == "cowbell" ? 0.35 : 0.7), 0, rate);
        }
        if (S.sweep && i == 0) { F.SweepTarget = has("kick") || has("kick2") || has("heart") ? 14000 : S.sweepLow != 0 ? S.sweepLow : 800; F.SweepTc = sec.pos == 0 ? 0.08 : 1.2; }
        if (last && i == 0 && (has("kick") || sec.next.Contains("kick")) && !sec.finale) Drum("crash", t + barLen, 0.4, 1);
        if (has("tom") && last && i >= S.steps - 3) Drum("tom", t, 0.6, 0, 1 + (i - S.steps + 3) * 0.2);
        if (last && i == 0 && !sec.finale && (has("riser") || (sec.next.Contains("kick") && !has("kick")))) Schedule(new Riser(this, barLen, true), t, 1);
        if (has("sub") && i == 0) Schedule(new Drone { f = Mtof(key - 12), dur = barLen * 1.02 }, t, 0);
        if (has("bass") && S.bass != null)
        {
            var B = S.bass; double v = GridHit(B.grid, i); var notes = pass > 0 && B.altNotes != null ? B.altNotes : B.notes;
            if (v > 0 && notes != null)
            {
                int up = B.patch == "fmBass" || B.patch == "growl" ? 0 : 12;
                int n = key + up + notes[i % notes.Length]; double dur = B.len * len * 0.9;
                int nxt = B.slide && GridHit(B.grid, i + 1) > 0 ? key + up + notes[(i + 1) % notes.Length] : 0;
                FmNote(t, Mtof(n), dur, v, Pa(B.patch), 0, nxt != 0 && r.Next() < 0.3 ? Mtof(nxt) / Mtof(n) : 0);
            }
        }
        if (has("pad") && S.pad != null && i == 0)
        {
            var notes = S.pad.cluster ? new[] { ch[0], ch[0] + 1, ch[0] + 7, ch[0] + 13 } : ch;
            foreach (var n in notes) FmNote(t, Mtof(K + n + S.pad.oct), barLen * 0.98, 0.8, Pa(S.pad.patch), 1);
            for (int q = 0; q < 2; q++) FmNote(t, Mtof(K + notes[q] + S.pad.oct), barLen * 0.98, 0.5, Pa(S.pad.patch), 0);
        }
        if (has("stab") && S.stab != null && GridHit(S.stab.grid, i) > 0) for (int q = 0; q < 3; q++) FmNote(t, Mtof(K + ch[q] + S.stab.oct + 12), len * 1.8, 0.55, Pa(S.stab.patch), r.Next() < 0.6 ? 0 : 1);
        if (has("arp") && S.arp != null && GridHit(S.arp.grid, i) > 0)
        {
            int k = i % 4, up = (bar % 2) != 0 ? 3 - k : k;
            int n = K + ch[up] + S.arp.oct + (i >= S.steps / 2 && bar % 4 == 3 ? 12 : 0) + (pass > 0 && bar % 8 >= 4 ? 12 : 0);
            FmNote(t, Mtof(n), len * 1.6, 0.7, Pa(S.arp.patch), 0); if (r.Next() < 0.3) FmNote(t, Mtof(n), len * 1.6, 0.4, Pa(S.arp.patch), 1);
        }
        if (has("glass") && S.glass != null && (i % 4 == 2 || (i == S.steps - 1 && r.Next() < 0.5))) FmNote(t, Mtof(K + ch[1 + (bar + i) % 3] + S.glass.oct), len * 2, 0.55, Pa(S.glass.patch), r.Next() < 0.5 ? 0 : 1);
        int mi = (pass > 0 ? 2 : 0) + ((bar >> 1) % 4 == 2 ? 1 : 0);
        foreach (var L in new[] { has("lead") ? S.lead : null, has("bell") ? S.bell : null })
        {
            if (L == null) continue;
            var m = S.motifs[mi]; int off = (bar % 2) * S.steps + i; var patch = Pa(pass > 0 && L.alt != null ? L.alt : L.patch);
            foreach (var nt in m) if (nt.s == off)
                {
                    int oct = L.oct + (pass > 0 && bar % 8 >= 6 ? 12 : 0), dg = nt.deg + ((bar >> 1) % 4 == 1 ? 1 : 0);
                    int n = MelodyNote(S, key, root, ch, oct, dg);
                    if (L.ring) F.ringF = Mtof(n) * 1.5;
                    FmNote(t, Mtof(n), nt.len * len * 0.95, nt.acc, patch, L.ring ? 2 : 0, L.bend && nt.len >= 2 && r.Next() < 0.4 ? (r.Next() < 0.5 ? 0.94 : 1.06) : 0);
                    if (L.harmSet && (pass > 0 || L.harmAlways)) FmNote(t, Mtof(MelodyNote(S, key, root, ch, oct, dg + L.harm)), nt.len * len * 0.95, nt.acc * 0.55, patch, 1);
                    FmNote(t, Mtof(n), nt.len * len * 0.95, nt.acc * 0.5, patch, 1);
                }
        }
        if (has("counter") && S.counter != null)
        {
            var C = S.counter; var m = S.motifs[(mi + 1) % 4]; int off = ((bar % 2) * S.steps + i + S.steps / 2) % (S.steps * 2);
            foreach (var nt in m) if (nt.s == off && r.Next() < 0.8) FmNote(t, Mtof(MelodyNote(S, key, root, ch, C.oct, 2 - nt.deg)), nt.len * len * 1.2, nt.acc * 0.7, Pa(C.patch), r.Next() < 0.5 ? 0 : 1);
        }
        if (has("choir") && S.choir != null && i == 0 && bar % 2 == 0)
        {
            var C = S.choir; string vw = C.vowels ?? "a"; char v = vw[(bar >> 1) % vw.Length];
            Schedule(new Choir(this, Mtof(key - root + ch[0] + C.oct), barLen * 2, 0.7, v, false), t, 1);
            Schedule(new Choir(this, Mtof(key - root + ch[0] + C.oct + (bar % 4 != 0 ? 6 : 7)), barLen * 2, 0.5, v, false), t, 1);
        }
        if (has("wail") && S.wail != null && i == 0 && bar % 2 == 1) FmNote(t, Mtof(key - root + ch[2] + S.wail.oct), barLen * 1.9, 0.8, Pa(S.wail.patch), 1, r.Next() < 0.5 ? 0.89 : 1.12);
        if (has("whisper") && i == (S.steps >> 1) && r.Next() < 0.5) Schedule(new Choir(this, 0, len * 6, 0.8, "aeiou"[(int)Math.Floor(r.Next() * 5)], true), t, 1);
        if (has("blip") && i % 2 == 0 && r.Next() < 0.09)
        {
            double f = 1180 + (bar % 3) * 90; int cnt = 1 + (int)Math.Floor(r.Next() * 3);
            for (int k = 0; k < cnt; k++) Schedule(new Blip { f = f, dd = r.Next() < 0.4 ? 0.09 : 0.035 }, t + k * len * 0.4, 0);
        }
        if (sec.finale && last && i == 0) { FmNote(t, Mtof(K + 12), barLen * 2, 0.8, Pa("bell"), 1); FmNote(t, Mtof(K), barLen * 2, 0.6, Pa("fmBass"), 0); }
        if (F.CombTarget >= 0 && i == 0) F.CombTarget = !on.Contains("kick") ? 0.78 : 0.42;
    }

    void Tick()
    {
        var S = song; if (S == null) return;
        double st = 60 / S.bpm / 4;
        if (tNext < now) tNext = now + 0.05;
        while (tNext < now + 0.2)
        {
            var sec = SectionAt(S, bar);
            if (sec == null) { NextSong(); return; }
            PlayStep(S, sec.Value, tNext, step, sec.Value.bar, st);
            tNext += st * (step % 2 != 0 ? 1 - S.swing : 1 + S.swing);
            step++; if (step >= S.steps) { step = 0; bar++; }
        }
    }

    double tickAcc;
    public bool Mix(float[] buf, int frames)
    {
        if (!Enabled) { now += frames / SR; return true; }
        if (song == null) NextSong();
        tickAcc += frames / SR; if (tickAcc >= 0.03) { tickAcc = 0; Tick(); }
        double t0 = now;
        for (int i = pending.Count - 1; i >= 0; i--) if (pending[i].Start < t0 + frames / SR) { active.Add(pending[i]); pending.RemoveAt(i); }
        for (int f = 0; f < frames; f++)
        {
            double t = t0 + f / SR;
            for (int vi = active.Count - 1; vi >= 0; vi--)
            {
                var v = active[vi]; double lt = t - v.Start; if (lt < 0) continue;
                if (!v.Render(lt, out var s)) { active.RemoveAt(vi); continue; }
                var c = v.Ch; if (v.Dest == 0) c.bus += s; else if (v.Dest == 1) c.send += s; else c.ring += s;
            }
            double L = 0, R = 0;
            for (int ci = chains.Count - 1; ci >= 0; ci--)
            {
                var c = chains[ci]; c.Run(out var l, out var r); L += l; R += r;
                if (c.dying && c.dieT > 2.5) { chains.RemoveAt(ci); active.RemoveAll(v => v.Ch == c); pending.RemoveAll(v => v.Ch == c); }
            }
            // the music master's compressor (threshold -18 dB, ratio 8) as a simple peak follower
            double pkv = Math.Max(Math.Abs(L), Math.Abs(R)), th = 0.126;
            double want = pkv > th ? (th + (pkv - th) / 8) / pkv : 1;
            compEnv = want < compEnv ? compEnv + (want - compEnv) * 0.02 : compEnv + (want - compEnv) * 0.0003;
            buf[f * 2] += (float)(L * compEnv * 0.6 * 1.6); buf[f * 2 + 1] += (float)(R * compEnv * 0.6 * 1.6);
        }
        now = t0 + frames / SR;
        return true;
    }
}

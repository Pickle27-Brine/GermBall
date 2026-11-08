using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using GermBall.Audio;
using Vortice.Multimedia;
using Vortice.XAudio2;

namespace GermBall.Win;

/// <summary>
/// 1.0's software mixer, feeding one XAudio2 source voice. Sound effects are sample buffers; music and voices are generators
/// (anything that can fill a stereo block). The mixer runs on its own thread so frame hitches never click.
/// </summary>
public sealed unsafe class AudioOut : IDisposable
{
    public const int Rate = AudioFormat.Rate, Block = 768;   // 16 ms blocks, four queued: enough slack that a frame hitch never starves the device
    IXAudio2 xa; IXAudio2MasteringVoice master; IXAudio2SourceVoice src;
    readonly Thread thread; volatile bool running = true;
    readonly object gate = new();
    readonly List<Voice> voices = new();
    readonly List<IAudioGenerator> gens = new();
    readonly AutoResetEvent bufferEnd = new(false);
    public float Master = 0.7f, MusicVolume = 0.45f, SfxVolume = 1;
    public bool Ok { get; private set; }
    /// <summary>Why there is no sound, if there isn't.</summary>
    public string Error { get; private set; }
    public static AudioOut Instance { get; private set; }


    sealed class Voice
    {
        public float[] Data; public double Pos, Rate; public float Gain, Pan; public bool Loop; public int Id;
        public float GainTarget, GainStep;
    }

    public AudioOut()
    {
        Instance = this;
        try
        {
            xa = XAudio2.XAudio2Create(ProcessorSpecifier.DefaultProcessor);
            master = xa.CreateMasteringVoice(2, Rate);
            var fmt = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
            src = xa.CreateSourceVoice(fmt, VoiceFlags.None, 2f);
            src.BufferEnd += _ => bufferEnd.Set();
            src.Start();
            Ok = true;
        }
        catch (Exception e) { Error = e.Message; Ok = false; }
        thread = new Thread(Run) { IsBackground = true, Name = "Audio mixer", Priority = ThreadPriority.AboveNormal };
        if (Ok) thread.Start();
    }

    int nextId = 1;
    /// <summary>Plays a mono buffer (rate = playback speed, pan -1..1). Returns a handle for Stop.</summary>
    public int Play(float[] data, float gain = 1, float rate = 1, float pan = 0, bool loop = false)
    {
        if (!Ok || data == null || data.Length == 0) return 0;
        lock (gate)
        {
            // too many voices: the oldest fades out over a few milliseconds instead of being cut (a cut is a click)
            if (voices.Count > 96) { foreach (var o in voices) if (o.GainTarget > 0) { o.GainTarget = 0; o.GainStep = -o.Gain / (0.008f * Rate); break; } }
            var v = new Voice { Data = data, Rate = rate * (data.Length > 0 ? 1 : 0), Gain = gain, GainTarget = gain, Pan = Math.Clamp(pan, -1, 1), Loop = loop, Id = nextId++ };
            voices.Add(v); return v.Id;
        }
    }
    public void SetGain(int id, float gain, float seconds = 0.05f)
    {
        lock (gate) foreach (var v in voices) if (v.Id == id) { v.GainTarget = gain; v.GainStep = (gain - v.Gain) / MathF.Max(1, seconds * Rate); }
    }
    /// <summary>Stops a voice with a very short fade (never a hard cut).</summary>
    public void Stop(int id) { lock (gate) foreach (var v in voices) if (v.Id == id) { v.GainTarget = 0; v.GainStep = -MathF.Max(1e-6f, v.Gain) / (0.01f * Rate); } }
    public void Add(IAudioGenerator g) { lock (gate) gens.Add(g); }
    public void Remove(IAudioGenerator g) { lock (gate) gens.Remove(g); }

    void Run()
    {
        const int nbuf = 4;
        var bufs = new float[nbuf][]; var handles = new GCHandle[nbuf];
        for (int i = 0; i < nbuf; i++) { bufs[i] = new float[Block * 2]; handles[i] = GCHandle.Alloc(bufs[i], GCHandleType.Pinned); }
        var music = new float[Block * 2]; int which = 0;
        while (running)
        {
            if (src.State.BuffersQueued >= nbuf) { bufferEnd.WaitOne(20); continue; }
            var b = bufs[which]; Array.Clear(b); Array.Clear(music);
            lock (gate)
            {
                for (int vi = voices.Count - 1; vi >= 0; vi--)
                {
                    var v = voices[vi]; float gl = MathF.Min(1, 1 - v.Pan) * SfxVolume, gr = MathF.Min(1, 1 + v.Pan) * SfxVolume; bool done = false;
                    for (int f = 0; f < Block; f++)
                    {
                        int i0 = (int)v.Pos; if (i0 >= v.Data.Length - 1) { if (v.Loop) { v.Pos -= v.Data.Length - 1; i0 = (int)v.Pos; } else { done = true; break; } }
                        float t = (float)(v.Pos - i0), s = (v.Data[i0] + (v.Data[i0 + 1] - v.Data[i0]) * t) * v.Gain;
                        // de-click: every one-shot fades in over its first 1.5 ms and out over its last 3 ms, whatever the buffer holds
                        if (!v.Loop) { double rem = v.Data.Length - 1 - v.Pos; if (v.Pos < 72) s *= (float)(v.Pos / 72); if (rem < 144) s *= (float)Math.Max(0, rem / 144); }
                        b[f * 2] += s * gl; b[f * 2 + 1] += s * gr; v.Pos += v.Rate;
                        if (v.GainStep != 0) { v.Gain += v.GainStep; if ((v.GainStep > 0 && v.Gain >= v.GainTarget) || (v.GainStep < 0 && v.Gain <= v.GainTarget)) { v.Gain = v.GainTarget; v.GainStep = 0; } }
                    }
                    if (done || (v.GainTarget <= 0 && v.Gain <= 0.0001f)) voices.RemoveAt(vi);
                }
                for (int gi = gens.Count - 1; gi >= 0; gi--)
                {
                    var g = gens[gi];
                    bool alive;
                    try { alive = g.Mix(g.IsMusic ? music : b, Block); } catch (Exception e) { Error = "audio generator failed: " + e.Message; alive = false; }
                    if (!alive) gens.RemoveAt(gi);
                }
            }
            float mv = MusicVolume, m = Master;
            for (int f = 0; f < Block; f++)
            {
                // a limiter instead of a hard ceiling: an envelope follower (instant attack, ~120 ms release) pulls the gain down
                // so peaks land under 0.95; anything left over is rounded off by a soft clip rather than squared off (squared = crackle)
                float l = (b[f * 2] + music[f * 2] * mv) * m, r = (b[f * 2 + 1] + music[f * 2 + 1] * mv) * m;
                float pk = MathF.Max(MathF.Abs(l), MathF.Abs(r));
                env = pk > env ? pk : env + (pk - env) * 0.00017f;
                float g = env > 0.95f ? 0.95f / env : 1;
                gain += (g - gain) * (g < gain ? 0.25f : 0.0004f);
                b[f * 2] = Soft(l * gain); b[f * 2 + 1] = Soft(r * gain);
            }
            var ab = new AudioBuffer { AudioDataPointer = handles[which].AddrOfPinnedObject(), AudioBytes = (uint)(b.Length * 4) };
            src.SubmitSourceBuffer(ab);
            which = (which + 1) % nbuf;
        }
        for (int i = 0; i < nbuf; i++) handles[i].Free();
    }

    float env, gain = 1;
    static float Soft(float x) { float a = MathF.Abs(x); if (a <= 0.9f) return x; float o = 0.9f + 0.1f * MathF.Tanh((a - 0.9f) / 0.1f); return MathF.Sign(x) * o; }

    public void Dispose()
    {
        running = false; bufferEnd.Set();
        if (thread.IsAlive) thread.Join(500);
        src?.DestroyVoice(); master?.Dispose(); xa?.Dispose();
    }
}

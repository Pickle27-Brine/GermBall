using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GermBall.Platform;

/// <summary>
/// Companion voices through Windows SAPI (the same voices the browser's speechSynthesis used on Windows), driven by
/// COM late binding so no interop assembly is needed. Each companion gets its own voice, pitch and rate.
/// </summary>
public static class Speech
{
    sealed class VoiceDef { public int Pitch, Rate, Pick; public Regex Pref; }
    static readonly Dictionary<string, VoiceDef> VOICE = new()
    {
        // browser pitch 0..2 → SAPI absolute pitch -10..10; browser rate ~0.5..2 → SAPI rate -10..10
        ["ally"] = new VoiceDef { Pitch = -2, Rate = 0, Pick = 0, Pref = new Regex("(male|daniel|david|alex|fred|guy|george)", RegexOptions.IgnoreCase) },
        ["ally2"] = new VoiceDef { Pitch = -8, Rate = -2, Pick = 1, Pref = new Regex("(male|bruce|ralph|mark|james|guy)", RegexOptions.IgnoreCase) },
        ["ally3"] = new VoiceDef { Pitch = 7, Rate = 1, Pick = 2, Pref = new Regex("(female|samantha|zira|victoria|karen|aria|jenny)", RegexOptions.IgnoreCase) },
    };
    static dynamic sp; static readonly List<(dynamic token, string name)> voices = new();
    static bool tried; static bool ok;
    /// <summary>The companion currently talking (music ducks while set).</summary>
    public static string Talking;

    public static bool Available { get { Init(); return ok; } }

    static void Init()
    {
        if (tried) return; tried = true;
        try
        {
            var t = Type.GetTypeFromProgID("SAPI.SpVoice"); if (t == null) return;
            sp = Activator.CreateInstance(t);
            dynamic list = sp.GetVoices();
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                dynamic tok = list.Item(i); string desc = tok.GetDescription();
                string lang = ""; try { lang = tok.GetAttribute("Language"); } catch { }
                // 409/809/c09/1009... are English locales (hex LCIDs ending in 09)
                if (lang.Length == 0 || lang.Split(';')[0].ToLowerInvariant().EndsWith("09")) voices.Add((tok, desc));
            }
            ok = true;
        }
        catch (Exception e) { Log.Warn("SAPI speech unavailable: " + e.Message); ok = false; }
    }

    public static bool Busy
    {
        get { if (!ok) return false; try { return (int)sp.Status.RunningState == 2; } catch { return false; } }
    }

    public static void Speak(string type, string text, bool urgent, float volume)
    {
        Init(); if (!ok || volume <= 0) return;
        try
        {
            if (Busy) { if (!urgent) return; }
            var V = VOICE.TryGetValue(type ?? "", out var vd) ? vd : VOICE["ally"];
            if (voices.Count > 0)
            {
                dynamic pick = null;
                foreach (var v in voices) if (V.Pref.IsMatch(v.name)) { pick = v.token; break; }
                pick ??= voices[V.Pick % voices.Count].token;
                sp.Voice = pick;
            }
            sp.Rate = V.Rate; sp.Volume = (int)Math.Clamp(volume * 100, 0, 100);
            string clean = System.Security.SecurityElement.Escape(text.Replace("—", ", ").Replace("–", ", "));
            const int ASYNC = 1, PURGE = 2, XML = 8;
            sp.Speak("<pitch absmiddle=\"" + V.Pitch + "\">" + clean + "</pitch>", ASYNC | XML | (urgent ? PURGE : 0));
            Talking = type;
        }
        catch (Exception e) { Log.Warn("speech failed: " + e.Message); }
    }

    /// <summary>Called every frame: clears Talking when the voice finishes.</summary>
    public static void Update() { if (Talking != null && !Busy) Talking = null; }

    public static void Cancel()
    {
        if (!ok) return;
        try { sp.Speak("", 2); } catch { }
        Talking = null;
    }
}

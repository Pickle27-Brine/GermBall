using System.Collections.Generic;
using GermBall.Platform;

namespace GermBall.Game;

/// <summary>Settings and key bindings on disk (Documents\My Games\GermBall\settings.json and keys.json).</summary>
public static class Prefs
{
    static Settings set; static Dictionary<string, string[]> binds;
    public static Settings Settings => set ??= Paths.Load<Settings>(Paths.Settings) ?? new Settings();
    public static Dictionary<string, string[]> Binds
    {
        get
        {
            if (binds != null) return binds;
            binds = Paths.Load<Dictionary<string, string[]>>(Paths.Keys) ?? World.DefaultBinds();
            // key maps saved before punch and reload existed: R was "back to base"; it is now reload
            if (!binds.ContainsKey("reload") && binds.TryGetValue("base", out var bb) && bb != null) for (int i = 0; i < bb.Length; i++) if (bb[i] == "KeyR") bb[i] = "KeyB";
            foreach (var d in World.BIND_DEF) if (!binds.TryGetValue(d.key, out var b) || b == null || b.Length < 2) binds[d.key] = new[] { d.a, d.b };
            return binds;
        }
    }
    public static void SaveSettings() => Paths.Save(Paths.Settings, Settings);
    public static void SaveBinds() => Paths.Save(Paths.Keys, Binds);
}

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GermBall.Platform;

/// <summary>
/// George Lopez's fun facts: every flip looks one up on the web (a random-fact service, a cat-fact service as a backup),
/// keeps it short, and falls back to a built-in list when there's no connection.
/// </summary>
public static class FunFacts
{
    static readonly HttpClient http = MakeClient();
    static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("GermBall/2.0 (+fun facts on the title screen)");
        return h;
    }
    public const int MaxLen = 110;
    static readonly string[] OFFLINE =
    {
        "Honey never spoils. Archaeologists have eaten 3,000-year-old honey.",
        "Octopuses have three hearts and blue blood.",
        "A day on Venus is longer than its year.",
        "Bananas are berries. Strawberries aren't.",
        "Your body has more bacterial cells than human ones. Roughly.",
        "Penicillin was found because a lab dish got moldy.",
        "Sharks are older than trees.",
        "A group of flamingos is called a flamboyance.",
        "Wombat poop is cube-shaped.",
        "The Eiffel Tower grows about 15 cm in summer heat.",
        "Tardigrades can survive in space.",
        "Hot water can freeze faster than cold. Sometimes.",
        "There are more possible chess games than atoms in the universe.",
        "A cloud can weigh over a million pounds.",
        "Sloths can hold their breath longer than dolphins.",
    };
    static readonly Random rnd = new(); static int lastOffline = -1;
    static string Offline() { int i; do i = rnd.Next(OFFLINE.Length); while (i == lastOffline && OFFLINE.Length > 1); lastOffline = i; return OFFLINE[i]; }

    /// <summary>Squeezes whitespace and quotes, and keeps it to one short sentence (or rejects it).</summary>
    static string Tidy(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = Regex.Replace(s.Replace('`', '\'').Replace("\"", ""), @"\s+", " ").Trim();
        if (s.Length <= MaxLen) return s;
        int cut = s.IndexOf(". ", StringComparison.Ordinal);
        if (cut > 20 && cut + 1 <= MaxLen) return s[..(cut + 1)];
        return null;
    }

    static async Task<string> Fetch(string url, string field)
    {
        try
        {
            var json = await http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var t) ? t.GetString() : null;
        }
        catch (Exception e) { Log.Info("fun fact lookup failed: " + e.Message); return null; }
    }

    /// <summary>A fresh, short fact. Never throws; offline it comes from the built-in list.</summary>
    public static async Task<string> Next()
    {
        for (int i = 0; i < 3; i++)
        {
            var f = Tidy(await Fetch("https://uselessfacts.jsph.pl/api/v2/facts/random?language=en", "text").ConfigureAwait(false));
            if (f != null) return f;
        }
        var c = Tidy(await Fetch("https://catfact.ninja/fact?max_length=" + MaxLen, "fact").ConfigureAwait(false));
        return c ?? Offline();
    }
}

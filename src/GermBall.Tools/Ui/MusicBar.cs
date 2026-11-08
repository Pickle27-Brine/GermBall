using System;
using System.Linq;
using System.Numerics;
using GermBall.Audio;

namespace GermBall.Tools.Ui;

/// <summary>The music player strip the editors put at the right end of their toolbar: back, pause/play, skip, loop,
/// and the song playing (click it to pick another). The same 1.0 songs and player as the game.</summary>
public static class MusicBar
{
    /// <summary>How wide the strip wants to be for a toolbar <paramref name="room"/> wide.</summary>
    public static float Width(Gui ui, float room) => Math.Clamp(room, 0, 46 * ui.CharW);

    public static void Draw(Gui ui, Music m, Vector4 r)
    {
        if (m == null || r.Z < 20 * ui.CharW) return;
        ui.BeginArea("music", r, Gui.TitleBg);
        float b = 4 * ui.CharW;
        if (ui.Button("<<", b)) m.Previous(); ui.Hint("previous song (or back to the start of this one)");
        ui.SameLine(); if (ui.Button(m.Paused ? "|>" : "||", b)) m.TogglePause(); ui.Hint(m.Paused ? "play the music" : "pause the music");
        ui.SameLine(); if (ui.Button(">>", b)) m.Next(); ui.Hint("next song");
        ui.SameLine(); if (ui.Toggle("Loop", m.Loop, 6 * ui.CharW)) m.Loop = !m.Loop; ui.Hint("repeat this song");
        float rest = r.Z - 2 * ui.Pad - 3 * b - 6 * ui.CharW - 4 * ui.Gap;
        if (rest > 8 * ui.CharW)
        {
            ui.SameLine();
            string name = m.Current.Length > 0 ? m.Current : "Music";
            var pick = ui.Menu(name, m.Songs.Select(s => s.name).ToList(), rest);
            if (pick != null) m.Play(m.Songs.FindIndex(s => s.name == pick));
            ui.Hint((m.Paused ? "paused" : "playing") + (m.Loop ? ", looping" : "") + ". Click to pick a song");
        }
        ui.EndArea();
    }
}

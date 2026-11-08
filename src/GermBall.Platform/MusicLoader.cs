using System.IO;
using GermBall.Audio;
using GermBall.Content;

namespace GermBall.Win;

/// <summary>Loads 1.0's songs from content/music and starts them on the mixer (the game and the editors share this).</summary>
public static class MusicLoader
{
    /// <summary>The music player, playing (or paused if <paramref name="play"/> is false); null if there's no sound or no songs.</summary>
    public static Music Start(AudioOut audio, ContentStore store, bool play = true)
    {
        if (audio == null || !audio.Ok || store?.Root == null) return null;
        var dir = Path.Combine(store.Root, "music");
        if (!File.Exists(Path.Combine(dir, "songs.json"))) return null;
        var m = Music.Load(dir);
        if (m.Songs.Count == 0) return null;
        m.Paused = !play;
        audio.Add(m);
        return m;
    }
}

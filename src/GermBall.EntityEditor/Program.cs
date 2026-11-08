using System;
using GermBall.Content;
using GermBall.Win;

namespace GermBall.EntityEditor.Host;

public static class Program
{
    /// <summary>EntityEditor.exe [entity name] [--content dir]</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            int ci = Array.IndexOf(args, "--content");
            var store = ContentStore.Find(ci >= 0 && ci + 1 < args.Length ? args[ci + 1] : null);
            string open = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
            var ed = new GermBall.Tools.EntityEditor.EntityEditor(store, open);
            ed.LaunchGame = a => { if (!EditorHost.LaunchGame(a, out var err)) ed.Status = err; };
            using var audio = new AudioOut();
            ed.Music = MusicLoader.Start(audio, store);
            EditorHost.Run("Germ Ball Entity Editor", ed.Frame);
            return 0;
        }
        catch (Exception e) { return EditorHost.Crash("EntityEditor", e); }
    }
}

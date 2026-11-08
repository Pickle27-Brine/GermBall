using System;
using GermBall.Content;
using GermBall.Win;

namespace GermBall.MapEditor.Host;

public static class Program
{
    /// <summary>MapEditor.exe [map name] [--content dir]</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            int ci = Array.IndexOf(args, "--content");
            var store = ContentStore.Find(ci >= 0 && ci + 1 < args.Length ? args[ci + 1] : null);
            string open = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
            var ed = new GermBall.Tools.MapEditor.MapEditor(store, open);
            ed.LaunchGame = a => { if (!EditorHost.LaunchGame(a, out var err)) ed.Status = err; };
            EditorHost.Run("Germ Ball Map Editor", ed.Frame);
            return 0;
        }
        catch (Exception e) { return EditorHost.Crash("MapEditor", e); }
    }
}

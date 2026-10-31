using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GermBall.Platform;

/// <summary>
/// Plain, open file locations. Nothing is hidden in a browser profile any more: every file the game reads or writes is an
/// ordinary file you can open, edit, back up or share.
///
///   Data   (read):   &lt;game folder&gt;\data\           music, textures, the title picture, mods
///   Saves  (write):  Documents\My Games\GermBall\     settings.json, keys.json, custom.json, boot.json, germs\*.germ.json, screenshots\
///
/// Both can be moved: put a paths.json next to GermBall.exe ({"data": "D:\\GermData", "saves": "D:\\GermSaves"}),
/// or pass --data &lt;dir&gt; / --saves &lt;dir&gt; on the command line. Relative paths resolve against the game folder.
/// </summary>
public static class Paths
{
    public static string GameDir { get; private set; } = AppContext.BaseDirectory;
    public static string Data { get; private set; }
    public static string Saves { get; private set; }
    public static string Germs => Dir(Path.Combine(Saves, "germs"));
    public static string Screenshots => Dir(Path.Combine(Saves, "screenshots"));
    public static string Logs => Dir(Path.Combine(Saves, "logs"));

    public static string Settings => Path.Combine(Saves, "settings.json");
    public static string Keys => Path.Combine(Saves, "keys.json");
    public static string Custom => Path.Combine(Saves, "custom.json");
    public static string Boot => Path.Combine(Saves, "boot.json");

    sealed class PathsFile { public string data { get; set; } public string saves { get; set; } }

    public static void Init(string[] args)
    {
        string data = null, saves = null;
        var pf = Path.Combine(GameDir, "paths.json");
        if (File.Exists(pf))
        {
            try { var p = JsonSerializer.Deserialize<PathsFile>(File.ReadAllText(pf)); data = p?.data; saves = p?.saves; }
            catch (Exception e) { Log.Warn($"paths.json ignored: {e.Message}"); }
        }
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--data") data = args[i + 1];
            if (args[i] == "--saves") saves = args[i + 1];
        }
        Data = Resolve(data) ?? Path.Combine(GameDir, "data");
        Saves = Resolve(saves) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "GermBall");
        Dir(Data); Dir(Saves);
    }

    static string Resolve(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        p = Environment.ExpandEnvironmentVariables(p);
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(GameDir, p));
    }

    public static string Dir(string d) { Directory.CreateDirectory(d); return d; }

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, IncludeFields = true, PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };

    public static T Load<T>(string file) where T : class
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) : null; }
        catch (Exception e) { Log.Warn($"Could not read {file}: {e.Message}"); return null; }
    }

    /// <summary>Atomic save: write a temp file next to the target, then swap it in, so a crash never leaves half a file.</summary>
    public static void Save<T>(string file, T value)
    {
        try
        {
            Dir(Path.GetDirectoryName(file));
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            File.Move(tmp, file, true);
        }
        catch (Exception e) { Log.Warn($"Could not write {file}: {e.Message}"); }
    }

    /// <summary>Opens a folder in Explorer (Settings → "Open saves folder").</summary>
    public static void Reveal(string dir) => Win32.ShellExecuteW(IntPtr.Zero, "open", dir, null, null, Win32.SW_SHOW);
}

public static class Log
{
    static readonly object Gate = new();
    static StreamWriter w;
    public static void Init()
    {
        try { w = new StreamWriter(Path.Combine(Paths.Logs, "germball.log"), false) { AutoFlush = true }; } catch { }
        Info($"Germ Ball starting. Game folder: {Paths.GameDir}  Data: {Paths.Data}  Saves: {Paths.Saves}");
    }
    public static void Info(string s) => Write("INFO", s);
    public static void Warn(string s) => Write("WARN", s);
    public static void Error(string s) => Write("FAIL", s);
    static void Write(string lvl, string s)
    {
        lock (Gate) { var line = $"{DateTime.Now:HH:mm:ss.fff} {lvl} {s}"; System.Diagnostics.Debug.WriteLine(line); w?.WriteLine(line); }
    }
}

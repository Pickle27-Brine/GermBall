using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GermBall.Content;

/// <summary>
/// The content folder shared by the game and the editors: <c>content/entities</c> and <c>content/maps</c>.
/// It sits next to the exes; while developing, the repository's own <c>content</c> folder is found by walking up.
/// Anything missing falls back to the built-in defaults, so the game always runs.
/// </summary>
public sealed class ContentStore
{
    public readonly string Root;
    public string EntitiesDir => Path.Combine(Root, "entities");
    public string MapsDir => Path.Combine(Root, "maps");
    readonly Dictionary<string, EntityDef> entities = new();

    public ContentStore(string root) { Root = root; }

    /// <summary>The content folder next to the program, or the nearest one above it (a repository checkout).</summary>
    public static ContentStore Find(string explicitRoot = null)
    {
        if (!string.IsNullOrEmpty(explicitRoot)) return new ContentStore(Path.GetFullPath(explicitRoot));
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            for (int i = 0; dir != null && i < 8; i++, dir = dir.Parent)
            {
                var c = Path.Combine(dir.FullName, "content");
                if (Directory.Exists(Path.Combine(c, "entities")) || Directory.Exists(Path.Combine(c, "maps"))) return new ContentStore(c);
            }
        }
        return new ContentStore(Path.Combine(AppContext.BaseDirectory, "content"));
    }

    /// <summary>A store with nothing on disk: only the built-in defaults (tests use this).</summary>
    public static ContentStore BuiltIn() => new(null);

    public string EntityPath(string name) => Path.Combine(EntitiesDir, name + ".entity.json");
    public string MapPath(string name) => Path.Combine(MapsDir, name + ".map.json");

    public EntityDef Entity(string name)
    {
        if (entities.TryGetValue(name, out var e)) return e;
        e = null;
        if (Root != null && File.Exists(EntityPath(name)))
        {
            try { e = Json.Load<EntityDef>(EntityPath(name)); } catch (Exception ex) { Errors.Add($"{name}.entity.json: {ex.Message}"); }
        }
        e ??= DefaultEntities.Get(name) ?? DefaultEntities.Get("coccus");
        entities[name] = e;
        return e;
    }
    public void Forget() => entities.Clear();

    /// <summary>A map by name or path, or null if there isn't one.</summary>
    public MapDef Map(string nameOrPath)
    {
        string path = File.Exists(nameOrPath) ? nameOrPath : Root != null ? MapPath(nameOrPath) : null;
        if (path == null || !File.Exists(path)) return null;
        try { return Json.Load<MapDef>(path); } catch (Exception ex) { Errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); return null; }
    }

    public IEnumerable<string> EntityNames()
    {
        var names = new SortedSet<string>(DefaultEntities.Names);
        if (Root != null && Directory.Exists(EntitiesDir))
            foreach (var f in Directory.GetFiles(EntitiesDir, "*.entity.json")) names.Add(Path.GetFileName(f)[..^".entity.json".Length]);
        return names;
    }
    public IEnumerable<string> MapNames()
    {
        var names = new SortedSet<string>();
        if (Root != null && Directory.Exists(MapsDir))
            foreach (var f in Directory.GetFiles(MapsDir, "*.map.json")) names.Add(Path.GetFileName(f)[..^".map.json".Length]);
        return names;
    }

    public void SaveEntity(EntityDef e) { Directory.CreateDirectory(EntitiesDir); Json.Save(EntityPath(e.Name), e); entities[e.Name] = e; }
    public void SaveMap(MapDef m) { Directory.CreateDirectory(MapsDir); Json.Save(MapPath(m.Name), m); }

    public readonly List<string> Errors = new();
}

/// <summary>Readable, indented JSON for content files (vectors as [x, y, z]).</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, IncludeFields = true, PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new Vec3Converter() },
    };
    public static string Write<T>(T v) => JsonSerializer.Serialize(v, Options);
    public static T Read<T>(string s) => JsonSerializer.Deserialize<T>(s, Options);
    public static T Load<T>(string path) => Read<T>(File.ReadAllText(path));
    /// <summary>Atomic save: write a temp file, then swap it in.</summary>
    public static void Save<T>(string path, T v)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Write(v));
        File.Move(tmp, path, true);
    }
    public static T Clone<T>(T v) => Read<T>(Write(v));

    sealed class Vec3Converter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        {
            if (r.TokenType == JsonTokenType.StartArray)
            {
                var v = new float[3]; int i = 0;
                while (r.Read() && r.TokenType != JsonTokenType.EndArray) { if (i < 3) v[i] = r.GetSingle(); i++; }
                return new Vector3(v[0], v[1], v[2]);
            }
            if (r.TokenType == JsonTokenType.StartObject)
            {
                var res = Vector3.Zero;
                while (r.Read() && r.TokenType != JsonTokenType.EndObject)
                {
                    var name = r.GetString(); r.Read(); float f = r.GetSingle();
                    switch (name?.ToLowerInvariant()) { case "x": res.X = f; break; case "y": res.Y = f; break; case "z": res.Z = f; break; }
                }
                return res;
            }
            throw new JsonException("expected [x, y, z]");
        }
        public override void Write(Utf8JsonWriter w, Vector3 v, JsonSerializerOptions o)
        {
            static string F(float f) => MathF.Round(f, 4).ToString("0.####", CultureInfo.InvariantCulture);
            w.WriteRawValue($"[{F(v.X)}, {F(v.Y)}, {F(v.Z)}]", skipInputValidation: true);
        }
    }
}

public static class ColorHex
{
    public static Vector4 Parse(string hex, float alpha = 1)
    {
        if (string.IsNullOrEmpty(hex)) return Vector4.One;
        var h = hex.TrimStart('#');
        if (h.Length != 6 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return Vector4.One;
        return new Vector4(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, alpha);
    }
    public static string Format(Vector3 c) =>
        "#" + ((int)MathF.Round(Math.Clamp(c.X, 0, 1) * 255)).ToString("X2") + ((int)MathF.Round(Math.Clamp(c.Y, 0, 1) * 255)).ToString("X2") + ((int)MathF.Round(Math.Clamp(c.Z, 0, 1) * 255)).ToString("X2");
}

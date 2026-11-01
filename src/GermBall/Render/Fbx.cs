using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace GermBall.Render;

/// <summary>
/// FBX in and out, for meshes. Reading takes binary FBX (any version from 7.x, compressed arrays included) and ASCII FBX,
/// merges every mesh in the file and triangulates it. Writing produces a binary FBX 7.4 file (what Blender, Maya, 3ds Max
/// and Unity read) with one mesh per colour, each with its own material, in metres, Y up.
/// </summary>
public static class Fbx
{
    // ---------------------------------------------------------------- reading
    sealed class Node { public string Name; public List<object> Props = new(); public List<Node> Kids = new(); }

    /// <summary>Loads every mesh in an FBX file as one triangle list (positions in file units, Y up as the file says).</summary>
    public static (float[] pos, uint[] idx) Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var pos = new List<float>(); var idx = new List<uint>();
        if (bytes.Length > 23 && Encoding.ASCII.GetString(bytes, 0, 18) == "Kaydara FBX Binary")
        {
            int ver = BitConverter.ToInt32(bytes, 23); int at = 27; bool wide = ver >= 7500;
            var root = new List<Node>();
            while (true) { var n = ReadNode(bytes, ref at, wide); if (n == null) break; root.Add(n); }
            foreach (var top in root) if (top.Name == "Objects") foreach (var o in top.Kids) if (o.Name == "Geometry") AddMesh(o, pos, idx);
        }
        else
        {   // ASCII: pull the vertex and polygon arrays out of each Geometry block
            var text = Encoding.UTF8.GetString(bytes);
            foreach (Match g in Regex.Matches(text, @"Geometry:\s*[^\{]*\{(.*?)\n\t\}", RegexOptions.Singleline))
            {
                var body = g.Groups[1].Value;
                var v = Regex.Match(body, @"Vertices:\s*\*\d+\s*\{\s*a:\s*([^\}]*)\}"); var p = Regex.Match(body, @"PolygonVertexIndex:\s*\*\d+\s*\{\s*a:\s*([^\}]*)\}");
                if (!v.Success || !p.Success) continue;
                var vs = Array.ConvertAll(v.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries), x => double.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture));
                var ps = Array.ConvertAll(p.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries), x => int.Parse(x.Trim()));
                Append(vs, ps, pos, idx);
            }
        }
        return (pos.ToArray(), idx.ToArray());
    }

    static void AddMesh(Node geo, List<float> pos, List<uint> idx)
    {
        double[] vs = null; int[] ps = null;
        foreach (var k in geo.Kids)
        {
            if (k.Name == "Vertices" && k.Props.Count > 0) vs = k.Props[0] switch { double[] d => d, float[] f => Array.ConvertAll(f, x => (double)x), _ => null };
            if (k.Name == "PolygonVertexIndex" && k.Props.Count > 0) ps = k.Props[0] switch { int[] i => i, long[] l => Array.ConvertAll(l, x => (int)x), _ => null };
        }
        if (vs != null && ps != null) Append(vs, ps, pos, idx);
    }

    // FBX polygons: a list of vertex indices where the last index of each polygon is stored as -(i+1); fan-triangulate each
    static void Append(double[] vs, int[] ps, List<float> pos, List<uint> idx)
    {
        uint b = (uint)(pos.Count / 3);
        foreach (var d in vs) pos.Add((float)d);
        var poly = new List<int>();
        foreach (int raw in ps)
        {
            bool end = raw < 0; poly.Add(end ? -raw - 1 : raw);
            if (!end) continue;
            for (int k = 1; k + 1 < poly.Count; k++) { idx.Add(b + (uint)poly[0]); idx.Add(b + (uint)poly[k]); idx.Add(b + (uint)poly[k + 1]); }
            poly.Clear();
        }
    }

    static Node ReadNode(byte[] d, ref int at, bool wide)
    {
        long end = wide ? BitConverter.ToInt64(d, at) : BitConverter.ToUInt32(d, at); at += wide ? 8 : 4;
        long nprops = wide ? BitConverter.ToInt64(d, at) : BitConverter.ToUInt32(d, at); at += wide ? 8 : 4;
        at += wide ? 8 : 4;   // property list length
        int nl = d[at++]; string name = Encoding.ASCII.GetString(d, at, nl); at += nl;
        if (end == 0) return null;
        var n = new Node { Name = name };
        for (int i = 0; i < nprops; i++) n.Props.Add(ReadProp(d, ref at));
        while (at < end)
        {
            var k = ReadNode(d, ref at, wide); if (k == null) break;
            n.Kids.Add(k);
        }
        at = (int)end;
        return n;
    }

    static object ReadProp(byte[] d, ref int at)
    {
        char t = (char)d[at++];
        switch (t)
        {
            case 'Y': { var v = BitConverter.ToInt16(d, at); at += 2; return v; }
            case 'C': return d[at++] != 0;
            case 'I': { var v = BitConverter.ToInt32(d, at); at += 4; return v; }
            case 'F': { var v = BitConverter.ToSingle(d, at); at += 4; return v; }
            case 'D': { var v = BitConverter.ToDouble(d, at); at += 8; return v; }
            case 'L': { var v = BitConverter.ToInt64(d, at); at += 8; return v; }
            case 'S': case 'R': { int len = BitConverter.ToInt32(d, at); at += 4; var v = t == 'S' ? (object)Encoding.UTF8.GetString(d, at, len) : d[at..(at + len)]; at += len; return v; }
            case 'f': case 'd': case 'l': case 'i': case 'b':
                {
                    int count = BitConverter.ToInt32(d, at), enc = BitConverter.ToInt32(d, at + 4), clen = BitConverter.ToInt32(d, at + 8); at += 12;
                    byte[] raw = d[at..(at + clen)]; at += clen;
                    if (enc == 1) { using var zs = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress); using var ms = new MemoryStream(); zs.CopyTo(ms); raw = ms.ToArray(); }
                    switch (t)
                    {
                        case 'f': { var a = new float[count]; Buffer.BlockCopy(raw, 0, a, 0, count * 4); return a; }
                        case 'd': { var a = new double[count]; Buffer.BlockCopy(raw, 0, a, 0, count * 8); return a; }
                        case 'l': { var a = new long[count]; Buffer.BlockCopy(raw, 0, a, 0, count * 8); return a; }
                        case 'i': { var a = new int[count]; Buffer.BlockCopy(raw, 0, a, 0, count * 4); return a; }
                        default: return raw;
                    }
                }
            default: throw new InvalidDataException("unknown FBX property type " + t);
        }
    }

    // ---------------------------------------------------------------- writing (binary FBX 7.4)
    public sealed class Part { public string Name; public Vector3 Color; public List<float> Pos = new(); public List<int> Poly = new(); }

    sealed class W
    {
        readonly BinaryWriter b; public W(BinaryWriter b) { this.b = b; }
        public void Node(string name, object[] props, Action kids = null)
        {
            long start = b.BaseStream.Position;
            b.Write(0u); b.Write((uint)props.Length); b.Write(0u); b.Write((byte)name.Length); b.Write(Encoding.ASCII.GetBytes(name));
            long p0 = b.BaseStream.Position;
            foreach (var p in props) Prop(p);
            long p1 = b.BaseStream.Position;
            if (kids != null) { kids(); b.Write(new byte[13]); }   // children end with a null record
            long end = b.BaseStream.Position;
            b.BaseStream.Position = start; b.Write((uint)end); b.Write((uint)props.Length); b.Write((uint)(p1 - p0)); b.BaseStream.Position = end;
        }
        void Prop(object p)
        {
            switch (p)
            {
                case bool v: b.Write((byte)'C'); b.Write((byte)(v ? 1 : 0)); break;
                case short v: b.Write((byte)'Y'); b.Write(v); break;
                case int v: b.Write((byte)'I'); b.Write(v); break;
                case float v: b.Write((byte)'F'); b.Write(v); break;
                case double v: b.Write((byte)'D'); b.Write(v); break;
                case long v: b.Write((byte)'L'); b.Write(v); break;
                case string v: { var s = Encoding.UTF8.GetBytes(v); b.Write((byte)'S'); b.Write(s.Length); b.Write(s); break; }
                case double[] a: b.Write((byte)'d'); b.Write(a.Length); b.Write(0); b.Write(a.Length * 8); foreach (var x in a) b.Write(x); break;
                case int[] a: b.Write((byte)'i'); b.Write(a.Length); b.Write(0); b.Write(a.Length * 4); foreach (var x in a) b.Write(x); break;
                default: throw new ArgumentException("FBX property " + p?.GetType());
            }
        }
    }
    static string N(string name, string cls) => name + "\u0000\u0001" + cls;
    static object[] P(params object[] a) => a;

    /// <summary>Writes the parts as one binary FBX 7.4 file: a mesh and a material per part, all under the scene root.</summary>
    public static void Save(string path, IList<Part> parts, string creator = "Germ Ball")
    {
        using var fs = File.Create(path); using var bw = new BinaryWriter(fs);
        bw.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  ")); bw.Write((byte)0); bw.Write((byte)0x1a); bw.Write((byte)0); bw.Write(7400u);
        var w = new W(bw);
        w.Node("FBXHeaderExtension", P(), () =>
        {
            w.Node("FBXHeaderVersion", P(1003)); w.Node("FBXVersion", P(7400)); w.Node("Creator", P(creator));
        });
        w.Node("GlobalSettings", P(), () =>
        {
            w.Node("Version", P(1000));
            w.Node("Properties70", P(), () =>
            {
                void Pi(string n, int v) => w.Node("P", P(n, "int", "Integer", "", v));
                Pi("UpAxis", 1); Pi("UpAxisSign", 1); Pi("FrontAxis", 2); Pi("FrontAxisSign", 1); Pi("CoordAxis", 0); Pi("CoordAxisSign", 1);
                Pi("OriginalUpAxis", 1); Pi("OriginalUpAxisSign", 1);
                w.Node("P", P("UnitScaleFactor", "double", "Number", "", 100.0)); w.Node("P", P("OriginalUnitScaleFactor", "double", "Number", "", 100.0));
            });
        });
        w.Node("Documents", P(), () => { w.Node("Count", P(1)); w.Node("Document", P(1L, "Scene", "Scene"), () => w.Node("RootNode", P(0L))); });
        w.Node("Definitions", P(), () =>
        {
            w.Node("Version", P(100)); w.Node("Count", P(1 + parts.Count * 3));
            w.Node("ObjectType", P("GlobalSettings"), () => w.Node("Count", P(1)));
            w.Node("ObjectType", P("Model"), () => w.Node("Count", P(parts.Count)));
            w.Node("ObjectType", P("Geometry"), () => w.Node("Count", P(parts.Count)));
            w.Node("ObjectType", P("Material"), () => w.Node("Count", P(parts.Count)));
        });
        long id = 1000000;
        var ids = new List<(long geo, long model, long mat)>();
        foreach (var _ in parts) { ids.Add((id, id + 1, id + 2)); id += 3; }
        w.Node("Objects", P(), () =>
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var pt = parts[i]; var (g, m, mt) = ids[i];
                w.Node("Geometry", P(g, N(pt.Name, "Geometry"), "Mesh"), () =>
                {
                    w.Node("Vertices", P(Array.ConvertAll(pt.Pos.ToArray(), x => (double)x)));
                    w.Node("PolygonVertexIndex", P(pt.Poly.ToArray()));
                    w.Node("GeometryVersion", P(124));
                    w.Node("LayerElementMaterial", P(0), () =>
                    {
                        w.Node("Version", P(101)); w.Node("Name", P("")); w.Node("MappingInformationType", P("AllSame")); w.Node("ReferenceInformationType", P("IndexToDirect"));
                        w.Node("Materials", P(new[] { 0 }));
                    });
                    w.Node("Layer", P(0), () => { w.Node("Version", P(100)); w.Node("LayerElement", P(), () => { w.Node("Type", P("LayerElementMaterial")); w.Node("TypedIndex", P(0)); }); });
                });
                w.Node("Model", P(m, N(pt.Name, "Model"), "Mesh"), () =>
                {
                    w.Node("Version", P(232));
                    w.Node("Properties70", P(), () =>
                    {
                        w.Node("P", P("Lcl Translation", "Lcl Translation", "", "A", 0.0, 0.0, 0.0));
                        w.Node("P", P("Lcl Rotation", "Lcl Rotation", "", "A", 0.0, 0.0, 0.0));
                        w.Node("P", P("Lcl Scaling", "Lcl Scaling", "", "A", 100.0, 100.0, 100.0));   // metres in a centimetre file
                    });
                    w.Node("Shading", P(true)); w.Node("Culling", P("CullingOff"));
                });
                w.Node("Material", P(mt, N(pt.Name + "_mat", "Material"), ""), () =>
                {
                    w.Node("Version", P(102)); w.Node("ShadingModel", P("phong")); w.Node("MultiLayer", P(0));
                    w.Node("Properties70", P(), () =>
                    {
                        w.Node("P", P("DiffuseColor", "Color", "", "A", (double)pt.Color.X, (double)pt.Color.Y, (double)pt.Color.Z));
                        w.Node("P", P("Diffuse", "Vector3D", "Vector", "", (double)pt.Color.X, (double)pt.Color.Y, (double)pt.Color.Z));
                    });
                });
            }
        });
        w.Node("Connections", P(), () =>
        {
            foreach (var (g, m, mt) in ids) { w.Node("C", P("OO", m, 0L)); w.Node("C", P("OO", g, m)); w.Node("C", P("OO", mt, m)); }
        });
        bw.Write(new byte[13]);   // the top level ends with a null record, then the standard footer
        bw.Write(new byte[] { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e });
        int pad = (int)((16 - (fs.Position % 16)) % 16); if (pad == 0) pad = 16; bw.Write(new byte[pad]);
        bw.Write(0); bw.Write(7400u); bw.Write(new byte[120]);
        bw.Write(new byte[] { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b });
    }
}

/// <summary>
/// The player's own models: every .fbx in data/models and in the saves folder's models folder, loaded at start, centred and
/// scaled to fit a unit shape (radius 1 across, half-height 1), and offered as segment and attachment shapes ("model:name").
/// </summary>
public static class ModelLibrary
{
    public static readonly Dictionary<string, char> Keys = new();          // model name → emit letter (private-use characters)
    public static readonly Dictionary<char, Geometry> Shapes = new();
    public static readonly List<string> Names = new();
    public static readonly List<string> Errors = new();

    public static void Load(params string[] dirs)
    {
        char next = '\uE000';
        foreach (var dir in dirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.fbx"))
            {
                string name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Replace(' ', '-');
                if (Keys.ContainsKey(name)) continue;
                try
                {
                    var (pos, idx) = Fbx.Load(f); if (pos.Length < 9 || idx.Length < 3) { Errors.Add(name + ": no mesh"); continue; }
                    var g = Normalise(pos, idx); char k = next++;
                    Keys[name] = k; Shapes[k] = g; Names.Add(name);
                }
                catch (Exception e) { Errors.Add(name + ": " + e.Message); }
            }
        }
    }

    /// <summary>Centres the mesh and scales it into the unit shape the body segments use (x and z to ±1, y to ±1), flat-shaded.</summary>
    static Geometry Normalise(float[] p, uint[] idx)
    {
        var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
        for (int i = 0; i < p.Length; i += 3) { var v = new Vector3(p[i], p[i + 1], p[i + 2]); mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
        var c = (mn + mx) * 0.5f; var h = Vector3.Max((mx - mn) * 0.5f, new Vector3(1e-4f));
        float sxz = 1 / MathF.Max(h.X, h.Z), sy = 1 / h.Y;
        // unshared vertices with face normals: imported models keep their hard edges
        var pos = new float[idx.Length * 3]; var nor = new float[idx.Length * 3]; var ix = new uint[idx.Length];
        for (int t = 0; t < idx.Length; t += 3)
        {
            Vector3 V(uint i) => new((p[i * 3] - c.X) * sxz, (p[i * 3 + 1] - c.Y) * sy, (p[i * 3 + 2] - c.Z) * sxz);
            var a = V(idx[t]); var b = V(idx[t + 1]); var d = V(idx[t + 2]); var n = Vector3.Cross(b - a, d - a); n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
            int o = t * 3; foreach (var q in new[] { a, b, d }) { pos[o] = q.X; pos[o + 1] = q.Y; pos[o + 2] = q.Z; nor[o] = n.X; nor[o + 1] = n.Y; nor[o + 2] = n.Z; o += 3; }
            ix[t] = (uint)t; ix[t + 1] = (uint)(t + 1); ix[t + 2] = (uint)(t + 2);
        }
        return new Geometry { Position = pos, Normal = nor, Index = ix };
    }
}

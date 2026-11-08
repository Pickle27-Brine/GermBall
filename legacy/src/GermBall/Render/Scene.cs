using System;
using System.Collections.Generic;
using System.Numerics;

namespace GermBall.Render;

/// <summary>CPU-side geometry. Bump <see cref="Version"/> after editing arrays so the GPU copy is refreshed.</summary>
public sealed class Geometry
{
    public float[] Position = Array.Empty<float>();   // xyz
    public float[] Normal;                            // xyz
    public float[] Uv;                                // uv
    public float[] Color;                             // rgb per vertex (vertexColors)
    public uint[] Index;
    public int Version = 1;
    public int DrawStart, DrawCount = -1;             // index (or vertex) range; -1 = all
    public Vector3 BoundCenter; public float BoundRadius = -1;
    internal object Gpu; internal int GpuVersion;
    public int VertexCount => Position.Length / 3;

    public void ComputeBounds()
    {
        int n = VertexCount; if (n == 0) { BoundRadius = 0; return; }
        Vector3 mn = new(float.MaxValue), mx = new(float.MinValue);
        for (int i = 0; i < n; i++) { var p = new Vector3(Position[i * 3], Position[i * 3 + 1], Position[i * 3 + 2]); mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
        BoundCenter = (mn + mx) * 0.5f; float r2 = 0;
        for (int i = 0; i < n; i++) { var p = new Vector3(Position[i * 3], Position[i * 3 + 1], Position[i * 3 + 2]); r2 = MathF.Max(r2, Vector3.DistanceSquared(p, BoundCenter)); }
        BoundRadius = MathF.Sqrt(r2);
    }

    /// <summary>Smooth vertex normals from the triangles (three.js computeVertexNormals).</summary>
    public void ComputeNormals()
    {
        int n = VertexCount; var nr = new float[n * 3];
        void Tri(int a, int b, int c)
        {
            var pa = P(a); var cb = P(c) - P(b); var ab = pa - P(b); var f = Vector3.Cross(cb, ab);
            for (int k = 0; k < 3; k++) { nr[a * 3 + k] += k == 0 ? f.X : k == 1 ? f.Y : f.Z; nr[b * 3 + k] += k == 0 ? f.X : k == 1 ? f.Y : f.Z; nr[c * 3 + k] += k == 0 ? f.X : k == 1 ? f.Y : f.Z; }
        }
        if (Index != null) for (int i = 0; i + 2 < Index.Length; i += 3) Tri((int)Index[i], (int)Index[i + 1], (int)Index[i + 2]);
        else for (int i = 0; i + 2 < n; i += 3) Tri(i, i + 1, i + 2);
        for (int i = 0; i < n; i++) { var v = new Vector3(nr[i * 3], nr[i * 3 + 1], nr[i * 3 + 2]); float l = v.Length(); if (l > 0) v /= l; nr[i * 3] = v.X; nr[i * 3 + 1] = v.Y; nr[i * 3 + 2] = v.Z; }
        Normal = nr; Version++;
    }
    public Vector3 P(int i) => new(Position[i * 3], Position[i * 3 + 1], Position[i * 3 + 2]);

    /// <summary>Bakes a transform into the positions and normals (three.js applyMatrix4).</summary>
    public Geometry Apply(Matrix4x4 m)
    {
        Matrix4x4.Invert(m, out var inv); var nm = Matrix4x4.Transpose(inv);
        for (int i = 0; i < VertexCount; i++)
        {
            var p = Vector3.Transform(P(i), m); Position[i * 3] = p.X; Position[i * 3 + 1] = p.Y; Position[i * 3 + 2] = p.Z;
            if (Normal != null) { var q = Vector3.Normalize(Vector3.TransformNormal(new Vector3(Normal[i * 3], Normal[i * 3 + 1], Normal[i * 3 + 2]), nm)); Normal[i * 3] = q.X; Normal[i * 3 + 1] = q.Y; Normal[i * 3 + 2] = q.Z; }
        }
        Version++; BoundRadius = -1; return this;
    }
    public Geometry Translate(float x, float y, float z) => Apply(Matrix4x4.CreateTranslation(x, y, z));
    public Geometry Scale(float x, float y, float z) => Apply(Matrix4x4.CreateScale(x, y, z));
    public Geometry RotateX(float a) => Apply(Matrix4x4.CreateRotationX(a));
    public Geometry RotateY(float a) => Apply(Matrix4x4.CreateRotationY(a));
    public Geometry RotateZ(float a) => Apply(Matrix4x4.CreateRotationZ(a));

    /// <summary>Converts to non-indexed triangles (for flat shading or per-face colours).</summary>
    public Geometry ToNonIndexed()
    {
        if (Index == null) return Clone();
        var g = new Geometry(); int n = Index.Length;
        g.Position = new float[n * 3]; if (Normal != null) g.Normal = new float[n * 3]; if (Uv != null) g.Uv = new float[n * 2]; if (Color != null) g.Color = new float[n * 3];
        for (int i = 0; i < n; i++)
        {
            int s = (int)Index[i];
            Array.Copy(Position, s * 3, g.Position, i * 3, 3);
            if (Normal != null) Array.Copy(Normal, s * 3, g.Normal, i * 3, 3);
            if (Uv != null) Array.Copy(Uv, s * 2, g.Uv, i * 2, 2);
            if (Color != null) Array.Copy(Color, s * 3, g.Color, i * 3, 3);
        }
        return g;
    }
    public Geometry Clone()
    {
        return new Geometry
        {
            Position = (float[])Position.Clone(), Normal = (float[])Normal?.Clone(), Uv = (float[])Uv?.Clone(), Color = (float[])Color?.Clone(), Index = (uint[])Index?.Clone(),
            BoundCenter = BoundCenter, BoundRadius = BoundRadius, DrawStart = DrawStart, DrawCount = DrawCount,
        };
    }
    /// <summary>Flat-shaded normals (one per face) for faceted looks.</summary>
    public Geometry Flat()
    {
        var g = ToNonIndexed(); g.Normal = new float[g.Position.Length];
        for (int i = 0; i + 2 < g.VertexCount; i += 3)
        {
            var f = Vector3.Normalize(Vector3.Cross(g.P(i + 2) - g.P(i + 1), g.P(i) - g.P(i + 1)));
            for (int k = 0; k < 3; k++) { g.Normal[(i + k) * 3] = f.X; g.Normal[(i + k) * 3 + 1] = f.Y; g.Normal[(i + k) * 3 + 2] = f.Z; }
        }
        return g;
    }
    public Geometry SetColor(float r, float g, float b) { Color = new float[Position.Length]; for (int i = 0; i < VertexCount; i++) { Color[i * 3] = r; Color[i * 3 + 1] = g; Color[i * 3 + 2] = b; } Version++; return this; }

    /// <summary>Merges geometries into one (positions, normals, and colours/uvs when every part has them).</summary>
    public static Geometry Merge(IList<Geometry> parts)
    {
        var g = new Geometry(); int nv = 0, ni = 0; bool col = true, uv = true;
        foreach (var p in parts) { nv += p.VertexCount; ni += p.Index?.Length ?? p.VertexCount; col &= p.Color != null; uv &= p.Uv != null; }
        g.Position = new float[nv * 3]; g.Normal = new float[nv * 3]; g.Index = new uint[ni]; if (col) g.Color = new float[nv * 3]; if (uv) g.Uv = new float[nv * 2];
        int vo = 0, io = 0;
        foreach (var p in parts)
        {
            Array.Copy(p.Position, 0, g.Position, vo * 3, p.Position.Length);
            if (p.Normal != null) Array.Copy(p.Normal, 0, g.Normal, vo * 3, p.Normal.Length);
            if (col) Array.Copy(p.Color, 0, g.Color, vo * 3, p.Color.Length);
            if (uv) Array.Copy(p.Uv, 0, g.Uv, vo * 2, p.Uv.Length);
            if (p.Index != null) for (int i = 0; i < p.Index.Length; i++) g.Index[io++] = (uint)(p.Index[i] + vo);
            else for (int i = 0; i < p.VertexCount; i++) g.Index[io++] = (uint)(vo + i);
            vo += p.VertexCount;
        }
        return g;
    }
}

public enum Side { Front, Back, Double }
public enum Blend { Normal, Additive, None }
public enum MatKind { Lambert, Phong, Basic, Points, Line }

/// <summary>Material: the subset of three.js material options the game uses.</summary>
public sealed class Material
{
    public MatKind Kind = MatKind.Lambert;
    public Vector3 Color = Vector3.One;
    public Vector3 Emissive = Vector3.Zero;
    public Vector3 Specular = new(0x2a / 255f, 0x2f / 255f, 0x36 / 255f);
    public float Shininess = 30;
    public float Opacity = 1, AlphaTest;
    public bool Transparent, VertexColors, DepthWrite = true, DepthTest = true, Fog = true, FlatShading, PolygonOffset, Wireframe;
    public float PolygonOffsetFactor = -1;
    public Side Side = Side.Front;
    public Blend Blending = Blend.Normal;
    public Texture Map;
    public Vector2 MapRepeat = Vector2.One, MapOffset = Vector2.Zero;
    public float Size = 1; public bool SizeAttenuation = true;   // points
    /// <summary>Toon steps: brightness snapped to this many levels through an ordered dither (0 = smooth).</summary>
    public float Toon;
    public bool Dashed; public float DashSize = 1, GapSize = 1;  // lines
    /// <summary>A named special shader ("sky", "roof", "villi", "terrain"...), with its own uniforms.</summary>
    public string Shader;
    public Vector4 U0, U1, U2, U3;           // custom shader uniforms
    public Vector4[] UArr;                   // custom shader array uniform (up to 8)
    public Material Clone() { var m = (Material)MemberwiseClone(); m.UArr = (Vector4[])UArr?.Clone(); return m; }

    public static Vector3 Hex(uint c) => new(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);
    public static Material Lambert(uint c) => new() { Kind = MatKind.Lambert, Color = Hex(c) };
    public static Material Phong(uint c) => new() { Kind = MatKind.Phong, Color = Hex(c) };
    public static Material Basic(uint c) => new() { Kind = MatKind.Basic, Color = Hex(c) };
}

/// <summary>An RGBA8 image used as a texture. Edit <see cref="Pixels"/> then bump <see cref="Version"/>.</summary>
public sealed class Texture
{
    public int Width, Height; public byte[] Pixels;   // RGBA
    public bool Repeat, Nearest = true, Mipmaps = true;
    public int Version = 1;
    internal object Gpu; internal int GpuVersion;
    public Texture(int w, int h) { Width = w; Height = h; Pixels = new byte[w * h * 4]; }
    public static Texture From(Canvas2D c, bool repeat = false) { var t = new Texture(c.W, c.H) { Repeat = repeat }; c.CopyTo(t.Pixels); return t; }
}

public class Node
{
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.Identity;
    public Vector3 Scale = Vector3.One;
    public bool Visible = true;
    public bool MatrixAutoUpdate = true;
    public Matrix4x4 Local = Matrix4x4.Identity;
    public Matrix4x4 World = Matrix4x4.Identity;
    public Node Parent;
    public readonly List<Node> Children = new();
    public int RenderOrder;
    public string Name;
    public object Tag;

    public T Add<T>(T n) where T : Node { n.Parent?.Children.Remove(n); n.Parent = this; Children.Add(n); return n; }
    public void Remove(Node n) { if (Children.Remove(n)) n.Parent = null; }
    public void RemoveFromParent() => Parent?.Remove(this);
    public void SetEuler(float x, float y, float z)
    {
        var q = new Core.Q4(); Core.M.QEuler(x, y, z, q); Rotation = new Quaternion(q.x, q.y, q.z, q.w);
    }
    public void UpdateWorld(in Matrix4x4 parent)
    {
        if (MatrixAutoUpdate) Local = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);
        World = Local * parent;
        foreach (var c in Children) c.UpdateWorld(World);
    }
}

public class Mesh : Node
{
    public Geometry Geo; public Material Mat;
    public bool CastShadow, ReceiveShadow, FrustumCulled = true;
    public Mesh(Geometry g, Material m) { Geo = g; Mat = m; }
}

/// <summary>Many copies of one geometry in one draw call. Per-instance matrix and optional colour.</summary>
public sealed class InstancedMesh : Mesh
{
    public int Count, Capacity;
    public Matrix4x4[] Matrices;
    public Vector3[] Colors;          // null = white
    public int Version = 1;           // bump after edits
    internal object Gpu; internal int GpuVersion;
    public InstancedMesh(Geometry g, Material m, int capacity) : base(g, m)
    {
        Capacity = capacity; Count = capacity; Matrices = new Matrix4x4[capacity]; FrustumCulled = false;
        for (int i = 0; i < capacity; i++) Matrices[i] = Matrix4x4.Identity;
    }
    public void SetMatrix(int i, in Matrix4x4 m) { Matrices[i] = m; }
    public void SetColor(int i, Vector3 c) { Colors ??= Fill(Capacity); Colors[i] = c; }
    static Vector3[] Fill(int n) { var a = new Vector3[n]; Array.Fill(a, Vector3.One); return a; }
    public void Touch() => Version++;
    /// <summary>Grows the buffers (the browser build had to fix these up front; here they simply grow).</summary>
    public void EnsureCapacity(int n)
    {
        if (n <= Capacity) return;
        int c = Math.Max(n, Capacity * 2);
        Array.Resize(ref Matrices, c); if (Colors != null) { int o = Colors.Length; Array.Resize(ref Colors, c); for (int i = o; i < c; i++) Colors[i] = Vector3.One; }
        Capacity = c; Version++;
    }
}

/// <summary>Camera-facing square sprites, one per vertex (three.js Points).</summary>
public sealed class Points : Node
{
    public Geometry Geo; public Material Mat;
    public Points(Geometry g, Material m) { Geo = g; Mat = m; }
}

/// <summary>Line segments (pairs of vertices) or a strip.</summary>
public sealed class Lines : Node
{
    public Geometry Geo; public Material Mat; public bool Strip;
    public Lines(Geometry g, Material m, bool strip = false) { Geo = g; Mat = m; Strip = strip; }
}

public sealed class Scene : Node
{
    public Vector3 Background = new(0.84f, 0.64f, 0.9f);
    public Vector3 FogColor = new(0.84f, 0.64f, 0.9f);
    public float FogNear = 130, FogFar = 680;
    public Vector3 HemiSky = Vector3.One, HemiGround = new(0.2f), SunColor = Vector3.One, SunDir = Vector3.Normalize(new Vector3(-0.35f, 0.8f, 0.45f));
    public float HemiIntensity = 0.78f, SunIntensity = 0.85f;
    public bool Shadows = true; public int ShadowMapSize = 2048;
    /// <summary>Centre and half-size of the sun's shadow box (it follows the camera target).</summary>
    public Vector3 ShadowCenter; public float ShadowHalf = 38, ShadowNear = 1, ShadowFar = 260, ShadowBias = -0.0006f, ShadowNormalBias = 0.03f;
    public float Time;
}

public sealed class Camera
{
    public Vector3 Position; public Quaternion Rotation = Quaternion.Identity;
    public float Fov = 66, Aspect = 16f / 9, Near = 0.1f, Far = 3500;
    public Matrix4x4 View => Matrix4x4.CreateTranslation(-Position) * Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(Rotation));
    public Matrix4x4 Proj => Matrix4x4.CreatePerspectiveFieldOfView(Fov * MathF.PI / 180, Aspect, Near, Far);
    public void LookAt(Vector3 target, Vector3? up = null)
    {
        var m = Matrix4x4.CreateLookAt(Position, target, up ?? Vector3.UnitY);
        Matrix4x4.Invert(m, out var w); Rotation = Quaternion.CreateFromRotationMatrix(w);
    }
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);
    /// <summary>World point to normalised device coordinates (x, y in -1..1, z &gt; 1 or w &lt; 0 when behind).</summary>
    public Vector3 Project(Vector3 p)
    {
        var v = Vector4.Transform(new Vector4(p, 1), View * Proj);
        if (v.W <= 0) return new Vector3(0, 0, 2);
        return new Vector3(v.X / v.W, v.Y / v.W, v.Z / v.W);
    }
}

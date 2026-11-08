using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using GermBall.Platform;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace GermBall.Render;

/// <summary>
/// Direct3D 11 forward renderer for the scene graph: hemisphere + sun lighting, a PCF shadow map that follows the camera,
/// linear fog, instancing for everything that repeats, transparent sorting, and a 2D pass for the HUD and menus.
/// Windows only, by design: no WebGL limits on buffer sizes, texture sizes or draw counts.
/// </summary>
public sealed unsafe class Renderer : IDisposable, IRenderer
{
    public ID3D11Device Device { get; private set; }
    public ID3D11DeviceContext Ctx { get; private set; }
    IDXGISwapChain1 swap; IDXGIFactory2 factory;
    ID3D11RenderTargetView rtv; ID3D11Texture2D depthTex; ID3D11DepthStencilView dsv;
    ID3D11Texture2D shadowTex; ID3D11DepthStencilView shadowDsv; ID3D11ShaderResourceView shadowSrv; int shadowSize;
    ID3D11Buffer frameCb, objectCb, identityInst, quadVb, quadIb, uiCb, uiVb;
    ID3D11InputLayout layout, uiLayout;
    readonly Dictionary<string, (ID3D11VertexShader vs, ID3D11PixelShader ps)> programs = new();
    ID3D11VertexShader shadowVs; ID3D11PixelShader shadowPs, uiPs; ID3D11VertexShader uiVs;
    ID3D11SamplerState sampNearestWrap, sampNearestClamp, sampLinearWrap, sampLinearClamp, sampShadow;
    ID3D11RasterizerState rsBack, rsFront, rsNone, rsBackOffset, rsNoneOffset, rsShadow, rsScissor;
    ID3D11BlendState bsOpaque, bsAlpha, bsAdd;
    ID3D11DepthStencilState dsOn, dsReadOnly, dsOff;
    Texture white;
    readonly bool tearing;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool VSync { get; set; } = true;
    public int DrawCalls { get; private set; }
    public int Triangles { get; private set; }
    /// <summary>Render resolution scale (the original's pixelated look rendered small and scaled up; 1 = native).</summary>
    public float RenderScale { get; set; } = 1;
    // low-resolution scene target (the original rendered a few hundred lines and scaled up with nearest filtering)
    ID3D11Texture2D lowTex, lowDepth; ID3D11RenderTargetView lowRtv; ID3D11ShaderResourceView lowSrv; ID3D11DepthStencilView lowDsv; int lowW, lowH;
    void EnsureLow(int w, int h)
    {
        if (lowTex != null && lowW == w && lowH == h) return;
        lowRtv?.Dispose(); lowSrv?.Dispose(); lowTex?.Dispose(); lowDsv?.Dispose(); lowDepth?.Dispose();
        lowW = w; lowH = h;
        lowTex = Device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
        lowRtv = Device.CreateRenderTargetView(lowTex); lowSrv = Device.CreateShaderResourceView(lowTex);
        lowDepth = Device.CreateTexture2D(new Texture2DDescription(Format.D24_UNorm_S8_UInt, (uint)w, (uint)h, 1, 1, BindFlags.DepthStencil));
        lowDsv = Device.CreateDepthStencilView(lowDepth);
    }

    const int VSTRIDE = 11 * 4, ISTRIDE = 20 * 4;

    [StructLayout(LayoutKind.Sequential)]
    struct FrameData
    {
        public Matrix4x4 ViewProj, View, ShadowVP;
        public Vector4 CamPos, FogColor, FogParams, HemiSky, HemiGround, SunDir, SunColor, Screen, Proj;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ObjectData
    {
        public Matrix4x4 World;
        public Vector4 Color, Emissive, Specular, Flags, Flags2, UvXform, U0, U1, U2, U3;
        public fixed float UArr[32];
    }

    public Renderer(Window w)
    {
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        var flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        if (Environment.GetEnvironmentVariable("GERMBALL_D3D_DEBUG") == "1") flags |= DeviceCreationFlags.Debug;
#endif
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels, out ID3D11Device dev, out ID3D11DeviceContext ctx).CheckError();
        Device = dev; Ctx = ctx;
        factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        using (var f5 = factory.QueryInterfaceOrNull<IDXGIFactory5>())
        {
            if (f5 != null) tearing = f5.PresentAllowTearing;
        }
        Width = w.Width; Height = w.Height;
        var sd = new SwapChainDescription1
        {
            Width = (uint)Width, Height = (uint)Height, Format = Format.R8G8B8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipDiscard, Scaling = Scaling.Stretch, AlphaMode = AlphaMode.Ignore,
            Flags = tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None,
        };
        swap = factory.CreateSwapChainForHwnd(Device, w.Handle, sd);
        factory.MakeWindowAssociation(w.Handle, WindowAssociationFlags.IgnoreAltEnter);
        CreateTargets();
        CreateStates();
        CreateShaders();
        CreateBuffers();
        white = new Texture(1, 1); white.Pixels[0] = white.Pixels[1] = white.Pixels[2] = white.Pixels[3] = 255;
        Log.Info($"Direct3D 11 device ready ({Device.FeatureLevel}), tearing {(tearing ? "available" : "unavailable")}");
    }

    void CreateTargets()
    {
        using (var bb = swap.GetBuffer<ID3D11Texture2D>(0)) rtv = Device.CreateRenderTargetView(bb);
        depthTex = Device.CreateTexture2D(new Texture2DDescription(Format.D24_UNorm_S8_UInt, (uint)Width, (uint)Height, 1, 1, BindFlags.DepthStencil));
        dsv = Device.CreateDepthStencilView(depthTex);
    }

    public void Resize(int w, int h)
    {
        if (w <= 0 || h <= 0 || (w == Width && h == Height)) return;
        Width = w; Height = h;
        Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null, null);
        rtv?.Dispose(); dsv?.Dispose(); depthTex?.Dispose();
        swap.ResizeBuffers(2, (uint)w, (uint)h, Format.R8G8B8A8_UNorm, tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None).CheckError();
        CreateTargets();
    }

    void EnsureShadowMap(int size)
    {
        if (size == shadowSize && shadowTex != null) return;
        shadowSrv?.Dispose(); shadowDsv?.Dispose(); shadowTex?.Dispose();
        shadowSize = size;
        shadowTex = Device.CreateTexture2D(new Texture2DDescription(Format.R32_Typeless, (uint)size, (uint)size, 1, 1, BindFlags.DepthStencil | BindFlags.ShaderResource));
        shadowDsv = Device.CreateDepthStencilView(shadowTex, new DepthStencilViewDescription(DepthStencilViewDimension.Texture2D, Format.D32_Float));
        shadowSrv = Device.CreateShaderResourceView(shadowTex, new ShaderResourceViewDescription(ShaderResourceViewDimension.Texture2D, Format.R32_Float, 0, 1));
    }

    void CreateStates()
    {
        SamplerDescription S(Filter f, TextureAddressMode m) => new(f, m, m, m, 0, 8, ComparisonFunction.Never, new Color4(0, 0, 0, 0), 0, float.MaxValue);
        sampNearestWrap = Device.CreateSamplerState(S(Filter.MinLinearMagPointMipLinear, TextureAddressMode.Wrap));
        sampNearestClamp = Device.CreateSamplerState(S(Filter.MinLinearMagPointMipLinear, TextureAddressMode.Clamp));
        sampLinearWrap = Device.CreateSamplerState(S(Filter.Anisotropic, TextureAddressMode.Wrap));
        sampLinearClamp = Device.CreateSamplerState(S(Filter.MinMagMipLinear, TextureAddressMode.Clamp));
        sampShadow = Device.CreateSamplerState(new SamplerDescription(Filter.ComparisonMinMagLinearMipPoint, TextureAddressMode.Border, TextureAddressMode.Border, TextureAddressMode.Border, 0, 1, ComparisonFunction.LessEqual, new Color4(1, 1, 1, 1), 0, float.MaxValue));
        RasterizerDescription R(CullMode c, int bias = 0, float slope = 0, bool scissor = false) => new(c, FillMode.Solid) { FrontCounterClockwise = true /* three.js winding (CCW front, right-handed) */, DepthClipEnable = true, DepthBias = bias, SlopeScaledDepthBias = slope, ScissorEnable = scissor };
        rsBack = Device.CreateRasterizerState(R(CullMode.Back)); rsFront = Device.CreateRasterizerState(R(CullMode.Front)); rsNone = Device.CreateRasterizerState(R(CullMode.None));
        rsBackOffset = Device.CreateRasterizerState(R(CullMode.Back, -40, -2)); rsNoneOffset = Device.CreateRasterizerState(R(CullMode.None, -40, -2));
        rsShadow = Device.CreateRasterizerState(R(CullMode.None, 600, 1.5f));
        rsScissor = Device.CreateRasterizerState(R(CullMode.None, 0, 0, true));
        bsOpaque = Device.CreateBlendState(BlendDescription.Opaque);
        bsAlpha = Device.CreateBlendState(BlendDescription.NonPremultiplied);
        bsAdd = Device.CreateBlendState(BlendDescription.Additive);
        dsOn = Device.CreateDepthStencilState(DepthStencilDescription.Default);
        dsReadOnly = Device.CreateDepthStencilState(DepthStencilDescription.DepthRead);
        dsOff = Device.CreateDepthStencilState(DepthStencilDescription.None);
    }

    static ReadOnlyMemory<byte> Compile(string src, string entry, string profile, string defines)
    {
        var full = defines + "\n" + src;
        try { return Compiler.Compile(full, entry, entry + ".hlsl", profile, ShaderFlags.OptimizationLevel3); }
        catch (Exception e) { Log.Error($"Shader {entry} ({defines.Replace('\n', ' ')}) failed: {e.Message}"); throw; }
    }

    void CreateShaders()
    {
        string main = Shaders.Common + Shaders.Main;
        var vsBlob = Compile(main, "VS", "vs_5_0", "");
        var elems = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32_Float, 32, 0),
            new InputElementDescription("INST", 0, Format.R32G32B32A32_Float, 0, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST", 1, Format.R32G32B32A32_Float, 16, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST", 2, Format.R32G32B32A32_Float, 32, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST", 3, Format.R32G32B32A32_Float, 48, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST", 4, Format.R32G32B32A32_Float, 64, 1, InputClassification.PerInstanceData, 1),
        };
        layout = Device.CreateInputLayout(elems, vsBlob.Span);
        void Prog(string name, string src, string vsE, string psE, string defs)
        {
            var v = Compile(src, vsE, "vs_5_0", defs); var p = Compile(src, psE, "ps_5_0", defs);
            programs[name] = (Device.CreateVertexShader(v.Span), Device.CreatePixelShader(p.Span));
        }
        programs[""] = (Device.CreateVertexShader(vsBlob.Span), Device.CreatePixelShader(Compile(main, "PS", "ps_5_0", "").Span));
        Prog("points", main, "VS", "PS", "#define POINTS 1");
        Prog("villi", main, "VS", "PS", "#define VILLI 1");
        Prog("sky", Shaders.Common + Shaders.Sky, "VSSky", "PSSky", "");
        Prog("roof", Shaders.Common + Shaders.Roof, "VSRoof", "PSRoof", "");
        shadowVs = Device.CreateVertexShader(Compile(main, "VSShadow", "vs_5_0", "").Span);
        shadowPs = Device.CreatePixelShader(Compile(main, "PSShadow", "ps_5_0", "").Span);
        var uiv = Compile(Shaders.UI, "VSUI", "vs_5_0", "");
        uiVs = Device.CreateVertexShader(uiv.Span); uiPs = Device.CreatePixelShader(Compile(Shaders.UI, "PSUI", "ps_5_0", "").Span);
        uiLayout = Device.CreateInputLayout(new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 16, 0),
        }, uiv.Span);
    }

    void CreateBuffers()
    {
        frameCb = Device.CreateBuffer(new BufferDescription((uint)((sizeof(FrameData) + 15) & ~15), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        objectCb = Device.CreateBuffer(new BufferDescription((uint)((sizeof(ObjectData) + 15) & ~15), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        uiCb = Device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        var id = new float[20]; id[0] = id[5] = id[10] = id[15] = 1; id[16] = id[17] = id[18] = id[19] = 1;
        identityInst = Device.CreateBuffer(id, BindFlags.VertexBuffer);
        // a unit quad for points: corners at ±0.5
        var q = new float[4 * 11]; float[][] c = { new[] { -0.5f, -0.5f }, new[] { 0.5f, -0.5f }, new[] { 0.5f, 0.5f }, new[] { -0.5f, 0.5f } };
        for (int i = 0; i < 4; i++) { q[i * 11] = c[i][0]; q[i * 11 + 1] = c[i][1]; q[i * 11 + 4] = 1; q[i * 11 + 8] = q[i * 11 + 9] = q[i * 11 + 10] = 1; }
        quadVb = Device.CreateBuffer(q, BindFlags.VertexBuffer);
        quadIb = Device.CreateBuffer(new uint[] { 0, 1, 2, 0, 2, 3 }, BindFlags.IndexBuffer);
    }

    // ---------------------------------------------------------------- GPU resources
    sealed class GpuGeo : IDisposable
    {
        public ID3D11Buffer Vb, Ib; public int Verts, Indices;
        public void Dispose() { Vb?.Dispose(); Ib?.Dispose(); }
    }
    sealed class GpuInst : IDisposable { public ID3D11Buffer Buf; public int Cap; public void Dispose() => Buf?.Dispose(); }
    sealed class GpuTex : IDisposable { public ID3D11Texture2D Tex; public ID3D11ShaderResourceView Srv; public int W, H; public void Dispose() { Srv?.Dispose(); Tex?.Dispose(); } }

    GpuGeo Upload(Geometry g)
    {
        if (g.Gpu is GpuGeo old && g.GpuVersion == g.Version) return old;
        int n = g.VertexCount;
        var data = new float[Math.Max(1, n) * 11];
        for (int i = 0; i < n; i++)
        {
            int o = i * 11;
            data[o] = g.Position[i * 3]; data[o + 1] = g.Position[i * 3 + 1]; data[o + 2] = g.Position[i * 3 + 2];
            if (g.Normal != null) { data[o + 3] = g.Normal[i * 3]; data[o + 4] = g.Normal[i * 3 + 1]; data[o + 5] = g.Normal[i * 3 + 2]; } else data[o + 4] = 1;
            if (g.Uv != null) { data[o + 6] = g.Uv[i * 2]; data[o + 7] = g.Uv[i * 2 + 1]; }
            if (g.Color != null) { data[o + 8] = g.Color[i * 3]; data[o + 9] = g.Color[i * 3 + 1]; data[o + 10] = g.Color[i * 3 + 2]; } else data[o + 8] = data[o + 9] = data[o + 10] = 1;
        }
        var gg = g.Gpu as GpuGeo;
        if (gg != null && gg.Verts == n && (g.Index?.Length ?? 0) == gg.Indices)
        {
            Ctx.UpdateSubresource(data, gg.Vb);
            if (g.Index != null && g.Index.Length > 0) Ctx.UpdateSubresource(g.Index, gg.Ib);
        }
        else
        {
            gg?.Dispose();
            gg = new GpuGeo { Verts = n, Indices = g.Index?.Length ?? 0 };
            gg.Vb = Device.CreateBuffer(data, BindFlags.VertexBuffer);
            if (g.Index != null && g.Index.Length > 0) gg.Ib = Device.CreateBuffer(g.Index, BindFlags.IndexBuffer);
        }
        g.Gpu = gg; g.GpuVersion = g.Version;
        if (g.BoundRadius < 0) g.ComputeBounds();
        return gg;
    }

    ID3D11Buffer UploadInstances(InstancedMesh m)
    {
        var gi = m.Gpu as GpuInst;
        if (gi != null && m.GpuVersion == m.Version && gi.Cap >= m.Count) return gi.Buf;
        int cap = Math.Max(1, m.Capacity);
        if (gi == null || gi.Cap < cap)
        {
            gi?.Dispose();
            gi = new GpuInst { Cap = cap, Buf = Device.CreateBuffer(new BufferDescription((uint)(cap * ISTRIDE), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write)) };
            m.Gpu = gi;
        }
        var map = Ctx.Map(gi.Buf, 0, MapMode.WriteDiscard);
        float* p = (float*)map.DataPointer;
        int n = Math.Min(m.Count, m.Capacity);
        for (int i = 0; i < n; i++)
        {
            ref var mm = ref m.Matrices[i]; float* d = p + i * 20;
            d[0] = mm.M11; d[1] = mm.M12; d[2] = mm.M13; d[3] = mm.M14; d[4] = mm.M21; d[5] = mm.M22; d[6] = mm.M23; d[7] = mm.M24;
            d[8] = mm.M31; d[9] = mm.M32; d[10] = mm.M33; d[11] = mm.M34; d[12] = mm.M41; d[13] = mm.M42; d[14] = mm.M43; d[15] = mm.M44;
            if (m.Colors != null) { var c = m.Colors[i]; d[16] = c.X; d[17] = c.Y; d[18] = c.Z; } else { d[16] = d[17] = d[18] = 1; }
            d[19] = 1;
        }
        Ctx.Unmap(gi.Buf, 0);
        m.GpuVersion = m.Version;
        return gi.Buf;
    }

    // points: one instance per vertex (translation in row 3, colour from vertex colours)
    readonly Dictionary<Geometry, (ID3D11Buffer buf, int ver, int n)> pointBufs = new();
    ID3D11Buffer UploadPoints(Geometry g, out int n)
    {
        n = g.VertexCount;
        if (pointBufs.TryGetValue(g, out var e) && e.ver == g.Version && e.n == n) return e.buf;
        e.buf?.Dispose();
        var data = new float[Math.Max(1, n) * 20];
        for (int i = 0; i < n; i++)
        {
            float* _ = null; int o = i * 20;
            data[o] = 1; data[o + 5] = 1; data[o + 10] = 1; data[o + 12] = g.Position[i * 3]; data[o + 13] = g.Position[i * 3 + 1]; data[o + 14] = g.Position[i * 3 + 2]; data[o + 15] = 1;
            if (g.Color != null) { data[o + 16] = g.Color[i * 3]; data[o + 17] = g.Color[i * 3 + 1]; data[o + 18] = g.Color[i * 3 + 2]; } else data[o + 16] = data[o + 17] = data[o + 18] = 1;
            data[o + 19] = 1;
        }
        var buf = Device.CreateBuffer(data, BindFlags.VertexBuffer);
        pointBufs[g] = (buf, g.Version, n);
        return buf;
    }

    GpuTex UploadTex(Texture t)
    {
        if (t.Gpu is GpuTex old && t.GpuVersion == t.Version) return old;
        var gt = t.Gpu as GpuTex;
        if (gt == null || gt.W != t.Width || gt.H != t.Height)
        {
            gt?.Dispose();
            gt = new GpuTex { W = t.Width, H = t.Height };
            uint mips = t.Mipmaps ? 0u : 1u;
            gt.Tex = Device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, (uint)t.Width, (uint)t.Height, 1, mips, BindFlags.ShaderResource | (t.Mipmaps ? BindFlags.RenderTarget : 0), ResourceUsage.Default, CpuAccessFlags.None, 1, 0, t.Mipmaps ? ResourceOptionFlags.GenerateMips : ResourceOptionFlags.None));
            gt.Srv = Device.CreateShaderResourceView(gt.Tex);
        }
        fixed (byte* p = t.Pixels) Ctx.UpdateSubresource(gt.Tex, 0, null, (IntPtr)p, (uint)(t.Width * 4), 0);
        if (t.Mipmaps) Ctx.GenerateMips(gt.Srv);
        t.Gpu = gt; t.GpuVersion = t.Version;
        return gt;
    }

    // ---------------------------------------------------------------- frame
    struct Item { public Node Node; public Geometry Geo; public Material Mat; public Matrix4x4 World; public float Depth; public int Kind; }   // kind 0 mesh, 1 instanced, 2 points, 3 lines
    readonly List<Item> opaque = new(), transp = new(), casters = new();
    readonly Plane[] frustum = new Plane[6];

    static void Extract(Matrix4x4 m, Plane[] p)
    {
        p[0] = Plane.Normalize(new Plane(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41));
        p[1] = Plane.Normalize(new Plane(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41));
        p[2] = Plane.Normalize(new Plane(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42));
        p[3] = Plane.Normalize(new Plane(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42));
        p[4] = Plane.Normalize(new Plane(m.M13, m.M23, m.M33, m.M43));
        p[5] = Plane.Normalize(new Plane(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43));
    }
    bool InFrustum(Vector3 c, float r)
    {
        foreach (var pl in frustum) if (Plane.DotCoordinate(pl, c) < -r) return false;
        return true;
    }

    void Collect(Node n, Vector3 camPos, Matrix4x4 view)
    {
        if (!n.Visible) return;
        switch (n)
        {
            case InstancedMesh im when im.Geo != null && im.Mat != null && im.Count > 0:
                Add(new Item { Node = im, Geo = im.Geo, Mat = im.Mat, World = im.World, Kind = 1 }, im.CastShadow, view); break;
            case InstancedMesh: break;   // empty (or incomplete) instanced meshes draw nothing; without this they fell through to the plain-mesh case and drew their unit-size geometry at the origin
            case Mesh m when m.Geo != null && m.Mat != null:
                {
                    if (m.Geo.BoundRadius < 0) m.Geo.ComputeBounds();
                    bool vis = true;
                    if (m.FrustumCulled)
                    {
                        var c = Vector3.Transform(m.Geo.BoundCenter, m.World);
                        float s = MathF.Max(new Vector3(m.World.M11, m.World.M12, m.World.M13).Length(), MathF.Max(new Vector3(m.World.M21, m.World.M22, m.World.M23).Length(), new Vector3(m.World.M31, m.World.M32, m.World.M33).Length()));
                        vis = InFrustum(c, m.Geo.BoundRadius * s);
                    }
                    var it = new Item { Node = m, Geo = m.Geo, Mat = m.Mat, World = m.World, Kind = 0 };
                    if (vis) Add(it, false, view);
                    if (m.CastShadow) casters.Add(it);
                    break;
                }
            case Points p when p.Geo != null: Add(new Item { Node = p, Geo = p.Geo, Mat = p.Mat, World = p.World, Kind = 2 }, false, view); break;
            case Lines l when l.Geo != null: Add(new Item { Node = l, Geo = l.Geo, Mat = l.Mat, World = l.World, Kind = 3 }, false, view); break;
        }
        foreach (var c in n.Children) Collect(c, camPos, view);
    }
    void Add(Item it, bool cast, Matrix4x4 view)
    {
        it.Depth = -Vector3.Transform(new Vector3(it.World.M41, it.World.M42, it.World.M43), view).Z;
        if (it.Mat.Transparent || it.Mat.Blending == Blend.Additive) transp.Add(it); else opaque.Add(it);
        if (cast) casters.Add(it);
    }

    /// <summary>Clears the back buffer (screens with no 3D scene).</summary>
    public void Clear(System.Numerics.Vector3 c)
    {
        Ctx.OMSetRenderTargets(rtv, dsv);
        Ctx.ClearRenderTargetView(rtv, new Color4(c.X, c.Y, c.Z, 1));
        Ctx.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1, 0);
    }

    public void Render(Scene scene, Camera cam)
    {
        if (scene == null || cam == null) { Clear(new System.Numerics.Vector3(0x12 / 255f, 0x06 / 255f, 0x1f / 255f)); return; }
        DrawCalls = 0; Triangles = 0;
        cam.Aspect = (float)Width / Height;
        bool low = RenderScale < 0.999f;
        int rw = low ? Math.Max(1, (int)MathF.Round(Width * RenderScale)) : Width, rh = low ? Math.Max(1, (int)MathF.Round(Height * RenderScale)) : Height;
        if (low) EnsureLow(rw, rh);
        scene.UpdateWorld(Matrix4x4.Identity);
        var view = cam.View; var proj = cam.Proj; var vp = view * proj;
        Extract(vp, frustum);
        opaque.Clear(); transp.Clear(); casters.Clear();
        Collect(scene, cam.Position, view);

        // ---- shadow map: an orthographic box round the focus point, snapped to texels so edges don't crawl
        Matrix4x4 svp = Matrix4x4.Identity; bool shadows = scene.Shadows && casters.Count > 0;
        if (shadows)
        {
            EnsureShadowMap(scene.ShadowMapSize);
            var sd = Vector3.Normalize(scene.SunDir); float half = scene.ShadowHalf;
            var lv = Matrix4x4.CreateLookAt(scene.ShadowCenter + sd * (scene.ShadowFar * 0.5f), scene.ShadowCenter, MathF.Abs(sd.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY);
            float texel = half * 2 / shadowSize;
            var cs = Vector3.Transform(scene.ShadowCenter, lv);
            lv.M41 -= cs.X - MathF.Floor(cs.X / texel) * texel; lv.M42 -= cs.Y - MathF.Floor(cs.Y / texel) * texel;
            var lp = Matrix4x4.CreateOrthographicOffCenter(-half, half, -half, half, scene.ShadowNear, scene.ShadowFar);
            svp = lv * lp;
        }
        var fd = new FrameData
        {
            ViewProj = vp, View = view, ShadowVP = svp,
            CamPos = new Vector4(cam.Position, scene.Time),
            FogColor = new Vector4(scene.FogColor, scene.FogNear),
            FogParams = new Vector4(scene.FogFar, -0.0015f, scene.ShadowNormalBias, shadowSize > 0 ? 1f / shadowSize : 0),
            HemiSky = new Vector4(scene.HemiSky * scene.HemiIntensity, 0), HemiGround = new Vector4(scene.HemiGround * scene.HemiIntensity, 0),
            SunDir = new Vector4(Vector3.Normalize(scene.SunDir), shadows ? 1 : 0), SunColor = new Vector4(scene.SunColor * scene.SunIntensity, 0),
            Screen = new Vector4(rw, rh, 1f / rw, 1f / rh), Proj = new Vector4(MathF.Tan(cam.Fov * MathF.PI / 360), cam.Aspect, 0, 0),
        };
        var map = Ctx.Map(frameCb, 0, MapMode.WriteDiscard); *(FrameData*)map.DataPointer = fd; Ctx.Unmap(frameCb, 0);

        Ctx.IASetInputLayout(layout);
        Ctx.VSSetConstantBuffers(0, new[] { frameCb, objectCb }); Ctx.PSSetConstantBuffers(0, new[] { frameCb, objectCb });
        Ctx.PSSetSamplers(0, new[] { sampNearestWrap, sampShadow });

        if (shadows)
        {
            Ctx.PSSetShaderResources(1, new ID3D11ShaderResourceView[] { null });
            Ctx.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>(), shadowDsv);
            Ctx.ClearDepthStencilView(shadowDsv, DepthStencilClearFlags.Depth, 1, 0);
            Ctx.RSSetViewport(new Viewport(0, 0, shadowSize, shadowSize));
            Ctx.RSSetState(rsShadow); Ctx.OMSetDepthStencilState(dsOn); Ctx.OMSetBlendState(bsOpaque);
            Ctx.VSSetShader(shadowVs); Ctx.PSSetShader(shadowPs);
            foreach (var it in casters) if (it.Kind <= 1) Draw(it, true, proj);
        }

        var tRtv = low ? lowRtv : rtv; var tDsv = low ? lowDsv : dsv;
        Ctx.OMSetRenderTargets(tRtv, tDsv);
        Ctx.ClearRenderTargetView(tRtv, new Color4(scene.Background.X, scene.Background.Y, scene.Background.Z, 1));
        Ctx.ClearDepthStencilView(tDsv, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1, 0);
        Ctx.RSSetViewport(new Viewport(0, 0, rw, rh));
        if (shadows) Ctx.PSSetShaderResources(1, new[] { shadowSrv });

        opaque.Sort((a, b) => a.Node.RenderOrder != b.Node.RenderOrder ? a.Node.RenderOrder.CompareTo(b.Node.RenderOrder) : a.Depth.CompareTo(b.Depth));
        transp.Sort((a, b) => a.Node.RenderOrder != b.Node.RenderOrder ? a.Node.RenderOrder.CompareTo(b.Node.RenderOrder) : b.Depth.CompareTo(a.Depth));
        foreach (var it in opaque) Draw(it, false, proj);
        foreach (var it in transp) Draw(it, false, proj);
        Ctx.PSSetShaderResources(1, new ID3D11ShaderResourceView[] { null });
        if (low) Upscale();
    }

    /// <summary>Stretches the low-resolution scene over the back buffer with nearest filtering.</summary>
    void Upscale()
    {
        Span<UIVert> q = stackalloc UIVert[6];
        UIVert V(float x, float y, float u, float v) => new() { X = x, Y = y, U = u, V = v, R = 1, G = 1, B = 1, A = 1 };
        q[0] = V(0, 0, 0, 0); q[1] = V(Width, 0, 1, 0); q[2] = V(Width, Height, 1, 1); q[3] = V(0, 0, 0, 0); q[4] = V(Width, Height, 1, 1); q[5] = V(0, Height, 0, 1);
        if (uiVb == null || uiCap < 6) { uiVb?.Dispose(); uiCap = Math.Max(4096, uiCap); uiVb = Device.CreateBuffer(new BufferDescription((uint)(uiCap * sizeof(UIVert)), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write)); }
        var map = Ctx.Map(uiVb, 0, MapMode.WriteDiscard);
        fixed (UIVert* src = q) Buffer.MemoryCopy(src, (void*)map.DataPointer, uiCap * sizeof(UIVert), 6 * sizeof(UIVert));
        Ctx.Unmap(uiVb, 0);
        var m2 = Ctx.Map(uiCb, 0, MapMode.WriteDiscard); *(Vector4*)m2.DataPointer = new Vector4(Width, Height, 1f / Width, 1f / Height); Ctx.Unmap(uiCb, 0);
        Ctx.OMSetRenderTargets(rtv, dsv);
        Ctx.RSSetViewport(new Viewport(0, 0, Width, Height));
        Ctx.IASetInputLayout(uiLayout); Ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        Ctx.IASetVertexBuffers(0, new[] { uiVb }, new uint[] { (uint)sizeof(UIVert) }, new uint[] { 0 });
        Ctx.VSSetShader(uiVs); Ctx.PSSetShader(uiPs); Ctx.VSSetConstantBuffers(0, new[] { uiCb });
        Ctx.OMSetBlendState(bsOpaque); Ctx.OMSetDepthStencilState(dsOff); Ctx.RSSetState(rsScissor);
        Ctx.RSSetScissorRect(0, 0, Width, Height);
        Ctx.PSSetShaderResources(0, new[] { lowSrv }); Ctx.PSSetSamplers(0, new[] { sampNearestClamp });
        Ctx.Draw(6, 0);
        Ctx.PSSetShaderResources(0, new ID3D11ShaderResourceView[] { null });
    }

    void Draw(in Item it, bool shadow, Matrix4x4 proj)
    {
        var m = it.Mat; var g = it.Geo;
        if (g.VertexCount == 0) return;
        var gg = Upload(g);
        var od = new ObjectData
        {
            World = it.World,
            Color = new Vector4(m.Color, m.Opacity),
            Emissive = new Vector4(m.Emissive, m.AlphaTest),
            Specular = new Vector4(m.Specular, m.Shininess),
            Flags = new Vector4(m.Kind == MatKind.Lambert ? 1 : m.Kind == MatKind.Phong ? 2 : 0, m.VertexColors ? 1 : 0, m.Map != null ? 1 : 0, it.Node is Mesh me && me.ReceiveShadow ? 1 : 0),
            Flags2 = new Vector4(m.Fog ? 1 : 0, m.Toon, m.Size, m.SizeAttenuation ? 1 : 0),
            UvXform = new Vector4(m.MapRepeat, m.MapOffset.X, m.MapOffset.Y),
            U0 = m.U0, U1 = m.U1, U2 = m.U2, U3 = m.U3,
        };
        if (m.UArr != null) for (int i = 0; i < Math.Min(8, m.UArr.Length); i++) { od.UArr[i * 4] = m.UArr[i].X; od.UArr[i * 4 + 1] = m.UArr[i].Y; od.UArr[i * 4 + 2] = m.UArr[i].Z; od.UArr[i * 4 + 3] = m.UArr[i].W; }
        if (it.Kind == 2)
        {
            od.UArr[16] = proj.M11; od.UArr[17] = proj.M12; od.UArr[18] = proj.M13; od.UArr[19] = proj.M14; od.UArr[20] = proj.M21; od.UArr[21] = proj.M22; od.UArr[22] = proj.M23; od.UArr[23] = proj.M24;
            od.UArr[24] = proj.M31; od.UArr[25] = proj.M32; od.UArr[26] = proj.M33; od.UArr[27] = proj.M34; od.UArr[28] = proj.M41; od.UArr[29] = proj.M42; od.UArr[30] = proj.M43; od.UArr[31] = proj.M44;
            od.Flags.Y = 1;
        }
        var map = Ctx.Map(objectCb, 0, MapMode.WriteDiscard); *(ObjectData*)map.DataPointer = od; Ctx.Unmap(objectCb, 0);

        var tex = UploadTex(m.Map ?? white);
        Ctx.PSSetShaderResources(0, new[] { tex.Srv });
        if (m.Map != null) Ctx.PSSetSamplers(0, new[] { m.Map.Nearest ? (m.Map.Repeat ? sampNearestWrap : sampNearestClamp) : (m.Map.Repeat ? sampLinearWrap : sampLinearClamp) });

        if (!shadow)
        {
            string prog = it.Kind == 2 ? "points" : m.Shader ?? "";
            if (!programs.TryGetValue(prog, out var p)) p = programs[""];
            Ctx.VSSetShader(p.vs); Ctx.PSSetShader(p.ps);
            bool off = m.PolygonOffset;
            Ctx.RSSetState(m.Side == Side.Double || it.Kind >= 2 ? (off ? rsNoneOffset : rsNone) : m.Side == Side.Back ? rsFront : (off ? rsBackOffset : rsBack));
            Ctx.OMSetBlendState(m.Blending == Blend.Additive ? bsAdd : m.Transparent ? bsAlpha : bsOpaque);
            Ctx.OMSetDepthStencilState(!m.DepthTest ? dsOff : m.DepthWrite ? dsOn : dsReadOnly);
        }

        Ctx.IASetPrimitiveTopology(it.Kind == 3 ? ((Lines)it.Node).Strip ? PrimitiveTopology.LineStrip : PrimitiveTopology.LineList : PrimitiveTopology.TriangleList);
        if (it.Kind == 2)
        {
            var pb = UploadPoints(g, out int n);
            Ctx.IASetVertexBuffers(0, new[] { quadVb, pb }, new uint[] { VSTRIDE, ISTRIDE }, new uint[] { 0, 0 });
            Ctx.IASetIndexBuffer(quadIb, Format.R32_UInt, 0);
            Ctx.DrawIndexedInstanced(6, (uint)n, 0, 0, 0); DrawCalls++; Triangles += 2 * n;
            return;
        }
        int instances = 1; ID3D11Buffer ib = identityInst;
        if (it.Kind == 1) { var im = (InstancedMesh)it.Node; ib = UploadInstances(im); instances = Math.Min(im.Count, im.Capacity); }
        Ctx.IASetVertexBuffers(0, new[] { gg.Vb, ib }, new uint[] { VSTRIDE, ISTRIDE }, new uint[] { 0, 0 });
        if (gg.Ib != null)
        {
            Ctx.IASetIndexBuffer(gg.Ib, Format.R32_UInt, 0);
            int count = g.DrawCount < 0 ? gg.Indices - g.DrawStart : Math.Min(g.DrawCount, gg.Indices - g.DrawStart);
            if (count > 0) { Ctx.DrawIndexedInstanced((uint)count, (uint)instances, (uint)g.DrawStart, 0, 0); Triangles += count / 3 * instances; }
        }
        else
        {
            int count = g.DrawCount < 0 ? gg.Verts - g.DrawStart : Math.Min(g.DrawCount, gg.Verts - g.DrawStart);
            if (count > 0) { Ctx.DrawInstanced((uint)count, (uint)instances, (uint)g.DrawStart, 0); Triangles += count / 3 * instances; }
        }
        DrawCalls++;
    }

    // ---------------------------------------------------------------- 2D
    [StructLayout(LayoutKind.Sequential)] public struct UIVert { public float X, Y, U, V, R, G, B, A; }
    int uiCap;
    /// <summary>Draws batched 2D triangles (pixel coordinates, top-left origin) with one texture per batch.</summary>
    public void DrawUI(List<(Texture tex, int start, int count, Rect? clip)> batches, UIVert[] verts, int nverts)
    {
        if (nverts == 0) return;
        if (uiVb == null || uiCap < nverts)
        {
            uiVb?.Dispose(); uiCap = Math.Max(nverts, Math.Max(4096, uiCap * 2));
            uiVb = Device.CreateBuffer(new BufferDescription((uint)(uiCap * sizeof(UIVert)), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }
        var map = Ctx.Map(uiVb, 0, MapMode.WriteDiscard);
        fixed (UIVert* src = verts) Buffer.MemoryCopy(src, (void*)map.DataPointer, uiCap * sizeof(UIVert), nverts * sizeof(UIVert));
        Ctx.Unmap(uiVb, 0);
        var m2 = Ctx.Map(uiCb, 0, MapMode.WriteDiscard); *(Vector4*)m2.DataPointer = new Vector4(Width, Height, 1f / Width, 1f / Height); Ctx.Unmap(uiCb, 0);
        Ctx.OMSetRenderTargets(rtv, null);
        Ctx.RSSetViewport(new Viewport(0, 0, Width, Height));
        Ctx.IASetInputLayout(uiLayout); Ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        Ctx.IASetVertexBuffers(0, new[] { uiVb }, new uint[] { (uint)sizeof(UIVert) }, new uint[] { 0 });
        Ctx.VSSetShader(uiVs); Ctx.PSSetShader(uiPs);
        Ctx.VSSetConstantBuffers(0, new[] { uiCb });
        Ctx.OMSetBlendState(bsAlpha); Ctx.OMSetDepthStencilState(dsOff); Ctx.RSSetState(rsScissor);
        foreach (var (tex, start, count, clip) in batches)
        {
            if (count <= 0) continue;
            var t = UploadTex(tex ?? white);
            Ctx.PSSetShaderResources(0, new[] { t.Srv });
            Ctx.PSSetSamplers(0, new[] { tex != null && tex.Nearest ? sampNearestClamp : sampLinearClamp });
            var c = clip ?? new Rect(0, 0, Width, Height);
            Ctx.RSSetScissorRect((int)c.X, (int)c.Y, (int)(c.X + c.Width), (int)(c.Y + c.Height));
            Ctx.Draw((uint)count, (uint)start); DrawCalls++;
        }
    }

    public void Present()
    {
        swap.Present(VSync ? 1u : 0u, !VSync && tearing ? PresentFlags.AllowTearing : PresentFlags.None);
    }

    /// <summary>Reads the back buffer (for screenshots). Call after drawing, before Present.</summary>
    public byte[] Capture(out int w, out int h)
    {
        w = Width; h = Height;
        using var bb = swap.GetBuffer<ID3D11Texture2D>(0);
        using var st = Device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        Ctx.CopyResource(st, bb);
        var map = Ctx.Map(st, 0, MapMode.Read);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++) Marshal.Copy(map.DataPointer + (nint)(y * map.RowPitch), px, y * w * 4, w * 4);
        Ctx.Unmap(st, 0);
        return px;
    }

    public void Release(Geometry g) { (g?.Gpu as IDisposable)?.Dispose(); if (g != null) g.Gpu = null; }
    public void Release(Texture t) { (t?.Gpu as IDisposable)?.Dispose(); if (t != null) t.Gpu = null; }

    public void Dispose()
    {
        foreach (var p in programs.Values) { p.vs.Dispose(); p.ps.Dispose(); }
        foreach (var e in pointBufs.Values) e.buf?.Dispose();
        rtv?.Dispose(); dsv?.Dispose(); depthTex?.Dispose(); lowRtv?.Dispose(); lowSrv?.Dispose(); lowTex?.Dispose(); lowDsv?.Dispose(); lowDepth?.Dispose(); shadowSrv?.Dispose(); shadowDsv?.Dispose(); shadowTex?.Dispose();
        swap?.Dispose(); factory?.Dispose(); Ctx?.Dispose(); Device?.Dispose();
    }
}

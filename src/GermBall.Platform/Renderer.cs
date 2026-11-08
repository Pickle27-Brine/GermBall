using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using GermBall.Engine;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace GermBall.Win;

/// <summary>
/// A small Direct3D 11 renderer for <see cref="DrawList"/>: one lit shader (hemisphere + sun, world grid, fog), instanced by shape,
/// an alpha pass sorted back to front, and a 2D pass of coloured rectangles for the sky gradient and the HUD.
/// </summary>
public sealed unsafe class Renderer : IDisposable
{
    readonly ID3D11Device dev; readonly ID3D11DeviceContext ctx;
    readonly IDXGIFactory2 factory; readonly IDXGISwapChain1 swap; readonly bool tearing;
    ID3D11RenderTargetView rtv; ID3D11Texture2D depthTex; ID3D11DepthStencilView dsv;
    readonly ID3D11VertexShader vs, vs2d; readonly ID3D11PixelShader ps, ps2d;
    readonly ID3D11InputLayout layout, layout2d;
    readonly ID3D11Buffer frameCb, screenCb;
    ID3D11Buffer instBuf, buf2d; int instCap, cap2d;
    readonly ID3D11RasterizerState rs; readonly ID3D11BlendState bsOpaque, bsAlpha;
    readonly ID3D11DepthStencilState dsOn, dsRead, dsOff;
    readonly (ID3D11Buffer vb, ID3D11Buffer ib, int count)[] meshes = new (ID3D11Buffer, ID3D11Buffer, int)[(int)MeshId.Count];

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool VSync = true;

    [StructLayout(LayoutKind.Sequential)]
    struct FrameData { public Matrix4x4 ViewProj; public Vector4 CamPos, SunDir, Sky, Ground, Fog, FogParams; }
    [StructLayout(LayoutKind.Sequential)]
    struct Instance { public Vector4 R0, R1, R2, R3, Color, Params; }
    [StructLayout(LayoutKind.Sequential)]
    struct Vert2D { public Vector2 Pos; public Vector4 Color; }

    public Renderer(Window w)
    {
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out ID3D11Device d, out ID3D11DeviceContext c).CheckError();
        dev = d; ctx = c;
        factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        using (var f5 = factory.QueryInterfaceOrNull<IDXGIFactory5>()) if (f5 != null) tearing = f5.PresentAllowTearing;
        Width = w.Width; Height = w.Height;
        swap = factory.CreateSwapChainForHwnd(dev, w.Handle, new SwapChainDescription1
        {
            Width = (uint)Width, Height = (uint)Height, Format = Format.R8G8B8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipDiscard, Scaling = Scaling.Stretch, AlphaMode = AlphaMode.Ignore,
            Flags = tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None,
        });
        factory.MakeWindowAssociation(w.Handle, WindowAssociationFlags.IgnoreAltEnter);
        CreateTargets();

        var vsb = Compile(Shaders.Main, "VS", "vs_5_0");
        vs = dev.CreateVertexShader(vsb.Span); ps = dev.CreatePixelShader(Compile(Shaders.Main, "PS", "ps_5_0").Span);
        layout = dev.CreateInputLayout(new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32_Float, 24, 0),
            new InputElementDescription("INST", 0, Format.R32G32B32A32_Float, 0, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST", 1, Format.R32G32B32A32_Float, 16, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST", 2, Format.R32G32B32A32_Float, 32, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST", 3, Format.R32G32B32A32_Float, 48, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST", 4, Format.R32G32B32A32_Float, 64, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST", 5, Format.R32G32B32A32_Float, 80, 1, InputClassification.PerInstanceData, 1),
        }, vsb.Span);
        var vs2b = Compile(Shaders.Flat2D, "VS2D", "vs_5_0");
        vs2d = dev.CreateVertexShader(vs2b.Span); ps2d = dev.CreatePixelShader(Compile(Shaders.Flat2D, "PS2D", "ps_5_0").Span);
        layout2d = dev.CreateInputLayout(new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 8, 0),
        }, vs2b.Span);

        frameCb = dev.CreateBuffer(new BufferDescription((uint)((sizeof(FrameData) + 15) & ~15), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        screenCb = dev.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

        rs = dev.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthClipEnable = true });
        bsOpaque = dev.CreateBlendState(BlendDescription.Opaque);
        bsAlpha = dev.CreateBlendState(BlendDescription.NonPremultiplied);
        dsOn = dev.CreateDepthStencilState(DepthStencilDescription.Default);
        dsRead = dev.CreateDepthStencilState(DepthStencilDescription.DepthRead);
        dsOff = dev.CreateDepthStencilState(DepthStencilDescription.None);

        for (int i = 0; i < meshes.Length; i++)
        {
            var m = Mesh.Get((MeshId)i);
            var data = new float[m.Pos.Count * 9];
            for (int k = 0; k < m.Pos.Count; k++)
            {
                data[k * 9] = m.Pos[k].X; data[k * 9 + 1] = m.Pos[k].Y; data[k * 9 + 2] = m.Pos[k].Z;
                data[k * 9 + 3] = m.Nrm[k].X; data[k * 9 + 4] = m.Nrm[k].Y; data[k * 9 + 5] = m.Nrm[k].Z;
                data[k * 9 + 6] = m.Col[k].X; data[k * 9 + 7] = m.Col[k].Y; data[k * 9 + 8] = m.Col[k].Z;
            }
            meshes[i] = (dev.CreateBuffer(data, BindFlags.VertexBuffer), dev.CreateBuffer(m.Idx.ToArray(), BindFlags.IndexBuffer), m.Idx.Count);
        }
    }

    static ReadOnlyMemory<byte> Compile(string src, string entry, string profile) =>
        Compiler.Compile(src, entry, entry + ".hlsl", profile, ShaderFlags.OptimizationLevel3);

    void CreateTargets()
    {
        using (var bb = swap.GetBuffer<ID3D11Texture2D>(0)) rtv = dev.CreateRenderTargetView(bb);
        depthTex = dev.CreateTexture2D(new Texture2DDescription(Format.D24_UNorm_S8_UInt, (uint)Width, (uint)Height, 1, 1, BindFlags.DepthStencil));
        dsv = dev.CreateDepthStencilView(depthTex);
    }

    public void Resize(int w, int h)
    {
        if (w <= 0 || h <= 0 || (w == Width && h == Height)) return;
        Width = w; Height = h;
        ctx.OMSetRenderTargets((ID3D11RenderTargetView)null, null);
        rtv.Dispose(); dsv.Dispose(); depthTex.Dispose();
        swap.ResizeBuffers(2, (uint)w, (uint)h, Format.R8G8B8A8_UNorm, tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None).CheckError();
        CreateTargets();
    }

    // ---------------------------------------------------------------- frame
    readonly List<DrawItem> opaque = new(), onTop = new();
    readonly List<(DrawItem it, float d)> transp = new();
    readonly List<Vert2D> verts2d = new();

    public void Render(DrawList dl)
    {
        ctx.OMSetRenderTargets(rtv, dsv);
        ctx.ClearRenderTargetView(rtv, new Color4(dl.FogColor.X, dl.FogColor.Y, dl.FogColor.Z, 1));
        ctx.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1, 0);
        ctx.RSSetViewport(new Viewport(0, 0, Width, Height));
        ctx.RSSetState(rs);
        var vp = dl.ViewportOr(Width, Height);

        // sky: a vertical gradient behind everything
        verts2d.Clear();
        var top = new Vector4(dl.SkyColor * 0.85f, 1); var hor = new Vector4(dl.FogColor, 1);
        AddQuad(vp.X, vp.Y, vp.Z, vp.W, top, top, hor, hor);
        Draw2D();
        ctx.RSSetViewport(new Viewport(vp.X, vp.Y, vp.Z, vp.W));

        // 3D
        var fd = new FrameData
        {
            ViewProj = dl.View * dl.Proj, CamPos = new Vector4(dl.CamPos, 1), SunDir = new Vector4(dl.SunDir, 0),
            Sky = new Vector4(dl.SkyColor, 1), Ground = new Vector4(dl.GroundColor, 1), Fog = new Vector4(dl.FogColor, 1),
            FogParams = new Vector4(dl.FogStart, dl.FogEnd, 0, 0),
        };
        var map = ctx.Map(frameCb, 0, MapMode.WriteDiscard); *(FrameData*)map.DataPointer = fd; ctx.Unmap(frameCb, 0);
        ctx.IASetInputLayout(layout); ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.VSSetShader(vs); ctx.PSSetShader(ps);
        ctx.VSSetConstantBuffers(0, new[] { frameCb }); ctx.PSSetConstantBuffers(0, new[] { frameCb });

        opaque.Clear(); transp.Clear(); onTop.Clear();
        foreach (var it in dl.Items)
        {
            if ((it.Flags & DrawFlags.OnTop) != 0) onTop.Add(it);
            else if (it.Color.W >= 0.999f) opaque.Add(it);
            else transp.Add((it, Vector3.DistanceSquared(it.World.Translation, dl.CamPos)));
        }
        opaque.Sort((a, b) => a.Mesh.CompareTo(b.Mesh));
        transp.Sort((a, b) => b.d.CompareTo(a.d));

        ctx.OMSetBlendState(bsOpaque); ctx.OMSetDepthStencilState(dsOn);
        DrawRuns(opaque.Count, i => opaque[i]);
        ctx.OMSetBlendState(bsAlpha); ctx.OMSetDepthStencilState(dsRead);
        DrawRuns(transp.Count, i => transp[i].it);
        ctx.OMSetDepthStencilState(dsOff);
        DrawRuns(onTop.Count, i => onTop[i]);
        ctx.RSSetViewport(new Viewport(0, 0, Width, Height));

        // HUD
        verts2d.Clear();
        foreach (var q in dl.Overlay.Quads) AddQuad(q.X, q.Y, q.W, q.H, q.Color, q.Color, q.Color, q.Color);
        Draw2D();
    }

    /// <summary>Uploads the items as instances and draws each run of the same shape with one call (order is kept, for blending).</summary>
    void DrawRuns(int n, Func<int, DrawItem> get)
    {
        if (n == 0) return;
        if (instBuf == null || instCap < n)
        {
            instBuf?.Dispose(); instCap = Math.Max(n, Math.Max(1024, instCap * 2));
            instBuf = dev.CreateBuffer(new BufferDescription((uint)(instCap * sizeof(Instance)), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }
        var map = ctx.Map(instBuf, 0, MapMode.WriteDiscard);
        var dst = (Instance*)map.DataPointer;
        for (int i = 0; i < n; i++)
        {
            var it = get(i); var m = it.World;
            dst[i] = new Instance
            {
                R0 = new Vector4(m.M11, m.M12, m.M13, m.M14), R1 = new Vector4(m.M21, m.M22, m.M23, m.M24),
                R2 = new Vector4(m.M31, m.M32, m.M33, m.M34), R3 = new Vector4(m.M41, m.M42, m.M43, m.M44),
                Color = it.Color,
                Params = new Vector4((it.Flags & DrawFlags.Grid) != 0 ? 1 : 0, (it.Flags & DrawFlags.Unlit) != 0 ? 1 : 0, 0, 0),
            };
        }
        ctx.Unmap(instBuf, 0);
        int start = 0;
        while (start < n)
        {
            var mesh = get(start).Mesh; int end = start + 1;
            while (end < n && get(end).Mesh == mesh) end++;
            var (vb, ib, count) = meshes[(int)mesh];
            ctx.IASetVertexBuffers(0, new[] { vb, instBuf }, new uint[] { 36, (uint)sizeof(Instance) }, new uint[] { 0, 0 });
            ctx.IASetIndexBuffer(ib, Format.R32_UInt, 0);
            ctx.DrawIndexedInstanced((uint)count, (uint)(end - start), 0, 0, (uint)start);
            start = end;
        }
    }

    void AddQuad(float x, float y, float w, float h, Vector4 tl, Vector4 tr, Vector4 bl, Vector4 br)
    {
        verts2d.Add(new Vert2D { Pos = new Vector2(x, y), Color = tl });
        verts2d.Add(new Vert2D { Pos = new Vector2(x + w, y), Color = tr });
        verts2d.Add(new Vert2D { Pos = new Vector2(x, y + h), Color = bl });
        verts2d.Add(new Vert2D { Pos = new Vector2(x + w, y), Color = tr });
        verts2d.Add(new Vert2D { Pos = new Vector2(x + w, y + h), Color = br });
        verts2d.Add(new Vert2D { Pos = new Vector2(x, y + h), Color = bl });
    }

    void Draw2D()
    {
        int n = verts2d.Count; if (n == 0) return;
        if (buf2d == null || cap2d < n)
        {
            buf2d?.Dispose(); cap2d = Math.Max(n, Math.Max(6 * 4096, cap2d * 2));
            buf2d = dev.CreateBuffer(new BufferDescription((uint)(cap2d * sizeof(Vert2D)), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }
        var map = ctx.Map(buf2d, 0, MapMode.WriteDiscard);
        var dst = (Vert2D*)map.DataPointer;
        for (int i = 0; i < n; i++) dst[i] = verts2d[i];
        ctx.Unmap(buf2d, 0);
        var sm = ctx.Map(screenCb, 0, MapMode.WriteDiscard); *(Vector4*)sm.DataPointer = new Vector4(Width, Height, 0, 0); ctx.Unmap(screenCb, 0);
        ctx.IASetInputLayout(layout2d); ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.IASetVertexBuffers(0, new[] { buf2d }, new uint[] { (uint)sizeof(Vert2D) }, new uint[] { 0 });
        ctx.VSSetShader(vs2d); ctx.PSSetShader(ps2d); ctx.VSSetConstantBuffers(0, new[] { screenCb });
        ctx.OMSetBlendState(bsAlpha); ctx.OMSetDepthStencilState(dsOff);
        ctx.Draw((uint)n, 0);
    }

    public void Present() => swap.Present(VSync ? 1u : 0u, !VSync && tearing ? PresentFlags.AllowTearing : PresentFlags.None);

    public void Dispose()
    {
        foreach (var (vb, ib, _) in meshes) { vb?.Dispose(); ib?.Dispose(); }
        instBuf?.Dispose(); buf2d?.Dispose(); frameCb.Dispose(); screenCb.Dispose();
        rs.Dispose(); bsOpaque.Dispose(); bsAlpha.Dispose(); dsOn.Dispose(); dsRead.Dispose(); dsOff.Dispose();
        layout.Dispose(); layout2d.Dispose(); vs.Dispose(); ps.Dispose(); vs2d.Dispose(); ps2d.Dispose();
        rtv.Dispose(); dsv.Dispose(); depthTex.Dispose(); swap.Dispose(); factory.Dispose(); ctx.Dispose(); dev.Dispose();
    }
}

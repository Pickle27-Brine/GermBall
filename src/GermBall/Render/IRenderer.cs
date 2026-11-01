using System.Collections.Generic;
using Vortice.Mathematics;

namespace GermBall.Render;

/// <summary>What the screens need from a renderer: the Direct3D one in the game, a software one in tools.</summary>
public interface IRenderer
{
    int Width { get; }
    int Height { get; }
    float RenderScale { get; set; }
    bool VSync { get; set; }
    void Render(Scene scene, Camera cam);
    void DrawUI(List<(Texture tex, int start, int count, Rect? clip)> batches, Renderer.UIVert[] verts, int nverts);
    void Resize(int w, int h);
    void Present();
    byte[] Capture(out int w, out int h);
}

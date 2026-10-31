using System;
using GermBall.Core;
using GermBall.Render;

namespace GermBall.Game;

/// <summary>Floating spores drifting over the map: one point cloud of pixel sprites.</summary>
public sealed partial class World
{
    const int SPORES = 260;
    readonly float[] sporeData = new float[SPORES * 4];
    Points sporeMesh; Geometry sporeGeo;
    void InitSpores()
    {
        var r = Rng(31);
        for (int i = 0; i < SPORES; i++) { sporeData[i * 4] = (r() * 2 - 1) * 330; sporeData[i * 4 + 1] = 8 + r() * 60; sporeData[i * 4 + 2] = (r() * 2 - 1) * 330; sporeData[i * 4 + 3] = r() * 6.28f; }
        sporeGeo = new Geometry { Position = new float[SPORES * 3] };
        var m = new Material { Kind = MatKind.Points, Map = SPR_TEX.TryGetValue("spore", out var t) ? t : null, Size = 1.1f, SizeAttenuation = true, Transparent = true, AlphaTest = 0.5f, DepthWrite = false };
        sporeMesh = scene.Add(new Points(sporeGeo, m));
    }
    void UpdateSpores(float t)
    {
        if (sporeMesh == null) return;
        var p = sporeGeo.Position;
        for (int i = 0; i < SPORES; i++)
        {
            float x = sporeData[i * 4], y = sporeData[i * 4 + 1], z = sporeData[i * 4 + 2], ph = sporeData[i * 4 + 3];
            p[i * 3] = x + MathF.Sin(t * 0.1f + ph) * 6; p[i * 3 + 1] = y + MathF.Sin(t * 0.4f + ph * 2) * 2; p[i * 3 + 2] = z + MathF.Cos(t * 0.13f + ph) * 6;
        }
        sporeGeo.Version++; sporeGeo.BoundRadius = 500;
    }
}

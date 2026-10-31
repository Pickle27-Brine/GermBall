using System;
using System.Collections.Generic;
using GermBall.Render;

namespace GermBall.Game;

/// <summary>The face builder's drawing: shapes on a transparent square (x, y in 0..1, w size, h stretch, r degrees).</summary>
public static class FaceArt
{
    public static void Draw(Canvas2D g, int size, List<FaceShape> face)
    {
        g.Clear();
        foreach (var f in face ?? new())
        {
            g.Save(); g.ResetTransform(); g.Translate(f.X * size, f.Y * size); g.Rotate(f.R * MathF.PI / 180);
            float w = (f.W == 0 ? 0.15f : f.W) * size, hk = f.H == 0 ? 1 : f.H, h = w * hk;
            g.FillStyle = g.StrokeStyle = string.IsNullOrEmpty(f.C) ? "#111" : f.C; g.LineWidth = MathF.Max(2, w * 0.18f); g.RoundCap = true; g.BeginPath();
            switch (f.S)
            {
                case "circle": g.Arc(0, 0, w / 2, 0, MathF.PI * 2); g.Fill(); break;
                case "oval": g.Ellipse(0, 0, w / 2, h / 2 * 1.5f, 0, 0, MathF.PI * 2); g.Fill(); break;
                case "square": g.FillRect(-w / 2, -h / 2, w, h); break;
                case "triangle": g.MoveTo(0, -h / 2); g.LineTo(w / 2, h / 2); g.LineTo(-w / 2, h / 2); g.ClosePath(); g.Fill(); break;
                case "star": for (int i = 0; i < 10; i++) { float a = i / 10f * MathF.PI * 2 - MathF.PI / 2, rr = i % 2 != 0 ? w * 0.22f : w / 2; g.LineTo(MathF.Cos(a) * rr, MathF.Sin(a) * rr * hk); } g.ClosePath(); g.Fill(); break;
                case "heart": g.MoveTo(0, h * 0.35f); g.BezierCurveTo(-w * 0.6f, -h * 0.1f, -w * 0.3f, -h * 0.55f, 0, -h * 0.2f); g.BezierCurveTo(w * 0.3f, -h * 0.55f, w * 0.6f, -h * 0.1f, 0, h * 0.35f); g.Fill(); break;
                case "crescent": g.Arc(0, 0, w / 2, 0.1f * MathF.PI, 0.9f * MathF.PI); g.Stroke(); break;
                case "line": g.MoveTo(-w / 2, 0); g.LineTo(w / 2, 0); g.Stroke(); break;
                case "zigzag": g.MoveTo(-w / 2, 0); for (int i = 1; i <= 6; i++) g.LineTo(-w / 2 + i * w / 6, i % 2 != 0 ? -h * 0.2f : h * 0.2f); g.Stroke(); break;
                case "ring": g.Arc(0, 0, w / 2, 0, MathF.PI * 2); g.Stroke(); break;
                case "cross": g.MoveTo(-w / 2, -h / 2); g.LineTo(w / 2, h / 2); g.MoveTo(w / 2, -h / 2); g.LineTo(-w / 2, h / 2); g.Stroke(); break;
                case "drop": g.MoveTo(0, -h / 2); g.QuadraticCurveTo(w / 2, h * 0.1f, 0, h / 2); g.QuadraticCurveTo(-w / 2, h * 0.1f, 0, -h / 2); g.Fill(); break;
            }
            g.Restore();
        }
    }
}

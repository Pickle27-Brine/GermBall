using System;
using System.Numerics;
using GermBall.Engine;

namespace GermBall.Tools.Ui;

/// <summary>
/// The editors' 3D view: an orbit or fly camera inside a rectangle of the window, a ground grid, lines, picking rays,
/// and a move gizmo (three axis arrows plus a square for dragging on the ground plane).
/// </summary>
public sealed class View3D
{
    public Vector4 Rect;
    public bool Fly;
    // orbit
    public Vector3 Target = new(0, 1, 0); public float Yaw = 0.6f, Pitch = -0.25f, Dist = 4.5f;
    // fly
    public Vector3 Eye = new(0, 12, 30); public float FlySpeed = 12;
    public float Fov = 55 * MathX.Deg;
    public Vector3 CamPos { get; private set; }
    public Vector3 Forward { get; private set; }
    DrawList dl; int winW, winH;

    public void Setup(DrawList d, int w, int h)
    {
        dl = d; winW = w; winH = h;
        Forward = MathX.Forward(Yaw, Pitch);
        CamPos = Fly ? Eye : Target - Forward * Dist;
        d.Viewport = Rect;
        d.CamPos = CamPos;
        d.View = Matrix4x4.CreateLookAt(CamPos, CamPos + Forward, Vector3.UnitY);
        d.Proj = Matrix4x4.CreatePerspectiveFieldOfView(Fov, Rect.Z / MathF.Max(1, Rect.W), 0.03f, 900);
        d.FogStart = Fly ? 150 : 400; d.FogEnd = Fly ? 600 : 900;
    }

    public bool MouseIn(UiInput i) => i.MouseX >= Rect.X && i.MouseY >= Rect.Y && i.MouseX < Rect.X + Rect.Z && i.MouseY < Rect.Y + Rect.W;

    /// <summary>Camera controls. Orbit: right-drag turns, middle-drag (or shift + right-drag) pans, wheel zooms.
    /// Fly: hold right mouse to look and use WASD (Q/E down/up, shift fast); the wheel changes speed.</summary>
    public void Controls(UiInput i, bool uiOwnsMouse, bool keyboardFree, float dt)
    {
        bool inside = MouseIn(i) && !uiOwnsMouse;
        if (inside && (i.Pressed[1] || i.Pressed[2])) looking = true;
        if (!i.Down[1] && !i.Down[2]) looking = false;
        bool pan = looking && (i.Down[2] || (i.Down[1] && i.Shift));
        if (looking && !pan)
        {
            Yaw -= i.MouseDX * 0.006f; Pitch = Math.Clamp(Pitch - i.MouseDY * 0.006f, -1.5f, 1.5f);
        }
        if (Fly)
        {
            if (inside && i.Wheel != 0) FlySpeed = Math.Clamp(FlySpeed * MathF.Pow(1.2f, i.Wheel), 1, 200);
            if (looking && keyboardFree)
            {
                var f = MathX.Forward(Yaw, Pitch); var r = MathX.Right(Yaw);
                var mv = Vector3.Zero;
                if (i.Held('W')) mv += f; if (i.Held('S')) mv -= f;
                if (i.Held('D')) mv += r; if (i.Held('A')) mv -= r;
                if (i.Held('E')) mv += Vector3.UnitY; if (i.Held('Q')) mv -= Vector3.UnitY;
                Eye += mv * FlySpeed * (i.Shift ? 3 : 1) * dt;
            }
            if (pan) Eye += (-MathX.Right(Yaw) * i.MouseDX + Vector3.UnitY * i.MouseDY) * 0.002f * FlySpeed;
        }
        else
        {
            if (inside && i.Wheel != 0) Dist = Math.Clamp(Dist * MathF.Pow(0.88f, i.Wheel), 0.3f, 300);
            if (pan)
            {
                var up = Vector3.Cross(MathX.Right(Yaw), MathX.Forward(Yaw, Pitch));
                Target += (-MathX.Right(Yaw) * i.MouseDX + up * i.MouseDY) * Dist * 0.0018f;
            }
        }
    }
    bool looking;
    public bool Looking => looking;

    public void Frame(Vector3 center, float radius)
    {
        if (Fly) { Eye = center - MathX.Forward(Yaw, Pitch) * MathF.Max(4, radius * 3); }
        else { Target = center; Dist = MathF.Max(0.5f, radius * 2.8f); }
    }

    public (Vector3 o, Vector3 d) Ray(UiInput i) => dl.ScreenRay(i.MouseX, i.MouseY, winW, winH);
    public bool Project(Vector3 p, out Vector2 s) => dl.Project(p, winW, winH, out s, out _);

    // ---------------------------------------------------------------- drawing helpers
    public static void Line(DrawList d, Vector3 a, Vector3 b, Vector4 c, float width = 0.01f, bool onTop = false) =>
        d.Rod(a, b, width, c, DrawFlags.Unlit | (onTop ? DrawFlags.OnTop : 0));

    public void Grid(DrawList d, float half, float step, float y = 0)
    {
        float w = Fly ? 0.03f : 0.004f;
        for (float x = -half; x <= half + 1e-3f; x += step)
        {
            bool axis = MathF.Abs(x) < 1e-3f;
            var c = axis ? new Vector4(0.3f, 0.3f, 0.35f, 0.9f) : new Vector4(0.35f, 0.38f, 0.42f, 0.35f);
            Line(d, new Vector3(x, y, -half), new Vector3(x, y, half), axis ? new Vector4(0.36f, 0.55f, 0.92f, 0.8f) : c, w);
            Line(d, new Vector3(-half, y, x), new Vector3(half, y, x), axis ? new Vector4(0.85f, 0.32f, 0.32f, 0.8f) : c, w);
        }
    }

    /// <summary>A wire box (twelve edges) on top of everything.</summary>
    public static void WireBox(DrawList d, Matrix4x4 m, Vector4 c, float w = 0.008f)
    {
        Vector3 P(float x, float y, float z) => Vector3.Transform(new Vector3(x, y, z) * 0.5f, m);
        int[][] e = { new[] { 0, 1 }, new[] { 1, 3 }, new[] { 3, 2 }, new[] { 2, 0 }, new[] { 4, 5 }, new[] { 5, 7 }, new[] { 7, 6 }, new[] { 6, 4 }, new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 } };
        var v = new Vector3[8];
        for (int i = 0; i < 8; i++) v[i] = P((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1);
        foreach (var k in e) Line(d, v[k[0]], v[k[1]], c, w, true);
    }

    /// <summary>Ray against a unit cube transformed by <paramref name="m"/> (shapes and boxes are picked this way).</summary>
    public static bool RayBox(Vector3 o, Vector3 dir, Matrix4x4 m, out float t)
    {
        t = 0;
        if (!Matrix4x4.Invert(m, out var inv)) return false;
        var lo = Vector3.Transform(o, inv); var ld = Vector3.TransformNormal(dir, inv);
        float tmin = 0, tmax = float.MaxValue;
        for (int a = 0; a < 3; a++)
        {
            float oo = a == 0 ? lo.X : a == 1 ? lo.Y : lo.Z, dd = a == 0 ? ld.X : a == 1 ? ld.Y : ld.Z;
            if (MathF.Abs(dd) < 1e-9f) { if (oo < -0.5f || oo > 0.5f) return false; continue; }
            float t1 = (-0.5f - oo) / dd, t2 = (0.5f - oo) / dd; if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = MathF.Max(tmin, t1); tmax = MathF.Min(tmax, t2); if (tmin > tmax) return false;
        }
        // back to a world distance
        var hit = Vector3.Transform(lo + ld * tmin, m);
        t = Vector3.Distance(o, hit);
        return true;
    }

    // ---------------------------------------------------------------- the move gizmo
    int dragAxis = -1; Vector3 dragPrev; float gizLen;
    public bool GizmoActive => dragAxis >= 0;

    /// <summary>Draws the move gizmo at <paramref name="pos"/> and returns how far it was dragged this frame (world units).
    /// <paramref name="axes"/> lets it follow a rotated frame (bones, shapes). Axis 3 is the ground-plane square.</summary>
    public Vector3 Gizmo(DrawList d, UiInput i, Vector3 pos, bool uiOwnsMouse, Matrix4x4? axes = null, bool plane = true)
    {
        gizLen = Vector3.Distance(CamPos, pos) * 0.16f;
        var ax = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
        if (axes is Matrix4x4 m) for (int k = 0; k < 3; k++) { var v = Vector3.TransformNormal(ax[k], m); if (v.LengthSquared() > 1e-8f) ax[k] = Vector3.Normalize(v); }
        var cols = new[] { new Vector4(0.95f, 0.3f, 0.3f, 1), new Vector4(0.35f, 0.9f, 0.35f, 1), new Vector4(0.35f, 0.55f, 1f, 1) };
        // which handle is under the mouse
        int hover = -1; float best = 9;
        var mouse = new Vector2(i.MouseX, i.MouseY);
        if (!uiOwnsMouse && MouseIn(i) && dragAxis < 0 && Project(pos, out var s0))
        {
            for (int k = 0; k < 3; k++)
            {
                if (!Project(pos + ax[k] * gizLen, out var s1)) continue;
                float dd = SegDist(mouse, s0, s1);
                if (dd < best) { best = dd; hover = k; }
            }
            if (plane && Project(pos + (ax[0] + ax[2]) * gizLen * 0.3f, out var sq) && Vector2.Distance(mouse, sq) < 10) hover = 3;
        }
        if (hover >= 0 && i.Pressed[0]) { dragAxis = hover; dragPrev = PlanePoint(i, pos); }
        if (!i.Down[0]) dragAxis = -1;
        int shown = dragAxis >= 0 ? dragAxis : hover;
        for (int k = 0; k < 3; k++)
        {
            var c = k == shown ? new Vector4(1, 0.9f, 0.3f, 1) : cols[k];
            Line(d, pos, pos + ax[k] * gizLen, c, gizLen * 0.03f, true);
            d.Rod(pos + ax[k] * gizLen, pos + ax[k] * gizLen * 1.25f, gizLen * 0.08f, c, DrawFlags.Unlit | DrawFlags.OnTop, MeshId.Cone);
        }
        if (plane)
        {
            var q = pos + (ax[0] + ax[2]) * gizLen * 0.3f;
            d.Box(q, new Vector3(gizLen * 0.18f, gizLen * 0.01f, gizLen * 0.18f), shown == 3 ? new Vector4(1, 0.9f, 0.3f, 0.9f) : new Vector4(0.9f, 0.9f, 0.9f, 0.6f), DrawFlags.Unlit | DrawFlags.OnTop);
        }
        if (dragAxis < 0) return Vector3.Zero;
        if (dragAxis == 3)
        {
            var p = PlanePoint(i, pos); var delta = p - dragPrev; dragPrev = p; delta.Y = 0;
            return delta;
        }
        // project the mouse movement onto the axis as drawn on screen
        if (!Project(pos, out var a0) || !Project(pos + ax[dragAxis] * gizLen, out var a1)) return Vector3.Zero;
        var sd = a1 - a0; float pix = sd.Length(); if (pix < 1) return Vector3.Zero;
        float along = Vector2.Dot(new Vector2(i.MouseDX, i.MouseDY), sd / pix) / pix * gizLen;
        return ax[dragAxis] * along;
    }

    Vector3 PlanePoint(UiInput i, Vector3 through)
    {
        var (o, dd) = Ray(i);
        if (MathF.Abs(dd.Y) < 1e-4f) return through;
        float t = (through.Y - o.Y) / dd.Y; if (t < 0) return through;
        return o + dd * t;
    }

    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a; float l = ab.LengthSquared(); if (l < 1e-6f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / l, 0, 1);
        return Vector2.Distance(p, a + ab * t);
    }
}

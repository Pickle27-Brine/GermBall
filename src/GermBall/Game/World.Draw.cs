using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Core;
using GermBall.Render;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed partial class World
{
    public Vector3 camPos;
    // =====================================================================
    // Weapon models: each built from lumps, barrels, prisms, balls and spikes with its own colours and silhouette.
    // Hand at the origin, -Z forward. Grown parts: blocks are soft, swollen lumps of chitin, barrels bulge like a stalk.
    // =====================================================================
    static Dictionary<char, Geometry> WSHAPE;
    static void InitWShapes()
    {
        if (WSHAPE != null) return;
        var b = Geo.Box(1, 1, 1);   // a box rounded towards a ball
        var bs = Geo.Sphere(0.5f, 10, 8); // a smoother stand-in for the subdivided box
        var bx = Geo.Box(1, 1, 1);
        { var g = bx; for (int i = 0; i < g.VertexCount; i++) { float x = g.Position[i * 3], y = g.Position[i * 3 + 1], z = g.Position[i * 3 + 2], l = Hypot(x, y, z); if (l == 0) l = 1; const float k = 0.42f, r = 0.62f; g.Position[i * 3] = x * (1 - k) + x / l * r * k; g.Position[i * 3 + 1] = y * (1 - k) + y / l * r * k; g.Position[i * 3 + 2] = z * (1 - k) + z / l * r * k; } g.ComputeNormals(); }
        var c = Geo.Cylinder(0.5f, 0.5f, 1, 10, 4);
        for (int i = 0; i < c.VertexCount; i++) { float y = c.Position[i * 3 + 1], k = 1 + 0.12f * MathF.Cos(y * PI * 2); c.Position[i * 3] *= k; c.Position[i * 3 + 2] *= k; }
        c.ComputeNormals(); c.RotateX(PI / 2);
        var t = Geo.Cylinder(0.5f, 0.5f, 1, 3).RotateX(PI / 2);
        var o = Geo.Sphere(0.5f, 10, 8);
        var p = Geo.Cone(0.5f, 1, 4).RotateX(-PI / 2).RotateZ(PI / 4);
        WSHAPE = new() { ['b'] = bx, ['c'] = c, ['t'] = t, ['o'] = o, ['p'] = p };
    }
    // grown, not forged: chitin, bone, cartilage, membrane and bioluminescent glands
    static readonly Dictionary<char, (uint c, float sh)> WMAT_DEF = new()
    {
        ['d'] = (0x4a1e3a, 90), ['s'] = (0xf0e4d2, 120), ['w'] = (0xc8946a, 20), ['k'] = (0x2a1020, 60), ['y'] = (0xe0ff5a, 110), ['v'] = (0xb04a9a, 80), ['r'] = (0xff3a5a, 80), ['m'] = (0x4ac08a, 70),
        ['g'] = (0x7affd0, -1), ['p'] = (0xff5ce1, -1), ['o'] = (0xffb04a, -1), ['b'] = (0x6ad0ff, -1), ['n'] = (0xb4ff3a, -1),
        ['u'] = (0x2a1420, 6), ['c'] = (0xfff0e4, 150), ['q'] = (0x7a3a5e, 50), ['h'] = (0xff8a5a, 70), ['e'] = (0x9a4ac4, 70),
    };
    static Material Wmat(char k) { var (c, sh) = WMAT_DEF.TryGetValue(k, out var v) ? v : (0xffffffu, 30f); return sh < 0 ? BasicM(c) : Phong(c, sh, sh > 80 ? 0xffffffu : 0x333344u); }
    sealed class WPiece { public string key; public char shape, mat; public float x, y, z, sx, sy, sz; public Q4 q; }
    static readonly Dictionary<string, float[][]> WRAW_NUM = new();
    // [shape + material, x, y, z, sx, sy, sz, rotation x y z in degrees]
    static readonly Dictionary<string, (string k, float[] v)[]> WRAW = new()
    {
        ["pistol"] = new[] { ("bd", new[] { 0, 0.075f, -0.1f, 0.07f, 0.07f, 0.3f }), ("bk", new[] { 0, 0.03f, -0.07f, 0.066f, 0.05f, 0.25f }), ("bk", new[] { 0, -0.06f, 0.02f, 0.06f, 0.15f, 0.075f, 14 }), ("cs", new[] { 0, 0.075f, -0.26f, 0.042f, 0.042f, 0.04f }), ("og", new[] { 0, 0.12f, 0.02f, 0.025f, 0.025f, 0.025f }), ("bs", new[] { 0, 0.075f, -0.1f, 0.072f, 0.012f, 0.24f }) },
        ["revolver"] = new[] { ("cs", new[] { 0, 0.075f, -0.24f, 0.045f, 0.045f, 0.32f }), ("cd", new[] { 0, 0.05f, -0.05f, 0.11f, 0.11f, 0.1f }), ("bs", new[] { 0, 0.05f, -0.08f, 0.05f, 0.08f, 0.2f }), ("bw", new[] { 0, -0.06f, 0.05f, 0.05f, 0.15f, 0.075f, 25 }), ("py", new[] { 0, 0.11f, 0.04f, 0.03f, 0.05f, 0.05f, 50 }), ("py", new[] { 0, 0.11f, -0.38f, 0.02f, 0.03f, 0.03f, -90 }), ("by", new[] { 0, 0.105f, -0.24f, 0.012f, 0.012f, 0.3f }) },
        ["smg"] = new[] { ("bd", new[] { 0, 0.04f, -0.12f, 0.08f, 0.11f, 0.32f }), ("cs", new[] { 0, 0.05f, -0.34f, 0.035f, 0.035f, 0.14f }), ("bk", new[] { 0, -0.1f, -0.14f, 0.05f, 0.19f, 0.06f, -10 }), ("bk", new[] { 0, -0.07f, 0.02f, 0.05f, 0.13f, 0.06f, 12 }), ("bd", new[] { 0, 0.03f, 0.16f, 0.03f, 0.06f, 0.2f }), ("bo", new[] { 0.042f, 0.06f, -0.14f, 0.005f, 0.02f, 0.24f }), ("bo", new[] { -0.042f, 0.06f, -0.14f, 0.005f, 0.02f, 0.24f }), ("cs", new[] { 0, 0.12f, -0.12f, 0.03f, 0.03f, 0.08f }) },
        ["rifle"] = new[] { ("bd", new[] { 0, 0.05f, -0.08f, 0.08f, 0.12f, 0.42f }), ("cs", new[] { 0, 0.07f, -0.5f, 0.035f, 0.035f, 0.36f }), ("tv", new[] { 0, 0.06f, -0.36f, 0.1f, 0.1f, 0.26f }), ("bk", new[] { 0, -0.1f, -0.15f, 0.05f, 0.18f, 0.07f, -16 }), ("bd", new[] { 0, 0.02f, 0.24f, 0.06f, 0.13f, 0.26f }), ("bk", new[] { 0, -0.06f, 0.04f, 0.05f, 0.13f, 0.06f, 12 }),
            ("ck", new[] { 0, 0.16f, -0.08f, 0.05f, 0.05f, 0.2f }), ("cg", new[] { 0, 0.16f, -0.19f, 0.046f, 0.046f, 0.01f }), ("bs", new[] { 0, 0.115f, -0.08f, 0.03f, 0.015f, 0.1f }), ("pk", new[] { 0, 0.07f, -0.7f, 0.05f, 0.05f, 0.06f }) },
        ["hunting"] = new[] { ("bw", new[] { 0, 0.01f, 0.12f, 0.07f, 0.11f, 0.55f }), ("bw", new[] { 0, 0.04f, -0.2f, 0.06f, 0.06f, 0.3f }), ("cs", new[] { 0, 0.06f, -0.58f, 0.03f, 0.03f, 0.62f }), ("cd", new[] { 0, 0.14f, -0.12f, 0.06f, 0.06f, 0.34f }), ("co", new[] { 0, 0.14f, -0.295f, 0.055f, 0.055f, 0.01f }), ("os", new[] { 0.05f, 0.07f, 0.0f, 0.03f, 0.03f, 0.03f }), ("bd", new[] { 0, 0.1f, -0.12f, 0.03f, 0.04f, 0.06f }), ("by", new[] { 0, -0.02f, 0.38f, 0.072f, 0.12f, 0.02f }) },
        ["sniper"] = new[] { ("bv", new[] { 0, 0.05f, -0.08f, 0.09f, 0.12f, 0.4f }), ("ck", new[] { 0, 0.07f, -0.72f, 0.034f, 0.034f, 0.9f }), ("bd", new[] { 0, 0.07f, -1.18f, 0.07f, 0.06f, 0.1f }), ("ck", new[] { 0, 0.17f, -0.1f, 0.075f, 0.075f, 0.42f }), ("cr", new[] { 0, 0.17f, -0.315f, 0.07f, 0.07f, 0.01f }), ("ck", new[] { 0, 0.17f, 0.12f, 0.07f, 0.07f, 0.02f }),
            ("bd", new[] { 0, 0.06f, 0.25f, 0.03f, 0.12f, 0.25f }), ("bd", new[] { 0, -0.02f, 0.36f, 0.06f, 0.16f, 0.04f }), ("ts", new[] { 0.05f, -0.08f, -0.5f, 0.015f, 0.2f, 0.015f, 20, 0, 20 }), ("ts", new[] { -0.05f, -0.08f, -0.5f, 0.015f, 0.2f, 0.015f, 20, 0, -20 }), ("bk", new[] { 0, -0.08f, -0.12f, 0.05f, 0.12f, 0.06f }) },
        ["shotgun"] = new[] { ("cs", new[] { 0.032f, 0.07f, -0.34f, 0.055f, 0.055f, 0.56f }), ("cs", new[] { -0.032f, 0.07f, -0.34f, 0.055f, 0.055f, 0.56f }), ("bw", new[] { 0, 0.01f, -0.3f, 0.09f, 0.07f, 0.2f }), ("bd", new[] { 0, 0.04f, -0.02f, 0.1f, 0.11f, 0.18f }), ("bw", new[] { 0, -0.01f, 0.2f, 0.07f, 0.13f, 0.3f, -8 }), ("cy", new[] { 0, 0.04f, 0.0f, 0.11f, 0.11f, 0.03f }), ("bk", new[] { 0, -0.07f, 0.04f, 0.05f, 0.12f, 0.06f, 15 }) },
        ["rocket"] = new[] { ("cv", new[] { 0, 0.11f, -0.15f, 0.2f, 0.2f, 1.0f }), ("cd", new[] { 0, 0.11f, -0.66f, 0.26f, 0.26f, 0.12f }), ("ck", new[] { 0, 0.11f, 0.37f, 0.24f, 0.24f, 0.08f }), ("pr", new[] { 0, 0.11f, -0.66f, 0.12f, 0.12f, 0.14f }), ("oo", new[] { 0, 0.11f, 0.42f, 0.16f, 0.16f, 0.04f }),
            ("bk", new[] { 0, -0.04f, -0.05f, 0.05f, 0.14f, 0.06f, 10 }), ("bk", new[] { 0, -0.02f, -0.35f, 0.05f, 0.12f, 0.06f }), ("by", new[] { 0.12f, 0.2f, -0.25f, 0.03f, 0.08f, 0.05f }), ("by", new[] { 0, 0.11f, -0.15f, 0.205f, 0.205f, 0.04f }) },
        ["bow"] = new[] { ("tw", new[] { 0, 0.3f, 0.02f, 0.05f, 0.08f, 0.55f, 90 - 18, 0, 0 }), ("tw", new[] { 0, -0.3f, 0.02f, 0.05f, 0.08f, 0.55f, 90 + 18, 0, 0 }), ("bd", new[] { 0, 0, 0, 0.05f, 0.16f, 0.06f }), ("bs", new[] { 0, 0, 0.13f, 0.008f, 1.05f, 0.008f }), ("cw", new[] { 0, 0, -0.12f, 0.02f, 0.02f, 0.62f }), ("ps", new[] { 0, 0, -0.45f, 0.04f, 0.04f, 0.08f }), ("tr", new[] { 0, 0, 0.18f, 0.06f, 0.004f, 0.06f }), ("py", new[] { 0, 0.58f, 0.07f, 0.04f, 0.04f, 0.08f, 90 }), ("py", new[] { 0, -0.58f, 0.07f, 0.04f, 0.04f, 0.08f, -90 }) },
        ["knives"] = new[] { ("ts", new[] { 0, 0.02f, -0.16f, 0.02f, 0.07f, 0.26f }), ("ts", new[] { 0.07f, 0.0f, -0.13f, 0.02f, 0.07f, 0.24f, 0, -14, 0 }), ("ts", new[] { -0.07f, 0.0f, -0.13f, 0.02f, 0.07f, 0.24f, 0, 14, 0 }), ("bk", new[] { 0, 0.0f, 0.02f, 0.05f, 0.05f, 0.1f }), ("by", new[] { 0, 0, -0.03f, 0.18f, 0.02f, 0.02f }) },
        ["darts"] = new[] { ("bk", new[] { 0, 0.04f, -0.08f, 0.06f, 0.08f, 0.34f }), ("cr", new[] { 0, 0.05f, -0.32f, 0.016f, 0.016f, 0.2f }), ("cg", new[] { 0, 0.12f, -0.05f, 0.045f, 0.045f, 0.14f }), ("tr", new[] { 0, 0.1f, 0.06f, 0.008f, 0.08f, 0.1f }), ("bk", new[] { 0, -0.06f, 0.02f, 0.045f, 0.13f, 0.06f, 12 }), ("pr", new[] { 0, 0.05f, -0.44f, 0.02f, 0.02f, 0.05f }) },
        ["needler"] = new[] { ("ov", new[] { 0, 0.05f, -0.08f, 0.15f, 0.13f, 0.34f }), ("pp", new[] { 0, 0.15f, -0.2f, 0.035f, 0.035f, 0.14f, -60 }), ("pp", new[] { 0.035f, 0.15f, -0.12f, 0.035f, 0.035f, 0.15f, -55, 10 }), ("pp", new[] { -0.035f, 0.15f, -0.12f, 0.035f, 0.035f, 0.15f, -55, -10 }), ("pp", new[] { 0, 0.15f, -0.04f, 0.035f, 0.035f, 0.16f, -50 }), ("pp", new[] { 0.03f, 0.14f, 0.04f, 0.03f, 0.03f, 0.13f, -45, 10 }), ("pp", new[] { -0.03f, 0.14f, 0.04f, 0.03f, 0.03f, 0.13f, -45, -10 }),
            ("bd", new[] { 0, -0.06f, 0.03f, 0.05f, 0.13f, 0.07f, 10 }), ("td", new[] { 0, 0.03f, -0.28f, 0.08f, 0.06f, 0.08f }) },
        ["plasma"] = new[] { ("om", new[] { 0, 0.05f, -0.06f, 0.15f, 0.11f, 0.28f }), ("td", new[] { 0.05f, 0.05f, -0.24f, 0.03f, 0.05f, 0.16f, 0, 10 }), ("td", new[] { -0.05f, 0.05f, -0.24f, 0.03f, 0.05f, 0.16f, 0, -10 }), ("on", new[] { 0, 0.05f, -0.22f, 0.06f, 0.06f, 0.06f }), ("bd", new[] { 0, -0.05f, 0.04f, 0.05f, 0.12f, 0.07f, 15 }), ("bn", new[] { 0, 0.1f, -0.04f, 0.02f, 0.01f, 0.12f }) },
        ["laserpistol"] = new[] { ("bs", new[] { 0, 0.06f, -0.08f, 0.07f, 0.09f, 0.3f }), ("cg", new[] { 0, 0.06f, -0.25f, 0.06f, 0.06f, 0.02f }), ("cg", new[] { 0, 0.06f, -0.29f, 0.05f, 0.05f, 0.02f }), ("cd", new[] { 0, 0.06f, -0.27f, 0.035f, 0.035f, 0.08f }), ("tr", new[] { 0, 0.12f, -0.06f, 0.012f, 0.05f, 0.16f }), ("bd", new[] { 0, -0.05f, 0.02f, 0.05f, 0.13f, 0.07f, 14 }), ("bg", new[] { 0.037f, 0.06f, -0.08f, 0.004f, 0.015f, 0.22f }) },
        ["armcannon"] = new[] { ("cs", new[] { 0, 0.03f, -0.12f, 0.24f, 0.24f, 0.62f }), ("cg", new[] { 0, 0.03f, -0.44f, 0.27f, 0.27f, 0.04f }), ("cd", new[] { 0, 0.03f, -0.47f, 0.16f, 0.16f, 0.06f }), ("cd", new[] { 0, 0.03f, 0.2f, 0.22f, 0.22f, 0.06f }), ("bo", new[] { 0.125f, 0.03f, -0.1f, 0.01f, 0.05f, 0.3f }), ("bo", new[] { -0.125f, 0.03f, -0.1f, 0.01f, 0.05f, 0.3f }), ("py", new[] { 0, 0.16f, -0.05f, 0.05f, 0.05f, 0.12f, 60 }) },
        ["chargecannon"] = new[] { ("cd", new[] { 0, 0.08f, -0.2f, 0.26f, 0.26f, 0.85f }), ("cy", new[] { 0, 0.08f, -0.05f, 0.3f, 0.3f, 0.04f }), ("cy", new[] { 0, 0.08f, -0.2f, 0.3f, 0.3f, 0.04f }), ("cy", new[] { 0, 0.08f, -0.35f, 0.3f, 0.3f, 0.04f }), ("cb", new[] { 0, 0.08f, -0.62f, 0.12f, 0.12f, 0.06f }),
            ("pb", new[] { 0.1f, 0.08f, -0.7f, 0.04f, 0.04f, 0.16f }), ("pb", new[] { -0.1f, 0.08f, -0.7f, 0.04f, 0.04f, 0.16f }), ("pb", new[] { 0, 0.18f, -0.7f, 0.04f, 0.04f, 0.16f }), ("bk", new[] { 0, -0.08f, -0.05f, 0.05f, 0.14f, 0.06f, 10 }), ("bk", new[] { 0, 0.08f, 0.26f, 0.2f, 0.2f, 0.06f }) },
        ["bat"] = new[] { ("ck", new[] { 0, 0, 0.02f, 0.04f, 0.04f, 0.26f }), ("cw", new[] { 0, 0, -0.3f, 0.055f, 0.055f, 0.4f }), ("cw", new[] { 0, 0, -0.6f, 0.08f, 0.08f, 0.28f }), ("ow", new[] { 0, 0, -0.74f, 0.08f, 0.08f, 0.04f }), ("ck", new[] { 0, 0, 0.16f, 0.06f, 0.06f, 0.02f }), ("br", new[] { 0, 0, -0.08f, 0.042f, 0.042f, 0.03f }) },
        ["hammer"] = new[] { ("cw", new[] { 0, 0, -0.35f, 0.05f, 0.05f, 0.85f }), ("cr", new[] { 0, 0, -0.85f, 0.4f, 0.4f, 0.62f, 0, 90, 0 }), ("cy", new[] { 0, 0, -0.85f, 0.42f, 0.42f, 0.05f, 0, 90, 0 }), ("cs", new[] { 0.33f, 0, -0.85f, 0.36f, 0.36f, 0.04f, 0, 90, 0 }), ("cs", new[] { -0.33f, 0, -0.85f, 0.36f, 0.36f, 0.04f, 0, 90, 0 }), ("ck", new[] { 0, 0, 0.08f, 0.07f, 0.07f, 0.04f }) },
        ["sledge"] = new[] { ("cw", new[] { 0, 0, -0.4f, 0.045f, 0.045f, 1.0f }), ("bs", new[] { 0, 0, -0.92f, 0.44f, 0.2f, 0.2f }), ("bk", new[] { 0, 0, -0.92f, 0.1f, 0.22f, 0.22f }), ("bd", new[] { 0.2f, 0, -0.92f, 0.04f, 0.22f, 0.22f }), ("bd", new[] { -0.2f, 0, -0.92f, 0.04f, 0.22f, 0.22f }), ("bk", new[] { 0, 0, 0.05f, 0.06f, 0.06f, 0.12f }) },
        ["knife"] = new[] { ("ts", new[] { 0, 0.01f, -0.2f, 0.02f, 0.06f, 0.28f }), ("bd", new[] { 0, 0, -0.05f, 0.1f, 0.03f, 0.02f }), ("bk", new[] { 0, 0, 0.03f, 0.03f, 0.04f, 0.13f }), ("or", new[] { 0, 0, 0.1f, 0.03f, 0.03f, 0.03f }) },
        ["hmg"] = new[] { ("bd", new[] { 0, 0.05f, -0.1f, 0.16f, 0.18f, 0.5f }), ("ck", new[] { 0.045f, 0.09f, -0.58f, 0.045f, 0.045f, 0.6f }), ("ck", new[] { -0.045f, 0.09f, -0.58f, 0.045f, 0.045f, 0.6f }), ("ck", new[] { 0, 0.02f, -0.58f, 0.045f, 0.045f, 0.6f }), ("cs", new[] { 0, 0.065f, -0.88f, 0.15f, 0.15f, 0.05f }),
            ("cy", new[] { -0.15f, 0.0f, -0.05f, 0.16f, 0.16f, 0.12f, 0, 90, 0 }), ("bk", new[] { 0, -0.1f, 0.05f, 0.06f, 0.16f, 0.08f, 12 }), ("bo", new[] { 0.085f, 0.1f, -0.2f, 0.01f, 0.03f, 0.3f }), ("bk", new[] { 0, 0.16f, -0.1f, 0.04f, 0.06f, 0.25f }) },
        ["mechmg"] = new[] { ("bs", new[] { 0, 0.05f, -0.05f, 0.16f, 0.16f, 0.4f }), ("cd", new[] { 0, 0.05f, -0.42f, 0.16f, 0.16f, 0.36f }), ("ck", new[] { 0.05f, 0.05f, -0.76f, 0.04f, 0.04f, 0.36f }), ("ck", new[] { -0.05f, 0.05f, -0.76f, 0.04f, 0.04f, 0.36f }), ("ck", new[] { 0, 0.1f, -0.76f, 0.04f, 0.04f, 0.36f }), ("ck", new[] { 0, 0, -0.76f, 0.04f, 0.04f, 0.36f }),
            ("cb", new[] { 0, 0.05f, -0.94f, 0.17f, 0.17f, 0.03f }), ("bk", new[] { 0, -0.08f, 0.05f, 0.06f, 0.15f, 0.08f, 12 }), ("bb", new[] { 0.085f, 0.05f, -0.05f, 0.01f, 0.04f, 0.3f }) },
        ["mechhoming"] = new[] { ("bs", new[] { 0, 0.08f, -0.15f, 0.3f, 0.24f, 0.5f }), ("pr", new[] { -0.07f, 0.13f, -0.42f, 0.06f, 0.06f, 0.1f }), ("pr", new[] { 0.07f, 0.13f, -0.42f, 0.06f, 0.06f, 0.1f }), ("pr", new[] { -0.07f, 0.03f, -0.42f, 0.06f, 0.06f, 0.1f }), ("pr", new[] { 0.07f, 0.03f, -0.42f, 0.06f, 0.06f, 0.1f }),
            ("by", new[] { 0, 0.21f, -0.15f, 0.31f, 0.02f, 0.4f }), ("bk", new[] { 0, -0.06f, 0, 0.06f, 0.14f, 0.08f, 10 }), ("bk", new[] { 0, 0.08f, 0.12f, 0.26f, 0.2f, 0.06f }) },
        ["mechbomb"] = new[] { ("cd", new[] { 0, 0.1f, -0.2f, 0.26f, 0.26f, 0.6f }), ("co", new[] { 0, 0.1f, -0.51f, 0.22f, 0.22f, 0.02f }), ("cy", new[] { 0, 0.1f, -0.05f, 0.29f, 0.29f, 0.05f }), ("cy", new[] { 0, 0.1f, -0.35f, 0.29f, 0.29f, 0.05f }), ("bk", new[] { 0, -0.06f, 0, 0.06f, 0.14f, 0.08f, 10 }), ("os", new[] { 0, 0.1f, 0.12f, 0.2f, 0.2f, 0.2f }) },
    };
    static readonly Dictionary<string, WPiece[]> WLOOK = WRAW.ToDictionary(e => e.Key, e => e.Value.Select(p =>
    {
        float[] v = p.v; float rx = v.Length > 6 ? v[6] : 0, ry = v.Length > 7 ? v[7] : 0, rz = v.Length > 8 ? v[8] : 0;
        Q4 q = null; if (rx != 0 || ry != 0 || rz != 0) { q = new Q4(); QEuler(rx * PI / 180, ry * PI / 180, rz * PI / 180, q); }
        return new WPiece { key = "w" + p.k, shape = p.k[0], mat = p.k[1], x = v[0], y = v[1], z = v[2], sx = v[3], sy = v[4], sz = v[5], q = q };
    }).ToArray());
    void EnsureW(string key) { if (!IM.ContainsKey(key)) MkInst(key, WSHAPE[key[1]], Wmat(key[2]), 64, true); }
    Node WeaponMesh(string id)
    {
        var g = new Node();
        foreach (var pc in WLOOK.TryGetValue(id ?? "pistol", out var l) ? l : WLOOK["pistol"])
        {
            var b = new Mesh(WSHAPE[pc.shape], Wmat(pc.mat)) { Scale = new Vector3(pc.sx, pc.sy, pc.sz), Position = new Vector3(pc.x, pc.y, pc.z), CastShadow = true };
            if (pc.q != null) b.Rotation = new Quaternion(pc.q.x, pc.q.y, pc.q.z, pc.q.w);
            g.Add(b);
        }
        g.Scale = new Vector3(1.5f); return g;
    }

    // ---------------------------------------------------------------- germ bodies
    public readonly Dictionary<string, Species> GERM = new();
    static readonly Dictionary<string, string> ARMOR_STYLE_OF = new() { ["ally"] = "glass", ["ally2"] = "chitin", ["ally3"] = "slime", ["brute"] = "capsid" };
    static readonly Dictionary<string, uint> ARMOR_COL = new() { ["hero"] = 0xf7f4ff, ["ally"] = 0x9fe4ff, ["ally2"] = 0x8a9a52, ["ally3"] = 0xe9c6f5, ["brute"] = 0x5a4a5e, ["mhero"] = 0x1c1622, ["mally"] = 0x1c1622, ["mally2"] = 0x1c1622, ["mally3"] = 0x1c1622 };
    static uint DarkHex(uint c, float k) { uint r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255; return ((uint)MathF.Round(r * k) << 16) | ((uint)MathF.Round(g * k * 0.6f) << 8) | (uint)MathF.Round(b * k * 0.75f); }
    // ---------------------------------------------------------------- the heroes are medicine, the enemies germs
    public readonly Dictionary<string, MedSpec> MED = new();
    static readonly Dictionary<string, string> MED_OF = new() { ["ally"] = "tablet", ["ally2"] = "bottle", ["ally3"] = "syringe", ["mecha"] = "medmech" };
    void BuildGerms()
    {
        foreach (var t in GermDraw.TYPE_SPECIES.Keys.Append("mecha"))
        {
            string src = MIMIC_OF.TryGetValue(t, out var mm) ? mm : t;
            if (src == "hero" || MED_OF.ContainsKey(src))
            {
                var med = src == "hero" ? MedDraw.FromCustom(CFG.Custom) : MedDraw.PRESETS[MED_OF[src]].Clone();
                if (src != "hero") med.Col.D = ARMOR_COL.TryGetValue(src, out var acm) ? acm : med.Col.D;
                if (MIMIC_OF.ContainsKey(t)) med.Col = new SpeciesColors { A = DarkHex(med.Col.A, 0.24f), B = 0x3a0610, C = 0xff1830, E = 0xff2020, D = 0x1c1622 };
                if (t == "mecha") med.Bulk = 1.25f;
                MED[t] = med;
                // a germ-species stand-in carries the colours for materials and chips
                var stub = GermDraw.SPECIES["coccus"].Clone(); stub.Key = t; stub.Col = med.Col.Clone(); stub.Facet = false; stub.ArmorStyle = med.ArmorStyle;
                GERM[t] = stub; continue;
            }
            var sp = GermDraw.SPECIES[GermDraw.TYPE_SPECIES[src]].Clone();
            sp.ArmorStyle = ARMOR_STYLE_OF.TryGetValue(src, out var st) ? st : "chitin"; sp.Col.D = ARMOR_COL.TryGetValue(src, out var ac) ? ac : 0xd8d0e8;
            if (src == "mech") sp.Col = new SpeciesColors { A = 0x7a5ad8, B = 0x2a1a5a, C = 0xff3a6a, E = 0xff3a6a, D = 0x5a4a6e };   // the giant phage
            GERM[t] = sp;
        }
    }
    static Dictionary<char, Geometry> GGEO;
    readonly Dictionary<string, string> GK = new();
    string GermKey(string t, char sh, char tone)
    {
        string ck = t + sh + tone; if (GK.TryGetValue(ck, out var key)) return key;
        key = GK[ck] = t + "G" + sh + tone;
        if (!IM.ContainsKey(key))
        {
            var sp = GERM[t]; uint col = tone == 'k' ? 0x0a0510 : sp.Col.Get(tone);
            Material mat = tone == 'e' || tone == 'k' ? BasicM(col)
                : tone == 'c' ? Toon(new Material { Kind = MatKind.Phong, Color = C(col), Emissive = C(col) * 0.55f, Shininess = 90, Specular = Vector3.One }, 5)
                : Toon(new Material { Kind = MatKind.Phong, Color = C(col), Shininess = tone == 'd' ? 90 : 55, Specular = C(tone == 'd' ? 0xaaaaccu : 0x553344u) }, 5);
            MkInst(key, GGEO[sh], mat, 256, tone != 'e' && tone != 'k');
        }
        return key;
    }
    string CoreKey(string t, int i) => GermKey(GERM.ContainsKey(t) ? t : "normal", 'Z', PDEF[i].Dark ? 'b' : 'a');
    string EMIT_T = "hero";
    void GermEmit(char sh, char tone, float x, float y, float z, float qx, float qy, float qz, float qw, float sx, float sy, float sz)
        => InstAdd(tone == 'f' ? "flash" : GermKey(EMIT_T, sh, tone), x, y, z, qx, qy, qz, qw, sx, sy, sz);
    Emit germEmit;
    readonly DrawOpts GOPT = new();
    void DrawGermActor(Actor A, int lod, float fdt, float alpha)
    {
        var G = A.P; var sp = GERM[A.type];
        if (fdt > 0) for (int i = 0; i < NPART; i++) if (A.flash[i] > 0) A.flash[i] -= fdt;
        var o = GOPT; o.T = clockT + A.uid * 0.37f; o.Fdt = MathF.Min(fdt, 1 / 30f); o.Lod = lod; o.Has = A.has; o.Flash = A.flash; o.Curl = G.curl; o.St = A.springs; o.S = G.s;
        o.HasBall = G.curl > 0.6f;
        if (o.HasBall) { o.BallX = Lerp(G.px, G.x, alpha); o.BallY = Lerp(G.py, G.y, alpha) + G.BR; o.BallZ = Lerp(G.pz, G.z, alpha); o.BallR = G.BR; QNlerp(G.pBallQ, G.ballQ, alpha, o.BallQ); }
        EMIT_T = A.type;
        if (MED.TryGetValue(A.type, out var med)) MedDraw.Draw(med, A.pose, o, germEmit);
        else GermDraw.Draw(sp, A.pose, o, germEmit);
    }
    // ---- germ chunks: hits knock blobs of membrane off; a burst part sprays them
    void GermChips(Actor B, int part, int n, float burst, Dmg o)
    {
        if (B.poseT < 0) PosePartsNow(B);
        var ps = B.pose; int k = part * 13; var G = B.P; var sp = GERM[B.type];
        for (int i = 0; i < n; i++)
        {
            float u = (R() - 0.5f) * 1.6f, a = R() * TAU, r = MathF.Max(0.03f, ps[k + 7] * (0.35f + R() * 0.3f));
            float x = ps[k] + ps[k + 10] * ps[k + 8] * u + MathF.Cos(a) * ps[k + 7] * 0.8f, y = ps[k + 1] + ps[k + 11] * ps[k + 8] * u + 0.05f, z = ps[k + 2] + ps[k + 12] * ps[k + 8] * u + MathF.Sin(a) * ps[k + 7] * 0.8f;
            if (o != null && o.hasHit && i == 0) { x = Lerp(x, o.hx, 0.6f); y = Lerp(y, o.hy, 0.6f); z = Lerp(z, o.hz, 0.6f); }
            if (!float.IsFinite(x + y + z)) continue;
            // chips are cheap particles, not rigid bodies: they arc, bounce on whatever is below and fade
            float dx = x - ps[k], dz = z - ps[k + 2], dl = Hypot(dx, dz); if (dl == 0) dl = 1; float sp2 = (2 + R() * 3) * burst;
            char tone = R() < 0.7f ? (part >= 9 ? 'b' : 'a') : R() < 0.5f ? 'c' : 'b';
            var c = new Chip
            {
                pos = new V3(x, y, z), pp = new V3(x, y, z), q = new Q4(), pq = new Q4(), r = r,
                vel = new V3(G.vx * 0.5f + dx / dl * sp2 + (o?.fx ?? 0) * 0.03f, 1.5f + R() * 3 * burst, G.vz * 0.5f + dz / dl * sp2 + (o?.fz ?? 0) * 0.03f),
                w = new V3((R() - 0.5f) * 14, (R() - 0.5f) * 14, (R() - 0.5f) * 14),
                mk = GermKey(B.type, sp.Facet ? 'I' : 'Z', tone), ell = new V3(r, r * (0.7f + R() * 0.5f), r), life = 2.5f + R() * 1.5f,
            };
            CHIPS.Add(c); while (CHIPS.Count > CHIP_MAX * 1.5f) CHIPS.RemoveAt(0);
        }
    }
    void ChipPieces(Actor B, int part, float dmg, Dmg o) { if (GERM.ContainsKey(B.type)) GermChips(B, part, 1 + (int)MathF.Floor(dmg / 12) + (R() < 0.5f ? 1 : 0), 1, o); }
    void BlowPieces(Actor B, int part, Dmg o, float burst) { if (GERM.ContainsKey(B.type)) GermChips(B, part, 5, burst, o); }
    void BodyKick(Actor B, Dmg o, float dmg, int part)
    {
        var D = B.dyn; if (D == null) return;
        var G = B.P; float fx = -MathF.Sin(G.facing), fz = -MathF.Cos(G.facing), l = Hypot(o.fx, o.fz); if (l == 0) l = 1;
        float dx = o.fx / l, dz = o.fz / l, k = MathF.Min(3, 0.6f + dmg / 12) * (B.type == "brute" ? 0.4f : B.type == "small" ? 1.5f : 1);
        D.vf += -(dx * fx + dz * fz) * k * 2.2f; D.vs += (dx * -fz + dz * fx) * k * 1.8f;
        if (part == 0) { D.vhx += (R() - 0.5f) * 6 * k; D.vhz += 3 * k; }
        D.vb -= 0.5f * k;
    }

    // ---------------------------------------------------------------- drawing everything that moves
    void InitDrawLists()
    {
        InitWShapes();
        GGEO = new() { ['Z'] = Geo.Sphere(1, 16, 11), ['I'] = Geo.Icosahedron(1, 1), ['B'] = boxGeo, ['P'] = Geo.Cone(1, 1, 9), ['C'] = Geo.Cylinder(1, 1, 1, 10), ['T'] = Geo.Torus(1, 0.18f, 7, 22).RotateX(PI / 2), ['F'] = Geo.Cylinder(1, 0.72f, 1, 14) };
        germEmit = GermEmit;
        var sph = Geo.Sphere(1, 14, 10); var lowSph = Geo.Sphere(1, 8, 6);
        MkInst("flash", sph, BasicM(0xff2638), 512, false);
        MkInst("mflash", lowSph, new Material { Kind = MatKind.Basic, Color = C(0xfff1a8), Transparent = true, Opacity = 0.9f, Blending = Blend.Additive, DepthWrite = false }, 128, false);
        Geometry Tb(int segs) => Geo.Cylinder(1, 1, 1, segs, 1, true).RotateX(PI / 2);
        Material Add(uint c, float op) => new() { Kind = MatKind.Basic, Color = C(c), Transparent = true, Opacity = op, Blending = Blend.Additive, DepthWrite = false };
        MkInst("beam", Tb(8), Add(0x5ff2ff, 0.85f), 64, false);
        MkInst("beamE", Tb(8), Add(0xff3048, 0.85f), 64, false);
        MkInst("sight", Tb(4), Add(0xff2040, 0.55f), 32, false);
        MkInst("trail", Tb(6), Add(0xe8f6ff, 0.55f), 128, false);
        MkInst("trailHot", Tb(6), Add(0xffb84a, 0.7f), 64, false);
        MkInst("aura", sph, Add(0xff1a2a, 0.2f), 64, false);
        MkInst("aura2", sph, Add(0x7a10ff, 0.12f), 64, false);
        // projectile looks
        MkInst("pj_bullet", Geo.Box(0.05f, 0.05f, 1.4f), BasicM(0xfff1a8), 256, false); MkInst("pj_ebullet", Geo.Box(0.05f, 0.05f, 1.4f), BasicM(0xff4fd8), 256, false);
        MkInst("pj_pellet", Geo.Box(0.06f, 0.06f, 0.6f), BasicM(0xffd27a), 128, false); MkInst("pj_rocket", Geo.Cylinder(0.07f, 0.1f, 0.6f, 8).RotateX(PI / 2), BasicM(0xff9a3c), 32, false);
        MkInst("pj_arrow", Geo.Box(0.03f, 0.03f, 0.9f), BasicM(0x8a5a3a), 32, false); MkInst("pj_knife", Geo.Box(0.06f, 0.02f, 0.32f), BasicM(0xd8e4f0), 32, false);
        MkInst("pj_dart", Geo.Box(0.025f, 0.025f, 0.3f), BasicM(0xfff23a), 64, false); MkInst("pj_needle", Geo.Cone(0.035f, 0.32f, 5).RotateX(-PI / 2), BasicM(0xff5ce1), 64, false);
        MkInst("pj_plasma", Geo.Sphere(0.16f, 10, 8), BasicM(0x6dff8a), 64, false); MkInst("pj_plasma2", Geo.Sphere(0.32f, 10, 8), BasicM(0xb6ff3a), 16, false);
        MkInst("pj_laser", Geo.Box(0.04f, 0.04f, 2.6f), BasicM(0xff3048), 128, false); MkInst("pj_hlaser", Geo.Box(0.04f, 0.04f, 2.6f), BasicM(0x5ff2ff), 128, false);
        // pickups
        foreach (var (k, v) in ITEM)
        {
            var geo = k == "repair" ? Geo.Octahedron(0.34f) : k == "homing" ? Geo.Icosahedron(0.32f, 0) : Geo.Box(0.5f, 0.36f, 0.5f);
            MkInst("it_" + k, geo, new Material { Kind = MatKind.Phong, Color = C(v.color), Emissive = C(v.color) * 0.5f, Shininess = 80 }, 64, true);
        }
        foreach (var k in WRAW.Values.SelectMany(v => v).Select(p => p.k).Distinct()) EnsureW("w" + k);
        foreach (var D in VDEF.Values) foreach (var p in D.Pieces) { EnsureW(VkeyRaw(p.Key, 'h')); EnsureW(VkeyRaw(p.Key, 'e')); }
    }
    static string VkeyRaw(string k, char team) => "w" + k[0] + (k[1] == 'T' ? team : k[1]);

    static readonly int[] AURA_PARTS = { 1, 2, 0, 4, 7, 10, 13 };
    void DrawAura(Actor A)
    {
        var ps = A.pose; float s = A.P.s, f = clockT * 9 + A.uid;
        foreach (int i in AURA_PARTS)
        {
            if (!A.has[i]) continue;
            int k = i * 13; float r = 1 + 0.12f * MathF.Sin(f + i * 1.7f) + 0.06f * MathF.Sin(f * 2.3f + i);
            InstAdd("aura", ps[k], ps[k + 1] + 0.05f * s * MathF.Sin(f * 0.7f + i), ps[k + 2], ps[k + 3], ps[k + 4], ps[k + 5], ps[k + 6], ps[k + 7] * 1.7f * r, ps[k + 8] * 1.45f * r, ps[k + 9] * 1.7f * r);
            InstAdd("aura2", ps[k], ps[k + 1] + 0.12f * s, ps[k + 2], ps[k + 3], ps[k + 4], ps[k + 5], ps[k + 6], ps[k + 7] * 2.4f * r, ps[k + 8] * 2.0f * r, ps[k + 9] * 2.4f * r);
        }
        // embers rising off it
        if (R() < 0.25f) Fx("spark", A.P.x + (R() - 0.5f) * s, A.P.y + R() * A.P.H, A.P.z + (R() - 0.5f) * s, 0.25f, 0.5f, 0, 1.5f, 0);
    }
    readonly Q4 XQ = new(), XQW = new(); readonly V3 XV = new(), XO = new();
    void InstBeam(string key, float x0, float y0, float z0, float x1, float y1, float z1, float r)
    {
        float dx = x1 - x0, dy = y1 - y0, dz = z1 - z0, L = Hypot(dx, dy, dz); if (L < 0.05f) return;
        AimQuat(dx, dy, dz, XQ); InstAdd(key, (x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2, XQ.x, XQ.y, XQ.z, XQ.w, r, r, L);
    }
    void DrawExtras(Actor A)
    {
        var G = A.P; var ps = A.pose; float s = G.s;
        if (TYPES[A.type].Mimic != null && G.curl < 0.7f) DrawAura(A);
        // armor plates
        var gs = GERM.TryGetValue(A.type, out var g0) ? g0 : null;
        foreach (var pc in A.armor)
        {
            if (pc.hp <= 0 || !A.has[pc.part]) continue;
            if (pc.flash > 0) pc.flash -= 1 / 60f;
            if (G.curl >= 0.6f) continue;
            EMIT_T = A.type;
            if (MED.TryGetValue(A.type, out var med)) MedDraw.Armor(med, A.pose, pc.part, pc.y0, pc.y1, pc.phi, med.ArmorStyle, pc.flash > 0 ? 'f' : 'd', germEmit);
            else if (gs != null) GermDraw.ArmorPiece(gs.ArmorStyle, new ArmorPieceSpec(pc.part, pc.y0, pc.y1, pc.phi, pc.arc, pc.sc), A.pose, germEmit, pc.flash > 0 ? 'f' : 'd', clockT, GermDraw.LimbK(gs, pc.part));
        }
        // weapon in hand
        var w = A.W.slots[A.W.cur];
        if (w != null && A.has[PART.handR] && G.curl < 0.5f && G.ragdoll == null && !G.berserk)
        {
            int k = PART.handR * 13, fk = PART.farmR * 13; var def = WPN[w.id]; float gsc = s * (A.type == "brute" ? 1.15f : A.type == "small" ? 1.3f : 1);
            float ax, ay, az;
            if (def.Type == "melee")
            {
                ax = ps[fk + 10]; ay = ps[fk + 11] + 0.9f; az = ps[fk + 12]; float l = Hypot(ax, ay, az); if (l == 0) l = 1; ax /= l; ay /= l; az /= l;
                if (A.anim.swingT > 0) { ax = ps[fk + 10]; ay = ps[fk + 11]; az = ps[fk + 12]; }
            }
            else if (A.anim.aimK > 0.3f && A.AIM.ok) { ax = A.AIM.fx; ay = A.AIM.fy; az = A.AIM.fz; }
            else { ax = ps[fk + 10] - MathF.Sin(G.facing) * 0.6f; ay = ps[fk + 11]; az = ps[fk + 12] - MathF.Cos(G.facing) * 0.6f; float l = Hypot(ax, ay, az); if (l == 0) l = 1; ax /= l; ay /= l; az /= l; }
            AimQuat(ax, ay, az, XQ);
            var look = WLOOK.TryGetValue(w.id, out var lk) ? lk : WLOOK["pistol"];
            foreach (var pc in look)
            {
                XV.x = pc.x * gsc; XV.y = pc.y * gsc; XV.z = pc.z * gsc; QRot(XQ, XV, XO);
                var q = pc.q != null ? QMul(XQ, pc.q, XQW) : XQ;
                InstAdd(pc.key, ps[k] + XO.x, ps[k + 1] + XO.y, ps[k + 2] + XO.z, q.x, q.y, q.z, q.w, pc.sx * gsc, pc.sy * gsc, pc.sz * gsc);
            }
            if (A.W.charge > 0) { float fl = 0.05f + 0.12f * A.W.charge * gsc; InstAdd("mflash", ps[k] + ax * 0.5f * gsc, ps[k + 1] + ay * 0.5f * gsc, ps[k + 2] + az * 0.5f * gsc, 0, 0, 0, 1, fl, fl, fl); }
            // a melee swing leaves a streak behind the head of the weapon
            if (def.Type == "melee")
            {
                var tr = A.trail ??= new(); float L = (w.id == "knife" ? 0.4f : w.id == "bat" ? 0.85f : 1.0f) * gsc;
                if (A.anim.swingT > 0) { tr.Add(new[] { ps[k] + ax * L, ps[k + 1] + ay * L, ps[k + 2] + az * L }); if (tr.Count > 7) tr.RemoveAt(0); }
                else if (tr.Count > 0) tr.RemoveAt(0);
                for (int i = 1; i < tr.Count; i++) InstBeam(A.animSwingFin ? "trailHot" : "trail", tr[i - 1][0], tr[i - 1][1], tr[i - 1][2], tr[i][0], tr[i][1], tr[i][2], 0.03f + 0.06f * i / tr.Count * gsc);
            }
        }
        else A.trail?.Clear();
        // continuous laser, and the red sight line of an enemy marksman lining up a shot
        if (A.W.beam != null) { var b = A.W.beam; InstBeam(A.team == "hero" ? "beam" : "beamE", b.x0, b.y0, b.z0, b.x1, b.y1, b.z1, 0.05f + R() * 0.03f); }
        if (A.brain != null && A.brain.sightT > 0 && A.brain.tgtA != null && A.has[PART.handR])
        {
            int k = PART.handR * 13; var T = A.brain.tgtA.P;
            InstBeam("sight", ps[k], ps[k + 1], ps[k + 2], T.x, T.y + T.H * 0.6f, T.z, 0.015f + 0.02f * (1 - A.brain.sightT));
        }
    }
    void DrawVehicles(float alpha)
    {
        float cx = camPos.X, cz = camPos.Z;
        foreach (var Vh in vehicles)
        {
            if (Vh.dead) continue;
            float x = Lerp(Vh.px, Vh.x, alpha), z = Lerp(Vh.pz, Vh.z, alpha);
            if (MathF.Abs(x - cx) > 320 || MathF.Abs(z - cz) > 320) continue;
            float fl = Vh.flash > 0 ? 1.04f : 1;
            foreach (var q in Vh.pieces)
            {
                if (!q.on) continue;
                var p = q.p; var w = PieceWorldV(Vh, q, alpha); string key = Vkey(Vh, p.Key); EnsureW(key);
                InstAdd(key, w.x, w.y, w.z, w.q.x, w.q.y, w.q.z, w.q.w, p.Sx * fl, p.Sy * fl, p.Sz * fl);
            }
            if (Vh.beams != null) foreach (var b in Vh.beams) InstBeam("beam", b[0], b[1], b[2], b[3], b[4], b[5], 0.07f + R() * 0.05f);
            if (Vh.kind == "rover" && Vh.boostT > 0) { var f = Vfwd(Vh); InstAdd("mflash", x - f.x * 2.1f, Lerp(Vh.py, Vh.y, alpha) + 1, z - f.z * 2.1f, 0, 0, 0, 1, 0.5f, 0.5f, 0.5f); }
        }
    }
    int frameNo;
    readonly Q4 DQ = new();
    /// <summary>Fills every instanced draw list for this frame: germs, gear, vehicles, chips, debris, shots, beams, pickups.</summary>
    void DrawMen(float alpha, float fdt)
    {
        InstBegin(); frameNo++;
        float cx = camPos.X, cz = camPos.Z;
        foreach (var A in actors)
        {
            var G = A.P; if (!G.alive || A.hidden) continue;
            float d = Hypot(G.x - cx, G.z - cz); if (d > SET.drawDist * MathF.Max(1, G.s * 0.6f)) continue;
            if (A.human && SET.fp) { if (A.dormant) continue; PoseActor(A, alpha, fdt); continue; }
            if ((A.dormant || (d > 110 && (frameNo + A.uid) % 3 != 0)) && A.poseT >= 0) { /* frozen or far away: reuse the last pose */ } else PoseActor(A, alpha, fdt);
            int lod = d < 16 * MathF.Max(1, G.s * 0.7f) || A.human ? 2 : d < 55 ? 1 : 0;
            DrawGermActor(A, lod, fdt, alpha); DrawExtras(A);
        }
        DrawVehicles(alpha);
        foreach (var b in CHIPS)
        {
            if (MathF.Abs(b.pos.x - cx) > 160 || MathF.Abs(b.pos.z - cz) > 160) continue;
            QNlerp(b.pq, b.q, alpha, DQ); float fade = b.life < 0.7f ? MathF.Max(0.05f, b.life / 0.7f) : 1;
            InstAdd(b.mk, Lerp(b.pp.x, b.pos.x, alpha), Lerp(b.pp.y, b.pos.y, alpha), Lerp(b.pp.z, b.pos.z, alpha), DQ.x, DQ.y, DQ.z, DQ.w, b.ell.x * fade, b.ell.y * fade, b.ell.z * fade);
        }
        foreach (var b in debris)
        {
            if (b.Ell == null || b.Mk == null || MathF.Abs(b.Pos.x - cx) > 260 || MathF.Abs(b.Pos.z - cz) > 260) continue;
            QNlerp(b.Pq, b.Q, alpha, DQ); float fade = b.Life < 0.7f ? MathF.Max(0.05f, b.Life / 0.7f) : 1;   // shrink away rather than pop
            InstAdd(b.Mk, Lerp(b.Pp.x, b.Pos.x, alpha), Lerp(b.Pp.y, b.Pos.y, alpha), Lerp(b.Pp.z, b.Pos.z, alpha), DQ.x, DQ.y, DQ.z, DQ.w, b.Ell.x * fade, b.Ell.y * fade, b.Ell.z * fade);
        }
        // ragdoll parts are drawn through the pose; loose ragdoll bodies that are no longer anyone's become debris above
        foreach (var s in shots)
        {
            string key = s.kind == "bullet" && s.team == "enemy" ? "pj_ebullet" : s.kind == "laser" && s.team != "enemy" ? "pj_hlaser" : s.kind == "plasma" && s.charged ? "pj_plasma2" : "pj_" + s.kind;
            if (!IM.ContainsKey(key)) key = "pj_bullet";
            AimQuat(s.vx, s.vy, s.vz, XQ); InstAdd(key, s.x, s.y, s.z, XQ.x, XQ.y, XQ.z, XQ.w, 1, 1, 1);
        }
        foreach (var b in beams) { float k = 1 - b.t / b.life; InstBeam(b.color == 0x5ff2ffu ? "beam" : "beamE", b.x0, b.y0, b.z0, b.x1, b.y1, b.z1, b.r * MathF.Max(0.1f, k)); }
        // pickups: spinning, bobbing; weapons are drawn with their own pieces
        foreach (var it in items)
        {
            if (MathF.Abs(it.x - cx) > 200 || MathF.Abs(it.z - cz) > 200) continue;
            float yaw = it.rack ? it.rot : it.t * 1.6f, bobY = it.rack ? 0 : MathF.Sin(it.t * 2.2f) * 0.12f;
            var q = new Q4(); QAxis(0, 1, 0, yaw, q);
            if (it.kind == "weapon")
            {
                if (it.rack) { var q2 = new Q4(); QAxis(1, 0, 0, -PI / 2, q2); QMul(q, q2, q); }
                foreach (var pc in WLOOK.TryGetValue(it.wid, out var l) ? l : WLOOK["pistol"])
                {
                    XV.x = pc.x * 1.5f; XV.y = pc.y * 1.5f; XV.z = pc.z * 1.5f; QRot(q, XV, XO); var qq = pc.q != null ? QMul(q, pc.q, XQW) : q;
                    InstAdd(pc.key, it.x + XO.x, it.y + bobY + XO.y, it.z + XO.z, qq.x, qq.y, qq.z, qq.w, pc.sx * 1.5f, pc.sy * 1.5f, pc.sz * 1.5f);
                }
            }
            else InstAdd("it_" + it.kind, it.x, it.y + bobY, it.z, q.x, q.y, q.z, q.w, 1, 1, 1);
        }
        InstEnd();
    }
    /// <summary>Moves prop meshes to their bodies (interpolated) and lights bomb fuses.</summary>
    void SyncProps(float alpha)
    {
        foreach (var b in bodies)
        {
            var pr = b.Prop; if (pr?.vis == null) continue;
            QNlerp(b.Pq, b.Q, alpha, DQ);
            pr.vis.Position = new Vector3(Lerp(b.Pp.x, b.Pos.x, alpha), Lerp(b.Pp.y, b.Pos.y, alpha), Lerp(b.Pp.z, b.Pos.z, alpha));
            pr.vis.Rotation = new Quaternion(DQ.x, DQ.y, DQ.z, DQ.w);
            if (pr.lit != null) pr.lit.Mat = b.Fuse > 0 && ((int)(clockT * (b.Fuse < 0.6f ? 16 : 6)) & 1) == 0 ? bombLit : bombDim;
        }
    }
    void OnBodyRemoved(Body b) { if (b.Prop?.vis != null) scene.Remove(b.Prop.vis); }
}

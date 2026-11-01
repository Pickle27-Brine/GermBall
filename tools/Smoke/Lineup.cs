using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using GermBall.Core;
using GermBall.Game;
using GermBall.Platform;
using GermBall.Render;

namespace Smoke;

/// <summary>All the heroes (and the enemy germs) standing in a row, front three-quarter view, for judging silhouettes.</summary>
public static class Lineup
{
    public static void Run(SoftRenderer sr, string file, bool germs, float yawDeg = 25, bool armor = true, string pose = "rest")
    {
        var scene = new Scene { Background = Material.Hex(0x1a0618), FogNear = 1000, FogFar = 2000, Shadows = false, HemiSky = Material.Hex(0xffe8f4), HemiGround = Material.Hex(0x40142a), HemiIntensity = 0.85f, SunDir = Vector3.Normalize(new Vector3(2, 4, 3)), SunIntensity = 0.85f, SunColor = Vector3.One };
        var GEO = new Dictionary<char, Geometry> { ['Z'] = Geo.Sphere(1, 18, 12), ['I'] = Geo.Icosahedron(1, 1), ['B'] = Geo.Box(1, 1, 1), ['P'] = Geo.Cone(1, 1, 12), ['C'] = Geo.Cylinder(1, 1, 1, 16), ['T'] = Geo.Torus(1, 0.18f, 8, 24).RotateX(MathF.PI / 2), ['F'] = Geo.Cylinder(1, 0.72f, 1, 16) };
        var keys = germs ? GermDraw.SPECIES.Keys.ToArray() : MedDraw.PRESETS.Keys.ToArray();
        int n = keys.Length; float spacing = 1.6f;
        for (int idx = 0; idx < n; idx++)
        {
            var MESH = new Dictionary<string, InstancedMesh>();
            float ox = (idx - (n - 1) / 2f) * spacing;
            SpeciesColors col = germs ? GermDraw.SPECIES[keys[idx]].Col : MedDraw.PRESETS[keys[idx]].Col;
            void Emit(char sh, char tone, float x, float y, float z, float qx, float qy, float qz, float qw, float sx, float sy, float sz)
            {
                if (tone == 'f') tone = 'a';
                string k = new(new[] { sh, tone });
                if (!MESH.TryGetValue(k, out var m))
                {
                    uint c = tone == 'k' ? 0x0a0510u : col.Get(tone);
                    var mat = tone == 'e' || tone == 'k' ? new Material { Kind = MatKind.Basic, Color = Material.Hex(c) } : tone == 'c' ? new Material { Kind = MatKind.Phong, Color = Material.Hex(c), Emissive = Material.Hex(c) * 0.55f } : new Material { Kind = MatKind.Phong, Color = Material.Hex(c) };
                    m = new InstancedMesh(GEO[sh], mat, 64) { Count = 0 }; MESH[k] = m; scene.Add(m);
                }
                m.EnsureCapacity(m.Count + 1);
                m.Matrices[m.Count++] = Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateFromQuaternion(new Quaternion(qx, qy, qz, qw)) * Matrix4x4.CreateTranslation(x + ox, y, z);
            }
            var ps = GermDraw.RestPose(1, 0.7f);
            var o = new DrawOpts { T = 0.7f, Fdt = 1 / 60f, Lod = 2, Has = Enumerable.Repeat(true, 15).ToArray(), St = GermDraw.NewSpringState(), S = 1 };
            if (germs) GermDraw.Draw(GermDraw.SPECIES[keys[idx]], ps, o, Emit);
            else
            {
                var sp = MedDraw.PRESETS[keys[idx]];
                MedDraw.Draw(sp, ps, o, Emit);
                if (armor) foreach (var p in GermDraw.HERO_ARMOR) MedDraw.Armor(sp, ps, p.Part, p.Y0, p.Y1, p.Phi, sp.ArmorStyle, 'd', Emit);
            }
            foreach (var m in MESH.Values) m.Touch();
        }
        float yaw = (180 + yawDeg) * MathF.PI / 180, dist = n * spacing * 0.62f + 3;
        var cam = new Camera { Fov = 30, Near = 0.05f, Far = 100 };
        cam.Position = new Vector3(MathF.Sin(yaw) * dist, 1.3f + dist * 0.08f, MathF.Cos(yaw) * dist); cam.LookAt(new Vector3(0, 1.0f, 0));
        sr.Render(scene, cam);
        var px = sr.Capture(out int w, out int h); File.WriteAllBytes(file, Screenshot.Png(px, w, h));
        Console.WriteLine("lineup " + file);
    }
}

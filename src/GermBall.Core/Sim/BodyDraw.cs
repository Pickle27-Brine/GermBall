using System;
using System.Numerics;
using GermBall.Engine;

namespace GermBall.Sim;

/// <summary>Draws humanoid bodies from their joint positions: the gelcap hero and the four germs.</summary>
public sealed partial class Game
{
    static readonly Vector4 HeroRed = MathX.Rgb(0xE8303C), HeroWhite = MathX.Rgb(0xF4F1EC), HeroCyan = MathX.Rgb(0x7AFFEF), HeroBoot = MathX.Rgb(0x3A3F48);

    static void Capsule(DrawList dl, Vector3 a, Vector3 b, float r, Vector4 c)
    {
        dl.Rod(a, b, r, c); dl.Sphere(a, r, c); dl.Sphere(b, r, c);
    }

    /// <summary>A body's own axes from its pose: up along the spine, right across the shoulders, forward out of the chest.</summary>
    static (Vector3 up, Vector3 right, Vector3 fwd) Frame(Pose o)
    {
        var up = o[J.Chest] - o[J.Pelvis]; up = up.LengthSquared() > 1e-6f ? Vector3.Normalize(up) : Vector3.UnitY;
        var right = o[J.ShR] - o[J.ShL]; right -= up * Vector3.Dot(right, up);
        right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.UnitX;
        return (up, right, Vector3.Cross(up, right));
    }
    static Quaternion Orient(Vector3 up, Vector3 right, Vector3 fwd) =>
        Quaternion.CreateFromRotationMatrix(new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, -fwd.X, -fwd.Y, -fwd.Z, 0, 0, 0, 0, 1));

    /// <summary>The hero: a red-and-white gelcap with a visor helmet, pill-half pauldrons and a glowing cross.</summary>
    void DrawHero(DrawList dl, Pose o, bool flash, bool withGun)
    {
        var red = flash ? MathX.Rgb(0xFFFFFF) : HeroRed; var white = flash ? MathX.Rgb(0xFFFFFF) : HeroWhite;
        var (up, right, fwd) = Frame(o);
        var rot = Orient(up, right, fwd);

        // legs: white thighs, red shins, dark boots
        foreach (var (hip, knee, foot) in new[] { (J.HipL, J.KnL, J.FtL), (J.HipR, J.KnR, J.FtR) })
        {
            Capsule(dl, o[hip], o[knee], 0.085f, white);
            Capsule(dl, o[knee], o[foot], 0.072f, red);
            var shin = o[foot] - o[knee];
            var bootFwd = MathX.Flat(fwd); bootFwd = bootFwd.LengthSquared() > 1e-4f ? Vector3.Normalize(bootFwd) : -Vector3.UnitZ;
            var bootRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathX.YawOf(bootFwd));
            dl.Box(o[foot] + bootFwd * 0.05f - new Vector3(0, 0.02f, 0), new Vector3(0.13f, 0.11f, 0.27f), bootRot, HeroBoot);
            dl.Box(o[foot] + bootFwd * 0.16f - new Vector3(0, 0.035f, 0), new Vector3(0.12f, 0.06f, 0.07f), bootRot, white);
        }
        // hips and a two-tone capsule torso, narrow at the waist, broad and shallow at the chest
        Capsule(dl, o[J.HipL], o[J.HipR], 0.11f, white);
        var waist = Vector3.Lerp(o[J.Pelvis], o[J.Chest], 0.45f);
        Capsule(dl, o[J.Pelvis] + up * 0.04f, waist, 0.12f, white);
        Capsule(dl, waist, o[J.Chest] - up * 0.02f, 0.15f, red);
        Capsule(dl, o[J.ShL] - up * 0.03f, o[J.ShR] - up * 0.03f, 0.13f, red);
        var cross = o[J.Chest] + fwd * 0.135f - up * 0.06f;
        dl.Box(cross, new Vector3(0.12f, 0.035f, 0.02f), rot, HeroCyan, DrawFlags.Unlit);
        dl.Box(cross, new Vector3(0.035f, 0.12f, 0.02f), rot, HeroCyan, DrawFlags.Unlit);
        // neck and the capsule helmet with a visor
        dl.Rod(o[J.Neck] - up * 0.02f, o[J.Head], 0.05f, white);
        var headUp = o[J.Head] - o[J.Neck]; headUp = headUp.LengthSquared() > 1e-6f ? Vector3.Normalize(headUp) : up;
        var hc = o[J.Head];
        Capsule(dl, hc - headUp * 0.035f, hc + headUp * 0.01f, 0.125f, white);
        dl.Sphere(hc + headUp * 0.035f, 0.122f, red);
        dl.Rod(hc, hc + headUp * 0.035f, 0.123f, red);
        dl.Box(hc + fwd * 0.105f + headUp * 0.005f, new Vector3(0.2f, 0.055f, 0.05f), rot, HeroCyan, DrawFlags.Unlit);
        // arms: red pauldrons and upper arms, white forearms and hands
        foreach (var (sh, el, ha, sg) in new[] { (J.ShL, J.ElL, J.HaL, -1f), (J.ShR, J.ElR, J.HaR, 1f) })
        {
            dl.Sphere(o[sh] + right * sg * 0.03f + up * 0.03f, 0.105f, red);
            Capsule(dl, o[sh], o[el], 0.063f, red);
            Capsule(dl, o[el], o[ha], 0.057f, white);
            dl.Sphere(o[ha], 0.068f, white);
        }
        // the gun in the right hand
        var w = Weapon;
        if (withGun && w != null && SwitchT <= 0.15f)
        {
            var d = HeroAnim.GunDir(o, this);
            var gunRot = Quaternion.CreateFromYawPitchRoll(MathX.YawOf(d), MathF.Asin(MathX.Clamp(d.Y, -1, 1)), 0);
            float recoil = MathX.Clamp01(FireCooldown / MathF.Max(0.01f, w.Def.Interval)) * 0.06f;
            dl.Box(o[J.HaR] + d * (w.Def.Size.Z * 0.3f - recoil), w.Def.Size, gunRot, w.Def.Color);
        }
    }

    void DrawGerm(DrawList dl, Germ gm)
    {
        var o = gm.Pose; float s = gm.Type.Scale;
        var col = gm.HitFlash > 0 ? MathX.Rgb(0xFFFFFF) : gm.Type.Color;
        var dark = gm.Type.Dark;
        if (!gm.Alive) { col *= new Vector4(0.55f, 0.55f, 0.55f, 1); dark *= new Vector4(0.55f, 0.55f, 0.55f, 1); }
        var (up, right, fwd) = Frame(o);
        var body = Vector3.Lerp(o[J.Pelvis], o[J.Chest], 0.55f);

        // stubby limbs
        foreach (var (a, b, c) in new[] { (J.HipL, J.KnL, J.FtL), (J.HipR, J.KnR, J.FtR) })
        {
            Capsule(dl, o[a], o[b], 0.09f * s, dark); Capsule(dl, o[b], o[c], 0.08f * s, dark);
        }
        foreach (var (a, b, c) in new[] { (J.ShL, J.ElL, J.HaL), (J.ShR, J.ElR, J.HaR) })
        {
            Capsule(dl, o[a], o[b], 0.07f * s, dark); Capsule(dl, o[b], o[c], 0.06f * s, dark);
            dl.Sphere(o[c], 0.085f * s, col);
        }

        Vector3 eyes; float eyeR = 0.07f * s, eyeGap = 0.1f * s;
        switch (gm.Spawn.Kind)
        {
            case GermKind.Coccus:
                {
                    // a round berry with bumps
                    dl.Sphere(body, 0.36f * s, col);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * MathF.Tau / 6;
                        dl.Sphere(body + (right * MathF.Cos(a) + up * MathF.Sin(a) * 0.8f + fwd * 0.1f) * 0.33f * s, 0.09f * s, dark);
                    }
                    eyes = body + fwd * 0.3f * s + up * 0.12f * s;
                    break;
                }
            case GermKind.Bacillus:
                {
                    // a rod with trailing flagella
                    Capsule(dl, o[J.Pelvis] - up * 0.05f * s, o[J.Head], 0.24f * s, col);
                    for (int i = -1; i <= 1; i++)
                        dl.Rod(o[J.Pelvis] - fwd * 0.2f * s, o[J.Pelvis] - fwd * 0.6f * s + right * i * 0.25f * s - up * 0.2f * s, 0.025f * s, dark);
                    eyes = o[J.Head] + fwd * 0.2f * s;
                    break;
                }
            case GermKind.Brute:
                {
                    // a lumpy amoeba with pseudopods on its shoulders
                    dl.Sphere(body + up * 0.05f * s, 0.45f * s, col);
                    dl.Sphere(o[J.ShL] + up * 0.05f * s, 0.24f * s, col);
                    dl.Sphere(o[J.ShR] + up * 0.05f * s, 0.24f * s, col);
                    dl.Sphere(o[J.Pelvis] + fwd * 0.15f * s, 0.3f * s, dark);
                    eyes = body + fwd * 0.42f * s + up * 0.2f * s; eyeR = 0.06f * s; eyeGap = 0.13f * s;
                    break;
                }
            default:
                {
                    // a bacteriophage: capsid head on a collar and tail, glowing core
                    dl.Sphere(o[J.Head] + up * 0.12f * s, 0.32f * s, col);
                    dl.Sphere(o[J.Head] + up * 0.12f * s + fwd * 0.2f * s, 0.12f * s, MathX.Rgb(0xFF4050), DrawFlags.Unlit);
                    dl.Rod(o[J.Neck] - up * 0.02f * s, o[J.Neck] + up * 0.02f * s, 0.18f * s, dark);
                    Capsule(dl, o[J.Neck], o[J.Pelvis], 0.09f * s, dark);
                    dl.Rod(o[J.Pelvis] - up * 0.02f * s, o[J.Pelvis] + up * 0.02f * s, 0.22f * s, dark);
                    eyes = default;
                    break;
                }
        }
        if (gm.Spawn.Kind != GermKind.Titan)
        {
            var white = MathX.Rgb(0xFFFFFF); var black = MathX.Rgb(0x101010);
            foreach (float sg in new[] { -1f, 1f })
            {
                var e = eyes + right * sg * eyeGap;
                dl.Sphere(e, eyeR, gm.Alive ? white : MathX.Rgb(0x808080));
                if (gm.Alive) dl.Sphere(e + fwd * eyeR * 0.6f, eyeR * 0.5f, black);
            }
        }
        // health bar while hurt and standing
        if (gm.Standing && gm.Hp < gm.Type.Hp)
        {
            var barC = gm.Pos + new Vector3(0, gm.Height + 0.35f, 0);
            float f = MathX.Clamp01(gm.Hp / gm.Type.Hp), bw = 0.8f + 0.2f * s;
            var camRight = MathX.Right(CamYaw); var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, CamYaw);
            dl.Box(barC, new Vector3(bw, 0.1f, 0.02f), rot, MathX.Rgb(0x202020), DrawFlags.Unlit);
            dl.Box(barC - camRight * (0.5f * bw * (1 - f)) - CamForward * 0.02f, new Vector3(bw * f, 0.1f, 0.03f), rot, MathX.Rgb(0xE05050), DrawFlags.Unlit);
        }
        if (gm.Standing) BlobShadow(dl, gm.Pos + new Vector3(0, 0.3f, 0), gm.Radius * 1.3f);
    }
}

using System;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;

namespace GermBall.Sim;

public sealed partial class Game
{
    static Vector4 ZoneColor(ZoneKind k) => k switch
    {
        ZoneKind.Damage => MathX.Rgb(0x9CFF30, 0.45f),
        ZoneKind.Heal => MathX.Rgb(0xFF70B0, 0.45f),
        ZoneKind.Kill => MathX.Rgb(0xFF2020, 0.18f),
        ZoneKind.JumpPad => MathX.Rgb(0xFF9A30, 0.75f),
        ZoneKind.Boost => MathX.Rgb(0x30E0FF, 0.6f),
        ZoneKind.LowGravity => MathX.Rgb(0xB080FF, 0.07f),
        ZoneKind.Updraft => MathX.Rgb(0x90D0FF, 0.10f),
        ZoneKind.Checkpoint => MathX.Rgb(0x40FF80, 0.15f),
        _ => MathX.Rgb(0xFFFFFF, 0.04f),
    };

    /// <summary>Fills the draw list for this frame: camera, level, everything that moves, then the HUD.</summary>
    public void Draw(DrawList dl, int width, int height)
    {
        dl.Clear();
        float aspect = width / (float)Math.Max(1, height);
        dl.CamPos = CamPos;
        dl.View = Matrix4x4.CreateLookAt(CamPos, CamPos + CamForward, Vector3.UnitY);
        dl.Proj = Matrix4x4.CreatePerspectiveFieldOfView(Fov, aspect, 0.05f, 800f);
        DrawWorld(dl, true);
        DrawHud(dl, dl.Overlay, width, height);
    }

    /// <summary>The level and everything in it, without the camera or the HUD (the Map Editor draws its preview with this).</summary>
    public void DrawWorld(DrawList dl, bool withPlayer)
    {
        float a = alpha;
        foreach (var s in Level.Solids)
            dl.Box(s.Box.Center, s.Box.Half * 2, s.Box.Rot, s.Color, s.Grid ? DrawFlags.Grid : DrawFlags.None);

        DrawZones(dl);
        if (withPlayer) DrawPlayer(dl, a);
        DrawDummies(dl, a);
        DrawGerms(dl);
        foreach (var pr in Props)
        {
            var pos = Vector3.Lerp(pr.PrevPos, pr.Pos, a);
            dl.Add(MeshId.BallStriped, MathX.Trs(pos, Quaternion.Slerp(pr.PrevRot, pr.Rot, a), new Vector3(pr.Radius * 2)), pr.Color);
            BlobShadow(dl, pos, pr.Radius);
        }
        DrawPickups(dl);
        foreach (var pj in Projectiles)
            dl.Sphere(Vector3.Lerp(pj.PrevPos, pj.Pos, a), Projectile.Radius, MathX.Rgb(0x70C040), DrawFlags.Unlit);
        foreach (var d in Debris) d.Draw(dl);
        DrawEffects(dl);
    }

    void DrawZones(DrawList dl)
    {
        foreach (var z in Level.Zones)
        {
            var size = z.Max - z.Min; var c = z.Center;
            switch (z.Kind)
            {
                case ZoneKind.Area:
                    if (ShowZones) dl.Box(c, size, MathX.Rgb(0xFFFFFF, 0.035f), DrawFlags.Unlit);
                    break;
                case ZoneKind.JumpPad:
                case ZoneKind.Boost:
                    {
                        // a plate on the floor and an arrow showing which way it throws you
                        var plate = new Vector3(c.X, 0.03f, c.Z);
                        dl.Box(plate, new Vector3(size.X, 0.06f, size.Z), ZoneColor(z.Kind), DrawFlags.Unlit);
                        var dir = z.Kind == ZoneKind.JumpPad ? Vector3.Normalize(z.Dir) : Vector3.Normalize(z.Dir);
                        float pulse = 0.5f + 0.5f * MathF.Sin(Time * 6);
                        var basePt = plate + new Vector3(0, 0.25f + pulse * 0.2f, 0);
                        dl.Rod(basePt, basePt + dir * 0.7f, 0.25f, ZoneColor(z.Kind) with { W = 1 }, DrawFlags.Unlit, MeshId.Cone);
                        if (ShowZones) dl.Box(c, size, ZoneColor(z.Kind) with { W = 0.1f }, DrawFlags.Unlit);
                        break;
                    }
                case ZoneKind.Damage:
                case ZoneKind.Heal:
                    dl.Box(new Vector3(c.X, 0.02f, c.Z), new Vector3(size.X, 0.04f, size.Z), ZoneColor(z.Kind) with { W = 1 }, DrawFlags.Unlit);
                    if (ShowZones) dl.Box(c, size, ZoneColor(z.Kind) with { W = 0.1f }, DrawFlags.Unlit);
                    break;
                default:
                    dl.Box(c, size, ZoneColor(z.Kind), DrawFlags.Unlit);
                    break;
            }
        }
    }

    void BlobShadow(DrawList dl, Vector3 above, float radius)
    {
        var hit = Level.World.Raycast(above, -Vector3.UnitY, 40);
        if (!hit.Hit) return;
        float h = hit.T, fade = MathX.Clamp01(1 - h / 25);
        if (fade <= 0) return;
        float r = radius * (1.1f - 0.4f * (1 - fade));
        dl.Add(MeshId.Cylinder, MathX.Trs(hit.Point + hit.Normal * 0.025f, MathX.FromUpTo(hit.Normal), new Vector3(r * 2, 0.01f, r * 2)),
            new Vector4(0, 0, 0, 0.35f * fade), DrawFlags.Unlit);
    }

    void DrawPlayer(DrawList dl, float a)
    {
        var p = Player;
        if (PlayerRagdoll != null) { DrawHero(dl, Vector3.Zero, 0, false, PlayerRagdoll.Pos); return; }
        if (!p.Alive || p.Noclip) return;
        var feet = Vector3.Lerp(p.PrevPos, p.Pos, a);
        float yaw = p.PrevYaw + MathX.AngleWrap(p.Yaw - p.PrevYaw) * a;
        bool hurt = Time - p.LastHurt < 0.12f;
        BlobShadow(dl, feet + new Vector3(0, 0.3f, 0), 0.5f);
        if (p.Rolling)
        {
            // curled up: a striped ball in the entity's ball colour, or its own shapes spun like a ball ("pose" curl style)
            var c = feet + new Vector3(0, Player.BallRadius, 0);
            var spin = Quaternion.Slerp(p.PrevBallRot, p.BallRot, a);
            if (Hero.Def.CurlStyle == "pose")
            {
                FillHeroContext(HeroCtx, feet, yaw, 0);
                HeroCtx.Spin = spin * Quaternion.Inverse(Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw)); HeroCtx.SpinCenter = Player.BallRadius;
                Hero.Evaluate(HeroCtx); Hero.Draw(dl, hurt);
            }
            else dl.Add(MeshId.BallStriped, MathX.Trs(c, spin, new Vector3(Player.BallRadius * 2)), hurt ? MathX.Rgb(0xFFFFFF) : ColorHex.Parse(Hero.Def.BallColor));
            if (p.Pounding) dl.Rod(c + new Vector3(0, 0.6f, 0), c + new Vector3(0, 2.2f, 0), 0.3f, MathX.Rgb(0xFFFFFF, 0.35f), DrawFlags.Unlit);
            if (p.DashT > 0 && !p.Grounded) dl.Sphere(c, Player.BallRadius * 1.25f, MathX.Rgb(0x80E0FF, 0.25f), DrawFlags.Unlit);   // the dash is an attack
            return;
        }
        DrawHero(dl, feet, yaw, hurt, null);
    }

    void DrawGerms(DrawList dl)
    {
        foreach (var gm in Germs) if (gm.Alive || gm.Ragdoll != null) DrawGerm(dl, gm);
    }

    void DrawDummies(DrawList dl, float a)
    {
        foreach (var d in Dummies)
        {
            if (!d.Alive) continue;
            var pos = Vector3.Lerp(d.PrevPos, d.Pos, a);
            var tilt = Quaternion.CreateFromYawPitchRoll(0, d.Wobble.Y * 0.08f, -d.Wobble.X * 0.08f);
            var col = d.HitFlash > 0 ? MathX.Rgb(0xFFFFFF) : d.BaseColor;
            float bodyH = d.Height - d.HeadSize;
            var top = pos + Vector3.Transform(new Vector3(0, bodyH, 0), tilt);
            dl.Add(MeshId.Cylinder, MathX.Trs(pos + new Vector3(0, 0.05f, 0), Quaternion.Identity, new Vector3(d.Radius * 2.6f, 0.1f, d.Radius * 2.6f)), MathX.Rgb(0x505050));
            dl.Rod(pos, top, d.Radius, col);
            var headC = pos + Vector3.Transform(new Vector3(0, bodyH + d.HeadSize / 2, 0), tilt);
            dl.Sphere(headC, d.HeadSize / 2 + 0.02f, d.HitFlash > 0 ? MathX.Rgb(0xFFFFFF) : MathX.Rgb(0xE8D0A0));
            // a bullseye on the chest facing the firing line
            var chest = pos + Vector3.Transform(new Vector3(-d.Radius - 0.01f, bodyH * 0.6f, 0), tilt);
            dl.Rod(chest, chest + Vector3.Transform(new Vector3(-0.02f, 0, 0), tilt), d.Radius * 0.6f, MathX.Rgb(0xC03030));
            // health bar over the head
            if (d.Hp < d.MaxHp)
            {
                var barC = pos + new Vector3(0, d.Height + 0.35f, 0);
                float f = MathX.Clamp01(d.Hp / d.MaxHp);
                var camRight = MathX.Right(CamYaw); var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, CamYaw);
                dl.Box(barC, new Vector3(1.0f, 0.1f, 0.02f), rot, MathX.Rgb(0x202020), DrawFlags.Unlit);
                dl.Box(barC - camRight * (0.5f * (1 - f)) + CamForward * -0.02f, new Vector3(1.0f * f, 0.1f, 0.03f), rot, MathX.Rgb(0x60E060), DrawFlags.Unlit);
            }
        }
    }

    void DrawPickups(DrawList dl)
    {
        foreach (var pk in Pickups)
        {
            if (!pk.Active) continue;
            var c = pk.Pos + new Vector3(0, 0.15f * MathF.Sin(pk.Spin * 1.3f), 0);
            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, pk.Spin);
            switch (pk.Spawn.Kind)
            {
                case PickupKind.Health:
                    dl.Box(c, new Vector3(0.6f, 0.6f, 0.6f), rot, MathX.Rgb(0xF0F0F0));
                    dl.Box(c, new Vector3(0.62f, 0.4f, 0.12f), rot, MathX.Rgb(0xE03030), DrawFlags.Unlit);
                    dl.Box(c, new Vector3(0.12f, 0.4f, 0.62f), rot, MathX.Rgb(0xE03030), DrawFlags.Unlit);
                    break;
                case PickupKind.Ammo:
                    dl.Box(c, new Vector3(0.7f, 0.45f, 0.45f), rot, MathX.Rgb(0x707A3A));
                    dl.Box(c + new Vector3(0, 0.23f, 0), new Vector3(0.5f, 0.04f, 0.2f), rot, MathX.Rgb(0xE0C040), DrawFlags.Unlit);
                    break;
                case PickupKind.Rifle:
                    dl.Box(c, WeaponDef.Rifle.Size * 1.4f, rot, WeaponDef.Rifle.Color);
                    break;
                case PickupKind.Lobber:
                    dl.Box(c, WeaponDef.Lobber.Size * 1.4f, rot, WeaponDef.Lobber.Color);
                    break;
            }
            // a ring of light under it
            dl.Add(MeshId.Cylinder, MathX.Ts(new Vector3(pk.Pos.X, 0.02f, pk.Pos.Z), new Vector3(1.2f, 0.02f, 1.2f)), MathX.Rgb(0xFFF0A0, 0.4f), DrawFlags.Unlit);
        }
    }

    void DrawEffects(DrawList dl)
    {
        foreach (var p in Effects.Particles)
        {
            float k = p.Life / p.MaxLife;
            dl.Box(p.Pos, new Vector3(p.Size * (0.4f + 0.6f * k)), p.Color, DrawFlags.Unlit);
        }
        foreach (var t in Effects.Tracers)
            dl.Rod(t.A, t.B, 0.02f, t.Color with { W = MathX.Clamp01(t.Life / 0.06f) * 0.9f }, DrawFlags.Unlit);
        foreach (var f in Effects.Flashes)
        {
            float k = f.Life / f.MaxLife;
            dl.Sphere(f.Pos, f.Radius * (1.2f - 0.5f * k), f.Color with { W = 0.6f * k }, DrawFlags.Unlit);
        }
    }
}

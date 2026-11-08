using System;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;

namespace GermBall.Sim;

/// <summary>Draws the hero and the germs through their entity definitions (shapes on an animated rig).</summary>
public sealed partial class Game
{
    /// <summary>The hero standing (or lying, when <paramref name="joints"/> is a ragdoll), plus the gun in its right hand.</summary>
    void DrawHero(DrawList dl, Vector3 feet, float yaw, bool flash, Pose joints)
    {
        FillHeroContext(HeroCtx, feet, yaw, 0);
        HeroCtx.Joints = joints;
        if (joints != null) { HeroCtx.Rolling = false; HeroCtx.Armed = false; }
        Hero.Evaluate(HeroCtx);
        if (Hero.Sk.Humanoid) Hero.Anim.WorldJoints(Hero.Pose, HeroPose);
        Hero.Draw(dl, flash);
        if (joints != null) return;

        var w = Weapon; int hand = Hero.Sk.IndexOf("hand.R");
        if (w != null && SwitchT <= 0.15f && hand >= 0)
        {
            var d = GunDir();
            var gunRot = Quaternion.CreateFromYawPitchRoll(MathX.YawOf(d), MathF.Asin(MathX.Clamp(d.Y, -1, 1)), 0);
            float recoil = MathX.Clamp01(FireCooldown / MathF.Max(0.01f, w.Def.Interval)) * 0.06f;
            dl.Box(Hero.Pose.Joint(hand) + d * (w.Def.Size.Z * 0.3f - recoil), w.Def.Size, gunRot, w.Def.Color);
        }
    }

    /// <summary>The way the gun points: along the aim when aiming, else along the forearm.</summary>
    public Vector3 GunDir()
    {
        var p = Player;
        if ((aimHeld || p.FacingLockT > 0 || p.Hovering) && ReloadT <= 0 && PunchT <= 0) return MathX.Forward(CamYaw, CamPitch);
        int hand = Hero.Sk.IndexOf("hand.R"), elbow = Hero.Sk.IndexOf("elbow.R");
        var d = hand >= 0 && elbow >= 0 ? Hero.Pose.Joint(hand) - Hero.Pose.Joint(elbow) : Vector3.Zero;
        return d.LengthSquared() > 1e-6f ? Vector3.Normalize(d) : MathX.Forward(p.Yaw, 0);
    }

    void DrawGerm(DrawList dl, Germ gm)
    {
        gm.View.Draw(dl, gm.HitFlash > 0, gm.Alive ? 1 : 0.55f);
        // health bar while hurt and standing
        if (gm.Standing && gm.Hp < gm.MaxHp)
        {
            var barC = gm.Pos + new Vector3(0, gm.Height + 0.35f, 0);
            float f = MathX.Clamp01(gm.Hp / gm.MaxHp), bw = 0.8f + 0.2f * gm.Scale;
            var camRight = MathX.Right(CamYaw); var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, CamYaw);
            dl.Box(barC, new Vector3(bw, 0.1f, 0.02f), rot, MathX.Rgb(0x202020), DrawFlags.Unlit);
            dl.Box(barC - camRight * (0.5f * bw * (1 - f)) - CamForward * 0.02f, new Vector3(bw * f, 0.1f, 0.03f), rot, MathX.Rgb(0xE05050), DrawFlags.Unlit);
        }
        if (gm.Standing) BlobShadow(dl, gm.Pos + new Vector3(0, 0.3f, 0), gm.Radius * 1.3f);
    }
}

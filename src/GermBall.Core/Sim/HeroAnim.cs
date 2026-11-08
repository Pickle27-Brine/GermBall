using System;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;

namespace GermBall.Sim;

/// <summary>Body proportions for the procedural motion. Measured from an entity's rig (<see cref="Skeleton.Rig"/>), or the
/// default heroic build: broad shallow chest, narrow waist, long legs, small head.</summary>
public readonly struct Rig
{
    public readonly float S, PelvisH, Torso, Neck, HeadUp, ShoulderW, ShoulderDrop, HipW, HipDrop, Thigh, Shin, Ankle, Upper, Fore;
    /// <summary>Pelvis to abdomen (0: one-piece torso).</summary>
    public readonly float Abdomen;
    public Rig(float s)
        : this(0.98f * s, 0.40f * s, 0.13f * s, 0.16f * s, 0.25f * s, 0.02f * s, 0.11f * s, 0.04f * s, 0.47f * s, 0.46f * s, 0.29f * s, 0.28f * s) { }

    public Rig(float pelvisH, float torso, float neck, float headUp, float shoulderW, float shoulderDrop, float hipW, float hipDrop, float thigh, float shin, float upper, float fore, float abdomen = 0)
    {
        Abdomen = abdomen;
        S = MathF.Max(0.05f, pelvisH / 0.98f);
        PelvisH = pelvisH; Torso = torso; Neck = neck; HeadUp = headUp; ShoulderW = shoulderW; ShoulderDrop = shoulderDrop;
        HipW = hipW; HipDrop = hipDrop; Thigh = thigh; Shin = shin; Upper = upper; Fore = fore;
        // the feet rest where the legs reach with a slight knee bend
        Ankle = MathF.Max(0, pelvisH - hipDrop - (thigh + shin) * 0.97f);
    }

    /// <summary>A relaxed standing pose with a sway (enemies use this).</summary>
    public void Standing(Pose o, Vector3 feet, float yaw, float t, float sway, float armsUp = 0)
    {
        var up = Vector3.UnitY; var fwd = MathX.Forward(yaw, 0); var right = MathX.Right(yaw);
        float breathe = MathF.Sin(t * 2.1f) * 0.012f * S;
        var torsoDir = Vector3.Normalize(up + right * MathF.Sin(t * 1.3f) * sway + fwd * MathF.Sin(t * 0.9f) * sway * 0.5f);
        Spine(o, feet + up * (PelvisH - 0.01f * S), torsoDir, fwd, right, breathe);
        for (int side = 0; side < 2; side++)
        {
            float sg = side == 0 ? -1 : 1;
            var hip = o[side == 0 ? J.HipL : J.HipR];
            var foot = feet + right * (sg * (HipW + 0.02f * S)) + fwd * 0.02f * S + up * Ankle;
            Leg(o, side, hip, foot, fwd);
            var sh = o[side == 0 ? J.ShL : J.ShR];
            var hand = sh + (-up * (Upper + Fore - 0.04f * S)) + right * sg * 0.07f * S;
            if (armsUp > 0) hand = Vector3.Lerp(hand, sh + fwd * 0.35f * S + up * 0.15f * S + right * sg * 0.15f * S, armsUp);
            Arm(o, side, sh, hand, -fwd + right * sg * 0.3f);
        }
    }

    public void Spine(Pose o, Vector3 pelvis, Vector3 torsoDir, Vector3 fwd, Vector3 right, float breathe = 0)
    {
        o[J.Pelvis] = pelvis;
        if (Abdomen > 0)
        {
            // two segments: the abdomen bends half as much as the chest
            var low = Vector3.Normalize(Vector3.UnitY + torsoDir);
            o[J.Abdomen] = pelvis + low * Abdomen;
            o[J.Chest] = o[J.Abdomen] + torsoDir * (Torso - Abdomen + breathe);
        }
        else
        {
            o[J.Chest] = pelvis + torsoDir * (Torso + breathe);
            o[J.Abdomen] = (pelvis + o[J.Chest]) * 0.5f;
        }
        o[J.Neck] = o[J.Chest] + torsoDir * Neck;
        o[J.Head] = o[J.Neck] + torsoDir * HeadUp;
        var shBase = o[J.Chest] - torsoDir * ShoulderDrop;
        o[J.ShL] = shBase - right * ShoulderW; o[J.ShR] = shBase + right * ShoulderW;
        o[J.HipL] = pelvis - right * HipW - Vector3.UnitY * HipDrop; o[J.HipR] = pelvis + right * HipW - Vector3.UnitY * HipDrop;
    }

    public void Leg(Pose o, int side, Vector3 hip, Vector3 foot, Vector3 kneeDir)
    {
        var (knee, ft) = Skel.TwoBone(hip, foot, Thigh, Shin, kneeDir);
        o[side == 0 ? J.KnL : J.KnR] = knee; o[side == 0 ? J.FtL : J.FtR] = ft;
    }

    public void Arm(Pose o, int side, Vector3 shoulder, Vector3 hand, Vector3 elbowDir)
    {
        var (el, ha) = Skel.TwoBone(shoulder, hand, Upper, Fore, elbowDir);
        o[side == 0 ? J.ElL : J.ElR] = el; o[side == 0 ? J.HaL : J.HaR] = ha;
    }
}

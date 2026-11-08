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

/// <summary>
/// The hero's procedural motion (the "procedural" layer for player entities), in model space: a run cycle whose stride
/// matches the body's speed (so planted feet don't slide), per-foot floor finding, arm swing, lean, skids, ledge tuck,
/// hover, the gun arm on the aim, punches and reloads.
/// </summary>
public static class HeroMotion
{
    /// <summary>Strides per second for a speed: slow walks stride short, fast runs stride long (up to 1.2 m).</summary>
    public static float Cadence(float speed) => Math.Clamp(speed / 2.4f, 0.9f, 4f);
    public static float StrideFor(float speed) => MathF.Min(speed / (2 * Cadence(speed)), 1.2f);

    public static void Build(Pose o, Rig R, Animator a, AnimContext c, Matrix4x4 root, Matrix4x4 toModel)
    {
        var up = Vector3.UnitY; var fwd = -Vector3.UnitZ; var right = Vector3.UnitX;
        Vector3 ToModelDir(Vector3 d) { var v = Vector3.TransformNormal(d, toModel); v.Y = d.Y == 0 ? 0 : v.Y; return v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : fwd; }
        float scale = MathF.Max(1e-3f, new Vector3(root.M11, root.M12, root.M13).Length());
        float hs = c.Speed / scale;   // model-space speed, so strides stay planted at any size
        var moveDir = hs > 0.3f ? ToModelDir(MathX.Flat(c.MoveDir)) : fwd;
        float run = a.Run, lean = a.Lean, t = c.Time;

        // ---- spine: bob with the stride, lean into speed (or back when skidding)
        float cyc = a.Phase * MathF.Tau;
        float bob = run * 0.05f * MathF.Abs(MathF.Cos(cyc));
        var torsoDir = Vector3.Normalize(up * MathF.Cos(lean) + fwd * MathF.Sin(lean));
        float crouch = c.Grounded ? 0.04f * run : 0;
        R.Spine(o, up * (R.PelvisH - bob - crouch), torsoDir, fwd, right, MathF.Sin(t * 2.1f) * 0.01f * (1 - run));

        // ---- legs
        for (int side = 0; side < 2; side++)
        {
            float sg = side == 0 ? -1 : 1;
            var hip = o[side == 0 ? J.HipL : J.HipR];
            Vector3 foot;
            if (c.Grounded)
            {
                float stride = StrideFor(hs) * MathX.Clamp01(hs / 0.3f);
                float ph = a.Phase + side * 0.5f; ph -= MathF.Floor(ph);
                float off, lift;
                if (ph < 0.5f) { off = stride / 2 - stride * (ph / 0.5f); lift = 0; }   // planted: slides back under the body at its speed
                else
                {
                    float u = (ph - 0.5f) / 0.5f, s = u * u * (3 - 2 * u);
                    off = -stride / 2 + stride * s; lift = MathF.Sin(MathF.PI * u) * (0.08f + 0.22f * run);
                }
                if (c.Skidding && side == 1) off = 0.45f;
                foot = moveDir * off + right * (sg * (R.HipW + 0.01f)) + up * (R.Ankle + lift);
                // each foot finds its own floor (stairs, slopes)
                if (c.GroundAt != null)
                {
                    var wp = Vector3.Transform(foot with { Y = 0 }, root);
                    float gy = c.GroundAt(wp);
                    if (!float.IsNaN(gy)) foot.Y = Vector3.Transform(new Vector3(wp.X, gy, wp.Z), toModel).Y + R.Ankle + lift;
                }
            }
            else if (c.Hovering) foot = hip - up * (R.Thigh + R.Shin - 0.08f) + fwd * (sg * 0.05f) + right * sg * 0.04f;
            else foot = hip - up * (R.Thigh + R.Shin - 0.23f) + fwd * (side == 0 ? 0.25f : -0.2f) + up * (side == 0 ? 0.2f : 0);
            R.Leg(o, side, hip, foot, fwd);
        }

        // ---- arms
        bool aiming = c.Armed && c.Aiming && c.ReloadT <= 0;
        var aimDir = ToModelDir(c.AimDir);
        var shL = o[J.ShL]; var shR = o[J.ShR];
        float swing = MathF.Sin(cyc) * (0.25f + 0.55f * run);
        float reach = R.Upper + R.Fore;
        Vector3 Swing(Vector3 sh, float ang, float sg) => sh + (-up * MathF.Cos(ang) + moveDir * MathF.Sin(ang)) * (reach - 0.06f - 0.15f * run) + right * sg * 0.06f;
        var handL = c.Grounded ? Swing(shL, swing, -1) : shL - up * 0.35f - right * 0.3f + fwd * 0.1f;
        var handR = c.Grounded ? Swing(shR, -swing, 1) : shR - up * 0.35f + right * 0.3f + fwd * 0.1f;

        if (c.PunchT > 0)
        {
            float e = MathF.Sin(MathF.PI * (1 - c.PunchT / 0.2f));
            var flat = MathX.Flat(aimDir); flat = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : fwd;
            handR = shR + flat * (0.2f + 0.4f * e) - right * 0.12f * e;
        }
        else if (c.ReloadT > 0 && c.Armed)
        {
            float wob = MathF.Sin(t * 14) * 0.03f;
            handR = o[J.Chest] + fwd * 0.32f + right * 0.08f - up * 0.08f;
            handL = o[J.Chest] + fwd * 0.30f - right * 0.06f - up * (0.12f + wob);
        }
        else if (aiming)
        {
            handR = shR + aimDir * 0.48f - right * 0.1f + up * 0.03f;
            if (c.TwoHanded) handL = handR + aimDir * 0.26f - right * 0.02f;
        }
        else if (c.Armed) handR = Vector3.Lerp(handR, shR - up * 0.42f + fwd * 0.22f, 0.6f);   // low ready

        R.Arm(o, 0, shL, handL, -fwd * 0.6f - right * 0.6f - up * 0.4f);
        R.Arm(o, 1, shR, handR, -fwd * 0.6f + right * 0.6f - up * 0.4f);
    }
}

using System;
using System.Numerics;
using GermBall.Engine;

namespace GermBall.Sim;

/// <summary>Body proportions for a humanoid of a given scale (1 = the hero, 1.8 m). A heroic build: broad shallow chest,
/// narrow waist, long legs, small head.</summary>
public readonly struct Rig
{
    public readonly float S, PelvisH, Torso, Neck, HeadUp, ShoulderW, HipW, Thigh, Shin, Ankle, Upper, Fore;
    public Rig(float s)
    {
        S = s; PelvisH = 0.98f * s; Torso = 0.40f * s; Neck = 0.13f * s; HeadUp = 0.16f * s; ShoulderW = 0.25f * s; HipW = 0.11f * s;
        Thigh = 0.47f * s; Shin = 0.46f * s; Ankle = 0.07f * s; Upper = 0.29f * s; Fore = 0.28f * s;
    }

    /// <summary>A relaxed standing pose (used by the germs, and as the hero's base).</summary>
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
            var foot = feet + right * (sg * 0.13f * S) + fwd * 0.02f * S + up * Ankle;
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
        o[J.Chest] = pelvis + torsoDir * (Torso + breathe);
        o[J.Neck] = o[J.Chest] + torsoDir * Neck;
        o[J.Head] = o[J.Neck] + torsoDir * HeadUp;
        var shBase = o[J.Chest] - torsoDir * 0.02f * S;
        o[J.ShL] = shBase - right * ShoulderW; o[J.ShR] = shBase + right * ShoulderW;
        o[J.HipL] = pelvis - right * HipW - Vector3.UnitY * 0.04f * S; o[J.HipR] = pelvis + right * HipW - Vector3.UnitY * 0.04f * S;
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
/// The hero's procedural animation: a run cycle whose stride matches the body's speed (so planted feet don't slide),
/// arm swing, lean, skids, jumps, hover, aiming along the camera, punches and reloads. Rolling is drawn as the ball instead.
/// </summary>
public sealed class HeroAnim
{
    public readonly Rig Rig = new(1);
    public float Phase;     // run cycle, in cycles
    float lean, run;

    /// <summary>Advances the run cycle (fixed step).</summary>
    public void Update(Player p, float dt)
    {
        float hs = MathX.FlatLen(p.Vel);
        if (p.Grounded && !p.Rolling && hs > 0.3f)
        {
            Phase += Cadence(hs) * dt;
            if (Phase > 1000) Phase -= 1000;
        }
        float targetRun = p.Grounded && !p.Rolling ? MathX.Clamp01(hs / Player.TopSpeed) : 0;
        run += (targetRun - run) * MathF.Min(1, dt * 10);
        float targetLean = p.Skidding ? -0.3f : run * 0.3f;
        lean += (targetLean - lean) * MathF.Min(1, dt * 8);
    }

    /// <summary>Strides per second for a speed: slow walks stride short, fast runs stride long (up to 1.2 m).</summary>
    public static float Cadence(float speed) => Math.Clamp(speed / 2.4f, 0.9f, 4f);
    public static float StrideFor(float speed) => MathF.Min(speed / (2 * Cadence(speed)), 1.2f);

    public void Build(Pose o, Game g, Vector3 feet, float yaw)
    {
        var p = g.Player; var R = Rig;
        var up = Vector3.UnitY; var fwd = MathX.Forward(yaw, 0); var right = MathX.Right(yaw);
        float hs = MathX.FlatLen(p.Vel);
        var moveDir = hs > 0.3f ? MathX.Flat(p.Vel) / hs : fwd;
        float t = g.Time;

        // ---- spine: bob with the stride, lean into speed (or back when skidding)
        float cyc = Phase * MathF.Tau;
        float bob = run * 0.05f * MathF.Abs(MathF.Cos(cyc));
        var torsoDir = Vector3.Normalize(up * MathF.Cos(lean) + fwd * MathF.Sin(lean));
        float crouch = p.Grounded ? 0.04f * run : 0;
        R.Spine(o, feet + up * (R.PelvisH - bob - crouch), torsoDir, fwd, right, MathF.Sin(t * 2.1f) * 0.01f * (1 - run));

        // ---- legs
        for (int side = 0; side < 2; side++)
        {
            float sg = side == 0 ? -1 : 1;
            var hip = o[side == 0 ? J.HipL : J.HipR];
            Vector3 foot;
            if (p.Grounded)
            {
                float stride = StrideFor(hs) * MathX.Clamp01(hs / 0.3f);
                float ph = Phase + side * 0.5f; ph -= MathF.Floor(ph);
                float off, lift;
                if (ph < 0.5f) { off = stride / 2 - stride * (ph / 0.5f); lift = 0; }   // planted: slides back under the body at its speed
                else
                {
                    float u = (ph - 0.5f) / 0.5f, s = u * u * (3 - 2 * u);
                    off = -stride / 2 + stride * s; lift = MathF.Sin(MathF.PI * u) * (0.08f + 0.22f * run);
                }
                if (p.Skidding && side == 1) off = 0.45f;
                foot = feet + moveDir * off + right * (sg * 0.12f) + up * (R.Ankle + lift);
                // each foot finds its own floor (stairs, slopes)
                var hit = g.Level.World.Raycast(foot + up * 0.6f, -up, 1.2f);
                if (hit.Hit && hit.Normal.Y > 0.5f) foot.Y = hit.Point.Y + R.Ankle + lift;
            }
            else if (p.Hovering)
                foot = hip - up * 0.85f + fwd * (sg * 0.05f) + right * sg * 0.04f;
            else
            {
                // a jump without curling (running off a ledge): one knee up, one leg trailing
                foot = hip - up * 0.7f + fwd * (side == 0 ? 0.25f : -0.2f) + up * (side == 0 ? 0.2f : 0);
            }
            R.Leg(o, side, hip, foot, fwd);
        }

        // ---- arms
        var w = g.Weapon;
        bool armed = w != null && p.Alive;
        bool aiming = armed && (g.AimHeld || p.FacingLockT > 0 || p.Hovering) && g.ReloadT <= 0;
        var aimDir = MathX.Forward(g.CamYaw, g.CamPitch);
        bool twoHanded = w != null && w.Def != WeaponDef.Pistol;
        var shL = o[J.ShL]; var shR = o[J.ShR];
        float swing = MathF.Sin(cyc) * (0.25f + 0.55f * run);

        Vector3 handL, handR;
        Vector3 Swing(Vector3 sh, float a, float sg) =>
            sh + (-up * MathF.Cos(a) + moveDir * MathF.Sin(a)) * (R.Upper + R.Fore - 0.06f - 0.15f * run) + right * sg * 0.06f;
        handL = p.Grounded ? Swing(shL, swing, -1) : shL - up * 0.35f - right * 0.3f + fwd * 0.1f;
        handR = p.Grounded ? Swing(shR, -swing, 1) : shR - up * 0.35f + right * 0.3f + fwd * 0.1f;

        if (g.PunchT > 0)
        {
            float e = MathF.Sin(MathF.PI * (1 - g.PunchT / 0.2f));
            handR = shR + MathX.Flat(aimDir) * (0.2f + 0.4f * e) - right * 0.12f * e;
        }
        else if (g.ReloadT > 0 && armed)
        {
            float wob = MathF.Sin(t * 14) * 0.03f;
            handR = o[J.Chest] + fwd * 0.32f + right * 0.08f - up * 0.08f;
            handL = o[J.Chest] + fwd * 0.30f - right * 0.06f - up * (0.12f + wob);
        }
        else if (aiming)
        {
            handR = shR + aimDir * 0.48f - right * 0.1f + up * 0.03f;
            if (twoHanded) handL = handR + aimDir * 0.26f - right * 0.02f;
        }
        else if (armed)
            handR = Vector3.Lerp(handR, shR - up * 0.42f + fwd * 0.22f, 0.6f);   // low ready

        R.Arm(o, 0, shL, handL, -fwd * 0.6f - right * 0.6f - up * 0.4f);
        R.Arm(o, 1, shR, handR, -fwd * 0.6f + right * 0.6f - up * 0.4f);
    }

    /// <summary>The way the gun points: along the aim when aiming, else along the forearm.</summary>
    public static Vector3 GunDir(Pose o, Game g)
    {
        var p = g.Player;
        if ((g.AimHeld || p.FacingLockT > 0 || p.Hovering) && g.ReloadT <= 0 && g.PunchT <= 0) return MathX.Forward(g.CamYaw, g.CamPitch);
        var d = o[J.HaR] - o[J.ElR];
        return d.LengthSquared() > 1e-6f ? Vector3.Normalize(d) : MathX.Forward(p.Yaw, 0);
    }
}

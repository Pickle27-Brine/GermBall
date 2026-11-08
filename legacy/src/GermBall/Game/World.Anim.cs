using System;
using System.Collections.Generic;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>
/// Character and weapon animation styles layered on the procedural pose:
/// - each hero moves its own way (the gelcap's heroic run, the tablet's smooth glide, the bottle's heavy stomp, the syringe's
///   quick light steps) and idles its own way (hands on hips, arms folded, fists clenched, bouncing on its toes);
/// - every weapon class has its own hold, recoil and follow-through: one-handed pistols that kick at the wrist, shouldered
///   rifles, a shotgun you pump, a scoped rifle you work the bolt on, a gatling held at the hip, launchers on the shoulder,
///   a bow you draw, beams you brace; reloads drop the off hand to the belt for a fresh magazine.
/// </summary>
public sealed partial class World
{
    public sealed record HeroStyle(float Stride, float Bob, float Lean, float Swing, float Stance, float Lift, float Bounce, string Idle);
    static readonly Dictionary<string, HeroStyle> HERO_STYLE = new()
    {
        ["gelcap"] = new(1.05f, 1.0f, -0.04f, 1.25f, 1.05f, 1.15f, 0.025f, "hips"),
        ["tablet"] = new(0.95f, 0.35f, 0.06f, 0.6f, 0.95f, 0.8f, 0.0f, "cross"),
        ["bottle"] = new(1.15f, 1.7f, 0.1f, 1.1f, 1.3f, 0.9f, 0.0f, "fists"),
        ["syringe"] = new(0.85f, 0.8f, 0.14f, 0.9f, 0.85f, 1.5f, 0.03f, "bounce"),
        ["caplet"] = new(1.0f, 0.7f, 0.04f, 1.0f, 1.0f, 1.0f, 0.015f, "hips"),
        ["lozenge"] = new(1.05f, 1.3f, 0.05f, 1.1f, 1.2f, 0.9f, 0.0f, "fists"),
        ["gummy"] = new(0.9f, 1.6f, 0.0f, 1.3f, 1.0f, 1.3f, 0.05f, "bounce"),
        ["medmech"] = new(1.2f, 1.5f, 0.08f, 0.9f, 1.35f, 0.8f, 0.0f, "fists"),
    };
    /// <summary>The style for an actor, or null for the germs (they keep their menacing stalk).</summary>
    HeroStyle StyleOf(Actor A) => MED.TryGetValue(A.type, out var m) && HERO_STYLE.TryGetValue(m.Key == "custom" ? (CFG.Custom?.Preset ?? "gelcap") : m.Key, out var hs) ? hs : null;

    /// <summary>How a weapon is held: onehand, rifle, shotgun, scoped, heavy, launcher, bow, beam, melee.</summary>
    public static string GripOf(WeaponDef d) => d.Id switch
    {
        "pistol" or "revolver" or "darts" or "knives" or "laserpistol" or "plasma" => "onehand",
        "smg" or "rifle" or "needler" => "rifle",
        "shotgun" => "shotgun",
        "sniper" or "hunting" => "scoped",
        "hmg" => "heavy",
        "rocket" or "chargecannon" or "mechbomb" or "mechhoming" => "launcher",
        "bow" => "bow",
        "armcannon" => "beam",
        _ => d.Type == "melee" ? "melee" : "rifle",
    };
    static float RecoilOf(string grip, WeaponDef d) => grip switch
    {
        "onehand" => d.Id == "revolver" ? 0.5f : 0.22f, "rifle" => 0.12f, "shotgun" => 0.6f, "scoped" => 0.7f, "heavy" => 0.08f, "launcher" => 0.85f, "beam" => 0.04f, _ => 0.1f
    };

    /// <summary>
    /// Hand targets for holding the current weapon: right hand (rx, ry, rz) and left hand (lx, ly, lz), plus how strongly
    /// each should override the free arm (0..1). aim = how far the actor is aiming; low ready otherwise.
    /// </summary>
    void GunHands(Actor A, float[] shR, float[] shL, float[] aim, float[] up, float[] cr, float[] cf, float s, float L, float aimK,
        out float[] Rh, out float[] Lh, out float kR, out float kL)
    {
        Rh = new float[3]; Lh = new float[3]; kR = kL = 0;
        var w = CurWpn(A); if (w == null) return;
        var d = WPN[w.id]; string grip = GripOf(d); if (grip == "melee") return;
        var W2 = A.W; float since = W2.since, rec = MathF.Exp(-since * 16) * RecoilOf(grip, d);
        if (grip == "heavy" && W2.since < 0.12f) rec = (0.06f + 0.03f * MathF.Sin(clockT * 70)) * (A.IN.fire ? 1 : 0);
        void Set(float[] o, float[] b, float af, float uf, float rf, float ff) { for (int k = 0; k < 3; k++) o[k] = b[k] + aim[k] * af * L + up[k] * uf * s + cr[k] * rf * s + cf[k] * ff * s; }
        float ready = 1 - aimK;   // low ready: the gun held across the body, muzzle down
        switch (grip)
        {
            case "onehand":
                // arm out straight at the target; low ready: pointed at the ground beside you
                Set(Rh, shR, 0.97f * aimK + 0.55f * ready, -0.38f * ready, 0.05f * ready, 0.15f * ready);
                kR = 1; kL = 0;   // the other hand stays free (it swings, or balances)
                break;
            case "rifle": case "shotgun": case "scoped":
                {
                    float tuck = grip == "scoped" ? 0.5f : 0.62f;   // the stock pulled into the shoulder, the off hand out on the barrel
                    Set(Rh, shR, tuck * aimK + 0.35f * ready, 0.04f * aimK - 0.22f * ready, -0.04f * aimK - 0.1f * ready, 0.25f * ready);
                    float grip2 = grip == "scoped" ? 0.42f : 0.34f;
                    for (int k = 0; k < 3; k++) Lh[k] = Rh[k] + aim[k] * grip2 * s * aimK + (cf[k] * 0.28f - cr[k] * 0.12f + up[k] * 0.1f) * s * ready - up[k] * 0.03f * s;
                    if (grip == "shotgun" && (since < 0.5f || W2.pumpT > 0))
                    {   // rack the pump: the off hand slides back along the barrel and forward again (after firing, and after loading shells)
                        float pt = W2.pumpT > 0 ? 0.48f - W2.pumpT : since;
                        float u = MathF.Sin(Clamp((pt - 0.08f) / 0.4f, 0, 1) * PI);
                        for (int k = 0; k < 3; k++) Lh[k] -= aim[k] * 0.16f * s * u;
                    }
                    if (grip == "scoped" && since < 0.75f)
                    {   // work the bolt: the trigger hand comes up and back, then home
                        float u = MathF.Sin(Clamp((since - 0.15f) / 0.55f, 0, 1) * PI);
                        for (int k = 0; k < 3; k++) Rh[k] += (up[k] * 0.07f + cr[k] * 0.05f) * s * u - aim[k] * 0.06f * s * u;
                    }
                    kR = kL = 1; break;
                }
            case "heavy":
                // a gatling held low at the hip in both hands, braced against the body
                Set(Rh, shR, 0.25f, -0.28f, 0.02f, 0.3f);
                for (int k = 0; k < 3; k++) Lh[k] = Rh[k] + aim[k] * 0.36f * s - cr[k] * 0.16f * s + up[k] * 0.03f * s;
                kR = kL = 1; break;
            case "launcher":
                // up on the right shoulder; both hands underneath it
                Set(Rh, shR, 0.2f * aimK + 0.12f * ready, 0.02f, 0.02f, 0.05f);
                for (int k = 0; k < 3; k++) Lh[k] = Rh[k] + aim[k] * 0.32f * s - up[k] * 0.12f * s - cr[k] * 0.08f * s;
                kR = kL = 1; break;
            case "bow":
                {   // the left arm holds the bow out at the target; the right hand draws the string back toward the cheek
                    for (int k = 0; k < 3; k++) Lh[k] = shL[k] + aim[k] * 0.96f * L * aimK + (aim[k] * 0.5f * L - up[k] * 0.3f * s) * ready;
                    float draw = Clamp(W2.charge, 0, 1);
                    for (int k = 0; k < 3; k++) Rh[k] = Lh[k] - aim[k] * (0.12f + 0.62f * draw) * L * aimK + cr[k] * 0.04f * s + (cf[k] * 0.05f) * s * ready;
                    kR = kL = 1; break;
                }
            case "beam":
                {   // both hands forward bracing the emitter; it shakes while it fires, and pulls back as it charges
                    Set(Rh, shR, 0.8f * aimK + 0.45f * ready, -0.25f * ready, 0, 0.1f);
                    float jit = W2.beam != null ? 0.012f : 0;
                    for (int k = 0; k < 3; k++) { Rh[k] += (R() - 0.5f) * jit * s - aim[k] * 0.1f * W2.charge * L; Lh[k] = Rh[k] + aim[k] * 0.14f * s - cr[k] * 0.07f * s - up[k] * 0.04f * s; }
                    kR = kL = 1; break;
                }
        }
        // recoil: the muzzle kicks back and climbs
        for (int k = 0; k < 3; k++) { float dk = (-aim[k] * 0.22f + up[k] * 0.14f) * rec * s; Rh[k] += dk; if (grip != "onehand") Lh[k] += dk * 0.8f; }
        // drawing a weapon just switched to: it comes up from low across the body
        if (W2.switchT > 0) { float k = W2.switchT / 0.32f; k = k * k * (3 - 2 * k); for (int c = 0; c < 3; c++) { float dk = (-up[c] * 0.32f + cf[c] * 0.12f - aim[c] * 0.15f) * s * k; Rh[c] += dk; Lh[c] += dk; } }
        // reloading: every kind of gun has its own
        if (W2.reload > 0 && W2.reloadMax > 0 && grip != "bow" && grip != "beam")
        {
            float u = Clamp(1 - W2.reload / W2.reloadMax, 0, 1);
            ReloadHands(ReloadKind(d), u, Rh, Lh, shL, shR, aim, up, cr, cf, s, ref kL);
        }
    }

    /// <summary>How a weapon is reloaded.</summary>
    static string ReloadKind(WeaponDef d) => d.Id switch
    {
        "revolver" => "cylinder", "darts" or "knives" => "pouch", "shotgun" => "shell", "sniper" or "hunting" => "bolt", "hmg" => "belt",
        "rocket" or "chargecannon" or "mechbomb" or "mechhoming" => "tube", "smg" or "rifle" or "needler" => "rifle",
        _ => GripOf(d) == "onehand" ? "mag" : "rifle",
    };

    // a hand path through keyframes (time, point), eased between them
    static void Path(float u, float[] o, params (float t, float[] p)[] keys)
    {
        if (u <= keys[0].t) { Array.Copy(keys[0].p, o, 3); return; }
        for (int i = 1; i < keys.Length; i++)
            if (u <= keys[i].t)
            {
                float k = (u - keys[i - 1].t) / MathF.Max(1e-4f, keys[i].t - keys[i - 1].t); k = k * k * (3 - 2 * k);
                for (int c = 0; c < 3; c++) o[c] = keys[i - 1].p[c] + (keys[i].p[c] - keys[i - 1].p[c]) * k;
                return;
            }
        Array.Copy(keys[^1].p, o, 3);
    }

    /// <summary>
    /// The reload, per kind of gun: a pistol drops its magazine and takes a fresh one from the belt; a revolver tips up while
    /// rounds are thumbed in; a rifle swaps a magazine from the chest rig and racks the handle; a shotgun is fed one shell at a
    /// time from the belt (each cycle of u is one shell); a scoped rifle opens its bolt and thumbs in rounds; the heavy gun
    /// takes a new belt box from the back of the hip; a launcher comes off the shoulder for a rocket from the back.
    /// </summary>
    void ReloadHands(string kind, float u, float[] Rh, float[] Lh, float[] shL, float[] shR, float[] aim, float[] up, float[] cr, float[] cf, float s, ref float kL)
    {
        float[] At(float[] b, float a, float uu, float rr, float ff) { var o = new float[3]; for (int c = 0; c < 3; c++) o[c] = b[c] + aim[c] * a * s + up[c] * uu * s + cr[c] * rr * s + cf[c] * ff * s; return o; }
        float bump = MathF.Sin(u * PI);
        var R0 = (float[])Rh.Clone();
        float[] belt = At(shL, 0, -0.55f, 0.02f, 0.12f), chest = At(shL, 0, -0.28f, 0.14f, 0.26f), backHip = At(shL, 0, -0.45f, 0.05f, -0.25f), overShoulder = At(shL, 0, 0.14f, 0.05f, -0.22f);
        var tmp = new float[3];
        switch (kind)
        {
            case "mag":
                {   // the gun tips up a little; the old magazine drops, a fresh one comes up from the belt, a slap seats it
                    for (int c = 0; c < 3; c++) Rh[c] = R0[c] + (up[c] * 0.06f - cr[c] * 0.04f) * s * bump;
                    var well = At(Rh, 0.04f, -0.1f, 0, 0);
                    Path(u, Lh, (0, well), (0.25f, belt), (0.45f, belt), (0.72f, At(well, 0, -0.06f, 0, 0)), (0.8f, At(well, 0, 0.03f, 0, 0)), (0.9f, well));
                    kL = u < 0.88f ? 1 : MathF.Max(0, (1 - u) / 0.12f);
                    break;
                }
            case "cylinder":
                {   // the gun swings up and in, rounds are thumbed in one by one, then a flick of the wrist closes it
                    float lift = u < 0.12f ? u / 0.12f : u < 0.88f ? 1 : MathF.Max(0, (1 - u) / 0.12f);
                    for (int c = 0; c < 3; c++) Rh[c] = R0[c] + (up[c] * 0.18f - cr[c] * 0.14f - aim[c] * 0.25f) * s * lift;
                    var cyl = At(Rh, 0.06f, 0.02f, -0.06f, 0);
                    Path(u, Lh, (0, cyl), (0.18f, cyl), (0.32f, belt), (0.45f, cyl), (0.9f, cyl));
                    if (u > 0.45f && u < 0.85f) { float th = MathF.Abs(MathF.Sin((u - 0.45f) / 0.4f * PI * 3)); for (int c = 0; c < 3; c++) Lh[c] += aim[c] * 0.04f * s * th; }
                    kL = u < 0.88f ? 1 : MathF.Max(0, (1 - u) / 0.12f);
                    break;
                }
            case "pouch":
                Path(u, Lh, (0, At(Rh, 0, -0.1f, -0.1f, 0)), (0.4f, belt), (0.75f, At(Rh, 0.02f, 0, -0.04f, 0)), (1, At(Rh, 0, -0.1f, -0.1f, 0)));
                kL = u < 0.85f ? 1 : MathF.Max(0, (1 - u) / 0.15f);
                break;
            case "rifle":
                {   // magazine out and down, a fresh one from the chest rig, seated, then the charging handle racked
                    for (int c = 0; c < 3; c++) Rh[c] = R0[c] + (up[c] * 0.04f - cr[c] * 0.05f) * s * bump;
                    var well = At(Rh, 0.18f, -0.12f, 0, 0); var handle = At(Rh, 0.06f, 0.07f, 0.07f, 0);
                    Path(u, Lh, (0, At(Rh, 0.34f, 0, 0, 0)), (0.1f, well), (0.2f, At(well, 0, -0.25f, 0, 0)), (0.36f, chest), (0.52f, At(well, 0, -0.05f, 0, 0)), (0.6f, At(well, 0, 0.03f, 0, 0)),
                        (0.72f, handle), (0.8f, At(handle, -0.1f, 0, 0, 0)), (0.86f, handle), (1, At(Rh, 0.34f, 0, 0, 0)));
                    kL = 1; break;
                }
            case "shell":
                {   // canted, the off hand fetches a shell from the belt and pushes it up into the loading port; u is one shell
                    for (int c = 0; c < 3; c++) Rh[c] = R0[c] + (-up[c] * 0.08f - cr[c] * 0.06f) * s;
                    var port = At(Rh, 0.18f, -0.1f, 0, 0); var pouch = At(belt, 0, 0.05f, 0.05f, 0.1f);
                    Path(u, Lh, (0, port), (0.35f, pouch), (0.45f, pouch), (0.8f, port), (0.9f, At(port, 0.06f, 0.02f, 0, 0)), (1, port));
                    kL = 1; break;
                }
            case "bolt":
                {   // bolt up and back, rounds thumbed in from a pouch, bolt home
                    float boltOpen = u < 0.15f ? u / 0.15f : u < 0.82f ? 1 : MathF.Max(0, (1 - u) / 0.18f);
                    var boltPos = At(R0, 0.02f, 0.08f, 0.07f, 0);
                    for (int c = 0; c < 3; c++) Rh[c] = Lerp(R0[c], boltPos[c] - aim[c] * 0.07f * s, boltOpen);
                    var top = At(R0, 0.1f, 0.08f, 0, 0);
                    Path(u, Lh, (0, At(R0, 0.42f, 0, 0, 0)), (0.18f, At(R0, 0.42f, 0, 0, 0)), (0.32f, chest), (0.42f, top), (0.72f, top), (0.85f, At(R0, 0.42f, 0, 0, 0)));
                    if (u > 0.42f && u < 0.72f) { float th = MathF.Abs(MathF.Sin((u - 0.42f) / 0.3f * PI * 2)); for (int c = 0; c < 3; c++) Lh[c] -= up[c] * 0.04f * s * th; }
                    kL = 1; break;
                }
            case "belt":
                {   // the big gun sags to the hip; the off hand hauls a belt box from the back of the hip and clips it on
                    for (int c = 0; c < 3; c++) Rh[c] = R0[c] - up[c] * 0.14f * s * bump;
                    var side = At(Rh, 0.18f, 0.04f, -0.14f, 0);
                    Path(u, Lh, (0, At(Rh, 0.36f, 0.03f, -0.16f, 0)), (0.25f, backHip), (0.4f, backHip), (0.62f, side), (0.78f, At(side, 0, -0.04f, 0, 0)), (1, At(Rh, 0.36f, 0.03f, -0.16f, 0)));
                    kL = 1; break;
                }
            case "tube":
                {   // off the shoulder to the hip, a rocket from over the shoulder fed into the back of the tube, back up
                    float down = u < 0.15f ? u / 0.15f : u < 0.85f ? 1 : MathF.Max(0, (1 - u) / 0.15f);
                    for (int c = 0; c < 3; c++) Rh[c] = R0[c] + (-up[c] * 0.3f + cf[c] * 0.12f) * s * down;
                    var rear = At(Rh, -0.18f, 0.02f, 0, 0);
                    Path(u, Lh, (0, At(Rh, 0.32f, -0.12f, -0.08f, 0)), (0.32f, overShoulder), (0.55f, rear), (0.72f, At(rear, 0.14f, 0, 0, 0)), (1, At(Rh, 0.32f, -0.12f, -0.08f, 0)));
                    kL = 1; break;
                }
        }
    }

    /// <summary>A gun's kick shoves the shooter's chest back a little (heavier guns more).</summary>
    void RecoilKick(Actor A, WeaponDef d)
    {
        if (A.dyn == null) return;
        string grip = GripOf(d); float k = RecoilOf(grip, d) * (A.team == "enemy" ? 0.6f : 1);
        A.dyn.vf -= k * 2.2f; A.dyn.vb -= k * 0.6f;
    }
}

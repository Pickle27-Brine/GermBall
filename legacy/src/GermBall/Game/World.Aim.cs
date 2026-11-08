using System;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>
/// Aiming feel per weapon: how the right stick maps to turning (the response curve's exponent, top turn speed, how
/// much it slows when aiming down sights, and how fast a fully pushed stick ramps up), and the aim assist (the cone it
/// looks in, hip and aimed, how hard it pulls toward a target, how much it slows your turning over one, and its range).
/// Shotguns get a wide, sticky, short assist; scoped rifles a narrow cone, a precise curve and strong slowdown; launchers
/// and bows little pull (you lead your shots); SMGs and beams track; melee snaps to whatever's in front of you.
/// </summary>
public sealed partial class World
{
    public readonly record struct AimProf(float Curve, float Turn, float Ads, float Ramp, float Cone, float ConeAds, float Pull, float Slow, float Range, float MouseAds);

    static readonly AimProf AIM_DEFAULT = new(1.5f, 3.2f, 0.7f, 1.5f, 0.09f, 0.13f, 0.45f, 0.62f, 85, 0.75f);
    public AimProf AimProfOf(Actor A)
    {
        if (A == null) return AIM_DEFAULT;
        var w = CurWpn(A); var d = CurDef(A);
        if (w == null) return new(1.3f, 3.8f, 1, 1.6f, 0.2f, 0.2f, 0.7f, 0.8f, 6, 1);   // fists
        return d.Id switch
        {
            "revolver" => new(1.6f, 3.0f, 0.6f, 1.4f, 0.08f, 0.12f, 0.55f, 0.55f, 70, 0.7f),
            "smg" => new(1.5f, 3.3f, 0.75f, 1.6f, 0.12f, 0.15f, 0.5f, 0.7f, 50, 0.8f),
            "rifle" or "needler" => new(1.5f, 3.1f, 0.65f, 1.5f, 0.09f, 0.13f, 0.5f, 0.6f, 80, 0.7f),
            "shotgun" => new(1.3f, 3.8f, 0.8f, 1.7f, 0.17f, 0.21f, 0.65f, 0.72f, 22, 0.85f),
            "sniper" or "hunting" => new(2.0f, 2.6f, 0.38f, 1.25f, 0.045f, 0.06f, 0.22f, 0.4f, 170, 0.45f),
            "hmg" => new(1.5f, 2.4f, 0.7f, 1.35f, 0.12f, 0.15f, 0.55f, 0.6f, 70, 0.8f),
            "rocket" or "chargecannon" or "mechbomb" or "mechhoming" => new(1.6f, 2.6f, 0.6f, 1.4f, 0.07f, 0.09f, 0.28f, 0.5f, 90, 0.7f),
            "bow" => new(1.8f, 3.0f, 0.55f, 1.4f, 0.07f, 0.1f, 0.33f, 0.5f, 90, 0.6f),
            "armcannon" => new(1.4f, 3.0f, 0.7f, 1.5f, 0.1f, 0.13f, 0.6f, 0.65f, 45, 0.8f),
            "pistol" or "laserpistol" or "plasma" or "darts" or "knives" => new(1.4f, 3.4f, 0.75f, 1.6f, 0.1f, 0.14f, 0.5f, 0.65f, 60, 0.8f),
            _ => d.Type == "melee" ? new(1.3f, 3.8f, 1, 1.6f, 0.2f, 0.2f, 0.7f, 0.8f, 6, 1) : AIM_DEFAULT,
        };
    }
    float stickPinnedT;
}

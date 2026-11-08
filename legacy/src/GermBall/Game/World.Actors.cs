using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>Every tunable the settings screen can change. Saved to settings.json (the "SAVED" keys of the original).</summary>
public sealed class Settings
{
    public float density = 1, fov = 66; public bool fp, companion, companion2, companion3, chatter = true, brutes = true;
    public float volume = 0.7f, music = 0.45f, speed = 1, dmgTaken = 1, dmgDealt = 1, grav = 1, timeScale = 1;
    public float sens = 1, padSens = 1, adsSens = 0.5f; public bool invertY;
    /// <summary>Vertical render resolution (the browser build rendered at 480 lines for its pixel look; 0 = native).</summary>
    public int res = 480;
    public int shadows = 1; public float drawDist = 170; public int decor = 2, debrisMax = 240, enemyCap = 70; public float voice = 0.45f; public bool subs = true;
    // native extras
    public bool vsync = true, fullscreen = false; public int shadowRes = 2048; public int maxFps = 0;
    public Settings Clone() => (Settings)MemberwiseClone();
}

public sealed class ActorType
{
    public float S, F, Km, Hp, Carry; public string[] LiftMen; public string Label; public bool Fragile, Big, Boss; public string Mimic;
    public ActorType With(Action<ActorType> f) { var t = (ActorType)MemberwiseClone(); f(t); return t; }
}

public sealed class PartDef { public string N; public int Par; public float Hp; public bool Dark, Crit; public List<int> Kids = new(); }

/// <summary>The character controller state (the original's "P").</summary>
public sealed partial class PState
{
    public string type; public float s, f, R, H, BR, STEP, km, mass;
    public float x, y, z, vx, vy, vz, px, py, pz; public bool ground = true, rolling, jumping, jumpRolled, charging; public float rev, ctrlLock, facing, nx, ny = 1, nz;
    public Body gBody; public bool skid; public float shield = 100, lastHit = -10, squash; public bool prevJump, prevRoll, prevGrab, prevJumpG, prevRollG, prevPound, prevUse, prevPunch, prevReload, onStairs, prevFireT, chargeFire, fireBlock;
    public V3 spin = new(1, 0, 0); public float spinRate; public Q4 ballQ = new(), pBallQ = new(); public V3 up = new(0, 1, 0);
    public bool dashUsed; public float dashT, curl, downT, hoverFuel = 2.5f; public bool hover, ads; public float charge;
    public Actor grabbed; public float grabCD, struggle, flungT; public Actor flungBy; public Body hold; public Actor holdA; public float stunT, flatT; public int pound; public float poundT; public int homing; public float homingT; public Actor homeTgt;
    public bool alive = true; public float deadT; public Vehicle vehicle; public Ragdoll ragdoll; public bool berserk;
}

public sealed class InputState { public float mx, mz, yaw, aimYaw; public bool jump, roll, pound, ads, fire, grab, use, up, down, punch, reload; public V3 throwAt; }
public sealed class AimState { public float px, py, pz, fx, fy, fz = -1; public bool ok; }
public sealed class Anim { public float stride = 1, phase, lean, down, aimK, holdK, throwT, punchT, hurtT, twist, hop, yawRate, swingT, squashT, upYaw; }

public sealed partial class Actor
{
    public string type, team; public bool human; public PState P; public WState W; public AimState AIM = new(); public InputState IN = new();
    public Brain brain; public Squad squad;
    public float[] hp, hpMax, flash; public bool[] has; public float[] pose; public float poseT = -1; public Anim anim = new();
    public List<ArmorPlate> armor = new(); public float armorMax; public int needles;
    public bool inSim, dormant; public float heavy; public int lod; public float lodAcc; public int hitCount; public float lastHurt = -10; public Actor lastHurtBy;
    public Body body; public int uid; public bool NearProp;
    public bool InSim { get => inSim; set => inSim = value; }
    public float[] springs = GermDraw.NewSpringState();
    public object vis;   // render-side state
}

public sealed partial class World
{
    // the original measured speeds in Genesis pixels per frame
    const float PXM = 0.028f, FR = 60;
    static float Pxs(float v) => v * FR * PXM; static float Pxs2(float v) => v * FR * FR * PXM;
    // PACE slows the whole game down a notch from the old Genesis speeds
    const float PACE = 0.78f;
    float ACC = Pxs2(0.046875f) * 0.8f, DEC = Pxs2(0.5f), FRC = Pxs2(0.046875f), TOP = Pxs(7.5f) * PACE;
    float ROLL_FRC = Pxs2(0.0234375f), ROLL_DEC = Pxs2(0.125f), ROLL_MIN = Pxs(1.03125f), UNROLL = Pxs(0.5f), ROLL_CAP = Pxs(22) * 0.86f;
    float SLP = Pxs2(0.125f), SLP_UP = Pxs2(0.078125f), SLP_DOWN = Pxs2(0.3125f);
    float GRV = Pxs2(0.21875f), JMP = Pxs(6.5f), JMP_CUT = Pxs(4), AIR = Pxs2(0.09375f), MAX_FALL = Pxs(16), SLIP = Pxs(2.5f), SKID_KICK = Pxs(0.5f);
    const float AIR_DRAG = 31f / 32, ROLL_TURN = 1.7f, ADH = 3; public const float DT = 1f / 120;
    float DASH = 18; const float POUND_V = 34;
    /// <summary>Homing dashes fly this much faster than a plain air dash.</summary>
    const float HOMING_K = 1.75f;
    Dictionary<string, float> BASE;
    public Settings SET;

    // key bindings: every action takes two keys (mouse buttons count as Mouse0/1/2)
    public static readonly (string key, string name, string a, string b)[] BIND_DEF =
    {
        ("forward", "Move forward", "KeyW", "ArrowUp"), ("back", "Move back", "KeyS", "ArrowDown"), ("left", "Move left", "KeyA", "ArrowLeft"), ("right", "Move right", "KeyD", "ArrowRight"),
        ("jump", "Jump / air dash", "Space", ""), ("roll", "Roll · spin dash · ground pound", "KeyC", "ShiftLeft"), ("fire", "Fire / swing", "Mouse0", "KeyF"), ("aim", "Aim down sights", "Mouse2", ""),
        ("grab", "Pick up and throw", "KeyE", "Mouse1"), ("punch", "Punch · ground pound (in ball form)", "KeyQ", "Mouse3"), ("reload", "Reload", "KeyR", ""), ("use", "Interact / ride", "KeyG", ""), ("up", "Hover chair: rise", "Space", "KeyE"), ("down", "Hover chair: sink", "KeyC", "ShiftLeft"),
        ("next", "Next weapon", "KeyP", ""), ("slot1", "Weapon 1", "Digit1", ""), ("slot2", "Weapon 2", "Digit2", ""), ("slot3", "Weapon 3", "Digit3", ""),
        ("base", "Back to base", "KeyB", ""), ("freecam", "Free camera", "KeyV", ""), ("song", "Next song", "KeyM", ""),
    };
    public static Dictionary<string, string[]> DefaultBinds() => BIND_DEF.ToDictionary(b => b.key, b => new[] { b.a, b.b });
    public Dictionary<string, string[]> BINDS;

    void ApplyTuning()
    {
        float k = SET.speed, g = SET.grav;
        ACC = BASE["ACC"] * k; DEC = BASE["DEC"] * k; FRC = BASE["FRC"] * k; TOP = BASE["TOP"] * k; AIR = BASE["AIR"] * k;
        ROLL_FRC = BASE["ROLL_FRC"] * k; ROLL_DEC = BASE["ROLL_DEC"] * k; ROLL_MIN = BASE["ROLL_MIN"] * k; UNROLL = BASE["UNROLL"] * k; ROLL_CAP = BASE["ROLL_CAP"] * k; DASH = BASE["DASH"] * k;
        SLP = BASE["SLP"] * g; SLP_UP = BASE["SLP_UP"] * g; SLP_DOWN = BASE["SLP_DOWN"] * g; GRV = BASE["GRV"] * g; PGRAV = BASE["PGRAV"] * g; MAX_FALL = BASE["MAX_FALL"] * MathF.Sqrt(g);
    }
    void InitTuning()
    {
        BASE = new() { ["ACC"] = ACC, ["DEC"] = DEC, ["FRC"] = FRC, ["TOP"] = TOP, ["ROLL_FRC"] = ROLL_FRC, ["ROLL_DEC"] = ROLL_DEC, ["ROLL_MIN"] = ROLL_MIN, ["UNROLL"] = UNROLL, ["ROLL_CAP"] = ROLL_CAP,
            ["SLP"] = SLP, ["SLP_UP"] = SLP_UP, ["SLP_DOWN"] = SLP_DOWN, ["GRV"] = GRV, ["AIR"] = AIR, ["MAX_FALL"] = MAX_FALL, ["DASH"] = DASH, ["PGRAV"] = PGRAV };
    }
    // enemies hit for a quarter of what they used to
    const float ENEMY_DMG = 0.25f;

    // ----- body plan: 15 round parts. hp is for a normal-sized man
    public static class PART { public const int head = 0, torso = 1, pelvis = 2, uarmL = 3, farmL = 4, handL = 5, uarmR = 6, farmR = 7, handR = 8, thighL = 9, shinL = 10, footL = 11, thighR = 12, shinR = 13, footR = 14; }
    public static readonly PartDef[] PDEF = BuildPdef();
    public const int NPART = 15;
    static PartDef[] BuildPdef()
    {
        var d = new PartDef[]
        {
            new() { N = "head", Par = 1, Hp = 30, Crit = true }, new() { N = "torso", Par = -1, Hp = 75, Crit = true }, new() { N = "pelvis", Par = -1, Hp = 55 },
            new() { N = "uarmL", Par = 1, Hp = 22 }, new() { N = "farmL", Par = 3, Hp = 18, Dark = true }, new() { N = "handL", Par = 4, Hp = 9, Dark = true },
            new() { N = "uarmR", Par = 1, Hp = 22 }, new() { N = "farmR", Par = 6, Hp = 18, Dark = true }, new() { N = "handR", Par = 7, Hp = 9, Dark = true },
            new() { N = "thighL", Par = 2, Hp = 28 }, new() { N = "shinL", Par = 9, Hp = 22, Dark = true }, new() { N = "footL", Par = 10, Hp = 10, Dark = true },
            new() { N = "thighR", Par = 2, Hp = 28 }, new() { N = "shinR", Par = 12, Hp = 22, Dark = true }, new() { N = "footR", Par = 13, Hp = 10, Dark = true },
        };
        for (int i = 0; i < d.Length; i++) if (d[i].Par >= 0 && i != d[i].Par && d[i].Par != 1 && d[i].Par != 2) d[d[i].Par].Kids.Add(i);
        d[1].Kids.AddRange(new[] { 0, 3, 6 }); d[2].Kids.AddRange(new[] { 9, 12 });
        return d;
    }

    // man types. s: height scale, f: girth, km: walking speed, hp: health scale, carry: heaviest thing it can lift (kg)
    public static readonly Dictionary<string, ActorType> TYPES = BuildTypes();
    public static readonly Dictionary<string, string> MIMIC_OF = new() { ["mhero"] = "hero", ["mally"] = "ally", ["mally2"] = "ally2", ["mally3"] = "ally3" };
    static readonly HashSet<string> HERO_TYPES = new() { "hero", "ally", "ally2", "ally3", "mecha" };
    static Dictionary<string, ActorType> BuildTypes()
    {
        string[] all = { "small", "normal", "hero", "ally", "ally2", "ally3", "assassin" };
        var t = new Dictionary<string, ActorType>
        {
            ["hero"] = new() { S = 0.93f, F = 1.32f, Km = 1, Hp = 1, Carry = 170, LiftMen = new[] { "small", "assassin" }, Label = "you" },
            // the companions: Volt the marksman (tall and lean), Brick the tank (wide and heavy), Wisp the scout (small and quick)
            ["ally"] = new() { S = 0.97f, F = 1.1f, Km = 1, Hp = 1, Carry = 170, LiftMen = new[] { "small", "assassin" }, Label = "Tab" },
            ["ally2"] = new() { S = 1.06f, F = 1.85f, Km = 0.92f, Hp = 1.5f, Carry = 900, LiftMen = new[] { "small", "assassin", "normal" }, Label = "Cap" },
            ["ally3"] = new() { S = 0.84f, F = 0.92f, Km = 1.14f, Hp = 0.8f, Carry = 120, LiftMen = new[] { "small" }, Label = "Jab" },
            ["small"] = new() { S = 0.92f, F = 1.7f, Km = 0.8f, Hp = 0.45f, Carry = 50, LiftMen = Array.Empty<string>(), Label = "coccus" },
            ["normal"] = new() { S = 1.2f, F = 1.36f, Km = 0.74f, Hp = 1, Carry = 170, LiftMen = new[] { "small", "assassin" }, Label = "bacillus trooper" },
            ["brute"] = new() { S = 1.68f, F = 1.55f, Km = 0.62f, Hp = 3.2f, Carry = 2600, LiftMen = all, Label = "amoeba brute" },
            ["assassin"] = new() { S = 1.1f, F = 0.74f, Km = 1.05f, Hp = 0.32f, Carry = 60, LiftMen = Array.Empty<string>(), Label = "spirillum assassin", Fragile = true },
            ["mech"] = new() { S = 2.7f, F = 1.45f, Km = 0.42f, Hp = 9, Carry = 12000, LiftMen = all, Label = "phage titan", Big = true },
            ["mecha"] = new() { S = 2.3f, F = 1.4f, Km = 1.05f, Hp = 7, Carry = 12000, LiftMen = new[] { "small", "normal", "assassin", "brute" }, Label = "Med-Mech", Big = true },
        };
        // mimics: boss copies of you and your companions, darker, red-eyed, much tougher, fighting the way you do
        foreach (var (m, of) in new[] { ("mhero", "hero"), ("mally", "ally"), ("mally2", "ally2"), ("mally3", "ally3") })
        {
            var T = t[of]; t[m] = T.With(x => { x.S = T.S * 1.1f; x.Hp = T.Hp * 5; x.Carry = 2600; x.LiftMen = all; x.Label = "dark " + (m == "mhero" ? "double" : T.Label); x.Mimic = of; x.Boss = true; });
        }
        return t;
    }

    static PState MakeP(string type)
    {
        var T = TYPES[type]; float s = T.S, f = T.F;
        return new PState { type = type, s = s, f = f, R = 0.27f * s * MathF.Sqrt(f), H = 1.86f * s, BR = 0.46f * s * MathF.Sqrt(f), STEP = MathF.Max(0.35f, 0.45f * s), km = T.Km, mass = 90 * s * s * s * f * f };
    }
    int actorIds = 4000;
    Body MakeActorBody(Actor A) { var b = new Body { Id = ++actorIds, Kind = SPH, R = 0.3f, M = 90, Im = 1f / 90, E = 0.2f, Mu = 0.15f, Bound = 1, Actor = A }; return b; }
    public readonly List<Actor> actors = new();

    public Actor MakeActor(string type, float x, float z, bool human = false, float? facing = null)
    {
        var T = TYPES[type];
        var A = new Actor
        {
            type = type, team = HERO_TYPES.Contains(type) ? "hero" : "enemy", human = human, P = MakeP(type), W = MakeW(),
            hp = new float[NPART], hpMax = new float[NPART], has = new bool[NPART], flash = new float[NPART], pose = new float[NPART * 13],
        };
        A.anim.phase = R() * 6;
        A.body = MakeActorBody(A); A.uid = A.body.Id;
        for (int i = 0; i < NPART; i++) { A.hpMax[i] = A.hp[i] = PDEF[i].Hp * T.Hp; A.has[i] = true; }
        var P = A.P; P.x = x; P.z = z; P.y = GroundAt(x, z, 999, true).h + 0.02f; P.px = P.x; P.py = P.y; P.pz = P.z; P.facing = facing ?? R() * 6.28f;
        if (A.team == "enemy") P.shield = 0;
        // loadouts
        switch (type)
        {
            case "hero": GiveWeapon(A, "laserpistol"); break;
            case "ally": case "mally": GiveWeapon(A, "hunting"); GiveWeapon(A, "rifle", null, false); GiveWeapon(A, "pistol", null, false); break;
            case "ally2": case "mally2": GiveWeapon(A, "shotgun"); GiveWeapon(A, "sledge", null, false); GiveWeapon(A, "rocket", null, false); break;
            case "ally3": case "mally3": GiveWeapon(A, "smg"); GiveWeapon(A, "knives", null, false); GiveWeapon(A, "knife", null, false); break;
            case "mhero": GiveWeapon(A, "laserpistol"); GiveWeapon(A, "chargecannon", null, false); GiveWeapon(A, "hammer", null, false); break;
            case "brute": GiveWeapon(A, PickW(LOADOUT["brute"])); if (R() < 0.6f) GiveWeapon(A, PickW(LOADOUT["bruteRanged"])); break;
            case "assassin": GiveWeapon(A, "darts"); GiveWeapon(A, "knife", null, false); break;
            case "mech": GiveWeapon(A, "hmg"); break;
            case "mecha": GiveWeapon(A, "mechmg"); GiveWeapon(A, "mechhoming", null, false); GiveWeapon(A, "mechbomb", null, false); break;
            default: GiveWeapon(A, PickW(LOADOUT[type])); if (type == "normal" && R() < 0.3f) GiveWeapon(A, "pistol", null, false); break;
        }
        if (A.team == "hero") foreach (var w in A.W.slots) if (w != null) w.clip = 0;
        AddArmor(A);
        actors.Add(A); return A;
    }

    // While one actor steps, these point at its state (the original's globals P, PBODY, W, AIM, IN, CUR).
    PState P; Body PBODY; WState W; AimState AIM; InputState IN; Actor CUR;
    T WithActor<T>(Actor A, Func<T> fn)
    {
        var (sP, sB, sW, sA, sI, sC) = (P, PBODY, W, AIM, IN, CUR);
        P = A.P; PBODY = A.body; W = A.W; AIM = A.AIM; IN = A.IN; CUR = A;
        try { return fn(); } finally { P = sP; PBODY = sB; W = sW; AIM = sA; IN = sI; CUR = sC; }
    }
    void WithActor(Actor A, Action fn) => WithActor<int>(A, () => { fn(); return 0; });

    static int LegsOf(Actor A) => (A.has[PART.thighL] && A.has[PART.shinL] ? 1 : 0) + (A.has[PART.thighR] && A.has[PART.shinR] ? 1 : 0);
    static int ArmsOf(Actor A) => (A.has[PART.farmL] && A.has[PART.handL] ? 1 : 0) + (A.has[PART.farmR] && A.has[PART.handR] ? 1 : 0);
    static bool CanShoot(Actor A) => A.has[PART.farmR] && A.has[PART.uarmR];
    static float HealthOf(Actor A) { float s = 0, m = 0; for (int i = 0; i < NPART; i++) { m += A.hpMax[i]; if (A.has[i]) s += MathF.Max(0, A.hp[i]); } return s / m; }

    // ----- ground and walls (sizes come from the current actor)
    public struct GroundQ { public float h, nx, ny, nz; public Body body; public bool stair; }
    readonly List<Static> QP = new(), QW = new(); readonly List<Body> QBG = new();
    public GroundQ GroundAt(float x, float z, float top, bool noBodies = false)
    {
        var t = TerrainSample(x, z); float nl = Hypot(t.gx, 1, t.gz);
        var G = new GroundQ { h = t.h, nx = -t.gx / nl, ny = 1 / nl, nz = -t.gz / nl };
        QueryStatics(x - 0.5f, x + 0.5f, z - 0.5f, z + 0.5f, QP);
        foreach (var o in QP)
        {
            if (o.Kind == BOXK)
            {
                const float e = 0.12f;
                if (x > o.MinX - e && x < o.MaxX + e && z > o.MinZ - e && z < o.MaxZ + e && o.MaxY <= top && o.MaxY > G.h) { G.h = o.MaxY; G.nx = 0; G.ny = 1; G.nz = 0; G.stair = false; }
            }
            else if (o.Kind == CYLK)
            {
                // the whole top is floor (it used to be only the inner 80%: step onto the rim and you sank in, then got shoved out)
                if (o.Top <= top && o.Top > G.h && Hypot(x - o.X, z - o.Z) < o.R + 0.1f) { G.h = o.Top; G.nx = 0; G.ny = 1; G.nz = 0; }
            }
            else if (x >= o.MinX && x <= o.MaxX && z >= o.MinZ && z <= o.MaxZ)
            {
                float ph = o.Fn(x, z); if (ph > top || ph <= G.h) continue;
                G.h = ph; var PN = PatchNormal(o, x, z); G.nx = PN.nx; G.ny = PN.ny; G.nz = PN.nz; G.stair = o.Tag is string s && s == "stair";
            }
        }
        if (noBodies) return G;
        QueryBodies(x - 0.1f, x + 0.1f, z - 0.1f, z + 0.1f, QBG);
        foreach (var b in QBG)
        {
            if (b.Held != null) continue;
            float y = RayDownBody(b, x, top, z);
            if (y > G.h) { G.h = y; G.nx = RN.x; G.ny = RN.y; G.nz = RN.z; G.body = b; }
        }
        if (G.ny < 0.05f) { G.ny = 0.05f; float l = Hypot(G.nx, G.ny, G.nz); G.nx /= l; G.ny /= l; G.nz /= l; }
        return G;
    }
    void WallHit(float nx, float nz)
    {
        float vn = P.vx * nx + P.vz * nz;
        if (P.flungT > 0 && -vn > 7) { P.flungT = 0; ImpactHurt(-vn); }
        if (vn >= 0) return;
        float k = P.rolling ? 1.25f : 1; P.vx -= k * vn * nx; P.vz -= k * vn * nz;
        if (P.rolling && -vn > 6) { for (int i = 0; i < 4; i++) Puff(P.x, P.y + 0.3f, P.z, 3, 2, 0.12f); Rumble(MathF.Min(1, -vn / 20), 0.5f, 120); Sfx("thud", P.x, P.y, P.z, MathF.Min(1, -vn / 20)); }
    }
    void PlayerWalls(float feet, float head)
    {
        float R = P.rolling ? P.BR : P.R;
        QueryStatics(P.x - 1.5f, P.x + 1.5f, P.z - 1.5f, P.z + 1.5f, QW);
        foreach (var o in QW)
        {
            if (o.Kind == BOXK)
            {
                if (o.MaxY <= feet + P.STEP || o.MinY >= head) continue;
                float cx = Clamp(P.x, o.MinX, o.MaxX), cz = Clamp(P.z, o.MinZ, o.MaxZ), dx = P.x - cx, dz = P.z - cz, d2 = dx * dx + dz * dz;
                if (d2 >= R * R) continue;
                if (d2 > 1e-8f) { float d = MathF.Sqrt(d2); P.x += dx / d * (R - d); P.z += dz / d * (R - d); WallHit(dx / d, dz / d); }
                else
                {
                    var opts = new[] { (o.MinX - R - P.x, 0f, -1f, 0f), (o.MaxX + R - P.x, 0f, 1f, 0f), (0f, o.MinZ - R - P.z, 0f, -1f), (0f, o.MaxZ + R - P.z, 0f, 1f) };
                    var best = opts.OrderBy(a => MathF.Abs(a.Item1 + a.Item2)).First(); P.x += best.Item1; P.z += best.Item2; WallHit(best.Item3, best.Item4);
                }
            }
            else if (o.Kind == CYLK)
            {
                if (o.Top <= feet + P.STEP || o.Bottom >= head) continue;
                float dx = P.x - o.X, dz = P.z - o.Z, d = Hypot(dx, dz), min = o.R + R;
                if (d >= min) continue;
                if (min - d > MathF.Max(0.5f, R))
                {   // deep inside (never a normal bump): don't fling across to the far rim. A low solid takes you onto its top;
                    // otherwise you go back to where you were before this step, against its side
                    if (o.Top - feet < 1.0f) { P.y = o.Top; if (P.vy < 0) P.vy = 0; P.ground = true; continue; }
                    float bx = P.px - o.X, bz = P.pz - o.Z, bd = Hypot(bx, bz);
                    if (bd > d + 0.05f) { dx = bx; dz = bz; d = bd; }
                }
                if (d < 1e-6f) { dx = 1; dz = 0; d = 1; }
                P.x = o.X + dx / d * min; P.z = o.Z + dz / d * min; WallHit(dx / d, dz / d);
            }
        }
        if (LAYOUT.Bowl != null)
        {   // wave arenas: the cell membrane at the foot of the basin wall keeps the fight on the floor
            float d = Hypot(P.x, P.z), R2 = ArenaR;
            if (d > R2) { P.x *= R2 / d; P.z *= R2 / d; float vn = (P.vx * P.x + P.vz * P.z) / (R2 * R2); if (vn > 0) { P.vx -= vn * P.x * 1.3f; P.vz -= vn * P.z * 1.3f; } }
        }
        float lim = HALF - 30;
        if (MathF.Abs(P.x) > lim) { P.x = MathF.Sign(P.x) * lim; P.vx *= -0.3f; }
        if (MathF.Abs(P.z) > lim) { P.z = MathF.Sign(P.z) * lim; P.vz *= -0.3f; }
    }
    bool PatchBlocks(float x, float z, float feet)
    {
        QueryStatics(x - 0.5f, x + 0.5f, z - 0.5f, z + 0.5f, QW);
        foreach (var o in QW)
            if (o.Kind == PATCHK && x >= o.MinX && x <= o.MaxX && z >= o.MinZ && z <= o.MaxZ && o.Fn(x, z) > feet + P.STEP && (o.Tag is "stair" ? o.Fn(x, z) - 0.6f : o.MinY) < feet + P.H * 0.9f) return true;
        return false;
    }
    void HorizMove(float h)
    {
        float ox = P.x; P.x += P.vx * h; if (PatchBlocks(P.x, P.z, P.y)) { P.x = ox; WallHit(P.vx > 0 ? -1 : 1, 0); }
        float oz = P.z; P.z += P.vz * h; if (PatchBlocks(P.x, P.z, P.y)) { P.z = oz; WallHit(0, P.vz > 0 ? -1 : 1); }
    }
    float BodyH() => P.rolling ? P.BR * 2 : P.H * (P.stunT > 0 ? 0.4f : 0.95f);
    void SetNormal(GroundQ g) { P.nx = g.nx; P.ny = g.ny; P.nz = g.nz; P.gBody = g.body; P.onStairs = g.stair; }
    void GroundMove(float h)
    {
        HorizMove(h); P.y += P.vy * h;
        PlayerWalls(P.y, P.y + BodyH());
        var g = GroundAt(P.x, P.z, P.y + P.STEP, CUR.lod > 0);
        float sp = Hypot(P.vx, P.vy, P.vz), gap = P.y - g.h, snap = 0.06f + sp * h * 0.6f;
        if (gap > snap) { P.ground = false; return; }
        float vn = P.vx * g.nx + P.vy * g.ny + P.vz * g.nz;
        if (gap > -0.02f && vn > GRV * g.ny * h * ADH + 0.05f) { P.ground = false; return; }
        P.y = g.h; SetNormal(g);
        P.vx -= vn * g.nx; P.vy -= vn * g.ny; P.vz -= vn * g.nz;
        if (MathF.Abs(vn) < sp * 0.5f) { float ns = Hypot(P.vx, P.vy, P.vz); if (ns > 1e-6f) { float k = sp / ns; P.vx *= k; P.vy *= k; P.vz *= k; } }
    }
    float? AirMove(float h)
    {
        float oy = P.y, height = BodyH();
        HorizMove(h); P.y += P.vy * h;
        PlayerWalls(P.y, P.y + height);
        if (P.vy > 0) foreach (var o in QW) if (o.Kind == BOXK && oy + height <= o.MinY + 0.01f && P.y + height > o.MinY && P.x > o.MinX && P.x < o.MaxX && P.z > o.MinZ && P.z < o.MaxZ) { P.y = o.MinY - height; P.vy = 0; }
        var g = GroundAt(P.x, P.z, MathF.Max(oy, P.y) + 0.25f, CUR.lod > 0);
        if (P.y > g.h) return null;
        P.y = g.h; SetNormal(g);
        float vn = P.vx * g.nx + P.vy * g.ny + P.vz * g.nz;
        if (vn < 0) { P.vx -= vn * g.nx; P.vy -= vn * g.ny; P.vz -= vn * g.nz; }
        P.ground = true; return MathF.Min(0, vn);
    }
    float SpeedOf() => Hypot(P.vx, P.vy, P.vz);
    void SetSpeed(float s) { float c = Hypot(P.vx, P.vy, P.vz); if (c > 1e-6f) { float k = s / c; P.vx *= k; P.vy *= k; P.vz *= k; } }
    void SlowBy(float a) { float c = Hypot(P.vx, P.vy, P.vz); SetSpeed(MathF.Max(0, c - a)); }
    void TurnVelToward(V3 wd, float maxA)
    {
        float sp = Hypot(P.vx, P.vy, P.vz); if (sp < 1e-4f) return;
        float ux = P.vx / sp, uy = P.vy / sp, uz = P.vz / sp, c = Clamp(ux * wd.x + uy * wd.y + uz * wd.z, -1, 1), ang = MathF.Acos(c);
        if (ang < 1e-4f) return;
        float cxp = uy * wd.z - uz * wd.y, cyp = uz * wd.x - ux * wd.z, czp = ux * wd.y - uy * wd.x;
        float a = MathF.Min(ang, maxA) * ((cxp * P.nx + cyp * P.ny + czp * P.nz) >= 0 ? 1 : -1), ca = MathF.Cos(a), sa = MathF.Sin(a);
        float nvx = P.ny * P.vz - P.nz * P.vy, nvy = P.nz * P.vx - P.nx * P.vz, nvz = P.nx * P.vy - P.ny * P.vx;
        P.vx = P.vx * ca + nvx * sa; P.vy = P.vy * ca + nvy * sa; P.vz = P.vz * ca + nvz * sa;
    }

    // ----- air dash: bends about 15% toward an enemy ahead, or homes in fully while homing charges last
    struct DashV { public float vx, vy, vz, dx, dz; public Actor tgt; }
    const float ASSIST = 0.15f;
    Actor DashTarget(float dx, float dz, float cone, float range)
    {
        Actor best = null; float bs = -1e9f;
        foreach (var T in actors)
        {
            if (T == CUR || !T.P.alive || T.team == CUR.team || T.dormant) continue;
            float ex = T.P.x - P.x, ez = T.P.z - P.z, hd = Hypot(ex, ez);
            if (hd < 1.2f || hd > range) continue;
            float c = (ex * dx + ez * dz) / hd; if (c < cone) continue;
            float sc = c * 2 - hd / 15; if (sc > bs) { bs = sc; best = T; }
        }
        return best;
    }
    DashV DashVec(float m, float wx, float wz)
    {
        float dx, dz;
        if (m > 0.1f) { float l = Hypot(wx, wz); dx = wx / l; dz = wz / l; } else { dx = -MathF.Sin(P.facing); dz = -MathF.Cos(P.facing); }
        float along = P.vx * dx + P.vz * dz, sp = MathF.Max(DASH, along + 5);
        float vy = MathF.Max(P.vy, 2.2f);
        bool homing = P.homing > 0; var T = DashTarget(dx, dz, homing ? 0.45f : 0.8f, homing ? 32 : 22);
        var DV = new DashV { tgt = T };
        if (T != null)
        {
            float k = homing ? 1 : ASSIST, ex = T.P.x - P.x, ey = T.P.y + T.P.H * 0.45f - (P.y + P.BR), ez = T.P.z - P.z, hd = Hypot(ex, ez);
            float nx = dx + (ex / hd - dx) * k, nz = dz + (ez / hd - dz) * k, nl = Hypot(nx, nz);
            dx = nx / nl; dz = nz / nl;
            float t = hd / sp, vyI = ey / t + 0.5f * GRV * t;
            vy = Lerp(vy, Clamp(vyI, -8, 14), k);
        }
        DV.dx = dx; DV.dz = dz; DV.vx = dx * sp; DV.vz = dz * sp; DV.vy = vy;
        return DV;
    }

    // ----- the controller
    readonly V3 WD = new();
    public float simT;
    void PlayerStep(float dt, float t)
    {
        var A = CUR;
        float mx = IN.mx, mz = IN.mz;
        float m = Hypot(mx, mz); if (m > 1) { mx /= m; mz /= m; m = 1; }
        float sy = MathF.Sin(IN.yaw), cy = MathF.Cos(IN.yaw);
        float wx = -sy * mz + cy * mx, wz = -cy * mz - sy * mx;
        if (P.ctrlLock > 0) { P.ctrlLock -= dt; m = 0; wx = wz = 0; }
        bool outp = P.downT > 0 || P.stunT > 0 || P.flatT > 0;
        if (P.stunT > 0) P.stunT -= dt;
        P.ads = IN.ads && !outp && (A.team == "hero" || TYPES[A.type].Mimic != null);
        if (outp) { m = 0; wx = wz = 0; }
        int legs = LegsOf(A);
        bool punchHeld = IN.punch && !outp;
        bool punchPress = punchHeld && !P.prevPunch; P.prevPunch = punchHeld;
        // the punch button doubles as ground pound, but only while curled into a ball in the air
        bool ballAir = !P.ground && P.rolling && P.vehicle == null;
        bool jumpHeld = IN.jump && !outp && legs > 0, rollHeld = IN.roll && !outp, grabHeld = IN.grab && !outp, poundHeld = (IN.pound || (punchHeld && ballAir)) && !outp;
        if (punchPress && !P.rolling && P.vehicle == null && P.grabbed == null && P.hold == null && P.holdA == null) W.punchQ = 0.25f;
        bool jumpPress = jumpHeld && !P.prevJump, rollPress = rollHeld && !P.prevRoll, grabPress = grabHeld && !P.prevGrab, poundPress = poundHeld && !P.prevPound;
        bool grabRelease = !grabHeld && P.prevGrab;
        P.prevJump = jumpHeld; P.prevRoll = rollHeld; P.prevGrab = grabHeld; P.prevPound = poundHeld;
        if (P.grabCD > 0) P.grabCD -= dt;
        if (P.homingT > 0) P.homingT -= dt;
        bool usePress = IN.use && !P.prevUse && !outp; P.prevUse = IN.use;
        // recovery: tech out with jump or roll, or break out automatically after being held down too long
        if (A.team == "hero") { bool tech = (IN.jump && !P.prevJumpG) || (IN.roll && !P.prevRollG); if (P.grabbed == null) { P.prevJumpG = IN.jump; P.prevRollG = IN.roll; } RecoveryStep(A, dt, tech); }
        if (P.ragdoll != null) { RagdollFollow(A, dt); FinishStep(dt, t, 0, 0, 0); return; }
        if (usePress && P.hold == null && P.holdA == null) Interact(A);
        if (P.vehicle != null) { if (outp) Dismount(A, true); else { VehicleStep(dt, m, wx, wz, jumpPress); FinishStep(dt, t, 0, 0, 0); return; } }
        if (P.grabbed != null)
        {   // held up by someone bigger: mash jump or roll to wriggle free
            var H = P.grabbed;
            if (!H.P.alive || H.P.holdA != A) P.grabbed = null;
            else
            {
                if ((IN.jump && !P.prevJumpG) || (IN.roll && !P.prevRollG))
                {
                    P.struggle += A.type == "small" ? 0.12f : A.team == "hero" ? 0.34f : 0.26f; P.squash = 0.18f; Rumble(0.3f, 0.5f, 60);
                    if (P.struggle >= 1) { DropHeld(H); H.P.stunT = MathF.Max(H.P.stunT, 0.5f); }
                }
                P.prevJumpG = IN.jump; P.prevRollG = IN.roll;
                P.rolling = false; P.charging = false; P.hover = false; P.pound = 0;
                FinishStep(dt, t, 0, 0, 0); return;
            }
        }
        float km = P.km * (legs == 2 ? 1 : legs == 1 ? 0.55f : 0.3f) * (P.hold != null ? (P.hold.M > 200 ? 0.6f : 0.85f) : P.holdA != null ? 0.75f : 1) * (P.berserk ? 1.55f : 1);
        if (P.hold == null && P.holdA == null) P.charge = 0;
        // the fire button throws too: press it while holding something to wind up, release to let go
        bool holding = P.hold != null || P.holdA != null;
        bool fireT = A.human && IN.fire && !outp, firePress = fireT && !P.prevFireT; P.prevFireT = fireT;
        if (grabPress)
        {
            if (holding) { if (IN.throwAt != null) ThrowHeld(A); else { P.charge = 0.001f; P.chargeFire = false; } }
            else TryPickUp(A);
        }
        else if (firePress && holding && P.charge <= 0) { P.charge = 0.001f; P.chargeFire = true; P.fireBlock = true; }
        else if (P.charge > 0)
        {   // hold to wind up, release to throw
            bool held = P.chargeFire ? fireT : grabHeld, released = P.chargeFire ? !fireT : grabRelease;
            if (held) P.charge = MathF.Min(1, P.charge + dt / 0.9f);
            else if (released) { ThrowHeld(A, P.charge); P.charge = 0; P.chargeFire = false; }
        }

        if (rollPress && P.ground && !P.charging)
        {
            if (P.rolling) P.rolling = false;
            else if (Hypot(P.vx, P.vy, P.vz) >= ROLL_MIN) P.rolling = true;
            else { P.rolling = true; P.charging = true; P.rev = 0; }
            if (P.rolling && (P.hold != null || P.holdA != null)) DropHeld(A);
        }
        bool jumpUsed = false;
        if (P.charging)
        {
            if (!P.ground) P.charging = false;
            else if (rollHeld)
            {
                if (m > 0.1f) P.facing = MathF.Atan2(-wx, -wz);
                P.rev = MathF.Min(8, P.rev * MathF.Pow(31f / 32, dt * 60) + 9 * dt);
                if (jumpPress) { P.rev = MathF.Min(8, P.rev + 2); jumpUsed = true; Rumble(0.2f, 0.5f, 60); Sfx("rev", P.x, P.y, P.z, 0.4f + P.rev / 12); for (int i = 0; i < 3; i++) Puff(P.x, P.y + 0.1f, P.z, 2.5f, 1.5f, 0.11f); }
                float k = MathF.Exp(-12 * dt); P.vx *= k; P.vy *= k; P.vz *= k;
                if (R() < 0.4f) Puff(P.x + MathF.Sin(P.facing) * 0.35f, P.y + 0.08f, P.z + MathF.Cos(P.facing) * 0.35f, 1.5f, 1.2f, 0.1f);
            }
            else
            {   // launch: much harder off the line than before
                float dx = -MathF.Sin(P.facing), dz = -MathF.Cos(P.facing), d = dx * P.nx + dz * P.nz;
                float ex = dx - d * P.nx, ey = -d * P.ny, ez = dz - d * P.nz, l = Hypot(ex, ey, ez), s = Pxs(12 + P.rev * 0.75f) * SET.speed;
                P.vx = ex / l * s; P.vy = ey / l * s; P.vz = ez / l * s; P.charging = false; P.rev = 0; Rumble(0.7f, 1, 220); Sfx("launch", P.x, P.y, P.z);
                for (int i = 0; i < 12; i++) Puff(P.x, P.y + 0.1f, P.z, 6, 2, 0.15f);
            }
        }

        P.skid = false;
        if (P.ground)
        {
            float nx = P.nx, ny = P.ny, nz = P.nz;
            float vn = P.vx * nx + P.vy * ny + P.vz * nz; P.vx -= vn * nx; P.vy -= vn * ny; P.vz -= vn * nz;
            float gtx = nx * ny, gty = ny * ny - 1, gtz = nz * ny, sinT = Hypot(gtx, gty, gtz);
            if (m > 0.1f)
            {
                float wl = Hypot(wx, wz), ax = wx / wl, az = wz / wl, d = ax * nx + az * nz;
                WD.x = ax - d * nx; WD.y = -d * ny; WD.z = az - d * nz; float l = Hypot(WD.x, WD.y, WD.z); WD.x /= l; WD.y /= l; WD.z /= l;
            }
            if (!P.charging)
            {
                float Spd() => Hypot(P.vx, P.vy, P.vz);
                if (P.rolling)
                {
                    float s = (P.vx * gtx + P.vy * gty + P.vz * gtz) >= 0 ? SLP_DOWN : SLP_UP;
                    P.vx += gtx * s * dt; P.vy += gty * s * dt; P.vz += gtz * s * dt;
                    float sp = Spd();
                    if (m > 0.1f && sp > 0.5f)
                    {
                        float c = (P.vx * WD.x + P.vy * WD.y + P.vz * WD.z) / sp;
                        if (c < -0.5f) SlowBy(ROLL_DEC * m * dt); else TurnVelToward(WD, ROLL_TURN * m * dt);
                    }
                    SlowBy(ROLL_FRC * dt);
                    if (Spd() > ROLL_CAP) SetSpeed(ROLL_CAP);
                    if (Spd() < UNROLL && sinT < 0.25f && !rollHeld) P.rolling = false;
                }
                else
                {
                    float sp = Spd();
                    if ((sp > 0.05f || sinT > 0.13f || m > 0.1f) && !P.onStairs) { P.vx += gtx * SLP * dt; P.vy += gty * SLP * dt; P.vz += gtz * SLP * dt; }
                    sp = Spd();
                    float top = TOP * km;
                    if (m > 0.1f)
                    {
                        if (sp < 0.3f) { float ns = sp + ACC * km * dt; P.vx = WD.x * ns; P.vy = WD.y * ns; P.vz = WD.z * ns; }
                        else
                        {
                            float c = (P.vx * WD.x + P.vy * WD.y + P.vz * WD.z) / sp;
                            if (c < -0.4f && sp > 1.5f)
                            {
                                P.skid = true; SlowBy(DEC * dt);
                                if (Spd() < 0.05f) { P.vx = WD.x * SKID_KICK; P.vy = WD.y * SKID_KICK; P.vz = WD.z * SKID_KICK; }
                                if (R() < 0.5f) Puff(P.x, P.y + 0.05f, P.z, 1.5f, 1, 0.1f);
                            }
                            else
                            {
                                TurnVelToward(WD, Lerp(14, 3.2f, MathF.Min(1, sp / top)) * m * dt);
                                float cap = top * m * (P.ads ? 0.55f : 1);
                                if (sp < cap) SetSpeed(MathF.Min(cap, sp + ACC * km * dt)); else if (sp > cap && (P.ads || sp < top * 1.05f)) SlowBy(DEC * 0.2f * dt);
                            }
                        }
                    }
                    else SlowBy(FRC * dt);
                }
                if (ny < 0.69f && Spd() < SLIP && P.ctrlLock <= 0) P.ctrlLock = 0.5f;
            }
            if (jumpPress && !jumpUsed && !P.charging)
            {
                float jk = A.type == "brute" ? 0.8f : 1;
                P.vx += nx * JMP * jk; P.vy += ny * JMP * jk; P.vz += nz * JMP * jk;
                P.ground = false; P.jumping = true; P.jumpRolled = P.rolling; P.rolling = !(P.hold != null || P.holdA != null); P.squash = -0.2f;
                Sfx("jump", P.x, P.y, P.z);
            }
        }
        else
        {
            // ground pound: roll in the air
            if (poundPress && P.rolling && P.pound == 0 && !P.charging && P.hold == null && P.holdA == null && P.y - GroundAt(P.x, P.z, P.y + 0.1f, true).h > 0.8f)
            {
                P.pound = 1; P.poundT = 0.13f; P.vx *= 0.15f; P.vz *= 0.15f; P.vy = 3; P.rolling = true; P.dashT = 0; P.hover = false; Sfx("poundup", P.x, P.y, P.z);
            }
            if (P.pound == 1) { P.poundT -= dt; P.vy *= MathF.Exp(-8 * dt); if (P.poundT <= 0) { P.pound = 2; P.vy = -POUND_V; } }
            else if (P.pound == 2) { P.vy = -POUND_V; P.vx *= MathF.Exp(-3 * dt); P.vz *= MathF.Exp(-3 * dt); }
            if (jumpPress && !P.dashUsed && !P.charging && P.pound == 0)
            {   // second press in the air: boost forward
                bool homing = P.homing > 0;
                var DV = DashVec(m, wx, wz); float dx = DV.dx, dz = DV.dz;
                P.vx = DV.vx; P.vz = DV.vz; P.vy = DV.vy;
                P.hover = false; P.hoverFuel = 0;   // dashing ends any hover until you land
                if (homing && DV.tgt != null) { P.homing--; P.homingT = MathF.Min(1.2f, Hypot(DV.tgt.P.x - P.x, DV.tgt.P.z - P.z) / (DASH * HOMING_K) + 0.3f); P.homeTgt = DV.tgt; }
                P.dashUsed = true; P.dashT = 0.4f; P.rolling = true; P.jumping = true; P.jumpRolled = false; P.facing = MathF.Atan2(-dx, -dz);
                if (P.hold != null || P.holdA != null) DropHeld(A);
                for (int i = 0; i < 8; i++) Puff(P.x - dx * 0.4f, P.y + 0.3f, P.z - dz * 0.4f, 2, 1, 0.12f);
                Rumble(0.5f, 0.9f, 160); Sfx(homing && DV.tgt != null ? "homing" : "dash", P.x, P.y, P.z);
            }
            if (P.homingT > 0 && P.homeTgt != null && P.homeTgt.P.alive)
            {   // homing charge: steer all the way in
                var T = P.homeTgt.P; float ex = T.x - P.x, ey = T.y + T.H * 0.45f - P.y - P.BR, ez = T.z - P.z, l = Hypot(ex, ey, ez); if (l == 0) l = 1; float sp = MathF.Max(DASH * HOMING_K, Hypot(P.vx, P.vy, P.vz));
                float k = MathF.Min(1, dt * 22);
                if (R() < 0.6f) Puff(P.x, P.y + P.BR, P.z, 1.5f, 0.6f, 0.1f); P.vx += (ex / l * sp - P.vx) * k; P.vy += (ey / l * sp - P.vy) * k; P.vz += (ez / l * sp - P.vz) * k;
            }
            P.hover = P.ads && P.hoverFuel > 0 && !P.charging && P.pound == 0 && P.dashT <= 0;
            if (P.hover)
            {
                P.rolling = false;
                P.hoverFuel -= dt; P.vy += (-0.6f - P.vy) * MathF.Min(1, dt * 7);
                float k = MathF.Exp(-2.4f * dt); P.vx *= k; P.vz *= k;
            }
            else if (P.pound != 2 && !(P.homingT > 0)) P.vy = MathF.Max(P.vy - GRV * dt, -MAX_FALL);
            if (m > 0.1f && P.pound == 0)
            {
                float wl = Hypot(wx, wz), ux = wx / wl, uz = wz / wl, along = P.vx * ux + P.vz * uz;
                if (along < TOP) { float na = MathF.Min(TOP, along + AIR * m * (P.jumping && P.jumpRolled ? 0.5f : 1) * dt); P.vx += ux * (na - along); P.vz += uz * (na - along); }
            }
            if (P.vy > 0 && P.vy < JMP_CUT && P.dashT <= 0) { float k = MathF.Pow(AIR_DRAG, dt * 60); P.vx *= k; P.vz *= k; }
            if (P.jumping && !jumpHeld && P.vy > JMP_CUT) P.vy = JMP_CUT;
        }

        float sp0 = Hypot(P.vx, P.vy, P.vz); int nsub = Math.Max(1, (int)MathF.Ceiling(sp0 * dt / 0.15f)); float hh = dt / nsub;
        bool landed = false; float landVn = 0;
        for (int i = 0; i < nsub; i++)
        {
            if (P.ground) GroundMove(hh);
            else { var r = AirMove(hh); if (r != null) { landed = true; landVn = MathF.Min(landVn, r.Value); } }
        }
        if (!P.ground) P.gBody = null;
        if (landed)
        {
            P.squash = MathF.Min(0.35f, -landVn * 0.025f); P.dashUsed = false; P.hoverFuel = 2.5f; P.homingT = 0;
            if (P.pound == 2) { P.pound = 0; GroundPound(A); P.rolling = rollHeld; P.squash = 0.4f; }
            else if (landVn < -6) { for (int i = 0; i < 6; i++) Puff(P.x, P.y + 0.05f, P.z, 3, 1.2f, 0.12f); Rumble(MathF.Min(1, -landVn / 25), 0.3f, 120); Sfx("land", P.x, P.y, P.z, MathF.Min(1, -landVn / 20)); }
            P.pound = 0;
            if (P.flungT > 0 && landVn < -3) { P.flungT = 0; ImpactHurt(-landVn); }
            if (P.jumping) { P.jumping = false; P.rolling = rollHeld && Hypot(P.vx, P.vy, P.vz) >= ROLL_MIN; }
            if (P.gBody != null && landVn < -4) { Wake(P.gBody); ApplyImpulse(P.gBody, 0, landVn * 40 * P.s * P.s, 0, Rel(P.gBody)); }
        }
        FinishStep(dt, t, m, wx, wz);
    }

    void FinishStep(float dt, float t, float m, float wx, float wz)
    {
        var A = CUR;
        if (P.ground && P.gBody != null && !P.gBody.Sleep)
        {
            var b = P.gBody; var r = Rel(b);
            P.x += (b.Vel.x + b.W.y * r.z - b.W.z * r.y) * dt; P.z += (b.Vel.z + b.W.x * r.y - b.W.y * r.x) * dt;
            ApplyImpulse(b, 0, -(P.rolling ? 220 : 80) * P.s * GRV * dt * 0.5f, 0, r);
        }
        if (P.y < -40) RespawnActor(A);
        if (A.team == "hero" && t - P.lastHit > 5 && P.alive) P.shield = MathF.Min(100, P.shield + dt * 12);
        float hs = Hypot(P.vx, P.vz);
        if (!P.charging && P.stunT <= 0)
        {
            // the hips turn a beat behind the chest (the pose leads with the torso)
            if (hs > 0.3f && !P.skid) P.facing = TurnAngle(P.facing, MathF.Atan2(-P.vx, -P.vz), dt * (P.rolling ? 20 : 7));
            else if (m > 0.1f && !P.rolling) P.facing = TurnAngle(P.facing, MathF.Atan2(-wx, -wz), dt * 7);
            if ((W.aimT > 0 || P.ads || (A.human && SET.fp) || A.anim.aimK > 0.5f || W.charge > 0) && !P.rolling)
            {
                float diff = WrapAngle(IN.aimYaw - P.facing);
                if (hs > 0.6f && P.ground)
                {   // walking while aiming: legs go where you walk (or backpedal), chest twists to the aim
                    float mv = MathF.Atan2(-P.vx, -P.vz); if (MathF.Abs(WrapAngle(mv - IN.aimYaw)) > 1.9f) mv += PI;
                    if (MathF.Abs(WrapAngle(mv - IN.aimYaw)) > 1.3f) mv = IN.aimYaw;
                    P.facing = TurnAngle(P.facing, mv, dt * 7);
                }
                else if (MathF.Abs(diff) > 0.7f) P.facing = TurnAngle(P.facing, IN.aimYaw - MathF.Sign(diff) * 0.7f, dt * 5);
            }
        }
        if (P.dashT > 0) P.dashT -= dt;
        if (P.flungT > 0) P.flungT -= dt;
        float sp = Hypot(P.vx, P.vy, P.vz);
        if (P.charging) { P.spin.x = MathF.Cos(P.facing); P.spin.y = 0; P.spin.z = -MathF.Sin(P.facing); P.spinRate = -(22 + P.rev * 6); }
        else if (P.ground && sp > 0.05f)
        {
            float ax = P.ny * P.vz - P.nz * P.vy, ay = P.nz * P.vx - P.nx * P.vz, az = P.nx * P.vy - P.ny * P.vx, l = Hypot(ax, ay, az);
            if (l > 1e-6f) { P.spin.x = ax / l; P.spin.y = ay / l; P.spin.z = az / l; }
            P.spinRate = sp / P.BR;
        }
        else if (!P.ground && (P.jumping || P.pound != 0)) P.spinRate = MathF.Max(P.spinRate, P.pound != 0 ? 26 : 18);
        if (P.rolling && P.spinRate != 0)
        {
            float a = P.spinRate * dt * 0.5f, s = MathF.Sin(a), qx = P.spin.x * s, qy = P.spin.y * s, qz = P.spin.z * s, qw = MathF.Cos(a); var b = P.ballQ;
            float x = qw * b.x + qx * b.w + qy * b.z - qz * b.y, y = qw * b.y - qx * b.z + qy * b.w + qz * b.x, z = qw * b.z + qx * b.y - qy * b.x + qz * b.w, w = qw * b.w - qx * b.x - qy * b.y - qz * b.z;
            b.x = x; b.y = y; b.z = z; b.w = w; QNorm(b);
        }
        if (P.ground && P.rolling && sp > 12 && R() < sp / 60) Puff(P.x, P.y + 0.05f, P.z, 1.5f, 1.5f, 0.1f);
        if (P.hold != null) CarryBody(A);
        if (P.holdA != null) CarryActor(A);
    }
    readonly V3 RREL = new();
    V3 Rel(Body b) { RREL.x = P.x - b.Pos.x; RREL.y = P.y - b.Pos.y; RREL.z = P.z - b.Pos.z; return RREL; }
}

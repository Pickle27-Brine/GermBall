using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>A vehicle piece: shape+material key, position, size, rotation (deg), flags.</summary>
public sealed class VPiece
{
    public string Key; public float X, Y, Z, Sx, Sy, Sz; public float[] Rot; public int W = -1; public bool T, Tb, Door; public float Hp = 50; public int Gun;
    public Q4 Qr;
}
public sealed class VSeat { public float[] Hip, Foot, Hand; public float Lean; }
public sealed class VehicleDef { public string Kind; public float Hp, HitR, HitL, HitY, CamK, Scale = 1; public VSeat Seat; public float[][] Wheels; public VPiece[] Pieces; }
public sealed class VPieceState { public VPiece p; public float hp; public bool on = true; }
public sealed class Vehicle
{
    public string kind, team = "neutral", state; public VehicleDef def; public float x, y, z, px, py, pz, yaw, pyaw, pitch, ppitch, roll, proll, lean, speed, vx, vy, vz, wp, wr;
    public float hp, max; public Actor rider; public bool dead; public float wheel, flash; public (float x, float z, float yaw) home; public float crashT; public bool air, dashUsed;
    public VPieceState[] pieces; public float turretYaw, turretPitch, gunCD, boostCD, boostT, heat; public float[] susp = new float[4], suspV = new float[4]; public bool prevJump, prevRoll;
    public float[] wc, wv, wheelY; public float? lastTy, lastG; public float flipT, alt, beamAcc, respawnT, idleT, t, doorOpen, lx, lz; public bool over, unloaded; public int gunSide = 1;
    public List<float[]> beams; public Squad squad; public SquadComp comp;
    /// <summary>The virus chair's six planted feet (x, y, z each) and how far each is through a step.</summary>
    public float[] feet, stepT; public float legClock;
    public object vis;
}

public sealed partial class World
{
    public readonly List<Vehicle> vehicles = new();
    // piece: [shape+material, x, y, z, sx, sy, sz, rot[x,y,z] deg, flags]; material "T" = the team's colour
    // flags: w = wheel (spins), t = turret (yaws), tb = turret barrel (yaws and pitches), hp = hit points of the piece, door = bay door
    static VPiece Pc(string key, float x, float y, float z, float sx, float sy, float sz, float[] rot, float hp, int w = -1, bool t = false, bool tb = false, bool door = false, int gun = 0)
        => new() { Key = key, X = x, Y = y, Z = z, Sx = sx, Sy = sy, Sz = sz, Rot = rot, Hp = hp, W = w, T = t, Tb = tb, Door = door, Gun = gun };
    static float[] Rt(float x, float y, float z) => new[] { x, y, z };
    public static readonly Dictionary<string, VehicleDef> VDEF = BuildVdef();
    static Dictionary<string, VehicleDef> BuildVdef()
    {
        var d = new Dictionary<string, VehicleDef>
        {
            ["bike"] = new() { Kind = "bike", Hp = 160, HitR = 0.6f, HitL = 1.2f, HitY = 0.75f, CamK = 1.25f, Scale = 1.18f, Seat = new VSeat { Hip = new[] { 0.98f, 0.25f }, Foot = new[] { 0.36f, 0.38f, -0.55f }, Hand = new[] { 0.3f, 1.6f, -0.66f }, Lean = 0.2f },
                Pieces = new[] {
                    Pc("cu", 0, 0.42f, 0.85f, 0.84f, 0.84f, 0.34f, Rt(0, 90, 0), 40, w: 1), Pc("cc", 0, 0.42f, 0.85f, 0.36f, 0.36f, 0.36f, Rt(0, 90, 0), 30, w: 1),
                    Pc("cu", 0, 0.38f, -1.27f, 0.76f, 0.76f, 0.16f, Rt(0, 90, 0), 40, w: 1), Pc("cc", 0, 0.38f, -1.27f, 0.26f, 0.26f, 0.18f, Rt(0, 90, 0), 30, w: 1),
                    Pc("cc", 0.1f, 0.74f, -1.02f, 0.07f, 0.07f, 0.95f, Rt(-55, 0, 0), 30), Pc("cc", -0.1f, 0.74f, -1.02f, 0.07f, 0.07f, 0.95f, Rt(-55, 0, 0), 30),
                    Pc("bT", 0, 0.78f, -0.25f, 0.14f, 0.12f, 1.25f, Rt(8, 0, 0), 80), Pc("oT", 0, 1.0f, -0.42f, 0.44f, 0.34f, 0.85f, Rt(6, 0, 0), 50),
                    Pc("cc", 0, 0.66f, -0.12f, 0.22f, 0.22f, 0.5f, Rt(45, 0, 0), 40), Pc("cc", 0, 0.66f, 0.08f, 0.22f, 0.22f, 0.5f, Rt(-45, 0, 0), 40), Pc("bk", 0, 0.5f, 0, 0.36f, 0.3f, 0.5f, null, 60),
                    Pc("bk", 0, 0.9f, 0.34f, 0.38f, 0.1f, 0.55f, Rt(-6, 0, 0), 30), Pc("cc", 0, 1.22f, 0.74f, 0.05f, 0.05f, 0.62f, Rt(90, 0, 0), 20), Pc("bk", 0, 1.45f, 0.72f, 0.3f, 0.26f, 0.07f, null, 20),
                    Pc("cc", 0.26f, 1.33f, -0.72f, 0.05f, 0.05f, 0.52f, Rt(90, 0, 0), 20), Pc("cc", -0.26f, 1.33f, -0.72f, 0.05f, 0.05f, 0.52f, Rt(90, 0, 0), 20), Pc("cc", 0, 1.58f, -0.7f, 0.05f, 0.05f, 0.72f, Rt(0, 90, 0), 20),
                    Pc("og", 0, 1.08f, -0.94f, 0.24f, 0.24f, 0.15f, null, 15), Pc("cc", 0.22f, 0.42f, 0.45f, 0.11f, 0.11f, 1.2f, null, 25), Pc("cc", -0.22f, 0.42f, 0.45f, 0.11f, 0.11f, 1.2f, null, 25),
                    Pc("oT", 0, 0.78f, 0.86f, 0.42f, 0.24f, 0.9f, null, 30), Pc("oT", 0, 0.82f, -1.25f, 0.24f, 0.12f, 0.5f, null, 20), Pc("cc", 0, 0.35f, -0.55f, 0.05f, 0.05f, 0.85f, Rt(0, 90, 0), 20),
                } },
            // the rover is a red blood cell: a fat ring with a dimpled middle you sit in, hovering and sliding over the ground
            ["rover"] = new() { Kind = "rover", Hp = 420, HitR = 1.6f, HitL = 1.6f, HitY = 1.0f, CamK = 1.7f, Seat = new VSeat { Hip = new[] { 1.18f, 0.15f }, Foot = new[] { 0.32f, 0.95f, -0.75f }, Hand = new[] { 0.42f, 1.42f, -0.45f }, Lean = 0.1f },
                Pieces = new[] {
                    Pc("rR", 0, 1.0f, 0, 3.4f, 2.6f, 3.4f, null, 260), Pc("dP", 0, 0.98f, 0, 2.7f, 0.22f, 2.7f, null, 120), Pc("dP", 0, 0.9f, 0, 2.2f, 0.2f, 2.2f, null, 60),
                    Pc("oG", 1.45f, 1.05f, 0, 0.22f, 0.22f, 0.22f, null, 15), Pc("oG", -1.45f, 1.05f, 0, 0.22f, 0.22f, 0.22f, null, 15), Pc("oG", 0, 1.05f, -1.45f, 0.26f, 0.26f, 0.26f, null, 15),
                    Pc("oG", 1.02f, 1.05f, 1.02f, 0.2f, 0.2f, 0.2f, null, 15), Pc("oG", -1.02f, 1.05f, 1.02f, 0.2f, 0.2f, 0.2f, null, 15), Pc("oG", 1.02f, 1.05f, -1.02f, 0.2f, 0.2f, 0.2f, null, 15), Pc("oG", -1.02f, 1.05f, -1.02f, 0.2f, 0.2f, 0.2f, null, 15),
                    Pc("oP", 0, 0.62f, 0, 1.4f, 0.4f, 1.4f, null, 40),
                    Pc("oR", 0, 1.78f, 0.75f, 0.75f, 0.5f, 0.75f, null, 80), Pc("bT", 0, 2.02f, 0.75f, 0.6f, 0.34f, 0.8f, null, 80, t: true),
                    Pc("ck", 0.13f, 2.05f, 0.05f, 0.1f, 0.1f, 1.3f, null, 40, tb: true), Pc("ck", -0.13f, 2.05f, 0.05f, 0.1f, 0.1f, 1.3f, null, 40, tb: true), Pc("cy", 0, 2.05f, 0.5f, 0.42f, 0.24f, 0.12f, null, 30, tb: true),
                } },
            // the hover chair is a virus: a spiked capsid you ride on top of, held up by spider legs that dangle when it climbs
            ["chair"] = new() { Kind = "chair", Hp = 180, HitR = 0.8f, HitL = 0.5f, HitY = 0.9f, CamK = 1.25f, Seat = new VSeat { Hip = new[] { 1.42f, 0.05f }, Foot = new[] { 0.32f, 1.1f, -0.42f }, Hand = new[] { 0.45f, 1.5f, -0.3f }, Lean = -0.02f },
                Pieces = new[] {
                    Pc("iV", 0, 0.82f, 0, 1.25f, 1.15f, 1.25f, null, 80), Pc("oX", 0, 0.82f, 0, 0.9f, 0.85f, 0.9f, null, 40), Pc("dX", 0, 1.38f, 0.05f, 0.7f, 0.14f, 0.7f, null, 40),
                    Pc("pV", 0.62f, 1.02f, -0.18f, 0.2f, 0.2f, 0.5f, Rt(-20, -60, 0), 15), Pc("pV", -0.62f, 1.02f, -0.18f, 0.2f, 0.2f, 0.5f, Rt(-20, 60, 0), 15),
                    Pc("pV", 0.45f, 0.62f, 0.5f, 0.18f, 0.18f, 0.45f, Rt(30, -140, 0), 15), Pc("pV", -0.45f, 0.62f, 0.5f, 0.18f, 0.18f, 0.45f, Rt(30, 140, 0), 15), Pc("pV", 0, 1.25f, 0.62f, 0.18f, 0.18f, 0.45f, Rt(-40, 180, 0), 15),
                    Pc("cs", 0.55f, 1.02f, -0.42f, 0.16f, 0.16f, 0.62f, null, 40, gun: 1), Pc("cs", -0.55f, 1.02f, -0.42f, 0.16f, 0.16f, 0.62f, null, 40, gun: -1),
                    Pc("oG", 0.55f, 1.02f, -0.76f, 0.16f, 0.16f, 0.16f, null, 15), Pc("oG", -0.55f, 1.02f, -0.76f, 0.16f, 0.16f, 0.16f, null, 15),
                } },
            ["dropship"] = new() { Kind = "dropship", Hp = 1800, HitR = 2.8f, HitL = 5.8f, HitY = 0, CamK = 1,
                Pieces = new[] {
                    Pc("bq", 0, 0, 0, 4.0f, 2.6f, 11, null, 700), Pc("tq", 0, 0.4f, -6.4f, 3.6f, 2.2f, 2.4f, Rt(180, 0, 0), 200), Pc("og", 0, 1.1f, -5.4f, 1.6f, 0.9f, 2.4f, null, 80),
                    Pc("bq", 4.2f, 0.6f, 0.5f, 5.0f, 0.4f, 3.5f, null, 300), Pc("bq", -4.2f, 0.6f, 0.5f, 5.0f, 0.4f, 3.5f, null, 300),
                    Pc("ck", 5.8f, 0.2f, 1.0f, 1.4f, 1.4f, 3.2f, null, 200), Pc("ck", -5.8f, 0.2f, 1.0f, 1.4f, 1.4f, 3.2f, null, 200), Pc("co", 5.8f, 0.2f, 2.65f, 1.2f, 1.2f, 0.1f, null, 50), Pc("co", -5.8f, 0.2f, 2.65f, 1.2f, 1.2f, 0.1f, null, 50),
                    Pc("ck", 2.6f, -0.6f, 3.6f, 1.0f, 1.0f, 2.4f, null, 120), Pc("ck", -2.6f, -0.6f, 3.6f, 1.0f, 1.0f, 2.4f, null, 120), Pc("co", 2.6f, -0.6f, 4.85f, 0.85f, 0.85f, 0.1f, null, 30), Pc("co", -2.6f, -0.6f, 4.85f, 0.85f, 0.85f, 0.1f, null, 30),
                    Pc("bq", 1.3f, 2.0f, 4.6f, 0.3f, 2.4f, 2.4f, Rt(0, 0, 20), 100), Pc("bq", -1.3f, 2.0f, 4.6f, 0.3f, 2.4f, 2.4f, Rt(0, 0, -20), 100),
                    Pc("bk", 0, -1.4f, 1.0f, 3.0f, 0.15f, 5.0f, null, 150, door: true), Pc("ck", 0, -1.3f, -4.8f, 0.45f, 0.45f, 1.3f, null, 60, tb: true),
                    Pc("or", 2.0f, 0.5f, -4.0f, 0.3f, 0.3f, 0.3f, null, 10), Pc("or", -2.0f, 0.5f, -4.0f, 0.3f, 0.3f, 0.3f, null, 10), Pc("bk", 0, 1.45f, 0, 2.2f, 0.4f, 7, null, 150),
                } },
        };
        // a long, low chopper: everything a size up
        foreach (var D in d.Values)
        {
            float m = D.Scale; if (m == 1) continue;
            foreach (var p in D.Pieces) { p.X *= m; p.Y *= m; p.Z *= m; p.Sx *= m; p.Sy *= m; p.Sz *= m; }
            D.HitR *= m; D.HitL *= m; D.HitY *= m; var S = D.Seat; S.Hip = S.Hip.Select(v => v * m).ToArray(); S.Foot = S.Foot.Select(v => v * m).ToArray(); S.Hand = S.Hand.Select(v => v * m).ToArray();
        }
        return d;
    }
    public Vehicle MakeVehicle(string kind, float x, float z, float yaw, string team = null)
    {
        var D = VDEF[kind]; float y = kind == "dropship" ? 80 : GroundAt(x, z, 999, true).h;
        var Vh = new Vehicle { kind = kind, def = D, x = x, y = y, z = z, px = x, py = y, pz = z, yaw = yaw, pyaw = yaw, hp = D.Hp, max = D.Hp, team = team ?? "neutral", home = (x, z, yaw), pieces = D.Pieces.Select(p => new VPieceState { p = p, hp = p.Hp }).ToArray() };
        vehicles.Add(Vh); return Vh;
    }
    static readonly Dictionary<string, string> VEH_NAME = new() { ["bike"] = "CHOPPER", ["rover"] = "BLOOD CELL", ["chair"] = "VIRUS" };
    void Mount(Actor A, Vehicle Vh)
    {
        if (Vh.rider != null || Vh.dead || Vh.kind == "dropship") return;
        var P = A.P;
        DropHeld(A); P.rolling = false; P.charging = false; P.hover = false;
        if (Vh.crashT > 0 || MathF.Abs(Vh.roll) > 0.6f || MathF.Abs(Vh.pitch) > 0.6f) { Vh.crashT = 0; Vh.roll = 0; Vh.pitch = 0; Vh.wp = Vh.wr = 0; }   // set it back on its wheels
        Vh.rider = A; P.vehicle = Vh; P.x = Vh.x; P.y = Vh.y; P.z = Vh.z; P.facing = Vh.yaw; P.vx = P.vz = 0;
        Sfx("engine", Vh.x, Vh.y, Vh.z);
        if (A.human) ShowHint(VEH_NAME[Vh.kind] + " · INTERACT TO GET OFF" + (Vh.kind == "rover" ? " · ROLL TO BOOST · JUMP TO HOP" : Vh.kind == "bike" ? " · JUMP TWICE TO DASH" : " · RB / SPACE UP · LB / C DOWN"));
    }
    void Dismount(Actor A, bool forced = false)
    {
        var P = A.P; var Vh = P.vehicle; if (Vh == null) return;
        Vh.rider = null; P.vehicle = null;
        float rx = MathF.Cos(Vh.yaw), rz = -MathF.Sin(Vh.yaw), off = Vh.kind == "rover" ? 1.9f : 1.1f;
        if (!forced) { P.x = Vh.x + rx * off; P.z = Vh.z + rz * off; P.vx = P.vz = 0; P.y = MathF.Max(P.y, GroundAt(P.x, P.z, P.y + 3, true).h); }
        P.ground = false;
    }
    // the vehicle's orientation: yaw, then pitch (nose up +), then roll
    public static Q4 VehicleQuat(Vehicle Vh, Q4 o, float a = 1)
    {
        float yaw = Vh.pyaw + WrapAngle(Vh.yaw - Vh.pyaw) * a, pitch = Lerp(Vh.ppitch, Vh.pitch, a), roll = Lerp(Vh.proll, Vh.roll, a);
        float cy = MathF.Cos(yaw / 2), sy = MathF.Sin(yaw / 2), cp = MathF.Cos(pitch / 2), sp = MathF.Sin(pitch / 2), cr = MathF.Cos(roll / 2), sr = MathF.Sin(roll / 2);
        // q = qY * qX * qZ
        float x1 = cy * sp, y1 = sy * cp, z1 = -sy * sp, w1 = cy * cp;
        o.x = x1 * cr + y1 * sr; o.y = y1 * cr - x1 * sr; o.z = w1 * sr + z1 * cr; o.w = w1 * cr - z1 * sr;
        return o;
    }
    readonly Q4 VQ = new(); readonly V3 VV = new(), VO = new();
    V3 Vfwd(Vehicle Vh) { VehicleQuat(Vh, VQ); VV.Set(0, 0, -1); QRot(VQ, VV, VO); return VO; }
    // ---------- the rider drives (called from PlayerStep with the rider as the current actor)
    void VehicleStep(float dt, float m, float wx, float wz, bool jumpPress)
    {
        var Vh = P.vehicle;
        if (Vh.kind == "bike") BikeStep(dt, m, wx, wz, jumpPress);
        else if (Vh.kind == "rover") { RoverStep(Vh, dt, m, wx, wz, jumpPress, IN.roll && !Vh.prevRoll); Vh.prevRoll = IN.roll; }
        else if (Vh.kind == "chair") ChairStep(Vh, dt, m, wx, wz);
        P.facing = Vh.yaw; P.rolling = false; P.dashUsed = false;
    }
    void BikeStep(float dt, float m, float wx, float wz, bool jumpPress)
    {
        var Vh = P.vehicle; float fx = -MathF.Sin(Vh.yaw), fz = -MathF.Cos(Vh.yaw);
        Vh.ppitch = Vh.pitch; Vh.proll = Vh.roll;
        float want = m > 0.1f ? MathF.Atan2(-wx, -wz) : Vh.yaw, diff = WrapAngle(want - Vh.yaw);
        float inF = m > 0.1f ? MathF.Cos(diff) * m : 0, inS = m > 0.1f ? -MathF.Sin(diff) * m : 0;   // stick along and across the bike
        float sp = Vh.speed, top = 25 * SET.speed, acc = 12 * SET.speed;
        if (P.ground)
        {
            if (m > 0.1f) { if (MathF.Abs(diff) > 2.3f && sp > 2) sp -= 28 * dt; else sp = MathF.Min(top * m, sp + acc * m * dt * (1 - 0.45f * sp / top)); }
            else sp = MathF.Max(0, sp - 6 * dt);
            float tr = 2.8f - MathF.Min(1.7f, sp * 0.05f);
            if (m > 0.1f && MathF.Abs(diff) <= 2.3f) Vh.yaw += Clamp(diff, -tr * dt, tr * dt);
            // on the ground: lean into turns, pitch with the slope, and pop a wheelie when you open it up from slow
            float lean = m > 0.1f && MathF.Abs(diff) <= 2.3f ? -Clamp(diff, -1, 1) * MathF.Min(1, sp / 12) * 0.55f : 0;
            float slope = MathF.Atan2(-(P.nx * fx + P.nz * fz), P.ny);
            float wheelie = m > 0.7f && sp < 9 && inF > 0.6f ? 0.28f * (1 - sp / 9) : 0;
            Vh.roll += (lean - Vh.roll) * MathF.Min(1, dt * 6); Vh.pitch += (slope + wheelie - Vh.pitch) * MathF.Min(1, dt * 8);
            Vh.wp = 0; Vh.wr = 0; Vh.dashUsed = false;
        }
        else
        {
            // in the air the stick tips the bike: forward/back pitches, sideways rolls
            Vh.wp += (-inF * 5 - Vh.wp * 1.2f) * dt; Vh.wr += (-inS * 5 - Vh.wr * 1.2f) * dt;
            Vh.pitch += Vh.wp * dt; Vh.roll += Vh.wr * dt;
        }
        Vh.speed = sp;
        if (P.ground)
        {
            P.vx = fx * sp; P.vz = fz * sp; P.vy = -(P.nx * P.vx + P.nz * P.vz) / MathF.Max(0.3f, P.ny);
            if (jumpPress) { P.vy += 8.5f; P.ground = false; Vh.wp = 1.2f; Sfx("jump", P.x, P.y, P.z); }
        }
        else
        {
            if (jumpPress && !Vh.dashUsed)
            {   // jump dash: a burst forward in the air
                Vh.dashUsed = true; Vh.speed = sp = MathF.Min(top * 1.5f, sp + 13); P.vy = MathF.Max(P.vy, 3.5f);
                Sfx("dash", P.x, P.y, P.z); for (int i = 0; i < 6; i++) Puff(P.x - fx * 1.4f, P.y + 0.6f, P.z - fz * 1.4f, 1.5f, 1, 0.2f);
                Fx("flash", P.x - fx * 1.6f, P.y + 0.5f, P.z - fz * 1.6f, 1.2f, 0.12f);
            }
            P.vy = MathF.Max(P.vy - GRV * dt, -MAX_FALL);
            float along = MathF.Max(P.vx * fx + P.vz * fz, Vh.dashUsed ? sp * 0.98f : 0), k = MathF.Min(1, dt * 1.5f); P.vx += (fx * along - P.vx) * k; P.vz += (fz * along - P.vz) * k;
        }
        float sp0 = Hypot(P.vx, P.vy, P.vz); int ns = Math.Max(1, (int)MathF.Ceiling(sp0 * dt / 0.15f)); float h = dt / ns;
        bool landed = false; float vn = 0; bool wasAir = !P.ground;
        for (int i = 0; i < ns; i++) { if (P.ground) GroundMove(h); else { var r = AirMove(h); if (r != null) { landed = true; vn = MathF.Min(vn, r.Value); } } }
        if (landed && wasAir)
        {
            // a bad landing (nose or tail first, or on its side) throws the rider off
            float slope = MathF.Atan2(-(P.nx * fx + P.nz * fz), P.ny), dp = MathF.Abs(WrapAngle(Vh.pitch - slope)), dr = MathF.Abs(WrapAngle(Vh.roll));
            if (dp > 0.95f || dr > 1.0f) { CrashVehicle(Vh, CUR); return; }
            Vh.pitch = slope; Vh.roll = Clamp(WrapAngle(Vh.roll), -0.6f, 0.6f); Vh.wp = Vh.wr = 0;
            if (vn < -14) DamageVehicle(Vh, (-vn - 14) * 6, null);
        }
        Vh.speed = MathF.Min(Vh.speed, Hypot(P.vx, P.vz) + 0.5f);
        Vh.x = P.x; Vh.y = P.y; Vh.z = P.z; Vh.wheel += Vh.speed * dt / 0.4f;
        if (R() < Vh.speed / 80) Puff(Vh.x - fx * 1.4f, Vh.y + 0.35f, Vh.z - fz * 1.4f, 1, 0.6f, 0.12f);
    }
    // thrown off: the rider goes flying (a ragdoll for heroes), the machine tumbles and lies on its side
    void CrashVehicle(Vehicle Vh, Actor A)
    {
        var G = A.P; float fx = -MathF.Sin(Vh.yaw), fz = -MathF.Cos(Vh.yaw), sp = MathF.Max(Vh.speed, Hypot(G.vx, G.vz));
        Dismount(A, true); G.y += 0.6f;
        Knockdown(A, 1.6f, fx * sp * 0.8f, 5 + sp * 0.15f, fz * sp * 0.8f);
        Vh.crashT = 3; Vh.wr = (R() < 0.5f ? -1 : 1) * (4 + sp * 0.3f); Vh.wp = (R() - 0.5f) * 6; Vh.vy = 3; Vh.vx = fx * sp * 0.6f; Vh.vz = fz * sp * 0.6f;
        DamageVehicle(Vh, 20 + sp * 2, null);
        Sfx("slam", Vh.x, Vh.y, Vh.z, 1); for (int i = 0; i < 8; i++) Puff(Vh.x, Vh.y + 0.4f, Vh.z, 3, 2, 0.2f);
        if (A.human) ShowHint("WIPED OUT");
    }
    // ---------- rover: four sprung wheels, grip, a turret and a boost
    static readonly WeaponDef ROVER_GUN = new() { Id = "rovergun", Name = "ROVER CANNON", Type = "gun", Dmg = 14, Rate = 9, Spread = 0.025f, Speed = 170, Proj = "bullet", Snd = "boomshot" };
    // each wheel rides the ground on its own stiff spring, and the chassis floats on the four of them on softer springs,
    // so it sits tall, follows the terrain, and bounces and pitches over bumps
    const float ROVER_OFF = 0.45f, ROVER_WR = 0.55f;
    /// <summary>The red-cell rover: hovers a metre off the ground on a soft cushion, glides with little sideways grip
    /// (it drifts through turns), banks into them, and tilts with the slope. Jump hops it; boost shoves it forward.</summary>
    void RoverPhysics(Vehicle Vh, float dt, float thr, float steer, bool driven)
    {
        float cy = MathF.Cos(Vh.yaw), sy = MathF.Sin(Vh.yaw), fwx = -sy, fwz = -cy, rxx = cy, rxz = -sy;
        float hoverH = driven ? 1.05f : 0.35f, R2 = 1.5f;
        float gC = GroundAt(Vh.x, Vh.z, Vh.y + 2.5f, true).h;
        float gF = GroundAt(Vh.x + fwx * R2, Vh.z + fwz * R2, Vh.y + 2.5f, true).h, gB = GroundAt(Vh.x - fwx * R2, Vh.z - fwz * R2, Vh.y + 2.5f, true).h;
        float gR = GroundAt(Vh.x + rxx * R2, Vh.z + rxz * R2, Vh.y + 2.5f, true).h, gL = GroundAt(Vh.x - rxx * R2, Vh.z - rxz * R2, Vh.y + 2.5f, true).h;
        float g = MathF.Max(gC, (gF + gB + gL + gR) * 0.25f);
        float bob = MathF.Sin(simT * 2.1f + Vh.home.x) * 0.06f, target = g + hoverH + bob;
        if (Vh.air)
        {   // a hop: falls back onto its cushion
            Vh.vy -= GRV * 1.1f * dt; Vh.y += Vh.vy * dt;
            if (Vh.y <= target && Vh.vy <= 0) { Vh.air = false; if (Vh.vy < -14) DamageVehicle(Vh, (-Vh.vy - 14) * 4, null); Vh.vy *= 0.3f; }
        }
        else
        {
            float gv = Vh.lastG != null ? Clamp((g - Vh.lastG.Value) / dt, -25, 25) : 0;
            Vh.vy += ((target - Vh.y) * 48 - (Vh.vy - gv) * 8) * dt; Vh.y += Vh.vy * dt;
            if (Vh.y < g + 0.25f) { Vh.y = g + 0.25f; Vh.vy = MathF.Max(0, Vh.vy); }
        }
        Vh.lastG = g;
        // drive: forward thrust, a slippery sideways grip (it drifts), turning that works at any speed
        float hv = Vh.vx * fwx + Vh.vz * fwz, lv = Vh.vx * rxx + Vh.vz * rxz;
        float top = (driven ? 30 : 0) * SET.speed * (Vh.boostT > 0 ? 1.55f : 1), acc = (Vh.boostT > 0 ? 40 : 18) * SET.speed;
        float nhv = hv;
        if (MathF.Abs(thr) > 0.05f) nhv += Clamp(thr * top - hv, -acc * dt * 1.6f, acc * dt);
        else nhv -= MathF.Sign(hv) * MathF.Min(MathF.Abs(hv), (driven ? 4 : 10) * dt);
        nhv -= Clamp((gF - gB) / (2 * R2), -0.8f, 0.8f) * GRV * 0.35f * dt;   // slopes slow you going up
        float nlv = lv * MathF.Exp(-dt * (driven ? 1.6f : 4));
        Vh.vx = fwx * nhv + rxx * nlv; Vh.vz = fwz * nhv + rxz * nlv;
        float turn = steer * dt * (2.2f - MathF.Min(1.0f, MathF.Abs(nhv) * 0.03f));
        Vh.yaw += turn;
        // tilt: follow the slope, bank into the turn and the drift, nose up when accelerating
        float tp = MathF.Atan2(gF - gB, 2 * R2) + Clamp(-(nhv - hv) / dt * 0.004f, -0.12f, 0.12f), trr = MathF.Atan2(gR - gL, 2 * R2) - turn / dt * MathF.Min(1, MathF.Abs(nhv) / 20) * 0.12f + Clamp(nlv * 0.02f, -0.2f, 0.2f);
        Vh.wp += ((tp - Vh.pitch) * 30 - Vh.wp * 7) * dt; Vh.wr += ((trr - Vh.roll) * 30 - Vh.wr * 7) * dt;
        Vh.pitch += Vh.wp * dt; Vh.roll += Vh.wr * dt;
        Vh.x += Vh.vx * dt; Vh.z += Vh.vz * dt;
        VehicleWalls(Vh, 1.6f, 0.4f, 1.6f);
        Vh.speed = Hypot(Vh.vx, Vh.vz);
        Vh.wheel += Vh.speed * dt * 0.15f;   // the cell slowly turns as it slides
    }
    // circle-vs-statics for a chassis; smashes through building panels at speed
    void VehicleWalls(Vehicle Vh, float R2, float y0, float y1)
    {
        QueryStatics(Vh.x - R2 - 1, Vh.x + R2 + 1, Vh.z - R2 - 1, Vh.z + R2 + 1, QHs);
        foreach (var o in QHs.ToArray())
        {
            if (o.Kind == BOXK)
            {
                if (o.MaxY < Vh.y + y0 || o.MinY > Vh.y + y1) continue;
                float qx = Clamp(Vh.x, o.MinX, o.MaxX), qz = Clamp(Vh.z, o.MinZ, o.MaxZ), dx = Vh.x - qx, dz = Vh.z - qz, d = Hypot(dx, dz);
                if (d >= R2) continue;
                float vn = (dx * Vh.vx + dz * Vh.vz) / (d == 0 ? 1 : d), sp = Hypot(Vh.vx, Vh.vz);
                if (o.Chunk != null && sp > 7) { DamageChunk(o.Chunk, sp * 25, Vh.vx, 0, Vh.vz); if (!o.Chunk.alive) { Vh.vx *= 0.8f; Vh.vz *= 0.8f; continue; } }
                if (d < 1e-4f) continue;
                Vh.x = qx + dx / d * R2; Vh.z = qz + dz / d * R2;
                if (vn < 0) { Vh.vx -= dx / d * vn * 1.3f; Vh.vz -= dz / d * vn * 1.3f; if (-vn > 9) DamageVehicle(Vh, (-vn - 9) * 4, null); }
            }
            else if (o.Kind == CYLK)
            {
                if (o.Top < Vh.y + y0 || o.Bottom > Vh.y + y1) continue;
                float dx = Vh.x - o.X, dz = Vh.z - o.Z, d = Hypot(dx, dz), m = o.R + R2 * 0.7f;
                if (d < m && d > 1e-4f) { Vh.x = o.X + dx / d * m; Vh.z = o.Z + dz / d * m; float vn = (dx * Vh.vx + dz * Vh.vz) / d; if (vn < 0) { Vh.vx -= dx / d * vn * 1.4f; Vh.vz -= dz / d * vn * 1.4f; } }
            }
        }
    }
    void RoverStep(Vehicle Vh, float dt, float m, float wx, float wz, bool jumpPress, bool boostPress)
    {
        Vh.ppitch = Vh.pitch; Vh.proll = Vh.roll;
        float want = m > 0.1f ? MathF.Atan2(-wx, -wz) : Vh.yaw, diff = WrapAngle(want - Vh.yaw);
        float thr = m > 0.1f ? m : 0;
        if (MathF.Abs(diff) > 2.2f) { thr = -m * 0.6f; diff = WrapAngle(diff + PI); }   // stick pulled back: reverse
        float steer = Clamp(diff * 1.6f, -1, 1);
        Vh.boostCD -= dt; Vh.boostT -= dt;
        if (boostPress && Vh.boostCD <= 0) { Vh.boostCD = 5; Vh.boostT = 0.9f; var f = Vfwd(Vh); Vh.vx += f.x * 14; Vh.vz += f.z * 14; Sfx("launch", Vh.x, Vh.y, Vh.z, 1); if (CUR.human) ShowHint("BOOST"); }
        if (Vh.boostT > 0) { var f = Vfwd(Vh); for (int i = 0; i < 2; i++) Fx("flash", Vh.x - f.x * 2.2f + (R() - 0.5f) * 0.6f, Vh.y + 1, Vh.z - f.z * 2.2f, 1, 0.1f); }
        const int subs = 3; for (int i = 0; i < subs; i++) RoverPhysics(Vh, dt / subs, thr, steer, true);
        if (jumpPress && !Vh.air) { Vh.vy = 9; Vh.air = true; Vh.wp += 0.4f; Sfx("boing", Vh.x, Vh.y, Vh.z, 0.8f); }
        // flipped over: the driver is thrown out
        Vh.flipT = (MathF.Abs(Vh.roll) > 1.35f || MathF.Abs(Vh.pitch) > 1.4f) && !Vh.air ? Vh.flipT + dt : 0;
        if (Vh.flipT > 0.6f) { CrashVehicle(Vh, CUR); return; }
        // turret follows the aim; the trigger fires it
        float aimYaw = IN.aimYaw, aimPitch = AIM.ok ? MathF.Asin(Clamp(AIM.fy, -1, 1)) : 0;
        Vh.turretYaw = TurnAngle(Vh.turretYaw, WrapAngle(aimYaw - Vh.yaw), dt * 6); Vh.turretPitch += (Clamp(aimPitch, -0.3f, 0.6f) - Vh.turretPitch) * MathF.Min(1, dt * 8);
        Vh.gunCD -= dt;
        if (IN.fire && Vh.gunCD <= 0 && Vh.pieces.Any(q => q.p.Tb && q.on)) { Vh.gunCD = 1 / ROVER_GUN.Rate; TurretFire(Vh, ROVER_GUN); }
        P.x = Vh.x; P.y = Vh.y; P.z = Vh.z; P.vx = Vh.vx; P.vy = Vh.vy; P.vz = Vh.vz; P.ground = !Vh.air;
        RunOver(Vh, 2.0f);
    }
    void TurretFire(Vehicle Vh, WeaponDef def)
    {
        VehicleQuat(Vh, VQ);
        float ty = Vh.yaw + Vh.turretYaw, tp = Vh.turretPitch, fx = -MathF.Sin(ty) * MathF.Cos(tp), fy = MathF.Sin(tp), fz = -MathF.Cos(ty) * MathF.Cos(tp);
        VV.Set(0, 2.05f, 0.75f); QRot(VQ, VV, VO);
        Vh.gunSide = -Vh.gunSide; float side = Vh.gunSide * 0.13f;
        MUZ.x = Vh.x + VO.x + fx * 1.4f + MathF.Cos(ty) * side; MUZ.y = Vh.y + VO.y + fy * 1.4f; MUZ.z = Vh.z + VO.z + fz * 1.4f - MathF.Sin(ty) * side;
        // aim at what the crosshair is on, if anything
        float dx = fx, dy = fy, dz = fz;
        if (AIM.ok && CUR.human)
        {
            float d = AimDistance(150), tx = AIM.px + AIM.fx * d, ty2 = AIM.py + AIM.fy * d, tz = AIM.pz + AIM.fz * d, l = Hypot(tx - MUZ.x, ty2 - MUZ.y, tz - MUZ.z); if (l == 0) l = 1;
            dx = (tx - MUZ.x) / l; dy = (ty2 - MUZ.y) / l; dz = (tz - MUZ.z) / l;
        }
        var (x, y, z) = SpreadDir(new V3(dx, dy, dz), def.Spread);
        SpawnShot(def, def.Proj, x, y, z, def.Speed, def.Dmg);
        MuzzleFlash(MUZ.x, MUZ.y, MUZ.z, 1.3f); Sfx(def.Snd, MUZ.x, MUZ.y, MUZ.z, 0.8f); Noise(MUZ.x, MUZ.z, 50, CUR);
        if (CUR.human) Rumble(0.3f, 0.5f, 40);
    }
    // running into people at speed hurts them
    void RunOver(Vehicle Vh, float R2)
    {
        float sp = Vh.speed; if (sp < 6) return;
        foreach (var B in actors.ToArray())
        {
            if (B == Vh.rider || !B.P.alive || B.dormant || B.P.vehicle != null) continue;
            float dx = B.P.x - Vh.x, dz = B.P.z - Vh.z, d = Hypot(dx, dz);
            if (d > R2 + B.P.R || MathF.Abs(B.P.y - Vh.y) > 2) continue;
            if (B.hitByVeh != 0 && simT - B.hitByVeh < 0.5f) continue; B.hitByVeh = simT;
            DamageActor(B, sp * 2.2f, new Dmg { src = Vh.rider, kind = "impact", fx = Vh.vx, fz = Vh.vz, knock = sp * 0.8f, up = 5 });
            if (B.P.alive) Knockdown(B, 1, Vh.vx * 0.6f, 6, Vh.vz * 0.6f);
            Sfx("thud", B.P.x, B.P.y, B.P.z, 1); Vh.vx *= 0.92f; Vh.vz *= 0.92f;
        }
    }
    // ---------- hover chair: floats, slides any way you push, turns to your aim, two beams
    static readonly WeaponDef CHAIR_BEAM = new() { Id = "chairbeam", Name = "CHAIR BEAMS", Type = "beam", Dps = 30, Range = 70, Snd = "beam" };
    void ChairStep(Vehicle Vh, float dt, float m, float wx, float wz)
    {
        Vh.ppitch = Vh.pitch; Vh.proll = Vh.roll;
        float top = 13 * SET.speed, acc = 16, wl = Hypot(wx, wz); if (wl == 0) wl = 1;
        float tvx = m > 0.1f ? wx / wl * top * m : 0, tvz = m > 0.1f ? wz / wl * top * m : 0;
        Vh.vx += Clamp(tvx - Vh.vx, -acc * dt, acc * dt); Vh.vz += Clamp(tvz - Vh.vz, -acc * dt, acc * dt);
        Vh.yaw = TurnAngle(Vh.yaw, IN.aimYaw, dt * 6);
        // lean into the slide
        float fx = -MathF.Sin(Vh.yaw), fz = -MathF.Cos(Vh.yaw), af = Vh.vx * fx + Vh.vz * fz, asd = Vh.vx * -fz + Vh.vz * fx;
        Vh.pitch += (-af * 0.025f - Vh.pitch) * MathF.Min(1, dt * 5); Vh.roll += (-asd * 0.03f - Vh.roll) * MathF.Min(1, dt * 5);
        Vh.x += Vh.vx * dt; Vh.z += Vh.vz * dt;
        VehicleWalls(Vh, 0.8f, 0.3f, 1.6f);
        // altitude: RB (or Space) lifts, LB (or C / Shift) lowers; it holds that height over the ground or a roof
        Vh.alt = Clamp((Vh.alt == 0 ? 0.55f : Vh.alt) + ((IN.up ? 1 : 0) - (IN.down ? 1 : 0)) * 5 * dt, 0.55f, 14);
        float g = GroundAt(Vh.x, Vh.z, Vh.y + 1.2f, true).h, hover = Vh.alt + MathF.Sin(simT * 2.3f + Vh.home.x) * 0.06f * (1 + Vh.alt * 0.1f);
        float gv = Vh.lastG != null ? Clamp((g - Vh.lastG.Value) / dt, -20, 20) : 0; Vh.lastG = g;   // follow the ground's rise and fall
        Vh.vy += ((g + hover - Vh.y) * 110 - (Vh.vy - gv) * 16) * dt; Vh.y += Vh.vy * dt; if (Vh.y < g + 0.15f) { Vh.y = g + 0.15f; Vh.vy = MathF.Max(0, Vh.vy); }
        Vh.speed = Hypot(Vh.vx, Vh.vz);
        // the armrest beams
        Vh.heat = MathF.Max(0, Vh.heat - dt * (Vh.over ? 0.5f : 0.25f)); if (Vh.over && Vh.heat < 0.25f) Vh.over = false;
        Vh.beams = null;
        if (IN.fire && !Vh.over && Vh.pieces.Any(q => q.p.Gun != 0 && q.on)) { ChairBeams(Vh, dt); Vh.heat += dt * 0.28f; if (Vh.heat >= 1) { Vh.over = true; if (CUR.human) ShowHint("BEAMS OVERHEATED"); } }
        P.x = Vh.x; P.y = Vh.y; P.z = Vh.z; P.vx = Vh.vx; P.vy = 0; P.vz = Vh.vz; P.ground = true;
    }
    void ChairBeams(Vehicle Vh, float dt)
    {
        VehicleQuat(Vh, VQ); Vh.beams = new();
        foreach (var q in Vh.pieces)
        {
            if (q.p.Gun == 0 || !q.on) continue;
            VV.Set(q.p.X, q.p.Y, q.p.Z - 0.4f); QRot(VQ, VV, VO);
            float x0 = Vh.x + VO.x, y0 = Vh.y + VO.y, z0 = Vh.z + VO.z;
            float dx = AIM.ok ? AIM.fx : -MathF.Sin(Vh.yaw), dy = AIM.ok ? AIM.fy : 0, dz = AIM.ok ? AIM.fz : -MathF.Cos(Vh.yaw);
            if (AIM.ok)
            {
                float d = AimDistance(CHAIR_BEAM.Range), tx = AIM.px + AIM.fx * d, ty = AIM.py + AIM.fy * d, tz = AIM.pz + AIM.fz * d, l = Hypot(tx - x0, ty - y0, tz - z0); if (l == 0) l = 1;
                dx = (tx - x0) / l; dy = (ty - y0) / l; dz = (tz - z0) / l;
            }
            float end = CHAIR_BEAM.Range;
            for (float t = 0.5f; t < end; t += 0.6f) if (SolidAt(x0 + dx * t, y0 + dy * t, z0 + dz * t) != null) { end = t; if (SOLID_HIT?.Chunk != null) DamageChunk(SOLID_HIT.Chunk, CHAIR_BEAM.Dps * dt * 2, dx, dy, dz); break; }
            var man = HitMen(x0, y0, z0, x0 + dx * end, y0 + dy * end, z0 + dz * end, CUR.team, 0.05f);
            if (man != null)
            {
                end *= HITQ.t; Vh.beamAcc += dt;
                if (Vh.beamAcc > 0.1f) { Vh.beamAcc = 0; if (HITQ.veh != null) DamageVehicle(HITQ.veh, CHAIR_BEAM.Dps * 0.1f, CUR); else DamageActor((Actor)man, CHAIR_BEAM.Dps * 0.1f, new Dmg { src = CUR, kind = "bullet", part = HITQ.part, fx = dx, fz = dz, knock = 0.5f }); }
            }
            Vh.beams.Add(new[] { x0, y0, z0, x0 + dx * end, y0 + dy * end, z0 + dz * end });
        }
        if (R() < 0.2f) Sfx("beam", Vh.x, Vh.y + 1, Vh.z, 0.5f);
    }
    // ---------- damage: hits land on the nearest piece, which comes off when it's spent
    public void DamageVehicle(Vehicle Vh, float dmg, Actor src, float? hx = null, float? hy = null, float? hz = null)
    {
        if (Vh.dead || !(dmg > 0)) return;
        if (src != null && src.team == "hero" && (Vh.rider == null || Vh.rider.team != "hero")) dmg *= 2 * SET.dmgDealt;
        else if (Vh.rider != null && Vh.rider.team == "hero") dmg *= 0.5f * SET.dmgTaken;
        Vh.hp -= dmg; Vh.flash = 0.12f; Sfx("clank", Vh.x, Vh.y + 0.5f, Vh.z, 0.6f);
        // which piece?
        VPieceState best = null; float bd = 1e9f;
        if (hx == null) { var on = Vh.pieces.Where(q => q.on).ToArray(); if (on.Length > 0) best = on[(int)MathF.Floor(R() * on.Length)]; }
        else foreach (var q in Vh.pieces) { if (!q.on) continue; var w = PieceWorldV(Vh, q, 1); float d = (w.x - hx.Value) * (w.x - hx.Value) + (w.y - hy.Value) * (w.y - hy.Value) + (w.z - hz.Value) * (w.z - hz.Value); if (d < bd) { bd = d; best = q; } }
        if (best != null) { best.hp -= dmg; if (best.hp <= 0 && best != Vh.pieces[0]) DetachPiece(Vh, best, 1); HitSpark(hx ?? Vh.x, hy ?? Vh.y + 0.8f, hz ?? Vh.z, 0.8f); }
        if (Vh.hp <= 0) DestroyVehicle(Vh, src);
    }
    public struct PieceW { public float x, y, z; public Q4 q; }
    readonly Q4 PQL = new(), PQS = new(), PWQ = new(), TMPQ = new();
    // a piece's world transform (wheel spin, suspension, turret yaw and pitch, opening bay door)
    public PieceW PieceWorldV(Vehicle Vh, VPieceState q, float alpha)
    {
        var p = q.p;
        float lx = p.X, ly = p.Y, lz = p.Z;
        PQL.Identity();
        if (p.Rot != null) { p.Qr ??= QEulerDeg(p.Rot[0], p.Rot[1], p.Rot[2]); PQL.Copy(p.Qr); }
        if (p.W >= 0 && Vh.def.Wheels != null && Vh.wheelY != null) ly = Vh.wheelY[p.W];
        if (Vh.kind == "rover" && !p.T && !p.Tb) { float a = Vh.wheel; float c0 = MathF.Cos(a), s0 = MathF.Sin(a), nx = lx * c0 + lz * s0, nz = -lx * s0 + lz * c0; lx = nx; lz = nz; QMul(TMPQ.Set(0, MathF.Sin(a / 2), 0, MathF.Cos(a / 2)), PQL, PQL); }   // the cell's slow spin
        if (p.W >= 0) { float a = Vh.wheel / 2; QMul(TMPQ.Set(MathF.Sin(a), 0, 0, MathF.Cos(a)), PQL, PQL); }
        if (p.T || p.Tb)
        {   // about the turret pivot
            const float px = 0, pz = 0.75f; float cy = MathF.Cos(Vh.turretYaw), sy = MathF.Sin(Vh.turretYaw);
            float ox = lx - px, oy = ly - 2.0f, oz = lz - pz;
            if (p.Tb) { float cp = MathF.Cos(Vh.turretPitch), sp = MathF.Sin(Vh.turretPitch), ny = oy * cp - oz * sp, nz = oy * sp + oz * cp; oy = ny; oz = nz; QMul(TMPQ.Set(MathF.Sin(Vh.turretPitch / 2), 0, 0, MathF.Cos(Vh.turretPitch / 2)), PQL, PQL); }
            float rx2 = ox * cy + oz * sy, rz2 = -ox * sy + oz * cy; lx = px + rx2; ly = 2.0f + oy; lz = pz + rz2;
            QMul(TMPQ.Set(0, MathF.Sin(Vh.turretYaw / 2), 0, MathF.Cos(Vh.turretYaw / 2)), PQL, PQL);
        }
        if (p.Door && Vh.doorOpen > 0) { ly -= Vh.doorOpen * 0.8f; QMul(TMPQ.Set(MathF.Sin(Vh.doorOpen * 0.5f), 0, 0, MathF.Cos(Vh.doorOpen * 0.5f)), PQL, PQL); }
        VehicleQuat(Vh, PQS, alpha);
        VV.Set(lx, ly, lz); QRot(PQS, VV, VO);
        float x = Lerp(Vh.px, Vh.x, alpha), y = Lerp(Vh.py, Vh.y, alpha), z = Lerp(Vh.pz, Vh.z, alpha);
        return new PieceW { x = x + VO.x, y = y + VO.y, z = z + VO.z, q = QMul(PQS, PQL, new Q4()) };
    }
    static Q4 QEulerDeg(float rx, float ry, float rz)
    {
        float d = PI / 360, cx = MathF.Cos(rx * d), sx = MathF.Sin(rx * d), cy = MathF.Cos(ry * d), sy = MathF.Sin(ry * d), cz = MathF.Cos(rz * d), sz = MathF.Sin(rz * d);
        return new Q4(sx * cy * cz + cx * sy * sz, cx * sy * cz - sx * cy * sz, cx * cy * sz + sx * sy * cz, cx * cy * cz - sx * sy * sz);
    }
    public static string Vkey(Vehicle Vh, string k) => "w" + k[0] + (k[1] == 'T' ? (Vh.team == "enemy" || (Vh.rider != null && Vh.rider.team == "enemy") ? "e" : "h") : k[1].ToString());
    void DetachPiece(Vehicle Vh, VPieceState q, float burst)
    {
        if (!q.on) return;
        q.on = false;
        var w = PieceWorldV(Vh, q, 1); var p = q.p; char sh = p.Key[0];
        float dx = w.x - Vh.x, dz = w.z - Vh.z, dl = Hypot(dx, dz); if (dl == 0) dl = 1; float k = (3 + R() * 4) * burst;
        if (Vh.kind == "dropship" || CHIPS.Count < CHIP_MAX)
        {   // pieces fly as light particles
            string mk = Vkey(Vh, p.Key);
            AddChip(w.x, w.y, w.z, Vh.vx * 0.6f + dx / dl * k, 3 + R() * 4 * burst + MathF.Max(0, Vh.vy), Vh.vz * 0.6f + dz / dl * k, w.q, p.Sx, p.Sy, p.Sz, mk, 7 + R() * 3);
            for (int i = 0; i < 2; i++) Puff(w.x, w.y, w.z, 2, 1.5f, 0.15f);
            return;
        }
        var b = sh == 'o' ? MakeBody(SPH, w.x, w.y, w.z, 20, r: MathF.Max(0.08f, (p.Sx + p.Sy + p.Sz) / 6), e: 0.3f, mu: 0.7f, roll: 0.6f)
            : MakeBody(BOXB, w.x, w.y, w.z, MathF.Max(4, 300 * p.Sx * p.Sy * p.Sz), he: new V3(MathF.Max(0.05f, p.Sx / 2), MathF.Max(0.05f, p.Sy / 2), MathF.Max(0.05f, p.Sz / 2)), e: 0.2f, mu: 0.6f);
        b.Q.Copy(w.q); QNorm(b.Q); b.Pq.Copy(b.Q);
        b.Vel.x = Vh.vx * 0.6f + dx / dl * k; b.Vel.y = 3 + R() * 4 * burst + MathF.Max(0, Vh.vy); b.Vel.z = Vh.vz * 0.6f + dz / dl * k;
        b.W.x = (R() - 0.5f) * 12; b.W.y = (R() - 0.5f) * 12; b.W.z = (R() - 0.5f) * 12;
        b.IsDebris = true; b.Mk = Vkey(Vh, p.Key); b.Ell = new V3(p.Sx, p.Sy, p.Sz); b.Life = 7 + R() * 3;
        debris.Add(b); while (debris.Count > CHIP_MAX) RemoveDebrisBody(debris[0]);
        for (int i = 0; i < 3; i++) Puff(w.x, w.y, w.z, 2, 1.5f, 0.15f);
    }
    void DestroyVehicle(Vehicle Vh, Actor src)
    {
        if (Vh.dead) return;
        Vh.dead = true;
        foreach (var q in Vh.pieces) DetachPiece(Vh, q, Vh.kind == "dropship" ? 2.5f : 1.6f);
        var Rr = Vh.rider;
        if (Rr != null) { Dismount(Rr, true); Rr.P.vy = 7; Knockdown(Rr, 1.2f, -MathF.Sin(Vh.yaw) * Vh.speed * 0.5f, 7, -MathF.Cos(Vh.yaw) * Vh.speed * 0.5f); }
        Explode(Vh.x, Vh.y + 0.6f, Vh.z, Vh.kind == "dropship" ? 9 : Vh.kind == "rover" ? 5 : 3.5f, Vh.kind == "dropship" ? 2500 : 900, Vh.kind == "dropship" ? 80 : 30, src);
        if (Vh.kind == "dropship")
        {
            for (int i = 0; i < 4; i++) Boom(Vh.x + (R() - 0.5f) * 8, Vh.y + (R() - 0.5f) * 3, Vh.z + (R() - 0.5f) * 8, 5);
            if (Vh.squad != null && !Vh.unloaded) { Vh.squad.dropping = false; Vh.squad.ship = null; Vh.squad.respawnT = 60; if (src != null && src.team == "hero") ShowHint("DROPSHIP DOWN"); }
        }
        Vh.respawnT = 45;
    }
    // ---------- every step: riderless machines coast, crashed ones tumble, parked ones come back, dropships fly
    void StepVehicles(float dt)
    {
        for (int i = vehicles.Count - 1; i >= 0; i--)
        {
            if (i >= vehicles.Count) continue;
            var Vh = vehicles[i];
            Vh.px = Vh.x; Vh.py = Vh.y; Vh.pz = Vh.z; Vh.pyaw = Vh.yaw; if (Vh.flash > 0) Vh.flash -= dt;
            if (Vh.dead)
            {
                if (Vh.team == "neutral" && (Vh.respawnT -= dt) <= 0 && Hypot(HUMAN.P.x - Vh.home.x, HUMAN.P.z - Vh.home.z) > 40)
                {
                    Vh.dead = false; Vh.hp = Vh.max; foreach (var q in Vh.pieces) { q.on = true; q.hp = q.p.Hp; }
                    Vh.x = Vh.home.x; Vh.z = Vh.home.z; Vh.yaw = Vh.home.yaw; Vh.speed = 0; Vh.vx = Vh.vz = Vh.vy = 0; Vh.pitch = Vh.roll = 0; Vh.crashT = 0; Vh.y = GroundAt(Vh.x, Vh.z, 999, true).h;
                }
                else if (Vh.kind == "dropship" || Vh.team == "enemy") { if ((Vh.respawnT -= dt) < 30) vehicles.RemoveAt(i); }
                continue;
            }
            if (Vh.kind == "dropship") { StepDropship(Vh, dt); continue; }
            if (Vh.rider != null) { Vh.idleT = 0; continue; }
            // empty enemy machines far from you are cleared away after a while
            if (Vh.team == "enemy" && (Vh.idleT += dt) > 40 && Hypot(HUMAN.P.x - Vh.x, HUMAN.P.z - Vh.z) > 90) { vehicles.RemoveAt(i); continue; }
            Vh.ppitch = Vh.pitch; Vh.proll = Vh.roll;
            if (Vh.kind == "rover") { for (int k = 0; k < 2; k++) RoverPhysics(Vh, dt / 2, 0, 0, false); Vh.ppitch = Vh.pitch; continue; }
            if (Vh.kind == "chair")
            {   // an empty chair sinks to rest on the ground
                Vh.alt = 0.55f;
                float g0 = GroundAt(Vh.x, Vh.z, Vh.y + 1, true).h; Vh.y += (g0 + 0.3f - Vh.y) * MathF.Min(1, dt * 3); Vh.vx *= MathF.Exp(-dt * 3); Vh.vz *= MathF.Exp(-dt * 3); Vh.x += Vh.vx * dt; Vh.z += Vh.vz * dt; Vh.pitch *= 0.95f; Vh.roll *= 0.95f; continue;
            }
            // bikes
            if (Vh.crashT > 0)
            {   // tumbling across the ground after a crash
                Vh.crashT -= dt; Vh.x += Vh.vx * dt; Vh.z += Vh.vz * dt; Vh.vx *= MathF.Exp(-dt * 1.5f); Vh.vz *= MathF.Exp(-dt * 1.5f);
                Vh.vy -= GRV * dt; Vh.y += Vh.vy * dt; float g1 = GroundAt(Vh.x, Vh.z, Vh.y + 1, true).h; if (Vh.y < g1) { Vh.y = g1; Vh.vy = MathF.Abs(Vh.vy) * 0.3f; Vh.wr *= 0.7f; Vh.wp *= 0.7f; }
                Vh.roll += Vh.wr * dt; Vh.pitch += Vh.wp * dt;
                if (Vh.crashT <= 0) { float wr = WrapAngle(Vh.roll); Vh.roll = (wr < 0 ? -1 : 1) * 1.35f; Vh.pitch = 0; Vh.speed = 0; }
                VehicleWalls(Vh, 0.6f, 0.2f, 1.2f); continue;
            }
            Vh.lean *= MathF.Exp(-4 * dt);
            if (Vh.speed > 0.05f)
            {
                Vh.speed = MathF.Max(0, Vh.speed - 9 * dt);
                float nx = Vh.x - MathF.Sin(Vh.yaw) * Vh.speed * dt, nz = Vh.z - MathF.Cos(Vh.yaw) * Vh.speed * dt;
                if (InsideStatic(nx, nz, Vh.y, 0.5f)) Vh.speed = 0; else { Vh.x = nx; Vh.z = nz; }
                Vh.wheel += Vh.speed * dt / 0.4f;
            }
            float g = GroundAt(Vh.x, Vh.z, Vh.y + 1, true).h; Vh.vy -= PGRAV * dt; Vh.y += Vh.vy * dt; if (Vh.y <= g) { Vh.y = g; Vh.vy = 0; }
        }
    }
    // ---------- dropships: fly in high, come down over the drop point, open up, deliver, and leave
    public Vehicle CallDropship(Squad S, float x, float z, SquadComp comp)
    {
        float lx = x, lz = z; int k = 0;
        while (InsideStatic(lx, lz, GroundY(lx, lz), 5) && k++ < 14) { lx += (R() - 0.5f) * 16; lz += (R() - 0.5f) * 16; }
        float a = R() * TAU, sx = Clamp(lx + MathF.Cos(a) * 260, -HALF + 20, HALF - 20), sz = Clamp(lz + MathF.Sin(a) * 260, -HALF + 20, HALF - 20);
        var Vh = MakeVehicle("dropship", sx, sz, MathF.Atan2(-(lx - sx), -(lz - sz)), "enemy");
        Vh.y = Vh.py = GroundY(lx, lz) + 70; Vh.state = "approach"; Vh.lx = lx; Vh.lz = lz; Vh.squad = S; Vh.comp = comp; Vh.t = 0; Vh.doorOpen = 0; Vh.unloaded = false;
        return Vh;
    }
    static readonly WeaponDef DROP_GUN = new() { Id = "dropgun", Name = "DROPSHIP GUN", Type = "gun", Dmg = 7, Rate = 6, Spread = 0.06f, Speed = 120, Proj = "bullet", Snd = "eshot2" };
    void StepDropship(Vehicle Vh, float dt)
    {
        Vh.t += dt; Vh.ppitch = Vh.pitch; Vh.proll = Vh.roll;
        float g = GroundY(Vh.lx, Vh.lz), dx = Vh.lx - Vh.x, dz = Vh.lz - Vh.z, d = Hypot(dx, dz);
        if (Vh.state == "approach")
        {
            float sp = MathF.Min(32, 6 + d * 0.4f); Vh.yaw = TurnAngle(Vh.yaw, MathF.Atan2(-dx, -dz), dt * 1.2f);
            Vh.x += -MathF.Sin(Vh.yaw) * sp * dt; Vh.z += -MathF.Cos(Vh.yaw) * sp * dt; Vh.y += (g + 30 - Vh.y) * MathF.Min(1, dt * 0.5f);
            Vh.pitch = -0.12f * MathF.Min(1, sp / 20); if (d < 4) Vh.state = "descend";
        }
        else if (Vh.state == "descend")
        {
            Vh.x += dx * dt; Vh.z += dz * dt; Vh.y += (g + 5 - Vh.y) * MathF.Min(1, dt * 0.9f); Vh.pitch *= 0.95f;
            if (Vh.y < g + 6) { Vh.state = "unload"; Vh.t = 0; Sfx("alert", Vh.x, Vh.y, Vh.z, 1); }
        }
        else if (Vh.state == "unload")
        {
            Vh.doorOpen = MathF.Min(1, Vh.doorOpen + dt * 1.5f); Vh.y += (g + 5 - Vh.y) * MathF.Min(1, dt) + MathF.Sin(simT * 2) * 0.01f;
            if (Vh.t > 1 && !Vh.unloaded)
            {
                Vh.unloaded = true;
                var S = Vh.squad;
                if (S != null)
                {
                    S.dropping = false; S.ship = null; S.x = Vh.x; S.z = Vh.z; if (Vh.comp != null) S.comp = Vh.comp; SpawnSquadMembers(S);
                    var f = Vfwd(Vh); float ffx = f.x, ffz = f.z;
                    foreach (var A in S.members)
                    {
                        var G = A.P; float along = (R() - 0.3f) * 4, across = (R() - 0.5f) * 2.4f; if (G.vehicle != null) continue;
                        G.x = Vh.x + ffx * along - ffz * across; G.z = Vh.z + ffz * along + ffx * across; G.y = Vh.y - 1.6f; G.px = G.x; G.py = G.y; G.pz = G.z; G.ground = false; G.vy = -2; G.vx = (R() - 0.5f) * 3; G.vz = (R() - 0.5f) * 3;
                    }
                    if (S.kind == "wave") { S.state = "investigate"; S.investigate = (HUMAN.P.x, HUMAN.P.z); S.searchT = 30; }
                }
            }
            if (Vh.t > 4) { Vh.state = "leave"; Vh.t = 0; }
        }
        else
        {
            Vh.doorOpen = MathF.Max(0, Vh.doorOpen - dt); Vh.vy = MathF.Min(14, Vh.vy + dt * 6); Vh.y += Vh.vy * dt;
            if (Vh.y > g + 25) { Vh.x += -MathF.Sin(Vh.yaw) * 30 * dt; Vh.z += -MathF.Cos(Vh.yaw) * 30 * dt; Vh.pitch = -0.15f; }
            if (Vh.t > 18 || MathF.Abs(Vh.x) > HALF + 100 || MathF.Abs(Vh.z) > HALF + 100) { vehicles.Remove(Vh); return; }
        }
        // the chin gun rakes the nearest hero while it's low
        if ((Vh.state == "descend" || Vh.state == "unload") && Vh.pieces.Any(q => q.p.Tb && q.on))
        {
            Vh.gunCD -= dt;
            Actor T = null; float bd = 90; foreach (var A in actors) if (A.team == "hero" && A.P.alive) { float dd = Hypot(A.P.x - Vh.x, A.P.z - Vh.z); if (dd < bd) { bd = dd; T = A; } }
            if (T != null && Vh.gunCD <= 0)
            {
                Vh.gunCD = R() < 0.15f ? 1.2f : 1 / DROP_GUN.Rate;
                VehicleQuat(Vh, VQ); VV.Set(0, -1.3f, -5.4f); QRot(VQ, VV, VO);
                float mx = Vh.x + VO.x, my = Vh.y + VO.y, mz = Vh.z + VO.z;
                float tx = T.P.x - mx, ty = T.P.y + 1 - my, tz = T.P.z - mz, l = Hypot(tx, ty, tz); if (l == 0) l = 1;
                WithActor(DropActor(), () => { MUZ.Set(mx, my, mz); var (x, y, z) = SpreadDir(new V3(tx / l, ty / l, tz / l), DROP_GUN.Spread); SpawnShot(DROP_GUN, "bullet", x, y, z, DROP_GUN.Speed, DROP_GUN.Dmg); });
                MuzzleFlash(mx, my, mz, 1.2f); Sfx("eshot2", mx, my, mz, 0.7f);
            }
        }
        if (R() < 0.5f) foreach (var sgn in new[] { -1f, 1f }) { VehicleQuat(Vh, VQ); VV.Set(5.8f * sgn, 0.2f, 2.8f); QRot(VQ, VV, VO); Fx("flash", Vh.x + VO.x, Vh.y + VO.y, Vh.z + VO.z, 1.4f, 0.08f); }
        if (R() < 0.05f) Sfx("engine", Vh.x, Vh.y, Vh.z, 0.8f);
    }
    // a stand-in shooter for the dropship's gun (an enemy that is never drawn)
    Actor dropActor;
    Actor DropActor() { if (dropActor != null) return dropActor; var A = MakeActor("normal", 0, 0); actors.Remove(A); A.P.alive = true; dropActor = A; return A; }
}

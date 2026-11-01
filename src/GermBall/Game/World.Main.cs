using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Core;
using GermBall.Platform;
using GermBall.Render;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed partial class Actor { public bool parked; public Actor inMecha; }

/// <summary>Best wave scores per map (Documents\My Games\GermBall\best.json).</summary>
public static class Saves
{
    static Dictionary<string, int> best;
    static Dictionary<string, int> All => best ??= Paths.Load<Dictionary<string, int>>("best.json") ?? new();
    public static int Best(string map) => All.TryGetValue(map ?? "", out var v) ? v : 0;
    public static void SetBest(string map, int v) { All[map ?? ""] = v; Paths.Save("best.json", All); }
}

public sealed partial class World
{
    // =====================================================================
    // The player, the camera, input and the fixed-step loop
    // =====================================================================
    public Actor HUMAN, ME; Actor pendingMe;
    public bool playing;
    public readonly Camera camera = new();
    public sealed class CamState { public float yaw, pitch = 0.3f, dist = 6, cur = 6, tx, ty, tz, lastLook = -10, shake, aim, ads; }
    public readonly CamState cam = new();
    readonly V3 RP = new();
    float fpEye = 1.7f;
    public string hintText = ""; public float hintT;
    public float hurtA;
    public Body holoBody;
    float accum;
    public readonly PerfState PERF = new();
    public sealed class PerfState { public float ema = 1; public bool odd; }
    public int chipMaxDyn = 240;

    public void Sfx(string name, float x, float y, float z, float vol = 1) { if (playing) Sound.Play(name, x, y, z, vol); }
    public void Rumble(float strong, float weak, float ms) { if (CUR != null && !CUR.human) return; Gamepad.Rumble(strong, weak, ms / 1000f); }
    public void ShowHint(string msg) { hintText = msg; hintT = 2.8f; }
    public void HudHurt(float k) { hurtA = MathF.Max(hurtA, k); }

    // ---- the controller, read the way the original read the Gamepad API
    public sealed class PadState
    {
        public bool on; public float lx, ly, rx, ry, lt, rt; public bool a, b, x, y, lb, rb, back, start, rs;
        public bool pa, pb, px, py, plb, prb, pback, pstart, prs;
    }
    public readonly PadState pad = new();
    public bool padActive, padArmed; bool assistOn;
    static (float, float) Deadzone(float x, float y, float d) { float m = Hypot(x, y); if (m < d) return (0, 0); float k = MathF.Min(1, (m - d) / (1 - d)) / m; return (x * k, y * k); }
    public void PollPad()
    {
        var p = pad; p.pa = p.a; p.pb = p.b; p.px = p.x; p.py = p.y; p.plb = p.lb; p.prb = p.rb; p.pback = p.back; p.pstart = p.start; p.prs = p.rs;
        var g = Gamepad.First;
        if (g == null) { if (p.on) ShowHint("Controller disconnected"); p.on = false; p.lx = p.ly = p.rx = p.ry = p.lt = p.rt = 0; p.a = p.b = p.x = p.y = p.lb = p.rb = p.back = p.start = p.rs = false; return; }
        if (!p.on) ShowHint("Controller connected");
        p.on = true;
        var (lx, ly) = Deadzone(g.Axes[0], g.Axes[1], 0.2f);
        if (g.B(14)) lx = -1; if (g.B(15)) lx = 1; if (g.B(12)) ly = -1; if (g.B(13)) ly = 1;
        float l = Hypot(lx, ly); if (l > 1) { lx /= l; ly /= l; }
        p.lx = lx; p.ly = ly; (p.rx, p.ry) = Deadzone(g.Axes[2], g.Axes[3], 0.15f);
        p.a = g.B(0); p.b = g.B(1); p.x = g.B(2); p.y = g.B(3); p.lb = g.B(4); p.rb = g.B(5); p.lt = g.Value[6]; p.rt = g.Value[7]; p.back = g.B(8); p.start = g.B(9); p.rs = g.B(11);
        if (Hypot(lx, ly) > 0.3f || Hypot(p.rx, p.ry) > 0.2f || p.a || p.rt > 0.3f || p.lt > 0.3f) padActive = true;
    }
    public bool PadHit(string k) => k switch
    {
        "a" => pad.a && !pad.pa, "b" => pad.b && !pad.pb, "x" => pad.x && !pad.px, "y" => pad.y && !pad.py, "lb" => pad.lb && !pad.plb, "rb" => pad.rb && !pad.prb,
        "back" => pad.back && !pad.pback, "start" => pad.start && !pad.pstart, "rs" => pad.rs && !pad.prs, _ => false
    };

    // ---- key bindings
    public bool Act(string a) => BINDS.TryGetValue(a, out var b) && (Input.Down(b[0]) || Input.Down(b[1]));
    public bool ActHit(string a) => BINDS.TryGetValue(a, out var b) && (Input.Pressed(b[0]) || Input.Pressed(b[1]));

    // ---- mecha piloting
    public Actor SpawnMecha(float x, float z, float facing)
    {
        var M = MakeActor("mecha", x, z, false, facing);
        M.dormant = true; M.parked = true;
        return M;
    }
    void SetMe(Actor A) { ME = A; P = A.P; PBODY = A.body; W = A.W; AIM = A.AIM; IN = A.IN; CUR = A; }
    public bool BoardMecha(Actor A, Actor M)
    {
        if (!A.human || M.pilotBy != null || !M.P.alive) return false;
        DropHeld(A); if (A.P.vehicle != null) Dismount(A);
        M.pilotBy = A; A.inMecha = M; A.hidden = true; A.dormant = true; A.human = false;
        M.human = true; M.dormant = false; M.parked = false; M.IN = new InputState(); M.P.prevUse = true;   // the held button mustn't climb straight back out
        pendingMe = M;
        Sfx("powerup", M.P.x, M.P.y + 2, M.P.z, 1); Rumble(0.6f, 0.4f, 200);
        ShowHint("BATTLE MECHA · CURL TO ROLL · INTERACT TO CLIMB OUT");
        return true;
    }
    public void UnboardMecha(Actor M, bool forced = false)
    {
        var A = M.pilotBy; if (A == null) return;
        M.pilotBy = null; M.human = false; A.inMecha = null; A.hidden = false; A.dormant = false; A.human = true;
        M.IN = new InputState(); A.P.prevUse = true;
        var G = M.P; float rx = MathF.Cos(G.facing), rz = -MathF.Sin(G.facing); var Q = A.P;
        Q.x = G.x + rx * (G.R + 1.4f); Q.z = G.z + rz * (G.R + 1.4f); Q.y = MathF.Max(GroundAt(Q.x, Q.z, G.y + 3, true).h, G.y + (forced ? 2 : 0));
        Q.px = Q.x; Q.py = Q.y; Q.pz = Q.z; Q.vx = G.vx; Q.vz = G.vz; Q.vy = 0; Q.facing = G.facing; Q.ground = false;
        if (forced) { Q.vy = 9; Knockdown(A, 1.2f, rx * 6, 9, rz * 6); }
        else { M.dormant = true; M.parked = true; G.vx = G.vz = G.vy = 0; G.rolling = false; G.charging = false; }
        pendingMe = A;
        if (A.P.alive) ShowHint(forced ? "EJECTED" : "OUT OF THE MECHA");
    }
    void StepPiloting()
    {
        var M = HUMAN.inMecha;
        if (M != null) { var Q = HUMAN.P; Q.x = Q.px = M.P.x; Q.y = Q.py = M.P.y + M.P.H * 0.6f; Q.z = Q.pz = M.P.z; Q.vx = M.P.vx; Q.vz = M.P.vz; Q.facing = M.P.facing; }
        if (pendingMe != null) { SetMe(pendingMe); pendingMe = null; }
    }
    public void BackToBase()
    {
        if (!HUMAN.P.alive) return;
        if (ME.pilotBy != null) { UnboardMecha(ME); SetMe(HUMAN); pendingMe = null; }
        RespawnActor(HUMAN); ShowHint("BACK AT " + PADS[0].Name);
    }

    // =====================================================================
    // Camera: spring-follow with an obstruction probe, ADS over the shoulder, or first person from the eyes
    // =====================================================================
    readonly List<Static> QC = new();
    bool CamBlocked(float x, float y, float z)
    {
        if (y < TerrainH(x, z) + 0.35f) return true;
        QueryStatics(x - 0.3f, x + 0.3f, z - 0.3f, z + 0.3f, QC);
        foreach (var o in QC)
        {
            if (o.Kind == BOXK) { if (x > o.MinX - 0.25f && x < o.MaxX + 0.25f && z > o.MinZ - 0.25f && z < o.MaxZ + 0.25f && y > o.MinY - 0.25f && y < o.MaxY + 0.25f) return true; }
            else if (o.Kind == CYLK) { if (y > o.Bottom && y < o.Top + 0.3f && Hypot(x - o.X, z - o.Z) < o.R + 0.25f) return true; }
        }
        return false;
    }
    float PitchMin => SET.fp ? -1.5f : -1.25f; float PitchMax => SET.fp ? 1.5f : 1.48f;
    readonly Random camRnd = new();
    float Jit() => (float)camRnd.NextDouble() - 0.5f;

    void SetFov(float fov, float fdt) => camera.Fov += (fov - camera.Fov) * MathF.Min(1, fdt * 4);

    void UpdateCameraFP(float fdt)
    {
        float sp = WithActor(ME, () => SpeedOf());
        cam.ads += ((P.ads ? 1 : 0) - cam.ads) * MathF.Min(1, fdt * 9); cam.aim = cam.ads;
        fpEye += ((P.rolling ? P.BR * 1.5f : P.grabbed != null ? P.H * 0.4f : P.H * 0.93f) - fpEye) * MathF.Min(1, fdt * 10);
        float ex = RP.x, ey = RP.y + fpEye, ez = RP.z;
        cam.tx = ex; cam.ty = ey; cam.tz = ez;
        float cp = MathF.Cos(cam.pitch), fx = -MathF.Sin(cam.yaw) * cp, fy = -MathF.Sin(cam.pitch), fz = -MathF.Cos(cam.yaw) * cp;
        float shk = cam.shake; cam.shake = MathF.Max(0, shk - fdt * 1.6f);
        camera.Position = new Vector3(ex + Jit() * shk * 0.3f, ey + Jit() * shk * 0.3f, ez + Jit() * shk * 0.3f);
        camera.LookAt(new Vector3(ex + fx * 10, ey + fy * 10, ez + fz * 10));
        AIM.px = ex; AIM.py = ey; AIM.pz = ez; AIM.fx = fx; AIM.fy = fy; AIM.fz = fz; AIM.ok = true;
        SetFov(Lerp(SET.fov + MathF.Min(18, MathF.Max(0, sp - TOP) * 0.7f), SET.fov * 0.62f, cam.ads), fdt);
    }

    void UpdateCamera(float fdt, float t)
    {
        if (SET.fp && P.alive) { UpdateCameraFP(fdt); return; }
        float hs = Hypot(P.vx, P.vz), sp = Hypot(P.vx, P.vy, P.vz); bool manual = t - cam.lastLook < 3;
        float lx = P.vx * 0.1f, lz = P.vz * 0.1f, ll = Hypot(lx, lz); if (ll > 1.6f) { lx *= 1.6f / ll; lz *= 1.6f / ll; }
        cam.aim += ((W.aimT > 0 || P.ads ? 1 : 0) - cam.aim) * MathF.Min(1, fdt * 6);
        cam.ads += ((P.ads ? 1 : 0) - cam.ads) * MathF.Min(1, fdt * 9);
        // over the right shoulder, always (a little further over when aiming down sights)
        float sh = (0.78f + 0.18f * cam.ads) * MathF.Max(1, P.s * 0.9f) * (P.vehicle != null ? 0.6f : 1), sx = MathF.Cos(cam.yaw) * sh, sz = -MathF.Sin(cam.yaw) * sh;
        if (cam.ads > 0.5f) { lx *= 0.2f; lz *= 0.2f; }
        float dx = RP.x + lx + sx, dy = RP.y + P.H * 0.92f + 0.3f - 0.15f * cam.ads, dz = RP.z + lz + sz;
        float kh = 1 - MathF.Exp(-11 * fdt), kv = 1 - MathF.Exp(-6 * fdt);
        cam.tx += (dx - cam.tx) * kh; cam.tz += (dz - cam.tz) * kh; cam.ty += (dy - cam.ty) * kv;
        float ex = cam.tx - dx, ez = cam.tz - dz, el = Hypot(ex, ez);
        if (el > 25) { cam.tx = dx; cam.tz = dz; cam.ty = dy; }
        else if (el > 1.2f) { cam.tx = dx + ex / el * 1.2f; cam.tz = dz + ez / el * 1.2f; }
        if (MathF.Abs(cam.ty - dy) > 1.8f) cam.ty = dy + MathF.Sign(cam.ty - dy) * 1.8f;
        // the camera only turns when you turn it (the browser build swung it round behind you as you ran)
        float camK = P.vehicle?.def != null ? P.vehicle.def.CamK : 1;
        cam.dist += (Lerp((3.7f * P.s + MathF.Min(2.2f, sp * 0.06f)) * camK, 2.1f * P.s * camK, cam.ads) - cam.dist) * MathF.Min(1, fdt * (P.ads ? 10 : 2.5f));
        float cp = MathF.Cos(cam.pitch), ox = MathF.Sin(cam.yaw) * cp, oy = MathF.Sin(cam.pitch), oz = MathF.Cos(cam.yaw) * cp;
        float free = cam.dist;
        for (float f = 0.4f; f <= cam.dist + 0.3f; f += 0.2f) if (CamBlocked(cam.tx + ox * f, cam.ty + oy * f, cam.tz + oz * f)) { free = MathF.Max(0.5f, f - 0.45f); break; }
        if (free < cam.cur && CamBlocked(cam.tx + ox * cam.cur, cam.ty + oy * cam.cur, cam.tz + oz * cam.cur)) cam.cur += (free - cam.cur) * MathF.Min(1, fdt * 30);
        else cam.cur += (free - cam.cur) * MathF.Min(1, fdt * (free < cam.cur ? 7 : P.ads ? 10 : 2.5f));
        float cx = cam.tx + ox * cam.cur, cz = cam.tz + oz * cam.cur, cy = cam.ty + oy * cam.cur, floor = TerrainH(cx, cz) + 0.4f; if (cy < floor) cy = floor;
        float shk = cam.shake; cam.shake = MathF.Max(0, shk - fdt * 1.6f);
        camera.Position = new Vector3(cx + Jit() * shk * 0.5f, cy + Jit() * shk * 0.5f, cz + Jit() * shk * 0.5f);
        camera.LookAt(new Vector3(cam.tx, cam.ty, cam.tz));
        { float fx = cam.tx - cx, fy = cam.ty - cy, fz = cam.tz - cz, fl = Hypot(fx, fy, fz); if (fl == 0) fl = 1; AIM.px = cx; AIM.py = cy; AIM.pz = cz; AIM.fx = fx / fl; AIM.fy = fy / fl; AIM.fz = fz / fl; AIM.ok = true; }
        SetFov(Lerp(SET.fov + MathF.Min(18, MathF.Max(0, sp - TOP) * 0.7f), SET.fov * 0.62f, cam.ads), fdt);
    }

    // ---- free camera (debug): fly anywhere while the world carries on
    public sealed class FreeCamState { public bool on; public float x, y, z, yaw, pitch, fov = 70, wheel; }
    public readonly FreeCamState FREECAM = new();
    void FreeCamReset() { var F = FREECAM; F.x = camera.Position.X; F.y = camera.Position.Y; F.z = camera.Position.Z; F.yaw = cam.yaw; F.pitch = -cam.pitch; F.fov = SET.fov; }
    public void SetFreeCam(bool on)
    {
        FREECAM.on = on; if (on) FreeCamReset();
        ShowHint(on ? "FREE CAM · RT/LT ZOOM · RB/LB FOCAL · R3 RESET · V OR BACK TO LEAVE" : "FREE CAM OFF");
    }
    void UpdateFreeCam(float fdt)
    {
        var F = FREECAM; float fast = Input.Down("ShiftLeft") || Input.Down("ShiftRight") ? 3 : 1;
        float fx = -MathF.Sin(F.yaw), fz = -MathF.Cos(F.yaw), rx = MathF.Cos(F.yaw), rz = -MathF.Sin(F.yaw);
        float cp = MathF.Cos(F.pitch), lx = fx * cp, ly = MathF.Sin(F.pitch), lz = fz * cp;
        float mf = (Input.Down("KeyW") ? 1 : 0) - (Input.Down("KeyS") ? 1 : 0) - (pad.on ? pad.ly : 0), ms = (Input.Down("KeyD") ? 1 : 0) - (Input.Down("KeyA") ? 1 : 0) + (pad.on ? pad.lx : 0);
        float mu = (Input.Down("Space") || (pad.on && pad.a) ? 1 : 0) - (Input.Down("KeyC") || (pad.on && pad.x) ? 1 : 0);
        float sp = 14 * fast * fdt;
        F.x += (fx * mf + rx * ms) * sp; F.z += (fz * mf + rz * ms) * sp; F.y += mu * sp;
        float zoom = (pad.on ? pad.rt - pad.lt : 0) + (Input.Down("Equal") || Input.Down("NumpadAdd") ? 1 : 0) - (Input.Down("Minus") || Input.Down("NumpadSubtract") ? 1 : 0) + F.wheel;
        F.wheel = 0;
        F.x += lx * zoom * 18 * fast * fdt; F.y += ly * zoom * 18 * fast * fdt; F.z += lz * zoom * 18 * fast * fdt;
        float focal = (pad.on && pad.rb ? 1 : 0) - (pad.on && pad.lb ? 1 : 0) + (Input.Down("BracketRight") ? 1 : 0) - (Input.Down("BracketLeft") ? 1 : 0);
        F.fov = Clamp(F.fov * MathF.Exp(-focal * fdt * 0.9f), 8, 120);
        if (pad.on && (pad.rx != 0 || pad.ry != 0))
        {
            F.yaw -= MathF.Sign(pad.rx) * MathF.Pow(MathF.Abs(pad.rx), 1.5f) * 2.4f * fdt * (F.fov / 70);
            F.pitch = Clamp(F.pitch - MathF.Sign(pad.ry) * MathF.Pow(MathF.Abs(pad.ry), 1.5f) * 1.6f * fdt * (F.fov / 70), -1.5f, 1.5f);
        }
        float floor = TerrainH(F.x, F.z) + 0.3f; if (F.y < floor) F.y = floor;
        camera.Position = new Vector3(F.x, F.y, F.z);
        camera.LookAt(new Vector3(F.x + lx * 10, F.y + ly * 10, F.z + lz * 10));
        camera.Fov += (F.fov - camera.Fov) * MathF.Min(1, fdt * 8);
    }

    /// <summary>Mouse look (the original's look()): sens is per pixel of movement.</summary>
    public void Look(float dx, float dy, float sens)
    {
        sens *= SET.sens; if (SET.invertY) dy = -dy;
        if (FREECAM.on) { float k = FREECAM.fov / 70; FREECAM.yaw -= dx * sens * k; FREECAM.pitch = Clamp(FREECAM.pitch - dy * sens * k, -1.5f, 1.5f); return; }
        sens *= Lerp(1, SET.adsSens, cam.ads); cam.yaw -= dx * sens; cam.pitch = Clamp(cam.pitch + dy * sens, PitchMin, PitchMax); cam.lastLook = clockT;
    }

    // =====================================================================
    // Reticles: where an air dash would land, where a hit prop will go, where a throw will go
    // =====================================================================
    sealed class Reticle
    {
        public Lines line; public Geometry geo; public Mesh ring, dot; public Mesh[] ticks; public Material mat, lineMat; public float a; public int n; public bool hot;
        public float x, y, z, nx, ny = 1, nz;
    }
    Reticle RET, THRR, HOLO;
    Mesh holoHit, holoBox, holoSph; Points holoDots; Material holoDotMat, holoWireMat, holoHitMat;
    float hlx, hly, hlz, hhx, hhy, hhz, hnx, hny = 1, hnz;
    static Material AddMat(uint c, bool depthTest = true) => new() { Kind = MatKind.Basic, Color = Material.Hex(c), Transparent = true, Opacity = 0, DepthWrite = false, DepthTest = depthTest, Side = Side.Double, Blending = Blend.Additive, Fog = false };
    static Material LineMat(uint c, float dash, float gap) => new() { Kind = MatKind.Line, Color = Material.Hex(c), Transparent = true, Opacity = 0, DepthWrite = false, Blending = Blend.Additive, Dashed = true, DashSize = dash, GapSize = gap, Fog = false };
    Reticle MakeReticle(uint col, float r0, float r1, int segs, int maxPts, float dash, float gap, bool ticks)
    {
        var R = new Reticle { mat = AddMat(col, false), lineMat = LineMat(col, dash, gap) };
        R.geo = new Geometry { Position = new float[maxPts * 3] }; R.geo.DrawCount = 0;
        R.line = scene.Add(new Lines(R.geo, R.lineMat, true)); R.line.Visible = false;
        R.ring = scene.Add(new Mesh(Geo.Ring(r0, r1, segs).RotateX(-PI / 2), R.mat) { RenderOrder = 5 }); R.ring.Visible = false;
        if (ticks)
        {
            R.dot = new Mesh(Geo.Circle(0.07f, 12).RotateX(-PI / 2), R.mat); R.ring.Add(R.dot);
            R.ticks = new Mesh[4]; for (int i = 0; i < 4; i++) { R.ticks[i] = new Mesh(Geo.Plane(0.42f, 0.07f).RotateX(-PI / 2), R.mat); R.ring.Add(R.ticks[i]); }
        }
        return R;
    }
    void InitReticles()
    {
        RET = MakeReticle(0xffb347, 0.5f, 0.62f, 32, 60, 0.3f, 0.3f, true);
        THRR = MakeReticle(0xc8ff3a, 0.45f, 0.6f, 28, 70, 0.35f, 0.25f, false);
        HOLO = MakeReticle(0x7fe9ff, 0.6f, 0.85f, 36, 110, 0.45f, 0.22f, false); HOLO.ring.Mat.DepthTest = true;
        holoDotMat = new Material { Kind = MatKind.Points, Color = Material.Hex(0xc8f6ff), Size = 0.14f, Transparent = true, Opacity = 0, DepthWrite = false, Blending = Blend.Additive, Fog = false };
        holoDots = scene.Add(new Points(HOLO.geo, holoDotMat)); holoDots.Visible = false;
        holoHitMat = AddMat(0x7fe9ff); holoHit = scene.Add(new Mesh(Geo.Ring(0.12f, 0.2f, 24), holoHitMat)); holoHit.Visible = false;
        holoWireMat = new Material { Kind = MatKind.Line, Color = Material.Hex(0x7fe9ff), Transparent = true, Opacity = 0, DepthWrite = false, Blending = Blend.Additive, Fog = false, Wireframe = true };
        holoBox = scene.Add(new Mesh(Geo.Box(1, 1, 1), holoWireMat)); holoBox.Visible = false;
        holoSph = scene.Add(new Mesh(Geo.Icosahedron(1, 1), holoWireMat)); holoSph.Visible = false;
    }
    static void SetPt(Reticle R, int i, float x, float y, float z) { R.geo.Position[i * 3] = x; R.geo.Position[i * 3 + 1] = y; R.geo.Position[i * 3 + 2] = z; }
    static Quaternion FromUp(float nx, float ny, float nz)
    {
        var n = Vector3.Normalize(new Vector3(nx, ny, nz)); var up = Vector3.UnitY; float d = Vector3.Dot(up, n);
        if (d > 0.9999f) return Quaternion.Identity; if (d < -0.9999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, PI);
        var ax = Vector3.Normalize(Vector3.Cross(up, n)); return Quaternion.CreateFromAxisAngle(ax, MathF.Acos(d));
    }

    bool PredictDash()
    {
        var R = RET;
        if (P.ground || P.grabbed != null || !P.alive || P.charging || P.pound != 0) return false;
        float vx, vy, vz, dashT;
        if (!P.dashUsed)
        {
            float mx = IN.mx, mz = IN.mz, m = MathF.Min(1, Hypot(mx, mz)), sy = MathF.Sin(IN.yaw), cy = MathF.Cos(IN.yaw);
            var DVv = WithActor(ME, () => DashVec(m, -sy * mz + cy * mx, -cy * mz - sy * mx)); vx = DVv.vx; vy = DVv.vy; vz = DVv.vz; dashT = 0.4f; R.hot = DVv.tgt != null;
        }
        else { vx = P.vx; vy = P.vy; vz = P.vz; dashT = P.dashT; R.hot = false; }
        float x = P.x, y = P.y, z = P.z; const float h = 1 / 60f; R.n = 0;
        for (int i = 0; i < 240; i++)
        {
            if (i % 4 == 0 && R.n < 60) { SetPt(R, R.n, x, y + P.BR, z); R.n++; }
            vy = MathF.Max(vy - GRV * h, -MAX_FALL); dashT -= h;
            if (vy > 0 && vy < JMP_CUT && dashT <= 0) { float k = MathF.Pow(AIR_DRAG, h * 60); vx *= k; vz *= k; }
            float oy = y; x += vx * h; y += vy * h; z += vz * h;
            if (vy < 0)
            {
                var g = GroundAt(x, z, oy + 0.3f, true);
                if (y <= g.h) { R.x = x; R.y = g.h; R.z = z; R.nx = g.nx; R.ny = g.ny; R.nz = g.nz; return true; }
            }
            foreach (var e in actors)
            {
                if (e.team == "hero" || !e.P.alive || e.dormant) continue;
                var t = e.P;
                if (Hypot(t.x - x, t.y + t.H * 0.5f - (y + P.BR), t.z - z) < t.H * 0.5f + P.BR)
                {
                    R.x = x; R.y = y + P.BR; R.z = z; R.nx = P.x - x; R.ny = 0.2f; R.nz = P.z - z; float l = Hypot(R.nx, R.ny, R.nz); if (l == 0) l = 1; R.nx /= l; R.ny /= l; R.nz /= l; R.hot = true; return true;
                }
            }
        }
        return false;
    }
    static readonly Vector3 RET_COL = Material.Hex(0xffb347), RET_HOT = Material.Hex(0xff5a4a);
    void UpdateDashReticle(float fdt)
    {
        var R = RET; bool on = playing && PredictDash();
        R.a += ((on ? 1 : 0) - R.a) * MathF.Min(1, fdt * (on ? 16 : 6));
        bool vis = R.a > 0.02f; R.ring.Visible = R.line.Visible = vis;
        if (!vis) return;
        if (on)
        {
            R.ring.Position = new Vector3(R.x + R.nx * 0.06f, R.y + R.ny * 0.06f, R.z + R.nz * 0.06f); R.ring.Rotation = FromUp(R.nx, R.ny, R.nz);
            R.geo.DrawCount = R.n; R.geo.Version++;
        }
        float pulse = 0.75f + 0.25f * MathF.Sin(clockT * 10);
        R.mat.Color = R.hot ? RET_HOT : RET_COL; R.mat.Opacity = R.a * 0.9f * pulse; R.lineMat.Opacity = R.a * (P.dashUsed ? 0.25f : 0.45f); R.lineMat.Color = R.mat.Color;
        float sc = 1 + (R.hot ? 0.15f * MathF.Sin(clockT * 14) : 0); R.ring.Scale = new Vector3(sc);
        for (int i = 0; i < 4; i++)
        {
            float a = i * PI / 2 + clockT * (R.hot ? 3 : 0.8f);
            R.ticks[i].Position = new Vector3(MathF.Cos(a) * 0.9f, 0, -MathF.Sin(a) * 0.9f); R.ticks[i].Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, a);
        }
    }

    // ---- trajectory hologram: while airborne near a prop, predict where the hit will send it
    static readonly V3 CPN = new(), CPT = new(), LOCv = new(), LOC2v = new(), SCv = new(), TDv = new(), TAv = new(), TBv = new(), TCv = new();
    float ClosestOn(Body b, float x, float y, float z)
    {
        if (b.Kind == SPH)
        {
            float dx = x - b.Pos.x, dy = y - b.Pos.y, dz = z - b.Pos.z, d = Hypot(dx, dy, dz); if (d == 0) d = 1e-6f;
            CPN.x = dx / d; CPN.y = dy / d; CPN.z = dz / d; CPT.x = b.Pos.x + CPN.x * b.R; CPT.y = b.Pos.y + CPN.y * b.R; CPT.z = b.Pos.z + CPN.z * b.R; return d - b.R;
        }
        LOCv.x = x - b.Pos.x; LOCv.y = y - b.Pos.y; LOCv.z = z - b.Pos.z; QRot(b.Q, LOCv, LOCv, true);
        float qx = Clamp(LOCv.x, -b.He.x, b.He.x), qy = Clamp(LOCv.y, -b.He.y, b.He.y), qz = Clamp(LOCv.z, -b.He.z, b.He.z);
        float ex = LOCv.x - qx, ey = LOCv.y - qy, ez = LOCv.z - qz, dd = Hypot(ex, ey, ez);
        if (dd < 1e-6f) return -1;
        LOC2v.x = ex / dd; LOC2v.y = ey / dd; LOC2v.z = ez / dd; QRot(b.Q, LOC2v, CPN, false);
        SCv.x = qx; SCv.y = qy; SCv.z = qz; QRot(b.Q, SCv, CPT, false); CPT.x += b.Pos.x; CPT.y += b.Pos.y; CPT.z += b.Pos.z;
        return dd;
    }
    readonly List<Body> holoCand = new();
    bool PredictLaunch()
    {
        if (P.ground || P.grabbed != null || !P.alive) return false;
        bool rolling = P.rolling; float pr = rolling ? P.BR : P.R * 1.3f, pm = P.mass * (rolling ? 5 : 1); const float h = 1 / 60f;
        float x = P.x, y = P.y + (rolling ? P.BR : P.H * 0.45f), z = P.z, vx = P.vx, vy = P.vy, vz = P.vz;
        holoCand.Clear();
        foreach (var b in bodies) if (b.Held == null && Hypot(b.Pos.x - x, b.Pos.y - y, b.Pos.z - z) < 14 + b.Bound) holoCand.Add(b);
        if (holoCand.Count == 0) return false;
        var R = HOLO;
        for (int i = 0; i < 80; i++)
        {
            vy -= GRV * h; x += vx * h; y += vy * h; z += vz * h;
            if (y < TerrainH(x, z)) return false;
            foreach (var b in holoCand)
            {
                if (MathF.Abs(b.Pos.x - x) > b.Bound + pr || MathF.Abs(b.Pos.y - y) > b.Bound + pr || MathF.Abs(b.Pos.z - z) > b.Bound + pr) continue;
                float d = ClosestOn(b, x, y, z); if (d >= pr) continue;
                float vn = vx * CPN.x + vy * CPN.y + vz * CPN.z;
                if (vn > -1 || CPN.y > 0.75f) return false;   // grazing, or landing on top of it
                float mass = b.M;
                foreach (var o in bodies) if (o != b && o.Pos.y > b.Pos.y + b.Bound * 0.5f && Hypot(o.Pos.x - b.Pos.x, o.Pos.z - b.Pos.z) < b.Bound + o.Bound * 0.5f) mass += o.M;
                float kb = 1 / mass;
                if (b.Kind == BOXB)
                {
                    TDv.x = CPT.x - b.Pos.x; TDv.y = CPT.y - b.Pos.y; TDv.z = CPT.z - b.Pos.z;
                    Cross(TDv, CPN, TAv); InvIw(b, TAv, TBv); Cross(TBv, TDv, TCv); kb += Dot(TCv, CPN) * b.M / mass;
                }
                float j = -(1 + MathF.Max(b.E, rolling ? 0.3f : 0.05f)) * vn / (1 / pm + kb), im = 1 / mass;
                float bx = b.Pos.x, by = b.Pos.y, bz = b.Pos.z;
                float bvx = (b.Sleep ? 0 : b.Vel.x) - CPN.x * j * im, bvy = (b.Sleep ? 0 : b.Vel.y) - CPN.y * j * im, bvz = (b.Sleep ? 0 : b.Vel.z) - CPN.z * j * im;
                if (Hypot(bvx, bvy, bvz) < 1.2f) return false;
                hhx = CPT.x; hhy = CPT.y; hhz = CPT.z; hnx = CPN.x; hny = CPN.y; hnz = CPN.z;
                const float dt = 1 / 30f; float lift = b.Kind == SPH ? b.R : b.He.y, e = MathF.Max(0.1f, b.E), mu = b.Kind == SPH ? 0 : b.Mu;
                R.n = 0; bool landed = false;
                for (int k = 0; k < 110; k++)
                {
                    SetPt(R, k, bx, by, bz); R.n = k + 1;
                    bvy -= PGRAV * dt; bx += bvx * dt; by += bvy * dt; bz += bvz * dt;
                    var t = TerrainSample(bx, bz); float floor = t.h + lift;
                    if (by <= floor)
                    {
                        by = floor;
                        if (!landed) { landed = true; hlx = bx; hly = t.h + 0.05f; hlz = bz; }
                        bvy = bvy < -2.5f ? -bvy * e : 0;
                        float hs = Hypot(bvx, bvz);
                        if (b.Kind == SPH) { bvx -= t.gx * PGRAV * 0.7f * dt; bvz -= t.gz * PGRAV * 0.7f * dt; float k2 = hs > 1e-3f ? MathF.Max(0, hs - b.Roll * 0.6f * dt) / hs : 0; bvx *= k2; bvz *= k2; }
                        else { float k2 = hs > 1e-3f ? MathF.Max(0, hs - mu * PGRAV * dt) / hs : 0; bvx *= k2; bvz *= k2; }
                        if (Hypot(bvx, bvy, bvz) < 0.25f) break;
                    }
                }
                if (!landed) { hlx = bx; hly = TerrainH(bx, bz) + 0.05f; hlz = bz; }
                holoBody = b; return true;
            }
        }
        return false;
    }
    void UpdateHolo(float fdt)
    {
        var R = HOLO; bool on = playing && PredictLaunch();
        R.a += ((on ? 1 : 0) - R.a) * MathF.Min(1, fdt * (on ? 14 : 5));
        if (on)
        {
            R.geo.DrawCount = R.n; R.geo.Version++;
            R.ring.Position = new Vector3(hlx, hly, hlz);
            holoHit.Position = new Vector3(hhx, hhy, hhz); holoHit.Rotation = Quaternion.Normalize(FromUp(hnx, hny, hnz) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -PI / 2));
        }
        float pulse = 0.7f + 0.3f * MathF.Sin(clockT * 12), a = R.a;
        R.lineMat.Opacity = a * 0.95f * pulse; holoDotMat.Opacity = a * 0.8f; R.mat.Opacity = a * 0.75f * pulse; holoHitMat.Opacity = a * 0.75f * pulse; holoWireMat.Opacity = a * 0.85f;
        bool vis = a > 0.02f; R.line.Visible = holoDots.Visible = R.ring.Visible = holoHit.Visible = vis;
        R.ring.Scale = new Vector3(1 + 0.15f * MathF.Sin(clockT * 6));
        var b = holoBody;
        holoBox.Visible = vis && b != null && b.Kind == BOXB && !b.Removed; holoSph.Visible = vis && b != null && b.Kind == SPH && !b.Removed;
        if (b != null && !b.Removed)
        {
            var w = b.Kind == BOXB ? holoBox : holoSph;
            w.Position = new Vector3(b.Pos.x, b.Pos.y, b.Pos.z); w.Rotation = new Quaternion(b.Q.x, b.Q.y, b.Q.z, b.Q.w);
            w.Scale = b.Kind == BOXB ? new Vector3(b.He.x * 2.12f, b.He.y * 2.12f, b.He.z * 2.12f) : new Vector3(b.R * 1.1f);
        }
    }

    // ---- throw arc: while you carry something, where a throw at the current wind-up would go
    static readonly Vector3 THR_COL = Material.Hex(0xc8ff3a), THR_HOT = Material.Hex(0xff3048);
    bool PredictThrow()
    {
        var R = THRR;
        if ((P.hold == null && P.holdA == null) || !P.alive || P.grabbed != null) return false;
        var v = WithActor(ME, () => ThrowVel(ME, MathF.Max(P.charge, 0.3f)));
        float x = v.x, y = v.y, z = v.z, vx = v.vx, vy = v.vy, vz = v.vz; const float h = 1 / 60f; R.n = 0; R.hot = false;
        for (int i = 0; i < 210; i++)
        {
            if (i % 3 == 0 && R.n < 70) { SetPt(R, R.n, x, y, z); R.n++; }
            float ox = x, oy = y, oz = z;
            vy -= PGRAV * h; x += vx * h; y += vy * h; z += vz * h;
            if (i > 3 && HitMen(ox, oy, oz, x, y, z, "hero", 0.15f) != null)
            {
                R.hot = true; R.x = x; R.y = y; R.z = z; float l = Hypot(vx, vy, vz); if (l == 0) l = 1; R.nx = -vx / l; R.ny = -vy / l; R.nz = -vz / l; return true;
            }
            if (WorldBlocked(x, y, z))
            {
                var g = GroundAt(x, z, oy + 0.2f, true);
                if (y <= g.h + 0.05f) { R.x = x; R.y = g.h; R.z = z; R.nx = g.nx; R.ny = g.ny; R.nz = g.nz; }
                else { R.x = ox; R.y = oy; R.z = oz; float l = Hypot(vx, vz); if (l == 0) l = 1; R.nx = -vx / l; R.ny = 0; R.nz = -vz / l; }
                return true;
            }
        }
        return false;
    }
    void UpdateThrowReticle(float fdt)
    {
        var R = THRR; bool on = playing && PredictThrow();
        R.a += ((on ? 1 : 0) - R.a) * MathF.Min(1, fdt * (on ? 16 : 6));
        bool vis = R.a > 0.02f; R.ring.Visible = R.line.Visible = vis;
        if (!vis) return;
        if (on)
        {
            R.ring.Position = new Vector3(R.x + R.nx * 0.06f, R.y + R.ny * 0.06f, R.z + R.nz * 0.06f); R.ring.Rotation = FromUp(R.nx, R.ny, R.nz);
            R.geo.DrawCount = R.n; R.geo.Version++;
        }
        float k = P.charge > 0 ? 1 : 0.55f;
        R.mat.Color = R.hot ? THR_HOT : THR_COL; R.mat.Opacity = R.a * 0.9f * k; R.lineMat.Opacity = R.a * 0.55f * k; R.lineMat.Color = R.mat.Color;
        R.ring.Scale = new Vector3(1 + 0.12f * MathF.Sin(clockT * 10));
    }

    // =====================================================================
    // Props: bombs tick down, heavy things smash walls, spears fly point-first, used-up bombs come back later
    // =====================================================================
    sealed class Respawn { public float t; public string kind; public float x, z; }
    readonly List<Respawn> respawns = new();
    readonly Q4 AIMQ = new(); readonly List<Static> QPS = new();
    void StepProps(float dt)
    {
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            var b = bodies[i];
            if (b.Bomb && b.Fuse > 0)
            {
                b.Fuse -= dt; Wake(b);
                if (b.Fuse <= 0)
                {
                    float x = b.Pos.x, y = b.Pos.y, z = b.Pos.z; var src = b.ThrownBy ?? b.LastTouch;
                    foreach (var A in actors) if (A.P.hold == b) A.P.hold = null;
                    RemoveBody(b);
                    Explode(x, y, z, 5.5f, 1300, 60, src);
                    if (b.Prop == null || !b.Prop.debug) respawns.Add(new Respawn { t = 35, kind = "bomb", x = b.Home.x, z = b.Home.z });
                    continue;
                }
            }
            // heavy things flung fast smash through building walls
            if (b.Prop != null && b.Prop.heavy && !b.Sleep && b.Held == null && b.M > 400)
            {
                float sp = Hypot(b.Vel.x, b.Vel.y, b.Vel.z);
                if (sp > 7)
                {
                    float R = b.Bound + 0.25f, x = b.Pos.x, y = b.Pos.y, z = b.Pos.z; QueryStatics(x - R, x + R, z - R, z + R, QPS);
                    foreach (var o in QPS)
                        if (o.Chunk != null && o.Chunk.alive && x + R > o.MinX && x - R < o.MaxX && z + R > o.MinZ && z - R < o.MaxZ && y + R > o.MinY && y - R < o.MaxY)
                        {
                            DamageChunk(o.Chunk, b.M * sp * 0.02f, b.Vel.x, b.Vel.y, b.Vel.z);
                            if (!o.Chunk.alive) { b.Vel.x *= 0.85f; b.Vel.y *= 0.85f; b.Vel.z *= 0.85f; }
                        }
                }
            }
            // boulders torn up by mechs crumble away once they've been lying still a while
            if (b.Prop != null && b.Prop.ripped && b.Held == null) { b.Prop.ripT += dt; if (b.Prop.ripT > 40 && Hypot(P.x - b.Pos.x, P.z - b.Pos.z) > 30) { RemoveBody(b); continue; } }
            if (b.Spear && b.Held == null && !b.Sleep && b.Nc == 0)
            {
                float sp = Hypot(b.Vel.x, b.Vel.y, b.Vel.z);
                if (sp > 7) { AimQuat(b.Vel.x, b.Vel.y, b.Vel.z, AIMQ); QNlerp(b.Q, AIMQ, 0.12f, b.Q); b.W.x *= 0.9f; b.W.y *= 0.9f; b.W.z *= 0.9f; }
            }
        }
        for (int i = respawns.Count - 1; i >= 0; i--)
        {
            var r = respawns[i]; r.t -= dt;
            if (r.t <= 0 && Hypot(P.x - r.x, P.z - r.z) > 25) { PlaceProp(r.kind, r.x, r.z); respawns.RemoveAt(i); }
        }
    }

    // =====================================================================
    // Input
    // =====================================================================
    // the controller's X: tap to reload, hold to interact (take a weapon, get in or out of a vehicle or the mecha)
    public float xHeldT; bool xFired; public const float HOLD_USE = 0.35f;
    void ReadHumanInput()
    {
        var I = ME.IN; float mx = 0, mz = 0; bool on = playing;
        if (on && Act("forward")) mz += 1; if (on && Act("back")) mz -= 1;
        if (on && Act("right")) mx += 1; if (on && Act("left")) mx -= 1;
        I.mx = mx + pad.lx; I.mz = mz - pad.ly; I.yaw = I.aimYaw = cam.yaw;
        bool riding = ME.P.vehicle != null;
        I.jump = (on && Act("jump")) || pad.a; I.roll = (on && Act("roll")) || pad.b;
        I.pound = false;   // ground pound now comes from the punch button while in ball form
        I.punch = !riding && ((on && Act("punch")) || pad.rb);
        I.grab = (on && Act("grab")) || (!riding && pad.lb);
        I.ads = on && (pad.lt > 0.4f || Act("aim")); I.fire = on && (Act("fire") || pad.rt > 0.4f);
        I.throwAt = null;
        I.up = (riding && pad.rb) || (on && Act("up")); I.down = (riding && pad.lb) || (on && Act("down"));   // hover altitude while riding: RB up, LB down
        // X: tap = reload, hold = interact. The keyboard has its own keys for both.
        bool xUse = false, xReload = false;
        if (pad.x) { xHeldT += DT; if (xHeldT >= HOLD_USE && !xFired) { xFired = true; xUse = true; } }
        else { if (xHeldT > 0 && !xFired && xHeldT < HOLD_USE) xReload = true; xHeldT = 0; xFired = false; }
        I.use = (on && Act("use")) || xUse;
        I.reload = (on && Act("reload")) || xReload;
        if (FREECAM.on) { I.mx = I.mz = 0; I.jump = I.roll = I.use = I.pound = I.grab = I.ads = I.fire = I.punch = I.reload = false; I.aimYaw = I.yaw = ME.P.facing; }
    }

    /// <summary>Discrete key presses during play (weapon switching, free cam, back to base, next song).</summary>
    void HandlePlayKeys()
    {
        if (ActHit("freecam")) SetFreeCam(!FREECAM.on);
        if (FREECAM.on && Input.Pressed("KeyR")) FreeCamReset();
        if (ActHit("next")) CycleWeapon(ME, 1);
        for (int i = 0; i < 3; i++) if (ActHit("slot" + (i + 1))) SwitchWeapon(ME, i);
        if (ActHit("base") && !FREECAM.on) BackToBase();
        if (ActHit("song")) Music.Instance?.NextSong();
        if (Input.Wheel != 0) { if (FREECAM.on) FREECAM.wheel += MathF.Sign(Input.Wheel) * 4; else CycleWeapon(ME, Input.Wheel < 0 ? 1 : -1); }
        if (Input.Locked && (Input.MouseDX != 0 || Input.MouseDY != 0))
        {
            if (MathF.Abs(Input.MouseDX) + MathF.Abs(Input.MouseDY) > 2) padActive = false;
            Look(Input.MouseDX, Input.MouseDY, 0.0024f);
        }
    }

    void PadAimAssist(float fdt)
    {
        assistOn = false;
        if (!padActive || !AIM.ok || !P.alive) return;
        float cone = cam.ads > 0.5f ? 0.13f : 0.09f, ba = cone; float bx = 0, by = 0, bz = 0; bool found = false;
        foreach (var B in actors)
        {
            if (B.team == "hero" || !B.P.alive || B.dormant) continue;
            var G = B.P; float dx = G.x - AIM.px, dy = G.y + G.H * 0.6f - AIM.py, dz = G.z - AIM.pz, d = Hypot(dx, dy, dz);
            if (d > 85 || d < 2) continue;
            float a = MathF.Acos(Clamp((dx * AIM.fx + dy * AIM.fy + dz * AIM.fz) / d, -1, 1)) - MathF.Atan2(G.R, d);
            if (a < ba) { ba = a; bx = dx / d; by = dy / d; bz = dz / d; found = true; }
        }
        if (!found) return;
        assistOn = true;
        bool engaged = pad.rt > 0.3f || pad.lt > 0.4f || Hypot(pad.lx, pad.ly) > 0.2f || Hypot(pad.rx, pad.ry) > 0.1f;
        if (!engaged) return;
        float k = MathF.Min(1, fdt * (cam.ads > 0.5f ? 3.2f : 2.2f)) * 0.45f;
        float wantYaw = MathF.Atan2(-bx, -bz), curYaw = MathF.Atan2(-AIM.fx, -AIM.fz);
        cam.yaw += WrapAngle(wantYaw - curYaw) * k;
        cam.pitch = Clamp(cam.pitch - (MathF.Asin(Clamp(by, -1, 1)) - MathF.Asin(Clamp(AIM.fy, -1, 1))) * k, PitchMin, PitchMax);
    }
    /// <summary>Controller handling while playing (the original's padFrame, minus menus which the game screen owns).</summary>
    void PadPlay(float fdt)
    {
        if (!pad.on) return;
        if (!padArmed) { if (!(pad.a || pad.b || pad.x || pad.y || pad.start || pad.lb || pad.rb)) padArmed = true; return; }
        if (FREECAM.on) { if (PadHit("rs")) FreeCamReset(); if (PadHit("back")) SetFreeCam(false); return; }
        if (PadHit("back")) BackToBase();
        if (PadHit("y")) CycleWeapon(ME, 1);
        if (PadHit("rs")) { cam.yaw = P.facing; cam.pitch = SET.fp ? 0 : 0.3f; }
        if (pad.rx != 0 || pad.ry != 0)
        {
            float cx = MathF.Sign(pad.rx) * MathF.Pow(MathF.Abs(pad.rx), 1.5f), cy = MathF.Sign(pad.ry) * MathF.Pow(MathF.Abs(pad.ry), 1.5f);
            float ks = Lerp(1, SET.adsSens * 0.9f, cam.ads) * (assistOn ? 0.62f : 1) * SET.padSens;
            cam.yaw -= cx * 3.2f * ks * fdt; cam.pitch = Clamp(cam.pitch + cy * 2.0f * ks * fdt * (SET.invertY ? -1 : 1), PitchMin, PitchMax); cam.lastLook = clockT;
        }
        PadAimAssist(fdt);
    }

    // =====================================================================
    // The fixed-step simulation (120 Hz) with render interpolation
    // =====================================================================
    void StepDead(Actor A, float dt)
    {
        var G = A.P; G.deadT += dt;
        if (A.team == "enemy") { RemoveActor(A); return; }
        if (A.human && WAVES.on) { if (G.deadT > 1.5f) WaveGameOver(); return; }
        if (G.deadT > (A.human ? 3 : 6)) RespawnActor(A);
    }
    public bool fullPhys;
    void SimStep()
    {
        stepNo++;
        foreach (var A in actors) { var G = A.P; G.px = G.x; G.py = G.y; G.pz = G.z; G.pBallQ.Copy(G.ballQ); }
        foreach (var b in bodies) { b.Pp.Copy(b.Pos); b.Pq.Copy(b.Q); }
        BuildBodyGrid();
        StepSquads(DT); StepWaves(DT); CommsTick(DT); StepBosses(DT);
        ReadHumanInput();
        StepVehicles(DT);
        for (int i = actors.Count - 1; i >= 0; i--)
        {
            if (i >= actors.Count) continue;
            var A = actors[i];
            if (!A.P.alive) { StepDead(A, DT); continue; }
            if (A.dormant) continue;
            float dt = DT;
            if (A.lod > 0) { A.lodAcc += DT; if (((stepNo + A.uid) & 1) != 0) continue; dt = A.lodAcc; A.lodAcc = 0; }
            WithActor(A, () =>
            {
                if (A.brain != null) { if (A.isBoss) MimicBrain(A, dt); else if (A.team == "enemy") EnemyBrain(A, dt); else AllyBrain(A, dt); }
                PlayerStep(dt, simT);
                ActorWeapons(dt, IN.fire);
            });
        }
        StepShots(DT); SuppressTick();
        ActorsCollide();
        foreach (var A in actors) A.inSim = A.P.alive && !A.dormant && A.P.grabbed == null && A.P.ragdoll == null && A.P.vehicle == null;
        // rigid bodies step at 60 Hz in two-step strides unless something needs the full rate (a ragdoll, a held prop)
        bool full = fullPhys || joints.Count > 0 || bodies.Exists(b => b.Held != null) || PERF.ema < 0.9f;
        if (full) { RagdollMuscles(DT); PhysicsStep(DT); PERF.odd = false; }
        else if (PERF.odd = !PERF.odd) { RagdollMuscles(DT * 2); PhysicsStep(DT * 2); }
        foreach (var A in actors) A.inSim = false;
        // far from everyone, loose props go to sleep and cost nothing
        if (stepNo % 60 == 0)
        {
            var H = ME.P;
            foreach (var b in bodies) if (!b.Sleep && b.Held == null && !b.IsDebris && MathF.Abs(b.Pos.x - H.x) + MathF.Abs(b.Pos.z - H.z) > 180) { b.Sleep = true; b.Vel.Set(0, 0, 0); b.W.Set(0, 0, 0); b.VertsOk = false; }
        }
        // too many bullets in the air: the oldest go
        while (shots.Count > 260) shots.RemoveAt(0);
        StepProps(DT); StepOrganic(DT); StepDebris(DT); StepChips(DT); StepItems(DT); StepChunks(); StepPiloting();
        simT += DT;
    }
    // a frame-time governor: when the simulation runs heavy, the debris budget shrinks until it recovers
    void PerfTick(float ms)
    {
        PERF.ema += (ms - PERF.ema) * 0.05f;
        int bse = SET.debrisMax > 0 ? SET.debrisMax : 240;
        if (PERF.ema > 2.4f && chipMaxDyn > 50) { chipMaxDyn = Math.Max(50, chipMaxDyn - 6); while (debris.Count > chipMaxDyn) RemoveBody(debris[0]); }
        else if (PERF.ema < 1.4f && chipMaxDyn < bse) chipMaxDyn = Math.Min(bse, chipMaxDyn + 2);
    }

    readonly System.Diagnostics.Stopwatch perfSw = new();
    /// <summary>One displayed frame: simulation steps, camera, effects. The renderer draws <see cref="scene"/> afterwards.</summary>
    public void Frame(float fdt, float aspect)
    {
        clockT += fdt;
        Sound.Tick(fdt); Speech.Update();
        PollPad();
        if (playing) { HandlePlayKeys(); PadPlay(fdt); }
        float alpha = 1;
        if (playing)
        {
            // hit-stop: a landed melee blow freezes the world for an instant
            accum += fdt * SET.timeScale * (HITSTOP > 0 ? 0.07f : 1); if (HITSTOP > 0) HITSTOP -= fdt; int n = 0;
            perfSw.Restart();
            while (accum >= DT && n < 12) { SimStep(); accum -= DT; n++; }
            if (n > 0) PerfTick((float)perfSw.Elapsed.TotalMilliseconds / n);
            if (n == 12) accum = 0;
            alpha = accum / DT;
        }
        RP.x = Lerp(P.px, P.x, alpha); RP.y = Lerp(P.py, P.y, alpha); RP.z = Lerp(P.pz, P.z, alpha);
        camera.Aspect = aspect; camera.Far = 3500;
        SyncProps(alpha);
        DrawMen(alpha, fdt);
        StepSprites(fdt);
        if (FREECAM.on) UpdateFreeCam(fdt); else UpdateCamera(fdt, playing ? clockT : -100);
        camPos = camera.Position;
        Sound.LX = camPos.X; Sound.LY = camPos.Y; Sound.LZ = camPos.Z; Sound.LRx = MathF.Cos(cam.yaw); Sound.LRz = -MathF.Sin(cam.yaw);
        UpdateHolo(fdt); UpdateDashReticle(fdt); UpdateThrowReticle(fdt); UpdateSpores(clockT);
        UpdateOrganic(clockT, fdt, camPos);
        scene.ShadowCenter = new Vector3(RP.x, RP.y, RP.z); scene.Time = clockT;
        hurtA = MathF.Max(0, hurtA - fdt * 1.8f);
        if (hintT > 0) hintT -= fdt;
        StepCommsLines();
    }
}

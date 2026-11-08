using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Engine;

namespace GermBall.Sim;

/// <summary>
/// The whole game: a fixed 120 Hz simulation of the player, weapons, dummies, balls, pickups and zones, plus the camera.
/// The platform layer calls <see cref="Frame"/> once per rendered frame and <see cref="Draw"/> to fill a <see cref="DrawList"/>.
/// </summary>
public sealed partial class Game
{
    public const float DT = 1f / 120f;
    /// <summary>Shown in the HUD and the window title. 1.0 is the deprecated full game in legacy/.</summary>
    public const string Version = "2.0-dev";

    public readonly Level Level;
    public readonly Player Player = new();
    public readonly List<Dummy> Dummies = new();
    public readonly List<Prop> Props = new();
    public readonly List<Pickup> Pickups = new();
    public readonly List<Projectile> Projectiles = new();
    public readonly Effects Effects = new();
    public readonly Rng Rng = new(1234);

    public float Time;
    public float CamYaw, CamPitch = -0.15f;
    public Vector3 CamPos, CamForward = -Vector3.UnitZ;
    public float Fov = 70 * MathX.Deg;
    float camPivotY, accumulator, alpha;
    Btn pendingPressed;

    // debug and menu state
    public bool ShowHelp = true, ShowDebug, ShowZones, Paused, SlowMo;
    public float Fps;
    public Vector3 RespawnPos; public float RespawnYaw;
    public int LandmarkIndex;
    public string Toast = ""; public float ToastT;
    public Zone Area; public float AreaBannerT;
    public readonly List<Zone> ActiveZones = new();

    public Game(Level level)
    {
        Level = level;
        foreach (var s in level.Dummies) Dummies.Add(new Dummy(s));
        foreach (var s in level.Props) Props.Add(new Prop(s));
        foreach (var s in level.Pickups) Pickups.Add(new Pickup(s));
        Weapons.Add(new WeaponSlot(WeaponDef.Pistol));
        RespawnPos = level.Spawn; RespawnYaw = level.SpawnYaw;
        Player.Place(level.Spawn, level.SpawnYaw);
        CamYaw = level.SpawnYaw;
        camPivotY = Player.Pos.Y + 1.5f;
        UpdateCamera(1, 1);
    }

    // ---------------------------------------------------------------- per frame
    /// <summary>Advances the game by one rendered frame: camera look, menu keys, then as many fixed steps as the time allows.</summary>
    public void Frame(float dt, InputFrame input)
    {
        HandleMenuKeys(input);
        aimHeld = input.Has(Btn.Aim);
        if (Paused) { pendingPressed = 0; UpdateCamera(alpha, 0); return; }

        float sens = Player.Alive ? 1 : 0;
        CamYaw = MathX.AngleWrap(CamYaw - input.Look.X * sens);
        CamPitch = MathX.Clamp(CamPitch + input.Look.Y * sens, -1.45f, 1.45f);

        pendingPressed |= input.Pressed;
        accumulator += MathF.Min(dt, 0.1f) * (SlowMo ? 0.25f : 1f);
        int ticks = 0;
        while (accumulator >= DT && ticks < 12)
        {
            var tickInput = input; tickInput.Pressed = pendingPressed; pendingPressed = 0;
            Tick(tickInput);
            accumulator -= DT; ticks++;
        }
        if (ticks == 12) accumulator = 0;
        alpha = accumulator / DT;
        UpdateCamera(alpha, dt);
    }

    void HandleMenuKeys(in InputFrame input)
    {
        if (input.Hit(Btn.Pause)) Paused = !Paused;
        if (input.Hit(Btn.Help)) ShowHelp = !ShowHelp;
        if (input.Hit(Btn.Debug)) ShowDebug = !ShowDebug;
        if (input.Hit(Btn.ShowZones)) { ShowZones = !ShowZones; Say(ShowZones ? "Zone volumes shown" : "Zone volumes hidden"); }
        if (Paused) return;
        if (input.Hit(Btn.SlowMo)) { SlowMo = !SlowMo; Say(SlowMo ? "Slow motion 25%" : "Normal speed"); }
        if (input.Hit(Btn.God)) { Player.God = !Player.God; Say(Player.God ? "God mode on" : "God mode off"); }
        if (input.Hit(Btn.Noclip)) { Player.Noclip = !Player.Noclip; Player.Vel = Vector3.Zero; Say(Player.Noclip ? "Noclip on" : "Noclip off"); }
        if (input.Hit(Btn.Respawn)) { RespawnPlayer(); Say("Respawned"); }
        if (input.Hit(Btn.NextZone) || input.Hit(Btn.PrevZone))
        {
            int n = Level.Landmarks.Count;
            LandmarkIndex = (LandmarkIndex + (input.Hit(Btn.NextZone) ? 1 : n - 1)) % n;
            var m = Level.Landmarks[LandmarkIndex];
            Teleport(m.Pos, m.Yaw);
            Say("Teleport: " + m.Name);
        }
    }

    public void Say(string s) { Toast = s; ToastT = 2; }

    // ---------------------------------------------------------------- fixed step
    public void Tick(in InputFrame input)
    {
        Time += DT;
        ToastT -= DT; AreaBannerT -= DT;
        Player.Update(this, input, DT);
        UpdateWeapons(input, DT);
        foreach (var d in Dummies) d.Update(DT);
        PushPlayerOutOfDummies();
        foreach (var p in Props) p.Update(Level, DT);
        CollideProps();
        UpdateProjectiles(DT);
        UpdatePickups(DT);
        UpdateZones(DT);
        if (Player.Alive && !Player.Noclip && Player.Pos.Y < Level.KillY) Player.Die("the void");
        Effects.Update(DT);
    }

    public void RespawnPlayer()
    {
        Player.Alive = true; Player.Hp = Player.MaxHp; Player.Shield = Player.MaxShield;
        Player.Place(RespawnPos, RespawnYaw);
        CamYaw = RespawnYaw; CamPitch = -0.15f;
        camPivotY = Player.Pos.Y + 1.5f;
    }

    public void Teleport(Vector3 feet, float yaw)
    {
        if (!Player.Alive) { Player.Alive = true; Player.Hp = Player.MaxHp; Player.Shield = Player.MaxShield; }
        Player.Place(feet, yaw);
        CamYaw = yaw; CamPitch = -0.15f;
        camPivotY = Player.Pos.Y + 1.5f;
    }

    void UpdateZones(float dt)
    {
        ActiveZones.Clear();
        if (!Player.Alive || Player.Noclip) return;
        var feet = Player.Pos + new Vector3(0, 0.05f, 0);
        Zone area = null; float areaVol = float.MaxValue;
        foreach (var z in Level.Zones)
        {
            if (!z.Contains(feet)) continue;
            if (z.Kind == ZoneKind.Area)
            {
                var s = z.Max - z.Min; float vol = s.X * s.Y * s.Z;
                if (vol < areaVol) { areaVol = vol; area = z; }
                continue;
            }
            ActiveZones.Add(z);
            switch (z.Kind)
            {
                case ZoneKind.Damage: Player.Hurt(z.Power * dt, Vector3.Zero, this, z.Name); break;
                case ZoneKind.Heal: Player.Heal(z.Power * dt); break;
                case ZoneKind.Kill: Player.Die(z.Name); break;
                case ZoneKind.Checkpoint:
                    var cp = new Vector3(z.Center.X, z.Min.Y + 0.6f, z.Center.Z);
                    if (Vector3.Distance(RespawnPos, cp) > 0.5f)
                    {
                        RespawnPos = cp; RespawnYaw = Player.Yaw;
                        Say("Checkpoint set");
                    }
                    break;
            }
        }
        if (area != Area) { Area = area; AreaBannerT = 2.5f; }
    }

    void PushPlayerOutOfDummies()
    {
        var p = Player; if (!p.Alive || p.Noclip) return;
        float pr = p.Rolling ? Player.BallRadius : Player.BodyRadius, top = p.Pos.Y + (p.Rolling ? 2 * Player.BallRadius : Player.Height);
        foreach (var d in Dummies)
        {
            if (!d.Alive || top < d.Pos.Y || p.Pos.Y > d.Pos.Y + d.Height) continue;
            var v = MathX.Flat(p.Pos - d.Pos); float dist = v.Length(), min = pr + d.Radius;
            if (dist >= min) continue;
            var n = dist > 1e-4f ? v / dist : Vector3.UnitX;
            p.Pos += n * (min - dist);
            float vn = Vector3.Dot(p.Vel, n); if (vn < 0) p.Vel -= n * vn;
        }
    }

    void CollideProps()
    {
        // ball against ball
        for (int i = 0; i < Props.Count; i++)
            for (int k = i + 1; k < Props.Count; k++)
            {
                var a = Props[i]; var b = Props[k];
                var v = b.Pos - a.Pos; float dist = v.Length(), min = a.Radius + b.Radius;
                if (dist >= min || dist < 1e-5f) continue;
                var n = v / dist; float wa = b.Mass / (a.Mass + b.Mass), wb = 1 - wa;
                a.Pos -= n * (min - dist) * wa; b.Pos += n * (min - dist) * wb;
                float rel = Vector3.Dot(b.Vel - a.Vel, n);
                if (rel < 0) { float j = -1.3f * rel / (1 / a.Mass + 1 / b.Mass); a.Vel -= n * j / a.Mass; b.Vel += n * j / b.Mass; }
            }
        // ball against player (the player is heavy and is barely pushed back)
        var p = Player; if (!p.Alive || p.Noclip) return;
        foreach (var pr in Props)
        {
            Vector3 c; float r;
            if (p.Rolling) { c = p.BallCenter; r = Player.BallRadius; }
            else { c = p.Pos + new Vector3(0, MathX.Clamp(pr.Pos.Y - p.Pos.Y, Player.BodyRadius, Player.Height - Player.BodyRadius), 0); r = Player.BodyRadius; }
            var v = pr.Pos - c; float dist = v.Length(), min = r + pr.Radius;
            if (dist >= min || dist < 1e-5f) continue;
            var n = v / dist; const float playerMass = 70;
            float wp = pr.Mass / (pr.Mass + playerMass);
            pr.Pos += n * (min - dist) * (1 - wp);
            var pushP = n * (min - dist) * wp; pushP.Y = MathF.Min(pushP.Y, 0); p.Pos -= pushP;
            float rel = Vector3.Dot(pr.Vel - p.Vel, n);
            if (rel < 0) pr.Vel -= n * rel * 1.25f;
        }
    }

    void UpdateProjectiles(float dt)
    {
        foreach (var pj in Projectiles)
        {
            pj.PrevPos = pj.Pos; pj.Life -= dt;
            pj.Vel.Y -= Player.Gravity * 0.6f * dt;
            var step = pj.Vel * dt; float len = step.Length();
            if (len > 1e-6f)
            {
                var dir = step / len;
                var hit = RaycastAll(pj.Pos, dir, len + Projectile.Radius);
                if (hit.Hit) { pj.Pos = hit.Point - dir * Projectile.Radius; Detonate(pj); continue; }
            }
            pj.Pos += step;
            if (pj.Life <= 0 || pj.Pos.Y < Level.KillY) Detonate(pj);
        }
        Projectiles.RemoveAll(p => p.Dead);
    }

    void Detonate(Projectile pj)
    {
        if (pj.Dead) return;
        pj.Dead = true;
        Explode(pj.Pos, pj.Def.BlastRadius, pj.Def.Damage, pj.Def.Knock, true);
    }

    void UpdatePickups(float dt)
    {
        foreach (var pk in Pickups)
        {
            pk.Spin += dt * 2;
            if (!pk.Active) { pk.RespawnT -= dt; if (pk.RespawnT <= 0) pk.Active = true; continue; }
            if (!Player.Alive || Player.Noclip) continue;
            if (Vector3.Distance(Player.Center, pk.Pos) > 1.4f) continue;
            bool took = true;
            switch (pk.Spawn.Kind)
            {
                case PickupKind.Health:
                    if (Player.Hp >= Player.MaxHp) took = false; else { Player.Heal(50); Say("+50 health"); }
                    break;
                case PickupKind.Ammo:
                    took = false;
                    foreach (var w in Weapons)
                        if (!w.Endless && w.Reserve < w.Def.ReserveMax) { w.Reserve = Math.Min(w.Def.ReserveMax, w.Reserve + w.Def.Mag * 2); took = true; }
                    if (took) Say("Ammo");
                    break;
                case PickupKind.Rifle: GiveWeapon(WeaponDef.Rifle); Say("Rifle"); break;
                case PickupKind.Lobber: GiveWeapon(WeaponDef.Lobber); Say("Lobber"); break;
            }
            if (took) { pk.Active = false; pk.RespawnT = 8; Effects.Burst(pk.Pos, MathX.Rgb(0xFFFFFF), 10, 3); }
        }
    }

    // ---------------------------------------------------------------- camera
    /// <summary>Over-the-shoulder third person. The camera pulls in when a wall is behind it.</summary>
    void UpdateCamera(float a, float dt)
    {
        var p = Player;
        var feet = Vector3.Lerp(p.PrevPos, p.Pos, a);
        // one height whether curled or not, so every jump doesn't bob the camera
        float targetY = feet.Y + (p.Rolling && p.Grounded ? 1.3f : 1.6f);
        // smooth vertical motion (steps, landings) but never lag behind by more than half a metre
        camPivotY = MathX.Lerp(camPivotY, targetY, 1 - MathF.Exp(-14 * dt));
        camPivotY = MathX.Clamp(camPivotY, targetY - 0.5f, targetY + 0.5f);
        var pivot = new Vector3(feet.X, camPivotY, feet.Z);
        bool aiming = aimHeld && Weapon != null && !p.Rolling && p.Alive && !p.Noclip;
        float dist = aiming ? 2.4f : 4.4f, shoulder = aiming ? 0.8f : 0.9f;
        Fov = (aiming ? 52 : 70) * MathX.Deg;
        CamForward = MathX.Forward(CamYaw, CamPitch);
        var right = MathX.Right(CamYaw);
        var shoulderPt = pivot + right * shoulder;
        // keep the shoulder offset itself out of walls
        var sh = Level.World.Raycast(pivot, right, shoulder + 0.2f);
        if (sh.Hit) shoulderPt = pivot + right * MathF.Max(0, sh.T - 0.2f);
        var want = shoulderPt - CamForward * dist;
        var hit = Level.World.Raycast(shoulderPt, -CamForward, dist + 0.2f);
        CamPos = hit.Hit ? shoulderPt - CamForward * MathF.Max(0.3f, hit.T - 0.2f) : want;
    }
    bool aimHeld;
    /// <summary>Interpolation between the last two simulation steps, for drawing.</summary>
    public float Alpha => alpha;
}

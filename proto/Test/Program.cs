using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using GermBall.Proto.Engine;
using GermBall.Proto.Sim;

namespace GermBall.Proto.Test;

/// <summary>
/// Headless checks for the prototype.
///   dotnet run --project proto/Test                 run every check
///   dotnet run --project proto/Test -- shots DIR    also render screenshots of the debug level into DIR
/// </summary>
public static class Program
{
    static int failed, passed;

    public static int Main(string[] args)
    {
        Run("spawn settles on the pad", SpawnSettles);
        Run("walk speed", WalkSpeed);
        Run("step heights", StepHeights);
        Run("ramp angles", RampAngles);
        Run("stairs", Stairs);
        Run("jump height and air dash", JumpAndDash);
        Run("crawl tunnel needs rolling", CrawlTunnel);
        Run("rolling speeds up downhill", RollDownhill);
        Run("jump pads land on their ledges", JumpPads);
        Run("acid hurts, heal pad heals", AcidAndHeal);
        Run("pit kills, respawn, checkpoint", PitAndCheckpoint);
        Run("low gravity and updraft", LowGravityAndUpdraft);
        Run("boost pad", Boost);
        Run("pickups give weapons", Pickups);
        Run("pistol kills a dummy", ShootDummy);
        Run("lobber blast moves balls and hurts dummies", LobberBlast);
        Run("punch and ground pound", PunchAndPound);
        Run("random input soak (every landmark)", Soak);

        if (args.Length >= 2 && args[0] == "shots") Shots(args[1], args.Length >= 4 ? int.Parse(args[2]) : 1280, args.Length >= 4 ? int.Parse(args[3]) : 720);

        Console.WriteLine($"\n{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("  ok    " + name); passed++; }
        catch (Exception e) { Console.WriteLine("  FAIL  " + name + "\n        " + e.Message.Replace("\n", "\n        ")); failed++; }
    }
    static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }

    // ---------------------------------------------------------------- helpers
    static Game New()
    {
        var g = new Game(Level.BuildDebug()) { ShowHelp = false };
        Steps(g, 30, default);
        return g;
    }
    static void Steps(Game g, int n, InputFrame input, Btn pressFirst = 0)
    {
        for (int i = 0; i < n; i++)
        {
            var f = input; if (i == 0) f.Pressed |= pressFirst;
            g.Frame(Game.DT, f);
        }
    }
    static int Sec(float s) => (int)MathF.Round(s / Game.DT);
    static InputFrame Fwd(Btn held = 0) => new() { Move = new Vector2(0, 1), Held = held };
    static void Place(Game g, Vector3 feet, float yaw) { g.Teleport(feet, yaw); Steps(g, 20, default); }
    static string P(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";

    /// <summary>Turns the camera until the crosshair is on <paramref name="target"/>.</summary>
    static void AimAt(Game g, Vector3 target)
    {
        for (int i = 0; i < 6; i++)
        {
            var d = target - g.CamPos;
            g.CamYaw = MathX.YawOf(d);
            g.CamPitch = MathF.Atan2(d.Y, MathX.FlatLen(d));
            g.Frame(0, default);
        }
    }

    // ---------------------------------------------------------------- movement
    static void SpawnSettles()
    {
        var g = New(); Steps(g, Sec(1), default);
        Check(g.Player.Grounded, "not grounded at spawn");
        Check(MathF.Abs(g.Player.Pos.Y - 0.05f) < 0.02f, "feet not on the 5 cm spawn pad: " + P(g.Player.Pos));
    }

    static void WalkSpeed()
    {
        var g = New(); var start = g.Player.Pos;
        Steps(g, Sec(1), Fwd());
        float d = MathX.FlatLen(g.Player.Pos - start);
        Check(d > 6.3f && d < 7.1f, $"walked {d:0.00} m in 1 s");
        Check(g.Player.Pos.Z < start.Z, "forward should be north (-Z)");
    }

    static void StepHeights()
    {
        float[] heights = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.8f };
        var report = new List<string>();
        for (int i = 0; i < heights.Length; i++)
        {
            var g = New();
            Place(g, new Vector3(-15 + i * 5, 0, -17.5f), 0);
            bool onTop = false;
            for (int k = 0; k < Sec(1f); k++) { Steps(g, 1, Fwd()); onTop |= MathF.Abs(g.Player.Pos.Y - heights[i]) < 0.03f && g.Player.Grounded; }
            bool expect = heights[i] <= Player.StepHeight;
            if (onTop != expect) report.Add($"{heights[i]} m: {(onTop ? "climbed" : "blocked")}, ended at {P(g.Player.Pos)}");
        }
        Check(report.Count == 0, string.Join("; ", report));
    }

    static void RampAngles()
    {
        float[] angles = { 10, 20, 30, 40, 45, 50, 55, 60 };
        var report = new List<string>();
        for (int i = 0; i < angles.Length; i++)
        {
            var g = New();
            Place(g, new Vector3(-17.5f + i * 5, 0, -28), 0);
            float peak = 0;
            for (int k = 0; k < Sec(4f); k++) { Steps(g, 1, Fwd()); if (g.Player.Grounded) peak = MathF.Max(peak, g.Player.Pos.Y); }
            bool top = peak > 2.45f;
            bool expect = angles[i] <= Player.MaxSlopeDeg;
            if (top != expect) report.Add($"{angles[i]} deg: {(top ? "climbed" : "failed")}, highest footing {peak:0.00} m");
        }
        Check(report.Count == 0, string.Join("; ", report));
    }

    static void Stairs()
    {
        float Peak(float x)
        {
            var g = New(); Place(g, new Vector3(x, 0, -28), 0);
            float peak = 0;
            for (int k = 0; k < Sec(2.5f); k++) { Steps(g, 1, Fwd()); peak = MathF.Max(peak, g.Player.Pos.Y); }
            return peak;
        }
        float a = Peak(-30), b = Peak(-25);
        Check(a > 2.95f, $"0.25 m stairs: only reached {a:0.00} m");
        Check(b < 0.1f, $"0.5 m stairs should need jumps, reached {b:0.00} m");
    }

    static void JumpAndDash()
    {
        var g = New();
        Steps(g, Sec(1.5f), default, Btn.Jump);
        float expect = Player.JumpSpeed * Player.JumpSpeed / (2 * Player.Gravity);
        Check(MathF.Abs(g.Player.LastJumpHeight - expect) < 0.08f, $"jump height {g.Player.LastJumpHeight:0.00}, expected {expect:0.00}");
        // jump forward, compare with jump + dash
        float Dist(bool dash)
        {
            var h = New(); var s = h.Player.Pos;
            Steps(h, Sec(0.3f), Fwd(), Btn.Jump);
            if (dash) Steps(h, 1, Fwd(), Btn.Jump);
            Steps(h, Sec(1.2f), default);
            return MathX.FlatLen(h.Player.Pos - s);
        }
        float plain = Dist(false), dashed = Dist(true);
        Check(dashed > plain + 1.5f, $"dash adds too little: {plain:0.0} m vs {dashed:0.0} m");
    }

    static void CrawlTunnel()
    {
        var g = New(); Place(g, new Vector3(-25, 0, -52), 0);
        Steps(g, Sec(3), Fwd());
        Check(g.Player.Pos.Z > -55.2f, "walked into the 1.1 m tunnel: " + P(g.Player.Pos));
        Steps(g, Sec(3), Fwd(Btn.Roll));
        Check(g.Player.Pos.Z < -66, "ball didn't get through: " + P(g.Player.Pos));
        // standing up inside the tunnel isn't allowed
        g = New(); Place(g, new Vector3(-25, 0, -52), 0);
        Steps(g, Sec(1.6f), Fwd(Btn.Roll));
        Steps(g, Sec(0.5f), default);
        Check(g.Player.Pos.Z < -55.5f && g.Player.Pos.Z > -64.5f ? g.Player.Rolling : true, "stood up inside the tunnel");
    }

    static void RollDownhill()
    {
        var g = New();
        // near the top of the 12 degree slope (it rises westward from x = -44), facing down it (east)
        Place(g, new Vector3(-61f, 3.7f, 22), -MathF.PI / 2);
        Steps(g, Sec(0.2f), new InputFrame { Held = Btn.Roll });
        Steps(g, Sec(2.5f), new InputFrame { Held = Btn.Roll });
        float sp = MathX.FlatLen(g.Player.Vel);
        Check(sp > 8, $"rolling down a 12 degree slope without input only reached {sp:0.0} m/s at {P(g.Player.Pos)}");
    }

    static void JumpPads()
    {
        var report = new List<string>();
        void Pad(Vector3 pad, float top, float landZ)
        {
            var g = New(); Place(g, pad, 0);
            Steps(g, Sec(4), default);
            var p = g.Player.Pos;
            if (MathF.Abs(p.Y - top) > 0.05f || MathF.Abs(p.Z - landZ) > 2.1f) report.Add($"pad to {top} m ended at {P(p)}");
        }
        float[] pads = { 3, 6, 10 };
        for (int i = 0; i < pads.Length; i++) Pad(new Vector3(14 + i * 7, 0, 26), pads[i], 33);
        float[] ledges = { 2, 4, 8, 16 };
        for (int i = 0; i < ledges.Length; i++) Pad(new Vector3(26 + i * 6, 0, -55), ledges[i], -60);
        Check(report.Count == 0, string.Join("; ", report));
    }

    // ---------------------------------------------------------------- zones
    static void AcidAndHeal()
    {
        var g = New(); Place(g, new Vector3(-16, 0, 24), 0);
        float start = g.Player.Hp + g.Player.Shield;
        Steps(g, Sec(2), default);
        float lost = start - g.Player.Hp - g.Player.Shield;
        Check(MathF.Abs(lost - 40) < 2, $"2 s of acid took {lost:0.0}, expected 40");
        Place(g, new Vector3(-4, 0, 24), 0);
        float before = g.Player.Hp;
        Steps(g, Sec(1), default);
        Check(g.Player.Hp > before + 1 || g.Player.Hp >= Player.MaxHp, $"heal pad: {before:0} -> {g.Player.Hp:0}");
    }

    static void PitAndCheckpoint()
    {
        var g = New(); Place(g, new Vector3(0, 0, 41), MathF.PI);
        Steps(g, Sec(2), new InputFrame { Move = new Vector2(0.5f, 1) });
        Check(!g.Player.Alive, "didn't die in the pit: " + P(g.Player.Pos));
        Steps(g, Sec(2.5f), default);
        Check(g.Player.Alive && Vector3.Distance(g.Player.Pos, g.Level.Spawn) < 0.2f, "respawn not at spawn: " + P(g.Player.Pos));
        // walk the beam to the checkpoint
        Place(g, new Vector3(0, 0, 42), MathF.PI);
        Steps(g, Sec(2.2f), Fwd());
        Check(g.RespawnPos.Z > 55, "checkpoint not taken: player at " + P(g.Player.Pos));
    }

    static void LowGravityAndUpdraft()
    {
        var g = New(); Place(g, new Vector3(32, 0, 64), 0);
        Steps(g, Sec(4), default, Btn.Jump);
        float expect = Player.JumpSpeed * Player.JumpSpeed / (2 * Player.Gravity * 0.3f);
        Check(MathF.Abs(g.Player.LastJumpHeight - expect) < 0.3f, $"low-gravity jump {g.Player.LastJumpHeight:0.00} m, expected {expect:0.00}");
        g = New(); Place(g, new Vector3(-30, 0, 64), 0);
        float peak = 0;
        for (int i = 0; i < Sec(5); i++) { Steps(g, 1, default); peak = MathF.Max(peak, g.Player.Pos.Y); }
        Check(peak > 15, $"updraft only lifted to {peak:0.0} m");
    }

    static void Boost()
    {
        var g = New(); Place(g, new Vector3(-4, 0, 90), MathF.PI / 2);
        float best = 0;
        for (int i = 0; i < Sec(1.5f); i++) { Steps(g, 1, Fwd()); best = MathF.Max(best, MathX.FlatLen(g.Player.Vel)); }
        Check(best > 25, $"boost peak {best:0.0} m/s");
    }

    static void Pickups()
    {
        var g = New();
        Check(g.Weapons.Count == 1, "should start with the pistol only");
        Place(g, new Vector3(14, 0, -4), 0); Steps(g, 5, default);
        Place(g, new Vector3(14, 0, 4), 0); Steps(g, 5, default);
        Check(g.Weapons.Count == 3, $"have {g.Weapons.Count} weapons after the armory");
        Check(g.Weapon.Def == WeaponDef.Lobber, "should switch to the newest weapon");
    }

    // ---------------------------------------------------------------- combat
    static void ShootDummy()
    {
        var g = New(); Place(g, new Vector3(18, 0, -12), -MathF.PI / 2);
        var dm = g.Dummies[0];   // 10 m, at (30, 0, -12)
        AimAt(g, dm.Pos + new Vector3(0, 1.0f, 0));
        for (int i = 0; i < 5; i++) { Steps(g, 1, new InputFrame { Held = Btn.Fire }, Btn.Fire); Steps(g, Sec(0.25f), default); }
        Check(!dm.Alive, $"dummy still has {dm.Hp} hp after 5 body shots (hits {g.ShotsHit}/{g.ShotsFired})");
        Check(g.Weapon.Mag == 7, $"magazine at {g.Weapon.Mag}, expected 7");
        Steps(g, Sec(3.2f), default);
        Check(dm.Alive && dm.Hp == dm.MaxHp, "dummy didn't respawn");
        // headshots double
        AimAt(g, dm.Pos + new Vector3(0, dm.Height - 0.2f, 0));
        for (int i = 0; i < 3; i++) { Steps(g, 1, new InputFrame { Held = Btn.Fire }, Btn.Fire); Steps(g, Sec(0.25f), default); }
        Check(!dm.Alive, $"3 headshots left {dm.Hp} hp");
    }

    static void LobberBlast()
    {
        var g = New(); g.GiveWeapon(WeaponDef.Lobber);
        // a ball: blast right next to it
        var ball = g.Props[0];
        g.Explode(ball.Pos + new Vector3(1.5f, 0, 0), 4, 70, 10, false);
        Steps(g, 3, default);
        Check(ball.Vel.Length() > 2, $"ball barely moved: {ball.Vel.Length():0.0} m/s");
        // fire one at the 20 m dummy
        Place(g, new Vector3(18, 0, -6), -MathF.PI / 2);
        var dm = g.Dummies[1];
        AimAt(g, dm.Pos + new Vector3(0, 0.8f, 0));
        // lob: aim a bit higher to allow for the drop
        g.CamPitch += 0.08f; g.Frame(0, default);
        Steps(g, 1, new InputFrame { Held = Btn.Fire }, Btn.Fire);
        Steps(g, Sec(2), default);
        Check(dm.Hp < dm.MaxHp || !dm.Alive, $"lobber missed: dummy at {dm.Hp} hp");
    }

    static void PunchAndPound()
    {
        var g = New(); var dm = g.Dummies[0];
        Place(g, dm.Pos + new Vector3(-1.4f, 0, 0), -MathF.PI / 2);
        AimAt(g, dm.Pos + new Vector3(0, 1.2f, 0));
        Steps(g, 2, default, Btn.Punch);
        Check(dm.Hp == dm.MaxHp - Game.PunchDamage, $"punch: dummy at {dm.Hp}");
        // pound from 6 m up next to the dummy
        g = New(); dm = g.Dummies[0];
        Place(g, dm.Pos + new Vector3(-2f, 6, 0), 0);
        g.Teleport(dm.Pos + new Vector3(-2f, 6, 0), 0);
        Steps(g, 3, new InputFrame { Held = Btn.Roll });
        Steps(g, Sec(1), new InputFrame { Held = Btn.Roll }, Btn.Punch);
        Check(dm.Hp < dm.MaxHp, $"ground pound missed: dummy at {dm.Hp}, player {P(g.Player.Pos)} {g.Player.LastEvent}");
    }

    // ---------------------------------------------------------------- soak
    static void Soak()
    {
        var g = New(); var rng = new Rng(99);
        var input = new InputFrame();
        var dl = new DrawList();
        for (int lm = 0; lm < g.Level.Landmarks.Count; lm++)
        {
            Steps(g, 1, default, Btn.NextZone);
            for (int i = 0; i < Sec(6); i++)
            {
                if (i % 20 == 0)
                {
                    input.Move = new Vector2(rng.Range(-1, 1), rng.Range(-0.3f, 1));
                    input.Held = 0;
                    foreach (var b in new[] { Btn.Jump, Btn.Roll, Btn.Fire, Btn.Aim, Btn.Punch })
                        if (rng.Float() < 0.25f) input.Held |= b;
                    input.Look = new Vector2(rng.Range(-0.05f, 0.05f), rng.Range(-0.02f, 0.02f));
                }
                var f = input;
                if (i % 20 == 0) f.Pressed = input.Held | (rng.Float() < 0.1f ? Btn.WeaponNext : 0) | (rng.Float() < 0.05f ? Btn.Reload : 0);
                g.Frame(Game.DT, f);
                var p = g.Player;
                Check(MathX.Finite(p.Pos) && MathX.Finite(p.Vel), "NaN at " + g.Level.Landmarks[lm].Name);
                Check(!p.Alive || p.Pos.Y > g.Level.KillY - 1, "fell through the world at " + g.Level.Landmarks[lm].Name + " " + P(p.Pos));
                Check(!p.Alive || p.Noclip || !p.BodyBlocked(g, p.Pos) || p.Rolling, $"stuck in geometry at {g.Level.Landmarks[lm].Name} {P(p.Pos)}");
                foreach (var pr in g.Props) Check(MathX.Finite(pr.Pos), "ball NaN");
                if (i % 60 == 0) g.Draw(dl, 1280, 720);
            }
        }
    }

    // ---------------------------------------------------------------- screenshots
    static void Shots(string dir, int w, int h)
    {
        Directory.CreateDirectory(dir);
        var sr = new SoftRenderer(w, h); var dl = new DrawList();
        void Shot(string name, Game g)
        {
            g.Frame(0, default); g.Draw(dl, w, h); sr.Render(dl);
            var path = Path.Combine(dir, name + ".png"); sr.SavePng(path);
            Console.WriteLine("  shot  " + path);
        }
        var g = New(); g.ShowHelp = true;
        Shot("01-spawn", g);
        g.ShowHelp = false; g.ShowDebug = true;
        foreach (var (name, i) in new[] { ("02-steps", 1), ("03-ramps", 2), ("04-combat", 8), ("05-physics", 9), ("06-zones", 11), ("07-pads", 13) })
        {
            var m = g.Level.Landmarks[i]; g.Teleport(m.Pos, m.Yaw); Steps(g, 30, default);
            g.CamPitch = -0.12f;
            Shot(name, g);
        }
        // overview from above the hub
        g.ShowDebug = false; g.Player.Noclip = true;
        g.Teleport(new Vector3(0, 70, 95), 0); g.CamPitch = -0.55f; Steps(g, 2, default);
        Shot("08-overview", g);
        // shooting at the dummies
        g = New(); g.GiveWeapon(WeaponDef.Rifle);
        g.Teleport(new Vector3(18, 0, 2), -MathF.PI / 2); Steps(g, 20, default);
        AimAt(g, g.Dummies[2].Pos + new Vector3(0, 1.4f, 0));
        Steps(g, 25, new InputFrame { Held = Btn.Fire | Btn.Aim }, Btn.Fire);
        Shot("09-firing", g);
    }
}

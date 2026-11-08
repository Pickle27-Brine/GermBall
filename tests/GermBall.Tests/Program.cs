using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;
using GermBall.Sim;

namespace GermBall.Tests;

/// <summary>
/// Headless checks for Germ Ball 2.0.
///   dotnet run --project tests/GermBall.Tests                 run every check
///   dotnet run --project tests/GermBall.Tests -- shots DIR    also render screenshots of the debug level into DIR
/// </summary>
public static class Program
{
    static int failed, passed;

    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "export-content") { ExportContent(args[1]); return 0; }
        if (args.Length >= 2 && args[0] == "editor-shots") { EditorShots(args[1]); return 0; }
        Run("spawn settles on the pad", SpawnSettles);
        Run("run speed builds slowly", WalkSpeed);
        Run("inertia: coasting, skids, wide turns", Inertia);
        Run("step heights", StepHeights);
        Run("ramp angles", RampAngles);
        Run("stairs", Stairs);
        Run("jumps curl into a ball", JumpCurl);
        Run("jump height, short hop, air dash", JumpAndDash);
        Run("crawl tunnel needs a spin dash", CrawlTunnel);
        Run("rolling: downhill speeds up, flat slows down", RollDownhill);
        Run("jump pads land on their ledges", JumpPads);
        Run("acid hurts, heal pad heals", AcidAndHeal);
        Run("pit kills, respawn, checkpoint", PitAndCheckpoint);
        Run("low gravity and updraft", LowGravityAndUpdraft);
        Run("boost pad", Boost);
        Run("pickups give weapons", Pickups);
        Run("pistol kills a dummy", ShootDummy);
        Run("lobber blast moves balls and hurts dummies", LobberBlast);
        Run("punch and ground pound", PunchAndPound);
        Run("hero rig: limb lengths, feet on the floor, planted feet", HeroRig);
        Run("jump dashes floor germs (1 / 2 / 4)", DashFloorsGerms);
        Run("germ contact hurts; spin dash bowls germs over", GermContactAndRoll);
        Run("shooting a germ to death, and its respawn", ShootGermDead);
        Run("the hero ragdolls on death", PlayerRagdoll);
        Run("content: entity and map files round-trip", ContentRoundTrip);
        Run("content: a level rebuilt from its map is identical", MapRebuild);
        Run("animation: clips, layers, masks, additive, crossfades", AnimationLayers);
        Run("camera: locked behind the hero, aim eases in", CameraLock);
        Run("entity editor: shapes, colour, bones, keys, layers, undo, save, test in game", EntityEditorWorkflow);
        Run("map editor: add, pick, gizmo with snapping, properties, undo, save, play test", MapEditorWorkflow);
        Run("editors survive random clicking and typing", EditorFuzz);
        Run("random input soak (every landmark)", Soak);

        if (args.Length >= 2 && args[0] == "shots") Shots(args[1], args.Length >= 4 ? int.Parse(args[2]) : 1280, args.Length >= 4 ? int.Parse(args[3]) : 720);

        Console.WriteLine($"\n{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("  ok    " + name); passed++; }
        catch (Exception e)
        {
            string where = e.GetType() == typeof(Exception) ? "" : "\n        " + (e.StackTrace ?? "").Split('\n')[0].Trim();
            Console.WriteLine("  FAIL  " + name + "\n        " + e.Message.Replace("\n", "\n        ") + where); failed++;
        }
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
        // speed builds slowly (about 2.6 s to top speed), as in 1.0
        var g = New(); var start = g.Player.Pos;
        Steps(g, Sec(1), Fwd());
        float sp1 = MathX.FlatLen(g.Player.Vel);
        Check(MathF.Abs(sp1 - Player.Accel) < 0.4f, $"after 1 s: {sp1:0.00} m/s, expected about {Player.Accel:0.0}");
        Steps(g, Sec(3), Fwd());
        float top = MathX.FlatLen(g.Player.Vel);
        Check(MathF.Abs(top - Player.TopSpeed) < 0.1f, $"after 4 s: {top:0.00} m/s, expected top speed {Player.TopSpeed}");
        Check(g.Player.Pos.Z < start.Z, "forward should be north (-Z)");
    }

    static void Inertia()
    {
        // let go at top speed: coasts for about 2 s
        var g = New(); RunStart(g);
        var p0 = g.Player.Pos;
        Steps(g, Sec(1), default);
        float coast = MathX.FlatLen(g.Player.Vel);
        Check(coast > 4 && coast < 5.5f, $"1 s after letting go: {coast:0.00} m/s");
        Steps(g, Sec(1.5f), default);
        Check(MathX.FlatLen(g.Player.Vel) < 0.01f, "still moving 2.5 s after letting go");
        float slide = MathX.FlatLen(g.Player.Pos - p0);
        Check(slide > 8 && slide < 12, $"coasted {slide:0.0} m");
        // reverse at top speed: skids to a stop, then runs the other way
        g = New(); RunStart(g);
        bool skid = false;
        for (int i = 0; i < Sec(0.4f); i++) { Steps(g, 1, new InputFrame { Move = new Vector2(0, -1) }); skid |= g.Player.Skidding; }
        Check(skid, "no skid when reversing at speed");
        Check(g.Player.Vel.Z > 0, $"should be heading back south after the skid, vel {P(g.Player.Vel)}");
        // turning at speed is wide: a quarter turn takes a while
        g = New(); RunStart(g);
        Steps(g, Sec(0.2f), new InputFrame { Move = new Vector2(1, 0) });
        float ang = MathF.Atan2(g.Player.Vel.X, -g.Player.Vel.Z) / MathX.Deg;
        Check(ang > 20 && ang < 60, $"turned {ang:0} degrees in 0.2 s at top speed");
    }

    /// <summary>Gets the player running north at top speed.</summary>
    static void RunStart(Game g) { g.Player.Vel = new Vector3(0, 0, -Player.TopSpeed); Steps(g, 2, Fwd()); }

    static void StepHeights()
    {
        float[] heights = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.8f };
        var report = new List<string>();
        for (int i = 0; i < heights.Length; i++)
        {
            var g = New();
            Place(g, new Vector3(-15 + i * 5, 0, -17.5f), 0);
            bool onTop = false;
            for (int k = 0; k < Sec(2.5f); k++) { Steps(g, 1, Fwd()); onTop |= MathF.Abs(g.Player.Pos.Y - heights[i]) < 0.03f && g.Player.Grounded; }
            bool expect = heights[i] <= Player.StepHeight;
            if (onTop != expect) report.Add($"{heights[i]} m: {(onTop ? "climbed" : "blocked")}, ended at {P(g.Player.Pos)}");
        }
        Check(report.Count == 0, string.Join("; ", report));
    }

    static void RampAngles()
    {
        // with a running start, everything up to the slope limit can be run up; steeper can't
        float[] angles = { 10, 20, 30, 40, 45, 50, 55, 60 };
        var report = new List<string>();
        for (int i = 0; i < angles.Length; i++)
        {
            var g = New();
            Place(g, new Vector3(-17.5f + i * 5, 0, -29), 0); RunStart(g);
            float peak = 0;
            for (int k = 0; k < Sec(3f); k++) { Steps(g, 1, Fwd()); if (g.Player.Grounded) peak = MathF.Max(peak, g.Player.Pos.Y); }
            bool top = peak > 2.45f;
            bool expect = angles[i] <= Player.MaxSlopeDeg;
            if (top != expect) report.Add($"{angles[i]} deg: {(top ? "climbed" : "failed")}, highest footing {peak:0.00} m");
        }
        Check(report.Count == 0, string.Join("; ", report));
        // from a standstill, slopes push back harder than you can accelerate (as in 1.0)
        var h = New(); Place(h, new Vector3(2.5f, 0, -31.5f), 0);   // on the 45 degree ramp
        h.Teleport(new Vector3(2.5f, 1.5f, -31.5f), 0); Steps(h, Sec(3), Fwd());
        Check(h.Player.Pos.Y < 2.4f, $"walked up a 45 degree slope from a standstill to {h.Player.Pos.Y:0.00} m");
    }

    static void Stairs()
    {
        float Peak(float x)
        {
            var g = New(); Place(g, new Vector3(x, 0, -28), 0);
            float peak = 0;
            for (int k = 0; k < Sec(4f); k++) { Steps(g, 1, Fwd()); peak = MathF.Max(peak, g.Player.Pos.Y); }
            return peak;
        }
        float a = Peak(-30), b = Peak(-25);
        Check(a > 2.95f, $"0.25 m stairs: only reached {a:0.00} m");
        Check(b < 0.1f, $"0.5 m stairs should need jumps, reached {b:0.00} m");
    }

    static void JumpCurl()
    {
        var g = New();
        Steps(g, 1, new InputFrame { Held = Btn.Jump }, Btn.Jump);
        Check(g.Player.Rolling && !g.Player.Grounded, "jumping should curl into a ball");
        Steps(g, Sec(1.5f), new InputFrame { Held = Btn.Jump });
        Check(g.Player.Grounded && !g.Player.Rolling, "should land on its feet");
        // holding roll through the landing keeps the ball rolling on
        g = New(); RunStart(g);
        Steps(g, Sec(1.2f), new InputFrame { Move = new Vector2(0, 1), Held = Btn.Jump | Btn.Roll }, Btn.Jump);
        Check(g.Player.Grounded && g.Player.Rolling, "holding roll should land rolling");
        // curled up in the air, you fit through gaps the standing body doesn't
        Check(Player.BallRadius * 2 < Player.Height, "ball should be smaller than the body");
    }

    static void JumpAndDash()
    {
        var g = New();
        Steps(g, Sec(1.5f), new InputFrame { Held = Btn.Jump }, Btn.Jump);
        float expect = Player.JumpSpeed * Player.JumpSpeed / (2 * Player.Gravity);
        Check(MathF.Abs(g.Player.LastJumpHeight - expect) < 0.1f, $"held jump {g.Player.LastJumpHeight:0.00} m, expected {expect:0.00}");
        float full = g.Player.LastJumpHeight;
        g = New();
        Steps(g, Sec(1.5f), default, Btn.Jump);   // tapped
        Check(g.Player.LastJumpHeight < full * 0.6f, $"tapped jump {g.Player.LastJumpHeight:0.00} m should be a short hop (full {full:0.00})");
        // jump forward, compare with jump + dash
        float Dist(bool dash)
        {
            var h = New(); var s = h.Player.Pos;
            Steps(h, Sec(0.3f), Fwd(Btn.Jump), Btn.Jump);
            if (dash) Steps(h, 1, Fwd(Btn.Jump), Btn.Jump);
            Steps(h, Sec(1.2f), default);
            return MathX.FlatLen(h.Player.Pos - s);
        }
        float plain = Dist(false), dashed = Dist(true);
        Check(dashed > plain + 3f, $"dash adds too little: {plain:0.0} m vs {dashed:0.0} m");
    }

    static void CrawlTunnel()
    {
        var g = New(); Place(g, new Vector3(-25, 0, -52), 0);
        Steps(g, Sec(3), Fwd());
        Check(g.Player.Pos.Z > -55.2f, "walked into the 1.1 m tunnel: " + P(g.Player.Pos));
        // spin dash in: press roll while still, rev twice, let go
        Steps(g, Sec(0.3f), new InputFrame { Held = Btn.Roll, Move = new Vector2(0, 1) }, Btn.Roll);
        Check(g.Player.Charging, "roll while still should start a spin dash");
        Steps(g, 1, new InputFrame { Held = Btn.Roll | Btn.Jump }, Btn.Jump);
        Steps(g, Sec(0.2f), new InputFrame { Held = Btn.Roll });
        Steps(g, 1, new InputFrame { Held = Btn.Roll | Btn.Jump }, Btn.Jump);
        Steps(g, Sec(0.2f), new InputFrame { Held = Btn.Roll });
        Check(g.Player.Rev > 3, $"revs {g.Player.Rev:0.0}");
        Steps(g, 1, default);
        float launch = g.Player.Vel.Length();
        Check(launch > Player.SpinDashBase + 2, $"spin dash launched at {launch:0.0} m/s");
        Steps(g, Sec(3), default);
        Check(g.Player.Pos.Z < -66, "ball didn't get through: " + P(g.Player.Pos));
        // standing up inside the tunnel isn't allowed
        g = New(); g.Teleport(new Vector3(-25, 0, -60), 0); g.Player.Rolling = true;
        Steps(g, Sec(1), default);
        Steps(g, 2, default, Btn.Roll);
        Check(g.Player.Rolling, "stood up inside the tunnel");
    }

    static void RollDownhill()
    {
        var g = New();
        // near the top of the 12 degree slope (it rises westward from x = -44), facing down it (east)
        Place(g, new Vector3(-61f, 3.7f, 22), -MathF.PI / 2);
        g.Player.Vel = new Vector3(3, 0, 0);
        Steps(g, 1, new InputFrame { Held = Btn.Roll }, Btn.Roll);
        Check(g.Player.Rolling && !g.Player.Charging, "moving + roll should roll, not spin dash");
        Steps(g, Sec(2.5f), new InputFrame { Held = Btn.Roll });
        float sp = MathX.FlatLen(g.Player.Vel);
        Check(sp > 8, $"rolling down a 12 degree slope without input only reached {sp:0.0} m/s at {P(g.Player.Pos)}");
        // on the flat a roll only slows down, and uncurls once slow (when roll isn't held)
        g = New(); RunStart(g);
        Steps(g, 1, Fwd(), Btn.Roll);
        Check(g.Player.Rolling, "roll at speed should curl up");
        float s0 = g.Player.Vel.Length();
        Steps(g, Sec(1), Fwd());
        Check(g.Player.Vel.Length() < s0, "a roll on the flat shouldn't gain speed from the stick");
        Steps(g, Sec(5), default);
        Check(!g.Player.Rolling, "should uncurl when the roll runs out");
    }

    static void JumpPads()
    {
        var report = new List<string>();
        void Pad(Vector3 pad, float top, float zMin, float zMax)
        {
            var g = New(); Place(g, pad, 0);
            Steps(g, Sec(5), default);
            var p = g.Player.Pos;
            if (MathF.Abs(p.Y - top) > 0.05f || p.Z < zMin || p.Z > zMax) report.Add($"pad to {top} m ended at {P(p)}");
        }
        float[] pads = { 3, 6, 10 };
        for (int i = 0; i < pads.Length; i++) Pad(new Vector3(14 + i * 7, 0, 26), pads[i], 31, 39);
        float[] ledges = { 2, 4, 8, 16 };
        for (int i = 0; i < ledges.Length; i++) Pad(new Vector3(26 + i * 6, 0, -55), ledges[i], -66, -58);
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
        Steps(g, Sec(3), new InputFrame { Move = new Vector2(0.5f, 1) });
        Check(!g.Player.Alive, "didn't die in the pit: " + P(g.Player.Pos));
        Steps(g, Sec(2.5f), default);
        Check(g.Player.Alive && Vector3.Distance(g.Player.Pos, g.Level.Spawn) < 0.2f, "respawn not at spawn: " + P(g.Player.Pos));
        // walk the beam to the checkpoint
        Place(g, new Vector3(0, 0, 42), MathF.PI);
        Steps(g, Sec(4f), Fwd());
        Check(g.RespawnPos.Z > 55, "checkpoint not taken: player at " + P(g.Player.Pos));
    }

    static void LowGravityAndUpdraft()
    {
        var g = New(); Place(g, new Vector3(32, 0, 64), 0);
        Steps(g, Sec(6), new InputFrame { Held = Btn.Jump }, Btn.Jump);
        float expect = Player.JumpSpeed * Player.JumpSpeed / (2 * Player.Gravity * 0.3f);
        Check(MathF.Abs(g.Player.LastJumpHeight - expect) < 0.5f, $"low-gravity jump {g.Player.LastJumpHeight:0.00} m, expected {expect:0.00}");
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
        // jump (which curls up) next to the dummy, then pound
        g = New(); dm = g.Dummies[0];
        Place(g, dm.Pos + new Vector3(-2f, 0, 0), 0);
        Steps(g, Sec(0.4f), new InputFrame { Held = Btn.Jump }, Btn.Jump);
        Steps(g, Sec(1), default, Btn.Punch);
        Check(dm.Hp < dm.MaxHp, $"ground pound missed: dummy at {dm.Hp}, player {P(g.Player.Pos)} {g.Player.LastEvent}");
        // a pound right above the floor is just a punch attempt (too low)
        g = New();
        Steps(g, 1, new InputFrame { Held = Btn.Jump }, Btn.Jump);
        Steps(g, 1, default, Btn.Punch);
        Check(!g.Player.Pounding, "pounded from a few centimetres up");
    }

    // ---------------------------------------------------------------- bodies
    static void HeroRig()
    {
        var g = New(); RunStart(g);
        var rig = g.Hero.Anim.Rig; var pose = new Pose(); var prev = new Pose(); var ctx = new AnimContext();
        float stanceSpeed = 0; int stanceN = 0, bad = 0; string why = "";
        for (int i = 0; i < Sec(2); i++)
        {
            Steps(g, 1, Fwd());
            prev.CopyFrom(pose);
            g.FillHeroContext(ctx, g.Player.Pos, g.Player.Yaw, 0); g.Hero.Evaluate(ctx); g.Hero.Anim.WorldJoints(g.Hero.Pose, pose);
            void Len(J a, J b, float want) { float l = Vector3.Distance(pose[a], pose[b]); if (MathF.Abs(l - want) > 0.01f) { bad++; why = $"{a}-{b} {l:0.00} vs {want:0.00}"; } }
            Len(J.HipL, J.KnL, rig.Thigh); Len(J.KnL, J.FtL, rig.Shin); Len(J.HipR, J.KnR, rig.Thigh); Len(J.KnR, J.FtR, rig.Shin);
            Len(J.ShL, J.ElL, rig.Upper); Len(J.ElL, J.HaL, rig.Fore); Len(J.ShR, J.ElR, rig.Upper); Len(J.ElR, J.HaR, rig.Fore);
            foreach (var ft in new[] { J.FtL, J.FtR }) if (pose[ft].Y < 0 || pose[ft].Y > 0.55f) { bad++; why = $"{ft} at height {pose[ft].Y:0.00}"; }
            if (i < 2) continue;
            // a foot in its stance half should stay (nearly) put on the ground
            for (int side = 0; side < 2; side++)
            {
                float ph = g.Hero.Anim.Phase + side * 0.5f; ph -= MathF.Floor(ph);
                if (ph < 0.05f || ph > 0.45f) continue;
                var ft = side == 0 ? J.FtL : J.FtR;
                stanceSpeed += MathX.FlatLen(pose[ft] - prev[ft]) / Game.DT; stanceN++;
            }
        }
        Check(bad == 0, $"{bad} pose problems, e.g. {why}");
        float avg = stanceSpeed / Math.Max(1, stanceN), body = MathX.FlatLen(g.Player.Vel);
        Check(avg < body * 0.3f, $"planted feet slide at {avg:0.0} m/s while running at {body:0.0}");
    }

    static Germ GermOf(Game g, string k, int nth = 0)
    {
        foreach (var gm in g.Germs) if (gm.Name == k && nth-- == 0) return gm;
        throw new Exception("no " + k);
    }

    /// <summary>Hops at a germ from 6 m and dashes into it. Returns true if the dash connected.</summary>
    static bool DashInto(Game g, Germ gm)
    {
        Place(g, gm.Pos - new Vector3(6 + gm.Radius, 0, 0), -MathF.PI / 2);
        int before = gm.DashHits; bool wasDown = gm.Ragdoll != null; float hp = gm.Hp;
        Steps(g, Sec(0.12f), default, Btn.Jump);
        Steps(g, 1, Fwd(), Btn.Jump);
        for (int i = 0; i < Sec(1); i++)
        {
            Steps(g, 1, Fwd());
            if (gm.Hp < hp) return true;
        }
        return false;
    }

    static void DashFloorsGerms()
    {
        var g = New(); var c = GermOf(g, "coccus");
        Check(DashInto(g, c), "dash missed the coccus: player " + P(g.Player.Pos));
        Check(c.Ragdoll != null, "one dash should floor a coccus");
        Check(g.Player.Vel.Y > 3 && !g.Player.DashUsed, $"should bounce up with the dash back (vel {P(g.Player.Vel)}, dash used {g.Player.DashUsed})");
        // the ragdoll settles on the floor and gets back up
        Steps(g, Sec(1.2f), default);
        Check(c.Ragdoll != null && c.Ragdoll.Finite() && c.Ragdoll.Center.Y > 0.05f && c.Ragdoll.Center.Y < 1.2f, "ragdoll pelvis at " + (c.Ragdoll == null ? "-" : P(c.Ragdoll.Center)));
        Steps(g, Sec(1.5f), default);
        Check(c.Standing && MathF.Abs(c.Pos.Y) < 0.05f, $"coccus should be back up on the floor ({c.Standing}, {P(c.Pos)})");

        var b = GermOf(g, "brute");
        Check(DashInto(g, b), "dash missed the brute");
        Check(b.Standing && b.DashHits == 1, $"one dash shouldn't floor a brute (hits {b.DashHits})");
        Check(DashInto(g, b), "second dash missed the brute");
        Check(b.Ragdoll != null, "two dashes should floor a brute");

        var t = GermOf(g, "titan");
        for (int i = 1; i <= 4; i++)
        {
            Check(DashInto(g, t), $"dash {i} missed the titan");
            Check(i < 4 ? t.Standing : t.Ragdoll != null, $"titan after {i} dashes: {(t.Standing ? "standing" : "down")}");
        }
    }

    static void GermContactAndRoll()
    {
        // walking into a germ hurts and shoves you away
        var g = New(); var bac = GermOf(g, "bacillus");
        Place(g, bac.Pos - new Vector3(3, 0, 0), -MathF.PI / 2);
        float before = g.Player.Hp + g.Player.Shield;
        Steps(g, Sec(1.5f), Fwd());
        Check(g.Player.Hp + g.Player.Shield < before, "walking into a bacillus didn't hurt");
        Check(bac.Standing && bac.Hp == bac.MaxHp, "walking into it shouldn't hurt the germ");
        // a spin dash bowls a coccus over
        g = New(); var c = GermOf(g, "coccus");
        Place(g, c.Pos - new Vector3(5, 0, 0), -MathF.PI / 2);
        Steps(g, Sec(0.3f), new InputFrame { Held = Btn.Roll, Move = new Vector2(0, 1) }, Btn.Roll);
        Steps(g, 1, new InputFrame { Held = Btn.Roll | Btn.Jump }, Btn.Jump);
        Steps(g, Sec(0.2f), new InputFrame { Held = Btn.Roll });
        Steps(g, Sec(0.6f), default);
        Check(c.Hp < c.MaxHp && (c.Ragdoll != null || !c.Alive), $"spin dash into a coccus: hp {c.Hp}, down {c.Ragdoll != null}, player {P(g.Player.Pos)}");
    }

    static void ShootGermDead()
    {
        var g = New(); var bac = GermOf(g, "bacillus");
        Place(g, bac.Pos - new Vector3(10, 0, 0), -MathF.PI / 2);
        AimAt(g, bac.Pos + new Vector3(0, 1.0f, 0));
        for (int i = 0; i < 5; i++) { Steps(g, 1, new InputFrame { Held = Btn.Fire }, Btn.Fire); Steps(g, Sec(0.25f), default); }
        Check(!bac.Alive, $"bacillus has {bac.Hp} hp after 5 pistol shots");
        Check(bac.Ragdoll != null, "dead germs should ragdoll");
        Steps(g, Sec(6.5f), default);
        Check(bac.Standing && bac.Hp == bac.MaxHp, "dead germ should come back");
    }

    static void PlayerRagdoll()
    {
        var g = New(); RunStart(g);
        g.Player.Die("test");
        Steps(g, 2, default);
        Check(g.PlayerRagdoll != null, "no ragdoll on death");
        Steps(g, Sec(1.5f), default);
        var c = g.PlayerRagdoll.Center;
        Check(g.PlayerRagdoll.Finite() && c.Y > 0.05f && c.Y < 0.8f, "hero ragdoll pelvis at " + P(c));
        Steps(g, Sec(1), default);
        Check(g.Player.Alive && g.PlayerRagdoll == null, "should respawn and clear the ragdoll");
    }

    // ---------------------------------------------------------------- content
    /// <summary>Writes the built-in entities and the debug map as content files (the repository's content/ folder is made this way).</summary>
    static void ExportContent(string dir)
    {
        var store = new ContentStore(dir);
        foreach (var n in DefaultEntities.Names) store.SaveEntity(DefaultEntities.Get(n));
        var map = Level.BuildDebug().Map; map.Name = "debug";
        store.SaveMap(map);
        Console.WriteLine($"wrote {DefaultEntities.Names.Length} entities and 1 map to {Path.GetFullPath(dir)}");
    }

    static void ContentRoundTrip()
    {
        foreach (var n in DefaultEntities.Names)
        {
            var e = DefaultEntities.Get(n);
            var back = Json.Read<EntityDef>(Json.Write(e));
            Check(Json.Write(back) == Json.Write(e), $"{n} changed after a round trip");
            Check(back.Bones.Count == e.Bones.Count && back.Shapes.Count == e.Shapes.Count && back.Shapes[0].Size == e.Shapes[0].Size, $"{n} lost data");
        }
        var m = Level.BuildDebug().Map;
        var mb = Json.Read<MapDef>(Json.Write(m));
        Check(Json.Write(mb) == Json.Write(m), "map changed after a round trip");
        // a store on disk: save, forget, load
        var dir = Path.Combine(Path.GetTempPath(), "gb-content-" + Environment.ProcessId);
        var store = new ContentStore(dir);
        var hero = DefaultEntities.Hero(); hero.Name = "tester"; hero.Stats.Hp = 123; store.SaveEntity(hero);
        store.Forget();
        Check(store.Entity("tester").Stats.Hp == 123, "saved entity didn't load back");
        Directory.Delete(dir, true);
    }

    static void MapRebuild()
    {
        var a = Level.BuildDebug();
        var b = Level.FromMap(Json.Read<MapDef>(Json.Write(a.Map)));
        Check(a.Solids.Count == b.Solids.Count && a.Zones.Count == b.Zones.Count && a.Germs.Count == b.Germs.Count && a.Labels.Count == b.Labels.Count
              && a.Landmarks.Count == b.Landmarks.Count && a.Dummies.Count == b.Dummies.Count && a.Props.Count == b.Props.Count && a.Pickups.Count == b.Pickups.Count,
              "object counts differ");
        for (int i = 0; i < a.Solids.Count; i++)
        {
            var x = a.Solids[i].Box; var y = b.Solids[i].Box;
            Check(Vector3.Distance(x.Center, y.Center) < 1e-3f && Vector3.Distance(x.Half, y.Half) < 1e-3f && Vector3.Distance(x.AxisZ, y.AxisZ) < 1e-3f, $"solid {i} differs");
        }
        for (int i = 0; i < a.Zones.Count; i++) Check(Vector3.Distance(a.Zones[i].Dir, b.Zones[i].Dir) < 1e-3f && Vector3.Distance(a.Zones[i].Min, b.Zones[i].Min) < 1e-3f, $"zone {a.Zones[i].Name} differs");
    }

    static void AnimationLayers()
    {
        var e = DefaultEntities.Coccus();
        e.Layers.Clear(); e.Clips.Clear(); e.States.Clear();
        // a clip that turns the head 90 degrees halfway through, and the chest 30
        var c = new ClipDef { Name = "turn", Length = 1, Loop = true };
        c.Tracks.Add(new TrackDef { Bone = "head", Keys = { new KeyDef { T = 0 }, new KeyDef { T = 0.5f, Rot = new Vector3(0, 90, 0), Ease = "linear" } } });
        c.Tracks.Add(new TrackDef { Bone = "chest", Keys = { new KeyDef { T = 0 }, new KeyDef { T = 0.5f, Rot = new Vector3(0, 30, 0) } } });
        e.Clips.Add(c);
        var keyAt = Animator.SampleTrack(c.Tracks[0], 0.25f, 1, true);
        float ang = 2 * MathF.Acos(MathF.Min(1, MathF.Abs(keyAt.Rot.W))) / MathX.Deg;
        Check(MathF.Abs(ang - 45) < 0.5f, $"linear key halfway: {ang:0.0} degrees");
        var wrap = Animator.SampleTrack(c.Tracks[0], 0.75f, 1, true);   // between the last key and the first, looping
        ang = 2 * MathF.Acos(MathF.Min(1, MathF.Abs(wrap.Rot.W))) / MathX.Deg;
        Check(MathF.Abs(ang - 45) < 0.5f, $"looping wrap: {ang:0.0} degrees");

        float HeadYaw(EntityDef def, float t, string state = "idle")
        {
            var v = new EntityView(def); var ctx = new AnimContext { Dt = t, State = state };
            v.Update(ctx); v.Evaluate(ctx);
            int h = v.Sk.IndexOf("head");
            var q = v.Pose.Local[h].Rot; return MathF.Atan2(2 * (q.W * q.Y + q.X * q.Z), 1 - 2 * (q.Y * q.Y + q.X * q.X)) / MathX.Deg;
        }
        e.Layers.Add(new LayerDef { Name = "turn", Source = "clip", Clip = "turn", Mode = "override", Weight = 1 });
        Check(MathF.Abs(HeadYaw(e, 0.5f) - 90) < 1, $"override layer: head at {HeadYaw(e, 0.5f):0.0}");
        e.Layers[0].Weight = 0.5f;
        Check(MathF.Abs(HeadYaw(e, 0.5f) - 45) < 1, $"half-weight layer: head at {HeadYaw(e, 0.5f):0.0}");
        e.Layers[0].Weight = 1; e.Layers[0].Mask = new List<string> { "chest" }; e.Layers[0].MaskChildren = false;
        Check(MathF.Abs(HeadYaw(e, 0.5f)) < 1, "a chest-only mask moved the head");
        e.Layers[0].MaskChildren = true;
        Check(MathF.Abs(HeadYaw(e, 0.5f) - 90) < 1, "mask with children should include the head");
        e.Layers[0].Mask.Clear();
        e.Layers.Add(new LayerDef { Name = "add", Source = "clip", Clip = "turn", Mode = "additive", Weight = 1 });
        Check(MathF.Abs(HeadYaw(e, 0.25f) - 90) < 1, $"additive on top: head at {HeadYaw(e, 0.25f):0.0} (expected 45 + 45)");
        // state layers pick the clip for the state, and crossfade
        e.Layers.Clear();
        e.Layers.Add(new LayerDef { Name = "states", Source = "state", Mode = "override" });
        e.States.Add(new StateClip { State = "hurt", Clip = "turn", Fade = 0.2f });
        Check(MathF.Abs(HeadYaw(e, 0.5f, "idle")) < 1, "idle has no clip mapped");
        Check(MathF.Abs(HeadYaw(e, 0.5f, "hurt") - 90) < 1, $"hurt plays its clip: {HeadYaw(e, 0.5f, "hurt"):0.0}");
        var view = new EntityView(e); var cx = new AnimContext { Dt = 0.5f, State = "hurt" };
        view.Update(cx); view.Evaluate(cx);
        cx.State = "idle"; cx.Dt = 0.1f; view.Update(cx); view.Evaluate(cx);
        var q2 = view.Pose.Local[view.Sk.IndexOf("head")].Rot; float mid = 2 * MathF.Acos(MathF.Min(1, MathF.Abs(q2.W))) / MathX.Deg;
        Check(mid > 20 && mid < 80, $"crossfade out of hurt should be partway, got {mid:0.0} degrees");
        // the procedural layer on a humanoid puts the feet on the floor
        var hero = new EntityView(DefaultEntities.Hero()); var hc = new AnimContext();
        hero.Update(hc); hero.Evaluate(hc);
        foreach (var f in new[] { "foot.L", "foot.R" }) { float y = hero.Pose.Joint(hero.Sk.IndexOf(f)).Y; Check(y > -0.01f && y < 0.1f, $"{f} at {y:0.000}"); }
    }

    static void CameraLock()
    {
        var g = New();
        g.Frame(Game.DT, new InputFrame { Look = new Vector2(0.8f, 0), Move = new Vector2(0, 1) });
        Steps(g, 2, Fwd());
        Check(MathF.Abs(MathX.AngleWrap(g.Player.Yaw - g.CamYaw)) < 1e-3f, "hero should face the camera's way");
        var behind = g.Player.Pos - MathX.Forward(g.CamYaw, 0) * 3;
        Check(Vector3.Distance(MathX.Flat(g.CamPos), MathX.Flat(behind)) < 2.2f, "camera should be behind the hero");
        Check(g.CamPos.Y - g.Player.Pos.Y < 2.4f, $"camera height {g.CamPos.Y - g.Player.Pos.Y:0.00} above the feet");
        // aim eases in over a fifth of a second
        float prev = g.AimBlend; int rising = 0;
        for (int i = 0; i < 30; i++) { g.Frame(1 / 120f, new InputFrame { Held = Btn.Aim }); if (g.AimBlend > prev) rising++; Check(g.AimBlend - prev <= 1 / 120f / Game.AimBlendTime + 1e-4f, "aim jumped"); prev = g.AimBlend; }
        Check(rising >= 20 && g.AimBlend > 0.99f, $"aim blend {g.AimBlend:0.00} after {rising} rising frames");
    }

    /// <summary>A scratch copy of the built-in content, so editor tests never touch the repository's files.</summary>
    static ContentStore ScratchContent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gb-editor-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..6]);
        ExportContent(dir);
        return new ContentStore(dir);
    }

    static void EditorShots(string dir)
    {
        Directory.CreateDirectory(dir);
        var ee = new GermBall.Tools.EntityEditor.EntityEditor(ScratchContent(), "hero");
        var d = new EditorDriver(1600, 900, (i, dt, dl) => ee.Frame(i, dt, 1600, 900, dl));
        d.Step(3);
        d.Shot(Path.Combine(dir, "ee-01-hero.png"));
        ee.LeftTab = 2; ee.SelShape = 5; d.Step(2);
        d.Shot(Path.Combine(dir, "ee-02-shapes.png"));
        ee.LeftTab = 1; ee.SelBone = "elbow.R"; ee.RightTab = 1; d.Step(2);
        d.Shot(Path.Combine(dir, "ee-03-rig-layers.png"));
        ee.RightTab = 0; ee.SelClip = 0; ee.SelBone = "chest"; ee.Playhead = 0.1f; d.Step(2);
        d.Shot(Path.Combine(dir, "ee-04-clips.png"));
        ee.RightTab = 3; ee.SoloClip = false; ee.Ctx.Speed = 6; ee.Ctx.Armed = true; ee.Ctx.Aiming = true; d.Step(30);
        d.Shot(Path.Combine(dir, "ee-05-preview-run.png"));
        ee.Load("brute"); ee.DropRagdoll(); d.Step(70);
        d.Shot(Path.Combine(dir, "ee-06-brute-ragdoll.png"));

        var me = new GermBall.Tools.MapEditor.MapEditor(ScratchContent(), "debug");
        var m = new EditorDriver(1600, 900, (i, dt, dl) => me.Frame(i, dt, 1600, 900, dl));
        m.Step(2);
        m.Shot(Path.Combine(dir, "me-01-overview.png"));
        me.View.Eye = new Vector3(62, 10, 58); me.View.Yaw = -2.0f; me.View.Pitch = -0.35f;
        me.Sel = me.Map.Objects.FindIndex(o => o.Type == MapTypes.Germ && o.Entity == "brute"); m.Step(2);
        m.Shot(Path.Combine(dir, "me-02-germ-selected.png"));
        me.View.Eye = new Vector3(20, 8, 16); me.View.Yaw = MathF.PI; me.View.Pitch = -0.4f;
        me.Sel = me.Map.Objects.FindIndex(o => o.Type == MapTypes.Zone && o.Zone == "JumpPad"); m.Step(2);
        m.Shot(Path.Combine(dir, "me-03-jumppad.png"));
    }

    // ---------------------------------------------------------------- editors
    static void EntityEditorWorkflow()
    {
        var store = ScratchContent();
        var ee = new GermBall.Tools.EntityEditor.EntityEditor(store, "coccus");
        string launched = null; ee.LaunchGame = a => launched = a;
        var d = new EditorDriver(1600, 900, (i, dt, dl) => ee.Frame(i, dt, 1600, 900, dl));
        var ui = ee.Ui;
        d.Step(2);
        // shapes: add one through the Shapes tab and recolour it by typing a hex code
        GuiDriving.ClickLabel(ui, d, "Shapes");
        int before = ee.Def.Shapes.Count;
        GuiDriving.ClickLabel(ui, d, "Add");
        Check(ee.Def.Shapes.Count == before + 1 && ee.SelShape == before, $"Add didn't add a shape ({ee.Def.Shapes.Count})");
        GuiDriving.TypeInto(ui, d, "left/Name", "spike");
        Check(ee.CurShape.Name == "spike", $"name is {ee.CurShape.Name}");
        var colour = GuiDriving.Find(ui, d, "Colour");
        float sw = colour.W * 1.6f;
        d.Click(colour.X + sw + 20, colour.Y + colour.W / 2);
        for (int i = 0; i < 10; i++) d.Key(GermBall.Engine.Keys.Backspace);
        d.Type("#12AB34"); d.Key(GermBall.Engine.Keys.Enter);
        Check(ee.CurShape.Color == "#12AB34", $"colour is {ee.CurShape.Color}");
        // drag the X position field to the right
        var pos = GuiDriving.Find(ui, d, "Position");
        float x0 = ee.CurShape.Pos.X;
        d.Drag(pos.X + 10, pos.Y + pos.W / 2, pos.X + 60, pos.Y + pos.W / 2);
        Check(ee.CurShape.Pos.X > x0 + 0.05f, $"dragging Position X moved it {ee.CurShape.Pos.X - x0:0.000}");
        // undo twice: the drag, then the colour
        d.Key('Z', ctrl: true);
        Check(MathF.Abs(ee.CurShape.Pos.X - x0) < 1e-4f, "undo didn't revert the drag");
        d.Key('Z', ctrl: true);
        Check(ee.CurShape.Color != "#12AB34", "undo didn't revert the colour");
        d.Key('Y', ctrl: true);
        Check(ee.CurShape.Color == "#12AB34", "redo didn't bring the colour back");

        // rig: rename a bone and everything that refers to it follows
        GuiDriving.ClickLabel(ui, d, "Rig");
        GuiDriving.ClickLabel(ui, d, "pelvis");
        Check(ee.SelBone == "pelvis", "clicking a bone in the list should select it");
        GuiDriving.ClickLabel(ui, d, "Add child bone");
        Check(ee.Def.Bones.Count == 17 && ee.Def.Bone(ee.SelBone).Parent == "pelvis", "Add child bone");
        GuiDriving.TypeInto(ui, d, "left/Name", "tail");
        Check(ee.Def.Bone("tail") != null, "rename to tail");

        // clips: make one, key the tail at frame 0 and at 0.5 s through the panel, then it animates
        GuiDriving.ClickLabel(ui, d, "Clips");
        GuiDriving.ClickLabel(ui, d, "New clip");
        var clip = ee.CurClip; Check(clip != null && clip.Tracks.Count == 0, "New clip");
        GuiDriving.ClickLabel(ui, d, "Set key");
        ee.Playhead = 0.5f; d.Step();
        var rot = GuiDriving.Find(ui, d, "Rotate");
        d.Drag(rot.X + 10, rot.Y + rot.W / 2, rot.X + 90, rot.Y + rot.W / 2);
        var tr = clip.Track("tail");
        Check(tr != null && tr.Keys.Count == 2 && tr.Keys[1].Rot.X > 10, $"keys on tail: {tr?.Keys.Count}, rot {tr?.Keys[^1].Rot}");
        // layers: add a clip layer for it
        GuiDriving.ClickLabel(ui, d, "Layers");
        int layers = ee.Def.Layers.Count;
        GuiDriving.ClickLabel(ui, d, "Add layer");
        Check(ee.Def.Layers.Count == layers + 1 && ee.Def.Layers[^1].Clip == ee.Def.Clips[0].Name, "Add layer");
        // states: map "hurt" to the new clip through its combo
        GuiDriving.ClickLabel(ui, d, "States");
        int ci = ee.Def.Clips.IndexOf(clip);
        GuiDriving.PickCombo(ui, d, "hurt", ci, hasNone: true);
        Check(ee.Def.States.Exists(st => st.State == "hurt" && st.Clip == clip.Name), "state combo didn't map hurt");

        // save under a new name, and the game's store sees it
        GuiDriving.ClickLabel(ui, d, "Entity");
        GuiDriving.TypeInto(ui, d, "left/Name", "spiky");
        GuiDriving.ClickLabel(ui, d, "Save");
        store.Forget();
        var saved = store.Entity("spiky");
        Check(saved.Bone("tail") != null && saved.Shapes.Exists(x => x.Name == "spike" && x.Color == "#12AB34") && saved.Clip(clip.Name) != null, "saved file is missing edits");
        var game = new Game(Level.BuildDebug(), store);   // the game loads the edited entity without complaint
        Check(game.Store.Entity("spiky").Kind == "enemy", "game store");
        GuiDriving.ClickLabel(ui, d, "Test in game");
        Check(launched == "--spawn spiky", $"test in game launched with \"{launched}\"");
        // the view picks shapes with a click: the coccus's body fills the middle of the view
        ee.LeftTab = 2; ee.SelShape = -1; ee.ShowBones = false; d.Step(2);
        var v = ee.View.Rect;
        d.Click(v.X + v.Z / 2, v.Y + v.W / 2);
        Check(ee.SelShape >= 0, "clicking the model in the view should select a shape");
    }

    static void MapEditorWorkflow()
    {
        var store = ScratchContent();
        var me = new GermBall.Tools.MapEditor.MapEditor(store, "debug");
        string launched = null; me.LaunchGame = a => launched = a;
        var d = new EditorDriver(1600, 900, (i, dt, dl) => me.Frame(i, dt, 1600, 900, dl));
        var ui = me.Ui;
        d.Step(2);
        GuiDriving.ClickLabel(ui, d, "New");
        Check(me.Map.Objects.Count == 1 && me.Map.Name == "new_map", "New map should have just a floor");
        // look down at the middle of the floor and add a box: it lands on the floor, snapped
        me.View.Eye = new Vector3(6, 12, 12); me.View.Yaw = 0; me.View.Pitch = -0.78f; d.Step();
        GuiDriving.ClickLabel(ui, d, "Box");
        var box = me.Cur;
        Check(box != null && box.Type == MapTypes.Box && MathF.Abs(box.Pos.Y - 0.5f) < 0.3f, $"box at {P(box?.Pos ?? default)}");
        Check(MathF.Abs(box.Pos.X / 0.5f - MathF.Round(box.Pos.X / 0.5f)) < 1e-3f, "box position should be snapped to 0.5");
        // pick: click on empty floor selects the floor, click on the box selects the box
        me.Sel = -1; d.Step(2);
        Check(me.View.Project(box.Pos + new Vector3(0, 0.5f, 0), out var bs), "box on screen");
        d.Click(bs.X, bs.Y);
        Check(me.Cur == box, $"clicking the box selected {me.Sel}");
        // drag the gizmo's X arrow: the box moves along X in snapped steps
        float x0 = box.Pos.X;
        me.View.Project(box.Pos, out var g0); me.View.Project(box.Pos + Vector3.UnitX * Vector3.Distance(me.View.CamPos, box.Pos) * 0.16f, out var g1);
        var mid = (g0 + g1) / 2;
        d.Drag(mid.X, mid.Y, mid.X + 120, mid.Y, 12);
        Check(box.Pos.X > x0 + 0.4f && MathF.Abs(box.Pos.X / 0.5f - MathF.Round(box.Pos.X / 0.5f)) < 1e-3f, $"gizmo drag: x {x0} -> {box.Pos.X}");
        d.Key('Z', ctrl: true);
        Check(MathF.Abs(me.Map.Objects[me.Sel >= 0 ? me.Sel : 1].Pos.X - x0) < 1e-3f || MathF.Abs(me.Map.Objects[1].Pos.X - x0) < 1e-3f, "undo the move");
        me.Sel = 1;
        // properties: type a size
        var size = GuiDriving.Find(ui, d, "Size");
        float w3 = (size.Z - 2 * ui.Gap) / 3;
        d.Click(size.X + w3 / 2, size.Y + size.W / 2, 1);   // right-click to type
        d.Type("7"); d.Key(GermBall.Engine.Keys.Enter);
        Check(MathF.Abs(me.Cur.Size.X - 7) < 1e-3f, $"typed size x: {me.Cur.Size.X}");
        // add a jump pad and aim it at a ledge with the helper
        GuiDriving.PickCombo(ui, d, "Zone kind", Array.IndexOf(Enum.GetNames(typeof(ZoneKind)), "JumpPad"));
        GuiDriving.ClickLabel(ui, d, "Zone: JumpPad");
        Check(me.Cur.Type == MapTypes.Zone && me.Cur.Zone == "JumpPad", "jump pad added");
        GuiDriving.ClickLabel(ui, d, "Compute launch");
        Check(me.Cur.Dir.Y > 5, $"pad launch {P(me.Cur.Dir)}");
        // a germ, rotated with R
        GuiDriving.ClickLabel(ui, d, "Germ: coccus");
        float yaw0 = me.Cur.Yaw; d.Key('R');
        Check(MathF.Abs(me.Cur.Yaw - yaw0 - 15) < 1e-3f, "R rotates 15 degrees");
        d.Key('D', ctrl: true);
        Check(me.Map.Objects.Count(o => o.Type == MapTypes.Germ) == 2, "ctrl+D duplicates");
        d.Key(GermBall.Engine.Keys.Delete);
        Check(me.Map.Objects.Count(o => o.Type == MapTypes.Germ) == 1, "Delete removes");
        // save, play test, and the game builds the same level from the file
        me.Sel = -1; d.Step();
        GuiDriving.TypeInto(ui, d, "Name", "arena");
        GuiDriving.ClickLabel(ui, d, "Play test");
        Check(launched == "--map arena", $"play test launched with \"{launched}\"");
        var map = store.Map("arena");
        Check(map != null && map.Objects.Count == me.Map.Objects.Count, "saved map");
        var game = new Game(Level.FromMap(map), store);
        Check(game.Germs.Count == 1 && game.Level.Zones.Exists(z => z.Kind == ZoneKind.JumpPad) && game.Level.Solids.Count == 2, "game level from the saved map");
        for (int i = 0; i < 120; i++) game.Frame(Game.DT, default);
        Check(game.Player.Alive && MathX.Finite(game.Player.Pos), "playing the new map");
    }

    static void EditorFuzz()
    {
        var rng = new Rng(5);
        foreach (var which in new[] { 0, 1 })
        {
            var store = ScratchContent();
            Action<UiInput, float, DrawList> frame; GermBall.Tools.Ui.Gui ui;
            if (which == 0) { var ee = new GermBall.Tools.EntityEditor.EntityEditor(store, "hero"); ee.LaunchGame = _ => { }; frame = (i, dt, dl) => ee.Frame(i, dt, 1280, 720, dl); ui = ee.Ui; }
            else { var me = new GermBall.Tools.MapEditor.MapEditor(store, "debug"); me.LaunchGame = _ => { }; frame = (i, dt, dl) => me.Frame(i, dt, 1280, 720, dl); ui = me.Ui; }
            var d = new EditorDriver(1280, 720, frame);
            d.Step();
            for (int k = 0; k < 400; k++)
            {
                float x = rng.Range(0, 1280), y = rng.Range(0, 720);
                switch (rng.Next() % 6)
                {
                    case 0: case 1: d.Click(x, y); break;
                    case 2: d.Drag(x, y, x + rng.Range(-80, 80), y + rng.Range(-40, 40), 4); break;
                    case 3: d.Click(x, y, 1); d.Type("1.5"); d.Key(GermBall.Engine.Keys.Enter); break;
                    case 4: d.In.Wheel = rng.Range(-2, 2); d.MoveTo(x, y); d.Step(); break;
                    default: d.Key(GermBall.Engine.Keys.Escape); break;
                }
            }
            d.Step(3);
        }
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
                foreach (var gm in g.Germs) Check(gm.Pose.Finite(), "germ NaN");
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
        // the germ yard, and a dash into a coccus caught mid-ragdoll
        g = New(); g.ShowDebug = false;
        var gy = g.Level.Landmarks.Find(m => m.Name == "Germ yard");
        g.Teleport(gy.Pos, gy.Yaw); Steps(g, 30, default); g.CamPitch = -0.1f;
        Shot("10-germ-yard", g);
        var cc = GermOf(g, "coccus", 1);
        DashInto(g, cc); Steps(g, Sec(0.25f), default);
        g.CamYaw = -MathF.PI / 2 + 0.3f; g.CamPitch = -0.25f;
        Shot("11-dash-ragdoll", g);
        // the hero running, close up
        g = New(); RunStart(g); Steps(g, Sec(0.4f), Fwd());
        g.CamYaw = g.Player.Yaw + 2.2f; g.CamPitch = -0.1f; Steps(g, 1, Fwd());
        Shot("12-hero-run", g);
        g.CamYaw = g.Player.Yaw + MathF.PI; Steps(g, Sec(0.5f), new InputFrame { Held = Btn.Aim }); g.Frame(0, new InputFrame { Held = Btn.Aim });
        g.Draw(dl, w, h); sr.Render(dl); sr.SavePng(Path.Combine(dir, "13-hero-aim.png")); Console.WriteLine("  shot  13-hero-aim");
        // shooting at the dummies
        g = New(); g.GiveWeapon(WeaponDef.Rifle);
        g.Teleport(new Vector3(18, 0, 2), -MathF.PI / 2); Steps(g, 20, default);
        AimAt(g, g.Dummies[2].Pos + new Vector3(0, 1.4f, 0));
        Steps(g, 25, new InputFrame { Held = Btn.Fire | Btn.Aim }, Btn.Fire);
        Shot("09-firing", g);
    }
}

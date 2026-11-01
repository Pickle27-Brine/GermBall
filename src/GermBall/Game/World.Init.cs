using System;
using System.Collections.Generic;
using GermBall.Core;

namespace GermBall.Game;

public sealed partial class World
{
    /// <summary>
    /// Builds a whole world for one boot configuration, in the same order the original's top-level script ran:
    /// layout, terrain, scene and materials, buildings and props, the leviathan interior, effects, waves, germ visuals,
    /// then the player, squads and companions.
    /// </summary>
    public World(BootConfig cfg, Settings set, Dictionary<string, string[]> binds)
    {
        CFG = cfg ?? new BootConfig();
        SET = set ?? new Settings();
        BINDS = binds ?? DefaultBinds();
        foreach (var d in BIND_DEF) if (!BINDS.ContainsKey(d.key)) BINDS[d.key] = new[] { d.a, d.b };
        chipMaxDyn = SET.debrisMax;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string what) { Platform.Log.Info($"world: {what} ({sw.ElapsedMilliseconds} ms)"); }

        InitLayout(); Mark("layout");
        InitTerrain(); Mark("terrain");
        InitTuning(); ApplyTuning();
        InitScene(); InitStructureMats(); BuildTerrainMesh(); Mark("terrain mesh");
        InitPropMats();
        BuildLayout(); Mark("buildings");
        PlaceProps();
        BuildLeviathan(); Mark("leviathan");
        InitSprites(); InitSpores();
        InitWaves();
        BuildGerms(); InitDrawLists(); InitReticles(); Mark("germs");

        HUMAN = MakeActor("hero", SPAWNX, SPAWNZ, true, MathF.PI); ME = HUMAN;
        P = HUMAN.P; PBODY = HUMAN.body; W = HUMAN.W; AIM = HUMAN.AIM; IN = HUMAN.IN; CUR = HUMAN;
        RespawnActor(HUMAN);
        InitTips();
        if (!WAVES.on && SET.density > 0) InitSquads();
        if (SET.companion) SpawnCompanion("ally"); if (SET.companion2) SpawnCompanion("ally2"); if (SET.companion3) SpawnCompanion("ally3");
        cam.yaw = HUMAN.P.facing;
        Mark("ready");
    }

    /// <summary>The briefing card's eyebrow, title and text for this mode and map.</summary>
    public (string eyebrow, string title, string text) Briefing()
    {
        if (WAVES.on)
        {
            var wm = Array.Find(WAVE_MAPS, m => m.key == CFG.Map);
            return ("Wave mode · " + (wm.name ?? CFG.Map), "Hold the base",
                (wm.blurb ?? "") + " Squads come in from the rim, bigger every wave: cocci fanning out ahead, bacillus troopers moving cover to cover, spirillum assassins circling to your blind side, armored amoeba brutes at the back. Clearing a wave restores your shield and restocks the racks and stashes. Best score here: " + WAVES.best + ".");
        }
        string eb = CFG.Map == "classic" ? "Sandbox · classic range" : CFG.Map == "leviathan" ? "Sandbox · inside the leviathan" : "Sandbox · world seed " + CFG.Seed;
        if (CFG.Map == "leviathan")
            return (eb, "Inside the leviathan", "You've been swallowed. Your field station sits in the throat of something vast and alive: organs rise from folded flesh, intestines coil across the floor, acid pools in the stomach pits, ribs arch overhead, and cells, bacteria and viruses drift through the red haze. Parasite camps have dug in among the organs, and colonies of giant cells make good cover.");
        return (eb, "Germ Ball", BRIEF_TEXT);
    }
    const string BRIEF_TEXT = "A world at microscopic scale, inside something enormous and alive. You are a germ built of rubbery segments that bend further than any real body could: shots knock blobs of membrane off you and off every microbe you fight, losing the head or chest bursts you, and wrecked hips cost you your legs. Germ squads patrol between their nests and the cell colonies: swarms of round cocci in front, bacillus troopers behind, amoeba brutes in pairs at the back. Cocci fan out and scurry back when it gets hot; troopers push from cover to cover; spirillum assassins corkscrew round behind you with darts and barbs; armored amoeba brutes wait until the squad thins out, and when their plates come off they go berserk. Some bacilli float in on hover chairs; reinforcements drop in by transport; giant tardigrades rake you with cilia gatlings and hurl calcified boulders. Everything is grown, not built, and it all comes apart. You carry three weapons: take more from the fallen, from racks and from stashes. Choppers, rovers and hover chairs are parked around. Every few minutes a boss turns up: a dark, red-eyed copy of you or one of your companions that fights the way you do. Up to three companions (Settings) fight beside you, talk you through it and learn how you play.";
}

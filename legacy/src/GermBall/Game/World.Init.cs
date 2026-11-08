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
        BuildLayout(); BuildNerves(); Mark("buildings");
        PlaceProps();
        BuildLeviathan(); BuildMembrane(); Mark("leviathan");
        InitSprites();
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
            return ("Wave mode · " + (wm.name ?? CFG.Map), "Hold the clinic",
                (wm.blurb ?? "") + ". Germ squads push in through the membrane at the edge, bigger every wave: cocci fanning out ahead, bacillus troopers moving cover to cover, spirillum assassins circling to your blind side, armored amoeba brutes at the back. Clearing a wave restores your coating and restocks the dispensers and supply caches. Best score here: " + WAVES.best + ".");
        }
        string eb = "Sandbox · " + MapName(CFG.Map) + (CFG.Map is "classic" or "leviathan" ? "" : " · seed " + CFG.Seed);
        if (CFG.Map == "leviathan")
            return (eb, "Belly of the Beast", "You've been swallowed. Your field clinic sits in the throat of something vast and alive: organs rise from folded flesh, intestines coil across the floor, acid fills the stomach pits, and villi sway in the current. Germ biofilms have dug in among the organs, and colonies of giant cells make good cover. Every germ you hit bleeds; find the puddles and you've found the fight.");
        return (eb, "Germ Ball", BRIEF_TEXT);
    }
    const string BRIEF_TEXT = "A host body at microscopic scale, and it's infected. You are medicine: a pill, a capsule, a tablet, a syringe, built of hard segments with armour on top. Shots knock plates and pieces off you and off every germ you fight; losing the head or chest finishes you, and wrecked hips cost you your legs. Germ squads patrol between their biofilm camps and the cell colonies: swarms of cocci, bacillus troopers who use cover, spirillum assassins, amoeba brutes and the occasional phage titan. Curl into a ball and roll, shoot, squash, punch and dash them out of the host. Your clinic restocks you; Tab, Cap and Jab fight beside you.";
}

using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed class CommsLine { public string who, text; public uint css; public float t; }

public sealed partial class World
{
    // =====================================================================
    // Companions: up to three, each with a role, a look and a voice.
    //   Volt  (marksman) hangs back in cover and picks off snipers and whatever you are shooting
    //   Brick (tank)     wades in, stands between you and the worst of it, smashes groups, throws heavy things
    //   Wisp  (scout)    flanks, hunts assassins, watches your back and fetches ammo
    // They share what they see: threats are scored, fire is focused, nobody shoots through you,
    // the hurt fall back curled up, and they call out danger and give you tips.
    // =====================================================================
    public static readonly Dictionary<string, (string name, string role, uint css, float[] slot)> COMP = new()
    {
        ["ally"] = ("VOLT", "marksman", 0x7affef, new[] { 2.2f, 3.5f }),
        ["ally2"] = ("BRICK", "tank", 0xffc93c, new[] { -2.4f, -1.5f }),
        ["ally3"] = ("WISP", "scout", 0xff7ae6, new[] { -1.5f, 4.5f }),
    };
    public readonly List<Actor> ALLIES = new();
    public void SpawnCompanion(string type = "ally")
    {
        if (ALLIES.Any(a => a.type == type)) return;
        var A = MakeActor(type, HUMAN.P.x + 2, HUMAN.P.z + 2, false, HUMAN.P.facing);
        A.brain = MakeBrain(A); A.brain.mode = "follow"; A.brainState = "FOLLOW"; RespawnActor(A);
        ALLIES.Add(A);
        AllySay(A, "hello", 0);
    }
    public void RemoveCompanion(string type = "ally")
    {
        var A = ALLIES.FirstOrDefault(a => a.type == type); if (A == null) return;
        if (A.P.grabbed != null) { var H = A.P.grabbed; if (H.P.holdA == A) H.P.holdA = null; }
        RemoveActor(A); ALLIES.Remove(A);
    }
    // ---------- the voice: what each of them says, by situation (spoken aloud, quietly, with optional subtitles)
    static readonly Dictionary<string, Dictionary<string, string[]>> LINES = new()
    {
        ["hello"] = new() { ["ally"] = new[] { "Volt online. I'll cover you from the back.", "Volt here. Point me at something." }, ["ally2"] = new[] { "Brick. I go first.", "Brick's here. Stay behind me." }, ["ally3"] = new[] { "Wisp! I'll keep an eye on your back.", "Hiya. I'm fast, you're loud. Good team." } },
        ["back"] = new() { ["ally"] = new[] { "Back up. Membrane resealed.", "Rebuilt. Where were we?" }, ["ally2"] = new[] { "Put back together. Angrier.", "Brick's back." }, ["ally3"] = new[] { "I'm back! Missed me?", "Reassembled. Let's go!" } },
        ["down"] = new() { ["ally"] = new[] { "Volt down.", "Lost my footing." }, ["ally2"] = new[] { "Ugh. I'm in pieces.", "Brick down!" }, ["ally3"] = new[] { "Ow ow ow.", "I'm scattered!" } },
        ["hurt"] = new() { ["ally"] = new[] { "Taking damage. Repositioning.", "I'm hit. Falling back to cover." }, ["ally2"] = new[] { "Hurting. Still standing.", "That one stung." }, ["ally3"] = new[] { "Yikes, I'm hurt! Rolling out!", "Too hot! Pulling back!" } },
        ["mateDown"] = new() { ["ally"] = new[] { "{who} is down. Covering their spot.", "We lost {who}. Stay sharp." }, ["ally2"] = new[] { "{who}'s down! I've got you.", "They got {who}." }, ["ally3"] = new[] { "No, {who}!", "{who} went to pieces!" } },
        ["contact"] = new() { ["ally"] = new[] { "Contact {dir}. {n} hostiles.", "Colony {dir}, {n} of them." }, ["ally2"] = new[] { "Here they come, {dir}.", "{n} of them {dir}. Good." }, ["ally3"] = new[] { "Company {dir}! {n} of them.", "Germs {dir}!" } },
        ["assassin"] = new() { ["ally"] = new[] { "Spirillum flanking {dir}.", "Assassin on your blind side." }, ["ally2"] = new[] { "Sneaky one behind you!", "Corkscrew. Behind you." }, ["ally3"] = new[] { "Assassin creeping up {dir}. I'm on it!", "Behind you! Assassin!" } },
        ["sniper"] = new() { ["ally"] = new[] { "Red line on you, {dir}. Break line of sight.", "Sniper {dir}. Move!" }, ["ally2"] = new[] { "Red line on you! Duck!", "Shooter's aiming at you." }, ["ally3"] = new[] { "Red laser on you! Dodge!", "Sniper {dir}! Zig zag!" } },
        ["berserk"] = new() { ["ally"] = new[] { "That amoeba's armor is off. It's berserk. Roll and let it pass.", "Berserk brute {dir}. Curl up, it can't break the shell." }, ["ally2"] = new[] { "Big blob's mad. Leave him to me.", "Berserker! Roll, I'll catch him." }, ["ally3"] = new[] { "Brute's lost it {dir}! Roll roll roll!", "Angry blob incoming!" } },
        ["mech"] = new() { ["ally"] = new[] { "Tardigrade {dir}. Its legs come off. Aim low.", "Water bear {dir}. Stay mobile, it throws boulders." }, ["ally2"] = new[] { "Big one {dir}. Finally, a fair fight.", "Tardigrade! Watch for flying rocks." }, ["ally3"] = new[] { "Giant water bear {dir}! Keep moving!", "Tardigrade! Run circles round it!" } },
        ["dropship"] = new() { ["ally"] = new[] { "Transport inbound {dir}. Shoot it before it unloads.", "Transport {dir}. Rockets bring it down." }, ["ally2"] = new[] { "Flying sack {dir}. More to smash.", "Transport. Let them come." }, ["ally3"] = new[] { "Transport {dir}! Pop it before it lands!", "Incoming!" } },
        ["boss"] = new() { ["ally"] = new[] { "That's a copy of you. It dashes and homes like you do. Don't stand still.", "Dark double {dir}. Same moves as you, and a lot tougher." }, ["ally2"] = new[] { "That thing wears your face. I don't like it.", "A dark copy {dir}. I'll hold it, you hit it." }, ["ally3"] = new[] { "It's you, but evil!? {dir}!", "Shadow copy {dir}! It can spin dash too!" } },
        ["bossMate"] = new() { ["ally"] = new[] { "It's wearing my shape. Strange.", "That's me in the dark. Shoot it anyway." }, ["ally2"] = new[] { "Someone stole my face.", "That's a bad Brick. Get him." }, ["ally3"] = new[] { "Hey! That's my look!", "Evil Wisp! Rude." } },
        ["bossDown"] = new() { ["ally"] = new[] { "The double is down. Good shooting.", "Boss eliminated." }, ["ally2"] = new[] { "Shadow's in pieces. Good.", "Boss down." }, ["ally3"] = new[] { "We beat it!", "Bye bye, evil twin!" } },
        ["grabbed"] = new() { ["ally"] = new[] { "You're grabbed. Mash jump to wriggle free!", "Mash jump! I'm shooting the hands." }, ["ally2"] = new[] { "Hold on! Mash jump!", "Let go of them!" }, ["ally3"] = new[] { "Wiggle! Mash jump!", "Shake loose! Tap jump!" } },
        ["lowHealth"] = new() { ["ally"] = new[] { "You're coming apart. Curl up and roll, bullets glance off.", "Your membrane's thin. Find a repair kit in a stash." }, ["ally2"] = new[] { "You're hurt. Get behind me.", "Roll up. Let me take it." }, ["ally3"] = new[] { "You're in bits! Roll, it makes you bulletproof!", "Find a repair kit, quick!" } },
        ["overheat"] = new() { ["ally"] = new[] { "You're overheating. Swap while it cools.", "Heat warning. Switch weapons." }, ["ally2"] = new[] { "Gun's cooked. Swap.", "Too hot. Swap." }, ["ally3"] = new[] { "Your gun's smoking! Switch!", "Overheat! Swap!" } },
        ["dry"] = new() { ["ally"] = new[] { "You're out of ammo. Take weapons off the fallen or hit a stash.", "Empty. The racks restock." }, ["ally2"] = new[] { "Out of ammo? Use your fists. Or throw something.", "No ammo. Throw things." }, ["ally3"] = new[] { "You're dry! Grab something and throw it!", "No ammo? Pick something up!" } },
        ["streak"] = new() { ["ally"] = new[] { "Efficient.", "Three in a row. Clean work." }, ["ally2"] = new[] { "Ha! Nice.", "Smashing." }, ["ally3"] = new[] { "You're on fire!", "So cool!" } },
        ["kill"] = new() { ["ally"] = new[] { "Target down.", "Got him." }, ["ally2"] = new[] { "Next.", "Crunch." }, ["ally3"] = new[] { "Gotcha!", "Too slow!" } },
        ["waveClear"] = new() { ["ally"] = new[] { "Wave cleared. Restock at the racks.", "Clear. Shields are back up." }, ["ally2"] = new[] { "That all? Bring more.", "Clear." }, ["ally3"] = new[] { "Wave down! Grab ammo while it's quiet!", "We did it! Restock!" } },
        ["bossWave"] = new() { ["ally"] = new[] { "Boss wave. They copy your moves. Keep moving, use cover.", "Boss signature on the rim." }, ["ally2"] = new[] { "Boss wave. Good.", "Big one coming." }, ["ally3"] = new[] { "Boss wave! It's a dark you!", "Uh oh, boss wave!" } },
        ["pancake"] = new() { ["ally"] = new[] { "Flattened. Very scientific.", "That one's two dimensional now." }, ["ally2"] = new[] { "Ha! Pancake!", "Squish. Do it again." }, ["ally3"] = new[] { "Flat as a pancake! Again again!", "Boing! That was amazing!" } },
        ["longKill"] = new() { ["ally"] = new[] { "That was {d} metres. I'm impressed.", "Long shot. Nice." }, ["ally2"] = new[] { "From way over there? Huh.", "Long way to throw a bullet." }, ["ally3"] = new[] { "How did you even hit that!?", "Sniper germ!" } },
        ["airKill"] = new() { ["ally"] = new[] { "Mid-air. Show off.", "Shot from the air. Clean." }, ["ally2"] = new[] { "Dropping in on them. Good.", "From above!" }, ["ally3"] = new[] { "Air kill! So cool!", "You fly and fight!" } },
        ["vehKill"] = new() { ["ally"] = new[] { "Run them over, why not.", "Vehicular. Effective." }, ["ally2"] = new[] { "Roadkill.", "Ha! Flattened by wheels." }, ["ally3"] = new[] { "Beep beep! Splat!", "Drive-by!" } },
        ["throwKill"] = new() { ["ally"] = new[] { "Killed with a thrown {what}. Resourceful.", "Thrown {what}. Effective." }, ["ally2"] = new[] { "Threw a {what} at it. Respect.", "That {what} did the job." }, ["ally3"] = new[] { "You bonked it with a {what}!", "Yeet! Right on target!" } },
        ["meleeKill"] = new() { ["ally"] = new[] { "Up close, as usual.", "Personal." }, ["ally2"] = new[] { "Now that's how you do it.", "Crunch. Lovely." }, ["ally3"] = new[] { "Smack!", "Punchy!" } },
        ["died"] = new() { ["ally"] = new[] { "That's {n} times you've burst. Try rolling when it gets loud.", "Rebuilt. You keep charging the {cause}. Let me thin them first." }, ["ally2"] = new[] { "Back again. Stay behind me this time.", "You went in alone. Wait for Brick." }, ["ally3"] = new[] { "You popped! It happens. Try dashing out next time!", "Welcome back! Don't let them corner you." } },
        ["sRoller"] = new() { ["ally"] = new[] { "You roll everywhere. Fine by me, it's harder to hit you.", "Do you ever walk?" }, ["ally2"] = new[] { "Always rolling. Like a boulder. I like it.", "Rolling again. Good habit." }, ["ally3"] = new[] { "You roll more than I do!", "Roll roll roll. You're my kind of germ." } },
        ["sFlyer"] = new() { ["ally"] = new[] { "You spend half the fight in the air. They can't track you up there.", "Airborne again. Good angles." }, ["ally2"] = new[] { "You jump a lot. I'll hold the ground.", "Get down here and fight! Kidding." }, ["ally3"] = new[] { "You're always up in the air! Teach me!", "Jump jump dash! You're so bouncy." } },
        ["sBrawler"] = new() { ["ally"] = new[] { "You like getting close. I'll pick off whatever turns to face you.", "Melee again. I'll cover your approach." }, ["ally2"] = new[] { "You fight like me. Up close.", "You and me, in their faces." }, ["ally3"] = new[] { "You love punching things, huh?", "Up close and personal, every time!" } },
        ["sMarksman"] = new() { ["ally"] = new[] { "You aim like a marksman. I'll spot targets for you.", "Steady sights. We think alike." }, ["ally2"] = new[] { "You hang back and shoot. I'll be the wall.", "Keep shooting, I'll keep them busy." }, ["ally3"] = new[] { "You're always aiming down sights! Precise!", "Careful shooter, huh?" } },
        ["sSpray"] = new() { ["ally"] = new[] { "You fire a lot from the hip. Try the sights at range.", "Lots of lead in the air. Some of it hits." }, ["ally2"] = new[] { "Spray and pray. I respect it.", "Hold the trigger, sure." }, ["ally3"] = new[] { "Pew pew pew pew!", "You never let go of that trigger!" } },
        ["sDriver"] = new() { ["ally"] = new[] { "You like your vehicles. I'll keep up.", "Driving again. I'll watch the flanks." }, ["ally2"] = new[] { "Always riding. Lazy. I like it.", "Get out and walk sometime." }, ["ally3"] = new[] { "Vroom! Can I ride too?", "You really love driving!" } },
        ["sThrower"] = new() { ["ally"] = new[] { "You throw everything that isn't nailed down.", "Another throw. Physics is your weapon." }, ["ally2"] = new[] { "You throw things. Like me!", "Throw more. It's fun." }, ["ally3"] = new[] { "You throw so much stuff!", "Yeet master!" } },
        ["sReckless"] = new() { ["ally"] = new[] { "You've burst {n} times. Let them come to you.", "Maybe take cover once in a while." }, ["ally2"] = new[] { "You keep dying. Stay behind me.", "Too brave. Slow down." }, ["ally3"] = new[] { "You keep popping! Be careful!", "So many deaths! Roll more!" } },
        ["sFavorite"] = new() { ["ally"] = new[] { "You really like the {wpn}.", "The {wpn} again. It suits you." }, ["ally2"] = new[] { "Always the {wpn}. Good choice.", "You and that {wpn}." }, ["ally3"] = new[] { "The {wpn}! Your favourite!", "You never put down the {wpn}!" } },
        ["sPounder"] = new() { ["ally"] = new[] { "Ground pound again. Effective against groups.", "You like dropping on them from above." }, ["ally2"] = new[] { "Pound! Ha!", "Slam them!" }, ["ally3"] = new[] { "Bonk from above!", "Pound pound pound!" } },    };
    // lines that matter right now cut in; everything else waits its turn
    static readonly HashSet<string> URGENT = new() { "grabbed", "sniper", "assassin", "lowHealth", "down", "boss", "berserk", "bossMate" };
    // quiet-time tips, picked to fit what's around you and skipped once you clearly know the trick
    (Func<bool> ok, string text)[] TIPS;
    void InitTips()
    {
        bool NearVeh(string k) => vehicles.Any(v => v.kind == k && v.rider == null && !v.dead && Hypot(v.x - HUMAN.P.x, v.z - HUMAN.P.z) < 60);
        TIPS = new (Func<bool>, string)[]
        {
            (() => STYLE.spins < 2, "From a standstill, hold roll to charge a spin dash. It bowls cocci over."),
            (() => STYLE.dashes < 3, "Jump, then jump again in the air to dash. With homing charges the dash locks on."),
            (() => STYLE.throws < 3, "Pick up a spore pod or a small germ and hold the button to wind up a throw."),
            (() => STYLE.pounds < 3, "Ground pound in the air lands hard on groups."),
            (() => STYLE.roll < 20, "Curled up in your ball, bullets glance off you."),
            (() => true, "Shooting the plates off an amoeba makes it berserk. Kill it before that, or roll when it charges."),
            (() => true, "Colonies come apart. Shoot the walls under a sniper."),
            (() => HUMAN.W.slots.Any(w => w != null && WPN[w.id].Energy), "Energy weapons never run dry, but they overheat. Swap and let one cool."),
            (() => STYLE.pancakes == 0 && HUMAN.W.slots.Any(w => w != null && w.id == "hammer"), "That squash hammer flattens germs into pancakes. Try it."),
            (() => true, "Stashes and wall racks restock over time."),
            (() => NearVeh("chair"), "There's a hover chair nearby. R B lifts it, L B sinks it. Great over walls."),
            (() => NearVeh("rover"), "Rover nearby. Roll boosts it, and the turret aims where you look."),
            (() => NearVeh("bike"), "A chopper's parked close by. Jump twice on it to jump dash. Land on the wheels!"),
        };
    }
    public sealed class CommsState { public List<CommsLine> lines = new(); public Dictionary<string, float> cd = new(); public float t, quietT, tipT = 40; public int tipI = (int)(R() * 12); public HashSet<object> seen = new(); public List<float> kills = new(); public float lastSay = -10, obsT = 100; public Dictionary<string, float> styleSaid = new(); }
    public readonly CommsState COMMS = new();
    // ---------- how you play: a running tally the companions learn from
    public sealed class StyleState
    {
        public float t, roll, air, ads, fireT, meleeT, hipT, veh; public int kills, kmelee, deaths, pancakes, throws, spins, pounds, dashes; public List<float> recentDeaths = new();
        public Dictionary<string, float> wpn = new(); public string lastCause = "", throwWhat; public float throwT = -9, diedAt, acc; public bool pSpin, pPound; public float pDash;
    }
    public readonly StyleState STYLE = new();
    void StyleSample(float dt)
    {
        var G = ME.P; var I = ME.IN; if (!G.alive) return;
        STYLE.t += dt;
        if (G.curl > 0.6f || G.rolling) STYLE.roll += dt;
        if (!G.ground && G.vehicle == null) STYLE.air += dt;
        if (G.vehicle != null) STYLE.veh += dt;
        if (I.ads) STYLE.ads += dt;
        if (I.fire) { var w = CurWpn(ME); var d = CurDef(ME); STYLE.fireT += dt; if (d.Type == "melee") STYLE.meleeT += dt; else if (!I.ads) STYLE.hipT += dt; if (w != null) STYLE.wpn[w.id] = STYLE.wpn.GetValueOrDefault(w.id) + dt; }
        // edges: a spin dash, an air dash, a ground pound
        if (G.charging && !STYLE.pSpin) STYLE.spins++; STYLE.pSpin = G.charging;
        if (G.dashT > 0 && !(STYLE.pDash > 0)) STYLE.dashes++; STYLE.pDash = G.dashT;
        if (G.pound != 0 && !STYLE.pPound) STYLE.pounds++; STYLE.pPound = G.pound != 0;
    }
    static readonly Dictionary<string, string> PROP_NAME = new() { ["crate"] = "spore pod", ["ball"] = "volvox", ["rock"] = "nodule", ["spear"] = "stinger", ["bomb"] = "toxin vesicle", ["block"] = "chitin block", ["steelball"] = "pearl", ["girder"] = "bone", ["boulder"] = "boulder", ["container"] = "frustule" };
    // something you did that the team might react to
    void StyleEvent(string kind, Actor B)
    {
        var live = ALLIES.Where(a => a.P.alive && !a.dormant).ToList();
        if (kind == "throw") { STYLE.throws++; STYLE.throwT = simT; var h = ME.P.hold; STYLE.throwWhat = ME.P.holdA != null ? "germ" : h?.Prop?.kind != null && PROP_NAME.TryGetValue(h.Prop.kind, out var nm) ? nm : "thing"; return; }
        if (kind == "pancake") { STYLE.pancakes++; if (live.Count > 0 && (STYLE.pancakes <= 2 || R() < 0.3f)) AllySay(PickAlly(live), "pancake", 30); return; }
        if (kind == "death")
        {
            STYLE.deaths++; STYLE.recentDeaths.Add(simT); while (STYLE.recentDeaths.Count > 0 && simT - STYLE.recentDeaths[0] > 300) STYLE.recentDeaths.RemoveAt(0);
            var by = B?.lastHurtBy; STYLE.lastCause = by != null && TYPES.ContainsKey(by.type) ? System.Text.RegularExpressions.Regex.Replace(TYPES[by.type].Label, @"^\w+ ", "") + "s" : "enemies"; STYLE.diedAt = simT;
        }
    }
    string DirWord(float x, float z)
    {
        var H = HUMAN.P; float a = WrapAngle(MathF.Atan2(-(x - H.x), -(z - H.z)) - cam.yaw);
        return MathF.Abs(a) < 0.6f ? "ahead" : MathF.Abs(a) > 2.4f ? "behind you" : a > 0 ? "on your left" : "on your right";
    }
    static Actor PickAlly(List<Actor> live) => live[(int)(R() * live.Count)];
    // one line from companion A; key picks the situation, cool (s) stops that line repeating
    bool AllySay(Actor A, string key, float cool = 6, Dictionary<string, object> vars = null)
    {
        if (A == null || (!A.P.alive && key != "down") || !SET.chatter) return false;
        float now = simT; string ck = key + (vars != null && vars.TryGetValue("tag", out var tg) ? tg?.ToString() : ""); bool urgent = URGENT.Contains(key);
        // they don't talk over each other, and ordinary chatter waits for a good gap
        if (COMMS.cd.GetValueOrDefault(ck, -1e9f) > now || now - COMMS.lastSay < (urgent ? 3.5f : key == "hello" ? 0.5f : 12)) return false;
        if (!LINES.TryGetValue(key, out var byType) || !byType.TryGetValue(A.type, out var set)) return false;
        string text = set[(int)(R() * set.Length)];
        if (vars != null) foreach (var (k, v) in vars) text = text.Replace("{" + k + "}", v?.ToString());
        COMMS.cd[ck] = now + cool * 2; COMMS.lastSay = now;
        PushComms(A, text, urgent);
        return true;
    }
    static Dictionary<string, object> V(params (string, object)[] kv) => kv.ToDictionary(p => p.Item1, p => p.Item2);
    void AllyTip(Actor A, string text) { if (A == null || !SET.chatter || simT - COMMS.lastSay < 12) return; COMMS.lastSay = simT; PushComms(A, text, false); }
    void PushComms(Actor A, string text, bool urgent)
    {
        var C = COMP[A.type];
        if (SET.subs || SET.voice <= 0 || !Platform.Speech.Available)
        {
            COMMS.lines.Add(new CommsLine { who = C.name, css = C.css, text = text, t = clockT });
            if (COMMS.lines.Count > 3) COMMS.lines.RemoveAt(0);
        }
        Platform.Speech.Speak(A.type, text, urgent, SET.voice * 0.75f * (0.4f + 0.6f * SET.volume));
    }
    // ---------- what the team notices, a few times a second; the best-placed companion speaks
    void CommsTick(float dt)
    {
        if (ME?.P != null) { STYLE.acc += dt; if (STYLE.acc >= 0.1f) { StyleSample(STYLE.acc); STYLE.acc = 0; } }
        COMMS.t -= dt; if (COMMS.t > 0) return; COMMS.t = 0.35f;
        var live = ALLIES.Where(a => a.P.alive && !a.dormant).ToList(); if (live.Count == 0) return;
        var H = ME.P;
        Actor By(string role) => live.FirstOrDefault(a => COMP[a.type].role == role) ?? live[(int)(R() * live.Count)];
        bool combat = false;
        foreach (var E in actors.ToArray())
        {
            if (E.team != "enemy" || !E.P.alive || E.dormant) continue;
            float d = Hypot(E.P.x - H.x, E.P.z - H.z); if (d > 70) continue;
            var B = E.brain;
            if (B != null && (B.seeA != null || (B.tgtA != null && d < 60) || (E.squad != null && E.squad.state == "combat" && d < 60))) combat = true;
            if (E.squad != null && !E.squad.boss && E.squad.members.Count > 2 && E.squad.state == "combat" && !COMMS.seen.Contains(E.squad) && d < 55) { if (AllySay(By("marksman"), "contact", 20, V(("dir", DirWord(E.P.x, E.P.z)), ("n", E.squad.members.Count)))) COMMS.seen.Add(E.squad); }
            if (TYPES[E.type].Boss && !COMMS.seen.Contains(E)) { var mate = live.FirstOrDefault(a => MIMIC_OF.GetValueOrDefault(E.type) == a.type); if (AllySay(mate ?? By("marksman"), mate != null ? "bossMate" : "boss", 4, V(("dir", DirWord(E.P.x, E.P.z)), ("tag", E.uid)))) COMMS.seen.Add(E); }
            if (E.type == "mech" && !COMMS.seen.Contains(E) && d < 65) { if (AllySay(By("tank"), "mech", 6, V(("dir", DirWord(E.P.x, E.P.z)), ("tag", E.uid)))) COMMS.seen.Add(E); }
            if (E.type == "assassin" && d < 16 && MathF.Abs(WrapAngle(MathF.Atan2(-(E.P.x - H.x), -(E.P.z - H.z)) - cam.yaw)) > 1.9f) AllySay(By("scout"), "assassin", 20, V(("dir", DirWord(E.P.x, E.P.z))));
            if (B != null && B.sightT > 0.05f && B.tgtA == ME) AllySay(By("marksman"), "sniper", 20, V(("dir", DirWord(E.P.x, E.P.z))));
            if (E.P.berserk && d < 40) AllySay(By("tank"), "berserk", 25, V(("dir", DirWord(E.P.x, E.P.z))));
        }
        foreach (var Vh in vehicles) if (Vh.kind == "dropship" && !Vh.dead && Vh.state == "approach" && !COMMS.seen.Contains(Vh) && Hypot(Vh.x - H.x, Vh.z - H.z) < 140) { if (AllySay(By("marksman"), "dropship", 40, V(("dir", DirWord(Vh.lx, Vh.lz))))) COMMS.seen.Add(Vh); }
        if (H.grabbed != null) AllySay(By("tank"), "grabbed", 15);
        var w = CurWpn(ME); float hp = HealthOf(ME);
        if (hp < 0.4f && H.shield < 15 && H.alive) AllySay(By("tank"), "lowHealth", 45);
        if (w != null && w.over) AllySay(By("marksman"), "overheat", 60);
        if (w != null && !HasAmmo(ME) && WPN[w.id].Type != "melee" && !WPN[w.id].Energy) AllySay(By("scout"), "dry", 60);
        // just rebuilt after bursting: a word about it, once things settle
        if (STYLE.diedAt != 0 && H.alive && simT - STYLE.diedAt > 4 && simT - STYLE.diedAt < 20 && !combat) { if (AllySay(By("marksman"), "died", 60, V(("n", STYLE.deaths), ("cause", STYLE.lastCause)))) STYLE.diedAt = 0; }
        else if (STYLE.diedAt != 0 && simT - STYLE.diedAt >= 20) STYLE.diedAt = 0;
        COMMS.quietT = combat ? 0 : COMMS.quietT + 0.35f;
        // observations about how you play, now and then once they've watched you for a while
        if ((COMMS.obsT -= 0.35f) <= 0 && STYLE.t > 60 && COMMS.quietT > 3) { COMMS.obsT = 110 + R() * 80; Observe(live); }
        // the quiet: an occasional tip
        if (COMMS.quietT > 14 && (COMMS.tipT -= 0.35f) <= 0)
        {
            COMMS.tipT = 70 + R() * 50;
            for (int k = 0; k < TIPS.Length; k++) { var (ok, text) = TIPS[(COMMS.tipI++) % TIPS.Length]; if (ok()) { AllyTip(PickAlly(live), text); break; } }
        }
    }
    // the strongest habit they've noticed that they haven't already mentioned lately
    void Observe(List<Actor> live)
    {
        float T = MathF.Max(1, STYLE.t), F = MathF.Max(1, STYLE.fireT); var traits = new List<(string, float)>();
        string fav = null; float fv = 0; foreach (var (k, v) in STYLE.wpn) if (v > fv) { fv = v; fav = k; }
        traits.Add(("sRoller", STYLE.roll / T / 0.2f)); traits.Add(("sFlyer", STYLE.air / T / 0.3f)); traits.Add(("sBrawler", STYLE.meleeT / F / 0.45f)); traits.Add(("sMarksman", STYLE.ads / T / 0.25f));
        traits.Add(("sSpray", STYLE.hipT / F / 0.7f * (STYLE.ads / T < 0.05f ? 1 : 0))); traits.Add(("sDriver", STYLE.veh / T / 0.3f)); traits.Add(("sThrower", STYLE.throws / 6f)); traits.Add(("sPounder", STYLE.pounds / 6f)); traits.Add(("sReckless", STYLE.recentDeaths.Count / 3f));
        if (fav != null && fv > 25) traits.Add(("sFavorite", fv / F / 0.5f));
        traits.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        foreach (var (k, sc) in traits)
        {
            if (sc < 1) break;
            if (simT - COMMS.styleSaid.GetValueOrDefault(k, -1e9f) < 420) continue;
            string role = k == "sBrawler" || k == "sReckless" ? "tank" : k == "sMarksman" || k == "sSpray" || k == "sFavorite" ? "marksman" : "scout";
            var A = live.FirstOrDefault(a => COMP[a.type].role == role) ?? PickAlly(live);
            if (AllySay(A, k, 60, V(("wpn", fav != null ? WPN[fav].Name.ToLowerInvariant() : ""), ("n", STYLE.recentDeaths.Count)))) COMMS.styleSaid[k] = simT;
            return;
        }
    }
    // kills feed banter, praise and reactions to how you did it
    void CommsKill(Actor B)
    {
        var live = ALLIES.Where(a => a.P.alive).ToList(); if (live.Count == 0) return;
        var by = B.lastHurtBy;
        if (TYPES[B.type].Boss) { AllySay(live[0], "bossDown", 3, V(("tag", B.uid))); return; }
        if (by != null && ALLIES.Contains(by) && R() < 0.12f) AllySay(by, "kill", 20);
        if (by == ME || by == HUMAN)
        {
            var G = ME.P; float d = Hypot(B.P.x - G.x, B.P.z - G.z); string kind = B.lastHurtKind;
            STYLE.kills++; if (kind == "melee") STYLE.kmelee++;
            COMMS.kills.Add(simT); while (COMMS.kills.Count > 0 && simT - COMMS.kills[0] > 4) COMMS.kills.RemoveAt(0);
            if (COMMS.kills.Count >= 3) { COMMS.kills.Clear(); AllySay(PickAlly(live), "streak", 40); return; }
            var A = PickAlly(live);
            if (d > 55 && kind == "bullet" && R() < 0.5f) AllySay(A, "longKill", 60, V(("d", (int)MathF.Round(d))));
            else if (G.vehicle != null && kind == "impact" && R() < 0.5f) AllySay(A, "vehKill", 60);
            else if (kind == "impact" && G.vehicle == null && G.hold == null && simT - STYLE.throwT < 4 && R() < 0.6f) AllySay(A, "throwKill", 60, V(("what", STYLE.throwWhat ?? "thing")));
            else if (!G.ground && G.vehicle == null && R() < 0.3f) AllySay(A, "airKill", 60);
            else if (kind == "melee" && STYLE.kmelee > 3 && R() < 0.15f) AllySay(A, "meleeKill", 90);
        }
    }
    void StepCommsLines() { while (COMMS.lines.Count > 0 && clockT - COMMS.lines[0].t > 6.5f) COMMS.lines.RemoveAt(0); }

    // ---------- the brain
    static readonly Dictionary<string, float> ALLY_W = new() { ["small"] = 1, ["normal"] = 2, ["assassin"] = 2.6f, ["brute"] = 3, ["mech"] = 4.5f };
    float ThreatOf(Actor A, Actor E, string role)
    {
        var G = A.P; var t = E.P; var H = ME.P; float d = Hypot(t.x - G.x, t.z - G.z), dh = Hypot(t.x - H.x, t.z - H.z); var B = E.brain;
        float s = (ALLY_W.TryGetValue(E.type, out var aw) ? aw : TYPES[E.type].Boss ? 5 : 2) - d / 12;
        if (B != null && B.tgtA == ME) s += 1.5f;
        if (B != null && B.sightT > 0) s += role == "marksman" ? 6 : 3;           // a marksman lining up
        if (t.holdA == ME || t.holdA == HUMAN) s += 12;                           // someone has you
        if (t.berserk) s += role == "tank" ? 5 : 2;
        if (dh < 8) s += 2.5f * (1 - dh / 8);                                    // close to you
        if (HUMAN.lastTarget == E && simT - HUMAN.lastTargetT < 3) s += 2.5f;      // focus fire on what you're shooting
        if (HealthOf(E) < 0.35f) s += 1.2f;                                       // finish it
        if (role == "tank") s += (E.type == "brute" || E.type == "mech" ? 2.5f : 0) - d / 20;
        if (role == "scout") { if (E.type == "assassin") s += 4; bool behind = MathF.Abs(WrapAngle(MathF.Atan2(-(t.x - H.x), -(t.z - H.z)) - H.facing)) > 1.9f; if (behind && dh < 20) s += 2; }
        if (role == "marksman" && d > 14) s += 1;
        foreach (var O in ALLIES) if (O != A && O.brain != null && O.brain.tgtA == E && HUMAN.lastTarget != E) s -= 1.2f;   // spread out
        return s;
    }
    // a shot from A at E would pass too close to one of us
    bool FriendInLine(Actor A, Actor E)
    {
        var G = A.P; var t = E.P; float ux = t.x - G.x, uz = t.z - G.z, L = Hypot(ux, uz); if (L == 0) L = 1;
        foreach (var F in actors)
        {
            if (F == A || F.team != "hero" || !F.P.alive || F.hidden) continue;
            float fx = F.P.x - G.x, fz = F.P.z - G.z, along = (fx * ux + fz * uz) / L;
            if (along < 0.5f || along > L - 0.5f) continue;
            if (MathF.Abs(fx * uz - fz * ux) / L < F.P.R + 0.7f) return true;
        }
        return false;
    }
    readonly List<Body> QAL = new();
    void AllyBrain(Actor A, float dt)
    {
        var G = A.P; var B = A.brain; var I = A.IN; var H = ME.P; string role = COMP.TryGetValue(A.type, out var cp) ? cp.role : "marksman";
        I.mx = I.mz = 0; I.yaw = 0; I.fire = false; I.ads = false; I.throwAt = null;
        if (B.seq == null) { I.jump = false; I.roll = false; I.grab = false; I.pound = false; }
        if (!G.alive)
        {
            if (!A.wasDown)
            {
                A.wasDown = true; AllySay(A, "down", 2); var o = ALLIES.FirstOrDefault(x => x != A && x.P.alive);
                if (o != null) { string nm = COMP[A.type].name; Later(0.9f, () => AllySay(o, "mateDown", 6, V(("who", nm[0] + nm[1..].ToLowerInvariant())))); }
            }
            return;
        }
        if (A.wasDown) { A.wasDown = false; AllySay(A, "back", 4); }
        if (G.grabbed != null) { A.brainState = "GRABBED"; B.mash -= dt; if (B.mash <= 0) { B.mash = 0.11f; I.jump = !I.jump; } return; }
        if (G.stunT > 0 || G.ragdoll != null) return;
        if (G.vehicle != null) { I.use = !I.use; return; }   // companions don't drive: hop off
        float dH = Hypot(H.x - G.x, H.z - G.z);
        // no teleporting: a companion that has fallen behind retraces your trail at a run (curling up to roll on the flat)
        B.regroupT = H.alive && (dH > 18 || (MathF.Abs(H.y - G.y) > 2.5f && dH < 40)) ? B.regroupT + dt : 0;
        B.dashCD -= dt; B.think -= dt; B.throwCD -= dt; B.poundCD -= dt; B.coverT -= dt;
        float hp = HealthOf(A);
        if (hp < 0.35f && !A.saidHurt) { A.saidHurt = true; AllySay(A, "hurt", 20); } else if (hp > 0.6f) A.saidHurt = false;
        // ---- choose a target
        if (B.think <= 0)
        {
            B.think = 0.25f + R() * 0.1f; var prev = B.tgtA; B.tgtA = null; float bs = -1e9f;
            foreach (var E0 in actors)
            {
                if (E0.team != "enemy" || !E0.P.alive || E0.dormant) continue;
                float d = Hypot(E0.P.x - G.x, E0.P.z - G.z), dh = Hypot(E0.P.x - H.x, E0.P.z - H.z);
                if (d > (role == "marksman" ? 55 : 38) || dh > 45) continue;
                float sc = ThreatOf(A, E0, role);
                if (sc > bs && ((role == "tank" && d < 12) || LosClear(G.x, G.y + G.H * 0.85f, G.z, E0.P.x, E0.P.y + E0.P.H * 0.6f, E0.P.z, 1.5f))) { bs = sc; B.tgtA = E0; }
            }
        }
        // ---- danger: a lit bomb or something heavy flying at us
        float fleeX = 0, fleeZ = 0;
        QueryBodies(G.x - 8, G.x + 8, G.z - 8, G.z + 8, QAL);
        foreach (var b in QAL)
        {
            if (b.Actor != null) continue;
            float dx = G.x - b.Pos.x, dz = G.z - b.Pos.z, d = Hypot(dx, dz); if (d == 0) d = 1;
            if (b.Bomb && b.Fuse > 0 && d < 6.5f) { fleeX += dx / d * 2; fleeZ += dz / d * 2; }
            else if (b.ThrownT > 0 && b.ThrownBy != null && b.ThrownBy.team == "enemy" && d < 7 && (b.Vel.x * dx + b.Vel.z * dz) > 0 && B.dashCD <= 0 && G.ground && B.seq == null) { StartSeq(B, "dash"); B.dashCD = 2; A.dodge = (-dz / d, dx / d); }
        }
        var E = B.tgtA != null && B.tgtA.P.alive ? B.tgtA : null;
        float gx = G.x, gz = G.z, speed = 0;
        // ---- pick-ups: ammo and guns; Wisp goes out of its way for them
        float reach = role == "scout" ? 18 : 9;
        var it = items.FirstOrDefault(i => i.stash == null && Hypot(i.x - G.x, i.z - G.z) < reach && (i.kind == "ammo" || (i.kind == "weapon" && (Array.IndexOf(A.W.slots, null) >= 0 || !HasAmmo(A)))));
        if (it != null && it.kind == "weapon" && Hypot(it.x - G.x, it.z - G.z) < 1.8f)
        {
            if (!HasAmmo(A)) { int k = Array.FindIndex(A.W.slots, w => w != null && WPN[w.id].Type != "melee"); if (k >= 0) A.W.slots[k] = null; }
            if (GiveItem(A, it, true)) RemoveItem(it);
        }
        if (fleeX != 0 || fleeZ != 0) { A.brainState = "DODGE"; gx = G.x + fleeX * 4; gz = G.z + fleeZ * 4; speed = 1; }
        else if (E != null && hp < 0.3f && role != "tank")
        {
            // badly hurt: fall back to the far side of you, curled up so shots glance off
            A.brainState = "FALLBACK";
            var t = E.P; float ex = H.x - t.x, ez = H.z - t.z, el = Hypot(ex, ez); if (el == 0) el = 1;
            gx = H.x + ex / el * 5; gz = H.z + ez / el * 5; speed = 1;
            if (Hypot(gx - G.x, gz - G.z) > 3) I.roll = true;
            AimAt(A, E, 0.5f, dt); I.fire = !FriendInLine(A, E) && AiTrigger(A, E, Hypot(t.x - G.x, t.z - G.z), true, dt);
        }
        else if (E != null)
        {
            var t = E.P; float d = Hypot(t.x - G.x, t.z - G.z), dd = d == 0 ? 1 : d, ux = (t.x - G.x) / dd, uz = (t.z - G.z) / dd;
            B.strafeT -= dt; if (B.strafeT <= 0) { B.strafeT = 1 + R() * 1.6f; B.strafe = R() < 0.5f ? -1 : 1; }
            if (role == "marksman")
            {
                A.brainState = "SNIPING";
                // hold back at range, in cover when there's some; peek out to shoot and duck back in
                if ((B.cover == null || B.coverT <= 0) && coverPts.Count > 0) { B.cover = PickCover(A, E, 14, 30, 22, 0); B.coverT = B.cover != null ? 5 + R() * 3 : 2; B.coverIn = 0; }
                if (B.cover != null && Hypot(B.cover.x - H.x, B.cover.z - H.z) < 32)
                {
                    var c = B.cover; float dc = Hypot(c.x - G.x, c.z - G.z); if (dc < 1.4f) B.coverIn += dt;
                    bool outp = B.coverIn > 0.3f && (B.coverIn % 2.6f) < 1.6f;
                    gx = c.x + (outp ? -c.nz * B.peek * 1.3f : c.nx * 0.3f); gz = c.z + (outp ? c.nx * B.peek * 1.3f : c.nz * 0.3f); speed = dc > 2 ? 1 : 0.6f;
                    A.brainState = outp ? "SNIPING" : "IN COVER";
                }
                else
                {
                    float mx = -uz * B.strafe * 0.5f, mz = ux * B.strafe * 0.5f;
                    if (d > 26) { mx += ux; mz += uz; } else if (d < 16) { mx -= ux; mz -= uz; }
                    gx = G.x + mx * 3; gz = G.z + mz * 3; speed = 0.85f;
                }
                if (dH > 24) { gx = Lerp(gx, H.x, 0.5f); gz = Lerp(gz, H.z, 0.5f); }
            }
            else if (role == "tank")
            {
                A.brainState = "HOLDING";
                // stand between you and the target when it's after you, otherwise close in
                bool threatened = Hypot(t.x - H.x, t.z - H.z) < 14;
                if (threatened && !t.berserk) { float ex = H.x - t.x, ez = H.z - t.z, el = Hypot(ex, ez); if (el == 0) el = 1; gx = t.x + ex / el * MathF.Min(4, el * 0.5f); gz = t.z + ez / el * MathF.Min(4, el * 0.5f); A.brainState = "GUARDING"; }
                else { gx = t.x - ux * 2.5f; gz = t.z - uz * 2.5f; A.brainState = "CHARGING"; }
                speed = 1;
                // a group bunched up close: jump in and ground pound; at a distance: spin dash through them
                int near = actors.Count(O => O.team == "enemy" && O.P.alive && Hypot(O.P.x - G.x, O.P.z - G.z) < 5);
                if (near >= 3 && B.poundCD <= 0 && G.ground && B.seq == null) { StartSeq(B, "pound"); B.poundCD = 5; A.brainState = "SMASH"; }
                else if (d > 6 && d < 16 && B.dashCD <= 0 && G.ground && B.seq == null && E.type != "mech") { StartSeq(B, "roll"); B.rollT = 0.7f; B.dashCD = 6; }
                // something heavy to hand and the target a way off: throw it
                if (G.hold == null && B.pick == null && B.throwCD <= 0 && d > 7 && d < 22) { B.pick = NearProp(A, 6, 10, 900); B.pickT = 3; if (B.pick == null) B.throwCD = 3; }
            }
            else
            {
                A.brainState = "FLANKING";
                // come at it from the side it isn't facing, dash in close, then out again
                float side = B.side, ang = t.facing + (PI * 0.6f) * side, r = E.type == "assassin" ? 4 : 9;
                gx = t.x + MathF.Sin(ang) * r; gz = t.z + MathF.Cos(ang) * r; speed = 1;
                if (R() < dt * 0.15f) B.side = -B.side;
                if (B.dashCD <= 0 && G.ground && d > 5 && d < 14 && B.seq == null) { StartSeq(B, "dash"); B.dashCD = 3 + R() * 2; }
                if (B.seq == "dash" && A.dodge == null) { gx = t.x; gz = t.z; }
            }
            // carrying something: throw it at the target
            if (G.hold != null) { B.holdT += dt; if (B.holdT > 0.5f && B.seq == null) { I.throwAt = new V3(t.x + t.vx * 0.4f, t.y + t.H * 0.4f, t.z + t.vz * 0.4f); StartSeq(B, "grab"); B.holdT = 0; B.throwCD = 5; } }
            else if (B.pick != null)
            {
                B.pickT -= dt;
                if (B.pickT <= 0 || B.pick.Held != null || B.pick.Removed) { B.pick = null; B.throwCD = 3; }
                else
                {
                    gx = B.pick.Pos.x; gz = B.pick.Pos.z; speed = 1;
                    if (Hypot(gx - G.x, gz - G.z) < 1.5f * G.s + B.pick.Bound * 0.6f && B.seq == null) { G.facing = MathF.Atan2(-(gx - G.x), -(gz - G.z)); I.aimYaw = G.facing; if (WithActor(A, () => TryPickUp(A))) B.pick = null; else B.pickT -= 0.5f; }
                }
            }
            // weapons: the right gun for the range, aim, and never through a friend
            AiPickWeapon(A, d, dt, role == "tank" && d < 3.2f);
            float err = role == "marksman" ? 0.18f : role == "scout" ? 0.32f : 0.4f;
            AimAt(A, E, err, dt);
            bool seeIt = d < 60 && LosClear(G.x, G.y + G.H * 0.85f, G.z, t.x, t.y + t.H * 0.6f, t.z, 1.5f);
            if (FriendInLine(A, E)) { I.fire = false; gx += -uz * B.strafe * 3; gz += ux * B.strafe * 3; }
            else if (G.hold == null) I.fire = AiTrigger(A, E, d, seeIt, dt);
            if (A.dodge is var (ddx, ddz) && A.dodge != null && B.seq == "dash") { gx = G.x + ddx * 5; gz = G.z + ddz * 5; } else if (B.seq == null) A.dodge = null;
        }
        else
        {
            // nothing to fight: hold a spot near you (each has its own), pick up what's useful
            A.brainState = it != null ? "SCAVENGING" : "FOLLOW"; A.AIM.ok = false; I.aimYaw = G.facing;
            if (B.cover != null && B.cover.by == A) { B.cover.by = null; B.cover = null; }
            var sl = COMP.TryGetValue(A.type, out var cc) ? cc.slot : new[] { 2f, 2 }; float f = H.facing, fx = -MathF.Sin(f), fz = -MathF.Cos(f), rx = MathF.Cos(f), rz = -MathF.Sin(f);
            float sx = H.x + rx * sl[0] + fx * sl[1], sz = H.z + rz * sl[0] + fz * sl[1], ds = Hypot(sx - G.x, sz - G.z);
            if (it != null) { gx = it.x; gz = it.z; speed = 0.9f; }
            else if (ds > 1.5f) { gx = sx; gz = sz; speed = MathF.Min(1, ds / 5 + (Hypot(H.vx, H.vz) > TOP * 0.9f ? 1 : 0)); }
            // keep up with you when you're rolling fast: roll too
            if (Hypot(H.vx, H.vz) > TOP * 1.2f && dH > 8) I.roll = true;
        }
        // fallen behind: follow your trail (round the obstacles and up the stairs you took), running, rolling on long flats
        if (B.regroupT > 0.4f && (B.tgtA == null || dH > 30))
        {
            var cg = CrumbGoal(A);
            if (cg is var (cx, cz)) { gx = cx; gz = cz; speed = 1; A.brainState = "CATCHING UP"; if (dH > 30 && G.ground && B.seq == null) I.roll = true; }
        }
        else B.crumbI = -1;
        var v = Steer(A, gx, gz, speed, dt); I.mx = v.Item1; I.mz = -v.Item2;
        RunSeq(A, dt);
        if (!I.roll) Uncurl(A, dt, 0.9f);
    }
    // still curled up from a dash or a roll with no reason to be: tap roll to stand up and shoot
    static void Uncurl(Actor A, float dt, float after)
    {
        var G = A.P; var B = A.brain;
        if (G.rolling && G.ground && B.seq == null && !G.charging) { A.curlT += dt; if (A.curlT > after && !G.prevRoll) { A.IN.roll = true; A.curlT = 0; } }
        else A.curlT = 0;
    }

    // =====================================================================
    // Bosses: mimics. Dark copies of you and your companions, red-eyed and wrapped in an aura, far tougher than anything
    // else, and they fight the way you do. Sandbox: one turns up every few minutes. Waves: every fifth wave is a boss wave.
    // =====================================================================
    public sealed class BossState { public float nextT = 150 + R() * 90; public List<Actor> list = new(); public HashSet<Actor> announced = new(); }
    public readonly BossState BOSS = new();
    string[] MimicTypes() { var t = new List<string> { "mhero" }; foreach (var A in ALLIES) t.Add("m" + A.type); return t.Count > 1 ? t.ToArray() : new[] { "mhero", "mhero", "mally", "mally2", "mally3" }; }
    public Actor SpawnBoss(float x, float z, string kind, string type = null)
    {
        var mt = MimicTypes(); type ??= mt[(int)(R() * mt.Length)];
        var S = new Squad { id = ++squadIds, kind = kind ?? "debug", route = new() { (x, z) }, comp = new SquadComp(), x = x, z = z, state = "investigate", investigate = (HUMAN.P.x, HUMAN.P.z), searchT = 60, respawnT = 1e9f, size0 = 1, boss = true };
        squads.Add(S);
        var A = MakeActor(type, x, z, false, MathF.Atan2(-(HUMAN.P.x - x), -(HUMAN.P.z - z)));
        A.squad = S; A.brain = MakeBrain(A); A.isBoss = true; A.slotI = 0; A.slotN = 1;
        A.P.homing = 99; A.P.shield = 0; A.brain.provoked = true;
        S.members.Add(A); BOSS.list.Add(A);
        for (int i = 0; i < 16; i++) Puff(A.P.x, A.P.y + 1, A.P.z, 3, 3, 0.3f);
        Sfx("alert", A.P.x, A.P.y + 1, A.P.z, 1);
        AnnounceBoss(A);
        return A;
    }
    public static string BossName(Actor A) => TYPES[A.type].Label.ToUpperInvariant();
    public float bossFlashT;
    void AnnounceBoss(Actor A)
    {
        if (!BOSS.announced.Add(A)) return;
        ShowHint("⚠ BOSS · " + BossName(A) + " · " + DirWord(A.P.x, A.P.z).ToUpperInvariant());
        bossFlashT = 1.2f; Rumble(0.8f, 0.6f, 400);
    }
    void StepBosses(float dt)
    {
        BOSS.list.RemoveAll(b => !b.P.alive || !actors.Contains(b));
        if (WAVES.on || SET.density <= 0) return;
        // sandbox: a boss turns up every few minutes, somewhere out of sight
        if (BOSS.list.Count == 0 && (BOSS.nextT -= dt) <= 0)
        {
            BOSS.nextT = 200 + R() * 140; var H = HUMAN.P;
            for (int k = 0; k < 20; k++)
            {
                float a = R() * TAU, d = 45 + R() * 20, x = H.x + MathF.Cos(a) * d, z = H.z + MathF.Sin(a) * d;
                if (MathF.Abs(x) > HALF - 30 || MathF.Abs(z) > HALF - 30 || InsideStatic(x, z, GroundY(x, z), 1.5f)) continue;
                SpawnBoss(x, z, "debug"); break;
            }
        }
    }
    // the boss's health for the HUD: body and armor together
    public static float BossHealth(Actor A) { float a = 0, am = 0; foreach (var pc in A.armor) { a += MathF.Max(0, pc.hp); am += pc.max; } return (HealthOf(A) * 3 + (am > 0 ? a / am : 0)) / 4; }
    // ---- the brain: you, played by the computer
    void MimicBrain(Actor A, float dt)
    {
        var G = A.P; var B = A.brain; var I = A.IN;
        I.mx = I.mz = 0; I.yaw = 0; I.fire = false; I.ads = false; I.throwAt = null;
        if (B.seq == null) { I.jump = false; I.roll = false; I.grab = false; I.pound = false; }
        if (!G.alive) return;
        if (G.grabbed != null) { B.mash -= dt; if (B.mash <= 0) { B.mash = 0.09f; I.jump = !I.jump; } return; }
        if (G.stunT > 0 || G.ragdoll != null) return;
        B.dashCD -= dt; B.poundCD -= dt; B.throwCD -= dt; B.think -= dt; B.punchCD -= dt;
        // target: whoever of yours is nearest, you first
        if (B.think <= 0 || B.tgtA == null || !B.tgtA.P.alive || B.tgtA.dormant)
        {
            B.think = 0.4f; float bd = 1e9f; B.tgtA = null;
            foreach (var Hh in actors) { if (Hh.team != "hero" || !Hh.P.alive || Hh.dormant || Hh.hidden) continue; float d0 = Hypot(Hh.P.x - G.x, Hh.P.z - G.z) - (Hh == ME ? 8 : 0); if (d0 < bd) { bd = d0; B.tgtA = Hh; } }
        }
        var T = B.tgtA; if (T == null) return;
        var t = T.P; float d = Hypot(t.x - G.x, t.z - G.z), dd = d == 0 ? 1 : d, ux = (t.x - G.x) / dd, uz = (t.z - G.z) / dd;
        bool seeIt = d < 70 && LosClear(G.x, G.y + G.H * 0.85f, G.z, t.x, t.y + t.H * 0.6f, t.z, 1.5f);
        var S = A.squad; if (S != null && S.state != "combat") { S.state = "combat"; S.know = new Knowledge { A = T, x = t.x, z = t.z, t = simT }; S.engageT0 = simT; }
        B.strafeT -= dt; if (B.strafeT <= 0) { B.strafeT = 0.6f + R() * 1.1f; B.strafe = R() < 0.5f ? -1 : 1; }
        // circle-strafe at a fighting distance that suits the gun in hand
        var w = CurDef(A); float want = w.Type == "melee" ? 1.6f : w.Radius != 0 ? 14 : 10;
        float mx = -uz * B.strafe, mz = ux * B.strafe;
        if (d > want + 3) { mx += ux * 1.4f; mz += uz * 1.4f; } else if (d < want - 3) { mx -= ux; mz -= uz; }
        float gx = G.x + mx * 4, gz = G.z + mz * 4, speed = 1;
        // under fire: curl up and roll out of it
        bool hurtRecently = simT - A.lastHurt < 0.6f;
        if (hurtRecently && G.ground && B.seq == null && R() < dt * 0.8f) { StartSeq(B, "roll"); B.rollT = 0.05f; }
        // jump-dash (homing) at the target from mid range
        if (G.ground && d > 5 && d < 24 && B.dashCD <= 0 && B.seq == null) { StartSeq(B, "dash"); B.dashCD = 2.6f + R() * 2; gx = t.x; gz = t.z; }
        // charge a spin dash from a standstill and let it go at the target
        else if (G.ground && d > 8 && d < 30 && B.dashCD <= 0 && B.seq == null && R() < 0.4f) { StartSeq(B, "spin"); B.dashCD = 4; }
        // above the target: ground pound
        if (!G.ground && G.y - t.y > 2.5f && d < 3.5f && B.poundCD <= 0) { I.pound = true; B.poundCD = 3; }
        // grab a prop or a small one of yours and throw it
        if (G.hold == null && G.holdA == null && B.throwCD <= 0 && d > 6 && d < 26) { B.pick = NearProp(A, 7, 3, 1500); B.pickT = 3; B.throwCD = B.pick != null ? 6 : 3; }
        if (B.pick != null && G.hold == null)
        {
            B.pickT -= dt;
            if (B.pickT <= 0 || B.pick.Held != null || B.pick.Removed) B.pick = null;
            else { gx = B.pick.Pos.x; gz = B.pick.Pos.z; if (Hypot(gx - G.x, gz - G.z) < 1.6f * G.s + B.pick.Bound * 0.6f) { G.facing = MathF.Atan2(-(gx - G.x), -(gz - G.z)); I.aimYaw = G.facing; if (WithActor(A, () => TryPickUp(A))) B.pick = null; } }
        }
        if (G.hold != null || G.holdA != null) { B.holdT += dt; if (B.holdT > 0.45f && B.seq == null) { I.throwAt = new V3(t.x + t.vx * 0.4f, t.y + t.H * 0.5f, t.z + t.vz * 0.4f); StartSeq(B, "grab"); B.holdT = 0; } }
        else if (d < 1.8f + t.R && B.punchCD <= 0 && T.type != "mecha") { B.punchCD = 9 + R() * 6; if (R() < 0.35f) GrabActor(A, T); }
        // weapons: pick for range, aim like a player (a little lead), hover while shooting in the air
        AiPickWeapon(A, d, dt, d < 2.2f);
        AimAt(A, T, 0.16f, dt);
        if (G.hold == null && G.holdA == null) { I.fire = seeIt && AiTrigger(A, T, d, seeIt, dt); if (I.fire && !G.ground && G.hoverFuel > 0) I.ads = true; }
        // the spin dash: hold roll from still, then let go when lined up
        if (B.seq == "spin")
        {
            B.seqT += dt; G.facing = MathF.Atan2(-ux, -uz);
            if (B.seqT < 0.8f) { I.roll = true; gx = G.x; gz = G.z; speed = 0; }
            else { I.roll = false; gx = t.x; gz = t.z; speed = 1; if (B.seqT > 1.4f) B.seq = null; }
        }
        if (B.seq == "dash") { gx = t.x; gz = t.z; }
        var v = Steer(A, gx, gz, speed, dt); I.mx = v.Item1; I.mz = -v.Item2;
        if (B.seq != "spin") RunSeq(A, dt);
        if (!I.roll) Uncurl(A, dt, 0.7f);
    }
    // delayed calls on the simulation clock (the original used setTimeout)
    readonly List<(float t, Action a)> later = new();
    void Later(float secs, Action a) => later.Add((simT + secs, a));
    void RunLater() { for (int i = later.Count - 1; i >= 0; i--) if (simT >= later[i].t) { var a = later[i].a; later.RemoveAt(i); a(); } }
}

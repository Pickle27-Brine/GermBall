# Germ Ball 2.0 (in development)

Germ Ball 2.0 is a rebuild on a new, small engine. It has the core game systems, a humanoid hero, static germ enemies with ragdolls, and one debug level made of plain boxes. There is no AI yet. Each build iterates on this version.

It comes with two editors, **EntityEditor.exe** and **MapEditor.exe**. Everything the game shows can be built or changed with them: the hero, the germs and their animation, and the maps.

**Germ Ball 1.0 is deprecated.** The original full game is frozen in [`legacy/`](legacy/README.md) and published once as the **Germ Ball 1.0** release. It isn't rebuilt or changed any more.

## Download and play

Every push builds everything for Windows. On the repository's **Releases** page, open **Germ Ball 2.0-dev: <branch>** and download `GermBall-win-x64.zip`. Unzip it, then double-click:

- `GermBall.exe`: the game
- `EntityEditor.exe`: build and animate the hero, enemies and props
- `MapEditor.exe`: build maps

Keep the `content` folder next to them: the editors save into it and the game loads from it. Nothing else needs installing.

## What's in it

- **The hero** is 1.0's gelcap in humanoid form: a red-and-white two-tone body with a visor helmet, pauldrons and a glowing cross. It's animated procedurally:
  - The run cycle's stride matches your speed, so planted feet don't slide, and each foot finds its own floor on steps and slopes.
  - Arms swing as you run, you lean into speed and back into skids, and you tuck a knee when you leave a ledge.
  - The gun arm follows your aim (two-handed weapons use both hands). Punches and reloads have their own poses.
- **Movement.** Movement uses 1.0's momentum-based tuning.
  - **Running has inertia.** Speed builds over about 2.6 s to top speed (9.8 m/s). Let go and you coast to a stop over about 2 s. Reverse at speed and you skid. The faster you go, the wider you turn. Slopes slow you going up and speed you up going down, so steep ramps need a run-up.
  - **Jumping curls you into a ball.** Hold jump for full height (2.7 m) or tap it for a short hop. You land on your feet, unless you hold roll, in which case you land rolling with your momentum.
  - **In the air:** press jump again to air-dash. Press punch to ground-pound (a short windup, then a slam). Hold aim to uncurl and hover for up to 2.5 s so you can shoot.
  - **Rolling.** Press roll while moving to curl up. A roll has no push on flat ground: you can steer and brake, and slopes speed you up. It uncurls when it runs out of speed. Hold roll to stay curled.
  - **Spin dash.** Press roll while standing still, tap jump to rev, then let go of roll to launch at 20–30 m/s.
  - **Walking** uses a floating capsule, so steps up to 0.45 m and slopes up to 52° never snag. The ball fits through 1.1 m gaps, and you can't stand up without headroom.
- **Combat.**
  - There are three weapons: a pistol, an automatic rifle, and a lobber that fires grenades with blast damage and rocket-jump knockback. Each has a magazine, reserve ammo and reloads.
  - You can also punch, aim down sights, and land headshots for double damage.
  - There are hit markers, damage numbers and a DPS readout.
  - You can't shoot while curled up, as in 1.0. To shoot in the air, hover.
- **Germs (static enemies).** Germs don't move or attack; you get hurt by walking into one. There are four kinds, using 1.0's numbers:
  - **Coccus:** 45 HP, scale 0.92.
  - **Bacillus:** 100 HP, scale 1.2.
  - **Amoeba brute:** 320 HP, scale 1.68.
  - **Phage titan:** 900 HP, scale 2.7.

  Every attack damages them: shots (headshots double), punches, blasts, ground pounds and fast rolls.
- **The jump dash is an attack.** Dashing into a germ damages it and bounces you up with your dash restored, so you can chain into the next one. It works like 1.0:
  - One dash floors a coccus or bacillus, a brute takes two, and the titan takes four. The count resets after 8 s.
  - The dash bends slightly toward the best germ ahead of you.
  - A roll faster than 7 m/s (a spin dash, for example) bowls small germs over too.
- **Ragdolls.** Every body has a 16-joint Verlet ragdoll that collides with the level.
  - Floored germs tumble, lie there, then blend back to standing.
  - Dead germs ragdoll and come back 6 s later.
  - When you die, the hero ragdolls until you respawn.
- **Health.** You have 100 HP and a 50-point shield. The shield regenerates after 3 s without damage. When you die, you respawn after 2 s at the last checkpoint.
- **Targets and props.**
  - Target dummies stand still or slide along a rail.
  - Physics balls can be shot, punched, blasted or pushed.
  - Pickups give health, ammo, the rifle and the lobber.
- **Trigger zones:** area names, acid, a heal pad, a kill volume, jump pads, boost pads, low gravity, an updraft and a checkpoint.

## Controls

The camera is locked behind the hero: looking turns the hero. Aiming eases the camera in over a fifth of a second.


| Action | Keyboard / mouse | Controller |
|---|---|---|
| Move / look | WASD / mouse | left / right stick |
| Jump (hold for height), again in the air to dash | Space | A |
| Roll (hold to stay curled) | Shift or C | B |
| Spin dash | roll while still, tap jump to rev, let go of roll | B, tap A, let go of B |
| Ground pound | Q while curled in the air | RB while curled in the air |
| Hover (in the air) | hold right click | hold LT |
| Fire / aim | left / right click (F also fires) | RT / LT |
| Punch | Q or mouse 4 | RB |
| Reload | R | X |
| Weapons | 1 2 3, mouse wheel | Y |
| Pause | Esc | Start |

Click the window to capture the mouse. Esc frees it.

| Debug key | What it does | Controller |
|---|---|---|
| F1 | Show or hide help | Back |
| F2 | Debug readout: position, velocity, state (running, skidding, curled, spin-dash revs, hover fuel), slope, last jump height, landing speed, DPS | D-pad right |
| F3 | Show zone volumes | |
| F4 | Noclip fly (Space up, Shift down, right click slow) | |
| F5 | Respawn | D-pad left |
| F6 / F7 | Teleport to the next / previous landmark | D-pad up / down |
| F8 | God mode | |
| F9 | Slow motion (25%) | |
| F11 / Alt+Enter | Fullscreen | |

## The debug level

The level is a flat 240 m square. North is -Z. Every solid has a 1 m grid, and signs label every test. You can teleport to each landmark with F6.

| Area | Where | What it tests |
|---|---|---|
| Hub | centre | spawn; signs pointing to each area |
| Steps | north | blocks 0.1–0.8 m tall: up to 0.45 m walk up, taller need a jump |
| Ramps | north | 10°–60° ramps, 2.5 m tall: up to 50° with a run-up, 55° and 60° never |
| Stairs | north-west | 0.25 m rises (walkable) next to 0.5 m rises (jump each one) |
| Walls | north-east | 1–4 m walls for jump and dash height |
| Gaps | north | 1 m platforms with 2–8 m gaps |
| Crawl tunnel | north-west | 1.1 m of headroom: spin dash through |
| Drops | north-east | ledges at 2, 4, 8 and 16 m, each with a jump pad |
| Combat range | east | dummies at 10–75 m, movers at 3 and 7 m/s, a 400 HP heavy, cover, armory pickups |
| Physics | west | 10 balls, a 12° rolling slope, a skate bowl with 25° banks |
| Germ yard | south-east | a row of cocci, a dash chain of cocci on posts, bacilli, two brutes and a phage titan |
| Zones | south | acid, heal pad, a pit with a 0.6 m beam and a checkpoint, 3/6/10 m jump pads, low gravity, updraft, boost lane |

## The Entity Editor

`EntityEditor.exe [name]` opens `content/entities/<name>.entity.json` (default: hero).

**What an entity is:**

- **A rig:** bones, each an offset from its parent joint.
- **Shapes** fixed to bones: box, sphere, capsule, cylinder, cone or striped ball, each with a position, rotation, size, colour, and glow or hidden flags.
- **Animation clips:** keyframes per bone (rotation, movement, and scale for squash and stretch), each easing smooth, linear or step to the next key.
- **A layer stack** that blends everything into the final pose.
- **State mappings:** which clip plays in each game state.
- **Stats:** health, scale, contact damage, dashes to floor, and collision size.

The game uses all of this.

**Left panel:**

- **Entity:** name, kind (player, enemy or prop), gameplay stats, and curled-up style (striped ball or its own shapes spun).
- **Rig:** the bone tree. Add, delete, rename and reparent bones, edit offsets and lengths with sliders or the move gizmo, mirror left to right, or reset to the standard humanoid. Renaming a bone updates every shape, clip and mask that uses it.
- **Shapes:** add, duplicate, delete, mirror and reorder shapes. Sliders cover position, rotation and size; "Point along bone" fits a capsule to its bone. There's a colour picker (hue, saturation and brightness, or type a hex code). Click a shape in the view to select it, or drag it with the gizmo.

**Right panel:**

- **Clips:** create, duplicate, rename and delete clips, and set length and looping.
  - Pick a bone (click a joint in the view) and set its rotation, movement and scale at the playhead. Editing a value keys it.
  - Other tools: copy and paste a pose, mirror a pose left to right, key all bones, and preview the clip alone, over the procedural motion, or as additive.
- **Layers:** the animation pipeline, evaluated top to bottom.
  - Each layer takes its motion from **procedural** (players get the run cycle, foot IK, aim, punch and reload; others an idle sway), **clip** (loops a clip) or **state** (plays the clip mapped to the current game state, with crossfades).
  - Layers can **override** or be **additive**, with weight, speed and a bone mask. "Upper body" is a one-click mask, and children can be included.
- **States:** pick a clip and a crossfade time for each game state: idle, run, skid, jump, fall, curl, spin, hover, aim, fire, punch, reload, hurt, stagger, down and dead.
- **Preview:** drive the procedural motion by hand to see the whole stack.
  - Settings: run speed, on the ground, skidding, hovering, curled, holding a one- or two-handed gun, aiming (with direction), plus punch and reload buttons.
  - Enemy idle: sway and raised arms.
  - Other tools: drop the ragdoll, turntable view, and toggles for the grid, bones, shapes and collision.

**Bottom: the timeline.** Play, pause and step through frames (30 a second). Scrub the ruler, click a key to select it, drag keys to retime them, and press Delete to remove the selected one.

**Keys and mouse:**

- File: Ctrl+S save, Ctrl+Z / Ctrl+Y undo and redo.
- Editing: Space play, F frame the selection, Ctrl+D duplicate a shape, Del delete.
- View: right-drag orbits, middle-drag pans, the wheel zooms.
- Numbers: right-click any slider or number to type an exact value. Shift drags finely, Ctrl drags coarsely.

**Test in game** saves, then starts the game. Players start the game as the entity (`--hero <name>`); enemies spawn one in front of you (`--spawn <name>`).

## The Map Editor

`MapEditor.exe [name]` opens `content/maps/<name>.map.json` (default: debug).

**What a map is:** the player spawn, a kill height, and a list of objects. Each object is one of:

- box, with any size and rotation
- ramp (angle, height, width, landing)
- trigger zone, any kind: area, damage, heal, kill, jump pad, boost, low gravity, updraft or checkpoint
- germ (any entity)
- target dummy (optionally on a rail)
- physics ball
- pickup
- sign
- landmark (F6/F7 teleport targets)

**The view** draws the map exactly as the game does, including the germs' idle animation. It also shows the spawn, signs, landmarks and zone outlines.

- **Flying:** hold the right mouse button and use WASD (Q/E down and up, Shift fast); the wheel sets the speed.
- **Editing:** click to select. Drag the gizmo's arrows (or its ground square) to move, snapped to the grid size (0.1–2 m) unless Snap is off.
- **Keys:** R / Shift+R rotate 15°, Ctrl+D duplicate, Del delete, F focus, Esc deselect, Ctrl+Z / Ctrl+Y undo and redo, Ctrl+S save.

**Left panel:**

- **Add palette:** new things go where the view is pointing, on the floor.
- **Outliner:** filter by type, search, and double-click to focus.

**Right panel:**

- **Properties** for the selection, all sliders and fields.
- **Jump pads** have an aiming helper: give it a ledge height and a landing point, and it computes the launch velocity.
- **With nothing selected:** map settings (name, kill height, player spawn).

**Play test** saves, then starts the game on the map (`--map <name>`).

## Content files

- **Where they live:** `content/entities/*.entity.json` and `content/maps/*.map.json` are plain, readable JSON (vectors are `[x, y, z]`). The editors write them, and you can also edit them by hand.
- **How the game uses them:** it loads `content/` next to the exe (or `--content <dir>`), plays `--map <name>` (default `debug`), and falls back to built-in defaults for anything missing.
- **Standard rig:** entities with the 16 standard humanoid bones (pelvis, chest, neck, head, shoulder/elbow/hand and hip/knee/foot, .L and .R) get the procedural motion and ragdolls. Extra bones (tails, antennae) are animated by clips.

## For development

```
src/GermBall.Core/    platform-free engine and game
  Engine/             math, oriented-box collision, unit meshes, the draw list, a 5x7 pixel font, skeleton and ragdoll, UI input
  Content/            entity and map formats, the content store, the animation pipeline, entity drawing, the default entities
  Sim/                Level (built from maps), Player, HeroAnim (procedural motion), Enemies (germs), Bodies (dash hits,
                      contact, ragdolls), BodyDraw, Weapons, Entities, Game (loop, camera), Draw, Hud
src/GermBall.Platform/ Windows: Win32 window, keyboard/mouse/XInput, Direct3D 11 renderer, the editor host
src/GermBall/         GermBall.exe (the game)
src/GermBall.Tools/   platform-free editors: an immediate-mode GUI and 3D view, the Entity Editor, the Map Editor
src/GermBall.EntityEditor/, src/GermBall.MapEditor/   EntityEditor.exe and MapEditor.exe
content/              the default entities and the debug map, shipped next to the exes
tests/GermBall.Tests/ headless checks and a software rasteriser for screenshots
legacy/               Germ Ball 1.0 (deprecated, frozen)
```

- **Tuning.** Movement is the constants at the top of `src/GermBall.Core/Sim/Player.cs`, and weapons are in `WeaponDef` in `Sim/Weapons.cs`. Entities and maps are edited with the editors.
- **Tests.** There are 32 scripted checks, and CI runs them before every build. They cover:
  - movement, the camera, zones, combat and germs
  - content round-trips and animation layers
  - both editors driven through their real interface (clicking, typing, dragging the gizmo, undo, save, play test)
  - random clicking in the editors, and a random-input soak in the game. To run them yourself, or to write screenshots:

```bash
dotnet run --project tests/GermBall.Tests -c Release
```

```bash
dotnet run --project tests/GermBall.Tests -c Release -- shots out 1280 720
```

To screenshot both editors:

```bash
dotnet run --project tests/GermBall.Tests -c Release -- editor-shots out
```

## Not in 2.0 yet

- **AI.** Germs stand still, and there are no companions or pathfinding.
- **Stage art.** The level is boxes and a grid only.
- **Other 1.0 systems:** audio and music, menus, the customizer, vehicles, grabbing and throwing, the charged homing dash, losing limbs, and saves.

These can come back one at a time.

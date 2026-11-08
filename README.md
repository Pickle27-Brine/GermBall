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

- **The hero** is 1.0's gelcap in humanoid form: a red-and-white two-tone body with a visor helmet, pauldrons and a glowing cross. It's animated with keyframed clips you can edit in the Entity Editor:
  - Idle, run, skid, fall and hover clips play by movement state. The run clip plays faster the faster you run, so planted feet don't slide.
  - The gun arms blend between aim-down, aim and aim-up clips by your aim pitch. Punch, reload and flinch clips play over the top on the arms or body.
  - Foot IK is the only procedural part: it puts each foot on its own floor on steps and slopes.
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
- **Limbs come off,** like 1.0 but rarer and calmer:
  - Only direct hits (shots and punches) near an arm or leg count, and only hits of at least 18 damage, so the rifle never takes limbs.
  - Each limb has its own health (at least two pistol shots), and a germ loses at most two.
  - A severed limb tumbles away and fades after 6 s.
  - Losing an arm staggers a germ. Losing a leg floors it for good.
  - Limbs come back when the germ respawns. All of this is set per entity in the Entity Editor.
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
- **Shapes** fixed to bones: box, sphere, capsule, cylinder, cone or striped ball, each with a position, rotation, size, taper (one end narrower), curve (bent along its length), colour, and glow or hidden flags.
- **Animation clips:** keyframes per bone (rotation, movement, and scale for squash and stretch), each easing smooth, linear or step to the next key.
- **A layer stack** that blends everything into the final pose.
- **State mappings:** which clip plays in each game state.
- **Stats:** health, scale, contact damage, dashes to floor, and collision size.
- **Limbs:** which bones can come off, and how hard that is.

The game uses all of this.

**Toolbar:** New (humanoid, hero copy, germ copy or empty) and Open menus, Save, Undo and Redo, the Move / Rotate / Scale gizmo, and **Play in game**. Hover over any control for a short tip about it.

**Left panel:**

- **Model:** the selected shape's controls sit at the top: name, shape, the bone it's attached to, position, rotation, size and colour.
  - **Form** (folded away) holds taper, curve, "Fit to bone", glow and hidden.
  - Below that, every shape is grouped by body part: head, torso, left and right arms, left and right legs, and other. Each part folds away.
  - Add (pick the type under "New shape"), Copy, Mirror and Delete sit above.
- **Rig:** the bone tree, by friendly names (Hips, Abdomen, Chest, Upper arm L, Forearm L, Thigh L, Shin L...).
  - Add, delete, rename and reparent bones.
  - Edit the joint and the length with sliders or the gizmos. Rotating or scaling a bone swings and stretches its children and shapes about the joint.
  - Mirror left to right, or reset to the standard humanoid (with a two-part torso).
  - Renaming a bone updates every shape, clip, mask and limb that uses it.
- **Settings:** name and kind (player, enemy or prop), plus three sections:
  - **Gameplay:** health, scale, contact damage, dash hits, collision.
  - **Curled up:** striped ball or its own shapes spun.
  - **Limbs:** whether limbs can come off, the smallest hit that counts, how many it can lose, and how long debris lasts.
    - Each limb is a bone and everything below it, with its own health and a "losing it kills" option.
    - Add a limb at any bone, or add both arms and legs in one go.

**Right panel:**

- **Animate:** pick a clip, then New, Copy or Delete. Set its name, length and looping.
  - "Preview this clip alone" shows just the clip; "Plant the feet" adds foot IK, as in the game.
  - Click a body part, then set its rotation, movement and scale at the playhead. Editing a value keys it. You can also add or delete the key there and set its ease.
  - **Pose tools** (folded away): copy and paste a pose, mirror it left to right, key all bones, and clear a bone.
- **Layers:** the animation pipeline, evaluated top to bottom. Each layer plays one of these:
  - **state clips:** the clip given to the current state. Select this layer to give a state a clip, set its fade, and make it play faster when running (at full rate at a set speed, so feet don't slide). States without a clip keep the movement's clip.
  - **one clip.**
  - **blend:** clips placed along a parameter (speed, aim pitch or vertical speed), blended by its value. The hero's aim uses this.
  - **foot IK:** feet on the ground under them.

  Layers **override** or are **additive**, with a weight and speed. **Plays in** picks the states a layer is active in (aim, punch, hurt...), with a fade. **Bones** is the mask.
- **Preview:**
  - **State:** a movement (idle, run, fall...), an action on top (aim, punch, reload, hurt...), run speed, aim up and down, on the ground, curled up, and holding a gun.
  - **Ragdoll and limbs:** drop the ragdoll and stand it up, or cut off a limb and put the limbs back.
  - **View:** turntable, grid, bones, shapes, collision and frame.

**In the view:**

- **Picking:** like Blender, the piece of the model under the mouse lights up, and clicking it selects that shape and its bone. On the Rig and Animate tabs you can also click a bone.
- **Bones** are drawn from their joint (the pivot, the thick end with the ball) to the next joint (the thin end). Selecting the forearm lights up the forearm, and rotating it turns it about the elbow.
- **Gizmos:** there's a move, rotate and scale gizmo (W, E, R, or the toolbar). What they work on follows the tab you last used:
  - **Model:** a shape's position, rotation (rings around its own axes) and size (per axis, or evenly from the centre).
  - **Rig:** a bone's rest pose.
  - **Animate:** the selected bone at the playhead, which sets a key.

  The top-left corner of the view says what you're editing.

**Bottom: the timeline.** Play, pause and step through frames (30 a second). Scrub the ruler, click a key to select it, drag keys to retime them, and press Delete to remove the selected one.

**Keys and mouse:**

- File: Ctrl+S save, Ctrl+Z / Ctrl+Y undo and redo.
- Editing: Space play, F frame the selection, Ctrl+D duplicate a shape, Del delete.
- View: right-drag orbits, middle-drag pans, the wheel zooms.
- Numbers: click any slider or number to type an exact value; drag it to change it (Shift drags finely, Ctrl coarsely).
- Text: the whole value is selected when you start, so typing replaces it. Left, Right, Home and End move the caret; Ctrl+A selects all; Ctrl+V pastes. Enter, Tab or clicking elsewhere keeps the edit, and Escape cancels it.

**Play in game** saves, then starts the game. Players start the game as the entity (`--hero <name>`); enemies spawn one in front of you (`--spawn <name>`).

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
- **Standard rig:** entities with the 16 standard humanoid bones get foot IK, ragdolls and destructible limbs. Those bones are pelvis, chest, neck, head, shoulder/elbow/hand and hip/knee/foot (.L and .R). An optional abdomen bone between pelvis and chest splits the torso in two (the hero has one, and the spine bends at both). Extra bones (tails, antennae) are animated by clips.

## For development

```
src/GermBall.Core/    platform-free engine and game
  Engine/             math, oriented-box collision, unit meshes, the draw list, a 5x7 pixel font, skeleton and ragdoll, UI input
  Content/            entity and map formats, the content store, the animation pipeline, entity drawing, the default entities
  Sim/                Level (built from maps), Player, HeroAnim (rig joints), Enemies (germs), Bodies (dash hits,
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
- **Other 1.0 systems:** audio and music, menus, the customizer, vehicles, grabbing and throwing, the charged homing dash, and saves.

These can come back one at a time.

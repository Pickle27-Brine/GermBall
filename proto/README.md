# Germ Ball prototype

This is a stripped-back rebuild of Germ Ball on a new engine of about 3,700 lines. It keeps the core game systems, uses one debug level made of plain boxes, and has no AI. The full game in `../src` is untouched and stays there for reference.

## What's in it

- **Movement.** The hero is a gelcap. It can walk, jump (with coyote time and a jump buffer), air-dash once per jump, roll into a ball, and ground-pound while rolled up in the air.
  - **Walking** uses a "floating capsule". The body collider sits 0.45 m above the feet, and five short rays find the floor. Steps up to 0.45 m and slopes up to 52° are walked with no snagging. Walking down slopes and off small ledges keeps the feet on the ground.
  - **Rolling** makes the hero a 0.5 m sphere with real slope gravity, momentum and braking. Only the ball fits through 1.1 m gaps, and you can't stand up without headroom.
- **Combat.** There are three weapons: a pistol, an automatic rifle, and a lobber that fires grenades with blast damage and rocket-jump knockback. Weapons have magazines, reserves and reloads. You can also punch, aim down sights, and land headshots for double damage.
  - Shots go where the crosshair points: the aim ray starts at the camera, and the shot is traced from the muzzle.
  - There are hit markers, damage numbers and a DPS readout.
- **Health.** You have 100 HP and a 50-point shield. The shield regenerates after 3 s without damage. When you die, you respawn after 2 s at the last checkpoint.
- **Targets.** Target dummies have no AI. They stand still or slide along a rail, wobble when hit, and respawn 3 s after being destroyed.
- **Physics balls** roll and bounce. You can shoot them, punch them, blast them or push them by walking or rolling into them.
- **Pickups** for health, ammo, the rifle and the lobber respawn after 8 s.
- **Trigger zones**:
  - area names
  - acid (damage)
  - heal pad
  - kill volume
  - jump pads (each one aimed at its ledge)
  - boost pads
  - low gravity
  - an updraft
  - a checkpoint

## Layout

```
proto/
  Core/            platform-free: runs anywhere .NET 8 runs
    Engine/        math, oriented-box collision, unit meshes, the draw list, a 5x7 pixel font for the HUD
    Sim/           the game: Level (and the debug level), Player, Weapons, Entities, Game (loop and camera), Draw, Hud
  Win/             Windows: Win32 window, keyboard/mouse/XInput, a Direct3D 11 renderer (one instanced shader)
  Test/            headless checks and a software rasteriser that renders the same draw list to PNG
```

The core never calls the platform. Each frame the Windows layer:

1. turns input into an `InputFrame`
2. calls `Game.Frame(dt, input)`, which runs the fixed 120 Hz simulation
3. calls `Game.Draw(drawList, w, h)`
4. hands the draw list to the renderer.

The draw list holds unit shapes (cube, sphere, cylinder, cone, striped ball), each with a transform, a colour and flags (`Grid`, `Unlit`). The HUD is coloured rectangles only, so the renderer needs no textures or font files.

## Download and play

Every push builds the Windows exes automatically. Open the repository's **Releases** page and pick **Windows build: <branch>**, then download `GermBallPrototype-win-x64.zip`, unzip it and double-click `GermBallProto.exe`. Nothing else needs installing. Each run of the **Windows exe** action also keeps the zips as an artifact.

## Controls

| Action | Keyboard / mouse | Controller |
|---|---|---|
| Move / look | WASD / mouse | left / right stick |
| Jump, then air dash | Space | A |
| Roll (hold) | Shift or C | B |
| Ground pound | Q while rolled up in the air | RB while rolled up in the air |
| Fire / aim | left / right click (F also fires) | RT / LT |
| Punch | Q or mouse 4 | RB |
| Reload | R | X |
| Weapons | 1 2 3, mouse wheel | Y |
| Pause | Esc | Start |

Click the window to capture the mouse. Esc frees it.

| Debug key | What it does | Controller |
|---|---|---|
| F1 | Show or hide help | Back |
| F2 | Debug readout: position, velocity, state, slope, last jump height, landing speed, DPS, counts | D-pad right |
| F3 | Show zone volumes | |
| F4 | Noclip fly (Space up, Shift down, right click slow) | |
| F5 | Respawn | D-pad left |
| F6 / F7 | Teleport to the next / previous landmark | D-pad up / down |
| F8 | God mode | |
| F9 | Slow motion (25%) | |
| F11 / Alt+Enter | Fullscreen | |

## The debug level

The level is a flat 240 m square. North is -Z. Every solid has a 1 m grid (with a heavier line every 5 m), and signs label every test. You can teleport to each landmark with F6.

| Area | Where | What it tests |
|---|---|---|
| Hub | centre | spawn; signs pointing to each area |
| Steps | north, z -21 | blocks 0.1–0.8 m tall: up to 0.45 m walk up, taller need a jump |
| Ramps | north, z -30 | 10°–60° ramps, 2.5 m tall: walkable up to 50°, 55° and 60° are not |
| Stairs | north-west | 0.25 m rises (walkable) next to 0.5 m rises (jump each one) |
| Walls | north-east | 1–4 m walls for jump and dash height |
| Gaps | north, z -50 to -110 | 1 m platforms with 2–8 m gaps |
| Crawl tunnel | north-west | 1.1 m of headroom: roll through |
| Drops | north-east | ledges at 2, 4, 8 and 16 m, each with a jump pad in front |
| Combat range | east | firing line and armory, dummies at 10/20/35/50/75 m, movers at 3 and 7 m/s, a 400 HP heavy, cover |
| Physics | west | 10 balls from 0.3 to 1.3 m radius, a 12° rolling slope, a skate bowl with 25° banks |
| Zones | south | acid strip, heal pad, a pit with a 0.6 m beam and a checkpoint beyond, 3/6/10 m jump pads, low-gravity room, updraft column, boost lane |

To change the level, edit `Level.BuildDebug` in `Core/Sim/Level.cs`. The builders are `Block`, `Box`, `Ramp`, `JumpPad`, `AddZone`, `Sign` and `Mark`. Spawns are lists on the level (`Dummies`, `Props`, `Pickups`). Movement tuning is the constants at the top of `Core/Sim/Player.cs`. Weapon tuning is in `WeaponDef` (`Core/Sim/Weapons.cs`).

## Tests

```bash
dotnet run --project proto/Test -c Release
```

The tests run on any OS. There are 18 scripted checks:

- step heights, ramp angles and stairs
- jump height and air dash
- the crawl tunnel, and rolling downhill
- every jump pad landing on its ledge
- acid and healing, the pit, respawn and the checkpoint
- low gravity, the updraft and the boost pad
- pickups
- pistol body shots and headshots, lobber blasts, punch and ground pound
- a random-input soak at every landmark that checks for NaNs, falling through the world and getting stuck in geometry.

To also write PNG screenshots of the level through the software rasteriser:

```bash
dotnet run --project proto/Test -c Release -- shots out 1280 720
```

## Left out on purpose

- **AI.** There are no enemies, companions, squads or pathfinding, and no AI code at all.
- **Stage art.** The level is boxes and a grid only.
- **Everything else from the full game:** audio and music, menus, the customizer, vehicles, ragdolls, buildings, fluids and saves.

These can come back one at a time on top of this core.

## Status

This prototype was written and tested on Linux:

- The core and every test run there.
- The Windows project compiles there against the Windows target.
- The HLSL was checked with DXC.

The window, the D3D11 renderer and controller input haven't been run on Windows yet.

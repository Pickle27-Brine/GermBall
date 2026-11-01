# Germ Ball (native Windows)

Germ Ball rewritten in C# as a native Windows game. The browser build (one HTML file running three.js) is gone. This version uses:

- a raw Win32 window, with borderless fullscreen and per-monitor DPI awareness,
- Direct3D 11 for rendering,
- XInput for controllers, with rumble,
- XAudio2 for sound,
- Windows SAPI for the companion voices.

There is no web layer, so the game isn't held to browser limits on memory, threads, frame rate, audio voices or storage.

## Requirements

- Windows 10 (1809+) or Windows 11, 64-bit
- A GPU with Direct3D 11 (feature level 11.0)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64) to run, or the .NET 8 SDK to build

## Build and run

```bash
dotnet build src/GermBall/GermBall.csproj -c Release
```

```bash
dotnet run --project src/GermBall/GermBall.csproj -c Release
```

For a folder you can zip up and hand to someone, with no .NET install needed on their machine:

```bash
dotnet publish src/GermBall/GermBall.csproj -c Release -r win-x64 --self-contained true -o publish
```

## Where files live (open file paths)

| What | Default location | Override |
|---|---|---|
| Game data (music and songs, title image) | `<game folder>\data\` | `--data <dir>` or `paths.json` |
| Settings | `Documents\My Games\GermBall\settings.json` | `--saves <dir>` or `paths.json` |
| Key bindings | `...\GermBall\keys.json` | |
| Your germ | `...\GermBall\custom.json` | |
| Last title-screen choices | `...\GermBall\boot.json` | |
| Best wave scores | `...\GermBall\best.json` | |
| Exported germs | `...\GermBall\germs\*.germ.json` (Export / Import in the customizer, any folder) | |
| Screenshots (F12) | `...\GermBall\screenshots\` | |
| Logs | `...\GermBall\logs\` | |

To keep everything next to the exe (portable install), put a `paths.json` beside `GermBall.exe`:

```json
{ "data": "data", "saves": "saves" }
```

Relative paths are resolved from the game folder, and environment variables such as `%APPDATA%` are expanded. Every file is plain, indented JSON written atomically: a temp file is written first and then swapped in. Under Settings → Files there are buttons that open these folders.

The music is data, not code: `data/music/songs.json` holds the 36 songs (chord roots, drum grids, bass lines, form, effect chain) and `data/music/patches.json` holds the FM instrument patches. Edit them, or add songs, without rebuilding. The title-screen pictures are `data/ui/lopez.png`, `data/ui/cast.png` and the map stills in `data/ui/maps/`.

## Controls

| Action | Keyboard / mouse | Controller |
|---|---|---|
| Move / look | WASD / mouse | left stick / right stick (the camera only turns when you turn it) |
| Jump, air dash | Space | A |
| Fire / swing | left click or F | RT |
| Aim | right click | LT |
| Punch (not while rolling) | Q or mouse 4 | RB |
| Ground pound | Q or mouse 4 while curled into a ball in the air | RB in ball mode only |
| Reload | R | tap X |
| Interact (take a weapon, enter a vehicle) | G | hold X |
| Grab and throw | E (or fire while holding: hold to wind up, release to throw) | LB (or RT while holding) |
| Roll / ball | C or Shift | B |
| Switch weapon | 1 2 3, wheel, P | Y |
| Call the base | B | Back |

Germs below brute rank that have lost both legs can be picked up and thrown. When three or fewer germs are left, each one gets a waypoint with its distance, pinned to the screen edge when it's off screen.

**Weapons in vehicles.** The blood cell and the virus fire their own mounted guns, so your hand weapons are stowed. On the bike you keep one hand on the bars: sidearms, the shotgun and melee work, two-handed weapons don't.

Guns use magazines. Spare magazines are stored and shown as pips on the weapon slot, with "clip | reserve" counts. With homing charged, the air dash is fast enough to burst small germs in one hit, and brutes can't counter it.

**Recovery.** Getting chained by attacks fills a lock meter. Once it fills, mash jump to tech out of a stagger or knockdown. After about two seconds locked, you break out with a shockwave and a short guard: you can't be grabbed, staggers are shortened, and melee damage is halved. Ragdolls end on a timer, so you can't be juggled for long.

## What's in this build

- **Heroes are medicine.** There are eight presets: gelcap, tablet, bottle, syringe, caplet, lozenge, gummy and med-mech. Each has a V-shaped heroic silhouette, its own walk and idle style, and its own motifs (a plunger, a cap, a score line). Companions use the tablet, bottle and syringe; the battle mecha is the med-mech. Enemies are all germs.
- **Customizer.** Start from a preset, then click any segment (head, chest, each limb part) and change its shape, size and colour. Drag a segment to change its thickness and length. Switch the drag to "Drag moves anchor" (or use the Side / Height / Depth sliders) to move where the head, an arm or a leg joins the body; with a controller, hold L3 and use the stick. You can add, move and resize attachments, or drag them with the mouse. On a controller, hold L3 and use the stick. A mirror toggle keeps the two sides matched. The face system is gone.
- **Armor** is eight slim plates that don't overlap and don't hide the body.
- **Weapon animations by role.** Pistols are one-handed with a snap recoil. Rifles brace on the off hand. The shotgun pumps, scoped weapons work a bolt, the launcher shoulders, the bow draws, beams jitter, and melee weapons have their own swings. Reloads use the off hand.
- **Vehicles.** The rover is a drivable, hovering red blood cell that drifts, banks and hops. The hover chair is a floating virus whose spider legs step on the ground and dangle when it climbs.
- **Loose weapons are physics props** that tumble and settle. They no longer spin in place.
- **Map decor.** Floating decor is removed; ground foliage such as villi stays. Everything you can walk into has collision: organs, the vessels and nerve tendrils where they meet the ground, synapse bulbs. Floor veins are half buried, and the crawling bacteria and hanging mucus strands are gone.
- **Buildings** are cell walls and towers, about 14–20 m across, with nerve fibres (dendrites, synapse bulbs, myelinated axon arches) pulsing between them. Every storey has its own layout: rooms split off with doorways, pillared halls, waist-high counters for cover, crates and the odd supply cache. The biggest buildings have an open atrium through every floor, ringed by railings. Interiors break apart like the outer walls.
- **AI.** Enemies and companions path around obstacles with grid A*. Companions no longer teleport: they follow your trail and roll to catch up. Enemy squads have ranks (leader, veterans, grunts) and formations. The leader gives push, hold and flank orders and hangs back. Squads break when the leader dies, and then someone is promoted.
- **Title screen.** Pre-rendered overhead views of each map pan and crossfade behind the menu; hovering over a map previews it. Flip George Lopez and he looks up a fresh fun fact online. Without a connection he uses an offline list.
- **UI scaling.** Fonts and 2D UI scale with window height, and text is rendered at 2× oversampling.

## Engine changes for Windows

- **Render resolution is a setting, not a limit.** The original drew a few hundred lines and scaled them up. That look is still the default (480p, nearest-neighbour upscale). 720p, 1080p and native are options too.
- **Uncapped frame rate.** Turn VSync off and the game renders as fast as the GPU allows; the swap chain allows tearing. The simulation still runs at a fixed 120 Hz with interpolation, so gameplay doesn't change with frame rate.
- **Bigger budgets.** Debris can go up to 800 pieces (the browser allowed 420), enemies up to 200 (was 120), and draw distance up to 400 m (was 280). There is a new "Ultra" preset.
- **No fixed-size GPU buffers.** Instanced meshes grow on demand where the browser build had to cap them up front.
- **Multithreading.** Terrain heights are generated across all cores. Worlds are built on a worker thread while the title screen stays responsive. Music and sound are mixed on their own thread, so frame hitches don't click.
- **Native audio.** The chiptune effects are synthesised exactly as before (same recipes, same seeded noise, sample-and-hold and bit-crush). The FM music chip (two- and three-operator voices, PCM drums, per-song effect racks) is rendered sample by sample into XAudio2, with no Web Audio node graphs.
- **Native input.** Raw-input mouse look is unaccelerated. Controllers go through XInput in the same "standard gamepad" layout, with rumble.
- **Windows voices.** Companions speak through SAPI, each with its own voice, pitch and rate.
- F11 or Alt+Enter toggles borderless fullscreen. F12 saves a PNG screenshot.

## Headless smoke test

`tools/Smoke` builds every map and runs a minute of play with a scripted player. It needs no window, GPU or audio device. It also renders the music and checks for NaNs and clipping. It is a quick way to catch crashes after changing the simulation:

```bash
dotnet run --project tools/Smoke -c Release -- data 30
```

It also contains a software rasteriser that mirrors the D3D11 renderer, so every screen can be rendered to PNG on any OS for visual checks:

```bash
dotnet run --project tools/Smoke -c Release -- data --shots out 1280 720
```

The modes are `interior`, `title`, `custom`, `heroes`, `lineup`, `vehicles`, `weapons`, `maps`, `cast`, `sandbox-random`, `waves-bowl`, `sandbox-leviathan` and `game`. Pass one as the last argument to render only that mode. The map stills in `data/ui/maps/` and `data/ui/cast.png` were made this way.

## Layout

```
src/GermBall/
  Program.cs, App.cs        entry point, main loop, screens
  Platform/                 Win32 window, input (raw mouse, XInput), XAudio2 mixer, SAPI speech, paths, file dialogs, screenshots
  Render/                   scene graph, three.js-compatible geometry, D3D11 renderer and shaders, software canvas, fonts, 2D UI drawing, PNG loader
  Core/                     vectors, quaternions, the seeded RNG and noise (bit-exact with the original, so seeds make the same worlds)
  Game/                     the game: one partial class World split by subsystem (layout, terrain, physics, actors, weapons, vehicles, AI, companions, effects, HUD...), GermDraw, sound, music
  UI/                       title screen, customizer, briefing/pause menus, immediate-mode GUI
data/                       music data and images, copied next to the exe
tools/Smoke/                headless smoke test
```

`PORTING.md` maps every section of the original script to its C# file and lists the remaining differences.

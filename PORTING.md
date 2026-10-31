# Porting notes (browser → native Windows C#)

Source: the published Germ Ball artifact (one HTML file, three.js r128, ~670 KB of JS).
Target: `src/GermBall` — .NET 8, Windows only (Win32 + Direct3D 11 + XInput + XAudio2 + SAPI).

## Conventions
- The original game script was one big closure of globals; it is ported as `partial class World` split by section
  (`Game/World.*.cs`). Global names keep their original spelling (`PADS`, `SPAWN`, `bodies`, `actors`...) so the two can be read side by side.
- `{x, y, z}` objects → mutable `V3` / `Q4` (reference semantics, temporaries reused in hot paths, as in the original).
- three.js scene objects → `Render.Scene` nodes (`Mesh`, `InstancedMesh`, `Points`, `Lines`), materials → `Render.Material`.
- HTML canvas textures → `Render.Canvas2D` (software rasteriser), DOM HUD/menus → immediate-mode `Render.Draw2D`.
- `localStorage` → JSON files under `Paths.Saves`; key names in bindings stay the browser `KeyboardEvent.code` names.
- Simulation step stays fixed at `DT = 1/120`.

## Status by source section (line numbers in the original HTML)
| Lines | Section | C# file | State |
|---|---|---|---|
| 442–834 | GermDraw | Game/GermDraw.cs, Game/FaceArt.cs | done |
| 841–891 | Math and noise | Core/MathUtil.cs | done |
| 892–1062 | Level layout | Game/World.Layout.cs | done |
| 1063–1161 | Spires, heightfield | Game/World.Terrain.cs | done |
| 1163–1661 | Statics, rigid bodies, SAT, joints | Game/World.Physics.cs | done |
| 1662–2288 | Actors, controller, dash, grab/throw | Game/World.Actors.cs, World.Grab.cs | done |
| 2289–2780 | Damage, debris, actor contacts, armor, ragdolls | Game/World.Damage.cs | done |
| 2781–3277 | Weapons, melee, needles | Game/World.Weapons.cs | done |
| 3278–3374 | Pickups | Game/World.Items.cs | done |
| 3375–3884 | Vehicles, dropships | Game/World.Vehicles.cs | done |
| 3885–4110 | Rendering setup, terrain mesh, structures, spores | Game/World.Landscape.cs, World.Spores.cs | done |
| 4110–4475 | Buildings, layout, props | Game/World.Buildings.cs | done |
| 4475–4935 | Flora, leviathan interior | Game/World.Leviathan.cs | done (flora skipped, see below) |
| 4935–5830 | Sky, effects, meshes | Game/World.Effects.cs, World.Draw.cs | done (sky skipped, see below) |
| 5830–7300 | Actor visuals, AI, squads, companions, bosses, mecha piloting | Game/World.Draw.cs, World.Pose.cs, World.AI.cs, World.EnemyBrain.cs, World.Companions.cs, World.Main.cs | done |
| 7300–7690 | Speech | Platform/Speech.cs (SAPI) | done |
| 7690–7790 | Sound effects | Game/Sound.cs | done (same recipes, same seeded noise) |
| 7790–8370 | Music synth, songs | Game/Music.cs + data/music/*.json | done (see below) |
| 8370–8580 | Dash reticle, prop trajectory hologram, throw arc | Game/World.Main.cs | done |
| 8580–8720 | Camera, free camera | Game/World.Main.cs | done |
| 8720–8800 | HUD | Game/World.Hud.cs | done |
| 8800–8960 | Prop stepping, gamepad, aim assist | Game/World.Main.cs | done |
| 8960–9260 | Input, menus (Controls/Settings/Keys/Debug), fixed-step loop | Game/World.Main.cs, World.Debug.cs, UI/GameScreen.cs, UI/Gui.cs | done |
| 9261–9352 | Title screen | UI/TitleScreen.cs | done |
| 9353–9609 | Customization screen | UI/CustomizeScreen.cs | done |

## Known differences from the browser build
- **Flora and the open sky** (nebula sphere, planet, the watching eye, floating isles, distant cones) are not ported. In the original
  every map passes through `organicDress`, so `LAYOUT.organic` was always true and that code never ran; the C# port follows what actually ran.
- **Touch controls** are gone (Windows desktop: mouse/keyboard and controllers).
- **Music effects** are re-implemented DSP, not Web Audio nodes: the reverb is a Freeverb-style network instead of a noise-impulse
  convolver, and the compressor is a simple peak follower. Notes, patches, drums, songs and the sequencer are the same.
- **Colour pickers** in the customizer are hue/saturation/lightness sliders instead of the browser's native colour input.
- **Speech voices** depend on the Windows voices installed; pitch/rate are mapped onto SAPI's ranges.
- New: settings for display mode, VSync, a 1080p render option and an Ultra preset; germ export/import to files; folder shortcuts.

## Verification
The port was written on Linux, where the Win32/D3D11/XAudio2 layer cannot run. `tools/Smoke` exercises everything else headlessly
(world generation for every map, a minute of scripted play with debug spawns, music rendering). The window, renderer, audio
device output, controllers and speech still need a first run on Windows.

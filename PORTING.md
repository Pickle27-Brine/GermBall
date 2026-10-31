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
| 442–834 | GermDraw | Game/GermDraw.cs | done |
| 841–891 | Math and noise | Core/MathUtil.cs | done |
| 892–1062 | Level layout | Game/World.Layout.cs | done |
| 1063–1161 | Spires, heightfield | Game/World.Terrain.cs | done |
| 1163–1661 | Statics, rigid bodies, SAT, joints | Game/World.Physics.cs | done |
| 1662–2288 | Actors, controller, dash, grab/throw | Game/World.Actors.cs, World.Grab.cs | done |
| 2289–2780 | Damage, debris, actor contacts, armor, ragdolls | Game/World.Damage.cs | done |
| 2781–3277 | Weapons, melee, needles | | todo |
| 3278–3374 | Pickups | | todo |
| 3375–3884 | Vehicles, dropships | | todo |
| 3885–4110 | Rendering setup, terrain mesh, structures | | todo |
| 4110–4475 | Buildings, layout, props | | todo |
| 4475–4935 | Flora, leviathan interior | | todo |
| 4935–5830 | Sky, effects, meshes | | todo |
| 5830–7300 | Actor visuals, AI, squads, companions | | todo |
| 7300–8300 | Speech, sfx, music synth | | todo |
| 8300–9260 | HUD, camera, main loop, menus | | todo |
| 9261–9352 | Title screen | UI/TitleScreen.cs | todo |
| 9353–9609 | Customization screen | | todo |

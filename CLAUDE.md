# Germ Ball: notes for Claude

Germ Ball 2.0 lives in `src/` (the game plus two editors). Germ Ball 1.0 is deprecated and frozen in `legacy/`. Don't change 1.0 unless the user asks.

## The editors must stay in sync (standing rule from the user)

The repository ships two editor exes next to the game: **EntityEditor.exe** and **MapEditor.exe**. Whenever a change touches anything they edit or show, update them in the same change:

- **Entity data** (`EntityDef`, bones, shapes, clips, layers, states, stats, curl style): update the Entity Editor's panels (`src/GermBall.Tools/EntityEditor/`).
- **New game states** for state layers: update `AnimStates.All`.
- **New procedural parameters** (things in `AnimContext`): expose them on the Entity Editor's Preview tab.
- **Map data**: update the Map Editor (`src/GermBall.Tools/MapEditor/`): its Add palette, properties panel, `Bounds`, `Describe`, and `Add` defaults. Map data means `MapDef` or `MapObject`, any new object type, zone kind or pickup kind, and anything `Level.Add` builds.
- **New gameplay numbers** that belong to an entity or a map object: put them in the data files, not in code, and give them a slider.
- **Default content:** if you change the built-in defaults (`DefaultEntities`, `Level.BuildDebug`), regenerate `content/` with:

  ```bash
  dotnet run --project tests/GermBall.Tests -c Release -- export-content content
  ```

  Commit the result.
- **Tests:** extend the editor tests in `tests/GermBall.Tests` (`EntityEditorWorkflow`, `MapEditorWorkflow`) to cover new controls. They drive the real UI by widget label (`GuiDriving.ClickLabel`, `TypeInto`, `PickCombo`).
- **Docs:** keep the README's editor sections current.

## Layout

- `src/GermBall.Core`: platform-free.
  - `Engine/` holds math, collision, meshes, the draw list, skeleton and ragdoll, and UI input.
  - `Content/` holds the entity and map formats, `ContentStore`, the animation pipeline (`Animation.cs`), `EntityView` and the default entities.
  - `Sim/` is the game.
- `src/GermBall.Platform`: Windows (Win32 window, input, D3D11 renderer, `EditorHost`).
- `src/GermBall`: the game exe.
- `src/GermBall.Tools`: platform-free editor logic. `Ui/` holds the immediate-mode `Gui` and `View3D`; then `EntityEditor/` and `MapEditor/`.
- `src/GermBall.EntityEditor`, `src/GermBall.MapEditor`: the editor exes (thin hosts).
- `tests/GermBall.Tests`: headless tests and a software rasteriser.
- `content/`: the default entities and maps. CI ships it next to the exes; the game loads it and the editors save into it.

## Checks before pushing

```bash
dotnet build GermBall.sln -c Release
```

```bash
dotnet run --project tests/GermBall.Tests -c Release
```

Visual checks on any OS render PNGs through the software rasteriser:

```bash
dotnet run --project tests/GermBall.Tests -c Release -- shots out
```

```bash
dotnet run --project tests/GermBall.Tests -c Release -- editor-shots out
```

The user wants exes, not build instructions. CI (`.github/workflows/windows-exe.yml`) publishes all three exes plus `content/` on every push to a "build-&lt;branch&gt;" release. When reporting work, point to that release rather than build commands.

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

The music is data, not code: `data/music/songs.json` holds the 36 songs (chord roots, drum grids, bass lines, form, effect chain) and `data/music/patches.json` holds the FM instrument patches. Edit them, or add songs, without rebuilding. The title-screen picture is `data/ui/lopez.png`.

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

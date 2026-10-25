# Developing MotorMod

## Layout

| Path | What |
|---|---|
| `src/MotorMod/` | The plugin (C#, BepInEx 5 + Jötunn). `BikeDefinition.cs` is the roster: assets, names, recipes, ride stats, rider pose defaults. |
| `src/MotorMod/Soundtrack/` | Sea soundtrack: playlist logic (`SoundtrackLogic.cs`, no game types, unit-tested), the music folder (`MusicLibrary.cs`), networking and playback (`SoundtrackManager.cs`). |
| `src/MotorMod/Harness/` | Optional in-game test harness, dormant unless the game starts with `-motorharness`. |
| `unity/MotorModUnity/` | Unity 6000.0.75f1 project that turns the prepared model, textures and sounds into one asset bundle per model and platform (the Hel Cycle, which the Wraith and Draugr Cycles share), plus one for the saddlebag. |
| `assets/models/` | Model pipeline scripts (Blender), configs, and each model's prepared FBX and layout (`<bike>/unity/`, `saddlebag/unity/`). |
| `assets/audio/` | The finished engine clips (`hel/*.wav`) and the sound pipeline script. |
| `tests/SoundtrackTests/` | Console tests for the playlist logic. |
| `tools/` | Release packaging, the macOS launcher, and the harness drivers. |

## Requirements

- Valheim with [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
  and [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) installed (the project
  compiles against the game's and Jötunn's assemblies).
- .NET SDK 8 (or newer) to build the plugin.
- Unity **6000.0.75f1**, the version Valheim 1.0 runs on, with Windows build support,
  to build the bundles. Bundles from other Unity versions do not load.
- For the asset pipelines only: Blender 5.x and Python 3.9+ with numpy, scipy, soundfile,
  noisereduce, pyloudnorm, UnityPy and Pillow.

## Build the plugin

```
dotnet build src/MotorMod -c Release
```

The project finds the game in the default Steam folder of each platform; pass
`-p:ValheimDir=/path/to/Valheim` otherwise. When the game is found, the build
copies `MotorMod.dll` and the bundles into `BepInEx/plugins/MotorMod`.

## Build the bundles

```
./unity/MotorModUnity/sync_assets.sh
Unity -batchmode -nographics -projectPath unity/MotorModUnity -executeMethod MotorModBuild.BuildAll -quit -logFile -
```

`sync_assets.sh` copies the bike's FBX and sounds, and the saddlebag's FBX,
into the Unity project (Unity keeps those copies out of git). The build writes
`osx` and `win` bundles to `src/MotorMod/Bundles/`, one named `hel` and one
named `saddlebag`; the plugin build then deploys them.
Mac players need the `osx` bundles (Metal shaders); Windows players and Linux
servers use `win`.

The Unity project must list the `com.unity.modules.*` packages in
`Packages/manifest.json`; without them Unity writes bundles the game rejects
as "not compatible with this newer version of the Unity runtime".

## Make a release

```
tools/release/package.sh
```

builds the plugin in Release and writes to `dist/`:

- `MotorMod-<version>.zip`: the `MotorMod` folder for players (DLL, all
  bundles).
- `MotorMod-<version>-server.zip`: the same without the macOS bundles.
- `MotorMod-<version>-thunderstore.zip`: the Thunderstore package (manifest,
  icon, README, changelog, plugin folder).

The version comes from `PluginVersion` in `MotorModPlugin.cs`; keep the
`<Version>` in the project file and `CHANGELOG.md` in step.

## Asset pipelines

The original model and sound recordings are not in the repository (see
`CREDITS.md` for where each comes from). To rebuild the bike's assets:

1. Download the original into the path named by `source` in
   `assets/models/configs/hel.json`.
2. `Blender --background --python assets/models/prep_bike.py -- assets/models/configs/hel.json`
   scales, splits and decimates the model and writes `assets/models/hel/unity/`
   (FBX, layout with seat, grips and headlight, textures) plus preview renders.
3. `python assets/models/make_unity_textures.py` converts the models' textures into
   the Unity project and writes `bikes.txt` and `props.txt`; `assets/models/render_icons.py`
   renders the hammer-menu icon.
4. The saddlebag: `Blender --background --python assets/models/prep_prop.py -- assets/models/configs/saddlebag.json`
   (the original is the Sketchfab model linked in `CREDITS.md`).
5. Sounds: `python assets/audio/process_audio.py` cuts, cleans and balances
   the engine clips from the recordings in `assets/audio/source/`; see
   `assets/audio/README.md`.

## Test harness (macOS)

`tools/mac/harness.sh start` launches the game with `-motorharness`: a
sandboxed session with its own save folder, a throwaway character and its own
world, in a small window that never grabs the mouse. It refuses to run outside
the sandbox, so your real characters and worlds are never touched.
`tools/harness/shoot.py` sends it requests and collects the results, for
example:

```
python tools/harness/shoot.py hel wraith --views right,front34  # standing pictures (feet down)
python tools/harness/shoot.py draugr --ride 3 --targets Greydwarf   # ride past a creature, log the hits and the trail
python tools/harness/shoot.py hel --water --wheelie              # wheelie test on open water
python tools/harness/shoot.py none --songs                       # measure and decode every song
python tools/harness/shoot.py none --fuel hel --storage hel --refund wraith --ui hel   # fuel, saddlebags, refunds, HUD screenshots
python tools/harness/shoot.py hel --water --sink                 # sink it; the whole recipe must go down loose, no bag
python tools/harness/shoot.py hel --water --seadry               # run dry at sea: the bike must be lost
```

`python tools/harness/shoot.py --help` lists every option. `tools/mac/harness.sh stop`
ends the session and restores your window settings. The scripts assume the
default macOS Steam folder.

## Tests

```
dotnet run --project tests/SoundtrackTests
```

## Conventions

- Colours: black, greys and white; brown only for dirt, rust and leather.
- Game effects are copied with `ZNetView.m_forceDisableInit` set (see
  `BikeTrail.LocalCopy`): a plain copy of a networked effect becomes a saved
  world object.
- Each bike is simulated on the machine of the player riding it; state other
  players need (speed, wheelie pitch, on water) travels in the bike's network
  record.

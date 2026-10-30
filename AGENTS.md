# AGENTS.md

Guidance for AI coding agents (and humans) working on **km2 City Builder**. Read [Readme.md](Readme.md) for the user-facing overview and the [wiki](wiki/Home.md) for the class-by-class internals; this file collects the rules, workflows and hard-won facts that aren't obvious from the code.

## What this project is

- A **city-building tool** (not a game) made with **Godot 4.7 .NET (C#, net9.0)**. It creates cities for videogames: there is no real-time simulation of cars or pedestrians, only static but interactive placement of roads, intersections, terrain patches, buildings, props, etc.
- Game support comes from **cores**: mini-projects with JSON definitions, Python (exporters, format readers, migrations) and Lua (scripted geometry). The only core so far is **Midtown Madness 2** ([km2cb-mm2-core](https://github.com/wilkovatch/km2cb-mm2-core)), installed in `Files/cores/MidtownMadness2`. BeamNG Drive is a planned target, not supported yet.
- **Core-specific guidance lives in each core's own `AGENTS.md`.** For MM2 (file formats, the three-repo exporter workflow, exporter tools and gotchas, engine limits and crash signatures), read [Files/cores/MidtownMadness2/AGENTS.md](Files/cores/MidtownMadness2/AGENTS.md) before working on the core or its exporters.
- Repository: https://github.com/wilkovatch/km2-city-builder

## Ground rules

1. **The base builder must stay generic.** Anything added to the C# program must work for any game. Game-specific behaviour goes in the core's JSON as options or expressions (type settings, float expressions over the element's variables, like `lineSettings` of `ArrayProperties` or the placement rules), not hard-coded in C#. Example: a LineRenderer mode hard-coded to MM2 decal UVs was rejected. Document new generic settings in the wiki (`Parameters-and-UI`, `Core-*` pages) and put the game-specific values in the core.
2. **Never commit original game data** (stock meshes, textures, material tables, ...) to this repo or to a core repo. The user provides these files. Read them from the gitignored `Files/common/<Core>/` folder, which mirrors the game layout (`geometry/`, `texture/`, `city/`, `jpg/`) and is created from the core's `baseCommon/`, where only `_put ... here` placeholder files are tracked. When such a file is missing, warn and fall back to a default.
3. **Documentation split:** the wiki (`wiki/`, published to the GitHub wiki by `.github/workflows/wiki.yml`) documents the program and the generic core mechanisms. Core-specific details go in the core's own README (user-facing) and `AGENTS.md` (agent notes). The wiki and README say that Claude Code was used to write them, so keep the same style when updating them.
4. **Commits:** leave changes uncommitted unless asked; the maintainer reviews and commits. Commit titles are short and descriptive (`Wireframe mode`, `Export filename fix`). Version bumps are their own commit (`Version number increase`, `<Version>` in `km2 City Builder.csproj`). A push to `main` that touches program files triggers a release build (`.github/workflows/release.yml`).
5. **Versions:** `Scripts/GlobalVariables.cs` holds `programVersion` (increase only for breaking changes, which resets `featureVersion` to 1) and `featureVersion` (increase for new features without breaking changes). Cores have their own `featureVersion`, and the city's `coreFeatureVersion` drives migrations (`migrate.py`). Don't bump these for features that haven't been pushed yet.
6. **Keep tests short.** Verify against game data with a small random sample (~10 files, fixed seed), not the whole stock set. Self-test runs should take ~30 to 60 s. Ask before launching multi-minute suites.
7. **Don't scan drives** for executables or game data. Machine-specific locations (Godot, the game install, stock data, tools) are kept in the gitignored `CLAUDE.local.md`; if it's missing, ask.
8. **Match the surrounding code:** C# braces go on the same line (`.editorconfig`), with 4-space indentation, `//` comments as dense as in the surrounding code, and lowerCamelCase public fields as in the existing classes. User-visible UI strings are keys in `Files/strings/en-US.json` (core strings in the core's `strings/en-US.json`), never literals.

## Repository layout

| Path | What |
|---|---|
| `Scripts/UI` | Menu bar, editor panels (`EditorPanel/EditorPanels`), placers and terrain modifiers, camera, debug console |
| `Scripts/CityElements/Logic` | Roads, intersections, patches, building lines, mesh instances, custom elements, city properties |
| `Scripts/CityElements/Geometry` | Mesh generators driven by the core's type definitions |
| `Scripts/CityElements/Types` | Core JSON type loading (`TypeParser`, `Parsers/TypeInheritance.cs` for `extends`) and runtime types |
| `Scripts/RuntimeCalculator` | Expression parser/evaluator, Lua scripting |
| `Scripts/IO` | Save/load, export, import, settings |
| `Scripts/History` | Undo/redo (`UndoManager`), `UndoSelfTest.cs` |
| `Scripts/Api`, `Scripts/Operations` | Scripting API (JSON commands, `CommandDispatcher`, `CityApi.*`) and the operations shared by the API and the editor; `ApiSelfTest.cs` |
| `Scripts/Cli` | Command line (`-- <command>`), see `wiki/Command-Line.md` |
| `Scripts/Mcp` | MCP server (stdio and in-editor HTTP), see `wiki/MCP-Server.md` |
| `Scripts/Rendering` | Offscreen `CityRenderer`, view toggles, selection outlines |
| `Scripts/GameEngine.cs` | The only place that talks to Godot directly, including the coordinate conversion |
| `ExpressionParser/` | ANTLR grammar. The generated C# is **not tracked**: run `compile.bat` with `antlr-4.13.2-complete.jar` after every grammar change (output goes to `CustomPackages/expressions-antlr4`) |
| `Files/cores/<Core>` | Installed cores, each its own git repo (gitignored here) |
| `Files/common/<Core>` | User-provided stock game assets (gitignored) |
| `Files/python` | Python side of the program (server, `citybuilder` scripting package) |
| `python/` | Bundled embeddable Python (gitignored, runtime) |
| `wiki/` | Documentation, published to the GitHub wiki |
| `addons/terrain_3d` | Terrain3D addon (`setupDependencies.ps1`/`.sh` restores it) |

Runtime files created in the project folder when running from the editor (gitignored): `settings.json` (program settings: `LastCity`, Python, MCP settings; **not** in app_userdata), `pythonLog.txt` (Python errors), `python/`.

### Coordinates

The program logic, the cores and the exported files are **left-handed, Y up, +Z forward, +X right**. Conversion to Godot's right-handed system happens only in `GameEngine`. Conversion to a game's own system (e.g. the MM2 X mirror) is done by the core's exporters. Raw Godot calls such as `DirectSpaceState.IntersectRay` need Godot coordinates (z flipped), so use `GameEngine.Raycast.DoRaycast` instead.

## Exporters

Exporters are Python scripts in the cores; changes to them usually touch the core repo too (see the core's `AGENTS.md`). `-- export-json <city> --out x.json` dumps the city in the exporter input format, to run a core exporter directly without the editor.

- Without a console, `PythonManager.RunScriptTask` feeds newlines on stdin (exporters may end with `input()`).
- A Python traceback goes to stderr, which neither `pythonLog.txt` nor the API `output` captures.
- Reported errors fail any export. Validation (`city.validate`, the `validate` CLI command) is a dry export driven by the exporter `settings.json` `validation.flags`.

## Building and running

- Godot binary: Godot 4.7.1 .NET (`Godot_v4.7.1-stable_mono_win64_console.exe` on Windows; use the `_console` exe for command-line work). The install path is in `CLAUDE.local.md`.
- Build: `dotnet build "km2 City Builder.csproj"`. The build output is localized in Italian ("Compilazione completata"), so grep for `error CS` instead of "Build succeeded".
- **New `.cs` files need their `.cs.uid`** (tracked in git). Generate it with `<godot> --headless --path . --import`.
- Command line: `<godot> [--headless] --path . --quiet -- <command> ...` (see `wiki/Command-Line.md`).
  - Any command after `--` skips the Python check and the LastCity auto-load (`Cli.CommandLine.IsCliSession`).
  - `--path` makes Godot chdir into the project, so relative paths resolve against the project folder. Clean up leftovers.
  - With `--headless --quiet`, stdout carries only the command output (`GameEngine.Print` goes to stderr in CLI sessions).
  - Bool options are bare flags (`--wireframe`, not `--wireframe true`).
  - `render`, `mcp`, `screenshots` and windowed self-tests **need a window** (no renderer with `--headless`: `view.render` fails with `no_renderer`). Keep `--quiet`. `CliRunner.HideWindow` moves the window off-screen.
  - Git Bash quirk: piping the Godot console exe into a native Windows program (`| python ...`) gives it an empty stdin. Use `| cat`, a file, or `cmd /c "... | python ..."`.
  - PowerShell 5.1 strips inner double quotes of native args: escape as `\"` or use `@file`.
- Headless startup logs a pre-existing `ArgumentException` from `DebugLogPopup.UpdatePosition` (zero-size viewport). It is not a failure.
- The bundled Python needs the core requirements (e.g. Pillow for the hudmap). CLI sessions skip the Python check, so install them by hand: `python/python.exe -m pip install -r Files/cores/<Core>/pythonInfo/requirements.txt`.
- Back up `settings.json` before tests that may touch `LastCity` or other settings.

## Self-tests (the test suite)

All self-tests write a log **next to** the given folder (not inside it) and exit 1 on failures. Use folders in a scratch directory and delete a previous run's folder first (`city.new` refuses an existing city).

### `selftest api <new folder>` (`Scripts/Api/ApiSelfTest.cs`)

Creates an MM2 SF city and drives it only through JSON API commands. It covers elements, anchors, terrain, undo/redo, transactions, generators, Python/Lua scripting, type inheritance, merge/split, seamless intersections, decals, export layout/hudmap, `.ar` packing, rendering, validation, MCP and custom elements. Writes `api_selftest.log`.

```
<godot console exe> --path . --resolution 1152x648 -- selftest api "<scratch>/api/city"     # windowed (also saves screenshots)
<godot console exe> --headless --path . -- selftest api "<scratch>/api/city"               # headless (skips the view checks)
```

**Baseline (2026-10-29): 783 OK windowed / 768 headless, 0 failures, ~75 s windowed.** Known and accepted: the full `city.export` reports an `export_failed` error on "Intersection 1" (PSDL perimeter infinite loop at the 3-road origin intersection). The test checks that the error names it and that PSDL/INST/BAI are still written.

- `InheritedTypes` temporarily creates `Files/cores/MidtownMadness2/elements/roads/zzInheritTest`. If a run is killed, delete that folder by hand.
- A run with failures has exited with 132 (SIGILL) during Godot's shutdown finalizers. Clean runs exit 0.
- A run that stops without an error (no log, the window idle) is an API reply that never comes: `SettleProjectedSplines` (awaited by the dispatcher after each command while projected splines exist) waits until an update rebuilds no road, intersection or patch, so an element reporting a change at every update stalls it (e.g. a border mesh state never flagged unchanged). Run without `--quiet` to see the `[api-selftest]` lines up to the stuck step.
- Empty ObjectState arrays vanish on JSON re-read, so compare `[]` and missing as equal.
- `city.exportJson` ids are indices in the manager lists, not API ids. Ids change after a reload, so re-list (or compare by name).
- An API `Call` flushes and resets the elements' editing state, and a change closes the open editors (as before an undo). To test a GUI action with undo, run the GUI steps first, then `city.flush`, then take the snapshots (`CheckGuiUndo` in `BaiConnections`), not `CheckUndo` around them.
- The custom element panel gets its element from the click mode (`GetPlacer()` is `terrainClick.modifier as CustomElementPlacer`): a temporary click mode (e.g. `ReferencePicker`) must restore the previous one before changing the element. While the point transform panel (M) is open the modifier is null; `CityBuilderMenuBar.ControlPointOwnerCustomElement` gives the element.
- Every menu entry and panel button runs inside `UndoManager.Record` (`IsBusy` is true), so a `RunLong` API call made synchronously from a GUI action fails with "another operation is running". GUI code must yield a frame first, and tests of GUI actions should go through `UndoManager.Record` like the menu does.
- Panel checks: call `panel.Update()`, not `placer.onFrame` (which returns early when nothing changed).
- Read core settings like `allowYaw`/`allowRoll` from the type instead of hardcoding them; the maintainer toggles them locally.

### `selftest undo <folder> [--mode new|props|ui|split|load|drag|dropdown]` (`Scripts/History/UndoSelfTest.cs`)

Writes `undo_selftest.log` and restores `LastCity`.

| Mode | City | Notes / baseline |
|---|---|---|
| `new` (default) | creates one | **35 pre-existing failures** (split/merge steps of straight Hermite and low-poly roads), not a regression |
| `split` | creates one | 0 failures, ~50 s. **Must run windowed.** `SplitModeTest` is flaky on window geometry, so rerun once before blaming a change |
| `drag` | creates one | numeric field middle-drag, 0 failures, ~15 s. Captures the real mouse, so a moving mouse can fail the Shift check; rerun once |
| `dropdown` | creates one | dropdown keys/wheel, 0 failures, ~10 s |
| `ui`, `load`, `props` | **a copy** of an existing city | the old test city is gone; use a copy of the api self-test city (`<scratch>/api/city`) or ask the maintainer for one |

Load/ui modes catch bugs that the scratch city misses. Reproduce user-reported bugs on a copy of a real city.

Simulating input in self-tests:
- `Input.ActionPress` does **not** work for clicks (the await continuations run after `_Process`), and `InputEventAction` counts as consumed by the UI. Use `WarpMouse` + `Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = Left, Pressed = true, Position/GlobalPosition = mouse })`, then release.
- Terrain modifiers need `Input.ActionPress("Select")` to apply.
- Panel buttons: reflection on `EditorPanelElements.Button`'s private `button`, then `EmitSignal(Pressed)` (goes through the undo recording). Reflection doesn't find a base class's private fields through the derived type, so use `typeof(Base).GetField`.
- Panels may be rebuilt when an element is selected: read panel fields after `SelectObject`.
- A test that deletes elements must call `M.FlagAsChanged()` like the editor does.
- Compare positions with a tolerance (projection drift).
- Editor key commands are polled globally (`GameEngine.Input`). Block them while a popup is open (`UIDetection.PopupOpen()`).

### Other

- `-- screenshots <copy of a city>` (`Scripts/UI/GuideScreenshots.cs`) regenerates the quickstart guide images and **overwrites the tracked `wiki/images`**. Revert them if they were only for a visual check.
- Bugs that only show in the normal GUI startup (LastCity auto-load) can't be seen by the self-tests, because CLI loads reset the core manager. Launch the GUI normally and grep the log.
- Visual check of a change: `-- render <city> --out x.png --camera top|custom ...` (windowed). Thin lines follow the editor camera distance, so set it with `view.camera.set` before `view.render`.

## Program internals worth knowing

- **Top-level nodes:** `GetParentNode3D()` returns null for a `TopLevel` node (`LineRenderer`, `RoadSplitLine`), so walk up with `GetParent() as Node3D`.
- **Update loop:** the UI never regenerates elements directly. Edits change state and mark dirty, and `ElementManager` rebuilds once per frame after every other node.
- **Renderer** (`Scripts/Rendering/CityRenderer`): a SubViewport sharing the main World3D. Render layer bit 17 (`GameEngine.Camera.RenderOnlyLayer`) is hidden from the editor camera, and bit 18 is the gizmo. During a render it forces the best LOD, suspends cull distances and points Terrain3D at the render camera.
- **MCP:** the HTTP server is hand-written on a loopback `TcpListener` (HttpListener would need a URL ACL), default port 7810, Bearer token, settings `mcpServerEnabled/Port/Token`. stdio `mcp` handles messages sequentially. `script_run` must not go through the dispatcher (deadlock), so it uses `ApiHost.RunOnMainThread`.
- **Custom elements** (generic element types defined by the core, not necessarily low-level representations: e.g. the MM2 PSDL attributes, but also its decals and trains): core folder `elements/customElements/<type>/`, save key `customElements`, API kind `customElement`, preset/reference/converter prefix `customElement/<type>`. They used to be called "low-level elements": cities saved before the rename (`lowLevelElements`, `lowLevel` references, `lowLevel/<type>` preset categories) are still read. Don't confuse them with the `customCityProperties` parameter type (`customCityPropertiesType`, types in `elements/customCityPropertiesTypes/`), the nested parameter groups of the city properties (formerly `customElement`/`customTypes`). Converted roads/intersections become hidden `ghost`s whose references resolve to their replacements at export. The generic mechanism is in `wiki/Core-Custom-Elements.md`, the core specifics (and the MM2 export-equivalence check) in the core's README and `AGENTS.md`.
- **Core view toggles:** core `settings.json` `viewToggles`, stored in the city `preferences.json` as `viewToggle.<name>`. Core settings must not be read before a city is loaded (that once cached the wrong settings and broke `.tex` decoding).
- **Terrain3D** snaps heightmap imports to its region grid; the region size is `min(256, resolution/2)`.
- Forward+ wireframe works without `RenderingServer.SetDebugGenerateWireframes` (needed only on the Compatibility renderer).
- Graphics settings (`renderingMode`, `shadowsEnabled`) are per city in `preferences.json`.

## Game-specific reference

Reverse-engineered game facts (engine limits, crash signatures, file format details) are in each core's `AGENTS.md`. For MM2: [Files/cores/MidtownMadness2/AGENTS.md](Files/cores/MidtownMadness2/AGENTS.md).

# AGENTS.md

Guidance for AI coding agents (and humans) working on **km2 City Builder**.
Read [Readme.md](Readme.md) for the user-facing overview and the [wiki](wiki/Home.md) for the class-by-class internals;
this entry file contains the essential rules and the reading index for detailed agent guidance.

## What this project is

- A **city-building tool** (not a game) made with **Godot 4.7 .NET (C#, net9.0)**. It creates cities for videogames: there is no real-time simulation of cars or pedestrians, only static but interactive placement of roads, intersections, terrain patches, buildings, props, etc.
- Game support comes from **cores**: mini-projects with JSON definitions, Python (exporters, format readers, migrations) and Lua (scripted geometry). The main core is **Midtown Madness 2** ([km2cb-mm2-core](https://github.com/wilkovatch/km2cb-mm2-core)), installed in `Files/cores/MidtownMadness2`. A **Midtown Madness 1** core is in development in `Files/cores/MidtownMadness1` (its own repository, not published yet). BeamNG Drive is a planned target, not supported yet.
- **Core-specific guidance lives in each core's own `AGENTS.md`.** For MM2 (file formats, the exporter workflow, exporter tools and gotchas, engine limits and crash signatures), read [Files/cores/MidtownMadness2/AGENTS.md](Files/cores/MidtownMadness2/AGENTS.md) before working on the core or its exporters.
- Repository: https://github.com/wilkovatch/km2-city-builder

## Ground rules

1. **The base builder must stay generic.** Anything added to the C# program must work for any game.
   Game-specific behaviour goes in the core's JSON as options or expressions (type settings, float expressions over the element's variables, like `lineSettings` of `ArrayProperties` or the placement rules), not hard-coded in C#.
   Example: a LineRenderer mode hard-coded to MM2 decal UVs was rejected. Document new generic settings in the wiki (`Parameters-and-UI`, `Core-*` pages) and put the game-specific values in the core.
2. **Never commit original game data** (stock meshes, textures, material tables, ...) to this repo or to a core repo. The user provides these files.
   Read them from the gitignored `Files/common/<Core>/` folder, which mirrors the game layout (`geometry/`, `texture/`, `city/`, `jpg/`) and is created from the core's `baseCommon/`, where only `_put ... here` placeholder files are tracked.
   When such a file is missing, warn and fall back to a default.
3. **Documentation split:** the wiki (`wiki/`, published to the GitHub wiki by `.github/workflows/wiki.yml`) documents the program and the generic core mechanisms.
   Core-specific details go in the core's own README (user-facing) and `AGENTS.md` and its linked `docs/agents/` topics (agent notes).
   The wiki and README say that Claude Code was used to write them, so keep the same style when updating them.
4. **Commits:** leave changes uncommitted unless asked; the maintainer reviews and commits. Commit titles are short and descriptive (`Wireframe mode`, `Export filename fix`).
   Version bumps are their own commit (`Version number increase`, `<Version>` in `km2 City Builder.csproj`).
   A push to `main` that touches program files triggers a release build (`.github/workflows/release.yml`).
5. **Versions:** `Scripts/GlobalVariables.cs` holds `programVersion` (increase only for breaking changes, which resets `featureVersion` to 1) and `featureVersion` (increase for new features without breaking changes).
   Cores have their own `featureVersion`, and the city's `coreFeatureVersion` drives migrations (`migrate.py`). Don't bump these for features that haven't been pushed yet.
6. **Keep tests short.** Verify against game data with a small random sample (~10 files, fixed seed), not the whole stock set.
   Self-test runs should take ~30 to 60 s. Ask before launching multi-minute suites.
7. **Don't scan drives** for executables or game data. Machine-specific locations (Godot, the game install, stock data, tools) are kept in the gitignored `CLAUDE.local.md`; if it's missing, ask.
8. **Match the surrounding code:** C# braces go on the same line (`.editorconfig`), with 4-space indentation, `//` comments as dense as in the surrounding code, and lowerCamelCase public fields as in the existing classes. User-visible UI strings are keys in `Files/strings/en-US.json` (core strings in the core's `strings/en-US.json`), never literals.

## Coordinates

The program logic, the cores and the exported files are **left-handed, Y up, +Z forward, +X right**. Conversion to Godot's right-handed system happens only in `GameEngine`. Conversion to a game's own system (e.g. the MM2 X mirror) is done by the core's exporters. Raw Godot calls such as `DirectSpaceState.IntersectRay` need Godot coordinates (z flipped), so use `GameEngine.Raycast.DoRaycast` instead.

## Read before working

Read every topic relevant to the task before changing code or running its workflows. Tasks spanning several areas require all of their relevant topics.

| Before working on... | Read |
|---|---|
| Program structure, element/state behavior, UI lifecycle, coordinates, rendering or core integration | [Architecture and program internals](docs/agents/architecture.md) |
| Builds, new C# files, command-line execution, Python setup or exporter integration | [Building, running and exporting](docs/agents/development.md) |
| Tests, bug reproduction, undo/API checks, input simulation or visual verification | [Testing and visual verification](docs/agents/testing.md) |
| MM2 core definitions, scripts, migrations, formats or exporters | [MM2 core instructions](Files/cores/MidtownMadness2/AGENTS.md), then its relevant topics |
| MM1 core definitions, formats or exporters | [MM1 core instructions](Files/cores/MidtownMadness1/AGENTS.md), then its relevant topics |

Paths and command examples in these topics are relative to the builder root unless stated otherwise. Machine-specific locations remain in the gitignored local instruction files.

## Maintaining these instructions

Keep this entry file short: project identity, mandatory rules, coordinates and the reading index belong here. Put detailed workflows, test baselines and technical facts in the appropriate `docs/agents/` topic. Update the index when adding a topic, and keep one authoritative home for each detail.

Both Codex and Claude use this index. `CLAUDE.md` imports only `AGENTS.md`; read the linked topics on demand rather than importing every topic at startup. Keep local instruction files gitignored.

## Game-specific reference

Reverse-engineered game facts (engine limits, crash signatures, file format details) are in the topics linked from each core's `AGENTS.md`. For MM2: [Files/cores/MidtownMadness2/AGENTS.md](Files/cores/MidtownMadness2/AGENTS.md).

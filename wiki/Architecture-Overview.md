# Architecture Overview

This page describes how the program is put together: the scene, the startup sequence, how a change made in the UI turns into new geometry, and where files live on disk. The individual classes are described in the class reference pages linked from [Home](Home).

## Layers

The code (all under `Scripts/`) is split into a few layers. Each layer only depends on the ones listed below it.

| Layer | Folder | Purpose |
|---|---|---|
| User interface | `Scripts/UI` | Menu bar, editor panels, mouse tools (placers, terrain modifiers), camera, debug console. |
| City elements | `Scripts/CityElements/Logic`, `ElementManager.cs`, `CityGroundHelper.cs` | The objects a city is made of (roads, intersections, terrain patches, building lines, mesh instances, city properties) and their editable state. |
| Geometry | `Scripts/CityElements/Geometry`, `GeometryHelper.cs` | Mesh generation for each element type, driven by the core's type definitions. |
| Type system | `Scripts/CityElements/Types` | Loading the core's JSON element definitions and compiling their expressions. |
| Runtime calculator | `Scripts/RuntimeCalculator` | Parser and evaluator for the expressions used in the core definitions. |
| Input/output | `Scripts/IO` | City save/load, export, mesh and texture import, materials, settings files. |
| Engine abstraction | `Scripts/GameEngine.cs`, `Scripts/LODGroup.cs` | Wraps every Godot specific call, so that the layers above are (mostly) engine agnostic. |

Python (`Files/python`, plus the Python code inside each core) is used for everything that is game specific and not expressible in JSON: reading game-specific mesh/texture formats, exporting, and migrating old cities. See [Core Python Integration](Core-Python-Integration).

## Building from source

The C# expression parser in `CustomPackages/expressions-antlr4` is generated from the grammar in `ExpressionParser/` and is not tracked by git. Generate it once after cloning, before opening the project in Godot, as described in [Runtime Calculator](Runtime-Calculator#parsing). The project then builds from the Godot editor (Godot 4.7, .NET version) or with `dotnet build`.

Exporting also generates `Quickstart guide.pdf` from the [Quickstart Guide](Quickstart-Guide) wiki page, which requires [pandoc](https://pandoc.org) (3.1.2 or later) and [Typst](https://typst.app) to be in the `PATH`. The guide uses the Calibri font, with the metrically identical [Carlito](https://fonts.google.com/specimen/Carlito) as a fallback where Calibri is not installed (e.g. on Linux, where it is usually available as the `fonts-crosextra-carlito` package). Without pandoc and Typst the export still completes, with a warning, but the build will not contain the guide.

There are export presets for Windows and Linux (x86_64), and either can be exported from either system once the Godot .NET export templates for that platform are installed. The Linux build leaves out the Windows-only Python installer scripts, because it downloads its Python itself the first time Python is needed.

## Scene tree and startup

The main scene is `CityBuilder.tscn`:

```
Root (Node3D)
├── Main Camera (Camera3D, CameraController)
├── DirectionalLight3D
├── WorldEnvironment
├── CityGroundHelper (StaticBody3D, CityGroundHelper)
│   └── Element Manager (ElementManager)          created at runtime
│       ├── Roads, TerrainPatches, TerrainPoints, Meshes, Buildings   (element containers)
│       ├── Dummies          hidden elements used when editing presets
│       └── ActiveDummies    the CityProperties object
└── CanvasLayer
    └── Editor (Control, CityBuilderMenuBar)
        ├── MenuBar (Control, MenuBar)
        └── TerrainClick (Control, TerrainClick)
```

Three autoloads are registered in `project.godot`: `IngameDebugConsole` (`DebugLogManager`), `TextInputFocus` and `UIInputTracker`.

Startup sequence:

1. `CityGroundHelper._Ready` finds the `Editor` node (`CityBuilderMenuBar`), loads the terrain materials, creates the `ElementManager` node and the progress bar, then calls `CityBuilderMenuBar.ManualStart`.
2. `ManualStart` sets the invariant culture, registers the key bindings (`GameEngine.Input.ReloadKeys`), creates the file browser, transform gizmo, placers and terrain tools, fills the menu bar and initializes every editor panel.
3. `PythonManager.CheckPython` verifies that a usable Python interpreter is available and offers to install it otherwise.
4. If the setting `LastCity` points to an existing city, it is loaded with `CityImporter.LoadCity`.

## Loading a city

A city is a folder (the *working directory*, `PreferencesManager.workingDirectory`) that contains at least `city.json.gz`, `preferences.json` and `presets.json`. Loading (`CityDecoder.JSON`) does the following:

1. Reads `preferences.json` and checks the core it refers to and the versions it was saved with. If needed, it runs the core's migration scripts through `Files/python/migrate.py` (after asking the user).
2. Checks that the Python modules required by the core are installed (`PythonManager.CheckPythonForCore`).
3. Loads the preferences fully: string tables, texture formats, Python server (restarted for the current core) and the per-core common folder.
4. Recreates every element from `city.json.gz` and flags the world as changed, so that everything is generated on the next frame.

## The update loop

Elements are never regenerated directly by the UI. Every edit changes the element's `ObjectState` (which marks it dirty) and calls `CityBuilderMenuBar.NotifyChange`, which sets `ElementManager.worldChanged`. `ElementManager` runs its update after every other node (`ProcessPriority = 1000`) and, when the world changed:

1. Removes deleted elements from its lists.
2. Propagates the injectable city properties to the roads (see [Core City Properties](Core-City-Properties)).
3. Updates the road curves, lets intersections reattach and resize, then updates the curves again (an intersection may have moved).
4. Rebuilds the meshes of roads, intersections, terrain patches and building lines whose inputs changed (each element compares its current inputs with the ones of the previous build).
5. Updates mesh instances and repositions the parametric objects of roads and of the city properties.
6. Notifies the menu bar (`NotifyUpdateCompleted`) and runs the garbage collector at most every 10 seconds.

## From core files to geometry

```
core JSON files ──TypeParser──▶ Types.* (plain deserialized classes)
                                   │
                                   ▼
                     Runtime.* types (expressions compiled by the RuntimeCalculator,
                                      one VariableContainer per type)
                                   │
 element ObjectState ──────────────┤
 (user parameters)                 ▼
                     generators (RoadGenerator, IntersectionGenerator, ...)
                     fill the variables, evaluate the expressions,
                     build vertices / UVs / submeshes
                                   │
                                   ▼
                     GameEngine.Mesh (Godot ArrayMesh + collider)
```

* `CityElements.Types.Parsers.TypeParser` reads the `elements` folder of the current core once and caches the result (see [Type System](Type-System)).
* Each runtime type compiles all its expressions when it is created and owns a `RuntimeCalculator.VariableContainer` with one slot per variable. Generators clone that container and fill it with the element's parameters and with geometric values (positions, directions, lengths), then evaluate the definitions and the mesh expressions.
* The resulting meshes are plain `ArrayMesh` surfaces, one per texture, with a concave collision shape used by the editor raycasts.

## Coordinate convention

The program logic, the core definitions and the exported files all use the program's **coordinate system**: left handed, Y up, +Z forward, +X right. Godot is right handed with -Z forward, so `GameEngine` converts at the boundary: positions and directions have their Z negated (`GameEngine.Vector3.EncodePosition`/`DecodePosition`), rotations have X and Y negated, and `GameEngine.Vector3.forward` is `(0, 0, 1)`. Any value read through `GameEngine.GameObject` (positions, rotations) is already in program coordinates.

For core authors this means that everything a core sees (expression variables, exported JSON, meshes returned by the Python mesh readers) is in program coordinates.

## Layers (collision and render)

Elements are put on numbered layers, used to choose what each mouse tool can hit. Layer *n* is Godot physics layer bit *n* and render layer *n* (`GameEngine.GameObject.SetLayer`).

| Layer | Used for |
|---|---|
| 0 | Default |
| 3 | Terrain (heightmap) |
| 7 | Generated road, intersection and terrain patch meshes |
| 8 | Movable handles (road points, selected objects) |
| 9 | Road end points that can become intersections |
| 10 | Terrain points and anchors |
| 11 | Mesh instances not flagged as props (MM2 core) |
| 12 | Point links (segments between terrain points) |
| 13 | Props |
| 14 | Lines along the roads, to connect another road to their middle |

Layers can be referenced by cores in the mesh instance placement rules (see [Core Props and Objects](Core-Props-and-Objects)).

## Folders at runtime

The base folder is the project folder when running from the Godot editor, and the executable's folder in an exported build (`PathHelper.BasePath`).

```
<base>/
├── settings.json          global settings (python path/port, extra folders, last city, ...)
├── coreRepos.json         list of known core repositories, shown in the core manager
├── pythonInstaller/       scripts to install the embedded Python and the base modules
├── python/                Python interpreter used by the program (embedded build on Windows, portable build on Linux, installed on demand)
├── pythonLog.txt          output of the Python scripts
└── Files/
    ├── strings/           base string tables (en-US.json)
    ├── python/            server.py, migrate.py, MigrationHelper.py, DecodeExportedJson.py
    ├── cores/<Core>/      installed cores (git clones)
    ├── common/<Core>/     per-core shared assets (created from the core's baseCommon folder)
    └── custom/templates/<Core>/   user-made city templates
```

A city folder contains `city.json.gz` (the saved city), `preferences.json` (core, core version, extra asset folders, overlay texture, ...) and `presets.json` (the presets of every element type), plus any asset the user puts there. Asset paths stored in a city (textures, meshes) are relative: they are looked up first in the city folder, then in the *extra folders* listed in the settings and preferences (see `PathHelper.FindInFolders` in [Settings, Paths and Python](Settings-Paths-and-Python)).

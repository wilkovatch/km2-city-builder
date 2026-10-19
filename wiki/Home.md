# km2 City Builder

km2 City Builder is a tool to create custom cities for videogames. It is an editor, not a game: roads, intersections, terrain, buildings and props are placed and edited interactively, and the result is exported in the format of the target game. No traffic or pedestrians are simulated.

The editor knows nothing about any game by itself. Everything game specific (which elements exist, what parameters they have, how their geometry is generated, which file formats can be read and how the city is exported) is provided by a **core**: a folder of JSON files and Python scripts. The only official core at the moment is the [Midtown Madness 2 core](https://github.com/wilkovatch/km2cb-mm2-core), which is also the reference used throughout this wiki.

This version of the program is written in C# on Godot 4.7 (.NET). Engine specific calls go through a single abstraction layer (see [Engine Abstraction](Engine-Abstraction)).

## Where to start

| If you want to... | Read |
|---|---|
| Learn how to use the editor | [Quickstart Guide](Quickstart-Guide) |
| Understand how the program is put together | [Architecture Overview](Architecture-Overview) |
| Find what a given class does | The **Class reference** pages below |
| Add support for a new game | [Creating a Core](Creating-a-Core), then the **Core reference** pages |
| Load, edit or export cities from scripts | [Scripting API](Scripting-API) |
| Export, generate or edit cities from a terminal or another program | [Command Line](Command-Line) |
| Write geometry formulas for a core | [Expression Language](Expression-Language) |

## Class reference (program internals)

1. [Architecture Overview](Architecture-Overview): scene tree, startup, update loop, data flow, coordinate conventions, folders.
2. [Engine Abstraction](Engine-Abstraction): `GameEngine`, `LODGroup`, `PostprocessBuild`, globals.
3. [Settings, Paths and Python](Settings-Paths-and-Python): `CoreManager`, `PreferencesManager`, `SettingsManager`, `PathHelper`, `StringManager`, `PythonManager`.
4. [Type System](Type-System): JSON type definitions, `TypeParser`, runtime types.
5. [Runtime Calculator](Runtime-Calculator): the expression parser and evaluator, and the Lua scripts host.
6. [City Elements](City-Elements): roads, intersections, terrain patches, buildings, mesh instances, city properties, state and presets.
7. [Geometry Generation](Geometry-Generation): the generators that turn types and state into meshes.
8. [Input and Output](Input-and-Output): saving, loading, exporting, mesh/texture import, materials.
9. [User Interface](User-Interface): menu bar, editor panels, placers and terrain tools, camera, debug console.
10. [Scripting API](Scripting-API): the operations layer, the API and its JSON commands; the [Command Line](Command-Line) on top of it.
11. [Third-Party Code](Third-Party-Code): bundled libraries and addons.

## Core reference (creating cores)

1. [Creating a Core](Creating-a-Core): step-by-step guide and checklist.
2. [Core Structure](Core-Structure): every file and folder of a core, versions and installation.
3. [Expression Language](Expression-Language): syntax, functions, arrays, definitions.
4. [Parameters and UI](Parameters-and-UI): `parameters.json`, `ui.json`, parameter types, strings.
5. [Core Roads](Core-Roads): road types, components, mesh description, lanes, props lines.
6. [Core Intersections](Core-Intersections): intersection types, junctions, extra terrain pieces.
7. [Core Terrain Patches](Core-Terrain-Patches): terrain patch types and border meshes.
8. [Core Buildings](Core-Buildings): building types and building blocks.
9. [Core Props and Objects](Core-Props-and-Objects): prop elements and containers, mesh instances, parametric objects.
10. [Core City Properties](Core-City-Properties): city-wide parameters and custom types.
11. [Core Lua Scripts](Core-Lua-Scripts): definitions computed by Lua functions (variables, context, vectors, sandbox).
12. [Core Python Integration](Core-Python-Integration): custom mesh/texture formats, exporters, export JSON format, migrations.
13. [Core Templates and Presets](Core-Templates-and-Presets): city templates, presets, preferences.

# km2 City Builder

km2 City Builder is a tool to create custom cities for videogames. Roads, intersections, terrain, buildings and props are placed and edited interactively, and the result is exported in the format of the target game.

The editor knows nothing about any game by itself. Everything game specific (which elements exist, what parameters they have, how their geometry is generated, which file formats can be read and how the city is exported) is provided by a **core**: a plugin containing JSON configuration files and Python scripts. The only available core at the moment is the [Midtown Madness 2 core](https://github.com/wilkovatch/km2cb-mm2-core).

This program is made with Godot 4.7 (.NET), with scripts written in C#.

> [!NOTE]
> Claude Code was used during the development of this project and for writing its documentation, including this README and the wiki.

## Documentation

The full documentation is in the [wiki](wiki/Home.md):

- [Quickstart Guide](wiki/Quickstart-Guide.md): how to use the editor (also shipped as `Quickstart guide.pdf` with the builds).
- [Architecture Overview](wiki/Architecture-Overview.md) and the other *Program internals* pages: how the program is put together, class by class.
- [Creating a Core](wiki/Creating-a-Core.md) and the other *Creating cores* pages: how to add support for a new game.

## Quickstart

### Setup

- **Python 3** is required. The city builder can manage its own Python installation, in which case the packages required by the cores are installed automatically. To use your own installation instead, set its path in *Settings→Preferences→Python* and install the packages required by the cores yourself.
- **Cores**: on first launch, go to *Settings→Preferences→Cores*, select one of the listed cores and click *Install*. Other cores can be added with *Add from repository* and their Git URL. Cores can only be managed while no city is loaded.
- **Assets**: textures and meshes are looked up in the city folder and in the paths added in *Edit→Project settings→Paths* (global paths for all cities, local paths for the current one). The stock assets of a game usually go in `Files/common/<core name>`. Supported formats are .bmp, .jpg, .png, .tga and .dds for textures and .obj for meshes; cores can add more (e.g. the native formats of the game) via Python.

### Typical workflow

1. **Create a city** with *File→New city*: choose the heightmap resolution, terrain size and maximum height (4096 for resolution and size is good for an average city), the core and a template, then select an empty folder. Optionally import a heightmap (*Edit→Load heightmap*) and a ground overlay image (e.g. a map of a real city) to use as a guide.
2. **Place the roads** (*Create→Road*, Ctrl+R). Intersections are created automatically when the end of a road is placed on the end of another.
3. **Fill the space between the roads with terrain patches** (*Create→Terrain patch*, Ctrl+T), attaching them to the anchor points of the roads, or use *Create→Whole city terrain* to do it in one step. Try to finish the road network first: changing its topology afterwards can disconnect the terrain patches from it.
4. **Add the buildings**, either procedurally generated with building lines (*Create→Building line*, Ctrl+E, or *Create→Whole city buildings*) or as pre-made meshes (*Create→Mesh instance*).
5. **Add props**, by hand (one at a time or mass-placed in a radius) or with prop rules along the roads, and set the city-wide properties (*Edit→City properties*).
6. **Save** (Ctrl+S) and **export** the city for the game (*File→Export city*).

Everything about a city is stored in its folder (`city.json.gz`, `presets.json`, `preferences.json` and the assets placed there), so copying the folder is enough to back it up.

### Controls

| Action | Mouse / key |
|---|---|
| Pan / rotate / zoom the camera | Middle drag / right drag / scroll wheel |
| Select an object | Click on it (Ctrl-click to click through meshes) |
| Deselect / delete | Escape / Delete |
| Move / rotate / scale a mesh | W / E / R (hold Shift for incremental steps) |
| Enter a position by hand | M |
| Undo / redo | Ctrl+Z / Ctrl+Y |

Every object type has **presets** that store a set of property values for reuse; objects can be linked to a preset and synced when the preset changes. Advanced tools include rotating the whole city and *Edit→Batch update*, which changes properties of many objects at once with simple `property=value` and `where` expressions.

Errors are reported in the debug console (click the message counters in the bottom-left corner); Python errors are saved in `pythonLog.txt`. See the [Quickstart Guide](wiki/Quickstart-Guide.md) for the details of every tool.

## Architecture

The code, all under `Scripts/`, is split into layers, each depending only on the ones below it:

| Layer | Folder | Purpose |
|---|---|---|
| User interface | `Scripts/UI` | Menu bar, editor panels, mouse tools (placers, terrain modifiers), camera, debug console. |
| City elements | `Scripts/CityElements/Logic` | Roads, intersections, terrain patches, building lines, mesh instances, city properties and their editable state. |
| Geometry | `Scripts/CityElements/Geometry` | Mesh generation for each element type, driven by the core's type definitions. |
| Type system | `Scripts/CityElements/Types` | Loading the core's JSON element definitions and compiling their expressions. |
| Runtime calculator | `Scripts/RuntimeCalculator` | Parser and evaluator for the expressions used in the core definitions. |
| Input/output | `Scripts/IO` | City save/load, export, mesh and texture import, materials, settings files. |
| Engine abstraction | `Scripts/GameEngine.cs` | Wraps every Godot specific call, so that the layers above are (mostly) engine agnostic. |

Python (`Files/python` plus the Python code of each core) handles what is game specific and not expressible in JSON: game-specific mesh and texture formats, exporting, and migrating old cities. It runs in a local Python server started by the program.

**From core files to geometry.** The core's JSON definitions are read once by the `TypeParser` and turned into runtime types, whose expressions are compiled by the runtime calculator. Generators (`RoadGenerator`, `IntersectionGenerator`, ...) fill the expression variables with the element's parameters and geometric values, evaluate them and build vertices, UVs and submeshes, which end up as Godot `ArrayMesh` surfaces with a collision shape for the editor raycasts.

**The update loop.** The UI never regenerates elements directly: every edit changes the element's state (marking it dirty) and flags the world as changed. Once per frame, after every other node, the `ElementManager` propagates the city properties, updates the road curves and the intersections, rebuilds the meshes whose inputs changed, and repositions mesh instances and parametric objects.

**Coordinates.** The program logic, the cores and the exported files use a left-handed coordinate system (Y up, +Z forward, +X right). Conversion to and from Godot's right-handed system happens only inside `GameEngine`.

See the [Architecture Overview](wiki/Architecture-Overview.md) for the scene tree, startup sequence, collision layers and runtime folder layout, and the other *Program internals* pages of the [wiki](wiki/Home.md) for a class-by-class reference.

## Building from source

1. Generate the expression parser: it is not tracked by git. Put `antlr-4.11.1-complete.jar` (from the [ANTLR download page](https://www.antlr.org/download/)) in the `ExpressionParser` folder and run `compile.bat` there (Java 11 or later required); on other systems, run the `java` command from the script and copy the generated `.cs` files to `CustomPackages/expressions-antlr4` by hand. Repeat after every grammar change.
2. Open the project in Godot 4.7 (.NET version), or build it with `dotnet build`.
3. To export, use the Windows or Linux (x86_64) export preset, with the Godot .NET export templates for that platform installed. Exporting also generates `Quickstart guide.pdf` from the wiki, which requires [pandoc](https://pandoc.org) (3.1.2 or later) and [Typst](https://typst.app) in the `PATH`; without them the export still completes, but without the guide.

`setupDependencies.ps1` (or `setupDependencies.sh` on Linux) downloads the [Terrain3D](https://github.com/TokisanGames/Terrain3D) addon into `addons`, for example to restore or update it.

## License

km2 City Builder is released under the MIT license, see [License.txt](License.txt). The licenses of the bundled third-party code are in the [Licenses](Licenses) folder, and described in [Third-Party Code](wiki/Third-Party-Code.md).

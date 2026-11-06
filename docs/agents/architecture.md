# Architecture and program internals

Read the [repository instructions](../../AGENTS.md) first. Paths and command examples below are relative to this repository root unless another working directory is specified. Builder CLI and self-test commands run from the builder root.

For execution and exporter workflows, also read [Development](development.md); for verification and test pitfalls, read [Testing](testing.md).

## Repository layout

| Path | What |
|---|---|
| `Scripts/UI` | Menu bar, editor panels (`EditorPanel/EditorPanels`), placers and terrain modifiers, camera, debug console, guide panel (`Guide`) |
| `Files/guides` | The guides of the program (Markdown, see `wiki/Core-Guides.md`). Keep them up to date when the workflows they describe change |
| `Scripts/CityElements/Logic` | Roads, intersections, patches, building lines, mesh instances, custom elements, city properties |
| `Scripts/CityElements/Geometry` | Mesh generators driven by the core's type definitions |
| `Scripts/CityElements/Types` | Core JSON type loading (`TypeParser`, `Parsers/TypeInheritance.cs` for `extends`) and runtime types |
| `Scripts/RuntimeCalculator` | Expression parser/evaluator, Lua scripting |
| `Scripts/IO` | Save/load, export, import, settings |
| `Scripts/History` | Undo/redo (`UndoManager`); core-owned scenarios use the developer test runner |
| `Scripts/Api`, `Scripts/Operations` | Scripting API (JSON commands, `CommandDispatcher`, `CityApi.*`) and the operations shared by the API and the editor; core-owned API scenarios use the developer test runner |
| `Scripts/Testing`, `Tests` | Generic core-test loader and contract; developer-only shared test support and runner regressions |
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

## Coordinates

See the [coordinate conventions](../../AGENTS.md#coordinates) in the entry file.

## Program internals worth knowing

- **Top-level nodes:** `GetParentNode3D()` returns null for a `TopLevel` node (`LineRenderer`, `RoadSplitLine`), so walk up with `GetParent() as Node3D`.
- **Update loop:** the UI never regenerates elements directly. Edits change state and mark dirty, and `ElementManager` rebuilds once per frame after every other node.
- **Freed before `_Process`:** input runs before the physics step, which flushes the delete queue, so an element a button or key deleted with `QueueFree` can already be freed when `_Process` runs in the same frame. `ProcessUpdate` must call `Cleanup` before touching the element lists, and an exception before `worldChanged` resets repeats every frame. To test this, delete from an `await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame)` continuation.
- **Renderer** (`Scripts/Rendering/CityRenderer`): a SubViewport sharing the main World3D. Render layer bit 17 (`GameEngine.Camera.RenderOnlyLayer`) is hidden from the editor camera, and bit 18 is the gizmo. During a render it forces the best LOD, suspends cull distances and points Terrain3D at the render camera.
- **MCP:** the HTTP server is hand-written on a loopback `TcpListener` (HttpListener would need a URL ACL), default port 7810, Bearer token, settings `mcpServerEnabled/Port/Token`. stdio `mcp` handles messages sequentially. `script_run` must not go through the dispatcher (deadlock), so it uses `ApiHost.RunOnMainThread`.
- **Custom elements** (generic element types defined by the core, not necessarily low-level representations: e.g. the MM2 PSDL attributes, but also its decals and trains): core folder `elements/customElements/<type>/`, save key `customElements`, API kind `customElement`, preset/reference/converter prefix `customElement/<type>`. They used to be called "low-level elements": cities saved before the rename (`lowLevelElements`, `lowLevel` references, `lowLevel/<type>` preset categories) are still read. Don't confuse them with the `customCityProperties` parameter type (`customCityPropertiesType`, types in `elements/customCityPropertiesTypes/`), the nested parameter groups of the city properties (formerly `customElement`/`customTypes`). Converted roads/intersections become hidden `ghost`s whose references resolve to their replacements at export. The generic mechanism is in `wiki/Core-Custom-Elements.md`, the core specifics in its README and [agent instructions](../../Files/cores/MidtownMadness2/AGENTS.md), including the [MM2 export-equivalence check](../../Files/cores/MidtownMadness2/docs/agents/psdl-and-bai.md).
- **Core view toggles:** core `settings.json` `viewToggles`, stored in the city `preferences.json` as `viewToggle.<name>`. Core settings must not be read before a city is loaded (that once cached the wrong settings and broke `.tex` decoding).
- **Spline values:** the values of a road's spline are in its `instanceState` (presets neither store nor change them), with initial values in the road type's `settings.json`. A road created in the editor copies them from `PresetManager.lastRoadInstanceState`, set by `RoadPlacer`; API roads always use the defaults. Curves get both states merged (`SplineCurves.ForRoad`). Terrain patches have no presets and no instance state.
- **Terrain3D** snaps heightmap imports to its region grid; the region size is `min(256, resolution/2)`.
- An exactly vertical physics ray on an edge of the Terrain3D heightmap cells can miss the terrain (Godot physics: whole coordinates at negative X near the origin, where the snapped clicks are). `DoRaycast` and `TryProjectPoint` retry vertical rays a little off the edges (`IntersectVerticalRay`); raw vertical `IntersectRay` calls need the same.
- Forward+ wireframe works without `RenderingServer.SetDebugGenerateWireframes` (needed only on the Compatibility renderer).
- Graphics settings (`renderingMode`, `shadowsEnabled`, `propsCullingDistance`) are global, in `settings.json` (Settings→Preferences→Graphics). Local paths and the ground overlay stay per city in `preferences.json`.

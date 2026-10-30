# Scripting API

The scripting API loads, reads, edits and exports cities without the mouse tools and editor panels. It is the base for command line and Python scripting, which talk to it with JSON commands. Everything the editor can do to a city can be done through it, in the same steps: a road is drawn point by point, a terrain patch gets its points as if they were clicked, a parameter is set like in its field, and so on.

## Layers

```
 scripts (JSON) ──▶ Api.CommandDispatcher    queue, method table, JSON conversions
						  │
						  ▼
 editor panels ──┐   Api.CityApi             references, validation, undo steps, updates
 and tools       │        │
				 └────────┴──▶ Operations.*  the editing logic, without UI
									│
					ElementManager, elements, presets, undo manager, IO
```

| Layer | Folder | Purpose |
|---|---|---|
| Operations | `Scripts/Operations` | The editing logic of the editor panels and placers (`RoadDrawing`, `Roads`, `Intersections`, `TerrainPatches`, `TerrainPoints`, `BuildingLines`, `Anchors`, `Meshes`, `Props`, `States`, `Presets`, `Builtins`, `Terrain`, `City`, `CityFiles`). The panels and placers call it too, so the API can't behave differently from the editor. The operations return results instead of showing alerts. |
| API | `Scripts/Api` | `CityApi` (one method per command), `CommandDispatcher` (the JSON protocol), `ApiHost` (the node running the queued commands every frame), `ScriptRunner` (the transport of the Python scripts), `ApiSelfTest`. |
| Command line | `Scripts/Cli` | The commands of the command line (see [Command Line](Command-Line)), on top of `CommandDispatcher`. |

The long operations (loading, saving, exporting, whole city generation, rotation, heightmap import) are awaitable: they return a `Task` and report their progress through an `IProgress<(float, string)>`. The decisions they need from the user (migrating a city, installing Python modules) go through `Operations.IInteraction`: `UiInteraction` asks with the editor popups, `FixedInteraction` answers from the options of the call, so a script is never blocked by a popup.

## Running commands

`ApiHost` is created at startup (`CityBuilderMenuBar.ManualStart`) in every session. A transport sends a command with `ApiHost.Instance.Dispatcher.Enqueue(json, reply)`, which can be called from any thread: the commands run one at a time on the main thread, and `reply` is called with the response (and with the events of the command before it). Every frame the queued commands run for up to 8 ms (`frameBudgetMs`); after a command sent from another thread, the next one is waited for within that time, so a script waiting for each reply runs many commands per frame while the program stays responsive. The program can run without a window (`--headless`): the editor UI still exists, but it is not drawn.

When a command is given on the command line, the program is driven by it: the last city is not loaded at startup and Python is not checked until the command loads a city. The [Command Line](Command-Line) runs single commands (`call`), files of commands (`run`), or reads them from its standard input and writes the responses to its standard output (`serve`), so any program can drive the city builder with this protocol.

The Python scripts of the Scripts menu (and the one given with the `script` command) are run by `ScriptRunner`, which sends the lines written by the script to the dispatcher and writes the responses back to it: see [Python Scripts](Python-Scripts).

AI agents use the API through the [MCP Server](MCP-Server) (`Scripts/Mcp`), over HTTP in the editor or with the `mcp` command. `ApiHost.RunOnMainThread` runs work from other threads on the main thread outside of the dispatcher (the MCP `script_run` tool: a script sends its own commands to the dispatcher meanwhile).

### Self-test

```
Godot_v4.7.1-stable_mono_win64_console.exe --path . [--headless] -- selftest api <new folder>
```

Creates a city in the folder (from the SF template of the Midtown Madness 2 core) and drives it only with JSON commands: every kind of element, anchors, terrain points and heights, parameters, arrays, presets, props, undo and redo of each change, batches, transactions, errors, a script changing an element open in the editor, parity with the elements made with the editor tools, whole city generation, save and load, export JSON, geometry, the core exporter, the validation, the renders (with a window; headless, only the `no_renderer` error), the MCP server (its messages, and over HTTP on a test port) and the custom elements (control points, constraints, containers, the conversions of the core, ghosts, save and load, their editor). It also contains the whole city terrain and building generation written with the commands only (`ScriptTerrain`, `ScriptBuildings`), checked against `city.generateTerrain` and `city.generateBuildings`: they are the reference for scripts generating cities. The same generators written in Python (the `cityTerrain` and `cityBuildings` examples of `wiki/examples/scripts`, see [Python Scripts](Python-Scripts#examples)) are run through `ScriptRunner` and checked in the same way, along with the runner itself (inputs, selection, single undo step, failures, cancelling, settings restored) and the input panel of the scripts. It writes `api_selftest.log` next to the folder and exits with code 1 if anything failed.

The suite also checks mesh-instance selection with generated fixtures: clicks through gaps, double-sided triangles, multiple surfaces, nested LODs, placement offsets, transformed instances, shared shapes, replacement, undo/redo, fallbacks and save/reload. Set the environment variable `KM2_SELFTEST_MESH_SELECTION=test` to run just these checks in a new test city. Unset it to run the full suite.

`KM2_SELFTEST_MESH_SELECTION=benchmark` runs a separate bounded comparison instead: ten available local mesh assets chosen with seed 42, 100 copies each, comparing the old boxes against shared triangle shapes. Mesh loading is warmed before timing. The log reports cold shape construction, warm instance creation, median/p95 ray query times and Godot's static allocator memory delta (not total process memory). Both cases use identical rays across overlapping objects and empty space; lower triangle hit counts can be expected where rays pass through geometric gaps. Camera LOD changes and copies must retain the cached shapes. Back up the program's `settings.json` before either test mode and restore it afterward, as for the full suite.

## Protocol

Request:

```json
{"id": 1, "method": "roads.create", "params": {"points": [{"position": [0, 0, 0], "onGround": true}, {"position": [100, 0, 0], "onGround": true}]}}
```

Response, with the same `id`:

```json
{"id": 1, "result": 12}
{"id": 1, "error": {"code": "not_found", "message": "element 99 not found", "details": null}}
```

While a long command runs, events are sent before its response: `{"event": "progress", "for": 1, "value": 0.4, "text": "Loading the city..."}`.

The parameters are matched by name (see the method reference below); missing optional ones take their default, unknown ones are an error (`invalid_param`), so are values of the wrong type (`invalid_value`). `api.methods` lists every method with its parameters.

### Batches

```json
{"id": 2, "method": "batch", "params": {"commands": [
    {"method": "state.setBuiltin", "params": {"ref": 12, "name": "segments", "value": 8}},
    {"method": "roads.extend", "params": {"road": 12, "point": {"position": [150, 0, 30], "onGround": true}}}
], "atomic": true}}
```

The commands of a batch are a single undo step, and the result is the list of their results. With `atomic` (the default), if a command fails the ones before it are undone; the error has `details.index`, the index of the failed command. The long operations can't be part of a batch.

## Conventions

### Elements and references

Every element has an id (an integer) in the session: roads, intersections, terrain patches, building lines, mesh instances and terrain points. The city properties have the id `0`. The ids stay the same through undo and redo, but they are assigned again when a city is loaded: the names of the elements are saved with the city, so a script that must work on a city across sessions finds its elements by name (`elements.list`), or references them by name directly.

A reference (`ref` in the method reference) is one of:

| Form | Refers to |
|---|---|
| `12`, `{"id": 12}` | the element with the id |
| `"Main street"`, `{"name": "Main street", "kind": "road"}` | the element with the name (the kind is needed only if two elements of different kinds have the same name) |
| `{"line": <ref>, "building": 0}` | a building of a building line |
| `{"line": <ref>, "building": 0, "side": "front"}` | a side of a building (`front`, `left`, `right`, `back`) |
| `{"line": <ref>, "building": 0, "side": "front", "block": 1}` | a block of the block dictionary of a side |
| `{"patch": <ref>, "borderMesh": 0}` | a border mesh of a terrain patch |
| `{"road": <ref>, "container": "right"}` | a props container of the prop rule of a road |
| `{"road": <ref>, "container": "right", "list": "elements", "index": 0}` | a props element (no `index`: the element of a list with a single one) |
| `{"parent": <ref>, "param": "trafficCars", "index": 0}` | an element of an array parameter (no `index`: a custom city properties parameter) |
| `{"owner": <ref>, "object": "trafficLight", "path": ["races.0"]}` | a parametric object (an `objectInstance` parameter) of a road or of the city properties; `path` lists the array elements leading to it, from the owner |

The kinds are `road`, `intersection`, `terrainPatch`, `buildingLine`, `meshInstance`, `terrainPoint`, `cityProperties`, `propPath`, and for the parts of elements `building`, `buildingSide`, `buildingBlock`, `borderMesh`, `propContainer`, `propElement`, `arrayElement`, `customCityProperties`, `parametricObject`.

### Points

The points of roads, terrain patches and building lines are given as what the click of the editor would have hit (`point` in the method reference), and they behave the same way:

| Form | Point | Used by |
|---|---|---|
| `{"position": [x, y, z], "onGround": true}` | a point (projected on the ground if `onGround`) | all |
| `{"terrainPoint": 34}` | an existing terrain point (shared with the element using it) | terrain patches, building lines |
| `{"anchor": {"of": <road or intersection>, "index": 3}}` | an anchor along the edge of a road or intersection (the point follows it; see the `anchors` of `elements.get` with `derived`) | terrain patches, building lines |
| `{"link": [34, 35], "position": [x, y, z]}` | a point on the segment between two terrain points (it stays on it) | terrain patches, building lines |
| `{"anchorLink": {"of": <road or intersection>, "index": 3}, "position": [x, y, z]}` | a point on the segment from an anchor to the next one along the same edge (it stays on it); without `position`, its middle. The automatic closing of a building line starts from such a point | terrain patches, building lines |
| `{"roadEnd": {"road": <ref>, "end": "start"}}` | the free end of another road: the roads are connected by an intersection | roads |
| `{"intersection": <ref>}` | an intersection: the road is connected to it | roads |
| `{"roadMiddle": {"road": <ref>, "near": [x, y, z]}}` | the middle of another road, at the point closest to `near`: the road is split there and connected to the new intersection | roads |

With `followAnchors`, a point on an anchor adds the anchors between it and the previous anchor too (like the modifier key in the editor).

`patches.create` and `lines.create` return the id of the element; with `details` they return `{id, points, anchors}`: the id of the terrain point each target gave (the last one when a target gave several, `null` when it gave none, e.g. a point already in it) and, with `autoClose`, the anchors of the loop it was closed with.

The points of terrain patches and building lines (in `elements.get` with `derived`) tell what they are attached to: `anchor` (the generated anchor they are on), `pointAnchor` (the terrain point they follow), or `segment` (`{anchor, percent}` or `{points, percent}`). `terrainPoints.move` moves them as the gizmo does: a point on a segment slides along it, a point on a fixed anchor can't be moved. `terrainPoints.attach` attaches an existing point to a road or intersection anchor (`to: {anchor}`) or to a segment between their anchors (`to: {anchorLink}`, with an optional `position`, otherwise its middle). Building-line points can also follow a terrain-patch point (`to: {terrainPoint: id}`), including one already on an anchor or segment, or a patch edge (`to: {link: [pointId, pointId], position: [x, y, z]}`: the existing link between two patch points). It snaps to the anchor or the closest position on the segment, and rejects circular attachments. `terrainPoints.detach` clears the attachment at the current position; the normal ground projection of its elements resumes. Both keep the same point, so all the elements sharing it (including border meshes) follow. Following a patch point keeps the building point separate from its source; detaching leaves the source unchanged. These commands don't split shared points or accept the anchors reserved for custom elements.

### Anchors

The anchors are the points along the edges of the roads and intersections where terrain patches and building lines attach. They are referenced as `{"of": <road or intersection>, "index": i}`, the index among the anchors of the element. The anchors of a road or intersection can be created again when it changes (e.g. its points or its width), so a script reads them again after changing roads. Each anchor is linked to the previous and next one (`prev`, `next`), across the elements too: around a block they form a closed loop, which is what the automatic closing follows.

- `anchors.list` gives every anchor of the city, in the order of the whole city generators, with its position, `insideDirection`, `prev`, `next` and `usedBy` (the patch and line points on it, or on its segment with `segment: true`). With `unusedBy` (`terrainPatch` or `buildingLine`), only the anchors with no point of that kind on them: the anchors the generators start from. The anchors across the road ends, only for the custom elements (`customElementsOnly`, see [Core Custom Elements](Core-Custom-Elements#control-points)), are listed only with `customElement: true` (and never with `unusedBy`).
- `anchors.loop` gives the anchors from one to another along the edges (without `to`: the loop around the block, ending with the anchor itself).
- `anchors.segment` gives the segment from an anchor to the next one (null at the end of an edge), where `anchorLink` points go.
- `patches.autoClose` and `lines.autoClose` return the anchors of the loop.

### Terrain heights

The heightmap is a grid of `gridSize` × `gridSize` samples (`terrain.info`), `pixelSize` meters apart: the sample `(x, z)` is at `(x * pixelSize + origin, z * pixelSize + origin)` in the city. `terrain.heights.get` and `terrain.heights.set` read and write the heights (in meters) of an area of the grid, row by row along z, as a list of rows (`encoding: "array"`) or as base64 little endian 32 bit floats (`"base64"`, the default, much smaller). The heights are limited to the range of the terrain (`-maxHeight` to `maxHeight`); `set` returns how many were clamped. A change is one undo step, and the elements on the ground follow the new heights.

### Geometry

`elements.geometry` gives what is generated for an element (road, intersection, terrain patch, building line with its buildings, mesh instance or parametric object) in the format of the export JSON read by the exporters of the cores: meshes as base64 arrays (`b64v3a_vertices`, `b64ia_indices`...), with the material of each submesh, the lanes and props of the roads, the splines and sides of the buildings. `materials` and `meshes` have the entries of the material and mesh dictionaries it references. The roads and intersections are referenced by their ids (in the export file, by their index). The encoding is described in [Input and Output](Input-and-Output#export-format-ioexportedcity-ioexportedcitycs-and-cityencoderjson_export); `Files/python/DecodeExportedJson.py` decodes it.

### Values

Vectors are `[x, y, z]` (or `{"x": ..., "y": ..., "z": ...}`), quaternions `[x, y, z, w]`, rotations of mesh instances euler angles in degrees. Everything is in the program coordinates: left handed, Y up, +Z forward, the same as the saved and exported cities.

States (the values of an element) are plain JSON objects, as in the saved city. The values of the parameters of the core are set with `state.setParam`, by the type of the parameter: `bool`, `int`, `float`, `string`, `texture` and `mesh` (relative paths), `enum` (the index, or the name of the value). The arrays and custom city properties are edited with `state.array.*`, `state.customCityProperties.ensure` and their references, the parametric objects with `objects.*`. The properties the editor panels have for every type (the curve type of a road, the height of a building, the floors of a building side...) are set with `state.setBuiltin`; `state.schema` lists both for an element.

### Images

`view.render` renders the city offscreen, with a camera of its own (`Rendering.CityRenderer`): the view of the editor doesn't change. It needs a renderer: with `--headless` it fails with `no_renderer`. The image is in the result as base64 (`image`), or saved to `path`.

- `camera`: `top` (orthogonal, looking down, x to the right and z up in the image), `orbit` (perspective, `pitch` degrees below the horizon, `yaw` around the vertical axis, `distance` or the one fitting the area), `custom` (`position`, `target`, `fov`) or `editor` (the camera of the editor).
- The area shown: `elements` (fitted with `margin`), `bounds` `[minX, minZ, maxX, maxZ]`, `center` and `size`, or the whole city.
- `layers`: `buildings`, `terrain`, `geometry` (roads, intersections, terrain patches), `meshes`, `props`, `helpers` (control points, terrain points, links); by default everything but `helpers`. Every LOD is at its most detailed level and nothing is culled by distance (`bestLod: false`: as the camera of the render sees them). `wireframe`: the meshes as wireframe, on a plain background unless `background` or `transparent` is given.
- Over the 3D: `labels` (`roads`, `intersections`, `patches`, `lines`, `meshes` or `all`: name and id of each element, moved apart when they overlap), `highlight` (elements tinted with `highlightColor`, over everything), `grid` (lines every `grid` meters with their coordinates), `scaleBar`, and `overlays`: `{"type": "label" | "marker" | "polyline", "text", "position" | "points" | "element", "color", "size", "closed"}`.

The result also has `mapping`, from the pixels to the city: for the top view `metersPerPixel` and `origin` (the x, z of the top left corner: `x = origin[0] + px * metersPerPixel`, `z = origin[1] - py * metersPerPixel`); for the others the position, forward and up directions and fov of the camera. `labels` and `overlays` give the pixel of each label and overlay (`inView`: inside the image).

`view.screenshot` is the window as the user sees it, and `view.camera.get`, `view.camera.set` and `view.focus` move the camera of the editor (e.g. to show the user an element). `view.toggles` and `view.setToggle` read and switch the visibility toggles the core adds to the View menu (e.g. the room outlines of MM2), and `view.terrainHidden` and `view.setTerrainHidden` the *Hide terrain grid* option.

### Changes and undo

Each command changing the city is an undo step, like an action in the editor: `history.undo` and Edit > Undo undo it. If a command fails halfway, its partial change is undone. With `api.configure` and `history: false` the changes are not recorded (faster for large scripts), but then a failed command can't be undone.

After each change the city is updated, so that what is computed from the elements (curves, anchors, intersection sizes, buildings) can be read and used by the next command. With `autoFlush: false` the updates wait until `city.flush` (or the next frame).

A transaction makes several commands a single undo step, also when the script reads the city between them (unlike a batch): `history.begin` opens it, `history.commit` closes it, `history.rollback` closes it undoing its changes. Each command still fails alone: a failed command is undone, the transaction stays open. While a transaction is open, undo and redo are not possible (from the script or the Edit menu), nor are the city rotation, the heightmap import and loading, creating, reloading or closing a city; the changes made meanwhile in the editor become part of it. While a Python script runs with the transaction of its run, it can't open or close transactions itself.

When a script changes the city while the editor is open, the editors are closed first, as before an undo.

### Large scripts

A script making thousands of changes (e.g. generating a whole city) runs faster with:

- `api.configure` with `autoFlush: false`, and `city.flush` before reading what the changes derive (anchors, curves...);
- reads grouped in a `batch`, and `anchors.list` instead of reading the anchors of each element;
- a transaction, to make its changes a single undo step.

An element left with less points than it needs (a road with one point, a terrain patch with two perimeter points, a building line with one point) is deleted, as when its editor is closed: the commands removing points return `{"id", "deleted"}`.

### Long operations

`city.new`, `city.load`, `city.reload`, `city.save`, `city.exportJson`, `city.export`, `city.validate`, `city.generateTerrain`, `city.generateBuildings`, `city.rotate` and `terrain.importHeightmap` last several frames and report their progress with events. No other command can run meanwhile (`busy`). `view.render` and `view.screenshot` last a few frames too.

`city.load` never asks anything: a city made with another version of its core is migrated only with `migrate: "always"` (which changes the city files), otherwise it fails with `migration_required`; the Python modules missing for the core are installed only with `installPythonModules`. A city loaded by a script is not loaded again when the program starts, unless `remember` is set.

## Errors

| Code | Meaning |
|---|---|
| `invalid_request` | the request is not valid JSON or has no method |
| `unknown_method` | no method with that name |
| `invalid_param` | a parameter is missing or unknown, or a reference is malformed |
| `invalid_value` | a value has the wrong type or is out of range |
| `no_city` | no city is loaded |
| `busy` | a long operation is running |
| `not_found` | the element, part, parameter, preset, template, exporter... does not exist |
| `wrong_kind` | the reference is to an element of another kind |
| `invalid_operation` | the change is not possible (e.g. adding a point after the end intersection of a road, deleting a parametric object generated by its element) |
| `limit_reached` | an array, border mesh list or props list is full |
| `autoclose_failed`, `no_loop_found` | the automatic closing of a terrain patch or building line failed |
| `already_exists` | `city.new` on a folder with a city |
| `core_not_found`, `city_for_newer_core`, `migration_required`, `migration_failed` | the city can't be loaded with the installed cores |
| `python_missing` | Python or the Python modules of the core are not available |
| `no_renderer` | `view.render` or `view.screenshot` without a renderer (`--headless`) |
| `no_camera` | the camera of the editor isn't available |
| `io_error`, `export_failed`, `batch_failed` | the operation failed (see the message; for `export_failed`, `details.items` has the warnings and errors reported by the exporter) |
| `internal_error` | an unexpected error (logged with its stack trace) |

## Example

A road ending on the middle of another road, a terrain patch along it, then the city saved and exported (25 stands for the id returned by the second command):

```json
{"id": 1, "method": "city.load", "params": {"folder": "C:/cities/test"}}
{"id": 2, "method": "roads.create", "params": {"points": [
	{"position": [0, 0, 0], "onGround": true},
	{"position": [200, 0, 0], "onGround": true},
	{"roadMiddle": {"road": "Road 3", "near": [400, 0, 0]}}]}}
{"id": 3, "method": "elements.get", "params": {"ref": 25, "derived": true}}
{"id": 4, "method": "patches.create", "params": {"perimeter": [
	{"anchor": {"of": 25, "index": 0}}, {"position": [0, 0, -50], "onGround": true}, {"position": [100, 0, -50], "onGround": true}]}}
{"id": 5, "method": "city.save"}
{"id": 6, "method": "city.export", "params": {"path": "C:/cities/test/out", "exporter": "psdl", "flags": {"bai": false}}}
```

## Method reference

### `api.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `api.configure` | `history: bool = null`<br>`autoFlush: bool = null` | object | Session settings: history (record the changes as undo steps), autoFlush (update the city after every change) |
| `api.version` | - | object | The version of the protocol and of the program |

### `city.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `city.batchParameters` | `kind: string` | object | The fields the batch update can use for a kind, by type of element |
| `city.batchUpdate` | `kind: string`<br>`update: string`<br>`where: string = ''` | int | Sets fields of every element of a kind satisfying the where expression (like Edit > Batch update), returns how many were updated |
| `city.close` | - | - | Closes the city (the unsaved changes are lost) |
| `city.eraseBuildings` | - | - | Deletes every building line |
| `city.eraseTerrain` | - | - | Deletes every terrain patch |
| `city.export` | `path: string`<br>`exporter: string`<br>`flags: {id: bool} = null`<br>`showConsole: bool = false` | object | Exports the city with an exporter of the core (by one of its formats, see exporters.list), with the given flags (the others keep their default, the ones with a format are set by it) |
| `city.exportJson` | `path: string` | object | Writes the export JSON of the city (with the generated geometry, as read by the exporters of the cores) |
| `city.flush` | - | - | Rebuilds the changed elements now (needed only with autoFlush off) |
| `city.generateBuildings` | `linePreset: string`<br>`buildingPresets: string[]`<br>`minLength: number = 15`<br>`maxLength: number = 15`<br>`subdivide: bool = true`<br>`nullOnly: bool = false` | object | Creates building lines around the blocks between the roads (like Create > Whole city buildings), split into buildings with the presets |
| `city.generateTerrain` | `distance: number = 5`<br>`segmentLength: number = 100`<br>`fusionDistance: number = 0`<br>`smoothing: int = 0`<br>`internalDistance: number = 0`<br>`texture: string = null` | object | Creates terrain patches between the roads (like Create > Whole city terrain) |
| `city.info` | - | object | The city loaded: folder, core, terrain settings and element counts |
| `city.load` | `folder: string`<br>`migrate: string = 'never'`<br>`installPythonModules: bool = false`<br>`remember: bool = false` | object | Loads the city of a folder. migrate: 'always' to migrate a city made with another version of its core (the city files are changed), 'never' to fail instead. remember: load it again when the program starts |
| `city.new` | `folder: string`<br>`core: string`<br>`template: string`<br>`terrainSize: int = 4096`<br>`heightmapResolution: int = 4096`<br>`maxHeight: number = 1000`<br>`installPythonModules: bool = false` | object | Creates a city in a folder from a template of a core (see cores.templates) and loads it |
| `city.reload` | - | object | Loads the current city again from its saved file (the unsaved changes are lost) |
| `city.rotate` | `angle: string` | object | Rotates the whole city (elements and terrain): 90ccw, 90cw or 180 |
| `city.save` | - | object | Saves the city in its folder |
| `city.validate` | `exporter: string = null` | object | Validates the city with the exporters of the core that can (or the one of the format): a dry export to a temporary folder, returning the warnings and errors the exporters reported (level, message, element {name, kind}, code, position, details, exporter); ok: no errors. See [Validation](Validation) |

### `history.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `history.begin` | `label: string = 'script'` | object | Opens a transaction: the changes until history.commit become a single undo step (each command still fails alone) |
| `history.commit` | - | object | Closes the transaction: its changes become a single undo step |
| `history.redo` | - | object | Redoes the last undone change, like Edit > Redo |
| `history.rollback` | - | object | Closes the transaction undoing its changes |
| `history.state` | - | object | Whether there are changes to undo or redo, and the label of the open transaction (null if none) |
| `history.undo` | - | object | Undoes the last change, like Edit > Undo |

### `cores.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `cores.list` | - | object | The installed cores |
| `cores.templates` | `core: string` | object | The city templates of a core (the name is used by city.new) |

### `types.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `types.list` | `kind: string` | object | The types of an element kind of the current core, with their parameters, built-in properties and ancestry (the type, then the types it extends) |

### `presets.*`

Custom element types opt in with `settings.json` `presets: true`. Their category is `customElement/<type>` (for example `customElement/decal`), and the existing `state.schema`, `state.applyPreset` and `state.saveAsPreset` commands support their elements. Applying or syncing a preset preserves geometry, attachments, container/source relationships, element references and parameters marked `instanceSpecific`. Saves reject a mismatched type or a spline curve mode excluded by the type. The editor's remembered preset does not affect `customElement.create`, imports or converters.

| Method | Parameters | Result | Description |
|---|---|---|---|
| `presets.delete` | `category: string`<br>`name: string` | - | Deletes a preset (not the default one); linked elements, including enabled custom element types, are unlinked from it |
| `presets.get` | `category: string`<br>`name: string` | state | The values of a preset |
| `presets.list` | `category: string` | string[] | The names of the presets of a category (road, intersection, buildingLine, building, buildingSide, buildingBlock, terrainPatchBorderMesh, propContainer, propElem, roadPropRule, or a container such as rail); the first one is the default |
| `presets.save` | `category: string`<br>`name: string`<br>`values: state` | - | Saves a preset with the given values (overwriting the one with the same name) |
| `presets.sync` | `category: string`<br>`name: string` | - | Gives the current values of a preset to linked roads, intersections, building parts or enabled custom element types |

### `assets.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `assets.list` | `kind: string`<br>`regex: string = null` | string[] | The textures or meshes available to the city (its folder and the extra folders), optionally filtered with a regular expression |

### `exporters.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `exporters.list` | - | object | The exporters of the current core, with their flags; validates: the exporter can validate a city (city.validate) |

### `elements.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `elements.convert` | `elements: ref[]`<br>`converter: string` | object | Converts elements to custom elements with a converter of the core (converters.list): the converter replaces them (roads and intersections become ghosts: hidden, not exported, kept for the elements connected to them; the other elements are deleted; some converters change existing custom elements instead). Returns the ids of the custom elements created, then the ones changed |
| `elements.geometry` | `ref: ref` | object | The generated geometry of a road, intersection, terrain patch, building line (with its buildings), mesh instance or parametric object, as in the export JSON (base64 arrays), with the materials and meshes it references |
| `elements.get` | `ref: ref`<br>`derived: bool = false` | object | An element as stored in the saved city (ids as references); derived: also the data computed from it (curve, anchors, buildings...) |
| `elements.list` | `kind: string = null`<br>`name: string = null`<br>`type: string = null` | list | The elements of the city (optionally of a kind, with a name or of a type): id, kind, name, type |
| `elements.rename` | `ref: ref`<br>`name: string` | object | Renames an element |
| `elements.runtimeValue` | `ref: ref`<br>`variable: string`<br>`valueType: string = 'float'` | object | The value of a definition of the type of a road (as computed for its last update) |

### `anchors.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `anchors.list` | `unusedBy: string = null`<br>`customElement: bool = false` | list | Every anchor of the city (in the order of the city-wide generators): owner, index, position, previous and next anchor, the points attached to it, customElementsOnly; unusedBy (terrainPatch or buildingLine): only the anchors no point of that kind is on; customElement: also the anchors only for the custom elements (customElementsOnly: across the road ends, not for terrain patches and building lines) |
| `anchors.loop` | `anchor: anchor`<br>`to: anchor = null`<br>`invert: bool = false` | list | The anchors from an anchor to another one (to: none for the loop around the block, ending with the anchor itself), along the shorter way (invert: the longer one), as followed when adding points between two anchors |
| `anchors.segment` | `anchor: anchor` | object | The segment from an anchor to the next one along the same edge (where a point can be placed with anchorLink), null at the end of the edge |

### `state.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `state.applyContainerPreset` | `ref: ref`<br>`container: string`<br>`name: string` | object | Gives the parameters of a container (e.g. the rails of a road) the values of a preset of the container |
| `state.applyPreset` | `ref: ref`<br>`name: string` | object | Gives the element the values of a preset (the instance specific values are kept) |
| `state.array.add` | `ref: ref`<br>`param: string` | int | Adds an element with the default values to an array parameter, returns its index |
| `state.array.clone` | `ref: ref`<br>`param: string`<br>`index: int` | int | Adds a copy of an element at the end of an array parameter, returns its index |
| `state.array.move` | `ref: ref`<br>`param: string`<br>`index: int`<br>`direction: string` | int | Swaps an element of an array parameter with the previous (up) or next (down) one, returns its new index |
| `state.array.remove` | `ref: ref`<br>`param: string`<br>`index: int` | - | Removes an element of an array parameter |
| `state.customCityProperties.ensure` | `ref: ref`<br>`param: string` | - | Creates the state of a custom city properties parameter if missing |
| `state.get` | `ref: ref` | object | The state (and instance state) of an element or part of one |
| `state.getParam` | `ref: ref`<br>`name: string` | object | The value of a parameter or built-in property (null if not set: the default applies) |
| `state.saveAsPreset` | `ref: ref`<br>`name: string` | object | Saves the values of the element as a preset (overwriting the one with the same name); the element gets linked to it |
| `state.saveContainerPreset` | `ref: ref`<br>`container: string`<br>`name: string` | object | Saves the parameters of a container of the element (e.g. the rails of a road) as a preset of the container |
| `state.schema` | `ref: ref` | object | The parameters of the core and the built-in properties an element or part of one can have, and its types and presets |
| `state.setBuiltin` | `ref: ref`<br>`name: string`<br>`value: any` | object | Sets a built-in property (see state.schema), e.g. the curve type of a road or the slices of a building side |
| `state.setParam` | `ref: ref`<br>`name: string`<br>`value: any` | object | Sets a parameter of the core (bool, int, float, enum, string, texture, mesh) |
| `state.setType` | `ref: ref`<br>`type: string` | object | Changes the type: the other values are kept (the missing ones read as defaults) |

### `roads.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `roads.create` | `points: point[]`<br>`preset: string = null`<br>`type: string = null` | int | Creates a road through the points (in order, as clicked with the road tool). The first point can be a road end, an intersection or the middle of a road, and so can the last one |
| `roads.delete` | `road: ref` | - | Deletes a road (and the intersections left with no road) |
| `roads.extend` | `road: ref`<br>`point: point`<br>`atStart: bool = false` | - | Adds a point at the end of a road (before its start if atStart), as with the road tool in add after last (add before first) mode (a road end, an intersection or the middle of a road connect the road there) |
| `roads.insertPoint` | `road: ref`<br>`index: int`<br>`position: vec3`<br>`onGround: bool = false` | - | Inserts a control point at the index, as with the road tool in insert mode |
| `roads.split` | `road: ref`<br>`near: vec3` | object | Splits a road at the point of its free part (outside the intersections at its ends) closest to near, as with the road tool in split mode: the road keeps the first part, a new road with the same properties gets the second one, and a new seamless intersection connects them (see intersections.setSeamless). Returns {newRoad, intersection} |
| `roads.movePoint` | `road: ref`<br>`index: int`<br>`position: vec3`<br>`onGround: bool = false` | - | Moves a control point of a road |
| `roads.props.applyRulePreset` | `road: ref`<br>`preset: string` | - | Gives the prop rule of a road the values of a prop rule preset (of its road type) |
| `roads.props.enable` | `road: ref`<br>`container: string`<br>`enabled: bool` | - | Enables or disables a props container of the prop rule of a road |
| `roads.props.saveRulePreset` | `road: ref`<br>`name: string` | - | Saves the prop rule of a road as a preset |
| `roads.removePoint` | `road: ref`<br>`index: int = null` | object | Removes a control point (the road must keep two), or the last one if no index is given (detaching the road from its end intersection, the road is deleted if it is left with one point) |
| `roads.setPointRotation` | `road: ref`<br>`index: int`<br>`yaw: float = null`<br>`roll: float = null` | object | Sets the yaw (around the vertical axis, positive to the right) and the roll (banking, positive raises the right side) of the sections at a control point, in degrees relative to the direction of the curve (up to 75). The values not given are kept. They are stored even where they have no effect: an axis the road type doesn't allow, or beyond 60 degrees of yaw at an end on an intersection. Returns `{yaw, roll, effectiveYaw, effectiveRoll}` |

### `intersections.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `intersections.delete` | `intersection: ref` | - | Deletes an intersection (the roads are detached from it) |
| `intersections.mergeRoads` | `intersection: ref`<br>`keep: ref = null` | int | Merges the two roads of an intersection into one road, deleting the intersection (keep: the road whose settings are kept, by default the first one; the end on the intersection gets the properties of the far end of the other road). The other road is reversed if needed, only its geometry: its left and right properties are not swapped. The merged road keeps the curve type of the kept road: the merge is exact for roads with the same curve (two Bézier roads only if they are the two parts of a curve, as after a split), otherwise its curve follows both roads as closely as its type allows. Returns the id of the merged road |
| `intersections.move` | `intersection: ref`<br>`position: vec3`<br>`onGround: bool = false` | - | Moves an intersection (the roads follow it) |
| `intersections.setSeamless` | `intersection: ref`<br>`seamless: bool = true` | - | Makes the two roads of an intersection join seamlessly (or not): the intersection has no surface and no size, and the ends of the roads share their direction and roll (no yaw). Kept with more than two roads, but without effect |

### `patches.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `patches.addInternalPoint` | `patch: ref`<br>`point: point` | - | Adds an internal point to a terrain patch (to add vertices inside it) |
| `patches.addPerimeterPoint` | `patch: ref`<br>`point: point`<br>`followAnchors: bool = false` | int or null | Adds a point at the end of the perimeter of a terrain patch, returns the terrain point id (null if not added, e.g. already in it) |
| `patches.autoClose` | `patch: ref` | object | Closes the perimeter following the anchors from its single point (on an anchor), returns the anchors of the loop |
| `patches.borderMesh.add` | `patch: ref` | int | Adds a border mesh (with the default preset) to a terrain patch, returns its index |
| `patches.borderMesh.addPoint` | `patch: ref`<br>`borderMesh: int`<br>`point: int` | - | Adds a perimeter point to a border mesh (with the perimeter points between it and the segment of the border mesh) |
| `patches.borderMesh.remove` | `patch: ref`<br>`borderMesh: int` | - | Removes a border mesh from a terrain patch |
| `patches.create` | `perimeter: point[]`<br>`internalPoints: point[] = null`<br>`type: string = null`<br>`followAnchors: bool = false`<br>`autoClose: bool = false`<br>`details: bool = false` | object | Creates a terrain patch with the perimeter points (as clicked, followAnchors: follow the anchors between two anchors, like with the modifier key) and the internal points; autoClose: close the perimeter from its single anchor point. Returns the id, or with details {id, points: the point id given by each perimeter target, anchors: the anchors of the loop} |
| `patches.delete` | `patch: ref` | - | Deletes a terrain patch |
| `patches.generateInternalPoints` | `patch: ref`<br>`distance: number` | int[] | Fills a terrain patch without internal points with internal points at the distance from each other and from the perimeter, returns their ids |
| `patches.removeLastPerimeterPoint` | `patch: ref` | object | Removes the last perimeter point (the patch is deleted if left with less than three) |
| `patches.removePoint` | `patch: ref`<br>`point: int` | object | Removes a perimeter or internal point from a terrain patch (the patch is deleted if left with less than three perimeter points) |

### `terrainPoints.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `terrainPoints.move` | `point: int`<br>`position: vec3`<br>`onGround: bool = false` | vec3 | Moves a point of terrain patches and building lines, as with the gizmo: a point on a segment slides along it, a point on an anchor can't be moved. Returns the new position |
| `terrainPoints.attach` | `point: int`<br>`to: point` | int | Attaches an existing point to `{anchor}` or `{anchorLink}` (optional position for the segment). Building points also accept `{terrainPoint}` for a patch corner, or `{link, position}` for a patch edge. Keeps its id and shared references; returns the point id |
| `terrainPoints.detach` | `point: int` | int | Clears the attachment, keeping the point, its position and shared references; returns the point id |

### `lines.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `lines.addPoint` | `line: ref`<br>`point: point`<br>`dividing: bool = false`<br>`followAnchors: bool = false` | int or null | Adds a point at the end of a building line, returns the terrain point id (null if not added) |
| `lines.applyBuildingPreset` | `line: ref`<br>`preset: string` | - | Gives every building of a line the values of a building preset |
| `lines.autoClose` | `line: ref`<br>`presets: string[]`<br>`minLength: number = 15`<br>`maxLength: number = 15`<br>`subdivide: bool = true` | object | Closes a building line following the anchors from its single point (on a segment between two anchors, see anchorLink), splitting it into buildings with the presets; returns the anchors of the loop |
| `lines.create` | `points: point[]`<br>`preset: string = null`<br>`type: string = null`<br>`dividing: int[] = null`<br>`followAnchors: bool = false`<br>`autoClose: object = null`<br>`details: bool = false` | object | Creates a building line through the points (dividing: the indices of the points splitting two buildings); autoClose: close it from its single point on a segment between two anchors (see anchorLink), splitting it into buildings with the presets. Returns the id, or with details {id, points: the point id given by each target, anchors: the anchors of the loop} |
| `lines.delete` | `line: ref` | - | Deletes a building line |
| `lines.removeLastPoint` | `line: ref` | object | Removes the last point of a building line (the line is deleted if left with one point) |
| `lines.removePoint` | `line: ref`<br>`point: int` | object | Removes a point from a building line (the line is deleted if left with one point) |
| `lines.toggleDividing` | `line: ref`<br>`point: int` | bool | Switches a point of a building line between dividing two buildings or not |

### `buildings.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `buildings.setAllSidesEqual` | `building: ref`<br>`equal: bool` | - | With all sides equal, the other sides of a building use the front one |
| `buildings.setEnabled` | `building: ref`<br>`enabled: bool` | - | Enables or disables a building of a line |

### `sides.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `sides.addBlock` | `side: ref` | int | Adds a block (of the first block type) to the dictionary of a building side, returns its index |
| `sides.addFloor` | `side: ref` | int | Adds an upper floor to a building side, returns its index |
| `sides.addSlice` | `side: ref`<br>`floor: int = -1` | int | Adds a slice to a floor of a building side (floor -1: the ground floor), returns its index |
| `sides.removeBlock` | `side: ref`<br>`block: int` | - | Removes a block from the dictionary of a building side |
| `sides.removeFloor` | `side: ref`<br>`floor: int` | - | Removes an upper floor of a building side |
| `sides.removeSlice` | `side: ref`<br>`floor: int`<br>`slice: int` | - | Removes a slice of a floor of a building side (floor -1: the ground floor) |

### `customElement.*`

Spline-driven custom elements keep kind `customElement`. Their point array is `spline`: for example `customElement.create(type="decal", points={"spline":[{"position":[0,0,0]},{"position":[0,0,10]}]})` in a core defining that type. Set curve parameters through `state.setParam`; allowed curve modes come from the core. Derived element data and export JSON include evaluated `spline.sections` and `spline.pieces`.

| Method | Parameters | Result | Description |
|---|---|---|---|
| `customElement.attachPoint` | `element: ref`<br>`to: point = null`<br>`path: string = null`<br>`vertex: int = null`<br>`line: int[] = null`<br>`column: int = null` | int | Replaces a control point (path) or a vertex (vertex) of a custom element with an existing terrain point (to: {terrainPoint}, shared from now on), or attaches it to an anchor or segment (to: {anchor} or {anchorLink}), to the line between two terrain points (line: [id, id], it stays where it is closest to it) or to the vertical line of a terrain point (column: id, it keeps its height and follows the x and z of that point; moved horizontally, that point follows) |
| `customElement.create` | `type: string`<br>`position: vec3 = null`<br>`points: any = null`<br>`mesh: object = null`<br>`state: object = null`<br>`container: ref = null`<br>`name: string = null` | int | Creates a custom element of a type of the core: its control points with the default layout around position, or as given in points (the structure of the control points of the type, each point a point target: position, terrainPoint to share an existing point, anchor, anchorLink...), its mesh (vertices: point targets, triangles: indices, uvs: [u, v] per vertex), its parameters (state, with propRule for the types with props containers, see customElement.setPropRule) and its container. Returns its id |
| `customElement.delete` | `element: ref` | - | Deletes a custom element (the elements it contains are left without a container); the ghost it replaces is deleted with the last element replacing it |
| `customElement.detachPoint` | `element: ref`<br>`path: string = null`<br>`vertex: int = null` | int | A control point (path) or vertex (vertex) of a custom element stops being shared with the other elements (it gets a copy of the point), or, if only the element uses it, stops following its anchor. Returns the id of the point |
| `customElement.insert` | `element: ref`<br>`array: string`<br>`index: int = null`<br>`position: vec3 = null` | object | Adds an element to an array of the control points (path: e.g. "sections" or "sections.2.lanes") at the index (the end by default), as with the Insert buttons: its points between the ones around it (or continuing the array); in a uniform array, every instance gets one. With position (only at the start or end of the array), the element is centered there and turned as the direction of the array changes, as with the Add at the start/end click modes. Returns the paths of the new points |
| `customElement.merge` | `from: ref`<br>`into: ref` | - | Moves the contents of a container into another one (of a type that can contain them all) and deletes it |
| `customElement.move` | `element: ref`<br>`offset: vec3 = null`<br>`position: vec3 = null` | int | Moves a custom element with its contents (and theirs) by an offset, or by the offset from the center of their points to a position, as with the move handle of a container: the points shared with other elements move too, the points on the anchors of roads and intersections stay there (the ones on the anchors of a ghost leave them and move), the points on a line follow its ends. Returns the number of points moved |
| `customElement.movePoint` | `element: ref`<br>`position: vec3`<br>`path: string = null`<br>`vertex: int = null`<br>`rotation: vec3 = null` | - | Moves a control point (path) or a vertex of the mesh (vertex) of a custom element, as with the gizmo (the constraints of the type run at the next update; a point on an anchor leaves it); rotation: the euler angles of a rotatable point |
| `customElement.remove` | `element: ref`<br>`array: string`<br>`index: int` | - | Removes the element of an array of the control points at the index (in every instance of a uniform array) |
| `customElement.removePoint` | `element: ref`<br>`path: string = null`<br>`vertex: int = null` | - | Removes a control point (path) or a vertex of the mesh (vertex, with its triangles) of a custom element, as with the Delete key: a control point takes with it the element of the outermost editable array containing it (e.g. the section of a lane point; customElement.remove removes from a given array). The point is deleted if no other element uses it |
| `customElement.setContainer` | `element: ref`<br>`container: ref = null` | - | Puts a custom element in a container (another custom element whose type can contain it), or out of any container (container: null) |
| `customElement.setMesh` | `element: ref`<br>`mesh: object` | - | Replaces the mesh of a custom element: vertices (point targets), triangles (indices), uvs ([u, v] per vertex) |
| `customElement.setPropRule` | `element: ref`<br>`rule: object = null` | - | Sets the prop rule of a custom element of a type with props containers (rule: {container: props container state, as in the prop rules of the roads}, null to remove it); the props are placed along the prop lines of its geometry |
| `customElement.setUV` | `element: ref`<br>`vertex: int`<br>`uv: vec2` | - | Sets the texture coordinates of a vertex of the mesh (types with editable uvs) |

### `propPaths.*`

Standalone rules are kind `propPath`. They reuse the custom element point paths shown by `elements.get(derived: true)`, such as `ribbons.0.segments.0.sections.1.start`. The `road` field of a props-container/element reference also accepts a prop-path owner.

| Method | Parameters | Result | Description |
|---|---|---|---|
| `propPaths.create` | `ribbons: object`, `rule: object = null`, `name: string = null` | int | Creates a rule; ribbons map container names to segments, each a list of `{start, end, reference?}` point targets. References must cover the whole segment if supplied. |
| `propPaths.setRibbons` | `element: ref`, `ribbons: object` | - | Replaces ribbon geometry. |
| `propPaths.setRule` | `element: ref`, `rule: object` | - | Replaces rule state, retaining ribbons and creating defaults for new containers. |
| `propPaths.move` | `element: ref`, `offset: vec3 = null`, `position: vec3 = null` | int | Moves the rule, respecting shared/anchored points. |
| `propPaths.delete` | `element: ref` | - | Deletes the owner and generated props. |
| `propPaths.insert` / `remove` | `element: ref`, `array: string`, `index: int` (optional for insert), `position: vec3 = null` (insert only) | object / - | Adds/removes segments or paired sections using point-array paths. |
| `propPaths.renameRibbon` / `removeRibbon` | `element: ref`, `index: int`, `name: string` (rename only) | - | Updates ribbon geometry and its container together. At least one ribbon remains. |
| `propPaths.movePoint` | `element: ref`, `path: string`, `position: vec3` | - | Moves a ribbon point. |
| `propPaths.attachPoint` | `element: ref`, `path: string`, `to: point = null`, `line: int[] = null`, `column: int = null` | int | Shares/anchors a ribbon point. |
| `propPaths.detachPoint` | `element: ref`, `path: string` | int | Detaches a ribbon point. |

```json
{"method":"propPaths.create","params":{"ribbons":{"main":[[
  {"start":{"position":[0,0,0]},"end":{"position":[2,0,0]}},
  {"start":{"position":[0,0,20]},"end":{"position":[2,0,20]}}
]]},"rule":{"main":{"type":"line","element":{"type":"line","mesh":"geometry/prop.obj","start":0,"elemDistance":5,"maxNumber":10,"xPos":0.5}}}}}
```

The example's prop types are supplied by the core. A rule with no enabled container remains a selectable ribbon.

`customElement.import(data)` applies the `points`/`elements` payload described in [Core Custom Elements](Core-Custom-Elements#converters), returning `{points: {localId: apiId}, elements: {localId: apiId}}`. It checks local IDs, references, meshes and cyclic attachments before applying. `updates`, `replace` and procedural `source` are forbidden. The normal 1 cm welding/anchor behavior applies; `weld: false` preserves a distinct new point. One local ID used repeatedly always shares that point. The built-in prop-path type is accepted as well as core custom element types.

`state.import(ref, values, append: string[] = null)` imports core parameter state, including nested arrays/custom city properties and object transforms. It normalizes numeric values against the core schema; `append` names arrays whose existing contents are retained. Other supplied parameters replace their current values. This is an undoable API for importer scripts; use `state.setParam` for ordinary scalar edits.

### `converters.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `converters.list` | - | object | The converters of the core: name, label, the kinds and types they convert |

### `ghosts.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `ghosts.list` | - | object | The ghosts: the roads and intersections replaced by custom elements, with the elements replacing them |
| `ghosts.restore` | `element: ref` | - | Brings back a ghost: the custom elements replacing it are deleted |
| `ghosts.set` | `element: ref`<br>`ghost: bool = true` | - | Makes a road or intersection a ghost (hidden, not exported, kept for the elements connected to it), or a normal element again (ghost: false) |

### `meshes.*`

Every mesh instance has a mesh instance type (see [Core Custom Elements](Core-Custom-Elements#mesh-instance-elements)), in `settings.type`: it is the `type` of the element in `elements.list` and `elements.get`, and `types.list` with kind `meshInstance` lists the types with their parameters, `massPlacement`, `randomRotation` and `compatibleTypes`.

| Method | Parameters | Result | Description |
|---|---|---|---|
| `meshes.create` | `mesh: string`<br>`position: vec3`<br>`type: string = null`<br>`settings: state = null`<br>`rotation: vec3 = null`<br>`scale: vec3 = null`<br>`randomRotation: bool = false`<br>`onGround: bool = false` | int | Places a mesh instance of a mesh instance type (type: optional if the core has only one, or if the settings have it; onGround: on the layers the instances of the type are placed on) |
| `meshes.delete` | `mesh: ref` | - | Deletes a mesh instance |
| `meshes.eraseInRadius` | `center: vec3`<br>`radius: number`<br>`type: string = null`<br>`settings: state = null` | int | Deletes the mesh instances of the type within the radius (like the delete mode of the mesh tool; only for the types with massPlacement), returns how many |
| `meshes.projectToGround` | `mesh: ref` | - | Puts a mesh instance on the ground under it |
| `meshes.replaceMesh` | `mesh: ref`<br>`newMesh: string` | int | Replaces the mesh of a mesh instance, keeping its transform (and id) |
| `meshes.scatter` | `mesh: string`<br>`center: vec3`<br>`radius: number`<br>`count: int`<br>`type: string = null`<br>`settings: state = null`<br>`randomRotation: bool = false` | int[] | Places count mesh instances at random points within the radius, on the ground (like the multiple placement of the mesh tool; only for the types with massPlacement) |
| `meshes.setType` | `mesh: ref`<br>`type: string` | - | Switches a mesh instance to one of the compatible types of its type (compatibleTypes): the parameters of both types are kept |
| `meshes.setTransform` | `mesh: ref`<br>`position: vec3 = null`<br>`rotation: vec3 = null`<br>`quaternion: quaternion = null`<br>`scale: vec3 = null` | - | Sets the position, rotation (euler angles in degrees, or quaternion) and scale of a mesh instance |

### `objects.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `objects.delete` | `object: ref` | - | Deletes the array element of a parametric object (the other parametric objects are generated by their element and can't be deleted) |
| `objects.get` | `object: ref` | object | A parametric object: its mesh and its transform relative to its base |
| `objects.projectToGround` | `object: ref` | - | Puts a parametric object on the ground under it |
| `objects.setMesh` | `object: ref`<br>`mesh: string` | - | Replaces the mesh of a parametric object (if it allows a custom one) |
| `objects.setTransform` | `object: ref`<br>`localPosition: vec3 = null`<br>`localRotation: vec3 = null`<br>`localScale: vec3 = null` | - | Sets the transform of a parametric object relative to its base (rotation: euler angles in degrees), on the axes it allows |

### `props.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `props.element.meshes.add` | `element: ref`<br>`mesh: string` | - | Adds a mesh to the meshes of a props element |
| `props.element.meshes.remove` | `element: ref`<br>`index: int` | - | Removes a mesh from a props element |
| `props.element.meshes.set` | `element: ref`<br>`index: int`<br>`mesh: string` | - | Replaces a mesh of a props element |
| `props.element.setMesh` | `element: ref`<br>`mesh: string` | - | Sets the mesh of a props element with a single one |
| `props.elements.add` | `container: ref`<br>`list: string` | int | Adds a props element (the first preset of an allowed type) to a list of a props container, returns its index (-1 for the lists with a single element) |
| `props.elements.remove` | `container: ref`<br>`list: string`<br>`index: int` | - | Removes a props element from a list of a props container |

### `terrain.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `terrain.brush` | `mode: string`<br>`position: vec3`<br>`range: number = 3`<br>`intensity: number = 1`<br>`height: number = null` | - | One step of the terrain brush at the position: mode raise, lower or level (to the height under the center, or to height) |
| `terrain.height` | `x: number`<br>`z: number` | number | The height of the ground (terrain and generated meshes) under a point |
| `terrain.heights.get` | `x: int`<br>`z: int`<br>`width: int`<br>`depth: int`<br>`encoding: string = 'base64'` | object | The heights (in meters) of an area of the heightmap grid (see terrain.info), row by row along z; encoding: base64 (little endian float32) or array (a list of rows) |
| `terrain.heights.set` | `x: int`<br>`z: int`<br>`width: int`<br>`depth: int`<br>`heights: any`<br>`encoding: string = 'base64'` | object | Sets the heights (in meters) of an area of the heightmap grid, as terrain.heights.get gives them; returns how many were out of the range of the terrain and clamped |
| `terrain.importHeightmap` | `file: string`<br>`scale: number = 1` | object | Sets the terrain heights from a grayscale image (in the city or extra folders), scale: the height of white |
| `terrain.info` | - | object | The terrain settings, and the heightmap grid (gridSize samples per side, pixelSize apart; the sample x, z is at x * pixelSize + origin, z * pixelSize + origin) |

### `project.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `project.settings.get` | - | object | The settings of the city: extra asset folders (of the city, and global) and ground overlay texture |
| `project.settings.set` | `extraFolders: string[] = null`<br>`overlayTexture: string = null` | object | Changes the settings of the city: extra asset folders of the city and ground overlay texture |

### `script.*`

Only while a Python script is running (see [Python Scripts](Python-Scripts)); otherwise they fail with `invalid_operation`.

| Method | Parameters | Result | Description |
|---|---|---|---|
| `script.alert` | `text: string`<br>`title: string = null` | - | Shows a message when the running script ends |
| `script.inputs` | - | any | The values entered in the input panel of the running script, by parameter name (enums as indices, arrays as lists of objects) |
| `script.log` | `text: string`<br>`level: string = 'info'` | - | Writes a line in the log of the program; level: info, warning or error |
| `script.progress` | `value: number`<br>`text: string = null` | - | Shows the progress of the running script (value from 0 to 1), with a text (null: keeps the last one) |
| `script.selection` | - | int[] | The ids of the elements selected or being edited when the running script was started |

### `view.*`

See [Images](#images).

| Method | Parameters | Result | Description |
|---|---|---|---|
| `view.camera.get` | - | object | The camera of the editor: position, forward direction, fov (vertical, degrees) |
| `view.camera.set` | `position: vec3`<br>`target: vec3` | object | Moves the camera of the editor to the position, looking at the target |
| `view.focus` | `ref: ref` | - | Moves the camera of the editor to show an element (as the zoom of the object list) |
| `view.render` | `camera: string = 'top'`<br>`elements: ref[] = null`<br>`bounds: number[] = null`<br>`center: vec3 = null`<br>`size: number = null`<br>`margin: number = 1.1`<br>`pitch: number = 50`<br>`yaw: number = 30`<br>`distance: number = null`<br>`position: vec3 = null`<br>`target: vec3 = null`<br>`fov: number = 50`<br>`width: int = 1024`<br>`height: int = 1024`<br>`layers: string[] = null`<br>`labels: string[] = null`<br>`labelSize: number = 14`<br>`highlight: ref[] = null`<br>`highlightColor: string = null`<br>`overlays: object[] = null`<br>`grid: number = 0`<br>`scaleBar: bool = false`<br>`background: string = null`<br>`transparent: bool = false`<br>`bestLod: bool = true`<br>`wireframe: bool = false`<br>`format: string = 'png'`<br>`quality: number = 0.9`<br>`path: string = null` | object | Renders the city offscreen and returns the image with the mapping between its pixels and the city coordinates, and the pixel positions of the labels and overlays |
| `view.screenshot` | `format: string = 'png'`<br>`quality: number = 0.9`<br>`path: string = null` | object | The view of the editor as the user sees it (with its panels) |
| `view.setTerrainHidden` | `hidden: bool` | object | Hides or shows the terrain (View->Hide terrain grid): a hidden terrain isn't drawn and the clicks and the placement snaps skip it, while the roads and the projected geometry still follow it; saved with the city preferences, not an undo step |
| `view.setToggle` | `name: string`<br>`on: bool` | object | Turns a visibility toggle of the core (see view.toggles) on or off; saved with the city preferences, not an undo step |
| `view.terrainHidden` | - | object | Whether the terrain is hidden (View->Hide terrain grid) |
| `view.toggles` | - | object | The visibility toggles of the core (in the View menu) and whether they're on |

### Dispatcher

| Method | Parameters | Result | Description |
|---|---|---|---|
| `batch` | `commands: [{method, params}]`<br>`atomic: bool = true` | list | Runs the commands as a single undo step (see [Batches](#batches)) |
| `api.methods` | - | list | Every method with its parameters |

### Core-defined spline choices

Road curve choices are filtered by their core type. In `state.schema`/`types.list`, the curve builtin has parallel `enumValues` (stable names) and `enumIds` (saved IDs), which need not be consecutive. `state.setBuiltin` accepts either. Existing `bezier`, `hermite` and `lowPoly` names keep their IDs 0?2. Custom element spline types continue to use an integer `curveType` parameter. Custom road split/merge calls use core callbacks and reject unsupported operations. See [Core Spline Types](Core-Splines).

Exporter JSON retains authored custom element spline controls in `points.spline` and road controls in `b64v3a_points` (`points` after decoding); these are distinct from evaluated preview samples.

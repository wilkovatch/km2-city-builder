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

### Self-test

```
Godot_v4.7.1-stable_mono_win64_console.exe --path . [--headless] -- selftest api <new folder>
```

Creates a city in the folder (from the SF template of the Midtown Madness 2 core) and drives it only with JSON commands: every kind of element, anchors, terrain points and heights, parameters, arrays, presets, props, undo and redo of each change, batches, transactions, errors, a script changing an element open in the editor, parity with the elements made with the editor tools, whole city generation, save and load, export JSON, geometry and the core exporter. It also contains the whole city terrain and building generation written with the commands only (`ScriptTerrain`, `ScriptBuildings`), checked against `city.generateTerrain` and `city.generateBuildings`: they are the reference for scripts generating cities. The same generators written in Python (the `cityTerrain` and `cityBuildings` examples of `wiki/examples/scripts`, see [Python Scripts](Python-Scripts#examples)) are run through `ScriptRunner` and checked in the same way, along with the runner itself (inputs, selection, single undo step, failures, cancelling, settings restored) and the input panel of the scripts. It writes `api_selftest.log` next to the folder and exits with code 1 if anything failed.

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
| `{"parent": <ref>, "param": "trafficCars", "index": 0}` | an element of an array parameter (no `index`: a custom element parameter) |
| `{"owner": <ref>, "object": "trafficLight", "path": ["races.0"]}` | a parametric object (an `objectInstance` parameter) of a road or of the city properties; `path` lists the array elements leading to it, from the owner |

The kinds are `road`, `intersection`, `terrainPatch`, `buildingLine`, `meshInstance`, `terrainPoint`, `cityProperties`, and for the parts of elements `building`, `buildingSide`, `buildingBlock`, `borderMesh`, `propContainer`, `propElement`, `arrayElement`, `customElement`, `parametricObject`.

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

The points of terrain patches and building lines (in `elements.get` with `derived`) tell what they are attached to: `anchor` (the anchor they are on) or `segment` (`{anchor, percent}` or `{points, percent}`). `terrainPoints.move` moves them as the gizmo does: a point on a segment slides along it, a point on an anchor can't be moved.

### Anchors

The anchors are the points along the edges of the roads and intersections where terrain patches and building lines attach. They are referenced as `{"of": <road or intersection>, "index": i}`, the index among the anchors of the element. The anchors of a road or intersection can be created again when it changes (e.g. its points or its width), so a script reads them again after changing roads. Each anchor is linked to the previous and next one (`prev`, `next`), across the elements too: around a block they form a closed loop, which is what the automatic closing follows.

- `anchors.list` gives every anchor of the city, in the order of the whole city generators, with its position, `insideDirection`, `prev`, `next` and `usedBy` (the patch and line points on it, or on its segment with `segment: true`). With `unusedBy` (`terrainPatch` or `buildingLine`), only the anchors with no point of that kind on them: the anchors the generators start from.
- `anchors.loop` gives the anchors from one to another along the edges (without `to`: the loop around the block, ending with the anchor itself).
- `anchors.segment` gives the segment from an anchor to the next one (null at the end of an edge), where `anchorLink` points go.
- `patches.autoClose` and `lines.autoClose` return the anchors of the loop.

### Terrain heights

The heightmap is a grid of `gridSize` × `gridSize` samples (`terrain.info`), `pixelSize` meters apart: the sample `(x, z)` is at `(x * pixelSize + origin, z * pixelSize + origin)` in the city. `terrain.heights.get` and `terrain.heights.set` read and write the heights (in meters) of an area of the grid, row by row along z, as a list of rows (`encoding: "array"`) or as base64 little endian 32 bit floats (`"base64"`, the default, much smaller). The heights are limited to the range of the terrain (`-maxHeight` to `maxHeight`); `set` returns how many were clamped. A change is one undo step, and the elements on the ground follow the new heights.

### Geometry

`elements.geometry` gives what is generated for an element (road, intersection, terrain patch, building line with its buildings, mesh instance or parametric object) in the format of the export JSON read by the exporters of the cores: meshes as base64 arrays (`b64v3a_vertices`, `b64ia_indices`...), with the material of each submesh, the lanes and props of the roads, the splines and sides of the buildings. `materials` and `meshes` have the entries of the material and mesh dictionaries it references. The roads and intersections are referenced by their ids (in the export file, by their index). The encoding is described in [Input and Output](Input-and-Output#export-format-ioexportedcity-ioexportedcitycs-and-cityencoderjson_export); `Files/python/DecodeExportedJson.py` decodes it.

### Values

Vectors are `[x, y, z]` (or `{"x": ..., "y": ..., "z": ...}`), quaternions `[x, y, z, w]`, rotations of mesh instances euler angles in degrees. Everything is in the program coordinates: left handed, Y up, +Z forward, the same as the saved and exported cities.

States (the values of an element) are plain JSON objects, as in the saved city. The values of the parameters of the core are set with `state.setParam`, by the type of the parameter: `bool`, `int`, `float`, `string`, `texture` and `mesh` (relative paths), `enum` (the index, or the name of the value). The arrays and custom elements are edited with `state.array.*`, `state.customElement.ensure` and their references, the parametric objects with `objects.*`. The properties the editor panels have for every type (the curve type of a road, the height of a building, the floors of a building side...) are set with `state.setBuiltin`; `state.schema` lists both for an element.

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

`city.new`, `city.load`, `city.reload`, `city.save`, `city.exportJson`, `city.export`, `city.generateTerrain`, `city.generateBuildings`, `city.rotate` and `terrain.importHeightmap` last several frames and report their progress with events. No other command can run meanwhile (`busy`).

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
| `io_error`, `export_failed`, `batch_failed` | the operation failed (see the message) |
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
| `city.export` | `path: string`<br>`exporter: string`<br>`flags: {id: bool} = null`<br>`showConsole: bool = false` | object | Exports the city with an exporter of the core (by its format, see exporters.list), with the given flags (the others keep their default) |
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

| Method | Parameters | Result | Description |
|---|---|---|---|
| `presets.delete` | `category: string`<br>`name: string` | - | Deletes a preset (not the default one); the roads, intersections, building lines, buildings, building sides and building blocks using it are unlinked from it |
| `presets.get` | `category: string`<br>`name: string` | state | The values of a preset |
| `presets.list` | `category: string` | string[] | The names of the presets of a category (road, intersection, buildingLine, building, buildingSide, buildingBlock, terrainPatchBorderMesh, propContainer, propElem, roadPropRule, or a container such as rail); the first one is the default |
| `presets.save` | `category: string`<br>`name: string`<br>`values: state` | - | Saves a preset with the given values (overwriting the one with the same name) |
| `presets.sync` | `category: string`<br>`name: string` | - | Gives the current values of a preset to the roads, intersections, building lines, buildings, building sides or building blocks linked to it |

### `assets.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `assets.list` | `kind: string`<br>`regex: string = null` | string[] | The textures or meshes available to the city (its folder and the extra folders), optionally filtered with a regular expression |

### `exporters.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `exporters.list` | - | object | The exporters of the current core, with their flags |

### `elements.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `elements.geometry` | `ref: ref` | object | The generated geometry of a road, intersection, terrain patch, building line (with its buildings), mesh instance or parametric object, as in the export JSON (base64 arrays), with the materials and meshes it references |
| `elements.get` | `ref: ref`<br>`derived: bool = false` | object | An element as stored in the saved city (ids as references); derived: also the data computed from it (curve, anchors, buildings...) |
| `elements.list` | `kind: string = null`<br>`name: string = null`<br>`type: string = null` | list | The elements of the city (optionally of a kind, with a name or of a type): id, kind, name, type |
| `elements.rename` | `ref: ref`<br>`name: string` | object | Renames an element |
| `elements.runtimeValue` | `ref: ref`<br>`variable: string`<br>`valueType: string = 'float'` | object | The value of a definition of the type of a road (as computed for its last update) |

### `anchors.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `anchors.list` | `unusedBy: string = null` | list | Every anchor of the city (in the order of the city-wide generators): owner, index, position, previous and next anchor, the points attached to it; unusedBy (terrainPatch or buildingLine): only the anchors no point of that kind is on |
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
| `state.customElement.ensure` | `ref: ref`<br>`param: string` | - | Creates the state of a custom element parameter if missing |
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
| `roads.extend` | `road: ref`<br>`point: point` | - | Adds a point at the end of a road, as with the road tool in add mode (a road end, an intersection or the middle of a road connect the road there) |
| `roads.insertPoint` | `road: ref`<br>`index: int`<br>`position: vec3`<br>`onGround: bool = false` | - | Inserts a control point at the index, as with the road tool in insert mode |
| `roads.split` | `road: ref`<br>`near: vec3` | object | Splits a road at the point of its free part (outside the intersections at its ends) closest to near, as with the road tool in split mode: the road keeps the first part, a new road with the same properties gets the second one, and a new intersection connects them. Returns {newRoad, intersection} |
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

### `meshes.*`

| Method | Parameters | Result | Description |
|---|---|---|---|
| `meshes.create` | `mesh: string`<br>`position: vec3`<br>`settings: state = null`<br>`rotation: vec3 = null`<br>`scale: vec3 = null`<br>`randomRotation: bool = false`<br>`onGround: bool = false` | int | Places a mesh instance (onGround: on the layers the mesh instances with these settings are placed on) |
| `meshes.delete` | `mesh: ref` | - | Deletes a mesh instance |
| `meshes.eraseInRadius` | `center: vec3`<br>`radius: number`<br>`settings: state = null` | int | Deletes the mesh instances on the layer of the settings within the radius (like the delete mode of the mesh tool), returns how many |
| `meshes.projectToGround` | `mesh: ref` | - | Puts a mesh instance on the ground under it |
| `meshes.replaceMesh` | `mesh: ref`<br>`newMesh: string` | int | Replaces the mesh of a mesh instance, keeping its transform (and id) |
| `meshes.scatter` | `mesh: string`<br>`center: vec3`<br>`radius: number`<br>`count: int`<br>`settings: state = null`<br>`randomRotation: bool = false` | int[] | Places count mesh instances at random points within the radius, on the ground (like the multiple placement of the mesh tool) |
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

### Dispatcher

| Method | Parameters | Result | Description |
|---|---|---|---|
| `batch` | `commands: [{method, params}]`<br>`atomic: bool = true` | list | Runs the commands as a single undo step (see [Batches](#batches)) |
| `api.methods` | - | list | Every method with its parameters |

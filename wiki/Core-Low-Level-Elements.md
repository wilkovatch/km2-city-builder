# Core Low-Level Elements

The procedural elements (roads, intersections, terrain patches, building lines) generate their geometry from a few parameters, which keeps them easy to edit but limits how finely they can be tuned. A core can also define **low-level elements**: elements that map directly to the primitives of its game (for Midtown Madness 2, the attributes of the rooms of the PSDL file), made of control points, an optional mesh and parameters, whose geometry is built by a Lua script of the core.

A procedural element is turned into low-level elements by a **converter**: a Python script of the core that receives the element as it would be exported and returns the low-level elements that replace it. The roads and intersections converted stay in the city as hidden **ghosts**, so that the elements connected to them keep their geometry.

```
<CoreName>/
├── elements/lowLevel/<type>/     one folder per type (optional folder)
│   ├── settings.json
│   ├── parameters.json
│   └── ui.json
├── lua/                          the geometry and constraint functions of the types
└── converters/<name>/            one folder per converter (optional folder)
    ├── converter.json
    └── convert.py
```

The types can extend each other as the other folder types (see [Core Type Inheritance](Core-Type-Inheritance)); the category is `lowLevel`.

## Types

### `settings.json`

```json
{
    "controlPoints": [
        { "name": "sections", "label": "LL_SECTIONS",
          "array": { "min": 2, "defaultCount": 2, "defaultStep": [0, 0, 10] },
          "children": [
              { "name": "left", "label": "LL_LEFT_EDGE", "defaultPosition": [-4, 0, 0] },
              { "name": "right", "label": "LL_RIGHT_EDGE", "defaultPosition": [4, 0, 0] }
          ] }
    ],
    "mesh": null,
    "container": null,
    "geometry": "psdl.roadWithoutSidewalks",
    "constraints": null,
    "creatable": true,
    "propsContainers": ["right", "left"],
    "propsPresetType": "psdl"
}
```

| Key | Description |
|---|---|
| `controlPoints` | The control points (see below); omitted for spline-driven types. |
| `spline` | Optional road-style spline settings (see [Spline-driven elements](#spline-driven-elements)); cannot be combined with `controlPoints`, `mesh` or a Lua `geometry` function. |
| `createMenu` | `{ "direct": true }` lists the type directly using its `ui.json` label. Otherwise `{ "group": "STRING_KEY" }` groups it with types using the same localized group key. Missing: the generic Low-level element group. The creation panel lists only types in the selected group. |
| `geometryLayer` | Collision layer index for generated geometry (default `7`). A projected overlay can use another layer and omit that layer from its projection mask, so overlays do not project onto each other. |
| `mesh` | Set if the element has a mesh: `uvs` (`"none"`, `"editable"`: stored with the element and edited in its panel, `"script"`: given by the geometry script), `defaultVertices` (offsets from where a new element is created) and `defaultTriangles`. |
| `container` | Set if the elements of the type can contain other low-level elements: `contains`, the types they can contain. |
| `geometry` | `<module>.<function>` of the `lua` folder: builds the geometry shown in the editor (see [Scripts](#scripts)). |
| `constraints` | `<module>.<function>`: moves points after others were moved (see [Scripts](#scripts)). |
| `derivedPoints` | `{flag, function}`: while the bool parameter `flag` is on, the control points are computed by `function` (`<module>.<function>`) from the other low-level elements converted from the same element, and can't be edited (see [Derived points](#derived-points)). |
| `creatable` | Listed in Create (default `true`); `createMenu` selects a direct entry or a group. |
| `propsContainers` | The props containers of the prop rule of the elements (`propRule` in their state, as the prop rules of the roads, see [Core Props and Objects](Core-Props-and-Objects)): the props are placed along the `propLines` of the geometry (see [Geometry](#geometry)) and exported with the element. |
| `propsPresetType` | The prop rule presets listed in the panel: the ones of this type (e.g. a road type with the same containers); all if not set. |

#### Control points

Each node of `controlPoints` is a point, or a group of points if it has `children`. A node with `array` is repeated (a list of points, or of groups):

| Key | Description |
|---|---|
| `name` | Unique among its siblings, without dots. |
| `label` | String key shown in the editor. |
| `children` | The nodes of a group. |
| `array` | `min`, `max` (0: no limit), `editable` (default `true`: the user can add and remove elements; otherwise only the converters and the API can), `uniform` (an array in an element of another array: every element of that array has as many elements in it, adding one adds one everywhere), `closed` (a loop, e.g. a perimeter: the element after the last one is the first one, so an element added at an end goes between the last and the first one), `defaultCount` and `defaultStep` (a new element: how many elements, and the offset between them). |
| `rotatable` | The point can be rotated with the gizmo; its rotation is given to the scripts and exported. |
| `defaultPosition` | The offset of the point from where a new element is created. |

The points are **terrain points**: like the points of the terrain patches and building lines they can be attached to the anchors of roads and intersections, to the segment between two points, or shared with other elements (the same point used by a terrain patch and by two low-level elements moves all of them). Unlike the points of the patches, a point on an anchor can still be moved with the gizmo: it then leaves the anchor (and stays shared). The vertices of the mesh are terrain points too.

Besides the anchors along their edges, the intersections have an anchor line across the end of each road whose type has `endAnchors` (see [Core Roads](Core-Roads#settingsjson)), only for the points of the low-level elements: a low-level road converted from a road keeps all the points of its end section on the intersection, and they follow it when it moves or changes. These anchors are shown only by the click modes of the low-level editor, and come after the other anchors of the intersection, so the indices of those don't change.

Two more anchors keep points of different elements in place with each other (e.g. the parts of a building):
- **on a line**: the point follows the line between two other terrain points (at the same fraction of it); moved, it slides along it.
- **on a vertical line**: the point follows the x and z of another terrain point (its master) and keeps its own height (also when the constraints of an element change it). Moved horizontally, it moves the master, and so every point on the same vertical line; moved vertically, only its height changes.

A point can't follow itself through these anchors (directly or through others).

A point is named by its **path**: the names of the nodes joined by dots, with the index (from 0) of the element in each array, e.g. `sections.2.left`.

### `parameters.json` and `ui.json`

As for the other types (see [Parameters and UI](Parameters-and-UI)). The parameters have no `instanceSpecific` (the elements have a single state). The **reference** parameter type is meant for these elements: its value is another element of the city (`referenceTypes`: the kinds it can reference, `road`, `intersection`, `lowLevel` or `lowLevel/<type>`), stored as `{"kind": ..., "id": ...}`. In the panel it is a reference field (the element is picked with a click, see [Parameters and UI](Parameters-and-UI)), shown where `ui.json` lists it. A reference to a ghost is kept as is: the exporter resolves it to the elements replacing the ghost.

The panel has these built-in groups, used by name in `ui.json`:

| Group | Content |
|---|---|
| `mainGroup` | End editing, delete, the type, and (for an element replacing a ghost) restoring the original element. |
| `pointsGroup` | The path of the selected point (and whether it is shared or anchored), the click mode, inserting an element before or after the one of the selected point in its array, removing it (the buttons name the array, and are disabled without a selected point in an editable array; for a point in arrays inside arrays they act on the outermost one, e.g. a lane point inserts or removes a whole section, as the Delete key does), adding an element after the one of the selected point to the innermost array containing it or removing it from there (e.g. a lane, in every section of a uniform array; these buttons are there only for the types with arrays inside arrays, and enabled only when the selected point is in an inner array), detaching the selected point (it stops being shared, or anchored). The click modes: add points at the end of an editable array of points (clicking a point already in it adds nothing, clicking the first point of a closed array ends the mode; in a uniform array, every instance gets a point: the clicked one in the instance whose element, e.g. the section, is the nearest to the click, a new one at the same offset from the center of the other elements, turned as their direction across, from their first to their last point, is; so the first lane of an empty uniform array can be added too); add an element at the end or at the start of an editable array of groups (e.g. a section of a road: a copy of the element at that end, centered where clicked and turned around the vertical axis as the direction of the array changes); attach the selected point to the clicked point (shared from then on) or anchor; attach it to the line between the two points clicked next; attach it to the vertical line of the point clicked. |
| `meshGroup` | The texture coordinates of the selected vertex (`uvs: "editable"`). |
| `containerGroup` | The container of the element, with a button to edit it and a *Change* button listing the other containers it can be in (and none) to move it to one with *Change to*; for a container, the list of its contents (the one picked is highlighted: outlined in cyan, with its points selected, and edited with *Edit*) and a *Merge into...* button listing the containers that can hold all its contents, to merge it into one with *Merge* (its contents move there and it is deleted). The lists are shown in place of the editor, which comes back after the action or *Cancel*. |
| `propsGroup` | The prop rule (types with `propsContainers`): its preset, and for each container a checkbox enabling it and a button editing it, as in the road editor. |

While a container is edited (in the None click mode) a cyan handle at the center of its points and of the points of its contents (and theirs) moves them all together, with the gizmo or the transform panel, as a single undo step. The points shared with other elements move too (the other elements follow), the points on the anchors of roads and intersections stay there (but not the ones on the anchors of a ghost, e.g. the sides of a room converted from a road: they leave them and move; the anchors across the end of a road that isn't a ghost still count), the points on a line follow its ends and the points on a vertical line keep following it (their height changes with the move). The scripting API does the same with `lowLevel.move`.

`convert:<converter>` adds a button converting the element with a converter of the core (shown only if the converter accepts it). It can be used in the `ui.json` of any type, procedural or low-level.

## Scripts

The functions are called as the [script definitions](Core-Lua-Scripts), with `v` (the parameters and static definitions of the type) and `ctx`:

| `ctx` | Content |
|---|---|
| `typeName`, `name` | The type and the name of the element. |
| `points` | The control points: a table with the structure of `controlPoints` (groups are tables of named nodes, arrays are lists starting from 1, points are `vec3`). |
| `rotations` | The rotations of the rotatable points (`vec3`, euler angles in degrees), with the same structure. |
| `vertices`, `triangles`, `uvs` | The mesh: a list of `vec3`, a flat list of indices (from 0) of the vertices, three per triangle, and the editable texture coordinates (`vec2`). |
| `references` | The low-level elements referenced by the parameters, by parameter name: `typeName`, `name`, `points`, `vertices`, `triangles`. The element is rebuilt when their points move. |
| `moved` | Constraints only: what moved since they last ran, `points` (a table with the structure of `controlPoints`, `true` for the points that moved) and `vertices` (`[index] = true`). |
| `siblings` | Derived points only: the other low-level elements converted from the same procedural element (the same `source`), in the order of their ids: `typeName`, `name`, `points`, `vertices`, `triangles`. |

### Geometry

The geometry function returns `{meshes, lines, propLines, uvs}`:

| Key | Content |
|---|---|
| `meshes` | A list of meshes: `texture` (a texture parameter of the element, or a built-in material such as `_TRANSPARENT_`; a mesh without texture is not drawn), `vertices`, `uvs`, `triangles` (indices from 0); `pickOnly = true`: not drawn, only to select the element by clicking on it; `placeholder = true`: drawn (and clickable) only while none of the contents of the element draws a mesh, e.g. a transparent surface so that a container that would otherwise be invisible can be clicked (not exported). Triangles are clockwise when seen from their visible side. |
| `lines` | A list of lines: `points`, `color` (`vec3` or a name: yellow, red, green, blue, cyan, magenta, white, black, orange), `width` (meters; without it the line keeps about the same width on screen), `texture`, `heightOffset`, `closed`, `always` (shown also when the element isn't edited; only these lines can be clicked to select it), `toggle` (the name of a `viewToggles` entry of the core settings: while it's off an `always` line is shown only when the element is edited, and can't be clicked), `withContainer` (also shown while the container of the element, or another element in it, is edited, e.g. the traffic lanes of a room), `traffic` (also shown by **View > Show all traffic lanes**, with the lanes of the roads, until they are hidden again). |
| `propLines` | The lines along which the props of the prop rule are placed (types with `propsContainers`): `container` (its name), `startBound` and `endBound` (two lines with as many points, as the `bounds` of the `propsLines` of the roads: the props are placed between them). |
| `uvs` | The texture coordinates of the vertices of the mesh of the element, exported with it (`uvs: "script"`). |

The geometry is shown in the editor only; what the exporter writes is up to the core.

### Constraints

The constraints function runs after points of the element moved (by the user, a script, an anchor, or the constraints of another element sharing them), and returns the new positions of other points: `{points = <the structure of controlPoints with only the points to move>, vertices = {[index] = vec3}}`. Points attached to an anchor are not moved. Since moving a shared point can trigger the constraints of the other elements using it, the constraints run a few times per update, until no point moves.

```lua
-- the vertices of a roof are all at the height of the vertex moved
function M.flatMesh(v, ctx)
    for i = 1, #ctx.vertices do
        if ctx.moved.vertices[i] then
            local y = ctx.vertices[i].y
            local res = {}
            for j, p in ipairs(ctx.vertices) do res[j] = vec3(p.x, y, p.z) end
            return { vertices = res }
        end
    end
    return {}
end
```

### Derived points

The `derivedPoints` function computes all the control points of an element from its siblings (e.g. a BAI road from the PSDL attributes of the road it was converted with), and returns `{points = <the whole structure of controlPoints>}` (the arrays can have a different number of elements, within their `min` and `max`, and the uniform ones the same number everywhere), or `nil` to leave the points as they are (e.g. no sibling to compute from). It runs:

- when the flag is turned on;
- when a point of the element or of a sibling moves, or a sibling gets or loses points, after the constraints (which run again if the derived points changed);
- not when the element is loaded, converted or restored by undo/redo: the points stay as they were saved until something changes (so the export is the same until then).

The change is part of the undo step of the edit that caused it. Each point of the result is a point of a sibling within 1 cm (shared with it, as after the conversion), or else the point of the element at the same path if no other element uses it (moved there, detached from its anchor), or else a new point. While the flag is on the points can't be moved, added or removed in the editor.

```lua
-- the line of the first sibling, 1 m higher (its first point shared)
function M.copy(v, ctx)
    local s = ctx.siblings[1]
    if s == nil then return nil end
    local line = {}
    for i, p in ipairs(s.points.line) do
        line[i] = i == 1 and p or p + vec3(0, 1, 0)
    end
    return { points = { line = line } }
end
```

## Spline-driven elements

A type can define `spline` instead of a point tree or an editable mesh. The user places individual points, as for roads: the first click creates one point and drawing continues. The editor supports adding at either end, inserting before or after a selected point, moving and removing points. Splines have no road intersections, splitting or merging.

```json
{
    "createMenu": { "direct": true },
    "spline": {
        "curveTypes": [0, 1, 2],
        "defaultCurveType": 2,
        "segments": 20,
        "projectPoints": false,
        "projectSections": false,
        "sectionVertices": ["-width/2", "width/2"],
        "components": [{ "name": "strip" }],
        "textures": ["texture"],
        "texturesMapping": { "strip": [0] }
    }
}
```

Geometry uses the [road component format](Core-Roads), including local definitions, conditions, UV expressions, shared/inherited components and end caps. It does not need road getters, junctions or traffic settings. Put `splineGroup` and `pointsGroup` in the type's UI for the curve controls and point placement.

| Setting | Meaning |
|---|---|
| `curveTypes`, `defaultCurveType` | Allowed curve IDs and the initial mode: `0` Bézier, `1` Hermite, `2` low-poly. The list must be nonempty and contain the default. |
| `segments`, `segmentsPer100m` | Initial smooth-curve resolution, fixed or per 100 m. Low-poly uses the control points directly. |
| `projectPoints`, `projectSections` | Project placed/moved control points and/or evaluated sections onto surfaces. Point projection preserves recorded heights during undo and redo. |
| `projectionMask` | Surface collision mask, default terrain and generated geometry (`136`). The element's own collider is excluded. |
| `maxMiter` | Corner width multiplier limit, default `2`. |
| `maxSectionLength` | Split evaluated spans longer than this distance; `0` disables the limit. |
| `independentSegments` | Evaluate components separately for each span. `z` starts at zero and `totalLength` is that span's length; caps remain at the whole spline's ends. Default `false`: continuous component geometry. |
| `splitCondition`, `maxSplits` | Optional boolean expression evaluated on each span with its `totalLength` and endpoint vertices (`v<n>_0`, `v<n>_1`). True halves the span, up to `maxSplits` (default `3`, at most `8`). Useful for quantized texture mapping. |

Control points use the existing point tree as `points.spline`; curve parameters (`curveType`, `segments`, `segmentsPer100m`, `hermiteTension`, `subdivideEqually`) use element state. The API kind remains `lowLevel`, so existing creation, point-editing, attachment, import and history commands apply. Empty, one-point and coincident-point splines are editable and produce no invalid mesh.

Exported spline elements additionally have `spline.sections` and `spline.pieces`. Sections contain `center`, `right`, `up`, `vertices` (all in builder coordinates) and cumulative `distance`. Pieces are pairs of evaluated sections, including subdivision, so exporters can use the same edges as the editor. This derived geometry is rebuilt on load rather than saved in the city. Projected splines rebuild after generated surfaces; API replies and export snapshots wait for the physics synchronization needed by newly replaced colliders.

## Converters

```json
{
    "label": "CONVERT_TO_PSDL",
    "tooltip": "CONVERT_TO_PSDL_TOOLTIP",
    "sources": ["road", "intersection", "terrainPatch", "buildingLine"],
    "types": ["psdl"],
    "script": "convert.py",
    "replace": true
}
```

| Key | Description |
|---|---|
| `label`, `tooltip` | String keys of the conversion button. |
| `sources` | What it converts: `road`, `intersection`, `terrainPatch`, `buildingLine`, `lowLevel` or `lowLevel/<type>`. |
| `types` | Only the procedural elements of these types (all if missing). |
| `script` | The Python script, in the folder of the converter (default `convert.py`). |
| `enabledWhen` | Optional `<module>.<function>` Lua predicate. Called as `function(_, ctx)`: `ctx.element` and `ctx.neighbors` contain detached `{kind, id, state}` snapshots (roads of an intersection, intersections of a road). Returning false disables an accepted button; availability is rechecked before and after running the converter. |
| `replace` | The converted elements are replaced (default `true`): the roads and intersections become ghosts, the other elements are deleted. |

The script runs with the Python of the program and gets three arguments: the `Files/python` folder (to import `DecodeExportedJson`), the input file and the output file. It fails with a non-zero exit code; the last line of its error output is shown to the user (a string key is translated, e.g. `sys.exit("MY_CONVERSION_ERROR")`).

**Input**: the format of the export JSON (see [Input and Output](Input-and-Output)), with the ids of the [scripting API](Scripting-API): `convert` lists the elements to convert (`{kind, id}`), and the lists have them with the elements around them (the intersections of a road and their roads, the roads of an intersection and their intersections, the container and contents of a low-level element, and the low-level elements replacing any of them).

**Output**: the low-level elements, in the format of the city save:

```json
{
    "points": [
        { "id": "p0", "position": [10, 0, 5], "weld": true },
        { "id": "p1", "position": [10, 0, 9] },
        { "id": "p4", "position": [10, 3, 9], "anchor": { "column": "p1" } }
    ],
    "elements": [
        { "id": "e0", "type": "room", "name": "Road 3 room", "state": { "echo": false } },
        { "id": "e1", "type": "roadWithoutSidewalks", "name": "Road 3", "container": "e0",
          "state": { "texture": "texture/r2_f.tex" },
          "points": { "sections": [ { "left": "p0", "right": "p1" }, { "left": "p2", "right": "p3" } ] } },
        { "id": "e2", "type": "baiRoad", "container": "e0",
          "state": { "startIntersection": { "kind": "intersection", "id": 15 } }, "points": { } }
    ],
    "updates": [
        { "element": { "kind": "lowLevel", "id": 42 }, "points": { "perimeter": ["p0", "p1", 1207] }, "state": { "simplifyPerimeter": true } }
    ],
    "replace": true
}
```

| Key | Description |
|---|---|
| `points` | The points of the elements, each with a local `id`. With `weld` (default `true`) a point takes the place of an existing point at the same position (within 1 cm), shared from then on, or else is attached to an anchor there (not of a ghost, except the anchors across the end of a road that isn't a ghost, which follow it: e.g. the dividers of a road converted after its intersection; not for a point with `anchor`). `rotation`: of a rotatable point. `anchor`: the point follows other points of the output, `{"line": [id, id]}` (on the line between them) or `{"column": id}` (on the vertical line of that point), see [Control points](#control-points); a point welded to an existing one keeps its own anchor. |
| `elements` | `id` (local), `type`, `name`, `state` (the parameters), `points` (the structure of the control points of the type with point ids; the missing points get their default position), `mesh` (`vertices`: point ids, `triangles`, `uvs`), `container` and `source` (a local element id, or a reference `{"kind", "id"}` to an existing element). A reference parameter is a local element id or a reference to an existing element. |
| `updates` | Changes of existing low-level elements (`elements` can then be missing): `element` (a reference `{"kind": "lowLevel", "id"}`, or a local element id), `points` (top-level nodes of the control points, replacing the ones of the element with the same names; a point is a local point id, or the id of an existing terrain point) and `state` (merged into the state of the element). The elements changed are reopened in the editor after a conversion started from it. |
| `replace` | Overrides the `replace` of `converter.json`. |

By default the elements replace the converted element: their `source` is the converted element (when a single one is converted) and, when a low-level element is converted, they take its container and its source. The whole conversion is a single undo step. With `replace: false`, generated elements have no implicit source or container; the converter can still give explicit references.

## Ghosts

A road or intersection converted becomes a ghost: its geometry is hidden and it can't be selected, but it stays in the city with its anchors, so the elements connected to it keep their geometry (an intersection keeps the size it needs for a converted road, a road keeps its end on a converted intersection) and the terrain points on its anchors stay where they are. In the export JSON it has `"ghost": true`: the exporter should not export its geometry, and should use the low-level elements whose `source` it is instead. The panel of such a low-level element can restore the original element (the elements replacing it are deleted). Deleting the last element replacing a ghost deletes the ghost too. The scripting API lists, sets and restores ghosts (`ghosts.*`).

## Export

The export JSON has `lowLevelElements`: `id`, `type`, `data` (`name`, `fields.state` with the references as `{kind, id}` with the ids of the export, `mesh`: the geometry built by the script), `points` (the structure of the control points, each point `{"id": <terrain point id>, "position": [x, y, z]}`, plus `rotation` for the rotatable ones; the same id for a point shared with other elements), `vertices` (the same for the vertices of the mesh), `triangles`, `uvs`, `containerId` (-1 if none) and `source` (`{kind, id}` of the ghost it replaces, null if none).

## Midtown Madness 2

The low-level types and converters of the Midtown Madness 2 core (the attributes of the rooms of the PSDL, the BAI roads and intersections) are described in the README of the core.

## Importing a payload without conversion

Scripts can call `lowLevel.import(data)` with the converter output's `points` and `elements`. Local point IDs preserve shared topology, element IDs resolve containers/references, and the return value maps both sets of IDs to API IDs. Import does not create ghosts or replace procedural geometry. The endpoint rejects updates, replacement directives, procedural sources, invalid references/meshes, and cyclic attachment graphs. Changes participate in the script's transaction. See [Scripting API](Scripting-API#proppaths).

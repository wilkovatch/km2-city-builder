# Core Props and Objects

This page covers everything placed in the city as a mesh loaded from a file:

* **props** placed automatically along roads, according to *prop rules*;
* **mesh instances** placed by hand, whose types are [mesh instance custom element types](Core-Custom-Elements#mesh-instance-elements);
* **parametric objects**: meshes attached to a parameter of a road or of the city properties (traffic lights, checkpoints, ...), positioned by the core.

```
elements/props/
├── elements/<type>.json        props element types
├── containers/<type>.json      props container types
└── parametric/<type>.json      parametric object types
```

## Props along roads

The hierarchy is: a road's **prop rule** (state property `propRule`, preset category `roadPropRule`) contains one **container** per props container name of the road type (`left`, `right`, `middle`... as listed in the road type's `propsContainers`); each container (preset category `propContainer`) contains one or more **elements** (preset category `propElem`); each element places one kind of prop along the container's props line using its element type's placement rules. The custom elements of the types with `propsContainers` have a prop rule too (see [Core Custom Elements](Core-Custom-Elements)).

The props lines themselves (where the props go) are defined by the road type, see [Core Roads](Core-Roads#props-lines).

### Container types (`containers/<type>.json`)

```json
{
    "label": "PROP_CONTAINER_TYPE_LINE",
    "parameters": [
        { "name": "element", "allowedTypes": ["line"], "maxNumber": 1 }
    ]
}
```

| Key | Description |
|---|---|
| `label` | String key of the type name. |
| `parameters` | The slots of the container. Each one has a `name`, optional `allowedTypes` (element type names; all types if missing or empty) and `maxNumber`: 1 for a single element, edited directly, any other value for a list of elements (0 or negative for no limit). |

String keys built from the slot name (in upper case): `PROP_ELEM_NAME_<NAME>` and `PROP_CONTAINER_EDIT_PROPERTY_<NAME>` for single elements, `PROP_ELEM_LIST_<NAME>` for lists.

A container state is `{ "type": ..., "name": ..., "<slot>": <element state or list of element states> }`.

### Element types (`elements/<type>.json`)

```json
{
    "label": "PROP_TYPE_LINE",
    "maxMeshes": 1,
    "parametersInfo": {
        "parameters": [
            { "name": "start", "label": "PROP_ELEM_START", "placeholder": "VALUE_PH", "type": "float" },
            { "name": "elemDistance", "label": "PROP_ELEM_DISTANCE", "placeholder": "VALUE_PH", "type": "float" },
            { "name": "maxNumber", "label": "PROP_ELEM_MAX_NUM", "placeholder": "VALUE_PH", "type": "int" },
            { "name": "xPos", "label": "PROP_ELEM_XPOS", "placeholder": "VALUE_PH", "type": "float" }
        ]
    },
    "uiInfo": [
        { "name": "start", "width": 0.75 },
        { "name": "elemDistance", "width": 1.5 }
    ],
    "placementRules": {
        "whileCondition": "elemDistance > 0 and i < maxNumber and z <= totalLength",
        "xPos": "xPos",
        "zPos": "if(i=0, start, lastZ+elemDistance)",
        "forward": "localForward"
    }
}
```

| Key | Description |
|---|---|
| `label` | String key of the type name. |
| `maxMeshes` | 1: the element has a single mesh (state property `mesh`). Any other value: a list of meshes (state property `meshes`), chosen with `meshIndex`. |
| `parametersInfo` | A `parameters.json`-like object: parameters, definitions, loop variables. Parameters cannot be `instanceSpecific`. |
| `uiInfo` | The list of fields of the element panel (`name`, `width`), without tabs or built-in groups. Type, preset and mesh selection are added by the program. |
| `placementRules` | See below. |

#### Placement rules

| Rule | Type | Description |
|---|---|---|
| `iterationScope` | string | `"path"` (default), or `"edge"`: restart placement on each nonzero polyline edge. |
| `zPos` | float | Distance along the props line (measured on the line halfway between its two bounds). |
| `xPos` | float | Position across the props line: 0 on the first bound, 1 on the second. |
| `whileCondition` | bool | Placement continues while this is true. |
| `ifCondition` | bool | Optional: a prop is placed at this step only if true. |
| `meshIndex` | float | Index into the `meshes` list (clamped), for types with `maxMeshes` other than 1. |
| `forward` | vec3 | Direction the prop faces (rotation around the vertical axis only). Default `localForward`. |
| `meshTransform` | bool | Optional: the mesh is placed with the transform the core gives for it (see [Mesh transforms](#mesh-transforms)). Default false. |
| `autoYOffset` | bool | Optional: if there is no core transform, the mesh is raised so that its lowest point sits on the line. Default true. |

The placement loop, for `i = 0, 1, 2, ...`:

1. set `i`, `lastX`, `lastZ` and evaluate the dynamic definitions;
2. evaluate `xPos` and `zPos`, find the point and the local frame at that position;
3. stop if `whileCondition` is false (or after 1000 props);
4. if `ifCondition` is true (or missing), place the mesh (chosen with `meshIndex`) at the point, facing `forward`, moved from the point as `meshTransform` and `autoYOffset` ask;
5. `lastX = x`, `lastZ = z`.

Variables of element types:

| Variable | Type | Meaning |
|---|---|---|
| `i` | float | Iteration index. |
| `x`, `z` | float | Position of the current prop (after step 2). |
| `lastX`, `lastZ` | float | Position of the previous iteration (0 at the first). |
| `totalLength` | float | Length of the props line. |
| `meshNum` | float | Number of meshes in the element's list (1 for single-mesh types). |
| `pathForward` | vec3 | Current path tangent, including slopes. |
| `localRight` | vec3 | Horizontal direction across the props line, pointing from the second bound to the first. |
| `localForward` | vec3 | Direction along the props line. |
| `localUp`, `worldUp`, `worldRight`, `worldForward` | vec3 | |

For random placements use `rnd(min, max, seed)`: its sequences restart each time the road is rebuilt, so the props do not move at every rebuild.

Props are put on layer 13 and exported with the road at their placement points (`propLines`, see [Input and Output](Input-and-Output#export-format-ioexportedcity-ioexportedcitycs-and-cityencoderjson_export)).

## Standalone prop rules

**Create → Prop rule** creates a freely placed rule (`propPath` in the API). It uses the same prop containers, elements and `roadPropRule` presets as a road, with editable ribbons instead of a road's generated props lines. A ribbon has two boundary lines; sections pair their points. Separate segments leave gaps in the placement area while retaining the spacing and maximum count across the ribbon. Each container has its own sequence.

The editor supports adding, renaming and removing ribbons, adding/removing segments and paired sections, sharing or anchoring points, and a central move handle. Ribbon guides remain selectable without any prop meshes; clicking a generated prop edits its rule. Generated props are rebuilt, not saved as individual instances. Points, anchors, presets and transforms participate in undo/redo and save/load.

An optional distance-reference point in every section of a segment changes where length is measured; interpolation still places props between the boundaries. Segments without a reference use their midpoint line. The **Use distance reference** checkbox enables the supplied references. A collapsed ribbon (both boundaries on the same line) is valid.

Prop element `placementRules.iterationScope` is `"path"` by default. `"edge"` restarts the element's expressions for every nonzero polyline edge, with `i`, `lastX`, `lastZ` reset and `totalLength` equal to that edge's length. `pathForward` is the current normalized path tangent; its horizontal projection can be used by `forward` when a collapsed ribbon has no across direction. For example, `cross(worldUp, pathForward)` turns a prop a quarter turn from the tangent. The existing 1000-iteration guard applies to each sequence.

Saved cities and export JSON have a separate `propPaths` array. Internally these elements reuse custom element point ownership and history; the reserved built-in type `__propPath` is independent of the core. Export records contain rule state, ribbon control points and evaluated `data.propLines`, using the ordinary mesh dictionary. Exporters decide whether to write a procedural path or its evaluated placements; a core's road-specific suppression of generated props must not silently suppress standalone rules.

## Placement rules

The placement rules of the mesh instances (`meshInstance.placementRules` of their [types](Core-Custom-Elements#mesh-instance-elements)) and of the parametric objects (`placementRules` of `props/parametric/<type>.json`):

```json
"placementRules": {
    "layer": "11",
    "placerLayerMask": "-1-2^7",
    "autoYOffset": "false",
    "meshTransform": "false"
}
```

| Key | Description |
|---|---|
| `placementRules.layer` | Float expression: the [layer](Architecture-Overview#layers-collision-and-render) of the instance. |
| `placementRules.placerLayerMask` | Float expression: the collision mask (bit *n* = layer *n*) used when placing the instance with the mouse. `-1-2^13` means "everything except layer 13". |
| `placementRules.autoYOffset` | Boolean expression: if true, the mesh is raised so that its lowest point sits on the placement point (unless `meshTransform` gives a transform). |
| `placementRules.meshTransform` | Optional boolean expression: if true, the mesh is placed with the transform the core gives for it (see [Mesh transforms](#mesh-transforms)); if the core gives none, `autoYOffset` applies. |
| `placementRules.snapToSurface` | Optional boolean expression: if true, the object goes back on the surface under it (the layers of `placerLayerMask`) after being moved with the gizmo. Parametric objects also use it after `objects.projectToGround` and when placed by clicking (array points). The highest surface wins, but a road or another surface less than 0.5 m under the terrain wins over the terrain, so that a point on an MM2 road (15 cm under the sidewalks) isn't put on a terrain covering it. While the terrain is hidden (*View→Hide terrain grid*) it's skipped. The object's own collider is excluded. Used with `allowPosition` `[true, false, true]` it keeps objects on the ground. |

The first three rules are required. They can use the parameters and static definitions.

The mesh placement clicks hit the first surface of `placerLayerMask` under the mouse, and the meshes placed in a radius use the `snapToSurface` rule above. To place on a surface covered by the terrain, hide the terrain (*View→Hide terrain grid*). The core's `surfaceProjectionTerrainFallbackOnly` doesn't apply to placement.

In both cases the position of the instance (the one that is exported) stays the placement point: only the mesh under it is moved.

## Mesh transforms

A game may draw a mesh away from the point where it is placed: for example MM2 draws a prop (a banger) with the origin of its mesh at its centre of gravity, which is in the game's tuning files, not in the mesh. A core gives these transforms with an optional module, `server/mesh_transforms.py` (see [Core Python Integration](Core-Python-Integration#mesh-transforms)), and the placement rules of the mesh instances, parametric objects and props elements turn them on with `meshTransform`.

The transform (position, rotation, scale, in the program's coordinates) goes from the placement point to the origin of the mesh, in the frame of the placed object: it moves and turns with it. It is asked once per mesh file and cached until the city is reloaded. Without one (no module, or the module returns `None`), `autoYOffset` applies.

## Parametric objects

A parameter of type `objectInstance` attaches a mesh to its element. The core decides where it goes (through definitions), the user can adjust it within the allowed axes, and it is exported as part of the element's state.

`objectInstance` parameters can be used in road types, in the city properties, and in the elements of `array` and `customCityProperties` parameters (for example the MM2 races, made of arrays of checkpoints).

```json
{
    "name": "startTrafficLight",
    "label": "...",
    "type": "objectInstance",
    "objectInstanceSettings": {
        "type": "trafficLight",
        "allowCustomModel": false,
        "defaultModel": {
            "dynamic": true,
            "options": ["_GLOBAL_trafficLight1", "_GLOBAL_trafficLight2"],
            "index": "startTrafficLight_meshIndex"
        },
        "defaultScale": [1, 1, 1],
        "condition": "startTrafficLightCondition",
        "basePosition": "startTrafficLightBasePosition",
        "baseRotation": "startTrafficLightBaseRotation",
        "allowPosition": [true, true, true],
        "allowRotation": [false, true, false],
        "allowScale": [false, false, false]
    }
}
```

| Key | Description |
|---|---|
| `type` | Name of a file in `props/parametric/`: the `parametersInfo` and `uiInfo` of the object (shown in its panel) and its [placement rules](#placement-rules). |
| `defaultModel` | The mesh: either `{"model": "<path>"}`, or `{"options": [...], "index": "<variable>"}` where `index` is the **name** of a float variable or definition selecting the option. With `"dynamic": true`, the options are names of state properties containing the path (for example values injected from the city properties, see [Core City Properties](Core-City-Properties#injectable-parameters)), searched in the element's state, instance state and runtime state. |
| `allowCustomModel` | Whether the user can replace the mesh. |
| `defaultScale` | Initial scale `[x, y, z]`. |
| `condition` | Name of a **boolean definition**: the object exists only while it is true. Omit it for objects that always exist. |
| `basePosition`, `baseRotation`, `baseScale` | Names of **vec3 definitions** giving the position, the rotation (Euler angles in degrees) and the scale of the object. Missing: zero position, no rotation, unit scale. For roads, use late definitions, which can use the first and last section vertices (`v<n>_0`, `v<n>_1`). |
| `allowPosition`, `allowRotation`, `allowScale` | Which axes the user can change (`[x, y, z]`). The user's changes are stored as offsets in the parameter's state (`localPosition`, `localRotation`, `localScale`, plus `meshPath`). |

The hooks (`condition`, `defaultModel.index`, `basePosition`, `baseRotation`, `baseScale`) are evaluated each time the objects of the element are placed, after all the elements have been rebuilt. The definitions they name can be expressions or [Lua scripts](Core-Lua-Scripts). Late definitions of a category other than `common` are only evaluated when a hook reads them, which makes such a category the place for the placement logic.

Elements of `array` and `customCityProperties` parameters have no definitions, so their objects should not use `condition` or the `base*` keys: they start at the origin of the element and are placed by the user.

### Objects near another road

Some objects belong to a road but stand near another road of the same intersection. MM2's traffic lights, for example, can stand on a corner of the road across the intersection. Their position is a [Lua script](Core-Lua-Scripts) definition: the script gets the intersections of the road through its context (`ctx:intersection("start")`), with the other roads sorted clockwise and their variables, and picks the corner.

Each road type describes where its own corners are (with simple definitions such as `startTrafficLightPosition`), while the script, shared by all the road types, decides which corner to use. See the [example](Core-Lua-Scripts#example-the-mm2-traffic-lights).

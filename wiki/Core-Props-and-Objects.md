# Core Props and Objects

This page covers everything placed in the city as a mesh loaded from a file:

* **props** placed automatically along roads, according to *prop rules*;
* **mesh instances** placed by hand, whose types are [mesh instance custom element types](Core-Custom-Elements#mesh-instance-elements);
* **parametric objects**: meshes attached to a parameter of a road or of the city properties (traffic lights, checkpoints, ...), positioned by the core.

```
elements/props/
├── types/<type>.json           props types
└── parametric/<type>.json      parametric object types
```

## Props

A road's **prop rule** (state property `propRule`, preset category `roadPropRule`) has one **props container** for each name listed in the road type's `propsContainers` (`left`, `right`, `middle`...). The custom elements of the types with `propsContainers` have a prop rule too (see [Core Custom Elements](Core-Custom-Elements)). A container holds the state of a **props type**:

```json
{ "type": "line", "name": "Default", "mesh": "geometry/tree.pkg", "elemDistance": 5, "xPos": 0.5 }
```

The builder knows only this one concept. How deep the props go, what fills a list and how they're placed comes from the types of the core, so a type can be as simple as one mesh in a row or as involved as a game needs.

The lines where the props go are defined by the road type, see [Core Roads](Core-Roads#props-lines), or by the [prop placer](Core-Prop-Placers) or the geometry of a custom element.

### Props types (`types/<type>.json`)

```json
{
    "label": "PROP_TYPE_ROW",
    "placement": "my_props.row",
    "meshTransform": true,
    "parametersInfo": {
        "parameters": [
            { "name": "mesh", "label": "PROP_MESH", "type": "mesh" },
            { "name": "spacing", "label": "PROP_SPACING", "type": "float", "defaultValueInArray": 5.0, "range": [0.25, 64.0] }
        ]
    },
    "uiInfo": [
        { "name": "mesh", "width": 1.5 },
        { "name": "spacing", "width": 1.5 }
    ]
}
```

| Key | Description |
|---|---|
| `label` | String key of the type name. |
| `nestedOnly` | Optional, default false: the type is only for the lists of other types, it can't be chosen for a props container. |
| `parametersInfo` | A `parameters.json`-like object: parameters, definitions, loop variables. Parameters cannot be `instanceSpecific`. Any [parameter type](Parameters-and-UI) works, in particular `mesh`, `meshList` and an `array` of other props (see below). |
| `uiInfo` | The fields of the panel (`name`, `width`), without tabs or built-in groups. The preset and the type are added by the program. A field can also have `boundsOnly`: it's not shown where the props have no bounds (a `lineOnly` type directly on a [prop placer](Core-Prop-Placers)). |
| `placement` | The Lua callback giving the placements, `<module>.<function>` (see below). |
| `meshTransform` | Optional, default false: the meshes are placed with the transform the core gives for them (see [Mesh transforms](#mesh-transforms)). |
| `autoYOffset` | Optional, default true: if there is no core transform, a mesh is raised so that its lowest point sits on the line. |
| `lineOnly` | Optional, default false: the props follow a single line, so a [prop placer strip](Core-Prop-Placers) doesn't use its bounds. |
| `authoredLine` | Optional, default false: on a [prop placer spline](Core-Prop-Placers#projection-and-export) the lines are the spline before its projection, so the ground gives only the height of each prop. |

The panel of a type is built from its parameters, like those of the other elements. Its changes apply at once and go through undo. In the panel of a road a props container opens a panel of its own, while a [prop placer strip](Core-Prop-Placers) can have the fields directly in the panel of the element.

Each type has its own preset category, `props/<type>` (see [Core Templates and Presets](Core-Templates-and-Presets)). Changing the type of a state keeps the values of the parameters the two types have in common, if the new type allows them: a value outside the `range` of the new parameter isn't kept, and a list keeps only the elements of the `propsTypes` it allows, up to its `maxElements`. The others come from the first preset of the new type (or the `defaultValueInArray` of its parameters, when the category is created), the values of the old type's other parameters are dropped, and the state isn't linked to a preset anymore.

### Groups

A type without `placement` is a **group**: its props are those of the elements of its `array` parameters with `arrayProperties.propsTypes`, the types an element of the list can have.

```json
{
    "label": "PROP_TYPE_MIXED",
    "parametersInfo": {
        "parameters": [{
            "name": "elements", "label": "PROP_ELEMENTS", "type": "array",
            "arrayProperties": { "propsTypes": ["row", "scattered"] }
        }]
    },
    "uiInfo": [{ "name": "elements", "width": 1.5 }]
}
```

Each element of such a list has the parameters of its own type, shown in a panel with its preset and, if more than one type is allowed, its type. It's labelled with the name of its preset (followed by `*` if it was changed) or else the label of its type. An element whose type has only one parameter, a mesh, opens the mesh selector directly. A type can have other parameters too and place props itself along with its lists.

### The placement callback

`placement` is called with `(variables, ctx)`, on each rebuild of the element for each state of the type. `variables` has the values of the parameters of the state, the definitions of the type, `totalLength` (the same as `ctx.length`) and `_GLOBAL_<parameter>`, the city properties injected on the element that owns the props (see [Core City Properties](Core-City-Properties#injectable-parameters)).

| `ctx` | Description |
|---|---|
| `length` | The length of the lines (measured on the line halfway between the two bounds). |
| `limit` | The most props the callback can give: 1000 on a road (the others are left out) and 100000 on a prop placer (reaching it is a placement error, as a procedural export must not silently disagree with a truncated preview). |
| `placer` | Whether the lines are those of a [prop placer](Core-Prop-Placers), instead of a container of a road or another element. |
| `projected` | Whether the placements are put on the ground after the callback (on a prop placer with **Project to ground**): only the height of their positions changes. |
| `sample(x, z)` | The point `z` metres along the lines, `x` across them (0 on the first bound, 1 on the second): a table with `position`, `right` (horizontal, from the second bound to the first), `forward` (horizontal, along the lines) and `tangent` (along the line, including slopes). |
| `edges()` | The spans between two points of the lines that aren't empty, in order, for what restarts on each one: a list of tables with their `length` and `sample(x, z)`. |
| `random()` | The next number in `[0, 1)` of a sequence of its own, starting from the name of the container and the position of the state in it. So it's the same at each rebuild, and it doesn't depend on the rest of the city. |
| `parameters` | The whole state as a table, for the values that aren't numbers (such as `parameters.mesh` and `parameters.meshes`). |

It returns a list of placements:

| Key | Description |
|---|---|
| `position` | `vec3`, required. The program puts it on the surface if the type and the element ask for it. |
| `rotation` or `forward` | `rotation`: Euler degrees. `forward`: the direction the prop faces, rotating only around the vertical axis. Default no rotation. |
| `scale` | `vec3`, default one. |
| `model` | The mesh path. Without one the placement is only a place: nothing is shown, but it's exported, for what the game fills by itself (as the parked cars of the MM2 core). |
| `data` | Optional table for the exporters, as it is. |

```lua
return { row = function(v, ctx)
    local result = {}
    local mesh = ctx.parameters.mesh
    for z = 0, ctx.length, v.spacing do
        local line = ctx.sample(0.5, z)
        result[#result + 1] = { position = line.position, forward = line.forward, model = mesh }
    end
    return result
end }
```

An error in the script, a result that isn't a list or a non-finite value is reported once in the log, as with the other scripts of the core. On a prop placer it's also the placement error of the element (see [Core Prop Placers](Core-Prop-Placers)).

For randomness use `ctx.random()`. `rnd(min, max, seed)` is also available, but its sequences are shared with the other props of the element and restart with each of its rebuilds, so its results depend on what's placed before. The numbers of the scripts are doubles while the positions use single precision: `single(x)` gives `x` in single precision.

### Export

Props are put on layer 13. The placements are exported with their element, in order, by container (`propLines`, see [Input and Output](Input-and-Output#export-format-ioexportedcity-ioexportedcitycs-and-cityencoderjson_export)): a place without a model (or whose model wasn't found) has `meshId` -1, so an exporter of props must leave it out.

## Prop placers

Core-defined [PropPlacers](Core-Prop-Placers) place props along spline strips or inside polygons. They are ordinary custom elements with click-by-click placement, presets, undo/redo, point attachments and a saved automatic ground-projection toggle. The core configures the number of polygon models, their optional densities and custom parameters, or each spline strip's permitted props types.

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

# Core Props and Objects

This page covers everything placed in the city as a mesh loaded from a file:

* **props** placed automatically along roads, according to *prop rules*;
* **mesh instances** placed by hand;
* **parametric objects**: meshes attached to a parameter of a road or of the city properties (traffic lights, checkpoints, ...), positioned by the core.

```
elements/props/
├── elements/<type>.json        props element types
├── containers/<type>.json      props container types
├── parametric/<type>.json      parametric object types
└── meshInstance.json           settings of hand-placed mesh instances
```

## Props along roads

The hierarchy is: a road's **prop rule** (state property `propRule`, preset category `roadPropRule`) contains one **container** per props container name of the road type (`left`, `right`, `middle`... as listed in the road type's `propsContainers`); each container (preset category `propContainer`) contains one or more **elements** (preset category `propElem`); each element places one kind of prop along the container's props line using its element type's placement rules.

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
| `zPos` | float | Distance along the props line (measured on the line halfway between its two bounds). |
| `xPos` | float | Position across the props line: 0 on the first bound, 1 on the second. |
| `whileCondition` | bool | Placement continues while this is true. |
| `ifCondition` | bool | Optional: a prop is placed at this step only if true. |
| `meshIndex` | float | Index into the `meshes` list (clamped), for types with `maxMeshes` other than 1. |
| `forward` | vec3 | Direction the prop faces (rotation around the vertical axis only). Default `localForward`. |

The placement loop, for `i = 0, 1, 2, ...`:

1. set `i`, `lastX`, `lastZ` and evaluate the dynamic definitions;
2. evaluate `xPos` and `zPos`, find the point and the local frame at that position;
3. stop if `whileCondition` is false (or after 1000 props);
4. if `ifCondition` is true (or missing), place the mesh (chosen with `meshIndex`) at the point, with its bottom on the line, facing `forward`;
5. `lastX = x`, `lastZ = z`.

Variables of element types:

| Variable | Type | Meaning |
|---|---|---|
| `i` | float | Iteration index. |
| `x`, `z` | float | Position of the current prop (after step 2). |
| `lastX`, `lastZ` | float | Position of the previous iteration (0 at the first). |
| `totalLength` | float | Length of the props line. |
| `meshNum` | float | Number of meshes in the element's list (1 for single-mesh types). |
| `localRight` | vec3 | Horizontal direction across the props line, pointing from the second bound to the first. |
| `localForward` | vec3 | Direction along the props line. |
| `localUp`, `worldUp`, `worldRight`, `worldForward` | vec3 | |

For random placements use `rnd(min, max, seed)`: its sequences restart each time the road is rebuilt, so the props do not move at every rebuild.

Props are put on layer 13 and exported with the road (`propLines`, see [Input and Output](Input-and-Output#export-format-ioexportedcity-ioexportedcitycs-and-cityencoderjson_export)).

## Mesh instances (`meshInstance.json`)

Settings shared by all hand-placed mesh instances:

```json
{
    "parametersInfo": {
        "parameters": [
            { "name": "prop", "label": "MESH_INSTANCE_PROP", "placeholder": "VALUE_PH", "type": "bool" }
        ]
    },
    "uiInfo": [ { "name": "prop", "width": 1.5 } ],
    "placementRules": {
        "layer": "if(prop, 13, 11)",
        "placerLayerMask": "-1-2^if(prop, 13, 7)",
        "autoYOffset": "prop"
    }
}
```

| Key | Description |
|---|---|
| `parametersInfo`, `uiInfo` | Parameters of every mesh instance, shown in the transform panel. Typical use: flags for the exporter (MM2 exports props and buildings to different files). |
| `placementRules.layer` | Float expression: the [layer](Architecture-Overview#layers-collision-and-render) of the instance. |
| `placementRules.placerLayerMask` | Float expression: the collision mask (bit *n* = layer *n*) used when placing the instance with the mouse. `-1-2^13` means "everything except layer 13". |
| `placementRules.autoYOffset` | Boolean expression: if true, the mesh is raised so that its lowest point sits on the placement point. |

All three rules are required. They can use the parameters and static definitions.

## Parametric objects

A parameter of type `objectInstance` attaches a mesh to its element. The core decides where it goes (through definitions), the user can adjust it within the allowed axes, and it is exported as part of the element's state.

`objectInstance` parameters can be used in road types, in the city properties, and in the elements of `array` and `customElement` parameters (for example the MM2 races, made of arrays of checkpoints).

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
        "intersection": 0,
        "condition": "startTrafficLightCondition",
        "basePosition": "startTrafficLightPosition",
        "baseRotation": "startTrafficLightRotation",
        "allowPosition": [true, true, true],
        "allowRotation": [false, true, false],
        "allowScale": [false, false, false]
    }
}
```

| Key | Description |
|---|---|
| `type` | Name of a file in `props/parametric/`, with the same format as `meshInstance.json` (layer, placer mask, automatic Y offset). |
| `defaultModel` | The mesh: either `{"model": "<path>"}`, or `{"options": [...], "index": "<variable>"}` where `index` is the **name** of a float variable or definition selecting the option. With `"dynamic": true`, the options are names of state properties containing the path (for example values injected from the city properties, see [Core City Properties](Core-City-Properties#injectable-parameters)), searched in the element's state, instance state and runtime state. |
| `allowCustomModel` | Whether the user can replace the mesh. |
| `defaultScale` | Initial scale `[x, y, z]`. |
| `condition` | Name of a **boolean definition**: the object exists only while it is true. Omit it for objects that always exist. |
| `basePosition`, `baseRotation`, `baseScale` | Names of **vec3 definitions** giving the position, the rotation (Euler angles in degrees) and the scale of the object. Missing: zero position, no rotation, unit scale. For roads, use late definitions, which can use the first and last section vertices (`v<n>_0`, `v<n>_1`). |
| `allowPosition`, `allowRotation`, `allowScale` | Which axes the user can change (`[x, y, z]`). The user's changes are stored as offsets in the parameter's state (`localPosition`, `localRotation`, `localScale`, plus `meshPath`). |
| `intersection` | For road choices: 0 for the start intersection, 1 for the end one. |
| `conditionRoadChoice`, `basePositionRoadChoice`, `baseRotationRoadChoice`, `baseScaleRoadChoice` | Optional: evaluate the corresponding definition on another road of the intersection, see below. |

Elements of `array` and `customElement` parameters have no definitions, so their objects should not use `condition` or the `base*` keys: they start at the origin of the element and are placed by the user.

### Choosing another road

Some objects belong to a road but stand near another road of the same intersection. MM2's traffic lights, for example, are placed on the corner of the road to the right. A *road choice* tells the program on which road to evaluate a definition:

```json
"basePositionRoadChoice": {
    "lateDefinitions": "startTrafficLight",
    "condition": "startTrafficLightPositionOnOtherRoad",
    "index": "startTrafficLightRoadIndex",
    "score": "startTrafficLightRoadScore",
    "inputData": ["startTrafficLightOppInv", "startTrafficLightInOffset", "projTrafficLightStart"]
}
```

1. `condition` (boolean definition): if false, the definition is evaluated on the road itself as usual.
2. The roads of the intersection (`intersection` key) are sorted clockwise.
3. If `score` is set, for every other road the program copies the `inputData` values (see below), evaluates `<score>_start` or `<score>_end` on that road (depending on which of its ends is at the intersection) and picks the road with the highest score; `maxScoreRoadIndex` is set to its position relative to the road itself.
4. The late definitions of the category `lateDefinitions` are evaluated, then `index` (float definition) gives the chosen road as an offset in the clockwise order from the road itself.
5. The `inputData` variables of the road are copied to the chosen road as `<name>_input` (the chosen road's type must have definitions with these names, giving defaults), and the chosen road's late definitions of the category are evaluated.
6. The definition is evaluated on the chosen road with the suffix `_start` or `_end` (for example `startTrafficLightPosition_start`), unless the chosen road is the road itself.

This makes it possible for each road type to describe where its own corners are, while the object's owner decides which corner to use.

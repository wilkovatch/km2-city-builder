# Parameters and UI

Most element types of a core are described by three files:

| File | Content |
|---|---|
| `parameters.json` | The user-editable parameters and the definitions (expressions computed from them). |
| `ui.json` | How the parameters are laid out in the editor panel. |
| `settings.json` | Element-specific settings (geometry, components, textures...), described on each element page. |

Building types use `line_*`, `building_*` and `side_*` prefixes for the three parts of a building; props, mesh instance and custom type files embed the same information in a single file (see their pages).

## `parameters.json`

```json
{
    "parameters": [ ... ],
    "internalParametersSettings": [ { "name": "makeCoplanar", "enabled": true } ],
    "loopVariables": [ "i" ],
    "staticDefinitions": [ ... ],
    "dynamicDefinitions": [ ... ],
    "lateDefinitions": [ { "category": "common", "definitions": [ ... ] } ]
}
```

Definitions and loop variables are described in [Expression Language](Expression-Language#definitions). `internalParametersSettings` enables or hides built-in parameters of the program; currently only `makeCoplanar` (roads) is configurable. When the key is missing, every built-in parameter is enabled.

### Parameter fields

| Field | Description |
|---|---|
| `name` | Identifier, used as variable name in expressions and as key in the saved state. |
| `type` | See below. |
| `label` | String key of the field label. |
| `placeholder` | String key of the placeholder text of input fields. |
| `tooltip` | String key of the tooltip (optional). |
| `container` | Optional group name. The parameter's full name becomes `<container>_<name>`, which is the name to use in expressions and in `ui.json`. All parameters of a container can be saved and loaded as one preset with a `PRESET_<container>` UI element (MM2 uses it for railings: `rail_type`, `rail_height`, ...). Do not use underscores in container names or in the names of parameters inside a container. |
| `instanceSpecific` | If true, the value belongs to this element only: it is stored in the element's *instance state*, which presets never overwrite (for example a block number or a per-road traffic rule). Only available on roads, intersections, building lines and terrain patch border meshes. |
| `enumLabels` | For `enum`: string keys of the options. |
| `arrayProperties` | For `array`, see below. |
| `customElementType` | For `customElement`: name of a file in `customTypes/`. |
| `objectInstanceSettings` | For `objectInstance`, see [Core Props and Objects](Core-Props-and-Objects#parametric-objects). |
| `defaultValueInArray` | Default value of the parameter when it is part of an array element or custom element and a new element is added. For `float`, `int` and `enum` write it with a decimal point (`5.0`), otherwise it is rejected with a warning. |

### Parameter types

| Type | Editor field | Value in state | In expressions |
|---|---|---|---|
| `bool` | Checkbox | `true`/`false` | float variable, 0 or 1 |
| `float` | Decimal input | number | float variable |
| `int` | Integer input | integer | float variable |
| `enum` | Dropdown (`enumLabels`) | index of the option | float variable |
| `string` | Text input | string | — |
| `texture` | Texture field + selector | relative texture path | — (referenced by name in `textures` lists) |
| `mesh` | Mesh field + selector | relative mesh path | — |
| `vec2`, `vec3` | none | vector | vector variable |
| `array` | Editable list | array of states | — |
| `customElement` | Button opening a sub-panel | nested state | — |
| `objectInstance` | Button selecting the placed object | nested state | — |

Values not set yet read as 0, false, an empty string or a zero vector. Default values for new elements come from the presets (see [Core Templates and Presets](Core-Templates-and-Presets)).

#### `array`

```json
{
    "name": "trafficCars",
    "label": "TRAFFIC_CARS",
    "type": "array",
    "arrayProperties": {
        "maxElements": -1,
        "elementLabel": "carModel",
        "elementProperties": [
            { "name": "carModel", "label": "TRAFFIC_CAR_MODEL", "type": "mesh" },
            { "name": "frequency", "label": "TRAFFIC_CAR_FREQUENCY", "type": "float" }
        ]
    }
}
```

| Field | Description |
|---|---|
| `maxElements` | Maximum number of elements; 0 or negative for no limit. |
| `elementProperties` | Parameters of each element (any type, including nested arrays, custom elements and object instances). |
| `customElementType` | Alternative to `elementProperties`: use the parameters of a custom type. |
| `elementLabel` | Name of the element parameter whose value is shown in the list (with `customElementType`, the custom type's `label` is used instead). Elements without a label are shown as "Element N". |

The editor shows the list with Add, Edit, Delete, Clone, Move up and Move down buttons. Each element is edited in its own panel, with all its parameters stacked vertically (there is no `ui.json` for array elements). When an element has a single parameter of type `objectInstance` (for example a checkpoint), editing it selects the placed object directly.

#### `customElement`

A button that opens a panel with the parameters of a custom type (`customTypes/<customElementType>.json`), stored as a nested state. See [Core City Properties](Core-City-Properties#custom-types).

## `ui.json`

```json
{
    "label": "PSDL_COMPATIBLE",
    "menuWidth": 1.5,
    "tabs": [
        {
            "label": "RD_TAB_MAIN",
            "elements": [
                { "name": "mainGroup", "width": 1.5 },
                { "name": "totalWidth", "width": 0.75 },
                { "name": "roadWidth", "width": 0.75 }
            ]
        }
    ]
}
```

| Field | Description |
|---|---|
| `label` | String key of the type's name, shown in the type dropdown. |
| `menuWidth` | Width of the panel, in units of 160 pixels (1.5 is the usual width). |
| `tabs` | One page per tab, with a button for each tab at the top of the panel (in two rows with four tabs or more). A tab `label` is a string key; an empty label is allowed for single-tab panels. |
| `elements` | Fields of the tab, in order. `name` is a parameter full name, a built-in group or a `PRESET_<container>` element; `width` is in the same units as `menuWidth`. Elements are placed side by side and wrap to a new row when the sum of their widths exceeds `menuWidth`. |

Parameters that are not listed in any tab are still variables and still saved, but cannot be edited. `array` parameters always take a full row.

### Built-in groups

Built-in groups insert the program's own controls (type and preset selectors, placement modes, built-in parameters). Each panel supports specific groups; the main group should normally be the first element of the first tab, since it contains the type selector and the end-editing button.

| Element type | Groups |
|---|---|
| Road | `mainGroup` (type, preset, point placement, projection, segments, smoothing, coplanarity, curve type), `intersectionGroup` (crosswalk sizes, intersection size increase and textures, road through intersection flags), `propsGroup` (props rule preset and one enable checkbox + edit button per props container) |
| Intersection | `mainGroup` (type, preset, sidewalk segments, size increase) |
| Terrain patch | `mainGroup` (type, point placement, smoothing, projection, auto-close, texture), `borderMeshGroup` (border mesh list and placement) |
| Terrain patch border mesh | `mainGroup` |
| Building line | `mainGroup` (type, preset, building list), `propertiesGroup` (height, front only, loop, inverted direction, projection, roof texture), `loopGroup` (automatic closing with presets) |
| Building | `mainGroup` (enabled sides, height, depth, roof texture, acute angle fix, side selection) |
| Building side | `mainGroup` (floors), `blocksGroup` (block dictionary) |
| Building block | `mainGroup` (block type) |

Props elements, mesh instance settings and parametric objects have no groups: their `uiInfo` is a flat list of elements (see [Core Props and Objects](Core-Props-and-Objects)).

## Strings

All labels are string keys, resolved in the core's `strings/<language>.json` first and then in the program's base strings. A key that is not found anywhere is displayed as is (and logged), which makes missing strings easy to find. Set a string to `""` to show nothing (for example an unused tooltip). Some keys are built by the program from names in the core; they are listed in [Core Structure](Core-Structure#strings).

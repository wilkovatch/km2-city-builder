# Core Roads

A road type is a folder in `elements/roads/<type>/`:

```
roads/<type>/
├── ui.json
├── parameters.json
├── settings.json
└── components/<component>.json      components specific to this type
```

Components can also be placed in `elements/sharedComponents/`, where they can be used by several road types, intersection types and border mesh types; a component is looked up in the type's own `components` folder first (then in those of the types it extends, see [Core Type Inheritance](Core-Type-Inheritance)).

The type name (folder name) is stored in each road's state and is used to name junction files (`<typeA>_<typeB>.json`), so it must not contain underscores.

Terrain patch border meshes use exactly the same format (see [Core Terrain Patches](Core-Terrain-Patches#border-meshes)).

## How a road is generated

1. The road curve is sampled into *sections* (one per segment boundary). For each section the program knows its center point, its direction and its `localRight` vector, rotated by the yaw and roll of the control points if the road has any (see [Rotated sections](#rotated-sections)).
2. The `sectionVertices` expressions give lateral offsets; section vertex `v<n>` is placed at `center + localRight * offset<n>`. These are the reference points of the cross section (edges of sidewalks, road, divider...).
3. Each **component** whose condition is true contributes a **cross-section** (a list of vertices, usually built from `v<n>`, heights and offsets) to every section, and faces that join each section to the previous one. Components can also have caps at the start and end of the road.
4. Faces are assigned to textures through `facesTextures` and `texturesMapping`; each texture becomes one submesh.
5. Anchors (for terrain patches and building lines), traffic lanes and props lines are computed from expressions at every section.

### Coordinate conventions

* Y is up, positions are in meters, and the coordinate system is left-handed.
* `localRight` is `normalize(cross(direction, up))`. In a left-handed system this vector points to the **left** of the direction of travel, so negative section offsets are on the right side (the MM2 core uses `v0` for the right edge, with an offset of `-0.5*totalWidth`, and `v6` for the left edge).
* Faces: a quad from vertex `j` to `j + 1` is visible from above when, looking along the direction of travel, vertex `j` is on the right and vertex `j + 1` on the left (that is, when the vertices are listed in increasing order of their offset along `localRight`). For vertical faces, think of the same rule rotated. If a face is invisible, swap the order of its two vertices.
* Cap triangles (start/end meshes) are visible from the side where they are listed clockwise.

### Rotated sections

The control points of a road can rotate its sections: a **yaw** around the vertical axis (positive to the right) and then a **roll** around the horizontal direction of the road (positive raises the right side, the side of the negative offsets). The values of the points are blended along the curve as the curve blends the points. A rotated section has:

* `localRight` rotated, and divided by the cosine of the yaw, so that the road keeps its width measured across it (the section vertices are farther apart along the skewed section).
* `localUp` rotated by the roll, so the parts built with it (heights, curbs, walls) lean with the road. The sections at an end connected to an intersection, and the roads through intersections, keep `localUp` vertical, as the intersections move their vertices only vertically.
* `localForward` rotated by the yaw (`cross(localUp, localRight)`).

A core that builds every vertex from `v<n>`, `localRight` and `localUp` needs nothing else. A road type can disable either rotation with `allowYaw` and `allowRoll` (for example, when the target game can't handle banked roads). A rolled road can't be made coplanar section by section, so `makeCoplanar` has no effect on it.

## `settings.json`

```json
{
    "variableSections": false,
    "sectionVertices": ["-0.5*totalWidthI", "-0.5*roadWidthT", "-0.5*trueDivWidth", "0",
                        "0.5*trueDivWidth", "0.5*roadWidthT", "0.5*totalWidthI"],
    "anchors": ["v6-anchorH", "v0-anchorH"],
    "propsContainers": ["right", "left", "middle"],
    "propsLines": [ ... ],
    "trafficTypes": [ { "name": "forward", "color": [0, 128, 255] } ],
    "trafficLanes": [ ... ],
    "textures": ["texture0", "texture1"],
    "texturesMapping": { "singleRoad": [1], "sidewalk_left": [0] },
    "widths": ["totalWidth", "roadWidth"],
    "getters": { "totalWidth": "totalWidthI", "roadWidth": "roadWidthT", ... },
    "components": [
        { "name": "sidewalk_right", "condition": "hasRealSidewalks" },
        { "name": "singleRoad", "condition": "not isDouble" }
    ]
}
```

| Key | Required | Description |
|---|---|---|
| `sectionVertices` | yes | Float expression array: the lateral offsets of the section vertices `v0`, `v1`, ... |
| `variableSections` | no | If true, the dynamic definitions are evaluated before computing each section's vertices, so offsets can vary along the road (by default they only use parameters and static definitions). |
| `allowYaw` | no | Default true. If false, the yaw of the control points has no effect on roads of this type (see [Rotated sections](#rotated-sections)). |
| `allowRoll` | no | Default true. If false, the roll of the control points has no effect on roads of this type. |
| `anchors` | no | Vec3 expression array: one terrain anchor line per entry (evaluated at every section). Usually the outer edges of the road. Components can add more. Without anchors, terrain patches and building lines cannot snap to the road. |
| `components` | yes | List of `{name, condition}`: the components of the road, in drawing order. The condition is a boolean expression evaluated per section; it can depend on dynamic variables. |
| `textures` | yes | List of texture *names*: each entry is the name of a parameter (or state property) holding a texture path, or a built-in material (`_TRANSPARENT_`). One submesh per entry. |
| `texturesMapping` | yes | For each component, the list of indices into `textures` that its `facesTextures` values refer to. Component face texture `k` uses `textures[texturesMapping[component][k]]`. |
| `propsContainers` | yes | Names of the props containers of this road type (see [Props](#props-lines)). Can be empty. |
| `propsLines` | no | Props lines defined at road level (components can define more). |
| `trafficTypes` | if lanes are used | List of lane types with a `name` and an RGB `color` (0-255) used to draw them. |
| `trafficLanes` | no | Traffic lanes defined at road level (components can define more). |
| `widths` | no | Names of the float parameters that are widths. With low-poly curves (or *adjust low-poly width*), they are multiplied at each section by `1 / cos(half the turning angle)` so that the road keeps its width at the corners. |
| `getters` | yes | Standard getters, see below. |

Keys not listed here are ignored by the program.

### Standard getters

Intersections, junctions and other parts of the program need some information about any road, regardless of its type. `getters` maps each standard name to a definition (or, for strings, a parameter) of the road type:

| Getter | Type | Used for |
|---|---|---|
| `totalWidth` | float | Full width of the road; used to size intersections. |
| `roadWidth` | float | Width of the drivable part; used for the intersection road surface. |
| `height` | float | Height of the road surface below the sidewalks (curb height); used by intersections. |
| `hasSidewalks` | bool | Whether the road has sidewalks (intersections check whether their roads agree). |
| `canBeSimplified` | bool | If true, a straight road with only two points is generated with a single segment. |
| `startCrosswalkTex`, `endCrosswalkTex` | string | Parameters holding the crosswalk textures, referenced by the intersection type settings. |
| any other name | | Can be used in a junction's `standardFloatsBoolsAndInts`, `standardVec3s`, `standardVec2s` (see [Core Intersections](Core-Intersections#junction-files)). |

A getter that is missing, or points to a missing definition, logs a warning once and returns 0/false.

### Built-in parameters

Besides the core's parameters, every road has these built-in state properties (edited in `mainGroup` and `intersectionGroup`): `type`, `segments`, `segmentsPer100m`, `project`, `projectAll`, `lpf` (smoothing), `makeCoplanar`, `pointPlaneTreshold`, `adjustLowPolyWidth`, `curveType` (0 Bézier, 1 Hermite, 2 low-poly), `subdivideEqually`, `hermiteTension`, `startCrosswalkSize`, `endCrosswalkSize`, `startIntersectionAdd`, `endIntersectionAdd`, `startIntersectionTexture`, `endIntersectionTexture`, `propRule`; and in the instance state `startIntersectionStart`, `startIntersectionEnd`, `endIntersectionStart`, `endIntersectionEnd`, `intersectionMove`, `startConnectionHeight`, `endConnectionHeight`, `startConnectionHeightAuto`, `endConnectionHeightAuto`. Preset values for them should be provided by the templates.

## Built-in variables

Available in every expression of a road type, in addition to its parameters and definitions:

| Variable | Type | When | Meaning |
|---|---|---|---|
| `worldUp`, `worldRight`, `worldForward` | vec3 | always | `(0,1,0)`, `(1,0,0)`, `(0,0,1)`. |
| `totalLength` | float | always | Length of the road curve. |
| `segments` | float | always | Number of sections. |
| `startCrosswalkSize`, `endCrosswalkSize` | float | always | Built-in road parameters. |
| `hasStartIntersection`, `hasEndIntersection` | bool | always | Whether each end is connected to an intersection. |
| `throughIntersection` | bool | always | True for the road pieces generated across an intersection. |
| `startIntersectionNumberOfRoads`, `endIntersectionNumberOfRoads` | float | always | Number of roads of each intersection. |
| `startIntersectionPosition`, `endIntersectionPosition` | vec3 | always | Center of each intersection (zero if none). |
| `startDir`, `endDir` | vec3 | always | Direction of the curve at the start and at the end. |
| `z` | float | per section | Distance of the section from the start of the road. |
| `segment` | float | per section | Index of the section, from 1 to `segments`. |
| `localUp`, `localRight`, `localForward` | vec3 | per section | Section frame. `localUp` is `(0,1,0)` unless the section is rolled (see [Rotated sections](#rotated-sections)). |
| `v<n>` | vec3 | per section | Position of section vertex `n`. |
| `x<n>` | float | per section | Distance of `v<n>` from the section center. |
| `absX<n>`, `absY<n>`, `absZ<n>` | float | per section | World coordinates of `v<n>` (useful for world-aligned UVs). |
| `localUp0`, `localRight0`, `localForward0`, `localUp1`, `localRight1`, `localForward1` | vec3 | per road | Frame of the first (`0`) and last (`1`) section. |
| `v<n>_0`, `v<n>_1` | vec3 | per road | Section vertex `n` of the first and last section. |
| `x<n>_0`, `x<n>_1`, `absX<n>_0`, ..., `absZ<n>_1` | float | late definitions | As above, for the first and last section. |

The order of evaluation for each road is: parameters and global variables → static definitions → section vertices of every section → `common` late definitions → for each section: per-section variables, dynamic definitions, then for each active component its local definitions and mesh.

## Component files

```json
{
    "localDefinitions": [
        { "name": "divHeight", "type": "vec3", "value": "if(divider=1, -h, -h+localUp*dividerParam)" }
    ],
    "mainMesh": {
        "vertices": ["v1-h", "v2-h", "v4-h", "v5-h"],
        "uvs": ["vec2(zRd, 0)", "vec2(zRd, 1)", "vec2(zRd, 1)", "vec2(zRd, 0)"],
        "faces": [0, 2],
        "facesTextures": [0, 0]
    },
    "startMesh": { ... },
    "endMesh": { ... },
    "anchors": [ ... ],
    "propsLines": [ ... ],
    "trafficLanes": [ ... ]
}
```

All keys are optional.

| Key | Description |
|---|---|
| `localDefinitions` | Definitions evaluated for each section before this component's expressions, only if the component's condition is true. |
| `mainMesh` | The cross-section of the component, repeated at every section. |
| `startMesh`, `endMesh` | Caps, generated once with the variables of the first and last section respectively. |
| `anchors` | Additional anchor lines (vec3 array). |
| `propsLines`, `trafficLanes` | As in `settings.json`, active only when the component is. |

### Mesh description

| Key | Type | `mainMesh` | `startMesh`/`endMesh` |
|---|---|---|---|
| `vertices` | vec3 array | The cross-section vertices of this component at the current section. | The cap vertices. |
| `uvs` | vec2 array | One UV per vertex. | One UV per vertex. |
| `faces` | float array | One entry per quad: index `j` (0-based, within this component's vertices) creates the quad between vertices `j` and `j + 1` of the previous section and of the current section. | Triangles: three vertex indices per face. |
| `facesTextures` | float array | One entry per face: the index into `texturesMapping[component]`. | Same. |

All four keys accept the [expression array](Expression-Language#expression-arrays) forms, so the number of vertices and faces can depend on parameters (for example with conditional blocks). The number of `mainMesh` vertices must be the same at every section, otherwise the quads join the wrong vertices.

To get hard edges or texture seams, repeat a vertex: in MM2's `sidewalk_right`, the vertex list `["v0", "v1", "v1", "v1-h"]` with faces `[0, 2]` creates the sidewalk top (`v0`–`v1`) and the curb (`v1`–`v1-h`) as separate quads with their own UVs.

## Traffic lanes

```json
"trafficLanes": [
    {
        "type": "if(pedestrianStreet, 0, 1)",
        "condition": "not disableTraffic",
        "bounds": ["trafV0", "trafV1"],
        "lanes": { "for": "i; 0; forwardLanes", "value": "(i+0.5)/totLanes" }
    }
]
```

| Key | Description |
|---|---|
| `type` | Float expression: index into `trafficTypes`. |
| `condition` | Optional boolean expression (evaluated once per road). |
| `bounds` | Two vec3 expressions: the two edges of the lane group, evaluated at each section. |
| `lanes` | Float expression array: one lane per value, at that position between the two bounds (0 = first bound, 1 = second bound). |

The direction of a lane is the direction of the road; lanes going the opposite way are usually given by swapping the bounds and using another traffic type (MM2 uses `forward` and `backward` types). The lanes are drawn on the road while editing and exported as polylines named `traffic_<type>_<component>_<group>_<lane>` in the road's `fields.lanes` (see [Input and Output](Input-and-Output#export-format-ioexportedcity-ioexportedcitycs-and-cityencoderjson_export)), where the exporter turns them into the game's AI paths.

## Props lines

```json
"propsLines": [
    { "containerName": "right", "condition": "hasRealSidewalks", "bounds": ["v1", "v0"] }
]
```

A props line is a strip along the road, between two bounds, where props are placed. `containerName` must be one of the road type's `propsContainers`. The bounds are evaluated at each section; props are placed by distance along the middle of the strip, and by position across it (0 = first bound, 1 = second bound), according to the props element types (see [Core Props and Objects](Core-Props-and-Objects)). The props line bounds are also exported as `props_<container>_start` and `props_<container>_end` lanes.

Users enable containers and fill them in the road's props tab (`propsGroup`). Each container needs the string `EDIT_PROP_CONTAINER_<NAME>` (container name in upper case).

## Example: a minimal road type

`elements/roads/simple/settings.json`:

```json
{
    "sectionVertices": ["-0.5*width", "0.5*width"],
    "anchors": ["v1", "v0"],
    "propsContainers": [],
    "textures": ["texture"],
    "texturesMapping": { "surface": [0] },
    "getters": {
        "totalWidth": "widthD",
        "roadWidth": "widthD",
        "height": "zeroHeight",
        "hasSidewalks": "noSidewalks",
        "canBeSimplified": "alwaysTrue"
    },
    "components": [ { "name": "surface", "condition": "true" } ]
}
```

`elements/roads/simple/parameters.json`:

```json
{
    "parameters": [
        { "name": "width", "label": "WIDTH", "placeholder": "WIDTH_PH", "type": "float" },
        { "name": "texture", "label": "TEXTURE", "placeholder": "TEXTURE_PH", "type": "texture" }
    ],
    "internalParametersSettings": [],
    "staticDefinitions": [
        { "name": "widthD", "type": "float", "value": "width" },
        { "name": "zeroHeight", "type": "float", "value": "0" },
        { "name": "noSidewalks", "type": "bool", "value": "false" },
        { "name": "alwaysTrue", "type": "bool", "value": "true" }
    ],
    "dynamicDefinitions": [
        { "name": "u", "type": "float", "value": "z/width" }
    ]
}
```

`elements/roads/simple/components/surface.json`:

```json
{
    "mainMesh": {
        "vertices": ["v0", "v1"],
        "uvs": ["vec2(u, 0)", "vec2(u, 1)"],
        "faces": [0],
        "facesTextures": [0]
    }
}
```

`elements/roads/simple/ui.json`:

```json
{
    "label": "SIMPLE_ROAD",
    "menuWidth": 1.5,
    "tabs": [
        {
            "label": "",
            "elements": [
                { "name": "mainGroup", "width": 1.5 },
                { "name": "intersectionGroup", "width": 1.5 },
                { "name": "width", "width": 1.5 },
                { "name": "texture", "width": 1.5 }
            ]
        }
    ]
}
```

Note that the getters point to definitions: numeric, boolean and vector getters need a definition of the right type (here `widthD` re-exposes the `width` parameter), while string getters point directly to a parameter.

To be usable, the road type also needs an intersection type with a junction for the pair `simple_simple` (see [Core Intersections](Core-Intersections)), the strings used as labels, and road presets in the templates (see [Core Templates and Presets](Core-Templates-and-Presets)).

# Core Extras

An *extra* is an optional piece of geometry of a road or intersection type, made by a function of the [Lua scripts](Core-Lua-Scripts) of the core while a condition is true, for example decals drawn along the road and across the intersection when a flag is on. Extras are part of their element: they are rebuilt with it, they are not separate elements, and they can't be selected or edited on their own. The data their functions return for the exporters is in the exported fields of the element.

The Midtown Madness 2 core draws its cable car rail decals with extras (see the end of this page).

## `settings.json`

The road types ([Core Roads](Core-Roads#settingsjson)) and the intersection types ([Core Intersections](Core-Intersections#settingsjson)) can list their extras:

```json
"extras": [
    { "name": "cableCarRails", "condition": "cableCarRails and cableCarLine", "geometry": "cableCars.road" }
]
```

| Field | Description |
|---|---|
| `name` | The name of the extra, in the exported data and in the errors. |
| `condition` | Optional boolean [expression](Expression-Language) over the variables of the element (its parameters and definitions). While it is false the function is not called and the extra has no geometry. Without a condition the extra is always on. |
| `geometry` | `<module>.<function>` of the `lua` folder of the core. |

The flags and the options of an extra are ordinary [parameters](Parameters-and-UI) of the type. Their fields can be disabled while they don't apply with the `enabledWhen` of the [ui.json elements](Parameters-and-UI#uijson).

## The function

It gets the usual two arguments ([Core Lua Scripts](Core-Lua-Scripts)): the variables of the element (`v`) and a context (`ctx`). The context of a road:

| `ctx` | Content |
|---|---|
| `extra` | The name of the extra. |
| `typeName`, `name` | The type and the name of the road. |
| `parameters`, `instanceParameters` | Detached snapshots of its state and of its instance state (also the strings, e.g. the textures, which `v` doesn't have). |
| `road` | The road as the context of its definitions: `ctx.road:intersection("start")` gives the intersection at its start with its roads, their variables and parameters (see [Core Lua Scripts](Core-Lua-Scripts#context)). |
| `center` | The points of its curve (`vec3`), from its start to its end. |
| `sections` | The vertices of its sections (a list per section of `vec3`, the section vertices of the type), from its start to its end. |
| `lanes` | Its traffic lanes and the bounds of its props lines as drawn: `{name, type, points}`, `name` as in the export (`traffic_<type>_<component>_<list>_<lane>` or `props_<container>_start`/`_end`), `type` the lane type of a traffic lane (nil for the props lines), `points` one per section. |

The context of an intersection:

| `ctx` | Content |
|---|---|
| `extra` | The name of the extra. |
| `typeName`, `name` | The type and the name of the intersection. |
| `parameters`, `instanceParameters` | Detached snapshots of its state and of its instance state. |
| `center` | Its point. |
| `roads` | Its roads, clockwise: `{name, typeName, atStart, parameters, vars, center, sections, lanes}`, `atStart` true if the start of the road is on the intersection (a road with both ends on it is listed twice, its start first), `vars` its variables, the others as in the context of a road. |

The roads are rebuilt before their intersections, so an intersection reads their current lanes and sections. A change of an intersection rebuilds its roads too, so a road can depend on its intersections (e.g. leave the end of a decal to the intersection when the intersection draws it).

The function returns `{meshes, export}`, or nil for nothing:

- `meshes`: in the format of the meshes of the [custom elements](Core-Custom-Elements#geometry): `texture` or `color`, `vertices`, `uvs`, `triangles` (clockwise when seen from their visible side). `texture` is a texture parameter of the element, a built-in material, or a texture path (e.g. `"texture/r4i_rails_f.tex"`) when the element has no value with that name: a default texture for a parameter left empty. `pickOnly` and `placeholder` meshes are ignored. The meshes are drawn in a child of the element, not in its mesh: they are not in its exported mesh, can't be clicked on and don't change its collider.
- `export`: any table, for the exporters. Lists become arrays and other tables objects; `vec3` and `vec2` values become arrays of numbers. Coordinates are in the space of the program, as everything else in the export.

An error in the function is reported once and leaves that extra out.

## Export

The exported fields of the roads and intersections have `extras`: a list of `{name, export}`, one per extra whose condition was true at the last rebuild (`export` is null if the function returned none). The roads through the intersections have none.

```json
"fields": {
    "extras": [ { "name": "cableCarRails", "export": { "decals": [ { "texture": "texture/r4i_rails_f.tex", "pairs": [[0, 0, 0], [2, 0, 0], ...] } ] } } ],
    ...
}
```

## Example: Midtown Madness 2 cable car rails

The `psdl` roads and intersections have the extra `cableCarRails` (`lua/cableCars.lua` of the core):

- **Roads:** the road draws strips of `cableCarRailTexture` (`r4i_rails_f` if empty) along its two cable car rails, its traffic lanes of type 3 and 4, while `cableCarRails` and `cableCarLine` are on. A rail arriving at an intersection that draws its rails stops where the cable cars leave the road.
- **Intersections:** the intersection draws the rails along the path the cable cars take in the game from each cable car road into the next one, with squares of `cableCarCrossingTexture` where two rails cross (unless `cableCarHideCrossings`).
- **Export:** both return their strips as pairs of points across them in `export.decals`, which the exporter writes to the decals of the city.
- **Panels:** the panels disable the flags with `enabledWhen` predicates while they don't apply: a road needs `cableCarLine`, and an intersection needs 2 or 4 cable car roads.

This page was written with Claude Code.

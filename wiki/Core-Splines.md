# Core Spline Types

Roads, road-style geometry and spline-driven custom elements share a curve registry. The built-in curves keep their saved IDs: `0` Bézier, `1` Hermite and `2` low-poly. A core can define additional curves without adding game-specific code to the builder.

## Definitions and availability

An optional `splineTypes.json` in the core folder contains an array:

```json
[
    {
        "id": 3,
        "name": "myCurve",
        "label": "MY_CURVE_LABEL",
        "prepare": "curves.prepare",
        "evaluate": "curves.evaluate",
        "controlParameters": "curves.controlParameters",
        "interpolateValues": "curves.interpolateValues",
        "split": "curves.split",
        "merge": "curves.merge"
    }
]
```

IDs must be unique integers of at least 3; names must be unique and cannot use the built-in names `bezier`, `hermite` or `lowPoly`. Keep IDs and names stable across core updates. Labels are core string keys; functions are in its `lua` folder, using the usual `<module>.<function>` references. `evaluate` and `controlParameters` are required; the other callbacks are optional.

A road type enables curves with `curveTypes` and `defaultCurveType` in its `settings.json`. A custom element type uses the same fields inside `spline`. Registration alone makes a curve available to neither: absent road settings retain the three built-in modes and existing defaults. For example, a core's train curve can be allowed only by its train type while its roads keep `[0, 1, 2]`.

The road editor and API schema list only the allowed choices. `state.setBuiltin` accepts their stable names or persisted numeric IDs, which are not dropdown indices. A road schema's `enumValues` and `enumIds` are parallel lists. Custom elements retain the integer `curveType` parameter. Presets and imports must respect the element's allowlist.

## Evaluation

Callbacks run on the main thread under the standard Lua instruction budget, as `function(v, ctx)`. `v` is a detached table of saved element parameters. The input contains no scene nodes or mutable live state:

| Context field | Meaning |
|---|---|
| `points` | Authored positions as a 1-based array of `vec3`, in builder coordinates. |
| `state` | The detached saved parameters, also available as `v`. |
| `startTangent`, `endTangent` | Optional imposed road-end tangents, used by seamless intersections. |
| `data` | The result of `prepare`, when supplied, reused during this rebuild. |
| `t` | Normalized curve parameter, from 0 to 1. |
| `values` | A 1-based array of `vec2` attributes, for `interpolateValues` (road yaw/roll). |

`prepare` is called once for a prepared curve. `controlParameters` returns a nondecreasing numeric array with one entry per authored point; for two or more points it must begin at 0 and end at 1. These parameters locate handles and attributes on the curve. They do not require the curve to pass through every control point, as with Bézier controls.

`evaluate` returns `{position = vec3(...), tangent = vec3(...)}`. The tangent is optional; when absent, the builder estimates it with nearby position queries. A road evaluator must account for imposed endpoint tangents when present. It must handle the incomplete inputs encountered during point placement. Non-finite positions, tangents and attributes are rejected; callback errors never silently select another curve.

`interpolateValues` returns a `vec2` at `ctx.t`. Without it, values are interpolated linearly between the declared control parameters. This choice does not change position interpolation. The builder keeps sample parameters with sampled positions, so nonuniform sampling does not turn section or handle queries into uniform-index lookups.

Road geometry, its editor line, directions, banking and handle queries use the shared evaluator. Projection, section geometry and the existing smoothing options still belong to the element. A custom curve can expose extra parameters through the usual core parameters and UI; built-in Hermite tension is not an editable custom-curve parameter.

## Splitting and merging roads

Custom curves use their own optional `split` and `merge` callbacks. Missing callbacks produce an unsupported-operation result, rather than invoking built-in curve fitting.

For `split`, `ctx.road` contains `points`, `rotations` (a `vec2` per point), `state`, optional end tangents, `startIntersection`/`endIntersection` booleans and `startClearance`/`endClearance` distances. `ctx.t` is the split parameter. Return `{first = controls, second = controls}`.

For `merge`, `ctx.first` is the kept road and `ctx.second` the other road, each with the same input fields. `ctx.keepAtEnd` and `ctx.otherAtStart` identify the ends at the joint; `ctx.joint` is its position. Return one `controls` object in the kept road's direction, accounting for any reversal of the other road and its rotations. A callback can decline an unsupported pairing with `nil` or an error.

Each `controls` object has:

```lua
{
    points = { vec3(...), vec3(...) },
    rotations = { vec2(...), vec2(...) },
    sources = { {road = 1, index = 1}, false },
    state = { myCurveParameter = ... }
}
```

There must be 2–10000 points and an equal number of rotations. `sources` is optional: a source maps to a retained authored handle, keeping its identity, position and attachments. Indices are 1-based; `road` is 1 for a split, or 1/2 for the kept/other merge input. Use `false` for a new point. A handle cannot occur twice or be retained by both split parts. Callback state updates may change only declared core parameters, not `type`, `curveType` or arbitrary built-in properties.

The builder validates both split outputs, the joins, the far ends, source mappings, parameter updates and prepared output curves before changing the scene. Application uses the normal undo transaction. The callback is responsible for describing the intended shape and accounting for the intersection constraints; there is no automatic approximation to another curve.

## Lines without surface geometry and export

A custom element spline can omit components and use empty `sectionVertices`, producing only its guide line and selection collider. Optional `spline.line` and `spline.controlLine` objects set a named `color` and `always` visibility; the latter draws the authored control polygon. Without styling, the existing yellow editing line is retained.

`previewTolerance` enables adaptive custom element display sampling, seeded at the control parameters and checking quarter, midpoint and three-quarter chord deviation. `previewMaxDepth` caps subdivision (maximum 8). These settings affect derived display samples, not authored controls.

Custom element export JSON already contains authored controls in `points.spline`, each `{id, position}`; roads contain authored positions in `b64v3a_points` (`points` after Python decoding). Sampled custom element `spline.sections`/`pieces` are separate derived data; sections also carry their curve `parameter`. An exporter whose game interpolates curves should use the authored points.

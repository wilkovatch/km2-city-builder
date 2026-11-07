# Core Prop Placers

A **PropPlacer** is a capability of a core-defined [custom element](Core-Custom-Elements), selected by `settings.propPlacer`. Its authored control points and properties are saved in `customElements`; its generated props are derived geometry. It uses the ordinary custom-element API, presets, undo/redo, attachments and selection. Clicking any generated prop opens its owner.

Placers can also declare `propertyContainerCategory` to belong to a selected [city-property container](Core-City-Properties#owned-elements-and-contextual-creation). Ownership applies to the whole placer and its generated props, follows the container's visibility, and stays outside property presets. Exporters receive the ownership identifier with the ordinary placer state.

The full properties panel opens before the first click. Spline placers append controls like other splines. Polygon placers append perimeter vertices like terrain patches; clicking the first vertex finishes the perimeter. Ctrl+click follows connected road anchors or shared perimeter edges, including intermediate attachments on those edges (the shortest way, as `followConnections` for the spline types, see [Core Custom Elements](Core-Custom-Elements#following-the-connections)). The usual point sharing, insertion, deletion and detach tools apply.

## Spline placers

Use `"propPlacer": {"type": "spline", ...}` together with the ordinary `spline` settings. The spline supplies its core-defined curve allowlist, default curve, sampling and segment controls. Do not define separate `controlPoints`, `mesh` or `geometry` settings.

```json
{
  "presets": true,
  "spline": {
    "curveTypes": [0, 1, 2], "defaultCurveType": 2, "segments": 20,
    "line": {"color": "cyan", "always": true},
    "sectionVertices": [], "components": [], "textures": [], "widths": []
  },
  "propPlacer": {
    "type": "spline",
    "strips": [{
      "name": "main", "label": "EDIT_MAIN_PROPS",
      "start": "-0.5*width", "end": "0.5*width",
      "allowedContainerTypes": ["line", "custom"],
      "inline": true,
      "boundsParameters": ["width"]
    }]
  }
}
```

Define `width` in `parameters.json` in this example. Each named strip has its own [props container](Core-Props-and-Objects), in `state.propRule.<name>`. `start` and `end` are signed lateral-offset expressions over the element's parameters. Equal offsets give a zero-width strip, without its outer guide lines. Strip names must be unique and cannot contain dots. An omitted or empty `allowedContainerTypes` permits every props type that isn't `nestedOnly`; a nonempty list restricts the editor, API and presets, and the first one is the default. The ranges of the parameters of a type (`range`, inclusive `[minimum, maximum]` optionally followed by the step) are checked here: a value outside them is a placement error, shown in the panel.

| Strip setting | Meaning |
|---|---|
| `inline` | The type and the fields of the props are directly in the panel of the element, in `propsGroup`, always enabled. Without it the group has a checkbox and a button opening a panel, as for the roads. It needs a type with a single strip (the fields of a second strip would be the same ones) and with `presets: true`: the only preset dropdown is the one of the element, which saves the props too. |
| `boundsParameters` | The parameters of the element that give the bounds, hidden from the panel while the props type is `lineOnly`. |

A props type with `lineOnly` has no bounds on a strip, whatever `start` and `end` give: the props follow the spline and there are no outer guide lines. The panel is rebuilt when the type of the props changes, also through a preset or an undo.

Use `mainGroup`, `projectToGround`, `splineGroup`, `pointsGroup`, `propsGroup` and `propPlacerGroup` in the type's UI. Ordinary core parameters, such as width, can be inserted between these groups. Placer properties use per-type custom-element presets; they do not expose the road-rule preset selector.

## Polygon placers

Use `"propPlacer": {"type": "polygon", ...}` without `spline`. The builder defines a closed `points.perimeter` array, initially empty. Valid simple polygons can be concave; holes are not supported. Degenerate or self-intersecting polygons remain editable, produce no props, and show a validation error. Fewer than three points produce no props.

| Setting | Meaning |
|---|---|
| `maxModels` | Maximum number of model entries; `1` permits one model, `0` or omitted permits any number. It does not limit instance count. |
| `density` | Expose per-model density, default `true`. |
| `modelParameters` | Additional parameter definitions for each model entry. |
| `placement` | Optional Lua callback, `<module>.<function>`, replacing the default distribution. |
| `meshTransform` | Use the core's mesh-placement transform, default `false`. |
| `autoYOffset` | Apply the normal mesh-bottom fallback, default `true`. |

The builder supplies `projectToGround`, `seed` and `models` parameters. Each model entry has a `mesh`, optional `density`, and `minYaw`/`maxYaw` in degrees, followed by the core's custom model parameters. Add ordinary element parameters in `parameters.json`. Model assets are chosen by the user; the core configures their number and fields.

Default filling samples the XZ polygon uniformly by area. Each model independently gets `floor(area * density / 100)` instances, where area is in square metres. New model entries default to density 1, yaw 0–360 degrees, and unit scale. With projection off, heights interpolate across the polygon's triangulation. Random choices use a saved seed and a local deterministic sequence, preserving results across rebuilds, save/reload and export. Overlaps are allowed. Generation reports an error above 100,000 instances rather than freezing the editor.

Use `mainGroup`, `projectToGround`, `seed`, `models`, `pointsGroup` and `propPlacerGroup` in the UI. The last group displays placement validation errors.

### Custom polygon logic

The callback works like the [one of a props type](Core-Props-and-Objects#the-placement-callback), with this context. It receives `(variables, context)`. `variables` contains normal expression variables. The context includes the usual custom-element context plus:

- `perimeter`: ordered `vec3` vertices; `triangles`: flat zero-based triangulation indices; `area`: XZ area in square metres.
- `parameters`: the complete element parameter state; `models`: model entries with normal and custom per-model values.
- `random()`: the next number in `[0, 1)` from this element's seeded sequence. Use this for repeatable custom randomness.

Return a list of `{model = index, position = vec3(...), rotation = vec3(...), scale = vec3(...)}` entries. `model` is an index in `models`, **one-based**, or a mesh path; without one the entry is exported as a place only. Rotations are Euler degrees (or `forward`, a direction). Rotation defaults to zero and scale to one. `data` is an optional table for the exporters. Invalid model indices or non-finite transforms report a placement error. The builder applies projection and mesh-placement transforms after the callback.

```lua
return { fill = function(v, ctx)
    return {{
        model = 1,
        position = ctx.perimeter[1] + vec3(ctx.random() * v.spread, ctx.models[1].height, 0),
        rotation = vec3(0, 90, 0)
    }}
end }
```

Here `spread` is a normal float parameter and `height` is a float in `modelParameters`.

## Projection and export

Both macro types have a saved **Project to ground** toggle. Its initial value comes from `propPlacer.projectToGround`, default `true`. `projectionMask` defaults to 136 (terrain and generated geometry); the existing core surface-projection policy applies. Attachments retain priority over control-point projection. Moving controls or changing projection surfaces automatically updates affected geometry, including after physics synchronization and undo/redo. Generated props and overlays are excluded from projection targets.

`propPlacer.guideHeightOffset` lifts the displayed guides and their picking colliders above the authored geometry, defaulting to 0.05 m. It is added to any spline line-style offset and does not change control points, generated props or exported paths.

Each generated position is projected separately, after the placement callback, which can tell it from `projected` in its context. The spline is projected at its sections too, and the callback walks that line, so the ground changes the distances along it: a props type with `authoredLine` walks the spline as authored instead, and the ground gives only the height of each prop. Disabling the saved toggle preserves authored shape heights. Random choices do not change when only projection changes.

The guides show where the props go, without changing the shape the props and the exporters read:

- With **Project to ground** on, each guide line (also the outer ones) is on the surface under it, with a point every 2 m at most (up to 512 points per line).
- With it off, the guides stay on the path of the props. The parts under a surface of `projectionMask` are drawn faintly through whatever covers them. This check doesn't use the depth buffer, because the terrain is a transparent grid that doesn't hide what's under it.

A guide is clicked through a collider of its own instead of the collider of the element: about 6 times the width of the line, so it keeps about the same size on screen, with a vertical ribbon to click it with a low camera too. It's on a physics-only layer that only the actions picking an element use (`Raycast.DoPickRaycast`), so it's not a surface for projections and placement. A part drawn through a surface can be clicked through it.

Exports contain ordinary custom elements, evaluated `data.propLines` placements and, for splines, the evaluated `spline` data. `validationError`, when present, identifies an invalid placement that the core exporter must report. Exporters choose native paths or evaluated individual placements according to the core type; road-specific prop suppression must not suppress standalone placer instances.

The former experimental standalone-rule type, API and save collection have been removed. A core migration must strip obsolete records before city decoding; the builder has no legacy loading fallback for them.

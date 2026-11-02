# Core Prop Placers

A **PropPlacer** is a capability of a core-defined [custom element](Core-Custom-Elements), selected by `settings.propPlacer`. Its authored control points and properties are saved in `customElements`; its generated props are derived geometry. It uses the ordinary custom-element API, presets, undo/redo, attachments and selection. Clicking any generated prop opens its owner.

The full properties panel opens before the first click. Spline placers append controls like other splines. Polygon placers append perimeter vertices like terrain patches; clicking the first vertex finishes the perimeter. Ctrl+click follows connected road anchors or shared perimeter edges, including intermediate attachments on those edges. The usual point sharing, insertion, deletion and detach tools apply.

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
      "allowedContainerTypes": ["line", "custom"]
    }]
  }
}
```

Define `width` in `parameters.json` in this example. Each named strip has its own existing [props container](Core-Props-and-Objects), in `state.propRule.<name>`. `start` and `end` are signed lateral-offset expressions over the element's parameters. Equal offsets give a zero-width strip. Strip names must be unique and cannot contain dots. An omitted or empty `allowedContainerTypes` permits every core container type; a nonempty list restricts the editor, API and presets.

A strip can also define a scoped placement profile: `placementRules` has the same fields as a props element's placement rules, `parametersInfo` supplies its parameter schema, and `uiInfo` supplies the exposed fields. The profile applies only inside that strip. The original props element's model multiplicity is retained. Supply all required placement expressions when defining a profile. Optional `ranges` maps parameter names to inclusive `[minimum, maximum]` values, optionally followed by a third value specifying the step; integer parameters must also contain integers. Ordinary road containers keep their original definitions.

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

The callback receives `(variables, context)`. `variables` contains normal expression variables. The context includes the usual custom-element context plus:

- `perimeter`: ordered `vec3` vertices; `triangles`: flat zero-based triangulation indices; `area`: XZ area in square metres.
- `parameters`: the complete element parameter state; `models`: model entries with normal and custom per-model values.
- `random()`: the next number in `[0, 1)` from this element's seeded sequence. Use this for repeatable custom randomness.

Return a list of `{model = index, position = vec3(...), rotation = vec3(...), scale = vec3(...)}` entries. Model indices are **one-based**; rotations are Euler degrees. Rotation defaults to zero and scale to one. Invalid model indices or non-finite transforms report a placement error. The builder applies projection and mesh-placement transforms after the callback.

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

`projectProps` defaults to `true`: each generated position is projected separately. A core can set it to `false` for native procedural paths whose props must interpolate the projected path. Disabling the saved toggle preserves authored shape heights. Random choices do not change when only projection changes.

Exports contain ordinary custom elements, evaluated `data.propLines` placements and, for splines, the evaluated `spline` data. `validationError`, when present, identifies an invalid placement that the core exporter must report. Exporters choose native paths or evaluated individual placements according to the core type; road-specific prop suppression must not suppress standalone placer instances.

The former experimental standalone-rule type, API and save collection have been removed. A core migration must strip obsolete records before city decoding; the builder has no legacy loading fallback for them.

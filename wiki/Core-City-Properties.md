# Core City Properties

The city properties are the parameters of the city as a whole: name, traffic settings, races, opponents and so on in MM2. They are edited from *Edit → City properties*, saved with the city and exported in the `cityProperties` field of the export JSON, where the core's exporter uses them. A template can give their initial values with a `cityProperties.json` (see [Core Templates and Presets](Core-Templates-and-Presets#citypropertiesjson)).

```
elements/cityProperties/
├── ui.json
├── parameters.json
└── settings.json
elements/customCityPropertiesTypes/<type>.json
```

## `parameters.json` and `ui.json`

Same format as for other elements (see [Parameters and UI](Parameters-and-UI)); all parameter types are available, including `array`, `customCityProperties` and `objectInstance`, which makes the city properties the natural place for anything that is not tied to a single element (race checkpoints, opponent paths, police cars...). There are no built-in groups, type selector or presets in this panel; a Close button is added at the end of the first tab. Parameters cannot be `instanceSpecific`.

One parameter name has a special meaning:

| Parameter | Effect |
|---|---|
| `cityFileName` (string) | Default file name proposed when exporting the city. |

## `settings.json`

```json
{
    "injectableParameters": [
        { "parameter": "trafficLight1", "onRoads": true },
        { "parameter": "trafficLight2", "onRoads": true }
    ]
}
```

### Injectable parameters

Each parameter listed in `injectableParameters` with `onRoads: true` is copied into the runtime state of every road as `_GLOBAL_<parameter>` whenever it changes, and the roads are rebuilt. Supported parameter types: `string`, `mesh`, `bool`, `float`, `int`.

Injected values are not expression variables: they are state properties, meant to be used where the program reads state properties by name, most notably the dynamic default model of a [parametric object](Core-Props-and-Objects#parametric-objects). MM2 uses this to let the user choose the two traffic light models once for the whole city:

```json
"defaultModel": {
    "dynamic": true,
    "options": ["_GLOBAL_trafficLight1", "_GLOBAL_trafficLight2"],
    "index": "startTrafficLight_meshIndex"
}
```

## Custom city properties types

A custom city properties type (`customCityPropertiesTypes/<type>.json`) is a reusable group of parameters, used by `customCityProperties` parameters and by `array` parameters with `customCityPropertiesType`.

```json
{
    "label": "name",
    "exclusiveEditing": {
        "enabled": true,
        "optional": false,
        "category": "race"
    },
    "parameters": [
        { "name": "name", "label": "RACE_NAME", "type": "string" },
        { "name": "checkpoints", "label": "CHECKPOINTS", "type": "array",
          "arrayProperties": { "maxElements": -1, "customCityPropertiesType": "checkpoint" } }
    ]
}
```

| Key | Description |
|---|---|
| `label` | Name of the parameter whose value is shown in lists of elements of this type (`null` for "Element N"). |
| `parameters` | The parameters of the type. Custom city properties types can contain arrays and other custom city properties, at any depth. |
| `exclusiveEditing` | Optional. When `enabled`, the parametric objects of only one element of the `category` are visible at a time: the one being edited. With `optional: true`, all elements are visible when none is being edited, and the element panel gets a button to hide the others. With `keepLinesVisible: true`, the elements not being edited hide only their objects, and the lines through them (`drawLine` arrays) stay visible (e.g. the decals of the MM2 core, whose points only show while editing them). |

`exclusiveEditing` keeps the scene readable when many elements contain objects in the same place, for example the checkpoints of different races (MM2 shows the checkpoints of one race at a time) or the paths of the opponents of a race.

The elements of the city properties can be selected by clicking them in the city, when no panel is open. Clicking a parametric object opens the city properties panel, then the panels of the elements that contain the object, then the transform panel on the object, with the innermost element panel as its parent. This is the same chain the user would open by hand. Clicking a `drawLine` line opens the panel of the element the line belongs to (e.g. a decal of the MM2 core, whose points are hidden until it is edited).

Parametric objects inside custom city properties types are not positioned by definitions (custom city properties types have none): they appear at the origin and the user places them. For points that only mark a position, the special model name `@handle` shows a small handle instead of a mesh (MM2 uses it for opponent waypoints).

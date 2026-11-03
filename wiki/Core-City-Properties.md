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

Set `onCustomElements: true` to also inject a parameter into custom elements, including their Lua geometry and spline definitions. Custom-element injection is runtime-only and does not add authored state properties. Changed injected parameters rebuild the targeted elements; new, restored and loaded elements receive the current values too.

Injected `bool`, `int` and `float` parameters are also available as `_GLOBAL_<parameter>` expression variables on their targets, populated before static definitions. Boolean values are booleans in Lua. For example, a lane's `arrowDirection` can be `"if(_GLOBAL_driveOnLeft, -1, 1)"` when `driveOnLeft` is injectable with `onRoads: true`; a custom-element geometry script can read `v._GLOBAL_driveOnLeft` with `onCustomElements: true`. The placement rules of the props types can read them too, with the value of the element that owns the prop: a parameter injected only on roads reads as 0 on the props of a custom element, and the other way around. Other injectable types remain state properties, most notably for the dynamic default model of a [parametric object](Core-Props-and-Objects#parametric-objects). MM2 uses this to let the user choose the two traffic light models once for the whole city:

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
| `uiInfo` | Optional. The layout of the panel of the type, see [Panel layout](#panel-layout). |
| `exclusiveEditing` | Optional. When `enabled`, the parametric objects of only one element of the `category` are visible at a time: the one being edited. With `optional: true`, all elements are visible when none is being edited, and the element panel gets a button to hide the others. With `keepLinesVisible: true`, the elements not being edited hide only their objects, and the lines through them (`drawLine` arrays) stay visible (e.g. the decals of the MM2 core, whose points only show while editing them). |

`exclusiveEditing` keeps the scene readable when many elements contain objects in the same place, for example the checkpoints of different races (MM2 shows the checkpoints of one race at a time) or the paths of the opponents of a race.

The elements of the city properties can be selected by clicking them in the city, when no panel is open. Clicking a parametric object opens the city properties panel, then the panels of the elements that contain the object, then the transform panel on the object, with the innermost element panel as its parent. This is the same chain the user would open by hand. Clicking a `drawLine` line opens the panel of the element the line belongs to (e.g. a decal of the MM2 core, whose points are hidden until it is edited).

Parametric objects inside custom city properties types are not positioned by definitions (custom city properties types have none): they appear at the origin and the user places them. For points that only mark a position, the special model name `@handle` shows a small handle instead of a mesh.

## Panel layout

By default the panel of a custom city properties type shows all its parameters, one per row. `uiInfo` lays them out instead, with the elements of a `ui.json` tab (see [Parameters and UI](Parameters-and-UI#uijson)). For example, two variants of each value (per difficulty) side by side, under a label covering the row:

```json
"parameters": [
    { "name": "speed", "label": "EASY", "type": "float" },
    { "name": "speed_hard", "label": "HARD", "type": "float" },
    { "name": "markers", "label": "EASY", "type": "customCityProperties", "customCityPropertiesType": "markers" },
    { "name": "markers_hard", "label": "HARD", "type": "customCityProperties", "customCityPropertiesType": "markers" }
],
"uiInfo": [
    { "name": "label:SPEED", "width": 1.5 },
    { "name": "speed_hard", "width": 0.75 },
    { "name": "speed", "width": 0.75 },
    { "name": "label:MARKERS", "width": 1.5, "height": 0.2 },
    { "name": "markers_hard", "width": 0.75 },
    { "name": "markers", "width": 0.75 }
]
```

Rows are filled from the right, so the element meant for the left comes second. The panel is 1.5 wide and has one page. As in a `ui.json`, parameters that aren't listed can't be edited, and also can't be selected by clicking their objects in the city.

`uiInfo` has one more kind of element, `content:<label>`: the button of the [content group](#owned-elements-and-contextual-creation) with that `label`.

## Owned elements and contextual creation

The city itself can be a singleton container. Set `elementContainer` in `elements/cityProperties/settings.json`, using the same metadata as a custom city properties type. Its state is the city-properties state, its identity is the usual `_elementContainerId`, and its contents use the same ownership, lists, creation and undo paths as other containers.

An optional `tab` (the string key of a city-properties tab) automatically selects the singleton when that tab, its contents list, an owned element or its creation draft is edited. Like the other containers it stays selected when these editors are closed, so that its contents stay visible and can be clicked, until it is deselected from the Containers menu (which also closes the editors) or the city-properties panel switches from its tab to another one. Its Containers-menu Edit action opens that tab. For example:

```json
"elementContainer": {
    "category": "annotations",
    "tab": "ANNOTATIONS_TAB",
    "types": ["annotation"],
    "contentGroups": [{ "label": "ANNOTATIONS_LIST", "types": ["annotation"] }]
}
```

Place `content:ANNOTATIONS_LIST` in the tab's `elements` to open the shared contents list. These singleton content buttons take a full row. Core geometry scripts define what owned elements draw; the container only controls ownership and visibility. Saved cities use the existing identity/owner fields.

A custom city properties type can own freely placed mesh instances and custom elements by defining `elementContainer`:

```json
"elementContainer": {
    "category": "scenario",
    "types": ["obstacle", "route"],
    "commands": [{"label": "ADD_MARKER", "path": "markers"}]
}
```

`category` matches the type's enabled `exclusiveEditing.category`. `types` lists the custom-element and mesh-instance type names accepted by the group. Eligible element types set `settings.propertyContainerCategory` to that category; `requiresPropertyContainer: true` hides their Create entries until an accepting container is selected. Optional types remain creatable without a container. Container-targeted Create entries and their submenu labels are blue.

Each command adds ordinary single-object array entries by clicking in the city. `path` is a dot-separated path through nested custom city properties to an array. Alternatively, supply `alternatePaths` and matching localized `alternateLabels` arrays to expose a target dropdown; the first target is the default. Commands appear only while their container is selected.

While a container is selected, the containers menu at the end of the menu bar has an entry to open its panel and one to deselect it. Their text is "Edit" and "Deselect" followed by the name of the category (the `CUSTOM_ELEMENT_CATEGORY_<CATEGORY>` string); `editLabel` and `deselectLabel` (string keys, optional) replace it where a more specific text fits. A container can have empty `types`, to get only the commands and the menu entries: it owns nothing, every Create entry keeps targeting the city, and its panel has no contents list.

The container panel has a button for each list of its owned elements, opening it in a sub-panel with Edit and Delete actions. The element selected in the list is outlined in the city while the list is shown. By default there is one list of all the contents. `contentGroups` splits them:

```json
"contentGroups": [
    {"label": "EASY_OBSTACLES", "hint": "OBSTACLES_HINT", "types": ["obstacle"], "filterParameter": "difficulty", "filterValue": 0},
    {"label": "ROUTES", "types": ["route"]}
]
```

`label` is the string key of the button and of the list. `buttonLabel` (optional, a string key) replaces it as the text of the button, for example when a row label already says what the list is. The buttons are placed by the `content:<label>` elements of [`uiInfo`](#panel-layout), in any row and at any width; the groups it doesn't list follow the layout, one per row. `hint` (optional) is a string key shown under the list, for example to say where the elements are created. `types` (optional) limits the list to these types. `filterParameter` (optional, an int or enum parameter of the element, 0 if missing) limits it to the elements where it has the value `filterValue`.

The panel of an owned element has a Back button: it returns to the list it was opened from, or to the panel of the container if it was opened by clicking it in the city. An array of single objects added by a command can set `arrayProperties.hint` to point to the Create entry and `arrayProperties.hideAddButton` to remove the Add button (see [Parameters and UI](Parameters-and-UI)). Deleting an array object clicked in the city closes its panels, while one opened from the panels returns to them.

Owned geometry, guides, handles and colliders are hidden while another container is selected or the category is deselected. Placement captures its target when started and is cancelled when that target is deselected or deleted. Closing a container from the containers menu also closes the open panels of what it owns (its elements and array objects, the lists of its contents, and the same for its subcontainers); the panels of other containers stay open. Radius erasing stays within the captured ownership scope.

Container identities are stored as `_elementContainerId`, with `_propertyContainerId` in the owned custom-element state or mesh-instance settings. These identifiers survive save/load, reordering and undo/redo, and are excluded from property presets. Cloning a container copies its contents with new identities and independent points, retaining sharing within the copy. Deleting it deletes its contents in the same undo step. The export JSON retains the identifiers; the core decides how to export each scope.

### Nested containers

A nested custom city properties type can set `elementContainer.parentCategory` to the category of its nearest enclosing container. It uses its own `category` and matching `exclusiveEditing.category`, so the parent and one of its children can be selected together. The containers menu counts both selections and offers separate Edit and Deselect entries. Opening a child selects the parents on the path to it. Switching or deselecting the parent clears its child selection and cancels placement into it; deselecting only the child keeps the parent selected.

For variants already stored as an int or enum on the parent's elements, a child can inherit a filtered part of the parent's contents:

```json
"exclusiveEditing": {"enabled": true, "category": "scenarioVariant"},
"elementContainer": {
    "category": "scenarioVariant",
    "parentCategory": "scenario",
    "filterParameter": "variant",
    "filterValue": 1,
    "types": ["route"],
    "commands": [{"label": "ADD_MARKER", "path": "markers"}]
}
```

`filterParameter` and `filterValue` select the accepted types with that value (0 if missing). Those elements keep the parent's `_propertyContainerId`, so existing saves and exporters retain their format. Their editor scope is the matching child: its list, visibility, picking, Back navigation and placement cancellation follow the child selection. Creation through the child's category sets the filter value automatically, including in the draft, and applying a preset preserves it. Omit that parameter from the element's layout when membership is chosen by selecting the child. Parent types not accepted by any filtered child keep the parent's scope.

Commands use paths relative to the child state. Fixed `customCityProperties` containers are initialized even before they have contents. Existing int/enum membership and existing nested array data need no migration; cloning the parent copies each owned element once and gives the child containers new identities.

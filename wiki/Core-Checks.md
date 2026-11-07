# Core Checks

Some problems of a city only show up in the game, and are found only by doing what the game does: whether the opponents of a race reach the finish, for example. A **check** is a Python script of the core that finds such problems for some elements, when the user asks, without changing anything. Its results are listed in the same panel as a [validation](Validation), and it can draw lines over the city (e.g. the route the game would take), shown while the results are listed.

The difference from the other scripts of a core: a [converter](Core-Custom-Elements#converters) changes the city, a [generator](Core-Generators) runs by itself and makes files, a validation exports the whole city. A check reads the whole city but looks only at the elements it was run on, and is usually much faster than a validation (it can stop before writing anything). The exporter can run the same code during an export, so that a validation reports the same problems.

```
<CoreName>/
└── checks/<name>/            one folder per check (optional folder)
    ├── check.json
    └── check.py
```

## `check.json`

```json
{
    "label": "CHECK_OPPONENTS",
    "tooltip": "CHECK_OPPONENTS_TOOLTIP",
    "sources": ["propertyContainer/checkpointRace", "customElement/opponent"],
    "script": "check.py"
}
```

| Key | Description |
|---|---|
| `label`, `tooltip` | String keys of the core: the text of the button and its tooltip, and the title of the results. |
| `sources` | What it checks: `road`, `intersection`, `terrainPatch`, `buildingLine`, `customElement`, `customElement/<type>`, or `propertyContainer/<type>`: an object of the city properties of that [custom city properties type](Core-City-Properties) that is a property container (it has an `elementContainer`, e.g. a race). |
| `types` | Only the procedural elements of these types (all if missing). |
| `script` | The Python script, in the folder of the check (default `check.py`). |
| `enabledWhen` | Optional `<module>.<function>` Lua predicate, as for the converters, enabling the button of an element. |

## The button

A `check:<name>` element in the layout of a type adds the button of the check, if the check accepts what the panel edits: in the `ui.json` of the elements and of the custom elements, and in the `uiInfo` of a custom city properties type (for its property containers). The button runs the check on that element or container only:

```json
{ "name": "check:validateOpponents", "width": 1.5 }
```

While it runs, the editor is locked, as for a converter. Then the panel lists the results: selecting one highlights its element and shows its message, *Zoom to it* goes to its position (or else its lines, or its element), *Run again* runs the check again on the same targets, and closing the panel removes the lines.

## The script

The program writes the export JSON of the whole city, as for an exporter (see [Input and Output](Input-and-Output#export-format-ioexportedcity-ioexportedcitycs-and-cityencoderjson_export)), and runs:

```
check.py <Files/python folder> <input JSON>
```

The JSON has two more keys:

| Key | Description |
|---|---|
| `check` | `{"name": <check>, "targets": [...]}`. The targets are `{"kind", "id"}`: the elements by their index in the lists of the JSON (`kind` as in `sources`: `road`, `customElement`, ...), the property containers as `{"kind": "propertyContainer", "id": <identity>, "type": <custom city properties type>}`, the identity being their `_elementContainerId` (the `_propertyContainerId` of what they own). |
| `report` | Where the results go (`{"file": ..., "validation": true}`), read by `ExportReport.init`. |

The results are the items of `Files/python/ExportReport.py`, as for the exporters (see [Warnings and errors](Core-Python-Integration#warnings-and-errors)): `error`, `warning` and `info` with the element (`element` name and `kind`), `position` and `overlay`, the lines to draw: `[{"type": "polyline", "points": [[x, y, z], ...], "color": "#rrggbb"}]` in the program coordinates. An `info` item can carry a summary (e.g. the whole route of a vehicle). A script that fails without reporting an error (an exit code other than 0) shows its output in an error message.

```python
import sys
sys.path.append(sys.argv[1])
import DecodeExportedJson
import ExportReport as report

data = DecodeExportedJson.DecodeExportedJson().decode_json(sys.argv[2])
report.init(data)
for target in data['check']['targets']:
    if target['kind'] == 'customElement':
        element = data['customElements'][target['id']]
        report.info('checked', element=element['data']['name'], kind='customElement',
                    overlay=[{'type': 'polyline', 'points': [[0, 0, 0], [0, 0, 50]], 'color': '#2e9e44'}])
```

## From scripts and agents

`checks.list` lists the checks of the core, and `elements.check(check, elements, containers)` runs one on elements (references) and property containers (identities, e.g. the ones in `state.get` of the city properties). The result is `{ok, errors, warnings, items, durationMs}` (see [Scripting API](Scripting-API#elements)).

## The Midtown Madness 2 core

`validateOpponents` (*Check opponent routes*, on the races and on the opponents) finds the opponents that won't reach the finish as the game drives them: the game doesn't search for a route between the waypoints, it follows a road only between two intersections joined by one, and drives straight otherwise. The details are in the races guide of the core and in its `docs/agents/races-and-state.md`. The MM2 exporter runs the same check, so a validation lists the same problems, as warnings.

## Internals

| Class | Purpose |
|---|---|
| `CheckCatalog` (`IO/CheckCatalog.cs`) | The checks of the core (`checks/*/check.json`), a kind of `CoreElementTool` like the converters and generators, accepting property containers too. |
| `Operations.Checks` | Runs a check (`Run`): the export JSON with `check` (`JSON_Export.check`), the script, and the report read back. `Reference` gives the reference of a target in the JSON. |
| `EditorPanel.AddCheckButton`, `ArrayElemEditorPanel` | The `check:<name>` buttons, of the elements and of the property containers. |
| `CityBuilderMenuBar.StartCheck` | Runs a check from a button and shows the results. |
| `EditorPanels.ValidationEditorPanel` | Lists the results and draws their overlays (`GameEngine.LineRenderer`, a solid line and a faint one drawn through what covers it). |

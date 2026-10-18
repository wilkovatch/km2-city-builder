# Python Scripts

Python scripts can edit the city through the [Scripting API](Scripting-API). They are listed in the **Scripts** menu and come in two kinds:

- **Scripts without inputs**: a single `.py` file, run as soon as it is chosen from the menu.
- **Scripts with inputs**: a folder with a `script.json` that declares input parameters. Choosing it from the menu opens an input panel, built like the panels of the city elements, and **Run** starts it with the values entered. The values are remembered (per city) for the next run.

While a script runs, the progress bar shows its progress with a **Cancel** button, and the editor is locked: the camera can still move, but the menus (except Help), the clicks on the city and the shortcuts don't work. By default the whole run is a single undo step. If the script fails or is cancelled, its changes are undone and its error is shown.

The scripts use the Python interpreter of the program (see [Settings, Paths and Python](Settings-Paths-and-Python#pythonmanager)). The `citybuilder` module needs nothing beyond the standard library.

## Where the scripts are

The menu lists, in this order and separated by dividers:

| Folder | Scripts |
|---|---|
| `Files/scripts` | The scripts of the program (none yet; Scripts > Open the scripts folder opens it). |
| `Files/cores/<core>/scripts` | The scripts of the core of the city (see [Core Structure](Core-Structure)). |
| The folders of Settings > Python > Script folders | The user's scripts (the `scriptFolders` setting). |

In each folder, every `.py` file is a script without inputs, and every subfolder containing a `script.json` is a script with inputs (or without, if it declares no parameters). Files and folders whose names start with `_` or `.` are skipped, so helper modules can sit next to the scripts. The working directory of a script is its folder.

## A script without inputs

```python
import citybuilder as cb
from citybuilder import api

roads = api.elements.list(kind="road")
cb.alert("%d roads" % len(roads), "Roads")
```

[`citySummary.py`](https://github.com/wilkovatch/km2-city-builder/blob/main/wiki/examples/scripts/citySummary.py) is a complete example (see [Examples](#examples)).

## A script with inputs

```
myScript/
├── script.json
├── main.py
└── strings/en-US.json     optional
```

```json
{
    "label": "MY_SCRIPT",
    "description": "MY_SCRIPT_DESC",
    "entry": "main.py",
    "transaction": true,
    "parameters": [
        { "name": "count", "type": "int", "label": "MY_SCRIPT_COUNT", "defaultValue": 10 },
        { "name": "linePreset", "type": "preset", "presetCategory": "buildingLine", "label": "BUILDING_LINE_PRESET" }
    ],
    "ui": {
        "menuWidth": 1.5,
        "tabs": [ { "label": "", "elements": [ { "name": "count", "width": 0.75 }, { "name": "linePreset", "width": 0.75 } ] } ]
    }
}
```

| Field | Description |
|---|---|
| `label`, `description` | String keys (or plain text) of the menu entry and of the top of the input panel. Without a label, the folder name is used. |
| `entry` | The Python file to run (default `main.py`). |
| `transaction` | `true` (default): the run is a single undo step, undone if the script fails or is cancelled. `false`: every command is an undo step of its own, unless the script makes transactions itself (see below). |
| `parameters` | The inputs, in the format of the parameters of the core types (see [Parameters and UI](Parameters-and-UI#parametersjson)), with the types and fields below. A script without parameters runs directly from the menu. |
| `ui` | The layout of the panel, in the format of `ui.json` (see [Parameters and UI](Parameters-and-UI#uijson)): tabs and elements with their widths. As in the element panels, the elements of a row are placed from right to left. Without it, every parameter gets a row of its own in a single tab. |

### Input types

| Type | Field | Value given to the script |
|---|---|---|
| `bool`, `int`, `float`, `string` | Checkbox, input field | the value |
| `enum` | Dropdown of `enumLabels` | the index of the option |
| `texture`, `mesh` | Texture or mesh field with its selector | the relative path |
| `preset` | Dropdown of the presets of `presetCategory` (`road`, `intersection`, `buildingLine`, `building`, `buildingSide`, ...) | the preset name |
| `file`, `folder` | Input field with a button opening the file dialog (`extensions`: the file extensions shown, e.g. `[".png"]`) | the path |
| `array` | Editable list, each element edited in its own panel (`arrayProperties`, as for the core types) | a list of objects |
| `customElement` | Button opening the panel of a custom type of the core | an object |

`defaultValue` is the starting value of a parameter (for an `array`, a list of objects). A `preset` without one starts on the default preset of its category. The labels, placeholders, tooltips and enum labels are looked up in `strings/<language>.json` of the script folder first, then in the strings of the core and of the program, and are shown as they are if no string has that key.

The examples [`cityTerrain`](https://github.com/wilkovatch/km2-city-builder/tree/main/wiki/examples/scripts/cityTerrain) and [`cityBuildings`](https://github.com/wilkovatch/km2-city-builder/tree/main/wiki/examples/scripts/cityBuildings) show complete `script.json` files (see [Examples](#examples)).

## The `citybuilder` module

`Files/python` is on the module path of the scripts, so `import citybuilder` works from any script folder.

| Function | Description |
|---|---|
| `call(method, params=None, **kwargs)` | Runs a command of the [Scripting API](Scripting-API#method-reference) and returns its result. On failure it raises `ApiError`, with `code` (see [Errors](Scripting-API#errors)), `message` and `details`. Use `params` for parameter names that are Python keywords. |
| `api` | The same as attributes: `api.roads.create(points=[...])` is `call("roads.create", points=[...])`. |
| `batch(commands, atomic=True)` | Several commands as a single request and undo step. `commands` are `(method, params)` pairs or `{"method", "params"}` dictionaries. |
| `transaction(label)` | A `with` block whose commands are a single undo step, rolled back if the block raises. Only for scripts with `"transaction": false`. |
| `inputs()` | The values of the input panel, by parameter name. |
| `selection()` | The ids of the element being edited and of the element of the selected control point when the script was started. |
| `progress(value, text=None)` | The progress shown by the program, from 0 to 1. |
| `log(text, level="info")` | A line in the log of the program (`info`, `warning` or `error`). |
| `alert(text, title=None)` | A message shown when the script ends. |
| `on_progress` | A function called with `(value, text)` for the progress events of the long commands (loading, generation, export...). |

`citybuilder.geometry` has the vector and polygon helpers of the editor (`distance`, `normalize`, `is_point_inside_polygon`, `outer_polygon`, ...), for points given as `[x, y, z]` lists.

`print()` writes to the log of the program (prefixed with the script name) and to `pythonLog.txt`, because the standard output is used for the commands. The module replaces `sys.stdout` with the standard error when it is imported. The module can only be imported by a script run by the program: elsewhere it raises an error.

### Ending, errors and undo

- The script ends when its code ends (exit code 0). Any other exit code, or an uncaught exception, is a failure. The error shown is the last line of the standard error without the exception type (`sys.exit("MY_SCRIPT_ERROR")` or `raise Exception("MY_SCRIPT_ERROR")` shows `MY_SCRIPT_ERROR`), translated if it is a string key.
- With the transaction of the run, the changes are undone when the script fails or is cancelled. While it runs, the script can't undo or redo, open or close transactions, load, create, reload or close a city, rotate the city or import a heightmap.
- The session settings changed with `api.configure` (`history`, `autoFlush`) are restored when the script ends. Turning `autoFlush` off makes large scripts faster (see [Large scripts](Scripting-API#large-scripts)).
- Cancel stops the Python process. The commands it had already sent are completed, then its changes are undone.
- Only one script runs at a time.

## Examples

The folder [`wiki/examples/scripts`](https://github.com/wilkovatch/km2-city-builder/tree/main/wiki/examples/scripts) of the repository has example scripts. They are not part of the program: to try them, copy the folder (or any script in it) somewhere and add it in Settings > Python > Script folders.

| Script | Kind | What it does |
|---|---|---|
| [`citySummary.py`](https://github.com/wilkovatch/km2-city-builder/blob/main/wiki/examples/scripts/citySummary.py) | without inputs | Counts the elements of each kind and lists the elements that were selected when it was started (`selection()`, `alert()`). |
| [`cityTerrain`](https://github.com/wilkovatch/km2-city-builder/tree/main/wiki/examples/scripts/cityTerrain) | with inputs | The whole city terrain generation of the Create menu, written with the API commands only, with the inputs of its panel. Uses `citybuilder.geometry` for the ring of patches around the roads. |
| [`cityBuildings`](https://github.com/wilkovatch/km2-city-builder/tree/main/wiki/examples/scripts/cityBuildings) | with inputs | The whole city building generation of the Create menu. Its inputs show the `preset` type and a list of presets (an `array` whose elements have a single `preset` parameter). Its error when the list is empty is a string key of its `strings` folder. |

The two generators make the same elements as the built-in ones: the API self-test runs them and compares the results.

## Command line

```
Godot_v4.7.1-stable_mono_win64_console.exe --path . [--headless] -- --api-script=<script .py or folder> [--api-inputs=<JSON file>]
```

The program starts without a city, runs the script, then quits with the exit code of the script. The inputs are the default values of the parameters, overwritten by the values in the JSON file. The script loads or creates a city itself (`city.load`, `city.new`), so no transaction is opened for the run.

## Internals

`ScriptCatalog` (`Scripts/IO/ScriptCatalog.cs`) finds the scripts and reads their `script.json` into a `ScriptInfo`. It also gives the default input values, and saves them in the preferences of the city (`scriptInputs:<source>:<name>`). `ScriptInputEditorPanel` builds the input panel with `EditorPanel.AddParameters`, like the element panels. The `preset`, `file` and `folder` types are only shown in this panel and its child panels (`AllowsScriptParameterTypes`), and its strings come from the script (`GetString`).

`Api.ScriptRunner` runs a script. It closes the editors, locks the editor (`CityBuilderMenuBar.SetScriptRunning`), opens the transaction, and starts the process (`PythonManager.StartScriptProcess`, with `KM2CB_SCRIPT=1`). A thread reads the lines of the standard output and queues them to `CommandDispatcher` (lines not starting with `{` are only logged). Another thread writes the responses and events to the standard input. The standard error is logged. When the process has ended and the dispatcher is idle, the runner commits or rolls back the transaction, restores the API settings and unlocks the editor. The `script.*` methods (see [Scripting API](Scripting-API#script)) give the running script its inputs and selection, and show its progress and messages.

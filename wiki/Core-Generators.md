# Core Generators

Some elements need files made from them and from the elements around them, such as a model fitted to the roads it joins. A **generator** is a Python script of the core that makes these files while the element is edited, so that the editor can show them. The exporter of the core makes the real files itself when exporting, with the same code, in the export folder. The city folder is never written to, and the user never has to press a button to make the files again: the files follow the element.

The difference from a [converter](Core-Custom-Elements#converters): a converter runs once, when the user asks, and changes the city (in one undo step). A generator runs by itself whenever its input changes, and changes nothing in the city: its results are values of the program, not saved and not undone.

```
<CoreName>/
└── generators/<name>/        one folder per generator (optional folder)
    ├── generator.json
    └── generate.py
```

## `generator.json`

```json
{
    "sources": ["customElement/bridge"],
    "script": "generate.py",
    "enabledWhen": "bridges.generatesMesh"
}
```

| Key | Description |
|---|---|
| `sources` | The elements it runs on: `road`, `intersection`, `terrainPatch`, `buildingLine`, `customElement` or `customElement/<type>`. |
| `types` | Only the procedural elements of these types (all if missing). |
| `script` | The Python script, in the folder of the generator (default `generate.py`). |
| `enabledWhen` | Optional `<module>.<function>` Lua predicate, as for the converters: `ctx.element` and `ctx.neighbors` contain detached `{kind, id, state}` snapshots. Usually it checks the flag of the element that asks for the files (and what they need, e.g. a point on a road). Without it, the generator runs on every element it accepts. |

## When it runs

After every change of the city, the program builds the input of each element the generator is enabled for (as for a converter: the element with the elements around it, see below) and compares it with the one of the last run. The geometry the elements build is left out of the comparison, so the values given back don't count as a change. When the input has changed and then stayed the same for half a second (so not while a point is being dragged), the script runs in the background, once for all the elements due, and one run at a time per generator. Results for an input that changed in the meantime are dropped, and the script runs again.

The files go in a cache folder of the program (`<temp>/km2cb_generated/<hash of the city folder>/`), with the layout of the game folders (`geometry/`, `texture/`, ...). It is emptied when a city is loaded or closed, and the elements are generated again after loading. The program keeps the meshes and textures it loaded until the city is closed, by path. So name the files after their contents (a hash), so that a new result is a new file.

A mesh in the cache folder finds its textures as the other meshes do: beside it (the `texture` folder of the cache), then in the folder of the same name of the city and of the extra folders.

## The script

The script runs with the Python of the program and gets three arguments: the `Files/python` folder (to import `DecodeExportedJson`), the input file and the output file.

**Input**: the input of the converters (see [Converters](Core-Custom-Elements#converters)): the format of the export JSON with the ids of the [scripting API](Scripting-API), the elements with the elements around them, `cityFolder` and `searchFolders`. `generate` lists the elements (`{kind, id}`), and `outputFolder` is the cache folder to write in.

**Output**:

```json
{
    "results": [
        { "element": { "kind": "customElement", "id": 42 }, "values": { "mesh": "C:/.../geometry/bridge_6f1da990.pkg", "length": 40.0 } },
        { "element": { "kind": "customElement", "id": 43 }, "error": "MY_GENERATION_ERROR" }
    ]
}
```

| Key | Description |
|---|---|
| `element` | The element, as in `generate`. |
| `values` | Any JSON values, e.g. the absolute path of a file it wrote. |
| `error` | Why the element has no values: a string key, shown in the error log of the program once per change of the element. |

An element missing from `results`, or a script that fails, gets an error too.

## The values

The geometry scripts of the custom elements get the values as `ctx.generated` (see [Scripts](Core-Custom-Elements#scripts)): an empty table until the first result, then the `values` of the generators enabled for the element (merged), with `error` when one failed. An absolute path works wherever the program takes a mesh or texture path, e.g. as the `model` of the `props` of the geometry.

The scripting API gives them with `elements.get` and `derived: true`: `generated` has the `values` and `pending`, true while the values aren't the ones of the current input yet.

## Exporting

The exporter of the core makes the files again, in its own output: the export JSON has the same elements and `searchFolders` (see [Input and Output](Input-and-Output)). Sharing the code between the generator and the exporter keeps the export identical to the preview. The cache folder is only for the editor.

## Midtown Madness 2

The MM2 core generates the model of a movable bridge from the roads its controls are attached to (`generators/bridgeMesh`, with the **Generate model from the roads** flag of the bridge). Its exporter makes the same model, and the low LOD textures of the roads with **Generate low LOD texture**, in the export folder (see the core's `docs/exporter.md`).

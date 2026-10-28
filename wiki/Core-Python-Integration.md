# Core Python Integration

Python is used by cores for three things:

1. reading **custom mesh and texture formats** (the game's own files), through a local server;
2. **exporting** the city to the game's formats;
3. **migrating** cities made with older versions of the core.

The program runs its own Python interpreter (see [Settings, Paths and Python](Settings-Paths-and-Python#pythonmanager)), never the system one. A core declares the modules it needs in `pythonInfo/`:

| File | Content |
|---|---|
| `pythonInfo/imports.txt` | One module name per line. Before loading a city, the program runs `import <module>` for each line; if it fails, it offers to install the requirements. |
| `pythonInfo/requirements.txt` | A pip requirements file, installed with `python -m pip install -r`. |
| `pythonInfo/licenses.txt` | The licenses of these dependencies (for distribution). |

The base installation only provides `psutil`. Python output and errors go to `pythonLog.txt` in the program folder.

## Custom mesh and texture formats

List the extensions (without dot) in the core's `settings.json`:

```json
"customTextureFormats": ["tex"],
"customMeshFormats": ["pkg"]
```

Files with these extensions appear in the texture and mesh selectors, and are loaded by asking the Python server. The server (`Files/python/server.py`) is started when a city of the core is loaded (if the core has custom formats or mesh transforms); it adds the core's `server/` folder to the module path and imports these modules from it, each optional:

| Module | Function |
|---|---|
| `server/texture_formats.py` | `read_file(path)` |
| `server/mesh_formats.py` | `read_file(path)` |
| `server/mesh_transforms.py` | `read_transform(path)`, see [Mesh transforms](#mesh-transforms) |

The format modules must exist when their list is not empty. `path` is an absolute path. The function returns a `bytearray` whose **first 4 bytes are a placeholder**: the server overwrites them with the length of the rest. All values are little-endian.

### Texture response

| Type | Content |
|---|---|
| int32 | width |
| int32 | height |
| int32 | format: 3 = RGB (3 bytes per pixel), 4 = RGBA (4 bytes per pixel), 10 = DXT1, 12 = DXT5, 1003 = DXT3 |
| int32 | number of mip levels in the data (1 = base level only) |
| bytes | pixel data: the base level, followed by the mip levels if any |

Rows go **from the bottom of the image to the top** (UV v = 0 is the first row). If the data contains a partial mip chain, the program rebuilds the mipmaps from the base level.

Example (from the MM2 core, simplified):

```python
def read_file(path):
    width, height, pixels, has_alpha = decode_my_format(path)  # pixels: rows top to bottom
    res = bytearray(4)                       # placeholder for the length
    res += width.to_bytes(4, 'little')
    res += height.to_bytes(4, 'little')
    res += (4 if has_alpha else 3).to_bytes(4, 'little')
    res += (1).to_bytes(4, 'little')         # mip levels
    for y in range(height - 1, -1, -1):      # bottom row first
        res += pixels[y]
    return res
```

### Mesh response

Strings are encoded as a uint16 length followed by ASCII bytes; vectors as consecutive float32; quaternions as x, y, z, w. Positions and normals are in the program's left-handed coordinates (Y up), triangles are **clockwise** when seen from their visible side.

```
uint32      material count
material[]  (see below)
object      the root object (see below)
```

Material:

| Type | Content |
|---|---|
| string | name |
| uint32 | blend mode: 0 opaque, 1 cutout, 2 fade, 3 transparent |
| uint8 | 1 = apply the blend mode only if the texture has an alpha channel |
| string | shader name: `Standard` (variants such as `Standard (Specular setup)` map to it) or the name of another `.gdshader` in the program's `Materials` folder |
| uint32 + entries | colors: `string key`, `string keyword`, 4 × uint8 RGBA |
| uint32 + entries | floats: `string key`, `string keyword`, float32 |
| uint32 + entries | textures: `string key`, `string keyword`, `string folder`, `string file name` (without extension: the program tries every supported texture format) |

`key` is the name of a shader parameter (for the `Standard` shader: `_Color`, `_MainTex`, `_MainTex_ST`, `_Glossiness`, `_Opacity`); `keyword`, if not empty, enables the boolean uniform `keyword_<keyword>`.

Object:

| Type | Content |
|---|---|
| string | name |
| uint32 *L* | number of LOD levels |
| string × *L* | LOD level names |
| float32 × *L* | screen-relative height below which each level stops being used (use one level with 0 for no LODs) |
| uint32 + meshes | the meshes of the object (see below) |
| vec3 | local position |
| vec3 | local scale |
| quaternion | local rotation |
| uint32 + objects | child objects (same format, recursive) |

Mesh:

| Type | Content |
|---|---|
| string | name |
| uint32 + strings | names of the LOD levels in which this mesh is shown |
| uint32 *N* | vertex count |
| uint8 | 1 if vertex colors follow |
| vec3 × *N* | positions |
| vec3 × *N* | normals |
| RGBA8 × *N* | vertex colors (only if the flag is 1) |
| uint32 *C* | number of UV channels (the first two are used) |
| vec2 × *N* × *C* | UVs |
| uint32 | submesh count |
| per submesh | uint32 material index, uint32 index count, uint32 × count triangle indices |

If the file cannot be read, return an empty object rather than raising (the MM2 core returns `bytearray(4 + 58)`, an object with no meshes); an exception in the server breaks the connection and the program falls back to a placeholder cube.

### Mesh transforms

`read_transform(path)` gives the transform of a mesh from its placement point, used where the placement rules have `meshTransform` (see [Core Props and Objects](Core-Props-and-Objects#mesh-transforms)). `path` is the absolute path of the mesh file. It returns `None` if the core has no transform for the mesh (`autoYOffset` applies then), or a dict:

| Key | Content |
|---|---|
| `position` | `[x, y, z]`, in the program's left-handed coordinates (default `[0, 0, 0]`) |
| `rotation` | quaternion `[x, y, z, w]` (default `[0, 0, 0, 1]`) |
| `scale` | `[x, y, z]` (default `[1, 1, 1]`) |
| `warning` | optional message, shown once in the log (e.g. game files missing from the common folder); a dict with only a warning gives no transform |

The server encodes it (uint8 1 if there is a transform, 10 float32, then the warning as a uint16 length and ASCII bytes). An exception in the function becomes a warning, with no transform.

Example (the MM2 core: the props are drawn from their centre of gravity, the `CG` of `tune/banger/<pkg>.dgBangerData`):

```python
def read_transform(path):
    cg = read_cg(find_banger_data(path))
    if cg is None:
        return None
    return {'position': [-cg[0], cg[1], cg[2]]}  # MM2 mirrors x
```

## Exporters

Each folder in `exporters/` is an export format:

```
exporters/<name>/
├── settings.json
└── export.py
```

`settings.json`:

```json
{
    "label": "EXPORTER_PSDL",
    "format": "psdl",
    "flags": [
        { "id": "bin", "label": "EXPORT_BIN", "defaultValue": false, "disables": ["psdl", "inst"] },
        { "id": "psdl", "label": "EXPORT_PSDL", "defaultValue": true, "enables": ["psdl_split"] },
        { "id": "psdl_split", "label": "EXPORT_PSDL_SPLIT", "tooltip": "EXPORT_PSDL_SPLIT_TOOLTIP", "defaultValue": false }
    ]
}
```

| Key | Description |
|---|---|
| `label` | String key shown as the file type in the save dialog. |
| `format` | File extension. The core's formats are listed before the built-in JSON one, and choosing a file with this extension opens the exporter's dialog. |
| `flags` | Checkboxes of the export dialog: `id`, `label`, `tooltip` (string keys), `defaultValue`. When a flag is checked, the flags listed in its `enables` become editable and those in `disables` read-only; when unchecked, the opposite. |
| `validation` | Optional: the exporter can validate a city, with a dry export using these flags (`{"flags": {id: bool}}`, the others keep their default). See [Validation](Validation#for-core-authors). |

A flag can also have a `format` (a file extension) and a `formatLabel` (string key, the exporter's `label` if missing). The format is listed in the save dialog as another file type of the exporter, after its own format. The flag is set when the chosen file has that extension and unset otherwise: its checkbox shows the value but can't be changed, and `city.export` rejects it in `flags` (pass the format as `exporter` instead). Its `enables` and `disables` apply as usual. MM2 uses it to pack the export in an archive:

```json
{ "id": "packAr", "label": "EXPORT_PACK_AR", "format": "ar", "formatLabel": "EXPORTER_AR", "disables": ["bin"], "defaultValue": false }
```

When the user exports, the program:

1. writes the export JSON (see [Input and Output](Input-and-Output#export-format-ioexportedcity-ioexportedcitycs-and-cityencoderjson_export)) next to the chosen file, with its extension (the exporter's format or a flag's) replaced by `.json`;
2. runs `export.py` in its own console window, with these arguments:

| Argument | Value |
|---|---|
| `sys.argv[1]` | Path of the program's `Files/python` folder, to import `DecodeExportedJson` |
| `sys.argv[2]` | Path of the export JSON |
| `sys.argv[3]` | Path of the output file chosen by the user |
| `sys.argv[4...]` | One `1` or `0` per flag, in the order of `flags` |

3. deletes the JSON when the script ends (whatever its exit code);
4. reads the warnings and errors the script reported (see [Warnings and errors](#warnings-and-errors)) and lists them in the *Export report* panel, if any.

Because the script runs in its own console, it can print progress and should wait for a key press at the end (MM2: `input("Press any key to continue...")`), especially on errors, so that the user can read the output.

Reading the city:

```python
import sys
sys.path.append(sys.argv[1])
import DecodeExportedJson

data = DecodeExportedJson.DecodeExportedJson().decode_json(sys.argv[2])
for road in data['roads']:
    state = road['data']['fields']['state']
    vertices = road['data']['mesh']['vertices']      # list of (x, y, z)
    for submesh in road['data']['mesh']['submeshes']:
        texture = data['materialDict'][submesh['materialId']]['data']['texture']
        triangles = submesh['indices']               # flat list, 3 per triangle
```

`DecodeExportedJson` decodes the Base64 fields and removes their prefixes (`b64v3a_vertices` becomes `vertices`, ...). The structure is described in [Input and Output](Input-and-Output#export-format-ioexportedcity-ioexportedcitycs-and-cityencoderjson_export). Everything is in the program's coordinate system (left-handed, Y up); convert it as the game requires.

The core's own exporter code can be organized freely in the exporter folder (MM2 uses a git submodule for code shared with its Blender plugin).

### Warnings and errors

The exporter reports what the user should know with `Files/python/ExportReport.py` (on the path with `sys.argv[1]`), instead of printing it: the program reads the report back and shows it (after an export, and for a [validation](Validation)).

```python
import ExportReport as report

data = DecodeExportedJson.DecodeExportedJson().decode_json(sys.argv[2])
report.init(data)       # the report file is in the export JSON: "report": {"file": ..., "validation": bool}
report.warning("the road has more than 6 lanes on a side", element="Road 3", kind="road")
try:
    write_block(block)
except Exception:
    report.exception("block 14 not exported", element="Intersection 1", kind="intersection")
...
sys.exit(1 if report.has_errors() else 0)
```

| Function | Description |
|---|---|
| `init(data)` | Reads the report file from the export JSON (and empties it). Without it (the script run by hand) the messages are only printed. |
| `warning`, `error`, `info` `(message, element=None, kind=None, code=None, position=None, details=None)` | A line of the report (also printed, as `Warning: ...` and `Error: ...`). `element` and `kind` name the element as the scripting API does (`road`, `intersection`, `terrainPatch`, `buildingLine`, `meshInstance`), so that the user can select it; `position` is `[x, y, z]` in the program coordinates; `code` an identifier of the problem; `details` any text (e.g. a traceback). |
| `exception(message=None, **kwargs)` | The exception being handled, as an error with its traceback in `details`. |
| `is_validation()` | The export only validates the city: its files go to a temporary folder deleted afterwards. |
| `has_errors()`, `counts()` | What was reported so far. |

An export with errors fails, even with exit code 0: `city.export` fails with `export_failed`, with the items in `details.items`. To report every problem at once, an exporter can go on after an error (skipping the element that failed) and fail at the end. The MM2 exporter does so for each element of the JSON, each block of the PSDL and each file it writes.

A core whose exporter doesn't use `ExportReport` still works: the program takes the lines of its output starting with `Warning:` and `Error:`, and its last error message if it fails.

## Migrations

When the data format of a core changes in a way that breaks existing cities, increase `coreVersion` in the core's `settings.json` and add a migration script `migrations/<coreVersion>.py`. A city saved with an older core version is migrated when it is opened, after the user confirms (and is advised to make a backup).

The program runs `Files/python/migrate.py <city folder>/ <programVersion> <featureVersion> <confirm>`. It uses `MigrationHelper.get_migrations_todo` to find the scripts to run: every `migrations/<N>.py` with `city coreVersion < N <= core coreVersion`. One script must exist for each version in between, otherwise the migration fails. With `confirm = 0` it only prints how many migrations are needed; with `confirm = 1` it executes them one after the other with `exec`.

A migration script receives the city folder as `sys.argv[1]` and typically:

1. reads and backs up the files it changes with `MigrationHelper.read_and_backup(name, gzipped)` (the backup is `<name>.v<old coreVersion>.bak`);
2. returns early if the city is already at this version (`mh.get_pref('coreVersion', 0) >= N`);
3. modifies the data (plain Python dictionaries: `city.json.gz` is gzipped JSON, `preferences.json` and `presets.json` are plain JSON);
4. updates `coreVersion` (to `N`) and `coreFeatureVersion` in `preferences.json`;
5. saves with `MigrationHelper.save(data, name, gzipped)`.

```python
import sys, MigrationHelper

mh = MigrationHelper.MigrationHelper(sys.argv[1])
prefs = mh.read_and_backup('preferences.json', False)
if mh.get_pref('coreVersion', 0) < 3:
    city = mh.read_and_backup('city.json.gz', True)
    for road in city['roads']:
        state = road['state']
        if 'oldName' in state:
            state['newName'] = state.pop('oldName')
    mh.save(city, 'city.json.gz', True)
    prefs['coreVersion'] = 3
    prefs['coreFeatureVersion'] = 1
    mh.save(prefs, 'preferences.json', False)
```

To report an error, raise an exception whose message is a string key (`raise Exception("MY_CORE_MIGRATION_ERROR")`); the program shows the exception message (the text after `Exception:` on the last line of the traceback), looked up in the string tables. `MigrationHelper` itself raises `MIGRATION_ERROR_CORE_IS_FOR_NEWER_PROGRAM`, `MIGRATION_ERROR_CORE_IS_FOR_OLDER_PROGRAM`, `MIGRATION_ERROR_CITY_IS_FOR_NEWER_CORE`, `MIGRATION_ERROR_WRONG_MIGRATION_NAME` and `MIGRATION_ERROR_MISSING_MIGRATIONS`.

Things to keep in mind:

* The scripts run in numeric order of their file names (`2.py`, `3.py`, ..., `10.py`).
* Changes that only add features (new parameters with sensible defaults, new element types) do not need a migration: increase `coreFeatureVersion` instead. Cities record the feature version they were saved with, and refuse to open with an older core.

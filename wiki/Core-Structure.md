# Core Structure

A core is a folder in `Files/cores/`. The folder name is the core's identifier: it is stored in the `core` key of every city made with it, so it must not change once cities exist. Cores are usually git repositories, so that the program can install and update them.

This page lists every file and folder a core can contain. The [Midtown Madness 2 core](https://github.com/wilkovatch/km2cb-mm2-core) is the complete reference implementation.

## Layout

```
<CoreName>/
├── settings.json                core settings and versions (required)
├── strings/
│   └── en-US.json               core strings (required: at least CORE_NAME)
├── elements/                    element type definitions (required)
│   ├── roads/<type>/             (type.json: optional parent type, see Core Type Inheritance)
│   ├── intersections/<type>/
│   ├── sharedComponents/
│   ├── terrainPatches/
│   │   ├── terrainPatches/<type>/
│   │   └── borderMeshes/<type>/
│   ├── buildings/<type>/
│   ├── buildingBlocks/<type>/
│   ├── props/
│   │   ├── elements/<type>.json
│   │   ├── containers/<type>.json
│   │   └── parametric/<type>.json
│   ├── cityProperties/
│   ├── customCityPropertiesTypes/<type>.json
│   └── customElements/<type>/          optional: custom element types, also the mesh instance types (see Core Custom Elements)
├── templates/                   city templates (at least one)
│   ├── _common/                 copied into every new city
│   └── <TemplateName>/          one folder per template (presets.json, cityProperties.json...)
├── baseCommon/                  initial content of Files/common/<CoreName>/
├── server/                      Python modules for custom mesh/texture formats and mesh transforms
│   ├── mesh_formats.py
│   ├── mesh_transforms.py       optional
│   └── texture_formats.py
├── exporters/<exporter>/        one folder per export format
│   ├── settings.json
│   └── export.py
├── migrations/<N>.py            city migration scripts
├── lua/                         optional: Lua scripts of the definitions (see Core Lua Scripts)
├── converters/<name>/           optional: converters to custom elements (see Core Custom Elements)
├── scripts/                     optional: Python scripts of the Scripts menu (see Python Scripts)
└── pythonInfo/
    ├── imports.txt              modules checked before loading a city
    ├── requirements.txt         pip requirements installed on demand
    └── licenses.txt             licenses of the Python dependencies
```

The program reads every type folder with `Directory.GetDirectories`/`GetFiles`, so **all the folders under `elements` must exist** (except `customElement`), even when empty. A type is identified by its folder (or file) name; avoid underscores in road type names, because junction files are named `<roadTypeA>_<roadTypeB>.json`. A type can extend another type of its folder and give only what differs from it (see [Core Type Inheritance](Core-Type-Inheritance)).

## `settings.json`

```json
{
    "program": "km2CityBuilder",
    "version": "0.1.3",
    "coreVersion": 2,
    "coreFeatureVersion": 4,
    "programVersion": 2,
    "featureVersion": 1,
    "customTextureFormats": ["tex"],
    "customMeshFormats": ["pkg"]
}
```

| Key | Meaning |
|---|---|
| `version` | Human-readable version, shown in the core manager (with the git commit). |
| `coreVersion` | Increase it when a change in the core breaks existing cities; provide a migration script for each increase. |
| `coreFeatureVersion` | Increase it when the core gains features without breaking existing cities. |
| `programVersion`, `featureVersion` | The program version the core was made for (see below). |
| `customTextureFormats`, `customMeshFormats` | File extensions (without the dot) read by the core's Python server. Leave both empty (or omit them) if the core only uses the built-in formats; the server is then not started. |
| `surfaceProjectionTerrainFallbackOnly` | Optional boolean, default `false`. Only for the automatic projection of derived geometry (the projected sections of the custom element splines, `projectSections`) onto mixed surfaces: `true` searches the supported city geometry first and uses the heightmap (layer 3) only if that search misses. A city surface can be any distance below the heightmap. With `false`, the highest supported surface wins, including the heightmap. Heightmap-only queries are unchanged, and a mask excluding the heightmap never falls back to it. Mesh types can opt into surface projection with `meshInstance.projectionSurface` (see [Core Custom Elements](Core-Custom-Elements#mesh-instance-elements)). The points of these elements are placed with the same rule (the clicks and `projectPoints`), so they go where their sections go. It doesn't change the rest of what the user places: the clicks hit the visible surface, and the snaps of placed or moved points and objects use the placement rule (see `snapToSurface` in [Core Props and Objects](Core-Props-and-Objects#placement-rules)). |
| `viewToggles` | Optional visibility switches added at the end of the View menu (after a separator): a list of `{"name", "label", "default"}` (`label` is a string key, `default` whether it starts on, true if omitted). Each city saves their values in its `preferences.json` (`viewToggle.<name>`). The lines of the custom elements with that `toggle` are hidden while it's off (see [Core Custom Elements](Core-Custom-Elements)). |

### Versions

The program has a `programVersion` (increased on breaking changes) and a `featureVersion` (increased with new features), see `GlobalVariables` in [Engine Abstraction](Engine-Abstraction#globalvariables-scriptsglobalvariablescs). The migration helper refuses to work with a core whose `programVersion` differs from the program's, or whose `featureVersion` is higher than the program's.

Each city stores the `coreVersion` and `coreFeatureVersion` it was last saved with in its `preferences.json`. When a city is loaded:

* same `coreVersion`, city `coreFeatureVersion` ≤ core's: loaded directly (and the city's feature version is updated on the next save);
* city `coreFeatureVersion` > core's: error, the city needs a newer core;
* city `coreVersion` lower than the core's: the user is asked to run the migrations (see [Core Python Integration](Core-Python-Integration#migrations));
* city `coreVersion` higher than the core's: error, the city needs a newer core.

Cores using [type inheritance](Core-Type-Inheritance) need `featureVersion` 2 or higher. Cores with [custom elements](Core-Custom-Elements) or converters need `programVersion` 3 and `featureVersion` 2 or higher.

Cities with no `core` in their preferences predate cores; they are treated as Midtown Madness 2 cities and migrated.

## `strings/`

One JSON object per language (`en-US.json` is the default and fallback), mapping keys to text. Core strings are looked up before the program's base strings (`Files/strings/en-US.json`), so a core can also override base strings. Keys the program expects from the core:

| Key | Used for |
|---|---|
| `CORE_NAME` | Name of the core in the core manager and in the new city dialog. |
| `TEMPLATE_NAME_<folder>` | Display name of each template (the folder name is used if missing). |
| Every `label`, `placeholder`, `tooltip`, `enumLabels` entry and every `ui.json` tab label | Parameter fields and panels. |
| `<CONTAINER>_PRESET` | Label of a `PRESET_<container>` UI element (container name in upper case). |
| `EDIT_PROP_CONTAINER_<NAME>` | Button of each props container of a road type (name in upper case). |
| `PROP_ELEM_NAME_<PARAM>`, `PROP_CONTAINER_EDIT_PROPERTY_<PARAM>`, `PROP_ELEM_LIST_<PARAM>` | Fields of the props container types (parameter name in upper case). |
| Exporter `label`s and flag `label`/`tooltip`s | Export dialog. |
| Migration error keys | Messages raised by migration scripts. |

A missing key is shown as the key itself and logged, which makes missing strings easy to spot.

## `templates/`

A new city is created by copying `templates/_common/` and then the chosen template folder into the city folder, and writing an empty `city.json.gz` (with the initial city properties of the template's `cityProperties.json`, if any). Users can add their own templates in `Files/custom/templates/<CoreName>/<name>/`. See [Core Templates and Presets](Core-Templates-and-Presets).

## `baseCommon/`

When a city of the core is loaded for the first time on a computer, `Files/common/<CoreName>/` is created with a copy of `baseCommon`. It is the place for assets shared by all cities of that core (for example the MM2 core asks the user to copy the game's geometry and texture files into `geometry/` and `texture/`). Cities reach it through an extra folder starting with a dot (`.\<CoreName>\`) in their preferences, typically set by the template's `preferences.json`.

## `lua/`

Lua modules whose functions compute definitions (`"script": "<module>.<function>"` instead of `"value"`). They run inside the program and need no Python. See [Core Lua Scripts](Core-Lua-Scripts).

## `server/`, `exporters/`, `migrations/`, `pythonInfo/`

The Python side of the core. See [Core Python Integration](Core-Python-Integration).

`pythonInfo/imports.txt` lists one module name per line; before a city is loaded the program runs `import <module>` for each one and, if one fails, offers to install `pythonInfo/requirements.txt` with pip into the program's Python.

## Distributing and installing a core

* **Manually**: copy or clone the core into `Files/cores/<CoreName>/` (clone with `--recurse-submodules` if it uses submodules).
* **From the core manager** (Settings → Cores): the cores listed in `coreRepos.json` (`core` = folder name, `name`, `repository` = git URL) can be installed, updated and uninstalled from the program. Updates are fetched and merged from the tracked branch; a core with local changes is not updated.

The program only reads the core from `Files/cores`, so during development it is convenient to work directly in that folder, inside a git clone.

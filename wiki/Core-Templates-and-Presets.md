# Core Templates and Presets

A new city is created from a **template**: a folder whose content is copied into the new city's folder. Templates provide the city's initial `preferences.json`, its `presets.json` (the default values of every element), and possibly assets.

```
templates/
├── _common/                  copied into every new city, first
│   ├── preferences.json
│   └── ...                   shared assets (optional)
├── <Template1>/              copied after _common
│   └── presets.json
└── <Template2>/
    └── presets.json
```

When creating a city, the user picks the core and one of its templates (displayed with the string `TEMPLATE_NAME_<folder>`, or the folder name). Users can add their own templates in `Files/custom/templates/<CoreName>/<name>/`; the core's `_common` folder is copied for them too. After copying, the program writes an empty `city.json.gz` (with the terrain settings chosen by the user) and loads the city.

## `preferences.json`

The template must provide the city's preferences, at least:

```json
{
    "core": "MidtownMadness2",
    "coreVersion": 2,
    "coreFeatureVersion": 4,
    "extraFolders": [".\\MidtownMadness2\\"]
}
```

| Key | Description |
|---|---|
| `core` | The core folder name. **Required**: a city without it is treated as a pre-core city and sent through the migrations. |
| `coreVersion`, `coreFeatureVersion` | The core's current versions (from its `settings.json`). Update them in the template whenever they change, otherwise new cities will require a migration. |
| `extraFolders` | Asset folders searched by this city. A path starting with `.` is relative to `Files/common`, so `.\<CoreName>\` points to the core's common folder (see [Core Structure](Core-Structure#basecommon)). |

Other preferences (overlay texture, graphics options...) are set by the user later and do not need to be in the template.

## `presets.json`

```json
{
    "presets": {
        "road": [
            { "name": "Default", "type": "psdl", "segments": 10, "totalWidth": 15.0, ... },
            { "name": "1 lane road", "type": "psdl", ... }
        ],
        "intersection": [ { "name": "Default", "type": "psdl", "sidewalkSegments": 5, ... } ],
        ...
    }
}
```

Each category holds a list of element states (see `ObjectState` in [City Elements](City-Elements#objectstate-statesobjectstatecs)), each with a `name`. The **first preset of each category is the default**: it is used for new elements and cannot be deleted. Users add, overwrite and delete the other presets from the editor, and the changes are saved in the city's own `presets.json` (the template is not modified).

A preset contains the values of the core's parameters and of the program's built-in properties for that element, including `type` (the core type name). Parameters marked `instanceSpecific` are not part of presets.

### Categories

| Category | Used for | Notes |
|---|---|---|
| `road` | Roads | `type` must be a road type. Should set the built-in road properties (`segments`, `curveType`, `hermiteTension`, `startCrosswalkSize`, `endCrosswalkSize`, `project`, `makeCoplanar`, ...). |
| `intersection` | Intersections | `type` must be an intersection type; `sidewalkSegments`, `sizeIncrease`. New intersections copy the last intersection state used. |
| `<container>` | Container presets (`PRESET_<container>` UI element) | One category per container name, for example `rail` in MM2. The keys are the parameter names **without** the container prefix (`type`, `height`...). |
| `roadPropRule` | Road props rules | One state per props container name of the road type, plus `type` (a road type, to filter rules by road type). |
| `propContainer` | Props containers | `type` (a container type) and the slots of the container. |
| `propElem` | Props elements | `type` (an element type), `mesh` or `meshes`, and the element parameters. |
| `buildingLine` | Building lines | `type` must be a building type; `height`, `frontOnly`, `loop`, ... |
| `building` | Buildings | `type`, enabled sides, `height`, `depth`, and the side states (`frontState`, `leftState`, `rightState`, `backState`). |
| `buildingSide` | Building sides | `type`, floors and block dictionary (see [Core Buildings](Core-Buildings#side)). |
| `buildingBlock` | Blocks of the block dictionary | `type` (a block type) and its parameters. |
| `terrainPatchBorderMesh` | Terrain patch border meshes | `type` must be a border mesh type. |

Terrain patches, mesh instances and city properties have no presets.

If a category is missing, the program creates it with an empty default state, which has no `type`: creating an element of that kind then fails. **Every template must provide at least a default preset with a valid `type` for each category the core uses** (`road`, `intersection`, `buildingLine`, `building`, `buildingSide`, `buildingBlock`, `terrainPatchBorderMesh` and the container categories of its road types).

The easiest way to make the presets of a new core is to create a city with a minimal `presets.json`, create and save presets from the editor, and copy the city's `presets.json` back into the template.

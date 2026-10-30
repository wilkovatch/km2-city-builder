# Core Type Inheritance

A type can **extend** another type of the same category and give only what differs from it. The program merges the files of the type over the ones of its parent when it loads the core, so the result is an ordinary type: the editor, the generators and the exporters see the complete type, with all the files of the parent included.

Typical uses: two road types that differ only in some components or parameters, a building type with a different set of UI tabs, a props element type with another default label. Inheritance is available in cores whose `featureVersion` is at least 2 (see [Core Structure](Core-Structure#versions)).

## Declaring the parent

**Folder types** (roads, intersections, terrain patches, border meshes, buildings, building blocks) name their parent in a `type.json` file in the type folder:

```
roads/psdlTunnel/
├── type.json          {"extends": "psdl"}
├── settings.json      only the keys that differ
├── parameters.json
└── components/
    └── tunnelLights.json
```

**Single file types** (props elements, props containers, parametric objects, custom city properties types) use the same keys in their file:

```json
{
    "extends": "psdl",
    "label": "PROP_TYPE_NARROW"
}
```

| Key | Description |
|---|---|
| `extends` | Name (folder or file name, without extension) of another type in the same folder. The parent can extend another type in turn. |
| `abstract` | `true` hides the type: it can be extended, but it is not listed in the editor or in the scripting API and elements can't use it. Default `false`. |

A parent that doesn't exist, a name with a path in it, and a cycle (a type extending itself, directly or through other types) are errors, reported when the types are loaded.

`cityProperties` and `props/meshInstance.json` have a single type each, so they don't support inheritance.

## How the files are inherited

Each file of a type (`ui.json`, `parameters.json`, `settings.json`, the `line_`, `building_` and `side_` files of buildings, the junction files, the component files) is looked up in the type folder and in the folders of its ancestors:

* a file only the parent has is used as it is;
* a file found in several of them is merged: the farthest ancestor first, then each type closer to the child, the child last.

So a child folder with only a `type.json` is an exact copy of its parent.

**Components** are looked up in the `components` folder of the type, then in those of its ancestors, then in `sharedComponents` (for roads, intersections and border meshes). The component files found along the ancestry are merged like the other files. A shared component is used only when no type of the ancestry has its own file with that name, and it is never merged with an own file: as for a type without a parent, an own component replaces the shared one.

**Junctions** of an intersection type are all the junction files of the type and of its ancestors (a junction file of the child is merged over the parent's file with the same name).

## Merge rules

| Parent value | Child value | Result |
|---|---|---|
| object | object | The keys are merged, recursively; the child's value wins. |
| array of named objects | array of named objects | Merged element by element (see below). |
| anything | anything else | The child's value replaces the parent's (`null` included). |

An object with `"$replace": true` is not merged: it replaces the parent's value whole (the marker can also be the first key of a file).

### Arrays of named objects

An array is merged element by element when all its elements, in the parent and in the child, are objects with a `name` (parameters, components, definitions, UI elements, traffic types...), a `label` (UI tabs) or a `category` (late definitions). The other arrays (`sectionVertices`, `textures`, `anchors`, `propsLines`, `trafficLanes`, arrays of strings or numbers) are replaced whole, because their elements are referred to by position.

Elements with the same name also need the same `container` (parameters of a container, such as the `rail` textures of MM2) and the same `fromStart` (imported parameters of the junctions, a missing `fromStart` being `false`) to match.

Starting from the parent's elements, each element of the child, in order:

* if it matches an element of the parent, is merged over it, keeping its position;
* otherwise is added at the end;
* with `"$remove": true`, removes the matching element instead;
* with `"$after": "<name>"` or `"$before": "<name>"`, is placed (moved, if inherited) right after or before the named element. The name is the element's `name` (or `label` or `category`), prefixed by `<container>_` for a parameter of a container, as in the `rail_texture1` variable. A name that matches no element, or more than one, is an error.

If the parent's array has two elements that can't be told apart, it is replaced instead of merged, with a warning.

Keys starting with `$` are removed after merging.

Some arrays are referred to by position: the elements of `trafficTypes` by the `type` of the traffic lanes, and those of `textures` (replaced whole, as an array of strings) by `texturesMapping`. Only add elements at the end of `trafficTypes`, and when the child needs more textures give the whole `textures` array, with the parent's textures first.

## Example

A road type like `psdl`, without banking, and with lights on the ceiling of the curved gallery:

`roads/psdlTunnel/type.json`:

```json
{ "extends": "psdl" }
```

`roads/psdlTunnel/settings.json`:

```json
{
    "allowRoll": false,
    "components": [
        { "name": "tunnelLights", "condition": "rail_typeI = 4 and tunnelLights", "$after": "rail_curvedGallery" }
    ],
    "texturesMapping": {
        "tunnelLights": [7]
    }
}
```

`roads/psdlTunnel/parameters.json`:

```json
{
    "parameters": [
        { "name": "tunnelLights", "label": "RD_TUNNEL_LIGHTS", "type": "bool", "$after": "warp" }
    ]
}
```

`roads/psdlTunnel/ui.json`:

```json
{
    "label": "PSDL_TUNNEL",
    "tabs": [
        { "label": "RD_TAB_MAIN", "elements": [ { "name": "tunnelLights", "width": 0.5, "$after": "warp" } ] }
    ]
}
```

plus `roads/psdlTunnel/components/tunnelLights.json`. Everything else (the other components, parameters, definitions, tabs, traffic) is the one of `psdl`.

## Junctions

The junction of two road types is looked up for the types themselves, then for the nearest pair of their ancestors: with `psdlTunnel` extending `psdl`, a `psdlTunnel` road meeting a `psdl` road uses `psdl_psdl.json` unless `psdlTunnel_psdl.json` exists. Only when no pair is found the program falls back to the junction of either type (or one of its ancestors) with itself, or any junction, and logs a warning (see [Core Intersections](Core-Intersections#missing-junctions)). Abstract road types can have junctions too, used by all the types extending them.

## Exporters

The elements of the exported city have the name of their own type. The export JSON also has a `typeAncestry` list, with the ancestry of every type of the core, so an exporter can treat a type as any of its ancestors:

```json
"typeAncestry": [
    { "category": "roads", "name": "psdlTunnel", "ancestry": ["psdlTunnel", "psdl"] },
    { "category": "roads", "name": "psdl", "ancestry": ["psdl"] }
]
```

`category` is the folder of the type inside `elements` (`roads`, `intersections`, `terrainPatches/terrainPatches`, `terrainPatches/borderMeshes`, `buildings`, `buildingBlocks`, `props/elements`, `props/containers`, `props/parametric`, `customCityPropertiesTypes`, `customElements`). In Python:

```python
ancestries = {(t['category'], t['name']): t['ancestry'] for t in data.get('typeAncestry', [])}

def is_type(category, name, base):
    return base in ancestries.get((category, name), [name])

# instead of state['type'] == 'psdl'
if is_type('roads', state['type'], 'psdl'):
    ...
```

An exporter that compares type names directly treats the types extending a type as unknown types.

The scripting API gives the same list as the `ancestry` of each type in `types.list` (see [Scripting API](Scripting-API)).

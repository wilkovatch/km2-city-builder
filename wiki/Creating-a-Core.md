# Creating a Core

A core adds support for a game. It tells the program which elements exist (road types, intersection types, building and prop types...), which parameters they have, how their geometry is built, how the game's files are read, and how the city is exported. A core is made of JSON files and Python scripts; no change to the program is needed.

This page is a step-by-step guide. Each step links to the reference page with the details. Keep the [Midtown Madness 2 core](https://github.com/wilkovatch/km2cb-mm2-core) at hand: copying and adapting it is the fastest way to start.

## Before starting

* Decide what the target game needs: which kinds of roads, how intersections and sidewalks look, whether buildings are meshes or textured facades, which props exist, and what the exported files must contain.
* Understand the [Expression Language](Expression-Language): almost all the geometry of a core is written with it.
* Remember the coordinate convention: Y up, left-handed. Convert to the game's convention in the exporter and in the mesh readers.
* Enable `pythonDebug` in the settings while developing, to see the consoles of the Python processes, and keep an eye on `pythonLog.txt` and on the debug console (errors in expressions are reported there, with the expression text).

## Step 1: create the folder

Create `Files/cores/<CoreName>/` (the folder name is the core's permanent identifier) and, ideally, make it a git repository. Create the full set of `elements` folders, even if some stay empty for now, because the program lists them all:

```
<CoreName>/
├── settings.json
├── strings/en-US.json
├── elements/
│   ├── roads/  intersections/  sharedComponents/
│   ├── terrainPatches/terrainPatches/  terrainPatches/borderMeshes/
│   ├── buildings/  buildingBlocks/
│   ├── props/elements/  props/containers/  props/parametric/  props/meshInstance.json
│   ├── cityProperties/
│   └── customTypes/
├── templates/_common/preferences.json
├── templates/<Template>/presets.json
└── pythonInfo/imports.txt, requirements.txt, licenses.txt
```

See [Core Structure](Core-Structure).

## Step 2: `settings.json` and strings

Write `settings.json` with `coreVersion: 1`, `coreFeatureVersion: 1`, the program's current `programVersion` and `featureVersion` (see `GlobalVariables` in the program), and empty `customTextureFormats` and `customMeshFormats` for now.

Create `strings/en-US.json` with at least `CORE_NAME`. Add every string key you use in the following steps; missing keys show up as raw keys in the UI.

## Step 3: a first road type

Create `elements/roads/<type>/` with `settings.json`, `parameters.json`, `ui.json` and a component. Start with a single flat surface, as in the minimal example of [Core Roads](Core-Roads#example-a-minimal-road-type), then add sidewalks, curbs, dividers and caps as components.

Checklist for road types:

* `settings.json` contains `sectionVertices`, `anchors`, `components`, `textures`, `texturesMapping`, `propsContainers` (can be empty) and `getters` with at least `totalWidth`, `roadWidth`, `height`, `hasSidewalks` and `canBeSimplified`.
* `parameters.json` can contain `internalParametersSettings`, to hide built-in parameters such as `makeCoplanar`.
* `ui.json` contains `mainGroup` (and `intersectionGroup`, `propsGroup` if relevant).
* The type folder name has no underscores.
* The anchors run along the outer edges of the road; they are what terrain patches and building lines snap to.

## Step 4: intersection type and junctions

Create `elements/intersections/<type>/` with `parameters.json` (defining `uMult` and `vMult`), `settings.json` (`roadTexture`, `defaultCrosswalkTexture`), `ui.json` (with `mainGroup`) and one junction file per pair of road types, `junctions/<A>_<B>.json`. Make the junction anchors start and end exactly on the road anchors, so that the anchor chains are continuous around blocks. See [Core Intersections](Core-Intersections).

## Step 5: terrain patches

Create a terrain patch type (parameters with `uMult` and `vMult`, settings, `ui.json` with `mainGroup`) and, if the game needs walls or railings along patches, border mesh types (same format as road types). See [Core Terrain Patches](Core-Terrain-Patches).

## Step 6: buildings

Create a building type (`line_*`, `building_*`, `side_*` files, with `uMult`/`vMult` in each `*_parameters.json`) and the block types: at least `texture` (with a `texture` parameter named `assetName`) and `empty`, plus any parametric block. See [Core Buildings](Core-Buildings).

## Step 7: props, mesh instances, city properties

* `props/meshInstance.json`: the parameters and placement rules of hand-placed meshes.
* Props element and container types, if roads should get props automatically; add the container names to the road types' `propsContainers`, and the `EDIT_PROP_CONTAINER_<NAME>` strings.
* Parametric object types in `props/parametric/` for every `objectInstance` parameter.
* `cityProperties/` (`parameters.json`, `ui.json`, `settings.json` with `injectableParameters: []`) and any custom type.

See [Core Props and Objects](Core-Props-and-Objects) and [Core City Properties](Core-City-Properties).

## Step 8: template and presets

Create `templates/_common/preferences.json` (with `core`, `coreVersion`, `coreFeatureVersion` and the extra folders) and at least one template with a `presets.json` that has a default preset with a valid `type` for every element category. See [Core Templates and Presets](Core-Templates-and-Presets).

At this point the core can be tested: create a new city with it, draw roads and intersections, patches and buildings, and check the panels, the geometry and the anchors (*View → show all traffic lanes* helps to check lanes).

## Step 9: game files

If the game's meshes or textures use their own formats, write `server/mesh_formats.py` and `server/texture_formats.py` and list the extensions in `settings.json`. Put the Python dependencies in `pythonInfo`. If the game's assets must be shared by all cities, add a `baseCommon` folder and point the template's `extraFolders` to `.\<CoreName>\`. See [Core Python Integration](Core-Python-Integration#custom-mesh-and-texture-formats).

## Step 10: exporter

Create `exporters/<format>/settings.json` and `export.py`. The script receives the export JSON with the full generated geometry and all the element states, and writes the game's files. See [Core Python Integration](Core-Python-Integration#exporters) and the export format in [Input and Output](Input-and-Output#export-format-ioexportedcity-ioexportedcitycs-and-cityencoderjson_export).

Tip: save a test city as plain JSON (*File → Export* and choose the JSON format) to get the export JSON without running the exporter, and develop the exporter against that file.

## Step 11: publish and maintain

* Push the core to a git repository. Users can install it by cloning it into `Files/cores/<CoreName>/`; to make it installable from the program's core manager, it must be listed in the program's `coreRepos.json`.
* For every release, update `version`, and:
  * for new features that keep existing cities valid: increase `coreFeatureVersion`, and update the templates' `preferences.json`;
  * for breaking changes: increase `coreVersion`, reset `coreFeatureVersion`, write `migrations/<coreVersion>.py`, and update the templates.

See [Core Structure](Core-Structure#versions) and [Core Python Integration](Core-Python-Integration#migrations).

## Common problems

| Symptom | Likely cause |
|---|---|
| The core does not appear or fails when a city is loaded, with `Invalid float parameter: <name>` in the log | An expression uses a variable that does not exist in that type (typo, missing definition, or a definition used before it is evaluated). |
| Error about a missing directory | One of the `elements` folders is missing. |
| A road's faces are invisible from above | The two vertices of the face are in the wrong order (see [Core Roads](Core-Roads#coordinate-conventions)). |
| Faces connect the wrong vertices | The number of `mainMesh` vertices of a component changes between sections. |
| Terrain patches do not follow roads around corners | The junction anchors do not start/end exactly on the road anchors. |
| A float variable is always 0 | Its name contains `pi`, `deg2rad` or `rad2deg` (see [Expression Language](Expression-Language#pitfalls)). |
| New elements cannot be created | The template's `presets.json` has no default preset (with a `type`) for that category. |
| Opening a new city asks for a migration | The template's `preferences.json` lacks `core`, or has outdated versions. |
| Labels appear as `SOME_KEY` | The string key is missing from `strings/en-US.json`. |

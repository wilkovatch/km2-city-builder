# Input and Output

The classes in `Scripts/IO` save and load cities, export them for the core exporters, and load the meshes and textures used by a city.

## Saving and loading a city

### File format: `IO.SavedCity` (`IO/SavedCity.cs`)

`city.json.gz` is a gzip-compressed JSON serialization of the `SavedCity` struct:

| Field | Content |
|---|---|
| `heightmapResolution`, `terrainSize`, `maxHeight` | Terrain settings. |
| `heightMap` | Terrain heights: raw bytes from `GameEngine.Terrain.GetHeightBytes`, Deflate-compressed, Base64-encoded (empty for a new city). |
| `cityProperties` | State of the city properties. |
| `meshes` | Mesh instances: `id`, `name`, `mesh` (relative path), `settings`, `position`, `scale`, `rotation`. |
| `roads` | `id`, `name`, `state`, `instanceState`, control `points`, `startIntersectionId`, `endIntersectionId` (-1 if none). |
| `intersections` | `id`, `name`, `roads` (road ids), `center`, `state`, `instanceState`. |
| `terrainPoints` | Every terrain point, with what it is anchored to: `linkType` (`None`, `Point`: an anchor of a road or intersection, `Line`: a point on a link between two terrain points), `elementType` + `elementId` + `anchorIndex` to find the anchor, `percent` for line links, and the `dividing` flag. |
| `terrainPatches` | `id`, `name`, `state`, perimeter and internal point ids, `borderMeshes` (state, instance state, segment point ids). |
| `buildingLines` | `id`, `name`, `state`, `instanceState`, line point ids, `buildings` (states) and `sides` (states). |

Vectors and quaternions use the serializable structs from `States.Utils` and are in program coordinates. Element states are `ObjectState`s, so they appear as plain JSON objects with the parameter names as keys.

### `CityEncoder.JSON` (`IO/Export/CityEncoder/Formats/JSON.cs`)

Writes a `SavedCity` (with gzip when used for the city save). Runs as an async method over a few frames so that the progress bar can be shown.

### `CityDecoder.JSON` (`IO/Import/CityDecoder/Formats/JSON.cs`)

Loads a city:

1. Erases the current city and checks the versions (`CheckVersion`): no core in the preferences, or a core version different from the installed one, means a migration is needed; a city saved with a newer core feature version or an unknown core is an error.
2. Runs the migration if the user accepts (`Files/python/migrate.py`, see [Core Python Integration](Core-Python-Integration#migrations)).
3. Checks the Python modules of the core.
4. Loads the preferences (strings, formats, Python server), reads the file and creates the terrain (from the saved heights or flat), then sets the city properties and recreates mesh instances, roads, intersections, terrain patches (with their terrain points, resolving the anchors they are attached to) and building lines. Presets are reloaded from the city folder on first use.
5. Stores the file in the `LastCity` setting.

### `CityExporter` and `CityImporter` (`IO/Export/CityExporter.cs`, `IO/Import/CityImporter.cs`)

* `CityImporter.LoadCity(manager, path)` sets the working directory to the folder of `city.json.gz`, clears the material cache and runs `CityDecoder.JSON`.
* `CityExporter.SaveCity(manager, path, gzipped)` dispatches by file extension: an extension matching a core exporter's `format` opens that exporter's dialog; `.json` writes the city save (gzipped) or the export JSON (plain); anything else shows an error.
* `CityExporter.SaveCityWithExporter(exporter, flags, manager, path)` writes the export JSON next to the output file and then runs the exporter's `export.py` asynchronously in its own console window, deleting the JSON when it finishes (see [Core Python Integration](Core-Python-Integration#exporters)).

### Export format: `IO.ExportedCity` (`IO/ExportedCity.cs`) and `CityEncoder.JSON_Export`

The export JSON is meant for core exporters. Unlike the save, it contains the **generated geometry** of every element, not just its parameters. All coordinates are in the program's left-handed system. Large numeric arrays are Base64-encoded; the prefix of each field name tells the format:

| Prefix | Content |
|---|---|
| `b64v3_` | One Vector3: 3 little-endian float32. |
| `b64v2_` | One Vector2: 2 float32. |
| `b64q_` | One quaternion: 4 float32 (x, y, z, w). |
| `b64ia_` | int32 array: a uint32 element count followed by the values. |
| `b64v3a_` | Vector3 array: a uint32 *float* count (3 × vectors) followed by the floats. |
| `b64v2a_` | Vector2 array: a uint32 float count (2 × vectors) followed by the floats. |
| `b64f_` | Facade instances: a dictionary of lists of lists of 4×4 matrices, each one encoded as a float array (count + 16 floats). |

`Files/python/DecodeExportedJson.py` decodes all of them recursively and strips the prefixes, so exporters normally see plain lists.

Top-level structure (`ExportedCity`):

| Field | Content |
|---|---|
| `materialDict` | Every material used, with `id` and `data.texture` (the texture path). |
| `meshDict` | Every mesh file used by props and mesh instances: `name` (path) and bounds. |
| `roads` | `data` (see below), `points`, intersection ids. |
| `intersections` | `data`, `center`, `roads` (ids), `roadsThrough` (road pieces generated across the intersection, same format as roads). |
| `terrainPatches` | `data`, perimeter and internal points, `borderMeshes` (`fields` + `segment`). |
| `buildingLines` | `data`, `linePoints`, `sides`, `roof`, `buildings` (each with `front`, `left`, `right`, `back` sides, `roof` and splines). Each side has its merged `data.mesh`, plus `meshDict`, `facades` and `paramMeshes` with the block instancing data. |
| `meshInstances` | `id`, `name`, `settings`, `reference` (`meshId` into `meshDict`, position, scale, rotation). |
| `cityProperties` | State of the city properties. |

`BaseData` (the `data` field of most elements):

| Field | Content |
|---|---|
| `name` | Node name. |
| `fields` | The public fields of the element's generator, converted with `States.Utils.GetFieldsAsState`: in particular `state`, `instanceState`, `runtimeState` where present, plus scalar fields. For roads it also contains `lanes`: the traffic lanes and props line bounds, each with `name` and `b64v3a_points`. |
| `mesh` | `MeshData`: vertices, normals, UVs and `submeshes` (`materialId` into `materialDict` and the triangle indices). |
| `collider` | Same format, when the element has a separate collider mesh. |
| `propLines` | Props placed by the road's prop rule, grouped by container name, each a `MeshReference`. |

## Meshes and textures

### `MeshImporter` (`IO/Import/MeshImporter.cs`)

`LoadMesh(name, parent)` resolves the path with `PathHelper.FindInFolders`, then:

* `.obj`: loaded with the bundled Dummiesman OBJ importer (`CustomPackages/OBJImport`).
* An extension listed in the core's `customMeshFormats`: requested from the Python server (`mesh|<path>`) and decoded by the nested `MeshResponseReader`, which builds materials, LOD groups, meshes and child objects (the response format is in [Core Python Integration](Core-Python-Integration#mesh-response)).
* A name ending with `@handle`, a missing file or a loading error: a fallback cube (handle material or transparent material).

`GetSupportedFormats` returns `obj` plus the core's custom formats (used by the mesh selector). The `MeshDecoder` interface and its empty `Formats` folder are the extension point for meshes decoded directly in C#.

### `TextureImporter` (`IO/Import/TextureImporter.cs`)

`LoadTexture(path)` dispatches by extension: `tga`, `dds` (DXT1, DXT3, DXT5; loaded flipped, top row at v = 0, like the textures of the cores), `bmp` (uncompressed or bitfields) through the C# decoders in `TextureDecoder/Formats` (`TGA`, `DDS`, `BMP`, implementing the `TextureDecoder` interface), `jpg`/`png` through Godot, and the core's `customTextureFormats` through the Python server (`texture|<path>`). `FindTexture(nameWithoutExtension)` tries every supported extension, in the order `tga, dds, bmp, jpg, jpeg, png`, then the custom formats; it is used when a mesh file references a texture without extension.

### `BufferedBinaryReader` (`IO/BufferedBinaryReader.cs`)

A fast little-endian binary reader over a stream with an internal buffer (MIT licensed code by Jackson Dunstan), used by the texture decoders.

### `MaterialManager` (`IO/MaterialManager.cs`)

Caches one material per texture path.

* `GetMaterial(name)` resolves the path, loads the texture and creates a `Standard` material with it; textures with an alpha channel get the cutout blend mode. A missing texture gives a magenta material named after the path, so it is still exported with its name.
* Built-in names: `_TRANSPARENT_` (a translucent material used for invisible terrain patches and fallback meshes) and `_HANDLE_`.
* `GetTextureName(state, entry)`: resolves an entry of a type's `textures` list: a built-in material name is used as is, anything else is the name of a state property containing the texture path.
* `GetHandleMaterial(color)`: unlit colored material for handles and lines.
* `GetAllMaterials` (used by the exporter), `ClearCache`, `SetBlendMode`.

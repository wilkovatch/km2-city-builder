# Engine Abstraction

The program logic does not call most Godot APIs directly: it goes through the static class `GameEngine` instead. Many files alias engine types at the top (for example `using GameObject = Godot.Node3D;`).

Throughout the code, `GO` is an alias for `GameEngine.GameObject` and `GS` for `GameEngine.Scriptable`.

## `GameEngine` (`Scripts/GameEngine.cs`)

A static partial class with one nested static class per engine area.

### Logging and environment

| Member | Description |
|---|---|
| `Print`, `LogWarning`, `LogError` | Write to the Godot output (`GD.Print`, `GD.PushWarning`, `GD.PushError`), which the in-game debug console also captures. In a command line session `Print` writes to the standard error instead, so that the standard output only has the output of the command (see [Command Line](Command-Line)). |
| `GetTime` | Seconds since startup. |
| `GetDataPath`, `IsEditor` | Project path, and whether the code runs inside the Godot editor. |

### Math types: `Math`, `Vector2`, `Vector3`, `Quaternion`, `Plane`, `Bounds`, `Rect`, `Matrix4x4`, `Color`, `Color32`

Thin wrappers on top of Godot types with the semantics the program logic expects. The differences from Godot's own behavior matter:

* `Math.Sign` returns 1 for zero (Godot returns 0). `Math.Lerp` is clamped, `LerpUnclamped` is not.
* `Vector2/3.Angle` is unsigned and in degrees, `SignedAngle` has a positive sign for zero; `Reflect`, `Project` and `Normalize` return a zero vector instead of NaN.
* `Plane.Create(a, b, c)` swaps two points, because Godot's three-point constructor yields the opposite normal. `Plane.Create(normal, point)` normalizes the normal.
* `Matrix4x4.TRS` builds rotation × scale, `AsArray` returns the 16 floats in the order expected by exporters.
* `Vector3.forward` is `(0, 0, 1)` and `back` is `(0, 0, -1)`, because the program logic works in its own left-handed coordinates.

#### Coordinate conversion

The program logic, the cores and the exported files use a left-handed coordinate system (see [Architecture Overview](Architecture-Overview#coordinate-convention)). Conversion happens only inside `GameEngine`:

* `Vector3.EncodePosition` / `DecodePosition` negate Z (Godot ⇄ program).
* `Vector3.EncodeRotation` / `DecodeRotation` negate the X and Y Euler angles; `Quaternion.Encode` / `Decode` negate the quaternion's X and Y.
* `GameObject.GetPosition/SetPosition/GetRotation/...` and `Mesh.GetVertices/GetNormals` always return or accept program coordinates.
* Mesh triangle winding is **not** changed: mirroring one axis keeps the clockwise front faces on screen, so the index order stays the same.

### `GameObject`

Node manipulation: names, parenting (`SetParent` preserves the global transform when both nodes are in the tree), positions/rotations/scales (global and local, all in program coordinates), `SetEnabled` (hides the node *and* disables processing and collisions), `IsNull` (true for null or freed nodes), `Destroy`, `Instantiate` (duplicate), `LookAt`, `CreateLineRenderer`, `CreateLODMesh`/`ConfigureLODMesh`, and `SetLayer`/`GetLayer`.

`SetLayer` maps a layer index to a Godot collision layer bit and render layer bit, and also applies the per-layer cull distance (see `Camera`).

### `Scriptable`

Component-style helpers: `Create<T>` (create a node of a script class and parent it), `Cast<T>` (casts the node to a script class), `FindRecursive<T>` (first descendant of a class), `FindMultipleRecursive<T>`, `FindMultiple<T>`, `FindInParent<T>`, `FindMeshRecursive`.

### `Mesh`

Everything related to meshes and colliders:

* `CreateMesh`/`CreateMeshObject` create a `MeshInstance3D` with an `ArrayMesh`; `AssignMaterials`/`GetMaterials` manage per-surface materials (one surface per submesh).
* `AssignData` uploads vertices, normals (computed with `CalculateNormals` when not provided), UVs, vertex colors and one index list per submesh. Empty submeshes are stored as a single degenerate triangle, because Godot cannot have empty surfaces and removing them would shift the following materials; `GetIndices` skips them when reading back.
* `GetVertices`, `GetNormals`, `GetUVs`, `GetIndices`, `GetSubmeshCount` read mesh data back (in program coordinates), used by the exporter.
* `CreateCollider`/`RefreshCollider` create and update a concave collision shape (double-sided, so that rays hit generated meshes from above), `CreateBoxCollider`, `CreateCapsuleCollider`, `AdjustCapsuleCollider`.
* `CombineMeshes`, `GetEncapsulatingBoundsForChildMeshes`, `SetShadowsTwoSidedRecursive`.

### `Raycast`

`DoRaycast` (ray against a collision mask, returning a `RaycastHit` with point, body and shape), `ProjectPoint` (drops a point vertically onto layer 3, the terrain, by default) and `GetRayFromScreen`.

### `LineRenderer`

A `MeshInstance3D` subclass that provides a line renderer (`positionCount`, `SetPositions`, `startWidth`/`endWidth`, `material`, `enabled`), used for road center lines, traffic lanes, building outlines and similar overlays. Positions are given in program coordinates; the line is drawn as a flat horizontal ribbon of the requested width (an `ImmediateMesh` triangle strip), rebuilt when its positions or widths change.

### `Texture`

Texture creation from raw data: `CreateFromData`, `CreateFromPixelData` (generates mipmaps), `CreateFromRawData` (base level followed by mip levels, used for DDS and for textures returned by Python), `CreateFromPtrData` (the Python texture response, see [Core Python Integration](Core-Python-Integration#texture-response)), `CreateFromFile` (PNG/JPG), `HasAlphaChannel`.

The `Format` enum uses fixed numeric values (`RGB8 = 3`, `RGBA8 = 4`, `DXT1 = 10`, `DXT5 = 12`), plus `DXT3 = 1003`, so that the values written by core Python code keep working.

### `Materials`

`GetShader(name)` maps a shader name to a `.gdshader` in `Materials/` (a name like `"Standard (Specular setup)"` maps to `Standard`; unknown names fall back to `Standard`). `CreateMaterial`, `SetShaderParameter` (float, vectors, color, texture), `SetColor`, `SetMainTexture`, `SetTextureScale`, `EnableKeyword` (sets a `keyword_<name>` boolean uniform) and `SetBlendMode`.

Godot fixes the transparency mode of a shader at compile time, so each rendering mode is a separate shader: 0 = `Standard` (opaque), 1 = `StandardCutout`, 2 = `StandardFade`, 3 = `StandardTransparent`. `SetBlendMode` switches the shader and carries over the parameters already set. The common uniforms are in `Materials/StandardCommon.gdshaderinc`: `_MainTex`, `_MainTex_ST`, `_Color`, `_Opacity`, `_Glossiness`.

### `Terrain`

The heightmap terrain is a [Terrain3D](https://github.com/TokisanGames/Terrain3D) node. `CreateTerrainGameObject` builds it from a height array (rows along the program's Z axis, flipped for Godot), `SetMaterial`/`RefreshMaterial` apply the grid or overlay material (copying the parameters, since Terrain3D renders with its own shader), and further methods read and write heights for the terrain tools and the city save.

### Other nested classes

| Class | Description |
|---|---|
| `Camera` | Gets the active camera and implements per-layer cull distances, applied as the visibility range of the geometry of each layer. |
| `Lighting` | Ambient light color, sun on/off and sun shadows. |
| `Gizmo` | Access to the transform gizmo (`RuntimeGizmos.TransformGizmo`) through `CityBuilderMenuBar.staticGizmo`. |
| `Input` | Key and mouse bindings (`KeyCommand` enum, `keyMapping`, `mouseMapping`), registered in the Godot `InputMap` by `ReloadKeys`; `GetInputStart` and `GetInputPressed` check a command (with its modifier key). |
| `Resources` | Loads a material (`.tres`) or instantiates a scene (`.tscn`) by path. |
| `Version` | Program version (assembly version plus the git commit hash embedded at build time) and Godot version, shown in the About dialog. |
| `CombineInstance` | Mesh + transform pair for `Mesh.CombineMeshes`. |

## `LODGroup` (`Scripts/LODGroup.cs`)

Level of detail group, used for meshes loaded from files that contain several levels of detail. Godot's visibility ranges are measured per instance and ignore the camera FOV, and the visibility range end is already taken by the per-layer cull distances, so this node picks the LOD itself from the screen-relative height of the group's bounds (with an LOD bias) and hides the renderers of the other levels. Its configuration is kept in exported properties and node metadata, so that it survives `Node.Duplicate` (meshes are cloned from a pool by `MeshManager`).

## `PostprocessBuild` (`Scripts/PostprocessBuild.cs`)

An `EditorExportPlugin`, registered by the `addons/km2_postprocess_build` editor plugin (`PostprocessBuildPlugin`). After a Godot export it copies the runtime data the program needs next to the executable: `coreRepos.json`, `defaultSettings.json` (as `settings.json`), the `pythonInstaller` folder and the `Files` folder (without `common`, `cores`, `custom` and `temp`, which hold user data and are kept when exporting over a used build). It also writes `License.txt`, made of the program license followed by every notice in `Licenses/` and Godot's own license and third-party notices, and `Quickstart guide.pdf`, converted from `wiki/Quickstart-Guide.md` by running pandoc with the Typst PDF engine (both must be in the `PATH`; if they are missing the export only warns). In the PDF the page heading is replaced by the full guide title, and the date shown is that of the last commit that changed the page. The PDF layout (A4, Calibri 11pt, title and heading sizes) is defined by the pandoc template `addons/km2_postprocess_build/quickstart-guide.typst`. The screenshots of the guide are in `wiki/images` (which has a `.gdignore`, so Godot does not import them): the pandoc filter `addons/km2_postprocess_build/quickstart-guide.lua` prints the full window ones (JPG) at the text width and the cropped panels (PNG) all at the same scale. They can be taken again after UI changes by running the program with `-- screenshots <copy of a city>` (see [Command Line](Command-Line)) (the `GuideScreenshots` node, in `Scripts/UI/GuideScreenshots.cs`), which loads the city, opens the editor panels and overwrites the images. For `.zip` exports the data is prepared in a temporary folder and added to the archive.

## `GlobalVariables` (`Scripts/GlobalVariables.cs`)

`programVersion` and `featureVersion` of the program. Cores declare which program version they were made for, and the city loader refuses incompatible combinations (see [Core Structure](Core-Structure#versions)):

* Increase `programVersion` only for breaking changes, and reset `featureVersion` to 1 when doing so.
* Increase `featureVersion` for new features without breaking changes.

## `RandomManager` (`Scripts/RandomManager.cs`)

Random number sources: a shared non-deterministic `rnd`, `DetRandom(seed)`, and `CalculatorDetRandom(seed)` that caches one generator per seed, used by the `rnd(a, b, seed)` expression function so that prop placement stays the same between rebuilds. The cache is cleared before props are placed on a road.

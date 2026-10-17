# Type System

The type system turns the JSON files in a core's `elements` folder into objects the generators can use. It has two levels:

1. **Data types** (`CityElements.Types`, files in `Scripts/CityElements/Types/*.cs`): plain classes that mirror the JSON structure and are filled by Newtonsoft.Json. They contain expressions as strings.
2. **Runtime types** (`CityElements.Types.Runtime`, files in `Scripts/CityElements/Types/Runtime`): built from the data types. They parse every expression once with the [Runtime Calculator](Runtime-Calculator) and own the variable layout used to evaluate them.

The JSON format itself is documented from the core author's point of view in the core reference pages ([Parameters and UI](Parameters-and-UI), [Core Roads](Core-Roads), ...). This page describes the classes.

## `TypeParser` (`Types/Parsers/TypeParser.cs`)

Static entry point. Every getter reads its folder of the current core (`Files/cores/<core>/elements/...`) the first time it is called, caches the result and returns the cache afterwards (pass `reload: true` to force a reload). The dictionary keys are the folder or file names, which are also the type names stored in the elements' state (`"type"` property).

| Getter | Source | Result |
|---|---|---|
| `GetRoadTypes` | `roads/<name>/` (+ `sharedComponents/`) | `Runtime.RoadType` |
| `GetIntersectionTypes` | `intersections/<name>/` (+ `sharedComponents/`) | `Runtime.IntersectionType` |
| `GetTerrainPatchTypes` | `terrainPatches/terrainPatches/<name>/` | `Runtime.TerrainPatchType` |
| `GetTerrainPatchBorderMeshTypes` | `terrainPatches/borderMeshes/<name>/` (+ `sharedComponents/`) | `Runtime.RoadType` (border meshes are road-like) |
| `GetBuildingTypes` | `buildings/<name>/` | `Runtime.Buildings.BuildingType` (with `GetBuildingLineTypes`, `GetBuildingBuildingTypes`, `GetBuildingSideTypes` returning its parts) |
| `GetBuildingBlockTypes` | `buildingBlocks/<name>/` | `Runtime.Buildings.BlockType` |
| `GetPropsElementTypes` | `props/elements/<name>.json` | `Runtime.PropsElementType` |
| `GetPropsContainersTypes` | `props/containers/<name>.json` | `PropsContainerType` (no expressions, no runtime type) |
| `GetMeshInstanceSettings` | `props/meshInstance.json` | `Runtime.MeshInstanceSettings` |
| `GetParametricMeshInstanceSettings` | `props/parametric/<name>.json` | `Runtime.MeshInstanceSettings` per object type |
| `GetCitySettings` | `cityProperties/` | `Runtime.CitySettings` under the key `"city"` |
| `GetCustomTypes` | `customTypes/<name>.json` | `CustomElement` |

## Data types

### Common (`CommonComponents.cs`)

| Class | Purpose |
|---|---|
| `Parameter` | One user-editable parameter: `name`, `label`, `placeholder`, `tooltip`, `type`, `container`, `enumLabels`, `instanceSpecific`, and the extra settings of complex types (`arrayProperties`, `customElementType`, `objectInstanceSettings`). `fullName()` is `container_name`, or `name` without a container. |
| `ArrayProperties` | Settings of an `array` parameter: `maxElements`, `elementLabel`, and either `elementProperties` or `customElementType`. |
| `CustomElement`, `CustomElementExclusiveEditing` | A reusable group of parameters (`customTypes/*.json`), optionally shown one at a time per category. |
| `ObjectInstanceSettings`, `ObjectInstanceDefaultModel`, `ObjectInstaceRoadChoice` | Settings of an `objectInstance` parameter: object type, default model, which definitions give its condition, position, rotation and scale, and which transform axes the user may change. |
| `Definition`, `LateDefinition` | A named, typed expression (`name`, `type`, `value`), and a named group of them. |
| `InternalParameterSettings` | Shows or hides a built-in parameter of the program (`name`, `enabled`). |
| `ParameterContainer` | The content of a `parameters.json`: `parameters`, `internalParametersSettings`, `loopVariables`, `staticDefinitions`, `dynamicDefinitions`, `lateDefinitions`. |
| `ComponentInfo` | A component reference with its `condition`. |
| `SubObjectWithLocalDefinitions` | Base class of components: `localDefinitions`. |
| `TabElement`, `Tab`, `Panel` | The content of a `ui.json`. |
| `ITypeWithUI` | Implemented by every type with a panel: `GetUI()`, `GetParameters()`. |

### Element specific

* **Roads and intersections** (`RoadComponents.cs`): `RoadType` (reads `ui.json`, `parameters.json`, `settings.json` and the component files, looking in `components/` first and then in the shared components folder), `RoadSettings`, `RoadComponent`, `RoadComponentMesh`, `PropLine`, `TrafficType`, `TrafficLane`; `IntersectionType` (reads every file in `junctions/` and the components they use), `IntersectionSettings`, `JunctionType`, `ImportedParameter`, `ImportedPropContainer`, `ImportedTrafficType`, `TextureDefinition`, `ExtraTerrainSpline`, `ExtraTerrainPiece`.
* **Terrain patches** (`TerrainPatchComponents.cs`): `TerrainPatchType`, `TerrainPatchSettings`.
* **Buildings** (`BuildingComponents.cs`): `BuildingType` (container of `BuildingLineType`, `BuildingBuildingType` and `BuildingSideType`, read from the `line_*`, `building_*` and `side_*` files of one folder), `BlockType`, `BlockSettings`, `BlockComponent`, `BuildingSideSettings`.
* **Props** (`PropsComponents.cs`): `PropsElementType`, `PropsElementPlacementRules`, `PropsContainerType`, `PropsContainerParameter`, `MeshInstanceSettings`, `MeshInstancePlacementRules`.
* **City** (`CitySettings.cs`): `CitySettings`, `Settings`, `ParameterInjection`.

### `CalculatorArray<T, R>` (`CalculatorArrays.cs`)

Parses an array of expressions that can be written in three forms (see [Expression Language](Expression-Language#expression-arrays)): a plain list, a list of conditional blocks (`{"if": ..., "append": [...]}`) or a loop (`{"for": "i; start; end; step", "value": ..., "if": ...}`). `GetValues(vc)` evaluates it to a `R[]` with the current variables. The concrete classes are `NumberArray` (also accepts JSON numbers), `Vector3Array` and `Vector2Array`. Used for mesh vertices, UVs, faces, face textures, section vertices, anchors and lane positions.

## Runtime types

### `RuntimeType<T>` (`Runtime/RuntimeType.cs`)

Base class of every runtime type. The constructor:

1. Registers a variable for each parameter: `float`, `int`, `bool` and `enum` parameters are float variables (booleans are 0/1), `vec2`/`vec3` parameters are vector variables. `objectInstance`, `array` and `customElement` parameters are collected in `objectInstanceParams`, `arrayParams` and `customElemParams` instead. Texture, mesh and string parameters are not variables.
2. Registers the `loopVariables` (float variables, used as loop indices in expression arrays).
3. Parses the static, dynamic and late definitions into `numberDefinitions`, `boolDefinitions`, `vector3Definitions`, `vector2Definitions` (each definition also becomes a variable with its name), keeping the late ones grouped by category in `lateDefinitionsDict`.
4. Parses the components' conditions (`subConditions`) and local definitions (`localNumberDefinitions`, ...).
5. Adds the built-in variables passed by the subclass, creates the `variableContainer` and resolves the variable indices of every expression (`SetBaseIndices`). An expression that uses an unknown variable fails here, when the core is loaded.

Subclasses fill the variables in this order, through the protected helpers:

| Helper | Fills |
|---|---|
| `FillBaseInitialVariables(vc, state, instanceState)` | Parameter values (from `instanceState` for `instanceSpecific` parameters, from `state` otherwise), then the static definitions. |
| `FillBaseIterationVariables(vc)` | The dynamic definitions (once per section, segment or loop iteration). |
| `FillBaseLateVariables(vc, category)` | The late definitions of one category. |
| `FillBaseSubVariables(vc, c)` | The local definitions of component `c`, if its condition is true. |

`InternalParameterVisible(name)` tells the UI whether a built-in parameter is enabled in `internalParametersSettings`.

### Subclasses

| Class | Built from | Notes |
|---|---|---|
| `RoadLikeType.RoadLikeType<T>` | | Abstract base of road and junction types: section vertex calculators (`segmentVerticesCalculators`), anchors, traffic types and lanes, props lines, one `ComponentMeshCalculatorContainer` (main/start/end `MeshCalculator`, anchors, lanes, props lines) per component. |
| `RoadType` | `Types.RoadType` | Adds the road built-in variables (see [Core Roads](Core-Roads#built-in-variables)), the low-poly width fix for the parameters listed in `widths`, the late definitions for parametric objects, and the *standard getters*: `GetStandardFloat/Bool/Vec3/Vec2/String(name)` evaluate the definition (or read the parameter) that the core maps to a standard name in `getters`. A missing getter logs one warning and returns the default value. Also used for terrain patch border meshes. |
| `JunctionType` | `Types.JunctionType` + parent `IntersectionType` | Merges the intersection's parameters and definitions with the junction's, imports variables from the two roads (`importedParameters`), evaluates the junction section vertices with their own small variable container, resolves `textureDefinitions` and the `extraTerrainSplines`. |
| `IntersectionType` | `Types.IntersectionType` | Holds one `JunctionType` per road type pair (junction file `A_B.json` → key `(A, B)`). `GetJunctionType(a, b)` falls back to `(a, a)`, `(b, b)` or any junction, with a warning, when the pair is missing. Compiles the extra terrain pieces. |
| `TerrainPatchType` | `Types.TerrainPatchType` | Parameters and definitions only. |
| `Buildings.BuildingType` | `Types.Buildings.BuildingType` | Three runtime types: `Line`, `Building`, `Side`. |
| `Buildings.BlockType` (via `MeshType.MeshType<T>`) | `Types.Buildings.BlockType` | Parametric building block meshes, with the `y0`, `y1`, `xMult`, `scale` variables. |
| `PropsElementType` | `Types.PropsElementType` | Prop placement rules and their variables. |
| `MeshInstanceSettings` | `Types.MeshInstanceSettings` | Layer, placer mask and automatic Y offset rules of mesh instances. |
| `CitySettings` | `Types.CitySettings.CitySettings` | City properties. |
| `ArrayProperties` | `Types.ArrayProperties` | Parameters of the elements of an `array`/`customElement` parameter. |

Each runtime type is shared by all the elements of that type. Road and block generators therefore work on a **clone** of `variableContainer` (`VariableContainer.GetClone()`, same layout, separate values). Some short, self-contained evaluations (junction states, mesh instance rules, terrain patches) fill the type's own container or a per-element clone right before using it; since everything runs on the main thread this is safe as long as the values are not kept between elements.

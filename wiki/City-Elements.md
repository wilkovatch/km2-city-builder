# City Elements

The classes in `Scripts/CityElements/Logic` (plus `ElementManager`, `PresetManager`, `MeshManager` and the state classes) represent the objects a city is made of. Each element keeps its editable data in `ObjectState` objects, knows which runtime type it uses, and asks a generator (see [Geometry Generation](Geometry-Generation)) to rebuild its mesh when its inputs change.

## State and presets

### `ObjectState` (`States/ObjectState.cs`)

A dictionary of named values (`properties`) used for every piece of editable data: element parameters, presets, array elements, prop rules, saved cities. It serializes to a plain JSON object (the properties are a `[JsonExtensionData]` dictionary), so a state is stored in files exactly as `{"name": value, ...}`.

* Typed accessors with defaults: `Int`, `Float`, `Bool`, `Str`, `Vector2`, `Vector3`, `State` (a nested state), `Array<T>`; and setters `SetInt`, `SetFloat`, ..., `SetState`, `SetArray`.
* After deserialization (`AdjustTypes`) JSON numbers become `int`/`float`, nested objects become `ObjectState`s and arrays become typed arrays.
* A **dirty flag**: every setter marks the state as changed (`HasChanged`, `FlagAsChanged`, `FlagAsUnchanged`). Elements use it to decide whether to rebuild.
* **Containers**: parameters with a `container` in the core are stored flat as `<container>_<name>`. `GetContainer(name)` extracts them into a new state and `SetContainer` writes them back (used by the `PRESET_<container>` UI element, for example the MM2 rail presets).
* `Name` is the `name` property, used for presets and object names.
* `CreateFromDefaultProperties(parameters)` creates the state of a new array element with default values.
* Parent/child links between nested states are kept to know which array element a selected object belongs to.

### `States.Utils` (`States/Utils.cs`)

Helpers: deep cloning of arrays, `DeJsonify` (converts Newtonsoft `JObject`/`JArray` values to dictionaries and arrays), `GetFieldsAsState` (copies the public fields of an object into a state, used by the exporter), and serializable vector and quaternion structs (`SerializableVector3`, `SerializableVector2`, `SerializableQuaternion`) used in saved cities.

### `PresetManager` (`PresetManager.cs`)

Reads and writes `presets.json` in the city folder (deserialized into a `PresetContainer`). Presets are grouped by category (`road`, `intersection`, `rail`, `propElem`, `propContainer`, `roadPropRule`, `buildingLine`, `building`, `buildingSide`, `buildingBlock`, `terrainPatchBorderMesh`, ...; a category is created on first use). The first preset of each category is the default one and cannot be deleted; the others are kept sorted by name. `GetPreset(type, index)` returns a clone flagged as changed. `lastIntersection` remembers the last intersection state so that new intersections copy it. See [Core Templates and Presets](Core-Templates-and-Presets).

### `MeshManager` (`MeshManager.cs`)

Caches every mesh file loaded by the city (props, mesh instances, parametric objects) in a hidden pool node, loading it through `MeshImporter` the first time. `GetMesh(name, parent)` returns a duplicate of the cached template. `GetBounds(name)` gives the bounds used to put props on the ground. `GetAllMeshes` is used by the exporter to build the mesh dictionary.

## `ElementManager` (`ElementManager.cs`)

The node that owns every element of the open city, in the lists `roads`, `intersections`, `patches`, `meshes` and `buildings`, and creates the container nodes they live in. Its main responsibilities:

* **Update loop** (`ProcessUpdate`, see [Architecture Overview](Architecture-Overview#the-update-loop)), run after all other nodes when `worldChanged` is set by `FlagAsChanged`.
* **Dummies**: hidden elements (`GetDummy<T>`, `GetDummyIntersection`) used when editing presets without a real element, plus the `CityProperties` object, which is a dummy that stays active.
* **City-wide operations**: `EraseCity`, `EraseCityTerrain`, `EraseCityBuildings`, `CloseCity`, and the generators `CreateCityTerrain` / `CreateCityBuildings` (see `CitywideTerrainGenerator` and `CitywideBuildingGenerator` in [Geometry Generation](Geometry-Generation)).
* Visibility helpers: `ShowIntersections`, `ShowAnchors`, `ShowAllTrafficLanes`, `GetObjectList`.

## Roads

### `Road` (`Logic/Road.cs`)

A road is a curve through a list of control points (`points`, handle nodes the user can move) and optionally connected to an `Intersection` at each end.

* **States**: `state` (the road type parameters and the built-in road settings, shared with presets), `instanceState` (parameters marked `instanceSpecific`, never copied by presets) and `runtimeState` (computed values: whether the road has intersections, their positions and road counts, start/end directions). Rail parameters are stored as a container (`SetRailState`).
* **Curve**: `UpdateLine` reads the control points (projecting them on the terrain if `project`/`projectAll` is set) and, if anything changed, `RebuildLine` samples the curve (`GeometryHelper.GetPointOnCurve` with the `curveType`: Bézier, Hermite or low-poly) into `curvePoints`, applies the optional low-pass filter (`lpf`) and flags the attached intersections for a rebuild.
* **Segments**: `GetSegments` returns the number of sections: the number of points for low-poly curves, 2 for straight two-point roads whose type allows it (`canBeSimplified` getter), the `segments` setting, or `segments` per 100 m.
* **Mesh**: `RebuildMesh` fills the runtime state, moves the road ends back by the intersection sizes, and passes everything to its `RoadGenerator`. Afterwards it updates the terrain anchors along the road edges (`anchorManager`).
* **Standard getters**: `GetStandardFloat/Bool/Vec3/Vec2/String` read the values the core maps in `getters` (total width, height, ...), used by intersections and other code that must understand roads of any type.
* **Runtime values**: `GetRuntimeFloat/Bool/Vec3/Vec2` (from `IObjectWithStateAndRuntimeType`) evaluate any definition of the road type, used for example to enable the buttons of conditional parametric objects.
* **Parametric objects**: `Road` is a `ParametricObjectContainer`. For `objectInstance` parameters with a *road choice* (for example the MM2 traffic lights, which can be placed on another road of the intersection), `GetObjectRoad` picks the road to evaluate on (by index or by best score among the roads of the intersection), copies the requested input values to it (`CopyInputVariables`) and evaluates the late definitions there.

### `Intersection` (`Logic/Intersection.cs`)

Not a node itself: it owns a `point` (the handle at the center) and a generator node (`geo`, an `IntersectionGenerator`). An `IntersectionComponent` node attached to the point (and to the road pieces through the intersection) leads back to the `Intersection` when the user clicks on it.

* `roads`: the connected roads; `sizes`: how far each road is pushed back from the center. `RecalculateSize` computes the sizes from the angles and total widths of the roads (so that their edges do not overlap), then adds the crosswalk size, the per-road intersection size increase and the intersection's own `sizeIncrease`.
* `GetRoadsSortedClockwise` gives the roads around the center in clockwise order (used for junctions and road choices).
* `RebuildRoads` keeps the end points of the connected roads on the center point when the intersection moves (projecting it on the terrain if one of the roads has `project` set).
* **Roads through the intersection**: the road instance flags `startIntersectionStart`/`endIntersectionStart` and `startIntersectionEnd`/`endIntersectionEnd` mark roads where a road continues across the intersection. For each "start" road, a road piece (a `RoadGenerator` child of the intersection, using that road's state) is generated across the intersection towards the "end" road.
* `RebuildMesh` regenerates the mesh when forced (road or state change) and updates the terrain anchors.
* `state` holds the intersection type parameters and the built-in `type`, `sidewalkSegments`, `sizeIncrease`; `instanceState` the instance-specific ones.

## Terrain

Terrain patches, building lines and the terrain anchors of roads and intersections share a small graph of points:

| Class | Role |
|---|---|
| `TerrainPoint` (`Logic/Helpers/TerrainPoint.cs`) | A point used by one or more terrain patches or building lines (`links`). It can be free, or attached to an `IAnchorable` (`anchor`), in which case it follows it. It is deleted when no element uses it any more. `dividing` marks building line points that split two buildings. |
| `TerrainAnchor` (`Logic/Helpers/TerrainAnchor.cs`) | A point generated along the edges of roads and intersections (from the `anchors` expressions of the core). Anchors are chained (`prev`, `next`), so that a patch can follow a road edge automatically between two clicked anchors (`GetPointListTo`). |
| `LineAnchor` (`Logic/Helpers/LineAnchor.cs`) | An anchor on the segment between two points, at a given percentage. |
| `IAnchorable` | Common interface of anchors (`GetPoint`, `IsMoveable`, `IsDeleted`). |
| `TerrainAnchorLineManager` (`Logic/Helpers/TerrainAnchorLineManager.cs`) | Creates, recycles and updates the anchor chains of a road or intersection after each rebuild, remembering which intersection each chain starts and ends at. |
| `PointLink` (`Logic/Helpers/PointLink.cs`) | A capsule collider between two consecutive points, so that the user can click on a segment of a patch outline. |
| `AddTerrainPointToSequence` (`Logic/Helpers/AddTerrainPointToSequence.cs`) | The shared logic used by patches and building lines to append a clicked point: reuse an existing point, attach to an anchor, or follow an anchor chain from the previous anchor. |
| `IGroundable` | Implemented by patches and building lines (`IsProjectedToGround`, `RemovePoint`). |

### `TerrainPatch` (`Logic/TerrainPatch.cs`)

A polygon of perimeter `TerrainPoint`s, with optional internal points (to add vertices inside, for example for hills), filled with a textured mesh by `TerrainPatchGenerator`.

* `state`: terrain patch type parameters plus the built-in `type`, `texture`, `smooth` and `projectToGround`.
* `AddPerimeterPoint`, `AddInternalPoint`, `RemovePerimeterPoint`, `AutoClose` (closes the loop by following anchors), `AutoGenerateInternalPoints(distance)` (Poisson disc sampling inside the polygon).
* **Border meshes** (`TerrainBorderMesh`, `Logic/Helpers/TerrainBorderMesh.cs`): road-like meshes generated along a chosen sequence of perimeter points (railings and walls in MM2), each with its own `state`, `instanceState` and border mesh type.
* `UpdatePatch` rebuilds the mesh when a point, the state or a border mesh changed.

## Buildings

| Class | Role |
|---|---|
| `BuildingLine` (`Logic/Buildings/BuildingLine.cs`) | A polyline of `TerrainPoint`s. The points flagged as dividing split it into consecutive `Building`s. Holds the line type parameters and the built-in `type`, `height`, `frontOnly`, `loop`, `invertDirection`, `projectToGround`, `roofTex`. Generates an optional roof over the closed line (`roof`). `AutoClose` follows the anchors of the block the line starts on to close its outline and can split it into buildings of random length (between a minimum and a maximum), assigning them states taken from a list of presets; it is used by the city-wide building generator. |
| `Building` (`Logic/Buildings/Building.cs`) | One building of a line, with up to four `BuildingSideGenerator`s (`front`, `left`, `right`, `back`) and a roof. Its state holds the building type parameters, the built-in `front`/`left`/`right`/`back`/`top` switches, `height`, `depth`, `fixAcuteAngles`, `topTexture`, and the states of its sides (`frontState`, ...). `PreUpdateMesh` computes the side splines from the line and the neighboring buildings; `UpdateMesh` rebuilds the sides and the roof. |
| `BuildingSideGenerator` | See [Geometry Generation](Geometry-Generation#buildings). |

## `MeshInstance` (`Logic/MeshInstance.cs`)

A mesh loaded from a file and placed freely in the city (a building, a prop, a parametric object).

* `meshPath` is the relative path of the mesh file; `settings` holds the mesh instance parameters of the core (`props/meshInstance.json`), for example the MM2 `prop` flag.
* The core's placement rules give the layer of the instance and whether its children are raised so that the bottom of the mesh sits on the placement point (`autoYOffset`).
* A box collider around the mesh bounds makes it selectable.
* **Parametric instances** (`CreateParametric`) are created by a `ParametricObjectContainer` for an `objectInstance` parameter: their position, rotation and scale are relative to a container node placed by the parent element, and the user's offsets are stored in the parameter's state (`localPosition`, `localRotation`, `localScale`, `meshPath`).
* `ReplaceMesh`/`SetNewMesh` swap the mesh, keeping the transform.

## `CityProperties` (`Logic/CityProperties.cs`)

The city-wide parameters defined by the core in `cityProperties/` (city name, traffic settings, races in MM2...). It is a `ParametricObjectContainer`, so `objectInstance`, `array` and `customElement` parameters work as for roads.

Parameters listed as injectable in the core settings are copied into the runtime state of every road as `_GLOBAL_<name>` whenever they change (`InjectParameter`, called by `ElementManager.PropagateCityProperties`), so that road types can use them (see [Core City Properties](Core-City-Properties)).

## Shared interfaces

| Interface | Members |
|---|---|
| `IObjectWithState` | `GetState()`; implemented by every editable element, used by the editor panels. |
| `IObjectWithStateAndRuntimeType` | Adds `GetRuntimeFloat/Bool/Vec3/Vec2(variable)`. |
| `GenericSelector` | A small node with a `type` string and a `parent` object, used to identify selectable sub-objects that are not elements themselves (a building or a building line roof) when the user clicks on them (`CityBuilderMenuBar.SelectObject`). |

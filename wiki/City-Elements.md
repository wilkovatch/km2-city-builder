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
* Creation helpers: `CreateRoad` (a new road with the default preset, numbered after the existing ones) and `CreateIntersection` (a new intersection with no roads at a position), used by the road placer and the road splitter.

## Roads

### `Road` (`Logic/Road.cs`)

A road is a curve through a list of control points (`points`, handle nodes the user can move) and optionally connected to an `Intersection` at each end.

* **States**: `state` (the road type parameters and the built-in road settings, shared with presets), `instanceState` (parameters marked `instanceSpecific`, never copied by presets) and `runtimeState` (computed values: whether the road has intersections, their positions and road counts, start/end directions). Rail parameters are stored as a container (`SetRailState`).
* **Curve**: `UpdateLine` reads the control points (projecting them on the terrain if `project`/`projectAll` is set) and, if anything changed, `RebuildLine` samples the curve (`GeometryHelper.GetPointOnCurve` with the `curveType`: Bézier, Hermite or low-poly) into `curvePoints`, applies the optional low-pass filter (`lpf`) and flags the attached intersections for a rebuild.
* **Segments**: `GetSegments` returns the number of sections: the number of points for low-poly curves, 2 for straight two-point roads whose type allows it (`canBeSimplified` getter), the `segments` setting, or `segments` per 100 m.
* **Mesh**: `RebuildMesh` fills the runtime state, moves the road ends back by the intersection sizes (not on a seamless intersection, see below), and passes everything to its `RoadGenerator`. Afterwards it updates the terrain anchors along the road edges (`anchorManager`).
* **Rotation of the sections**: each control point `Handle` has a `curveRotation` (yaw and roll in degrees, see [Core Roads](Core-Roads#rotated-sections)). `GetEffectiveRotations` gives the values used by the geometry: zero where the road type doesn't allow them (`allowYaw`, `allowRoll`), and the yaw of an end on an intersection limited to `GeometryHelper.MaxIntersectionYaw` (60 degrees), or none on a seamless intersection, where the two ends share the average of their rolls (`Intersection.GetSeamlessRoll`; a roll set on one end is given to the other one too); `GetEndYaw` and `GetEndRoll` give the rotation of an end to its intersection. `GetConnectionHeight` and `IsConnectionHeightAuto` read the height of an end on an intersection (instance properties `startConnectionHeight`, `endConnectionHeight`, `startConnectionHeightAuto`, `endConnectionHeightAuto`); `RebuildMesh` raises the end of the curve by the height the intersection computed. After a rebuild the handles are oriented in the frame of the curve at their point (`OrientHandles`), and a handle rotated by the gizmo gives its yaw and roll back to the point, without the pitch (`ReadRotatedHandles`). A point inserted in the middle of the road gets the rotation of the curve there, and `RoadSplitter` gives the parts the rotations of the original curve.
* **Standard getters**: `GetStandardFloat/Bool/Vec3/Vec2/String` read the values the core maps in `getters` (total width, height, ...), used by intersections and other code that must understand roads of any type.
* **Runtime values**: `GetRuntimeFloat/Bool/Vec3/Vec2` (from `IObjectWithStateAndRuntimeType`) evaluate any definition of the road type, used for example to enable the buttons of conditional parametric objects.
* **Split line**: `splitLine` (a `RoadSplitLine`) is shown along the curve while the free road ends are shown to place another road (`SetActive(false, true)`), so that the other road can be connected to the middle of this one (see below), and along the edited road in the split mode of the road editor, to split it.
* **Parametric objects**: `Road` is a `ParametricObjectContainer`: the hooks of its `objectInstance` parameters are definitions evaluated on its variables.
* **Script context**: `Road` is an `IScriptContextProvider`. Its `RoadScriptContext` (`ctx` in the [Lua scripts](Core-Lua-Scripts#context-ctx) of its definitions) gives read-only views of its intersections and of their roads, sorted clockwise (`IntersectionScriptView`, `RoadScriptView`). The road is the `owner` of the variable container of its generator, which is how a script definition finds its context.

### Connecting a road to the middle of another one

| Class | Role |
|---|---|
| `RoadSplitLine` (`Logic/Helpers/RoadSplitLine.cs`) | A cyan line along the curve of a road, with a collider around it on layer 14 (a tube at least as thick as the point link capsules, and as wide as the line, which gets wider with the camera distance). It leaves out the parts of the curve covered by the intersections at the road ends, plus a margin. `Resolve` gives the point of the line closest to a hit, as a curve segment and a position in it. The line is rebuilt after each rebuild of the road mesh (when the intersection sizes are known), or when it is shown. |
| `RoadSplitter` (`Logic/Helpers/RoadSplitter.cs`) | Splits a road at a point of its curve. The road keeps the first part and a new road with the same `state` and `instanceState` gets the second one. The end intersection is moved to the new road, and a new seamless intersection connects the two parts (not seamless when the split connects a road to the middle of the other one, `RoadDrawing`). The through road flags of the old end go to the new road, the new intersection has none. The anchors of the split road are recreated, so the terrain points anchored to it stay in place, unanchored. The control points of the parts follow the original curve exactly for Bézier curves (de Casteljau subdivision, `GeometryHelper.SplitBezier`) and low-poly curves. Since the road mesh starts only at the intersection size from its center, control points inside an intersection would fold the mesh. So for low-poly and Hermite curves the parts are finished at the next update (`ElementManager.FinishSplitParts`, `RoadSplitter.FinishPart`), when the intersection sizes are known: their control points inside the new intersection are removed. Hermite curves can't be split exactly (except the straight ones, with two points), so up to a few points of the original curve are added to each part outside the intersections, where they bring the visible part of the road closest to the original curve (horizontally, for roads projected on the terrain). A part of a road split by the same action (both ends of a road on the middle of the same road) is split again on the original curve, so the errors don't add up. |
| `RoadMerger` (`Logic/Helpers/RoadMerger.cs`) | The reverse of a split: merges the two roads of an intersection into one of them, deleting the other road and the intersection. The kept road keeps its `state` and `instanceState`, except the per-end properties of its end on the intersection (see [Core Roads](Core-Roads#per-end-parameters)), which come from the far end of the other road, and its `segments`, which become the sum of the two (so the curve is sampled as densely). If the roads go opposite ways, the other road is reversed: its control points and rotations are reversed and its rolls negated, but its left and right properties are not swapped. Hermite and low-poly roads get the control points of both roads in a row, the point on the intersection once (two straight roads in line become a straight road with two points). The merged road keeps the curve type of the kept road. When the other road has the same curve (for Hermite curves, the same tension and subdivision), its control points are used as they are. A Bézier curve is a single curve through all its control points: two Bézier roads that are the two parts of one curve get its control points back (the point where it was split is found from the ratio of the tangents at the joint, the curve is extended from the longer part, de Casteljau outside 0..1, and checked on both parts); otherwise a single Bézier curve with the degrees of both roads is fitted to samples of both curves (least squares with fixed ends, the samples parametrized by their distance along the curves, then by the closest points of the fitted curve, a few times; the rotations are fitted with the same weights). For a Hermite or low-poly road, the other road with another curve is sampled and followed by new control points: for Hermite the farthest sample is added until the curve is within 0.5 m of all of them, for low-poly the polyline is simplified to 0.25 m (Douglas-Peucker). The merge is refused only if the intersection doesn't have exactly two roads, or if the merged road would start and end on the same intersection (`GetMergeError`). The anchors of the kept road are recreated, so the terrain points anchored to the roads stay in place, unanchored. |

### `Intersection` (`Logic/Intersection.cs`)

Not a node itself: it owns a `point` (the handle at the center) and a generator node (`geo`, an `IntersectionGenerator`). An `IntersectionComponent` node attached to the point (and to the road pieces through the intersection) leads back to the `Intersection` when the user clicks on it.

* `roads`: the connected roads; `sizes`: how far each road is pushed back from the center; `heights`: the height of each road end relative to the point. `GetMouth` describes the end of a road as the intersection sees it: its direction, its left vector (shorter where the end is rolled, skewed along the road where it is yawed) and its bank slope. `RecalculateSize` computes the sizes from the angles and widths on the ground of the roads (so that their edges do not overlap; the corner of a skewed end that is farther in must still reach the edge of the next road), then adds the crosswalk size, the per-road intersection size increase and the intersection's own `sizeIncrease`. It then computes the heights (`RecalculateHeights`): the connection height of each road, plus for the automatic ones the height of the plane fitted through the mouth corners of the other roads (the ones not automatic).
* `GetRoadsSortedClockwise` gives the roads around the center in clockwise order (used for junctions and road choices).
* `RebuildRoads` keeps the end points of the connected roads on the center point when the intersection moves (projecting it on the terrain if one of the roads has `project` set). When the intersection didn't move but the end point of one of its roads did (compared with the positions of the previous update, horizontally for the roads projected on the terrain), the intersection moves there and the other roads follow. While a road is edited, the handles of its intersections are hidden and its ends on them are as big (`Road.SetActive`), so that they can be selected and moved; M on an intersection point while editing one of its roads edits the end of the road instead (`CityBuilderMenuBar.EditCurSelectedObjectTransform`). Moving such an end records the intersection in the undo step too (`UndoManager.TouchOwner`).
* **Roads through the intersection**: the road instance flags `startIntersectionStart`/`endIntersectionStart` and `startIntersectionEnd`/`endIntersectionEnd` mark roads where a road continues across the intersection. For each "start" road, a road piece (a `RoadGenerator` child of the intersection, using that road's state) is generated across the intersection towards the "end" road.
* `RebuildMesh` regenerates the mesh when forced (road or state change) and updates the terrain anchors.
* **Seamless intersections**: with the instance property `seamless` (`IsSeamlessSet`) and exactly two roads (`IsSeamless`), the two roads join without a surface between them, as if they were one road. The sizes and heights are 0, the generator makes no mesh (`IntersectionGenerator.RebuildSeamless`), only the anchor lines joining the anchor lines of the two roads (from the ends of those of the first road to the closest ends of those of the second one), so that the terrain anchor chains go on from one road to the other. The ends of the roads share a tangent (`GetSeamlessTangent`), computed from the control points next to their ends only, so that the roads don't depend on each other: its direction is between the directions of the two ends (so that the sections of low-poly roads match), its length the distance between those control points. `Road.GetEndTangents` gives the tangents to the curve (`GeometryHelper.GetPointOnCurve` and `GetValueOnCurve`): a Hermite curve uses it as the tangent of its end span (times the tension), so that two seamless Hermite roads follow the same curve as a single Hermite road through their points; a Bézier curve gets a hidden control point next to its end along it (at the distance keeping the speed of the curve there; the control points of the road are not moved); a low-poly curve doesn't change. The end sections of both roads take its direction (wider for low-poly roads and with `adjustLowPolyWidth`, as the sections between their segments), so they match when the roads have the same section. A road compares what its seamless ends get from the other road at each update, so it follows the changes of the other road. The flag is kept with more than two roads, but has no effect. A split makes seamless intersections (`RoadSplitter.Split`).
* `state` holds the intersection type parameters and the built-in `type`, `sidewalkSegments`, `sizeIncrease`; `instanceState` the instance-specific ones (and the built-in `seamless`).

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

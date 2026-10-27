# Geometry Generation

The generators in `Scripts/CityElements/Geometry` build the meshes of the city elements. They combine three inputs: the element's state (user parameters), the geometry of the element (curve points, polygon, neighboring roads) and the runtime type of the core, whose expressions describe the actual vertices, UVs and faces. The format of those descriptions is explained for core authors in [Core Roads](Core-Roads), [Core Intersections](Core-Intersections) and the other core pages.

## Roads

### `RoadLikeGenerator<T>` (`Geometry/Helpers/RoadLikeGenerator.cs`)

The engine shared by roads, junctions and terrain patch border meshes. It works on a list of sections along a curve:

* Inputs filled by the caller: `curvePoints`, `curveRightVectors`/`sectionRights`, `sectionMarkers` (distance along the curve), `sectionVertices` (the cross-section points of each section), and optionally `groundHeights`.
* `Reset(type, ...)` clones the type's variable container (only when the type changes) and clears the buffers.
* `GetRawSectionVertices(i)` evaluates the type's section vertex expressions and places them at `curvePoint + right * value`.
* `InitSection(i)` fills the per-section variables (`localUp/Right/Forward`, `z`, `segment`, `v<n>`, `x<n>`, `absX<n>`...) and the dynamic definitions.
* `AddSection(i, sidePoints)`:
  1. evaluates the type's anchors and each active component's anchors, appending them to the terrain anchor lines;
  2. for each component whose condition is true, fills its local definitions and evaluates its `mainMesh`: the vertices and UVs are appended, and from the second section on each entry of `faces` becomes a quad between vertex `j` and `j + 1` of the previous section and the same vertices of the current section;
  3. on the last section, evaluates the `endMesh` of each component, then re-initializes the first section and evaluates the `startMesh`s. Cap faces are triangles (three indices each) into the vertices of that cap.
* Each face goes to the submesh `texturesMapping[component][facesTextures[j]]`, an index into the type's `textures` list.

### `RoadGenerator` (`Geometry/RoadGenerator.cs`)

The node holding a road's mesh, collider, center line (`lr`), lane lines and props (also used for the road pieces through an intersection).

* `Rebuild` → `RebuildLine` samples the curve between `start`, the control points `cps` and `end` (already shortened by the intersection sizes), computes the direction and right vector of each section (towards the intersection centers at the ends, averaged for low-poly curves), fills the initial variables and computes the section vertices. The yaw and roll of the control points (`cpRotations`, or `forcedRotations` for the roads through intersections) are blended along the curve (`GeometryHelper.GetValueOnCurve`, with the Bernstein weights for Bézier curves, the Hermite interpolation through the values for Hermite curves, the value of the point for low-poly ones) and rotate the right vector of each section (`GeometryHelper.RotateSection`: the yaw around the vertical axis with the length divided by its cosine, then the roll around the horizontal forward direction) and its up vector, which `localUp` gets (the sections on an intersection and the roads through intersections keep it vertical). With `makeCoplanar`, each section is rotated so that it is coplanar with the previous one when possible (`GetCoplanarDir`), skipped below the `pointPlaneTreshold` distance and on rolled roads. When the type has `variableSections`, or the low-poly width fix is active, the dynamic definitions are evaluated before computing each section, so section vertices can depend on them.
* After the line, the common late definitions are evaluated (with `v<n>_0`/`v<n>_1`, the first and last section vertices) and the traffic lanes and props lines are prepared (`GenerateLanesPre`).
* `RebuildMesh` adds all sections, creates one line renderer per traffic lane and props line bound (`GenerateLanesPost`), keeps only the non-empty submeshes, assigns the materials (`GetMaterialSet`, from the type's `textures` list read from the state) and refreshes the collider.
* `PlaceProps` places the props of the road's prop rule (see below).
* `GetStandard*`, `GetFloat/Bool/Vec3/Vec2`, `Has*` give access to the type's definitions with the road's current variables (used by junctions to import road values).
* `sidePoints` holds the terrain anchor lines generated during the rebuild.

#### Lanes and props

Traffic lanes and props lines are described by pairs of bound expressions. For each section the generator evaluates both bounds and stores the point at the lane's position (0..1) between them, which gives one polyline per lane. Lane polylines are drawn in the color of their traffic type and exported with the city.

`PlaceProps` reads the road's `propRule` state and places the props with `PropPlacer` (shared with the low-level elements, whose lines are the `propLines` of their geometry script, see [Core Low-Level Elements](Core-Low-Level-Elements)). For each props container of the road type that is enabled in the rule, and for each element in it, the element type's placement loop runs: starting at `i = 0`, it evaluates `zPos` (distance along the line), `xPos` (0 = start bound, 1 = end bound) and the conditions, places the mesh on the ground at that point facing `forward`, and continues until `whileCondition` is false (with a hard limit of 1000 props per element). Props are put on layer 13 and named `<container>;<mesh>`.

## Intersections

### `IntersectionGenerator` (`Geometry/IntersectionGenerator.cs`)

Builds the mesh of an intersection. `RebuildMesh`:

1. Computes, for each road, the center of its end section and its left vector (`CalculateVerticesAndCenters`), and sorts the roads clockwise. The ends come from `Intersection.GetMouth`. The left vector of a road whose end is rolled is shorter (by the cosine of the roll: the road is narrower on the ground, as for the intersection size in `Intersection.RecalculateSize`), and its slope is kept: the vertices of the intersection near that road (`Bank`) are moved only vertically, by the connection height of the road plus their offset from its center line times the slope. The left vector of a yawed end is skewed by the tangent of the yaw along the road: the edge lines of the road stay where they are, and its corners slide along them (the planes along the roads keep the square left vector). Along the junction curves the heights blend from the end of road I to the end of road J, and the roads through the intersection are rotated from the yaw and roll of one end to the ones of the other.
2. For each pair of consecutive roads (I, J) (`ProcessRoad`), picks the junction type for their road types, evaluates the junction's section vertices for both road ends (using the standard getters of each road), and builds one curve per junction section vertex: a quadratic Bézier curve from the offset point on road I's end to the offset point on road J's end, whose control point is the intersection of the two offset edge lines (dropped, giving a straight segment, when that point lies behind one of the roads). The curves are sampled according to the intersection's `sidewalkSegments` and the junction's `actualSegments` expression.
3. Collects the road surface polygon between the junction curves (`SubroadManager`), with separate pieces when roads use non-default intersection textures, and the crosswalks.
4. `GenerateMesh` calls `IntersectionGroup.Generate` to triangulate everything and builds the submeshes and materials: the extra textures, then each junction's `textures`, then the crosswalk textures.

The nested `GeneratorState` keeps the per-rebuild data (junction ends, convexity, self-intersection flags, planes) and builds each junction's state (`GetJunctionState`): a copy of the intersection state plus `thisIsEndA/B`, `convex`, `selfIntersectingSpline`, `notDefaultTex`, the imported road parameters and the resolved texture definitions.

### `IntersectionGroup` (`Geometry/IntersectionGroup.cs`)

The triangulation step of an intersection:

1. **Extra terrain pieces**: for each piece defined by the intersection type, checks the junction condition on every junction, combines the results (`or`, `and`, `xor`), sets the `<piece>_index` and `<piece>_active` variables and evaluates the piece condition. Enabled pieces become extra surfaces through the points given by the junctions' `extraTerrainSplines` (walls and gallery ceilings in MM2).
2. **Road surfaces**: each polygon from the `SubroadManager` is triangulated (Delaunay constrained to the polygon edges), UVs are the world X/Z coordinates multiplied by the piece's UV multipliers and by the intersection's `uMult`/`vMult` definitions.
3. **Junctions**: each junction is generated with a `RoadLikeGenerator` along its curve, like a short road, and its anchors are added to the terrain anchor lines.

### `SubroadManager` (`Geometry/Helpers/SubroadManager.cs`)

Accumulates the polygons that make up the road surface of an intersection (and the extra pieces): `CreateSubRoad(texture, ...)` starts a polygon with its texture and UV multipliers, `AddSegmentToSubroad` appends a junction curve (detecting convex segments) and `MergeSubroadsIfNeeded` joins road surface pieces that have the same texture and share at least one vertex.

## Terrain patches

### `TerrainGeneration` (`Geometry/Helpers/TerrainGeneration.cs`)

`GetMesh(perimeter, internalPoints, borderMeshes, texture, smooth, uMult, vMult)`:

1. Removes consecutive duplicate perimeter points and triangulates perimeter + internal points with Delaunay (Delaunator), constrained to the perimeter edges (`ConstrainedDelaunay`), keeping only the triangles inside the polygon.
2. Optionally smooths the internal vertices (`GeometryHelper.SmoothMeshConstrained`, `smooth` iterations).
3. Sets the UVs to the world X/Z coordinates times `uMult`/`vMult` (definitions of the terrain patch type).
4. Generates each border mesh with a `RoadLikeGenerator` along its segment, using its border mesh type (a road-like type) and state.
5. An empty texture gives an invisible patch (`_TRANSPARENT_` material).

`GetSubmesh` returns the same result as a `SubMeshData` (used for building roofs).

### `TerrainPatchGenerator` (`Geometry/TerrainPatchGenerator.cs`)

The node of a terrain patch: mesh, collider and outline. `RebuildMesh` calls `TerrainGeneration.GetMesh` and uploads the result.

### `TerrainSubmeshGenerator` (`Geometry/TerrainSubmeshGenerator.cs`)

A flat polygon mesh with a collider, used for the roofs of buildings and building lines.

## Buildings

### `BuildingSideGenerator` (`Geometry/BuildingSideGenerator.cs`)

Generates one side (front, left, right or back) of a building from its side state. A side is made of:

* a **block dictionary** (`blockDict`): the list of blocks the side uses, each one a building block type with its parameters (for example a texture);
* an optional **ground floor** (`hasGroundFloor`, `groundFloor`): a minimum height and a list of slices, each with a `bottom`, `middle` and `top` block, a `Width` and a number of `Repetitions`. The ground floor follows the slope of the terrain: the bottom block fills the space below the floor line;
* **upper floors** (`upperFloors`): each with a `height` and a list of slices (`block`, `Width`, `Repetitions`). The first `lowUpperFloorsCount` floors are used once at the bottom, the last `highUpperFloorsCount` once at the top, and the ones in between are repeated to fill the building height.

Slices are repeated along the side as long as they fit, then scaled horizontally to fill its length. Adjacent identical slices are merged into one wider block. Blocks are generated as:

* `texture` blocks (a built-in block type name): a textured quad, stored once per size in `meshDict` and placed with transformation matrices (`Facade.instances`);
* `empty` blocks (built-in name): nothing;
* any other type: a parametric mesh from `BuildingBlockGenerator`.

The result is exported both as one merged mesh and as the original instancing data (facades, mesh dictionary, parametric meshes), so exporters can recreate the structure.

### `BuildingBlockGenerator` (`Geometry/Helpers/BuildingBlockGenerator.cs`)

Evaluates a parametric building block type: fills the block's parameters and static definitions, then the block variables (`y0`, `y1`, `xMult`, `scale`, `uMult`, `vMult`, `localUp/Right/Forward`), then the dynamic definitions; evaluates each active component's mesh (triangles, three indices per face, offset by the vertices of the previous components) and returns vertices, UVs, one index list per texture and the material names.

### `SubMesh` (`Geometry/Helpers/SubMesh.cs`)

`SubMeshData` is an engine-independent mesh (vertices, UVs, one index array and one material name per submesh, plus a position and scale). `MergeSubmeshes` combines several of them (merging submeshes with the same material) with an optional transform, and `GetMesh`/`SetMeshData` convert them to engine meshes.

## Parametric objects

### `ParametricObjectContainer<T>` (`Geometry/Helpers/ParametricObjectContainer.cs`)

Base class of every element that can own parametric objects (`Road`, `CityProperties`, `ArrayObject`). For each `objectInstance` parameter of its runtime type whose condition is true, it creates a `MeshInstance` inside a container node (`SyncObjectInstanceParamsDict`), chooses its model (the user's choice if allowed, the default model otherwise, see [Core Props and Objects](Core-Props-and-Objects#parametric-objects)), and places the container at the position, rotation and scale given by the type's definitions (`GetObjectPosition`, `GetObjectRotation`, `GetObjectScale`, all overridable). For `array` and `customElement` parameters it keeps one `ArrayObject` per element (`SyncArrays`), recursively. `UpdateObjects` resynchronizes everything when the state changes and repositions the objects when the parent was rebuilt.

### `ArrayObject` (`Geometry/Helpers/ArrayObject.cs`)

A node standing for one element of an `array` or `customElement` parameter. It is itself a `ParametricObjectContainer`, so array elements can contain parametric objects (for example the checkpoints of an MM2 race). It also implements exclusive editing: when a custom type has `exclusiveEditing` enabled, only the selected element of a category is visible (or all of them when none is selected and the category is `optional`).

## Helpers

| Class | Description |
|---|---|
| `GeometryHelper` (`GeometryHelper.cs`) | Curve sampling (`GetPointOnCurve` for Bézier, Hermite and low-poly curves, with optional equal subdivision), low-pass filter, polygon tests (point inside, clockwise, distance), line intersection, closest points, clockwise sorting, Delaunay triangulation wrapper, constrained smoothing, `ProjectPoint` (on the terrain), section index helpers, a cube mesh for fallback objects. |
| `ConstrainedDelaunay` (`Geometry/Helpers/ConstrainDelaunay.cs`) | Inserts missing constraint edges into a Delaunay triangulation (flip-based). |
| `PoissonDiscSampler` (`Geometry/Helpers/PoissonDiscSampler.cs`) | Deterministic Bridson Poisson-disc sampling, used to generate internal terrain patch points. |
| `LineSegmentsIntersection.Math2d` (`Geometry/Helpers/LineSegmentIntersection.cs`) | 2D segment intersection. |

## Ground and city-wide generation

### `CityGroundHelper` (`CityGroundHelper.cs`)

The script on the `CityGroundHelper` node. It starts the program (see [Architecture Overview](Architecture-Overview#scene-tree-and-startup)), owns the `ElementManager` and the progress bar, and manages the heightmap terrain: creating it with default or loaded heights (`SetHeights`, `SetDefaultHeights`, `ApplyHeightmap` from a grayscale image), the grid and ground overlay materials (`LoadOverlayTexture`, `SwitchMaterial`, `SwitchTransparency`), the brush circle of the terrain tools, and saving the city (`SaveCity` stores the core feature version in the preferences and writes `city.json.gz`). Its static fields hold the terrain size, heightmap resolution and maximum height of the current city.

### `CitywideTerrainGenerator` (`CitywideTerrainGenerator.cs`)

"Create whole city terrain": starting from every road and intersection anchor not used by a patch yet, creates a terrain patch and closes it by following the anchor chains (`TerrainPatch.AutoClose`), which fills every block enclosed by roads. Patches that would reuse anchors are discarded. A patch that encloses other anchors is the outer boundary of the road network: it is extended outwards by the given thickness and split into a ring of patches (using the segment length and vertex fusion distance settings). Finally every new patch gets internal points (if an internal point distance is set) and the chosen smoothing.

### `CitywideBuildingGenerator` (`CitywideBuildingGenerator.cs`)

"Create whole city buildings": for each closed loop of anchors, optionally only where the underlying terrain patch uses one of the selected textures, creates a building line and closes it with `BuildingLine.AutoClose`, splitting it into buildings of random length that take their states from the chosen presets.

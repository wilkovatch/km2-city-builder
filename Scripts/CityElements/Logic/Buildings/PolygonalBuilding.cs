using System.Collections.Generic;
using Godot;
using PhysicsScriptable = Godot.StaticBody3D; //the wall and roof colliders are attached to the building
using GameObject = Godot.Node3D;
using LineRenderer = GameEngine.LineRenderer;
using Mesh = Godot.ArrayMesh;
using MeshRenderer = Godot.MeshInstance3D;
using MeshCollider = Godot.CollisionShape3D;
using GO = GameEngine.GameObject;
using States;
using SubMeshData = GeometryHelpers.SubMesh.SubMeshData;
using RC = RuntimeCalculator;
using V3 = GameEngine.Vector3;

//A single building on the outline through its points, in their order and always closed (it can be concave): a wall on
//each edge, with the facade of a building side, and a flat roof at the height above the highest point. The walls always
//face outwards, whichever way the points go around. They use one side state (sideState in the state), or one each
//(individualSides), kept by the point the edge starts at (in the order of the points).
public partial class PolygonalBuilding : PhysicsScriptable, IGroundable, IObjectWithState, IBuildingOwner {
    List<TerrainPoint> points = new List<TerrainPoint>();
    public ObjectState state, instanceState;
    //the side states of the walls (individualSides), by the point their edge starts at
    Dictionary<TerrainPoint, ObjectState> wallStates = new Dictionary<TerrainPoint, ObjectState>();
    //the walls, one for each edge of the outline: wall k is the edge from point outline[k] to point outline[k + 1]
    public List<Building> walls = new List<Building>();
    List<int> outline = new List<int>();
    bool reversed = false; //the walls go from the end of their edge to its start, so that they face outwards
    bool selfIntersecting = false;
    public TerrainSubmeshGenerator roof;
    bool deleted = false;
    bool selected = false;

    public MeshRenderer mr;
    public Mesh m;
    public LineRenderer lr;
    public GameObject container;

    TerrainAnchor curAnchor = null;
    List<Vector3> oldPoints = new List<Vector3>();
    float wallsHeight, wallsMaxHeight;
    //the walls are being updated by the building (the side states they get are not edits)
    bool updatingWalls = false;
    //a side state given to a wall from outside (a preset), to give to every wall
    ObjectState pendingSharedState = null;

    CityElements.Types.Runtime.Buildings.BuildingType.Polygon curType;
    RC.VariableContainer variableContainer;

    public void Initialize(GameObject container) {
        (mr, m) = GameEngine.Mesh.CreateMesh(GO.GetObject(this), new Material[] { MaterialManager.GetMaterial("_TRANSPARENT_", true) });
        state = PresetManager.GetPreset("polygonalBuilding", 0);
        instanceState = new ObjectState();
        var type = GetPolygonType();
        if (type != null) instanceState.SetBool("projectToGround", type.typeData.settings.projectToGround);
        this.container = container;
        lr = GO.CreateLineRenderer(this);
        lr.startWidth = 0.5f;
        lr.endWidth = 0.5f;
        lr.material = MaterialManager.GetHandleMaterial((GameEngine.Color.red + GameEngine.Color.yellow) * 0.5f);
        lr.enabled = false;
    }

    public ObjectState GetState() {
        return state;
    }

    public GameObject GetBody() {
        return this;
    }

    public string GetBuildingTypeName() {
        return state.Str("type", null);
    }

    public CityElements.Types.Runtime.Buildings.BuildingType.Polygon GetPolygonType() {
        var name = state.Str("type", null);
        if (name == null) return null;
        var dict = CityElements.Types.Parsers.TypeParser.GetPolygonalBuildingTypes();
        return dict.TryGetValue(name, out var res) ? res : null;
    }

    void ReloadType() {
        var type = GetPolygonType();
        if (type == null) {
            curType = null;
            variableContainer = null;
            return;
        }
        if (type != curType) {
            variableContainer = type.variableContainer.GetClone();
            curType = type;
        }
        curType.FillInitialVariables(variableContainer, state, instanceState);
    }

    public bool IsIndividual() {
        return state.Bool("individualSides");
    }

    //whether the outline crosses itself (the walls and the roof are still generated)
    public bool IsSelfIntersecting() {
        return selfIntersecting;
    }

    //Points

    public List<TerrainPoint> GetPointsComponents() {
        return new List<TerrainPoint>(points);
    }

    public int GetPointCount() {
        return points.Count;
    }

    public bool ContainsPoint(TerrainPoint p) {
        return points.Contains(p);
    }

    //the index of the point the edge of each wall starts at, in the order of the walls
    public List<int> GetWallPoints() {
        var res = new List<int>();
        foreach (var wall in walls) res.Add(points.IndexOf(wall.firstPoint));
        return res;
    }

    //pointOnly: false follows the anchors from the previous anchor to the clicked one
    public TerrainPoint AddPoint(GameObject obj, Vector3 point, bool pointOnly = true) {
        History.UndoManager.Touch(this);
        return AddTerrainPointToSequence.AddPoint(points, ref curAnchor, container, this,
            delegate {
                var lastP = points[points.Count - 1];
                curAnchor = lastP.anchor is TerrainAnchor ta && !GO.IsNull(ta) ? ta : null;
            },
            delegate { ReloadLine(); },
            delegate { instanceState.SetBool("projectToGround", false); }, obj, point, pointOnly);
    }

    //adds a free point at the end (used by the dummy building of the preset editor)
    public TerrainPoint AddFreePoint(Vector3 position) {
        return InsertPoint(points.Count, position);
    }

    //inserts a free point before the point at the index (at the end with the number of points)
    public TerrainPoint InsertPoint(int index, Vector3 position) {
        if (deleted || index < 0 || index > points.Count) return null;
        History.UndoManager.Touch(this);
        var point = TerrainPoint.Create(position, this, container);
        points.Insert(index, point);
        ReloadLine();
        return point;
    }

    //the index to insert a point at to put it in the edge of the outline closest to the position (-1 with less than two
    //points)
    public int ClosestInsertIndex(Vector3 position) {
        return AddTerrainPointToSequence.ClosestSegmentInsertIndex(points, position, true);
    }

    public void RemoveLastPoint() {
        if (points.Count < 1) return;
        History.UndoManager.Touch(this);
        var p = points[points.Count - 1];
        p.DeleteManual(this);
        p.Select(false);
        ReloadLine();
    }

    public bool RemovePoint(object point) {
        if (point is TerrainPoint p) {
            History.UndoManager.Touch(this);
            if (!p.IsDeleted()) PointLink.Remove(p.GetObject(), this);
            return points.Remove(p);
        }
        return false;
    }

    public bool IsProjectedToGround() {
        return instanceState.Bool("projectToGround");
    }

    //the edges can be clicked (to insert a point in them), as the ones of the terrain patches
    void UpdatePointLinks() {
        for (int i = 0; i < points.Count; i++) {
            if (points[i].IsDeleted()) continue;
            var last = i == points.Count - 1;
            if (points.Count < 2 || (last && points.Count < 3)) {
                PointLink.Remove(points[i].GetObject(), this);
            } else {
                PointLink.Create(points[i].GetObject(), points[last ? 0 : i + 1].GetObject(), 0.9f, this);
            }
        }
    }

    //Side states

    //the side state of the wall starting at the point (the shared one without individual sides)
    public ObjectState GetWallState(TerrainPoint point) {
        if (!IsIndividual()) return state.State("sideState");
        return point != null && wallStates.TryGetValue(point, out var res) ? res : null;
    }

    //the side states of the walls starting at the points, in their order (null for the points without one)
    public ObjectState[] GetWallStatesArray() {
        var res = new ObjectState[points.Count];
        for (int i = 0; i < points.Count; i++) {
            if (wallStates.TryGetValue(points[i], out var s)) res[i] = s;
        }
        return res;
    }

    //gives every point without a side state of its own the one of the wall it was added in (the closest point before it
    //with one), or a copy of the shared one (when switching to individual sides)
    public void FillWallStates() {
        var shared = state.State("sideState");
        var had = new HashSet<TerrainPoint>(wallStates.Keys);
        for (int i = 0; i < points.Count; i++) {
            if (had.Contains(points[i])) continue;
            ObjectState source = null;
            for (int k = 1; k < points.Count && source == null; k++) {
                var prev = points[(i - k + points.Count) % points.Count];
                if (had.Contains(prev)) source = wallStates[prev];
            }
            source ??= shared;
            if (source != null) wallStates[points[i]] = (ObjectState)source.Clone();
        }
    }

    //called by the side generators when their state is replaced (also by a copy, when they are created)
    public void OnFrontStateSet(Building building, ObjectState newState) {
        if (building.firstPoint == null || newState == null) return;
        if (IsIndividual()) {
            wallStates[building.firstPoint] = newState;
        } else if (!updatingWalls) {
            pendingSharedState = newState;
        }
    }

    //Lifecycle

    void Clear() {
        foreach (var point in new List<TerrainPoint>(points)) {
            if (!point.IsDeleted()) PointLink.Remove(point.GetObject(), this);
            point.DeleteManual(this);
        }
        points.Clear();
        ClearWalls();
        lr.enabled = false;
    }

    void ClearWalls() {
        foreach (var wall in walls) wall.Delete();
        walls.Clear();
        outline.Clear();
        if (roof != null) roof.Delete();
        roof = null;
    }

    public void Delete() {
        History.UndoManager.Touch(this);
        Clear();
        GO.Destroy(this);
        deleted = true;
    }

    public bool IsDeleted() {
        return deleted;
    }

    //removes the building without deleting its points (used by undo/redo, which deletes them explicitly)
    public void RemoveForUndo() {
        foreach (var point in points) {
            if (!point.IsDeleted()) PointLink.Remove(point.GetObject(), this);
            point.RemoveLinkSilently(this);
        }
        points.Clear();
        ClearWalls();
        lr.enabled = false;
        GO.Destroy(this);
        deleted = true;
    }

    //sets the points, the states and the side states of the walls (used by undo/redo and loading)
    public void RestoreFromSnapshot(List<TerrainPoint> newPoints, ObjectState newState, ObjectState newInstanceState, ObjectState[] newWallStates) {
        var oldSet = new HashSet<TerrainPoint>(points);
        var newSet = new HashSet<TerrainPoint>(newPoints);
        foreach (var point in newSet) {
            if (!oldSet.Contains(point)) point.AddLink(this);
        }
        foreach (var point in oldSet) {
            if (!newSet.Contains(point)) {
                if (!point.IsDeleted()) PointLink.Remove(point.GetObject(), this);
                point.RemoveLinkSilently(this);
            }
        }
        points = new List<TerrainPoint>(newPoints);
        state = newState;
        instanceState = newInstanceState;
        wallStates.Clear();
        if (newWallStates != null) {
            for (int i = 0; i < newWallStates.Length && i < points.Count; i++) {
                if (newWallStates[i] != null) wallStates[points[i]] = newWallStates[i];
            }
        }
        pendingSharedState = null;
        curAnchor = null;
        oldPoints.Clear();
        state.FlagAsChanged(); //forces the rebuild
        ClearWalls(); //the walls are made again from the states
        lr.enabled = false;
    }

    //replaces the state and the side states of the walls (in the order of the points), keeping the walls
    public void SetStates(ObjectState newState, ObjectState[] newWallStates) {
        state = newState;
        if (newWallStates != null) {
            for (int i = 0; i < newWallStates.Length && i < points.Count; i++) {
                if (newWallStates[i] != null) wallStates[points[i]] = newWallStates[i];
            }
        }
        pendingSharedState = null;
        state.FlagAsChanged();
    }

    //Update

    bool DidChange() {
        //every polygonal building gets here at every update of the city: nothing is allocated when nothing changed
        var anyDeleted = false;
        foreach (var point in points) {
            if (point.IsDeleted()) {
                anyDeleted = true;
            } else {
                point.UpdatePosition();
            }
        }
        if (anyDeleted) points.RemoveAll(point => point.IsDeleted());
        if (oldPoints.Count != points.Count) return true;
        for (int i = 0; i < points.Count; i++) {
            if (!GeometryHelper.AreVectorsEqual(points[i].GetPoint(), oldPoints[i])) return true;
        }
        if (state.HasChanged()) return true;
        return false;
    }

    void UpdateOlds() {
        oldPoints.Clear();
        foreach (var point in points) {
            point.UpdateOlds();
            oldPoints.Add(point.GetPoint());
        }
        state.FlagAsUnchanged();
    }

    //an edit of the shared side state made on one of the walls (in place in the side editor, or a preset) is given to
    //the building, and with it to the other walls
    public void SyncSharedSideEdits() {
        if (IsIndividual()) {
            pendingSharedState = null;
            return;
        }
        var edited = pendingSharedState;
        pendingSharedState = null;
        if (edited == null) {
            foreach (var wall in walls) {
                var front = wall.state.State("frontState", false);
                if (front != null && front.HasChanged()) {
                    edited = front;
                    break;
                }
            }
        }
        if (edited != null) state.SetState("sideState", (ObjectState)edited.Clone());
    }

    public int UpdateBuilding() {
        if (deleted) return 0;
        SyncSharedSideEdits();
        if (DidChange()) {
            var res = UpdateWalls(true);
            UpdateOlds();
            return res;
        } else {
            return UpdateWalls(false);
        }
    }

    static bool SamePlace(Vector3 a, Vector3 b) {
        return Mathf.Abs(a[0] - b[0]) <= GeometryHelper.epsilon && Mathf.Abs(a[2] - b[2]) <= GeometryHelper.epsilon;
    }

    //the indices of the points of the outline, in their order, without the ones at the same place (on the horizontal
    //plane) as the one before, and whether the walls must go around it the other way to face outwards (the facade of a
    //side looks towards -cross(direction, up), see BuildingSideGenerator.CreateSegment); empty if it has no area
    public static (List<int> order, bool reversed) Outline(List<Vector3> positions) {
        var order = new List<int>();
        for (int i = 0; i < positions.Count; i++) {
            if (order.Count > 0 && SamePlace(positions[order[order.Count - 1]], positions[i])) continue;
            order.Add(i);
        }
        while (order.Count > 1 && SamePlace(positions[order[0]], positions[order[order.Count - 1]])) order.RemoveAt(order.Count - 1);
        if (order.Count < 3) return (new List<int>(), false);
        //no area only if the points are in line (an outline crossing itself can have no area in total)
        var p0 = positions[order[0]];
        var p1 = positions[order[1]];
        var inLine = true;
        foreach (var i in order) {
            var p = positions[i];
            if (Mathf.Abs((p1[0] - p0[0]) * (p[2] - p0[2]) - (p1[2] - p0[2]) * (p[0] - p0[0])) > GeometryHelper.epsilon) {
                inLine = false;
                break;
            }
        }
        if (inLine) return (new List<int>(), false);
        return (order, SignedArea(positions, order) < 0);
    }

    //the horizontal area of the polygon through the points with the indices, in their order: positive when the facades
    //of walls going that way face outwards
    public static float SignedArea(List<Vector3> positions, List<int> order) {
        var res = 0.0f;
        for (int k = 0; k < order.Count; k++) {
            var a = positions[order[k]];
            var b = positions[order[(k + 1) % order.Count]];
            res += a[0] * b[2] - b[0] * a[2];
        }
        return res * 0.5f;
    }

    //whether two edges of the outline that are not next to each other cross (on the horizontal plane)
    public static bool SelfIntersecting(List<Vector3> positions, List<int> order) {
        float Cross(Vector3 o, Vector3 a, Vector3 b) => (a[0] - o[0]) * (b[2] - o[2]) - (a[2] - o[2]) * (b[0] - o[0]);
        var n = order.Count;
        for (int i = 0; i < n; i++) {
            var a1 = positions[order[i]];
            var a2 = positions[order[(i + 1) % n]];
            for (int j = i + 2; j < n; j++) {
                if (i == 0 && j == n - 1) continue; //the edges next to each other across the closing point
                var b1 = positions[order[j]];
                var b2 = positions[order[(j + 1) % n]];
                var d1 = Cross(b1, b2, a1);
                var d2 = Cross(b1, b2, a2);
                var d3 = Cross(a1, a2, b1);
                var d4 = Cross(a1, a2, b2);
                if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            }
        }
        return false;
    }

    //the state the walls start from: only the front, with the height given by the building
    static ObjectState WallState() {
        var res = new ObjectState();
        res.SetBool("front", true);
        res.SetBool("left", false);
        res.SetBool("right", false);
        res.SetBool("back", false);
        res.SetBool("top", false);
        res.SetBool("allSidesEqual", false);
        res.SetBool("fixAcuteAngles", false);
        res.SetFloat("depth", 1.0f);
        return res;
    }

    //makes the walls match the edges of the outline, keeping the ones whose edge is still there
    void MatchWalls() {
        var newWalls = new List<Building>();
        var oldWalls = new List<Building>(walls);
        for (int k = 0; k < outline.Count; k++) {
            var first = points[outline[k]];
            var last = points[outline[(k + 1) % outline.Count]];
            var wall = oldWalls.Find(w => w.firstPoint == first && w.lastPoint == last);
            if (wall != null) {
                oldWalls.Remove(wall);
            } else {
                wall = new Building();
                wall.container = GO.GetObject(this);
                wall.Initialize(this, WallState());
                wall.firstPoint = first;
                wall.lastPoint = last;
            }
            newWalls.Add(wall);
        }
        foreach (var wall in oldWalls) wall.Delete();
        walls = newWalls;
    }

    //gives each wall its side state (a copy of the shared one, or its own one)
    void AssignWallStates() {
        var shared = state.State("sideState", false);
        if (IsIndividual()) FillWallStates();
        foreach (var wall in walls) {
            var current = wall.state.State("frontState", false);
            ObjectState target;
            if (IsIndividual()) {
                target = wallStates.TryGetValue(wall.firstPoint, out var own) ? own : null;
                if (target == null || target == current) continue;
            } else {
                //the shared state was copied from this one (or it is already the same)
                if (shared == null || (current != null && current.Equals(shared))) continue;
                target = (ObjectState)shared.Clone();
            }
            wall.state.SetState("frontState", target);
            target.FlagAsChanged();
        }
    }

    int UpdateWalls(bool force) {
        //nothing changed (most buildings at every update of the city)
        if (!force && !walls.Exists(w => w.NeedsUpdate())) return 0;
        var count = 0;
        if (force) {
            ReloadType();
            var positions = new List<Vector3>(points.Count);
            foreach (var p in points) positions.Add(p.GetPoint());
            (outline, reversed) = Outline(positions);
            selfIntersecting = outline.Count >= 3 && SelfIntersecting(positions, outline);
            if (outline.Count < 3 || curType == null) {
                foreach (var wall in walls) wall.Delete();
                walls.Clear();
                if (roof != null) roof.Delete();
                roof = null;
                ReloadLine();
                return 1;
            }
            var maxH = float.MinValue;
            foreach (var i in outline) {
                if (positions[i][1] > maxH) maxH = positions[i][1];
            }
            wallsMaxHeight = maxH;
            wallsHeight = maxH + state.Float("height");
            MatchWalls();
            AssignWallStates();
            for (int k = 0; k < walls.Count; k++) {
                var p0 = positions[outline[k]];
                var p1 = positions[outline[(k + 1) % outline.Count]];
                if (reversed) (p0, p1) = (p1, p0);
                //the facade faces -cross(direction, up), the inside is on the other side
                var inward = V3.Normalize(V3.Cross(p1 - p0, V3.up));
                inward = V3.Normalize(new Vector3(inward[0], 0, inward[2]));
                walls[k].spline = new List<(Vector3 point, Vector3 normal)> { (p0, inward), (p1, inward) };
            }

            //the roof
            if (roof == null) roof = new TerrainSubmeshGenerator(this);
            var roofSpline = new List<Vector3>();
            foreach (var i in outline) roofSpline.Add(new Vector3(positions[i][0], wallsHeight, positions[i][2]));
            var vc = variableContainer;
            try {
                roof.UpdateMesh(roofSpline, state.Str("roofTex"), vc.floats[vc.floatIndex["uMult"]], vc.floats[vc.floatIndex["vMult"]]);
            } catch (System.Exception e) {
                GameEngine.LogWarning("Error during mesh generation on polygonal building " + GO.GetName(this) + ": " + e.StackTrace.ToString());
            }
        }
        updatingWalls = true;
        try {
            foreach (var wall in walls) wall.PreUpdateMesh(true, wallsHeight, wallsMaxHeight, force);
            foreach (var wall in walls) count += wall.UpdateMesh(false, false, null, null, null, null, null, null);
        } finally {
            updatingWalls = false;
        }
        if (count > 0 || force) ReloadLine();
        return count;
    }

    //the mesh of the walls and the roof, the outline and the clickable edges
    void ReloadLine() {
        UpdatePointLinks();
        var meshes = new List<SubMeshData>();
        foreach (var wall in walls) meshes.AddRange(wall.GetSubmesh());
        if (roof != null) meshes.Add(roof.GetSubmesh());
        GameEngine.Mesh.Clear(m);
        if (meshes.Count > 0) {
            var smd = GeometryHelpers.SubMesh.MergeSubmeshes(meshes, V3.zero, V3.one, 0);
            GeometryHelpers.SubMesh.SetMeshData(smd, m, mr);
        }
        var line = new List<Vector3>();
        foreach (var p in points) {
            if (!p.IsDeleted()) line.Add(p.GetPoint());
        }
        if (line.Count > 2) line.Add(line[0]);
        lr.positionCount = line.Count;
        lr.SetPositions(line.ToArray());
        lr.enabled = selected && line.Count > 1;
    }

    //Selection

    void SetOutline(bool active) {
        foreach (var wall in walls) wall.SetOutline(active, false);
    }

    public void SetActive(bool active, bool moveable) {
        if (deleted) return;
        foreach (var point in points) point.SetActive(active);
        moveable = active && moveable;
        foreach (var point in points) point.SetMoveable(moveable);
        SetOutline(active && moveable);
    }

    public void Select(bool selected) {
        if (deleted) return;
        this.selected = selected;
        foreach (var point in points) point.Select(selected);
        lr.enabled = selected && points.Count > 1;
    }

    //the wall whose side has the collider, if any
    public BuildingSideGenerator GetColliderSide(MeshCollider c) {
        foreach (var wall in walls) {
            var res = wall.GetSideFromCollider(c);
            if (res != null) return res;
        }
        return null;
    }

    //the side of the wall whose edge starts at the point (null if there is none)
    public BuildingSideGenerator GetWallSide(TerrainPoint point) {
        foreach (var wall in walls) {
            if (wall.firstPoint == point) return wall.front;
        }
        return null;
    }
}

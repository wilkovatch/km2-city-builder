using System.Collections.Generic;
using Godot;
using Lua;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using RC = RuntimeCalculator;
using CustomElements;

//An element of a custom element type of the core: control points with the structure defined by the type, an optional mesh
//(vertices, triangles and texture coordinates) and parameters. Its geometry and lines are built by the Lua script of the
//type, which can also keep its points constrained (e.g. aligned). The points (and the vertices of the mesh) are terrain
//points, so they can be anchored and shared with other elements. An element can be put in a container (another
//custom element whose type lists its type, e.g. a room of a PSDL file).
public partial class CustomElement : Scriptable, IGroundable, IObjectWithState {
    public ObjectState state;
    public PointGroup points;
    public readonly List<TerrainPoint> meshVertices = new List<TerrainPoint>();
    public readonly List<int> triangles = new List<int>();
    public readonly List<Vector2> uvs = new List<Vector2>(); //the editable texture coordinates of the vertices
    public List<Vector2> scriptUVs; //the texture coordinates given by the geometry script (uvs "script")
    public CustomElement container;
    public object source; //the procedural element it replaces (a ghost), null if none
    public CustomElementGenerator generator;
    public CustomElementSplineGeometry.Data splineData;
    readonly Dictionary<TerrainPoint, Vector3> splineProjectedPoints = new();
    int splineProjectionUndoVersion = -1;
    GameObject pointContainer;
    Handle moveHandle; //a container being edited: moved, it moves the container with its contents (see ShowMoveHandle)
    Vector3 moveHandlePosition; //where the last update left it
    bool deleted = false;
    bool active = false;
    bool moveable = false;
    bool selected = false;
    TerrainPoint placementPoint;

    //the positions as the constraints last saw them (null: not run yet, the next update only records them)
    Dictionary<TerrainPoint, Vector3> constraintPositions = null;
    //the derived points (see UpdateDerivedPoints): seen by an update since the element was loaded, created or restored,
    //their flag, their inputs and the undo/redo applied, as that update saw them
    bool derivedSeen = false, derivedFlag = false;
    List<float> derivedInputs = null;
    int derivedAppliedVersion = -1;
    //the inputs of the last rebuild
    readonly List<Vector3> oldPositions = new List<Vector3>();
    int structureVersion = 0, oldStructureVersion = -1;
    int pointStateVersion = -1; //visibility and selection last applied to this structure
    CityElements.Types.Runtime.CustomElementType curType;
    RC.VariableContainer variableContainer;
    object updatedSource;
    CustomElement updatedContainer;
    bool updatingGeometry;
    int updatePointVersion = -1;
    List<TerrainPoint> updatePoints;
    List<(string path, PointLeaf leaf)> updateLeaves;

    internal void BeginUpdate() {
        updatingGeometry = true;
    }

    internal void EndUpdate() {
        updatingGeometry = false;
        updatePoints = null;
        updateLeaves = null;
        updatePointVersion = -1;
    }

    internal void RecordUpdate() {
        updatedSource = source;
        updatedContainer = container;
    }

    //Snapshots live for one manager update only: constraints and derived scripts invalidate them
    //when they replace the point tree. Public callers still get their own lists.
    void PrepareUpdatePoints() {
        if (updatePointVersion == structureVersion) return;
        updateLeaves = GetLeaves();
        updatePoints = new List<TerrainPoint>();
        var seen = new HashSet<TerrainPoint>();
        foreach (var (_, leaf) in updateLeaves) {
            if (leaf.point != null && seen.Add(leaf.point)) updatePoints.Add(leaf.point);
        }
        foreach (var point in meshVertices) {
            if (point != null && seen.Add(point)) updatePoints.Add(point);
        }
        updatePointVersion = structureVersion;
    }

    List<TerrainPoint> GetUpdatePoints() {
        if (!updatingGeometry) return GetAllPoints();
        PrepareUpdatePoints();
        return updatePoints;
    }

    List<(string path, PointLeaf leaf)> GetUpdateLeaves() {
        if (!updatingGeometry) return GetLeaves();
        PrepareUpdatePoints();
        return updateLeaves;
    }

    public string TypeName => state.Str("type", null);

    public void Initialize(string type, GameObject pointContainer) {
        this.pointContainer = pointContainer;
        state = Operations.CustomElementPresets.DefaultState(type);
        splineProjectionUndoVersion = History.UndoManager.AppliedVersion;
        points = new PointGroup(PointTree.RootDef(GetCustomElementType()?.typeData.settings.controlPoints));
        var generatorObj = GS.Create<CustomElementGenerator>("Geometry", this);
        generator = GS.Cast<CustomElementGenerator>(generatorObj);
        generator.Initialize();
        GO.SetLayer(generator, Settings?.geometryLayer ?? 7);
    }

    public ObjectState GetState() {
        return state;
    }

    public CityElements.Types.Runtime.CustomElementType GetCustomElementType() {
        var types = CityElements.Types.Parsers.TypeParser.GetCustomElementTypes();
        var type = TypeName;
        return type != null && types.TryGetValue(type, out var res) ? res : null;
    }

    public CityElements.Types.CustomElementSettings Settings => GetCustomElementType()?.typeData.settings;

    // Points

    public TerrainPoint CreatePoint(Vector3 position, IAnchorable anchor = null) {
        var p = TerrainPoint.Create(position, this, pointContainer, anchor);
        ApplyPointState(p);
        return p;
    }

    //a point new to the element gets the visibility and selection of the element's points (derived points can't be moved)
    void ApplyPointState(TerrainPoint p) {
        ApplySharedPointState(p);
    }

    //a point shared by custom elements (e.g. a road and the BAI road computed from it, a room and its contents) is
    //shown, selected and moveable if one of them shows, selects or lets it be moved, so that hiding an element or updating
    //one that can't be moved (or isn't edited) doesn't hide or lock the points of the one that is
    static void ApplySharedPointState(TerrainPoint p) {
        if (p == null || p.IsDeleted()) return;
        bool any = false, active = false, selected = false, moveable = false, highlighted = false;
        foreach (var link in p.GetLinks()) {
            if (link is not CustomElement e || e.deleted) continue;
            any = true;
            active |= e.active;
            selected |= e.selected;
            moveable |= e.active && e.moveable && !e.derivedFlag;
            highlighted |= e.active && e.moveable && !e.derivedFlag && e.placementPoint == p;
        }
        p.SetPlacementHighlighted(highlighted);
        if (!any) return; //only used by other kinds of elements, which set it
        p.ApplyCustomElementState(active, selected, moveable);
    }

    TerrainPoint CreatePoint(CityElements.Types.CustomElementPointNode def, Vector3 position) {
        return CreatePoint(position);
    }

    //the default content of a new element, around the point where it is created
    public void CreateDefault(Vector3 origin) {
        History.UndoManager.Touch(this);
        var type = GetCustomElementType();
        if (type == null) return;
        points = (PointGroup)PointTree.CreateDefault(PointTree.RootDef(type.typeData.settings.controlPoints), CreatePoint, origin);
        var meshSettings = type.typeData.settings.mesh;
        if (meshSettings?.defaultVertices != null) {
            foreach (var v in meshSettings.defaultVertices) meshVertices.Add(CreatePoint(origin + PointTree.ToVector(v, Vector3.Zero)));
            if (meshSettings.defaultTriangles != null) triangles.AddRange(meshSettings.defaultTriangles);
            foreach (var _ in meshVertices) uvs.Add(Vector2.Zero);
        }
        structureVersion++;
    }

    public List<(string path, PointLeaf leaf)> GetLeaves() {
        return PointTree.Leaves(points);
    }

    //every point of the element: the control points, then the vertices of the mesh
    public List<TerrainPoint> GetAllPoints() {
        var res = new List<TerrainPoint>();
        foreach (var (_, leaf) in GetLeaves()) {
            if (leaf.point != null && !res.Contains(leaf.point)) res.Add(leaf.point);
        }
        foreach (var v in meshVertices) {
            if (v != null && !res.Contains(v)) res.Add(v);
        }
        return res;
    }

    public bool HasPoint(TerrainPoint point) {
        return meshVertices.Contains(point) || PointTree.PathOf(points, point) != null;
    }

    //the center of the points (where new points go when nothing else tells)
    public Vector3 GetCenter() {
        var all = GetAllPoints();
        var res = Vector3.Zero;
        foreach (var p in all) res += p.GetPoint();
        return all.Count > 0 ? res / all.Count : res;
    }

    //the point is replaced with another one (e.g. shared with another element), every use of it in the element
    public void ReplacePoint(TerrainPoint oldPoint, TerrainPoint newPoint) {
        if (oldPoint == newPoint || oldPoint == null || newPoint == null) return;
        History.UndoManager.Touch(this);
        var used = false;
        foreach (var (_, leaf) in GetLeaves()) {
            if (leaf.point == oldPoint) {
                leaf.point = newPoint;
                used = true;
            }
        }
        for (int i = 0; i < meshVertices.Count; i++) {
            if (meshVertices[i] == oldPoint) {
                meshVertices[i] = newPoint;
                used = true;
            }
        }
        if (!used) return;
        if (!newPoint.IsLinkedTo(this)) newPoint.AddLink(this);
        oldPoint.RemoveLink(this);
        ApplyPointState(newPoint);
        structureVersion++;
    }

    //a new element of the array at the index (and of the other instances of a uniform array), false if the array can't grow
    public virtual bool InsertArrayElement(string arrayPath, int index, bool userEdit = true) {
        var list = PointTree.Find(points, arrayPath) as PointList;
        if (list == null || index < 0 || index > list.items.Count) return false;
        var a = list.def.array;
        if (userEdit && !a.editable) return false;
        if (a.max > 0 && list.items.Count >= a.max) return false;
        History.UndoManager.Touch(this);
        var origin = GetCenter();
        foreach (var instance in PointTree.UniformInstances(points, arrayPath)) {
            var i = System.Math.Min(index, instance.items.Count);
            instance.items.Insert(i, PointTree.CreateArrayElement(instance, i, CreatePoint, origin));
        }
        structureVersion++;
        return true;
    }

    //removes the element of the array at the index (and of the other instances of a uniform array), false if it can't
    public virtual bool RemoveArrayElement(string arrayPath, int index, bool userEdit = true) {
        var list = PointTree.Find(points, arrayPath) as PointList;
        if (list == null || index < 0 || index >= list.items.Count) return false;
        var a = list.def.array;
        if (userEdit && !a.editable) return false;
        if (list.items.Count <= a.min) return false;
        History.UndoManager.Touch(this);
        foreach (var instance in PointTree.UniformInstances(points, arrayPath)) {
            if (index >= instance.items.Count) continue;
            var removed = instance.items[index];
            instance.items.RemoveAt(index);
            foreach (var (_, leaf) in PointTree.Leaves(removed)) ReleasePoint(leaf.point);
        }
        structureVersion++;
        return true;
    }

    //the point is not used by the element any more (deleted if no other element uses it)
    void ReleasePoint(TerrainPoint point) {
        if (point == null || point.IsDeleted() || HasPoint(point)) return;
        if (placementPoint == point) SetPlacementPoint(null);
        point.Select(false);
        point.RemoveLink(this);
        ApplySharedPointState(point); //as the other elements using it want it
    }

    //sets the mesh (the vertices must be points of the city, they get linked to the element)
    public void SetMesh(List<TerrainPoint> vertices, List<int> newTriangles, List<Vector2> newUVs) {
        History.UndoManager.Touch(this);
        var old = new List<TerrainPoint>(meshVertices);
        meshVertices.Clear();
        meshVertices.AddRange(vertices);
        foreach (var v in vertices) {
            if (!old.Contains(v) && PointTree.PathOf(points, v) == null && !v.IsLinkedTo(this)) v.AddLink(this);
        }
        foreach (var v in old) ReleasePoint(v);
        triangles.Clear();
        triangles.AddRange(newTriangles);
        uvs.Clear();
        if (newUVs != null) uvs.AddRange(newUVs);
        while (uvs.Count < meshVertices.Count) uvs.Add(Vector2.Zero);
        if (uvs.Count > meshVertices.Count) uvs.RemoveRange(meshVertices.Count, uvs.Count - meshVertices.Count);
        structureVersion++;
    }

    public void SetUV(int vertex, Vector2 uv) {
        if (vertex < 0 || vertex >= uvs.Count) return;
        History.UndoManager.Touch(this);
        uvs[vertex] = uv;
        structureVersion++;
    }

    //sets the points of the tree (used by the decoder, the converters and undo/redo): the points are linked to the element
    public void SetPoints(PointGroup newPoints) {
        var old = GetAllPoints();
        points = newPoints;
        foreach (var p in GetAllPoints()) {
            if (!old.Contains(p) && !p.IsLinkedTo(this)) p.AddLink(this);
            ApplyPointState(p);
        }
        foreach (var p in old) ReleasePoint(p);
        if (History.UndoManager.IsLoading && Settings?.spline != null) {
            foreach (var p in GetAllPoints()) splineProjectedPoints[p] = p.GetPoint();
        }
        structureVersion++;
    }

    // IGroundable

    public bool IsProjectedToGround() {
        return false;
    }

    //a point deleted by the user: the element of the outermost editable array containing it (e.g. the section of a lane
    //point), or a vertex of the mesh with its triangles
    public bool RemovePoint(object point) {
        if (point is not TerrainPoint tp) return false;
        var path = PointTree.PathOf(points, tp);
        if (path != null) {
            var arrayElement = PointTree.FindArrayElement(points, path, true, true);
            return arrayElement.HasValue && RemoveArrayElement(arrayElement.Value.arrayPath, arrayElement.Value.index);
        }
        var vi = meshVertices.IndexOf(tp);
        if (vi < 0) return false;
        History.UndoManager.Touch(this);
        var newTriangles = new List<int>();
        for (int i = 0; i + 2 < triangles.Count; i += 3) {
            if (triangles[i] == vi || triangles[i + 1] == vi || triangles[i + 2] == vi) continue;
            for (int j = 0; j < 3; j++) newTriangles.Add(triangles[i + j] > vi ? triangles[i + j] - 1 : triangles[i + j]);
        }
        meshVertices.RemoveAt(vi);
        if (vi < uvs.Count) uvs.RemoveAt(vi);
        triangles.Clear();
        triangles.AddRange(newTriangles);
        structureVersion++;
        //the caller unlinks the point
        return true;
    }

    // Containers

    public bool CanBeIn(CustomElement newContainer) {
        if (newContainer == null) return true;
        if (newContainer == this) return false;
        var type = newContainer.GetCustomElementType();
        if (type == null || !type.CanContain(TypeName)) return false;
        //no cycles
        for (var c = newContainer; c != null; c = c.container) {
            if (c == this) return false;
        }
        return true;
    }

    public bool SetContainer(CustomElement newContainer) {
        if (!CanBeIn(newContainer)) return false;
        if (container == newContainer) return true;
        History.UndoManager.Touch(this);
        container = newContainer;
        return true;
    }

    // Visibility and selection

    //Other users of a shared terrain point can change its handle directly (e.g. a terrain patch).
    internal void InvalidatePointState() {
        pointStateVersion = -1;
    }

    //Transient editor state: only the old and new placement references need their shared handle state refreshed.
    public void SetPlacementPoint(TerrainPoint point) {
        if (placementPoint == point) return;
        var previous = placementPoint;
        placementPoint = point;
        ApplySharedPointState(previous);
        ApplySharedPointState(point);
    }

    public void SetActive(bool active, bool moveable) {
        if (deleted) return;
        if (!active || !moveable) SetPlacementPoint(null);
        //Global editor resets visit every element, often several times. Only changed elements need to walk their
        //shared points and update the handles. A changed structure still needs its new points initialized.
        if (this.active == active && this.moveable == moveable && pointStateVersion == structureVersion) return;
        this.active = active;
        this.moveable = moveable;
        foreach (var point in GetAllPoints()) ApplySharedPointState(point);
        pointStateVersion = structureVersion;
        generator.ShowLines(active);
        if (!active || !moveable) ShowMoveHandle(false, Vector3.Zero);
    }

    // Move handle

    //the handle of a container being edited (shown by its editor, at the center of the container and its contents): moved
    //with the gizmo or the transform panel, the update moves the container with its contents by the same offset (see
    //ElementManager.MoveByHandles). A handle already shown stays where it is
    public void ShowMoveHandle(bool show, Vector3 position) {
        if (deleted) return;
        if (GO.IsNull(moveHandle)) {
            if (!show) return;
            var obj = (StaticBody3D)GameEngine.Resources.InstantiateObject("Handle", this);
            moveHandle = GS.Cast<Handle>(obj);
            moveHandle.SetScale(GameEngine.Vector3.one * 2.0f);
            moveHandle.SetColor(GameEngine.Color.cyan);
            GO.SetLayer(obj, 8); //moveable
            GO.SetEnabled(obj, false);
        }
        if (show == IsMoveHandleShown()) return;
        if (show) PlaceMoveHandle(position);
        GO.SetEnabled(moveHandle, show);
    }

    public bool IsMoveHandleShown() {
        return !GO.IsNull(moveHandle) && GO.GetEnabled(moveHandle);
    }

    public Handle GetMoveHandle() {
        return IsMoveHandleShown() ? moveHandle : null;
    }

    public void PlaceMoveHandle(Vector3 position) {
        if (GO.IsNull(moveHandle)) return;
        GO.SetPosition(moveHandle, position);
        moveHandlePosition = position;
    }

    //the offset the handle was moved by since the last update, null if it wasn't moved
    public Vector3? TakeMoveHandleOffset() {
        if (deleted || !IsMoveHandleShown()) return null;
        var position = GO.GetPosition(moveHandle);
        if (GeometryHelper.AreVectorsEqual(position, moveHandlePosition)) return null;
        var offset = position - moveHandlePosition;
        moveHandlePosition = position;
        return offset;
    }

    public void Select(bool selected) {
        if (deleted) return;
        if (this.selected == selected && pointStateVersion == structureVersion) return;
        this.selected = selected;
        foreach (var point in GetAllPoints()) ApplySharedPointState(point);
        pointStateVersion = structureVersion;
    }

    public bool IsActive() {
        return active;
    }

    public bool IsSelected() {
        return selected;
    }

    // Deletion

    public bool IsDeleted() {
        return deleted;
    }

    public void Delete() {
        if (deleted) return;
        SetPlacementPoint(null);
        History.UndoManager.Touch(this);
        var all = GetAllPoints();
        foreach (var point in all) point.RemoveLink(this);
        points = new PointGroup(points.def);
        meshVertices.Clear();
        GO.Destroy(this);
        deleted = true;
        foreach (var point in all) ApplySharedPointState(point);
    }

    //removes the element without deleting its points (used by undo/redo, which deletes them explicitly)
    public void RemoveForUndo() {
        SetPlacementPoint(null);
        foreach (var point in GetAllPoints()) point.RemoveLinkSilently(this);
        points = new PointGroup(points.def);
        meshVertices.Clear();
        GO.Destroy(this);
        deleted = true;
    }

    //sets everything from a snapshot (used by undo/redo): the constraints don't run on the restored points
    public void RestoreFromSnapshot(ObjectState newState, PointGroup newPoints, List<TerrainPoint> vertices, List<int> newTriangles,
        List<Vector2> newUVs, CustomElement newContainer, object newSource) {

        SetPlacementPoint(null);
        var oldPoints = new HashSet<TerrainPoint>(GetAllPoints());
        state = newState;
        points = newPoints;
        meshVertices.Clear();
        meshVertices.AddRange(vertices);
        var newAll = new HashSet<TerrainPoint>(GetAllPoints());
        foreach (var p in newAll) {
            if (!oldPoints.Contains(p) && !p.IsLinkedTo(this)) p.AddLink(this);
            ApplyPointState(p);
        }
        foreach (var p in oldPoints) {
            if (!newAll.Contains(p)) p.RemoveLinkSilently(this);
        }
        triangles.Clear();
        triangles.AddRange(newTriangles);
        uvs.Clear();
        uvs.AddRange(newUVs);
        while (uvs.Count < meshVertices.Count) uvs.Add(Vector2.Zero);
        container = newContainer;
        source = newSource;
        constraintPositions = null;
        derivedSeen = false;
        structureVersion++;
    }

    // Update

    void ReloadType() {
        var type = GetCustomElementType();
        if (type?.typeData.settings.spline is CityElements.Types.CustomElementSplineSettings spline) {
            if (!state.properties.ContainsKey("curveType")) state.SetInt("curveType", spline.defaultCurveType);
            if (!state.properties.ContainsKey("segments")) state.SetInt("segments", spline.segments);
            if (!state.properties.ContainsKey("segmentsPer100m")) state.SetBool("segmentsPer100m", spline.segmentsPer100m);
            if (System.Array.IndexOf(spline.curveTypes, 1) >= 0 && !state.properties.ContainsKey("hermiteTension")) state.SetFloat("hermiteTension", 0.5f);
        }
        if (type != curType) {
            variableContainer = type?.variableContainer.GetClone();
            curType = type;
        }
        if (type != null && type.splineType == null) type.FillInitialVariables(variableContainer, state);
    }

    //the points follow their anchors (removing the points deleted meanwhile)
    public void RefreshPoints() {
        if (deleted) return;
        foreach (var (_, leaf) in GetUpdateLeaves()) {
            if (leaf.point != null && leaf.point.IsDeleted()) {
                leaf.point = CreatePoint(leaf.point.GetPointSafe());
                structureVersion++;
            }
        }
        for (int i = meshVertices.Count - 1; i >= 0; i--) {
            if (meshVertices[i].IsDeleted()) {
                meshVertices[i] = CreatePoint(meshVertices[i].GetPointSafe());
                structureVersion++;
            }
        }
        foreach (var p in GetUpdatePoints()) p.UpdatePosition();
        if (Settings?.spline?.projectPoints == true) {
            //Projection is a placement/move rule. Unrelated edits must not reproject saved points onto transient
            //colliders (e.g. while a road is replaced); undo/redo restores the recorded heights exactly.
            var restored = splineProjectionUndoVersion != History.UndoManager.AppliedVersion;
            splineProjectionUndoVersion = History.UndoManager.AppliedVersion;
            foreach (var p in GetUpdatePoints()) {
                var position = p.GetPoint();
                if (!restored && !History.UndoManager.IsLoading && GO.IsNull(p.anchor) &&
                    (!splineProjectedPoints.TryGetValue(p, out var old) || !GeometryHelper.AreVectorsEqual(old, position))) {
                    //with the rule of the sections, if they're projected too (as the clicks placing the points)
                    var projected = Settings.spline.projectSections
                        ? GeometryHelper.AutoProjectOnSurface(position, CityGroundHelper.ExcludeHiddenTerrain(Settings.spline.projectionMask), generator)
                        : GeometryHelper.ProjectOnSurface(position, Settings.spline.projectionMask, generator);
                    if (!GeometryHelper.AreVectorsEqual(projected, position)) {
                        History.UndoManager.Touch(p);
                        GO.SetPosition(p.GetObject(), projected);
                    }
                }
                splineProjectedPoints[p] = p.GetPoint();
            }
            if (splineProjectedPoints.Count > GetUpdatePoints().Count) {
                var used = new HashSet<TerrainPoint>(GetUpdatePoints());
                foreach (var p in new List<TerrainPoint>(splineProjectedPoints.Keys)) if (!used.Contains(p)) splineProjectedPoints.Remove(p);
            }
        }
    }

    //runs the constraints of the type if points moved since they last ran; returns true if they moved other points
    public bool ApplyConstraints() {
        if (deleted) return false;
        var type = GetCustomElementType();
        if (type?.constraintsFunction == null) {
            constraintPositions = null;
            return false;
        }
        var all = GetUpdatePoints();
        if (constraintPositions == null) {
            RecordConstraintPositions(all);
            return false;
        }
        var moved = new HashSet<TerrainPoint>();
        foreach (var p in all) {
            if (!constraintPositions.TryGetValue(p, out var old) || !GeometryHelper.AreVectorsEqual(old, p.GetPoint())) moved.Add(p);
        }
        if (moved.Count == 0 || type?.constraintsFunction == null) {
            RecordConstraintPositions(all);
            return false;
        }
        ReloadType();
        var ctx = CreateScriptContext(moved);
        var res = type.scriptHost.Call(type.constraintsFunction, RC.Scripting.LuaVariableView.Get(variableContainer), ctx, TypeName + " (constraints)");
        ConstraintsResult result;
        try {
            result = CustomElementScripting.ReadConstraints(res, points, meshVertices.Count);
        } catch (System.Exception e) {
            type.scriptHost.ReportError(TypeName + " (constraints)", e.Message);
            RecordConstraintPositions(all);
            return false;
        }
        var movedOthers = false;
        foreach (var (path, pos) in result.points) {
            var leaf = PointTree.Find(points, path) as PointLeaf;
            if (leaf?.point != null && MovePoint(leaf.point, pos)) movedOthers = true;
        }
        foreach (var (i, pos) in result.vertices) {
            if (i < meshVertices.Count && MovePoint(meshVertices[i], pos)) movedOthers = true;
        }
        RecordConstraintPositions(all);
        return movedOthers;
    }

    //a point moved by the constraints (not the anchored ones, which follow their anchor)
    static bool MovePoint(TerrainPoint point, Vector3 position) {
        if (point.IsDeleted() || GeometryHelper.AreVectorsEqual(point.GetPoint(), position)) return false;
        if (!GO.IsNull(point.anchor) && !point.anchor.IsDeleted() && !point.anchor.IsMoveable()) return false;
        History.UndoManager.Touch(point);
        GO.SetPosition(point.GetObject(), position);
        return true;
    }

    void RecordConstraintPositions(List<TerrainPoint> all) {
        constraintPositions = new Dictionary<TerrainPoint, Vector3>();
        foreach (var p in all) constraintPositions[p] = p.GetPoint();
    }

    // Derived points

    //the points are computed by the derivedPoints script of the type (its flag is on)
    public bool IsDerived => Settings?.derivedPoints != null && state.Bool(Settings.derivedPoints.flag);

    //the other custom elements replacing the same procedural element, in the order of their ids
    public List<CustomElement> GetSiblings(List<CustomElement> elements) {
        var res = new List<CustomElement>();
        if (source == null) return res;
        foreach (var other in elements) {
            if (other != this && !other.IsDeleted() && other.source == source) res.Add(other);
        }
        res.Sort((a, b) => History.ElementRegistry.GetId(a).CompareTo(History.ElementRegistry.GetId(b)));
        return res;
    }

    //what the derived points depend on: the points of the element and of its siblings (and how many each one has)
    List<float> GetDerivedInputs(List<CustomElement> siblings) {
        var res = new List<float>();
        void Add(CustomElement element) {
            var all = element.GetUpdatePoints();
            res.Add(History.ElementRegistry.GetId(element));
            res.Add(all.Count);
            foreach (var p in all) {
                var v = p.GetPoint();
                res.Add(v.X);
                res.Add(v.Y);
                res.Add(v.Z);
            }
        }
        Add(this);
        foreach (var sibling in siblings) Add(sibling);
        return res;
    }

    static bool SameInputs(List<float> a, List<float> b) {
        if (a == null || b == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) {
            if (Mathf.Abs(a[i] - b[i]) > 1e-5f) return false;
        }
        return true;
    }

    //computes the points with the derivedPoints script of the type when its flag is turned on or its inputs (the points of
    //the element and of its siblings) change, in the undo step of the change. The first time (an element loaded,
    //converted or restored by undo/redo) the inputs are only recorded, so the points stay as they were (e.g. an export is
    //the same until something is edited). Returns true if the points changed
    public bool UpdateDerivedPoints(List<CustomElement> elements) {
        if (deleted) return false;
        var type = GetCustomElementType();
        var settings = type?.typeData.settings.derivedPoints;
        if (settings == null || type.derivedFunction == null) return false;
        var flag = state.Bool(settings.flag);
        var firstTime = !derivedSeen || derivedAppliedVersion != History.UndoManager.AppliedVersion;
        var wasOn = derivedFlag && !firstTime;
        derivedSeen = true;
        derivedAppliedVersion = History.UndoManager.AppliedVersion;
        if (flag != derivedFlag) {
            derivedFlag = flag;
            foreach (var p in GetUpdatePoints()) ApplyPointState(p);
        }
        if (!flag) {
            derivedInputs = null;
            return false;
        }
        var siblings = GetSiblings(elements);
        var inputs = GetDerivedInputs(siblings);
        if (firstTime || (wasOn && SameInputs(inputs, derivedInputs))) {
            derivedInputs = inputs;
            return false;
        }
        var changed = false;
        History.UndoManager.RecordDerived(() => changed = ComputeDerivedPoints(type, siblings));
        derivedInputs = GetDerivedInputs(siblings);
        return changed;
    }

    bool ComputeDerivedPoints(CityElements.Types.Runtime.CustomElementType type, List<CustomElement> siblings) {
        ReloadType();
        var ctx = CreateScriptContext(null);
        var siblingsTable = new LuaTable();
        for (int i = 0; i < siblings.Count; i++) siblingsTable[i + 1] = ElementTable(siblings[i]);
        ctx["siblings"] = siblingsTable;
        var name = TypeName + " (derived points)";
        var res = type.scriptHost.Call(type.derivedFunction, RC.Scripting.LuaVariableView.Get(variableContainer), ctx, name);
        if (res.Type == LuaValueType.Nil) return false; //nothing to compute from (or the script failed, reported)
        PointGroup tree;
        Dictionary<PointLeaf, Vector3> positions;
        try {
            if (!res.TryRead<LuaTable>(out var t)) throw new System.Exception("the result is not a table");
            (tree, positions) = CustomElementScripting.ReadPointTree(t["points"], points.def);
        } catch (System.Exception e) {
            type.scriptHost.ReportError(name, e.Message);
            return false;
        }
        return ApplyDerivedPoints(tree, positions, siblings);
    }

    //the points of the tree: a point of a sibling at the position (shared, as after the conversion), the point of the
    //element at the same path if no other element uses it (moved there), or a new point
    bool ApplyDerivedPoints(PointGroup tree, Dictionary<PointLeaf, Vector3> positions, List<CustomElement> siblings) {
        History.UndoManager.Touch(this);
        var siblingPoints = new HashSet<TerrainPoint>();
        foreach (var sibling in siblings) siblingPoints.UnionWith(sibling.GetUpdatePoints());
        var used = new HashSet<TerrainPoint>();
        var newLeaves = PointTree.Leaves(tree);
        var changed = newLeaves.Count != GetUpdateLeaves().Count;
        foreach (var (path, leaf) in newLeaves) {
            var position = positions[leaf];
            var old = (PointTree.Find(points, path) as PointLeaf)?.point;
            TerrainPoint point = null;
            foreach (var p in siblingPoints) {
                if (!p.IsDeleted() && p.GetPoint().DistanceTo(position) < Operations.CustomElements.WeldDistance) {
                    point = p;
                    break;
                }
            }
            if (point == null && old != null && !old.IsDeleted() && !used.Contains(old) && !siblingPoints.Contains(old) && old.GetLinks().Count == 1) {
                point = old;
                if (!GeometryHelper.AreVectorsEqual(old.GetPoint(), position) || !GO.IsNull(old.anchor)) {
                    History.UndoManager.Touch(old);
                    old.anchor = null;
                    GO.SetPosition(old.GetObject(), position);
                    changed = true;
                }
            }
            if (point == null) point = CreatePoint(position);
            if (point != old) changed = true;
            if (leaf.def.rotatable && old != null && !old.IsDeleted()) point.SetRotation(old.GetRotation());
            leaf.point = point;
            used.Add(point);
        }
        if (changed) SetPoints(tree);
        return changed;
    }

    //the custom elements referenced by the parameters (their points are given to the scripts, see CreateScriptContext)
    public List<(string name, CustomElement element)> GetReferencedElements() {
        var res = new List<(string, CustomElement)>();
        var type = GetCustomElementType();
        if (type == null) return res;
        foreach (var name in type.GetReferenceParameters()) {
            var reference = state.State(name);
            if (reference == null || reference.Str("kind", "") != "customElement") continue;
            var target = History.ElementRegistry.Get(reference.Int("id", -1)) as CustomElement;
            if (target != null && target != this && !target.IsDeleted()) res.Add((name, target));
        }
        return res;
    }

    //the positions the geometry depends on: the points, then the points of the referenced elements
    List<Vector3> GetInputPositions(List<TerrainPoint> all) {
        var res = new List<Vector3>();
        foreach (var p in all) res.Add(p.GetPoint());
        foreach (var (_, element) in GetReferencedElements()) {
            foreach (var p in element.GetUpdatePoints()) res.Add(p.GetPoint());
        }
        return res;
    }

    bool DidChange(List<TerrainPoint> all) {
        if (state.HasChanged() || structureVersion != oldStructureVersion || GetCustomElementType() != curType) return true;
        var positions = GetInputPositions(all);
        if (positions.Count != oldPositions.Count) return true;
        for (int i = 0; i < positions.Count; i++) {
            if (!GeometryHelper.AreVectorsEqual(positions[i], oldPositions[i])) return true;
        }
        //the rotations of the rotatable points
        foreach (var (_, leaf) in GetUpdateLeaves()) {
            if (leaf.def.rotatable && leaf.point != null && leaf.point.RotationChanged()) return true;
        }
        return false;
    }

    internal bool NeedsUpdate() {
        return !deleted && (source != updatedSource || container != updatedContainer || DidChange(GetUpdatePoints()));
    }

    internal void InvalidateSplineSurface() {
        if (Settings?.spline?.projectSections == true) structureVersion++;
    }

    //rebuilds the geometry if anything changed, returns 1 if it did
    public int UpdateGeometry() {
        if (deleted) return 0;
        var all = GetUpdatePoints();
        if (!DidChange(all)) return 0;
        ReloadType();
        Rebuild();
        FinishGeometryUpdate(all);
        return 1;
    }

    void FinishGeometryUpdate(List<TerrainPoint> all) {
        foreach (var p in all) p.UpdateOlds();
        oldPositions.Clear();
        oldPositions.AddRange(GetInputPositions(all));
        state.FlagAsUnchanged();
        oldStructureVersion = structureVersion;
    }

    //captured on the main thread after constraints and derived points; the worker only sees Lua tables and numbers
    internal GeometryUpdate PrepareGeometryUpdate() {
        var all = GetUpdatePoints();
        if (deleted || !DidChange(all)) return null;
        ReloadType();
        return new GeometryUpdate {
            element = this, all = all, type = curType,
            variables = curType?.geometryFunction != null ? RC.Scripting.LuaVariableView.Get(variableContainer) : LuaValue.Nil,
            context = curType?.geometryFunction != null ? CreateScriptContext(null) : LuaValue.Nil
        };
    }

    internal sealed class GeometryUpdate {
        internal CustomElement element;
        internal List<TerrainPoint> all;
        internal CityElements.Types.Runtime.CustomElementType type;
        internal LuaValue variables, context;
        internal CustomElements.GeometryResult geometry;
        internal bool retryOnMainThread;
        internal readonly List<string> prints = new List<string>();
        internal readonly List<(string definition, string message)> errors = new List<(string, string)>();

        internal void Calculate(RC.Scripting.LuaScriptHost host) {
            host.BeginWork();
            var name = type.name + " (geometry)";
            LuaFunction function;
            try {
                function = host.Resolve(type.typeData.settings.geometry);
            } catch (System.Exception) {
                retryOnMainThread = true; //the original host may still have a working module if the files changed meanwhile
                return;
            }
            try {
                var result = host.Call(function, variables, context, name);
                geometry = CustomElementScripting.ReadGeometry(result);
            } catch (System.Exception e) {
                host.ReportError(name, e.Message);
            }
            retryOnMainThread = host.needsMainThread;
            prints.AddRange(host.prints);
            errors.AddRange(host.errors);
        }

        internal void Apply() {
            if (retryOnMainThread || type?.geometryFunction == null) {
                element.Rebuild();
            } else {
                foreach (var message in prints) GameEngine.Print(message);
                foreach (var error in errors) type.scriptHost.ReportError(error.definition, error.message);
                try {
                    element.scriptUVs = geometry?.uvs;
                    if (geometry == null) element.generator.Clear();
                    else element.generator.Rebuild(geometry, element.state, type.typeData.settings.propsContainers);
                } catch (System.Exception e) {
                    type.scriptHost.ReportError(element.TypeName + " (geometry)", e.Message);
                    element.generator.Clear();
                }
            }
            element.FinishGeometryUpdate(all);
        }
    }

    protected virtual void Rebuild() {
        var type = curType;
        if (type?.splineType != null) {
            try {
                var geometry = CustomElementSplineGeometry.Build(this, out splineData);
                generator.Rebuild(geometry, state, type.typeData.settings.propsContainers);
            } catch (System.Exception e) {
                splineData = new CustomElementSplineGeometry.Data();
                generator.Clear();
                GameEngine.LogError("Spline type '" + TypeName + "': " + e.Message);
            }
            return;
        }
        splineData = null;
        if (type?.geometryFunction == null) {
            generator.Clear();
            scriptUVs = null;
            return;
        }
        var ctx = CreateScriptContext(null);
        var res = type.scriptHost.Call(type.geometryFunction, RC.Scripting.LuaVariableView.Get(variableContainer), ctx, TypeName + " (geometry)");
        try {
            var geometry = CustomElementScripting.ReadGeometry(res);
            scriptUVs = geometry.uvs;
            generator.Rebuild(geometry, state, type.typeData.settings.propsContainers);
        } catch (System.Exception e) {
            type.scriptHost.ReportError(TypeName + " (geometry)", e.Message);
            generator.Clear();
        }
    }

    // Scripts

    //the second argument of the scripts: the points, the mesh and (for the constraints) what moved
    LuaTable CreateScriptContext(HashSet<TerrainPoint> moved) {
        var ctx = new LuaTable();
        ctx["typeName"] = TypeName ?? "";
        ctx["name"] = GO.GetName(this);
        ctx["points"] = CustomElementScripting.PointsTable(points, leaf => RC.Scripting.LuaVectors.ToLua(leaf.point.GetPoint()));
        ctx["rotations"] = CustomElementScripting.PointsTable(points, leaf => leaf.def.rotatable ? RC.Scripting.LuaVectors.ToLua(leaf.point.GetRotation()) : LuaValue.Nil);
        var vertices = new List<Vector3>();
        foreach (var v in meshVertices) vertices.Add(v.GetPoint());
        ctx["vertices"] = CustomElementScripting.List(vertices);
        ctx["triangles"] = CustomElementScripting.List(triangles);
        ctx["uvs"] = CustomElementScripting.List(uvs);
        //the referenced custom elements, by parameter: {typeName, name, points, vertices, triangles}
        var references = new LuaTable();
        foreach (var (name, element) in GetReferencedElements()) references[name] = ElementTable(element);
        ctx["references"] = references;
        if (moved != null) {
            var movedTable = new LuaTable();
            movedTable["points"] = CustomElementScripting.PointsTable(points, leaf => moved.Contains(leaf.point) ? new LuaValue(true) : LuaValue.Nil);
            var movedVertices = new LuaTable();
            for (int i = 0; i < meshVertices.Count; i++) {
                if (moved.Contains(meshVertices[i])) movedVertices[i + 1] = true;
            }
            movedTable["vertices"] = movedVertices;
            ctx["moved"] = movedTable;
        }
        return ctx;
    }

    //another element given to the scripts (a reference or a sibling): {typeName, name, points, vertices, triangles}
    static LuaTable ElementTable(CustomElement element) {
        var r = new LuaTable();
        r["typeName"] = element.TypeName ?? "";
        r["name"] = GO.GetName(element);
        r["points"] = CustomElementScripting.PointsTable(element.points, leaf => RC.Scripting.LuaVectors.ToLua(leaf.point.GetPoint()));
        var vertices = new List<Vector3>();
        foreach (var v in element.meshVertices) vertices.Add(v.GetPoint());
        r["vertices"] = CustomElementScripting.List(vertices);
        r["triangles"] = CustomElementScripting.List(element.triangles);
        return r;
    }
}

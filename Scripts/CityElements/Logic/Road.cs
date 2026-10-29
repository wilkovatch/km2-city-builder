using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using PhysicsGameObject = Godot.StaticBody3D;
using States;
using RC = RuntimeCalculator;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using TerrainModifier;

public partial class Road : ParametricObjectContainer<CityElements.Types.RoadType>, IObjectWithStateAndRuntimeType, RC.Scripting.IScriptContextProvider {
    public bool forceRemesh = false;
    public List<Vector3> curvePoints = new List<Vector3>();
    public RoadGenerator generator;

    public List<PhysicsGameObject> points = new List<PhysicsGameObject>();
    public Intersection startIntersection = null, endIntersection = null;
    List<Vector3> cps = new List<Vector3>();
    public ObjectState state, instanceState, runtimeState;
    Vector3 start, end;
    RC.VariableContainer variableContainer;

    List<Vector2> rots = new List<Vector2>(); //of all the points (start, control points, end)
    (Vector3? startTangent, Vector3? endTangent, Vector2 startRotation, Vector2 endRotation) seamlessEnds, old_seamlessEnds;
    List<Vector3> old_cps = new List<Vector3>();
    List<Vector2> old_rots = new List<Vector2>();
    List<Vector3> old_intersections = new List<Vector3>();
    Vector3 old_start, old_end;
    CityElements.Types.Runtime.RoadType curType;

    public TerrainAnchorLineManager anchorManager;
    public RoadSplitLine splitLine;

    public bool deleted = false;
    //replaced by low-level elements: its geometry is hidden, can't be selected and is not exported, but it stays for the
    //elements connected to it (its intersections, the terrain points on its anchors)
    public bool ghost = false;
    public bool cityPropertiesInjected = false; //the parameters injected by the city properties are in runtimeState
    bool activeSelf = true;
    RoadScriptContext scriptContext;
    readonly Dictionary<PhysicsGameObject, Vector3> projectedPoints = new Dictionary<PhysicsGameObject, Vector3>();
    ulong projectedTerrainVersion = ulong.MaxValue;

    public void Initialize() {
        state = PresetManager.GetPreset("road", 0);
        instanceState = new ObjectState();
        runtimeState = new ObjectState();
        var generatorObj = GS.Create<RoadGenerator>(null, this);
        generator = GS.Cast<RoadGenerator>(generatorObj);
        generator.Initialize(state);
        generator.subGen.owner = this; //the scripts of the definitions get the road as context
        anchorManager = new TerrainAnchorLineManager(this);
        splitLine = RoadSplitLine.Create(this);
    }

    public ObjectState GetState() {
        return state;
    }

    public void SetState(ObjectState newState) {
        History.UndoManager.Touch(this);
        state = newState;
        resetObjectsPositions = true;
    }

    public void SetRailState(ObjectState newState) {
        state.SetContainer(newState, "rail");
    }

    public void SetPointsMoveable(bool enabled) {
        foreach (var point in points) {
            GO.SetLayer(point, enabled ? 8 : 0);
        }
        if (startIntersection != null) GO.SetLayer(startIntersection.point, enabled ? 8 : 0);
        if (endIntersection != null) GO.SetLayer(endIntersection.point, enabled ? 8 : 0);
    }

    void ResetIntersectionPoint(PhysicsGameObject point) {
        GO.SetLayer(point, 8);
        GS.Cast<Handle>(point).SetScale(GameEngine.Vector3.one);
        GS.Cast<Handle>(point).SetColor(GameEngine.Color.green);
    }

    void ShowIntersectionPoint(PhysicsGameObject point) {
        GO.SetEnabled(point, true);
        GO.SetLayer(point, 9);
        GS.Cast<Handle>(point).SetScale(GameEngine.Vector3.one * 3);
        GS.Cast<Handle>(point).SetColor(GameEngine.Color.cyan);
    }

    //the end the new points are added to (the first point while the road is extended backwards)
    public bool markFirstPoint = false;

    void MarkEditedEnd() {
        ResetColors();
        if (points.Count > 0) {
            GS.Cast<Handle>(points[markFirstPoint ? 0 : points.Count - 1]).SetColor(GameEngine.Color.yellow);
        }
    }

    void ResetColors() {
        foreach (var point in points) {
            GS.Cast<Handle>(point).SetColor(GameEngine.Color.green);
        }
    }

    public void SetActive(bool active, bool activeIntersection = false) {
        activeSelf = active;
        generator.lr.enabled = active && points.Count > 0;
        GO.SetEnabled(generator.lanesRenderersContainer, active);
        if (points.Count > 0) {
            ResetIntersectionPoint(points[0]);
            ResetIntersectionPoint(points[points.Count - 1]);
        }
        foreach (var point in points) {
            GO.SetEnabled(point, active);
        }
        if (points.Count > 0 && activeIntersection && !ghost) {
            if (startIntersection == null) ShowIntersectionPoint(points[0]);
            if (endIntersection == null) ShowIntersectionPoint(points[points.Count - 1]);
        }
        //the other roads can be connected to the middle of this one while their points are placed
        splitLine.SetActive(!ghost && !active && activeIntersection && points.Count >= 2);
        if (active) MarkEditedEnd();

        //while the road is edited, its ends on the intersections are selected and moved instead of the intersections
        //(which follow them): the handles of the intersections are hidden, the ones of the ends are as big
        if (startIntersection != null) startIntersection.SetActive(false, true);
        if (endIntersection != null) endIntersection.SetActive(false, true, true);
        EnlargeEndsOnIntersections();
    }

    //while the road is edited (also after an undo restored its points)
    void EnlargeEndsOnIntersections() {
        if (!activeSelf || points.Count == 0) return;
        if (startIntersection != null) GS.Cast<Handle>(points[0]).SetScale(GameEngine.Vector3.one * IntersectionHandleScale);
        if (endIntersection != null) GS.Cast<Handle>(points[points.Count - 1]).SetScale(GameEngine.Vector3.one * IntersectionHandleScale);
    }

    public const float IntersectionHandleScale = 3.0f; //of the handles of the intersections, and of the road ends on them

    public override void Delete() {
        History.UndoManager.Touch(this);
        deleted = true;
        DetachFromIntersection(true);
        DetachFromIntersection(false);
        DeleteObjects();
        base.Delete();
    }

    public void DetachFromIntersection(bool start) {
        History.UndoManager.Touch(this);
        if (start && startIntersection != null) {
            startIntersection.RemoveRoad(this);
            startIntersection = null;
            if (points.Count > 0) GS.Cast<Handle>(points[0]).SetScale(GameEngine.Vector3.one); //the end is a plain point again
        }
        else if (!start && endIntersection != null) {
            endIntersection.RemoveRoad(this);
            endIntersection = null;
            if (points.Count > 0) GS.Cast<Handle>(points[points.Count - 1]).SetScale(GameEngine.Vector3.one);
        }
    }

    public void DeleteObjects() {
        foreach (var obj in objectInstances) {
            GS.FindRecursive<MeshInstance>(obj.Value).Delete();
        }
    }

    //a point inserted in the middle without a rotation gets the one the curve has there, so that the road keeps its shape
    public void AddPoint(Vector3 point, int position = -1, Vector2? rotation = null) {
        //never past the ends on intersections
        if (endIntersection != null && (position == -1 || position >= points.Count)) return;
        if (startIntersection != null && position == 0 && points.Count > 0) return;
        History.UndoManager.Touch(this);
        var inserted = position >= 0 && position < points.Count;
        var newRotation = rotation ?? (inserted ? GetRotationNear(point) : GameEngine.Vector2.zero);
        var newObj = (PhysicsGameObject)GameEngine.Resources.InstantiateObject("Handle");
        GO.SetParent(newObj, GO.GetObject(this));
        GO.SetPosition(newObj, point);
        GO.SetLayer(newObj, 8);
        GetHandle(newObj).curveRotation = ClampRotation(newRotation);
        generator.lr.enabled = activeSelf && points.Count > 0;
        if (!inserted) {
            points.Add(newObj);
        }
        else {
            points.Insert(position, newObj);
        }
        MarkEditedEnd();
    }

    // Rotation of the control points (yaw and roll of the sections, relative to the direction of the curve)

    static Handle GetHandle(Node3D point) {
        return GS.Cast<Handle>(point);
    }

    static Vector2 ClampRotation(Vector2 rotation) {
        return new Vector2(Mathf.Clamp(rotation.X, -GeometryHelper.MaxSectionYaw, GeometryHelper.MaxSectionYaw),
            Mathf.Clamp(rotation.Y, -GeometryHelper.MaxSectionRoll, GeometryHelper.MaxSectionRoll));
    }

    //the rotations of the points as set (start, control points, end)
    public List<Vector2> GetPointRotations() {
        var res = new List<Vector2>();
        foreach (var point in points) res.Add(GetHandle(point)?.curveRotation ?? GameEngine.Vector2.zero);
        return res;
    }

    public void SetPointRotation(int index, Vector2 rotation) {
        History.UndoManager.Touch(this);
        GetHandle(points[index]).curveRotation = ClampRotation(rotation);
        SyncSeamlessRoll(index);
    }

    //the intersection the point (by index) is the end on, if seamless
    Intersection GetSeamlessIntersection(int index) {
        Intersection intersection = null;
        if (index == 0) intersection = startIntersection;
        else if (index == points.Count - 1) intersection = endIntersection;
        return intersection != null && intersection.IsSeamless() ? intersection : null;
    }

    //the roll set on an end on a seamless intersection is given to the end of the other road too, so that the roll they
    //share (see Intersection.GetSeamlessRoll) is the one set
    void SyncSeamlessRoll(int index) {
        var intersection = GetSeamlessIntersection(index);
        if (intersection == null || !IsRollAllowed()) return;
        intersection.SetSeamlessRoll(this, ClampRotation(GetHandle(points[index]).curveRotation).Y);
    }

    //the roll (degrees) of the end of the road on its start or end, as set on its point
    public float GetOwnEndRoll(bool atStart) {
        if (points.Count == 0 || !IsRollAllowed()) return 0.0f;
        return ClampRotation(GetHandle(points[atStart ? 0 : points.Count - 1]).curveRotation).Y;
    }

    public void SetOwnEndRoll(bool atStart, float roll) {
        if (points.Count == 0) return;
        History.UndoManager.Touch(this);
        var handle = GetHandle(points[atStart ? 0 : points.Count - 1]);
        handle.curveRotation = ClampRotation(new Vector2(handle.curveRotation.X, roll));
    }

    //the tangents (along the curve) imposed to its ends by the seamless intersections they are on
    public (Vector3? start, Vector3? end) GetEndTangents() {
        var start = startIntersection?.GetSeamlessTangent(this);
        var end = endIntersection?.GetSeamlessTangent(this);
        return (start, end.HasValue ? -end.Value : null);
    }

    //the control points between the ends and the rotations of all the points the curve is evaluated with: a quadratic
    //Bézier road between two intersections is evaluated as the same cubic one (the generator needs two intermediate
    //control points), for the line too (the hidden control points of the seamless ends would make them differ)
    (List<Vector3> cps, List<Vector2> rotations) GetEvaluatedControlPoints(Vector3 start, List<Vector3> cps, Vector3 end, List<Vector2> rotations) {
        var isBezier = GetCurveType() == GeometryHelper.CurveType.Bezier;
        if (!isBezier || startIntersection == null || endIntersection == null || cps.Count != 1) return (cps, rotations);
        var newCps = new List<Vector3> {
            start + 2.0f / 3.0f * (cps[0] - start),
            end + 2.0f / 3.0f * (cps[0] - end)
        };
        if (rotations == null || rotations.Count != 3) return (newCps, rotations);
        var r = rotations;
        return (newCps, new List<Vector2> { r[0], r[0] + 2.0f / 3.0f * (r[1] - r[0]), r[2] + 2.0f / 3.0f * (r[1] - r[2]), r[2] });
    }

    CityElements.Types.RoadSettings GetTypeSettings() {
        var types = CityElements.Types.Parsers.TypeParser.GetRoadTypes();
        var typeName = state.Str("type", null);
        return typeName != null && types.TryGetValue(typeName, out var type) ? type.typeData.settings : null;
    }

    public bool IsYawAllowed(int index) {
        var settings = GetTypeSettings();
        return settings != null && settings.allowYaw;
    }

    public bool IsRollAllowed() {
        var settings = GetTypeSettings();
        return settings != null && settings.allowRoll;
    }

    //an end on an intersection has a lower yaw limit (the corners of its mouth move by tan(yaw) times half its width)
    static float ClampIntersectionYaw(float yaw) {
        return Mathf.Clamp(yaw, -GeometryHelper.MaxIntersectionYaw, GeometryHelper.MaxIntersectionYaw);
    }

    //the rotations used by the geometry: the ones the road type doesn't allow are 0
    public List<Vector2> GetEffectiveRotations() {
        var res = GetPointRotations();
        var settings = GetTypeSettings();
        var allowYaw = settings != null && settings.allowYaw;
        var allowRoll = settings != null && settings.allowRoll;
        for (int i = 0; i < res.Count; i++) {
            //the ends on a seamless intersection share their roll, without yaw (the joint has the direction of both roads)
            var seamless = GetSeamlessIntersection(i);
            if (seamless != null) {
                res[i] = new Vector2(0.0f, seamless.GetSeamlessRoll(this));
                continue;
            }
            var onIntersection = (i == 0 && startIntersection != null) || (i == res.Count - 1 && endIntersection != null);
            var r = ClampRotation(res[i]);
            var yaw = onIntersection ? ClampIntersectionYaw(r.X) : r.X;
            res[i] = new Vector2(allowYaw ? yaw : 0.0f, allowRoll ? r.Y : 0.0f);
        }
        return res;
    }

    int GetEndIndex(Intersection intersection) {
        return startIntersection == intersection ? 0 : points.Count - 1;
    }

    //the roll of the end of the road on an intersection (degrees)
    public float GetEndRoll(Intersection intersection) {
        if (points.Count == 0 || !IsRollAllowed()) return 0.0f;
        return ClampRotation(GetHandle(points[GetEndIndex(intersection)]).curveRotation).Y;
    }

    //the yaw of the end of the road on an intersection (degrees)
    public float GetEndYaw(Intersection intersection) {
        if (points.Count == 0 || !IsYawAllowed(GetEndIndex(intersection))) return 0.0f;
        return ClampIntersectionYaw(GetHandle(points[GetEndIndex(intersection)]).curveRotation.X);
    }

    //the rotation of the curve at the point closest to the given one (from the rotations of the points)
    Vector2 GetRotationNear(Vector3 point) {
        if (points.Count < 2 || curvePoints.Count < 2) return GameEngine.Vector2.zero;
        GeometryHelper.ClosestPointToCurve(point, curvePoints, out var minI);
        if (minI < 0) return GameEngine.Vector2.zero;
        var f = GeometryHelper.ClosestPointFactor(point, curvePoints[minI], curvePoints[minI + 1]);
        var t = (minI + f) / (curvePoints.Count - 1.0f);
        var positions = new List<Vector3>();
        foreach (var p in points) positions.Add(GO.GetPosition(p));
        var hermiteTension = state.Float("hermiteTension", 0.5f);
        var subEq = state.Bool("subdivideEqually");
        var first = positions[0];
        var last = positions[positions.Count - 1];
        var (evalCps, evalRotations) = GetEvaluatedControlPoints(first, positions.GetRange(1, positions.Count - 2), last, GetPointRotations());
        var (startTangent, endTangent) = GetEndTangents();
        return GeometryHelper.GetValueOnCurve(first, evalCps, last, evalRotations, t, Mathf.RoundToInt(minI + f), GetCurveType(), hermiteTension, subEq, startTangent, endTangent);
    }

    //the direction of the curve at a curve parameter (where it passes through a sample, around it)
    Vector3 GetCurveDirection(float t) {
        var n = curvePoints.Count;
        var f = Mathf.Clamp(t, 0.0f, 1.0f) * (n - 1);
        var k = Mathf.RoundToInt(f);
        if (Mathf.Abs(f - k) < 0.001f) return curvePoints[Mathf.Min(k + 1, n - 1)] - curvePoints[Mathf.Max(k - 1, 0)];
        var a = Mathf.Clamp(Mathf.FloorToInt(f), 0, n - 2);
        return curvePoints[a + 1] - curvePoints[a];
    }

    //the handles show the rotation of the sections at the points, in the frame of the curve there
    void OrientHandles() {
        if (points.Count < 2 || curvePoints.Count < 2) return;
        var positions = new List<Vector3>();
        foreach (var p in points) positions.Add(GO.GetPosition(p));
        var parameters = GeometryHelper.GetPointParameters(positions, GetCurveType(), state.Bool("subdivideEqually"));
        var rotations = GetEffectiveRotations();
        for (int i = 0; i < points.Count; i++) {
            var handle = GetHandle(points[i]);
            if (handle == null) continue;
            handle.curveFrame = GeometryHelper.GetSectionFrame(GetCurveDirection(parameters[i]));
            OrientHandle(handle, rotations[i]);
        }
    }

    static void OrientHandle(Handle handle, Vector2 rotation) {
        var orientation = (handle.curveFrame * GameEngine.Quaternion.Euler(0, rotation.X, rotation.Y)).Normalized();
        handle.curveOrientation = orientation;
        GO.SetRotation(handle, orientation);
    }

    //a handle rotated by the gizmo (in the frame of the curve) gives its yaw and roll to the point, without the pitch
    public void ReadRotatedHandles() {
        var target = GameEngine.Gizmo.GetMainTargetRoot();
        if (target == null || !GodotObject.IsInstanceValid(target) || target.GetParent() != this) return;
        for (int i = 0; i < points.Count; i++) {
            var handle = GetHandle(points[i]);
            if (handle == null || !handle.curveOrientation.HasValue) continue;
            var rotation = GO.GetRotationQuaternion(handle);
            if (rotation.AngleTo(handle.curveOrientation.Value) < 0.001f) continue; //the conversions make tiny differences
            var euler = (handle.curveFrame.Inverse() * rotation).Normalized().GetEuler();
            var cur = handle.curveRotation;
            handle.curveRotation = ClampRotation(new Vector2(IsYawAllowed(i) ? Mathf.RadToDeg(euler.Y) : cur.X,
                IsRollAllowed() ? Mathf.RadToDeg(euler.Z) : cur.Y));
            if (handle.curveRotation.Y != cur.Y) SyncSeamlessRoll(i);
            var effective = GetEffectiveRotations()[i];
            OrientHandle(handle, effective);
        }
    }

    public void RemovePoint(bool force = false) {
        if (points.Count < 3 && !force) {
            return;
        }
        else {
            History.UndoManager.Touch(this);
            var obj = points[points.Count - 1];
            points.RemoveAt(points.Count - 1);
            GO.Destroy(obj);
            DetachFromIntersection(false);
            MarkEditedEnd();
        }
    }

    public void RemoveFirstPoint(bool force = false) {
        if (points.Count < 3 && !force) return;
        History.UndoManager.Touch(this);
        var obj = points[0];
        points.RemoveAt(0);
        GO.Destroy(obj);
        DetachFromIntersection(true);
        MarkEditedEnd();
    }

    //sets the control points to the given positions and rotations (none: no rotation), used by undo/redo and when
    //splitting the road
    public void RestorePoints(List<Vector3> positions, List<Vector2> rotations = null) {
        //the old end points may end up in the middle, without the look and layer of the free road ends
        if (points.Count > 0) {
            ResetIntersectionPoint(points[0]);
            ResetIntersectionPoint(points[points.Count - 1]);
        }
        while (points.Count > positions.Count) {
            var obj = points[points.Count - 1];
            points.RemoveAt(points.Count - 1);
            GO.Destroy(obj);
        }
        while (points.Count < positions.Count) {
            var newObj = (PhysicsGameObject)GameEngine.Resources.InstantiateObject("Handle");
            GO.SetParent(newObj, GO.GetObject(this));
            GO.SetLayer(newObj, 8);
            GO.SetEnabled(newObj, activeSelf);
            points.Add(newObj);
        }
        for (int i = 0; i < positions.Count; i++) {
            GO.SetPosition(points[i], positions[i]);
            var rotation = rotations != null && i < rotations.Count ? rotations[i] : GameEngine.Vector2.zero;
            GetHandle(points[i]).curveRotation = ClampRotation(rotation);
        }
        generator.lr.enabled = activeSelf && points.Count > 0;
        MarkEditedEnd();
    }

    public void RemoveControlPoints(List<PhysicsGameObject> toRemove) {
        History.UndoManager.Touch(this);
        foreach (var point in toRemove) {
            if (points.Count <= 2 || !points.Remove(point)) continue;
            GO.Destroy(point);
        }
        MarkEditedEnd();
    }

    void UpdateCurve() {
        if (points.Count == 0) return;
        if (state.Bool("project") || state.Bool("projectAll")) {
            if (projectedTerrainVersion != GameEngine.Terrain.HeightmapVersion || projectedPoints.Count != points.Count) {
                projectedPoints.Clear();
                projectedTerrainVersion = GameEngine.Terrain.HeightmapVersion;
            }
            foreach (var point in points) {
                var position = GO.GetPosition(point);
                if (projectedPoints.TryGetValue(point, out var projected) && position == projected) continue;
                projected = GeometryHelper.ProjectPoint(position);
                if (position != projected) GO.SetPosition(point, projected);
                projectedPoints[point] = projected;
            }
        } else {
            projectedPoints.Clear();
        }
        cps.Clear();
        start = GO.GetPosition(points[0]);
        for (int i = 1; i < points.Count - 1; i++) {
            cps.Add(GO.GetPosition(points[i]));
        }
        end = GO.GetPosition(points[points.Count - 1]);
        ReadRotatedHandles();
        rots = GetPointRotations();
        seamlessEnds = GetSeamlessEnds();
    }

    //what the seamless intersections at the ends impose to them (from the other roads too)
    (Vector3? startTangent, Vector3? endTangent, Vector2 startRotation, Vector2 endRotation) GetSeamlessEnds() {
        var (startTangent, endTangent) = GetEndTangents();
        var rotations = GetEffectiveRotations();
        if (rotations.Count == 0) return (startTangent, endTangent, GameEngine.Vector2.zero, GameEngine.Vector2.zero);
        return (startTangent, endTangent, rotations[0], rotations[rotations.Count - 1]);
    }

    float GetLineLength() {
        var curPoint = points[0];
        var dist = 0.0f;
        foreach (var point in points) {
            dist += GameEngine.Vector3.Distance(GO.GetPosition(point), GO.GetPosition(curPoint));
            curPoint = point;
        }
        return dist;
    }

    public GeometryHelper.CurveType GetCurveType() {
        return (GeometryHelper.CurveType)state.Int("curveType");
    }

    public int GetStateSegments() {
        return state.Int("segments", 2);
    }

    public Vector3 GetStandardVec3(string name) {
        return curType.GetStandardVec3(name, variableContainer);
    }

    public Vector2 GetStandardVec2(string name) {
        return curType.GetStandardVec2(name, variableContainer);
    }

    public float GetStandardFloat(string name) {
        return curType.GetStandardFloat(name, variableContainer);
    }

    public bool GetStandardBool(string name) {
        return curType.GetStandardBool(name, variableContainer);
    }

    public string GetStandardString(string name) {
        return curType.GetStandardString(name, state);
    }

    public int GetSegments() {
        //the generator needs at least two segments, even while the road has not been placed yet
        if (points.Count < 2) return 2;
        if (GetCurveType() == GeometryHelper.CurveType.LowPoly) return points.Count;
        var canBeSimplified = GetStandardBool("canBeSimplified");
        if (!state.Bool("projectAll") && points.Count <= 2 && canBeSimplified) return 2;
        if (state.Bool("segmentsPer100m")) {
            var length = GetLineLength();
            return Mathf.Max(2, (int)(GetStateSegments() * length / 100));
        }
        else return GetStateSegments() + 1;
    }

    public void RebuildLine() {
        ReloadType();
        curvePoints.Clear();
        var segments = GetSegments();
        var (evalCps, _) = GetEvaluatedControlPoints(start, cps, end, null);
        var (startTangent, endTangent) = GetEndTangents();
        for (int i = 0; i < segments; i++) {
            var hermiteTension = state.Float("hermiteTension", 0.5f);
            var subEq = state.Bool("subdivideEqually");
            var pos = GeometryHelper.GetPointOnCurve(start, evalCps, end, i / (segments - 1.0f), i, GetCurveType(), hermiteTension, subEq, startTangent, endTangent);
            if (state.Bool("project") || state.Bool("projectAll")) pos = GeometryHelper.ProjectPoint(pos);
            curvePoints.Add(pos);
        }
        if (state.Int("lpf") > 0) curvePoints = GeometryHelper.LowPassFilter(curvePoints, state.Int("lpf"), segments);
        forceRemesh = true;
        RebuildIntersections();
    }

    public CityElements.Types.Runtime.RoadType GetRoadType() {
        var dict = CityElements.Types.Parsers.TypeParser.GetRoadTypes();
        return dict[state.Str("type", null)];
    }

    void ReloadType() {
        var type = GetRoadType();
        if (type != curType) {
            variableContainer = GetRoadType().variableContainer.GetClone();
            curType = type;
        }
        curType.FillInitialVariables(variableContainer, state, instanceState, runtimeState, 0.0f, 0);
    }

    public void RebuildMesh() {
        //extra info in runtimeState
        runtimeState.SetBool("hasStartIntersection", startIntersection != null);
        runtimeState.SetBool("hasEndIntersection", endIntersection != null);
        runtimeState.SetVector3("startIntersectionPosition", startIntersection != null ? GO.GetPosition(startIntersection.point) : GameEngine.Vector3.zero);
        runtimeState.SetVector3("endIntersectionPosition", endIntersection != null ? GO.GetPosition(endIntersection.point) : GameEngine.Vector3.zero);
        runtimeState.SetFloat("startIntersectionNumberOfRoads", startIntersection != null ? startIntersection.roads.Count : 0);
        runtimeState.SetFloat("endIntersectionNumberOfRoads", endIntersection != null ? endIntersection.roads.Count : 0);
        runtimeState.SetVector3("startDir", curvePoints.Count > 1 ? GameEngine.Vector3.Normalize(curvePoints[1] - curvePoints[0]) : GameEngine.Vector3.zero);
        runtimeState.SetVector3("endDir", curvePoints.Count > 1 ? GameEngine.Vector3.Normalize(curvePoints[curvePoints.Count - 1] - curvePoints[curvePoints.Count - 2]) : GameEngine.Vector3.zero);

        generator.state = state;
        generator.instanceState = instanceState;
        generator.runtimeState = runtimeState;
        generator.curType = GetRoadType();
        generator.hasStartIntersection = startIntersection != null;
        generator.hasEndIntersection = endIntersection != null;
        generator.segments = GetSegments();
        generator.cps = new List<Vector3>(cps);
        generator.cpRotations = GetEffectiveRotations();
        generator.start = start;
        generator.end = end;
        generator.valid = points.Count > 1;
        generator.startIntersectionCenter = null;
        generator.endIntersectionCenter = null;
        (generator.startTangent, generator.endTangent) = GetEndTangents();
        (generator.cps, generator.cpRotations) = GetEvaluatedControlPoints(start, generator.cps, end, generator.cpRotations);

        //the ends on a seamless intersection are on its point, their sections along the direction of the joint
        if (startIntersection != null && !startIntersection.IsSeamless()) {
            var dir = curvePoints[1] - curvePoints[0];
            dir = new Vector3(dir[0], 0, dir[2]);
            var index = startIntersection.roads.IndexOf(this);
            generator.start += GameEngine.Vector3.Normalize(dir) * startIntersection.sizes[index];
            var height = startIntersection.GetConnectionHeight(index);
            if (height != 0) generator.start += GameEngine.Vector3.up * height;
            generator.startIntersectionCenter = GO.GetPosition(startIntersection.point);
        }
        if (endIntersection != null && !endIntersection.IsSeamless()) {
            var dir = curvePoints[curvePoints.Count - 2] - curvePoints[curvePoints.Count - 1];
            dir = new Vector3(dir[0], 0, dir[2]);
            var index = endIntersection.roads.IndexOf(this);
            generator.end += GameEngine.Vector3.Normalize(dir) * endIntersection.sizes[index];
            var height = endIntersection.GetConnectionHeight(index);
            if (height != 0) generator.end += GameEngine.Vector3.up * height;
            generator.endIntersectionCenter = GO.GetPosition(endIntersection.point);
        }

        generator.Rebuild();

        //for terrain
        var startConns = new List<object>();
        var endConns = new List<object>();
        for (int i = 0; i < generator.sidePoints.Count; i++) {
            startConns.Add(startIntersection);
            endConns.Add(endIntersection);
        }
        var centerLines = new List<List<Vector3>>();
        for (int i = 0; i < generator.sidePoints.Count; i++) {
            centerLines.Add(generator.GetCurvePoints());
        }
        anchorManager.Update(GO.GetObject(this), generator.sidePoints, startConns, endConns, centerLines);
        splitLine.MarkDirty();
        OrientHandles();
        EnlargeEndsOnIntersections();

        UpdateOlds();
    }

    void RebuildIntersections() {
        if (startIntersection != null) startIntersection.forceRemesh = true;
        if (endIntersection != null) endIntersection.forceRemesh = true;
    }

    bool DidRoadChange() {
        var intersections = new List<Vector3>();
        if (startIntersection != null) intersections.Add(GO.GetPosition(startIntersection.point));
        if (endIntersection != null) intersections.Add(GO.GetPosition(endIntersection.point));
        if (old_intersections.Count != intersections.Count) return true;
        if (cps.Count != old_cps.Count) return true;
        for (int i = 0; i < cps.Count; i++) {
            if (!GeometryHelper.AreVectorsEqual(cps[i], old_cps[i])) return true;
        }
        if (rots.Count != old_rots.Count) return true;
        for (int i = 0; i < rots.Count; i++) {
            if (rots[i] != old_rots[i]) return true;
        }
        if (seamlessEnds != old_seamlessEnds) return true;
        if (!GeometryHelper.AreVectorsEqual(start, old_start)) return true;
        if (!GeometryHelper.AreVectorsEqual(end, old_end)) return true;
        if (state.HasChanged()) return true;
        if (instanceState.HasChanged()) return true;
        return false;
    }

    void UpdateOlds() {
        old_cps = new List<Vector3>(cps);
        old_rots = new List<Vector2>(rots);
        old_seamlessEnds = seamlessEnds;
        old_intersections = new List<Vector3>();
        if (startIntersection != null) old_intersections.Add(GO.GetPosition(startIntersection.point));
        if (endIntersection != null) old_intersections.Add(GO.GetPosition(endIntersection.point));
        old_start = start;
        old_end = end;
        state.FlagAsUnchanged();
        instanceState.FlagAsUnchanged();
    }

    public float GetCrosswalkSize(Intersection intersection) {
        return intersection == startIntersection ? state.Float("startCrosswalkSize", 1.0f) : state.Float("endCrosswalkSize", 1.0f);
    }

    public string GetIntersectionTexture(Intersection intersection) {
        return intersection == startIntersection ? state.Str("startIntersectionTexture", "") : state.Str("endIntersectionTexture", "");
    }

    public float GetIntersectionAdd(Intersection intersection) {
        return intersection == startIntersection ? state.Float("startIntersectionAdd") : state.Float("endIntersectionAdd");
    }

    //the height of the end on an intersection relative to the intersection point (added to the automatic one if set)
    public float GetConnectionHeight(Intersection intersection) {
        return intersection == startIntersection ? instanceState.Float("startConnectionHeight") : instanceState.Float("endConnectionHeight");
    }

    //whether the end on an intersection takes the height of the other roads there
    public bool IsConnectionHeightAuto(Intersection intersection) {
        return intersection == startIntersection ? instanceState.Bool("startConnectionHeightAuto") : instanceState.Bool("endConnectionHeightAuto");
    }

    public void UpdateLine() {
        if (generator == null) return;
        UpdateCurve();
        if (DidRoadChange()) RebuildLine();
    }

    public void SetGhost(bool value) {
        History.UndoManager.Touch(this);
        ghost = value;
        ApplyGhost();
    }

    public void ApplyGhost() {
        if (generator == null || GO.IsNull(generator)) return;
        if (generator.mr != null) generator.mr.Visible = !ghost;
        generator.CollisionLayer = ghost ? 0u : 1u << 7;
        if (generator.propsContainer != null) GO.SetEnabled(generator.propsContainer, !ghost);
    }

    public int UpdateMesh() {
        if (forceRemesh) {
            RebuildMesh();
            ApplyGhost();
            forceRemesh = false;
            repositionObjects = true;
            return 1;
        }
        return 0;
    }

    private bool hasRuntimeTypeActive() {
        return generator != null && generator.curType != null;
    }

    public Vector3 GetRuntimeVec3(string variable) {
        if (hasRuntimeTypeActive() && generator.curType.vector3Definitions.ContainsKey(variable)) {
            return generator.curType.vector3Definitions[variable].GetValue(generator.subGen.variableContainer);
        }
        else {
            return GameEngine.Vector3.zero;
        }
    }

    public Vector2 GetRuntimeVec2(string variable) {
        if (hasRuntimeTypeActive() && generator.curType.vector2Definitions.ContainsKey(variable)) {
            return generator.curType.vector2Definitions[variable].GetValue(generator.subGen.variableContainer);
        }
        else {
            return GameEngine.Vector2.zero;
        }
    }

    public float GetRuntimeFloat(string variable) {
        if (hasRuntimeTypeActive() && generator.curType.numberDefinitions.ContainsKey(variable)) {
            return generator.curType.numberDefinitions[variable].GetValue(generator.subGen.variableContainer);
        }
        else {
            return 0f;
        }
    }

    public bool GetRuntimeBool(string variable) {
        if (hasRuntimeTypeActive() && generator != null && generator.curType.boolDefinitions.ContainsKey(variable)) {
            return generator.curType.boolDefinitions[variable].GetValue(generator.subGen.variableContainer);
        }
        else {
            return false;
        }
    }

    // Parametric objects

    public override RC.VariableContainer GetParametricObjectVariableContainer() {
        return generator.subGen.variableContainer;
    }

    public override ObjectState GetContainerStateForParametricObjects() {
        return state;
    }

    public override ObjectState[] GetContainerStateArrayForParametricObjects() {
        return new ObjectState[] { state, instanceState, runtimeState };
    }

    public override CityElements.Types.Runtime.RuntimeType<CityElements.Types.RoadType> GetRuntimeType() {
        return generator.curType;
    }

    public Lua.LuaValue GetScriptContext() {
        scriptContext ??= new RoadScriptContext(this);
        return scriptContext;
    }
}

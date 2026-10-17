using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using PhysicsScriptable = Godot.StaticBody3D; //the side and roof colliders are attached to the line
using GameObject = Godot.Node3D;
using LineRenderer = GameEngine.LineRenderer;
using Mesh = Godot.ArrayMesh;
using MeshRenderer = Godot.MeshInstance3D;
using MeshCollider = Godot.CollisionShape3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using States;
using SubMeshData = GeometryHelpers.SubMesh.SubMeshData;
using RC = RuntimeCalculator;
using V3 = GameEngine.Vector3;

public partial class BuildingLine : PhysicsScriptable, IGroundable, IObjectWithState {
    List<TerrainPoint> linePoints = new List<TerrainPoint>();
    public List<Building> buildings = new List<Building>();
    public List<BuildingSideGenerator> buildingSides = new List<BuildingSideGenerator>();
    public ObjectState state, instanceState;
    bool deleted = false;
    public TerrainSubmeshGenerator roof;
    public ObjectState stateForNewBuildings;

    public MeshRenderer mr;
    public Mesh m;

    TerrainAnchor curAnchor = null;

    List<Vector3> oldLinePoints = new List<Vector3>();
    List<TerrainPoint> oldDividingPoints = new List<TerrainPoint>();
    int oldBuildingsCount;

    GameObject lrContainer;
    public LineRenderer lr;
    public GameObject container;
    CityElements.Types.Runtime.Buildings.BuildingType.Line curType;
    RC.VariableContainer variableContainer;

    public List<ObjectState> forcedBuildingStates = null;
    public List<ObjectState> forcedSideStates = null;

    public void Initialize(GameObject container) {
        (mr, m) = GameEngine.Mesh.CreateMesh(GO.GetObject(this), new Material[] { MaterialManager.GetMaterial("_TRANSPARENT_", true) }); //TODO: check if not sharedMaterial breaks anything
        state = PresetManager.GetPreset("buildingLine", 0);
        stateForNewBuildings = PresetManager.GetPreset("building", 0);
        instanceState = new ObjectState();
        this.container = container;
        InitializeLineRenderer();
    }

    public ObjectState GetState() {
        return state;
    }

    void ReloadType() {
        var type = GetLineType();
        if (type != curType) {
            variableContainer = GetLineType().variableContainer.GetClone();
            curType = type;
        }
        curType.FillInitialVariables(variableContainer, state, instanceState);
    }

    public CityElements.Types.Runtime.Buildings.BuildingType.Line GetLineType() {
        var dict = CityElements.Types.Parsers.TypeParser.GetBuildingTypes();
        return dict[state.Str("type", null)].line;
    }

    void AddBuilding(TerrainPoint firstPoint, TerrainPoint lastPoint) {
        var b = new Building();
        b.container = GO.GetObject(this);
        buildings.Add(b);
        b.Initialize(this, stateForNewBuildings);
        b.firstPoint = firstPoint;
        b.lastPoint = lastPoint;
    }

    void RemoveBuilding(int i) {
        if (i < 0 || i >= buildings.Count) return;
        buildings[i].Delete();
        buildings.RemoveAt(i);
    }

    public List<TerrainPoint> GetPointsComponents() {
        var res = new List<TerrainPoint>();
        res.AddRange(linePoints);
        return res;
    }

    public Building GetColliderBuilding(MeshCollider c) {
        foreach (var b in buildings) {
            if (b.HasCollider(c)) return b;
        }
        return null;
    }

    public BuildingSideGenerator GetColliderSide(MeshCollider c) {
        foreach (var b in buildings) {
            var res = b.GetSideFromCollider(c);
            if (res != null) return res;
        }
        return null;
    }

    void InitializeLineRenderer() {
        lr = GO.CreateLineRenderer(this);
        lr.startWidth = 0.5f;
        lr.endWidth = 0.5f;
        lr.material = MaterialManager.GetHandleMaterial((GameEngine.Color.red + GameEngine.Color.yellow) * 0.5f);
        lr.enabled = false;
    }

    void Clear() {
        var linePointsTemp = new List<TerrainPoint>(linePoints);
        foreach (var point in linePointsTemp) {
            point.DeleteManual(this);
        }
        linePoints.Clear();
        foreach (var building in buildings) {
            building.Delete();
        }
        buildings.Clear();
        lr.enabled = false;
    }

    public void Delete() {
        History.UndoManager.Touch(this);
        Clear();
        GO.Destroy(this);
        if (roof != null) roof.Delete();
        deleted = true;
    }

    public bool IsDeleted() {
        return deleted;
    }

    //removes the line without deleting its points (used by undo/redo, which deletes them explicitly)
    public void RemoveForUndo() {
        foreach (var point in linePoints) point.RemoveLinkSilently(this);
        linePoints.Clear();
        foreach (var building in buildings) {
            building.Delete();
        }
        buildings.Clear();
        lr.enabled = false;
        GO.Destroy(this);
        if (roof != null) roof.Delete();
        deleted = true;
    }

    //sets the points and the states of the line and of its buildings (used by undo/redo)
    public void RestoreFromSnapshot(List<TerrainPoint> points, ObjectState newState, ObjectState newInstanceState,
        List<ObjectState> buildingStates, List<ObjectState> sideStates) {

        var oldPoints = new HashSet<TerrainPoint>(linePoints);
        var newPoints = new HashSet<TerrainPoint>(points);
        foreach (var point in newPoints) {
            if (!oldPoints.Contains(point)) point.AddLink(this);
        }
        foreach (var point in oldPoints) {
            if (!newPoints.Contains(point)) point.RemoveLinkSilently(this);
        }
        linePoints = new List<TerrainPoint>(points);
        state = newState;
        instanceState = newInstanceState;
        curAnchor = null;
        if (linePoints.Count > 0 && linePoints[linePoints.Count - 1].anchor is TerrainAnchor ta && !GO.IsNull(ta)) curAnchor = ta;
        //applied by the next update, once the buildings match the points (as when loading)
        forcedBuildingStates = buildingStates.Count > 0 ? buildingStates : null;
        forcedSideStates = sideStates.Count > 0 ? sideStates : null;
        oldBuildingsCount = -1; //forces the rebuild
        lr.enabled = false;
    }

    bool DidChange() {
        var pointsToDelete = new List<TerrainPoint>();
        foreach (var point in linePoints) {
            if (point.IsDeleted()) {
                pointsToDelete.Add(point);
            } else {
                point.UpdatePosition();
            }
        }
        foreach (var point in pointsToDelete) {
            linePoints.Remove(point);
        }
        if (oldLinePoints.Count != linePoints.Count) return true;
        for (int i = 0; i < linePoints.Count; i++) {
            if (!GeometryHelper.AreVectorsEqual(linePoints[i].GetPoint(), oldLinePoints[i])) return true;
        }
        var dividingPoints = new List<TerrainPoint>();
        foreach (var point in linePoints) {
            if (point.dividing) dividingPoints.Add(point);
        }
        if (oldDividingPoints.Count != dividingPoints.Count) return true;
        for (int i = 0; i < dividingPoints.Count; i++) {
            if (dividingPoints[i] != oldDividingPoints[i]) return true;
        }
        if (oldBuildingsCount != buildings.Count) return true;
        for (int i = 0; i < buildings.Count; i++) {
            if (buildings[i].DidChange()) return true;
        }
        if (state.HasChanged()) return true;

        return false;
    }

    (List<(Vector3, Vector3)>, List<int>) SplineWithoutDuplicates(List<(Vector3 point, Vector3 normal)> input) {
        var splineMap = new List<int>();
        var res = new List<(Vector3 point, Vector3 normal)>();
        for (int i = 0; i < input.Count; i++) {
            if (i == 0 || V3.Magnitude(input[i].point - res[res.Count - 1].point) > GeometryHelper.epsilon) {
                res.Add(input[i]);
            }
            splineMap.Add(res.Count - 1);
        }
        return (res, splineMap);
    }

    bool FillSplineNormals(List<(Vector3 point, Vector3 normal)> input) {
        bool reverse = false;
        for (int i = 0; i < input.Count; i++) {
            var d1 = i == 0 ? V3.zero : (input[i].point - input[i - 1].point);
            var d2 = i >= (input.Count - 1) ? V3.zero : (input[i + 1].point - input[i].point);
            var d = V3.Normalize(V3.Normalize(d1) + V3.Normalize(d2));
            var n = V3.Normalize(V3.Cross(d, V3.up));
            if (input[i].normal == V3.zero) {
                input[i] = (input[i].point, n);
            } else {
                var mult = GameEngine.Math.Sign(V3.Dot(input[i].normal, n));
                if (mult < 0) reverse = true;
                input[i] = (input[i].point, n);
            }
        }
        return reverse;
    }

    (List<(Vector3, Vector3)>, List<int>, bool) GetFullSpline() {
        var res = new List<(Vector3 point, Vector3 normal)>();
        for (int i = 0; i < linePoints.Count; i++) {
            var point = linePoints[i].GetPoint();
            var normal = V3.zero;
            if (linePoints[i].anchor is TerrainAnchor anchor) {
                normal = -anchor.insideDirection;
            }
            res.Add((point, normal));
        }
        var res2 = SplineWithoutDuplicates(res);
        res = res2.Item1;
        var splineMap = res2.Item2;
        var reversed = FillSplineNormals(res);
        if (reversed ^ state.Bool("invertDirection")) {
            for (int i = 0; i < res.Count; i++) {
                res[i] = (res[i].point, V3.Normalize(res[i].normal) * -1);
            }
        }
        return (res, splineMap, reversed);
    }

    List<(Vector3, Vector3)> GetBuildingSpline(int startI, int endI, List<(Vector3 point, Vector3 normal)> fullSpline, List<int> splineMap, bool reversed) {
        var res = new List<(Vector3 point, Vector3 normal)>();
        var resAux = new List<Vector3>();
        if (endI == 0 && startI == linePoints.Count - 1) {
            res.Add(fullSpline[fullSpline.Count - 1]);
            res.Add(fullSpline[0]);
        } else {
            for (int i = startI; i <= endI; i++) {
                var v = fullSpline[splineMap[i]];
                var idx = GeometryHelper.FindVector(resAux, v.point);
                if (idx == -1) {
                    res.Add(v);
                    resAux.Add(v.point);
                } else {
                    res[idx] = (res[idx].point, res[idx].normal + v.normal);
                }
            }
        }
        for (int i = 0; i < res.Count; i++) {
            res[i] = (res[i].point, V3.Normalize(res[i].normal));
        }
        if (reversed ^ state.Bool("invertDirection")) {
            res.Reverse();
        }
        return res;
    }

    TerrainSubmeshGenerator GetRoof(bool enabled, TerrainSubmeshGenerator current) {
        if (enabled) {
            if (current == null) {
                var newSide = new TerrainSubmeshGenerator(this);
                return newSide;
            } else {
                return current;
            }
        } else {
            if (current != null) current.Delete();
            return null;
        }
    }

    int UpdateBuildings(bool force) {
        if (linePoints.Count == 0) return 0;
        var count = 0;
        var splineParts = GetFullSpline();
        var fullSpline = splineParts.Item1;
        var splineMap = splineParts.Item2;
        var reversed = splineParts.Item3;
        roof = GetRoof(state.Bool("frontOnly"), roof);
        var forcedHeight = 0.0f;
        var forcedMaxHeight = 0.0f;
        if (state.Bool("frontOnly")) {
            var roofSpline = new List<Vector3>();
            var maxH = float.MinValue;
            for (int i = 0; i < fullSpline.Count; i++) {
                var p = fullSpline[i].Item1;
                if (p[1] > maxH) maxH = p[1];
            }
            forcedHeight = maxH + state.Float("height");
            forcedMaxHeight = maxH;
            for (int i = 0; i < fullSpline.Count; i++) {
                var p = fullSpline[i].Item1;
                var p2 = new Vector3(p[0], forcedHeight, p[2]);
                roofSpline.Add(p2);
            }

            var vc = variableContainer;
            if (roof != null && fullSpline.Count > 2) {
                try {
                    roof.UpdateMesh(roofSpline, state.Str("roofTex"), vc.floats[vc.floatIndex["uMult"]], vc.floats[vc.floatIndex["vMult"]]);
                } catch (System.Exception e) {
                    GameEngine.LogWarning("Error during mesh generation on building line" + GO.GetName(this) + ": " + e.StackTrace.ToString());
                }
            }
        }
        for (int i = 0; i < buildings.Count; i++) {
            var elem = buildings[i];
            if (state.Bool("frontOnly")) {
                elem.state.SetBool("back", false);
                elem.state.SetBool("left", false);
                elem.state.SetBool("right", false);
                elem.state.SetBool("top", false);
                elem.state.SetFloat("height", state.Float("height"));
            }
            if (force) elem.spline = GetBuildingSpline(linePoints.IndexOf(elem.firstPoint), linePoints.IndexOf(elem.lastPoint), fullSpline, splineMap, reversed);
        }
        for (int i = 0; i < buildings.Count; i++) {
            if (forcedBuildingStates != null && forcedBuildingStates.Count == buildings.Count) {
                buildings[i].State = forcedBuildingStates[i]; //through the property, which flags the building as changed
            } else if (forcedSideStates != null && forcedSideStates.Count == buildings.Count) {
                buildings[i].state.SetState("frontState", forcedSideStates[i]);
            }
            buildings[i].PreUpdateMesh(state.Bool("frontOnly"), forcedHeight, forcedMaxHeight, force);
        }

        //Check if clockwise (to reverse the previous and next if needed)
        var clockwise = false;
        if (buildings.Count > 1 && buildings[0].spline.Count > 0 && buildings[1].spline.Count > 0) {
            var s0 = buildings[0].spline;
            var s1 = buildings[1].spline;
            var p0 = s0[0];
            var p1 = s1[0];
            var dir = V3.Normalize(p1.point - p0.point);
            var n = V3.Normalize(p0.normal);
            var cross = V3.Cross(dir, n);
            clockwise = cross[1] > 0;
        }

        for (int i = 0; i < buildings.Count; i++) {
            float? prevHeight = null, nextHeight = null, prevDepth = null, nextDepth = null;
            bool? prevFixAngles = null, nextFixAngles = null;
            bool prevActive = false, nextActive = false;
            Building prevBuilding = null, nextBuilding = null;
            
            //Get the next
            if (i > 0) prevBuilding = buildings[i - 1];
            else if (state.Bool("loop")) prevBuilding = buildings[buildings.Count - 1];
            
            //Get the previous
            if (i < buildings.Count - 1) nextBuilding = buildings[i + 1];
            else if (state.Bool("loop")) nextBuilding = buildings[0];

            //Reverse them if needed
            if (clockwise) {
                var tmp = prevBuilding;
                prevBuilding = nextBuilding;
                nextBuilding = tmp;
            }

            //Get the parameters
            if (prevBuilding != null) {
                prevActive = prevBuilding.enabled;
                prevHeight = prevBuilding.height;
                prevDepth = prevBuilding.state.Float("depth");
                prevFixAngles = prevBuilding.state.Bool("fixAcuteAngles");
            }
            if (nextBuilding != null) {
                nextActive = nextBuilding.enabled;
                nextHeight = nextBuilding.height;
                nextDepth = nextBuilding.state.Float("depth");
                nextFixAngles = nextBuilding.state.Bool("fixAcuteAngles");
            }

            //Update the building mesh
            count += buildings[i].UpdateMesh(prevActive, nextActive, prevHeight, nextHeight, prevDepth, nextDepth, prevFixAngles, nextFixAngles);
        }
        if (forcedBuildingStates != null) {
            forcedBuildingStates.Clear();
            forcedBuildingStates = null;
        }
        if (forcedSideStates != null) {
            forcedSideStates.Clear();
            forcedSideStates = null;
        }
        if (count > 0 || force) ReloadLine();
        return count;
    }

    public (int resultCode, List<TerrainAnchor> points) AutoClose(float minLength, float maxLength, List<ObjectState> states, bool subdivide) {
        var extraPoints = new List<TerrainPoint>();
        void AddAnchorPoint(TerrainAnchor anchor) {
            linePoints.Add(TerrainPoint.Create(anchor.GetPoint(), this, container, anchor));
        }

        void AddAnchorLinkPoint(PointLink linkObj, float percent, bool subdivide = true) {
            AddPoint(GO.GetObject(linkObj), V3.Lerp(GO.GetPosition(linkObj), GO.GetPosition(linkObj.next), percent));
            if (subdivide) {
                var newP = linePoints[linePoints.Count - 1];
                newP.dividing = true;
                extraPoints.Add(newP);
            }
        }

        List<float> GetRandomDistances(float totalDistance) {
            var res = new List<float>();
            var total = 0.0f;
            while (total < totalDistance) {
                var d = minLength + (float)RandomManager.rnd.NextDouble() * (maxLength - minLength);
                total += d;
                res.Add(total);
            }
            for (int i = 0; i < res.Count; i++) {
                res[i] /= total;
            }
            if (res.Count > 0) res.RemoveAt(res.Count - 1);
            return res;
        }

        void AddRandomPointsToMiddleLine(PointLink link, TerrainAnchor start, TerrainAnchor end) {
            var dist = V3.Distance(start.GetPoint(), end.GetPoint());
            var distances = GetRandomDistances(dist);
            //links point to the anchor's handle object (not the anchor's own object)
            if (link.next == start.GetObject()) distances.Reverse();
            foreach (var d in distances) {
                AddAnchorLinkPoint(link, d);
            }
        }

        void AddRandomPointsToFirstAndLastLine(PointLink link, float startPercent, float endPercent, bool keepLast) {
            var p1 = GO.GetPosition(link);
            var p2 = GO.GetPosition(link.next);
            var startP = V3.Lerp(p1, p2, startPercent);
            var endP = V3.Lerp(p1, p2, endPercent);
            var dist = V3.Distance(endP, startP);
            var distances = GetRandomDistances(dist);
            if (keepLast && distances.Count == 0) {
                distances.Add(0.5f);
            }
            foreach (var d in distances) {
                AddAnchorLinkPoint(link, startPercent + (endPercent - startPercent) * d);
            }
        }
        if (linePoints.Count != 1 || !(linePoints[0].anchor is LineAnchor)) return (-1, null);
        if (subdivide && (minLength <= 0 || maxLength <= 0 || maxLength < minLength)) return (0, null);
        History.UndoManager.Touch(this);
        var lineAnchor = (LineAnchor)linePoints[0].anchor;
        var startAnchor = GS.FindRecursive<TerrainAnchor>(lineAnchor.start);
        var endAnchor = GS.FindRecursive<TerrainAnchor>(lineAnchor.end);
        if (startAnchor == null || endAnchor == null) return (0, null);
        var list = endAnchor.GetPointListTo(startAnchor, true);
        list.Insert(0, endAnchor);
        if (list.Count <= 1) return (0, null);
        var linkObj = GS.FindRecursive<PointLink>(lineAnchor.start);
        if (linkObj == null) return (0, null);
        IAnchorable curAnchor = lineAnchor;
        foreach (var item in list) {
            if (subdivide) {
                if (curAnchor is LineAnchor lA) { //first segment
                    AddRandomPointsToFirstAndLastLine(linkObj, lineAnchor.percent, 1, false);
                } else if (curAnchor is TerrainAnchor tA) { //middle segments
                    if (!GeometryHelper.AreVectorsEqual(curAnchor.GetPoint(), item.GetPoint())) {
                        var link2 = GS.FindRecursive<PointLink>(tA.GetObject());
                        var link3 = GS.FindRecursive<PointLink>(item.GetObject());
                        if (link2 != null && link2.next == item.GetObject()) {
                            AddRandomPointsToMiddleLine(link2, tA, item);
                        } else if (link3 != null && link3.next == tA.GetObject()) {
                            AddRandomPointsToMiddleLine(link3, tA, item);
                        }
                    }
                }
            }
            AddAnchorPoint(item);
            curAnchor = item;
        }
        //final segment
        if (subdivide) {
            AddRandomPointsToFirstAndLastLine(linkObj, 0, lineAnchor.percent, true);
        } else {
            AddAnchorLinkPoint(linkObj, lineAnchor.percent * 0.5f, false);
        }
        state.SetBool("loop", true);

        //clean points at same position
        var pointsToDelete = new List<TerrainPoint>();
        for (int i = 1; i < linePoints.Count - 1; i++) {
            var pPrev = linePoints[i - 1];
            var pCur = linePoints[i];
            if (GeometryHelper.AreVectorsEqual(pPrev.GetPoint(), pCur.GetPoint())) {
                pointsToDelete.Add(pCur);
            }
        }
        foreach (var point in pointsToDelete) {
            RemovePoint(point);
            point.Delete();
        }
        pointsToDelete.Clear();

        //clean points in straight line
        for (int i = 1; i < linePoints.Count - 1; i++) {
            var pCur = linePoints[i];
            var pNext = linePoints[i + 1];
            var pPrev = linePoints[i - 1];
            var vecTo = pNext.GetPoint() - pCur.GetPoint();
            var vecFrom = pPrev.GetPoint() - pCur.GetPoint();
            var angle = 180.0f - V3.Angle(vecFrom, vecTo);
            if (angle <= GeometryHelper.epsilon && !pCur.dividing) {
                pointsToDelete.Add(pCur);
            }
        }
        foreach (var point in pointsToDelete) {
            RemovePoint(point);
            point.Delete();
        }
        pointsToDelete.Clear();

        //split buildings if angle greater than threshold
        var maxDepth = 0.0f;
        foreach (var state in states) {
            if (state.Float("depth") > maxDepth) maxDepth = state.Float("depth");
        }
        for (int i = 1; i < linePoints.Count - 1; i++) {
            var pCur = linePoints[i];
            var pNext = linePoints[i + 1];
            var pPrev = linePoints[i - 1];
            var vecTo = pNext.GetPoint() - pCur.GetPoint();
            var vecFrom = pPrev.GetPoint() - pCur.GetPoint();
            var angle = 180.0f - V3.Angle(vecFrom, vecTo);
            if (angle >= 45.0f) {
                pCur.dividing = true;
            }
        }
        foreach (var point in pointsToDelete) {
            RemovePoint(point);
            point.Delete();
        }
        ResetChangedBuildings();
        foreach (var building in buildings) {
            var randomI = RandomManager.rnd.Next(0, states.Count);
            building.SetState(states[randomI]);
        }
        return (1, list);
    }


    public int UpdateLine() {
        if (DidChange()) {
            ResetChangedBuildings();
            var res = UpdateBuildings(true);
            UpdateOlds();
            return res;
        } else {
            return UpdateBuildings(false);
        }
    }

    void SetOutline(bool active) {
        for (int i = 0; i < buildings.Count; i++) {
            buildings[i].SetOutline(active, false);
        }
    }

    public void SetActive(bool active, bool moveable) {
        if (deleted) return;
        foreach (var point in linePoints) {
            point.SetActive(active);
        }
        moveable = active && moveable;
        foreach (var point in linePoints) {
            point.SetMoveable(moveable);
        }
        SetOutline(active && moveable);
    }

    public void Select(bool selected) {
        if (deleted) return;
        foreach (var point in linePoints) {
            point.Select(selected);
        }
        lr.enabled = linePoints.Count > 1 && selected;
    }

    void UpdateOlds() {
        oldLinePoints.Clear();
        oldDividingPoints.Clear();
        foreach (var point in linePoints) {
            point.UpdateOlds();
            oldLinePoints.Add(point.GetPoint());
            if (point.dividing) oldDividingPoints.Add(point);
        }
        oldBuildingsCount = buildings.Count;
        state.FlagAsUnchanged();
    }

    public int GetPointCount() {
        return linePoints.Count;
    }

    public TerrainPoint AddPoint(GameObject obj, Vector3 point, bool pointOnly = false) {
        History.UndoManager.Touch(this);
        return AddTerrainPointToSequence.AddPoint(linePoints, ref curAnchor, container, this,
            delegate {
                var lastP = linePoints[linePoints.Count - 1];
                if (lastP.anchor is TerrainAnchor ta && !GO.IsNull(ta)) {
                    curAnchor = ta;
                } else {
                    curAnchor = null;
                }
            },
            delegate {
                lr.enabled = linePoints.Count > 1;
                ReloadLine();
            },
            delegate {
                state.SetBool("projectToGround", false);
            }, obj, point, pointOnly);
    }

    public bool ContainsPoint(TerrainPoint p) {
        return linePoints.Contains(p);
    }

    void ResetChangedBuildings() {
        if (linePoints.Count < 2) {
            foreach (var building in buildings) {
                building.Delete();
            }
            buildings.Clear();
        } else {
            var looping = state.Bool("loop") && linePoints.Count > 2;
            //get the split points
            var splitPoints = new List<TerrainPoint>();
            splitPoints.Add(linePoints[0]);
            for (int i = 1; i < linePoints.Count - 1; i++) {
                var p = linePoints[i];
                if (p.dividing) splitPoints.Add(p);
            }
            splitPoints.Add(linePoints[linePoints.Count - 1]);
            //remove the wrong ones
            var buildingsToDelete = new List<Building>();
            var indicesDone = new List<int>();
            foreach (var building in buildings) {
                var i1 = splitPoints.IndexOf(building.firstPoint);
                var i2 = splitPoints.IndexOf(building.lastPoint);
                if ((i1 == (i2 - 1) || (looping && i2 == 0 && i1 == splitPoints.Count - 1)) && !indicesDone.Contains(i1) && i1 != -1 && i2 != -1) {
                    indicesDone.Add(i1);
                } else {
                    buildingsToDelete.Add(building);
                }
            }
            foreach (var building in buildingsToDelete) {
                RemoveBuilding(buildings.IndexOf(building));
            }
            var indicesTodo = new List<int>();
            for (int i = 0; i < splitPoints.Count - 1 + (looping ? 1 : 0); i++) {
                if (!indicesDone.Contains(i)) indicesTodo.Add(i);
            }
            //rebuild the gaps and sort the list
            foreach (var i in indicesTodo) {
                var i2 = i + 1;
                if (i2 >= splitPoints.Count) i2 = 0;
                AddBuilding(splitPoints[i], splitPoints[i2]);
            }
            buildings.Sort((x, y) => linePoints.IndexOf(x.firstPoint).CompareTo(linePoints.IndexOf(y.firstPoint)));
        }
    }

    /*void SetCurAnchor(int lastI, TerrainAnchor anchor) {
        if (lastI >= 0) {
            linePoints[lastI].SetBig(false);
            if (anchor != null) {
                linePoints[linePoints.Count - 1].SetBig(true);
            }
        }
        curAnchor = anchor;
    }*/

    void ReloadLine() {
        ReloadType();
        lr.positionCount = linePoints.Count;
        var points = new List<Vector3>();
        foreach (var p in linePoints) {
            points.Add(p.GetPoint());
        }
        var meshes = new List<SubMeshData>();
        foreach (var b in buildings) {
            meshes.AddRange(b.GetSubmesh());
        }
        if (roof != null) meshes.Add(roof.GetSubmesh());
        GameEngine.Mesh.Clear(m);
        if (meshes.Count > 0) {
            var smd = GeometryHelpers.SubMesh.MergeSubmeshes(meshes, V3.zero, V3.one, 0);
            GeometryHelpers.SubMesh.SetMeshData(smd, m, mr);
        }
        lr.SetPositions(points.ToArray());
    }

    public bool IsProjectedToGround() {
        return state.Bool("projectToGround");
    }

    public void RemoveLastLinePoint() {
        if (linePoints.Count < 1) return;
        History.UndoManager.Touch(this);
        var lastI = linePoints.Count - 1;
        var p = linePoints[lastI];
        p.DeleteManual(this);
        p.dividing = false;
        p.Select(false);
        lr.enabled = linePoints.Count > 1;
        ReloadLine();
    }

    public void RemoveLastPoint() {
        if (linePoints.Count > 0) RemovePoint(linePoints[linePoints.Count - 1]);
    }

    public bool RemovePoint(object point) {
        if (point is TerrainPoint bp) {
            History.UndoManager.Touch(this);
            return linePoints.Remove(bp);
        } else {
            return false;
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;
using SubMeshData = GeometryHelpers.SubMesh.SubMeshData;
using RC = RuntimeCalculator;

public class BuildingLine : MonoBehaviour, IGroundable, IObjectWithState {
    List<TerrainPoint> linePoints = new List<TerrainPoint>();
    public List<Building> buildings = new List<Building>();
    public List<BuildingSideGenerator> buildingSides = new List<BuildingSideGenerator>();
    public ObjectState state;
    bool deleted = false;
    public TerrainSubmeshGenerator roof;
    public ObjectState stateForNewBuildings;

    public MeshRenderer mr;
    MeshFilter mf;
    public Mesh m;

    TerrainAnchor curAnchor = null;

    List<Vector3> oldLinePoints = new List<Vector3>();
    List<TerrainPoint> oldDividingPoints = new List<TerrainPoint>();
    int oldBuildingsCount;
    public LineRenderer lr;
    public GameObject container;
    CityElements.Types.Runtime.Buildings.BuildingType.Line curType;
    RC.VariableContainer variableContainer;

    public List<ObjectState> forcedBuildingStates = null;
    public List<ObjectState> forcedSideStates = null;

    public void Initialize(GameObject container) {
        m = new Mesh();
        mr = gameObject.AddComponent<MeshRenderer>();
        mf = gameObject.AddComponent<MeshFilter>();
        mr.sharedMaterial = MaterialManager.GetMaterial("_TRANSPARENT_", true);
        mf.sharedMesh = m;
        state = PresetManager.GetPreset("buildingLine", 0);
        stateForNewBuildings = PresetManager.GetPreset("building", 0);
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
        curType.FillInitialVariables(variableContainer, state);
    }

    public CityElements.Types.Runtime.Buildings.BuildingType.Line GetLineType() {
        var dict = CityElements.Types.Parsers.TypeParser.GetBuildingTypes();
        return dict[state.Str("type", null)].line;
    }

    void AddBuilding(TerrainPoint firstPoint, TerrainPoint lastPoint) {
        var b = new Building();
        b.container = gameObject;
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
        lr = gameObject.AddComponent<LineRenderer>();
        lr.material = Resources.Load<Material>("Materials/Line");
        lr.startWidth = 0.5f;
        lr.endWidth = 0.5f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.material = MaterialManager.GetHandleMaterial((Color.red + Color.yellow) * 0.5f);
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
        Clear();
        Destroy(gameObject);
        if (roof != null) roof.Delete();
        deleted = true;
    }

    private void LateUpdate() {
        foreach (var b in buildings) {
            b.LateUpdate();
        }
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
            pointsToDelete.Remove(point);
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
            if (i == 0 || (input[i].point - res[res.Count - 1].point).magnitude > GeometryHelper.epsilon) {
                res.Add(input[i]);
            }
            splineMap.Add(res.Count - 1);
        }
        return (res, splineMap);
    }

    bool FillSplineNormals(List<(Vector3 point, Vector3 normal)> input) {
        bool reverse = false;
        for (int i = 0; i < input.Count; i++) {
            var d1 = i == 0 ? Vector3.zero : (input[i].point - input[i - 1].point);
            var d2 = i >= (input.Count - 1) ? Vector3.zero : (input[i + 1].point - input[i].point);
            var d = (d1.normalized + d2.normalized).normalized;
            var n = Vector3.Cross(d, Vector3.up).normalized;
            if (input[i].normal == Vector3.zero) {
                input[i] = (input[i].point, n);
            } else {
                var mult = Mathf.Sign(Vector3.Dot(input[i].normal, n));
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
            var normal = Vector3.zero;
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
                res[i] = (res[i].point, res[i].normal.normalized * -1);
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
            res[i] = (res[i].point, res[i].normal.normalized);
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
                if (p.y > maxH) maxH = p.y;
            }
            forcedHeight = maxH + state.Float("height");
            forcedMaxHeight = maxH;
            for (int i = 0; i < fullSpline.Count; i++) {
                var p = fullSpline[i].Item1;
                var p2 = new Vector3(p.x, forcedHeight, p.z);
                roofSpline.Add(p2);
            }

            var vc = variableContainer;
            if (roof != null && fullSpline.Count > 2) roof.UpdateMesh(roofSpline, state.Str("roofTex"), vc.floats[vc.floatIndex["uMult"]], vc.floats[vc.floatIndex["vMult"]]);
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
                buildings[i].state = forcedBuildingStates[i];
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
            var dir = (p1.point - p0.point).normalized;
            var n = p0.normal.normalized;
            var cross = Vector3.Cross(dir, n);
            clockwise = cross.y > 0;
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
            linePoints.Add(TerrainPoint.Create(anchor.gameObject.transform.position, this, container, anchor));
        }

        void AddAnchorLinkPoint(PointLink linkObj, float percent, bool subdivide = true) {
            AddPoint(linkObj.gameObject, Vector3.Lerp(linkObj.gameObject.transform.position, linkObj.next.transform.position, percent));
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
            var dist = Vector3.Distance(start.gameObject.transform.position, end.gameObject.transform.position);
            var distances = GetRandomDistances(dist);
            if (link.next == start.gameObject) distances.Reverse();
            foreach (var d in distances) {
                AddAnchorLinkPoint(link, d);
            }
        }

        void AddRandomPointsToFirstAndLastLine(PointLink link, float startPercent, float endPercent, bool keepLast) {
            var p1 = link.gameObject.transform.position;
            var p2 = link.next.transform.position;
            var startP = Vector3.Lerp(p1, p2, startPercent);
            var endP = Vector3.Lerp(p1, p2, endPercent);
            var dist = Vector3.Distance(endP, startP);
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
        var lineAnchor = (LineAnchor)linePoints[0].anchor;
        var startAnchor = lineAnchor.start.GetComponent<TerrainAnchor>();
        var endAnchor = lineAnchor.end.GetComponent<TerrainAnchor>();
        if (startAnchor == null || endAnchor == null) return (0, null);
        var list = endAnchor.GetPointListTo(startAnchor, true);
        list.Insert(0, endAnchor);
        if (list.Count <= 1) return (0, null);
        var linkObj = lineAnchor.start.GetComponentInChildren<PointLink>();
        if (linkObj == null) return (0, null);
        IAnchorable curAnchor = lineAnchor;
        foreach (var item in list) {
            if (subdivide) {
                if (curAnchor is LineAnchor lA) { //first segment
                    AddRandomPointsToFirstAndLastLine(linkObj, lineAnchor.percent, 1, false);
                } else if (curAnchor is TerrainAnchor tA) { //middle segments
                    if (!GeometryHelper.AreVectorsEqual(curAnchor.GetPosition(), item.gameObject.transform.position)) {
                        var link2 = tA.gameObject.GetComponentInChildren<PointLink>();
                        var link3 = item.gameObject.GetComponentInChildren<PointLink>();
                        if (link2 != null && link2.next == item.gameObject) {
                            AddRandomPointsToMiddleLine(link2, tA, item);
                        } else if (link3 != null && link3.next == tA.gameObject) {
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
            if (GeometryHelper.AreVectorsEqual(pPrev.transform.position, pCur.transform.position)) {
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
            var vecTo = pNext.GetPoint() - pCur.transform.position;
            var vecFrom = pPrev.GetPoint() - pCur.transform.position;
            var angle = 180.0f - Vector3.Angle(vecFrom, vecTo);
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
            var vecTo = pNext.GetPoint() - pCur.transform.position;
            var vecFrom = pPrev.GetPoint() - pCur.transform.position;
            var angle = 180.0f - Vector3.Angle(vecFrom, vecTo);
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
        return AddTerrainPointToSequence.AddPoint(linePoints, ref curAnchor, container, this,
            delegate {
                var lastP = linePoints[linePoints.Count - 1];
                if (lastP.anchor is TerrainAnchor ta) {
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
        m.Clear();
        if (meshes.Count > 0) {
            var smd = GeometryHelpers.SubMesh.MergeSubmeshes(meshes, Vector3.zero, Vector3.one, 0);
            GeometryHelpers.SubMesh.SetMeshData(smd, m, mr);
        }
        lr.SetPositions(points.ToArray());
    }

    public bool IsProjectedToGround() {
        return state.Bool("projectToGround");
    }

    public void RemoveLastLinePoint() {
        if (linePoints.Count < 1) return;
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
            return linePoints.Remove(bp);
        } else {
            return false;
        }
    }
}
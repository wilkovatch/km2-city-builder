using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;
using RC = RuntimeCalculator;

public class TerrainPatch : MonoBehaviour, IGroundable, IObjectWithState {
    List<TerrainPoint> perimeterPoints = new List<TerrainPoint>();
    List<TerrainPoint> internalPoints = new List<TerrainPoint>();
    public TerrainPatchGenerator generator;
    bool deleted = false;

    Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
    TerrainAnchor curAnchor = null;
    CityElements.Types.Runtime.TerrainPatchType curType;
    RC.VariableContainer variableContainer;

    List<TerrainBorderMesh> borderMeshes = new List<TerrainBorderMesh>();

    public ObjectState state;
    List<Vector3> oldPerimeterPoints = new List<Vector3>();
    List<Vector3> oldInternalPoints = new List<Vector3>();
    int oldPointCount, oldInternalPointCount, oldBorderMeshCount;

    GameObject container;

    public void Initialize(GameObject container, GameObject parent) {
        this.container = container;
        transform.parent = parent.transform;
        state = new ObjectState();
        state.SetStr("texture", PreferencesManager.Get("curTerrainTexture", ""));
        state.SetBool("projectToGround", true);
        state.SetInt("smooth", 0);
        var typeDict = CityElements.Types.Parsers.TypeParser.GetTerrainPatchTypes();
        var type = "";
        foreach (var t in typeDict.Keys) { //get the first key
            type = t;
            break;
        }
        state.SetStr("type", type);
        generator = gameObject.AddComponent<TerrainPatchGenerator>();
        generator.Initialize();
    }

    public ObjectState GetState() {
        return state;
    }

    public void AddBorderMesh(ObjectState state) {
        var borderMesh = new TerrainBorderMesh();
        borderMesh.state = state;
        borderMeshes.Add(borderMesh);
    }

    public void SetBorderMeshState(int i, ObjectState newState) {
        if (i < 0 || i >= borderMeshes.Count) return;
        borderMeshes[i].state = newState;
    }

    public void RemoveBorderMesh(int i) {
        if (i < 0 || i >= borderMeshes.Count) return;
        borderMeshes.RemoveAt(i);
    }

    public int GetBorderMeshCount() {
        return borderMeshes.Count;
    }

    public TerrainBorderMesh GetBorderMesh(int i) {
        if (i < 0 || i >= borderMeshes.Count) return null;
        return borderMeshes[i];
    }

    void AddPointToBorderMeshSorted(int i, TerrainPoint point) {
        var seg = borderMeshes[i].segment;
        if (seg.Count == 0) {
            seg.Add(point);
            return;
        }
        var lastPoint = seg[seg.Count - 1];
        var firstPoint = seg[0];
        var lastIndex = perimeterPoints.IndexOf(lastPoint);
        var firstIndex = perimeterPoints.IndexOf(firstPoint);
        var next = lastIndex + 1;
        if (next >= perimeterPoints.Count) next = 0;
        var prev = firstIndex - 1;
        if (prev < 0) prev = perimeterPoints.Count - 1;
        var newIndex = perimeterPoints.IndexOf(point);
        if (newIndex == next) {
            seg.Add(point);
        } else if (newIndex == prev) {
            seg.Insert(0, point);
        }
    }

    public void AddPointToBorderMesh(int i, TerrainPoint point) {
        if (!perimeterPoints.Contains(point)) return;
        if (i < 0 || i >= borderMeshes.Count || point == null) return;
        var sCount = borderMeshes[i].segment.Count;
        if (sCount > 0) {
            var path1 = GetBorderMeshPath(borderMeshes[i].segment[sCount - 1], point, true);
            var path2 = GetBorderMeshPath(borderMeshes[i].segment[0], point, false);
            var path = (path1.Count < path2.Count) ? path1 : path2;
            foreach (var p in path) {
                AddPointToBorderMeshSorted(i, p);
            }
        } else {
            AddPointToBorderMeshSorted(i, point);
        }
    }

    public void HighlightBorderMesh(int i) {
        foreach (var point in perimeterPoints) {
            point.Select(i == -1);
        }
        if (i < 0 || i >= borderMeshes.Count) return;
        foreach (var point in borderMeshes[i].segment) {
            point.Select(true);
        }
    }

    List<TerrainPoint> GetBorderMeshPath(TerrainPoint start, TerrainPoint end, bool forward) {
        var res = new List<TerrainPoint>();
        var startIndex = perimeterPoints.IndexOf(start);
        if (!perimeterPoints.Contains(end)) return res;
        var increment = forward ? 1 : -1;
        if (startIndex >= 0) {
            TerrainPoint curPoint = null;
            var curIndex = startIndex;
            while (curPoint != end) {
                curIndex += increment;
                if (curIndex < 0) curIndex = perimeterPoints.Count - 1;
                if (curIndex >= perimeterPoints.Count) curIndex = 0;
                curPoint = perimeterPoints[curIndex];
                res.Add(curPoint);
            }
        }
        return res;
    }

    void HighlightCurPerimeterPoint() {
        if (perimeterPoints.Count == 0) return;
        foreach (var point in perimeterPoints) {
            point.SetBig(false);
        }
        var lastP = perimeterPoints[perimeterPoints.Count - 1];
        lastP.SetBig(true);
        if (lastP.anchor is TerrainAnchor ta) {
            curAnchor = ta;
        } else {
            curAnchor = null;
        }
        for (int i = 0; i < perimeterPoints.Count; i++) {
            var j = (i < perimeterPoints.Count - 1) ? (i + 1) : 0;
            PointLink.Create(perimeterPoints[i].gameObject, perimeterPoints[j].gameObject, 0.9f, this);
        }
    }

    void RecalculateBounds() {
        var minX = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxZ = float.MinValue;
        foreach (var point in perimeterPoints) {
            var p = point.GetPoint();
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }
        var midX = (minX + maxX) * 0.5f;
        var midY = (minZ + maxZ) * 0.5f;
        var sizX = Mathf.Abs(minX - maxX);
        var sizZ = Mathf.Abs(minZ - maxZ);
        bounds = new Bounds(new Vector3(midX, 0, midY), new Vector3(sizX, 1, sizZ));
    }

    public (int resultCode, List<TerrainAnchor> points) AutoClose() {
        if (perimeterPoints.Count != 1 || curAnchor == null) return (-1, null);
        var lastI = perimeterPoints.Count - 1;
        var list = curAnchor.GetPointListTo(curAnchor);
        if (list.Count > 0) list.RemoveAt(list.Count - 1);
        if (list.Count <= 1) return (0, null);
        foreach (var item in list) {
            perimeterPoints.Add(TerrainPoint.Create(item.gameObject.transform.position, this, container, item));
        }
        HighlightCurPerimeterPoint();
        RecalculateBounds();
        return (1, list);
    }

    public TerrainPoint AddPerimeterPoint(GameObject obj, Vector3 point, bool pointOnly = false) {
        return AddTerrainPointToSequence.AddPoint(perimeterPoints, ref curAnchor, container, this,
            delegate {
                borderMeshes.Clear();
            },
            delegate {
                generator.lr.enabled = perimeterPoints.Count > 2;
                HighlightCurPerimeterPoint();
                RecalculateBounds();
            },
            delegate {
                state.SetBool("projectToGround", false);
            }, obj, point, pointOnly);
    }

    public void AddInternalPoint(GameObject obj, Vector3 point, bool force = false) {
        var anchor = obj == null ? null : obj.GetComponent<TerrainAnchor>();
        if (anchor == null) {
            var oldPoint = obj == null ? null : obj.GetComponent<TerrainPoint>();
            if (perimeterPoints.Contains(oldPoint)) return;
            if (oldPoint == null) {
                var p = TerrainPoint.Create(point, this, container);
                internalPoints.Add(p);
            } else if (force) {
                internalPoints.Add(oldPoint);
                oldPoint.AddLink(this);
            }
        }
    }

    public void RemovePerimeterPoint() {
        if (perimeterPoints.Count < 1) return;
        var lastI = perimeterPoints.Count - 1;
        perimeterPoints[lastI].DeleteManual(this);
        generator.lr.enabled = perimeterPoints.Count > 2;
        borderMeshes.Clear();
        HighlightCurPerimeterPoint();
    }

    public bool RemovePerimeterPoint(TerrainPoint point) {
        var res = perimeterPoints.Remove(point);
        if (res) borderMeshes.Clear();
        HighlightCurPerimeterPoint();
        return res;
    }

    public bool RemoveInternalPoint(TerrainPoint point) {
        return internalPoints.Remove(point);
    }

    void UpdateMesh() {
        if (deleted) return;
        if (perimeterPoints.Count <= 2) {
            generator.Clear();
        } else {
            ReloadType();
            var vc = variableContainer;
            var uMult = vc.floats[vc.floatIndex["uMult"]];
            var vMult = vc.floats[vc.floatIndex["vMult"]];
            generator.RebuildMesh(GetPerimeterPoints(), GetInternalPoints(), GetBorderMeshes(), state.Str("texture"), state.Int("smooth"), uMult, vMult);
        }
    }

    List<(List<Vector3>, List<Vector3>, ObjectState)> GetBorderMeshes() {
        var res = new List<(List<Vector3>, List<Vector3>, ObjectState)>();
        for (int i = 0; i < borderMeshes.Count; i++) {
            var lefts = new List<Vector3>();
            var points = new List<Vector3>();
            foreach (var point in borderMeshes[i].segment) {
                points.Add(point.GetPoint());
            }
            if (!GeometryHelper.IsPolygonClockwise(GetPerimeterPoints())) {
                points.Reverse();
            }
            for (int j = 0; j < points.Count; j++) {
                var dirPrev = (j == 0) ? Vector3.zero : (points[j] - points[j - 1]).normalized;
                var dirNext = (j == points.Count - 1) ? Vector3.zero : (points[j + 1] - points[j]).normalized;
                var dir = dirPrev + dirNext;
                if (j > 0 && j < points.Count - 1) dir *= 0.5f;
                var left = Vector3.Cross(dir, Vector3.up).normalized;
                lefts.Add(left);
            }
            res.Add((points, lefts, borderMeshes[i].state));
        }
        return res;
    }

    public List<TerrainBorderMesh> GetTerrainBorderMeshes() {
        var res = new List<TerrainBorderMesh>();
        res.AddRange(borderMeshes);
        return res;
    }

    public void SetActive(bool active, bool moveable) {
        if (deleted) return;
        moveable = active && moveable;
        foreach (var point in perimeterPoints) {
            point.SetActive(active);
            point.SetMoveable(moveable);
        }
        foreach (var point in internalPoints) {
            point.SetActive(active);
            point.SetMoveable(moveable);
        }
    }

    public void Select(bool selected) {
        if (deleted) return;
        foreach (var point in perimeterPoints) {
            point.Select(selected);
        }
        foreach (var point in internalPoints) {
            point.Select(selected);
        }
        generator.lr.enabled = selected;
    }

    public void Clear() {
        if (deleted) return;
        foreach (var point in perimeterPoints) {
            point.RemoveLink(this);
        }
        perimeterPoints.Clear();

        foreach (var point in internalPoints) {
            point.RemoveLink(this);
        }
        internalPoints.Clear();
    }

    public void Delete() {
        Clear();
        Destroy(gameObject);
        deleted = true;
    }

    public bool IsDeleted() {
        return deleted;
    }

    public List<Vector3> GetPerimeterPoints() {
        var res = new List<Vector3>();
        for (int i = 0; i < perimeterPoints.Count; i++) {
            res.Add(perimeterPoints[i].GetPoint());
        }
        return res;
    }

    public List<Vector3> GetInternalPoints() {
        var res = new List<Vector3>();
        for (int i = 0; i < internalPoints.Count; i++) {
            res.Add(internalPoints[i].GetPoint());
        }
        return res;
    }

    public List<TerrainPoint> GetPerimeterPointsComponents() {
        var res = new List<TerrainPoint>();
        res.AddRange(perimeterPoints);
        return res;
    }

    public List<TerrainPoint> GetInternalPointsComponents() {
        var res = new List<TerrainPoint>();
        res.AddRange(internalPoints);
        return res;
    }

    bool DidChange() {
        var perimPointsToDelete = new List<TerrainPoint>();
        var internalPointsToDelete = new List<TerrainPoint>();
        foreach (var point in perimeterPoints) {
            if (point.IsDeleted()) {
                perimPointsToDelete.Add(point);
            } else {
                point.UpdatePosition();
            }
        }
        foreach (var point in internalPoints) {
            if (point.IsDeleted()) {
                internalPointsToDelete.Add(point);
            } else {
                point.UpdatePosition();
            }
        }
        foreach (var point in perimPointsToDelete) {
            perimeterPoints.Remove(point);
        }
        foreach (var point in internalPointsToDelete) {
            internalPoints.Remove(point);
        }
        if (state.HasChanged()) return true;
        if (oldInternalPointCount != internalPoints.Count) return true;
        if (oldPointCount != perimeterPoints.Count) return true;
        for (int i = 0; i < perimeterPoints.Count; i++) {
            if (!GeometryHelper.AreVectorsEqual(perimeterPoints[i].GetPoint(), oldPerimeterPoints[i])) return true;
        }
        for (int i = 0; i < internalPoints.Count; i++) {
            if (!GeometryHelper.AreVectorsEqual(internalPoints[i].GetPoint(), oldInternalPoints[i])) return true;
        }
        if (borderMeshes.Count != oldBorderMeshCount) return true;
        foreach (var borderMesh in borderMeshes) {
            if (borderMesh.DidChange()) return true;
        }
        return false;
    }

    public CityElements.Types.Runtime.TerrainPatchType GetTerrainPatchType() {
        var dict = CityElements.Types.Parsers.TypeParser.GetTerrainPatchTypes();
        return dict[state.Str("type", null)];
    }

    void ReloadType() {
        var type = GetTerrainPatchType();
        if (type != curType) {
            variableContainer = GetTerrainPatchType().variableContainer.GetClone();
            curType = type;
        }
        curType.FillInitialVariables(variableContainer, state);
        curType.FillStaticVariables(variableContainer);
    }

    public int UpdatePatch() {
        if (DidChange()) {
            UpdateMesh();
            UpdateOlds();
            return 1;
        } else {
            return 0;
        }
    }

    void UpdateOlds() {
        oldInternalPoints.Clear();
        oldPerimeterPoints.Clear();
        foreach (var point in perimeterPoints) {
            point.UpdateOlds();
            oldPerimeterPoints.Add(point.GetPoint());
        }
        foreach (var point in internalPoints) {
            point.UpdateOlds();
            oldInternalPoints.Add(point.GetPoint());
        }
        foreach (var borderMesh in borderMeshes) {
            borderMesh.UpdateOlds();
        }
        state.FlagAsUnchanged();
        oldPointCount = perimeterPoints.Count;
        oldInternalPointCount = internalPoints.Count;
        oldBorderMeshCount = borderMeshes.Count;
    }

    public int GetPerimeterPointCount() {
        return perimeterPoints.Count;
    }

    public bool IsPointInside(TerrainAnchor point) {
        var pos = point.transform.position;
        if (!bounds.Contains(new Vector3(pos.x, 0, pos.z))) return false;
        var list = new List<Vector3>();
        foreach (var p in perimeterPoints) {
            if (AddTerrainPointToSequence.AnchorEquals(p.anchor, point)) return false;
            list.Add(p.GetPoint());
        }
        return GeometryHelper.IsPointInsidePolygon(pos, list);
    }

    public int AutoGenerateInternalPoints(float distance) {
        if (deleted) return -2;
        if (internalPoints.Count > 0) return -1;
        var pPoints = GetPerimeterPoints();
        var squaredDistance = distance * distance;
        var min = bounds.min;
        var sampler = new PoissonDiscSampler(bounds.size.x, bounds.size.z, distance);
        var samples = sampler.Samples();
        foreach (var sample in samples) {
            var sample2 = sample + new Vector2(min.x, min.z);
            var p = new Vector3(sample2.x, 0, sample2.y);
            if (GeometryHelper.IsPointInsidePolygon(p, pPoints) && GeometryHelper.PointPolygonSquaredDistance(p, pPoints) > squaredDistance) {
                AddInternalPoint(null, p);
            }
        }
        return 1;
    }

    public bool IsProjectedToGround() {
        return state.Bool("projectToGround");
    }

    public bool RemovePoint(object point) {
        if (point is TerrainPoint tpPoint) {
            return RemoveInternalPoint(tpPoint) || RemovePerimeterPoint(tpPoint);
        } else {
            return false;
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using Bounds = Godot.Aabb;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using States;
using RC = RuntimeCalculator;

public partial class TerrainPatch : Scriptable, IGroundable, IObjectWithState {
    List<TerrainPoint> perimeterPoints = new List<TerrainPoint>();
    List<TerrainPoint> internalPoints = new List<TerrainPoint>();
    public TerrainPatchGenerator generator;
    bool deleted = false;

    Bounds bounds = GameEngine.Bounds.Create(GameEngine.Vector3.zero, GameEngine.Vector3.zero);
    TerrainAnchor curAnchor = null;
    CityElements.Types.Runtime.TerrainPatchType curType;
    RC.VariableContainer variableContainer;

    public ObjectState state;
    List<Vector3> oldPerimeterPoints = new List<Vector3>();
    List<Vector3> oldInternalPoints = new List<Vector3>();
    int oldPointCount, oldInternalPointCount;

    GameObject container;

    public void Initialize(GameObject container, GameObject parent) {
        this.container = container;
        GO.SetParent(this, parent);
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
        var generatorObj = GS.Create<TerrainPatchGenerator>(null, this);
        generator = GS.Cast<TerrainPatchGenerator>(generatorObj);
        generator.Initialize();
    }

    public ObjectState GetState() {
        return state;
    }

    void HighlightCurPerimeterPoint() {
        if (perimeterPoints.Count == 0) return;
        foreach (var point in perimeterPoints) {
            point.SetBig(false);
        }
        var lastP = perimeterPoints[perimeterPoints.Count - 1];
        lastP.SetBig(true);
        if (lastP.anchor is TerrainAnchor ta && !GO.IsNull(ta)) {
            curAnchor = ta;
        } else {
            curAnchor = null;
        }
        for (int i = 0; i < perimeterPoints.Count; i++) {
            var j = (i < perimeterPoints.Count - 1) ? (i + 1) : 0;
            PointLink.Create(perimeterPoints[i].GetObject(), perimeterPoints[j].GetObject(), 0.9f, this);
        }
    }

    void RecalculateBounds() {
        var minX = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxZ = float.MinValue;
        foreach (var point in perimeterPoints) {
            var p = point.GetPoint();
            if (p[0] < minX) minX = p[0];
            if (p[0] > maxX) maxX = p[0];
            if (p[2] < minZ) minZ = p[2];
            if (p[2] > maxZ) maxZ = p[2];
        }
        var midX = (minX + maxX) * 0.5f;
        var midY = (minZ + maxZ) * 0.5f;
        var sizX = Mathf.Abs(minX - maxX);
        var sizZ = Mathf.Abs(minZ - maxZ);
        bounds = GameEngine.Bounds.Create(new Vector3(midX, 0, midY), new Vector3(sizX, 1, sizZ));
    }

    public (int resultCode, List<TerrainAnchor> points) AutoClose() {
        if (perimeterPoints.Count != 1 || GO.IsNull(curAnchor)) return (-1, null);
        var lastI = perimeterPoints.Count - 1;
        var list = curAnchor.GetPointListTo(curAnchor);
        if (list.Count > 0) list.RemoveAt(list.Count - 1);
        if (list.Count <= 1) return (0, null);
        History.UndoManager.Touch(this);
        foreach (var item in list) {
            perimeterPoints.Add(TerrainPoint.Create( item.GetPoint(), this, container, item));
        }
        HighlightCurPerimeterPoint();
        RecalculateBounds();
        return (1, list);
    }

    public TerrainPoint AddPerimeterPoint(GameObject obj, Vector3 point, bool pointOnly = false) {
        History.UndoManager.Touch(this);
        return AddTerrainPointToSequence.AddPoint(perimeterPoints, ref curAnchor, container, this,
            null,
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
        History.UndoManager.Touch(this);
        var anchor = obj == null ? null : GS.FindRecursive<TerrainAnchor>(obj);
        if (anchor == null) {
            var oldPoint = obj == null ? null : GS.FindRecursive<TerrainPoint>(obj);
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
        History.UndoManager.Touch(this);
        var lastI = perimeterPoints.Count - 1;
        perimeterPoints[lastI].DeleteManual(this);
        generator.lr.enabled = perimeterPoints.Count > 2;
        HighlightCurPerimeterPoint();
    }

    public bool RemovePerimeterPoint(TerrainPoint point) {
        History.UndoManager.Touch(this);
        var res = perimeterPoints.Remove(point);
        HighlightCurPerimeterPoint();
        return res;
    }

    public bool RemoveInternalPoint(TerrainPoint point) {
        History.UndoManager.Touch(this);
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
            generator.RebuildMesh(GetPerimeterPoints(), GetInternalPoints(), state.Str("texture"), state.Int("smooth"), uMult, vMult);
        }
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
        History.UndoManager.Touch(this);
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
        History.UndoManager.Touch(this);
        Clear();
        GO.Destroy(this);
        deleted = true;
    }

    //removes the patch without deleting its points (used by undo/redo, which deletes them explicitly)
    public void RemoveForUndo() {
        foreach (var point in perimeterPoints) point.RemoveLinkSilently(this);
        foreach (var point in internalPoints) point.RemoveLinkSilently(this);
        perimeterPoints.Clear();
        internalPoints.Clear();
        GO.Destroy(this);
        deleted = true;
    }

    //sets the points and state (used by undo/redo)
    public void RestoreFromSnapshot(List<TerrainPoint> perimeter, List<TerrainPoint> internals, ObjectState newState) {

        var oldPoints = new HashSet<TerrainPoint>(perimeterPoints);
        oldPoints.UnionWith(internalPoints);
        var newPoints = new HashSet<TerrainPoint>(perimeter);
        newPoints.UnionWith(internals);
        foreach (var point in newPoints) {
            if (!oldPoints.Contains(point)) point.AddLink(this);
        }
        foreach (var point in oldPoints) {
            if (!newPoints.Contains(point)) point.RemoveLinkSilently(this);
        }
        perimeterPoints = new List<TerrainPoint>(perimeter);
        internalPoints = new List<TerrainPoint>(internals);
        state = newState;
        generator.lr.enabled = perimeterPoints.Count > 2;
        HighlightCurPerimeterPoint();
        RecalculateBounds();
        oldPointCount = -1; //forces the rebuild
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
        state.FlagAsUnchanged();
        oldPointCount = perimeterPoints.Count;
        oldInternalPointCount = internalPoints.Count;
    }

    public int GetPerimeterPointCount() {
        return perimeterPoints.Count;
    }

    public bool IsPointInside(TerrainAnchor point) {
        var pos = point.GetPoint();
        if (!GameEngine.Bounds.Contains(bounds, new Vector3(pos[0], 0, pos[2]))) return false;
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
        var min = GameEngine.Bounds.GetMin(bounds);
        var sampler = new PoissonDiscSampler(GameEngine.Bounds.GetSize(bounds)[0], GameEngine.Bounds.GetSize(bounds)[2], distance);
        var samples = sampler.Samples();
        foreach (var sample in samples) {
            var sample2 = sample + new Vector2(min[0], min[2]);
            var p = new Vector3(sample2[0], 0, sample2[1]);
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
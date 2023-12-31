using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;
using RC = RuntimeCalculator;

public class Road : MonoBehaviour, IObjectWithState {
    public bool forceRemesh = false;
    public List<Vector3> curvePoints = new List<Vector3>();
    public RoadGenerator generator;

    public List<GameObject> points = new List<GameObject>();
    public Intersection startIntersection = null, endIntersection = null;
    List<Vector3> cps = new List<Vector3>();
    public ObjectState state;
    public ObjectState instanceState;
    Vector3 start, end;
    RC.VariableContainer variableContainer;

    List<Vector3> old_cps = new List<Vector3>();
    List<Vector3> old_intersections = new List<Vector3>();
    Vector3 old_start, old_end;
    CityElements.Types.Runtime.RoadType curType;

    public TerrainAnchorLineManager anchorManager;

    public bool deleted = false;
    bool activeSelf = true;

    public void Initialize() {
        state = PresetManager.GetPreset("road", 0);
        instanceState = new ObjectState();
        generator = gameObject.AddComponent<RoadGenerator>();
        generator.Initialize(state);
        anchorManager = new TerrainAnchorLineManager(this);
    }

    public ObjectState GetState() {
        return state;
    }

    public void SetState(ObjectState newState) {
        state = newState;
    }

    public void SetRailState(ObjectState newState) {
        state.SetContainer(newState, "rail");
    }

    public void SetPointsMoveable(bool enabled) {
        foreach (var point in points) {
            point.layer = enabled ? 8 : 0;
        }
        if (startIntersection != null) startIntersection.point.layer = enabled ? 8 : 0;
        if (endIntersection != null) endIntersection.point.layer = enabled ? 8 : 0;
    }

    void ResetIntersectionPoint(GameObject point) {
        point.layer = 8;
        point.GetComponent<Handle>().SetScale(Vector3.one);
        point.GetComponent<Handle>().SetColor(Color.green);
    }

    void ShowIntersectionPoint(GameObject point) {
        point.SetActive(true);
        point.layer = 9;
        point.GetComponent<Handle>().SetScale(Vector3.one * 3);
        point.GetComponent<Handle>().SetColor(Color.cyan);
    }

    void MarkLastPoint() {
        ResetColors();
        if (points.Count > 0) points[points.Count - 1].GetComponent<Handle>().SetColor(Color.yellow);
    }

    void ResetColors() {
        foreach (var point in points) {
            point.GetComponent<Handle>().SetColor(Color.green);
        }
    }

    public void SetActive(bool active, bool activeIntersection = false) {
        activeSelf = active;
        generator.lr.enabled = active && points.Count > 0;
        generator.lanesRenderersContainer.SetActive(active);
        if (points.Count > 0) {
            ResetIntersectionPoint(points[0]);
            ResetIntersectionPoint(points[points.Count - 1]);
        }
        foreach (var point in points) {
            point.SetActive(active);
        }
        if (points.Count > 0 && activeIntersection) {
            if (startIntersection == null) ShowIntersectionPoint(points[0]);
            if (endIntersection == null) ShowIntersectionPoint(points[points.Count - 1]);
        }
        if (active) MarkLastPoint();

        if (startIntersection != null) startIntersection.SetActive(active, true);
        if (endIntersection != null) endIntersection.SetActive(active, true, true);
    }

    public void Delete() {
        deleted = true;
        DetachFromIntersection(true);
        DetachFromIntersection(false);
        Destroy(gameObject);
    }

    public void DetachFromIntersection(bool start) {
        if (start && startIntersection != null) {
            startIntersection.RemoveRoad(this);
            startIntersection = null;
        } else if (endIntersection != null) {
            endIntersection.RemoveRoad(this);
            endIntersection = null;
        }
    }

    public void AddPoint(Vector3 point, int position = -1) {
        if (endIntersection != null && position == -1) return;
        var newObj = Instantiate(Resources.Load<GameObject>("Handle"));
        newObj.transform.position = point;
        newObj.transform.SetParent(transform, true);
        newObj.layer = 8;
        generator.lr.enabled = activeSelf && points.Count > 0;
        if (position < 0 || position >= points.Count) {
            points.Add(newObj);
        } else {
            points.Insert(position, newObj);
        }
        MarkLastPoint();
    }

    public void RemovePoint(bool force = false) {
        if (points.Count < 3 && !force) {
            return;
        } else {
            var obj = points[points.Count - 1];
            points.RemoveAt(points.Count - 1);
            Destroy(obj);
            DetachFromIntersection(false);
            MarkLastPoint();
        }
    }

    void UpdateCurve() { //TODO: detect actual point/terrain update, instead of every frame
        if (points.Count == 0) return;
        if (state.Bool("project") || state.Bool("projectAll")) {
            foreach (var point in points) {
                point.transform.position = GeometryHelper.ProjectPoint(point.transform.position);
            }
        }
        cps.Clear();
        start = points[0].transform.position;
        for (int i = 1; i < points.Count - 1; i++) {
            cps.Add(points[i].transform.position);
        }
        end = points[points.Count - 1].transform.position;
    }

    float GetLineLength() {
        var curPoint = points[0];
        var dist = 0.0f;
        foreach(var point in points) {
            dist += Vector3.Distance(point.transform.position, curPoint.transform.position);
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
        var realName = curType.typeData.settings.getters[name];
        return curType.vector3Definitions[realName].GetValue(variableContainer);
    }

    public Vector2 GetStandardVec2(string name) {
        var realName = curType.typeData.settings.getters[name];
        return curType.vector2Definitions[realName].GetValue(variableContainer);
    }

    public float GetStandardFloat(string name) {
        var realName = curType.typeData.settings.getters[name];
        return curType.numberDefinitions[realName].GetValue(variableContainer);
    }

    public bool GetStandardBool(string name) {
        var realName = curType.typeData.settings.getters[name];
        return curType.boolDefinitions[realName].GetValue(variableContainer);
    }

    public string GetStandardString(string name) {
        var realName = curType.typeData.settings.getters[name];
        return state.Str(realName);
    }

    public int GetSegments() {
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
        for (int i = 0; i < segments; i++) {
            var hermiteTension = state.Float("hermiteTension", 0.5f);
            var subEq = state.Bool("subdivideEqually");
            var pos = GeometryHelper.GetPointOnCurve(start, cps, end, i / (segments - 1.0f), i, GetCurveType(), hermiteTension, subEq);
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
        curType.FillInitialVariables(variableContainer, state, instanceState, 0.0f, 0);
    }

    public void RebuildMesh() {
        generator.state = state;
        generator.instanceState = instanceState;
        generator.curType = GetRoadType();
        generator.hasStartIntersection = startIntersection != null;
        generator.hasEndIntersection = endIntersection != null;
        generator.segments = GetSegments();
        generator.cps = new List<Vector3>(cps);
        generator.start = start;
        generator.end = end;
        generator.valid = points.Count > 1;
        generator.startIntersectionCenter = null;
        generator.endIntersectionCenter = null;

        var isBezier = GetCurveType() == GeometryHelper.CurveType.Bezier;

        if (isBezier && startIntersection != null && endIntersection != null && generator.cps.Count == 1) //we need two intermediate control points, but we have just one
        {
            //Transform quadratic bézier curve to cubic
            generator.cps = new List<Vector3> {
                start + 2.0f / 3.0f * (cps[0] - start),
                end + 2.0f / 3.0f * (cps[0] - end)
            };
        }

        if (startIntersection != null) {
            var dir = curvePoints[1] - curvePoints[0];
            dir = new Vector3(dir.x, 0, dir.z);
            generator.start += dir.normalized * startIntersection.sizes[startIntersection.roads.IndexOf(this)];
            generator.startIntersectionCenter = startIntersection.point.transform.position;
        }
        if (endIntersection != null) {
            var dir = curvePoints[curvePoints.Count - 2] - curvePoints[curvePoints.Count - 1];
            dir = new Vector3(dir.x, 0, dir.z);
            generator.end += dir.normalized * endIntersection.sizes[endIntersection.roads.IndexOf(this)];
            generator.endIntersectionCenter = endIntersection.point.transform.position;
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
        anchorManager.Update(gameObject, generator.sidePoints, startConns, endConns, centerLines);

        UpdateOlds();
    }

    void RebuildIntersections() {
        if (startIntersection != null) startIntersection.forceRemesh = true;
        if (endIntersection != null) endIntersection.forceRemesh = true;
    }

    bool DidRoadChange() {
        var intersections = new List<Vector3>();
        if (startIntersection != null) intersections.Add(startIntersection.point.transform.position);
        if (endIntersection != null) intersections.Add(endIntersection.point.transform.position);
        if (old_intersections.Count != intersections.Count) return true;
        if (cps.Count != old_cps.Count) return true;
        for (int i = 0; i < cps.Count; i++) {
            if (!GeometryHelper.AreVectorsEqual(cps[i], old_cps[i])) return true;
        }
        if (!GeometryHelper.AreVectorsEqual(start, old_start)) return true;
        if (!GeometryHelper.AreVectorsEqual(end, old_end)) return true;
        if (state.HasChanged()) return true;
        if (instanceState.HasChanged()) return true;
        return false;
    }

    void UpdateOlds() {
        old_cps = new List<Vector3>(cps);
        old_intersections = new List<Vector3>();
        if (startIntersection != null) old_intersections.Add(startIntersection.point.transform.position);
        if (endIntersection != null) old_intersections.Add(endIntersection.point.transform.position);
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

    public void UpdateLine() {
        if (generator == null) return;
        UpdateCurve();
        if (DidRoadChange()) RebuildLine();
    }

    public int UpdateMesh() {
        if (forceRemesh) {
            RebuildMesh();
            forceRemesh = false;
            return 1;
        }
        return 0;
    }
}

using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using PhysicsScriptable = Godot.StaticBody3D;
using GameObject = Godot.Node3D;
using PointType = Godot.StaticBody3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using States;

public partial class Intersection: IObjectWithState {
    public PointType point;
    public GameObject geo;

    public List<Road> roads = new List<Road>();
    public List<float> sizes = new List<float>();
    public int minI = -1;

    public IntersectionGenerator generator;
    public ObjectState state, instanceState;
    public bool forceRemesh = false;
    bool deleted = false;

    Vector3 oldPointPos;
    int oldRoadCount = 0;
    List<GameObject> roadsThrough = new List<GameObject>();

    public TerrainAnchorLineManager anchorManager;

    public partial class IntersectionComponent : PhysicsScriptable {
        public Intersection intersection;
    }

    public Intersection(PointType point, GameObject container) {
        if (PresetManager.lastIntersection == null) PresetManager.lastIntersection = PresetManager.GetPreset("intersection", 0);
        state = (ObjectState)PresetManager.lastIntersection.Clone();
        instanceState = new ObjectState();
        this.point = point;
        oldPointPos = GO.GetPosition(point);
        geo = GS.Create<IntersectionGenerator>();
        if (container != null) GO.SetName(geo, "Intersection " + Actions.Helpers.GetLatestObjectNumber(container, "Intersection "));
        generator = GS.Cast<IntersectionGenerator>(geo);
        generator.Initialize(this);
        var compObj = GS.Create<IntersectionComponent>(null, point);
        GO.SetLayer(compObj, GO.GetLayer(point));
        var comp = GS.Cast<IntersectionComponent>(compObj);
        GO.SetParent(point, geo);
        comp.intersection = this;
        anchorManager = new TerrainAnchorLineManager(this);
    }

    public ObjectState GetState() {
        return state;
    }

    public void SetState(ObjectState newState) {
        PresetManager.lastIntersection = (ObjectState)newState.Clone();
        state = (ObjectState)newState.Clone();
    }

    public void SetActive(bool active, bool selected = false, bool last = false) {
        if (deleted) return;
        var color = selected ? last ? GameEngine.Color.yellow : GameEngine.Color.green : GameEngine.Color.cyan;
        var layer = selected ? 8 : 9;
        GO.SetLayer(point, layer);
        var handle = GS.Cast<Handle>(point);
        if (handle != null) {
            handle.SetHandleActive(active);
            handle.SetColor(color);
        }
    }

    public void RemoveFromRoads() {
        var tempRoads = new List<Road>(roads);
        foreach (var road in tempRoads) {
            var isStart = road.startIntersection == this;
            var isEnd = road.endIntersection == this;
            if (isStart) road.DetachFromIntersection(true);
            if (isEnd) road.DetachFromIntersection(false);
        }
    }

    public Vector3 GetDirectionToCenter(Road r, Vector3 center) {
        if (r.curvePoints.Count < 2) return GameEngine.Vector3.zero;
        var is1 = r.startIntersection == this;
        var is2 = r.endIntersection == this;
        int pI = is1 ? 1 : is2 ? (r.curvePoints.Count - 2) : -1;
        if (pI == -1) return GameEngine.Vector3.zero;
        var roadPoint = r.curvePoints[pI];
        var d = center - roadPoint;
        d = new Vector3(d[0], 0, d[2]);
        return d;
    }

    private float GetSize(Vector3 v1, Vector3 v2, float width1, float width2) {
        var angle = GameEngine.Vector3.Angle(v1, v2);
        var radAngle = angle * GameEngine.Math.deg2rad;
        var factor = 0.0f;
        if (angle <= 90) {
            factor = 0.5f * (width2 / Mathf.Sin(radAngle) + width1 * Mathf.Cos(radAngle) / Mathf.Sin(radAngle));
        } else {
            factor = width2 * Mathf.Cos(radAngle * 0.5f) / (2.0f * Mathf.Sin(radAngle * 0.5f));
        }
        return factor;
    }

    public List<GameObject> GetRoadsThrough() {
        return new List<GameObject>(roadsThrough);
    }

    public void RebuildRoads(Road caller) {
        if (state.HasChanged()) {
            forceRemesh = true; //TODO: ???
            state.FlagAsUnchanged();
        }
        var pos = GO.GetPosition(point);
        foreach (var road in roads) {
            if (road.state.Bool("project")) {
                pos = GeometryHelper.ProjectPoint(pos);
                break;
            }
        }
        if (caller == null) {
            var posDiff = pos - oldPointPos;
            if (GameEngine.Vector3.SqrMagnitude(posDiff) <= GeometryHelper.epsilon) {
                return;
            }
        } else {
            pos = caller.startIntersection == this ? GO.GetPosition(caller.points[0]) : GO.GetPosition(caller.points[caller.points.Count - 1]);
        }
        forceRemesh = true; //TODO: ???
        GO.SetPosition(point, pos);
        oldPointPos = pos;
        foreach (var road in roads) {
            if (road != caller) {
                var index = road.startIntersection == this ? 0 : (road.points.Count - 1);
                GO.SetPosition(road.points[index], pos);
            }
        }
    }

    public void RecalculateSize() {
        if (oldRoadCount != roads.Count) forceRemesh = true;
        if (!forceRemesh) return;
        forceRemesh = true;
        oldRoadCount = roads.Count;
        sizes = new List<float>();
        var pointCenter = GO.GetPosition(point);
        for (int i = 0; i < roads.Count; i++) {
            var maxSize = 0.0f;
            roads[i].forceRemesh = true;
            var d1 = GetDirectionToCenter(roads[i], pointCenter);
            for (int j = 0; j < roads.Count; j++) {
                if (i == j) continue;
                var d2 = GetDirectionToCenter(roads[j], pointCenter);
                var wI = roads[i].GetStandardFloat("totalWidth");
                var wJ = roads[j].GetStandardFloat("totalWidth");
                var thisSize = GetSize(d1, d2, wI, wJ);
                if (thisSize > maxSize) maxSize = thisSize;
            }
            sizes.Add(maxSize);
        }
        //check roads that still overlap
        int found = 1;
        int k = 0;
        while (found > 0 && k < 100) {
            k++;
            found = 0;
            for (int i = 0; i < roads.Count; i++) {
                var maxAdd = 0.0f;
                var r = roads[i];
                var dir = GameEngine.Vector3.Normalize(GetDirectionToCenter(r, pointCenter));
                var left = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(dir, GameEngine.Vector3.up));
                var roadCenter = pointCenter - dir * sizes[i];
                var roadLeft = roadCenter + left;
                var roadUp = roadCenter + GameEngine.Vector3.up;
                var p1 = GameEngine.Plane.Create(roadCenter, roadLeft, roadUp);
                int thisFound = 0;
                for (int j = 0; j < roads.Count; j++) {
                    if (i == j) continue;
                    var r2 = roads[j];
                    var dir2 = GameEngine.Vector3.Normalize(GetDirectionToCenter(roads[j], pointCenter));
                    var left2 = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(dir2, GameEngine.Vector3.up)) * 0.5f;
                    var road2Center = pointCenter - dir2 * sizes[j];
                    var w = r2.GetStandardFloat("totalWidth");
                    var road2Left = road2Center + left2 * w;
                    var road2Right = road2Center - left2 * w;
                    var add1 = GameEngine.Plane.DistanceToPoint(p1, road2Left);
                    var add2 = GameEngine.Plane.DistanceToPoint(p1, road2Right);
                    var add = Mathf.Max(add1, add2);
                    if (add > maxAdd) {
                        thisFound = 1;
                        maxAdd = add;
                    }
                }
                found += thisFound;
                sizes[i] += maxAdd;
            }
        }

        //add space for crosswalk
        for (int i = 0; i < roads.Count; i++) {
            sizes[i] += roads[i].GetCrosswalkSize(this) + roads[i].GetIntersectionAdd(this) + state.Float("sizeIncrease");
        }
    }

    public List<Road> GetRoadsSortedClockwise() {
        var res = new List<Road>();
        var roadCenters = new List<Vector3>();
        for (var i = 0; i < roads.Count; i++) {
            var dir = GameEngine.Vector3.Normalize(GetDirectionToCenter(roads[i], GO.GetPosition(point)));
            var roadCenter = GO.GetPosition(point) - dir * sizes[i];
            roadCenters.Add(roadCenter);
        }
        var sortOrder = GeometryHelper.SortClockwise(roadCenters, GO.GetPosition(point));
        foreach (var i in sortOrder) {
            res.Add(roads[i]);
        }
        return res;
    }

    public void RemoveRoad(Road road) {
        History.UndoManager.Touch(this);
        roads.Remove(road);
        if (roads.Count <= 1) Delete();
    }

    public bool IsDeleted() {
        return deleted;
    }

    public void Delete() {
        History.UndoManager.Touch(this);
        foreach (var roadThrough in roadsThrough) {
            GO.Destroy(roadThrough);
        }
        roadsThrough.Clear();
        GO.Destroy(point);
        GO.Destroy(geo);
        RemoveFromRoads();
        deleted = true;
    }

    List<int> GetStartAndEndRoads(ref int endRoad) {
        List<int> startRoads = new List<int>();
        for (int i = 0; i < roads.Count; i++) {
            var r = roads[i];
            if (r.startIntersection == this) {
                if (r.instanceState.Bool("startIntersectionStart")) {
                    startRoads.Add(i);
                }
                if (r.instanceState.Bool("startIntersectionEnd")) {
                    endRoad = i;
                }

            }
            if (r.endIntersection == this) {
                if (r.instanceState.Bool("endIntersectionStart")) {
                    startRoads.Add(i);
                }
                if (r.instanceState.Bool("endIntersectionEnd")) {
                    endRoad = i;
                }
            }
        }
        return startRoads;
    }

    List<List<Vector3>> UpdateRoadsThroughAndGetDirections(List<int> startRoads, bool isThroughRoad) {
        var throughDirections = new List<List<Vector3>>();
        if (isThroughRoad) {
            if (roadsThrough.Count < startRoads.Count) {
                for (int i = roadsThrough.Count; i < startRoads.Count; i++) {
                    //the component is a child of the generator
                    //(so that it can be found from the generator's collider, as for the intersection point)
                    var roadThrough = GS.Create<RoadGenerator>(null, geo);
                    GO.SetLayer(roadThrough, 7);
                    var comp = GS.Create<IntersectionComponent>(null, roadThrough);
                    GO.SetLayer(comp, 7);
                    comp.intersection = this;
                    var roadGenerator = GS.Cast<RoadGenerator>(roadThrough);
                    roadGenerator.curType = roads[startRoads[i]].GetRoadType();
                    roadGenerator.Initialize(roads[startRoads[i]].state);
                    roadGenerator.lr.enabled = false;
                    roadsThrough.Add(roadThrough);
                }
            } else if (roadsThrough.Count > startRoads.Count) {
                for (int i = startRoads.Count; i < roadsThrough.Count; i++) {
                    GO.Destroy(roadsThrough[i]);
                }
                for (int i = roadsThrough.Count - 1; i > startRoads.Count - 1; i--) {
                    roadsThrough.RemoveAt(i);
                }
            }
            for (int i = 0; i < startRoads.Count; i++) {
                var roadThrough = roadsThrough[i];
                var startRoad = startRoads[i];
                var roadGen = GS.Cast<RoadGenerator>(roadThrough);
                roadGen.state = (ObjectState)roads[startRoad].state.Clone();
                roadGen.instanceState = (ObjectState)roads[startRoad].instanceState.Clone();
                roadGen.runtimeState = (ObjectState)roads[startRoad].runtimeState.Clone();
                roadGen.runtimeState.SetBool("throughIntersection", true);
                roadGen.state.SetInt("curveType", (int)GeometryHelper.CurveType.Hermite);
                roadGen.state.SetBool("adjustLowPolyWidth", false);
                roadGen.segments = 2;
                roadGen.ResetSubGen();

                roadGen.cps.Clear();
                roadGen.valid = true;
                roadGen.PrecalculateVariables();
                throughDirections.Add(new List<Vector3>());
            }
        } else {
            foreach (var roadThrough in roadsThrough) {
                GO.Destroy(roadThrough);
            }
            roadsThrough.Clear();
        }
        return throughDirections;
    }

    public int RebuildMesh() {
        if (!forceRemesh) return 0;
        forceRemesh = false;

        int endRoad = -1;
        var startRoads = GetStartAndEndRoads(ref endRoad);

        bool isThroughRoad = startRoads.Count > 0 && endRoad != -1;
        var throughDirections = UpdateRoadsThroughAndGetDirections(startRoads, isThroughRoad);

        generator.RebuildMesh(roadsThrough, throughDirections, isThroughRoad, startRoads, endRoad);

        var centers = new List<List<Vector3>>();
        if (generator.sidePoints.Count > 0) {
            for (int j = 0; j < generator.sidePoints.Count; j++) {
                var centersJ = new List<Vector3>();
                for (int i = 0; i < generator.sidePoints[j].Count; i++) {
                    centersJ.Add(GO.GetPosition(point));
                }
                centers.Add(centersJ);
            }
        }

        anchorManager.Update(geo, generator.sidePoints, generator.sidePointStartRoads, generator.sidePointEndRoads, centers);

        foreach(var road in roads) {
            anchorManager.ConnectTo(road.anchorManager);
        }

        return 1;
    }
}

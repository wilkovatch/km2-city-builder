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
    public List<float> heights = new List<float>(); //of the ends of the roads relative to the point (see RecalculateHeights)
    public int minI = -1;

    public IntersectionGenerator generator;
    public ObjectState state, instanceState;
    public bool forceRemesh = false;
    bool deleted = false;

    Vector3 oldPointPos;
    int oldRoadCount = 0;
    bool oldSeamless = false;
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

    //the end of a road on the intersection, as seen from it (horizontally: a rolled end is narrower on the ground and
    //its vertices are moved vertically only, along its slope; a yawed end is skewed, its corners slide along its edges)
    public struct RoadMouth {
        public Vector3 dir; //toward the center, normalized and horizontal
        public Vector3 leftDir; //normalized and horizontal
        public Vector3 left; //half of the mouth per unit of width
        public float groundScale; //of the width (cos of the roll)
        public float slope; //height per meter along leftDir
        public float yaw; //radians
        public float throughRoll; //degrees, of a road through the intersection with its right vector along leftDir
    }

    public RoadMouth GetMouth(Road road, Vector3 center) {
        var res = new RoadMouth();
        res.dir = GameEngine.Vector3.Normalize(GetDirectionToCenter(road, center));
        res.leftDir = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(res.dir, GameEngine.Vector3.up));
        //the right vector of the road points to the left here at its end, to the right at its start
        var sign = road.startIntersection == this ? 1 : -1;
        var roll = road.GetEndRoll(this);
        var rollRad = roll * GameEngine.Math.deg2rad;
        res.yaw = road.GetEndYaw(this) * GameEngine.Math.deg2rad;
        res.groundScale = Mathf.Cos(rollRad);
        res.slope = sign * Mathf.Tan(rollRad);
        res.throughRoll = -sign * roll;
        if (res.yaw == 0) {
            res.left = res.leftDir * 0.5f * res.groundScale;
        } else {
            //the yawed right vector of the road is longer by 1/cos(yaw) (the road keeps its width), so its bank is steeper
            //across the road
            res.left = (res.leftDir + res.dir * Mathf.Tan(res.yaw)) * 0.5f * res.groundScale;
            res.slope /= Mathf.Cos(res.yaw);
        }
        return res;
    }

    // Seamless intersections: the two roads join without a surface between them, with the same direction at the joint

    //set on the intersection (kept with more than two roads, but without effect)
    public bool IsSeamlessSet() {
        return instanceState.Bool("seamless");
    }

    public bool IsSeamless() {
        return IsSeamlessSet() && roads.Count == 2 && roads[0] != roads[1];
    }

    //the point of the road next to its end on the intersection, null if the road has no other point
    Vector3? GetNeighborPoint(Road road) {
        if (road.points.Count < 2) return null;
        return GO.GetPosition(road.startIntersection == this ? road.points[1] : road.points[road.points.Count - 2]);
    }

    //the direction (normalized) toward which the end of the road on the intersection points
    Vector3? GetInwardDirection(Road road) {
        var neighbor = GetNeighborPoint(road);
        if (!neighbor.HasValue) return null;
        var end = GO.GetPosition(road.startIntersection == this ? road.points[0] : road.points[road.points.Count - 1]);
        var dir = end - neighbor.Value;
        if (GameEngine.Vector3.SqrMagnitude(dir) <= GeometryHelper.epsilon) return null;
        return GameEngine.Vector3.Normalize(dir);
    }

    //the tangent shared by the two roads at a seamless intersection, from the joint into the given road, from the control
    //points next to their ends (not the curves, so that the roads don't depend on each other): its direction is between
    //the directions of the ends (so that the sections of low-poly roads match), its length the distance between those
    //points, as the tangent of a Hermite curve through them (see Road.GetEndTangents). Null if the intersection isn't
    //seamless or the roads fold back on each other.
    public Vector3? GetSeamlessTangent(Road road) {
        if (!IsSeamless()) return null;
        var index = roads.IndexOf(road);
        if (index < 0) return null;
        var in0 = GetInwardDirection(roads[0]);
        var in1 = GetInwardDirection(roads[1]);
        if (!in0.HasValue || !in1.HasValue) return null;
        var flow = in0.Value - in1.Value; //from road 0 into road 1
        if (GameEngine.Vector3.Magnitude(flow) < 0.01f) return null;
        var length = GameEngine.Vector3.Distance(GetNeighborPoint(roads[0]).Value, GetNeighborPoint(roads[1]).Value);
        flow = GameEngine.Vector3.Normalize(flow) * length;
        return index == 0 ? -flow : flow;
    }

    //the roll (degrees) shared by the ends of the two roads at a seamless intersection, for the given road: the average of
    //their rolls, the one of the other road negated if the roads go the opposite way along the joint (end to end or start
    //to start), since the roll is relative to the direction of the road. No roll if a road type doesn't allow it.
    public float GetSeamlessRoll(Road road) {
        var index = roads.IndexOf(road);
        if (!IsSeamless() || index < 0) return 0.0f;
        var other = roads[1 - index];
        if (!road.IsRollAllowed() || !other.IsRollAllowed()) return 0.0f;
        var otherRoll = other.GetOwnEndRoll(other.startIntersection == this);
        return (road.GetOwnEndRoll(road.startIntersection == this) + (IsSameWay() ? otherRoll : -otherRoll)) * 0.5f;
    }

    //whether one of the two roads ends on the intersection and the other starts there
    bool IsSameWay() {
        return (roads[0].startIntersection == this) != (roads[1].startIntersection == this);
    }

    //gives the roll set on the end of the road to the end of the other one (see GetSeamlessRoll)
    public void SetSeamlessRoll(Road road, float roll) {
        var index = roads.IndexOf(road);
        if (!IsSeamless() || index < 0) return;
        var other = roads[1 - index];
        if (!other.IsRollAllowed()) return;
        other.SetOwnEndRoll(other.startIntersection == this, IsSameWay() ? roll : -roll);
    }

    //the height of the end of a road (by index) relative to the point
    public float GetConnectionHeight(int index) {
        return index >= 0 && index < heights.Count ? heights[index] : 0.0f;
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
        if (state.HasChanged() || instanceState.HasChanged()) {
            forceRemesh = true; //TODO: ???
            state.FlagAsUnchanged();
            instanceState.FlagAsUnchanged();
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
                //the intersection didn't move, but the end of one of its roads may have (the end handle replaces the one of
                //the intersection while the road is edited): the intersection follows it
                var movedEnd = GetMovedRoadEnd();
                if (!movedEnd.HasValue) {
                    RecordEndPositions();
                    return;
                }
                History.UndoManager.Touch(this);
                pos = movedEnd.Value;
            }
        } else {
            pos = caller.startIntersection == this ? GO.GetPosition(caller.points[0]) : GO.GetPosition(caller.points[caller.points.Count - 1]);
        }
        forceRemesh = true; //TODO: ???
        GO.SetPosition(point, pos);
        oldPointPos = pos;
        foreach (var road in roads) {
            if (road != caller) {
                foreach (var index in GetEndIndices(road)) GO.SetPosition(road.points[index], pos);
            }
        }
        RecordEndPositions();
    }

    //the last known positions of the ends of the roads on the intersection, to tell when one was moved
    readonly Dictionary<(Road road, int index), Vector3> endPositions = new Dictionary<(Road road, int index), Vector3>();

    List<int> GetEndIndices(Road road) {
        var res = new List<int>();
        if (road.points.Count == 0) return res;
        if (road.startIntersection == this) res.Add(0);
        if (road.endIntersection == this && road.points.Count > 1) res.Add(road.points.Count - 1);
        return res;
    }

    void RecordEndPositions() {
        endPositions.Clear();
        foreach (var road in roads) {
            foreach (var index in GetEndIndices(road)) endPositions[(road, index)] = GO.GetPosition(road.points[index]);
        }
    }

    //the position of a road end moved since the last update (only the ends seen before, so that a road just connected
    //or a city just loaded doesn't move the intersection). The ends of the roads projected on the terrain are compared
    //horizontally, their projection is not a move.
    Vector3? GetMovedRoadEnd() {
        foreach (var road in roads) {
            var projected = road.state.Bool("project") || road.state.Bool("projectAll");
            foreach (var index in GetEndIndices(road)) {
                var pos = GO.GetPosition(road.points[index]);
                if (!endPositions.TryGetValue((road, index), out var last)) continue;
                var diff = pos - last;
                if (projected) diff[1] = 0;
                if (GameEngine.Vector3.SqrMagnitude(diff) > GeometryHelper.epsilon) return pos;
            }
        }
        return null;
    }

    public void RecalculateSize() {
        if (oldRoadCount != roads.Count || oldSeamless != IsSeamless()) forceRemesh = true;
        if (!forceRemesh) return;
        forceRemesh = true;
        oldRoadCount = roads.Count;
        oldSeamless = IsSeamless();
        sizes = new List<float>();
        if (IsSeamless()) {
            //the roads end on the point, at its height
            heights = new List<float>();
            foreach (var road in roads) {
                road.forceRemesh = true;
                sizes.Add(0.0f);
                heights.Add(0.0f);
            }
            return;
        }
        var pointCenter = GO.GetPosition(point);
        var mouths = new List<RoadMouth>();
        var groundWidths = new List<float>(); //a rolled end is narrower on the ground
        var skews = new List<float>(); //how far the left corner of a yawed end is toward the center (the right one away)
        foreach (var road in roads) {
            var mouth = GetMouth(road, pointCenter);
            var groundWidth = road.GetStandardFloat("totalWidth") * mouth.groundScale;
            mouths.Add(mouth);
            groundWidths.Add(groundWidth);
            skews.Add(mouth.yaw == 0 ? 0.0f : 0.5f * groundWidth * Mathf.Tan(mouth.yaw));
        }
        for (int i = 0; i < roads.Count; i++) {
            var maxSize = 0.0f;
            roads[i].forceRemesh = true;
            var d1 = GetDirectionToCenter(roads[i], pointCenter);
            for (int j = 0; j < roads.Count; j++) {
                if (i == j) continue;
                var d2 = GetDirectionToCenter(roads[j], pointCenter);
                var thisSize = GetSize(d1, d2, groundWidths[i], groundWidths[j]);
                //the size is where the facing edges cross: the corner on the side of road j must reach it
                if (skews[i] != 0) thisSize += GameEngine.Vector3.Dot(-d2, mouths[i].leftDir) > 0 ? skews[i] : -skews[i];
                if (thisSize > maxSize) maxSize = thisSize;
            }
            //the corner toward the center can't pass the point
            if (skews[i] != 0) maxSize = Mathf.Max(maxSize, Mathf.Abs(skews[i]));
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
                var dir = mouths[i].dir;
                var left = mouths[i].leftDir;
                var roadCenter = pointCenter - dir * sizes[i];
                var roadLeft = roadCenter + left;
                var roadUp = roadCenter + GameEngine.Vector3.up;
                var p1 = GameEngine.Plane.Create(roadCenter, roadLeft, roadUp); //facing away from the center
                var yawTan = mouths[i].yaw == 0 ? 0.0f : Mathf.Tan(mouths[i].yaw);
                var halfWidth = groundWidths[i] * 0.5f;
                //how far behind the mouth a point is: a skewed mouth is closer to the center on its left, only along the
                //road (a point beside it is compared to its corner, as with a square mouth, otherwise the planes of two
                //skewed roads would push each other away endlessly)
                float Behind(Vector3 p) {
                    var d = GameEngine.Plane.DistanceToPoint(p1, p);
                    if (yawTan == 0) return d;
                    return d + Mathf.Clamp(GameEngine.Vector3.Dot(p - roadCenter, left), -halfWidth, halfWidth) * yawTan;
                }
                int thisFound = 0;
                for (int j = 0; j < roads.Count; j++) {
                    if (i == j) continue;
                    var dir2 = mouths[j].dir;
                    var left2 = mouths[j].leftDir * 0.5f;
                    if (mouths[j].yaw != 0) left2 += dir2 * (0.5f * Mathf.Tan(mouths[j].yaw));
                    var road2Center = pointCenter - dir2 * sizes[j];
                    var w = groundWidths[j];
                    var road2Left = road2Center + left2 * w;
                    var road2Right = road2Center - left2 * w;
                    var add1 = Behind(road2Left);
                    var add2 = Behind(road2Right);
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
        RecalculateHeights(pointCenter, mouths);
    }

    //the heights of the road ends: their connection height, plus for the automatic ones the height of the plane fitted
    //through the corners of the mouths of the other roads (the ones not automatic, so that they don't depend on each other)
    void RecalculateHeights(Vector3 pointCenter, List<RoadMouth> mouths) {
        heights = new List<float>();
        var points = new List<Vector3>(); //relative to the point
        var fixedRoads = new List<int>();
        for (int i = 0; i < roads.Count; i++) {
            if (roads[i].IsConnectionHeightAuto(this)) continue;
            fixedRoads.Add(i);
            var center = -mouths[i].dir * sizes[i];
            var corner = mouths[i].left * roads[i].GetStandardFloat("totalWidth");
            var h = roads[i].GetConnectionHeight(this);
            var rise = mouths[i].slope * GameEngine.Vector3.Dot(corner, mouths[i].leftDir);
            points.Add(center + corner + GameEngine.Vector3.up * (h + rise));
            points.Add(center - corner + GameEngine.Vector3.up * (h - rise));
        }
        var plane = FitHeightPlane(points);
        for (int i = 0; i < roads.Count; i++) {
            var height = roads[i].GetConnectionHeight(this);
            if (roads[i].IsConnectionHeightAuto(this) && fixedRoads.Count > 0) {
                var center = -mouths[i].dir * sizes[i];
                if (plane.HasValue) {
                    height += plane.Value[0] + plane.Value[1] * center[0] + plane.Value[2] * center[2];
                } else {
                    //a single road (or all the corners in line): its bank extended
                    var j = fixedRoads[0];
                    var centerJ = -mouths[j].dir * sizes[j];
                    height += roads[j].GetConnectionHeight(this) + mouths[j].slope * GameEngine.Vector3.Dot(center - centerJ, mouths[j].leftDir);
                }
            }
            heights.Add(height);
        }
    }

    //the plane y = a + b x + c z (least squares), null if the points don't define one
    static Vector3? FitHeightPlane(List<Vector3> points) {
        if (points.Count < 3) return null;
        double n = points.Count, sx = 0, sz = 0, sxx = 0, sxz = 0, szz = 0, sy = 0, sxy = 0, szy = 0;
        foreach (var p in points) {
            double x = p[0], y = p[1], z = p[2];
            sx += x; sz += z; sxx += x * x; sxz += x * z; szz += z * z;
            sy += y; sxy += x * y; szy += z * y;
        }
        double Det(double a1, double b1, double c1, double a2, double b2, double c2, double a3, double b3, double c3) {
            return a1 * (b2 * c3 - c2 * b3) - b1 * (a2 * c3 - c2 * a3) + c1 * (a2 * b3 - b2 * a3);
        }
        var det = Det(n, sx, sz, sx, sxx, sxz, sz, sxz, szz);
        //relative to the spread of the points, so that the test doesn't depend on the size of the intersection
        var spread = (sxx + szz) / n;
        if (spread <= 0 || System.Math.Abs(det) <= 1e-6 * n * n * n * spread * spread) return null;
        var a = Det(sy, sx, sz, sxy, sxx, sxz, szy, sxz, szz) / det;
        var b = Det(n, sy, sz, sx, sxy, sxz, sz, szy, szz) / det;
        var c = Det(n, sx, sy, sx, sxx, sxy, sz, sxz, szy) / det;
        return new Vector3((float)a, (float)b, (float)c);
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

        if (IsSeamless()) {
            UpdateRoadsThroughAndGetDirections(new List<int>(), false);
            generator.RebuildSeamless();
        } else {
            int endRoad = -1;
            var startRoads = GetStartAndEndRoads(ref endRoad);

            bool isThroughRoad = startRoads.Count > 0 && endRoad != -1;
            var throughDirections = UpdateRoadsThroughAndGetDirections(startRoads, isThroughRoad);

            generator.RebuildMesh(roadsThrough, throughDirections, isThroughRoad, startRoads, endRoad);
        }

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

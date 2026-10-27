using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using PhysicsScriptable = Godot.StaticBody3D;
using GameObject = Godot.Node3D;
using Mesh = Godot.ArrayMesh;
using MeshRenderer = Godot.MeshInstance3D;
using MeshCollider = Godot.CollisionShape3D;
using LineRenderer = GameEngine.LineRenderer;
using GO = GameEngine.GameObject;
using States;
using RC = RuntimeCalculator;
using GH = GeometryHelper;

public partial class RoadGenerator : PhysicsScriptable {
    class LaneInfo {
        public float pos;
        public RC.Vector3s.Vector startBound, endBound;
        public Color color;
        public string name;
        public RC.VariableContainer vars;
        public int component;

        public LaneInfo(float pos, RC.Vector3s.Vector startBound, RC.Vector3s.Vector endBound, Color color, string name, RC.VariableContainer vars, int component) {
            this.pos = pos;
            this.startBound = startBound;
            this.endBound = endBound;
            this.color = color;
            this.name = name;
            this.vars = vars;
            this.component = component;
        }
    }

    public ObjectState state, instanceState, runtimeState;
    public CityElements.Types.Runtime.RoadType curType;
    public bool hasStartIntersection = false;
    public bool hasEndIntersection = false;

    public int segments;
    public MeshRenderer mr;
    public Mesh m;
    MeshCollider mc;
    public LineRenderer lr;
    public GameObject lanesRenderersContainer;
    public GameObject propsContainer;
    //Godot sanitizes and deduplicates node names, so the export can't recover these from the prop object name
    public List<(string container, string meshName, GameObject obj)> props = new List<(string, string, GameObject)>();
    List<LineRenderer> lineRenderers = new List<LineRenderer>();

    List<List<Vector3>> lanes = new List<List<Vector3>>();
    List<LaneInfo> lanesInfo;
    Dictionary<string, (int startBoundIndex, int endBoundIndex)> propsLanesDict;

    int numMeshes;
    public List<Vector3> cps = new List<Vector3>();
    public List<Vector2> cpRotations = null; //yaw and roll of start, cps and end (none: no rotation)
    public Vector3 start, end;
    public Vector3? startIntersectionCenter, endIntersectionCenter;
    public Vector3? startTangent, endTangent; //imposed to the ends of the curve, their directions to their sections (seamless intersections)

    public List<List<Vector3>> sidePoints = new List<List<Vector3>>();

    public ElementPlacer.RoadPlacer placer = null;
    public bool valid = false;

    float totalLength;
    bool rolled; //some sections are rolled: they can't be made coplanar (a twisted road has no planar faces)
    float curLineSize = 0.1f;
    public int vertsPerSection;

    CityElements.Types.Runtime.RoadType oldType;

    public RoadLikeGenerator<CityElements.Types.RoadType> subGen;

    public void Initialize(ObjectState state) {
        lr = GO.CreateLineRenderer(this);
        lr.material = GameEngine.Resources.LoadMaterial("Materials/Line");
        lr.startWidth = 0.1f;
        lr.endWidth = 0.1f;
        lr.frameUpdate = Update; //the lanes are shown only with the road line (when the road is active)

        GO.SetLayer(this, 7);
        this.state = state;

        var mats = GetMaterialSet();
        (mr, m) = GameEngine.Mesh.CreateMesh(GO.GetObject(this), mats);
        numMeshes = mats.Length;
        subGen = new RoadLikeGenerator<CityElements.Types.RoadType>(state, instanceState, segments, numMeshes);

        mc = GameEngine.Mesh.CreateCollider(this, m);

        lanesRenderersContainer = GO.Create("LanesRenderers");
        GO.SetParent(lanesRenderersContainer, this);
        propsContainer = GO.Create("PropsContainer");
        GO.SetParent(propsContainer, this);
    }

    public void ResetSubGen() {
        ResetType();
        subGen.Reset(curType, state, instanceState, segments, numMeshes);
    }

    public void ResetType() {
        var newMats = GetMaterialSet();
        GameEngine.Mesh.AssignMaterials(mr, newMats);
        numMeshes = newMats.Length;
    }

    Material[] GetMaterialSet() {
        var list = new List<Material>();
        if (curType != null) {
            foreach (var tex in curType.typeData.settings.textures) {
                var mat = MaterialManager.GetMaterial(MaterialManager.GetTextureName(state, tex));
                list.Add(mat);
            }
        }
        return list.ToArray();
    }

    public Vector3 GetStandardVec3(string name) {
        return curType.GetStandardVec3(name, subGen.variableContainer);
    }

    public Vector2 GetStandardVec2(string name) {
        return curType.GetStandardVec2(name, subGen.variableContainer);
    }

    public float GetStandardFloat(string name) {
        return curType.GetStandardFloat(name, subGen.variableContainer);
    }

    public bool GetStandardBool(string name) {
        return curType.GetStandardBool(name, subGen.variableContainer);
    }

    public string GetStandardString(string name) {
        return curType.GetStandardString(name, state);
    }

    public Vector3 GetVec3(string name) {
        var vc = subGen.variableContainer;
        if (vc.vec3Index.ContainsKey(name)) return vc.vector3s[vc.vec3Index[name]];
        return curType.vector3Definitions[name].GetValue(subGen.variableContainer);
    }

    public Vector2 GetVec2(string name) {
        var vc = subGen.variableContainer;
        if (vc.vec2Index.ContainsKey(name)) return vc.vector2s[vc.vec2Index[name]];
        return curType.vector2Definitions[name].GetValue(subGen.variableContainer);
    }

    public float GetFloat(string name) {
        var vc = subGen.variableContainer;
        if (vc.floatIndex.ContainsKey(name)) return vc.floats[vc.floatIndex[name]];
        return curType.numberDefinitions[name].GetValue(subGen.variableContainer);
    }

    public bool GetBool(string name) {
        var vc = subGen.variableContainer;
        if (vc.floatIndex.ContainsKey(name)) return vc.floats[vc.floatIndex[name]] > 0;
        return curType.boolDefinitions[name].GetValue(subGen.variableContainer);
    }

    // A junction can import parameters that the road's type does not define (e.g. when falling back to the junction
    // of another road type), these tell whether the getters above can provide them.
    public bool HasVec3(string name) {
        return subGen.variableContainer.vec3Index.ContainsKey(name) || curType.vector3Definitions.ContainsKey(name);
    }

    public bool HasVec2(string name) {
        return subGen.variableContainer.vec2Index.ContainsKey(name) || curType.vector2Definitions.ContainsKey(name);
    }

    public bool HasFloat(string name) {
        return subGen.variableContainer.floatIndex.ContainsKey(name) || curType.numberDefinitions.ContainsKey(name);
    }

    public bool HasBool(string name) {
        return subGen.variableContainer.floatIndex.ContainsKey(name) || curType.boolDefinitions.ContainsKey(name);
    }

    //the forced directions (and rotations, yaw and roll) are of each section, see IntersectionGenerator
    public void Rebuild(List<Vector3> forcedDirections = null, List<Vector2> forcedRotations = null) {
        if (curType != oldType) ResetType();
        RebuildLine(forcedDirections, forcedRotations);
        RebuildMesh();
    }

    public void PrecalculateVariables() {
        curType.FillInitialVariables(subGen.variableContainer, state, instanceState, runtimeState, totalLength, segments);
    }

    //of the width of a section whose direction differs from the one of the segment next to it, so that the road keeps
    //its width across the segment (as the sections between the segments of a low-poly road)
    static float GetMiterFactor(Vector3 sectionDir, Vector3 segmentDir) {
        var a = new Vector3(sectionDir[0], 0, sectionDir[2]);
        var b = new Vector3(segmentDir[0], 0, segmentDir[2]);
        if (GameEngine.Vector3.Magnitude(a) <= GH.epsilon || GameEngine.Vector3.Magnitude(b) <= GH.epsilon) return 1.0f;
        var angle = Mathf.Min(GameEngine.Vector3.Angle(a, b), GH.MaxSectionYaw);
        return 1.0f / Mathf.Cos(angle * GameEngine.Math.deg2rad);
    }

    void RebuildLine(List<Vector3> forcedDirections = null, List<Vector2> forcedRotations = null) {
        float epsilon = 0.01f;
        var curveLength = 0.0f;
        subGen.Reset(curType, state, instanceState, segments, numMeshes);

        var curveType = (GH.CurveType)state.Int("curveType");
        var hermiteTension = state.Float("hermiteTension", 0.5f);
        var subdivideEqually = state.Bool("subdivideEqually");
        var projectAll = state.Bool("projectAll");
        var adjustLowPolyWidth = state.Bool("adjustLowPolyWidth");
        var lpf = state.Int("lpf");

        lr.positionCount = segments;
        totalLength = 0.0f;
        var curvePoints = subGen.curvePoints;

        for (int i = 0; i < segments; i++) {
            var pos = GH.GetPointOnCurve(start, cps, end, i / (segments - 1.0f), i, curveType, hermiteTension, subdivideEqually, startTangent, endTangent);
            var dontProject = (i == 0 && hasStartIntersection) || (i == segments - 1 && hasEndIntersection);
            if (!dontProject && projectAll) pos = GH.ProjectPoint(pos);
            curvePoints.Add(pos);
            if (i > 0) {
                totalLength += GameEngine.Vector3.Magnitude(curvePoints[i] - curvePoints[i - 1]);
            }
        }
        if (lpf > 0) {
            curvePoints = GH.LowPassFilter(curvePoints, lpf, segments);
            subGen.curvePoints = curvePoints;
        }
        var hasRotations = cpRotations != null && cpRotations.Count == cps.Count + 2 && cpRotations.Exists(r => r != GameEngine.Vector2.zero);
        rolled = false;
        for (int i = 0; i < segments; i++) {
            lr.SetPosition(i, curvePoints[i]);
            Vector3 dir, right;
            var widthFactor = 1.0f;
            var rotation = GameEngine.Vector2.zero;
            if (forcedDirections != null && forcedDirections.Count >= segments) {
                dir = forcedDirections[i];
                right = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(dir, GameEngine.Vector3.up)) * GameEngine.Vector3.Magnitude(dir);
                if (forcedRotations != null && i < forcedRotations.Count) rotation = forcedRotations[i];
            } else {
                var t = i / (segments - 1.0f);
                var lowPolyWidth = adjustLowPolyWidth || curveType == GH.CurveType.LowPoly;
                if (i == 0 && startTangent.HasValue) {
                    dir = GameEngine.Vector3.Normalize(startTangent.Value);
                    widthFactor = lowPolyWidth ? GetMiterFactor(dir, curvePoints[i + 1] - curvePoints[i]) : 1.0f;
                } else if (i == segments - 1 && endTangent.HasValue) {
                    dir = GameEngine.Vector3.Normalize(endTangent.Value);
                    widthFactor = lowPolyWidth ? GetMiterFactor(dir, curvePoints[i] - curvePoints[i - 1]) : 1.0f;
                } else if (i == 0) {
                    var p1 = curvePoints[i];
                    if (startIntersectionCenter.HasValue) {
                        dir = p1 - startIntersectionCenter.Value;
                    } else {
                        var p2 = curvePoints[i + 1];
                        dir = p2 - p1;
                    }
                } else if (i == segments - 1) {
                    var p2 = curvePoints[i];
                    if (endIntersectionCenter.HasValue) {
                        dir = endIntersectionCenter.Value - p2;
                    } else {
                        var p1 = curvePoints[i - 1];
                        dir = p2 - p1;
                    }
                } else {
                    Vector3 p1, p2;
                    if (lpf > 0) {
                        //cannot sample the real curve, get an approximate direction
                        p1 = curvePoints[i - 1];
                        p2 = curvePoints[i + 1];
                    } else {
                        //get the accurate direction
                        p1 = GH.GetPointOnCurve(start, cps, end, t - epsilon * 0.5f, i - 1, curveType, hermiteTension, subdivideEqually, startTangent, endTangent);
                        p2 = GH.GetPointOnCurve(start, cps, end, t + epsilon * 0.5f, i + 1, curveType, hermiteTension, subdivideEqually, startTangent, endTangent);
                    }
                    if (lowPolyWidth) {
                        var p1a = GameEngine.Vector3.Normalize(curvePoints[i] - p1);
                        var p2a = GameEngine.Vector3.Normalize(p2 - curvePoints[i]);
                        dir = GameEngine.Vector3.Normalize(p1a + p2a);
                    } else {
                        dir = p2 - p1;
                    }
                }
                right = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(dir, GameEngine.Vector3.up)) * widthFactor;
                if (hasRotations) rotation = GH.GetValueOnCurve(start, cps, end, cpRotations, t, i, curveType, hermiteTension, subdivideEqually, startTangent, endTangent);
            }
            var up = GameEngine.Vector3.up;
            if (rotation != GameEngine.Vector2.zero) {
                if (rotation[1] != 0) rolled = true;
                (right, up) = GH.RotateSection(right, rotation[0], rotation[1]);
                //the intersections move their vertices only vertically: the sections joining them (and the roads through
                //them) keep the vertical up direction, only the right vector is rolled
                var atIntersection = (i == 0 && hasStartIntersection) || (i == segments - 1 && hasEndIntersection);
                if (atIntersection || forcedRotations != null) up = GameEngine.Vector3.up;
            }
            subGen.curveDirections.Add(dir);
            subGen.curveUpVectors.Add(up);
            if (i > 0) {
                curveLength += GameEngine.Vector3.Magnitude(curvePoints[i] - curvePoints[i - 1]);
            }
            subGen.sectionMarkers.Add(curveLength);
            subGen.curveRightVectors.Add(right);
        }
        curType.FillInitialVariables(subGen.variableContainer, state, instanceState, runtimeState, totalLength, segments);
        subGen.sectionRights.Clear();
        subGen.sectionUps.Clear();
        subGen.sectionVertices.Clear();
        subGen.InitBaseSectionsInfo();
        for (int i = 0; i < segments; i++) {
            if (curType.typeData.settings.variableSections || curType.NeedsLowPolyFix(state, segments))
                curType.FillSegmentVariables(subGen.variableContainer, state, instanceState, curvePoints[i], GameEngine.Vector3.zero, subGen.curveUpVectors[i],
                    subGen.sectionMarkers[i], 0, i + 1, new Vector3[0], curvePoints, segments);
            var v = GetRealSectionVertices(i);
            subGen.sectionVertices.Add(v.Item1);
            subGen.sectionRights.Add(v.Item2);
            subGen.sectionUps.Add(subGen.curveUpVectors[i]);
        }
        curType.FillCommonLateVariables(subGen.variableContainer, curvePoints[0], curvePoints[segments - 1], subGen.sectionVertices[0], subGen.sectionVertices[segments - 1]);
        GenerateLanesPre();
    }

    void ProcessTrafficLanes(CityElements.Types.Runtime.RoadLikeType.TrafficLaneContainer[] lanesList, RC.VariableContainer vars, int component) {
        if (lanesList == null) return;
        for (int li = 0; li < lanesList.Length; li++) {
            var lane = lanesList[li];
            if (lane.condition == null || lane.condition.GetValue(vars)) {
                var trafficLanes = lane.lanes.GetValues(vars);
                var type = (int)lane.type.GetValue(vars);
                var color = curType.trafficTypes[type].color;
                for (int ti = 0; ti < trafficLanes.Length; ti++) {
                    var trafficLane = trafficLanes[ti];
                    lanes.Add(new List<Vector3>());
                    var laneName = "traffic_" + type + "_" + component + "_" + li + "_" + ti;
                    lanesInfo.Add(new LaneInfo(trafficLane, lane.startBound, lane.endBound, color, laneName, vars, component));
                }
            }
        }
    }

    void ProcessPropLinesRenderers(CityElements.Types.Runtime.RoadLikeType.PropLine[] propLines, RC.VariableContainer vars, int component) {
        if (propLines == null) return;
        foreach (var line in propLines) {
            if (line.condition == null || line.condition.GetValue(vars)) {
                lanes.Add(new List<Vector3>());
                lanes.Add(new List<Vector3>());
                var startBound = new LaneInfo(0, line.startBound, line.endBound, new Color(1.0f, 1.0f, 1.0f), "props_" + line.containerName + "_start", vars, component);
                var endBound = new LaneInfo(1, line.startBound, line.endBound, new Color(0.8f, 0.8f, 0.8f), "props_" + line.containerName + "_end", vars, component);
                lanesInfo.Add(startBound);
                lanesInfo.Add(endBound);
                propsLanesDict.Add(line.containerName, (lanesInfo.IndexOf(startBound), lanesInfo.IndexOf(endBound)));
            }
        }
    }

    void GenerateLanesPre() {
        GO.Destroy(lanesRenderersContainer);
        lineRenderers.Clear();
        lanesRenderersContainer = new GameObject();
        GO.SetParent(lanesRenderersContainer, this);
        GO.SetName(lanesRenderersContainer, "LanesRenderers");
        lanes.Clear();

        //determine the lanes and create the lists
        lanesInfo = new List<LaneInfo>();
        propsLanesDict = new Dictionary<string, (int, int)>();
        ProcessTrafficLanes(curType.trafficLanes, subGen.variableContainer, -1);
        ProcessPropLinesRenderers(curType.propLines, subGen.variableContainer, -1);
        for (int c = 0; c < curType.componentMeshes.Count; c++) {
            var compMesh = curType.componentMeshes[c];
            if (curType.subConditions[c] == null || curType.subConditions[c].GetValue(subGen.variableContainer)) {
                curType.FillComponentVariables(subGen.variableContainer, c);
                ProcessTrafficLanes(compMesh.trafficLanes, subGen.variableContainer, c);
                ProcessPropLinesRenderers(compMesh.propLines, subGen.variableContainer, c);
            }
        }
    }

    void GenerateLanesPost() {
        //create the renderers
        for (int i = 0; i < lanes.Count; i++) {
            CreateLineRenderer(lanes[i], lanesInfo[i].color, lanesInfo[i].name);
        }
        GO.SetEnabled(lanesRenderersContainer, lr.enabled);
    }

    LineRenderer CreateLineRenderer(List<Vector3> lane, Color color, string name) {
        var laneRenderer = GO.CreateLineRenderer(lanesRenderersContainer, name);
        laneRenderer.startWidth = curLineSize;
        laneRenderer.endWidth = curLineSize;
        laneRenderer.material = MaterialManager.GetHandleMaterial(color);
        laneRenderer.positionCount = lane.Count;
        var laneOffset = new List<Vector3>();
        foreach (var pos in lane) {
            laneOffset.Add(pos + GameEngine.Vector3.up * 0.01f);
        }
        laneRenderer.SetPositions(laneOffset.ToArray());
        lineRenderers.Add(laneRenderer);
        return laneRenderer;
    }

    void PlaceProps() {
        GO.Destroy(propsContainer);
        props.Clear();
        RandomManager.rnds.Clear();
        propsContainer =  GO.Create("PropsContainer");
        GO.SetParent(propsContainer, this);
        PropPlacer.Place(state.State("propRule"), curType.typeData.settings.propsContainers, containerType => {
            if (!propsLanesDict.ContainsKey(containerType)) return null;
            var indices = propsLanesDict[containerType];
            return (lanes[indices.startBoundIndex].ToArray(), lanes[indices.endBoundIndex].ToArray());
        }, propsContainer, props);
    }

    public List<Vector3> GetCurvePoints() {
        return subGen.curvePoints;
    }

    void ReinitializeAnchorPoints() {
        foreach (var points in sidePoints) {
            points.Clear();
        }
        sidePoints.Clear();
        var anchors = curType.anchorsCalculators.GetValues(subGen.variableContainer);
        foreach (var a in anchors) {
            sidePoints.Add(new List<Vector3>());
        }
        for (int c = 0; c < curType.typeData.components.Length; c++) {
            var compMesh = curType.componentMeshes[c];
            if ((curType.subConditions[c] == null || curType.subConditions[c].GetValue(subGen.variableContainer)) && compMesh.anchorsCalculators != null) {
                curType.FillComponentVariables(subGen.variableContainer, c);
                var subAnchors = compMesh.anchorsCalculators.GetValues(subGen.variableContainer);
                foreach (var a in subAnchors) {
                    sidePoints.Add(new List<Vector3>());
                }
            }
        }
    }

    void RebuildMesh() {
        ReinitializeAnchorPoints();
        for (int i = 0; i < segments; i++) {
            AddSection(i);
        }
        GenerateLanesPost();
        var realNumMeshes = 0;
        for (int i = 0; i < numMeshes; i++) if (subGen.tempIndices[i].Count > 0) realNumMeshes++;
        vertsPerSection = subGen.GetVerticesPerSection();

        int realI = 0;
        var realMats = new Material[realNumMeshes];
        var materialSet = GetMaterialSet();
        var indices = new List<int[]>();
        for (int i = 0; i < numMeshes; i++) {
            if (subGen.tempIndices[i].Count > 0) {
                indices.Add(subGen.tempIndices[i].ToArray());
                realMats[realI] = materialSet[i];
                realI += 1;
            }
        }

        GameEngine.Mesh.AssignMaterials(mr, realMats);
        GameEngine.Mesh.AssignData(m, realMats, indices, subGen.tempVertices, subGen.tempUVs, GO.GetName(this));
        GameEngine.Mesh.RefreshCollider(m, mc, segments > 1 && valid);

        oldType = curType;

        PlaceProps();
    }

    void AddSection(int i) {
        subGen.InitSection(i);

        //fill the lanes
        for (int j = 0; j < lanes.Count; j++) {
            var p = lanesInfo[j];
            if (p.component >= 0) curType.FillComponentVariables(subGen.variableContainer, p.component);
            lanes[j].Add(GameEngine.Vector3.Lerp(p.startBound.GetValue(p.vars), p.endBound.GetValue(p.vars), p.pos));
        }

        subGen.AddSection(i, sidePoints);
    }

    (Vector3[], Vector3) GetRealSectionVertices(int i) {
        var v = subGen.GetRawSectionVertices(i);
        if (i > 0 && state.Bool("makeCoplanar") && !rolled && !(i == segments - 1 && hasEndIntersection)) {
            v = GetCoplanarRawSectionVertices(v, i);
        }
        return v;
    }

    private void Update() {
        var cam = GameEngine.Camera.Get(this);
        if (lanesRenderersContainer != null && GO.GetEnabled(lanesRenderersContainer) && cam != null) {
            var camPos = GO.GetPosition(cam);
            var pos = GH.ClosestPointToCurve(camPos, subGen.curvePoints, out _);
            var dist = GameEngine.Vector3.Distance(pos, camPos);
            curLineSize = Mathf.Max(0.1f * (dist / 20.0f), 0.1f);
            foreach (var lr in lineRenderers) {
                lr.startWidth = curLineSize;
                lr.endWidth = curLineSize;
            }
        }
    }

    Vector3 GetCoplanarDir(Vector3[] v, int i) {
        var vL = v[0];
        var vM = v[v.Length / 2];
        var vR = v[v.Length - 1];
        var vPrev = subGen.GetRawSectionVertices(i - 1).Item1;
        var vPrevL = vPrev[0];
        var vPrevR = vPrev[vPrev.Length - 1];
        var p1A = vPrevL;
        var p1C = vPrevR;
        var p2A = vL;
        var p2B = vM;
        var p2C = vR;
        var p1 = GameEngine.Plane.Create(p1A, p2B, p1C);
        Plane p2;
        if (i < segments - 1) {
            var vNext = subGen.GetRawSectionVertices(i + 1).Item1;
            var vNextM = vNext[vNext.Length / 2];
            var p3B = vNextM;
            p2 = GameEngine.Plane.Create(p2A, p3B, p2C);
        } else {
            p2 = GameEngine.Plane.Create(p2B + GameEngine.Vector3.right, p2B, p2B + GameEngine.Vector3.back);
        }
        var origDir = GameEngine.Vector3.Normalize(vR - vL);
        var lineDirection = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(GameEngine.Plane.Normal(p1), GameEngine.Plane.Normal(p2)));
        if (GameEngine.Vector3.Dot(lineDirection, vR - vL) < 0) lineDirection = -lineDirection;
        if (GameEngine.Vector3.Magnitude(lineDirection) < 0.0) {
            lineDirection = origDir;
        } else {
            var angle = GameEngine.Vector3.Angle(lineDirection, vR - vL);
            lineDirection /= Mathf.Cos(angle * GameEngine.Math.deg2rad);
            var dist = Mathf.Abs(GameEngine.Plane.DistanceToPoint(p1, p2A));
            if (dist < state.Float("pointPlaneTreshold", 0.02f)) lineDirection = origDir;
        }
        return lineDirection;
    }

    (Vector3[], Vector3) GetCoplanarRawSectionVertices((Vector3[], Vector3) v, int i) {
        return subGen.GetRawSectionVertices(i, GetCoplanarDir(v.Item1, i));
    }
}

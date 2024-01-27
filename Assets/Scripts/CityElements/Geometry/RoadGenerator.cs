using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;
using RC = RuntimeCalculator;
using GH = GeometryHelper;

public class RoadGenerator : MonoBehaviour {
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

    public ObjectState state, instanceState;
    public CityElements.Types.Runtime.RoadType curType;
    public bool hasStartIntersection = false;
    public bool hasEndIntersection = false;

    public int segments;
    public MeshRenderer mr;
    MeshFilter mf;
    public Mesh m;
    MeshCollider mc;
    public LineRenderer lr;
    public GameObject lanesRenderersContainer;
    public GameObject propsContainer;
    List<LineRenderer> lineRenderers = new List<LineRenderer>();

    List<List<Vector3>> lanes = new List<List<Vector3>>();
    List<LaneInfo> lanesInfo;
    Dictionary<string, (int startBoundIndex, int endBoundIndex)> propsLanesDict;

    int numMeshes;
    public List<Vector3> cps = new List<Vector3>();
    public Vector3 start, end;
    public Vector3? startIntersectionCenter, endIntersectionCenter;

    public List<List<Vector3>> sidePoints = new List<List<Vector3>>();

    public ElementPlacer.RoadPlacer placer = null;
    public bool valid = false;

    float totalLength;
    float curLineSize = 0.1f;
    public int vertsPerSection;

    CityElements.Types.Runtime.RoadType oldType;

    public RoadLikeGenerator<CityElements.Types.RoadType> subGen;

    public void Initialize(ObjectState state) {
        lr = gameObject.AddComponent<LineRenderer>();
        lr.material = Resources.Load<Material>("Materials/Line");
        lr.startWidth = 0.1f;
        lr.endWidth = 0.1f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        gameObject.layer = 7;
        this.state = state;

        mr = gameObject.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mf = gameObject.AddComponent<MeshFilter>();
        m = new Mesh();
        mf.mesh = m;
        mr.materials = GetMaterialSet();
        numMeshes = mr.materials.Length;
        subGen = new RoadLikeGenerator<CityElements.Types.RoadType>(state, instanceState, segments, numMeshes);

        mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = m;
        lanesRenderersContainer = new GameObject();
        lanesRenderersContainer.transform.SetParent(transform, true);
        lanesRenderersContainer.name = "LanesRenderers";
        propsContainer = new GameObject("PropsContainer");
        propsContainer.transform.SetParent(transform, true);
    }

    public void ResetSubGen() {
        ResetType();
        subGen.Reset(curType, state, instanceState, segments, numMeshes);
    }

    public void ResetType() {
        mr.materials = GetMaterialSet();
        numMeshes = mr.materials.Length;
    }

    Material[] GetMaterialSet() {
        var list = new List<Material>();
        if (curType != null) {
            foreach (var tex in curType.typeData.settings.textures) {
                var mat = MaterialManager.GetMaterial(state.Str(tex));
                list.Add(mat);
            }
        }
        return list.ToArray();
    }

    //TODO: deduplicate (already on road)
    public Vector3 GetStandardVec3(string name) {
        var realName = curType.typeData.settings.getters[name];
        return curType.vector3Definitions[realName].GetValue(subGen.variableContainer);
    }

    public Vector2 GetStandardVec2(string name) {
        var realName = curType.typeData.settings.getters[name];
        return curType.vector2Definitions[realName].GetValue(subGen.variableContainer);
    }

    public float GetStandardFloat(string name) {
        var realName = curType.typeData.settings.getters[name];
        return curType.numberDefinitions[realName].GetValue(subGen.variableContainer);
    }

    public bool GetStandardBool(string name) {
        var realName = curType.typeData.settings.getters[name];
        return curType.boolDefinitions[realName].GetValue(subGen.variableContainer);
    }

    public string GetStandardString(string name) {
        var realName = curType.typeData.settings.getters[name];
        return state.Str(realName);
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

    public void Rebuild(List<Vector3> forcedDirections = null) {
        if (curType != oldType) ResetType();
        RebuildLine(forcedDirections);
        RebuildMesh();
    }

    public void PrecalculateVariables() {
        curType.FillInitialVariables(subGen.variableContainer, state, instanceState, totalLength, segments);
    }

    void RebuildLine(List<Vector3> forcedDirections = null) {
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
            var pos = GH.GetPointOnCurve(start, cps, end, i / (segments - 1.0f), i, curveType, hermiteTension, subdivideEqually);
            var dontProject = (i == 0 && hasStartIntersection) || (i == segments - 1 && hasEndIntersection);
            if (!dontProject && projectAll) pos = GH.ProjectPoint(pos);
            curvePoints.Add(pos);
            if (i > 0) {
                totalLength += (curvePoints[i] - curvePoints[i - 1]).magnitude;
            }
        }
        if (lpf > 0) {
            curvePoints = GH.LowPassFilter(curvePoints, lpf, segments);
            subGen.curvePoints = curvePoints;
        }
        for (int i = 0; i < segments; i++) {
            lr.SetPosition(i, curvePoints[i]);
            Vector3 dir, right;
            if (forcedDirections != null && forcedDirections.Count >= segments) {
                dir = forcedDirections[i];
                right = Vector3.Cross(dir, Vector3.up).normalized * dir.magnitude;
            } else {
                var t = i / (segments - 1.0f);
                if (i == 0) {
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
                        p1 = GH.GetPointOnCurve(start, cps, end, t - epsilon * 0.5f, i - 1, curveType, hermiteTension, subdivideEqually);
                        p2 = GH.GetPointOnCurve(start, cps, end, t + epsilon * 0.5f, i + 1, curveType, hermiteTension, subdivideEqually);
                    }
                    if (adjustLowPolyWidth || curveType == GH.CurveType.LowPoly) {
                        var p1a = (curvePoints[i] - p1).normalized;
                        var p2a = (p2 - curvePoints[i]).normalized;
                        dir = (p1a + p2a).normalized;
                    } else {
                        dir = p2 - p1;
                    }
                }
                right = Vector3.Cross(dir, Vector3.up).normalized;
            }
            subGen.curveDirections.Add(dir);
            if (i > 0) {
                curveLength += (curvePoints[i] - curvePoints[i - 1]).magnitude;
            }
            subGen.sectionMarkers.Add(curveLength);
            subGen.curveRightVectors.Add(right);
        }
        curType.FillInitialVariables(subGen.variableContainer, state, instanceState, totalLength, segments);
        subGen.sectionRights.Clear();
        subGen.sectionVertices.Clear();
        subGen.InitBaseSectionsInfo();
        for (int i = 0; i < segments; i++) {
            if (curType.typeData.settings.variableSections || curType.NeedsLowPolyFix(state, segments))
                curType.FillSegmentVariables(subGen.variableContainer, state, instanceState, curvePoints[i], Vector3.zero,
                    subGen.sectionMarkers[i], 0, i + 1, new Vector3[0], curvePoints, segments);
            var v = GetRealSectionVertices(i);
            subGen.sectionVertices.Add(v.Item1);
            subGen.sectionRights.Add(v.Item2);
        }
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
        Destroy(lanesRenderersContainer);
        lineRenderers.Clear();
        lanesRenderersContainer = new GameObject();
        lanesRenderersContainer.transform.SetParent(transform, true);
        lanesRenderersContainer.name = "LanesRenderers";
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
        lanesRenderersContainer.SetActive(lr.enabled);
    }

    LineRenderer CreateLineRenderer(List<Vector3> lane, Color color, string name) {
        var laneRendererObj = new GameObject();
        if (name != null) laneRendererObj.name = name;
        laneRendererObj.transform.SetParent(lanesRenderersContainer.transform, true);
        var laneRenderer = laneRendererObj.AddComponent<LineRenderer>();
        laneRenderer.material = Resources.Load<Material>("Materials/Line");
        laneRenderer.startWidth = curLineSize;
        laneRenderer.endWidth = curLineSize;
        laneRenderer.material = MaterialManager.GetHandleMaterial(color);
        laneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        laneRenderer.receiveShadows = false;
        laneRenderer.positionCount = lane.Count;
        var laneOffset = new List<Vector3>();
        foreach (var pos in lane) {
            laneOffset.Add(pos + Vector3.up * 0.01f);
        }
        laneRenderer.SetPositions(laneOffset.ToArray());
        lineRenderers.Add(laneRenderer);
        return laneRenderer;
    }

    void PlacePropsElem(string containerType, ObjectState elemState) {
        if (!propsLanesDict.ContainsKey(containerType)) return;
        var indices = propsLanesDict[containerType];
        var pathIn = lanes[indices.startBoundIndex].ToArray();
        var pathOut = lanes[indices.endBoundIndex].ToArray();
        PlacePropsLane(pathIn, pathOut, elemState, containerType);
    }

    void PlaceProps() {
        Destroy(propsContainer);
        RandomManager.rnds.Clear();
        propsContainer = new GameObject("PropsContainer");
        propsContainer.transform.SetParent(transform, true);
        var rule = state.State("propRule");
        if (rule != null) {
            var types = CityElements.Types.Parsers.TypeParser.GetPropsContainersTypes();
            foreach (var containerType in curType.typeData.settings.propsContainers) {
                var container = rule.State(containerType);
                if (container != null) {
                    var typeName = container.Str("type");
                    if (typeName != null && types.ContainsKey(typeName)) {
                        var type = types[typeName];
                        foreach (var param in type.parameters) {
                            if (param.maxNumber == 1) {
                                var paramState = container.State(param.name);
                                if (paramState != null) PlacePropsElem(containerType, paramState);
                            } else {
                                var states = container.Array<ObjectState>(param.name);
                                if (states != null) {
                                    foreach (var paramState in states) {
                                        PlacePropsElem(containerType, paramState);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    void PlacePropsLane(Vector3[] inLine, Vector3[] outLine, ObjectState elem, string containerType) {
        if (inLine.Length != outLine.Length) {
            print("Invalid props spline detected");
            return;
        }
        var types = CityElements.Types.Parsers.TypeParser.GetPropsElementTypes();
        var typeName = elem.Str("type");
        if (typeName != null && types.ContainsKey(typeName)) {
            int i;
            var midLine = new Vector3[inLine.Length];
            for (i = 0; i < midLine.Length; i++) {
                midLine[i] = (inLine[i] + outLine[i]) * 0.5f;
            }
            var midLength = GH.GetPathLength(midLine);
            var type = types[typeName];
            int numElemMeshes = 1;
            if (type.typeData.maxMeshes != 1) {
                var meshesList = elem.Array<string>("meshes");
                if (meshesList == null) {
                    print("Invalid props elem detected");
                    return;
                }
                numElemMeshes = meshesList.Length;
            }
            var varSet = type.variableContainer.GetClone();
            type.FillInitialVariables(varSet, elem, midLength, numElemMeshes);

            //place the props
            i = 0;
            var x = 0.0f;
            var z = 0.0f;
            var lastX = 0.0f;
            var lastZ = 0.0f;
            var mesh = elem.Str("mesh");
            var meshes = elem.Array<string>("meshes");
            var posDir = GH.GetPointAndDirOnSidewalk(inLine, outLine, midLine, x, z);
            type.FillSegmentVariables(varSet, i, lastX, lastZ);
            type.FillPositionVariables(varSet, posDir.dir, x, z);
            while (true) {
                type.FillSegmentVariables(varSet, i, lastX, lastZ);
                x = type.rules.xPos.GetValue(varSet);
                z = type.rules.zPos.GetValue(varSet);
                posDir = GH.GetPointAndDirOnSidewalk(inLine, outLine, midLine, x, z);
                type.FillPositionVariables(varSet, posDir.dir, x, z);
                if (!type.rules.whileCondition.GetValue(varSet)) break;
                if (type.rules.ifCondition == null || type.rules.ifCondition.GetValue(varSet)) {
                    string meshName;
                    if (type.typeData.maxMeshes != 1) {
                        int meshIndex = (int)type.rules.meshIndex.GetValue(varSet);
                        meshIndex = Mathf.Clamp(meshIndex, 0, meshes.Length - 1);
                        meshName = meshes[meshIndex];
                    } else {
                        meshName = mesh;
                    }
                    if (meshName != null && meshName != "") {
                        var forward = type.rules.forward.GetValue(varSet);
                        try {
                            var propObj = MeshManager.GetMesh(meshName, propsContainer);
                            propObj.name = containerType + ";" + meshName;
                            Actions.Helpers.SetLayerRecursively(propObj, 13);
                            propObj.transform.parent = propsContainer.transform;
                            propObj.transform.position = posDir.pos + new Vector3(0, -MeshManager.GetBounds(meshName).min.y, 0); //TODO: make customizable
                            var rot = Vector3.SignedAngle(Vector3.forward, forward, Vector3.up);
                            propObj.transform.rotation = Quaternion.Euler(0, rot, 0);
                        } catch (System.Exception e) {
                            print(e.ToString());
                        }
                    }
                }
                lastX = x;
                lastZ = z;
                i++;
            }
        }
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
            if (curType.subConditions[c] == null || curType.subConditions[c].GetValue(subGen.variableContainer) && compMesh.anchorsCalculators != null) {
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
        m.Clear();
        m.indexFormat = subGen.tempVertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.subMeshCount = realNumMeshes;
        m.SetVertices(subGen.tempVertices);
        m.SetUVs(0, subGen.tempUVs);
        vertsPerSection = subGen.GetVerticesPerSection();
        int realI = 0;
        var realMats = new Material[realNumMeshes];
        var materialSet = GetMaterialSet();
        for (int i = 0; i < numMeshes; i++) {
            if (subGen.tempIndices[i].Count > 0) {
                m.SetIndices(subGen.tempIndices[i], MeshTopology.Triangles, realI);
                realMats[realI] = materialSet[i];
                realI += 1;
            }
        }
        mr.materials = realMats;
        m.RecalculateNormals();
        m.RecalculateBounds();
        m.name = gameObject.name;
        //refresh the collider
        mc.enabled = false;
        if (segments > 1 && valid) mc.enabled = true;

        oldType = curType;

        PlaceProps();
    }

    void AddSection(int i) {
        subGen.InitSection(i);

        //fill the lanes
        for (int j = 0; j < lanes.Count; j++) {
            var p = lanesInfo[j];
            if (p.component >= 0) curType.FillComponentVariables(subGen.variableContainer, p.component);
            lanes[j].Add(Vector3.Lerp(p.startBound.GetValue(p.vars), p.endBound.GetValue(p.vars), p.pos));
        }

        subGen.AddSection(i, sidePoints);
    }

    (Vector3[], Vector3) GetRealSectionVertices(int i) {
        var v = subGen.GetRawSectionVertices(i);
        if (i > 0 && state.Bool("makeCoplanar") && !(i == segments - 1 && hasEndIntersection)) {
            v = GetCoplanarRawSectionVertices(v, i);
        }
        return v;
    }

    private void Update() {
        if (lanesRenderersContainer != null && lanesRenderersContainer.activeSelf && Camera.main != null) {
            var camPos = Camera.main.transform.position;
            var pos = GH.ClosestPointToCurve(camPos, subGen.curvePoints, out _);
            var dist = Vector3.Distance(pos, camPos);
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
        var p1 = new Plane(p1A, p2B, p1C);
        Plane p2;
        if (i < segments - 1) {
            var vNext = subGen.GetRawSectionVertices(i + 1).Item1;
            var vNextM = vNext[vNext.Length / 2];
            var p3B = vNextM;
            p2 = new Plane(p2A, p3B, p2C);
        } else {
            p2 = new Plane(p2B + Vector3.right, p2B, p2B + Vector3.back);
        }
        var origDir = (vR - vL).normalized;
        var lineDirection = Vector3.Cross(p1.normal, p2.normal).normalized;
        if (Vector3.Dot(lineDirection, vR - vL) < 0) lineDirection = -lineDirection;
        if (lineDirection.magnitude < 0.0) {
            lineDirection = origDir;
        } else {
            var angle = Vector3.Angle(lineDirection, vR - vL);
            lineDirection /= Mathf.Cos(angle * Mathf.Deg2Rad);
            var dist = Mathf.Abs(p1.GetDistanceToPoint(p2A));
            if (dist < state.Float("pointPlaneTreshold", 0.02f)) lineDirection = origDir;
        }
        return lineDirection;
    }

    (Vector3[], Vector3) GetCoplanarRawSectionVertices((Vector3[], Vector3) v, int i) {
        return subGen.GetRawSectionVertices(i, GetCoplanarDir(v.Item1, i));
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;

public class IntersectionGenerator : MonoBehaviour {
    public MeshRenderer mr;
    MeshCollider mc;
    MeshFilter mf;
    public Mesh m;
    Intersection parentIntersection;

    List<Vector3> roadCenters = new List<Vector3>();
    List<Vector3> roadCenters2 = new List<Vector3>();
    List<Vector3> roadCentersPlusDir = new List<Vector3>();
    List<Vector3> roadLefts = new List<Vector3>();
    List<Vector3> upFactors2 = new List<Vector3>();
    public List<int> sortOrder;
    int curSubRoad = -1;
    int lastSubRoad = -1;
    int lastI = -1;
    string lastTex = "";
    bool lastTexIsDefault = false;
    bool lastDrawnAlongRoad = false;

    ObjectState mainRailState = new ObjectState();

    public List<List<Vector3>> sidePoints = new List<List<Vector3>>();
    public List<object> sidePointStartRoads = new List<object>();
    public List<object> sidePointEndRoads = new List<object>();

    public List<string> partsInfo = new List<string>(); //TODO: implement for roads too (along with separated meshes)
    public CityElements.Types.Runtime.IntersectionType curType;

    GeneratorState genState;

    public class GeneratorState {
        public GeometryHelpers.SubroadManager srManager = new GeometryHelpers.SubroadManager();
        public List<List<Vector3>> sidewalkVertices = new List<List<Vector3>>();
        public List<(Road, Road)> sidewalkEnds = new List<(Road, Road)>();
        public List<List<Vector3>> crosswalkVertices = new List<List<Vector3>>();
        public List<List<float>> groundHeights = new List<List<float>>();
        public List<Plane> planes = new List<Plane>();
        public List<bool> selfIntersectingSplines = new List<bool>();
        public List<bool> convexes = new List<bool>();
        public List<bool> notDefaultTexes = new List<bool>();
        public ObjectState state, instanceState;
        IntersectionGenerator parent;

        public GeneratorState(IntersectionGenerator generator) {
            parent = generator;
        }

        public void Clear() {
            srManager.Clear();
            sidewalkVertices.Clear();
            sidewalkEnds.Clear();
            crosswalkVertices.Clear();
            groundHeights.Clear();
            selfIntersectingSplines.Clear();
            convexes.Clear();
            notDefaultTexes.Clear();
            planes.Clear();
        }

        public CityElements.Types.Runtime.JunctionType GetJunctionType(CityElements.Types.Runtime.IntersectionType curType, int i) {
            return curType.junctionTypes[(sidewalkEnds[i].Item1.GetRoadType().name, sidewalkEnds[i].Item2.GetRoadType().name)];
        }

        public ObjectState GetJunctionInstanceState() {
            return (ObjectState)instanceState.Clone();
        }

        public ObjectState GetJunctionState(CityElements.Types.Runtime.IntersectionType curType, int i) {
            var junction = GetJunctionType(curType, i);
            var tempState = (ObjectState)state.Clone();
            var tempIState = (ObjectState)instanceState.Clone();
            var ends = sidewalkEnds[i];
            tempState.SetBool("thisIsEndA", ends.Item1.endIntersection == parent.parentIntersection);
            tempState.SetBool("thisIsEndB", ends.Item2.endIntersection == parent.parentIntersection);
            tempState.SetBool("selfIntersectingSpline", selfIntersectingSplines[i]);
            tempState.SetBool("convex", convexes[i]);
            tempState.SetBool("notDefaultTex", notDefaultTexes[i]);
            foreach (var param in junction.typeData.importedParameters) {
                var originRoad = param.fromStart ? ends.Item1 : ends.Item2;
                var originState = param.fromStart ? ends.Item1.state : ends.Item2.state;
                var originIState = param.fromStart ? ends.Item1.instanceState : ends.Item2.instanceState;
                var gen = originRoad.generator;
                switch (param.type) {
                    case "int":
                        tempState.SetInt(param.newName, (int)gen.GetFloat(param.name));
                        break;
                    case "bool":
                        tempState.SetBool(param.newName, gen.GetBool(param.name));
                        break;
                    case "float":
                        tempState.SetFloat(param.newName, gen.GetFloat(param.name));
                        break;
                    case "vec2":
                        tempState.SetVector2(param.newName, gen.GetVec2(param.name));
                        break;
                    case "vec3":
                        tempState.SetVector3(param.newName, gen.GetVec3(param.name));
                        break;
                    case "texture":
                        tempState.SetStr(param.newName, originState.properties.ContainsKey(param.name) ? originState.Str(param.name) : originIState.Str(param.name));
                        break;
                }
            }
            junction.FillInitialVariables(junction.variableContainer, tempState, tempIState, 0, tempState.Int("sidewalkSegments"), ends.Item1.generator.subGen, ends.Item2.generator.subGen);
            foreach (var d in junction.textureDefinitions) {
                var index = (int)d.Value.index.GetValue(junction.variableContainer);
                var trueName = d.Value.options[index];
                tempState.SetStr(d.Key, tempState.Str(trueName));
            }
            return tempState;
        }
    }

    public void Initialize(Intersection parentIntersection) {
        this.parentIntersection = parentIntersection;
        mr = gameObject.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mf = gameObject.AddComponent<MeshFilter>();
        m = new Mesh();
        mf.mesh = m;
        mr.materials = new Material[0];
        mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = m;
        gameObject.layer = 7;
        genState = new GeneratorState(this);
    }

    public CityElements.Types.Runtime.IntersectionType GetIntersectionType() {
        var dict = CityElements.Types.Parsers.TypeParser.GetIntersectionTypes();
        return dict[parentIntersection.state.Str("type", null)];
    }

    Material[] GetMaterialSet(List<string> extraTextures, List<(Road, Road)> sidewalkEnds) {
        var res = new List<Material>() {  };
        if (extraTextures != null) {
            foreach (var tex in extraTextures) {
                res.Add(MaterialManager.GetMaterial(tex));
            }
        }
        for (int i = 0;  i < genState.sidewalkEnds.Count; i++) {
            var junction = genState.GetJunctionType(curType, i);
            var tempState = genState.GetJunctionState(curType, i);
            foreach (var tex in junction.typeData.textures) {
                res.Add(MaterialManager.GetMaterial(tempState.Str(tex)));
            }
        }
        var defTex = curType.typeData.settings.defaultCrosswalkTexture;
        var startTex = curType.typeData.settings.roadStartCrosswalkTexture;
        var endTex = curType.typeData.settings.roadEndCrosswalkTexture;
        var hasStartTex = startTex != null && startTex != "";
        var hasEndTex = endTex != null && endTex != "";
        foreach (var road in parentIntersection.roads) {
            var activeStartCrosswalk = parentIntersection == road.startIntersection && road.state.Float("startCrosswalkSize") > 0;
            var activeEndCrosswalk = parentIntersection == road.endIntersection && road.state.Float("endCrosswalkSize") > 0;
            if (activeStartCrosswalk || activeEndCrosswalk) {
                if (hasStartTex && road.GetStandardString(startTex) != "") {
                    res.Add(MaterialManager.GetMaterial(road.GetStandardString(startTex)));
                } else if (hasEndTex && road.GetStandardString(endTex) != "") {
                    res.Add(MaterialManager.GetMaterial(road.GetStandardString(endTex)));
                } else {
                    res.Add(MaterialManager.GetMaterial(parentIntersection.state.Str(defTex)));
                }
            }
        }
        return res.ToArray();
    }

    public void RebuildMesh(List<GameObject> roadsThrough, List<List<Vector3>> throughDirections, bool isThroughRoad, List<int> startRoads, int endRoad) {
        Clear();
        curType = GetIntersectionType();
        CalculateVerticesAndCenters();
        var pointCenter = parentIntersection.point.transform.position;
        sortOrder = GeometryHelper.SortClockwise(roadCenters, pointCenter);
        var roads = parentIntersection.roads;
        genState.state = parentIntersection.state;
        genState.instanceState = parentIntersection.instanceState;
        for (int i = 0; i < roads.Count; i++) {
            ProcessRoad(i, roads, sortOrder, isThroughRoad, throughDirections, roadsThrough, startRoads, endRoad);
        }
        genState.srManager.MergeSubroadsIfNeeded();
        RebuildRoadsThrough(roadsThrough, throughDirections, isThroughRoad, startRoads, endRoad);
        GenerateMesh();
    }

    public ObjectState GetMainRailState() {
        return mainRailState;
    }

    void Clear() {
        genState.Clear();

        roadCenters.Clear();
        roadCenters2.Clear();
        roadCentersPlusDir.Clear();
        roadLefts.Clear();
        upFactors2.Clear();
        curSubRoad = -1;
        lastSubRoad = -1;
        lastI = -1;
        lastTex = "";
        lastTexIsDefault = false;
        lastDrawnAlongRoad = false;

        sidePoints.Clear();
        sidePointStartRoads.Clear();
        sidePointEndRoads.Clear();

        partsInfo.Clear();
    }

    void CalculateVerticesAndCenters() {
        var roads = parentIntersection.roads;
        var sizes = parentIntersection.sizes;
        var pointCenter = parentIntersection.point.transform.position;

        //calculate road and crosswalk vertices and get road centers (for sidewalk vertices)
        for (int i = 0; i < roads.Count; i++) {
            var dir = parentIntersection.GetDirectionToCenter(roads[i], pointCenter).normalized;
            var left = Vector3.Cross(dir, Vector3.up).normalized * 0.5f;
            var upFactor2 = Vector3.up * roads[i].GetStandardFloat("height");
            var leftFactor = left * roads[i].GetStandardFloat("roadWidth");
            var roadCenter = pointCenter - dir * sizes[i];
            var roadCenter2 = pointCenter - dir * (sizes[i] - roads[i].GetCrosswalkSize(parentIntersection));
            var roadCenterPlusDir = pointCenter - dir * (sizes[i] - roads[i].GetCrosswalkSize(parentIntersection) - 0.1f);
            roadCenters.Add(roadCenter);
            roadCenters2.Add(roadCenter2);
            roadCentersPlusDir.Add(roadCenterPlusDir);
            roadLefts.Add(left);
            upFactors2.Add(upFactor2);
            if (roads[i].GetCrosswalkSize(parentIntersection) > 0) {
                var thisCrosswalkVertices = new List<Vector3>();
                thisCrosswalkVertices.Add(roadCenter + leftFactor - upFactor2);
                thisCrosswalkVertices.Add(roadCenter2 + leftFactor - upFactor2);
                thisCrosswalkVertices.Add(roadCenter - leftFactor - upFactor2);
                thisCrosswalkVertices.Add(roadCenter2 - leftFactor - upFactor2);
                genState.crosswalkVertices.Add(thisCrosswalkVertices);
            }
        }
    }

    void RebuildRoadsThrough(List<GameObject> roadsThrough, List<List<Vector3>> throughDirections, bool isThroughRoad, List<int> startRoads, int endRoad) {
        for (int i = 0; i < roadsThrough.Count; i++) {
            var roadThrough = roadsThrough[i];
            var roadGen = roadThrough.GetComponent<RoadGenerator>();
            if (throughDirections[i].Count == 0) {
                roadGen.segments = 2;
                roadGen.start = roadCenters2[startRoads[i]];
                roadGen.end = roadCenters2[endRoad];
                roadGen.valid = true;
                var dir1 = (roadCentersPlusDir[startRoads[i]] - roadCenters[startRoads[i]]).normalized;
                var dir2 = (roadCenters[endRoad] - roadCentersPlusDir[endRoad]).normalized;
                throughDirections[i].AddRange(new List<Vector3>() { dir1, dir2 });
            }
            roadGen.Rebuild(throughDirections[i]);
        }
    }

    bool AreThereDiscordRoads() {
        int withSidewalks = 0;
        int withoutSidewalks = 0;
        foreach (var road in parentIntersection.roads) {
            if (road.generator.GetStandardBool("hasSidewalks")) withSidewalks++;
            else withoutSidewalks++;
        }
        return withSidewalks > 0 && withoutSidewalks > 0;
    }

    void GenerateMesh() {
        try {
            var discordRoads = AreThereDiscordRoads();
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();

            var outTris = new List<List<int>>();

            curType.FillInitialVariables(curType.variableContainer, parentIntersection.state, parentIntersection.instanceState);

            RoadParts.IntersectionGroup.Generate(genState, vertices, uvs, outTris, partsInfo, discordRoads, curType, sidePoints, sidePointStartRoads, sidePointEndRoads);
            //TODO: minimize submeshes if needed

            //assign all
            mr.materials = GetMaterialSet(genState.srManager.subRoadTextures, genState.sidewalkEnds);
            m.Clear();
            m.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.subMeshCount = outTris.Count;
            m.vertices = vertices.ToArray();
            for (int i = 0; i < outTris.Count; i++) {
                m.SetIndices(outTris[i], MeshTopology.Triangles, i);
            }
            m.SetUVs(0, uvs);
            m.RecalculateNormals();
            m.RecalculateBounds();
            m.name = parentIntersection.geo.name;

            //refresh the collider
            mc.enabled = false;
            mc.enabled = true;
        } catch (System.Exception e) {
            Debug.LogWarning("Error during mesh generation on intersection " + gameObject.name + ": " + e.StackTrace.ToString());
            mc.enabled = false;
        }
    }

    void ProcessRoad(int i, List<Road> roads, List<int> sortOrder, bool isThroughRoad, List<List<Vector3>> throughDirections,
        List<GameObject> roadsThrough, List<int> startRoads, int endRoad) {

        var j = i + 1;
        if (i == roads.Count - 1) j = 0;

        var trueI = sortOrder[i];
        var trueJ = sortOrder[j];

        var roadI = roads[trueI];
        var roadJ = roads[trueJ];

        var centerI = roadCenters[trueI];
        var centerJ = roadCenters[trueJ];
        var centerI2 = roadCenters2[trueI];
        var centerJ2 = roadCenters2[trueJ];
        var centerI2P = roadCentersPlusDir[trueI];
        var centerJ2P = roadCentersPlusDir[trueJ];
        var leftIs = new List<Vector3>();
        var leftJs = new List<Vector3>();
        var junction = curType.junctionTypes[(roadI.GetRoadType().name, roadJ.GetRoadType().name)];

        //roadI section vertices
        foreach (var sf in junction.typeData.standardFloatsBoolsAndInts) {
            junction.sectionVerticesVS.SetFloat(sf, roadI.GetStandardFloat(sf));
        }
        foreach (var sf in junction.typeData.standardVec3s) {
            junction.sectionVerticesVS.SetVector3(sf, roadI.GetStandardVec3(sf));
        }
        foreach (var sf in junction.typeData.standardVec2s) {
            junction.sectionVerticesVS.SetVector2(sf, roadI.GetStandardVec2(sf));
        }
        foreach (var sv in junction.sectionVerticesCalculators) {
            leftIs.Add(roadLefts[trueI] * sv.GetValue(junction.sectionVerticesVS));
        }

        //road J section vertices
        foreach (var sf in junction.typeData.standardFloatsBoolsAndInts) {
            junction.sectionVerticesVS.SetFloat(sf, roadJ.GetStandardFloat(sf));
        }
        foreach (var sf in junction.typeData.standardVec3s) {
            junction.sectionVerticesVS.SetVector3(sf, roadJ.GetStandardVec3(sf));
        }
        foreach (var sf in junction.typeData.standardVec2s) {
            junction.sectionVerticesVS.SetVector2(sf, roadJ.GetStandardVec2(sf));
        }
        foreach (var sv in junction.sectionVerticesCalculators) {
            leftJs.Add(roadLefts[trueJ] * sv.GetValue(junction.sectionVerticesVS));
        }

        var leftIRoad = leftIs[junction.typeData.roadSplineVertex];
        var leftJRoad = leftJs[junction.typeData.roadSplineVertex];

        //find out if the spline is self intersecting (at least the case where the total intersection size increase of any road is negative)
        var planeSW = new Plane(roadLefts[trueI], centerI);
        var d0 = planeSW.GetDistanceToPoint(centerI + leftIs[0] * GeometryHelper.epsilon);
        var d1 = planeSW.GetDistanceToPoint(centerJ - leftJs[0] * GeometryHelper.epsilon);
        var convex = Mathf.Sign(d0) != Mathf.Sign(d1);
        var szInc = parentIntersection.state.Float("sizeIncrease");
        var selfIntersectingSpline = !convex && (roadI.GetIntersectionAdd(parentIntersection) + szInc < 0 || roadJ.GetIntersectionAdd(parentIntersection) + szInc < 0);
        genState.convexes.Add(convex);
        genState.selfIntersectingSplines.Add(selfIntersectingSpline);
        genState.sidewalkEnds.Add((roadI, roadJ));

        //get road texture and start
        var defaultTex = parentIntersection.state.Str(curType.typeData.settings.roadTexture);
        var texI = roads[trueI].GetIntersectionTexture(parentIntersection);
        var texIIsDefault = texI == "" || texI == null;
        if (texIIsDefault) texI = defaultTex;
        var IIsRoadStart = startRoads.Contains(trueI);
        var IIsRoadEnd = trueI == endRoad;
        if (IIsRoadStart || IIsRoadEnd) {
            texIIsDefault = false;
            texI = "";
        }

        var texJ = roads[trueJ].GetIntersectionTexture(parentIntersection);
        var texJIsDefault = texJ == "" || texJ == null;
        if (texJIsDefault) texJ = defaultTex;
        var JIsRoadStart = startRoads.Contains(trueJ);
        var JIsRoadEnd = trueJ == endRoad;
        if (JIsRoadStart || JIsRoadEnd) {
            texJIsDefault = false;
            texJ = "";
        }

        var IsMainRoad = isThroughRoad && ((startRoads.Contains(trueI) && trueJ == endRoad) || (startRoads.Contains(trueJ) && trueI == endRoad));
        var isEnd = trueI == endRoad;

        var thisSidewalk = new List<Vector3>();
        var thisGroundHeights = new List<float>();

        foreach (var sv in leftIs) {
            thisSidewalk.Add(centerI + sv);
        }
        foreach (var sv in leftIs) {
            thisSidewalk.Add(centerI2 + sv);
        }

        thisGroundHeights.Add(upFactors2[trueI].y);
        thisGroundHeights.Add(upFactors2[trueI].y);
        var ILeftPoint = centerI2 + leftIRoad - upFactors2[trueI];
        var IRightPoint = centerI2 - leftIRoad - upFactors2[trueI];

        var startIndex = startRoads.Contains(trueI) ? trueI : startRoads.Contains(trueJ) ? trueJ : -1;
        var indexInList = startRoads.IndexOf(startIndex);
        RoadGenerator throughComp = null;
        float throughWidth = 0;
        var sameMainRoad = false;
        if (IsMainRoad) {
            if (indexInList != -1) {
                throughComp = roadsThrough[indexInList].GetComponent<RoadGenerator>();
                throughWidth = throughComp.GetStandardFloat("totalWidth");
                if (throughDirections[indexInList].Count > 0) {
                    sameMainRoad = true;
                } else {
                    var dir1 = (roadCentersPlusDir[trueI] - roadCenters[trueI]).normalized;
                    throughDirections[indexInList].Add(dir1);
                    var throughStart = roadCenters2[startIndex];
                    var throughEnd = roadCenters2[endRoad] + roadLefts[endRoad] * roads[startIndex].instanceState.Float("intersectionMove");
                    throughComp.start = startIndex == trueI ? throughStart : throughEnd;
                    throughComp.end = startIndex == trueI ? throughEnd : throughStart;
                }
            }
        }

        List<Vector3> roadThroughSegment = null;
        if (isThroughRoad && IsMainRoad && indexInList != -1) {
            var move = isEnd ? (leftIRoad.normalized * (roadJ.instanceState.Float("intersectionMove") - throughWidth) * 0.5f) : Vector3.zero;
            var firstPoint = isEnd ? (centerI2 + move - upFactors2[trueI]) : IRightPoint;
            roadThroughSegment = new List<Vector3>() { firstPoint };
        }

        //intermediate segments
        var curveControlPoints = new List<List<Vector3>>();
        for(int vi = 0; vi < leftIs.Count; vi++) {
            var curveControlPoint = GeometryHelper.GetLineIntersection(centerI + leftIs[vi], centerI2P + leftIs[vi], centerJ - leftJs[vi], centerJ2P - leftJs[vi]);
            curveControlPoints.Add(new List<Vector3>() { curveControlPoint });
            var r1p = new Plane(centerI2P + leftIs[vi], centerI2P, centerI2P + Vector3.up);
            var r2p = new Plane(centerJ2P - leftJs[vi], centerJ2P, centerJ2P + Vector3.up);
            if (r1p.SameSide(curveControlPoint, centerI) || r2p.SameSide(curveControlPoint, centerJ)) {
                curveControlPoints[vi] = new List<Vector3>(); //otherwise it breaks in some cases
            }
        }

        var sidewalkRoadSegment = new List<Vector3>() { ILeftPoint };

        var notDefault = (!texIIsDefault && texI != "") || (!texJIsDefault && texJ != "");
        genState.notDefaultTexes.Add(notDefault);
        var junc = genState.GetJunctionType(curType, i);
        genState.GetJunctionState(curType, i); //for initialization
        var thisSidewalkSegments = (int)junc.segmentsCalculator.GetValue(junc.variableContainer);

        System.Func<int, float, Vector3> sectionCenterK = (vi, alpha) => {
            return GeometryHelper.GetPointOnCurve(centerI2 + leftIs[vi], curveControlPoints[vi], centerJ2 - leftJs[vi], alpha, 0, GeometryHelper.CurveType.Bezier);
        };

        for (int k = 1; k < thisSidewalkSegments; k++) {
            var alpha = ((float)k) / parentIntersection.state.Int("sidewalkSegments", 1);

            var sectionCenters = new List<Vector3>();
            for (int vi = 0; vi < leftIs.Count; vi++) {
                sectionCenters.Add(sectionCenterK(vi, alpha));
            }

            var centerInsideK = sectionCenters[junction.typeData.roadSplineVertex];
            var centerInsideKPlus = sectionCenterK(junction.typeData.roadSplineVertex, alpha + GeometryHelper.epsilon);
            var centerInsideKDir = (centerInsideKPlus - centerInsideK).normalized;
            var centerInsideKLeft = Vector3.Cross(centerInsideKDir, Vector3.up).normalized;
            var upFactor2K = Vector3.Lerp(upFactors2[trueI], upFactors2[trueJ], alpha);
            var centerInsideKGround = centerInsideK - upFactor2K;

            thisSidewalk.AddRange(sectionCenters);
            thisGroundHeights.Add(upFactor2K.y);
            sidewalkRoadSegment.Add(centerInsideKGround);

            if (isThroughRoad && IsMainRoad && indexInList != -1) {
                var throughCenter = centerInsideK - throughWidth * 0.5f * centerInsideKLeft;
                if (sameMainRoad) {
                    var t = throughComp.cps.Count - 1;
                    var throughCenter_Other = throughComp.cps[t - (k - 1)];
                    var centerInsideKDir_Other = throughDirections[indexInList][2 + t - k];
                    var centerInsideKLeft_Other = Vector3.Cross(centerInsideKDir_Other, Vector3.up).normalized;
                    var centerInsideK_Other = throughCenter_Other + throughWidth * 0.5f * centerInsideKLeft_Other;
                    throughComp.cps[t - (k - 1)] = (centerInsideK + centerInsideK_Other) * 0.5f;
                    var mult = (centerInsideK - centerInsideK_Other).magnitude / throughWidth;
                    throughDirections[indexInList][2 + t - k] = Vector3.Cross(centerInsideK - centerInsideK_Other, Vector3.up).normalized * mult;
                } else {
                    throughComp.cps.Add(throughCenter);
                    throughDirections[indexInList].Add(centerInsideKDir);
                    var mult = isEnd ? 1 : 1;
                    roadThroughSegment.Add(centerInsideKGround - throughWidth * centerInsideKLeft * mult);
                }
            }
        }
        if (IsMainRoad && !sameMainRoad) {
            if (indexInList != -1) {
                var dir2 = (roadCenters[trueJ] - roadCentersPlusDir[trueJ]).normalized;
                throughComp.segments = thisSidewalkSegments + 1;
                throughDirections[indexInList].Add(dir2);
            }
        }

        foreach (var sv in leftJs) {
            thisSidewalk.Add(centerJ2 - sv);
        }
        foreach (var sv in leftJs) {
            thisSidewalk.Add(centerJ - sv);
        }

        thisGroundHeights.Add(upFactors2[trueJ].y);
        thisGroundHeights.Add(upFactors2[trueJ].y);

        var JLeftPoint = centerJ2 - leftJRoad - upFactors2[trueJ];
        sidewalkRoadSegment.Add(JLeftPoint);

        genState.sidewalkVertices.Add(thisSidewalk);
        genState.groundHeights.Add(thisGroundHeights);
        if (roadThroughSegment != null) {
            var move = leftJRoad.normalized * (throughWidth * 0.5f);
            roadThroughSegment.Add((isEnd ? (centerJ2) : throughComp.end) + move - upFactors2[trueJ]);
        }

        // Road patch
        if (lastI == -1) lastI = sortOrder[sortOrder.Count - 1];
        if (lastTex == "") lastTex = roads[lastI].GetIntersectionTexture(parentIntersection);
        var LastIIsRoadStart = startRoads.Contains(lastI);
        var drawingBetweenRoads = JIsRoadStart && (IIsRoadStart || LastIIsRoadStart) && trueI != endRoad;
        var drawnAlongRoad = false;
        var plane = new Plane(roadLefts[trueI], roadCenters[trueI]);
        genState.planes.Add(plane);

        if (texI == "") {
            lastSubRoad = curSubRoad;
            curSubRoad = -1;
        } else if (curSubRoad == -1 || texI != genState.srManager.subRoadTextures[curSubRoad]) {
            curSubRoad = genState.srManager.CreateSubRoad(texI, texIIsDefault, endRoad != -1, new Vector2(1, 1), defaultTex);
        }

        if (curSubRoad != -1) {
            genState.srManager.AddSegmentToSubroad(curSubRoad, new List<Vector3>() { IRightPoint, ILeftPoint }, plane, false);
            if (!(texJ == texI || texJIsDefault || texJ == "")) {
                curSubRoad = genState.srManager.CreateSubRoad(texJ, texJIsDefault, endRoad != -1, new Vector2(1, 1), defaultTex);
            }
            genState.srManager.AddSegmentToSubroad(curSubRoad, sidewalkRoadSegment, plane, false);
        } else {
            if (texJ != "") {
                curSubRoad = genState.srManager.CreateSubRoad(texJ, texJIsDefault, endRoad != -1, new Vector2(1, 1), defaultTex);
                genState.srManager.AddSegmentToSubroad(curSubRoad, sidewalkRoadSegment, plane, false);
            } else {
                var defTex = curType.typeData.settings.roadTexture;
                if (drawingBetweenRoads) {
                    if (lastDrawnAlongRoad && lastSubRoad != -1) {
                        curSubRoad = lastSubRoad;
                    } else {
                        curSubRoad = genState.srManager.CreateSubRoad(parentIntersection.state.Str(defTex), true, endRoad != -1, new Vector2(1, 1), defaultTex);
                    }
                    genState.srManager.AddSegmentToSubroad(curSubRoad, sidewalkRoadSegment, plane, false);
                } else {
                    curSubRoad = lastSubRoad;
                    if (curSubRoad == -1) {
                        curSubRoad = genState.srManager.CreateSubRoad(parentIntersection.state.Str(defTex), true, endRoad != -1, new Vector2(1, 1), defaultTex); //TODO maybe: check why lastTex+lastTexIsDefault breaks it
                    }
                    drawnAlongRoad = true;
                    genState.srManager.AddSegmentToSubroad(curSubRoad, roadThroughSegment, plane, true);
                    lastSubRoad = curSubRoad;
                    curSubRoad = -1;
                }
            }
        }
        lastDrawnAlongRoad = drawnAlongRoad;
        lastTex = texI;
        lastTexIsDefault = texIIsDefault;
        lastI = trueI;
    }
}

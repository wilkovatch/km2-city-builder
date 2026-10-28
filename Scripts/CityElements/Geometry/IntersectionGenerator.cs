using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using PhysicsScriptable = Godot.StaticBody3D;
using GameObject = Godot.Node3D;
using Mesh = Godot.ArrayMesh;
using MeshRenderer = Godot.MeshInstance3D;
using MeshCollider = Godot.CollisionShape3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using States;

public partial class IntersectionGenerator : PhysicsScriptable {
    public MeshRenderer mr;
    MeshCollider mc;
    public Mesh m;
    Intersection parentIntersection;

    List<Vector3> roadCenters = new List<Vector3>();
    List<Vector3> roadCenters2 = new List<Vector3>();
    List<Vector3> roadCentersPlusDir = new List<Vector3>();
    List<Vector3> roadLefts = new List<Vector3>();
    List<Vector3> roadLeftDirections = new List<Vector3>(); //normalized and horizontal
    List<float> roadBankSlopes = new List<float>(); //height per meter to the left, where the end of the road is rolled
    List<float> roadGroundScales = new List<float>(); //of the widths of the roads (a rolled end is narrower on the ground)
    List<float> roadHeights = new List<float>(); //of the road ends relative to the point
    List<float> roadYaws = new List<float>(); //of the road ends (degrees), their mouths are skewed
    List<float> roadThroughRolls = new List<float>(); //of a road through the intersection, with its right vector to the left
    List<Vector3> roadPlaneNormals = new List<Vector3>(); //of the planes along the roads
    List<Vector3> upFactors2 = new List<Vector3>();
    List<List<Vector2>> throughRotations = new List<List<Vector2>>(); //of the sections of the roads through, as their directions
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
    public int roadEndLinesStart = 0; //the side point lines from this one on are the ends of the roads (see AddRoadEndLines)

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
            return curType.GetJunctionType(sidewalkEnds[i].Item1.GetRoadType().name, sidewalkEnds[i].Item2.GetRoadType().name);
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
                        tempState.SetInt(param.newName, gen.HasFloat(param.name) ? (int)gen.GetFloat(param.name) : 0);
                        break;
                    case "bool":
                        tempState.SetBool(param.newName, gen.HasBool(param.name) && gen.GetBool(param.name));
                        break;
                    case "float":
                        tempState.SetFloat(param.newName, gen.HasFloat(param.name) ? gen.GetFloat(param.name) : 0.0f);
                        break;
                    case "vec2":
                        tempState.SetVector2(param.newName, gen.HasVec2(param.name) ? gen.GetVec2(param.name) : GameEngine.Vector2.zero);
                        break;
                    case "vec3":
                        tempState.SetVector3(param.newName, gen.HasVec3(param.name) ? gen.GetVec3(param.name) : GameEngine.Vector3.zero);
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
        (mr, m) = GameEngine.Mesh.CreateMesh(GO.GetObject(this), new Material[0]);
        mc = GameEngine.Mesh.CreateCollider(GO.GetObject(this), m);
        GO.SetLayer(this, 7);
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
                res.Add(MaterialManager.GetMaterial(MaterialManager.GetTextureName(tempState, tex)));
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
        foreach (var directions in throughDirections) throughRotations.Add(new List<Vector2>());
        CalculateVerticesAndCenters();
        var pointCenter = GO.GetPosition(parentIntersection.point);
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
        AddRoadEndLines();
    }

    //a line across the end of each road, with the endAnchors of its type (for the low-level elements only), after the other
    //lines so that the indices of their anchors don't change; not connected to the anchors of the roads
    void AddRoadEndLines() {
        roadEndLinesStart = sidePoints.Count;
        var done = new HashSet<Road>();
        foreach (var road in parentIntersection.roads) {
            if (road == null || !done.Add(road) || road.generator == null) continue;
            if (road.startIntersection == parentIntersection) AddRoadEndLine(road.generator.startEndAnchors);
            if (road.endIntersection == parentIntersection) AddRoadEndLine(road.generator.endEndAnchors);
        }
    }

    void AddRoadEndLine(List<Vector3> points) {
        var line = new List<Vector3>();
        foreach (var p in points) {
            if (line.Count == 0 || GameEngine.Vector3.Distance(line[line.Count - 1], p) >= 0.01f) line.Add(p);
        }
        if (line.Count == 0) return;
        sidePoints.Add(line);
        sidePointStartRoads.Add(null);
        sidePointEndRoads.Add(null);
    }

    //a seamless intersection has no surface: only the terrain anchor lines joining the edges of its two roads, from the
    //ends of the anchor lines of the first road to the closest ones of the second (a single point where they meet)
    public void RebuildSeamless() {
        Clear();
        curType = GetIntersectionType();
        var roads = parentIntersection.roads;
        sortOrder = new List<int> { 0, 1 };
        var ends0 = GetAnchorLineEnds(roads[0]);
        var ends1 = GetAnchorLineEnds(roads[1]);
        foreach (var p0 in ends0) {
            if (ends1.Count == 0) break;
            var p1 = ends1[0];
            foreach (var p in ends1) {
                if (GameEngine.Vector3.Distance(p, p0) < GameEngine.Vector3.Distance(p1, p0)) p1 = p;
            }
            var line = new List<Vector3> { p0 };
            if (GameEngine.Vector3.Distance(p0, p1) >= 0.01f) line.Add(p1);
            sidePoints.Add(line);
            sidePointStartRoads.Add(roads[0]);
            sidePointEndRoads.Add(roads[1]);
        }
        AddRoadEndLines();
        GameEngine.Mesh.AssignData(m, new Material[0], new List<int[]>(), new List<Vector3>(), new List<Vector2>(), GO.GetName(parentIntersection.geo));
        GameEngine.Mesh.RefreshCollider(m, mc, false);
    }

    //the points of the anchor lines of the road at its end on the intersection
    List<Vector3> GetAnchorLineEnds(Road road) {
        var res = new List<Vector3>();
        var isStart = road.startIntersection == parentIntersection;
        foreach (var line in road.generator.sidePoints) {
            if (line.Count > 0) res.Add(isStart ? line[0] : line[line.Count - 1]);
        }
        return res;
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
        roadLeftDirections.Clear();
        roadBankSlopes.Clear();
        roadGroundScales.Clear();
        roadHeights.Clear();
        roadYaws.Clear();
        roadThroughRolls.Clear();
        roadPlaneNormals.Clear();
        upFactors2.Clear();
        throughRotations.Clear();
        curSubRoad = -1;
        lastSubRoad = -1;
        lastI = -1;
        lastTex = "";
        lastTexIsDefault = false;
        lastDrawnAlongRoad = false;

        sidePoints.Clear();
        sidePointStartRoads.Clear();
        sidePointEndRoads.Clear();
        roadEndLinesStart = 0;

        partsInfo.Clear();
    }

    void CalculateVerticesAndCenters() {
        var roads = parentIntersection.roads;
        var sizes = parentIntersection.sizes;
        var pointCenter = GO.GetPosition(parentIntersection.point);

        //calculate road and crosswalk vertices and get road centers (for sidewalk vertices)
        for (int i = 0; i < roads.Count; i++) {
            //a rolled road end is narrower on the ground, and its vertices are moved vertically only, along its slope; a
            //yawed one is skewed (see Intersection.GetMouth)
            var mouth = parentIntersection.GetMouth(roads[i], pointCenter);
            var dir = mouth.dir;
            roadLeftDirections.Add(mouth.leftDir);
            roadBankSlopes.Add(mouth.slope);
            roadGroundScales.Add(mouth.groundScale);
            roadHeights.Add(parentIntersection.GetConnectionHeight(i));
            roadYaws.Add(mouth.yaw * GameEngine.Math.rad2deg);
            roadThroughRolls.Add(mouth.throughRoll);
            var left = mouth.left;
            roadPlaneNormals.Add(mouth.yaw == 0 ? left : mouth.leftDir);
            var upFactor2 = GameEngine.Vector3.up * roads[i].GetStandardFloat("height");
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
                thisCrosswalkVertices.Add(Bank(i, roadCenter + leftFactor - upFactor2));
                thisCrosswalkVertices.Add(Bank(i, roadCenter2 + leftFactor - upFactor2));
                thisCrosswalkVertices.Add(Bank(i, roadCenter - leftFactor - upFactor2));
                thisCrosswalkVertices.Add(Bank(i, roadCenter2 - leftFactor - upFactor2));
                genState.crosswalkVertices.Add(thisCrosswalkVertices);
            }
        }
    }

    //the height of a point near the end of a road: its connection height, plus its offset from the center line along
    //the slope where the end is rolled
    float BankHeight(int road, Vector3 point) {
        if (roadBankSlopes[road] == 0) return roadHeights[road];
        return roadHeights[road] + roadBankSlopes[road] * GameEngine.Vector3.Dot(point - roadCenters[road], roadLeftDirections[road]);
    }

    Vector3 Bank(int road, Vector3 point) {
        if (roadBankSlopes[road] == 0 && roadHeights[road] == 0) return point;
        return point + GameEngine.Vector3.up * BankHeight(road, point);
    }

    //the rotation (yaw, roll) of a road through the intersection at the end of a road, with its right vector to the left
    //(toward the center) or to the right (away from it) of the road there
    Vector2 GetThroughRotation(int road, bool rightToTheLeft) {
        var roll = roadThroughRolls[road];
        return new Vector2(roadYaws[road], rightToTheLeft ? roll : -roll);
    }

    //the direction along the mouth of a road of one of its section vertices (with the length of the mouth per unit of width)
    Vector3 MouthDirection(int road, Vector3 vertexOffset) {
        if (roadYaws[road] == 0) return GameEngine.Vector3.Normalize(vertexOffset) * roadGroundScales[road];
        var sign = GameEngine.Vector3.Dot(vertexOffset, roadLeftDirections[road]) < 0 ? -1 : 1;
        return roadLefts[road] * (2.0f * sign);
    }

    //the offset across a road through the intersection, from the side to the left of its direction: rolled (the width
    //on the ground is narrower, the far side higher or lower) and yawed (skewed along the direction)
    static Vector3 ThroughOffset(float width, Vector3 dir, Vector3 left, float yaw, float roll) {
        var rollRad = roll * GameEngine.Math.deg2rad;
        var yawRad = yaw * GameEngine.Math.deg2rad;
        var horizontal = (left + GameEngine.Vector3.Normalize(dir) * Mathf.Tan(yawRad)) * (width * Mathf.Cos(rollRad));
        return -horizontal + GameEngine.Vector3.up * (width * Mathf.Sin(rollRad) / Mathf.Cos(yawRad));
    }

    void RebuildRoadsThrough(List<GameObject> roadsThrough, List<List<Vector3>> throughDirections, bool isThroughRoad, List<int> startRoads, int endRoad) {
        for (int i = 0; i < roadsThrough.Count; i++) {
            var roadThrough = roadsThrough[i];
            var roadGen = GS.FindRecursive<RoadGenerator>(roadThrough);
            if (throughDirections[i].Count == 0) {
                roadGen.segments = 2;
                roadGen.start = Bank(startRoads[i], roadCenters2[startRoads[i]]);
                roadGen.end = Bank(endRoad, roadCenters2[endRoad]);
                roadGen.valid = true;
                var dir1 = GameEngine.Vector3.Normalize(roadCentersPlusDir[startRoads[i]] - roadCenters[startRoads[i]]);
                var dir2 = GameEngine.Vector3.Normalize(roadCenters[endRoad] - roadCentersPlusDir[endRoad]);
                throughDirections[i].AddRange(new List<Vector3>() { dir1, dir2 });
                throughRotations[i].Clear();
                throughRotations[i].Add(GetThroughRotation(startRoads[i], true));
                throughRotations[i].Add(GetThroughRotation(endRoad, false));
            }
            roadGen.Rebuild(throughDirections[i], throughRotations[i]);
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
            var materials = GetMaterialSet(genState.srManager.subRoadTextures, genState.sidewalkEnds);
            //TODO: minimize submeshes if needed

            //assign all
            GameEngine.Mesh.AssignMaterials(mr, materials);
            GameEngine.Mesh.AssignData(m, materials, GameEngine.Mesh.AdaptList(outTris), vertices, uvs, GO.GetName(parentIntersection.geo));

            GameEngine.Mesh.RefreshCollider(m, mc, true);
        } catch (System.Exception e) {
            GameEngine.LogWarning("Error during mesh generation on intersection " + GO.GetName(this) + ": " + e.StackTrace.ToString());
            GameEngine.Mesh.RefreshCollider(m, mc, false);
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
        var junction = curType.GetJunctionType(roadI.GetRoadType().name, roadJ.GetRoadType().name);

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
        var planeSW = GameEngine.Plane.Create(roadPlaneNormals[trueI], centerI);
        var d0 = GameEngine.Plane.DistanceToPoint(planeSW, centerI + leftIs[0] * GeometryHelper.epsilon);
        var d1 = GameEngine.Plane.DistanceToPoint(planeSW, centerJ - leftJs[0] * GeometryHelper.epsilon);
        var convex = GameEngine.Math.Sign(d0) != GameEngine.Math.Sign(d1);
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
            thisSidewalk.Add(Bank(trueI, centerI + sv));
        }
        foreach (var sv in leftIs) {
            thisSidewalk.Add(Bank(trueI, centerI2 + sv));
        }

        thisGroundHeights.Add(upFactors2[trueI][1]);
        thisGroundHeights.Add(upFactors2[trueI][1]);
        var ILeftPoint = Bank(trueI, centerI2 + leftIRoad - upFactors2[trueI]);
        var IRightPoint = Bank(trueI, centerI2 - leftIRoad - upFactors2[trueI]);

        var startIndex = startRoads.Contains(trueI) ? trueI : startRoads.Contains(trueJ) ? trueJ : -1;
        var indexInList = startRoads.IndexOf(startIndex);
        RoadGenerator throughComp = null;
        float throughWidth = 0;
        var sameMainRoad = false;
        if (IsMainRoad) {
            if (indexInList != -1) {
                throughComp = GS.FindRecursive<RoadGenerator>(roadsThrough[indexInList]);
                throughWidth = throughComp.GetStandardFloat("totalWidth");
                if (throughDirections[indexInList].Count > 0) {
                    sameMainRoad = true;
                } else {
                    var dir1 = GameEngine.Vector3.Normalize(roadCentersPlusDir[trueI] - roadCenters[trueI]);
                    throughDirections[indexInList].Add(dir1);
                    throughRotations[indexInList].Add(GetThroughRotation(trueI, true));
                    var throughStart = Bank(startIndex, roadCenters2[startIndex]);
                    var throughEnd = Bank(endRoad, roadCenters2[endRoad] + roadLefts[endRoad] * roads[startIndex].instanceState.Float("intersectionMove"));
                    throughComp.start = startIndex == trueI ? throughStart : throughEnd;
                    throughComp.end = startIndex == trueI ? throughEnd : throughStart;
                }
            }
        }

        List<Vector3> roadThroughSegment = null;
        if (isThroughRoad && IsMainRoad && indexInList != -1) {
            var move = isEnd ? (MouthDirection(trueI, leftIRoad) * (roadJ.instanceState.Float("intersectionMove") - throughWidth) * 0.5f) : GameEngine.Vector3.zero;
            var firstPoint = isEnd ? Bank(trueI, centerI2 + move - upFactors2[trueI]) : IRightPoint;
            roadThroughSegment = new List<Vector3>() { firstPoint };
        }

        //intermediate segments
        var curveControlPoints = new List<List<Vector3>>();
        for(int vi = 0; vi < leftIs.Count; vi++) {
            var curveControlPoint = GeometryHelper.GetLineIntersection(centerI + leftIs[vi], centerI2P + leftIs[vi], centerJ - leftJs[vi], centerJ2P - leftJs[vi]);
            curveControlPoints.Add(new List<Vector3>() { curveControlPoint });
            var r1p = GameEngine.Plane.Create(centerI2P + leftIs[vi], centerI2P, centerI2P + GameEngine.Vector3.up);
            var r2p = GameEngine.Plane.Create(centerJ2P - leftJs[vi], centerJ2P, centerJ2P + GameEngine.Vector3.up);
            if (GameEngine.Plane.SameSide(r1p, curveControlPoint, centerI) || GameEngine.Plane.SameSide(r2p, curveControlPoint, centerJ)) {
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

            var centerInsideKFlat = sectionCenters[junction.typeData.roadSplineVertex];
            var centerInsideKPlus = sectionCenterK(junction.typeData.roadSplineVertex, alpha + GeometryHelper.epsilon);
            var centerInsideKDir = GameEngine.Vector3.Normalize(centerInsideKPlus - centerInsideKFlat);
            for (int vi = 0; vi < leftIs.Count; vi++) {
                var heightI = BankHeight(trueI, centerI2 + leftIs[vi]);
                var heightJ = BankHeight(trueJ, centerJ2 - leftJs[vi]);
                if (heightI != 0 || heightJ != 0) sectionCenters[vi] += GameEngine.Vector3.up * Mathf.Lerp(heightI, heightJ, alpha);
            }
            var centerInsideK = sectionCenters[junction.typeData.roadSplineVertex];
            var centerInsideKLeft = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(centerInsideKDir, GameEngine.Vector3.up));
            var upFactor2K = GameEngine.Vector3.Lerp(upFactors2[trueI], upFactors2[trueJ], alpha);
            var centerInsideKGround = centerInsideK - upFactor2K;

            thisSidewalk.AddRange(sectionCenters);
            thisGroundHeights.Add(upFactor2K[1]);
            sidewalkRoadSegment.Add(centerInsideKGround);

            if (isThroughRoad && IsMainRoad && indexInList != -1) {
                if (sameMainRoad) {
                    //the road through spans from this corner to the one on the other side, rolled if they are at different heights
                    var t = throughComp.cps.Count - 1;
                    var throughCenter_Other = throughComp.cps[t - (k - 1)];
                    var centerInsideKDir_Other = throughDirections[indexInList][2 + t - k];
                    var rotation_Other = throughRotations[indexInList][2 + t - k];
                    var roll_Other = rotation_Other[1] * GameEngine.Math.deg2rad;
                    var centerInsideKLeft_Other = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(centerInsideKDir_Other, GameEngine.Vector3.up));
                    Vector3 centerInsideK_Other;
                    if (rotation_Other[0] != 0) {
                        centerInsideK_Other = throughCenter_Other - ThroughOffset(throughWidth, centerInsideKDir_Other, centerInsideKLeft_Other, rotation_Other[0], rotation_Other[1]) * 0.5f;
                    } else {
                        var otherOffset = roll_Other == 0 ? centerInsideKLeft_Other
                            : centerInsideKLeft_Other * Mathf.Cos(roll_Other) - GameEngine.Vector3.up * Mathf.Sin(roll_Other);
                        centerInsideK_Other = throughCenter_Other + throughWidth * 0.5f * otherOffset;
                    }
                    throughComp.cps[t - (k - 1)] = (centerInsideK + centerInsideK_Other) * 0.5f;
                    var span = centerInsideK - centerInsideK_Other;
                    var mult = GameEngine.Vector3.Magnitude(span) / throughWidth;
                    var spanDirection = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(span, GameEngine.Vector3.up)) * mult;
                    var flatSpan = Mathf.Sqrt(span[0] * span[0] + span[2] * span[2]);
                    var spanRoll = Mathf.Abs(span[1]) < GeometryHelper.epsilon ? 0.0f : Mathf.Atan2(span[1], flatSpan) * GameEngine.Math.rad2deg;
                    var spanYaw = 0.0f;
                    if (roadYaws[trueI] != 0 || roadYaws[trueJ] != 0) {
                        //between skewed mouths the span is skewed too: the direction follows the two sides (this one runs
                        //the other way) and the yaw turns the section onto the span, keeping the edges on the sides
                        var travel = GameEngine.Vector3.Normalize(centerInsideKDir_Other) - centerInsideKDir;
                        travel = new Vector3(travel[0], 0, travel[2]);
                        if (GameEngine.Vector3.SqrMagnitude(travel) > GeometryHelper.epsilon) {
                            travel = GameEngine.Vector3.Normalize(travel);
                            var sectionRight = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(travel, GameEngine.Vector3.up));
                            var spanRight = -GameEngine.Vector3.Normalize(new Vector3(span[0], 0, span[2]));
                            var yawCos = GameEngine.Vector3.Dot(spanRight, sectionRight);
                            if (yawCos > Mathf.Cos(GeometryHelper.MaxSectionYaw * GameEngine.Math.deg2rad)) {
                                spanYaw = Mathf.Atan2(GameEngine.Vector3.Dot(spanRight, travel), yawCos) * GameEngine.Math.rad2deg;
                                spanDirection = travel * (mult * yawCos);
                            }
                        }
                    }
                    throughDirections[indexInList][2 + t - k] = spanDirection;
                    throughRotations[indexInList][2 + t - k] = new Vector2(spanYaw, spanRoll);
                } else {
                    //the rotation blends from the one of road I to the one of road J, and so does the width on the ground
                    var throughRotation = GetThroughRotation(trueI, true).Lerp(GetThroughRotation(trueJ, false), alpha);
                    var throughRoll = throughRotation[1];
                    var throughYaw = throughRotation[0];
                    var throughCenter = centerInsideK - throughWidth * 0.5f * centerInsideKLeft;
                    var segmentPoint = centerInsideKGround - throughWidth * centerInsideKLeft;
                    if (throughYaw != 0) {
                        var offset = ThroughOffset(throughWidth, centerInsideKDir, centerInsideKLeft, throughYaw, throughRoll);
                        throughCenter = centerInsideK + offset * 0.5f;
                        segmentPoint = centerInsideKGround + offset;
                    } else if (throughRoll != 0) {
                        var rollRad = throughRoll * GameEngine.Math.deg2rad;
                        var groundWidth = throughWidth * Mathf.Cos(rollRad);
                        var rise = throughWidth * Mathf.Sin(rollRad);
                        throughCenter = centerInsideK - groundWidth * 0.5f * centerInsideKLeft + GameEngine.Vector3.up * (rise * 0.5f);
                        segmentPoint = centerInsideKGround - groundWidth * centerInsideKLeft + GameEngine.Vector3.up * rise;
                    }
                    throughComp.cps.Add(throughCenter);
                    throughDirections[indexInList].Add(centerInsideKDir);
                    throughRotations[indexInList].Add(new Vector2(throughYaw, throughRoll));
                    roadThroughSegment.Add(segmentPoint);
                }
            }
        }
        if (IsMainRoad && !sameMainRoad) {
            if (indexInList != -1) {
                var dir2 = GameEngine.Vector3.Normalize(roadCenters[trueJ] - roadCentersPlusDir[trueJ]);
                throughComp.segments = thisSidewalkSegments + 1;
                throughDirections[indexInList].Add(dir2);
                throughRotations[indexInList].Add(GetThroughRotation(trueJ, false));
            }
        }

        foreach (var sv in leftJs) {
            thisSidewalk.Add(Bank(trueJ, centerJ2 - sv));
        }
        foreach (var sv in leftJs) {
            thisSidewalk.Add(Bank(trueJ, centerJ - sv));
        }

        thisGroundHeights.Add(upFactors2[trueJ][1]);
        thisGroundHeights.Add(upFactors2[trueJ][1]);

        var JLeftPoint = Bank(trueJ, centerJ2 - leftJRoad - upFactors2[trueJ]);
        sidewalkRoadSegment.Add(JLeftPoint);

        genState.sidewalkVertices.Add(thisSidewalk);
        genState.groundHeights.Add(thisGroundHeights);
        if (roadThroughSegment != null) {
            var move = MouthDirection(trueJ, leftJRoad) * (throughWidth * 0.5f);
            //the end of the road through is already raised: from its flat position
            var throughEndFlat = isEnd ? centerJ2 : throughComp.end - GameEngine.Vector3.up * BankHeight(trueJ, throughComp.end);
            roadThroughSegment.Add(Bank(trueJ, throughEndFlat + move - upFactors2[trueJ]));
        }

        // Road patch
        if (lastI == -1) lastI = sortOrder[sortOrder.Count - 1];
        if (lastTex == "") lastTex = roads[lastI].GetIntersectionTexture(parentIntersection);
        var LastIIsRoadStart = startRoads.Contains(lastI);
        var drawingBetweenRoads = JIsRoadStart && (IIsRoadStart || LastIIsRoadStart) && trueI != endRoad;
        var drawnAlongRoad = false;
        var plane = GameEngine.Plane.Create(roadPlaneNormals[trueI], roadCenters[trueI]);
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

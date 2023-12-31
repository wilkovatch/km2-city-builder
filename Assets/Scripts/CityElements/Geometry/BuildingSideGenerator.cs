using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SubMeshData = GeometryHelpers.SubMesh.SubMeshData;
using RC = RuntimeCalculator;

public class BuildingSideGenerator: IObjectWithState {
    public Mesh colliderMesh;
    SubMeshData m;
    public MeshCollider mc;
    GameObject lineContainer;
    LineRenderer lr;
    Material orangeMat, greenMat;
    public Building building;
    public ObjectState State { get { return state; } set { state = value; SyncWithBuilding(); } }
    public ObjectState state; //DO NOT MODIFY DIRECTLY UNLESS ABSOLUTELY NECESSARY
    public Dictionary<int, SubMeshData> meshDict = new Dictionary<int, SubMeshData>();
    Dictionary<TempRectangleColumnIdentifier, List<TempRectangle>> tempRectangles = new Dictionary<TempRectangleColumnIdentifier, List<TempRectangle>>();
    public List<Facade> facades = new List<Facade>();
    Dictionary<int, bool> alwaysDraws = new Dictionary<int, bool>();
    List<SubMeshData> tempParamMeshes = new List<SubMeshData>();
    List<SubMeshData> meshParts = new List<SubMeshData>();
    public List<SubMeshData> finalParamMeshes = new List<SubMeshData>();
    public enum Side {
        Back, Front, Left, Right
    }
    Side side;
    bool deleted = false;

    CityElements.Types.Runtime.Buildings.BuildingType.Side curType;
    RC.VariableContainer variableContainer;

    List<Vector3> old_spline;
    float old_height;
    float old_trueHeight;
    float old_maxHeight;

    struct TempRectangle {
        public Vector3 relPos;
        public Vector3 absPos;
        public Vector3 scale;
        public Quaternion rot;
        public Facade facade;
        public ObjectState block;
        public int hash;
        public float x;
        public float totalWidth;
        public int y;
        public int len;
        public int blockIndex;
        public float height;
    }

    struct TempRectangleColumnIdentifier {
        public float x;
        public float totalWidth;
        public int len;
        public int hash;
    }

    public class Facade {
        public Vector3 forward = Vector3.zero;
        public Bounds bounds;
        public Dictionary<int, List<List<Matrix4x4>>> instances;
        public bool hasAlwaysDraws;
    }

    public BuildingSideGenerator(Building building, ObjectState state, Side side, bool outline = false) {
        this.building = building;
        Initialize(state, side);
        SetOutline(outline);
    }

    public void Initialize(ObjectState state, Side side, bool outline = false) {
        m = new SubMeshData();
        colliderMesh = new Mesh();
        mc = building.line.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = colliderMesh;

        lineContainer = new GameObject();
        lineContainer.name = "Line container";
        lineContainer.transform.parent = building.line.gameObject.transform;
        lr = lineContainer.AddComponent<LineRenderer>();
        lr.receiveShadows = false;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        orangeMat = MaterialManager.GetHandleMaterial((Color.red + Color.yellow) * 0.5f);
        greenMat = MaterialManager.GetHandleMaterial(Color.green);
        lr.material = greenMat;
        lr.startWidth = 0.1f;
        lr.endWidth = 0.1f;
        lr.enabled = outline;

        this.side = side;
        this.state = (ObjectState)state.Clone();
        SyncWithBuilding();
    }

    public ObjectState GetState() {
        return state;
    }

    void ReloadType() {
        var type = GetSideType();
        if (type != curType) {
            variableContainer = GetSideType().variableContainer.GetClone();
            curType = type;
        }
        curType.FillInitialVariables(variableContainer, state);
    }

    public CityElements.Types.Runtime.Buildings.BuildingType.Side GetSideType() {
        var dict = CityElements.Types.Parsers.TypeParser.GetBuildingTypes();
        return dict[state.Str("type", null)].side;
    }

    public void Delete() {
        Object.Destroy(mc);
        Object.Destroy(colliderMesh);
        Object.Destroy(lineContainer);
        deleted = true;
    }

    public void SetOutline(bool active, bool highlighted = true) {
        if (deleted) return;
        lineContainer.SetActive(active);
        lr.enabled = active;
        lr.material = active ? (highlighted ? greenMat : orangeMat) : greenMat;
    }

    void GenerateOutline(List<Vector3> spline, float height) {
        var lineVerts = new List<Vector3>();

        //bottom line
        lineVerts.AddRange(spline);

        //upper line
        var upperVerts = new List<Vector3>();
        upperVerts.AddRange(spline);
        upperVerts.Reverse();

        for (int i = 0; i < upperVerts.Count; i++) {
            var point = upperVerts[i];
            var point2 = new Vector3(point.x, height, point.z);
            upperVerts[i] = point2;
        }
        lineVerts.AddRange(upperVerts);

        //back to start
        lineVerts.Add(spline[0]);

        //apply
        lr.positionCount = lineVerts.Count;
        lr.SetPositions(lineVerts.ToArray());
    }

    SubMeshData GetRectBlock(ObjectState block, float xMult, float yMult, float width, float height) {
        var vc = variableContainer;
        var uMult = vc.floats[vc.floatIndex["uMult"]];
        var vMult = vc.floats[vc.floatIndex["vMult"]];

        var res = new SubMeshData();
        var p00 = new Vector3(0, 0, 0);
        var p10 = new Vector3(1, 0, 0);
        var p01 = new Vector3(0, 1, 0);
        var p11 = new Vector3(1, 1, 0);
        res.vertices = new Vector3[] { p00, p10, p01, p11 };
        res.uvs = new Vector2[] { new Vector2(xMult * uMult, yMult * vMult), new Vector2(0, yMult * vMult), new Vector2(xMult * uMult, 0), new Vector2(0, 0) };
        var indices = new int[] { 0, 2, 1, 1, 2, 3 };
        res.indices = new int[1][] { indices };
        res.materials = new string[] { block.Str("assetName") };
        res.scale = new Vector3(width, height, 1);
        return res;
    }

    void CreateParamMesh(ObjectState block, Vector3 relPos, Vector3 scale, bool bottom, bool bottomOnly, float yPos, float yPos2, float xMult) {
        var vc = variableContainer;
        var uMult = vc.floats[vc.floatIndex["uMult"]];
        var vMult = vc.floats[vc.floatIndex["vMult"]];

        var paramMesh = new SubMeshData();
        var rising = yPos2 > yPos;
        var prevBottom = 0.0f;
        var nextBottom = 0.0f;
        if (bottom) {
            if (bottomOnly) {
                if (rising) {
                    nextBottom = (yPos2 - yPos) / (scale.y / scale.z);
                } else {
                    prevBottom = (yPos - yPos2) / (scale.y / scale.z);
                }
            } else {
                if (rising) {
                    nextBottom = 1;
                } else {
                    prevBottom = 1;
                }
            }
        }

        var blockType = GetBlockType(block);
        var gen = new BuildingBlockGenerator(blockType, block);
        gen.Reset(blockType, block);

        gen.CalculateMesh(prevBottom, nextBottom, uMult, vMult, xMult, scale);
        paramMesh.vertices = gen.tempVertices.ToArray();
        paramMesh.uvs = gen.tempUVs.ToArray();
        paramMesh.indices = new int[gen.tempIndices.Count][];
        for(int i = 0; i < gen.tempIndices.Count; i++) {
            paramMesh.indices[i] = gen.tempIndices[i].ToArray();
        }
        paramMesh.materials = gen.GetMaterialSet();

        paramMesh.pos = relPos;
        paramMesh.scale = scale;
        tempParamMeshes.Add(paramMesh);
    }

    int GetRectHash(int blockIndex, int yMult, int xMult) {
        return blockIndex * 1024 * 1024 + yMult * 1024 * xMult;
    }

    void CreateRectMesh(ObjectState block, int blockIndex, Vector3 pos, Vector3 scale, Quaternion rotation, Facade facade, int xMult) {
        int blockHash = GetRectHash(blockIndex, 1, xMult);
        if (!meshDict.ContainsKey(blockHash)) {
            var res = GetRectBlock(block, xMult, 1, 1, 1);
            meshDict.Add(blockHash, res);
            if (!alwaysDraws.ContainsKey(blockHash)) alwaysDraws.Add(blockHash, false);
        }
        CreateMatrix(block, blockHash, pos, scale, rotation, facade);
    }

    void CreateRectMeshTemp(ObjectState block, int blockIndex, Vector3 pos, Vector3 relPos, Vector3 scale, Quaternion rotation, Facade facade, int xMult, float posX, float totalWidth, int posY, float floorHeight) {
        int blockHash = GetRectHash(blockIndex, 1, xMult);

        var rect = new TempRectangle();
        rect.relPos = relPos;
        rect.absPos = pos;
        rect.scale = scale;
        rect.rot = rotation;
        rect.facade = facade;
        rect.block = block;
        rect.hash = blockHash;
        rect.x = posX;
        rect.totalWidth = totalWidth;
        rect.y = posY;
        rect.len = xMult;
        rect.blockIndex = blockIndex;
        rect.height = floorHeight;

        var identifier = new TempRectangleColumnIdentifier();
        identifier.x = posX;
        identifier.hash = blockHash;
        identifier.len = xMult;
        identifier.totalWidth = totalWidth;

        if (!tempRectangles.ContainsKey(identifier)) {
            tempRectangles.Add(identifier, new List<TempRectangle>());
        }
        tempRectangles[identifier].Add(rect);
    }

    CityElements.Types.Runtime.Buildings.BlockType GetBlockType(ObjectState block) {
        var t = block.Str("type");
        var types = CityElements.Types.Parsers.TypeParser.GetBuildingBlockTypes();
        return types[t];
    }

    bool IsBlockAlwaysDrawn(ObjectState block) {
        var t = block.Str("type");
        if (t == "texture" || t == "empty") {
            return false;
        } else {
            var bt = GetBlockType(block);
            return bt.typeData.settings.alwaysDraw;
        }
    }

    void CreateMatrix(ObjectState block, int blockHash, Vector3 pos, Vector3 scale, Quaternion rotation, Facade facade) { //TODO: currently useless
        var matrix = Matrix4x4.TRS(pos, rotation, scale);
        if (!facade.instances.ContainsKey(blockHash)) {
            facade.instances.Add(blockHash, new List<List<Matrix4x4>>() { new List<Matrix4x4>() });
            facade.hasAlwaysDraws |= IsBlockAlwaysDrawn(block);
        }
        var matrices = facade.instances[blockHash];
        var last = matrices[matrices.Count - 1];
        if (last.Count == 1023) {
            last = new List<Matrix4x4>();
            matrices.Add(last);
        }
        last.Add(matrix);
    }

    ObjectState GetBlockFromDict(int index) {
        var blockDict = state.Array<ObjectState>("blockDict");
        if (index >= blockDict.Length) index = blockDict.Length - 1;
        return blockDict[index];
    }

    void CreateSlice(int blockHash, float width, float curLength, float yPos, float floorHeight,
        Vector3 floorpos, Vector3 buildingPos, Quaternion buildingRot, Vector3 floorScale, Facade facade, int yIndex, bool flatGround,
        bool bottom = false, bool bottomOnly = false, float yPos2 = 0, int xMult = 1, float fixAdd = 0) {

        var block = GetBlockFromDict(blockHash);
        var relAdd = Vector3.Scale(new Vector3(curLength, yPos + fixAdd, 0) + floorpos, floorScale);
        var trueAdd = buildingRot * relAdd;
        var scale = Vector3.Scale(new Vector3(width, floorHeight, 1), floorScale);
        var frontType = block.Str("type");
        if (frontType == "texture") {
            if (yIndex == -1 && !flatGround) {
                CreateRectMesh(block, blockHash, trueAdd + buildingPos, scale, buildingRot, facade, xMult);
            } else {
                CreateRectMeshTemp(block, blockHash, trueAdd + buildingPos, relAdd, scale, buildingRot, facade, xMult, curLength, width * xMult, yIndex, floorHeight);
            }
        } else if (frontType == "empty") {

        } else {
            CreateParamMesh(block, relAdd, scale, bottom, bottomOnly, yPos, yPos2, xMult);
        }
    }

    (List<int> okBlocks, Vector3 floorScale) GetFloorInfo(ObjectState floor, float sideLength) {
        //determine parts that fit
        var lengthLeft = sideLength;
        var okBlocks = new List<int>();
        var slices = floor.Array<ObjectState>("Slices", false);
		for (int i = 0; i < slices.Length && lengthLeft > 0; i++) {
			var s = slices[i];
			for (int j = 0; j < s.Int("Repetitions") && lengthLeft > 0; j++) {
				okBlocks.Add(i);
				lengthLeft -= s.Float("Width");
			}
			if (lengthLeft > 0 && i == slices.Length - 1) i = -1;
		}
        if (okBlocks.Count > 1) okBlocks.RemoveAt(okBlocks.Count - 1);

        var trueCurLength = 0.0f;
        foreach (int i in okBlocks) {
            var s = slices[i];
            trueCurLength += s.Float("Width");
        }
        var floorScale = new Vector3(sideLength / trueCurLength, 1, 1);
        return (okBlocks, floorScale);
    }

    void CreateFloor(ObjectState floor, ObjectState state, float yPos, float sideLength, Vector3 floorpos, Vector3 buildingPos, Quaternion buildingRot, Facade facade, float heightMult, int yIndex) {
        var info = GetFloorInfo(floor, sideLength);
        //generate the parts
        var curLength = 0.0f;
        var startI = 0;
        var curLength0 = 0.0f;
        var slices = floor.Array<ObjectState>("Slices", false);
        for (int ii = 0; ii < info.okBlocks.Count; ii++) {
            var i = info.okBlocks[ii];
            var s = slices[i];

            var drawBlock = false;
            if (ii < info.okBlocks.Count - 1) {
                var s1 = slices[info.okBlocks[ii + 1]];
                if (s1.Int("block") != s.Int("block") || s1.Float("Width") != s.Float("Width")) drawBlock = true;
            } else {
                drawBlock = true;
            }
            var block = GetBlockFromDict(s.Int("block"));
            if (block.Str("type") != "texture") drawBlock = true;
            if (drawBlock) {
                if (s.Float("Width") <= 0) return;
                var xMult = ii - startI + 1;
                CreateSlice(s.Int("block"), s.Float("Width") * xMult, curLength0, yPos, floor.Float("height") * heightMult, floorpos, buildingPos, buildingRot, info.floorScale, facade, yIndex, false, false, false, 0, xMult);
                startI = ii + 1;
                curLength0 = curLength + s.Float("Width");
            }

            curLength += s.Float("Width");
        }
    }



    (float yBottom, float yMid, float yTop) GetGroundHeights(float sWidth, float groundStart, float groundEnd, float curLength, float sideLength, float realFloorHeight, float floorScaleX) {
        var add = groundEnd < groundStart ? 0 : sWidth;
        var add2 = groundEnd > groundStart ? 0 : sWidth;
        var yBottom = Mathf.LerpUnclamped(groundStart, groundEnd, (curLength + add2) / sideLength * floorScaleX);
        var yMid = Mathf.LerpUnclamped(groundStart, groundEnd, (curLength + add) / sideLength * floorScaleX);
        var yTop = yMid + realFloorHeight;
        return (yBottom, yMid, yTop);
    }

    void CreateGroundFloor(ObjectState floor, ObjectState state, float realFloorHeight, float groundStart, float groundEnd, float realGroundEnd, float sideLength, Vector3 floorpos, Vector3 buildingPos, Quaternion buildingRot, Facade facade) {
        var info = GetFloorInfo(floor, sideLength);
        if (info.okBlocks.Count == 0) return;
        //generate the parts
        var curLength = 0.0f;
        var slices = floor.Array<ObjectState>("Slices", false);
        var bottomOnly = floor.Float("minHeight") == 0;
        var startI = 0;
        var curLength0 = 0.0f;
        var s0 = slices[info.okBlocks[0]];
        var hI_0 = GetGroundHeights(s0.Float("Width"), groundStart, groundEnd, curLength, sideLength, realFloorHeight, info.floorScale.x);
        var lastTop = 0.0f;
        for (int ii = 0; ii < info.okBlocks.Count; ii++) {
            var i = info.okBlocks[ii];
            var s = slices[i];
            if (s.Float("Width") <= 0) return;
            var hI = GetGroundHeights(s.Float("Width"), groundStart, groundEnd, curLength, sideLength, realFloorHeight, info.floorScale.x);
            var hI2 = GetGroundHeights(s.Float("Width"), groundStart, groundEnd, curLength + s.Float("Width"), sideLength, realFloorHeight, info.floorScale.x);
            var hBottom = hI.yMid - hI.yBottom;
            var hTop = realGroundEnd - hI.yTop;
            if (bottomOnly) {
                var fixAdd = 0.0f;
                if (groundEnd >= groundStart) {
                    hTop = realGroundEnd - hI_0.yBottom;
                } else {
                    hTop = realGroundEnd - hI.yBottom;
                    fixAdd = -hTop + lastTop + hI.yTop - hI.yBottom;
                }
                if (hTop > GeometryHelper.epsilon) {
                    var drawBlock = false;
                    if (ii < info.okBlocks.Count - 1) {
                        var s1 = slices[info.okBlocks[ii + 1]];
                        if (s1.Int("top") != s.Int("top") || s1.Float("Width") != s.Float("Width")) drawBlock = true;
                    } else {
                        drawBlock = true;
                    }
                    //next slice is different, draw the identical ones so far in one quad
                    if (drawBlock) {
                        var xMult = ii - startI + 1;
                        CreateSlice(s.Int("top"), s.Float("Width") * xMult, curLength0, hI_0.yBottom, hTop, floorpos, buildingPos, buildingRot, info.floorScale, facade, -1, false, true, true, hI2.yBottom, xMult, fixAdd);
                        startI = ii + 1;
                        curLength0 = curLength + s.Float("Width");
                        hI_0 = hI2;
                        lastTop = hTop;
                    }
                }
            } else {
                //bottom
                if (hBottom > GeometryHelper.epsilon)
                    CreateSlice(s.Int("bottom"), s.Float("Width"), curLength, hI.yBottom, hBottom, floorpos, buildingPos, buildingRot, info.floorScale, facade, -1, false, true, false, hI2.yBottom);
                if (Mathf.Abs(groundStart - groundEnd) < GeometryHelper.epsilon) {
                    //middle and top, merge repetitions
                    var drawBlock = false;
                    if (ii < info.okBlocks.Count - 1) {
                        var s1 = slices[info.okBlocks[ii + 1]];
                        if (s1.Int("middle") != s.Int("middle") || s1.Float("Width") != s.Float("Width")) drawBlock = true;
                    } else {
                        drawBlock = true;
                    }
                    var block = GetBlockFromDict(s.Int("middle"));
                    if (block.Str("type") != "texture") drawBlock = true;
                    if (drawBlock) {
                        var xMult = ii - startI + 1;
                        //middle
                        CreateSlice(s.Int("middle"), s.Float("Width") * xMult, curLength0, hI.yMid, realFloorHeight, floorpos, buildingPos, buildingRot, info.floorScale, facade, -1, true, false, false, 0, xMult);
                        //top
                        if (hTop > GeometryHelper.epsilon)
                            CreateSlice(s.Int("top"), s.Float("Width") * xMult, curLength0, hI.yTop, hTop, floorpos, buildingPos, buildingRot, info.floorScale, facade, -1, true, false, false, 0, xMult);
                        startI = ii + 1;
                        curLength0 = curLength + s.Float("Width");
                    }
                } else {
                    //middle
                    CreateSlice(s.Int("middle"), s.Float("Width"), curLength, hI.yMid, realFloorHeight, floorpos, buildingPos, buildingRot, info.floorScale, facade, -1, false);
                    //top
                    if (hTop > GeometryHelper.epsilon)
                        CreateSlice(s.Int("top"), s.Float("Width"), curLength, hI.yTop, hTop, floorpos, buildingPos, buildingRot, info.floorScale, facade, -1, false);
                }
            }
            curLength += s.Float("Width");
        }
    }

    Bounds CreateBounds(List<Vector3> points) {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var maxZ = float.MinValue;
        foreach (var p in points) {
            if (p.x < minX) minX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.z < minZ) minZ = p.z;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
            if (p.z > maxZ) maxZ = p.z;
        }
        var maxV = new Vector3(maxX, maxY, maxZ);
        var minV = new Vector3(minX, minY, minZ);
        var center = (maxV + minV) * 0.5f;
        var size = maxV - minV;
        return new Bounds(center, size);
    }

    void CreateSegment(ObjectState state, Vector3 p0, Vector3 p1, float maxHeight, float height) {
        //calculate parameters
        var endPosFlat = new Vector3(p1.x, p0.y, p1.z);
        var sideLength = (endPosFlat - p0).magnitude;
        var lowPoint = Mathf.Min(p0.y, p1.y);
        var highPoint = Mathf.Max(p0.y, p1.y);
        var goingUp = lowPoint == p0.y;
        var groundStart = highPoint - lowPoint;
        var groundEnd = maxHeight - lowPoint;
        var realGroundHeight = Mathf.Min(height, state.State("groundFloor").Float("minHeight"));
        var upperHeight = height - realGroundHeight;
        if (!state.Bool("hasGroundFloor")) {
            upperHeight = height + groundEnd;
            groundEnd = 0;
        }

        //calculate parts that fit
        var upperFloors = state.Array<ObjectState>("upperFloors");
        var upperHeightLeft = upperHeight;
        var okFloorsL = new List<int>();
        var okFloorsM = new List<int>();
        var okFloorsH = new List<int>();
        List<int> lastList = null;
        if (upperFloors != null) {
            for (int i = 0; i < state.Int("lowUpperFloorsCount") && upperHeightLeft > 0; i++) {
                if (upperFloors[i].Float("height") <= 0) return;
                okFloorsL.Add(i);
                lastList = okFloorsL;
                upperHeightLeft -= upperFloors[i].Float("height");
            }
            for (int i = 0; i < state.Int("highUpperFloorsCount") && upperHeightLeft > 0; i++) {
                var idx = upperFloors.Length - 1 - i;
                if (upperFloors[idx].Float("height") <= 0) return;
                okFloorsH.Add(idx);
                lastList = okFloorsH;
                upperHeightLeft -= upperFloors[idx].Float("height");
            }
            for (int i = state.Int("lowUpperFloorsCount"); (i < upperFloors.Length - state.Int("highUpperFloorsCount")) && upperHeightLeft > 0; i++) {
                if (upperFloors[i].Float("height") <= 0) return;
                okFloorsM.Add(i);
                lastList = okFloorsM;
                upperHeightLeft -= upperFloors[i].Float("height");
                if (upperHeightLeft > 0 && i == upperFloors.Length - state.Int("highUpperFloorsCount") - 1) i = state.Int("lowUpperFloorsCount") - 1;
            }
        }
        if (lastList != null && lastList.Count > 0) lastList.RemoveAt(lastList.Count - 1);

        //generate the parts
        var curHeight = 0.0f;
        var floorPos = new Vector3(0, groundEnd, 0);
        var buildingPos = new Vector3(p0.x, lowPoint, p0.z);
        var angle = Vector3.SignedAngle(Vector3.right, endPosFlat - p0, Vector3.up);
        var buildingRot = Quaternion.Euler(0, angle, 0);

        var points = new List<Vector3>();
        points.Add(p0);
        points.Add(p1);
        points.Add(new Vector3(p0.x, maxHeight + height, p0.z));
        points.Add(new Vector3(p1.x, maxHeight + height, p1.z));
        var facade = new Facade();
        var bounds = CreateBounds(points);
        facade.bounds = bounds;
        facade.forward = -Vector3.Cross(endPosFlat - p0, Vector3.up).normalized;
        facade.instances = new Dictionary<int, List<List<Matrix4x4>>>();
        facades.Add(facade);
        tempParamMeshes.Clear();

        if (state.Bool("hasGroundFloor")) {
            CreateGroundFloor(state.State("groundFloor"), state, realGroundHeight, goingUp ? 0 : groundStart, goingUp ? groundStart : 0, groundEnd, sideLength, Vector3.zero, buildingPos, buildingRot, facade);
        }

        var trueCurHeight = 0.0f;
        foreach (int i in okFloorsL) {
            var f = upperFloors[i];
            trueCurHeight += f.Float("height");
        }
        foreach (int i in okFloorsM) {
            var f = upperFloors[i];
            trueCurHeight += f.Float("height");
        }
        foreach (int i in okFloorsH) {
            var f = upperFloors[i];
            trueCurHeight += f.Float("height");
        }
        var heightFactor = upperHeight / trueCurHeight;
        var floorIndex = 0;
        foreach (int i in okFloorsL) {
            var f = upperFloors[i];
            CreateFloor(f, state, curHeight * heightFactor, sideLength, floorPos, buildingPos, buildingRot, facade, heightFactor, floorIndex);
            curHeight += f.Float("height");
            floorIndex++;
        }
        foreach (int i in okFloorsM) {
            var f = upperFloors[i];
            CreateFloor(f, state, curHeight * heightFactor, sideLength, floorPos, buildingPos, buildingRot, facade, heightFactor, floorIndex);
            curHeight += f.Float("height");
            floorIndex++;
        }
        foreach (int i in okFloorsH) {
            var f = upperFloors[i];
            CreateFloor(f, state, curHeight * heightFactor, sideLength, floorPos, buildingPos, buildingRot, facade, heightFactor, floorIndex);
            curHeight += f.Float("height");
            floorIndex++;
        }
        meshParts.Add(GeometryHelpers.SubMesh.MergeSubmeshes(tempParamMeshes, buildingPos, Vector3.one, angle));
        foreach (var paramMesh in tempParamMeshes) {
            var newParamMesh = GeometryHelpers.SubMesh.MergeSubmeshes(new List<SubMeshData>() { paramMesh }, buildingPos, Vector3.one, angle);
            finalParamMeshes.Add(newParamMesh);
        }
    }

    void GenerateCollider(List<Vector3> spline, float height) {
        var verts = new List<Vector3>();
        verts.AddRange(spline);
        foreach (var point in spline) {
            var point2 = new Vector3(point.x, height, point.z);
            verts.Add(point2);
        }
        var indices = new List<int>();
        for (int i = 0; i < spline.Count - 1; i++) {
            indices.Add(i);
            indices.Add(spline.Count + i);
            indices.Add(i + 1);

            indices.Add(spline.Count + i);
            indices.Add(spline.Count + i + 1);
            indices.Add(i + 1);
        }
        colliderMesh.vertices = verts.ToArray();
        colliderMesh.triangles = indices.ToArray();
        colliderMesh.RecalculateBounds();
        colliderMesh.RecalculateNormals();
        mc.enabled = true;
    }

    public void Clear() {
        if (deleted) return;
        colliderMesh.Clear();
        mc.enabled = false;
        meshDict.Clear();
        facades.Clear();
        alwaysDraws.Clear();
        finalParamMeshes.Clear();
        ClearTemp();
    }

    public void ClearTemp() {
        meshParts.Clear();
        tempRectangles.Clear();
        tempParamMeshes.Clear();
    }

    public void SyncWithBuilding() {
        SetState(state);
    }

    public void LateUpdate() {
        if (deleted) return;
        if (lr.enabled) {
            var cam = Camera.main;
            var minDist = Vector3.Distance(colliderMesh.bounds.center, cam.transform.position) - colliderMesh.bounds.extents.magnitude;
            minDist = Mathf.Clamp(minDist / 20, 1.0f, 1000);
            lr.startWidth = 0.1f * minDist;
            lr.endWidth = 0.1f * minDist;
        }
    }

    void AddSortedRect(TempRectangle elem, int yMult) {
        var blockHash = elem.hash;
        var block = elem.block;
        if (!meshDict.ContainsKey(blockHash)) {
            var res = GetRectBlock(block, elem.len, yMult, 1, 1);
            meshDict.Add(blockHash, res);
            if (!alwaysDraws.ContainsKey(blockHash)) alwaysDraws.Add(blockHash, false);
        }
        CreateMatrix(block, blockHash, elem.absPos, elem.scale, elem.rot, elem.facade);
    }

    public void SetState(ObjectState newState) {
        if (deleted) return;
        state = newState;
        if (!(building.state.Bool("allSidesEqual") && side != Side.Front)) {
            switch (side) {
                case Side.Left:
                    building.state.SetState("leftState", newState);
                    break;
                case Side.Right:
                    building.state.SetState("rightState", newState);
                    break;
                case Side.Front:
                    building.state.SetState("frontState", newState);
                    break;
                case Side.Back:
                    building.state.SetState("backState", newState);
                    break;
            }
        }
        building.changed = true;
    }

    void GroupTempRects() {
        foreach (var entry in tempRectangles) {
            var group = entry.Value;
            var startI = 0;
            for (int i = 0; i < group.Count; i++) {
                var groupI = group[i];
                bool draw = false;
                if (i < group.Count - 1) {
                    var groupI2 = group[i + 1];
                    if (groupI2.y != groupI.y + 1 || groupI2.height != groupI.height) draw = true;
                } else {
                    draw = true;
                }
                if (draw) {
                    var elem = group[startI];
                    var count = i - startI + 1;
                    elem.hash = GetRectHash(elem.blockIndex, count, elem.len);
                    elem.scale = Vector3.Scale(elem.scale, new Vector3(1, count, 1));
                    AddSortedRect(elem, count);
                    startI = i + 1;
                }
            }
        }
    }

    public SubMeshData GetSubmesh() {
        return m;
    }

    bool DidChange(List<Vector3> spline, float height, float trueHeight, float maxHeight) {
        if (height != old_height) return true;
        if (trueHeight != old_trueHeight) return true;
        if (maxHeight != old_maxHeight) return true;
        if (spline != null && old_spline == null || spline == null && old_spline != null) return true;
        if (spline.Count != old_spline.Count) return true;
        for (int i = 0; i < spline.Count; i++) {
            if (!GeometryHelper.AreVectorsEqual(spline[i], old_spline[i])) return true;
        }
        if (state.HasChanged()) return true;
        return false;
    }

    void UpdateOlds(List<Vector3> spline, float height, float trueHeight, float maxHeight) {
        old_height = height;
        old_trueHeight = trueHeight;
        old_maxHeight = maxHeight;
        old_spline = new List<Vector3>(spline);
        state.FlagAsUnchanged();
    }

    public int UpdateMesh(List<Vector3> spline, float height, float trueHeight, float maxHeight, ObjectState state) {
        if (deleted) return 0;
        SetState(state);
        ReloadType();
        if (!DidChange(spline, height, trueHeight, maxHeight)) return 0;
        Clear();
        var trueMaxHeight = maxHeight;
        if (spline.Count < 2) return 0;
        var upperFloors = state.Array<ObjectState>("upperFloors");
        if (upperFloors == null || state.Int("highUpperFloorsCount") + state.Int("lowUpperFloorsCount") > upperFloors.Length) {
            if (!state.Bool("hasGroundFloor")) return 0;
        }
        GenerateOutline(spline, height);
        GenerateCollider(spline, height);
        if (state.Bool("hasGroundFloor")) trueMaxHeight += state.State("groundFloor").Float("minHeight");

        //create the mesh parts
        for (int i = 0; i < spline.Count - 1; i++) {
            CreateSegment(state, spline[i], spline[i + 1], trueMaxHeight, trueHeight);
        }

        //determine how textured rectangles have to be grouped
        GroupTempRects();

        //get the facade parts
        foreach (var facade in facades) {
            foreach (var entry in facade.instances) {
                var meshEntry = meshDict[entry.Key];
                foreach (var batch in entry.Value) {
                    for (int i = 0; i < meshEntry.materials.Length; i++) {
                        if (meshEntry.materials[i] != null) {
                            for (int j = 0; j < batch.Count; j++) {
                                var a = (SubMeshData)meshEntry.Clone();
                                for (int vi = 0; vi < a.vertices.Length; vi++) {
                                    a.vertices[vi] = batch[j].MultiplyPoint(a.vertices[vi]);
                                }
                                meshParts.Add(a);
                            }
                        }
                    }
                }
            }
        }

        var paramMeshesMerged = GeometryHelpers.SubMesh.MergeSubmeshes(meshParts, Vector3.zero, Vector3.one, 0);
        m = paramMeshesMerged;
        UpdateOlds(spline, height, trueHeight, maxHeight);
        ClearTemp();
        return 1;
    }
}
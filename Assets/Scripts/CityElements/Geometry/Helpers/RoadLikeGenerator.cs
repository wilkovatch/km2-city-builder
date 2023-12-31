using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GH = GeometryHelper;
using RC = RuntimeCalculator;
using CityElements.Types.Runtime.RoadLikeType;

public class RoadLikeGenerator<T> {
    public List<Vector3> tempVertices;
    public List<List<int>> tempIndices;
    public List<Vector2> tempUVs;
    public List<float> sectionMarkers;
    public List<Vector3> curvePoints = new List<Vector3>();
    public List<Vector3> curveDirections = new List<Vector3>();
    public List<Vector3> curveRightVectors = new List<Vector3>();
    public List<float> groundHeights = new List<float>();
    public List<Vector3> sectionRights = new List<Vector3>();
    public List<Vector3[]> sectionVertices = new List<Vector3[]>();
    public RC.VariableContainer variableContainer;

    int n;
    ObjectState state, instanceState;
    int segments;
    RoadLikeType<T> curType = null;
    int vertsPerSection;

    public RoadLikeGenerator(ObjectState state, ObjectState instanceState, int segments, int numMeshes) {
        tempVertices = new List<Vector3>();
        tempIndices = new List<List<int>>();
        for (int i = 0; i < numMeshes; i++) {
            tempIndices.Add(new List<int>());
        }
        tempUVs = new List<Vector2>();
        sectionMarkers = new List<float>();
        this.state = (ObjectState)state.Clone();
        this.instanceState = instanceState != null ? (ObjectState)instanceState.Clone() : null;
        this.segments = segments;
        n = 0;
    }

    public void Reset(RoadLikeType<T> curType, ObjectState state, ObjectState instanceState, int segments, int numMeshes) {
        if (this.curType != curType) {
            this.curType = curType;
            if (curType != null) variableContainer = curType.variableContainer.GetClone();
        }
        tempVertices.Clear();
        for (int i = 0; i < tempIndices.Count; i++) {
            tempIndices[i].Clear();
        }
        tempIndices.Clear();
        for (int i = 0; i < numMeshes; i++) {
            tempIndices.Add(new List<int>());
        }
        tempUVs.Clear();
        sectionMarkers.Clear();
        this.state = (ObjectState)state.Clone();
        this.instanceState = instanceState != null ? (ObjectState)instanceState.Clone() : null;
        this.segments = segments;
        n = 0;
        curvePoints.Clear();
        curveDirections.Clear();
        curveRightVectors.Clear();
        sectionRights.Clear();
        sectionVertices.Clear();
        groundHeights.Clear();
    }

    public void Clear() {
        tempVertices.Clear();
        tempIndices.Clear();
        tempUVs.Clear();
        sectionMarkers.Clear();
        curvePoints.Clear();
        curveDirections.Clear();
        curveRightVectors.Clear();
        sectionRights.Clear();
        sectionVertices.Clear();
        groundHeights.Clear();
    }

    public void InitBaseSectionsInfo() {
        if (sectionVertices.Count == 0) curType.FillInitialSegmentVariables(variableContainer, Vector3.zero, Vector3.zero, new Vector3[0], new Vector3[0]);
        else curType.FillInitialSegmentVariables(variableContainer, sectionRights[0], sectionRights[sectionRights.Count - 1], sectionVertices[0], sectionVertices[sectionVertices.Count - 1]);
    }

    public void InitSection(int i) {
        if (i == 1) n = tempVertices.Count;
        var g = groundHeights != null && groundHeights.Count > i ? groundHeights[i] : 0.0f;
        curType.FillSegmentVariables(variableContainer, state, instanceState, curvePoints[i],
            sectionRights[i], sectionMarkers[i], g, i + 1, sectionVertices[i], curvePoints, segments);
    }

    public void AddSection(int i, List<List<Vector3>> sidePoints, int curAnchorLine = 0) {
        //anchor lines
        if (sidePoints != null) {
            var anchors = curType.anchorsCalculators.GetValues(variableContainer);
            foreach (var a in anchors) {
                sidePoints[curAnchorLine++].Add(a);
            }
        }

        var components = curType.GetComponents();

        for (int c = 0; c < components.Length; c++) {
            var comp = components[c];
            var compMesh = curType.componentMeshes[c];
            var vars = variableContainer;
            if (curType.subConditions[c] == null || curType.subConditions[c].GetValue(variableContainer)) {
                curType.FillComponentVariables(variableContainer, c);

                //anchor lines
                if (sidePoints != null && compMesh.anchorsCalculators != null) {
                    var subAnchors = compMesh.anchorsCalculators.GetValues(vars);
                    foreach (var a in subAnchors) {
                        sidePoints[curAnchorLine++].Add(a);
                    }
                }

                //the mesh
                if (comp.mainMesh != null) {
                    AddMesh(i, compMesh.name, true, compMesh.mainMesh, vars);
                }
            }
        }
        if (i == segments - 1) {
            //end cap
            for (int c = 0; c < components.Length; c++) {
                var comp = components[c];
                var compMesh = curType.componentMeshes[c];
                var vars = variableContainer;
                if (comp.endMesh != null && (curType.subConditions[c] == null || curType.subConditions[c].GetValue(variableContainer))) {
                    curType.FillComponentVariables(variableContainer, c);
                    AddMesh(i, compMesh.name, false, compMesh.endMesh, vars);
                }
            }

            //start cap
            curType.FillSegmentVariables(variableContainer, state, instanceState, curvePoints[0], sectionRights[0], sectionMarkers[0], 0, 1, sectionVertices[0], curvePoints, segments);
            for (int c = 0; c < components.Length; c++) {
                var comp = components[c];
                var compMesh = curType.componentMeshes[c];
                var vars = variableContainer;
                if (comp.startMesh != null && (curType.subConditions[c] == null || curType.subConditions[c].GetValue(variableContainer))) {
                    curType.FillComponentVariables(variableContainer, c);
                    AddMesh(i, compMesh.name, false, compMesh.startMesh, vars);
                }
            }
        }
        if (i == 0) vertsPerSection = tempVertices.Count;
    }

    void AddMesh(int i, string compName, bool mainMesh, MeshCalculator mesh, RC.VariableContainer vars) {
        int curV;
        if (mainMesh) {
            curV = tempVertices.Count - n;
        } else {
            curV = tempVertices.Count;
        }

        //vertices
        var calculatedVertices = mesh.verticesCalculators.GetValues(vars);
        tempVertices.AddRange(calculatedVertices);

        //uvs
        var calculatedUvs = mesh.uvsCalculators.GetValues(vars);
        tempUVs.AddRange(calculatedUvs);

        //indices
        var texturesMapping = curType.GetTexturesMapping();
        if (mainMesh) {
            if (i > 0) {
                var calculatedIndices = mesh.faces.GetValues(vars);
                var calculatedTextures = mesh.facesTextures.GetValues(vars);
                for (int j = 0; j < calculatedTextures.Length; j++) {
                    var realCalculatedTexture = texturesMapping[compName][(int)calculatedTextures[j]];
                    tempIndices[realCalculatedTexture].AddRange(GH.GetSectionIndices(curV + (int)calculatedIndices[j], n));
                }
            }
        } else {
            var calculatedIndices = mesh.faces.GetValues(vars);
            var calculatedTextures = mesh.facesTextures.GetValues(vars);
            for (int j = 0; j < calculatedTextures.Length; j++) {
                var realCalculatedTexture = texturesMapping[compName][(int)calculatedTextures[j]];
                tempIndices[realCalculatedTexture].Add(curV + (int)calculatedIndices[j * 3]);
                tempIndices[realCalculatedTexture].Add(curV + (int)calculatedIndices[j * 3 + 1]);
                tempIndices[realCalculatedTexture].Add(curV + (int)calculatedIndices[j * 3 + 2]);
            }
        }
    }

    public (Vector3[], Vector3) GetRawSectionVertices(int i, Vector3? forcedRight = null) {
        var values = curType.segmentVerticesCalculators.GetValues(variableContainer);
        var res = new Vector3[values.Length];
        var pos = curvePoints[i];
        var right = forcedRight ?? curveRightVectors[i];
        right *= curveRightVectors[i].magnitude;
        for (int j = 0; j < res.Length; j++) {
            res[j] = pos + right * values[j];
        }
        return (res, right);
    }

    public int GetVerticesPerSection() {
        return vertsPerSection;
    }
}

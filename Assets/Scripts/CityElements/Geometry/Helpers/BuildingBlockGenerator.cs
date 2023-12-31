using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GH = GeometryHelper;
using RC = RuntimeCalculator;
using CityElements.Types.Runtime.Buildings;

public class BuildingBlockGenerator {
    public List<Vector3> tempVertices;
    public List<List<int>> tempIndices;
    public List<Vector2> tempUVs;
    public List<float> sectionMarkers;
    public RC.VariableContainer variableContainer;

    ObjectState state;
    BlockType curType = null;

    public BuildingBlockGenerator(BlockType curType, ObjectState state) {
        var numMeshes = curType.typeData.settings.textures.Length;
        tempVertices = new List<Vector3>();
        tempIndices = new List<List<int>>();
        for (int i = 0; i < numMeshes; i++) {
            tempIndices.Add(new List<int>());
        }
        tempUVs = new List<Vector2>();
        sectionMarkers = new List<float>();
        this.state = (ObjectState)state.Clone();
    }

    public void Reset(BlockType curType, ObjectState state) {
        var numMeshes = curType.typeData.settings.textures.Length;
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
    }

    public void Clear() {
        tempVertices.Clear();
        tempIndices.Clear();
        tempUVs.Clear();
        sectionMarkers.Clear();
    }

    public void CalculateMesh(float y0, float y1, float uMult, float vMult, float xMult, Vector3 scale) {
        curType.FillVariables(variableContainer, state, null, new Vector3(0, 1, 0));
        curType.FillBlockVariables(variableContainer, y0, y1, uMult, vMult, xMult, scale);
        var components = curType.GetComponents();
        for (int c = 0; c < components.Length; c++) {
            var comp = components[c];
            var compMesh = curType.componentMeshes[c];
            var vars = variableContainer;
            if (curType.subConditions[c] == null || curType.subConditions[c].GetValue(variableContainer)) {
                curType.FillComponentVariables(variableContainer, c);
                if (comp.mesh != null) {
                    AddMesh(compMesh.name, true, compMesh.mesh, vars);
                }
            }
        }
    }

    void AddMesh(string compName, bool mainMesh, CityElements.Types.Runtime.RoadLikeType.MeshCalculator mesh, RC.VariableContainer vars) {
        //vertices
        var calculatedVertices = mesh.verticesCalculators.GetValues(vars);
        tempVertices.AddRange(calculatedVertices);

        //uvs
        var calculatedUvs = mesh.uvsCalculators.GetValues(vars);
        tempUVs.AddRange(calculatedUvs);

        //indices
        var texturesMapping = curType.GetTexturesMapping();
        if (mainMesh) {
            var calculatedIndices = mesh.faces.GetValues(vars);
            var calculatedTextures = mesh.facesTextures.GetValues(vars);
            for (int j = 0; j < calculatedTextures.Length; j++) {
                var realCalculatedTexture = texturesMapping[compName][(int)calculatedTextures[j]];
                tempIndices[realCalculatedTexture].Add((int)calculatedIndices[j * 3]);
                tempIndices[realCalculatedTexture].Add((int)calculatedIndices[j * 3 + 1]);
                tempIndices[realCalculatedTexture].Add((int)calculatedIndices[j * 3 + 2]);
            }
        }
    }

    public string[] GetMaterialSet() {
        var list = new List<string>();
        if (curType != null) {
            foreach (var tex in curType.typeData.settings.textures) {
                var mat = state.Str(tex);
                list.Add(mat);
            }
        }
        return list.ToArray();
    }
}

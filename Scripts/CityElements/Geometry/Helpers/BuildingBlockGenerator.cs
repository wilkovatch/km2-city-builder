using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
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
        //block parameters and static definitions first, then the values given by the side (which take precedence
        //over block definitions with the same name), then the dynamic definitions, which can use all of them
        curType.FillInitialVariables(variableContainer, state, null);
        curType.FillBlockVariables(variableContainer, y0, y1, uMult, vMult, xMult, scale);
        curType.FillVariables(variableContainer, state, null, GameEngine.Vector3.right);
        curType.EvaluateComponents(variableContainer, tempVertices, tempUVs, tempIndices);
    }

    public string[] GetMaterialSet() {
        var list = new List<string>();
        if (curType != null) {
            foreach (var tex in curType.typeData.settings.textures) {
                var mat = MaterialManager.GetTextureName(state, tex);
                list.Add(mat);
            }
        }
        return list.ToArray();
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;
using SubMeshData = GeometryHelpers.SubMesh.SubMeshData;

public class TerrainSubmeshGenerator {
    SubMeshData m;
    public MeshCollider mc;

    public TerrainSubmeshGenerator(BuildingLine line) {
        Initialize(line);
    }

    public void Initialize(BuildingLine line) {
        m = new SubMeshData();
        mc = line.gameObject.AddComponent<MeshCollider>();
    }

    public void Delete() {
        Object.Destroy(mc);
    }

    public void Clear() {
        mc.sharedMesh.Clear();
        mc.enabled = false;
    }

    public SubMeshData GetSubmesh() {
        return m;
    }

    public int UpdateMesh(List<Vector3> spline, string texture, float uMult, float vMult) {
        m = GeometryHelpers.TerrainGeneration.GetSubmesh(spline, null, null, texture, false, 0, uMult, vMult);
        mc.sharedMesh = GeometryHelpers.SubMesh.GetMesh(m).Item1;
        mc.enabled = false;
        mc.enabled = true;
        return 1;
    }
}

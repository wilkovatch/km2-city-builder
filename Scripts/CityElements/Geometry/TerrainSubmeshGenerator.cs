using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using Mesh = Godot.ArrayMesh;
using MeshCollider = Godot.CollisionShape3D;
using MeshRenderer = Godot.MeshInstance3D;
using GO = GameEngine.GameObject;
using States;
using SubMeshData = GeometryHelpers.SubMesh.SubMeshData;

public class TerrainSubmeshGenerator {
    SubMeshData m;
    public MeshCollider mc;
    Mesh mesh = null;

    public TerrainSubmeshGenerator(BuildingLine line) {
        Initialize(line);
    }

    public void Initialize(BuildingLine line) {
        m = new SubMeshData();
        mc = GameEngine.Mesh.CreateCollider(line, mesh);
    }

    public void Delete() {
        GameEngine.Mesh.Destroy(mesh);
        GameEngine.Mesh.DestroyCollider(mc);
    }

    public void Clear() {
        GameEngine.Mesh.Clear(mesh);
        GameEngine.Mesh.RefreshCollider(mesh, mc, false);
    }

    public SubMeshData GetSubmesh() {
        return m;
    }

    public int UpdateMesh(List<Vector3> spline, string texture, float uMult, float vMult) {
        m = GeometryHelpers.TerrainGeneration.GetSubmesh(spline, null, null, texture, false, 0, uMult, vMult);
        GameEngine.Mesh.Clear(mesh);
        mesh = GeometryHelpers.SubMesh.GetMesh(m).Item1;
        GameEngine.Mesh.RefreshCollider(mesh, mc, true);
        return 1;
    }
}

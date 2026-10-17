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

public partial class TerrainPatchGenerator : PhysicsScriptable {
    public MeshRenderer mr;
    public Mesh m;
    MeshCollider mc;

    GameObject lrContainer;
    public LineRenderer lr;

    public void Initialize() {
        GO.SetLayer(this, 7);
        (mr, m) = GameEngine.Mesh.CreateMesh(GO.GetObject(this), new Material[] { MaterialManager.GetMaterial("") });
        mc = GameEngine.Mesh.CreateCollider(GO.GetObject(this), m);

        lr = GO.CreateLineRenderer(this);
        lr.startWidth = 0.5f;
        lr.endWidth = 0.5f;
        lr.material = MaterialManager.GetHandleMaterial((GameEngine.Color.red + GameEngine.Color.yellow) * 0.5f);
    }

    public void RebuildMesh(List<Vector3> perimeterPoints, List<Vector3> internalPoints, List<(List<Vector3> segment,
        List<Vector3> lefts, ObjectState state, ObjectState instanceState)> borderMeshes, string tex, int smooth, float uMult, float vMult) {

        Clear();

        try {
            var terrain = GeometryHelpers.TerrainGeneration.GetMesh(perimeterPoints, internalPoints, borderMeshes, tex, smooth, uMult, vMult);
            if (terrain.res == 0) return;
            lr.positionCount = terrain.linePoints.Count;
            lr.SetPositions(terrain.linePoints.ToArray());
            var indices = terrain.GetIndices();
            var materials = terrain.materials.ToArray();

            GameEngine.Mesh.AssignMaterials(mr, materials);
            GameEngine.Mesh.AssignData(m, materials, indices, terrain.vertices, terrain.uvs, GO.GetName(this));
            GameEngine.Mesh.RefreshCollider(m, mc, true);
        } catch (System.Exception e) {
            GameEngine.LogWarning("Error during mesh generation on patch " + GO.GetName(GO.GetObject(this)) + ": " + e.StackTrace.ToString());
            GameEngine.Mesh.RefreshCollider(m, mc, false);
        }
    }

    public void Clear() {
        lr.positionCount = 0;
        lr.SetPositions(new Vector3[] { });
        GameEngine.Mesh.Clear(m);
    }
}
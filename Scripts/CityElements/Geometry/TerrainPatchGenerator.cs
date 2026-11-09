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

    //a mesh added to the surface by the geometry script of the type (see TerrainPatch.ScriptMeshes)
    public class ExtraMesh {
        public List<Vector3> vertices;
        public List<Vector2> uvs; //as many as the vertices
        public List<int> triangles;
        public Material material;
    }

    //extraMeshes: the meshes to add to the surface, made from it (its vertices, uvs and the triangles of its submeshes), null for none
    public void RebuildMesh(List<Vector3> perimeterPoints, List<Vector3> internalPoints, string tex, int smooth, float uMult, float vMult,
        System.Func<List<Vector3>, List<Vector2>, List<int[]>, List<ExtraMesh>> extraMeshes = null) {

        Clear();

        try {
            var terrain = GeometryHelpers.TerrainGeneration.GetMesh(perimeterPoints, internalPoints, tex, smooth, uMult, vMult);
            if (terrain.res == 0) return;
            lr.positionCount = terrain.linePoints.Count;
            lr.SetPositions(terrain.linePoints.ToArray());
            var indices = terrain.GetIndices();
            var materials = new List<Material>(terrain.materials);
            var vertices = terrain.vertices;
            var uvs = terrain.uvs;
            var extra = extraMeshes?.Invoke(terrain.vertices, terrain.uvs, indices);
            if (extra != null && extra.Count > 0) {
                //after the submeshes of the surface, in their order (so an exporter can tell them apart)
                vertices = new List<Vector3>(terrain.vertices);
                uvs = new List<Vector2>(terrain.uvs);
                foreach (var mesh in extra) {
                    var offset = vertices.Count;
                    vertices.AddRange(mesh.vertices);
                    uvs.AddRange(mesh.uvs);
                    indices.Add(mesh.triangles.ConvertAll(i => i + offset).ToArray());
                    materials.Add(mesh.material);
                }
            }

            GameEngine.Mesh.AssignMaterials(mr, materials.ToArray());
            GameEngine.Mesh.AssignData(m, materials.ToArray(), indices, vertices, uvs, GO.GetName(this));
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
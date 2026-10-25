using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using Material = Godot.Material;
using Matrix4x4 = Godot.Transform3D;
using Mesh = Godot.ArrayMesh;
using MeshRenderer = Godot.MeshInstance3D;
using MeshCollider = Godot.CollisionShape3D;
using GO = GameEngine.GameObject;
using States;
using System.IO;
using System;
using IO.ExportedCity;

namespace GeometryHelpers {
    public class SubMesh {
        //Instance components
        public struct SubMeshData: ICloneable {
            public Vector3[] vertices;
            public Vector2[] uvs;
            public int[][] indices;
            public string[] materials;
            public Vector3 pos;
            public Vector3 scale;

            public object Clone() { //necessary because of the arrays
                var res = new SubMeshData();
                res.pos = pos;
                res.scale = scale;
                res.vertices = Utils.CloneArray(vertices);
                res.uvs = Utils.CloneArray(uvs);
                res.materials = Utils.CloneArray(materials);
                res.indices = Utils.CloneArray(indices);
                return res;
            }
        }

        public static SubMeshData MergeSubmeshes(List<SubMeshData> meshes, Vector3 pos, Vector3 scale, float angle) {
            Matrix4x4? matrix = null;
            if (pos != GameEngine.Vector3.zero || scale != GameEngine.Vector3.one || angle != 0.0f)
                matrix = GameEngine.Matrix4x4.TRS(pos, GameEngine.Quaternion.Euler(0, angle, 0), scale);
            var res = new SubMeshData();
            int vertCount = 0;
            var cleanMeshes = new List<SubMeshData>();
            foreach (var m in meshes) {
                if (m.materials != null && m.vertices != null && m.uvs != null && m.indices != null) {
                    cleanMeshes.Add(m);
                }
            }
            meshes = cleanMeshes;
            foreach (var m in meshes) vertCount += m.vertices.Length;
            res.vertices = new Vector3[vertCount];
            res.uvs = new Vector2[vertCount];
            var matSet = new Dictionary<string, (int index, int count)>();
            foreach (var m in meshes) {
                for (int mi = 0; mi < m.materials.Length; mi++) {
                    var mat = m.materials[mi];
                    if (!matSet.ContainsKey(mat)) {
                        matSet.Add(mat, (matSet.Count, m.indices[mi].Length));
                    } else {
                        matSet[mat] = (matSet[mat].index, matSet[mat].count + m.indices[mi].Length);
                    }
                }
            }
            res.indices = new int[matSet.Count][];
            res.materials = new string[matSet.Count];
            foreach (var mat in matSet) {
                res.indices[mat.Value.index] = new int[mat.Value.count];
            }
            int curVert = 0;
            int[] curIVerts = new int[matSet.Count];
            foreach (var m in meshes) {
                for (int vi = 0; vi < m.vertices.Length; vi++) {
                    var oV = (m.pos != GameEngine.Vector3.zero || m.scale != GameEngine.Vector3.one) ? (GameEngine.Vector3.Scale(m.vertices[vi], m.scale) + m.pos) : m.vertices[vi];
                    var finalV = matrix.HasValue ? GameEngine.Matrix4x4.MultiplyPoint(matrix.Value, oV) : oV;
                    res.vertices[curVert + vi] = finalV;
                }
                m.uvs.CopyTo(res.uvs, curVert);
                for (int mi = 0; mi < m.materials.Length; mi++) {
                    var mat = m.materials[mi];
                    var i = matSet[mat].index;
                    res.materials[i] = mat;
                    foreach (var vi in m.indices[mi]) {
                        res.indices[i][curIVerts[i]++] = curVert + vi;
                    }
                }
                curVert += m.vertices.Length;
            }
            res.scale = GameEngine.Vector3.one;
            res.pos = GameEngine.Vector3.zero;
            return res;
        }

        public static (Mesh, Material[]) GetMesh(SubMeshData submesh) {
            var mesh = new Mesh();
            var (materials, indices) = GetMaterials(submesh, mesh);
            AssignDataToMesh(submesh, mesh, materials, indices);
            return (mesh, materials);
        }

        public static void SetMeshData(SubMeshData submesh, Mesh mesh, MeshRenderer renderer) {
            var (materials, indices) = GetMaterials(submesh, mesh);
            GameEngine.Mesh.AssignMaterials(renderer, materials);
            AssignDataToMesh(submesh, mesh, materials, indices);
        }

        static void AssignDataToMesh(SubMeshData submesh, Mesh mesh, Material[] materials, List<int[]> indices) {
            //an element that generated no geometry (e.g. a building side with invalid floors) has null arrays
            var vertices = submesh.vertices != null ? new List<Vector3>(submesh.vertices) : new List<Vector3>();
            var uvs = submesh.uvs != null ? new List<Vector2>(submesh.uvs) : new List<Vector2>();
            GameEngine.Mesh.AssignData(mesh, materials, indices, vertices, uvs, null);
        }

        static (Material[] materials, List<int[]> indices) GetMaterials(SubMeshData submesh, Mesh mesh) {
            var materials = new List<Material>();
            var indices = new List<int[]>();
            if (submesh.materials != null) {
                for (int i = 0; i < submesh.materials.Length; i++) {
                    materials.Add(MaterialManager.GetMaterial(submesh.materials[i]));
                    indices.Add(submesh.indices[i]);
                }
            }
            return (materials.ToArray(), indices);
        }
    }
}
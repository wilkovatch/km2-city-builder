/*
 * Copyright (c) 2019 Dummiesman
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
*/

using System.Collections.Generic;
using Godot;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;

// Godot port of the (customized) Dummiesman's OBJ Importer
// (see res://CustomPackages/OBJImport/LICENSE.txt).
namespace Dummiesman {
    public class OBJObjectBuilder {
        //
        public int PushedFaceCount { get; private set; } = 0;

        //stuff passed in by ctor
        private OBJLoader _loader;
        private string _name;

        private Dictionary<ObjLoopHash, int> _globalIndexRemap = new Dictionary<ObjLoopHash, int>();
        private Dictionary<string, List<int>> _materialIndices = new Dictionary<string, List<int>>();
        private List<int> _currentIndexList;
        private string _lastMaterial = null;

        //our local vert/normal/uv
        private List<Vector3> _vertices = new List<Vector3>();
        private List<Vector3> _normals = new List<Vector3>();
        private List<Vector2> _uvs = new List<Vector2>();

        //this will be set if the model has no normals or missing normal info
        private bool recalculateNormals = false;

        /// <summary>
        /// Loop hasher helper class
        /// </summary>
        private class ObjLoopHash {
            public int vertexIndex;
            public int normalIndex;
            public int uvIndex;

            public override bool Equals(object obj) {
                if (!(obj is ObjLoopHash))
                    return false;

                var hash = obj as ObjLoopHash;
                return (hash.vertexIndex == vertexIndex) && (hash.uvIndex == uvIndex) && (hash.normalIndex == normalIndex);
            }

            public override int GetHashCode() {
                int hc = 3;
                hc = unchecked(hc * 314159 + vertexIndex);
                hc = unchecked(hc * 314159 + normalIndex);
                hc = unchecked(hc * 314159 + uvIndex);
                return hc;
            }
        }

        public GameObject Build() {
            var go = GO.Create(_name);

            //locate the material for each submesh
            //(a plain Material array: the mesh keeps it and may later store materials of other types in it)
            Material[] materialArray = new Material[_materialIndices.Count];
            int submesh = 0;
            foreach (var kvp in _materialIndices) {
                ShaderMaterial material = null;
                if (_loader.Materials == null) {
                    material = OBJLoaderHelper.CreateNullMaterial();
                    GameEngine.Materials.SetName(material, kvp.Key);
                } else {
                    if (!_loader.Materials.TryGetValue(kvp.Key, out material)) {
                        material = OBJLoaderHelper.CreateNullMaterial();
                        GameEngine.Materials.SetName(material, kvp.Key);
                        _loader.Materials[kvp.Key] = material;
                    }
                }
                materialArray[submesh] = material;
                submesh++;
            }

            //set faces
            var indices = new List<int[]>();
            foreach (var kvp in _materialIndices) {
                indices.Add(kvp.Value.ToArray());
            }

            //recalculations
            var normals = recalculateNormals ? GameEngine.Mesh.CalculateNormals(_vertices, indices) : _normals;

            //add the mesh, the vertex data is converted to Godot's coordinates there
            var (mr, msh) = GameEngine.Mesh.CreateMesh(go, materialArray);
            GameEngine.Mesh.AssignData(msh, materialArray, indices, _vertices.ToArray(), normals.ToArray(), new Vector2[][] { _uvs.ToArray() }, null);

            //
            return go;
        }

        public void SetMaterial(string name) {
            if (!_materialIndices.TryGetValue(name, out _currentIndexList)) {
                _currentIndexList = new List<int>();
                _materialIndices[name] = _currentIndexList;
            }
        }

        public void PushFace(string material, List<int> vertexIndices, List<int> normalIndices, List<int> uvIndices) {
            //invalid face size?
            if (vertexIndices.Count < 3) {
                return;
            }

            //set material
            if (material != _lastMaterial) {
                SetMaterial(material);
                _lastMaterial = material;
            }

            //remap
            int[] indexRemap = new int[vertexIndices.Count];
            for (int i = 0; i < vertexIndices.Count; i++) {
                int vertexIndex = vertexIndices[i];
                int normalIndex = normalIndices[i];
                int uvIndex = uvIndices[i];

                var hashObj = new ObjLoopHash() {
                    vertexIndex = vertexIndex,
                    normalIndex = normalIndex,
                    uvIndex = uvIndex
                };
                int remap = -1;

                if (!_globalIndexRemap.TryGetValue(hashObj, out remap)) {
                    //add to dict
                    _globalIndexRemap.Add(hashObj, _vertices.Count);
                    remap = _vertices.Count;

                    //add new verts and what not
                    _vertices.Add((vertexIndex >= 0 && vertexIndex < _loader.Vertices.Count) ? _loader.Vertices[vertexIndex] : Vector3.Zero);
                    _normals.Add((normalIndex >= 0 && normalIndex < _loader.Normals.Count) ? _loader.Normals[normalIndex] : Vector3.Zero);
                    _uvs.Add((uvIndex >= 0 && uvIndex < _loader.UVs.Count) ? _loader.UVs[uvIndex] : Vector2.Zero);

                    //mark recalc flag
                    if (normalIndex < 0)
                        recalculateNormals = true;
                }

                indexRemap[i] = remap;
            }

            //add face to our mesh list
            if (indexRemap.Length == 3) {
                _currentIndexList.AddRange(new int[] { indexRemap[0], indexRemap[1], indexRemap[2] });
            } else if (indexRemap.Length == 4) {
                _currentIndexList.AddRange(new int[] { indexRemap[0], indexRemap[1], indexRemap[2] });
                _currentIndexList.AddRange(new int[] { indexRemap[2], indexRemap[3], indexRemap[0] });
            } else if (indexRemap.Length > 4) {
                for (int i = indexRemap.Length - 1; i >= 2; i--) {
                    _currentIndexList.AddRange(new int[] { indexRemap[0], indexRemap[i - 1], indexRemap[i] });
                }
            }

            PushedFaceCount++;
        }

        public OBJObjectBuilder(string name, OBJLoader loader) {
            _name = name;
            _loader = loader;
        }
    }
}

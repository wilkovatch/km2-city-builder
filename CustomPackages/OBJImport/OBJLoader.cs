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

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using GameObject = Godot.Node3D;
using Material = Godot.ShaderMaterial;
using GO = GameEngine.GameObject;

// Godot port of the (customized) Dummiesman's OBJ Importer
// (see res://CustomPackages/OBJImport/LICENSE.txt).
// The editor menu item of the original is not ported.
namespace Dummiesman {
    public enum SplitMode {
        None,
        Object,
        Material
    }

    public class OBJLoader {
        //options
        /// <summary>
        /// Determines how objects will be created
        /// </summary>
        public SplitMode SplitMode = SplitMode.Object;

        //global lists, accessed by objobjectbuilder
        internal List<Vector3> Vertices = new List<Vector3>();
        internal List<Vector3> Normals = new List<Vector3>();
        internal List<Vector2> UVs = new List<Vector2>();

        //materials, accessed by objobjectbuilder
        internal Dictionary<string, Material> Materials;

        //file info for files loaded from file path, used for GameObject naming and MTL finding
        private FileInfo _objInfo;

        /// <summary>
        /// Helper function to load mtllib statements
        /// </summary>
        /// <param name="mtlLibPath"></param>
        private void LoadMaterialLibrary(string mtlLibPath) {
            if (_objInfo != null) {
                var relativePath = PathHelper.ResolveFile(Path.Combine(_objInfo.Directory.FullName, mtlLibPath));
                if (File.Exists(relativePath)) {
                    Materials = new MTLLoader().Load(relativePath);
                    return;
                }
            }

            mtlLibPath = PathHelper.ResolveFile(mtlLibPath);
            if (File.Exists(mtlLibPath)) {
                Materials = new MTLLoader().Load(mtlLibPath);
                return;
            }
        }

        // OBJ indices start from 1, negative ones are relative to the end of the list read so far (-1 is the last element)
        static int PostprocessIndex(int index, int count) {
            if (index > int.MinValue) {
                if (index < 0)
                    index = count + index + 1;
                index--;
            }
            return index;
        }

        /// <summary>
        /// Load an OBJ file from a stream. No materials will be loaded, and will instead be supplemented by a blank white material.
        /// </summary>
        /// <param name="input">Input OBJ stream</param>
        /// <returns>Returns a GameObject represeting the OBJ file, with each imported object as a child.</returns>
        public GameObject Load(Stream input) {
            var inputReader = new StreamReader(input);
            var reader = new StringReader(inputReader.ReadToEnd());

            Dictionary<string, OBJObjectBuilder> builderDict = new Dictionary<string, OBJObjectBuilder>();
            OBJObjectBuilder currentBuilder = null;
            string currentMaterial = "default";

            //lists for face data
            //prevents excess GC
            List<int> vertexIndices = new List<int>();
            List<int> normalIndices = new List<int>();
            List<int> uvIndices = new List<int>();

            //helper func
            Action<string> setCurrentObjectFunc = (string objectName) => {
                if (!builderDict.TryGetValue(objectName, out currentBuilder)) {
                    currentBuilder = new OBJObjectBuilder(objectName, this);
                    builderDict[objectName] = currentBuilder;
                }
            };

            //create default object
            setCurrentObjectFunc.Invoke("default");

            //do the reading
            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine()) {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string processedLine = line.Clean();
                string[] splitLine = processedLine.Split(' ');

                //comment or blank
                if (processedLine[0] == '#' || splitLine.Length < 2) {
                    continue;
                }

                //load material library
                if (splitLine[0] == "mtllib" && Materials == null) {
                    string mtlLibPath = processedLine.Substring(7);
                    LoadMaterialLibrary(mtlLibPath);
                }

                //vtx
                if (splitLine[0] == "v") {
                    Vertices.Add(OBJLoaderHelper.VectorFromStrArray(splitLine));
                    continue;
                }

                //normal
                if (splitLine[0] == "vn") {
                    Normals.Add(OBJLoaderHelper.VectorFromStrArray(splitLine));
                    continue;
                }

                //uv
                if (splitLine[0] == "vt") {
                    UVs.Add(OBJLoaderHelper.UVFromStrArray(splitLine));
                    continue;
                }

                //new material
                if (splitLine[0] == "usemtl") {
                    string materialName = processedLine.Substring(7);
                    currentMaterial = materialName;

                    if (SplitMode == SplitMode.Material) {
                        setCurrentObjectFunc.Invoke(materialName);
                    }
                    continue;
                }

                //new object
                if ((splitLine[0] == "o" || splitLine[0] == "g") && SplitMode == SplitMode.Object) {
                    string objectName = processedLine.Substring(2);
                    setCurrentObjectFunc.Invoke(objectName);
                    continue;
                }

                //face data (the fun part)
                if (splitLine[0] == "f") {
                    //loop through indices
                    for (int i = 1; i < splitLine.Length; i++) {
                        string faceLoop = splitLine[i];

                        int vertexIndex = int.MinValue;
                        int normalIndex = int.MinValue;
                        int uvIndex = int.MinValue;

                        //parse face loop
                        if (faceLoop.Contains("//")) {
                            //vertex and normal
                            string[] slashSplits = faceLoop.Split('/');
                            vertexIndex = OBJLoaderHelper.FastIntParse(slashSplits[0]);
                            normalIndex = OBJLoaderHelper.FastIntParse(slashSplits[2]);
                        } else if (faceLoop.Contains('/')) {
                            //get slash splits
                            string[] slashSplits = faceLoop.Split('/');
                            if (slashSplits.Length > 2) {
                                //vertex, uv, and normal
                                vertexIndex = OBJLoaderHelper.FastIntParse(slashSplits[0]);
                                uvIndex = OBJLoaderHelper.FastIntParse(slashSplits[1]);
                                normalIndex = OBJLoaderHelper.FastIntParse(slashSplits[2]);
                            } else {
                                //vertex, and uv
                                vertexIndex = OBJLoaderHelper.FastIntParse(slashSplits[0]);
                                uvIndex = OBJLoaderHelper.FastIntParse(slashSplits[1]);
                            }
                        } else {
                            //just vertex index
                            vertexIndex = OBJLoaderHelper.FastIntParse(faceLoop);
                        }

                        //"postprocess" indices
                        vertexIndex = PostprocessIndex(vertexIndex, Vertices.Count);
                        normalIndex = PostprocessIndex(normalIndex, Normals.Count);
                        uvIndex = PostprocessIndex(uvIndex, UVs.Count);

                        //set array values
                        vertexIndices.Add(vertexIndex);
                        normalIndices.Add(normalIndex);
                        uvIndices.Add(uvIndex);
                    }

                    //push to builder
                    vertexIndices.Reverse(); //CHANGED: FLIP X AXIS
                    normalIndices.Reverse(); //CHANGED: FLIP X AXIS
                    uvIndices.Reverse(); //CHANGED: FLIP X AXIS
                    currentBuilder.PushFace(currentMaterial, vertexIndices, normalIndices, uvIndices);

                    //clear lists
                    vertexIndices.Clear();
                    normalIndices.Clear();
                    uvIndices.Clear();
                }
            }

            //finally, put it all together
            GameObject obj = GO.Create(_objInfo != null ? Path.GetFileNameWithoutExtension(_objInfo.Name) : "WavefrontObject");

            foreach (var builder in builderDict) {
                //empty object
                if (builder.Value.PushedFaceCount == 0)
                    continue;

                var builtObj = builder.Value.Build();
                GO.SetParent(builtObj, obj);
            }

            return obj;
        }

        /// <summary>
        /// Load an OBJ and MTL file from a stream.
        /// </summary>
        /// <param name="input">Input OBJ stream</param>
        /// /// <param name="mtlInput">Input MTL stream</param>
        /// <returns>Returns a GameObject represeting the OBJ file, with each imported object as a child.</returns>
        public GameObject Load(Stream input, Stream mtlInput) {
            var mtlLoader = new MTLLoader();
            Materials = mtlLoader.Load(mtlInput);

            return Load(input);
        }

        /// <summary>
        /// Load an OBJ and MTL file from a file path.
        /// </summary>
        /// <param name="path">Input OBJ path</param>
        /// /// <param name="mtlPath">Input MTL path</param>
        /// <returns>Returns a GameObject represeting the OBJ file, with each imported object as a child.</returns>
        public GameObject Load(string path, string mtlPath) {
            _objInfo = new FileInfo(path);
            if (!string.IsNullOrEmpty(mtlPath) && File.Exists(mtlPath)) {
                var mtlLoader = new MTLLoader();
                Materials = mtlLoader.Load(mtlPath);
            }

            using (var fs = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read)) {
                return Load(fs);
            }
        }

        /// <summary>
        /// Load an OBJ file from a file path. This function will also attempt to load the MTL defined in the OBJ file.
        /// </summary>
        /// <param name="path">Input OBJ path</param>
        /// <returns>Returns a GameObject represeting the OBJ file, with each imported object as a child.</returns>
        public GameObject Load(string path) {
            return Load(path, null);
        }
    }
}

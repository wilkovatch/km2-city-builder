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
using System.IO;
using Godot;
using Material = Godot.ShaderMaterial;

// Godot port of the (customized) Dummiesman's OBJ Importer
// (see res://CustomPackages/OBJImport/LICENSE.txt).
// Parameters the Godot Standard shaders don't have yet (specular color, normal and emission maps) are still set,
// like the materials sent by the Python cores (see MeshImporter).
namespace Dummiesman {
    public class MTLLoader {
        // Relative search paths are relative to the folder of the mtl file
        public List<string> SearchPaths = new List<string>() { "%FileName%_Textures", string.Empty };

        private FileInfo _objFileInfo = null;

        /// <summary>
        /// The texture loading function. Overridable for stream loading purposes.
        /// </summary>
        /// <param name="path">The path supplied by the OBJ file, converted to OS path seperation</param>
        /// <param name="isNormalMap">Whether the loader is requesting we convert this into a normal map</param>
        /// <returns>Texture2D if found, or NULL if missing</returns>
        public virtual Texture2D TextureLoadFunction(string path, bool isNormalMap) {
            //find it
            foreach (var searchPath in SearchPaths) {
                //replace varaibles and combine path
                string processedPath = (_objFileInfo != null) ? searchPath.Replace("%FileName%", Path.GetFileNameWithoutExtension(_objFileInfo.Name))
                                                              : searchPath;
                if (_objFileInfo != null) processedPath = Path.Combine(_objFileInfo.DirectoryName, processedPath);
                string filePath = PathHelper.ResolveFile(Path.Combine(processedPath, path));

                //return if eists
                if (File.Exists(filePath)) {
                    //Godot's normal maps use the texture as is
                    return MaterialManager.GetTexture(filePath, true); //CHANGED
                }
            }

            //not found
            return null;
        }

        private Texture2D TryLoadTexture(string texturePath, bool normalMap = false) {
            //swap directory seperator char
            texturePath = texturePath.Replace('\\', Path.DirectorySeparatorChar);
            texturePath = texturePath.Replace('/', Path.DirectorySeparatorChar);

            return TextureLoadFunction(texturePath, normalMap);
        }

        /// <summary>
        /// Loads a *.mtl file
        /// </summary>
        /// <param name="input">The input stream from the MTL file</param>
        /// <returns>Dictionary containing loaded materials</returns>
        public Dictionary<string, Material> Load(Stream input) {
            var inputReader = new StreamReader(input);
            var reader = new StringReader(inputReader.ReadToEnd());

            Dictionary<string, Material> mtlDict = new Dictionary<string, Material>();
            Material currentMaterial = null;

            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine()) {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string processedLine = line.Clean();
                string[] splitLine = processedLine.Split(' ');

                //blank or comment
                if (splitLine.Length < 2 || processedLine[0] == '#')
                    continue;

                //newmtl
                if (splitLine[0] == "newmtl") {
                    string materialName = processedLine.Substring(7);

                    var newMtl = OBJLoaderHelper.CreateNullMaterial(); //ADDED: instancing
                    GameEngine.Materials.SetName(newMtl, materialName);
                    mtlDict[materialName] = newMtl;
                    currentMaterial = newMtl;

                    continue;
                }

                //anything past here requires a material instance
                if (currentMaterial == null)
                    continue;

                //diffuse color
                if (splitLine[0] == "Kd" || splitLine[0] == "kd") {
                    GameEngine.Materials.SetShaderParameter(currentMaterial, "_Color", OBJLoaderHelper.ColorFromStrArray(splitLine));
                    continue;
                }

                //diffuse map
                if (splitLine[0] == "map_Kd" || splitLine[0] == "map_kd") {
                    string texturePath = processedLine.Substring(7);

                    var KdTexture = TryLoadTexture(texturePath);
                    GameEngine.Materials.SetShaderParameter(currentMaterial, "_MainTex", KdTexture);

                    //set transparent mode if the texture has transparency
                    if (KdTexture != null && GameEngine.Texture.HasAlphaChannel(KdTexture)) {
                        OBJLoaderHelper.EnableMaterialTransparency(currentMaterial);
                    }

                    //flip texture if this is a dds
                    if (Path.GetExtension(texturePath).ToLower() == ".dds") {
                        GameEngine.Materials.SetTextureScale(currentMaterial, "_MainTex", new Vector2(1f, -1f));
                    }

                    continue;
                }

                //bump map
                if (splitLine[0] == "map_Bump" || splitLine[0] == "map_bump") {
                    string texturePath = processedLine.Substring(9);
                    var bumpTexture = TryLoadTexture(texturePath, true);

                    if (bumpTexture != null) {
                        GameEngine.Materials.SetShaderParameter(currentMaterial, "_BumpMap", bumpTexture);
                        GameEngine.Materials.EnableKeyword(currentMaterial, "_NORMALMAP");
                    }

                    continue;
                }

                //specular color
                if (splitLine[0] == "Ks" || splitLine[0] == "ks") {
                    GameEngine.Materials.SetShaderParameter(currentMaterial, "_SpecColor", OBJLoaderHelper.ColorFromStrArray(splitLine));
                    continue;
                }

                //emission color
                if (splitLine[0] == "Ka" || splitLine[0] == "ka") {
                    GameEngine.Materials.SetShaderParameter(currentMaterial, "_EmissionColor", OBJLoaderHelper.ColorFromStrArray(splitLine, 0.05f));
                    GameEngine.Materials.EnableKeyword(currentMaterial, "_EMISSION");
                    continue;
                }

                //emission map
                if (splitLine[0] == "map_Ka" || splitLine[0] == "map_ka") {
                    string texturePath = processedLine.Substring(7);
                    GameEngine.Materials.SetShaderParameter(currentMaterial, "_EmissionMap", TryLoadTexture(texturePath));
                    continue;
                }

                //alpha
                if (splitLine[0] == "d") {
                    float visibility = OBJLoaderHelper.FastFloatParse(splitLine[1]);
                    if (visibility < 1f)  {
                        Color temp = GameEngine.Materials.GetColor(currentMaterial);

                        temp.A = visibility;
                        GameEngine.Materials.SetShaderParameter(currentMaterial, "_Color", temp);

                        OBJLoaderHelper.EnableMaterialTransparency(currentMaterial);
                    }
                    continue;
                }

                //glossiness
                if (splitLine[0] == "Ns" || splitLine[0] == "ns") {
                    float Ns = OBJLoaderHelper.FastFloatParse(splitLine[1]);
                    Ns = (Ns / 1000f);
                    GameEngine.Materials.SetShaderParameter(currentMaterial, "_Glossiness", Ns);
                }
            }

            //return our dict
            return mtlDict;
        }

        /// <summary>
        /// Loads a *.mtl file
        /// </summary>
        /// <param name="path">The path to the MTL file</param>
        /// <returns>Dictionary containing loaded materials</returns>
        public Dictionary<string, Material> Load(string path) {
            _objFileInfo = new FileInfo(path); //get file info

            using (var fs = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read)) {
                return Load(fs); //actually load
            }
        }
    }
}

using System.Collections.Generic;
using System.IO;
using Godot;
using GameObject = Godot.Node3D;
using Color32 = GameEngine.Color32;
using GO = GameEngine.GameObject;

public static class MeshImporter {
    static readonly List<string> baseSupportedFormats = new List<string>() { "obj" };
    static List<string> fullSupportedFormats, customFormats;

    class MeshResponseReader {
        byte[] bytes;
        int position = 0;

        public MeshResponseReader(byte[] bytes) {
            this.bytes = bytes;
        }

        uint ReadUint32() {
            var res = System.BitConverter.ToUInt32(bytes, position);
            position += 4;
            return res;
        }

        uint ReadUint16() {
            var res = System.BitConverter.ToUInt16(bytes, position);
            position += 2;
            return res;
        }

        float ReadFloat() {
            var res = System.BitConverter.ToSingle(bytes, position);
            position += 4;
            return res;
        }

        byte ReadByte() {
            return bytes[position++];
        }

        string ReadString() {
            var len = ReadUint16();
            var res = new char[len];
            for (int i = 0; i < len; i++) {
                res[i] = (char)ReadByte();
            }
            return new string(res);
        }

        Vector3 ReadVector3() {
            var x = ReadFloat();
            var y = ReadFloat();
            var z = ReadFloat();
            return new Vector3(x, y, z);
        }

        Vector2 ReadVector2() {
            var x = ReadFloat();
            var y = ReadFloat();
            return new Vector2(x, y);
        }

        Color32 ReadColor() {
            var r = ReadByte();
            var g = ReadByte();
            var b = ReadByte();
            var a = ReadByte();
            return new Color32(r, g, b, a);
        }

        Quaternion ReadQuaternion() {
            var x = ReadFloat();
            var y = ReadFloat();
            var z = ReadFloat();
            var w = ReadFloat();
            return new Quaternion(x, y, z, w);
        }

        (string key, string keyword, Color color) ReadMaterialColor() {
            var key = ReadString();
            var keyword = ReadString();
            var color = ReadColor();
            return (key, keyword, color);
        }

        (string key, string keyword, float value) ReadMaterialFloat() {
            var key = ReadString();
            var keyword = ReadString();
            var value = ReadFloat();
            return (key, keyword, value);
        }

        Texture2D ReadTexture() {
            var folder = ReadString();
            var filename = ReadString();
            if (filename != null && filename != "") {
                var texNameWithoutExt = Path.Combine(folder, filename);
                var texName = TextureImporter.FindTexture(texNameWithoutExt, true);
                var tex = MaterialManager.GetTexture(texName, true);
                return tex;
            } else {
                return null;
            }
        }

        (string key, string keyword, Texture2D texture) ReadMaterialTexture() {
            var key = ReadString();
            var keyword = ReadString();
            var texture = ReadTexture();
            return (key, keyword, texture);
        }

        Material ReadMaterial() {
            var name = ReadString();
            var blendMode = ReadUint32();
            var applyBlendModeOnlyIfTexIsTransparent = ReadByte() > 0;
            var shaderName = ReadString();
            var res = GameEngine.Materials.CreateMaterial(GameEngine.Materials.GetShader(shaderName));
            GameEngine.Materials.SetName(res, name);

            //colors
            var numColors = ReadUint32();
            for (int i = 0; i < numColors; i++) {
                var color = ReadMaterialColor();
                if (color.keyword != "") GameEngine.Materials.EnableKeyword(res, color.keyword);
                GameEngine.Materials.SetShaderParameter(res, color.key, color.color);
            }

            //floats
            var numFloats = ReadUint32();
            for (int i = 0; i < numFloats; i++) {
                var value = ReadMaterialFloat();
                if (value.keyword != "") GameEngine.Materials.EnableKeyword(res, value.keyword);
                GameEngine.Materials.SetShaderParameter(res, value.key, value.value);
            }

            var numTextures = ReadUint32();
            for (int i = 0; i < numTextures; i++) {
                var texture = ReadMaterialTexture();
                if (texture.keyword != "") GameEngine.Materials.EnableKeyword(res, texture.keyword);
                GameEngine.Materials.SetShaderParameter(res, texture.key, texture.texture);
                if (texture.texture != null) {
                    var transparent = GameEngine.Texture.HasAlphaChannel(texture.texture);
                    var apply = !applyBlendModeOnlyIfTexIsTransparent || transparent;
                    if (apply) MaterialManager.SetBlendMode(res, (int)blendMode);
                }
            }

            return res;
        }

        Material[] ReadMaterials() {
            var len = ReadUint32();
            var res = new Material[len];
            for (int i = 0; i < len; i++) {
                res[i] = ReadMaterial();
            }
            return res;
        }

        (int matId, int[] indices) ReadSubmesh() {
            var matId = (int)ReadUint32();
            var len = ReadUint32();
            var indices = new int[len];
            for (int i = 0; i < len; i++) {
                indices[i] = (int)ReadUint32();
            }
            return (matId, indices);
        }

        (string[] lodNames, GameObject mesh) ReadMesh(Material[] materials) {
            //Read the data
            var name = ReadString();
            var numLodNames = ReadUint32();
            var lodNames = new string[numLodNames];
            for (int i = 0; i < numLodNames; i++) {
                lodNames[i] = ReadString();
            }
            var numVertices = ReadUint32();
            var hasColors = ReadByte() > 0;
            var vertices = new Vector3[numVertices];
            var normals = new Vector3[numVertices];
            for (int i = 0; i < numVertices; i++) {
                vertices[i] = ReadVector3();
            }
            for (int i = 0; i < numVertices; i++) {
                normals[i] = ReadVector3();
            }
            Color32[] colors = null;
            if (hasColors) {
                colors = new Color32[numVertices];
                for (int i = 0; i < numVertices; i++) {
                    colors[i] = ReadColor();
                }
            }
            var uvChannels = ReadUint32();
            var uvs = new Vector2[uvChannels][];
            for (int j = 0; j < uvChannels; j++) {
                uvs[j] = new Vector2[numVertices];
                for (int i = 0; i < numVertices; i++) {
                    uvs[j][i] = ReadVector2();
                }
            }
            var submeshCount = ReadUint32();
            var submeshes = new (int matId, int[] indices)[submeshCount];
            var indices = new List<int[]>();
            for (int i = 0; i < submeshCount; i++) {
                submeshes[i] = ReadSubmesh();
                indices.Add(submeshes[i].indices);
            }

            //Create the mesh
            var subMaterials = new Material[submeshCount];
            for (int i = 0; i < submeshCount; i++) {
                subMaterials[i] = materials[submeshes[i].matId];
            }
            var res = GO.Create();
            var (mr, mesh) = GameEngine.Mesh.CreateMesh(res, subMaterials);
            GO.SetName(res, name);

            GameEngine.Mesh.AssignData(mesh, subMaterials, indices, vertices, normals, uvs, colors);

            return (lodNames, res);
        }

        GameObject ReadObject(Material[] materials) {
            //Create the object
            var res = GO.CreateLODMesh(ReadString());

            //Meshes and LODs
            var numAllLods = ReadUint32();
            var transitionValues = new Dictionary<string, float>();
            var lodNames = new string[numAllLods];
            for (int i = 0; i < numAllLods; i++) {
                lodNames[i] = ReadString();
            }
            for (int i = 0; i < numAllLods; i++) {
                transitionValues[lodNames[i]] = ReadFloat();
            }
            var numLods = ReadUint32();
            var lodMeshes = new List<(string[] lodNames, GameObject mesh)>();
            for (int i = 0; i < numLods; i++) {
                var mesh = ReadMesh(materials);
                GO.SetParent(mesh.mesh, res);
                lodMeshes.Add(mesh);
            }
            GO.ConfigureLODMesh(res, (int)numAllLods, (int)numLods, lodNames, lodMeshes, transitionValues);

            //Read the transform
            var localPosition = ReadVector3();
            var localScale = ReadVector3();
            var localRotation = ReadQuaternion();

            //Children
            var numChildren = ReadUint32();
            for (int i = 0; i < numChildren; i++) {
                var child = ReadObject(materials);
                GO.SetParent(child, res);
            }

            //Apply the transform (if done before the children would be transformed incorrectly)
            GO.SetLocalPosition(res, localPosition);
            GO.SetLocalScale(res, localScale);
            GO.SetLocalRotation(res, localRotation);

            return res;
        }

        public GameObject DecodeMeshResponse() {
            var materials = ReadMaterials();
            return ReadObject(materials);
        }
    }

    public static void ReloadMeshFormats() {
        fullSupportedFormats = new List<string>(baseSupportedFormats);
        customFormats = CoreManager.GetList("customMeshFormats", new List<string>());
        fullSupportedFormats.AddRange(customFormats);
    }

    public static List<string> GetSupportedFormats() {
        if (fullSupportedFormats == null) ReloadMeshFormats();
        return fullSupportedFormats;
    }

    public static GameObject LoadMesh(string name, GameObject parent, bool absolute = false) {
        GameObject res = null;
        name = absolute ? PathHelper.ResolveFile(name) : PathHelper.FindInFolders(name);
        try {
            if (name.EndsWith("@handle")) {
                res = GetFallbackObject(true);
            } else if (File.Exists(name)) {
                var ext = PathHelper.GetExtension(name);
                if (customFormats == null) ReloadMeshFormats();
                if (customFormats.Contains(ext)) {
                    var bytes = PythonManager.SendRequest("mesh|" + name);
                    var reader = new MeshResponseReader(bytes);
                    res = reader.DecodeMeshResponse();
                } else {
                    switch (ext) {
                        case "obj":
                            res = new Dummiesman.OBJLoader().Load(name);
                            break;
                    }
                }
            } else {
                GameEngine.Print("Error while loading mesh " + name + ", the file is missing");
                res = GetFallbackObject();
            }
        } catch (System.Exception e) {
            GameEngine.Print("Error while loading mesh " + name + ": " + e.Message + "\nStack: " + e.StackTrace);
            res = GetFallbackObject();
        }
        if (res != null && parent != null) {
            GameEngine.Mesh.SetShadowsTwoSidedRecursive(res);
            GO.SetParent(res, parent);
        }
        return res;
    }

    //the transform of a mesh from its placement point given by the core (server/mesh_transforms.py), null if it gives none,
    //and a warning to show (empty if none)
    public static (MeshManager.MeshTransform? transform, string warning) LoadTransform(string name) {
        if (!PythonManager.CoreHasServerModule("mesh_transforms")) return (null, "");
        name = PathHelper.FindInFolders(name);
        if (name.EndsWith("@handle") || !File.Exists(name)) return (null, "");
        var bytes = PythonManager.SendRequest("meshTransform|" + name);
        //uint8 1 if there is a transform, position (3 floats), rotation (4), scale (3), warning (uint16 length + ASCII)
        if (bytes == null || bytes.Length < 43) return (null, "");
        float F(int i) => System.BitConverter.ToSingle(bytes, 1 + i * 4);
        var warningLength = System.BitConverter.ToUInt16(bytes, 41);
        var warning = System.Text.Encoding.ASCII.GetString(bytes, 43, System.Math.Min(warningLength, bytes.Length - 43));
        if (bytes[0] == 0) return (null, warning);
        var res = new MeshManager.MeshTransform {
            position = new Vector3(F(0), F(1), F(2)),
            rotation = new Quaternion(F(3), F(4), F(5), F(6)).Normalized(),
            scale = new Vector3(F(7), F(8), F(9))
        };
        return (res, warning);
    }

    static GameObject GetFallbackObject(bool handle = false) {
        Material mat;
        if (handle) {
            mat = MaterialManager.GetMaterial("_HANDLE_", true);
        } else {
            mat = MaterialManager.GetMaterial("_TRANSPARENT_", true);
        }
        var c = GeometryHelper.GetCube(10);
        var res = GO.Create();
        var (mr, m) = GameEngine.Mesh.CreateMesh(res, new Material[] { mat });
        GameEngine.Mesh.AssignData(m, c.indices, c.vertices, null);
        return res;
    }

    static GameObject ReadMesh(MeshDecoder.MeshDecoder decoder, string filename, int buffer = 4096) {
        var r = new BufferedBinaryReader(File.Open(filename, FileMode.Open), buffer);
        var res = decoder.DecodeMesh(r, filename);
        r.Dispose();
        return res;
    }
}

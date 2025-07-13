using System.Collections.Generic;
using System.IO;
using UnityEngine;

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
            var res = new Material(Shader.Find(shaderName));
            res.name = name;

            //colors
            var numColors = ReadUint32();
            for (int i = 0; i < numColors; i++) {
                var color = ReadMaterialColor();
                if (color.keyword != "") res.EnableKeyword(color.keyword);
                res.SetColor(color.key, color.color);
            }

            //floats
            var numFloats = ReadUint32();
            for (int i = 0; i < numFloats; i++) {
                var value = ReadMaterialFloat();
                if (value.keyword != "") res.EnableKeyword(value.keyword);
                res.SetFloat(value.key, value.value);
            }

            var numTextures = ReadUint32();
            for (int i = 0; i < numTextures; i++) {
                var texture = ReadMaterialTexture();
                if (texture.keyword != "") res.EnableKeyword(texture.keyword);
                res.SetTexture(texture.key, texture.texture);
                if (texture.texture != null) {
                    var transparent = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(texture.texture.graphicsFormat);
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
            for (int i = 0; i < submeshCount; i++) {
                submeshes[i] = ReadSubmesh();
            }

            //Create the mesh
            var mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.normals = normals;
            if (hasColors) mesh.colors32 = colors;
            for (int i = 0; i < uvChannels; i++) {
                mesh.SetUVs(i, uvs[i]);
            }
            mesh.subMeshCount = (int)submeshCount;
            for (int i = 0; i < submeshCount; i++) {
                mesh.SetIndices(submeshes[i].indices, MeshTopology.Triangles, i);
            }
            mesh.RecalculateBounds(); //TODO: check if necessary
            var res = new GameObject();
            res.name = name;
            var mf = res.AddComponent<MeshFilter>();
            var mr = res.AddComponent<MeshRenderer>();
            var subMaterials = new Material[submeshCount];
            for (int i = 0; i < submeshCount; i++) {
                subMaterials[i] = materials[submeshes[i].matId];
            }
            mr.materials = subMaterials;
            mf.sharedMesh = mesh;

            return (lodNames, res);
        }

        GameObject ReadObject(Material[] materials) {
            //Create the object
            var res = new GameObject();
            res.name = ReadString();

            //Meshes and LODs
            var numAllLods = ReadUint32();
            var lodGroup = res.AddComponent<LODGroup>();
            var renderers = new Dictionary<string, List<Renderer>>();
            var transitionValues = new Dictionary<string, float>();
            var lodNames = new string[numAllLods];
            for (int i = 0; i < numAllLods; i++) {
                lodNames[i] = ReadString();
            }
            for (int i = 0; i < numAllLods; i++) {
                renderers[lodNames[i]] = new List<Renderer>();
                transitionValues[lodNames[i]] = ReadFloat();
            }
            var numLods = ReadUint32();
            for (int i = 0; i < numLods; i++) {
                var mesh = ReadMesh(materials);
                mesh.mesh.transform.parent = res.transform;
                var r = mesh.mesh.GetComponent<MeshRenderer>();
                foreach (var name in mesh.lodNames) {
                    renderers[name].Add(r);
                }
            }
            var lods = new LOD[numAllLods];
            for (int i = 0; i < numAllLods; i++) {
                var lodI = new LOD();
                lodI.screenRelativeTransitionHeight = transitionValues[lodNames[i]];
                lodI.renderers = renderers[lodNames[i]].ToArray();
                lods[i] = lodI;
            }
            lodGroup.SetLODs(lods);

            //Read the transform
            var localPosition = ReadVector3();
            var localScale = ReadVector3();
            var localRotation = ReadQuaternion();

            //Children
            var numChildren = ReadUint32();
            for (int i = 0; i < numChildren; i++) {
                var child = ReadObject(materials);
                child.transform.parent = res.transform;
            }

            //Apply the transform (if done before the children would be transformed incorrectly)
            res.transform.localPosition = localPosition;
            res.transform.localScale = localScale;
            res.transform.localRotation = localRotation;

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
        if (!absolute) name = PathHelper.FindInFolders(name);
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
                MonoBehaviour.print("Error while loading mesh " + name + ", the file is missing");
                res = GetFallbackObject();
            }
        } catch (System.Exception e) {
            MonoBehaviour.print("Error while loading mesh " + name + ": " + e.Message + "\nStack: " + e.StackTrace);
            res = GetFallbackObject();
        }
        if (res != null && parent != null) {
            foreach (var mr in res.GetComponentsInChildren<MeshRenderer>()) {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            }
            res.transform.parent = parent.transform;
        }
        return res;
    }

    static GameObject GetFallbackObject(bool handle = false) {
        var c = GeometryHelper.GetCube(10);
        var m = new Mesh();
        m.vertices = c.vertices.ToArray();
        m.SetIndices(c.indices, MeshTopology.Triangles, 0);
        var  res = new GameObject();
        var mr = res.AddComponent<MeshRenderer>();
        var mf = res.AddComponent<MeshFilter>();
        mf.mesh = m;
        if (handle) {
            mr.material = MaterialManager.GetMaterial("_HANDLE_", true);
        } else {
            mr.material = MaterialManager.GetMaterial("_TRANSPARENT_", true);
        }
        return res;
    }

    static GameObject ReadMesh(MeshDecoder.MeshDecoder decoder, string filename, int buffer = 4096) {
        var r = new BufferedBinaryReader(File.Open(filename, FileMode.Open), buffer);
        var res = decoder.DecodeMesh(r, filename);
        r.Dispose();
        return res;
    }
}
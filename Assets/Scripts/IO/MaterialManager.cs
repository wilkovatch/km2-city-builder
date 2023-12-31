using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class MaterialManager {
    static MaterialManager instance = null;

    Dictionary<string, Material> materials = new Dictionary<string, Material>();
    Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    Dictionary<Color, Material> handleMaterials = new Dictionary<Color, Material>();
    Dictionary<string, string> fullNames = new Dictionary<string, string>();
    Shader standardShader = null;
    Sprite checkerboardSprite;

    [System.Serializable]
    class MaterialDescription {
        public string normal = "";
        public float xTile = 1.0f,
            yTile = 1.0f,
            normalXTile = 1.0f,
            normalYTile = 1.0f,
            normalIntensity = 1.0f,
            smoothness = 0.5f,
            metallic = 0.0f;
    }

    public (string, Material)[] GetAllMaterials() {
        var res = new (string, Material)[materials.Count];
        int i = 0;
        foreach (var elem in materials) {
            res[i] = (elem.Key, elem.Value);
            i++;
        }
        return res;
    }

    public static Material GetHandleMaterial(Color color) {
        return GetInstance().GetHandleMaterial_instance(color);
    }

    public static Material GetMaterial(string name, bool absolute = false) {
        var fullName = name;
        if (!absolute) fullName = GetInstance().FindInFolders(fullName);
        return GetInstance().GetMaterial_instance(fullName, name);
    }

    public static void ClearCache() {
        //TODO: check if other stuff needs to be cleared
        GetInstance().ClearCache_instance();
    }

    private void ClearCache_instance() {
        fullNames.Clear();
    }

    private string FindInFolders(string fullName) {
        if (fullName == null) return fullName;
        if (fullNames.ContainsKey(fullName)) return fullNames[fullName];
        var res = PathHelper.FindInFolders(fullName);
        fullNames.Add(fullName, res);
        return res;
    }

    public static Sprite GetSprite(string name, bool absolute = false) {
        if (!absolute) name = PathHelper.FindInFolders(name);
        return GetInstance().GetSprite_instance(name);
    }

    public static Texture2D GetTexture(string name, bool absolute = false) {
        if (!absolute) name = PathHelper.FindInFolders(name);
        return GetInstance().GetTexture_instance(name);
    }

    public Sprite GetCheckerboard() {
        if (checkerboardSprite == null) {
            var cSize = 64;
            var sq = cSize / 4;
            var sq2 = sq / 2;
            var checkerboardTex = new Texture2D(cSize, cSize, TextureFormat.RGB24, true);
            var colors = new Color[cSize * cSize];
            for (int y = 0; y < cSize; y++) {
                for (int x = 0; x < cSize; x++) {
                    bool white = y % sq < sq2 ^ x % sq < sq2;
                    colors[y * cSize + x] = white ? Color.white : Color.gray;
                }
            }
            checkerboardTex.SetPixels(colors, 0);
            checkerboardTex.Apply();
            checkerboardSprite = Sprite.Create(checkerboardTex, new Rect(0, 0, cSize, cSize), Vector2.zero);
        }
        return checkerboardSprite;
    }

    public static MaterialManager GetInstance() {
        if (instance == null) instance = new MaterialManager();
        return instance;
    }

    public void UnloadAll() {
        foreach (var elem in materials) {
            Object.Destroy(elem.Value);
        }
        materials.Clear();
        foreach (var elem in sprites) {
            Object.Destroy(elem.Value);
        }
        sprites.Clear();
        foreach (var elem in textures) {
            Object.Destroy(elem.Value);
        }
        textures.Clear();
    }

    MaterialManager() {
        standardShader = Shader.Find("Standard");
    }

    Texture2D GetTexture_instance(string name) {
        Texture2D tex = null;
        if (name == null) return tex;
        if (textures.TryGetValue(name, out tex)) {
            return tex;
        } else {
            tex = TextureImporter.LoadTexture(name);
            textures[name] = tex;
            return tex;
        }
    }

    /*string GetBaseNormalName(string name) { //TODO: remake customizable
        var parts = name.Split('.');
        var str = "";
        for (int i = 0; i < parts.Length - 1; i++) {
            str += parts[i] + (i == parts.Length - 2 ? "" : ".");
        }
        str += "_normal." + parts[parts.Length - 1];
        return str;
    }

    MaterialDescription GetNormalData(string name) { //TODO: remake customizable
        var fi = new FileInfo(name); 
        var path = fi.DirectoryName;
        var partName = fi.Name;
        var mat = path + "/" + partName + "_material.json";
        if (File.Exists(mat)) {
            return JsonUtility.FromJson<MaterialDescription>(File.ReadAllText(mat));
        }
        return null;
    }*/

    Material CreateEmptyMaterial(string name, string shortName) {
        var mat = new Material(standardShader);
        mat.enableInstancing = true;
        materials[name] = mat;
        mat.color = Color.magenta;
        mat.name = shortName;
        return mat;
    }

    Material GetMaterial_instance(string name, string shortName) {
        Material mat;
        if (name == null) return CreateEmptyMaterial(name, shortName);
        if (materials.TryGetValue(name, out mat)) {
            return mat;
        } else {
            if (name == "_TRANSPARENT_") {
                mat = new Material(standardShader);
                SetBlendMode(mat, 2);
                mat.color = new Color(1, 1, 1, 0.25f);
                mat.enableInstancing = true;
                materials[name] = mat;
                return mat;
            } else {
                var tex = GetTexture_instance(name);
                if (tex != null) {
                    mat = new Material(standardShader);
                    mat.enableInstancing = true;
                    mat.mainTexture = tex;
                    /*var nData = GetNormalData(name); //TODO: remake customizable
                    if (nData != null) {
                        var fi = new FileInfo(name);
                        var path = fi.DirectoryName;
                        var texN = GetTexture_instance(nData.normal == "" ? GetBaseNormalName(name) : (path + "/" + nData.normal));
                        if (texN != null) {
                            mat.EnableKeyword("_NORMALMAP");
                            if (nData.normalXTile != nData.xTile || nData.normalYTile != nData.yTile) {
                                mat.EnableKeyword("_DETAIL_MULX2");
                                mat.SetTexture("_DetailNormalMap", texN);
                                mat.SetTextureScale("_DetailAlbedoMap", new Vector2(nData.normalXTile, nData.normalYTile));
                                mat.SetFloat("_DetailNormalMapScale", nData.normalIntensity);
                            } else {
                                mat.SetTexture("_BumpMap", texN);
                                mat.SetFloat("_BumpScale", nData.normalIntensity);
                            }
                        }
                        mat.SetFloat("_Glossiness", nData.smoothness);
                        mat.SetFloat("_Metallic", nData.metallic);
                        mat.SetTextureScale("_MainTex", new Vector2(nData.xTile, nData.yTile));
                    } else {
                        var texN = GetTexture_instance(GetBaseNormalName(name));
                        if (texN != null) {
                            mat.EnableKeyword("_NORMALMAP");
                            mat.SetTexture("_BumpMap", texN);
                        }
                    }*/
                    var transparent = GraphicsFormatUtility.HasAlphaChannel(tex.graphicsFormat);
                    if (transparent) SetBlendMode(mat, 1); // Make cutout
                    materials[name] = mat;
                    mat.name = shortName;
                    return mat;
                }
            }
        }
        return CreateEmptyMaterial(name, shortName);
    }

    Sprite GetSprite_instance(string name) {
        if (name == null || name == "_TRANSPARENT_") return null;
        Sprite sprite;
        if (sprites.TryGetValue(name, out sprite)) {
            return sprite;
        } else {
            var tex = GetTexture_instance(name);
            if (tex != null) {
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
                sprites[name] = sprite;
                return sprite;
            }
        }
        return null;
    }

    Material GetHandleMaterial_instance(Color color) {
        if (handleMaterials.ContainsKey(color)) {
            return handleMaterials[color];
        } else {
            var mat = new Material(Resources.Load<Material>("Materials/Line"));
            mat.color = color;
            handleMaterials.Add(color, mat);
            return mat;
        }
    }

    public static void SetBlendMode(Material mat, int blendMode) {
        switch (blendMode) {
            case 0: //opaque
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = -1;
                break;
            case 1: //cutout
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 2450;
                break;
            case 2: //fade
                mat.SetOverrideTag("RenderType", "Fade");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
                break;
            case 3: //transparent
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
                break;
        }
    }
}

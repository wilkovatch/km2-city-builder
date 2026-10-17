using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using Material = Godot.ShaderMaterial;
using GO = GameEngine.GameObject;

public class MaterialManager {
    static MaterialManager instance = null;

    Dictionary<string, Material> materials = new Dictionary<string, Material>();
    //Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(); TODO: In GameEngine
    Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    Dictionary<Color, Material> handleMaterials = new Dictionary<Color, Material>();
    Dictionary<string, string> fullNames = new Dictionary<string, string>();
    Shader standardShader = null;
    Texture checkerboardTex; //TODO: checkerboardSprite in GameEngine

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
        if (!absolute && !IsBuiltInMaterial(name)) fullName = GetInstance().FindInFolders(fullName);
        return GetInstance().GetMaterial_instance(fullName, name);
    }

    public static bool IsBuiltInMaterial(string name) {
        return name == "_TRANSPARENT_" || name == "_HANDLE_";
    }

    // Texture lists in type settings name the state property holding each texture; a built-in material name is used as is
    // (it is not looked up as a property, otherwise e.g. "_TRANSPARENT_" would end up as the missing texture material).
    public static string GetTextureName(ObjectState state, string entry) {
        return IsBuiltInMaterial(entry) ? entry : state.Str(entry);
    }

    public static void ClearCache() {
        //TODO: check if other stuff needs to be cleared
        GetInstance().ClearCache_instance();
    }

    private void ClearCache_instance() {
        fullNames.Clear();
        PathHelper.ClearCaseCache();
    }

    private string FindInFolders(string fullName) {
        if (fullName == null) return fullName;
        if (fullNames.ContainsKey(fullName)) return fullNames[fullName];
        var res = PathHelper.FindInFolders(fullName);
        fullNames.Add(fullName, res);
        return res;
    }

    /*public static Sprite GetSprite(string name, bool absolute = false) { //TODO: In GameEngine
        if (!absolute) name = PathHelper.FindInFolders(name);
        return GetInstance().GetSprite_instance(name);
    }*/

    public static Texture2D GetTexture(string name, bool absolute = false) {
        if (!absolute) name = PathHelper.FindInFolders(name);
        return GetInstance().GetTexture_instance(name);
    }

    public Texture GetCheckerboard() {
        if (checkerboardTex == null) {
            var cSize = 64;
            var sq = cSize / 4;
            var sq2 = sq / 2;
            var colors = new Color[cSize * cSize];
            for (int y = 0; y < cSize; y++) {
                for (int x = 0; x < cSize; x++) {
                    bool white = y % sq < sq2 ^ x % sq < sq2;
                    colors[y * cSize + x] = white ? GameEngine.Color.white : GameEngine.Color.gray;
                }
            }

            var checkerboardTex = GameEngine.Texture.CreateFromData(cSize, cSize, GameEngine.Texture.Format.RGB8, true, colors);

            //checkerboardSprite = Texture.Create(checkerboardTex, new Rect(0, 0, cSize, cSize), GameEngine.Vector2.zero); //TODO: GameEngine
        }
        return checkerboardTex;
    }

    public static MaterialManager GetInstance() {
        if (instance == null) instance = new MaterialManager();
        return instance;
    }

    public void UnloadAll() {
        foreach (var elem in materials) {
            GO.Destroy(elem.Value);
        }
        materials.Clear();
        /*foreach (var elem in sprites) { //TODO: in GameEngine
            Object.Destroy(elem.Value);
        }
        sprites.Clear();*/
        foreach (var elem in textures) {
            GO.Destroy(elem.Value);
        }
        textures.Clear();
    }

    MaterialManager() {
        standardShader = GameEngine.Materials.GetShader("Standard");
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
        var mat = GameEngine.Materials.CreateMaterial(standardShader, true);
        materials[name] = mat;
        GameEngine.Materials.SetColor(mat, GameEngine.Color.magenta);
        GameEngine.Materials.SetName(mat, shortName);
        return mat;
    }

    Material GetMaterial_instance(string name, string shortName) {
        Material mat;
        if (name == null) return CreateEmptyMaterial(name, shortName);
        if (materials.TryGetValue(name, out mat)) {
            return mat;
        } else {
            if (name == "_TRANSPARENT_") {
                mat = GameEngine.Materials.CreateMaterial(standardShader, true);
                SetBlendMode(mat, 2);
                GameEngine.Materials.SetColor(mat, new Color(1, 1, 1, 0.25f));
                materials[name] = mat;
                return mat;
            } else if (name == "_HANDLE_") {
                return GetHandleMaterial_instance(new Color(1, 1, 0, 1));
            } else {
                var tex = GetTexture_instance(name);
                if (tex != null) {
                    mat = GameEngine.Materials.CreateMaterial(standardShader, true);
                    GameEngine.Materials.SetMainTexture(mat, tex);
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
                    var transparent = GameEngine.Texture.HasAlphaChannel(tex);
                    if (transparent) SetBlendMode(mat, 1); // Make cutout
                    materials[name] = mat;
                    GameEngine.Materials.SetName(mat, shortName);
                    return mat;
                }
            }
        }
        return CreateEmptyMaterial(name, shortName);
    }

    /*Sprite GetSprite_instance(string name) { //TODO: In GameEngine
        if (name == null || name == "_TRANSPARENT_") return null;
        Sprite sprite;
        if (sprites.TryGetValue(name, out sprite)) {
            return sprite;
        } else {
            var tex = GetTexture_instance(name);
            if (tex != null) {
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), GameEngine.Vector2.zero);
                sprites[name] = sprite;
                return sprite;
            }
        }
        return null;
    }*/

    Material GetHandleMaterial_instance(Color color) {
        if (handleMaterials.ContainsKey(color)) {
            return handleMaterials[color];
        } else {
            // GD.Load returns the shared cached resource, so copy it.
            var mat = (Material)GameEngine.Resources.LoadMaterial("Materials/Line").Duplicate();
            GameEngine.Materials.SetColor(mat, color);
            handleMaterials.Add(color, mat);
            return mat;
        }
    }

    public static void SetBlendMode(Material mat, int blendMode) {
        GameEngine.Materials.SetBlendMode(mat, blendMode);
    }
}

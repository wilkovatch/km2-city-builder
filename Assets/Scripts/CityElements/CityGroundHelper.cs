using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using SM = StringManager;

public class CityGroundHelper : MonoBehaviour {
    [System.Serializable]
    public class CityPreferences {
        public string overlayTex;
        public string curTerrainTexture;
        public float curTerrainUVMult;

        public CityPreferences() {
            overlayTex = "";
            curTerrainTexture = "";
            curTerrainUVMult = 1.0f;
        }
    }

    public static int heightmapResolution = 4096;
    public static int terrainSize = 4096;

    Material mat, mat2;
    bool matIsGrid = true;
    int overlayIsTransparent = 0;
    public ElementManager elementManager;
    public CityBuilderMenuBar menuBar;

    public ProgressBar curProgressBar = null;

    public static float maxHeight = 1000;

    public GameObject terrainObj;

    void Start() {
        mat = Resources.Load("Materials/Grid", typeof(Material)) as Material;
        mat2 = Resources.Load("Materials/Default", typeof(Material)) as Material;
        mat2.SetFloat("_Opacity", 1.0f);
        mat2.SetVector("_Size", new Vector4(terrainSize, -terrainSize * 0.5f, terrainSize, -terrainSize * 0.5f));
        elementManager = gameObject.AddComponent<ElementManager>();
        curProgressBar = new ProgressBar(menuBar.gameObject, SM.Get("GENERATING_TERRAIN"));
        curProgressBar.SetActive(false);
        menuBar.ManualStart();
        var pyRes = PythonManager.CheckPython(menuBar);
        if (pyRes) {
            var lastCity = SettingsManager.Get("LastCity", "");
            if (PreferencesManager.workingDirectory == "" && lastCity != "" && File.Exists(lastCity)) {
                menuBar.CloseAllPanels();
                CityImporter.LoadCity(elementManager, lastCity);
            }
        }
    }

    public void SaveCity() {
        //set core feature version (since in case of non breaking change there is no migration)
        var core = PreferencesManager.Get("core", "");
        var curCoreFeatureVersion = CoreManager.GetCoreFeatureVersion(core);
        PreferencesManager.Set("coreFeatureVersion", curCoreFeatureVersion);

        SavePreferences();

        CityExporter.SaveCity(elementManager, PreferencesManager.workingDirectory + "/city.json", true);
    }

    public void SavePreferences() {
        PreferencesManager.Save();
    }

    public void SetHeights(float[,] heights) {
        Destroy(terrainObj);
        TerrainData tData = new TerrainData();
        tData.heightmapResolution = heightmapResolution;
        tData.size = new Vector3(terrainSize, 2 * maxHeight, terrainSize);
        tData.SetHeights(0, 0, heights);
        tData.SyncHeightmap();
        terrainObj = Terrain.CreateTerrainGameObject(tData);
        terrainObj.layer = gameObject.layer;
        terrainObj.GetComponent<Terrain>().materialTemplate = GetGridMaterial();
        terrainObj.transform.position = new Vector3(-terrainSize * 0.5f, -maxHeight, -terrainSize * 0.5f);
        mat2.SetVector("_Size", new Vector4(terrainSize, -terrainSize * 0.5f, terrainSize, -terrainSize * 0.5f));
    }

    public IEnumerator SetHeightsCoroutine(float[,] heights, float scale, System.Func<IEnumerator> post) {
        maxHeight = scale;
        curProgressBar.SetText(SM.Get("GENERATING_TERRAIN"));
        curProgressBar.SetActive(true);
        curProgressBar.SetProgress(0);
        yield return new WaitForEndOfFrame();
        SetHeights(heights);
        if (post != null) {
            yield return new WaitForEndOfFrame();
            StartCoroutine(post.Invoke());
        }
        yield return null;
    }

    public IEnumerator SetDefaultHeights(float scale, System.Func<IEnumerator> post) {
        maxHeight = scale;
        curProgressBar.SetText(SM.Get("GENERATING_TERRAIN"));
        curProgressBar.SetActive(true);
        curProgressBar.SetProgress(0);
        yield return new WaitForEndOfFrame();
        Destroy(terrainObj);
        TerrainData tData = new TerrainData();
        tData.heightmapResolution = heightmapResolution;
        tData.size = new Vector3(terrainSize, 2 * maxHeight, terrainSize);
        var size = heightmapResolution + 1;
        var heights = new float[size, size];
        for (int x = 0; x < size; x++) {
            for (int y = 0; y < size; y++) {
                heights[y, x] = 0.5f;// inHeights[y, x];
            }
            if (x % 512 == 0) {
                float percent = 1.0f * x / size;
                curProgressBar.SetProgress(percent);
                yield return new WaitForEndOfFrame();
            }
        }
        tData.SetHeights(0, 0, heights);
        tData.SyncHeightmap();
        terrainObj = Terrain.CreateTerrainGameObject(tData);
        terrainObj.layer = gameObject.layer;
        terrainObj.GetComponent<Terrain>().materialTemplate = GetGridMaterial();
        terrainObj.transform.position = new Vector3(-terrainSize * 0.5f, -maxHeight, -terrainSize * 0.5f);
        mat2.SetVector("_Size", new Vector4(terrainSize, -terrainSize * 0.5f, terrainSize, -terrainSize * 0.5f));
        curProgressBar.SetActive(false);
        if (post != null) {
            yield return new WaitForEndOfFrame();
            StartCoroutine(post.Invoke());
        }
        yield return null;
    }

    public void ApplyHeightmap(string filename, float scale) {
        StartCoroutine(ApplyHeightmapCoroutine(filename, scale));
    }

    IEnumerator ApplyHeightmapCoroutine(string filename, float scale) {
        curProgressBar.SetText(SM.Get("GENERATING_TERRAIN"));
        curProgressBar.SetActive(true);
        curProgressBar.SetProgress(0);
        var tex = MaterialManager.GetTexture(filename, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        var terrain = terrainObj.GetComponent<Terrain>();
        var tData = terrain.terrainData;
        tData.size = new Vector3(tData.size.x, 2 * maxHeight, tData.size.z);
        var size = heightmapResolution + 1;
        var heights = new float[size, size];
        yield return new WaitForEndOfFrame();
        for (int x = 0; x < size; x++) {
            for (int y = 0; y < size; y++) {
                heights[y, x] = 0.5f + (tex.GetPixelBilinear(1.0f * x / size, 1.0f * y / size).grayscale * 0.5f) * scale / maxHeight;
            }
            if (x % 512 == 0) {
                float percent = 1.0f * x / size;
                curProgressBar.SetProgress(percent);
                yield return new WaitForEndOfFrame();
            }
        }
        tData.SetHeights(0, 0, heights);
        curProgressBar.SetActive(false);
        yield return null;
    }

    float GetTransparency(int value) {
        switch (value) {
            case 0:
                return 1.0f;
            case 1:
                return 0.25f;
            case 2:
                return 0.0f;
            default:
                return 1.0f;
        }
    }

    void RestoreOverlay() {
        matIsGrid = !PreferencesManager.Get("OverlayActive", false);
        var transparentOverlay = PreferencesManager.Get("TransparentOverlay", 0);
        overlayIsTransparent = transparentOverlay;
        mat2.SetFloat("_Opacity", GetTransparency(transparentOverlay));
    }

    public void LoadOverlayTexture(string filename) {
        try {
            var tex = TextureImporter.LoadTexture(PathHelper.FindInFolders(filename));
            mat2.mainTexture = tex;
            RestoreOverlay();
        } catch (System.Exception e) {
            print(filename);
            print(e);
        }
    }

    Material GetGridMaterial() {
        return matIsGrid ? mat : mat2;
    }

    public void SwitchMaterial() {
        matIsGrid = !matIsGrid;
        terrainObj.GetComponent<Terrain>().materialTemplate = GetGridMaterial();
        PreferencesManager.Set("OverlayActive", !matIsGrid);
    }

    public void SwitchTransparency() {
        overlayIsTransparent += 1;
        if (overlayIsTransparent > 2) overlayIsTransparent = 0;
        mat2.SetFloat("_Opacity", GetTransparency(overlayIsTransparent));
        PreferencesManager.Set("TransparentOverlay", overlayIsTransparent);
    }

    public void SetCirclePosition(Vector3 HitPoint, float radius) {
        mat.SetVector("_Center", HitPoint);
        mat.SetFloat("_Radius", radius);
    }
}

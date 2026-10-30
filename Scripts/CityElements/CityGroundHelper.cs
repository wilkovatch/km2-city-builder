using System.Collections;
using System.Collections.Generic;
using System.IO;
using Godot;
using ThisScriptable = Godot.StaticBody3D;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using PhysicsGameObject = Godot.StaticBody3D;
using Terrain = GDExtension.Wrappers.Terrain3D;
using TerrainData = GDExtension.Wrappers.Terrain3DData;
using Material = Godot.ShaderMaterial;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using SM = StringManager;

public partial class CityGroundHelper: ThisScriptable {
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

    public Node3D terrainObj;

    //View->Hide terrain grid: the terrain isn't drawn and can't be clicked (saved in the city preferences)
    public static bool TerrainHidden { get; private set; }

    public override void _Ready() {
        // The editor's terrain raycasts use layer 3 (Godot physics layer 4).
        CollisionLayer = 1u << 3;
        if (menuBar == null) menuBar = GetTree().Root.FindChild("Editor", true, false) as CityBuilderMenuBar;
        if (menuBar == null) {
            GD.PushError("CityBuilderMenuBar is missing from the main scene.");
            return;
        }
        menuBar.helper = this;
        mat = GameEngine.Resources.LoadMaterial("Materials/Grid");
        mat2 = GameEngine.Resources.LoadMaterial("Materials/Default");
        if (mat == null || mat2 == null) {
            GD.PushError("Required terrain materials could not be loaded.");
            return;
        }
        GameEngine.Materials.SetShaderParameter(mat2, "_Opacity", 1.0f);
        GameEngine.Materials.SetShaderParameter(mat2, "_Size", new Vector4(terrainSize, -terrainSize * 0.5f, terrainSize, -terrainSize * 0.5f));
        var elementManagerObj = GS.Create<ElementManager>("Element Manager", this);
        elementManager = GS.Cast<ElementManager>(elementManagerObj);
        curProgressBar = new ProgressBar(menuBar, SM.Get("GENERATING_TERRAIN"));
        curProgressBar.SetActive(false);
        menuBar.ManualStart();
        if (Cli.CommandLine.IsCliSession) return; //Python is checked when the command loads a city
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
        SaveCityAsync();
    }

    async void SaveCityAsync() {
        var error = await Operations.CityFiles.Save(elementManager);
        if (error != null) {
            menuBar.CreateAlert(SM.Get("ERROR"), SM.Get("CITY_SAVE_ERROR_TEXT") + error, SM.Get("OK"));
        } else {
            menuBar.CreateAlert(SM.Get("SUCCESS"), SM.Get("CITY_SAVED_TEXT"), SM.Get("OK"));
        }
    }

    public void SavePreferences() {
        PreferencesManager.Save();
    }

    public void SetHeights(float[,] heights) {
        GO.Destroy(terrainObj);
        terrainObj = GameEngine.Terrain.CreateTerrainGameObject(heights, heightmapResolution, terrainSize, maxHeight, GetGridMaterial(), this);
        GameEngine.Materials.SetShaderParameter(mat2, "_Size", new Vector4(terrainSize, -terrainSize * 0.5f, terrainSize, -terrainSize * 0.5f));
        RefreshOverlay();
        RestoreTerrainVisibility();
    }

    //creates the terrain with the given heights
    public async System.Threading.Tasks.Task SetHeightsAsync(float[,] heights, float scale, Operations.ProgressReporter progressBar) {
        maxHeight = scale;
        progressBar.SetText(SM.Get("GENERATING_TERRAIN"));
        progressBar.SetActive(true);
        progressBar.SetProgress(0);
        await ToSignal(GetTree(), "process_frame");
        SetHeights(heights);
    }

    //creates a flat terrain
    public async System.Threading.Tasks.Task SetDefaultHeightsAsync(float scale, Operations.ProgressReporter progressBar) {
        maxHeight = scale;
        progressBar.SetText(SM.Get("GENERATING_TERRAIN"));
        progressBar.SetActive(true);
        progressBar.SetProgress(0);
        await ToSignal(GetTree(), "process_frame");
        GO.Destroy(terrainObj);
        var size = heightmapResolution + 1;
        var heights = new float[size, size];
        for (int x = 0; x < size; x++) {
            for (int y = 0; y < size; y++) {
                heights[y, x] = 0.5f;// inHeights[y, x];
            }
            if (x % 512 == 0) {
                float percent = 1.0f * x / size;
                progressBar.SetProgress(percent);
                await ToSignal(GetTree(), "process_frame");
            }
        }
        terrainObj = GameEngine.Terrain.CreateHeightmap(terrainSize, heightmapResolution, maxHeight, heights, this, GetGridMaterial());
        GameEngine.Materials.SetShaderParameter(mat2, "_Size", new Vector4(terrainSize, -terrainSize * 0.5f, terrainSize, -terrainSize * 0.5f));
        RefreshOverlay();
        RestoreTerrainVisibility();
        //the progress bar stays visible: the caller goes on loading the city
    }

    public void ApplyHeightmap(string filename, float scale) {
        _ = ApplyHeightmapAsync(filename, scale, null);
    }

    //sets the terrain heights from a grayscale image (a single undo step)
    public async System.Threading.Tasks.Task ApplyHeightmapAsync(string filename, float scale, System.IProgress<(float, string)> progress) {
        var text = SM.Get("GENERATING_TERRAIN");
        curProgressBar.SetText(text);
        curProgressBar.SetActive(true);
        curProgressBar.SetProgress(0);
        try {
            var tex = GameEngine.Texture.GetSampleableImage(MaterialManager.GetTexture(filename, true));
            var size = heightmapResolution + 1;
            var heights = new float[size, size];
            await ToSignal(GetTree(), "process_frame");
            for (int x = 0; x < size; x++) {
                for (int y = 0; y < size; y++) {
                    var c = GameEngine.Texture.ToGrayscale(GameEngine.Texture.SampleBilinear(tex, 1.0f * x / size, 1.0f * y / size));
                    heights[y, x] = 0.5f + c * 0.5f * scale / maxHeight;
                }
                if (x % 512 == 0) {
                    float percent = 1.0f * x / size;
                    curProgressBar.SetProgress(percent);
                    progress?.Report((percent, text));
                    await ToSignal(GetTree(), "process_frame");
                }
            }
            History.UndoManager.Begin(SM.Get("LOAD_HEIGHTMAP"));
            History.UndoManager.TouchWholeHeightmap();
            GameEngine.Terrain.SetHeightmap(terrainObj, maxHeight, heights);
            History.UndoManager.Commit();
        } finally {
            curProgressBar.SetActive(false);
        }
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
        GameEngine.Materials.SetShaderParameter(mat2, "_Opacity", GetTransparency(transparentOverlay));
        RefreshOverlay();
    }

    public void LoadOverlayTexture(string filename) {
        try {
            var tex = TextureImporter.LoadTexture(PathHelper.FindInFolders(filename));
            GameEngine.Materials.SetMainTexture(mat2, tex);
            RestoreOverlay();
        } catch (System.Exception e) {
            GameEngine.Print(filename);
            GameEngine.Print(e.Message);
        }
    }

    Material GetGridMaterial() {
        return matIsGrid ? mat : mat2;
    }

    public void SwitchMaterial() {
        matIsGrid = !matIsGrid;
        GameEngine.Terrain.SetMaterial(terrainObj, GetGridMaterial());
        PreferencesManager.Set("OverlayActive", !matIsGrid);
    }

    public void SetTerrainHidden(bool hidden) {
        PreferencesManager.Set("TerrainHidden", hidden);
        RestoreTerrainVisibility();
    }

    void RestoreTerrainVisibility() {
        TerrainHidden = PreferencesManager.workingDirectory != "" && PreferencesManager.Get("TerrainHidden", false);
        GameEngine.Terrain.SetVisible(terrainObj, !TerrainHidden);
    }

    //the mask without the terrain while it's hidden, for what the user clicks or places
    public static int ExcludeHiddenTerrain(int mask) {
        return TerrainHidden ? mask & ~(1 << 3) : mask;
    }

    public void SwitchTransparency() {
        overlayIsTransparent += 1;
        if (overlayIsTransparent > 2) overlayIsTransparent = 0;
        GameEngine.Materials.SetShaderParameter(mat2, "_Opacity", GetTransparency(overlayIsTransparent));
        PreferencesManager.Set("TransparentOverlay", overlayIsTransparent);
        RefreshOverlay();
    }

    // Terrain3D needs the parameters of the overlay material copied over.
    void RefreshOverlay() {
        if (!matIsGrid) GameEngine.Terrain.RefreshMaterial(terrainObj);
    }

    public void SetCirclePosition(Vector3 HitPoint, float radius) {
        GameEngine.Materials.SetShaderParameter(mat, "_Center", GameEngine.Vector3.DecodePosition(HitPoint));
        GameEngine.Materials.SetShaderParameter(mat, "_Radius", radius);
        if (matIsGrid) GameEngine.Terrain.RefreshMaterial(terrainObj);
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SM = StringManager;
using EditorPanels;
using System.Globalization;

public class CityBuilderMenuBar : MonoBehaviour {
    public CityGroundHelper helper;
    TerrainEditorPanel terrainEditorPanel = new TerrainEditorPanel();
    RoadEditorPanel roadEditorPanel = new RoadEditorPanel();
    IntersectionEditorPanel intersectionEditorPanel = new IntersectionEditorPanel();
    NewCityEditorPanel newCityEditorPanel = new NewCityEditorPanel();
    HeightmapEditorPanel heightmapEditorPanel = new HeightmapEditorPanel();
    PresetEditorPanel presetEditorPanel = new PresetEditorPanel();
    TerrainPatchEditorPanel terrainPatchEditorPanel = new TerrainPatchEditorPanel();
    EditorPanels.Buildings.LineEditorPanel buildingLineEditorPanel = new EditorPanels.Buildings.LineEditorPanel();
    WholeCityTerrainEditorPanel cityTerrainEditorPanel = new WholeCityTerrainEditorPanel();
    WholeCityBuildingsEditorPanel cityBuildingsEditorPanel = new WholeCityBuildingsEditorPanel();
    ObjectListEditorPanel objectListEditorPanel = new ObjectListEditorPanel();
    TextureListEditorPanel textureListEditorPanel = new TextureListEditorPanel();
    MeshListEditorPanel meshListEditorPanel = new MeshListEditorPanel();
    TransformPropertiesEditorPanel transformPropertiesEditorPanel = new TransformPropertiesEditorPanel();
    SettingsEditorPanel settingsEditorPanel = new SettingsEditorPanel();
    ProjectSettingsEditorPanel projectSettingsEditorPanel = new ProjectSettingsEditorPanel();
    public EditorPanels.Props.ContainerEditorPanel propContainerEditorPanel = new EditorPanels.Props.ContainerEditorPanel();
    public TerrainPatchBorderMeshEditorPanel tpBorderMeshEditorPanel = new TerrainPatchBorderMeshEditorPanel();
    BatchUpdateEditorPanel batchUpdateEditorPanel = new BatchUpdateEditorPanel();
    WholeCityRotationEditorPanel rotateCityPanel = new WholeCityRotationEditorPanel();
    CustomExporterEditorPanel customExporterPanel = new CustomExporterEditorPanel();

    ElementPlacer.RoadPlacer roadPlacer;
    ElementPlacer.TerrainPlacer terrainPlacer;
    ElementPlacer.BuildingPlacer buildingPlacer;
    public ElementPlacer.MeshPlacer meshPlacer;

    public TerrainClick terrainClick;
    public FileBrowserHelper fileBrowser;
    List<EditorPanel> panels;
    private bool quitEnabled = false;
    Alert alert;
    InputFieldPopup inputFieldPopup;
    public RuntimeGizmos.TransformGizmo gizmo;
    public static RuntimeGizmos.TransformGizmo staticGizmo;
    TerrainModifier.Null nullModifier, nullModifierNoSelect;

    GameObject selectedObject = null;
    bool noCity = false;

    public void ManualStart() {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged += PythonManager.StopServerEditor;
#endif
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Application.wantsToQuit += WantsToQuit;
        Application.targetFrameRate = 60;
        ActionHandlerManager.manager = helper.elementManager;

        gizmo.onAnyChange += NotifyChange;
        gizmo.onAnyChange += transformPropertiesEditorPanel.Refresh;
        staticGizmo = gizmo;
        nullModifier = new TerrainModifier.Null(helper, SelectObject);
        nullModifierNoSelect = new TerrainModifier.Null(helper, null);
        helper.elementManager.builder = this;

        roadPlacer = new ElementPlacer.RoadPlacer(helper.elementManager);
        terrainPlacer = new ElementPlacer.TerrainPlacer(helper.elementManager);
        buildingPlacer = new ElementPlacer.BuildingPlacer(helper.elementManager);
        meshPlacer = new ElementPlacer.MeshPlacer(helper.elementManager);

        fileBrowser = gameObject.AddComponent<FileBrowserHelper>();
        fileBrowser.enableMenuUI = EnableUI;

        //Menu bar
        var menuBar = GetComponent<MenuBar>();
        menuBar.Initialize();

        //File
        var entries = new List<string> {
            SM.Get("NEW_CITY"),
            SM.Get("LOAD_CITY"),
            SM.Get("RELOAD_CITY"),
            SM.Get("SAVE_CITY"),
            SM.Get("EXPORT_CITY"),
            SM.Get("CLOSE_CITY"),
            SM.Get("EXIT")
        };
        var actions = new List<System.Action> {
            NewCity,
            LoadCity,
            ReloadCity,
            SaveCity,
            ExportCity,
            CloseCity,
            Quit
        };
        menuBar.AddElement(SM.Get("FILE"), entries, actions);

        //Edit
        entries = new List<string> {
            SM.Get("PROJECT_SETTINGS"),
            SM.Get("TOGGLE_TERRAIN_EDIT_MENU"),
            SM.Get("LOAD_HEIGHTMAP"),
            SM.Get("MANAGE_PRESETS"),
            SM.Get("VIEW_OBJ_LST"),
            SM.Get("BATCH_UPDATE"),
            SM.Get("ROTATE_CITY")
        };
        actions = new List<System.Action> {
            ToggleProjectSettings,
            ToggleTerrainEdit,
            LoadHeightmap,
            OpenPresetList,
            OpenObjectList,
            OpenBatchUpdate,
            RotateCity
        };
        menuBar.AddElement(SM.Get("EDIT"), entries, actions);

        //View
        entries = new List<string> {
            SM.Get("TOGGLE_GROUND_OVERLAY"),
            SM.Get("CHANGE_OVERLAY_TRANSPARENCY"),
            SM.Get("SHOW_ALL_TRAFFIC_LANES"),
            SM.Get("TOGGLE_GROUND_ONLY"),
            SM.Get("RESET_WINDOW")
            /*, SM.Get("TOGGLE_WIREFRAME")*/ //TODO: fix wireframe in build
        };
        actions = new List<System.Action> {
            SwitchGrid,
            ChangeOverlayTransparency,
            ShowAllTrafficLanes,
            ToggleGroundOnly,
            ResetWindow
            /*, ToggleWireframe*/
        };
        menuBar.AddElement(SM.Get("VIEW"), entries, actions);

        //Create
        entries = new List<string> {
            SM.Get("CREATE_ROAD"),
            SM.Get("CREATE_TERRAIN_PATCH"),
            SM.Get("CREATE_BUILDING_LINE"),
            SM.Get("CREATE_WHOLE_CITY_TERRAIN"),
            SM.Get("CREATE_WHOLE_CITY_BUILDINGS"),
            SM.Get("CREATE_MESH_INSTANCE")
        };
        actions = new List<System.Action> {
            CreateRoad,
            CreateTerrain,
            CreateBuildingLine,
            CreateCityTerrain,
            CreateCityBuildings,
            CreateMeshInstance
        };
        menuBar.AddElement(SM.Get("CREATE"), entries, actions);

        //Settings
        entries = new List<string> {
            SM.Get("SETTINGS"),
        };
        actions = new List<System.Action> {
            ToggleSettings
        };
        menuBar.AddElement(SM.Get("SETTINGS_MENU"), entries, actions);

        //Help
        entries = new List<string> {
            SM.Get("HELP_QUICKSTART"),
            SM.Get("HELP_ABOUT")
        };
        actions = new List<System.Action> {
            ShowQuickstart,
            ShowAbout
        };
        menuBar.AddElement(SM.Get("HELP"), entries, actions);

        panels = new List<EditorPanel> {
            terrainEditorPanel,
            roadEditorPanel,
            intersectionEditorPanel,
            newCityEditorPanel,
            heightmapEditorPanel,
            presetEditorPanel,
            terrainPatchEditorPanel,
            cityTerrainEditorPanel,
            cityBuildingsEditorPanel,
            objectListEditorPanel,
            textureListEditorPanel,
            meshListEditorPanel,
            transformPropertiesEditorPanel,
            buildingLineEditorPanel,
            settingsEditorPanel,
            projectSettingsEditorPanel,
            propContainerEditorPanel,
            tpBorderMeshEditorPanel,
            batchUpdateEditorPanel,
            rotateCityPanel,
            customExporterPanel
        };

        foreach (var panel in panels) {
            panel.Initialize(gameObject);
        }

        DisableAllPanels(null);
        terrainClick.modifier = nullModifier;
        terrainClick.editEnabled = true;

        menuBar.SetAsLastSibling();

        Reload();
    }

    void EnableUI(bool enabled) {
        terrainClick.uiEnabled = enabled;
        if (Camera.main != null) {
            var controller = Camera.main.GetComponent<CameraController>();
            if (controller != null) {
                controller.controlsEnabled = enabled;
            }
        }
        var menuBar = GetComponent<MenuBar>();
        for (int i = 1; i < menuBar.GetElementCount() - 2; i++) {
            menuBar.EnableElement(i, enabled);
        }
    }

    public void Reload() {
        SM.Reload();
        foreach (var panel in panels) {
            panel.Initialize(gameObject);
        }
        var menuBar = GetComponent<MenuBar>();
        DisableAllPanels(null);
        if (PreferencesManager.workingDirectory == "") {
            for (int i = 1; i < menuBar.GetElementCount() - 2; i++) {
                menuBar.EnableElement(i, false);
            }
            noCity = true;
        } else {
            for (int i = 1; i < menuBar.GetElementCount() - 2; i++) {
                menuBar.EnableElement(i, true);
            }
            noCity = false;
        }
        roadPlacer.placementMode = ElementPlacer.RoadPlacer.PlacementMode.None;
        terrainPlacer.placementMode = ElementPlacer.TerrainPlacer.PlacementMode.None;
        buildingPlacer.placementMode = ElementPlacer.BuildingPlacer.PlacementMode.None;
        meshPlacer.placeEnabled = false;
        meshListEditorPanel.ResetEntries(); //otherwise when opened for the first time it would have broken paths due to prefs not being loaded at first load
        SetCameraRenderingMode(PreferencesManager.Get("renderingMode", 0), false);
        SetShadows(PreferencesManager.Get("shadowsEnabled", true), false);
        CityElements.Types.Parsers.TypeParser.GetRoadTypes(true);
        CityElements.Types.Parsers.TypeParser.GetPropsElementTypes(true);
        CityElements.Types.Parsers.TypeParser.GetPropsContainersTypes(true);
    }

    public void SetCameraRenderingMode(int mode) {
        SetCameraRenderingMode(mode, true);
    }

    void SetCameraRenderingMode(int mode, bool save) {
        if (Camera.main != null) {
            switch (mode) {
                case 1:
                    Camera.main.renderingPath = RenderingPath.VertexLit;
                    RenderSettings.ambientLight = Color.white;
                    EnableLight(false);
                    break;
                default:
                    Camera.main.renderingPath = RenderingPath.UsePlayerSettings;
                    RenderSettings.ambientLight = new Color(0.23f, 0.23f, 0.23f);
                    EnableLight(true);
                    break;
            }
        }
        if (save) PreferencesManager.Set("renderingMode", mode);
    }

    public void SetShadows(bool enabled) {
        SetShadows(enabled, true);
    }

    void EnableLight(bool enabled) {
        var obj = GameObject.Find("Directional Light");
        if (obj != null) {
            var l = obj.GetComponent<Light>();
            if (l != null) {
                l.enabled = enabled;
            }
        }
    }

    void SetShadows(bool enabled, bool save) {
        var obj = GameObject.Find("Directional Light");
        if (obj != null) {
            var l = obj.GetComponent<Light>();
            if (l != null) {
                l.shadows = enabled ? LightShadows.Soft : LightShadows.None;
            }
        }
        if (save) PreferencesManager.Set("shadowsEnabled", enabled);
    }

    public void SetPropsCullingDistance(string distance) {
        var v = float.Parse(distance);
        PreferencesManager.Set("propsCullingDistance", v);
        var cam = Camera.main;
        if (cam != null) {
            var comp = cam.gameObject.GetComponent<CameraController>();
            if (comp != null) {
                comp.SetPropsCullingDistance(v);
            }
        }
    }

    public void UnsetModifier(bool selectObject = true) {
        terrainClick.modifier = selectObject ? nullModifier : nullModifierNoSelect;
    }

    public void NotifyChange() {
        NotifyChange(false);
    }

    public void NotifyChange(bool propsChanged) {
        if (helper != null) {
            if (helper.elementManager != null) {
                helper.elementManager.FlagAsChanged(propsChanged);
            }
        }
    }

    public void NotifyUpdateCompleted() {
        foreach (var panel in panels) {
            if (panel.ActiveSelf()) panel.Update();
        }
    }

    void ToggleWireframe() {
        if (Camera.main != null) {
            Camera.main.gameObject.GetComponent<CameraController>().ToggleWireframe();
        }
    }

    void ShowAllTrafficLanes() {
        helper.elementManager.ShowAllTrafficLanes();
    }

    void ToggleGroundOnly() {
        if (Camera.main != null) {
            var res = Camera.main.gameObject.GetComponent<CameraController>().ToggleGroundOnly();
            nullModifier.groundOnly = res;
        }
    }

    void ShowQuickstart() {
        CreateAlert(SM.Get("HELP_QUICKSTART"), SM.Get("HELP_QUICKSTART_TEXT"), SM.Get("OK"), SM.Get("HELP_QUICKSTART_OPEN"), null, OpenQuickstartGuide);
    }

    void OpenQuickstartGuide() {
        Application.OpenURL(PathHelper.BasePath() + "/Quickstart guide.pdf");
    }

    void ShowAbout() {
        var text = SM.Get("HELP_ABOUT_TEXT");
        text = text.Replace("$VERSION", Application.version).Replace("$UNITY", Application.unityVersion);
        CreateAlert(SM.Get("HELP_ABOUT"), text, SM.Get("OK"), SM.Get("HELP_ABOUT_OPEN_LICENSE"), null, OpenLicense, 220, true);
    }


    void OpenLicense() {
        Application.OpenURL(PathHelper.BasePath() + "/License.txt");
    }

    void ToggleSettings() {
        ShowPanel(settingsEditorPanel);
    }

    void ToggleProjectSettings() {
        ShowPanel(projectSettingsEditorPanel);
    }

    void ChangeOverlayTransparency() {
        helper.SwitchTransparency();
    }

    void DisablePanelIfNeeded(EditorPanel panel, EditorPanel excluded) {
        foreach (var childPanel in panel.childPanels) {
            DisablePanelIfNeeded(childPanel, excluded);
        }
        if (excluded != panel) {
            if (panel.parentPanel != null) {
                panel.Terminate();
            } else {
                panel.SetActive(false);
            }
        }
    }

    public void CloseAllPanels() {
        DisableAllPanels(null);
    }

    void DisableAllPanels(EditorPanel excluded) {
        foreach (var panel in panels) {
            DisablePanelIfNeeded(panel, excluded);
        }
        helper.SetCirclePosition(Vector3.zero, 0);
    }

    void ShowPanel(EditorPanel panel) {
        DisableAllPanels(panel);
        panel.SetActive(true);
    }

    //Menu elements

    void LoadCity() {
        DeselectAndCloseCurrentPanel();
        fileBrowser.LoadFolder(LoadCityDirectory);
    }

    void LoadCityDirectory(string dir) {
        PreferencesManager.workingDirectory = dir;
        CoreManager.Reset();
        CityImporter.LoadCity(helper.elementManager, dir + "/city.json.gz");
    }

    void NoCityAlert() {
        CreateAlert(SM.Get("ERROR"), SM.Get("NO_CITY_ERROR"), SM.Get("OK"));
    }

    void SaveCity() {
        if (noCity) {
            NoCityAlert();
            return;
        }
        try {
            helper.SaveCity();
        } catch (System.Exception e) {
            print(e.Message);
            print(e.StackTrace);
            CreateAlert(SM.Get("ERROR"), SM.Get("CITY_SAVE_ERROR_TEXT") + e.Message, SM.Get("OK"));
        }
    }

    void NewCity() {
        ShowPanel(newCityEditorPanel);
    }

    void ExportCity() {
        if (noCity) {
            NoCityAlert();
            return;
        }
        DeselectAndCloseCurrentPanel();
        fileBrowser.SaveFile(ExportCityToFile);
    }

    void CloseCity() {
        DeselectAndCloseCurrentPanel();
        helper.elementManager.CloseCity();
        Reload();
    }

    void ExportCityToFile(string filename) {
        try {
            CityExporter.SaveCity(helper.elementManager, filename);
        } catch (System.Exception e) {
            print(e.Message);
            print(e.StackTrace);
            CreateAlert(SM.Get("ERROR"), SM.Get("CITY_SAVE_ERROR_TEXT") + e.Message, SM.Get("OK"));
        }
    }

    void ToggleTerrainEdit() {
        DisableAllPanels(terrainEditorPanel);
        terrainEditorPanel.Toggle();
        var editEnabled = terrainEditorPanel.ActiveSelf();
        terrainClick.editEnabled = editEnabled;
        helper.elementManager.ShowIntersections(false);
        if (editEnabled) {
            terrainEditorPanel.ReloadTerrainModifier();
        } else {
            terrainClick.modifier = nullModifier;
        }
    }

    void OpenPresetList() {
        ShowPanel(presetEditorPanel);
    }

    void LoadHeightmap() {
        ShowPanel(heightmapEditorPanel);
    }

    void OpenObjectList() {
        ShowPanel(objectListEditorPanel);
    }

    void OpenBatchUpdate() {
        ShowPanel(batchUpdateEditorPanel);
    }

    void RotateCity() {
        ShowPanel(rotateCityPanel);
    }

    public void OpenTextureSelector(GameObject panel, System.Func<object> valueGetter, System.Action<string> valueSetter, System.Action afterSetAction) {
        textureListEditorPanel.SetActive(true);
        textureListEditorPanel.SetSelection((string)valueGetter.Invoke());
        textureListEditorPanel.Open(panel, valueSetter, afterSetAction);
    }

    public void OpenMeshSelector(GameObject panel, System.Action<string> valueSetter, System.Action afterSetAction, bool disablePlacer = false) {
        if (disablePlacer) meshListEditorPanel.disablePlacer = true;
        meshListEditorPanel.Open(panel, valueSetter, afterSetAction);
    }

    void CreateMeshInstance() {
        ShowPanel(meshListEditorPanel);
    }

    void SwitchGrid() {
        helper.SwitchMaterial();
    }

    void ResetWindow() {
        Screen.SetResolution(1280, 720, false);
    }

    void ReloadCity() {
        CreateAlert(SM.Get("WARNING"), SM.Get("RELOAD_WARNING"), SM.Get("YES"), SM.Get("NO"), ReloadCityAction);
    }

    void ReloadCityAction() {
        CityImporter.LoadCity(helper.elementManager, PreferencesManager.workingDirectory + "/city.json.gz");
    }

    void CreateRoad() {
        roadEditorPanel.Terminate();
        DisableAllPanels(roadEditorPanel);
        terrainClick.editEnabled = true;
        roadPlacer.SetRoad(null);
        roadPlacer.placementMode = ElementPlacer.RoadPlacer.PlacementMode.Add;
        terrainClick.modifier = roadPlacer;
        roadEditorPanel.SetActive(true, true);
    }

    void CreateTerrain() {
        DisableAllPanels(terrainPatchEditorPanel);
        terrainClick.editEnabled = true;
        terrainPlacer.SetTerrainPatch(null);
        terrainPlacer.placementMode = ElementPlacer.TerrainPlacer.PlacementMode.Perimeter;
        terrainClick.modifier = terrainPlacer;
        terrainPatchEditorPanel.SetActive(true);
        helper.elementManager.ShowAnchors(true);
    }

    void CreateBuildingLine() {
        DisableAllPanels(buildingLineEditorPanel);
        terrainClick.editEnabled = true;
        buildingPlacer.SetBuildingLine(null);
        buildingPlacer.placementMode = ElementPlacer.BuildingPlacer.PlacementMode.Point;
        terrainClick.modifier = buildingPlacer;
        buildingLineEditorPanel.SetActive(true);
        helper.elementManager.ShowAnchors(true);
    }

    void CreateCityTerrain() {
        ShowPanel(cityTerrainEditorPanel);
    }

    void CreateCityBuildings() {
        ShowPanel(cityBuildingsEditorPanel);
    }

    void Quit() {
        Application.Quit();
    }

    void ForceQuit() {
        quitEnabled = true;
        Application.Quit();
    }

    bool AlertActive() {
        return alert != null && !alert.IsClosed();
    }

    bool InputActive() {
        return inputFieldPopup != null && !inputFieldPopup.IsClosed();
    }

    public void CreateAlert(string title, string message, string buttonText,
        System.Action genericAction = null, float height = 180.0f, bool leftAlign = false, float width = 400.0f) {

        if (!AlertActive()) alert = new Alert(gameObject, title, message, buttonText, genericAction, height, leftAlign, width);
    }

    public void CreateAlert(string title, string message, string okText, string cancelText,
        System.Action okAction = null, System.Action cancelAction = null, float height = 180.0f, bool leftAlign = false, float width = 400.0f) {

        if (!AlertActive()) alert = new Alert(gameObject, title, message, okText, cancelText, okAction, cancelAction, height, leftAlign, width);
    }

    public void CreateInput(string title, string placeholder, string buttonText, System.Action<string> genericAction = null) {
        if (!InputActive()) inputFieldPopup = new InputFieldPopup(gameObject, title, placeholder, buttonText, genericAction);
    }

    public void CreateInput(string title, string placeholder, string okText, string cancelText, System.Action<string> okAction = null, System.Action cancelAction = null, string initialText = null) {
        if (!InputActive()) inputFieldPopup = new InputFieldPopup(gameObject, title, placeholder, okText, cancelText, okAction, cancelAction, initialText);
    }

    public bool WantsToQuit() {
        if (quitEnabled || noCity) {
            return true;
        } else {
            CreateAlert(SM.Get("WARNING"), SM.Get("QUIT_WARNING"), SM.Get("YES"), SM.Get("NO"), ForceQuit);
            return false;
        }
    }

    public void DoDelayed(System.Action action) {
        StartCoroutine(DoDelayedCoroutine(action));
    }

    IEnumerator DoDelayedCoroutine(System.Action action) {
        yield return new WaitForEndOfFrame();
        action.Invoke();
    }

    public void SelectObject(RaycastHit hit) {
        SelectObject(hit.transform.gameObject, true, hit.collider);
    }

    public void SelectObject(GameObject obj) {
        SelectObject(obj, true, null);
    }

    public void DeselectObject(bool allowSelect) {
        if (selectedObject == null) return;
        var road = selectedObject.GetComponent<Road>();
        var intersection = selectedObject.GetComponentInChildren<Intersection.IntersectionComponent>();
        var patch = selectedObject.GetComponent<TerrainPatch>();
        var building = selectedObject.GetComponent<BuildingLine>();
        if (road != null) {
            road.SetActive(false);
        } else if (intersection != null) {
            intersection.intersection.SetActive(false);
        } else if (patch != null) {
            patch.SetActive(false, false);
        } else if (building != null) {
            building.SetActive(false, false);
        }
        selectedObject = null;
        if (allowSelect) terrainClick.modifier = nullModifier;
        helper.elementManager.ShowAnchors(false);
        helper.elementManager.ShowIntersections(false);
        helper.elementManager.DeselectMeshes();
    }

    public void EditRoadPresets() {
        roadEditorPanel.Terminate();
        DisableAllPanels(roadEditorPanel);
        terrainClick.editEnabled = true;
        roadPlacer.SetRoad(null);
        terrainClick.modifier = nullModifier;
        roadEditorPanel.SetActive(true, false);
    }

    public void EditIntersectionPresets() {
        intersectionEditorPanel.Terminate();
        DisableAllPanels(intersectionEditorPanel);
        terrainClick.editEnabled = true;
        intersectionEditorPanel.intersection = null;
        terrainClick.modifier = nullModifier;
        intersectionEditorPanel.SetActive(true);
    }

    public void EditBuildingLinePresets() {
        buildingLineEditorPanel.Terminate();
        DisableAllPanels(buildingLineEditorPanel);
        terrainClick.editEnabled = true;
        buildingPlacer.SetBuildingLine(null);
        terrainClick.modifier = nullModifier;
        buildingLineEditorPanel.SetActive(true);
    }

    public void ShowCustomExportDialog(FileBrowserHelper.CustomExporter exporter, string name) {
        DisableAllPanels(customExporterPanel);
        customExporterPanel.exporter = exporter;
        customExporterPanel.name = name;
        customExporterPanel.SetActive(true);
    }

    void ReselectObjectPanelAndModifier(GameObject obj, bool withEditor, EditorPanel panel, TerrainAction modifier) {
        DeselectObject(withEditor);
        selectedObject = obj;
        if (withEditor) {
            DisableAllPanels(panel);
            terrainClick.editEnabled = true;
            terrainClick.modifier = modifier;
        }
    }

    public void SelectObject(GameObject obj, bool withEditor, Collider c) {
        transformPropertiesEditorPanel.Select(false);
        var road = obj.GetComponent<Road>();
        var intersection = obj.GetComponentInChildren<Intersection.IntersectionComponent>();
        var patch = obj.GetComponent<TerrainPatch>();
        var mesh = obj.GetComponent<MeshInstance>();
        var buildingLine = obj.GetComponent<BuildingLine>();
        var buildingSide = (buildingLine != null && c != null && c is MeshCollider mc) ? buildingLine.GetColliderSide(mc) : null;
        var building = (buildingLine != null && c != null && c is MeshCollider mc2) ? buildingLine.GetColliderBuilding(mc2) : null;
        var selector = obj.GetComponent<GenericSelector>();
        if (road != null) {
            ReselectObjectPanelAndModifier(obj, withEditor, roadEditorPanel, roadPlacer);
            if (withEditor) roadPlacer.SetRoad(road);
            helper.elementManager.ShowIntersections(false);
            road.SetActive(true);
            if (withEditor) roadEditorPanel.SetActive(true);
        } else if (intersection != null) {
            ReselectObjectPanelAndModifier(obj, withEditor, intersectionEditorPanel, null);
            helper.elementManager.ShowIntersections(false);
            intersectionEditorPanel.intersection = intersection.intersection;
            intersection.intersection.SetActive(true, true, true);
            if (withEditor) intersectionEditorPanel.SetActive(true);
        } else if (patch != null) {
            ReselectObjectPanelAndModifier(obj, withEditor, terrainPatchEditorPanel, terrainPlacer);
            if (withEditor) terrainPlacer.SetTerrainPatch(patch);
            helper.elementManager.ShowAnchors(true);
            patch.SetActive(true, true);
            patch.Select(true);
            if (withEditor) terrainPatchEditorPanel.SetActive(true);
        } else if (mesh != null) {
            ReselectObjectPanelAndModifier(obj, withEditor, transformPropertiesEditorPanel, null);
            if (withEditor) {
                transformPropertiesEditorPanel.instance = mesh.gameObject;
                transformPropertiesEditorPanel.SetActive(true);
            } else {
                transformPropertiesEditorPanel.Select(false);
                transformPropertiesEditorPanel.instance = mesh.gameObject;
                transformPropertiesEditorPanel.Select(true);
            }
        } else if (buildingSide != null) {
            buildingLine = buildingSide.building.line;
            ReselectObjectPanelAndModifier(obj, withEditor, buildingLineEditorPanel, buildingPlacer);
            if (withEditor) buildingPlacer.SetBuildingLine(buildingLine);
            helper.elementManager.ShowAnchors(true);
            buildingLine.SetActive(true, true);
            buildingLine.Select(true);
            if (withEditor) {
                buildingLineEditorPanel.SetActive(true);
                if (Input.GetKey(KeyCode.LeftShift)) {
                    //do nothing => edit the line
                } else if (GetControl()) {
                    buildingLineEditorPanel.EditBuilding(buildingSide.building);
                } else {
                    buildingLineEditorPanel.EditSide(buildingSide);
                }
            }
        } else if (building != null) {
            buildingLine = building.line;
            ReselectObjectPanelAndModifier(obj, withEditor, buildingLineEditorPanel, buildingPlacer);
            if (withEditor) buildingPlacer.SetBuildingLine(buildingLine);
            helper.elementManager.ShowAnchors(true);
            buildingLine.SetActive(true, true);
            buildingLine.Select(true);
            if (withEditor) {
                buildingLineEditorPanel.SetActive(true);
                if (Input.GetKey(KeyCode.LeftShift)) {
                    //do nothing => edit the line
                } else {
                    buildingLineEditorPanel.EditBuilding(building);
                }
            }
        } else if (buildingLine != null) {
            ReselectObjectPanelAndModifier(obj, withEditor, buildingLineEditorPanel, buildingPlacer);
            if (withEditor) buildingPlacer.SetBuildingLine(buildingLine);
            helper.elementManager.ShowAnchors(true);
            buildingLine.SetActive(true, true);
            buildingLine.Select(true);
            if (withEditor) buildingLineEditorPanel.SetActive(true);
        } else if (selector != null) {
            switch (selector.type) {
                case "Building":
                    buildingLine = ((Building)selector.parent).line;
                    ReselectObjectPanelAndModifier(obj, withEditor, buildingLineEditorPanel, buildingPlacer);
                    if (withEditor) buildingPlacer.SetBuildingLine(buildingLine);
                    helper.elementManager.ShowAnchors(true);
                    buildingLine.SetActive(true, true);
                    buildingLine.Select(true);
                    if (withEditor) {
                        buildingLineEditorPanel.SetActive(true);
                        if (Input.GetKey(KeyCode.LeftShift)) {
                            //do nothing => edit the line
                        } else {
                            buildingLineEditorPanel.EditBuilding(((Building)selector.parent));
                        }
                    }
                    break;
                case "BuildingLine":
                    buildingLine = (BuildingLine)selector.parent;
                    ReselectObjectPanelAndModifier(obj, withEditor, buildingLineEditorPanel, buildingPlacer);
                    if (withEditor) buildingPlacer.SetBuildingLine(buildingLine);
                    helper.elementManager.ShowAnchors(true);
                    buildingLine.SetActive(true, true);
                    buildingLine.Select(true);
                    if (withEditor) buildingLineEditorPanel.SetActive(true);
                    break;
                default:
                    break;
            }
        }
    }

    void DeleteCurObj() {
        if (roadEditorPanel.ActiveSelf()) {
            roadEditorPanel.Delete(true);
        } else if (terrainPatchEditorPanel.ActiveSelf()) {
            if (gizmo.mainTargetRoot != null) {
                var point = gizmo.mainTargetRoot.GetComponent<TerrainPoint>();
                if (point != null) {
                    if (point.DeleteManual(terrainPatchEditorPanel.GetPatch())) {
                        gizmo.RemoveTarget(gizmo.mainTargetRoot);
                        point.Select(false);
                        NotifyChange();
                    }
                }
            } else {
                terrainPatchEditorPanel.Delete();
            }
        } else if (transformPropertiesEditorPanel.ActiveSelf()) {
            transformPropertiesEditorPanel.Delete();
        } else if (buildingLineEditorPanel.ActiveSelf()) {
            if (gizmo.mainTargetRoot != null) {
                var point = gizmo.mainTargetRoot.GetComponent<TerrainPoint>();
                if (point != null) {
                    if (point.DeleteManual(buildingLineEditorPanel.GetLine())) {
                        gizmo.RemoveTarget(gizmo.mainTargetRoot);
                        point.Select(false);
                        NotifyChange();
                    }
                }
            } else {
                buildingLineEditorPanel.Delete();
            }
        }
    }

    public Transform GetCurSelectedObject() {
        return gizmo.mainTargetRoot;
    }

    bool GetControl() {
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }

    void DeselectAndCloseCurrentPanel() {
        DisableAllPanels(null);
        DeselectObject(true);
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.Escape)) {
            DeselectAndCloseCurrentPanel();
        } else if (GetControl()) {
            if (Input.GetKeyDown(KeyCode.R)) {
                CreateRoad();
            } else if (Input.GetKeyDown(KeyCode.T)) {
                CreateTerrain();
            } else if (Input.GetKeyDown(KeyCode.E)) {
                CreateBuildingLine();
            } else if (Input.GetKeyDown(KeyCode.S)) {
                SaveCity();
            }
        } else if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject == null) {
            if (Input.GetKeyDown(KeyCode.Delete)) {
                DeleteCurObj();
            } else if (Input.GetKeyDown(KeyCode.M)) {
                var obj = GetCurSelectedObject();
                if (obj != null) {
                    terrainClick.modifier = null;
                    DisableAllPanels(transformPropertiesEditorPanel);
                    terrainClick.editEnabled = true;
                    transformPropertiesEditorPanel.instance = obj.gameObject;
                    transformPropertiesEditorPanel.SetActive(true);
                }
            }
        }
    }
}

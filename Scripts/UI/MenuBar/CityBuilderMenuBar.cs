using System.Collections;
using System.Collections.Generic;
using System.IO;
using Godot;
using GameObject = Godot.Node3D;
using SM = StringManager;
using EditorPanels;
using System.Globalization;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

public partial class CityBuilderMenuBar : Control {
    public CityGroundHelper helper;
    TerrainEditorPanel terrainEditorPanel = new TerrainEditorPanel();
    RoadEditorPanel roadEditorPanel = new RoadEditorPanel();
    IntersectionEditorPanel intersectionEditorPanel = new IntersectionEditorPanel();
    NewCityEditorPanel newCityEditorPanel = new NewCityEditorPanel();
    HeightmapEditorPanel heightmapEditorPanel = new HeightmapEditorPanel();
    PresetEditorPanel presetEditorPanel = new PresetEditorPanel();
    TerrainPatchEditorPanel terrainPatchEditorPanel = new TerrainPatchEditorPanel();
    EditorPanels.Buildings.LineEditorPanel buildingLineEditorPanel = new EditorPanels.Buildings.LineEditorPanel();
    public EditorPanels.CustomElementEditorPanel customElementEditorPanel = new EditorPanels.CustomElementEditorPanel();
    WholeCityTerrainEditorPanel cityTerrainEditorPanel = new WholeCityTerrainEditorPanel();
    WholeCityBuildingsEditorPanel cityBuildingsEditorPanel = new WholeCityBuildingsEditorPanel();
    ObjectListEditorPanel objectListEditorPanel = new ObjectListEditorPanel();
    TextureListEditorPanel textureListEditorPanel = new TextureListEditorPanel();
    MeshListEditorPanel meshListEditorPanel = new MeshListEditorPanel();
    TransformPropertiesEditorPanel transformPropertiesEditorPanel = new TransformPropertiesEditorPanel();
    SettingsEditorPanel settingsEditorPanel = new SettingsEditorPanel();
    ProjectSettingsEditorPanel projectSettingsEditorPanel = new ProjectSettingsEditorPanel();
    CityPropertiesEditorPanel cityPropertiesEditorPanel = new CityPropertiesEditorPanel();
    public EditorPanels.Props.ContainerEditorPanel propContainerEditorPanel = new EditorPanels.Props.ContainerEditorPanel();
    public TerrainPatchBorderMeshEditorPanel tpBorderMeshEditorPanel = new TerrainPatchBorderMeshEditorPanel();
    BatchUpdateEditorPanel batchUpdateEditorPanel = new BatchUpdateEditorPanel();
    WholeCityRotationEditorPanel rotateCityPanel = new WholeCityRotationEditorPanel();
    CustomExporterEditorPanel customExporterPanel = new CustomExporterEditorPanel();
    ScriptInputEditorPanel scriptInputEditorPanel = new ScriptInputEditorPanel();
    internal ValidationEditorPanel validationEditorPanel = new ValidationEditorPanel();

    ElementPlacer.RoadPlacer roadPlacer;
    ElementPlacer.TerrainPlacer terrainPlacer;
    ElementPlacer.BuildingPlacer buildingPlacer;
    public ElementPlacer.CustomElementPlacer customElementPlacer;
    public ElementPlacer.MeshPlacer meshPlacer;
    public ElementPlacer.ArrayPointPlacer arrayPointPlacer;
    public ElementPlacer.ReferencePicker referencePicker;

    public TerrainClick terrainClick;
    public FileBrowserHelper fileBrowser;
    List<EditorPanel> panels;
    private bool quitEnabled = false;
    Alert alert;
    InputFieldPopup inputFieldPopup;
    public RuntimeGizmos.TransformGizmo gizmo;
    public static RuntimeGizmos.TransformGizmo staticGizmo;
    public static int panelsActive = 0;
    TerrainModifier.Null nullModifier;
    int selectedEntityMenuItemIndex = -1;
    int scriptsMenuIndex = -1;
    int viewMenuIndex = -1;
    int createMenuIndex = -1;
    List<ScriptCatalog.ScriptInfo> menuScripts = new List<ScriptCatalog.ScriptInfo>();

    Node3D selectedObject = null;
    bool noCity = false;

    //state of the editor panel hidden while the transform panel edits one of its control points
    TerrainAction controlPointOwnerModifier = null;
    RuntimeGizmos.TransformType controlPointOwnerTransformType;

    public void ManualStart() {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Engine.MaxFps = 60;
        GetTree().AutoAcceptQuit = false; //quitting is handled in _Notification
        ActionHandlerManager.manager = helper.elementManager;
        History.UndoManager.manager = helper.elementManager;
        GameEngine.Input.ReloadKeys();
        terrainClick = GS.FindRecursive<TerrainClick>(this);
        if (terrainClick == null) {
            terrainClick = new TerrainClick { Name = "TerrainClick", MouseFilter = Control.MouseFilterEnum.Ignore };
            AddChild(terrainClick);
        }
        terrainClick.helper = helper;

        if (fileBrowser == null) {
            fileBrowser = new FileBrowserHelper { Name = "FileBrowser" };
            AddChild(fileBrowser);
        }
        fileBrowser.builder = this;
        fileBrowser.enableMenuUI = EnableUI;

        if (gizmo == null) {
            //TransformGizmo component on the main camera, configured in the scene
            gizmo = new RuntimeGizmos.TransformGizmo { Name = "TransformGizmo" };
            GetViewport().GetCamera3D().AddChild(gizmo);
        }
        gizmo.onAnyChange += NotifyChange;
        gizmo.onAnyChange += transformPropertiesEditorPanel.Refresh;
        gizmo.onTransformFinished += SnapTransformedObjects;
        staticGizmo = gizmo;
        nullModifier = new TerrainModifier.Null(helper, hit => {
            if (HasActivePanels()) return; //otherwise clicks on the transform gizmo reselect the object behind it
            var hitNode = hit.collider as Node3D;
            if (hitNode != null && !SelectCityPropertiesObject(hitNode)) SelectObject(hitNode, true, hit.shape);
        });
        helper.elementManager.builder = this;

        roadPlacer = new ElementPlacer.RoadPlacer(helper.elementManager);
        terrainPlacer = new ElementPlacer.TerrainPlacer(helper.elementManager);
        buildingPlacer = new ElementPlacer.BuildingPlacer(helper.elementManager);
        customElementPlacer = new ElementPlacer.CustomElementPlacer(helper.elementManager);
        meshPlacer = new ElementPlacer.MeshPlacer(helper.elementManager);
        arrayPointPlacer = new ElementPlacer.ArrayPointPlacer(this);
        referencePicker = new ElementPlacer.ReferencePicker(this);

        /*fileBrowser = gameObject.AddComponent<FileBrowserHelper>();
        fileBrowser.builder = this;
        fileBrowser.enableMenuUI = EnableUI;*/ //TODO: file browser

        //Menu bar
        var menuBar = GetMenuBar();
        menuBar.Initialize();

        //File
        var entries = new List<string> {
            SM.Get("NEW_CITY"),
            SM.Get("LOAD_CITY"),
            SM.Get("RELOAD_CITY"),
            SM.Get("SAVE_CITY"),
            SM.Get("EXPORT_CITY"),
            SM.Get("VALIDATE_CITY"),
            SM.Get("CLOSE_CITY"),
            SM.Get("EXIT")
        };
        var actions = new List<System.Action> {
            NewCity,
            LoadCity,
            ReloadCity,
            SaveCity,
            ExportCity,
            ValidateCity,
            CloseCity,
            Quit
        };
        menuBar.AddElement(SM.Get("FILE"), entries, Recorded(entries, actions), GameEngine.Color.black);

        //Edit
        entries = new List<string> {
            SM.Get("PROJECT_SETTINGS"),
            SM.Get("CITY_PROPERTIES"),
            SM.Get("TOGGLE_TERRAIN_EDIT_MENU"),
            SM.Get("LOAD_HEIGHTMAP"),
            SM.Get("MANAGE_PRESETS"),
            SM.Get("VIEW_OBJ_LST"),
            SM.Get("BATCH_UPDATE"),
            SM.Get("ROTATE_CITY")
        };
        actions = new List<System.Action> {
            ToggleProjectSettings,
            ToggleCityProperties,
            ToggleTerrainEdit,
            LoadHeightmap,
            OpenPresetList,
            OpenObjectList,
            OpenBatchUpdate,
            RotateCity
        };
        actions = Recorded(entries, actions);
        //undo and redo must not run inside a transaction
        entries.InsertRange(0, new List<string> { SM.Get("UNDO"), SM.Get("REDO"), MenuBar.Divider });
        actions.InsertRange(0, new List<System.Action> { Undo, Redo, null });
        menuBar.AddElement(SM.Get("EDIT"), entries, actions, GameEngine.Color.black);
        var editMenuIndex = menuBar.GetElementCount() - 1;
        menuBar.SetOnOpen(editMenuIndex, () => {
            menuBar.EnableEntry(editMenuIndex, 0, !noCity && History.UndoManager.CanUndo);
            menuBar.EnableEntry(editMenuIndex, 1, !noCity && History.UndoManager.CanRedo);
        });

        //View (the entries are listed when it opens: the toggles of the core follow the program ones)
        menuBar.AddElement(SM.Get("VIEW"), new List<string>(), new List<System.Action>(), GameEngine.Color.black);
        viewMenuIndex = menuBar.GetElementCount() - 1;
        ReloadViewMenu();
        menuBar.SetOnOpen(viewMenuIndex, ReloadViewMenu);

        //Create (core entries are resolved only after a city has been loaded)
        menuBar.AddElement(SM.Get("CREATE"), new List<string>(), new List<System.Action>(), GameEngine.Color.black);
        createMenuIndex = menuBar.GetElementCount() - 1;
        menuBar.SetOnOpen(createMenuIndex, ReloadCreateMenu);

        //Scripts (the entries are listed when it opens, the runs are undo steps of their own)
        menuBar.AddElement(SM.Get("SCRIPTS"), new List<string>(), new List<System.Action>(), GameEngine.Color.black);
        scriptsMenuIndex = menuBar.GetElementCount() - 1;
        menuBar.SetOnOpen(scriptsMenuIndex, ReloadScriptsMenu);

        //Settings
        entries = new List<string> {
            SM.Get("SETTINGS"),
        };
        actions = new List<System.Action> {
            ToggleSettings
        };
        menuBar.AddElement(SM.Get("SETTINGS_MENU"), entries, actions, GameEngine.Color.black);

        //Help
        entries = new List<string> {
            SM.Get("HELP_QUICKSTART"),
            SM.Get("HELP_ABOUT")
        };
        actions = new List<System.Action> {
            ShowQuickstart,
            ShowAbout
        };
        menuBar.AddElement(SM.Get("HELP"), entries, actions, GameEngine.Color.black);

        //Currently selected instances
        entries = new List<string> { };
        actions = new List<System.Action> { };
        menuBar.AddElement(SM.Get("CONTAINERS_SELECTED_PLUR").Replace("$NUMBER", "9"), entries, actions, GameEngine.Color.blue);
        selectedEntityMenuItemIndex = menuBar.GetElementCount() - 1;
        menuBar.ShowElement(selectedEntityMenuItemIndex, false);

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
            customExporterPanel,
            cityPropertiesEditorPanel,
            scriptInputEditorPanel,
            validationEditorPanel,
            customElementEditorPanel
        };

        menuBar.SetAsLastSibling();

        Reload();
        SetSelectObjectMode();
        Api.ApiHost.Create(this);
        Cli.CliRunner.Start(this); //only when started with a command on the command line
        if (!Cli.CommandLine.IsCliSession) Mcp.McpHttp.ApplySettings(); //if enabled in the settings
    }

    //the menu actions can change the city (e.g. closing an editor deletes the element left incomplete), so they are undo steps
    static List<System.Action> Recorded(List<string> labels, List<System.Action> actions) {
        var res = new List<System.Action>();
        for (int i = 0; i < actions.Count; i++) {
            var label = labels[i];
            var action = actions[i];
            res.Add(action == null ? null : () => History.UndoManager.Record(label, action));
        }
        return res;
    }

    void PrepareForUndo() {
        DeselectAndCloseCurrentPanel();
        gizmo.ClearTargets();
        ArrayObject.DeselectAll();
    }

    void AfterUndo(OpenEditorState editorState) {
        helper.elementManager.ShowAnchors(false);
        helper.elementManager.ShowIntersections(false);
        NotifyVisibilityChange();
        SetSelectObjectMode();
        RestoreEditorState(editorState);
    }

    public void PerformUndo() {
        Undo();
    }

    public void PerformRedo() {
        Redo();
    }

    //before a change made by a script: the editors are closed like before an undo (closing them can change the city,
    //e.g. deleting an element left incomplete: that is a separate undo step)
    public void BeginExternalChange() {
        if (!HasActivePanels() && gizmo.mainTargetRoot == null) return;
        History.UndoManager.Record(SM.Get("CLOSE"), PrepareForUndo);
    }

    //after a change made by a script: the elements are shown as when nothing is selected
    public void EndExternalChange() {
        helper.elementManager.ShowAnchors(false);
        helper.elementManager.ShowIntersections(false);
        NotifyVisibilityChange();
        SetSelectObjectMode();
    }

    void Undo() {
        if (noCity || History.UndoManager.IsBusy || History.UndoManager.IsGroupOpen) return; //a script transaction is open
        var editorState = CaptureEditorState();
        //closing the editor can delete the element it left incomplete: that is recorded into the last step
        //(and if it was the whole last step, closing the editor is the undo)
        var consumed = History.UndoManager.AmendLast(PrepareForUndo);
        if (!consumed) History.UndoManager.Undo();
        while (History.UndoManager.LastAppliedLeftIncompleteElements() && History.UndoManager.CanUndo) History.UndoManager.Undo();
        AfterUndo(editorState);
    }

    void Redo() {
        if (noCity || History.UndoManager.IsBusy || History.UndoManager.IsGroupOpen) return; //a script transaction is open
        var editorState = CaptureEditorState();
        //any change made by closing the editor is a new step, which discards the steps to redo
        History.UndoManager.Record(SM.Get("CLOSE"), PrepareForUndo);
        History.UndoManager.Redo();
        while (History.UndoManager.LastAppliedLeftIncompleteElements() && History.UndoManager.CanRedo) History.UndoManager.Redo();
        AfterUndo(editorState);
    }

    // Reopening the editors closed by undo/redo

    //the editor panels open, from the main one to the one shown, and how to reopen each of them
    OpenEditorState CaptureEditorState() {
        try {
            var shown = FindShownPanel();
            if (shown == null) return null;
            var chain = new List<EditorPanel> { shown };
            var cur = shown;
            while (true) {
                //the child panels created by their parent always have it, the parent of the others (transform panel,
                //props container, border mesh...) is set when they are opened from it, and only counts while it's open
                var parent = cur.parentPanel;
                if (parent == null || chain.Contains(parent) || !(parent.childPanels.Contains(cur) || parent.IsOpen())) break;
                chain.Insert(0, parent);
                cur = parent;
            }
            var state = new OpenEditorState();
            for (int i = 0; i < chain.Count; i++) {
                var panel = chain[i];
                System.Func<EditorPanel, EditorPanel> reopen;
                if (i == 0) reopen = CaptureRootReopen(panel);
                else if (panel == transformPropertiesEditorPanel) reopen = CaptureTransformChildReopen();
                else reopen = panel.CaptureReopen();
                if (reopen == null) break;
                state.steps.Add((reopen, panel.GetShownPage()));
            }
            if (state.steps.Count == 0) return null;
            var selected = gizmo.mainTargetRoot;
            if (chain.Count == 1 && IsElementEditor(shown) && selected != null && GodotObject.IsInstanceValid(selected) && GS.Cast<Handle>(selected) != null) {
                state.selectedPoint = CapturePointLocator(selected);
                state.transformType = gizmo.transformType;
            }
            return state;
        } catch (System.Exception e) {
            GameEngine.LogError("Undo: could not record the open editor: " + e);
            return null;
        }
    }

    void RestoreEditorState(OpenEditorState state) {
        if (state == null) return;
        History.UndoManager.Suppress(true); //reopening the editors is not a change
        try {
            //the parametric objects and the buildings of the restored elements are regenerated by the update
            try {
                helper.elementManager.ProcessUpdate();
            } catch (System.Exception e) {
                GameEngine.LogError("Undo: error when updating the city: " + e);
            }
            EditorPanel parent = null;
            var restored = 0;
            foreach (var (reopen, page) in state.steps) {
                var panel = reopen(parent);
                if (panel == null || !panel.IsOpen()) break;
                panel.ShowPage(page);
                parent = panel;
                restored++;
            }
            if (restored == state.steps.Count && state.selectedPoint != null) {
                var point = state.selectedPoint();
                if (point != null) {
                    gizmo.ClearTargets();
                    gizmo.AddTarget(point);
                    gizmo.transformType = state.transformType;
                }
            }
        } catch (System.Exception e) {
            GameEngine.LogError("Undo: could not reopen the editor: " + e);
            DeselectAndCloseCurrentPanel();
        } finally {
            History.UndoManager.Suppress(false);
        }
    }

    EditorPanel FindShownPanel() {
        foreach (var panel in panels) {
            var shown = FindShownPanel(panel);
            if (shown != null) return shown;
        }
        //picking a mesh for a field of a panel, hidden meanwhile
        return meshListEditorPanel.IsSelector() ? meshListEditorPanel.GetOpener() : null;
    }

    EditorPanel FindShownPanel(EditorPanel panel) {
        //the texture and mesh selectors are not reopened (their actions belong to the field they were opened from)
        if (panel == textureListEditorPanel || (panel == meshListEditorPanel && meshListEditorPanel.IsSelector())) return null;
        foreach (var childPanel in panel.childPanels) {
            var shown = FindShownPanel(childPanel);
            if (shown != null) return shown;
        }
        return panel.ActiveSelf() ? panel : null;
    }

    bool IsElementEditor(EditorPanel panel) {
        return panel == roadEditorPanel || panel == intersectionEditorPanel || panel == terrainPatchEditorPanel || panel == buildingLineEditorPanel
            || panel == customElementEditorPanel;
    }

    System.Func<EditorPanel, EditorPanel> CaptureRootReopen(EditorPanel root) {
        //while the transform panel edits a control point, the click modifier of the editor of its element is put aside
        var modifier = transformPropertiesEditorPanel.ActiveSelf() && root != transformPropertiesEditorPanel ? controlPointOwnerModifier : terrainClick.modifier;
        var editingPresets = modifier is TerrainModifier.Null;
        if (root == roadEditorPanel) {
            if (editingPresets) return _ => { EditRoadPresets(); return roadEditorPanel; };
            var mode = (int)roadPlacer.placementMode;
            return CaptureElementReopen((modifier as ElementPlacer.RoadPlacer)?.GetRoad(false), roadEditorPanel, () => roadEditorPanel.SetPointPlacementMode(mode));
        }
        if (root == intersectionEditorPanel) {
            if (editingPresets) return _ => { EditIntersectionPresets(); return intersectionEditorPanel; };
            return CaptureElementReopen(intersectionEditorPanel.intersection, intersectionEditorPanel, null);
        }
        if (root == terrainPatchEditorPanel) {
            var mode = (int)terrainPlacer.placementMode;
            var borderMesh = terrainPatchEditorPanel.GetCurBorderMeshIndex();
            return CaptureElementReopen((modifier as ElementPlacer.TerrainPlacer)?.GetPatch(false), terrainPatchEditorPanel, () => terrainPatchEditorPanel.SetPointPlacementMode(mode, borderMesh));
        }
        if (root == buildingLineEditorPanel) {
            if (editingPresets) return _ => { EditBuildingLinePresets(); return buildingLineEditorPanel; };
            var mode = (int)buildingPlacer.placementMode;
            return CaptureElementReopen((modifier as ElementPlacer.BuildingPlacer)?.GetLine(false), buildingLineEditorPanel, () => buildingLineEditorPanel.SetPointPlacementMode(mode));
        }
        if (root == customElementEditorPanel) {
            var element = (modifier as ElementPlacer.CustomElementPlacer)?.GetElement();
            if (element == null) {
                var type = customElementEditorPanel.creationType;
                var group = customElementEditorPanel.creationGroup;
                var draft = customElementEditorPanel.CaptureCreationState();
                return _ => {
                    if (editingPresets) EditCustomElementPresets(type);
                    else StartCustomElementCreation(type, group);
                    customElementEditorPanel.RestoreCreationState(draft);
                    return customElementEditorPanel;
                };
            }
            var mode = customElementEditorPanel.GetPointPlacementMode();
            return CaptureElementReopen(element, customElementEditorPanel, () => customElementEditorPanel.SetPointPlacementMode(mode));
        }
        if (root == transformPropertiesEditorPanel) {
            var mesh = CaptureMeshLocator(transformPropertiesEditorPanel.instance);
            if (mesh == null) return null;
            return _ => {
                var node = mesh();
                if (node == null) return null;
                SelectObject(node);
                return transformPropertiesEditorPanel;
            };
        }
        if (root == terrainEditorPanel) {
            return _ => {
                DisableAllPanels(terrainEditorPanel);
                terrainEditorPanel.SetActive(true);
                helper.elementManager.ShowIntersections(false);
                terrainEditorPanel.ReloadTerrainModifier();
                return terrainEditorPanel;
            };
        }
        var standalonePanels = new List<EditorPanel> {
            cityPropertiesEditorPanel, newCityEditorPanel, heightmapEditorPanel, presetEditorPanel, cityTerrainEditorPanel,
            cityBuildingsEditorPanel, objectListEditorPanel, meshListEditorPanel, settingsEditorPanel, projectSettingsEditorPanel,
            batchUpdateEditorPanel, rotateCityPanel, customExporterPanel, scriptInputEditorPanel
        };
        if (standalonePanels.Contains(root)) {
            return _ => {
                ShowPanel(root);
                return root;
            };
        }
        return null;
    }

    //the editor of a city element, reopened by selecting the element again
    System.Func<EditorPanel, EditorPanel> CaptureElementReopen(object element, EditorPanel panel, System.Action restoreMode) {
        var id = History.ElementRegistry.GetId(element);
        if (id < 0) return null;
        return _ => {
            var current = History.UndoManager.GetAliveElement(id);
            //opening the editor of an element left incomplete would delete it
            if (current == null || History.UndoManager.IsIncomplete(current)) return null;
            var node = current is Intersection intersection ? intersection.geo : current as Node3D;
            if (node == null) return null;
            SelectObject(node, true, null);
            restoreMode?.Invoke();
            return panel;
        };
    }

    //the transform panel opened from another panel: on a control point of the element it edits, or on a parametric object
    System.Func<EditorPanel, EditorPanel> CaptureTransformChildReopen() {
        var instance = transformPropertiesEditorPanel.instance;
        if (instance == null || !GodotObject.IsInstanceValid(instance)) return null;
        if (GS.Cast<Handle>(instance) != null) {
            var point = CapturePointLocator(instance);
            if (point == null) return null;
            var ownerTransformType = controlPointOwnerTransformType;
            return parent => {
                var node = point();
                if (node == null) return null;
                gizmo.ClearTargets();
                gizmo.AddTarget(node);
                gizmo.transformType = ownerTransformType;
                EditCurSelectedObjectTransform();
                return transformPropertiesEditorPanel;
            };
        }
        var mesh = CaptureMeshLocator(instance);
        if (mesh == null) return null;
        return parent => {
            var node = mesh();
            if (node == null) return null;
            //as when opened from the parent panel (EditorPanel.EditObjectInstanceParameter)
            SetTransformPanelParent(parent);
            parent.Hide(true);
            SelectObject(node);
            if (!parent.IsOpen()) parent.Hide(true);
            return transformPropertiesEditorPanel;
        };
    }

    //a mesh instance, or a parametric object found again by its path from the element generating it
    System.Func<Node3D> CaptureMeshLocator(Node3D node) {
        var mesh = node != null && GodotObject.IsInstanceValid(node) ? GS.Cast<MeshInstance>(node) : null;
        if (mesh == null) return null;
        if (mesh.objectParameter != null) return CaptureParametricLocator(mesh);
        var id = History.ElementRegistry.GetId(mesh);
        if (id < 0) return null;
        return () => History.UndoManager.GetAliveElement(id) as MeshInstance;
    }

    System.Func<Node3D> CaptureParametricLocator(MeshInstance mesh) {
        //the mesh is in a node kept by its container (a road, the city properties, or an array element of one of them)
        var holder = mesh.GetParent() as Node3D;
        var container = holder?.GetParent() as IParametricObjectContainer;
        var key = container?.FindObjectKey(holder);
        if (key == null) return null;
        var parentParams = new List<string>(); //from the innermost array element, as in GetParametricObject
        while (container is ArrayObject arrayObject) {
            var parentContainer = arrayObject.GetParent() as IParametricObjectContainer;
            var index = parentContainer?.IndexOfArrayObject(arrayObject) ?? -1;
            if (index < 0) return null;
            parentParams.Add(arrayObject.paramInParent + "." + index);
            container = parentContainer;
        }
        var id = History.ElementRegistry.GetId(container);
        if (id < 0) return null;
        return () => {
            var owner = History.UndoManager.GetAliveElement(id) as IParametricObjectContainer;
            return owner?.FindParametricObject(key, new List<string>(parentParams));
        };
    }

    //a control point (of a road, an intersection or a terrain point), found again by its element and index
    System.Func<Node3D> CapturePointLocator(Node3D node) {
        var owner = History.UndoManager.ResolveOwner(node);
        var id = History.ElementRegistry.GetId(owner);
        if (id < 0) return null;
        if (owner is TerrainPoint) {
            return () => (History.UndoManager.GetAliveElement(id) as TerrainPoint)?.GetObject();
        }
        if (owner is Intersection intersection && intersection.point == node) {
            return () => (History.UndoManager.GetAliveElement(id) as Intersection)?.point;
        }
        if (owner is Road road) {
            var index = road.points.IndexOf(node as StaticBody3D);
            if (index < 0) return null;
            return () => {
                var current = History.UndoManager.GetAliveElement(id) as Road;
                return current != null && index < current.points.Count ? current.points[index] : null;
            };
        }
        if (owner is CustomElement element && element.GetMoveHandle() == node) {
            return () => (History.UndoManager.GetAliveElement(id) as CustomElement)?.GetMoveHandle();
        }
        return null;
    }

    MenuBar GetMenuBar() {
        return GS.FindRecursive<MenuBar>(this);
    }

    CameraController GetCameraController() {
        var cam = GameEngine.Camera.Get(this);
        return GS.Cast<CameraController>(cam);
    }

    void EnableUI(bool enabled) {
        terrainClick.uiEnabled = enabled;
        var cam = GetCameraController();
        if (cam != null) cam.controlsEnabled = enabled;
        var menuBar = GetMenuBar();
        for (int i = 1; i < menuBar.GetElementCount() - 3; i++) {
            menuBar.EnableElement(i, enabled);
        }
    }

    //while a script runs the editor is locked: no clicks on the city, no menus (except Help), no shortcuts
    public void SetScriptRunning(bool running) {
        terrainClick.uiEnabled = !running;
        var menuBar = GetMenuBar();
        var count = menuBar.GetElementCount();
        menuBar.EnableElement(0, !running);
        for (int i = 1; i < count - 3; i++) menuBar.EnableElement(i, !running && !noCity);
        menuBar.EnableElement(count - 3, !running);
    }

    // View

    void ReloadViewMenu() {
        var entries = new List<string> {
            SM.Get("TOGGLE_GROUND_OVERLAY"),
            SM.Get("CHANGE_OVERLAY_TRANSPARENCY"),
            SM.Get("SHOW_ALL_TRAFFIC_LANES"),
            SM.Get("TOGGLE_GROUND_ONLY"),
            SM.Get("TOGGLE_WIREFRAME"),
            SM.Get("RESET_WINDOW")
        };
        var actions = Recorded(entries, new List<System.Action> {
            SwitchGrid,
            ChangeOverlayTransparency,
            ShowAllTrafficLanes,
            ToggleGroundOnly,
            ToggleWireframe,
            ResetWindow
        });
        //camera moves are not undo steps
        entries.Add(SM.Get("ZOOM_TO_SELECTION"));
        actions.Add(ZoomToSelection);
        //the visibility toggles of the core (not undo steps: they're view settings)
        var toggles = noCity ? new List<Rendering.ViewToggles.Toggle>() : Rendering.ViewToggles.List();
        if (toggles.Count > 0) {
            entries.Add(MenuBar.Divider);
            actions.Add(null);
        }
        var firstToggle = entries.Count;
        foreach (var toggle in toggles) {
            var name = toggle.name;
            entries.Add(SM.Get(toggle.label ?? name));
            actions.Add(() => helper.elementManager.SetViewToggle(name, !Rendering.ViewToggles.IsOn(name)));
        }
        var menuBar = GetMenuBar();
        menuBar.SetEntries(viewMenuIndex, entries, actions);
        for (int i = 0; i < toggles.Count; i++) menuBar.SetEntryChecked(viewMenuIndex, firstToggle + i, Rendering.ViewToggles.IsOn(toggles[i].name));
    }

    public void ReloadCreateMenu() {
        var entries = new List<string> { SM.Get("CREATE_ROAD"), SM.Get("CREATE_TERRAIN_PATCH"),
            SM.Get("CREATE_BUILDING_LINE"), SM.Get("CREATE_MESH_INSTANCE") };
        var actions = new List<System.Action> { CreateRoad, CreateTerrain, CreateBuildingLine, CreateMeshInstance };
        var groups = new HashSet<string>();
        if (!noCity && PathHelper.CoreAvailable()) {
            foreach (var pair in CityElements.Types.Parsers.TypeParser.GetCustomElementTypes()) {
                var settings = pair.Value.typeData.settings;
                if (!settings.creatable) continue;
                var type = pair.Key;
                var group = settings.createMenu?.group ?? "CREATE_CUSTOM_ELEMENT";
                if (settings.createMenu?.direct == true) {
                    entries.Add(Operations.CustomElements.GetTypeLabel(type));
                    actions.Add(() => StartCustomElementCreation(type));
                } else if (groups.Add(group)) {
                    entries.Add(SM.Get(group));
                    actions.Add(() => StartCustomElementCreation(null, group));
                }
            }
        }
        entries.AddRange(new[] { SM.Get("CREATE_PROP_RULE"), MenuBar.Divider, SM.Get("CREATE_WHOLE_CITY_TERRAIN"), SM.Get("CREATE_WHOLE_CITY_BUILDINGS") });
        actions.AddRange(new System.Action[] { CreatePropPath, null, CreateCityTerrain, CreateCityBuildings });
        GetMenuBar().SetEntries(createMenuIndex, entries, Recorded(entries, actions));
    }

    // Scripts

    void ReloadScriptsMenu() {
        menuScripts = ScriptCatalog.List();
        var entries = new List<string>();
        var actions = new List<System.Action>();
        var lastSource = menuScripts.Count > 0 ? menuScripts[0].source : null;
        foreach (var script in menuScripts) {
            if (script.source != lastSource) {
                entries.Add(MenuBar.Divider);
                actions.Add(null);
                lastSource = script.source;
            }
            var s = script;
            entries.Add(script.label + (script.HasInputs ? "..." : ""));
            actions.Add(() => StartScript(s));
        }
        if (entries.Count > 0) {
            entries.Add(MenuBar.Divider);
            actions.Add(null);
        }
        entries.Add(SM.Get("SCRIPTS_OPEN_FOLDER"));
        actions.Add(OpenScriptsFolder);
        GetMenuBar().SetEntries(scriptsMenuIndex, entries, actions);
    }

    void StartScript(ScriptCatalog.ScriptInfo script) {
        if (noCity) {
            NoCityAlert();
            return;
        }
        var selection = GetSelectedElementIds();
        if (script.HasInputs) {
            DisableAllPanels(scriptInputEditorPanel);
            scriptInputEditorPanel.Open(script, selection);
        } else {
            _ = Api.ScriptRunner.Run(this, script, null, selection);
        }
    }

    void OpenScriptsFolder() {
        var folder = ScriptCatalog.ProgramFolder();
        Directory.CreateDirectory(folder);
        OS.ShellShowInFileManager(folder, true);
    }

    //the ids of the element edited and of the element of the selected control point, for the scripts acting on them
    public List<int> GetSelectedElementIds() {
        var res = new List<int>();
        void Add(object element) {
            if (element == null) return;
            var id = Api.CityApi.IdOf(element);
            if (id >= 0 && !res.Contains(id)) res.Add(id);
        }
        Add(GetActivePanel()?.GetUndoTarget());
        if (gizmo.mainTargetRoot != null && GodotObject.IsInstanceValid(gizmo.mainTargetRoot)) {
            Add(History.UndoManager.ResolveOwner(gizmo.mainTargetRoot));
        }
        return res;
    }

    public void Reload() {
        SM.Reload();
        DisableAllPanels(null);
        foreach (var panel in panels) {
            panel.DeferInitialize(this);
        }
        var menuBar = GetMenuBar();
        if (PreferencesManager.workingDirectory == "") {
            for (int i = 1; i < menuBar.GetElementCount() - 3; i++) {
                menuBar.EnableElement(i, false);
            }
            noCity = true;
        }
        else {
            for (int i = 1; i < menuBar.GetElementCount() - 3; i++) {
                menuBar.EnableElement(i, true);
            }
            noCity = false;
        }
        roadPlacer.placementMode = ElementPlacer.RoadPlacer.PlacementMode.None;
        terrainPlacer.placementMode = ElementPlacer.TerrainPlacer.PlacementMode.None;
        buildingPlacer.placementMode = ElementPlacer.BuildingPlacer.PlacementMode.None;
        customElementPlacer.placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.None;
        meshPlacer.placeEnabled = false;
        arrayPointPlacer.Stop();
        meshListEditorPanel.ResetEntries(); //otherwise when opened for the first time it would have broken paths due to prefs not being loaded at first load
        SetCameraRenderingMode(PreferencesManager.Get("renderingMode", 0), false);
        SetShadows(PreferencesManager.Get("shadowsEnabled", true), false);
    }

    public void SetCameraRenderingMode(int mode) {
        SetCameraRenderingMode(mode, true);
    }

    void SetCameraRenderingMode(int mode, bool save) {
        var cam = GameEngine.Camera.Get(this);
        if (cam != null) {
            // Godot has no vertex lit path: the mode is full white ambient with the sun off,
            // which leaves the textures unshaded.
            switch (mode) {
                case 1:
                    GameEngine.Lighting.SetAmbientLight(this, GameEngine.Color.white);
                    EnableLight(false);
                    break;
                default:
                    GameEngine.Lighting.SetAmbientLight(this, new Color(0.23f, 0.23f, 0.23f));
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
        GameEngine.Lighting.SetSunEnabled(this, enabled);
    }

    void SetShadows(bool enabled, bool save) {
        GameEngine.Lighting.SetSunShadows(this, enabled);
        if (save) PreferencesManager.Set("shadowsEnabled", enabled);
    }

    public void SetPropsCullingDistance(string distance) {
        if (!float.TryParse(distance, out var v)) return;
        PreferencesManager.Set("propsCullingDistance", v);
        var cam = GetCameraController();
        if (cam != null) cam.SetPropsCullingDistance(v);
    }

    public void ReloadCameraControls() {
        var cam = GetCameraController();
        if (cam != null) cam.LoadControls();
    }

    public void SetSelectObjectMode() {
        terrainClick.modifier = nullModifier;
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
        if (transformPropertiesEditorPanel != null && transformPropertiesEditorPanel.instance != null && GodotObject.IsInstanceValid(transformPropertiesEditorPanel.instance)) {
            var inst = GS.Cast<MeshInstance>(transformPropertiesEditorPanel.instance);
            if (inst != null) {
                inst.SyncParametricObjectState();
            }
        }
    }

    //the objects moved with the gizmo whose placement rules ask for it go back on the surface (e.g. the points of a decal)
    void SnapTransformedObjects(IReadOnlyList<Node3D> targets) {
        var snapped = false;
        foreach (var target in targets) {
            var inst = GS.Cast<MeshInstance>(target);
            if (inst != null && GodotObject.IsInstanceValid(inst)) snapped |= inst.SnapToSurface();
        }
        if (snapped) {
            NotifyChange();
            transformPropertiesEditorPanel.Refresh();
        }
    }

    public void NotifyVisibilityChange() {
        if (helper != null) {
            if (helper.elementManager != null) {
                helper.elementManager.visibilityChanged = true;
                SyncSelectedEntityMenuItemVisibility();
            }
        }
    }

    void SyncSelectedEntityMenuItemVisibility() {
        var idx = selectedEntityMenuItemIndex;
        var menuBar = GetMenuBar();
        var entitiesSelected = new List<string>();
        var entries = new List<string>();
        var actions = new List<System.Action>();
        foreach (var entry in ArrayObject.activeElements) {
            if (entry.Value != null) {
                entitiesSelected.Add(entry.Key);
                entries.Add(SM.Get("DESELECT_CONTAINER").Replace("$CONTAINER", SM.Get("CUSTOM_ELEMENT_CATEGORY_" + entry.Key.ToUpper())));
                actions.Add(delegate { DeselectEntity(entry.Key); });
            }
        }
        menuBar.SetEntries(idx, entries, actions);
        var suffix = entitiesSelected.Count > 1 ? "PLUR" : "SING";
        menuBar.SetText(idx, SM.Get("CONTAINERS_SELECTED_" + suffix).Replace("$NUMBER", entitiesSelected.Count.ToString()));
        menuBar.ShowElement(idx, entitiesSelected.Count > 0);
    }

    void DeselectEntity(string type) {
        ArrayObject.activeElements[type] = null;
        NotifyVisibilityChange();
    }

    public void NotifyUpdateCompleted() {
        foreach (var panel in panels) {
            if (panel.ActiveSelf()) {
                panel.UpdateConversionButtons();
                panel.Update();
            }
        }
    }

    void ToggleWireframe() {
        var cam = GetCameraController();
        if (cam != null) cam.ToggleWireframe();
    }

    void ShowAllTrafficLanes() {
        helper.elementManager.ShowAllTrafficLanes();
    }

    void ToggleGroundOnly() {
        var cam = GetCameraController();
        if (cam != null) {
            var res = cam.ToggleGroundOnly();
            nullModifier.groundOnly = res;
        }
    }

    void ShowQuickstart() {
        var path = PathHelper.BasePath() + "/Quickstart guide.pdf";
        if (File.Exists(path)) OS.ShellOpen(path);
        else CreateAlert(SM.Get("HELP_QUICKSTART"), SM.Get("HELP_QUICKSTART_NOT_FOUND"), SM.Get("OK"));
    }

    void ShowAbout() {
        var text = SM.Get("HELP_ABOUT_TEXT");
        text = text.Replace("$VERSION", GameEngine.Version.GetVersion()).Replace("$ENGINEVERSION", GameEngine.Version.GetEngineVersion());
        CreateAlert(SM.Get("HELP_ABOUT"), text, SM.Get("OK"), SM.Get("HELP_ABOUT_OPEN_LICENSE"), null, OpenLicense, 220, true);
    }


    void OpenLicense() {
        OS.ShellOpen(PathHelper.BasePath() + "/License.txt");
    }

    void ToggleSettings() {
        ShowPanel(settingsEditorPanel);
    }

    void ToggleProjectSettings() {
        ShowPanel(projectSettingsEditorPanel);
    }

    void ToggleCityProperties() {
        ShowPanel(cityPropertiesEditorPanel);
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
                //closed child panels are terminated too, and their parent with them (e.g. after editing a road's
                //props, every selection terminated the road editor, deleting the preset dummy road and rebuilding the city)
                if (panel.IsOpen()) panel.Terminate();
            }
            else {
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
        helper.SetCirclePosition(Vector3.Zero, 0);
    }

    void ShowPanel(EditorPanel panel, Stack<Node3D> stack = null) {
        DisableAllPanels(panel);
        panel.ShowWithStack(stack);
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
        }
        catch (System.Exception e) {
            GameEngine.Print(e.Message);
            GameEngine.Print(e.StackTrace);
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

    //a dry export with the exporters of the core, listing the warnings and errors they report
    public async void ValidateCity() {
        if (noCity) {
            NoCityAlert();
            return;
        }
        DeselectAndCloseCurrentPanel();
        //the menu entry and the panel buttons run in an undo step, which keeps the history busy: the validation
        //(a long operation of the API) starts once it is closed
        await ToSignal(GetTree(), "process_frame");
        if (noCity) return;
        CityValidator.Result res;
        try {
            res = (CityValidator.Result)await Api.ApiHost.Instance.Api.Validate();
        } catch (Api.ApiException e) {
            CreateAlert(SM.Get("ERROR"), e.code == "not_found" ? SM.Get("VALIDATION_NOT_AVAILABLE") : e.Message, SM.Get("OK"));
            return;
        }
        DisableAllPanels(validationEditorPanel);
        validationEditorPanel.Show(res.items, SM.Get("VALIDATION_TITLE"));
    }

    //the warnings and errors reported by an export
    public void ShowExportReport(List<Newtonsoft.Json.Linq.JObject> items) {
        DisableAllPanels(validationEditorPanel);
        validationEditorPanel.Show(items, SM.Get("EXPORT_REPORT_TITLE"));
    }

    void ExportCityToFile(string filename) {
        try {
            CityExporter.SaveCity(helper.elementManager, filename);
        }
        catch (System.Exception e) {
            GameEngine.Print(e.Message);
            GameEngine.Print(e.StackTrace);
            CreateAlert(SM.Get("ERROR"), SM.Get("CITY_SAVE_ERROR_TEXT") + e.Message, SM.Get("OK"));
        }
    }

    void ToggleTerrainEdit() {
        DisableAllPanels(terrainEditorPanel);
        terrainEditorPanel.Toggle();
        var editEnabled = terrainEditorPanel.ActiveSelf();
        helper.elementManager.ShowIntersections(false);
        if (editEnabled) {
            terrainEditorPanel.ReloadTerrainModifier();
        }
        else {
            SetSelectObjectMode();
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

    public void OpenTextureSelector(Control panel, System.Func<object> valueGetter, System.Action<string> valueSetter, System.Action afterSetAction) {
        textureListEditorPanel.SetActive(true);
        textureListEditorPanel.SetSelection((string)valueGetter.Invoke());
        textureListEditorPanel.Open(panel, valueSetter, afterSetAction);
    }

    public void OpenMeshSelector(EditorPanel panel, Control panelObj, System.Action<string> valueSetter, System.Action afterSetAction, bool disablePlacer = false) {
        if (disablePlacer) meshListEditorPanel.disablePlacer = true;
        if (panel != null) meshListEditorPanel.Open(panel, valueSetter, afterSetAction);
        else if (panelObj != null) meshListEditorPanel.Open(panelObj, valueSetter, afterSetAction);
    }

    void CreateMeshInstance() {
        ShowPanel(meshListEditorPanel);
    }

    void SwitchGrid() {
        helper.SwitchMaterial();
    }

    void ResetWindow() {
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
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
        roadPlacer.SetRoad(null);
        roadPlacer.placementMode = ElementPlacer.RoadPlacer.PlacementMode.Add;
        terrainClick.modifier = roadPlacer;
        roadEditorPanel.SetActive(true, true);
    }

    void CreateTerrain() {
        DisableAllPanels(terrainPatchEditorPanel);
        terrainPlacer.SetTerrainPatch(null);
        terrainPlacer.placementMode = ElementPlacer.TerrainPlacer.PlacementMode.Perimeter;
        terrainClick.modifier = terrainPlacer;
        terrainPatchEditorPanel.SetActive(true);
        helper.elementManager.ShowAnchors(true);
    }

    void CreateBuildingLine() {
        DisableAllPanels(buildingLineEditorPanel);
        buildingPlacer.SetBuildingLine(null);
        buildingPlacer.placementMode = ElementPlacer.BuildingPlacer.PlacementMode.Point;
        terrainClick.modifier = buildingPlacer;
        buildingLineEditorPanel.SetActive(true);
        helper.elementManager.ShowAnchors(true);
    }

    void CreatePropPath() {
        StartCustomElementCreation(PropPath.TypeId);
    }

    void CreateCustomElement() {
        StartCustomElementCreation(null);
    }

    void StartCustomElementCreation(string type, string group = "CREATE_CUSTOM_ELEMENT") {
        customElementEditorPanel.Terminate();
        DisableAllPanels(customElementEditorPanel);
        customElementEditorPanel.creationType = type;
        customElementEditorPanel.creationGroup = group;
        customElementPlacer.createType = type;
        customElementPlacer.SetElement(null);
        customElementPlacer.placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.Create;
        terrainClick.modifier = customElementPlacer;
        customElementEditorPanel.SetActive(true);
    }

    //converts the element with a converter of the core (see Operations.Conversion): the editor is locked while the
    //converter runs, then the first element created is edited
    public async void StartConversion(object element, string converter) {
        if (element == null || conversionRunning) return;
        conversionRunning = true;
        History.UndoManager.Record(SM.Get("CLOSE"), DeselectAndCloseCurrentPanel);
        SetScriptRunning(true);
        helper.curProgressBar.SetActive(true);
        helper.curProgressBar.SetText(SM.Get("CONVERTING"));
        Operations.Conversion.Result res;
        try {
            res = await Operations.Conversion.Run(helper.elementManager, converter, new List<object> { element });
        } catch (System.Exception e) {
            GameEngine.LogError(e.ToString());
            res = Operations.Conversion.Result.Fail("converter_failed", e.Message);
        } finally {
            helper.curProgressBar.SetActive(false);
            SetScriptRunning(false);
            conversionRunning = false;
        }
        if (!res.success) {
            CreateAlert(SM.Get("ERROR"), SM.Get("CONVERSION_ERROR_TEXT") + res.message, SM.Get("OK"));
            return;
        }
        NotifyChange();
        if (res.created.Count > 0) {
            var first = res.created.Find(e => e.container == null) ?? res.created[0];
            History.UndoManager.Record(SM.Get("EDIT"), () => SelectObject(first, true, null));
        } else if (res.updated.Count > 0) {
            //the converter changed existing elements (e.g. the one being edited): it is edited again
            History.UndoManager.Record(SM.Get("EDIT"), () => SelectObject(res.updated[0], true, null));
        }
    }

    bool conversionRunning = false;

    void CreateCityTerrain() {
        ShowPanel(cityTerrainEditorPanel);
    }

    void CreateCityBuildings() {
        ShowPanel(cityBuildingsEditorPanel);
    }

    void Quit() {
        if (WantsToQuit()) GetTree().Quit();
    }

    void ForceQuit() {
        Api.ScriptRunner.Current?.Cancel();
        quitEnabled = true;
        GetTree().Quit();
    }

    public bool AlertActive() {
        return alert != null && !alert.IsClosed();
    }

    bool InputActive() {
        return inputFieldPopup != null && !inputFieldPopup.IsClosed();
    }

    public void CreateAlert(string title, string message, string buttonText,
        System.Action genericAction = null, float height = 180.0f, bool leftAlign = false, float width = 400.0f) {

        if (!AlertActive()) alert = new Alert(this, title, message, buttonText, genericAction, height, leftAlign, width);
    }

    public void CreateAlert(string title, string message, string okText, string cancelText,
        System.Action okAction = null, System.Action cancelAction = null, float height = 180.0f, bool leftAlign = false, float width = 400.0f) {

        if (!AlertActive()) alert = new Alert(this, title, message, okText, cancelText, okAction, cancelAction, height, leftAlign, width);
    }

    public void CreateInput(string title, string placeholder, string buttonText, System.Action<string> genericAction = null) {
        if (!InputActive()) inputFieldPopup = new InputFieldPopup(this, title, placeholder, buttonText, genericAction);
    }

    public void CreateInput(string title, string placeholder, string okText, string cancelText, System.Action<string> okAction = null, System.Action cancelAction = null, string initialText = null) {
        if (!InputActive()) inputFieldPopup = new InputFieldPopup(this, title, placeholder, okText, cancelText, okAction, cancelAction, initialText);
    }

    public bool WantsToQuit() {
        if (quitEnabled || noCity) {
            return true;
        } else {
            CreateAlert(SM.Get("WARNING"), SM.Get("QUIT_WARNING"), SM.Get("YES"), SM.Get("NO"), ForceQuit);
            return false;
        }
    }

    public override void _Notification(int what) {
        if (what == NotificationWMCloseRequest) {
            if (WantsToQuit()) GetTree().Quit();
        }
    }

    public override void _ExitTree() {
        // Free the nodes kept outside the scene tree, otherwise they (and their meshes, materials and textures) leak at exit
        MeshManager.Shutdown();
    }

    public void DoDelayed(System.Action action) {
        DoDelayedCoroutine(action, helper);
    }

    public async void DoDelayedCoroutine(System.Action action, Node caller) {
        await caller.ToSignal(caller.GetTree(), "process_frame");
        action.Invoke();
    }

    /*public void SelectObject(RaycastHit hit) { //TODO: UI
        if (HasActivePanels()) return;
        SelectObject(hit.transform.gameObject, true, hit.collider);
    }*/

    public void SelectObject(Node3D obj) {
        SelectObject(obj, true, null);
    }

    //a parametric object of the city properties (e.g. a checkpoint) is edited from the panels of the elements it belongs to,
    //opened as if it was chosen from the city properties panel (as when the transform panel is reopened by CaptureTransformChildReopen);
    //returns false if the node is not one
    bool SelectCityPropertiesObject(Node3D node) {
        MeshInstance mesh = null;
        var stack = new Stack<Node3D>(); //the array elements containing it, the outermost on top (as in SelectObject)
        for (; node != null; node = node.GetParent() as Node3D) {
            if (GS.Cast<CityProperties>(node) != null) break;
            if (mesh != null) stack.Push(node);
            else mesh = GS.Cast<MeshInstance>(node);
        }
        if (node == null || mesh?.objectParameter == null) return false;
        SelectObject(node, true, null, stack);
        var parent = GetActivePanel();
        if (parent == null || parent == transformPropertiesEditorPanel) return true;
        SetTransformPanelParent(parent);
        parent.Hide(true);
        SelectObject(mesh);
        if (!parent.IsOpen()) parent.Hide(true);
        return true;
    }

    public void DeselectObject() {
        SetSelectObjectMode();
        if (selectedObject != null && !GodotObject.IsInstanceValid(selectedObject)) selectedObject = null;
        if (selectedObject == null) return;
        var road = GS.Cast<Road>(selectedObject);
        var intersection = GS.FindRecursive<Intersection.IntersectionComponent>(selectedObject);
        var patch = GS.Cast<TerrainPatch>(selectedObject);
        var building = GS.Cast<BuildingLine>(selectedObject);
        if (road != null) {
            road.SetActive(false);
        }
        else if (intersection != null) {
            intersection.intersection.SetActive(false);
        }
        else if (patch != null) {
            patch.SetActive(false, false);
        }
        else if (building != null) {
            building.SetActive(false, false);
        }
        selectedObject = null;
        helper.elementManager.ShowAnchors(false);
        helper.elementManager.ShowIntersections(false);
        helper.elementManager.DeselectMeshes();
    }

    public void EditRoadPresets() {
        roadEditorPanel.Terminate();
        DisableAllPanels(roadEditorPanel);
        roadPlacer.SetRoad(null);
        roadEditorPanel.SetActive(true, false);
    }

    public void EditCustomElementPresets(string type) {
        customElementEditorPanel.Terminate();
        DisableAllPanels(customElementEditorPanel);
        customElementPlacer.SetElement(null);
        terrainClick.modifier = nullModifier;
        customElementEditorPanel.creationType = type;
        customElementEditorPanel.SetActive(true);
    }

    public void EditIntersectionPresets() {
        intersectionEditorPanel.Terminate();
        DisableAllPanels(intersectionEditorPanel);
        intersectionEditorPanel.intersection = null;
        intersectionEditorPanel.SetActive(true);
    }

    public void EditBuildingLinePresets() {
        buildingLineEditorPanel.Terminate();
        DisableAllPanels(buildingLineEditorPanel);
        buildingPlacer.SetBuildingLine(null);
        buildingLineEditorPanel.SetActive(true);
    }

    public void ShowCustomExportDialog(FileBrowserHelper.CustomExporter exporter, string name) {
        DisableAllPanels(customExporterPanel);
        customExporterPanel.exporter = exporter;
        customExporterPanel.name = name;
        customExporterPanel.SetActive(true);
    }

    void ReselectObjectPanelAndModifier(Node3D obj, bool withEditor, EditorPanel panel, TerrainAction modifier) {
        DeselectObject();
        selectedObject = obj;
        if (withEditor) {
            DisableAllPanels(panel);
            terrainClick.modifier = modifier;
        }
    }

    public void SelectObject(Node3D obj, bool withEditor, CollisionShape3D c, Stack<Node3D> stack = null) {
        if (obj == null || !GodotObject.IsInstanceValid(obj)) return;
        transformPropertiesEditorPanel.Select(false);
        var cityProperties = GS.Cast<CityProperties>(obj);
        var road = GS.Cast<Road>(obj);
        var intersection = GS.FindRecursive<Intersection.IntersectionComponent>(obj);
        var patch = GS.Cast<TerrainPatch>(obj);
        var mesh = GS.Cast<MeshInstance>(obj);
        var buildingLine = GS.Cast<BuildingLine>(obj);
        var buildingSide = (buildingLine != null && c != null) ? buildingLine.GetColliderSide(c) : null;
        var building = (buildingLine != null && c != null) ? buildingLine.GetColliderBuilding(c) : null;
        var selector = GS.Cast<GenericSelector>(obj);
        var customElement = GS.Cast<CustomElement>(obj);
        if (customElement != null && customElement.IsDeleted()) customElement = null;
        if (road != null && road.ghost) road = null;
        if (intersection != null && intersection.intersection.ghost) intersection = null;
        if (customElement != null) {
            ReselectObjectPanelAndModifier(obj, withEditor, customElementEditorPanel, customElementPlacer);
            if (withEditor) {
                customElementPlacer.SetElement(customElement);
                customElementPlacer.placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.None;
                customElementEditorPanel.ShowWithStack(stack);
            } else {
                customElement.SetActive(true, true);
                customElement.Select(true);
            }
        }
        else if (cityProperties != null) {
            DeselectAndCloseCurrentPanel();
            ShowPanel(cityPropertiesEditorPanel, stack);
            //todo: go to the correct tab if there is more than one
        }
        else if (road != null) {
            ReselectObjectPanelAndModifier(obj, withEditor, roadEditorPanel, roadPlacer);
            if (withEditor) roadPlacer.SetRoad(road);
            helper.elementManager.ShowIntersections(false);
            road.SetActive(true);
            if (withEditor) roadEditorPanel.ShowWithStack(stack);
        }
        else if (intersection != null && GO.GetParent(GO.GetParent(intersection)) == obj) {
            ReselectObjectPanelAndModifier(obj, withEditor, intersectionEditorPanel, null); //todo: make work wirh nullModifier (or rework altogether)
            helper.elementManager.ShowIntersections(false);
            intersectionEditorPanel.intersection = intersection.intersection;
            intersection.intersection.SetActive(true, true, true);
            if (withEditor) intersectionEditorPanel.ShowWithStack(stack);
        }
        else if (patch != null) {
            ReselectObjectPanelAndModifier(obj, withEditor, terrainPatchEditorPanel, terrainPlacer);
            if (withEditor) terrainPlacer.SetTerrainPatch(patch);
            helper.elementManager.ShowAnchors(true);
            patch.SetActive(true, true);
            patch.Select(true);
            if (withEditor) terrainPatchEditorPanel.ShowWithStack(stack);
        }
        else if (mesh != null) {
            ReselectObjectPanelAndModifier(obj, withEditor, transformPropertiesEditorPanel, nullModifier);
            if (withEditor) {
                transformPropertiesEditorPanel.instance = mesh;
                transformPropertiesEditorPanel.SetActive(true);
            }
            else {
                transformPropertiesEditorPanel.Select(false);
                transformPropertiesEditorPanel.instance = mesh;
                transformPropertiesEditorPanel.Select(true);
            }
        }
        else if (buildingSide != null) {
            buildingLine = buildingSide.building.line;
            ReselectObjectPanelAndModifier(obj, withEditor, buildingLineEditorPanel, buildingPlacer);
            if (withEditor) buildingPlacer.SetBuildingLine(buildingLine);
            helper.elementManager.ShowAnchors(true);
            buildingLine.SetActive(true, true);
            buildingLine.Select(true);
            if (withEditor) {
                buildingLineEditorPanel.SetActive(true);
                if (Input.GetInputPressed(KeyCommand.Modifier1)) {
                    //do nothing => edit the line
                }
                else if (Input.GetInputPressed(KeyCommand.Modifier2)) {
                    buildingLineEditorPanel.EditBuilding(buildingSide.building);
                }
                else {
                    buildingLineEditorPanel.EditSide(buildingSide);
                }
            }
        }
        else if (building != null) {
            buildingLine = building.line;
            ReselectObjectPanelAndModifier(obj, withEditor, buildingLineEditorPanel, buildingPlacer);
            if (withEditor) buildingPlacer.SetBuildingLine(buildingLine);
            helper.elementManager.ShowAnchors(true);
            buildingLine.SetActive(true, true);
            buildingLine.Select(true);
            if (withEditor) {
                if (Input.GetInputPressed(KeyCommand.Modifier1)) {
                    //edit the line
                    buildingLineEditorPanel.ShowWithStack(stack);
                }
                else {
                    buildingLineEditorPanel.SetActive(true);
                    buildingLineEditorPanel.EditBuilding(building);
                }
            }
        }
        else if (buildingLine != null) {
            ReselectObjectPanelAndModifier(obj, withEditor, buildingLineEditorPanel, buildingPlacer);
            if (withEditor) buildingPlacer.SetBuildingLine(buildingLine);
            helper.elementManager.ShowAnchors(true);
            buildingLine.SetActive(true, true);
            buildingLine.Select(true);
            if (withEditor) buildingLineEditorPanel.ShowWithStack(stack);
        }
        else if (selector != null) {
            switch (selector.type) {
                case "Building":
                    buildingLine = ((Building)selector.parent).line;
                    ReselectObjectPanelAndModifier(obj, withEditor, buildingLineEditorPanel, buildingPlacer);
                    if (withEditor) buildingPlacer.SetBuildingLine(buildingLine);
                    helper.elementManager.ShowAnchors(true);
                    buildingLine.SetActive(true, true);
                    buildingLine.Select(true);
                    if (withEditor) {
                        if (Input.GetInputPressed(KeyCommand.Modifier1)) {
                            //edit the line
                            buildingLineEditorPanel.ShowWithStack(stack);
                        }
                        else {
                            buildingLineEditorPanel.SetActive(true);
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
                    if (withEditor) buildingLineEditorPanel.ShowWithStack(stack);
                    break;
                default:
                    break;
            }
        }
        else if (obj.GetParent() != null) {
            if (stack == null) {
                stack = new Stack<GameObject>();
            }
            stack.Push(obj);
            SelectObject(obj.GetParent() as Node3D, withEditor, c, stack); //not GetParentNode3D, null for a top level node (e.g. a line)
        }
    }

    void DeleteCurObj() {
        History.UndoManager.Begin(SM.Get("DELETE"));
        try {
            History.UndoManager.Touch(GetActivePanel()?.GetUndoTarget());
            DeleteCurObjUnrecorded();
        } finally {
            History.UndoManager.Commit();
        }
    }

    void DeleteCurObjUnrecorded() {
        var setSelectMode = true; //set to false if no objects deleted (i.e. nothing or control points only)
        if (roadEditorPanel.ActiveSelf()) {
            setSelectMode = roadEditorPanel.Delete(true);
        }
        else if (terrainPatchEditorPanel.ActiveSelf()) {
            if (gizmo.mainTargetRoot != null) {
                var point = GS.FindRecursive<TerrainPoint>(gizmo.mainTargetRoot);
                if (point != null) {
                    if (Operations.TerrainPatches.RemovePoint(terrainPatchEditorPanel.GetPatch(), point)) {
                        gizmo.RemoveTarget(gizmo.mainTargetRoot);
                        point.Select(false);
                        NotifyChange();
                    }
                }
                setSelectMode = false;
            }
            else {
                terrainPatchEditorPanel.Delete();
            }
        }
        else if (transformPropertiesEditorPanel.ActiveSelf()) {
            //control points and generated parametric objects (e.g. traffic lights) are not deleted from here,
            //array elements are but the panel goes back to the parent element, whose modifier must be kept
            var instance = GS.Cast<MeshInstance>(transformPropertiesEditorPanel.instance);
            var canDelete = transformPropertiesEditorPanel.CanDelete();
            setSelectMode = canDelete && instance.objectParameter == null;
            if (canDelete) transformPropertiesEditorPanel.Delete();
        }
        else if (intersectionEditorPanel.ActiveSelf()) {
            setSelectMode = intersectionEditorPanel.CanDelete();
            if (setSelectMode) intersectionEditorPanel.Delete();
        }
        else if (customElementEditorPanel.ActiveSelf()) {
            var element = customElementEditorPanel.GetElement();
            if (element == null) {
                setSelectMode = false;
            } else if (gizmo.mainTargetRoot != null) {
                var point = GS.FindRecursive<TerrainPoint>(gizmo.mainTargetRoot);
                if (point != null && Operations.CustomElements.RemovePoint(element, point)) {
                    gizmo.RemoveTarget(gizmo.mainTargetRoot);
                    point.Select(false);
                    NotifyChange();
                }
                setSelectMode = false;
            } else {
                customElementEditorPanel.Delete();
            }
        }
        else if (buildingLineEditorPanel.ActiveSelf()) {
            if (gizmo.mainTargetRoot != null) {
                var point = GS.FindRecursive<TerrainPoint>(gizmo.mainTargetRoot);
                if (point != null) {
                    if (Operations.BuildingLines.RemovePoint(buildingLineEditorPanel.GetLine(), point)) {
                        gizmo.RemoveTarget(gizmo.mainTargetRoot);
                        point.Select(false);
                        NotifyChange();
                    }
                }
                setSelectMode = false;
            }
            else {
                buildingLineEditorPanel.Delete();
            }
        }
        else {
            setSelectMode = false;
        }
        if (setSelectMode) SetSelectObjectMode();
    }

    public Node3D GetCurSelectedObject() {
        return gizmo.mainTargetRoot;
    }

    //moves the camera to show the selection: the selected control point or mesh, else the edited element, else the highlighted object
    public void ZoomToSelection() {
        if (noCity) return;
        object target = null;
        if (gizmo.mainTargetRoot != null && GodotObject.IsInstanceValid(gizmo.mainTargetRoot)) target = gizmo.mainTargetRoot;
        if (target == null) {
            target = GetActivePanel()?.GetUndoTarget();
            if (target != null && !History.ElementSnapshot.IsAlive(target)) target = null;
        }
        if (target == null && selectedObject != null && GodotObject.IsInstanceValid(selectedObject)) target = selectedObject;
        var bounds = target != null ? Api.CityApi.BoundsOf(target) : null;
        if (bounds == null) return;
        GetCameraController()?.ZoomIn(bounds.Value.GetCenter(), Mathf.Max(bounds.Value.Size.Length(), 10));
    }

    void EditCurSelectedObjectTransform() {
        var obj = GetCurSelectedObject();
        if (obj == null || transformPropertiesEditorPanel.ActiveSelf()) return;
        var roadEnd = GetEditedRoadEnd(obj);
        if (roadEnd != null) {
            gizmo.ClearTargets();
            gizmo.AddTarget(roadEnd);
            obj = roadEnd;
        }
        var ownerPanel = GS.Cast<Handle>(obj) != null ? GetActivePanel() : null;
        if (ownerPanel != null) {
            //control point: hide the editor of its element instead of closing it,
            //closing it deactivated the element and made the point invisible while being edited
            ownerPanel.Hide(true);
            SetTransformPanelParent(ownerPanel);
            controlPointOwnerModifier = terrainClick.modifier;
            controlPointOwnerTransformType = gizmo.transformType;
            terrainClick.modifier = null; //no point placement or object selection while the transform panel is open
        }
        else {
            DisableAllPanels(transformPropertiesEditorPanel);
        }
        transformPropertiesEditorPanel.instance = obj;
        transformPropertiesEditorPanel.SetActive(true);
    }

    //the end of the road being edited on the intersection whose point is given (null if it isn't one): it is edited
    //instead of the intersection point, and the intersection follows it
    Node3D GetEditedRoadEnd(Node3D obj) {
        var component = GS.FindRecursive<Intersection.IntersectionComponent>(obj);
        if (component == null || GO.GetParent(component) != obj) return null;
        var road = (GetActivePanel() as EditorPanels.RoadEditorPanel)?.GetUndoTarget() as Road;
        if (road == null || road.points.Count == 0) return null;
        if (road.startIntersection == component.intersection) return road.points[0];
        if (road.endIntersection == component.intersection) return road.points[road.points.Count - 1];
        return null;
    }

    //the custom element whose editor the transform panel was opened from (on one of its points), null otherwise
    public CustomElement ControlPointOwnerCustomElement => (controlPointOwnerModifier as ElementPlacer.CustomElementPlacer)?.GetElement();

    //called by the transform panel before returning to (or terminating) the editor owning the control point
    public void RestoreControlPointOwner() {
        terrainClick.modifier = controlPointOwnerModifier;
        controlPointOwnerModifier = null;
    }

    public void ReselectControlPoint(Node3D point) {
        gizmo.ClearTargets();
        gizmo.AddTarget(point);
        //an attachment edited in the transform panel may have changed whether the point can move
        var terrainPoint = GS.FindRecursive<TerrainPoint>(point);
        gizmo.transformType = terrainPoint == null ? controlPointOwnerTransformType
            : GO.GetLayer(terrainPoint.GetObject()) == 10 ? RuntimeGizmos.TransformType.None : RuntimeGizmos.TransformType.Move;
    }

    EditorPanel GetActivePanel() {
        foreach (var panel in panels) {
            var activePanel = GetActivePanel(panel);
            if (activePanel != null) return activePanel;
        }
        return null;
    }

    static EditorPanel GetActivePanel(EditorPanel panel) {
        foreach (var childPanel in panel.childPanels) {
            var activePanel = GetActivePanel(childPanel);
            if (activePanel != null) return activePanel;
        }
        return panel.ActiveSelf() ? panel : null;
    }

    void DeselectAndCloseCurrentPanel() {
        DisableAllPanels(null);
        DeselectObject();
    }

    public void SetTransformPanelParent(EditorPanel newParent) {
        transformPropertiesEditorPanel.parentPanel = newParent;
    }

    bool HasActivePanels() {
        //counts the active children of the canvas besides the menu bar, i.e. the open editor panels
        foreach (var panel in panels) {
            if (IsPanelActive(panel)) return true;
        }
        return false;
    }

    static bool IsPanelActive(EditorPanel panel) {
        if (panel.ActiveSelf()) return true;
        foreach (var childPanel in panel.childPanels) {
            if (IsPanelActive(childPanel)) return true;
        }
        return false;
    }

    public override void _Process(double delta) {
        if (Api.ScriptRunner.IsRunning) return; //the editor is locked while a script runs
        if (Input.GetInputStart(KeyCommand.Close)) {
            //Escape ends a picker first, keeping its panel open
            if (referencePicker.IsActive()) referencePicker.Stop();
            else if (!transformPropertiesEditorPanel.CancelAttachment()) History.UndoManager.Record(SM.Get("CLOSE"), DeselectAndCloseCurrentPanel);
        }
        else if (Input.GetInputStart(KeyCommand.CreateRoad)) {
            History.UndoManager.Record(SM.Get("CREATE_ROAD"), CreateRoad);
        }
        else if (Input.GetInputStart(KeyCommand.CreateTerrainPatch)) {
            History.UndoManager.Record(SM.Get("CREATE_TERRAIN_PATCH"), CreateTerrain);
        }
        else if (Input.GetInputStart(KeyCommand.CreateBuildingLine)) {
            History.UndoManager.Record(SM.Get("CREATE_BUILDING_LINE"), CreateBuildingLine);
        }
        else if (Input.GetInputStart(KeyCommand.SaveCity)) {
            SaveCity();
        }
        else if (!TextInputFocused()) {
            if (Input.GetInputStart(KeyCommand.Undo)) {
                Undo();
            }
            else if (Input.GetInputStart(KeyCommand.Redo)) {
                Redo();
            }
            else if (Input.GetInputStart(KeyCommand.Delete)) {
                DeleteCurObj();
            }
            else if (Input.GetInputStart(KeyCommand.Edit)) {
                EditCurSelectedObjectTransform();
            }
            else if (Input.GetInputStart(KeyCommand.ZoomToSelection)) {
                ZoomToSelection();
            }
        }
    }

    //Godot keeps buttons focused after clicking them, so only text fields should block the shortcuts
    bool TextInputFocused() {
        var focused = GetViewport().GuiGetFocusOwner();
        return focused is LineEdit || focused is TextEdit;
    }
}

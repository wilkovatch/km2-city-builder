using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using GO = GameEngine.GameObject;

//Development tool that takes the screenshots of the quickstart guide, run with "-- --guide-screenshots=<city folder>" on the
//command line (use a copy of a city, it is closed at the end without saving). Opens the editor panels on the city and saves
//the images in wiki/images, then quits. The last opened city setting is restored at the end.
public partial class GuideScreenshots : Node {
    CityBuilderMenuBar builder;
    string folder;
    string outFolder;
    ElementManager M => builder.helper.elementManager;

    public static void StartIfRequested(CityBuilderMenuBar builder) {
        foreach (var arg in OS.GetCmdlineUserArgs()) {
            if (arg.StartsWith("--guide-screenshots=")) {
                builder.AddChild(new GuideScreenshots { builder = builder, folder = arg.Substring("--guide-screenshots=".Length), Name = "GuideScreenshots" });
            }
        }
    }

    public override void _Ready() {
        outFolder = Path.Combine(ProjectSettings.GlobalizePath("res://"), "wiki", "images");
        Directory.CreateDirectory(outFolder);
        Run();
    }

    static object Field(object obj, string name) {
        for (var type = obj.GetType(); type != null; type = type.BaseType) {
            var field = type.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field != null) return field.GetValue(obj);
        }
        throw new System.MissingFieldException(name);
    }

    void Call(string method, params object[] args) {
        foreach (var m in typeof(CityBuilderMenuBar).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
            if (m.Name == method && m.GetParameters().Length == args.Length) { m.Invoke(builder, args); return; }
        }
        throw new System.MissingMethodException(method);
    }

    T Panel<T>(string name) where T : EditorPanel {
        return (T)Field(builder, name);
    }

    CameraController Camera() {
        return (CameraController)GetViewport().GetCamera3D();
    }

    //looks at the point from the given distance, with the given pitch (degrees below the horizon) and yaw
    void LookAt(Vector3 target, float distance, float pitch = 50, float yaw = 30) {
        var cam = Camera();
        var p = Mathf.DegToRad(pitch);
        var y = Mathf.DegToRad(yaw);
        var dir = new Vector3(Mathf.Cos(p) * Mathf.Sin(y), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Cos(y));
        cam.Position = target + dir * distance;
        cam.LookAt(target, Vector3.Up);
        cam.GetType().GetField("curRotation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(cam, cam.RotationDegrees);
    }

    //frames an area of the given size, centered in the part of the screen not covered by the editor panel on the right
    void Frame((Vector3 center, float size) bounds, float pitch = 50, float yaw = 20, float margin = 1.2f, bool panel = true) {
        var cam = Camera();
        var viewSize = GetViewport().GetVisibleRect().Size;
        var halfHeight = Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
        var halfWidth = halfHeight * viewSize.X / viewSize.Y;
        var visibleWidth = panel ? (viewSize.X - 260) / viewSize.X : 1.0f;
        var distance = bounds.size * margin * 0.5f / Mathf.Min(halfHeight, halfWidth * visibleWidth);
        LookAt(bounds.center, distance, pitch, yaw);
        if (panel) cam.Position += cam.GlobalBasis.X * (distance * halfWidth * (1 - visibleWidth));
    }

    //the bounds of the visible meshes of an element
    static (Vector3 center, float size) NodeBounds(Node node) {
        var points = new List<Vector3>();
        var nodes = new List<Node>(node.FindChildren("*", nameof(MeshInstance3D), true, false));
        if (node is MeshInstance3D) nodes.Add(node);
        foreach (var child in nodes) {
            var mi = (MeshInstance3D)child;
            if (!mi.IsVisibleInTree() || mi.Mesh == null) continue;
            var aabb = mi.GlobalTransform * mi.GetAabb();
            points.Add(aabb.Position);
            points.Add(aabb.End);
        }
        return Bounds(points);
    }

    static (Vector3 center, float size) Bounds(IEnumerable<Vector3> points) {
        var first = true;
        var aabb = new Aabb();
        foreach (var point in points) {
            if (first) aabb = new Aabb(point, Vector3.Zero); else aabb = aabb.Expand(point);
            first = false;
        }
        return (aabb.GetCenter(), Mathf.Max(aabb.Size.X, aabb.Size.Z));
    }

    //the rectangle of the editor panel shown, including its tab buttons
    Rect2 PanelRect(EditorPanel panel) {
        var pages = (System.Collections.IList)Field(panel, "pages");
        Rect2? res = null;
        foreach (EditorPanelPage page in pages) {
            if (!page.ActiveSelf()) continue;
            if (page.panel is not Control control || !control.IsVisibleInTree()) continue;
            var rect = control.GetGlobalRect();
            res = res == null ? rect : res.Value.Merge(rect);
        }
        return res ?? new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size);
    }

    async Task Shot(string name, Rect2? crop = null) {
        await Frames(10);
        var image = GetViewport().GetTexture().GetImage();
        if (crop != null) {
            var r = crop.Value.Intersection(new Rect2(0, 0, image.GetWidth(), image.GetHeight()));
            image = image.GetRegion(new Rect2I((Vector2I)r.Position.Floor(), (Vector2I)r.Size.Ceil()));
        }
        //the panels are saved as PNG, the full window (mostly 3D view) as JPG to keep the files small
        if (crop != null) image.SavePng(Path.Combine(outFolder, name + ".png"));
        else image.SaveJpg(Path.Combine(outFolder, name + ".jpg"), 0.85f);
        GD.Print("[GuideScreenshots] saved " + name);
    }

    async Task Frames(int n) {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), "process_frame");
    }

    void Close() => Call("DeselectAndCloseCurrentPanel");

    async void Run() {
        var lastCity = SettingsManager.Get("LastCity", "");
        try {
            await Frames(30);
            PreferencesManager.workingDirectory = folder;
            CoreManager.Reset();
            CityImporter.LoadCity(M, folder + "/city.json.gz");
            var waited = 0;
            while ((History.UndoManager.IsLoading || M.GetDummy<CityProperties>() == null || M.roads.Count == 0) && waited < 3000) {
                await Frames(1);
                waited++;
            }
            await Frames(60);
            History.UndoManager.Clear();
            GD.Print("[GuideScreenshots] city loaded: " + M.roads.Count + " roads, " + M.patches.Count + " patches, " + M.buildings.Count + " building lines, " + M.meshes.Count + " meshes");

            //the whole city
            var allPoints = new List<Vector3>();
            foreach (var road in M.roads) foreach (var point in road.points) allPoints.Add(GO.GetPosition(point));
            var city = Bounds(allPoints);
            Frame(city, 55, 20, 1.0f, false);
            await Shot("editor-overview");

            //the edit menu
            MenuButton editMenu = null;
            foreach (var node in builder.FindChildren("*", nameof(MenuButton), true, false)) {
                if (((MenuButton)node).Text == StringManager.Get("EDIT")) editMenu = (MenuButton)node;
            }
            editMenu.ShowPopup();
            var popup = editMenu.GetPopup();
            await Frames(5);
            popup.SetFocusedItem(-1);
            await Frames(2);
            var menuRect = new Rect2(Vector2.Zero, popup.Position + popup.Size + new Vector2I(16, 16));
            await Shot("menu-edit", menuRect);
            popup.Hide();

            //a road, with its control points
            //(the one with the most points among those not spanning most of the city)
            Road shownRoad = null;
            foreach (var road in M.roads) {
                var size = NodeBounds(road).size;
                if (size > city.size * 0.6f || size < 50) continue;
                if (shownRoad == null || road.points.Count > shownRoad.points.Count) shownRoad = road;
            }
            shownRoad ??= M.roads[0];
            Frame(NodeBounds(shownRoad), 60, 20, 1.1f);
            builder.SelectObject(shownRoad, true, null);
            await Shot("road-editor");
            var roadPanelRect = PanelRect(Panel<EditorPanel>("roadEditorPanel"));
            roadPanelRect.Size = new Vector2(roadPanelRect.Size.X, 460); //the tabs, the preset and the placement mode
            await Shot("road-editor-panel", roadPanelRect);
            Close();

            //an intersection
            if (M.intersections.Count > 0) {
                var intersection = M.intersections[0];
                Frame((GO.GetPosition(intersection.point), 70), 55, 20, 1.0f);
                builder.SelectObject(intersection.geo, true, null);
                await Shot("intersection-editor");
                Close();
            }

            //a terrain patch, with the anchors
            if (M.patches.Count > 0) {
                //the largest one not spanning most of the city and with no buildings on it
                var buildingAreas = new List<Aabb>();
                foreach (var l in M.buildings) {
                    var b = NodeBounds(l);
                    buildingAreas.Add(new Aabb(b.center - new Vector3(b.size, 1000, b.size) * 0.5f, new Vector3(b.size, 2000, b.size)));
                }
                TerrainPatch patch = null;
                var patchSize = 0.0f;
                foreach (var p in M.patches) {
                    var b = Bounds(p.GetPerimeterPoints());
                    if (b.size > city.size * 0.6f || b.size <= patchSize) continue;
                    var covered = false;
                    foreach (var area in buildingAreas) if (area.HasPoint(b.center)) covered = true;
                    if (covered) continue;
                    patch = p;
                    patchSize = b.size;
                }
                patch ??= M.patches[0];
                builder.SelectObject(patch, true, null);
                await Frames(5);
                Frame(Bounds(patch.GetPerimeterPoints()), 60, 20, 1.3f);
                await Shot("terrain-patch-editor");
                await Shot("terrain-patch-editor-panel", PanelRect(Panel<EditorPanel>("terrainPatchEditorPanel")));
                Close();
            }

            //a building line, a building side and its floors
            BuildingLine line = null;
            foreach (var l in M.buildings) if (l.buildings.Count > 0) { line = l; break; }
            if (line != null) {
                var linePanel = Panel<EditorPanels.Buildings.LineEditorPanel>("buildingLineEditorPanel");
                Frame(NodeBounds(line), 45, 20, 1.1f);
                builder.SelectObject(line, true, null);
                await Shot("building-line-editor");
                await Shot("building-line-editor-panel", PanelRect(linePanel));
                var side = linePanel.GetBuildingSide(0, "front");
                if (side != null) {
                    Close();
                    builder.SelectObject(line, true, null);
                    linePanel.EditSide(side);
                    await Frames(5);
                    var sidePanel = (EditorPanel)builder.GetType().GetMethod("FindShownPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null, System.Type.EmptyTypes, null).Invoke(builder, null);
                    await Shot("building-side-editor-panel", PanelRect(sidePanel));
                }
                Close();
            }

            Frame(city, 55, 20, 1.0f);

            //mesh instance placement
            Call("CreateMeshInstance");
            await Shot("mesh-list-panel", PanelRect(Panel<EditorPanel>("meshListEditorPanel")));
            Close();

            //terrain editing
            Call("ToggleTerrainEdit");
            await Shot("terrain-edit-panel", PanelRect(Panel<EditorPanel>("terrainEditorPanel")));
            Call("ToggleTerrainEdit");

            //project settings
            Call("ToggleProjectSettings");
            await Shot("project-settings-panel", PanelRect(Panel<EditorPanel>("projectSettingsEditorPanel")));
            Close();

            //preferences
            Call("ToggleSettings");
            await Shot("preferences-cores-panel", PanelRect(Panel<EditorPanel>("settingsEditorPanel")));
            Close();

            //no city loaded: the new city panel is shown
            Call("CloseCity");
            await Frames(30);
            Call("NewCity");
            await Shot("new-city-panel", PanelRect(Panel<EditorPanel>("newCityEditorPanel")));
        } catch (System.Exception e) {
            GD.PrintErr("[GuideScreenshots] " + e);
        }
        SettingsManager.Set("LastCity", lastCity);
        GetTree().Quit();
    }
}

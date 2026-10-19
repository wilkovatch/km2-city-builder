using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Godot;
using GO = GameEngine.GameObject;

namespace History {
    //Development self-test of undo/redo, run with "-- selftest undo <new folder>" on the command line. Creates a city in the
    //folder, edits it through the element and editor code, then undoes and redoes every step checking the whole city against
    //the states recorded after each step. Writes undo_selftest.log next to the folder and quits (exit code 1 on failures).
    //Nothing is saved, the last opened city setting is restored at the end.
    public partial class UndoSelfTest : Node {
        public CityBuilderMenuBar builder;
        string folder;
        ElementManager M => builder.helper.elementManager;
        readonly List<string> log = new List<string>();
        int failures = 0;

        bool loadExisting = false;

        //new: a new city in the folder; props: a copy of an existing city, its road props selected again; ui: a copy of an
        //existing city, the editors open before undo/redo must be reopened after it; split: only the tests of the roads
        //connected to the middle of other roads, on a new city; load: a copy of an existing city, the terrain under its
        //intersections is edited, undone and redone
        public static readonly string[] Modes = { "new", "props", "ui", "split", "load" };

        public static void Start(CityBuilderMenuBar builder, string folder, string mode) {
            builder.AddChild(new UndoSelfTest {
                builder = builder, folder = folder, Name = "UndoSelfTest",
                propsSelection = mode == "props", uiRestore = mode == "ui", splitOnly = mode == "split", loadExisting = mode == "load"
            });
        }

        bool propsSelection = false;
        bool splitOnly = false;

        static object Invoke(object obj, string name, params object[] args) {
            for (var type = obj.GetType(); type != null; type = type.BaseType) {
                foreach (var m in type.GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)) {
                    if (m.Name == name && m.GetParameters().Length == args.Length) return m.Invoke(obj, args);
                }
            }
            throw new System.MissingMethodException(name);
        }

        static object Field(object obj, string name) {
            return obj.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).GetValue(obj);
        }

        static Control FieldControl(EditorPanelElement element) {
            return (Control)typeof(EditorPanelElement).GetField("fieldControl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(element);
        }

        Godot.Button VisibleButton(string text) {
            foreach (var node in builder.FindChildren("*", nameof(Godot.Button), true, false)) {
                var button = (Godot.Button)node;
                if (button.Text == text && button.IsVisibleInTree() && !button.Disabled) return button;
            }
            return null;
        }

        static int UndoCount() {
            var stack = (System.Collections.IList)typeof(UndoManager).GetField("undoStack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            return stack.Count;
        }

        void LogNewEntries(int from) {
            var stack = (List<HistoryEntry>)typeof(UndoManager).GetField("undoStack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            for (int i = from; i < stack.Count; i++) {
                Log("  entry '" + stack[i].label + "'");
                foreach (var change in stack[i].elements.Values) {
                    var before = change.before?.Json ?? "(none)";
                    var after = change.after?.Json ?? "(none)";
                    Log("    element " + change.id + " (" + change.kind + ")" + Diff(before, after));
                }
            }
        }

        //a road whose prop line was edited must not record an undo step when it is selected again
        async void RunPropsSelection() {
            var lastCity = SettingsManager.Get("LastCity", "");
            try {
                await Frames(30);
                PreferencesManager.workingDirectory = folder;
                CoreManager.Reset();
                CityImporter.LoadCity(M, folder + "/city.json.gz");
                var waited = 0;
                while ((UndoManager.IsLoading || M.GetDummy<CityProperties>() == null || M.roads.Count == 0) && waited < 3000) {
                    await Frames(1);
                    waited++;
                }
                await Frames(30);
                UndoManager.Clear();
                var road = ElementRegistry.Get<Road>(10);
                Log("road 10: " + (road == null ? "missing" : GO.GetName(road)));
                //the clicks in the viewport select through the null modifier, inside the TerrainClick undo step
                var stroke = 0;
                async Task Select() {
                    stroke++;
                    UndoManager.Record("Null", () => builder.SelectObject(road, true, null), (nameof(TerrainClick), stroke));
                    await Frames(6);
                }
                async Task Press(string what, Godot.Button button) {
                    Log("press " + what + (button == null ? " (NOT FOUND)" : ""));
                    if (button == null) { failures++; return; }
                    button.EmitSignal(BaseButton.SignalName.Pressed);
                    await Frames(6);
                }
                await Select();
                Log("after first selection: " + UndoCount() + " entries");
                var roadPanel = Field(builder, "roadEditorPanel");
                var propControls = (System.Collections.IDictionary)Field(roadPanel, "propControls");
                var controls = ((EditorPanelElements.Button button, EditorPanelElements.Checkbox checkbox))propControls["right"];
                var count = UndoCount();
                ((CheckBox)FieldControl(controls.checkbox)).ButtonPressed = true;
                await Frames(6);
                Log("enabled: " + (UndoCount() - count) + " new entries");
                await Press("Edit right", (Godot.Button)FieldControl(controls.button));
                count = UndoCount();
                await Press("Add", VisibleButton(StringManager.Get("ADD")));
                Log("added: " + (UndoCount() - count) + " new entries");
                await Press("Close", VisibleButton(StringManager.Get("CLOSE")));
                Log("rule: " + Newtonsoft.Json.JsonConvert.SerializeObject(road.state.State("propRule")));
                bool ShowsPreset(string preset) {
                    foreach (var node in builder.FindChildren("*", nameof(OptionButton), true, false)) {
                        var dropdown = (OptionButton)node;
                        if (dropdown.IsVisibleInTree() && dropdown.Selected > 0 && dropdown.GetItemText(dropdown.Selected) == preset) return true;
                    }
                    return false;
                }
                for (int i = 0; i < 3; i++) {
                    count = UndoCount();
                    var dummy = M.GetDummy<Road>();
                    await Select();
                    Check("selection " + (i + 1) + " records nothing (" + (UndoCount() - count) + " new entries)", UndoCount() == count);
                    LogNewEntries(count);
                    Check("selection " + (i + 1) + " keeps the preset dummy road", M.GetDummy<Road>() == dummy);
                    Check("selection " + (i + 1) + " shows the road preset '" + road.state.Name + "'", ShowsPreset(road.state.Name));
                    count = UndoCount();
                    await Press("End editing", VisibleButton(StringManager.Get("END_EDITING")));
                    Check("closing " + (i + 1) + " records nothing", UndoCount() == count);
                    LogNewEntries(count);
                }
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            SettingsManager.Set("LastCity", lastCity);
            Log("finished with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "undo_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }

        bool uiRestore = false;

        T CallBuilder<T>(string method, params object[] args) {
            foreach (var m in typeof(CityBuilderMenuBar).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
                if (m.Name == method && m.GetParameters().Length == args.Length) return (T)m.Invoke(builder, args);
            }
            throw new System.MissingMethodException(method);
        }

        T Panel<T>(string name) where T : EditorPanel {
            return (T)Field(builder, name);
        }

        EditorPanel Shown() {
            return CallBuilder<EditorPanel>("FindShownPanel");
        }

        static int RedoCount() {
            var stack = (System.Collections.IList)typeof(UndoManager).GetField("redoStack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            return stack.Count;
        }

        static string PanelName(EditorPanel panel) {
            return panel == null ? "nothing" : panel.GetType().Name;
        }

        //makes an edit (recorded like the editor does), then checks after undo and after redo that the UI is as before them
        async Task CheckRestore(string what, System.Action edit, System.Func<string> uiState) {
            await Frames(3);
            var expected = uiState();
            UndoManager.Record("edit " + what, edit);
            M.FlagAsChanged();
            await Frames(6);
            var beforeUndo = uiState();
            foreach (var op in new[] { "Undo", "Redo" }) {
                var undoCount = UndoCount();
                var redoCount = RedoCount();
                var expectedNow = op == "Undo" ? expected : beforeUndo;
                InvokeBuilder(op);
                var immediately = uiState();
                var undoAfter = UndoCount();
                var redoAfter = RedoCount();
                await Frames(6);
                var actual = uiState();
                Check(what + ": " + op.ToLower() + " restores the UI (" + expectedNow + ")", actual == expectedNow && immediately == expectedNow);
                if (actual != expectedNow) Log("      after " + op.ToLower() + ": " + actual);
                else if (immediately != expectedNow) Log("      right after " + op.ToLower() + ": " + immediately);
                var recordsNothing = UndoCount() == undoAfter && RedoCount() == redoAfter
                    && undoAfter == undoCount + (op == "Undo" ? -1 : 1) && redoAfter == redoCount + (op == "Undo" ? 1 : -1);
                Check(what + ": " + op.ToLower() + " restore records nothing", recordsNothing);
                if (!recordsNothing) Log("      undo/redo entries " + undoCount + "/" + redoCount + " -> " + undoAfter + "/" + redoAfter + " -> " + UndoCount() + "/" + RedoCount());
            }
            InvokeBuilder("Undo"); //leave the city as it was
            await Frames(6);
        }

        async void RunUIRestore() {
            var lastCity = SettingsManager.Get("LastCity", "");
            try {
                await Frames(30);
                PreferencesManager.workingDirectory = folder;
                CoreManager.Reset();
                CityImporter.LoadCity(M, folder + "/city.json.gz");
                var waited = 0;
                while ((UndoManager.IsLoading || M.GetDummy<CityProperties>() == null || M.roads.Count == 0) && waited < 3000) {
                    await Frames(1);
                    waited++;
                }
                await Frames(30);
                UndoManager.Clear();
                Log("city loaded: " + M.roads.Count + " roads, " + M.intersections.Count + " intersections, " + M.patches.Count + " patches, "
                    + M.buildings.Count + " building lines, " + M.meshes.Count + " meshes");
                var roadPanel = Panel<EditorPanels.RoadEditorPanel>("roadEditorPanel");
                var transformPanel = Panel<EditorPanels.TransformPropertiesEditorPanel>("transformPropertiesEditorPanel");
                var gizmo = builder.gizmo;
                int Id(object e) => ElementRegistry.GetId(e);
                string Target(EditorPanel p) => p == null ? "-" : Id(p.GetUndoTarget()).ToString();
                string Chain() {
                    var res = new List<string>();
                    for (var p = Shown(); p != null; p = p.parentPanel) {
                        res.Insert(0, PanelName(p) + "[" + Target(p) + ",p" + p.GetShownPage() + "]");
                        if (p.parentPanel != null && !p.parentPanel.IsOpen() && !p.parentPanel.childPanels.Contains(p)) break;
                    }
                    return res.Count == 0 ? "nothing" : string.Join(" > ", res);
                }
                void Close() => CallBuilder<object>("DeselectAndCloseCurrentPanel");
                Road road = null;
                foreach (var r in M.roads) if (ElementSnapshot.IsAlive(r) && (road == null || r.points.Count > road.points.Count)) road = r;
                var roadId = Id(road);

                //road editor, on its second tab
                builder.SelectObject(road, true, null);
                Log("     road " + roadId + " selected: road panel active " + roadPanel.ActiveSelf() + ", modifier " + builder.terrainClick.modifier?.GetType().Name + ", shown " + PanelName(Shown()));
                roadPanel.ShowPage(1);
                await CheckRestore("road editor (tab 2)", () => { UndoManager.Touch(road); FieldStatus.SetValue(road, "7", "state.properties.segments"); }, Chain);
                Close();

                //road editor adding points
                builder.SelectObject(road, true, null);
                roadPanel.SetPointPlacementMode((int)ElementPlacer.RoadPlacer.PlacementMode.Add);
                var roadPlacer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
                await CheckRestore("road editor in add mode", () => { UndoManager.Touch(road); FieldStatus.SetValue(roadPlacer.GetRoad(), "6", "state.properties.segments"); },
                    () => Chain() + " mode " + ((builder.terrainClick.modifier as ElementPlacer.RoadPlacer)?.placementMode.ToString() ?? "-"));
                Close();

                //road editor with a control point selected
                builder.SelectObject(road, true, null);
                gizmo.ClearTargets();
                gizmo.AddTarget(road.points[1]);
                string SelectedPoint() {
                    var cur = ElementRegistry.Get<Road>(roadId);
                    var sel = gizmo.mainTargetRoot;
                    return Chain() + " point " + (sel == null ? "none" : cur != null ? cur.points.IndexOf(sel as StaticBody3D).ToString() : "?");
                }
                await CheckRestore("road editor with point 1 selected", () => {
                    UndoManager.TouchOwner(road.points[1]);
                    GO.SetPosition(road.points[1], GO.GetPosition(road.points[1]) + new Vector3(0, 0, 3));
                }, SelectedPoint);
                Close();

                //transform panel on a control point of the road
                builder.SelectObject(road, true, null);
                gizmo.ClearTargets();
                gizmo.AddTarget(road.points[1]);
                CallBuilder<object>("EditCurSelectedObjectTransform");
                Check("transform panel opened on the road point", Shown() == transformPanel && transformPanel.parentPanel == roadPanel);
                await CheckRestore("transform panel on a road point", () => {
                    UndoManager.TouchOwner(road.points[1]);
                    GO.SetPosition(road.points[1], GO.GetPosition(road.points[1]) + new Vector3(0, 0, 3));
                }, SelectedPoint);
                Close();

                //intersection editor
                Intersection intersection = null;
                foreach (var it in M.intersections) if (ElementSnapshot.IsAlive(it)) { intersection = it; break; }
                if (intersection != null) {
                    builder.SelectObject(intersection.geo, true, null);
                    await CheckRestore("intersection editor", () => { UndoManager.Touch(intersection); FieldStatus.SetValue(intersection, "1.5", "state.properties.sizeIncrease"); }, Chain);
                    Close();
                }

                //transform panel on a parametric object (e.g. a traffic light) of a road, regenerated by the undo
                Road roadWithObjects = null;
                foreach (var r in M.roads) if (ElementSnapshot.IsAlive(r) && r.objectInstances.Count > 0) { roadWithObjects = r; break; }
                if (roadWithObjects != null) {
                    var objId = Id(roadWithObjects);
                    string key = null;
                    foreach (var k in roadWithObjects.objectInstances.Keys) { key = k; break; }
                    var obj = GameEngine.Scriptable.FindRecursive<MeshInstance>(roadWithObjects.objectInstances[key]);
                    builder.SelectObject(obj, true, null);
                    string ParametricState() {
                        var cur = ElementRegistry.Get<Road>(objId);
                        var expected = cur != null && cur.objectInstances.TryGetValue(key, out var holder) ? GameEngine.Scriptable.FindRecursive<MeshInstance>(holder) : null;
                        return PanelName(Shown()) + (transformPanel.instance != null && transformPanel.instance == expected ? " on " + key : " not on " + key);
                    }
                    //moved like from the transform panel
                    await CheckRestore("transform panel on the parametric object " + key, () => {
                        var cur = transformPanel.instance;
                        UndoManager.TouchOwner(cur);
                        GO.SetPosition(cur, GO.GetPosition(cur) + new Vector3(1, 0, 0));
                        GameEngine.Scriptable.Cast<MeshInstance>(cur).SyncParametricObjectState();
                    }, ParametricState);
                    Close();
                } else {
                    Log("     no road with parametric objects");
                }

                //array/custom element panels (city properties, then the road)
                foreach (var (owner, panelField) in new (object, string)[] { (M.GetDummy<CityProperties>(), "cityPropertiesEditorPanel"), (road, "roadEditorPanel") }) {
                    var panel = Panel<EditorPanel>(panelField);
                    if (owner is Road) builder.SelectObject(road, true, null); else CallBuilder<object>("ToggleCityProperties");
                    var parameterMap = (System.Collections.IDictionary)typeof(EditorPanel).GetField("parameterMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(panel);
                    var stateOwner = (IObjectWithState)owner;
                    EditorPanel opened = null;
                    foreach (CityElements.Types.Parameter param in parameterMap.Values) {
                        if (param.type == "array" && stateOwner.GetState().Array<ObjectState>(param.fullName())?.Length > 0) opened = panel.OpenArrayElement(param.fullName(), 0);
                        else if (param.type == "customElement" && stateOwner.GetState().State(param.fullName()) != null) opened = panel.OpenArrayElement(param.fullName(), null);
                        if (opened != null && Shown() == opened) {
                            Log("     array element " + param.fullName() + " of " + panelField);
                            break;
                        }
                        opened = null;
                    }
                    if (opened != null) {
                        await CheckRestore("array element panel of " + panelField, () => {
                            UndoManager.Touch(owner);
                            stateOwner.GetState().SetStr("undoUiTest", "x");
                            stateOwner.GetState().FlagAsChanged();
                        }, Chain);
                    } else {
                        Log("     no array element to open in " + panelField);
                    }
                    Close();
                }

                //props container of the road (enabled if needed) and one of its elements
                builder.SelectObject(road, true, null);
                string container = null;
                foreach (var k in ((System.Collections.IDictionary)Field(roadPanel, "propControls")).Keys) { container = (string)k; break; }
                if (container != null) {
                    if (road.state.State("propRule")?.State(container) == null) {
                        UndoManager.Record("enable props", () => { UndoManager.Touch(road); Invoke(roadPanel, "EnablePropContainer", container, true); });
                        M.FlagAsChanged();
                        await Frames(6);
                    }
                    var containerPanel = roadPanel.OpenPropContainer(container) as EditorPanels.Props.ContainerEditorPanel;
                    void EditRoad() { UndoManager.Touch(road); FieldStatus.SetValue(road, "8", "state.properties.segments"); }
                    if (containerPanel != null) {
                        await CheckRestore("props container " + container, EditRoad, Chain);
                        //the first list of elements of the container type, with an element added if empty
                        var tS = Invoke(containerPanel, "TS");
                        var containerType = (CityElements.Types.PropsContainerType)((System.Collections.IDictionary)Field(tS, "types"))[(string)Field(tS, "curType")];
                        string list = null;
                        foreach (var param in containerType.parameters) if (param.maxNumber != 1) { list = param.name; break; }
                        if (list != null && !(containerPanel.GetContainer().Array<ObjectState>(list)?.Length > 0)) {
                            UndoManager.Record("add prop", () => { UndoManager.Touch(road); Invoke(containerPanel, "AddElem", list); });
                            M.FlagAsChanged();
                            await Frames(6);
                        }
                        if (list != null && containerPanel.OpenElem(list, 0) != null) await CheckRestore("prop element of " + list, EditRoad, Chain);
                        else Log("     no prop element to open");
                    }
                } else {
                    Log("     no props container for this road type");
                }
                Close();

                //terrain patch editor and border mesh
                TerrainPatch patch = null;
                foreach (var p in M.patches) if (ElementSnapshot.IsAlive(p)) { patch = p; if (p.GetBorderMeshCount() > 0) break; }
                if (patch != null) {
                    var patchPanel = Panel<EditorPanels.TerrainPatchEditorPanel>("terrainPatchEditorPanel");
                    builder.SelectObject(patch, true, null);
                    await CheckRestore("terrain patch editor", () => { UndoManager.Touch(patch); FieldStatus.SetValue(patch, "2", "state.properties.smooth"); }, Chain);
                    if (patch.GetBorderMeshCount() == 0) {
                        UndoManager.Record("add border mesh", () => { UndoManager.Touch(patch); Invoke(patchPanel, "AddBorderMesh"); });
                        M.FlagAsChanged();
                        await Frames(6);
                    }
                    if (patch.GetBorderMeshCount() > 0 && patchPanel.OpenBorderMesh(0) != null) {
                        await CheckRestore("border mesh panel", () => { UndoManager.Touch(patch); FieldStatus.SetValue(patch, "3", "state.properties.smooth"); }, Chain);
                    } else {
                        Log("     no border mesh to open");
                    }
                    Close();
                }

                //building line chain
                BuildingLine line = null;
                foreach (var l in M.buildings) if (ElementSnapshot.IsAlive(l) && l.buildings.Count > 0) { line = l; break; }
                if (line != null) {
                    var linePanel = Panel<EditorPanels.Buildings.LineEditorPanel>("buildingLineEditorPanel");
                    builder.SelectObject(line, true, null);
                    void Rename() { UndoManager.Touch(line); GO.SetName(line, GO.GetName(line) + "x"); }
                    await CheckRestore("building line editor", Rename, Chain);
                    var buildingPanel = linePanel.OpenBuilding(0);
                    if (buildingPanel != null && Shown() == buildingPanel) await CheckRestore("building editor", Rename, Chain);
                    var side = linePanel.GetBuildingSide(0, "front");
                    if (side != null) {
                        //as when clicking the side in the viewport
                        Close();
                        builder.SelectObject(line, true, null);
                        linePanel.EditSide(side);
                        await CheckRestore("building side editor", Rename, Chain);
                        if (Shown() is EditorPanels.Buildings.SideEditorPanel sidePanel) {
                            if (sidePanel.OpenBlock(0) != null) await CheckRestore("building block editor", Rename, Chain);
                            Close();
                            builder.SelectObject(line, true, null);
                            linePanel.EditSide(side);
                            var floors = side.state.Array<ObjectState>("upperFloors");
                            sidePanel = Shown() as EditorPanels.Buildings.SideEditorPanel;
                            var floorPanel = sidePanel == null ? null : floors?.Length > 0 ? sidePanel.OpenFloor(false, floors.Length - 1) : sidePanel.OpenFloor(true, 0);
                            if (floorPanel != null) await CheckRestore("building floor editor", Rename, Chain);
                            else Log("     no floor to open");
                        }
                    }
                    Close();
                } else {
                    Log("     no building line");
                }

                //a standalone panel
                CallBuilder<object>("ToggleSettings");
                await CheckRestore("settings panel", () => { UndoManager.Touch(road); FieldStatus.SetValue(road, "9", "state.properties.segments"); }, Chain);
                Close();

                //the editor of an element the undo deletes is not reopened
                Road newRoad = null;
                UndoManager.Record("create road", () => {
                    var placer = new ElementPlacer.RoadPlacer(M);
                    newRoad = placer.GetRoad();
                    var p0 = GO.GetPosition(road.points[0]);
                    newRoad.AddPoint(p0 + new Vector3(30, 0, 30));
                    newRoad.AddPoint(p0 + new Vector3(60, 0, 30));
                });
                M.FlagAsChanged();
                await Frames(6);
                builder.SelectObject(newRoad, true, null);
                await Frames(3);
                InvokeBuilder("Undo");
                await Frames(6);
                Check("undo of the creation of the edited road leaves the editor closed (" + Chain() + ")", Shown() == null && !ElementSnapshot.IsAlive(newRoad));
                InvokeBuilder("Redo");
                await Frames(6);
                Check("redo of it with no editor open opens nothing (" + Chain() + ")", Shown() == null);
                InvokeBuilder("Undo");
                await Frames(6);
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            SettingsManager.Set("LastCity", lastCity);
            Log("finished with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "undo_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }

        public override void _Ready() {
            if (uiRestore) RunUIRestore();
            else if (propsSelection) RunPropsSelection();
            else if (loadExisting) RunOnExistingCity();
            else if (splitOnly) RunSplit();
            else Run();
        }

        async void RunOnExistingCity() {
            var lastCity = SettingsManager.Get("LastCity", "");
            try {
                await Frames(30);
                PreferencesManager.workingDirectory = folder;
                CoreManager.Reset();
                CityImporter.LoadCity(M, folder + "/city.json.gz");
                var waited = 0;
                while ((UndoManager.IsLoading || M.GetDummy<CityProperties>() == null || M.roads.Count == 0) && waited < 3000) {
                    await Frames(1);
                    waited++;
                }
                await Frames(30);
                Log("city loaded after " + waited + " frames");
                UndoManager.Clear();
                var intersections = new List<Intersection>();
                foreach (var it in M.intersections) if (ElementSnapshot.IsAlive(it)) intersections.Add(it);
                var stroke = 0;
                //the first pass is not checked: the points anchored to the intersections keep their saved positions until
                //their anchors move, and the saved positions can differ slightly from the regenerated geometry
                for (int pass = 0; pass < 2; pass++) {
                    var check = pass == 1;
                    foreach (var intersection in intersections) {
                        var before = Snapshot();
                        var y0 = GO.GetPosition(intersection.point).Y;
                        stroke++;
                        //a brush stroke over several frames through the terrain modifier, like TerrainClick does
                        TerrainModifier.TerrainModifier modifier = (stroke % 2 == 1)
                            ? new TerrainModifier.RaiseFlat(builder.helper, 6, 30, false)
                            : new TerrainModifier.Level(builder.helper, 6, 1, false, true, 3);
                        var hitPoint = GeometryHelper.ProjectPoint(GO.GetPosition(intersection.point) + new Vector3(2.3f, 0, 1.7f));
                        Godot.Input.ActionPress(GameEngine.Input.KeyCommand.Select.ToString());
                        for (int f = 0; f < 20; f++) {
                            var hits = new List<GameEngine.Raycast.RaycastHit?> { new GameEngine.Raycast.RaycastHit { point = hitPoint } };
                            UndoManager.Record(modifier.GetType().Name, () => modifier.Apply(hits), ("stroke", stroke));
                            await Frames(1);
                        }
                        Godot.Input.ActionRelease(GameEngine.Input.KeyCommand.Select.ToString());
                        await Frames(6);
                        var edited = Snapshot();
                        var yEdited = GO.GetPosition(intersection.point).Y;
                        InvokeBuilder("Undo");
                        await Frames(6);
                        var yUndone = GO.GetPosition(intersection.point).Y;
                        var id = ElementRegistry.GetId(intersection);
                        if (check) Log((Compare("undo terrain under intersection " + id, before, Snapshot()) ? "OK   " : "FAIL ") + "undo terrain under intersection " + id);
                        InvokeBuilder("Redo");
                        await Frames(6);
                        var yRedone = GO.GetPosition(intersection.point).Y;
                        if (check) {
                            Log((Compare("redo terrain under intersection " + id, edited, Snapshot()) ? "OK   " : "FAIL ") + "redo terrain under intersection " + id);
                            Log("  intersection " + id + ": y " + y0 + " -> edited " + yEdited + ", undone " + yUndone + ", redone " + yRedone);
                        }
                        InvokeBuilder("Undo");
                        await Frames(6);
                    }
                }

                //the parametric objects (e.g. traffic lights, whose model comes from the city properties) of a deleted road
                //must be restored as they were
                string ParametricObjects(Road road) {
                    var res = new List<string>();
                    foreach (var obj in road.objectInstances) {
                        res.Add(obj.Key + "=" + GameEngine.Scriptable.FindRecursive<MeshInstance>(obj.Value)?.meshPath);
                    }
                    res.Sort();
                    return string.Join(", ", res);
                }
                var roads = new List<Road>();
                foreach (var r in M.roads) if (ElementSnapshot.IsAlive(r)) roads.Add(r);
                foreach (var road in roads) {
                    var id = ElementRegistry.GetId(road);
                    var before = ParametricObjects(road);
                    UndoManager.Record("delete road", () => road.Delete());
                    M.FlagAsChanged();
                    await Frames(6);
                    InvokeBuilder("Undo");
                    await Frames(6);
                    var restored = ElementRegistry.Get<Road>(id);
                    var after = restored == null ? "(not restored)" : ParametricObjects(restored);
                    Check("undo road " + id + " delete restores its parametric objects (" + before + ")", before == after);
                    if (before != after) Log("      after: " + after);
                }

                //the whole city erase operations (run from an alert, outside the undo step of the panel button)
                foreach (var (name, erase) in new (string, System.Action)[] { ("CB_ERASE", M.EraseCityBuildings), ("CT_ERASE", M.EraseCityTerrain) }) {
                    if ((name == "CB_ERASE" ? M.buildings.Count : M.patches.Count) == 0) {
                        Log("     " + name + ": nothing to erase in this city");
                        continue;
                    }
                    var before = Snapshot();
                    UndoManager.Record(name, erase);
                    M.FlagAsChanged();
                    await Frames(6);
                    var erased = Snapshot();
                    InvokeBuilder("Undo");
                    await Frames(6);
                    Log((Compare("undo " + name, before, Snapshot()) ? "OK   " : "FAIL ") + "undo " + name);
                    InvokeBuilder("Redo");
                    await Frames(6);
                    Log((Compare("redo " + name, erased, Snapshot()) ? "OK   " : "FAIL ") + "redo " + name);
                    Log("  " + name + ": " + before.Count + " elements -> " + erased.Count);
                }

                //the undo and redo entries of the edit menu are disabled when there is nothing to undo/redo
                MenuButton editMenu = null;
                foreach (var button in builder.FindChildren("*", nameof(MenuButton), true, false)) {
                    if (((MenuButton)button).Text == StringManager.Get("EDIT")) editMenu = (MenuButton)button;
                }
                var popup = editMenu.GetPopup();
                async Task CheckEditMenu(string what, bool canUndo, bool canRedo) {
                    editMenu.ShowPopup();
                    await Frames(3);
                    Check("edit menu " + what + ": undo " + (canUndo ? "enabled" : "disabled") + ", redo " + (canRedo ? "enabled" : "disabled"),
                        popup.IsItemDisabled(0) == !canUndo && popup.IsItemDisabled(1) == !canRedo);
                    popup.Hide();
                    await Frames(2);
                }
                Check("edit menu shows the shortcuts", popup.GetItemAccelerator(0) == (Key)((long)Key.Z | (long)KeyModifierMask.MaskCtrl) && popup.GetItemAccelerator(1) == (Key)((long)Key.Y | (long)KeyModifierMask.MaskCtrl));
                await CheckEditMenu("after the last step", true, false);
                while (UndoManager.CanUndo) InvokeBuilder("Undo");
                await Frames(6);
                await CheckEditMenu("with nothing to undo", false, true);
                editMenu.ShowPopup();
                await Frames(3);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, "..", "undo_selftest_menu.png"));
                popup.Hide();
                while (UndoManager.CanRedo) InvokeBuilder("Redo");
                await Frames(6);
                await CheckEditMenu("with nothing to redo", true, false);
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            SettingsManager.Set("LastCity", lastCity);
            Log("finished with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "undo_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }

        async Task Frames(int n) {
            for (int i = 0; i < n; i++) await ToSignal(GetTree(), "process_frame");
        }

        void Log(string s) {
            GD.Print("[UndoSelfTest] " + s);
            log.Add(s);
        }

        Dictionary<int, string> Snapshot() {
            var res = new Dictionary<int, string>();
            void Add(object e) {
                if (e == null || !ElementSnapshot.IsAlive(e)) return;
                var kind = ElementSnapshot.GetKind(e);
                if (!kind.HasValue) return;
                var id = ElementRegistry.GetId(e);
                if (id < 0) {
                    Log("  unregistered element: " + kind.Value);
                    failures++;
                    return;
                }
                var json = ElementSnapshot.Capture(e, kind.Value, id);
                res[id] = kind.Value + " " + json;
            }
            Add(M.GetDummy<CityProperties>());
            foreach (var e in M.meshes) Add(e);
            foreach (var e in M.roads) Add(e);
            foreach (var e in M.intersections) Add(e);
            foreach (var e in M.GetTerrainPoints()) Add(e);
            foreach (var e in M.patches) Add(e);
            foreach (var e in M.buildings) Add(e);
            var bytes = GameEngine.Terrain.GetHeightBytes(builder.helper.terrainObj);
            res[-1] = "heights " + System.Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(bytes));
            return res;
        }

        static string Diff(string a, string b) {
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            int start = System.Math.Max(0, i - 60);
            string Part(string s) => s.Substring(start, System.Math.Min(200, s.Length - start));
            return "\n      expected: ..." + Part(a) + "\n      actual:   ..." + Part(b);
        }

        static readonly System.Text.RegularExpressions.Regex number = new System.Text.RegularExpressions.Regex(@"-?\d+(\.\d+)?(E-?\d+)?");

        //the numbers are compared with a tolerance: the projection on the ground moves the points by a few 1e-5 at every update
        static bool Same(string a, string b) {
            if (a == b) return true;
            if (number.Replace(a, "#") != number.Replace(b, "#")) return false;
            var na = number.Matches(a);
            var nb = number.Matches(b);
            for (int i = 0; i < na.Count; i++) {
                var x = double.Parse(na[i].Value, System.Globalization.CultureInfo.InvariantCulture);
                var y = double.Parse(nb[i].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (System.Math.Abs(x - y) > 0.01) return false;
            }
            return true;
        }

        bool Compare(string what, Dictionary<int, string> expected, Dictionary<int, string> actual) {
            var ok = true;
            foreach (var kv in expected) {
                if (!actual.TryGetValue(kv.Key, out var a)) {
                    Log("  " + what + ": missing element " + kv.Key + " " + kv.Value.Substring(0, System.Math.Min(80, kv.Value.Length)));
                    ok = false;
                } else if (!Same(kv.Value, a)) {
                    Log("  " + what + ": element " + kv.Key + " differs" + Diff(kv.Value, a));
                    ok = false;
                }
            }
            foreach (var kv in actual) {
                if (!expected.ContainsKey(kv.Key)) {
                    Log("  " + what + ": unexpected element " + kv.Key + " " + kv.Value.Substring(0, System.Math.Min(80, kv.Value.Length)));
                    ok = false;
                }
            }
            if (!ok) failures++;
            return ok;
        }

        void CreateCity(int terrainSize = 512) {
            Directory.CreateDirectory(folder);
            var templatesPath = Path.Combine(PathHelper.BasePath(), "Files", "cores", "MidtownMadness2", "templates");
            PathHelper.DirectoryCopy(Path.Combine(templatesPath, "_common"), folder);
            PathHelper.DirectoryCopy(Path.Combine(templatesPath, "SF"), folder);
            var savedCity = new IO.SavedCity.SavedCity();
            savedCity.heightmapResolution = 512;
            savedCity.terrainSize = terrainSize;
            savedCity.heightMap = "";
            savedCity.maxHeight = 1000;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(savedCity, Newtonsoft.Json.Formatting.Indented);
            var bytes = System.Text.Encoding.ASCII.GetBytes(json);
            using (var compressedFileStream = File.Create(Path.Combine(folder, "city.json.gz"))) {
                using (var compressor = new GZipStream(compressedFileStream, CompressionMode.Compress)) {
                    compressor.Write(bytes, 0, bytes.Length);
                }
            }
            PreferencesManager.workingDirectory = folder;
            CoreManager.Reset();
            CityImporter.LoadCity(M, folder + "/city.json.gz");
        }

        void Check(string what, bool ok) {
            Log((ok ? "OK   " : "FAIL ") + what);
            if (!ok) failures++;
        }

        void InvokeBuilder(string method) {
            typeof(CityBuilderMenuBar).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(builder, null);
        }

        int AliveRoads() {
            var n = 0;
            foreach (var r in M.roads) if (ElementSnapshot.IsAlive(r)) n++;
            return n;
        }

        Road LastRoad() {
            Road res = null;
            foreach (var r in M.roads) if (ElementSnapshot.IsAlive(r)) res = r;
            return res;
        }

        //the flows of the editor: the road editor creating the road, clicks adding points, undo from the menu bar
        async Task MenuLevelTests() {
            UndoManager.Clear();
            await Frames(6);
            var roads0 = AliveRoads();
            UndoManager.Record("create road", () => InvokeBuilder("CreateRoad"));
            await Frames(6);
            Check("menu: the road editor creates a road", AliveRoads() == roads0 + 1);
            var placer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
            UndoManager.Record("click", () => placer.GetRoad().AddPoint(new Vector3(0, 0, 200)), ("click", 1));
            await Frames(6);
            UndoManager.Record("click", () => placer.GetRoad().AddPoint(new Vector3(50, 0, 200)), ("click", 2));
            await Frames(6);
            UndoManager.Record("click", () => placer.GetRoad().AddPoint(new Vector3(100, 0, 200)), ("click", 3));
            await Frames(6);
            Check("menu: three points placed", LastRoad()?.points.Count == 3);

            InvokeBuilder("Undo");
            await Frames(6);
            Check("menu: undo removes the last point", AliveRoads() == roads0 + 1 && LastRoad().points.Count == 2);
            InvokeBuilder("Undo");
            await Frames(6);
            Check("menu: undo of an incomplete road removes it entirely", AliveRoads() == roads0);
            InvokeBuilder("Redo");
            await Frames(6);
            Check("menu: redo restores the road with two points", AliveRoads() == roads0 + 1 && LastRoad().points.Count == 2);
            InvokeBuilder("Redo");
            await Frames(6);
            Check("menu: redo restores the third point", AliveRoads() == roads0 + 1 && LastRoad().points.Count == 3);

            //a road left with a single point in the editor: undo closes the editor, which deletes it, and that is the undo
            UndoManager.Record("create road", () => InvokeBuilder("CreateRoad"));
            await Frames(6);
            placer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
            UndoManager.Record("click", () => placer.GetRoad().AddPoint(new Vector3(0, 0, 300)), ("click", 4));
            await Frames(6);
            Check("menu: second road started", AliveRoads() == roads0 + 2);
            InvokeBuilder("Undo");
            await Frames(6);
            Check("menu: undo with a one point road in the editor removes only that road", AliveRoads() == roads0 + 1 && LastRoad().points.Count == 3);
            InvokeBuilder("Undo");
            await Frames(6);
            Check("menu: next undo removes the first road's last point", AliveRoads() == roads0 + 1 && LastRoad().points.Count == 2);

            //coalescing: the steps with the same key are a single entry
            var road = LastRoad();
            var orig = GO.GetPosition(road.points[0]);
            for (int i = 1; i <= 5; i++) {
                var k = i;
                UndoManager.Record("drag", () => { UndoManager.Touch(road); GO.SetPosition(road.points[0], orig + new Vector3(k, 0, 0)); }, ("slider", 1));
                await Frames(2);
            }
            InvokeBuilder("Undo");
            await Frames(6);
            road = LastRoad();
            Check("coalesced steps are undone at once", road != null && GO.GetPosition(road.points[0]).DistanceTo(orig) < 0.01f);
        }

        static List<Vector3> ControlPoints(Road road) {
            var res = new List<Vector3>();
            foreach (var p in road.points) res.Add(GO.GetPosition(p));
            return res;
        }

        //the curve of the road sampled densely from its control points, horizontally (the test roads are projected on the
        //terrain, so the heights of the curve between them don't matter)
        static List<Vector3> DenseCurve(Road road) {
            var res = DenseCurve3D(road);
            for (int i = 0; i < res.Count; i++) res[i] = new Vector3(res[i].X, 0, res[i].Z);
            return res;
        }

        static List<Vector3> DenseCurve3D(Road road) {
            var cps = ControlPoints(road);
            var res = new List<Vector3>();
            if (cps.Count < 2) return res;
            var type = road.GetCurveType();
            if (type == GeometryHelper.CurveType.LowPoly) {
                for (int i = 0; i < cps.Count - 1; i++) {
                    for (int j = 0; j < 20; j++) res.Add(cps[i].Lerp(cps[i + 1], j / 20.0f));
                }
                res.Add(cps[cps.Count - 1]);
                return res;
            }
            var mid = cps.GetRange(1, cps.Count - 2);
            var tension = road.state.Float("hermiteTension", 0.5f);
            var subdivideEqually = road.state.Bool("subdivideEqually");
            for (int i = 0; i <= 400; i++) {
                res.Add(GeometryHelper.GetPointOnCurve(cps[0], mid, cps[cps.Count - 1], i / 400.0f, i, type, tension, subdivideEqually));
            }
            return res;
        }

        //the control points of the road (not the ends) inside the intersection, where the road mesh starts only at the
        //intersection size from its center
        static int ControlPointsInsideIntersection(Road road, Intersection intersection) {
            var idx = intersection == null ? -1 : intersection.roads.IndexOf(road);
            if (idx < 0 || idx >= intersection.sizes.Count) return 0;
            var center = GO.GetPosition(intersection.point);
            var res = 0;
            for (int i = 1; i < road.points.Count - 1; i++) {
                var d = GO.GetPosition(road.points[i]) - center;
                if (new Vector2(d.X, d.Z).Length() < intersection.sizes[idx]) res++;
            }
            return res;
        }

        static float DistanceToCurves(Vector3 point, List<List<Vector3>> curves) {
            var res = float.MaxValue;
            foreach (var curve in curves) res = Mathf.Min(res, point.DistanceTo(GeometryHelper.ClosestPointToCurve(point, curve, out _)));
            return res;
        }

        //the largest distance between the parts of a split road and the original curve, both ways, outside the
        //intersections at the ends of the parts (where the road is not shown)
        static float SplitDeviation(List<Vector3> original, List<Road> parts, bool bothWays = true) {
            var hidden = new List<(Vector3 center, float radius)>();
            foreach (var part in parts) {
                foreach (var intersection in new[] { part.startIntersection, part.endIntersection }) {
                    var idx = intersection == null ? -1 : intersection.roads.IndexOf(part);
                    if (idx >= 0 && idx < intersection.sizes.Count) hidden.Add((GO.GetPosition(intersection.point), intersection.sizes[idx]));
                }
            }
            bool IsHidden(Vector3 p) {
                foreach (var (center, radius) in hidden) {
                    if (new Vector2(p.X - center.X, p.Z - center.Z).Length() < radius) return true;
                }
                return false;
            }
            var partCurves = new List<List<Vector3>>();
            foreach (var part in parts) partCurves.Add(DenseCurve(part));
            var originalCurves = new List<List<Vector3>> { original };
            var res = 0.0f;
            foreach (var curve in partCurves) {
                foreach (var p in curve) {
                    if (!IsHidden(p)) res = Mathf.Max(res, DistanceToCurves(p, originalCurves));
                }
            }
            if (bothWays) {
                foreach (var p in original) {
                    if (!IsHidden(p)) res = Mathf.Max(res, DistanceToCurves(p, partCurves));
                }
            }
            return res;
        }

        //connecting roads to the middle of other roads (for each curve type), which are split there
        async Task SplitTests() {
            UndoManager.Clear();
            await Frames(6);
            var curveTypes = new List<(string name, GeometryHelper.CurveType type, bool subdivideEqually)> {
                ("Bézier", GeometryHelper.CurveType.Bezier, false),
                ("Hermite", GeometryHelper.CurveType.Hermite, false),
                ("Hermite subdivided equally", GeometryHelper.CurveType.Hermite, true),
                ("low-poly", GeometryHelper.CurveType.LowPoly, false),
                ("straight Hermite", GeometryHelper.CurveType.Hermite, false)
            };
            var steps = new List<(string name, System.Action action, System.Action check)>();
            for (int k = 0; k < curveTypes.Count; k++) {
                var kind = k;
                var (typeName, curveType, subdivideEqually) = curveTypes[k];
                //arches 600 m wide (the default road type is 40 m wide), the connected roads meet them at wide angles
                var x = k == 4 ? 0.0f : k % 2 == 0 ? -450.0f : 450.0f;
                var z = k == 4 ? -300.0f : k < 2 ? -700.0f : 100.0f;
                Road road = null, other = null;
                List<Vector3> original = null;
                var roadsBefore = new HashSet<Road>();
                TerrainPoint anchoredPoint = null;
                var anchoredPosition = Vector3.Zero;
                Vector3 originalEnd = Vector3.Zero;
                Vector3 MiddlePoint(int curveIndex) => road.splitLine.Resolve(road.curvePoints[curveIndex]).Value.point;
                List<Road> Parts() {
                    var res = new List<Road> { road };
                    foreach (var r in M.roads) {
                        if (ElementSnapshot.IsAlive(r) && !roadsBefore.Contains(r) && r != other) res.Add(r);
                    }
                    return res;
                }

                steps.Add(("split: create " + typeName + " road", () => {
                    road = new ElementPlacer.RoadPlacer(M).GetRoad();
                    road.AddPoint(new Vector3(x - 300, 0, z));
                    if (kind == 4) {
                        //a straight road on bumpy terrain: the split point is projected on it, off the straight line between the ends
                        var terrain = builder.helper.terrainObj;
                        var pixelSize = GameEngine.Terrain.GetPixelSize(terrain);
                        var half = GameEngine.Terrain.GetSize(terrain) * 0.5f;
                        int x0 = (int)((x - 320 + half) / pixelSize), y0 = (int)((z - 40 + half) / pixelSize);
                        int w = (int)(640 / pixelSize), h = (int)(80 / pixelSize);
                        UndoManager.TouchHeights(x0, y0, w, h);
                        var heights = GameEngine.Terrain.GetHeights(terrain, x0, y0, w, h);
                        for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) heights[j, i] += 0.01f * (1.0f + Mathf.Sin(i * 0.15f));
                        GameEngine.Terrain.SetHeightsDelayLOD(terrain, x0, y0, heights);
                        GameEngine.Terrain.SyncHeightmap(terrain);
                    } else {
                        road.AddPoint(new Vector3(x - 120, 0, z + 150));
                        road.AddPoint(new Vector3(x + 120, 0, z + 150));
                    }
                    road.AddPoint(new Vector3(x + 300, 0, z - (kind == 3 ? 300 : 0)));
                    road.state.SetInt("curveType", (int)curveType);
                    road.state.SetBool("subdivideEqually", subdivideEqually);
                }, () => {
                    original = DenseCurve(road);
                    originalEnd = GO.GetPosition(road.points[road.points.Count - 1]);
                    Check("split: the " + typeName + " road has a curve", road.curvePoints.Count > 1 && road.splitLine.Resolve(road.curvePoints[1]).HasValue);
                }));
                if (kind == 0) {
                    steps.Add(("split: create terrain patch anchored to the " + typeName + " road", () => {
                        var patch = new ElementPlacer.TerrainPlacer(M).GetPatch();
                        var anchors = road.anchorManager.GetTerrainAnchors();
                        var anchor = anchors[anchors.Count / 4];
                        patch.AddPerimeterPoint(anchor.GetObject(), anchor.GetPoint(), true);
                        patch.AddPerimeterPoint(null, anchor.GetPoint() + new Vector3(20, 0, 30), true);
                        patch.AddPerimeterPoint(null, anchor.GetPoint() + new Vector3(-20, 0, 30), true);
                        anchoredPoint = patch.GetPerimeterPointsComponents()[0];
                    }, () => {
                        anchoredPosition = anchoredPoint.GetPoint();
                        Check("split: the patch point is anchored to the road", !GO.IsNull(anchoredPoint.anchor) && !anchoredPoint.anchor.IsDeleted());
                    }));
                }
                steps.Add(("split: connect a road to the middle of the " + typeName + " road", () => {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    M.ShowIntersections(true); //as the road editor does in the add mode: the free road ends are enlarged
                    roadsBefore = new HashSet<Road>(M.roads);
                    var placer = new ElementPlacer.RoadPlacer(M);
                    other = placer.GetRoad();
                    roadsBefore.Add(other);
                    var mid = road.curvePoints.Count / 2;
                    switch (kind) {
                        case 0: //from the terrain to the middle
                        case 4:
                            other.AddPoint(new Vector3(x, 0, z + 400));
                            placer.Drawing.ConnectToMiddle(road, MiddlePoint(mid));
                            break;
                        case 1: //from the middle to the terrain, after abandoning a road started on the middle
                            var abandonedPlacer = new ElementPlacer.RoadPlacer(M);
                            var abandoned = abandonedPlacer.GetRoad();
                            abandonedPlacer.Drawing.ConnectToMiddle(road, MiddlePoint(mid));
                            Check("split: a road started on the middle does not split it yet", road.endIntersection == null && abandoned.points.Count == 1);
                            abandoned.Delete();
                            placer.Drawing.ConnectToMiddle(road, MiddlePoint(mid));
                            Check("split: the first point does not split the road", road.endIntersection == null);
                            placer.Drawing.AddPoint(new Vector3(x, 0, z + 400));
                            break;
                        case 2: //from the middle to another point of the middle of the same road
                            placer.Drawing.ConnectToMiddle(road, MiddlePoint(mid / 2));
                            placer.Drawing.ConnectToMiddle(road, MiddlePoint(mid + mid / 2));
                            break;
                        case 3: //from the middle (on a control point) to the end of the same road
                            placer.Drawing.ConnectToMiddle(road, MiddlePoint(1));
                            placer.Drawing.ConnectToHandle(road.points[road.points.Count - 1]);
                            break;
                    }
                    Log("  split " + typeName + " in " + watch.ElapsedMilliseconds + " ms");
                }, () => {
                    var parts = Parts();
                    var deviation = SplitDeviation(original, parts);
                    //a Hermite curve can't be split exactly, and the test roads are very curvy (the road connecting two points
                    //of the same road meets it at shallow angles, making intersections almost 100 m large)
                    var tolerance = curveType == GeometryHelper.CurveType.Hermite ? 1.5f : 0.02f;
                    var pointCount = 0;
                    foreach (var part in parts) pointCount += part.points.Count;
                    var partDeviations = "";
                    foreach (var part in parts) partDeviations += " " + part.points.Count + ":" + SplitDeviation(original, new List<Road> { part }, false);
                    Log("  split " + typeName + ": " + parts.Count + " parts, " + pointCount + " points, deviation " + deviation + " (points:deviation of each part" + partDeviations + ")");
                    Check("split: the parts of the " + typeName + " road follow its curve", deviation <= tolerance);
                    bool HasRoads(Intersection i, int count) => i != null && !i.IsDeleted() && i.roads.Count == count;
                    switch (kind) {
                        case 0:
                            Check("split: the road ends on the middle of the other one", parts.Count == 2 && HasRoads(other.endIntersection, 3)
                                && other.endIntersection == road.endIntersection && parts[1].startIntersection == road.endIntersection);
                            break;
                        case 1:
                            Check("split: the road starts on the middle of the other one", parts.Count == 2 && HasRoads(other.startIntersection, 3)
                                && other.startIntersection == road.endIntersection && other.endIntersection == null);
                            break;
                        case 2:
                            Check("split: the road connects two points of the middle of the same road", parts.Count == 3 && HasRoads(other.startIntersection, 3)
                                && HasRoads(other.endIntersection, 3) && other.startIntersection != other.endIntersection);
                            break;
                        case 3:
                            Check("split: the road connects the middle and the end of the same road", parts.Count == 2 && HasRoads(other.startIntersection, 3)
                                && HasRoads(other.endIntersection, 2) && other.endIntersection.roads.Contains(parts[1])
                                && GO.GetPosition(other.endIntersection.point).DistanceTo(originalEnd) < 0.01f);
                            break;
                        case 4:
                            Check("split: the parts of a straight road have two points", parts.Count == 2 && parts[0].points.Count == 2 && parts[1].points.Count == 2);
                            Check("split: the straight road is on bumpy terrain", Mathf.Abs(GO.GetPosition(road.points[1]).Y - GO.GetPosition(road.points[0]).Y) > 0.5f
                                || Mathf.Abs(GO.GetPosition(road.points[1]).Y - (GO.GetPosition(road.points[0]).Y + GO.GetPosition(parts[1].points[1]).Y) * 0.5f) > 0.5f);
                            break;
                    }
                    var pointsInside = 0;
                    var pointsAsEnds = 0;
                    foreach (var part in parts) {
                        if (curveType != GeometryHelper.CurveType.Bezier) {
                            pointsInside += ControlPointsInsideIntersection(part, part.startIntersection);
                            pointsInside += ControlPointsInsideIntersection(part, part.endIntersection);
                        }
                        for (int i = 1; i < part.points.Count - 1; i++) {
                            if (GO.GetLayer(part.points[i]) == 9) pointsAsEnds++;
                        }
                    }
                    Check("split: no control points of the " + typeName + " road parts inside the intersections", pointsInside == 0);
                    Check("split: no middle control points of the " + typeName + " road parts look like road ends", pointsAsEnds == 0);
                    if (anchoredPoint != null) {
                        Check("split: the anchored patch point stays in place, unanchored", (GO.IsNull(anchoredPoint.anchor) || anchoredPoint.anchor.IsDeleted())
                            && anchoredPoint.GetPoint().DistanceTo(anchoredPosition) < 0.01f);
                        anchoredPoint = null;
                    }
                }));
            }

            var snaps = new List<Dictionary<int, string>> { Snapshot() };
            foreach (var step in steps) {
                try {
                    UndoManager.Record(step.name, step.action);
                } catch (System.Exception e) {
                    Log("step '" + step.name + "' threw: " + e);
                    failures++;
                }
                M.FlagAsChanged();
                await Frames(6);
                try {
                    step.check();
                } catch (System.Exception e) {
                    Log("check of '" + step.name + "' threw: " + e);
                    failures++;
                }
                snaps.Add(Snapshot());
                Log("done: " + step.name + " (" + snaps[snaps.Count - 1].Count + " elements)");
            }
            for (int i = steps.Count - 1; i >= 0; i--) {
                UndoManager.Undo();
                await Frames(6);
                var ok = Compare("undo '" + steps[i].name + "'", snaps[i], Snapshot());
                Log((ok ? "OK   " : "FAIL ") + "undo " + steps[i].name);
            }
            for (int i = 0; i < steps.Count; i++) {
                UndoManager.Redo();
                await Frames(6);
                var ok = Compare("redo '" + steps[i].name + "'", snaps[i + 1], Snapshot());
                Log((ok ? "OK   " : "FAIL ") + "redo " + steps[i].name);
            }
        }

        static void PlaceIntersection(ElementPlacer.RoadPlacer placer, Node3D handle, bool addPoint) {
            placer.Drawing.PlaceIntersection(handle, addPoint);
        }

        async Task LoadNewCity(int terrainSize = 512) {
            await Frames(30);
            CreateCity(terrainSize);
            var waited = 0;
            while ((PreferencesManager.workingDirectory == "" || UndoManager.IsLoading || M.GetDummy<CityProperties>() == null) && waited < 3000) {
                await Frames(1);
                waited++;
            }
            await Frames(30);
            Log("city loaded after " + waited + " frames");
        }

        //the lines along the roads while placing a road, with the cursor on one of them (split_lines.png next to the folder)
        async Task SplitLinesScreenshot() {
            InvokeBuilder("CreateRoad");
            await Frames(6);
            var cam = GetViewport().GetCamera3D();
            GO.SetPosition(cam, new Vector3(-450, 700, -1150));
            GO.LookAt(cam, new Vector3(-450, 0, -550));
            await Frames(6);
            Road hovered = null;
            foreach (var road in M.roads) {
                if (ElementSnapshot.IsAlive(road) && GO.GetEnabled(road.splitLine) && road.curvePoints.Count > 2) {
                    hovered = road;
                    break;
                }
            }
            Check("split: the lines along the roads are shown while placing a road", hovered != null);
            if (hovered == null) return;
            var point = hovered.splitLine.Resolve(hovered.curvePoints[hovered.curvePoints.Count / 2]).Value.point;
            GetViewport().WarpMouse(cam.UnprojectPosition(GameEngine.Vector3.DecodePosition(point)));
            await Frames(10);
            var placer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
            Check("split: the edited road has no line", !GO.GetEnabled(placer.GetRoad().splitLine));
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, "..", "split_lines.png"));
        }

        async void RunSplit() {
            var lastCity = SettingsManager.Get("LastCity", "");
            try {
                await LoadNewCity(2048); //room for roads much longer than the size of their intersections (the default road type is 40 m wide)
                await SplitTests();
                await SplitLinesScreenshot();
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            SettingsManager.Set("LastCity", lastCity);
            Log("finished with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "undo_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }

        async void Run() {
            var lastCity = SettingsManager.Get("LastCity", "");
            try {
                await LoadNewCity();
                UndoManager.Clear();

                Road roadA = null, roadB = null, roadC = null;
                TerrainPatch patch = null;
                BuildingLine line = null;
                MeshInstance mesh = null;
                var steps = new List<(string name, System.Action action)> {
                    ("create road A", () => {
                        var placer = new ElementPlacer.RoadPlacer(M);
                        roadA = placer.GetRoad();
                        roadA.AddPoint(new Vector3(0, 0, 0));
                        roadA.AddPoint(new Vector3(60, 0, 0));
                        roadA.AddPoint(new Vector3(120, 0, 10));
                    }),
                    ("create road B joined to A", () => {
                        var placer = new ElementPlacer.RoadPlacer(M);
                        roadB = placer.GetRoad();
                        roadB.AddPoint(new Vector3(120, 0, 120));
                        roadB.AddPoint(new Vector3(125, 0, 60));
                        PlaceIntersection(placer, roadA.points[roadA.points.Count - 1], true);
                    }),
                    ("create road C joined to the intersection", () => {
                        var placer = new ElementPlacer.RoadPlacer(M);
                        roadC = placer.GetRoad();
                        roadC.AddPoint(new Vector3(220, 0, 0));
                        roadC.AddPoint(new Vector3(170, 0, 5));
                        PlaceIntersection(placer, roadA.endIntersection.point, true);
                    }),
                    ("create terrain patch", () => {
                        var placer = new ElementPlacer.TerrainPlacer(M);
                        patch = placer.GetPatch();
                        var anchors = roadA.anchorManager.GetTerrainAnchors();
                        patch.AddPerimeterPoint(anchors[0].GetObject(), anchors[0].GetPoint(), true);
                        patch.AddPerimeterPoint(null, new Vector3(0, 0, -40), true);
                        patch.AddPerimeterPoint(null, new Vector3(-40, 0, -40), true);
                        patch.AddPerimeterPoint(null, new Vector3(-40, 0, 0), true);
                        patch.AddInternalPoint(null, new Vector3(-20, 0, -20));
                    }),
                    ("create building line", () => {
                        var placer = new ElementPlacer.BuildingPlacer(M);
                        line = placer.GetLine();
                        line.AddPoint(null, new Vector3(0, 0, 40), true);
                        line.AddPoint(null, new Vector3(30, 0, 40), true).dividing = true;
                        line.AddPoint(null, new Vector3(60, 0, 40), true);
                    }),
                    ("create mesh", () => {
                        mesh = MeshInstance.Create("", new Vector3(-60, 0, 60), M.GetMeshContainer(), new ObjectState());
                        M.AddMesh(mesh);
                    }),
                    ("move road A point (gizmo)", () => {
                        UndoManager.TouchOwner(roadA.points[1]);
                        GO.SetPosition(roadA.points[1], GO.GetPosition(roadA.points[1]) + new Vector3(0, 0, 15));
                    }),
                    ("move patch point (gizmo)", () => {
                        var p = patch.GetPerimeterPointsComponents()[2];
                        UndoManager.TouchOwner(p.GetObject());
                        GO.SetPosition(p.GetObject(), p.GetPoint() + new Vector3(-10, 0, 0));
                    }),
                    ("move mesh (gizmo)", () => {
                        UndoManager.TouchOwner(mesh);
                        GO.SetPosition(mesh, GO.GetPosition(mesh) + new Vector3(5, 0, 5));
                    }),
                    ("edit road A field", () => {
                        UndoManager.Touch(roadA);
                        FieldStatus.SetValue(roadA, "5", "state.properties.segments");
                    }),
                    ("edit city properties", () => {
                        var cp = M.GetDummy<CityProperties>();
                        UndoManager.Touch(cp);
                        cp.state.SetStr("undoTest", "value");
                    }),
                    ("rename building line", () => {
                        UndoManager.Touch(line);
                        GO.SetName(line, "Renamed line 1.5/a:b");
                        Check("object name keeps characters invalid in node names", GO.GetName(line) == "Renamed line 1.5/a:b");
                    }),
                    ("toggle building line dividing point", () => {
                        var p = line.GetPointsComponents()[1];
                        UndoManager.Touch(p);
                        p.dividing = !p.dividing;
                    }),
                    ("brush heights", () => {
                        UndoManager.TouchHeights(100, 100, 40, 40);
                        var heights = GameEngine.Terrain.GetHeights(builder.helper.terrainObj, 100, 100, 40, 40);
                        for (int y = 0; y < 40; y++) for (int x = 0; x < 40; x++) heights[y, x] += 0.01f;
                        GameEngine.Terrain.SetHeightsDelayLOD(builder.helper.terrainObj, 100, 100, heights);
                        GameEngine.Terrain.SyncHeightmap(builder.helper.terrainObj);
                    }),
                    ("brush heights under the intersection", () => {
                        var terrain = builder.helper.terrainObj;
                        var pixelSize = GameEngine.Terrain.GetPixelSize(terrain);
                        var terrainSize = GameEngine.Terrain.GetSize(terrain);
                        var center = (GO.GetPosition(roadA.endIntersection.point) + new Vector3(terrainSize, 0, terrainSize) * 0.5f) / pixelSize;
                        int x0 = (int)center.X - 20, y0 = (int)center.Z - 20;
                        UndoManager.TouchHeights(x0, y0, 40, 40);
                        var heights = GameEngine.Terrain.GetHeights(terrain, x0, y0, 40, 40);
                        for (int y = 0; y < 40; y++) for (int x = 0; x < 40; x++) heights[y, x] += 0.005f;
                        GameEngine.Terrain.SetHeightsDelayLOD(terrain, x0, y0, heights);
                        GameEngine.Terrain.SyncHeightmap(terrain);
                        M.worldChanged = true;
                    }),
                    ("delete road C", () => roadC.Delete()),
                    ("delete road A (intersection and anchored point)", () => roadA.Delete()),
                    ("delete patch internal point", () => {
                        var p = patch.GetInternalPointsComponents()[0];
                        p.DeleteManual(patch);
                    }),
                    ("delete patch", () => patch.Delete()),
                    ("delete building line", () => line.Delete()),
                    ("delete mesh", () => mesh.Delete()),
                };

                var snaps = new List<Dictionary<int, string>> { Snapshot() };
                foreach (var step in steps) {
                    try {
                        UndoManager.Record(step.name, step.action);
                    } catch (System.Exception e) {
                        Log("step '" + step.name + "' threw: " + e);
                        failures++;
                    }
                    M.FlagAsChanged();
                    await Frames(6);
                    snaps.Add(Snapshot());
                    Log("done: " + step.name + " (" + snaps[snaps.Count - 1].Count + " elements)");
                }

                for (int i = steps.Count - 1; i >= 0; i--) {
                    UndoManager.Undo();
                    await Frames(6);
                    var ok = Compare("undo '" + steps[i].name + "'", snaps[i], Snapshot());
                    Log((ok ? "OK   " : "FAIL ") + "undo " + steps[i].name);
                }
                for (int i = 0; i < steps.Count; i++) {
                    UndoManager.Redo();
                    await Frames(6);
                    var ok = Compare("redo '" + steps[i].name + "'", snaps[i + 1], Snapshot());
                    Log((ok ? "OK   " : "FAIL ") + "redo " + steps[i].name);
                }
                //and back again, the redone elements are recreated objects
                for (int i = steps.Count - 1; i >= 0; i--) {
                    UndoManager.Undo();
                    await Frames(6);
                    var ok = Compare("undo again '" + steps[i].name + "'", snaps[i], Snapshot());
                    Log((ok ? "OK   " : "FAIL ") + "undo again " + steps[i].name);
                }
                await MenuLevelTests();
                await SplitTests();
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            SettingsManager.Set("LastCity", lastCity);
            Log("finished with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "undo_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }
    }
}

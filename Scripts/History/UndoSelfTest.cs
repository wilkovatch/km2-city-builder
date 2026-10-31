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
        //intersections is edited, undone and redone; drag: the middle mouse drag of the numeric fields, on a new city;
        //dropdown: the keyboard navigation of the dropdown lists and the camera under them, on a new city;
        //selection: selection and deselection timings and handle cleanup, on a copy of an existing city;
        //placement: control point selection and insertion on the first roads created after startup, on a new city;
        //height: the placement height (Shift + mouse movement) and the snapping toggle, on a new city
        public static readonly string[] Modes = { "new", "props", "ui", "split", "load", "drag", "dropdown", "selection", "placement", "height" };

        public static void Start(CityBuilderMenuBar builder, string folder, string mode) {
            builder.AddChild(new UndoSelfTest {
                builder = builder, folder = folder, Name = "UndoSelfTest",
                propsSelection = mode == "props", uiRestore = mode == "ui", splitOnly = mode == "split", placementOnly = mode == "placement", loadExisting = mode == "load",
                dragOnly = mode == "drag", dropdownOnly = mode == "dropdown", selectionOnly = mode == "selection", heightOnly = mode == "height"
            });
        }

        bool propsSelection = false;
        bool splitOnly = false;
        bool placementOnly = false;
        bool dragOnly = false;
        bool dropdownOnly = false;
        bool selectionOnly = false;
        bool heightOnly = false;

        //Selection on a copy of a large city, including cities with no procedural roads. Times the synchronous work
        //separately from the next frame, which includes the renderer and physics server processing the handle changes.
        async void RunSelection() {
            var lastCity = SettingsManager.Get("LastCity", "");
            try {
                await Frames(2);
                PreferencesManager.workingDirectory = folder;
                CoreManager.Reset();
                var loaded = await CityImporter.LoadCityAsync(M, folder, new CityDecoder.LoadOptions {
                    interaction = new Operations.FixedInteraction(), rememberAsLastCity = false
                });
                if (!loaded.success) throw new System.Exception(loaded.message);
                await Frames(3);
                UndoManager.Clear();
                Log("city loaded: " + M.customElements.Count + " custom elements, " + M.meshes.Count + " meshes");
                void Time(string label, System.Action action) {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    action();
                    Log(label + ": " + timer.Elapsed.TotalMilliseconds.ToString("F1") + " ms");
                }
                Time("hide anchors (already hidden)", () => M.ShowAnchors(false));
                Time("hide intersections (already hidden)", () => M.ShowIntersections(false));
                Time("deselect meshes (already deselected)", () => M.DeselectMeshes());
                var samples = new List<Node3D>();
                if (M.meshes.Count > 0) samples.Add(M.meshes[0]);
                var element = M.customElements.Find(e => e.GetAllPoints().Count > 0 && !(e.GetCustomElementType()?.IsContainer ?? false));
                if (element != null) samples.Add(element);
                var container = M.customElements.Find(e => e.GetCustomElementType()?.IsContainer ?? false);
                if (container != null) samples.Add(container);
                foreach (var sample in samples) {
                    for (int i = 0; i < 2; i++) {
                        Time("select " + GO.GetName(sample), () => UndoManager.Record("select", () => builder.SelectObject(sample, true, null)));
                        await Frames(2);
                        Time("deselect " + GO.GetName(sample), () => UndoManager.Record("close", () => InvokeBuilder("DeselectAndCloseCurrentPanel")));
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        await Frames(2);
                        Log("frames after deselection: " + timer.Elapsed.TotalMilliseconds.ToString("F1") + " ms");
                        Check("closing leaves no editor open", Shown() == null);
                        Check("selection records no undo steps", UndoCount() == 0);
                    }
                }
                var allHidden = true;
                var seen = new HashSet<TerrainPoint>();
                foreach (var e in M.customElements) {
                    allHidden &= !e.IsActive() && !e.IsSelected() && !e.IsMoveHandleShown();
                    foreach (var p in e.GetAllPoints()) {
                        if (seen.Add(p)) allHidden &= !GO.GetEnabled(p.GetObject());
                    }
                }
                Check("all custom element handles are hidden after closing", allHidden);
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            SettingsManager.Set("LastCity", lastCity);
            Log("finished with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "undo_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }

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
                await Frames(3);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, "..", "undo_selftest_road_tab2.png"));
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

                //rotation of a control point with the gizmo (in the frame of the curve, yaw around its local Y), preferably
                //an end on an intersection (its mouth is skewed)
                var rotIndex = -1;
                for (int i = 0; i < road.points.Count; i++) if (road.IsYawAllowed(i)) { rotIndex = i; break; }
                if (road.endIntersection != null && road.IsYawAllowed(road.points.Count - 1)) rotIndex = road.points.Count - 1;
                if (road.startIntersection != null && road.IsYawAllowed(0)) rotIndex = 0;
                Check("a road point that can have a yaw (" + rotIndex + ")", rotIndex >= 0 && road.IsYawAllowed(rotIndex));
                if (rotIndex < 0) rotIndex = 0; //checked above, the rest of the test fails without crashing
                builder.SelectObject(road, true, null);
                gizmo.ClearTargets();
                gizmo.AddTarget(road.points[rotIndex]);
                CallBuilder<object>("EditCurSelectedObjectTransform");
                typeof(EditorPanels.TransformPropertiesEditorPanel).GetMethod("SetRotate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(transformPanel, null);
                Check("rotate mode on a road point: local space, no pitch, yaw and roll as the road type allows",
                    gizmo.transformType == RuntimeGizmos.TransformType.Rotate && gizmo.space == RuntimeGizmos.TransformSpace.Local
                    && !gizmo.rotAxesAllowed[0] && gizmo.rotAxesAllowed[1] && gizmo.rotAxesAllowed[2] == road.IsRollAllowed());
                var yaw0 = road.GetPointRotations()[rotIndex].X;
                string RotatedPoint() {
                    var cur = ElementRegistry.Get<Road>(roadId);
                    var sel = gizmo.mainTargetRoot;
                    var index = cur != null && sel != null ? cur.points.IndexOf(sel as StaticBody3D) : -1;
                    return Chain() + " point " + index + " yaw " + (index >= 0 ? cur.GetPointRotations()[index].X.ToString("0.0") : "-");
                }
                await CheckRestore("gizmo rotation of a road point", () => {
                    UndoManager.TouchOwner(road.points[rotIndex]);
                    var handle = road.points[rotIndex];
                    handle.GlobalRotate(handle.GlobalBasis.Y.Normalized(), Mathf.DegToRad(10));
                }, RotatedPoint);
                InvokeBuilder("Redo"); //CheckRestore leaves the edit undone
                await Frames(6);
                var rotated = ElementRegistry.Get<Road>(roadId);
                var yaw1 = rotated != null ? rotated.GetPointRotations()[rotIndex].X : float.NaN;
                Check("the gizmo rotation of the handle gives the yaw of the point (" + yaw0 + " -> " + yaw1 + ")", Mathf.Abs(Mathf.Abs(yaw1 - yaw0) - 10) < 0.5f);
                InvokeBuilder("Undo");
                await Frames(6);
                Close();

                //the ends of the edited road on intersections replace the intersection points (hidden), and the intersections follow them
                var endIndex = road.startIntersection != null ? 0 : road.endIntersection != null ? road.points.Count - 1 : -1;
                Check("the road has an end on an intersection", endIndex >= 0);
                if (endIndex >= 0) {
                    var endIntersection = endIndex == 0 ? road.startIntersection : road.endIntersection;
                    var intersectionId = Id(endIntersection);
                    builder.SelectObject(road, true, null);
                    await Frames(3);
                    var endHandle = (Handle)road.points[endIndex];
                    Check("editing a road: its end on an intersection is as big as the intersection point, which is hidden",
                        endHandle.HandleScale == Vector3.One * Road.IntersectionHandleScale && endHandle.IsHandleActive() && GO.GetEnabled(endHandle)
                        && !((Handle)endIntersection.point).IsHandleActive());
                    gizmo.ClearTargets();
                    gizmo.AddTarget(endIntersection.point);
                    CallBuilder<object>("EditCurSelectedObjectTransform");
                    Check("M on the intersection point edits the road end instead",
                        Shown() == transformPanel && transformPanel.instance == road.points[endIndex] && gizmo.mainTargetRoot == road.points[endIndex]);
                    string EndState() {
                        var cur = ElementRegistry.Get<Intersection>(intersectionId);
                        return SelectedPoint() + " intersection at " + (cur != null ? GO.GetPosition(cur.point).ToString("0.0") : "-");
                    }
                    var center0 = GO.GetPosition(endIntersection.point);
                    var offset = new Vector3(4, 0, 3);
                    await CheckRestore("road end on an intersection moved", () => {
                        UndoManager.TouchOwner(road.points[endIndex]);
                        GO.SetPosition(road.points[endIndex], center0 + offset);
                    }, EndState);
                    InvokeBuilder("Redo"); //CheckRestore leaves the edit undone
                    await Frames(6);
                    var movedIntersection = ElementRegistry.Get<Intersection>(intersectionId);
                    var followed = movedIntersection != null;
                    if (followed) {
                        var target = center0 + offset;
                        bool Near(Vector3 p) => new Vector2(p.X - target.X, p.Z - target.Z).Length() < 0.01f;
                        followed = Near(GO.GetPosition(movedIntersection.point));
                        foreach (var r in movedIntersection.roads) {
                            if (r.startIntersection == movedIntersection) followed &= Near(GO.GetPosition(r.points[0]));
                            if (r.endIntersection == movedIntersection) followed &= Near(GO.GetPosition(r.points[r.points.Count - 1]));
                        }
                    }
                    Check("the intersection and the ends of its other roads follow the road end", followed);
                    InvokeBuilder("Undo");
                    await Frames(6);
                    Close();
                }

                //the buttons of the intersection tab of the road editor switch to the editors of the intersections at its ends
                //(a button of an end not on an intersection is disabled)
                var switchedPanel = Panel<EditorPanels.IntersectionEditorPanel>("intersectionEditorPanel");
                var switches = new List<string>();
                var switchesOk = true;
                foreach (var r in new List<Road>(M.roads)) {
                    if (!ElementSnapshot.IsAlive(r)) continue;
                    foreach (var atStart in new[] { true, false }) {
                        var expected = atStart ? r.startIntersection : r.endIntersection;
                        builder.SelectObject(r, true, null);
                        roadPanel.ShowPage(1);
                        await Frames(1);
                        //read after opening the panel, which may be rebuilt
                        var button = (EditorPanelElements.Button)Field(roadPanel, atStart ? "startIntersectionButton" : "endIntersectionButton");
                        var enabled = button.GetInteractable();
                        if (expected == null) {
                            switchesOk &= !enabled;
                            switches.Add((atStart ? "start" : "end") + " none " + (enabled ? "enabled" : "disabled"));
                        } else {
                            ((Godot.Button)FieldControl(button)).EmitSignal(Godot.BaseButton.SignalName.Pressed);
                            await Frames(2);
                            var ok = enabled && Shown() == switchedPanel && switchedPanel.intersection == expected;
                            switchesOk &= ok;
                            switches.Add((atStart ? "start" : "end") + " " + (ok ? "ok" : "not ok: " + Chain()));
                        }
                        Close();
                    }
                    if (switches.Count >= 8) break;
                }
                Check("edit start/end intersection (" + string.Join(", ", switches) + ")", switchesOk && switches.Count > 0);

                //intersection editor
                Intersection intersection = null;
                foreach (var it in M.intersections) if (ElementSnapshot.IsAlive(it)) { intersection = it; break; }
                if (intersection != null) {
                    builder.SelectObject(intersection.geo, true, null);
                    await CheckRestore("intersection editor", () => { UndoManager.Touch(intersection); FieldStatus.SetValue(intersection, "1.5", "state.properties.sizeIncrease"); }, Chain);
                    Close();
                }

                //the merge buttons of the intersection editor are shown only with two roads, without leaving their row empty
                //(the seamless checkbox below them moves up by a row)
                var intersectionPanel = Panel<EditorPanels.IntersectionEditorPanel>("intersectionEditorPanel");
                var mergeShown = new List<string>();
                var mergeOk = true;
                var checkboxY = new Dictionary<bool, float>();
                foreach (var it in new List<Intersection>(M.intersections)) {
                    if (!ElementSnapshot.IsAlive(it)) continue;
                    builder.SelectObject(it.geo, true, null);
                    await Frames(1);
                    var buttons = (List<EditorPanelElements.Button>)Field(intersectionPanel, "mergeButtons");
                    var visible = buttons.FindAll(b => FieldControl(b).GetParent<Control>().Visible).Count;
                    var two = it.roads.Count == 2;
                    var y = FieldControl((EditorPanelElement)Field(intersectionPanel, "seamlessCheckbox")).GetParent<Control>().Position.Y;
                    if (!checkboxY.ContainsKey(two)) {
                        checkboxY[two] = y;
                        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, "..", "undo_selftest_intersection_" + (two ? "2" : "more") + "_roads.png"));
                    }
                    mergeOk &= checkboxY[two] == y;
                    mergeShown.Add(it.roads.Count + " roads: " + visible + " at " + y);
                    mergeOk &= visible == (two ? 2 : 0);
                    Close();
                }
                var moved = checkboxY.ContainsKey(true) && checkboxY.ContainsKey(false) ? checkboxY[true] - checkboxY[false] : float.NaN;
                Check("merge buttons only with two roads, their row taken by the next ones without them (" + moved + " px; " + string.Join(", ", mergeShown) + ")",
                    mergeOk && mergeShown.Count > 0 && moved == 50);

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

                //array/custom city properties panels (city properties, then the road)
                foreach (var (owner, panelField) in new (object, string)[] { (M.GetDummy<CityProperties>(), "cityPropertiesEditorPanel"), (road, "roadEditorPanel") }) {
                    var panel = Panel<EditorPanel>(panelField);
                    if (owner is Road) builder.SelectObject(road, true, null); else CallBuilder<object>("ToggleCityProperties");
                    var parameterMap = (System.Collections.IDictionary)typeof(EditorPanel).GetField("parameterMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(panel);
                    var stateOwner = (IObjectWithState)owner;
                    EditorPanel opened = null;
                    foreach (CityElements.Types.Parameter param in parameterMap.Values) {
                        if (param.type == "array" && stateOwner.GetState().Array<ObjectState>(param.fullName())?.Length > 0) opened = panel.OpenArrayElement(param.fullName(), 0);
                        else if (param.type == "customCityProperties" && stateOwner.GetState().State(param.fullName()) != null) opened = panel.OpenArrayElement(param.fullName(), null);
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

                //the points of a train line opened one after the other (the point panel is rebuilt each time, it used to keep
                //the buttons of the destroyed pages and fail on them once freed)
                var cityProps = M.GetDummy<CityProperties>();
                var cityPropsPanel = Panel<EditorPanel>("cityPropertiesEditorPanel");
                CallBuilder<object>("ToggleCityProperties");
                CityElements.Types.Parameter trainLinesParam = null;
                foreach (CityElements.Types.Parameter param in ((System.Collections.IDictionary)typeof(EditorPanel).GetField("parameterMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(cityPropsPanel)).Values) {
                    if (param.fullName() == "trainLines") trainLinesParam = param;
                }
                if (trainLinesParam != null) {
                    var trainLineParams = CityElements.Types.Parsers.TypeParser.GetCustomCityPropertiesTypes()["trainLine"].parameters;
                    var pointsParam = System.Array.Find(trainLineParams, p => p.name == "points");
                    var li = -1;
                    UndoManager.Record("add train line", () => {
                        UndoManager.Touch(cityProps);
                        li = Operations.States.AddArrayElement(cityProps.GetState(), trainLinesParam);
                        var lineState = cityProps.GetState().Array<ObjectState>("trainLines")[li];
                        Operations.States.AddArrayObjectAt(lineState, pointsParam, new Vector3(10, 0, 10));
                        Operations.States.AddArrayObjectAt(lineState, pointsParam, new Vector3(60, 0, 10));
                        cityProps.GetState().FlagAsChanged();
                    });
                    M.FlagAsChanged();
                    await Frames(6);
                    var linePanel = cityPropsPanel.OpenArrayElement("trainLines", li);
                    string error = null;
                    EditorPanel pointPanel = null;
                    foreach (var pi in new[] { 0, 1, 0, 1 }) {
                        try {
                            pointPanel = linePanel?.OpenArrayElement("points", pi);
                        } catch (System.Exception e) {
                            error ??= "point " + pi + ": " + e.GetType().Name + " " + e.Message;
                        }
                        await Frames(6);
                    }
                    Check("train line points opened one after the other" + (error != null ? " (" + error + ")" : ""), linePanel != null && pointPanel != null && error == null);
                    var buttons = pointPanel != null ? ((System.Collections.ICollection)Field(pointPanel, "objectInstanceParameters")).Count : -1;
                    Check("the point panel keeps only its own object buttons (" + buttons + ")", buttons == 1);
                    Close();
                    InvokeBuilder("Undo");
                    await Frames(6);
                } else {
                    Log("     no trainLines parameter in the core");
                    Close();
                }

                //props container of the road (enabled if needed) and one of its elements
                builder.SelectObject(road, true, null);
                string container = null;
                foreach (var k in ((System.Collections.IDictionary)Field(roadPanel, "propControls")).Keys) { container = (string)k; break; }
                if (container != null) {
                    if (road.state.State("propRule")?.State(container) == null) {
                        UndoManager.Record("enable props", () => { UndoManager.Touch(road); Invoke(Field(roadPanel, "propRule"), "EnableContainer", container, true); });
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

                    //a building preset chosen in the line editor goes to the buildings of the line and to its new ones, not to the line
                    var tempPreset = (ObjectState)line.buildings[0].state.Clone();
                    Operations.Presets.Save(tempPreset, "building", "ui test building");
                    builder.SelectObject(line, true, null);
                    await Frames(3);
                    object presetSelector = null;
                    foreach (var element in ((System.Collections.IDictionary)typeof(EditorPanel).GetField("complexElements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(linePanel)).Values) {
                        if (element.GetType().Name == "PresetSelector") presetSelector = element;
                    }
                    var buildingDropdown = (EditorPanelElements.Dropdown)Field(((System.Collections.IList)((System.Collections.IDictionary)Field(presetSelector, "dropdowns"))["building"])[0], "dropdown");
                    var lineStateBefore = (ObjectState)line.state.Clone();
                    buildingDropdown.SetValue(System.Array.IndexOf(PresetManager.GetNames("building"), "ui test building") + 1);
                    await Frames(6);
                    Check("a building preset chosen in the line editor leaves the state of the line as it was", line.state.Equals(lineStateBefore));
                    Check("a building preset chosen in the line editor goes to the buildings of the line", line.buildings.TrueForAll(b => b.state.Name == "ui test building"));
                    Check("a building preset chosen in the line editor goes to the new buildings of the line", line.stateForNewBuildings?.Name == "ui test building");
                    InvokeBuilder("Undo");
                    await Frames(6);
                    Close();
                    CallBuilder<object>("CreateBuildingLine");
                    await Frames(3);
                    var newLine = linePanel.GetLine();
                    Check("a new line uses the last building preset chosen for its buildings", newLine?.stateForNewBuildings?.Name == "ui test building" && newLine.state.Name != "ui test building");
                    Close();
                    await Frames(3);
                    Operations.Presets.Delete(M, "building", "ui test building");
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
            if (selectionOnly) RunSelection();
            else if (uiRestore) RunUIRestore();
            else if (propsSelection) RunPropsSelection();
            else if (loadExisting) RunOnExistingCity();
            else if (splitOnly || placementOnly) RunSplit();
            else if (dragOnly) RunDrag();
            else if (dropdownOnly) RunDropdown();
            else if (heightOnly) RunHeight();
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
        static float SplitDeviation(List<Vector3> original, List<Road> parts, bool bothWays = true, List<(Vector3 center, float radius)> extraHidden = null) {
            var hidden = new List<(Vector3 center, float radius)>();
            if (extraHidden != null) hidden.AddRange(extraHidden);
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
                var jointAreas = new List<(Vector3 center, float radius)>();
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
                    //where the road is not shown, the split removed its control points (for the merge test)
                    jointAreas.Clear();
                    foreach (var part in parts) {
                        var intersection = part.endIntersection;
                        var idx = intersection == null ? -1 : intersection.roads.IndexOf(part);
                        if (idx >= 0 && idx < intersection.sizes.Count) jointAreas.Add((GO.GetPosition(intersection.point), intersection.sizes[idx]));
                    }
                    Check("split: no control points of the " + typeName + " road parts inside the intersections", pointsInside == 0);
                    Check("split: no middle control points of the " + typeName + " road parts look like road ends", pointsAsEnds == 0);
                    if (anchoredPoint != null) {
                        Check("split: the anchored patch point stays in place, unanchored", (GO.IsNull(anchoredPoint.anchor) || anchoredPoint.anchor.IsDeleted())
                            && anchoredPoint.GetPoint().DistanceTo(anchoredPosition) < 0.01f);
                        anchoredPoint = null;
                    }
                }));
                //the road connected to the middle deleted, the parts merged back into the road
                steps.Add(("merge: delete the road connected to the " + typeName + " road", () => {
                    other.Delete();
                }, () => {
                    Check("merge: the intersections between the parts of the " + typeName + " road have two roads",
                        road.endIntersection != null && road.endIntersection.roads.Count == 2);
                }));
                steps.Add(("merge: merge the parts of the " + typeName + " road", () => {
                    var merges = 0;
                    while (road.endIntersection != null && road.endIntersection.roads.Count == 2 && merges++ < 5) {
                        var error = Operations.Intersections.GetMergeError(road.endIntersection, road);
                        if (error != null) {
                            Log("  merge " + typeName + ": " + error);
                            break;
                        }
                        Operations.Intersections.Merge(M, road.endIntersection, road);
                    }
                }, () => {
                    //the Bézier, low-poly and straight curves are merged exactly, a Hermite curve gets the points of its parts,
                    //so it is as close to the original as they are, except where the split removed its points
                    var hermite = curveType == GeometryHelper.CurveType.Hermite && kind != 4;
                    var outside = SplitDeviation(original, new List<Road> { road }, true, jointAreas);
                    var deviation = SplitDeviation(original, new List<Road> { road });
                    Log("  merge " + typeName + ": " + road.points.Count + " points, deviation " + deviation + " (" + outside + " outside the former intersections)");
                    Check("merge: the " + typeName + " road is whole again", Parts().Count == 1 && road.endIntersection == null && road.startIntersection == null
                        && GO.GetPosition(road.points[road.points.Count - 1]).DistanceTo(originalEnd) < 0.01f);
                    Check("merge: the merged " + typeName + " road follows the original curve", hermite ? outside <= 1.5f && deviation <= 5.0f : deviation <= 0.02f);
                    if (kind == 4) Check("merge: the straight road has two points again", road.points.Count == 2);
                    var pointsAsEnds = 0;
                    for (int i = 1; i < road.points.Count - 1; i++) {
                        if (GO.GetLayer(road.points[i]) == 9) pointsAsEnds++;
                    }
                    Check("merge: no middle control points of the merged " + typeName + " road look like road ends", pointsAsEnds == 0);
                }));
                //the whole road split again without connecting a road to it (as in the split mode of the road editor)
                List<Vector3> beforeSplit = null;
                Intersection splitIntersection = null;
                Road splitRoad = null;
                steps.Add(("split: split the " + typeName + " road without connecting a road", () => {
                    beforeSplit = DenseCurve(road);
                    (splitIntersection, splitRoad) = Operations.Roads.Split(M, road, MiddlePoint(road.curvePoints.Count / 2));
                }, () => {
                    var parts = Parts();
                    var deviation = SplitDeviation(beforeSplit, parts);
                    var tolerance = curveType == GeometryHelper.CurveType.Hermite ? 1.5f : 0.02f;
                    Log("  split " + typeName + " without a connected road: " + parts.Count + " parts, deviation " + deviation);
                    Check("split: the " + typeName + " road is split in two connected by a new intersection", splitIntersection != null && parts.Count == 2
                        && parts[1] == splitRoad && road.endIntersection == splitIntersection && splitRoad.startIntersection == splitIntersection
                        && splitIntersection.roads.Count == 2 && road.startIntersection == null && splitRoad.endIntersection == null
                        && GO.GetPosition(splitRoad.points[splitRoad.points.Count - 1]).DistanceTo(originalEnd) < 0.01f);
                    Check("split: the new part of the " + typeName + " road has its properties", splitRoad != null
                        && splitRoad.GetCurveType() == road.GetCurveType() && splitRoad.state.Str("type") == road.state.Str("type"));
                    Check("split: the parts of the " + typeName + " road split without a connected road follow its curve", deviation <= tolerance);
                    if (kind == 4) Check("split: the parts of the straight road split without a connected road have two points", parts.TrueForAll(p => p.points.Count == 2));
                    var pointsInside = 0;
                    foreach (var part in parts) {
                        if (curveType != GeometryHelper.CurveType.Bezier) pointsInside += ControlPointsInsideIntersection(part, splitIntersection);
                    }
                    Check("split: no control points of the " + typeName + " road parts inside the intersection of the split without a connected road", pointsInside == 0);
                }));
            }
            await RunSteps(steps);
        }

        //runs and checks the steps, then undoes and redoes all of them comparing the snapshots
        async Task RunSteps(List<(string name, System.Action action, System.Action check)> steps) {
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

        //the split mode of the road editor: the line along the edited road (only) is shown, and clicking it splits the road
        async Task SplitModeTest() {
            InvokeBuilder("DeselectAndCloseCurrentPanel");
            await Frames(6);
            Road road = null;
            foreach (var r in M.roads) {
                if (ElementSnapshot.IsAlive(r) && r.GetCurveType() == GeometryHelper.CurveType.Bezier && (road == null || r.curvePoints.Count > road.curvePoints.Count)) road = r;
            }
            Check("split mode: a Bézier road to split", road != null);
            if (road == null) return;
            var roadPanel = Panel<EditorPanels.RoadEditorPanel>("roadEditorPanel");
            ElementPlacer.RoadPlacer Placer() => builder.terrainClick.modifier as ElementPlacer.RoadPlacer;
            builder.SelectObject(road, true, null);
            roadPanel.SetPointPlacementMode((int)ElementPlacer.RoadPlacer.PlacementMode.Split);
            await Frames(6);
            var otherLines = 0;
            foreach (var r in M.roads) {
                if (r != road && ElementSnapshot.IsAlive(r) && GO.GetEnabled(r.splitLine)) otherLines++;
            }
            Check("split mode: the line along the edited road is shown, not the ones of the other roads", GO.GetEnabled(road.splitLine) && otherLines == 0);

            //a click on the line, away from the control points, through the real click handling
            var point = road.splitLine.Resolve(road.curvePoints[road.curvePoints.Count / 3]).Value.point;
            var cam = GetViewport().GetCamera3D();
            GO.SetPosition(cam, point + new Vector3(0, 250, -250));
            GO.LookAt(cam, point);
            await Frames(6);
            GetViewport().WarpMouse(cam.UnprojectPosition(GameEngine.Vector3.DecodePosition(point)));
            await Frames(6);
            //what the click handling sees under the cursor
            var ray = GameEngine.Raycast.GetRayFromScreen(cam, GetViewport().GetMousePosition());
            var underCursor = "";
            foreach (var mask in Placer().GetLayerMasks()) {
                var found = GameEngine.Raycast.DoRaycast(builder.helper, ray.origin, ray.direction, out var hit, 100000f, mask);
                underCursor += (found ? hit.collider?.Name.ToString() : "-") + " ";
            }
            var onUI = (bool)typeof(TerrainClick).GetMethod("OnUI", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(builder.terrainClick, null);
            Log("  split mode: under the cursor " + underCursor + "(mouse " + GetViewport().GetMousePosition() + ", on UI " + onUI + ", near axis " + GameEngine.Gizmo.IsNearAxis() + ")");
            var oldEnd = road.endIntersection;
            var roadsBefore = AliveRoads();
            var before = Snapshot();
            //a real mouse button event, handled at the start of the next frame like a click (an action pressed from here would no
            //longer be "just pressed" when the click handling runs, and a press not seen by the GUI counts as a click on it)
            InputEventMouseButton Click(bool pressed) => new InputEventMouseButton {
                ButtonIndex = MouseButton.Left, Pressed = pressed, Position = GetViewport().GetMousePosition(), GlobalPosition = GetViewport().GetMousePosition()
            };
            Godot.Input.ParseInputEvent(Click(true));
            await Frames(2);
            Godot.Input.ParseInputEvent(Click(false));
            await Frames(6);
            var after = Snapshot();
            Check("split mode: clicking the line splits the edited road", AliveRoads() == roadsBefore + 1 && road.endIntersection != null
                && road.endIntersection != oldEnd && road.endIntersection.roads.Count == 2);
            Check("split mode: the editor stays on the first part", roadPanel.ActiveSelf() && Placer()?.GetRoad(false) == road
                && Placer()?.placementMode == ElementPlacer.RoadPlacer.PlacementMode.Split);

            InvokeBuilder("Undo");
            await Frames(6);
            Log((Compare("undo split mode click", before, Snapshot()) ? "OK   " : "FAIL ") + "undo split mode click");
            Check("split mode: the editor is reopened in split mode after undo", roadPanel.ActiveSelf() && Placer()?.placementMode == ElementPlacer.RoadPlacer.PlacementMode.Split);
            InvokeBuilder("Redo");
            await Frames(6);
            Log((Compare("redo split mode click", after, Snapshot()) ? "OK   " : "FAIL ") + "redo split mode click");

            var edited = Placer()?.GetRoad(false);
            roadPanel.SetPointPlacementMode((int)ElementPlacer.RoadPlacer.PlacementMode.None);
            await Frames(3);
            Check("split mode: the line is hidden in the other modes", edited != null && !GO.GetEnabled(edited.splitLine));
            InvokeBuilder("DeselectAndCloseCurrentPanel");
            await Frames(6);
        }

        static float DistanceXZ(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

        //the roads extended backwards: points added before the first one, connections made at the start
        async Task BackwardsTests() {
            UndoManager.Clear();
            await Frames(6);
            //away from the roads of the split tests
            Road a = null, b = null, c = null, f = null, d = null, e = null, g = null;
            ElementPlacer.RoadPlacer placerA = null;
            Vector3 P(Road r, int i) => GO.GetPosition(r.points[i < 0 ? r.points.Count + i : i]);
            bool Joins(Intersection i, params Road[] roads) => i != null && !i.IsDeleted() && i.roads.Count == roads.Length && System.Array.TrueForAll(roads, i.roads.Contains);
            Road NewRoad(params Vector3[] points) {
                var road = new ElementPlacer.RoadPlacer(M).GetRoad();
                foreach (var p in points) road.AddPoint(p);
                return road;
            }
            var steps = new List<(string name, System.Action action, System.Action check)>();

            steps.Add(("backwards: create roads", () => {
                a = NewRoad(new Vector3(-200, 0, 800), new Vector3(0, 0, 820), new Vector3(200, 0, 800));
                b = NewRoad(new Vector3(-900, 0, 600), new Vector3(-500, 0, 700));
                c = NewRoad(new Vector3(300, 0, 900), new Vector3(500, 0, 910), new Vector3(800, 0, 900));
                f = NewRoad(new Vector3(-900, 0, 980), new Vector3(-600, 0, 960), new Vector3(-300, 0, 980));
                placerA = new ElementPlacer.RoadPlacer(M);
                placerA.SetRoad(a);
            }, () => {
                Check("backwards: the roads are created", a.points.Count == 3 && b.points.Count == 2 && c.points.Count == 3 && f.points.Count == 3);
            }));

            steps.Add(("backwards: add a point before the first one", () => {
                placerA.Drawing.AddPoint(new Vector3(-350, 0, 780), 0);
            }, () => {
                Check("backwards: the new point is the first one, the old first one follows", a.points.Count == 4
                    && DistanceXZ(P(a, 0), new Vector3(-350, 0, 780)) < 0.01f && DistanceXZ(P(a, 1), new Vector3(-200, 0, 800)) < 0.01f
                    && a.startIntersection == null && a.endIntersection == null);
                Check("backwards: the new first point has no rotation", a.GetPointRotations()[0] == Vector2.Zero);
                var placer = new ElementPlacer.RoadPlacer(M);
                placer.SetRoad(a);
                var results = new List<int>();
                foreach (var (mode, target) in new (ElementPlacer.RoadPlacer.PlacementMode, StaticBody3D)[] {
                    (ElementPlacer.RoadPlacer.PlacementMode.Add, null), (ElementPlacer.RoadPlacer.PlacementMode.AddBeforeFirst, null),
                    (ElementPlacer.RoadPlacer.PlacementMode.Insert, a.points[2]), (ElementPlacer.RoadPlacer.PlacementMode.InsertBefore, a.points[2]),
                    (ElementPlacer.RoadPlacer.PlacementMode.InsertBefore, a.points[0]) }) {
                    placer.placementMode = mode;
                    results.Add(placer.GetNewPointIndex(target));
                }
                Log("  backwards: placer indices " + string.Join(" ", results));
                Check("backwards: add after last, add before first, insert after and before the selected point", string.Join(" ", results) == "-1 0 3 2 0");
            }));

            steps.Add(("backwards: insert a point before the selected one", () => {
                placerA.Drawing.AddPoint(new Vector3(-100, 0, 815), 2);
            }, () => {
                Check("backwards: the point is inserted before the selected one", a.points.Count == 5 && DistanceXZ(P(a, 2), new Vector3(-100, 0, 815)) < 0.01f
                    && DistanceXZ(P(a, 3), new Vector3(0, 0, 820)) < 0.01f);
            }));

            steps.Add(("backwards: connect the start to a road end", () => {
                placerA.Drawing.ConnectToHandle(b.points[b.points.Count - 1], true);
            }, () => {
                Check("backwards: the start of the road is on the intersection with the road end", a.points.Count == 6 && Joins(a.startIntersection, a, b)
                    && b.endIntersection == a.startIntersection && a.endIntersection == null && DistanceXZ(P(a, 0), new Vector3(-500, 0, 700)) < 0.01f);
                //refused, nothing changes
                placerA.Drawing.AddPoint(new Vector3(-600, 0, 760), 0);
                Check("backwards: no point is added before a start on an intersection", a.points.Count == 6);
                var placer = new ElementPlacer.RoadPlacer(M) { placementMode = ElementPlacer.RoadPlacer.PlacementMode.InsertBefore };
                placer.SetRoad(a);
                var canBeforeFirst = placer.CanInsertNextTo(a.points[0]);
                var canBeforeSecond = placer.CanInsertNextTo(a.points[1]);
                placer.placementMode = ElementPlacer.RoadPlacer.PlacementMode.Insert;
                var canAfterLast = placer.CanInsertNextTo(a.points[a.points.Count - 1]);
                Check("backwards: points are inserted before the selected one only inside the road", !canBeforeFirst && canBeforeSecond && canAfterLast);
            }));

            steps.Add(("backwards: remove the first point", () => {
                Operations.Roads.RemoveFirstPoint(a);
            }, () => {
                Check("backwards: the first point is removed, the start detached", a.points.Count == 5 && a.startIntersection == null
                    && DistanceXZ(P(a, 0), new Vector3(-350, 0, 780)) < 0.01f && (b.endIntersection == null || !b.endIntersection.roads.Contains(a)));
            }));

            var middle = Vector3.Zero;
            steps.Add(("backwards: connect the start to the middle of a road", () => {
                M.ShowIntersections(true);
                middle = f.splitLine.Resolve(new Vector3(-600, 0, 960)).Value.point;
                placerA.Drawing.ConnectToMiddle(f, middle, true);
            }, () => {
                Log("  backwards: " + a.points.Count + " points, start on " + (a.startIntersection?.roads.Count ?? 0) + " roads, first point "
                    + DistanceXZ(P(a, 0), middle) + " m from the split point");
                Check("backwards: the start of the road is on the middle of the other one", a.points.Count == 6 && Joins(a.startIntersection, a, f, LastRoad())
                    && f.endIntersection == a.startIntersection && a.endIntersection == null && DistanceXZ(P(a, 0), middle) < 0.5f);
            }));

            steps.Add(("backwards: start a road on a road end, then add a point before it", () => {
                var placer = new ElementPlacer.RoadPlacer(M);
                d = placer.GetRoad();
                placer.Drawing.ConnectToHandle(c.points[0], true);
                placer.Drawing.AddPoint(new Vector3(150, 0, 850), 0);
            }, () => {
                Check("backwards: the road ends on the road end it was started on", d.points.Count == 2 && Joins(d.endIntersection, d, c)
                    && c.startIntersection == d.endIntersection && d.startIntersection == null && DistanceXZ(P(d, -1), new Vector3(300, 0, 900)) < 0.01f
                    && DistanceXZ(P(d, 0), new Vector3(150, 0, 850)) < 0.01f);
                var placer = new ElementPlacer.RoadPlacer(M) { placementMode = ElementPlacer.RoadPlacer.PlacementMode.Insert };
                placer.SetRoad(d);
                Check("backwards: no point is inserted after an end on an intersection", !placer.CanInsertNextTo(d.points[1]));
            }));

            steps.Add(("backwards: start a road on the middle of a road, then add a point before it", () => {
                var placer = new ElementPlacer.RoadPlacer(M);
                e = placer.GetRoad();
                placer.Drawing.ConnectToMiddle(c, c.splitLine.Resolve(new Vector3(600, 0, 905)).Value.point, true);
                placer.Drawing.AddPoint(new Vector3(600, 0, 700), 0);
            }, () => {
                Check("backwards: the road ends on the middle of the road it was started on", e.points.Count == 2 && e.endIntersection != null
                    && e.endIntersection.roads.Count == 3 && c.endIntersection == e.endIntersection && e.startIntersection == null
                    && DistanceXZ(P(e, 0), new Vector3(600, 0, 700)) < 0.01f);
            }));

            steps.Add(("backwards: start a road on a road end, then connect a point before it", () => {
                var placer = new ElementPlacer.RoadPlacer(M);
                g = placer.GetRoad();
                placer.Drawing.ConnectToHandle(b.points[0], true);
                placer.Drawing.ConnectToHandle(f.points[0], true);
            }, () => {
                Check("backwards: the road starts on the second road end and ends on the first one", g.points.Count == 2 && Joins(g.startIntersection, g, f)
                    && Joins(g.endIntersection, g, b) && f.startIntersection == g.startIntersection && b.startIntersection == g.endIntersection
                    && DistanceXZ(P(g, 0), new Vector3(-900, 0, 980)) < 0.01f && DistanceXZ(P(g, -1), new Vector3(-900, 0, 600)) < 0.01f);
            }));

            await RunSteps(steps);
        }

        //the add before first mode of the road editor, with a real click on the terrain
        async Task BackwardsModeTest() {
            InvokeBuilder("DeselectAndCloseCurrentPanel");
            await Frames(6);
            //the free road of the backwards tests
            Road road = null;
            foreach (var r in M.roads) {
                if (ElementSnapshot.IsAlive(r) && r.points.Count == 2 && r.startIntersection == null && r.endIntersection != null
                    && DistanceXZ(GO.GetPosition(r.points[0]), new Vector3(150, 0, 850)) < 0.01f) road = r;
            }
            Check("backwards mode: a road with a free start", road != null);
            if (road == null) return;
            var roadPanel = Panel<EditorPanels.RoadEditorPanel>("roadEditorPanel");
            ElementPlacer.RoadPlacer Placer() => builder.terrainClick.modifier as ElementPlacer.RoadPlacer;
            builder.SelectObject(road, true, null);
            roadPanel.SetPointPlacementMode((int)ElementPlacer.RoadPlacer.PlacementMode.AddBeforeFirst);
            await Frames(6);
            Check("backwards mode: the editor is in add before first mode, the first point marked", Placer()?.placementMode == ElementPlacer.RoadPlacer.PlacementMode.AddBeforeFirst
                && road.markFirstPoint);

            var point = new Vector3(0, 0, 700);
            var cam = GetViewport().GetCamera3D();
            GO.SetPosition(cam, point + new Vector3(0, 250, -250));
            GO.LookAt(cam, point);
            await Frames(6);
            GetViewport().WarpMouse(cam.UnprojectPosition(GameEngine.Vector3.DecodePosition(point)));
            await Frames(6);
            var count = road.points.Count;
            var before = Snapshot();
            InputEventMouseButton Click(bool pressed) => new InputEventMouseButton {
                ButtonIndex = MouseButton.Left, Pressed = pressed, Position = GetViewport().GetMousePosition(), GlobalPosition = GetViewport().GetMousePosition()
            };
            Godot.Input.ParseInputEvent(Click(true));
            await Frames(2);
            Godot.Input.ParseInputEvent(Click(false));
            await Frames(6);
            var after = Snapshot();
            Check("backwards mode: clicking the terrain adds a point before the first one", road.points.Count == count + 1
                && DistanceXZ(GO.GetPosition(road.points[0]), point) < 1.0f && road.endIntersection != null);

            InvokeBuilder("Undo");
            await Frames(6);
            Log((Compare("undo backwards mode click", before, Snapshot()) ? "OK   " : "FAIL ") + "undo backwards mode click");
            Check("backwards mode: the editor is reopened in add before first mode after undo", roadPanel.ActiveSelf()
                && Placer()?.placementMode == ElementPlacer.RoadPlacer.PlacementMode.AddBeforeFirst);
            InvokeBuilder("Redo");
            await Frames(6);
            Log((Compare("redo backwards mode click", after, Snapshot()) ? "OK   " : "FAIL ") + "redo backwards mode click");

            //remove point removes the first point in this mode
            var edited = Placer()?.GetRoad(false);
            var first = edited == null ? Vector3.Zero : GO.GetPosition(edited.points[1]);
            count = edited?.points.Count ?? 0;
            typeof(EditorPanels.RoadEditorPanel).GetMethod("RemovePoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(roadPanel, null);
            await Frames(6);
            Check("backwards mode: remove point removes the first point", edited != null && edited.points.Count == count - 1
                && DistanceXZ(GO.GetPosition(edited.points[0]), first) < 0.01f && edited.endIntersection != null);
            InvokeBuilder("DeselectAndCloseCurrentPanel");
            await Frames(6);
            Check("backwards mode: the first point is no longer marked after closing the editor", edited != null && !edited.markFirstPoint);
        }

        //must run before SelectObject or the transform panel is used: selecting an object resets the gizmo's selection state
        async Task FirstRoadPlacementTests() {
            var panel = Panel<EditorPanels.RoadEditorPanel>("roadEditorPanel");
            var cam = GetViewport().GetCamera3D();
            GO.SetPosition(cam, new Vector3(0, 250, -250));
            GO.LookAt(cam, Vector3.Zero);
            await Frames(6);

            async Task<Vector3?> Click(Vector3 point) {
                GetViewport().WarpMouse(cam.UnprojectPosition(GameEngine.Vector3.DecodePosition(point)));
                await Frames(3);
                var mouse = GetViewport().GetMousePosition();
                var ray = GameEngine.Raycast.GetRayFromScreen(cam, mouse);
                var onGround = GameEngine.Raycast.DoRaycast(builder.helper, ray.origin, ray.direction, out var hit, 100000f, 1 << 3);
                InputEventMouseButton Event(bool pressed) => new InputEventMouseButton {
                    ButtonIndex = MouseButton.Left, Pressed = pressed, Position = mouse, GlobalPosition = mouse
                };
                Input.ParseInputEvent(Event(true));
                await Frames(2);
                Input.ParseInputEvent(Event(false));
                await Frames(6);
                return onGround ? hit.point : null;
            }

            for (int i = 0; i < 2; i++) {
                var label = "placement: road " + (i + 1);
                UndoManager.Record("create road", () => InvokeBuilder("CreateRoad"));
                await Frames(6);
                var placer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
                var road = placer.GetRoad(false);
                var z = i * 80;
                foreach (var x in new[] { -80, 0, 80 }) await Click(new Vector3(x, 0, z));
                Check(label + " is drawn with three terrain clicks", road.points.Count == 3);
                if (road.points.Count != 3) return;
                var middle = road.points[1];

                foreach (var mode in new[] { ElementPlacer.RoadPlacer.PlacementMode.Insert, ElementPlacer.RoadPlacer.PlacementMode.InsertBefore }) {
                    panel.SetPointPlacementMode((int)mode);
                    builder.gizmo.ClearTargets();
                    await Frames(3);
                    var count = road.points.Count;
                    await Click(GO.GetPosition(middle));
                    Check(label + " selects a control point in " + mode + " mode", builder.gizmo.mainTargetRoot == middle);
                    Check(label + " selection adds no point in " + mode + " mode", road.points.Count == count);
                    var index = road.points.IndexOf(middle) + (mode == ElementPlacer.RoadPlacer.PlacementMode.Insert ? 1 : 0);
                    //away from the gizmo axes; use the actual ray hit since the mouse is rounded to a screen pixel
                    var position = await Click(new Vector3(mode == ElementPlacer.RoadPlacer.PlacementMode.Insert ? 30 : -30, 0, z - 60));
                    Check(label + " inserts a point " + (mode == ElementPlacer.RoadPlacer.PlacementMode.Insert ? "after" : "before") + " the selected point", road.points.Count == count + 1
                        && position.HasValue && GO.GetPosition(road.points[index]).DistanceTo(position.Value) < 0.1f
                        && road.points[mode == ElementPlacer.RoadPlacer.PlacementMode.Insert ? index - 1 : index + 1] == middle);
                }
                InvokeBuilder("DeselectAndCloseCurrentPanel");
                await Frames(6);
                if (i == 0) {
                    builder.SelectObject(road, true, null);
                    panel.SetPointPlacementMode((int)ElementPlacer.RoadPlacer.PlacementMode.Insert);
                    await Frames(3);
                    await Click(GO.GetPosition(middle));
                    Check(label + " also selects points after reopening", builder.gizmo.mainTargetRoot == middle);

                    InvokeBuilder("EditCurSelectedObjectTransform");
                    await Frames(6);
                    var transformPanel = Panel<EditorPanels.TransformPropertiesEditorPanel>("transformPropertiesEditorPanel");
                    Check("placement: the transform panel locks point selection", transformPanel.ActiveSelf() && !builder.gizmo.allowDeselect);
                    await Click(GO.GetPosition(road.points[0]));
                    Check("placement: world clicks keep the point being edited in the transform panel", builder.gizmo.mainTargetRoot == middle);
                    typeof(EditorPanels.TransformPropertiesEditorPanel).GetMethod("EndHandleEditing",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(transformPanel, null);
                    await Frames(6);
                    await Click(GO.GetPosition(road.points[0]));
                    Check("placement: closing the transform panel restores point selection", panel.ActiveSelf() && builder.gizmo.mainTargetRoot == road.points[0]);
                    InvokeBuilder("DeselectAndCloseCurrentPanel");
                    await Frames(6);
                }
            }
        }

        async void RunSplit() {
            var lastCity = SettingsManager.Get("LastCity", "");
            try {
                await LoadNewCity(2048); //room for roads much longer than the size of their intersections (the default road type is 40 m wide)
                if (placementOnly) await FirstRoadPlacementTests();
                else {
                    await SplitTests();
                    await SplitLinesScreenshot();
                    await SplitModeTest();
                    await BackwardsTests();
                    await BackwardsModeTest();
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

        async void RunDrag() {
            var lastCity = SettingsManager.Get("LastCity", "");
            try {
                await LoadNewCity();
                await DragTests();
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            SettingsManager.Set("LastCity", lastCity);
            Log("finished with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "undo_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }

        async void RunDropdown() {
            var lastCity = SettingsManager.Get("LastCity", "");
            try {
                await LoadNewCity();
                await DropdownTests();
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            SettingsManager.Set("LastCity", lastCity);
            Log("finished with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "undo_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }

        async void RunHeight() {
            var lastCity = SettingsManager.Get("LastCity", "");
            var snapping = Snapping.Enabled;
            try {
                await LoadNewCity();
                await HeightTests();
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            Snapping.Enabled = snapping;
            SettingsManager.Set("LastCity", lastCity);
            Log("finished with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "undo_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }

        //the placement height of the road points: Shift + mouse movement raises it, a Shift tap or turning the projection on
        //again brings it back to the ground, other input while Shift is held does neither; G toggles the snapping
        async Task HeightTests() {
            var cam = GetViewport().GetCamera3D();
            void PlaceCamera() {
                GO.SetPosition(cam, new Vector3(0, 250, -250));
                GO.LookAt(cam, Vector3.Zero);
            }
            PlaceCamera();
            Snapping.Enabled = false;
            await Frames(6);
            Vector2 Screen(Vector3 p) => cam.UnprojectPosition(GameEngine.Vector3.DecodePosition(p));
            void Send(InputEvent @event) => Input.ParseInputEvent(@event);
            async Task MoveTo(Vector2 pos) {
                GetViewport().WarpMouse(pos);
                await Frames(2);
            }
            async Task Shift(bool pressed) {
                Send(new InputEventKey { Keycode = Godot.Key.Shift, PhysicalKeycode = Godot.Key.Shift, Pressed = pressed, ShiftPressed = pressed });
                await Frames(2);
            }
            async Task Key(Key key) {
                Send(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                await Frames(2);
                Send(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
                await Frames(2);
            }
            async Task Button(MouseButton button, bool shift = false) {
                var mouse = GetViewport().GetMousePosition();
                Send(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = mouse, GlobalPosition = mouse, ShiftPressed = shift });
                await Frames(2);
                Send(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = mouse, GlobalPosition = mouse, ShiftPressed = shift });
                await Frames(6);
            }
            //Shift held while the mouse moves up from the point on the ground
            async Task Raise(Vector3 from, float pixels, bool release = true) {
                var start = Screen(from);
                await MoveTo(start);
                await Shift(true);
                for (int k = 1; k <= 10; k++) await MoveTo(start + new Vector2(0, -pixels * k / 10));
                if (release) await Shift(false);
            }
            bool LineShown() {
                var line = (PlacementHeightLine)typeof(ActionHandlerManager).GetField("heightLine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                return line != null && line.enabled;
            }
            float Y(int i, Road r) => GO.GetPosition(r.points[i]).Y;

            UndoManager.Record("create road", () => InvokeBuilder("CreateRoad"));
            await Frames(6);
            var placer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
            await MoveTo(Screen(new Vector3(-80, 0, 0)));
            await Button(MouseButton.Left);
            var road = placer.GetRoad(false);
            Check("height: the first point is placed", road != null && road.points.Count == 1);
            if (road == null || road.points.Count != 1) return;
            var ground = Operations.Terrain.GetHeight(GO.GetPosition(road.points[0]));
            Check("height: by default points are placed on the ground (" + Y(0, road) + ", ground " + ground + ")", Mathf.Abs(Y(0, road) - ground) < 0.05f);
            UndoManager.Record("project", () => { UndoManager.Touch(road); road.state.SetBool("project", true); road.state.SetBool("projectAll", false); });
            await Frames(3);

            await Raise(new Vector3(0, 0, 0), 80, false);
            var height = PlacementHeight.Height;
            Check("height: moving the mouse with Shift raises the placement (" + height + ")", PlacementHeight.Raised && height > ground + 1);
            Check("height: the height line is shown", LineShown());
            await Button(MouseButton.Left, true);
            await Shift(false);
            Check("height: a click while dragging places a raised point", road.points.Count == 2 && Mathf.Abs(Y(1, road) - height) < 0.05f);
            Check("height: the raised point is on the vertical line of the drag", road.points.Count == 2
                && new Vector2(GO.GetPosition(road.points[1]).X, GO.GetPosition(road.points[1]).Z).Length() < 2);
            Check("height: placing a raised point turns off the projection", !road.state.Bool("project") && !road.state.Bool("projectAll"));
            Check("height: the placement stays raised after releasing Shift", PlacementHeight.Raised);

            await MoveTo(Screen(new Vector3(80, height, 0)));
            Check("height: the height line follows the cursor", LineShown());
            await Button(MouseButton.Left);
            Check("height: a click after the drag places a point at the same height", road.points.Count == 3 && Mathf.Abs(Y(2, road) - height) < 0.05f);

            //panning the camera with Shift and the wheel (scheme B) is not a tap
            await Shift(true);
            await Button(MouseButton.WheelUp, true);
            await Shift(false);
            PlaceCamera();
            await Frames(3);
            Check("height: Shift + wheel keeps the height", PlacementHeight.Raised && PlacementHeight.Height == height);
            //a key while Shift is held is not a tap either
            await Shift(true);
            await Key(Godot.Key.Down);
            await Shift(false);
            Check("height: Shift + another key keeps the height", PlacementHeight.Raised);

            await Shift(true);
            await Shift(false);
            Check("height: a Shift tap brings back the ground placement", !PlacementHeight.Raised && !LineShown());
            await MoveTo(Screen(new Vector3(160, 0, 0)));
            await Button(MouseButton.Left);
            Check("height: then points are placed on the ground", road.points.Count == 4 && Mathf.Abs(Y(3, road) - ground) < 0.05f);

            await Key(Godot.Key.G);
            Check("height: G turns on the snapping", Snapping.Enabled);
            await Raise(new Vector3(160, 0, 0), 77);
            Check("height: with snapping the height is a whole number (" + PlacementHeight.Height + ")", PlacementHeight.Raised
                && PlacementHeight.Height == Mathf.Round(PlacementHeight.Height) && PlacementHeight.Height > ground);
            await Key(Godot.Key.G);
            Check("height: G turns off the snapping", !Snapping.Enabled);

            //turning on the projection in the panel
            var panel = Panel<EditorPanels.RoadEditorPanel>("roadEditorPanel");
            panel.Update();
            await Frames(3);
            CheckBox projectBox = null;
            foreach (var node in builder.FindChildren("*", nameof(CheckBox), true, false)) {
                var box = (CheckBox)node;
                if (box.Text == StringManager.Get("RD_PROJECT_TO_GROUND") && box.IsVisibleInTree()) projectBox = box;
            }
            Check("height: the panel shows the projection turned off", projectBox != null && !projectBox.ButtonPressed);
            if (projectBox != null) {
                projectBox.ButtonPressed = true;
                await Frames(3);
                Check("height: turning on the projection brings back the ground placement", !PlacementHeight.Raised && road.state.Bool("project"));
            }

            //the transform panel of a point keeps the placement session
            await Raise(new Vector3(0, 0, 60), 60);
            builder.gizmo.ClearTargets();
            builder.gizmo.AddTarget(road.points[0]);
            await Frames(3);
            InvokeBuilder("EditCurSelectedObjectTransform");
            await Frames(6);
            var transformPanel = Panel<EditorPanels.TransformPropertiesEditorPanel>("transformPropertiesEditorPanel");
            typeof(EditorPanels.TransformPropertiesEditorPanel).GetMethod("EndHandleEditing",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(transformPanel, null);
            await Frames(6);
            Check("height: editing a point with M keeps the height", PlacementHeight.Raised && builder.terrainClick.modifier == placer);
            UndoManager.Record("create road", () => InvokeBuilder("CreateRoad"));
            await Frames(6);
            Check("height: creating another element keeps the height", PlacementHeight.Raised && LineShown());
            var line = (PlacementHeightLine)typeof(ActionHandlerManager).GetField("heightLine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            var lineMesh = (ImmediateMesh)typeof(PlacementHeightLine).GetField("mesh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(line);
            var quads = lineMesh.GetSurfaceCount() == 1 ? ((Vector3[])lineMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]).Length / 6 : 0;
            Check("height: the line has height ticks (" + quads + " quads)", quads > 1);
            await Raise(new Vector3(0, 0, 60), 60);
            InvokeBuilder("DeselectAndCloseCurrentPanel");
            await Frames(6);
            Check("height: ending the placement brings back the ground placement", !PlacementHeight.Raised);

            //with the terrain hidden there is nothing to click: the zero height plane
            builder.helper.SetTerrainHidden(true);
            try {
                UndoManager.Record("create road", () => InvokeBuilder("CreateRoad"));
                await Frames(6);
                placer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
                //a new road projected to the ground (e.g. by its preset)
                UndoManager.Record("project", () => placer.GetRoad().state.SetBool("project", true));
                await MoveTo(Screen(new Vector3(-80, 0, -120)));
                await Button(MouseButton.Left);
                var hiddenRoad = placer.GetRoad(false);
                Check("height: with the terrain hidden points are placed at zero height", hiddenRoad != null && hiddenRoad.points.Count == 1 && Mathf.Abs(Y(0, hiddenRoad)) < 0.05f);
                Check("height: the first point with the terrain hidden turns off the projection", hiddenRoad != null && !hiddenRoad.state.Bool("project"));
                //turned on again, the next points keep it
                UndoManager.Record("project", () => { UndoManager.Touch(hiddenRoad); hiddenRoad.state.SetBool("project", true); });
                await MoveTo(Screen(new Vector3(-40, 0, -120)));
                await Button(MouseButton.Left);
                Check("height: the projection turned on again with the terrain hidden is kept", hiddenRoad.points.Count == 2 && hiddenRoad.state.Bool("project"));
                UndoManager.Record("project", () => { UndoManager.Touch(hiddenRoad); hiddenRoad.state.SetBool("project", false); });
                InvokeBuilder("DeselectAndCloseCurrentPanel");
                await Frames(6);
                //a road started on the end of another one (an intersection, not the ground) turns it off as well
                var hiddenEnd = GO.GetPosition(hiddenRoad.points[1]);
                UndoManager.Record("create road", () => InvokeBuilder("CreateRoad"));
                await Frames(6);
                placer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
                UndoManager.Record("project", () => placer.GetRoad().state.SetBool("project", true));
                await MoveTo(Screen(hiddenEnd));
                await Button(MouseButton.Left);
                await MoveTo(Screen(new Vector3(-40, 0, -80)));
                await Button(MouseButton.Left);
                var connectedRoad = placer.GetRoad(false);
                Check("height: a road started on an intersection with the terrain hidden turns off the projection", connectedRoad != null
                    && connectedRoad.points.Count == 2 && connectedRoad.startIntersection != null && !connectedRoad.state.Bool("project"));
                InvokeBuilder("DeselectAndCloseCurrentPanel");
                await Frames(6);
            } finally {
                builder.helper.SetTerrainHidden(false);
            }

            //the raised point and the projection turned off are one undo step (the undo closes the editor and may replace
            //the road, so it is looked up again)
            UndoManager.Record("create road", () => InvokeBuilder("CreateRoad"));
            await Frames(6);
            placer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
            await MoveTo(Screen(new Vector3(-80, 0, 120)));
            await Button(MouseButton.Left);
            await MoveTo(Screen(new Vector3(-40, 0, 120)));
            await Button(MouseButton.Left);
            var undoRoad = placer.GetRoad(false);
            UndoManager.Record("project", () => { UndoManager.Touch(undoRoad); undoRoad.state.SetBool("project", true); });
            await Frames(3);
            await Raise(new Vector3(0, 0, 120), 80, false);
            height = PlacementHeight.Height;
            await Button(MouseButton.Left, true);
            await Shift(false);
            Check("height: a raised point on another road", undoRoad.points.Count == 3 && !undoRoad.state.Bool("project"));
            Road Latest() {
                Road res = null;
                foreach (var r in M.roads) if (ElementSnapshot.IsAlive(r) && r.points.Count > 0 && Mathf.Abs(GO.GetPosition(r.points[0]).Z - 120) < 2) res = r;
                return res;
            }
            InvokeBuilder("Undo");
            await Frames(6);
            var undone = Latest();
            Check("height: undo removes the point and turns the projection on again", undone != null && undone.points.Count == 2 && undone.state.Bool("project"));
            InvokeBuilder("Redo");
            await Frames(6);
            var redone = Latest();
            Check("height: redo", redone != null && redone.points.Count == 3 && !redone.state.Bool("project") && Mathf.Abs(Y(2, redone) - height) < 0.05f);
        }

        //the item list of a road editor dropdown, opened with a real click: arrow keys move through it, the mouse wheel over it
        //does not move the camera
        async Task DropdownTests() {
            var road = new ElementPlacer.RoadPlacer(M).GetRoad();
            road.AddPoint(new Vector3(0, 0, 0));
            road.AddPoint(new Vector3(60, 0, 0));
            await Frames(6);
            builder.SelectObject(road, true, null);
            await Frames(6);
            UndoManager.FinalizePending();
            OptionButton dropdown = null;
            foreach (var node in builder.FindChildren("*", nameof(OptionButton), true, false)) {
                var button = (OptionButton)node;
                if (button.IsVisibleInTree() && !button.Disabled && button.ItemCount >= 3) { dropdown = button; break; }
            }
            Check("the road editor has a dropdown with at least 3 items", dropdown != null);
            if (dropdown == null) return;
            var popup = dropdown.GetPopup();
            void Send(InputEvent @event) => Input.ParseInputEvent(@event);
            async Task Click(Vector2 pos) {
                Input.WarpMouse(pos);
                await Frames(2);
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = pos, GlobalPosition = pos, ButtonMask = MouseButtonMask.Left });
                await Frames(1);
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = pos, GlobalPosition = pos });
                await Frames(4);
            }
            async Task Key(Key key) {
                Send(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                await Frames(2);
                Send(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
                await Frames(2);
            }
            dropdown.Select(0);
            await Click(dropdown.GetGlobalRect().GetCenter());
            Log("dropdown '" + dropdown.GetItemText(0) + "' (" + dropdown.ItemCount + " items), list open " + popup.Visible + ", focused item " + popup.GetFocusedItem()
                + ", focus owner " + GetViewport().GuiGetFocusOwner()?.Name);
            Check("a click opens the item list", popup.Visible);
            if (!popup.Visible) return;
            await Key(Godot.Key.Down);
            var focused1 = popup.GetFocusedItem();
            await Key(Godot.Key.Down);
            var focused2 = popup.GetFocusedItem();
            await Key(Godot.Key.Up);
            var focused3 = popup.GetFocusedItem();
            Log("focused item after down, down, up: " + focused1 + ", " + focused2 + ", " + focused3);
            Check("the arrow keys move through the items", focused1 >= 0 && focused2 == focused1 + 1 && focused3 == focused1);
            await Key(Godot.Key.Delete);
            Check("delete on the item list does not delete the edited road", M.roads.Contains(road) && popup.Visible);
            await Key(Godot.Key.Down);
            var count = UndoCount();
            await Key(Godot.Key.Enter);
            await Frames(4);
            UndoManager.FinalizePending();
            Log("selected after enter: " + dropdown.Selected + ", list open " + popup.Visible + ", " + (UndoCount() - count) + " new entries");
            Check("enter selects the focused item and closes the list", dropdown.Selected == focused2 && !popup.Visible);

            //the wheel over the open list scrolls the list, not the camera
            var camera = GetViewport().GetCamera3D();
            await Click(dropdown.GetGlobalRect().GetCenter());
            Check("a second click opens the item list again", popup.Visible);
            if (!popup.Visible) return;
            var cameraTransform = camera.GlobalTransform;
            var listPos = popup.Position + popup.Size / 2;
            Input.WarpMouse(listPos);
            await Frames(3);
            for (int i = 0; i < 5; i++) {
                foreach (var pressed in new[] { true, false }) {
                    Send(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = pressed, Position = listPos, GlobalPosition = listPos, Factor = 1 });
                    await Frames(1);
                }
            }
            await Frames(4);
            Log("wheel at " + listPos + " over the list " + new Rect2I(popup.Position, popup.Size) + ", list open " + popup.Visible);
            Check("the wheel over the item list does not move the camera", camera.GlobalTransform.IsEqualApprox(cameraTransform));
            await Key(Godot.Key.Escape);
            Check("escape closes the item list", !popup.Visible);
            Check("escape on the item list leaves the road editor open", Panel<EditorPanels.RoadEditorPanel>("roadEditorPanel").IsOpen());
        }

        //the middle mouse drag on the numeric fields of the road editor, with real mouse events through the viewport
        async Task DragTests() {
            var road = new ElementPlacer.RoadPlacer(M).GetRoad();
            road.AddPoint(new Vector3(0, 0, 0));
            road.AddPoint(new Vector3(60, 0, 0));
            road.AddPoint(new Vector3(120, 0, 10));
            await Frames(6);
            builder.SelectObject(road, true, null);
            await Frames(6);
            UndoManager.FinalizePending();
            UndoManager.Clear();
            var roadPanel = Panel<EditorPanels.RoadEditorPanel>("roadEditorPanel");

            LineEdit FindField(string fieldName, EditorPanel panel = null, string axis = "inputField") {
                panel ??= roadPanel;
                var pages = (List<EditorPanelPage>)typeof(EditorPanel).GetField("pages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(panel);
                for (int i = 0; i < pages.Count; i++) {
                    var elements = (Dictionary<EditorPanelElement, FieldStatus>)Field(pages[i], "fieldElements");
                    foreach (var pair in elements) {
                        if (pair.Value.fieldName != fieldName) continue;
                        panel.ShowPage(i);
                        return (LineEdit)Field(pair.Key, axis);
                    }
                }
                return null;
            }
            object Value(string fieldName) => FieldStatus.GetValue(road, fieldName);
            void Send(InputEvent @event) => Input.ParseInputEvent(@event);
            async Task Drag(LineEdit field, Vector2 motion, int count, bool shift = false, bool cancel = false, bool ctrl = false) {
                var pos = field.GetGlobalRect().GetCenter();
                Input.WarpMouse(pos);
                await Frames(2);
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true, Position = pos, GlobalPosition = pos, ButtonMask = MouseButtonMask.Middle });
                await Frames(2);
                for (int i = 0; i < count; i++) {
                    Send(new InputEventMouseMotion { Relative = motion, Position = pos, GlobalPosition = pos, ButtonMask = MouseButtonMask.Middle, ShiftPressed = shift, CtrlPressed = ctrl });
                    await Frames(1);
                }
                await Frames(2);
                if (cancel) {
                    Send(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
                    await Frames(2);
                    Send(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
                }
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false, Position = pos, GlobalPosition = pos });
                await Frames(6);
                UndoManager.FinalizePending();
            }

            //integer field, dragged right: one undo step for the whole drag
            var segments = FindField("state.properties.segments");
            Check("the road editor has the segments field", segments != null);
            if (segments == null) return;
            await Frames(3);
            var segments0 = System.Convert.ToInt32(Value("state.properties.segments"));
            var camera = GetViewport().GetCamera3D();
            var cameraTransform = camera.GlobalTransform;
            var before = Snapshot();
            var count = UndoCount();
            var pressed = false;
            var pos0 = segments.GetGlobalRect().GetCenter();
            Input.WarpMouse(pos0);
            await Frames(2);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true, Position = pos0, GlobalPosition = pos0, ButtonMask = MouseButtonMask.Middle });
            await Frames(2);
            pressed = NumericFieldDrag.IsDragging;
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false, Position = pos0, GlobalPosition = pos0 });
            await Frames(3);
            Check("a middle press on a numeric field starts a drag, the release ends it", pressed && !NumericFieldDrag.IsDragging);
            Check("a drag without motion records nothing (" + (UndoCount() - count) + " new entries)", UndoCount() == count);
            await Drag(segments, new Vector2(10, 0), 10);
            var segments1 = System.Convert.ToInt32(Value("state.properties.segments"));
            Log("segments: " + segments0 + " -> " + segments1 + " (field '" + segments.Text + "')");
            Check("dragging right increases an integer field and its value", segments1 > segments0 && segments.Text == segments1.ToString());
            Check("the drag is a single undo step (" + (UndoCount() - count) + " new entries)", UndoCount() == count + 1);
            Check("the mouse is visible again after the drag", Input.MouseMode == Input.MouseModeEnum.Visible);
            var after = Snapshot();
            UndoManager.Undo();
            await Frames(6);
            Compare("undo of the drag", before, Snapshot());
            Check("undo of the drag restores the value", System.Convert.ToInt32(Value("state.properties.segments")) == segments0);
            UndoManager.Redo();
            await Frames(6);
            Compare("redo of the drag", after, Snapshot());

            //two drags are two steps
            count = UndoCount();
            await Drag(segments, new Vector2(0, -10), 5); //up: positive too
            var segments2 = System.Convert.ToInt32(Value("state.properties.segments"));
            await Drag(segments, new Vector2(-10, 0), 5);
            var segments3 = System.Convert.ToInt32(Value("state.properties.segments"));
            Log("segments: " + segments1 + " -> " + segments2 + " -> " + segments3);
            Check("dragging up increases, dragging left decreases", segments2 > segments1 && segments3 < segments2);
            Check("two drags are two undo steps (" + (UndoCount() - count) + " new entries)", UndoCount() == count + 2);

            //escape restores the starting value and records nothing
            count = UndoCount();
            before = Snapshot();
            await Drag(segments, new Vector2(10, 0), 5, cancel: true);
            Log("segments after the canceled drag: " + Value("state.properties.segments") + " (field '" + segments.Text + "')");
            Check("escape restores the value of the field", System.Convert.ToInt32(Value("state.properties.segments")) == segments3 && segments.Text == segments3.ToString());
            Check("a canceled drag records nothing (" + (UndoCount() - count) + " new entries)", UndoCount() == count);
            Compare("canceled drag", before, Snapshot());
            Check("escape during a drag leaves the road editor open", roadPanel.IsOpen());
            Check("the drags do not move the camera", camera.GlobalTransform.IsEqualApprox(cameraTransform));

            //decimal field: the step is proportional to the value, Ctrl makes it 10 times smaller
            var size = FindField("state.properties.endCrosswalkSize");
            Check("the road editor has the end crosswalk size field", size != null);
            if (size == null) return;
            await Frames(3);
            float Size() => System.Convert.ToSingle(Value("state.properties.endCrosswalkSize"));
            var size0 = Size();
            await Drag(size, new Vector2(10, 0), 5);
            var size1 = Size();
            await Drag(size, new Vector2(10, 0), 5, ctrl: true);
            var size2 = Size();
            Log("end crosswalk size: " + size0 + " -> " + size1 + " -> " + size2 + " (field '" + size.Text + "')");
            Check("dragging right increases a decimal field", size1 > size0);
            Check("with Ctrl the change is about 10 times smaller", size2 > size1 && Mathf.Abs((size1 - size0) / (size2 - size1) - 10) < 3);
            await Drag(size, new Vector2(10, 0), 3, shift: true);
            var size3 = Size();
            await Drag(size, new Vector2(10, 0), 12, shift: true);
            var size4 = Size();
            Log("end crosswalk size with Shift: " + size2 + " -> " + size3 + " -> " + size4 + " (field '" + size.Text + "')");
            Check("with Shift the value snaps to the nearest integer", size3 == Mathf.Round(size3) && size4 == Mathf.Round(size4) && size.Text == size4.ToString());
            Check("with Shift the snapped value moves on from one integer to the next", size4 > size3);
            await Drag(size, new Vector2(-10, 0), 100);
            Log("end crosswalk size after dragging left: " + Size() + " (field '" + size.Text + "')");
            Check("dragging left goes below zero", Size() < 0);

            //transform fields: the same motion gives the same offset near zero and far from the origin
            var mesh = MeshInstance.Create("", Vector3.Zero, M.GetMeshContainer(),
                Operations.Meshes.NewSettings(new List<string>(CityElements.Types.Parsers.TypeParser.GetMeshInstanceTypes().Keys)[0]));
            M.AddMesh(mesh);
            builder.SelectObject(mesh, true, null);
            await Frames(6);
            var transformPanel = Panel<EditorPanels.TransformPropertiesEditorPanel>("transformPropertiesEditorPanel");
            foreach (var start in new[] { 0f, 5000f, -2500f }) {
                GO.SetPosition(mesh, new Vector3(start, 0, 0));
                transformPanel.Refresh();
                await Frames(3);
                var x = FindField("transform.position", transformPanel, "xField");
                await Drag(x, new Vector2(10, 0), 3);
                Check("position drag adds 3 units starting at " + start, Mathf.Abs(GO.GetPosition(mesh).X - start - 3) < 0.001f);
                await Drag(x, new Vector2(-10, 0), 3);
                Check("position drag reverses exactly starting at " + start, Mathf.Abs(GO.GetPosition(mesh).X - start) < 0.001f);
            }
            var position = FindField("transform.position", transformPanel, "xField");
            var position0 = GO.GetPosition(mesh).X;
            await Drag(position, new Vector2(10, 0), 3, ctrl: true);
            Check("Ctrl gives 0.01 units per pixel on position", Mathf.Abs(GO.GetPosition(mesh).X - position0 - 0.3f) < 0.001f);
            await Drag(position, new Vector2(3, 0), 3, shift: true);
            Check("Shift snaps a position drag to whole units", GO.GetPosition(mesh).X == Mathf.Round(GO.GetPosition(mesh).X));
            position0 = GO.GetPosition(mesh).X;
            count = UndoCount();
            await Drag(position, new Vector2(10, 0), 3, cancel: true);
            Check("cancel restores a fixed-rate position drag without an undo entry", GO.GetPosition(mesh).X == position0 && UndoCount() == count);

            foreach (var start in new[] { 0f, 120f, -120f }) {
                GO.SetRotation(mesh, GameEngine.Quaternion.Euler(0, start, 0));
                transformPanel.Refresh();
                await Frames(3);
                var yaw = FindField("transform.rotation", transformPanel, "yField");
                await Drag(yaw, new Vector2(10, 0), 3);
                var expected = GameEngine.Quaternion.Euler(0, start + 15, 0);
                Check("rotation drag adds 15 degrees starting at " + start, GO.GetRotationQuaternion(mesh).AngleTo(expected) < 0.001f);
            }

            builder.SelectObject(road, true, null);
            builder.gizmo.ClearTargets();
            builder.gizmo.AddTarget(road.points[1]);
            InvokeBuilder("EditCurSelectedObjectTransform");
            await Frames(6);
            foreach (var axis in new[] { "yaw", "roll" }) {
                var field = FindField("rotation." + axis, transformPanel);
                if (!field.Editable) continue; //the core determines which axes are allowed
                foreach (var start in new[] { 0f, 30f }) {
                    road.SetPointRotation(1, axis == "yaw" ? new Vector2(start, 0) : new Vector2(0, start));
                    transformPanel.Refresh();
                    await Frames(3);
                    await Drag(field, new Vector2(10, 0), 3);
                    var angles = road.GetPointRotations()[1];
                    Check("road point " + axis + " drag adds 15 degrees starting at " + start,
                        Mathf.Abs((axis == "yaw" ? angles.X : angles.Y) - start - 15) < 0.001f);
                }
            }
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
                        mesh = MeshInstance.Create("", new Vector3(-60, 0, 60), M.GetMeshContainer(),
                            Operations.Meshes.NewSettings(new List<string>(CityElements.Types.Parsers.TypeParser.GetMeshInstanceTypes().Keys)[0]));
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

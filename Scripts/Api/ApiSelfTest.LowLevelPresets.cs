using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    public partial class ApiSelfTest {
        async Task LowLevelPresetWorkflow() {
            const string category = "lowLevel/decal";
            const string presetName = "Preset workflow test";
            var panel = builder.lowLevelEditorPanel;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, flags).GetValue(obj);
            ObjectState Draft() => Field<ObjectState>(panel, "draft");
            void Start(string type = "decal", string group = "CREATE_LOW_LEVEL_ELEMENT") =>
                History.UndoManager.Record("start low-level creation", () => CallBuilder<object>("StartLowLevelCreation", type, group));
            object PresetControls() {
                var elements = (IDictionary)typeof(EditorPanel).GetField("complexElements", flags).GetValue(panel);
                var selector = elements.Values.Cast<object>().First(x => x.GetType().Name == "PresetSelector");
                var dropdowns = Field<IDictionary>(selector, "dropdowns");
                return ((IList)dropdowns[category])[0];
            }
            EditorPanelElements.Dropdown PresetDropdown() => Field<EditorPanelElements.Dropdown>(PresetControls(), "dropdown");
            void ClickControl(string name) => Field<Godot.Button>(Field<EditorPanelElements.Button>(PresetControls(), name), "button")
                .EmitSignal(BaseButton.SignalName.Pressed);
            void LoadPreset(string name) {
                var names = PresetManager.GetNames(category);
                PresetDropdown().SetValue(Array.IndexOf(names, name) + 1);
            }
            var expected = Operations.LowLevelPresets.DefaultPreset("decal");
            var initial = (await Call("presets.get", new { category, name = "Default" })).ToObject<ObjectState>();
            Check("decal Default uses SF tram rails, 2 metres and Bezier", initial.Str("texture") == "texture/r4i_rails_f.tex"
                && initial.Float("width") == 2 && initial.Int("curveType") == 0);
            Check("template and fallback defaults agree", initial.Equals(expected));
            foreach (var template in new[] { "SF", "London" }) {
                var json = JObject.Parse(File.ReadAllText(Path.Combine("Files/cores/MidtownMadness2/templates", template, "presets.json")));
                Check(template + " ships the same decal Default", json["presets"][category][0].ToObject<ObjectState>().Equals(expected));
            }
            var count = M.lowLevelElements.Count;
            var before = await Call("history.state");
            Start();
            await Frames(3);
            Check("creation immediately binds properties to a standalone draft", panel.ActiveSelf() && Draft() != null
                && builder.lowLevelPlacer.GetElement() == null && M.lowLevelElements.Count == count);
            Check("draft exposes presets and disables point placement", PresetDropdown() != null
                && !Field<EditorPanelElements.Dropdown>(panel, "placementDropdown").GetInteractable());
            var pages = (IList)typeof(EditorPanel).GetField("pages", flags).GetValue(panel);
            var fields = pages.Cast<object>().SelectMany(page => Field<Dictionary<EditorPanelElement, FieldStatus>>(page, "fieldElements"));
            var widthField = fields.First(p => p.Value.fieldName == "state.properties.width");
            var input = Field<LineEdit>(widthField.Key, "inputField");
            input.EmitSignal(LineEdit.SignalName.EditingToggled, true);
            input.Text = "3.5";
            input.EmitSignal(LineEdit.SignalName.EditingToggled, false);
            Check("property controls edit the draft before placement", Draft().Float("width") == 3.5f);
            panel.Hide(true);
            panel.Hide(false);
            Check("temporary panel hiding preserves draft edits", Draft().Float("width") == 3.5f);
            var savedDraft = panel.CaptureCreationState();
            var reopen = CallBuilder<Func<EditorPanel, EditorPanel>>("CaptureRootReopen", panel);
            builder.CloseAllPanels();
            reopen?.Invoke(null);
            Check("reopening the creation panel preserves its draft", Draft()?.Equals(savedDraft) == true);
            await Frames(2);
            Screenshot("lowlevel_draft.png");
            builder.CloseAllPanels();
            await Frames(2);
            Check("cancelling a draft creates no city elements", M.lowLevelElements.Count == count);
            Same("draft edits and cancellation create no undo entries", before, await Call("history.state"));

            Start(null, "CREATE_PSDL_ELEMENT");
            await Frames(2);
            var grouped = Field<List<string>>(panel, "creatableTypes");
            var typeDropdown = Field<EditorPanelElements.Dropdown>(panel, "createTypeDropdown");
            Check("grouped creation shows a type dropdown and a typed draft", typeDropdown != null && Draft() != null && !grouped.Contains("decal"));
            if (grouped.Count > 1) {
                var index = (typeDropdown.GetIndex() + 1) % grouped.Count;
                var next = grouped[index];
                typeDropdown.SetValue(index);
                await Frames(3);
                Check("switching grouped type rebuilds properties without placing anything", Draft().Str("type") == next && M.lowLevelElements.Count == count);
            }
            builder.CloseAllPanels();
            Start();
            Draft().SetFloat("width", 5);
            var draftPages = (IList)typeof(EditorPanel).GetField("pages", flags).GetValue(panel);
            var saveButton = draftPages.Cast<EditorPanelPage>().SelectMany(p => p.panel.FindChildren("*", "Button", true, false))
                .OfType<Godot.Button>().First(b => b.Text == StringManager.Get("SAVE_PRESET"));
            saveButton.EmitSignal(BaseButton.SignalName.Pressed);
            var popup = Field<InputFieldPopup>(builder, "inputFieldPopup");
            var presetInput = Field<LineEdit>(popup, "input");
            presetInput.Text = "UI draft preset";
            presetInput.EmitSignal(LineEdit.SignalName.TextSubmitted, "UI draft preset");
            builder.CloseAllPanels();
            Start();
            Check("saving a preset from a draft remembers it without placement", Draft().Name == "UI draft preset" && Draft().Float("width") == 5
                && M.lowLevelElements.Count == count);
            Draft().SetFloat("width", 9);
            typeof(EditorPanel).GetMethod("ReadCurValues", flags).Invoke(panel, null);
            ClickControl("resetButton");
            Check("Reset restores the selected preset's saved properties", Draft().Float("width") == 5);
            ClickControl("unlinkButton");
            Check("Unlink keeps draft properties and removes the preset link", Draft().Name == "" && Draft().Float("width") == 5);
            builder.CloseAllPanels();
            PresetManager.DeletePreset("UI draft preset", category);
            Start(PropPath.TypeId);
            await Frames(2);
            History.UndoManager.Record("place prop path draft", () => builder.lowLevelPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(700, 0, 700) }));
            Check("the shared prop-path placer still creates its default point layout", builder.lowLevelPlacer.GetElement() is PropPath path && path.GetAllPoints().Count > 0);
            var pathId = History.ElementRegistry.GetId(builder.lowLevelPlacer.GetElement());
            await Call("propPaths.delete", new { element = pathId });

            Start();
            Draft().SetFloat("width", 4);
            History.UndoManager.Record("place preset draft", () => builder.lowLevelPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(650, 0, 650) }));
            var placed = builder.lowLevelPlacer.GetElement();
            GameEngine.GameObject.SetName(placed, "Preset workflow placed");
            var placedId = History.ElementRegistry.GetId(placed);
            Check("first click applies draft settings and continues the spline", placed.state.Float("width") == 4 && placed.state.Int("curveType") == 0
                && placed.GetAllPoints().Count == 1 && builder.lowLevelPlacer.placementMode == ElementPlacer.LowLevelPlacer.PlacementMode.AppendPoint);
            foreach (var position in new[] { new Vector3(680, 0, 665), new Vector3(650, 0, 695) }) {
                History.UndoManager.Record("draw Bezier decal", () => builder.lowLevelPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = position }));
            }
            await Call("view.camera.set", new { position = new[] { 670, 80, 720 }, target = new[] { 660, 0, 670 } });
            await Frames(3);
            Check("manually placed Bezier decals build a curved strip", placed.splineData?.sections.Length > 3 && placed.generator.DrawsMesh());
            Screenshot("lowlevel_bezier_decal.png");
            await Call("state.saveAsPreset", new { @ref = placedId, name = presetName });
            Start();
            LoadPreset(presetName);
            Draft().SetFloat("width", 9);
            builder.CloseAllPanels();
            Start();
            Check("new creation reuses saved preset values rather than unsaved edits", Draft().Name == presetName && Draft().Float("width") == 4);
            var alternate = (ObjectState)expected.Clone();
            alternate.SetInt("curveType", 2);
            PresetManager.SavePreset(alternate, category);
            builder.CloseAllPanels();
            Start();
            LoadPreset("Default");
            Check("loading a preset refreshes the spline mode control", Draft().Int("curveType") == 2
                && Field<EditorPanelElements.Dropdown>(panel, "splineTypeDropdown").GetIndex() == 2);
            PresetManager.SavePreset(expected, category);
            builder.CloseAllPanels();

            builder.EditLowLevelPresets("decal");
            await Frames(3);
            LoadPreset(presetName);
            Check("preset management edits a draft without a city element", panel.ActiveSelf() && Draft().Name == presetName && panel.GetElement() == null);
            Check("preset management exposes deletion", Field<EditorPanelElements.Button>(PresetControls(), "deleteButton").GetInteractable());
            await Frames(2);
            Screenshot("lowlevel_preset_management.png");
            builder.CloseAllPanels();
            Start();
            LoadPreset(presetName);
            builder.CloseAllPanels();

            var peerId = (await Call("lowLevel.create", new { type = "decal", name = "Preset workflow peer", points = new { spline = new object[] {
                new { terrainPoint = History.ElementRegistry.GetId(placed.GetAllPoints()[0]) }, new { position = new[] { 670, 0, 650 } } } } })).Value<int>();
            LowLevelElement Element(int id) => History.UndoManager.GetAliveElement(id) as LowLevelElement;
            await Call("lowLevel.attachPoint", new { element = peerId, path = "spline.1", column = History.ElementRegistry.GetId(Element(placedId).GetAllPoints()[1]) });
            var points = Element(peerId).GetAllPoints().ToArray();
            var anchorPosition = points[1].anchor.GetPoint();
            await CheckUndo("applying a low-level preset", () => Call("state.applyPreset", new { @ref = peerId, name = presetName }));
            Check("applying a preset preserves shared point identities and attachments", Element(peerId).GetAllPoints().SequenceEqual(points)
                && Element(placedId).GetAllPoints()[0] == points[0] && points[1].anchor is ColumnAnchor
                && points[1].anchor.GetPoint().DistanceTo(anchorPosition) < 0.001f);
            await Call("presets.save", new { category, name = presetName, values = new { type = "decal", width = 6, curveType = 0 } });
            await CheckUndo("syncing low-level presets", () => Call("presets.sync", new { category, name = presetName }));
            Check("sync updates all linked decals", Element(placedId).state.Float("width") == 6 && Element(peerId).state.Float("width") == 6);
            builder.SelectObject(Element(peerId), true, null);
            Check("partial API presets compare equal after application", Field<EditorPanelElements.Button>(PresetControls(), "syncButton").GetInteractable()
                && !Field<EditorPanelElements.Button>(PresetControls(), "resetButton").GetInteractable());
            builder.CloseAllPanels();
            var curveTypes = TP.GetLowLevelTypes()["decal"].typeData.settings.spline.curveTypes;
            try {
                TP.GetLowLevelTypes()["decal"].typeData.settings.spline.curveTypes = new[] { 2 };
                await Expect("preset application rejects a curve mode excluded by the core", "invalid_value", "state.applyPreset", new { @ref = peerId, name = presetName });
                await Expect("preset sync rejects a curve mode excluded by the core", "invalid_value", "presets.sync", new { category, name = presetName });
            } finally { TP.GetLowLevelTypes()["decal"].typeData.settings.spline.curveTypes = curveTypes; }
            var roomSettings = TP.GetLowLevelTypes()["room"].typeData.settings;
            var roomPresets = roomSettings.presets;
            var contains = roomSettings.container.contains;
            int roomId = -1;
            try {
                roomSettings.presets = true;
                roomSettings.container.contains = contains.Concat(new[] { "decal" }).ToArray();
                roomId = (await Call("lowLevel.create", new { type = "room" })).Value<int>();
                await Call("presets.save", new { category = "lowLevel/room", name = presetName, values = new { type = "room", marker = "other type" } });
                await Call("state.applyPreset", new { @ref = roomId, name = presetName });
                await Call("lowLevel.setContainer", new { element = peerId, container = roomId });
                await Call("presets.sync", new { category, name = presetName });
                Check("sync preserves containers and isolates matching names in another type", Element(peerId).container == Element(roomId)
                    && Element(roomId).state.Str("marker") == "other type");
                Operations.LowLevelPresets.Remember("room", presetName);
                Check("creation memory is independent for each low-level type", Operations.LowLevelPresets.CreationState("room").Str("type") == "room"
                    && Operations.LowLevelPresets.CreationState("decal").Str("type") == "decal");
            } finally {
                if (roomId >= 0) await Call("lowLevel.delete", new { element = roomId });
                PresetManager.GetPresets().presets.Remove("lowLevel/room");
                PresetManager.SavePresets();
                roomSettings.presets = roomPresets;
                roomSettings.container.contains = contains;
            }
            await Expect("preset saves reject another type", "invalid_value", "presets.save", new { category, name = "bad", values = new { type = "room" } });
            await Expect("preset saves reject a non-string type", "invalid_value", "presets.save", new { category, name = "bad", values = new { type = 42 } });
            await Expect("preset saves reject unsupported curve modes", "invalid_value", "presets.save", new { category, name = "bad", values = new { type = "decal", curveType = 99 } });
            await Expect("disabled low-level types have no preset category", "invalid_value", "presets.list", new { category = "lowLevel/room" });

            var type = TP.GetLowLevelTypes()["decal"];
            var oldParameters = type.parameterContainer.parameters;
            try {
                type.parameterContainer.parameters = oldParameters.Concat(new[] {
                    new CityElements.Types.Parameter { name = "testInstance", type = "string", instanceSpecific = true },
                    new CityElements.Types.Parameter { name = "testReference", type = "reference" },
                    new CityElements.Types.Parameter { name = "testGroup", type = "customElement", arrayProperties = new CityElements.Types.ArrayProperties {
                        elementProperties = new[] { new CityElements.Types.Parameter { name = "instanceValue", type = "string", instanceSpecific = true } }
                    } }
                }).ToArray();
                var nested = Operations.LowLevelPresets.DefaultState("decal");
                var group = new ObjectState(); group.SetStr("instanceValue", "keep nested");
                nested.SetState("testGroup", group);
                Operations.LowLevelPresets.Apply(nested, expected, "decal");
                Check("preset application preserves instance values in an omitted custom group", nested.State("testGroup")?.Str("instanceValue") == "keep nested");
                Element(peerId).state.SetStr("testInstance", "keep");
                var reference = new ObjectState(); reference.SetInt("id", placedId); reference.SetStr("kind", "lowLevel");
                Element(peerId).state.SetState("testReference", reference);
                await Call("state.saveAsPreset", new { @ref = peerId, name = "instance test" });
                var reusable = await Call("presets.get", new { category, name = "instance test" });
                Check("saved presets exclude instance properties and element references", reusable["testInstance"] == null && reusable["testReference"] == null);
                await Call("state.applyPreset", new { @ref = peerId, name = presetName });
                Check("applying presets preserves instance properties and references", Element(peerId).state.Str("testInstance") == "keep"
                    && Element(peerId).state.State("testReference").Int("id") == placedId);
                await Call("presets.delete", new { category, name = "instance test" });
            } finally {
                type.parameterContainer.parameters = oldParameters;
                Element(peerId).state.properties.Remove("testInstance");
                Element(peerId).state.properties.Remove("testReference");
            }
            await CheckUndo("deleting low-level presets", () => Call("presets.delete", new { category, name = presetName }));
            Check("deleting a preset unlinks its elements", Element(peerId).state.Name == "" && Element(placedId).state.Name == "");
            Start();
            Check("a deleted remembered preset falls back to Default", Draft().Name == "Default");
            builder.CloseAllPanels();

            var presets = PresetManager.GetPresets();
            presets.presets.Remove(category);
            PresetManager.SavePresets();
            PresetManager.loaded = false;
            var fallback = PresetManager.GetPresets().GetList(category)[0];
            Check("older cities receive the core-defined Default", fallback.Equals(expected));
            Check("the missing category is persisted", JObject.Parse(File.ReadAllText(Path.Combine(folder, "presets.json")))["presets"][category] != null);
            fallback.SetFloat("width", 7);
            PresetManager.SavePreset(fallback, category);
            Operations.LowLevelPresets.Remember("decal", "obsolete selection");
            PresetManager.loaded = false;
            Check("reloading preserves user defaults and clears remembered selections", Operations.LowLevelPresets.CreationState("decal").Float("width") == 7);
            PresetManager.SavePreset(expected, category);
            var remembered = (ObjectState)expected.Clone();
            remembered.Name = "Reload selection";
            remembered.SetFloat("width", 8);
            PresetManager.SavePreset(remembered, category);
            Operations.LowLevelPresets.Remember("decal", remembered.Name);
            Check("the session remembers a named preset before reload", Operations.LowLevelPresets.CreationState("decal").Float("width") == 8);
            await Call("city.save");
            await Call("city.reload");
            var afterReload = Operations.LowLevelPresets.CreationState("decal");
            Check("city reload preserves presets and clears per-type creation memory", afterReload.Name == "Default"
                && afterReload.Float("width") == 2 && PresetManager.GetPresetByName(remembered.Name, category)?.Float("width") == 8);
            var elementsAfterReload = (JArray)await Call("elements.list", new { kind = "lowLevel", type = "decal" });
            placedId = elementsAfterReload.First(e => e.Value<string>("name") == "Preset workflow placed").Value<int>("id");
            peerId = elementsAfterReload.First(e => e.Value<string>("name") == "Preset workflow peer").Value<int>("id");
            await Call("presets.delete", new { category, name = remembered.Name });
            await Call("lowLevel.delete", new { element = peerId });
            await Call("lowLevel.delete", new { element = placedId });
        }
    }
}

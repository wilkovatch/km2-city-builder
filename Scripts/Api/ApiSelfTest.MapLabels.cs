using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Lua;
using RuntimeCalculator.Scripting;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;
using PC = Operations.PropertyContainers;

namespace Api {
    public partial class ApiSelfTest {
        //A short regression for field-based labels, separate from map rendering and the full race workflow.
        async Task CustomElementListLabels() {
            Log("custom-element list labels");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, flags).GetValue(obj);
            List<string> Texts(EditorPanelElements.ScrollList list) => Field<List<string>>(list, "strings");
            var state = new ObjectState();
            string Format(string type, string key = "value", string container = null, string[] enums = null) => Operations.States.GetParameterLabel(state,
                new CityElements.Types.Parameter { name = key, container = container, type = type, enumLabels = enums });
            state.SetBool("value", false); Check("false is a valid list label", Format("bool") == "False");
            state.SetInt("value", 0); Check("zero is a valid list label", Format("int") == "0");
            state.SetFloat("value", 1.5f); Check("float labels use array formatting", Format("float") == 1.5f.ToString());
            state.SetInt("value", 1); Check("enum labels use array formatting", Format("enum", enums: new[] { "A", "B" }) == "B");
            state.SetInt("value", -1); Check("invalid enum values have no label", Format("enum", enums: new[] { "A" }) == null);
            state.SetStr("group_text", "District"); Check("container prefixes resolve full parameter names", Format("string", "text", "group") == "District");
            state.SetStr("value", "geometry/vpcop.pkg"); Check("initial object strings label police models", Format("objectInstance") == "geometry/vpcop.pkg");
            var model = new ObjectState(); model.SetStr("meshPath", "geometry/vpbug.pkg"); state.SetState("value", model);
            Check("object labels read the model path", Format("objectInstance") == "geometry/vpbug.pkg");
            Check("unsupported parameters have no label", Format("reference") == null);

            var police = new ArrayListsPanel { parameters = new[] { new CityElements.Types.Parameter { name = "police", type = "array", label = "POLICE_CARS",
                arrayProperties = new CityElements.Types.ArrayProperties { maxElements = -1, customCityPropertiesType = "policeCar" } } } };
            var policeState = new ObjectState(); policeState.SetState("carModel", model);
            police.state.SetArray("police", new[] { policeState });
            police.Initialize(builder); police.SetActive(true);
            try { Check("the police array still displays its model path", Texts(police.List("police")).SequenceEqual(new[] { "geometry/vpbug.pkg" })); }
            finally { police.Destroy(); }

            var city = M.GetDummy<CityProperties>();
            PC.Sync(M);
            var owner = PC.EnsureId(city.state);
            var names = Enumerable.Range(0, 16).Select(i => "Stored label " + i).ToArray();
            History.UndoManager.Record("create list labels", () => {
                foreach (var name in names) {
                    var label = Operations.CustomElements.Create(M, "mapLabel", Vector3.Zero);
                    GO.SetName(label, name); label.state.SetStr("text", "District"); PC.Assign(label.state, owner);
                }
                M.FlagAsChanged();
            });
            M.ProcessUpdate(); await Frames(2);
            CustomElement Label(int i) => M.customElements.First(e => !e.IsDeleted() && GO.GetName(e) == names[i]);
            var labelType = Label(0).GetCustomElementType();
            var settings = labelType.typeData.settings;
            var originalKey = settings.labelParameter;
            var originalParameters = labelType.parameterContainer.parameters;
            try {
                Check("MM2 labels select their text parameter", originalKey == "text" && Operations.CustomElements.GetListLabel(Label(0)) == "District");
                foreach (var key in new[] { null, "notDeclared", "text.meshPath" }) {
                    settings.labelParameter = key;
                    Check("unconfigured or unknown label falls back: " + (key ?? "null"), Operations.CustomElements.GetListLabel(Label(0)) == names[0]);
                }
                settings.labelParameter = "text";
                foreach (var value in new[] { "", "  ", null }) {
                    Label(0).state.SetStr("text", value);
                    Check("blank label falls back to its stored name", Operations.CustomElements.GetListLabel(Label(0)) == names[0]);
                }
                Label(0).state.RemoveProperty("text");
                Check("missing label falls back to its stored name", Operations.CustomElements.GetListLabel(Label(0)) == names[0]);
                var synthetic = new CityElements.Types.Parameter { name = "value", container = "group", type = "int" };
                labelType.parameterContainer.parameters = originalParameters.Append(synthetic).ToArray();
                settings.labelParameter = "group_value";
                Check("missing numeric labels fall back instead of showing zero", Operations.CustomElements.GetListLabel(Label(0)) == names[0]);
                Label(0).state.SetInt("group_value", 0);
                Check("a configured zero is displayed", Operations.CustomElements.GetListLabel(Label(0)) == "0");
                synthetic.type = "bool"; Label(0).state.SetBool("group_value", false);
                Check("a configured false is displayed", Operations.CustomElements.GetListLabel(Label(0)) == "False");
                synthetic.type = "reference";
                Check("unsupported configured parameters fall back", Operations.CustomElements.GetListLabel(Label(0)) == names[0]);
                synthetic.type = "string"; Label(0).state.SetStr("group_value", "Prefixed value");
                Check("configured full parameter names resolve", Operations.CustomElements.GetListLabel(Label(0)) == "Prefixed value");
            } finally {
                settings.labelParameter = originalKey;
                labelType.parameterContainer.parameters = originalParameters;
                Label(0).state.SetStr("text", "District"); Label(0).state.RemoveProperty("group_value");
            }

            EditorPanels.ContainerContentsPanel OpenLabels() {
                builder.CloseAllPanels(); builder.SelectObject(M.GetDummy<CityProperties>());
                builder.cityPropertiesEditorPanel.ShowTab("CP_HUDMAP");
                return (EditorPanels.ContainerContentsPanel)builder.cityPropertiesEditorPanel.OpenContents(0);
            }
            var contents = OpenLabels(); await Frames(2);
            var list = Field<EditorPanelElements.ScrollList>(contents, "list");
            Check("owned lists show duplicate text as separate entries", Texts(list).Count == 16 && Texts(list).All(t => t == "District"));
            list.SetValue(6);
            var scroll = Field<ScrollContainer>(list, "scroll"); scroll.ScrollVertical = 125; await Frames(1);
            var scrollPosition = scroll.ScrollVertical;
            History.UndoManager.Record("edit list text", () => {
                History.UndoManager.Touch(Label(0)); Label(0).state.SetStr("text", "Centre"); M.FlagAsChanged();
            });
            M.ProcessUpdate(); await Frames(2);
            Check("owned lists refresh text without a membership change", Texts(list)[0] == "Centre");
            Check("label refresh preserves selection and scroll", list.Selected == 6 && Field<int>(contents, "selected") == 6
                && scrollPosition > 0 && scroll.ScrollVertical == scrollPosition);
            Check("changing text leaves the stored name intact", GO.GetName(Label(0)) == names[0]);
            builder.PerformUndo(); await Frames(2);
            contents = OpenLabels(); list = Field<EditorPanelElements.ScrollList>(contents, "list");
            Check("undo restores the displayed label", Texts(list)[0] == "District");
            builder.PerformRedo(); await Frames(2);
            contents = OpenLabels(); list = Field<EditorPanelElements.ScrollList>(contents, "list");
            Check("redo restores the edited label", Texts(list)[0] == "Centre");

            builder.CloseAllPanels();
            PC.Assign(Label(0).state, ""); //the general object list intentionally excludes owned elements
            var objectList = Field<EditorPanels.ObjectListEditorPanel>(builder, "objectListEditorPanel");
            objectList.SetActive(true);
            list = Field<EditorPanelElements.ScrollList>(objectList, "lst");
            Check("the object list displays the configured field", Texts(list).Contains("Centre") && !Texts(list).Contains(names[0]));
            Label(0).state.SetStr("text", "Object edit"); objectList.Update();
            Check("object-list labels refresh", Texts(list).Contains("Object edit"));
            Label(0).state.SetStr("text", "Centre"); objectList.SetActive(false);
            PC.Assign(Label(0).state, owner);

            var room = Operations.CustomElements.Create(M, "room", Vector3.Zero);
            var secondLabel = Label(1);
            GO.SetName(room, "Parent");
            var contains = room.Settings.container.contains;
            room.Settings.container.contains = contains.Append("mapLabel").ToArray();
            try {
                Label(0).SetContainer(room); Label(1).SetContainer(room);
                Label(0).state.SetStr("text", "Parent District"); Label(1).state.SetStr("text", "");
                GO.SetName(Label(1), "Parent fallback");
                builder.SelectObject(room); M.ProcessUpdate(); await Frames(2);
                list = Field<EditorPanelElements.ScrollList>(builder.customElementEditorPanel, "contentsList");
                Check("custom contents keep configured prefixes and shorten fallback names", Texts(list).SequenceEqual(new[] { "Parent District", "fallback" }));
                list.SetValue(0); Label(0).state.SetStr("text", "Live contents"); builder.customElementEditorPanel.Update();
                Check("custom contents refresh without losing selection", Texts(list)[0] == "Live contents" && list.Selected == 0);
                var picker = Field<EditorPanels.CustomElementPickerPanel>(builder.customElementEditorPanel, "picker");
                picker.Open("test", "test", new List<CustomElement> { null, Label(0), secondLabel }, null, "test", _ => { });
                list = Field<EditorPanelElements.ScrollList>(picker, "lst");
                Check("custom-element pickers display configured labels", Texts(list)[1] == "Live contents");
                list.SetValue(1); Label(0).state.SetStr("text", "Live picker"); builder.NotifyUpdateCompleted();
                Check("picker labels refresh while selected", Texts(list)[1] == "Live picker" && list.Selected == 1);
            } finally {
                builder.CloseAllPanels();
                foreach (var label in M.GetCustomElementChildren(room)) label.SetContainer(null);
                room.Settings.container.contains = contains;
                GO.SetName(secondLabel, names[1]);
                Label(0).state.SetStr("text", "Centre"); Label(1).state.SetStr("text", "District");
                Operations.CustomElements.Delete(M, room); M.FlagAsChanged();
            }

            city = M.GetDummy<CityProperties>();
            var raceParameter = CityElements.Types.Parsers.TypeParser.GetCitySettings()["city"].typeData.parametersInfo.parameters.First(p => p.name == "circuitRaces");
            Operations.States.AddArrayElement(city.state, raceParameter);
            var race = city.state.Array<ObjectState>("circuitRaces").Last();
            PC.Sync(M);
            var opponent = Operations.CustomElements.Create(M, "opponent", Vector3.Zero);
            GO.SetName(opponent, "Stored opponent"); PC.Assign(opponent.state, PC.EnsureId(race.State("policeCars")));
            opponent.state.SetState("carModel", (ObjectState)model.Clone());
            opponent.InsertArrayElement("spline", 0); opponent.InsertArrayElement("spline", 1);
            M.FlagAsChanged(); M.ProcessUpdate();
            var racePanel = (EditorPanels.ArrayElemEditorPanel)PC.Open(builder, PC.Find(PC.EnsureId(race.State("policeCars"))));
            contents = (EditorPanels.ContainerContentsPanel)racePanel.OpenContents(0); await Frames(2);
            list = Field<EditorPanelElements.ScrollList>(contents, "list");
            Check("opponent lists show their car model", Texts(list).SequenceEqual(new[] { "geometry/vpbug.pkg" }));
            opponent.state.State("carModel").SetStr("meshPath", "geometry/vpcop.pkg"); contents.Update();
            Check("opponent model changes update the list", Texts(list).SequenceEqual(new[] { "geometry/vpcop.pkg" }) && GO.GetName(opponent) == "Stored opponent");
            Check("API names remain stored names", ((JArray)await Call("elements.list", new { kind = "customElement" })).Any(e => e["name"].Value<string>() == names[0]));
            await Call("city.save"); await Call("city.reload");
            contents = OpenLabels(); list = Field<EditorPanelElements.ScrollList>(contents, "list");
            Check("save/reload preserves text labels and stored names", Texts(list)[0] == "Centre" && GO.GetName(Label(0)) == names[0]);
            var exportPath = Path.Combine(folder, "..", "list_labels_export.json");
            await Call("city.exportJson", new { path = exportPath });
            var exported = JObject.Parse(File.ReadAllText(exportPath))["customElements"];
            Check("exported names remain stored names", exported.Any(e => e["data"]?["name"]?.Value<string>() == names[0]) && !exported.Any(e => e["data"]?["name"]?.Value<string>() == "Centre"));
            builder.CloseAllPanels();
        }

        async Task MapLabels() {
            Log("core-generated map labels and singleton container");
            var city = M.GetDummy<CityProperties>();
            PC.Sync(M);
            var owner = PC.EnsureId(city.state);
            History.UndoManager.Record("add map labels", () => {
                var index = 0;
                foreach (var name in new[] { "Downtown", "Harbour" }) {
                    var label = Operations.CustomElements.Create(M, "mapLabel", new Vector3(20 + index++ * 280, 0, 30));
                    GO.SetName(label, name); label.state.SetStr("text", name); PC.Assign(label.state, owner);
                }
                M.FlagAsChanged();
            });
            M.ProcessUpdate(); await Frames(2);
            CustomElement[] Labels() => M.customElements.Where(e => !e.IsDeleted() && e.TypeName == "mapLabel").ToArray();
            GameEngine.LineRenderer[] Lines(CustomElement e) => e.FindChildren("*", "", true, false).OfType<GameEngine.LineRenderer>().Where(l => !l.IsQueuedForDeletion()).ToArray();
            Check("labels are owned by the root city-property container", PC.Find(owner)?.path.Count == 0 && PC.Contents(owner).Count == 2);
            Check("labels are hidden outside their container section", Labels().All(e => !e.IsVisibleInTree()));
            builder.SelectObject(city);
            var panel = builder.cityPropertiesEditorPanel;
            panel.ShowTab("CP_HUDMAP"); M.ProcessUpdate(); await Frames(2);
            Check("opening the Hudmap tab selects its singleton container", PC.Active("hudmap")?.state == city.state);
            var containerMenuLabel = StringManager.Get("CONTAINERS_SELECTED_SING").Replace("$NUMBER", "1");
            Check("tab selection also updates the shared containers menu", CallBuilder<MenuBar>("GetMenuBar").FindChildren("*", nameof(MenuButton), true, false)
                .OfType<MenuButton>().Any(b => b.Text == containerMenuLabel && b.Visible));
            Vector3[] Vertices(CustomElement e) => GameEngine.Mesh.GetVertices(e.generator.m);
            float FontHeight(CustomElement e) {
                var angle=Mathf.DegToRad(e.state.Float("angle"));
                var up=new Vector3(Mathf.Sin(angle),0,-Mathf.Cos(angle));
                var positions=Vertices(e).Select(p => p.Dot(up)).ToArray();
                return positions.Max()-positions.Min();
            }
            Check("the core generates smooth filled glyph meshes", Labels().All(e => e.IsVisibleInTree() && Vertices(e).Length > 100 && Lines(e).Length == 2));
            Check("glyph triangles face up for picking above the map", Labels().All(e => GameEngine.Mesh.GetNormals(e.generator.m).All(n => n.Y > 0.99f)));
            Check("no builder text nodes are used", !city.GetTree().Root.FindChildren("*", nameof(Label3D), true, false).Any());
            var initialHeight=FontHeight(Labels()[0]);
            Check("white text with a black outline, unshaded on both faces", Labels().All(e => {
                Color Surface(int i) => (e.generator.mr.GetActiveMaterial(i) as ShaderMaterial).GetShaderParameter("_Color").AsColor();
                var material=e.generator.mr.GetActiveMaterial(0) as ShaderMaterial;
                return e.generator.m.GetSurfaceCount()==2 && material.Shader.Code.Contains("cull_disabled") && material.Shader.Code.Contains("unshaded")
                    && Surface(0).IsEqualApprox(Colors.White) && Surface(1).IsEqualApprox(Colors.Black);
            }));
            Check("the labels don't cast shadows", Labels().All(e => e.generator.mr.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off));
            Check("text floats five metres above its unchanged authored point", Labels().All(e => {
                var center=e.GetAllPoints()[0].GetPoint();
                return Vertices(e).All(p => Mathf.Abs(p.Y-center.Y-5)<0.03f)
                    && Mathf.Abs(Lines(e)[0].GetPosition(0).Y-center.Y-0.4f)<0.001f;
            }));
            global::CustomElements.GeometryResult Glyphs(string text) {
                var e=Labels()[0]; var type=e.GetCustomElementType();
                var context=(LuaTable)typeof(CustomElement).GetMethod("CreateScriptContext",System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(e,new object[] { null });
                context["parameters"].Read<LuaTable>()["text"]=text;
                var result=type.scriptHost.Call(type.geometryFunction,LuaVariableView.Get(e.GetParametricObjectVariableContainer()),context,"label font test");
                return global::CustomElements.CustomElementScripting.ReadGeometry(result);
            }
            var accented=Glyphs("Citt\u00e0"); var upper=Glyphs("CITT\u00c0");
            Check("the core keeps accented glyphs and Unicode uppercase", accented.meshes[0].vertices.SequenceEqual(upper.meshes[0].vertices)
                && !accented.meshes[0].vertices.SequenceEqual(Glyphs("CITTA").meshes[0].vertices));
            Check("the outline goes around the text on every side", Glyphs("Hudmap") is var outlined && outlined.meshes.Count == 2 && new System.Func<Vector3, float>[] { p => p.X, p => -p.X, p => p.Z, p => -p.Z }
                .All(side => outlined.meshes[1].vertices.Max(side) > outlined.meshes[0].vertices.Max(side) + 0.5f));
            var contents = panel.OpenContents(0); M.ProcessUpdate(); await Frames(2);
            Check("the singleton uses the shared container contents panel", contents is EditorPanels.ContainerContentsPanel && Labels().All(e => e.IsVisibleInTree()));
            builder.SelectObject(Labels()[0]); M.ProcessUpdate(); await Frames(2);
            Check("editing an owned label retains the whole container preview", PC.IsCurrent(owner) && Labels().All(e => e.IsVisibleInTree()));
            History.UndoManager.Record("edit core label", () => {
                var label = Labels()[0];
                History.UndoManager.Touch(label); History.UndoManager.Touch(city); History.UndoManager.Touch(label.GetAllPoints()[0]);
                label.state.SetStr("text", "City Centre"); label.state.SetFloat("size", 12); label.state.SetFloat("angle", 30);
                city.state.SetFloat("hudmapMetersPerPixel", 3);
                GO.SetPosition(label.GetAllPoints()[0].GetObject(), new Vector3(45, 0, 55));
                M.FlagAsChanged();
            });
            M.ProcessUpdate(); await Frames(2);
            Check("core geometry follows text, size and city scale edits", Labels()[0].state.Str("text") == "City Centre"
                && Mathf.Abs(FontHeight(Labels()[0])/initialHeight-36f/37.125f)<0.005f);
            Check("label point edits preserve authored coordinates", Labels()[0].GetAllPoints()[0].GetPoint().DistanceTo(new Vector3(45, 0, 55)) < 0.01f);
            builder.PerformUndo(); await Frames(3);
            city = M.GetDummy<CityProperties>();
            Check("undo restores label data and ownership", Labels()[0].state.Str("text") == "Downtown" && PC.Owner(Labels()[0].state) == owner);
            builder.CloseAllPanels(); builder.SelectObject(city); panel.ShowTab("CP_HUDMAP"); M.ProcessUpdate();
            panel.ShowPage(0); M.ProcessUpdate(); await Frames(2);
            Check("switching city tabs hides container contents", PC.Active("hudmap") == null && Labels().All(e => !e.IsVisibleInTree()));
            panel.ShowTab("CP_HUDMAP"); M.ProcessUpdate();
            History.UndoManager.Record("new map label draft", () => CallBuilder<object>("StartCustomElementCreation", "mapLabel", "CREATE_CUSTOM_ELEMENT"));
            Check("label creation captures the singleton owner", builder.customElementPlacer.propertyContainerId == owner && PC.IsCurrent(owner));
            History.UndoManager.Record("place owned map label", () => builder.customElementPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit {
                collider = builder.helper, point = new Vector3(100, 0, 100) }));
            M.ProcessUpdate(); await Frames(2);
            Check("click placement creates a label in the active singleton", Labels().Length == 3 && PC.Owner(builder.customElementPlacer.GetElement()?.state) == owner);
            builder.PerformUndo(); await Frames(3);
            Check("undo removes the placed label", Labels().Length == 2);
            builder.CloseAllPanels(); builder.SelectObject(city); panel.ShowTab("CP_HUDMAP"); M.ProcessUpdate();
            History.UndoManager.Record("another map label draft", () => CallBuilder<object>("StartCustomElementCreation", "mapLabel", "CREATE_CUSTOM_ELEMENT"));
            builder.CloseAllPanels(); M.ProcessUpdate(); await Frames(2);
            Check("closing the section cancels its captured placement and keeps the labels shown", builder.customElementPlacer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None
                && PC.Active("hudmap")?.state == city.state && Labels().All(e => e.IsVisibleInTree()));
            builder.SelectObject(city); panel.ShowTab("CP_HUDMAP"); M.ProcessUpdate();
            panel.Terminate(); M.ProcessUpdate(); await Frames(2);
            Check("the Close button keeps the container selected", !panel.IsOpen() && PC.Active("hudmap")?.state == city.state && PC.IsCurrent(owner) && Labels().All(e => e.IsVisibleInTree()));
            var clicked = Labels()[0];
            var glyph = GameEngine.Vector3.EncodePosition(clicked.generator.ToGlobal(clicked.generator.m.GetFaces().Take(3).Aggregate(Vector3.Zero, (sum, p) => sum + p) / 3));
            var selector = (TerrainModifier.Null)typeof(CityBuilderMenuBar).GetField("nullModifier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).GetValue(builder);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); await Frames(1);
            var picked = selector.Raycast(builder.helper, glyph + Vector3.Up * 100, Vector3.Down, out var labelHit, 200, selector.GetLayerMasks()[0]);
            Check("with the panel closed a click ray hits the label", picked && labelHit.collider == clicked.generator);
            if (picked) selector.Select(labelHit);
            M.ProcessUpdate(); await Frames(2);
            Check("clicking a label with the panel closed opens its editor", builder.customElementEditorPanel.IsOpen() && builder.customElementEditorPanel.GetUndoTarget() == clicked);
            builder.CloseAllPanels(); M.ProcessUpdate(); await Frames(2);
            Check("closing the label editor keeps the container selected", PC.Active("hudmap")?.state == city.state && Labels().All(e => e.IsVisibleInTree()));
            builder.SelectObject(city); M.ProcessUpdate(); await Frames(2);
            Check("reopening the city properties on another tab keeps the container selected", PC.Active("hudmap")?.state == city.state && Labels().All(e => e.IsVisibleInTree()));
            builder.CloseAllPanels(); CallBuilder<object>("DeselectEntity", "hudmap"); M.ProcessUpdate(); await Frames(2);
            Check("the Containers menu deselects the closed container", PC.Active("hudmap") == null && Labels().All(e => !e.IsVisibleInTree()));
            await Call("city.save"); await Call("city.reload");
            city = M.GetDummy<CityProperties>();
            Check("save/reload preserves singleton identity and label controls", PC.Find(owner)?.state == city.state && Labels().Length == 2
                && Labels()[0].GetAllPoints()[0].GetPoint().DistanceTo(new Vector3(20, 0, 30)) < 0.01f);
            JObject ReadCity(string path) { using var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress); using var reader = new StreamReader(stream); return JObject.Parse(reader.ReadToEnd()); }
            void WriteCity(string path, JObject data) { using var stream = new GZipStream(File.Create(path), CompressionMode.Compress); using var writer = new StreamWriter(stream); writer.Write(data.ToString()); }
            var cityPath = Path.Combine(folder, "city.json.gz");
            var legacy = ReadCity(cityPath);
            legacy["customElements"] = new JArray(legacy["customElements"].Where(e => e["state"]?["type"]?.Value<string>() != "mapLabel"));
            legacy["cityProperties"]["mapLabels"] = JArray.FromObject(new[] {
                new { text = "Downtown", point = new { localPosition = new { x = 20f, y = 0f, z = 30f } } },
                new { text = "Harbour", point = new { localPosition = new { x = 300f, y = 0f, z = 30f } } }
            });
            WriteCity(cityPath, legacy);
            var preferencesPath = Path.Combine(folder, "preferences.json");
            var preferences = JObject.Parse(File.ReadAllText(preferencesPath)); preferences["coreVersion"] = 4;
            File.WriteAllText(preferencesPath, preferences.ToString());
            await Call("city.load", new { folder, migrate = "always" });
            city = M.GetDummy<CityProperties>();
            Check("normal migration preserves legacy labels and their positions", Labels().Length == 2 && Labels()[0].state.Str("text") == "Downtown"
                && Labels()[0].GetAllPoints()[0].GetPoint().DistanceTo(new Vector3(20, 0, 30)) < 0.01f && PC.Contents(owner).Count == 2);
            Check("migration backs up the original array before decoding", ReadCity(cityPath + ".v4.bak")["cityProperties"]["mapLabels"] is JArray
                && ReadCity(cityPath)["cityProperties"]["mapLabels"] == null);
            Check("migration advances only the schema version", JObject.Parse(File.ReadAllText(preferencesPath))["coreVersion"].Value<int>() == CoreManager.GetCoreVersion("MidtownMadness2"));
            PC.Open(builder, PC.Find(owner)); M.ProcessUpdate(); await Frames(2);
            Check("opening a singleton from the containers menu restores its tab", panel.ShowsTab("CP_HUDMAP") && Labels().All(e => e.IsVisibleInTree()));
            CallBuilder<object>("DeselectEntity", "hudmap"); M.ProcessUpdate(); await Frames(2);
            Check("deselecting a tab container closes it", PC.Active("hudmap") == null && Labels().All(e => !e.IsVisibleInTree()));
            var json = Path.Combine(folder, "..", "map_labels_export.json");
            await Call("city.exportJson", new { path = json });
            var exported = JObject.Parse(File.ReadAllText(json));
            var labels = exported["customElements"].Where(e => e["type"].Value<string>() == "mapLabel").ToArray();
            Check("export JSON contains labels as owned point elements", labels.Length == 2 && labels[0]["data"]["fields"]["state"]["text"].Value<string>() == "Downtown"
                && labels[0]["points"]["center"]["position"][0].Value<float>() == 20);
            //over a surface above the terrain, whose collider isn't in the physics world yet at the first update of a load
            var raisedSurface = (await Call("customElement.create", new { type = "triangleFan", name = "Raised surface", mesh = new {
                vertices = new[] { new { position = new[] { 560f, 12f, 560f } }, new { position = new[] { 640f, 12f, 560f } }, new { position = new[] { 640f, 12f, 640f } }, new { position = new[] { 560f, 12f, 640f } } },
                triangles = new[] { 0, 2, 1, 0, 3, 2 } } })).Value<int>();
            History.UndoManager.Record("label over a surface", () => {
                var label = Operations.CustomElements.Create(M, "mapLabel", new Vector3(600, 0, 600));
                GO.SetName(label, "Raised"); label.state.SetStr("text", "Raised"); PC.Assign(label.state, owner);
                M.FlagAsChanged();
            });
            await Call("city.flush"); await Frames(5);
            var surfaceHeight = GeometryHelper.AutoProjectOnSurface(new Vector3(600, 99, 600), 136).Y;
            CustomElement Raised() => Labels().First(e => e.state.Str("text") == "Raised");
            float RaisedHeight() => Raised().GetAllPoints()[0].GetPoint().Y;
            Check("the surface is above the terrain (" + surfaceHeight + ")", surfaceHeight > 5);
            Check("a new label goes on the surface (" + RaisedHeight() + ")", Mathf.Abs(RaisedHeight() - surfaceHeight) < 0.01f);
            await Call("city.save"); await Call("city.reload"); await Frames(3);
            Check("a label saved on a surface is still on it after loading (" + RaisedHeight() + ")", Mathf.Abs(RaisedHeight() - surfaceHeight) < 0.01f);
            var heightless = ReadCity(cityPath);
            var savedLabel = heightless["customElements"].First(e => e["state"]?["text"]?.Value<string>() == "Raised");
            savedLabel["points"]["center"]["position"]["y"] = 0;
            WriteCity(cityPath, heightless);
            await Call("city.reload"); await Frames(3);
            Check("a label saved at height 0 is projected when the city is loaded (" + RaisedHeight() + ")", Mathf.Abs(RaisedHeight() - surfaceHeight) < 0.01f);
            Check("loading doesn't record an undo step", !(await Call("history.state"))["canUndo"].Value<bool>());
            raisedSurface = M.customElements.First(e => GO.GetName(e) == "Raised surface") is var loadedSurface ? History.ElementRegistry.GetId(loadedSurface) : -1;
            await Call("customElement.move", new { element = raisedSurface, offset = new[] { 0, 3, 0 } });
            Check("a label follows the surface under it (" + RaisedHeight() + ")", Mathf.Abs(RaisedHeight() - surfaceHeight - 3) < 0.01f);
            await Call("history.undo");
            Check("undoing the move of the surface brings the label back (" + RaisedHeight() + ")", Mathf.Abs(RaisedHeight() - surfaceHeight) < 0.01f);
            city = M.GetDummy<CityProperties>();
            if (DisplayServer.GetName() != "headless") {
                await Call("view.camera.set", new { position = V(160, 600, 500), target = V(160, 0, 30) });
                builder.SelectObject(city); panel.ShowTab("CP_HUDMAP"); M.ProcessUpdate(); await Frames(3);
                Screenshot("map_labels_editor.png");
                await Call("view.camera.set", new { position=V(20,150,150),target=V(20,5,30) });
                builder.SelectObject(city); panel.ShowTab("CP_HUDMAP"); M.ProcessUpdate(); await Frames(3);
                Screenshot("map_label_above.png");
                await Call("view.camera.set", new { position=V(20,-150,150),target=V(20,5,30) });
                builder.SelectObject(city); panel.ShowTab("CP_HUDMAP"); M.ProcessUpdate(); await Frames(3);
                Screenshot("map_label_below.png");
            }
            builder.CloseAllPanels();
            CallBuilder<object>("DeselectEntity", "hudmap");
        }
    }
}

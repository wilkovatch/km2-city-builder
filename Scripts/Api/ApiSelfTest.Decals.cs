using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;

namespace Api {
    public partial class ApiSelfTest {
        static float[] SplinePosition(Vector3 value) => new[] { value.X, value.Y, value.Z };

        //the PSDL attributes are not in the Create menu: the checks of the grouped creation make their group creatable for a while
        static System.Action MakeGroupCreatable(string group) {
            var hidden = CityElements.Types.Parsers.TypeParser.GetCustomElementTypes().Values.Select(t => t.typeData.settings)
                .Where(s => !s.creatable && s.createMenu?.group == group).ToList();
            foreach (var s in hidden) s.creatable = true;
            return () => { foreach (var s in hidden) s.creatable = false; };
        }

        async Task Decals() {
            var types = CityElements.Types.Parsers.TypeParser.GetCustomElementTypes();
            if (!types.ContainsKey("decal")) return;
            var schema = await Call("state.schema", new { @ref = 0 });
            Check("manual and automatic decals are no longer city properties", !schema["parameters"].Any(p =>
                p["name"].Value<string>() == "decals" || p["name"].Value<string>() == "cableCarRailDecals"));
            var psdlTypes = types.Values.Where(t => t.typeData.settings.createMenu?.group == "CREATE_PSDL_ELEMENT").ToList();
            Check("decals have a direct menu entry and PSDL elements are hidden from the Create menu", types["decal"].typeData.settings.createMenu.direct &&
                psdlTypes.Count > 0 && psdlTypes.All(t => !t.typeData.settings.creatable));
            builder.ReloadCreateMenu();
            var menu = CallBuilder<MenuBar>("GetMenuBar").FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>()
                .First(b => b.Text == StringManager.Get("CREATE")).GetPopup();
            //the decals are in the Decals submenu with short labels, and keep their full names elsewhere
            var decalsIndex = Enumerable.Range(0, menu.ItemCount).First(i => menu.GetItemSubmenuNode(i) != null && menu.GetItemText(i) == StringManager.Get("DECALS_MENU"));
            var decalsMenu = menu.GetItemSubmenuNode(decalsIndex);
            var splineLabel = StringManager.Get("CREATE_DECAL_SPLINE_MENU");
            Check("the decals are in the Decals submenu as Single and Spline", Enumerable.Range(0, decalsMenu.ItemCount).Select(i => decalsMenu.GetItemText(i)).OrderBy(t => t)
                .SequenceEqual(new[] { StringManager.Get("CREATE_DECAL_SINGLE_MENU"), splineLabel }.OrderBy(t => t)) &&
                !Enumerable.Range(0, menu.ItemCount).Any(i => menu.GetItemText(i) == splineLabel));
            Check("the decals keep their full names outside of the menu", Operations.CustomElements.GetTypeLabel("decal") == StringManager.Get("CREATE_DECAL") &&
                Operations.CustomElements.GetTypeLabel("decalSingle") == StringManager.Get("CREATE_DECAL_SINGLE"));
            var direct = Enumerable.Range(0, decalsMenu.ItemCount).First(i => decalsMenu.GetItemText(i) == splineLabel);
            Check("Create exposes a direct decal and no PSDL group", !Enumerable.Range(0, menu.ItemCount).Any(i => menu.GetItemText(i) == StringManager.Get("CREATE_PSDL_ELEMENT")));
            decalsMenu.EmitSignal(PopupMenu.SignalName.IdPressed, decalsMenu.GetItemId(direct));
            History.UndoManager.Record("place first spline point", () => builder.customElementPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-700, 0, -700) }));
            var clicked = builder.customElementPlacer.GetElement();
            Check("the first click creates one point and continues drawing", clicked != null && clicked.GetAllPoints().Count == 1 &&
                builder.customElementPlacer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint);
            History.UndoManager.Record("place second spline point", () => builder.customElementPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-700, 0, -680) }));
            await Frames(1);
            builder.customElementEditorPanel.SetPointPlacementMode(2);
            History.UndoManager.Record("prepend spline point", () => builder.customElementPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-700, 0, -720) }));
            Check("prepend places an individual point at the clicked position", clicked.GetLeaves()[0].leaf.point.GetPoint().Z == -720);
            var clickedId = History.ElementRegistry.GetId(clicked);
            await Call("customElement.delete", new { element = clickedId });
            await IntersectionDecalPlacement();
            var restore = MakeGroupCreatable("CREATE_PSDL_ELEMENT");
            try {
                builder.ReloadCreateMenu();
                var grouped = Enumerable.Range(0, menu.ItemCount).First(i => menu.GetItemText(i) == StringManager.Get("CREATE_PSDL_ELEMENT"));
                menu.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(grouped));
                var groupedTypes = (List<string>)typeof(EditorPanels.CustomElementEditorPanel).GetField("creatableTypes",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(builder.customElementEditorPanel);
                Check("the PSDL creation panel excludes direct decal entries", groupedTypes.Count > 0 && !groupedTypes.Contains("decal"));
                builder.CloseAllPanels();
            } finally {
                restore();
                builder.ReloadCreateMenu();
            }
            var id = (await Call("customElement.create", new { type = "decal", name = "Spline decal test",
                state = new { texture = "texture/decal_zigzag_l.tex", width = 3, curveType = 2 },
                points = new { spline = new[] { new { position = new[] { -560, 0, 0 } }, new { position = new[] { -500, 0, 2 } }, new { position = new[] { -450, 0, -3 } } } }
            })).Value<int>();
            CustomElement Element() => History.UndoManager.GetAliveElement(id) as CustomElement;
            var element = Element();
            Check("spline geometry is built from core components", element.splineData?.sections.Length == 3 && element.splineData.pieces.Length >= 5 && element.generator.DrawsMesh());
            var section = element.splineData.sections[0];
            var exported = await Call("elements.geometry", new { @ref = id });
            Check("export contains the evaluated spline pieces", exported["element"]?["spline"]?["pieces"]?.Count() == element.splineData.pieces.Length);
            Check("the decal section has the configured width", Mathf.Abs(new Vector3(section.vertices[0][0], section.vertices[0][1], section.vertices[0][2]).DistanceTo(
                new Vector3(section.vertices[1][0], section.vertices[1][1], section.vertices[1][2])) - 3) < 0.01f);
            await CheckUndo("moving a spline point", () => Call("customElement.movePoint", new { element = id, path = "spline.1", position = new[] { -500, 12, 3 } }));
            element = Element();
            var point = ((CustomElements.PointLeaf)((CustomElements.PointList)element.points.Get("spline")).items[1]).point;
            Check("moving a spline point projects it back onto the road", Mathf.Abs(point.GetPoint().Y - GeometryHelper.AutoProjectOnSurface(point.GetPoint(), 136, element.generator).Y) < 0.001f);
            var y = point.GetPoint().Y;
            await Call("city.flush"); await Call("city.flush");
            Check("surface projection excludes decals and does not accumulate height", Mathf.Abs(point.GetPoint().Y - y) < 0.001f);
            await CheckUndo("inserting a spline point", () => Call("customElement.insert", new { element = id, array = "spline", index = 1 }));
            await Call("customElement.remove", new { element = id, array = "spline", index = 1 });
            foreach (var mode in new[] { 0, 1, 2 }) {
                await Call("state.setParam", new { @ref = id, name = "curveType", value = mode });
                Check("spline mode " + mode + " builds finite sections", Element().splineData.sections.Length >= 2 &&
                    Element().splineData.sections.All(s => s.vertices.All(v => v.All(float.IsFinite))));
            }
            await Call("state.setParam", new { @ref = id, name = "curveType", value = 2 });
            var settings = types["decal"].typeData.settings.spline;
            var allowed = settings.curveTypes;
            try {
                settings.curveTypes = new[] { 2 };
                await Expect("a curve mode excluded by the core", "invalid_value", "state.setParam", new { @ref = id, name = "curveType", value = 0 });
                await Expect("state import respects the allowed spline modes", "invalid_value", "state.import", new { @ref = id, values = new { curveType = 0 } });
            } finally { settings.curveTypes = allowed; }
            await Expect("a spline cannot accept an editable mesh", "invalid_operation", "customElement.setMesh", new { element = id, mesh = new { vertices = new object[0], triangles = new int[0] } });
            await CheckUndo("changing spline width", () => Call("state.setParam", new { @ref = id, name = "width", value = 4 }));
            var empty = (await Call("customElement.create", new { type = "decal", name = "Incomplete spline" })).Value<int>();
            Check("new splines do not create two default points", ((CustomElements.PointList)(History.UndoManager.GetAliveElement(empty) as CustomElement).points.Get("spline")).items.Count == 0);
            await Call("customElement.delete", new { element = empty });
            var duplicate = (await Call("customElement.create", new { type = "decal", points = new { spline = new[] {
                new { position = new[] { 0, 0, 0 } }, new { position = new[] { 0, 0, 0 } } } } })).Value<int>();
            Check("coincident spline points produce no invalid geometry", !(History.UndoManager.GetAliveElement(duplicate) as CustomElement).generator.DrawsMesh());
            await Call("customElement.delete", new { element = duplicate });
            element = Element();
            History.UndoManager.Record("edit spline", () => builder.SelectObject(element, true, null));
            Check("clicking a spline opens the custom element editor", builder.customElementEditorPanel.GetElement() == element);
            builder.customElementEditorPanel.SetPointPlacementMode(2);
            Check("spline placement offers prepend", builder.customElementPlacer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.PrependPoint);
            builder.customElementEditorPanel.SetPointPlacementMode(0);
            builder.CloseAllPanels();
            if (DisplayServer.GetName() != "headless") {
                var previousCamera = await Call("view.camera.get");
                await Call("view.camera.set", new { position = new[] { -500, 100, -40 }, target = new[] { -500, 0, 0 } });
                await Frames(3); Screenshot("decal_preview.png");
                var previousPosition = Vec(previousCamera["position"]);
                await Call("view.camera.set", new { position = SplinePosition(previousPosition), target = SplinePosition(previousPosition + Vec(previousCamera["forward"])) });
            }
            await RailDecalGeneration();
            await LodTextureGeneration();
        }

        //the low LOD texture of a road, generated in the texture folder of the city from its textures and widths: a converter
        //changing the procedural element it converts (the textures are written here, the test doesn't need the game data)
        async Task LodTextureGeneration() {
            var converter = ConverterCatalog.Get("generateLodTexture");
            var city = PreferencesManager.workingDirectory;
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(city, "texture"));
            foreach (var (name, color) in new[] { ("lodTestRoad", Colors.DimGray), ("lodTestWalk", Colors.Beige) }) {
                var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgb8);
                image.Fill(color);
                image.SavePng(System.IO.Path.Combine(city, "texture", name + ".png"));
            }
            var id = (await Call("roads.create", new { points = new object[] { new { position = SplinePosition(new Vector3(-700, 0, 300)) }, new { position = SplinePosition(new Vector3(-700, 0, 400)) } } })).Value<int>();
            var road = History.ElementRegistry.Get(id) as Road;
            foreach (var (name, value) in new (string, object)[] { ("isDouble", true), ("hasSidewalks", true), ("totalWidth", 40), ("roadWidth", 30), ("texture0", "texture/lodTestWalk.png"), ("texture1", "texture/lodTestRoad.png") }) {
                await Call("state.setParam", new { @ref = id, name, value });
            }

            History.UndoManager.Record("edit road", () => builder.SelectObject(road, true, null));
            var buttons = (List<(EditorPanelElements.Button button, ConverterCatalog.ConverterInfo converter)>)typeof(EditorPanel)
                .GetField("conversionButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(CallBuilder<EditorPanel>("GetActivePanel"));
            var button = buttons.FirstOrDefault(b => b.converter.name == "generateLodTexture").button;
            Check("the road panel has the low LOD texture button", button != null);
            if (DisplayServer.GetName() != "headless" && button != null) {
                var control = (Control)typeof(EditorPanelElements.Button).GetField("button", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(button);
                Node parent = control;
                while (parent != null && parent is not ScrollContainer) parent = parent.GetParent();
                await Frames(2);
                (parent as ScrollContainer)?.EnsureControlVisible(control);
                await Frames(3);
                Screenshot("road_lod_button.png");
            }
            foreach (var isDouble in new[] { false, true }) {
                History.UndoManager.Record("low LOD test flag", () => {
                    History.UndoManager.Touch(road); road.state.SetBool("isDouble", isDouble);
                    M.FlagAsChanged();
                });
                M.ProcessUpdate();
                Check("low LOD generator availability, double " + isDouble, converter.IsEnabled(road) == isDouble);
                Check("low LOD button availability, double " + isDouble, button != null && button.GetInteractable() == isDouble);
            }

            await Call("city.flush");
            JToken ids = null;
            await CheckUndo("generating a low LOD texture", async () => ids = await Call("elements.convert", new { elements = new[] { id }, converter = "generateLodTexture" }));
            var first = road.state.Str("textureLOD");
            Check("the conversion reports the road as changed", ids is JArray list && list.Values<int>().Contains(id));
            Check("the low LOD texture is assigned to the road", first != null && first.StartsWith("texture/lodtestroad_lod_") && first.EndsWith(".tex"));
            Check("the low LOD texture is in the texture folder of the city", first != null && System.IO.File.Exists(System.IO.Path.Combine(city, first)));
            Check("the road is still a normal road", !road.ghost && !road.deleted);
            await Call("history.undo");
            Check("undo restores the previous low LOD texture", road.state.Str("textureLOD") != first);
            await Call("history.redo");

            await Call("state.setParam", new { @ref = id, name = "totalWidth", value = 46 });
            await Call("elements.convert", new { elements = new[] { id }, converter = "generateLodTexture" });
            Check("other widths give another low LOD texture", road.state.Str("textureLOD") != first);

            //as the button does: the road is edited again when it is done
            await Call("state.setParam", new { @ref = id, name = "roadWidth", value = 26 });
            var previous = road.state.Str("textureLOD");
            History.UndoManager.Record("edit road", () => builder.SelectObject(road, true, null));
            builder.StartConversion(road, "generateLodTexture");
            for (int i = 0; i < 600 && road.state.Str("textureLOD") == previous; i++) await Frames(1);
            await Frames(3);
            Check("the button assigns the new texture", road.state.Str("textureLOD") != previous);
            Check("the road is edited again after the button", CallBuilder<EditorPanel>("GetActivePanel") is EditorPanels.RoadEditorPanel);
            builder.CloseAllPanels();

            //the third texture of the road in the PSDL (the export may report the known error of the origin intersection, it still writes it)
            await Send("city.export", new { path = System.IO.Path.Combine(folder, "..", "api_export_lod"), exporter = "psdl", flags = new Dictionary<string, bool> { ["propRules"] = false, ["psdl"] = true, ["inst"] = false, ["bai"] = false, ["pathset"] = false, ["cinfo_aimap"] = false, ["hudmap"] = false, ["pkgs"] = false } });
            var psdl = System.IO.Path.Combine(folder, "..", "city", "api_export_lod.psdl");
            var names = System.IO.File.Exists(psdl) ? System.Text.Encoding.Latin1.GetString(System.IO.File.ReadAllBytes(psdl)) : "";
            var lodName = System.IO.Path.GetFileNameWithoutExtension(road.state.Str("textureLOD"));
            var roadAt = names.IndexOf("lodtestroad");
            var walkAt = names.IndexOf("lodtestwalk");
            var lodAt = names.IndexOf(lodName);
            Check("the PSDL has the road, sidewalk and low LOD textures in this order", roadAt >= 0 && walkAt > roadAt && lodAt > walkAt && walkAt - roadAt < 20 && lodAt - walkAt < 20);

            await Call("state.setParam", new { @ref = id, name = "texture1", value = "texture/lodTestMissing.png" });
            await Expect("a missing road texture is reported", "converter_failed", "elements.convert", new { elements = new[] { id }, converter = "generateLodTexture" });
            await Call("roads.delete", new { road = id });
        }

        //the single decal: a point element (one point, rotated and scaled with the gizmo) whose scale gives the size of the quad
        async Task IndependentPoints() {
            Log("independent point values, editing, migration and placement targets");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var panel = builder.customElementEditorPanel;
            var transformPanel = (EditorPanels.TransformPropertiesEditorPanel)typeof(CityBuilderMenuBar).GetField("transformPropertiesEditorPanel", flags).GetValue(builder);
            CustomElement Element(int id) => History.UndoManager.GetAliveElement(id) as CustomElement;
            TerrainPoint Center(int id) => Element(id).GetLeaves()[0].leaf.point;
            T Field<T>(string name) => (T)panel.GetType().GetField(name, flags).GetValue(panel);
            void Click(string name) => ((Godot.Button)typeof(EditorPanelElements.Button).GetField("button", flags)
                .GetValue(Field<EditorPanelElements.Button>(name))).EmitSignal(Godot.BaseButton.SignalName.Pressed);
            var ids = new List<int>();
            foreach (var type in new[] { "parkedCar", "decalSingle", "mapLabel" }) {
                var id = (await Call("customElement.create", new { type, name = "Independent " + type, position = V(-400 + ids.Count * 20, 8, -400) })).Value<int>();
                ids.Add(id);
                Check(type + " owns an unregistered independent center", !Center(id).anchorable && History.ElementRegistry.GetId(Center(id)) == -1);
                Check(type + " cannot be an attachment target", Operations.TerrainPoints.AttachmentFromClick(Center(id).GetObject(), Center(id).GetPoint(), false) == null);
                builder.SelectObject(Element(id), true, null);
                CallBuilder<object>("EditCurSelectedObjectTransform");
                Check(type + " stays in its element editor on M, with no attachment controls", panel.ActiveSelf() && !transformPanel.ActiveSelf()
                    && Field<EditorPanelElements.Button>("pointAttachButton") == null);
                var value = Center(id).GetPoint();
                await CheckUndo("moving independent " + type, () => Call("customElement.movePoint", new { element = id, path = "center", position = SplinePosition(value + new Vector3(3, 0, 2)) }));
                builder.SelectObject(Element(id), true, null);
                var guiPosition = Center(id).GetPoint();
                History.UndoManager.Record("move owned point with its gizmo", () => {
                    History.UndoManager.TouchOwner(Center(id).GetObject());
                    GO.SetPosition(Center(id).GetObject(), guiPosition + new Vector3(2, 0, 0)); M.FlagAsChanged();
                });
                await Frames(2);
                var mode = type == "mapLabel" ? RuntimeGizmos.TransformType.Move : RuntimeGizmos.TransformType.Rotate;
                builder.gizmo.transformType = mode;
                CallBuilder<object>("Undo"); await Frames(2);
                Check(type + " GUI undo restores its point selection and gizmo mode", panel.ActiveSelf() && builder.gizmo.mainTargetRoot == Center(id).GetObject()
                    && builder.gizmo.transformType == mode && Center(id).GetPoint().DistanceTo(guiPosition) < 0.01f);
                CallBuilder<object>("Redo"); await Frames(2);
                Check(type + " GUI redo retains its point selection and mode", panel.ActiveSelf() && builder.gizmo.mainTargetRoot == Center(id).GetObject()
                    && builder.gizmo.transformType == mode && Center(id).GetPoint().DistanceTo(guiPosition + new Vector3(2, 0, 0)) < 0.01f);
                if (type != "mapLabel") await Call("customElement.movePoint", new { element = id, path = "center", position = SplinePosition(Center(id).GetPoint()), rotation = V(0, 35, 0),
                    scale = type == "decalSingle" ? V(3, 1, 7) : null });
                var kept = Center(id);
                await CheckUndo("deleting independent " + type, () => Call("customElement.delete", new { element = id }));
                await Call("history.undo");
                Check(type + " deletion and undo recreate only the owned center", kept.IsDeleted() && !Center(id).anchorable && Center(id).GetLinks().Count == 1);
                await Expect(type + " cannot be attached", "invalid_operation", "customElement.attachPoint", new { element = id, path = "center", column = 999 });
                await Expect(type + " has no attachment to detach", "invalid_operation", "customElement.detachPoint", new { element = id, path = "center" });
                await Expect(type + " cannot be created from a reference", "invalid_param", "customElement.create", new { type, points = new { center = new { terrainPoint = 999 } } });
                await Expect(type + " rejects malformed direct values", "invalid_param", "customElement.create", new { type, points = new { center = new { position = new[] { 1, 2 } } } });
            }
            var originals = ids.ToDictionary(id => GO.GetName(Element(id)), id => (position: Center(id).GetPoint(), rotation: Center(id).GetRotation(), scale: Center(id).GetScale()));
            await Call("city.save");
            JObject ReadCity() {
                using var stream = new System.IO.Compression.GZipStream(System.IO.File.OpenRead(System.IO.Path.Combine(folder, "city.json.gz")), System.IO.Compression.CompressionMode.Decompress);
                using var reader = new System.IO.StreamReader(stream);
                return JObject.Parse(reader.ReadToEnd());
            }
            var saved = ReadCity();
            Check("independent centers save inline without shared point records", saved["customElements"].All(e => e["points"]?["center"] is JObject)
                && ((JArray)saved["terrainPoints"]).Count == 0);
            History.UndoManager.Record("clone independent point contents", () => {
                var source = new ObjectState(); var owner = Operations.PropertyContainers.EnsureId(source);
                foreach (var id in ids) Operations.PropertyContainers.Assign(Element(id).state, owner);
                var destination = (ObjectState)source.Clone();
                Operations.PropertyContainers.CloneContents(source, destination);
                var copiedOwner = Operations.PropertyContainers.EnsureId(destination);
                var copies = M.customElements.Where(e => Operations.PropertyContainers.Owner(e.state) == copiedOwner).ToArray();
                Check("container cloning copies every independent point value", copies.Length == ids.Count && copies.All(e => {
                    var original = Element(ids.Single(id => Element(id).TypeName == e.TypeName));
                    var p = e.GetLeaves()[0].leaf.point; var q = original.GetLeaves()[0].leaf.point;
                    return p != q && p.GetPoint().DistanceTo(q.GetPoint()) < 0.01f && p.GetRotation().DistanceTo(q.GetRotation()) < 0.01f && p.GetScale().DistanceTo(q.GetScale()) < 0.01f;
                }));
                var copied = copies[0].GetLeaves()[0].leaf.point;
                Operations.TerrainPoints.Move(copied, copied.GetPoint() + new Vector3(15, 0, 0));
                Check("moving a cloned point keeps the original independent", ids.All(id => Center(id).GetPoint().DistanceTo(originals[GO.GetName(Element(id))].position) < 0.01f));
            });
            await Call("city.reload");
            Check("all independent transforms survive save/reload", M.customElements.Where(e => originals.ContainsKey(GO.GetName(e))).All(e => {
                var p = e.GetLeaves()[0].leaf.point; var value = originals[GO.GetName(e)];
                return p.GetPoint().DistanceTo(value.position) < 0.01f && p.GetRotation().DistanceTo(value.rotation) < 0.01f && p.GetScale().DistanceTo(value.scale) < 0.01f;
            }));
            //The old references are converted by the core before the new reader sees them.
            var legacyPoints = (JArray)saved["terrainPoints"];
            int pointId = 100;
            foreach (var element in saved["customElements"]) {
                var center = (JObject)element["points"]["center"];
                var old = (JObject)center.DeepClone(); old["id"] = ++pointId;
                old["linkType"] = 0; old["elementType"] = 0;
                legacyPoints.Add(old); element["points"]["center"] = pointId;
            }
            using (var stream = new System.IO.Compression.GZipStream(System.IO.File.Create(System.IO.Path.Combine(folder, "city.json.gz")), System.IO.Compression.CompressionMode.Compress))
            using (var writer = new System.IO.StreamWriter(stream)) writer.Write(saved.ToString());
            var prefPath = System.IO.Path.Combine(folder, "preferences.json");
            var preferences = JObject.Parse(System.IO.File.ReadAllText(prefPath)); preferences["coreVersion"] = 5;
            System.IO.File.WriteAllText(prefPath, preferences.ToString());
            await Call("city.load", new { folder, migrate = "always" });
            Check("core migration runs before the independent-value reader", ReadCity()["customElements"].All(e => e["points"]?["center"] is JObject)
                && System.IO.File.Exists(System.IO.Path.Combine(folder, "city.json.gz.v5.bak")));
            Check("core migration preserves the saved world transforms", M.customElements.All(e => {
                var p = e.GetLeaves()[0].leaf.point; var value = originals[GO.GetName(e)];
                return p.GetPoint().DistanceTo(value.position) < 0.01f && p.GetRotation().DistanceTo(value.rotation) < 0.01f && p.GetScale().DistanceTo(value.scale) < 0.01f;
            }));
            await Call("city.save");
            Check("saving after migration drops unused shared points", ((JArray)ReadCity()["terrainPoints"]).Count == 0);
            foreach (var element in M.customElements.ToArray()) await Call("customElement.delete", new { element = History.ElementRegistry.GetId(element) });
            //Opt in temporarily to exercise the generic point panel using the core's ordinary geometry.
            var settings = CityElements.Types.Parsers.TypeParser.GetCustomElementTypes()["parkedCar"].typeData.settings;
            var definitions = settings.controlPoints;
            settings.point.anchorable = true;
            settings.controlPoints = CityElements.Types.CustomElementType.PointNodes(settings.point);
            try {
                var id = (await Call("customElement.create", new { type = "parkedCar", position = V(-300, 5, -300) })).Value<int>();
                var line = (await Call("customElement.create", new { type = "propLine", points = new { spline = new[] {
                    new { position = V(-310, 0, -310) }, new { position = V(-310, 0, -280) } } } })).Value<int>();
                var target = Element(line).GetLeaves()[0].leaf.point;
                History.UndoManager.Record("start anchorable point placement", () => CallBuilder<object>("StartCustomElementCreation", "parkedCar", "CREATE_CUSTOM_ELEMENT"));
                await Frames(1);
                Check("anchorable point creation shows its targets", builder.customElementPlacer.AnchorablePlacement && target.GetObject().IsVisibleInTree());
                History.UndoManager.Record("place anchorable point on a target", () => builder.customElementPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { collider = target.GetObject(), point = target.GetPoint() }));
                var placedId = History.ElementRegistry.GetId(builder.customElementPlacer.GetElement());
                Check("an anchorable point follows its placement target", Center(placedId).anchor is PointAnchor);
                await Call("customElement.delete", new { element = placedId });
                builder.SelectObject(Element(id), true, null); await Frames(2);
                Check("an anchorable point exposes attachment controls in its element panel", Field<EditorPanelElements.Button>("pointAttachButton") != null);
                Click("pointAttachButton");
                var picker = Field<ElementPlacer.TerrainPointAttachmentPicker>("pointAttachmentPicker");
                Check("the element panel starts attachment picking with visible targets", picker.IsActive && GameEngine.Scriptable.Cast<Handle>(target.GetObject()).IsHandleActive());
                Check("cancelling the point picker restores the element panel", panel.CancelPointAttachment() && panel.GetElement() == Element(id));
                Click("pointAttachButton");
                History.UndoManager.Record("attach anchorable point", () => picker.Pick(target.GetObject(), target.GetPoint()));
                await Frames(2);
                Check("the element panel attaches the center without opening an M editor", Center(id).anchor is PointAnchor && panel.ActiveSelf() && !transformPanel.ActiveSelf());
                History.UndoManager.Undo(); await Frames(2);
                Check("point-panel attachment can be undone", GO.IsNull(Center(id).anchor));
                History.UndoManager.Redo(); await Frames(2);
                Check("point-panel attachment can be redone", Center(id).anchor is PointAnchor);
                await Call("customElement.movePoint", new { element = line, path = "spline.0", position = SplinePosition(target.GetPoint() + new Vector3(2, 3, 1)) });
                Check("an attached point follows a moved target", Center(id).GetPoint().DistanceTo(target.GetPoint()) < 0.01f);
                builder.SelectObject(Element(id), true, null); await Frames(1);
                Click("pointDetachButton"); await Frames(2);
                Check("the point panel detaches its center", GO.IsNull(Center(id).anchor));
                foreach (var button in new[] { "pointLineButton", "pointColumnButton" }) {
                    Click(button);
                    var other = Element(line).GetLeaves()[1].leaf.point;
                    History.UndoManager.Record("attach center " + button, () => {
                        picker.Pick(target.GetObject(), target.GetPoint());
                        if (button == "pointLineButton") picker.Pick(other.GetObject(), other.GetPoint());
                    });
                    await Frames(2);
                    Check("the point panel supports " + button, button == "pointLineButton" ? Center(id).anchor is LineAnchor : Center(id).anchor is ColumnAnchor);
                    Click("pointDetachButton"); await Frames(1);
                }
                await Call("customElement.delete", new { element = id });
                await PlacementAnchorTargets(line);
            } finally {
                builder.CloseAllPanels();
                settings.point.anchorable = false;
                settings.controlPoints = definitions;
            }
            Check("the core requires the current breaking program version", CoreManager.ProgramCompatibilityError("MidtownMadness2") == null);
            try {
                GlobalVariables.programVersion = 4;
                Check("a newer core is rejected by an old builder", CoreManager.ProgramCompatibilityError("MidtownMadness2") == "MIGRATION_ERROR_CORE_IS_FOR_NEWER_PROGRAM");
                GlobalVariables.programVersion = 6;
                Check("an old core is rejected by a newer builder", CoreManager.ProgramCompatibilityError("MidtownMadness2") == "MIGRATION_ERROR_CORE_IS_FOR_OLDER_PROGRAM");
            } finally { GlobalVariables.programVersion = 5; }
        }

        async Task PlacementAnchorTargets(int targetId) {
            var target = (History.UndoManager.GetAliveElement(targetId) as CustomElement).GetLeaves()[0].leaf.point;
            var panel = builder.customElementEditorPanel;
            var placer = builder.customElementPlacer;
            var roadId = (await Call("roads.create", new { points = new[] { new { position = V(-200, 0, -330) }, new { position = V(-200, 0, -270) } } })).Value<int>();
            var road = (Road)History.UndoManager.GetAliveElement(roadId);
            bool RoadTargetsVisible() => road.anchorManager.GetTerrainAnchors().Any(a => a.GetObject().IsVisibleInTree() && GameEngine.Scriptable.Cast<Handle>(a.GetObject()).IsHandleActive());
            bool Visible() => GameEngine.Scriptable.Cast<Handle>(target.GetObject()).IsHandleActive() && target.GetObject().IsVisibleInTree();
            foreach (var type in new[] { "propLine", "propPolygon" }) {
                History.UndoManager.Record("start placement " + type, () => CallBuilder<object>("StartCustomElementCreation", type, "CREATE_CUSTOM_ELEMENT"));
                await Frames(2);
                Check(type + " shows anchor targets before the first click", Visible() && RoadTargetsVisible());
                History.UndoManager.Record("place first " + type + " point", () => placer.PlaceAt(new GameEngine.Raycast.RaycastHit { collider = target.GetObject(), point = target.GetPoint() }));
                await Frames(2);
                var element = placer.GetElement();
                Check(type + " anchors its first point and keeps targets visible", element.GetLeaves()[0].leaf.point == target && Visible());
                panel.SetPointPlacementMode(0); await Frames(1);
                Check(type + " stopping placement hides other targets", !Visible() || element.HasPoint(target));
                panel.SetPointPlacementMode(1); await Frames(1);
                panel.Hide(true); await Frames(1);
                Check(type + " hides targets while its child panel is open", !placer.AnchorablePlacement || placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None);
                panel.Hide(false); await Frames(1);
                Check(type + " restores target visibility after its child panel closes", Visible() && placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint);
                panel.Terminate(); await Frames(1);
                Check(type + " terminating clears anchor targets", !Visible());
            }
        }

        async Task SingleDecals() {
            var types = CityElements.Types.Parsers.TypeParser.GetCustomElementTypes();
            if (!types.TryGetValue("decalSingle", out var type)) return;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var def = type.typeData.settings.controlPoints.Length == 1 ? type.typeData.settings.controlPoints[0] : null;
            var pointSettings = type.typeData.settings.point;
            Check("the single decal is a point type with a rotatable and scalable center point, projected on the surface", pointSettings != null &&
                def != null && def.name == "center" && def.rotatable && def.scalable && pointSettings.project &&
                pointSettings.PositionAxes().SequenceEqual(new[] { true, false, true }) && pointSettings.RotationAxes().SequenceEqual(new[] { false, true, false }) &&
                pointSettings.ScaleAxes().SequenceEqual(new[] { true, false, true }));
            //the menu entry creates the element with one click
            builder.ReloadCreateMenu();
            var menu = CallBuilder<MenuBar>("GetMenuBar").FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>()
                .First(b => b.Text == StringManager.Get("CREATE")).GetPopup();
            var shortLabel = StringManager.Get("CREATE_DECAL_SINGLE_MENU");
            var submenu = Enumerable.Range(0, menu.ItemCount).Select(i => menu.GetItemSubmenuNode(i))
                .First(s => s != null && Enumerable.Range(0, s.ItemCount).Any(i => s.GetItemText(i) == shortLabel));
            var entry = Enumerable.Range(0, submenu.ItemCount).First(i => submenu.GetItemText(i) == shortLabel);
            submenu.EmitSignal(PopupMenu.SignalName.IdPressed, submenu.GetItemId(entry));
            History.UndoManager.Record("place single decal", () => builder.customElementPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-640, 0, -640) }));
            var placed = builder.customElementPlacer.GetElement();
            Check("one click creates a single decal with the default scale and ends the placement", placed != null && placed.TypeName == "decalSingle" &&
                placed.GetAllPoints().Count == 1 && builder.customElementPlacer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None &&
                placed.GetAllPoints()[0].GetScale().DistanceTo(new Vector3(2, 1, 10)) < 1e-4f && GO.GetName(placed).StartsWith(StringManager.Get("CREATE_DECAL_SINGLE")));
            if (placed != null) await Call("customElement.delete", new { element = History.ElementRegistry.GetId(placed) });
            builder.CloseAllPanels();

            var id = (await Call("customElement.create", new { type = "decalSingle", name = "Single decal test",
                state = new { texture = "texture/decal_zigzag_l.tex" }, points = new { center = new { position = new[] { -620, 0, -600 } } } })).Value<int>();
            CustomElement Element() => History.UndoManager.GetAliveElement(id) as CustomElement;
            TerrainPoint Center() => Element().GetLeaves()[0].leaf.point;
            Check("a point created by the API has the default scale of the type", Center().GetScale().DistanceTo(new Vector3(2, 1, 10)) < 1e-4f);
            //the quad: its extents, and its height above the point
            (Vector3 size, float lift) Quad() {
                var mesh = GameEngine.Mesh.RetrieveMesh(GameEngine.Mesh.RetrieveMeshRenderer(GO.GetObject(Element().generator))) as ArrayMesh;
                var v = GameEngine.Mesh.GetVertices(mesh);
                if (v.Length != 4) return (new Vector3(-1, -1, -1), 0);
                var min = v.Aggregate((a, b) => new Vector3(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Min(a.Z, b.Z)));
                var max = v.Aggregate((a, b) => new Vector3(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z)));
                return (max - min, v.Min(p => p.Y) - Center().GetPoint().Y);
            }
            Check("a single decal draws a quad as wide as the scale X and as long as the scale Z, 1 cm above the point",
                Element().generator.DrawsMesh() && Quad().size.DistanceTo(new Vector3(2, 0, 10)) < 1e-3f && Mathf.Abs(Quad().lift - 0.01f) < 1e-3f);
            var quadMesh = GameEngine.Mesh.RetrieveMesh(GameEngine.Mesh.RetrieveMeshRenderer(GO.GetObject(Element().generator))) as ArrayMesh;
            Check("a single decal faces up", GameEngine.Mesh.GetNormals(quadMesh).All(n => n.Y > 0.99f));
            await CheckUndo("rotating and scaling a single decal", () => Call("customElement.movePoint", new { element = id, path = "center", position = new[] { -620, 0, -600 },
                rotation = new[] { 0, 90, 0 }, scale = new[] { 4, 1, 6 } }));
            await Call("customElement.movePoint", new { element = id, path = "center", position = new[] { -620, 0, -600 }, rotation = new[] { 0, 90, 0 }, scale = new[] { 4, 1, 6 } });
            await Call("city.flush");
            Check("the rotation and scale move the quad (yaw 90 turns the length to X)", Center().GetRotation().DistanceTo(new Vector3(0, 90, 0)) < 1e-2f &&
                Center().GetScale().DistanceTo(new Vector3(4, 1, 6)) < 1e-4f && Quad().size.DistanceTo(new Vector3(6, 0, 4)) < 1e-2f);
            var exported = await Call("elements.geometry", new { @ref = id });
            var exportedPoint = exported["element"]?["points"]?["center"];
            Check("the export has the rotation and the scale of the point", exportedPoint?["rotation"]?.Count() == 3 && Mathf.Abs(exportedPoint["rotation"][1].Value<float>() - 90) < 1e-2f &&
                exportedPoint["scale"]?.Count() == 3 && Mathf.Abs(exportedPoint["scale"][0].Value<float>() - 4) < 1e-4f);
            //edited as a mesh instance: the gizmo is on the element (its point has no handle), limited to the axes of the type, and
            //its height follows the surface
            var point = Center();
            builder.SelectObject(Element(), true, null);
            await Frames(3);
            var panel = builder.customElementEditorPanel;
            var panelPages = panel.GetType().BaseType.GetField("pages", flags).GetValue(panel) as System.Collections.IList;
            Check("automatic projection omits Project to ground for a single decal", !panelPages.Cast<EditorPanelPage>()
                .SelectMany(p => p.panel.FindChildren("*", "Button", true, false)).OfType<Godot.Button>()
                .Any(b => b.Text == StringManager.Get("PROJECT_TO_GROUND")));
            Check("selecting a single decal puts the gizmo on the element with no handle and not in the point transform panel", panel.ActiveSelf() &&
                builder.gizmo.mainTargetRoot == point.GetObject() && !GameEngine.Scriptable.Cast<Handle>(point.GetObject()).IsHandleActive() &&
                !((EditorPanels.TransformPropertiesEditorPanel)typeof(CityBuilderMenuBar).GetField("transformPropertiesEditorPanel", flags).GetValue(builder)).ActiveSelf());
            Check("the gizmo moves it on X and Z only, rotates it on Y only and scales it on X and Z",
                builder.gizmo.posAxesAllowed.SequenceEqual(new[] { true, false, true }) && builder.gizmo.rotAxesAllowed.SequenceEqual(new[] { false, true, false }) &&
                builder.gizmo.scaleAxesAllowed.SequenceEqual(new[] { true, false, true }));
            Check("the gizmo keys of the allowed modes are set", builder.gizmo.SetMoveType == Godot.Key.W && builder.gizmo.SetRotateType == Godot.Key.E && builder.gizmo.SetScaleType == Godot.Key.R);
            await Call("city.flush");
            var surface = Center().GetPoint().Y;
            //moved up with the gizmo, it goes back on the surface
            History.UndoManager.Record("lift the single decal", () => {
                History.UndoManager.TouchOwner(point.GetObject());
                GO.SetPosition(point.GetObject(), point.GetPoint() + new Vector3(0, 30, 0));
                M.FlagAsChanged();
            });
            await Frames(3);
            Check("a single decal is projected on the surface when moved", Mathf.Abs(point.GetPoint().Y - surface) < 0.01f);
            builder.customElementEditorPanel.Terminate();
            await Frames(2);
            Check("closing the editor gives the gizmo back", builder.gizmo.mainTargetRoot == null && builder.gizmo.transformPanel == null);
            await SingleDecalPresets(id);
            await Call("customElement.delete", new { element = id });
            //kept for the export (see ExportDecalNames)
            await Call("customElement.create", new { type = "decalSingle", name = "Single decal export", state = new { texture = "texture/decal_x_inter_l.tex" },
                points = new { center = new { position = new[] { -560, 0, -560 }, rotation = new[] { 0, 30, 0 }, scale = new[] { 3, 1, 8 } } } });
        }

        async Task SingleDecalPresets(int id) {
            const string category = "customElement/decalSingle";
            const string name = "Single decal size test";
            const string uiName = "Single decal panel size test";
            CustomElement Element() => History.UndoManager.GetAliveElement(id) as CustomElement;
            TerrainPoint Center() => Element().GetLeaves()[0].leaf.point;
            var position = Center().GetPoint();
            var size = Center().GetScale();
            var rotation = Center().GetRotation();
            var settings = Element().Settings.point;
            await Call("state.saveAsPreset", new { @ref = id, name });
            var saved = (await Call("presets.get", new { category, name })).ToObject<ObjectState>();
            Check("single decal presets capture the actual scale and exclude position and rotation", saved.Vector3("pointScale").DistanceTo(size) < 1e-4f
                && !saved.properties.ContainsKey("pointRotation") && !saved.properties.ContainsKey("points"));
            await Call("customElement.movePoint", new { element = id, path = "center", position = SplinePosition(position),
                scale = new[] { 7, 1, 9 }, rotation = new[] { 0, 45, 0 } });
            await CheckUndo("applying a single decal scale preset", () => Call("state.applyPreset", new { @ref = id, name }));
            await Call("state.applyPreset", new { @ref = id, name });
            Check("loading a single decal preset restores scale and preserves position and rotation", Center().GetScale().DistanceTo(size) < 1e-4f
                && Center().GetPoint().DistanceTo(position) < 0.01f && Center().GetRotation().DistanceTo(new Vector3(0, 45, 0)) < 0.01f);

            //Save through the panel, then create another decal from the remembered preset and edit its draft scale.
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var panel = builder.customElementEditorPanel;
            builder.SelectObject(Element(), true, null);
            await Frames(2);
            var pages = (System.Collections.IList)typeof(EditorPanel).GetField("pages", flags).GetValue(panel);
            pages.Cast<EditorPanelPage>().SelectMany(p => p.panel.FindChildren("*", "Button", true, false)).OfType<Godot.Button>()
                .First(b => b.Text == StringManager.Get("SAVE_PRESET")).EmitSignal(BaseButton.SignalName.Pressed);
            var popup = (InputFieldPopup)typeof(CityBuilderMenuBar).GetField("inputFieldPopup", flags).GetValue(builder);
            var input = (LineEdit)typeof(InputFieldPopup).GetField("input", flags).GetValue(popup);
            input.Text = uiName;
            input.EmitSignal(LineEdit.SignalName.TextSubmitted, uiName);
            Check("saving through the single decal panel includes scale", PresetManager.GetPresetByName(uiName, category)?.Vector3("pointScale").DistanceTo(size) < 1e-4f);
            builder.CloseAllPanels();
            History.UndoManager.Record("start single decal preset creation", () => CallBuilder<object>("StartCustomElementCreation", "decalSingle", "CREATE_CUSTOM_ELEMENT"));
            await Frames(2);
            var draft = (ObjectState)panel.GetType().GetField("draft", flags).GetValue(panel);
            Check("new single decal creation remembers the selected size preset", draft.Name == uiName && draft.Vector3("pointScale").DistanceTo(size) < 1e-4f);
            pages = (System.Collections.IList)typeof(EditorPanel).GetField("pages", flags).GetValue(panel);
            var fields = pages.Cast<EditorPanelPage>().SelectMany(p => (Dictionary<EditorPanelElement, FieldStatus>)p.GetType().GetField("fieldElements", flags).GetValue(p));
            var scaleField = fields.Single(p => p.Value.fieldName == "scale");
            var x = (LineEdit)typeof(EditorPanelElements.VectorInputField).GetField("xField", flags).GetValue(scaleField.Key);
            Check("the draft size fields show the saved scale", float.Parse(x.Text) == size.X);
            x.EmitSignal(LineEdit.SignalName.EditingToggled, true);
            x.Text = "5";
            x.EmitSignal(LineEdit.SignalName.EditingToggled, false);
            History.UndoManager.Record("place sized single decal", () => builder.customElementPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-660, 0, -660) }));
            var placed = builder.customElementPlacer.GetElement();
            Check("one click applies the edited preset scale to the new point", placed.GetAllPoints()[0].GetScale().DistanceTo(new Vector3(5, size.Y, size.Z)) < 1e-4f);
            await Call("customElement.delete", new { element = History.ElementRegistry.GetId(placed) });

            await Call("state.applyPreset", new { @ref = id, name });
            await Call("presets.save", new { category, name, values = new { type = "decalSingle", pointScale = new { x = 6, y = 1, z = 8 } } });
            await CheckUndo("syncing single decal scale presets", () => Call("presets.sync", new { category, name }));
            await Call("presets.sync", new { category, name });
            Check("sync restores the saved scale while retaining the current rotation", Center().GetScale().DistanceTo(new Vector3(6, 1, 8)) < 1e-4f
                && Center().GetRotation().DistanceTo(new Vector3(0, 45, 0)) < 0.01f);
            try {
                settings.presetScale = false;
                settings.presetRotation = true;
                var configured = Operations.CustomElementPresets.Extract(Element());
                Check("rotation and scale preset inclusion are independently configurable", !configured.properties.ContainsKey("pointScale")
                    && configured.Vector3("pointRotation").DistanceTo(Center().GetRotation()) < 0.01f);
                configured.SetVector3("pointRotation", rotation);
                Operations.CustomElementPresets.Apply(Element(), configured);
                Check("an excluded scale is preserved while an opted-in rotation is applied", Center().GetScale().DistanceTo(new Vector3(6, 1, 8)) < 1e-4f
                    && Center().GetRotation().DistanceTo(rotation) < 0.01f);
            } finally { settings.presetScale = true; settings.presetRotation = false; }
            var legacy = new ObjectState();
            legacy.SetStr("type", "decalSingle");
            Operations.CustomElementPresets.Apply(Element(), legacy);
            Check("older size-less presets use the type's default scale", Center().GetScale().DistanceTo(new Vector3(2, 1, 10)) < 1e-4f);
        }

        //saved and reloaded: the rotation and scale of the point
        async Task SingleDecalReload() {
            if (!CityElements.Types.Parsers.TypeParser.GetCustomElementTypes().ContainsKey("decalSingle")) return;
            var id = (await Call("customElement.create", new { type = "decalSingle", name = "Single decal reload", state = new { texture = "texture/decal_zigzag_l.tex" },
                points = new { center = new { position = new[] { -610, 0, -590 }, rotation = new[] { 10, 20, 30 }, scale = new[] { 3, 1, 5 } } } })).Value<int>();
            await Call("city.save");
            await Call("city.reload");
            var preset = PresetManager.GetPresetByName("Single decal size test", "customElement/decalSingle");
            Check("single decal scale presets survive save and reload", preset != null && preset.Vector3("pointScale").DistanceTo(new Vector3(6, 1, 8)) < 1e-4f);
            var loaded = M.customElements.FirstOrDefault(e => !e.IsDeleted() && GO.GetName(e) == "Single decal reload");
            var p = loaded?.GetLeaves()[0].leaf.point;
            Check("save/reload keeps the rotation and scale of a single decal", p != null && p.GetRotation().DistanceTo(new Vector3(10, 20, 30)) < 1e-2f &&
                p.GetScale().DistanceTo(new Vector3(3, 1, 5)) < 1e-4f && loaded.generator.DrawsMesh());
            if (loaded != null) await Call("customElement.delete", new { element = History.ElementRegistry.GetId(loaded) });
        }

        async Task IntersectionDecalPlacement() {
            var junction = M.intersections.First(i => !i.IsDeleted() && !Operations.CustomElements.IsGhost(i) && i.roads.Count >= 3);
            var center = GO.GetPosition(junction.point);
            var placer = new ElementPlacer.CustomElementPlacer(M) {
                createType = "decal", placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.Create
            };
            await ToSignal(GetTree(), "physics_frame"); await Frames(2);
            //the grid covers the intersection (at the sidewalk height): the decals are placed under it, as their sections
            var expected = new List<Vector3>();
            foreach (var offset in new[] { new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1) }) {
                var origin = center + offset + Vector3.Up * 100;
                var found = ClickRay(placer, origin, Vector3.Down, out var hit, 200, placer.GetLayerMasks()[2]);
                Check("decal placement ray hits the intersection surface", found && hit.collider == junction.generator);
                if (!found || hit.collider != junction.generator) continue;
                expected.Add(hit.point);
                placer.ShowOnMap(new List<GameEngine.Raycast.RaycastHit?> { null, null, hit });
                var preview = (Node3D)typeof(ActionHandlerManager).GetField("handle",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                Check("decal hover follows the intersection surface under the mouse", GO.GetPosition(preview).DistanceTo(hit.point) < .002f);
                History.UndoManager.Record("place decal on intersection", () => placer.PlaceAt(hit));
                var points = placer.GetElement().GetLeaves().Select(l => l.leaf.point).ToArray();
                Check("decal points keep their distinct intersection hit positions without corner attachments", points.Length == expected.Count
                    && points.Select((p, i) => p.anchor == null && p.GetPoint().DistanceTo(expected[i]) < .002f).All(matches => matches));
            }
            var element = placer.GetElement();
            if (element == null) return;
            try {
                var anchor = junction.anchorManager.GetTerrainAnchors().First();
                placer.ShowOnMap(new List<GameEngine.Raycast.RaycastHit?> {
                    new GameEngine.Raycast.RaycastHit { collider = anchor.GetObject(), point = anchor.GetPoint() }, null, null
                });
                Check("deliberate intersection anchor hits still preview the anchor", Actions.Helpers.GetAnchorInfo(anchor.GetObject(), center).pos == anchor.GetPoint());
                History.UndoManager.Record("snap decal to intersection anchor", () => placer.PlaceAt(new GameEngine.Raycast.RaycastHit {
                    collider = anchor.GetObject(), point = anchor.GetPoint()
                }));
                Check("deliberate intersection anchor hits still attach decal points", element.GetLeaves()[^1].leaf.point.anchor == anchor);
                var shared = element.GetLeaves()[0].leaf.point;
                Check("deliberate point hits still share the clicked point", Operations.CustomElements.PointFromClick(M, shared.GetObject(), center) == shared);
                var link = GameEngine.Scriptable.FindRecursive<PointLink>(anchor.GetObject());
                if (link != null) {
                    var position = (anchor.GetPoint() + GO.GetPosition(link.next)) / 2;
                    History.UndoManager.Record("snap decal to intersection edge", () => placer.PlaceAt(new GameEngine.Raycast.RaycastHit { collider = link, point = position }));
                    Check("deliberate edge hits still attach decal points to the edge", element.GetLeaves()[^1].leaf.point.anchor is LineAnchor
                        && element.GetLeaves()[^1].leaf.point.GetPoint().DistanceTo(position) < .002f);
                }
            } finally {
                placer.SetElement(null);
                await Call("customElement.delete", new { element = History.ElementRegistry.GetId(element) });
            }
        }

        async Task RailDecalGeneration() {
            var intersection = M.intersections.First(i => !i.IsDeleted() && !Operations.CustomElements.IsGhost(i) && i.roads.Count >= 2);
            var converter = ConverterCatalog.Get("generateRailDecals");
            var iid = History.ElementRegistry.GetId(intersection);
            var center = GO.GetPosition(intersection.point);
            var extra = (await Call("roads.create", new { points = new object[] { new { intersection = iid }, new { position = SplinePosition(center + new Vector3(100, 0, 100)) } } })).Value<int>();
            var roads = intersection.roads.Distinct().ToArray();
            var previous = roads.Select(r => r.state.Bool("cableCarLine")).ToArray();
            History.UndoManager.Record("edit rail intersection", () => builder.SelectObject(intersection.geo, true, null));
            var panel = CallBuilder<EditorPanel>("GetActivePanel");
            var buttons = (List<(EditorPanelElements.Button button, ConverterCatalog.ConverterInfo converter)>)typeof(EditorPanel)
                .GetField("conversionButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(panel);
            var railButton = buttons.FirstOrDefault(b => b.converter.name == "generateRailDecals").button;
            Check("the intersection panel exposes the rail-generation action", railButton != null);
            for (int count = 0; count <= roads.Length; count++) {
                History.UndoManager.Record("rail test flags", () => {
                    for (int i = 0; i < roads.Length; i++) {
                        History.UndoManager.Touch(roads[i]); roads[i].state.SetBool("cableCarLine", i < count);
                    }
                    M.FlagAsChanged();
                });
                M.ProcessUpdate();
                Check("rail generator availability with " + count + " tram roads", converter.IsEnabled(intersection) == (count >= 2));
                Check("rail button availability with " + count + " tram roads", railButton != null && railButton.GetInteractable() == (count >= 2));
            }
            await Call("city.flush");
            await CheckUndo("generating independent intersection rail decals", () => Call("elements.convert", new { elements = new[] { iid }, converter = "generateRailDecals" }));
            var generated = M.customElements.Where(e => !e.IsDeleted() && e.TypeName == "decal" && e.state.Str("texture").Contains("r4i_rails_f")).ToList();
            Check("rail decals are editable independent elements", generated.Count >= 2 && generated.All(e => e.source == null && e.container == null) && !Operations.CustomElements.IsGhost(intersection));
            var ids = (JArray)await Call("elements.convert", new { elements = new[] { iid }, converter = "generateRailDecals" });
            Check("repeated rail generation appends another set", ids.Count == generated.Count);
            foreach (var id in ids.Values<int>()) await Call("customElement.delete", new { element = id });
            for (int i = 0; i < roads.Length; i++) await Call("state.setParam", new { @ref = History.ElementRegistry.GetId(roads[i]), name = "cableCarLine", value = previous[i] });
            await Call("roads.delete", new { road = extra });
        }

        async Task SplineComponents() {
            var types = CityElements.Types.Parsers.TypeParser.GetCustomElementTypes();
            var source = Newtonsoft.Json.JsonConvert.DeserializeObject<CityElements.Types.CustomElementType>(
                Newtonsoft.Json.JsonConvert.SerializeObject(types["decal"].typeData));
            var settings = source.settings.spline;
            settings.projectPoints = settings.projectSections = false;
            settings.maxSectionLength = 0; settings.splitCondition = null;
            settings.variableSections = true;
            settings.sectionVertices = new object[] { "-varyingWidth/2", "varyingWidth/2" };
            settings.components[0].condition = "drawStrip";
            source.parameters.parameters = source.parameters.parameters.Append(new CityElements.Types.Parameter { name = "drawStrip", type = "bool" }).ToArray();
            source.parameters.dynamicDefinitions = new[] { new CityElements.Types.Definition { name = "varyingWidth", type = "float", value = "width+segment" } };
            var mesh = source.splineComponents[0];
            mesh.mainMesh.uvs = new[] { "vec2(0,z)", "vec2(1,z)" };
            mesh.startMesh = new CityElements.Types.RoadComponentMesh { vertices = new[] { "v0", "v1", "v0+worldUp" },
                uvs = new[] { "vec2(0,0)", "vec2(1,0)", "vec2(0,1)" }, faces = new object[] { 0, 1, 2 }, facesTextures = new object[] { 0 } };
            mesh.endMesh = mesh.startMesh;
            const string key = "__testSplineComponents";
            types[key] = new CityElements.Types.Runtime.CustomElementType(source, key);
            int id = -1;
            try {
                id = (await Call("customElement.create", new { type = key, state = new { width = 3, drawStrip = true, texture = "texture/decal_zigzag_l.tex", curveType = 2 },
                    points = new { spline = new[] { new { position = new[] { 600, 0, 600 } }, new { position = new[] { 600, 0, 610 } }, new { position = new[] { 600, 0, 620 } } } } })).Value<int>();
                var element = (CustomElement)History.UndoManager.GetAliveElement(id);
                Check("spline components evaluate dynamic definitions per section", Mathf.Abs(element.splineData.sections[0].vertices[1][0] - element.splineData.sections[0].vertices[0][0]) == 4 &&
                    Mathf.Abs(element.splineData.sections[2].vertices[1][0] - element.splineData.sections[2].vertices[0][0]) == 6);
                var indices = 0;
                for (int i = 0; i < element.generator.m.GetSurfaceCount(); i++) indices += element.generator.m.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Index].AsInt32Array().Length;
                Check("independent spline spans have only one start and end cap", indices == 18);
                await Call("state.setParam", new { @ref = id, name = "drawStrip", value = false });
                Check("spline component conditions can hide the mesh", !element.generator.DrawsMesh());
            } finally {
                if (id >= 0) await Call("customElement.delete", new { element = id });
                types.Remove(key);
            }
            var overlay = (await Call("customElement.create", new { type = "decal", points = new { spline = new[] {
                new { position = new[] { 650, 0, 600 } }, new { position = new[] { 650, 0, 610 } } } } })).Value<int>();
            var surface = (await Call("customElement.create", new { type = "triangleFan", mesh = new {
                vertices = new[] { new { position = new[] { 640, 10, 590 } }, new { position = new[] { 640, 10, 620 } },
                    new { position = new[] { 660, 10, 620 } }, new { position = new[] { 660, 10, 590 } } }, triangles = new[] { 0, 1, 2, 0, 2, 3 }
            } })).Value<int>();
            var overlayElement = (CustomElement)History.UndoManager.GetAliveElement(overlay);
            Check("spline sections project onto custom element surfaces created later (" + overlayElement.splineData.sections[0].center[1] + ")", overlayElement.splineData.sections.All(s => Mathf.Abs(s.center[1] - 10) < 0.001f));
            await Call("customElement.move", new { element = surface, offset = new[] { 0, 3, 0 } });
            Check("moving a custom element surface refreshes projected spline sections (" + overlayElement.splineData.sections[0].center[1] + ")", overlayElement.splineData.sections.All(s => Mathf.Abs(s.center[1] - 13) < 0.001f));
            await Call("customElement.delete", new { element = overlay });
            await Call("customElement.delete", new { element = surface });
        }

        async Task ReloadSplines() {
            var before = M.customElements.Where(e => e.TypeName == "decal" && !e.IsDeleted()).Select(e => new {
                name = GO.GetName(e), points = e.GetAllPoints().Select(p => p.GetPoint()).ToArray(), pieces = e.splineData.pieces.Length
            }).ToArray();
            await Call("city.save");
            await Call("city.reload");
            Check("save/reload rebuilds spline geometry and preserves control points", before.All(record => {
                var e = M.customElements.FirstOrDefault(e => GO.GetName(e) == record.name && e.TypeName == "decal");
                return e != null && e.GetAllPoints().Select(p => p.GetPoint()).SequenceEqual(record.points) && e.splineData.pieces.Length == record.pieces;
            }));
        }
    }
}

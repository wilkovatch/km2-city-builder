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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;

namespace Api {
    public partial class ApiSelfTest {
        //The movable bridges of the core: the models in the preview (from the geometry script) and the points on the free road
        //ends. What is written for the game is checked by the tools of the core (_test_bridges.py).
        async Task Bridges() {
            Log("movable bridges");
            const float z = -700;
            var west = (await Call("roads.create", new { points = new[] { Pos(-400, z), Pos(-330, z) } })).Value<int>();
            var east = (await Call("roads.create", new { points = new[] { Pos(-250, z), Pos(-180, z) } })).Value<int>();
            var id = (await Call("customElement.create", new { type = "bridge", name = "Bridge test", position = V(-290, 0, z + 40) })).Value<int>();
            await Call("city.flush");
            CustomElement Bridge() => M.customElements.First(e => !e.IsDeleted() && GO.GetName(e) == "Bridge test");
            TerrainPoint Point(string name) => global::CustomElements.PointTree.Leaves(Bridge().points).First(l => l.path == name).leaf.point;
            bool Near(Vector3 a, Vector3 b) => a.DistanceTo(b) < 0.01f;
            bool NearFloat(float a, float b) => Mathf.Abs(a - b) < 0.01f;
            float Angle(float a, float b) => Mathf.Abs(Mathf.Wrap(a - b, -180, 180));

            //the road ends: only with the option, for the free ends
            async Task<List<JToken>> RoadEnds(bool customElements = true) => ((JArray)await Call("anchors.list", new { customElements }))
                .Where(a => a["roadEnd"].Value<bool>()).ToList();
            Check("the road ends are listed only with customElements", (await RoadEnds(false)).Count == 0);
            var ends = await RoadEnds();
            Vector3 PositionOf(JToken anchor) => anchor["position"].ToObject<Vector3>(Newtonsoft.Json.JsonSerializer.Create(Json.Settings));
            JToken EndAt(float x) => ends.FirstOrDefault(a => Near(PositionOf(a), new Vector3(x, 0, z)));
            Check("each free road has an anchor on both ends", ends.Count(a => a["of"].Value<int>() == west) == 2 && ends.Count(a => a["of"].Value<int>() == east) == 2
                && EndAt(-400) != null && EndAt(-330) != null && EndAt(-250) != null && EndAt(-180) != null);
            Check("the road ends come after the other anchors of a road", ends.Where(a => a["of"].Value<int>() == west).All(a =>
                a["index"].Value<int>() >= History.ElementRegistry.Get<Road>(west).anchorManager.GetTerrainAnchors().Count - 2));

            TerrainAnchor EndAnchor(Road road, bool start) => road.anchorManager.GetTerrainAnchors().Where(a => a.roadEnd).ElementAt(start ? 0 : 1);
            var westRoad = History.ElementRegistry.Get<Road>(west);
            bool Shown(TerrainAnchor anchor) => anchor.GetObject().Visible;
            M.ShowAnchors(true, true);
            Check("the road ends aren't shown for the other types", !Shown(EndAnchor(westRoad, true)) && !Shown(EndAnchor(westRoad, false))
                && westRoad.anchorManager.GetTerrainAnchors().Any(a => !a.roadEnd && Shown(a)));
            M.ShowAnchors(true, true, false, true);
            Check("the free road ends are shown for the types asking for them", Shown(EndAnchor(westRoad, true)) && Shown(EndAnchor(westRoad, false)));
            await Call("view.camera.set", new { position = V(-290, 60, z - 60), target = V(-290, 0, z) });
            await Frames(2);
            Screenshot("bridge_road_ends.png");
            M.ShowAnchors(true);
            Check("the road ends are never for the terrain patches and the building lines", !Shown(EndAnchor(westRoad, true)));
            M.ShowAnchors(false);

            //another type can't be attached to a road end
            var fan = (await Call("customElement.create", new { type = "triangleFan", points = new JObject(),
                mesh = new { vertices = new object[] { new { position = V(-400, 0, -800) }, new { position = V(-390, 0, -800) }, new { position = V(-390, 0, -790) } }, triangles = new[] { 0, 1, 2 } } })).Value<int>();
            await Expect("a type without roadEndAnchors can't use a road end", "invalid_operation", "customElement.attachPoint",
                new { element = fan, vertex = 0, to = new { anchor = new { of = EndAt(-330)["of"], index = EndAt(-330)["index"] } } });
            await Call("customElement.delete", new { element = fan });

            //the points follow the road end, with undo
            await CheckUndo("attaching a bridge point to a road end", async () =>
                await Call("customElement.attachPoint", new { element = id, path = "start", to = new { anchor = new { of = EndAt(-330)["of"], index = EndAt(-330)["index"] } } }));
            await Call("customElement.attachPoint", new { element = id, path = "finish", to = new { anchor = new { of = EndAt(-250)["of"], index = EndAt(-250)["index"] } } });
            await Call("city.flush"); await Frames(2);
            Check("the points go on the road ends", Near(Point("start").GetPoint(), new Vector3(-330, 0, z)) && Near(Point("finish").GetPoint(), new Vector3(-250, 0, z)));
            Check("the points are on the anchors of the road ends", (Point("start").anchor as TerrainAnchor)?.roadEnd == true);

            //the preview: the hinges at the points, the leaves turned along the line between them and into the opening, as in the game
            await Call("state.setParam", new { @ref = id, name = "mesh", value = "geometry/giz_bridge01_l.pkg" });
            await Call("city.flush"); await Frames(2);
            var withModels = MeshManager.GetSize("geometry/giz_bridge01_l.pkg").HasValue;
            var leaves = Bridge().generator.placements;
            Check("a paired bridge has two leaves", leaves.Count == 2);
            void CheckLeaves(string what, Vector3 a, Vector3 b, float length, float pitch) {
                leaves = Bridge().generator.placements;
                for (int i = 0; i < 2; i++) {
                    var origin = i == 0 ? a : b;
                    var other = i == 0 ? b : a;
                    var away = (origin - other).Normalized();
                    var yaw = Mathf.RadToDeg(Mathf.Atan2(away.X, away.Z));
                    var rotation = leaves[i].rotation;
                    Check(what + ": the leaf " + i + " is along the bridge, not at any other yaw (" + rotation.Y + ", expected " + yaw + ")", Angle(rotation.Y, yaw) < 0.01f);
                    Check(what + ": the leaf " + i + " has the pitch of the pose", NearFloat(rotation.X, pitch));
                    //the end of the model at the hinge, the other one (raised when open) toward the opening
                    var hinge = leaves[i].position + away * (length * 0.5f * Mathf.Cos(Mathf.DegToRad(pitch))) + Vector3.Down * (length * 0.5f * Mathf.Sin(Mathf.DegToRad(pitch)));
                    Check(what + ": the leaf " + i + " turns around the hinge", hinge.DistanceTo(origin + Vector3.Down * 0.3f) < 0.01f);
                }
            }
            CheckLeaves("along X", new Vector3(-330, 0, z), new Vector3(-250, 0, z), 30.000004f, 0);
            await Call("state.setParam", new { @ref = id, name = "preview", value = 2 });
            await Call("city.flush"); await Frames(2);
            CheckLeaves("along X, open", new Vector3(-330, 0, z), new Vector3(-250, 0, z), 30.000004f, 27);
            await Call("state.setParam", new { @ref = id, name = "preview", value = 1 });

            //any direction: these yaws were wrong with the two arguments of math.atan, ignored by the runtime
            var (startBefore, finishBefore) = (Point("start").GetPoint(), Point("finish").GetPoint());
            foreach (var (dx, dz) in new[] { (60f, 60f), (-60f, 60f), (60f, -60f), (-60f, -60f), (0f, -80f), (10f, 80f) }) {
                await Call("customElement.movePoint", new { element = id, path = "start", position = V(-290, 0, -600) });
                await Call("customElement.movePoint", new { element = id, path = "finish", position = V(-290 + dx, 0, -600 + dz) });
                await Call("city.flush"); await Frames(2);
                CheckLeaves("direction " + dx + "," + dz, new Vector3(-290, 0, -600), new Vector3(-290 + dx, 0, -600 + dz), 30.000004f, 0);
            }
            await Call("customElement.attachPoint", new { element = id, path = "start", to = new { anchor = new { of = EndAt(-330)["of"], index = EndAt(-330)["index"] } } });
            await Call("customElement.attachPoint", new { element = id, path = "finish", to = new { anchor = new { of = EndAt(-250)["of"], index = EndAt(-250)["index"] } } });
            await Call("city.flush"); await Frames(2);
            Check("attached again", Near(Point("start").GetPoint(), startBefore) && Near(Point("finish").GetPoint(), finishBefore));

            if (withModels) {
                //the size of the chosen model decides the hinge
                await Call("state.setParam", new { @ref = id, name = "mesh", value = "geometry/giz_bridge02_l.pkg" });
                await Call("city.flush"); await Frames(2);
                CheckLeaves("another model", new Vector3(-330, 0, z), new Vector3(-250, 0, z), MeshManager.GetSize("geometry/giz_bridge02_l.pkg").Value.Z, 0);
                Check("the models are the chosen ones", Bridge().generator.placements.All(p => p.mesh == "geometry/giz_bridge02_l.pkg" && p.obj != null));
                Check("the leaves use the transform of the banger data", Bridge().generator.placements.All(p => p.meshTransform && !p.autoYOffset));
            }
            var errors = Bridge().GetCustomElementType().scriptHost.ErrorCount;
            await Call("state.setParam", new { @ref = id, name = "mesh", value = "geometry/not_a_real_model.pkg" });
            await Call("city.flush"); await Frames(2);
            Check("a mesh that isn't there shows the fallback model, with no script errors", Bridge().generator.placements.Count == 2 && Bridge().GetCustomElementType().scriptHost.ErrorCount == errors);
            await Call("state.setParam", new { @ref = id, name = "mesh", value = "geometry/giz_bridge01_l.pkg" });

            //the model generated from the roads the points are attached to
            var free = (await Call("customElement.create", new { type = "bridge", name = "Free bridge", position = V(-290, 0, z - 200) })).Value<int>();
            await Call("city.flush");
            await Expect("a bridge on no road can't generate a model", "not_accepted", "elements.convert", new { elements = new[] { free }, converter = "generateBridgeMesh" });
            await Call("customElement.delete", new { element = free });
            var eastRoadId = History.ElementRegistry.GetId(M.roads.First(r => r.anchorManager.GetTerrainAnchors().Any(a => a.roadEnd && Near(a.GetPoint(), new Vector3(-250, 0, z)))));
            var exported = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "km2cb_bridges_" + System.Guid.NewGuid().ToString("N") + ".json");
            await Call("city.exportJson", new { path = exported });
            var attachedPoint = JObject.Parse(System.IO.File.ReadAllText(exported))["customElements"].First(e => e["data"]["name"].Value<string>() == "Bridge test")["points"]["start"]["anchor"];
            System.IO.File.Delete(exported);
            Check("the export tells the road a point is on", attachedPoint?["kind"]?.Value<string>() == "road" && attachedPoint["roadEnd"].Value<bool>()
                && attachedPoint["insideDirection"].ToObject<float[]>().Length == 3 && Mathf.Abs(attachedPoint["insideDirection"][0].Value<float>() + 1) < 0.01f);
            string Param(string name) => Bridge().state.Str(name, null);
            string ModelFile(string folder, string suffix) => System.IO.Path.Combine(PreferencesManager.workingDirectory, folder, System.IO.Path.GetFileNameWithoutExtension(Param("mesh")) + suffix);
            async Task Generate() {
                await Call("elements.convert", new { elements = new[] { id }, converter = "generateBridgeMesh" });
                await Call("city.flush"); await Frames(2);
            }
            await CheckUndo("generating the model of a bridge", Generate);
            await Call("city.flush"); await Frames(2);
            var first = Param("mesh");
            var width = History.ElementRegistry.Get<Road>(west).state.Float("totalWidth");
            Check("the model is assigned to the bridge (" + first + ")", first.StartsWith("geometry/bridge_") && Param("generatedMesh") == first && Bridge().generator.placements.All(p => p.mesh == first && p.obj != null));
            Check("the files of the model are in the city", System.IO.File.Exists(ModelFile("geometry", ".pkg")) && System.IO.File.Exists(ModelFile("bound", "_BOUND.bnd")) && System.IO.File.Exists(ModelFile("tune/banger", ".dgBangerData")));
            Check("each leaf reaches the middle", NearFloat(Bridge().state.Float("generatedLength"), 40));
            Check("the model has the size of the road, with the railings", NearFloat(MeshManager.GetSize(first).Value.X, width + 0.5f) && NearFloat(MeshManager.GetSize(first).Value.Z, 40));
            CheckLeaves("generated model", new Vector3(-330, 0, z), new Vector3(-250, 0, z), 40, 0);
            await Call("view.camera.set", new { position = V(-290, 40, z - 60), target = V(-290, 0, z) });
            await Frames(2);
            //the button of the panel: enabled only while a point is on a road
            History.UndoManager.Record("edit bridge", () => builder.SelectObject(Bridge(), true, null));
            await Frames(3);
            var meshConverter = ((List<(EditorPanelElements.Button button, ConverterCatalog.ConverterInfo converter)>)typeof(EditorPanel)
                .GetField("conversionButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(CallBuilder<EditorPanel>("GetActivePanel")))
                .FirstOrDefault(b => b.converter.name == "generateBridgeMesh").converter;
            Check("the panel of the bridge has the button of the model", meshConverter != null && meshConverter.IsEnabled(Bridge()));
            Screenshot("bridge_generated.png");
            await Call("state.setParam", new { @ref = id, name = "trussTexture", value = "texture/my_truss.tex" });
            await Generate();
            Check("the textures of the bridge go in the model", Param("mesh") != first && System.Text.Encoding.ASCII.GetString(System.IO.File.ReadAllBytes(ModelFile("geometry", ".pkg"))).Contains("my_truss"));
            await Call("state.setParam", new { @ref = id, name = "trussTexture", value = "" });
            await Call("state.setParam", new { @ref = eastRoadId, name = "totalWidth", value = width + 6 });
            await Call("city.flush"); await Frames(2);
            await Generate();
            Check("with two different roads it fits the widest one", NearFloat(MeshManager.GetSize(Param("mesh")).Value.X, width + 6.5f));
            await Call("state.setParam", new { @ref = eastRoadId, name = "totalWidth", value = width });
            await Call("city.flush"); await Frames(2);
            //a road at an angle: the ends of the leaves along the end of the road, the model longer than the distance of its ends
            await Call("roads.movePoint", new { road = west, index = 0, position = V(-400, 0, z + 70) });
            await Call("roads.movePoint", new { road = eastRoadId, index = 1, position = V(-180, 0, z - 70) });
            await Call("city.flush"); await Frames(2);
            await Generate();
            Check("at an angle the model is longer than the leaf", MeshManager.GetSize(Param("mesh")).Value.Z > Bridge().state.Float("generatedLength") + 1);
            CheckLeaves("generated model at an angle", new Vector3(-330, 0, z), new Vector3(-250, 0, z), 40, 0);
            Screenshot("bridge_generated_angle.png");
            await Call("roads.movePoint", new { road = west, index = 0, position = V(-330, 0, z - 80) });
            await Call("roads.movePoint", new { road = eastRoadId, index = 1, position = V(-250, 0, z - 80) });
            await Call("city.flush"); await Frames(2);
            await Expect("a road across the bridge is refused", "converter_failed", "elements.convert", new { elements = new[] { id }, converter = "generateBridgeMesh" });
            await Call("roads.movePoint", new { road = west, index = 0, position = V(-400, 0, z) });
            await Call("roads.movePoint", new { road = eastRoadId, index = 1, position = V(-180, 0, z) });
            await Call("state.setParam", new { @ref = id, name = "mesh", value = "geometry/giz_bridge01_l.pkg" });
            await Call("city.flush"); await Frames(2);
            CheckLeaves("the size of the mesh for another model", new Vector3(-330, 0, z), new Vector3(-250, 0, z), 30.000004f, 0);

            //a road end moved or connected keeps the point
            await Call("roads.movePoint", new { road = west, index = 1, position = V(-335, 0, z) });
            await Call("city.flush"); await Frames(2);
            Check("the point follows the road end", Near(Point("start").GetPoint(), new Vector3(-335, 0, z)));
            CheckLeaves("after moving the road", new Vector3(-335, 0, z), new Vector3(-250, 0, z), 30.000004f, 0);
            await Call("roads.movePoint", new { road = west, index = 1, position = V(-330, 0, z) });
            await Call("city.flush"); await Frames(2);

            //round trip
            await Call("city.save"); await Call("city.reload");
            await Call("city.flush"); await Frames(2);
            Check("the points stay on the road ends in a saved city", (Point("start").anchor as TerrainAnchor)?.roadEnd == true && (Point("finish").anchor as TerrainAnchor)?.roadEnd == true
                && Near(Point("start").GetPoint(), new Vector3(-330, 0, z)) && Near(Point("finish").GetPoint(), new Vector3(-250, 0, z)));
            Check("the bridge shows its model after loading", Bridge().generator.placements.Count == 2);
            await BridgeReplacement();
            await BridgePlacement(EndAnchor(M.roads.First(r => r.anchorManager.GetTerrainAnchors().Any(a => a.roadEnd && Near(a.GetPoint(), new Vector3(-400, 0, z)))), true), z);

            //a connected end isn't free anymore: not shown, but what's on it stays there
            var other = (await Call("roads.create", new { points = new object[] { new { roadEnd = new { road = History.ElementRegistry.GetId(M.roads.First(r => r.anchorManager.GetTerrainAnchors().Any(a => a.roadEnd && Near(a.GetPoint(), new Vector3(-180, 0, z))))), end = "end" } }, Pos(-180, z + 80) } })).Value<int>();
            await Call("city.flush"); await Frames(2);
            var eastRoad = M.roads.First(r => r.anchorManager.GetTerrainAnchors().Any(a => a.roadEnd && Near(a.GetPoint(), new Vector3(-250, 0, z))));
            M.ShowAnchors(true, true, false, true);
            Check("only the free ends are shown", Shown(EndAnchor(eastRoad, true)) && !Shown(EndAnchor(eastRoad, false)));
            M.ShowAnchors(false);
            await Call("city.flush");
            await Call("customElement.delete", new { element = History.ElementRegistry.GetId(Bridge()) });
            await Call("roads.delete", new { road = other });
            await Call("roads.delete", new { road = History.ElementRegistry.GetId(eastRoad) });
            await Call("roads.delete", new { road = History.ElementRegistry.GetId(M.roads.First(r => r.anchorManager.GetTerrainAnchors().Any(a => a.roadEnd && Near(a.GetPoint(), new Vector3(-330, 0, z))))) });
            await Call("city.flush");
        }

        //A new bridge from the Create menu: its points are placed one by one (placementPoints), each attached to what is
        //clicked, and the panel can place them again
        async Task BridgePlacement(TerrainAnchor roadEnd, float z) {
            var panel = builder.customElementEditorPanel;
            var placer = builder.customElementPlacer;
            const ElementPlacer.CustomElementPlacer.PlacementMode placePoints = ElementPlacer.CustomElementPlacer.PlacementMode.PlacePoints;
            TerrainPoint Point(CustomElement element, string name) => global::CustomElements.PointTree.Leaves(element.points).First(l => l.path == name).leaf.point;
            bool Near(Vector3 a, Vector3 b) => a.DistanceTo(b) < 0.01f;
            void Click(string label, Node3D collider, Vector3 point) =>
                History.UndoManager.Record(label, () => placer.PlaceAt(new GameEngine.Raycast.RaycastHit { collider = collider, point = point }));
            var before = M.customElements.Count(e => !e.IsDeleted());
            History.UndoManager.Record("start bridge placement", () => CallBuilder<object>("StartCustomElementCreation", "bridge", "CREATE_CUSTOM_ELEMENT"));
            await Frames(2);
            Check("the bridge placement shows the free road ends before the first click", placer.AnchorablePlacement && roadEnd.GetObject().Visible);
            Click("place the first hinge", roadEnd.GetObject(), roadEnd.GetPoint());
            await Frames(2);
            var bridge = placer.GetElement();
            Check("the first click creates the bridge with its first hinge on the road end", bridge != null && M.customElements.Count(e => !e.IsDeleted()) == before + 1
                && Point(bridge, "start").anchor == roadEnd && Near(Point(bridge, "start").GetPoint(), roadEnd.GetPoint()));
            Check("the second hinge waits on the first one", Near(Point(bridge, "finish").GetPoint(), roadEnd.GetPoint()) && Point(bridge, "finish").anchor == null
                && placer.placementMode == placePoints && placer.placeIndex == 1 && bridge.generator.placements.Count == 0);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var label = panel.GetType().GetField("selectedLabel", flags).GetValue(panel);
            var shown = (typeof(EditorPanelElements.Label).BaseType.GetMethod("GetControl", flags).MakeGenericMethod(typeof(Label))
                .Invoke(label, new object[] { null }) as Label)?.Text;
            Check("the panel names the point to place (" + shown + ")", panel.GetPointPlacementMode() == 1
                && shown == StringManager.Get("CE_PLACING_POINT").Replace("$POINT", StringManager.GetOrKey("BRIDGE_FINISH"))
                && placer.AnchorablePlacement && roadEnd.GetObject().Visible);
            Handle HandleOf(Node3D node) => GameEngine.Scriptable.Cast<Handle>(node);
            Check("the first hinge is marked, over the handle of the road end", HandleOf(Point(bridge, "start").GetObject()).HandleScale.X > HandleOf(roadEnd.GetObject()).HandleScale.X
                && Point(bridge, "start").GetObject().Visible && HandleOf(Point(bridge, "start").GetObject()).IsHandleActive());
            var hover = new Vector3(-440, 0, z + 20);
            placer.Apply(new List<GameEngine.Raycast.RaycastHit?> { null, null, new GameEngine.Raycast.RaycastHit { point = hover } });
            Check("a line goes from the first hinge to where the second would go", ActionHandlerManager.GuideLineShown);
            await Call("view.camera.set", new { position = V(-420, 50, z - 50), target = V(-400, 0, z) });
            await Frames(2);
            Screenshot("bridge_first_hinge.png");
            Click("place on the first hinge", Point(bridge, "start").GetObject(), roadEnd.GetPoint());
            Check("the other points of the bridge can't be clicked", placer.placementMode == placePoints && placer.placeIndex == 1);
            var ground = new Vector3(-460, 0, z);
            Click("place the second hinge", null, ground);
            await Frames(2);
            Check("the second click places the second hinge, free on the ground, and ends the placement", Near(Point(bridge, "finish").GetPoint(), ground)
                && Point(bridge, "finish").anchor == null && placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None && panel.GetPointPlacementMode() == 0);
            Check("the bridge shows its leaves once both hinges are placed", bridge.generator.placements.Count == 2);

            //placing the points again: the first one leaves the road end
            panel.SetPointPlacementMode(1);
            await Frames(1);
            Check("the panel places the points again from the first", placer.placementMode == placePoints && placer.placeIndex == 0);
            var moved = new Vector3(-470, 0, z + 10);
            Click("place the first hinge again", null, moved);
            Check("the first hinge leaves the road end", Near(Point(bridge, "start").GetPoint(), moved) && Point(bridge, "start").anchor == null && placer.placeIndex == 1);
            Click("place the second hinge on the road end", roadEnd.GetObject(), roadEnd.GetPoint());
            await Frames(2);
            Check("the second hinge goes on the road end", Point(bridge, "finish").anchor == roadEnd && placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None);
            await Call("city.flush");
            await Call("history.undo");
            await Call("city.flush");
            Check("undo takes back the last click only", M.customElements.Contains(bridge) && !bridge.IsDeleted() && Point(bridge, "finish").anchor == null
                && Near(Point(bridge, "finish").GetPoint(), ground) && Near(Point(bridge, "start").GetPoint(), moved));
            panel.Terminate();
            await Frames(1);
            Check("a placed bridge stays when its editor is closed", !bridge.IsDeleted() && !History.UndoManager.IsIncomplete(bridge));
            await Call("customElement.delete", new { element = History.ElementRegistry.GetId(bridge) });
            await Call("city.flush");

            //a bridge left with one hinge is deleted, as a road with one point: closing the editor, or undo
            async Task<CustomElement> FirstHinge() {
                History.UndoManager.Record("start bridge placement", () => CallBuilder<object>("StartCustomElementCreation", "bridge", "CREATE_CUSTOM_ELEMENT"));
                await Frames(2);
                Click("place the first hinge", null, new Vector3(-450, 0, z + 30));
                await Frames(2);
                return placer.GetElement();
            }
            var half = await FirstHinge();
            Check("a bridge with one hinge is incomplete", half != null && History.UndoManager.IsIncomplete(half));
            History.UndoManager.Record("end editing", () => panel.Terminate());
            await Frames(2);
            Check("closing the editor before the second hinge deletes the bridge", half.IsDeleted() && M.customElements.Count(e => !e.IsDeleted()) == before);
            half = await FirstHinge();
            builder.PerformUndo();
            await Frames(2);
            Check("undo before the second hinge removes the bridge", half.IsDeleted() && M.customElements.Count(e => !e.IsDeleted()) == before);
            half = await FirstHinge();
            Click("place the second hinge", null, new Vector3(-450, 0, z + 60));
            await Frames(2);
            Check("a bridge with both hinges isn't incomplete", !half.IsDeleted() && !History.UndoManager.IsIncomplete(half));
            await Call("city.flush");
            builder.PerformUndo();
            await Frames(2);
            Check("undoing the second hinge removes the whole bridge", half.IsDeleted() && M.customElements.Count(e => !e.IsDeleted()) == before);
            builder.PerformRedo();
            await Frames(2);
            var redone = M.customElements.FirstOrDefault(e => !e.IsDeleted() && e.TypeName == "bridge" && Near(e.GetCenter(), new Vector3(-450, 0, z + 45)));
            Check("redo brings it back with both hinges", redone != null && M.customElements.Count(e => !e.IsDeleted()) == before + 1 && !History.UndoManager.IsIncomplete(redone));
            if (redone != null) await Call("customElement.delete", new { element = History.ElementRegistry.GetId(redone) });
            await Call("city.flush");
        }

        //A race bridge replacing a city bridge (replacesReference): the city bridge is hidden while the race is selected
        async Task BridgeReplacement() {
            Log("race bridge replacing a city bridge");
            const float z = -900;
            var city = M.GetDummy<CityProperties>();
            var param = CityElements.Types.Parsers.TypeParser.GetCitySettings()["city"].typeData.parametersInfo.parameters.First(p => p.name == "circuitRaces");
            History.UndoManager.Record("add race", () => {
                History.UndoManager.Touch(city);
                Operations.States.AddArrayElement(city.state, param);
                M.FlagAsChanged();
            });
            M.ProcessUpdate();
            var race = city.state.Array<ObjectState>("circuitRaces")[^1];
            var owner = Operations.PropertyContainers.EnsureId(race);
            void Select(ObjectState state) {
                ArrayObject.activeElements["race"] = state;
                ArrayObject.activeElements["raceDifficulty"] = state?.State("policeCars");
                builder.NotifyVisibilityChange();
                M.ProcessUpdate();
            }
            var cityId = (await Call("customElement.create", new { type = "bridge", name = "City bridge", position = V(-290, 0, z) })).Value<int>();
            var cityBridges = new[] { cityId, (await Call("customElement.create", new { type = "bridge", name = "Other city bridge", position = V(-290, 0, z - 100) })).Value<int>() };
            var raceId = (await Call("customElement.create", new { type = "bridge", name = "Race bridge", position = V(-290, 0, z + 5) })).Value<int>();
            var raceBridge = History.ElementRegistry.Get<CustomElement>(raceId);
            Operations.PropertyContainers.Assign(raceBridge.state, owner);
            M.FlagAsChanged();
            await Call("city.flush");
            bool Shown(int id) => History.ElementRegistry.Get<CustomElement>(id) is CustomElement e && e.IsVisibleInTree() && e.ProcessMode != ProcessModeEnum.Disabled;
            Select(race);
            Check("without a replacement the city bridges stay shown in the race", Shown(cityBridges[0]) && Shown(cityBridges[1]) && Shown(raceId));
            //the field of the replacement: only in the panel of a race bridge, or of one created while a race is selected
            bool HasReplaceField() => ((System.Collections.Generic.Dictionary<string, CityElements.Types.Parameter>)typeof(EditorPanel)
                .GetField("parameterMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(builder.customElementEditorPanel)).Values.Any(p => p.name == "replaces");
            async Task<bool> PanelHasReplaceField(int id) {
                History.UndoManager.Record("edit bridge", () => builder.SelectObject(History.ElementRegistry.Get<CustomElement>(id), true, null));
                await Frames(3);
                return builder.customElementEditorPanel.IsOpen() && builder.customElementPlacer.GetElement() == History.ElementRegistry.Get<CustomElement>(id) && HasReplaceField();
            }
            Check("the panel of a race bridge can pick the bridge it replaces", await PanelHasReplaceField(raceId));
            Check("the panel of a city bridge can't", !await PanelHasReplaceField(cityId));
            Screenshot("bridge_city_panel.png");
            builder.CloseAllPanels();
            History.UndoManager.Record("start bridge", () => CallBuilder<object>("StartCustomElementCreation", "bridge", "CREATE_CUSTOM_ELEMENT"));
            await Frames(3);
            Check("a new bridge in the race can pick it", builder.customElementEditorPanel.IsOpen() && HasReplaceField());
            builder.CloseAllPanels();
            Select(null);
            History.UndoManager.Record("start bridge", () => CallBuilder<object>("StartCustomElementCreation", "bridge", "CREATE_CUSTOM_ELEMENT"));
            await Frames(3);
            Check("a new city bridge can't", builder.customElementEditorPanel.IsOpen() && !HasReplaceField());
            builder.CloseAllPanels();
            await Frames(2);
            Select(race);
            await Call("state.setParam", new { @ref = raceId, name = "replaces", value = cityId });
            await Call("city.flush"); await Frames(2);
            Check("the replaced city bridge is hidden while the race is selected", !Shown(cityId) && Shown(cityBridges[1]) && Shown(raceId));
            Select(null);
            Check("without the race the city bridge is shown again", Shown(cityId) && !Shown(raceId));
            Select(race);
            Check("selecting the race hides it again", !Shown(cityId));
            await Call("history.undo");
            await Call("city.flush"); await Frames(2);
            Check("undoing the replacement shows the city bridge", Shown(cityId) && History.ElementRegistry.Get<CustomElement>(raceId).state.State("replaces") == null);
            await Call("history.redo");
            await Call("city.flush"); await Frames(2);
            Check("redo hides it again", !Shown(cityId));
            await Call("state.setParam", new { @ref = raceId, name = "replaces", value = cityBridges[1] });
            await Call("city.flush"); await Frames(2);
            Check("replacing another bridge shows the first one", Shown(cityId) && !Shown(cityBridges[1]));
            await Call("state.setParam", new { @ref = raceId, name = "replaces", value = (int?)null });
            await Call("city.flush"); await Frames(2);
            Check("clearing the replacement shows the city bridge", Shown(cityId) && Shown(cityBridges[1]));
            await Call("state.setParam", new { @ref = raceId, name = "replaces", value = cityId });
            await Call("city.flush"); await Frames(2);
            await Call("customElement.delete", new { element = raceId });
            await Call("city.flush"); await Frames(2);
            Check("deleting the race bridge shows the city bridge", Shown(cityId));
            Select(null);
            foreach (var id in cityBridges) await Call("customElement.delete", new { element = id });
            History.UndoManager.Record("remove race", () => {
                History.UndoManager.Touch(city);
                city.state.SetProperty("circuitRaces", city.state.Array<ObjectState>("circuitRaces")[..^1]);
                M.FlagAsChanged();
            });
            await Call("city.flush");
        }
    }
}

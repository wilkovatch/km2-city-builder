using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;

namespace Api {
    public partial class ApiSelfTest {
        static float[] SplinePosition(Vector3 value) => new[] { value.X, value.Y, value.Z };
        async Task Decals() {
            var types = CityElements.Types.Parsers.TypeParser.GetCustomElementTypes();
            if (!types.ContainsKey("decal")) return;
            var schema = await Call("state.schema", new { @ref = 0 });
            Check("manual and automatic decals are no longer city properties", !schema["parameters"].Any(p =>
                p["name"].Value<string>() == "decals" || p["name"].Value<string>() == "cableCarRailDecals"));
            Check("decals have a direct menu entry and PSDL elements have their own group", types["decal"].typeData.settings.createMenu.direct &&
                types.Where(p => p.Key != "decal" && p.Key != "train" && p.Value.typeData.settings.creatable).All(p => p.Value.typeData.settings.createMenu?.group == "CREATE_PSDL_ELEMENT"));
            builder.ReloadCreateMenu();
            var menu = CallBuilder<MenuBar>("GetMenuBar").FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>()
                .First(b => b.Text == StringManager.Get("CREATE")).GetPopup();
            var direct = Enumerable.Range(0, menu.ItemCount).First(i => menu.GetItemText(i) == StringManager.Get("CREATE_DECAL"));
            Check("Create exposes a direct decal and a PSDL group", Enumerable.Range(0, menu.ItemCount).Any(i => menu.GetItemText(i) == StringManager.Get("CREATE_PSDL_ELEMENT")));
            menu.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(direct));
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
            var grouped = Enumerable.Range(0, menu.ItemCount).First(i => menu.GetItemText(i) == StringManager.Get("CREATE_PSDL_ELEMENT"));
            menu.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(grouped));
            var groupedTypes = (List<string>)typeof(EditorPanels.CustomElementEditorPanel).GetField("creatableTypes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(builder.customElementEditorPanel);
            Check("the PSDL creation panel excludes direct decal entries", groupedTypes.Count > 0 && !groupedTypes.Contains("decal"));
            builder.CloseAllPanels();
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
            Check("moving a spline point projects it back onto the road", Mathf.Abs(point.GetPoint().Y - GeometryHelper.ProjectOnSurface(point.GetPoint(), 136, element.generator).Y) < 0.001f);
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
        }

        async Task IntersectionDecalPlacement() {
            var junction = M.intersections.First(i => !i.IsDeleted() && !Operations.CustomElements.IsGhost(i) && i.roads.Count >= 3);
            var center = GO.GetPosition(junction.point);
            var placer = new ElementPlacer.CustomElementPlacer(M) {
                createType = "decal", placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.Create
            };
            await ToSignal(GetTree(), "physics_frame"); await Frames(2);
            var expected = new List<Vector3>();
            foreach (var offset in new[] { new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1) }) {
                var origin = center + offset + Vector3.Up * 100;
                var found = placer.Raycast(builder.helper, origin, Vector3.Down, out var hit, 200, placer.GetLayerMasks()[2]);
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
                id = (await Call("customElement.create", new { type = key, state = new { width = 3, drawStrip = true, texture = "texture/decal_zigzag_l.tex" },
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

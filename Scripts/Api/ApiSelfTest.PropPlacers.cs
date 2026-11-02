using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;
using CT = CityElements.Types;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    public partial class ApiSelfTest {
        async Task PropPlacers() {
            const string mesh = "prop_placer_selection.obj";
            SelectionObj(System.IO.Path.Combine(folder, mesh), new[] {
                new Vector3(1, -2, 0.2f), new Vector3(2, -2, 0.2f), new Vector3(2, -2, 0.8f), new Vector3(1, -2, 0.8f)
            }, new[] { new[] { 0, 1, 2, 0, 2, 3 } });
            object P(float x, float z, float y = 0) => new { position = V(x, y, z) };
            CustomElement Element(int id) => History.UndoManager.GetAliveElement(id) as CustomElement;
            var line = (await Call("customElement.create", new { type = "propLine", name = "Native prop line", points = new { spline = new[] { P(-710, -700), P(-710, -730) } },
                state = new { propRule = new { main = new { type = "line", element = new { type = "line", mesh, elemDistance = 5 } } } } })).Value<int>();
            Check("prop placers use the custom element API kind", (await Call("elements.get", new { @ref = line }))["kind"].Value<string>() == "customElement");
            Check("native line profile previews six props on a thirty metre span", Element(line).generator.props.Count == 6);
            await CheckUndo("moving a prop placer", () => Call("customElement.move", new { element = line, offset = V(3, 0, 0) }));
            await CheckUndo("inserting a prop placer control", () => Call("customElement.insert", new { element = line, array = "spline", index = 1 }));
            await Call("customElement.remove", new { element = line, array = "spline", index = 1 });
            await Expect("a restricted spline strip rejects another container type", "invalid_value", "customElement.setPropRule", new { element = line, rule = new { main = new { type = "custom" } } });
            await Expect("native line rejects unencodable spacing", "invalid_value", "customElement.setPropRule", new { element = line,
                rule = new { main = new { type = "line", element = new { type = "line", mesh, elemDistance = 64 } } } });
            await Expect("native line enforces quarter metre steps", "invalid_value", "customElement.setPropRule", new { element = line,
                rule = new { main = new { type = "line", element = new { type = "line", mesh, elemDistance = 0.3 } } } });
            await Call("state.setParam", new { @ref = line, name = "segments", value = 8 });
            foreach (var mode in new[] { 0, 1 }) {
                await Call("state.setParam", new { @ref = line, name = "curveType", value = mode });
                Check("prop placers evaluate curve mode " + mode + " with the requested segments", Element(line).splineData.sections.Length == 9);
            }
            await Call("state.setParam", new { @ref = line, name = "curveType", value = 2 });
            var longLine = (await Call("customElement.create", new { type = "propLine", points = new { spline = new[] { P(-900,-700), P(-900,-1000) } },
                state = new { projectToGround = false, propRule = new { main = new { type = "line", element = new { type = "line", mesh, elemDistance = 0.25 } } } } })).Value<int>();
            Check("native line previews more than the old road iteration cap", Element(longLine).generator.props.Count == 1200);
            await Call("customElement.delete", new { element = longLine });
            foreach (var prop in Element(line).generator.props.ToArray()) {
                Check("every generated prop resolves to its owner", ElementPlacer.ReferencePicker.ElementOf(prop.obj) == Element(line));
                Check("every generated prop has the shared asset picking shape", prop.obj.GetChildren().OfType<CollisionShape3D>()
                    .Any(c => c.Shape == MeshManager.GetSelectionShape(mesh)));
                builder.CloseAllPanels(); builder.SetSelectObjectMode();
                var selector = (TerrainModifier.Null)builder.terrainClick.modifier;
                await Frames(2);
                var position = GameEngine.Vector3.EncodePosition(prop.obj.GlobalTransform * GameEngine.Vector3.DecodePosition(new Vector3(1.5f,0,0.5f)));
                var hitProp = GameEngine.Raycast.DoRaycast(builder.helper, position + Vector3.Up * 20, Vector3.Down, out var hit, 40, selector.GetLayerMasks()[0]);
                Check("each generated instance can be picked by its mesh", hitProp && hit.collider == prop.obj);
                if (hitProp) History.UndoManager.Record("select generated prop", () => selector.Select(hit));
                await Frames(2);
                Check("picking any generated instance opens the owner editor", builder.customElementEditorPanel.IsOpen() && builder.customElementEditorPanel.GetElement() == Element(line));
            }
            builder.CloseAllPanels();
            var polygon = (await Call("customElement.create", new { type = "propPolygon", name = "Seeded polygon", points = new { perimeter = new[] {
                P(-680,-700,50), P(-660,-700,50), P(-660,-690,50), P(-670,-690,50), P(-670,-680,50), P(-680,-680,50) } }, state = new { seed = 42,
                projectToGround = false, models = new[] { new { mesh, density = 4, minYaw = 15, maxYaw = 25 } } } })).Value<int>();
            Vector3[] Positions() => Element(polygon).generator.props.Select(p => GO.GetPosition(p.obj)).ToArray();
            var positions = Positions();
            Check("concave polygon density uses its three hundred square metre area", positions.Length == 12);
            Check("concave polygon never fills the missing corner", positions.All(p => p.X <= -670 || p.Z <= -690));
            Check("projection off retains the polygon surface height", positions.All(p => Math.Abs(p.Y - 50) < 0.001f));
            Check("per model yaw stays in its configured range", Element(polygon).generator.props.All(p => GO.GetRotation(p.obj).Y >= 14.99 && GO.GetRotation(p.obj).Y <= 25.01));
            Element(polygon).state.FlagAsChanged();
            await Call("city.flush");
            Check("unrelated rebuild does not reroll the seeded distribution", positions.SequenceEqual(Positions()));
            await Call("state.setParam", new { @ref = polygon, name = "projectToGround", value = true });
            Check("projection updates every generated prop height", Positions().All(p => Math.Abs(p.Y) < 0.01f));
            Check("projection preserves seeded horizontal choices", positions.Zip(Positions(), (a,b) => Math.Abs(a.X-b.X) < 0.001 && Math.Abs(a.Z-b.Z) < 0.001).All(x => x));
            const string surfaceMesh = "prop_placer_surface.obj";
            SelectionObj(Path.Combine(folder, surfaceMesh), new[] { new Vector3(-15,0,-15), new Vector3(15,0,-15), new Vector3(15,0,15), new Vector3(-15,0,15) }, new[] { new[] { 0,1,2,0,2,3 } });
            var surface = (await Call("meshes.create", new { mesh = surfaceMesh, type = "instMesh", position = V(-670,6,-690) })).Value<int>();
            Check("adding a surface reprojects an existing polygon", Positions().All(p => Math.Abs(p.Y - 6) < 0.01));
            await Call("meshes.setTransform", new { mesh = surface, position = V(-670,9,-690) });
            Check("moving a surface reprojects generated props", Positions().All(p => Math.Abs(p.Y - 9) < 0.01));
            await Call("history.undo");
            Check("undoing a surface move settles prop projection", Positions().All(p => Math.Abs(p.Y - 6) < 0.01));
            await Call("meshes.delete", new { mesh = surface });
            Check("deleting a surface restores terrain projection", Positions().All(p => Math.Abs(p.Y) < 0.01));
            await CheckUndo("changing polygon seed", () => Call("state.setParam", new { @ref = polygon, name = "seed", value = 43 }));
            await Call("state.import", new { @ref = polygon, values = new { models = new[] { new { mesh, density = 4, minYaw = 15, maxYaw = 25 }, new { mesh, density = 2, minYaw = 0, maxYaw = 0 } } } });
            Check("polygon model densities contribute independent counts", Positions().Length == 18);
            builder.SelectObject(Element(polygon), true, null);
            var modelPanel = builder.customElementEditorPanel;
            modelPanel.RefreshValues();
            const BindingFlags listFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var complex = (System.Collections.IDictionary)typeof(EditorPanel).GetField("complexElements", listFlags).GetValue(modelPanel);
            var ioList = complex.Values.Cast<object>().Single(c => c.GetType().Name == "IOList");
            var lists = (System.Collections.IDictionary)ioList.GetType().GetField("listControls", listFlags).GetValue(ioList);
            var modelsList = (EditorPanelElements.ScrollList)lists["models"].GetType().GetField("list", listFlags).GetValue(lists["models"]);
            var modelNames = (List<string>)typeof(EditorPanelElements.ScrollList).GetField("strings", listFlags).GetValue(modelsList);
            Check("polygon list labels show model names through the same array-label mechanism as traffic cars", modelNames.SequenceEqual(new[] { mesh, mesh }));
            builder.CloseAllPanels();
            await PropPlacerDensity(mesh);
            await Expect("polygon presets reject negative density", "invalid_value", "presets.save", new { category = "customElement/propPolygon", name = "Invalid density", values = new {
                type = "propPolygon", models = new[] { new { mesh, density = -1 } } } });
            var selfCrossing = PropPlacerGeometry.Triangulate(new[] { new Vector3(0,0,0), new Vector3(10,0,10), new Vector3(0,0,10), new Vector3(10,0,0) });
            Check("self intersecting polygons are rejected", selfCrossing.Length == 0);
            Check("degenerate polygons are rejected", PropPlacerGeometry.Triangulate(new[] { Vector3.Zero, Vector3.Right, Vector3.Right * 2 }).Length == 0);
            var invalid = (await Call("customElement.create", new { type = "propPolygon", points = new { perimeter = new[] { P(-550,-700),P(-540,-690),P(-550,-690),P(-540,-700) } } })).Value<int>();
            Check("invalid polygons remain editable with a validation error", Element(invalid).propPlacementError == "PP_INVALID_POLYGON" && Element(invalid).GetAllPoints().Count == 4 && Element(invalid).generator.props.Count == 0);
            await Call("customElement.delete", new { element = invalid });

            var strip = (await Call("customElement.create", new { type = "propStrip", name = "Editable prop strip", points = new { spline = new[] { P(-620,-700), P(-620,-730) } },
                state = new { width = 4, propRule = new { main = new { type = "custom", elements = new[] { new { type = "custom", meshes = new[] { mesh },
                    start = 0, end = 0, elemDistance = 5, maxNumber = 20, minHorizPos = 0, maxHorizPos = 1, rotationAngle = 0, random = false } } } } } })).Value<int>();
            Check("prop strips reuse road placement containers", Element(strip).generator.props.Count == 7);
            Check("strip props stay inside their editable width", Element(strip).generator.props.All(p => Math.Abs(GO.GetPosition(p.obj).X + 620) <= 2.001));
            await CheckUndo("changing strip width", () => Call("state.setParam", new { @ref = strip, name = "width", value = 8 }));
            Check("changing strip width regenerates its band", Element(strip).generator.props.All(p => Math.Abs(GO.GetPosition(p.obj).X + 620) <= 4.001));
            await PropPlacerCoreExtensions(mesh);
            await PropPlacerConnections();
            await ParkedCarRows();

            builder.ReloadCreateMenu();
            var createMenu = CallBuilder<MenuBar>("GetMenuBar").FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>()
                .First(b => b.Text == StringManager.Get("CREATE")).GetPopup();
            var menuIndex = Enumerable.Range(0, createMenu.ItemCount).FirstOrDefault(i => createMenu.GetItemText(i) == StringManager.Get("PROPS_MENU"), -1);
            var propsMenu = menuIndex < 0 ? null : createMenu.GetItemSubmenuNode(menuIndex);
            Check("Props submenu contains the individual prop and three placers", propsMenu?.ItemCount == 4);

            //The new menu opens the full property draft without creating a city element.
            foreach (var type in new[] { "propLine", "propStrip", "propPolygon" }) {
                var count = M.customElements.Count;
                History.UndoManager.Record("start prop placer", () => CallBuilder<object>("StartCustomElementCreation", type, "CREATE_CUSTOM_ELEMENT"));
                await Frames(2);
                var field = typeof(EditorPanels.CustomElementEditorPanel).GetField("draft", BindingFlags.NonPublic | BindingFlags.Instance);
                var draft = field.GetValue(builder.customElementEditorPanel) as ObjectState;
                Check(type + " exposes its full draft before the first click", draft?.Str("type") == type && M.customElements.Count == count);
                History.UndoManager.Record("first prop placer click", () => builder.customElementPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-600,0,-600) }));
                var placed = builder.customElementPlacer.GetElement();
                Check(type + " starts with one control and continues placement", placed.GetAllPoints().Count == 1
                    && builder.customElementPlacer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint);
                if (type == "propPolygon") {
                    var placer = builder.customElementPlacer;
                    Check("polygon placement can hit its editable perimeter controls", (placer.GetLayerMasks()[0] & (1 << 8)) != 0);
                    History.UndoManager.Record("second polygon click", () => placer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-590,0,-600) }));
                    History.UndoManager.Record("third polygon click", () => placer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-590,0,-590) }));
                    var first = placed.GetAllPoints()[0];
                    History.UndoManager.Record("close polygon", () => placer.PlaceAt(new GameEngine.Raycast.RaycastHit { collider = first.GetObject(), point = first.GetPoint() }));
                    Check("clicking the first polygon control finishes its perimeter", placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None && placed.GetAllPoints().Count == 3);
                }
                builder.CloseAllPanels();
                await Call("customElement.delete", new { element = History.ElementRegistry.GetId(placed) });
            }
            await PropPlacerPanelLifecycle(line, polygon);
            Check("polygon density label includes the square metre symbol", StringManager.Get("PP_DENSITY").Contains("m\u00b2"));
            foreach (var id in new[] { line, polygon, strip }) {
                var element = Element(id);
                var geometry = PropPlacerGeometry.Build(element);
                Check(element.TypeName + " guides are lifted five centimetres", geometry.lines.Count > 0 && geometry.lines.All(l => Math.Abs(l.heightOffset - 0.05f) < 0.00001f));
                Check(element.TypeName + " guide lift leaves authored and generated heights unchanged", geometry.lines.All(l => l.points.All(p => Math.Abs(p.Y) < 0.01))
                    && element.generator.props.All(p => Math.Abs(GO.GetPosition(p.obj).Y) < 0.01));
            }
            await PropPlacerPersistence();
            //Reload changes ids, so reacquire the surviving owners by name.
            line = History.ElementRegistry.GetId(M.customElements.Single(x => GO.GetName(x) == "Native prop line"));
            polygon = History.ElementRegistry.GetId(M.customElements.Single(x => GO.GetName(x) == "Seeded polygon"));
            strip = History.ElementRegistry.GetId(M.customElements.Single(x => GO.GetName(x) == "Editable prop strip"));
            await Call("customElement.delete", new { element = line });
            await Call("customElement.delete", new { element = polygon });
            await Call("customElement.delete", new { element = strip });
        }

        async Task PropPlacerDensity(string mesh) {
            foreach (var (width, depth, density, count) in new[] {
                (10f, 10f, 1f, 1), (20f, 10f, 1f, 2), (100f, 100f, 1f, 100), (100f, 100f, 0.1f, 10), (100f, 100f, 0f, 0)
            }) {
                object P(float x, float z) => new { position = V(-1100 + x, 0, -1100 + z) };
                var id = (await Call("customElement.create", new { type = "propPolygon", points = new { perimeter = new[] {
                    P(0,0), P(width,0), P(width,depth), P(0,depth) } }, state = new { projectToGround = false,
                        models = new[] { new { mesh, density } } } })).Value<int>();
                var polygon = (CustomElement)History.UndoManager.GetAliveElement(id);
                Check(width + " by " + depth + " metre polygon at density " + density + " produces " + count + " props", polygon.generator.props.Count == count);
                await Call("customElement.delete", new { element = id });
            }
        }

        async Task PropPlacerPanelLifecycle(int line, int polygon) {
            var panel = builder.customElementEditorPanel;
            var placer = builder.customElementPlacer;
            foreach (var id in new[] { line, polygon }) {
                var element = (CustomElement)History.UndoManager.GetAliveElement(id);
                builder.SelectObject(element, true, null);
                panel.SetPointPlacementMode(1);
                var count = element.GetAllPoints().Count;
                History.UndoManager.Record("open child editor", () => panel.Hide(true));
                Check(element.TypeName + " suspends placement while a child editor is open", placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None);
                History.UndoManager.Record("click outside hidden owner", () => placer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-300,0,-300) }));
                Check(element.TypeName + " does not add controls while hidden", element.GetAllPoints().Count == count);
                panel.Hide(false);
                Check(element.TypeName + " resumes its placement only on return to the owner", placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint);
                builder.SetSelectObjectMode();
                panel.SetActive(false);
                Check(element.TypeName + " clears placement even after the click modifier changes", placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None && !element.IsActive());
                await Frames(2);
            }
            var owner = (CustomElement)History.UndoManager.GetAliveElement(line);
            builder.SelectObject(owner, true, null);
            var child = panel.OpenPropContainer("main");
            Check("prop line opens its actual container child panel", child != null && child.ActiveSelf() && !panel.ActiveSelf());
            //Back may refresh the parent's presets after a picker or closing action changed the click modifier.
            builder.SetSelectObjectMode();
            History.UndoManager.Record("return from prop line child without an owner", () => typeof(EditorPanel)
                .GetMethod("GoUp", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(child, null));
            panel.RefreshValues();
            Check("returning from a prop line child tolerates an absent preset state", panel.ActiveSelf() && placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None);
            builder.CloseAllPanels();
            builder.SelectObject(owner, true, null);
            Check("prop line can still be edited after the child panel closes", panel.GetElement() == owner && panel.ActiveSelf());
            builder.CloseAllPanels();
        }

        async Task PropPlacerCoreExtensions(string mesh) {
            const string custom = "zzPropPlacerPolygon", spline = "zzPropPlacerSpline";
            var scripts = Path.Combine(folder, "placer_lua");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "placement.lua"), "return { fill = function(v,c) return {{model=1, position=c.perimeter[1]+vec3(c.random()*v.spread,c.models[1].height,0), rotation=vec3(0,c.models[1].yaw,0), scale=vec3(2,1,1)}} end }");
            using var host = RuntimeCalculator.Scripting.LuaScriptHost.CreateForFolder(scripts);
            var data = new CT.CustomElementType { parameters = new CT.ParameterContainer { parameters = new[] {
                new CT.Parameter { name = "spread", type = "float" } } }, ui = new CT.Panel { label = "PP_MODELS" }, settings = new CT.CustomElementSettings {
                propPlacer = new CT.PropPlacerSettings { type = "polygon", maxModels = 1, density = false, projectToGround = false,
                    placement = "placement.fill", modelParameters = new[] { new CT.Parameter { name = "height", type = "float" }, new CT.Parameter { name = "yaw", type = "float" } } } } };
            data.settings.propPlacer.Initialize(data);
            TP.GetCustomElementTypes().Add(custom, new CT.Runtime.CustomElementType(data, custom, host));
            var strips = new CT.CustomElementType { parameters = new CT.ParameterContainer { parameters = Array.Empty<CT.Parameter>() },
                ui = new CT.Panel { label = "PP_MODELS" }, settings = new CT.CustomElementSettings {
                    spline = new CT.CustomElementSplineSettings(), propPlacer = new CT.PropPlacerSettings { type = "spline", projectToGround = false, strips = new[] {
                        new CT.PropPlacerStrip { name = "a", start = "-2", end = "-2", allowedContainerTypes = new[] { "line" } },
                        new CT.PropPlacerStrip { name = "b", start = "2", end = "2", allowedContainerTypes = new[] { "custom" } } } } } };
            strips.settings.propPlacer.Initialize(strips);
            strips.settings.controlPoints = CT.CustomElementType.SplinePoints();
            TP.GetCustomElementTypes().Add(spline, new CT.Runtime.CustomElementType(strips, spline));
            var ids = new List<int>();
            try {
                object Point(float x, float z) => new { position = V(x, 0, z) };
                var points = new { perimeter = new[] { Point(-520,-700), Point(-500,-700), Point(-500,-680), Point(-520,-680) } };
                var id = (await Call("customElement.create", new { type = custom, points, state = new { seed = 8, spread = 10,
                    models = new[] { new { mesh, height = 7, yaw = 25 } } } })).Value<int>(); ids.Add(id);
                CustomElement Element() => (CustomElement)History.UndoManager.GetAliveElement(id);
                var prop = Element().generator.props.Single().obj;
                Check("custom polygon logic receives global and per-model parameters", Math.Abs(GO.GetPosition(prop).Y - 7) < 0.001 && Math.Abs(GO.GetRotation(prop).Y - 25) < 0.001);
                Check("custom polygon logic returns instance scale", GO.GetLocalScale(prop) == new Vector3(2,1,1));
                var position = GO.GetPosition(prop);
                Element().state.FlagAsChanged(); await Call("city.flush");
                Check("custom placement receives repeatable local randomness", GO.GetPosition(Element().generator.props.Single().obj) == position);
                await Expect("core model cap is enforced by the API", "invalid_value", "customElement.create", new { type = custom, points,
                    state = new { models = new[] { new { mesh }, new { mesh } } } });
                var settings = TP.GetCustomElementTypes()["propLine"].typeData.settings;
                var invalidPreset = Operations.CustomElementPresets.DefaultPreset("propLine");
                invalidPreset.State("propRule").State("main").SetStr("type", "custom");
                await Expect("placer presets enforce container restrictions", "invalid_value", "presets.save", new { category = "customElement/propLine", name = "Invalid placer preset", values = invalidPreset });
                var multi = (await Call("customElement.create", new { type = spline, points = new { spline = new[] { Point(-480,-700), Point(-480,-720) } }, state = new { propRule = new {
                    a = new { type = "line", element = new { type = "line", mesh, elemDistance = 5, maxNumber = 10, xPos = 0.5f } },
                    b = new { type = "custom", elements = new[] { new { type = "custom", meshes = new[] { mesh }, elemDistance = 5, maxNumber = 10, minHorizPos = 0.5, maxHorizPos = 0.5 } } } } } })).Value<int>(); ids.Add(multi);
                var generated = ((CustomElement)History.UndoManager.GetAliveElement(multi)).generator.props;
                Check("a spline supports independently configured named strips", generated.Count > 2 && generated.Select(p => p.container).Distinct().Count() == 2
                    && generated.Where(p => p.container == "a").All(p => Math.Abs(GO.GetPosition(p.obj).X + 482) < 0.01)
                    && generated.Where(p => p.container == "b").All(p => Math.Abs(GO.GetPosition(p.obj).X + 478) < 0.01));
            } finally {
                foreach (var id in ids) if (History.UndoManager.GetAliveElement(id) is CustomElement) await Call("customElement.delete", new { element = id });
                TP.GetCustomElementTypes().Remove(custom); TP.GetCustomElementTypes().Remove(spline);
            }
        }

        async Task PropPlacerConnections() {
            var roadId = (await Call("roads.create", new { points = new[] { new { position = V(-400,0,-700) }, new { position = V(-400,0,-650) }, new { position = V(-400,0,-600) } } })).Value<int>();
            var road = (Road)History.UndoManager.GetAliveElement(roadId);
            var anchors = road.anchorManager.GetTerrainAnchors();
            var from = anchors.FirstOrDefault(a => a.next?.next != null && a.next.next != a && !a.customElementsOnly);
            if (from == null) { Check("road exposes a connected anchor chain", false); await Call("roads.delete", new { road = roadId }); return; }
            var to = from.next.next;
            var id = (await Call("customElement.create", new { type = "propPolygon", name = "Connected placer" })).Value<int>();
            CustomElement Element() => (CustomElement)History.UndoManager.GetAliveElement(id);
            History.UndoManager.Record("start connected perimeter", () => Operations.CustomElements.AppendPoint(M, Element(), "perimeter", from.GetObject(), from.GetPoint()));
            await Call("city.flush");
            var count = Element().GetAllPoints().Count;
            History.UndoManager.Record("follow connected anchors", () => Operations.CustomElements.AppendPoint(M, Element(), "perimeter", to.GetObject(), to.GetPoint(), true));
            await Call("city.flush");
            var after = Element().GetAllPoints().Count;
            Check("Ctrl-click traverses intermediate road anchors", after >= count + 2);
            await Call("history.undo");
            Check("one undo removes the complete connection traversal", Element().GetAllPoints().Count == count);
            await Call("history.redo");
            Check("redo restores the complete connection traversal", Element().GetAllPoints().Count == after);
            await Call("customElement.delete", new { element = id });
            var spline = (await Call("customElement.create", new { type = "propStrip" })).Value<int>();
            CustomElement Spline() => (CustomElement)History.UndoManager.GetAliveElement(spline);
            var link = PointLink.Create(from.GetObject(), from.next.GetObject(), 1);
            var midpoint = (from.GetPoint() + from.next.GetPoint()) / 2;
            History.UndoManager.Record("start on anchor line", () => Operations.CustomElements.AppendPoint(M, Spline(), "spline", link, midpoint));
            History.UndoManager.Record("follow from anchor line", () => Operations.CustomElements.AppendPoint(M, Spline(), "spline", to.GetObject(), to.GetPoint(), true));
            await Call("city.flush");
            Check("spline Ctrl-click follows connections from intermediate line anchors", Spline().GetAllPoints().Count == 3 && Spline().GetAllPoints()[0].anchor is LineAnchor);
            await Call("roads.movePoint", new { road = roadId, index = 1, position = V(-390,0,-650) });
            var first = Spline().GetAllPoints()[0];
            Check("line attachments follow moving road controls", first.anchor is LineAnchor attached && first.GetPoint().DistanceTo(attached.GetPoint()) < 0.01);
            await Call("customElement.delete", new { element = spline });
            await Call("roads.delete", new { road = roadId });

            TerrainPatch patch = null;
            History.UndoManager.Record("source perimeter", () => {
                patch = Operations.TerrainPatches.Create(M);
                foreach (var point in new[] { new Vector3(-380,0,-600), new Vector3(-370,0,-600), new Vector3(-370,0,-590), new Vector3(-380,0,-590) })
                    Operations.TerrainPatches.AddPerimeterPoint(patch, null, point, true);
            });
            await Call("city.flush");
            var perimeter = patch.GetPerimeterPointsComponents();
            var polygon = (await Call("customElement.create", new { type = "propPolygon" })).Value<int>();
            CustomElement Polygon() => (CustomElement)History.UndoManager.GetAliveElement(polygon);
            History.UndoManager.Record("share first perimeter point", () => Operations.CustomElements.AppendPoint(M, Polygon(), "perimeter", perimeter[0].GetObject(), perimeter[0].GetPoint()));
            History.UndoManager.Record("follow shared perimeter", () => Operations.CustomElements.AppendPoint(M, Polygon(), "perimeter", perimeter[2].GetObject(), perimeter[2].GetPoint(), true));
            await Call("city.flush");
            Check("Ctrl-click follows terrain perimeter connections and shares points", Polygon().GetAllPoints().Count == 3 && Polygon().HasPoint(perimeter[0]) && Polygon().HasPoint(perimeter[2]));
            History.UndoManager.Record("delete source perimeter", () => { patch.Delete(); M.FlagAsChanged(); });
            await Call("city.flush");
            Check("deleting a source preserves points shared by a prop placer", Polygon().GetAllPoints().All(p => !p.IsDeleted()));
            await Call("customElement.delete", new { element = polygon });
        }

        //fixedMeshes and exportSlots, through the parked cars of the MM2 core
        async Task ParkedCarRows() {
            var element = TP.GetPropsElementTypes()["parkedCars"].typeData;
            var roadId = (await Call("roads.create", new { points = new[] { new { position = V(-300,0,-700) }, new { position = V(-300,0,-600) } } })).Value<int>();
            var road = (Road)History.UndoManager.GetAliveElement(roadId);
            await Call("state.import", new { @ref = roadId, values = new { forwardPark = true, backwardPark = true, forwardParkWidth = 2, backwardParkWidth = 2 } });
            foreach (var row in new[] { "rightParking", "leftParking" }) {
                var rowRef = new { road = roadId, container = row };
                var elementRef = new { road = roadId, container = row, list = "element" };
                await Call("roads.props.enable", new { road = roadId, container = row, enabled = true });
                await Call("state.setType", new { @ref = rowRef, type = "parkedCars" });
                await Call("props.elements.add", new { container = rowRef, list = "element" });
                await Call("state.setType", new { @ref = elementRef, type = "parkedCars" });
                await Call("state.setParam", new { @ref = elementRef, name = "elemDistance", value = 6 });
                await Call("state.setParam", new { @ref = elementRef, name = "xPos", value = 0.5f });
            }
            await Call("city.flush");
            var slots = road.generator.propSlots;
            var right = slots.Where(s => s.container == "rightParking").ToArray();
            var left = slots.Where(s => s.container == "leftParking").ToArray();
            var placed = road.generator.props.ToArray();
            Check("a road row lists every slot of its length", right.Length == 17 && left.Length == 17);
            Check("the example preview leaves some slots empty", placed.Length > 0 && placed.Length < slots.Count);
            Check("the props of a type with fixed meshes use only those meshes", placed.All(p => element.fixedMeshes.Contains(p.meshName)));
            const string hookModel = "geometry/giz_pcar03_l.pkg";
            Check("the example preview follows the stock game by default", placed.All(p => p.meshName != hookModel) && element.fixedMeshes.Where(m => m != hookModel).All(m => placed.Any(p => p.meshName == m)));
            Check("the cars of road rows follow the traffic of their side", right.All(s => s.forward.Z > 0.99f) && left.All(s => s.forward.Z < -0.99f));
            Check("each prop is at one of the slots", placed.All(p => slots.Any(s => s.container == p.container && GO.GetPosition(p.obj).DistanceTo(s.position) < 0.001f)));
            var example = placed.Select(p => (p.meshName, GO.GetPosition(p.obj))).ToArray();
            road.GetState().FlagAsChanged();
            await Call("city.flush");
            Check("the example preview doesn't change at a rebuild", example.SequenceEqual(road.generator.props.Select(p => (p.meshName, GO.GetPosition(p.obj)))));
            await Expect("the meshes of a fixed type can't be changed", "invalid_operation", "props.element.meshes.add", new { element = new { road = roadId, container = "rightParking", list = "element" }, mesh = "" });

            var strip = (await Call("customElement.create", new { type = "propStrip", points = new { spline = new[] { new { position = V(-280,0,-700) }, new { position = V(-280,0,-640) } } },
                state = new { width = 4, projectToGround = false, propRule = new { main = new { type = "parkedCars", element = new { type = "parkedCars", elemDistance = 6, xPos = 0.5f, reverse = true } } } } })).Value<int>();
            CustomElement Strip() => History.UndoManager.GetAliveElement(strip) as CustomElement;
            Check("a prop strip accepts the parked cars", Strip().generator.propSlots.Count == 11);
            Check("reverse direction follows the spline of a prop strip", Strip().generator.propSlots.All(s => s.forward.Z > 0.99f));
            var meshes = element.fixedMeshes;
            try {
                element.fixedMeshes = new[] { "geometry/zz_missing_model.pkg" };
                Strip().state.FlagAsChanged();
                await Call("city.flush");
                Check("missing models still give the slots", Strip().generator.propSlots.Count == 11);
            } finally {
                element.fixedMeshes = meshes;
                Strip().state.FlagAsChanged();
                await Call("city.flush");
            }
            Check("a prop strip follows the stock game by default", Strip().generator.props.All(p => p.meshName != hookModel));
            await Call("state.setParam", new { @ref = 0, name = "assumeMM2Hook", value = true });
            await Call("city.flush");
            Check("the rules of the props of a road read the injected city properties", element.fixedMeshes.All(m => road.generator.props.Any(p => p.meshName == m)));
            Check("the rules of the props of a custom element read the injected city properties", Strip().generator.props.Any(p => p.meshName == hookModel));
            Check("an injected city property doesn't change the slots of a row", road.generator.propSlots.Count == 34 && Strip().generator.propSlots.Count == 11);
            var hookJson = Path.Combine(folder, "assume_hook_export.json");
            await Call("city.exportJson", new { path = hookJson });
            Check("the exporters can read the city property", JObject.Parse(File.ReadAllText(hookJson))["cityProperties"]["assumeMM2Hook"].Value<bool>());
            await Call("state.setParam", new { @ref = 0, name = "assumeMM2Hook", value = false });
            await Call("city.flush");
            Check("turning the city property off restores the example preview", example.SequenceEqual(road.generator.props.Select(p => (p.meshName, GO.GetPosition(p.obj)))));

            var jsonPath = Path.Combine(folder, "parked_cars_export.json");
            await Call("city.exportJson", new { path = jsonPath });
            var json = JObject.Parse(File.ReadAllText(jsonPath));
            var roadLines = json["roads"].First(r => r["data"]["name"].Value<string>() == GO.GetName(road))["data"]["propLines"];
            Check("the export lists the slots of the rows", roadLines.All(l => l["b64v3a_slots"].Value<string>() != null && l["b64v3a_slotForwards"].Value<string>() != null));
            Check("the export has no slots for the other types", json["customElements"].SelectMany(e => e["data"]["propLines"] ?? new JArray())
                .Where(l => l["name"].Value<string>() != "main" || l["b64v3a_slots"].Type == JTokenType.Null).All(l => l["b64v3a_slots"].Type == JTokenType.Null));
            var export = await Call("city.export", new { path = Path.Combine(folder, "..", "api_parked_cars"), exporter = "psdl", flags = new Dictionary<string,bool> {
                ["propRules"] = false, ["psdl"] = false, ["inst"] = false, ["bai"] = false, ["pathset"] = true, ["cinfo_aimap"] = true, ["pkgs"] = false, ["hudmap"] = false } });
            var reported = export["path"].Value<string>();
            var city = Path.GetFileNameWithoutExtension(reported);
            var file = Path.Combine(Path.GetDirectoryName(reported), "race", city, city + "_parkedcar.pathset");
            Check("an export writes the parked cars of the game", File.Exists(file));
            if (File.Exists(file)) using (var reader = new BinaryReader(File.OpenRead(file))) {
                reader.ReadUInt32();
                Check("each row is one path of the parked cars file", reader.ReadUInt32() == 3);
            }
            await Call("customElement.delete", new { element = strip });
            await Call("roads.delete", new { road = roadId });

            var state = new ObjectState();
            state.SetStr("type", "parkedCars");
            var panel = new EditorPanels.Props.ElemEditorPanel();
            panel.Initialize(builder);
            panel.SetEditedElem(state);
            panel.SetActive(true);
            try {
                var complex = (System.Collections.IDictionary)typeof(EditorPanel).GetField("complexElements", PreviewFlags).GetValue(panel);
                var io = complex.Values.Cast<object>().Single(c => c.GetType().Name == "IOList");
                Check("the panel of a type with fixed meshes has no mesh list", !((System.Collections.IDictionary)io.GetType().GetField("listControls", PreviewFlags).GetValue(io)).Contains("propMeshes"));
            } finally { panel.Destroy(); }
        }

        async Task PropPlacerPersistence() {
            var before = M.customElements.Where(e => e.Settings?.propPlacer != null).ToDictionary(e => GO.GetName(e), e => e.generator.props.Select(p => GO.GetPosition(p.obj)).ToArray());
            await Call("city.save"); await Call("city.reload");
            Check("placer instances regenerate identically after save/reload", M.customElements.Where(e => e.Settings?.propPlacer != null)
                .All(e => before.TryGetValue(GO.GetName(e), out var positions) && positions.SequenceEqual(e.generator.props.Select(p => GO.GetPosition(p.obj)))));
            await Call("city.exportJson", new { path = Path.Combine(folder, "prop_placer_export.json") });
            var export = await Call("city.export", new { path = Path.Combine(folder, "..", "api_prop_placers"), exporter = "psdl", flags = new Dictionary<string,bool> {
                ["propRules"] = false, ["psdl"] = false, ["inst"] = false, ["bai"] = false, ["pathset"] = true, ["cinfo_aimap"] = false, ["pkgs"] = false, ["hudmap"] = false } });
            var reported = export["path"].Value<string>();
            var pathset = Path.Combine(Path.GetDirectoryName(reported), "city", Path.GetFileNameWithoutExtension(reported), "props.pathset");
            Check("normal MM2 export writes the placer PATHSET", File.Exists(pathset));
            using (var reader = new BinaryReader(File.OpenRead(pathset))) {
                var magic = new string(reader.ReadChars(4)); var paths = reader.ReadUInt32(); reader.ReadUInt32();
                int native = 0, individuals = 0;
                for (int i = 0; i < paths; i++) {
                    reader.ReadBytes(32); var count = reader.ReadUInt32(); reader.ReadUInt32();
                    reader.ReadBytes(checked((int)count * 16));
                    var type = reader.ReadByte(); var spacing = reader.ReadByte(); reader.ReadUInt16();
                    if (type == 2) { native++; Check("normal export preserves native line spacing", spacing == 20); }
                    else individuals += (int)count / (type == 1 ? 2 : 1);
                }
                var expected = M.customElements.Where(e => e.TypeName != "propLine").Sum(e => e.generator.props.Count);
                Check("normal export emits one native line and every individual placer prop", magic == "PTH1" && native == 1 && individuals == expected);
            }
            JObject ReadCity(string path) { using var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress); using var reader = new StreamReader(stream); return JObject.Parse(reader.ReadToEnd()); }
            void WriteCity(string path, JObject city) { using var stream = new GZipStream(File.Create(path), CompressionMode.Compress); using var writer = new StreamWriter(stream); writer.Write(city.ToString()); }
            var cityPath = Path.Combine(folder,"city.json.gz");
            var preferencesPath = Path.Combine(folder,"preferences.json");
            var city = ReadCity(cityPath);
            var legacy = JObject.Parse("{id:9001,name:'obsolete experimental rule',state:{type:'__propPath'},points:{},containerId:-1}");
            city["propPaths"] = new JArray(legacy);
            ((JArray)city["customElements"]).Add(legacy.DeepClone());
            WriteCity(cityPath, city);
            var preferences = JObject.Parse(File.ReadAllText(preferencesPath)); preferences["coreVersion"] = 4;
            File.WriteAllText(preferencesPath, preferences.ToString());
            await Call("city.load", new { folder, migrate = "always" });
            Check("migration removes obsolete records before decoding", M.customElements.Count(e => e.Settings?.propPlacer != null) == before.Count);
            var migrated = ReadCity(cityPath);
            Check("migration strips the obsolete collection", migrated["propPaths"] == null && !migrated["customElements"].Any(e => e["state"]?["type"]?.Value<string>() == "__propPath"));
            Check("migration preserves a backup of the original city", ReadCity(cityPath+".v4.bak")["propPaths"] is JArray);
            Check("migration advances the core version", JObject.Parse(File.ReadAllText(preferencesPath))["coreVersion"].Value<int>() == 5);
            await Call("city.save"); await Call("city.reload");
            Check("new saves have no obsolete collection", ReadCity(cityPath)["propPaths"] == null);
            var failed = Path.Combine(folder,"failed_migration"); Directory.CreateDirectory(failed);
            File.Copy(cityPath, Path.Combine(failed,"city.json.gz"));
            preferences["coreVersion"] = 4;
            File.WriteAllText(Path.Combine(failed,"preferences.json"), preferences.ToString());
            File.WriteAllText(Path.Combine(failed,"presets.json"), "invalid json");
            await Expect("a failed migration stops city loading", "migration_failed", "city.load", new { folder = failed, migrate = "always" });
            Check("failed migration does not decode any elements", M.customElements.Count == 0);
            await Call("city.load", new { folder });
        }
    }
}

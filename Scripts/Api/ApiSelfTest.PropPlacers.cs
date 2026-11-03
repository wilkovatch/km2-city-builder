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
                state = new { propRule = new { main = new { type = "pathsetStrip", mesh, elemDistance = 5 } } } })).Value<int>();
            Check("prop placers use the custom element API kind", (await Call("elements.get", new { @ref = line }))["kind"].Value<string>() == "customElement");
            Check("native line profile previews six props on a thirty metre span", Element(line).generator.props.Count == 6);
            Check("native line lays the X axis of the model along the path", Element(line).generator.props.All(p => Math.Abs(Mathf.PosMod(GO.GetRotation(p.obj).Y, 180) - 90) < 0.01f));
            await CheckUndo("moving a prop placer", () => Call("customElement.move", new { element = line, offset = V(3, 0, 0) }));
            await CheckUndo("inserting a prop placer control", () => Call("customElement.insert", new { element = line, array = "spline", index = 1 }));
            await Call("customElement.remove", new { element = line, array = "spline", index = 1 });
            await Expect("a spline strip rejects a type it doesn't list", "invalid_value", "customElement.setPropRule", new { element = line, rule = new { main = new { type = "customProp" } } });
            await Expect("native line rejects unencodable spacing", "invalid_value", "customElement.setPropRule", new { element = line,
                rule = new { main = new { type = "pathsetStrip", mesh, elemDistance = 64 } } });
            await Expect("native line enforces quarter metre steps", "invalid_value", "customElement.setPropRule", new { element = line,
                rule = new { main = new { type = "pathsetStrip", mesh, elemDistance = 0.3 } } });
            await Call("state.setParam", new { @ref = line, name = "segments", value = 8 });
            foreach (var mode in new[] { 0, 1 }) {
                await Call("state.setParam", new { @ref = line, name = "curveType", value = mode });
                Check("prop placers evaluate curve mode " + mode + " with the requested segments", Element(line).splineData.sections.Length == 9);
            }
            await Call("state.setParam", new { @ref = line, name = "curveType", value = 2 });
            var longLine = (await Call("customElement.create", new { type = "propLine", points = new { spline = new[] { P(-900,-700), P(-900,-1000) } },
                state = new { projectToGround = false, propRule = new { main = new { type = "pathsetStrip", mesh, elemDistance = 0.25 } } } })).Value<int>();
            Check("native line previews more than the old road iteration cap", Element(longLine).generator.props.Count == 1200);
            await Call("customElement.delete", new { element = longLine });
            var shortSpans = (await Call("customElement.create", new { type = "propLine", points = new { spline = new[] { P(-880,-700), P(-880,-712), P(-880,-715) } },
                state = new { projectToGround = false, propRule = new { main = new { type = "pathsetStrip", mesh, elemDistance = 5 } } } })).Value<int>();
            var spanProps = Element(shortSpans).generator.props.Select(p => GO.GetPosition(p.obj).Z).OrderByDescending(z => z).ToArray();
            Check("native line stretches the step to fill a span", spanProps.Length == 2 && Math.Abs(spanProps[0] - spanProps[1] - 6) < 0.001f);
            Check("native line skips a span shorter than the spacing", spanProps.All(z => z > -712.001f));
            await Call("customElement.delete", new { element = shortSpans });
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

            var strip = (await Call("customElement.create", new { type = "propLine", name = "Editable prop strip", points = new { spline = new[] { P(-620,-700), P(-620,-730) } },
                state = new { width = 4, propRule = new { main = new { type = "custom", elements = new[] { new { type = "customProp", meshes = new[] { mesh },
                    start = 0, end = 0, elemDistance = 5, maxNumber = 20, minHorizPos = 0, maxHorizPos = 1, rotationAngle = 0, random = false } } } } } })).Value<int>();
            Check("prop lines reuse road placement containers", Element(strip).generator.props.Count == 7);
            Check("strip props stay inside their editable width", Element(strip).generator.props.All(p => Math.Abs(GO.GetPosition(p.obj).X + 620) <= 2.001));
            await CheckUndo("changing strip width", () => Call("state.setParam", new { @ref = strip, name = "width", value = 8 }));
            Check("changing strip width regenerates its band", Element(strip).generator.props.All(p => Math.Abs(GO.GetPosition(p.obj).X + 620) <= 4.001));
            await PropPlacerCoreExtensions(mesh);
            await PropPlacerConnections();
            await ParkedCarRows();
            await PropLinePanel(mesh);
            await PropRows(mesh);
            await PropsTypeScripts(mesh);
            await PropGuides(mesh, surfaceMesh);

            builder.ReloadCreateMenu();
            var createMenu = CallBuilder<MenuBar>("GetMenuBar").FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>()
                .First(b => b.Text == StringManager.Get("CREATE")).GetPopup();
            var menuIndex = Enumerable.Range(0, createMenu.ItemCount).FirstOrDefault(i => createMenu.GetItemText(i) == StringManager.Get("PROPS_MENU"), -1);
            var propsMenu = menuIndex < 0 ? null : createMenu.GetItemSubmenuNode(menuIndex);
            Check("Props submenu contains the individual prop and two placers", propsMenu?.ItemCount == 3);

            //The new menu opens the full property draft without creating a city element.
            foreach (var type in new[] { "propLine", "propPolygon" }) {
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

        //the guide lines: on the ground where each prop is, otherwise the path of the props also where it's covered; easy to click on
        async Task PropGuides(string mesh, string surfaceMesh) {
            const float surfaceHeight = 6;
            var surface = (await Call("meshes.create", new { mesh = surfaceMesh, type = "instMesh", position = V(-800, surfaceHeight, -730) })).Value<int>();
            object P(float x, float z, float y = 0) => new { position = V(x, y, z) };
            CustomElement Element(int id) => History.UndoManager.GetAliveElement(id) as CustomElement;
            async Task<int> Line(float x, object rule, float width = 0, bool project = true, float y = 0) => (await Call("customElement.create", new { type = "propLine",
                points = new { spline = new[] { P(x, -700, y), P(x, -760, y) } }, state = new { width, projectToGround = project, propRule = new { main = rule } } })).Value<int>();
            var lineRule = new { type = "line", mesh, elemDistance = 5, maxNumber = 9999 };
            var pathsetRule = new { type = "pathsetStrip", mesh, elemDistance = 5 };
            var groupRule = new { type = "custom", elements = new[] { new { type = "customProp", meshes = new[] { mesh }, elemDistance = 5 } } };
            float Ground(Vector3 p) => p.X >= -815 && p.X <= -785 && p.Z >= -745 && p.Z <= -715 ? surfaceHeight : 0;

            //a custom line: on its spline, each prop projected on its own, so the ground doesn't change where they go along it
            var customLine = (await Call("customElement.create", new { type = "propLine", points = new { spline = new[] { P(-786, -700), P(-786, -760) } }, state = new { width = 4, curveType = 1, propRule = new { main = lineRule } } })).Value<int>();
            Check("a curve has sections closer than the props", Element(customLine).splineData.sections.Length > 13);
            Check("a custom line has only the guide of its spline", PropPlacerGeometry.Build(Element(customLine)).lines.Single().points.All(p => Math.Abs(p.X + 786) < 0.01f));
            var customProps = Element(customLine).generator.props.Select(p => GO.GetPosition(p.obj)).ToArray();
            Check("the props of a custom line are on the ground", customProps.Length == 13 && customProps.All(p => Math.Abs(p.Y - Ground(p)) < 0.01f) && customProps.Any(p => p.Y > 1));
            Check("the ground doesn't change where the props of a custom line go along it", customProps.Select((p, i) => Math.Abs(p.Z + 700 + 5 * i) + Math.Abs(p.X + 786)).All(d => d < 0.01f));
            Check("the props of a custom line keep their direction along the spline without a width", Element(customLine).generator.placements.All(p => Math.Abs(Math.Abs(p.rotation.Y) - 180) < 0.01f));
            var defaultYaw = Element(customLine).generator.placements.Select(p => p.rotation.Y).ToArray();
            foreach (var angle in new[] { 90f, -90f, 37f }) {
                await Call("customElement.setPropRule", new { element = customLine, rule = new { main = new { type = "line", mesh, elemDistance = 5, maxNumber = 0, rotationAngle = angle } } });
                var placements = Element(customLine).generator.placements;
                Check("custom line rotation offsets its default yaw by " + angle + " degrees", placements.Count == defaultYaw.Length
                    && placements.Select((p, i) => Math.Abs(Mathf.PosMod(p.rotation.Y - defaultYaw[i] - angle + 180, 360) - 180) < 0.01f).All(x => x));
                Check("rotating a custom line keeps its projected positions", customProps.SequenceEqual(Element(customLine).generator.props.Select(p => GO.GetPosition(p.obj))));
            }
            await Call("customElement.delete", new { element = customLine });

            //a width across the edge of the surface: the outer lines have other heights than the spline
            var individual = await Line(-786, groupRule, 4);
            var guides = PropPlacerGeometry.Build(Element(individual)).lines;
            Check("individually projected props have the guides on the ground", guides.Count == 3 && guides.All(l => !l.drawnThrough
                && l.points.All(p => Math.Abs(p.Y - Ground(p)) < 0.01f)));
            Check("the outer guides have their own heights", guides.Any(l => l.points.All(p => p.X > -785)) && guides.Any(l => l.points.All(p => p.X < -785) && l.points.Any(p => p.Y > 1)));
            Check("a guide on the ground has a point every two metres", guides.All(l => l.points.Zip(l.points.Skip(1), (a, b) => new Vector2(a.X - b.X, a.Z - b.Z).Length()).All(d => d <= 2.01f)));
            Check("a guide reaches the ends of the line", guides.All(l => Math.Abs(l.points[0].Z + 700) < 0.01f && Math.Abs(l.points[^1].Z + 760) < 0.01f));
            Check("the guides on the ground don't change the sections the exporters read", Element(individual).splineData.sections.Length < guides[0].points.Count);
            await Call("customElement.delete", new { element = individual });
            foreach (var (name, rule) in new (string, object)[] { ("parked cars", new { type = "parkedCars", elemDistance = 6 }), ("a group of props", groupRule) }) {
                var id = await Line(-786, rule);
                Check("the guide of " + name + " is on the ground", PropPlacerGeometry.Build(Element(id)).lines.Single() is var l && !l.drawnThrough && l.points.All(p => Math.Abs(p.Y - Ground(p)) < 0.01f));
                await Call("customElement.delete", new { element = id });
            }
            var polygon = (await Call("customElement.create", new { type = "propPolygon", points = new { perimeter = new[] { P(-790, -700), P(-780, -700), P(-780, -730), P(-790, -730) } } })).Value<int>();
            var outline = PropPlacerGeometry.Build(Element(polygon)).lines.Single();
            Check("a closed guide on the ground stays closed without repeating its first point", outline.closed && outline.points[0].DistanceTo(outline.points[^1]) > 0.5f
                && outline.points.All(p => Math.Abs(p.Y - Ground(p)) < 0.01f) && outline.points.Any(p => p.Y > 1));
            await Call("customElement.delete", new { element = polygon });

            //a pathset line on the ground, as the other types: each prop is there and the path goes through them
            var onGround = await Line(-786, pathsetRule);
            guides = PropPlacerGeometry.Build(Element(onGround)).lines;
            Check("the guide of a pathset line on the ground follows it", guides.Single() is var groundGuide && !groundGuide.drawnThrough && groundGuide.points.All(p => Math.Abs(p.Y - Ground(p)) < 0.01f) && groundGuide.points.Any(p => p.Y > 1));
            var groundProps = Element(onGround).generator.props.Select(p => GO.GetPosition(p.obj)).ToArray();
            Check("the props of a pathset line on the ground are on it", groundProps.Length == 12 && groundProps.All(p => Math.Abs(p.Y - Ground(p)) < 0.01f) && groundProps.Any(p => p.Y > 1));
            var groundPlaces = Element(onGround).generator.placements;
            Check("the path of a pathset line on the ground ends without a model at the end of the line", groundPlaces.Count == groundProps.Length + 1 && groundPlaces[^1].mesh == null && Math.Abs(groundPlaces[^1].position.Z + 760) < 0.01f);
            Check("the props of a pathset line on the ground keep the spacing along the line, also where the ground steps", groundPlaces.Zip(groundPlaces.Skip(1), (a, b) => a.position.DistanceTo(b.position)).All(d => d >= 4.99f));
            await Call("customElement.delete", new { element = onGround });
            var climbing = (await Call("customElement.create", new { type = "propLine", points = new { spline = new[] { P(-786, -700), P(-786, -730, surfaceHeight) } }, state = new { projectToGround = true, propRule = new { main = pathsetRule } } })).Value<int>();
            var climbingPlaces = Element(climbing).generator.placements;
            Check("the props on a sloped span are counted on the plan, so that the ground can't make a span shorter than the spacing", climbingPlaces.Count == 7
                && climbingPlaces.Zip(climbingPlaces.Skip(1), (a, b) => new Vector2(a.position.X - b.position.X, a.position.Z - b.position.Z).Length()).All(d => d >= 4.99f));
            await Call("customElement.delete", new { element = climbing });
            //without the ground: the props are along the straight spans, the guide shows them
            var flat = await Line(-900, pathsetRule, 0, false);
            guides = PropPlacerGeometry.Build(Element(flat)).lines;
            var sections = Element(flat).splineData.sections;
            Check("props on straight spans over the ground keep one guide on them", guides.Single() is var flatGuide && !flatGuide.drawnThrough && flatGuide.points.Count == sections.Length
                && flatGuide.points.Zip(sections, (p, s) => p.DistanceTo(new Vector3(s.center[0], s.center[1], s.center[2]))).All(d => d < 0.001f));
            Check("a pathset line not on the ground has no extra place in its path", Element(flat).generator.placements.Count == Element(flat).generator.props.Count);
            await Call("customElement.delete", new { element = flat });
            var straight = await Line(-786, pathsetRule, 0, false);
            guides = PropPlacerGeometry.Build(Element(straight)).lines;
            Check("a straight span under a surface has only that part of the guide drawn through it", guides.Count == 3 && !guides[0].drawnThrough && guides[1].drawnThrough && !guides[2].drawnThrough
                && guides[1].points.All(p => p.Z >= -747 && p.Z <= -713 && Math.Abs(p.Y) < 0.01f) && guides[1].points.Count > 2);
            Check("the parts of a guide stay on the span, without gaps", guides.All(l => l.points.All(p => Math.Abs(p.X + 786) < 0.01f && Math.Abs(p.Y) < 0.01f))
                && guides[0].points[^1] == guides[1].points[0] && guides[1].points[^1] == guides[2].points[0]
                && Math.Abs(guides[0].points[0].Z + 700) < 0.01f && Math.Abs(guides[2].points[^1].Z + 760) < 0.01f);
            await Frames(2);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var renderers = ((System.Collections.IList)typeof(CustomElementGenerator).GetField("lines", flags).GetValue(Element(straight).generator)).Cast<(GameEngine.LineRenderer lr, global::CustomElements.GeometryLine line)>().ToArray();
            Check("the part under a surface has the faint material", renderers.Length == 3 && renderers[1].lr.drawnThrough && renderers[1].lr.MaterialOverride == MaterialManager.GetDrawnThroughLineMaterial(Colors.Cyan)
                && !renderers[0].lr.drawnThrough && renderers[0].lr.MaterialOverride == MaterialManager.GetHandleMaterial(Colors.Cyan));
            var unprojected = await Line(-800, groupRule, 4, false, 2);
            guides = PropPlacerGeometry.Build(Element(unprojected)).lines;
            Check("props not on the ground have the guides at their height, drawn through what covers them", guides.Any(l => l.drawnThrough) && guides.Any(l => !l.drawnThrough) && guides.All(l => l.points.All(p => Math.Abs(p.Y - 2) < 0.01f)));
            await Call("customElement.delete", new { element = unprojected });

            //clicking: at the distance of the camera about the same size on screen
            await Call("view.camera.set", new { position = new[] { -786, 100, -750 }, target = new[] { -786, 0, -750 } });
            await Frames(3);
            builder.CloseAllPanels();
            builder.SetSelectObjectMode();
            var selector = (TerrainModifier.Null)builder.terrainClick.modifier;
            var mask = selector.GetLayerMasks()[0];
            bool Click(Vector3 from, Vector3 to, out GameEngine.Raycast.RaycastHit hit) => selector.Raycast(builder.helper, from, (to - from).Normalized(), out hit, 1000, mask);
            var hitWide = Click(new Vector3(-784.8f, 50, -750), new Vector3(-784.8f, 0, -750), out var clicked);
            Check("a line can be clicked from afar next to it, where its old strip was too narrow", hitWide && ElementPlacer.ReferencePicker.ElementOf(clicked.collider) == Element(straight));
            if (hitWide) History.UndoManager.Record("click a guide", () => selector.Select(clicked));
            await Frames(2);
            Check("clicking a guide opens the panel of its element", builder.customElementEditorPanel.IsOpen() && builder.customElementEditorPanel.GetElement() == Element(straight));
            builder.CloseAllPanels();
            builder.SetSelectObjectMode();
            selector = (TerrainModifier.Null)builder.terrainClick.modifier;
            await Frames(2);
            Check("a click far from the line doesn't select it", !Click(new Vector3(-776, 50, -750), new Vector3(-776, 0, -750), out clicked) || ElementPlacer.ReferencePicker.ElementOf(clicked.collider) != Element(straight));
            Check("a line can be clicked with a low camera too", Click(new Vector3(-700, 1, -750), new Vector3(-786, 1, -750), out clicked) && ElementPlacer.ReferencePicker.ElementOf(clicked.collider) == Element(straight));
            await Call("customElement.delete", new { element = straight });
            await Frames(2);
            Check("a deleted line has no collider left", !Click(new Vector3(-786, 50, -750), new Vector3(-786, 0, -750), out clicked) || ElementPlacer.ReferencePicker.ElementOf(clicked.collider) == null);

            //through what covers it: only a guide drawn there. The first ray goes down through the surface to the ground just beyond it
            var from = new Vector3(-800, 20, -730);
            var to = new Vector3(-784, 0, -730);
            await Call("view.camera.set", new { position = new[] { -784, 40, -730 }, target = new[] { -784, 0, -730 } });
            var covered = await Line(-784, lineRule);
            await Frames(3);
            Check("a guide on the ground covered by a surface isn't clicked through it", Click(from, to, out clicked) && ElementPlacer.ReferencePicker.ElementOf(clicked.collider) != Element(covered));
            await Call("customElement.delete", new { element = covered });
            covered = await Line(-788, pathsetRule, 0, false);
            await Frames(3);
            Check("a guide under a surface is clicked through it", Click(new Vector3(-788, 20, -730), new Vector3(-788, 0, -730), out clicked) && ElementPlacer.ReferencePicker.ElementOf(clicked.collider) == Element(covered));
            await Call("customElement.delete", new { element = covered });

            //the colliders of the guides are only for the clicks
            var raised = await Line(-900, pathsetRule, 0, false, 50);
            await Frames(3);
            Check("a raised guide isn't a surface to project on", Math.Abs(GeometryHelper.AutoProjectOnSurface(new Vector3(-900, 99, -730), (1 << 3) | (1 << 7)).Y) < 0.01f);
            Check("a raised guide isn't hit by the other actions", !GameEngine.Raycast.DoRaycast(builder.helper, new Vector3(-900, 99, -730), Vector3.Down, out clicked, 60, ~(1 << 3)) || ElementPlacer.ReferencePicker.ElementOf(clicked.collider) != Element(raised));
            await Call("customElement.delete", new { element = raised });
            await Call("meshes.delete", new { mesh = surface });
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
            Check("a prop line has no child panel for its props", panel.OpenPropContainer("main") == null && panel.ActiveSelf());
            //Back may refresh the parent's presets after a picker or closing action changed the click modifier.
            History.UndoManager.Record("open a list element", () => { History.UndoManager.Touch(owner); Operations.Props.GetPropRule(owner.state, false).SetState("main", Operations.Props.NewState("psdl")); });
            builder.NotifyChange();
            await Call("city.flush");
            builder.SelectObject(owner, true, null);
            History.UndoManager.Record("add a list element", () => { History.UndoManager.Touch(owner); Operations.States.AddArrayElement(owner.state.State("propRule").State("main"), Operations.Props.GetListParameter(owner.state.State("propRule").State("main"), "elements")); });
            panel.RefreshValues();
            var child = panel.OpenArrayElement("elements", 0);
            Check("a prop line opens the elements of its list in a child panel", child != null && child.ActiveSelf() && !panel.ActiveSelf());
            builder.SetSelectObjectMode();
            History.UndoManager.Record("return from prop line child without an owner", () => typeof(EditorPanel)
                .GetMethod("GoUp", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(child, null));
            panel.RefreshValues();
            Check("returning from a prop line child tolerates an absent preset state", panel.ActiveSelf() && placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None);
            builder.CloseAllPanels();
            await Call("history.undo");
            await Call("history.undo");
            builder.SelectObject(owner, true, null);
            Check("prop line can still be edited after the child panel closes", panel.GetElement() == owner && panel.ActiveSelf());
            builder.CloseAllPanels();
        }

        async Task PropPlacerCoreExtensions(string mesh) {
            const string custom = "zzPropPlacerPolygon", spline = "zzPropPlacerSpline";
            var scripts = Path.Combine(folder, "placer_lua");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "placement.lua"), "return { fill = function(v,c) return {{model=1, position=c.perimeter[1]+vec3(c.random()*v.spread,c.models[1].height,0), rotation=vec3(0,c.models[1].yaw,0), scale=vec3(2,1,1)}, {model='" + mesh + "', position=c.perimeter[2]}, {position=c.perimeter[3], data={tag='gate'}}} end }");
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
                        new CT.PropPlacerStrip { name = "a", start = "-2", end = "-2", allowedContainerTypes = new[] { "mixed" } },
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
                Check("custom polygon logic gives a mesh directly or by model, or no model at all", Element().generator.props.Count == 2 && Element().generator.placements.Count == 3
                    && Element().generator.props.All(p => p.meshName == mesh) && Element().generator.placements[2].obj == null);
                var prop = Element().generator.props[0].obj;
                Check("custom polygon logic receives global and per-model parameters", Math.Abs(GO.GetPosition(prop).Y - 7) < 0.001 && Math.Abs(GO.GetRotation(prop).Y - 25) < 0.001);
                Check("custom polygon logic returns instance scale", GO.GetLocalScale(prop) == new Vector3(2,1,1));
                var position = GO.GetPosition(prop);
                Element().state.FlagAsChanged(); await Call("city.flush");
                Check("custom placement receives repeatable local randomness", GO.GetPosition(Element().generator.props[0].obj) == position);
                await Expect("core model cap is enforced by the API", "invalid_value", "customElement.create", new { type = custom, points,
                    state = new { models = new[] { new { mesh }, new { mesh } } } });
                var settings = TP.GetCustomElementTypes()["propLine"].typeData.settings;
                var invalidPreset = Operations.CustomElementPresets.DefaultPreset("propLine");
                invalidPreset.State("propRule").State("main").SetStr("type", "customProp");
                await Expect("placer presets enforce container restrictions", "invalid_value", "presets.save", new { category = "customElement/propLine", name = "Invalid placer preset", values = invalidPreset });
                var multi = (await Call("customElement.create", new { type = spline, points = new { spline = new[] { Point(-480,-700), Point(-480,-720) } }, state = new { propRule = new {
                    a = new { type = "mixed", elements = new[] { new { type = "line", mesh, elemDistance = 5, maxNumber = 10, xPos = 0.5f } } },
                    b = new { type = "custom", elements = new[] { new { type = "customProp", meshes = new[] { mesh }, elemDistance = 5, maxNumber = 10, minHorizPos = 0.5, maxHorizPos = 0.5 } } } } } })).Value<int>(); ids.Add(multi);
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
            var spline = (await Call("customElement.create", new { type = "propLine" })).Value<int>();
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

        //the rows of props of the MM2 core
        async Task PropRows(string mesh) {
            var roadId = (await Call("roads.create", new { points = new[] { new { position = V(-190,0,-900) }, new { position = V(-190,0,-700) } } })).Value<int>();
            var road = (Road)History.UndoManager.GetAliveElement(roadId);
            var row = new { road = roadId, container = "right" };
            await Call("roads.props.enable", new { road = roadId, container = "right", enabled = true });
            await Call("state.setType", new { @ref = row, type = "line" });
            await Call("state.import", new { @ref = row, values = new { mesh, start = 2f, end = 150f, elemDistance = 7f, maxNumber = 999, xPos = 0.5f } });
            await Call("city.flush");
            var zs = road.generator.placements.Select(p => p.position.Z + 900).ToArray();
            Check("a row starts at its start and repeats at its distance", zs.Length > 1 && Math.Abs(zs[0] - 2) < 0.5f && zs.Zip(zs.Skip(1), (a, b) => Math.Abs(b - a - 7) < 0.5f).All(x => x));
            Check("a row stops before its end", zs.Max() < 200 - 150 + 1);
            await Call("state.setParam", new { @ref = row, name = "maxNumber", value = 3 });
            await Call("city.flush");
            Check("a row stops at its maximum number", road.generator.placements.Count == 3);
            await Call("state.import", new { @ref = row, values = new { end = 0f, elemDistance = 0.1f, maxNumber = 99999 } });
            await Call("city.flush");
            Check("a row of a road stops at 1000 props", road.generator.placements.Count == 1000);
            await Call("state.setType", new { @ref = row, type = "mixed" });
            foreach (var type in new[] { "line", "customProp", "psdlProp" }) {
                async Task Maximum(int maximum) {
                    var entry = new JObject { ["type"] = type, ["start"] = 0, ["elemDistance"] = 5, ["maxNumber"] = maximum };
                    if (type == "line") { entry["mesh"] = mesh; entry["xPos"] = 0.5f; }
                    else { entry["meshes"] = new JArray(mesh); entry["minHorizPos"] = 0.5f; entry["maxHorizPos"] = 0.5f; }
                    await Call("state.import", new { @ref = row, values = new { elements = new[] { entry } } });
                }
                await Maximum(9999);
                var expected = road.generator.placements.Select(p => p.position).ToArray();
                await Maximum(0);
                Check(type + " treats maximum zero as 9999", expected.Length > 3 && expected.SequenceEqual(road.generator.placements.Select(p => p.position)));
                await Maximum(3);
                Check(type + " retains a positive maximum", road.generator.placements.Count == 3);
            }
            await Call("roads.delete", new { road = roadId });
        }

        //a Lua callback of a type of the core
        async Task PropsTypeScripts(string mesh) {
            const string typeName = "zzLuaProps", spline = "zzLuaSpline";
            var scripts = Path.Combine(folder, "props_lua");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "rows.lua"), @"
return {
    fill = function(v, c)
        local res = {}
        local z = 0
        while z <= c.length do
            local line = c.sample(v.across, z)
            res[#res + 1] = { position = line.position, forward = line.forward, model = v.withModels and c.parameters.model or nil, scale = vec3(2, 1, 1), data = { index = #res, random = c.random() } }
            z = z + v.step
        end
        return res
    end,
    broken = function(v, c) error('broken on purpose') end,
    notList = function(v, c) return 3 end,
    notFinite = function(v, c) return { { position = vec3(0/0, 0, 0) } } end,
}");
            using var host = RuntimeCalculator.Scripting.LuaScriptHost.CreateForFolder(scripts);
            CT.PropsType Data(string function) => new CT.PropsType { label = "PROP_ELEM_MESH", placement = function, parametersInfo = new CT.ParameterContainer { parameters = new[] {
                new CT.Parameter { name = "model", type = "mesh" }, new CT.Parameter { name = "across", type = "float" }, new CT.Parameter { name = "step", type = "float" }, new CT.Parameter { name = "withModels", type = "bool" } } } };
            var types = TP.GetPropsTypes();
            types[typeName] = new CT.Runtime.PropsType(Data("rows.fill"), typeName, host);
            types[typeName + "Broken"] = new CT.Runtime.PropsType(Data("rows.broken"), typeName + "Broken", host);
            types[typeName + "NotList"] = new CT.Runtime.PropsType(Data("rows.notList"), typeName + "NotList", host);
            types[typeName + "NotFinite"] = new CT.Runtime.PropsType(Data("rows.notFinite"), typeName + "NotFinite", host);
            var splines = new CT.CustomElementType { parameters = new CT.ParameterContainer { parameters = Array.Empty<CT.Parameter>() },
                ui = new CT.Panel { label = "PP_MODELS" }, settings = new CT.CustomElementSettings {
                    spline = new CT.CustomElementSplineSettings(), propPlacer = new CT.PropPlacerSettings { type = "spline", projectToGround = false, strips = new[] {
                        new CT.PropPlacerStrip { name = "main", start = "-2", end = "2", allowedContainerTypes = new[] { typeName, typeName + "Broken", typeName + "NotList", typeName + "NotFinite" } } } } } };
            splines.settings.propPlacer.Initialize(splines);
            splines.settings.controlPoints = CT.CustomElementType.SplinePoints();
            TP.GetCustomElementTypes().Add(spline, new CT.Runtime.CustomElementType(splines, spline));
            var id = -1;
            try {
                object Point(float x, float z) => new { position = V(x, 0, z) };
                id = (await Call("customElement.create", new { type = spline, points = new { spline = new[] { Point(-210,-700), Point(-210,-680) } }, state = new { propRule = new {
                    main = new { type = typeName, model = mesh, across = 1f, step = 5f, withModels = true } } } })).Value<int>();
                CustomElement Element() => (CustomElement)History.UndoManager.GetAliveElement(id);
                Check("a callback gives the placements of a type", Element().generator.placements.Count == 5 && Element().generator.props.Count == 5 && Element().propPlacementError == null);
                Check("a callback samples the lines across and along", Element().generator.placements.Select((p, i) => Math.Abs(p.position.Z - (-700 + 5 * i)) < 0.01f).All(x => x)
                    && Element().generator.placements.All(p => Math.Abs(Math.Abs(p.position.X + 210) - 2) < 0.01f));
                Check("a callback reads the mesh from the parameters", Element().generator.props.All(p => p.meshName == mesh));
                Check("a callback gives the scale", Element().generator.props.All(p => GO.GetLocalScale(p.obj) == new Vector3(2, 1, 1)));
                var forwards = Element().generator.placements.Select(p => p.rotation).ToArray();
                Check("a callback gives the direction", forwards.All(r => Mathf.IsZeroApprox(r.X) && Mathf.IsZeroApprox(r.Z) && Math.Abs(Mathf.PosMod(r.Y, 180)) < 0.01f));
                var randoms = Element().generator.placements.Select(p => ((Dictionary<string, object>)p.data)["random"]).ToArray();
                Check("a callback has random numbers", randoms.Distinct().Count() == randoms.Length);
                Element().state.FlagAsChanged();
                await Call("city.flush");
                Check("the random numbers of a callback don't change at a rebuild", randoms.SequenceEqual(Element().generator.placements.Select(p => ((Dictionary<string, object>)p.data)["random"])));
                await Call("state.setParam", new { @ref = new { road = id, container = "main" }, name = "withModels", value = false });
                await Call("city.flush");
                Check("a callback can give places without a model", Element().generator.placements.Count == 5 && Element().generator.props.Count == 0);
                var jsonPath = Path.Combine(folder, "props_scripts.json");
                await Call("city.exportJson", new { path = jsonPath });
                var line = JObject.Parse(File.ReadAllText(jsonPath))["customElements"].First(e => e["type"].Value<string>() == spline)["data"]["propLines"][0];
                Check("the places without a model are exported, with their data", line["props"].Count() == 5 && line["props"].All(p => p["meshId"].Value<int>() == -1) && line["props"][2]["data"]["index"].Value<int>() == 2);
                await Call("state.setParam", new { @ref = new { road = id, container = "main" }, name = "withModels", value = true });
                await Call("state.setParam", new { @ref = new { road = id, container = "main" }, name = "model", value = "geometry/zz_missing_model.pkg" });
                await Call("city.flush");
                Check("a missing model still gives the place", Element().generator.placements.Count == 5);
                foreach (var (suffix, what) in new[] { ("Broken", "an error of a callback"), ("NotList", "a callback not returning a list"), ("NotFinite", "a non finite placement") }) {
                    await Call("customElement.setPropRule", new { element = id, rule = new { main = new { type = typeName + suffix } } });
                    await Call("city.flush");
                    Check(what + " is shown as a placement error", Element().propPlacementError == "PP_INVALID_PLACEMENT");
                }
                await Call("customElement.setPropRule", new { element = id, rule = new { main = new { type = typeName, model = mesh, across = 1f, step = 5f, withModels = true } } });
                await Call("city.flush");
                Check("a working callback clears the placement error", Element().propPlacementError == null && Element().generator.props.Count == 5);
            } finally {
                if (id >= 0) await Call("customElement.delete", new { element = id });
                TP.GetCustomElementTypes().Remove(spline);
                foreach (var suffix in new[] { "", "Broken", "NotList", "NotFinite" }) types.Remove(typeName + suffix);
            }
        }

        //the prop line of the MM2 core: one preset dropdown, the fields of its props directly in its panel, applied at once
        async Task PropLinePanel(string mesh) {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var panel = builder.customElementEditorPanel;
            IEnumerable<EditorPanelElement> Elements(EditorPanel from) => ((System.Collections.IList)typeof(EditorPanel).GetField("pages", flags).GetValue(from)).Cast<EditorPanelPage>()
                .SelectMany(page => ((List<List<EditorPanelElement>>)typeof(EditorPanelPage).GetField("elements", flags).GetValue(page)).SelectMany(row => row));
            FieldStatus Field(EditorPanel from, string name) => ((System.Collections.IList)typeof(EditorPanel).GetField("pages", flags).GetValue(from)).Cast<EditorPanelPage>()
                .SelectMany(page => ((Dictionary<EditorPanelElement, FieldStatus>)typeof(EditorPanelPage).GetField("fieldElements", flags).GetValue(page)).Values).FirstOrDefault(s => s.fieldName == name);
            string[] Options(EditorPanelElements.Dropdown dropdown) {
                var option = (OptionButton)typeof(EditorPanelElements.Dropdown).GetField("dropdown", flags).GetValue(dropdown);
                return Enumerable.Range(0, option.ItemCount).Select(i => option.GetItemText(i)).ToArray();
            }
            string TypeLabel(string type) => StringManager.Get(TP.GetPropsTypes()[type].typeData.label);
            EditorPanelElements.Dropdown TypeDropdown(EditorPanel from, string type) => Elements(from).OfType<EditorPanelElements.Dropdown>().FirstOrDefault(d => Options(d).Contains(TypeLabel(type)));
            List<string> PresetCategories(EditorPanel from) {
                var complex = (System.Collections.IDictionary)typeof(EditorPanel).GetField("complexElements", flags).GetValue(from);
                var selector = complex.Values.Cast<object>().Single(c => c.GetType().Name == "PresetSelector");
                return ((System.Collections.IDictionary)selector.GetType().GetField("dropdowns", flags).GetValue(selector)).Keys.Cast<string>().ToList();
            }
            object P(float x, float z) => new { position = V(x, 0, z) };

            var id = (await Call("customElement.create", new { type = "propLine", name = "Panel line", points = new { spline = new[] { P(-250,-700), P(-250,-670) } },
                state = new { width = 6, projectToGround = false, propRule = new { main = new { type = "pathsetStrip", mesh, elemDistance = 5 } } } })).Value<int>();
            CustomElement Line() => (CustomElement)History.UndoManager.GetAliveElement(id);
            ObjectState Props() => Line().state.State("propRule").State("main");
            int Guides() => PropPlacerGeometry.Build(Line()).lines.Count;
            builder.SelectObject(Line(), true, null);
            await Frames(2);
            Screenshot("prop_line_pathset.png");
            Check("a pathset line has its fields in its panel", Field(panel, "properties.mesh") != null && Field(panel, "properties.elemDistance") != null);
            Check("a pathset line hides the width", Field(panel, "state.properties.width") == null);
            Check("a prop line has a single preset dropdown", PresetCategories(panel).SequenceEqual(new[] { "customElement/propLine" }));
            Check("a prop line lists the types it allows", TypeDropdown(panel, "pathsetStrip") != null && Options(TypeDropdown(panel, "pathsetStrip")).Length == Operations.Props.AllowedTypes(Line().state, "main").Count);
            Check("a prop line has no move handle at its center", Line().GetMoveHandle() == null);
            var pathsetGuides = Guides();
            var pathsetPositions = Line().generator.props.Select(p => GO.GetPosition(p.obj)).ToArray();

            var meshB = mesh.Replace(".obj", "_b.obj");
            File.Copy(Path.Combine(folder, mesh), Path.Combine(folder, meshB), true);
            History.UndoManager.Record("change the mesh", () => { History.UndoManager.Touch(Line()); Field(panel, "properties.mesh").SetValue(meshB); });
            builder.NotifyChange();
            await Call("city.flush");
            Check("changing the mesh in the panel applies at once", Line().generator.props.Count == pathsetPositions.Length && Line().generator.props.All(p => p.meshName == meshB));
            await Call("history.undo");
            Check("changing the mesh in the panel can be undone", Line().generator.props.All(p => p.meshName == mesh));

            builder.SelectObject(Line(), true, null);
            await Frames(2);
            var before = (JObject)await Call("state.get", new { @ref = id });
            var dropdown = TypeDropdown(panel, "line");
            History.UndoManager.Record("switch the type", () => { History.UndoManager.Touch(Line()); dropdown.SetValue(Array.IndexOf(Options(dropdown), TypeLabel("line"))); });
            await Frames(2);
            Check("the type changes from the panel, keeping the shared values", Props().Str("type") == "line" && Props().Str("mesh") == mesh && Props().Float("elemDistance") == 5);
            Screenshot("prop_line_line.png");
            Check("the panel is rebuilt for the new type", Field(panel, "properties.maxNumber") != null);
            Check("a custom line exposes its rotation in the panel", Field(panel, "properties.rotationAngle") != null);
            Check("a custom line on a prop line hides what it doesn't use", Field(panel, "state.properties.width") == null && Field(panel, "properties.xPos") == null && Guides() == pathsetGuides);
            await Call("city.flush");
            Check("the new type places its props", Line().generator.props.Count > 0);
            await Call("history.undo");
            Check("undo restores the pathset line", JToken.DeepEquals(before, await Call("state.get", new { @ref = id })) && Guides() == pathsetGuides
                && pathsetPositions.SequenceEqual(Line().generator.props.Select(p => GO.GetPosition(p.obj))));
            builder.SelectObject(Line(), true, null);
            await Frames(2);
            Check("the panel follows the type restored by an undo", Field(panel, "state.properties.width") == null);

            var props = new { type = "mixed", elements = new object[] {
                new { type = "line", mesh, elemDistance = 5, maxNumber = 10, xPos = 0.5f },
                new { type = "customProp", meshes = new[] { mesh }, elemDistance = 5, maxNumber = 10, minHorizPos = 0.5, maxHorizPos = 0.5 } } };
            await Call("customElement.setPropRule", new { element = id, rule = new { main = props } });
            await Frames(2);
            builder.SelectObject(Line(), true, null);
            await Frames(2);
            Screenshot("prop_line_mixed.png");
            var list = panel.OpenArrayElement("elements", 0);
            Check("a line in a list has its fields", list != null && Field(list, "properties.mesh") != null && Field(list, "properties.xPos") != null);
            Check("an element of a list has its own preset", PresetCategories(list).SequenceEqual(new[] { "props/line" }));
            typeof(EditorPanel).GetMethod("GoUp", flags).Invoke(list, null);
            list = panel.OpenArrayElement("elements", 1);
            await Frames(2);
            Screenshot("prop_line_list_item.png");
            Check("a custom prop in a list has its mesh list", list != null && Field(list, "properties.xPos") == null && Elements(list).OfType<EditorPanelElements.ScrollList>().Any());
            Check("an element of a list can change type", TypeDropdown(list, "line") != null && Options(TypeDropdown(list, "line")).Length == 3);
            builder.CloseAllPanels();
            await Expect("a list rejects a type it doesn't allow", "invalid_value", "customElement.setPropRule", new { element = id, rule = new { main = new { type = "mixed", elements = new[] { new { type = "parkedCars" } } } } });

            await Call("customElement.setPropRule", new { element = id, rule = new { main = new { type = "pathsetStrip", mesh, elemDistance = 5 } } });
            builder.SelectObject(Line(), true, null);
            await Frames(2);
            History.UndoManager.Record("invalid spacing", () => { History.UndoManager.Touch(Line()); Field(panel, "properties.elemDistance").SetValue(0.3f); });
            builder.NotifyChange();
            await Call("city.flush");
            Check("a value out of the range of its parameter is shown as an error", Line().propPlacementError == "PP_INVALID_PLACEMENT");
            await Call("history.undo");
            Check("undoing it clears the error", Line().propPlacementError == null);

            //a new type keeps only what it can have
            async Task SwitchTo(string type) {
                builder.SelectObject(Line(), true, null);
                await Frames(2);
                var typeDropdown = TypeDropdown(panel, type);
                History.UndoManager.Record("switch the type", () => { History.UndoManager.Touch(Line()); typeDropdown.SetValue(Array.IndexOf(Options(typeDropdown), TypeLabel(type))); });
                builder.NotifyChange();
                await Call("city.flush");
            }
            ObjectState[] Items() => Props().Array<ObjectState>("elements") ?? new ObjectState[0];
            var customItem = new { type = "customProp", meshes = new[] { mesh }, elemDistance = 5, maxNumber = 10, minHorizPos = 0.5, maxHorizPos = 0.5 };
            await Call("customElement.setPropRule", new { element = id, rule = new { main = new { type = "custom", elements = new[] { customItem } } } });
            await SwitchTo("psdl");
            Check("custom props don't stay in a group that doesn't allow them", Props().Str("type") == "psdl" && Items().All(x => x.Str("type") == "psdlProp") && Line().propPlacementError == null);
            Check("a group shows the width and the outer lines", Field(panel, "state.properties.width") != null && Guides() == pathsetGuides + 2);
            await Call("customElement.setPropRule", new { element = id, rule = new { main = new { type = "custom", elements = new[] { customItem } } } });
            await SwitchTo("line");
            Check("a type doesn't keep the values of the old one", !Props().properties.ContainsKey("elements") && Line().propPlacementError == null);
            await SwitchTo("psdl");
            Check("nor do they come back through another type", Items().All(x => x.Str("type") == "psdlProp") && Line().propPlacementError == null);
            await Call("customElement.setPropRule", new { element = id, rule = new { main = new { type = "mixed", elements = new object[] { new { type = "line", mesh, elemDistance = 5, maxNumber = 10, xPos = 0.5f }, customItem } } } });
            await SwitchTo("custom");
            Check("a group keeps the elements it allows", Items().Length == 1 && Items()[0].Str("type") == "customProp" && Items()[0].Array<string>("meshes")?[0] == mesh && Line().propPlacementError == null);
            await Call("customElement.setPropRule", new { element = id, rule = new { main = new { type = "line", mesh, elemDistance = 5.1f, maxNumber = 10, xPos = 0.5f } } });
            await SwitchTo("pathsetStrip");
            Check("a value out of the range of the new type is replaced", Props().Str("mesh") == mesh && Props().Float("elemDistance") != 5.1f && Line().propPlacementError == null);
            builder.CloseAllPanels();
            await Call("customElement.delete", new { element = id });

            //the type can be chosen before the first point
            History.UndoManager.Record("start prop line", () => CallBuilder<object>("StartCustomElementCreation", "propLine", "CREATE_CUSTOM_ELEMENT"));
            await Frames(2);
            Check("a new prop line starts as a pathset line", Field(panel, "properties.mesh") != null && Field(panel, "state.properties.width") == null);
            dropdown = TypeDropdown(panel, "line");
            History.UndoManager.Record("switch the type of the draft", () => dropdown.SetValue(Array.IndexOf(Options(dropdown), TypeLabel("line"))));
            await Frames(2);
            Check("the draft panel follows the type", Field(panel, "properties.maxNumber") != null);
            History.UndoManager.Record("first click", () => builder.customElementPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-240,0,-700) }));
            var placed = builder.customElementPlacer.GetElement();
            Check("the type of the draft goes to the new element", placed?.state.State("propRule")?.State("main")?.Str("type") == "line");
            builder.CloseAllPanels();
            await Call("customElement.delete", new { element = History.ElementRegistry.GetId(placed) });

            //picking a mesh in the panel, with the mesh list built as when it's first opened in a city: the panel keeps its click mode
            var picker = (EditorPanels.MeshListEditorPanel)typeof(CityBuilderMenuBar).GetField("meshListEditorPanel", flags).GetValue(builder);
            async Task PickMesh(string value) {
                picker.DeferInitialize(builder);
                var field = Elements(panel).OfType<EditorPanelElements.MeshField>().First();
                ((Godot.Button)typeof(EditorPanelElements.MeshField).GetField("button", flags).GetValue(field)).EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
                typeof(EditorPanels.MeshListEditorPanel).GetField("curVal", flags).SetValue(picker, value);
                typeof(EditorPanels.MeshListEditorPanel).GetMethod("SelectAction", flags).Invoke(picker, null);
                await Frames(2);
            }
            string MeshOf(ObjectState owner) => owner?.State("propRule")?.State("main")?.Str("mesh");
            var meshPlacer = builder.customElementPlacer;
            History.UndoManager.Record("start prop line", () => CallBuilder<object>("StartCustomElementCreation", "propLine", "CREATE_CUSTOM_ELEMENT"));
            await Frames(2);
            await PickMesh(meshB);
            Check("picking a mesh for a new prop line keeps its creation", builder.terrainClick.modifier == meshPlacer && meshPlacer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.Create
                && panel.ActiveSelf() && Field(panel, "properties.mesh") != null && MeshOf(meshPlacer.creationState) == meshB);
            History.UndoManager.Record("first click", () => meshPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-240,0,-700) }));
            placed = meshPlacer.GetElement();
            Check("the mesh picked for a new prop line goes to the element", MeshOf(placed?.state) == meshB);
            await Frames(2);
            await PickMesh(mesh);
            Check("picking a mesh for a placed prop line goes back to it", builder.terrainClick.modifier == meshPlacer && panel.GetElement() == placed && panel.ActiveSelf()
                && Field(panel, "properties.mesh") != null && meshPlacer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint);
            Check("the mesh picked for a placed prop line applies at once", MeshOf(placed?.state) == mesh);
            builder.CloseAllPanels();
            await Call("customElement.delete", new { element = History.ElementRegistry.GetId(placed) });
            File.Delete(Path.Combine(folder, meshB));

            //a road: the same fields in a panel of their own
            var road = (await Call("roads.create", new { points = new[] { new { position = V(-230,0,-700) }, new { position = V(-230,0,-650) } } })).Value<int>();
            var roadElement = (Road)History.UndoManager.GetAliveElement(road);
            await Call("roads.props.enable", new { road, container = "right", enabled = true });
            await Call("state.setType", new { @ref = new { road, container = "right" }, type = "line" });
            builder.SelectObject(roadElement, true, null);
            await Frames(2);
            var roadEditor = (EditorPanels.RoadEditorPanel)typeof(CityBuilderMenuBar).GetField("roadEditorPanel", flags).GetValue(builder);
            var roadPanel = (EditorPanels.Props.PropsEditorPanel)roadEditor.OpenPropContainer("right");
            await Frames(2);
            Screenshot("road_props_panel.png");
            Check("a road opens the fields of its props in one panel", roadPanel != null && roadPanel.ActiveSelf() && Field(roadPanel, "properties.mesh") != null);
            Check("the panel of a road has a preset and a type", PresetCategories(roadPanel).SequenceEqual(new[] { "props/line" }) && TypeDropdown(roadPanel, "line") != null);
            Check("a custom line on a road has its position across", Field(roadPanel, "properties.xPos") != null);
            History.UndoManager.Record("change the mesh of a road", () => { History.UndoManager.Touch(roadElement); Field(roadPanel, "properties.mesh").SetValue(mesh); Field(roadPanel, "properties.elemDistance").SetValue(5f); Field(roadPanel, "properties.maxNumber").SetValue(10); });
            builder.NotifyChange();
            await Call("city.flush");
            Check("the props of a road change without saving", roadElement.generator.props.Any(p => p.meshName == mesh));
            builder.CloseAllPanels();
            await Call("roads.delete", new { road });
        }

        //the placements of a Lua callback, through the parked cars of the MM2 core
        async Task ParkedCarRows() {
            var models = new[] { "geometry/giz_pcar01_l.pkg", "geometry/giz_pcar02_l.pkg", "geometry/giz_pcar03_l.pkg" };
            static Vector3 Forward(Placement p) => new Vector3(Mathf.Sin(Mathf.DegToRad(p.rotation.Y)), 0, Mathf.Cos(Mathf.DegToRad(p.rotation.Y)));
            var roadId = (await Call("roads.create", new { points = new[] { new { position = V(-300,0,-700) }, new { position = V(-300,0,-600) } } })).Value<int>();
            var road = (Road)History.UndoManager.GetAliveElement(roadId);
            await Call("state.import", new { @ref = roadId, values = new { forwardPark = true, backwardPark = true, forwardParkWidth = 2, backwardParkWidth = 2 } });
            foreach (var row in new[] { "rightParking", "leftParking" }) {
                var rowRef = new { road = roadId, container = row };
                await Call("roads.props.enable", new { road = roadId, container = row, enabled = true });
                await Call("state.setType", new { @ref = rowRef, type = "parkedCars" });
                await Call("state.setParam", new { @ref = rowRef, name = "elemDistance", value = 6 });
                await Call("state.setParam", new { @ref = rowRef, name = "xPos", value = 0.5f });
            }
            await Call("city.flush");
            var slots = road.generator.placements;
            var right = slots.Where(s => s.container == "rightParking").ToArray();
            var left = slots.Where(s => s.container == "leftParking").ToArray();
            var placed = road.generator.props.ToArray();
            Check("a road row lists every slot of its length", right.Length == 17 && left.Length == 17);
            Check("the example preview leaves some slots empty", placed.Length > 0 && placed.Length < slots.Count);
            Check("the props of the callback use its models", placed.All(p => models.Contains(p.meshName)));
            const string hookModel = "geometry/giz_pcar03_l.pkg";
            Check("the example preview follows the stock game by default", placed.All(p => p.meshName != hookModel) && models.Where(m => m != hookModel).All(m => placed.Any(p => p.meshName == m)));
            Check("the cars of road rows follow the traffic of their side", right.All(s => Forward(s).Z > 0.99f) && left.All(s => Forward(s).Z < -0.99f));
            Check("each prop is at one of the slots", placed.All(p => slots.Any(s => s.container == p.container && GO.GetPosition(p.obj).DistanceTo(s.position) < 0.001f)));
            var example = placed.Select(p => (p.meshName, GO.GetPosition(p.obj))).ToArray();
            road.GetState().FlagAsChanged();
            await Call("city.flush");
            Check("the example preview doesn't change at a rebuild", example.SequenceEqual(road.generator.props.Select(p => (p.meshName, GO.GetPosition(p.obj)))));
            await Expect("a type without a mesh list has no meshes to change", "invalid_operation", "props.element.meshes.add", new { element = new { road = roadId, container = "rightParking" }, mesh = "" });

            var strip = (await Call("customElement.create", new { type = "propLine", points = new { spline = new[] { new { position = V(-280,0,-700) }, new { position = V(-280,0,-640) } } },
                state = new { width = 4, projectToGround = false, propRule = new { main = new { type = "parkedCars", elemDistance = 6, xPos = 0.5f, reverse = true } } } })).Value<int>();
            CustomElement Strip() => History.UndoManager.GetAliveElement(strip) as CustomElement;
            Check("a prop line accepts the parked cars", Strip().generator.placements.Count == 11);
            Check("reverse direction follows the spline of a prop line", Strip().generator.placements.All(s => Forward(s).Z > 0.99f));
            Check("a prop line follows the stock game by default", Strip().generator.props.All(p => p.meshName != hookModel));
            await Call("state.setParam", new { @ref = 0, name = "assumeMM2Hook", value = true });
            await Call("city.flush");
            Check("the rules of the props of a road read the injected city properties", models.All(m => road.generator.props.Any(p => p.meshName == m)));
            Check("the rules of the props of a custom element read the injected city properties", Strip().generator.props.Any(p => p.meshName == hookModel));
            Check("an injected city property doesn't change the slots of a row", road.generator.placements.Count == 34 && Strip().generator.placements.Count == 11);
            var hookJson = Path.Combine(folder, "assume_hook_export.json");
            await Call("city.exportJson", new { path = hookJson });
            Check("the exporters can read the city property", JObject.Parse(File.ReadAllText(hookJson))["cityProperties"]["assumeMM2Hook"].Value<bool>());
            await Call("state.setParam", new { @ref = 0, name = "assumeMM2Hook", value = false });
            await Call("city.flush");
            Check("turning the city property off restores the example preview", example.SequenceEqual(road.generator.props.Select(p => (p.meshName, GO.GetPosition(p.obj)))));
            await Call("state.setParam", new { @ref = 0, name = "driveOnLeft", value = true });
            await Call("city.flush");
            Check("left-hand traffic turns the cars of road rows around", road.generator.placements.All(s => Forward(s).Z * (s.container == "rightParking" ? 1 : -1) < -0.99f));
            Check("left-hand traffic doesn't change the slots of a road row", road.generator.placements.Count == 34);
            Check("left-hand traffic doesn't turn the cars of a prop line around", Strip().generator.placements.All(s => Forward(s).Z > 0.99f));
            await Call("state.setParam", new { @ref = 0, name = "driveOnLeft", value = false });
            await Call("city.flush");
            Check("right-hand traffic turns the cars of road rows back", road.generator.placements.All(s => Forward(s).Z * (s.container == "rightParking" ? 1 : -1) > 0.99f));

            var jsonPath = Path.Combine(folder, "parked_cars_export.json");
            await Call("city.exportJson", new { path = jsonPath });
            var json = JObject.Parse(File.ReadAllText(jsonPath));
            var roadLines = json["roads"].First(r => r["data"]["name"].Value<string>() == GO.GetName(road))["data"]["propLines"];
            Check("the export lists the places without a model too", roadLines.Sum(l => l["props"].Count()) == 34 && roadLines.SelectMany(l => l["props"]).Any(p => p["meshId"].Value<int>() < 0)
                && roadLines.SelectMany(l => l["props"]).Any(p => p["meshId"].Value<int>() >= 0));
            float[] Rotation(JToken prop) {
                var bytes = Convert.FromBase64String(prop["b64q_rotation"].Value<string>());
                return Enumerable.Range(0, 4).Select(i => BitConverter.ToSingle(bytes, i * 4)).ToArray();
            }
            foreach (var roadLine in roadLines) {
                var withModel = roadLine["props"].First(p => p["meshId"].Value<int>() >= 0);
                Check("a place without a model has the rotation it would have with one (" + roadLine["name"] + ")", roadLine["props"].All(p => Math.Abs(Math.Abs(Rotation(p).Zip(Rotation(withModel), (a, b) => a * b).Sum()) - 1) < 0.001f)); //a quaternion and its opposite are the same rotation
            }
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
            var owner = new ObjectState();
            Operations.Props.GetPropRule(owner, true).SetState("main", state);
            var panel = new EditorPanels.Props.PropsEditorPanel();
            panel.Initialize(builder);
            panel.SetContainer(owner, "main");
            panel.SetActive(true);
            try {
                var complex = (System.Collections.IDictionary)typeof(EditorPanel).GetField("complexElements", PreviewFlags).GetValue(panel);
                Check("the panel of a type without a mesh parameter has no mesh list", !complex.Values.Cast<object>().Any(c => c.GetType().Name == "IOList"));
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
                var expected = M.customElements.Where(e => e.state.State("propRule")?.State("main")?.Str("type") != "pathsetStrip").Sum(e => e.generator.props.Count);
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
            Check("migrated prop lines keep generating their props", M.customElements.Where(e => e.TypeName == "propLine").All(e => e.generator.props.Count > 0));
            Check("migration preserves a backup of the original city", ReadCity(cityPath+".v4.bak")["propPaths"] is JArray);
            Check("migration advances the core version", JObject.Parse(File.ReadAllText(preferencesPath))["coreVersion"].Value<int>() == CoreManager.GetCoreVersion("MidtownMadness2"));
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

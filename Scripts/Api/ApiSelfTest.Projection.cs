using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    public partial class ApiSelfTest {
        StaticBody3D ProjectionPlane(Node parent, Vector3 position, int layer, float radius = 12) {
            var body = new StaticBody3D();
            parent.AddChild(body);
            GO.SetPosition(body, position);
            GO.SetLayer(body, layer);
            var a = new Vector3(-radius, 0, -radius);
            var b = new Vector3(radius, 0, -radius);
            var c = new Vector3(radius, 0, radius);
            var d = new Vector3(-radius, 0, radius);
            GameEngine.Mesh.CreateShapeCollider(body, new ConcavePolygonShape3D { Data = new[] { a, b, c, a, c, d }, BackfaceCollision = true });
            return body;
        }

        async Task ProjectionPlacementPreviews(Dictionary<string, object> coreSettings, Vector3 surface, string surfaceName) {
            const string key = "surfaceProjectionTerrainFallbackOnly";
            foreach (var type in new[] { "train", "decal" }) {
                var placer = new ElementPlacer.CustomElementPlacer(M) {
                    createType = type, placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.Create
                };
                var mask = placer.GetLayerMasks()[2];
                //An oblique ray hits the covering grid at a different X/Z: moving its hit vertically is insufficient.
                var expected = surface + new Vector3(.35f, 0, .4f);
                var origin = expected + new Vector3(0, 70, -60);
                var direction = (expected - origin).Normalized();
                Check(type + " placement ray prioritizes " + surfaceName + " below the grid", placer.Raycast(builder.helper,
                    origin, direction, out var hit, 300, mask) && hit.point.DistanceTo(expected) < .002f);
                placer.ShowOnMap(new List<GameEngine.Raycast.RaycastHit?> { null, null, hit });
                var preview = (Node3D)typeof(ActionHandlerManager).GetField("handle", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Check(type + " preview marks the mouse ray's actual city-surface hit", GO.GetPosition(preview).DistanceTo(expected) < .002f);
                var oldModifier = builder.terrainClick.modifier;
                try {
                    builder.terrainClick.modifier = placer;
                    var snapped = new List<GameEngine.Raycast.RaycastHit?>();
                    typeof(TerrainClick).GetMethod("SnapToGrid", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(builder.terrainClick, new object[] { hit, snapped, mask });
                    Check(type + " Ctrl snapping keeps the preview on " + surfaceName, snapped.Count == 1 && snapped[0].HasValue
                        && snapped[0].Value.point.DistanceTo(surface) < .002f);
                } finally { builder.terrainClick.modifier = oldModifier; }
                coreSettings[key] = false;
                Check(type + " preview respects normal terrain priority in other cores", placer.Raycast(builder.helper,
                    origin, direction, out var terrainHit, 300, mask) && Mathf.Abs(terrainHit.point.Y - 20) < .002f);
                coreSettings[key] = true;
                Check(type + " preview falls back to terrain where city surfaces are missing", placer.Raycast(builder.helper,
                    new Vector3(3200, 99, 3000), Vector3.Down, out var fallbackHit, 200, mask) && Mathf.Abs(fallbackHit.point.Y - 20) < .002f);
                History.UndoManager.Record("preview placement " + type, () => placer.PlaceAt(hit));
                var element = placer.GetElement();
                var id = History.ElementRegistry.GetId(element);
                try {
                    Check(type + " first placed point matches its preview", element.GetAllPoints().Count == 1
                        && element.GetAllPoints()[0].GetPoint().DistanceTo(expected) < .002f);
                    expected += Vector3.Right;
                    Check(type + " append ray uses the same surface priority", placer.Raycast(builder.helper,
                        origin + Vector3.Right, direction, out var appendedHit, 300, mask) && appendedHit.point.DistanceTo(expected) < .002f);
                    placer.ShowOnMap(new List<GameEngine.Raycast.RaycastHit?> { null, null, appendedHit });
                    History.UndoManager.Record("preview append " + type, () => placer.PlaceAt(appendedHit));
                    Check(type + " appended point matches its preview", element.GetAllPoints().Count == 2
                        && element.GetAllPoints()[1].GetPoint().DistanceTo(GO.GetPosition(preview)) < .002f);
                } finally {
                    placer.SetElement(null);
                    await Call("customElement.delete", new { element = id });
                }
            }
        }

        async Task SurfaceProjection() {
            Log("surface projection");
            const int mixed = (1 << 3) | (1 << 7);
            const string key = "surfaceProjectionTerrainFallbackOnly";
            const string flatPath = "projection_flat.obj", lowerPath = "projection_lower.obj";
            SelectionObj(Path.Combine(folder, flatPath), new[] {
                new Vector3(-12, 0, -12), new Vector3(12, 0, -12), new Vector3(12, 0, 12), new Vector3(-12, 0, 12)
            }, new[] { new[] { 0, 1, 2, 0, 2, 3 } });
            SelectionObj(Path.Combine(folder, lowerPath), new[] {
                new Vector3(-12, -3, -12), new Vector3(12, -3, -12), new Vector3(12, -3, 12), new Vector3(-12, -3, 12)
            }, new[] { new[] { 0, 1, 2, 0, 2, 3 } });
            //Override the loaded setting in memory only: no core files or user preferences are written.
            var settings = typeof(CoreManager).GetMethod("GetInstance", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var values = (Dictionary<string, object>)typeof(GenericSettingsManager).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
            var hadSetting = values.TryGetValue(key, out var oldSetting);
            var fixtures = new Node3D();
            M.AddChild(fixtures);
            //Outside the city's heightmap, so the synthetic terrain has a known, independent height.
            ProjectionPlane(fixtures, new Vector3(3000, 20, 3000), 3, 250);
            ProjectionPlane(fixtures, new Vector3(3000, 5, 3000), 7);
            var upper = ProjectionPlane(fixtures, new Vector3(3000, 10, 3000), 7);
            ProjectionPlane(fixtures, new Vector3(3000, 40, 3000), 13);
            var instType = TP.GetMeshInstanceTypes()["instMesh"];
            var propType = TP.GetMeshInstanceTypes()["individualProp"];
            var instCompatible = instType.Settings.compatibleTypes;
            var propCompatible = propType.Settings.compatibleTypes;
            var ids = new List<int>();
            int decal = -1, target = -1;
            bool Near(float a, float b) => Mathf.Abs(a - b) < .002f;
            CustomElement Decal() => (CustomElement)History.UndoManager.GetAliveElement(decal);
            MeshInstance Target() => SelectionInstance(target);
            void Height(string label, float height) => Check(label + " (" + string.Join(",", Decal().splineData.sections.Select(s => s.Position.Y.ToString("F3"))) + ")", Decal().splineData.sections.Length >= 2 && Decal().splineData.sections.All(s => Near(s.Position.Y, height)));
            try {
                Check("MM2 enables grid fallback and flags only INST meshes", CoreManager.Get(key, false) && instType.Settings.projectionSurface && !propType.Settings.projectionSurface);
                values[key] = true;
                await Frames(3);
                var pos = new Vector3(3000, 99, 3000);
                Check("city surfaces far below the grid win; highest city surface wins", Near(GeometryHelper.ProjectOnSurface(pos, mixed).Y, 10));
                Check("mixed ProjectPoint shares the projection policy", Near(GeometryHelper.ProjectPoint(pos, mixed).Y, 10));
                Check("heightmap-only queries still hit the grid", Near(GeometryHelper.ProjectPoint(pos).Y, 20));
                var exact = new Vector3(3000, 10, 3000);
                Check("a hit at the input height counts as success", GameEngine.Raycast.TryProjectPoint(exact, out var hit, 1 << 7) && Near(hit.Y, 10)
                    && Near(GeometryHelper.ProjectOnSurface(exact, mixed).Y, 10));
                var fallback = new Vector3(3200, 99, 3000);
                Check("missing city surfaces fall back to the grid", Near(GeometryHelper.ProjectOnSurface(fallback, mixed).Y, 20));
                Check("terrain excluded by the mask is never a fallback", GeometryHelper.ProjectOnSurface(fallback, 1 << 7) == fallback);
                var miss = new Vector3(4000, 123, 4000);
                Check("missing all surfaces preserves the input", GeometryHelper.ProjectOnSurface(miss, mixed) == miss
                    && !GameEngine.Raycast.TryProjectPoint(miss, out var unchanged, mixed) && unchanged == miss);
                values[key] = false;
                Check("normal terrain mode chooses the highest surface", Near(GeometryHelper.ProjectOnSurface(pos, mixed).Y, 20));
                values.Remove(key);
                Check("omitted core setting defaults to normal terrain mode", Near(GeometryHelper.ProjectOnSurface(pos, mixed).Y, 20));
                values[key] = true;
                Check("projection can exclude one collider without losing its layer", Near(GeometryHelper.ProjectOnSurface(pos, mixed, upper).Y, 5));
                var stable = GeometryHelper.ProjectOnSurface(pos, mixed);
                for (int i = 0; i < 20; i++) stable = GeometryHelper.ProjectOnSurface(stable, mixed);
                Check("projection ignores overlay layer 13 and does not drift", stable.X == pos.X && stable.Z == pos.Z && Near(stable.Y, 10));
                await ProjectionPlacementPreviews(values, new Vector3(3000, 10, 3000), "generated geometry");

                decal = (await Call("customElement.create", new { type = "decal", name = "Projection spline",
                    state = new { curveType = 2, width = 1 }, points = new { spline = new[] {
                        new { position = V(3097, 90, 3000) }, new { position = V(3103, 90, 3000) }
                    } } })).Value<int>();
                Height("spline initially uses the grid fallback", 20);
                target = (await Call("meshes.create", new { mesh = flatPath, type = "instMesh", position = V(3100, 6, 3000) })).Value<int>();
                ids.Add(target);
                Height("creating an INST target refreshes existing spline sections", 6);
                await ProjectionPlacementPreviews(values, new Vector3(3100, 6, 3000), "flagged mesh geometry");
                Check("projection target bit preserves selection layer 11", GO.GetLayer(Target()) == 11
                    && Target().CollisionLayer == ((1u << 11) | (uint)GameEngine.Raycast.ProjectionSurfaceMask));
                Check("ordinary raycasts still select targets by their normal layer", GameEngine.Raycast.DoRaycast(builder.helper,
                    new Vector3(3100, 99, 3000), Vector3.Down, out var selectedHit, 200, 1 << 11) && selectedHit.collider == Target());
                Check("ordinary raycasts can still click through the mesh layer", GameEngine.Raycast.DoRaycast(builder.helper,
                    new Vector3(3100, 99, 3000), Vector3.Down, out var throughHit, 200, ~((1 << 11) | (1 << 13))) && throughHit.collider != Target());
                Target().SetMoveable(true);
                await Frames(2);
                var renderers = Target().FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
                Check("selected targets retain projection without adding a render layer", GO.GetLayer(Target()) == 8
                    && (Target().CollisionLayer & GameEngine.Raycast.ProjectionSurfaceMask) != 0
                    && renderers.Length > 0 && renderers.All(r => r.Layers == 1u << 8)
                    && Near(GeometryHelper.ProjectOnSurface(new Vector3(3100, 99, 3000), mixed).Y, 6));
                Target().SetMoveable(false);
                var prop = (await Call("meshes.create", new { mesh = flatPath, type = "individualProp", position = V(3100, 30, 3000) })).Value<int>();
                ids.Add(prop);
                Check("unflagged props do not become spline projection targets", Near(GeometryHelper.ProjectOnSurface(new Vector3(3100, 99, 3000), mixed).Y, 6)
                    && (SelectionInstance(prop).CollisionLayer & GameEngine.Raycast.ProjectionSurfaceMask) == 0);
                var placed = (await Call("meshes.create", new { mesh = flatPath, type = "individualProp", position = V(3100, 99, 3000), onGround = true })).Value<int>();
                ids.Add(placed);
                Check("API mixed-mask placement uses the shared policy", Near(GO.GetPosition(SelectionInstance(placed)).Y, 6));
                var scattered = (await Call("meshes.scatter", new { mesh = flatPath, type = "individualProp", center = V(3100, 99, 3000), radius = 1, count = 2 })).Values<int>().ToArray();
                ids.AddRange(scattered);
                Check("scattered instances use the shared projection policy", scattered.All(id => Near(GO.GetPosition(SelectionInstance(id)).Y, 6)));
                Check("a target can exclude itself and fall back to terrain", Near(GeometryHelper.ProjectOnSurface(new Vector3(3100, 99, 3000), mixed, Target()).Y, 20));

                //Exercise the actual snap rule of a parametric object on the same surfaces.
                var objectSettings = new ObjectState();
                objectSettings.SetVector3("localScale", Vector3.One);
                var parameter = new CityElements.Types.Parameter { objectInstanceSettings = new CityElements.Types.ObjectInstanceSettings { type = "mapLabelPoint" } };
                var snapped = MeshInstance.CreateParametric(flatPath, fixtures, false, "Projection object", parameter, objectSettings);
                snapped.Start();
                GO.SetPosition(snapped, new Vector3(3100, 99, 3000));
                Check("parametric surface snapping includes flagged INST meshes", snapped.SnapToSurface() && Near(GO.GetPosition(snapped).Y, 6));
                snapped.QueueFree();

                await Call("meshes.setTransform", new { mesh = target, position = V(3100, -10, 3000) });
                Height("moving a target below the grid refreshes sections", -10);
                await Call("history.undo");
                Height("undo restores projection after a target move", 6);
                await Call("history.redo");
                Height("redo restores projection after a target move", -10);
                await Call("meshes.setTransform", new { mesh = target, position = V(3100, 7, 3000), rotation = V(15, 25, 8), scale = V(2, 3, .5f) });
                var knownPoint = Target().GlobalTransform * GameEngine.Vector3.DecodePosition(new Vector3(2, 0, 1));
                var known = GameEngine.Vector3.EncodePosition(knownPoint);
                var actualHeight = GeometryHelper.ProjectOnSurface(new Vector3(known.X, 99, known.Z), mixed).Y;
                Check("translated, rotated, scaled target uses the real triangle surface (" + actualHeight + " expected " + known.Y + ")", Near(actualHeight, known.Y));
                Check("spline sections follow the transformed target plane (" + string.Join(",", Decal().splineData.sections.Select(s => (Target().GlobalTransform.AffineInverse() * GameEngine.Vector3.DecodePosition(s.Position)).Y)) + ")", Decal().splineData.sections.All(s =>
                    Mathf.Abs((Target().GlobalTransform.AffineInverse() * GameEngine.Vector3.DecodePosition(s.Position)).Y) < .002f));
                await Call("meshes.setTransform", new { mesh = target, position = V(3100, 6, 3000), rotation = V(0, 0, 0), scale = V(1, 1, 1) });
                await Call("meshes.replaceMesh", new { mesh = target, newMesh = lowerPath });
                Height("mesh replacement refreshes spline projection", 3);
                await Call("history.undo");
                Height("undo restores the original target mesh projection", 6);
                await Call("history.redo");
                Height("redo restores replacement target projection", 3);
                await Call("meshes.delete", new { mesh = target });
                Height("deleting a target restores the terrain fallback", 20);
                await Call("history.undo");
                Height("undoing target deletion restores surface projection", 3);
                instType.Settings.compatibleTypes = new[] { "individualProp" };
                propType.Settings.compatibleTypes = new[] { "instMesh" };
                await Call("meshes.setType", new { mesh = target, type = "individualProp" });
                Height("switching to an unflagged type removes the target", 20);
                await Call("history.undo");
                Height("undoing a type change restores the target", 3);
                await Call("history.redo");
                Height("redoing a type change removes the target", 20);
                await Call("meshes.setType", new { mesh = target, type = "instMesh" });
                Height("switching back to a flagged type restores projection", 3);
                var unchangedMesh = Decal().generator.mr.Mesh;
                for (int i = 0; i < 3; i++) { M.FlagAsChanged(); await Call("city.flush"); }
                Check("unchanged target snapshots do not rebuild splines", Decal().generator.mr.Mesh == unchangedMesh);
                var exported = await Call("elements.geometry", new { @ref = decal });
                var pieces = (JArray)exported["element"]["spline"]["pieces"];
                Check("exported spline pieces use the settled target height", pieces.Count > 0 && pieces.SelectMany(p => p.Children()).SelectMany(s => s["vertices"].Children()).All(v => Near(v[1].Value<float>(), 3)));

                instType.Settings.compatibleTypes = instCompatible;
                propType.Settings.compatibleTypes = propCompatible;
                //Remove synthetic terrain before reload, leaving only a real saved mesh target and spline.
                fixtures.QueueFree();
                await Frames(2);
                await Call("city.save");
                await Call("city.load", new { folder });
                decal = History.ElementRegistry.GetId(M.customElements.Single(e => GO.GetName(e) == "Projection spline"));
                target = History.ElementRegistry.GetId(M.meshes.Single(m => m.meshPath == lowerPath));
                Height("save/reload preserves target-based projection", 3);
                Check("reload preserves the target bit and ordinary selection layer", GO.GetLayer(Target()) == 11
                    && (Target().CollisionLayer & GameEngine.Raycast.ProjectionSurfaceMask) != 0);
                ids.Clear();
                ids.AddRange(M.meshes.Where(m => m.meshPath == flatPath || m.meshPath == lowerPath).Select(History.ElementRegistry.GetId));
            } finally {
                if (hadSetting) values[key] = oldSetting; else values.Remove(key);
                instType.Settings.compatibleTypes = instCompatible;
                propType.Settings.compatibleTypes = propCompatible;
                if (GodotObject.IsInstanceValid(fixtures) && !fixtures.IsQueuedForDeletion()) fixtures.QueueFree();
                if (decal >= 0 && History.UndoManager.GetAliveElement(decal) is CustomElement) await Call("customElement.delete", new { element = decal });
                foreach (var id in ids) {
                    if (History.UndoManager.GetAliveElement(id) is MeshInstance) await Call("meshes.delete", new { mesh = id });
                }
            }
        }
    }
}

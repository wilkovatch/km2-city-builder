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

        //the mouse ray of the editor with the placer as the click mode (it skips the hidden terrain)
        bool ClickRay(TerrainAction placer, Vector3 origin, Vector3 direction, out GameEngine.Raycast.RaycastHit hit, float distance, int mask) {
            var oldModifier = builder.terrainClick.modifier;
            var args = new object[] { origin, direction, null, distance, mask };
            bool found;
            try {
                builder.terrainClick.modifier = placer;
                found = (bool)typeof(TerrainClick).GetMethod("Raycast", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(builder.terrainClick, args);
            } finally { builder.terrainClick.modifier = oldModifier; }
            hit = found ? (GameEngine.Raycast.RaycastHit)args[2] : default;
            return found;
        }

        List<GameEngine.Raycast.RaycastHit?> CtrlSnap(TerrainAction placer, GameEngine.Raycast.RaycastHit hit, int mask) {
            var oldModifier = builder.terrainClick.modifier;
            var snapped = new List<GameEngine.Raycast.RaycastHit?>();
            try {
                builder.terrainClick.modifier = placer;
                snapped.Add((GameEngine.Raycast.RaycastHit)typeof(TerrainClick).GetMethod("SnapToGrid", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(builder.terrainClick, new object[] { hit, mask }));
            } finally { builder.terrainClick.modifier = oldModifier; }
            return snapped;
        }

        //The clicks hit what is visible (the grid covering a surface wins, unless it's hidden), except for the elements
        //whose sections are projected (the decals): their clicks follow the core's surface rule, as their sections.
        async Task ProjectionPlacementPreviews(Dictionary<string, object> coreSettings, Vector3 surface, string surfaceName, params string[] types) {
            const string key = "surfaceProjectionTerrainFallbackOnly";
            foreach (var type in types) {
                var placer = new ElementPlacer.CustomElementPlacer(M) {
                    createType = type, placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.Create
                };
                var auto = TP.GetCustomElementTypes()[type].typeData.settings.spline?.projectSections == true;
                var mask = placer.GetLayerMasks()[2];
                //An oblique ray hits the covering grid at a different X/Z: moving its hit vertically is insufficient.
                var expected = surface + new Vector3(.35f, 0, .4f);
                var origin = expected + new Vector3(0, 70, -60);
                var direction = (expected - origin).Normalized();
                var visible = ClickRay(placer, origin, direction, out var visibleHit, 300, mask);
                if (auto) {
                    Check(type + " placement ray prioritizes " + surfaceName + " below the visible grid", visible && visibleHit.point.DistanceTo(expected) < .002f);
                    coreSettings[key] = false;
                    Check(type + " placement ray respects normal terrain priority in other cores", ClickRay(placer, origin, direction,
                        out var terrainHit, 300, mask) && Mathf.Abs(terrainHit.point.Y - 20) < .002f);
                    coreSettings[key] = true;
                    Check(type + " placement ray falls back to the grid where city surfaces are missing", ClickRay(placer,
                        new Vector3(3200, 99, 3000), Vector3.Down, out var fallbackHit, 200, mask) && Mathf.Abs(fallbackHit.point.Y - 20) < .002f);
                } else {
                    Check(type + " placement ray hits the visible grid over " + surfaceName + " (core fallback policy on)", visible
                        && Mathf.Abs(visibleHit.point.Y - 20) < .002f);
                }
                //the elements placed on what is visible reach the surface under the grid when it's hidden
                await Call("view.setTerrainHidden", new { hidden = true });
                CustomElement element = null;
                try {
                    Check(type + " a hidden grid can not be clicked", !ClickRay(placer, new Vector3(3200, 99, 3000), Vector3.Down, out _, 200, mask));
                    if (auto) await Call("view.setTerrainHidden", new { hidden = false });
                    Check(type + " placement ray reaches " + surfaceName + (auto ? "" : " with the grid hidden"), ClickRay(placer, origin, direction,
                        out var hit, 300, mask) && hit.point.DistanceTo(expected) < .002f);
                    placer.ShowOnMap(new List<GameEngine.Raycast.RaycastHit?> { null, null, hit });
                    var preview = (Node3D)typeof(ActionHandlerManager).GetField("handle", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    Check(type + " preview marks the hit of the mouse ray", GO.GetPosition(preview).DistanceTo(expected) < .002f);
                    var snapped = CtrlSnap(placer, hit, mask);
                    Check(type + " Ctrl snapping keeps the preview on " + surfaceName, snapped.Count == 1 && snapped[0].HasValue
                        && snapped[0].Value.point.DistanceTo(surface) < .002f);
                    History.UndoManager.Record("preview placement " + type, () => placer.PlaceAt(hit));
                    element = placer.GetElement();
                    Check(type + " first placed point matches its preview", element.GetAllPoints().Count == 1
                        && element.GetAllPoints()[0].GetPoint().DistanceTo(expected) < .002f);
                    expected += Vector3.Right;
                    Check(type + " append ray hits the same surface", ClickRay(placer, origin + Vector3.Right, direction, out var appendedHit, 300, mask)
                        && appendedHit.point.DistanceTo(expected) < .002f);
                    placer.ShowOnMap(new List<GameEngine.Raycast.RaycastHit?> { null, null, appendedHit });
                    History.UndoManager.Record("preview append " + type, () => placer.PlaceAt(appendedHit));
                    Check(type + " appended point matches its preview", element.GetAllPoints().Count == 2
                        && element.GetAllPoints()[1].GetPoint().DistanceTo(GO.GetPosition(preview)) < .002f);
                } finally {
                    placer.SetElement(null);
                    if (element != null) await Call("customElement.delete", new { element = History.ElementRegistry.GetId(element) });
                    await Call("view.setTerrainHidden", new { hidden = false });
                }
            }
        }

        async Task PropPlacementPreviews(Vector3 surface, string surfaceName, string meshPath) {
            await Frames(2);
            var placer = new ElementPlacer.MeshPlacer(M) {
                settings = Operations.Meshes.NewSettings("individualProp"), meshPath = meshPath, multipleRadius = 1, multipleIntensity = 2
            };
            var mask = placer.GetLayerMasks()[0];
            var expected = surface + new Vector3(.35f, 0, .4f);
            var origin = expected + new Vector3(0, 70, -60);
            var direction = (expected - origin).Normalized();
            Check("prop placement hits the visible grid over " + surfaceName, ClickRay(placer, origin, direction, out var gridHit, 300, mask)
                && Mathf.Abs(gridHit.point.Y - 20) < .002f);
            await Call("view.setTerrainHidden", new { hidden = true });
            try {
                Check("prop placement reaches " + surfaceName + " with the grid hidden", ClickRay(placer, origin, direction, out var hit, 300, mask)
                    && hit.point.DistanceTo(expected) < .002f);
                var hits = new List<GameEngine.Raycast.RaycastHit?> { hit };
                var snapped = CtrlSnap(placer, hit, mask);
                Check("prop Ctrl snapping stays on " + surfaceName, snapped.Count == 1 && snapped[0].HasValue
                    && snapped[0].Value.point.DistanceTo(surface) < .002f);
                foreach (var multiple in new[] { false, true }) {
                    placer.multiple = multiple;
                    placer.ShowOnMap(hits);
                    var preview = (Node3D)typeof(ActionHandlerManager).GetField(multiple ? "discHandle" : "handle", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    Check((multiple ? "mass prop" : "single prop") + " preview marks " + surfaceName,
                        GO.GetPosition(preview).DistanceTo(expected + (multiple ? Vector3.Up * .1f : Vector3.Zero)) < .002f);
                    var previous = M.meshes.ToHashSet();
                    var mouse = new Vector2(550, 350);
                    var oldMouse = GetViewport().GetMousePosition();
                    var oldUiEnabled = builder.terrainClick.uiEnabled;
                    var applied = false;
                    void ClickFrame() {
                        if (applied || !GameEngine.Input.GetInputStart(GameEngine.Input.KeyCommand.Select)) return;
                        applied = true;
                        History.UndoManager.Record("prop preview click", () => placer.Apply(hits));
                    }
                    builder.terrainClick.uiEnabled = false;
                    GetViewport().WarpMouse(mouse);
                    await Frames(2);
                    GetTree().ProcessFrame += ClickFrame;
                    try {
                        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = mouse, GlobalPosition = mouse });
                        await Frames(2);
                    } finally {
                        GetTree().ProcessFrame -= ClickFrame;
                        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = mouse, GlobalPosition = mouse });
                        builder.terrainClick.uiEnabled = oldUiEnabled;
                        GetViewport().WarpMouse(oldMouse);
                    }
                    var created = M.meshes.Where(m => !previous.Contains(m)).ToArray();
                    try {
                        Check((multiple ? "scattered props" : "single placed prop") + " agree with the preview surface (" + created.Length + " placed)", created.Length == (multiple ? 2 : 1)
                            && created.All(m => Mathf.Abs(GO.GetPosition(m).Y - surface.Y) < .002f)
                            && (multiple || GO.GetPosition(created[0]).DistanceTo(expected) < .002f));
                    } finally {
                        foreach (var mesh in created) await Call("meshes.delete", new { mesh = History.ElementRegistry.GetId(mesh) });
                    }
                }
            } finally {
                await Call("view.setTerrainHidden", new { hidden = false });
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
            //a road a little under the grid covering it
            ProjectionPlane(fixtures, new Vector3(3200, 19.8f, 3200), 7);
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
                Check("city surfaces far below the grid win; highest city surface wins", Near(GeometryHelper.AutoProjectOnSurface(pos, mixed).Y, 10));
                Check("mixed ProjectPoint is a plain highest-surface projection", Near(GeometryHelper.ProjectPoint(pos, mixed).Y, 20));
                Check("heightmap-only queries still hit the grid", Near(GeometryHelper.ProjectPoint(pos).Y, 20));
                var exact = new Vector3(3000, 10, 3000);
                Check("a hit at the input height counts as success", GameEngine.Raycast.TryProjectPoint(exact, out var hit, 1 << 7) && Near(hit.Y, 10)
                    && Near(GeometryHelper.AutoProjectOnSurface(exact, mixed).Y, 10));
                var fallback = new Vector3(3200, 99, 3000);
                Check("missing city surfaces fall back to the grid", Near(GeometryHelper.AutoProjectOnSurface(fallback, mixed).Y, 20));
                Check("terrain excluded by the mask is never a fallback", GeometryHelper.AutoProjectOnSurface(fallback, 1 << 7) == fallback);
                var miss = new Vector3(4000, 123, 4000);
                Check("missing all surfaces preserves the input", GeometryHelper.AutoProjectOnSurface(miss, mixed) == miss
                    && !GameEngine.Raycast.TryProjectPoint(miss, out var unchanged, mixed) && unchanged == miss);
                values[key] = false;
                Check("normal terrain mode chooses the highest surface", Near(GeometryHelper.AutoProjectOnSurface(pos, mixed).Y, 20));
                values.Remove(key);
                Check("omitted core setting defaults to normal terrain mode", Near(GeometryHelper.AutoProjectOnSurface(pos, mixed).Y, 20));
                values[key] = true;
                Check("projection can exclude one collider without losing its layer", Near(GeometryHelper.AutoProjectOnSurface(pos, mixed, upper).Y, 5));
                var stable = GeometryHelper.AutoProjectOnSurface(pos, mixed);
                for (int i = 0; i < 20; i++) stable = GeometryHelper.AutoProjectOnSurface(stable, mixed);
                Check("projection ignores overlay layer 13 and does not drift", stable.X == pos.X && stable.Z == pos.Z && Near(stable.Y, 10));

                //The placement rule of the clicked or moved points ignores the core policy: the grid covering a city surface
                //wins, unless the surface is less than 0.5 m under it or the grid is hidden.
                Check("placement snaps keep the grid over deep city surfaces", Near(GeometryHelper.ProjectOnSurface(pos, mixed).Y, 20));
                var shallow = new Vector3(3200, 99, 3200);
                Check("placement snaps prefer a road a little under the grid", Near(GeometryHelper.ProjectOnSurface(shallow, mixed).Y, 19.8f)
                    && Near(GeometryHelper.ProjectPoint(shallow, mixed).Y, 20));
                Check("placement snaps fall back to the grid", Near(GeometryHelper.ProjectOnSurface(fallback, mixed).Y, 20));
                Check("the grid starts visible", !(bool)await Call("view.terrainHidden") && builder.helper.terrainObj.Visible);
                await Call("view.setTerrainHidden", new { hidden = true });
                try {
                    Check("view.setTerrainHidden hides the grid and saves it", (bool)await Call("view.terrainHidden") && !builder.helper.terrainObj.Visible
                        && PreferencesManager.Get("TerrainHidden", false) && CityGroundHelper.ExcludeHiddenTerrain(mixed) == (1 << 7));
                    Check("placement snaps skip the hidden grid", Near(GeometryHelper.ProjectOnSurface(pos, mixed).Y, 10)
                        && GeometryHelper.ProjectOnSurface(fallback, mixed) == fallback);
                    Check("the automatic projection still uses the hidden grid", Near(GeometryHelper.AutoProjectOnSurface(fallback, mixed).Y, 20)
                        && Near(GeometryHelper.ProjectPoint(fallback).Y, 20));
                } finally {
                    await Call("view.setTerrainHidden", new { hidden = false });
                }
                Check("view.setTerrainHidden shows the grid again", !CityGroundHelper.TerrainHidden && builder.helper.terrainObj.Visible
                    && CityGroundHelper.ExcludeHiddenTerrain(mixed) == mixed);
                await ProjectionPlacementPreviews(values, new Vector3(3000, 10, 3000), "generated geometry", "train", "decal");
                await PropPlacementPreviews(new Vector3(3000, 10, 3000), "generated geometry", flatPath);

                decal = (await Call("customElement.create", new { type = "decal", name = "Projection spline",
                    state = new { curveType = 2, width = 1 }, points = new { spline = new[] {
                        new { position = V(3097, 90, 3000) }, new { position = V(3103, 90, 3000) }
                    } } })).Value<int>();
                Height("spline initially uses the grid fallback", 20);
                target = (await Call("meshes.create", new { mesh = flatPath, type = "instMesh", position = V(3100, 6, 3000) })).Value<int>();
                ids.Add(target);
                Height("creating an INST target refreshes existing spline sections", 6);
                await ProjectionPlacementPreviews(values, new Vector3(3100, 6, 3000), "flagged mesh geometry", "decal");
                await PropPlacementPreviews(new Vector3(3100, 6, 3000), "a mesh", flatPath);
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
                    && Near(GeometryHelper.AutoProjectOnSurface(new Vector3(3100, 99, 3000), mixed).Y, 6));
                Target().SetMoveable(false);
                var prop = (await Call("meshes.create", new { mesh = flatPath, type = "individualProp", position = V(3100, 30, 3000) })).Value<int>();
                ids.Add(prop);
                Check("unflagged props do not become spline projection targets", Near(GeometryHelper.AutoProjectOnSurface(new Vector3(3100, 99, 3000), mixed).Y, 6)
                    && (SelectionInstance(prop).CollisionLayer & GameEngine.Raycast.ProjectionSurfaceMask) == 0);
                var placed = (await Call("meshes.create", new { mesh = flatPath, type = "individualProp", position = V(3100, 99, 3000), onGround = true })).Value<int>();
                ids.Add(placed);
                Check("API placement on the ground ignores the core policy", Near(GO.GetPosition(SelectionInstance(placed)).Y, 20));
                var scattered = (await Call("meshes.scatter", new { mesh = flatPath, type = "individualProp", center = V(3100, 99, 3000), radius = 1, count = 2 })).Values<int>().ToArray();
                ids.AddRange(scattered);
                Check("scattered instances use the placement rule", scattered.All(id => Near(GO.GetPosition(SelectionInstance(id)).Y, 20)));
                Check("a target can exclude itself and fall back to terrain", Near(GeometryHelper.AutoProjectOnSurface(new Vector3(3100, 99, 3000), mixed, Target()).Y, 20));

                //Exercise the actual snap rule of a parametric object on the same surfaces.
                var objectSettings = new ObjectState();
                objectSettings.SetVector3("localScale", Vector3.One);
                var parameter = new CityElements.Types.Parameter { objectInstanceSettings = new CityElements.Types.ObjectInstanceSettings { type = "mapLabelPoint" } };
                var snapped = MeshInstance.CreateParametric(flatPath, fixtures, false, "Projection object", parameter, objectSettings);
                snapped.Start();
                GO.SetPosition(snapped, new Vector3(3100, 99, 3000));
                Check("parametric surface snapping keeps the visible grid", snapped.SnapToSurface() && Near(GO.GetPosition(snapped).Y, 20));
                await Call("view.setTerrainHidden", new { hidden = true });
                try {
                    GO.SetPosition(snapped, new Vector3(3100, 99, 3000));
                    Check("parametric surface snapping reaches the mesh under the hidden grid", snapped.SnapToSurface() && Near(GO.GetPosition(snapped).Y, 6));
                } finally {
                    await Call("view.setTerrainHidden", new { hidden = false });
                }
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
                var actualHeight = GeometryHelper.AutoProjectOnSurface(new Vector3(known.X, 99, known.Z), mixed).Y;
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

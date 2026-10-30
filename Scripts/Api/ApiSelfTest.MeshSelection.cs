using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    public partial class ApiSelfTest {
        static MeshInstance3D SelectionSurface(Node parent, Vector3[] vertices, int[] indices = null, Godot.Mesh.PrimitiveType primitive = Godot.Mesh.PrimitiveType.Triangles) {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = vertices;
            if (indices != null) arrays[(int)Godot.Mesh.ArrayType.Index] = indices;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(primitive, arrays);
            var renderer = new MeshInstance3D { Mesh = mesh };
            parent.AddChild(renderer);
            return renderer;
        }

        void SelectionGeometry() {
            var root = new LODGroup { Transform = new Transform3D(new Basis(Vector3.Up, 0.3f).Scaled(new Vector3(-2, 3, 1)), new Vector3(7, 4, 9)) };
            var triangle = new[] { Vector3.Zero, Vector3.Right, Vector3.Back };
            var high = SelectionSurface(root, triangle, new[] { 0, 1, 2, 0, 0, 1 });
            var low = SelectionSurface(root, triangle);
            low.Position = new Vector3(100, 0, 0);
            var shared = SelectionSurface(root, triangle);
            shared.Position = new Vector3(2, 0, 0);
            var always = SelectionSurface(root, triangle);
            always.Position = new Vector3(4, 0, 0);
            root.SetLODs(new[] { 0.5f, 0.01f }, new[] {
                new List<GeometryInstance3D> { high, shared }, new List<GeometryInstance3D> { low, shared }
            });
            var nested = new LODGroup { Position = new Vector3(0, 5, 0) };
            root.AddChild(nested);
            var nestedHigh = SelectionSurface(nested, triangle);
            var nestedLow = SelectionSurface(nested, triangle);
            nestedLow.Position = new Vector3(200, 0, 0);
            nested.SetLODs(new[] { 0.5f, 0.01f }, new[] { new List<GeometryInstance3D> { nestedHigh }, new List<GeometryInstance3D> { nestedLow } });
            SelectionSurface(root, new[] { Vector3.Zero, Vector3.One }, primitive: Godot.Mesh.PrimitiveType.Lines);
            var line = new GameEngine.LineRenderer { Mesh = high.Mesh };
            root.AddChild(line);
            try {
                using var shape = (ConcavePolygonShape3D)GameEngine.Mesh.CreateSelectionShape(root);
                var expected = new[] { high, shared, always, nestedHigh }.SelectMany(r => triangle.Select(v => root.Transform * (r == nestedHigh ? nested.Transform : Transform3D.Identity) * r.Transform * v)).ToArray();
                var faces = shape.GetFaces();
                Check("selection combines indexed/non-indexed surfaces, skipping degenerates, lines and lower LODs", faces.Length == 12);
                Check("selection includes root and nested local transforms", faces.Length == expected.Length && faces.Zip(expected).All(p => p.First.DistanceTo(p.Second) < 0.0001f));
                Check("selection triangles are double sided", shape.BackfaceCollision);
                high.Visible = false;
                nestedHigh.Visible = false;
                low.Visible = true;
                using var hiddenShape = (ConcavePolygonShape3D)GameEngine.Mesh.CreateSelectionShape(root);
                Check("selection geometry is independent of current LOD visibility", hiddenShape.GetFaces().SequenceEqual(faces));
                var clone = (Node3D)root.Duplicate((int)Node.DuplicateFlags.Scripts);
                try {
                    using var cloneShape = (ConcavePolygonShape3D)GameEngine.Mesh.CreateSelectionShape(clone);
                    Check("duplicated LOD metadata preserves selection geometry", cloneShape.GetFaces().SequenceEqual(faces));
                } finally { clone.Free(); }
            } finally { root.Free(); }
            var empty = new Node3D();
            try {
                SelectionSurface(empty, triangle, new[] { 0, 0, 0 });
                Check("no usable triangles gives no selection shape", GameEngine.Mesh.CreateSelectionShape(empty) == null && GameEngine.Mesh.CreateSelectionShape(null) == null);
            } finally { empty.Free(); }
        }

        //OBJ import mirrors X; write these generated fixtures in program coordinates.
        static void SelectionObj(string path, Vector3[] vertices, int[][] surfaces) {
            var lines = vertices.Select(v => string.Format(CultureInfo.InvariantCulture, "v {0} {1} {2}", -v.X, v.Y, v.Z)).ToList();
            for (int s = 0; s < surfaces.Length; s++) {
                lines.Add("usemtl selection" + s);
                var indices = surfaces[s];
                for (int i = 0; i < indices.Length; i += 3) lines.Add($"f {indices[i] + 1} {indices[i + 1] + 1} {indices[i + 2] + 1}");
            }
            File.WriteAllLines(path, lines);
        }

        MeshInstance SelectionInstance(int id) => (MeshInstance)History.UndoManager.GetAliveElement(id);
        static CollisionShape3D SelectionCollider(MeshInstance instance) => instance.GetChildren().OfType<CollisionShape3D>().SingleOrDefault();

        Node SelectionHit(MeshInstance instance, Vector3 local, bool fromBelow = false, int mask = (1 << 11) | (1 << 13)) {
            var step = instance.GlobalTransform.Basis * (fromBelow ? Vector3.Up : Vector3.Down);
            var point = instance.GlobalTransform * GameEngine.Vector3.DecodePosition(local);
            var origin = GameEngine.Vector3.EncodePosition(point - step * 30);
            return GameEngine.Raycast.DoRaycast(builder.helper, origin, GameEngine.Vector3.EncodePosition(step.Normalized()), out var hit, step.Length() * 60, mask) ? hit.collider : null;
        }

        async Task MeshSelection() {
            SelectionGeometry();
            const string frontPath = "selection_front.obj", rearPath = "selection_rear.obj";
            SelectionObj(Path.Combine(folder, frontPath), new[] {
                new Vector3(-4, -2, -4), new Vector3(-1, -2, -4), new Vector3(-1, -2, 4), new Vector3(-4, -2, 4),
                new Vector3(4, -2, -4), new Vector3(4, -2, -1), new Vector3(-1, -2, -1)
            }, new[] { new[] { 0, 1, 2, 0, 2, 3 }, new[] { 1, 4, 5, 1, 5, 6 } });
            SelectionObj(Path.Combine(folder, rearPath), new[] {
                new Vector3(-4, -6, -4), new Vector3(4, -6, -4), new Vector3(4, -6, 4), new Vector3(-4, -6, 4)
            }, new[] { new[] { 0, 1, 2, 0, 2, 3 } });
            var front = (await Call("meshes.create", new { mesh = frontPath, position = V(1400, 60, 1400), type = "instMesh" })).Value<int>();
            var rear = (await Call("meshes.create", new { mesh = rearPath, position = V(1400, 60, 1400), type = "instMesh" })).Value<int>();
            var copy = (await Call("meshes.create", new { mesh = frontPath, position = V(1430, 60, 1400), type = "instMesh" })).Value<int>();
            await Frames(3);
            var surface = new Vector3(-3, -2, 2);
            var gap = new Vector3(2, -2, 2);
            var firstShape = SelectionCollider(SelectionInstance(front)).Shape;
            Check("mesh instances have one triangle collider", firstShape is ConcavePolygonShape3D && ((ConcavePolygonShape3D)firstShape).GetFaces().Length == 12);
            Check("mesh copies share the cached selection shape", firstShape == SelectionCollider(SelectionInstance(copy)).Shape);
            Check("clicking an empty mesh region selects the object behind it", SelectionHit(SelectionInstance(front), gap) == SelectionInstance(rear));
            Check("clicking a mesh surface selects the mesh", SelectionHit(SelectionInstance(front), surface) == SelectionInstance(front));
            Check("mesh surfaces can be selected from below", SelectionHit(SelectionInstance(copy), surface, true) == SelectionInstance(copy));
            foreach (var scale in new[] { V(2, 3, 0.5f), V(-2, 3, 0.5f) }) {
                await Call("meshes.setTransform", new { mesh = front, position = V(1405, 70, 1403), rotation = V(15, 37, 8), scale });
                await Frames(2);
                Check("transformed mesh selection (scale X " + scale[0] + ")", SelectionHit(SelectionInstance(front), surface) == SelectionInstance(front));
            }
            Check("moving a mesh leaves the shared shape and other copies unchanged", SelectionHit(SelectionInstance(copy), surface) == SelectionInstance(copy) && firstShape == SelectionCollider(SelectionInstance(front)).Shape);
            //Exercise autoYOffset independently of optional core transforms (e.g. a core can return zero for an unknown prop).
            //The placement rules change with the type: the two MM2 types are made compatible for the test.
            var propType = TP.GetMeshInstanceTypes()["individualProp"];
            var instType = TP.GetMeshInstanceTypes()["instMesh"];
            var rules = propType.rules;
            var coreTransformRule = rules.meshTransform;
            var propCompatible = propType.Settings.compatibleTypes;
            var instCompatible = instType.Settings.compatibleTypes;
            rules.meshTransform = null;
            propType.Settings.compatibleTypes = new[] { "instMesh" };
            instType.Settings.compatibleTypes = new[] { "individualProp" };
            try {
                var prop = (await Call("meshes.create", new { mesh = frontPath, position = V(1450, 60, 1400), type = "individualProp" })).Value<int>();
                await Frames(2);
                Check("initial prop offset is applied to the collider exactly once", SelectionCollider(SelectionInstance(prop)).Position.DistanceTo(new Vector3(0, 2, 0)) < 0.001f
                    && SelectionHit(SelectionInstance(prop), surface + Vector3.Up * 2) == SelectionInstance(prop));
                await Call("meshes.delete", new { mesh = prop });
                await Call("meshes.setType", new { mesh = copy, type = "individualProp" });
                await Frames(2);
                Check("prop placement offset moves the collider with the geometry", SelectionCollider(SelectionInstance(copy)).Position.DistanceTo(new Vector3(0, 2, 0)) < 0.001f && SelectionHit(SelectionInstance(copy), surface + Vector3.Up * 2) == SelectionInstance(copy));
                await Call("meshes.setType", new { mesh = copy, type = "instMesh" });
                await Frames(2);
                Check("changing placement rules restores the collider without accumulating offsets", SelectionCollider(SelectionInstance(copy)).Position.IsZeroApprox());
            } finally {
                rules.meshTransform = coreTransformRule;
                propType.Settings.compatibleTypes = propCompatible;
                instType.Settings.compatibleTypes = instCompatible;
            }
            await Call("meshes.replaceMesh", new { mesh = copy, newMesh = rearPath });
            await Frames(2);
            Check("mesh replacement updates the collider", SelectionCollider(SelectionInstance(copy)).Shape == MeshManager.GetSelectionShape(rearPath) && SelectionHit(SelectionInstance(copy), gap) == SelectionInstance(copy));
            await Call("history.undo");
            await Frames(2);
            Check("undo restores the old mesh collider and its gap", SelectionCollider(SelectionInstance(copy)).Shape == firstShape && SelectionHit(SelectionInstance(copy), gap) == null);
            await Call("history.redo");
            await Frames(2);
            Check("redo restores the replacement collider", SelectionCollider(SelectionInstance(copy)).Shape == MeshManager.GetSelectionShape(rearPath));

            //Use an existing parametric type, but generated geometry and an isolated transformed parent.
            var paramType = TP.GetParametricMeshInstanceSettings().First();
            var parent = new Node3D { Transform = new Transform3D(new Basis(Vector3.Up, 0.6f).Scaled(new Vector3(2, 1, 0.5f)), new Vector3(1500, 80, -1400)) };
            M.AddChild(parent);
            var state = new ObjectState();
            state.SetVector3("localScale", Vector3.One);
            var parameter = new CityElements.Types.Parameter { objectInstanceSettings = new CityElements.Types.ObjectInstanceSettings { type = paramType.Key } };
            var parametric = MeshInstance.CreateParametric(frontPath, parent, false, "Selection parametric", parameter, state);
            parametric.Start();
            try {
                await Frames(2);
                Check("parametric instances share the asset collider", SelectionCollider(parametric).Shape == firstShape);
                Check("parametric collider follows its transformed parent", SelectionHit(parametric, surface, mask: -1) == parametric);
            } finally { parent.QueueFree(); }

            //Inject a non-triangle template to exercise the legacy bounds fallback without changing import formats.
            var templates = (Dictionary<string, Node3D>)typeof(MeshManager).GetField("meshes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(MeshManager.GetInstance());
            var lines = new Node3D();
            SelectionSurface(lines, new[] { -Vector3.One, Vector3.One }, primitive: Godot.Mesh.PrimitiveType.Lines);
            const string linePath = "__selection_lines";
            templates.Add(linePath, lines);
            var fallback = MeshInstance.Create(linePath, new Vector3(1470, 60, 1400), M.GetMeshContainer(), Operations.Meshes.NewSettings("instMesh"));
            fallback.Start();
            try {
                await Frames(2);
                Check("non-triangle geometry retains a selectable bounds fallback", SelectionCollider(fallback)?.Shape is BoxShape3D && SelectionHit(fallback, Vector3.Zero) == fallback);
                var shapes = (Dictionary<string, Shape3D>)typeof(MeshManager).GetField("selectionShapes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(MeshManager.GetInstance());
                Check("unsuccessful selection builds are cached", shapes.ContainsKey(linePath) && shapes[linePath] == null && MeshManager.GetSelectionShape(linePath) == null);
            } finally { fallback.QueueFree(); templates.Remove(linePath); lines.Free(); }
            var missing = (await Call("meshes.create", new { mesh = "selection_missing.obj", position = V(1490, 60, 1400), type = "instMesh" })).Value<int>();
            await Frames(2);
            Check("missing meshes keep selectable placeholder geometry", SelectionCollider(SelectionInstance(missing)).Shape is ConcavePolygonShape3D && SelectionHit(SelectionInstance(missing), Vector3.Zero) == SelectionInstance(missing));
            await Call("elements.rename", new { @ref = front, name = "Selection transformed" });
            await Call("elements.rename", new { @ref = copy, name = "Selection replaced" });
            await Call("city.save");
            await Call("city.close");
            await Call("city.load", new { folder });
            await Frames(3);
            var loaded = M.meshes.Single(m => m.Name == "Selection transformed");
            var replaced = M.meshes.Single(m => m.Name == "Selection replaced");
            Check("save/reload rebuilds accurate transformed colliders", SelectionHit(loaded, surface) == loaded && SelectionHit(replaced, gap) == replaced);
            Check("save/reload keeps the mesh instance type", loaded.TypeName == "instMesh" && GameEngine.GameObject.GetLayer(loaded) == 11);
            Check("city reload invalidates the old selection cache", SelectionCollider(loaded).Shape != firstShape && SelectionCollider(loaded).Shape == MeshManager.GetSelectionShape(frontPath));
        }

        //Opt-in, bounded A/B run: KM2_SELFTEST_MESH_SELECTION=benchmark with selftest api <new folder>.
        //Mesh loading is excluded; both cases duplicate the same warmed templates and cast identical rays.
        async Task MeshSelectionBenchmark() {
            var random = new System.Random(42);
            var assets = ((JArray)await Call("assets.list", new { kind = "mesh" })).Values<string>()
                .Where(n => File.Exists(PathHelper.FindInFolders(n))).OrderBy(n => n, System.StringComparer.Ordinal)
                .OrderBy(_ => random.Next()).Take(10).ToArray();
            Check("selection benchmark has ten local assets", assets.Length == 10);
            if (assets.Length != 10) return;
            foreach (var name in assets) {
                var warm = MeshManager.GetMesh(name, null);
                warm?.Free();
            }
            Log("BENCH assets (seed 42): " + string.Join(", ", assets));
            const int copies = 100, layer = 27;
            var rays = new List<(Vector3 origin, Vector3 direction, float distance)>();
            var shapeCache = new Dictionary<string, Shape3D>();
            var coldMs = new List<double>();
            long boxMemory = 0;
            long StaticMemory() => (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic);
            for (int mode = 0; mode < 2; mode++) {
                var triangle = mode == 1;
                var label = triangle ? "triangles" : "boxes";
                var memoryBefore = StaticMemory();
                if (triangle) {
                    foreach (var name in assets) {
                        var cold = Stopwatch.StartNew();
                        shapeCache[name] = MeshManager.GetSelectionShape(name);
                        cold.Stop();
                        coldMs.Add(cold.Elapsed.TotalMilliseconds);
                        Log("BENCH cold " + name + ": " + cold.Elapsed.TotalMilliseconds.ToString("0.000") + " ms, "
                            + ((shapeCache[name] as ConcavePolygonShape3D)?.GetFaces().Length / 3 ?? 0) + " triangles");
                    }
                }
                var bodies = new Node3D();
                M.AddChild(bodies);
                var warm = Stopwatch.StartNew();
                for (int a = 0; a < assets.Length; a++) for (int i = 0; i < copies; i++) {
                    //Every other pair overlaps vertically, alongside isolated instances and guaranteed miss rays.
                    var body = new StaticBody3D { CollisionLayer = 1u << layer, CollisionMask = 0,
                        Position = new Vector3(20000 + a * 3000 + (i / 2 % 10) * 200, 200 + i % 2 * 2, 20000 + i / 20 * 200) };
                    bodies.AddChild(body);
                    MeshManager.GetMesh(assets[a], body);
                    if (triangle && shapeCache[assets[a]] != null) GameEngine.Mesh.CreateShapeCollider(body, shapeCache[assets[a]]);
                    else {
                        var bounds = GameEngine.Mesh.GetEncapsulatingBoundsForChildMeshes(body);
                        GameEngine.Mesh.CreateBoxCollider(body, bounds.Size, bounds.GetCenter());
                    }
                }
                warm.Stop();
                await Frames(3);
                var memory = StaticMemory() - memoryBefore;
                if (!triangle) {
                    boxMemory = memory;
                    foreach (var body in bodies.GetChildren().OfType<Node3D>()) {
                        var bounds = GameEngine.Mesh.GetEncapsulatingBoundsForChildMeshes(body);
                        var center = body.GlobalTransform * bounds.GetCenter();
                        var height = Mathf.Max(100, bounds.Size.Length() * 2);
                        rays.Add((GameEngine.Vector3.EncodePosition(center + Vector3.Up * height), Vector3.Down, height * 2));
                        rays.Add((GameEngine.Vector3.EncodePosition(center + Vector3.Right * (bounds.Size.Length() + 50) + Vector3.Up * height), Vector3.Down, height * 2));
                        rays.Add((GameEngine.Vector3.EncodePosition(center + Vector3.Right * height), Vector3.Left, height * 2));
                    }
                }
                var samples = new List<double>();
                var hits = 0;
                //First pass warms query code and physics caches; five measured passes use the same rays.
                for (int pass = 0; pass < 6; pass++) foreach (var ray in rays) {
                    var start = Stopwatch.GetTimestamp();
                    var hit = GameEngine.Raycast.DoRaycast(builder.helper, ray.origin, ray.direction, out _, ray.distance, 1 << layer);
                    var elapsed = (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency;
                    if (pass > 0) { samples.Add(elapsed); if (hit) hits++; }
                }
                samples.Sort();
                Log("BENCH " + label + ": warm 1000 instances " + warm.Elapsed.TotalMilliseconds.ToString("0.00")
                    + " ms; ray median/p95 " + samples[samples.Count / 2].ToString("0.000") + "/" + samples[(int)(samples.Count * 0.95)].ToString("0.000")
                    + " us; hits " + hits + "/" + samples.Count + "; Godot static memory delta " + memory + " bytes");
                if (triangle) {
                    Log("BENCH cold shape total " + coldMs.Sum().ToString("0.000") + " ms; triangle-minus-box static memory " + (memory - boxMemory) + " bytes (allocator counter, not total process memory)");
                    foreach (var group in GameEngine.Scriptable.FindMultipleRecursive<LODGroup>(bodies)) group.UpdateLod(new LODUpdater.CameraData { orthogonal = true, orthoHeight = 1000000 });
                    Check("benchmark copies and LOD changes retain cached shapes", assets.All(n => shapeCache[n] != null && shapeCache[n] == MeshManager.GetSelectionShape(n))
                        && bodies.GetChildren().OfType<StaticBody3D>().All(b => b.GetChildren().OfType<CollisionShape3D>().Single().Shape == shapeCache[assets[(int)((b.Position.X - 20000) / 3000)]]));
                }
                bodies.QueueFree();
                await Frames(3);
            }
        }
    }
}

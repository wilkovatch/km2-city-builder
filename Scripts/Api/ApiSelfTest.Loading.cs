using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Lua;
using RuntimeCalculator.Scripting;
using CT = CityElements.Types;

namespace Api {
    public partial class ApiSelfTest {
        async Task LazyEditorInitialization() {
            EditorPanel Panel(string name) => (EditorPanel)typeof(CityBuilderMenuBar).GetField(name,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(builder);
            var roadPanel = Panel("roadEditorPanel");
            var heightPanel = Panel("heightmapEditorPanel");
            var projectPanel = Panel("projectSettingsEditorPanel");
            var types = CT.Parsers.TypeParser.GetCustomElementTypes();
            var scripts = LuaScriptHost.Get();
            var parametric = CT.Parsers.TypeParser.GetParametricMeshInstanceSettings();
            var custom = CT.Parsers.TypeParser.GetCustomCityPropertiesTypes();
            Check("city loading leaves hidden editor panels unbuilt", !roadPanel.IsInitialized
                && !heightPanel.IsInitialized && !projectPanel.IsInitialized);
            Check("parametric mesh and custom city properties types are cached", object.ReferenceEquals(parametric, CT.Parsers.TypeParser.GetParametricMeshInstanceSettings())
                && object.ReferenceEquals(custom, CT.Parsers.TypeParser.GetCustomCityPropertiesTypes()));
            builder.Reload();
            Check("refreshing the UI preserves the runtime types and Lua state", object.ReferenceEquals(types, CT.Parsers.TypeParser.GetCustomElementTypes())
                && object.ReferenceEquals(scripts, LuaScriptHost.Get()));
            heightPanel.SetActive(true);
            Check("a panel can be opened for the first time after loading", heightPanel.IsInitialized && heightPanel.ActiveSelf());
            builder.CloseAllPanels();
            Check("closing a panel leaves other hidden panels unbuilt", !heightPanel.ActiveSelf() && !roadPanel.IsInitialized);
            builder.Reload();
            Check("refreshing the UI discards old controls without rebuilding them", !heightPanel.IsInitialized);
            heightPanel.SetActive(true);
            Check("an invalidated panel builds again when opened", heightPanel.IsInitialized && heightPanel.ActiveSelf());
            builder.CloseAllPanels();

            var core = PreferencesManager.Get("core", "");
            try {
                CT.Parsers.TypeInheritance.SetAncestries("cacheTest", new Dictionary<string, string[]> { { "child", new[] { "child", "parent" } } });
                PreferencesManager.Set("core", "__cacheTestMissingCore");
                var other = CT.Parsers.TypeParser.GetCustomElementTypes(); //custom element folders are optional
                Check("changing core clears the old type and ancestry caches", other.Count == 1 && other.ContainsKey(PropPath.TypeId)
                    && CT.Parsers.TypeInheritance.GetAllAncestries().All(x => x.category != "cacheTest"));
            } finally {
                PreferencesManager.Set("core", core);
            }
            var restored = CT.Parsers.TypeParser.GetCustomElementTypes();
            Check("returning to the original core reads fresh types", !object.ReferenceEquals(types, restored) && types.Keys.OrderBy(x => x).SequenceEqual(restored.Keys.OrderBy(x => x)));
            var host = LuaScriptHost.Get();
            await Call("city.reload");
            Check("reloading the city invalidates its types and scripts", !object.ReferenceEquals(restored, CT.Parsers.TypeParser.GetCustomElementTypes())
                && !object.ReferenceEquals(host, LuaScriptHost.Get()) && !roadPanel.IsInitialized);
        }

        void ParallelLuaGeometry() {
            var dir = folder + "_parallel_lua";
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "batch.lua"), @"
local M = {}
function M.geometry(v, ctx)
    local a = ctx.p + vec3(v.offset, 0, 0)
    local b = a + vec3(1, 0, 0)
    local c = a + vec3(0, 0, 1)
    local n = (b-a):cross(c-a):normalize()
    assert(n.y == -1 and (b-a):dot(c-a) == 0)
    assert((a * 2 / 2).x == a.x and (-vec3(1,2,3)).z == -3)
    local uv = vec2(v.offset, 2)
    assert(uv.x == v.offset and (uv + vec2(1,1)).y == 3)
    assert(uv:scale(vec2(2,3)).y == 6 and uv:min(vec2(0,1)).y == 1)
    assert(uv:max(vec2(0,7)).y == 7 and uv:lerp(vec2(0,4),0.5).y == 3)
    assert(vec2(1,-1):reflect(vec2(0,1)).y == 1)
    print('geometry', v.offset)
    return {meshes={{vertices={a,b,c}, triangles={0,1,2}, uvs={uv,uv,uv}}}}
end
function M.random(v, ctx) return {uvs={vec2(rnd(0,1,19472831),0)}} end
function M.bad(v, ctx) return {meshes={{vertices={ctx.p}, triangles={0,1,2}}}} end
function M.fail(v, ctx) error('expected worker error') end
return M
");
            using var main = LuaScriptHost.CreateForFolder(dir);
            CT.Runtime.CustomElementType Type(string function, CT.Definition[] definitions = null) {
                return new CT.Runtime.CustomElementType(new CT.CustomElementType {
                    parameters = new CT.ParameterContainer { staticDefinitions = definitions },
                    settings = new CT.CustomElementSettings { geometry = "batch." + function }
                }, "batch", main);
            }
            var type = Type("geometry");
            CustomElement.GeometryUpdate Job(CT.Runtime.CustomElementType t, int offset) {
                return new CustomElement.GeometryUpdate {
                    type = t, variables = new LuaTable { ["offset"] = offset },
                    context = new LuaTable { ["p"] = LuaVectors.ToLua(new Vector3(3, 4, 5)) }
                };
            }
            var jobs = Enumerable.Range(0, 256).Select(i => Job(type, i)).ToArray();
            //cold vector bindings and modules, several batches per isolated state: catches shared metatable writes
            Parallel.For(0, 4, worker => {
                using var host = LuaScriptHost.CreateWorker(dir);
                for (int i = worker; i < jobs.Length; i += 4) jobs[i].Calculate(host);
            });
            Check("parallel Lua: independent geometry, vectors and variables for 256 jobs", jobs.Select((j, i) =>
                !j.retryOnMainThread && j.errors.Count == 0 && j.geometry?.meshes.Count == 1
                && j.geometry.meshes[0].vertices[0] == new Vector3(3 + i, 4, 5)
                && j.geometry.meshes[0].uvs[0] == new Vector2(i, 2)).All(x => x));
            Check("parallel Lua: print messages stay with their element", jobs.Select((j, i) => j.prints.Count == 1
                && j.prints[0] == "[lua] geometry\t" + i).All(x => x));

            using var retryHost = LuaScriptHost.CreateWorker(dir);
            var random = Job(Type("random"), 0);
            var oldSeed = RandomManager.rnds.TryGetValue(19472831, out var oldRandom);
            RandomManager.rnds.Remove(19472831);
            random.Calculate(retryHost);
            Check("parallel Lua: rnd retries on the main thread without advancing the random sequence",
                random.retryOnMainThread && !RandomManager.rnds.ContainsKey(19472831));
            if (oldSeed) RandomManager.rnds[19472831] = oldRandom;
            var malformed = Job(Type("bad"), 0);
            malformed.Calculate(retryHost);
            Check("parallel Lua: malformed geometry is buffered for the main thread", malformed.geometry == null && malformed.errors.Count == 1);
            var failed = Job(Type("fail"), 0);
            failed.Calculate(retryHost);
            Check("parallel Lua: runtime errors are buffered for the main thread", failed.errors.Count == 1 && main.ErrorCount == 0);
            var after = Job(type, 7);
            after.Calculate(retryHost);
            Check("parallel Lua: the host works after a failed job", after.errors.Count == 0 && after.geometry.meshes.Count == 1);
            Check("parallel Lua: random expressions keep preparation in element order",
                !Type("geometry", new[] { new CT.Definition { name = "x", type = "float", value = "rnd(0,1,3)" } }).canBatchGeometryPreparation);
        }

        void DeferredPointVisuals() {
            var parent = GameEngine.GameObject.Create("Deferred point test");
            GameEngine.GameObject.SetParent(parent, M.GetTerrainPointContainer());
            var point = TerrainPoint.Create(new Vector3(17, 3, 29), null, parent, active: false);
            var handle = (Handle)point.GetObject();
            try {
                point.Select(false);
                point.SetMoveable(true, true);
                Check("hidden points need no mesh or collision shape", handle.deferredVisuals
                    && GameEngine.Scriptable.FindRecursive<MeshInstance3D>(handle) == null
                    && GameEngine.Scriptable.FindRecursive<CollisionShape3D>(handle) == null);
                point.SetActive(true);
                var mesh = GameEngine.Scriptable.FindRecursive<MeshInstance3D>(handle);
                var shape = GameEngine.Scriptable.FindRecursive<CollisionShape3D>(handle);
                Check("showing a deferred point creates its mesh and collider", !handle.deferredVisuals && mesh != null
                    && shape?.Shape != null && !shape.Disabled && point.GetPoint() == new Vector3(17, 3, 29));
                Check("deferred visuals inherit the movement layer and saved color", mesh.Layers == handle.CollisionLayer
                    && mesh.MaterialOverride == MaterialManager.GetHandleMaterial(GameEngine.Color.green));
                point.SetActive(false);
                point.SetActive(true);
                Check("showing the point again reuses its visual nodes", mesh == GameEngine.Scriptable.FindRecursive<MeshInstance3D>(handle)
                    && shape == GameEngine.Scriptable.FindRecursive<CollisionShape3D>(handle));
            } finally {
                point.Delete();
                GameEngine.GameObject.Destroy(parent);
            }
        }

        void DirectColliderGeometry() {
            var body = GameEngine.GameObject.CreateStaticBody("Direct collider test");
            using var mesh = new ArrayMesh();
            var vertices = new List<Vector3> { new Vector3(2, 3, 5), new Vector3(7, 3, 5), new Vector3(2, 3, 11) };
            var indices = new List<int> { 0, 1, 2, 0, 0, 0 };
            try {
                GameEngine.Mesh.AssignData(mesh, indices, vertices);
                var old = GameEngine.Mesh.CreateCollider(body, mesh);
                var direct = GameEngine.Mesh.CreateShapeCollider(body, null);
                GameEngine.Mesh.RefreshCollider(direct, vertices, indices);
                var expected = (ConcavePolygonShape3D)old.Shape;
                var actual = (ConcavePolygonShape3D)direct.Shape;
                Check("direct collider faces match mesh readback, including winding and Z conversion",
                    actual.BackfaceCollision && actual.GetFaces().SequenceEqual(expected.GetFaces()));
                GameEngine.Mesh.RefreshCollider(direct, vertices, new int[0]);
                Check("an empty direct collider is disabled and releases its shape", direct.Disabled && direct.Shape == null);
            } finally {
                GameEngine.GameObject.Destroy(body);
            }
        }
    }
}

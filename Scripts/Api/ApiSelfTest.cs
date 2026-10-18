using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;

namespace Api {
    //Development self-test of the scripting API, run with "-- --api-selftest=<new folder>" on the command line. Creates a city
    //in the folder and drives it only with JSON commands sent to the dispatcher (as a script would): creation and editing
    //of every kind of element, undo and redo, batches, errors, parity with the editor, save and load, export.
    //Writes api_selftest.log next to the folder and quits (exit code 1 on failures).
    public partial class ApiSelfTest : Node {
        CityBuilderMenuBar builder;
        string folder;
        readonly List<string> log = new List<string>();
        int failures = 0;
        int nextId = 0;
        int progressEvents = 0;
        ElementManager M => builder.helper.elementManager;

        class Abort : System.Exception {
            public Abort(string message) : base(message) { }
        }

        public static void StartIfRequested(CityBuilderMenuBar builder) {
            foreach (var arg in OS.GetCmdlineUserArgs()) {
                if (arg.StartsWith("--api-selftest=")) {
                    var test = new ApiSelfTest { builder = builder, folder = arg.Substring("--api-selftest=".Length), Name = "ApiSelfTest" };
                    builder.AddChild(test);
                }
            }
        }

        public override void _Ready() {
            Run();
        }

        void Log(string line) {
            log.Add(line);
            GD.Print("[api-selftest] " + line);
        }

        void Check(string what, bool ok) {
            Log((ok ? "OK   " : "FAIL ") + what);
            if (!ok) failures++;
        }

        async Task Frames(int n) {
            for (int i = 0; i < n; i++) await ToSignal(GetTree(), "process_frame");
        }

        //sends a command through the queue of the dispatcher and waits for the response
        async Task<JObject> Send(string method, object prms) {
            var id = ++nextId;
            var request = new JObject { ["id"] = id, ["method"] = method, ["params"] = prms == null ? new JObject() : Json.ToToken(prms) };
            var tcs = new TaskCompletionSource<JObject>();
            ApiHost.Instance.Dispatcher.Enqueue(request.ToString(Newtonsoft.Json.Formatting.None), reply => {
                var o = JObject.Parse(reply);
                if (o["event"] != null) progressEvents++;
                else tcs.TrySetResult(o);
            });
            var res = await tcs.Task;
            if (res["id"]?.Value<int>() != id) Check(method + ": response id", false);
            return res;
        }

        async Task<JToken> Call(string method, object prms = null) {
            var res = await Send(method, prms);
            if (res["error"] != null) {
                Log("FAIL " + method + " " + Json.ToToken(prms) + " -> " + res["error"]);
                failures++;
                throw new Abort(method + " failed");
            }
            return res["result"];
        }

        async Task Expect(string what, string code, string method, object prms = null) {
            var res = await Send(method, prms);
            var actual = res["error"]?["code"]?.Value<string>();
            Check(what + " fails with " + code + (actual != code ? " (got " + (actual ?? "success") + ")" : ""), actual == code);
        }

        static int Len(JToken t) {
            return t is JArray a ? a.Count : -1;
        }

        static float[] V(float x, float y, float z) {
            return new[] { x, y, z };
        }

        static object Pos(float x, float z) {
            return new { position = V(x, 0, z), onGround = true };
        }

        // Snapshots of the whole city (through the API), compared with a tolerance on the numbers

        async Task<JObject> Snapshot() {
            var res = new JObject();
            foreach (var e in (JArray)await Call("elements.list")) {
                var id = e["id"].Value<int>();
                res[id.ToString()] = (await Call("elements.get", new { @ref = id }))["data"];
            }
            foreach (var e in (JArray)await Call("elements.list", new { kind = "terrainPoint" })) {
                var id = e["id"].Value<int>();
                res[id.ToString()] = (await Call("elements.get", new { @ref = id }))["data"];
            }
            return res;
        }

        static bool SameToken(JToken a, JToken b, string path, List<string> diffs) {
            //empty arrays are not kept when a state is read from JSON
            bool IsEmpty(JToken t) => t == null || (t is JArray arr && arr.Count == 0);
            if (a == null || b == null) {
                if (IsEmpty(a) && IsEmpty(b)) return true;
                if (a != b) diffs.Add(path);
                return a == b;
            }
            if ((a.Type == JTokenType.Float || a.Type == JTokenType.Integer) && (b.Type == JTokenType.Float || b.Type == JTokenType.Integer)) {
                var ok = System.Math.Abs(a.Value<double>() - b.Value<double>()) < 0.05;
                if (!ok) diffs.Add(path + ": " + a + " != " + b);
                return ok;
            }
            if (a.Type != b.Type) {
                diffs.Add(path + ": " + a.Type + " != " + b.Type);
                return false;
            }
            if (a is JObject oa) {
                var ob = (JObject)b;
                var ok = true;
                foreach (var p in oa.Properties()) ok &= SameToken(p.Value, ob[p.Name], path + "." + p.Name, diffs);
                foreach (var p in ob.Properties()) if (oa[p.Name] == null && !IsEmpty(p.Value)) { diffs.Add(path + "." + p.Name + " added"); ok = false; }
                return ok;
            }
            if (a is JArray aa) {
                var ab = (JArray)b;
                if (aa.Count != ab.Count) {
                    diffs.Add(path + ": length " + aa.Count + " != " + ab.Count);
                    return false;
                }
                var ok = true;
                for (int i = 0; i < aa.Count; i++) ok &= SameToken(aa[i], ab[i], path + "[" + i + "]", diffs);
                return ok;
            }
            var same = JToken.DeepEquals(a, b);
            if (!same) diffs.Add(path + ": " + a + " != " + b);
            return same;
        }

        bool Same(string what, JToken a, JToken b) {
            var diffs = new List<string>();
            var ok = SameToken(a, b, "", diffs);
            Check(what, ok);
            for (int i = 0; i < diffs.Count && i < 8; i++) Log("      " + diffs[i]);
            return ok;
        }

        //makes a change, then checks that undo restores the city as before it and redo as after it
        async Task CheckUndo(string what, System.Func<Task> change) {
            var before = await Snapshot();
            await change();
            var after = await Snapshot();
            var top = DebugTopEntry();
            await Call("history.undo");
            if (!Same("undo " + what, before, await Snapshot())) Log("      DEBUG entry: " + top);
            await Call("history.redo");
            Same("redo " + what, after, await Snapshot());
        }

        static string DebugTopEntry() {
            var stack = (System.Collections.IList)typeof(History.UndoManager).GetField("undoStack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            if (stack.Count == 0) return "(none)";
            var entry = stack[stack.Count - 1];
            var elements = (System.Collections.IDictionary)entry.GetType().GetField("elements").GetValue(entry);
            var res = "";
            foreach (var change in elements.Values) {
                var before = change.GetType().GetField("before").GetValue(change);
                var after = change.GetType().GetField("after").GetValue(change);
                string J(object m) => m == null ? "(none)" : (string)m.GetType().GetProperty("Json").GetValue(m);
                res += "\n        " + change.GetType().GetField("id").GetValue(change) + " before " + J(before) + "\n        after " + J(after);
            }
            return res;
        }

        T CallBuilder<T>(string method, params object[] args) {
            foreach (var m in typeof(CityBuilderMenuBar).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
                if (m.Name == method && m.GetParameters().Length == args.Length) return (T)m.Invoke(builder, args);
            }
            throw new System.MissingMethodException(method);
        }

        async void Run() {
            var lastCity = SettingsManager.Get("LastCity", "");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try {
                await Frames(10);
                await Basics();
                await NewCity();
                await Roads();
                await TerrainAndBuildings();
                await Anchors();
                await Heights();
                await States();
                await Props();
                await Meshes();
                await Batches();
                await Transactions();
                await Errors();
                await EditorCoordination();
                await Parity();
                await WholeCity();
                await ScriptedGenerators();
                await PythonScripts();
                await Files();
            } catch (Abort e) {
                Log("aborted: " + e.Message);
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            SettingsManager.Set("LastCity", lastCity);
            Log("finished in " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "api_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }

        async Task Basics() {
            var version = await Call("api.version");
            Check("api.version", version["protocol"].Value<int>() == CityApi.ProtocolVersion);
            var methods = (JArray)await Call("api.methods");
            Check("api.methods lists the methods (" + methods.Count + ")", methods.Count > 60);
            await Expect("unknown method", "unknown_method", "roads.fly");
            await Expect("command without a city", "no_city", "elements.list");
            var cores = (JArray)await Call("cores.list");
            Check("cores.list has MidtownMadness2", cores.ToString().Contains("MidtownMadness2"));
            var templates = (JArray)await Call("cores.templates", new { core = "MidtownMadness2" });
            Check("cores.templates has SF", templates.ToString().Contains("\"SF\""));
            var invalid = await ApiHost.Instance.Dispatcher.Execute("{not json");
            Check("invalid JSON is an error", invalid["error"]?["code"]?.Value<string>() == "invalid_request");

            //a script on another thread waiting for each reply runs several commands per frame
            var frames = Engine.GetProcessFrames();
            const int count = 60;
            var replies = await Task.Run(() => {
                var n = 0;
                for (int i = 0; i < count; i++) {
                    var done = new System.Threading.ManualResetEventSlim();
                    ApiHost.Instance.Dispatcher.Enqueue("{\"id\": " + i + ", \"method\": \"api.version\"}", reply => {
                        if (reply.Contains("\"result\"")) n++;
                        done.Set();
                    });
                    if (!done.Wait(10000)) break;
                }
                return n;
            });
            var elapsed = Engine.GetProcessFrames() - frames;
            Check("commands from another thread run several per frame (" + count + " in " + elapsed + " frames)", replies == count && elapsed < count / 2);
        }

        async Task NewCity() {
            await Expect("city.new with an unknown template", "not_found", "city.new", new { folder, core = "MidtownMadness2", template = "Atlantis" });
            var info = await Call("city.new", new { folder, core = "MidtownMadness2", template = "SF", terrainSize = 2048, heightmapResolution = 512, maxHeight = 1000 });
            Check("city.new loads the city", info["loaded"].Value<bool>() && info["terrainSize"].Value<int>() == 2048);
            Check("progress events while loading (" + progressEvents + ")", progressEvents > 0);
            Check("the history is empty after loading", !(await Call("history.state"))["canUndo"].Value<bool>());
            await Expect("city.new on an existing city", "already_exists", "city.new", new { folder, core = "MidtownMadness2", template = "SF" });
        }

        int roadA, roadB, roadC, roadD, intersection, patch, line, mesh;

        async Task<JToken> Derived(int id) {
            return (await Call("elements.get", new { @ref = id, derived = true }))["derived"];
        }

        async Task Roads() {
            var types = (JArray)await Call("types.list", new { kind = "road" });
            Check("types.list road has types", types.Count > 0 && types[0]["builtins"].ToString().Contains("curveType"));
            roadA = (await Call("roads.create", new { points = new[] { Pos(-600, 0), Pos(-300, 0), Pos(0, 10) } })).Value<int>();
            var a = await Derived(roadA);
            Check("road A has its points and curve", Len(a["points"]) == 3 && Len(a["curve"]) > 3);
            roadB = (await Call("roads.create", new { points = new object[] { Pos(0, 300), Pos(5, 150), new { roadEnd = new { road = roadA, end = "end" } } } })).Value<int>();
            a = await Derived(roadA);
            var b = await Derived(roadB);
            Check("road B ends at an intersection with road A", a["endIntersection"].Type != JTokenType.Null && a["endIntersection"].Value<int>() == b["endIntersection"].Value<int>());
            intersection = b["endIntersection"].Value<int>();
            roadC = (await Call("roads.create", new { points = new object[] { Pos(300, 0), Pos(150, 5), new { intersection } } })).Value<int>();
            var i = await Derived(intersection);
            Check("the intersection has three roads", Len(i["roadsClockwise"]) == 3);
            var roadsBefore = ((JArray)await Call("elements.list", new { kind = "road" })).Count;
            roadD = (await Call("roads.create", new { points = new object[] { Pos(-300, -400), Pos(-310, -200), new { roadMiddle = new { road = roadA, near = V(-300, 0, 0) } } } })).Value<int>();
            Check("connecting to the middle of road A splits it", ((JArray)await Call("elements.list", new { kind = "road" })).Count == roadsBefore + 2
                && ((JArray)await Call("elements.list", new { kind = "intersection" })).Count == 2);
            await Expect("a road can't continue after its end intersection", "invalid_operation", "roads.extend", new { road = roadD, point = Pos(0, 0) });
            await Expect("a road needs two points", "invalid_param", "roads.create", new { points = new[] { Pos(0, 0) } });

            await CheckUndo("road point moved", () => Call("roads.movePoint", new { road = roadC, index = 0, position = V(320, 0, 20), onGround = true }));
            await CheckUndo("road point inserted", () => Call("roads.insertPoint", new { road = roadC, index = 1, position = V(250, 0, 0), onGround = true }));
            await CheckUndo("road point removed", () => Call("roads.removePoint", new { road = roadC, index = 1 }));
            await CheckUndo("intersection moved", () => Call("intersections.move", new { intersection, position = V(10, 0, 10), onGround = true }));
            var e = await Call("roads.create", new { points = new[] { Pos(-800, 400), Pos(-700, 400), Pos(-600, 400) } });
            var roadE = e.Value<int>();
            await Call("roads.extend", new { road = roadE, point = Pos(-500, 450) });
            Check("roads.extend adds a point", Len((await Derived(roadE))["points"]) == 4);
            var removed = await Call("roads.removePoint", new { road = roadE });
            Check("removing the last point keeps the road", !removed["deleted"].Value<bool>());
            await CheckUndo("road deleted", () => Call("roads.delete", new { road = roadE }));
            await Call("elements.rename", new { @ref = roadA, name = "Main street" });
            Check("elements.list by name", ((JArray)await Call("elements.list", new { name = "Main street" })).Count == 1);
            Check("reference by name", (await Call("elements.get", new { @ref = "Main street" }))["id"].Value<int>() == roadA);
        }

        async Task TerrainAndBuildings() {
            var a = await Derived(roadA);
            Check("road A has anchors", Len(a["anchors"]) > 0);
            patch = (await Call("patches.create", new {
                perimeter = new object[] { new { anchor = new { of = roadA, index = 0 } }, Pos(-600, -100), Pos(-700, -100), Pos(-700, 0) },
                internalPoints = new[] { Pos(-650, -50) }
            })).Value<int>();
            var p = await Derived(patch);
            Check("the terrain patch has 4 perimeter points and 1 internal one", Len(p["perimeter"]) == 4 && Len(p["internalPoints"]) == 1);
            var pointIds = new List<int>();
            foreach (var point in p["perimeter"]) pointIds.Add(point["id"].Value<int>());
            await CheckUndo("terrain patch point added", () => Call("patches.addPerimeterPoint", new { patch, point = Pos(-720, 50) }));
            var bm = (await Call("patches.borderMesh.add", new { patch })).Value<int>();
            await Call("patches.borderMesh.addPoint", new { patch, borderMesh = bm, point = pointIds[1] });
            await Call("patches.borderMesh.addPoint", new { patch, borderMesh = bm, point = pointIds[2] });
            p = await Derived(patch);
            Check("the border mesh has a segment", Len(p["borderMeshes"][0]["points"]) >= 2);
            await Expect("a point not on the perimeter", "not_found", "patches.borderMesh.addPoint", new { patch, borderMesh = bm, point = 999999 });
            var bmRef = new { patch, borderMesh = bm };
            var bmSchema = await Call("state.schema", new { @ref = bmRef });
            var bmType = (await Call("state.get", new { @ref = bmRef }))["type"].Value<string>();
            var otherType = bmSchema["types"].ToObject<List<string>>().Find(t => t != bmType);
            if (otherType != null) {
                await CheckUndo("border mesh type", () => Call("state.setType", new { @ref = bmRef, type = otherType }));
            } else {
                Log("     only one border mesh type");
            }
            await CheckUndo("border mesh preset", () => Call("state.setParam", new { @ref = bmRef, name = ((JArray)bmSchema["parameters"]).First(x => x["type"].Value<string>() == "float")["name"].Value<string>(), value = 7.5 }));
            var linkPatch = (await Call("patches.create", new {
                perimeter = new object[] { new { terrainPoint = pointIds[1] }, new { link = new[] { pointIds[1], pointIds[2] }, position = V(-650, 0, -100) }, Pos(-650, -200) }
            })).Value<int>();
            Check("a terrain patch sharing points and on a link", Len((await Derived(linkPatch))["perimeter"]) == 3);
            var del = await Call("patches.removeLastPerimeterPoint", new { patch = linkPatch });
            Check("a terrain patch left with two points is deleted", del["deleted"].Value<bool>());

            line = (await Call("lines.create", new { points = new[] { Pos(-600, 200), Pos(-560, 200), Pos(-520, 200) }, dividing = new[] { 1 } })).Value<int>();
            var l = await Derived(line);
            Check("the building line has two buildings", Len(l["buildings"]) == 2);
            var middle = l["points"][1]["id"].Value<int>();
            await CheckUndo("dividing point switched", () => Call("lines.toggleDividing", new { line, point = middle }));
            Check("toggling the dividing point merges the buildings", Len((await Derived(line))["buildings"]) == 1);
            var side = new { line, building = 0, side = "front" };
            await CheckUndo("building side floor added", () => Call("sides.addFloor", new { side }));
            await Call("sides.addSlice", new { side, floor = 0 });
            await Call("sides.addSlice", new { side });
            await Call("sides.addBlock", new { side });
            await Call("state.setBuiltin", new { @ref = side, name = "upperFloors.0.height", value = 3.5 });
            Check("a nested built-in property", (await Call("state.getParam", new { @ref = side, name = "upperFloors.0.height" })).Value<float>() == 3.5f);
            await Expect("a missing floor", "not_found", "state.setBuiltin", new { @ref = side, name = "upperFloors.5.height", value = 1 });
            await Call("state.setBuiltin", new { @ref = new { line, building = 0 }, name = "height", value = 12.0 });
            await Call("buildings.setAllSidesEqual", new { building = new { line, building = 0 }, equal = true });
            var blockTypes = (JArray)await Call("types.list", new { kind = "buildingBlock" });
            await Call("state.setType", new { @ref = new { line, building = 0, side = "front", block = 0 }, type = blockTypes[0]["name"].Value<string>() });
        }

        async Task States() {
            var schema = await Call("state.schema", new { @ref = roadC });
            JToken floatParam = null;
            foreach (var param in schema["parameters"]) {
                if (param["type"].Value<string>() == "float" && !param["instanceSpecific"].Value<bool>()) { floatParam = param; break; }
            }
            if (floatParam != null) {
                var name = floatParam["name"].Value<string>();
                await CheckUndo("road parameter " + name, () => Call("state.setParam", new { @ref = roadC, name, value = 1.25 }));
                Check("state.getParam reads it", (await Call("state.getParam", new { @ref = roadC, name })).Value<float>() == 1.25f);
            } else {
                Log("     no float parameter for the road type");
            }
            await CheckUndo("road curve type", () => Call("state.setBuiltin", new { @ref = roadC, name = "curveType", value = "hermite" }));
            await Call("state.setBuiltin", new { @ref = roadC, name = "segments", value = 7 });
            await Call("state.setBuiltin", new { @ref = roadC, name = "endIntersectionEnd", value = true });
            var st = await Call("state.get", new { @ref = roadC });
            Check("instance specific built-in in the instance state", st["instanceState"]?["endIntersectionEnd"]?.Value<bool>() == true && st["state"]["endIntersectionEnd"] == null);
            await Expect("invalid enum value", "invalid_value", "state.setBuiltin", new { @ref = roadC, name = "curveType", value = "spiral" });
            await Expect("wrong value type", "invalid_value", "state.setBuiltin", new { @ref = roadC, name = "segments", value = "many" });
            var types = schema["types"].ToObject<List<string>>();
            if (types.Count > 1) {
                var cur = (await Call("state.get", new { @ref = roadC }))["type"].Value<string>();
                var other = types.Find(t => t != cur);
                await CheckUndo("road type", () => Call("state.setType", new { @ref = roadC, type = other }));
                await Call("history.undo");
            }
            var presets = (JArray)await Call("presets.list", new { category = "road" });
            await Call("state.saveAsPreset", new { @ref = roadC, name = "api test road" });
            Check("state.saveAsPreset adds a preset", ((JArray)await Call("presets.list", new { category = "road" })).Count == presets.Count + 1);
            await CheckUndo("road preset", () => Call("state.applyPreset", new { @ref = roadB, name = "api test road" }));
            await Call("presets.delete", new { category = "road", name = "api test road" });
            await Expect("the default preset can't be deleted", "invalid_operation", "presets.delete", new { category = "road", name = "Default" });

            //city properties: array and custom element parameters
            var cityId = 0;
            var citySchema = await Call("state.schema", new { @ref = cityId });
            string arrayParam = null, customParam = null;
            foreach (var param in citySchema["parameters"]) {
                if (param["type"].Value<string>() == "array" && arrayParam == null) arrayParam = param["name"].Value<string>();
                if (param["type"].Value<string>() == "customElement" && customParam == null) customParam = param["name"].Value<string>();
            }
            if (arrayParam != null) {
                await CheckUndo("array element added", () => Call("state.array.add", new { @ref = cityId, param = arrayParam }));
                var clone = (await Call("state.array.clone", new { @ref = cityId, param = arrayParam, index = 0 })).Value<int>();
                Check("state.array.clone adds at the end", clone == 1);
                var elemSchema = await Call("state.schema", new { @ref = new { parent = cityId, param = arrayParam, index = 0 } });
                foreach (var param in elemSchema["parameters"]) {
                    var type = param["type"].Value<string>();
                    if (type == "string" || type == "mesh") {
                        var name = param["name"].Value<string>();
                        await CheckUndo("array element parameter", () => Call("state.setParam", new { @ref = new { parent = cityId, param = arrayParam, index = 0 }, name, value = "api" }));
                        await Call("state.array.move", new { @ref = cityId, param = arrayParam, index = 0, direction = "down" });
                        Check("state.array.move swaps the elements", (await Call("state.getParam", new { @ref = new { parent = cityId, param = arrayParam, index = 1 }, name })).Value<string>() == "api");
                        break;
                    }
                }
                await Call("state.array.remove", new { @ref = cityId, param = arrayParam, index = 1 });
                await Expect("an array element out of range", "not_found", "state.array.remove", new { @ref = cityId, param = arrayParam, index = 5 });
            }
            if (customParam != null) {
                await Call("state.customElement.ensure", new { @ref = cityId, param = customParam });
                await Call("state.get", new { @ref = new { parent = cityId, param = customParam } });
                Check("custom element reference", true);
            }
        }

        async Task Props() {
            var containers = (await Derived(roadB))["props"].ToObject<List<string>>();
            if (containers.Count == 0) {
                Log("     no props containers for this road type");
                return;
            }
            var container = containers[0];
            await CheckUndo("props container enabled", () => Call("roads.props.enable", new { road = roadB, container, enabled = true }));
            var containerRef = new { road = roadB, container };
            var containerType = (await Call("state.get", new { @ref = containerRef }))["type"].Value<string>();
            JToken lists = null;
            foreach (var t in (JArray)await Call("types.list", new { kind = "propContainer" })) if (t["name"].Value<string>() == containerType) lists = t["lists"];
            var list = lists[0]["name"].Value<string>();
            var index = (await Call("props.elements.add", new { container = containerRef, list })).Value<int>();
            var elemRef = index >= 0 ? (object)new { road = roadB, container, list, index } : new { road = roadB, container, list };
            await Call("state.get", new { @ref = elemRef });
            await CheckUndo("props element mesh", () => Call("props.element.meshes.add", new { element = elemRef, mesh = "" }));
            Check("props element reference", true);
        }

        async Task Meshes() {
            var assets = (JArray)await Call("assets.list", new { kind = "mesh" });
            Log("     " + assets.Count + " meshes available");
            var meshName = assets.Count > 0 ? assets[0].Value<string>() : "missing.obj";
            await Expect("a mesh instance without mesh", "invalid_param", "meshes.create", new { mesh = "", position = V(0, 0, 0) });
            mesh = (await Call("meshes.create", new { mesh = meshName, position = V(-800, 0, -300), onGround = true })).Value<int>();
            var d = await Derived(mesh);
            Check("meshes.create places a mesh instance", d["mesh"].Value<string>() == meshName);
            await CheckUndo("mesh moved", () => Call("meshes.setTransform", new { mesh, position = V(-810, 5, -300), rotation = V(0, 45, 0), scale = V(2, 2, 2) }));
            await Call("meshes.projectToGround", new { mesh });
            var scattered = (JArray)await Call("meshes.scatter", new { mesh = meshName, center = V(-800, 0, 300), radius = 20, count = 3 });
            Check("meshes.scatter places 3 instances", scattered.Count == 3);
            var erased = (await Call("meshes.eraseInRadius", new { center = V(-800, 0, 300), radius = 25 })).Value<int>();
            Check("meshes.eraseInRadius deletes them (" + erased + ")", erased == 3);

            //parametric objects
            foreach (var r in (JArray)await Call("elements.list", new { kind = "road" })) {
                var objects = (await Derived(r["id"].Value<int>()))["parametricObjects"].ToObject<List<string>>();
                if (objects.Count == 0) continue;
                var objRef = new { owner = r["id"].Value<int>(), @object = objects[0] };
                var obj = await Call("objects.get", new { @object = objRef });
                Check("objects.get reads a parametric object", obj["mesh"] != null);
                await Expect("a generated parametric object can't be deleted", "invalid_operation", "objects.delete", new { @object = objRef });
                break;
            }
        }

        async Task Batches() {
            var before = await Snapshot();
            var res = await Send("batch", new {
                commands = new object[] {
                    new { method = "roads.movePoint", @params = new { road = roadC, index = 0, position = V(330, 0, 40), onGround = true } },
                    new { method = "roads.delete", @params = new { road = 999999 } }
                },
                atomic = true
            });
            Check("a failing batch reports the failed command", res["error"]?["code"]?.Value<string>() == "not_found" && res["error"]["details"]["index"].Value<int>() == 1);
            Same("an atomic batch that fails changes nothing", before, await Snapshot());
            var ok = await Call("batch", new {
                commands = new object[] {
                    new { method = "roads.movePoint", @params = new { road = roadC, index = 0, position = V(330, 0, 40), onGround = true } },
                    new { method = "state.setBuiltin", @params = new { @ref = roadC, name = "segments", value = 9 } }
                }
            });
            Check("a batch returns the results", ((JArray)ok).Count == 2);
            await Call("history.undo");
            Same("a batch is a single undo step", before, await Snapshot());
            await Call("history.redo");

            var updated = (await Call("city.batchUpdate", new { kind = "road", update = "segments = 4", where = "segments = 9" })).Value<int>();
            Check("city.batchUpdate updates the matching roads (" + updated + ")", updated == 1);
            await Expect("batch update syntax error", "invalid_value", "city.batchUpdate", new { kind = "road", update = "segments 4" });
        }

        async Task Errors() {
            await Expect("an unknown element", "not_found", "elements.get", new { @ref = 999999 });
            await Expect("an element of the wrong kind", "wrong_kind", "roads.delete", new { road = patch });
            await Expect("an unknown parameter of a command", "invalid_param", "roads.delete", new { road = roadC, force = true });
            await Expect("a missing parameter", "invalid_param", "roads.delete");
            await Expect("a vector with two numbers", "invalid_value", "meshes.create", new { mesh = "x.obj", position = new[] { 1, 2 } });
            await Expect("an unknown reference field", "invalid_param", "elements.get", new { @ref = new { street = 1 } });
            await Expect("an unknown kind", "invalid_value", "types.list", new { kind = "castle" });
        }

        //a script edits an element open in the editor: the editor is closed first, the change can be undone from the menu
        async Task EditorCoordination() {
            var road = History.UndoManager.GetAliveElement(roadC) as Road;
            builder.SelectObject(road, true, null);
            await Frames(3);
            Check("the road editor is open", CallBuilder<EditorPanel>("FindShownPanel") != null);
            var before = (await Call("state.getParam", new { @ref = roadC, name = "segments" })).Value<int>();
            await Call("state.setBuiltin", new { @ref = roadC, name = "segments", value = before + 3 });
            await Frames(3);
            Check("the editor is closed by the change", CallBuilder<EditorPanel>("FindShownPanel") == null);
            builder.PerformUndo();
            await Frames(3);
            Check("Edit > Undo reverts the change of the script", (await Call("state.getParam", new { @ref = roadC, name = "segments" })).Value<int>() == before);
        }

        //the elements created by the API are the same as the ones created with the editor tools
        async Task Parity() {
            //road: the road tool, clicked point by point with the road editor open
            UndoManagerRecord("create road", () => CallBuilder<object>("CreateRoad"));
            await Frames(3);
            var placer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
            var points = new[] { new Vector3(200, 0, -600), new Vector3(400, 0, -620), new Vector3(600, 0, -600) };
            foreach (var p in points) {
                UndoManagerRecord("click", () => placer.Drawing.AddPoint(GeometryHelper.ProjectPoint(p)));
                await Frames(2);
            }
            var uiRoad = placer.GetRoad(false);
            CallBuilder<object>("DeselectAndCloseCurrentPanel");
            await Frames(3);
            var offset = new Vector3(0, 0, 200);
            var apiRoad = (await Call("roads.create", new { points = System.Array.ConvertAll(points, p => new { position = p + offset, onGround = true }) })).Value<int>();
            var ui = await Call("elements.get", new { @ref = History.ElementRegistry.GetId(uiRoad) });
            var api = await Call("elements.get", new { @ref = apiRoad });
            Same("road state like the editor", ui["data"]["state"], api["data"]["state"]);
            Same("road instance state like the editor", ui["data"]["instanceState"], api["data"]["instanceState"]);
            var uiPoints = (JArray)ui["data"]["points"];
            var apiPoints = (JArray)api["data"]["points"];
            var samePoints = uiPoints.Count == apiPoints.Count;
            for (int i = 0; samePoints && i < uiPoints.Count; i++) {
                samePoints = System.Math.Abs(uiPoints[i]["x"].Value<float>() - apiPoints[i]["x"].Value<float>()) < 0.01f
                    && System.Math.Abs(uiPoints[i]["z"].Value<float>() + offset.Z - apiPoints[i]["z"].Value<float>()) < 0.01f;
            }
            Check("road points like the editor", samePoints);

            //terrain patch: the terrain tool
            UndoManagerRecord("create terrain", () => CallBuilder<object>("CreateTerrain"));
            await Frames(3);
            var terrainPlacer = (ElementPlacer.TerrainPlacer)builder.terrainClick.modifier;
            var corners = new[] { new Vector3(700, 0, 600), new Vector3(800, 0, 600), new Vector3(800, 0, 700) };
            foreach (var p in corners) {
                UndoManagerRecord("click", () => Operations.TerrainPatches.AddPerimeterPoint(terrainPlacer.GetPatch(), null, GeometryHelper.ProjectPoint(p), true));
                await Frames(2);
            }
            var uiPatch = terrainPlacer.GetPatch(false);
            CallBuilder<object>("DeselectAndCloseCurrentPanel");
            await Frames(3);
            var apiPatch = (await Call("patches.create", new { perimeter = System.Array.ConvertAll(corners, p => new { position = p - offset, onGround = true }) })).Value<int>();
            Same("terrain patch state like the editor",
                (await Call("elements.get", new { @ref = History.ElementRegistry.GetId(uiPatch) }))["data"]["state"],
                (await Call("elements.get", new { @ref = apiPatch }))["data"]["state"]);
        }

        static void UndoManagerRecord(string label, System.Action action) {
            History.UndoManager.Record(label, action);
        }

        async Task WholeCity() {
            //a command sent while a long operation runs is refused
            var dispatcher = ApiHost.Instance.Dispatcher;
            var generation = dispatcher.Execute(new JObject { ["id"] = -1, ["method"] = "city.generateTerrain", ["params"] = new JObject() });
            await Frames(1);
            var busy = await dispatcher.Execute(new JObject { ["id"] = -2, ["method"] = "roads.delete", ["params"] = new JObject { ["road"] = roadC } });
            Check("a command during the generation is refused (busy)", busy["error"]?["code"]?.Value<string>() == "busy");
            var res = await generation;
            Check("city.generateTerrain creates terrain patches", res["result"]?["terrainPatches"]?.Value<int>() > 2);
            await Call("history.undo");
            await Call("terrain.brush", new { mode = "raise", position = V(500, 0, 500), range = 30, intensity = 50 });
            var h = (await Call("terrain.height", new { x = 500, z = 500 })).Value<float>();
            Check("terrain.brush raises the terrain (" + h + ")", h > 1);
            await Call("history.undo");
            Check("undoing the brush", (await Call("terrain.height", new { x = 500, z = 500 })).Value<float>() < h);
        }

        // Anchors, terrain points, heights

        //an anchor reference as a key ("of:index"), null for none
        static string Key(JToken a) {
            return a == null || a.Type == JTokenType.Null ? null : a["of"] + ":" + a["index"];
        }

        static Vector3 Vec(JToken t) {
            return new Vector3(t[0].Value<float>(), t[1].Value<float>(), t[2].Value<float>());
        }

        static float DistanceXZ(Vector3 a, Vector3 b) {
            return new Vector2(a.X - b.X, a.Z - b.Z).Length();
        }

        async Task Anchors() {
            var all = (JArray)await Call("anchors.list");
            Check("anchors.list lists the anchors (" + all.Count + ")", all.Count > 0);
            var byKey = new Dictionary<string, JToken>();
            foreach (var a in all) byKey[Key(a)] = a;
            Check("the anchors have distinct references", byKey.Count == all.Count);
            var mutual = true;
            foreach (var a in all) {
                foreach (var dir in new[] { "next", "prev" }) {
                    if (a[dir].Type == JTokenType.Null) continue;
                    if (!byKey.TryGetValue(Key(a[dir]), out var o) || (Key(o["next"]) != Key(a) && Key(o["prev"]) != Key(a))) mutual = false;
                }
            }
            Check("the links between the anchors go both ways", mutual);
            var used = byKey[roadA + ":0"];
            Check("the anchor of the terrain patch is used by it", used["usedBy"].Any(u => u["element"].Value<int>() == patch && !u["segment"].Value<bool>()));
            var unused = (JArray)await Call("anchors.list", new { unusedBy = "terrainPatch" });
            Check("anchors.list unusedBy leaves it out", unused.Count < all.Count && !unused.Any(u => Key(u) == roadA + ":0"));
            var p = await Derived(patch);
            Check("the perimeter points tell their anchor", Key(p["perimeter"][0]["anchor"]) == roadA + ":0" && p["perimeter"][1]["anchor"].Type == JTokenType.Null);
            Check("elements.get derived has the anchor links", (await Derived(roadA))["anchors"].Any(x => x["next"].Type != JTokenType.Null));
            await Expect("an anchor out of range", "not_found", "anchors.loop", new { anchor = new { of = roadA, index = 9999 } });

            //a building line starting on the segment between two anchors
            JToken from = null;
            foreach (var a in all) {
                if (a["of"].Value<int>() != roadC) continue;
                var seg = await Call("anchors.segment", new { anchor = new { of = roadC, index = a["index"].Value<int>() } });
                if (seg.Type != JTokenType.Null && seg["length"].Value<float>() > 4) { from = a; break; }
            }
            Check("anchors.segment finds a segment along road C", from != null);
            if (from != null) {
                var anchorRef = new { of = roadC, index = from["index"].Value<int>() };
                var seg = await Call("anchors.segment", new { anchor = anchorRef });
                var start = Vec(seg["start"]);
                var end = Vec(seg["end"]);
                var onSegment = start + (end - start) * 0.25f;
                var segLine = (await Call("lines.create", new { points = new object[] { new { anchorLink = anchorRef, position = onSegment + new Vector3(0, 0, 3) }, Pos(onSegment.X, onSegment.Z + 40) }, details = true }));
                var linePoint = segLine["points"][0].Value<int>();
                var lp = await Derived(segLine["id"].Value<int>());
                Check("a line point on an anchor segment", Key(lp["points"][0]["segment"]?["anchor"]) == Key(from) && DistanceXZ(Vec(lp["points"][0]["position"]), onSegment) < 3.5f);

                //moving it slides it along the segment
                var moved = Vec(await Call("terrainPoints.move", new { point = linePoint, position = start + (end - start) * 0.75f + new Vector3(0, 0, 10) }));
                var expected = start + (end - start) * 0.75f;
                Check("a point on a segment slides along it", DistanceXZ(moved, expected) < 3.5f && GeometryHelper.ClosestPoint(moved, start, end).DistanceTo(moved) < 0.5f);
                await Call("lines.delete", new { line = segLine["id"].Value<int>() });
            }
            await Expect("a point on an anchor can't be moved", "invalid_operation", "terrainPoints.move", new { point = p["perimeter"][0]["id"].Value<int>(), position = V(0, 0, 0) });
            var free = p["perimeter"][1]["id"].Value<int>();
            await CheckUndo("terrain point moved", () => Call("terrainPoints.move", new { point = free, position = V(-610, 0, -110), onGround = true }));
            Check("the terrain point is moved", DistanceXZ(Vec((await Derived(patch))["perimeter"][1]["position"]), new Vector3(-610, 0, -110)) < 0.01f);

            //internal points
            var big = (await Call("patches.create", new { perimeter = new[] { Pos(-1000, -1000), Pos(-850, -1000), Pos(-850, -850), Pos(-1000, -850) } })).Value<int>();
            await CheckUndo("internal points generated", () => Call("patches.generateInternalPoints", new { patch = big, distance = 30 }));
            Check("internal points generated (" + Len((await Derived(big))["internalPoints"]) + ")", Len((await Derived(big))["internalPoints"]) > 3);
            await Expect("internal points generated twice", "invalid_operation", "patches.generateInternalPoints", new { patch = big, distance = 30 });
            await Call("patches.delete", new { patch = big });
        }

        async Task Heights() {
            var info = await Call("terrain.info");
            var grid = info["gridSize"].Value<int>();
            var pixel = info["pixelSize"].Value<float>();
            var origin = info["origin"].Value<float>();
            Check("terrain.info has the grid (" + grid + ", " + pixel + " m)", grid > 0 && pixel > 0);
            //a free area
            int gx = (int)((-950 - origin) / pixel), gz = (int)((950 - origin) / pixel);
            var area = await Call("terrain.heights.get", new { x = gx, z = gz, width = 4, depth = 3, encoding = "array" });
            var h0 = area["heights"][0][0].Value<float>();
            var ground = (await Call("terrain.height", new { x = gx * pixel + origin, z = gz * pixel + origin })).Value<float>();
            Check("terrain.heights.get matches terrain.height (" + h0 + ", " + ground + ")", System.Math.Abs(h0 - ground) < 0.05f);
            var b64 = await Call("terrain.heights.get", new { x = gx, z = gz, width = 4, depth = 3 });
            var bytes = System.Convert.FromBase64String(b64["heights"].Value<string>());
            Check("the base64 heights are the same", bytes.Length == 48 && System.Math.Abs(System.BitConverter.ToSingle(bytes, 4 * 5) - area["heights"][1][1].Value<float>()) < 1e-4);
            var raised = new float[3][];
            for (int i = 0; i < 3; i++) {
                raised[i] = new float[4];
                for (int j = 0; j < 4; j++) raised[i][j] = area["heights"][i][j].Value<float>() + 5;
            }
            await CheckUndo("terrain heights set", () => Call("terrain.heights.set", new { x = gx, z = gz, width = 4, depth = 3, heights = raised, encoding = "array" }));
            var after = await Call("terrain.heights.get", new { x = gx, z = gz, width = 4, depth = 3, encoding = "array" });
            Check("terrain.heights.set raises the area", System.Math.Abs(after["heights"][2][3].Value<float>() - raised[2][3]) < 0.05f);
            var groundAfter = (await Call("terrain.height", new { x = gx * pixel + origin, z = gz * pixel + origin })).Value<float>();
            Check("the ground follows (" + groundAfter + ")", System.Math.Abs(groundAfter - raised[0][0]) < 0.05f);
            var tooHigh = new float[1][] { new float[] { 1e7f, 0 } };
            var clamped = await Call("terrain.heights.set", new { x = gx, z = gz, width = 2, depth = 1, heights = tooHigh, encoding = "array" });
            Check("heights out of range are clamped", clamped["clamped"].Value<int>() == 1);
            await Call("history.undo");
            await Expect("an area out of the grid", "invalid_value", "terrain.heights.get", new { x = grid - 1, z = 0, width = 4, depth = 1 });
            await Expect("heights of the wrong size", "invalid_value", "terrain.heights.set", new { x = gx, z = gz, width = 4, depth = 3, heights = System.Convert.ToBase64String(new byte[8]) });
        }

        // Transactions

        async Task Transactions() {
            var before = await Snapshot();
            await Expect("history.commit without a transaction", "invalid_operation", "history.commit");
            await Call("history.begin", new { label = "api script" });
            await Expect("a second transaction", "invalid_operation", "history.begin");
            await Expect("undo inside a transaction", "invalid_operation", "history.undo");
            Check("history.state tells the transaction", (await Call("history.state"))["transaction"]?.Value<string>() == "api script");
            var road = (await Call("roads.create", new { points = new[] { Pos(-900, 700), Pos(-800, 720), Pos(-700, 700) } })).Value<int>();
            var patchesBefore = ((JArray)await Call("elements.list", new { kind = "terrainPatch" })).Count;
            await Expect("a command failing halfway inside a transaction", "invalid_value", "patches.create", new { perimeter = new[] { Pos(-900, 800), Pos(-850, 800) } });
            Check("the failed command left nothing", ((JArray)await Call("elements.list", new { kind = "terrainPatch" })).Count == patchesBefore);
            await Call("state.setBuiltin", new { @ref = road, name = "segments", value = 6 });
            await Call("roads.extend", new { road, point = Pos(-600, 690) });
            await Call("history.commit");
            var after = await Snapshot();
            Check("history.commit closes the transaction", (await Call("history.state"))["transaction"].Type == JTokenType.Null);
            await Call("history.undo");
            Same("a transaction is a single undo step", before, await Snapshot());
            await Call("history.redo");
            Same("redo of a transaction", after, await Snapshot());

            await Call("history.begin");
            await Call("roads.delete", new { road });
            await Call("state.setBuiltin", new { @ref = roadC, name = "segments", value = 3 });
            await Call("history.rollback");
            Same("history.rollback undoes the transaction", after, await Snapshot());
            await Call("history.undo");
        }

        // City-wide generation made by a script with the commands, compared with the built-in one

        int[] square;

        async Task BuildBlock() {
            var r1 = (await Call("roads.create", new { points = new[] { Pos(450, -300), Pos(650, -300), Pos(850, -300) } })).Value<int>();
            var r2 = (await Call("roads.create", new { points = new object[] { new { roadEnd = new { road = r1, end = "end" } }, Pos(850, -100), Pos(850, 100) } })).Value<int>();
            var r3 = (await Call("roads.create", new { points = new object[] { new { roadEnd = new { road = r2, end = "end" } }, Pos(650, 100), Pos(450, 100) } })).Value<int>();
            var r4 = (await Call("roads.create", new { points = new object[] { new { roadEnd = new { road = r3, end = "end" } }, Pos(450, -100), new { roadEnd = new { road = r1, end = "start" } } } })).Value<int>();
            square = new[] { r1, r2, r3, r4 };
            Check("a block closed by four roads", (await Derived(r4))["endIntersection"].Type != JTokenType.Null);
        }

        static object PointOf(int? terrainPoint, Vector3 position) {
            return terrainPoint.HasValue ? (object)new { terrainPoint = terrainPoint.Value } : new { position };
        }

        //creates a patch through the targets (each followed from the previous anchor, as the generator clicks them),
        //returns its id and the point given by each target
        async Task<(int id, List<int?> points)> CreateGeneratedPatch(List<object> targets) {
            var res = await Send("patches.create", new { perimeter = targets, followAnchors = true, details = true });
            if (res["error"] != null) {
                Log("     generated patch not created: " + res["error"]["message"]);
                return (-1, targets.Select(t => (int?)null).ToList());
            }
            return (res["result"]["id"].Value<int>(), res["result"]["points"].Select(t => t.Type == JTokenType.Null ? (int?)null : t.Value<int>()).ToList());
        }

        //the city-wide terrain generation, made with the commands only (it is the reference for the scripts doing it)
        async Task<List<int>> ScriptTerrain(float distance, float segmentLength, float fusionDistance, int smooth, float internalDistance) {
            var all = (JArray)await Call("anchors.list");
            var anchorRefs = new Dictionary<string, JToken>();
            var positions = new Dictionary<string, Vector3>();
            foreach (var a in all) {
                anchorRefs[Key(a)] = new JObject { ["of"] = a["of"], ["index"] = a["index"] };
                positions[Key(a)] = Vec(a["position"]);
            }
            var anchors = ((JArray)await Call("anchors.list", new { unusedBy = "terrainPatch" })).Select(Key).ToList();
            var anchorSet = new HashSet<string>(anchors);
            var doneAnchors = all.Select(Key).Where(k => !anchorSet.Contains(k)).ToList();
            var doneSet = new HashSet<string>(doneAnchors);
            var tmpPatches = new List<int>();
            var patches = new List<int>();
            while (anchors.Count > 0) {
                var anchor = anchors[0];
                var res = await Send("patches.create", new { perimeter = new[] { new { anchor = anchorRefs[anchor] } }, followAnchors = true, autoClose = true, details = true });
                if (res["error"] == null) {
                    var toDelete = false;
                    foreach (var point in res["result"]["anchors"].Select(Key)) {
                        if (anchorSet.Contains(point)) {
                            anchors.Remove(point);
                            anchorSet.Remove(point);
                            doneAnchors.Add(point);
                            doneSet.Add(point);
                        } else if (doneSet.Contains(point)) {
                            toDelete = true;
                            break;
                        }
                    }
                    var id = res["result"]["id"].Value<int>();
                    if (!toDelete) {
                        patches.Add(id);
                        tmpPatches.Add(id);
                    } else {
                        await Call("patches.delete", new { patch = id });
                    }
                } else {
                    var code = res["error"]["code"].Value<string>();
                    if (code != "no_loop_found" && code != "autoclose_failed") Check("generation: patch on " + anchor + " (" + res["error"] + ")", false);
                }
                anchors.Remove(anchor);
                anchorSet.Remove(anchor);
            }

            //the patches with anchors inside (around the outside of the roads) are replaced by a ring of patches
            var deleted = new HashSet<int>();
            foreach (var patchId in tmpPatches) {
                var perimeter = (JArray)(await Derived(patchId))["perimeter"];
                var points = perimeter.Select(x => Vec(x["position"])).ToList();
                var comps = perimeter.Select(x => (int?)x["id"].Value<int>()).ToList();
                var compAnchors = perimeter.Select(x => Key(x["anchor"])).ToList();
                float minX = points.Min(x => x.X), maxX = points.Max(x => x.X), minZ = points.Min(x => x.Z), maxZ = points.Max(x => x.Z);
                foreach (var done in doneAnchors) {
                    var pos = positions[done];
                    if (pos.X < minX || pos.X > maxX || pos.Z < minZ || pos.Z > maxZ) continue;
                    if (compAnchors.Contains(done) || !GeometryHelper.IsPointInsidePolygon(pos, points)) continue;

                    //outer vertices
                    var origPointsCount = points.Count;
                    var outerPoints = GeometryHelper.GetOuterPolygon(points, distance);
                    outerPoints.Reverse();
                    foreach (var p in outerPoints) {
                        var added = await Call("patches.addPerimeterPoint", new { patch = patchId, point = new { position = p }, followAnchors = true });
                        comps.Add(added.Type == JTokenType.Null ? (int?)null : added.Value<int>());
                        points.Add(p);
                    }
                    outerPoints.Reverse();

                    //last patch
                    var targets = new List<object> { PointOf(comps[0], points[0]), PointOf(comps[origPointsCount - 1], points[origPointsCount - 1]) };
                    var initialMidIndices = new List<int>();
                    if (internalDistance > 0) {
                        var p0 = points[origPointsCount - 1];
                        var p1 = points[origPointsCount];
                        var dir = GameEngine.Vector3.Normalize(p1 - p0);
                        var pT = p0;
                        while (GameEngine.Vector3.Distance(pT, p1) > 2 * internalDistance) {
                            pT += dir * internalDistance;
                            initialMidIndices.Add(targets.Count);
                            targets.Add(new { position = pT });
                        }
                    }
                    var op2Index = targets.Count;
                    targets.Add(PointOf(comps[origPointsCount], points[origPointsCount]));
                    var ip2Index = targets.Count;
                    targets.Add(PointOf(comps[comps.Count - 1], points[points.Count - 1]));
                    var finalMidIndices = new List<int>();
                    if (internalDistance > 0) {
                        var p0 = points[points.Count - 1];
                        var p1 = points[0];
                        var dir = GameEngine.Vector3.Normalize(p1 - p0);
                        var pT = p0;
                        while (GameEngine.Vector3.Distance(pT, p1) > 2 * internalDistance) {
                            pT += dir * internalDistance;
                            finalMidIndices.Add(targets.Count);
                            targets.Add(new { position = pT });
                        }
                    }
                    var patch2 = await CreateGeneratedPatch(targets);
                    if (patch2.id >= 0) patches.Add(patch2.id);
                    var initialMidPoints = initialMidIndices.Select(i => patch2.points[i]).ToList();
                    var op2 = patch2.points[op2Index];

                    //split in multiple patches and remove excess vertices
                    var curPos = 0.0f;
                    var startJ = 0;
                    var lastOuterPoint = patch2.points[ip2Index];
                    var lastMidPoints = finalMidIndices.Select(i => patch2.points[i]).ToList();
                    lastMidPoints.Reverse();
                    var lastInsertedPoint = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    for (int j = 1; j < origPointsCount; j++) {
                        curPos += GameEngine.Vector3.Distance(points[j], points[j - 1]);
                        if (curPos > segmentLength || j == origPointsCount - 1) {
                            var subOuterPoints = outerPoints.GetRange(startJ, j - startJ + 1);
                            subOuterPoints.Reverse();
                            var targets3 = new List<object>();
                            for (int k = startJ; k <= j; k++) {
                                targets3.Add(compAnchors[k] != null ? (object)new { anchor = anchorRefs[compAnchors[k]] } : new { position = points[k] });
                            }
                            var newLastMidIndices = new List<int>();
                            if (internalDistance > 0) {
                                if (j == origPointsCount - 1) {
                                    foreach (var mp in initialMidPoints) targets3.Add(new { terrainPoint = mp });
                                } else {
                                    var p0 = points[j];
                                    var p1 = subOuterPoints[0];
                                    var dir = GameEngine.Vector3.Normalize(p1 - p0);
                                    var pT = p0;
                                    while (GameEngine.Vector3.Distance(pT, p1) > 2 * internalDistance) {
                                        pT += dir * internalDistance;
                                        newLastMidIndices.Add(targets3.Count);
                                        targets3.Add(new { position = pT });
                                    }
                                }
                            }
                            var firstOuterIndex = -1;
                            for (int k = 0; k < j - startJ; k++) {
                                int? existingPoint = (j == origPointsCount - 1 && k == 0) ? op2 : null;
                                if (k == 0 || (GameEngine.Vector3.Distance(lastInsertedPoint, subOuterPoints[k]) > fusionDistance)
                                     && GameEngine.Vector3.Distance(subOuterPoints[j - startJ], subOuterPoints[k]) > fusionDistance) {
                                    if (firstOuterIndex < 0) firstOuterIndex = targets3.Count;
                                    targets3.Add(PointOf(existingPoint, subOuterPoints[k]));
                                    lastInsertedPoint = subOuterPoints[k];
                                }
                            }
                            targets3.Add(PointOf(lastOuterPoint, subOuterPoints[subOuterPoints.Count - 1]));
                            if (internalDistance > 0) {
                                lastMidPoints.Reverse();
                                foreach (var mp in lastMidPoints) targets3.Add(new { terrainPoint = mp });
                            }
                            var patch3 = await CreateGeneratedPatch(targets3);
                            if (patch3.id >= 0) patches.Add(patch3.id);
                            lastOuterPoint = firstOuterIndex >= 0 ? patch3.points[firstOuterIndex] : null;
                            if (internalDistance > 0) lastMidPoints = newLastMidIndices.Select(i => patch3.points[i]).ToList();
                            curPos = 0;
                            startJ = j;
                        }
                    }
                    await Call("patches.delete", new { patch = patchId });
                    deleted.Add(patchId);
                    break;
                }
            }
            var res2 = new List<int>();
            foreach (var p in patches) {
                if (deleted.Contains(p)) continue;
                if (internalDistance > 0) await Send("patches.generateInternalPoints", new { patch = p, distance = internalDistance });
                await Call("state.setBuiltin", new { @ref = p, name = "smooth", value = smooth });
                res2.Add(p);
            }
            return res2;
        }

        //the city-wide building generation, made with the commands only
        async Task<List<int>> ScriptBuildings(string linePreset, List<string> buildingPresets, float minLength, float maxLength, bool subdivide) {
            var anchors = ((JArray)await Call("anchors.list", new { unusedBy = "buildingLine" })).ToList();
            var all = (JArray)await Call("anchors.list");
            var anchorSet = new HashSet<string>(anchors.Select(Key));
            var doneSet = new HashSet<string>(all.Select(Key).Where(k => !anchorSet.Contains(k)));
            //the length of the segment from each anchor to the next one (they don't change while the lines are added)
            var lengths = new Dictionary<string, float>();
            foreach (var a in anchors) {
                var seg = await Call("anchors.segment", new { anchor = new { of = a["of"], index = a["index"] } });
                if (seg.Type != JTokenType.Null) lengths[Key(a)] = seg["length"].Value<float>();
            }
            var lines = new List<int>();
            while (anchors.Count > 0) {
                //the anchor with the longest segment
                JToken anchor = null;
                var maxDist = 0.0f;
                foreach (var a in anchors) {
                    if (lengths.TryGetValue(Key(a), out var dist) && dist > maxDist) {
                        maxDist = dist;
                        anchor = a;
                    }
                }
                if (anchor == null) break;
                var anchorRef = new { of = anchor["of"], index = anchor["index"] };
                var loopPoints = (JArray)await Call("anchors.loop", new { anchor = anchorRef });
                if (loopPoints.Count > 1) {
                    var res = await Send("lines.create", new {
                        points = new[] { new { anchorLink = anchorRef } }, preset = linePreset, followAnchors = true,
                        autoClose = new { presets = buildingPresets, minLength, maxLength, subdivide }, details = true
                    });
                    if (res["error"] == null) {
                        var toDelete = false;
                        foreach (var point in res["result"]["anchors"].Select(Key)) {
                            if (anchorSet.Contains(point)) {
                                anchors.RemoveAll(x => Key(x) == point);
                                anchorSet.Remove(point);
                                doneSet.Add(point);
                            } else if (doneSet.Contains(point)) {
                                toDelete = true;
                                break;
                            }
                        }
                        var id = res["result"]["id"].Value<int>();
                        if (!toDelete) lines.Add(id);
                        else await Call("lines.delete", new { line = id });
                    }
                }
                anchors.RemoveAll(x => Key(x) == Key(anchor));
                anchorSet.Remove(Key(anchor));
            }
            return lines;
        }

        //the points (x, z) of the new elements of a kind, in a comparable order
        async Task<List<List<Vector3>>> Shapes(string kind, HashSet<int> except) {
            var res = new List<List<Vector3>>();
            foreach (var e in (JArray)await Call("elements.list", new { kind })) {
                var id = e["id"].Value<int>();
                if (except.Contains(id)) continue;
                var d = await Derived(id);
                res.Add(((JArray)(kind == "terrainPatch" ? d["perimeter"] : d["points"])).Select(x => Vec(x["position"])).ToList());
            }
            return res.OrderBy(s => s.Count).ThenBy(s => System.Math.Round(s.Average(p => p.X), 1)).ThenBy(s => System.Math.Round(s.Average(p => p.Z), 1)).ToList();
        }

        bool SameShapes(string what, List<List<Vector3>> a, List<List<Vector3>> b) {
            var ok = a.Count == b.Count;
            string diff = ok ? null : a.Count + " elements instead of " + b.Count;
            for (int i = 0; ok && i < a.Count; i++) {
                ok = a[i].Count == b[i].Count;
                for (int j = 0; ok && j < a[i].Count; j++) ok = DistanceXZ(a[i][j], b[i][j]) < 0.05f;
                if (!ok) diff = "element " + i + ": " + string.Join(" ", a[i].Select(p => "(" + p.X.ToString("0.0") + "," + p.Z.ToString("0.0") + ")"))
                        + " instead of " + string.Join(" ", b[i].Select(p => "(" + p.X.ToString("0.0") + "," + p.Z.ToString("0.0") + ")"));
            }
            Check(what + " (" + b.Count + ")", ok);
            if (diff != null) Log("      " + diff);
            return ok;
        }

        async Task<HashSet<int>> Ids(string kind) {
            return new HashSet<int>(((JArray)await Call("elements.list", new { kind })).Select(e => e["id"].Value<int>()));
        }

        async Task ScriptedGenerators() {
            await BuildBlock();
            //the loop of an anchor is the one auto close follows
            JToken closed = null;
            foreach (var a in (JArray)await Call("anchors.list", new { unusedBy = "terrainPatch" })) {
                if (a["of"].Value<int>() != square[0]) continue;
                var anchorRef = new { of = a["of"], index = a["index"] };
                var res = await Send("patches.create", new { perimeter = new[] { new { anchor = anchorRef } }, autoClose = true, details = true });
                if (res["error"] != null) continue;
                var loop = ((JArray)await Call("anchors.loop", new { anchor = anchorRef })).Select(Key).ToList();
                var closedWith = res["result"]["anchors"].Select(Key).ToList();
                Check("anchors.loop gives the anchors auto close follows (" + closedWith.Count + ")", closedWith.Count == loop.Count - 1 && closedWith.SequenceEqual(loop.Take(loop.Count - 1)));
                closed = res["result"];
                await Call("history.undo");
                break;
            }
            Check("a terrain patch closed around the block", closed != null);

            var patchesBefore = await Ids("terrainPatch");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await Call("history.begin", new { label = "scripted terrain" });
            await ScriptTerrain(5, 100, 0, 2, 0);
            await Call("history.commit");
            Log("     scripted terrain generation: " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s");
            var scripted = await Shapes("terrainPatch", patchesBefore);
            await Call("history.undo");
            Check("the scripted terrain generation is a single undo step", (await Ids("terrainPatch")).SetEquals(patchesBefore));
            await Call("city.generateTerrain", new { distance = 5, segmentLength = 100, fusionDistance = 0, smoothing = 2, internalDistance = 0 });
            var generated = await Shapes("terrainPatch", patchesBefore);
            SameShapes("the scripted terrain generation gives the patches of city.generateTerrain", scripted, generated);
            await Call("history.undo");

            var linePreset = ((JArray)await Call("presets.list", new { category = "buildingLine" }))[0].Value<string>();
            var buildingPreset = ((JArray)await Call("presets.list", new { category = "building" }))[0].Value<string>();
            var linesBefore = await Ids("buildingLine");
            watch.Restart();
            await Call("history.begin", new { label = "scripted buildings" });
            await ScriptBuildings(linePreset, new List<string> { buildingPreset }, 15, 15, false);
            await Call("history.commit");
            Log("     scripted building generation: " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s");
            var scriptedLines = await Shapes("buildingLine", linesBefore);
            await Call("history.undo");
            await Call("city.generateBuildings", new { linePreset, buildingPresets = new[] { buildingPreset }, minLength = 15, maxLength = 15, subdivide = false });
            var generatedLines = await Shapes("buildingLine", linesBefore);
            SameShapes("the scripted building generation gives the lines of city.generateBuildings", scriptedLines, generatedLines);
            await Call("history.undo");
        }

        // Python scripts run by ScriptRunner

        async Task<int> CountOf(string kind) {
            return ((JArray)await Call("elements.list", new { kind })).Count;
        }

        async Task PythonScripts() {
            if (!PythonManager.IsPythonReady()) {
                Log("     Python is not available: the script tests are skipped");
                return;
            }
            //the examples of the wiki, listed as the scripts of a user folder
            var scripts = new List<ScriptCatalog.ScriptInfo>();
            var examples = Path.Combine(PathHelper.BasePath(), "wiki", "examples", "scripts");
            typeof(ScriptCatalog).GetMethod("Scan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { examples, "user", scripts });
            var terrain = scripts.FirstOrDefault(s => s.id == "user:cityTerrain");
            var buildings = scripts.FirstOrDefault(s => s.id == "user:cityBuildings");
            Check("the scripts of a folder are listed", terrain != null && buildings != null && scripts.Any(s => s.id == "user:citySummary.py" && !s.HasInputs));
            if (terrain == null || buildings == null) return;
            Check("the label of a script is read from its strings", terrain.label == "Whole city terrain (script)");
            var defaults = ScriptCatalog.ToInputs(ScriptCatalog.DefaultValues(terrain));
            Check("the default inputs of a script", terrain.parameters.Length == 6 && defaults["distance"]?.Value<float>() == 5
                && defaults["segmentLength"]?.Value<float>() == 100 && defaults["smoothing"]?.Value<int>() == 0);
            var buildingDefaults = ScriptCatalog.ToInputs(ScriptCatalog.DefaultValues(buildings));
            var linePreset = ((JArray)await Call("presets.list", new { category = "buildingLine" }))[0].Value<string>();
            var buildingPreset = ((JArray)await Call("presets.list", new { category = "building" }))[0].Value<string>();
            Check("a preset input defaults to the default preset", buildingDefaults["linePreset"]?.Value<string>() == linePreset && buildingDefaults["subdivide"]?.Value<bool>() == true);

            //the generators written in Python give the elements of the built-in ones
            var patchesBefore = await Ids("terrainPatch");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var inputs = new JObject { ["distance"] = 5, ["segmentLength"] = 100, ["fusionDistance"] = 0, ["smoothing"] = 2, ["internalDistance"] = 0, ["texture"] = "" };
            var res = await ScriptRunner.Run(builder, terrain, inputs, null, false);
            Check("the terrain script runs" + (res.success ? "" : " (" + res.error + ")"), res.success);
            Log("     Python terrain generation: " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s");
            var scripted = await Shapes("terrainPatch", patchesBefore);
            await Call("history.undo");
            Check("a script run is a single undo step", (await Ids("terrainPatch")).SetEquals(patchesBefore));
            await Call("city.generateTerrain", new { distance = 5, segmentLength = 100, fusionDistance = 0, smoothing = 2, internalDistance = 0 });
            SameShapes("the Python terrain script gives the patches of city.generateTerrain", scripted, await Shapes("terrainPatch", patchesBefore));
            await Call("history.undo");

            var linesBefore = await Ids("buildingLine");
            watch.Restart();
            var buildingInputs = new JObject {
                ["linePreset"] = linePreset, ["buildingPresets"] = new JArray(new JObject { ["preset"] = buildingPreset }),
                ["minLength"] = 15, ["maxLength"] = 15, ["subdivide"] = false, ["nullOnly"] = false
            };
            res = await ScriptRunner.Run(builder, buildings, buildingInputs, null, false);
            Check("the buildings script runs" + (res.success ? "" : " (" + res.error + ")"), res.success);
            Log("     Python building generation: " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s");
            var scriptedLines = await Shapes("buildingLine", linesBefore);
            await Call("history.undo");
            await Call("city.generateBuildings", new { linePreset, buildingPresets = new[] { buildingPreset }, minLength = 15, maxLength = 15, subdivide = false });
            SameShapes("the Python buildings script gives the lines of city.generateBuildings", scriptedLines, await Shapes("buildingLine", linesBefore));
            await Call("history.undo");
            buildingInputs["buildingPresets"] = new JArray();
            res = await ScriptRunner.Run(builder, buildings, buildingInputs, null, false);
            Check("the error of a script is translated (" + res.error + ")", !res.success && res.error == buildings.GetString("SCRIPT_CITY_BUILDINGS_NO_PRESETS") && res.error != "SCRIPT_CITY_BUILDINGS_NO_PRESETS");

            //the runner
            var dir = Path.GetFullPath(Path.Combine(folder, "..", "api_scripts"));
            Directory.CreateDirectory(dir);
            ScriptCatalog.ScriptInfo Script(string name, string code, string settings = null) {
                var path = Path.Combine(dir, name);
                if (settings == null) {
                    File.WriteAllText(path + ".py", code);
                    return ScriptCatalog.FromPath(path + ".py");
                }
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "main.py"), code);
                File.WriteAllText(Path.Combine(path, ScriptCatalog.SettingsFile), settings);
                return ScriptCatalog.FromPath(path);
            }
            const string header = "import os, sys, time\nimport citybuilder as cb\nfrom citybuilder import api\n"
                + "def road(x):\n    return api.roads.create(points=[{\"position\": [x, 0, 900], \"onGround\": True}, {\"position\": [x + 60, 0, 900], \"onGround\": True}])\n";

            var roads = await CountOf("road");
            var selected = new List<int> { square[0] };
            res = await ScriptRunner.Run(builder, Script("ok", header + @"
values = cb.inputs()
assert values == {'a': 1, 'b': 'x'}, values
assert cb.selection() == [" + square[0] + @"], cb.selection()
for i in range(300):
    print('a line printed by the script', i)
os.write(1, b'not a command\n')
r = road(-950)
road(-850)
api.elements.rename(ref=r, name='Piazza è')
cb.progress(0.5, 'halfway')
assert cb.batch([('api.version', {}), {'method': 'city.info'}])[0]['protocol'] == 1
api.api.configure(autoFlush=False)
for method in ('history.begin', 'city.reload', 'history.undo'):
    try:
        cb.call(method)
    except cb.ApiError as e:
        cb.alert(e.code, method)
cb.alert('done', 'test')
"), new JObject { ["a"] = 1, ["b"] = "x" }, selected, false);
            Check("a script gets its inputs and the selection, can print and write to its output" + (res.success ? "" : " (" + res.error + ")"), res.success);
            Check("a script can't open a transaction, load a city or undo during its own transaction",
                res.messages.Count(m => m.text == "invalid_operation") == 3 && res.messages.Any(m => m.title == "test" && m.text == "done"));
            Check("the settings changed by a script are restored", ApiHost.Instance.Api.autoFlush);
            Check("the text sent by a script is UTF-8", ((JArray)await Call("elements.list", new { name = "Piazza è" })).Count == 1);
            Check("the changes of the script are kept", await CountOf("road") == roads + 2);
            await Call("history.undo");
            Check("the changes of the script are undone in one step", await CountOf("road") == roads);

            res = await ScriptRunner.Run(builder, Script("fail", header + @"
road(-950)
raise RuntimeError('deliberate failure')
"), null, null, false);
            Check("a failing script reports its error (" + res.error + ")", !res.success && !res.cancelled && res.error == "deliberate failure" && res.undone);
            Check("the changes of a failing script are undone", await CountOf("road") == roads && !(await Call("history.state"))["canRedo"].Value<bool>()
                && (await Call("history.state"))["transaction"].Type == JTokenType.Null);

            var running = ScriptRunner.Run(builder, Script("loop", header + @"
road(-950)
cb.alert('started')
while True:
    time.sleep(0.05)
"), null, null, false);
            for (int i = 0; i < 600 && await CountOf("road") == roads; i++) await Frames(1);
            Check("a script is running", ScriptRunner.IsRunning && await CountOf("road") == roads + 1);
            var second = await ScriptRunner.Run(builder, Script("other", header), null, null, false);
            Check("only one script runs at a time", !second.success && second.error == StringManager.Get("SCRIPT_BUSY"));
            ScriptRunner.Current?.Cancel();
            res = await running;
            Check("a cancelled script is stopped and its changes undone", res.cancelled && !res.success && !ScriptRunner.IsRunning && await CountOf("road") == roads);

            res = await ScriptRunner.Run(builder, Script("manual", header + @"
with cb.transaction('manual'):
    road(-950)
    road(-850)
road(-750)
", "{\"transaction\": false}"), null, null, false);
            Check("a script without the run transaction manages its own" + (res.success ? "" : " (" + res.error + ")"), res.success && await CountOf("road") == roads + 3);
            await Call("history.undo");
            Check("its last change is an undo step of its own", await CountOf("road") == roads + 2);
            await Call("history.undo");
            Check("its transaction is a single undo step", await CountOf("road") == roads);

            //the editor: the Scripts menu and the input panels
            CallBuilder<object>("ReloadScriptsMenu");
            var panel = (EditorPanels.ScriptInputEditorPanel)typeof(CityBuilderMenuBar).GetField("scriptInputEditorPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(builder);
            CallBuilder<object>("StartScript", buildings);
            await Frames(3);
            Check("the input panel of a script opens", panel.ActiveSelf());
            Screenshot("script_panel_buildings.png");
            CallBuilder<object>("StartScript", terrain);
            await Frames(3);
            Screenshot("script_panel_terrain.png");
            patchesBefore = await Ids("terrainPatch");
            typeof(EditorPanels.ScriptInputEditorPanel).GetMethod("Run", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(panel, null);
            Check("the input panel closes when the script is run", !panel.ActiveSelf());
            for (int i = 0; i < 10 && !ScriptRunner.IsRunning; i++) await Frames(1);
            Check("the Run button starts the script", ScriptRunner.IsRunning);
            await Frames(20);
            Screenshot("script_running.png");
            for (int i = 0; i < 3000 && ScriptRunner.IsRunning; i++) await Frames(1);
            Check("the script run from the panel creates the patches", (await Ids("terrainPatch")).Count > patchesBefore.Count);
            Check("the values of the panel are kept for the next run", PreferencesManager.Get<string>("scriptInputs:user:cityTerrain", null) != null);
            await Call("history.undo");
        }

        void Screenshot(string name) {
            if (DisplayServer.GetName() == "headless") return;
            builder.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, "..", name));
        }

        //the geometry of each kind of element is the one of the export file
        async Task Geometry(JObject exported) {
            var kinds = new (string kind, string list)[] { ("road", "roads"), ("intersection", "intersections"), ("terrainPatch", "terrainPatches"), ("buildingLine", "buildingLines") };
            foreach (var (kind, list) in kinds) {
                var elements = (JArray)await Call("elements.list", new { kind });
                if (elements.Count == 0) {
                    Log("     no " + kind);
                    continue;
                }
                var name = elements[0]["name"].Value<string>();
                var geometry = await Call("elements.geometry", new { @ref = elements[0]["id"].Value<int>() });
                var exportedElement = ((JArray)exported[list]).FirstOrDefault(x => x["data"]["name"]?.Value<string>() == name);
                var mesh = geometry["element"]["data"]["mesh"];
                Check("elements.geometry of a " + kind + " has its mesh", mesh != null && mesh.Type != JTokenType.Null && mesh["b64v3a_vertices"] != null);
                Check("elements.geometry of a " + kind + " is the one exported", exportedElement != null && JToken.DeepEquals(mesh, exportedElement["data"]["mesh"]));
                if (kind == "road") Check("the materials of the geometry are given", ((JObject)geometry["materials"]).Count > 0);
            }
            await Expect("the geometry of a terrain point", "wrong_kind", "elements.geometry", new { @ref = ((JArray)await Call("elements.list", new { kind = "terrainPoint" }))[0]["id"].Value<int>() });
        }

        async Task Files() {
            var before = await Call("city.info");
            await Call("city.save");
            var names = new Dictionary<string, JToken>();
            foreach (var e in (JArray)await Call("elements.list", new { kind = "road" })) {
                names[e["name"].Value<string>()] = (await Call("elements.get", new { @ref = e["id"].Value<int>() }))["data"]["state"];
            }
            await Call("city.close");
            Check("city.close", !(await Call("city.info"))["loaded"].Value<bool>());
            await Call("city.load", new { folder });
            var after = await Call("city.info");
            Check("save and load keep the elements", before["roads"].Value<int>() == after["roads"].Value<int>()
                && before["intersections"].Value<int>() == after["intersections"].Value<int>()
                && before["terrainPatches"].Value<int>() == after["terrainPatches"].Value<int>()
                && before["buildingLines"].Value<int>() == after["buildingLines"].Value<int>()
                && before["meshInstances"].Value<int>() == after["meshInstances"].Value<int>());
            var sameStates = true;
            foreach (var e in (JArray)await Call("elements.list", new { kind = "road" })) {
                var name = e["name"].Value<string>();
                var state = (await Call("elements.get", new { @ref = e["id"].Value<int>() }))["data"]["state"];
                sameStates &= names.TryGetValue(name, out var saved) && SameToken(saved, state, name, new List<string>());
            }
            Check("save and load keep the road states", sameStates);

            var exportJson = Path.Combine(folder, "..", "api_export.json");
            await Call("city.exportJson", new { path = exportJson });
            var exported = JObject.Parse(File.ReadAllText(exportJson));
            Check("city.exportJson writes the roads with their geometry", ((JArray)exported["roads"]).Count == after["roads"].Value<int>() && exported["roads"][0]["data"]?["mesh"] != null);
            await Geometry(exported);

            var exporters = (JArray)await Call("exporters.list");
            if (exporters.Count > 0) {
                var format = exporters[0]["format"].Value<string>();
                await Expect("an unknown exporter flag", "invalid_value", "city.export", new { path = Path.Combine(folder, "..", "api_export"), exporter = format, flags = new Dictionary<string, bool> { ["teleport"] = true } });
                var export = await Send("city.export", new { path = Path.Combine(folder, "..", "api_export"), exporter = format });
                if (export["error"] != null) {
                    Log("FAIL city.export: " + export["error"]);
                    failures++;
                } else {
                    var path = export["result"]["path"].Value<string>();
                    Check("city.export runs the exporter (exit code " + export["result"]["exitCode"] + ")", export["result"]["exitCode"].Value<int>() == 0 && File.Exists(path));
                    Check("the export JSON is deleted after the export", !File.Exists(Path.ChangeExtension(path, ".json")));
                }
            }
        }
    }
}

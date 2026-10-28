using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;
using TI = CityElements.Types.Parsers.TypeInheritance;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    //Development self-test of the scripting API, run with "-- selftest api <new folder>" on the command line. Creates a city
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

        public static void Start(CityBuilderMenuBar builder, string folder) {
            builder.AddChild(new ApiSelfTest { builder = builder, folder = folder, Name = "ApiSelfTest" });
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
                LuaScripts();
                TypeMerging();
                await NewCity();
                InheritedTypeFiles();
                await Roads();
                await TrafficLights();
                await TerrainAndBuildings();
                await Anchors();
                await Heights();
                await States();
                await ArrayPoints();
                await Decals();
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
                await InheritedTypes();
                await Files();
                await View();
                await Validate();
                await McpTests();
                await LowLevel();
                await LowLevelEditing();
                await DerivedPoints();
                await AutoBai();
                await LowLevelRoomEditing();
                await BaiConnections();
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

        //Lua scripts of the definitions, from a scratch folder instead of the lua folder of the core
        void LuaScripts() {
            var dir = folder + "_lua";
            Directory.CreateDirectory(Path.Combine(dir, "lib"));
            File.WriteAllText(Path.Combine(dir, "lib", "util.lua"), "return { double = function(x) return x * 2 end }");
            File.WriteAllText(Path.Combine(dir, "broken.lua"), "return { f = function(v) return 1 + end }");
            File.WriteAllText(Path.Combine(dir, "test.lua"), string.Join("\n",
                "local util = require('lib.util')",
                "local M = {}",
                "function M.number(v) return util.double(v.a) end",
                "function M.boolean(v) return v.flag and not v.other end",
                "function M.types(v) return type(v.flag) == 'boolean' and type(v.a) == 'number' end",
                "function M.vector3(v) return v.p * 2 + vec3(1, 0, 0):cross(vec3(0, 1, 0)) end",
                "function M.vector2(v) return -v.q + vec2(1, 1) end",
                "function M.angle(v) return v.p:signedAngle(vec3(0, 0, 1), vec3(0, -1, 0)) end",
                "function M.write(v) v.a = 1 return 0 end",
                "function M.unknown(v) return v.nope end",
                "function M.wrongType(v) return 'text' end",
                "function M.sandbox(v) return io == nil and os == nil and debug == nil and dofile == nil and loadfile == nil and math.random == nil end",
                "function M.escape(v) return require('../test') ~= nil end",
                "function M.loop(v) while true do end end",
                "function M.noContext(v, ctx) return ctx == nil end",
                "return M"));

            var host = RuntimeCalculator.Scripting.LuaScriptHost.CreateForFolder(dir);
            var vc = new RuntimeCalculator.VariableContainer(new List<(string, System.Type)> {
                ("a", typeof(float)), ("flag", typeof(float)), ("other", typeof(float)), ("p", typeof(Vector3)), ("q", typeof(Vector2))
            });
            vc.boolNames.Add("flag");
            vc.boolNames.Add("other");
            vc.SetFloat("a", 1.5f);
            vc.SetFloat("flag", 1);
            vc.SetFloat("other", 0);
            vc.SetVector3("p", new Vector3(1, 2, 3));
            vc.SetVector2("q", new Vector2(4, 5));
            float Number(string f) => new RuntimeCalculator.Scripting.ScriptNumber("test." + f, f, host).GetValue(vc);
            bool Boolean(string f) => new RuntimeCalculator.Scripting.ScriptBoolean("test." + f, f, host).GetValue(vc);

            Check("lua: number definition (with a required module)", Mathf.Abs(Number("number") - 3) < 1e-5f);
            Check("lua: boolean definition", Boolean("boolean"));
            Check("lua: bool variables are booleans, the others numbers", Boolean("types"));
            var v3 = new RuntimeCalculator.Scripting.ScriptVector3("test.vector3", "vector3", host).GetValue(vc);
            Check("lua: vec3 definition (" + v3 + ")", v3.IsEqualApprox(new Vector3(2, 4, 7)));
            var v2 = new RuntimeCalculator.Scripting.ScriptVector2("test.vector2", "vector2", host).GetValue(vc);
            Check("lua: vec2 definition (" + v2 + ")", v2.IsEqualApprox(new Vector2(-3, -4)));
            var angle = GameEngine.Vector3.SignedAngle(new Vector3(1, 2, 3), new Vector3(0, 0, 1), new Vector3(0, -1, 0));
            Check("lua: vec3 functions give the results of the expressions", Mathf.Abs(Number("angle") - angle) < 1e-4f);
            Check("lua: no io, os, debug, file loading, random", Boolean("sandbox"));
            Check("lua: the context is nil without an element", Boolean("noContext"));

            var errors = host.ErrorCount;
            Check("lua: writing a variable fails, the result is the default", Number("write") == 0 && host.ErrorCount == errors + 1);
            Number("write");
            Check("lua: an error is reported only once", host.ErrorCount == errors + 1);
            Check("lua: unknown variable fails", Number("unknown") == 0 && host.ErrorCount == errors + 2);
            Check("lua: a value of the wrong type fails", Number("wrongType") == 0 && host.ErrorCount == errors + 3);
            Check("lua: modules can't be required from outside the folder", !Boolean("escape") && host.ErrorCount == errors + 4);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var looped = Number("loop");
            Check("lua: an infinite loop is stopped (" + watch.ElapsedMilliseconds + " ms)", looped == 0 && watch.ElapsedMilliseconds < 5000);
            Check("lua: the state still works after an error", Mathf.Abs(Number("number") - 3) < 1e-5f);

            bool Throws(string reference) {
                try {
                    new RuntimeCalculator.Scripting.ScriptNumber(reference, reference, host);
                    return false;
                } catch (System.Exception e) {
                    Log("     " + reference + ": " + e.Message);
                    return true;
                }
            }
            Check("lua: a missing module is an error when the types are read", Throws("missing.f"));
            Check("lua: a missing function is an error when the types are read", Throws("test.missing"));
            Check("lua: a syntax error is an error when the types are read", Throws("broken.f"));
            Check("lua: a reference without a function is an error", Throws("test"));

            var definition = new RuntimeCalculator.Scripting.ScriptNumber("test.number", "number", host);
            watch.Restart();
            for (int i = 0; i < 100000; i++) definition.GetValue(vc);
            Check("lua: 100000 calls in " + watch.ElapsedMilliseconds + " ms", watch.ElapsedMilliseconds < 3000);
        }

        //the traffic lights of the core are placed by its Lua script
        async Task TrafficLights() {
            var steps = 0;
            async Task Set(string name, object value) {
                await Call("state.setParam", new { @ref = roadA, name, value });
                steps++;
            }
            await Set("end_rule", 1);
            await Set("forwardLanes", 1);
            var center = (await Derived((await Derived(roadA))["endIntersection"].Value<int>()))["center"].ToObject<float[]>();
            var objRef = new { owner = roadA, @object = "endTrafficLight" };
            Check("a road with lights at its end has an end traffic light", (await Derived(roadA))["parametricObjects"].ToObject<List<string>>().Contains("endTrafficLight"));
            var p = (await Call("objects.get", new { @object = objRef }))["position"].ToObject<float[]>();
            var distance = new Vector3(p[0] - center[0], 0, p[2] - center[2]).Length();
            Check("the traffic light is on a corner of the intersection (" + distance.ToString("0.0") + " m from its center)", distance > 1 && distance < 40);
            await Set("trafficLightOnOppositeRoad", true);
            var p2 = (await Call("objects.get", new { @object = objRef }))["position"].ToObject<float[]>();
            var distance2 = new Vector3(p2[0] - center[0], 0, p2[2] - center[2]).Length();
            var moved = new Vector3(p2[0] - p[0], p2[1] - p[1], p2[2] - p[2]).Length();
            Check("on the opposite road the light moves to another corner (" + moved.ToString("0.0") + " m)", moved > 1 && distance2 < 40);
            for (int i = 0; i < steps; i++) await Call("history.undo");
            Check("the traffic light is removed with its rule", !(await Derived(roadA))["parametricObjects"].ToObject<List<string>>().Contains("endTrafficLight"));
        }

        async Task NewCity() {
            await Expect("city.new with an unknown template", "not_found", "city.new", new { folder, core = "MidtownMadness2", template = "Atlantis" });
            var info = await Call("city.new", new { folder, core = "MidtownMadness2", template = "SF", terrainSize = 2048, heightmapResolution = 512, maxHeight = 1000 });
            Check("city.new loads the city", info["loaded"].Value<bool>() && info["terrainSize"].Value<int>() == 2048);
            Check("progress events while loading (" + progressEvents + ")", progressEvents > 0);
            Check("the history is empty after loading", !(await Call("history.state"))["canUndo"].Value<bool>());
            Check("city.new gives the city the city properties of the template",
                (await Call("state.getParam", new { @ref = 0, name = "speedLimit" }))?.Value<float>() == 15
                && !File.Exists(Path.Combine(folder, Operations.CityFiles.TemplateCityPropertiesFile)));
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

            //rotation of the sections at the control points (as the road type allows them, a lower yaw limit at an end on an intersection)
            var roadSettings = CityElements.Types.Parsers.TypeParser.GetRoadTypes()[History.ElementRegistry.Get<Road>(roadC).state.Str("type")].typeData.settings;
            float Allowed(bool allowed, float value) => allowed ? value : 0;
            await CheckUndo("road point rotated", () => Call("roads.setPointRotation", new { road = roadC, index = 0, yaw = 15, roll = 10 }));
            var rotation = await Call("roads.setPointRotation", new { road = roadC, index = 0, roll = 12 });
            Check("roads.setPointRotation keeps the value not given, the effective values follow the road type (" + rotation.ToString(Newtonsoft.Json.Formatting.None) + ")",
                rotation["yaw"].Value<float>() == 15 && rotation["roll"].Value<float>() == 12
                && rotation["effectiveYaw"].Value<float>() == Allowed(roadSettings.allowYaw, 15) && rotation["effectiveRoll"].Value<float>() == Allowed(roadSettings.allowRoll, 12));
            var last = Len((await Derived(roadC))["points"]) - 1;
            var endRotation = await Call("roads.setPointRotation", new { road = roadC, index = last, yaw = 100 });
            var intersectionYaw = Allowed(roadSettings.allowYaw, GeometryHelper.MaxIntersectionYaw);
            Check("values clamped, a lower yaw limit at the end on an intersection", endRotation["yaw"].Value<float>() == 75 && endRotation["effectiveYaw"].Value<float>() == intersectionYaw);
            var c = await Derived(roadC);
            Check("the derived info has the rotations (" + c["pointRotations"].ToString(Newtonsoft.Json.Formatting.None) + " " + c["effectivePointRotations"].ToString(Newtonsoft.Json.Formatting.None) + ")",
                c["pointRotations"][0][0].Value<float>() == 15 && c["effectivePointRotations"][0][1].Value<float>() == Allowed(roadSettings.allowRoll, 12)
                && c["pointRotations"][last][0].Value<float>() == 75 && c["effectivePointRotations"][last][0].Value<float>() == intersectionYaw);
            await Expect("a rotation of a point that doesn't exist", "not_found", "roads.setPointRotation", new { road = roadC, index = 5, yaw = 1 });
            foreach (var p in new[] { 0, last }) await Call("roads.setPointRotation", new { road = roadC, index = p, yaw = 0, roll = 0 });

            //the height of a road end on an intersection
            await CheckUndo("connection height set", () => Call("state.setBuiltin", new { @ref = roadC, name = "endConnectionHeight", value = 2.5f }));
            await Call("state.setBuiltin", new { @ref = roadC, name = "endConnectionHeight", value = 2.5f });
            c = await Derived(roadC);
            Check("the derived info has the connection height (" + c["endConnectionHeight"] + ")", System.Math.Abs(c["endConnectionHeight"].Value<float>() - 2.5f) < 1e-4);
            var cRoad = History.ElementRegistry.Get<Road>(roadC);
            var cEnd = cRoad.generator.GetCurvePoints();
            var cCenter = (await Derived(intersection))["center"];
            Check("the road ends higher on the intersection (" + (cEnd[cEnd.Count - 1][1] - cCenter[1].Value<float>()) + ")",
                System.Math.Abs(cEnd[cEnd.Count - 1][1] - cCenter[1].Value<float>() - 2.5f) < 0.01f);
            await Call("history.undo");
            Check("undo resets the connection height", (await Derived(roadC))["endConnectionHeight"].Value<float>() == 0);

            //moving the end of a road on an intersection moves the intersection, and the ends of its other roads
            var center0 = (await Derived(intersection))["center"];
            await CheckUndo("road end on an intersection moved", () => Call("roads.movePoint", new { road = roadC, index = last, position = V(15, 0, 25), onGround = true }));
            var movedCenter = (await Derived(intersection))["center"];
            var bEnd = (await Derived(roadB))["points"].Last;
            bool Near(JToken p, float x, float z) => System.Math.Abs(p[0].Value<float>() - x) < 0.01f && System.Math.Abs(p[2].Value<float>() - z) < 0.01f;
            Check("the intersection follows the road end", Near(movedCenter, 15, 25) && Near(bEnd, 15, 25));
            await Call("history.undo");
            Check("undo moves the intersection back", Near((await Derived(intersection))["center"], center0[0].Value<float>(), center0[2].Value<float>())
                && Near((await Derived(roadB))["points"].Last, center0[0].Value<float>(), center0[2].Value<float>()));
            await CheckUndo("intersection moved", () => Call("intersections.move", new { intersection, position = V(10, 0, 10), onGround = true }));
            var e = await Call("roads.create", new { points = new[] { Pos(-800, 400), Pos(-700, 400), Pos(-600, 400) } });
            var roadE = e.Value<int>();
            await Call("roads.extend", new { road = roadE, point = Pos(-500, 450) });
            Check("roads.extend adds a point", Len((await Derived(roadE))["points"]) == 4);
            //extended backwards, undone after
            await Call("roads.extend", new { road = roadE, point = Pos(-900, 380), atStart = true });
            var ePoints = (await Derived(roadE))["points"];
            Check("roads.extend atStart adds a point before the first one", Len(ePoints) == 5 && System.Math.Abs(ePoints[0][0].Value<float>() + 900) < 0.01f
                && System.Math.Abs(ePoints[1][0].Value<float>() + 800) < 0.01f);
            var roadF = (await Call("roads.create", new { points = new[] { Pos(-950, 700), Pos(-950, 500) } })).Value<int>();
            await Call("roads.extend", new { road = roadE, point = new { roadEnd = new { road = roadF, end = "end" } }, atStart = true });
            var eDerived = await Derived(roadE);
            Check("roads.extend atStart connects the start of the road", eDerived["startIntersection"].Type != JTokenType.Null
                && eDerived["startIntersection"].Value<int>() == (await Derived(roadF))["endIntersection"].Value<int>() && eDerived["endIntersection"].Type == JTokenType.Null);
            await Expect("a road can't continue before its start intersection", "invalid_operation", "roads.extend", new { road = roadE, point = Pos(-950, 300), atStart = true });
            await Expect("no point inserted before a start on an intersection", "invalid_operation", "roads.insertPoint", new { road = roadE, index = 0, position = V(-950, 0, 300) });
            for (int u = 0; u < 3; u++) await Call("history.undo");
            Check("undo removes the points added before the start", Len((await Derived(roadE))["points"]) == 4);
            var removed = await Call("roads.removePoint", new { road = roadE });
            Check("removing the last point keeps the road", !removed["deleted"].Value<bool>());
            await CheckUndo("road deleted", () => Call("roads.delete", new { road = roadE }));
            await MergeRoads();
            await SeamlessIntersections();
            await Call("elements.rename", new { @ref = roadA, name = "Main street" });
            Check("elements.list by name", ((JArray)await Call("elements.list", new { name = "Main street" })).Count == 1);
            Check("reference by name", (await Call("elements.get", new { @ref = "Main street" }))["id"].Value<int>() == roadA);
        }

        //merging the two roads of an intersection into one (the reverse of a split), on roads of their own deleted at the end
        async Task MergeRoads() {
            async Task<int> CountOf(string kind) => ((JArray)await Call("elements.list", new { kind })).Count;
            float Horizontal(JToken a, JToken b) => new Vector2(a[0].Value<float>() - b[0].Value<float>(), a[2].Value<float>() - b[2].Value<float>()).Length();
            //the largest horizontal distance of the points of a curve from another one
            float Deviation(JToken curve, JToken original) {
                var res = 0.0f;
                foreach (var p in curve) {
                    var point = new Vector3(p[0].Value<float>(), 0, p[2].Value<float>());
                    var min = float.MaxValue;
                    for (int i = 0; i < original.Count() - 1; i++) {
                        var a = new Vector3(original[i][0].Value<float>(), 0, original[i][2].Value<float>());
                        var b = new Vector3(original[i + 1][0].Value<float>(), 0, original[i + 1][2].Value<float>());
                        min = Mathf.Min(min, point.DistanceTo(GeometryHelper.ClosestPoint(point, a, b)));
                    }
                    res = Mathf.Max(res, min);
                }
                return res;
            }

            //a road split by a road connected to its middle, which is then deleted: two roads on an intersection
            var roadsBefore = await CountOf("road");
            var intersectionsBefore = await CountOf("intersection");
            var main = (await Call("roads.create", new { points = new[] { Pos(-800, 700), Pos(-650, 780), Pos(-500, 700), Pos(-350, 640) } })).Value<int>();
            var original = (await Derived(main))["curve"];
            var originalPoints = (await Derived(main))["points"];
            var near = original[original.Count() / 2];
            var cross = (await Call("roads.create", new { points = new object[] { Pos(-560, 900), new { roadMiddle = new { road = main, near = near.ToObject<float[]>() } } } })).Value<int>();
            var joint = (await Derived(main))["endIntersection"].Value<int>();
            var second = -1; //the part after the split, starting on the new intersection
            foreach (var e in (JArray)await Call("elements.list", new { kind = "road" })) {
                var id = e["id"].Value<int>();
                var start = (await Derived(id))["startIntersection"];
                if (id != main && id != cross && start?.Type == JTokenType.Integer && start.Value<int>() == joint) second = id;
            }
            Check("merge: the road is split in two", second > 0);
            await Expect("merging an intersection of three roads", "invalid_operation", "intersections.mergeRoads", new { intersection = joint });
            await Call("roads.delete", new { road = cross });
            await Expect("merging into a road not on the intersection", "invalid_operation", "intersections.mergeRoads", new { intersection = joint, keep = roadA });
            await Expect("merging into an element that is not a road", "wrong_kind", "intersections.mergeRoads", new { intersection = joint, keep = joint });
            await Expect("merging without an intersection", "invalid_param", "intersections.mergeRoads");
            //the far end of the second part gives its properties to the merged road
            await Call("state.setBuiltin", new { @ref = second, name = "endCrosswalkSize", value = 3.5f });
            await Call("state.setBuiltin", new { @ref = main, name = "endCrosswalkSize", value = 7.0f });
            await Call("state.setBuiltin", new { @ref = main, name = "endIntersectionEnd", value = true });
            int merged = -1;
            await CheckUndo("roads merged", async () => merged = (await Call("intersections.mergeRoads", new { intersection = joint })).Value<int>());
            var m = await Derived(merged);
            var st = (JObject)(await Call("elements.get", new { @ref = merged }))["data"];
            //the points of the original curve on the merged one (which has the segments of both parts, more than the original)
            var deviation = Deviation(original, m["curve"]);
            var pointsBack = Len(m["points"]) == Len(originalPoints);
            for (int i = 0; pointsBack && i < Len(originalPoints); i++) pointsBack = Horizontal(m["points"][i], originalPoints[i]) < 0.05f;
            Check("merge: the first road is kept, the two parts of the Bézier curve give its control points again", merged == main && pointsBack);
            Check("merge: the other road and the intersection are deleted", await CountOf("road") == roadsBefore + 1 && await CountOf("intersection") == intersectionsBefore);
            Check("merge: the merged road follows the original curve (" + deviation.ToString("0.00") + " m)", deviation < 0.5f);
            Check("merge: the end gets the properties of the far end of the other road (" + st.SelectToken("state.endCrosswalkSize") + ", " + st.SelectToken("instanceState.endIntersectionEnd") + ")",
                st.SelectToken("state.endCrosswalkSize")?.Value<float>() == 3.5f && st.SelectToken("instanceState.endIntersectionEnd")?.Value<bool>() != true);

            //the merged road split again without a connected road, then merged back
            JToken splitResult = null;
            await CheckUndo("road split", async () => splitResult = await Call("roads.split", new { road = main, near = near.ToObject<float[]>() }));
            var splitJoint = splitResult["intersection"].Value<int>();
            var splitSecond = splitResult["newRoad"].Value<int>();
            Check("split: the road is split in two connected by an intersection of two roads", await CountOf("road") == roadsBefore + 2
                && await CountOf("intersection") == intersectionsBefore + 1 && Len((await Derived(splitJoint))["roadsClockwise"]) == 2
                && (await Derived(main))["endIntersection"].Value<int>() == splitJoint && (await Derived(splitSecond))["startIntersection"].Value<int>() == splitJoint);
            await Expect("splitting an element that is not a road", "wrong_kind", "roads.split", new { road = splitJoint, near = V(0, 0, 0) });
            await Expect("splitting without a road", "invalid_param", "roads.split", new { near = V(0, 0, 0) });
            var remerged = (await Call("intersections.mergeRoads", new { intersection = splitJoint, keep = main })).Value<int>();
            var rm = await Derived(remerged);
            var backAgain = remerged == main && Len(rm["points"]) == Len(originalPoints);
            for (int i = 0; backAgain && i < Len(originalPoints); i++) backAgain = Horizontal(rm["points"][i], originalPoints[i]) < 0.05f;
            Check("split: merging the parts back gives the control points of the Bézier curve again", backAgain
                && await CountOf("road") == roadsBefore + 1 && await CountOf("intersection") == intersectionsBefore);

            //two roads going opposite ways: the other one is reversed (its roll negated, its start properties at the end)
            var r1 = (await Call("roads.create", new { points = new[] { Pos(-800, 1000), Pos(-700, 1050), Pos(-600, 1000) } })).Value<int>();
            var r2 = (await Call("roads.create", new { points = new object[] { Pos(-400, 1000), Pos(-500, 950), new { roadEnd = new { road = r1, end = "end" } } } })).Value<int>();
            var j2 = (await Derived(r1))["endIntersection"].Value<int>();
            var r2Start = (await Derived(r2))["points"][0];
            await Call("state.setBuiltin", new { @ref = r1, name = "curveType", value = "hermite" });
            await Call("state.setBuiltin", new { @ref = r2, name = "curveType", value = "hermite" });
            await Call("roads.setPointRotation", new { road = r2, index = 1, yaw = 5, roll = 10 });
            await Call("state.setBuiltin", new { @ref = r2, name = "startCrosswalkSize", value = 2.5f });
            await Call("intersections.mergeRoads", new { intersection = j2, keep = r1 });
            var m2 = await Derived(r1);
            var st2 = (JObject)(await Call("elements.get", new { @ref = r1 }))["data"];
            Check("merge reversed: the points go on from the end of the kept road to the start of the other one",
                Len(m2["points"]) == 5 && Horizontal(m2["points"][4], r2Start) < 0.01f && m2["endIntersection"].Type == JTokenType.Null);
            Check("merge reversed: the roll is negated, the yaw kept (" + m2["pointRotations"][3].ToString(Newtonsoft.Json.Formatting.None) + ")",
                m2["pointRotations"][3][0].Value<float>() == 5 && m2["pointRotations"][3][1].Value<float>() == -10);
            Check("merge reversed: the start properties of the other road are at the end", st2.SelectToken("state.endCrosswalkSize")?.Value<float>() == 2.5f);

            //roads of different curves: the merged road keeps the curve type of the kept one, following both roads
            const float cornerZ = -800;
            async Task<int> Corner(float x, string keepType, bool curvedOther) {
                var first = (await Call("roads.create", new { points = new[] { Pos(x, cornerZ), Pos(x + 200, cornerZ) } })).Value<int>();
                var secondPoints = curvedOther
                    ? new object[] { new { roadEnd = new { road = first, end = "end" } }, Pos(x + 350, cornerZ + 50), Pos(x + 300, cornerZ + 200) }
                    : new object[] { new { roadEnd = new { road = first, end = "end" } }, Pos(x + 200, cornerZ + 200) };
                await Call("roads.create", new { points = secondPoints });
                if (keepType != null) await Call("state.setBuiltin", new { @ref = first, name = "curveType", value = keepType });
                return first;
            }
            async Task<(JToken merged, JToken followed)> MergeCorner(int first) {
                var j = (await Derived(first))["endIntersection"].Value<int>();
                var other = (await Derived(j))["roadsClockwise"].ToObject<List<int>>().Find(x => x != first);
                var followed = (await Derived(other))["curve"];
                var id = (await Call("intersections.mergeRoads", new { intersection = j, keep = first })).Value<int>();
                return (await Derived(id), followed);
            }
            string TypeOf(int road) => History.ElementRegistry.Get<Road>(road).GetCurveType().ToString();

            //two straight Bézier roads at a right angle: a single Bézier curve rounding the corner
            var corner = await Corner(-800, null, false);
            var (cornerMerged, _) = await MergeCorner(corner);
            var cornerDistance = float.MaxValue;
            foreach (var p in cornerMerged["curve"]) cornerDistance = Mathf.Min(cornerDistance, new Vector2(p[0].Value<float>() + 600, p[2].Value<float>() - cornerZ).Length());
            //joining the control points would give a curve 70.7 m from the corner
            Check("merge best effort: two Bézier roads at a corner give a quadratic Bézier curve close to the corner (" + cornerDistance.ToString("0.0") + " m, " + Len(cornerMerged["points"]) + " points)",
                TypeOf(corner) == "Bezier" && Len(cornerMerged["points"]) == 3 && cornerDistance < 60.0f
                && Horizontal(cornerMerged["points"][2], JToken.FromObject(V(-600, 0, cornerZ + 200))) < 0.01f);
            //a curved Bézier road merged into a Hermite road and into a low-poly road
            foreach (var type in new[] { "hermite", "lowPoly" }) {
                var road = await Corner(type == "hermite" ? -300 : 200, type, true);
                var (mergedCurve, followed) = await MergeCorner(road);
                var followedDeviation = Deviation(followed, mergedCurve["curve"]);
                Check("merge best effort: a curved Bézier road merged into a " + type + " road (" + TypeOf(road) + ", " + Len(mergedCurve["points"]) + " points) follows it ("
                    + followedDeviation.ToString("0.00") + " m)", TypeOf(road).ToLowerInvariant() == type.ToLowerInvariant() && followedDeviation < 1.0f);
                await Call("roads.delete", new { road });
            }
            foreach (var r in new[] { main, r1, corner }) await Call("roads.delete", new { road = r });
            Check("merge: the test roads are deleted", await CountOf("road") == roadsBefore && await CountOf("intersection") == intersectionsBefore);
        }

        //two roads joining seamlessly (no surface, the same direction and section at the joint), as after a split, on roads
        //of their own deleted at the end
        async Task SeamlessIntersections() {
            async Task<int> CountOf(string kind) => ((JArray)await Call("elements.list", new { kind })).Count;
            //the direction of the end of a road on an intersection (from the joint, along the road) and its section
            (Vector3 dir, Vector3[] section) EndOf(int id, int joint) {
                var road = History.ElementRegistry.Get<Road>(id);
                var sub = road.generator.subGen;
                var atStart = road.startIntersection == History.ElementRegistry.Get<Intersection>(joint);
                var i = atStart ? 0 : sub.curveDirections.Count - 1;
                var dir = sub.curveDirections[i].Normalized();
                return (atStart ? dir : -dir, sub.sectionVertices[i]);
            }
            //the angle between the ends (0 if they go on each other) and the largest distance between their section vertices
            (float angle, float gap) Joint(int a, int b, int joint) {
                var (dirA, secA) = EndOf(a, joint);
                var (dirB, secB) = EndOf(b, joint);
                var gap = 0.0f;
                foreach (var p in secA) {
                    var min = float.MaxValue;
                    foreach (var q in secB) min = Mathf.Min(min, p.DistanceTo(q));
                    gap = Mathf.Max(gap, min);
                }
                return (Mathf.RadToDeg(dirA.AngleTo(-dirB)), gap);
            }
            string Fmt((float angle, float gap) j) => j.angle.ToString("0.000") + "°, " + j.gap.ToString("0.000") + " m";
            List<Vector3> Curve(JToken curve) => curve.Select(p => new Vector3(p[0].Value<float>(), p[1].Value<float>(), p[2].Value<float>())).ToList();
            //the largest angle between consecutive segments of a curve (degrees)
            float Sharpest(List<Vector3> curve) {
                var res = 0.0f;
                for (int i = 1; i < curve.Count - 1; i++) res = Mathf.Max(res, Mathf.RadToDeg((curve[i] - curve[i - 1]).AngleTo(curve[i + 1] - curve[i])));
                return res;
            }

            var roadsBefore = await CountOf("road");
            var intersectionsBefore = await CountOf("intersection");
            var x = -800.0f;
            foreach (var type in Operations.Builtins.CurveTypes) {
                var road = (await Call("roads.create", new { points = new[] { Pos(x, 1250), Pos(x + 120, 1330), Pos(x + 260, 1240), Pos(x + 360, 1300) } })).Value<int>();
                await Call("state.setBuiltin", new { @ref = road, name = "curveType", value = type });
                var curve = (await Derived(road))["curve"];
                var near = curve[curve.Count() / 2];
                JToken split = null;
                await CheckUndo("road split (" + type + ")", async () => split = await Call("roads.split", new { road, near = near.ToObject<float[]>() }));
                var joint = split["intersection"].Value<int>();
                var second = split["newRoad"].Value<int>();
                var j = await Derived(joint);
                Check("seamless: a split gives a seamless intersection without size (" + type + ")",
                    j["seamless"].Value<bool>() && j["seamlessActive"].Value<bool>() && j["sizes"].All(s => s.Value<float>() == 0));
                var joined = Joint(road, second, joint);
                Check("seamless: the parts of a " + type + " road have the same direction and section at the joint (" + Fmt(joined) + ")", joined.angle < 0.01f && joined.gap < 0.01f);
                if (type != "lowPoly") {
                    //two roads at a right angle made seamless: the curves turn to go on through the joint instead of meeting
                    //at a corner (a low-poly road keeps its segments, only its sections line up)
                    var c1 = (await Call("roads.create", new { points = new[] { Pos(x, 1450), Pos(x + 80, 1460), Pos(x + 160, 1450) } })).Value<int>();
                    var c2 = (await Call("roads.create", new { points = new object[] { new { roadEnd = new { road = c1, end = "end" } }, Pos(x + 170, 1530), Pos(x + 160, 1610) } })).Value<int>();
                    foreach (var r in new[] { c1, c2 }) await Call("state.setBuiltin", new { @ref = r, name = "curveType", value = type });
                    var cj = (await Derived(c1))["endIntersection"].Value<int>();
                    async Task<float> JointAngle() {
                        var a = Curve((await Derived(c1))["curve"]);
                        var b = Curve((await Derived(c2))["curve"]);
                        return Mathf.RadToDeg((a[a.Count - 1] - a[a.Count - 2]).AngleTo(b[1] - b[0]));
                    }
                    var cornerAngle = await JointAngle();
                    await Call("intersections.setSeamless", new { intersection = cj });
                    var smoothAngle = await JointAngle();
                    var c1Curve = Curve((await Derived(c1))["curve"]);
                    var c2Curve = Curve((await Derived(c2))["curve"]);
                    var cornerJoint = Joint(c1, c2, cj);
                    if (type == "bezier") {
                        var sharpest = Mathf.Max(Sharpest(c1Curve), Sharpest(c2Curve));
                        Check("seamless: two " + type + " roads at a corner go on through the joint (" + cornerAngle.ToString("0.0") + "° -> " + smoothAngle.ToString("0.0") + "°, "
                            + sharpest.ToString("0.0") + "° along the roads, " + Fmt(cornerJoint) + ")",
                            cornerAngle > 45 && smoothAngle <= sharpest * 1.5f + 0.1f && cornerJoint.angle < 0.01f && cornerJoint.gap < 0.01f);
                    } else {
                        //the joint is like a point in the middle of a single Hermite curve through the points of both roads
                        //(the segments next to the joint have the same length, its direction is the one of that curve)
                        var all = new List<Vector3>();
                        foreach (var p in (await Derived(c1))["points"]) all.Add(new Vector3(p[0].Value<float>(), 0, p[2].Value<float>()));
                        foreach (var p in (await Derived(c2))["points"].Skip(1)) all.Add(new Vector3(p[0].Value<float>(), 0, p[2].Value<float>()));
                        var single = new List<Vector3>();
                        for (int i = 0; i <= 2000; i++) {
                            single.Add(GeometryHelper.GetPointOnCurve(all[0], all.GetRange(1, all.Count - 2), all[all.Count - 1], i / 2000.0f, i, GeometryHelper.CurveType.Hermite));
                        }
                        var deviation = 0.0f;
                        foreach (var curvePoint in c1Curve.Concat(c2Curve)) {
                            var p = new Vector3(curvePoint[0], 0, curvePoint[2]); //horizontally
                            GeometryHelper.ClosestPointToCurve(p, single, out var minI);
                            deviation = Mathf.Max(deviation, p.DistanceTo(GeometryHelper.ClosestPoint(p, single[minI], single[minI + 1])));
                        }
                        Check("seamless: two " + type + " roads at a corner follow a single curve through their points (" + cornerAngle.ToString("0.0") + "° -> " + smoothAngle.ToString("0.0")
                            + "° between the segments at the joint, " + deviation.ToString("0.000") + " m, " + Fmt(cornerJoint) + ")",
                            cornerAngle > 45 && deviation < 0.05f && cornerJoint.angle < 0.01f && cornerJoint.gap < 0.01f);
                    }
                    foreach (var r in new[] { c1, c2 }) await Call("roads.delete", new { road = r });
                }

                if (type == "hermite") {
                    //the direction of the joint depends on both roads
                    var dirBefore = EndOf(road, joint).dir;
                    var p1 = (await Derived(second))["points"][1];
                    await Call("roads.movePoint", new { road = second, index = 1, position = V(p1[0].Value<float>(), 0, p1[2].Value<float>() + 40), onGround = true });
                    var moved = Joint(road, second, joint);
                    Check("seamless: moving the point next to the joint on a road turns the end of the other one (" + Mathf.RadToDeg(dirBefore.AngleTo(EndOf(road, joint).dir)).ToString("0.00") + "°, "
                        + Fmt(moved) + ")", dirBefore.AngleTo(EndOf(road, joint).dir) > 0.001f && moved.angle < 0.01f && moved.gap < 0.01f);

                    //the roll set on an end is given to the other one
                    if (History.ElementRegistry.Get<Road>(road).IsRollAllowed()) {
                        var last = Len((await Derived(road))["points"]) - 1;
                        await Call("roads.setPointRotation", new { road, index = last, yaw = 0, roll = 8 });
                        var otherRoll = (await Derived(second))["pointRotations"][0][1].Value<float>();
                        var rolled = Joint(road, second, joint);
                        Check("seamless: the roll set on an end is given to the other one (" + otherRoll + "°, " + Fmt(rolled) + ")", otherRoll == 8 && rolled.gap < 0.01f);
                    }

                    await CheckUndo("intersection not seamless", () => Call("intersections.setSeamless", new { intersection = joint, seamless = false }));
                    var off = await Derived(joint);
                    Check("seamless off: the intersection has a size again", !off["seamless"].Value<bool>() && off["sizes"].Any(s => s.Value<float>() > 0));
                    await Call("intersections.setSeamless", new { intersection = joint });

                    //with a third road the intersection isn't seamless, without it again
                    var third = (await Call("roads.create", new { points = new object[] { new { intersection = joint }, Pos(x + 200, 1150) } })).Value<int>();
                    var three = await Derived(joint);
                    Check("seamless: no effect with a third road", three["seamless"].Value<bool>() && !three["seamlessActive"].Value<bool>() && three["sizes"].All(s => s.Value<float>() > 0));
                    await Call("roads.delete", new { road = third });
                    Check("seamless: back without the third road", (await Derived(joint))["seamlessActive"].Value<bool>() && Joint(road, second, joint).gap < 0.01f);
                }
                //the Hermite roads are kept, so that the city is saved, loaded and exported with a seamless intersection
                if (type != "hermite") {
                    foreach (var r in new[] { road, second }) await Call("roads.delete", new { road = r });
                }
                x += 450;
            }
            Check("seamless: the test roads are deleted (but two)", await CountOf("road") == roadsBefore + 2 && await CountOf("intersection") == intersectionsBefore + 1);
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

            //building presets: sync and delete reach the lines, the buildings, and the sides and blocks inside them
            var buildingRef = new { line, building = 0 };
            var sideRef = new { line, building = 0, side = "front" };
            var blockRef = new { line, building = 0, side = "front", block = 0 };
            async Task<float> Param(object r, string name) => (await Call("state.getParam", new { @ref = r, name })).Value<float>();
            async Task<string> PresetOf(object r) => (await Call("state.get", new { @ref = r }))["state"]?["name"]?.Value<string>() ?? "";
            async Task SetParam(object r, string name, object value) => await Call("state.setBuiltin", new { @ref = r, name, value });
            //saved from the inside out, so that the building preset holds the links of its side and block
            await Call("state.saveAsPreset", new { @ref = blockRef, name = "api test block" });
            await Call("state.saveAsPreset", new { @ref = sideRef, name = "api test side" });
            await Call("state.saveAsPreset", new { @ref = buildingRef, name = "api test building" });
            await Call("state.saveAsPreset", new { @ref = line, name = "api test line" });
            var savedHeight = await Param(buildingRef, "height");
            await SetParam(buildingRef, "height", savedHeight + 9);
            await CheckUndo("building preset sync", () => Call("presets.sync", new { category = "building", name = "api test building" }));
            Check("presets.sync gives the building preset to the buildings linked to it", await Param(buildingRef, "height") == savedHeight);
            var savedFloors = await Param(sideRef, "lowUpperFloorsCount");
            await SetParam(sideRef, "lowUpperFloorsCount", (int)savedFloors + 2);
            await CheckUndo("building side preset sync", () => Call("presets.sync", new { category = "buildingSide", name = "api test side" }));
            Check("presets.sync gives the side preset to the building sides linked to it", await Param(sideRef, "lowUpperFloorsCount") == savedFloors);
            var savedListName = (await Call("state.getParam", new { @ref = blockRef, name = "listName" }))?.Value<string>();
            await SetParam(blockRef, "listName", "api test changed");
            await CheckUndo("building block preset sync", () => Call("presets.sync", new { category = "buildingBlock", name = "api test block" }));
            Check("presets.sync gives the block preset to the blocks linked to it", (await Call("state.getParam", new { @ref = blockRef, name = "listName" }))?.Value<string>() == savedListName);
            var savedLineHeight = await Param(line, "height");
            await SetParam(line, "height", savedLineHeight + 7.5);
            await CheckUndo("building line preset sync", () => Call("presets.sync", new { category = "buildingLine", name = "api test line" }));
            Check("presets.sync gives the line preset to the lines linked to it", await Param(line, "height") == savedLineHeight && await PresetOf(line) == "api test line");
            foreach (var (category, name, r) in new (string, string, object)[] { ("buildingBlock", "api test block", blockRef), ("buildingSide", "api test side", sideRef), ("building", "api test building", buildingRef), ("buildingLine", "api test line", line) }) {
                await CheckUndo(category + " preset delete", () => Call("presets.delete", new { category, name }));
                Check("deleting the " + category + " preset unlinks the elements using it", await PresetOf(r) == "" && !((JArray)await Call("presets.list", new { category })).Any(x => x.Value<string>() == name));
            }
            await Expect("terrain patch border mesh presets can't be synced", "invalid_operation", "presets.sync", new { category = "terrainPatchBorderMesh", name = "Default" });

            //city properties: array and custom element parameters
            var cityId = 0;
            var citySchema = await Call("state.schema", new { @ref = cityId });
            string arrayParam = null, customParam = null;
            foreach (var param in citySchema["parameters"]) {
                if (param["type"].Value<string>() == "array" && arrayParam == null) arrayParam = param["name"].Value<string>();
                if (param["type"].Value<string>() == "customElement" && customParam == null) customParam = param["name"].Value<string>();
            }
            if (arrayParam != null) {
                //the template can give the array some elements already
                var initialCount = (await Call("state.getParam", new { @ref = cityId, name = arrayParam }) as JArray)?.Count ?? 0;
                await CheckUndo("array element added", () => Call("state.array.add", new { @ref = cityId, param = arrayParam }));
                var clone = (await Call("state.array.clone", new { @ref = cityId, param = arrayParam, index = 0 })).Value<int>();
                Check("state.array.clone adds at the end", clone == initialCount + 1);
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
                await Expect("an array element out of range", "not_found", "state.array.remove", new { @ref = cityId, param = arrayParam, index = initialCount + 5 });
            }
            if (customParam != null) {
                await Call("state.customElement.ensure", new { @ref = cityId, param = customParam });
                await Call("state.get", new { @ref = new { parent = cityId, param = customParam } });
                Check("custom element reference", true);
            }
        }

        //the line through the parametric objects of an array with drawLine, and their click placement (the train lines of the MM2 core)
        async Task ArrayPoints() {
            var cityId = 0;
            var citySchema = await Call("state.schema", new { @ref = cityId });
            if (!citySchema["parameters"].Any(p => p["name"].Value<string>() == "trainLines")) {
                Log("     no trainLines parameter in the core, array points skipped");
                return;
            }
            var li = (await Call("state.array.add", new { @ref = cityId, param = "trainLines" })).Value<int>();
            var lineRef = new { parent = cityId, param = "trainLines", index = li };
            object PointRef(int pi) => new { owner = cityId, @object = "point", path = new[] { "trainLines." + li, "points." + pi } };
            var positions = new[] { new Vector3(10, 0, 10), new Vector3(60, 0, 10) };
            foreach (var pos in positions) {
                var pi = (await Call("state.array.add", new { @ref = lineRef, param = "points" })).Value<int>();
                await Call("objects.setTransform", new { @object = PointRef(pi), localPosition = new[] { pos.X, pos.Y, pos.Z } });
            }
            await Call("city.flush");
            await Frames(3);
            var cityProperties = M.GetDummy<CityProperties>();
            var lineObject = cityProperties.arrayObjects["trainLines"][li];
            var lr = GO.GetObject(lineObject).GetChildren().OfType<GameEngine.LineRenderer>().FirstOrDefault();
            Check("drawLine: a line through the points of the array", lr != null && lr.positionCount == 2
                && GeometryHelper.AreVectorsEqual(lr.GetPosition(1), positions[1]));
            await Call("objects.setTransform", new { @object = PointRef(1), localPosition = new[] { 60f, 0f, 40f } });
            await Frames(3);
            Check("drawLine: the line follows the points", lr != null && GeometryHelper.AreVectorsEqual(lr.GetPosition(1), new Vector3(60, 0, 40)));

            //click placement: what the placer does on a click (recorded like the terrain click actions)
            var lineState = cityProperties.GetState().Array<ObjectState>("trainLines")[li];
            var pointsParam = CityElements.Types.Parsers.TypeParser.GetCustomTypes()["trainLine"].parameters.First(p => p.name == "points");
            Check("click placement offered for the points", Operations.States.GetSingleObjectInstanceSubparam(pointsParam) != null);
            var placed = new Vector3(100, 0, 30);
            var placer = builder.arrayPointPlacer;
            placer.Start(this, () => lineState, pointsParam, () => cityProperties);
            var started = builder.terrainClick.modifier == placer && placer.IsPlacing(this, pointsParam);
            await CheckUndo("array object placed by clicking", async () => {
                History.UndoManager.Record(nameof(ElementPlacer.ArrayPointPlacer), () => placer.PlaceAt(placed));
                await Call("city.flush");
            });
            placer.Stop();
            Check("click placement mode on and off", started && builder.terrainClick.modifier != placer);
            var obj = await Call("objects.get", new { @object = PointRef(2) });
            Check("the clicked point is placed there", GeometryHelper.AreVectorsEqual(Vec(obj["localPosition"]), placed));
            await Call("state.array.remove", new { @ref = cityId, param = "trainLines", index = li });
        }

        //a line drawn with the width and texture of its element, and objects put back on the surface after a move (the decals
        //of the MM2 core); the decal is kept for the export (see ExportDecals)
        async Task Decals() {
            var cityId = 0;
            var citySchema = await Call("state.schema", new { @ref = cityId });
            if (!citySchema["parameters"].Any(p => p["name"].Value<string>() == "decals")) {
                Log("     no decals parameter in the core, decals skipped");
                return;
            }
            var di = (await Call("state.array.add", new { @ref = cityId, param = "decals" })).Value<int>();
            var decalRef = new { parent = cityId, param = "decals", index = di };
            object PointRef(int pi) => new { owner = cityId, @object = "point", path = new[] { "decals." + di, "points." + pi } };
            const string texture = "texture/decal_zigzag_l.tex";
            await Call("state.setParam", new { @ref = decalRef, name = "texture", value = texture });
            await Call("state.setParam", new { @ref = decalRef, name = "width", value = 3.0f });
            //along road A, which is above the terrain
            var positions = new[] { new Vector3(-560, 0, 0), new Vector3(-500, 0, 2), new Vector3(-450, 0, -3) };
            foreach (var pos in positions) {
                var pi = (await Call("state.array.add", new { @ref = decalRef, param = "points" })).Value<int>();
                await Call("objects.setTransform", new { @object = PointRef(pi), localPosition = new[] { pos.X, pos.Y, pos.Z } });
                await Call("objects.projectToGround", new { @object = PointRef(pi) });
            }
            await Call("city.flush");
            await Frames(3);
            var roadY = GeometryHelper.ProjectPoint(positions[0], 1 << 7).Y;
            var obj = await Call("objects.get", new { @object = PointRef(0) });
            Check("objects.projectToGround puts a decal point on the road", Mathf.Abs(Vec(obj["localPosition"]).Y - roadY) < 1e-3f);

            var cityProperties = M.GetDummy<CityProperties>();
            var decalObject = cityProperties.arrayObjects["decals"][di];
            var lr = GO.GetObject(decalObject).GetChildren().OfType<GameEngine.LineRenderer>().FirstOrDefault();
            //the line settings of the core: the width and the texture tiles from the width parameter
            Check("a decal is drawn textured with its width", lr != null && lr.uvs.HasValue && lr.positionCount == 3
                && Mathf.IsEqualApprox(lr.startWidth, 3) && Mathf.IsEqualApprox(lr.uvs.Value.vLength, 3)
                && lr.material == MaterialManager.GetMaterial(texture) && lr.heightOffset > 0);
            await Call("state.setParam", new { @ref = decalRef, name = "width", value = 4.0f });
            await Call("city.flush");
            await Frames(3);
            Check("the decal line follows the width", lr != null && Mathf.IsEqualApprox(lr.startWidth, 4) && Mathf.IsEqualApprox(lr.endWidth, 4)
                && Mathf.IsEqualApprox(lr.uvs.Value.vLength, 4));

            //exclusive editing keeping the lines: the points only show while the decal is edited
            var category = CityElements.Types.Parsers.TypeParser.GetCustomTypes()["decal"].exclusiveEditing?.category;
            var keepsLines = CityElements.Types.Parsers.TypeParser.GetCustomTypes()["decal"].exclusiveEditing?.keepLinesVisible ?? false;
            if (category != null && keepsLines) {
                var pointHolder = GO.GetObject(decalObject.arrayObjects["points"][0]);
                ArrayObject.activeElements[category] = null;
                builder.NotifyVisibilityChange();
                await Frames(3);
                var hidden = !GO.GetEnabled(pointHolder) && GO.GetEnabled(lr) && lr.IsVisibleInTree();
                ArrayObject.activeElements[category] = decalObject.state;
                builder.NotifyVisibilityChange();
                await Frames(3);
                Check("the points of a decal show only while it is edited, its line always", hidden && GO.GetEnabled(pointHolder));
                ArrayObject.activeElements[category] = null;
                builder.NotifyVisibilityChange();
            }
            if (DisplayServer.GetName() != "headless") {
                //from above the decal, to see it (windowed runs only)
                var cam = builder.GetViewport().GetCamera3D();
                var camTransform = cam.GlobalTransform;
                var target = GameEngine.Vector3.DecodePosition(positions[1]);
                cam.GlobalPosition = target + new Vector3(0, 25, 25);
                cam.LookAt(target);
                await Frames(10);
                Screenshot("decal_preview.png");
                cam.GlobalTransform = camTransform;
            }

            //the gizmo with the axes of a decal point (no height): its horizontal plane moves it, and the next targets get
            //all the axes again
            var gizmo = builder.gizmo;
            gizmo.posAxesAllowed = new[] { true, false, true };
            var nearAxisAllowed = typeof(RuntimeGizmos.TransformGizmo).GetMethod("GetNearAxisAllowed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            bool Allowed(RuntimeGizmos.Axis axis, RuntimeGizmos.Axis plane) {
                gizmo.SetTranslatingAxis(RuntimeGizmos.TransformType.Move, axis, plane);
                return (bool)nearAxisAllowed.Invoke(gizmo, new object[] { gizmo.posAxesAllowed });
            }
            var planes = Allowed(RuntimeGizmos.Axis.Y, RuntimeGizmos.Axis.Y) && !Allowed(RuntimeGizmos.Axis.X, RuntimeGizmos.Axis.X)
                && !Allowed(RuntimeGizmos.Axis.Y, RuntimeGizmos.Axis.None) && Allowed(RuntimeGizmos.Axis.X, RuntimeGizmos.Axis.None);
            gizmo.SetTranslatingAxis(RuntimeGizmos.TransformType.Move, RuntimeGizmos.Axis.None);
            gizmo.ClearTargets();
            Check("the gizmo moves along the allowed axes and planes, and allows all of them after", planes && gizmo.posAxesAllowed[1]);

            //a point moved off the road with the gizmo goes back on it at the end of the move
            var point = (MeshInstance)Operations.Meshes.FindParametricObject(cityProperties, "point", new[] { "decals." + di, "points.1" });
            var moved = GO.GetPosition(point) + Vector3.Up * 20;
            GO.SetPosition(point, moved);
            builder.gizmo.onTransformFinished?.Invoke(new List<Node3D> { point });
            obj = await Call("objects.get", new { @object = PointRef(1) });
            Check("a decal point moved with the gizmo snaps back on the road",
                Mathf.Abs(Vec(obj["localPosition"]).Y - GeometryHelper.ProjectPoint(moved, 1 << 7).Y) < 1e-3f);

        }

        //the decals exported alone (the PATHSET only), with the cable car rail decals of the roads between two intersections
        //(as cable car lines, undone after the export): the names of the paths of decals.pathset, and whether there are rails
        async Task<(List<string> names, bool rails)> ExportDecals(string format) {
            await Call("history.begin", new { label = "cable car rail decals" });
            var rails = false;
            await Call("state.setParam", new { @ref = 0, name = "cableCarRailDecals", value = true });
            foreach (var e in (JArray)await Call("elements.list", new { kind = "road" })) {
                var derived = await Derived(e["id"].Value<int>());
                if (derived["startIntersection"].Type == JTokenType.Null || derived["endIntersection"].Type == JTokenType.Null) continue;
                await Call("state.setParam", new { @ref = e["id"].Value<int>(), name = "cableCarLine", value = true });
                rails = true;
            }
            await Call("history.commit");
            try {
                return (await ExportDecalNames(format), rails);
            } finally {
                await Call("history.undo");
            }
        }

        async Task<List<string>> ExportDecalNames(string format) {
            var flags = new Dictionary<string, bool> { ["propRules"] = false, ["psdl"] = false, ["inst"] = false, ["bai"] = false,
                ["pathset"] = true, ["cinfo_aimap"] = false, ["pkgs"] = false };
            var export = await Send("city.export", new { path = Path.Combine(folder, "..", "api_export_decals"), exporter = format, flags });
            if (export["error"] != null) return null;
            //the files are laid out as in the game folder: the PSDL and its folder in a city folder next to the path
            var reported = export["result"]["path"].Value<string>();
            var path = Path.Combine(Path.GetDirectoryName(reported), "city", Path.GetFileName(reported));
            var decals = Path.Combine(Path.ChangeExtension(path, null), "decals.pathset");
            if (File.Exists(Path.ChangeExtension(path, ".bin")) || !File.Exists(decals)) return null;
            var names = new List<string>();
            using (var r = new BinaryReader(File.OpenRead(decals))) {
                if (new string(r.ReadChars(4)) != "PTH1") return null;
                var n = r.ReadUInt32();
                r.ReadUInt32();
                for (int i = 0; i < n; i++) {
                    names.Add(System.Text.Encoding.ASCII.GetString(r.ReadBytes(32)).TrimEnd('\0'));
                    var points = r.ReadUInt32();
                    r.ReadBytes(4 + 16 * (int)points + 4);
                }
            }
            return names;
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

        // Inheritance between the types of a core

        //the merge rules, on JSON literals
        void TypeMerging() {
            bool Merges(string what, string parent, string child, string expected) {
                var parentToken = JToken.Parse(parent);
                var res = TI.Merge(parentToken, JToken.Parse(child));
                TI.StripMarkers(res);
                var ok = JToken.DeepEquals(res, JToken.Parse(expected)) && JToken.DeepEquals(parentToken, JToken.Parse(parent));
                Check("type merge: " + what + (ok ? "" : " (got " + res.ToString(Newtonsoft.Json.Formatting.None) + ")"), ok);
                return ok;
            }
            Merges("objects are merged, the child wins", "{'a': 1, 'b': {'c': 2, 'd': 3}}", "{'b': {'d': 4}, 'e': 5}", "{'a': 1, 'b': {'c': 2, 'd': 4}, 'e': 5}");
            Merges("$replace replaces an object", "{'b': {'c': 2, 'd': 3}}", "{'b': {'$replace': true, 'd': 4}}", "{'b': {'d': 4}}");
            Merges("a null value is kept", "{'a': 1}", "{'a': null}", "{'a': null}");
            Merges("arrays of values are replaced", "{'v': ['a', 'b', 'c']}", "{'v': ['x']}", "{'v': ['x']}");
            Merges("arrays of objects without names are replaced", "{'l': [{'containerName': 'a'}, {'containerName': 'b'}]}", "{'l': [{'containerName': 'a', 'x': 1}]}",
                "{'l': [{'containerName': 'a', 'x': 1}]}");
            Merges("named elements are merged in place, new ones appended", "[{'name': 'a', 'v': 1}, {'name': 'b', 'v': 2}]", "[{'name': 'a', 'v': 3}, {'name': 'c'}]",
                "[{'name': 'a', 'v': 3}, {'name': 'b', 'v': 2}, {'name': 'c'}]");
            Merges("the container tells apart parameters with the same name, $after takes the container prefix",
                "[{'name': 't', 'type': 'texture'}, {'name': 't', 'container': 'rail', 'type': 'texture'}, {'name': 'w'}]",
                "[{'name': 't', 'container': 'rail', 'label': 'R'}, {'name': 'n', '$after': 'rail_t'}, {'name': 'm', '$after': 't'}]",
                "[{'name': 't', 'type': 'texture'}, {'name': 'm'}, {'name': 't', 'container': 'rail', 'type': 'texture', 'label': 'R'}, {'name': 'n'}, {'name': 'w'}]");
            Merges("fromStart tells apart imported parameters (missing is false)",
                "[{'name': 'x', 'fromStart': true, 'newName': 'A'}, {'name': 'x', 'fromStart': false, 'newName': 'B'}]", "[{'name': 'x', 'newName': 'C'}]",
                "[{'name': 'x', 'fromStart': true, 'newName': 'A'}, {'name': 'x', 'fromStart': false, 'newName': 'C'}]");
            Merges("$remove and $before", "[{'name': 'a'}, {'name': 'b'}, {'name': 'c'}]", "[{'name': 'b', '$remove': true}, {'name': 'c', '$before': 'a'}]",
                "[{'name': 'c'}, {'name': 'a'}]");
            Merges("$after moves an inherited element", "[{'name': 'a'}, {'name': 'b'}, {'name': 'c'}]", "[{'name': 'a', '$after': 'c'}]",
                "[{'name': 'b'}, {'name': 'c'}, {'name': 'a'}]");
            Merges("tabs (by label) and their elements are merged",
                "{'tabs': [{'label': 'T1', 'elements': [{'name': 'a', 'width': 1}, {'name': 'b', 'width': 1}]}, {'label': 'T2', 'elements': [{'name': 'c', 'width': 1}]}]}",
                "{'tabs': [{'label': 'T1', 'elements': [{'name': 'x', 'width': 0.5, '$after': 'a'}, {'name': 'b', 'width': 2}]}]}",
                "{'tabs': [{'label': 'T1', 'elements': [{'name': 'a', 'width': 1}, {'name': 'x', 'width': 0.5}, {'name': 'b', 'width': 2}]}, {'label': 'T2', 'elements': [{'name': 'c', 'width': 1}]}]}");
            Merges("late definitions are merged by category", "[{'category': 'c1', 'definitions': [{'name': 'a', 'value': '1'}]}]",
                "[{'category': 'c1', 'definitions': [{'name': 'a', 'value': '2'}]}]", "[{'category': 'c1', 'definitions': [{'name': 'a', 'value': '2'}]}]");
            Merges("an inherited array with repeated names is replaced", "[{'name': 'a', 'v': 1}, {'name': 'a', 'v': 2}]", "[{'name': 'a', 'v': 3}]", "[{'name': 'a', 'v': 3}]");
            bool Throws(string parent, string child) {
                try {
                    TI.Merge(JToken.Parse(parent), JToken.Parse(child));
                    return false;
                } catch (InvalidDataException e) {
                    Log("     " + e.Message);
                    return true;
                }
            }
            Check("type merge: $after an element that is not there is an error", Throws("[{'name': 'a'}]", "[{'name': 'b', '$after': 'z'}]"));
            Check("type merge: $after an ambiguous element is an error", Throws("[{'name': 'x', 'fromStart': true}, {'name': 'x'}]", "[{'name': 'y', '$after': 'x'}]"));
        }

        static void CopyFolder(string from, string to) {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        static string CoreElementsDir() {
            return Path.Combine(PathHelper.BasePath(), "Files", "cores", "MidtownMadness2", "elements");
        }

        static bool SameJson(object a, object b) {
            if (a == null || b == null) return a == b;
            return JToken.DeepEquals(JToken.FromObject(a), JToken.FromObject(b));
        }

        //types loaded from a scratch elements folder, made of copies of the types of the core and of types extending them
        void InheritedTypeFiles() {
            var core = CoreElementsDir();
            var dir = Path.Combine(folder + "_types", "elements");
            if (Directory.Exists(folder + "_types")) Directory.Delete(folder + "_types", true);
            foreach (var sub in new[] { "roads/psdl", "intersections/psdl", "sharedComponents", "props/elements" }) {
                CopyFolder(Path.Combine(core, sub), Path.Combine(dir, sub));
            }
            void Write(string path, string json) {
                var full = Path.Combine(dir, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, json.Replace('\'', '"'));
            }
            Write("roads/child/type.json", "{'extends': 'psdl'}");
            Write("roads/base/type.json", "{'extends': 'psdl', 'abstract': true}");
            Write("roads/child2/type.json", "{'extends': 'base'}");
            Write("roads/child2/settings.json", "{'components': [{'name': 'singleRoad', 'condition': 'false'}]}");
            Write("roads/child2/parameters.json", "{'parameters': [{'name': 'tunnel', 'label': 'RD_TUNNEL', 'type': 'bool', '$after': 'echo'}]}");
            Write("roads/child2/ui.json", "{'tabs': [{'label': 'RD_TAB_MAIN', 'elements': [{'name': 'doNotExport', '$remove': true}]}]}");
            Write("roads/child2/components/singleRoad.json", "{'mainMesh': {'uvs': ['vec2(zRd, 0)', 'vec2(zRd, 2)']}}");
            Write("intersections/child/type.json", "{'extends': 'psdl'}");
            Write("props/elements/narrow.json", "{'extends': 'psdl', 'label': 'PROP_TYPE_NARROW'}");
            Write("props/elements/hidden.json", "{'extends': 'psdl', 'abstract': true}");

            var ancestries = new Dictionary<string, string[]>();
            var roads = TP.LoadRoadTypes(dir, ancestries);
            Check("inherited road types: the abstract type is not listed (" + string.Join(", ", roads.Keys) + ")",
                roads.ContainsKey("child") && roads.ContainsKey("child2") && !roads.ContainsKey("base"));
            Check("inherited road types: the ancestries (" + string.Join(" -> ", ancestries["child2"]) + ")",
                ancestries["child"].SequenceEqual(new[] { "child", "psdl" }) && ancestries["child2"].SequenceEqual(new[] { "child2", "base", "psdl" })
                && ancestries["psdl"].SequenceEqual(new[] { "psdl" }));
            var psdl = roads["psdl"].typeData;
            Check("inherited road types: a type with only type.json is its parent", SameJson(roads["child"].typeData, psdl));

            var child2 = roads["child2"].typeData;
            var expectedSettings = JToken.FromObject(psdl.settings);
            expectedSettings["components"].First(c => c["name"].Value<string>() == "singleRoad")["condition"] = "false";
            Check("inherited road types: settings merged (component condition overridden)", JToken.DeepEquals(JToken.FromObject(child2.settings), expectedSettings));
            var names = psdl.parameters.parameters.Select(p => p.fullName()).ToList();
            names.Insert(names.IndexOf("echo") + 1, "tunnel");
            Check("inherited road types: a parameter added after another one", child2.parameters.parameters.Select(p => p.fullName()).SequenceEqual(names)
                && SameJson(child2.parameters.parameters.Where(p => p.name != "tunnel").ToArray(), psdl.parameters.parameters)
                && SameJson(child2.parameters.staticDefinitions, psdl.parameters.staticDefinitions));
            var tabElements = psdl.ui.tabs[0].elements.Select(e => e.name).Where(n => n != "doNotExport");
            Check("inherited road types: a UI element removed", child2.ui.tabs[0].elements.Select(e => e.name).SequenceEqual(tabElements)
                && child2.ui.tabs.Length == psdl.ui.tabs.Length && SameJson(child2.ui.tabs[1], psdl.ui.tabs[1]));
            var expectedComponents = JToken.FromObject(psdl.components);
            var single = System.Array.FindIndex(psdl.settings.components, c => c.name == "singleRoad");
            expectedComponents[single]["mainMesh"]["uvs"] = new JArray("vec2(zRd, 0)", "vec2(zRd, 2)");
            Check("inherited road types: a component merged over the inherited one", JToken.DeepEquals(JToken.FromObject(child2.components), expectedComponents));

            var intersections = TP.LoadIntersectionTypes(dir);
            Check("inherited intersection type: same junctions and components as its parent", intersections.ContainsKey("child")
                && intersections["child"].typeData.junctions.Keys.SequenceEqual(intersections["psdl"].typeData.junctions.Keys)
                && SameJson(intersections["child"].typeData, intersections["psdl"].typeData));

            var props = TP.LoadPropsElementTypes(dir);
            Check("inherited props element: label overridden, parameters inherited, abstract one not listed", props.ContainsKey("narrow") && !props.ContainsKey("hidden")
                && props["narrow"].typeData.label == "PROP_TYPE_NARROW" && SameJson(props["narrow"].typeData.parametersInfo, props["psdl"].typeData.parametersInfo));

            var bad = Path.Combine(folder + "_types", "bad");
            void WriteBad(string path, string json) {
                var full = Path.Combine(bad, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, json.Replace('\'', '"'));
            }
            WriteBad("roads/a/type.json", "{'extends': 'b'}");
            WriteBad("roads/b/type.json", "{'extends': 'a'}");
            WriteBad("roads/orphan/type.json", "{'extends': 'nope'}");
            WriteBad("roads/escape/type.json", "{'extends': '../a'}");
            WriteBad("props/x.json", "{'extends': 'y'}");
            WriteBad("props/y.json", "{'extends': 'x'}");
            bool Throws(System.Action action) {
                try {
                    action();
                    return false;
                } catch (InvalidDataException e) {
                    Log("     " + e.Message);
                    return true;
                }
            }
            Check("inheritance cycle is an error", Throws(() => TI.FolderAncestry(Path.Combine(bad, "roads", "a"))));
            Check("extending a missing type is an error", Throws(() => TI.FolderAncestry(Path.Combine(bad, "roads", "orphan"))));
            Check("extending a type outside the category is an error", Throws(() => TI.FolderAncestry(Path.Combine(bad, "roads", "escape"))));
            Check("inheritance cycle of single file types is an error", Throws(() => TI.SingleFileAncestry(Path.Combine(bad, "props", "x.json"))));
        }

        //a road type of the core extending another one, in a city
        async Task InheritedTypes() {
            var typeDir = Path.Combine(CoreElementsDir(), "roads", "zzInheritTest");
            var steps = 0;
            try {
                Directory.CreateDirectory(typeDir);
                File.WriteAllText(Path.Combine(typeDir, "type.json"), "{\"extends\": \"psdl\"}");
                TP.GetRoadTypes(true);
                var types = (JArray)await Call("types.list", new { kind = "road" });
                var listed = types.FirstOrDefault(t => t["name"].Value<string>() == "zzInheritTest");
                Check("types.list gives the ancestry of an inherited type", listed != null && listed["ancestry"].ToObject<string[]>().SequenceEqual(new[] { "zzInheritTest", "psdl" })
                    && types.First(t => t["name"].Value<string>() == "psdl")["ancestry"].ToObject<string[]>().SequenceEqual(new[] { "psdl" }));

                var road = (await Call("roads.create", new { points = new[] { Pos(600, -600), Pos(700, -650), Pos(800, -600) }, type = "psdl" })).Value<int>();
                steps++;
                var parentMesh = (await Call("elements.geometry", new { @ref = road }))["element"]["data"]["mesh"];
                await Call("state.setType", new { @ref = road, type = "zzInheritTest" });
                steps++;
                var childMesh = (await Call("elements.geometry", new { @ref = road }))["element"]["data"]["mesh"];
                Check("a road of a type extending psdl has the geometry of a psdl road", parentMesh.Type != JTokenType.Null && JToken.DeepEquals(parentMesh, childMesh));

                var other = (await Call("roads.create", new { points = new object[] { Pos(700, -400), Pos(780, -500), new { roadEnd = new { road, end = "end" } } }, type = "psdl" })).Value<int>();
                steps++;
                var crossing = (await Derived(other))["endIntersection"];
                Check("an intersection joins the inherited type and psdl", crossing.Type != JTokenType.Null
                    && (await Call("elements.geometry", new { @ref = crossing.Value<int>() }))["element"]["data"]["mesh"].Type != JTokenType.Null);
                var intersectionType = TP.GetIntersectionTypes()["psdl"];
                var psdlJunction = intersectionType.junctionTypes[("psdl", "psdl")];
                Check("the junction of the inherited type is the one of its parent", intersectionType.GetJunctionType("zzInheritTest", "psdl") == psdlJunction
                    && intersectionType.GetJunctionType("psdl", "zzInheritTest") == psdlJunction && intersectionType.GetJunctionType("zzInheritTest", "zzInheritTest") == psdlJunction);

                var exportJson = Path.Combine(folder, "..", "api_export_inherit.json");
                await Call("city.exportJson", new { path = exportJson });
                var exported = JObject.Parse(File.ReadAllText(exportJson));
                var entry = ((JArray)exported["typeAncestry"])?.FirstOrDefault(t => t["category"].Value<string>() == "roads" && t["name"].Value<string>() == "zzInheritTest");
                Check("the export JSON has the ancestry of the inherited type", entry != null && entry["ancestry"].ToObject<string[]>().SequenceEqual(new[] { "zzInheritTest", "psdl" }));
                File.Delete(exportJson);
            } finally {
                for (int i = 0; i < steps; i++) await Call("history.undo");
                if (Directory.Exists(typeDir)) Directory.Delete(typeDir, true);
                TP.GetRoadTypes(true);
            }
            Check("the type is gone after reloading", !TP.GetRoadTypes().ContainsKey("zzInheritTest"));
        }

        //the low-level elements of the core (MM2: the PSDL attributes): creation, control points, constraints, containers,
        //conversions, ghosts, undo, save and load, and their editor
        async Task LowLevel() {
            var types = (JArray)await Call("types.list", new { kind = "lowLevel" });
            Check("the core has low-level types (" + types.Count + ")", types.Count >= 18);
            var converters = (JArray)await Call("converters.list");
            Check("the core has the toPsdl converter", converters.Any(c => c["name"].Value<string>() == "toPsdl"));
            async Task<JToken> Derived(int id) => (await Call("elements.get", new { @ref = id, derived = true }))["derived"];
            Vector3 Pos(JToken p) => new Vector3(p["position"][0].Value<float>(), p["position"][1].Value<float>(), p["position"][2].Value<float>());

            //a facade: its corners stay a vertical rectangle
            var facade = (await Call("lowLevel.create", new { type = "facade", position = new[] { 900, 0, 900 } })).Value<int>();
            var d = await Derived(facade);
            Check("a new facade has its four corners", d["paths"].Count() == 4);
            await CheckUndo("moving a corner of a facade", async () => {
                var tl = Pos(d["points"]["topLeft"]);
                await Call("lowLevel.movePoint", new { element = facade, path = "topLeft", position = new[] { tl.X + 1, tl.Y + 2, tl.Z } });
                await Frames(2);
            });
            d = await Derived(facade);
            var p = new Dictionary<string, Vector3>();
            foreach (var name in new[] { "bottomLeft", "bottomRight", "topLeft", "topRight" }) p[name] = Pos(d["points"][name]);
            Check("the constraints keep the facade rectangular", Mathf.Abs(p["topLeft"].X - p["bottomLeft"].X) < 1e-3 && Mathf.Abs(p["topRight"].Y - p["topLeft"].Y) < 1e-3);

            //arrays: a road with sidewalks gets and loses sections, not below its minimum
            var road = (await Call("lowLevel.create", new { type = "roadWithSidewalks", position = new[] { 930, 0, 900 } })).Value<int>();
            await CheckUndo("inserting a section", async () => await Call("lowLevel.insert", new { element = road, array = "sections", index = 1 }));
            Check("the road has three sections", (await Derived(road))["points"]["sections"].Count() == 3);
            await Call("lowLevel.remove", new { element = road, array = "sections", index = 1 });
            await Expect("removing a section below the minimum", "invalid_operation", "lowLevel.remove", new { element = road, array = "sections", index = 0 });
            //uniform arrays: a lane added to a section of a BAI road is added to all of them
            var bai = (await Call("lowLevel.create", new { type = "baiRoad", position = new[] { 960, 0, 900 } })).Value<int>();
            await Call("lowLevel.insert", new { element = bai, array = "sections.0.forwardLanes" });
            var sections = (await Derived(bai))["points"]["sections"];
            Check("a lane is added to every section of a uniform array", sections.All(sec => sec["forwardLanes"].Count() == 2));

            //containers: a facade in a group of building parts, the group merged into a room
            var room = (await Call("lowLevel.create", new { type = "room", position = new[] { 900, 0, 930 } })).Value<int>();
            var group = (await Call("lowLevel.create", new { type = "buildingGroup", position = new[] { 900, 0, 930 } })).Value<int>();
            await Call("lowLevel.setContainer", new { element = facade, container = group });
            await Expect("a road in a group of building parts", "invalid_operation", "lowLevel.setContainer", new { element = road, container = group });
            await CheckUndo("merging a group into a room", async () => await Call("lowLevel.merge", new { from = group, into = room }));
            Check("the facade is in the room after the merge", (await Derived(facade))["container"]?.Value<int>() == room);

            //mesh types and the conversion between them (Python)
            var fan = (await Call("lowLevel.create", new {
                type = "triangleFan", points = new JObject(),
                mesh = new { vertices = new object[] { new { position = new[] { 900, 0, 960 } }, new { position = new[] { 910, 0, 960 } }, new { position = new[] { 910, 1, 970 } } }, triangles = new[] { 0, 1, 2 } }
            })).Value<int>();
            var converted = (JArray)await Call("elements.convert", new { elements = new[] { fan }, converter = "toRoofTriangleFan" });
            var roof = converted.Count == 1 ? converted[0].Value<int>() : -1;
            var roofDerived = roof >= 0 ? await Derived(roof) : null;
            Check("a triangle fan converted to a roof triangle fan", roofDerived != null && roofDerived["type"].Value<string>() == "roofTriangleFan"
                && roofDerived["vertices"].Count() == 3 && History.UndoManager.GetAliveElement(fan) == null);
            Check("the roof is flat", roofDerived != null && roofDerived["vertices"].All(v => Mathf.Abs(Pos(v).Y - 1) < 1e-3));

            //a road of the city converted: it becomes a ghost replaced by a room with its PSDL attributes and BAI road
            var cityRoad = ((JArray)await Call("elements.list", new { kind = "road" })).First(r => History.UndoManager.GetAliveElement(r["id"].Value<int>()) is Road rd
                && rd.startIntersection != null && rd.endIntersection != null && rd.state.Str("type", "") == "psdl")["id"].Value<int>();
            var before = await Snapshot();
            var created = (JArray)await Call("elements.convert", new { elements = new[] { cityRoad }, converter = "toPsdl" });
            var ghosts = (JArray)await Call("ghosts.list");
            Check("the converted road is a ghost", ghosts.Any(g => g["id"].Value<int>() == cityRoad));
            var createdTypes = new HashSet<string>();
            foreach (var id in created) createdTypes.Add((await Derived(id.Value<int>()))["type"].Value<string>());
            Check("the road is converted to a room, a road attribute and a BAI road (" + string.Join(", ", createdTypes) + ")",
                createdTypes.Contains("room") && createdTypes.Contains("baiRoad") && createdTypes.Count >= 3);

            //the points of the road ends are on the anchors across them (endAnchors of the road type), and follow the intersection
            var cityRoadElem = History.UndoManager.GetAliveElement(cityRoad) as Road;
            var endIntersections = new[] { cityRoadElem.startIntersection, cityRoadElem.endIntersection }.Select(i => History.ElementRegistry.GetId(i)).ToList();
            async Task<List<Vector3>> RoadEndAnchors() => ((JArray)await Call("anchors.list", new { lowLevel = true }))
                .Where(a => a["lowLevelOnly"].Value<bool>() && endIntersections.Contains(a["of"].Value<int>())).Select(a => Pos(a)).ToList();
            async Task<List<(Vector3 pos, bool anchored)>> CreatedRoadPoints() {
                var res = new List<(Vector3, bool)>();
                foreach (var id in created) {
                    var der = await Derived(id.Value<int>());
                    if (der["points"]?["sections"] is not JArray sections || der["type"].Value<string>() == "baiRoad") continue;
                    foreach (var s in sections) foreach (var p in ((JObject)s).Properties()) {
                        if (p.Value is JObject o && o["position"] != null) res.Add((Pos(o), o["anchored"].Value<bool>()));
                    }
                }
                return res;
            }
            List<(Vector3 pos, bool anchored)> OnRoadEndAnchors(List<Vector3> anchors, List<(Vector3 pos, bool anchored)> points)
                => points.Where(p => anchors.Any(a => a.DistanceTo(p.pos) < 0.01f)).ToList();
            var onEnds = OnRoadEndAnchors(await RoadEndAnchors(), await CreatedRoadPoints());
            Check("anchors.list lists the road end anchors only with lowLevel", ((JArray)await Call("anchors.list")).All(x => !x["lowLevelOnly"].Value<bool>()));
            Check("the road end points of the converted road are on the road end anchors (" + onEnds.Count + ")", onEnds.Count >= 8 && onEnds.All(p => p.anchored));
            var movedIntersection = GO.GetPosition(cityRoadElem.startIntersection.point) + new Vector3(4, 0, 3);
            await Call("intersections.move", new { intersection = endIntersections[0], position = new[] { movedIntersection.X, movedIntersection.Y, movedIntersection.Z } });
            await Frames(2);
            var onEndsMoved = OnRoadEndAnchors(await RoadEndAnchors(), await CreatedRoadPoints());
            Check("the road end points follow the intersection moved (" + onEndsMoved.Count + ")", onEndsMoved.Count == onEnds.Count
                && onEndsMoved.Count(p => !onEnds.Any(q => q.pos.DistanceTo(p.pos) < 0.01f)) >= 4);
            await Call("history.undo");

            await Call("history.undo");
            Same("undoing the conversion", before, await Snapshot());
            await Call("history.redo");
            Check("redoing the conversion", ((JArray)await Call("ghosts.list")).Count == ghosts.Count);

            //save and load (the ids change: the elements are compared by name)
            async Task<List<string>> Described() {
                var names = new Dictionary<int, string>();
                foreach (var e in (JArray)await Call("elements.list")) names[e["id"].Value<int>()] = e["name"].Value<string>();
                string Name(JToken id) => id == null || id.Type == JTokenType.Null ? "-" : names.TryGetValue(id.Value<int>(), out var n) ? n : "?";
                string Positions(JToken t) {
                    if (t is JObject o && o["position"] != null) return string.Join(",", o["position"].Select(c => System.Math.Round(c.Value<float>(), 2)));
                    if (t is JObject g) return "{" + string.Join(";", g.Properties().Select(x => x.Name + ":" + Positions(x.Value))) + "}";
                    if (t is JArray arr) return "[" + string.Join(";", arr.Select(Positions)) + "]";
                    return "";
                }
                var res = new List<string>();
                foreach (var e in (JArray)await Call("elements.list", new { kind = "lowLevel" })) {
                    var id = e["id"].Value<int>();
                    var data = (await Call("elements.get", new { @ref = id }))["data"];
                    var der = await Derived(id);
                    var state = (JObject)data["state"].DeepClone();
                    foreach (var prop in state.Properties().ToList()) {
                        if (prop.Value is JObject r && r["kind"] != null && r["id"] != null) prop.Value = Name(r["id"]);
                    }
                    res.Add(e["name"] + " " + der["type"] + " in " + Name(der["container"]) + " from " + Name(der["source"]) + " " + state.ToString(Newtonsoft.Json.Formatting.None)
                        + " " + Positions(der["points"]) + " " + string.Join(";", der["vertices"].Select(Positions)) + " " + string.Join(",", der["triangles"]));
                }
                foreach (var g in (JArray)await Call("ghosts.list")) res.Add("ghost " + g["name"]);
                res.Sort(System.StringComparer.Ordinal);
                return res;
            }
            var saved = await Described();
            await Call("city.save");
            await Call("city.reload");
            var loaded = await Described();
            var missing = saved.Except(loaded).ToList();
            Check("the low-level elements and ghosts are saved and loaded (" + saved.Count + ")", missing.Count == 0 && saved.Count == loaded.Count);
            foreach (var m in missing.Take(4)) Log("      " + m.Substring(0, System.Math.Min(300, m.Length)));
            cityRoad = ((JArray)await Call("ghosts.list")).First()["id"].Value<int>();
            road = ((JArray)await Call("elements.list", new { kind = "lowLevel", type = "roadWithSidewalks" })).Last()["id"].Value<int>();

            //the editor of a low-level element, and the creation from the menu
            var element = History.UndoManager.GetAliveElement(road) as LowLevelElement;
            builder.SelectObject(element, true, null);
            await Frames(3);
            Check("the low-level editor is open", CallBuilder<EditorPanel>("FindShownPanel") == builder.lowLevelEditorPanel);
            Screenshot("lowlevel_editor.png");
            builder.CloseAllPanels();
            await Frames(2);
            History.UndoManager.Record("create", () => CallBuilder<object>("CreateLowLevelElement"));
            await Frames(3);
            Check("Create > Low-level element opens the editor to choose the type", CallBuilder<EditorPanel>("FindShownPanel") == builder.lowLevelEditorPanel
                && builder.lowLevelPlacer.placementMode == ElementPlacer.LowLevelPlacer.PlacementMode.Create);
            Screenshot("lowlevel_create.png");
            builder.CloseAllPanels();
            await Frames(2);

            //the ghost brought back
            await Call("ghosts.restore", new { element = cityRoad });
            Check("the restored road is not a ghost any more", ((JArray)await Call("ghosts.list")).All(g => g["id"].Value<int>() != cityRoad));

            //a point on an anchor leaves it when moved, by the API and with the gizmo
            var anchorRef = ((JArray)await Call("anchors.list")).First(a => !Operations.LowLevel.IsGhost(History.UndoManager.GetAliveElement(a["of"].Value<int>())));
            var anchoredFan = (await Call("lowLevel.create", new {
                type = "triangleFan", points = new JObject(),
                mesh = new { vertices = new object[] { new { position = new[] { 900, 0, 990 } }, new { position = new[] { 910, 0, 990 } }, new { position = new[] { 910, 0, 1000 } } }, triangles = new[] { 0, 1, 2 } }
            })).Value<int>();
            async Task<TerrainPoint> Vertex(int index) => History.UndoManager.GetAliveElement((await Derived(anchoredFan))["vertices"][index]["id"].Value<int>()) as TerrainPoint;
            async Task<TerrainPoint> AttachVertex(int index) {
                await Call("lowLevel.attachPoint", new { element = anchoredFan, vertex = index, to = new { anchor = new { of = anchorRef["of"], index = anchorRef["index"] } } });
                await Frames(2);
                return await Vertex(index);
            }
            var anchoredPoint = await AttachVertex(0);
            Check("a vertex attached to an anchor", anchoredPoint != null && anchoredPoint.anchor is TerrainAnchor);
            var target = anchoredPoint.GetPoint() + new Vector3(3, 0, 3);
            await CheckUndo("moving a vertex on an anchor", async () => {
                await Call("lowLevel.movePoint", new { element = anchoredFan, vertex = 0, position = new[] { target.X, target.Y, target.Z } });
                await Frames(2);
            });
            anchoredPoint = await Vertex(0);
            Check("the vertex moved by the API leaves the anchor", anchoredPoint.anchor == null && anchoredPoint.GetPoint().DistanceTo(target) < 1e-3);
            anchoredPoint = await AttachVertex(0);
            builder.SelectObject(History.UndoManager.GetAliveElement(anchoredFan) as LowLevelElement, true, null);
            await Frames(3);
            builder.gizmo.ClearTargets();
            builder.gizmo.AddTarget(anchoredPoint.GetObject());
            History.UndoManager.Record("drag", () => {
                History.UndoManager.TouchOwner(anchoredPoint.GetObject());
                GameEngine.GameObject.SetPosition(anchoredPoint.GetObject(), target);
                builder.helper.elementManager.FlagAsChanged();
            });
            await Frames(3);
            Check("the vertex moved with the gizmo leaves the anchor", anchoredPoint.anchor == null && anchoredPoint.GetPoint().DistanceTo(target) < 1e-3);
            builder.CloseAllPanels();
            await Frames(2);

            //the editor of a room (no points group) opened from the editor of an element in it
            var roomElement = History.UndoManager.GetAliveElement(((JArray)await Call("elements.list", new { kind = "lowLevel", type = "room" })).First()["id"].Value<int>()) as LowLevelElement;
            var inRoom = builder.helper.elementManager.GetLowLevelChildren(roomElement).First();
            //a second content to move the highlight to (deleted after)
            var extraContent = History.UndoManager.GetAliveElement((await Call("lowLevel.create", new {
                type = "dividedRoad", position = GameEngine.Vector3.EncodePosition(inRoom.GetAllPoints()[0].GetPoint()),
                container = History.ElementRegistry.GetId(roomElement) })).Value<int>()) as LowLevelElement;
            await Frames(2);
            builder.SelectObject(inRoom, true, null);
            await Frames(3);
            builder.lowLevelEditorPanel.GetType().GetMethod("EditContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(builder.lowLevelEditorPanel, null);
            await Frames(3);
            string frameError = null;
            try {
                builder.lowLevelEditorPanel.Update(); //shows the selected point again (in a field freed by the rebuild before the fix)
            } catch (System.Exception e) {
                frameError = e.GetType().Name;
            }
            Check("the editor of the container opens from an element in it" + (frameError != null ? " (" + frameError + ")" : ""),
                CallBuilder<EditorPanel>("FindShownPanel") == builder.lowLevelEditorPanel && builder.lowLevelEditorPanel.GetElement() == roomElement && frameError == null);
            //the content picked in the contents dropdown is outlined (on a layer of its own) with its points selected
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var contentsDropdown = builder.lowLevelEditorPanel.GetType().GetField("contentsDropdown", flags).GetValue(builder.lowLevelEditorPanel) as EditorPanelElements.Dropdown;
            var contentsOption = typeof(EditorPanelElements.Dropdown).GetField("dropdown", flags).GetValue(contentsDropdown) as OptionButton;
            var roomContents = builder.helper.elementManager.GetLowLevelChildren(roomElement);
            const uint contentLayer = 1u << 16;
            bool Outlined(LowLevelElement e) {
                var renderers = RuntimeGizmos.SelectionOutline.GetRenderers(e.generator);
                return renderers.Count > 0 && renderers.All(r => !r.Visible || (r.Layers & contentLayer) != 0);
            }
            bool NoneOutlined() => roomContents.All(c => RuntimeGizmos.SelectionOutline.GetRenderers(c.generator).All(r => (r.Layers & contentLayer) == 0));
            bool HighlightOnly(LowLevelElement picked) => Outlined(picked) && picked.IsSelected()
                && roomContents.Where(c => c != picked).All(c => !c.IsSelected() && !Outlined(c));
            Check("nothing in the room is highlighted before a content is picked", NoneOutlined() && roomContents.All(c => !c.IsSelected()));
            contentsOption.EmitSignal(OptionButton.SignalName.ItemSelected, 0); //the first one, shown already
            await Frames(2);
            var firstHighlighted = HighlightOnly(roomContents[0]);
            async Task HighlightScreenshot(string name) {
                if (DisplayServer.GetName() == "headless") return;
                //all the contents in view
                var points = roomContents.SelectMany(e => e.GetAllPoints()).Select(p => p.GetPoint()).ToList();
                var bounds = new Aabb(points[0], Vector3.Zero);
                foreach (var p in points) bounds = bounds.Expand(p);
                var center = bounds.GetCenter();
                (builder.GetViewport().GetCamera3D() as CameraController)?.SetView(center + new Vector3(0, 0.8f, 0.6f) * Mathf.Max(bounds.Size.Length(), 10), center);
                await Frames(3);
                Screenshot(name);
            }
            await HighlightScreenshot("contents_highlight_1.png");
            var secondHighlighted = roomContents.Count > 1;
            if (roomContents.Count > 1) {
                contentsOption.Select(1);
                contentsOption.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
                await Frames(2);
                secondHighlighted = HighlightOnly(roomContents[1]);
                await HighlightScreenshot("contents_highlight_2.png");
            }
            Check("picking a content in the contents dropdown highlights it (first " + firstHighlighted + ", second of " + roomContents.Count + " " + secondHighlighted + ")",
                firstHighlighted && secondHighlighted);
            builder.CloseAllPanels();
            await Frames(2);
            Check("the highlight is removed when the editor is closed", NoneOutlined() && roomContents.All(c => !c.IsSelected())
                && !builder.gizmo.GetNode<Node3D>("ContentOutline").GetChildren().OfType<MeshInstance3D>().Any(q => q.Visible));
            Operations.LowLevel.Delete(M, extraContent);
            await Frames(2);
        }

        //sections added where clicked, dividers, room perimeters, prop rules and the anchors between points
        async Task LowLevelEditing() {
            async Task<JToken> Derived(int id) => (await Call("elements.get", new { @ref = id, derived = true }))["derived"];
            Vector3 Pos(JToken p) => new Vector3(p["position"][0].Value<float>(), p["position"][1].Value<float>(), p["position"][2].Value<float>());
            float[] A(Vector3 v) => new[] { v.X, v.Y, v.Z };
            LowLevelElement Element(int id) => History.UndoManager.GetAliveElement(id) as LowLevelElement;
            int Triangles(LowLevelElement e) {
                var res = 0;
                for (int i = 0; i < GameEngine.Mesh.GetSubmeshCount(e.generator.m); i++) res += GameEngine.Mesh.GetIndices(e.generator.m, i).Length / 3;
                return res;
            }

            //sections: added at the end and at the start of a road where clicked, turned to continue it
            var road = (await Call("lowLevel.create", new { type = "roadWithSidewalks", position = new[] { 1000, 0, 1200 } })).Value<int>();
            Vector3 Center(JToken section) {
                var c = Vector3.Zero;
                var n = 0;
                foreach (var prop in ((JObject)section).Properties()) {
                    c += Pos(prop.Value);
                    n++;
                }
                return c / n;
            }
            var secs = (await Derived(road))["points"]["sections"];
            var width = Pos(secs[0]["leftSidewalk"]).DistanceTo(Pos(secs[0]["rightSidewalk"]));
            var endTarget = Center(secs[1]) + new Vector3(10, 0, 10);
            await CheckUndo("adding a section where clicked", async () => await Call("lowLevel.insert", new { element = road, array = "sections", position = A(endTarget) }));
            secs = (await Derived(road))["points"]["sections"];
            var across = Pos(secs[2]["rightSidewalk"]) - Pos(secs[2]["leftSidewalk"]);
            Check("a section added at the end is centered where clicked", secs.Count() == 3 && Center(secs[2]).DistanceTo(endTarget) < 1e-3);
            Check("the section added keeps its width and is turned with the road (" + across + ")", Mathf.Abs(across.Length() - width) < 1e-3
                && Mathf.Abs(across.Normalized().Dot((endTarget - Center(secs[1])).Normalized())) < 1e-3);
            var startTarget = Center(secs[0]) - new Vector3(0, 0, 10);
            await Call("lowLevel.insert", new { element = road, array = "sections", index = 0, position = A(startTarget) });
            secs = (await Derived(road))["points"]["sections"];
            Check("a section added at the start is centered where clicked", secs.Count() == 4 && Center(secs[0]).DistanceTo(startTarget) < 1e-3);
            await Expect("a section added at a position in the middle", "invalid_param", "lowLevel.insert", new { element = road, array = "sections", index = 2, position = A(startTarget) });
            //the points added can be moved with the gizmo (on the moveable layer while the element is edited)
            var roadElement = Element(road);
            roadElement.SetActive(true, true);
            Operations.LowLevel.InsertArrayElement(roadElement, "sections", 1);
            var layers = string.Join(",", roadElement.GetAllPoints().Select(p => GO.GetLayer(p.GetObject())).Distinct());
            Check("the points added while editing can be moved (layers " + layers + ")", roadElement.GetAllPoints().All(p => GO.GetLayer(p.GetObject()) == 8));
            roadElement.SetActive(false, false);
            //the panel names the array of the selected point in its buttons
            builder.SelectObject(roadElement, true, null);
            await Frames(3);
            builder.gizmo.ClearTargets();
            builder.gizmo.AddTarget(roadElement.GetAllPoints()[0].GetObject());
            builder.lowLevelEditorPanel.Update();
            var insertButton = builder.lowLevelEditorPanel.GetType().GetField("insertBeforeButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(builder.lowLevelEditorPanel) as EditorPanelElements.Button;
            Check("the insert buttons name the array of the selected point", insertButton != null && insertButton.GetInteractable());
            Screenshot("lowlevel_sections.png");
            builder.CloseAllPanels();
            await Frames(2);

            //arrays in arrays (the lanes of the sections of a BAI road): Insert, Remove and Delete act on the sections, the
            //inner buttons on the lanes, and a lane point added by click goes in every section
            var bai = (await Call("lowLevel.create", new { type = "baiRoad", position = new[] { 1200, 0, 1400 } })).Value<int>();
            async Task<string> Counts() {
                var s = (await Derived(bai))["points"]["sections"];
                return string.Join(",", s.Count(), s.Min(x => x["forwardLanes"].Count()), s.Max(x => x["forwardLanes"].Count()),
                    s.Min(x => x["backwardLanes"].Count()), s.Max(x => x["backwardLanes"].Count()));
            }
            await Call("lowLevel.insert", new { element = bai, array = "sections" });
            await CheckUndo("removing a lane point", async () => await Call("lowLevel.removePoint", new { element = bai, path = "sections.2.forwardLanes.0" }));
            var counts = await Counts();
            Check("removing a lane point removes its section (sections, forward and backward lanes: " + counts + ")", counts == "2,1,1,1,1");
            await Expect("removing a lane point with the sections at their minimum", "invalid_operation", "lowLevel.removePoint", new { element = bai, path = "sections.0.forwardLanes.0" });
            builder.SelectObject(Element(bai), true, null);
            await Frames(3);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Godot.Button PanelButton(string name) => typeof(EditorPanelElements.Button).GetField("button", flags)
                .GetValue(builder.lowLevelEditorPanel.GetType().GetField(name, flags).GetValue(builder.lowLevelEditorPanel)) as Godot.Button;
            void SelectPoint(string path) {
                builder.gizmo.ClearTargets();
                builder.gizmo.AddTarget(((global::LowLevel.PointLeaf)global::LowLevel.PointTree.Find(Element(bai).points, path)).point.GetObject());
                builder.lowLevelEditorPanel.Update();
            }
            async Task Press(string button) {
                SelectPoint("sections.0.forwardLanes.0");
                PanelButton(button).EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
            }
            SelectPoint("sections.0.forwardLanes.0");
            await Frames(2);
            Screenshot("lowlevel_bai_lanes.png");
            var sectionsLabel = StringManager.Get("LL_SECTIONS").ToLowerInvariant();
            var lanesLabel = StringManager.Get("LL_BAI_FORWARD_LANES").ToLowerInvariant();
            Check("with a lane point selected, Insert and Remove name the sections and the inner buttons the lanes (" + PanelButton("insertAfterButton").Text
                + " / " + PanelButton("addInnerButton").Text + ")", PanelButton("insertAfterButton").Text.Contains(sectionsLabel)
                && PanelButton("removeButton").Text.Contains(sectionsLabel) && PanelButton("addInnerButton").Text.Contains(lanesLabel)
                && PanelButton("removeInnerButton").Text.Contains(lanesLabel) && !PanelButton("addInnerButton").Disabled);
            await CheckUndo("inserting a section from a lane point", async () => await Press("insertAfterButton"));
            counts = await Counts();
            Check("Insert after with a lane point selected adds a section (" + counts + ")", counts == "3,1,1,1,1");
            await CheckUndo("adding a lane from a lane point", async () => await Press("addInnerButton"));
            counts = await Counts();
            Check("Add to lanes adds a lane to every section (" + counts + ")", counts == "3,2,2,1,1");
            await Press("removeInnerButton");
            counts = await Counts();
            Check("Remove from lanes removes a lane from every section (" + counts + ")", counts == "3,1,1,1,1");
            SelectPoint("sections.0.rightCurb");
            Check("the inner buttons are disabled for a point in the sections only", PanelButton("addInnerButton").Disabled && PanelButton("removeInnerButton").Disabled);
            builder.CloseAllPanels();
            await Frames(2);
            //the first lane added by click: in every section, at the same place across it (the last section turned by 90 degrees)
            await Call("lowLevel.remove", new { element = bai, array = "sections.0.backwardLanes", index = 0 });
            var lastSection = Element(bai).GetLeaves().Where(l => l.path.StartsWith("sections.2.")).ToList();
            var lastCenter = lastSection.Aggregate(Vector3.Zero, (c, l) => c + l.leaf.point.GetPoint()) / lastSection.Count;
            foreach (var (path, leaf) in lastSection) {
                await Call("lowLevel.movePoint", new { element = bai, path, position = A(lastCenter + (leaf.point.GetPoint() - lastCenter).Rotated(Vector3.Up, Mathf.Pi / 2)) });
            }
            var secs0 = (await Derived(bai))["points"]["sections"][0];
            var click = Pos(secs0["rightCurb"]).Lerp(Pos(secs0["leftCurb"]), 0.3f);
            await CheckUndo("adding a lane point by click", async () => {
                History.UndoManager.Record("append", () => Operations.LowLevel.AppendPoint(M, Element(bai), "sections.0.backwardLanes", null, click));
                await Frames(2);
            });
            counts = await Counts();
            var baiSections = (await Derived(bai))["points"]["sections"];
            var toRight = click.DistanceTo(Pos(secs0["rightCurb"]));
            var toLeft = click.DistanceTo(Pos(secs0["leftCurb"]));
            var placed = baiSections.Select(s => s["backwardLanes"].Count() == 1 ? Pos(s["backwardLanes"][0]) : Vector3.Inf).ToList();
            Check("a lane point added by click goes in every section (" + counts + ")", counts == "3,1,1,1,1");
            Check("it is where clicked in the nearest section, at the same place across the others (" + string.Join(" ", placed) + ")",
                placed[0].DistanceTo(click) < 1e-3 && baiSections.Skip(1).All(s => Mathf.Abs(Pos(s["backwardLanes"][0]).DistanceTo(Pos(s["rightCurb"])) - toRight) < 1e-3
                    && Mathf.Abs(Pos(s["backwardLanes"][0]).DistanceTo(Pos(s["leftCurb"])) - toLeft) < 1e-3));

            //dividers: the closed ends are drawn
            var dividerState = new { dividerType = 2, dividerValue = 0.5 };
            var open = (await Call("lowLevel.create", new { type = "dividedRoad", position = new[] { 1040, 0, 1200 }, state = dividerState })).Value<int>();
            var closed = (await Call("lowLevel.create", new { type = "dividedRoad", position = new[] { 1070, 0, 1200 },
                state = new { dividerType = 2, dividerValue = 0.5, closeStart = true, closeEnd = true } })).Value<int>();
            await Frames(2);
            var capTriangles = Triangles(Element(closed)) - Triangles(Element(open));
            Check("the closed ends of an elevated divider are drawn (" + capTriangles + " triangles)", capTriangles == 8);
            foreach (var type in new[] { 1, 3 }) {
                var flat = (await Call("lowLevel.create", new { type = "dividedRoad", position = new[] { 1100 + type * 30, 0, 1200 }, state = new { dividerType = type, dividerValue = 1.0, closeStart = true } })).Value<int>();
                await Frames(2);
                Check("a divider of type " + type + " is drawn", Triangles(Element(flat)) > Triangles(Element(open)) - 40);
            }

            //rooms: a perimeter set manually, shown as a line that can be clicked to select the room
            var room = (await Call("lowLevel.create", new {
                type = "room", points = new { perimeter = new object[] {
                    new { position = new[] { 1000, 0, 1300 } }, new { position = new[] { 1020, 0, 1300 } },
                    new { position = new[] { 1020, 0, 1320 } }, new { position = new[] { 1000, 0, 1320 } } } }
            })).Value<int>();
            await Frames(3);
            var roomElement = Element(room);
            var clicked = GameEngine.Raycast.DoRaycast(builder.helper, new Vector3(1010, 50, 1300), Vector3.Down, out var hit, 100, 1 << 7);
            Check("a room with a perimeter can be clicked", clicked && hit.collider == roomElement.generator);
            //the outline is hidden by the core's view toggle (and can't be clicked then), but shown while the room is edited;
            //the room has no contents, so its placeholder surface inside the perimeter can be clicked anyway (the outline is
            //clicked just outside it)
            bool OutlineShown() => roomElement.generator.GetChildren().OfType<GameEngine.LineRenderer>().Any(l => l.enabled);
            bool ClickableAt(float z) => GameEngine.Raycast.DoRaycast(builder.helper, new Vector3(1010, 50, z), Vector3.Down, out var h, 100, 1 << 7) && h.collider == roomElement.generator;
            bool Clickable() => ClickableAt(1299.9f);
            var toggles = (JArray)await Call("view.toggles");
            Check("view.toggles lists the room outlines of the core, on", toggles.Any(t => t["name"].Value<string>() == "roomOutlines" && t["on"].Value<bool>()));
            await Call("view.setToggle", new { name = "roomOutlines", on = false });
            await Frames(2);
            var offShown = OutlineShown();
            var offClickable = Clickable();
            var placeholderClickable = ClickableAt(1310);
            roomElement.SetActive(true, true);
            var editedShown = OutlineShown();
            roomElement.SetActive(false, false);
            Check("the room outline is hidden by its view toggle (shown " + offShown + ", clickable " + offClickable + ", while edited " + editedShown + ")",
                !offShown && !offClickable && editedShown && !OutlineShown());
            Check("a room without contents can be clicked inside its perimeter (its placeholder surface) with the outlines hidden", placeholderClickable);
            Check("the view toggle is saved in the city preferences", PreferencesManager.Get("viewToggle.roomOutlines", true) == false
                && ((JArray)await Call("view.toggles")).Any(t => t["name"].Value<string>() == "roomOutlines" && !t["on"].Value<bool>()));
            await Call("view.setToggle", new { name = "roomOutlines", on = true });
            await Frames(2);
            Check("the room outline is shown again with its view toggle on", OutlineShown() && Clickable());
            await Expect("an unknown view toggle", "not_found", "view.setToggle", new { name = "noSuchToggle", on = true });
            //in the View menu: after Reset window and a separator, a check item switching it
            var viewMenu = CallBuilder<MenuBar>("GetMenuBar").FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>().First(b => b.Text == StringManager.Get("VIEW")).GetPopup();
            viewMenu.EmitSignal(PopupMenu.SignalName.AboutToPopup);
            var outlinesItem = viewMenu.ItemCount - 1;
            Check("the View menu ends with a separator and the checked room outlines toggle",
                viewMenu.GetItemText(outlinesItem - 2) == StringManager.Get("RESET_WINDOW") && viewMenu.IsItemSeparator(outlinesItem - 1)
                && viewMenu.GetItemText(outlinesItem) == StringManager.Get("VIEW_ROOM_OUTLINES") && viewMenu.IsItemCheckable(outlinesItem) && viewMenu.IsItemChecked(outlinesItem));
            async Task MenuScreenshot(string name) {
                if (DisplayServer.GetName() == "headless") return;
                viewMenu.Popup(new Rect2I(200, 40, 0, 0));
                await Frames(3);
                Screenshot(name);
                viewMenu.Hide();
            }
            await MenuScreenshot("view_menu_toggle_on.png");
            viewMenu.EmitSignal(PopupMenu.SignalName.IdPressed, viewMenu.GetItemId(outlinesItem));
            await Frames(2);
            var offFromMenu = !OutlineShown();
            viewMenu.EmitSignal(PopupMenu.SignalName.AboutToPopup);
            var uncheckedInMenu = !viewMenu.IsItemChecked(outlinesItem);
            await MenuScreenshot("view_menu_toggle_off.png");
            viewMenu.EmitSignal(PopupMenu.SignalName.IdPressed, viewMenu.GetItemId(outlinesItem));
            await Frames(2);
            Check("the View menu item switches the toggle (off " + offFromMenu + ", unchecked " + uncheckedInMenu + ")", offFromMenu && uncheckedInMenu && OutlineShown());
            //a closed array: the element added after the last one is between it and the first one
            await Call("lowLevel.insert", new { element = room, array = "perimeter" });
            var perimeter = (await Derived(room))["points"]["perimeter"];
            Check("a point added at the end of a closed array is between the last and the first one",
                perimeter.Count() == 5 && Pos(perimeter[4]).DistanceTo(new Vector3(1000, 0, 1310)) < 1e-3);
            var first = History.UndoManager.GetAliveElement(perimeter[0]["id"].Value<int>()) as TerrainPoint;
            var again = Operations.LowLevel.AppendPoint(M, roomElement, "perimeter", first.GetObject(), first.GetPoint());
            Check("a point is not added twice to an array of points", again == null && (await Derived(room))["points"]["perimeter"].Count() == 5);
            //computed from the contents (converter roomPerimeter)
            var withRoad = (await Call("lowLevel.create", new { type = "room", position = new[] { 1000, 0, 1200 } })).Value<int>();
            await Call("lowLevel.setContainer", new { element = road, container = withRoad });
            JArray changed = null;
            await CheckUndo("computing the perimeter of a room", async () => changed = (JArray)await Call("elements.convert", new { elements = new[] { withRoad }, converter = "roomPerimeter" }));
            var computed = (await Derived(withRoad))["points"]["perimeter"];
            Check("the perimeter of a room computed from its contents (" + computed.Count() + " points)", changed != null && changed.Any(c => c.Value<int>() == withRoad)
                && computed.Count() >= 4 && (await Call("elements.get", new { @ref = withRoad }))["data"]["state"]["simplifyPerimeter"]?.Value<bool>() == true);
            Check("the perimeter computed shares the points of the contents", computed.All(p => p["shared"].Value<bool>()));
            await Expect("the perimeter of a room without surfaces", "converter_failed", "elements.convert", new { elements = new[] { room }, converter = "roomPerimeter" });

            //prop rules: the props of a low-level road placed along its sidewalks, exported with it
            var rulePreset = Operations.Presets.Filter("roadPropRule", new List<string> { "psdl" }).FirstOrDefault(r => r.State("right") != null || r.State("left") != null);
            if (rulePreset != null) {
                var rule = JObject.FromObject(rulePreset, Newtonsoft.Json.JsonSerializer.Create(Json.Settings));
                await CheckUndo("setting the prop rule of a low-level road", async () => {
                    await Call("lowLevel.setPropRule", new { element = road, rule });
                    await Frames(2);
                });
                var props = Element(road).generator.props;
                Check("the props of the prop rule are placed along the low-level road (" + props.Count + ")", props.Count > 0 && props.All(p => p.container == "right" || p.container == "left"));
                var exportJson = Path.Combine(folder, "..", "api_lowlevel_props.json");
                await Call("city.exportJson", new { path = exportJson });
                var exported = ((JArray)JObject.Parse(File.ReadAllText(exportJson))["lowLevelElements"]).FirstOrDefault(e => e["id"].Value<int>() == M.lowLevelElements.IndexOf(Element(road))); //the ids of the export are the indices
                Check("the props of a low-level road are exported", exported?["data"]?["propLines"] is JArray propLines && propLines.Sum(l => ((JArray)l["props"]).Count) == props.Count);
            } else {
                Log("     no prop rule preset with sidewalk props in the template");
            }
            await Expect("a prop rule on a type without props containers", "invalid_operation", "lowLevel.setPropRule", new { element = room, rule = new JObject() });
            await Expect("a prop rule with a container of another type", "not_found", "lowLevel.setPropRule", new { element = road, rule = new JObject { ["middle"] = new JObject() } });

            //anchors between points: on the line between two points, on the vertical line of a point
            object Fan(float x) => new {
                type = "triangleFan", points = new JObject(),
                mesh = new { vertices = new object[] { new { position = new[] { x, 0, 1400 } }, new { position = new[] { x + 10, 0, 1400 } }, new { position = new[] { x + 10, 0, 1410 } } }, triangles = new[] { 0, 1, 2 } }
            };
            var fanA = (await Call("lowLevel.create", Fan(1000))).Value<int>();
            var fanB = (await Call("lowLevel.create", Fan(1030))).Value<int>();
            async Task<TerrainPoint> V(int fan, int i) => History.UndoManager.GetAliveElement((await Derived(fan))["vertices"][i]["id"].Value<int>()) as TerrainPoint;
            var a0 = await V(fanA, 0);
            var a1 = await V(fanA, 1);
            await CheckUndo("attaching a point to a line", async () => await Call("lowLevel.attachPoint", new { element = fanB, vertex = 0, line = new[] { History.ElementRegistry.GetId(a0), History.ElementRegistry.GetId(a1) } }));
            var onLine = await V(fanB, 0);
            Check("a point attached to a line is on it", onLine.anchor is LineAnchor && GeometryHelper.ClosestPointToCurve(onLine.GetPoint(), new List<Vector3> { a0.GetPoint(), a1.GetPoint() }, out _).DistanceTo(onLine.GetPoint()) < 1e-3);
            var percent = ((LineAnchor)onLine.anchor).percent;
            await Call("terrainPoints.move", new { point = History.ElementRegistry.GetId(a1), position = A(a1.GetPoint() + new Vector3(0, 4, 6)) });
            await Frames(2);
            Check("a point on a line follows its ends", onLine.GetPoint().DistanceTo(a0.GetPoint().Lerp(a1.GetPoint(), percent)) < 1e-3);
            await CheckUndo("attaching a point to a vertical line", async () => await Call("lowLevel.attachPoint", new { element = fanB, vertex = 1, column = History.ElementRegistry.GetId(a0) }));
            var onColumn = await V(fanB, 1);
            await Call("lowLevel.movePoint", new { element = fanB, vertex = 1, position = A(onColumn.GetPoint() + new Vector3(0, 7, 0)) });
            await Frames(2);
            Check("a point on a vertical line keeps its own height", onColumn.anchor is ColumnAnchor && Mathf.Abs(onColumn.GetPoint().Y - 7) < 1e-3
                && Mathf.Abs(onColumn.GetPoint().X - a0.GetPoint().X) < 1e-3 && Mathf.Abs(onColumn.GetPoint().Z - a0.GetPoint().Z) < 1e-3);
            await Call("terrainPoints.move", new { point = History.ElementRegistry.GetId(a0), position = A(a0.GetPoint() + new Vector3(3, 0, -2)) });
            await Frames(2);
            Check("a point on a vertical line follows the point", Mathf.Abs(onColumn.GetPoint().X - a0.GetPoint().X) < 1e-3 && Mathf.Abs(onColumn.GetPoint().Z - a0.GetPoint().Z) < 1e-3
                && Mathf.Abs(onColumn.GetPoint().Y - 7) < 1e-3);
            var a0Before = a0.GetPoint();
            await CheckUndo("moving a point on a vertical line horizontally", async () => {
                await Call("lowLevel.movePoint", new { element = fanB, vertex = 1, position = A(onColumn.GetPoint() + new Vector3(-2, 1, 1)) });
                await Frames(2);
            });
            Check("moving a point on a vertical line horizontally moves the point it follows", a0.GetPoint().DistanceTo(a0Before + new Vector3(-2, 0, 1)) < 1e-3
                && Mathf.Abs(onColumn.GetPoint().Y - 8) < 1e-3);
            //with the gizmo
            builder.SelectObject(Element(fanB), true, null);
            await Frames(3);
            builder.gizmo.ClearTargets();
            builder.gizmo.AddTarget(onColumn.GetObject());
            a0Before = a0.GetPoint();
            History.UndoManager.Record("drag", () => {
                History.UndoManager.TouchOwner(onColumn.GetObject());
                GO.SetPosition(onColumn.GetObject(), onColumn.GetPoint() + new Vector3(1, 0, 0));
                M.FlagAsChanged();
            });
            await Frames(3);
            Check("dragging a point on a vertical line moves the point it follows", a0.GetPoint().DistanceTo(a0Before + new Vector3(1, 0, 0)) < 1e-3
                && onColumn.anchor is ColumnAnchor && Mathf.Abs(onColumn.GetPoint().X - a0.GetPoint().X) < 1e-3);
            builder.CloseAllPanels();
            await Frames(2);
            await Expect("a point following itself", "invalid_operation", "lowLevel.attachPoint", new { element = fanA, vertex = 0, column = History.ElementRegistry.GetId(onColumn) });
            //saved and loaded
            GO.SetName(Element(fanA), "anchor fan A");
            GO.SetName(Element(fanB), "anchor fan B");
            await Call("city.save");
            await Call("city.reload");
            var fans = (JArray)await Call("elements.list", new { kind = "lowLevel", type = "triangleFan" });
            int Named(string name) => fans.First(f => f["name"].Value<string>() == name)["id"].Value<int>();
            var loadedA0 = await V(Named("anchor fan A"), 0);
            var loadedB0 = await V(Named("anchor fan B"), 0);
            var loadedB1 = await V(Named("anchor fan B"), 1);
            Check("the anchors between points are saved and loaded", loadedB1.anchor is ColumnAnchor column && column.master == loadedA0
                && loadedB0.anchor is LineAnchor line && line.StartPoint == loadedA0 && Mathf.Abs(loadedB1.GetPoint().Y - 8) < 1e-3);

            //a building converted: its points keep their places when one is moved
            var lines = (JArray)await Call("elements.list", new { kind = "buildingLine" });
            if (lines.Count > 0) {
                var parts = (JArray)await Call("elements.convert", new { elements = new[] { lines[0]["id"].Value<int>() }, converter = "toPsdl" });
                var points = new HashSet<TerrainPoint>();
                foreach (var id in parts) foreach (var p in Element(id.Value<int>())?.GetAllPoints() ?? new List<TerrainPoint>()) points.Add(p);
                var columns = points.Where(p => p.anchor is ColumnAnchor).ToList();
                var onLines = points.Where(p => p.anchor is LineAnchor la && la.StartPoint != null).ToList();
                Check("the points of a converted building follow each other (" + columns.Count + " on vertical lines, " + onLines.Count + " on lines)", columns.Count > 0);
                var master = columns.Select(p => ((ColumnAnchor)p.anchor).master).First(m => m.anchor == null);
                var followers = columns.Where(p => ((ColumnAnchor)p.anchor).master == master).ToList();
                await Call("terrainPoints.move", new { point = History.ElementRegistry.GetId(master), position = A(master.GetPoint() + new Vector3(0.5f, 0, 0.5f)) });
                await Frames(3);
                Check("moving a corner of a converted building moves the corners above it", followers.All(p => Mathf.Abs(p.GetPoint().X - master.GetPoint().X) < 1e-3 && Mathf.Abs(p.GetPoint().Z - master.GetPoint().Z) < 1e-3));
            }
        }

        //derived points, with a type of the test (no type of the core uses them yet): its line copies the line of the first
        //other element of the same source, with the first point shared and the others 1 m higher
        async Task DerivedPoints() {
            float[] A(Vector3 v) => new[] { v.X, v.Y, v.Z };
            LowLevelElement Element(int id) => History.UndoManager.GetAliveElement(id) as LowLevelElement;
            var dir = Path.GetFullPath(folder + "_derived");
            Directory.CreateDirectory(Path.Combine(dir, "derivedTest"));
            Directory.CreateDirectory(Path.Combine(dir, "lua"));
            File.WriteAllText(Path.Combine(dir, "derivedTest", "settings.json"), @"{
                ""controlPoints"": [{ ""name"": ""line"", ""array"": { ""min"": 2, ""defaultCount"": 3, ""defaultStep"": [0, 0, 5] } }],
                ""derivedPoints"": { ""flag"": ""derived"", ""function"": ""derivedtest.copy"" },
                ""creatable"": false }");
            File.WriteAllText(Path.Combine(dir, "derivedTest", "parameters.json"), @"{ ""parameters"": [{ ""name"": ""derived"", ""label"": ""derived"", ""type"": ""bool"" }] }");
            File.WriteAllText(Path.Combine(dir, "derivedTest", "ui.json"), @"{ ""label"": ""Derived test"", ""menuWidth"": 1.5, ""tabs"": [{ ""label"": """", ""elements"": [
                { ""name"": ""mainGroup"", ""width"": 1.5 }, { ""name"": ""derived"", ""width"": 1.5 }, { ""name"": ""pointsGroup"", ""width"": 1.5 }] }] }");
            File.WriteAllText(Path.Combine(dir, "lua", "derivedtest.lua"), string.Join("\n",
                "local M = {}",
                "function M.copy(v, ctx)",
                "  local s = ctx.siblings[1]",
                "  if s == nil then return nil end",
                "  local line = {}",
                "  for i, p in ipairs(s.points.line) do",
                "    if i == 1 then line[i] = p else line[i] = p + vec3(0, 1, 0) end",
                "  end",
                "  return { points = { line = line } }",
                "end",
                "return M"));
            var types = CityElements.Types.Parsers.TypeParser.GetLowLevelTypes();
            var host = RuntimeCalculator.Scripting.LuaScriptHost.CreateForFolder(Path.Combine(dir, "lua"));
            types["derivedTest"] = new CityElements.Types.Runtime.LowLevelType(new CityElements.Types.LowLevelType(Path.Combine(dir, "derivedTest")), "derivedTest", host);
            try {
                var road = History.UndoManager.GetAliveElement(((JArray)await Call("elements.list", new { kind = "road" })).First()["id"].Value<int>());
                var src = (await Call("lowLevel.create", new { type = "derivedTest", position = new[] { 1300, 0, 1400 } })).Value<int>();
                var copy = (await Call("lowLevel.create", new { type = "derivedTest", position = new[] { 1320, 0, 1400 } })).Value<int>();
                Element(src).source = road; //as if both were converted from the road
                Element(copy).source = road;
                await Frames(2);
                bool Follows() {
                    var s = Element(src).GetLeaves();
                    var c = Element(copy).GetLeaves();
                    return s.Count == c.Count && Enumerable.Range(0, s.Count).All(i => i == 0 ? c[0].leaf.point == s[0].leaf.point
                        : c[i].leaf.point != s[i].leaf.point && c[i].leaf.point.GetPoint().DistanceTo(s[i].leaf.point.GetPoint() + Vector3.Up) < 1e-3);
                }
                Check("derived points: not computed while their flag is off", !Follows());
                await CheckUndo("turning the derived points on", () => Call("state.setParam", new { @ref = copy, name = "derived", value = true }));
                Check("derived points: computed when their flag is turned on (the first one shared with the other element)", Follows());
                var moved = Element(src).GetLeaves()[1].leaf.point.GetPoint() + new Vector3(2, 0, 1);
                await CheckUndo("moving a point the derived points follow", () => Call("lowLevel.movePoint", new { element = src, path = "line.1", position = A(moved) }));
                Check("derived points: computed again when the other element changes", Follows() && Element(copy).GetLeaves()[1].leaf.point.GetPoint().DistanceTo(moved + Vector3.Up) < 1e-3);
                await CheckUndo("adding a point the derived points follow", () => Call("lowLevel.insert", new { element = src, array = "line" }));
                Check("derived points: the structure follows too (" + Element(copy).GetLeaves().Count + " points)", Follows() && Element(copy).GetLeaves().Count == 4);
                //the editor: the points can't be moved, added or removed while computed
                builder.SelectObject(Element(copy), true, null);
                await Frames(3);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                global::EditorPanelElement PanelField(string name) => builder.lowLevelEditorPanel.GetType().GetField(name, flags).GetValue(builder.lowLevelEditorPanel) as global::EditorPanelElement;
                var layers = string.Join(",", Element(copy).GetLeaves().Skip(1).Select(l => GO.GetLayer(l.leaf.point.GetObject())).Distinct());
                Check("derived points: the editor can't change them (layers " + layers + ")", !PanelField("placementDropdown").GetInteractable()
                    && !PanelField("insertAfterButton").GetInteractable() && !PanelField("detachButton").GetInteractable()
                    && Element(copy).GetLeaves().Skip(1).All(l => GO.GetLayer(l.leaf.point.GetObject()) != 8));
                Screenshot("derived_points_editor.png");
                builder.CloseAllPanels();
                await Frames(2);
                await Call("state.setParam", new { @ref = copy, name = "derived", value = false });
                await Call("lowLevel.movePoint", new { element = src, path = "line.2", position = A(Element(src).GetLeaves()[2].leaf.point.GetPoint() + new Vector3(0, 0, 3)) });
                Check("derived points: kept as they are with their flag off", !Follows() && Element(copy).GetLeaves().Count == 4);
                Operations.LowLevel.Delete(M, Element(src));
                Operations.LowLevel.Delete(M, Element(copy));
                await Frames(2);
            } finally {
                types.Remove("derivedTest");
                Directory.Delete(dir, true);
            }
        }

        //a BAI road converted with its road (MM2) is auto-calculated: it follows the road attributes converted with it, until
        //turned off
        async Task AutoBai() {
            float[] A(Vector3 v) => new[] { v.X, v.Y, v.Z };
            LowLevelElement Element(int id) => History.UndoManager.GetAliveElement(id) as LowLevelElement;
            TerrainPoint Point(LowLevelElement e, string path) => (global::LowLevel.PointTree.Find(e.points, path) as global::LowLevel.PointLeaf)?.point;
            List<Vector3> Positions(LowLevelElement e) => e.GetLeaves().Select(l => l.leaf.point.GetPoint()).ToList();
            bool SamePositions(List<Vector3> a, List<Vector3> b) => a.Count == b.Count && Enumerable.Range(0, a.Count).All(i => a[i].DistanceTo(b[i]) < 1e-4f);
            var cityRoad = ((JArray)await Call("elements.list", new { kind = "road" })).First(r => History.UndoManager.GetAliveElement(r["id"].Value<int>()) is Road rd
                && rd.startIntersection != null && rd.endIntersection != null && rd.state.Str("type", "") == "psdl" && !Operations.LowLevel.IsGhost(rd))["id"].Value<int>();
            var created = ((JArray)await Call("elements.convert", new { elements = new[] { cityRoad }, converter = "toPsdl" })).Select(t => Element(t.Value<int>())).ToList();
            var bai = created.FirstOrDefault(e => e?.TypeName == "baiRoad");
            var roadTypes = new[] { "roadWithSidewalks", "roadWithoutSidewalks", "dividedRoad" };
            var parts = created.Where(e => e != null && roadTypes.Contains(e.TypeName)).ToList();
            Check("a converted BAI road is auto-calculated (road attributes: " + string.Join(", ", parts.Select(e => e.TypeName)) + ")",
                bai != null && bai.state.Bool("autoCalculated") && parts.Count > 0);
            if (bai == null || parts.Count == 0) return;
            var baiId = History.ElementRegistry.GetId(bai);
            //the lanes are spread evenly between each curb and the middle of the road (the dividers if divided), and the edges and
            //curbs are the points of the road attributes
            string Spread() {
                var paths = bai.GetLeaves().Select(l => l.path).ToList();
                var n = paths.Count(x => x.EndsWith(".rightEdge"));
                var b = paths.Count(x => x.StartsWith("sections.0.backwardLanes."));
                var f = paths.Count(x => x.StartsWith("sections.0.forwardLanes."));
                for (int i = 0; i < n; i++) {
                    var sec = "sections." + i + ".";
                    var rc = Point(bai, sec + "rightCurb");
                    var lc = Point(bai, sec + "leftCurb");
                    //the section of a road attribute with the same right curb
                    string partSection = null;
                    LowLevelElement part = null;
                    foreach (var e in parts) {
                        foreach (var (path, leaf) in e.GetLeaves()) {
                            if (leaf.point == rc && (path.EndsWith(".leftRoad") || path.EndsWith(".left"))) {
                                part = e;
                                partSection = path.Substring(0, path.LastIndexOf('.') + 1);
                            }
                        }
                    }
                    if (part == null) return "section " + i + ": its right curb is not a point of a road attribute";
                    var lcName = part.TypeName == "roadWithoutSidewalks" ? "right" : "rightRoad";
                    if (Point(part, partSection + lcName) != lc) return "section " + i + ": its left curb is not the one of the road attribute";
                    Vector3 rInner, lInner;
                    if (part.TypeName == "dividedRoad") {
                        rInner = Point(part, partSection + "leftDivider").GetPoint();
                        lInner = Point(part, partSection + "rightDivider").GetPoint();
                    } else {
                        var mid = (rc.GetPoint() + lc.GetPoint()) / 2;
                        rInner = f > 0 ? mid : lc.GetPoint();
                        lInner = b > 0 ? mid : rc.GetPoint();
                    }
                    for (int k = 0; k < b; k++) {
                        var expected = rc.GetPoint() + (rInner - rc.GetPoint()) * ((k + 0.5f) / b);
                        if (Point(bai, sec + "backwardLanes." + k).GetPoint().DistanceTo(expected) > 1e-3f) return "section " + i + ": backward lane " + k;
                    }
                    for (int k = 0; k < f; k++) {
                        var expected = lInner + (lc.GetPoint() - lInner) * ((k + 0.5f) / f);
                        if (Point(bai, sec + "forwardLanes." + k).GetPoint().DistanceTo(expected) > 1e-3f) return "section " + i + ": forward lane " + k;
                    }
                }
                return null;
            }
            //the conversion only records the inputs: the points are the converted ones (the lanes of the editor) until an edit
            var converted = Positions(bai);
            await Frames(2);
            await Call("state.setParam", new { @ref = baiId, name = "autoCalculated", value = false });
            await Call("state.setParam", new { @ref = baiId, name = "autoCalculated", value = true });
            var recomputed = Positions(bai);
            var why = Spread();
            Check("an auto-calculated BAI road computed when turned on (" + (why ?? "ok") + "; " + Enumerable.Range(0, System.Math.Min(converted.Count, recomputed.Count))
                .Count(i => converted[i].DistanceTo(recomputed[i]) > 1e-4f) + " points differ from the converted ones)", why == null);
            await Call("history.undo");
            await Call("history.undo");
            Check("the converted points are kept after the conversion", SamePositions(converted, Positions(bai)) && bai.state.Bool("autoCalculated"));
            //it follows the road attributes
            var part0 = parts[0];
            var curb = part0.TypeName == "roadWithoutSidewalks" ? "left" : "leftRoad";
            var movedTo = Point(part0, "sections.1." + curb).GetPoint() + new Vector3(0.6f, 0, 0.4f);
            var beforeMove = Positions(bai);
            await CheckUndo("moving a point of the road an auto-calculated BAI road follows", () => Call("lowLevel.movePoint", new { element = History.ElementRegistry.GetId(part0), path = "sections.1." + curb, position = A(movedTo) }));
            why = Spread();
            Check("an auto-calculated BAI road follows a point of its road moved (" + (why ?? "ok") + ")", why == null && !SamePositions(beforeMove, Positions(bai)));
            var sectionCount = bai.GetLeaves().Count(l => l.path.EndsWith(".rightEdge"));
            await CheckUndo("adding a section to the road an auto-calculated BAI road follows", () => Call("lowLevel.insert", new { element = History.ElementRegistry.GetId(part0), array = "sections", index = 1 }));
            why = Spread();
            var newCount = bai.GetLeaves().Count(l => l.path.EndsWith(".rightEdge"));
            Check("an auto-calculated BAI road gets the section added to its road (" + sectionCount + " -> " + newCount + ", " + (why ?? "ok") + ")", why == null && newCount == sectionCount + 1);
            //the editor: its points can't be moved
            builder.SelectObject(bai, true, null);
            await Frames(3);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            global::EditorPanelElement PanelField(string name) => builder.lowLevelEditorPanel.GetType().GetField(name, flags).GetValue(builder.lowLevelEditorPanel) as global::EditorPanelElement;
            var layers = string.Join(",", bai.GetAllPoints().Select(p => GO.GetLayer(p.GetObject())).Distinct());
            Check("an auto-calculated BAI road can't be edited in the editor (layers " + layers + ")", !PanelField("placementDropdown").GetInteractable()
                && !PanelField("insertAfterButton").GetInteractable() && bai.GetAllPoints().All(p => GO.GetLayer(p.GetObject()) != 8));
            if (DisplayServer.GetName() != "headless") {
                var points = bai.GetAllPoints().Select(p => p.GetPoint()).ToList();
                var bounds = new Aabb(points[0], Vector3.Zero);
                foreach (var p in points) bounds = bounds.Expand(p);
                var center = bounds.GetCenter();
                (builder.GetViewport().GetCamera3D() as CameraController)?.SetView(center + new Vector3(0, 0.8f, 0.6f) * Mathf.Max(bounds.Size.Length(), 10), center);
                await Frames(3);
                Screenshot("auto_bai_editor.png");
            }
            builder.CloseAllPanels();
            await Frames(2);
            //turned off: the road is edited alone
            await Call("state.setParam", new { @ref = baiId, name = "autoCalculated", value = false });
            //(its edges and curbs are still the points of the road: only the lanes are compared)
            List<Vector3> Lanes() => bai.GetLeaves().Where(l => l.path.Contains("Lanes.")).Select(l => l.leaf.point.GetPoint()).ToList();
            var beforeOff = Lanes();
            await Call("lowLevel.movePoint", new { element = History.ElementRegistry.GetId(part0), path = "sections.1." + curb, position = A(movedTo + new Vector3(0, 0, 1)) });
            await Frames(2);
            Check("a BAI road not auto-calculated doesn't follow its road", beforeOff.Count > 0 && SamePositions(beforeOff, Lanes()));
            await Call("ghosts.restore", new { element = cityRoad });
            await Frames(2);
        }

        //editing the elements of a converted room: the points shared with the auto-calculated BAI road and with the room stay
        //shown and moveable, the BAI lanes are shown while anything in the room is edited, a room without visible contents
        //gets its placeholder surface, and the ghost is deleted with the last element replacing it
        async Task LowLevelRoomEditing() {
            LowLevelElement Element(int id) => History.UndoManager.GetAliveElement(id) as LowLevelElement;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            string PointProblems(LowLevelElement e) {
                var hidden = e.GetAllPoints().Count(p => !GO.GetEnabled(p.GetObject()));
                var fixedPoints = e.GetAllPoints().Count(p => GO.GetLayer(p.GetObject()) != 8);
                return hidden == 0 && fixedPoints == 0 ? null : hidden + " hidden, " + fixedPoints + " not moveable of " + e.GetAllPoints().Count;
            }
            bool LanesShown(LowLevelElement bai) {
                var lines = (System.Collections.IList)typeof(LowLevelGenerator).GetField("lines", flags).GetValue(bai.generator);
                var shown = 0;
                foreach (var entry in lines) {
                    var (lr, line) = ((GameEngine.LineRenderer, global::LowLevel.GeometryLine))entry;
                    if (line.withContainer && lr.enabled) shown++;
                }
                return shown > 0;
            }
            var cityRoad = ((JArray)await Call("elements.list", new { kind = "road" })).First(r => History.UndoManager.GetAliveElement(r["id"].Value<int>()) is Road rd
                && rd.startIntersection != null && rd.endIntersection != null && rd.state.Str("type", "") == "psdl" && !Operations.LowLevel.IsGhost(rd))["id"].Value<int>();
            var created = ((JArray)await Call("elements.convert", new { elements = new[] { cityRoad }, converter = "toPsdl" })).Select(t => Element(t.Value<int>())).ToList();
            var room = created.FirstOrDefault(e => e?.TypeName == "room");
            var bai = created.FirstOrDefault(e => e?.TypeName == "baiRoad");
            var roadTypes = new[] { "roadWithSidewalks", "roadWithoutSidewalks", "dividedRoad" };
            var part = created.FirstOrDefault(e => e != null && roadTypes.Contains(e.TypeName));
            Check("a converted road gives a room, a road attribute and an auto-calculated BAI road", room != null && part != null && bai != null && bai.IsDerived);
            if (room == null || part == null || bai == null) return;

            //a road attribute edited: its points (shared with the BAI road, which can't be moved) can be moved, also after
            //moving one of its inner points (the BAI road follows)
            builder.SelectObject(part, true, null);
            await Frames(3);
            var why = PointProblems(part);
            Check("the points of a road attribute shared with an auto-calculated BAI road can be moved (" + (why ?? "ok") + ")", why == null);
            Check("the BAI lanes are shown while a road attribute of their room is edited", LanesShown(bai));
            var curbName = part.TypeName == "roadWithoutSidewalks" ? "left" : "leftRoad";
            var curb = (global::LowLevel.PointTree.Find(part.points, "sections.1." + curbName) as global::LowLevel.PointLeaf)?.point;
            var baiBefore = bai.GetAllPoints().Select(p => p.GetPoint()).ToList();
            if (curb != null) {
                History.UndoManager.Record("move point", () => {
                    builder.gizmo.ClearTargets();
                    builder.gizmo.AddTarget(curb.GetObject());
                    History.UndoManager.Touch(curb);
                    GO.SetPosition(curb.GetObject(), curb.GetPoint() + new Vector3(0.3f, 0, 0.2f));
                    M.FlagAsChanged();
                });
                await Frames(4);
            }
            var baiMoved = baiBefore.Count != bai.GetAllPoints().Count || bai.GetAllPoints().Select(p => p.GetPoint()).Where((p, i) => p.DistanceTo(baiBefore[i]) > 1e-4f).Any();
            why = PointProblems(part);
            Check("after moving an inner point of the road attribute (the BAI road follows: " + baiMoved + "), its points are still shown and moveable (" + (why ?? "ok") + ")",
                curb != null && baiMoved && why == null);
            //the click mode changed and back to None
            builder.lowLevelEditorPanel.SetPointPlacementMode(1);
            await Frames(2);
            Check("the BAI lanes are shown in another click mode", LanesShown(bai));
            builder.lowLevelEditorPanel.SetPointPlacementMode(0);
            await Frames(2);
            why = PointProblems(part);
            Check("back to the None click mode the points can be moved (" + (why ?? "ok") + ") and the BAI lanes are shown", why == null && LanesShown(bai));
            builder.CloseAllPanels();
            await Frames(2);
            Check("the BAI lanes are hidden when nothing in the room is edited", !LanesShown(bai));

            //the room edited: its perimeter points (shared with its contents) can be moved
            builder.SelectObject(room, true, null);
            await Frames(3);
            why = PointProblems(room);
            Check("the perimeter points of a room can be moved when it's edited (" + (why ?? "ok") + ", " + room.GetAllPoints().Count + " points)",
                why == null && room.GetAllPoints().Count > 0);
            Check("the BAI lanes are shown while their room is edited", LanesShown(bai));
            builder.lowLevelEditorPanel.SetPointPlacementMode(1);
            await Frames(2);
            builder.lowLevelEditorPanel.SetPointPlacementMode(0);
            await Frames(2);
            var contentsShown = M.GetLowLevelChildren(room).All(c => c.IsActive());
            Check("back to the None click mode the room's contents are shown and its points can be moved", PointProblems(room) == null && contentsShown);
            builder.CloseAllPanels();
            await Frames(2);

            //the placeholder surface of the room: shown only while its contents draw nothing
            bool PlaceholderShown() => (bool)typeof(LowLevelGenerator).GetField("placeholderShown", flags).GetValue(room.generator);
            Check("a room whose contents draw meshes has no placeholder surface", room.generator.HasPlaceholder() && !PlaceholderShown());
            var drawing = M.GetLowLevelChildren(room).Where(c => c.generator.DrawsMesh()).ToList();
            foreach (var c in drawing) await Call("lowLevel.setContainer", new { element = History.ElementRegistry.GetId(c), container = (object)null });
            await Frames(2);
            Check("a room whose contents draw nothing (" + M.GetLowLevelChildren(room).Count + " left) shows its placeholder surface", PlaceholderShown());
            if (DisplayServer.GetName() != "headless") {
                var points = room.GetAllPoints().Select(p => p.GetPoint()).ToList();
                var bounds = new Aabb(points[0], Vector3.Zero);
                foreach (var p in points) bounds = bounds.Expand(p);
                var center = bounds.GetCenter();
                (builder.GetViewport().GetCamera3D() as CameraController)?.SetView(center + new Vector3(0, 0.8f, 0.6f) * Mathf.Max(bounds.Size.Length(), 10), center);
                await Frames(3);
                Screenshot("room_placeholder.png");
            }
            foreach (var c in drawing) await Call("lowLevel.setContainer", new { element = History.ElementRegistry.GetId(c), container = History.ElementRegistry.GetId(room) });
            await Frames(2);
            Check("the placeholder surface is hidden again with the contents back", !PlaceholderShown());

            //the ghost is deleted with the last element replacing it (not with the others), undone with it
            var replacements = Operations.LowLevel.GetReplacements(M, History.UndoManager.GetAliveElement(cityRoad)).Select(e => History.ElementRegistry.GetId(e)).ToList();
            bool GhostAlive() => History.UndoManager.GetAliveElement(cityRoad) is Road r && !r.deleted;
            await Call("lowLevel.delete", new { element = replacements[0] });
            Check("the ghost is kept while other elements replace it (" + (replacements.Count - 1) + ")", replacements.Count < 2 || GhostAlive());
            await Call("history.undo");
            await CheckUndo("deleting the elements replacing a ghost", async () => {
                await Call("history.begin", new { label = "delete replacements" });
                foreach (var id in replacements) await Call("lowLevel.delete", new { element = id });
                await Call("history.commit");
            });
            await Frames(2);
            //(redone by CheckUndo)
            Check("the ghost is deleted with the last element replacing it", !GhostAlive() && !((JArray)await Call("ghosts.list")).Any(g => g["id"].Value<int>() == cityRoad));
            await Call("history.undo");
            await Frames(2);
            Check("undone, the ghost is back", GhostAlive() && Operations.LowLevel.IsGhost(History.UndoManager.GetAliveElement(cityRoad)));
            await Call("ghosts.restore", new { element = cityRoad });
            await Frames(2);
            Check("a restored ghost is not deleted with its replacements", GhostAlive() && !Operations.LowLevel.IsGhost(History.UndoManager.GetAliveElement(cityRoad)));
        }

        //a BAI road (MM2) connected by hand to a procedural intersection: the intersection lists it too, in the order of the
        //roads around it (checked with the lint of the core's bai_check.py)
        async Task BaiConnections() {
            if (!PythonManager.IsPythonReady()) {
                Log("     Python is not available: the BAI connection test is skipped");
                return;
            }
            var format = ((JArray)await Call("exporters.list"))[0]["format"].Value<string>();
            var script = Path.GetFullPath(Path.Combine(folder, "..", "bai_ends.py"));
            File.WriteAllText(script, string.Join("\n",
                "import json, sys",
                "sys.path.insert(0, sys.argv[1])",
                "from bai_check import read_bai, lint, CDCDCDCD",
                "roads, ints = read_bai(sys.argv[2])",
                "ends = [[rd[k]['intersection'] if rd[k]['index'] != CDCDCDCD else -1 for k in ('start', 'end')] for rd in roads]",
                "problems = [p for p in lint(roads, ints, False) if 'intersection' in p]",
                "print(json.dumps({'ends': ends, 'intersections': [it['roads'] for it in ints], 'problems': problems}))"));
            var tools = Path.Combine(PathHelper.BasePath(), "Files", "cores", "MidtownMadness2", "exporters", "psdl", "tools");
            async Task<JObject> ExportBai() {
                var flags = new Dictionary<string, bool> { ["propRules"] = false, ["psdl"] = false, ["inst"] = false, ["bai"] = true,
                    ["pathset"] = false, ["cinfo_aimap"] = false, ["hudmap"] = false, ["pkgs"] = false };
                var export = await Send("city.export", new { path = Path.Combine(folder, "..", "api_export_bai"), exporter = format, flags });
                if (export["error"] != null) {
                    Log("      export error: " + export["error"]);
                    return null;
                }
                var reported = export["result"]["path"].Value<string>();
                var path = Path.Combine(Path.GetDirectoryName(reported), "city", Path.GetFileName(reported));
                var baiFile = Path.ChangeExtension(path, ".bai");
                if (File.Exists(Path.ChangeExtension(path, ".bin")) || !File.Exists(baiFile)) return null;
                var (code, data) = await PythonManager.RunScriptTask(script, true, new List<string> { tools, baiFile });
                if (code != 0) Log("      bai_ends.py: " + data);
                return code == 0 ? JObject.Parse(data) : null;
            }
            int Listed(JObject read) => read["intersections"].Sum(i => i.Count());

            //a procedural intersection of the BAI (with a road to another intersection), the BAI road through its centre
            var road = ((JArray)await Call("elements.list", new { kind = "road" })).Select(r => History.UndoManager.GetAliveElement(r["id"].Value<int>()) as Road)
                .First(rd => rd != null && rd.startIntersection != null && rd.endIntersection != null && rd.state.Str("type", "") == "psdl"
                    && !Operations.LowLevel.IsGhost(rd) && !rd.startIntersection.IsSeamless());
            var intersection = road.startIntersection;
            var centre = GO.GetPosition(intersection.point);
            var bai = (await Call("lowLevel.create", new { type = "baiRoad", position = new[] { centre.X, centre.Y, centre.Z - 5 } })).Value<int>();
            var before = await ExportBai();
            int Connected(JObject read) => read["ends"].Sum(e => e.Count(x => x.Value<int>() >= 0));
            //a road converted: its BAI road takes its place in the intersections
            await Call("elements.convert", new { elements = new[] { History.ElementRegistry.GetId(road) }, converter = "toPsdl" });
            var converted = await ExportBai();
            Check("the BAI road of a converted road is listed by its intersections" + (converted != null ? " (" + Listed(before) + " -> " + Listed(converted)
                + " road ends listed, " + Connected(before) + " -> " + Connected(converted) + " connected)" : ""), before != null && converted != null
                && Listed(converted) == Listed(before) && Connected(converted) == Connected(before) && converted["problems"].Count() <= before["problems"].Count());
            await Call("history.undo");
            await Expect("a reference to an element of a kind it doesn't accept", "invalid_value", "state.setParam",
                new { @ref = bai, name = "startIntersection", value = History.ElementRegistry.GetId(road) });

            //connected in the editor: picked with the reference field of the panel
            var baiElement = History.UndoManager.GetAliveElement(bai) as LowLevelElement;
            var picker = builder.referencePicker;
            async Task<EditorPanelElements.ReferenceField> OpenField() {
                builder.SelectObject(baiElement, true, null);
                await Frames(3);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                foreach (var page in (List<EditorPanelPage>)typeof(EditorPanel).GetField("pages", flags).GetValue(builder.lowLevelEditorPanel)) {
                    foreach (var pair in (Dictionary<EditorPanelElement, FieldStatus>)typeof(EditorPanelPage).GetField("fieldElements", flags).GetValue(page)) {
                        if (pair.Key is EditorPanelElements.ReferenceField r && pair.Value.fieldName.EndsWith(".startIntersection")) return r;
                    }
                }
                return null;
            }
            //(the API calls close the editors, as before an undo: the panel is used between them, then the city flushed as
            //after a click)
            async Task CheckGuiUndo(string what, JToken before) {
                await Call("city.flush");
                var after = await Snapshot();
                var top = DebugTopEntry();
                await Call("history.undo");
                if (!Same("undo " + what, before, await Snapshot())) Log("      DEBUG entry: " + top);
                await Call("history.redo");
                Same("redo " + what, after, await Snapshot());
            }
            var beforePick = await Snapshot();
            var field = await OpenField();
            Check("the BAI road panel has a reference field for its start intersection (" + field?.GetShownName() + ")",
                field != null && field.GetShownName() == StringManager.Get("REF_NONE"));
            if (field != null) {
                History.UndoManager.Record("pick", field.Pick);
                await Frames(1);
                Check("its Pick button starts picking", picker.IsActive() && field.GetShownName() == StringManager.Get("REF_PICKING"));
                var refused = picker.Pick(road);
                Check("a road is not picked for an intersection reference", !refused && picker.IsActive() && baiElement.state.State("startIntersection") == null);
                History.UndoManager.Record("pick", () => picker.Pick(intersection));
                await Frames(1);
                var picked = Operations.States.ResolveReference(baiElement.state.State("startIntersection"));
                Check("the picked intersection is the reference (" + field.GetShownName() + "), the click mode is restored",
                    picked == intersection && !picker.IsActive() && builder.terrainClick.modifier == builder.lowLevelPlacer
                    && field.GetShownName() == Api.CityApi.GetName(intersection));
                Screenshot("reference_field.png");
                History.UndoManager.Record("pick", field.Pick);
                builder.CloseAllPanels();
                await Frames(2);
                Check("closing the panel ends the pick", !picker.IsActive());
                await CheckGuiUndo("picking a reference", beforePick);
            }
            if (baiElement.state.State("startIntersection") == null) {
                await Call("state.setParam", new { @ref = bai, name = "startIntersection", value = History.ElementRegistry.GetId(intersection) });
            }
            var after = await ExportBai();
            Check("a BAI road connected to a procedural intersection is exported", before != null && after != null);
            if (before != null && after != null) {
                var connected = Connected(after) - Connected(before);
                Check("the procedural intersection lists the BAI road connected to it (" + Listed(before) + " -> " + Listed(after) + " road ends listed, "
                    + connected + " more connected)", Listed(after) == Listed(before) + 1 && connected == 1);
                Check("the road ends and the intersections agree, in the stock order (" + string.Join("; ", after["problems"]) + ")",
                    after["problems"].Count() <= before["problems"].Count());
            }
            var beforeClear = await Snapshot();
            field = await OpenField();
            if (field != null) {
                History.UndoManager.Record("clear", field.Clear);
                await Frames(1);
                Check("the clear button of a reference field removes the reference", baiElement.state.State("startIntersection") == null
                    && field.GetShownName() == StringManager.Get("REF_NONE"));
                builder.CloseAllPanels();
                await Frames(2);
                await CheckGuiUndo("clearing a reference", beforeClear);
                await Call("history.undo");
            }
            await Call("history.undo");
            await Call("history.undo");
            File.Delete(script);
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
            var seamlessLoaded = false;
            foreach (var e in (JArray)await Call("elements.list", new { kind = "intersection" })) {
                var derived = await Derived(e["id"].Value<int>());
                seamlessLoaded |= derived["seamlessActive"].Value<bool>() && derived["sizes"].All(x => x.Value<float>() == 0);
            }
            Check("save and load keep the seamless intersection", seamlessLoaded);

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
                //the PSDL writer fails on the 3-road intersection at the origin built by Roads() (a degenerate sidewalk
                //fan, a known bug of the exporter): the error is reported with its element, and the rest is still written
                var psdl = Path.Combine(folder, "..", "city", "api_export.psdl");
                if (export["error"] != null) {
                    var items = export["error"]["details"]?["items"] as JArray;
                    var errors = items?.Where(i => i["level"]?.Value<string>() == "error").ToList();
                    var known = errors != null && errors.Count > 0 && errors.All(i => i["element"]?["name"]?.Value<string>() == "Intersection 1");
                    if (known) {
                        Log("     city.export fails on the known perimeter error of Intersection 1: " + errors[0]["message"]);
                        Check("the errors of a failed export are reported with their element", true);
                        Check("the export goes on after the failed block (PSDL, INST and BAI written)",
                            File.Exists(psdl) && File.Exists(Path.ChangeExtension(psdl, ".inst")) && File.Exists(Path.ChangeExtension(psdl, ".bai")));
                    } else {
                        Log("FAIL city.export: " + export["error"]);
                        failures++;
                    }
                } else {
                    var path = export["result"]["path"].Value<string>();
                    Check("city.export runs the exporter (exit code " + export["result"]["exitCode"] + ")", export["result"]["exitCode"].Value<int>() == 0 && File.Exists(path));
                }
                Check("the export JSON is deleted after the export", !File.Exists(Path.ChangeExtension(psdl, ".json")));
                if (M.GetDummy<CityProperties>().GetState().Array<ObjectState>("decals")?.Length > 0) {
                    var (decalNames, rails) = await ExportDecals(format);
                    Check("the decals are exported", decalNames != null && decalNames.Contains("decal_zigzag_l"));
                    if (rails) Check("the cable car rail decals are exported", decalNames != null && decalNames.Contains("r4i_rails_f"));
                }
                foreach (var flag in exporters[0]["flags"]) {
                    if (flag["format"]?.Type != JTokenType.String) continue;
                    Check("a flag with a format is not set by the main format", !File.Exists(Path.Combine(folder, "..", "api_export." + flag["format"])));
                    await ExportPacked(exporters[0], flag);
                }
            }
        }

        //offscreen renders (only with a window: headless, they fail with no_renderer)
        async Task View() {
            if (DisplayServer.GetName() == "headless") {
                await Expect("view.render without a renderer", "no_renderer", "view.render");
                return;
            }
            await Expect("view.render with an unknown camera", "invalid_value", "view.render", new { camera = "fisheye" });
            await Expect("view.render with an unknown layer", "invalid_value", "view.render", new { layers = new[] { "clouds" } });
            var res = await Call("view.render", new { labels = new[] { "intersections" }, width = 800, height = 600, grid = 100, scaleBar = true });
            var image = new Image();
            var loaded = image.LoadPngFromBuffer(System.Convert.FromBase64String(res["image"].Value<string>())) == Error.Ok;
            Check("view.render returns a PNG of the size asked", loaded && image.GetWidth() == 800 && image.GetHeight() == 600);
            var colors = new HashSet<Color>();
            for (int y = 0; loaded && y < 600; y += 37) for (int x = 0; x < 800; x += 41) colors.Add(image.GetPixel(x, y));
            Check("the render isn't uniform (" + colors.Count + " colors sampled)", colors.Count > 10);
            image.SavePng(Path.Combine(folder, "..", "view_top.png"));
            //the roads only (most of the image is background), as wireframe and solid on the same background
            var bg = GameEngine.Camera.WireframeBackground;
            var wireImage = new Image();
            var solidImage = new Image();
            var wire = await Call("view.render", new { width = 400, height = 300, layers = new[] { "geometry" }, wireframe = true });
            var solid = await Call("view.render", new { width = 400, height = 300, layers = new[] { "geometry" }, background = "#" + bg.ToHtml(false) });
            var wireLoaded = wireImage.LoadPngFromBuffer(System.Convert.FromBase64String(wire["image"].Value<string>())) == Error.Ok
                && solidImage.LoadPngFromBuffer(System.Convert.FromBase64String(solid["image"].Value<string>())) == Error.Ok;
            //compared with the solid render, since the background goes through the tonemapping of the environment too
            var corner = wireLoaded ? wireImage.GetPixel(0, 0) : Colors.Transparent;
            var solidCorner = wireLoaded ? solidImage.GetPixel(0, 0) : Colors.Black;
            Check("the wireframe render has the plain background (" + corner.ToHtml(false) + ", " + solidCorner.ToHtml(false) + ")", wireLoaded
                && Mathf.Abs(corner.R - solidCorner.R) < 0.02f && Mathf.Abs(corner.G - solidCorner.G) < 0.02f && Mathf.Abs(corner.B - solidCorner.B) < 0.02f);
            int differing = 0;
            for (int y = 0; wireLoaded && y < 300; y++) for (int x = 0; x < 400; x++) {
                if (!solidImage.GetPixel(x, y).IsEqualApprox(wireImage.GetPixel(x, y))) differing++;
            }
            Check("the wireframe render differs from the solid one (" + differing + " pixels)", differing > 200);
            if (wireLoaded) wireImage.SavePng(Path.Combine(folder, "..", "view_wireframe.png"));

            //the wireframe view of the editor (View menu), with the ambient light of the rendering mode kept on its environment
            var editorCam = builder.GetViewport().GetCamera3D() as CameraController;
            if (editorCam != null) {
                await Call("view.focus", new { @ref = "Intersection 1" });
                var before = await Call("view.screenshot");
                var on = editorCam.ToggleWireframe();
                var during = await Call("view.screenshot");
                var renderingMode = PreferencesManager.Get("renderingMode", 0);
                builder.SetCameraRenderingMode(1);
                var ambientSynced = editorCam.Environment?.AmbientLightColor.IsEqualApprox(Colors.White) == true;
                builder.SetCameraRenderingMode(renderingMode);
                var off = editorCam.ToggleWireframe();
                Check("the editor wireframe view toggles on and off", on && !off && editorCam.Environment == null
                    && builder.GetViewport().DebugDraw == Viewport.DebugDrawEnum.Disabled);
                Check("the editor wireframe view changes the view", before["image"].Value<string>() != during["image"].Value<string>());
                Check("the wireframe view keeps the ambient light of the rendering mode", ambientSynced);
                var duringImage = new Image();
                if (duringImage.LoadPngFromBuffer(System.Convert.FromBase64String(during["image"].Value<string>())) == Error.Ok) {
                    duringImage.SavePng(Path.Combine(folder, "..", "view_editor_wireframe.png"));
                }
            }
            var label = ((JArray)res["labels"]).FirstOrDefault(l => l["name"].Value<string>() == "Intersection 1");
            Check("the labels have the intersections, in the image", label != null && label["inView"].Value<bool>());
            if (label != null) {
                var center = (await Derived(label["id"].Value<int>()))["center"];
                var m = res["mapping"];
                var mpp = m["metersPerPixel"].Value<float>();
                var px = (center[0].Value<float>() - m["origin"][0].Value<float>()) / mpp;
                var py = (m["origin"][1].Value<float>() - center[2].Value<float>()) / mpp;
                Check("the mapping of the top view gives the pixel of the label (" + px.ToString("0") + ", " + py.ToString("0") + ")",
                    Mathf.Abs(px - label["pixel"][0].Value<float>()) < 2 && Mathf.Abs(py - label["pixel"][1].Value<float>()) < 2);
            }
            var orbit = await Call("view.render", new {
                camera = "orbit", elements = new object[] { "Intersection 1" }, margin = 3, highlight = new object[] { "Intersection 1" }, width = 640, height = 400,
                overlays = new object[] { new { type = "marker", position = V(0, 0, 0), text = "origin" }, new { type = "polyline", points = new[] { V(-50, 0, 0), V(50, 0, 0) } } },
                path = Path.Combine(folder, "..", "view_orbit.png")
            });
            Check("view.render saves to the path (" + orbit["path"] + ")", File.Exists(orbit["path"].Value<string>()) && orbit["image"] == null);
            Check("the overlays are placed in the image", ((JArray)orbit["overlays"]).Count == 2 && orbit["overlays"][0]["inView"].Value<bool>());
            Check("the orbit view gives the camera", orbit["mapping"]["projection"].Value<string>() == "perspective" && orbit["mapping"]["position"] is JArray);
        }

        //the validation of the city by the exporter of the core (a dry export): the known perimeter error of the PSDL writer
        //on Intersection 1 (see Files) is reported with its element, and nothing is left in the temporary folder
        async Task Validate() {
            var exporters = (JArray)await Call("exporters.list");
            Check("exporters.list says which exporters validate", exporters.Any(e => e["validates"]?.Value<bool>() == true));
            await Expect("city.validate with an exporter that doesn't exist", "not_found", "city.validate", new { exporter = "nope" });
            var temp = Path.GetTempPath();
            int Leftovers() => Directory.GetDirectories(temp, "km2cb_validate_*").Length + Directory.GetFiles(temp, "km2cb_report_*").Length;
            var before = Leftovers();
            var res = await Call("city.validate");
            var items = (JArray)res["items"];
            Log("     city.validate: " + res["errors"] + " errors, " + res["warnings"] + " warnings in " + res["durationMs"].Value<double>().ToString("0") + " ms");
            Check("city.validate reports the error of Intersection 1", !res["ok"].Value<bool>() && res["errors"].Value<int>() > 0
                && items.Any(i => i["level"].Value<string>() == "error" && i["element"]?["name"]?.Value<string>() == "Intersection 1" && i["exporter"] != null));
            Check("city.validate leaves no temporary files", Leftovers() == before);

            //File > Validate city: the panel lists the items (the menu entries run in an undo step)
            if (DisplayServer.GetName() != "headless") {
                History.UndoManager.Record("Validate city", builder.ValidateCity);
                var waited = 0;
                while (!builder.validationEditorPanel.ActiveSelf() && waited++ < 1200) await Frames(1);
                await Frames(5);
                Check("Validate city opens the validation panel", builder.validationEditorPanel.ActiveSelf());
                Screenshot("validation_panel.png");
                builder.validationEditorPanel.SetActive(false);
            }
        }

        //the MCP server: the messages handled directly, then over HTTP on a test port (from another thread, as a client)
        async Task McpTests() {
            var server = new Mcp.McpServer(ApiHost.Instance.Dispatcher);
            async Task<JObject> Rpc(object message) => (JObject)await server.Handle(Json.ToToken(message));
            var init = await Rpc(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "selftest" } } });
            Check("MCP initialize answers with the version asked and the tools capability",
                init["result"]?["protocolVersion"]?.Value<string>() == "2025-06-18" && init["result"]["capabilities"]["tools"] != null);
            Check("an MCP notification has no response", await server.Handle(Json.ToToken(new { jsonrpc = "2.0", method = "notifications/initialized" })) == null);
            var tools = (JArray)(await Rpc(new { jsonrpc = "2.0", id = 2, method = "tools/list" }))["result"]["tools"];
            Check("MCP tools/list has api_call and view_render (" + tools.Count + " tools)", tools.Any(t => t["name"].Value<string>() == "api_call")
                && tools.Any(t => t["name"].Value<string>() == "view_render" && t["inputSchema"]["properties"]["camera"]["default"]?.Value<string>() == "top"));
            var info = await Rpc(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "city_info", arguments = new { } } });
            Check("MCP city_info gives the city", info["result"]["isError"].Value<bool>() == false && info["result"]["content"][0]["text"].Value<string>().Contains("\"loaded\":true"));
            var wrong = await Rpc(new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "api_call", arguments = new { method = "roads.fly" } } });
            Check("an MCP tool call failing is a tool error", wrong["result"]["isError"].Value<bool>() && wrong["result"]["content"][0]["text"].Value<string>().Contains("unknown_method"));
            var unknown = await Rpc(new { jsonrpc = "2.0", id = 5, method = "teleport" });
            Check("an unknown MCP method is a JSON-RPC error", unknown["error"]?["code"]?.Value<int>() == -32601);
            if (DisplayServer.GetName() != "headless") {
                var render = await Rpc(new { jsonrpc = "2.0", id = 6, method = "tools/call", @params = new { name = "view_render", arguments = new { width = 64, height = 64 } } });
                Check("MCP view_render gives an image", render["result"]["content"][0]["type"].Value<string>() == "image" && render["result"]["content"][1]["type"].Value<string>() == "text");
            }

            //over HTTP
            const string token = "selftest-token";
            var http = Mcp.McpHttp.Start(47913, token);
            Check("the MCP HTTP server starts", http != null);
            if (http == null) return;
            try {
                var results = await Task.Run(async () => {
                    using (var client = new System.Net.Http.HttpClient()) {
                        async Task<(int status, string body)> Post(string body, string auth, string origin = null) {
                            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, http.Url) {
                                Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json")
                            };
                            if (auth != null) request.Headers.Add("Authorization", "Bearer " + auth);
                            if (origin != null) request.Headers.Add("Origin", origin);
                            var response = await client.SendAsync(request);
                            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
                        }
                        var call = "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"city_info\",\"arguments\":{}}}";
                        return new (int status, string body)[] {
                            await Post(call, token),
                            await Post(call, "wrong"),
                            await Post(call, token, "http://evil.example"),
                            await Post("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", token),
                            ((int)(await client.GetAsync(http.Url)).StatusCode, ""),
                        };
                    }
                });
                Check("MCP over HTTP runs a tool", results[0].status == 200 && results[0].body.Contains("\\\"loaded\\\":true"));
                Check("MCP over HTTP needs the token (" + results[1].status + ")", results[1].status == 401);
                Check("MCP over HTTP refuses other origins (" + results[2].status + ")", results[2].status == 403);
                Check("an MCP notification over HTTP is accepted (" + results[3].status + ")", results[3].status == 202);
                Check("MCP over HTTP has no GET (" + results[4].status + ")", results[4].status == 401 || results[4].status == 405);
            } finally {
                http.Close();
            }
        }

        //the export with the format of a flag (MM2: the .ar archive, a zip file): the PATHSET only, with a custom texture
        //of the city, which must be packed with it and leave no loose file or temporary folder
        async Task ExportPacked(JToken exporter, JToken flag) {
            var format = flag["format"].Value<string>();
            var id = flag["id"].Value<string>();
            var outPath = Path.Combine(folder, "..", "api_export_packed");
            Check("exporters.list lists the format of the flag " + id, exporter["formats"].Any(f => f.Value<string>() == format));
            await Expect("a flag set by the format", "invalid_value", "city.export",
                new { path = outPath, exporter = exporter["format"].Value<string>(), flags = new Dictionary<string, bool> { [id] = true } });

            var known = exporter["flags"].Select(f => f["id"].Value<string>()).ToHashSet();
            var flags = new Dictionary<string, bool> { ["propRules"] = false, ["psdl"] = false, ["inst"] = false, ["bai"] = false,
                ["pathset"] = true, ["cinfo_aimap"] = false, ["hudmap"] = false, ["pkgs"] = false }
                .Where(x => known.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
            var textureFolder = Path.Combine(folder, "texture");
            var createdFolder = !Directory.Exists(textureFolder);
            Directory.CreateDirectory(textureFolder);
            var texture = Path.Combine(textureFolder, "selftest_user.tex");
            File.WriteAllBytes(texture, new byte[] { 1, 2, 3, 4 });
            var tempDirs = Directory.GetDirectories(Path.GetTempPath(), "km2_ar_*").Length;
            try {
                //named with the main format: the packed archive must not be city.psdl.ar
                var export = await Send("city.export", new { path = outPath + "." + exporter["format"].Value<string>(), exporter = format, flags });
                if (export["error"] != null) {
                    Log("FAIL city.export (" + format + "): " + export["error"]);
                    failures++;
                    return;
                }
                var path = export["result"]["path"].Value<string>();
                var entries = new List<string>();
                if (File.Exists(path)) {
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(path)) entries = zip.Entries.Select(e => e.FullName).ToList();
                }
                var name = Path.GetFileNameWithoutExtension(path);
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                Check("city.export with the format " + format + " writes the archive (exit code " + export["result"]["exitCode"] + ")",
                    export["result"]["exitCode"].Value<int>() == 0 && path.EndsWith("." + format) && entries.Contains("city/" + name + "/props.pathset"));
                Check("the extension of the main format is replaced by the packed one (" + Path.GetFileName(path) + ")", name == "api_export_packed");
                Check("the archive has the custom textures of the city", entries.Contains("texture/selftest_user.tex"));
                Check("the archive has no placeholder files", !entries.Any(e => Path.GetFileName(e).StartsWith("_put ")));
                Check("the packed export leaves no loose files", !File.Exists(Path.Combine(dir, "city", name + ".psdl")) && !Directory.Exists(Path.Combine(dir, "city", name))
                    && !File.Exists(Path.ChangeExtension(path, ".json")) && !File.Exists(path + ".tmp"));
                Check("the temporary folder of the packed export is deleted", Directory.GetDirectories(Path.GetTempPath(), "km2_ar_*").Length <= tempDirs);
            } finally {
                File.Delete(texture);
                if (createdFolder) Directory.Delete(textureFolder);
            }
        }
    }
}

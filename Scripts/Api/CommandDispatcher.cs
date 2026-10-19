using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Api {
    //Runs the JSON commands on the CityApi, one at a time on the main thread.
    //Request: {"id": 1, "method": "roads.create", "params": {...}}
    //Response: {"id": 1, "result": ...} or {"id": 1, "error": {"code": "...", "message": "...", "details": ...}}
    //Events sent while a command runs: {"event": "progress", "for": 1, "value": 0.5, "text": "..."}
    //A batch runs several commands as a single undo step:
    //  {"id": 2, "method": "batch", "params": {"commands": [{"method": ..., "params": ...}, ...], "atomic": true}}
    //  the result is the list of their results; with atomic, if one fails the ones before it are undone
    public class CommandDispatcher {
        public class Method {
            public string name;
            public string description;
            public MethodInfo info;
        }

        //the methods marked with ApiMethod, by name (also read by the command line before the API exists)
        static Dictionary<string, Method> methods;

        public static IReadOnlyDictionary<string, Method> Methods {
            get {
                if (methods == null) {
                    var res = new Dictionary<string, Method>();
                    foreach (var m in typeof(CityApi).GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
                        var attr = m.GetCustomAttribute<ApiMethodAttribute>();
                        if (attr == null) continue;
                        res[attr.name] = new Method { name = attr.name, description = attr.description, info = m };
                    }
                    methods = res;
                }
                return methods;
            }
        }

        //the methods handled by the dispatcher itself
        public static readonly string[] BuiltinMethods = { "api.methods", "batch" };

        readonly CityApi api;
        readonly ConcurrentQueue<(string json, System.Action<string> reply, bool remote)> queue = new ConcurrentQueue<(string, System.Action<string>, bool)>();
        //released once for every queued command
        readonly System.Threading.SemaphoreSlim available = new System.Threading.SemaphoreSlim(0);
        readonly int mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        bool running = false;

        //the time per frame the commands can take (the program stays responsive meanwhile)
        public double frameBudgetMs = 8;

        public CommandDispatcher(CityApi api) {
            this.api = api;
        }

        public bool IsIdle => !running && queue.IsEmpty;

        //can be called from any thread: the command runs on the main thread (see Pump), the reply and the events of the
        //command are called there too
        public void Enqueue(string json, System.Action<string> reply) {
            queue.Enqueue((json, reply, System.Threading.Thread.CurrentThread.ManagedThreadId != mainThread));
            available.Release();
        }

        //runs the queued commands, one at a time, for up to the frame budget (called every frame by ApiHost). A command
        //lasting several frames ends the pumping until it is done. After a command sent from another thread, the next
        //one is waited for within the budget, so that a script waiting for each reply runs several commands per frame.
        public void Pump() {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var waitForNext = false;
            while (!running) {
                var remaining = frameBudgetMs - watch.Elapsed.TotalMilliseconds;
                if (remaining <= 0) return;
                if (!available.Wait(waitForNext ? (int)System.Math.Ceiling(remaining) : 0)) return;
                if (!queue.TryDequeue(out var item)) return;
                waitForNext = item.remote;
                Run(item.json, item.reply);
            }
        }

        async void Run(string json, System.Action<string> reply) {
            running = true;
            try {
                var res = await Execute(json, x => reply(x.ToString(Formatting.None)));
                reply(res.ToString(Formatting.None));
            } catch (System.Exception e) {
                GameEngine.LogError("API: " + e);
            } finally {
                running = false;
            }
        }

        static JObject Error(JToken id, string code, string message, object details = null) {
            var error = new JObject { ["code"] = code, ["message"] = message };
            if (details != null) error["details"] = Json.ToToken(details);
            return new JObject { ["id"] = id?.DeepClone(), ["error"] = error };
        }

        //runs a command (a JSON request), returns the response
        public async Task<JObject> Execute(string json, System.Action<JObject> emit = null) {
            JObject request;
            try {
                request = JObject.Parse(json);
            } catch (JsonException e) {
                return Error(null, "invalid_request", "invalid JSON: " + e.Message);
            }
            return await Execute(request, emit);
        }

        public async Task<JObject> Execute(JObject request, System.Action<JObject> emit = null) {
            var id = request["id"];
            var name = request["method"]?.Value<string>();
            if (name == null) return Error(id, "invalid_request", "missing method");
            var parameters = request["params"] as JObject ?? new JObject();
            if (request["params"] != null && request["params"].Type != JTokenType.Object && request["params"].Type != JTokenType.Null) {
                return Error(id, "invalid_request", "params must be an object");
            }
            api.progress = new EventProgress(value => emit?.Invoke(new JObject { ["event"] = "progress", ["for"] = id?.DeepClone(), ["value"] = value.value, ["text"] = value.text }));
            try {
                object result;
                if (name == "batch") result = await RunBatch(parameters);
                else if (name == "api.methods") result = ListMethods();
                else result = await Invoke(name, parameters);
                return new JObject { ["id"] = id?.DeepClone(), ["result"] = Json.ToToken(result) };
            } catch (BatchFailure e) {
                var inner = e.InnerException as ApiException;
                return Error(id, inner?.code ?? "internal_error", e.InnerException.Message, new { index = e.index, details = inner?.details });
            } catch (ApiException e) {
                return Error(id, e.code, e.Message, e.details);
            } catch (System.Exception e) {
                GameEngine.LogError("API: error in " + name + ": " + e);
                return Error(id, "internal_error", e.Message);
            } finally {
                api.progress = null;
            }
        }

        class EventProgress : System.IProgress<(float value, string text)> {
            readonly System.Action<(float value, string text)> action;

            public EventProgress(System.Action<(float value, string text)> action) {
                this.action = action;
            }

            public void Report((float value, string text) value) {
                action(value);
            }
        }

        class BatchFailure : System.Exception {
            public readonly int index;

            public BatchFailure(int index, System.Exception inner) : base(inner.Message, inner) {
                this.index = index;
            }
        }

        async Task<object> RunBatch(JObject parameters) {
            if (!(parameters["commands"] is JArray commands)) throw ApiException.InvalidParam("missing commands");
            var atomic = parameters["atomic"]?.Value<bool>() ?? true;
            var list = new List<System.Func<Task<object>>>();
            for (int i = 0; i < commands.Count; i++) {
                var index = i;
                if (!(commands[i] is JObject command)) throw ApiException.InvalidParam("command " + i + " is not an object");
                var name = command["method"]?.Value<string>();
                if (name == null || name == "batch") throw ApiException.InvalidParam("invalid method in command " + i);
                var commandParams = command["params"] as JObject ?? new JObject();
                list.Add(async () => {
                    try {
                        return await Invoke(name, commandParams);
                    } catch (System.Exception e) {
                        throw new BatchFailure(index, e);
                    }
                });
            }
            return await api.RunBatch(list, atomic);
        }

        public static List<object> ListMethods() {
            var res = new List<object>();
            var names = new List<string>(Methods.Keys);
            names.Sort(System.StringComparer.Ordinal);
            foreach (var n in names) {
                var m = Methods[n];
                var ps = new List<object>();
                foreach (var p in m.info.GetParameters()) {
                    ps.Add(new { name = p.Name, type = TypeName(p.ParameterType), optional = p.HasDefaultValue, defaultValue = p.HasDefaultValue ? p.DefaultValue : null });
                }
                res.Add(new { name = n, description = m.description, @params = ps, returns = TypeName(m.info.ReturnType) });
            }
            return res;
        }

        public static string TypeName(System.Type t) {
            if (t == typeof(void) || t == typeof(Task)) return "null";
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>)) return TypeName(t.GetGenericArguments()[0]);
            var nullable = System.Nullable.GetUnderlyingType(t);
            if (nullable != null) return TypeName(nullable);
            if (t == typeof(int)) return "int";
            if (t == typeof(float) || t == typeof(double)) return "number";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t == typeof(Godot.Vector3)) return "vec3";
            if (t == typeof(Godot.Quaternion)) return "quaternion";
            if (t == typeof(ElementRef)) return "ref";
            if (t == typeof(PointTarget)) return "point";
            if (t == typeof(ObjectState)) return "state";
            if (t == typeof(JToken)) return "any";
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return TypeName(t.GetGenericArguments()[0]) + "[]";
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return "{string: " + TypeName(t.GetGenericArguments()[1]) + "}";
            return "object";
        }

        async Task<object> Invoke(string name, JObject parameters) {
            if (!Methods.TryGetValue(name, out var method)) throw new ApiException("unknown_method", "unknown method '" + name + "'");
            var infos = method.info.GetParameters();
            var args = new object[infos.Length];
            var known = new HashSet<string>();
            for (int i = 0; i < infos.Length; i++) {
                var p = infos[i];
                known.Add(p.Name);
                var token = parameters[p.Name];
                if (token == null) {
                    if (!p.HasDefaultValue) throw ApiException.InvalidParam("missing parameter '" + p.Name + "'");
                    args[i] = p.DefaultValue;
                    continue;
                }
                if (p.ParameterType == typeof(JToken)) {
                    args[i] = token;
                    continue;
                }
                try {
                    args[i] = Json.ToObject(token, p.ParameterType);
                } catch (ApiException) {
                    throw;
                } catch (System.Exception e) when (e is JsonException || e is System.FormatException || e is System.InvalidCastException || e is System.ArgumentException) {
                    throw ApiException.InvalidValue("invalid value for '" + p.Name + "': " + e.Message);
                }
                if (args[i] == null && token.Type != JTokenType.Null && p.ParameterType.IsValueType && System.Nullable.GetUnderlyingType(p.ParameterType) == null) {
                    throw ApiException.InvalidValue("invalid value for '" + p.Name + "'");
                }
            }
            foreach (var prop in parameters.Properties()) {
                if (!known.Contains(prop.Name)) throw ApiException.InvalidParam("unknown parameter '" + prop.Name + "' of " + name);
            }
            object res;
            try {
                res = method.info.Invoke(api, args);
            } catch (TargetInvocationException e) {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
            if (res is Task task) {
                await task;
                var resultProperty = task.GetType().GetProperty("Result");
                return method.info.ReturnType.IsGenericType ? resultProperty?.GetValue(task) : null;
            }
            return res;
        }
    }
}

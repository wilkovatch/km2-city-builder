using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mcp {
    //A Model Context Protocol server (JSON-RPC 2.0) giving the scripting API to AI agents: a few tools (the generic
    //api_call, api_methods, api_batch and script_run, and tools for the most used methods, view_render returning the
    //image itself) and the pages of the wiki as resources. It has no transport of its own: the mcp command of the
    //command line (McpStdio) and the HTTP endpoint of the editor (McpHttp) pass it the messages. Thread-safe: the
    //commands run on the main thread through the dispatcher of the API.
    public class McpServer {
        public const string Name = "km2-city-builder";
        //newest first: the one asked by the client if known, the newest otherwise
        static readonly string[] ProtocolVersions = { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

        //the API methods with a tool of their own (the others are called with api_call)
        static readonly string[] MethodTools = { "city.info", "city.validate", "elements.list", "elements.get", "view.render", "view.screenshot", "view.focus" };
        //the longest text of a result (the rest is cut)
        const int MaxText = 100000;

        const string Instructions =
            "km2 City Builder is an editor of cities for videogames (Midtown Madness 2 through its core): roads, intersections, " +
            "terrain patches, building lines, props and meshes, generated from their parameters and exported to the files of the game. " +
            "The tools drive the program through its scripting API. Start with city_info (city.new or city.load with api_call to open one). " +
            "api_methods lists every method with its parameters, api_call runs any of them, api_batch runs several as one undo step. " +
            "Coordinates are in meters, y is up; in the top view of view_render x goes right and z up. Elements are referenced by id " +
            "(integer), by name (string) or by an object (see the Scripting-API resource). Every change is an undo step (history.undo). " +
            "Look at the city with view_render (labels: names and ids of the elements, grid: coordinates) and check it with city_validate " +
            "before exporting. For many changes, script_run runs a Python script with the citybuilder module (see the Python-Scripts resource).";

        readonly Api.CommandDispatcher dispatcher;
        int nextId = 0;

        public McpServer(Api.CommandDispatcher dispatcher) {
            this.dispatcher = dispatcher;
        }

        // JSON-RPC

        static JObject Response(JToken id, JToken result) {
            return new JObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };
        }

        static JObject ErrorResponse(JToken id, int code, string message) {
            return new JObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JObject { ["code"] = code, ["message"] = message } };
        }

        class RpcException : System.Exception {
            public readonly int code;

            public RpcException(int code, string message) : base(message) {
                this.code = code;
            }
        }

        //handles a message (text): the response to send, null for notifications and responses
        public async Task<string> Handle(string text, System.Action<JObject> notify = null) {
            JToken message;
            try {
                message = JToken.Parse(text);
            } catch (JsonReaderException e) {
                return ErrorResponse(null, -32700, "parse error: " + e.Message).ToString(Formatting.None);
            }
            var res = await Handle(message, notify);
            return res?.ToString(Formatting.None);
        }

        //a request, a notification or a batch of them
        public async Task<JToken> Handle(JToken message, System.Action<JObject> notify = null) {
            if (message is JArray batch) {
                var responses = new JArray();
                foreach (var m in batch) {
                    var r = await HandleOne(m, notify);
                    if (r != null) responses.Add(r);
                }
                return responses.Count > 0 ? responses : null;
            }
            return await HandleOne(message, notify);
        }

        async Task<JObject> HandleOne(JToken message, System.Action<JObject> notify) {
            if (!(message is JObject request) || request["method"]?.Type != JTokenType.String) {
                //a response of the client (no request is sent to it) is ignored
                if (message is JObject o && (o["result"] != null || o["error"] != null)) return null;
                return ErrorResponse(null, -32600, "invalid request");
            }
            var id = request["id"];
            var isNotification = id == null;
            var method = request["method"].Value<string>();
            var prms = request["params"] as JObject ?? new JObject();
            try {
                JToken result;
                switch (method) {
                    case "initialize": result = Initialize(prms); break;
                    case "ping": result = new JObject(); break;
                    case "tools/list": result = new JObject { ["tools"] = Tools() }; break;
                    case "tools/call": result = await CallTool(prms, notify); break;
                    case "resources/list": result = new JObject { ["resources"] = Resources() }; break;
                    case "resources/read": result = ReadResource(prms); break;
                    case "resources/templates/list": result = new JObject { ["resourceTemplates"] = new JArray() }; break;
                    case "prompts/list": result = new JObject { ["prompts"] = new JArray() }; break;
                    default:
                        //notifications/initialized, notifications/cancelled... need no answer
                        if (isNotification) return null;
                        throw new RpcException(-32601, "method not found: " + method);
                }
                return isNotification ? null : Response(id, result);
            } catch (RpcException e) {
                return isNotification ? null : ErrorResponse(id, e.code, e.Message);
            } catch (System.Exception e) {
                GameEngine.LogError("MCP: error in " + method + ": " + e);
                return isNotification ? null : ErrorResponse(id, -32603, e.Message);
            }
        }

        JObject Initialize(JObject prms) {
            var asked = prms["protocolVersion"]?.Value<string>();
            var version = System.Array.IndexOf(ProtocolVersions, asked) >= 0 ? asked : ProtocolVersions[0];
            var client = prms["clientInfo"]?["name"]?.Value<string>() ?? "a client";
            _ = Api.ApiHost.Instance?.RunOnMainThread(() => {
                GameEngine.Print("MCP: " + client + " connected");
                return Task.FromResult(0);
            });
            return new JObject {
                ["protocolVersion"] = version,
                ["capabilities"] = new JObject { ["tools"] = new JObject(), ["resources"] = new JObject() },
                ["serverInfo"] = new JObject { ["name"] = Name, ["title"] = "km2 City Builder", ["version"] = GameEngine.Version.GetVersion() },
                ["instructions"] = Instructions
            };
        }

        // API calls

        //runs an API command through the dispatcher (from any thread), with its progress events
        Task<JObject> Call(string method, JObject prms, System.Action<float, string> progress = null) {
            var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new JObject { ["id"] = System.Threading.Interlocked.Increment(ref nextId), ["method"] = method, ["params"] = prms ?? new JObject() };
            dispatcher.Enqueue(request.ToString(Formatting.None), reply => {
                var o = JObject.Parse(reply);
                if (o["event"] != null) {
                    if (o["event"].Value<string>() == "progress") progress?.Invoke(o["value"]?.Value<float>() ?? 0, o["text"]?.Value<string>());
                } else {
                    tcs.TrySetResult(o);
                }
            });
            return tcs.Task;
        }

        // Tools

        static JObject Tool(string name, string title, string description, JObject properties, params string[] required) {
            var schema = new JObject { ["type"] = "object", ["properties"] = properties };
            if (required.Length > 0) schema["required"] = new JArray(required);
            return new JObject { ["name"] = name, ["title"] = title, ["description"] = description, ["inputSchema"] = schema };
        }

        static string ToolName(string method) => method.Replace('.', '_');

        JArray Tools() {
            var res = new JArray {
                Tool("api_call", "Call an API method",
                    "Runs a method of the scripting API (api_methods lists them with their parameters) and returns its result. " +
                    "A change is an undo step; on error nothing is changed.",
                    new JObject {
                        ["method"] = new JObject { ["type"] = "string", ["description"] = "e.g. roads.create, elements.list, history.undo" },
                        ["params"] = new JObject { ["type"] = "object", ["description"] = "the parameters, by name" }
                    }, "method"),
                Tool("api_methods", "List the API methods",
                    "The methods of the scripting API with their parameters (name: type = default) and a description, optionally only the ones starting with a prefix (e.g. roads.)",
                    new JObject { ["prefix"] = new JObject { ["type"] = "string" } }),
                Tool("api_batch", "Run API methods as one step",
                    "Runs several API methods as a single undo step; atomic (default): if one fails, the ones before it are undone. Returns the list of their results.",
                    new JObject {
                        ["commands"] = new JObject {
                            ["type"] = "array",
                            ["items"] = new JObject {
                                ["type"] = "object",
                                ["properties"] = new JObject { ["method"] = new JObject { ["type"] = "string" }, ["params"] = new JObject { ["type"] = "object" } },
                                ["required"] = new JArray("method")
                            }
                        },
                        ["atomic"] = new JObject { ["type"] = "boolean", ["default"] = true }
                    }, "commands"),
                Tool("script_run", "Run a Python script",
                    "Runs a Python script in the program, as from its Scripts menu: code (the source) or path (a .py file or a script folder), " +
                    "with the inputs of its panel. The script uses the citybuilder module (import citybuilder as cb; cb.api.roads.create(...)); " +
                    "its changes are one undo step, undone if it fails. Returns success, the error and what it printed.",
                    new JObject {
                        ["code"] = new JObject { ["type"] = "string" },
                        ["path"] = new JObject { ["type"] = "string" },
                        ["inputs"] = new JObject { ["type"] = "object" }
                    }),
            };
            foreach (var method in MethodTools) {
                if (!Api.CommandDispatcher.Methods.TryGetValue(method, out var m)) continue;
                var properties = new JObject();
                var required = new List<string>();
                foreach (var p in m.info.GetParameters()) {
                    var schema = SchemaOf(p.ParameterType);
                    if (p.HasDefaultValue && p.DefaultValue != null && (p.DefaultValue is string || p.DefaultValue.GetType().IsPrimitive)) schema["default"] = JToken.FromObject(p.DefaultValue);
                    properties[p.Name] = schema;
                    if (!p.HasDefaultValue) required.Add(p.Name);
                }
                res.Add(Tool(ToolName(method), method, m.description + " (the API method " + method + ")", properties, required.ToArray()));
            }
            return res;
        }

        //the JSON schema of a parameter of an API method
        static JObject SchemaOf(System.Type t) {
            t = System.Nullable.GetUnderlyingType(t) ?? t;
            if (t == typeof(string)) return new JObject { ["type"] = "string" };
            if (t == typeof(int) || t == typeof(long)) return new JObject { ["type"] = "integer" };
            if (t == typeof(float) || t == typeof(double)) return new JObject { ["type"] = "number" };
            if (t == typeof(bool)) return new JObject { ["type"] = "boolean" };
            if (t == typeof(Godot.Vector3)) return new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" }, ["minItems"] = 3, ["maxItems"] = 3, ["description"] = "[x, y, z]" };
            if (t == typeof(Api.ElementRef)) return new JObject { ["description"] = "an element: its id (integer), its name (string) or a reference object" };
            if (t == typeof(JToken)) return new JObject();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return new JObject { ["type"] = "array", ["items"] = SchemaOf(t.GetGenericArguments()[0]) };
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return new JObject { ["type"] = "object", ["additionalProperties"] = SchemaOf(t.GetGenericArguments()[1]) };
            if (t.IsClass && t.Namespace == "Api") {
                var properties = new JObject();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) properties[f.Name] = SchemaOf(f.FieldType);
                return new JObject { ["type"] = "object", ["properties"] = properties };
            }
            return new JObject { ["type"] = "object" };
        }

        static JObject Text(string text, bool isError = false) {
            if (text.Length > MaxText) text = text.Substring(0, MaxText) + "\n... (cut, " + text.Length + " characters)";
            return new JObject { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } }, ["isError"] = isError };
        }

        static JObject Json(JToken value) => Text(value == null ? "null" : value.ToString(Formatting.None));

        //an API error as a tool error (the agent sees it and can fix the call)
        static JObject ApiError(JObject response) {
            var e = (JObject)response["error"];
            return Text("error [" + e["code"] + "]: " + e["message"] + (e["details"] != null ? "\ndetails: " + e["details"].ToString(Formatting.None) : ""), true);
        }

        async Task<JObject> CallTool(JObject prms, System.Action<JObject> notify) {
            var name = prms["name"]?.Value<string>() ?? throw new RpcException(-32602, "missing tool name");
            var args = prms["arguments"] as JObject ?? new JObject();
            var token = prms["_meta"]?["progressToken"];
            System.Action<float, string> progress = null;
            if (token != null && notify != null) {
                progress = (value, text) => notify(new JObject {
                    ["jsonrpc"] = "2.0", ["method"] = "notifications/progress",
                    ["params"] = new JObject { ["progressToken"] = token.DeepClone(), ["progress"] = value, ["total"] = 1, ["message"] = text }
                });
            }
            switch (name) {
                case "api_call": {
                    var method = args["method"]?.Value<string>();
                    if (string.IsNullOrEmpty(method)) return Text("missing method", true);
                    return await RunMethod(method, args["params"] as JObject ?? new JObject(), progress);
                }
                case "api_methods":
                    return Text(Cli.CliCommands.MethodList(args["prefix"]?.Value<string>() ?? ""));
                case "api_batch": {
                    var res = await Call("batch", new JObject { ["commands"] = args["commands"], ["atomic"] = args["atomic"] ?? true }, progress);
                    return res["error"] != null ? ApiError(res) : Json(res["result"]);
                }
                case "script_run":
                    return await RunScript(args);
            }
            foreach (var method in MethodTools) {
                if (ToolName(method) == name) return await RunMethod(method, args, progress);
            }
            throw new RpcException(-32602, "unknown tool: " + name);
        }

        async Task<JObject> RunMethod(string method, JObject prms, System.Action<float, string> progress) {
            var res = await Call(method, prms, progress);
            if (res["error"] != null) return ApiError(res);
            //an image (view.render, view.screenshot) is given as an image, with the rest of the result as text
            if (res["result"] is JObject result && result["image"]?.Type == JTokenType.String && result["format"] != null) {
                var data = result["image"].Value<string>();
                var mime = result["format"].Value<string>() == "jpg" ? "image/jpeg" : "image/png";
                result.Remove("image");
                return new JObject {
                    ["content"] = new JArray {
                        new JObject { ["type"] = "image", ["data"] = data, ["mimeType"] = mime },
                        new JObject { ["type"] = "text", ["text"] = result.ToString(Formatting.None) }
                    },
                    ["isError"] = false
                };
            }
            return Json(res["result"]);
        }

        //runs the script on the main thread (not through the dispatcher, which runs the commands of the script meanwhile)
        async Task<JObject> RunScript(JObject args) {
            var code = args["code"]?.Value<string>();
            var path = args["path"]?.Value<string>();
            if ((code == null) == (path == null)) return Text("give either code or path", true);
            string temp = null;
            if (code != null) {
                temp = Path.Combine(Path.GetTempPath(), "km2cb_mcp_" + System.Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                path = Path.Combine(temp, "mcp_script.py");
                File.WriteAllText(path, code);
            }
            try {
                var host = Api.ApiHost.Instance;
                var res = await host.RunOnMainThread(async () => {
                    var script = ScriptCatalog.FromPath(path);
                    var inputs = ScriptCatalog.ToInputs(ScriptCatalog.DefaultValues(script));
                    if (args["inputs"] is JObject given) inputs.Merge(given, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                    return await Api.ScriptRunner.Run(host.Api.builder, script, inputs, null, false);
                });
                var messages = new JArray();
                foreach (var (title, text) in res.messages) messages.Add(new JObject { ["title"] = title, ["text"] = text });
                var output = res.output ?? "";
                if (output.Length > MaxText / 2) output = "... (cut)\n" + output.Substring(output.Length - MaxText / 2);
                var result = new JObject {
                    ["success"] = res.success, ["cancelled"] = res.cancelled, ["exitCode"] = res.exitCode, ["error"] = res.error,
                    ["undone"] = res.undone, ["messages"] = messages, ["output"] = output
                };
                return Text(result.ToString(Formatting.None), !res.success);
            } catch (System.Exception e) when (e is IOException || e is JsonException || e is System.UnauthorizedAccessException) {
                return Text("could not run the script: " + e.Message, true);
            } finally {
                if (temp != null) {
                    try {
                        Directory.Delete(temp, true);
                    } catch (IOException) {
                    }
                }
            }
        }

        // Resources: the pages of the wiki

        static string WikiFolder() {
            var dir = Path.Combine(PathHelper.BasePath(), "wiki");
            return Directory.Exists(dir) ? dir : null;
        }

        static JArray Resources() {
            var res = new JArray();
            var dir = WikiFolder();
            if (dir == null) return res;
            var files = new List<string>(Directory.GetFiles(dir, "*.md"));
            files.Sort(System.StringComparer.Ordinal);
            foreach (var file in files) {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("_")) continue;
                res.Add(new JObject {
                    ["uri"] = "km2cb://wiki/" + name, ["name"] = name, ["title"] = name.Replace('-', ' '),
                    ["description"] = "The " + name.Replace('-', ' ') + " page of the documentation", ["mimeType"] = "text/markdown"
                });
            }
            return res;
        }

        static JObject ReadResource(JObject prms) {
            var uri = prms["uri"]?.Value<string>() ?? throw new RpcException(-32602, "missing uri");
            const string prefix = "km2cb://wiki/";
            var dir = WikiFolder();
            var name = uri.StartsWith(prefix) ? uri.Substring(prefix.Length) : null;
            if (dir == null || string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\', '.' }) >= 0 || !File.Exists(Path.Combine(dir, name + ".md"))) {
                throw new RpcException(-32002, "resource not found: " + uri);
            }
            return new JObject {
                ["contents"] = new JArray { new JObject { ["uri"] = uri, ["mimeType"] = "text/markdown", ["text"] = File.ReadAllText(Path.Combine(dir, name + ".md")) } }
            };
        }
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cli {
    //The commands of the command line. Most of them are API calls: the options of a command mapped to an API method are
    //its parameters (terrainSize is --terrain-size), read with CommandLine.ToValue.
    public static class CliCommands {
        public enum OptionKind { None, Flag, Value }

        public class Command {
            public string name;
            public string usage = "";
            public string summary;
            public string[] positionals = new string[0];
            public int optionalPositionals = 0;
            //name=value arguments after the positionals (call)
            public bool pairs = false;
            //the API method whose parameters are options too
            public string method;
            //its parameters given otherwise (by the positionals)
            public string[] bound = new string[0];
            //option names of its parameters other than the default ones (option -> parameter)
            public Dictionary<string, string> renamed = new Dictionary<string, string>();
            public string[] values = new string[0];
            public string[] flags = new string[0];
            //the command quits the program itself when done (the self-tests)
            public bool quitsItself = false;
            public System.Action<Invocation> validate;
            public System.Func<CliContext, Task<int>> run;

            //the options given by the parameters of the method: option name and parameter
            public List<(string option, ParameterInfo param)> MethodOptions() {
                var res = new List<(string, ParameterInfo)>();
                if (method == null) return res;
                var reverse = new Dictionary<string, string>();
                foreach (var pair in renamed) reverse[pair.Value] = pair.Key;
                foreach (var p in CommandLine.ParametersOf(method)) {
                    if (System.Array.IndexOf(bound, p.Name) >= 0) continue;
                    res.Add((reverse.TryGetValue(p.Name, out var option) ? option : CommandLine.ToOption(p.Name), p));
                }
                return res;
            }

            public OptionKind KindOf(string option) {
                if (System.Array.IndexOf(flags, option) >= 0) return OptionKind.Flag;
                if (System.Array.IndexOf(values, option) >= 0) return OptionKind.Value;
                foreach (var (name, p) in MethodOptions()) {
                    if (name == option) return CommandLine.ValueType(p.ParameterType) == typeof(bool) ? OptionKind.Flag : OptionKind.Value;
                }
                return OptionKind.None;
            }

            public void Validate(Invocation inv) {
                foreach (var (option, p) in MethodOptions()) {
                    if (!p.HasDefaultValue && !inv.Has(option)) throw new UsageException(name + " needs --" + option);
                }
                validate?.Invoke(inv);
            }

            //the parameters of the method given with the options
            public JObject MethodParams(Invocation inv) {
                var res = new JObject();
                foreach (var (option, p) in MethodOptions()) {
                    if (!inv.Has(option)) continue;
                    var type = CommandLine.ValueType(p.ParameterType);
                    res[p.Name] = type == typeof(bool) ? new JValue(inv.Flag(option)) : CommandLine.ToValue(inv.Get(option), type);
                }
                return res;
            }
        }

        static readonly string[] Output = { "output" };
        static readonly string[] OutputFlags = { "pretty", "progress" };
        static readonly string[] LoadFlags = { "migrate", "install-python-modules" };
        static readonly string[] ChangeFlags = { "save", "no-history" };

        static string[] Join(params string[][] lists) {
            var res = new List<string>();
            foreach (var list in lists) res.AddRange(list);
            return res.ToArray();
        }

        static readonly Dictionary<string, string> OptionHelp = new Dictionary<string, string> {
            ["output"] = "write the result to this file instead of the standard output",
            ["pretty"] = "indent the JSON",
            ["progress"] = "write the progress of the long operations to the standard error",
            ["city"] = "load the city of this folder first",
            ["migrate"] = "migrate the city if it was made with another version of its core (its files are changed)",
            ["install-python-modules"] = "install the Python modules the core needs if missing",
            ["save"] = "save the city if the command succeeds",
            ["no-history"] = "don't record the changes as undo steps (faster)",
            ["params"] = "the parameters as a JSON object, or @file (name=value arguments override them)",
            ["keep-going"] = "run the remaining commands after an error",
            ["json"] = "print JSON instead of text",
            ["inputs"] = "a JSON file with input values of the script (the others keep their default)",
            ["mode"] = "new (default): a new city in the folder; split: only the roads connected to the middle of other roads, on a new city; props, ui, load: on a copy of an existing city",
            ["flag"] = "an exporter flag, as id=true or id=false (repeatable, see the exporters of the info command)",
            ["out"] = "the output path",
        };

        static readonly Dictionary<string, string> Placeholders = new Dictionary<string, string> {
            ["output"] = "<file>",
            ["city"] = "<folder>",
            ["params"] = "<json or @file>",
            ["inputs"] = "<file>",
            ["mode"] = "<mode>",
            ["flag"] = "<id=bool>",
        };

        static List<Command> commands;

        public static List<Command> All {
            get {
                if (commands == null) commands = Create();
                return commands;
            }
        }

        public static Command Find(string name) {
            foreach (var c in All) {
                if (c.name == name) return c;
            }
            return null;
        }

        //the subcommands of a command made of two words (generate terrain, generate buildings...)
        public static List<string> Group(string first) {
            var res = new List<string>();
            foreach (var c in All) {
                if (c.name.StartsWith(first + " ")) res.Add(c.name.Substring(first.Length + 1));
            }
            return res;
        }

        static List<Command> Create() {
            return new List<Command> {
                new Command {
                    name = "help", usage = "[command]", summary = "Shows the commands, or the arguments and options of one",
                    positionals = new[] { "command", "subcommand" }, optionalPositionals = 2,
                    validate = inv => {
                        if (inv.positionals.Count == 0 || FindHelp(inv.positionals) != null) return;
                        if (inv.positionals.Count == 1 && Group(inv.positionals[0]).Count > 0) return;
                        throw new UsageException("unknown command '" + string.Join(" ", inv.positionals) + "'");
                    },
                    run = ctx => {
                        var cmd = FindHelp(ctx.inv.positionals);
                        if (cmd != null) ctx.output.Line(Help(cmd));
                        else if (ctx.inv.positionals.Count == 1) ctx.output.Line(Help(ctx.inv.positionals[0] + " "));
                        else ctx.output.Line(Help(""));
                        return Task.FromResult(0);
                    }
                },
                new Command {
                    name = "version", summary = "The version of the program and of the API protocol",
                    values = Output, flags = OutputFlags,
                    run = async ctx => {
                        ctx.output.Result(await ctx.Call("api.version"));
                        return 0;
                    }
                },
                new Command {
                    name = "methods", usage = "[prefix]", summary = "Lists the API methods (optionally the ones starting with the prefix) with their parameters",
                    positionals = new[] { "prefix" }, optionalPositionals = 1,
                    values = Output, flags = Join(new[] { "json" }, OutputFlags),
                    run = ctx => {
                        var prefix = ctx.inv.positionals.Count > 0 ? ctx.inv.positionals[0] : "";
                        if (ctx.inv.Flag("json")) {
                            var list = new JArray();
                            foreach (var m in Api.CommandDispatcher.ListMethods()) {
                                var token = Api.Json.ToToken(m);
                                if (token["name"].Value<string>().StartsWith(prefix)) list.Add(token);
                            }
                            ctx.output.Result(list);
                        } else {
                            ctx.output.Line(MethodList(prefix));
                        }
                        return Task.FromResult(0);
                    }
                },
                new Command {
                    name = "call", usage = "<method> [name=value ...]", summary = "Runs an API method and prints its result",
                    positionals = new[] { "method" }, pairs = true,
                    values = Join(new[] { "params", "city" }, Output), flags = Join(LoadFlags, ChangeFlags, OutputFlags),
                    validate = ValidateCall,
                    run = Call
                },
                new Command {
                    name = "run", usage = "<file or ->", summary = "Runs the JSON commands of a file (or of the standard input), one per line, and prints the responses",
                    positionals = new[] { "file" },
                    values = Join(new[] { "city" }, Output), flags = Join(new[] { "keep-going" }, LoadFlags, ChangeFlags),
                    run = Run
                },
                new Command {
                    name = "serve", summary = "Reads JSON commands from the standard input and writes the responses to the standard output, one per line, until the end of the input",
                    values = new[] { "city" }, flags = Join(LoadFlags, new[] { "no-history" }),
                    run = Serve
                },
                new Command {
                    name = "cores", summary = "Lists the installed cores",
                    values = Output, flags = OutputFlags,
                    run = async ctx => {
                        ctx.output.Result(await ctx.Call("cores.list"));
                        return 0;
                    }
                },
                new Command {
                    name = "templates", usage = "<core>", summary = "Lists the city templates of a core",
                    positionals = new[] { "core" },
                    values = Output, flags = OutputFlags,
                    run = async ctx => {
                        ctx.output.Result(await ctx.Call("cores.templates", new JObject { ["core"] = ctx.inv.positionals[0] }));
                        return 0;
                    }
                },
                new Command {
                    name = "new", usage = "<folder>", summary = "Creates a city in a folder from a template of a core",
                    positionals = new[] { "folder" }, method = "city.new", bound = new[] { "folder" },
                    values = Output, flags = OutputFlags,
                    run = async ctx => {
                        var prms = ctx.inv.command.MethodParams(ctx.inv);
                        prms["folder"] = Path.GetFullPath(ctx.inv.positionals[0]);
                        ctx.output.Result(await ctx.Call("city.new", prms));
                        return 0;
                    }
                },
                new Command {
                    name = "info", usage = "<city>", summary = "The city of a folder (core, terrain, element counts) and the exporters of its core",
                    positionals = new[] { "city" },
                    values = Output, flags = Join(LoadFlags, OutputFlags),
                    run = async ctx => {
                        await ctx.Prepare(ctx.inv.positionals[0]);
                        ctx.output.Result(new JObject { ["city"] = await ctx.Call("city.info"), ["exporters"] = await ctx.Call("exporters.list") });
                        return 0;
                    }
                },
                new Command {
                    name = "export", usage = "<city>", summary = "Exports a city with an exporter of its core",
                    positionals = new[] { "city" }, method = "city.export", renamed = new Dictionary<string, string> { ["out"] = "path" },
                    values = Join(new[] { "flag" }, Output), flags = Join(LoadFlags, OutputFlags),
                    run = async ctx => {
                        var prms = ctx.inv.command.MethodParams(ctx.inv);
                        prms["path"] = Path.GetFullPath(prms["path"].Value<string>());
                        if (ctx.inv.Has("flag")) {
                            var flags = prms["flags"] as JObject ?? new JObject();
                            foreach (var flag in ctx.inv.All("flag")) {
                                var eq = flag.IndexOf('=');
                                var value = eq < 0 ? "true" : flag.Substring(eq + 1);
                                if (eq == 0 || (value != "true" && value != "false")) throw new UsageException("--flag is id=true or id=false");
                                flags[eq < 0 ? flag : flag.Substring(0, eq)] = value == "true";
                            }
                            prms["flags"] = flags;
                        }
                        await ctx.Prepare(ctx.inv.positionals[0]);
                        ctx.output.Result(await ctx.Call("city.export", prms));
                        return 0;
                    }
                },
                new Command {
                    name = "export-json", usage = "<city>", summary = "Writes the export JSON of a city (the generated geometry, as read by the exporters of the cores)",
                    positionals = new[] { "city" }, method = "city.exportJson", renamed = new Dictionary<string, string> { ["out"] = "path" },
                    values = Output, flags = Join(LoadFlags, OutputFlags),
                    run = async ctx => {
                        var prms = ctx.inv.command.MethodParams(ctx.inv);
                        prms["path"] = Path.GetFullPath(prms["path"].Value<string>());
                        await ctx.Prepare(ctx.inv.positionals[0]);
                        ctx.output.Result(await ctx.Call("city.exportJson", prms));
                        return 0;
                    }
                },
                Generate("terrain", "city.generateTerrain", "Creates terrain patches between the roads of a city"),
                Generate("buildings", "city.generateBuildings", "Creates building lines around the blocks between the roads of a city"),
                new Command {
                    name = "script", usage = "<.py file or script folder>", summary = "Runs a Python script, and exits with its exit code",
                    positionals = new[] { "script" },
                    values = new[] { "inputs", "city" }, flags = LoadFlags,
                    run = Script
                },
                new Command {
                    name = "selftest api", usage = "<new folder>", summary = "Self-test of the scripting API (development)",
                    positionals = new[] { "folder" }, quitsItself = true,
                    run = ctx => {
                        Api.ApiSelfTest.Start(ctx.builder, ctx.inv.positionals[0]);
                        return Task.FromResult(0);
                    }
                },
                new Command {
                    name = "selftest undo", usage = "<folder>", summary = "Self-test of undo and redo (development)",
                    positionals = new[] { "folder" }, values = new[] { "mode" }, quitsItself = true,
                    validate = inv => {
                        var mode = inv.Get("mode", "new");
                        if (System.Array.IndexOf(History.UndoSelfTest.Modes, mode) < 0) throw new UsageException("--mode is one of: " + string.Join(", ", History.UndoSelfTest.Modes));
                    },
                    run = ctx => {
                        History.UndoSelfTest.Start(ctx.builder, ctx.inv.positionals[0], ctx.inv.Get("mode", "new"));
                        return Task.FromResult(0);
                    }
                },
                new Command {
                    name = "screenshots", usage = "<city folder>", summary = "Takes the screenshots of the guide on a copy of a city (development, needs the window)",
                    positionals = new[] { "folder" }, quitsItself = true,
                    run = ctx => {
                        GuideScreenshots.Start(ctx.builder, ctx.inv.positionals[0]);
                        return Task.FromResult(0);
                    }
                },
            };
        }

        static Command Generate(string what, string method, string summary) {
            return new Command {
                name = "generate " + what, usage = "<city>", summary = summary,
                positionals = new[] { "city" }, method = method,
                values = Output, flags = Join(LoadFlags, ChangeFlags, OutputFlags),
                run = async ctx => {
                    var prms = ctx.inv.command.MethodParams(ctx.inv);
                    await ctx.Prepare(ctx.inv.positionals[0]);
                    var res = await ctx.Call(method, prms);
                    await ctx.Finish();
                    ctx.output.Result(res);
                    return 0;
                }
            };
        }

        static Command FindHelp(List<string> words) {
            if (words.Count == 0) return null;
            return Find(string.Join(" ", words));
        }

        // call

        static void ValidateCall(Invocation inv) {
            var method = inv.positionals[0];
            if (System.Array.IndexOf(Api.CommandDispatcher.BuiltinMethods, method) >= 0) return;
            var parameters = CommandLine.ParametersOf(method);
            if (parameters == null) throw new UsageException("unknown method '" + method + "' (the methods command lists them)");
            foreach (var (name, _) in inv.pairs) {
                if (System.Array.Find(parameters, p => p.Name == name) == null) throw new UsageException("unknown parameter '" + name + "' of " + method);
            }
        }

        static async Task<int> Call(CliContext ctx) {
            var inv = ctx.inv;
            var method = inv.positionals[0];
            var parameters = CommandLine.ParametersOf(method);
            var prms = new JObject();
            if (inv.Has("params")) {
                if (!(CommandLine.ParseJson(CommandLine.ReadArgument(inv.Get("params"))) is JObject given)) throw new UsageException("--params is a JSON object");
                prms = given;
            }
            foreach (var (name, text) in inv.pairs) {
                var type = parameters == null ? typeof(JToken) : CommandLine.ValueType(System.Array.Find(parameters, p => p.Name == name).ParameterType);
                prms[name] = CommandLine.ToValue(CommandLine.ReadArgument(text), type);
            }
            await ctx.Prepare(inv.Get("city"));
            var res = await ctx.Call(method, prms);
            await ctx.Finish();
            ctx.output.Result(res);
            return 0;
        }

        // run

        static async Task<int> Run(CliContext ctx) {
            var file = ctx.inv.positionals[0];
            string[] lines;
            try {
                if (file == "-") {
                    using (var reader = new StreamReader(System.Console.OpenStandardInput(), new UTF8Encoding(false))) {
                        lines = reader.ReadToEnd().Split('\n');
                    }
                } else {
                    lines = File.ReadAllLines(file);
                }
            } catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException) {
                throw new UsageException("could not read " + file + ": " + e.Message);
            }
            await ctx.Prepare(ctx.inv.Get("city"));
            var failed = false;
            for (int i = 0; i < lines.Length; i++) {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                JObject response;
                try {
                    var request = JToken.Parse(line) as JObject;
                    if (request == null) {
                        response = new JObject { ["id"] = null, ["error"] = new JObject { ["code"] = "invalid_request", ["message"] = "line " + (i + 1) + " is not a JSON object" } };
                    } else {
                        if (request["id"] == null) request["id"] = i + 1;
                        response = await ctx.dispatcher.Execute(request, e => ctx.output.Line(e.ToString(Formatting.None)));
                    }
                } catch (JsonReaderException e) {
                    response = new JObject { ["id"] = null, ["error"] = new JObject { ["code"] = "invalid_request", ["message"] = "line " + (i + 1) + ": invalid JSON: " + e.Message } };
                }
                ctx.output.Line(response.ToString(Formatting.None));
                if (response["error"] != null) {
                    failed = true;
                    if (!ctx.inv.Flag("keep-going")) break;
                }
            }
            if (failed) return 1;
            await ctx.Finish();
            return 0;
        }

        // serve

        static async Task<int> Serve(CliContext ctx) {
            await ctx.Prepare(ctx.inv.Get("city"));
            var ended = new System.Threading.ManualResetEventSlim(false);
            var reader = new System.Threading.Thread(() => {
                try {
                    using (var input = new StreamReader(System.Console.OpenStandardInput(), new UTF8Encoding(false))) {
                        string line;
                        while ((line = input.ReadLine()) != null) {
                            line = line.Trim();
                            if (line.Length == 0) continue;
                            ctx.dispatcher.Enqueue(line, reply => ctx.output.Line(reply));
                        }
                    }
                } catch (System.Exception e) when (e is IOException || e is System.ObjectDisposedException) {
                    //the input was closed
                }
                ended.Set();
            }) { IsBackground = true, Name = "Command line input" };
            reader.Start();
            //the commands still queued when the input ends are run before quitting
            while (!ended.IsSet || !ctx.dispatcher.IsIdle) await ctx.builder.ToSignal(ctx.builder.GetTree(), "process_frame");
            return 0;
        }

        // script

        static async Task<int> Script(CliContext ctx) {
            ScriptCatalog.ScriptInfo script;
            try {
                script = ScriptCatalog.FromPath(ctx.inv.positionals[0]);
            } catch (System.Exception e) when (e is IOException || e is JsonException || e is System.UnauthorizedAccessException) {
                throw new CliError("not_found", e.Message);
            }
            var inputs = ScriptCatalog.ToInputs(ScriptCatalog.DefaultValues(script));
            if (ctx.inv.Has("inputs")) {
                JObject given;
                try {
                    given = JObject.Parse(File.ReadAllText(ctx.inv.Get("inputs")));
                } catch (System.Exception e) when (e is IOException || e is JsonException || e is System.UnauthorizedAccessException) {
                    throw new UsageException("could not read the inputs: " + e.Message);
                }
                inputs.Merge(given, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            }
            await ctx.Prepare(ctx.inv.Get("city"));
            var res = await Api.ScriptRunner.Run(ctx.builder, script, inputs, null, false);
            return res.success ? 0 : (res.exitCode != 0 ? res.exitCode : 1);
        }

        // Help

        static string Program() {
            var path = OS.GetExecutablePath();
            var exe = Path.GetFileName(path);
            //on Windows the output is seen through the console wrapper of the executable
            foreach (var suffix in new[] { "_console.exe", ".console.exe" }) {
                var console = Path.ChangeExtension(path, null) + suffix;
                if (exe.EndsWith(".exe") && File.Exists(console)) exe = Path.GetFileName(console);
            }
            //the editor binary runs the project of the folder given with --path
            return OS.HasFeature("editor") ? exe + " --path <project folder>" : exe;
        }

        public static string Usage(Command cmd) {
            return "Usage: " + Program() + " --headless --quiet -- " + cmd.name + (cmd.usage.Length > 0 ? " " + cmd.usage : "") + " [options]";
        }

        //the list of the commands (starting with the prefix)
        static string Help(string prefix) {
            var sb = new StringBuilder();
            sb.AppendLine("Usage: " + Program() + " --headless --quiet -- <command> [arguments] [options]");
            sb.AppendLine();
            sb.AppendLine("Commands:");
            var width = 0;
            foreach (var c in All) width = System.Math.Max(width, (c.name + " " + c.usage).Length);
            foreach (var c in All) {
                if (c.name.StartsWith(prefix)) sb.AppendLine("  " + (c.name + " " + c.usage).PadRight(width + 2) + c.summary);
            }
            sb.AppendLine();
            sb.AppendLine("help <command> shows the arguments and options of a command, --help after a command too.");
            sb.AppendLine("The results are written as JSON to the standard output, the errors to the standard error.");
            sb.Append("Exit codes: 0 done, 1 failed, 2 invalid command line (a script exits with its own code).");
            return sb.ToString();
        }

        public static string Help(Command cmd) {
            var sb = new StringBuilder();
            sb.AppendLine(Usage(cmd));
            sb.AppendLine();
            sb.AppendLine(cmd.summary);
            var rows = new List<(string option, string text)>();
            foreach (var (option, p) in cmd.MethodOptions()) {
                var type = CommandLine.ValueType(p.ParameterType);
                var left = "--" + option + (type == typeof(bool) ? "" : " <" + Api.CommandDispatcher.TypeName(p.ParameterType) + ">");
                var text = OptionHelp.TryGetValue(option, out var known) ? known + ", " : "";
                text += p.HasDefaultValue ? "default " + FormatDefault(p.DefaultValue) : "required";
                rows.Add((left, text));
            }
            foreach (var v in cmd.values) rows.Add(("--" + v + " " + Placeholders[v], OptionHelp.TryGetValue(v, out var t) ? t : ""));
            foreach (var f in cmd.flags) rows.Add(("--" + f, OptionHelp.TryGetValue(f, out var t) ? t : ""));
            if (rows.Count > 0) {
                sb.AppendLine();
                sb.AppendLine("Options:");
                var width = 0;
                foreach (var r in rows) width = System.Math.Max(width, r.option.Length);
                foreach (var r in rows) sb.AppendLine("  " + r.option.PadRight(width + 2) + r.text);
            }
            if (cmd.pairs) {
                sb.AppendLine();
                sb.AppendLine("name=value gives a parameter of the method: the string parameters take the text as it is, the others read it");
                sb.AppendLine("as JSON (a text that isn't valid JSON is a string). name=@file reads the value from a file.");
            }
            if (cmd.method != null) {
                sb.AppendLine();
                sb.AppendLine("API method: " + cmd.method + " (" + Api.CommandDispatcher.Methods[cmd.method].description + ")");
            }
            return sb.ToString().TrimEnd();
        }

        static string FormatDefault(object value) {
            if (value == null) return "null";
            if (value is string s) return "'" + s + "'";
            if (value is bool b) return b ? "true" : "false";
            return System.Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        static string MethodList(string prefix) {
            var names = new List<string>(Api.CommandDispatcher.Methods.Keys);
            names.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var n in names) {
                if (!n.StartsWith(prefix)) continue;
                var m = Api.CommandDispatcher.Methods[n];
                var ps = new List<string>();
                foreach (var p in m.info.GetParameters()) {
                    ps.Add(p.Name + ": " + Api.CommandDispatcher.TypeName(p.ParameterType) + (p.HasDefaultValue ? " = " + FormatDefault(p.DefaultValue) : ""));
                }
                sb.AppendLine(n + "(" + string.Join(", ", ps) + ") -> " + Api.CommandDispatcher.TypeName(m.info.ReturnType));
                if (m.description != null) sb.AppendLine("    " + m.description);
            }
            if ("api.methods".StartsWith(prefix)) {
                sb.AppendLine("api.methods() -> list");
                sb.AppendLine("    The methods with their parameters");
            }
            if ("batch".StartsWith(prefix)) {
                sb.AppendLine("batch(commands: object[], atomic: bool = true) -> list");
                sb.AppendLine("    Runs several commands as a single undo step; atomic: if one fails, the ones before it are undone");
            }
            return sb.ToString().TrimEnd();
        }
    }
}

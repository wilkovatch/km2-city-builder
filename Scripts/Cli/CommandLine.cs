using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Godot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cli {
    //An invalid command line: the usage is printed and the program quits with code 2
    public class UsageException : System.Exception {
        public UsageException(string message) : base(message) { }
    }

    //The command line after "--", parsed against the command table (see CliCommands)
    public class Invocation {
        public CliCommands.Command command;
        public readonly List<string> positionals = new List<string>();
        //options by name (without the dashes); flags have the value "true" or "false"
        public readonly Dictionary<string, List<string>> options = new Dictionary<string, List<string>>();
        //name=value arguments (call)
        public readonly List<(string name, string value)> pairs = new List<(string, string)>();
        public bool help;
        public string error;

        public bool Has(string option) => options.ContainsKey(option);

        public string Get(string option, string fallback = null) {
            return options.TryGetValue(option, out var values) ? values[values.Count - 1] : fallback;
        }

        public bool Flag(string option) => Get(option) == "true";

        public List<string> All(string option) {
            return options.TryGetValue(option, out var values) ? values : new List<string>();
        }
    }

    //Reads the command line once. A session started with arguments after "--" is a command line session: the last city
    //is not loaded at startup, nothing is asked with popups, and the program quits when the command is done.
    public static class CommandLine {
        static readonly System.Lazy<Invocation> current = new System.Lazy<Invocation>(() => Parse(OS.GetCmdlineUserArgs()));

        //null when the program was started without a command (the editor)
        public static Invocation Current => current.Value;

        public static bool IsCliSession => Current != null;

        public static Invocation Parse(string[] args) {
            if (args.Length == 0) return null;
            var inv = new Invocation();
            try {
                Parse(args, inv);
            } catch (UsageException e) {
                inv.error = e.Message;
            }
            return inv;
        }

        static void Parse(string[] args, Invocation inv) {
            var i = 0;
            if (args[0] == "--help" || args[0] == "-h") {
                inv.command = CliCommands.Find("help");
                i = 1;
            } else if (args[0] == "--version") {
                inv.command = CliCommands.Find("version");
                i = 1;
            } else if (args.Length >= 2 && CliCommands.Find(args[0] + " " + args[1]) != null) {
                inv.command = CliCommands.Find(args[0] + " " + args[1]);
                i = 2;
            } else {
                inv.command = CliCommands.Find(args[0]);
                if (inv.command == null) {
                    var group = CliCommands.Group(args[0]);
                    if (group.Count > 0) throw new UsageException("'" + args[0] + "' needs one of: " + string.Join(", ", group));
                    throw new UsageException("unknown command '" + args[0] + "'");
                }
                i = 1;
            }
            var cmd = inv.command;
            for (; i < args.Length; i++) {
                var arg = args[i];
                if (arg == "--help" || arg == "-h") {
                    inv.help = true;
                } else if (arg.StartsWith("--") && arg.Length > 2) {
                    var eq = arg.IndexOf('=');
                    var name = eq < 0 ? arg.Substring(2) : arg.Substring(2, eq - 2);
                    var value = eq < 0 ? null : arg.Substring(eq + 1);
                    switch (cmd.KindOf(name)) {
                        case CliCommands.OptionKind.Flag:
                            if (value != null && value != "true" && value != "false") throw new UsageException("--" + name + " is true or false");
                            Add(inv, name, value ?? "true");
                            break;
                        case CliCommands.OptionKind.Value:
                            if (value == null) {
                                if (i + 1 >= args.Length) throw new UsageException("--" + name + " needs a value");
                                value = args[++i];
                            }
                            Add(inv, name, value);
                            break;
                        default:
                            throw new UsageException("unknown option --" + name + " of " + cmd.name);
                    }
                } else if (cmd.pairs && inv.positionals.Count >= cmd.positionals.Length && arg.IndexOf('=') > 0) {
                    var eq = arg.IndexOf('=');
                    inv.pairs.Add((arg.Substring(0, eq), arg.Substring(eq + 1)));
                } else {
                    inv.positionals.Add(arg);
                }
            }
            if (inv.help) return;
            var required = cmd.positionals.Length - cmd.optionalPositionals;
            if (inv.positionals.Count < required) {
                throw new UsageException(cmd.name + " needs " + Describe(cmd.positionals, inv.positionals.Count, required));
            }
            if (inv.positionals.Count > cmd.positionals.Length) {
                throw new UsageException("unexpected argument '" + inv.positionals[cmd.positionals.Length] + "' of " + cmd.name);
            }
            cmd.Validate(inv);
        }

        static string Describe(string[] names, int from, int to) {
            var res = new List<string>();
            for (int i = from; i < to; i++) res.Add("<" + names[i] + ">");
            return string.Join(" ", res);
        }

        static void Add(Invocation inv, string name, string value) {
            if (!inv.options.TryGetValue(name, out var values)) inv.options[name] = values = new List<string>();
            values.Add(value);
        }

        // Values

        //the API parameter names as options: terrainSize is --terrain-size
        public static string ToOption(string param) {
            var sb = new System.Text.StringBuilder();
            foreach (var c in param) {
                if (char.IsUpper(c)) sb.Append('-').Append(char.ToLowerInvariant(c));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        //a value given as text for a parameter of the type: the strings are taken as they are, the lists of strings can be
        //separated by commas, the rest is read as JSON (or taken as a string if it isn't valid JSON)
        public static JToken ToValue(string text, System.Type type) {
            if (type == typeof(string)) return new JValue(text);
            if (type == typeof(List<string>) && !text.TrimStart().StartsWith("[")) {
                var list = new JArray();
                foreach (var item in text.Split(',')) {
                    if (item.Trim().Length > 0) list.Add(item.Trim());
                }
                return list;
            }
            return ParseJson(text);
        }

        //JSON, or a string if the text isn't valid JSON
        public static JToken ParseJson(string text) {
            try {
                return JToken.Parse(text);
            } catch (JsonReaderException) {
                return new JValue(text);
            }
        }

        //"@file" is the content of the file, any other text is itself
        public static string ReadArgument(string text) {
            if (!text.StartsWith("@")) return text;
            var file = text.Substring(1);
            try {
                return File.ReadAllText(file);
            } catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException) {
                throw new UsageException("could not read " + file + ": " + e.Message);
            }
        }

        //the parameters of an API method (null for the ones handled by the dispatcher, which take any value)
        public static ParameterInfo[] ParametersOf(string method) {
            return Api.CommandDispatcher.Methods.TryGetValue(method, out var m) ? m.info.GetParameters() : null;
        }

        public static System.Type ValueType(System.Type type) {
            return System.Nullable.GetUnderlyingType(type) ?? type;
        }
    }
}

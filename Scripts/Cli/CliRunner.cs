using System.IO;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;

namespace Cli {
    //A failed API call (or an error with an API error code): printed as "error [code]: message", exit code 1
    public class CliError : System.Exception {
        public readonly string code;

        public CliError(string code, string message) : base(message) {
            this.code = code;
        }
    }

    //What a command works with: the command line, the API and the output
    public class CliContext {
        public Invocation inv;
        public CityBuilderMenuBar builder;
        public CliOutput output;
        public Api.CommandDispatcher dispatcher;
        int nextId = 0;

        //runs an API method, returns its result (throws CliError if it fails)
        public async Task<JToken> Call(string method, JObject parameters = null) {
            var request = new JObject { ["id"] = ++nextId, ["method"] = method, ["params"] = parameters ?? new JObject() };
            var response = await dispatcher.Execute(request, output.Progress);
            if (response["error"] is JObject error) {
                throw new CliError(error["code"]?.Value<string>(), error["message"]?.Value<string>());
            }
            return response["result"];
        }

        //the options shared by the commands changing a city: --no-history, and the city to load first (null for none)
        public async Task Prepare(string city) {
            if (inv.Flag("no-history")) await Call("api.configure", new JObject { ["history"] = false });
            if (city == null) return;
            await Call("city.load", new JObject {
                ["folder"] = Path.GetFullPath(city),
                ["migrate"] = inv.Flag("migrate") ? "always" : "never",
                ["installPythonModules"] = inv.Flag("install-python-modules")
            });
        }

        //--save: the city is saved when the command has succeeded
        public async Task Finish() {
            if (inv.Flag("save")) await Call("city.save");
        }
    }

    //Runs the command of a command line session, then quits with its exit code
    public partial class CliRunner : Node {
        CityBuilderMenuBar builder;

        public static void Start(CityBuilderMenuBar builder) {
            if (!CommandLine.IsCliSession) return;
            if (CommandLine.Current.command?.needsWindow == true) HideWindow();
            builder.AddChild(new CliRunner { builder = builder, Name = "CliRunner" });
        }

        //the commands rendering images need a window (there is no renderer with --headless), which is kept out of
        //the way: small, borderless, without focus and outside of the screens (a minimized window isn't drawn)
        static void HideWindow() {
            if (DisplayServer.GetName() == "headless") return;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            DisplayServer.WindowSetSize(new Vector2I(320, 240));
            var origin = Vector2I.Zero;
            for (int i = 0; i < DisplayServer.GetScreenCount(); i++) origin = new Vector2I(System.Math.Min(origin.X, DisplayServer.ScreenGetPosition(i).X), origin.Y);
            DisplayServer.WindowSetPosition(new Vector2I(origin.X - 20000, 0));
        }

        public override void _Ready() {
            Run();
        }

        async void Run() {
            var inv = CommandLine.Current;
            if (inv.error != null) {
                CliOutput.PrintError(null, inv.error);
                CliOutput.Error.WriteLine(inv.command != null ? CliCommands.Usage(inv.command) + "\n(help " + inv.command.name + " shows the options)" : "(help lists the commands)");
                GetTree().Quit(2);
                return;
            }
            //the program finishes starting
            for (int i = 0; i < 10; i++) await ToSignal(GetTree(), "process_frame");
            int code;
            CliOutput output = null;
            try {
                output = new CliOutput(inv.Get("output"), inv.Flag("pretty"), inv.Flag("progress"));
                var ctx = new CliContext { inv = inv, builder = builder, output = output, dispatcher = Api.ApiHost.Instance.Dispatcher };
                if (inv.help) {
                    output.Line(CliCommands.Help(inv.command));
                    code = 0;
                } else {
                    code = await inv.command.run(ctx);
                }
            } catch (UsageException e) {
                CliOutput.PrintError(null, e.Message);
                code = 2;
            } catch (CliError e) {
                CliOutput.PrintError(e.code, e.Message);
                code = 1;
            } catch (System.Exception e) {
                CliOutput.PrintError("internal_error", e.ToString());
                code = 1;
            } finally {
                output?.Dispose();
            }
            if (inv.command.quitsItself && code == 0 && !inv.help) return;
            GetTree().Quit(code);
        }
    }
}

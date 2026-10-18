using Godot;

namespace Api {
    //Owns the API of the program and runs its queued commands every frame. The transports (command line, scripts)
    //send their commands with Dispatcher.Enqueue.
    public partial class ApiHost : Node {
        public static ApiHost Instance { get; private set; }

        public CityApi Api { get; private set; }
        public CommandDispatcher Dispatcher { get; private set; }

        //a script session started from the command line drives the program: the last city is not loaded at startup and
        //nothing is asked with popups
        public static bool IsScriptSession() {
            foreach (var arg in OS.GetCmdlineUserArgs()) {
                if (arg.StartsWith("--api-")) return true;
            }
            return false;
        }

        public static ApiHost Create(CityBuilderMenuBar builder) {
            var host = new ApiHost { Name = "ApiHost" };
            host.Api = new CityApi(builder);
            host.Dispatcher = new CommandDispatcher(host.Api);
            builder.AddChild(host);
            Instance = host;
            return host;
        }

        public override void _Process(double delta) {
            Dispatcher.Pump();
        }
    }
}

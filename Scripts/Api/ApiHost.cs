using System.Collections.Concurrent;
using System.Threading.Tasks;
using Godot;

namespace Api {
    //Owns the API of the program and runs its queued commands every frame. The transports (command line, scripts)
    //send their commands with Dispatcher.Enqueue.
    public partial class ApiHost : Node {
        public static ApiHost Instance { get; private set; }

        public CityApi Api { get; private set; }
        public CommandDispatcher Dispatcher { get; private set; }

        //work sent from other threads that must run on the main thread outside of the dispatcher (e.g. a script, whose
        //own commands go through the dispatcher)
        readonly ConcurrentQueue<System.Action> mainThreadQueue = new ConcurrentQueue<System.Action>();

        public static ApiHost Create(CityBuilderMenuBar builder) {
            var host = new ApiHost { Name = "ApiHost" };
            host.Api = new CityApi(builder);
            host.Dispatcher = new CommandDispatcher(host.Api);
            builder.AddChild(host);
            Instance = host;
            return host;
        }

        //runs the function on the main thread (from any thread), the task ends with its result
        public Task<T> RunOnMainThread<T>(System.Func<Task<T>> function) {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            mainThreadQueue.Enqueue(async () => {
                try {
                    tcs.TrySetResult(await function());
                } catch (System.Exception e) {
                    tcs.TrySetException(e);
                }
            });
            return tcs.Task;
        }

        public override void _Process(double delta) {
            while (mainThreadQueue.TryDequeue(out var action)) action();
            Dispatcher.Pump();
        }
    }
}

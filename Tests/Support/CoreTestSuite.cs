using Godot;

namespace CoreSelfTests {
    //Compiled into the developer test assembly, never into the builder.
    public abstract class CoreTestSuite {
        protected CoreTestContext context;
        protected int failures { get => context.failures; set => context.failures = value; }
        protected void Log(string line) => context.Log(line);
        protected SceneTree GetTree() => context.builder.GetTree();
        protected Viewport GetViewport() => context.builder.GetViewport();
        protected Window GetWindow() => context.builder.GetWindow();
        protected void AddChild(Node child) => context.builder.AddChild(child);
        protected SignalAwaiter ToSignal(GodotObject source, StringName signal) => context.builder.ToSignal(source, signal);
    }
}

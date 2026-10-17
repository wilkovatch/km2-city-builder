#if TOOLS
using Godot;

[Tool]
public partial class PostprocessBuildPlugin : EditorPlugin {
    PostprocessBuild exportPlugin;

    public override void _EnterTree() {
        exportPlugin = new PostprocessBuild();
        AddExportPlugin(exportPlugin);
    }

    public override void _ExitTree() {
        RemoveExportPlugin(exportPlugin);
        exportPlugin = null;
    }
}
#endif

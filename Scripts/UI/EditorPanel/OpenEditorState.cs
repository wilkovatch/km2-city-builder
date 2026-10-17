using System.Collections.Generic;
using Godot;

//The editor panels open before an undo/redo, from the main panel to the shown one, reopened after it as far as what
//they edit still exists (see CityBuilderMenuBar.CaptureEditorState and EditorPanel.CaptureReopen)
public class OpenEditorState {
    //each panel is reopened from its parent as reopened (null for the main panel), returning it, or null if it's gone
    public readonly List<(System.Func<EditorPanel, EditorPanel> reopen, int page)> steps = new List<(System.Func<EditorPanel, EditorPanel>, int)>();
    //the control point selected in the editor of its element
    public System.Func<Node3D> selectedPoint = null;
    public RuntimeGizmos.TransformType transformType;
}

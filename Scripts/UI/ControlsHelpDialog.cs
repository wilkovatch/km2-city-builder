using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using SM = StringManager;

//Read-only reference, rebuilt on opening so it follows the current mappings and camera settings.
public static class ControlsHelpDialog {
    public static void Show(Control parent, RuntimeGizmos.TransformGizmo gizmo) {
        if (parent.GetNodeOrNull<AcceptDialog>("ControlsHelp") != null) return;
        var dialog = new AcceptDialog {
            Name = "ControlsHelp", Title = SM.Get("HELP_CONTROLS"),
            Exclusive = true, Transient = true, Unresizable = false,
            MinSize = new Vector2I(520, 300), OkButtonText = SM.Get("CLOSE")
        };
        dialog.AddThemeStyleboxOverride("panel", UILayout.Box(new Color(0.9f, 0.9f, 0.9f), 12, 12, 12, 12));
        dialog.VisibilityChanged += () => { if (!dialog.Visible) dialog.QueueFree(); };
        var content = new VBoxContainer();
        dialog.AddChild(content);
        var tabs = new TabContainer { Name = "ControlsTabs", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        tabs.AddThemeStyleboxOverride("panel", UILayout.Box(new Color(0.96f, 0.96f, 0.96f)));
        tabs.AddThemeStyleboxOverride("tab_selected", UILayout.Box(Colors.White, 12, 12, 6, 6));
        tabs.AddThemeStyleboxOverride("tab_unselected", UILayout.Box(new Color(0.84f, 0.84f, 0.84f), 12, 12, 6, 6));
        tabs.AddThemeStyleboxOverride("tab_hovered", UILayout.Box(new Color(0.92f, 0.92f, 0.92f), 12, 12, 6, 6));
        foreach (var state in new[] { "selected", "unselected", "hovered" }) tabs.AddThemeColorOverride("font_" + state + "_color", UILayout.TextColor);
        content.AddChild(tabs);
        dialog.GetOkButton().Hide();
        var close = new Button {
            Name = "CloseControls", Text = SM.Get("CLOSE"),
            CustomMinimumSize = new Vector2(160, 30), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        UILayout.StyleButton(close);
        close.Pressed += () => dialog.Hide();
        content.AddChild(close);

        var camera = AddTab(tabs, "PREF_CAMERA_CONTROLS");
        var schemeB = SettingsManager.Get("cameraControls", 0) == 1;
        Note(camera, SM.Get("HELP_CONTROLS_ACTIVE_SCHEME").Replace("$SCHEME", SM.Get(schemeB ? "PREF_CAMERA_CONTROLS_B" : "PREF_CAMERA_CONTROLS_A")));
        var middle = Binding(KeyCommand.ZoomInAndPan2);
        var shift = Binding(KeyCommand.Modifier1);
        var ctrl = Binding(KeyCommand.Modifier2);
        var wheel = Alternatives(Binding(KeyCommand.ZoomIn2), Binding(KeyCommand.ZoomOut2));
        if (schemeB) {
            Row(camera, Drag(middle), "HELP_CONTROLS_ORBIT");
            Row(camera, Alternatives(Drag(Chord(shift, middle)), MouseMove(Binding(KeyCommand.ZoomInAndPan))), "HELP_CONTROLS_PAN");
            Row(camera, Chord(ctrl, wheel), "HELP_CONTROLS_PAN_HORIZONTAL");
            Row(camera, Chord(shift, wheel), "HELP_CONTROLS_PAN_VERTICAL");
            Row(camera, Drag(Chord(ctrl, middle)), "HELP_CONTROLS_ZOOM");
        } else {
            Row(camera, Drag(Binding(KeyCommand.RotateCamera)), "HELP_CONTROLS_ROTATE_VIEW");
            Row(camera, Alternatives(Drag(middle), MouseMove(Binding(KeyCommand.ZoomInAndPan))), "HELP_CONTROLS_PAN");
        }
        Row(camera, wheel, "HELP_CONTROLS_ZOOM");
        Row(camera, Alternatives(Binding(KeyCommand.ZoomIn), Binding(KeyCommand.ZoomOut)), "HELP_CONTROLS_ZOOM");
        Row(camera, SM.Get("HELP_CONTROLS_CLICK").Replace("$INPUT", middle), "HELP_CONTROLS_ZOOM_POINT");
        Row(camera, Binding(KeyCommand.ZoomToSelection), "HELP_CONTROLS_ZOOM_SELECTION");
        if (Input.emulateMiddleButton) {
            Row(camera, Chord(KeyLabel(Key.Alt), Binding(KeyCommand.Select)), "HELP_CONTROLS_EMULATE_MIDDLE");
        }
        Note(camera, SM.Get("HELP_CONTROLS_CAMERA_SETTINGS"));

        var editing = AddTab(tabs, "HELP_CONTROLS_EDITING");
        Row(editing, Binding(KeyCommand.Select), "HELP_CONTROLS_SELECT");
        Row(editing, Chord(ctrl, Binding(KeyCommand.Select)), "HELP_CONTROLS_CLICK_THROUGH");
        Row(editing, Binding(KeyCommand.Close), "HELP_CONTROLS_DESELECT");
        Row(editing, Binding(KeyCommand.Delete), "HELP_CONTROLS_DELETE");
        Row(editing, Binding(KeyCommand.Edit), "HELP_CONTROLS_TRANSFORM");
        Row(editing, MouseMove(shift), "HELP_CONTROLS_PLACEMENT_HEIGHT");
        Row(editing, SM.Get("HELP_CONTROLS_TAP").Replace("$INPUT", shift), "HELP_CONTROLS_RESET_HEIGHT");
        Note(editing, SM.Get("HELP_CONTROLS_GIZMO_CONTEXT"));
        if (gizmo != null) {
            GizmoRow(editing, gizmo.SetMoveType, "TRANSF_P_MOVE");
            GizmoRow(editing, gizmo.SetRotateType, "TRANSF_P_ROTATE");
            GizmoRow(editing, gizmo.SetScaleType, "TRANSF_P_SCALE");
            GizmoRow(editing, gizmo.SetSpaceToggle, "HELP_CONTROLS_TRANSFORM_SPACE");
        }
        //Numeric fields use physical middle/Shift/Ctrl directly, independently of the world mappings.
        Row(editing, Drag(MouseLabel(MouseButton.Middle)), "HELP_CONTROLS_NUMERIC_DRAG");
        Row(editing, KeyLabel(Key.Ctrl), "HELP_CONTROLS_NUMERIC_FINE");
        Row(editing, KeyLabel(Key.Shift), "HELP_CONTROLS_NUMERIC_SNAP");

        var shortcuts = AddTab(tabs, "HELP_CONTROLS_SHORTCUTS");
        Row(shortcuts, Binding(KeyCommand.SaveCity), "SAVE_CITY");
        Row(shortcuts, Binding(KeyCommand.Undo), "UNDO");
        Row(shortcuts, Binding(KeyCommand.Redo), "REDO");
        Row(shortcuts, Binding(KeyCommand.CreateRoad), "CREATE_ROAD");
        Row(shortcuts, Binding(KeyCommand.CreateTerrainPatch), "CREATE_TERRAIN_PATCH");
        Row(shortcuts, Binding(KeyCommand.CreateBuildingLine), "CREATE_BUILDING_LINE");
        Row(shortcuts, Binding(KeyCommand.ToggleSnapping), "SNAP_TO_GRID");

        parent.AddChild(dialog);
        var available = (Vector2I)parent.GetViewportRect().Size - new Vector2I(40, 60);
        dialog.PopupCentered(new Vector2I(Mathf.Min(780, available.X), Mathf.Min(560, available.Y)));
        UIDetection.NotifyPopupOpen();
    }

    static VBoxContainer AddTab(TabContainer tabs, string titleKey) {
        var scroll = new ScrollContainer {
            Name = titleKey, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        tabs.AddChild(scroll);
        tabs.SetTabTitle(tabs.GetTabCount() - 1, SM.Get(titleKey));
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 12);
        scroll.AddChild(margin);
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 8);
        margin.AddChild(rows);
        return rows;
    }

    static void Note(VBoxContainer rows, string text) {
        var label = UILayout.MakeLabel(text, HorizontalAlignment.Left);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rows.AddChild(label);
    }

    static void Row(VBoxContainer rows, string binding, string actionKey) {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var keycap = new PanelContainer { CustomMinimumSize = new Vector2(230, 32), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        keycap.AddThemeStyleboxOverride("panel", UILayout.Box(Colors.White, 8, 8, 6, 6));
        var keys = UILayout.MakeLabel(binding);
        keys.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        keycap.AddChild(keys);
        row.AddChild(keycap);
        //Menu strings also contain default shortcuts, which must not be repeated in this reference.
        var action = UILayout.MakeLabel(SM.Get(actionKey).Split('\t')[0].TrimEnd(), HorizontalAlignment.Left);
        action.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        action.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(action);
        rows.AddChild(row);
    }

    static void GizmoRow(VBoxContainer rows, Key key, string actionKey) {
        if (key != Key.None) Row(rows, KeyLabel(key), actionKey);
    }

    static string Binding(KeyCommand command) {
        if (Input.mouseMapping.TryGetValue(command, out var mouse)) return MouseLabel(mouse);
        if (Input.keyMapping.TryGetValue(command, out var keys) && keys.key != Key.None) {
            return keys.modifier == Key.None ? KeyLabel(keys.key) : Chord(KeyLabel(keys.modifier), KeyLabel(keys.key));
        }
        return SM.Get("HELP_CONTROLS_UNASSIGNED");
    }

    static string MouseLabel(MouseButton button) => SM.GetOrKey("HELP_CONTROLS_MOUSE_" + button);
    static string KeyLabel(Key key) => OS.GetKeycodeString(key);
    static string Chord(string modifier, string input) => modifier + " + " + input;
    static string Alternatives(string first, string second) => first + " / " + second;
    static string Drag(string input) => SM.Get("HELP_CONTROLS_DRAG").Replace("$INPUT", input);
    static string MouseMove(string input) => SM.Get("HELP_CONTROLS_MOUSE_MOVE").Replace("$INPUT", input);
}

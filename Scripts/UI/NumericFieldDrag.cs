using Godot;

/// <summary>
/// Middle mouse drag on the numeric input fields: moving the mouse right/up increases the value, left/down decreases it.
/// - The step is proportional to the current value (Ctrl: x0.1), Shift snaps the value to the nearest integer (as with the gizmo).
/// - The value is applied at most once per frame, and all the changes of a drag are a single undo step
///   (the panels record them with <see cref="CurrentKey"/> as coalesce key).
/// - Escape or another mouse button during the drag restores the value the field had when it started.
/// </summary>
public static class NumericFieldDrag {
    class Drag {
        public readonly object key = new object();
        public LineEdit field;
        public bool integer;
        public System.Action apply;
        public string startText;
        public double startValue;
        public double value; //not snapped, so that the snapped value moves on from one integer to the next
        public bool snap;
        public Vector2 startMousePos;
        public bool pending;
        public bool applied;
    }

    static Drag current;
    static ulong endFrame = ulong.MaxValue;

    /// <summary>The coalesce key of the drag in progress (a new one for each drag), null if there is none</summary>
    public static object CurrentKey => current?.key;

    public static bool IsDragging => current != null;

    /// <summary>
    /// A drag is in progress or ended in this frame (by the release, Escape or another button, whose input
    /// actions are still "just pressed"): the editor shortcuts and the camera must ignore the input
    /// </summary>
    public static bool BlocksInput => current != null || endFrame == Engine.GetProcessFrames();

    /// <summary>
    /// Enables the drag on the field, apply is invoked after the field text is changed by the drag
    /// </summary>
    public static void Attach(LineEdit field, bool integer, System.Action apply) {
        field.GuiInput += @event => OnGuiInput(field, integer, apply, @event);
        field.TreeExiting += () => {
            if (current?.field == field) Stop();
        };
    }

    // gui_input is emitted before the LineEdit handles the event, and the press makes the field the mouse focus
    // of the viewport, so the motion and the release come here even outside the field
    static void OnGuiInput(LineEdit field, bool integer, System.Action apply, InputEvent @event) {
        if (@event is InputEventMouseButton button) {
            if (button.ButtonIndex == MouseButton.Middle) {
                if (button.Pressed && current == null && field.Editable) Begin(field, integer, apply);
                else if (!button.Pressed && current?.field == field) End(false);
                else return;
                field.AcceptEvent();
            } else if (button.Pressed && current?.field == field) {
                End(true);
                field.AcceptEvent();
            }
        } else if (@event is InputEventMouseMotion motion && current?.field == field) {
            Move(motion);
            field.AcceptEvent();
        }
    }

    /// <summary>
    /// Called by TextInputFocus before the event reaches the GUI and the editor shortcuts,
    /// returns true if the event was consumed by the drag in progress
    /// </summary>
    public static bool HandleInput(InputEvent @event) {
        if (current == null || !@event.IsActionPressed("ui_cancel")) return false;
        End(true);
        return true;
    }

    static void Begin(LineEdit field, bool integer, System.Action apply) {
        if (field.IsEditing()) field.Unedit(); //commits the text being typed (its own undo step, before the drag)
        if (!double.TryParse(field.Text, out var value)) value = 0;
        current = new Drag {
            field = field, integer = integer, apply = apply,
            startText = field.Text, startValue = value, value = value,
            startMousePos = field.GetViewport().GetMousePosition()
        };
        Input.MouseMode = Input.MouseModeEnum.Captured; //no limit from the screen edges
        Tree.ProcessFrame += Flush;
    }

    static void Move(InputEventMouseMotion motion) {
        var delta = motion.Relative.X - motion.Relative.Y;
        if (delta == 0) return;
        var magnitude = System.Math.Abs(current.value);
        var step = current.integer ? System.Math.Max(0.01 * magnitude, 0.2) : 0.01 * System.Math.Max(magnitude, 1);
        if (motion.CtrlPressed) step *= 0.1;
        current.value += delta * step;
        current.snap = motion.ShiftPressed;
        current.pending = true;
    }

    static void Flush() {
        if (current == null || !current.pending) return;
        current.pending = false;
        current.applied = true;
        current.field.Text = Format(current.value, current.integer || current.snap);
        current.apply();
    }

    static void End(bool cancel) {
        var drag = current;
        if (cancel) {
            if (drag.applied) {
                //the restored value is merged into the step of the drag, which disappears
                drag.field.Text = double.TryParse(drag.startText, out _) ? drag.startText : Format(drag.startValue, drag.integer);
                drag.apply();
            } else {
                drag.field.Text = drag.startText;
            }
        } else {
            Flush();
        }
        Stop();
        Input.WarpMouse(drag.startMousePos);
    }

    static void Stop() {
        var drag = current;
        current = null;
        endFrame = Engine.GetProcessFrames();
        Tree.ProcessFrame -= Flush;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    static string Format(double value, bool integer) {
        var text = integer ? ((long)System.Math.Round(value)).ToString() : ((float)value).ToString("0.#####");
        return text == "-0" ? "0" : text; //small negative values rounded to zero
    }

    static SceneTree Tree => (SceneTree)Engine.GetMainLoop();
}

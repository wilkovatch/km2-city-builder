using Godot;

/// <summary>
/// Input field focus behavior for Godot text fields (registered as an autoload):
/// - The selected input field is deselected on any mouse press outside it, Godot's
///   LineEdit/TextEdit keep the focus (and keep blocking the editor shortcuts) unless another
///   focusable control takes it.
/// - The end of edit fires on submit, cancel and focus loss, Godot's TextSubmitted only on submit.
/// </summary>
public partial class TextInputFocus : Node {
    public override void _Input(InputEvent @event) {
        if (@event is not InputEventMouseButton { Pressed: true } mouseButton) return;
        if (mouseButton.ButtonIndex != MouseButton.Left && mouseButton.ButtonIndex != MouseButton.Right
            && mouseButton.ButtonIndex != MouseButton.Middle) return;
        var focused = GetViewport().GuiGetFocusOwner();
        if (focused is not (LineEdit or TextEdit)) return;
        var localPos = focused.GetGlobalTransformWithCanvas().AffineInverse() * mouseButton.Position;
        if (!new Rect2(Vector2.Zero, focused.Size).HasPoint(localPos)) focused.ReleaseFocus();
    }

    /// <summary>
    /// Content type filter for the numeric types: characters that would not be
    /// accepted (anything but digits, a leading minus and, for decimals, one decimal point) are discarded.
    /// </summary>
    public static void SetContentType(LineEdit lineEdit, GameEngine.InputContentType contentType) {
        lineEdit.VirtualKeyboardType = contentType switch {
            GameEngine.InputContentType.Decimal => LineEdit.VirtualKeyboardTypeEnum.NumberDecimal,
            GameEngine.InputContentType.Integer => LineEdit.VirtualKeyboardTypeEnum.Number,
            _ => LineEdit.VirtualKeyboardTypeEnum.Default
        };
        if (contentType == GameEngine.InputContentType.Standard) return;
        bool allowDecimal = contentType == GameEngine.InputContentType.Decimal;
        lineEdit.TextChanged += text => {
            var res = new System.Text.StringBuilder(text.Length);
            int caret = lineEdit.CaretColumn;
            int newCaret = caret;
            bool hasSeparator = false;
            for (int i = 0; i < text.Length; i++) {
                var c = text[i];
                bool keep = (c >= '0' && c <= '9')
                    || (c == '-' && res.Length == 0)
                    || (allowDecimal && c == '.' && !hasSeparator);
                if (keep) {
                    if (c == '.') hasSeparator = true;
                    res.Append(c);
                } else if (i < caret) {
                    newCaret--;
                }
            }
            var filtered = res.ToString();
            if (filtered == text) return;
            lineEdit.Text = filtered; //setting the text does not emit text_changed again
            lineEdit.CaretColumn = Mathf.Clamp(newCaret, 0, filtered.Length);
        };
    }

    /// <summary>
    /// End of edit: invokes the action when the field leaves edit mode,
    /// whether by submitting, cancelling (which restores the text the field had when editing started)
    /// or losing the focus. The action is skipped if the text did not change.
    /// </summary>
    public static void OnEndEdit(LineEdit lineEdit, System.Action<string> action) {
        string originalText = null;
        bool canceled = false;
        // gui_input is emitted before the LineEdit handles the event (and leaves edit mode on ui_cancel)
        lineEdit.GuiInput += @event => {
            if (lineEdit.IsEditing() && @event.IsActionPressed("ui_cancel")) canceled = true;
        };
        lineEdit.EditingToggled += editing => {
            if (editing) {
                originalText = lineEdit.Text;
                canceled = false;
                return;
            }
            if (canceled && originalText != null) lineEdit.Text = originalText;
            canceled = false;
            if (lineEdit.Text != originalText) action(lineEdit.Text);
        };
    }
}

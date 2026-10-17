using Godot;

/// <summary>
/// Input field popup: a 400x135 light grey panel centred on screen with a bold
/// 20px title, a centred input field (inset 40px left/right, 45px from the top, 30px tall) and
/// 160x30 buttons 10px above the bottom edge.
/// </summary>
public class InputFieldPopup {
    private readonly Panel root;
    private readonly LineEdit input;
    private bool closed;

    private InputFieldPopup(Control parent, string title, string placeholder, string initialText) {
        root = Alert.CreatePopupPanel(parent, "InputFieldPopup", title, 400, 135);
        input = new LineEdit { Name = "InputField", PlaceholderText = placeholder ?? "", Text = initialText ?? "" };
        UILayout.SetRect(input, 0, 0, 1, 1, 0, 7.5f, -80, -105);
        UILayout.StyleLineEdit(input, HorizontalAlignment.Center);
        root.AddChild(input);
        input.CallDeferred(Control.MethodName.GrabFocus);
    }

    public InputFieldPopup(Control parent, string title, string placeholder, string buttonText,
        System.Action<string> genericAction = null, string initialText = null)
        : this(parent, title, placeholder, initialText) {
        System.Action confirm = () => { genericAction?.Invoke(input.Text); Close(); };
        Alert.AddButton(root, "Button", buttonText, 0, confirm);
        input.TextSubmitted += _ => confirm();
    }

    public InputFieldPopup(Control parent, string title, string placeholder, string okText, string cancelText,
        System.Action<string> okAction = null, System.Action cancelAction = null, string initialText = null)
        : this(parent, title, placeholder, initialText) {
        System.Action confirm = () => { okAction?.Invoke(input.Text); Close(); };
        Alert.AddButton(root, "OK", okText, -90, confirm);
        Alert.AddButton(root, "Cancel", cancelText, 90, () => { cancelAction?.Invoke(); Close(); });
        input.TextSubmitted += _ => confirm();
    }

    private void Close() {
        if (closed) return;
        closed = true;
        if (GodotObject.IsInstanceValid(root)) root.QueueFree();
    }

    public bool IsClosed() => closed || !GodotObject.IsInstanceValid(root);
}

using Godot;

/// <summary>
/// Alert popup: a light grey panel (matching the editor panels) centred on screen with a bold
/// 20px title at the top, a 20px message and 160x30 buttons 10px above the bottom edge
/// (a single centred button, or OK / Cancel at -90 / +90).
/// </summary>
public class Alert {
    private readonly Panel root;
    private bool closed;

    public Alert(Control parent, string title, string message, string buttonText, System.Action genericAction = null,
        float height = 180, bool leftAlign = false, float width = 400) {
        root = Create(parent, title, message, width, height, leftAlign);
        AddButton(root, "Button", buttonText, 0, () => { genericAction?.Invoke(); Close(); });
    }

    public Alert(Control parent, string title, string message, string okText, string cancelText,
        System.Action okAction = null, System.Action cancelAction = null, float height = 180, bool leftAlign = false, float width = 400) {
        root = Create(parent, title, message, width, height, leftAlign);
        AddButton(root, "OK", okText, -90, () => { okAction?.Invoke(); Close(); });
        AddButton(root, "Cancel", cancelText, 90, () => { cancelAction?.Invoke(); Close(); });
    }

    internal static Panel CreatePopupPanel(Control parent, string name, string title, float width, float height) {
        var panel = new Panel { Name = name };
        panel.AddThemeStyleboxOverride("panel", UILayout.Box(new Color(0.9f, 0.9f, 0.9f)));
        UILayout.SetRect(panel, 0.5f, 0.5f, 0.5f, 0.5f, 0, 0, width, height);

        var titleLabel = UILayout.MakeLabel(title, HorizontalAlignment.Center, VerticalAlignment.Top, 20);
        titleLabel.Name = "Title";
        titleLabel.AddThemeFontOverride("font", UILayout.BoldFont());
        titleLabel.ClipText = true;
        UILayout.SetRect(titleLabel, 0, 0, 1, 1, 0, -2.5f, -20, -5);
        panel.AddChild(titleLabel);

        parent.AddChild(panel);
        return panel;
    }

    internal static Button AddButton(Control panel, string name, string text, float x, System.Action action) {
        var button = new Button { Name = name, Text = text ?? "" };
        UILayout.SetRect(button, 0.5f, 0, 0.5f, 0, x, 25, 160, 30);
        UILayout.StyleButton(button);
        panel.AddChild(button);
        TextFit.Apply(button);
        button.Pressed += () => action();
        return button;
    }

    private static Panel Create(Control parent, string title, string message, float width, float height, bool leftAlign) {
        var panel = CreatePopupPanel(parent, "Alert", title, width, height);
        var messageLabel = UILayout.MakeLabel(message, leftAlign ? HorizontalAlignment.Left : HorizontalAlignment.Center, VerticalAlignment.Top, 20);
        messageLabel.Name = "Message";
        UILayout.SetRect(messageLabel, 0, 0, 1, 1, 0, 0, -20, -90);
        panel.AddChild(messageLabel);
        TextFit.Apply(messageLabel, 20, 9);
        return panel;
    }

    private void Close() {
        closed = true;
        if (GodotObject.IsInstanceValid(root)) root.QueueFree();
    }

    public bool IsClosed() => closed || !GodotObject.IsInstanceValid(root);
}

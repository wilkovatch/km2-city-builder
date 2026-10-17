using Godot;

/// <summary>
/// Loading panel: a 400x70 white panel centred on screen with a 20px
/// message at the top and a 300x20 bar (translucent white background, green fill, centred
/// percentage) 10px below the panel centre.
/// </summary>
public class ProgressBar {
    private readonly Control root;
    private readonly Label messageLabel;
    private readonly Label percentLabel;
    private readonly ColorRect fill;

    public ProgressBar(Control parent, string message) {
        root = new Panel { Name = "LoadingPanel", Visible = false };
        root.AddThemeStyleboxOverride("panel", UILayout.Box(Colors.White));
        UILayout.SetRect(root, 0.5f, 0.5f, 0.5f, 0.5f, 0, 0, 400, 70);

        messageLabel = UILayout.MakeLabel(message, HorizontalAlignment.Center, VerticalAlignment.Top, 20);
        messageLabel.Name = "Message";
        messageLabel.ClipText = true;
        UILayout.SetRect(messageLabel, 0, 0, 1, 1, 0, -2.5f, 0, -5);
        root.AddChild(messageLabel);

        var background = new ColorRect { Name = "BackgroundBar", Color = new Color(1, 1, 1, 0.5019608f) };
        UILayout.SetRect(background, 0.5f, 0.5f, 0.5f, 0.5f, 0, -10, 300, 20);
        root.AddChild(background);

        fill = new ColorRect { Name = "GreenBar", Color = new Color(0, 1, 0) };
        UILayout.Stretch(fill);
        fill.SetAnchor(Side.Right, 0, true, false); // keep offsets at 0 so the width follows the anchor
        background.AddChild(fill);

        percentLabel = UILayout.MakeLabel("0%");
        percentLabel.Name = "Text";
        UILayout.Stretch(percentLabel);
        background.AddChild(percentLabel);

        parent.AddChild(root);
    }

    public void SetText(string message) {
        messageLabel.Text = message ?? "";
    }

    public void SetProgress(float percent) {
        // Takes a 0..1 fraction; values above 1 are treated as a percentage for compatibility.
        var fraction = Mathf.Clamp(percent <= 1f ? percent : percent / 100f, 0f, 1f);
        fill.SetAnchor(Side.Right, fraction, true, false);
        percentLabel.Text = $"{Mathf.RoundToInt(fraction * 100)}%";
    }

    public void SetActive(bool active) => root.Visible = active;
}

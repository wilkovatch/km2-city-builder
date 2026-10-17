using Godot;

/// <summary>
/// Applies the wrapping and best-fit behavior used by the UI text
/// components to fixed-size Godot labels and buttons.
/// </summary>
public static class TextFit {
    public const int DefaultFontSize = 14;
    public const int MinimumFontSize = 9;

    public static void Apply(Label label, int maximumFontSize = DefaultFontSize, int minimumFontSize = MinimumFontSize) {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.ClipText = true;
        Attach(label, maximumFontSize, minimumFontSize);
    }

    public static void Apply(Button button, int maximumFontSize = DefaultFontSize, int minimumFontSize = MinimumFontSize) {
        button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        button.ClipText = true;
        Attach(button, maximumFontSize, minimumFontSize);
    }

    public static void Refit(Control control) {
        if (!GodotObject.IsInstanceValid(control) || !control.IsInsideTree() || control.Size.X <= 0 || control.Size.Y <= 0) return;

        var text = control switch {
            Label label => label.Text,
            Button button => button.Text,
            _ => null
        };
        if (string.IsNullOrEmpty(text)) return;

        var font = control.GetThemeFont("font");
        if (font == null) return;

        var horizontalInset = control is Button buttonControl
            ? GetHorizontalInset(buttonControl)
            : 2f;
        // Labels have no padding of their own: like best fit, the text may
        // use the full rect height. Buttons lose their stylebox content margins.
        var verticalInset = control is Button verticalButton ? GetVerticalInset(verticalButton) : 0f;
        var availableWidth = Mathf.Max(1f, control.Size.X - horizontalInset);
        var availableHeight = Mathf.Max(1f, control.Size.Y - verticalInset);
        var maximumFontSize = (int)control.GetMeta("text_fit_max_size", DefaultFontSize);
        var minimumFontSize = (int)control.GetMeta("text_fit_min_size", MinimumFontSize);

        // With best fit, a size only fits when the wrapped text fits vertically AND no single
        // word is wider than the rect. Without the width check, a size was accepted whose rendering then
        // split an over-long word ("Inter" / "s.") and overflowed the rect vertically.
        var words = text.Split(new[] { ' ', '\n', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (var size = maximumFontSize; size >= minimumFontSize; size--) {
            var measured = font.GetMultilineStringSize(text, HorizontalAlignment.Left, availableWidth, size);
            if (measured.Y <= availableHeight + 0.5f && LongestWordWidth(font, words, size) <= availableWidth) {
                control.AddThemeFontSizeOverride("font_size", size);
                return;
            }
        }

        control.AddThemeFontSizeOverride("font_size", minimumFontSize);
    }

    private static float LongestWordWidth(Font font, string[] words, int size) {
        var longest = 0f;
        foreach (var word in words) {
            longest = Mathf.Max(longest, font.GetStringSize(word, HorizontalAlignment.Left, -1, size).X);
        }
        return longest;
    }

    private static void Attach(Control control, int maximumFontSize, int minimumFontSize) {
        control.SetMeta("text_fit_max_size", maximumFontSize);
        control.SetMeta("text_fit_min_size", minimumFontSize);
        control.AddThemeFontSizeOverride("font_size", maximumFontSize);
        // Line spacing 1 adds no gap between lines; Godot's default adds 3px, which the measurement
        // above does not account for.
        control.AddThemeConstantOverride("line_spacing", 0);
        control.Resized += () => Refit(control);
        control.TreeEntered += () => Refit(control);
        Refit(control);
    }

    private static float GetHorizontalInset(Button button) {
        // CheckBox: 20px box + 1px separation, (title inset 21px).
        var inset = button is CheckBox ? 21f : 4f;
        if (button.HasThemeStylebox("normal")) {
            var style = button.GetThemeStylebox("normal");
            // GetMargin, not GetContentMargin: an unset content margin reads -1 (e.g. the checkbox
            // StyleBoxEmpty), which counted 2px more room than Godot wraps the text in.
            inset += style.GetMargin(Side.Left) + style.GetMargin(Side.Right);
        }
        return inset;
    }

    private static float GetVerticalInset(Button button) {
        if (!button.HasThemeStylebox("normal")) return 0f;
        var style = button.GetThemeStylebox("normal");
        return style.GetMargin(Side.Top) + style.GetMargin(Side.Bottom);
    }
}

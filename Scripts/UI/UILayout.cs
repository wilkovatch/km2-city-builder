using Godot;

/// <summary>
/// Helpers for the layout and default look of the program's UI.
///
/// SetRect takes rect values (anchorMin, anchorMax, anchoredPosition, sizeDelta,
/// pivot, all y-up) and converts them to Godot anchors/offsets (y-down).
/// </summary>
public static class UILayout {
    // Default text colour (50, 50, 50).
    public static readonly Color TextColor = new Color(50 / 255f, 50 / 255f, 50 / 255f);
    public static readonly Color PlaceholderColor = new Color(50 / 255f, 50 / 255f, 50 / 255f, 0.5f);
    public static readonly Color BorderColor = new Color(0.62f, 0.62f, 0.62f);
    // The fill of buttons, toggles and dropdowns is not pure white: it is (245, 245, 245),
    // which the colour block then tints. InputFieldBackground is pure white.
    public static readonly Color SpriteFill = new Color(245 / 255f, 245 / 255f, 245 / 255f);

    // The UI uses an Arial like font. Godot's default (Open Sans SemiBold) is wider,
    // heavier and has taller lines, which breaks the tightly packed panel layouts.
    static readonly string[] FontNames = { "Arial", "Liberation Sans", "Arimo", "Helvetica", "Sans-Serif" };

    // Default control colour block.
    public static readonly Color NormalColor = Colors.White;
    public static readonly Color HighlightedColor = new Color(0.9607843f, 0.9607843f, 0.9607843f);
    public static readonly Color PressedColor = new Color(0.78431374f, 0.78431374f, 0.78431374f);
    public static readonly Color DisabledColor = new Color(0.78431374f, 0.78431374f, 0.78431374f);

    // Dropdown list colours (Template background and selected item).
    public static readonly Color ListBackgroundColor = new Color(0.9490197f, 0.9490197f, 0.9490197f);
    public static readonly Color ListSelectedColor = new Color(0.5686275f, 0.78823537f, 0.9686275f);

    /// <summary>Applies a rect (y-up) to a Godot control whose parent is a plain Control.</summary>
    public static T SetRect<T>(T control, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot) where T : Control {
        var offsetMin = anchoredPosition - sizeDelta * pivot;
        var offsetMax = anchoredPosition + sizeDelta * (Vector2.One - pivot);
        // Godot clamps an anchor that would cross its opposite one. Setting left/top first with
        // push_opposite_anchor = true pushes right/bottom out of the way; right/bottom are then set to
        // their final values (always >= left/top). Offsets are overwritten below, so keep_offset is irrelevant.
        control.SetAnchor(Side.Left, anchorMin.X, false, true);
        control.SetAnchor(Side.Right, anchorMax.X, false, true);
        control.SetAnchor(Side.Top, 1 - anchorMax.Y, false, true);
        control.SetAnchor(Side.Bottom, 1 - anchorMin.Y, false, true);
        control.OffsetLeft = offsetMin.X;
        control.OffsetRight = offsetMax.X;
        control.OffsetTop = -offsetMax.Y;
        control.OffsetBottom = -offsetMin.Y;
        return control;
    }

    /// <summary>Shorthand for SetRect with a centred pivot, the most common case.</summary>
    public static T SetRect<T>(T control, float anchorMinX, float anchorMinY, float anchorMaxX, float anchorMaxY,
        float posX, float posY, float sizeX, float sizeY, float pivotX = 0.5f, float pivotY = 0.5f) where T : Control {
        return SetRect(control, new Vector2(anchorMinX, anchorMinY), new Vector2(anchorMaxX, anchorMaxY),
            new Vector2(posX, posY), new Vector2(sizeX, sizeY), new Vector2(pivotX, pivotY));
    }

    /// <summary>Makes a control fill its parent (anchors 0..1, sizeDelta 0).</summary>
    public static T Stretch<T>(T control) where T : Control {
        return SetRect(control, 0, 0, 1, 1, 0, 0, 0, 0);
    }

    /// <summary>Box approximating the sliced sprites of buttons and input fields.</summary>
    public static StyleBoxFlat Box(Color fill, float marginLeft = 0, float marginRight = 0, float marginTop = 0, float marginBottom = 0, bool border = true) {
        var box = new StyleBoxFlat {
            BgColor = fill,
            BorderColor = BorderColor,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            ContentMarginLeft = marginLeft,
            ContentMarginRight = marginRight,
            ContentMarginTop = marginTop,
            ContentMarginBottom = marginBottom
        };
        if (border) {
            box.BorderWidthLeft = 1;
            box.BorderWidthTop = 1;
            box.BorderWidthRight = 1;
            box.BorderWidthBottom = 1;
        }
        return box;
    }

    /// <summary>Flat rectangle without sprite (an image with no source image).</summary>
    public static StyleBoxFlat Flat(Color fill) => new StyleBoxFlat { BgColor = fill };

    public static void SetFontColors(Control control, Color color) {
        control.AddThemeColorOverride("font_color", color);
        control.AddThemeColorOverride("font_hover_color", color);
        control.AddThemeColorOverride("font_hover_pressed_color", color);
        control.AddThemeColorOverride("font_pressed_color", color);
        control.AddThemeColorOverride("font_focus_color", color);
        control.AddThemeColorOverride("font_disabled_color", new Color(color, color.A * 0.5f));
        control.AddThemeColorOverride("font_uneditable_color", new Color(color, color.A * 0.5f));
    }

    public static Label MakeLabel(string text, HorizontalAlignment hAlign = HorizontalAlignment.Center,
        VerticalAlignment vAlign = VerticalAlignment.Center, int fontSize = 14) {
        var label = new Label { Text = text ?? "", HorizontalAlignment = hAlign, VerticalAlignment = vAlign };
        label.AddThemeColorOverride("font_color", TextColor);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeConstantOverride("line_spacing", 0); // line spacing 1 = no extra gap
        return label;
    }

    public static void StyleButton(Button button, float marginLeft = 0, float marginRight = 0) {
        SetFontColors(button, TextColor);
        button.AddThemeConstantOverride("line_spacing", 0);
        button.AddThemeStyleboxOverride("normal", Box(NormalColor * SpriteFill, marginLeft, marginRight));
        button.AddThemeStyleboxOverride("hover", Box(HighlightedColor * SpriteFill, marginLeft, marginRight));
        button.AddThemeStyleboxOverride("pressed", Box(PressedColor * SpriteFill, marginLeft, marginRight));
        button.AddThemeStyleboxOverride("hover_pressed", Box(PressedColor * SpriteFill, marginLeft, marginRight));
        button.AddThemeStyleboxOverride("disabled", Box(new Color(DisabledColor, 0.5f) * SpriteFill, marginLeft, marginRight));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>Input field: white sliced background, text inset 10px left/right.</summary>
    public static void StyleLineEdit(LineEdit lineEdit, HorizontalAlignment alignment = HorizontalAlignment.Left) {
        lineEdit.Alignment = alignment;
        // Godot gives LineEdit a minimum width of 4 "M" characters plus margins (~70px), so narrow fields
        // (e.g. four per row) grew past their rect and overlapped each other. Fields never grow.
        lineEdit.AddThemeConstantOverride("minimum_character_width", 0);
        SetFontColors(lineEdit, TextColor);
        lineEdit.AddThemeColorOverride("font_placeholder_color", PlaceholderColor);
        lineEdit.AddThemeColorOverride("caret_color", TextColor);
        lineEdit.AddThemeColorOverride("selection_color", new Color(0.659f, 0.808f, 1f, 0.75f));
        lineEdit.AddThemeStyleboxOverride("normal", Box(NormalColor, 10, 10, 2, 2));
        lineEdit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        lineEdit.AddThemeStyleboxOverride("read_only", Box(new Color(DisabledColor, 0.5f), 10, 10, 2, 2));
    }

    /// <summary>Dropdown: label inset 10px left and 25px right (arrow area).</summary>
    public static void StyleOptionButton(OptionButton optionButton) {
        StyleButton(optionButton, 10, 8);
        optionButton.FitToLongestItem = false; // dropdowns keep their rect size whatever the options are.
        optionButton.AddThemeColorOverride("icon_normal_color", TextColor);
        optionButton.AddThemeColorOverride("icon_hover_color", TextColor);
        optionButton.AddThemeColorOverride("icon_pressed_color", TextColor);
        optionButton.AddThemeColorOverride("icon_focus_color", TextColor);
        StylePopup(optionButton.GetPopup());
    }

    /// <summary>Dropdown list look (dropdown template).</summary>
    public static void StylePopup(PopupMenu popup) {
        popup.AddThemeColorOverride("font_color", TextColor);
        popup.AddThemeColorOverride("font_hover_color", TextColor);
        popup.AddThemeColorOverride("font_disabled_color", new Color(TextColor, 0.5f));
        popup.AddThemeFontSizeOverride("font_size", 14);
        var panel = Box(ListBackgroundColor, 0, 0, 0, 0);
        panel.SetCornerRadiusAll(0);
        popup.AddThemeStyleboxOverride("panel", panel);
        popup.AddThemeStyleboxOverride("hover", Flat(ListSelectedColor));
        popup.AddThemeConstantOverride("item_start_padding", 10);
        popup.AddThemeConstantOverride("item_end_padding", 10);
        popup.AddThemeConstantOverride("v_separation", 3);
    }

    public static void StyleSlider(Godot.Slider slider) {
        slider.Scrollable = false; // sliders ignore the mouse wheel; let the panel scroll instead.
        var track = Box(NormalColor);
        track.ContentMarginTop = 5;
        track.ContentMarginBottom = 5;
        slider.AddThemeStyleboxOverride("slider", track);
        var fill = Box(NormalColor);
        fill.ContentMarginTop = 5;
        fill.ContentMarginBottom = 5;
        slider.AddThemeStyleboxOverride("grabber_area", fill);
        slider.AddThemeStyleboxOverride("grabber_area_highlight", fill);
        var knob = HandleTexture();
        slider.AddThemeIconOverride("grabber", knob);
        slider.AddThemeIconOverride("grabber_highlight", knob);
        slider.AddThemeIconOverride("grabber_disabled", HandleTexture(true));
    }

    static ImageTexture checkerboard, whiteTexture;
    static readonly ImageTexture[] handleTextures = new ImageTexture[2];
    static readonly ImageTexture[] checkboxTextures = new ImageTexture[4];

    /// <summary>
    /// White circular knob used by sliders (Knob sprite, 20x20). The handle is the slider's target
    /// graphic, so the disabled variant applies the Selectable disabled tint (200, 200, 200, 128).
    /// </summary>
    static ImageTexture HandleTexture(bool disabled = false) {
        int index = disabled ? 1 : 0;
        if (handleTextures[index] != null) return handleTextures[index];
        const int size = 20;
        var img = Godot.Image.CreateEmpty(size, size, false, Godot.Image.Format.Rgba8);
        var center = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                var d = new Vector2(x, y).DistanceTo(center);
                if (d <= size / 2f - 0.5f) img.SetPixel(x, y, d >= size / 2f - 1.5f ? BorderColor : Colors.White);
                else img.SetPixel(x, y, new Color(0, 0, 0, 0));
            }
        }
        if (disabled) {
            var tint = new Color(DisabledColor, 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    img.SetPixel(x, y, img.GetPixel(x, y) * tint);
        }
        return handleTextures[index] = ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// Toggle background (20x20 UISprite: rounded corners, grey border, (245, 245, 245) fill) with
    /// optional checkmark. Disabled variants apply the Selectable disabled tint (200, 200, 200, 128).
    /// </summary>
    public static ImageTexture CheckboxTexture(bool isChecked, bool disabled = false) {
        int index = (isChecked ? 1 : 0) + (disabled ? 2 : 0);
        if (checkboxTextures[index] != null) return checkboxTextures[index];
        const int size = 20;
        const float radius = 3f;
        var img = Godot.Image.CreateEmpty(size, size, false, Godot.Image.Format.Rgba8);
        var half = new Vector2(size / 2f, size / 2f);
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                // Signed distance from the pixel centre to the rounded rectangle outline.
                var p = new Vector2(x + 0.5f, y + 0.5f) - half;
                var q = new Vector2(Mathf.Abs(p.X), Mathf.Abs(p.Y)) - (half - new Vector2(radius, radius));
                var outside = new Vector2(Mathf.Max(q.X, 0), Mathf.Max(q.Y, 0)).Length();
                var d = outside + Mathf.Min(Mathf.Max(q.X, q.Y), 0) - radius;
                var alpha = Mathf.Clamp(0.5f - d, 0f, 1f);
                var borderMix = Mathf.Clamp(d + 1.5f, 0f, 1f);
                var c = SpriteFill.Lerp(BorderColor, borderMix);
                img.SetPixel(x, y, new Color(c, alpha));
            }
        }
        if (isChecked) {
            void Stamp(float fx, float fy) {
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++) {
                        int px = Mathf.RoundToInt(fx) + dx, py = Mathf.RoundToInt(fy) + dy;
                        if (px > 0 && py > 0 && px < size - 1 && py < size - 1) img.SetPixel(px, py, TextColor);
                    }
            }
            void Line(Vector2 a, Vector2 b) {
                for (float t = 0; t <= 1; t += 0.05f) {
                    var p = a.Lerp(b, t);
                    Stamp(p.X, p.Y);
                }
            }
            Line(new Vector2(4.5f, 10), new Vector2(8, 14));
            Line(new Vector2(8, 14), new Vector2(15.5f, 5.5f));
        }
        if (disabled) {
            var tint = new Color(DisabledColor, 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    img.SetPixel(x, y, img.GetPixel(x, y) * tint);
        }
        return checkboxTextures[index] = ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// 1x1 white texture: an image with no sprite renders as a plain white rect, which is what the
    /// texture previews show when no texture is set (covering the checkerboard behind them).
    /// </summary>
    public static ImageTexture WhiteTexture() {
        if (whiteTexture != null) return whiteTexture;
        var img = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
        img.Fill(Colors.White);
        whiteTexture = ImageTexture.CreateFromImage(img);
        return whiteTexture;
    }

    public static void StyleCheckBox(CheckBox checkBox) {
        SetFontColors(checkBox, TextColor);
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" }) {
            checkBox.AddThemeStyleboxOverride(state, empty);
        }
        checkBox.AddThemeIconOverride("checked", CheckboxTexture(true));
        checkBox.AddThemeIconOverride("unchecked", CheckboxTexture(false));
        checkBox.AddThemeIconOverride("checked_disabled", CheckboxTexture(true, true));
        checkBox.AddThemeIconOverride("unchecked_disabled", CheckboxTexture(false, true));
        checkBox.AddThemeConstantOverride("h_separation", 1);
        checkBox.AddThemeConstantOverride("line_spacing", 0);
    }

    /// <summary>Grey/white checkerboard shown behind texture previews (same pattern as MaterialManager).</summary>
    public static ImageTexture Checkerboard() {
        if (checkerboard != null) return checkerboard;
        const int cSize = 64, sq = cSize / 4, sq2 = sq / 2;
        var img = Godot.Image.CreateEmpty(cSize, cSize, false, Godot.Image.Format.Rgb8);
        for (int y = 0; y < cSize; y++) {
            for (int x = 0; x < cSize; x++) {
                bool white = y % sq < sq2 ^ x % sq < sq2;
                img.SetPixel(x, y, white ? Colors.White : new Color(0.5f, 0.5f, 0.5f));
            }
        }
        checkerboard = ImageTexture.CreateFromImage(img);
        return checkerboard;
    }

    /// <summary>Panel scrollbar: 10px wide, white track, grey handle.</summary>
    public static void StyleScrollBar(ScrollBar bar, float width = 10) {
        bar.CustomMinimumSize = new Vector2(width, 0);
        bar.AddThemeStyleboxOverride("scroll", Box(NormalColor));
        bar.AddThemeStyleboxOverride("scroll_focus", Box(NormalColor));
        bar.AddThemeStyleboxOverride("grabber", Box(PressedColor));
        bar.AddThemeStyleboxOverride("grabber_highlight", Box(HighlightedColor.Darkened(0.15f)));
        bar.AddThemeStyleboxOverride("grabber_pressed", Box(PressedColor.Darkened(0.1f)));
    }

    static Font boldFont;

    /// <summary>Bold variant of the UI font (the real Arial Bold face when available).</summary>
    public static Font BoldFont() {
        if (boldFont == null) {
            boldFont = new SystemFont { FontNames = FontNames, FontWeight = 700 };
        }
        return boldFont;
    }

    /// <summary>Tooltips cover the whole element; Godot only reads the hovered control's tooltip.</summary>
    public static void SetTooltipRecursive(Control control, string tooltip) {
        control.TooltipText = tooltip ?? "";
        foreach (var child in control.GetChildren()) {
            if (child is Control c) SetTooltipRecursive(c, tooltip);
        }
    }

    /// <summary>Loads an image file for UI previews.</summary>
    public static Texture2D LoadPreview(string path) {
        if (string.IsNullOrEmpty(path) || path == "_TRANSPARENT_") return null;
        try {
            return TextureImporter.LoadTexture(path);
        } catch (System.Exception e) {
            GameEngine.LogWarning("Unable to load preview " + path + ": " + e.Message);
            return null;
        }
    }
}

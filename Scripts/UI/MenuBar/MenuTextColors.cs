using System.Collections.Generic;
using Godot;

//PopupMenu has no per-item text color. Keep its layout, input, icons and accessibility text, and draw only the labels
//in its scrolling item canvas. The row metrics follow PopupMenu's font/icon/separator theme metrics.
public partial class MenuTextColors : Control {
    PopupMenu popup;
    Dictionary<int, Color> colors;
    readonly List<TextLine> labels = new();

    static Control ItemCanvas(Node root) {
        foreach (var child in root.GetChildren(true)) {
            if (root is ScrollContainer && child.GetType() == typeof(Control)) return (Control)child;
            if (child is PopupMenu) continue;
            var found = ItemCanvas(child);
            if (found != null) return found;
        }
        return null;
    }

    public static void Configure(PopupMenu popup, Dictionary<int, Color> colors) {
        var canvas = ItemCanvas(popup);
        if (canvas == null) return;
        var overlay = canvas.GetNodeOrNull<MenuTextColors>("MenuTextColors");
        if (colors.Count == 0) {
            foreach (var key in new[] { "font_color", "font_hover_color", "font_disabled_color" }) popup.RemoveThemeColorOverride(key);
            UILayout.StylePopup(popup);
            if (overlay != null) { canvas.RemoveChild(overlay); overlay.QueueFree(); }
            return;
        }
        if (overlay == null) {
            overlay = new MenuTextColors { Name = "MenuTextColors", MouseFilter = MouseFilterEnum.Ignore };
            canvas.AddChild(overlay);
        }
        overlay.popup = popup;
        overlay.colors = new(colors);
        foreach (var old in overlay.labels) old.Dispose();
        overlay.labels.Clear();
        var font = popup.GetThemeFont("font", "PopupMenu");
        var size = popup.GetThemeFontSize("font_size", "PopupMenu");
        for (int i = 0; i < popup.ItemCount; i++) {
            var line = new TextLine();
            line.AddString(popup.GetItemText(i), font, size);
            overlay.labels.Add(line);
        }
        foreach (var key in new[] { "font_color", "font_hover_color", "font_disabled_color" }) popup.AddThemeColorOverride(key, Colors.Transparent);
        overlay.QueueRedraw();
    }

    public override void _Process(double delta) {
        if (popup?.Visible == true) QueueRedraw();
    }

    public override void _Draw() {
        if (popup == null || !popup.Visible) return;
        int Constant(string key) => popup.GetThemeConstant(key, "PopupMenu");
        Texture2D Icon(string key) => popup.GetThemeIcon(key, "PopupMenu");
        var fontSize = popup.GetThemeFontSize("font_size", "PopupMenu");
        var fontHeight = popup.GetThemeFont("font", "PopupMenu").GetHeight(fontSize);
        var checkWidth = 0f;
        var checkHeight = 0f;
        var iconWidth = 0f;
        var compact = Constant("gutter_compact") != 0;
        for (int i = 0; i < popup.ItemCount; i++) {
            var icon = popup.GetItemIcon(i);
            if (icon != null) iconWidth = Mathf.Max(iconWidth, icon.GetWidth());
            if (!popup.IsItemCheckable(i)) continue;
            checkWidth = Mathf.Max(Icon("checked").GetWidth(), Icon("radio_checked").GetWidth());
            checkHeight = Mathf.Max(Icon("checked").GetHeight(), Icon("radio_checked").GetHeight());
            if (icon != null) compact = false;
        }
        var y = Constant("v_separation") / 2;
        for (int i = 0; i < popup.ItemCount && i < labels.Count; i++) {
            var line = labels[i];
            var icon = popup.GetItemIcon(i);
            var separator = popup.IsItemSeparator(i);
            var height = Mathf.Max(line.GetSize().Y, icon?.GetHeight() ?? 0);
            if (separator) {
                foreach (var key in new[] { "separator", "labeled_separator_left", "labeled_separator_right" }) height = Mathf.Max(height, popup.GetThemeStylebox(key, "PopupMenu").GetMinimumSize().Y);
            } else {
                height = Mathf.Max(height, fontHeight);
                if (popup.IsItemCheckable(i)) height = Mathf.Max(height, checkHeight);
                var gutter = compact ? Mathf.Max(iconWidth, checkWidth) + Constant("h_separation")
                    : (iconWidth > 0 ? iconWidth + Constant("h_separation") : 0) + (checkWidth > 0 ? checkWidth + Constant("h_separation") : 0);
                var x = Constant("item_start_padding") + gutter + popup.GetItemIndent(i) * Constant("indent");
                if (IsLayoutRtl()) x = ((Control)GetParent()).Size.X - line.GetSize().X - x;
                var color = colors.TryGetValue(i, out var selected) ? selected : UILayout.TextColor;
                if (popup.IsItemDisabled(i)) color.A *= 0.5f;
                line.Draw(GetCanvasItem(), new Vector2(x, y + Mathf.Floor((height - line.GetSize().Y) / 2)), color);
            }
            y += (int)height + Constant("v_separation");
        }
    }

    public override void _ExitTree() {
        foreach (var line in labels) line.Dispose();
        labels.Clear();
    }
}

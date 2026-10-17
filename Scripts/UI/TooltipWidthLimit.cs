using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// Limits the width of the default tooltips (registered as an autoload): Godot's tooltip label never wraps,
/// so long tooltips become a single very long line. Like the tooltip panel (300 units wide with a
/// 5 units padding, the tooltip look is in RuntimeTheme.tres), text longer than MaxWidth is word wrapped.
/// </summary>
public partial class TooltipWidthLimit : Node {
    const float MaxWidth = 290.0f;

    public override void _Ready() {
        GetTree().NodeAdded += OnNodeAdded;
    }

    void OnNodeAdded(Node node) {
        // the viewport creates the label with its text, adds it to the tooltip popup and then sizes the popup
        // from its contents minimum size, so the label can be changed here before the popup is sized
        if (node is not Label label || label.ThemeTypeVariation != "TooltipLabel") return;
        if (label.AutowrapMode != TextServer.AutowrapMode.Off || label.GetMinimumSize().X <= MaxWidth) return;
        // the line breaks are inserted in the text instead of using the label autowrap, which wraps at the
        // label width: setting it here (before the label is ready) is overridden by its anchors
        label.Text = WrapText(label.Text, label.GetThemeFont("font"), label.GetThemeFontSize("font_size"));
    }

    static string WrapText(string text, Font font, int fontSize) {
        var paragraph = new TextParagraph {
            Width = MaxWidth,
            BreakFlags = TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound
                | TextServer.LineBreakFlag.Adaptive
        };
        paragraph.AddString(text, font, fontSize);
        var chars = text.EnumerateRunes().ToArray(); //the line ranges are in Unicode code points
        var res = new StringBuilder(text.Length + 16);
        for (int i = 0; i < paragraph.GetLineCount(); i++) {
            var range = paragraph.GetLineRange(i);
            var line = new StringBuilder();
            for (int j = range.X; j < range.Y && j < chars.Length; j++) line.Append(chars[j].ToString());
            if (i > 0) res.Append('\n');
            res.Append(line.ToString().TrimEnd()); //drops the space or newline the line was broken at
        }
        return res.ToString();
    }
}

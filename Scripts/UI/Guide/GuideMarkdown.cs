using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;

//The subset of Markdown used by the guides: headings, paragraphs, bold, italic, inline code, nested lists, tables,
//fenced code, blockquotes (shown as notes), rules, images and links. The parser is independent of the UI, the renderer
//uses the tag stack of the RichTextLabel instead of BBCode, so that the text of the guides doesn't need escaping.
public static class GuideMarkdown {
    public enum BlockType { Heading, Paragraph, List, Table, Code, Quote, Rule }

    //A piece of text with the same style. An image has src set, and its text is the alternative text
    public class Run {
        public string text;
        public bool bold, italic, code;
        public string link;
        public string src;
    }

    public class ListItem {
        public List<Run> runs = new List<Run>();
        public Block sublist;
    }

    public class Block {
        public BlockType type;
        public int level; //the heading level
        public List<Run> runs = new List<Run>(); //heading and paragraph
        public string text; //code
        public bool ordered; //list
        public List<ListItem> items = new List<ListItem>(); //list
        public List<List<Run>> header = new List<List<Run>>(); //table
        public List<List<List<Run>>> rows = new List<List<List<Run>>>(); //table
        public List<Block> children = new List<Block>(); //quote
    }

    static readonly Regex ListMarker = new Regex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$");
    static readonly Regex HeadingLine = new Regex(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
    static readonly Regex RuleLine = new Regex(@"^\s*([-*_])(\s*\1){2,}\s*$");
    static readonly Regex TableSeparator = new Regex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$");

    public static List<Block> Parse(string markdown) {
        var lines = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return ParseLines(lines);
    }

    static List<Block> ParseLines(string[] lines) {
        var res = new List<Block>();
        var i = 0;
        while (i < lines.Length) {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) {
                i++;
            } else if (line.TrimStart().StartsWith("```")) {
                res.Add(ParseCode(lines, ref i));
            } else if (HeadingLine.Match(line) is { Success: true } heading) {
                res.Add(new Block { type = BlockType.Heading, level = heading.Groups[1].Length, runs = ParseInline(heading.Groups[2].Value) });
                i++;
            } else if (RuleLine.IsMatch(line)) {
                res.Add(new Block { type = BlockType.Rule });
                i++;
            } else if (line.TrimStart().StartsWith(">")) {
                res.Add(ParseQuote(lines, ref i));
            } else if (IsTableStart(lines, i)) {
                res.Add(ParseTable(lines, ref i));
            } else if (ListMarker.IsMatch(line)) {
                res.Add(ParseList(lines, ref i, ListMarker.Match(line).Groups[1].Length));
            } else {
                res.Add(ParseParagraph(lines, ref i));
            }
        }
        return res;
    }

    static Block ParseCode(string[] lines, ref int i) {
        var text = new List<string>();
        i++;
        while (i < lines.Length && !lines[i].TrimStart().StartsWith("```")) text.Add(lines[i++]);
        i++;
        return new Block { type = BlockType.Code, text = string.Join("\n", text) };
    }

    static Block ParseQuote(string[] lines, ref int i) {
        var inner = new List<string>();
        while (i < lines.Length && lines[i].TrimStart().StartsWith(">")) {
            var content = lines[i].TrimStart().Substring(1);
            inner.Add(content.StartsWith(" ") ? content.Substring(1) : content);
            i++;
        }
        return new Block { type = BlockType.Quote, children = ParseLines(inner.ToArray()) };
    }

    static bool IsTableStart(string[] lines, int i) {
        return lines[i].Contains('|') && i + 1 < lines.Length && lines[i + 1].Contains('-') && TableSeparator.IsMatch(lines[i + 1]);
    }

    static Block ParseTable(string[] lines, ref int i) {
        var block = new Block { type = BlockType.Table };
        block.header = SplitCells(lines[i]).Select(ParseInline).ToList();
        i += 2;
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].Contains('|')) {
            var row = SplitCells(lines[i]).Select(ParseInline).ToList();
            while (row.Count < block.header.Count) row.Add(new List<Run>());
            block.rows.Add(row.Take(block.header.Count).ToList());
            i++;
        }
        return block;
    }

    //the cells of a table line: the | of inline code and the escaped ones don't separate them
    static List<string> SplitCells(string line) {
        line = line.Trim();
        if (line.StartsWith("|")) line = line.Substring(1);
        var res = new List<string>();
        var cell = new StringBuilder();
        var inCode = false;
        for (var c = 0; c < line.Length; c++) {
            if (line[c] == '\\' && c + 1 < line.Length && line[c + 1] == '|') {
                cell.Append('|');
                c++;
            } else if (line[c] == '|' && !inCode) {
                res.Add(cell.ToString().Trim());
                cell.Clear();
            } else {
                if (line[c] == '`') inCode = !inCode;
                cell.Append(line[c]);
            }
        }
        if (cell.ToString().Trim() != "") res.Add(cell.ToString().Trim());
        return res;
    }

    static Block ParseList(string[] lines, ref int i, int indent) {
        var first = ListMarker.Match(lines[i]);
        var block = new Block { type = BlockType.List, ordered = IsOrdered(first) };
        var text = (StringBuilder)null;
        ListItem current = null;
        void EndItem() {
            if (current != null && text != null) current.runs = ParseInline(text.ToString().TrimEnd());
            text = null;
        }
        while (i < lines.Length) {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) {
                //a blank line ends the list, unless it goes on with another item
                var next = i + 1;
                while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next])) next++;
                if (next >= lines.Length) { i = next; break; }
                var nextMarker = ListMarker.Match(lines[next]);
                if (!nextMarker.Success || nextMarker.Groups[1].Length < indent) break;
                if (nextMarker.Groups[1].Length == indent && IsOrdered(nextMarker) != block.ordered) break;
                i = next;
                continue;
            }
            var marker = ListMarker.Match(line);
            if (marker.Success) {
                var markerIndent = marker.Groups[1].Length;
                if (markerIndent < indent) break;
                if (markerIndent == indent && IsOrdered(marker) != block.ordered) break; //another kind of list starts
                if (markerIndent > indent && current != null) {
                    EndItem();
                    current.sublist = ParseList(lines, ref i, markerIndent);
                    continue;
                }
                EndItem();
                current = new ListItem();
                block.items.Add(current);
                text = new StringBuilder(marker.Groups[3].Value);
                i++;
            } else {
                //a continuation line of the item
                if (current == null || text == null || IsBlockStart(lines, i)) break;
                AppendLine(text, line);
                i++;
            }
        }
        EndItem();
        return block;
    }

    static bool IsOrdered(Match marker) {
        return char.IsDigit(marker.Groups[2].Value[0]);
    }

    static Block ParseParagraph(string[] lines, ref int i) {
        var text = new StringBuilder(lines[i].TrimStart());
        i++;
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsBlockStart(lines, i)) {
            AppendLine(text, lines[i]);
            i++;
        }
        return new Block { type = BlockType.Paragraph, runs = ParseInline(text.ToString().TrimEnd()) };
    }

    //breaks a line without starting a new paragraph (a newline would, in a RichTextLabel)
    const string LineBreak = "\u2028";

    //joins two lines of a paragraph: two spaces at the end of the first one are a line break
    static void AppendLine(StringBuilder text, string line) {
        var breaks = text.Length > 1 && text[text.Length - 1] == ' ' && text[text.Length - 2] == ' ';
        while (text.Length > 0 && text[text.Length - 1] == ' ') text.Length--;
        text.Append(breaks ? LineBreak : " ").Append(line.TrimStart());
    }

    static bool IsBlockStart(string[] lines, int i) {
        var line = lines[i];
        return line.TrimStart().StartsWith("```") || HeadingLine.IsMatch(line) || RuleLine.IsMatch(line)
            || line.TrimStart().StartsWith(">") || ListMarker.IsMatch(line) || IsTableStart(lines, i);
    }

    public static List<Run> ParseInline(string text) {
        var res = new List<Run>();
        ParseInline(text ?? "", false, false, null, res);
        return res;
    }

    //Only * marks emphasis: the names of the game files are full of _. A marker without its closing one is plain text.
    static void ParseInline(string text, bool bold, bool italic, string link, List<Run> res) {
        var plain = new StringBuilder();
        void Flush() {
            if (plain.Length > 0) res.Add(new Run { text = plain.ToString(), bold = bold, italic = italic, link = link });
            plain.Clear();
        }
        var i = 0;
        while (i < text.Length) {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && !char.IsLetterOrDigit(text[i + 1])) {
                plain.Append(text[i + 1]);
                i += 2;
            } else if (c == '`' && text.IndexOf('`', i + 1) is var codeEnd && codeEnd > i) {
                Flush();
                res.Add(new Run { text = text.Substring(i + 1, codeEnd - i - 1), code = true, bold = bold, italic = italic, link = link });
                i = codeEnd + 1;
            } else if (c == '!' && i + 1 < text.Length && text[i + 1] == '[' && ParseLink(text, i + 1, out var alt, out var src, out var imageEnd)) {
                Flush();
                res.Add(new Run { text = alt, src = src, link = link });
                i = imageEnd;
            } else if (c == '[' && ParseLink(text, i, out var label, out var href, out var linkEnd)) {
                Flush();
                ParseInline(label, bold, italic, href, res);
                i = linkEnd;
            } else if (c == '*' && i + 1 < text.Length && text[i + 1] == '*' && FindClosing(text, i + 2, "**") is var boldEnd && boldEnd > i + 2) {
                Flush();
                ParseInline(text.Substring(i + 2, boldEnd - i - 2), true, italic, link, res);
                i = boldEnd + 2;
            } else if (c == '*' && FindClosing(text, i + 1, "*") is var italicEnd && italicEnd > i + 1) {
                Flush();
                ParseInline(text.Substring(i + 1, italicEnd - i - 1), bold, true, link, res);
                i = italicEnd + 1;
            } else {
                plain.Append(c);
                i++;
            }
        }
        Flush();
    }

    //The closing marker of an emphasis: the text starts and ends without spaces. Inline code is skipped, and a single *
    //doesn't close on one of the two of a ** nor the other way around.
    static int FindClosing(string text, int start, string marker) {
        if (start >= text.Length || char.IsWhiteSpace(text[start])) return -1;
        for (var i = start; i < text.Length; i++) {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '`') {
                var end = text.IndexOf('`', i + 1);
                if (end < 0) continue;
                i = end;
                continue;
            }
            if (text[i] != '*') continue;
            var count = 1;
            while (i + count < text.Length && text[i + count] == '*') count++;
            if (count >= marker.Length && i > start && !char.IsWhiteSpace(text[i - 1]) && (count != 2 || marker.Length == 2)) {
                //the closing ** of ***text*** is after the closing * of the text
                return count == 3 && marker.Length == 2 ? i + 1 : i;
            }
            i += count - 1;
        }
        return -1;
    }

    //[label](target) starting at the bracket
    static bool ParseLink(string text, int start, out string label, out string target, out int end) {
        label = target = null;
        end = -1;
        var depth = 0;
        for (var i = start; i < text.Length; i++) {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '[') depth++;
            else if (text[i] == ']' && --depth == 0) {
                if (i + 1 >= text.Length || text[i + 1] != '(') return false;
                var close = text.IndexOf(')', i + 2);
                if (close < 0) return false;
                label = text.Substring(start + 1, i - start - 1);
                target = text.Substring(i + 2, close - i - 2).Trim();
                end = close + 1;
                return true;
            }
        }
        return false;
    }

    //all the links and images of the blocks, to check that they lead somewhere
    public static IEnumerable<Run> AllRuns(IEnumerable<Block> blocks) {
        foreach (var block in blocks) {
            foreach (var run in block.runs) yield return run;
            foreach (var run in ItemRuns(block.items)) yield return run;
            foreach (var cell in block.header.Concat(block.rows.SelectMany(r => r))) {
                foreach (var run in cell) yield return run;
            }
            foreach (var run in AllRuns(block.children)) yield return run;
        }
    }

    static IEnumerable<Run> ItemRuns(IEnumerable<ListItem> items) {
        foreach (var item in items) {
            foreach (var run in item.runs) yield return run;
            if (item.sublist == null) continue;
            foreach (var run in ItemRuns(item.sublist.items)) yield return run;
        }
    }

    //the text without the styles, for the tests and the alternative text of the images
    public static string PlainText(IEnumerable<Run> runs) {
        return string.Concat(runs.Select(r => r.text));
    }

    //Renders

    public const int BaseFontSize = 14;
    const int NarrowColumn = 12;
    static readonly Color LinkColor = new Color(0.08f, 0.33f, 0.72f);
    static readonly Color CodeBackground = new Color(0, 0, 0, 0.08f);
    static readonly Color NoteColor = new Color(0.28f, 0.28f, 0.28f);
    static readonly Color TableHeader = new Color(0.8f, 0.8f, 0.8f);
    static readonly Color TableOdd = new Color(0.97f, 0.97f, 0.97f);
    static readonly Color TableEven = new Color(0.92f, 0.92f, 0.92f);
    static readonly Color TableBorder = new Color(0.62f, 0.62f, 0.62f);

    //the style of the fonts and text of a label showing a guide
    public static void StyleLabel(RichTextLabel label) {
        //whole words only: with WordSmart a table column can shrink to a single letter, as its words can be broken
        label.AutowrapMode = TextServer.AutowrapMode.Word;
        label.AddThemeColorOverride("default_color", UILayout.TextColor);
        label.AddThemeFontSizeOverride("normal_font_size", BaseFontSize);
        label.AddThemeFontSizeOverride("bold_font_size", BaseFontSize);
        label.AddThemeFontSizeOverride("italics_font_size", BaseFontSize);
        label.AddThemeFontSizeOverride("bold_italics_font_size", BaseFontSize);
        label.AddThemeFontSizeOverride("mono_font_size", BaseFontSize - 1);
        label.AddThemeFontOverride("bold_font", UILayout.BoldFont());
        label.AddThemeFontOverride("italics_font", UILayout.ItalicFont());
        label.AddThemeFontOverride("bold_italics_font", UILayout.BoldItalicFont());
        label.AddThemeFontOverride("mono_font", UILayout.MonoFont());
        label.AddThemeConstantOverride("paragraph_separation", 7);
        label.AddThemeConstantOverride("table_h_separation", 0);
        label.AddThemeConstantOverride("table_v_separation", 0);
    }

    //images: gives the texture of the src of an image, null if it can't be shown
    public static void Render(RichTextLabel label, List<Block> blocks, System.Func<string, Texture2D> images) {
        label.Clear();
        RenderBlocks(label, blocks, images, 0);
    }

    static void RenderBlocks(RichTextLabel label, List<Block> blocks, System.Func<string, Texture2D> images, int listDepth) {
        for (var i = 0; i < blocks.Count; i++) {
            RenderBlock(label, blocks[i], images, listDepth);
            if (i < blocks.Count - 1) label.Newline();
        }
    }

    static void RenderBlock(RichTextLabel label, Block block, System.Func<string, Texture2D> images, int listDepth) {
        switch (block.type) {
            case BlockType.Heading:
                label.PushFont(UILayout.BoldFont(), block.level == 1 ? 22 : block.level == 2 ? 17 : 15);
                RenderRuns(label, block.runs, images);
                label.Pop();
                break;
            case BlockType.Paragraph:
                RenderRuns(label, block.runs, images);
                break;
            case BlockType.List:
                RenderList(label, block, images, listDepth);
                break;
            case BlockType.Table:
                RenderTable(label, block, images);
                break;
            case BlockType.Code:
                label.PushIndent(1);
                label.PushMono();
                label.PushBgcolor(CodeBackground);
                label.AddText(block.text.Replace("\n", LineBreak));
                label.Pop();
                label.Pop();
                label.Pop();
                break;
            case BlockType.Quote:
                label.PushIndent(1);
                label.PushColor(NoteColor);
                RenderBlocks(label, block.children, images, listDepth);
                label.Pop();
                label.Pop();
                break;
            case BlockType.Rule:
                label.PushColor(TableBorder);
                label.AddText(new string('─', 30));
                label.Pop();
                break;
        }
    }

    static void RenderList(RichTextLabel label, Block block, System.Func<string, Texture2D> images, int depth) {
        label.PushList(depth + 1, block.ordered ? RichTextLabel.ListType.Numbers : RichTextLabel.ListType.Dots, false);
        for (var i = 0; i < block.items.Count; i++) {
            var item = block.items[i];
            RenderRuns(label, item.runs, images);
            if (item.sublist != null) {
                label.Newline();
                RenderList(label, item.sublist, images, depth + 1);
            }
            if (i < block.items.Count - 1) label.Newline();
        }
        label.Pop();
    }

    static void RenderTable(RichTextLabel label, Block block, System.Func<string, Texture2D> images) {
        var columns = block.header.Count;
        if (columns == 0) return;
        label.PushTable(columns);
        var padding = new Rect2(5, 3, 5, 3);
        void Cell(List<Run> runs, bool header) {
            label.PushCell();
            label.SetCellBorderColor(TableBorder);
            label.SetCellPadding(padding);
            if (header) label.SetCellRowBackgroundColor(TableHeader, TableHeader);
            else label.SetCellRowBackgroundColor(TableOdd, TableEven);
            if (header) label.PushBold();
            RenderRuns(label, runs, images);
            if (header) label.Pop();
            label.Pop();
        }
        for (var c = 0; c < columns; c++) {
            //short columns keep their width, the others share the rest of it by the length of their text
            var longest = block.rows.Select(r => PlainText(r[c]).Length).Append(PlainText(block.header[c]).Length).Max();
            label.SetTableColumnExpand(c, longest > NarrowColumn, Mathf.Clamp(longest, 1, 60));
            Cell(block.header[c], true);
        }
        foreach (var row in block.rows) {
            foreach (var cell in row) Cell(cell, false);
        }
        label.Pop();
    }

    static void RenderRuns(RichTextLabel label, List<Run> runs, System.Func<string, Texture2D> images) {
        foreach (var run in runs) RenderRun(label, run, images);
    }

    static void RenderRun(RichTextLabel label, Run run, System.Func<string, Texture2D> images) {
        var pushed = 0;
        void Push(System.Action action) {
            action();
            pushed++;
        }
        if (run.link != null) {
            Push(() => label.PushMeta(run.link));
            Push(() => label.PushColor(LinkColor));
        }
        if (run.src != null) {
            var texture = images?.Invoke(run.src);
            if (texture != null) {
                //wide images fit the text, the others keep their size (a size of 0 keeps the aspect ratio)
                var wide = texture.GetWidth() > 300;
                label.AddImage(texture, wide ? 100f : 0f, 0f, null, InlineAlignment.Center, null, default, false, run.text ?? "",
                    wide ? RichTextLabel.ImageUnit.Percent : RichTextLabel.ImageUnit.Pixel, RichTextLabel.ImageUnit.Pixel, run.text ?? "");
            } else {
                Push(label.PushItalics);
                label.AddText(string.IsNullOrEmpty(run.text) ? run.src : run.text);
            }
        } else {
            if (run.code) {
                Push(label.PushMono);
                Push(() => label.PushBgcolor(CodeBackground));
            } else if (run.bold && run.italic) {
                Push(label.PushBoldItalics);
            } else if (run.bold) {
                Push(label.PushBold);
            } else if (run.italic) {
                Push(label.PushItalics);
            }
            label.AddText(run.text);
        }
        for (var i = 0; i < pushed; i++) label.Pop();
    }
}

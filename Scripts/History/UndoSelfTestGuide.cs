using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace History {
    //The guides: the Markdown parser, the content of all the installed guides, the links and images a guide
    //can't reach, and the panel (navigation, move, resize, minimize, close, what it does to the editor).
    public partial class UndoSelfTest {
        const string PanelPage = "Layout/Body/Page/";
        string guideFixtures;

        async void RunGuide() {
            var lastCity = SettingsManager.Get("LastCity", "");
            GuidePanel panel = null;
            try {
                await Frames(2);
                guideFixtures = Path.GetFullPath(Path.Combine(folder, "..", "guide_fixtures"));
                if (Directory.Exists(guideFixtures)) Directory.Delete(guideFixtures, true);
                var (first, second) = WriteGuideFixtures();
                GuideParserTests();
                GuideContentTests();
                GuideLinkTests(first, second);
                //the guides are for beginners too: they have to work with no city
                panel = GuidePanel.Open(builder, first);
                await Frames(2);
                Check("the panel opens with no city", panel.Visible && panel.Guide.id == first.id);
                await GuideNavigationTests(panel, first);
                await GuideGeometryTests(panel);
                await GuideStateTests(panel, first, second);
                await LoadNewCity();
                await Frames(3);
                Check("the panel stays open while a city loads", panel.Visible && GodotObject.IsInstanceValid(panel));
                builder.Reload();
                await Frames(3);
                Check("the panel stays open after a reload of the menu bar", panel.Visible);
                await GuideInputTests(panel);
                await GuideMenuTests();
                await GuideFrames(panel, first);
            } catch (System.Exception e) {
                Log("exception: " + e);
                failures++;
            }
            SettingsManager.Set("LastCity", lastCity);
            Log("finished with " + failures + " failures");
            File.WriteAllLines(Path.Combine(folder, "..", "undo_selftest.log"), log);
            GetTree().Quit(failures == 0 ? 0 : 1);
        }

        (Guide first, Guide second) WriteGuideFixtures() {
            void Write(string guide, string file, string text) {
                var dir = Path.Combine(guideFixtures, guide);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, file), text);
            }
            var longText = string.Join("\n\n", Enumerable.Range(1, 60).Select(i => "Paragraph number " + i + " of a long chapter, to have something to scroll."));
            Write("first", "guide.json", "{ \"title\": \"First guide\", \"order\": 1, \"chapters\": [\"one\", \"two\", \"three\", \"missing\"] }");
            Write("first", "one.md", "# Chapter one\n\nSome **bold**, *italic*, ***both*** and `code` text, a [link](two.md) and a [web link](https://example.com).\n\n" +
                "## A section\n\n- First item\n- Second item\n  - Nested item\n  - Another nested item\n- Third item with `giz_pcar01_l`\n\n1. Step one\n2. Step two\n\n" +
                "### A smaller section\n\n| Flag | Default | What it does |\n|---|---|---|\n| `psdl` | yes | Writes the **city** geometry |\n| `bai` | yes | Writes the traffic paths, with a long cell to see the wrapping of the table in the panel |\n\n" +
                "> A note, shown differently from the text.\n\n```\ncode line 1\ncode line 2\n```\n\n---\n\nA wide image:\n\n![Wide image](images/wide.png)\n\nA small image ![small](images/small.png) in the text, and a missing one: ![Missing image](images/nothing.png)\n");
            Write("first", "two.md", "# Chapter two\n\nSee [the first chapter](one.md).\n");
            Write("first", "three.md", "# Chapter three\n\n" + longText + "\n");
            Write("second", "guide.json", "{ \"title\": \"Second guide\", \"order\": 2, \"chapters\": [\"alone\"] }");
            Write("second", "alone.md", "# Alone\n\nA link to [the other guide](../first/two.md).\n");
            var images = Path.Combine(guideFixtures, "first", "images");
            Directory.CreateDirectory(images);
            var wide = Image.CreateEmpty(600, 120, false, Image.Format.Rgb8);
            wide.Fill(new Color(0.3f, 0.5f, 0.8f));
            wide.SavePng(Path.Combine(images, "wide.png"));
            var small = Image.CreateEmpty(16, 16, false, Image.Format.Rgb8);
            small.Fill(new Color(0.8f, 0.3f, 0.3f));
            small.SavePng(Path.Combine(images, "small.png"));
            File.WriteAllText(Path.Combine(guideFixtures, "secret.png"), "");
            return (GuideLibrary.LoadGuide(Path.Combine(guideFixtures, "first")), GuideLibrary.LoadGuide(Path.Combine(guideFixtures, "second")));
        }

        void GuideParserTests() {
            string Plain(List<GuideMarkdown.Run> runs) => GuideMarkdown.PlainText(runs);
            var inline = GuideMarkdown.ParseInline("a **b** *c* ***d*** `e` [f](g.md) ![h](i.png)");
            Check("inline: the plain text", Plain(inline) == "a b c d e f h");
            Check("inline: bold", inline.Any(r => r.bold && !r.italic && r.text == "b"));
            Check("inline: italic", inline.Any(r => r.italic && !r.bold && r.text == "c"));
            Check("inline: bold and italic", inline.Any(r => r.bold && r.italic && r.text == "d"));
            Check("inline: code", inline.Any(r => r.code && r.text == "e"));
            Check("inline: link", inline.Any(r => r.link == "g.md" && r.text == "f"));
            Check("inline: image", inline.Any(r => r.src == "i.png" && r.text == "h"));
            Check("inline: underscores are plain text", Plain(GuideMarkdown.ParseInline("giz_pcar01_l and _this_")) == "giz_pcar01_l and _this_");
            Check("inline: a lone star is plain text", Plain(GuideMarkdown.ParseInline("mm*data.csv")) == "mm*data.csv");
            Check("inline: spaced stars are not emphasis", Plain(GuideMarkdown.ParseInline("2 * 3 * 4")) == "2 * 3 * 4");
            Check("inline: escapes", Plain(GuideMarkdown.ParseInline("\\*not\\* \\`code\\`")) == "*not* `code`");
            Check("inline: stars in code are code", GuideMarkdown.ParseInline("`a*b*c`").Single().text == "a*b*c");
            Check("inline: bold around a link", GuideMarkdown.ParseInline("**[a](b.md)**").Single() is { bold: true, link: "b.md", text: "a" });
            Check("inline: an unclosed link is plain text", Plain(GuideMarkdown.ParseInline("[a](b")) == "[a](b");

            var blocks = GuideMarkdown.Parse("# Title #\r\n\r\nA paragraph\r\non two lines.\r\nAnd a break  \r\nhere.\r\n\r\n## Section\r\n---\r\n");
            Check("blocks: heading level and text", blocks[0].type == GuideMarkdown.BlockType.Heading && blocks[0].level == 1 && Plain(blocks[0].runs) == "Title");
            Check("blocks: lines join in a paragraph", blocks[1].type == GuideMarkdown.BlockType.Paragraph && Plain(blocks[1].runs).StartsWith("A paragraph on two lines. And a break"));
            Check("blocks: two spaces break the line", Plain(blocks[1].runs).Contains('\u2028'));
            Check("blocks: rule", blocks.Last().type == GuideMarkdown.BlockType.Rule);

            var list = GuideMarkdown.Parse("- a\n- b\n  continued\n  - c\n  - d\n\n- e\n\nAfter").ToList();
            Check("list: items", list[0].type == GuideMarkdown.BlockType.List && list[0].items.Count == 3 && !list[0].ordered);
            Check("list: continuation line", Plain(list[0].items[1].runs) == "b continued");
            Check("list: nesting", list[0].items[1].sublist?.items.Count == 2 && Plain(list[0].items[1].sublist.items[1].runs) == "d");
            Check("list: the paragraph after it", list.Count == 2 && Plain(list[1].runs) == "After");
            Check("list: ordered", GuideMarkdown.Parse("1. a\n2. b")[0] is { type: GuideMarkdown.BlockType.List, ordered: true });
            Check("list: another kind after a blank line is another list", GuideMarkdown.Parse("- a\n\n1. b") is { Count: 2 } kinds && !kinds[0].ordered && kinds[1].ordered);
            Check("list: another kind on the next line is another list", GuideMarkdown.Parse("1. a\n- b") is { Count: 2 } kinds2 && kinds2[0].ordered && !kinds2[1].ordered);
            Check("list: a nested list of another kind stays nested", GuideMarkdown.Parse("- a\n  1. b\n- c") is { Count: 1 } nested && nested[0].items.Count == 2 && nested[0].items[0].sublist.ordered);

            var table = GuideMarkdown.Parse("| A | B |\n|---|:--:|\n| `x|y` | 2 |\n| only one |\n| 1 | 2 | 3 |\n\nAfter");
            Check("table: header", table[0].type == GuideMarkdown.BlockType.Table && table[0].header.Count == 2);
            Check("table: the | of code is not a separator", Plain(table[0].rows[0][0]) == "x|y");
            Check("table: short and long rows fit the header", table[0].rows.Count == 3 && table[0].rows.All(r => r.Count == 2));
            Check("table: the text after it", table.Count == 2);

            var other = GuideMarkdown.Parse("```\n# not a heading\n- not a list\n```\n\n> # Quoted\n> text\n\nA | B without a separator");
            Check("code: taken as it is", other[0] is { type: GuideMarkdown.BlockType.Code, text: "# not a heading\n- not a list" });
            Check("quote: its own blocks", other[1].type == GuideMarkdown.BlockType.Quote && other[1].children.Count == 2);
            Check("a line with | alone is a paragraph", other[2].type == GuideMarkdown.BlockType.Paragraph);
            Check("empty text gives no blocks", GuideMarkdown.Parse("").Count == 0 && GuideMarkdown.Parse(null).Count == 0);
        }

        //every guide installed, so a mistake in the text of a guide (or in a future core) fails here
        void GuideContentTests() {
            var groups = GuideLibrary.Load();
            var own = groups.Find(g => g.folder == null);
            Check("the program has the Basic guide", own?.guides.Find(g => g.id == "basic") != null);
            var mm2 = groups.Find(g => g.folder == "MidtownMadness2");
            if (Directory.Exists(Path.Combine(PathHelper.BasePath(), "Files", "cores", "MidtownMadness2"))) {
                Check("the guides of the MM2 core, in order", mm2 != null && mm2.guides.Select(g => g.id).SequenceEqual(new[] { "beginner", "advanced", "export" }));
                Check("the name of a core is used for its guides", mm2?.name == "Midtown Madness 2");
            }
            foreach (var group in groups) {
                foreach (var guide in group.guides) {
                    var name = (group.name ?? "program") + "/" + guide.id;
                    var problems = new List<string>();
                    foreach (var chapter in guide.chapters) {
                        var text = GuideLibrary.ReadChapter(chapter);
                        if (text == null) {
                            problems.Add(chapter.id + ": can't be read");
                            continue;
                        }
                        var blocks = GuideMarkdown.Parse(text);
                        if (blocks.FirstOrDefault() is not { type: GuideMarkdown.BlockType.Heading, level: 1 }) problems.Add(chapter.id + ": doesn't start with a title");
                        if (blocks.Count < 2) problems.Add(chapter.id + ": empty");
                        if (chapter.title == chapter.id) problems.Add(chapter.id + ": no title");
                        foreach (var run in GuideMarkdown.AllRuns(blocks)) {
                            if (run.link != null && !GuideLibrary.IsWebLink(run.link) && !GuideLibrary.ResolveChapter(groups, guide, run.link, out _, out _)) problems.Add(chapter.id + ": link not found " + run.link);
                            if (run.src != null && GuideLibrary.ResolveImage(guide, run.src) == null) problems.Add(chapter.id + ": image not found " + run.src);
                            if (run.text != null && (run.text.Contains("**") || run.text.Contains("](") || run.text.Contains('`'))) problems.Add(chapter.id + ": unparsed Markdown in \"" + run.text + "\"");
                        }
                    }
                    Check("guide " + name + " (" + guide.chapters.Count + " chapters) " + string.Join("; ", problems), problems.Count == 0);
                }
            }
        }

        void GuideLinkTests(Guide first, Guide second) {
            var groups = new List<GuideGroup> { new GuideGroup { guides = new List<Guide> { first, second } } };
            Check("link: a chapter of the guide", GuideLibrary.ResolveChapter(groups, first, "two.md", out var g1, out var c1) && g1 == first && c1 == 1);
            Check("link: a chapter of another guide", GuideLibrary.ResolveChapter(groups, second, "../first/three.md", out var g2, out var c2) && g2 == first && c2 == 2);
            foreach (var bad in new[] { "nothing.md", "../../../somewhere/else.md", "two.exe", "", "file:///C:/Windows/notepad.exe", "C:/Windows/notepad.exe", "calc.exe", "ms-settings:", "javascript:alert(1)" }) {
                Check("link: not a chapter: \"" + bad + "\"", !GuideLibrary.ResolveChapter(groups, first, bad, out _, out _));
            }
            Check("link: http and https are web links", GuideLibrary.IsWebLink("http://example.com") && GuideLibrary.IsWebLink("https://example.com/a?b=c"));
            foreach (var bad in new[] { "file:///C:/Windows/notepad.exe", "C:/Windows/notepad.exe", "C:\\Windows\\notepad.exe", "\\\\server\\share\\a.exe", "calc.exe", "ms-settings:", "javascript:alert(1)", "two.md", "" }) {
                Check("link: not a web link: \"" + bad + "\"", !GuideLibrary.IsWebLink(bad));
            }
            Check("image: inside of the guide", GuideLibrary.ResolveImage(first, "images/wide.png") != null);
            Check("image: missing", GuideLibrary.ResolveImage(first, "images/nothing.png") == null);
            Check("image: outside of the guide", GuideLibrary.ResolveImage(first, "../secret.png") == null);
            Check("image: an absolute path outside of the guide", GuideLibrary.ResolveImage(first, Path.Combine(guideFixtures, "secret.png")) == null);
            Check("image: a folder with the same start as the guide", !Directory.Exists(first.folder + "x") && GuideLibrary.ResolveImage(first, "../firstx/a.png") == null);
            var noFile = Path.Combine(guideFixtures, "noFile");
            Directory.CreateDirectory(noFile);
            Check("a folder without guide.json isn't a guide", GuideLibrary.LoadGuide(noFile) == null);
            File.WriteAllText(Path.Combine(noFile, "guide.json"), "{ not json");
            Check("a broken guide.json isn't a guide", GuideLibrary.LoadGuide(noFile) == null);
            File.WriteAllText(Path.Combine(noFile, "guide.json"), "{ \"title\": \"a\", \"chapters\": [] }");
            Check("a guide without chapters isn't a guide", GuideLibrary.LoadGuide(noFile) == null);
        }

        static IEnumerable<T> Descendants<T>(Node node) where T : Node {
            foreach (var child in node.GetChildren()) {
                if (child is T match) yield return match;
                foreach (var inner in Descendants<T>(child)) yield return inner;
            }
        }

        static void Press(Button button) {
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        async Task GuideNavigationTests(GuidePanel panel, Guide guide) {
            var previous = panel.GetNode<Button>(PanelPage + "Navigation/Previous");
            var next = panel.GetNode<Button>(PanelPage + "Navigation/Next");
            var position = panel.GetNode<Label>(PanelPage + "Navigation/Position");
            var content = panel.GetNode<RichTextLabel>(PanelPage + "ContentBox/Content");
            var list = panel.GetNode<Control>("Layout/Body/Chapters/List");
            Check("one button for each chapter", list.GetChildCount() == guide.chapters.Count);
            Check("the title of the guide", panel.GetNode<Label>("Layout/TitleBar/Title").Text == guide.title);
            Check("a guide opens on its first chapter", panel.ChapterIndex == 0 && content.GetParsedText().Contains("Chapter one"));
            Check("no previous chapter at the start", previous.Disabled && !next.Disabled);
            Check("the position", position.Text == "1 / 4");
            Press(next);
            await Frames(1);
            Check("next chapter", panel.ChapterIndex == 1 && content.GetParsedText().Contains("Chapter two") && !previous.Disabled);
            Check("the position follows", position.Text == "2 / 4");
            Press(previous);
            await Frames(1);
            Check("previous chapter", panel.ChapterIndex == 0);
            Press(list.GetChild<Button>(2));
            await Frames(1);
            Check("chapter button", panel.ChapterIndex == 2 && content.GetParsedText().Contains("Chapter three"));
            Press(list.GetChild<Button>(3));
            await Frames(1);
            Check("no next chapter at the end", next.Disabled && !previous.Disabled);
            Check("a missing chapter shows a message", panel.ChapterIndex == 3 && content.GetParsedText().Length > 10);
            Check("an internal link", panel.FollowLink("one.md") && panel.ChapterIndex == 0);
            foreach (var bad in new[] { "nothing.md", "file:///C:/Windows/notepad.exe", "calc.exe" }) {
                Check("a refused link stays on the chapter: " + bad, !panel.FollowLink(bad) && panel.ChapterIndex == 0);
            }
            Check("every button is out of the keyboard focus", Descendants<Control>(panel).All(c => c.FocusMode == Control.FocusModeEnum.None || c is not BaseButton && c is not ScrollBar));
        }

        async Task MouseMove(Vector2 to) {
            builder.GetViewport().WarpMouse(to);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to });
            await Frames(2);
        }

        async Task MouseDrag(Vector2 from, Vector2 to) {
            await MouseMove(from);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
            await Frames(2);
            await MouseMove(to);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
            await Frames(2);
        }

        bool GuideInside(GuidePanel panel) {
            var area = builder.Size;
            return panel.Position.X >= -0.01f && panel.Position.Y >= MenuBar.Height - 0.01f
                && panel.Position.X + panel.Size.X <= area.X + 0.01f && panel.Position.Y + panel.Size.Y <= area.Y + 0.01f;
        }

        async Task GuideGeometryTests(GuidePanel panel) {
            bool Near(float a, float b) => Mathf.Abs(a - b) < 1.5f;
            var area = builder.Size;
            Check("the menu bar fills the viewport", Near(area.X, GetViewport().GetVisibleRect().Size.X) && Near(area.Y, GetViewport().GetVisibleRect().Size.Y));
            Check("it opens on the left, under the menu bar", panel.Position.X < 20 && panel.Position.Y >= MenuBar.Height && panel.Position.Y < MenuBar.Height + 20);
            Check("it leaves the log counters visible", panel.Position.Y + panel.Size.Y <= area.Y - 40);
            Check("it starts inside", GuideInside(panel));
            Vector2 TitlePoint() => panel.Position + new Vector2(panel.Size.X * 0.4f, 12);

            //moves
            var start = panel.Position;
            var size = panel.Size;
            await MouseDrag(TitlePoint(), TitlePoint() + new Vector2(40, 30));
            Check("the title moves it", Near(panel.Position.X, start.X + 40) && Near(panel.Position.Y, start.Y + 30) && panel.Size == size);
            await MouseDrag(TitlePoint(), TitlePoint() + new Vector2(-5000, -5000));
            Check("it can't leave the top left corner", Near(panel.Position.X, 0) && Near(panel.Position.Y, MenuBar.Height) && panel.Size == size);
            await MouseDrag(TitlePoint(), TitlePoint() + new Vector2(5000, 5000));
            Check("it can't leave the bottom right corner", GuideInside(panel) && Near(panel.Position.X + panel.Size.X, area.X) && Near(panel.Position.Y + panel.Size.Y, area.Y) && panel.Size == size);
            var body = panel.Position + panel.Size / 2;
            var before = panel.Position;
            await MouseDrag(panel.Position + new Vector2(panel.Size.X * 0.7f, panel.Size.Y - 16), panel.Position + new Vector2(panel.Size.X * 0.7f - 50, panel.Size.Y - 66));
            Check("dragging the text doesn't move it", panel.Position == before);

            //resizes, from the middle of the viewport
            await MouseDrag(TitlePoint(), TitlePoint() - (panel.Position - new Vector2(200, 60)));
            var left = panel.Position.X;
            var top = panel.Position.Y;
            var right = left + panel.Size.X;
            var bottom = top + panel.Size.Y;
            await MouseDrag(new Vector2(right - 2, top + 100), new Vector2(right - 62, top + 100));
            Check("the right edge resizes", Near(panel.Position.X, left) && Near(panel.Position.X + panel.Size.X, right - 60) && Near(panel.Position.Y, top));
            right = panel.Position.X + panel.Size.X;
            await MouseDrag(new Vector2(left + 2, top + 100), new Vector2(left - 28, top + 100));
            Check("the left edge resizes, the other one stays", Near(panel.Position.X, left - 30) && Near(panel.Position.X + panel.Size.X, right));
            left = panel.Position.X;
            await MouseDrag(new Vector2(left + 100, bottom - 2), new Vector2(left + 100, bottom - 42));
            Check("the bottom edge resizes", Near(panel.Position.Y + panel.Size.Y, bottom - 40) && Near(panel.Position.Y, top));
            bottom = panel.Position.Y + panel.Size.Y;
            await MouseDrag(new Vector2(left + 100, top + 2), new Vector2(left + 100, top + 22));
            Check("the top edge resizes, the other one stays", Near(panel.Position.Y, top + 20) && Near(panel.Position.Y + panel.Size.Y, bottom));
            top = panel.Position.Y;
            await MouseDrag(new Vector2(right - 2, bottom - 2), new Vector2(right + 18, bottom + 18));
            Check("a corner resizes both ways", Near(panel.Position.X + panel.Size.X, right + 20) && Near(panel.Position.Y + panel.Size.Y, bottom + 20));
            right = panel.Position.X + panel.Size.X;
            bottom = panel.Position.Y + panel.Size.Y;

            //limits
            await MouseDrag(new Vector2(right - 2, top + 100), new Vector2(-5000, top + 100));
            Check("the minimum width, the left edge stays", Near(panel.Size.X, GuidePanel.MinimumSize.X) && Near(panel.Position.X, left));
            right = panel.Position.X + panel.Size.X;
            await MouseDrag(new Vector2(left + 100, bottom - 2), new Vector2(left + 100, -5000));
            Check("the minimum height, the top edge stays", Near(panel.Size.Y, GuidePanel.MinimumSize.Y) && Near(panel.Position.Y, top));
            bottom = panel.Position.Y + panel.Size.Y;
            await MouseDrag(new Vector2(left + 2, top + 100), new Vector2(5000, top + 100));
            Check("the minimum width from the left edge, the right one stays", Near(panel.Size.X, GuidePanel.MinimumSize.X) && Near(panel.Position.X + panel.Size.X, right));
            await MouseDrag(new Vector2(right - 2, bottom - 2), new Vector2(5000, 5000));
            Check("a corner stops at the viewport", GuideInside(panel) && Near(panel.Position.X + panel.Size.X, area.X) && Near(panel.Position.Y + panel.Size.Y, area.Y));
            await MouseDrag(new Vector2(panel.Position.X + 100, panel.Position.Y + 2), new Vector2(panel.Position.X + 100, -5000));
            Check("the top edge stops at the menu bar", Near(panel.Position.Y, MenuBar.Height) && GuideInside(panel));
            await MouseDrag(new Vector2(panel.Position.X + 2, panel.Position.Y + 100), new Vector2(-5000, panel.Position.Y + 100));
            Check("the left edge stops at the viewport", Near(panel.Position.X, 0) && GuideInside(panel));
            Check("it fills the area", Near(panel.Size.X, area.X) && Near(panel.Size.Y, area.Y - MenuBar.Height));

            //a smaller window keeps it inside, and it stays usable when the window is smaller than its minimum size
            var window = GetWindow();
            var windowSize = window.Size;
            var mode = window.Mode;
            window.Mode = Window.ModeEnum.Windowed;
            window.Size = new Vector2I(800, 500);
            await Frames(5);
            Check("a smaller window: it follows", builder.Size.X <= 801 && GuideInside(panel));
            window.Size = new Vector2I(300, 200);
            await Frames(5);
            Check("a tiny window: it stays inside", GuideInside(panel));
            window.Size = windowSize;
            window.Mode = mode;
            await Frames(5);
            Check("the window back: it stays inside", GuideInside(panel));
            area = builder.Size;
            panel.Size = new Vector2(640, 500);
            panel.Position = new Vector2(100, 60);
            await Frames(1);

            //minimize
            var minimize = panel.GetNode<Button>("Layout/TitleBar/Minimize");
            var fullSize = panel.Size;
            var place = panel.Position;
            Press(minimize);
            await Frames(2);
            Check("minimized: only the title", panel.Minimized && panel.Size.Y < 40 && !panel.GetNode<Control>("Layout/Body").Visible);
            Check("minimized: same place and width", panel.Position == place && Near(panel.Size.X, fullSize.X));
            var sizeBefore = panel.Size;
            await MouseDrag(new Vector2(panel.Position.X + panel.Size.X - 2, panel.Position.Y + 2), new Vector2(panel.Position.X + panel.Size.X + 100, panel.Position.Y + 2));
            Check("minimized: it doesn't resize", panel.Size == sizeBefore);
            await MouseDrag(TitlePoint(), TitlePoint() + new Vector2(5000, 5000));
            Check("minimized: it moves and stays inside", GuideInside(panel) && Near(panel.Position.Y + panel.Size.Y, area.Y));
            Press(minimize);
            await Frames(2);
            Check("restored: its size, and inside even from the bottom", !panel.Minimized && Near(panel.Size.Y, fullSize.Y) && GuideInside(panel) && panel.GetNode<Control>("Layout/Body").Visible);
            panel.Position = place;
        }

        async Task GuideStateTests(GuidePanel panel, Guide first, Guide second) {
            panel.ShowChapter(2);
            var place = panel.Position;
            var size = panel.Size;
            Press(panel.GetNode<Button>("Layout/TitleBar/Close"));
            await Frames(1);
            Check("the X hides it", !panel.Visible);
            Check("the same panel opens again", GuidePanel.Open(builder, first) == panel && panel.Visible);
            await Frames(1);
            Check("it keeps its place, size and chapter", panel.Position == place && panel.Size == size && panel.ChapterIndex == 2);
            Check("another guide uses the same panel", GuidePanel.Open(builder, second) == panel && panel.Guide.id == second.id && panel.ChapterIndex == 0);
            await Frames(1);
            Check("there is one panel", Descendants<GuidePanel>(builder).Count() == 1);
            Check("one button for each chapter of the other guide", panel.GetNode<Control>("Layout/Body/Chapters/List").GetChildCount() == 1);
            Check("a single chapter has no navigation", panel.GetNode<Button>(PanelPage + "Navigation/Previous").Disabled && panel.GetNode<Button>(PanelPage + "Navigation/Next").Disabled);
            Check("each guide keeps its last chapter", GuidePanel.Open(builder, first).ChapterIndex == 2);
            await Frames(1);
            panel.SetMinimized(true);
            await Frames(1);
            GuidePanel.Open(builder, first);
            await Frames(1);
            Check("opening a guide restores a minimized panel", !panel.Minimized);
            Check("it doesn't count as a modal popup", !UIDetection.PopupOpen());
        }

        async Task GuideInputTests(GuidePanel panel) {
            var camera = GetViewport().GetCamera3D();
            panel.ShowChapter(2);
            await Frames(2);
            panel.Position = new Vector2(10, 40);
            await Frames(1);
            var content = panel.GetNode<RichTextLabel>(PanelPage + "ContentBox/Content");
            var over = content.GetGlobalRect().GetCenter();
            await MouseMove(over);
            Check("over the text it is on the UI", UIDetection.OnUI());
            await MouseMove(panel.Position + new Vector2(panel.Size.X * 0.5f, 12));
            Check("over the title it is on the UI", UIDetection.OnUI());
            await MouseMove(panel.Position + new Vector2(1, panel.Size.Y * 0.5f));
            Check("over its edge it is on the UI", UIDetection.OnUI());
            async Task Wheel(Vector2 at) {
                await MouseMove(at);
                for (var i = 0; i < 4; i++) {
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = at, GlobalPosition = at });
                    await Frames(1);
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = false, Position = at, GlobalPosition = at });
                    await Frames(1);
                }
                await Frames(4);
            }
            var cameraPosition = camera.GlobalPosition;
            var scroll = content.GetVScrollBar().Value;
            await Wheel(over);
            Check("the wheel scrolls the text", content.GetVScrollBar().Value > scroll);
            Check("the wheel leaves the camera", camera.GlobalPosition.IsEqualApprox(cameraPosition));
            var outside = new Vector2(panel.Position.X + panel.Size.X + 60, panel.Position.Y + panel.Size.Y * 0.5f);
            await MouseMove(outside);
            Check("outside of it it isn't on the UI", !UIDetection.OnUI());
            var cameraBefore = camera.GlobalPosition;
            await Wheel(outside);
            Check("the wheel outside of it moves the camera", !camera.GlobalPosition.IsEqualApprox(cameraBefore));
        }

        async Task GuideMenuTests() {
            var menu = builder.GuidesMenu();
            var groups = GuideLibrary.Load();
            var own = groups.Find(g => g.folder == null);
            Check("menu: the guides of the program come first", menu.entries[0] == own.guides[0].title);
            var mm2 = groups.Find(g => g.folder == "MidtownMadness2");
            if (mm2 != null) {
                var core = menu.entries.IndexOf(mm2.name);
                Check("menu: a divider before the cores", core > 0 && menu.entries[core - 1] == MenuBar.Divider);
                Check("menu: a submenu for the core", menu.submenus.ContainsKey(core));
                Check("menu: the guides of the core", menu.submenus[core].entries.SequenceEqual(new[] { "Beginner guide", "Advanced guide", "Export guide" }));
                menu.submenus[core].actions[2].Invoke();
                await Frames(1);
                var panel = builder.GetNode<GuidePanel>("GuidePanel");
                Check("menu: an entry opens its guide", panel.Visible && panel.Guide.id == "export");
                panel.Close();
            }
            var bar = (MenuBar)typeof(CityBuilderMenuBar).GetMethod("GetMenuBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(builder, null);
            var index = (int)typeof(CityBuilderMenuBar).GetField("helpMenuIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(builder);
            var popup = bar.GetPopup(index);
            //a real click: the menu is empty until it opens
            var center = popup.GetParent<Control>().GetGlobalRect().GetCenter();
            await MouseMove(center);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = center, GlobalPosition = center });
            await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = center, GlobalPosition = center });
            await Frames(3);
            Check("menu: a click on Help opens it", popup.Visible);
            Check("menu: the Help entries", popup.ItemCount == 4 && popup.GetItemText(1) == StringManager.Get("HELP_GUIDES") && popup.GetItemText(2) == StringManager.Get("HELP_QUICKSTART"));
            Check("menu: Guides is a submenu", popup.GetItemSubmenuNode(1) != null);
            popup.Hide();
            await Frames(2);
        }

        //frames to check the look: they aren't tracked, they go next to the log
        async Task GuideFrames(GuidePanel panel, Guide fixture) {
            async Task Save(string name) {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(guideFixtures, name + ".png"));
            }
            var groups = GuideLibrary.Load();
            GuidePanel.Open(builder, fixture, 0);
            panel.Position = new Vector2(8, 28);
            panel.Size = new Vector2(640, 600);
            await Frames(3);
            await Save("guide_markdown");
            var scroll = panel.GetNode<RichTextLabel>(PanelPage + "ContentBox/Content").GetVScrollBar();
            scroll.Value = scroll.MaxValue;
            await Frames(3);
            await Save("guide_markdown_end");
            var basic = groups.Find(g => g.folder == null)?.guides.Find(g => g.id == "basic");
            if (basic != null) {
                GuidePanel.Open(builder, basic, 1);
                await Frames(3);
                await Save("guide_basic");
                panel.SetMinimized(true);
                await Frames(3);
                await Save("guide_minimized");
                panel.SetMinimized(false);
            }
            var export = groups.Find(g => g.folder == "MidtownMadness2")?.guides.Find(g => g.id == "export");
            if (export != null) {
                var flags = export.chapters.FindIndex(c => c.id == "flags");
                GuidePanel.Open(builder, export, Mathf.Max(flags, 0));
                await Frames(3);
                await Save("guide_export");
            }
            panel.Close();
        }
    }
}

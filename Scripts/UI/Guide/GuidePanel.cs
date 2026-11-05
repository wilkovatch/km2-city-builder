using System.Collections.Generic;
using Godot;
using SM = StringManager;

//The window showing a guide: a title bar (minimize and close), the chapters on the left and the content with the navigation
//on the right. It can be moved, resized and minimized, and always stays inside of the area under the menu bar.
//It doesn't block the editor (the guides are meant to be followed while working on the city), so it isn't one of the editor panels
//of the menu bar: these are closed on reloads, while a guide stays open. Closing only hides it, so it remembers its place
//and the last chapter of each guide until the program quits.
public partial class GuidePanel : Panel {
    const float Grip = 5; //the size of the border that resizes the panel
    const float CornerGrip = 14; //how far from a corner both of its sides are resized
    const float TitleHeight = 24;
    const float ChaptersWidth = 170;
    public static readonly Vector2 MinimumSize = new Vector2(420, 260);
    static readonly Vector2 DefaultSize = new Vector2(640, 720);
    const float DefaultMargin = 8;
    const float DefaultBottomMargin = 100; //leaves visible the log counters and the snapping indicator in the bottom left corner

    [System.Flags]
    enum Edge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

    public Guide Guide { get; private set; }
    public int ChapterIndex { get; private set; }
    public bool Minimized { get; private set; }
    public float RestoredHeight { get; private set; }

    readonly Dictionary<string, int> lastChapters = new Dictionary<string, int>();
    List<GuideGroup> groups = new List<GuideGroup>();
    Label titleLabel, positionLabel;
    Button minimizeButton, closeButton, previousButton, nextButton;
    Control titleBar, body;
    VBoxContainer chapterList;
    readonly List<Button> chapterButtons = new List<Button>();
    RichTextLabel content;
    bool moving;
    Edge resizing;
    Vector2 dragStartMouse, dragStartPosition, dragStartSize;

    //opens the guide in the panel of the parent, created the first time
    public static GuidePanel Open(Control parent, Guide guide, int chapter = -1) {
        var panel = parent.GetNodeOrNull<GuidePanel>("GuidePanel");
        if (panel == null) {
            panel = new GuidePanel { Name = "GuidePanel" };
            parent.AddChild(panel);
            panel.Build();
        }
        panel.Show(guide, chapter);
        return panel;
    }

    void Show(Guide guide, int chapter) {
        groups = GuideLibrary.Load();
        Visible = true;
        //above the editor panels, the alerts are created later
        GetParent().MoveChild(this, -1);
        SetMinimized(false);
        SetGuide(guide);
        if (chapter < 0) lastChapters.TryGetValue(guide.folder, out chapter);
        ShowChapter(chapter);
    }

    void Build() {
        SetAnchorsPreset(LayoutPreset.TopLeft);
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        AddThemeStyleboxOverride("panel", UILayout.Box(new Color(0.9f, 0.9f, 0.9f)));

        var layout = new VBoxContainer { Name = "Layout" };
        UILayout.Stretch(layout);
        layout.OffsetLeft = Grip;
        layout.OffsetRight = -Grip;
        layout.OffsetTop = Grip;
        layout.OffsetBottom = -Grip;
        layout.AddThemeConstantOverride("separation", 4);
        layout.MouseFilter = MouseFilterEnum.Pass;
        AddChild(layout);

        //the events of the title go on to the panel, which handles the move
        var title = new HBoxContainer { Name = "TitleBar", CustomMinimumSize = new Vector2(0, TitleHeight), MouseFilter = MouseFilterEnum.Pass };
        title.AddThemeConstantOverride("separation", 2);
        titleBar = title;
        layout.AddChild(title);
        titleLabel = UILayout.MakeLabel("", HorizontalAlignment.Left);
        titleLabel.AddThemeFontOverride("font", UILayout.BoldFont());
        titleLabel.Name = "Title";
        titleLabel.ClipText = true;
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleLabel.MouseFilter = MouseFilterEnum.Pass;
        title.AddChild(titleLabel);
        minimizeButton = TitleButton("Minimize", "GUIDE_MINIMIZE", MinimizeIcon(), () => SetMinimized(!Minimized));
        title.AddChild(minimizeButton);
        closeButton = TitleButton("Close", "GUIDE_CLOSE", CloseIcon(), Close);
        title.AddChild(closeButton);

        var row = new HBoxContainer { Name = "Body", SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", 6);
        body = row;
        layout.AddChild(row);

        var chapterScroll = new ScrollContainer {
            Name = "Chapters", CustomMinimumSize = new Vector2(ChaptersWidth, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FocusMode = FocusModeEnum.None
        };
        UILayout.StyleScrollBar(chapterScroll.GetVScrollBar());
        chapterScroll.GetVScrollBar().FocusMode = FocusModeEnum.None;
        row.AddChild(chapterScroll);
        chapterList = new VBoxContainer { Name = "List", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        chapterList.AddThemeConstantOverride("separation", 2);
        chapterScroll.AddChild(chapterList);

        var page = new VBoxContainer { Name = "Page", SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        page.AddThemeConstantOverride("separation", 4);
        row.AddChild(page);
        var contentBox = new PanelContainer { Name = "ContentBox", SizeFlagsVertical = SizeFlags.ExpandFill };
        contentBox.AddThemeStyleboxOverride("panel", UILayout.Box(Colors.White, 10, 6, 8, 8));
        page.AddChild(contentBox);
        content = new RichTextLabel {
            Name = "Content", ScrollActive = true, SelectionEnabled = false, FocusMode = FocusModeEnum.None,
            TabSize = 2, ContextMenuEnabled = false
        };
        GuideMarkdown.StyleLabel(content);
        UILayout.StyleScrollBar(content.GetVScrollBar());
        content.GetVScrollBar().FocusMode = FocusModeEnum.None;
        content.MetaClicked += meta => FollowLink(meta.AsString());
        contentBox.AddChild(content);

        var navigation = new HBoxContainer { Name = "Navigation", MouseFilter = MouseFilterEnum.Pass };
        page.AddChild(navigation);
        previousButton = NavigationButton("Previous", "GUIDE_PREVIOUS", () => ShowChapter(ChapterIndex - 1));
        navigation.AddChild(previousButton);
        positionLabel = UILayout.MakeLabel("");
        positionLabel.Name = "Position";
        positionLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        navigation.AddChild(positionLabel);
        nextButton = NavigationButton("Next", "GUIDE_NEXT", () => ShowChapter(ChapterIndex + 1));
        navigation.AddChild(nextButton);

        var parent = GetParent<Control>();
        parent.Resized += () => {
            if (IsInstanceValid(this)) Clamp();
        };
        var area = Area(parent);
        Size = new Vector2(Mathf.Min(DefaultSize.X, area.Size.X - 2 * DefaultMargin), Mathf.Min(DefaultSize.Y, area.Size.Y - DefaultMargin - DefaultBottomMargin));
        Position = area.Position + new Vector2(DefaultMargin, DefaultMargin);
        Clamp();
    }

    Button TitleButton(string name, string tooltipKey, Texture2D icon, System.Action action) {
        var button = new Button {
            Name = name, Icon = icon, TooltipText = SM.Get(tooltipKey), FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(TitleHeight + 2, TitleHeight), IconAlignment = HorizontalAlignment.Center
        };
        UILayout.StyleButton(button);
        button.Pressed += () => action();
        return button;
    }

    Button NavigationButton(string name, string textKey, System.Action action) {
        var button = new Button { Name = name, Text = SM.Get(textKey), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(90, 26) };
        UILayout.StyleButton(button);
        button.Pressed += () => action();
        return button;
    }

    void SetGuide(Guide guide) {
        Guide = guide;
        titleLabel.Text = guide.title;
        foreach (var button in chapterButtons) {
            chapterList.RemoveChild(button);
            button.QueueFree();
        }
        chapterButtons.Clear();
        for (var i = 0; i < guide.chapters.Count; i++) {
            var index = i;
            var button = new Button {
                Name = "Chapter" + i, Text = (i + 1) + ". " + guide.chapters[i].title, Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            button.Pressed += () => ShowChapter(index);
            chapterList.AddChild(button);
            chapterButtons.Add(button);
        }
    }

    //shows a chapter of the current guide (limited to the existing ones)
    public void ShowChapter(int index) {
        if (Guide == null) return;
        ChapterIndex = Mathf.Clamp(index, 0, Guide.chapters.Count - 1);
        lastChapters[Guide.folder] = ChapterIndex;
        var chapter = Guide.chapters[ChapterIndex];
        var markdown = GuideLibrary.ReadChapter(chapter);
        var blocks = GuideMarkdown.Parse(markdown ?? SM.Get("GUIDE_CHAPTER_NOT_FOUND"));
        GuideMarkdown.Render(content, blocks, LoadImage);
        content.ScrollToLine(0);
        positionLabel.Text = SM.Get("GUIDE_POSITION").Replace("$CURRENT", (ChapterIndex + 1).ToString()).Replace("$TOTAL", Guide.chapters.Count.ToString());
        previousButton.Disabled = ChapterIndex == 0;
        nextButton.Disabled = ChapterIndex == Guide.chapters.Count - 1;
        for (var i = 0; i < chapterButtons.Count; i++) StyleChapterButton(chapterButtons[i], i == ChapterIndex);
    }

    static void StyleChapterButton(Button button, bool current) {
        UILayout.StyleButton(button, 6, 6);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" }) {
            var box = button.GetThemeStylebox(state) as StyleBoxFlat;
            if (box == null) continue;
            box.ContentMarginTop = 3;
            box.ContentMarginBottom = 3;
            if (current) box.BgColor = UILayout.ListSelectedColor;
        }
    }

    Texture2D LoadImage(string src) {
        var path = GuideLibrary.ResolveImage(Guide, src);
        if (path == null) {
            GameEngine.LogWarning("Guide image not found: " + src + " (" + Guide.folder + ")");
            return null;
        }
        var image = Image.LoadFromFile(path);
        return image == null ? null : ImageTexture.CreateFromImage(image);
    }

    //what a click on a link does: web links open in the browser and the links to chapters open them here, anything else is refused
    public bool FollowLink(string href) {
        if (GuideLibrary.IsWebLink(href)) {
            OS.ShellOpen(href);
            return true;
        }
        if (Guide != null && GuideLibrary.ResolveChapter(groups, Guide, href, out var guide, out var chapter)) {
            if (guide.folder != Guide.folder) SetGuide(guide);
            ShowChapter(chapter);
            return true;
        }
        GameEngine.LogWarning("Guide link not followed: " + href);
        return false;
    }

    public void Close() {
        Visible = false;
    }

    public void SetMinimized(bool minimized) {
        if (Minimized == minimized) {
            UpdateMinimizeButton();
            return;
        }
        if (minimized) RestoredHeight = Size.Y;
        Minimized = minimized;
        body.Visible = !minimized;
        Size = new Vector2(Size.X, minimized ? MinimizedHeight : RestoredHeight);
        UpdateMinimizeButton();
        Clamp();
    }

    void UpdateMinimizeButton() {
        if (minimizeButton == null) return;
        minimizeButton.Icon = Minimized ? RestoreIcon() : MinimizeIcon();
        minimizeButton.TooltipText = SM.Get(Minimized ? "GUIDE_RESTORE" : "GUIDE_MINIMIZE");
    }

    static float MinimizedHeight => TitleHeight + 2 * Grip;

    //the area the panel has to stay inside of, in the coordinates of its parent
    static Rect2 Area(Control parent) {
        return new Rect2(0, MenuBar.Height, parent.Size.X, Mathf.Max(parent.Size.Y - MenuBar.Height, 0));
    }

    //Keeps the panel in the area, respecting its minimum size if the area is big enough
    void Clamp() {
        var parent = GetParent<Control>();
        if (parent == null) return;
        var area = Area(parent);
        var minimumHeight = Minimized ? MinimizedHeight : MinimumSize.Y;
        var size = new Vector2(
            Mathf.Min(Mathf.Max(Size.X, MinimumSize.X), area.Size.X),
            Minimized ? MinimizedHeight : Mathf.Min(Mathf.Max(Size.Y, minimumHeight), area.Size.Y));
        var position = new Vector2(
            Mathf.Clamp(Position.X, area.Position.X, area.End.X - size.X),
            Mathf.Clamp(Position.Y, area.Position.Y, Mathf.Max(area.End.Y - size.Y, area.Position.Y)));
        Size = size;
        Position = position;
    }

    Edge EdgesAt(Vector2 at) {
        if (Minimized) return Edge.None;
        var res = Edge.None;
        var nearLeft = at.X < Grip;
        var nearRight = at.X >= Size.X - Grip;
        var nearTop = at.Y < Grip;
        var nearBottom = at.Y >= Size.Y - Grip;
        if (!nearLeft && !nearRight && !nearTop && !nearBottom) return res;
        if (nearLeft || (nearTop || nearBottom) && at.X < CornerGrip) res |= Edge.Left;
        if (nearRight || (nearTop || nearBottom) && at.X >= Size.X - CornerGrip) res |= Edge.Right;
        if (nearTop || (nearLeft || nearRight) && at.Y < CornerGrip) res |= Edge.Top;
        if (nearBottom || (nearLeft || nearRight) && at.Y >= Size.Y - CornerGrip) res |= Edge.Bottom;
        return res;
    }

    public override int _GetCursorShape(Vector2 atPosition) {
        return (int)CursorFor(resizing != Edge.None ? resizing : EdgesAt(atPosition));
    }

    static CursorShape CursorFor(Edge edges) {
        if (edges == (Edge.Left | Edge.Top) || edges == (Edge.Right | Edge.Bottom)) return CursorShape.Fdiagsize;
        if (edges == (Edge.Right | Edge.Top) || edges == (Edge.Left | Edge.Bottom)) return CursorShape.Bdiagsize;
        if ((edges & (Edge.Left | Edge.Right)) != 0) return CursorShape.Hsize;
        if ((edges & (Edge.Top | Edge.Bottom)) != 0) return CursorShape.Vsize;
        return CursorShape.Arrow;
    }

    public override void _GuiInput(InputEvent @event) {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left) {
            if (button.Pressed) {
                var edges = EdgesAt(button.Position);
                var onTitle = button.Position.Y < Grip + TitleHeight;
                if (edges == Edge.None && !onTitle) return;
                resizing = edges;
                moving = edges == Edge.None;
                dragStartMouse = GetGlobalMousePosition();
                dragStartPosition = Position;
                dragStartSize = Size;
            } else {
                moving = false;
                resizing = Edge.None;
            }
            AcceptEvent();
        } else if (@event is InputEventMouseMotion) {
            if (!moving && resizing == Edge.None) return;
            Drag(GetGlobalMousePosition() - dragStartMouse);
            AcceptEvent();
        }
    }

    void Drag(Vector2 delta) {
        if (moving) {
            Position = dragStartPosition + delta;
            Clamp();
            return;
        }
        //the side that isn't being dragged stays where it is, also when the size or the area limit the drag
        var area = Area(GetParent<Control>());
        var left = dragStartPosition.X;
        var top = dragStartPosition.Y;
        var right = left + dragStartSize.X;
        var bottom = top + dragStartSize.Y;
        if ((resizing & Edge.Left) != 0) left = Mathf.Clamp(left + delta.X, area.Position.X, right - Mathf.Min(MinimumSize.X, area.Size.X));
        if ((resizing & Edge.Right) != 0) right = Mathf.Clamp(right + delta.X, left + Mathf.Min(MinimumSize.X, area.Size.X), area.End.X);
        if ((resizing & Edge.Top) != 0) top = Mathf.Clamp(top + delta.Y, area.Position.Y, bottom - Mathf.Min(MinimumSize.Y, area.Size.Y));
        if ((resizing & Edge.Bottom) != 0) bottom = Mathf.Clamp(bottom + delta.Y, top + Mathf.Min(MinimumSize.Y, area.Size.Y), area.End.Y);
        Position = new Vector2(left, top);
        Size = new Vector2(right - left, bottom - top);
    }

    //Icons

    const int IconSize = 12;
    static ImageTexture minimizeIcon, restoreIcon, closeIcon;

    static ImageTexture MakeIcon(System.Func<int, int, bool> filled) {
        var image = Image.CreateEmpty(IconSize, IconSize, false, Image.Format.Rgba8);
        for (var y = 0; y < IconSize; y++) {
            for (var x = 0; x < IconSize; x++) {
                image.SetPixel(x, y, filled(x, y) ? UILayout.TextColor : new Color(UILayout.TextColor, 0));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    static ImageTexture MinimizeIcon() {
        return minimizeIcon ??= MakeIcon((x, y) => x >= 1 && x <= 10 && y >= 9 && y <= 10);
    }

    static ImageTexture RestoreIcon() {
        return restoreIcon ??= MakeIcon((x, y) => x >= 1 && x <= 10 && y >= 1 && y <= 10 && (x <= 1 || x >= 10 || y <= 2 || y >= 10));
    }

    static ImageTexture CloseIcon() {
        return closeIcon ??= MakeIcon((x, y) => x >= 1 && x <= 10 && y >= 1 && y <= 10 && (x == y || x == y + 1 || 11 - x == y || 11 - x == y + 1));
    }
}

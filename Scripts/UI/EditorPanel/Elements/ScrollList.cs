using System.Collections;
using System.Collections.Generic;
using Godot;
using GameObject = Godot.Control;
using Color = Godot.Color;

namespace EditorPanelElements {
    public class ScrollList : EditorPanelElement {
        System.Action<int> action;
        ScrollContainer scroll;
        Control content;
        bool withImages;
        List<string> strings = new List<string>();
        List<string> images = new List<string>();
        List<Color> colors = new List<Color>();
        // Only a fixed pool of rows exists: they are reassigned to the visible
        // items while scrolling (item i is always shown by row i % rows.Count, placed at y = i * rowHeight).
        List<(GameObject row, int rowID)> rows = new List<(GameObject, int)>();
        List<Godot.Label> rowLabels = new List<Godot.Label>();
        Dictionary<int, int> curRowsValues = new Dictionary<int, int>();
        static readonly Color pressedCol = new Color(145 / 255.0f, 201 / 255.0f, 247 / 255.0f);
        static StyleBoxFlat normalStyle, selectedStyle, hoverStyle, pressedStyle;
        static readonly StyleBoxEmpty focusStyle = new StyleBoxEmpty();
        int selected = -1;
        float height;
        float rowHeight = 25.0f;
        int paddingRows = 4;
        int lastFirstIndex = -1;
        ScrollListComponent scrl;
        string emptyLabel = "EMPTY";

        protected override string TemplateName() {
            return "ScrollList";
        }

        void Setup(System.Action<int> action, bool withImage, string emptyLabel) {
            this.action = action;
            this.withImages = withImage;
            if (!string.IsNullOrEmpty(emptyLabel)) this.emptyLabel = emptyLabel;
            height = 225;
            // Scroll view: white sliced background with a 20px vertical scrollbar, filling the area below the title.
            scroll = new ScrollContainer { Name = "ScrollContainer", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            UILayout.Stretch(scroll);
            // The rows are inset so so they stay inside the 1px border and 3px corners.
            scroll.AddThemeStyleboxOverride("panel", UILayout.Box(Colors.White, 2, 2, 2, 2));
            UILayout.StyleScrollBar(scroll.GetVScrollBar(), 20);
            content = new Control { Name = "Content", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
            scroll.AddChild(content);
            fieldControl = scroll;
            body.AddChild(scroll);
            scrl = new ScrollListComponent { Name = "ScrollListComponent", list = this };
            obj.AddChild(scrl);
            for (float p = 0; p < height + 2 * paddingRows * rowHeight; p += rowHeight) {
                CreateRow();
            }
            scroll.GetVScrollBar().ValueChanged += _ => UpdateRows();
        }

        public ScrollList(string title, List<string> items, System.Action<int> action, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null, string emptyLabel = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            Setup(action, false, emptyLabel);
            SetItems(items);
        }

        public ScrollList(string title, List<string> items, List<string> images, System.Action<int> action, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null, string emptyLabel = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            Setup(action, true, emptyLabel);
            SetItems(items, images);
        }

        public void ManualScroll(bool next) {
            if (selected == -1) return;
            bool changed = false;
            if (next) {
                if (selected < strings.Count - 1) {
                    SelectItemManual(selected + 1);
                    changed = true;
                }
            } else {
                if (selected > 0) {
                    SelectItemManual(selected - 1);
                    changed = true;
                }
            }
            if (changed) {
                var viewHeight = ViewHeight();
                var contTopY = scroll.ScrollVertical;
                var contBottomY = contTopY + viewHeight;
                var itemTopY = selected * rowHeight;
                var itemBottomY = itemTopY + rowHeight;
                if (itemTopY < contTopY) {
                    scroll.ScrollVertical = (int)itemTopY;
                } else if (itemBottomY > contBottomY) {
                    scroll.ScrollVertical = (int)Mathf.Ceil(itemBottomY - viewHeight);
                }
                UpdateRows();
            }
        }

        void CreateRow() {
            int rowID = rows.Count;
            // Item and image item: 25px rows, white background (selected rows tinted),
            // text MiddleLeft with best fit 8-14, inset 5px (30px when an image is shown).
            var rowButton = new Godot.Button { Name = "Row" + rowID, Visible = false, FocusMode = Control.FocusModeEnum.None,
                AnchorLeft = 0, AnchorRight = 1, OffsetLeft = 0, OffsetRight = 0 };
            ApplyRowStyle(rowButton, false);
            if (withImages) {
                var frame = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
                UILayout.SetRect(frame, 0, 0.5f, 0, 0.5f, 5, 0, 22, 22, 0, 0.5f);
                rowButton.AddChild(frame);
                var checker = new TextureRect { Texture = UILayout.Checkerboard(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
                UILayout.SetRect(checker, 0, 0.5f, 0, 0.5f, 6, 0, 20, 20, 0, 0.5f);
                rowButton.AddChild(checker);
                // Loaded textures are stored bottom row first (see GameEngine.Texture.CreateFromFile), flipped to be shown upright.
                var preview = new TextureRect { Name = "Image", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore, FlipV = true };
                UILayout.SetRect(preview, 0, 0.5f, 0, 0.5f, 6, 0, 20, 20, 0, 0.5f);
                rowButton.AddChild(preview);
                scrl.SetupSprite(rowID, preview);
            }
            var rowLabel = UILayout.MakeLabel("", HorizontalAlignment.Left);
            rowLabel.Name = "Text";
            rowLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            UILayout.SetRect(rowLabel, 0, 0, 1, 1, withImages ? 15 : 2.5f, 0, withImages ? -30 : -5, 0);
            rowButton.AddChild(rowLabel);
            TextFit.Apply(rowLabel, TextFit.DefaultFontSize, 8);
            rowButton.Pressed += () => SelectItem(rowID);
            content.AddChild(rowButton);
            rows.Add((rowButton, rowID));
            rowLabels.Add(rowLabel);
            curRowsValues.Add(rowID, -1);
        }

        void SetRow(int row, int index) {
            var rowButton = (Godot.Button)rows[row].row;
            if (index < 0 || index > strings.Count - 1) {
                if (rowButton.Visible) rowButton.Visible = false;
                if (curRowsValues[row] != -1 && withImages) scrl.ClearSprite(row);
                curRowsValues[row] = -1;
                return;
            }
            if (curRowsValues[row] == index) return;
            rowButton.OffsetTop = index * rowHeight;
            rowButton.OffsetBottom = (index + 1) * rowHeight;
            var txt = rowLabels[row];
            bool empty = string.IsNullOrEmpty(strings[index]);
            txt.Text = empty ? StringManager.Get(emptyLabel) : strings[index];
            var textColor = colors.Count > index ? colors[index] : (empty ? new Color(0.5f, 0.5f, 0.5f) : UILayout.TextColor);
            txt.AddThemeColorOverride("font_color", textColor);
            TextFit.Refit(txt);
            ApplyRowStyle(rowButton, selected == index);
            if (withImages) {
                scrl.EnqueueNewSprite(row, images.Count > index ? images[index] : null);
            }
            curRowsValues[row] = index;
            if (!rowButton.Visible) rowButton.Visible = true;
        }

        public void Deselect() {
            if (selected != -1) {
                SetRowHighlight(selected, false);
                selected = -1;
            }
        }

        void SelectItemManual(int index) {
            Deselect();
            selected = index;
            SetRowHighlight(index, true);
            if (actionEnabled) action?.Invoke(index);
        }

        public override void SetValue(object value) {
            if (value is int) {
                SelectItemManual((int)value);
            }
        }

        void SelectItem(int rowID) {
            if (rowID == -1) return;
            var index = curRowsValues[rowID];
            if (index == -1) return;
            Deselect();
            selected = index;
            SetRowHighlight(selected, true);
            if (actionEnabled) action?.Invoke(index);
        }

        public void SetItems(List<string> newItems, List<string> newImages = null, List<Color> newColors = null) {
            strings.Clear();
            images.Clear();
            colors.Clear();
            strings.AddRange(newItems);
            if (newImages != null) images.AddRange(newImages);
            if (newColors != null) colors.AddRange(newColors);
            content.CustomMinimumSize = new Vector2(0, strings.Count * rowHeight);
            for (int i = 0; i < rows.Count; i++) curRowsValues[i] = -1;
            lastFirstIndex = -1;
            UpdateRows();
        }

        public void UpdateRows() {
            if (rows.Count == 0) return;
            // Content may shrink before the ScrollContainer clamps its offset: clamp here too.
            var maxScroll = Mathf.Max(0, strings.Count * rowHeight - ViewHeight());
            var pos = Mathf.Clamp(scroll.ScrollVertical, 0, maxScroll);
            int firstIndex = (int)(pos / rowHeight) - paddingRows;
            if (firstIndex < 0) firstIndex = 0;
            else if (firstIndex + rows.Count > strings.Count) firstIndex = Mathf.Max(0, strings.Count - rows.Count);
            if (firstIndex == lastFirstIndex) return;
            for (int i = firstIndex; i < firstIndex + rows.Count; i++) {
                SetRow(i % rows.Count, i);
            }
            lastFirstIndex = firstIndex;
        }

        // Height of the scrolled area, excluding the frame's content margins.
        float ViewHeight() {
            if (scroll.Size.Y <= 0) return height;
            var frame = scroll.GetThemeStylebox("panel");
            return scroll.Size.Y - (frame != null ? frame.GetMargin(Side.Top) + frame.GetMargin(Side.Bottom) : 0);
        }

        static void ApplyRowStyle(Godot.Button rowButton, bool isSelected) {
            if (normalStyle == null) {
                normalStyle = MakeRowStyle(Colors.White);
                selectedStyle = MakeRowStyle(pressedCol);
                hoverStyle = MakeRowStyle(UILayout.HighlightedColor);
                pressedStyle = MakeRowStyle(UILayout.PressedColor);
            }
            var normal = isSelected ? selectedStyle : normalStyle;
            rowButton.AddThemeStyleboxOverride("normal", normal);
            rowButton.AddThemeStyleboxOverride("hover", isSelected ? selectedStyle : hoverStyle);
            rowButton.AddThemeStyleboxOverride("pressed", pressedStyle);
            rowButton.AddThemeStyleboxOverride("hover_pressed", pressedStyle);
            rowButton.AddThemeStyleboxOverride("disabled", normal);
            rowButton.AddThemeStyleboxOverride("focus", focusStyle);
        }

        void SetRowHighlight(int index, bool isSelected) {
            if (index < 0 || rows.Count == 0) return;
            var rowID = index % rows.Count;
            if (curRowsValues[rowID] == index && rows[rowID].row is Godot.Button rowButton && GodotObject.IsInstanceValid(rowButton)) {
                ApplyRowStyle(rowButton, isSelected);
            }
        }

        static StyleBoxFlat MakeRowStyle(Color fill) => new() {
            BgColor = fill,
            ContentMarginLeft = 0,
            ContentMarginRight = 0,
            ContentMarginTop = 0,
            ContentMarginBottom = 0
        };
    }
}

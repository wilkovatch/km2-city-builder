using Godot;

namespace IngameDebugConsole {
    // A UI element to show information about a debug entry
    public partial class DebugLogItem : Control {
        public const float ItemHeight = 35f;
        const int LogFontSize = 15;
        // Copy button (36px, 2px above the bottom) plus 2px between it and the text
        const float CopyLogButtonHeight = 2f + 36f + 2f;
        // LogText rect: 35px from the left, 5px from the right, 1px from the top and the bottom
        const float LogTextHorizontalInset = 40f;
        const float LogTextVerticalInset = 2f;

        Panel background;
        TextureRect logTypeImage;
        Label logText;
        Panel logCountParent;
        Label logCountText;
        Button copyLogButton;

        public DebugLogEntry Entry { get; private set; }
        public int Index { get; private set; }
        public bool Expanded { get; private set; }

        public DebugLogItem() {
            Name = "DebugLogItem";
            MouseFilter = MouseFilterEnum.Ignore; // clicks are handled by the list view

            background = DebugConsoleUI.Image(DebugConsoleUI.SlicedBackground2, Colors.White, "Background");
            UILayout.Stretch(background);
            AddChild(background);

            logTypeImage = DebugConsoleUI.Icon(DebugLogManager.LogTypeSprite(DebugLogType.Info), Colors.White, false);
            UILayout.SetRect(logTypeImage, 0, 0.5f, 0, 0.5f, 18, 0, 25, 25);
            AddChild(logTypeImage);

            logText = DebugConsoleUI.Text("", LogFontSize, HorizontalAlignment.Left);
            logText.Name = "LogText";
            AddChild(logText);

            logCountParent = DebugConsoleUI.Image(DebugConsoleUI.SlicedBackground3, new Color(0.42647058f, 0.42647058f, 0.42647058f), "LogCount");
            UILayout.SetRect(logCountParent, 1, 0.5f, 1, 0.5f, -28, 0, 38, 28);
            logCountParent.Visible = false;
            AddChild(logCountParent);

            logCountText = DebugConsoleUI.Text("1", 16, HorizontalAlignment.Center);
            logCountText.Name = "LogCountText";
            UILayout.SetRect(logCountText, 0, 0, 1, 1, 0, 0, -2, 0);
            logCountParent.AddChild(logCountText);
            TextFit.Apply(logCountText, 16, 10);

            copyLogButton = DebugConsoleUI.Button("CopyLogButton", DebugConsoleUI.SlicedBackground, new Color(0.42647058f, 0.42647058f, 0.42647058f));
            copyLogButton.Text = "Copy";
            UILayout.SetRect(copyLogButton, 0, 0, 1, 0, 0, 2, -70, 36, 0.5f, 0);
            copyLogButton.Visible = false;
            copyLogButton.Pressed += CopyLog;
            AddChild(copyLogButton);

            SetExpandedLayout(false);
        }

        public void SetContent(DebugLogEntry logEntry, int entryIndex, bool isExpanded) {
            Entry = logEntry;
            Index = entryIndex;
            Expanded = isExpanded;

            copyLogButton.Visible = isExpanded;
            SetExpandedLayout(isExpanded);
            logText.Text = isExpanded ? logEntry.ToString() : logEntry.logString;
            logTypeImage.Texture = DebugConsoleUI.Texture(DebugLogManager.LogTypeSprite(logEntry.logType));
        }

        void SetExpandedLayout(bool isExpanded) {
            if (isExpanded) {
                // Wrapped text above the copy button
                logText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                logText.MaxLinesVisible = -1;
                UILayout.SetRect(logText, 0, 0, 1, 1, 15, CopyLogButtonHeight * 0.5f, -LogTextHorizontalInset, -LogTextVerticalInset - CopyLogButtonHeight);
            }
            else {
                // A single line, cut at the right edge (horizontal overflow inside the masked viewport)
                logText.AutowrapMode = TextServer.AutowrapMode.Off;
                logText.MaxLinesVisible = 1;
                UILayout.SetRect(logText, 0, 0, 1, 1, 15, 0, -LogTextHorizontalInset, -LogTextVerticalInset);
            }
        }

        public void SetColor(Color color) {
            DebugConsoleUI.SetImageColor(background, color);
        }

        // Show the collapsed count of the debug entry
        public void ShowCount() {
            logCountText.Text = Entry.count.ToString();
            logCountParent.Visible = true;
            TextFit.Refit(logCountText);
        }

        // Hide the collapsed count of the debug entry
        public void HideCount() {
            logCountParent.Visible = false;
        }

        public void CopyLog() {
            if (Entry == null) return;
            var log = Entry.ToString();
            if (!string.IsNullOrEmpty(log)) DisplayServer.ClipboardSet(log);
        }

        // Height of the item when the entry is expanded, for the given item width. (whose text
        // rect was 2px shorter than the measured text, hiding the last line), the text always fits.
        public float CalculateExpandedHeight(DebugLogEntry logEntry, float itemWidth) {
            var font = logText.GetThemeFont("font");
            var width = Mathf.Max(1f, itemWidth - LogTextHorizontalInset);
            var textHeight = font.GetMultilineStringSize(logEntry.ToString(), HorizontalAlignment.Left, width, LogFontSize, -1,
                TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive).Y;
            return Mathf.Max(ItemHeight, Mathf.Ceil(textHeight) + LogTextVerticalInset + CopyLogButtonHeight);
        }
    }
}

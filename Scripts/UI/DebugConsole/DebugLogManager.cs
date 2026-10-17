using System.Collections.Generic;
using Godot;

// Port of yasirkula's In-game Debug Console (MIT, see res://Textures/DebugConsole/LICENSE.txt), the in-app log
// viewer. Only the log viewer is ported, the command console is not.
//
// Receives debug entries and custom events (e.g. Clear, Collapse, Filter by Type)
// and notifies the recycled list view of changes to the list of debug entries
//
// - Vocabulary -
// Debug/Log entry: a log message (GD.Print, GD.PushWarning, GD.PushError, engine errors...) intercepted by this manager
// Debug/Log item: a visual representation of a debug entry
//
// There can be a lot of debug entries in the system but there will only be a handful of log items
// to show their properties on screen (these log items are recycled as the list is scrolled)
namespace IngameDebugConsole {
    // Registered as an autoload (a singleton surviving scene changes), so that it receives the
    // logs from the very start
    public partial class DebugLogManager : CanvasLayer {
        public static DebugLogManager Instance { get; private set; }

        // Configuration of the console
        const float minimumHeight = 200f;
        const float topSearchbarMinWidth = 360f;
        const int queuedLogLimit = 256;
        const int maxLogLength = 10000;
        const float resizeButtonHeight = 36f;
        const float barHeight = 36f;
        const float resizeDragThreshold = 10f;

        static readonly Color collapseButtonNormalColor = new Color(0.31617647f, 0.31617647f, 0.31617647f);
        static readonly Color collapseButtonSelectedColor = new Color(0.44117647f, 0.4346886f, 0.4346886f);
        static readonly Color filterButtonsNormalColor = new Color(0.31617647f, 0.31617647f, 0.31617647f);
        static readonly Color filterButtonsSelectedColor = new Color(0.44117647f, 0.4346886f, 0.4346886f);

        public static string LogTypeSprite(DebugLogType logType) {
            return logType switch {
                DebugLogType.Info => "IconInfo",
                DebugLogType.Warning => "IconWarning",
                _ => "IconError"
            };
        }

        DebugLogLogger logger;

        // UI
        Control canvas;
        Panel logWindow;
        Button clearButton, collapseButton, hideButton;
        Button filterInfoButton, filterWarningButton, filterErrorButton;
        Label infoEntryCountText, warningEntryCountText, errorEntryCountText;
        Control searchbar;
        LineEdit searchField;
        Panel searchbarSlotTop, searchbarSlotBottom;
        Panel logItemsScrollRect;
        Panel resizeButton;
        Button snapToBottomButton;
        DebugLogRecycledListView recycledListView;
        DebugLogScrollbar logItemsScrollbar;
        DebugLogPopup popupManager;

        bool isLogWindowVisible = true;
        public bool IsLogWindowVisible => isLogWindowVisible;

        bool screenDimensionsChanged = true;
        float logWindowPreviousWidth;

        // Number of entries filtered by their types
        int infoEntryCount = 0, warningEntryCount = 0, errorEntryCount = 0;
        bool entryCountTextsDirty;

        // Number of new entries received this frame
        int newInfoEntryCount = 0, newWarningEntryCount = 0, newErrorEntryCount = 0;

        // Filters to apply to the list of debug entries to show
        bool isCollapseOn = false;
        DebugLogFilter logFilter = DebugLogFilter.All;

        // Search filter
        string searchTerm;
        bool isInSearchMode;

        // If the last log item is completely visible (scrollbar is at the bottom),
        // scrollbar will remain at the bottom when new debug entries are received
        bool snapToBottom = true;

        // List of unique debug entries (duplicates of entries are not kept)
        readonly List<DebugLogEntry> collapsedLogEntries = new List<DebugLogEntry>(128);

        // Dictionary to quickly find if a log already exists in collapsedLogEntries
        readonly Dictionary<DebugLogEntry, int> collapsedLogEntriesMap = new Dictionary<DebugLogEntry, int>(128);

        // The order the collapsedLogEntries are received
        // (duplicate entries have the same index (value))
        readonly List<int> uncollapsedLogEntriesIndices = new List<int>();

        // Filtered list of debug entries to show
        readonly List<int> indicesOfListEntriesToShow = new List<int>();

        // Whether or not logs list view should be updated this frame
        bool shouldUpdateRecycledListView = false;

        // Logs that should be registered in the process loop (they can be received from any thread)
        readonly Queue<QueuedDebugLogEntry> queuedLogEntries = new Queue<QueuedDebugLogEntry>(256);
        readonly object logEntriesLock = new object();

        // Resize button drag
        bool resizePointerDown, resizing;
        Vector2 resizePointerDownPosition;

        public override void _EnterTree() {
            // Only one instance of debug console is allowed
            if (Instance != null && Instance != this) {
                QueueFree();
                return;
            }
            Instance = this;

            // canvas sorting order 1001, above all the other UI
            Layer = 100;
            ProcessMode = ProcessModeEnum.Always;

            logger = new DebugLogLogger {
                manager = this,
                projectPath = ProjectSettings.GlobalizePath("res://").Replace('\\', '/')
            };
            OS.AddLogger(logger);
            if (EngineDebugger.IsActive()) System.AppDomain.CurrentDomain.FirstChanceException += logger.OnFirstChanceException;
        }

        public override void _ExitTree() {
            if (logger != null) {
                System.AppDomain.CurrentDomain.FirstChanceException -= logger.OnFirstChanceException;
                OS.RemoveLogger(logger);
                logger.manager = null;
                logger = null;
            }
            if (Instance == this) Instance = null;
        }

        public override void _Ready() {
            if (Instance != this) return;

            BuildUI();
            recycledListView.Initialize(this, logItemsScrollbar, collapsedLogEntries, indicesOfListEntriesToShow);
            popupManager.Initialize(this, canvas);
            recycledListView.UpdateItemsInTheList(true);

            // Start: the popup is enabled and the console starts in popup mode
            HideLogWindow();
        }

        void BuildUI() {
            canvas = new Control { Name = "IngameDebugConsole", MouseFilter = Control.MouseFilterEnum.Ignore };
            UILayout.Stretch(canvas);
            AddChild(canvas);
            canvas.Resized += () => screenDimensionsChanged = true;

            // Log window: top half of the screen
            logWindow = new Panel { Name = "DebugLogWindow", MouseFilter = Control.MouseFilterEnum.Stop };
            logWindow.AddThemeStyleboxOverride("panel", DebugConsoleUI.Box(DebugConsoleUI.SlicedBackground, new Color(0.21960784f, 0.21960784f, 0.21960784f, 0.797f)));
            UILayout.SetRect(logWindow, 0, 0.5f, 1, 1, 0, 0, 0, 0, 0, 1);
            canvas.AddChild(logWindow);

            // Top bar
            var buttons = DebugConsoleUI.Image(DebugConsoleUI.SlicedBackground, new Color(0.5f, 0.5f, 0.5f), "Buttons");
            UILayout.SetRect(buttons, 0, 1, 1, 1, 0, 0, 0, barHeight, 1, 1);
            logWindow.AddChild(buttons);
            var buttonsLayout = new HBoxContainer { Name = "Layout", MouseFilter = Control.MouseFilterEnum.Ignore };
            buttonsLayout.AddThemeConstantOverride("separation", 0);
            UILayout.Stretch(buttonsLayout);
            buttons.AddChild(buttonsLayout);

            clearButton = AddIconButton(buttonsLayout, "ClearButton", "IconClear", DebugConsoleUI.IconColor, ClearLogs);
            collapseButton = AddIconButton(buttonsLayout, "CollapseButton", "IconCollapse", DebugConsoleUI.IconColor, CollapseButtonPressed);

            searchbarSlotTop = DebugConsoleUI.Image(DebugConsoleUI.SlicedBackground, new Color(0.24264705f, 0.24443123f, 0.24443123f), "SearchbarSlotTop");
            searchbarSlotTop.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            searchbarSlotTop.SizeFlagsStretchRatio = 2;
            buttonsLayout.AddChild(searchbarSlotTop);
            BuildSearchbar();
            searchbarSlotTop.AddChild(searchbar);

            filterInfoButton = AddFilterButton(buttonsLayout, "FilterLogButton", DebugLogType.Info, FilterLogButtonPressed, out infoEntryCountText);
            filterWarningButton = AddFilterButton(buttonsLayout, "FilterWarningButton", DebugLogType.Warning, FilterWarningButtonPressed, out warningEntryCountText);
            filterErrorButton = AddFilterButton(buttonsLayout, "FilterErrorButton", DebugLogType.Error, FilterErrorButtonPressed, out errorEntryCountText);
            hideButton = AddIconButton(buttonsLayout, "HideButton", "IconHide", Colors.White, HideLogWindow);

            // Searchbar slot used when the window is too narrow for the top bar
            searchbarSlotBottom = DebugConsoleUI.Image(DebugConsoleUI.SlicedBackground, filterButtonsNormalColor, "SearchbarSlotBottom");
            UILayout.SetRect(searchbarSlotBottom, 0, 1, 1, 1, 0, -barHeight, 0, barHeight, 0.5f, 1);
            searchbarSlotBottom.Visible = false;
            logWindow.AddChild(searchbarSlotBottom);

            // Log list
            logItemsScrollRect = DebugConsoleUI.Image(DebugConsoleUI.SlicedBackground, new Color(1, 1, 1, 0.392f), "Debugs");
            logWindow.AddChild(logItemsScrollRect);
            SetLogItemsScrollRectRect(false);

            recycledListView = new DebugLogRecycledListView();
            UILayout.SetRect(recycledListView, 0, 0, 1, 1, 0, 0, -29, 0, 0, 1);
            logItemsScrollRect.AddChild(recycledListView);

            logItemsScrollbar = new DebugLogScrollbar();
            UILayout.SetRect(logItemsScrollbar, 1, 0, 1, 1, 0, 0, 28, 0, 1, 1);
            logItemsScrollRect.AddChild(logItemsScrollbar);

            snapToBottomButton = DebugConsoleUI.Button("SnapToBottom", "IconSnapToBottomBg", new Color(0.30980393f, 0.30980393f, 0.30980393f, 0.6862745f));
            UILayout.SetRect(snapToBottomButton, 0.5f, 0, 0.5f, 0, 0, 8, 42, 42, 0.5f, 0);
            var snapIcon = DebugConsoleUI.Icon("IconSnapToBottom", DebugConsoleUI.IconColor);
            UILayout.SetRect(snapIcon, 0.2f, 0.2f, 0.8f, 0.8f, 0, 0, 0, 0);
            snapToBottomButton.AddChild(snapIcon);
            snapToBottomButton.Visible = false;
            snapToBottomButton.Pressed += () => SetSnapToBottom(true);
            recycledListView.AddChild(snapToBottomButton);

            // Resize button: bottom right corner, vertical resizing only
            resizeButton = DebugConsoleUI.Image(DebugConsoleUI.SlicedBackground, filterButtonsNormalColor, "DebugLogWindowResize");
            resizeButton.MouseFilter = Control.MouseFilterEnum.Stop;
            resizeButton.MouseDefaultCursorShape = Control.CursorShape.Vsize;
            UILayout.SetRect(resizeButton, 1, 0, 1, 0, 0, 0, 64, resizeButtonHeight, 1, 0);
            var resizeIcon = DebugConsoleUI.Icon("IconResizeVertialOnly", DebugConsoleUI.IconColor);
            UILayout.SetRect(resizeIcon, 0.15f, 0.12f, 0.85f, 0.88f, 0, 0, 0, 0);
            resizeButton.AddChild(resizeIcon);
            resizeButton.GuiInput += OnResizeButtonInput;
            logWindow.AddChild(resizeButton);

            // Initially, all log types are visible
            DebugConsoleUI.SetButtonColor(filterInfoButton, DebugConsoleUI.SlicedBackground, filterButtonsSelectedColor);
            DebugConsoleUI.SetButtonColor(filterWarningButton, DebugConsoleUI.SlicedBackground, filterButtonsSelectedColor);
            DebugConsoleUI.SetButtonColor(filterErrorButton, DebugConsoleUI.SlicedBackground, filterButtonsSelectedColor);

            popupManager = new DebugLogPopup();
            canvas.AddChild(popupManager);
        }

        static Button AddIconButton(Control parent, string name, string sprite, Color iconColor, System.Action action) {
            var button = DebugConsoleUI.Button(name, DebugConsoleUI.SlicedBackground, collapseButtonNormalColor);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var icon = DebugConsoleUI.Icon(sprite, iconColor);
            UILayout.SetRect(icon, 0.15f, 0.12f, 0.85f, 0.88f, 0, 0, 0, 0);
            button.AddChild(icon);
            button.Pressed += action;
            parent.AddChild(button);
            return button;
        }

        static Button AddFilterButton(Control parent, string name, DebugLogType type, System.Action action, out Label countText) {
            var button = DebugConsoleUI.Button(name, DebugConsoleUI.SlicedBackground, filterButtonsNormalColor);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var icon = DebugConsoleUI.Icon(LogTypeSprite(type), Colors.White);
            UILayout.SetRect(icon, 0, 0, 0.55f, 1, 1, 0.5f, -2, -7);
            button.AddChild(icon);
            countText = DebugConsoleUI.Text("0", 16, HorizontalAlignment.Center);
            UILayout.SetRect(countText, 0.55f, 0, 1, 1, 0, 0, -2, -2);
            button.AddChild(countText);
            TextFit.Apply(countText, 16, 10);
            button.Pressed += action;
            parent.AddChild(button);
            return button;
        }

        void BuildSearchbar() {
            searchbar = new Control { Name = "Searchbar", MouseFilter = Control.MouseFilterEnum.Ignore };
            UILayout.Stretch(searchbar);

            var icon = DebugConsoleUI.Icon("SearchIcon", DebugConsoleUI.IconColor);
            UILayout.SetRect(icon, 0, 0, 0, 1, 2, 0, 25, -6, 0, 0.5f);
            searchbar.AddChild(icon);

            // Input field without background: text inset 30px left, 7px top, 6px bottom
            searchField = new LineEdit { Name = "SearchField", PlaceholderText = "Search...", ContextMenuEnabled = false };
            UILayout.Stretch(searchField);
            var style = new StyleBoxEmpty { ContentMarginLeft = 30, ContentMarginRight = 0, ContentMarginTop = 7, ContentMarginBottom = 6 };
            searchField.AddThemeStyleboxOverride("normal", style);
            searchField.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            searchField.AddThemeStyleboxOverride("read_only", style);
            searchField.AddThemeFontSizeOverride("font_size", 16);
            var textColor = new Color(0.8392157f, 0.84313726f, 0.84313726f);
            searchField.AddThemeColorOverride("font_color", textColor);
            searchField.AddThemeColorOverride("caret_color", textColor);
            searchField.AddThemeColorOverride("font_placeholder_color", new Color(DebugConsoleUI.TextColor, 0.653f));
            searchField.AddThemeColorOverride("selection_color", new Color(0.659f, 0.808f, 1f, 0.75f));
            searchField.AddThemeConstantOverride("minimum_character_width", 0);
            searchField.TextChanged += SearchTermChanged;
            searchbar.AddChild(searchField);
        }

        // The log list sits between the top bar (and the bottom searchbar slot, when used) and the bottom strip
        void SetLogItemsScrollRectRect(bool searchbarAtBottom) {
            float searchbarHeight = searchbarAtBottom ? barHeight : 0f;
            UILayout.SetRect(logItemsScrollRect, 0, 0, 1, 1, 0, -searchbarHeight * 0.5f, 0, -2 * barHeight - searchbarHeight);
        }

        public override void _Process(double delta) {
            if (Instance != this || recycledListView == null) return;

            int numberOfLogsToProcess;
            lock (logEntriesLock) {
                numberOfLogsToProcess = isLogWindowVisible ? queuedLogEntries.Count : (queuedLogEntries.Count - queuedLogLimit);
            }
            for (int i = 0; i < numberOfLogsToProcess; i++) {
                QueuedDebugLogEntry logEntry;
                lock (logEntriesLock) {
                    logEntry = queuedLogEntries.Dequeue();
                }
                ProcessLog(logEntry);
            }

            int newInfoEntryCount, newWarningEntryCount, newErrorEntryCount;
            lock (logEntriesLock) {
                newInfoEntryCount = this.newInfoEntryCount;
                newWarningEntryCount = this.newWarningEntryCount;
                newErrorEntryCount = this.newErrorEntryCount;

                this.newInfoEntryCount = 0;
                this.newWarningEntryCount = 0;
                this.newErrorEntryCount = 0;
            }

            // Update entry count texts in a single batch
            if (newInfoEntryCount > 0 || newWarningEntryCount > 0 || newErrorEntryCount > 0) {
                if (newInfoEntryCount > 0) {
                    infoEntryCount += newInfoEntryCount;
                    if (isLogWindowVisible)
                        SetCountText(infoEntryCountText, infoEntryCount);
                }

                if (newWarningEntryCount > 0) {
                    warningEntryCount += newWarningEntryCount;
                    if (isLogWindowVisible)
                        SetCountText(warningEntryCountText, warningEntryCount);
                }

                if (newErrorEntryCount > 0) {
                    errorEntryCount += newErrorEntryCount;
                    if (isLogWindowVisible)
                        SetCountText(errorEntryCountText, errorEntryCount);
                }

                // If debug popup is visible, notify it of the new debug entries
                if (!isLogWindowVisible) {
                    entryCountTextsDirty = true;
                    popupManager.NewLogsArrived(newInfoEntryCount, newWarningEntryCount, newErrorEntryCount);
                }
            }

            if (isLogWindowVisible) {
                // Update visible logs if necessary
                if (shouldUpdateRecycledListView) {
                    recycledListView.OnLogEntriesUpdated(false);
                    shouldUpdateRecycledListView = false;
                }

                float logWindowWidth = logWindow.Size.X;
                if (!Mathf.IsEqualApprox(logWindowWidth, logWindowPreviousWidth)) {
                    logWindowPreviousWidth = logWindowWidth;

                    if (logWindowWidth >= topSearchbarMinWidth) {
                        if (searchbar.GetParent() == searchbarSlotBottom) {
                            searchbarSlotTop.Visible = true;
                            searchbar.Reparent(searchbarSlotTop, false);
                            searchbarSlotBottom.Visible = false;

                            SetLogItemsScrollRectRect(false);
                        }
                    }
                    else {
                        if (searchbar.GetParent() == searchbarSlotTop) {
                            searchbarSlotBottom.Visible = true;
                            searchbar.Reparent(searchbarSlotBottom, false);
                            searchbarSlotTop.Visible = false;

                            SetLogItemsScrollRectRect(true);
                        }
                    }
                }

                // If snapToBottom is enabled, force the scrollbar to the bottom
                if (snapToBottom) {
                    recycledListView.ScrollToBottom();
                    snapToBottomButton.Visible = false;
                }
                else {
                    float scrollPos = recycledListView.NormalizedPosition;
                    snapToBottomButton.Visible = scrollPos > 1E-6f && scrollPos < 0.9999f;
                }
            }

            if (screenDimensionsChanged) {
                // Update the recycled list view
                if (isLogWindowVisible)
                    recycledListView.OnViewportHeightChanged();
                else
                    popupManager.UpdatePosition(true);

                screenDimensionsChanged = false;
            }
        }

        static void SetCountText(Label label, int count) {
            label.Text = count.ToString();
            TextFit.Refit(label);
        }

        public void ShowLogWindow() {
            // Show the log window
            logWindow.Visible = true;

            popupManager.HidePopup();

            // Update the recycled list view
            // (in case new entries were intercepted while log window was hidden)
            recycledListView.OnLogEntriesUpdated(true);

            if (entryCountTextsDirty) {
                SetCountText(infoEntryCountText, infoEntryCount);
                SetCountText(warningEntryCountText, warningEntryCount);
                SetCountText(errorEntryCountText, errorEntryCount);

                entryCountTextsDirty = false;
            }

            isLogWindowVisible = true;
        }

        public void HideLogWindow() {
            // Hide the log window
            logWindow.Visible = false;
            ReleaseInputFocus();

            popupManager.ShowPopup();

            isLogWindowVisible = false;
        }

        // The input field must lose the focus when clicking elsewhere, Godot's LineEdit keeps it (and keeps
        // blocking the editor shortcuts)
        internal void ReleaseInputFocus() {
            if (searchField != null && searchField.HasFocus()) searchField.ReleaseFocus();
        }

        // A debug entry is received (from any thread)
        public void ReceivedLog(string logString, string stackTrace, DebugLogType logType) {
            logString ??= "";

            // Truncate the log if it is longer than maxLogLength
            int logLength = logString.Length;
            if (stackTrace == null) {
                if (logLength > maxLogLength)
                    logString = logString.Substring(0, maxLogLength - 11) + "<truncated>";
            }
            else {
                logLength += stackTrace.Length;
                if (logLength > maxLogLength) {
                    // Decide which log component(s) to truncate
                    int halfMaxLogLength = maxLogLength / 2;
                    if (logString.Length >= halfMaxLogLength) {
                        if (stackTrace.Length >= halfMaxLogLength) {
                            // Truncate both logString and stackTrace
                            logString = logString.Substring(0, halfMaxLogLength - 11) + "<truncated>";
                            stackTrace = stackTrace.Substring(0, halfMaxLogLength - 12) + "<truncated>\n";
                        }
                        else {
                            // Truncate logString
                            logString = logString.Substring(0, maxLogLength - stackTrace.Length - 11) + "<truncated>";
                        }
                    }
                    else {
                        // Truncate stackTrace
                        stackTrace = stackTrace.Substring(0, maxLogLength - logString.Length - 12) + "<truncated>\n";
                    }
                }
            }

            var queuedLogEntry = new QueuedDebugLogEntry(logString, stackTrace ?? "", logType);

            lock (logEntriesLock) {
                queuedLogEntries.Enqueue(queuedLogEntry);

                if (logType == DebugLogType.Info)
                    newInfoEntryCount++;
                else if (logType == DebugLogType.Warning)
                    newWarningEntryCount++;
                else
                    newErrorEntryCount++;
            }
        }

        // Present the log entry in the console
        void ProcessLog(QueuedDebugLogEntry queuedLogEntry) {
            var logEntry = new DebugLogEntry();
            logEntry.Initialize(queuedLogEntry.logString, queuedLogEntry.stackTrace);

            // Check if this entry is a duplicate (i.e. has been received before)
            bool isEntryInCollapsedEntryList = collapsedLogEntriesMap.TryGetValue(logEntry, out int logEntryIndex);
            if (!isEntryInCollapsedEntryList) {
                // It is not a duplicate,
                // add it to the list of unique debug entries
                logEntry.logType = queuedLogEntry.logType;

                logEntryIndex = collapsedLogEntries.Count;
                collapsedLogEntries.Add(logEntry);
                collapsedLogEntriesMap[logEntry] = logEntryIndex;
            }
            else {
                // It is a duplicate, increment the original debug item's collapsed count
                logEntry = collapsedLogEntries[logEntryIndex];
                logEntry.count++;
            }

            // Add the index of the unique debug entry to the list
            // that stores the order the debug entries are received
            uncollapsedLogEntriesIndices.Add(logEntryIndex);

            // If this debug entry matches the current filters,
            // add it to the list of debug entries to show
            if (isCollapseOn && isEntryInCollapsedEntryList) {
                if (isLogWindowVisible) {
                    int logEntryIndexInEntriesToShow;
                    if (!isInSearchMode && logFilter == DebugLogFilter.All)
                        logEntryIndexInEntriesToShow = logEntryIndex;
                    else
                        logEntryIndexInEntriesToShow = indicesOfListEntriesToShow.IndexOf(logEntryIndex);

                    if (logEntryIndexInEntriesToShow >= 0)
                        recycledListView.OnCollapsedLogEntryAtIndexUpdated(logEntryIndexInEntriesToShow);
                }
            }
            else if ((!isInSearchMode || queuedLogEntry.MatchesSearchTerm(searchTerm)) && IsShownByFilter(logEntry.logType)) {
                indicesOfListEntriesToShow.Add(logEntryIndex);
                shouldUpdateRecycledListView = true;
            }
        }

        bool IsShownByFilter(DebugLogType logType) {
            return logType switch {
                DebugLogType.Info => (logFilter & DebugLogFilter.Info) == DebugLogFilter.Info,
                DebugLogType.Warning => (logFilter & DebugLogFilter.Warning) == DebugLogFilter.Warning,
                _ => (logFilter & DebugLogFilter.Error) == DebugLogFilter.Error
            };
        }

        // Value of snapToBottom is changed (user scrolled the list manually)
        public void SetSnapToBottom(bool snapToBottom) {
            this.snapToBottom = snapToBottom;
        }

        // Clear all the logs
        public void ClearLogs() {
            snapToBottom = true;

            infoEntryCount = 0;
            warningEntryCount = 0;
            errorEntryCount = 0;

            SetCountText(infoEntryCountText, 0);
            SetCountText(warningEntryCountText, 0);
            SetCountText(errorEntryCountText, 0);

            collapsedLogEntries.Clear();
            collapsedLogEntriesMap.Clear();
            uncollapsedLogEntriesIndices.Clear();
            indicesOfListEntriesToShow.Clear();

            recycledListView.DeselectSelectedLogItem();
            recycledListView.OnLogEntriesUpdated(true);
        }

        // Collapse button is clicked
        void CollapseButtonPressed() {
            // Swap the value of collapse mode
            isCollapseOn = !isCollapseOn;

            snapToBottom = true;
            DebugConsoleUI.SetButtonColor(collapseButton, DebugConsoleUI.SlicedBackground, isCollapseOn ? collapseButtonSelectedColor : collapseButtonNormalColor);
            recycledListView.SetCollapseMode(isCollapseOn);

            // Determine the new list of debug entries to show
            FilterLogs();
        }

        // Filtering mode of info logs has changed
        void FilterLogButtonPressed() {
            ToggleFilter(DebugLogFilter.Info, filterInfoButton);
        }

        // Filtering mode of warning logs has changed
        void FilterWarningButtonPressed() {
            ToggleFilter(DebugLogFilter.Warning, filterWarningButton);
        }

        // Filtering mode of error logs has changed
        void FilterErrorButtonPressed() {
            ToggleFilter(DebugLogFilter.Error, filterErrorButton);
        }

        void ToggleFilter(DebugLogFilter filter, Button button) {
            logFilter ^= filter;
            DebugConsoleUI.SetButtonColor(button, DebugConsoleUI.SlicedBackground,
                (logFilter & filter) == filter ? filterButtonsSelectedColor : filterButtonsNormalColor);
            FilterLogs();
        }

        // Search term has changed
        void SearchTermChanged(string searchTerm) {
            if (searchTerm != null)
                searchTerm = searchTerm.Trim();

            this.searchTerm = searchTerm;
            bool isInSearchMode = !string.IsNullOrEmpty(searchTerm);
            if (isInSearchMode || this.isInSearchMode) {
                this.isInSearchMode = isInSearchMode;
                FilterLogs();
            }
        }

        // Debug window is being resized: the resize button is grabbed from its top edge, the window keeps
        // its minimum height
        void OnResizeButtonInput(InputEvent @event) {
            if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left) {
                resizeButton.AcceptEvent();
                resizePointerDown = mouseButton.Pressed;
                resizing = false;
                resizePointerDownPosition = mouseButton.GlobalPosition;
            }
            else if (@event is InputEventMouseMotion motion && resizePointerDown) {
                resizeButton.AcceptEvent();
                if (!resizing && motion.GlobalPosition.DistanceTo(resizePointerDownPosition) >= resizeDragThreshold) resizing = true;
                if (resizing) Resize(motion.GlobalPosition);
            }
        }

        void Resize(Vector2 globalPosition) {
            float canvasHeight = canvas.Size.Y;
            if (canvasHeight <= 0f) return;

            float windowBottom = globalPosition.Y - canvas.GetGlobalRect().Position.Y + resizeButtonHeight;
            if (windowBottom < minimumHeight)
                windowBottom = minimumHeight;

            logWindow.SetAnchor(Side.Bottom, Mathf.Clamp(windowBottom / canvasHeight, 0f, 1f), false, false);
            logWindow.OffsetBottom = 0;
        }

        // Determine the filtered list of debug entries to show on screen
        void FilterLogs() {
            indicesOfListEntriesToShow.Clear();

            if (logFilter != DebugLogFilter.None) {
                if (isCollapseOn) {
                    // All the unique debug entries will be listed just once.
                    // So, list of debug entries to show is the same as the
                    // order these unique debug entries are added to collapsedLogEntries
                    for (int i = 0, count = collapsedLogEntries.Count; i < count; i++) {
                        var logEntry = collapsedLogEntries[i];
                        if (isInSearchMode && !logEntry.MatchesSearchTerm(searchTerm)) continue;
                        if (IsShownByFilter(logEntry.logType)) indicesOfListEntriesToShow.Add(i);
                    }
                }
                else {
                    for (int i = 0, count = uncollapsedLogEntriesIndices.Count; i < count; i++) {
                        var logEntry = collapsedLogEntries[uncollapsedLogEntriesIndices[i]];
                        if (isInSearchMode && !logEntry.MatchesSearchTerm(searchTerm)) continue;
                        if (IsShownByFilter(logEntry.logType)) indicesOfListEntriesToShow.Add(uncollapsedLogEntriesIndices[i]);
                    }
                }
            }

            // Update the recycled list view
            recycledListView.DeselectSelectedLogItem();
            recycledListView.OnLogEntriesUpdated(true);
        }
    }
}

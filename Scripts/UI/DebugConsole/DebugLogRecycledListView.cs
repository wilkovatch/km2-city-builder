using System.Collections.Generic;
using Godot;

namespace IngameDebugConsole {
    // The log list's scroll view (the "Viewport" of the Debugs ScrollRect). Handles the log items in an
    // optimized way such that existing log items are recycled within the list instead of creating a new
    // log item at each chance. Also replaces the ScrollRect (wheel and drag scrolling with inertia,
    // clamped movement) and DebugsOnScrollListener (snap to bottom after a user scroll).
    public partial class DebugLogRecycledListView : Control {
        const float LogItemHeight = DebugLogItem.ItemHeight;
        const float ScrollSensitivity = 25f;
        const float DecelerationRate = 0.01f;
        const float DragThreshold = 10f;

        static readonly Color logItemNormalColor1 = new Color(0.23529412f, 0.23529412f, 0.23529412f, 0.697f);
        static readonly Color logItemNormalColor2 = new Color(0.19607843f, 0.19607843f, 0.19607843f, 0.697f);
        static readonly Color logItemSelectedColor = new Color(0.32941177f, 0.4862745f, 0.7058824f, 0.697f);

        DebugLogManager manager;
        DebugLogScrollbar scrollbar;
        readonly Control logItemsContainer;
        readonly DebugLogItem measureItem; // never shown, used to calculate the expanded height

        // Unique debug entries
        List<DebugLogEntry> collapsedLogEntries;

        // Indices of debug entries to show in collapsedLogEntries
        List<int> indicesOfEntriesToShow;

        int indexOfSelectedLogEntry = int.MaxValue;
        float positionOfSelectedLogEntry = float.MaxValue;
        float heightOfSelectedLogEntry;
        float deltaHeightOfSelectedLogEntry;

        // Log items used to visualize the debug entries at specified indices
        readonly Dictionary<int, DebugLogItem> logItemsAtIndices = new Dictionary<int, DebugLogItem>(256);
        readonly List<DebugLogItem> pooledLogItems = new List<DebugLogItem>(16);

        bool isCollapseOn = false;

        // Current indices of debug entries shown on screen
        int currentTopIndex = -1, currentBottomIndex = -1;

        float contentHeight = 1f;
        float scrollOffset; // content position, 0 = top
        float viewportHeight, viewportWidth;

        // Pointer state
        bool pointerDown, dragging;
        Vector2 pointerDownPosition;
        float dragStartOffset;
        float velocity, previousOffset;
        int hoveredIndex = -1, pressedIndex = -1;

        public DebugLogRecycledListView() {
            Name = "Viewport";
            ClipContents = true;
            MouseFilter = MouseFilterEnum.Stop;

            logItemsContainer = new Control { Name = "LogsContainer", MouseFilter = MouseFilterEnum.Ignore };
            AddChild(logItemsContainer);

            measureItem = new DebugLogItem { Name = "MeasureItem", Visible = false };
            AddChild(measureItem);

            Resized += OnResized;
            MouseExited += () => SetHoveredIndex(-1);
        }

        public void Initialize(DebugLogManager manager, DebugLogScrollbar scrollbar, List<DebugLogEntry> collapsedLogEntries, List<int> indicesOfEntriesToShow) {
            this.manager = manager;
            this.scrollbar = scrollbar;
            this.collapsedLogEntries = collapsedLogEntries;
            this.indicesOfEntriesToShow = indicesOfEntriesToShow;

            scrollbar.ValueChanged += value => NormalizedPosition = value;
            scrollbar.DragStarted += () => manager.SetSnapToBottom(false);
            scrollbar.DragEnded += () => manager.SetSnapToBottom(IsScrollbarAtBottom());
            scrollbar.WheelScrolled += OnScroll;
        }

        public void SetCollapseMode(bool collapse) {
            isCollapseOn = collapse;
        }

        // ScrollRect.verticalNormalizedPosition: 1 = top, 0 = bottom (also 0 when everything fits)
        public float NormalizedPosition {
            get {
                var maxOffset = MaxScrollOffset;
                return maxOffset <= 0f ? 0f : 1f - scrollOffset / maxOffset;
            }
            set {
                SetScrollOffset((1f - Mathf.Clamp(value, 0f, 1f)) * MaxScrollOffset);
            }
        }

        float MaxScrollOffset => Mathf.Max(0f, contentHeight - viewportHeight);

        bool IsScrollbarAtBottom() {
            return NormalizedPosition <= 1E-6f;
        }

        void SetScrollOffset(float offset) {
            offset = Mathf.Clamp(offset, 0f, MaxScrollOffset);
            bool changed = !Mathf.IsEqualApprox(offset, scrollOffset);
            scrollOffset = offset;
            logItemsContainer.Position = new Vector2(1f, -scrollOffset);
            UpdateScrollbar();
            if (changed) UpdateItemsInTheList(false);
        }

        void UpdateScrollbar() {
            scrollbar?.SetState(NormalizedPosition, contentHeight <= 0f ? 1f : viewportHeight / contentHeight);
        }

        // Clicking a log item highlights (expands) it, clicking it again deselects it
        internal void OnLogItemClicked(int itemIndex) {
            if (indexOfSelectedLogEntry != itemIndex) {
                DeselectSelectedLogItem();

                indexOfSelectedLogEntry = itemIndex;
                positionOfSelectedLogEntry = itemIndex * LogItemHeight;
                heightOfSelectedLogEntry = measureItem.CalculateExpandedHeight(collapsedLogEntries[indicesOfEntriesToShow[itemIndex]], viewportWidth);
                deltaHeightOfSelectedLogEntry = heightOfSelectedLogEntry - LogItemHeight;

                manager.SetSnapToBottom(false);
            }
            else {
                DeselectSelectedLogItem();
            }

            if (indexOfSelectedLogEntry >= currentTopIndex && indexOfSelectedLogEntry <= currentBottomIndex)
                ColorLogItem(logItemsAtIndices[indexOfSelectedLogEntry], indexOfSelectedLogEntry);

            CalculateContentHeight();

            HardResetItems();
            UpdateItemsInTheList(true);
        }

        // Deselect the currently selected log item
        public void DeselectSelectedLogItem() {
            int indexOfPreviouslySelectedLogEntry = indexOfSelectedLogEntry;
            indexOfSelectedLogEntry = int.MaxValue;

            positionOfSelectedLogEntry = float.MaxValue;
            heightOfSelectedLogEntry = deltaHeightOfSelectedLogEntry = 0f;

            if (indexOfPreviouslySelectedLogEntry >= currentTopIndex && indexOfPreviouslySelectedLogEntry <= currentBottomIndex)
                ColorLogItem(logItemsAtIndices[indexOfPreviouslySelectedLogEntry], indexOfPreviouslySelectedLogEntry);
        }

        // Number of debug entries may be changed, update the list
        public void OnLogEntriesUpdated(bool updateAllVisibleItemContents) {
            viewportHeight = Size.Y;
            CalculateContentHeight();

            if (updateAllVisibleItemContents)
                HardResetItems();

            UpdateItemsInTheList(updateAllVisibleItemContents);
        }

        // A single collapsed log entry at specified index is updated, refresh its item if visible
        public void OnCollapsedLogEntryAtIndexUpdated(int index) {
            if (logItemsAtIndices.TryGetValue(index, out var logItem))
                logItem.ShowCount();
        }

        void OnResized() {
            var widthChanged = !Mathf.IsEqualApprox(Size.X, viewportWidth);
            viewportWidth = Size.X;
            viewportHeight = Size.Y;
            logItemsContainer.Size = new Vector2(viewportWidth, contentHeight);
            foreach (var item in logItemsAtIndices.Values) item.Size = new Vector2(viewportWidth, item.Size.Y);
            if (manager == null) return;
            if (widthChanged) OnViewportWidthChanged();
            else OnViewportHeightChanged();
        }

        // Log window's width has changed, update the expanded (currently selected) log's height
        void OnViewportWidthChanged() {
            if (indexOfSelectedLogEntry >= indicesOfEntriesToShow.Count) {
                OnViewportHeightChanged();
                return;
            }

            heightOfSelectedLogEntry = measureItem.CalculateExpandedHeight(collapsedLogEntries[indicesOfEntriesToShow[indexOfSelectedLogEntry]], viewportWidth);
            deltaHeightOfSelectedLogEntry = heightOfSelectedLogEntry - LogItemHeight;

            CalculateContentHeight();

            HardResetItems();
            UpdateItemsInTheList(true);
        }

        // Log window's height has changed, update the list
        public void OnViewportHeightChanged() {
            viewportHeight = Size.Y;
            SetScrollOffset(scrollOffset);
            UpdateItemsInTheList(false);
        }

        void HardResetItems() {
            if (currentTopIndex != -1) {
                DestroyLogItemsBetweenIndices(currentTopIndex, currentBottomIndex);
                currentTopIndex = -1;
            }
        }

        // Also clamps the scroll position, like the ScrollRect's clamped movement does
        void CalculateContentHeight() {
            contentHeight = Mathf.Max(1f, indicesOfEntriesToShow.Count * LogItemHeight + deltaHeightOfSelectedLogEntry);
            logItemsContainer.Size = new Vector2(viewportWidth, contentHeight);
            scrollOffset = Mathf.Clamp(scrollOffset, 0f, MaxScrollOffset);
            logItemsContainer.Position = new Vector2(1f, -scrollOffset);
            UpdateScrollbar();
        }

        // Calculate the indices of log entries to show and handle log items accordingly
        public void UpdateItemsInTheList(bool updateAllVisibleItemContents) {
            // If there is at least one log entry to show
            if (indicesOfEntriesToShow.Count > 0) {
                float contentPosTop = scrollOffset - 1f;
                float contentPosBottom = contentPosTop + viewportHeight + 2f;

                if (positionOfSelectedLogEntry <= contentPosBottom) {
                    if (positionOfSelectedLogEntry <= contentPosTop) {
                        contentPosTop -= deltaHeightOfSelectedLogEntry;
                        contentPosBottom -= deltaHeightOfSelectedLogEntry;

                        if (contentPosTop < positionOfSelectedLogEntry - 1f)
                            contentPosTop = positionOfSelectedLogEntry - 1f;

                        if (contentPosBottom < contentPosTop + 2f)
                            contentPosBottom = contentPosTop + 2f;
                    }
                    else {
                        contentPosBottom -= deltaHeightOfSelectedLogEntry;
                        if (contentPosBottom < positionOfSelectedLogEntry + 1f)
                            contentPosBottom = positionOfSelectedLogEntry + 1f;
                    }
                }

                int newTopIndex = (int)(contentPosTop / LogItemHeight);
                int newBottomIndex = (int)(contentPosBottom / LogItemHeight);

                if (newTopIndex < 0)
                    newTopIndex = 0;

                if (newBottomIndex > indicesOfEntriesToShow.Count - 1)
                    newBottomIndex = indicesOfEntriesToShow.Count - 1;

                if (currentTopIndex == -1) {
                    // There are no log items visible on screen, just create the new log items
                    updateAllVisibleItemContents = true;

                    currentTopIndex = newTopIndex;
                    currentBottomIndex = newBottomIndex;

                    CreateLogItemsBetweenIndices(newTopIndex, newBottomIndex);
                }
                else {
                    // There are some log items visible on screen

                    if (newBottomIndex < currentTopIndex || newTopIndex > currentBottomIndex) {
                        // If user scrolled a lot such that, none of the log items are now within
                        // the bounds of the scroll view, pool all the previous log items and create
                        // new log items for the new list of visible debug entries
                        updateAllVisibleItemContents = true;

                        DestroyLogItemsBetweenIndices(currentTopIndex, currentBottomIndex);
                        CreateLogItemsBetweenIndices(newTopIndex, newBottomIndex);
                    }
                    else {
                        // User did not scroll a lot such that, there are still some log items within
                        // the bounds of the scroll view. Don't destroy them but update their content,
                        // if necessary
                        if (newTopIndex > currentTopIndex)
                            DestroyLogItemsBetweenIndices(currentTopIndex, newTopIndex - 1);

                        if (newBottomIndex < currentBottomIndex)
                            DestroyLogItemsBetweenIndices(newBottomIndex + 1, currentBottomIndex);

                        if (newTopIndex < currentTopIndex) {
                            CreateLogItemsBetweenIndices(newTopIndex, currentTopIndex - 1);

                            // If it is not necessary to update all the log items,
                            // then just update the newly created log items. Otherwise,
                            // wait for the major update
                            if (!updateAllVisibleItemContents)
                                UpdateLogItemContentsBetweenIndices(newTopIndex, currentTopIndex - 1);
                        }

                        if (newBottomIndex > currentBottomIndex) {
                            CreateLogItemsBetweenIndices(currentBottomIndex + 1, newBottomIndex);

                            // If it is not necessary to update all the log items,
                            // then just update the newly created log items. Otherwise,
                            // wait for the major update
                            if (!updateAllVisibleItemContents)
                                UpdateLogItemContentsBetweenIndices(currentBottomIndex + 1, newBottomIndex);
                        }
                    }

                    currentTopIndex = newTopIndex;
                    currentBottomIndex = newBottomIndex;
                }

                if (updateAllVisibleItemContents) {
                    // Update all the log items
                    UpdateLogItemContentsBetweenIndices(currentTopIndex, currentBottomIndex);
                }
            }
            else
                HardResetItems();
        }

        void CreateLogItemsBetweenIndices(int topIndex, int bottomIndex) {
            for (int i = topIndex; i <= bottomIndex; i++)
                CreateLogItemAtIndex(i);
        }

        // Create (or unpool) a log item
        void CreateLogItemAtIndex(int index) {
            DebugLogItem logItem = PopLogItem();

            // Reposition the log item
            float y = index * LogItemHeight;
            if (index > indexOfSelectedLogEntry)
                y += deltaHeightOfSelectedLogEntry;

            logItem.Position = new Vector2(0f, y);

            // To access this log item easily in the future, add it to the dictionary
            logItemsAtIndices[index] = logItem;

            // Color the log item
            ColorLogItem(logItem, index);
        }

        void DestroyLogItemsBetweenIndices(int topIndex, int bottomIndex) {
            for (int i = topIndex; i <= bottomIndex; i++) {
                PoolLogItem(logItemsAtIndices[i]);
                logItemsAtIndices.Remove(i);
            }
        }

        void UpdateLogItemContentsBetweenIndices(int topIndex, int bottomIndex) {
            for (int i = topIndex; i <= bottomIndex; i++) {
                var logItem = logItemsAtIndices[i];
                bool expanded = i == indexOfSelectedLogEntry;
                logItem.SetContent(collapsedLogEntries[indicesOfEntriesToShow[i]], i, expanded);
                logItem.Size = new Vector2(viewportWidth, expanded ? heightOfSelectedLogEntry : LogItemHeight);

                if (isCollapseOn)
                    logItem.ShowCount();
                else
                    logItem.HideCount();
            }
        }

        DebugLogItem PopLogItem() {
            DebugLogItem logItem;
            if (pooledLogItems.Count > 0) {
                logItem = pooledLogItems[pooledLogItems.Count - 1];
                pooledLogItems.RemoveAt(pooledLogItems.Count - 1);
                logItem.Visible = true;
            }
            else {
                logItem = new DebugLogItem();
                logItemsContainer.AddChild(logItem);
            }
            logItem.Size = new Vector2(viewportWidth, LogItemHeight);
            return logItem;
        }

        void PoolLogItem(DebugLogItem logItem) {
            logItem.Visible = false;
            pooledLogItems.Add(logItem);
        }

        // Color a log item using its index (and the button tint of the hovered/pressed item)
        void ColorLogItem(DebugLogItem logItem, int index) {
            Color color;
            if (index == indexOfSelectedLogEntry)
                color = logItemSelectedColor;
            else if (index % 2 == 0)
                color = logItemNormalColor1;
            else
                color = logItemNormalColor2;

            if (index == pressedIndex && index == hoveredIndex) color *= DebugConsoleUI.PressedTint;
            else if (index == hoveredIndex && !dragging) color *= DebugConsoleUI.HighlightedTint;
            logItem.SetColor(color);
        }

        void RecolorItem(int index) {
            if (index >= 0 && logItemsAtIndices.TryGetValue(index, out var item)) ColorLogItem(item, index);
        }

        void SetHoveredIndex(int index) {
            if (index == hoveredIndex) return;
            var previous = hoveredIndex;
            hoveredIndex = index;
            RecolorItem(previous);
            RecolorItem(index);
        }

        int GetIndexAt(Vector2 localPosition) {
            foreach (var pair in logItemsAtIndices) {
                if (pair.Value.GetRect().HasPoint(localPosition - logItemsContainer.Position)) return pair.Key;
            }
            return -1;
        }

        // Scroll event (mouse wheel), see DebugsOnScrollListener
        void OnScroll(InputEventMouseButton mouseButton) {
            float amount = ScrollSensitivity * (mouseButton.Factor > 0f ? mouseButton.Factor : 1f);
            velocity = 0f;
            SetScrollOffset(scrollOffset + (mouseButton.ButtonIndex == MouseButton.WheelUp ? -amount : amount));
            manager.SetSnapToBottom(IsScrollbarAtBottom());
        }

        public override void _GuiInput(InputEvent @event) {
            if (@event is InputEventMouseButton mouseButton) {
                if (mouseButton.ButtonIndex == MouseButton.WheelUp || mouseButton.ButtonIndex == MouseButton.WheelDown) {
                    if (mouseButton.Pressed) OnScroll(mouseButton);
                    AcceptEvent();
                    return;
                }
                if (mouseButton.ButtonIndex != MouseButton.Left) return;
                AcceptEvent();
                if (mouseButton.Pressed) {
                    manager.ReleaseInputFocus();
                    pointerDown = true;
                    dragging = false;
                    velocity = 0f;
                    pointerDownPosition = mouseButton.Position;
                    dragStartOffset = scrollOffset;
                    pressedIndex = GetIndexAt(mouseButton.Position);
                    RecolorItem(pressedIndex);
                }
                else if (pointerDown) {
                    pointerDown = false;
                    var previouslyPressed = pressedIndex;
                    pressedIndex = -1;
                    if (dragging) {
                        // OnEndDrag
                        dragging = false;
                        manager.SetSnapToBottom(IsScrollbarAtBottom());
                    }
                    else if (previouslyPressed >= 0 && previouslyPressed == GetIndexAt(mouseButton.Position)) {
                        OnLogItemClicked(previouslyPressed); // OnPointerClick
                    }
                    RecolorItem(previouslyPressed);
                    SetHoveredIndex(GetIndexAt(mouseButton.Position));
                }
            }
            else if (@event is InputEventMouseMotion motion) {
                if (pointerDown) {
                    if (!dragging && motion.Position.DistanceTo(pointerDownPosition) >= DragThreshold) {
                        // OnBeginDrag: the content follows the pointer from now on
                        dragging = true;
                        manager.SetSnapToBottom(false);
                        var previouslyPressed = pressedIndex;
                        pressedIndex = -1;
                        RecolorItem(previouslyPressed);
                        pointerDownPosition = motion.Position;
                        dragStartOffset = scrollOffset;
                        previousOffset = scrollOffset;
                    }
                    if (dragging) {
                        SetScrollOffset(dragStartOffset - (motion.Position.Y - pointerDownPosition.Y));
                    }
                    AcceptEvent();
                }
                else {
                    SetHoveredIndex(GetIndexAt(motion.Position));
                }
            }
        }

        public override void _Process(double delta) {
            float dt = (float)delta;
            if (dt <= 0f) return;
            if (dragging) {
                // ScrollRect: track the velocity while dragging for the inertia
                float newVelocity = (scrollOffset - previousOffset) / dt;
                velocity = Mathf.Lerp(velocity, newVelocity, dt * 10f);
                previousOffset = scrollOffset;
            }
            else if (velocity != 0f) {
                velocity *= Mathf.Pow(DecelerationRate, dt);
                if (Mathf.Abs(velocity) < 1f) velocity = 0f;
                var target = scrollOffset + velocity * dt;
                if (target <= 0f || target >= MaxScrollOffset) velocity = 0f;
                SetScrollOffset(target);
            }
        }

        // Snap to bottom stops the inertia
        public void ScrollToBottom() {
            velocity = 0f;
            NormalizedPosition = 0f;
        }
    }
}

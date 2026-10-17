using Godot;

namespace IngameDebugConsole {
    // Manager class for the debug popup: a draggable bubble glued to the nearest screen edge, showing the
    // number of new logs since the log window was hidden. Clicking it opens the log window.
    public partial class DebugLogPopup : Panel {
        const float DragThreshold = 10f;
        static readonly Vector2 popupSize = new Vector2(72, 72);
        static readonly Color normalColor = new Color(0.30588236f, 0.30588236f, 0.30588236f);
        static readonly Color alertColorInfo = new Color(0.050980393f, 0.32941177f, 0.5647059f);
        static readonly Color alertColorWarning = new Color(0.59607846f, 0.45490196f, 0f);
        static readonly Color alertColorError = new Color(0.5647059f, 0.050980393f, 0.050980393f);

        DebugLogManager debugManager;
        Control canvas;
        Label newInfoCountText, newWarningCountText, newErrorCountText;
        StyleBoxTexture backgroundImage;

        // Dimensions of the popup divided by 2
        readonly Vector2 halfSize = popupSize * 0.5f;

        // Number of new debug entries since the log window has been closed
        int newInfoCount = 0, newWarningCount = 0, newErrorCount = 0;

        bool isPopupBeingDragged = false;
        bool pointerDown = false;
        Vector2 pointerDownPosition;
        Vector2 normalizedPosition;

        // Position relative to the canvas centre, y up
        Vector2 anchoredPosition;

        // Smooth movement animation
        bool moving = false;
        float moveModifier;
        Vector2 moveInitialPos, moveTargetPos;

        public DebugLogPopup() {
            Name = "DebugLogPopup";
            MouseFilter = MouseFilterEnum.Stop;
            Size = popupSize;
            backgroundImage = DebugConsoleUI.Box(DebugConsoleUI.SlicedBackground3, normalColor);
            AddThemeStyleboxOverride("panel", backgroundImage);

            AddRow(DebugLogType.Info, 0.675f, 0.975f, out newInfoCountText);
            AddRow(DebugLogType.Warning, 0.35f, 0.65f, out newWarningCountText);
            AddRow(DebugLogType.Error, 0.025f, 0.325f, out newErrorCountText);
        }

        void AddRow(DebugLogType type, float anchorMinY, float anchorMaxY, out Label countText) {
            var image = DebugConsoleUI.Icon(DebugLogManager.LogTypeSprite(type), Colors.White);
            UILayout.SetRect(image, 0, anchorMinY, 0.45f, anchorMaxY, 0, 0, -1, 0, 1, 0.5f);
            AddChild(image);

            countText = DebugConsoleUI.Text("0", 16, HorizontalAlignment.Center);
            UILayout.SetRect(countText, 0.45f, anchorMinY, 1, anchorMaxY, -1, 0, -2, 0);
            AddChild(countText);
            TextFit.Apply(countText, 16, 10);
        }

        public void Initialize(DebugLogManager debugManager, Control canvas) {
            this.debugManager = debugManager;
            this.canvas = canvas;

            // The CityBuilder scene moved the popup to (-1000, -1000): bottom left corner
            normalizedPosition = new Vector2(-1000f, -1000f).Normalized();
        }

        Vector2 AnchoredPosition {
            get => anchoredPosition;
            set {
                anchoredPosition = value;
                Position = canvas.Size * 0.5f + new Vector2(value.X, -value.Y) - halfSize;
            }
        }

        public void NewLogsArrived(int newInfo, int newWarning, int newError) {
            if (newInfo > 0) {
                newInfoCount += newInfo;
                SetCount(newInfoCountText, newInfoCount);
            }

            if (newWarning > 0) {
                newWarningCount += newWarning;
                SetCount(newWarningCountText, newWarningCount);
            }

            if (newError > 0) {
                newErrorCount += newError;
                SetCount(newErrorCountText, newErrorCount);
            }

            if (newErrorCount > 0)
                backgroundImage.ModulateColor = alertColorError;
            else if (newWarningCount > 0)
                backgroundImage.ModulateColor = alertColorWarning;
            else
                backgroundImage.ModulateColor = alertColorInfo;
        }

        static void SetCount(Label label, int count) {
            label.Text = count.ToString();
            TextFit.Refit(label);
        }

        void ResetCounters() {
            newInfoCount = 0;
            newWarningCount = 0;
            newErrorCount = 0;

            SetCount(newInfoCountText, 0);
            SetCount(newWarningCountText, 0);
            SetCount(newErrorCountText, 0);

            backgroundImage.ModulateColor = normalColor;
        }

        public override void _Process(double delta) {
            // A simple smooth movement animation
            if (!moving) return;
            moveModifier += 4f * (float)delta;
            AnchoredPosition = moveInitialPos.Lerp(moveTargetPos, Mathf.Min(moveModifier, 1f));
            if (moveModifier >= 1f) moving = false;
        }

        public override void _GuiInput(InputEvent @event) {
            if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left) {
                AcceptEvent();
                if (mouseButton.Pressed) {
                    pointerDown = true;
                    pointerDownPosition = mouseButton.GlobalPosition;
                }
                else if (pointerDown) {
                    pointerDown = false;
                    if (isPopupBeingDragged) OnEndDrag();
                    else debugManager.ShowLogWindow(); // Hide the popup and show the log window
                }
            }
            else if (@event is InputEventMouseMotion motion && pointerDown) {
                AcceptEvent();
                if (!isPopupBeingDragged && motion.GlobalPosition.DistanceTo(pointerDownPosition) >= DragThreshold) {
                    OnBeginDrag();
                }
                if (isPopupBeingDragged) OnDrag(motion.GlobalPosition);
            }
        }

        // Hides the log window and shows the popup
        public void ShowPopup() {
            Visible = true;

            // Reset the counters
            ResetCounters();

            // Update position in case resolution was changed while the popup was hidden
            UpdatePosition(true);
        }

        // Hide the popup
        public void HidePopup() {
            Visible = false;

            isPopupBeingDragged = false;
            pointerDown = false;
        }

        void OnBeginDrag() {
            isPopupBeingDragged = true;

            // If a smooth movement animation is in progress, cancel it
            moving = false;
        }

        // Reposition the popup
        void OnDrag(Vector2 globalPosition) {
            var localPoint = globalPosition - canvas.GetGlobalRect().Position;
            AnchoredPosition = new Vector2(localPoint.X - canvas.Size.X * 0.5f, canvas.Size.Y * 0.5f - localPoint.Y);
        }

        // Smoothly translate the popup to the nearest edge
        void OnEndDrag() {
            isPopupBeingDragged = false;
            UpdatePosition(false);
        }

        public void UpdatePosition(bool immediately) {
            Vector2 canvasSize = canvas.Size;

            float canvasWidth = canvasSize.X;
            float canvasHeight = canvasSize.Y;
            if (canvasWidth <= 0f || canvasHeight <= 0f) return;

            // normalizedPosition allows us to glue the popup to a specific edge of the screen. It becomes useful when
            // the popup is at the right edge and we switch from portrait screen orientation to landscape screen orientation.
            // Without normalizedPosition, popup could jump to bottom or top edges instead of staying at the right edge
            Vector2 pos = immediately ? new Vector2(normalizedPosition.X * canvasWidth, normalizedPosition.Y * canvasHeight) : AnchoredPosition;

            // Find distances to all four edges
            float distToLeft = canvasWidth * 0.5f + pos.X;
            float distToRight = canvasWidth - distToLeft;

            float distToBottom = canvasHeight * 0.5f + pos.Y;
            float distToTop = canvasHeight - distToBottom;

            float horDistance = Mathf.Min(distToLeft, distToRight);
            float vertDistance = Mathf.Min(distToBottom, distToTop);

            // Find the nearest edge's coordinates
            if (horDistance < vertDistance) {
                if (distToLeft < distToRight)
                    pos = new Vector2(canvasWidth * -0.5f + halfSize.X, pos.Y);
                else
                    pos = new Vector2(canvasWidth * 0.5f - halfSize.X, pos.Y);

                pos.Y = Mathf.Clamp(pos.Y, canvasHeight * -0.5f + halfSize.Y, canvasHeight * 0.5f - halfSize.Y);
            }
            else {
                if (distToBottom < distToTop)
                    pos = new Vector2(pos.X, canvasHeight * -0.5f + halfSize.Y);
                else
                    pos = new Vector2(pos.X, canvasHeight * 0.5f - halfSize.Y);

                pos.X = Mathf.Clamp(pos.X, canvasWidth * -0.5f + halfSize.X, canvasWidth * 0.5f - halfSize.X);
            }

            normalizedPosition = new Vector2(pos.X / canvasWidth, pos.Y / canvasHeight);

            // If another smooth movement animation is in progress, cancel it
            moving = false;

            if (immediately)
                AnchoredPosition = pos;
            else {
                // Smoothly translate the popup to the specified position
                moving = true;
                moveModifier = 0f;
                moveInitialPos = AnchoredPosition;
                moveTargetPos = pos;
            }
        }
    }
}

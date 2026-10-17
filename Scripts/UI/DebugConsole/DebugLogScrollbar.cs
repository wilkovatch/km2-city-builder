using Godot;

namespace IngameDebugConsole {
    // The log list's vertical Scrollbar ("Scrollbar Vertical"): bottom-to-top value (0 = bottom),
    // handle filling the bar's width inside a sliding area inset by 10px, a click on the track moves one
    // page per frame towards the pointer while held
    public partial class DebugLogScrollbar : Control {
        const float SlidingAreaInset = 10f;
        static readonly Color backgroundColor = new Color(0.2f, 0.2f, 0.2f);
        static readonly Color handleColor = new Color(0.2509804f, 0.2509804f, 0.2509804f);

        public float Value { get; private set; } // 0 = bottom, 1 = top
        public float HandleSize { get; private set; } = 1f;

        public event System.Action<float> ValueChanged;
        public event System.Action DragStarted, DragEnded;
        public event System.Action<InputEventMouseButton> WheelScrolled;

        readonly StyleBoxTexture backgroundBox = DebugConsoleUI.Box(DebugConsoleUI.SlicedBackground, backgroundColor);
        readonly StyleBoxTexture handleBox = DebugConsoleUI.Box(DebugConsoleUI.SlicedBackground, handleColor);
        bool hovered, draggingHandle, clickRepeating;
        float dragOffset; // pointer position inside the handle when the drag started

        public DebugLogScrollbar() {
            Name = "Scrollbar Vertical";
            MouseFilter = MouseFilterEnum.Stop;
            MouseEntered += () => { hovered = true; QueueRedraw(); };
            MouseExited += () => { hovered = false; QueueRedraw(); };
        }

        public void SetState(float value, float handleSize) {
            value = Mathf.Clamp(value, 0f, 1f);
            handleSize = Mathf.Clamp(handleSize, 0f, 1f);
            if (Mathf.IsEqualApprox(value, Value) && Mathf.IsEqualApprox(handleSize, HandleSize)) return;
            Value = value;
            HandleSize = handleSize;
            QueueRedraw();
        }

        float SlidingAreaHeight => Mathf.Max(0f, Size.Y - 2 * SlidingAreaInset);

        // The handle's anchors span its share of the sliding area, its 20px sizeDelta extends it by 10px
        // on each side, i.e. to the full bar width and 20px taller than its share
        Rect2 HandleRect() {
            var anchorMin = Value * (1f - HandleSize);
            var anchorMax = anchorMin + HandleSize;
            var top = Size.Y - (2 * SlidingAreaInset + SlidingAreaHeight * anchorMax);
            var bottom = Size.Y - SlidingAreaHeight * anchorMin;
            return new Rect2(0, top, Size.X, bottom - top);
        }

        public override void _Draw() {
            DrawSliced(backgroundBox, new Rect2(Vector2.Zero, Size), backgroundColor);
            var tint = draggingHandle ? DebugConsoleUI.PressedTint : hovered ? DebugConsoleUI.HighlightedTint : Colors.White;
            DrawSliced(handleBox, HandleRect(), handleColor * tint);
        }

        // The borders of a sliced sprite that is smaller than them are shrunk
        void DrawSliced(StyleBoxTexture box, Rect2 rect, Color color) {
            box.SetTextureMarginAll(Mathf.Min(12f, Mathf.Min(rect.Size.X, rect.Size.Y) * 0.5f));
            box.ModulateColor = color;
            DrawStyleBox(box, rect);
        }

        public override void _GuiInput(InputEvent @event) {
            if (@event is InputEventMouseButton mouseButton) {
                if (mouseButton.ButtonIndex == MouseButton.WheelUp || mouseButton.ButtonIndex == MouseButton.WheelDown) {
                    if (mouseButton.Pressed) WheelScrolled?.Invoke(mouseButton); // scroll events reach the scroll rect
                    AcceptEvent();
                    return;
                }
                if (mouseButton.ButtonIndex != MouseButton.Left) return;
                AcceptEvent();
                if (mouseButton.Pressed) {
                    var handle = HandleRect();
                    if (handle.HasPoint(mouseButton.Position)) {
                        draggingHandle = true;
                        dragOffset = mouseButton.Position.Y - handle.Position.Y;
                        DragStarted?.Invoke();
                        QueueRedraw();
                    }
                    else {
                        clickRepeating = true;
                    }
                }
                else {
                    clickRepeating = false;
                    if (draggingHandle) {
                        draggingHandle = false;
                        QueueRedraw();
                        DragEnded?.Invoke();
                    }
                }
            }
            else if (@event is InputEventMouseMotion motion && draggingHandle) {
                AcceptEvent();
                var remainingSize = SlidingAreaHeight * (1f - HandleSize);
                if (remainingSize <= 0f) return;
                // Handle top (from the top of the sliding area) -> value
                var handleTop = motion.Position.Y - dragOffset;
                var value = 1f - handleTop / remainingSize;
                SetValueFromUser(value);
            }
        }

        public override void _Process(double delta) {
            if (!clickRepeating) return;
            if (!Godot.Input.IsMouseButtonPressed(MouseButton.Left)) {
                clickRepeating = false;
                return;
            }
            var mouse = GetLocalMousePosition();
            var handle = HandleRect();
            if (handle.HasPoint(mouse)) return;
            SetValueFromUser(mouse.Y < handle.Position.Y ? Value + HandleSize : Value - HandleSize);
        }

        void SetValueFromUser(float value) {
            value = Mathf.Clamp(value, 0f, 1f);
            if (Mathf.IsEqualApprox(value, Value)) return;
            Value = value;
            QueueRedraw();
            ValueChanged?.Invoke(value);
        }
    }
}

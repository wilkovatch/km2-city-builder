using Godot;

/// <summary>
/// Records the mouse presses that reach the world (registered as an autoload, used by UIDetection).
/// Detects whether the UI is under the mouse position. Checking the hovered control in Godot
/// misses presses that a control or popup consumes while the hovered control does not reflect it,
/// e.g. the item lists of dropdowns, which are separate embedded windows.
/// Presses the GUI consumed never get to _UnhandledInput.
/// </summary>
public partial class UIInputTracker : Node {
    public override void _Ready() {
        ProcessPriority = int.MaxValue; //record the popups still open at the end of the frame
    }

    public override void _Process(double delta) {
        if (AnyPopupOpen()) UIDetection.NotifyPopupOpen();
    }

    public override void _UnhandledInput(InputEvent @event) {
        if (@event is InputEventMouseButton { Pressed: true } mouseButton) {
            UIDetection.NotifyWorldPress(mouseButton.ButtonIndex);
            GameEngine.Input.TrackMouseEvent(mouseButton, true);
        }
    }

    //releases are tracked before the GUI can consume them, so the emulated middle button is released over the UI too
    public override void _Input(InputEvent @event) {
        if (@event is InputEventMouseButton { Pressed: false } mouseButton) {
            GameEngine.Input.TrackMouseEvent(mouseButton, false);
        }
    }

    //a press outside of a dropdown list closes it and then reaches the world, the dropdown blocker consumes it
    bool AnyPopupOpen() {
        foreach (var window in GetViewport().GetEmbeddedSubwindows()) {
            if (window.Visible && (window is PopupMenu || window.Exclusive)) return true;
        }
        return false;
    }
}

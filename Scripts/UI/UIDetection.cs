using System.Collections;
using System.Collections.Generic;
using Godot;

public class UIDetection {
    //process frame of the latest press of each mouse button that the GUI did not consume (see UIInputTracker)
    static readonly Dictionary<MouseButton, ulong> worldPressFrames = new Dictionary<MouseButton, ulong>();
    //latest process frame that ended with a popup open (see UIInputTracker)
    static ulong popupOpenFrame = ulong.MaxValue;

    public static void NotifyWorldPress(MouseButton button) {
        worldPressFrames[button] = Engine.GetProcessFrames();
    }

    public static void NotifyPopupOpen() {
        popupOpenFrame = Engine.GetProcessFrames();
    }

    public static bool OnUI() {
        var loop = Engine.GetMainLoop() as SceneTree;
        if (loop?.CurrentScene == null) return false;
        if (PressConsumedByUI() || NumericFieldDrag.BlocksInput) return true; //the captured mouse of the drag can be anywhere
        var viewport = loop.Root.GetViewport();
        //the item lists of dropdowns are separate embedded windows, the hovered control of the viewport does not include them
        var mouse = (Vector2I)viewport.GetMousePosition();
        foreach (var window in viewport.GetEmbeddedSubwindows()) {
            if (window.Visible && new Rect2I(window.GetPositionWithDecorations(), window.GetSizeWithDecorations()).HasPoint(mouse)) return true;
        }
        var hovered = viewport.GuiGetHoveredControl();
        while (hovered != null) {
            if (hovered.IsVisibleInTree() && hovered.MouseFilter == Control.MouseFilterEnum.Stop) return true;
            hovered = hovered.GetParent() as Control;
        }
        return false;
    }

    //a popup (e.g. the item list of a dropdown) was open at the end of the previous frame, it gets the keys (arrows, escape...)
    public static bool PopupOpen() {
        var frame = Engine.GetProcessFrames();
        return popupOpenFrame == frame - 1 || popupOpenFrame == frame;
    }

    //a mouse button was pressed in this frame, but the press was consumed by a control or a popup
    static bool PressConsumedByUI() {
        var frame = Engine.GetProcessFrames();
        foreach (var entry in GameEngine.Input.mouseMapping) {
            var button = entry.Value;
            if (button != MouseButton.Left && button != MouseButton.Right && button != MouseButton.Middle) continue;
            if (!Godot.Input.IsActionJustPressed(entry.Key.ToString())) continue;
            if (popupOpenFrame == frame - 1) return true;
            if (!worldPressFrames.TryGetValue(button, out var pressFrame) || pressFrame != frame) return true;
        }
        return false;
    }
}

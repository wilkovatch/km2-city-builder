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
        if (PressConsumedByUI()) return true;
        var viewport = loop.Root.GetViewport();
        var hovered = viewport.GuiGetHoveredControl();
        while (hovered != null) {
            if (hovered.IsVisibleInTree() && hovered.MouseFilter == Control.MouseFilterEnum.Stop) return true;
            hovered = hovered.GetParent() as Control;
        }
        return false;
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

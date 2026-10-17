using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;

public partial class Tooltip : Control {
    bool mouseOver = false;
    bool wasStill = false;
    float startTime = 0.0f;
    Vector2 lastPos = new Vector2(0, 0);
    public string tooltip = "tooltip";
    bool tooltipActive = false;
    public Control tooltipObj;

    void Update() {
        if (mouseOver && !Input.GetInputPressed(KeyCommand.Select)) {
            var mouseStil = (lastPos - GetViewport().GetMousePosition()).Length() < 1.0f;
            if (mouseStil && !wasStill) {
                wasStill = true;
                startTime = GameEngine.GetTime();
            }
            else if (!mouseStil || Godot.Input.IsAnythingPressed() || Input.GetInputPressed(KeyCommand.ScrollDown) || Input.GetInputPressed(KeyCommand.ScrollUp)) {
                wasStill = false;
                if (tooltipActive) HideTooltip();
            }
            if (mouseStil && wasStill && GameEngine.GetTime() - startTime > 0.5f && !tooltipActive) {
                ShowTooltip();
            }
        }
        else {
            wasStill = false;
            if (tooltipActive) HideTooltip();
        }
        lastPos = GetViewport().GetMousePosition();
    }

    void HideTooltip() {
        tooltipActive = false;

    }

    void ShowTooltip() {
    }

    private void OnDisable() {
        mouseOver = false;
    }


}

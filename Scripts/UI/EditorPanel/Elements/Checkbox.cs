using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class Checkbox : EditorPanelElement {
        System.Action<bool> action;
        Godot.CheckBox checkbox;

        protected override string TemplateName() {
            return "Checkbox";
        }

        public Checkbox(string title, bool defaultValue, System.Action<bool> action, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            checkbox = GetControl<Godot.CheckBox>();
            checkbox.ButtonPressed = defaultValue;
            checkbox.Toggled += delegate {
                Select();
                parentPanel.CheckFieldsInteractabilityDelayed();
            };
        }

        void Select() {
            if (actionEnabled) action.Invoke(checkbox.ButtonPressed);
        }

        public bool GetValue() {
            return checkbox.ButtonPressed;
        }

        public override void SetValue(object value) {
            if (value is bool) {
                checkbox.ButtonPressed = (bool)value;
            } else if (value is string strValue) {
                if (strValue == "True") checkbox.ButtonPressed = true;
                else if (strValue == "False") checkbox.ButtonPressed = false;
            } else if (value is null) {
                checkbox.ButtonPressed = false;
            }
        }

        public override void SetInteractable(bool interactable) {
            checkbox.Disabled = !interactable;
        }

        public override bool GetInteractable() {
            return !checkbox.Disabled;
        }
    }
}

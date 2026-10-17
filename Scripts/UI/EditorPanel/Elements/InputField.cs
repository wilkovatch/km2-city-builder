using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class InputField : EditorPanelElement {
        System.Action<string> action;
        Godot.LineEdit inputField;

        protected override string TemplateName() {
            return "InputField";
        }

        public InputField(string title, string placeholder, string defaultValue, GameEngine.InputContentType contentType,
            System.Action<string> action, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            inputField = GetControl<Godot.LineEdit>();
            inputField.MaxLength = 255;
            inputField.PlaceholderText = placeholder;
            inputField.Text = defaultValue;
            TextInputFocus.SetContentType(inputField, contentType);
            TextInputFocus.OnEndEdit(inputField, delegate(string _) {
                ChangeValue();
                parentPanel.CheckFieldsInteractabilityDelayed();
            });
        }

        void ChangeValue() {
            if (actionEnabled) action.Invoke(inputField.Text);
        }

        public override void SetInteractable(bool interactable) {
            inputField.Editable = interactable;
        }

        public override bool GetInteractable() {
            return inputField.Editable;
        }

        public override void SetValue(object value) {
            if (value == null) value = "";
            inputField.Text = value.ToString();
        }
    }
}


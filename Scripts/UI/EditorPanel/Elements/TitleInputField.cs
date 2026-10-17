using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class TitleInputField : EditorPanelElement {
        System.Action<string> action;
        Godot.LineEdit inputField;

        protected override string TemplateName() {
            return "TitleInputField";
        }

        protected override string TitlePath() {
            return "";
        }

        public TitleInputField(string placeholder, string defaultValue, GameEngine.InputContentType contentType,
            System.Action<string> action, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base("", parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            inputField = GetControl<Godot.LineEdit>();
            TextInputFocus.SetContentType(inputField, contentType);
            inputField.MaxLength = 255;
            inputField.Text = defaultValue;
            inputField.PlaceholderText = placeholder;
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


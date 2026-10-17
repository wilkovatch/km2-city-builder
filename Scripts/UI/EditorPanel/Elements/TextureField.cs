using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class TextureField : EditorPanelElement {
        System.Action<string> action;
        System.Action buttonAction;
        Godot.LineEdit inputField;
        Godot.Button button;
        TextureRect image;

        protected override string TemplateName() {
            return "TextureField";
        }

        public TextureField(string title, string placeholder, string defaultValue, System.Action<string> action,
            System.Action buttonAction, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            this.buttonAction = buttonAction;
            button = GetControl<Godot.Button>("Browse");
            image = GetControl<TextureRect>("Image");
            inputField = GetControl<Godot.LineEdit>("InputField");
            inputField.MaxLength = 255;
            inputField.Text = defaultValue;
            inputField.PlaceholderText = placeholder;
            TextInputFocus.OnEndEdit(inputField, delegate(string _) {
                ChangeValue();
                parentPanel.CheckFieldsInteractabilityDelayed();
            });
            button.Pressed += delegate {
                Select();
            };
        }

        void Select() {
            if (actionEnabled) buttonAction.Invoke();
        }

        void ChangeValue() {
            UpdatePreview(inputField.Text);
            action.Invoke(inputField.Text);
        }

        public override void SetValue(object value) {
            if (value == null) value = "";
            inputField.Text = value.ToString();
            UpdatePreview(value.ToString());
        }

        void UpdatePreview(string name) {
            // A missing texture renders as a white rect.
            if (string.IsNullOrEmpty(name) || name == "_TRANSPARENT_") { image.Texture = UILayout.WhiteTexture(); return; }
            try {
                image.Texture = MaterialManager.GetTexture(name) ?? UILayout.WhiteTexture();
            } catch (System.Exception) {
                image.Texture = UILayout.WhiteTexture();
            }
        }

        public override void SetInteractable(bool interactable) {
            button.Disabled = !interactable;
            inputField.Editable = interactable;
        }

        public override bool GetInteractable() {
            return !button.Disabled;
        }
    }
}

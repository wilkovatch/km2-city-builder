using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class MeshField : EditorPanelElement {
        System.Action<string> action;
        System.Action buttonAction;
        Godot.LineEdit inputField;
        Godot.Button button;

        protected override string TemplateName() {
            return "MeshField";
        }

        public MeshField(string title, string placeholder, string defaultValue, System.Action<string> action,
            System.Action buttonAction, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            this.buttonAction = buttonAction;
            button = GetControl<Godot.Button>("Browse");
            button.Text = StringManager.Get("MESH_LIST");
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
            action.Invoke(inputField.Text);
        }

        public override void SetValue(object value) {
            if (value == null) value = "";
            inputField.Text = value.ToString();
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

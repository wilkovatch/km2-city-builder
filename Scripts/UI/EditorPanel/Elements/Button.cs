using System;
using Godot;

namespace EditorPanelElements {
    public class Button : EditorPanelElement {
        System.Action action;
        Godot.Button button;

        protected override string TemplateName() {
            return "Button";
        }

        protected override string TitlePath() {
            return "Button/Text";
        }

        public Button(string title, Action action, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            button = GetControl<Godot.Button>();
            button.Pressed += delegate {
                Select();
            };
        }

        void Select() {
            if (actionEnabled) action.Invoke();
        }

        public override void SetInteractable(bool interactable) {
            button.Disabled = !interactable;
        }

        public override bool GetInteractable() {
            return !button.Disabled;
        }
    }
}

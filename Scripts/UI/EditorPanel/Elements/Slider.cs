using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class Slider : EditorPanelElement {
        System.Action<float> action;
        Godot.Slider slider;

        protected override string TemplateName() {
            return "Slider";
        }

        public Slider(string title, float min, float max, System.Action<float> action, Control parent, Vector2 pos, float? defaultValue = null,
            float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            slider = GetControl<Godot.Slider>();
            slider.MinValue = min;
            slider.MaxValue = max;
            if (defaultValue.HasValue) slider.Value = defaultValue.Value;
            slider.ValueChanged += delegate {
                Select();
                parentPanel.CheckFieldsInteractabilityDelayed();
            };
        }

        public override void SetInteractable(bool interactable) {
            slider.Editable = interactable;
        }

        public override bool GetInteractable() {
            return slider.Editable;
        }

        void Select() {
            if (actionEnabled) action.Invoke((float)slider.Value);
        }

        public override void SetValue(object value) {
            if (value is float) slider.Value = (float)value;
        }
    }
}

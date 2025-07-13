using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class Slider : EditorPanelElement {
        System.Action<float> action;
        UnityEngine.UI.Slider slider;

        protected override string TemplateName() {
            return "Slider";
        }

        public Slider(string title, float min, float max, System.Action<float> action, GameObject parent, Vector2 pos, float? defaultValue = null,
            float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            slider = obj.transform.Find("Slider").GetComponent<UnityEngine.UI.Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            if (defaultValue.HasValue) slider.value = defaultValue.Value;
            slider.onValueChanged.AddListener(delegate {
                Select();
                parentPanel.CheckFieldsInteractabilityDelayed();
            });
        }

        public override void SetInteractable(bool interactable) {
            slider.interactable = interactable;
        }

        public override bool GetInteractable() {
            return slider.interactable;
        }

        void Select() {
            if (actionEnabled) action.Invoke(slider.value);
        }

        public override void SetValue(object value) {
            if (value is float) slider.value = (float)value;
        }
    }
}
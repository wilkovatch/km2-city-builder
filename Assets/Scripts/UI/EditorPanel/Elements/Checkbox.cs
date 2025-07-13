using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class Checkbox : EditorPanelElement {
        System.Action<bool> action;
        UnityEngine.UI.Toggle checkbox;

        protected override string TemplateName() {
            return "Checkbox";
        }

        public Checkbox(string title, bool defaultValue, System.Action<bool> action, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            checkbox = obj.transform.Find("Toggle").GetComponent<UnityEngine.UI.Toggle>();
            checkbox.isOn = defaultValue;
            checkbox.onValueChanged.AddListener(delegate {
                Select();
                parentPanel.CheckFieldsInteractabilityDelayed();
            });
        }

        void Select() {
            if (actionEnabled) action.Invoke(checkbox.isOn);
        }

        public bool GetValue() {
            return checkbox.isOn;
        }

        public override void SetValue(object value) {
            if (value is bool) {
                checkbox.isOn = (bool)value;
            } else if (value is string strValue) {
                if (strValue == "True") checkbox.isOn = true;
                else if (strValue == "False") checkbox.isOn = false;
            } else if (value is null) {
                checkbox.isOn = false;
            }
        }

        public override void SetInteractable(bool interactable) {
            checkbox.interactable = interactable;
        }

        public override bool GetInteractable() {
            return checkbox.interactable;
        }
    }
}
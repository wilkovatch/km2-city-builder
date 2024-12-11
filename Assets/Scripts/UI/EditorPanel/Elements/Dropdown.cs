using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class Dropdown : EditorPanelElement {
        System.Action<int> action;
        UnityEngine.UI.Dropdown dropdown;

        protected override string TemplateName() {
            return "Dropdown";
        }

        public Dropdown(string title, List<string> options, System.Action<int> action, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            dropdown = obj.transform.Find("Dropdown").GetComponent<UnityEngine.UI.Dropdown>();
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            var template = obj.transform.Find("Dropdown/Template");
            var labelItem = template.Find("Viewport/Content/Item/Item Label");
            var labelText = labelItem.GetComponent<Text>();
            dropdown.onValueChanged.AddListener(delegate {
                ChangeValue();
            });
        }

        void ChangeValue() {
            if (actionEnabled) action.Invoke(dropdown.value);
        }

        public override void SetValue(object value) {
            if (value is null) {
                dropdown.value = 0;
            } else if (value is long valueL) {
                dropdown.value = (int)valueL;
            } else {
                dropdown.value = (int)value;
            }
        }

        public void SetOptions(List<string> newOptions) {
            dropdown.ClearOptions();
            dropdown.AddOptions(newOptions);
        }

        public override void SetInteractable(bool interactable) {
            dropdown.interactable = interactable;
        }

        public override bool GetInteractable() {
            return dropdown.interactable;
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class InputField : EditorPanelElement {
        System.Action<string> action;
        UnityEngine.UI.InputField inputField;

        protected override string TemplateName() {
            return "InputField";
        }

        public InputField(string title, string placeholder, string defaultValue, UnityEngine.UI.InputField.ContentType contentType,
            System.Action<string> action, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            inputField = obj.transform.Find("InputField").GetComponent<UnityEngine.UI.InputField>();
            inputField.contentType = contentType;
            inputField.characterLimit = 255;
            inputField.text = defaultValue;
            inputField.placeholder.GetComponent<Text>().text = placeholder;
            inputField.onEndEdit.AddListener(delegate {
                ChangeValue();
            });
        }

        void ChangeValue() {
            if (actionEnabled) action.Invoke(inputField.text);
        }

        public override void SetInteractable(bool interactable) {
            inputField.interactable = interactable;
        }

        public override bool GetInteractable() {
            return inputField.interactable;
        }

        public override void SetValue(object value) {
            if (value == null) value = "";
            inputField.text = value.ToString();
        }
    }
}
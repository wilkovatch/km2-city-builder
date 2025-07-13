using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class MeshField : EditorPanelElement {
        System.Action<string> action;
        System.Action buttonAction;
        UnityEngine.UI.InputField inputField;
        UnityEngine.UI.Button button;

        protected override string TemplateName() {
            return "MeshField";
        }

        public MeshField(string title, string placeholder, string defaultValue, System.Action<string> action,
            System.Action buttonAction, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            this.buttonAction = buttonAction;
            button = obj.transform.Find("Button").GetComponent<UnityEngine.UI.Button>();
            obj.transform.Find("Button").GetComponentInChildren<Text>().text = StringManager.Get("MESH_LIST");
            inputField = obj.transform.Find("InputField").GetComponent<UnityEngine.UI.InputField>();
            inputField.contentType = UnityEngine.UI.InputField.ContentType.Standard;
            inputField.characterLimit = 255;
            inputField.text = defaultValue;
            inputField.placeholder.GetComponent<Text>().text = placeholder;
            inputField.onEndEdit.AddListener(delegate {
                ChangeValue();
                parentPanel.CheckFieldsInteractabilityDelayed();
            });
            button.onClick.AddListener(delegate {
                Select();
            });
        }

        void Select() {
            if (actionEnabled) buttonAction.Invoke();
        }

        void ChangeValue() {
            action.Invoke(inputField.text);
        }

        public override void SetValue(object value) {
            if (value == null) value = "";
            inputField.text = value.ToString();
        }

        public override void SetInteractable(bool interactable) {
            button.interactable = interactable;
            inputField.interactable = interactable;
        }

        public override bool GetInteractable() {
            return button.interactable;
        }
    }
}
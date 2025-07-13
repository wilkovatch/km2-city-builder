using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class TextureField : EditorPanelElement {
        System.Action<string> action;
        System.Action buttonAction;
        UnityEngine.UI.InputField inputField;
        UnityEngine.UI.Button button;
        UnityEngine.UI.Image image;

        protected override string TemplateName() {
            return "TextureField";
        }

        public TextureField(string title, string placeholder, string defaultValue, System.Action<string> action,
            System.Action buttonAction, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            this.buttonAction = buttonAction;
            button = obj.transform.Find("Button").Find("Image").GetComponent<UnityEngine.UI.Button>();
            image = obj.transform.Find("Button").Find("Image").GetComponent<UnityEngine.UI.Image>();
            obj.transform.Find("Button").Find("Checkerboard").GetComponent<UnityEngine.UI.Image>().sprite = MaterialManager.GetInstance().GetCheckerboard();
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
            image.sprite = MaterialManager.GetSprite(inputField.text);
            action.Invoke(inputField.text);
        }

        public override void SetValue(object value) {
            if (value == null) value = "";
            inputField.text = value.ToString();
            image.sprite = MaterialManager.GetSprite(value.ToString());
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
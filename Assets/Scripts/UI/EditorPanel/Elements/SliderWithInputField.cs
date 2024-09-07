using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class SliderWithInputField : EditorPanelElement {
        System.Action<float> action;
        UnityEngine.UI.Slider slider;
        UnityEngine.UI.InputField inputField;
        bool manualInput = false;

        protected override string TemplateName() {
            return "SliderWithInputField";
        }

        public SliderWithInputField(string title, float min, float max, System.Action<float> action, GameObject parent, Vector2 pos, float? defaultValue = null,
            float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;

            //slider
            slider = obj.transform.Find("Slider").GetComponent<UnityEngine.UI.Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            if (defaultValue.HasValue) slider.value = defaultValue.Value;
            slider.onValueChanged.AddListener(delegate {
                SliderChanged();
            });

            //input field
            inputField = obj.transform.Find("InputField").GetComponent<UnityEngine.UI.InputField>();
            inputField.contentType = UnityEngine.UI.InputField.ContentType.DecimalNumber; //TODO: integer too (the slider needs to be adjusted too)
            inputField.characterLimit = 255;
            inputField.text = slider.value.ToString();
            inputField.placeholder.GetComponent<Text>().text = "";
            inputField.onEndEdit.AddListener(delegate {
                InputFieldChanged();
            });
        }

        public override void SetInteractable(bool interactable) {
            slider.interactable = interactable;
        }

        public override bool GetInteractable() {
            return slider.interactable;
        }

        void SliderChanged() {
            if (!manualInput) inputField.text = slider.value.ToString();
            manualInput = false;
            if (actionEnabled) action.Invoke(float.Parse(inputField.text)); //get the input field to allow out of bound values
        }

        void InputFieldChanged() {
            manualInput = true;
            var newVal = Mathf.Clamp(float.Parse(inputField.text), slider.minValue, slider.maxValue); //the actual value that will be set anyway
            if (newVal != slider.value) {
                slider.value = newVal;
                //do nothing because SliderChanged will be triggered
            } else {
                SliderChanged(); //we need to trigger it manually (since it didn't change (can happen if out of bounds))
            }
        }

        public override void SetValue(object value) {
            if (value is float floatval) {
                slider.value = floatval;
                //input field will be changed in SliderChanged
            }
        }
    }
}
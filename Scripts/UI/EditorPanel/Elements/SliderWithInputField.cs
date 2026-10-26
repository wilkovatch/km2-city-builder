using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class SliderWithInputField : EditorPanelElement {
        System.Action<float> action;
        Godot.Slider slider;
        Godot.LineEdit inputField;
        bool manualInput = false;

        protected override string TemplateName() {
            return "SliderWithInputField";
        }

        public SliderWithInputField(string title, float min, float max, System.Action<float> action, Control parent, Vector2 pos, float? defaultValue = null,
            float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;

            //slider
            slider = GetControl<Godot.Slider>("Slider");
            slider.MinValue = min;
            slider.MaxValue = max;
            if (defaultValue.HasValue) slider.Value = defaultValue.Value;
            slider.ValueChanged += delegate {
                SliderChanged();
                parentPanel.CheckFieldsInteractabilityDelayed();
            };

            //input field
            inputField = GetControl<Godot.LineEdit>("InputField");
            inputField.MaxLength = 255;
            inputField.Text = ((float)slider.Value).ToString();
            inputField.PlaceholderText = "";
            TextInputFocus.SetContentType(inputField, GameEngine.InputContentType.Decimal);
            TextInputFocus.OnEndEdit(inputField, delegate(string _) {
                InputFieldChanged();
                parentPanel.CheckFieldsInteractabilityDelayed();
            });
            NumericFieldDrag.Attach(inputField, false, delegate {
                InputFieldChanged();
                parentPanel.CheckFieldsInteractabilityDelayed();
            });
        }

        public override void SetInteractable(bool interactable) {
            slider.Editable = interactable;
            inputField.Editable = interactable;
        }

        public override bool GetInteractable() {
            return slider.Editable;
        }

        void SliderChanged() {
            if (!manualInput) inputField.Text = ((float)slider.Value).ToString();
            manualInput = false;
            if (actionEnabled) action.Invoke(float.Parse(inputField.Text)); //get the input field to allow out of bound values
        }

        void InputFieldChanged() {
            if (!float.TryParse(inputField.Text, out var inputVal)) { //empty or incomplete input (e.g. "-"): restore the current value
                inputField.Text = ((float)slider.Value).ToString();
                return;
            }
            manualInput = true;
            var newVal = Mathf.Clamp(inputVal, slider.MinValue, slider.MaxValue); //the actual value that will be set anyway
            if (newVal != slider.Value) {
                slider.Value = newVal;
                //do nothing because SliderChanged will be triggered
            } else {
                SliderChanged(); //we need to trigger it manually (since it didn't change (can happen if out of bounds))
            }
        }

        public override void SetValue(object value) {
            if (value is float floatval) {
                slider.Value = floatval;
                //input field will be changed in SliderChanged
            }
        }
    }
}

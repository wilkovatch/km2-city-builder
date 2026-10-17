using System.Collections;
using System.Collections.Generic;
using Godot;
using GameObject = Godot.Control;

namespace EditorPanelElements {
    public class VectorInputField: EditorPanelElement {
        System.Action<Vector3> vecAction = null;
        System.Action<Quaternion> rotAction = null;
        Godot.LineEdit xField;
        Godot.LineEdit yField;
        Godot.LineEdit zField;

        protected override string TemplateName() {
            return "VectorInputField";
        }

        public VectorInputField(string title, System.Action<Vector3> vecAction, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.vecAction = vecAction;
            BindFields();
        }

        public VectorInputField(string title, System.Action<Quaternion> rotAction, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.rotAction = rotAction;
            BindFields();
        }

        void BindFields() {
            xField = GetControl<LineEdit>("X");
            yField = GetControl<LineEdit>("Y");
            zField = GetControl<LineEdit>("Z");
            foreach (var field in new[] { xField, yField, zField }) {
                TextInputFocus.SetContentType(field, GameEngine.InputContentType.Decimal);
                TextInputFocus.OnEndEdit(field, delegate(string _) {
                    ChangeValue();
                    parentPanel.CheckFieldsInteractabilityDelayed();
                });
            }
        }

        void ChangeValue() {
            var x = ParseAxis(xField.Text);
            var y = ParseAxis(yField.Text);
            var z = ParseAxis(zField.Text);
            if (vecAction != null) {
                var vec = new Vector3(x, y, z);
                if (actionEnabled) vecAction.Invoke(vec);
            } else if (rotAction != null) {
                if (actionEnabled) rotAction.Invoke(GameEngine.Quaternion.Euler(x, y, z));
            }
        }

        //empty or incomplete input (e.g. "-") counts as 0 instead of throwing
        static float ParseAxis(string text) {
            return float.TryParse(text, out var value) ? value : 0;
        }

        public override void SetValue(object value) {
            if (value is Vector3) {
                var vec = (Vector3)value;
                xField.Text = vec[0].ToString("0.#####");
                yField.Text = vec[1].ToString("0.#####");
                zField.Text = vec[2].ToString("0.#####");
            } else if (value is Quaternion) {
                var vec = ((Quaternion)value).GetEuler();
                var x = Mathf.RadToDeg(vec[0]);
                var y = Mathf.RadToDeg(vec[1]);
                var z = Mathf.RadToDeg(vec[2]);
                if (x > 180) x -= 360;
                if (y > 180) y -= 360;
                if (z > 180) z -= 360;
                xField.Text = x.ToString("0.#####");
                yField.Text = y.ToString("0.#####");
                zField.Text = z.ToString("0.#####");
            }
        }

        public override void SetInteractable(bool interactable) {
            xField.Editable = interactable;
            yField.Editable = interactable;
            zField.Editable = interactable;
        }

        public void SetInteractable(bool x, bool y, bool z) {
            xField.Editable = x;
            yField.Editable = y;
            zField.Editable = z;
        }

        public override bool GetInteractable() {
            return xField.Editable || yField.Editable || zField.Editable;
        }
    }
}


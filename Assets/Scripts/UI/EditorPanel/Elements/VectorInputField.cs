using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class VectorInputField: EditorPanelElement {
        System.Action<Vector3> vecAction = null;
        System.Action<Quaternion> rotAction = null;
        UnityEngine.UI.InputField xField;
        UnityEngine.UI.InputField yField;
        UnityEngine.UI.InputField zField;

        protected override string TemplateName() {
            return "VectorInputField";
        }

        public VectorInputField(string title, System.Action<Vector3> vecAction, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.vecAction = vecAction;
            var inputFields = obj.transform.Find("VectorInputField");
            GetField(inputFields, ref xField, "X");
            GetField(inputFields, ref yField, "Y");
            GetField(inputFields, ref zField, "Z");
        }

        public VectorInputField(string title, System.Action<Quaternion> rotAction, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.rotAction = rotAction;
            var inputFields = obj.transform.Find("VectorInputField");
            GetField(inputFields, ref xField, "X");
            GetField(inputFields, ref yField, "Y");
            GetField(inputFields, ref zField, "Z");
        }

        void GetField(Transform inputFields, ref UnityEngine.UI.InputField field, string name) {
            field = inputFields.Find(name).Find("Field").GetComponent<UnityEngine.UI.InputField>();
            field.contentType = UnityEngine.UI.InputField.ContentType.DecimalNumber;
            field.onEndEdit.AddListener(delegate {
                ChangeValue();
            });
        }

        void ChangeValue() {
            var x = xField.text == "" ? 0 : float.Parse(xField.text);
            var y = yField.text == "" ? 0 : float.Parse(yField.text);
            var z = zField.text == "" ? 0 : float.Parse(zField.text);
            var vec = new Vector3(x, y, z);
            if (vecAction != null) {
                if (actionEnabled) vecAction.Invoke(vec);
            } else if (rotAction != null) {
                if (actionEnabled) rotAction.Invoke(Quaternion.Euler(vec));
            }
        }

        public override void SetValue(object value) {
            if (value is Vector3) {
                var vec = (Vector3)value;
                xField.text = vec.x.ToString("{0.#####}");
                yField.text = vec.y.ToString("{0.#####}");
                zField.text = vec.z.ToString("{0.#####}");
            } else if (value is Quaternion) {
                var vec = ((Quaternion)value).eulerAngles;
                var x = vec.x;
                var y = vec.y;
                var z = vec.z;
                if (x > 180) x -= 360;
                if (y > 180) y -= 360;
                if (z > 180) z -= 360;
                xField.text = x.ToString("{0.#####}");
                yField.text = y.ToString("{0.#####}");
                zField.text = z.ToString("{0.#####}");
            }
        }

        public override void SetInteractable(bool interactable) {
            xField.interactable = interactable;
            yField.interactable = interactable;
            zField.interactable = interactable;
        }

        public override bool GetInteractable() {
            return xField.interactable;
        }
    }
}

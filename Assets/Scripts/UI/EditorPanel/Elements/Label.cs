using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class Label : EditorPanelElement {
        protected override string TemplateName() {
            return "Label";
        }

        public Label(string title, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null, float heightFactor = 1.0f)
            : base(title, parent, pos, widthFactor, tooltip, tag, heightFactor) { }

        public override void SetValue(object value) {
            if (value is string str) {
                obj.transform.Find(TitlePath()).GetComponent<Text>().text = str;
            }
        }
    }
}
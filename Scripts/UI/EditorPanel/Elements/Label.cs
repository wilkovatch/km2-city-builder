using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class Label : EditorPanelElement {
        protected override string TemplateName() {
            return "Label";
        }

        public Label(string title, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null, float heightFactor = 1.0f)
            : base(title, parent, pos, widthFactor, tooltip, tag, heightFactor) { }

        public override void SetValue(object value) {
            if (value is string str) {
                if (GetControl<Godot.Label>() is Godot.Label label) {
                    label.Text = str;
                    TextFit.Refit(label);
                }
            }
        }
    }
}

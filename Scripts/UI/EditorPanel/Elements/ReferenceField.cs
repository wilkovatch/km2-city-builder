using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    //A reference parameter: the name of the element referenced, a button to pick it with a click on the city (pressed
    //again to cancel) and one to clear it
    public class ReferenceField : EditorPanelElement {
        System.Func<object, string> getName;
        System.Action pickAction;
        System.Action clearAction;
        System.Func<bool> isPicking;
        Godot.LineEdit nameField;
        Godot.Button pickButton;
        Godot.Button clearButton;

        protected override string TemplateName() {
            return "ReferenceField";
        }

        //getName: the text shown for a value of the parameter, isPicking: whether the pick of this field is running
        public ReferenceField(string title, System.Func<object, string> getName, System.Action pickAction, System.Action clearAction,
            System.Func<bool> isPicking, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.getName = getName;
            this.pickAction = pickAction;
            this.clearAction = clearAction;
            this.isPicking = isPicking;
            nameField = GetControl<Godot.LineEdit>("InputField");
            pickButton = GetControl<Godot.Button>("Pick");
            clearButton = GetControl<Godot.Button>("Clear");
            pickButton.Text = StringManager.Get("REF_PICK");
            UILayout.SetTooltipRecursive(clearButton, StringManager.Get("REF_CLEAR"));
            pickButton.Pressed += delegate {
                if (actionEnabled) pickAction.Invoke();
            };
            clearButton.Pressed += delegate {
                if (actionEnabled) clearAction.Invoke();
            };
        }

        public override void SetValue(object value) {
            var picking = isPicking.Invoke();
            nameField.Text = picking ? StringManager.Get("REF_PICKING") : getName.Invoke(value);
            pickButton.Text = StringManager.Get(picking ? "REF_CANCEL" : "REF_PICK");
            TextFit.Refit(pickButton);
        }

        public string GetShownName() {
            return nameField.Text;
        }

        public void Pick() {
            pickAction.Invoke();
        }

        public void Clear() {
            clearAction.Invoke();
        }

        public override void SetInteractable(bool interactable) {
            pickButton.Disabled = !interactable;
            clearButton.Disabled = !interactable;
        }

        public override bool GetInteractable() {
            return !pickButton.Disabled;
        }
    }
}

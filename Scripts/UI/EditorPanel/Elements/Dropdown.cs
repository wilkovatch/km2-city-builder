using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class Dropdown : EditorPanelElement {
        System.Action<int> action;
        OptionButton dropdown;

        protected override string TemplateName() {
            return "Dropdown";
        }

        public Dropdown(string title, List<string> options, System.Action<int> action, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            dropdown = GetControl<OptionButton>();
            SetOptions(options);
            dropdown.ItemSelected += delegate(long index) {
                OnValueChanged();
            };
        }

        void OnValueChanged() {
            ChangeValue();
            parentPanel.CheckFieldsInteractabilityDelayed();
        }

        void ChangeValue() {
            if (actionEnabled && dropdown.Selected >= 0) action?.Invoke(dropdown.Selected);
        }

        public override void SetValue(object value) {
            int index;
            if (value == null) index = 0;
            else if (!int.TryParse(value.ToString(), out index)) return;
            //notify only on an actual change (OptionButton.Select does not emit ItemSelected)
            if (dropdown.ItemCount == 0 || index == dropdown.Selected) return;
            dropdown.Select(Mathf.Clamp(index, 0, dropdown.ItemCount - 1));
            OnValueChanged();
        }

        public int GetIndex() {
            return dropdown.Selected;
        }

        //picking the selected option again calls the action too
        public void SetAllowReselect(bool allow) {
            dropdown.AllowReselect = allow;
        }

        public void SetOptions(List<string> newOptions) {
            dropdown.Clear();
            foreach (var option in newOptions) dropdown.AddItem(option);
        }

        public override void SetInteractable(bool interactable) {
            dropdown.Disabled = !interactable;
        }

        public override bool GetInteractable() {
            return !dropdown.Disabled;
        }
    }
}

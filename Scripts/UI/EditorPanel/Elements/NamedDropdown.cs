using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    //a dropdown whose value is the name of the option chosen instead of its index (the first one if the name is unknown)
    public class NamedDropdown : Dropdown {
        readonly List<string> names;

        public NamedDropdown(string title, List<string> names, System.Action<string> action, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, names, i => action?.Invoke(i >= 0 && i < names.Count ? names[i] : null), parent, pos, widthFactor, tooltip, tag) {
            this.names = names;
        }

        public override void SetValue(object value) {
            var index = value is string name ? names.IndexOf(name) : -1;
            base.SetValue(index < 0 ? 0 : index);
        }
    }
}

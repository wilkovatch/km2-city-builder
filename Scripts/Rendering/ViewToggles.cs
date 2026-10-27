using System.Collections.Generic;

namespace Rendering {
    //The visibility switches defined by the core (viewToggles in its settings.json), listed in the View menu: the lines of
    //the low-level elements with a toggle are hidden while it's off (unless the element is edited). Saved in the city
    //preferences
    public static class ViewToggles {
        public class Toggle {
            public string name;
            public string label;
            public bool @default = true;
        }

        static List<Toggle> toggles;
        static string togglesCore;

        //none without a city (the core isn't known yet)
        public static List<Toggle> List() {
            if (PreferencesManager.workingDirectory == "") return new List<Toggle>();
            var core = PathHelper.FilesPath();
            if (toggles == null || togglesCore != core) {
                toggles = CoreManager.GetList("viewToggles", new List<Toggle>()).FindAll(t => !string.IsNullOrEmpty(t.name));
                togglesCore = core;
            }
            return toggles;
        }

        static Toggle Find(string name) {
            return List().Find(t => t.name == name);
        }

        //a toggle the core doesn't define is on
        public static bool IsOn(string name) {
            if (name == null) return true;
            var toggle = Find(name);
            if (toggle == null) return true;
            return PreferencesManager.Get("viewToggle." + name, toggle.@default);
        }

        //false if the core doesn't define it or there is no city
        public static bool Set(string name, bool on) {
            if (Find(name) == null || PreferencesManager.workingDirectory == "") return false;
            PreferencesManager.Set("viewToggle." + name, on);
            return true;
        }
    }
}

using System.Collections.Generic;

namespace Operations {
    //Presets of the city (presets.json), by category ("road", "intersection", "building", ...)
    public static class Presets {
        public const string DefaultName = "Default";

        //a copy of the preset, flagged as changed so that the element using it is rebuilt (null if not found)
        public static ObjectState Get(string category, string name) {
            if (PresetManager.GetPresetByName(name, category) == null) return null;
            return PresetManager.GetPresetCloneByName(name, category);
        }

        //the presets of the category whose type is one of the allowed ones (all of them if none)
        public static List<ObjectState> Filter(string category, List<string> allowedTypes) {
            var list = PresetManager.GetPresets().GetList(category);
            var res = new List<ObjectState>();
            foreach (var preset in list) {
                if (allowedTypes == null || allowedTypes.Count == 0 || allowedTypes.Contains(preset.Str("type"))) res.Add(preset);
            }
            return res;
        }

        //names the state after the preset and saves a copy of it (overwriting a preset with the same name)
        public static void Save(ObjectState state, string category, string name) {
            state.Name = name;
            PresetManager.SavePreset((ObjectState)state.Clone(), category);
        }

        public static bool Exists(string category, string name) {
            return PresetManager.DoesPresetExist(name, category);
        }

        //deletes the preset (the default one can't be), the elements using it are unlinked from it
        public static bool Delete(ElementManager manager, string category, string name) {
            if (name == null || name == DefaultName || !Exists(category, name)) return false;
            PresetManager.DeletePreset(name, category);
            Unlink(manager, category, name);
            return true;
        }

        //gives the elements using the preset its current values (roads and intersections only)
        public static bool Sync(ElementManager manager, string category, string name) {
            var curPreset = PresetManager.GetPresetByName(name, category);
            switch (category) {
                case "road":
                    foreach (var road in manager.roads) {
                        if (road.state.Name == name) {
                            var newState = (ObjectState)curPreset.Clone();
                            newState.FlagAsChanged();
                            road.SetState(newState);
                        }
                    }
                    return true;
                case "intersection":
                    foreach (var intersection in manager.intersections) {
                        if (intersection.state.Name == name) {
                            var newState = (ObjectState)curPreset.Clone();
                            newState.FlagAsChanged();
                            intersection.SetState(newState);
                        }
                    }
                    return true;
                default:
                    GameEngine.LogWarning("Syncing presets for the type '" + category + "' is unimplemented");
                    return false;
            }
        }

        static void Unlink(ElementManager manager, string category, string name) {
            switch (category) {
                case "road":
                    foreach (var road in manager.roads) {
                        if (road.state.Name == name) road.state.Name = "";
                    }
                    break;
                case "intersection":
                    foreach (var intersection in manager.intersections) {
                        if (intersection.state.Name == name) intersection.state.Name = "";
                    }
                    break;
                default:
                    GameEngine.LogWarning("Clearing presets for the type '" + category + "' is unimplemented");
                    break;
            }
        }
    }
}

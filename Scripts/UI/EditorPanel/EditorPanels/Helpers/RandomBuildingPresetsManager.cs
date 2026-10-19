using System.Collections.Generic;

namespace EditorPanels.Helpers {
    public class RandomBuildingPresetsManager {
        //the names of the building presets to choose from (their values are read when used, so they follow the saved presets)
        public static List<string> loopPresets = new List<string>();
        public static float minLoopLength = 15, maxLoopLength = 15;
        public static bool subdivide = true;

        //removes the presets that don't exist anymore (deleted, or another city loaded)
        public static void RemoveMissing() {
            loopPresets.RemoveAll(x => !Operations.Presets.Exists("building", x));
        }

        //the current values of the presets of the list
        public static List<ObjectState> GetLoopPresetStates() {
            RemoveMissing();
            var res = new List<ObjectState>();
            foreach (var name in loopPresets) res.Add(Operations.Presets.Get("building", name));
            return res;
        }
    }
}

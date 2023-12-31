using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorPanels.Helpers {
    public class RandomBuildingPresetsManager {
        public static List<ObjectState> loopPresets = new List<ObjectState>();
        public static float minLoopLength = 15, maxLoopLength = 15;
        public static bool subdivide = true;
    }
}

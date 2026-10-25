using System.Collections.Generic;

namespace Rendering {
    //The render layers of the program by name (see GameEngine.GameObject.SetLayer: layer n is bit n), for the renders
    public static class RenderLayers {
        public static readonly Dictionary<string, uint> Groups = new Dictionary<string, uint> {
            //buildings and the other objects on the default layer
            ["buildings"] = 1u << 0,
            ["terrain"] = 1u << 3,
            //roads, intersections, terrain patches
            ["geometry"] = 1u << 7,
            //mesh instances that aren't props
            ["meshes"] = 1u << 11,
            ["props"] = 1u << 13,
            //the editing helpers: control points, terrain points and anchors, point links, split lines
            ["helpers"] = (1u << 8) | (1u << 9) | (1u << 10) | (1u << 12) | (1u << 14),
        };

        //the city as exported (the sun and the highlights are on the render-only layer)
        public static readonly uint City = Groups["buildings"] | Groups["terrain"] | Groups["geometry"] | Groups["meshes"] | Groups["props"]
            | GameEngine.Camera.RenderOnlyLayer;

        //the mask of the named groups (null or empty: the city)
        public static uint MaskOf(IEnumerable<string> names, out string unknown) {
            unknown = null;
            if (names == null) return City;
            uint res = GameEngine.Camera.RenderOnlyLayer;
            var any = false;
            foreach (var name in names) {
                if (!Groups.TryGetValue(name, out var mask)) {
                    unknown = name;
                    return 0;
                }
                res |= mask;
                any = true;
            }
            return any ? res : City;
        }
    }
}

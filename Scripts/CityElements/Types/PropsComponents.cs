using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace CityElements.Types {
    //A kind of props: its parameters and its panel come from the core, like any other element, and a Lua callback
    //(placement) gives its placements. A type without one is a group: its props are those of the elements of its array
    //parameters with propsTypes.
    public class PropsType {
        public string label;
        public bool nestedOnly; //only as an element of a group, not directly in a props container of an element
        public bool lineOnly; //along a line: on a prop placer spline the bounds of the strip have no effect
        public bool authoredLine; //on a prop placer spline, along the spline as authored: the ground gives only the height of each prop
        public string placement; //Lua callback "<module>.<function>" (variables, context) -> the placements
        public bool meshTransform; //place the meshes with the transform given by the core
        public bool autoYOffset = true; //without a core transform, put the lowest point of the mesh on the line
        public ParameterContainer parametersInfo;
        public TabElement[] uiInfo;
    }

    public class MeshInstancePlacementRules {
        public string autoYOffset;
        public string layer;
        public string placerLayerMask;
        public string snapToSurface; //put back on the placer layers after being moved with the gizmo (optional)
        public string meshTransform; //place the mesh with the transform given by the core, instead of autoYOffset (optional)
    }

    public class MeshInstanceSettings {
        public ParameterContainer parametersInfo;
        public TabElement[] uiInfo;
        public MeshInstancePlacementRules placementRules;
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace CityElements.Types {
    public class PropsElementPlacementRules {
        public string meshIndex;
        public string whileCondition;
        public string ifCondition;
        public string xPos;
        public string zPos;
        public string forward;
        public string autoYOffset; //put the lowest point of the mesh on the line (optional, default true)
        public string meshTransform; //place the mesh with the transform given by the core (optional, default false)
    }

    public class PropsElementType {
        public string label;
        public int maxMeshes;
        public ParameterContainer parametersInfo;
        public TabElement[] uiInfo;
        public PropsElementPlacementRules placementRules;
    }

    public class PropsContainerParameter {
        public string name;
        public string[] allowedTypes;
        public int maxNumber;
    }

    public class PropsContainerType {
        public string label;
        public PropsContainerParameter[] parameters;
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

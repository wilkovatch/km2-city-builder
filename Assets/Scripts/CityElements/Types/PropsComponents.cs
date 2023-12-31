using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityElements.Types {
    public class PropsElementPlacementRules {
        public string meshIndex;
        public string whileCondition;
        public string ifCondition;
        public string xPos;
        public string zPos;
        public string forward;
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
    }

    public class MeshInstanceSettings {
        public ParameterContainer parametersInfo;
        public TabElement[] uiInfo;
        public MeshInstancePlacementRules placementRules;
    }
}

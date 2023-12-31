using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CityElements.Types {
    public class Parameter {
        public string name;
        public string label;
        public string placeholder;
        public string tooltip;
        public string type;
        public string container;
        public string[] enumLabels;
        public bool instanceSpecific;
        
        public string fullName() {
            return container != null ? container + "_" + name : name;
        }
    }

    public struct Definition {
        public string name;
        public string type;
        public string value;
    }

    public class InternalParameterSettings {
        public string name;
        public bool enabled;
    }

    public class ParameterContainer {
        public Parameter[] parameters;
        public InternalParameterSettings[] internalParametersSettings;
        public string[] loopVariables;
        public Definition[] staticDefinitions;
        public Definition[] dynamicDefinitions;
    }

    public class ComponentInfo {
        public string name;
        public string condition;
    }

    public abstract class SubObjectWithLocalDefinitions {
        public Definition[] localDefinitions;
    }

    public struct TabElement {
        public string name;
        public float width;
    }

    public struct Tab {
        public string label;
        public TabElement[] elements;
    }

    public struct Panel {
        public string label;
        public float menuWidth;
        public Tab[] tabs;
    }

    public interface ITypeWithUI {
        public Panel GetUI();
        public ParameterContainer GetParameters();
    }
}

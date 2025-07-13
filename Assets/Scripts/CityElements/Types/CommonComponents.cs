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
        public string customElementType;
        public object defaultValueInArray;
        public string container;
        public string[] enumLabels;
        public bool instanceSpecific;
        public ArrayProperties arrayProperties;
        public ObjectInstanceSettings objectInstanceSettings;

        public string fullName() {
            return container != null ? container + "_" + name : name;
        }
    }

    public class ArrayProperties {
        public int maxElements;
        public string elementLabel;
        public Parameter[] elementProperties;
        public string customElementType;
    }

    public class CustomElement {
        public string label;
        public CustomElementExclusiveEditing exclusiveEditing;
        public Parameter[] parameters;
    }

    public class CustomElementExclusiveEditing {
        public bool enabled;
        public bool optional;
        public string category;
    }

    public class ObjectInstanceSettings {
        public string type;
        public ObjectInstanceDefaultModel defaultModel;
        public float[] defaultScale;
        public bool allowCustomModel;
        public int intersection;
        public string condition;
        public ObjectInstaceRoadChoice conditionRoadChoice;
        public string basePosition;
        public ObjectInstaceRoadChoice basePositionRoadChoice;
        public string baseRotation;
        public ObjectInstaceRoadChoice baseRotationRoadChoice;
        public string baseScale;
        public ObjectInstaceRoadChoice baseScaleRoadChoice;
        public bool[] allowPosition;
        public bool[] allowRotation;
        public bool[] allowScale;
    }

    public class ObjectInstanceDefaultModel {
        public bool dynamic; //todo: make it inferrable from the mesh string
        public string model;
        public string[] options;
        public string index;
    }

    public class ObjectInstaceRoadChoice {
        public string lateDefinitions;
        public string condition;
        public string index;
        public string score;
        public string[] inputData;
    }

    public struct Definition {
        public string name;
        public string type;
        public string value;
    }

    public struct LateDefinition {
        public string category;
        public Definition[] definitions;
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
        public LateDefinition[] lateDefinitions;
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

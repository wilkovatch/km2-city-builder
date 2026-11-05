using System.Collections;
using System.Collections.Generic;

namespace CityElements.Types {
    public class Parameter {
        public string name;
        public string label;
        public string placeholder;
        public string tooltip;
        public string type;
        public string customCityPropertiesType;
        public object defaultValueInArray;
        public string container;
        public string[] enumLabels;
        public bool instanceSpecific;
        public bool reuseInNewRoads; //an instance specific parameter of a road type: a new road takes it from the last road selected
        public ArrayProperties arrayProperties;
        public ObjectInstanceSettings objectInstanceSettings;
        //the starting value, only for the inputs of the Python scripts and the parameters of the mesh instance types
        public object defaultValue;
        //only for the inputs of the Python scripts: the category of a "preset" parameter and the extensions of a "file" parameter
        public string presetCategory;
        public string[] extensions;
        //a "reference" parameter (another element of the city): the kinds it can reference ("road", "intersection",
        //"customElement", or "customElement/<type>" for a single custom element type)
        public string[] referenceTypes;
        public float[] range; //a checked float or int parameter: the inclusive [minimum, maximum] and optionally the step

        public string fullName() {
            return container != null ? container + "_" + name : name;
        }
    }

    public class ArrayProperties {
        public int maxElements;
        public string hint; //optional text under the list (e.g. where to create the elements)
        public bool subPanel; //a button opening the list in a sub-panel, instead of the list in the panel
        public bool hideAddButton; //the elements are added some other way (e.g. a command of a property container)
        public string elementLabel;
        public Parameter[] elementProperties;
        public string customCityPropertiesType;
        public string[] propsTypes; //an array of props: each element has one of these props types, and the parameters of its type
        public bool drawLine; //draw a line through the parametric objects of the elements, in order (e.g. the points of a path)
        public LineSettings lineSettings; //optional, how the line is drawn (by default a handle line)
    }

    //the look of the line through the parametric objects of an array (e.g. a strip textured as it is exported); the
    //expressions can use the variables of the element owning the array
    public class LineSettings {
        public string arrowDirection; //positive follows point order, negative reverses it, zero or omitted draws no arrows
        public string width; //float expression, the width in metres (if not set, a thin line growing with the camera distance)
        public string texture; //the texture parameter of the element (if not set or empty, the color of the handles)
        public string heightOffset; //float expression, the height of the line above the points
        public bool miterCorners; //the sides keep the width at the corners
        public LineUVSettings uvs; //the texture coordinates (if not set, the line has none)
    }

    public class LineUVSettings {
        public string uScale; //float expression, u goes from 0 to this across the line (1 if not set)
        public string vLength; //float expression, the length of the line for each unit of v (if not set or 0, v goes from 0 to 1 along all of it)
        public bool vRoundPerSegment; //with vLength, each segment between two points gets a whole number of units of v (at least 1)
    }

    public class CustomCityPropertiesType {
        public string label;
        public CustomCityPropertiesExclusiveEditing exclusiveEditing;
        public PropertyContainerSettings elementContainer;
        public Parameter[] parameters;
        public TabElement[] uiInfo; //optional, the layout of the panel (if not set, all the parameters one per row)
    }

    //Optional ownership of freely placed elements by a city-property group.
    public class PropertyContainerSettings {
        public string category;
        public string tab; //optional: activate this singleton while its city-properties tab or contents are edited
        public string parentCategory; //a nested selection; its parent must be selected too
        public string filterParameter; //inherit the parent's owned elements with this int/enum value
        public int filterValue;
        public string[] types;
        public PropertyContainerCommand[] commands;
        public PropertyContainerContentGroup[] contentGroups; //if not set, the panel of the container has one list of all its contents
        public string editLabel; //the text of the entries in the containers menu (string keys), generic if not set
        public string deselectLabel;
    }

    //A list of some of the owned elements, in a sub-panel of the panel of the container.
    public class PropertyContainerContentGroup {
        public string label;
        public string hint; //optional text under the list (e.g. where to create the elements)
        public string buttonLabel; //the text of the button, the label if not set
        public string[] types; //all the owned types if not set
        public string filterParameter; //optional int or enum parameter of the elements, which must have filterValue
        public int filterValue;
    }

    public class PropertyContainerCommand {
        public string label;
        public string path;
        public string[] alternatePaths;
        public string[] alternateLabels;
    }

    public class CustomCityPropertiesExclusiveEditing {
        public bool enabled;
        public bool optional;
        public string category;
        public bool keepLinesVisible; //the elements not being edited hide only their objects, not the lines through them (drawLine)
    }

    public class ObjectInstanceSettings {
        public string type;
        public ObjectInstanceDefaultModel defaultModel;
        public float[] defaultScale;
        public bool allowCustomModel;
        public string condition;
        public string basePosition;
        public string baseRotation;
        public string baseScale;
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

    public struct Definition {
        public string name;
        public string type;
        public string value;
        public string script; //"<module>.<function>" of the lua folder of the core, instead of the value
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
        public InternalParameterSettings[] internalParametersSettings = new InternalParameterSettings[0];
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
        public float height; //only for the "label:" elements, the height of their row (0.4 if not set)
        public bool boundsOnly; //only for the props types: not shown where the props have no bounds (lineOnly directly on a prop placer)
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

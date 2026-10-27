using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace CityElements.Types {
    //A control point of a low-level element, or a group of them (with children), optionally repeated (array)
    public class LowLevelPointNode {
        public string name;
        public string label; //string key shown in the editor
        public LowLevelPointNode[] children; //a group of points if set, otherwise a single point
        public LowLevelArraySettings array; //repeated, as a list of points or of groups
        public bool rotatable; //the gizmo can rotate the point (its rotation is given to the scripts)
        public bool anchorable = true; //the point can be attached to anchors and shared with other elements
        public float[] defaultPosition; //a new element: the offset of the point from where it is created
    }

    public class LowLevelArraySettings {
        public int min;
        public int max; //0: no limit
        public bool editable = true; //the user can add and remove elements (otherwise only the converters and the API)
        public bool uniform; //in an element of another array: the same number of elements in every element of that array
        public bool closed; //a loop: the element after the last one is the first (e.g. a perimeter)
        public int defaultCount; //a new element: the number of elements (at least min)
        public float[] defaultStep; //a new element: the offset between two consecutive elements
    }

    public class LowLevelMeshSettings {
        public string uvs = "none"; //"none", "editable" (stored with the element) or "script" (given by the geometry script)
        public float[][] defaultVertices; //a new element: the offsets of the vertices from where it is created
        public int[] defaultTriangles;
    }

    public class LowLevelContainerSettings {
        public string[] contains; //the low-level types that can be put in elements of this type
    }

    //the control points computed by a script from the other low-level elements replacing the same procedural element (its
    //siblings, e.g. the BAI road of the PSDL attributes of a road), while the bool parameter flag is on
    public class LowLevelDerivedSettings {
        public string flag;
        public string function; //"<module>.<function>": returns {points = the whole tree}, or nil to keep them
    }

    public class LowLevelSettings {
        public LowLevelPointNode[] controlPoints;
        public LowLevelMeshSettings mesh; //null: no mesh
        public LowLevelContainerSettings container; //null: not a container
        public string geometry; //"<module>.<function>" of the lua folder of the core: the displayed geometry and lines
        public string constraints; //"<module>.<function>": moves points after others were moved (e.g. keeps them aligned)
        public LowLevelDerivedSettings derivedPoints; //null: the points are only edited by the user
        public bool creatable = true; //can be created from the Create menu
        public string[] propsContainers; //the props containers of the prop rule ("propRule" of the state), placed along the propLines of the geometry
        public string propsPresetType; //the prop rule presets listed: the ones of this type (e.g. a road type with the same containers), all if not set
    }

    public class LowLevelType: ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public LowLevelSettings settings;

        public LowLevelType(string folder) {
            ui = Parsers.TypeInheritance.Load<Panel>(folder, "ui.json");
            parameters = Parsers.TypeInheritance.Load<ParameterContainer>(folder, "parameters.json");
            settings = Parsers.TypeInheritance.Load<LowLevelSettings>(folder, "settings.json");
            if (settings.controlPoints == null) settings.controlPoints = new LowLevelPointNode[0];
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }
}

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
        public bool presets; //optional per-type presets, in category lowLevel/<type>
        public ObjectState defaultPreset; //initial Default when an older city has no category yet
        public LowLevelSplineSettings spline;
        public LowLevelCreateMenu createMenu;
        public int geometryLayer = 7;
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

    public class LowLevelCreateMenu {
        public bool direct;
        public string group = "CREATE_LOW_LEVEL_ELEMENT";
    }

    //Road component geometry without road connections or traffic. Curve ids match GeometryHelper.CurveType.
    public class LowLevelSplineSettings : RoadSettings {
        public int[] curveTypes = new[] { 0, 1, 2 };
        public int defaultCurveType = 2;
        public int segments = 20;
        public bool segmentsPer100m;
        public bool projectPoints;
        public bool projectSections;
        public int projectionMask = (1 << 3) | (1 << 7);
        public float maxSectionLength;
        public float maxMiter = 2;
        public bool independentSegments;
        //Optional expressions to subdivide pieces (e.g. a texture's quantized tile length).
        public string splitCondition;
        public int maxSplits = 3;
    }

    public class LowLevelType: ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public LowLevelSettings settings;
        public RoadComponent[] splineComponents;

        public LowLevelType() { }

        public LowLevelType(string folder) {
            ui = Parsers.TypeInheritance.Load<Panel>(folder, "ui.json");
            parameters = Parsers.TypeInheritance.Load<ParameterContainer>(folder, "parameters.json");
            settings = Parsers.TypeInheritance.Load<LowLevelSettings>(folder, "settings.json");
            if (settings.spline != null) {
                var builtins = new[] {
                    new Parameter { name = "curveType", type = "int" },
                    new Parameter { name = "segments", type = "int" },
                    new Parameter { name = "segmentsPer100m", type = "bool" },
                    new Parameter { name = "hermiteTension", type = "float" },
                    new Parameter { name = "subdivideEqually", type = "bool" }
                };
                var parametersList = new List<Parameter>(parameters.parameters ?? new Parameter[0]);
                foreach (var builtin in builtins) {
                    if (!parametersList.Exists(p => p.fullName() == builtin.name)) parametersList.Add(builtin);
                }
                parameters.parameters = parametersList.ToArray();
                if (settings.mesh != null || (settings.controlPoints?.Length ?? 0) > 0 || settings.geometry != null) {
                    throw new System.Exception("Spline low-level types cannot also define mesh, controlPoints or geometry");
                }
                settings.controlPoints = SplinePoints();
                var components = settings.spline.components ?? new ComponentInfo[0];
                splineComponents = new RoadComponent[components.Length];
                var common = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(folder)), "sharedComponents");
                for (int i = 0; i < components.Length; i++) {
                    splineComponents[i] = Parsers.TypeInheritance.LoadComponent<RoadComponent>(folder, components[i].name, common);
                }
            }
            if (settings.controlPoints == null) settings.controlPoints = new LowLevelPointNode[0];
        }

        public static LowLevelPointNode[] SplinePoints() {
            return new[] { new LowLevelPointNode { name = "spline", label = "LL_SPLINE_POINTS",
                array = new LowLevelArraySettings { min = 0, defaultCount = 0 } } };
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }
}

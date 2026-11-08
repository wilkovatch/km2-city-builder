using System.Collections;
using System.Collections.Generic;
using System.IO;
using CityElements.Types.Parsers;

namespace CityElements.Types {
    public class PropLine {
        public string containerName;
        public string condition;
        public string[] bounds;
    }

    public class TrafficType {
        public string name;
        public int[] color;
    }

    public class TrafficLane {
        public string type;
        public string arrowDirection;
        public string condition;
        public string[] bounds;
        public object lanes;
    }

    public class RoadSettings {
        public int[] curveTypes; //null: the three built-in curves
        public int defaultCurveType;
        //the initial values of the spline of a new element (kept in its instance state, never in the presets)
        public int segments = 10;
        public bool segmentsPer100m;
        public float hermiteTension = 0.5f;
        public bool subdivideEqually;
        public bool project = true;
        public bool projectAll;
        public int lpf;
        public bool adjustLowPolyWidth;
        public bool variableSections;
        //the roads define only their traffic lanes (no meshes): left out of the geometry of their intersections, picked by
        //an invisible strip across their sections, an intersection with only such roads has no geometry
        public bool geometryless;
        //"<module>.<function>" of the lua folder of the core: edits the sections once they are computed (see SectionPostProcess)
        public string postProcess;
        public bool allowYaw = true; //whether the control points can rotate the sections around the vertical axis
        public bool allowRoll = true; //whether the control points can bank the sections
        public string yawRequires; //bool city property injected on roads, needed to edit the yaw in the editor
        public string rollRequires; //the same for the roll
        public object[] sectionVertices;
        public object anchors;
        public object endAnchors; //the points of the end section given to its intersection as anchors for the custom elements
        public string[] propsContainers;
        public PropLine[] propsLines;
        public TrafficType[] trafficTypes;
        public TrafficLane[] trafficLanes;
        public string[] textures;
        public Dictionary<string, int[]> texturesMapping;
        public string[] widths;
        public Dictionary<string, string> getters;
        public ComponentInfo[] components;
    }

    public class RoadComponentMesh {
        public object vertices;
        public object uvs;
        public object faces;
        public object facesTextures;
    }

    public class RoadComponent : SubObjectWithLocalDefinitions {
        public object anchors;
        public PropLine[] propsLines;
        public TrafficLane[] trafficLanes;
        public RoadComponentMesh mainMesh;
        public RoadComponentMesh startMesh;
        public RoadComponentMesh endMesh;
    }

    public class RoadType: ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public RoadSettings settings;
        public RoadComponent[] components;

        public RoadType() { }

        public RoadType(string folder, string commonFolder) {
            ui = TypeInheritance.Load<Panel>(folder, "ui.json");
            parameters = TypeInheritance.Load<ParameterContainer>(folder, "parameters.json");
            settings = TypeInheritance.Load<RoadSettings>(folder, "settings.json");
            components = new RoadComponent[settings.components.Length];
            for (int i = 0; i < components.Length; i++) {
                components[i] = TypeInheritance.LoadComponent<RoadComponent>(folder, settings.components[i].name, commonFolder);
            }
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }

    public class ImportedParameter {
        public bool fromStart;
        public string name;
        public string type;
        public string newName;
    }

    public class ImportedPropContainer {
        public bool fromStart;
        public string name;
        public string newName;
    }

    public class ImportedTrafficType {
        public bool fromStart;
        public string name;
        public string newName;
    }

    public class ExtraTerrainSpline {
        public string name;
        public string vertex;
    }

    public class TextureDefinition {
        public string name;
        public string[] options;
        public string index;
    }

    public class ExtraTerrainPiece {
        public string name;
        public string junctionCondition;
        public string junctionConditionMode;
        public string parametersSourceIndex;
        public bool splineOverride;
        public bool facingUp;
        public string uMult;
        public string vMult;
        public string texture;
        public string textureSource;
        public string condition;
    }

    public class JunctionType {
        public string[] standardFloatsBoolsAndInts;
        public string[] standardVec3s;
        public string[] standardVec2s;
        public string actualSegments;
        public string[] sectionVertices;
        public int roadSplineVertex;
        public ImportedParameter[] importedParameters = new ImportedParameter[0];
        public Definition[] staticDefinitions;
        public Definition[] dynamicDefinitions;
        public LateDefinition[] lateDefinitions;
        public TextureDefinition[] textureDefinitions;
        public object anchors;
        public ImportedPropContainer[] importedPropsContainers;
        public PropLine[] propsLines;
        public ImportedTrafficType[] importedTrafficTypes;
        public TrafficLane[] trafficLanes;
        public string[] textures;
        public Dictionary<string, int[]> texturesMapping;
        public ComponentInfo[] components;
        public ExtraTerrainSpline[] extraTerrainSplines;
    }

    //the mesh of the crosswalks of an intersection type (crosswalk.json), evaluated once per crosswalk
    public class CrosswalkType {
        public ImportedParameter[] importedParameters = new ImportedParameter[0]; //from the road of the crosswalk (fromStart is ignored)
        public Definition[] staticDefinitions;
        public string[] textures;
        public Dictionary<string, int[]> texturesMapping;
        public ComponentInfo[] components;
    }

    public class IntersectionSettings {
        public string roadTexture;
        public string defaultCrosswalkTexture;
        public string roadStartCrosswalkTexture;
        public string roadEndCrosswalkTexture;
        public bool minimizeSubmeshes;
        public ExtraTerrainPiece[] extraTerrainPieces = new ExtraTerrainPiece[0];
    }

    public class IntersectionType: ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public Dictionary<string, RoadComponent> components;
        public Dictionary<string, JunctionType> junctions;
        public IntersectionSettings settings;
        public CrosswalkType crosswalk; //null if the type has no crosswalk.json (no crosswalk geometry)
        public Buildings.BlockComponent[] crosswalkComponents;

        public IntersectionType(string folder, string commonFolder) {
            ui = TypeInheritance.Load<Panel>(folder, "ui.json");
            parameters = TypeInheritance.Load<ParameterContainer>(folder, "parameters.json");
            settings = TypeInheritance.Load<IntersectionSettings>(folder, "settings.json");
            if (TypeInheritance.Exists(folder, "crosswalk.json")) {
                crosswalk = TypeInheritance.Load<CrosswalkType>(folder, "crosswalk.json");
                crosswalk.components ??= new ComponentInfo[0];
                crosswalk.textures ??= new string[0];
                crosswalk.texturesMapping ??= new Dictionary<string, int[]>();
                crosswalkComponents = new Buildings.BlockComponent[crosswalk.components.Length];
                for (int i = 0; i < crosswalkComponents.Length; i++) {
                    crosswalkComponents[i] = TypeInheritance.LoadComponent<Buildings.BlockComponent>(folder, crosswalk.components[i].name, null);
                }
            }
            junctions = new Dictionary<string, JunctionType>();
            components = new Dictionary<string, RoadComponent>();
            foreach (var junctionName in TypeInheritance.FileNames(folder, "junctions")) {
                var junction = TypeInheritance.Load<JunctionType>(folder, Path.Combine("junctions", junctionName + ".json"));
                junctions[junctionName] = junction;
                foreach (var elem in junction.components) {
                    if (!components.ContainsKey(elem.name)) {
                        components[elem.name] = TypeInheritance.LoadComponent<RoadComponent>(folder, elem.name, commonFolder);
                    }
                }
            }
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }
}

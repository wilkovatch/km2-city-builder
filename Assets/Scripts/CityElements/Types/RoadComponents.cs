using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

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
        public string condition;
        public string[] bounds;
        public object lanes;
    }

    public class RoadSettings {
        public bool variableSections;
        public object[] sectionVertices;
        public object anchors;
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

        public RoadType(string folder, string commonFolder) {
            var uiFile = Path.Combine(folder, "ui.json");
            var uiContent = File.ReadAllText(uiFile);
            var parametersFile = Path.Combine(folder, "parameters.json");
            var parametersContent = File.ReadAllText(parametersFile);
            var settingsFile = Path.Combine(folder, "settings.json");
            var settingsContent = File.ReadAllText(settingsFile);
            ui = Newtonsoft.Json.JsonConvert.DeserializeObject<Panel>(uiContent);
            parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterContainer>(parametersContent);
            settings = Newtonsoft.Json.JsonConvert.DeserializeObject<RoadSettings>(settingsContent);
            var compDir = Path.Combine(folder, "components");
            var componentFiles = new string[settings.components.Length];
            for (int i = 0; i < componentFiles.Length; i++) {
                var compPath = Path.Combine(compDir, settings.components[i].name + ".json");
                if (!File.Exists(compPath)) compPath = Path.Combine(commonFolder, settings.components[i].name + ".json");
                componentFiles[i] = compPath;
            }
            components = new RoadComponent[componentFiles.Length];
            for (int i = 0; i < componentFiles.Length; i++) {
                var compContent = File.ReadAllText(componentFiles[i]);
                components[i] = Newtonsoft.Json.JsonConvert.DeserializeObject<RoadComponent>(compContent);
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
        public ImportedParameter[] importedParameters;
        public Definition[] staticDefinitions;
        public Definition[] dynamicDefinitions;
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

    public class IntersectionSettings {
        public string roadTexture;
        public string defaultCrosswalkTexture;
        public string roadStartCrosswalkTexture;
        public string roadEndCrosswalkTexture;
        public bool minimizeSubmeshes;
        public ExtraTerrainPiece[] extraTerrainPieces;
    }

    public class IntersectionType: ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public Dictionary<string, RoadComponent> components;
        public Dictionary<string, JunctionType> junctions;
        public IntersectionSettings settings;

        public IntersectionType(string folder, string commonFolder) {
            var uiFile = Path.Combine(folder, "ui.json");
            var uiContent = File.ReadAllText(uiFile);
            var parametersFile = Path.Combine(folder, "parameters.json");
            var parametersContent = File.ReadAllText(parametersFile);
            var settingsFile = Path.Combine(folder, "settings.json");
            var settingsContent = File.ReadAllText(settingsFile);
            ui = Newtonsoft.Json.JsonConvert.DeserializeObject<Panel>(uiContent);
            parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterContainer>(parametersContent);
            settings = Newtonsoft.Json.JsonConvert.DeserializeObject<IntersectionSettings>(settingsContent);
            var junctionsPath = Path.Combine(folder, "junctions");
            var junctionFiles = Directory.GetFiles(junctionsPath, "*.json");
            junctions = new Dictionary<string, JunctionType>();
            var compDir = Path.Combine(folder, "components");
            components = new Dictionary<string, RoadComponent>();
            for (int i = 0; i < junctionFiles.Length; i++) {
                var junctionContent = File.ReadAllText(Path.Combine(junctionsPath, junctionFiles[i]));
                var cleanName = Path.GetFileName(junctionFiles[i]).Replace(".json", "");
                junctions[cleanName] = Newtonsoft.Json.JsonConvert.DeserializeObject<JunctionType>(junctionContent);
                foreach (var elem in junctions[cleanName].components) {
                    if (!components.ContainsKey(elem.name)) {
                        var compPath = Path.Combine(compDir, elem.name + ".json");
                        if (!File.Exists(compPath)) compPath = Path.Combine(commonFolder, elem.name + ".json");
                        var compContent = File.ReadAllText(compPath);
                        components[elem.name] = Newtonsoft.Json.JsonConvert.DeserializeObject<RoadComponent>(compContent);
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

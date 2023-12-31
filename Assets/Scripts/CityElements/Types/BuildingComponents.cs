using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityElements.Types.Buildings {
    //Building line
    public class BuildingLineSettings {
        //TODO
    }

    public class BuildingLineType : ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public BuildingLineSettings settings;

        public BuildingLineType(string folder) {
            var uiFile = Path.Combine(folder, "line_ui.json");
            var uiContent = File.ReadAllText(uiFile);
            var parametersFile = Path.Combine(folder, "line_parameters.json");
            var parametersContent = File.ReadAllText(parametersFile);
            var settingsFile = Path.Combine(folder, "line_settings.json");
            var settingsContent = File.ReadAllText(settingsFile);
            ui = Newtonsoft.Json.JsonConvert.DeserializeObject<Panel>(uiContent);
            parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterContainer>(parametersContent);
            settings = Newtonsoft.Json.JsonConvert.DeserializeObject<BuildingLineSettings>(settingsContent);
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }



    //Building
    public class BuildingBuildingSettings {
        //TODO
    }

    public class BuildingBuildingType : ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public BuildingBuildingSettings settings;

        public BuildingBuildingType(string folder) {
            var uiFile = Path.Combine(folder, "building_ui.json");
            var uiContent = File.ReadAllText(uiFile);
            var parametersFile = Path.Combine(folder, "building_parameters.json");
            var parametersContent = File.ReadAllText(parametersFile);
            var settingsFile = Path.Combine(folder, "building_settings.json");
            var settingsContent = File.ReadAllText(settingsFile);
            ui = Newtonsoft.Json.JsonConvert.DeserializeObject<Panel>(uiContent);
            parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterContainer>(parametersContent);
            settings = Newtonsoft.Json.JsonConvert.DeserializeObject<BuildingBuildingSettings>(settingsContent);
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }



    //Building side block
    public class BlockComponent : SubObjectWithLocalDefinitions {
        public RoadComponentMesh mesh;
    }

    public class BlockSettings {
        public bool alwaysDraw;
        public string thumbnail; //TODO
        public bool mergeable; //TODO
        public string[] textures;
        public Dictionary<string, int[]> texturesMapping;
        public ComponentInfo[] components;
    }

    public class BlockType : ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public BlockSettings settings;
        public BlockComponent[] components;

        public BlockType(string folder) {
            var uiFile = Path.Combine(folder, "ui.json");
            var uiContent = File.ReadAllText(uiFile);
            var parametersFile = Path.Combine(folder, "parameters.json");
            var parametersContent = File.ReadAllText(parametersFile);
            var settingsFile = Path.Combine(folder, "settings.json");
            var settingsContent = File.ReadAllText(settingsFile);
            ui = Newtonsoft.Json.JsonConvert.DeserializeObject<Panel>(uiContent);
            parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterContainer>(parametersContent);
            settings = Newtonsoft.Json.JsonConvert.DeserializeObject<BlockSettings>(settingsContent);

            //components
            if (settings.components != null && settings.components.Length > 0) {
                var compDir = Path.Combine(folder, "components");
                var componentFiles = new string[settings.components.Length];
                for (int i = 0; i < componentFiles.Length; i++) {
                    var compPath = Path.Combine(compDir, settings.components[i].name + ".json");
                    //if (!File.Exists(compPath)) compPath = Path.Combine(commonFolder, settings.components[i].name + ".json"); //TODO (maybe)
                    componentFiles[i] = compPath;
                }
                components = new BlockComponent[componentFiles.Length];
                for (int i = 0; i < componentFiles.Length; i++) {
                    var compContent = File.ReadAllText(componentFiles[i]);
                    components[i] = Newtonsoft.Json.JsonConvert.DeserializeObject<BlockComponent>(compContent);
                }
            } else {
                components = new BlockComponent[0];
            }
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }



    //Building Side
    public class BuildingSideSettings {
        public string[] blockTypes;
    }

    public class BuildingSideType : ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public BuildingSideSettings settings;
        //public Dictionary<string, BlockType> blockTypes;

        public BuildingSideType(string folder) {
            var uiFile = Path.Combine(folder, "side_ui.json");
            var uiContent = File.ReadAllText(uiFile);
            var parametersFile = Path.Combine(folder, "side_parameters.json");
            var parametersContent = File.ReadAllText(parametersFile);
            var settingsFile = Path.Combine(folder, "side_settings.json");
            var settingsContent = File.ReadAllText(settingsFile);
            ui = Newtonsoft.Json.JsonConvert.DeserializeObject<Panel>(uiContent);
            parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterContainer>(parametersContent);
            settings = Newtonsoft.Json.JsonConvert.DeserializeObject<BuildingSideSettings>(settingsContent);
            /*blockTypes = new Dictionary<string, BlockType>();
            var blockDirs = Directory.GetDirectories(Path.Combine(folder, "blocks"));
            foreach (var dir in blockDirs) {
                var key = Path.GetFileName(dir);
                blockTypes[key] = new BlockType(dir);
            }*/
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }

    //Container
    public class BuildingType {
        public BuildingLineType lineType;
        public BuildingBuildingType buildingType;
        public BuildingSideType sideType;

        public BuildingType(string folder) {
            lineType = new BuildingLineType(folder);
            buildingType = new BuildingBuildingType(folder);
            sideType = new BuildingSideType(folder);
        }
    }
}

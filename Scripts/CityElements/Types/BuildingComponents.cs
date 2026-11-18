using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace CityElements.Types.Buildings {
    //Building line
    public class BuildingLineSettings {
        public bool projectToGround; //the initial value of a new line (kept in its instance state, never in the presets)
    }

    public class BuildingLineType : ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public BuildingLineSettings settings;

        public BuildingLineType(string folder) {
            ui = Parsers.TypeInheritance.Load<Panel>(folder, "line_ui.json");
            parameters = Parsers.TypeInheritance.Load<ParameterContainer>(folder, "line_parameters.json");
            settings = Parsers.TypeInheritance.Load<BuildingLineSettings>(folder, "line_settings.json");
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }



    //Polygonal building (optional files: a type without them can't be used for polygonal buildings)
    public class PolygonalBuildingSettings {
        public bool projectToGround; //the initial value of a new polygonal building (kept in its instance state)
    }

    public class PolygonalBuildingType : ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public PolygonalBuildingSettings settings;

        public PolygonalBuildingType(string folder) {
            ui = Parsers.TypeInheritance.Load<Panel>(folder, "polygon_ui.json");
            parameters = Parsers.TypeInheritance.Load<ParameterContainer>(folder, "polygon_parameters.json");
            settings = Parsers.TypeInheritance.Exists(folder, "polygon_settings.json")
                ? Parsers.TypeInheritance.Load<PolygonalBuildingSettings>(folder, "polygon_settings.json")
                : new PolygonalBuildingSettings();
        }

        public static bool IsDefined(string folder) {
            return Parsers.TypeInheritance.Exists(folder, "polygon_ui.json") && Parsers.TypeInheritance.Exists(folder, "polygon_parameters.json");
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
            ui = Parsers.TypeInheritance.Load<Panel>(folder, "building_ui.json");
            parameters = Parsers.TypeInheritance.Load<ParameterContainer>(folder, "building_parameters.json");
            settings = Parsers.TypeInheritance.Load<BuildingBuildingSettings>(folder, "building_settings.json");
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
            ui = Parsers.TypeInheritance.Load<Panel>(folder, "ui.json");
            parameters = Parsers.TypeInheritance.Load<ParameterContainer>(folder, "parameters.json");
            settings = Parsers.TypeInheritance.Load<BlockSettings>(folder, "settings.json");

            //components
            if (settings.components != null && settings.components.Length > 0) {
                components = new BlockComponent[settings.components.Length];
                for (int i = 0; i < components.Length; i++) {
                    components[i] = Parsers.TypeInheritance.LoadComponent<BlockComponent>(folder, settings.components[i].name, null);
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
            ui = Parsers.TypeInheritance.Load<Panel>(folder, "side_ui.json");
            parameters = Parsers.TypeInheritance.Load<ParameterContainer>(folder, "side_parameters.json");
            settings = Parsers.TypeInheritance.Load<BuildingSideSettings>(folder, "side_settings.json");
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
        public PolygonalBuildingType polygonType; //null when the type has no polygon_* files

        public BuildingType(string folder) {
            lineType = new BuildingLineType(folder);
            buildingType = new BuildingBuildingType(folder);
            sideType = new BuildingSideType(folder);
            if (PolygonalBuildingType.IsDefined(folder)) polygonType = new PolygonalBuildingType(folder);
        }
    }
}

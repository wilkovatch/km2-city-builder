using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace CityElements.Types {
    public class TerrainPatchSettings {
        public string geometry; //optional <module>.<function> of the lua folder: meshes added to the surface of the patch
    }

    public class TerrainPatchType: ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public TerrainPatchSettings settings;

        public TerrainPatchType(string folder) {
            ui = Parsers.TypeInheritance.Load<Panel>(folder, "ui.json");
            parameters = Parsers.TypeInheritance.Load<ParameterContainer>(folder, "parameters.json");
            settings = Parsers.TypeInheritance.Load<TerrainPatchSettings>(folder, "settings.json");
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }
}

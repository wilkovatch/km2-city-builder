using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityElements.Types {
    public class TerrainPatchSettings {
        public string[] borderMeshTypes;
        public string borderMeshName;
        public int maxBorderMeshes;
    }

    public class TerrainPatchType: ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public TerrainPatchSettings settings;

        public TerrainPatchType(string folder) {
            var uiFile = Path.Combine(folder, "ui.json");
            var uiContent = File.ReadAllText(uiFile);
            var parametersFile = Path.Combine(folder, "parameters.json");
            var parametersContent = File.ReadAllText(parametersFile);
            var settingsFile = Path.Combine(folder, "settings.json");
            var settingsContent = File.ReadAllText(settingsFile);
            ui = Newtonsoft.Json.JsonConvert.DeserializeObject<Panel>(uiContent);
            parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterContainer>(parametersContent);
            settings = Newtonsoft.Json.JsonConvert.DeserializeObject<TerrainPatchSettings>(settingsContent);
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }
}

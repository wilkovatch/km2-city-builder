using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityElements.Types.CitySettings {
    public class ParameterInjection {
        public string parameter;
        public bool onRoads;
        /*public bool onIntersections; //TODO
        public bool onTerrain;
        public bool onBuildings;*/
    }

    public class Settings {
        public ParameterInjection[] injectableParameters;
    }

    public class CitySettings: ITypeWithUI {
        public ParameterContainer parametersInfo;
        public Panel uiInfo;
        public Settings settings;

        public CitySettings(string folder) {
            var uiFile = Path.Combine(folder, "ui.json");
            var uiContent = File.ReadAllText(uiFile);
            var parametersFile = Path.Combine(folder, "parameters.json");
            var parametersContent = File.ReadAllText(parametersFile);
            var settingsFile = Path.Combine(folder, "settings.json");
            var settingsContent = File.ReadAllText(settingsFile);
            uiInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<Panel>(uiContent);
            parametersInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterContainer>(parametersContent);
            settings = Newtonsoft.Json.JsonConvert.DeserializeObject<Settings>(settingsContent);
        }

        public ParameterContainer GetParameters() {
            return parametersInfo;
        }

        public Panel GetUI() {
            return uiInfo;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    public class CitySettings : RuntimeType<Types.CitySettings.CitySettings> {
        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists() {
            var floatVars = new List<string>();
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>();
            return (floatVars, vec2Vars, vec3Vars);
        }

        public CitySettings(Types.CitySettings.CitySettings type) : base(type, type.parametersInfo, null, GetVarLists(), "settings") {
            SetIndices();
        }

        protected void SetIndices() {
            SetBaseIndices();
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state) {
            FillBaseInitialVariables(variableContainer, state, null);
        }
    }
}

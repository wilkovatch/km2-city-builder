using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    public class TerrainPatchType: RuntimeType<Types.TerrainPatchType> {
        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists() {
            var floatVars = new List<string>();
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>();
            return (floatVars, vec2Vars, vec3Vars);
        }

        public TerrainPatchType(Types.TerrainPatchType type, string name) : base(type, type.parameters, null, GetVarLists(), name) {
            SetIndices();
        }

        protected void SetIndices() {
            SetBaseIndices();
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state) {
            FillBaseInitialVariables(variableContainer, state, null);
        }

        public void FillStaticVariables(RC.VariableContainer variableContainer) {
            FillStaticDefinitionVariables(variableContainer);
        }

        public void FillSegmentVariables(RC.VariableContainer variableContainer) {
            FillBaseIterationVariables(variableContainer);
        }
    }
}

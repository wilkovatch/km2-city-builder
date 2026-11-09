using System.Collections;
using System.Collections.Generic;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    public class TerrainPatchType: RuntimeType<Types.TerrainPatchType> {
        //the geometry function of the Lua scripts of the core (null if the type has none) and its host
        public readonly Lua.LuaFunction geometryFunction;
        public readonly RC.Scripting.LuaScriptHost scriptHost;

        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists() {
            var floatVars = new List<string>();
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>();
            return (floatVars, vec2Vars, vec3Vars);
        }

        public TerrainPatchType(Types.TerrainPatchType type, string name) : base(type, type.parameters, null, GetVarLists(), name) {
            SetIndices();
            if (type.settings?.geometry != null) {
                scriptHost = RC.Scripting.LuaScriptHost.Get();
                geometryFunction = scriptHost.Resolve(type.settings.geometry);
            }
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

        public void FillLateVariables(RC.VariableContainer variableContainer, string category) {
            FillBaseLateVariables(variableContainer, category); //TODO: fix when actually used
        }
    }
}

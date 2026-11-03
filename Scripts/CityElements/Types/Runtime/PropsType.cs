using System.Collections;
using System.Collections.Generic;
using RC = RuntimeCalculator;
using Godot;

namespace CityElements.Types.Runtime {
    public class PropsType : RuntimeType<Types.PropsType> {
        public readonly RC.Scripting.LuaScriptHost scriptHost;
        public readonly Lua.LuaFunction placementFunction;
        public bool IsGroup => placementFunction == null; //places only the elements of its array parameters

        //scriptHost: the host of the scripts (by default the one of the core; the tests use their own)
        public PropsType(Types.PropsType type, string name, RC.Scripting.LuaScriptHost scriptHost = null): base(type, type.parametersInfo ?? new ParameterContainer(), null,
            (new List<string>() { "totalLength" }, new List<string>(), new List<string>()), name, "props") {
            if (type.placement != null) {
                this.scriptHost = scriptHost ?? RC.Scripting.LuaScriptHost.Get();
                placementFunction = this.scriptHost.Resolve(type.placement);
            }
        }

        //the variables of the callback: the parameters of the state and the definitions of the type
        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state, float totalLength, ObjectState injectedState = null) {
            FillFloat(variableContainer, "totalLength", totalLength);
            FillBaseInitialVariables(variableContainer, state, null, injectedState);
            FillBaseIterationVariables(variableContainer);
        }
    }
}

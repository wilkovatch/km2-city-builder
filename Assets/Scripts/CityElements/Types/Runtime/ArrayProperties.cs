using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    public class ArrayProperties : RuntimeType<Types.ArrayProperties> {
        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists() {
            var floatVars = new List<string>();
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>();
            return (floatVars, vec2Vars, vec3Vars);
        }

        static ParameterContainer GetParameterContainer(Types.ArrayProperties type) {
            var res = new ParameterContainer();
            res.parameters = GetParameters(type);
            return res;
        }

        static Parameter[] GetParameters(Types.ArrayProperties type) {
            if (type == null) return null;
            if (type.customElementType != null) {
                var ct = Parsers.TypeParser.GetCustomTypes()[type.customElementType];
                return ct.parameters;
            } else {
                return type.elementProperties;
            }
        }

        public ArrayProperties(Types.ArrayProperties type) : base(type, GetParameterContainer(type), null, GetVarLists(), "settings") {
            SetIndices();
        }

        public static ArrayProperties FromCustomType(string ct) {
            var auxArrayProperties = new Types.ArrayProperties();
            auxArrayProperties.customElementType = ct;
            return new ArrayProperties(auxArrayProperties);
        }

        protected void SetIndices() {
            SetBaseIndices();
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state) {
            FillBaseInitialVariables(variableContainer, state, null);
        }
    }
}

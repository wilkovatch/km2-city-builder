using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    public abstract class RuntimeType<T> {
        public T typeData;
        public string name;
        public ParameterContainer parameterContainer;
        public SubObjectWithLocalDefinitions[] subTypes;
        public List<RC.Booleans.Boolean> subConditions = new List<RC.Booleans.Boolean>(); //TODO: merge with array above?
        public RC.VariableContainer variableContainer;
        public Dictionary<string, RC.Numbers.Number> numberDefinitions = new Dictionary<string, RC.Numbers.Number>();
        public Dictionary<string, RC.Vector3s.Vector> vector3Definitions = new Dictionary<string, RC.Vector3s.Vector>();
        public Dictionary<string, RC.Vector2s.Vector> vector2Definitions = new Dictionary<string, RC.Vector2s.Vector>();
        public Dictionary<string, RC.Booleans.Boolean> boolDefinitions = new Dictionary<string, RC.Booleans.Boolean>();
        public List<Dictionary<string, RC.Numbers.Number>> localNumberDefinitions = new List<Dictionary<string, RC.Numbers.Number>>();
        public List<Dictionary<string, RC.Vector3s.Vector>> localVector3Definitions = new List<Dictionary<string, RC.Vector3s.Vector>>();
        public List<Dictionary<string, RC.Vector2s.Vector>> localVector2Definitions = new List<Dictionary<string, RC.Vector2s.Vector>>();
        public List<Dictionary<string, RC.Booleans.Boolean>> localBoolDefinitions = new List<Dictionary<string, RC.Booleans.Boolean>>();
        public Dictionary<string, Parameter> objectInstanceParams = new Dictionary<string, Parameter>();
        public Dictionary<string, Types.ArrayProperties> arrayParams = new Dictionary<string, Types.ArrayProperties>();
        public Dictionary<string, string> customElemParams = new Dictionary<string, string>();
        public Dictionary<string, Definition[]> lateDefinitionsDict = new Dictionary<string, Definition[]>();

        void ParseDefinition(Definition def, List<string> floatVars, List<string> vec3Vars, List<string> vec2Vars) {
            if (def.type == "float" || def.type == "int") {
                floatVars.Add(def.name);
                numberDefinitions.Add(def.name, RC.Parsers.FloatParser.ParseExpression(def.value));
            } else if (def.type == "bool") {
                floatVars.Add(def.name);
                boolDefinitions.Add(def.name, RC.Parsers.BooleanParser.ParseExpression(def.value));
            } else if (def.type == "vec3") {
                vec3Vars.Add(def.name);
                vector3Definitions.Add(def.name, RC.Parsers.Vector3Parser.ParseExpression(def.value));
            } else if (def.type == "vec2") {
                vec2Vars.Add(def.name);
                vector2Definitions.Add(def.name, RC.Parsers.Vector2Parser.ParseExpression(def.value));
            }
        }

        public RuntimeType(T typeData, ParameterContainer parameterContainer, (SubObjectWithLocalDefinitions obj, string condition)[] components,
            (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) vars, string name) {

            this.name = name;
            this.typeData = typeData;
            this.parameterContainer = parameterContainer;

            if (parameterContainer.parameters != null) {
                foreach (var param in parameterContainer.parameters) {
                    if (param.type == "float" || param.type == "int" || param.type == "bool" || param.type == "enum") {
                        vars.floatVars.Add(param.fullName());
                    } else if (param.type == "vec2") {
                        vars.vec2Vars.Add(param.fullName());
                    } else if (param.type == "vec3") {
                        vars.vec3Vars.Add(param.fullName());
                    } else if (param.type == "objectInstance") {
                        objectInstanceParams.Add(param.fullName(), param);
                    } else if (param.type == "array") {
                        arrayParams.Add(param.fullName(), param.arrayProperties);
                    } else if (param.type == "customElement") {
                        customElemParams.Add(param.fullName(), param.customElementType);
                    }
                }
            }
            if (parameterContainer.loopVariables != null) {
                foreach (var param in parameterContainer.loopVariables) {
                    vars.floatVars.Add(param);
                }
            }
            var definitions = new List<Definition[]>() {
                parameterContainer.staticDefinitions,
                parameterContainer.dynamicDefinitions
            };
            if (parameterContainer.lateDefinitions != null) {
                foreach (var def in parameterContainer.lateDefinitions) {
                    definitions.Add(def.definitions);
                    lateDefinitionsDict.Add(def.category, def.definitions);
                }
            }
            foreach (var defArray in definitions) {
                if (defArray != null) {
                    foreach (var def in defArray) {
                        ParseDefinition(def, vars.floatVars, vars.vec3Vars, vars.vec2Vars);
                    }
                }
            }
            var dict = new List<(string, System.Type)>();
            foreach (var v in vars.floatVars) dict.Add((v, typeof(float)));
            foreach (var v in vars.vec2Vars) dict.Add((v, typeof(Vector2)));
            foreach (var v in vars.vec3Vars) dict.Add((v, typeof(Vector3)));

            if (components != null) {
                subTypes = new SubObjectWithLocalDefinitions[components.Length];
                int i = 0;
                foreach (var info in components) {
                    subTypes[i] = info.obj;
                    i++;
                    var component = info.obj;
                    var condition = info.condition;
                    if (info.condition != null) subConditions.Add(RC.Parsers.BooleanParser.ParseExpression(info.condition));
                    else subConditions.Add(null);
                    var numDict = new Dictionary<string, RC.Numbers.Number>();
                    var boolDict = new Dictionary<string, RC.Booleans.Boolean>();
                    var vec3Dict = new Dictionary<string, RC.Vector3s.Vector>();
                    var vec2Dict = new Dictionary<string, RC.Vector2s.Vector>();
                    if (component.localDefinitions != null) {
                        (string, System.Type) defTuple;
                        foreach (var def in component.localDefinitions) {
                            if (def.type == "float" || def.type == "int") {
                                defTuple = (def.name, typeof(float));
                                if (!dict.Contains(defTuple)) dict.Add(defTuple);
                                numDict.Add(def.name, RC.Parsers.FloatParser.ParseExpression(def.value));
                            } else if (def.type == "bool") {
                                defTuple = (def.name, typeof(float));
                                if (!dict.Contains(defTuple)) dict.Add(defTuple);
                                boolDict.Add(def.name, RC.Parsers.BooleanParser.ParseExpression(def.value));
                            } else if (def.type == "vec3") {
                                defTuple = (def.name, typeof(Vector3));
                                if (!dict.Contains(defTuple)) dict.Add(defTuple);
                                vec3Dict.Add(def.name, RC.Parsers.Vector3Parser.ParseExpression(def.value));
                            } else if (def.type == "vec2") {
                                defTuple = (def.name, typeof(Vector2));
                                if (!dict.Contains(defTuple)) dict.Add(defTuple);
                                vec2Dict.Add(def.name, RC.Parsers.Vector2Parser.ParseExpression(def.value));
                            }
                        }
                    }
                    localNumberDefinitions.Add(numDict);
                    localBoolDefinitions.Add(boolDict);
                    localVector3Definitions.Add(vec3Dict);
                    localVector2Definitions.Add(vec2Dict);
                }
            }

            variableContainer = new RC.VariableContainer(dict);

            SetBaseIndices();
        }

        protected void FillVector3(RC.VariableContainer variableContainer, string name, Vector3 val) {
            variableContainer.SetVector3(name, val);
        }

        protected void FillVector2(RC.VariableContainer variableContainer, string name, Vector2 val) {
            variableContainer.SetVector2(name, val);
        }

        protected void FillFloat(RC.VariableContainer variableContainer, string name, float val) {
            variableContainer.SetFloat(name, val);
        }

        protected void FillBool(RC.VariableContainer variableContainer, string name, bool val) {
            FillFloat(variableContainer, name, val ? 1.0f : 0.0f);
        }

        protected void SetBaseIndices() {
            var vc = variableContainer;
            foreach (var d in numberDefinitions.Values) d.SetIndices(vc);
            foreach (var d in boolDefinitions.Values) d.SetIndices(vc);
            foreach (var d in vector3Definitions.Values) d.SetIndices(vc);
            foreach (var d in vector2Definitions.Values) d.SetIndices(vc);
            if (subTypes != null) {
                for (int c = 0; c < subTypes.Length; c++) {
                    var vars = variableContainer;
                    subConditions[c]?.SetIndices(vc);
                    foreach (var d in localNumberDefinitions[c].Values) d.SetIndices(vars);
                    foreach (var d in localBoolDefinitions[c].Values) d.SetIndices(vars);
                    foreach (var d in localVector3Definitions[c].Values) d.SetIndices(vars);
                    foreach (var d in localVector2Definitions[c].Values) d.SetIndices(vars);
                }
            }
        }

        private void FillDefinition(RC.VariableContainer variableContainer, Definition def) {
            if (def.type == "float" || def.type == "int") FillFloat(variableContainer, def.name, numberDefinitions[def.name].GetValue(variableContainer));
            else if (def.type == "bool") FillBool(variableContainer, def.name, boolDefinitions[def.name].GetValue(variableContainer));
            else if (def.type == "vec3") FillVector3(variableContainer, def.name, vector3Definitions[def.name].GetValue(variableContainer));
            else if (def.type == "vec2") FillVector2(variableContainer, def.name, vector2Definitions[def.name].GetValue(variableContainer));
        }

        private void FillDefinitionsCommon(RC.VariableContainer variableContainer, Definition[] definitions) {
            if (definitions == null) return;
            foreach (var def in definitions) {
                FillDefinition(variableContainer, def);
            }
        }

        protected void FillStaticDefinitionVariables(RC.VariableContainer variableContainer) {
            FillDefinitionsCommon(variableContainer, parameterContainer.staticDefinitions);
        }

        protected void FillBaseInitialVariables(RC.VariableContainer variableContainer, ObjectState state, ObjectState instanceState) {
            if (parameterContainer.parameters != null) {
                foreach (var param in parameterContainer.parameters) {
                    var realState = param.instanceSpecific ? instanceState : state;
                    if (param.type == "float") FillFloat(variableContainer, param.fullName(), realState.Float(param.fullName()));
                    else if (param.type == "int") FillFloat(variableContainer, param.fullName(), realState.Int(param.fullName()));
                    else if (param.type == "bool") FillBool(variableContainer, param.fullName(), realState.Bool(param.fullName()));
                    else if (param.type == "enum") FillFloat(variableContainer, param.fullName(), realState.Int(param.fullName()));
                    else if (param.type == "vec2") FillVector2(variableContainer, param.fullName(), realState.Vector2(param.fullName()));
                    else if (param.type == "vec3") FillVector3(variableContainer, param.fullName(), realState.Vector3(param.fullName()));
                }
            }
            FillStaticDefinitionVariables(variableContainer);
        }

        protected void FillBaseIterationVariables(RC.VariableContainer variableContainer) {
            FillDefinitionsCommon(variableContainer, parameterContainer.dynamicDefinitions);
        }

        protected void FillBaseLateVariables(RC.VariableContainer variableContainer, string category) {
            if (!lateDefinitionsDict.ContainsKey(category)) return;
            FillDefinitionsCommon(variableContainer, lateDefinitionsDict[category]);
        }

        protected void FillBaseSubVariables(RC.VariableContainer variableContainer, int c) {
            if (subTypes != null) {
                var locDef = subTypes[c].localDefinitions;
                if (locDef != null) {
                    foreach (var def in subTypes[c].localDefinitions) {
                        if (subConditions[c] == null || subConditions[c].GetValue(variableContainer)) {
                            if (def.type == "float" || def.type == "int") variableContainer.SetFloat(def.name, localNumberDefinitions[c][def.name].GetValue(variableContainer));
                            else if (def.type == "bool") variableContainer.SetFloat(def.name, localBoolDefinitions[c][def.name].GetValue(variableContainer) ? 1.0f : 0.0f);
                            else if (def.type == "vec3") variableContainer.SetVector3(def.name, localVector3Definitions[c][def.name].GetValue(variableContainer));
                            else if (def.type == "vec2") variableContainer.SetVector2(def.name, localVector2Definitions[c][def.name].GetValue(variableContainer));
                        }
                    }
                }
            }
        }

        public bool InternalParameterVisible(string name) {
            foreach (var p in parameterContainer.internalParametersSettings) {
                if (p.name == name) return p.enabled;
            }
            return true;
        }
    }
}

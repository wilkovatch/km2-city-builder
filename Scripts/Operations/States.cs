using System.Collections.Generic;
using Parameter = CityElements.Types.Parameter;

namespace Operations {
    //Editing of element states through the parameters defined by the core: array and custom element parameters,
    //types, and where each parameter is stored
    public static class States {
        //where a parameter of an element is stored: the instance specific ones are never part of presets
        public static string GetParameterPath(Parameter param, bool prependState = true) {
            return (prependState ? (param.instanceSpecific ? "instanceState." : "state.") : "") + "properties." + param.fullName();
        }

        //the parameters of the elements of an array parameter, or of a custom element parameter
        public static Parameter[] GetSubparams(Parameter param) {
            if (param.arrayProperties != null) {
                if (param.arrayProperties.customElementType != null) {
                    return CityElements.Types.Parsers.TypeParser.GetCustomTypes()[param.arrayProperties.customElementType].parameters;
                }
                return param.arrayProperties.elementProperties;
            } else if (param.customElementType != null) {
                return CityElements.Types.Parsers.TypeParser.GetCustomTypes()[param.customElementType].parameters;
            }
            GameEngine.LogWarning("invalid subparameters for parameter " + param.fullName());
            return null;
        }

        public static Parameter[] GetArrayProperties(CityElements.Types.ArrayProperties type) {
            if (type == null) return null;
            if (type.customElementType != null) {
                return CityElements.Types.Parsers.TypeParser.GetCustomTypes()[type.customElementType].parameters;
            }
            return type.elementProperties;
        }

        //the name of the parameter used to label the elements of an array
        public static string GetArrayLabel(CityElements.Types.ArrayProperties type) {
            if (type == null) return null;
            if (type.customElementType != null) {
                return CityElements.Types.Parsers.TypeParser.GetCustomTypes()[type.customElementType].label;
            }
            return type.elementLabel;
        }

        public static List<ObjectState> GetArray(ObjectState owner, Parameter param) {
            var arr = owner.Array<ObjectState>(param.fullName());
            return arr == null ? new List<ObjectState>() : new List<ObjectState>(arr);
        }

        static void SetArray(ObjectState owner, Parameter param, List<ObjectState> list) {
            owner.SetArray(param.fullName(), list.ToArray());
        }

        public static bool IsArrayFull(ObjectState owner, Parameter param) {
            var max = param.arrayProperties?.maxElements ?? 0;
            return max > 0 && GetArray(owner, param).Count >= max;
        }

        //adds an element with the default values of its parameters, returns its index
        public static int AddArrayElement(ObjectState owner, Parameter param) {
            var arr = GetArray(owner, param);
            var newObj = ObjectState.CreateFromDefaultProperties(GetArrayProperties(param.arrayProperties));
            arr.Add(newObj);
            SetArray(owner, param, arr);
            return arr.Count - 1;
        }

        //adds a copy of the element at the end of the array
        public static bool CloneArrayElement(ObjectState owner, Parameter param, int i) {
            var arr = GetArray(owner, param);
            if (arr.Count == 0 || i < 0 || i >= arr.Count) {
                GameEngine.LogWarning("Unable to clone, array is empty");
                return false;
            }
            arr.Add((ObjectState)arr[i].Clone());
            SetArray(owner, param, arr);
            return true;
        }

        //swaps the element with the next one (positive) or the previous one
        public static bool ShiftArrayElement(ObjectState owner, Parameter param, int i, bool positive) {
            var arr = GetArray(owner, param);
            if (arr.Count == 0) {
                GameEngine.LogWarning("Unable to shift, array is empty");
                return false;
            }
            if (i < 0 || i >= arr.Count || (positive && i >= arr.Count - 1) || (!positive && i <= 0)) {
                GameEngine.LogWarning("Unable to shift");
                return false;
            }
            var shift = positive ? 1 : -1;
            var elem1 = arr[i];
            arr[i] = arr[i + shift];
            arr[i + shift] = elem1;
            SetArray(owner, param, arr);
            return true;
        }

        public static bool DeleteArrayElement(ObjectState owner, Parameter param, int i) {
            var arr = GetArray(owner, param);
            if (arr.Count == 0 || i < 0 || i >= arr.Count) {
                GameEngine.LogWarning("Unable to delete, array is empty");
                return false;
            }
            ArrayObject.PruneSelections(arr[i]);
            arr.RemoveAt(i);
            SetArray(owner, param, arr);
            return true;
        }

        //the state of a custom element parameter, created empty if missing
        public static ObjectState EnsureCustomElement(ObjectState owner, Parameter param) {
            var subObj = owner.State(param.fullName());
            if (subObj == null) {
                subObj = new ObjectState();
                owner.SetState(param.fullName(), subObj);
            }
            return subObj;
        }

        //changes the type, the other properties are kept (the missing ones read as defaults)
        public static void SetType(ObjectState state, string type) {
            state.SetStr("type", type);
        }

        //an element without a type gets the first one
        public static void EnsureType<T>(ObjectState state, Dictionary<string, T> types) {
            var cur = state.Str("type", null);
            if (!string.IsNullOrEmpty(cur)) return;
            foreach (var key in types.Keys) {
                state.SetStr("type", key);
                return;
            }
        }

        public static void FlagAsChanged(IObjectWithState obj) {
            obj?.GetState()?.FlagAsChanged();
        }
    }
}

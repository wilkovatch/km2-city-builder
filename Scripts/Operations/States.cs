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

        //the parametric object parameter of the elements of an array whose elements are only that (e.g. the points of a path), null otherwise
        public static Parameter GetSingleObjectInstanceSubparam(Parameter param) {
            if (param?.type != "array") return null;
            var subparams = GetSubparams(param);
            return subparams != null && subparams.Length == 1 && subparams[0].type == "objectInstance" ? subparams[0] : null;
        }

        //adds an element to an array of single parametric objects (see GetSingleObjectInstanceSubparam), with the object at
        //the position relative to its base (which is the origin for the objects of custom elements), returns its index (-1 if not added)
        public static int AddArrayObjectAt(ObjectState owner, Parameter param, Godot.Vector3 localPosition) {
            var objParam = GetSingleObjectInstanceSubparam(param);
            if (objParam == null || IsArrayFull(owner, param)) return -1;
            var index = AddArrayElement(owner, param);
            var elem = GetArray(owner, param)[index];
            var objState = elem.State(objParam.fullName()) ?? new ObjectState();
            objState.SetVector3("localPosition", localPosition);
            elem.SetState(objParam.fullName(), objState);
            return index;
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

        //References: the value of a reference parameter is another element of the city, {"kind": ..., "id": ...} (the
        //registry id); referenceTypes lists the kinds it accepts (road, intersection, lowLevel, lowLevel/<type>...)

        //whether a reference parameter of the owner can reference the element (not itself, a ghost or a deleted element)
        public static bool AcceptsReference(Parameter param, object owner, object element) {
            var kind = History.ElementSnapshot.ReferenceKind(element);
            if (kind == null || element == owner || LowLevel.IsGhost(element) || IsDeleted(element)) return false;
            if (param.referenceTypes == null || param.referenceTypes.Length == 0) return true;
            var type = element is LowLevelElement lowLevel ? lowLevel.TypeName : null;
            foreach (var t in param.referenceTypes) {
                if (t == kind || (type != null && t == kind + "/" + type)) return true;
            }
            return false;
        }

        public static ObjectState MakeReference(object element) {
            var reference = new ObjectState();
            reference.SetStr("kind", History.ElementSnapshot.ReferenceKind(element));
            reference.SetInt("id", History.ElementRegistry.GetId(element));
            return reference;
        }

        //the element of a reference (null if none, or if it was deleted)
        public static object ResolveReference(ObjectState reference) {
            if (reference == null) return null;
            var element = History.UndoManager.GetAliveElement(reference.Int("id", -1));
            return element == null || IsDeleted(element) ? null : element;
        }

        static bool IsDeleted(object element) {
            switch (element) {
                case Road road: return road.deleted;
                case Intersection intersection: return intersection.IsDeleted();
                case TerrainPatch patch: return patch.IsDeleted();
                case LowLevelElement lowLevel: return lowLevel.IsDeleted();
                default: return false;
            }
        }
    }
}

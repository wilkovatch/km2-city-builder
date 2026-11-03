using System.Collections.Generic;
using CityElements.Types;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Operations {
    //The props of the prop rules: the state of a props container of an element has a props type, which gives its
    //parameters, its panel and how it's placed. What the types are made of is up to the core.
    public static class Props {
        const string CategoryPrefix = "props/";

        public static CityElements.Types.Runtime.PropsType TypeOf(ObjectState state) {
            var type = state?.Str("type", null);
            return type != null && TP.GetPropsTypes().TryGetValue(type, out var res) ? res : null;
        }

        //the types that a props container of an element can have, without the limits of the element
        public static List<string> ContainerTypes() {
            var res = new List<string>();
            foreach (var t in TP.GetPropsTypes()) {
                if (!t.Value.typeData.nestedOnly) res.Add(t.Key);
            }
            return res;
        }

        // Presets: a category for each type, as the custom elements

        public static string PresetCategory(string type) => CategoryPrefix + type;

        public static string TypeOfCategory(string category) {
            if (category == null || !category.StartsWith(CategoryPrefix, System.StringComparison.Ordinal)) return null;
            var type = category.Substring(CategoryPrefix.Length);
            return TP.GetPropsTypes().ContainsKey(type) ? type : null;
        }

        public static ObjectState DefaultPreset(string type) {
            var state = ObjectState.CreateFromDefaultProperties(TP.GetPropsTypes()[type].parameterContainer.parameters ?? new Parameter[0]);
            state.SetStr("type", type);
            state.Name = Presets.DefaultName;
            return state;
        }

        //a new state of the type: its first preset
        public static ObjectState NewState(string type) {
            var state = (ObjectState)PresetManager.GetPresets().GetList(PresetCategory(type))[0].Clone();
            state.SetStr("type", type);
            state.FlagAsChanged();
            return state;
        }

        //gives the state another type: it keeps the values of the parameters both types have, if the new type allows them,
        //and gets those of the first preset of the new type for the others; it's not linked to a preset of the old type anymore
        public static void SwitchType(ObjectState state, string type) {
            var oldParameters = TypeOf(state)?.parameterContainer.parameters ?? new Parameter[0];
            var kept = new Dictionary<string, object>();
            foreach (var p in TP.GetPropsTypes()[type].parameterContainer.parameters ?? new Parameter[0]) {
                var shared = System.Array.Exists(oldParameters, x => x.fullName() == p.fullName() && x.type == p.type);
                if (shared && AllowedValue(state, p) is object value) kept[p.fullName()] = value;
            }
            state.ReplaceWith(NewState(type));
            foreach (var value in kept) {
                if (value.Value is ObjectState[] items) state.SetArray(value.Key, items);
                else state.SetProperty(value.Key, value.Value);
            }
            state.Name = "";
            state.SetStr("type", type);
        }

        //the value of a parameter of another type as this parameter allows it (for a list of props: the elements it allows),
        //null if there's nothing it allows
        static object AllowedValue(ObjectState state, Parameter parameter) {
            var name = parameter.fullName();
            if (!state.properties.TryGetValue(name, out var value) || value == null) return null;
            if (parameter.range != null && !PropPlacerGeometry.InRange(state, parameter)) return null;
            var array = parameter.arrayProperties;
            if (parameter.type != "array" || array?.propsTypes == null) return value;
            var items = new List<ObjectState>();
            foreach (var item in state.Array<ObjectState>(name) ?? new ObjectState[0]) {
                if (System.Array.IndexOf(array.propsTypes, item.Str("type", "")) < 0) continue;
                if (array.maxElements <= 0 || items.Count < array.maxElements) items.Add(item);
            }
            return items.Count > 0 ? items.ToArray() : null;
        }

        //applies a preset to a state keeping the same object (the fields of the panels are bound to it)
        public static void ApplyPreset(ObjectState state, ObjectState preset) {
            state.ReplaceWith(preset);
            state.FlagAsChanged();
        }

        // The structure of a props state

        //the array parameters of the type containing other props
        public static IEnumerable<Parameter> Lists(ObjectState state) {
            var parameters = TypeOf(state)?.parameterContainer.parameters;
            if (parameters == null) yield break;
            foreach (var p in parameters) {
                if (p.type == "array" && p.arrayProperties?.propsTypes != null) yield return p;
            }
        }

        //the states that place props (those of a type that isn't a group), in order: the state, then the elements of its lists
        public static IEnumerable<ObjectState> Placing(ObjectState state) {
            var type = TypeOf(state);
            if (type == null) yield break;
            if (!type.IsGroup) yield return state;
            foreach (var list in Lists(state)) {
                foreach (var item in state.Array<ObjectState>(list.fullName()) ?? new ObjectState[0]) {
                    foreach (var placing in Placing(item)) yield return placing;
                }
            }
        }

        public static void NormalizeRule(ObjectState rule) {
            if (rule == null) return;
            foreach (var value in rule.properties.Values) {
                if (value is not ObjectState container) continue;
                var type = TypeOf(container);
                if (type == null) throw new System.ArgumentException("unknown props type");
                States.Normalize(container, type.parameterContainer.parameters);
            }
        }

        //a props container without a type gets the first one
        public static void EnsureType(ObjectState container) {
            if (!string.IsNullOrEmpty(container.Str("type", null))) return;
            var types = ContainerTypes();
            if (types.Count > 0) container.SetStr("type", types[0]);
        }

        public static Parameter GetListParameter(ObjectState state, string list) {
            foreach (var p in Lists(state)) {
                if (p.fullName() == list) return p;
            }
            return null;
        }

        //the first parameter of the type with this parameter type (mesh, meshList)
        public static Parameter FindParameter(ObjectState state, string parameterType) {
            foreach (var p in TypeOf(state)?.parameterContainer.parameters ?? new Parameter[0]) {
                if (p.type == parameterType) return p;
            }
            return null;
        }

        // The meshes of a meshList parameter

        static List<string> GetMeshes(ObjectState state, string list) {
            var meshes = state.Array<string>(list);
            return meshes == null ? new List<string>() : new List<string>(meshes);
        }

        public static void AddMesh(ObjectState state, string list, string mesh) {
            var lst = GetMeshes(state, list);
            lst.Add(mesh);
            state.SetArray(list, lst.ToArray());
        }

        public static bool SetMesh(ObjectState state, string list, int i, string mesh) {
            var lst = GetMeshes(state, list);
            if (i < 0 || i >= lst.Count) return false;
            lst[i] = mesh;
            state.SetArray(list, lst.ToArray());
            return true;
        }

        public static bool RemoveMesh(ObjectState state, string list, int i) {
            var lst = GetMeshes(state, list);
            if (i < 0 || i >= lst.Count) return false;
            lst.RemoveAt(i);
            state.SetArray(list, lst.ToArray());
            return true;
        }

        // Prop rules: the props containers of an element (a road, or a custom element of a type with props
        // containers), in "propRule" of its state

        public static ObjectState GetPropRule(ObjectState owner, bool createIfNull) {
            if (owner == null) return null;
            var res = owner.State("propRule");
            if (res == null && createIfNull) {
                res = new ObjectState(true);
                SetPropRule(owner, res);
            }
            return res;
        }

        public static void SetPropRule(ObjectState owner, ObjectState rule) {
            if (owner != null && TP.GetCustomElementTypes().TryGetValue(owner.Str("type", ""), out var type)
                && type.typeData.settings.propPlacer != null) {
                var candidate = (ObjectState)owner.Clone();
                candidate.SetState("propRule", rule);
                PropPlacerGeometry.Validate(candidate, type.name);
            }
            owner?.SetState("propRule", rule);
        }

        public static bool IsPropContainerEnabled(ObjectState owner, string container) {
            var rule = GetPropRule(owner, false);
            return rule != null && rule.State(container) != null;
        }

        //the types allowed in a props container of the element
        public static List<string> AllowedTypes(ObjectState owner, string container) {
            var strip = PropPlacerGeometry.Strip(owner, container);
            return strip?.allowedContainerTypes?.Length > 0 ? new List<string>(strip.allowedContainerTypes) : ContainerTypes();
        }

        public static void EnablePropContainer(ObjectState owner, string container, bool enabled) {
            if (owner == null) return;
            ObjectState value = null;
            if (enabled) {
                var types = AllowedTypes(owner, container);
                value = types.Count > 0 ? NewState(types[0]) : new ObjectState(true);
            }
            GetPropRule(owner, true).SetState(container, value);
            owner.FlagAsChanged();
        }
    }
}

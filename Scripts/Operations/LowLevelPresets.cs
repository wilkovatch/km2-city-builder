using System;
using System.Collections.Generic;
using CityElements.Types;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Operations {
    //Property presets never carry point geometry or instance references. Creation memory is local to the loaded city.
    public static class LowLevelPresets {
        const string Prefix = "lowLevel/";
        static readonly Dictionary<string, string> lastPresets = new();

        public static string Category(string type) => type != null && TP.GetLowLevelTypes().TryGetValue(type, out var t)
            && t.typeData.settings.presets ? Prefix + type : null;

        public static string TypeOfCategory(string category) {
            if (category == null || !category.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            var type = category.Substring(Prefix.Length);
            if (Category(type) == null) throw new ArgumentException("presets are not enabled for low-level type '" + type + "'");
            return type;
        }

        public static ObjectState DefaultState(string type) {
            var state = new ObjectState();
            state.SetStr("type", type);
            if (TP.GetLowLevelTypes().TryGetValue(type, out var t) && t.typeData.settings.spline is LowLevelSplineSettings spline) {
                state.SetInt("curveType", spline.defaultCurveType);
                state.SetInt("segments", spline.segments);
                state.SetBool("segmentsPer100m", spline.segmentsPer100m);
                state.SetFloat("hermiteTension", 0.5f);
            }
            return state;
        }

        public static ObjectState DefaultPreset(string type) {
            var state = DefaultState(type);
            var defaults = TP.GetLowLevelTypes()[type].typeData.settings.defaultPreset;
            if (defaults != null) foreach (var p in defaults.properties) state.SetProperty(p.Key, p.Value);
            state.Name = Presets.DefaultName;
            return Extract(state, type);
        }

        public static void Validate(ObjectState state, string type) {
            if (state.properties.TryGetValue("type", out var value) && (value is not string typeName || typeName != type)) {
                throw new ArgumentException("preset type must be '" + type + "'");
            }
            var t = TP.GetLowLevelTypes()[type];
            States.Normalize(state, t.parameterContainer.parameters);
            var spline = t.typeData.settings.spline;
            if (spline != null && state.properties.ContainsKey("curveType") && Array.IndexOf(spline.curveTypes, state.Int("curveType")) < 0) {
                throw new ArgumentException("curve type is not allowed by this spline type");
            }
        }

        //The single saved state also holds instance-specific parameters, which are excluded recursively.
        static void Filter(ObjectState state, ObjectState original, Parameter[] parameters, bool restore) {
            foreach (var p in parameters ?? Array.Empty<Parameter>()) {
                var key = p.fullName();
                if (p.instanceSpecific || p.type == "reference") {
                    state.properties.Remove(key);
                    if (restore && original != null && original.properties.TryGetValue(key, out var value)) state.SetProperty(key, value);
                } else if (p.type == "customElement") {
                    var child = state.State(key);
                    var oldChild = original?.State(key);
                    if (child != null || (restore && oldChild != null)) {
                        child ??= new ObjectState();
                        Filter(child, oldChild, States.GetSubparams(p), restore);
                        if (child.properties.Count > 0) state.SetState(key, child);
                    }
                } else if (p.type == "array") {
                    var items = state.Array<ObjectState>(key);
                    var old = original?.Array<ObjectState>(key);
                    for (int i = 0; i < (items?.Length ?? 0); i++) Filter(items[i], i < (old?.Length ?? 0) ? old[i] : null, States.GetSubparams(p), restore);
                }
            }
        }

        public static ObjectState Extract(ObjectState state, string type) {
            var copy = DefaultState(type);
            foreach (var p in ((ObjectState)state.Clone()).properties) copy.SetProperty(p.Key, p.Value);
            Validate(copy, type);
            copy.SetStr("type", type);
            Filter(copy, null, TP.GetLowLevelTypes()[type].parameterContainer.parameters, false);
            return copy;
        }

        public static ObjectState Apply(ObjectState original, ObjectState preset, string type) {
            var copy = Extract(preset, type);
            Filter(copy, original, TP.GetLowLevelTypes()[type].parameterContainer.parameters, true);
            original.ReplaceWith(copy);
            original.FlagAsChanged();
            return original;
        }

        public static ObjectState CreationState(string type) {
            var category = Category(type);
            if (category == null) return DefaultState(type);
            PresetManager.GetPresets(); //also clears session memory after a city switch
            var preset = lastPresets.TryGetValue(type, out var name) ? PresetManager.GetPresetByName(name, category) : null;
            return Extract(preset ?? PresetManager.GetPresets().GetList(category)[0], type);
        }

        public static void Remember(string type, string name) {
            if (!string.IsNullOrEmpty(name)) lastPresets[type] = name;
        }

        public static void ClearLastPresets() => lastPresets.Clear();

        public static bool ChangeLinked(ElementManager manager, string category, string name, bool unlink) {
            var type = TypeOfCategory(category);
            if (type == null) return false;
            var preset = unlink ? null : PresetManager.GetPresetByName(name, category);
            if (preset != null) Validate((ObjectState)preset.Clone(), type);
            foreach (var element in manager.lowLevelElements) {
                if (element.IsDeleted() || element.TypeName != type || element.state.Name != name) continue;
                History.UndoManager.Touch(element);
                if (unlink) element.state.Name = "";
                else Apply(element.state, preset, type);
            }
            return true;
        }
    }
}

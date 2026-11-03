using System;
using System.Collections.Generic;
using CityElements.Types;
using TP = CityElements.Types.Parsers.TypeParser;
using Vector3 = Godot.Vector3;

namespace Operations {
    //Property presets carry only opted-in point transforms, never positions or instance references.
    public static class CustomElementPresets {
        const string Prefix = "customElement/";
        static readonly Dictionary<string, string> lastPresets = new();

        public static string Category(string type) => type != null && TP.GetCustomElementTypes().TryGetValue(type, out var t)
            && t.typeData.settings.presets ? Prefix + type : null;

        //the categories of the cities saved before the rename (lowLevel/<type>)
        public static void RenameOldCategories(Dictionary<string, ObjectState[]> presets) {
            foreach (var key in new List<string>(presets.Keys)) {
                if (!key.StartsWith("lowLevel/", StringComparison.Ordinal)) continue;
                var category = Prefix + key.Substring("lowLevel/".Length);
                if (!presets.ContainsKey(category)) presets[category] = presets[key];
                presets.Remove(key);
            }
        }

        public static string TypeOfCategory(string category) {
            if (category == null || !category.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            var type = category.Substring(Prefix.Length);
            if (Category(type) == null) throw new ArgumentException("presets are not enabled for custom element type '" + type + "'");
            return type;
        }

        public static ObjectState DefaultState(string type) {
            var state = new ObjectState();
            if (TP.GetCustomElementTypes().TryGetValue(type, out var defaults) && defaults.typeData.settings.defaultPreset != null) {
                foreach (var p in ((ObjectState)defaults.typeData.settings.defaultPreset.Clone()).properties) state.SetProperty(p.Key, p.Value);
            }
            state.SetStr("type", type);
            if (TP.GetCustomElementTypes().TryGetValue(type, out var propType) && propType.typeData.settings.propPlacer != null) {
                state.SetBool("projectToGround", propType.typeData.settings.propPlacer.projectToGround);
            }
            if (TP.GetCustomElementTypes().TryGetValue(type, out var t) && t.typeData.settings.spline is CustomElementSplineSettings spline) {
                state.SetInt("curveType", spline.defaultCurveType);
                state.SetInt("segments", spline.segments);
                state.SetBool("segmentsPer100m", spline.segmentsPer100m);
                if (System.Array.IndexOf(spline.curveTypes, 1) >= 0) state.SetFloat("hermiteTension", 0.5f);
            }
            return state;
        }

        public static ObjectState DefaultPreset(string type) {
            var state = DefaultState(type);
            var defaults = TP.GetCustomElementTypes()[type].typeData.settings.defaultPreset;
            if (defaults != null) foreach (var p in defaults.properties) state.SetProperty(p.Key, p.Value);
            state.Name = Presets.DefaultName;
            return Extract(state, type);
        }

        public static void Validate(ObjectState state, string type) {
            if (state.properties.TryGetValue("type", out var value) && (value is not string typeName || typeName != type)) {
                throw new ArgumentException("preset type must be '" + type + "'");
            }
            var t = TP.GetCustomElementTypes()[type];
            States.Normalize(state, t.parameterContainer.parameters);
            PropPlacerGeometry.Validate(state, type);
            var spline = t.typeData.settings.spline;
            if (spline != null) SplineCurves.ValidateState(spline, state);
            if (spline != null && state.properties.ContainsKey("curveType") && Array.IndexOf(spline.curveTypes, state.Int("curveType")) < 0) {
                throw new ArgumentException("curve type is not allowed by this spline type");
            }
        }

        //The single saved state also holds instance-specific parameters, which are excluded recursively.
        static void Filter(ObjectState state, ObjectState original, Parameter[] parameters, bool restore) {
            foreach (var p in parameters ?? Array.Empty<Parameter>()) {
                var key = p.fullName();
                if (p.instanceSpecific || p.type == "reference" || p.type == "objectInstance") {
                    state.properties.Remove(key);
                    if (restore && original != null && original.properties.TryGetValue(key, out var value)) state.SetProperty(key, value);
                } else if (p.type == "customCityProperties") {
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
                    for (int i = 0; i < (items?.Length ?? 0); i++) Filter(items[i], i < (old?.Length ?? 0) ? old[i] : null, States.GetSubparams(p, items[i]), restore);
                }
            }
        }

        public static ObjectState Extract(ObjectState state, string type) {
            var copy = DefaultState(type);
            foreach (var p in ((ObjectState)state.Clone()).properties) copy.SetProperty(p.Key, p.Value);
            copy.RemoveProperty(PropertyContainers.OwnerKey);
            Validate(copy, type);
            copy.SetStr("type", type);
            Filter(copy, null, TP.GetCustomElementTypes()[type].parameterContainer.parameters, false);
            var point = TP.GetCustomElementTypes()[type].typeData.settings.point;
            TransformValue(copy, "pointRotation", point?.rotatable == true && point.presetRotation, Vector3.Zero);
            TransformValue(copy, "pointScale", point?.scalable == true && point.presetScale,
                global::CustomElements.PointTree.ToVector(point?.defaultScale, Vector3.One));
            return copy;
        }

        //Canonical vectors also make presets read from JSON compare equal to the live point transforms.
        static void TransformValue(ObjectState state, string key, bool enabled, Vector3 fallback) {
            if (!enabled) {
                state.RemoveProperty(key);
                return;
            }
            var value = state.Vector3(key, fallback);
            if (state.properties.TryGetValue(key, out var raw)) {
                if (raw is ObjectState vector) {
                    foreach (var axis in new[] { "x", "y", "z" }) {
                        if (!vector.properties.TryGetValue(axis, out var component) || component is not (int or float or double or long)) {
                            throw new ArgumentException(key + " needs numeric x, y and z components");
                        }
                        vector.SetFloat(axis, Convert.ToSingle(component));
                    }
                    value = state.Vector3(key, fallback);
                } else if (raw is not Vector3 && raw is not global::States.SerializableVector3) {
                    throw new ArgumentException(key + " needs a vector");
                }
            }
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) throw new ArgumentException(key + " must be finite");
            state.SetVector3(key, value);
        }

        public static ObjectState Extract(CustomElement element) {
            var copy = (ObjectState)element.state.Clone();
            if (element.Settings?.point is CustomElementPointSettings settings && element.points.Get("center") is global::CustomElements.PointLeaf leaf) {
                if (settings.rotatable && settings.presetRotation) copy.SetVector3("pointRotation", leaf.point.GetRotation());
                if (settings.scalable && settings.presetScale) copy.SetVector3("pointScale", leaf.point.GetScale());
            }
            return Extract(copy, element.TypeName);
        }

        public static ObjectState Apply(CustomElement element, ObjectState preset) {
            var copy = Extract(preset, element.TypeName);
            History.UndoManager.Touch(element);
            if (element.Settings?.point is CustomElementPointSettings settings && element.points.Get("center") is global::CustomElements.PointLeaf leaf) {
                if ((settings.rotatable && settings.presetRotation) || (settings.scalable && settings.presetScale)) History.UndoManager.Touch(leaf.point);
                if (settings.rotatable && settings.presetRotation) leaf.point.SetRotation(copy.Vector3("pointRotation"));
                if (settings.scalable && settings.presetScale) leaf.point.SetScale(copy.Vector3("pointScale"));
            }
            Apply(element.state, copy, element.TypeName);
            //The live transforms belong to the point; the state fields are only for presets and creation drafts.
            element.state.RemoveProperty("pointRotation");
            element.state.RemoveProperty("pointScale");
            return element.state;
        }

        public static ObjectState Apply(ObjectState original, ObjectState preset, string type) {
            var copy = Extract(preset, type);
            PropertyContainers.Assign(copy, PropertyContainers.Owner(original));
            Filter(copy, original, TP.GetCustomElementTypes()[type].parameterContainer.parameters, true);
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
            foreach (var element in manager.customElements) {
                if (element.IsDeleted() || element.TypeName != type || element.state.Name != name) continue;
                History.UndoManager.Touch(element);
                if (unlink) element.state.Name = "";
                else Apply(element, preset);
            }
            return true;
        }
    }
}

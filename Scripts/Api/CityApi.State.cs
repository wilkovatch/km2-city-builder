using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Parameter = CityElements.Types.Parameter;
using SM = StringManager;

namespace Api {
    //The generic editing of the states: what the parameter fields, the type and preset dropdowns and the array lists
    //of the editor panels do
    public partial class CityApi {
        static object ConvertValue(string type, JToken value, string[] enumValues, string name) {
            if (value == null) throw ApiException.InvalidValue("missing value for '" + name + "'");
            switch (type) {
                case "bool":
                    if (value.Type != JTokenType.Boolean) throw ApiException.InvalidValue("'" + name + "' needs a boolean");
                    return value.Value<bool>();
                case "int":
                    if (value.Type == JTokenType.Integer) return value.Value<int>();
                    if (value.Type == JTokenType.Float && value.Value<double>() % 1 == 0) return (int)value.Value<double>();
                    throw ApiException.InvalidValue("'" + name + "' needs an integer");
                case "float":
                    if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return value.Value<float>();
                    throw ApiException.InvalidValue("'" + name + "' needs a number");
                case "enum":
                    var count = enumValues?.Length ?? 0;
                    if (value.Type == JTokenType.Integer) {
                        var i = value.Value<int>();
                        if (i < 0 || i >= count) throw ApiException.InvalidValue("'" + name + "' needs a value between 0 and " + (count - 1));
                        return i;
                    }
                    if (value.Type == JTokenType.String) {
                        var s = value.Value<string>();
                        for (int i = 0; i < count; i++) {
                            if (enumValues[i] == s || SM.GetOrKey(enumValues[i]) == s) return i;
                        }
                        throw ApiException.InvalidValue("'" + s + "' is not a value of '" + name + "' (" + string.Join(", ", enumValues ?? new string[0]) + ")");
                    }
                    throw ApiException.InvalidValue("'" + name + "' needs the index or the name of a value");
                case "string":
                case "texture":
                case "mesh":
                    if (value.Type != JTokenType.String) throw ApiException.InvalidValue("'" + name + "' needs a string");
                    return value.Value<string>();
                case "reference": {
                    //another element: its id ({"id": n} or n), or null for none
                    var id = value.Type == JTokenType.Object ? value["id"] : value;
                    if (id == null || id.Type == JTokenType.Null) return null;
                    if (id.Type != JTokenType.Integer) throw ApiException.InvalidValue("'" + name + "' needs the id of an element");
                    var target = History.UndoManager.GetAliveElement(id.Value<int>());
                    var kind = History.ElementSnapshot.ReferenceKind(target);
                    if (kind == null) throw ApiException.NotFound("element " + id);
                    var reference = new ObjectState();
                    reference.SetStr("kind", kind);
                    reference.SetInt("id", id.Value<int>());
                    return reference;
                }
                case "objectInstance":
                    throw ApiException.InvalidParam("'" + name + "' is a parametric object: use objects.setTransform and objects.setMesh");
                case "array":
                    throw ApiException.InvalidParam("'" + name + "' is an array: use state.array.* and the array element references");
                case "customElement":
                    throw ApiException.InvalidParam("'" + name + "' is a custom element: use state.customElement.ensure and the custom element references");
                default:
                    throw ApiException.InvalidParam("parameters of type '" + type + "' can't be edited");
            }
        }

        //sets a value in a state through a path with array indices ("upperFloors.0.Slices.1.Width")
        static void SetPath(ObjectState state, string path, object value) {
            var parts = path.Split('.');
            object cur = state;
            for (int i = 0; i < parts.Length - 1; i++) {
                if (cur is ObjectState s) {
                    var arr = s.Array<ObjectState>(parts[i]);
                    cur = arr != null ? (object)arr : s.State(parts[i]);
                } else if (cur is ObjectState[] a && int.TryParse(parts[i], out var index) && index >= 0 && index < a.Length) {
                    cur = a[index];
                } else {
                    cur = null;
                }
                if (cur == null) throw ApiException.NotFound("'" + string.Join(".", parts, 0, i + 1) + "'");
            }
            if (!(cur is ObjectState target)) throw ApiException.NotFound("'" + path + "'");
            target.SetProperty(parts[parts.Length - 1], value);
            state.FlagAsChanged();
        }

        static object GetPath(ObjectState state, string path) {
            object cur = state;
            foreach (var part in path.Split('.')) {
                if (cur is ObjectState s) {
                    cur = s.properties.TryGetValue(part, out var v) ? v : null;
                } else if (cur is ObjectState[] a && int.TryParse(part, out var index) && index >= 0 && index < a.Length) {
                    cur = a[index];
                } else {
                    return null;
                }
            }
            return cur;
        }

        static object SchemaOf(Parameter p) {
            return new {
                name = p.fullName(),
                type = p.type,
                label = p.label != null ? SM.GetOrKey(p.label) : null,
                instanceSpecific = p.instanceSpecific,
                defaultValue = p.defaultValueInArray,
                enumValues = p.enumLabels,
                maxElements = p.arrayProperties?.maxElements,
                elementParameters = (p.type == "array" || p.type == "customElement") ? SchemaOf(Operations.States.GetSubparams(p)) : null,
                objectInstance = p.objectInstanceSettings == null ? null : new { condition = p.objectInstanceSettings.condition, allowCustomModel = p.objectInstanceSettings.allowCustomModel }
            };
        }

        static List<object> SchemaOf(Parameter[] parameters) {
            var res = new List<object>();
            if (parameters == null) return res;
            foreach (var p in parameters) res.Add(SchemaOf(p));
            return res;
        }

        static List<object> SchemaOf(List<Operations.Builtins.Property> builtins) {
            var res = new List<object>();
            foreach (var p in builtins) res.Add(new { name = p.name, type = p.type, instanceSpecific = p.instanceSpecific, enumValues = p.enumValues });
            return res;
        }

        [ApiMethod("state.get", "The state (and instance state) of an element or part of one")]
        public object GetState(ElementRef @ref) {
            var t = GetStateTarget(@ref);
            return new { kind = t.kind, type = t.state?.Str("type", null), state = t.state, instanceState = t.instanceState };
        }

        [ApiMethod("state.import", "Imports core state values, optionally appending named array parameters (for city import scripts)")]
        public void ImportState(ElementRef @ref, JObject values, string[] append = null) {
            ApplyChange("import state", () => {
                var target = GetStateTarget(@ref);
                var incoming = values.ToObject<ObjectState>();
                Operations.States.Normalize(incoming, target.parameters);
                if (target.owner is LowLevelElement spline && spline.Settings?.spline != null && incoming.properties.ContainsKey("curveType") &&
                    System.Array.IndexOf(spline.Settings.spline.curveTypes, incoming.Int("curveType")) < 0) {
                    throw ApiException.InvalidValue("curve type is not allowed by this spline type");
                }
                foreach (var property in incoming.properties) {
                    if (target.FindParameter(property.Key) == null) throw ApiException.NotFound("parameter '" + property.Key + "'");
                }
                History.UndoManager.Touch(target.owner);
                void Assign(string name, object value) {
                    if (value is ObjectState child) target.state.SetState(name, child);
                    else if (value is ObjectState[] children) target.state.SetArray(name, children);
                    else target.state.SetProperty(name, value);
                }
                foreach (var property in incoming.properties) {
                    if (append != null && System.Array.IndexOf(append, property.Key) >= 0) {
                        if (values[property.Key] is not JArray array) throw ApiException.InvalidValue("append requires an array");
                        var existing = Json.ToToken(target.state.GetProperty<object>(property.Key, null)) as JArray ?? new JArray();
                        foreach (var item in array) existing.Add(item.DeepClone());
                        var merged = new JObject { [property.Key] = existing }.ToObject<ObjectState>();
                        Operations.States.Normalize(merged, target.parameters);
                        Assign(property.Key, merged.properties[property.Key]);
                    } else Assign(property.Key, property.Value);
                }
                target.state.FlagAsChanged();
                target.afterChange?.Invoke();
            });
        }

        [ApiMethod("state.schema", "The parameters of the core and the built-in properties an element or part of one can have, and its types and presets")]
        public object GetSchema(ElementRef @ref) {
            var t = GetStateTarget(@ref);
            return new {
                kind = t.kind,
                parameters = SchemaOf(t.parameters),
                builtins = SchemaOf(Operations.Builtins.GetProperties(t.kind, t.roadType)),
                types = t.types != null ? new List<string>(t.types()) : null,
                presetCategory = t.presetCategory,
                presets = t.presetCategory != null ? PresetNames(t.presetCategory, t.presetAllowedTypes) : null
            };
        }

        [ApiMethod("state.getParam", "The value of a parameter or built-in property (null if not set: the default applies)")]
        public object GetParam(ElementRef @ref, string name) {
            var t = GetStateTarget(@ref);
            var p = t.FindParameter(name);
            if (p != null) return GetPath(p.instanceSpecific ? t.instanceState : t.state, name);
            var b = Operations.Builtins.Find(t.kind, name, t.roadType);
            if (b != null) return GetPath(b.instanceSpecific ? t.instanceState : t.state, name);
            throw ApiException.NotFound("parameter '" + name + "' of " + t.kind);
        }

        [ApiMethod("state.setParam", "Sets a parameter of the core (bool, int, float, enum, string, texture, mesh)")]
        public object SetParam(ElementRef @ref, string name, JToken value) {
            return ApplyChange("set " + name, () => {
                var t = GetStateTarget(@ref);
                var p = t.FindParameter(name);
                if (p == null) throw ApiException.NotFound("parameter '" + name + "' of " + t.kind + (t.parameters.Length == 0 ? " (it has none)" : ""));
                var v = ConvertValue(p.type, value, p.enumLabels, name);
                if (t.owner is LowLevelElement spline && name == "curveType" && spline.Settings?.spline != null &&
                    System.Array.IndexOf(spline.Settings.spline.curveTypes, System.Convert.ToInt32(v)) < 0) {
                    throw ApiException.InvalidValue("curve type is not allowed by this spline type");
                }
                if (v is ObjectState reference && p.type == "reference" && !Operations.States.AcceptsReference(p, t.owner, Operations.States.ResolveReference(reference))) {
                    throw ApiException.InvalidValue("'" + name + "' can't reference element " + reference.Int("id", -1)
                        + (p.referenceTypes != null ? " (" + string.Join(", ", p.referenceTypes) + ")" : ""));
                }
                var target = p.instanceSpecific ? t.instanceState : t.state;
                if (target == null) throw ApiException.InvalidParam("'" + name + "' is instance specific, but " + t.kind + " has no instance state");
                History.UndoManager.Touch(t.owner);
                target.SetProperty(p.fullName(), v);
                t.afterChange?.Invoke();
                return v;
            });
        }

        [ApiMethod("state.setBuiltin", "Sets a built-in property (see state.schema), e.g. the curve type of a road or the slices of a building side")]
        public object SetBuiltin(ElementRef @ref, string name, JToken value) {
            return ApplyChange("set " + name, () => {
                var t = GetStateTarget(@ref);
                var b = Operations.Builtins.Find(t.kind, name, t.roadType);
                if (b == null) throw ApiException.NotFound("built-in property '" + name + "' of " + t.kind);
                var v = ConvertValue(b.type, value, b.enumValues, name);
                var target = b.instanceSpecific ? t.instanceState : t.state;
                History.UndoManager.Touch(t.owner);
                SetPath(target, name, v);
                t.afterChange?.Invoke();
                return v;
            });
        }

        [ApiMethod("state.setType", "Changes the type: the other values are kept (the missing ones read as defaults)")]
        public object SetType(ElementRef @ref, string type) {
            return ApplyChange("set type", () => {
                var t = GetStateTarget(@ref);
                if (t.types == null) throw ApiException.InvalidOperation("the type of " + t.kind + " can't be changed");
                if (!new List<string>(t.types()).Contains(type)) throw ApiException.InvalidValue("unknown type '" + type + "' (" + string.Join(", ", t.types()) + ")");
                History.UndoManager.Touch(t.owner);
                Operations.States.SetType(t.state, type);
                t.afterChange?.Invoke();
                return type;
            });
        }

        [ApiMethod("state.applyPreset", "Gives the element the values of a preset (the instance specific values are kept)")]
        public object ApplyPreset(ElementRef @ref, string name) {
            return ApplyChange("load preset", () => {
                var t = GetStateTarget(@ref);
                if (t.presetCategory == null) throw ApiException.InvalidOperation(t.kind + " has no presets");
                var preset = Operations.Presets.Get(t.presetCategory, name);
                if (preset == null) throw ApiException.NotFound("preset '" + name + "' of " + t.presetCategory);
                if (t.presetAllowedTypes != null && t.presetAllowedTypes.Count > 0 && !t.presetAllowedTypes.Contains(preset.Str("type"))) {
                    throw ApiException.InvalidValue("the preset '" + name + "' has a type not allowed here");
                }
                History.UndoManager.Touch(t.owner);
                t.applyPreset(preset);
                t.afterChange?.Invoke();
                return name;
            });
        }

        [ApiMethod("state.applyContainerPreset", "Gives the parameters of a container (e.g. the rails of a road) the values of a preset of the container")]
        public object ApplyContainerPreset(ElementRef @ref, string container, string name) {
            return ApplyChange("load preset", () => {
                var t = GetStateTarget(@ref);
                var preset = Operations.Presets.Get(container, name);
                if (preset == null) throw ApiException.NotFound("preset '" + name + "' of " + container);
                History.UndoManager.Touch(t.owner);
                t.state.SetContainer(preset, container);
                t.state.FlagAsChanged();
                t.afterChange?.Invoke();
                return name;
            });
        }

        [ApiMethod("state.saveAsPreset", "Saves the values of the element as a preset (overwriting the one with the same name); the element gets linked to it")]
        public object SaveAsPreset(ElementRef @ref, string name) {
            if (string.IsNullOrWhiteSpace(name)) throw ApiException.InvalidValue("the preset name can't be empty");
            return ApplyChange("save preset", () => {
                var t = GetStateTarget(@ref);
                if (t.presetCategory == null) throw ApiException.InvalidOperation(t.kind + " has no presets");
                History.UndoManager.Touch(t.owner);
                Operations.Presets.Save(t.state, t.presetCategory, name);
                t.afterChange?.Invoke();
                return name;
            });
        }

        [ApiMethod("state.saveContainerPreset", "Saves the parameters of a container of the element (e.g. the rails of a road) as a preset of the container")]
        public object SaveContainerPreset(ElementRef @ref, string container, string name) {
            if (string.IsNullOrWhiteSpace(name)) throw ApiException.InvalidValue("the preset name can't be empty");
            return ApplyChange("save preset", () => {
                var t = GetStateTarget(@ref);
                History.UndoManager.Touch(t.owner);
                var c = t.state.GetContainer(container);
                c.Name = name;
                t.state.SetContainer(c, container);
                PresetManager.SavePreset((ObjectState)c.Clone(), container);
                t.afterChange?.Invoke();
                return name;
            });
        }

        [ApiMethod("elements.rename", "Renames an element")]
        public object Rename(ElementRef @ref, string name) {
            if (string.IsNullOrEmpty(name)) throw ApiException.InvalidValue("the name can't be empty");
            return ApplyChange("rename", () => {
                var e = Resolve(@ref);
                History.UndoManager.Touch(e);
                switch (e) {
                    case Intersection i: GameEngine.GameObject.SetName(i.geo, name); break;
                    case Road or TerrainPatch or BuildingLine or MeshInstance or LowLevelElement: GameEngine.GameObject.SetName((Godot.Node3D)e, name); break;
                    default: throw ApiException.WrongKind("road, intersection, terrainPatch, buildingLine, meshInstance or lowLevel", KindOf(e));
                }
                return name;
            });
        }

        // Arrays and custom elements

        (StateTarget t, Parameter p) GetArrayParam(ElementRef @ref, string param, string type) {
            var t = GetStateTarget(@ref);
            var p = t.FindParameter(param);
            if (p == null) throw ApiException.NotFound("parameter '" + param + "' of " + t.kind);
            if (p.type != type) throw ApiException.InvalidParam("'" + param + "' is not a" + (type == "array" ? "n array" : " custom element") + " parameter");
            return (t, p);
        }

        [ApiMethod("state.array.add", "Adds an element with the default values to an array parameter, returns its index")]
        public int ArrayAdd(ElementRef @ref, string param) {
            return ApplyChange("add element", () => {
                var (t, p) = GetArrayParam(@ref, param, "array");
                if (Operations.States.IsArrayFull(t.state, p)) throw new ApiException("limit_reached", "'" + param + "' can have at most " + p.arrayProperties.maxElements + " elements");
                History.UndoManager.Touch(t.owner);
                var res = Operations.States.AddArrayElement(t.state, p);
                t.afterChange?.Invoke();
                return res;
            });
        }

        [ApiMethod("state.array.clone", "Adds a copy of an element at the end of an array parameter, returns its index")]
        public int ArrayClone(ElementRef @ref, string param, int index) {
            return ApplyChange("clone element", () => {
                var (t, p) = GetArrayParam(@ref, param, "array");
                if (Operations.States.IsArrayFull(t.state, p)) throw new ApiException("limit_reached", "'" + param + "' can have at most " + p.arrayProperties.maxElements + " elements");
                History.UndoManager.Touch(t.owner);
                if (!Operations.States.CloneArrayElement(t.state, p, index)) throw ApiException.NotFound("element " + index + " of '" + param + "'");
                t.afterChange?.Invoke();
                return Operations.States.GetArray(t.state, p).Count - 1;
            });
        }

        [ApiMethod("state.array.move", "Swaps an element of an array parameter with the previous (up) or next (down) one, returns its new index")]
        public int ArrayMove(ElementRef @ref, string param, int index, string direction) {
            if (direction != "up" && direction != "down") throw ApiException.InvalidValue("direction must be 'up' or 'down'");
            return ApplyChange("move element", () => {
                var (t, p) = GetArrayParam(@ref, param, "array");
                History.UndoManager.Touch(t.owner);
                if (!Operations.States.ShiftArrayElement(t.state, p, index, direction == "down")) throw ApiException.InvalidOperation("element " + index + " can't be moved " + direction);
                t.afterChange?.Invoke();
                return direction == "down" ? index + 1 : index - 1;
            });
        }

        [ApiMethod("state.array.remove", "Removes an element of an array parameter")]
        public void ArrayRemove(ElementRef @ref, string param, int index) {
            ApplyChange("delete element", () => {
                var (t, p) = GetArrayParam(@ref, param, "array");
                History.UndoManager.Touch(t.owner);
                if (!Operations.States.DeleteArrayElement(t.state, p, index)) throw ApiException.NotFound("element " + index + " of '" + param + "'");
                t.afterChange?.Invoke();
                builder.NotifyVisibilityChange();
            });
        }

        [ApiMethod("state.customElement.ensure", "Creates the state of a custom element parameter if missing")]
        public void CustomElementEnsure(ElementRef @ref, string param) {
            ApplyChange("edit element", () => {
                var (t, p) = GetArrayParam(@ref, param, "customElement");
                History.UndoManager.Touch(t.owner);
                Operations.States.EnsureCustomElement(t.state, p);
                t.afterChange?.Invoke();
            });
        }
    }
}

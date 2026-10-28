using System.Collections.Generic;

namespace Operations {
    //The props containers of the road prop rules (see Roads.EnablePropContainer) and their elements
    public static class Props {
        public static void NormalizeRule(ObjectState rule) {
            if (rule == null) return;
            var elementTypes = CityElements.Types.Parsers.TypeParser.GetPropsElementTypes();
            foreach (var value in rule.properties.Values) {
                if (value is not ObjectState container) continue;
                var type = GetContainerType(container);
                if (type == null) throw new System.ArgumentException("unknown prop container type");
                foreach (var slot in type.parameters) {
                    var elements = slot.maxNumber == 1 ? new[] { container.State(slot.name) } : container.Array<ObjectState>(slot.name);
                    foreach (var e in elements ?? new ObjectState[0]) {
                        if (e == null) continue;
                        if (!elementTypes.TryGetValue(e.Str("type"), out var element)) throw new System.ArgumentException("unknown prop element type");
                        States.Normalize(e, element.parameterContainer.parameters);
                    }
                }
            }
        }
        //the type of the container: the first one if not set yet (as shown by its editor, which sets it)
        public static CityElements.Types.PropsContainerType GetContainerType(ObjectState container) {
            var types = CityElements.Types.Parsers.TypeParser.GetPropsContainersTypes();
            var type = container.Str("type", null);
            if (string.IsNullOrEmpty(type)) {
                foreach (var t in types.Values) return t;
                return null;
            }
            return types.TryGetValue(type, out var res) ? res : null;
        }

        public static void EnsureContainerType(ObjectState container) {
            States.EnsureType(container, CityElements.Types.Parsers.TypeParser.GetPropsContainersTypes());
        }

        public static CityElements.Types.PropsContainerParameter GetListParameter(ObjectState container, string list) {
            var type = GetContainerType(container);
            if (type == null) return null;
            foreach (var parameter in type.parameters) {
                if (parameter.name == list) return parameter;
            }
            return null;
        }

        //the types of the props elements allowed in a list of the container (all if empty)
        public static List<string> GetAllowedTypes(ObjectState container, string list) {
            var parameter = GetListParameter(container, list);
            return parameter?.allowedTypes != null ? new List<string>(parameter.allowedTypes) : new List<string>();
        }

        public static bool IsListFull(ObjectState container, string list) {
            var parameter = GetListParameter(container, list);
            if (parameter == null || parameter.maxNumber <= 0) return false;
            return (container.Array<ObjectState>(list)?.Length ?? 0) >= parameter.maxNumber;
        }

        //adds the first props element preset of an allowed type to the list, returns its index
        public static int AddElement(ObjectState container, string list) {
            EnsureContainerType(container);
            var elem = PresetManager.GetFirstPresetByAllowedTypes("propElem", GetAllowedTypes(container, list));
            var elems = new List<ObjectState>();
            var array = container.Array<ObjectState>(list);
            if (array != null) elems.AddRange(array);
            elems.Add(elem);
            container.SetArray(list, elems.ToArray());
            return elems.Count - 1;
        }

        public static bool RemoveElement(ObjectState container, string list, int i) {
            var array = container.Array<ObjectState>(list);
            if (array == null || i < 0 || i >= array.Length) return false;
            var elems = new List<ObjectState>(array);
            elems.RemoveAt(i);
            container.SetArray(list, elems.ToArray());
            return true;
        }

        //the element of a list of the container with a single element (maxNumber 1), created empty if missing
        public static ObjectState EnsureSingleElement(ObjectState container, string name) {
            EnsureContainerType(container);
            var elem = container.State(name);
            if (elem == null) {
                elem = new ObjectState();
                container.SetState(name, elem);
            }
            return elem;
        }

        // The meshes of a props element

        static List<string> GetMeshes(ObjectState elem) {
            var meshes = elem.Array<string>("meshes");
            return meshes == null ? new List<string>() : new List<string>(meshes);
        }

        public static void AddMesh(ObjectState elem, string mesh) {
            var lst = GetMeshes(elem);
            lst.Add(mesh);
            elem.SetArray("meshes", lst.ToArray());
        }

        public static bool SetMesh(ObjectState elem, int i, string mesh) {
            var lst = GetMeshes(elem);
            if (i < 0 || i >= lst.Count) return false;
            lst[i] = mesh;
            elem.SetArray("meshes", lst.ToArray());
            return true;
        }

        public static bool RemoveMesh(ObjectState elem, int i) {
            var lst = GetMeshes(elem);
            if (i < 0 || i >= lst.Count) return false;
            lst.RemoveAt(i);
            elem.SetArray("meshes", lst.ToArray());
            return true;
        }

        //the mesh of the props element types with a single one
        public static void SetSingleMesh(ObjectState elem, string mesh) {
            elem.SetStr("mesh", mesh);
        }

        // Prop rules: the props containers of an element (a road, or a low-level element of a type with props
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
            owner?.SetState("propRule", rule);
        }

        public static bool IsPropContainerEnabled(ObjectState owner, string container) {
            var rule = GetPropRule(owner, false);
            return rule != null && rule.State(container) != null;
        }

        public static void EnablePropContainer(ObjectState owner, string container, bool enabled) {
            if (owner == null) return;
            GetPropRule(owner, true).SetState(container, enabled ? new ObjectState(true) : null);
            owner.FlagAsChanged();
        }
    }
}

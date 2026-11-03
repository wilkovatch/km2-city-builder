using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using CityElements.Types;
using TP = CityElements.Types.Parsers.TypeParser;
using GO = GameEngine.GameObject;

namespace Operations {
    //Stable ownership by a city-property group, independent of its array index and runtime ArrayObject node.
    public static class PropertyContainers {
        public const string IdKey = "_elementContainerId";
        public const string OwnerKey = "_propertyContainerId";
        public sealed class Entry {
            public ObjectState state;
            public PropertyContainerSettings settings;
            public List<(string parameter, int? index)> path;
            public string parentId;
        }
        static readonly Dictionary<string, Entry> entries = new();
        public static ElementManager manager { get; private set; }

        public static string EnsureId(ObjectState state) {
            var id = state.Str(IdKey, "");
            if (id == "") state.SetStr(IdKey, id = Guid.NewGuid().ToString("N"));
            return id;
        }

        public static Entry Find(string id) => id != null && entries.TryGetValue(id, out var entry) ? entry : null;
        public static string Owner(ObjectState state) => state?.Str(OwnerKey, "") ?? "";
        public static void Assign(ObjectState state, string id) {
            var entry = Find(id);
            if (entry?.settings.filterParameter != null && entry.parentId != null) {
                state.SetInt(entry.settings.filterParameter, entry.settings.filterValue);
                id = entry.parentId;
            }
            if (string.IsNullOrEmpty(id)) state.RemoveProperty(OwnerKey);
            else state.SetStr(OwnerKey, id);
        }

        public static Entry Active(string category) {
            var singleton = entries.Values.FirstOrDefault(e => e.path.Count == 0 && e.settings.category == category && e.settings.tab != null);
            if (singleton != null) {
                var selected = ArrayObject.activeElements.TryGetValue(category, out var current) && current == singleton.state;
                return selected || manager?.builder?.IsEditingPropertyContainer(EnsureId(singleton.state), singleton.settings.tab) == true ? singleton : null;
            }
            if (category == null || !ArrayObject.activeElements.TryGetValue(category, out var state) || state == null) return null;
            var entry = Find(state.Str(IdKey, ""));
            return entry?.parentId == null || IsCurrent(entry.parentId) ? entry : null;
        }

        public static bool Accepts(Entry entry, string type) => entry != null && Array.IndexOf(entry.settings.types ?? Array.Empty<string>(), type) >= 0;
        public static string Capture(string category, string type) {
            var entry = Active(category);
            return Accepts(entry, type) ? EnsureId(entry.state) : "";
        }
        public static bool IsCurrent(string id) {
            if (string.IsNullOrEmpty(id)) return true;
            var entry = Find(id);
            return entry != null && Active(entry.settings.category)?.state == entry.state;
        }

        //Filtered subcontainers retain the parent's storage identity, so existing saves and exporters keep working.
        public static string Scope(ObjectState state) {
            var owner = Owner(state);
            if (owner == "") return owner;
            var scope = entries.Values.FirstOrDefault(e => e.parentId == owner && e.settings.filterParameter != null
                && Accepts(e, state.Str("type", "")) && state.Int(e.settings.filterParameter, 0) == e.settings.filterValue);
            return scope != null ? EnsureId(scope.state) : owner;
        }

        //whether a scope is the container or one of its subcontainers (what is in it gets hidden with the container)
        public static bool IsWithin(string scope, string id) {
            for (var cur = scope; !string.IsNullOrEmpty(cur); cur = Find(cur)?.parentId) {
                if (cur == id) return true;
            }
            return false;
        }

        public static void PruneSelections() {
            foreach (var category in ArrayObject.activeElements.Keys.ToArray()) {
                var state = ArrayObject.activeElements[category];
                var entry = Find(state?.Str(IdKey, ""));
                if (entry?.parentId != null && !IsCurrent(entry.parentId)) ArrayObject.activeElements[category] = null;
            }
        }

        public static void Sync(ElementManager current) {
            var previous = manager == current ? entries.Values.ToArray() : Array.Empty<Entry>();
            manager = current;
            entries.Clear();
            var city = current.GetDummy<CityProperties>();
            if (city == null || !TP.GetCitySettings().TryGetValue("city", out var type)) return;
            Entry root = null;
            if (type.typeData.settings.elementContainer is PropertyContainerSettings singleton) {
                entries[EnsureId(city.state)] = root = new Entry { state = city.state, settings = singleton, path = new() };
            }
            Visit(city.state, type.typeData.parametersInfo.parameters, new(), root);
            foreach (var old in previous) {
                if (Find(old.state.Str(IdKey, "")) == null) DeleteContents(old.state);
            }
            //Undo can replace the states while the selected category still holds the old state instance.
            foreach (var category in ArrayObject.activeElements.Keys.ToArray()) {
                var old = ArrayObject.activeElements[category];
                if (old?.Str(IdKey, "") is string id && id != "") ArrayObject.activeElements[category] = Find(id)?.state;
            }
            PruneSelections();
        }

        //Before an undo or a script change: the selected containers stay selected (Sync follows their states), otherwise
        //what they own would be hidden.
        public static void DeselectAllButContainers() {
            foreach (var category in ArrayObject.activeElements.Keys.ToArray()) {
                if (ArrayObject.activeElements[category]?.Str(IdKey, "") is null or "") ArrayObject.activeElements[category] = null;
            }
        }

        static void Visit(ObjectState state, Parameter[] parameters, List<(string, int?)> path, Entry parent) {
            if (state == null) return;
            foreach (var param in parameters ?? Array.Empty<Parameter>()) {
                var ctName = param.type == "array" ? param.arrayProperties?.customCityPropertiesType : param.customCityPropertiesType;
                var ct = ctName != null && TP.GetCustomCityPropertiesTypes().TryGetValue(ctName, out var value) ? value : null;
                if (param.type != "array" && param.type != "customCityProperties") continue;
                //Fixed nested containers also exist before their first object is placed.
                if (param.type == "customCityProperties" && ct?.elementContainer != null && state.State(param.fullName()) == null)
                    States.EnsureCustomCityProperties(state, param);
                var children = param.type == "array" ? state.Array<ObjectState>(param.fullName()) : new[] { state.State(param.fullName()) };
                if (children == null) continue;
                for (int i = 0; i < children.Length; i++) {
                    var child = children[i];
                    if (child == null) continue;
                    var childPath = new List<(string, int?)>(path) { (param.fullName(), param.type == "array" ? i : null) };
                    var childEntry = parent;
                    if (ct?.elementContainer != null) {
                        childEntry = new Entry { state = child, settings = ct.elementContainer, path = childPath,
                            parentId = ct.elementContainer.parentCategory != null && parent?.settings.category == ct.elementContainer.parentCategory ? EnsureId(parent.state) : null };
                        entries[EnsureId(child)] = childEntry;
                    }
                    Visit(child, ct?.parameters ?? param.arrayProperties?.elementProperties, childPath, childEntry);
                }
            }
        }

        public static List<Node3D> Contents(string id) {
            var entry = Find(id);
            if (entry?.parentId != null && entry.settings.filterParameter != null) {
                return DirectContents(entry.parentId).FindAll(node => {
                    var state = (node as CustomElement)?.state ?? (node as MeshInstance)?.settings;
                    return Scope(state) == id;
                });
            }
            return DirectContents(id);
        }

        static List<Node3D> DirectContents(string id) {
            var result = new List<Node3D>();
            if (manager == null || string.IsNullOrEmpty(id)) return result;
            result.AddRange(manager.customElements.Where(e => !e.IsDeleted() && Owner(e.state) == id));
            result.AddRange(manager.meshes.Where(m => GodotObject.IsInstanceValid(m) && !m.IsQueuedForDeletion() && Owner(m.settings) == id));
            return result;
        }

        static readonly PropertyContainerContentGroup[] allContents = { new PropertyContainerContentGroup { label = "CONTAINER_CONTENTS" } };

        //the lists of the owned elements in the panel of a container: none if it can't own any
        public static PropertyContainerContentGroup[] ContentGroups(PropertyContainerSettings settings) {
            if (!(settings?.types?.Length > 0)) return Array.Empty<PropertyContainerContentGroup>();
            return settings.contentGroups?.Length > 0 ? settings.contentGroups : allContents;
        }

        public static List<Node3D> Contents(string id, PropertyContainerContentGroup group) {
            return Contents(id).FindAll(node => {
                var type = (node as CustomElement)?.TypeName ?? (node as MeshInstance)?.TypeName;
                var state = (node as CustomElement)?.state ?? (node as MeshInstance)?.settings;
                if (group.types != null && Array.IndexOf(group.types, type) < 0) return false;
                return group.filterParameter == null || state.Int(group.filterParameter, 0) == group.filterValue;
            });
        }

        public static void DeleteContents(ObjectState state) {
            foreach (var child in NestedStates(state)) {
                foreach (var node in DirectContents(child.Str(IdKey, ""))) {
                    if (node is CustomElement element) CustomElements.Delete(manager, element);
                    else if (node is MeshInstance mesh) mesh.Delete();
                }
            }
        }

        static IEnumerable<ObjectState> NestedStates(ObjectState state) {
            if (state == null) yield break;
            yield return state;
            foreach (var value in state.properties.Values) {
                if (value is ObjectState child) foreach (var item in NestedStates(child)) yield return item;
                else if (value is ObjectState[] array) foreach (var entry in array) foreach (var item in NestedStates(entry)) yield return item;
            }
        }

        public static void CloneContents(ObjectState source, ObjectState target) {
            var ids = new Dictionary<string, string>();
            var original = NestedStates(source).ToArray();
            var copied = NestedStates(target).ToArray();
            for (int i = 0; i < original.Length; i++) {
                var id = original[i].Str(IdKey, "");
                if (id == "") continue;
                copied[i].RemoveProperty(IdKey);
                ids[id] = EnsureId(copied[i]);
            }
            if (manager == null) return;
            var points = new Dictionary<TerrainPoint, TerrainPoint>();
            var copies = new Dictionary<CustomElement, CustomElement>();
            foreach (var pair in ids) foreach (var node in DirectContents(pair.Key)) {
                if (node is MeshInstance mesh) {
                    var settings = (ObjectState)mesh.settings.Clone();
                    Assign(settings, pair.Value);
                    var copy = Meshes.Create(manager, mesh.meshPath, GO.GetPosition(mesh), settings);
                    GO.SetRotation(copy, GO.GetRotation(mesh));
                    GO.SetLocalScale(copy, GO.GetLocalScale(mesh));
                } else if (node is CustomElement element) {
                    var copy = manager.CreateCustomElement(element.TypeName);
                    copy.state = (ObjectState)element.state.Clone();
                    Assign(copy.state, pair.Value);
                    TerrainPoint CopyPoint(TerrainPoint p) {
                        if (!points.TryGetValue(p, out var result)) {
                            points[p] = result = copy.CreatePoint(p.GetPoint());
                            result.SetRotation(p.GetRotation());
                            result.SetScale(p.GetScale());
                        }
                        return result;
                    }
                    var controls = element.GetAllPoints();
                    var json = global::CustomElements.PointTree.ToJson(element.points, p => controls.IndexOf(p));
                    copy.SetPoints((global::CustomElements.PointGroup)global::CustomElements.PointTree.FromJson(json, element.points.def,
                        t => CopyPoint(controls[(int)t]), (def, pos) => copy.CreatePoint(pos), Vector3.Zero));
                    foreach (var p in element.meshVertices) copy.meshVertices.Add(CopyPoint(p));
                    copy.triangles.AddRange(element.triangles);
                    copy.uvs.AddRange(element.uvs);
                    GO.SetName(copy, GO.GetName(element));
                    copies[element] = copy;
                }
            }
            foreach (var pair in copies) {
                if (pair.Key.container != null && copies.TryGetValue(pair.Key.container, out var parent)) pair.Value.SetContainer(parent);
                foreach (var key in pair.Key.GetCustomElementType().GetReferenceParameters()) {
                    var reference = pair.Value.state.State(key);
                    if (reference?.Str("kind") == "customElement" && History.ElementRegistry.Get(reference.Int("id")) is CustomElement old && copies.TryGetValue(old, out var mapped)) {
                        reference.SetInt("id", History.ElementRegistry.GetId(mapped));
                    }
                }
            }
        }

        public static void SyncTabSelections() {
            if (manager == null) return;
            foreach (var entry in entries.Values.Where(e => e.path.Count == 0 && e.settings.tab != null)) {
                ArrayObject.activeElements[entry.settings.category] = Active(entry.settings.category)?.state;
            }
        }

        //the city properties panel went to another tab (closing the panel leaves the singleton selected)
        public static void LeaveTab(string tab) {
            foreach (var entry in entries.Values.Where(e => e.path.Count == 0 && e.settings.tab == tab)) {
                ArrayObject.activeElements[entry.settings.category] = null;
            }
        }

        public static void ApplyVisibility() {
            if (manager == null) return;
            SyncTabSelections();
            manager.builder?.CancelInvalidContainerPlacement();
            foreach (var element in manager.customElements) {
                var id = Owner(element.state);
                if (id == "") continue;
                var visible = IsCurrent(Scope(element.state));
                if (!visible && element.Visible) { element.SetActive(false, false); element.Select(false); }
                GO.SetEnabled(element, visible);
            }
            foreach (var mesh in manager.meshes) {
                var id = Owner(mesh.settings);
                if (id != "") GO.SetEnabled(mesh, IsCurrent(Scope(mesh.settings)));
            }
        }

        public static EditorPanel Open(CityBuilderMenuBar builder, Entry entry) {
            entry = Find(entry.state.Str(IdKey, ""));
            if (entry == null) return null;
            builder.SelectObject(manager.GetDummy<CityProperties>());
            EditorPanel panel = builder.cityPropertiesEditorPanel;
            if (entry.settings.tab != null) builder.cityPropertiesEditorPanel.ShowTab(entry.settings.tab);
            foreach (var (parameter, index) in entry.path) {
                panel = panel.OpenArrayElement(parameter, index);
                if (panel == null) return null;
            }
            return panel;
        }
    }
}

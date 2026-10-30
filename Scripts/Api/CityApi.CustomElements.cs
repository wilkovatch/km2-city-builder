using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;
using Parameter = CityElements.Types.Parameter;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    //The custom elements of the core (see the Core Custom Elements page of the wiki), the conversion of elements to
    //them, and the ghosts of the procedural elements they replace
    public partial class CityApi {
        [ApiMethod("customElement.import", "Imports a complete custom element points/elements payload with local ids as one undoable operation")]
        public object ImportCustomElement(JObject data) {
            RequireCity();
            var error = Operations.Conversion.ValidateImport(data);
            if (error != null) throw ApiException.InvalidValue(error);
            return ApplyChange("import custom elements", () => {
                var result = Operations.Conversion.Import(M, data);
                if (!result.success) throw ApiException.InvalidValue(result.message);
                return new { points = result.pointIds, elements = result.elementIds };
            });
        }

        // Points

        //the point of a custom element from a point target (a new one, or an existing terrain point to share)
        TerrainPoint CustomElementPoint(PointTarget t) {
            var (obj, pos) = ResolvePoint(t);
            return Operations.CustomElements.PointFromClick(M, obj, pos);
        }

        //the points of a new element from {"name": point target, "array": [point targets or groups], ...}; the missing ones
        //get their default position, from the position of the element
        CustomElements.PointGroup CustomElementPoints(CustomElement element, JToken points, Vector3 origin) {
            var created = new List<TerrainPoint>();
            var res = History.ElementSnapshot.CustomElementPoints(points, element.state, element, token => {
                var target = token.ToObject<PointTarget>(Newtonsoft.Json.JsonSerializer.Create(Json.Settings));
                var p = CustomElementPoint(target);
                created.Add(p);
                return p;
            }, origin);
            return res;
        }

        CustomElement ResolveCustomElement(ElementRef r) {
            return Resolve<CustomElement>(r, null);
        }

        static object CustomElementPointInfo(CustomElements.PointLeaf leaf) {
            var p = leaf.point;
            return new {
                id = History.ElementRegistry.GetId(p),
                position = p.GetPoint(),
                rotation = leaf.def.rotatable ? p.GetRotation() : (Vector3?)null,
                shared = p.GetLinks().Count > 1,
                anchored = !GO.IsNull(p.anchor) && !p.anchor.IsDeleted()
            };
        }

        static JToken CustomElementPointsInfo(CustomElements.PointNode node) {
            switch (node) {
                case CustomElements.PointLeaf leaf:
                    return Json.ToToken(CustomElementPointInfo(leaf));
                case CustomElements.PointGroup group: {
                    var obj = new JObject();
                    foreach (var (name, child) in group.children) obj[name] = CustomElementPointsInfo(child);
                    return obj;
                }
                case CustomElements.PointList list: {
                    var arr = new JArray();
                    foreach (var item in list.items) arr.Add(CustomElementPointsInfo(item));
                    return arr;
                }
            }
            return JValue.CreateNull();
        }

        object CustomElementDerived(CustomElement element) {
            var vertices = new List<object>();
            foreach (var v in element.meshVertices) vertices.Add(new { id = History.ElementRegistry.GetId(v), position = v.GetPoint() });
            var contents = new List<int>();
            foreach (var child in M.GetCustomElementChildren(element)) contents.Add(IdOf(child));
            return new {
                type = element.TypeName,
                points = CustomElementPointsInfo(element.points),
                paths = element.GetLeaves().ConvertAll(l => l.path),
                vertices,
                triangles = element.triangles,
                uvs = element.uvs,
                scriptUVs = element.scriptUVs,
                container = element.container != null ? IdOf(element.container) : (int?)null,
                contents,
                source = element.source != null && History.ElementSnapshot.IsAlive(element.source) ? IdOf(element.source) : (int?)null,
                spline = element.splineData,
                problems = CustomElements.PointTree.Validate(element.points)
            };
        }

        // Elements

        [ApiMethod("customElement.create", "Creates a custom element of a type of the core: its control points with the default layout around position, or as given in points (the structure of the control points of the type, each point a point target: position, terrainPoint to share an existing point, anchor, anchorLink...), its mesh (vertices: point targets, triangles: indices, uvs: [u, v] per vertex), its parameters (state, with propRule for the types with props containers, see customElement.setPropRule) and its container. Returns its id")]
        public int CreateCustomElement(string type, Vector3? position = null, JToken points = null, JObject mesh = null, JObject state = null, ElementRef container = null, string name = null) {
            return ApplyChange("create custom element", () => {
                if (type == null || !TP.GetCustomElementTypes().ContainsKey(type)) throw ApiException.InvalidValue("unknown custom element type '" + type + "'");
                CustomElement containerElement = container != null ? ResolveCustomElement(container) : null;
                var origin = position ?? Vector3.Zero;
                var element = M.CreateCustomElement(type, name);
                try {
                    if (state != null) {
                        foreach (var p in state.Properties()) {
                            if (p.Name == "type") continue;
                            SetCustomElementParam(element, p.Name, p.Value);
                        }
                    }
                    if (points == null) {
                        element.CreateDefault(origin);
                    } else {
                        element.SetPoints(CustomElementPoints(element, points, origin));
                    }
                    if (mesh != null) SetCustomElementMesh(element, mesh);
                    var problems = CustomElements.PointTree.Validate(element.points);
                    if (problems != null) throw ApiException.InvalidValue(problems);
                    if (containerElement != null && !element.SetContainer(containerElement)) {
                        throw ApiException.InvalidOperation("a " + type + " can't be put in a " + containerElement.TypeName);
                    }
                } catch {
                    element.Delete();
                    throw;
                }
                return IdOf(element);
            });
        }

        void SetCustomElementParam(CustomElement element, string name, JToken value) {
            var type = element.GetCustomElementType();
            if (name == "curveType" && element.Settings?.spline != null &&
                System.Array.IndexOf(element.Settings.spline.curveTypes, value.Value<int>()) < 0) throw ApiException.InvalidValue("curve type is not allowed by this spline type");
            if (name == "propRule") {
                SetCustomElementPropRule(element, value);
                return;
            }
            Parameter p = null;
            foreach (var param in type?.parameterContainer.parameters ?? new Parameter[0]) {
                if (param.fullName() == name) p = param;
            }
            if (p == null) throw ApiException.NotFound("parameter '" + name + "' of the custom element type " + element.TypeName);
            element.state.SetProperty(p.fullName(), ConvertValue(p.type, value, p.enumLabels, name));
        }

        void SetCustomElementMesh(CustomElement element, JObject mesh) {
            if (element.Settings?.spline != null) throw ApiException.InvalidOperation("a spline element cannot have an editable mesh");
            var vertices = new List<TerrainPoint>();
            if (mesh["vertices"] is JArray vs) {
                foreach (var v in vs) vertices.Add(CustomElementPoint(v.ToObject<PointTarget>(Newtonsoft.Json.JsonSerializer.Create(Json.Settings))));
            }
            var triangles = mesh["triangles"] is JArray ts ? ts.ToObject<List<int>>() : new List<int>();
            if (triangles.Count % 3 != 0) throw ApiException.InvalidValue("the number of triangle indices is not a multiple of 3");
            foreach (var i in triangles) {
                if (i < 0 || i >= vertices.Count) throw ApiException.InvalidValue("the triangle index " + i + " is out of the vertices");
            }
            List<Vector2> uvs = null;
            if (mesh["uvs"] is JArray us) {
                uvs = new List<Vector2>();
                foreach (var uv in us) uvs.Add(new Vector2((float)uv[0], (float)uv[1]));
            }
            element.SetMesh(vertices, triangles, uvs);
            foreach (var v in vertices) {
                if (v.GetLinks().Count == 0) v.Delete();
            }
        }

        //the prop rule of the element: {container: {type, elements or element...}} with the props containers of its type
        void SetCustomElementPropRule(CustomElement element, JToken value) {
            var containers = element.Settings?.propsContainers ?? new string[0];
            if (containers.Length == 0) throw ApiException.InvalidOperation("the type " + element.TypeName + " has no props containers");
            if (value == null || value.Type == JTokenType.Null) {
                element.state.RemoveProperty("propRule");
                return;
            }
            if (value is not JObject rule) throw ApiException.InvalidValue("the prop rule must be an object");
            foreach (var p in rule.Properties()) {
                if (p.Value is JObject && System.Array.IndexOf(containers, p.Name) < 0) {
                    throw ApiException.NotFound("props container '" + p.Name + "' (" + string.Join(", ", containers) + ")");
                }
            }
            var state = rule.ToObject<ObjectState>();
            foreach (var name in containers) {
                var container = state.State(name);
                if (container != null) Operations.Props.EnsureContainerType(container);
            }
            Operations.Props.SetPropRule(element.state, state);
            element.state.FlagAsChanged();
        }

        [ApiMethod("customElement.setPropRule", "Sets the prop rule of a custom element of a type with props containers (rule: {container: props container state, as in the prop rules of the roads}, null to remove it); the props are placed along the prop lines of its geometry")]
        public void SetCustomElementPropRuleApi(ElementRef element, JObject rule = null) {
            ApplyChange("set prop rule", () => {
                var e = ResolveCustomElement(element);
                History.UndoManager.Touch(e);
                SetCustomElementPropRule(e, rule);
            });
        }

        [ApiMethod("customElement.setMesh", "Replaces the mesh of a custom element: vertices (point targets), triangles (indices), uvs ([u, v] per vertex)")]
        public void SetMesh(ElementRef element, JObject mesh) {
            ApplyChange("set mesh", () => {
                var e = ResolveCustomElement(element);
                if (!(e.GetCustomElementType()?.HasMesh ?? false)) throw ApiException.InvalidOperation("the type " + e.TypeName + " has no mesh");
                SetCustomElementMesh(e, mesh);
            });
        }

        [ApiMethod("customElement.setUV", "Sets the texture coordinates of a vertex of the mesh (types with editable uvs)")]
        public void SetUV(ElementRef element, int vertex, Vector2 uv) {
            ApplyChange("set uv", () => {
                var e = ResolveCustomElement(element);
                if (e.Settings?.mesh?.uvs != "editable") throw ApiException.InvalidOperation("the texture coordinates of " + e.TypeName + " are not editable");
                if (vertex < 0 || vertex >= e.meshVertices.Count) throw ApiException.NotFound("vertex " + vertex);
                e.SetUV(vertex, uv);
            });
        }

        [ApiMethod("customElement.insert", "Adds an element to an array of the control points (path: e.g. \"sections\" or \"sections.2.lanes\") at the index (the end by default), as with the Insert buttons: its points between the ones around it (or continuing the array); in a uniform array, every instance gets one. With position (only at the start or end of the array), the element is centered there and turned as the direction of the array changes, as with the Add at the start/end click modes. Returns the paths of the new points")]
        public object InsertCustomElement(ElementRef element, string array, int? index = null, Vector3? position = null) {
            return ApplyChange<object>("insert", () => {
                var e = ResolveCustomElement(element);
                var list = CustomElements.PointTree.Find(e.points, array) as CustomElements.PointList;
                if (list == null) throw ApiException.NotFound("array '" + array + "'");
                var i = index ?? list.items.Count;
                if (i < 0 || i > list.items.Count) throw ApiException.InvalidValue("index out of range (0-" + list.items.Count + ")");
                if (position.HasValue) {
                    if (i != 0 && i != list.items.Count) throw ApiException.InvalidParam("position is only allowed at the start or end of the array");
                    if (!Operations.CustomElements.AppendArrayElementAt(e, array, i == 0 && list.items.Count > 0, position.Value, false)) {
                        throw ApiException.InvalidOperation("the array '" + array + "' can't have more elements");
                    }
                } else if (!Operations.CustomElements.InsertArrayElement(e, array, i, false)) {
                    throw ApiException.InvalidOperation("the array '" + array + "' can't have more elements");
                }
                var res = new List<string>();
                var prefix = array + "." + i;
                foreach (var (path, _) in e.GetLeaves()) {
                    if (path == prefix || path.StartsWith(prefix + ".")) res.Add(path);
                }
                return res;
            });
        }

        [ApiMethod("customElement.remove", "Removes the element of an array of the control points at the index (in every instance of a uniform array)")]
        public void RemoveCustomElement(ElementRef element, string array, int index) {
            ApplyChange("remove", () => {
                var e = ResolveCustomElement(element);
                if (!(CustomElements.PointTree.Find(e.points, array) is CustomElements.PointList list)) throw ApiException.NotFound("array '" + array + "'");
                if (index < 0 || index >= list.items.Count) throw ApiException.NotFound("element " + index + " of '" + array + "'");
                if (!Operations.CustomElements.RemoveArrayElement(e, array, index, false)) throw ApiException.InvalidOperation("the array '" + array + "' has its minimum number of elements");
            });
        }

        [ApiMethod("customElement.removePoint", "Removes a control point (path) or a vertex of the mesh (vertex, with its triangles) of a custom element, as with the Delete key: a control point takes with it the element of the outermost editable array containing it (e.g. the section of a lane point; customElement.remove removes from a given array). The point is deleted if no other element uses it")]
        public void RemoveCustomElementPoint(ElementRef element, string path = null, int? vertex = null) {
            ApplyChange("remove point", () => {
                var e = ResolveCustomElement(element);
                var p = CustomElementPointAt(e, path, vertex);
                if (!Operations.CustomElements.RemovePoint(e, p)) throw ApiException.InvalidOperation("the point can't be removed (it is not in an editable array, or the array has its minimum number of elements)");
            });
        }

        TerrainPoint CustomElementPointAt(CustomElement e, string path, int? vertex) {
            if (vertex.HasValue) {
                if (vertex.Value < 0 || vertex.Value >= e.meshVertices.Count) throw ApiException.NotFound("vertex " + vertex.Value);
                return e.meshVertices[vertex.Value];
            }
            if (!(CustomElements.PointTree.Find(e.points, path) is CustomElements.PointLeaf leaf)) throw ApiException.NotFound("point '" + path + "'");
            return leaf.point;
        }

        [ApiMethod("customElement.movePoint", "Moves a control point (path) or a vertex of the mesh (vertex) of a custom element, as with the gizmo (the constraints of the type run at the next update; a point on an anchor leaves it); rotation: the euler angles of a rotatable point")]
        public void MoveCustomElementPoint(ElementRef element, Vector3 position, string path = null, int? vertex = null, Vector3? rotation = null) {
            ApplyChange("move point", () => {
                var e = ResolveCustomElement(element);
                var p = CustomElementPointAt(e, path, vertex);
                Operations.TerrainPoints.Move(p, position, true);
                if (rotation.HasValue) {
                    var leaf = path != null ? CustomElements.PointTree.Find(e.points, path) as CustomElements.PointLeaf : null;
                    if (leaf == null || !leaf.def.rotatable) throw ApiException.InvalidOperation("the point can't be rotated");
                    p.SetRotation(rotation.Value);
                }
            });
        }

        [ApiMethod("customElement.move", "Moves a custom element with its contents (and theirs) by an offset, or by the offset from the center of their points to a position, as with the move handle of a container: the points shared with other elements move too, the points on the anchors of roads and intersections stay there (the ones on the anchors of a ghost leave them and move), the points on a line follow its ends. Returns the number of points moved")]
        public int MoveCustomElement(ElementRef element, Vector3? offset = null, Vector3? position = null) {
            if (offset.HasValue == position.HasValue) throw ApiException.InvalidParam("give either offset or position");
            return ApplyChange("move", () => {
                var e = ResolveCustomElement(element);
                var center = Operations.CustomElements.CenterWithContents(M, e);
                if (!center.HasValue) throw ApiException.InvalidOperation("the element has no points");
                return Operations.CustomElements.Move(M, e, offset ?? position.Value - center.Value);
            });
        }

        [ApiMethod("customElement.attachPoint", "Replaces a control point (path) or a vertex (vertex) of a custom element with an existing terrain point (to: {terrainPoint}, shared from now on), or attaches it to an anchor or segment (to: {anchor} or {anchorLink}), to the line between two terrain points (line: [id, id], it stays where it is closest to it) or to the vertical line of a terrain point (column: id, it keeps its height and follows the x and z of that point; moved horizontally, that point follows)")]
        public int AttachCustomElementPoint(ElementRef element, PointTarget to = null, string path = null, int? vertex = null, int[] line = null, int? column = null) {
            return ApplyChange("attach point", () => {
                var e = ResolveCustomElement(element);
                var p = CustomElementPointAt(e, path, vertex);
                TerrainPoint TerrainPointOf(int id) => History.UndoManager.GetAliveElement(id) as TerrainPoint ?? throw ApiException.NotFound("terrain point " + id);
                if (line != null) {
                    if (line.Length != 2) throw ApiException.InvalidParam("line must be two terrain points");
                    if (!Operations.CustomElements.AttachToLine(p, TerrainPointOf(line[0]), TerrainPointOf(line[1]))) {
                        throw ApiException.InvalidOperation("the point can't follow that line (one of its ends is the point or follows it)");
                    }
                    return History.ElementRegistry.GetId(p);
                }
                if (column.HasValue) {
                    if (!Operations.CustomElements.AttachToColumn(p, TerrainPointOf(column.Value))) {
                        throw ApiException.InvalidOperation("the point can't follow that point (it is the point or follows it)");
                    }
                    return History.ElementRegistry.GetId(p);
                }
                if (to == null || (!to.terrainPoint.HasValue && to.anchor == null && to.anchorLink == null && to.link == null)) {
                    throw ApiException.InvalidParam("to must be a terrain point, an anchor or a segment (or give line or column)");
                }
                var (obj, pos) = ResolvePoint(to);
                var res = Operations.CustomElements.AttachPoint(M, e, p, obj, pos);
                if (res == null) throw ApiException.InvalidOperation("nothing to attach to");
                return History.ElementRegistry.GetId(res);
            });
        }

        [ApiMethod("customElement.detachPoint", "A control point (path) or vertex (vertex) of a custom element stops being shared with the other elements (it gets a copy of the point), or, if only the element uses it, stops following its anchor. Returns the id of the point")]
        public int DetachCustomElementPoint(ElementRef element, string path = null, int? vertex = null) {
            return ApplyChange("detach point", () => {
                var e = ResolveCustomElement(element);
                var p = CustomElementPointAt(e, path, vertex);
                return History.ElementRegistry.GetId(Operations.CustomElements.DetachPoint(M, e, p));
            });
        }

        [ApiMethod("customElement.setContainer", "Puts a custom element in a container (another custom element whose type can contain it), or out of any container (container: null)")]
        public void SetCustomElementContainer(ElementRef element, ElementRef container = null) {
            ApplyChange("set container", () => {
                var e = ResolveCustomElement(element);
                var c = container != null ? ResolveCustomElement(container) : null;
                if (!e.SetContainer(c)) throw ApiException.InvalidOperation("a " + e.TypeName + " can't be put in " + (c == null ? "nothing" : "a " + c.TypeName));
            });
        }

        [ApiMethod("customElement.merge", "Moves the contents of a container into another one (of a type that can contain them all) and deletes it")]
        public void MergeCustomElement(ElementRef from, ElementRef into) {
            ApplyChange("merge", () => {
                if (!Operations.CustomElements.Merge(M, ResolveCustomElement(from), ResolveCustomElement(into))) {
                    throw ApiException.InvalidOperation("the contents can't all be put in the other container");
                }
            });
        }

        [ApiMethod("customElement.delete", "Deletes a custom element (the elements it contains are left without a container); the ghost it replaces is deleted with the last element replacing it")]
        public void DeleteCustomElement(ElementRef element) {
            ApplyChange("delete", () => Operations.CustomElements.Delete(M, ResolveCustomElement(element)));
        }

        // Conversion and ghosts

        [ApiMethod("elements.convert", "Converts elements to custom elements with a converter of the core (converters.list): the converter replaces them (roads and intersections become ghosts: hidden, not exported, kept for the elements connected to them; the other elements are deleted; some converters change existing custom elements instead). Returns the ids of the custom elements created, then the ones changed")]
        public Task<object> Convert(List<ElementRef> elements, string converter) {
            RequireCity();
            if (elements == null || elements.Count == 0) throw ApiException.InvalidParam("no elements to convert");
            var resolved = new List<object>();
            foreach (var r in elements) resolved.Add(Resolve(r));
            return RunLong<object>(async () => {
                builder.BeginExternalChange();
                Operations.Conversion.Result res;
                if (!recordHistory) History.UndoManager.Suppress(true);
                try {
                    res = await Operations.Conversion.Run(M, converter, resolved);
                } finally {
                    if (!recordHistory) History.UndoManager.Suppress(false);
                }
                builder.EndExternalChange();
                if (!res.success) throw new ApiException(res.error, res.message);
                Flush();
                var ids = res.created.ConvertAll(IdOf);
                foreach (var e in res.updated) ids.Add(IdOf(e));
                return ids;
            });
        }

        [ApiMethod("converters.list", "The converters of the core: name, label, the kinds and types they convert")]
        public object ListConverters() {
            RequireCity();
            var res = new List<object>();
            foreach (var c in ConverterCatalog.List()) {
                res.Add(new { name = c.name, label = c.label != null ? StringManager.GetOrKey(c.label) : c.name, sources = c.sources, types = c.types, replace = c.replace });
            }
            return res;
        }

        [ApiMethod("ghosts.list", "The ghosts: the roads and intersections replaced by custom elements, with the elements replacing them")]
        public object ListGhosts() {
            RequireCity();
            var res = new List<object>();
            foreach (var e in AllElements(M)) {
                if (!Operations.CustomElements.IsGhost(e) || !History.ElementSnapshot.IsAlive(e)) continue;
                res.Add(new { id = IdOf(e), kind = KindOf(e), name = GetName(e), replacedBy = Operations.CustomElements.GetReplacements(M, e).ConvertAll(IdOf) });
            }
            return res;
        }

        [ApiMethod("ghosts.restore", "Brings back a ghost: the custom elements replacing it are deleted")]
        public void RestoreGhost(ElementRef element) {
            ApplyChange("restore", () => {
                var e = Resolve(element);
                if (!Operations.CustomElements.IsGhost(e)) throw ApiException.InvalidOperation(KindOf(e) + " " + IdOf(e) + " is not a ghost");
                Operations.CustomElements.RestoreGhost(M, e);
            });
        }

        [ApiMethod("ghosts.set", "Makes a road or intersection a ghost (hidden, not exported, kept for the elements connected to it), or a normal element again (ghost: false)")]
        public void SetGhost(ElementRef element, bool ghost = true) {
            ApplyChange("ghost", () => {
                var e = Resolve(element);
                if (!(e is Road || e is Intersection)) throw ApiException.WrongKind("road or intersection", KindOf(e));
                Operations.CustomElements.SetGhost(e, ghost);
            });
        }
    }
}

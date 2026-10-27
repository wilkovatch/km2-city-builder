using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using System.Linq;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;

namespace Operations {
    //Conversion of elements to low-level elements by a converter of the core (see ConverterCatalog): the converter gets
    //the elements in the format of the export JSON (with the elements around them) and returns the low-level elements in
    //the format of the city save, which replace them (see the Core Low-Level Elements page of the wiki)
    public static class Conversion {
        public class Result {
            public bool success;
            public string error; //unknown_converter, not_accepted, converter_failed, invalid_output
            public string message;
            public List<LowLevelElement> created = new List<LowLevelElement>();
            public List<LowLevelElement> updated = new List<LowLevelElement>(); //the existing elements changed (output "updates")

            public static Result Fail(string error, string message) {
                return new Result { success = false, error = error, message = message };
            }
        }

        public static string ElementKind(object element) {
            return History.ElementSnapshot.ReferenceKind(element);
        }

        static int Id(object element) {
            return History.ElementRegistry.GetId(element);
        }

        // Input

        //the elements to convert and the ones the converter may need: the intersections of the roads and the roads of the
        //intersections (one level more), the containers and contents of the low-level elements, and the low-level
        //elements replacing any of them
        static List<object> GetContext(ElementManager manager, List<object> elements) {
            var res = new List<object>();
            void Add(object e) {
                if (e != null && History.ElementSnapshot.IsAlive(e) && !res.Contains(e)) res.Add(e);
            }
            foreach (var e in elements) Add(e);
            for (int level = 0; level < 2; level++) {
                foreach (var e in new List<object>(res)) {
                    switch (e) {
                        case Road r:
                            Add(r.startIntersection);
                            Add(r.endIntersection);
                            break;
                        case Intersection i:
                            foreach (var r in i.roads) Add(r);
                            break;
                        case LowLevelElement l:
                            Add(l.container);
                            Add(l.source);
                            foreach (var child in manager.GetLowLevelChildren(l)) Add(child);
                            break;
                    }
                }
            }
            foreach (var e in new List<object>(res)) {
                foreach (var replacement in LowLevel.GetReplacements(manager, e)) Add(replacement);
            }
            return res;
        }

        //the input of the converter: the export JSON of the elements (ids: the ids of the scripting API) and "convert", the
        //references of the elements to convert
        public static JObject BuildInput(ElementManager manager, ConverterCatalog.ConverterInfo converter, List<object> elements) {
            var res = new JObject();
            res["converter"] = converter.name;
            var convert = new JArray();
            foreach (var e in elements) convert.Add(new JObject { ["kind"] = ElementKind(e), ["id"] = Id(e) });
            res["convert"] = convert;
            var roads = new JArray();
            var intersections = new JArray();
            var patches = new JArray();
            var lines = new JArray();
            var lowLevel = new JArray();
            using (var geometry = new CityEncoder.ExportGeometry(Id)) {
                foreach (var e in GetContext(manager, elements)) {
                    switch (e) {
                        case Road r: roads.Add(JToken.FromObject(geometry.GetRoad(r, Id(r)))); break;
                        case Intersection i: intersections.Add(JToken.FromObject(geometry.GetIntersection(i, Id(i)))); break;
                        case TerrainPatch p: patches.Add(JToken.FromObject(geometry.GetTerrainPatch(p, Id(p)))); break;
                        case BuildingLine l: lines.Add(JToken.FromObject(geometry.GetBuildingLine(l, Id(l)))); break;
                        case LowLevelElement l: lowLevel.Add(JToken.FromObject(geometry.GetLowLevelElement(l, Id(l)))); break;
                    }
                }
                res["materialDict"] = JToken.FromObject(geometry.materials);
                res["meshDict"] = JToken.FromObject(geometry.meshes);
            }
            res["roads"] = roads;
            res["intersections"] = intersections;
            res["terrainPatches"] = patches;
            res["buildingLines"] = lines;
            res["lowLevelElements"] = lowLevel;
            res["meshInstances"] = new JArray();
            res["cityProperties"] = JToken.FromObject(manager.GetDummy<CityProperties>().GetState());
            var ancestries = new JArray();
            foreach (var (category, name, ancestry) in CityElements.Types.Parsers.TypeParser.GetAllAncestries()) {
                ancestries.Add(new JObject { ["category"] = category, ["name"] = name, ["ancestry"] = new JArray(ancestry) });
            }
            res["typeAncestry"] = ancestries;
            res["cityFolder"] = PreferencesManager.workingDirectory;
            return res;
        }

        // Run

        //converts the elements: the converter runs (without blocking the editor), then its output replaces them in a single
        //undo step (the elements changed meanwhile are checked again)
        public static async Task<Result> Run(ElementManager manager, string converterName, List<object> elements) {
            var converter = ConverterCatalog.Get(converterName);
            if (converter == null) return Result.Fail("unknown_converter", "unknown converter " + converterName);
            if (elements.Count == 0) return Result.Fail("not_accepted", "no elements to convert");
            foreach (var e in elements) {
                if (!converter.Accepts(e)) return Result.Fail("not_accepted", "the converter " + converterName + " can't convert " + ElementKind(e) + " " + Id(e));
            }
            var tempDir = Path.Combine(Path.GetTempPath(), "km2cb_convert_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try {
                var inputPath = Path.Combine(tempDir, "input.json");
                var outputPath = Path.Combine(tempDir, "output.json");
                File.WriteAllText(inputPath, BuildInput(manager, converter, elements).ToString(Newtonsoft.Json.Formatting.None));
                var pythonFolder = Path.Combine(PathHelper.FilesPath(true), "python");
                var (code, data) = await PythonManager.RunScriptTask(converter.ScriptPath, true, new List<string> { pythonFolder, inputPath, outputPath });
                if (code != 0) return Result.Fail("converter_failed", StringManager.Get(data ?? ""));
                if (!File.Exists(outputPath)) return Result.Fail("converter_failed", "the converter wrote no output");
                JObject output;
                try {
                    output = JObject.Parse(File.ReadAllText(outputPath));
                } catch (System.Exception e) {
                    return Result.Fail("invalid_output", "the output of the converter is not valid JSON: " + e.Message);
                }
                foreach (var e in elements) {
                    if (!History.ElementSnapshot.IsAlive(e)) return Result.Fail("not_accepted", "an element was deleted during the conversion");
                }
                Result res = null;
                History.UndoManager.Record(StringManager.Get("CONVERT"), () => res = Apply(manager, converter, elements, output));
                if (res.success) manager.FlagAsChanged();
                return res;
            } finally {
                try {
                    Directory.Delete(tempDir, true);
                } catch (System.Exception) { }
            }
        }

        // Output

        static Vector3 ReadVector(JToken token, string what) {
            if (token is JArray a && a.Count >= 3) return new Vector3((float)a[0], (float)a[1], (float)a[2]);
            throw new System.Exception(what + " must be [x, y, z]");
        }

        static string Key(JToken token) {
            return token == null || token.Type == JTokenType.Null ? null : token.ToString();
        }

        //checks the output before anything is changed; returns the error, null if valid
        static string Validate(JObject output, Dictionary<string, CityElements.Types.Runtime.LowLevelType> types) {
            var pointIds = new HashSet<string>();
            if (output["points"] is JArray points) {
                foreach (var p in points) {
                    var id = Key(p["id"]);
                    if (id == null) return "a point has no id";
                    if (!pointIds.Add(id)) return "the point " + id + " is defined twice";
                    ReadVector(p["position"], "the position of the point " + id);
                }
                //the anchors between the points: {"line": [id, id]} or {"column": id}
                foreach (var p in points) {
                    if (p["anchor"] is not JObject anchor) continue;
                    var ends = anchor["line"] is JArray line ? line.Select(Key).ToList() : anchor["column"] != null ? new List<string> { Key(anchor["column"]) } : null;
                    if (ends == null || (anchor["line"] != null && ends.Count != 2)) return "the anchor of the point " + Key(p["id"]) + " is not a line or a column";
                    foreach (var end in ends) {
                        if (end == null || !pointIds.Contains(end)) return "the anchor of the point " + Key(p["id"]) + " uses the undefined point " + end;
                    }
                }
            }
            var elementIds = new HashSet<string>();
            var elements = output["elements"] as JArray;
            if (elements == null && output["updates"] is not JArray) return "the output has no elements";
            //a point used by an element or an update: a point of the output (its id), or an existing terrain point (the id of
            //the scripting API) in the points of an update
            string CheckPoint(JValue v, bool existing) {
                if (v.Type == JTokenType.Null) return null;
                if (existing && v.Type == JTokenType.Integer) return History.ElementRegistry.Get((int)v) is TerrainPoint ? null : v.ToString();
                return pointIds.Contains(v.ToString()) ? null : v.ToString();
            }
            string MissingPoint(JToken t, bool existing) {
                if (t == null) return null;
                if (t is JValue v) return CheckPoint(v, existing);
                foreach (var child in t.Children()) {
                    var res = MissingPoint(child is JProperty prop ? prop.Value : child, existing);
                    if (res != null) return res;
                }
                return null;
            }
            if (output["updates"] is JArray updates) {
                foreach (var u in updates) {
                    if (u["element"] == null) return "an update has no element";
                    var missing = MissingPoint(u["points"], true);
                    if (missing != null) return "an update uses the undefined point " + missing;
                }
            }
            foreach (var e in elements ?? new JArray()) {
                var id = Key(e["id"]) ?? "?";
                var type = Key(e["type"]);
                if (type == null || !types.ContainsKey(type)) return "the element " + id + " has an unknown type " + type;
                if (!elementIds.Add(id)) return "the element " + id + " is defined twice";
                var missing = MissingPoint(e["points"], false) ?? MissingPoint(e["mesh"]?["vertices"], false);
                if (missing != null) return "the element " + id + " uses the undefined point " + missing;
            }
            return null;
        }

        public static Result Apply(ElementManager manager, ConverterCatalog.ConverterInfo converter, List<object> elements, JObject output) {
            var types = CityElements.Types.Parsers.TypeParser.GetLowLevelTypes();
            string error;
            try {
                error = Validate(output, types);
            } catch (System.Exception e) {
                error = e.Message;
            }
            if (error != null) return Result.Fail("invalid_output", error);
            var res = new Result { success = true };

            //points (welded to the existing points and anchors at the same place, unless "weld" is false)
            var points = new Dictionary<string, TerrainPoint>();
            var createdPoints = new HashSet<TerrainPoint>();
            if (output["points"] is JArray pointArray) {
                foreach (var p in pointArray) {
                    var pos = ReadVector(p["position"], "position");
                    var weld = p["weld"] == null || (bool)p["weld"];
                    TerrainPoint point = null;
                    if (weld) point = LowLevel.FindPointAt(manager, pos, createdPoints);
                    if (point == null) {
                        //a point following other points of the output is not put on an anchor of a road or intersection
                        var anchor = weld && p["anchor"] == null ? LowLevel.FindAnchorAt(manager, pos) : null;
                        point = TerrainPoint.Create(anchor != null ? anchor.GetPoint() : pos, null, manager.GetTerrainPointContainer(), anchor);
                        createdPoints.Add(point);
                        if (p["rotation"] != null) point.SetRotation(ReadVector(p["rotation"], "rotation"));
                    }
                    points[Key(p["id"])] = point;
                }
                //the anchors between the points created (the existing points welded to keep theirs)
                foreach (var p in pointArray) {
                    if (p["anchor"] is not JObject anchor) continue;
                    var point = points[Key(p["id"])];
                    if (!createdPoints.Contains(point)) continue;
                    var ok = anchor["line"] is JArray line
                        ? LowLevel.AttachToLine(point, points[Key(line[0])], points[Key(line[1])])
                        : LowLevel.AttachToColumn(point, points[Key(anchor["column"])]);
                    if (!ok) GameEngine.LogWarning("Conversion: the anchor of the point " + Key(p["id"]) + " would make it follow itself, skipped");
                }
            }

            //elements
            var created = new Dictionary<string, LowLevelElement>();
            var outputElements = output["elements"] as JArray ?? new JArray();
            //by default the elements replace the converted element (a low-level element: they take its place, its container and
            //the procedural element it replaces)
            object defaultSource = elements.Count == 1 ? elements[0] : null;
            LowLevelElement defaultContainer = null;
            if (defaultSource is LowLevelElement lowLevelSource) {
                defaultContainer = lowLevelSource.container;
                defaultSource = lowLevelSource.source;
            }
            foreach (var e in outputElements) {
                var type = Key(e["type"]);
                var element = manager.CreateLowLevelElement(type, Key(e["name"]));
                var state = e["state"] is JObject stateObj ? stateObj.ToObject<ObjectState>() : new ObjectState();
                state.SetStr("type", type);
                element.state = state;
                var origin = Vector3.Zero;
                foreach (var p in points.Values) {
                    origin = p.GetPoint();
                    break;
                }
                element.SetPoints(History.ElementSnapshot.LowLevelPoints(e["points"], state, element, t => points.TryGetValue(t.ToString(), out var tp) ? tp : null, origin));
                if (e["mesh"] is JObject mesh) {
                    var vertices = new List<TerrainPoint>();
                    if (mesh["vertices"] is JArray vs) foreach (var v in vs) vertices.Add(points[v.ToString()]);
                    var triangles = mesh["triangles"] is JArray ts ? ts.ToObject<List<int>>() : new List<int>();
                    List<Vector2> uvs = null;
                    if (mesh["uvs"] is JArray us) {
                        uvs = new List<Vector2>();
                        foreach (var uv in us) uvs.Add(new Vector2((float)uv[0], (float)uv[1]));
                    }
                    element.SetMesh(vertices, triangles, uvs);
                }
                created[Key(e["id"])] = element;
                res.created.Add(element);
            }

            //references between the elements (local ids) or to existing ones (ids of the scripting API)
            object Resolve(JToken reference) {
                if (reference == null || reference.Type == JTokenType.Null) return null;
                if (reference.Type == JTokenType.String) return created.TryGetValue(reference.ToString(), out var l) ? l : null;
                if (reference is JObject obj) {
                    var id = obj["id"];
                    if (id == null) return null;
                    if (id.Type == JTokenType.String) return created.TryGetValue(id.ToString(), out var l) ? l : null;
                    return History.UndoManager.GetAliveElement((int)id);
                }
                if (reference.Type == JTokenType.Integer) return History.UndoManager.GetAliveElement((int)reference);
                return null;
            }
            foreach (var e in outputElements) {
                var element = created[Key(e["id"])];
                if (e["container"] != null) {
                    if (Resolve(e["container"]) is LowLevelElement container && !element.SetContainer(container)) {
                        GameEngine.LogWarning("Conversion: " + GO.GetName(element) + " can't be put in " + GO.GetName(container));
                    }
                } else if (defaultContainer != null && element.CanBeIn(defaultContainer)) {
                    element.SetContainer(defaultContainer);
                }
                element.source = e["source"] != null ? Resolve(e["source"]) : defaultSource;
                var type = types[element.TypeName];
                foreach (var name in type.GetReferenceParameters()) {
                    var token = (e["state"] as JObject)?[name];
                    if (token == null) continue;
                    var target = Resolve(token);
                    if (target == null) {
                        element.state.RemoveProperty(name);
                    } else {
                        var reference = new ObjectState();
                        reference.SetStr("kind", ElementKind(target));
                        reference.SetInt("id", Id(target));
                        element.state.SetState(name, reference);
                    }
                }
            }

            //the changes of existing elements: their points of the names given replaced, their state merged
            if (output["updates"] is JArray updateArray) {
                foreach (var u in updateArray) {
                    if (Resolve(u["element"]) is not LowLevelElement target || target.IsDeleted()) {
                        GameEngine.LogWarning("Conversion: the element of an update was not found");
                        continue;
                    }
                    History.UndoManager.Touch(target);
                    if (u["state"] is JObject partial) {
                        foreach (var p in partial.ToObject<ObjectState>().properties) {
                            if (p.Key != "type") target.state.SetProperty(p.Key, p.Value);
                        }
                    }
                    if (u["points"] is JObject newPoints) {
                        var tree = global::LowLevel.PointTree.ToJson(target.points, Id) as JObject ?? new JObject();
                        foreach (var p in newPoints.Properties()) tree[p.Name] = p.Value;
                        TerrainPoint GetPoint(JToken t) {
                            if (t.Type == JTokenType.Integer) return History.ElementRegistry.Get((int)t) as TerrainPoint;
                            return points.TryGetValue(t.ToString(), out var tp) ? tp : null;
                        }
                        target.SetPoints(History.ElementSnapshot.LowLevelPoints(tree, target.state, target, GetPoint, target.GetCenter()));
                    }
                    if (!res.updated.Contains(target)) res.updated.Add(target);
                }
            }

            //the points created but not used are deleted
            foreach (var p in createdPoints) {
                if (p.GetLinks().Count == 0) p.Delete();
            }

            //the converted elements are replaced
            var replace = output["replace"] != null ? (bool)output["replace"] : converter.replace;
            if (replace) {
                foreach (var e in elements) Replace(manager, e);
            }
            return res;
        }

        //the roads and intersections become ghosts, the other elements are deleted
        static void Replace(ElementManager manager, object element) {
            switch (element) {
                case Road r:
                    r.SetGhost(true);
                    break;
                case Intersection i:
                    i.SetGhost(true);
                    break;
                case TerrainPatch p:
                    TerrainPatches.Delete(p);
                    break;
                case BuildingLine l:
                    BuildingLines.Delete(l);
                    break;
                case LowLevelElement l:
                    //the elements replacing a low-level element take its place in its container
                    foreach (var child in manager.GetLowLevelChildren(l)) child.SetContainer(l.container);
                    l.Delete();
                    break;
            }
        }
    }
}

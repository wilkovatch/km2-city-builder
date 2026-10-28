using System.Collections.Generic;
using Godot;
using Lua;
using RuntimeCalculator.Scripting;

//The tables given to and returned by the Lua functions of the low-level types (see the Core Low-Level Elements page of the
//wiki): the control points mirror their structure (the groups are tables of named nodes, the arrays are lists, 1-based as
//usual in Lua, and the points are vec3), the vertices of the mesh are a list of vec3, its triangles a flat list of 0-based
//indices
namespace LowLevel {
    public class GeometryMesh {
        public string texture; //a texture parameter of the element (or a built-in material name)
        public List<Vector3> vertices = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();
        public List<int> triangles = new List<int>();
        public bool pickOnly; //only to select the element by clicking on it, not drawn
        //drawn (and clickable) only while none of the contents of the element draws a mesh (e.g. a room otherwise invisible)
        public bool placeholder;
    }

    public class GeometryLine {
        public List<Vector3> points = new List<Vector3>();
        public float? width; //null: a thin line growing with the camera distance
        public Color color = new Color(1, 1, 0);
        public string texture; //a texture parameter of the element (null: the color)
        public float heightOffset;
        public bool closed;
        public bool always; //shown also while the element is not being edited
        public string toggle; //a view toggle of the core (see Rendering.ViewToggles) hiding it, when always, while it's off
        public bool withContainer; //shown also while the container of the element, or an element in it, is being edited
    }

    //the lines along which the props of a props container of the prop rule are placed (as the propsLines of the roads)
    public class GeometryPropLine {
        public string container;
        public List<Vector3> distanceReference = new List<Vector3>();
        public List<Vector3> startBound = new List<Vector3>();
        public List<Vector3> endBound = new List<Vector3>(); //as many points as startBound
    }

    public class GeometryResult {
        public List<GeometryMesh> meshes = new List<GeometryMesh>();
        public List<GeometryLine> lines = new List<GeometryLine>();
        public List<GeometryPropLine> propLines = new List<GeometryPropLine>();
        public List<Vector2> uvs; //the texture coordinates of the vertices of the mesh of the element (when computed by the script)
    }

    public class ConstraintsResult {
        public Dictionary<string, Vector3> points = new Dictionary<string, Vector3>(); //by path
        public Dictionary<int, Vector3> vertices = new Dictionary<int, Vector3>(); //by 0-based index
    }

    public static class LowLevelScripting {
        // To Lua

        public static LuaTable PointsTable(PointNode node, System.Func<PointLeaf, LuaValue> leafValue) {
            switch (node) {
                case PointGroup group: {
                    var t = new LuaTable();
                    foreach (var (name, child) in group.children) {
                        var v = ToLua(child, leafValue);
                        if (v.Type != LuaValueType.Nil) t[name] = v;
                    }
                    return t;
                }
                case PointList list: {
                    var t = new LuaTable();
                    for (int i = 0; i < list.items.Count; i++) t[i + 1] = ToLua(list.items[i], leafValue);
                    return t;
                }
            }
            return new LuaTable();
        }

        static LuaValue ToLua(PointNode node, System.Func<PointLeaf, LuaValue> leafValue) {
            if (node is PointLeaf leaf) return leafValue(leaf);
            return PointsTable(node, leafValue);
        }

        public static LuaTable List(IReadOnlyList<Vector3> values) {
            var t = new LuaTable();
            for (int i = 0; i < values.Count; i++) t[i + 1] = LuaVectors.ToLua(values[i]);
            return t;
        }

        public static LuaTable List(IReadOnlyList<Vector2> values) {
            var t = new LuaTable();
            for (int i = 0; i < values.Count; i++) t[i + 1] = LuaVectors.ToLua(values[i]);
            return t;
        }

        public static LuaTable List(IReadOnlyList<int> values) {
            var t = new LuaTable();
            for (int i = 0; i < values.Count; i++) t[i + 1] = values[i];
            return t;
        }

        // From Lua

        static LuaTable Table(LuaValue value) {
            return value.TryRead<LuaTable>(out var t) ? t : null;
        }

        static float Number(LuaValue value, float fallback) {
            return value.TryRead<double>(out var d) ? (float)d : fallback;
        }

        static List<Vector3> Vector3s(LuaValue value, string what) {
            var res = new List<Vector3>();
            var t = Table(value);
            if (t == null) return res;
            for (int i = 1; i <= t.ArrayLength; i++) {
                if (!t[i].TryRead<LuaVec3>(out var v)) throw new System.Exception(what + "[" + i + "] is not a vec3");
                res.Add(v.value);
            }
            return res;
        }

        static List<Vector2> Vector2s(LuaValue value, string what) {
            var res = new List<Vector2>();
            var t = Table(value);
            if (t == null) return res;
            for (int i = 1; i <= t.ArrayLength; i++) {
                if (!t[i].TryRead<LuaVec2>(out var v)) throw new System.Exception(what + "[" + i + "] is not a vec2");
                res.Add(v.value);
            }
            return res;
        }

        static List<int> Ints(LuaValue value, string what) {
            var res = new List<int>();
            var t = Table(value);
            if (t == null) return res;
            for (int i = 1; i <= t.ArrayLength; i++) {
                if (!t[i].TryRead<double>(out var d)) throw new System.Exception(what + "[" + i + "] is not a number");
                res.Add((int)d);
            }
            return res;
        }

        static string String(LuaValue value) {
            return value.TryRead<string>(out var s) ? s : null;
        }

        static readonly Dictionary<string, Color> namedColors = new Dictionary<string, Color> {
            { "yellow", new Color(1, 1, 0) }, { "red", new Color(1, 0, 0) }, { "green", new Color(0, 1, 0) },
            { "blue", new Color(0, 0, 1) }, { "cyan", new Color(0, 1, 1) }, { "magenta", new Color(1, 0, 1) },
            { "white", new Color(1, 1, 1) }, { "black", new Color(0, 0, 0) }, { "orange", new Color(1, 0.5f, 0) }
        };

        static Color ReadColor(LuaValue value, Color fallback) {
            if (value.TryRead<LuaVec3>(out var v)) return new Color(v.value.X, v.value.Y, v.value.Z);
            var name = String(value);
            if (name != null && namedColors.TryGetValue(name, out var c)) return c;
            return fallback;
        }

        public static GeometryResult ReadGeometry(LuaValue value) {
            var res = new GeometryResult();
            var t = Table(value);
            if (t == null) return res;
            var meshes = Table(t["meshes"]);
            if (meshes != null) {
                for (int i = 1; i <= meshes.ArrayLength; i++) {
                    var m = Table(meshes[i]);
                    if (m == null) continue;
                    var mesh = new GeometryMesh {
                        texture = String(m["texture"]),
                        vertices = Vector3s(m["vertices"], "meshes[" + i + "].vertices"),
                        uvs = Vector2s(m["uvs"], "meshes[" + i + "].uvs"),
                        triangles = Ints(m["triangles"], "meshes[" + i + "].triangles"),
                        pickOnly = m["pickOnly"].TryRead<bool>(out var pickOnly) && pickOnly,
                        placeholder = m["placeholder"].TryRead<bool>(out var placeholder) && placeholder
                    };
                    foreach (var index in mesh.triangles) {
                        if (index < 0 || index >= mesh.vertices.Count) throw new System.Exception("meshes[" + i + "].triangles: index " + index + " out of the vertices");
                    }
                    if (mesh.triangles.Count % 3 != 0) throw new System.Exception("meshes[" + i + "].triangles: the count is not a multiple of 3");
                    while (mesh.uvs.Count < mesh.vertices.Count) mesh.uvs.Add(Vector2.Zero);
                    res.meshes.Add(mesh);
                }
            }
            var lines = Table(t["lines"]);
            if (lines != null) {
                for (int i = 1; i <= lines.ArrayLength; i++) {
                    var l = Table(lines[i]);
                    if (l == null) continue;
                    var line = new GeometryLine {
                        points = Vector3s(l["points"], "lines[" + i + "].points"),
                        texture = String(l["texture"]),
                        heightOffset = Number(l["heightOffset"], 0),
                        color = ReadColor(l["color"], new Color(1, 1, 0)),
                        closed = l["closed"].TryRead<bool>(out var closed) && closed,
                        always = l["always"].TryRead<bool>(out var always) && always,
                        toggle = String(l["toggle"]),
                        withContainer = l["withContainer"].TryRead<bool>(out var withContainer) && withContainer
                    };
                    if (l["width"].TryRead<double>(out var w)) line.width = (float)w;
                    if (line.points.Count >= 2) res.lines.Add(line);
                }
            }
            var propLines = Table(t["propLines"]);
            if (propLines != null) {
                for (int i = 1; i <= propLines.ArrayLength; i++) {
                    var l = Table(propLines[i]);
                    if (l == null) continue;
                    var line = new GeometryPropLine {
                        container = String(l["container"]),
                        startBound = Vector3s(l["startBound"], "propLines[" + i + "].startBound"),
                        endBound = Vector3s(l["endBound"], "propLines[" + i + "].endBound")
                    };
                    if (line.container == null) throw new System.Exception("propLines[" + i + "]: no container");
                    if (line.startBound.Count != line.endBound.Count) throw new System.Exception("propLines[" + i + "]: startBound and endBound have different counts");
                    if (line.startBound.Count >= 2) res.propLines.Add(line);
                }
            }
            if (t["uvs"].Type != LuaValueType.Nil) res.uvs = Vector2s(t["uvs"], "uvs");
            return res;
        }

        //the positions of the points returned (a table with the structure of the control points, with only the moved ones)
        //and of the vertices ({[index] = vec3}, 1-based)
        public static ConstraintsResult ReadConstraints(LuaValue value, PointNode root, int vertexCount) {
            var res = new ConstraintsResult();
            var t = Table(value);
            if (t == null) return res;
            var points = Table(t["points"]);
            if (points != null) ReadPoints(points, root, "", res.points);
            var vertices = Table(t["vertices"]);
            if (vertices != null) {
                for (int i = 1; i <= vertexCount; i++) {
                    if (vertices[i].TryRead<LuaVec3>(out var v)) res.vertices[i - 1] = v.value;
                }
            }
            return res;
        }

        //a whole tree of control points (the derived points): its structure (the leaves without their terrain points yet,
        //checked with PointTree.Validate) and the position of every leaf
        public static (PointGroup tree, Dictionary<PointLeaf, Vector3> positions) ReadPointTree(LuaValue value, CityElements.Types.LowLevelPointNode rootDef) {
            var positions = new Dictionary<PointLeaf, Vector3>();
            var t = Table(value);
            if (t == null) throw new System.Exception("points is not a table");
            var tree = (PointGroup)ReadNode(t, rootDef, "points", positions, false);
            var problem = PointTree.Validate(tree);
            if (problem != null) throw new System.Exception("points: " + problem);
            return (tree, positions);
        }

        static PointNode ReadNode(LuaValue value, CityElements.Types.LowLevelPointNode def, string path, Dictionary<PointLeaf, Vector3> positions, bool asArray = true) {
            if (asArray && def.array != null) {
                var list = new PointList(def);
                var items = Table(value);
                if (items == null && value.Type != LuaValueType.Nil) throw new System.Exception(path + " is not a list");
                for (int i = 1; items != null && i <= items.ArrayLength; i++) list.items.Add(ReadNode(items[i], def, path + "[" + i + "]", positions, false));
                return list;
            }
            if (def.children != null) {
                var t = Table(value);
                if (t == null) throw new System.Exception(path + " is not a table");
                var group = new PointGroup(def);
                foreach (var child in def.children) group.children.Add((child.name, ReadNode(t[child.name], child, path + "." + child.name, positions)));
                return group;
            }
            if (!value.TryRead<LuaVec3>(out var v)) throw new System.Exception(path + " is not a vec3");
            var leaf = new PointLeaf(def, null);
            positions[leaf] = v.value;
            return leaf;
        }

        static void ReadPoints(LuaTable t, PointNode node, string path, Dictionary<string, Vector3> res) {
            switch (node) {
                case PointGroup group:
                    foreach (var (name, child) in group.children) ReadChild(t[name], child, path == "" ? name : path + "." + name, res);
                    break;
                case PointList list:
                    for (int i = 0; i < list.items.Count; i++) ReadChild(t[i + 1], list.items[i], path == "" ? i.ToString() : path + "." + i, res);
                    break;
            }
        }

        static void ReadChild(LuaValue value, PointNode node, string path, Dictionary<string, Vector3> res) {
            if (value.Type == LuaValueType.Nil) return;
            if (node is PointLeaf) {
                if (value.TryRead<LuaVec3>(out var v)) res[path] = v.value;
                return;
            }
            var t = Table(value);
            if (t != null) ReadPoints(t, node, path, res);
        }
    }
}

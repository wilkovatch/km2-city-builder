using System.Collections.Generic;
using Godot;
using Newtonsoft.Json.Linq;
using Def = CityElements.Types.LowLevelPointNode;

//The control points of a low-level element, with the structure defined by its type: named points, groups of points and
//arrays of points or groups. A point is a terrain point, so it can be anchored and shared like the points of a patch.
//Paths name a point or a node: the names of the nodes joined by dots, with the 0-based index of the element in each array
//(e.g. "sections.2.leftRoad").
namespace LowLevel {
    public abstract class PointNode {
        public readonly Def def;

        protected PointNode(Def def) {
            this.def = def;
        }
    }

    public class PointLeaf : PointNode {
        public TerrainPoint point;

        public PointLeaf(Def def, TerrainPoint point) : base(def) {
            this.point = point;
        }
    }

    //the children of a group, or the root of the element (in the order of the definitions)
    public class PointGroup : PointNode {
        public readonly List<(string name, PointNode node)> children = new List<(string, PointNode)>();

        public PointGroup(Def def) : base(def) { }

        public PointNode Get(string name) {
            foreach (var (n, node) in children) {
                if (n == name) return node;
            }
            return null;
        }
    }

    //the elements of an array: points or groups, all with the definition of the array
    public class PointList : PointNode {
        public readonly List<PointNode> items = new List<PointNode>();

        public PointList(Def def) : base(def) { }
    }

    public static class PointTree {
        //the definition of the root (a group with the control points of the type as children)
        public static Def RootDef(Def[] controlPoints) {
            return new Def { name = "", children = controlPoints ?? new Def[0] };
        }

        static bool IsGroup(Def def) {
            return def.children != null;
        }

        // Creation

        //a new node with the default content (the minimum number of elements of the arrays, at least defaultCount),
        //the points created by createPoint(definition, offset of the point from the origin of the element)
        public static PointNode CreateDefault(Def def, System.Func<Def, Vector3, TerrainPoint> createPoint, Vector3 offset, bool asArray = true) {
            if (asArray && def.array != null) {
                var list = new PointList(def);
                var count = System.Math.Max(def.array.min, def.array.defaultCount);
                var step = ToVector(def.array.defaultStep, Vector3.Zero);
                for (int i = 0; i < count; i++) list.items.Add(CreateDefault(def, createPoint, offset + step * i, false));
                return list;
            }
            if (IsGroup(def)) {
                var group = new PointGroup(def);
                foreach (var child in def.children) group.children.Add((child.name, CreateDefault(child, createPoint, offset)));
                return group;
            }
            return new PointLeaf(def, createPoint(def, offset + ToVector(def.defaultPosition, Vector3.Zero)));
        }

        public static Vector3 ToVector(float[] a, Vector3 fallback) {
            if (a == null || a.Length < 3) return fallback;
            return new Vector3(a[0], a[1], a[2]);
        }

        // Traversal

        //every point, with its path (in the order of the definitions and of the arrays)
        public static List<(string path, PointLeaf leaf)> Leaves(PointNode root) {
            var res = new List<(string, PointLeaf)>();
            CollectLeaves(root, "", res);
            return res;
        }

        static string Join(string prefix, string name) {
            return prefix == "" ? name : prefix + "." + name;
        }

        static void CollectLeaves(PointNode node, string path, List<(string, PointLeaf)> res) {
            switch (node) {
                case PointLeaf leaf:
                    res.Add((path, leaf));
                    break;
                case PointGroup group:
                    foreach (var (name, child) in group.children) CollectLeaves(child, Join(path, name), res);
                    break;
                case PointList list:
                    for (int i = 0; i < list.items.Count; i++) CollectLeaves(list.items[i], Join(path, i.ToString()), res);
                    break;
            }
        }

        //the node at the path (null if there is none)
        public static PointNode Find(PointNode root, string path) {
            if (string.IsNullOrEmpty(path)) return root;
            var cur = root;
            foreach (var part in path.Split('.')) {
                switch (cur) {
                    case PointGroup group:
                        cur = group.Get(part);
                        break;
                    case PointList list:
                        if (!int.TryParse(part, out var i) || i < 0 || i >= list.items.Count) return null;
                        cur = list.items[i];
                        break;
                    default:
                        return null;
                }
                if (cur == null) return null;
            }
            return cur;
        }

        //the path of the point in the tree (null if it isn't there)
        public static string PathOf(PointNode root, TerrainPoint point) {
            foreach (var (path, leaf) in Leaves(root)) {
                if (leaf.point == point) return path;
            }
            return null;
        }

        //the innermost (or outermost) array whose element contains the path: the path of the array and the index of the
        //element (null if the path is not in an array)
        public static (string arrayPath, int index)? FindArrayElement(PointNode root, string path, bool editableOnly, bool outermost = false) {
            if (path == null) return null;
            var parts = path.Split('.');
            (string, int)? res = null;
            var cur = root;
            var curPath = "";
            for (int p = 0; p < parts.Length && cur != null; p++) {
                if (cur is PointList list) {
                    if (!int.TryParse(parts[p], out var i) || i < 0 || i >= list.items.Count) return res;
                    if (!editableOnly || list.def.array.editable) {
                        res = (curPath, i);
                        if (outermost) return res;
                    }
                    cur = list.items[i];
                } else if (cur is PointGroup group) {
                    cur = group.Get(parts[p]);
                } else {
                    break;
                }
                curPath = Join(curPath, parts[p]);
            }
            return res;
        }

        // Arrays

        //an editable array is inside the element of another one (e.g. the lanes of a section)
        public static bool HasNestedArrays(Def def, bool inArray = false) {
            var editable = def.array != null && def.array.editable;
            if (editable && inArray) return true;
            if (def.children == null) return false;
            foreach (var child in def.children) {
                if (HasNestedArrays(child, inArray || editable)) return true;
            }
            return false;
        }

        //the instances of the array at the path in every element of the arrays containing it (the path itself only, if
        //it is not uniform): the elements of a uniform array keep the same count everywhere
        public static List<PointList> UniformInstances(PointNode root, string arrayPath) {
            var res = new List<PointList>();
            var list = Find(root, arrayPath) as PointList;
            if (list == null) return res;
            if (!list.def.array.uniform) {
                res.Add(list);
                return res;
            }
            //the path with every index replaced by a wildcard
            CollectMatching(root, arrayPath.Split('.'), 0, res);
            return res;
        }

        static void CollectMatching(PointNode node, string[] parts, int p, List<PointList> res) {
            if (p == parts.Length) {
                if (node is PointList l) res.Add(l);
                return;
            }
            if (node is PointGroup group) {
                var child = group.Get(parts[p]);
                if (child != null) CollectMatching(child, parts, p + 1, res);
            } else if (node is PointList list) {
                foreach (var item in list.items) CollectMatching(item, parts, p + 1, res);
            }
        }

        //a new element of the list at the index: its points between the ones of the elements around it (or continuing
        //the list at its ends; in a closed list, between the last and the first element), the uniform arrays in it with as
        //many elements as in the element next to it
        public static PointNode CreateArrayElement(PointList list, int index, System.Func<Def, Vector3, TerrainPoint> createPoint, Vector3 origin) {
            var before = index > 0 ? list.items[index - 1] : null;
            var after = index < list.items.Count ? list.items[index] : null;
            if (list.def.array.closed && list.items.Count >= 2) {
                before ??= list.items[list.items.Count - 1];
                after ??= list.items[0];
            }
            var before2 = index > 1 ? list.items[index - 2] : null;
            var after2 = index + 1 < list.items.Count ? list.items[index + 1] : null;
            var model = before ?? after;
            var step = ToVector(list.def.array.defaultStep, new Vector3(1, 0, 0));
            if (model == null) return CreateDefault(list.def, createPoint, origin, false);
            return CreateLike(model, list.def, (relPath) => {
                var b = before != null ? PositionAt(before, relPath) : (Vector3?)null;
                var a = after != null ? PositionAt(after, relPath) : (Vector3?)null;
                if (b.HasValue && a.HasValue) return (b.Value + a.Value) * 0.5f;
                if (b.HasValue) {
                    var b2 = before2 != null ? PositionAt(before2, relPath) : null;
                    return b2.HasValue ? b.Value * 2 - b2.Value : b.Value + step;
                }
                var a2 = after2 != null ? PositionAt(after2, relPath) : null;
                return a2.HasValue ? a.Value * 2 - a2.Value : a.Value - step;
            }, createPoint);
        }

        static Vector3? PositionAt(PointNode node, string relPath) {
            var leaf = Find(node, relPath) as PointLeaf;
            return leaf?.point != null && !leaf.point.IsDeleted() ? leaf.point.GetPoint() : (Vector3?)null;
        }

        //a copy of the structure of the model, with new points
        static PointNode CreateLike(PointNode model, Def def, System.Func<string, Vector3> position, System.Func<Def, Vector3, TerrainPoint> createPoint, string relPath = "") {
            switch (model) {
                case PointLeaf:
                    return new PointLeaf(def, createPoint(def, position(relPath)));
                case PointGroup group: {
                    var res = new PointGroup(def);
                    foreach (var (name, child) in group.children) {
                        var childDef = System.Array.Find(def.children, d => d.name == name);
                        res.children.Add((name, CreateLike(child, childDef, position, createPoint, Join(relPath, name))));
                    }
                    return res;
                }
                case PointList list: {
                    var res = new PointList(def);
                    for (int i = 0; i < list.items.Count; i++) {
                        res.items.Add(CreateLike(list.items[i], def, position, createPoint, Join(relPath, i.ToString())));
                    }
                    return res;
                }
            }
            return null;
        }

        // Serialization (the points as ids: the city save, the undo snapshots and the converters)

        public static JToken ToJson(PointNode node, System.Func<TerrainPoint, int> pointId) {
            switch (node) {
                case PointLeaf leaf:
                    return new JValue(pointId(leaf.point));
                case PointGroup group: {
                    var obj = new JObject();
                    foreach (var (name, child) in group.children) obj[name] = ToJson(child, pointId);
                    return obj;
                }
                case PointList list: {
                    var arr = new JArray();
                    foreach (var item in list.items) arr.Add(ToJson(item, pointId));
                    return arr;
                }
            }
            return JValue.CreateNull();
        }

        //the node of the definition from its JSON; the missing points (or the ones that can't be resolved) are created by
        //createPoint, the missing elements of the arrays below their minimum too
        public static PointNode FromJson(JToken token, Def def, System.Func<JToken, TerrainPoint> getPoint,
            System.Func<Def, Vector3, TerrainPoint> createPoint, Vector3 origin, bool asArray = true) {

            if (asArray && def.array != null) {
                var list = new PointList(def);
                if (token is JArray arr) {
                    foreach (var item in arr) list.items.Add(FromJson(item, def, getPoint, createPoint, origin, false));
                }
                while (list.items.Count < def.array.min) {
                    list.items.Add(CreateArrayElement(list, list.items.Count, createPoint, origin));
                }
                return list;
            }
            if (IsGroup(def)) {
                var group = new PointGroup(def);
                foreach (var child in def.children) {
                    var childToken = (token as JObject)?[child.name];
                    group.children.Add((child.name, FromJson(childToken, child, getPoint, createPoint, origin)));
                }
                return group;
            }
            var point = token != null && token.Type != JTokenType.Null ? getPoint(token) : null;
            return new PointLeaf(def, point ?? createPoint(def, origin + ToVector(def.defaultPosition, Vector3.Zero)));
        }

        //the problems of the structure (counts of the arrays, uniform arrays with different counts), null if none
        public static string Validate(PointNode root) {
            string res = null;
            void Check(PointNode node, string path) {
                if (res != null) return;
                switch (node) {
                    case PointGroup group:
                        foreach (var (name, child) in group.children) Check(child, Join(path, name));
                        break;
                    case PointList list:
                        var a = list.def.array;
                        if (list.items.Count < a.min) res = "the array " + path + " has less than " + a.min + " elements";
                        else if (a.max > 0 && list.items.Count > a.max) res = "the array " + path + " has more than " + a.max + " elements";
                        for (int i = 0; i < list.items.Count; i++) Check(list.items[i], Join(path, i.ToString()));
                        break;
                }
            }
            Check(root, "");
            if (res != null) return res;
            //uniform arrays: every instance of each one has the same count
            var counts = new Dictionary<string, int>();
            foreach (var (path, list) in Lists(root)) {
                if (!list.def.array.uniform) continue;
                var key = Wildcard(path);
                if (counts.TryGetValue(key, out var count) && count != list.items.Count) return "the uniform array " + key + " has different counts";
                counts[key] = list.items.Count;
            }
            return null;
        }

        //every array, with its path
        public static List<(string path, PointList list)> Lists(PointNode root) {
            var res = new List<(string, PointList)>();
            void Collect(PointNode node, string path) {
                switch (node) {
                    case PointGroup group:
                        foreach (var (name, child) in group.children) Collect(child, Join(path, name));
                        break;
                    case PointList list:
                        res.Add((path, list));
                        for (int i = 0; i < list.items.Count; i++) Collect(list.items[i], Join(path, i.ToString()));
                        break;
                }
            }
            Collect(root, "");
            return res;
        }

        //the path with its indices replaced by "*"
        public static string Wildcard(string path) {
            var parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++) {
                if (int.TryParse(parts[i], out _)) parts[i] = "*";
            }
            return string.Join(".", parts);
        }
    }
}

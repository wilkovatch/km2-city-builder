using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CityElements.Types.Parsers {
    //Inheritance between the types of a core: a type can extend another type of the same category and only give what
    //differs from it. A folder type names its parent in a type.json file ({"extends": "<folder>", "abstract": false}),
    //a single file type with the "extends" and "abstract" keys of its file. The files of a type are merged over the ones
    //of its ancestors (missing files are taken from the nearest ancestor that has them), see the Core Type Inheritance
    //page of the wiki for the merge rules.
    public static class TypeInheritance {
        public const string TypeFile = "type.json";

        //the categories of types, as the folders inside the elements folder of the core
        public const string Roads = "roads";
        public const string Intersections = "intersections";
        public const string TerrainPatches = "terrainPatches/terrainPatches";
        public const string BorderMeshes = "terrainPatches/borderMeshes";
        public const string Buildings = "buildings";
        public const string BuildingBlocks = "buildingBlocks";
        public const string PropsElements = "props/elements";
        public const string PropsContainers = "props/containers";
        public const string ParametricObjects = "props/parametric";
        public const string CustomTypes = "customTypes";

        static readonly string[] keyFields = { "name", "label", "category" };

        // Ancestries of the loaded types

        static readonly Dictionary<string, Dictionary<string, string[]>> ancestries = new Dictionary<string, Dictionary<string, string[]>>();

        public static void SetAncestries(string category, Dictionary<string, string[]> types) {
            ancestries[category] = types;
        }

        //the type followed by its ancestors, nearest first (only the type if it is not known)
        public static string[] GetAncestry(string category, string name) {
            if (name != null && ancestries.TryGetValue(category, out var types) && types.TryGetValue(name, out var res)) return res;
            return new[] { name };
        }

        public static List<(string category, string name, string[] ancestry)> GetAllAncestries() {
            var res = new List<(string, string, string[])>();
            foreach (var category in ancestries) {
                foreach (var type in category.Value) res.Add((category.Key, type.Key, type.Value));
            }
            return res;
        }

        // Ancestry from the files

        static JObject ReadTypeInfo(string folder) {
            var file = Path.Combine(folder, TypeFile);
            if (!File.Exists(file)) return null;
            return ReadJson(file) as JObject ?? throw new InvalidDataException(file + ": not a JSON object");
        }

        static string ParentName(JObject info, string source) {
            var parent = info?["extends"];
            if (parent == null || parent.Type == JTokenType.Null) return null;
            var name = parent.Type == JTokenType.String ? parent.Value<string>() : null;
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name == "." || name == "..") {
                throw new InvalidDataException(source + ": \"extends\" must be the name of another type of the same category");
            }
            return name;
        }

        static string[] Ancestry(string start, System.Func<string, string> parentOf, System.Func<string, bool> exists, string category) {
            var res = new List<string>();
            var cur = start;
            while (cur != null) {
                if (res.Contains(cur)) {
                    throw new InvalidDataException("type '" + start + "' of " + category + ": inheritance cycle (" + string.Join(" -> ", res) + " -> " + cur + ")");
                }
                if (res.Count > 0 && !exists(cur)) {
                    throw new InvalidDataException("type '" + res[res.Count - 1] + "' of " + category + " extends '" + cur + "', which doesn't exist");
                }
                res.Add(cur);
                cur = parentOf(cur);
            }
            return res.ToArray();
        }

        //the names of the folder type and of its ancestors, nearest first
        public static string[] FolderAncestry(string folder) {
            var categoryDir = Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd('/', '\\'));
            return Ancestry(Path.GetFileName(folder.TrimEnd('/', '\\')),
                name => ParentName(ReadTypeInfo(Path.Combine(categoryDir, name)), Path.Combine(categoryDir, name, TypeFile)),
                name => Directory.Exists(Path.Combine(categoryDir, name)), categoryDir);
        }

        static string[] AncestorFolders(string folder) {
            var categoryDir = Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd('/', '\\'));
            var names = FolderAncestry(folder);
            var res = new string[names.Length];
            res[0] = folder;
            for (int i = 1; i < names.Length; i++) res[i] = Path.Combine(categoryDir, names[i]);
            return res;
        }

        public static bool IsAbstractFolder(string folder) {
            var info = ReadTypeInfo(folder);
            return info != null && Flag(info, "abstract");
        }

        //the names of the single file type and of its ancestors, nearest first
        public static string[] SingleFileAncestry(string file) {
            var dir = Path.GetDirectoryName(file);
            var ext = Path.GetExtension(file);
            string FileOf(string name) => Path.Combine(dir, name + ext);
            return Ancestry(Path.GetFileNameWithoutExtension(file),
                name => ParentName(ReadJson(FileOf(name)) as JObject, FileOf(name)),
                name => File.Exists(FileOf(name)), dir);
        }

        public static bool IsAbstractFile(string file) {
            return ReadJson(file) is JObject obj && Flag(obj, "abstract");
        }

        // Loading

        public static JToken ReadJson(string path) {
            using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None }) {
                return JToken.ReadFrom(reader);
            }
        }

        //a file found in one place only is read as it is, the others are merged (farthest ancestor first)
        static T LoadMerged<T>(List<string> files, string description) {
            if (files.Count == 0) throw new FileNotFoundException(description + " not found");
            if (files.Count == 1) return JsonConvert.DeserializeObject<T>(File.ReadAllText(files[0]));
            JToken merged = null;
            for (int i = files.Count - 1; i >= 0; i--) {
                var token = ReadJson(files[i]);
                merged = merged == null ? token : Merge(merged, token, files[i]);
                StripMarkers(merged);
            }
            return merged.ToObject<T>();
        }

        //a file of a folder type (e.g. "settings.json" or "junctions/a_b.json")
        public static T Load<T>(string folder, string relativeFile) {
            var files = new List<string>();
            foreach (var dir in AncestorFolders(folder)) {
                var path = Path.Combine(dir, relativeFile);
                if (File.Exists(path)) files.Add(path);
            }
            return LoadMerged<T>(files, Path.Combine(folder, relativeFile));
        }

        //a component of a folder type: from the components folder of the type or of its ancestors, otherwise from the
        //common folder (if any)
        public static T LoadComponent<T>(string folder, string name, string commonFolder) {
            var files = new List<string>();
            foreach (var dir in AncestorFolders(folder)) {
                var path = Path.Combine(dir, "components", name + ".json");
                if (File.Exists(path)) files.Add(path);
            }
            if (files.Count == 0 && commonFolder != null) {
                var path = Path.Combine(commonFolder, name + ".json");
                if (File.Exists(path)) files.Add(path);
            }
            return LoadMerged<T>(files, "component '" + name + "' of " + folder);
        }

        //the names (without extension) of the JSON files in a subfolder of the folder type and of its ancestors
        public static List<string> FileNames(string folder, string subfolder) {
            var res = new List<string>();
            foreach (var dir in AncestorFolders(folder)) {
                var path = Path.Combine(dir, subfolder);
                if (!Directory.Exists(path)) continue;
                foreach (var file in Directory.GetFiles(path, "*.json")) {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!res.Contains(name)) res.Add(name);
                }
            }
            return res;
        }

        //a single file type
        public static T LoadSingle<T>(string file) {
            var ext = Path.GetExtension(file);
            var dir = Path.GetDirectoryName(file);
            var files = SingleFileAncestry(file).Select(name => Path.Combine(dir, name + ext)).ToList();
            return LoadMerged<T>(files, file);
        }

        // Merging

        static bool IsMarker(string name) {
            return name.StartsWith("$");
        }

        static bool Flag(JToken obj, string name) {
            var v = obj[name];
            return v != null && v.Type == JTokenType.Boolean && v.Value<bool>();
        }

        //removes the "$..." keys, recursively
        public static void StripMarkers(JToken token) {
            if (token is JObject obj) {
                foreach (var p in obj.Properties().ToList()) {
                    if (IsMarker(p.Name)) p.Remove();
                    else StripMarkers(p.Value);
                }
            } else if (token is JArray arr) {
                foreach (var e in arr) StripMarkers(e);
            }
        }

        //the child merged over the parent, without changing either; source is used in the messages
        public static JToken Merge(JToken parent, JToken child, string source = "") {
            if (child is JObject childObj) {
                if (Flag(childObj, "$replace") || !(parent is JObject parentObj)) return childObj.DeepClone();
                var res = (JObject)parentObj.DeepClone();
                foreach (var p in childObj.Properties()) {
                    if (p.Name == "$replace") continue;
                    var cur = res[p.Name];
                    res[p.Name] = cur != null && !IsMarker(p.Name) ? Merge(cur, p.Value, source) : p.Value.DeepClone();
                }
                return res;
            }
            if (child is JArray childArr && parent is JArray parentArr) {
                var key = KeyField(parentArr, childArr);
                if (key != null) return MergeKeyed(parentArr, childArr, key, source);
            }
            return child.DeepClone();
        }

        //the field identifying the elements of both arrays, if all of them are objects with it
        static string KeyField(JArray parent, JArray child) {
            if (child.Count == 0) return null;
            foreach (var key in keyFields) {
                bool HasKey(JToken e) => e is JObject o && o[key]?.Type == JTokenType.String;
                if (parent.All(HasKey) && child.All(HasKey)) return key;
            }
            return null;
        }

        //elements with the same key are told apart by their container (parameters) and fromStart (imported parameters)
        static string Identity(JToken e, string key) {
            var container = e["container"]?.Type == JTokenType.String ? e["container"].Value<string>() : "";
            var fromStart = e["fromStart"]?.Type == JTokenType.Boolean && e["fromStart"].Value<bool>();
            return e[key].Value<string>() + "\n" + container + "\n" + fromStart;
        }

        //the name used to refer to an element in "$after" and "$before": the key, prefixed by the container if any
        static string FullName(JToken e, string key) {
            var container = e["container"]?.Type == JTokenType.String ? e["container"].Value<string>() : null;
            return container != null ? container + "_" + e[key].Value<string>() : e[key].Value<string>();
        }

        static JArray MergeKeyed(JArray parent, JArray child, string key, string source) {
            var list = parent.Select(e => e.DeepClone()).ToList();
            var ids = list.Select(e => Identity(e, key)).ToList();
            if (ids.Distinct().Count() != ids.Count) {
                GameEngine.LogWarning(source + ": the inherited array has elements with the same " + key + ", it is replaced instead of merged");
                return (JArray)child.DeepClone();
            }
            foreach (var entry in child) {
                var id = Identity(entry, key);
                var idx = list.FindIndex(e => Identity(e, key) == id);
                if (Flag(entry, "$remove")) {
                    if (idx >= 0) list.RemoveAt(idx);
                    else GameEngine.LogWarning(source + ": '" + FullName(entry, key) + "' to remove is not inherited");
                    continue;
                }
                var merged = idx >= 0 ? Merge(list[idx], entry, source) : entry.DeepClone();
                var after = entry["$after"]?.Type == JTokenType.String ? entry["$after"].Value<string>() : null;
                var before = entry["$before"]?.Type == JTokenType.String ? entry["$before"].Value<string>() : null;
                if (after != null || before != null) {
                    if (idx >= 0) list.RemoveAt(idx);
                    var target = after ?? before;
                    var matches = Enumerable.Range(0, list.Count).Where(i => FullName(list[i], key) == target).ToList();
                    if (matches.Count != 1) {
                        throw new InvalidDataException(source + ": '" + FullName(entry, key) + "' is placed " + (after != null ? "after" : "before") + " '" + target + "', which "
                            + (matches.Count == 0 ? "is not in the array" : "is ambiguous"));
                    }
                    list.Insert(after != null ? matches[0] + 1 : matches[0], merged);
                } else if (idx >= 0) {
                    list[idx] = merged;
                } else {
                    list.Add(merged);
                }
            }
            return new JArray(list);
        }
    }
}

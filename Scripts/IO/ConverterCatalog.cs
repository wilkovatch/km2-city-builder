using System.Collections.Generic;
using System.IO;

//The converters of the current core (its converters folder): Python scripts turning elements of the city into low-level
//elements (see the Core Low-Level Elements page of the wiki). Each one is a folder with a converter.json:
//{"label": ..., "sources": ["road", "lowLevel/triangleFan", ...], "types": [...], "script": "convert.py", "replace": true}
public static class ConverterCatalog {
    public class ConverterInfo {
        public string name; //the folder
        public string folder;
        public string label;
        public string tooltip;
        public string[] sources; //the kinds converted: road, intersection, terrainPatch, buildingLine, lowLevel, lowLevel/<type>
        public string[] types; //the procedural types converted (all if null)
        public string script = "convert.py";
        //the converted elements are replaced: the roads and intersections become ghosts, the others are deleted
        public bool replace = true;
        public string enabledWhen; //optional Lua predicate, given detached snapshots of the element and its neighbors

        public string ScriptPath => Path.Combine(folder, script);

        public bool IsEnabled(object element) {
            if (element == null || !History.ElementSnapshot.IsAlive(element) || !Accepts(element)) return false;
            if (enabledWhen == null) return true;
            Lua.LuaValue Snapshot(object value) {
                var result = new Lua.LuaTable();
                result["kind"] = History.ElementSnapshot.ReferenceKind(value) ?? "";
                result["id"] = History.ElementRegistry.GetId(value);
                result["state"] = JsonValue(Newtonsoft.Json.Linq.JToken.FromObject(((IObjectWithState)value).GetState()));
                return result;
            }
            var ctx = new Lua.LuaTable();
            ctx["element"] = Snapshot(element);
            var neighbors = new Lua.LuaTable();
            if (element is Intersection intersection) {
                int i = 0;
                foreach (var connected in intersection.roads) if (!connected.deleted) neighbors[++i] = Snapshot(connected);
            } else if (element is Road road) {
                if (road.startIntersection != null) neighbors[1] = Snapshot(road.startIntersection);
                if (road.endIntersection != null) neighbors[2] = Snapshot(road.endIntersection);
            }
            ctx["neighbors"] = neighbors;
            var host = RuntimeCalculator.Scripting.LuaScriptHost.Get();
            try {
                var value = host.Call(host.Resolve(enabledWhen), Lua.LuaValue.Nil, ctx, name + " (availability)");
                return value.TryRead<bool>(out var enabled) && enabled;
            } catch (System.Exception e) {
                host.ReportError(name + " (availability)", e.Message);
                return false;
            }
        }

        static Lua.LuaValue JsonValue(Newtonsoft.Json.Linq.JToken token) {
            if (token is Newtonsoft.Json.Linq.JObject obj) {
                var table = new Lua.LuaTable();
                foreach (var p in obj.Properties()) table[p.Name] = JsonValue(p.Value);
                return table;
            }
            if (token is Newtonsoft.Json.Linq.JArray array) {
                var table = new Lua.LuaTable();
                for (int i = 0; i < array.Count; i++) table[i + 1] = JsonValue(array[i]);
                return table;
            }
            return token.Type switch {
                Newtonsoft.Json.Linq.JTokenType.Boolean => new Lua.LuaValue((bool)token),
                Newtonsoft.Json.Linq.JTokenType.Integer or Newtonsoft.Json.Linq.JTokenType.Float => new Lua.LuaValue((double)token),
                Newtonsoft.Json.Linq.JTokenType.String => new Lua.LuaValue((string)token),
                _ => Lua.LuaValue.Nil
            };
        }

        public bool Accepts(object element) {
            if (sources == null) return false;
            var kind = History.ElementSnapshot.ReferenceKind(element);
            if (kind == null) return false;
            string type = null;
            if (element is IObjectWithState withState) type = withState.GetState()?.Str("type", null);
            foreach (var source in sources) {
                if (source == kind || (kind == "lowLevel" && source == "lowLevel/" + type)) {
                    if (types == null || kind == "lowLevel" || System.Array.IndexOf(types, type) >= 0) return true;
                }
            }
            return false;
        }
    }

    static string Folder() {
        return PathHelper.CoreAvailable() ? Path.Combine(PathHelper.FilesPath(), "converters") : null;
    }

    public static List<ConverterInfo> List() {
        var res = new List<ConverterInfo>();
        var folder = Folder();
        if (folder == null || !Directory.Exists(folder)) return res;
        var dirs = Directory.GetDirectories(folder);
        System.Array.Sort(dirs, System.StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs) {
            var file = Path.Combine(dir, "converter.json");
            if (!File.Exists(file)) continue;
            try {
                var info = Newtonsoft.Json.JsonConvert.DeserializeObject<ConverterInfo>(File.ReadAllText(file));
                info.name = Path.GetFileName(dir);
                info.folder = dir;
                res.Add(info);
            } catch (System.Exception e) {
                GameEngine.LogError("Converter " + dir + ": " + e.Message);
            }
        }
        return res;
    }

    public static ConverterInfo Get(string name) {
        return List().Find(c => c.name == name);
    }

    //the converters that accept the element
    public static List<ConverterInfo> For(object element) {
        return List().FindAll(c => c.Accepts(element));
    }
}

using System.Collections.Generic;
using System.IO;

//A Python script of the core run on elements of the city: the converters (see ConverterCatalog) and the generators (see
//GeneratorCatalog). Each one is a folder of the core with a JSON file naming the elements it accepts and its script
public abstract class CoreElementTool {
    public string name; //the folder
    public string folder;
    public string[] sources; //the kinds accepted: road, intersection, terrainPatch, buildingLine, customElement, customElement/<type>
    public string[] types; //the procedural types accepted (all if null)
    public string script;
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
        } else if (element is CustomElement custom) {
            int i = 0;
            foreach (var owner in custom.GetAttachedOwners()) neighbors[++i] = Snapshot(owner);
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

    //a JSON value as a Lua value (objects and arrays as tables)
    internal static Lua.LuaValue JsonValue(Newtonsoft.Json.Linq.JToken token) {
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
        return token?.Type switch {
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
            if (source == kind || (kind == "customElement" && source == "customElement/" + type)) {
                if (types == null || kind == "customElement" || System.Array.IndexOf(types, type) >= 0) return true;
            }
        }
        return false;
    }

    //the tools in the folders of <core>/<folderName>, each with its <fileName>
    internal static List<T> Load<T>(string folderName, string fileName) where T : CoreElementTool {
        var res = new List<T>();
        if (!PathHelper.CoreAvailable()) return res;
        var folder = Path.Combine(PathHelper.FilesPath(), folderName);
        if (!Directory.Exists(folder)) return res;
        var dirs = Directory.GetDirectories(folder);
        System.Array.Sort(dirs, System.StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs) {
            var file = Path.Combine(dir, fileName);
            if (!File.Exists(file)) continue;
            try {
                var info = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(File.ReadAllText(file));
                info.name = Path.GetFileName(dir);
                info.folder = dir;
                res.Add(info);
            } catch (System.Exception e) {
                GameEngine.LogError(typeof(T).Name + " " + dir + ": " + e.Message);
            }
        }
        return res;
    }
}

//The converters of the current core (its converters folder): Python scripts turning elements of the city into custom
//elements (see the Core Custom Elements page of the wiki). Each one is a folder with a converter.json:
//{"label": ..., "sources": ["road", "customElement/triangleFan", ...], "types": [...], "script": "convert.py", "replace": true}
public static class ConverterCatalog {
    public class ConverterInfo : CoreElementTool {
        public string label;
        public string tooltip;
        //the converted elements are replaced: the roads and intersections become ghosts, the others are deleted
        public bool replace = true;

        public ConverterInfo() {
            script = "convert.py";
        }
    }

    public static List<ConverterInfo> List() => CoreElementTool.Load<ConverterInfo>("converters", "converter.json");

    public static ConverterInfo Get(string name) {
        return List().Find(c => c.name == name);
    }

    //the converters that accept the element
    public static List<ConverterInfo> For(object element) {
        return List().FindAll(c => c.Accepts(element));
    }
}

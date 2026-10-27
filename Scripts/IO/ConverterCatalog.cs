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

        public string ScriptPath => Path.Combine(folder, script);

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

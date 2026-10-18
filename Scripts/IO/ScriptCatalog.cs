using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CityElements.Types;

//The Python scripts of the Scripts menu: the .py files and the folders with a script.json in the scripts folder of the
//program, in the one of the current core and in the folders of the scriptFolders setting
public static class ScriptCatalog {
    public const string SettingsFile = "script.json";
    public const string DefaultEntry = "main.py";
    static readonly HashSet<string> inputTypes = new HashSet<string> {
        "bool", "int", "float", "enum", "string", "texture", "mesh", "array", "customElement", "preset", "file", "folder"
    };

#pragma warning disable CS0649 //set by the deserialization of script.json
    class Settings {
        public string label;
        public string description;
        public string entry;
        public bool? transaction;
        public Parameter[] parameters;
        public Panel? ui;
    }
#pragma warning restore CS0649

    public class ScriptInfo {
        public string id; //the source and the name, the key of the saved input values
        public string source; //program, core, user or file
        public string folder; //the working directory of the script
        public string entry; //the Python file run
        public string label;
        public string description;
        public bool transaction = true; //the changes of a run are a single undo step, undone if it fails
        public Parameter[] parameters = new Parameter[0];
        public Panel ui;
        public Dictionary<string, string> strings;

        public bool HasInputs => parameters.Length > 0;

        //a string of the script (strings/<language>.json in its folder), of the core or of the program, or the text itself
        public string GetString(string key) {
            if (string.IsNullOrEmpty(key)) return null;
            if (strings != null && strings.TryGetValue(key, out var res)) return res == "" ? null : res;
            res = StringManager.GetOrKey(key);
            return res == "" ? null : res;
        }
    }

    public static string ProgramFolder() {
        return Path.Combine(PathHelper.BasePath(), "Files", "scripts");
    }

    public static string CoreFolder() {
        return PathHelper.CoreAvailable() ? Path.Combine(PathHelper.FilesPath(), "scripts") : null;
    }

    public static List<string> UserFolders() {
        return SettingsManager.GetList<string>("scriptFolders", null) ?? new List<string>();
    }

    //the scripts of the program, then the ones of the core, then the ones of the user folders (each group by label)
    public static List<ScriptInfo> List() {
        var res = new List<ScriptInfo>();
        Scan(ProgramFolder(), "program", res);
        Scan(CoreFolder(), "core", res);
        foreach (var folder in UserFolders()) Scan(folder, "user", res);
        return res;
    }

    static bool Hidden(string path) {
        var name = Path.GetFileName(path);
        return name.StartsWith("_") || name.StartsWith(".");
    }

    static void Scan(string root, string source, List<ScriptInfo> res) {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        var found = new List<ScriptInfo>();
        foreach (var file in Directory.GetFiles(root, "*.py")) {
            if (!Hidden(file)) found.Add(FromFile(file, source));
        }
        foreach (var dir in Directory.GetDirectories(root)) {
            if (Hidden(dir) || !File.Exists(Path.Combine(dir, SettingsFile))) continue;
            try {
                found.Add(FromFolder(dir, source));
            } catch (System.Exception e) {
                GameEngine.LogWarning("Invalid script in " + dir + ": " + e.Message);
            }
        }
        found.Sort((a, b) => string.Compare(a.label, b.label, System.StringComparison.OrdinalIgnoreCase));
        res.AddRange(found);
    }

    //a script given by its path: a .py file or a folder with a script.json
    public static ScriptInfo FromPath(string path, string source = "file") {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path)) return FromFolder(path, source);
        if (!File.Exists(path)) throw new FileNotFoundException("script not found: " + path);
        return FromFile(path, source);
    }

    static ScriptInfo FromFile(string file, string source) {
        var name = Path.GetFileNameWithoutExtension(file);
        return new ScriptInfo {
            id = source + ":" + Path.GetFileName(file),
            source = source,
            folder = Path.GetDirectoryName(file),
            entry = file,
            label = name,
            ui = DefaultPanel(new Parameter[0])
        };
    }

    static ScriptInfo FromFolder(string dir, string source) {
        var settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(Path.Combine(dir, SettingsFile))) ?? new Settings();
        var entry = Path.Combine(dir, settings.entry ?? DefaultEntry);
        if (!File.Exists(entry)) throw new FileNotFoundException("the entry file " + entry + " does not exist");
        var parameters = new List<Parameter>();
        foreach (var p in settings.parameters ?? new Parameter[0]) {
            if (p == null) continue;
            if (CheckParameter(p, dir)) parameters.Add(p);
        }
        var info = new ScriptInfo {
            id = source + ":" + Path.GetFileName(dir),
            source = source,
            folder = dir,
            entry = entry,
            transaction = settings.transaction ?? true,
            parameters = parameters.ToArray(),
            strings = LoadStrings(dir)
        };
        info.label = info.GetString(settings.label) ?? Path.GetFileName(dir);
        info.description = info.GetString(settings.description);
        var ui = settings.ui ?? DefaultPanel(info.parameters);
        if (ui.menuWidth <= 0) ui.menuWidth = 1.5f;
        if (ui.tabs == null || ui.tabs.Length == 0) ui.tabs = DefaultPanel(info.parameters).tabs;
        info.ui = ui;
        return info;
    }

    //every parameter in a single tab, one per row
    static Panel DefaultPanel(Parameter[] parameters) {
        var elements = new List<TabElement>();
        foreach (var p in parameters) elements.Add(new TabElement { name = p.fullName(), width = 1.5f });
        return new Panel { label = "", menuWidth = 1.5f, tabs = new[] { new Tab { label = "", elements = elements.ToArray() } } };
    }

    //warns about the parameters that can't be shown, prepares the default values for the elements of arrays
    static bool CheckParameter(Parameter p, string dir) {
        void Warn(string message) => GameEngine.LogWarning("Script " + dir + ", parameter " + p.name + ": " + message);
        if (string.IsNullOrEmpty(p.name) || string.IsNullOrEmpty(p.type)) {
            GameEngine.LogWarning("Script " + dir + ": a parameter has no name or type");
            return false;
        }
        if (!inputTypes.Contains(p.type)) {
            Warn("the type " + p.type + " can't be an input of a script");
            return false;
        }
        if (p.type == "enum" && (p.enumLabels == null || p.enumLabels.Length == 0)) {
            Warn("an enum needs enumLabels");
            return false;
        }
        if (p.type == "preset" && string.IsNullOrEmpty(p.presetCategory)) {
            Warn("a preset needs presetCategory");
            return false;
        }
        if (p.type == "array") {
            if (p.arrayProperties == null || (p.arrayProperties.elementProperties == null && p.arrayProperties.customElementType == null)) {
                Warn("an array needs arrayProperties with elementProperties");
                return false;
            }
            if (p.arrayProperties.elementProperties != null) {
                var valid = new List<Parameter>();
                foreach (var sub in p.arrayProperties.elementProperties) {
                    if (sub != null && sub.type != "array" && CheckParameter(sub, dir)) valid.Add(sub);
                }
                p.arrayProperties.elementProperties = valid.ToArray();
            }
        }
        //the new elements of arrays take defaultValueInArray (numbers as doubles)
        if (p.defaultValueInArray == null && p.defaultValue != null) {
            p.defaultValueInArray = p.defaultValue is long l ? (double)l : p.defaultValue;
        }
        return true;
    }

    static Dictionary<string, string> LoadStrings(string dir) {
        var lang = SettingsManager.Get("Language", "en-US");
        foreach (var name in new[] { lang, "en-US" }) {
            var file = Path.Combine(dir, "strings", name + ".json");
            if (!File.Exists(file)) continue;
            try {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(file));
            } catch (System.Exception e) {
                GameEngine.LogWarning("Invalid strings file " + file + ": " + e.Message);
            }
        }
        return null;
    }

    // Input values

    public static List<string> PresetNames(string category) {
        var res = new List<string>();
        try {
            foreach (var p in Operations.Presets.Filter(category, null)) res.Add(p.Name);
        } catch (System.Exception) {
            //no city loaded or unknown category
        }
        return res;
    }

    public static string DefaultPreset(string category) {
        var names = PresetNames(category);
        return names.Count > 0 ? names[0] : "";
    }

    public static ObjectState DefaultValues(ScriptInfo script) {
        var res = ObjectState.CreateFromDefaultProperties(script.parameters);
        foreach (var p in script.parameters) {
            if (p.type == "array" && p.defaultValueInArray is JArray arr) {
                var elements = new List<ObjectState>();
                foreach (var e in arr) {
                    if (e is JObject o) elements.Add(JsonConvert.DeserializeObject<ObjectState>(o.ToString(Formatting.None)));
                }
                res.SetArray(p.fullName(), elements.ToArray());
            }
        }
        return res;
    }

    static string PreferenceKey(ScriptInfo script) {
        return "scriptInputs:" + script.id;
    }

    //the values last used with the script in the current city, or the default ones
    public static ObjectState LoadValues(ScriptInfo script) {
        var res = DefaultValues(script);
        var saved = PreferencesManager.Get<string>(PreferenceKey(script), null);
        if (!string.IsNullOrEmpty(saved)) {
            try {
                var json = JObject.Parse(saved);
                var state = JsonConvert.DeserializeObject<ObjectState>(saved);
                foreach (var p in script.parameters) {
                    var name = p.fullName();
                    if (json[name] == null) continue;
                    state.properties.TryGetValue(name, out var value);
                    if (p.type == "array") {
                        //an empty array is not kept when the state is read
                        res.SetArray(name, value as ObjectState[] ?? new ObjectState[0]);
                    } else if (value != null) {
                        res.SetProperty(name, value);
                    }
                }
            } catch (System.Exception e) {
                GameEngine.LogWarning("Could not read the saved inputs of " + script.id + ": " + e.Message);
            }
        }
        //a preset deleted meanwhile is replaced by the default one
        foreach (var p in script.parameters) {
            if (p.type == "preset" && !PresetNames(p.presetCategory).Contains(res.Str(p.fullName()))) {
                res.SetStr(p.fullName(), DefaultPreset(p.presetCategory));
            }
        }
        return res;
    }

    public static void SaveValues(ScriptInfo script, ObjectState values) {
        if (PreferencesManager.workingDirectory == "") return;
        PreferencesManager.Set(PreferenceKey(script), JsonConvert.SerializeObject(values, Formatting.None));
    }

    //the values as given to the script
    public static JObject ToInputs(ObjectState values) {
        return JObject.Parse(JsonConvert.SerializeObject(values, Formatting.None));
    }
}

using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class StringManager {
    Dictionary<string, string> stringMap;
    Dictionary<string, string> localStringMap;
    static StringManager instance = null;
    static string defaultLang = "en-US";

    static string GetRaw(string key) {
        try {
            return GetInstance().localStringMap[key];
        } catch (System.Exception e1) {
            try {
                return GetInstance().stringMap[key];
            } catch (System.Exception e2) {
                MonoBehaviour.print(e1 + "\n\n\n" + e2 + "\n Error occurred on key: " + key);
                return key;
            }
        }
    }

    public static string Get(string key) {
        if (key == null) return null;
        var res = GetRaw(key);
        if (res == "") return null;
        return res;
    }

    public static string Get(string key, string folder) {
        if (key == null || folder == null) return null;
        string res;
        try {
            var filename = folder + "/strings/" + GetLang() + ".json";
            if (!File.Exists(filename)) return null;
            var fileContent = File.ReadAllText(filename);
            var strings = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(fileContent);
            res = strings[key];
        } catch (System.Exception e) {
            MonoBehaviour.print(e + " (" + key + ")");
            return key;
        }
        if (res == "") return key;
        return res;
    }

    static StringManager GetInstance() {
        if (instance == null) instance = new StringManager();
        return instance;
    }

    static string GetLang() {
        return SettingsManager.Get("Language", defaultLang); //TODO: language selection menu
    }

    Dictionary<string, string> GetStringMap(bool local) {
        var lang = GetLang();
        var filename = GetStringFilename(lang, local);
        if (!File.Exists(filename)) filename = GetStringFilename(defaultLang, local);
        var fileContent = File.ReadAllText(filename);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(fileContent);
    }

    StringManager() {
        stringMap = GetStringMap(false);
        localStringMap = GetStringMap(true);
    }

    public static void Reload() {
        GetInstance().Reload_instance();
    }

    void Reload_instance() {
        stringMap = GetStringMap(false);
        localStringMap = GetStringMap(true);
    }

    static string GetStringFilename(string lang, bool local) {
        return PathHelper.FilesPath(!local) + "strings/" + lang + ".json";
    }
}
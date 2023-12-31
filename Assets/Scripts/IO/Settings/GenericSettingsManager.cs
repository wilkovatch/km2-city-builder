using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class GenericSettingsManager {
    Dictionary<string, object> settings;
    string filename;

    public T Get<T>(string key, T defaultValue) {
        try {
            var res = States.Utils.DeJsonify(GetVal<object>(key));
            if (typeof(T) == typeof(int) && res is long) {
                object val = System.Convert.ToInt32(res);
                return (T)val;
            } else {
                if (typeof(T) == typeof(float) && res is double) {
                    object val = System.Convert.ToSingle(res);
                    return (T)val;
                } else {
                    return (T)res;
                }
            }
        } catch (System.Exception) {
            return defaultValue;
        }
    }

    public void Set<T>(string key, T value) {
        SetVal(key, value);
    }

    public List<T> GetList<T>(string key, List<T> defaultValue) {
        try {
            var res = GetVal<object>(key);
            if (res is List<T>) {
                return (List<T>)res;
            } else {
                return ((Newtonsoft.Json.Linq.JArray)res).ToObject<List<T>>();
            }
        } catch (System.Exception) {
            return defaultValue;
        }
    }

    public void SetList<T>(string key, List<T> value) {
        SetVal(key, value);
    }

    public void Save() {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(filename, json);
    }

    public void ChangeFilename(string filename) {
        this.filename = filename;
    }

    public GenericSettingsManager(string filename) {
        this.filename = filename;
        LoadSettings();
    }

    public void LoadSettings() {
        settings = new Dictionary<string, object>();
        if (File.Exists(filename)) {
            var content = File.ReadAllText(filename);
            settings = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(content);
        }
    }

    void SetVal<T>(string key, T value) {
        settings[key] = value;
        Save();
    }

    T GetVal<T>(string key) {
        return (T)settings[key];
    }
}
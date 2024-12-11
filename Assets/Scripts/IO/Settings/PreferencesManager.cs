using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PreferencesManager {
    static GenericSettingsManager instance = null;
    public static string workingDirectory = "";

    public static T Get<T>(string key, T defaultValue) {
        return GetInstance().Get(key, defaultValue);
    }

    public static void Set<T>(string key, T value) {
        GetInstance().Set(key, value);
    }

    public static List<T> GetList<T>(string key, List<T> defaultValue) {
        return GetInstance().GetList(key, defaultValue);
    }

    public static void SetList<T>(string key, List<T> value) {
        GetInstance().SetList(key, value);
    }

    public static void Save() {
        GetInstance().Save();
    }

    public static void Load(bool full = true) {
        GetInstance().ChangeFilename(workingDirectory + "/preferences.json");
        GetInstance().LoadSettings();
        if (full) {
            StringManager.Reload();
            PythonManager.StartServer();
            TextureImporter.ReloadTextureFormats();
            PathHelper.CreateCommonFolder();
        }
    }

    public static void Unload() {
        instance = null;
    }

    static GenericSettingsManager GetInstance() {
        if (instance == null) {
            instance = new GenericSettingsManager(workingDirectory + "/preferences.json");
            instance.LoadSettings();
        }
        return instance;
    }
}

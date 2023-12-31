using System.Collections.Generic;
using System.IO;
using UnityEngine;
using States;

static class PresetManager {
    [System.Serializable]
    public class PresetContainer {
        public Dictionary<string, ObjectState[]> presets = new Dictionary<string, ObjectState[]>();

        public ObjectState[] GetList(string type) {
            if (!presets.ContainsKey(type)) presets[type] = new ObjectState[1] { new ObjectState() };
            return presets[type];
        }

        public void SetList(string type, ObjectState[] list) {
            presets[type] = list;
        }

        public string[] GetNames(string type) {
            var list = GetList(type);
            var res = new string[list.Length];
            for (int i = 0; i < list.Length; i++) {
                res[i] = list[i].Name;
            }
            return res;
        }

        public bool DoesPresetExist(string name, string type) {
            var list = GetList(type);
            for (int i = 0; i < list.Length; i++) {
                if (list[i].Name == name) return true;
            }
            return false;
        }

        public void SavePreset(ObjectState preset, string type) {
            preset = (ObjectState)preset.Clone();
            var tmpPresets = presetList.GetList(type);
            if (PresetManager.DoesPresetExist(preset.Name, type)) {
                for (int i = 0; i < tmpPresets.Length; i++) {
                    var oldPreset = tmpPresets[i];
                    if (CompareNames(oldPreset.Name, preset.Name)) {
                        tmpPresets[i] = preset;
                        presets[type] = tmpPresets;
                        break;
                    }
                }
            } else {
                var tmpList = new List<ObjectState>(tmpPresets);
                tmpList.Add(preset);
                var defaultPreset = tmpList[0];
                tmpList.RemoveAt(0);
                tmpList.Sort();
                tmpList.Insert(0, defaultPreset);
                tmpPresets = tmpList.ToArray();
                presets[type] = tmpPresets;
            }
        }

        public void DeletePreset(string name, string type) {
            var tmpList = new List<ObjectState>(presetList.GetList(type));
            for (int i = 1; i < tmpList.Count; i++) //do not delete the first preset (the default one)
            {
                if (CompareNames(tmpList[i].Name, name)) {
                    tmpList.RemoveAt(i);
                    presets[type] = tmpList.ToArray();
                    break;
                }
            }
        }
    }

    static PresetContainer presetList;
    public static bool loaded = false;

    public static ObjectState lastIntersection = null;

    public static PresetContainer GetPresets() {
        if (!loaded) {
            if (PreferencesManager.workingDirectory != "") {
                var filename = PreferencesManager.workingDirectory + "/presets.json";
                var fileContent = File.ReadAllText(filename);
                presetList = Newtonsoft.Json.JsonConvert.DeserializeObject<PresetContainer>(fileContent);
            } else {
                presetList = new PresetContainer();
            }
            loaded = true;
            lastIntersection = null;
        }
        return presetList;
    }

    public static ObjectState GetPreset(string type, int index) {
        var res = (ObjectState)GetPresets().GetList(type)[index].Clone();
        res.FlagAsChanged();
        return res;
    }

    public static ObjectState GetFirstPresetByAllowedTypes(string type, List<string> allowedTypes) {
        if (allowedTypes == null || allowedTypes.Count == 0) return GetPreset(type, 0);
        var propElems = GetPresets().presets["propElem"];
        for (int i = 0; i < propElems.Length; i++) {
            if (allowedTypes.Contains(propElems[i].Str("type"))) return (ObjectState)propElems[i].Clone();
        }
        return new ObjectState();
    }

    //READ ONLY USAGE
    public static ObjectState GetPresetByName(string name, string type) {
        var list = GetPresets().GetList(type);
        for (int i = 0; i < list.Length; i++) {
            var elem = list[i];
            if (elem.Name == name) {
                return elem;
            }
        }
        return null;
    }

    public static ObjectState GetPresetCloneByName(string name, string type) {
        var res = (ObjectState)GetPresetByName(name, type).Clone();
        res.FlagAsChanged();
        return res;
    }

    public static void SavePresets() {
        if (PreferencesManager.workingDirectory == "") return;
        var presets = GetPresets();
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(presets, Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(PreferencesManager.workingDirectory + "/presets.json", json);
    }

    public static string[] GetNames(string type) {
        GetPresets();
        return presetList.GetNames(type);
    }

    public static bool DoesPresetExist(string name, string type) {
        GetPresets();
        return presetList.DoesPresetExist(name, type);
    }

    public static void SavePreset(ObjectState preset, string type) {
        GetPresets();
        presetList.SavePreset(preset, type);
        SavePresets();
    }

    public static void DeletePreset(string name, string type) {
        GetPresets();
        presetList.DeletePreset(name, type);
        SavePresets();
    }

    static bool CompareNames(string name1, string name2) {
        return name1.Trim().ToLower() == name2.Trim().ToLower();
    }
}
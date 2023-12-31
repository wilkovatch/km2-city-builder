using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

public abstract partial class EditorPanel {
    protected class PresetSelector: ComplexElement {
        public class DropdownTuple {
            public string current = null;
            public string lastPreset = null;
            public int lastPresetCount = -1;
            public string lastFirstPreset = ""; //to check if it changed after the first loading if the new list only has 1 preset
            public bool reloadLast;
            public bool loadAndSave;
            public EditorPanelElements.Dropdown dropdown;
            public EditorPanelElements.Button resetButton = null;
            public EditorPanelElements.Button syncButton = null;
            public EditorPanelElements.Button unlinkButton = null;
            public EditorPanelElements.Button deleteButton = null;
            public System.Func<ObjectState> stateGetter = null;
            public List<string> allowedTypes = null;

            public DropdownTuple(EditorPanelElements.Dropdown dropdown, bool loadAndSave, bool reloadLast, List<string> allowedTypes) {
                this.dropdown = dropdown;
                this.loadAndSave = loadAndSave;
                this.reloadLast = reloadLast;
                this.allowedTypes = allowedTypes;
            }

            public static List<DropdownTuple> DropdownTupleList(EditorPanelElements.Dropdown dropdown, bool loadAndSave, bool reloadLast, List<string> allowedTypes) {
                var res = new List<DropdownTuple>();
                res.Add(new DropdownTuple(dropdown, loadAndSave, reloadLast, allowedTypes));
                return res;
            }
        }

        public Dictionary<string, List<DropdownTuple>> dropdowns = new Dictionary<string, List<DropdownTuple>>();
        bool loadOnChange = true;

        public PresetSelector(EditorPanel panel): base(panel) {}

        public override void Destroy() {
            dropdowns.Clear();
        }

        public void AddPresetLoadDropdown(EditorPanelPage p, string preset, bool reloadLast, string type,
            System.Func<ObjectState, ObjectState> setter, List<string> allowedTypes = null, System.Action post = null, float width = 1.5f) {

            var presetList = new List<string> { SM.Get("SELECT_VALUE_TO_LOAD_PRESET") };
            presetList.AddRange(FilterPresetList(allowedTypes, type, PresetManager.GetNames(type)));
            var index = dropdowns.ContainsKey(type) ? dropdowns[type].Count : 0;
            var dropdown = p.AddDropdown(preset, presetList, x => { if (loadOnChange) { LoadPreset(x, type, setter, index); post?.Invoke(); } }, width);
            if (!dropdowns.ContainsKey(type)) {
                dropdowns.Add(type, DropdownTuple.DropdownTupleList(dropdown, false, reloadLast, allowedTypes));
            } else {
                dropdowns[type].Add(new DropdownTuple(dropdown, false, reloadLast, allowedTypes));
            }
            p.IncreaseRow();
        }

        public override void BaseExtraAction() {
            loadOnChange = false;
            foreach (var key in dropdowns.Keys) {
                var dropdownList = dropdowns[key];
                foreach (var dropdown in dropdownList) {
                    if (dropdown.stateGetter == null) continue;
                    var state = dropdown.stateGetter.Invoke();
                    var curPreset = PresetManager.GetPresetByName(state?.Name, key);
                    var same = state != null && state.Equals(curPreset);
                    var hasName = state != null && state.Name != "";
                    dropdown.unlinkButton?.SetInteractable(hasName);
                    var list = FilterPresets(dropdown.allowedTypes, key, PresetManager.GetPresets().GetList(key));
                    int presetIndex = PresetIndexInList(curPreset?.Name, list) + 1;
                    dropdown.resetButton?.SetInteractable(!same && hasName && presetIndex > 0);
                    dropdown.syncButton?.SetInteractable(same && hasName);
                    dropdown.deleteButton?.SetInteractable(hasName && presetIndex > 1);
                    dropdown.dropdown.actionEnabled = false;
                    dropdown.dropdown.SetValue(presetIndex);
                    dropdown.dropdown.actionEnabled = true;
                    dropdown.current = curPreset?.Name;
                }
            }
            loadOnChange = true;
        }

        public void AddPresetLoadAndSaveDropdown(EditorPanelPage p, string preset, bool reloadLast, string type,
            System.Func<ObjectState, ObjectState> setter, System.Func<ObjectState> stateGetter, bool subPreset,
            System.Action<string> nameSetter = null, List<string> allowedTypes = null, System.Action post = null, float width = 1.5f) {

            var editMode = EditingPreset();
            AddPresetLoadDropdown(p, preset, reloadLast, type, setter, allowedTypes, post);
            var index = dropdowns.ContainsKey(type) ? (dropdowns[type].Count - 1) : 0;
            EditorPanelElements.Button presetSyncButton = null, deleteButton = null, presetUnlinkButton = null;
            var divider = 1.0f + (subPreset ? 0.0f : 1.0f) + (editMode ? 0.0f : 1.0f);
            if (!subPreset) {
                presetSyncButton = p.AddButton(SM.Get("SYNC_PRESET"), delegate { SyncPresetAlert(stateGetter.Invoke().Name, type); }, width / divider);
                presetSyncButton.SetInteractable(false);
            }
            var presetResetButton = p.AddButton(SM.Get("RESET_PRESET"), delegate { ReloadPreset(stateGetter, type, setter, index); post?.Invoke(); }, width / divider);
            presetResetButton.SetInteractable(false);
            if (!editMode) {
                presetUnlinkButton = p.AddButton(SM.Get("UNLINK_PRESET"), delegate { if (nameSetter != null) { nameSetter.Invoke(""); } else { stateGetter.Invoke().Name = ""; }; panel.ReadCurValues(); }, width / divider);
                presetUnlinkButton.SetInteractable(false);
            }
            p.IncreaseRow();
            divider = editMode ? 2.0f : 1.0f;
            if (editMode) {
                deleteButton = p.AddButton(SM.Get("DELETE_PRESET"), delegate { Delete(type, index); }, width / divider);
                deleteButton.SetInteractable(false);
            }
            p.AddButton(SM.Get("SAVE_PRESET"), delegate { SaveNewGenericPreset(panel.builder, x => { SavePreset(x, type, stateGetter, nameSetter); }, type, stateGetter()?.Name); }, width / divider);
            if (dropdowns.ContainsKey(type)) {
                dropdowns[type][index].resetButton = presetResetButton;
                dropdowns[type][index].syncButton = presetSyncButton;
                dropdowns[type][index].deleteButton = deleteButton;
                dropdowns[type][index].unlinkButton = presetUnlinkButton;
                dropdowns[type][index].stateGetter = stateGetter;
            }
            p.IncreaseRow();
        }

        List<string> FilterPresetList(List<string> allowedTypes, string key, string[] list) {
            var actualValues = new List<string>();
            if (allowedTypes != null && allowedTypes.Count > 0) {
                foreach (var value in list) {
                    var preset = PresetManager.GetPresetByName(value, key);
                    if (allowedTypes.Contains(preset.Str("type"))) actualValues.Add(value);
                }
            } else {
                actualValues.AddRange(list);
            }
            return actualValues;
        }

        List<ObjectState> FilterPresets(List<string> allowedTypes, string key, ObjectState[] list) {
            var actualValues = new List<ObjectState>();
            if (allowedTypes != null && allowedTypes.Count > 0) {
                foreach (var value in list) {
                    var preset = PresetManager.GetPresetByName(value.Name, key);
                    if (allowedTypes.Contains(preset.Str("type"))) actualValues.Add(value);
                }
            } else {
                actualValues.AddRange(list);
            }
            return actualValues;
        }

        protected void ReloadValues(string key, bool load) {
            var rawValues = PresetManager.GetNames(key);
            foreach (var dropdown in dropdowns[key]) {
                var presets = new List<string> { load ? SM.Get("SELECT_VALUE_TO_LOAD_PRESET") : SM.Get("SELECT_PRESET") };
                presets.AddRange(FilterPresetList(dropdown.allowedTypes, key, rawValues));
                dropdown.dropdown.SetOptions(presets);
                dropdown.dropdown.actionEnabled = false;
                dropdown.dropdown.SetValue(0);
                dropdown.dropdown.actionEnabled = true;
                dropdown.lastPresetCount = presets.Count - 1;
                dropdown.lastFirstPreset = presets.Count > 1 ? presets[1] : "";
            }
            BaseExtraAction();
        }

        public override void SetActive(bool active) {
            if (active) {
                foreach (var key in dropdowns.Keys) {
                    foreach (var dropdown in dropdowns[key]) {
                        ReloadValues(key, dropdown.loadAndSave);
                    }
                }
                BaseExtraAction();
            }
        }

        public override void ReadCurValues() {
            BaseExtraAction();
        }

        public void ReloadValues() {
            foreach (var key in dropdowns.Keys) {
                ReloadValues(key, false);
            }
        }

        int PresetIndexInList(string preset, List<ObjectState> list) {
            if (preset == null) return -1;
            for (int i = 0; i < list.Count; i++) {
                if (preset == list[i].Name) return i;
            }
            return -1;
        }

        public void LoadPreset(int preset, string type, System.Func<ObjectState, ObjectState> setter, int index) {
            var list = FilterPresets(dropdowns[type][index].allowedTypes, type, PresetManager.GetPresets().GetList(type));
            if (preset > 0) {
                dropdowns[type][index].lastPreset = list[preset - 1].Name;
                dropdowns[type][index].current = list[preset - 1].Name;
                if (setter != null) {
                    var stateClone = (ObjectState)list[preset - 1].Clone();
                    stateClone.FlagAsChanged();
                    var res = setter.Invoke(stateClone);
                    panel.SyncState(res);
                    panel.builder.NotifyChange();
                }
                panel.ReadCurValues();
            } else {
                dropdowns[type][index].dropdown.SetValue(0);
            }
        }

        public void LoadPreset(string preset, string type, System.Func<ObjectState, ObjectState> setter, int index) {
            var list = FilterPresets(dropdowns[type][index].allowedTypes, type, PresetManager.GetPresets().GetList(type));
            LoadPreset(PresetIndexInList(preset, list) + 1, type, setter, index);
        }

        protected void ReloadPreset(System.Func<ObjectState> stateGetter, string type, System.Func<ObjectState, ObjectState> setter, int index) {
            LoadPreset(stateGetter.Invoke().Name, type, setter, index);
        }

        void SavePreset(string str, string type, System.Func<ObjectState> getter, System.Action<string> nameSetter) {
            if (nameSetter != null) {
                nameSetter.Invoke(str);
            } else {
                var origPreset = getter.Invoke();
                origPreset.Name = str;
            }
            var preset = (ObjectState)getter.Invoke().Clone();
            PresetManager.SavePreset(preset, type);
            ReloadValues(type, true);
            panel.ReadCurValues();
        }

        void Delete(string type, int i) {
            var testA = dropdowns[type];
            var testB = testA[i];
            string preset = dropdowns[type][i].current;
            if (preset == null) {
                panel.builder.CreateAlert(SM.Get("ERROR"), SM.Get("NO_PRESET_SELECTED_ERROR"), SM.Get("OK"));
            } else if (preset == "Default") {
                panel.builder.CreateAlert(SM.Get("ERROR"), SM.Get("CANNOT_DELETE_DEFAULT_PRESET_ERROR"), SM.Get("OK"));
            } else {
                panel.builder.CreateAlert(SM.Get("WARNING"), SM.Get("DELETE_PRESET_WARNING"), SM.Get("YES"), SM.Get("NO"), delegate { DeletePreset(preset, type); });
            }
        }

        void DeletePreset(string curPreset, string type) {
            PresetManager.DeletePreset(curPreset, type);
            ClearPreset(curPreset, type);
            ReloadValues();
        }

        protected void SaveNewGenericPreset(CityBuilderMenuBar builder, System.Action<string> saveAction, string type, string initialText) {
            builder.CreateInput(SM.Get("PRESET_NAME_INPUT_TITLE"), SM.Get("PRESET_NAME_INPUT_PH"), SM.Get("SAVE"), SM.Get("CANCEL"), str => { ValidateGenericPresetName(builder, str, saveAction, type, initialText); }, null, initialText);
        }

        void ValidateGenericPresetName(CityBuilderMenuBar builder, string str, System.Action<string> saveAction, string type, string initialText) {
            if (str == null || str.Trim() == "") {
                builder.CreateAlert(SM.Get("PRESET_NAME_EMPTY_ERROR_TITLE"), SM.Get("PRESET_NAME_EMPTY_ERROR"), SM.Get("OK"), delegate { SaveNewGenericPreset(builder, saveAction, type, initialText); });
                return;
            }
            var exists = PresetManager.DoesPresetExist(str, type);
            if (exists) {
                builder.CreateAlert(SM.Get("PRESET_OVERWRITE_WARNING_TITLE"), SM.Get("PRESET_OVERWRITE_WARNING"), SM.Get("YES"), SM.Get("NO"), delegate { saveAction(str); }, delegate { SaveNewGenericPreset(builder, saveAction, type, initialText); });
            } else {
                saveAction(str);
            }
        }

        void SyncPresetAlert(string name, string type) {
            panel.builder.CreateAlert(SM.Get("WARNING"), SM.Get("PRESET_SYNC_WARNING"), SM.Get("YES"), SM.Get("NO"), delegate { SyncPreset(name, type); });
        }

        void SyncPreset(string name, string type) {
            var curPreset = PresetManager.GetPresetByName(name, type);
            switch (type) {
                case "road":
                    foreach (var road in panel.builder.helper.elementManager.roads) {
                        if (road.state.Name == name) {
                            var newState = (ObjectState)curPreset.Clone();
                            newState.FlagAsChanged();
                            road.SetState(newState);
                        }
                    }
                    break;
                    //TODO: other cases
            }
            panel.builder.NotifyChange();
        }

        void ClearPreset(string name, string type) {
            switch (type) {
                case "road":
                    foreach (var road in panel.builder.helper.elementManager.roads) {
                        if (road.state.Name == name) road.state.Name = "";
                    }
                    break;
                    //TODO: other cases
            }
        }

        public bool EditingPreset() {
            var modifier = panel.builder.terrainClick.modifier;
            return (modifier != null && modifier is TerrainModifier.Null);
        }

        public override void SyncState(ObjectState state) {

        }
    }
}
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SM = StringManager;
using RPB = EditorPanels.Helpers.RandomBuildingPresetsManager;

namespace EditorPanels {
    public class WholeCityBuildingsEditorPanel : EditorPanel {
        EditorPanelElements.ScrollList loopPresetList;
        EditorPanelElements.InputField minLoopLengthField, maxLoopLengthField;

        int curLoopPresetI = -1;
        ObjectState curLoopState = null;
        ObjectState curLineState = null;
        bool nullOnly = false;

        public WholeCityBuildingsEditorPanel() {
            AddComplexElement(new PresetSelector(this));
        }

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);

            var pS = GetComplexElement<PresetSelector>();
            pS.AddPresetLoadDropdown(p0, SM.Get("BUILDING_LINE_PRESET"), true, "buildingLine", null, null, SetCurrentLineState);
            pS.AddPresetLoadDropdown(p0, SM.Get("BUILDING_PRESET"), true, "building", null, null, SetCurrentLoopState);
            loopPresetList = p0.AddScrollList(SM.Get("BL_LOOP_PRESET_LIST_TITLE"), new List<string>(), x => curLoopPresetI = x, 1.5f, SM.Get("BL_PRESET_LIST_TOOLTIP"));
            p0.IncreaseRow(5.0f);
            p0.AddButton(SM.Get("BL_DELETE_LOOP_PRESET"), DeleteLoopPreset, 0.75f);
            p0.AddButton(SM.Get("BL_ADD_LOOP_PRESET"), AddLoopPreset, 0.75f);
            p0.IncreaseRow();

            maxLoopLengthField = p0.AddInputField(SM.Get("BL_LOOP_MAX_LENGTH"), "length", "15", UnityEngine.UI.InputField.ContentType.DecimalNumber, x => RPB.maxLoopLength = float.Parse(x), 0.75f);
            minLoopLengthField = p0.AddInputField(SM.Get("BL_LOOP_MIN_LENGTH"), "length", "15", UnityEngine.UI.InputField.ContentType.DecimalNumber, x => RPB.minLoopLength = float.Parse(x), 0.75f);
            p0.IncreaseRow();
            p0.AddCheckbox(SM.Get("BL_LOOP_SUBDIVIDE"), true, SetSubdivide, 1.5f);
            p0.IncreaseRow();
            p0.AddCheckbox(SM.Get("CB_NULL_ONLY"), false, x => nullOnly = x, 1.5f);
            p0.IncreaseRow();

            p0.AddButton(SM.Get("CB_CREATE"), CreateCityBuildings, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CB_ERASE"), EraseCityBuildings, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CANCEL"), Cancel, 1.5f);
            p0.IncreaseRow();
        }

        void DeleteLoopPreset() {
            if (curLoopPresetI == -1) return;
            RPB.loopPresets.RemoveAt(curLoopPresetI);
            ReloadLoopPresetsList(true);
        }

        void AddLoopPreset() {
            if (curLoopState == null) return;
            RPB.loopPresets.Add(curLoopState);
            ReloadLoopPresetsList(true);
        }

        void ReloadLoopPresetsList(bool deselect) {
            loopPresetList.Deselect();
            var items = new List<string>();
            for (int i = 0; i < RPB.loopPresets.Count; i++) {
                items.Add(RPB.loopPresets[i].Name);
            }
            loopPresetList.SetItems(items);
            if (deselect) curLoopPresetI = -1;
        }

        void SetCurrentLoopState() {
            var pS = GetComplexElement<PresetSelector>();
            var state = PresetManager.GetPresetCloneByName(pS.dropdowns["building"][0].current, "building");
            curLoopState = state;
        }

        void SetCurrentLineState() {
            var pS = GetComplexElement<PresetSelector>();
            var state = PresetManager.GetPresetCloneByName(pS.dropdowns["buildingLine"][0].current, "buildingLine");
            curLineState = state;
        }

        void SetSubdivide(bool input) {
            RPB.subdivide = input;
            minLoopLengthField.SetInteractable(input);
            maxLoopLengthField.SetInteractable(input);
        }

        public override void SetActive(bool active) {
            if (active) {
                ReloadLoopPresetsList(true);
            }
            base.SetActive(active);
        }

        void Cancel() {
            SetActive(false);
        }

        void CreateCityBuildings() {
            if (curLineState == null) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("CB_NEED_LINE_PRESET"), SM.Get("OK"));
                return;
            }
            if (RPB.loopPresets.Count == 0) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("BL_NEED_PRESETS_FOR_AUTOCLOSE"), SM.Get("OK"));
                return;
            }
            builder.helper.elementManager.CreateCityBuildings(builder.gameObject, RPB.minLoopLength, RPB.maxLoopLength, RPB.loopPresets, RPB.subdivide, curLineState, nullOnly ? new List<string>() { "" } : null);
        }

        void EraseCityBuildings() {
            builder.CreateAlert(SM.Get("WARNING"), SM.Get("CB_ERASE_WARNING"), SM.Get("YES"), SM.Get("NO"), builder.helper.elementManager.EraseCityBuildings);
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GameObject = Godot.Control;
using SM = StringManager;
using RPB = EditorPanels.Helpers.RandomBuildingPresetsManager;

namespace EditorPanels {
    public class WholeCityBuildingsEditorPanel : EditorPanel {
        EditorPanelElements.ScrollList loopPresetList;
        EditorPanelElements.InputField minLoopLengthField, maxLoopLengthField;

        int curLoopPresetI = -1;
        bool nullOnly = false;

        public WholeCityBuildingsEditorPanel() {
            AddComplexElement(new PresetSelector(this));
        }

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);

            var pS = GetComplexElement<PresetSelector>();
            pS.AddPresetLoadDropdown(p0, SM.Get("BUILDING_LINE_PRESET"), true, "buildingLine", null);
            pS.AddPresetLoadDropdown(p0, SM.Get("BUILDING_PRESET"), true, "building", null);
            loopPresetList = p0.AddScrollList(SM.Get("BL_LOOP_PRESET_LIST_TITLE"), new List<string>(), x => curLoopPresetI = x, 1.5f, SM.Get("BL_PRESET_LIST_TOOLTIP"));
            p0.IncreaseRow(5.0f);
            p0.AddButton(SM.Get("BL_DELETE_LOOP_PRESET"), DeleteLoopPreset, 0.75f);
            p0.AddButton(SM.Get("BL_ADD_LOOP_PRESET"), AddLoopPreset, 0.75f);
            p0.IncreaseRow();

            maxLoopLengthField = p0.AddInputField(SM.Get("BL_LOOP_MAX_LENGTH"), "length", "15", GameEngine.InputContentType.Decimal, x => RPB.maxLoopLength = float.Parse(x), 0.75f);
            minLoopLengthField = p0.AddInputField(SM.Get("BL_LOOP_MIN_LENGTH"), "length", "15", GameEngine.InputContentType.Decimal, x => RPB.minLoopLength = float.Parse(x), 0.75f);
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
            var preset = GetComplexElement<PresetSelector>().dropdowns["building"][0].current;
            if (preset == null) return;
            RPB.loopPresets.Add(preset);
            ReloadLoopPresetsList(true);
        }

        void ReloadLoopPresetsList(bool deselect) {
            loopPresetList.Deselect();
            RPB.RemoveMissing();
            loopPresetList.SetItems(new List<string>(RPB.loopPresets));
            if (deselect) curLoopPresetI = -1;
        }

        void SetSubdivide(bool input) {
            RPB.subdivide = input;
            minLoopLengthField.SetInteractable(input);
            maxLoopLengthField.SetInteractable(input);
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            if (active) {
                ReloadLoopPresetsList(true);
            }
            base.SetActive(active);
        }

        void Cancel() {
            SetActive(false);
        }

        void CreateCityBuildings() {
            var lineState = Operations.Presets.Get("buildingLine", GetComplexElement<PresetSelector>().dropdowns["buildingLine"][0].current);
            if (lineState == null) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("CB_NEED_LINE_PRESET"), SM.Get("OK"));
                return;
            }
            var loopPresets = RPB.GetLoopPresetStates();
            ReloadLoopPresetsList(true);
            if (loopPresets.Count == 0) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("BL_NEED_PRESETS_FOR_AUTOCLOSE"), SM.Get("OK"));
                return;
            }
            _ = builder.helper.elementManager.CreateCityBuildings(RPB.minLoopLength, RPB.maxLoopLength, loopPresets, RPB.subdivide, lineState, nullOnly ? new List<string>() { "" } : null);
        }

        void EraseCityBuildings() {
            builder.CreateAlert(SM.Get("WARNING"), SM.Get("CB_ERASE_WARNING"), SM.Get("YES"), SM.Get("NO"), () => History.UndoManager.Record(SM.Get("CB_ERASE"), builder.helper.elementManager.EraseCityBuildings)); //the alert action runs outside the button's undo step
        }
    }
}
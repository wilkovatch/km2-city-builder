using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    namespace Buildings {
        public class FloorEditorPanel : EditorPanel {
            public BuildingSideGenerator curSide = null;
            public int curFloor;
            int curSlice;
            EditorPanelElements.ScrollList sliceList;
            EditorPanelElements.Button delSliceBtn;
            EditorPanelElements.InputField repetitionsField, widthField, heightField;
            bool groundMode = false;
            public List<string> curBlockNames = new List<string>();
            EditorPanelElements.Dropdown blocksDropdown, topDropdown, bottomDropdown;

            public FloorEditorPanel() {
                AddComplexElement(new PresetSelector(this));
            }

            public override void Initialize(GameObject canvas) {
                Initialize(canvas, 1);
                var p0 = GetPage(0);

                p0.AddButton(SM.Get("END_EDITING"), Terminate, 0.75f);
                p0.AddButton(SM.Get("BS_FB_GO_UP"), GoUp, 0.75f);
                p0.IncreaseRow();

                var floorIdxGet = new Dictionary<string, System.Func<string>>() { { "floor", delegate { return curFloor.ToString(); } }, { "slice", delegate { return curSlice.ToString(); } } };
                heightField = p0.AddFieldInputField(SM.Get("BS_F_HEIGHT"), SM.Get("BS_F_HEIGHT_PH"), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetSide, "State.properties.upperFloors.$floor.properties.height", floorIdxGet, 1.5f, SM.Get("BS_F_GROUND_HEIGHT_TOOLTIP"));
                p0.IncreaseRow();

                sliceList = p0.AddScrollList(SM.Get("BS_F_LIST_TITLE"), new List<string>(), SelectSlice, 1.5f, SM.Get("BS_F_LIST_TOOLTIP"));
                p0.IncreaseRow(5.0f);

                delSliceBtn = p0.AddButton(SM.Get("BS_F_DELETE_SLICE"), DeleteSlice, 0.75f);
                p0.AddButton(SM.Get("BS_F_ADD_SLICE"), AddSlice, 0.75f);
                p0.IncreaseRow();

                p0.AddLabel(SM.Get("BS_F_SLICE_LABEL"), 1.5f);
                p0.IncreaseRow();
                blocksDropdown = p0.AddFieldDropdown(SM.Get("BS_F_BLOCK"), new List<string>(), GetSide, "State.properties.upperFloors.$floor.properties.Slices.$slice.properties.block", floorIdxGet, 1.5f);
                p0.IncreaseRow();
                topDropdown = p0.AddFieldDropdown(SM.Get("BS_F_BLOCK_T"), new List<string>(), GetSide, "State.properties.groundFloor.properties.Slices.$slice.properties.top", floorIdxGet, 0.75f, SM.Get("BS_F_GROUND_BLOCK_TOOLTIP"));
                bottomDropdown = p0.AddFieldDropdown(SM.Get("BS_F_BLOCK_B"), new List<string>(), GetSide, "State.properties.groundFloor.properties.Slices.$slice.properties.bottom", floorIdxGet, 0.75f, SM.Get("BS_F_GROUND_BLOCK_TOOLTIP"));
                p0.IncreaseRow();
                repetitionsField = p0.AddFieldInputField(SM.Get("BS_F_REPETITIONS"), SM.Get("BS_F_REPETITIONS_PH"), UnityEngine.UI.InputField.ContentType.IntegerNumber, GetSide, "State.properties.upperFloors.$floor.properties.Slices.$slice.properties.Repetitions", floorIdxGet, 0.75f);
                widthField = p0.AddFieldInputField(SM.Get("BS_F_WIDTH"), SM.Get("BS_F_WIDTH_PH"), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetSide, "State.properties.upperFloors.$floor.properties.Slices.$slice.properties.Width", floorIdxGet, 0.75f);
                p0.IncreaseRow();
            }

            void UnsetGetters() {
                var p0 = GetPage(0);
                p0.UpdateFieldName(widthField, "");
                p0.UpdateFieldName(repetitionsField, "");
                p0.UpdateFieldName(blocksDropdown, "");
                p0.UpdateFieldName(topDropdown, "");
                p0.UpdateFieldName(bottomDropdown, "");
                widthField.SetValue("");
                repetitionsField.SetValue("");
            }

            public void SetMode(bool ground) {
                var p0 = GetPage(0);
                groundMode = ground;
                if (ground) {
                    p0.UpdateFieldName(heightField, "State.properties.groundFloor.properties.minHeight");
                    p0.UpdateFieldName(widthField, "State.properties.groundFloor.properties.Slices.$slice.properties.Width");
                    p0.UpdateFieldName(repetitionsField, "State.properties.groundFloor.properties.Slices.$slice.properties.Repetitions");
                    p0.UpdateFieldName(blocksDropdown, "State.properties.groundFloor.properties.Slices.$slice.properties.middle");
                    p0.UpdateFieldName(topDropdown, "State.properties.groundFloor.properties.Slices.$slice.properties.top");
                    p0.UpdateFieldName(bottomDropdown, "State.properties.groundFloor.properties.Slices.$slice.properties.bottom");
                } else {
                    p0.UpdateFieldName(heightField, "State.properties.upperFloors.$floor.properties.height");
                    p0.UpdateFieldName(widthField, "State.properties.upperFloors.$floor.properties.Slices.$slice.properties.Width");
                    p0.UpdateFieldName(repetitionsField, "State.properties.upperFloors.$floor.properties.Slices.$slice.properties.Repetitions");
                    p0.UpdateFieldName(blocksDropdown, "State.properties.upperFloors.$floor.properties.Slices.$slice.properties.block");
                    p0.UpdateFieldName(topDropdown, "");
                    p0.UpdateFieldName(bottomDropdown, "");
                }
            }

            void SelectSlice(int i) {
                curSlice = i;
                delSliceBtn.SetInteractable(i > -1);
                repetitionsField.SetInteractable(i > -1);
                widthField.SetInteractable(i > -1);
                if (i > -1) {
                    blocksDropdown.SetOptions(curBlockNames);
                    if (groundMode) {
                        topDropdown.SetOptions(curBlockNames);
                        bottomDropdown.SetOptions(curBlockNames);
                    } else {
                        topDropdown.SetOptions(new List<string>());
                        bottomDropdown.SetOptions(new List<string>());
                    }
                } else {
                    blocksDropdown.SetOptions(new List<string>());
                    topDropdown.SetOptions(new List<string>());
                    bottomDropdown.SetOptions(new List<string>());
                }
                blocksDropdown.SetInteractable(i > -1);
                topDropdown.SetInteractable(i > -1 && groundMode);
                bottomDropdown.SetInteractable(i > -1 && groundMode);
                if (i == -1) {
                    UnsetGetters();
                } else {
                    SetMode(groundMode);
                }
                ReadCurValues();
            }

            void DeleteSlice() {
                if (curSide == null || curSlice < 0) return;
                if (groundMode) {
                    var groundFloorState = curSide.state.State("groundFloor");
                    var slices = groundFloorState.Array<ObjectState>("Slices", false);
                    if (curSlice > slices.Length - 1) return;
                    var newSlices = new List<ObjectState>();
                    newSlices.AddRange(slices);
                    newSlices.RemoveAt(curSlice);
                    groundFloorState.SetArray("Slices", newSlices.ToArray());
                    curSide.SyncWithBuilding();
                } else {
                    var curFloorState = curSide.state.Array<ObjectState>("upperFloors")[curFloor];
                    var slices = curFloorState.Array<ObjectState>("Slices", false);
                    if (curSlice > slices.Length - 1) return;
                    var newSlices = new List<ObjectState>();
                    newSlices.AddRange(slices);
                    newSlices.RemoveAt(curSlice);
                    curFloorState.SetArray("Slices", newSlices.ToArray());
                    curSide.SyncWithBuilding();
                }
                ReloadSlices(true);
                builder.NotifyChange();
            }

            void ReloadSlices(bool deselect) {
                if (GetSide() == null) return;
                sliceList.Deselect();
                var slices = new List<string>();
                if (groundMode) {
                    var sliceArray = GetSide().state.State("groundFloor").Array<ObjectState>("Slices", false);
                    for (int i = 0; i < sliceArray.Length; i++) {
                        slices.Add("Slice " + (i + 1).ToString());
                    }
                } else {
                    var upperFloors = GetSide().state.Array<ObjectState>("upperFloors");
                    curFloor = Mathf.Min(upperFloors.Length - 1, curFloor); //should not happen
                    var sliceArray = upperFloors[curFloor].Array<ObjectState>("Slices", false);
                    for (int i = 0; i < sliceArray.Length; i++) {
                        slices.Add("Slice " + (i + 1).ToString());
                    }
                }
                sliceList.SetItems(slices);
                if (deselect) SelectSlice(-1);
            }

            void AddSlice() {
                if (curSide == null) return;
                if (groundMode) {
                    var groundFloorState = curSide.state.State("groundFloor");
                    var slices = groundFloorState.Array<ObjectState>("Slices", false);
                    var f = new ObjectState();
                    f.SetInt("Repetitions", 1);
                    f.SetFloat("Width", 1);
                    f.SetInt("top", 0);
                    f.SetInt("middle", 0);
                    f.SetInt("bottom", 0);
                    var newSlices = new List<ObjectState>();
                    newSlices.AddRange(slices);
                    newSlices.Add(f);
                    groundFloorState.SetArray("Slices", newSlices.ToArray());
                    curSide.SyncWithBuilding();
                } else {
                    var curFloorState = curSide.state.Array<ObjectState>("upperFloors")[curFloor];
                    var slices = curFloorState.Array<ObjectState>("Slices", false);
                    var f = new ObjectState();
                    f.SetInt("Repetitions", 1);
                    f.SetFloat("Width", 1);
                    f.SetInt("block", 0);
                    var newSlices = new List<ObjectState>();
                    newSlices.AddRange(slices);
                    newSlices.Add(f);
                    curFloorState.SetArray("Slices", newSlices.ToArray());
                    curSide.SyncWithBuilding();
                }
                ReloadSlices(true);
                builder.NotifyChange();
            }

            public override void Terminate() {
                SetActive(false);
                parentPanel.Terminate();
            }

            public override void SetActive(bool active) {
                if (curSide != null) {
                    curSide.SetOutline(active);
                    ReloadSlices(true);
                }
                base.SetActive(active);
            }

            BuildingSideGenerator GetSide() {
                return curSide;
            }
        }
    }
}
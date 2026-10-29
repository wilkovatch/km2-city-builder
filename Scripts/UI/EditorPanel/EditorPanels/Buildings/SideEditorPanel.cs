using System;
using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;

namespace EditorPanels {
    namespace Buildings {
        public class SideEditorPanel : EditorPanel {
            public BuildingSideGenerator curSide = null;
            public BlockEditorPanel blockEditor;
            public FloorEditorPanel floorEditor;
            EditorPanelElements.Button groundBtn, delFloorBtn, editFloorBtn, delBlockBtn, editBlockBtn;
            EditorPanelElements.ScrollList floorList, blockList;
            int curFloorI = -1;
            int curBlockI = -1;
            List<string> curBlockNames = new List<string>();

            public SideEditorPanel() {
                AddComplexElement(new PresetSelector(this));
            }

            public override void Initialize(GameObject canvas) {
                var line = ((LineEditorPanel)((BuildingEditorPanel)parentPanel).parentPanel).GetLine();
                if (line == null) return;
                var type = line.GetLineType().name;
                InitializeWithCustomParameters<CityElements.Types.Runtime.Buildings.BuildingType.Side, CityElements.Types.Buildings.BuildingSideType>(canvas, GetSide, null,
                    type, CityElements.Types.Parsers.TypeParser.GetBuildingSideTypes, ProcessCustomParts, true, 1.5f, false);

                floorEditor = AddChildPanel<FloorEditorPanel>(canvas);
                blockEditor = AddChildPanel<BlockEditorPanel>(canvas);
            }

            bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
                TypeSelector<CityElements.Types.Runtime.Buildings.BuildingType.Side> tS, CityElements.Types.Runtime.Buildings.BuildingType.Side type) {

                var w = elem.width;
                if (elem.name == "mainGroup") {
                    p.AddButton(SM.Get("END_EDITING"), Terminate, 0.5f * w);
                    p.AddButton(SM.Get("BS_GO_UP"), GoUp, 0.5f * w);
                    p.IncreaseRow();

                    Func<ObjectState> getter = delegate { return GetSide().State; };
                    Func<ObjectState, ObjectState> setter = x => { GetSide().State = (ObjectState)x.Clone(); return GetSide().state; };
                    pS.AddPresetLoadAndSaveDropdown(p, SM.Get("BUILDING_SIDE_PRESET"), true, "buildingSide", setter, getter, false, null, null, ReloadLists);

                    p.AddFieldCheckbox(SM.Get("BLDG_ENABLED"), GetSide, "State.properties.hasGroundFloor", null, w / 3, null, null, x => { groundBtn.SetInteractable(x); });
                    groundBtn = p.AddButton(SM.Get("BS_EDIT_GROUND"), EditGroundFloor, 2 * w / 3);
                    p.IncreaseRow();

                    p.AddFieldInputField(SM.Get("BS_SIDE_LOW_FLOORS"), SM.Get("BS_SIDE_LOW_FLOORS_PH"), GameEngine.InputContentType.Integer, GetSide, "State.properties.lowUpperFloorsCount", null, w * 0.5f);
                    p.AddFieldInputField(SM.Get("BS_SIDE_HIGH_FLOORS"), SM.Get("BS_SIDE_HIGH_FLOORS_PH"), GameEngine.InputContentType.Integer, GetSide, "State.properties.highUpperFloorsCount", null, w * 0.5f);
                    p.IncreaseRow();

                    floorList = p.AddScrollList(SM.Get("BS_FLOOR_LIST_TITLE"), new List<string>(), SelectFloor, w, SM.Get("BS_FLOOR_LIST_TOOLTIP"));
                    p.IncreaseRow(5.0f);

                    editFloorBtn = p.AddButton(SM.Get("BS_EDIT_FLOOR"), EditFloor, w / 3);
                    delFloorBtn = p.AddButton(SM.Get("BS_DELETE_FLOOR"), DeleteFloor, w / 3);
                    p.AddButton(SM.Get("BS_ADD_FLOOR"), AddFloor, w / 3);
                    p.IncreaseRow();
                } else if (elem.name == "blocksGroup") {
                    blockList = p.AddScrollList(SM.Get("BS_BLOCK_LIST_TITLE"), new List<string>(), new List<string>(), SelectBlock, w, SM.Get("BS_BLOCK_LIST_TOOLTIP"));
                    p.IncreaseRow(5.0f);

                    editBlockBtn = p.AddButton(SM.Get("BS_EDIT_BLOCK"), EditBlock, w / 3);
                    delBlockBtn = p.AddButton(SM.Get("BS_DELETE_BLOCK"), DeleteBlock, w / 3);
                    p.AddButton(SM.Get("BS_ADD_BLOCK"), AddBlock, w / 3);
                    p.IncreaseRow();
                } else {
                    return false;
                }
                return true;
            }

            public override void SetActive(bool active) {
                if (!PrepareActivation(active)) return;
                var pS = GetComplexElement<PresetSelector>();
                if (active) {
                    Func<ObjectState, ObjectState> setter = x => { Operations.BuildingLines.SetSideState(GetSide(), x); return GetSide().State; };
                    pS.LoadPreset(GetSide() == null ? pS.dropdowns["buildingSide"][0].lastPreset : null, "buildingSide", setter, 0);
                }
                if (curSide != null) {
                    curSide.SetOutline(active);
                    ReloadLists();
                }
                base.SetActive(active);
            }

            BuildingSideGenerator GetSide() {
                return curSide;
            }

            //the side editor is opened from the building editor, or directly from the line editor for front-only lines
            public override System.Func<EditorPanel, EditorPanel> CaptureReopen() {
                var linePanel = parentPanel as LineEditorPanel ?? parentPanel?.parentPanel as LineEditorPanel;
                var line = linePanel?.GetLine();
                var building = curSide?.building;
                if (line == null || building == null) return null;
                var buildingIndex = line.buildings.IndexOf(building);
                string sideName = null;
                if (building.front == curSide) sideName = "front";
                else if (building.left == curSide) sideName = "left";
                else if (building.right == curSide) sideName = "right";
                else if (building.back == curSide) sideName = "back";
                if (buildingIndex < 0 || sideName == null) return null;
                return parent => {
                    var parentLinePanel = parent as LineEditorPanel ?? parent?.parentPanel as LineEditorPanel;
                    var side = parentLinePanel?.GetBuildingSide(buildingIndex, sideName);
                    if (side == null) return null;
                    if (parent is BuildingEditorPanel buildingPanel) {
                        if (buildingPanel.curBuilding != side.building) return null;
                        buildingPanel.ShowSideEditor(side);
                        return buildingPanel.sideEditor;
                    }
                    parentLinePanel.EditSide(side);
                    return parentLinePanel.buildingEditor.sideEditor;
                };
            }

            public EditorPanel OpenFloor(bool ground, int i) {
                if (curSide == null) return null;
                if (ground) {
                    if (!curSide.state.Bool("hasGroundFloor")) return null;
                    EditGroundFloor();
                } else {
                    var floors = curSide.state.Array<ObjectState>("upperFloors");
                    if (floors == null || i < 0 || i >= floors.Length) return null;
                    SelectFloor(i);
                    EditFloor();
                }
                return floorEditor;
            }

            public EditorPanel OpenBlock(int i) {
                var blocks = curSide?.state.Array<ObjectState>("blockDict");
                if (blocks == null || i < 0 || i >= blocks.Length) return null;
                SelectBlock(i);
                EditBlock();
                return blockEditor;
            }

            void ReloadLists() {
                ReloadBlocks(true);
                ReloadFloors(true);
            }

            //Floors
            void SelectFloor(int i) {
                curFloorI = i;
                delFloorBtn.SetInteractable(i > -1);
                editFloorBtn.SetInteractable(i > -1);
            }

            void EditFloor() {
                if (curFloorI < 0 || GetSide() == null) return;
                floorEditor.curFloor = curFloorI;
                floorEditor.curSide = curSide;
                floorEditor.curBlockNames = curBlockNames;
                floorEditor.SetMode(false);
                Hide(true);
                floorEditor.SetActive(true);
            }

            void EditGroundFloor() {
                if (GetSide() == null) return;
                floorEditor.curSide = curSide;
                floorEditor.curBlockNames = curBlockNames;
                floorEditor.SetMode(true);
                Hide(true);
                floorEditor.SetActive(true);
            }

            void AddFloor() {
                if (curSide == null) return;
                Operations.BuildingLines.AddUpperFloor(curSide);
                ReloadFloors(true);
                builder.NotifyChange();
            }

            void DeleteFloor() {
                if (curSide == null || !Operations.BuildingLines.RemoveUpperFloor(curSide, curFloorI)) return;
                ReloadFloors(true);
                builder.NotifyChange();
            }

            void ReloadFloors(bool deselect) {
                if (GetSide() == null) return;
                floorList.Deselect();
                var floors = new List<string>();
                var floorArray = GetSide().state.Array<ObjectState>("upperFloors");
                if (floorArray != null) {
                    for (int i = 0; i < floorArray.Length; i++) {
                        floors.Add("Floor " + (i + 1).ToString());
                    }
                }
                floorList.SetItems(floors);
                if (deselect) SelectFloor(-1);
            }

            //Blocks
            void SelectBlock(int i) {
                curBlockI = i;
                delBlockBtn.SetInteractable(i > -1);
                editBlockBtn.SetInteractable(i > -1);
            }

            void EditBlock() {
                if (curBlockI < 0 || GetSide() == null) return;
                blockEditor.curBlock = curBlockI;
                blockEditor.curSide = curSide;
                blockEditor.ReloadState();
                Hide(true);
                blockEditor.SetActive(true);
            }

            void AddBlock() {
                if (curSide == null) return;
                Operations.BuildingLines.AddBlock(curSide);
                ReloadBlocks(true);
                builder.NotifyChange();
            }

            void DeleteBlock() {
                if (curSide == null || !Operations.BuildingLines.RemoveBlock(curSide, curBlockI)) return;
                ReloadBlocks(true);
                builder.NotifyChange();
            }

            bool EmptyName(string name) {
                return name == null || name == "";
            }

            void ReloadBlocks(bool deselect) {
                if (GetSide() == null) return;
                var blockTypes = CityElements.Types.Parsers.TypeParser.GetBuildingBlockTypes();
                blockList.Deselect();
                curBlockNames = new List<string>();
                var images = new List<string>();
                var blockArray = GetSide().state.Array<ObjectState>("blockDict");
                if (blockArray != null) {
                    for (int i = 0; i < blockArray.Length; i++) {
                        var blockName = blockArray[i].Name;
                        var blockListName = blockArray[i].Str("listName");
                        var realName = EmptyName(blockListName) ? blockName : blockListName;
                        var emptyName = EmptyName(realName);
                        curBlockNames.Add(emptyName ? "Block " + (i + 1).ToString() : realName);
                        var thumbnailName = blockTypes[blockArray[i].Str("type")].typeData.settings.thumbnail;
                        var thumbnailFile = EmptyName(thumbnailName) ? null : blockArray[i].Str(thumbnailName);
                        var thumbnail = EmptyName(thumbnailFile) ? null : PathHelper.FindInFolders(thumbnailFile);
                        images.Add(thumbnail);
                    }
                }
                blockList.SetItems(curBlockNames, images);
                if (deselect) SelectBlock(-1);
            }
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using SM = StringManager;
using RPB = EditorPanels.Helpers.RandomBuildingPresetsManager;

namespace EditorPanels {
    namespace Buildings {
        public class LineEditorPanel : EditorPanel {
            EditorPanelElements.ScrollList buildingList, loopPresetList;
            EditorPanelElements.Dropdown pointPlacementModeDropdown;
            public BuildingEditorPanel buildingEditor;
            EditorPanelElements.InputField heightField, minLoopLengthField, maxLoopLengthField;
            EditorPanelElements.TextureField topTex;
            int curI = -1;
            int curLoopPresetI = -1;

            public LineEditorPanel() {
                AddComplexElement(new PresetSelector(this));
                AddComplexElement(new TypeSelector<CityElements.Types.Runtime.Buildings.BuildingType.Line>(this) { stateGetter = () => builder?.terrainClick != null ? GetCurLine(false)?.state : null });
            }

            TypeSelector<CityElements.Types.Runtime.Buildings.BuildingType.Line> TS() {
                return GetComplexElement<TypeSelector<CityElements.Types.Runtime.Buildings.BuildingType.Line>>();
            }

            public override void Initialize(GameObject canvas) {
                InitializeWithCustomParameters<CityElements.Types.Runtime.Buildings.BuildingType.Line, CityElements.Types.Buildings.BuildingLineType>(canvas, GetCurLine, TS,
                    null, CityElements.Types.Parsers.TypeParser.GetBuildingLineTypes, ProcessCustomParts, true);
                buildingEditor = AddChildPanel<BuildingEditorPanel>(canvas);
            }

            bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
                TypeSelector<CityElements.Types.Runtime.Buildings.BuildingType.Line> tS, CityElements.Types.Runtime.Buildings.BuildingType.Line type) {

                var w = elem.width;
                if (elem.name == "mainGroup") {
                    //Buildings tab
                    if (!pS.EditingPreset()) {
                        p.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.5f);
                        p.AddButton(SM.Get("DELETE"), Delete, w * 0.5f);
                        p.IncreaseRow();
                    }

                    pS.AddPresetLoadDropdown(p, SM.Get("BUILDING_PRESET"), true, "building", SetBuildingsState, null, SetDefaultState);

                    var placementModes = new List<string> { SM.Get("BL_PM_NONE"), SM.Get("BL_PM_POINT"), SM.Get("BL_PM_DIVPOINT") };
                    pointPlacementModeDropdown = p.AddDropdown(SM.Get("BL_POINT_PLACEMENT_MODE"), placementModes, LoadPointPlacementMode, w);
                    p.IncreaseRow();

                    buildingList = p.AddScrollList(SM.Get("BL_LIST_TITLE"), new List<string>(), SelectBuilding, w, SM.Get("BL_LIST_TOOLTIP"));
                    p.IncreaseRow(5.0f);

                    var b1 = p.AddButton(SM.Get("BL_EDIT_SELECTED"), EditSelected, w);
                    p.IncreaseRow();

                    var b2 = p.AddButton(SM.Get("BL_SWITCH_DIV_POINT"), SwitchDivPoint, w);
                    p.IncreaseRow();

                    var b3 = p.AddButton(SM.Get("BL_DELETE_LAST_POINT"), DeleteLastPoint, w);
                    p.IncreaseRow();

                    if (pS.EditingPreset()) {
                        pointPlacementModeDropdown.SetInteractable(false);
                        b2.SetInteractable(false);
                        b3.SetInteractable(false);
                    }

                } else if (elem.name == "propertiesGroup") {
                    //Line properties tab
                    tS.AddTypeDropdown(p, SM.Get("BUILDING_TYPE"), w);
                    pS.AddPresetLoadAndSaveDropdown(p, SM.Get("BUILDING_LINE_PRESET"), true, "buildingLine", SetCurLineState, delegate { return GetCurLine(true).state; }, false, null, null, CheckIfFrontOnly);

                    p.AddFieldCheckbox(SM.Get("BL_PROJECT_TO_GROUND"), GetCurLine, "state.properties.projectToGround", null, w * 0.5f);
                    p.AddFieldCheckbox(SM.Get("BL_INV_DIR"), GetCurLine, "state.properties.invertDirection", null, w * 0.5f);
                    p.IncreaseRow();

                    p.AddFieldCheckbox(SM.Get("BL_LOOP"), GetCurLine, "state.properties.loop", null, w * 0.5f);
                    p.AddFieldCheckbox(SM.Get("BL_FRONTONLY"), GetCurLine, "state.properties.frontOnly", null, w * 0.5f, null, null, x => { CheckIfFrontOnly(); });
                    p.IncreaseRow();

                    heightField = p.AddFieldInputField(SM.Get("BLDG_HEIGHT"), SM.Get("BLDG_HEIGHT_PH"), GameEngine.InputContentType.Decimal, GetCurLine, "state.properties.height", null, w * 0.5f);
                    topTex = p.AddFieldTextureField(builder, SM.Get("BLDG_ROOF_TEX"), SM.Get("BLDG_ROOF_TEX_PH"), GetCurLine, "state.properties.roofTex", null, w * 0.5f);
                    p.IncreaseRow();

                } else if (elem.name == "loopGroup") {
                    //Loop tab
                    pS.AddPresetLoadDropdown(p, SM.Get("BUILDING_PRESET"), true, "building", null);
                    loopPresetList = p.AddScrollList(SM.Get("BL_LOOP_PRESET_LIST_TITLE"), new List<string>(), x => curLoopPresetI = x, w, SM.Get("BL_PRESET_LIST_TOOLTIP"));
                    p.IncreaseRow(5.0f);

                    var b4 = p.AddButton(SM.Get("BL_DELETE_LOOP_PRESET"), DeleteLoopPreset, w * 0.5f);
                    var b5 = p.AddButton(SM.Get("BL_ADD_LOOP_PRESET"), AddLoopPreset, w * 0.5f);
                    p.IncreaseRow();

                    maxLoopLengthField = p.AddInputField(SM.Get("BL_LOOP_MAX_LENGTH"), SM.Get("LENGTH_PH"), "15", GameEngine.InputContentType.Decimal, x => RPB.maxLoopLength = float.Parse(x), w * 0.5f);
                    minLoopLengthField = p.AddInputField(SM.Get("BL_LOOP_MIN_LENGTH"), SM.Get("LENGTH_PH"), "15", GameEngine.InputContentType.Decimal, x => RPB.minLoopLength = float.Parse(x), w * 0.5f);
                    p.IncreaseRow();

                    var cb = p.AddCheckbox(SM.Get("BL_LOOP_SUBDIVIDE"), true, SetSubdivide, w);
                    p.IncreaseRow();
                    var b6 = p.AddButton(SM.Get("TP_AUTOCLOSE"), AutoCloseLoop, w);
                    p.IncreaseRow();

                    if (pS.EditingPreset()) {
                        loopPresetList.SetInteractable(false);
                        maxLoopLengthField.SetInteractable(false);
                        minLoopLengthField.SetInteractable(false);
                        cb.SetInteractable(false);
                        b4.SetInteractable(false);
                        b5.SetInteractable(false);
                        b6.SetInteractable(false);
                    }

                } else {
                    return false;
                }
                return true;
            }

                void SetSubdivide(bool input) {
                RPB.subdivide = input;
                minLoopLengthField.SetInteractable(input);
                maxLoopLengthField.SetInteractable(input);
            }

            void DeleteLoopPreset() {
                if (curLoopPresetI == -1) return;
                RPB.loopPresets.RemoveAt(curLoopPresetI);
                ReloadLoopPresetsList(true);
            }

            void AddLoopPreset() {
                var preset = GetComplexElement<PresetSelector>().dropdowns["building"][1].current; //the one of the loop tab
                if (preset == null) return;
                RPB.loopPresets.Add(preset);
                ReloadLoopPresetsList(true);
            }

            void AutoCloseLoop() {
                if (GetPlacer() != null && GetCurLine(false) != null) {
                    switch (Operations.BuildingLines.AutoClose(GetCurLine(), RPB.minLoopLength, RPB.maxLoopLength, RPB.GetLoopPresetStates(), RPB.subdivide)) {
                        case Operations.AutoCloseResult.NoPresets:
                            builder.CreateAlert(SM.Get("ERROR"), SM.Get("BL_NEED_PRESETS_FOR_AUTOCLOSE"), SM.Get("OK"));
                            break;
                        case Operations.AutoCloseResult.Closed:
                            builder.NotifyChange();
                            ReadCurValues();
                            break;
                        case Operations.AutoCloseResult.CannotClose:
                            builder.CreateAlert(SM.Get("ERROR"), SM.Get("BL_CANNOT_AUTOCLOSE_ERROR"), SM.Get("OK"));
                            break;
                        default: //no loop found
                            builder.CreateAlert(SM.Get("ERROR"), SM.Get("BL_NO_LOOP_FOUND_ERROR"), SM.Get("OK"));
                            break;
                    }
                }
            }

            void LoadPointPlacementMode(int mode) {
                if (GetPlacer() != null && GetCurLine(false) != null) {
                    GetPlacer().placementMode = (ElementPlacer.BuildingPlacer.PlacementMode)mode;
                    pointPlacementModeDropdown.SetValue(mode);
                    GetCurLine().SetActive(true, mode == 0);
                }
            }

            ElementPlacer.BuildingPlacer GetPlacer() {
                var modifier = builder.terrainClick.modifier;
                if (modifier != null && modifier is ElementPlacer.BuildingPlacer) {
                    return ((ElementPlacer.BuildingPlacer)modifier);
                }
                return null;
            }

            void EditSelected() {
                if (curI < 0 || GetCurLine(false) == null) return;
                var bList = GetCurLine(false).buildings;
                buildingEditor.curBuilding = bList[curI];
                Hide(true);
                buildingEditor.SetActive(true);
                if (GetCurLine(false).state.Bool("frontOnly")) {
                    buildingEditor.sideEditor.parentPanel = this;
                    buildingEditor.ShowSideEditor(bList[curI].front);
                }
            }

            void SelectBuilding(int i) {
                curI = i;
                SetOutline(ActiveSelf());
            }

            public void EditSide(BuildingSideGenerator side) {
                var bList = GetCurLine(false).buildings;
                SelectBuilding(bList.IndexOf(side.building));
                EditSelected();
                if (GetCurLine(false) != null && GetCurLine(false).state.Bool("frontOnly")) {
                    buildingEditor.sideEditor.parentPanel = this;
                } else {
                    buildingEditor.sideEditor.parentPanel = buildingEditor;
                }
                buildingEditor.ShowSideEditor(side);
            }

            public void EditBuilding(Building building) {
                var bList = GetCurLine(false).buildings;
                SelectBuilding(bList.IndexOf(building));
                EditSelected();
            }

            public EditorPanel OpenBuilding(int i) {
                var line = GetCurLine(false);
                if (line == null || i < 0 || i >= line.buildings.Count || line.buildings[i] == null) return null;
                EditBuilding(line.buildings[i]);
                return buildingEditor;
            }

            //side: "front", "left", "right" or "back"
            public BuildingSideGenerator GetBuildingSide(int buildingIndex, string side) {
                var line = GetCurLine(false);
                if (line == null || buildingIndex < 0 || buildingIndex >= line.buildings.Count) return null;
                return Operations.BuildingLines.GetSide(line.buildings[buildingIndex], side);
            }

            public void SetPointPlacementMode(int mode) {
                LoadPointPlacementMode(mode);
            }

            void CheckIfFrontOnly() {
                var frontOnly = GetCurLine(false) != null && GetCurLine(false).state.Bool("frontOnly");
                heightField.SetInteractable(frontOnly);
                topTex.SetInteractable(frontOnly);
            }

            /*void AutoCloseLoop() {
                if (GetPlacer() != null && GetCurLine(false) != null) {
                    var res = GetCurLine().AutoClose();
                    switch (res.resultCode) {
                        case 1:
                            builder.NotifyChange();
                            break;
                        case -1:
                            builder.CreateAlert(SM.Get("ERROR"), SM.Get("BL_CANNOT_AUTOCLOSE_ERROR"), SM.Get("OK"));
                            break;
                        default: //0
                            builder.CreateAlert(SM.Get("ERROR"), SM.Get("BL_NO_LOOP_FOUND_ERROR"), SM.Get("OK"));
                            break;
                    }
                }
            }*/

            void ReloadList(bool deselect) {
                if (GetCurLine(false) == null) return;
                buildingList.Deselect();
                var items = new List<string>();
                var bList = GetCurLine(false).buildings;
                for (int i = 0; i < bList.Count; i++) {
                    items.Add("Building " + (i + 1).ToString());
                }
                buildingList.SetItems(items);
                if (deselect) SelectBuilding(-1);
                SetOutline(ActiveSelf());
            }

            void ReloadLoopPresetsList(bool deselect) {
                loopPresetList.Deselect();
                RPB.RemoveMissing();
                loopPresetList.SetItems(new List<string>(RPB.loopPresets));
                if (deselect) curLoopPresetI = -1;
            }

            void DeleteLastPoint() {
                if (GetPlacer() != null && GetCurLine(false) != null) {
                    Operations.BuildingLines.RemoveLastPoint(GetCurLine());
                    builder.NotifyChange();
                }
            }

            void ClearEmptyLine() {
                if (Operations.BuildingLines.IsIncomplete(GetCurLine(false))) {
                    Operations.BuildingLines.Delete(GetCurLine(false));
                    builder.NotifyChange();
                }
            }

            public override void Hide(bool hide) {
                if (hide && GetPlacer() != null) {
                    GetPlacer().placementMode = ElementPlacer.BuildingPlacer.PlacementMode.None;
                }
                GameEngine.Gizmo.ClearTargets();
                base.Hide(hide);
            }

            void SetOutline(bool active) {
                if (GetCurLine(false) != null) {
                    var bList = GetCurLine(false).buildings;
                    for (int i = 0; i < bList.Count; i++) {
                        bList[i].SetOutline(active, curI == i);
                    }
                }
            }

            //the buildings added to the line from now on use the last building preset chosen
            void SetDefaultState() {
                var line = GetCurLine(false);
                var pS = GetComplexElement<PresetSelector>();
                var preset = Operations.Presets.Get("building", pS.dropdowns["building"][0].lastPreset);
                if (line != null && preset != null) line.stateForNewBuildings = preset;
            }

            ObjectState SetBuildingsState(ObjectState state) {
                var line = GetCurLine(false);
                if (line != null) Operations.BuildingLines.SetAllBuildingsState(line, state);
                return state;
            }

            ObjectState SetCurLineState(ObjectState state) {
                Operations.BuildingLines.SetLineState(GetCurLine(), state);
                return GetCurLine().state;
            }

            public override void SetActive(bool active) {
                var pS = GetComplexElement<PresetSelector>();
                bool noLine = false;
                if (active) {
                    noLine = GetCurLine(false) == null;
                    ClearEmptyLine();
                } else {
                    if (ActiveSelf() && !keepActive) Terminate(true);
                }
                base.SetActive(active);
                if (active) {
                    //a new line gets the last presets chosen, for itself and for its buildings
                    if (noLine) pS.LoadPreset(pS.dropdowns["buildingLine"][0].lastPreset, "buildingLine", SetCurLineState, 0);
                    pS.LoadPreset(-1, "building", null, 0);
                    pointPlacementModeDropdown.SetValue(1);
                    if (!noLine) {
                        SetPlaceNewPoints(false);
                        pointPlacementModeDropdown.SetValue(0);
                    }
                    GetCurLine()?.SetActive(active, true);
                    if (noLine) SetDefaultState(); //the line exists now
                    TS().ReadCurValues(); //a new line only gets its state now
                    ReloadList(true);
                    ReloadLoopPresetsList(true);
                    CheckIfFrontOnly();

                    if (pS.EditingPreset()) {
                        SetTitle(SM.Get("PRESET"), false);
                    } else if (GetCurLine(false) != null) {
                        SetTitle(GO.GetName(GO.GetObject(GetCurLine(false))));
                    }
                }
                SetOutline(active);
            }

            protected override void ReplaceTitle(string value) {
                var obj = GetCurLine(false);
                if (value == null || value == "") {
                    if (obj != null) {
                        value = GO.GetName(GO.GetObject(obj));
                    } else {
                        value = "";
                    }
                } else {
                    if (obj != null) GO.SetName(GO.GetObject(obj), value);
                }
                base.ReplaceTitle(value);
            }

            void SetPlaceNewPoints(bool enabled) {
                var modifier = builder.terrainClick.modifier;
                if (modifier != null && modifier is ElementPlacer.BuildingPlacer) {
                    ((ElementPlacer.BuildingPlacer)modifier).placementMode = ElementPlacer.BuildingPlacer.PlacementMode.Point;
                }
                GetCurLine()?.SetActive(true, !enabled);
            }

            public override void Update() {
                ReloadList(true);
                ReloadLoopPresetsList(true);
            }

            public void Delete() {
                Operations.BuildingLines.Delete(GetCurLine());
                builder.NotifyChange();
                Terminate();
            }

            public override void Terminate() {
                keepActive = false;
                Terminate(false);
            }

            public void Terminate(bool auto) {
                SetOutline(false);
                if (GetPlacer() != null) {
                    GetPlacer().placementMode = ElementPlacer.BuildingPlacer.PlacementMode.None;
                }
                GetCurLine()?.SetActive(false, false);
                ClearEmptyLine();
                builder.helper.elementManager.ShowAnchors(false);
                if (!auto) SetActive(false);
                GameEngine.Gizmo.ClearTargets();
                if (!auto) builder.SetSelectObjectMode(); //otherwise the building placer stays as the click modifier and selection stops working
            }

            BuildingLine GetCurLine() {
                return GetCurLine(true);
            }

            public override object GetUndoTarget() {
                return GetCurLine(false);
            }

            public BuildingLine GetLine() {
                return GetCurLine(false);
            }

            Building[] GetBuildings() {
                var lst = new List<Building>();
                var line = GetCurLine(false);
                if (line != null) {
                    foreach (var building in line.buildings) {
                        lst.Add(building);
                    }
                }
                return lst.ToArray();
            }

            BuildingLine GetCurLine(bool createIfNull) {
                var modifier = builder.terrainClick.modifier;
                if (modifier is TerrainModifier.Null) {
                    return builder.helper.elementManager.GetDummy<BuildingLine>();
                }
                else {
                    if (modifier != null && modifier is ElementPlacer.BuildingPlacer) {
                        return ((ElementPlacer.BuildingPlacer)modifier).GetLine(createIfNull);
                    } else {
                        return null;
                    }
                }
            }

            void SwitchDivPoint() {
                var obj = builder.GetCurSelectedObject();
                var line = GetLine();
                if (obj != null && line != null) {
                    var point = GS.FindRecursive<TerrainPoint>(obj);
                    if (Operations.BuildingLines.ToggleDividing(line, point)) {
                        builder.NotifyChange();
                        ReloadList(true);
                    }
                }
            }
        }
    }
}
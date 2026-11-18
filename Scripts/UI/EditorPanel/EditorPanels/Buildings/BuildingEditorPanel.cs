using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;
using RB = CityElements.Types.Runtime.Buildings;
using B = CityElements.Types.Buildings;

namespace EditorPanels {
    namespace Buildings {
        public class BuildingEditorPanel : EditorPanel, ISideEditorHost {
            public Building curBuilding = null;
            EditorPanelElements.Button frontBtn, leftBtn, rightBtn, backBtn;
            EditorPanelElements.TextureField topTex;
            EditorPanelElements.Checkbox allSidesEqualCheckbox;
            public SideEditorPanel sideEditor;
            bool refreshingAllSidesCheckbox = false;
            BuildingSizeHandles sizeHandles;

            public BuildingEditorPanel() {
                AddComplexElement(new PresetSelector(this));
            }

            public override void Initialize(GameObject canvas) {
                var line = ((LineEditorPanel)parentPanel).GetLine();
                if (line == null) return;
                var type = line.GetLineType().name;
                InitializeWithCustomParameters<RB.BuildingType.Building, B.BuildingBuildingType>(canvas, GetBuilding, null,
                    type, CityElements.Types.Parsers.TypeParser.GetBuildingBuildingTypes, ProcessCustomParts, true, 1.5f, false);
                sideEditor = AddChildPanel<SideEditorPanel>(canvas);
            }

            bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
                TypeSelector<RB.BuildingType.Building> tS, RB.BuildingType.Building type) {

                var w = elem.width;
                if (elem.name == "mainGroup") {
                    p.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.5f);
                    p.AddButton(SM.Get("BLDG_GO_UP"), GoUp, w * 0.5f);
                    p.IncreaseRow();

                    System.Func<ObjectState> getter = delegate { return GetBuilding().state; };
                    System.Func<ObjectState, ObjectState> setter = x => { GetBuilding().SetState(x); return GetBuilding().State; };
                    pS.AddPresetLoadAndSaveDropdown(p, SM.Get("BUILDING_PRESET"), true, "building", setter, getter, false, null, null, RefreshSidesDelayed);

                    p.AddFieldCheckbox(SM.Get("BLDG_ENABLED"), GetBuilding, "State.properties.front", null, w / 3, null, null, _ => RefreshSidesDelayed());
                    frontBtn = p.AddButton(SM.Get("BLDG_EDIT_FRONT"), delegate { ShowSideEditor(curBuilding.front); }, w * 2.0f / 3);
                    p.IncreaseRow();

                    p.AddFieldCheckbox(SM.Get("BLDG_ENABLED"), GetBuilding, "State.properties.left", null, w / 3, null, null, _ => RefreshSidesDelayed());
                    leftBtn = p.AddButton(SM.Get("BLDG_EDIT_LEFT"), delegate { ShowSideEditor(curBuilding.left); }, w * 2.0f / 3);
                    p.IncreaseRow();

                    p.AddFieldCheckbox(SM.Get("BLDG_ENABLED"), GetBuilding, "State.properties.right", null, w / 3, null, null, _ => RefreshSidesDelayed());
                    rightBtn = p.AddButton(SM.Get("BLDG_EDIT_RIGHT"), delegate { ShowSideEditor(curBuilding.right); }, w * 2.0f / 3);
                    p.IncreaseRow();

                    p.AddFieldCheckbox(SM.Get("BLDG_ENABLED"), GetBuilding, "State.properties.back", null, w / 3, null, null, _ => RefreshSidesDelayed());
                    backBtn = p.AddButton(SM.Get("BLDG_EDIT_BACK"), delegate { ShowSideEditor(curBuilding.back); }, w * 2.0f / 3);
                    p.IncreaseRow();

                    p.AddFieldCheckbox(SM.Get("BLDG_ENABLED"), GetBuilding, "State.properties.top", null, w / 3, null, null, x => { topTex.SetInteractable(x); });
                    topTex = p.AddFieldTextureField(builder, SM.Get("BLDG_ROOF_TEX"), SM.Get("BLDG_ROOF_TEX_PH"), GetBuilding, "State.properties.topTexture", null, w * 2.0f / 3);
                    p.IncreaseRow();

                    p.AddFieldInputField(SM.Get("BLDG_HEIGHT"), SM.Get("BLDG_HEIGHT_PH"), GameEngine.InputContentType.Decimal, GetBuilding, "State.properties.height", null, w * 0.5f, null, null, _ => RefreshSidesDelayed());
                    p.AddFieldInputField(SM.Get("BLDG_DEPTH"), SM.Get("BLDG_DEPTH_PH"), GameEngine.InputContentType.Decimal, GetBuilding, "State.properties.depth", null, w * 0.5f, null, null, _ => RefreshSidesDelayed());
                    p.IncreaseRow();

                    p.AddFieldCheckbox(SM.Get("BLDG_FIXACUTE"), GetBuilding, "State.properties.fixAcuteAngles", null, w / 3, SM.Get("BLDG_FIXACUTE_TOOLTIP"));
                    p.AddFieldCheckbox(SM.Get("BLDG_ENABLED"), GetBuilding, "enabled", null, w / 3, null, null, _ => GetBuilding()?.RefreshOutline());
                    allSidesEqualCheckbox = p.AddCheckbox(SM.Get("BLDG_ALL_SIDES_EQUAL"), false, SetAllSidesEqual, w / 3);
                    p.IncreaseRow();
                } else {
                    return false;
                }
                return true;
            }

                public void ShowSideEditor(BuildingSideGenerator side) {
                if (side == null) return;
                sideEditor.curSide = side;
                Hide(true);
                sideEditor.SetActive(true);
            }

            void SetAllSidesEqual(bool x) {
                if (refreshingAllSidesCheckbox) return;
                Operations.BuildingLines.SetAllSidesEqual(GetBuilding(), x);
                builder.NotifyChange();
                RefreshSidesDelayed();
            }

            void RefreshSidesDelayed() {
                builder.DoDelayed(RefreshSides); //the building update is done on the next frame, so we have to wait to update the buttons
            }

            void RefreshSides() {
                refreshingAllSidesCheckbox = true;
                allSidesEqualCheckbox.SetValue(curBuilding.state.Bool("allSidesEqual"));
                refreshingAllSidesCheckbox = false;
                frontBtn.SetInteractable(curBuilding.state.Bool("front"));
                backBtn.SetInteractable(curBuilding.state.Bool("back") && !curBuilding.state.Bool("allSidesEqual"));
                leftBtn.SetInteractable(curBuilding.left != null && !curBuilding.state.Bool("allSidesEqual"));
                rightBtn.SetInteractable(curBuilding.right != null && !curBuilding.state.Bool("allSidesEqual"));
            }

            public override void SetActive(bool active) {
                if (!PrepareActivation(active)) return;
                var pS = GetComplexElement<PresetSelector>();
                if (active) {
                    System.Func<ObjectState, ObjectState> setter = x => { Operations.BuildingLines.SetBuildingState(GetBuilding(), x); return GetBuilding().State; };
                    pS.LoadPreset(GetBuilding() == null ? pS.dropdowns["building"][0].lastPreset : null, "building", setter, 0);
                }
                if (curBuilding != null) {
                    RefreshSides();
                    topTex.SetInteractable(curBuilding.state.Bool("top"));
                    curBuilding.SetOutline(active);
                }
                base.SetActive(active);
                //the handles to change the height and the depth of the building in the city (not for the presets)
                if (active && curBuilding != null && !pS.EditingPreset()) {
                    sizeHandles ??= new BuildingSizeHandles(builder);
                    sizeHandles.Show(curBuilding);
                } else {
                    sizeHandles?.Hide();
                }
            }

            //after the updates of the city: the handles follow the building
            public override void Update() {
                if (ActiveSelf()) sizeHandles?.Update();
            }

            Building GetBuilding() {
                return curBuilding;
            }

            public string GetSideBuildingType() {
                var line = (parentPanel as LineEditorPanel)?.GetLine();
                return line?.GetLineType().name;
            }

            public System.Func<EditorPanel, EditorPanel> CaptureSideReopen(BuildingSideGenerator curSide) {
                var line = (parentPanel as LineEditorPanel)?.GetLine();
                var building = curSide?.building;
                if (line == null || building == null) return null;
                var buildingIndex = line.buildings.IndexOf(building);
                var sideName = Operations.BuildingLines.GetSideName(curSide);
                if (buildingIndex < 0 || sideName == null) return null;
                return parent => {
                    if (parent is not BuildingEditorPanel buildingPanel) return null;
                    var side = (buildingPanel.parentPanel as LineEditorPanel)?.GetBuildingSide(buildingIndex, sideName);
                    if (side == null || buildingPanel.curBuilding != side.building) return null;
                    buildingPanel.ShowSideEditor(side);
                    return buildingPanel.sideEditor;
                };
            }

            public override System.Func<EditorPanel, EditorPanel> CaptureReopen() {
                var line = (parentPanel as LineEditorPanel)?.GetLine();
                var index = line != null ? line.buildings.IndexOf(curBuilding) : -1;
                if (index < 0) return null;
                return parent => (parent as LineEditorPanel)?.OpenBuilding(index);
            }
        }
    }
}
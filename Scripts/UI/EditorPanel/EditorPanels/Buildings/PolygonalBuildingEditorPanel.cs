using System.Collections.Generic;
using GameObject = Godot.Control;
using GO = GameEngine.GameObject;
using SM = StringManager;
using RB = CityElements.Types.Runtime.Buildings;
using TP = CityElements.Types.Parsers.TypeParser;

namespace EditorPanels {
    namespace Buildings {
        //The editor of a polygonal building: its points (the closed outline of the building), its properties and the
        //sides of its walls (one for every wall, or one each)
        public class PolygonalBuildingEditorPanel : EditorPanel, ISideEditorHost {
            EditorPanelElements.ScrollList wallList;
            EditorPanelElements.Dropdown pointPlacementModeDropdown;
            EditorPanelElements.Button sidePropertiesButton, editWallButton;
            public SideEditorPanel sideEditor;
            int curI = -1;
            BuildingSizeHandles sizeHandles;

            public PolygonalBuildingEditorPanel() {
                AddComplexElement(new PresetSelector(this));
                AddComplexElement(new TypeSelector<RB.BuildingType.Polygon>(this) { stateGetter = () => builder?.terrainClick != null ? GetCurBuilding(false)?.state : null });
            }

            TypeSelector<RB.BuildingType.Polygon> TS() {
                return GetComplexElement<TypeSelector<RB.BuildingType.Polygon>>();
            }

            public override void Initialize(GameObject canvas) {
                InitializeWithCustomParameters<RB.BuildingType.Polygon, CityElements.Types.Buildings.PolygonalBuildingType>(canvas, GetCurBuilding, TS,
                    null, TP.GetPolygonalBuildingTypes, ProcessCustomParts, true);
                sideEditor = AddChildPanel<SideEditorPanel>(canvas);
            }

            bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
                TypeSelector<RB.BuildingType.Polygon> tS, RB.BuildingType.Polygon type) {

                var w = elem.width;
                if (elem.name == "mainGroup") {
                    if (!pS.EditingPreset()) {
                        p.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.5f);
                        p.AddButton(SM.Get("DELETE"), Delete, w * 0.5f);
                        p.IncreaseRow();
                    }

                    var placementModes = new List<string> { SM.Get("BL_PM_NONE"), SM.Get("BL_PM_POINT") };
                    pointPlacementModeDropdown = p.AddDropdown(SM.Get("BL_POINT_PLACEMENT_MODE"), placementModes, LoadPointPlacementMode, w);
                    p.IncreaseRow();

                    var deleteLast = p.AddButton(SM.Get("BL_DELETE_LAST_POINT"), DeleteLastPoint, w);
                    p.IncreaseRow();

                    p.AddFieldCheckbox(SM.Get("PB_INDIVIDUAL_SIDES"), GetCurBuilding, "state.properties.individualSides", null, w * 0.5f, SM.Get("PB_INDIVIDUAL_SIDES_TOOLTIP"), null, _ => IndividualSidesChanged());
                    sidePropertiesButton = p.AddButton(SM.Get("PB_SIDE_PROPERTIES"), EditSharedSide, w * 0.5f);
                    p.IncreaseRow();

                    wallList = p.AddScrollList(SM.Get("PB_SIDE_LIST"), new List<string>(), SelectWall, w, SM.Get("PB_SIDE_LIST_TOOLTIP"));
                    p.IncreaseRow(5.0f);

                    editWallButton = p.AddButton(SM.Get("PB_EDIT_SIDE"), EditSelectedWall, w);
                    p.IncreaseRow();

                    if (pS.EditingPreset()) {
                        pointPlacementModeDropdown.SetInteractable(false);
                        deleteLast.SetInteractable(false);
                    }
                } else if (elem.name == "propertiesGroup") {
                    tS.AddTypeDropdown(p, SM.Get("BUILDING_TYPE"), w);
                    pS.AddPresetLoadAndSaveDropdown(p, SM.Get("POLYGONAL_BUILDING_PRESET"), true, Operations.PolygonalBuildings.PresetCategory, SetCurState,
                        delegate { return GetCurBuilding(true).state; }, false, null, null, RefreshSides);

                    var projectField = p.AddFieldCheckbox(SM.Get("BL_PROJECT_TO_GROUND"), GetCurBuilding, "instanceState.properties.projectToGround", null, w * 0.5f, null, null, PlacementHeight.OnProjectionToggled);
                    if (pS.EditingPreset()) projectField.SetInteractable(false); //presets never store instance specific values
                    p.IncreaseRow();

                    p.AddFieldInputField(SM.Get("BLDG_HEIGHT"), SM.Get("BLDG_HEIGHT_PH"), GameEngine.InputContentType.Decimal, GetCurBuilding, "state.properties.height", null, w * 0.5f);
                    p.AddFieldTextureField(builder, SM.Get("BLDG_ROOF_TEX"), SM.Get("BLDG_ROOF_TEX_PH"), GetCurBuilding, "state.properties.roofTex", null, w * 0.5f);
                    p.IncreaseRow();
                } else {
                    return false;
                }
                return true;
            }

            // Sides

            public string GetSideBuildingType() {
                var type = GetCurBuilding(false)?.state.Str("type", null);
                if (type != null) return type;
                foreach (var key in TP.GetPolygonalBuildingTypes().Keys) return key;
                return null;
            }

            public System.Func<EditorPanel, EditorPanel> CaptureSideReopen(BuildingSideGenerator side) {
                var building = GetCurBuilding(false);
                var index = Operations.PolygonalBuildings.WallIndex(building, side);
                if (index < 0) return null;
                return parent => (parent as PolygonalBuildingEditorPanel)?.OpenWallSide(index);
            }

            //opens the side of the wall starting at the point with the index (null if there is none)
            public EditorPanel OpenWallSide(int pointIndex) {
                var side = Operations.PolygonalBuildings.GetWallSide(GetCurBuilding(false), pointIndex);
                if (side == null) return null;
                EditSide(side);
                return sideEditor;
            }

            public void EditSide(BuildingSideGenerator side) {
                if (side == null) return;
                var building = GetCurBuilding(false);
                if (building != null) {
                    var i = building.walls.IndexOf(side.building);
                    if (i >= 0 && building.IsIndividual()) SelectWall(i);
                }
                sideEditor.curSide = side;
                Hide(true);
                sideEditor.SetActive(true);
            }

            void EditSharedSide() {
                var building = GetCurBuilding(false);
                if (building == null || building.IsIndividual()) return;
                EditSide(Operations.PolygonalBuildings.GetWallSide(building, null));
            }

            void EditSelectedWall() {
                var building = GetCurBuilding(false);
                if (building == null || curI < 0 || curI >= building.walls.Count) return;
                EditSide(building.walls[curI].front);
            }

            void SelectWall(int i) {
                curI = i;
                SetOutline(ActiveSelf());
            }

            void IndividualSidesChanged() {
                var building = GetCurBuilding(false);
                if (building != null && building.IsIndividual()) building.FillWallStates();
                builder.NotifyChange();
                builder.DoDelayed(RefreshSides); //the walls get their states at the next update
            }

            //the side list, with the side properties button in its place when the walls share one side
            void RefreshSides() {
                if (wallList == null) return;
                var building = GetCurBuilding(false);
                var individual = building != null && building.IsIndividual();
                wallList.Deselect();
                var items = new List<string>();
                if (building != null) {
                    for (int i = 0; i < building.walls.Count; i++) items.Add(SM.Get("PB_SIDE") + " " + (i + 1));
                }
                wallList.SetItems(items);
                curI = -1;
                wallList.SetInteractable(individual);
                editWallButton.SetInteractable(individual);
                sidePropertiesButton.SetInteractable(!individual && building != null && building.walls.Count > 0);
                SetOutline(ActiveSelf());
            }

            void SetOutline(bool active) {
                var building = GetCurBuilding(false);
                if (building == null) return;
                for (int i = 0; i < building.walls.Count; i++) {
                    building.walls[i].SetOutline(active, curI == i);
                }
            }

            // Points

            void LoadPointPlacementMode(int mode) {
                if (GetPlacer() != null && GetCurBuilding(false) != null) {
                    GetPlacer().placementMode = (ElementPlacer.PolygonalBuildingPlacer.PlacementMode)mode;
                    pointPlacementModeDropdown.SetValue(mode);
                    GetCurBuilding().SetActive(true, mode == 0);
                }
            }

            public void SetPointPlacementMode(int mode) {
                LoadPointPlacementMode(mode);
            }

            void DeleteLastPoint() {
                if (GetPlacer() != null && GetCurBuilding(false) != null) {
                    Operations.PolygonalBuildings.RemoveLastPoint(GetCurBuilding());
                    builder.NotifyChange();
                }
            }

            void ClearEmptyBuilding() {
                if (Operations.PolygonalBuildings.IsIncomplete(GetCurBuilding(false))) {
                    Operations.PolygonalBuildings.Delete(GetCurBuilding(false));
                    builder.NotifyChange();
                }
            }

            // Panel

            ElementPlacer.PolygonalBuildingPlacer GetPlacer() {
                return builder.terrainClick.modifier as ElementPlacer.PolygonalBuildingPlacer;
            }

            ObjectState SetCurState(ObjectState state) {
                Operations.PolygonalBuildings.SetState(GetCurBuilding(), state);
                return GetCurBuilding().state;
            }

            public override void Hide(bool hide) {
                if (hide && GetPlacer() != null) {
                    GetPlacer().placementMode = ElementPlacer.PolygonalBuildingPlacer.PlacementMode.None;
                }
                GameEngine.Gizmo.ClearTargets();
                base.Hide(hide);
            }

            public override void SetActive(bool active) {
                if (!PrepareActivation(active)) return;
                var pS = GetComplexElement<PresetSelector>();
                var noBuilding = false;
                if (active) {
                    ClearEmptyBuilding(); //first, as an empty building is cleared and then edited as a new one
                    noBuilding = GetCurBuilding(false) == null;
                } else {
                    if (ActiveSelf() && !keepActive) Terminate(true);
                }
                base.SetActive(active);
                if (active) {
                    //a new building gets the last preset chosen
                    if (noBuilding) pS.LoadPreset(pS.dropdowns[Operations.PolygonalBuildings.PresetCategory][0].lastPreset, Operations.PolygonalBuildings.PresetCategory, SetCurState, 0);
                    pointPlacementModeDropdown.SetValue(1);
                    if (!noBuilding) pointPlacementModeDropdown.SetValue(0); //an existing building is edited by moving its points
                    var building = GetCurBuilding();
                    building?.SetActive(true, true);
                    building?.Select(true);
                    TS().ReadCurValues(); //a new building only gets its state now
                    RefreshSides();

                    if (pS.EditingPreset()) {
                        SetTitle(SM.Get("PRESET"), false);
                    } else if (GetCurBuilding(false) != null) {
                        SetTitle(GO.GetName(GO.GetObject(GetCurBuilding(false))));
                    }
                }
                SetOutline(active);
                //the roof can be dragged to change the height (not for the presets)
                var edited = active ? GetCurBuilding(false) : null;
                if (edited != null && !pS.EditingPreset()) {
                    sizeHandles ??= new BuildingSizeHandles(builder);
                    sizeHandles.Show(edited);
                } else {
                    sizeHandles?.Hide();
                }
            }

            protected override void ReplaceTitle(string value) {
                var obj = GetCurBuilding(false);
                if (value == null || value == "") {
                    value = obj != null ? GO.GetName(GO.GetObject(obj)) : "";
                } else {
                    if (obj != null) GO.SetName(GO.GetObject(obj), value);
                }
                base.ReplaceTitle(value);
            }

            //after the updates of the city: the handle of the roof follows the building
            public override void Update() {
                RefreshSides();
                if (ActiveSelf()) {
                    var building = GetCurBuilding(false);
                    if (building != null && sizeHandles == null && !GetComplexElement<PresetSelector>().EditingPreset()) {
                        sizeHandles = new BuildingSizeHandles(builder);
                        sizeHandles.Show(building);
                    }
                    sizeHandles?.Update();
                }
            }

            public void Delete() {
                Operations.PolygonalBuildings.Delete(GetCurBuilding());
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
                    GetPlacer().placementMode = ElementPlacer.PolygonalBuildingPlacer.PlacementMode.None;
                }
                var building = GetCurBuilding(false);
                building?.SetActive(false, false);
                building?.Select(false);
                ClearEmptyBuilding();
                builder.helper.elementManager.ShowAnchors(false);
                if (!auto) SetActive(false);
                GameEngine.Gizmo.ClearTargets();
                if (!auto) builder.SetSelectObjectMode(); //otherwise the placer stays as the click modifier and selection stops working
            }

            public override object GetUndoTarget() {
                return GetCurBuilding(false);
            }

            public PolygonalBuilding GetBuilding() {
                return GetCurBuilding(false);
            }

            PolygonalBuilding GetCurBuilding() {
                return GetCurBuilding(true);
            }

            PolygonalBuilding GetCurBuilding(bool createIfNull) {
                var modifier = builder.terrainClick.modifier;
                if (modifier is TerrainModifier.Null) {
                    return builder.helper.elementManager.GetDummy<PolygonalBuilding>();
                }
                if (modifier is ElementPlacer.PolygonalBuildingPlacer placer) {
                    return placer.GetBuilding(createIfNull);
                }
                return null;
            }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace EditorPanels {
    public class TerrainPatchEditorPanel : EditorPanel {
        EditorPanelElements.Dropdown pointPlacementModeDropdown, borderMeshDropdown;
        int curBorderMesh = -1;
        TerrainBorderMesh dummyBorderMesh = new TerrainBorderMesh();
        EditorPanelElements.Button switchToBorderMeshPlacementButton, editBMButton;

        public TerrainPatchEditorPanel() {
            AddComplexElement(new TypeSelector<CityElements.Types.Runtime.TerrainPatchType>(this) { stateGetter = () => builder?.terrainClick != null ? GetCurPatch(false)?.state : null });
        }

        TypeSelector<CityElements.Types.Runtime.TerrainPatchType> TS() {
            return GetComplexElement<TypeSelector<CityElements.Types.Runtime.TerrainPatchType>>();
        }

        public override void Initialize(GameObject canvas) {
            var res = InitializeWithCustomParameters<CityElements.Types.Runtime.TerrainPatchType, CityElements.Types.TerrainPatchType>(canvas, GetCurPatch, TS,
                null, CityElements.Types.Parsers.TypeParser.GetTerrainPatchTypes, ProcessCustomParts, false);
            if (res) UpdateBorderMeshesList();
        }

        bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
            TypeSelector<CityElements.Types.Runtime.TerrainPatchType> tS, CityElements.Types.Runtime.TerrainPatchType type) {

            var w = elem.width;
            if (elem.name == "mainGroup") {
                //Main
                p.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.5f);
                p.AddButton(SM.Get("DELETE"), Delete, w * 0.5f);
                p.IncreaseRow();

                tS.AddTypeDropdown(p, SM.Get("TERRAIN_PATCH_TYPE"), w);

                var placementModes = new List<string> { SM.Get("TP_PM_NONE"), SM.Get("TP_PM_PERIMETER"), SM.Get("TP_PM_INTERNAL"), SM.Get("TP_PM_BORDER_MESH") };
                pointPlacementModeDropdown = p.AddDropdown(SM.Get("TP_POINT_PLACEMENT_MODE"), placementModes, LoadPointPlacementMode, 1.5f);
                p.IncreaseRow();

                p.AddButton(SM.Get("TP_DELETE_LAST_PERIMETER_POINT"), DeleteLastPerimeterPoint, 1.5f);
                p.IncreaseRow();

                p.AddFieldInputField(SM.Get("TP_SMOOTHING"), SM.Get("TP_SMOOTHING_PH"), GameEngine.InputContentType.Integer, GetCurPatch, "state.properties.smooth", null, 1.5f);
                p.IncreaseRow();

                p.AddFieldCheckbox(SM.Get("TP_PROJECT_TO_GROUND"), GetCurPatch, "state.properties.projectToGround", null, 1.5f, null, null, PlacementHeight.OnProjectionToggled);
                p.IncreaseRow();

                p.AddButton(SM.Get("TP_AUTOCLOSE"), AutoCloseLoop, 1.5f);
                p.IncreaseRow();

                p.AddFieldTextureField(builder, SM.Get("TEXTURE"), SM.Get("TEXTURE_PH"), GetCurPatch, "state.properties.texture", null, 1.5f, null, null, x => { PreferencesManager.Set("curTerrainTexture", x); });
                p.IncreaseRow();

            } else if (elem.name == "borderMeshGroup") {
                //Border meshes
                borderMeshDropdown = p.AddDropdown(SM.Get("TP_BORDER_MESH_LIST"), new List<string> { SM.Get("NONE") }, GetBorderMesh, 1.5f);
                p.IncreaseRow();

                switchToBorderMeshPlacementButton = p.AddButton(SM.Get("TP_SWITCH_TO_BORDER_MESH_PLACEMENT"), SwitchToBorderMeshPlacement, 1.5f);
                p.IncreaseRow();

                p.AddButton(SM.Get("TP_ADD_BORDER_MESH"), AddBorderMesh, 0.75f);
                p.AddButton(SM.Get("TP_REMOVE_BORDER_MESH"), RemoveBorderMeshAlert, 0.75f);
                p.IncreaseRow();

                editBMButton = p.AddButton(SM.Get("TP_EDIT_BORDER_MESH"), EditBorderMesh, 1.5f);
                editBMButton.SetInteractable(false);
                p.IncreaseRow();
            } else {
                return false;
            }
            return true;
        }

        void EditBorderMesh() {
            SetPlaceNewPoints(false);
            pointPlacementModeDropdown.SetValue(0);
            var panel = builder.tpBorderMeshEditorPanel;
            panel.curMesh = GetCurBorderMesh();
            panel.parentPanel = this;
            Hide(true);
            panel.SetActive(true);
        }

        public int GetCurBorderMeshIndex() {
            return curBorderMesh;
        }

        public EditorPanel OpenBorderMesh(int i) {
            if (!SelectBorderMesh(i)) return null;
            EditBorderMesh();
            return builder.tpBorderMeshEditorPanel;
        }

        bool SelectBorderMesh(int i) {
            var patch = GetCurPatch(false);
            if (patch == null || i < 0 || i >= patch.GetBorderMeshCount()) return false;
            borderMeshDropdown.SetValue(i);
            GetBorderMesh(i);
            return true;
        }

        public void SetPointPlacementMode(int mode, int borderMesh) {
            SelectBorderMesh(borderMesh);
            LoadPointPlacementMode(mode);
        }

        TerrainBorderMesh GetCurBorderMesh() {
            var res = GetCurPatch(false)?.GetBorderMesh(curBorderMesh);
            if (res == null) {
                res = dummyBorderMesh;
                res.state = new ObjectState();
            }
            return res;
        }

        void GetBorderMesh(int i) {
            curBorderMesh = i;
            if (GetPlacer() != null) {
                GetPlacer().curBorderMesh = curBorderMesh;
                HighlightCurrentBorderMesh();
                editBMButton.SetInteractable(GetCurPatch().GetBorderMeshCount() > 0);
            }
            ReadCurValues();
        }

        void HighlightCurrentBorderMesh() {
            GetCurPatch().HighlightBorderMesh(GetPlacer().placementMode == ElementPlacer.TerrainPlacer.PlacementMode.BorderMesh ? curBorderMesh : -1);
        }

        void AddBorderMesh() {
            if (Operations.TerrainPatches.AddBorderMesh(GetCurPatch()) == Operations.AddBorderMeshResult.LimitReached) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("TP_TOO_MANY_BORDER_MESHES_ERROR"), SM.Get("OK"));
                return;
            }
            builder.NotifyChange();
            UpdateBorderMeshesList();
            borderMeshDropdown.SetValue(GetCurPatch().GetBorderMeshCount());
            HighlightCurrentBorderMesh();
        }

        void SwitchToBorderMeshPlacement() {
            pointPlacementModeDropdown.SetValue(3);
        }

        void RemoveBorderMeshAlert() {
            var realCurBorderMesh = curBorderMesh;
            if (GetCurPatch().GetBorderMeshCount() == 0) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("TP_DELETE_BORDER_MESH_NONE_ERROR"), SM.Get("OK"));
            } else {
                builder.CreateAlert(SM.Get("WARNING"), SM.Get("TP_DELETE_BORDER_MESH_WARNING"), SM.Get("YES"), SM.Get("NO"), delegate { History.UndoManager.Record(SM.Get("TP_REMOVE_BORDER_MESH"), () => RemoveBorderMesh(realCurBorderMesh)); }, null); //the alert action runs outside the button's undo step
            }
        }

        void RemoveBorderMesh(int borderMesh) {
            Operations.TerrainPatches.RemoveBorderMesh(GetCurPatch(), borderMesh);
            builder.NotifyChange();
            UpdateBorderMeshesList();
        }

        void UpdateBorderMeshesList() {
            var tS = TS();
            var borderMeshName = tS.types[tS.curType].typeData.settings.borderMeshName;
            editBMButton.SetInteractable(false);
            if (GetCurPatch(false) == null)  return;
            var options = new List<string>();
            var borderMeshCount = GetCurPatch().GetBorderMeshCount();
            if (borderMeshCount == 0) {
                options.Add(SM.Get("NONE"));
            } else {
                for (int i = 0; i < borderMeshCount; i++) {
                    options.Add(borderMeshName + " " + (i + 1).ToString());
                }
            }
            borderMeshDropdown.SetOptions(options);
            borderMeshDropdown.SetValue(0);
            GetBorderMesh(0);
        }

        ElementPlacer.TerrainPlacer GetPlacer() {
            var modifier = builder.terrainClick.modifier;
            if (modifier != null && modifier is ElementPlacer.TerrainPlacer) {
                return ((ElementPlacer.TerrainPlacer)modifier);
            }
            return null;
        }

        void AutoCloseLoop() {
            if (GetPlacer() != null && GetCurPatch(false) != null) {
                switch (Operations.TerrainPatches.AutoClose(GetCurPatch())) {
                    case Operations.AutoCloseResult.Closed:
                        builder.NotifyChange();
                        break;
                    case Operations.AutoCloseResult.CannotClose:
                        builder.CreateAlert(SM.Get("ERROR"), SM.Get("TP_CANNOT_AUTOCLOSE_ERROR"), SM.Get("OK"));
                        break;
                    default: //0
                        builder.CreateAlert(SM.Get("ERROR"), SM.Get("TP_NO_LOOP_FOUND_ERROR"), SM.Get("OK"));
                        break;
                }
            }
        }

        void LoadPointPlacementMode(int mode) {
            switchToBorderMeshPlacementButton.SetInteractable(true);
            if (GetPlacer() != null && GetCurPatch(false) != null) {
                GetPlacer().placementMode = (ElementPlacer.TerrainPlacer.PlacementMode)mode;
                pointPlacementModeDropdown.SetValue(mode);
                HighlightCurrentBorderMesh();
                GetCurPatch().SetActive(true, mode == 0);
                switchToBorderMeshPlacementButton.SetInteractable(mode != 3);
            }
        }

        void DeleteLastPerimeterPoint() {
            if (GetPlacer() != null && GetCurPatch(false) != null) {
                Operations.TerrainPatches.RemoveLastPerimeterPoint(GetCurPatch());
                builder.NotifyChange();
            }
        }

        void ClearEmptyPatch() {
            if (Operations.TerrainPatches.IsIncomplete(GetCurPatch(false))) {
                Operations.TerrainPatches.Delete(GetCurPatch(false));
                builder.NotifyChange();
            }
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            int curPlacementMode = 0;
            if (active && !keepActive) {
                ClearEmptyPatch();
                pointPlacementModeDropdown.SetValue(1);
                curPlacementMode = 1;
                if (GetCurPatch(false) != null) {
                    SetPlaceNewPoints(false);
                    pointPlacementModeDropdown.SetValue(0);
                    curPlacementMode = 0;
                }
            } else if (!keepActive) {
                if (ActiveSelf()) Terminate(true);
            }
            base.SetActive(active);
            if (active && !keepActive) {
                GetCurPatch()?.SetActive(active, true);
                TS().ReadCurValues(); //a new patch only gets its state now
                UpdateBorderMeshesList();
            }
            if (active) {
                if (GetCurPatch(false) != null) {
                    SetTitle(GO.GetName(GO.GetObject(GetCurPatch(false))));
                }
            }
            if (active && !keepActive) {
                pointPlacementModeDropdown.SetValue(curPlacementMode);
            }
        }

        protected override void ReplaceTitle(string value) {
            var obj = GetCurPatch(false);
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

        public void Delete() {
            Operations.TerrainPatches.Delete(GetCurPatch());
            builder.NotifyChange();
            Terminate();
        }

        public override void Terminate() {
            keepActive = false;
            Terminate(false);
        }

        public void Terminate(bool auto) {
            if (GetPlacer() != null) {
                GetPlacer().placementMode = ElementPlacer.TerrainPlacer.PlacementMode.None;
            }
            GetCurPatch()?.SetActive(false, false);
            ClearEmptyPatch();
            builder.helper.elementManager.ShowAnchors(false);
            if (!auto) SetActive(false);
            GameEngine.Gizmo.ClearTargets();
            if (!auto) builder.SetSelectObjectMode(); //otherwise the terrain placer stays as the click modifier and selection stops working
        }

        TerrainPatch GetCurPatch() {
            return GetCurPatch(true);
        }

        public override object GetUndoTarget() {
            return GetCurPatch(false);
        }

        public TerrainPatch GetPatch() {
            return GetCurPatch(false);
        }

        void SetPlaceNewPoints(bool enabled) {
            var modifier = builder.terrainClick.modifier;
            if (modifier != null && modifier is ElementPlacer.TerrainPlacer) {
                ((ElementPlacer.TerrainPlacer)modifier).placementMode = ElementPlacer.TerrainPlacer.PlacementMode.Perimeter;
            }
            GetCurPatch()?.SetActive(true, !enabled);
        }

        TerrainPatch GetCurPatch(bool createIfNull) {
            var modifier = builder.terrainClick.modifier;
            if (modifier != null && modifier is ElementPlacer.TerrainPlacer) {
                return GS.Cast<TerrainPatch>(((ElementPlacer.TerrainPlacer)modifier).GetPatch(createIfNull));
            } else {
                return null;
            }
        }
    }
}
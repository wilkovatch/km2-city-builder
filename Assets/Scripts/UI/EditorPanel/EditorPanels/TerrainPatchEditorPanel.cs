using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class TerrainPatchEditorPanel : EditorPanel {
        EditorPanelElements.Dropdown pointPlacementModeDropdown, borderMeshDropdown;
        int curBorderMesh = -1;
        TerrainBorderMesh dummyBorderMesh = new TerrainBorderMesh();
        EditorPanelElements.Button switchToBorderMeshPlacementButton, editBMButton;

        public TerrainPatchEditorPanel() {
            AddComplexElement(new TypeSelector<CityElements.Types.Runtime.TerrainPatchType>(this));
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

                p.AddFieldInputField(SM.Get("TP_SMOOTHING"), SM.Get("TP_SMOOTHING_PH"), UnityEngine.UI.InputField.ContentType.IntegerNumber, GetCurPatch, "state.properties.smooth", null, 1.5f);
                p.IncreaseRow();

                p.AddFieldCheckbox(SM.Get("TP_PROJECT_TO_GROUND"), GetCurPatch, "state.properties.projectToGround", null, 1.5f);
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
            var tS = TS();
            var limit = tS.types[tS.curType].typeData.settings.maxBorderMeshes;
            if (limit > 0 && GetCurPatch().GetBorderMeshCount() >= limit) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("TP_TOO_MANY_BORDER_MESHES_ERROR"), SM.Get("OK"));
                return;
            }
            var state = PresetManager.GetPreset("terrainPatchBorderMesh", 0);
            GetCurPatch().AddBorderMesh(state, new ObjectState());
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
                builder.CreateAlert(SM.Get("WARNING"), SM.Get("TP_DELETE_BORDER_MESH_WARNING"), SM.Get("YES"), SM.Get("NO"), delegate { RemoveBorderMesh(realCurBorderMesh); }, null);
            }
        }

        void RemoveBorderMesh(int borderMesh) {
            GetCurPatch().RemoveBorderMesh(borderMesh);
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
                var res = GetCurPatch().AutoClose();
                switch (res.resultCode) {
                    case 1:
                        builder.NotifyChange();
                        break;
                    case -1:
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
                GetCurPatch().RemovePerimeterPoint();
                builder.NotifyChange();
            }
        }

        void ClearEmptyPatch() {
            if (GetCurPatch(false) && GetCurPatch(false).GetPerimeterPointCount() < 3) {
                GetCurPatch(false).Delete();
                builder.NotifyChange();
            }
        }

        public override void SetActive(bool active) {
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
                UpdateBorderMeshesList();
            }
            if (active) {
                if (GetCurPatch(false) != null) {
                    SetTitle(GetCurPatch(false).gameObject.name);
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
                    value = obj.gameObject.name;
                } else {
                    value = "";
                }
            } else {
                if (obj != null) obj.gameObject.name = value;
            }
            base.ReplaceTitle(value);
        }

        public void Delete() {
            GetCurPatch().Delete();
            builder.NotifyChange();
            Terminate();
        }

        public override void Terminate() {
            Terminate(false);
        }

        public void Terminate(bool auto) {
            if (GetPlacer() != null) {
                GetPlacer().placementMode = ElementPlacer.TerrainPlacer.PlacementMode.None;
            }
            GetCurPatch()?.SetActive(false, false);
            ClearEmptyPatch();
            builder.UnsetModifier();
            builder.helper.elementManager.ShowAnchors(false);
            if (!auto) SetActive(false);
            Camera.main.GetComponent<RuntimeGizmos.TransformGizmo>()?.ClearTargets();
        }

        TerrainPatch GetCurPatch() {
            return GetCurPatch(true);
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
            return (modifier != null && modifier is ElementPlacer.TerrainPlacer) ? ((ElementPlacer.TerrainPlacer)modifier).GetPatch(createIfNull)?.GetComponent<TerrainPatch>() : null;
        }
    }
}
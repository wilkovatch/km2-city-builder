using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class RoadEditorPanel : EditorPanel {
        EditorPanelElements.Dropdown pointPlacementModeDropdown;
        Dictionary<string, (EditorPanelElements.Button button, EditorPanelElements.Checkbox checkbox)> propControls = new Dictionary<string, (EditorPanelElements.Button, EditorPanelElements.Checkbox)>();

        public RoadEditorPanel() {
            AddComplexElement(new PresetSelector(this));
            AddComplexElement(new TypeSelector<CityElements.Types.Runtime.RoadType>(this));
        }

        TypeSelector<CityElements.Types.Runtime.RoadType> TS() {
            return GetComplexElement<TypeSelector<CityElements.Types.Runtime.RoadType>>();
        }

        public override void Initialize(GameObject canvas) {
            InitializeWithCustomParameters<CityElements.Types.Runtime.RoadType, CityElements.Types.RoadType>(canvas, GetCurRoad, TS,
                null, CityElements.Types.Parsers.TypeParser.GetRoadTypes, ProcessCustomParts, true);
        }

        bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
            TypeSelector<CityElements.Types.Runtime.RoadType> tS, CityElements.Types.Runtime.RoadType type) {

            var w = elem.width;
            if (elem.name == "mainGroup") {
                //Main
                if (!pS.EditingPreset()) {
                    p.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.35f);
                    p.AddButton(SM.Get("DELETE"), Delete, w * 0.25f);
                    p.AddButton(SM.Get("RD_REMOVE_POINT"), RemovePoint, w * 0.4f);
                } else {
                    p.AddButton(SM.Get("END_EDITING"), Terminate, w);
                }
                p.IncreaseRow();

                tS.AddTypeDropdown(p, SM.Get("ROAD_TYPE"), w);

                System.Func<ObjectState> getter = delegate { return GetCurRoad().state; };
                System.Func<ObjectState, ObjectState> setter = x => { GetCurRoad().SetState((ObjectState)x.Clone()); return GetCurRoad().GetState(); };
                pS.AddPresetLoadAndSaveDropdown(p, SM.Get("ROAD_PRESET"), true, "road", setter, getter, false, null, null, null, w);

                var placementModes = new List<string> { SM.Get("RD_PM_NONE"), SM.Get("RD_PM_ADD"), SM.Get("RD_PM_INSERT") };
                pointPlacementModeDropdown = p.AddDropdown(SM.Get("RD_POINT_PLACEMENT_MODE"), placementModes, LoadPointPlacementMode, w);
                p.IncreaseRow();

                p.AddFieldCheckbox(SM.Get("RD_PROJECT_ALL_TO_GROUND"), GetCurRoad, "state.properties.projectAll", null, w / 3, null);
                p.AddFieldCheckbox(SM.Get("RD_PROJECT_TO_GROUND"), GetCurRoad, "state.properties.project", null, w / 3, null);
                p.AddFieldCheckbox(SM.Get("RD_SEGMENTS_100M"), GetCurRoad, "state.properties.segmentsPer100m", null, w / 3, null);
                p.IncreaseRow();

                p.AddFieldInputField(SM.Get("SEGMENTS"), SM.Get("SEGMENTS_PH"), UnityEngine.UI.InputField.ContentType.IntegerNumber, GetCurRoad, "state.properties.segments", null, w / 2, null);
                p.AddFieldInputField(SM.Get("RD_SMOOTHING"), SM.Get("RD_SMOOTHING_PH"), UnityEngine.UI.InputField.ContentType.IntegerNumber, GetCurRoad, "state.properties.lpf", null, w / 2, null);
                p.IncreaseRow();

                var withCoplanar = type.InternalParameterVisible("makeCoplanar");
                if (withCoplanar) p.AddFieldCheckbox(SM.Get("RD_MAKE_COPLANAR"), GetCurRoad, "state.properties.makeCoplanar", null, w / 2, SM.Get("RD_COPLANAR_TOOLTIP"));
                p.AddFieldCheckbox(SM.Get("RD_ADJUST_LOWPOLY_WIDTH"), GetCurRoad, "state.properties.adjustLowPolyWidth", null, w / (1 + (withCoplanar ? 1.0f : 0.0f)));
                p.IncreaseRow();

                var curveTypes = new List<string> { SM.Get("RD_SP_T_BEZIER"), SM.Get("RD_SP_T_HERMITE"), SM.Get("RD_SP_T_LOWPOLY") };
                p.AddFieldDropdown(SM.Get("RD_SPLINE_TYPE"), curveTypes, GetCurRoad, "state.properties.curveType", null, w * 0.4f, null);
                p.AddFieldCheckbox(SM.Get("RD_SUBDIVIDE_EQUALLY"), GetCurRoad, "state.properties.subdivideEqually", null, w * 0.35f, null);
                p.AddFieldInputField(SM.Get("RD_HERMITE_TENSION"), SM.Get("RD_HERMITE_TENSION_PH"), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurRoad, "state.properties.hermiteTension", null, w * 0.25f, null);
                p.IncreaseRow();

            } else if (elem.name == "intersectionGroup") {
                //Intersection
                p.AddLabel(SM.Get("IS_CW_SIZE"), w / 2, null, null, 0.4f);
                p.AddLabel(SM.Get("IS_SIZE_INCREASE"), w / 2, null, null, 0.4f);
                p.IncreaseRow(0.4f);
                p.AddFieldInputField(SM.Get("END"), SM.Get("SIZE_PH"), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurRoad, "state.properties.endCrosswalkSize", null, w / 4, null);
                p.AddFieldInputField(SM.Get("START"), SM.Get("SIZE_PH"), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurRoad, "state.properties.startCrosswalkSize", null, w / 4, null);
                p.AddFieldInputField(SM.Get("END"), SM.Get("SIZE_PH"), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurRoad, "state.properties.endIntersectionAdd", null, w / 4, null);
                p.AddFieldInputField(SM.Get("START"), SM.Get("SIZE_PH"), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurRoad, "state.properties.startIntersectionAdd", null, w / 4, null);
                p.IncreaseRow(1.2f);

                p.AddLabel(SM.Get("RD_IS_TEXS"), w, null, null, 0.4f);
                p.IncreaseRow(0.4f);
                p.AddFieldTextureField(builder, SM.Get("RD_IS_END_TEX"), SM.Get("TEXTURE_PH"), GetCurRoad, "state.properties.endIntersectionTexture", null, w / 2, null);
                p.AddFieldTextureField(builder, SM.Get("RD_IS_START_TEX"), SM.Get("TEXTURE_PH"), GetCurRoad, "state.properties.startIntersectionTexture", null, w / 2, null);
                p.IncreaseRow();

                p.AddFieldCheckbox(SM.Get("RD_IS_RD_EIE"), GetCurRoad, "instanceState.properties.endIntersectionEnd", null, w / 4, null);
                p.AddFieldCheckbox(SM.Get("RD_IS_RD_EIS"), GetCurRoad, "instanceState.properties.endIntersectionStart", null, w / 4, null);
                p.AddFieldCheckbox(SM.Get("RD_IS_RD_SIE"), GetCurRoad, "instanceState.properties.startIntersectionEnd", null, w / 4, null);
                p.AddFieldCheckbox(SM.Get("RD_IS_RD_SIS"), GetCurRoad, "instanceState.properties.startIntersectionStart", null, w / 4, null);
                p.IncreaseRow();

                p.AddFieldInputField(SM.Get("RD_IS_MOVE"), "move", UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurRoad, "instanceState.properties.intersectionMove", null, w, null);
                p.IncreaseRow();

            } else if (elem.name == "propsGroup") {
                //Props
                var types = new List<string> { GetCurType() };
                pS.AddPresetLoadAndSaveDropdown(p, SM.Get("RD_PROP_RULE_PRESET"), true, "roadPropRule", SetPropRule, GetPropRule, true, x => { var obj = GetPropRule(true); obj.Name = x; SetPropRule(obj); }, types, null, w);
                propControls.Clear();
                foreach (var containerType in type.typeData.settings.propsContainers) {
                    var cb = p.AddCheckbox(SM.Get("RD_PROP_ENABLED"), false, x => { EnablePropContainer(containerType, x); }, w * 0.3f);
                    var btn = p.AddButton(SM.Get("EDIT_PROP_CONTAINER_" + containerType.ToUpper()), delegate { EditPropContainer(containerType); }, w * 0.7f);
                    propControls.Add(containerType, (btn, cb));
                    p.IncreaseRow();
                }

            } else {
                return false;
            }
            return true;
        }

        string GetCurType() {
            var road = GetCurRoad(false);
            if (road != null) {
                return road.state.Str("type");
            }
            return null;
        }

        ElementPlacer.RoadPlacer GetPlacer() {
            var modifier = builder.terrainClick.modifier;
            if (modifier != null && modifier is ElementPlacer.RoadPlacer) {
                return ((ElementPlacer.RoadPlacer)modifier);
            }
            return null;
        }

        void SetPlacePoint(bool value) {
            GetCurRoad()?.SetPointsMoveable(!value);
            builder.helper.elementManager.ShowIntersections(value);
            GetCurRoad()?.SetActive(true);
        }

        void ClearEmptyRoad() {
            var pS = GetComplexElement<PresetSelector>();
            if (pS != null && pS.EditingPreset() && keepActive) return;
            if (GetCurRoad(false) && GetCurRoad(false).points.Count < 2) {
                GetCurRoad(false).Delete();
                builder.NotifyChange();
            }
        }

        ObjectState GetPropRule() {
            return GetPropRule(false);
        }

        ObjectState GetPropRule(bool createIfNull) {
            var res = GetCurRoad().state.State("propRule");
            if (res == null && createIfNull) {
                res = new ObjectState(true);
                SetPropRule(res);
            }
            return res;
        }

        ObjectState SetPropRule(ObjectState state) {
            GetCurRoad(true).state.SetState("propRule", state);
            ReadCurValues();
            builder.NotifyChange();
            return state;
        }

        void EnablePropContainer(string name, bool enabled) {
            if (enabled) {
                GetPropRule(true).SetState(name, new ObjectState(true));
                GetCurRoad().state.FlagAsChanged();
            } else {
                GetPropRule(true).SetState(name, null);
                GetCurRoad().state.FlagAsChanged();
            }
            ReadCurValues();
            builder.NotifyChange();
        }

        protected override void ReadCurValues() {
            base.ReadCurValues();
            foreach (var key in propControls.Keys) {
                var rule = GetPropRule();
                var enabled = rule != null && rule.State(key) != null;
                propControls[key].button.SetInteractable(enabled);
                var cb = propControls[key].checkbox;
                cb.actionEnabled = false;
                cb.SetValue(enabled);
                cb.actionEnabled = true;
            }
        }

        void EditPropContainer(string name) {
            var panel = builder.propContainerEditorPanel;
            panel.SetContainer(GetPropRule().State(name), x => { GetPropRule().SetState(name, x); });
            panel.parentPanel = this;
            panel.parentState = GetCurRoad(false)?.state;
            Hide(true);
            panel.SetActive(true);
        }

        public void SetActive(bool active, bool withIntersections) {
            string lastPreset = null;
            bool noRoad = false;
            var pS = GetComplexElement<PresetSelector>();
            if (active) {
                noRoad = !GetCurRoad(false) || GetCurRoad(false).points.Count < 2;
                ClearEmptyRoad();
                lastPreset = pS.dropdowns["road"][0].lastPreset;
            } else {
                if (ActiveSelf() && !keepActive) Terminate(true);
            }
            base.SetActive(active);
            if (active && !keepActive) {
                pS.dropdowns["road"][0].lastPreset = lastPreset;
                System.Func<ObjectState, ObjectState> setter = x => { GetCurRoad().SetState((ObjectState)x.Clone()); return GetCurRoad().GetState(); };
                pS.LoadPreset(noRoad && !pS.EditingPreset() ? lastPreset : null, "road", setter, 0);
                builder.helper.elementManager.ShowIntersections(withIntersections);
                GetCurRoad()?.SetActive(active);
                //propDropdown.SetOptions(GetProps());
                var placementMode = GetPlacer() != null ? ((int)GetPlacer().placementMode) : 0;
                pointPlacementModeDropdown.SetValue(placementMode);
            }
            if (active) {
                if (pS.EditingPreset()) {
                    SetTitle(SM.Get("PRESET"), false);
                } else if (GetCurRoad(false) != null) {
                    SetTitle(GetCurRoad(false).gameObject.name);
                }
            }
        }

        protected override void ReplaceTitle(string value) {
            var obj = GetCurRoad(false);
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

        void LoadPointPlacementMode(int mode) {
            if (GetPlacer() != null && GetCurRoad(false) != null) {
                GetPlacer().placementMode = (ElementPlacer.RoadPlacer.PlacementMode)mode;
                pointPlacementModeDropdown.SetValue(mode);
            }
        }

        public override void SetActive(bool active) {
            SetActive(active, false);
        }

        public void Delete() {
            Delete(false);
        }

        public void Delete(bool deletePointIfSelected) {
            if (deletePointIfSelected && builder.gizmo.mainTargetRoot != null && GetCurRoad(false) != null && GetCurRoad(false).points.Count > 2) {
                var point = builder.gizmo.mainTargetRoot.gameObject;
                if (GetCurRoad().points.Contains(point)) {
                    GetCurRoad().points.Remove(point);
                    Object.Destroy(point);
                    builder.NotifyChange();
                    return;
                }
            }
            GetCurRoad().Delete();
            builder.NotifyChange();
            Terminate();
        }

        void RemovePoint() {
            GetCurRoad()?.RemovePoint();
            builder.NotifyChange();
        }

        public override void Terminate() {
            keepActive = false;
            Terminate(false);
        }

        public void Terminate(bool auto) {
            if (GetPlacer() != null) GetPlacer().placementMode = ElementPlacer.RoadPlacer.PlacementMode.None;
            SetPlacePoint(false);
            ClearEmptyRoad();
            GetCurRoad()?.SetActive(false);
            builder.UnsetModifier();
            builder.helper.elementManager.ShowIntersections(false);
            if (!auto) SetActive(false);
            Camera.main.GetComponent<RuntimeGizmos.TransformGizmo>()?.ClearTargets();
        }

        Road GetCurRoad() {
            return GetCurRoad(true);
        }

        Road GetCurRoad(bool createIfNull) {
            if (builder == null || builder.terrainClick == null) return null;
            var modifier = builder.terrainClick.modifier;
            if (modifier is TerrainModifier.Null) return builder.helper.elementManager.GetDummy<Road>();
            else return (modifier != null && modifier is ElementPlacer.RoadPlacer) ? ((ElementPlacer.RoadPlacer)modifier).GetRoad(createIfNull)?.GetComponent<Road>() : null;
        }
    }
}
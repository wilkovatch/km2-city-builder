using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class IntersectionEditorPanel : EditorPanel {
        public Intersection intersection = null;

        public IntersectionEditorPanel() {
            AddComplexElement(new PresetSelector(this));
            AddComplexElement(new TypeSelector<CityElements.Types.Runtime.IntersectionType>(this));
        }

        TypeSelector<CityElements.Types.Runtime.IntersectionType> TS() {
            return GetComplexElement<TypeSelector<CityElements.Types.Runtime.IntersectionType>>();
        }

        public override void Initialize(GameObject canvas) {
            InitializeWithCustomParameters<CityElements.Types.Runtime.IntersectionType, CityElements.Types.IntersectionType>(canvas, GetCurIntersection, TS,
                null, CityElements.Types.Parsers.TypeParser.GetIntersectionTypes, ProcessCustomParts, true);
        }

        bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
            TypeSelector<CityElements.Types.Runtime.IntersectionType> tS, CityElements.Types.Runtime.IntersectionType type) {

            var w = elem.width;
            if (elem.name == "mainGroup") {
                if (!pS.EditingPreset()) {
                    p.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.5f);
                    p.AddButton(SM.Get("DELETE"), Delete, w * 0.5f);
                } else {
                    p.AddButton(SM.Get("END_EDITING"), Terminate, w);
                }
                p.IncreaseRow();

                tS.AddTypeDropdown(p, SM.Get("INTERSECTION_TYPE"), w);

                System.Func<ObjectState> getter = delegate { return GetCurIntersection().state; };
                System.Func<ObjectState, ObjectState> setter = x => { GetCurIntersection().state = (ObjectState)x.Clone(); return GetCurIntersection().state; };
                pS.AddPresetLoadAndSaveDropdown(p, SM.Get("INTERSECTION_PRESET"), false, "intersection", setter, getter, false);

                p.AddFieldInputField(SM.Get("RD_SIDEWALK_SEGMENTS"), SM.Get("SEGMENTS_PH"), UnityEngine.UI.InputField.ContentType.IntegerNumber, GetCurIntersection, "state.properties.sidewalkSegments", null, w * 0.5f, null);
                p.AddFieldInputField(SM.Get("IS_SIZE_INCREASE"), SM.Get("SIZE_PH"), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurIntersection, "state.properties.sizeIncrease", null, w * 0.5f, null);
                p.IncreaseRow();
            } else {
                return false;
            }
            return true;
        }

        ElementPlacer.RoadPlacer GetPlacer() {
            var modifier = builder.terrainClick.modifier;
            if (modifier != null && modifier is ElementPlacer.RoadPlacer) {
                return ((ElementPlacer.RoadPlacer)modifier);
            }
            return null;
        }

        public override void SetActive(bool active) {
            var pS = GetComplexElement<PresetSelector>();
            if (active) {
            } else {
                if (ActiveSelf()) Terminate(true);
            }
            base.SetActive(active);
            if (active && !keepActive) {
                builder.helper.elementManager.ShowIntersections(false);
                GetCurIntersection()?.SetActive(active, true, true);
            }
            if (active) {
                if (pS.EditingPreset()) {
                    SetTitle(SM.Get("PRESET"), false);
                } else if (GetCurIntersection() != null) {
                    SetTitle(GetCurIntersection().geo.name);
                }
            }
        }

        protected override void ReplaceTitle(string value) {
            var obj = GetCurIntersection();
            if (value == null || value == "") {
                if (obj != null) {
                    value = obj.geo.name;
                } else {
                    value = "";
                }
            } else {
                if (obj != null) obj.geo.name = value;
            }
            base.ReplaceTitle(value);
        }

        public void Delete() {
            GetCurIntersection().Delete();
            builder.NotifyChange();
            Terminate();
        }

        public override void Terminate() {
            Terminate(false);
        }

        public void Terminate(bool auto) {
            if (GetPlacer() != null) GetPlacer().placementMode = ElementPlacer.RoadPlacer.PlacementMode.None;
            GetCurIntersection()?.SetActive(false);
            builder.UnsetModifier();
            builder.helper.elementManager.ShowIntersections(false);
            if (!auto) SetActive(false);
            Camera.main.GetComponent<RuntimeGizmos.TransformGizmo>()?.ClearTargets();
        }

        Intersection GetCurIntersection() {
            var modifier = builder.terrainClick.modifier;
            if (modifier is TerrainModifier.Null) return builder.helper.elementManager.GetDummyIntersection();
            else return intersection;
        }
    }
}
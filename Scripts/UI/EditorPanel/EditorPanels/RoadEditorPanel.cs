using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;

namespace EditorPanels {
    public class RoadEditorPanel : EditorPanel, IPropRuleEditor {
        EditorPanelElements.Dropdown pointPlacementModeDropdown, curveTypeDropdown;
        int[] allowedCurves;
        System.Action updateCurveFields;
        EditorPanelElements.Button startIntersectionButton, endIntersectionButton;
        readonly PropRuleGroup propRule;
        readonly Dictionary<string, (EditorPanelElements.Button button, EditorPanelElements.Checkbox checkbox)> propControls;

        public RoadEditorPanel() {
            propRule = new PropRuleGroup(this, create => GetCurRoad(create)?.state, () => {
                ReadCurValues();
                builder.NotifyChange();
            });
            propControls = propRule.controls;
            AddComplexElement(new PresetSelector(this));
            AddComplexElement(new TypeSelector<CityElements.Types.Runtime.RoadType>(this) { stateGetter = () => GetCurRoad(false)?.state });
        }

        TypeSelector<CityElements.Types.Runtime.RoadType> TS() {
            return GetComplexElement<TypeSelector<CityElements.Types.Runtime.RoadType>>();
        }

        public override void Initialize(GameObject canvas) {
            propControls.Clear();
            curveTypeDropdown = null; updateCurveFields = null;
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

                var placementModes = new List<string> { SM.Get("RD_PM_NONE"), SM.Get("RD_PM_ADD"), SM.Get("RD_PM_ADD_BEFORE_FIRST"),
                    SM.Get("RD_PM_INSERT"), SM.Get("RD_PM_INSERT_BEFORE"), SM.Get("RD_PM_SPLIT") };
                pointPlacementModeDropdown = p.AddDropdown(SM.Get("RD_POINT_PLACEMENT_MODE"), placementModes, LoadPointPlacementMode, w);
                if (pS.EditingPreset()) pointPlacementModeDropdown.SetInteractable(false);
                p.IncreaseRow();

                p.AddFieldCheckbox(SM.Get("RD_PROJECT_ALL_TO_GROUND"), GetCurRoad, "state.properties.projectAll", null, w / 3, null, null, PlacementHeight.OnProjectionToggled);
                p.AddFieldCheckbox(SM.Get("RD_PROJECT_TO_GROUND"), GetCurRoad, "state.properties.project", null, w / 3, null, null, PlacementHeight.OnProjectionToggled);
                p.AddFieldCheckbox(SM.Get("RD_SEGMENTS_100M"), GetCurRoad, "state.properties.segmentsPer100m", null, w / 3, null);
                p.IncreaseRow();

                p.AddFieldInputField(SM.Get("SEGMENTS"), SM.Get("SEGMENTS_PH"), GameEngine.InputContentType.Integer, GetCurRoad, "state.properties.segments", null, w / 2, null);
                p.AddFieldInputField(SM.Get("RD_SMOOTHING"), SM.Get("RD_SMOOTHING_PH"), GameEngine.InputContentType.Integer, GetCurRoad, "state.properties.lpf", null, w / 2, null);
                p.IncreaseRow();

                var withCoplanar = type.InternalParameterVisible("makeCoplanar");
                if (withCoplanar) p.AddFieldCheckbox(SM.Get("RD_MAKE_COPLANAR"), GetCurRoad, "state.properties.makeCoplanar", null, w / 2, SM.Get("RD_COPLANAR_TOOLTIP"));
                p.AddFieldCheckbox(SM.Get("RD_ADJUST_LOWPOLY_WIDTH"), GetCurRoad, "state.properties.adjustLowPolyWidth", null, w / (1 + (withCoplanar ? 1.0f : 0.0f)));
                p.IncreaseRow();

                var allowed = allowedCurves = SplineCurves.Allowed(type.typeData.settings);
                var entries = new List<int>(allowed).ConvertAll(i => SM.Get(SplineCurves.Label(i)));
                var curveDropdown = curveTypeDropdown = p.AddDropdown(SM.Get("RD_SPLINE_TYPE"), entries, i => {
                    GetCurRoad().state.SetInt("curveType", allowed[i]);
                    builder.NotifyChange();
                    Update();
                }, w * 0.4f);
                //no road created while building the panel (with no road to edit yet), Update sets it later
                var curRoad = GetCurRoad(false);
                if (curRoad != null) {
                    curveDropdown.actionEnabled = false;
                    curveDropdown.SetValue(System.Array.IndexOf(allowed, curRoad.state.Int("curveType")));
                    curveDropdown.actionEnabled = true;
                }
                if (System.Array.IndexOf(allowed, 1) >= 0) {
                    var equallyField = p.AddFieldCheckbox(SM.Get("RD_SUBDIVIDE_EQUALLY"), GetCurRoad, "state.properties.subdivideEqually", null, w * 0.35f, null);
                    var tensionField = p.AddFieldInputField(SM.Get("RD_HERMITE_TENSION"), SM.Get("RD_HERMITE_TENSION_PH"), GameEngine.InputContentType.Decimal, GetCurRoad, "state.properties.hermiteTension", null, w * 0.25f, null);
                    updateCurveFields = () => {
                        var builtin = GetCurRoad(false)?.state.Int("curveType") == 1;
                        equallyField.SetActive(builtin); tensionField.SetActive(builtin);
                    };
                }
                p.IncreaseRow();

            } else if (elem.name == "intersectionGroup") {
                //Intersection
                endIntersectionButton = null;
                startIntersectionButton = null;
                if (!pS.EditingPreset()) {
                    var tooltip = SM.Get("RD_EDIT_INTERSECTION_TOOLTIP");
                    endIntersectionButton = p.AddButton(SM.Get("RD_EDIT_END_INTERSECTION"), () => EditIntersection(false), w / 2, tooltip);
                    startIntersectionButton = p.AddButton(SM.Get("RD_EDIT_START_INTERSECTION"), () => EditIntersection(true), w / 2, tooltip);
                    p.IncreaseRow();
                }

                p.AddLabel(SM.Get("IS_CW_SIZE"), w / 2, null, null, 0.4f);
                p.AddLabel(SM.Get("IS_SIZE_INCREASE"), w / 2, null, null, 0.4f);
                p.IncreaseRow(0.4f);
                p.AddFieldInputField(SM.Get("END"), SM.Get("SIZE_PH"), GameEngine.InputContentType.Decimal, GetCurRoad, "state.properties.endCrosswalkSize", null, w / 4, null);
                p.AddFieldInputField(SM.Get("START"), SM.Get("SIZE_PH"), GameEngine.InputContentType.Decimal, GetCurRoad, "state.properties.startCrosswalkSize", null, w / 4, null);
                p.AddFieldInputField(SM.Get("END"), SM.Get("SIZE_PH"), GameEngine.InputContentType.Decimal, GetCurRoad, "state.properties.endIntersectionAdd", null, w / 4, null);
                p.AddFieldInputField(SM.Get("START"), SM.Get("SIZE_PH"), GameEngine.InputContentType.Decimal, GetCurRoad, "state.properties.startIntersectionAdd", null, w / 4, null);
                p.IncreaseRow(1.2f);

                p.AddLabel(SM.Get("RD_IS_TEXS"), w, null, null, 0.4f);
                p.IncreaseRow(0.4f);
                p.AddFieldTextureField(builder, SM.Get("RD_IS_END_TEX"), SM.Get("TEXTURE_PH"), GetCurRoad, "state.properties.endIntersectionTexture", null, w / 2, null);
                p.AddFieldTextureField(builder, SM.Get("RD_IS_START_TEX"), SM.Get("TEXTURE_PH"), GetCurRoad, "state.properties.startIntersectionTexture", null, w / 2, null);
                p.IncreaseRow();

                var instanceFields = new List<EditorPanelElement> {
                    p.AddFieldCheckbox(SM.Get("RD_IS_RD_EIE"), GetCurRoad, "instanceState.properties.endIntersectionEnd", null, w / 4, null),
                    p.AddFieldCheckbox(SM.Get("RD_IS_RD_EIS"), GetCurRoad, "instanceState.properties.endIntersectionStart", null, w / 4, null),
                    p.AddFieldCheckbox(SM.Get("RD_IS_RD_SIE"), GetCurRoad, "instanceState.properties.startIntersectionEnd", null, w / 4, null),
                    p.AddFieldCheckbox(SM.Get("RD_IS_RD_SIS"), GetCurRoad, "instanceState.properties.startIntersectionStart", null, w / 4, null)
                };
                p.IncreaseRow();

                instanceFields.Add(p.AddFieldInputField(SM.Get("RD_IS_MOVE"), "move", GameEngine.InputContentType.Decimal, GetCurRoad, "instanceState.properties.intersectionMove", null, w, null));
                p.IncreaseRow();

                p.AddLabel(SM.Get("RD_IS_CONN_HEIGHT"), w, null, null, 0.4f);
                p.IncreaseRow(0.4f);
                var autoTooltip = SM.Get("RD_IS_CONN_AUTO_TOOLTIP");
                instanceFields.Add(p.AddFieldCheckbox(SM.Get("RD_IS_CONN_AUTO"), GetCurRoad, "instanceState.properties.endConnectionHeightAuto", null, w / 4, autoTooltip));
                instanceFields.Add(p.AddFieldInputField(SM.Get("END"), SM.Get("HEIGHT_PH"), GameEngine.InputContentType.Decimal, GetCurRoad, "instanceState.properties.endConnectionHeight", null, w / 4, null));
                instanceFields.Add(p.AddFieldCheckbox(SM.Get("RD_IS_CONN_AUTO"), GetCurRoad, "instanceState.properties.startConnectionHeightAuto", null, w / 4, autoTooltip));
                instanceFields.Add(p.AddFieldInputField(SM.Get("START"), SM.Get("HEIGHT_PH"), GameEngine.InputContentType.Decimal, GetCurRoad, "instanceState.properties.startConnectionHeight", null, w / 4, null));
                p.IncreaseRow(1.2f);

                //presets never store instance specific values
                if (pS.EditingPreset()) {
                    foreach (var field in instanceFields) field.SetInteractable(false);
                }

            } else if (elem.name == "propsGroup") {
                //Props
                propRule.Add(p, pS, type.typeData.settings.propsContainers, new List<string> { GetCurType() }, w);

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

        //no new road if there is none (or the empty one was just deleted)
        void SetPlacePoint(bool value) {
            var road = GetCurRoad(false);
            road?.SetPointsMoveable(!value);
            builder.helper.elementManager.ShowIntersections(value);
            road?.SetActive(true);
        }

        void ClearEmptyRoad() {
            var pS = GetComplexElement<PresetSelector>();
            if (pS != null && pS.EditingPreset() && keepActive) return;
            if (Operations.Roads.IsIncomplete(GetCurRoad(false))) {
                Operations.Roads.Delete(GetCurRoad(false));
                builder.NotifyChange();
            }
        }

        protected override void ReadCurValues() {
            base.ReadCurValues();
            Update();
            propRule.Read();
        }

        public EditorPanel OpenPropContainer(string name) {
            return propRule.OpenContainer(name);
        }

        public void SetActive(bool active, bool withIntersections) {
            if (!PrepareActivation(active)) return;
            string lastPreset = null;
            bool noRoad = false;
            var pS = GetComplexElement<PresetSelector>();
            if (active) {
                noRoad = GetCurRoad(false) == null || GetCurRoad(false).points.Count < 2;
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
                TS().ReadCurValues(); //a new road only gets its state now
                //propDropdown.SetOptions(GetProps());
                var placementMode = GetPlacer() != null ? ((int)GetPlacer().placementMode) : 0;
                pointPlacementModeDropdown.SetValue(placementMode);
            }
            if (active) {
                if (pS.EditingPreset()) {
                    SetTitle(SM.Get("PRESET"), false);
                } else if (GetCurRoad(false) != null) {
                    SetTitle(GameEngine.GameObject.GetName(GetCurRoad(false)));
                }
            }
        }

        protected override void ReplaceTitle(string value) {
            var obj = GetCurRoad(false);
            if (value == null || value == "") {
                if (obj != null) {
                    value = GameEngine.GameObject.GetName(obj);
                } else {
                    value = "";
                }
            } else {
                if (obj != null) GameEngine.GameObject.SetName(obj, value);
            }
            base.ReplaceTitle(value);
        }

        void LoadPointPlacementMode(int mode) {
            if (GetPlacer() != null && GetCurRoad(false) != null) {
                var placementMode = (ElementPlacer.RoadPlacer.PlacementMode)mode;
                GetPlacer().placementMode = placementMode;
                pointPlacementModeDropdown.SetValue(mode);
                //the intersection points (other roads' free ends and existing intersections) must be visible and clickable while placing points
                builder.helper.elementManager.ShowIntersections(placementMode == ElementPlacer.RoadPlacer.PlacementMode.Add
                    || placementMode == ElementPlacer.RoadPlacer.PlacementMode.AddBeforeFirst);
                GetCurRoad().markFirstPoint = placementMode == ElementPlacer.RoadPlacer.PlacementMode.AddBeforeFirst;
                GetCurRoad().SetActive(true);
            }
        }

        public void SetPointPlacementMode(int mode) {
            LoadPointPlacementMode(mode);
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            SetActive(active, false);
        }

        public void Delete() {
            Delete(false);
        }

        public bool Delete(bool deletePointIfSelected) {
            if (deletePointIfSelected) {
                var point = builder.gizmo.mainTargetRoot as Godot.StaticBody3D;
                if (Operations.Roads.CanRemoveControlPoint(GetCurRoad(false), point)) {
                    Operations.Roads.RemoveControlPoint(GetCurRoad(), point);
                    builder.NotifyChange();
                    return false;
                }
            }
            Operations.Roads.Delete(GetCurRoad());
            builder.NotifyChange();
            Terminate();
            return true;
        }

        //from the end the points are added to
        void RemovePoint() {
            if (GetPlacer()?.placementMode == ElementPlacer.RoadPlacer.PlacementMode.AddBeforeFirst) {
                Operations.Roads.RemoveFirstPoint(GetCurRoad());
            } else {
                Operations.Roads.RemoveLastPoint(GetCurRoad());
            }
            builder.NotifyChange();
        }

        //the intersection the start or the end of the road is on, if any
        Intersection GetIntersection(bool atStart) {
            var road = GetCurRoad(false);
            var intersection = road == null ? null : atStart ? road.startIntersection : road.endIntersection;
            return intersection != null && !intersection.IsDeleted() ? intersection : null;
        }

        //switches to the editor of the intersection the start or the end of the road is on
        void EditIntersection(bool atStart) {
            var intersection = GetIntersection(atStart);
            if (intersection == null) return;
            Terminate();
            builder.SelectObject(intersection.geo, true, null);
        }

        //the road can be connected or detached while the editor is open
        public override void Update() {
            if (curveTypeDropdown != null && GetCurRoad(false) is Road road) {
                curveTypeDropdown.actionEnabled = false;
                curveTypeDropdown.SetValue(System.Array.IndexOf(allowedCurves, road.state.Int("curveType")));
                curveTypeDropdown.actionEnabled = true;
            }
            updateCurveFields?.Invoke();
            startIntersectionButton?.SetInteractable(GetIntersection(true) != null);
            endIntersectionButton?.SetInteractable(GetIntersection(false) != null);
        }

        public override void Terminate() {
            keepActive = false;
            Terminate(false);
        }

        public void Terminate(bool auto) {
            if (GetPlacer() != null) GetPlacer().placementMode = ElementPlacer.RoadPlacer.PlacementMode.None;
            var road = GetCurRoad(false);
            if (road != null) road.markFirstPoint = false;
            SetPlacePoint(false);
            ClearEmptyRoad();
            GetCurRoad(false)?.SetActive(false); //not a new road in place of the empty one just deleted
            builder.helper.elementManager.ShowIntersections(false);
            if (!auto) SetActive(false);
            GameEngine.Gizmo.ClearTargets();
            if (!auto) builder.SetSelectObjectMode(); //otherwise the road placer stays as the click modifier and selection stops working
        }

        Road GetCurRoad() {
            return GetCurRoad(true);
        }

        public override object GetUndoTarget() {
            return GetCurRoad(false);
        }

        Road GetCurRoad(bool createIfNull) {
            if (builder == null || builder.terrainClick == null) return null;
            var modifier = builder.terrainClick.modifier;
            if (modifier is TerrainModifier.Null) {
                return builder.helper.elementManager.GetDummy<Road>();
            }
            else {
                if (modifier != null && modifier is ElementPlacer.RoadPlacer) {
                    return ((ElementPlacer.RoadPlacer)modifier).GetRoad(createIfNull);
                }
                else {
                    return null;
                }
            }
        }
    }
}
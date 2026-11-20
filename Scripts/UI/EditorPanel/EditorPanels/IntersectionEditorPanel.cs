using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;

namespace EditorPanels {
    public class IntersectionEditorPanel : EditorPanel {
        public Intersection intersection = null;
        readonly List<EditorPanelElements.Button> mergeButtons = new List<EditorPanelElements.Button>();
        EditorPanelElements.Checkbox seamlessCheckbox;

        public IntersectionEditorPanel() {
            AddComplexElement(new PresetSelector(this));
            AddComplexElement(new TypeSelector<CityElements.Types.Runtime.IntersectionType>(this) { stateGetter = () => builder?.terrainClick != null ? GetCurIntersection()?.state : null });
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
                mergeButtons.Clear();
                //the sidewalk elements of the previous type (the pages are made again)
                ClearOutlines();
                sidewalksPage = null;
                sidewalkList = null;
                swOverrideCheckbox = null;
                listedSidewalks = new List<int>();
                if (!pS.EditingPreset()) {
                    p.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.5f);
                    p.AddButton(SM.Get("DELETE"), Delete, w * 0.5f);
                    p.IncreaseRow();
                    //one for each road (the titles are set when the intersection is shown)
                    for (int i = 0; i < 2; i++) {
                        var index = i;
                        mergeButtons.Add(p.AddButton(SM.Get("IS_MERGE"), () => Merge(index), w * 0.5f, SM.Get("IS_MERGE_TOOLTIP")));
                    }
                } else {
                    p.AddButton(SM.Get("END_EDITING"), Terminate, w);
                }
                p.IncreaseRow();

                tS.AddTypeDropdown(p, SM.Get("INTERSECTION_TYPE"), w);

                System.Func<ObjectState> getter = delegate { return GetCurIntersection().state; };
                System.Func<ObjectState, ObjectState> setter = x => { GetCurIntersection().state = (ObjectState)x.Clone(); return GetCurIntersection().state; };
                pS.AddPresetLoadAndSaveDropdown(p, SM.Get("INTERSECTION_PRESET"), false, "intersection", setter, getter, false);

                //the types without the sidewalks groups (see below) have the sidewalk segments here
                if (!HasUIElement(type, "sidewalksGroup")) {
                    p.AddFieldInputField(SM.Get("RD_SIDEWALK_SEGMENTS"), SM.Get("SEGMENTS_PH"), GameEngine.InputContentType.Integer, GetCurIntersection, "state.properties.sidewalkSegments", null, w * 0.5f, null);
                }
                p.AddFieldInputField(SM.Get("IS_SIZE_INCREASE"), SM.Get("SIZE_PH"), GameEngine.InputContentType.Decimal, GetCurIntersection, "state.properties.sizeIncrease", null, w * 0.5f, null);
                p.IncreaseRow();

                //presets never store instance specific values
                seamlessCheckbox = p.AddFieldCheckbox(SM.Get("IS_SEAMLESS"), GetCurIntersection, "instanceState.properties.seamless", null, w, SM.Get("IS_SEAMLESS_TOOLTIP"));
                if (pS.EditingPreset()) seamlessCheckbox.SetInteractable(false);
                p.IncreaseRow();
            } else if (elem.name == "sidewalksGroup") {
                //the segments of all the sidewalks (in rows of their own, after any element of the core)
                p.EnsureNewRow();
                p.AddLabel(SM.Get("IS_SW_GENERAL"), w);
                p.IncreaseRow();
                AddSidewalkFields(p, w, "state.properties.sidewalkSegments", "state.properties.sidewalkOuterSegments", "state.properties.sidewalkOuterSkipsCrosswalks", null);
            } else if (elem.name == "sidewalkListGroup") {
                //the sidewalks, each with its own segments if overridden (never in presets)
                sidewalksPage = p;
                listedSidewalks = new List<int>();
                p.EnsureNewRow();
                sidewalkList = p.AddScrollList(SM.Get("IS_SW_LIST"), new List<string>(), SelectSidewalk, w, SM.Get("IS_SW_LIST_TOOLTIP"), null, SM.Get("IS_SW_LIST_EMPTY"));
                p.IncreaseRow(5.0f);
                p.AddLabel(SM.Get("IS_SW_SELECTED"), w);
                p.IncreaseRow();
                swOverrideCheckbox = p.AddCheckbox(SM.Get("IS_SW_OVERRIDE"), false, SetSidewalkOverridden, w, SM.Get("IS_SW_OVERRIDE_TOOLTIP"));
                p.IncreaseRow();
                var swIndex = new Dictionary<string, System.Func<string>> { { "sw", () => GetSidewalkOverrideIndex().ToString() } };
                (swInnerField, swOuterField, swSkipCheckbox) = AddSidewalkFields(p, w, "", "", "", swIndex);
                if (pS.EditingPreset()) sidewalkList.SetInteractable(false);
                RefreshSidewalkList();
            } else {
                return false;
            }
            return true;
        }

        //the three segment fields (inner, outer, outer skips the crosswalks), on the given paths
        (EditorPanelElements.InputField, EditorPanelElements.InputField, EditorPanelElements.Checkbox) AddSidewalkFields(EditorPanelPage p, float w,
            string inner, string outer, string skip, Dictionary<string, System.Func<string>> indexGetters) {

            var innerField = p.AddFieldInputField(SM.Get("IS_SW_INNER_SEGMENTS"), SM.Get("SEGMENTS_PH"), GameEngine.InputContentType.Integer, GetCurIntersection, inner, indexGetters, w * 0.5f, SM.Get("IS_SW_INNER_SEGMENTS_TOOLTIP"));
            var outerField = p.AddFieldInputField(SM.Get("IS_SW_OUTER_SEGMENTS"), SM.Get("SEGMENTS_PH"), GameEngine.InputContentType.Integer, GetCurIntersection, outer, indexGetters, w * 0.5f, SM.Get("IS_SW_OUTER_SEGMENTS_TOOLTIP"));
            p.IncreaseRow();
            var skipCheckbox = p.AddFieldCheckbox(SM.Get("IS_SW_OUTER_SKIP_CW"), GetCurIntersection, skip, indexGetters, w, SM.Get("IS_SW_OUTER_SKIP_CW_TOOLTIP"));
            p.IncreaseRow();
            return (innerField, outerField, skipCheckbox);
        }

        static bool HasUIElement(CityElements.Types.Runtime.IntersectionType type, string name) {
            var tabs = type?.typeData.GetUI().tabs;
            if (tabs == null) return false;
            foreach (var tab in tabs) {
                if (tab.elements == null) continue;
                foreach (var e in tab.elements) {
                    if (e.name == name) return true;
                }
            }
            return false;
        }

        // Sidewalks: the list of the sidewalks of the intersection, the segments of the selected one (its own if
        // overridden), its outline in the view, picking it by clicking it (see ElementPlacer.SidewalkPicker)

        EditorPanelPage sidewalksPage;
        EditorPanelElements.ScrollList sidewalkList;
        EditorPanelElements.Checkbox swOverrideCheckbox, swSkipCheckbox;
        EditorPanelElements.InputField swInnerField, swOuterField;
        int curSidewalkRoad = -1; //the key of the selected sidewalk (IntersectionGenerator.SidewalkInfo.road), -1 if none
        List<int> listedSidewalks = new List<int>(); //the keys of the sidewalks in the list
        Intersection listedIntersection;
        readonly List<GameEngine.LineRenderer> outlines = new List<GameEngine.LineRenderer>();
        Godot.Material outlineMat, selectedOutlineMat;

        List<IntersectionGenerator.SidewalkInfo> GetSidewalks() {
            if (GetComplexElement<PresetSelector>().EditingPreset()) return new List<IntersectionGenerator.SidewalkInfo>();
            var gen = GetCurIntersection()?.generator;
            return gen != null ? gen.sidewalks : new List<IntersectionGenerator.SidewalkInfo>();
        }

        IntersectionGenerator.SidewalkInfo GetSelectedSidewalk() {
            if (curSidewalkRoad < 0) return null;
            return GetSidewalks().Find(s => s.road == curSidewalkRoad);
        }

        //the index of the override of the selected sidewalk in the sidewalks array of the instance state
        int GetSidewalkOverrideIndex() {
            var arr = GetCurIntersection()?.instanceState.Array<ObjectState>("sidewalks");
            if (arr == null) return -1;
            for (int i = 0; i < arr.Length; i++) {
                if (arr[i] != null && arr[i].Int("road", -1) == curSidewalkRoad) return i;
            }
            return -1;
        }

        string GetSidewalkLabel(IntersectionGenerator.SidewalkInfo s) {
            var label = SM.Get("IS_SW_ITEM").Replace("$A", GameEngine.GameObject.GetName(s.roadI)).Replace("$B", GameEngine.GameObject.GetName(s.roadJ));
            return s.overridden ? label + " *" : label;
        }

        //after a change of the sidewalks (a rebuild): the same items keep the selection
        void RefreshSidewalkList() {
            if (sidewalkList == null || sidewalkList.IsFreed()) return;
            if (GetCurIntersection() != listedIntersection) {
                listedIntersection = GetCurIntersection();
                curSidewalkRoad = -1;
                listedSidewalks = new List<int>();
                sidewalkList.Deselect();
            }
            var sidewalks = GetSidewalks();
            var keys = sidewalks.ConvertAll(s => s.road);
            var labels = sidewalks.ConvertAll(GetSidewalkLabel);
            if (keys.Count != listedSidewalks.Count || !keys.TrueForAll(k => listedSidewalks[keys.IndexOf(k)] == k)) {
                listedSidewalks = keys;
                sidewalkList.SetItems(labels);
                var index = keys.IndexOf(curSidewalkRoad);
                if (index < 0) {
                    curSidewalkRoad = -1;
                    sidewalkList.Deselect();
                } else {
                    sidewalkList.actionEnabled = false;
                    sidewalkList.SetValue(index);
                    sidewalkList.actionEnabled = true;
                }
            } else {
                sidewalkList.UpdateLabels(labels);
            }
            RefreshSidewalkFields();
            RefreshOutlines();
        }

        void SelectSidewalk(int index) {
            curSidewalkRoad = index >= 0 && index < listedSidewalks.Count ? listedSidewalks[index] : -1;
            RefreshSidewalkFields();
            RefreshOutlines();
        }

        //what a click on the sidewalk in the view does (see ElementPlacer.SidewalkPicker)
        public void PickSidewalk(int index) {
            if (!ActiveSelf() || sidewalkList == null || sidewalkList.IsFreed()) return;
            RefreshSidewalkList();
            if (index < 0 || index >= listedSidewalks.Count) return;
            var page = GetPageIndex(sidewalksPage);
            if (page >= 0) ShowPage(page);
            sidewalkList.actionEnabled = false;
            sidewalkList.SetValue(index);
            sidewalkList.actionEnabled = true;
            sidewalkList.Reveal(index);
            SelectSidewalk(index);
        }

        //the fields of the selected sidewalk: its override, or the values of the intersection (read only)
        void RefreshSidewalkFields() {
            if (swOverrideCheckbox == null || sidewalksPage == null) return;
            var selected = GetSelectedSidewalk();
            var overridden = selected != null && GetSidewalkOverrideIndex() >= 0;
            const string swPath = "instanceState.properties.sidewalks.$sw.properties.";
            if (selected == null) {
                sidewalksPage.UpdateFieldName(swInnerField, "");
                sidewalksPage.UpdateFieldName(swOuterField, "");
                sidewalksPage.UpdateFieldName(swSkipCheckbox, "");
                swInnerField.SetValue("");
                swOuterField.SetValue("");
                swSkipCheckbox.actionEnabled = false; //a checkbox runs its action when set
                swSkipCheckbox.SetValue(false);
                swSkipCheckbox.actionEnabled = true;
            } else if (overridden) {
                //the inner segments beside a road through are those of the intersection
                sidewalksPage.UpdateFieldName(swInnerField, selected.innerLocked ? "state.properties.sidewalkSegments" : swPath + "innerSegments");
                sidewalksPage.UpdateFieldName(swOuterField, swPath + "outerSegments");
                sidewalksPage.UpdateFieldName(swSkipCheckbox, swPath + "outerSkipsCrosswalks");
            } else {
                sidewalksPage.UpdateFieldName(swInnerField, "state.properties.sidewalkSegments");
                sidewalksPage.UpdateFieldName(swOuterField, "state.properties.sidewalkOuterSegments");
                sidewalksPage.UpdateFieldName(swSkipCheckbox, "state.properties.sidewalkOuterSkipsCrosswalks");
            }
            swOverrideCheckbox.actionEnabled = false;
            swOverrideCheckbox.SetValue(overridden);
            swOverrideCheckbox.actionEnabled = true;
            swOverrideCheckbox.SetInteractable(selected != null);
            swInnerField.SetInteractable(overridden && !selected.innerLocked);
            swOuterField.SetInteractable(overridden);
            swSkipCheckbox.SetInteractable(overridden);
            sidewalksPage.ReadCurValues();
        }

        void SetSidewalkOverridden(bool overridden) {
            var intersection = GetCurIntersection();
            var selected = GetSelectedSidewalk();
            if (intersection == null || selected == null || GetComplexElement<PresetSelector>().EditingPreset()) return;
            if (overridden == (Operations.Intersections.GetSidewalkOverride(intersection, selected.road) != null)) return;
            var values = overridden ? Operations.Intersections.SidewalkOverrideFromIntersection(intersection) : null;
            Operations.Intersections.SetSidewalkOverride(intersection, selected.road, values);
            builder.NotifyChange();
            RefreshSidewalkFields();
        }

        //the outlines of the sidewalks while their tab is shown, the selected one highlighted
        void RefreshOutlines() {
            ClearOutlines();
            var intersection = GetCurIntersection();
            if (sidewalksPage == null || !sidewalksPage.ActiveSelf() || intersection == null || intersection.IsDeleted() || intersection.ghost) return;
            if (outlineMat == null) {
                outlineMat = MaterialManager.GetHandleMaterial((GameEngine.Color.red + GameEngine.Color.yellow) * 0.5f);
                selectedOutlineMat = MaterialManager.GetHandleMaterial(GameEngine.Color.green);
            }
            foreach (var s in GetSidewalks()) {
                if (s.outline.Count < 2) continue;
                var lr = GameEngine.GameObject.CreateLineRenderer(intersection.geo, "Sidewalk outline");
                var points = new List<Godot.Vector3>(s.outline) { s.outline[0] };
                lr.material = s.road == curSidewalkRoad ? selectedOutlineMat : outlineMat;
                lr.heightOffset = 0.05f;
                lr.positionCount = points.Count;
                lr.SetPositions(points.ToArray());
                lr.enabled = true;
                var line = lr;
                lr.frameUpdate = () => UpdateOutlineWidth(line, s.road == curSidewalkRoad);
                outlines.Add(lr);
            }
        }

        //wider when farther from the camera, as the outlines of the building sides
        void UpdateOutlineWidth(GameEngine.LineRenderer lr, bool selected) {
            if (!Godot.GodotObject.IsInstanceValid(lr) || !lr.enabled) return;
            var cam = GameEngine.Camera.Get(lr);
            if (cam == null) return;
            var intersection = GetCurIntersection();
            if (intersection == null || intersection.IsDeleted()) return;
            var distance = GameEngine.Vector3.Distance(GameEngine.GameObject.GetPosition(intersection.point), GameEngine.GameObject.GetPosition(cam));
            var width = (selected ? 0.12f : 0.08f) * Godot.Mathf.Clamp(distance / 20, 1.0f, 1000);
            lr.startWidth = width;
            lr.endWidth = width;
        }

        void ClearOutlines() {
            foreach (var lr in outlines) {
                if (Godot.GodotObject.IsInstanceValid(lr)) GameEngine.GameObject.Destroy(lr);
            }
            outlines.Clear();
        }

        public override void Update() {
            base.Update();
            if (ActiveSelf()) RefreshSidewalkList();
        }

        protected override void OnPageChanged() {
            base.OnPageChanged();
            RefreshOutlines();
        }

        ElementPlacer.RoadPlacer GetPlacer() {
            var modifier = builder.terrainClick.modifier;
            if (modifier != null && modifier is ElementPlacer.RoadPlacer) {
                return ((ElementPlacer.RoadPlacer)modifier);
            }
            return null;
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            var pS = GetComplexElement<PresetSelector>();
            if (active) {
            } else {
                if (ActiveSelf() && !keepActive) Terminate(true);
            }
            base.SetActive(active);
            if (active) RefreshSidewalkList();
            else ClearOutlines();
            if (active && !keepActive) {
                builder.helper.elementManager.ShowIntersections(false);
                GetCurIntersection()?.SetActive(active, true, true);
            }
            if (active) {
                if (pS.EditingPreset()) {
                    SetTitle(SM.Get("PRESET"), false);
                } else if (GetCurIntersection() != null) {
                    SetTitle(GameEngine.GameObject.GetName(GetCurIntersection().geo));
                }
            }
        }

        protected override void ReplaceTitle(string value) {
            var obj = GetCurIntersection();
            if (value == null || value == "") {
                if (obj != null) {
                    value = GameEngine.GameObject.GetName(obj.geo);
                } else {
                    value = "";
                }
            } else {
                if (obj != null) GameEngine.GameObject.SetName(obj.geo, value);
            }
            base.ReplaceTitle(value);
        }

        //same condition under which the delete button is shown (the preset dummy intersection can't be deleted)
        public bool CanDelete() {
            return !GetComplexElement<PresetSelector>().EditingPreset() && Operations.Intersections.CanDelete(GetCurIntersection());
        }

        public void Delete() {
            if (!CanDelete()) return;
            Operations.Intersections.Delete(GetCurIntersection());
            builder.NotifyChange();
            Terminate();
        }

        //the road of the intersection kept by the merge button, null if the button can't merge
        Road GetMergeRoad(int index) {
            var intersection = GetCurIntersection();
            if (GetComplexElement<PresetSelector>().EditingPreset() || intersection == null || intersection.roads.Count != 2) return null;
            return intersection.roads[index];
        }

        //merges the two roads into the one of the button, deleting the intersection
        void Merge(int index) {
            var road = GetMergeRoad(index);
            if (road == null || !Operations.Intersections.CanMerge(GetCurIntersection(), road)) return;
            Operations.Intersections.Merge(builder.helper.elementManager, GetCurIntersection(), road);
            builder.NotifyChange();
            Terminate();
        }

        protected override void ReadCurValues() {
            base.ReadCurValues();
            //only an intersection with two roads can be merged or seamless
            var twoRoads = GetCurIntersection()?.roads.Count == 2;
            if (seamlessCheckbox != null && !GetComplexElement<PresetSelector>().EditingPreset()) seamlessCheckbox.SetInteractable(twoRoads);
            for (int i = 0; i < mergeButtons.Count; i++) {
                mergeButtons[i].SetActive(twoRoads);
                var road = GetMergeRoad(i);
                mergeButtons[i].SetTitle(road != null ? SM.Get("IS_MERGE_INTO").Replace("$ROAD", GameEngine.GameObject.GetName(road)) : SM.Get("IS_MERGE"));
                mergeButtons[i].SetInteractable(road != null && Operations.Intersections.CanMerge(GetCurIntersection(), road));
            }
        }

        public override void Terminate() {
            keepActive = false;
            Terminate(false);
        }

        public void Terminate(bool auto) {
            if (GetPlacer() != null) GetPlacer().placementMode = ElementPlacer.RoadPlacer.PlacementMode.None;
            GetCurIntersection()?.SetActive(false);
            builder.helper.elementManager.ShowIntersections(false);
            ClearOutlines();
            if (!auto) SetActive(false);
            GameEngine.Gizmo.ClearTargets();
            builder.SetSelectObjectMode(); //todo: rework preset selection detection to avoid this
        }

        public override object GetUndoTarget() {
            return GetCurIntersection();
        }

        Intersection GetCurIntersection() {
            var modifier = builder.terrainClick.modifier;
            if (modifier is TerrainModifier.Null) return builder.helper.elementManager.GetDummyIntersection();
            else return intersection;
        }
    }
}
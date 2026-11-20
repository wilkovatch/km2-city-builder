using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace EditorPanels {
    public class TerrainPatchEditorPanel : EditorPanel {
        EditorPanelElements.Dropdown pointPlacementModeDropdown;

        public TerrainPatchEditorPanel() {
            AddComplexElement(new TypeSelector<CityElements.Types.Runtime.TerrainPatchType>(this) { stateGetter = () => builder?.terrainClick != null ? GetCurPatch(false)?.state : null });
        }

        TypeSelector<CityElements.Types.Runtime.TerrainPatchType> TS() {
            return GetComplexElement<TypeSelector<CityElements.Types.Runtime.TerrainPatchType>>();
        }

        public override void Initialize(GameObject canvas) {
            InitializeWithCustomParameters<CityElements.Types.Runtime.TerrainPatchType, CityElements.Types.TerrainPatchType>(canvas, GetCurPatch, TS,
                null, CityElements.Types.Parsers.TypeParser.GetTerrainPatchTypes, ProcessCustomParts, false);
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

                var placementModes = new List<string> { SM.Get("TP_PM_NONE"), SM.Get("TP_PM_PERIMETER"), SM.Get("TP_PM_INTERNAL") };
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

                p.AddButton(SM.Get("TP_SPLIT"), Split, 0.75f, SM.Get("TP_SPLIT_TOOLTIP"));
                p.AddButton(SM.Get("TP_MERGE_PATCH"), Merge, 0.75f, SM.Get("TP_MERGE_PATCH_TOOLTIP"));
                p.IncreaseRow();

                p.AddFieldTextureField(builder, SM.Get("TEXTURE"), SM.Get("TEXTURE_PH"), GetCurPatch, "state.properties.texture", null, 1.5f, null, null, x => { PreferencesManager.Set("curTerrainTexture", x); });
                p.IncreaseRow();

            } else {
                return false;
            }
            return true;
        }

        public void SetPointPlacementMode(int mode) {
            LoadPointPlacementMode(mode);
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

        //the patch is shown as edited again, after an operation that changed its points
        void ReselectPatch() {
            var patch = GetCurPatch(false);
            if (patch == null || GetPlacer() == null) return;
            patch.Select(true);
            patch.SetActive(true, GetPlacer().placementMode == ElementPlacer.TerrainPlacer.PlacementMode.None);
        }

        static bool SamePlace(ElementPlacer.OutlinePicker.Place a, ElementPlacer.OutlinePicker.Place b) {
            return a == b || (!a.free && !b.free && new Godot.Vector2(a.position.X - b.position.X, a.position.Z - b.position.Z).LengthSquared() < 1e-4f);
        }

        //null if the place can follow the ones picked in the line cutting the patch (see Split), otherwise why not
        public static string CutError(TerrainPatch patch, IReadOnlyList<ElementPlacer.OutlinePicker.Place> picked, ElementPlacer.OutlinePicker.Place place) {
            var line = new List<Godot.Vector3>();
            foreach (var p in picked) line.Add(p.position);
            if (picked.Count == 0) {
                return place.free ? Operations.TerrainPatches.GetCutError(patch, new List<Godot.Vector3> { place.position }, false, false, false) : null;
            }
            var fromPerimeter = !picked[0].free;
            if (SamePlace(place, picked[0])) {
                //back to the first place: the polygon of the line is carved
                return Operations.TerrainPatches.GetCutError(patch, line, fromPerimeter, true);
            }
            line.Add(place.position);
            if (place.free) return Operations.TerrainPatches.GetCutError(patch, line, fromPerimeter, false, false);
            if (!fromPerimeter) return "a line started inside the terrain patch can only end on its first point";
            return Operations.TerrainPatches.GetCutError(patch, line, true, false);
        }

        //cuts the patch along the line through the places clicked next: it starts on the perimeter (on a point, or on a
        //side, where a point is inserted) or inside the patch, and goes through places inside it. When it reaches the
        //perimeter again the patch is split in two along it, when it comes back to its first place the polygon it makes
        //is carved out of the patch (see Operations.TerrainPatches.Split and Carve)
        void Split() {
            var patch = GetCurPatch(false);
            if (GetPlacer() == null || patch == null) return;
            builder.outlinePicker.StartLine(() => patch.IsDeleted() ? null : patch.GetPerimeterPointsComponents(), true,
                (picked, place) => CutError(patch, picked, place) == null,
                picked => picked.Count >= 2 && (!picked[picked.Count - 1].free || picked[picked.Count - 1] == picked[0]),
                places => History.UndoManager.Record(SM.Get("TP_SPLIT"), () => {
                    if (GetCurPatch(false) != patch) return;
                    var manager = builder.helper.elementManager;
                    var first = places[0];
                    var last = places[places.Count - 1];
                    var inside = new List<Godot.Vector3>();
                    for (int i = 0; i < places.Count - 1; i++) {
                        if (places[i].free) inside.Add(places[i].position);
                    }
                    if (last.free && last != first) inside.Add(last.position);
                    TerrainPoint Insert(int index, Godot.Vector3 position) => Operations.TerrainPatches.InsertPerimeterPoint(patch, position, index);
                    var created = new List<TerrainPatch>();
                    if (first.free) {
                        created = Operations.TerrainPatches.Carve(manager, patch, inside);
                    } else if (SamePlace(first, last)) {
                        var at = ElementPlacer.OutlinePicker.Resolve(new List<ElementPlacer.OutlinePicker.Place> { first }, Insert)[0];
                        created = Operations.TerrainPatches.Carve(manager, patch, inside, at);
                    } else {
                        var points = ElementPlacer.OutlinePicker.Resolve(new List<ElementPlacer.OutlinePicker.Place> { first, last }, Insert);
                        var newPatch = Operations.TerrainPatches.Split(manager, patch, points[0], points[1], inside);
                        created = newPatch == null ? null : new List<TerrainPatch> { newPatch };
                    }
                    if (created == null) return;
                    GameEngine.Gizmo.ClearTargets();
                    //the points of the new patches are hidden, the ones they share with this one are shown again with it
                    foreach (var newPatch in created) {
                        newPatch.Select(false);
                        newPatch.SetActive(false, false);
                    }
                    ReselectPatch();
                    builder.NotifyChange();
                }));
        }

        //merges the patch clicked next into the patch
        void Merge() {
            var patch = GetCurPatch(false);
            if (GetPlacer() == null || patch == null) return;
            builder.elementPicker.Start(
                element => element is TerrainPatch other && Operations.TerrainPatches.GetMergeError(patch, other) == null,
                element => History.UndoManager.Record(SM.Get("TP_MERGE_PATCH"), () => {
                    if (GetCurPatch(false) != patch || !Operations.TerrainPatches.Merge(builder.helper.elementManager, patch, (TerrainPatch)element)) return;
                    GameEngine.Gizmo.ClearTargets();
                    ReselectPatch();
                    builder.NotifyChange();
                }));
        }

        void LoadPointPlacementMode(int mode) {
            if (GetPlacer() != null && GetCurPatch(false) != null) {
                GetPlacer().placementMode = (ElementPlacer.TerrainPlacer.PlacementMode)mode;
                pointPlacementModeDropdown.SetValue(mode);
                GetCurPatch().Select(true);
                GetCurPatch().SetActive(true, mode == 0);
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
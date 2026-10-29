using System.Collections.Generic;
using Godot;
using GameObject = Godot.Control;
using SM = StringManager;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace EditorPanels {
    //The editor of a low-level element (also used to create one: the type is chosen, then the element is placed with a
    //click). Its panel is the ui.json of the type, with these built-in groups:
    //mainGroup (end editing, delete, type), pointsGroup (selected point, point placement, array elements),
    //meshGroup (texture coordinates of the selected vertex), containerGroup (the container, changed from a list, the
    //contents and the merge into another container, chosen from a list), propsGroup (the prop rule, for the types with
    //props containers)
    public class LowLevelElementEditorPanel : EditorPanel, IPropRuleEditor {
        string creationTypeValue;
        public string creationGroup = "CREATE_LOW_LEVEL_ELEMENT";
        public string creationType {
            get => creationTypeValue;
            set { creationTypeValue = value; builtType = "\0"; }
        }
        int selectedRibbon;
        string builtType = null; //the type the panel was built for
        LowLevelElement builtElement = null; //and the element (its arrays, container and contents are listed)
        EditorPanelElements.Dropdown createTypeDropdown, placementDropdown;
        EditorPanelElements.Label selectedLabel;
        EditorPanelElements.ScrollList contentsList;
        EditorPanelElements.Button insertBeforeButton, insertAfterButton, removeButton, detachButton, addInnerButton, removeInnerButton, editContentButton,
            changeContainerButton, mergeButton;
        EditorPanelElements.InputField uField, vField;
        List<string> creatableTypes = new List<string>();
        //the arrays the placement modes add to: arrays of points get points at the end, arrays of groups get elements at
        //the start or end
        List<(string path, bool atStart)> appendArrays = new List<(string, bool)>();
        List<LowLevelElement> possibleContainers = new List<LowLevelElement>();
        List<LowLevelElement> contents = new List<LowLevelElement>();
        List<LowLevelElement> mergeTargets = new List<LowLevelElement>();
        TerrainPoint shownPoint = null;
        bool shownAnything = false;
        bool shownDerived = false; //the points were computed (see LowLevelElement.IsDerived) when last shown
        bool shownHandle = false; //the move handle of the container was selected when last shown
        readonly PropRuleGroup propRule;
        //the content picked in the contents dropdown, outlined (in a colour of its own, on a free render layer) and with
        //its points selected
        const uint ContentOutlineLayer = 1u << 16;
        LowLevelElement highlighted = null;
        RuntimeGizmos.SelectionOutline contentOutline;
        List<GeometryInstance3D> outlinedRenderers = new List<GeometryInstance3D>();
        //the container whose elements (and itself) show their lines shown with the container (e.g. the BAI lanes of a room)
        LowLevelElement linesContainer = null;
        //the list of containers to change the container to, or to merge into
        LowLevelElementPickerPanel picker;

        public LowLevelElementEditorPanel() {
            AddComplexElement(new PresetSelector(this)); //for the presets of the prop rule (the element itself has none)
            propRule = new PropRuleGroup(this, _ => GetElement()?.state, () => {
                if (GetElement() is PropPath path) { path.SynchronizeRule(); builtElement = null; }
                ReadCurValues();
                builder.NotifyChange();
            });
        }

        public override void Initialize(GameObject canvas) {
            var element = GetElement();
            builtType = element?.TypeName;
            builtElement = element;
            //the fields of the previous build are freed, and the groups of this type may not create them again
            createTypeDropdown = placementDropdown = null;
            selectedLabel = null;
            contentsList = null;
            insertBeforeButton = insertAfterButton = removeButton = detachButton = addInnerButton = removeInnerButton = editContentButton = changeContainerButton = mergeButton = null;
            uField = vField = null;
            appendArrays.Clear();
            possibleContainers.Clear();
            HighlightContent(null);
            contents.Clear();
            mergeTargets.Clear();
            propRule.controls.Clear();
            shownAnything = false;
            if (builtType == null || PreferencesManager.workingDirectory == "") {
                InitializeCreation(canvas);
                return;
            }
            InitializeWithCustomParameters<CityElements.Types.Runtime.LowLevelType, CityElements.Types.LowLevelType>(canvas, GetElementState, null,
                builtType, CityElements.Types.Parsers.TypeParser.GetLowLevelTypes, ProcessCustomParts, false);
            picker = AddChildPanel<LowLevelElementPickerPanel>(canvas);
        }

        IObjectWithState GetElementState() {
            return GetElement();
        }

        //the panel shown while choosing the type of a new element
        void InitializeCreation(GameObject canvas) {
            Initialize(canvas, 1, 1.5f);
            if (PreferencesManager.workingDirectory == "") return;
            var p = GetPage(0);
            p.AddButton(SM.Get("END_EDITING"), Terminate, 1.5f);
            p.IncreaseRow();
            if (creationType != null) {
                p.AddLabel(Operations.LowLevel.GetTypeLabel(creationType) + ": " + SM.Get("LL_CLICK_TO_CREATE"), 1.5f);
                return;
            }
            creatableTypes.Clear();
            var names = new List<string>();
            foreach (var type in CityElements.Types.Parsers.TypeParser.GetLowLevelTypes()) {
                if (!type.Value.typeData.settings.creatable) continue;
                var menu = type.Value.typeData.settings.createMenu;
                if (menu?.direct == true || (menu?.group ?? "CREATE_LOW_LEVEL_ELEMENT") != creationGroup) continue;
                creatableTypes.Add(type.Key);
                names.Add(Operations.LowLevel.GetTypeLabel(type.Key));
            }
            if (creatableTypes.Count == 0) {
                p.AddLabel(SM.Get("LL_NO_TYPES"), 1.5f);
                return;
            }
            createTypeDropdown = p.AddDropdown(SM.Get("LL_TYPE"), names, SetCreateType, 1.5f);
            p.IncreaseRow();
            p.AddLabel(SM.Get("LL_CLICK_TO_CREATE"), 1.5f);
            p.IncreaseRow();
        }

        void SetCreateType(int i) {
            var placer = GetPlacer();
            if (placer == null || i < 0 || i >= creatableTypes.Count) return;
            placer.createType = creatableTypes[i];
            placer.placementMode = ElementPlacer.LowLevelPlacer.PlacementMode.Create;
            PreferencesManager.Set("lastLowLevelType", creatableTypes[i]);
        }

        bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
            TypeSelector<CityElements.Types.Runtime.LowLevelType> tS, CityElements.Types.Runtime.LowLevelType type) {

            var w = elem.width;
            var settings = type.typeData.settings;
            if (elem.name == "splineGroup" && settings.spline != null) {
                p.IncreaseRow();
                var allowed = settings.spline.curveTypes;
                var labels = new[] { "RD_SP_T_BEZIER", "RD_SP_T_HERMITE", "RD_SP_T_LOWPOLY" };
                var dropdown = p.AddDropdown(SM.Get("RD_SPLINE_TYPE"), new List<int>(allowed).ConvertAll(i => SM.Get(labels[i])), i => {
                    GetElement().state.SetInt("curveType", allowed[i]); builder.NotifyChange();
                }, w);
                dropdown.actionEnabled = false;
                dropdown.SetValue(System.Array.IndexOf(allowed, GetElement().state.Int("curveType", settings.spline.defaultCurveType)));
                dropdown.actionEnabled = true;
                p.IncreaseRow();
                p.AddFieldInputField(SM.Get("SEGMENTS"), SM.Get("SEGMENTS_PH"), GameEngine.InputContentType.Integer, GetElement,
                    "state.properties.segments", null, w / 2, null);
                p.AddFieldCheckbox(SM.Get("RD_SEGMENTS_100M"), GetElement, "state.properties.segmentsPer100m", null, w / 2, null);
                p.IncreaseRow();
                p.AddFieldCheckbox(SM.Get("RD_SUBDIVIDE_EQUALLY"), GetElement, "state.properties.subdivideEqually", null, w / 2, null);
                p.AddFieldInputField(SM.Get("RD_HERMITE_TENSION"), SM.Get("RD_HERMITE_TENSION_PH"), GameEngine.InputContentType.Decimal,
                    GetElement, "state.properties.hermiteTension", null, w / 2, null);
                p.IncreaseRow();
                return true;
            }
            if (elem.name == "pointsGroup" && settings.spline != null) {
                selectedLabel = p.AddLabel("", w); p.IncreaseRow();
                placementDropdown = p.AddDropdown(SM.Get("RD_POINT_PLACEMENT_MODE"), new List<string> {
                    SM.Get("RD_PM_NONE"), SM.Get("RD_PM_ADD"), SM.Get("RD_PM_ADD_BEFORE_FIRST"),
                    SM.Get("RD_PM_INSERT"), SM.Get("RD_PM_INSERT_BEFORE") }, SetSplinePlacementMode, w);
                p.IncreaseRow();
                removeButton = p.AddButton(SM.Get("RD_REMOVE_POINT"), RemoveArrayElement, w); p.IncreaseRow();
                return true;
            }
            var builtIn = elem.name == "mainGroup" || elem.name == "pointsGroup" || elem.name == "meshGroup" || elem.name == "containerGroup"
                || elem.name == "propsGroup";
            if (builtIn) p.EnsureNewRow(); //after a parameter sharing its row
            if (elem.name == "mainGroup") {
                p.AddButton(SM.Get("END_EDITING"), Terminate, w * 0.5f);
                p.AddButton(SM.Get("DELETE"), Delete, w * 0.5f);
                p.IncreaseRow();
                p.AddLabel(SM.Get("LL_TYPE") + ": " + Operations.LowLevel.GetTypeLabel(type.name), w);
                p.IncreaseRow();
                var element = GetElement();
                if (element?.source != null && Operations.LowLevel.IsGhost(element.source)) {
                    p.AddButton(SM.Get("LL_RESTORE_SOURCE"), RestoreSource, w, SM.Get("LL_RESTORE_SOURCE_TOOLTIP"));
                    p.IncreaseRow();
                }
            } else if (elem.name == "pointsGroup") {
                selectedLabel = p.AddLabel("", w);
                p.IncreaseRow();
                var modes = new List<string> { SM.Get("LL_PM_NONE") };
                appendArrays.Clear();
                var element = GetElement();
                if (element != null) {
                    foreach (var (path, list) in LowLevel.PointTree.Lists(element.points)) {
                        if (!list.def.array.editable) continue;
                        var key = LowLevel.PointTree.Wildcard(path);
                        //a uniform array is the same everywhere: its first instance stands for the others
                        if (list.def.array.uniform && appendArrays.Exists(a => LowLevel.PointTree.Wildcard(a.path) == key)) continue;
                        var name = ArrayLabel(element, path);
                        if (list.def.children == null) {
                            appendArrays.Add((path, false));
                            modes.Add(SM.Get("LL_PM_APPEND").Replace("$ARRAY", name));
                        } else {
                            appendArrays.Add((path, false));
                            modes.Add(SM.Get("LL_PM_APPEND_END").Replace("$ARRAY", name));
                            appendArrays.Add((path, true));
                            modes.Add(SM.Get("LL_PM_APPEND_START").Replace("$ARRAY", name));
                        }
                    }
                }
                modes.Add(SM.Get("LL_PM_ATTACH"));
                modes.Add(SM.Get("LL_PM_ATTACH_LINE"));
                modes.Add(SM.Get("LL_PM_ATTACH_COLUMN"));
                placementDropdown = p.AddDropdown(SM.Get("LL_POINT_PLACEMENT_MODE"), modes, SetPlacementMode, w);
                p.IncreaseRow();
                insertBeforeButton = p.AddButton(SM.Get("LL_INSERT_BEFORE"), () => InsertArrayElement(false), w * 0.5f, SM.Get("LL_INSERT_TOOLTIP"));
                insertAfterButton = p.AddButton(SM.Get("LL_INSERT_AFTER"), () => InsertArrayElement(true), w * 0.5f, SM.Get("LL_INSERT_TOOLTIP"));
                p.IncreaseRow();
                removeButton = p.AddButton(SM.Get("LL_REMOVE_ELEMENT"), RemoveArrayElement, w * 0.5f, SM.Get("LL_REMOVE_ELEMENT_TOOLTIP"));
                detachButton = p.AddButton(SM.Get("LL_DETACH_POINT"), DetachPoint, w * 0.5f, SM.Get("LL_DETACH_POINT_TOOLTIP"));
                p.IncreaseRow();
                //the buttons above act on the outermost array (e.g. the sections), these on the innermost one (the lanes)
                if (element != null && LowLevel.PointTree.HasNestedArrays(element.points.def)) {
                    addInnerButton = p.AddButton(SM.Get("LL_ADD_INNER"), AddInnerArrayElement, w * 0.5f, SM.Get("LL_ADD_INNER_TOOLTIP"));
                    removeInnerButton = p.AddButton(SM.Get("LL_REMOVE_INNER"), RemoveInnerArrayElement, w * 0.5f, SM.Get("LL_REMOVE_INNER_TOOLTIP"));
                    p.IncreaseRow();
                }
            } else if (elem.name == "meshGroup") {
                if (settings.mesh?.uvs == "editable") {
                    uField = p.AddInputField("U", "U", "", GameEngine.InputContentType.Decimal, x => SetUV(0, x), w * 0.5f);
                    vField = p.AddInputField("V", "V", "", GameEngine.InputContentType.Decimal, x => SetUV(1, x), w * 0.5f);
                    p.IncreaseRow();
                }
            } else if (elem.name == "propPathGroup" && GetElement() is PropPath propPath) {
                var ribbonNames = new List<string>(propPath.RibbonNames);
                selectedRibbon = System.Math.Clamp(selectedRibbon, 0, ribbonNames.Count - 1);
                var ribbonDropdown = p.AddDropdown(SM.Get("PROP_PATH_RIBBONS"), ribbonNames, i => { selectedRibbon = i; builtElement = null; }, w);
                ribbonDropdown.actionEnabled = false;
                ribbonDropdown.SetValue(selectedRibbon);
                ribbonDropdown.actionEnabled = true;
                p.IncreaseRow();
                p.AddInputField(SM.Get("PROP_PATH_NAME"), "", ribbonNames[selectedRibbon], GameEngine.InputContentType.Standard, value => {
                    propPath.RenameRibbon(selectedRibbon, value); builder.NotifyChange(); builtElement = null;
                }, w);
                p.IncreaseRow();
                p.AddButton(SM.Get("PROP_PATH_REMOVE_RIBBON"), () => {
                    propPath.RemoveRibbon(selectedRibbon); builder.NotifyChange(); builtElement = null;
                }, w);
                p.IncreaseRow();
                p.AddButton(SM.Get("PROP_PATH_ADD_RIBBON"), () => {
                    var names = new List<string>(propPath.RibbonNames);
                    Operations.LowLevel.InsertArrayElement(propPath, "ribbons", names.Count, false);
                    var number = names.Count;
                    while (names.Contains("ribbon" + number)) number++;
                    names.Add("ribbon" + number);
                    propPath.state.SetArray("ribbonNames", names.ToArray());
                    propPath.state.FlagAsChanged();
                    builder.NotifyChange();
                    builtElement = null;
                }, w);
                p.IncreaseRow();
            } else if (elem.name == "propsGroup") {
                if (GetElement() is PropPath path) {
                    propRule.Add(p, GetComplexElement<PresetSelector>(), path.RibbonNames, null, w);
                    return true;
                }
                if (settings.propsContainers != null && settings.propsContainers.Length > 0) {
                    var presetTypes = settings.propsPresetType != null ? new List<string> { settings.propsPresetType } : null;
                    propRule.Add(p, GetComplexElement<PresetSelector>(), settings.propsContainers, presetTypes, w);
                }
            } else if (elem.name == "containerGroup") {
                var element = GetElement();
                if (element == null) return true;
                possibleContainers = Operations.LowLevel.GetPossibleContainers(builder.helper.elementManager, element);
                possibleContainers.Sort((a, b) => string.Compare(GO.GetName(a), GO.GetName(b), System.StringComparison.Ordinal));
                if (possibleContainers.Count > 0 || element.container != null) {
                    p.AddLabel(SM.Get("LL_CONTAINER") + ": " + ContainerName(element), w * 0.5f);
                    p.AddButton(SM.Get("LL_EDIT_CONTAINER"), EditContainer, w * 0.25f).SetInteractable(element.container != null);
                    changeContainerButton = p.AddButton(SM.Get("LL_CHANGE_CONTAINER"), OpenContainerChange, w * 0.25f);
                    p.IncreaseRow();
                }
                if (type.IsContainer) {
                    contents = builder.helper.elementManager.GetLowLevelChildren(element);
                    var names = new List<string>();
                    var prefix = GO.GetName(element) + " "; //not repeated in the contents named after the container
                    foreach (var c in contents) {
                        var name = GO.GetName(c);
                        names.Add(name.Length > prefix.Length && name.StartsWith(prefix, System.StringComparison.Ordinal) ? name.Substring(prefix.Length) : name);
                    }
                    contentsList = p.AddScrollList(SM.Get("LL_CONTENTS").Replace("$NUMBER", contents.Count.ToString()), names, PickContent, w);
                    p.IncreaseRow(5.0f);
                    editContentButton = p.AddButton(SM.Get("EDIT"), EditContent, w);
                    editContentButton.SetInteractable(false); //until a content is picked
                    p.IncreaseRow();
                    mergeTargets = new List<LowLevelElement>();
                    foreach (var other in builder.helper.elementManager.lowLevelElements) {
                        //the containers that can hold all the contents (e.g. a group of building parts into a room)
                        if (other == element || other.IsDeleted() || !(other.GetLowLevelType()?.IsContainer ?? false)) continue;
                        if (contents.TrueForAll(c => c == other || c.CanBeIn(other))) mergeTargets.Add(other);
                    }
                    mergeTargets.Sort((a, b) => string.Compare(GO.GetName(a), GO.GetName(b), System.StringComparison.Ordinal));
                    if (mergeTargets.Count > 0) {
                        mergeButton = p.AddButton(SM.Get("LL_MERGE_INTO"), OpenMerge, w, SM.Get("LL_MERGE_TOOLTIP"));
                        p.IncreaseRow();
                    }
                }
            } else {
                return false;
            }
            return true;
        }

        // Points

        //the label of the array (its translated label, or its name), e.g. "sections"
        static string ArrayLabel(LowLevelElement element, string arrayPath) {
            var list = LowLevel.PointTree.Find(element.points, arrayPath) as LowLevel.PointList;
            var label = list?.def.label;
            if (!string.IsNullOrEmpty(label)) return SM.Get(label).ToLowerInvariant();
            var parts = arrayPath.Split('.');
            return parts[parts.Length - 1];
        }

        TerrainPoint GetSelectedPoint() {
            var target = builder.gizmo.mainTargetRoot;
            if (target == null || !GodotObject.IsInstanceValid(target)) return null;
            var point = GS.FindRecursive<TerrainPoint>(target);
            var element = GetElement();
            return point != null && element != null && element.HasPoint(point) ? point : null;
        }

        //the path of the selected point (or "vertex <n>"), shown while the editor is open
        void ShowSelectedPoint() {
            var point = GetSelectedPoint();
            var element = GetElement();
            var derived = element?.IsDerived ?? false;
            var onHandle = element != null && element.GetMoveHandle() != null && builder.gizmo.mainTargetRoot == element.GetMoveHandle();
            if (point == shownPoint && shownAnything && derived == shownDerived && onHandle == shownHandle) return;
            shownPoint = point;
            shownAnything = true;
            shownDerived = derived;
            shownHandle = onHandle;
            string text;
            if (onHandle) {
                text = SM.Get("LL_MOVE_HANDLE_SELECTED");
            } else if (derived) {
                //computed from other elements: they can't be edited
                text = SM.Get("LL_POINTS_DERIVED");
                if (GetPlacer() != null && GetPlacer().placementMode != ElementPlacer.LowLevelPlacer.PlacementMode.None) SetPointPlacementMode(0);
            } else if (point == null || element == null) {
                text = SM.Get("LL_NO_POINT_SELECTED");
            } else {
                var path = LowLevel.PointTree.PathOf(element.points, point);
                var vertex = element.meshVertices.IndexOf(point);
                text = path ?? (vertex >= 0 ? SM.Get("LL_VERTEX") + " " + vertex : "");
                var shared = point.GetLinks().Count - 1;
                if (shared > 0) text += " (" + SM.Get("LL_SHARED").Replace("$NUMBER", shared.ToString()) + ")";
                if (!GO.IsNull(point.anchor) && !point.anchor.IsDeleted()) {
                    var anchorText = point.anchor is ColumnAnchor ? "LL_ON_COLUMN" : point.anchor is LineAnchor ? "LL_ON_LINE" : "LL_ANCHORED";
                    text += " (" + SM.Get(anchorText) + ")";
                }
            }
            selectedLabel?.SetValue(text);
            UpdateArrayButtons(element, point);
            if (uField != null && vField != null) {
                var vi = point != null && element != null ? element.meshVertices.IndexOf(point) : -1;
                var uv = vi >= 0 && vi < element.uvs.Count ? element.uvs[vi] : Vector2.Zero;
                uField.SetValue(vi >= 0 ? uv.X.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
                vField.SetValue(vi >= 0 ? uv.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
                uField.SetInteractable(vi >= 0);
                vField.SetInteractable(vi >= 0);
            }
        }

        //the buttons editing the arrays of the selected point name them, and are disabled without one (the inner ones also
        //when the point is in one array only), or while the points are computed
        void UpdateArrayButtons(LowLevelElement element, TerrainPoint point) {
            var editable = !(element?.IsDerived ?? false);
            placementDropdown?.SetInteractable(editable);
            var arrayElement = Operations.LowLevel.ArrayElementOf(element, point, true);
            var innerElement = Operations.LowLevel.ArrayElementOf(element, point, false);
            if (innerElement.HasValue && innerElement.Value.arrayPath == arrayElement?.arrayPath) innerElement = null;
            void Set(EditorPanelElements.Button button, string key, (string arrayPath, int index)? shown, bool enabled) {
                if (button == null) return;
                var name = shown.HasValue ? ArrayLabel(element, shown.Value.arrayPath) : null;
                button.SetTitle(name != null ? SM.Get(key + "_IN").Replace("$ARRAY", name) : SM.Get(key));
                button.SetInteractable(enabled && editable);
            }
            Set(insertBeforeButton, "LL_INSERT_BEFORE", arrayElement, arrayElement.HasValue);
            Set(insertAfterButton, "LL_INSERT_AFTER", arrayElement, arrayElement.HasValue);
            var isVertex = point != null && element != null && element.meshVertices.Contains(point);
            Set(removeButton, "LL_REMOVE_ELEMENT", arrayElement, arrayElement.HasValue || isVertex);
            Set(addInnerButton, "LL_ADD_INNER", innerElement, innerElement.HasValue);
            Set(removeInnerButton, "LL_REMOVE_INNER", innerElement, innerElement.HasValue);
            detachButton?.SetInteractable(point != null && editable);
        }

        void SetPlacementMode(int mode) {
            var placer = GetPlacer();
            if (placer == null) return;
            placer.EndAttaching();
            shownAnything = false; //the selected point may have been attached
            if (mode == 0) {
                placer.placementMode = ElementPlacer.LowLevelPlacer.PlacementMode.None;
            } else if (mode <= appendArrays.Count) {
                var (path, atStart) = appendArrays[mode - 1];
                var list = GetElement() != null ? LowLevel.PointTree.Find(GetElement().points, path) as LowLevel.PointList : null;
                placer.placementMode = list?.def.children != null ? ElementPlacer.LowLevelPlacer.PlacementMode.AppendElement
                    : ElementPlacer.LowLevelPlacer.PlacementMode.AppendPoint;
                placer.arrayPath = path;
                placer.atStart = atStart;
            } else if (mode == appendArrays.Count + 1) {
                placer.placementMode = ElementPlacer.LowLevelPlacer.PlacementMode.AttachSelected;
            } else if (mode == appendArrays.Count + 2) {
                placer.placementMode = ElementPlacer.LowLevelPlacer.PlacementMode.AttachToLine;
            } else {
                placer.placementMode = ElementPlacer.LowLevelPlacer.PlacementMode.AttachToColumn;
            }
            builder.helper.elementManager.ShowAnchors(mode > 0, true);
            var element = GetElement();
            if (element == null) return;
            element.SetActive(true, mode == 0);
            element.Select(true);
            //the anchors shown and hidden again hid the contents too
            if (mode == 0) HighlightContents(element, true);
            if (mode == 0) ShowMoveHandle(element);
        }

        void SetSplinePlacementMode(int mode) {
            var placer = GetPlacer();
            if (placer == null) return;
            placer.arrayPath = "spline";
            placer.atStart = mode == 4;
            placer.placementMode = mode switch {
                1 => ElementPlacer.LowLevelPlacer.PlacementMode.AppendPoint,
                2 => ElementPlacer.LowLevelPlacer.PlacementMode.PrependPoint,
                3 or 4 => ElementPlacer.LowLevelPlacer.PlacementMode.InsertPoint,
                _ => ElementPlacer.LowLevelPlacer.PlacementMode.None
            };
            GetElement()?.SetActive(true, true);
        }

        public void SetPointPlacementMode(int mode) {
            if (placementDropdown == null) return;
            placementDropdown.SetValue(mode);
            if (GetElement()?.Settings?.spline != null) SetSplinePlacementMode(mode);
            else SetPlacementMode(mode);
        }

        public int GetPointPlacementMode() {
            var placer = GetPlacer();
            if (placer == null) return 0;
            if (GetElement()?.Settings?.spline != null) return placer.placementMode switch {
                ElementPlacer.LowLevelPlacer.PlacementMode.AppendPoint => 1,
                ElementPlacer.LowLevelPlacer.PlacementMode.PrependPoint => 2,
                ElementPlacer.LowLevelPlacer.PlacementMode.InsertPoint => placer.atStart ? 4 : 3,
                _ => 0
            };
            switch (placer.placementMode) {
                case ElementPlacer.LowLevelPlacer.PlacementMode.AppendPoint: return appendArrays.IndexOf((placer.arrayPath, false)) + 1;
                case ElementPlacer.LowLevelPlacer.PlacementMode.AppendElement: return appendArrays.IndexOf((placer.arrayPath, placer.atStart)) + 1;
                case ElementPlacer.LowLevelPlacer.PlacementMode.AttachSelected: return appendArrays.Count + 1;
                case ElementPlacer.LowLevelPlacer.PlacementMode.AttachToLine: return appendArrays.Count + 2;
                case ElementPlacer.LowLevelPlacer.PlacementMode.AttachToColumn: return appendArrays.Count + 3;
                default: return 0;
            }
        }

        //a new element in the outermost array containing the selected point, before or after its element (in the
        //innermost one, after it)
        void InsertArrayElement(bool after) {
            InsertArrayElement(after, true);
        }

        void AddInnerArrayElement() {
            InsertArrayElement(true, false);
        }

        void InsertArrayElement(bool after, bool outermost) {
            var element = GetElement();
            var point = GetSelectedPoint();
            if (element == null || point == null) return;
            var arrayElement = Operations.LowLevel.ArrayElementOf(element, point, outermost);
            if (!arrayElement.HasValue) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("LL_NOT_IN_ARRAY_ERROR"), SM.Get("OK"));
                return;
            }
            var (arrayPath, index) = arrayElement.Value;
            if (!Operations.LowLevel.InsertArrayElement(element, arrayPath, after ? index + 1 : index)) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("LL_ARRAY_FULL_ERROR"), SM.Get("OK"));
                return;
            }
            builder.NotifyChange();
        }

        void RemoveArrayElement() {
            var element = GetElement();
            var point = GetSelectedPoint();
            if (element == null || point == null) return;
            if (!Operations.LowLevel.RemovePoint(element, point)) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("LL_CANNOT_REMOVE_ERROR"), SM.Get("OK"));
                return;
            }
            GameEngine.Gizmo.ClearTargets();
            builder.NotifyChange();
        }

        //removes the element of the innermost array containing the selected point (e.g. its lane, in every section)
        void RemoveInnerArrayElement() {
            var element = GetElement();
            var point = GetSelectedPoint();
            var arrayElement = Operations.LowLevel.ArrayElementOf(element, point, false);
            if (!arrayElement.HasValue) return;
            if (!Operations.LowLevel.RemoveArrayElement(element, arrayElement.Value.arrayPath, arrayElement.Value.index)) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("LL_CANNOT_REMOVE_ERROR"), SM.Get("OK"));
                return;
            }
            GameEngine.Gizmo.ClearTargets();
            builder.NotifyChange();
        }

        void DetachPoint() {
            var element = GetElement();
            var point = GetSelectedPoint();
            if (element == null || point == null) return;
            var res = Operations.LowLevel.DetachPoint(builder.helper.elementManager, element, point);
            builder.gizmo.ClearTargets();
            builder.gizmo.AddTarget(res.GetObject());
            shownAnything = false;
            builder.NotifyChange();
        }

        void SetUV(int component, string value) {
            var element = GetElement();
            var point = GetSelectedPoint();
            if (element == null || point == null) return;
            var vi = element.meshVertices.IndexOf(point);
            if (vi < 0 || !float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) return;
            var uv = element.uvs[vi];
            if (component == 0) uv.X = f;
            else uv.Y = f;
            element.SetUV(vi, uv);
            builder.NotifyChange();
        }

        // Props

        protected override void ReadCurValues() {
            base.ReadCurValues();
            propRule.Read();
        }

        public EditorPanel OpenPropContainer(string name) {
            return propRule.OpenContainer(name);
        }

        // Containers

        string ContainerName(LowLevelElement element) {
            return element.container != null ? GO.GetName(element.container) : SM.Get("NONE");
        }

        //the containers the element can be moved to (none first, if it has one), in a list replacing the panel
        void OpenContainerChange() {
            var element = GetElement();
            if (element == null || picker == null) return;
            var targets = new List<LowLevelElement>();
            if (element.container != null) targets.Add(null);
            foreach (var c in possibleContainers) {
                if (c != element.container) targets.Add(c);
            }
            picker.Open(SM.Get("LL_CHANGE_CONTAINER_TITLE").Replace("$ELEMENT", GO.GetName(element)),
                SM.Get("LL_CONTAINERS").Replace("$NUMBER", targets.FindAll(c => c != null).Count.ToString()), targets,
                SM.Get("LL_CURRENT_CONTAINER").Replace("$CONTAINER", ContainerName(element)), SM.Get("LL_CHANGE_TO"), SetContainer);
        }

        void SetContainer(LowLevelElement container) {
            var element = GetElement();
            if (element == null) return;
            element.SetContainer(container);
            builder.NotifyChange();
            Rebuild(); //shows the new container (and its merge targets)
        }

        void EditContainer() {
            var container = GetElement()?.container;
            if (container == null) return;
            Edit(container);
        }

        void PickContent(int i) {
            HighlightContent(i >= 0 && i < contents.Count ? contents[i] : null);
            editContentButton?.SetInteractable(highlighted != null);
        }

        void HighlightContent(LowLevelElement content) {
            highlighted = content;
            foreach (var c in contents) {
                if (!GO.IsNull(c) && !c.IsDeleted()) c.Select(c == highlighted);
            }
            UpdateContentOutline();
        }

        //called every frame, as the renderers of the content change with its geometry (and GO.SetLayer resets the layers)
        void UpdateContentOutline() {
            if (highlighted != null && (GO.IsNull(highlighted) || highlighted.IsDeleted())) highlighted = null;
            foreach (var render in outlinedRenderers) RuntimeGizmos.SelectionOutline.RemoveFromMask(render, ContentOutlineLayer);
            outlinedRenderers = highlighted != null ? RuntimeGizmos.SelectionOutline.GetRenderers(GO.GetObject(highlighted.generator))
                : new List<GeometryInstance3D>();
            if (contentOutline == null) {
                if (outlinedRenderers.Count == 0 || builder?.gizmo == null) return;
                contentOutline = new RuntimeGizmos.SelectionOutline {
                    Name = "ContentOutline",
                    maskLayer = ContentOutlineLayer,
                    outlineColor = new Color(0.0f, 0.85f, 1.0f)
                };
                builder.gizmo.AddChild(contentOutline);
            }
            contentOutline.UpdateOutline(outlinedRenderers);
        }

        void EditContent() {
            if (highlighted == null || GO.IsNull(highlighted) || highlighted.IsDeleted()) return;
            Edit(highlighted);
        }

        void Edit(LowLevelElement other) {
            Terminate();
            builder.SelectObject(other, true, null);
        }

        //the containers that can hold all the contents, in a list replacing the panel
        void OpenMerge() {
            var element = GetElement();
            if (element == null || picker == null) return;
            picker.Open(SM.Get("LL_MERGE_TITLE").Replace("$ELEMENT", GO.GetName(element)),
                SM.Get("LL_CONTAINERS").Replace("$NUMBER", mergeTargets.Count.ToString()), new List<LowLevelElement>(mergeTargets),
                null, SM.Get("LL_MERGE"), Merge);
        }

        void Merge(LowLevelElement target) {
            var element = GetElement();
            if (element == null || target == null || GO.IsNull(target) || target.IsDeleted()) return;
            if (!Operations.LowLevel.Merge(builder.helper.elementManager, element, target)) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("LL_MERGE_ERROR"), SM.Get("OK"));
                return;
            }
            builder.NotifyChange();
            Edit(target);
        }

        void RestoreSource() {
            var element = GetElement();
            var source = element?.source;
            if (source == null) return;
            builder.CreateAlert(SM.Get("WARNING"), SM.Get("LL_RESTORE_SOURCE_WARNING"), SM.Get("YES"), SM.Get("NO"), delegate {
                History.UndoManager.Record(SM.Get("LL_RESTORE_SOURCE"), () => {
                    Terminate();
                    Operations.LowLevel.RestoreGhost(builder.helper.elementManager, source);
                    builder.NotifyChange();
                });
            }, null);
        }

        // Panel

        ElementPlacer.LowLevelPlacer GetPlacer() {
            return builder?.terrainClick?.modifier as ElementPlacer.LowLevelPlacer;
        }

        public LowLevelElement GetElement() {
            return GetPlacer()?.GetElement();
        }

        public override object GetUndoTarget() {
            return GetElement();
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            if (active && !keepActive) {
                var element = GetElement();
                //the panel is built for the type of the element (or for the creation)
                if ((element != builtElement || element?.TypeName != builtType) && lastCanvas != null) Initialize(lastCanvas);
                var placer = GetPlacer();
                if (placer != null) {
                    placer.onFrame = OnFrame;
                    placer.onModeEnded = () => SetPointPlacementMode(0);
                    placer.onElementCreated = OnElementCreated;
                    if (element == null && createTypeDropdown != null && creatableTypes.Count > 0) {
                        var last = creatableTypes.IndexOf(PreferencesManager.Get("lastLowLevelType", ""));
                        createTypeDropdown.SetValue(last < 0 ? 0 : last);
                        SetCreateType(last < 0 ? 0 : last);
                    }
                }
                shownAnything = false;
            } else if (!active && !keepActive && ActiveSelf()) {
                Terminate(true);
            }
            base.SetActive(active);
            if (active) {
                var element = GetElement();
                if (element != null) {
                    SetTitle(GO.GetName(element));
                    element.SetActive(true, true);
                    element.Select(true);
                    HighlightContents(element, true);
                    ShowContainerLines(element);
                    ShowMoveHandle(element);
                } else {
                    SetTitle(creationType != null ? Operations.LowLevel.GetTypeLabel(creationType) : SM.Get(creationGroup));
                }
            }
        }

        void OnFrame() {
            if (GetElement() != builtElement && lastCanvas != null) Rebuild();
            ShowSelectedPoint();
            UpdateContentOutline();
        }

        //built again while open (for another element, or to list what changed, e.g. the ribbons of a prop path): the new
        //pages are visible but not active until shown, so they are shown as the old ones were (on the same page), or hidden
        //while a panel is shown in place of this one (as the editor of a props container), except the list of containers,
        //which is rebuilt with this panel (its action is lost: this panel is shown again)
        void Rebuild() {
            var pickerShown = picker != null && picker.IsOpen();
            var page = keepActive ? (pickerShown ? System.Math.Max(lastPage, 0) : -1) : ActivePage();
            Initialize(lastCanvas);
            if (page >= 0) {
                keepActive = false;
                SetActive(true);
                ShowPage(page);
            } else {
                base.SetActive(false);
            }
        }

        //the points of the elements in a container are shown with it
        void HighlightContents(LowLevelElement element, bool show) {
            if (!element.GetLowLevelType()?.IsContainer ?? true) return;
            foreach (var child in builder.helper.elementManager.GetLowLevelChildren(element)) {
                child.SetActive(show, show);
                child.Select(show && child == highlighted);
            }
        }

        //a container gets a handle moving it with its contents, at their center (hidden with the element, or in another
        //click mode)
        void ShowMoveHandle(LowLevelElement element) {
            if (element is not PropPath && (!element.GetLowLevelType()?.IsContainer ?? true)) return;
            var center = Operations.LowLevel.CenterWithContents(builder.helper.elementManager, element);
            if (center.HasValue) element.ShowMoveHandle(true, center.Value);
        }

        //the elements in the container of the element (or in the element, if it's a container) and the container itself show
        //their lines shown with the container
        void ShowContainerLines(LowLevelElement element) {
            var container = element == null ? null : (element.GetLowLevelType()?.IsContainer ?? false) ? element : element.container;
            if (container == linesContainer) return;
            SetContainerLines(linesContainer, false);
            linesContainer = container;
            SetContainerLines(container, true);
        }

        void SetContainerLines(LowLevelElement container, bool show) {
            if (container == null || GO.IsNull(container) || container.IsDeleted()) return;
            container.generator.ShowContainerLines(show);
            foreach (var child in builder.helper.elementManager.GetLowLevelChildren(container)) child.generator.ShowContainerLines(show);
        }

        void OnElementCreated() {
            builder.NotifyChange();
            SetActive(true);
            if (GetElement()?.Settings?.spline != null) SetPointPlacementMode(1);
        }

        protected override void ReplaceTitle(string value) {
            var element = GetElement();
            if (string.IsNullOrEmpty(value)) {
                value = element != null ? GO.GetName(element) : "";
            } else if (element != null) {
                GO.SetName(element, value);
            }
            base.ReplaceTitle(value);
        }

        public override void Update() {
            if (GetElement() != builtElement && lastCanvas != null) Rebuild();
            shownAnything = false;
            ShowSelectedPoint();
        }

        public void Delete() {
            var element = GetElement();
            if (element != null) Operations.LowLevel.Delete(builder.helper.elementManager, element);
            builder.NotifyChange();
            Terminate();
        }

        public override void Terminate() {
            keepActive = false;
            Terminate(false);
        }

        public void Terminate(bool auto) {
            var placer = GetPlacer();
            var element = GetElement();
            if (placer != null) {
                placer.placementMode = ElementPlacer.LowLevelPlacer.PlacementMode.None;
                placer.onFrame = null;
                placer.onModeEnded = null;
            }
            HighlightContent(null);
            ShowContainerLines(null);
            if (element != null) {
                element.SetActive(false, false);
                element.Select(false);
                HighlightContents(element, false);
            }
            builder.helper.elementManager.ShowAnchors(false);
            if (!auto) SetActive(false);
            GameEngine.Gizmo.ClearTargets();
            if (!auto) builder.SetSelectObjectMode();
        }
    }
}

using System.Collections.Generic;
using Godot;
using GameObject = Godot.Control;
using SM = StringManager;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace EditorPanels {
    //The editor of a custom element (also used to create one: the type is chosen, then the element is placed with a
    //click). Its panel is the ui.json of the type, with these built-in groups:
    //mainGroup (end editing, delete, type), pointsGroup (selected point, point placement, array elements),
    //meshGroup (texture coordinates of the selected vertex), containerGroup (the container, changed from a list, the
    //contents and the merge into another container, chosen from a list), propsGroup (the prop rule, for the types with
    //props containers)
    public class CustomElementEditorPanel : EditorPanel, IPropRuleEditor, RuntimeGizmos.ITransformPanel {
        string creationTypeValue;
        public string creationGroup = "CREATE_CUSTOM_ELEMENT";
        public int contentsGroup = -1; //the list of the contents of its container it was opened from, to go back to
        public string creationType {
            get => creationTypeValue;
            set { creationTypeValue = value; builtType = "\0"; draft = null; creating = true; }
        }
        ObjectState draft;
        bool creating;
        ElementPlacer.CustomElementPlacer.PlacementMode? suspendedPlacement;
        CustomElement suspendedElement;
        string suspendedType;
        protected override bool SingleStateParameters => true;

        ObjectState CurrentState() => GetElement()?.state ?? draft;
        protected override bool EditsPlacerProps => CityElements.Types.Parsers.TypeParser.GetCustomElementTypes().TryGetValue(CurrentState()?.Str("type", "") ?? "", out var type) && type.typeData.settings.propPlacer != null;

        internal ObjectState CaptureCreationState() => draft == null ? null : (ObjectState)draft.Clone();

        internal void RestoreCreationState(ObjectState state) {
            if (state == null) return;
            draft = (ObjectState)state.Clone();
            if (GetPlacer() is ElementPlacer.CustomElementPlacer placer) {
                placer.createType = draft.Str("type");
                placer.creationState = draft;
            }
            Rebuild();
        }

        void EnsureDraft() {
            if (!creating || GetElement() != null || PreferencesManager.workingDirectory == "") return;
            creatableTypes.Clear();
            foreach (var entry in CityElements.Types.Parsers.TypeParser.GetCustomElementTypes()) {
                var settings = entry.Value.typeData.settings;
                if (settings.creatable && settings.createMenu?.direct != true &&
                    (settings.createMenu?.group ?? "CREATE_CUSTOM_ELEMENT") == creationGroup) creatableTypes.Add(entry.Key);
            }
            var type = creationType ?? GetPlacer()?.createType;
            if (creationType == null && !creatableTypes.Contains(type)) {
                var last = PreferencesManager.Get("lastCustomElementType", "");
                type = creatableTypes.Contains(last) ? last : creatableTypes.Count > 0 ? creatableTypes[0] : null;
            }
            if (type == null) return;
            if (draft?.Str("type") != type) draft = Operations.CustomElementPresets.CreationState(type);
            if (GetPlacer() is ElementPlacer.CustomElementPlacer placer) {
                placer.createType = type;
                Operations.PropertyContainers.Assign(draft, placer.propertyContainerId);
                placer.creationState = draft;
            }
        }
        string builtType = null; //the type the panel was built for
        string builtPropsType = null; //the type of the props shown in the panel, when it was built
        CustomElement builtElement = null; //and the element (its arrays, container and contents are listed)
        EditorPanelElements.Dropdown createTypeDropdown, placementDropdown, splineTypeDropdown;
        int[] splineCurveTypes;
        System.Action updateCurveFields;
        EditorPanelElements.Label selectedLabel;
        EditorPanelElements.ScrollList contentsList;
        EditorPanelElements.Button insertBeforeButton, insertAfterButton, removeButton, detachButton, addInnerButton, removeInnerButton, editContentButton,
            changeContainerButton, mergeButton;
        EditorPanelElements.InputField uField, vField;
        EditorPanelElements.Label spaceLabel; //the transform group of a point element
        (Vector3 position, Vector3 rotation, Vector3 scale)? shownTransform; //of its point when the fields were last read
        List<string> creatableTypes = new List<string>();
        //the arrays the placement modes add to: arrays of points get points at the end, arrays of groups get elements at
        //the start or end
        List<(string path, bool atStart)> appendArrays = new List<(string, bool)>();
        List<CustomElement> possibleContainers = new List<CustomElement>();
        List<CustomElement> contents = new List<CustomElement>();
        List<CustomElement> mergeTargets = new List<CustomElement>();
        TerrainPoint shownPoint = null;
        bool shownAnything = false;
        bool shownDerived = false; //the points were computed (see CustomElement.IsDerived) when last shown
        bool shownHandle = false; //the move handle of the container was selected when last shown
        readonly PropRuleGroup propRule;
        EditorPanelElements.Label propPlacementErrorLabel;
        //the content picked in the contents dropdown, outlined (see HighlightedGeometry) and with its points selected
        CustomElement highlighted = null;
        //the container whose elements (and itself) show their lines shown with the container (e.g. the BAI lanes of a room)
        CustomElement linesContainer = null;
        //the list of containers to change the container to, or to merge into
        CustomElementPickerPanel picker;
        ElementPlacer.TerrainPointAttachmentPicker pointAttachmentPicker;
        EditorPanelElements.Label pointAttachmentStatus;
        EditorPanelElements.Button pointAttachButton, pointLineButton, pointColumnButton, pointDetachButton;

        public CustomElementEditorPanel() {
            AddComplexElement(new PresetSelector(this));
            propRule = new PropRuleGroup(this, _ => CurrentState(), () => {
                ReadCurValues();
                builder.NotifyChange();
            });
        }

        public override void Initialize(GameObject canvas) {
            builder = canvas as CityBuilderMenuBar;
            EnsureDraft();
            var element = GetElement();
            builtType = element?.TypeName ?? draft?.Str("type", null);
            builtElement = element;
            //the fields of the previous build are freed, and the groups of this type may not create them again
            createTypeDropdown = placementDropdown = splineTypeDropdown = null;
            updateCurveFields = null;
            selectedLabel = null;
            contentsList = null;
            insertBeforeButton = insertAfterButton = removeButton = detachButton = addInnerButton = removeInnerButton = editContentButton = changeContainerButton = mergeButton = null;
            uField = vField = null;
            spaceLabel = null;
            pointAttachmentStatus = null;
            pointAttachButton = pointLineButton = pointColumnButton = pointDetachButton = null;
            shownTransform = null;
            propPlacementErrorLabel = null;
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
            InitializeWithCustomParameters<CityElements.Types.Runtime.CustomElementType, CityElements.Types.CustomElementType>(canvas, GetElementState, null,
                builtType, CityElements.Types.Parsers.TypeParser.GetCustomElementTypes, ProcessCustomParts, true);
            builtPropsType = propRule.InlineType();
            picker = AddChildPanel<CustomElementPickerPanel>(canvas);
        }

        IObjectWithState GetElementState() {
            return (IObjectWithState)GetElement() ?? draft;
        }

        //the panel shown while choosing the type of a new element
        void InitializeCreation(GameObject canvas) {
            Initialize(canvas, 1, 1.5f);
            if (PreferencesManager.workingDirectory == "") return;
            var p = GetPage(0);
            p.AddButton(SM.Get("END_EDITING"), Terminate, 1.5f);
            p.IncreaseRow();
            if (creationType != null) {
                p.AddLabel(Operations.CustomElements.GetTypeLabel(creationType) + ": " + SM.Get("CE_CLICK_TO_CREATE"), 1.5f);
                return;
            }
            creatableTypes.Clear();
            var names = new List<string>();
            foreach (var type in CityElements.Types.Parsers.TypeParser.GetCustomElementTypes()) {
                if (!type.Value.typeData.settings.creatable) continue;
                var menu = type.Value.typeData.settings.createMenu;
                if (menu?.direct == true || (menu?.group ?? "CREATE_CUSTOM_ELEMENT") != creationGroup) continue;
                creatableTypes.Add(type.Key);
                names.Add(Operations.CustomElements.GetTypeLabel(type.Key));
            }
            if (creatableTypes.Count == 0) {
                p.AddLabel(SM.Get("CE_NO_TYPES"), 1.5f);
                return;
            }
            createTypeDropdown = p.AddDropdown(SM.Get("CE_TYPE"), names, SetCreateType, 1.5f);
            p.IncreaseRow();
            p.AddLabel(SM.Get("CE_CLICK_TO_CREATE"), 1.5f);
            p.IncreaseRow();
        }

        //a parameter giving the bounds of a strip whose props don't use them
        bool HidesBoundsParameter(CityElements.Types.CustomElementSettings settings, string name) {
            foreach (var strip in settings.propPlacer?.strips ?? new CityElements.Types.PropPlacerStrip[0]) {
                if (strip.boundsParameters == null || System.Array.IndexOf(strip.boundsParameters, name) < 0) continue;
                var props = Operations.Props.GetPropRule(CurrentState(), false)?.State(strip.name);
                if (Operations.Props.TypeOf(props)?.typeData.lineOnly == true) return true;
            }
            return false;
        }

        //the type of the props of the panel changed: it has other fields
        protected override void Reinitialize() {
            Rebuild();
        }

        void SetCreateType(int i) {
            var placer = GetPlacer();
            if (placer == null || i < 0 || i >= creatableTypes.Count) return;
            placer.createType = creatableTypes[i];
            placer.placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.Create;
            PreferencesManager.Set("lastCustomElementType", creatableTypes[i]);
            draft = null;
            EnsureDraft();
            builtType = "\0";
            builder.DoDelayed(Rebuild);
        }

        bool ProcessCustomParts(CityElements.Types.TabElement elem, EditorPanelPage p, PresetSelector pS,
            TypeSelector<CityElements.Types.Runtime.CustomElementType> tS, CityElements.Types.Runtime.CustomElementType type) {

            var w = elem.width;
            var settings = type.typeData.settings;
            if (HidesBoundsParameter(settings, elem.name)) return true;
            var objectParameter = System.Array.Find(type.parameterContainer.parameters ?? System.Array.Empty<CityElements.Types.Parameter>(), parameter => parameter.fullName() == elem.name && parameter.type == "objectInstance");
            if (GetElement() == null && objectParameter != null) {
                p.EnsureNewRow();
                var key = objectParameter.fullName();
                var state = draft.State(key);
                if (state == null) {
                    state = new ObjectState();
                    state.SetStr("meshPath", objectParameter.objectInstanceSettings.defaultModel?.model ?? "");
                    draft.SetState(key, state);
                }
                p.AddFieldMeshField(builder, SM.Get(objectParameter.label), SM.Get("NONE"), () => draft.State(key), "properties.meshPath", null, true, w)
                    .SetInteractable(objectParameter.objectInstanceSettings.allowCustomModel);
                p.IncreaseRow();
                return true;
            }
            if (elem.name == "splineGroup" && settings.spline != null) {
                p.IncreaseRow();
                var allowed = settings.spline.curveTypes;
                splineCurveTypes = allowed;
                var dropdown = splineTypeDropdown = p.AddDropdown(SM.Get("RD_SPLINE_TYPE"), new List<int>(allowed).ConvertAll(i => SM.Get(SplineCurves.Label(i))), i => {
                    CurrentState().SetInt("curveType", allowed[i]); builder.NotifyChange();
                    updateCurveFields?.Invoke();
                }, w);
                dropdown.actionEnabled = false;
                dropdown.SetValue(System.Array.IndexOf(allowed, CurrentState().Int("curveType", settings.spline.defaultCurveType)));
                dropdown.actionEnabled = true;
                p.IncreaseRow();
                var segmentsField = p.AddFieldInputField(SM.Get("SEGMENTS"), SM.Get("SEGMENTS_PH"), GameEngine.InputContentType.Integer, GetElementState,
                    "state.properties.segments", null, w / 2, null);
                var segmentsPer100mField = p.AddFieldCheckbox(SM.Get("RD_SEGMENTS_100M"), GetElementState, "state.properties.segmentsPer100m", null, w / 2, null);
                //presets never store instance specific values
                var editingPreset = EditingPreset();
                if (editingPreset) { dropdown.SetInteractable(false); segmentsField.SetInteractable(false); segmentsPer100mField.SetInteractable(false); }
                p.IncreaseRow();
                updateCurveFields = () => {
                    var lowPoly = CurrentState()?.Int("curveType", settings.spline.defaultCurveType) == (int)GeometryHelper.CurveType.LowPoly;
                    segmentsField.SetActive(!lowPoly); segmentsPer100mField.SetActive(!lowPoly);
                };
                if (System.Array.IndexOf(allowed, 1) >= 0) {
                    var equallyField = p.AddFieldCheckbox(SM.Get("RD_SUBDIVIDE_EQUALLY"), GetElementState, "state.properties.subdivideEqually", null, w / 2, null);
                    var tensionField = p.AddFieldInputField(SM.Get("RD_HERMITE_TENSION"), SM.Get("RD_HERMITE_TENSION_PH"), GameEngine.InputContentType.Decimal,
                        GetElementState, "state.properties.hermiteTension", null, w / 2, null);
                    if (editingPreset) { equallyField.SetInteractable(false); tensionField.SetInteractable(false); }
                    updateCurveFields += () => {
                        var builtin = CurrentState()?.Int("curveType", settings.spline.defaultCurveType) == 1;
                        equallyField.SetActive(builtin); tensionField.SetActive(builtin);
                    };
                    p.IncreaseRow();
                }
                updateCurveFields();
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
            if (elem.name == "transformGroup" && settings.point != null) {
                p.EnsureNewRow();
                AddPointTransform(p, w, settings.point);
                return true;
            }
            var builtIn = elem.name == "mainGroup" || elem.name == "pointsGroup" || elem.name == "meshGroup" || elem.name == "containerGroup"
                || elem.name == "propsGroup";
            if (builtIn) p.EnsureNewRow(); //after a parameter sharing its row
            if (elem.name == "mainGroup") {
                var owner = Operations.PropertyContainers.Find(Operations.PropertyContainers.Scope(GetElement()?.state));
                var mainW = owner != null ? w / 3 : w * 0.5f;
                p.AddButton(SM.Get("END_EDITING"), Terminate, mainW);
                p.AddButton(SM.Get("DELETE"), Delete, mainW).SetInteractable(GetElement() != null);
                if (owner != null) p.AddButton(SM.Get("BACK"), BackToContainer, mainW);
                p.IncreaseRow();
                if (GetElement() == null && creationType == null && creatableTypes.Count > 0) {
                    createTypeDropdown = p.AddDropdown(SM.Get("CE_TYPE"), creatableTypes.ConvertAll(Operations.CustomElements.GetTypeLabel), SetCreateType, w);
                    createTypeDropdown.actionEnabled = false;
                    createTypeDropdown.SetValue(creatableTypes.IndexOf(type.name));
                    createTypeDropdown.actionEnabled = true;
                } else p.AddLabel(SM.Get("CE_TYPE") + ": " + Operations.CustomElements.GetTypeLabel(type.name), w);
                p.IncreaseRow();
                if (GetElement() == null && !EditingPreset()) {
                    p.AddLabel(SM.Get("CE_CLICK_TO_CREATE"), w);
                    p.IncreaseRow();
                }
                var category = Operations.CustomElementPresets.Category(type.name);
                if (category != null) {
                    pS.AddPresetLoadAndSaveDropdown(p, SM.Get("PRESET"), true, category,
                        value => {
                            if (GetElement() is CustomElement element) Operations.CustomElementPresets.Apply(element, value);
                            else Operations.CustomElementPresets.Apply(CurrentState(), value, type.name);
                            if (!EditingPreset()) Operations.CustomElementPresets.Remember(type.name, value.Name);
                            return CurrentState();
                        }, () => GetElement() is CustomElement element ? Operations.CustomElementPresets.Extract(element)
                            : CurrentState() is ObjectState state ? Operations.CustomElementPresets.Extract(state, type.name) : null, false,
                        name => {
                            CurrentState().Name = name;
                            if (!EditingPreset()) Operations.CustomElementPresets.Remember(type.name, name);
                        }, null, () => { propRule.Read(); Update(); }, w);
                }
                var element = GetElement();
                if (element?.source != null && Operations.CustomElements.IsGhost(element.source)) {
                    p.AddButton(SM.Get("CE_RESTORE_SOURCE"), RestoreSource, w, SM.Get("CE_RESTORE_SOURCE_TOOLTIP"));
                    p.IncreaseRow();
                }
            } else if (elem.name == "pointsGroup") {
                selectedLabel = p.AddLabel("", w);
                p.IncreaseRow();
                var modes = new List<string> { SM.Get("CE_PM_NONE") };
                appendArrays.Clear();
                var element = GetElement();
                if (element != null) {
                    foreach (var (path, list) in CustomElements.PointTree.Lists(element.points)) {
                        if (!list.def.array.editable) continue;
                        var key = CustomElements.PointTree.Wildcard(path);
                        //a uniform array is the same everywhere: its first instance stands for the others
                        if (list.def.array.uniform && appendArrays.Exists(a => CustomElements.PointTree.Wildcard(a.path) == key)) continue;
                        var name = ArrayLabel(element, path);
                        if (list.def.children == null) {
                            appendArrays.Add((path, false));
                            modes.Add(SM.Get("CE_PM_APPEND").Replace("$ARRAY", name));
                        } else {
                            appendArrays.Add((path, false));
                            modes.Add(SM.Get("CE_PM_APPEND_END").Replace("$ARRAY", name));
                            appendArrays.Add((path, true));
                            modes.Add(SM.Get("CE_PM_APPEND_START").Replace("$ARRAY", name));
                        }
                    }
                }
                placementDropdown = p.AddDropdown(SM.Get("CE_POINT_PLACEMENT_MODE"), modes, SetPlacementMode, w);
                p.IncreaseRow();
                insertBeforeButton = p.AddButton(SM.Get("CE_INSERT_BEFORE"), () => InsertArrayElement(false), w * 0.5f, SM.Get("CE_INSERT_TOOLTIP"));
                insertAfterButton = p.AddButton(SM.Get("CE_INSERT_AFTER"), () => InsertArrayElement(true), w * 0.5f, SM.Get("CE_INSERT_TOOLTIP"));
                p.IncreaseRow();
                removeButton = p.AddButton(SM.Get("CE_REMOVE_ELEMENT"), RemoveArrayElement, w * 0.5f, SM.Get("CE_REMOVE_ELEMENT_TOOLTIP"));
                detachButton = p.AddButton(SM.Get("CE_DETACH_POINT"), DetachPoint, w * 0.5f, SM.Get("CE_DETACH_POINT_TOOLTIP"));
                p.IncreaseRow();
                //the buttons above act on the outermost array (e.g. the sections), these on the innermost one (the lanes)
                if (element != null && CustomElements.PointTree.HasNestedArrays(element.points.def)) {
                    addInnerButton = p.AddButton(SM.Get("CE_ADD_INNER"), AddInnerArrayElement, w * 0.5f, SM.Get("CE_ADD_INNER_TOOLTIP"));
                    removeInnerButton = p.AddButton(SM.Get("CE_REMOVE_INNER"), RemoveInnerArrayElement, w * 0.5f, SM.Get("CE_REMOVE_INNER_TOOLTIP"));
                    p.IncreaseRow();
                }
            } else if (elem.name == "meshGroup") {
                if (settings.mesh?.uvs == "editable") {
                    uField = p.AddInputField("U", "U", "", GameEngine.InputContentType.Decimal, x => SetUV(0, x), w * 0.5f);
                    vField = p.AddInputField("V", "V", "", GameEngine.InputContentType.Decimal, x => SetUV(1, x), w * 0.5f);
                    p.IncreaseRow();
                }
            } else if (elem.name == "propPlacerGroup") {
                propPlacementErrorLabel = p.AddLabel("", w);
                p.IncreaseRow();
            } else if (elem.name == "propsGroup") {
                if (settings.propsContainers != null && settings.propsContainers.Length > 0) {
                    var presetTypes = settings.propsPresetType != null ? new List<string> { settings.propsPresetType } : null;
                    propRule.Add(p, GetComplexElement<PresetSelector>(), settings.propsContainers, presetTypes, w);
                }
            } else if (elem.name == "containerGroup") {
                var element = GetElement();
                if (element == null) return true;
                possibleContainers = Operations.CustomElements.GetPossibleContainers(builder.helper.elementManager, element);
                possibleContainers.Sort((a, b) => string.Compare(GO.GetName(a), GO.GetName(b), System.StringComparison.Ordinal));
                if (possibleContainers.Count > 0 || element.container != null) {
                    p.AddLabel(SM.Get("CE_CONTAINER") + ": " + ContainerName(element), w * 0.5f);
                    p.AddButton(SM.Get("CE_EDIT_CONTAINER"), EditContainer, w * 0.25f).SetInteractable(element.container != null);
                    changeContainerButton = p.AddButton(SM.Get("CE_CHANGE_CONTAINER"), OpenContainerChange, w * 0.25f);
                    p.IncreaseRow();
                }
                if (type.IsContainer) {
                    contents = builder.helper.elementManager.GetCustomElementChildren(element);
                    var names = new List<string>();
                    var prefix = GO.GetName(element) + " "; //not repeated in the contents named after the container
                    foreach (var c in contents) {
                        names.Add(Operations.CustomElements.GetListLabel(c, prefix));
                    }
                    contentsList = p.AddScrollList(SM.Get("CE_CONTENTS").Replace("$NUMBER", contents.Count.ToString()), names, PickContent, w);
                    builder.listHighlight.Add(contentsList, _ => HighlightedGeometry());
                    p.IncreaseRow(5.0f);
                    editContentButton = p.AddButton(SM.Get("EDIT"), EditContent, w);
                    editContentButton.SetInteractable(false); //until a content is picked
                    p.IncreaseRow();
                    mergeTargets = new List<CustomElement>();
                    foreach (var other in builder.helper.elementManager.customElements) {
                        //the containers that can hold all the contents (e.g. a group of building parts into a room)
                        if (other == element || other.IsDeleted() || !(other.GetCustomElementType()?.IsContainer ?? false)) continue;
                        if (contents.TrueForAll(c => c == other || c.CanBeIn(other))) mergeTargets.Add(other);
                    }
                    mergeTargets.Sort((a, b) => string.Compare(GO.GetName(a), GO.GetName(b), System.StringComparison.Ordinal));
                    if (mergeTargets.Count > 0) {
                        mergeButton = p.AddButton(SM.Get("CE_MERGE_INTO"), OpenMerge, w, SM.Get("CE_MERGE_TOOLTIP"));
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
        static string ArrayLabel(CustomElement element, string arrayPath) {
            var list = CustomElements.PointTree.Find(element.points, arrayPath) as CustomElements.PointList;
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
                text = SM.Get("CE_MOVE_HANDLE_SELECTED");
            } else if (derived) {
                //computed from other elements: they can't be edited
                text = SM.Get("CE_POINTS_DERIVED");
                if (GetPlacer() != null && GetPlacer().placementMode != ElementPlacer.CustomElementPlacer.PlacementMode.None) SetPointPlacementMode(0);
            } else if (point == null || element == null) {
                text = SM.Get("CE_NO_POINT_SELECTED");
            } else {
                var path = CustomElements.PointTree.PathOf(element.points, point);
                var vertex = element.meshVertices.IndexOf(point);
                text = path ?? (vertex >= 0 ? SM.Get("CE_VERTEX") + " " + vertex : "");
                var shared = point.GetLinks().Count - 1;
                if (shared > 0) text += " (" + SM.Get("CE_SHARED").Replace("$NUMBER", shared.ToString()) + ")";
                if (!GO.IsNull(point.anchor) && !point.anchor.IsDeleted()) {
                    var anchorText = point.anchor is ColumnAnchor ? "CE_ON_COLUMN" : point.anchor is LineAnchor ? "CE_ON_LINE"
                        : point.anchor is PointAnchor ? "CE_ON_POINT" : "CE_ANCHORED";
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
        void UpdateArrayButtons(CustomElement element, TerrainPoint point) {
            var editable = !(element?.IsDerived ?? false);
            placementDropdown?.SetInteractable(editable && element != null);
            var arrayElement = Operations.CustomElements.ArrayElementOf(element, point, true);
            var innerElement = Operations.CustomElements.ArrayElementOf(element, point, false);
            if (innerElement.HasValue && innerElement.Value.arrayPath == arrayElement?.arrayPath) innerElement = null;
            void Set(EditorPanelElements.Button button, string key, (string arrayPath, int index)? shown, bool enabled) {
                if (button == null) return;
                var name = shown.HasValue ? ArrayLabel(element, shown.Value.arrayPath) : null;
                button.SetTitle(name != null ? SM.Get(key + "_IN").Replace("$ARRAY", name) : SM.Get(key));
                button.SetInteractable(enabled && editable);
            }
            Set(insertBeforeButton, "CE_INSERT_BEFORE", arrayElement, arrayElement.HasValue);
            Set(insertAfterButton, "CE_INSERT_AFTER", arrayElement, arrayElement.HasValue);
            var isVertex = point != null && element != null && element.meshVertices.Contains(point);
            Set(removeButton, "CE_REMOVE_ELEMENT", arrayElement, arrayElement.HasValue || isVertex);
            Set(addInnerButton, "CE_ADD_INNER", innerElement, innerElement.HasValue);
            Set(removeInnerButton, "CE_REMOVE_INNER", innerElement, innerElement.HasValue);
            detachButton?.SetInteractable(point != null && editable);
        }

        void SetPlacementMode(int mode) {
            var placer = GetPlacer();
            if (placer == null) return;
            if (mode == 0) {
                placer.placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.None;
            } else if (mode <= appendArrays.Count) {
                var (path, atStart) = appendArrays[mode - 1];
                var list = GetElement() != null ? CustomElements.PointTree.Find(GetElement().points, path) as CustomElements.PointList : null;
                placer.placementMode = list?.def.children != null ? ElementPlacer.CustomElementPlacer.PlacementMode.AppendElement
                    : ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint;
                placer.arrayPath = path;
                placer.atStart = atStart;
            }
            placer.UpdateAnchorVisibility();
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
                1 => ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint,
                2 => ElementPlacer.CustomElementPlacer.PlacementMode.PrependPoint,
                3 or 4 => ElementPlacer.CustomElementPlacer.PlacementMode.InsertPoint,
                _ => ElementPlacer.CustomElementPlacer.PlacementMode.None
            };
            placer.UpdateAnchorVisibility();
            GetElement()?.SetActive(true, true);
            placer.UpdatePlacementPoint();
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
                ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint => 1,
                ElementPlacer.CustomElementPlacer.PlacementMode.PrependPoint => 2,
                ElementPlacer.CustomElementPlacer.PlacementMode.InsertPoint => placer.atStart ? 4 : 3,
                _ => 0
            };
            switch (placer.placementMode) {
                case ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint: return appendArrays.IndexOf((placer.arrayPath, false)) + 1;
                case ElementPlacer.CustomElementPlacer.PlacementMode.AppendElement: return appendArrays.IndexOf((placer.arrayPath, placer.atStart)) + 1;
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
            var arrayElement = Operations.CustomElements.ArrayElementOf(element, point, outermost);
            if (!arrayElement.HasValue) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("CE_NOT_IN_ARRAY_ERROR"), SM.Get("OK"));
                return;
            }
            var (arrayPath, index) = arrayElement.Value;
            if (!Operations.CustomElements.InsertArrayElement(element, arrayPath, after ? index + 1 : index)) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("CE_ARRAY_FULL_ERROR"), SM.Get("OK"));
                return;
            }
            builder.NotifyChange();
        }

        void RemoveArrayElement() {
            var element = GetElement();
            var point = GetSelectedPoint();
            if (element == null || point == null) return;
            if (!Operations.CustomElements.RemovePoint(element, point)) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("CE_CANNOT_REMOVE_ERROR"), SM.Get("OK"));
                return;
            }
            GameEngine.Gizmo.ClearTargets();
            builder.NotifyChange();
        }

        //removes the element of the innermost array containing the selected point (e.g. its lane, in every section)
        void RemoveInnerArrayElement() {
            var element = GetElement();
            var point = GetSelectedPoint();
            var arrayElement = Operations.CustomElements.ArrayElementOf(element, point, false);
            if (!arrayElement.HasValue) return;
            if (!Operations.CustomElements.RemoveArrayElement(element, arrayElement.Value.arrayPath, arrayElement.Value.index)) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("CE_CANNOT_REMOVE_ERROR"), SM.Get("OK"));
                return;
            }
            GameEngine.Gizmo.ClearTargets();
            builder.NotifyChange();
        }

        void DetachPoint() {
            var element = GetElement();
            var point = GetSelectedPoint();
            if (element == null || point == null) return;
            var res = Operations.CustomElements.DetachPoint(builder.helper.elementManager, element, point);
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
            if (splineTypeDropdown != null && CurrentState() != null) {
                splineTypeDropdown.actionEnabled = false;
                splineTypeDropdown.SetValue(System.Array.IndexOf(splineCurveTypes, CurrentState().Int("curveType")));
                splineTypeDropdown.actionEnabled = true;
            }
            if (CurrentState() != null) updateCurveFields?.Invoke();
        }

        public EditorPanel OpenPropContainer(string name) {
            return propRule.OpenContainer(name);
        }

        // Containers

        string ContainerName(CustomElement element) {
            return element.container != null ? GO.GetName(element.container) : SM.Get("NONE");
        }

        //the containers the element can be moved to (none first, if it has one), in a list replacing the panel
        void OpenContainerChange() {
            var element = GetElement();
            if (element == null || picker == null) return;
            var targets = new List<CustomElement>();
            if (element.container != null) targets.Add(null);
            foreach (var c in possibleContainers) {
                if (c != element.container) targets.Add(c);
            }
            picker.Open(SM.Get("CE_CHANGE_CONTAINER_TITLE").Replace("$ELEMENT", GO.GetName(element)),
                SM.Get("CE_CONTAINERS").Replace("$NUMBER", targets.FindAll(c => c != null).Count.ToString()), targets,
                SM.Get("CE_CURRENT_CONTAINER").Replace("$CONTAINER", ContainerName(element)), SM.Get("CE_CHANGE_TO"), SetContainer);
        }

        void SetContainer(CustomElement container) {
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

        void HighlightContent(CustomElement content) {
            highlighted = content;
            foreach (var c in contents) {
                if (!GO.IsNull(c) && !c.IsDeleted()) c.Select(c == highlighted);
            }
        }

        Godot.Node HighlightedGeometry() {
            if (highlighted != null && (GO.IsNull(highlighted) || highlighted.IsDeleted())) highlighted = null;
            return highlighted != null ? GO.GetObject(highlighted.generator) : null;
        }

        void EditContent() {
            if (highlighted == null || GO.IsNull(highlighted) || highlighted.IsDeleted()) return;
            Edit(highlighted);
        }

        void Edit(CustomElement other) {
            Terminate();
            builder.SelectObject(other, true, null);
        }

        //the containers that can hold all the contents, in a list replacing the panel
        void OpenMerge() {
            var element = GetElement();
            if (element == null || picker == null) return;
            picker.Open(SM.Get("CE_MERGE_TITLE").Replace("$ELEMENT", GO.GetName(element)),
                SM.Get("CE_CONTAINERS").Replace("$NUMBER", mergeTargets.Count.ToString()), new List<CustomElement>(mergeTargets),
                null, SM.Get("CE_MERGE"), Merge);
        }

        void Merge(CustomElement target) {
            var element = GetElement();
            if (element == null || target == null || GO.IsNull(target) || target.IsDeleted()) return;
            if (!Operations.CustomElements.Merge(builder.helper.elementManager, element, target)) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("CE_MERGE_ERROR"), SM.Get("OK"));
                return;
            }
            builder.NotifyChange();
            Edit(target);
        }

        void RestoreSource() {
            var element = GetElement();
            var source = element?.source;
            if (source == null) return;
            builder.CreateAlert(SM.Get("WARNING"), SM.Get("CE_RESTORE_SOURCE_WARNING"), SM.Get("YES"), SM.Get("NO"), delegate {
                History.UndoManager.Record(SM.Get("CE_RESTORE_SOURCE"), () => {
                    Terminate();
                    Operations.CustomElements.RestoreGhost(builder.helper.elementManager, source);
                    builder.NotifyChange();
                });
            }, null);
        }

        // Panel

        ElementPlacer.CustomElementPlacer GetPlacer() {
            return builder?.terrainClick?.modifier as ElementPlacer.CustomElementPlacer;
        }

        public CustomElement GetElement() {
            return GetPlacer()?.GetElement();
        }

        public override object GetUndoTarget() {
            return GetElement();
        }

        public override void Hide(bool hide) {
            if (hide) CancelPointAttachment();
            var placer = GetPlacer() ?? builder.customElementPlacer;
            if (hide && !suspendedPlacement.HasValue) {
                suspendedPlacement = placer.placementMode;
                suspendedElement = placer.GetElement();
                suspendedType = placer.createType;
                placer.placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.None;
                placer.UpdatePlacementPoint();
            }
            base.Hide(hide);
            if (!hide && suspendedPlacement.HasValue) {
                if (GetPlacer() == placer && placer.GetElement() == suspendedElement && placer.createType == suspendedType
                    && placer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None) {
                    placer.placementMode = suspendedPlacement.Value;
                    if (placementDropdown != null) {
                        placementDropdown.actionEnabled = false;
                        placementDropdown.SetValue(GetPointPlacementMode());
                        placementDropdown.actionEnabled = true;
                    }
                    placer.UpdatePlacementPoint();
                }
                suspendedPlacement = null;
                suspendedElement = null;
            }
            placer.UpdateAnchorVisibility();
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            if (active && !keepActive) {
                var element = GetElement();
                EnsureDraft();
                //the panel is built for the type of the element (or for the creation)
                if ((element != builtElement || (element?.TypeName ?? draft?.Str("type", null)) != builtType) && lastCanvas != null) Initialize(lastCanvas);
                var placer = GetPlacer();
                if (placer != null) {
                    placer.onFrame = OnFrame;
                    placer.onModeEnded = () => SetPointPlacementMode(0);
                    placer.onElementCreated = OnElementCreated;
                }
                //the dropdown kept the mode of the last time (e.g. add after last, set on creation), not the placer's
                if (placementDropdown != null) {
                    placementDropdown.actionEnabled = false;
                    placementDropdown.SetValue(GetPointPlacementMode());
                    placementDropdown.actionEnabled = true;
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
                    ShowElement(element);
                } else {
                    SetTitle(creationType != null ? Operations.CustomElements.GetTypeLabel(creationType) : SM.Get(creationGroup), false);
                }
            }
            GetPlacer()?.UpdateAnchorVisibility(true);
        }

        //the element is shown as while edited: its points (and the ones of its contents) moveable, its lines, its move
        //handle (also after the point transform panel showed the anchors to attach a point)
        public void ShowElement(CustomElement element) {
            if (element == null || GO.IsNull(element) || element.IsDeleted()) return;
            element.SetActive(true, true);
            element.Select(true);
            HighlightContents(element, true);
            ShowContainerLines(element);
            ShowMoveHandle(element);
            EnableTransform(element);
            GetPlacer()?.UpdatePlacementPoint();
        }

        // Point elements: edited as the mesh instances, with the gizmo on the element (its point has no handle)

        static TerrainPoint PointOf(CustomElement element) {
            var leaves = element.GetLeaves();
            return leaves.Count == 1 && element.Settings?.point != null ? leaves[0].leaf.point : null;
        }

        static bool Any(bool[] axes) {
            return axes[0] || axes[1] || axes[2];
        }

        class PointPresetTransform {
            readonly ObjectState state;
            public PointPresetTransform(ObjectState state) { this.state = state; }
            public Vector3 rotation { get => state.Vector3("pointRotation"); set => state.SetVector3("pointRotation", value); }
            public Vector3 scale { get => state.Vector3("pointScale"); set => state.SetVector3("pointScale", value); }
        }

        object GetPointPresetTransform() => draft == null ? null : new PointPresetTransform(draft);

        void AddPointTransform(EditorPanelPage p, float w, CityElements.Types.CustomElementPointSettings point) {
            var moveAxes = point.PositionAxes();
            var rotateAxes = point.RotationAxes();
            var scaleAxes = point.ScaleAxes();
            if (GetElement() == null) {
                if (point.rotatable && point.presetRotation) {
                    p.AddFieldVectorInputField(SM.Get("TRANSF_P_ROT"), GetPointPresetTransform, "rotation", null, w)
                        .SetInteractable(rotateAxes[0], rotateAxes[1], rotateAxes[2]);
                    p.IncreaseRow();
                }
                if (point.scalable && point.presetScale) {
                    p.AddFieldVectorInputField(SM.Get("TRANSF_P_SCALE_VAL"), GetPointPresetTransform, "scale", null, w)
                        .SetInteractable(scaleAxes[0], scaleAxes[1], scaleAxes[2]);
                    p.IncreaseRow();
                }
                return;
            }
            var third = w / 3.0f;
            if (point.anchorable) {
                pointAttachmentStatus = p.AddLabel("", w);
                p.IncreaseRow();
                pointAttachButton = p.AddButton(SM.Get("POINT_ATTACH_CUSTOM_ELEMENT"), () => AttachPoint(ElementPlacer.TerrainPointAttachmentPicker.Target.Anchor), w * 0.65f, SM.Get("POINT_ATTACH_CUSTOM_ELEMENT_TOOLTIP"));
                pointDetachButton = p.AddButton(SM.Get("POINT_DETACH"), DetachElementPoint, w * 0.35f, SM.Get("POINT_DETACH_TOOLTIP"));
                p.IncreaseRow();
                pointLineButton = p.AddButton(SM.Get("POINT_ATTACH_LINE"), () => AttachPoint(ElementPlacer.TerrainPointAttachmentPicker.Target.Line), w * 0.5f, SM.Get("POINT_ATTACH_LINE_TOOLTIP"));
                pointColumnButton = p.AddButton(SM.Get("POINT_ATTACH_COLUMN"), () => AttachPoint(ElementPlacer.TerrainPointAttachmentPicker.Target.Column), w * 0.5f, SM.Get("POINT_ATTACH_COLUMN_TOOLTIP"));
                p.IncreaseRow();
                RefreshPointAttachment();
            }
            p.AddButton(SM.Get("TRANSF_P_SCALE"), () => SetGizmoMode(RuntimeGizmos.TransformType.Scale), third).SetInteractable(Any(scaleAxes));
            p.AddButton(SM.Get("TRANSF_P_ROTATE"), () => SetGizmoMode(RuntimeGizmos.TransformType.Rotate), third).SetInteractable(Any(rotateAxes));
            p.AddButton(SM.Get("TRANSF_P_MOVE"), () => SetGizmoMode(RuntimeGizmos.TransformType.Move), third).SetInteractable(Any(moveAxes));
            p.IncreaseRow();
            p.AddFieldCheckbox(SM.Get("TRANSF_P_SNAPTOGRID"), () => builder.gizmo, "snapToGrid", null, point.project ? w : w * 0.5f, SM.Get("TRANSF_P_SNAPTOGRID_TOOLTIP"));
            if (!point.project) p.AddButton(SM.Get("PROJECT_TO_GROUND"), ProjectPoint, w * 0.5f);
            p.IncreaseRow();
            spaceLabel = p.AddLabel(SM.Get("TRANSF_P_SPACE_LABEL_GLOBAL"), w * 0.5f);
            p.AddButton(SM.Get("TRANSF_P_SWITCH_SPACE"), SwitchSpace, w * 0.5f, SM.Get("TRANSF_P_SWITCH_SPACE_TOOLTIP"));
            p.IncreaseRow();
            p.AddFieldVectorInputField(SM.Get("TRANSF_P_POS"), GetPointTransform, "transform.position", null, w, null, null, value => Retarget(), fixedDragStep: 0.1)
                .SetInteractable(moveAxes[0], moveAxes[1], moveAxes[2]);
            p.IncreaseRow();
            if (point.rotatable) {
                p.AddFieldRotationVectorInputField(SM.Get("TRANSF_P_ROT"), GetPointTransform, "transform.rotation", null, w, null, null, value => Retarget())
                    .SetInteractable(rotateAxes[0], rotateAxes[1], rotateAxes[2]);
                p.IncreaseRow();
            }
            if (point.scalable) {
                p.AddFieldVectorInputField(SM.Get("TRANSF_P_SCALE_VAL"), GetPointTransform, "transform.localScale", null, w, null, null, value => Retarget())
                    .SetInteractable(scaleAxes[0], scaleAxes[1], scaleAxes[2]);
                p.IncreaseRow();
            }
        }

        object GetPointTransform() {
            var point = GetElement() is CustomElement element ? PointOf(element) : null;
            return point != null && !point.IsDeleted() ? new TransformPropertiesEditorPanel.TransformBinding(point.GetObject(), point, true) : null;
        }

        void RefreshPointAttachment() {
            var element = GetElement() ?? builtElement;
            var point = element != null ? PointOf(element) : null;
            if (pointAttachmentStatus == null || point == null || point.IsDeleted()) return;
            var picking = pointAttachmentPicker?.IsActive == true;
            var target = pointAttachmentPicker?.CurrentTarget;
            var attached = !GO.IsNull(point.anchor) && !point.anchor.IsDeleted();
            var key = picking ? target switch {
                ElementPlacer.TerrainPointAttachmentPicker.Target.Line => pointAttachmentPicker.LineStart == null ? "POINT_PICKING_LINE_START" : "POINT_PICKING_LINE_END",
                ElementPlacer.TerrainPointAttachmentPicker.Target.Column => "POINT_PICKING_COLUMN",
                _ => "POINT_PICKING_CUSTOM_ELEMENT"
            } : !attached ? "POINT_FREE" : point.anchor is LineAnchor ? "POINT_ON_SEGMENT" : point.anchor is ColumnAnchor ? "POINT_ON_COLUMN"
                : point.anchor is PointAnchor ? "POINT_ON_POINT" : "POINT_ANCHORED";
            pointAttachmentStatus.SetValue(SM.Get(key));
            pointAttachButton?.SetInteractable(!picking || target == ElementPlacer.TerrainPointAttachmentPicker.Target.Anchor);
            pointLineButton?.SetInteractable(!picking || target == ElementPlacer.TerrainPointAttachmentPicker.Target.Line);
            pointColumnButton?.SetInteractable(!picking || target == ElementPlacer.TerrainPointAttachmentPicker.Target.Column);
            pointDetachButton?.SetInteractable(attached && !picking);
        }

        void AttachPoint(ElementPlacer.TerrainPointAttachmentPicker.Target target) {
            if (CancelPointAttachment()) return;
            var element = GetElement();
            var point = element != null ? PointOf(element) : null;
            if (point?.anchorable != true) return;
            pointAttachmentPicker ??= new ElementPlacer.TerrainPointAttachmentPicker(builder);
            builder.helper.elementManager.ShowAnchors(true, true);
            element.SetActive(true, false);
            pointAttachmentPicker.Start(point, () => {
                builder.customElementPlacer.UpdateAnchorVisibility(true);
                if (ActiveSelf()) {
                    ShowElement(element);
                    ReadCurValues();
                    RefreshPointAttachment();
                }
            }, target, false);
            pointAttachmentPicker.onFrame = RefreshPointAttachment;
            builder.gizmo.ClearTargets();
            RefreshPointAttachment();
        }

        public bool CancelPointAttachment() {
            var active = pointAttachmentPicker?.IsActive == true;
            pointAttachmentPicker?.Stop();
            return active;
        }

        void DetachElementPoint() {
            var element = GetElement();
            var point = element != null ? PointOf(element) : null;
            if (point == null || !point.anchorable) return;
            Operations.TerrainPoints.Detach(point);
            builder.NotifyChange();
            Retarget();
            RefreshPointAttachment();
        }

        void SetGizmoMode(RuntimeGizmos.TransformType type) {
            builder.gizmo.transformType = type;
        }

        void SwitchSpace() {
            builder.gizmo.space = builder.gizmo.space == RuntimeGizmos.TransformSpace.Global ? RuntimeGizmos.TransformSpace.Local : RuntimeGizmos.TransformSpace.Global;
            UpdateSpaceLabel();
        }

        public void UpdateSpaceLabel() {
            spaceLabel?.SetValue(SM.Get("TRANSF_P_SPACE_LABEL_" + (builder.gizmo.space == RuntimeGizmos.TransformSpace.Global ? "GLOBAL" : "LOCAL")));
        }

        //puts the point on the surface it is projected on (it projects itself whenever it is moved, see CustomElement.RefreshPoints)
        void ProjectPoint() {
            var element = GetElement();
            var point = element != null ? PointOf(element) : null;
            if (point == null || element.Settings.point.project) return;
            var mask = element.Settings.PointProjection?.mask ?? element.Settings.point.projectionMask;
            Operations.TerrainPoints.Move(point, GeometryHelper.AutoProjectOnSurface(point.GetPoint(), CityGroundHelper.ExcludeHiddenTerrain(mask), element.generator), false);
            builder.NotifyChange();
            Retarget();
        }

        //the gizmo goes back on the point after its node was moved by a field
        void Retarget() {
            var element = GetElement();
            var point = element != null ? PointOf(element) : null;
            if (point == null) return;
            builder.gizmo.allowDeselect = true;
            builder.gizmo.ClearTargets();
            builder.gizmo.AddTarget(point.GetObject());
            ApplyGizmoAxes(element);
            builder.gizmo.allowDeselect = false;
        }

        void ApplyGizmoAxes(CustomElement element) {
            var settings = element.Settings?.point;
            if (settings == null) return;
            builder.gizmo.posAxesAllowed = settings.PositionAxes();
            builder.gizmo.rotAxesAllowed = settings.RotationAxes();
            builder.gizmo.scaleAxesAllowed = settings.ScaleAxes();
        }

        //the gizmo goes on the point of the element, with the modes (and keys) its type allows
        void EnableTransform(CustomElement element) {
            var point = PointOf(element);
            if (point == null || point.IsDeleted()) return;
            var gizmo = builder.gizmo;
            if (gizmo.mainTargetRoot != point.GetObject()) {
                gizmo.ClearTargets();
                gizmo.AddTarget(point.GetObject());
            }
            var settings = element.Settings.point;
            ApplyGizmoAxes(element);
            gizmo.SetMoveType = Any(settings.PositionAxes()) ? Key.W : Key.None;
            gizmo.SetRotateType = Any(settings.RotationAxes()) ? Key.E : Key.None;
            gizmo.SetScaleType = Any(settings.ScaleAxes()) ? Key.R : Key.None;
            gizmo.SetSpaceToggle = Key.S;
            gizmo.transformPanel = this;
            if (gizmo.transformType == RuntimeGizmos.TransformType.None) gizmo.transformType = RuntimeGizmos.TransformType.Move;
            gizmo.allowDeselect = false;
            UpdateSpaceLabel();
        }

        void DisableTransform() {
            if (builder?.gizmo == null || builder.gizmo.transformPanel != this) return;
            var gizmo = builder.gizmo;
            gizmo.transformType = RuntimeGizmos.TransformType.Move;
            gizmo.SetRotateType = Key.None;
            gizmo.SetMoveType = Key.None;
            gizmo.SetScaleType = Key.None;
            gizmo.SetSpaceToggle = Key.None;
            gizmo.transformPanel = null;
            gizmo.space = RuntimeGizmos.TransformSpace.Global;
            gizmo.allowDeselect = true;
            gizmo.snapToGrid = true;
        }

        //the fields show the transform of the point when the gizmo (or the projection) changed it
        public void RefreshTransform() {
            if (!ActiveSelf() || GetElement() is not CustomElement element || PointOf(element) is not TerrainPoint point || point.IsDeleted()) return;
            var current = (point.GetPoint(), point.GetRotation(), point.GetScale());
            if (shownTransform == current) return;
            shownTransform = current;
            ReadCurValues();
        }

        void OnFrame() {
            RefreshPointAttachment();
            var placementError = GetElement()?.propPlacementError is string error ? SM.Get(error) : "";
            propPlacementErrorLabel?.SetValue(placementError);
            if (builder.propsEditorPanel.parentPanel == this && builder.propsEditorPanel.ActiveSelf()) builder.propsEditorPanel.ShowError(placementError);
            if ((GetElement() != builtElement || (GetElement()?.TypeName ?? draft?.Str("type", null)) != builtType || propRule.InlineType() != builtPropsType) && lastCanvas != null) Rebuild();
            ShowSelectedPoint();
            RefreshTransform();
            GetPlacer()?.UpdatePlacementPoint();
        }

        //built again while open (for another element, or to list what changed): the new
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
        void HighlightContents(CustomElement element, bool show) {
            if (!element.GetCustomElementType()?.IsContainer ?? true) return;
            foreach (var child in builder.helper.elementManager.GetCustomElementChildren(element)) {
                child.SetActive(show, show);
                child.Select(show && child == highlighted);
            }
        }

        //a container gets a handle moving it with its contents, at their center (hidden with the element, or in another
        //click mode)
        void ShowMoveHandle(CustomElement element) {
            if (!element.GetCustomElementType()?.IsContainer ?? true) return;
            var center = Operations.CustomElements.CenterWithContents(builder.helper.elementManager, element);
            if (center.HasValue) element.ShowMoveHandle(true, center.Value);
        }

        //the elements in the container of the element (or in the element, if it's a container) and the container itself show
        //their lines shown with the container
        void ShowContainerLines(CustomElement element) {
            var container = element == null ? null : (element.GetCustomElementType()?.IsContainer ?? false) ? element : element.container;
            if (container == linesContainer) return;
            SetContainerLines(linesContainer, false);
            linesContainer = container;
            SetContainerLines(container, true);
        }

        void SetContainerLines(CustomElement container, bool show) {
            if (container == null || GO.IsNull(container) || container.IsDeleted()) return;
            container.generator.ShowContainerLines(show);
            foreach (var child in builder.helper.elementManager.GetCustomElementChildren(container)) child.generator.ShowContainerLines(show);
        }

        void OnElementCreated() {
            creating = false;
            draft = null;
            GetPlacer().creationState = null;
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
            if (contentsList != null && GetElement() is CustomElement container) {
                var current = builder.helper.elementManager.GetCustomElementChildren(container);
                if (current.Count != contents.Count || !current.TrueForAll(contents.Contains)) {
                    contents = current;
                    HighlightContent(null);
                    contentsList.Deselect();
                    contentsList.SetItems(contents.ConvertAll(c => Operations.CustomElements.GetListLabel(c, GO.GetName(container) + " ")));
                    editContentButton.SetInteractable(false);
                } else {
                    contentsList.UpdateLabels(contents.ConvertAll(c => Operations.CustomElements.GetListLabel(c, GO.GetName(container) + " ")));
                }
            }
            shownAnything = false;
            ShowSelectedPoint();
        }

        public void Delete() {
            var element = GetElement();
            if (element != null) Operations.CustomElements.Delete(builder.helper.elementManager, element);
            builder.NotifyChange();
            Terminate();
        }

        //to the panel of the city-property group owning the element, or to the list it was opened from
        void BackToContainer() {
            var owner = Operations.PropertyContainers.Find(Operations.PropertyContainers.Scope(GetElement()?.state));
            if (owner == null) return;
            var group = contentsGroup;
            var panel = Operations.PropertyContainers.Open(builder, owner);
            if (group >= 0) {
                if (panel is ArrayElemEditorPanel arrayPanel) arrayPanel.OpenContents(group);
                else if (panel is CityPropertiesEditorPanel cityPanel) cityPanel.OpenContents(group);
            }
        }

        public override void Terminate() {
            keepActive = false;
            Terminate(false);
        }

        public void Terminate(bool auto) {
            CancelPointAttachment();
            var placer = GetPlacer() ?? builder.customElementPlacer;
            var element = placer?.GetElement();
            if (placer != null) {
                placer.placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.None;
                placer.onFrame = null;
                placer.onModeEnded = null;
                placer.creationState = null;
            }
            draft = null;
            creating = false;
            contentsGroup = -1;
            suspendedPlacement = null;
            suspendedElement = null;
            HighlightContent(null);
            ShowContainerLines(null);
            if (element != null) {
                element.SetActive(false, false);
                element.Select(false);
                HighlightContents(element, false);
            }
            builder.helper.elementManager.ShowAnchors(false);
            if (!auto) SetActive(false);
            DisableTransform();
            GameEngine.Gizmo.ClearTargets();
            if (!auto) builder.SetSelectObjectMode();
        }
    }
}

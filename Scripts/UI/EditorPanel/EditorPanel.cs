using System.Collections;
using System.Collections.Generic;
using UIObject = Godot.Control;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using SM = StringManager;

public abstract partial class EditorPanel {
    protected abstract class ComplexElement {
        protected EditorPanel panel;
        public ComplexElement(EditorPanel panel) {
            this.panel = panel;
        }
        public abstract void SetActive(bool active);
        public abstract void Destroy();
        public abstract void ReadCurValues();
        public abstract void BaseExtraAction();
        public abstract void SyncState(ObjectState state);
    }

    protected CityBuilderMenuBar builder;
    public EditorPanel parentPanel;
    public string parentParamPrefix = "";
    List<EditorPanelPage> pages = new List<EditorPanelPage>();
    protected List<string> pageButtonNames = new List<string>();
    protected bool keepActive = false;
    protected bool titleActive = false;
    protected List<EditorPanelElements.TitleInputField> titleFields = new List<EditorPanelElements.TitleInputField>();
    public List<EditorPanel> childPanels = new List<EditorPanel>();
    protected UIObject lastCanvas = null;
    protected bool needsReloading = false;
    protected int lastPage = -1;
    protected Dictionary<System.Type, ComplexElement> complexElements = new Dictionary<System.Type, ComplexElement>();
    protected List<(EditorPanelElements.Button btn, CityElements.Types.Parameter param)> objectInstanceParameters = new List<(EditorPanelElements.Button btn, CityElements.Types.Parameter param)>();
    protected System.Func<IObjectWithState> getObject = null;
    private EditorPanels.ArrayElemEditorPanel arrayElemPanel = null;
    private EditorPanels.ArrayListPanel arrayListPanel = null;
    private Dictionary<string, CityElements.Types.Parameter> parameterMap = new Dictionary<string, CityElements.Types.Parameter>();
    //the object holding an array parameter, when it's not the one of the panel (e.g. the props of an element shown in its panel)
    private Dictionary<CityElements.Types.Parameter, System.Func<IObjectWithState>> parameterOwners = new Dictionary<CityElements.Types.Parameter, System.Func<IObjectWithState>>();
    bool initializationPending = true;
    bool initializedForActivation = false;
    readonly List<(EditorPanelElements.Button button, ConverterCatalog.ConverterInfo converter)> conversionButtons = new();
    internal bool IsInitialized => !initializationPending;

    //discard controls for the previous city without constructing the hidden panel for the next one
    public void DeferInitialize(UIObject canvas) {
        Destroy();
        lastCanvas = canvas;
        builder = canvas as CityBuilderMenuBar;
        initializationPending = true;
        initializedForActivation = false;
        keepActive = false;
        lastPage = -1;
    }

    //activation overrides often use their fields before base.SetActive, so they call this first
    protected bool PrepareActivation(bool active) {
        if (!initializationPending) return true;
        if (!active) return false;
        Initialize(lastCanvas);
        initializedForActivation = true;
        return true;
    }

    public abstract void Initialize(UIObject canvas);

    protected void Initialize(UIObject canvas, int numPages, float width = 1.5f) {
        Initialize(canvas, new List<int>() { numPages }, width);
    }

    protected bool InitializeWithCustomParameters<T, U>(UIObject canvas, System.Func<IObjectWithState> getObject,
        System.Func<TypeSelector<T>> TS, string fallbackType, System.Func<bool, Dictionary<string, T>> typesGetter,
        System.Func<CityElements.Types.TabElement, EditorPanelPage, PresetSelector, TypeSelector<T>, T, bool> processCustomParts,
        bool withPresetSelector, float width = 1.5f, bool titleActive = true)
        where T: CityElements.Types.Runtime.RuntimeType<U>
        where U: CityElements.Types.ITypeWithUI {

        if (PreferencesManager.workingDirectory == "") {
            Initialize(canvas, 1, width);
            return false;
        }
        this.getObject = getObject;
        objectInstanceParameters.Clear();
        this.titleActive = titleActive;
        var pS = withPresetSelector ? GetComplexElement<PresetSelector>() : null;
        T type;
        TypeSelector<T> tS = null;
        if (TS != null) {
            tS = TS.Invoke();
            var valid = tS.Initialize(typesGetter.Invoke(false), x => { return x.typeData.GetUI().label; }, true);
            if (!valid) {
                Initialize(canvas, 1, width);
                return false;
            }
            type = tS.types[tS.curType];
        } else {
            var types = typesGetter.Invoke(false);
            if (!types.ContainsKey(fallbackType)) {
                Initialize(canvas, 1, width);
                return false;
            } else {
                type = types[fallbackType];
            }
        }
        foreach (var tab in type.typeData.GetUI().tabs) {
            pageButtonNames.Add(tab.label != "" ? SM.Get(tab.label) : "");
        }
        var rows = new List<int>();
        var nTabs = pageButtonNames.Count;
        if (nTabs < 4) {
            rows.Add(nTabs);
        } else {
            var nTabsDiv = nTabs / 2;
            rows.Add(nTabs - nTabsDiv);
            rows.Add(nTabsDiv);
        }

        var totW = type.typeData.GetUI().menuWidth;
        Initialize(canvas, rows, totW);
        for (int tabI = 0; tabI < type.typeData.GetUI().tabs.Length; tabI++) {
            var tab = type.typeData.GetUI().tabs[tabI];
            var p = GetPage(tabI);
            var curW = 0.0f;
            foreach (var elem in tab.elements) {
                var w = elem.width;
                if (processCustomParts != null && processCustomParts.Invoke(elem, p, pS, tS, type)) continue;
                else if (elem.name.StartsWith("convert:")) AddConversionButton(p, ref curW, totW, elem, elem.name.Substring("convert:".Length));
                else if (elem.name.Split('_')[0] == "PRESET") {
                    var parts = elem.name.Split('_');
                    System.Func<ObjectState> getter = delegate { return getObject().GetState().GetContainer(parts[1]); };
                    System.Func<ObjectState, ObjectState> setter = x => { getObject().GetState().SetContainer(x, parts[1]); return getObject().GetState(); };
                    pS?.AddPresetLoadAndSaveDropdown(p, SM.Get(parts[1].ToUpper() + "_PRESET"), true, parts[1], setter, getter, true,
                        x => { var obj = getter.Invoke(); obj.Name = x; setter.Invoke(obj); }, null,
                        delegate { FlagParentChange(getObject); }, w);

                } else {
                    var parameters = type.typeData.GetParameters().parameters;
                    AddParameters(p, ref curW, totW, parameters, elem, getObject, true);
                }
            }
        }
        return true;
    }

    //a button converting the edited element with a converter of the core (not shown if the element can't be converted by it)
    void AddConversionButton(EditorPanelPage p, ref float curW, float totW, CityElements.Types.TabElement elem, string converterName) {
        var converter = ConverterCatalog.Get(converterName);
        var element = getObject?.Invoke();
        if (converter == null || element == null || !converter.Accepts(element)) return;
        PlaceInRow(p, ref curW, totW, elem.width);
        var button = p.AddButton(GetString(converter.label ?? converterName), delegate {
            builder.DoDelayed(() => builder.StartConversion(getObject?.Invoke(), converterName));
        }, elem.width, converter.tooltip != null ? GetString(converter.tooltip) : null);
        conversionButtons.Add((button, converter));
        button.SetInteractable(converter.IsEnabled(element));
    }

    //the text of a string key of the parameters (the child panels use the strings of their parent)
    protected virtual string GetString(string key) {
        return parentPanel != null ? parentPanel.GetString(key) : SM.Get(key);
    }

    //the parameter types that exist only for the inputs of the scripts (preset, file, folder) can be shown
    protected virtual bool AllowsScriptParameterTypes() {
        return parentPanel != null && parentPanel.AllowsScriptParameterTypes();
    }

    protected virtual bool SingleStateParameters => parentPanel?.SingleStateParameters ?? false;

    //the next element goes in the current row if it fits, otherwise in a new one
    protected static void PlaceInRow(EditorPanelPage p, ref float curW, float totW, float width) {
        curW += width;
        if (curW > totW) {
            p.IncreaseRow();
            curW = width;
        }
    }

    protected void AddParameters(EditorPanelPage p, ref float curW, float totW, CityElements.Types.Parameter[] parameters, CityElements.Types.TabElement elem, System.Func<IObjectWithState> getObject, bool prependstate) {
        //TODO: in practice only processes one parameter, rename to AddParameter and pass directly the correct parameter?
        PlaceInRow(p, ref curW, totW, elem.width);
        if (elem.name.StartsWith("label:")) {
            //the row can be shorter than the text (e.g. above buttons, which have a margin at their top)
            var height = elem.height > 0 ? elem.height : 0.4f;
            p.AddLabel(GetString(elem.name.Substring("label:".Length)), elem.width, null, null, System.Math.Max(height, 0.4f));
            p.EnsureCurRowHeight(height);
            return;
        }
        foreach (var param in parameters) {
            if (param.fullName() == elem.name) {
                var pFullName = Operations.States.GetParameterPath(param, prependstate);
                if (SingleStateParameters) pFullName = pFullName.Replace("instanceState.", "state.");
                parameterMap.Add(pFullName, param);
                parameterOwners[param] = getObject;
                EditorPanelElement field = null;
                switch (param.type) {
                    case "bool":
                        field = p.AddFieldCheckbox(GetString(param.label), getObject, pFullName, null, elem.width, GetString(param.tooltip));
                        break;
                    case "float":
                        field = p.AddFieldInputField(GetString(param.label), GetString(param.placeholder), GameEngine.InputContentType.Decimal, getObject, pFullName, null, elem.width, GetString(param.tooltip));
                        break;
                    case "texture":
                        field = p.AddFieldTextureField(builder, GetString(param.label), GetString(param.placeholder), getObject, pFullName, null, elem.width, GetString(param.tooltip));
                        break;
                    case "mesh":
                        field = p.AddFieldMeshField(builder, GetString(param.label), GetString(param.placeholder), getObject, pFullName, null, true, elem.width, GetString(param.tooltip));
                        break;
                    case "string":
                        field = p.AddFieldInputField(GetString(param.label), GetString(param.placeholder), GameEngine.InputContentType.Standard, getObject, pFullName, null, elem.width, GetString(param.tooltip));
                        break;
                    case "int":
                        field = p.AddFieldInputField(GetString(param.label), GetString(param.placeholder), GameEngine.InputContentType.Integer, getObject, pFullName, null, elem.width, GetString(param.tooltip));
                        break;
                    case "enum":
                        var typesNames = new List<string>();
                        foreach (var t in param.enumLabels) typesNames.Add(GetString(t));
                        field = p.AddFieldDropdown(GetString(param.label), typesNames, getObject, pFullName, null, elem.width, GetString(param.tooltip));
                        break;
                    case "objectInstance":
                        var objBtn = p.AddButton(GetString(param.label), delegate { EditObjectInstanceParameter(param.fullName(), new List<string>() { }); }, elem.width, GetString(param.tooltip));
                        objectInstanceParameters.Add((objBtn, param));
                        break;
                    case "customCityProperties":
                        p.AddButton(GetString(param.label), delegate { SelectCustomCityProperties(param); }, elem.width, GetString(param.tooltip), null);
                        break;
                    case "reference":
                        field = p.AddFieldReferenceField(builder, GetString(param.label), getObject, pFullName, param, getObject, elem.width, GetString(param.tooltip));
                        break;
                    case "preset":
                        if (!AllowsScriptParameterTypes()) break;
                        field = p.AddFieldNamedDropdown(GetString(param.label), ScriptCatalog.PresetNames(param.presetCategory), getObject, pFullName, null, elem.width, GetString(param.tooltip));
                        break;
                    case "file":
                    case "folder":
                        if (!AllowsScriptParameterTypes()) break;
                        field = p.AddFieldInputField(GetString(param.label), GetString(param.placeholder), GameEngine.InputContentType.Standard, getObject, pFullName, null, elem.width * 0.8f, GetString(param.tooltip));
                        p.AddButton("...", delegate { BrowsePath(param, getObject, pFullName); }, elem.width * 0.2f, GetString(param.tooltip));
                        break;
                    case "meshList":
                        AddMeshList(p, param, getObject, elem.width);
                        curW = 0;
                        break;
                    case "array":
                        if (OpensInSubPanel(param)) {
                            p.AddButton(GetString(param.label), delegate { OpenArrayList(param); }, elem.width, GetString(param.tooltip), null);
                            break;
                        }
                        if (!complexElements.ContainsKey(typeof(IOList))) {
                            AddComplexElement(new IOList(this));
                        }
                        System.Action<string, int> CloneArrayElementD = (x, y) => { CloneArrayElement(param, y); };
                        System.Action<string, int> MoveUpArrayElementD = (x, y) => { ShiftArrayElement(param, y, false); };
                        System.Action<string, int> MoveDownArrayElementD = (x, y) => { ShiftArrayElement(param, y, true); };
                        System.Action<string> AddArrayElementD = x => { AddArrayElement(param); };
                        System.Action<string, int> SelectArrayElementD = (x, y) => { SelectArrayElement(param, y); };
                        System.Action<string, int> DeleteArrayElementD = (x, y) => { DeleteArrayElement(param, y); };
                        System.Func<List<string>> GetArrayD = delegate { return GetArray(param); };
                        var extraButtons = new List<(string, System.Action<string, int>, IOListButtonMode)>() {
                            (SM.Get("ARRAY_CLONE"), CloneArrayElementD, IOListButtonMode.ElementSelectedAndNotFull),
                            (SM.Get("ARRAY_MOVE_UP"), MoveUpArrayElementD, IOListButtonMode.ElementSelectedNotFirst),
                            (SM.Get("ARRAY_MOVE_DOWN"), MoveDownArrayElementD, IOListButtonMode.ElementSelectedNotLast),
                        };
                        var l = GetComplexElement<IOList>();
                        var meshProperty = GetArrayMeshProperty(param);
                        l.AddFullEditableList(p, param.fullName(), GetString(param.label),
                            param.arrayProperties.hideAddButton ? null : SM.Get("ARRAY_ADD"), SM.Get("ARRAY_EDIT"), SM.Get("ARRAY_DELETE"), param.arrayProperties.maxElements,
                            AddArrayElementD, SelectArrayElementD, DeleteArrayElementD, GetArrayD, 5.0f, elem.width, GetString(param.tooltip), null, extraButtons,
                            meshProperty != null ? () => GetArrayMeshes(param, meshProperty) : null, i => ArrayElementNode(param, i));
                        if (param.arrayProperties.hint != null) {
                            p.AddLabel(GetString(param.arrayProperties.hint), elem.width);
                            p.IncreaseRow();
                        }
                        curW = 0;
                        break;
                }
                //presets never store instance specific values
                if (prependstate && EditingPreset() && (param.instanceSpecific || (SingleStateParameters && param.type == "reference"))) field?.SetInteractable(false);
                break;
            }
        }
    }

    //a meshList parameter: a list of mesh paths in the state, which can have empty entries
    void AddMeshList(EditorPanelPage p, CityElements.Types.Parameter param, System.Func<IObjectWithState> getOwner, float width) {
        if (!complexElements.ContainsKey(typeof(IOList))) AddComplexElement(new IOList(this));
        var name = param.fullName();
        var label = GetString(param.label);
        ObjectState State() => getOwner()?.GetState();
        List<string> Meshes() => new List<string>(State()?.Array<string>(name) ?? new string[0]);
        //the mesh selector calls back outside of the undo step of the button opening it
        void Change(System.Action change) {
            History.UndoManager.Record(label, () => {
                History.UndoManager.Touch(GetUndoTarget());
                change();
                builder.NotifyChange();
            });
            ReadCurValues();
        }
        var l = GetComplexElement<IOList>();
        var extraButtons = new List<(string, System.Action<string, int>, IOListButtonMode)>() {
            (SM.Get("ADD_EMPTY"), (x, y) => { Operations.Props.AddMesh(State(), name, ""); builder.NotifyChange(); }, IOListButtonMode.NotFull)
        };
        l.AddFullEditableList(p, name, label, SM.Get("ADD"), SM.Get("CHANGE"), SM.Get("DELETE"), param.arrayProperties?.maxElements ?? 0,
            x => builder.OpenMeshSelector(this, null, mesh => Change(() => Operations.Props.AddMesh(State(), name, mesh)), null, true),
            (x, i) => builder.OpenMeshSelector(this, null, mesh => Change(() => Operations.Props.SetMesh(State(), name, i, mesh)), null, true, Meshes()[i]),
            (x, i) => { Operations.Props.RemoveMesh(State(), name, i); builder.NotifyChange(); },
            Meshes, 5.0f, width, GetString(param.tooltip), null, extraButtons, Meshes);
    }

    //The panel part of a props state: its preset, its type (if it can have other types) and the fields of its type.
    //getState can give another object after an undo, a preset or a new selection.
    protected void AddPropsFields(EditorPanelPage p, ref float curW, float width, System.Func<ObjectState> getState, System.Action<ObjectState> applyPreset,
        List<string> allowedTypes, bool withPreset, bool nested = false) {
        var state = getState();
        var type = Operations.Props.TypeOf(state);
        if (type == null) return;
        if (withPreset) {
            if (!complexElements.ContainsKey(typeof(PresetSelector))) AddComplexElement(new PresetSelector(this));
            GetComplexElement<PresetSelector>().AddPresetLoadAndSaveDropdown(p, SM.Get("PROPS_PRESET"), true, Operations.Props.PresetCategory(type.name), x => {
                applyPreset(x);
                return getState();
            }, getState, true, x => { getState().Name = x; }, null, null, width);
        }
        if (allowedTypes != null && allowedTypes.Count > 1) {
            var names = allowedTypes.ConvertAll(t => CityElements.Types.Parsers.TypeParser.GetPropsTypes().TryGetValue(t, out var info) ? GetString(info.typeData.label) : t);
            var dropdown = p.AddDropdown(SM.Get("PROPS_TYPE"), names, i => {
                Operations.Props.SwitchType(getState(), allowedTypes[i]);
                builder.NotifyChange();
                Reinitialize();
            }, width);
            dropdown.actionEnabled = false;
            dropdown.SetValue(allowedTypes.IndexOf(type.name));
            dropdown.actionEnabled = true;
            p.IncreaseRow();
        }
        foreach (var ui in type.typeData.uiInfo ?? new CityElements.Types.TabElement[0]) {
            if (ui.boundsOnly && !nested && EditsPlacerProps && type.typeData.lineOnly) continue;
            AddParameters(p, ref curW, width, type.parameterContainer.parameters, ui, getState, false);
        }
        p.EnsureNewRow();
    }

    //the props edited are those of a prop placer, not of a road: the panels of its props ask the panel of the element
    protected virtual bool EditsPlacerProps => parentPanel != null && parentPanel.EditsPlacerProps;

    //rebuilds the panel staying on the tab shown (the new pages are all visible)
    protected virtual void Reinitialize() {
        var page = ActivePage();
        Initialize(lastCanvas);
        ReadCurValues();
        if (page >= 0 && pages.Count > 0) SetPage(System.Math.Min(page, pages.Count - 1));
    }

    //a file or folder parameter: chosen with the file dialog
    void BrowsePath(CityElements.Types.Parameter param, System.Func<IObjectWithState> getObject, string fieldName) {
        System.Action<string> set = path => {
            new FieldStatus(getObject, fieldName, null).SetValue(path);
            ReadCurValues();
        };
        if (param.type == "folder") {
            builder.fileBrowser.LoadFolder(set);
        } else {
            var extensions = param.extensions != null && param.extensions.Length > 0 ? param.extensions : new[] { "" };
            builder.fileBrowser.LoadFile(GetString(param.label), extensions, extensions[0], set);
        }
    }

    protected void AddComplexElement<T>(T elem) where T: ComplexElement {
        complexElements.Add(typeof(T), elem);
    }

    protected T GetComplexElement<T>() where T: ComplexElement {
        return (T)complexElements[typeof(T)];
    }

    //presets are edited on a dummy element, while actual elements are always edited through their placer
    protected bool EditingPreset() {
        var modifier = builder?.terrainClick?.modifier;
        return modifier != null && modifier is TerrainModifier.Null;
    }

    protected void SetTitle(string value, bool interactable = true) {
        foreach (var title in titleFields) {
            title.SetValue(value);
            title.SetInteractable(interactable);
        }
    }

    protected virtual void ReplaceTitle(string value) {
        foreach (var title in titleFields) {
            title.SetValue(value);
        }
    }

    protected void Initialize(UIObject canvas, List<int> numPages, float width = 1.5f) {
        if (canvas == null) return;
        lastCanvas = canvas;
        Destroy();
        initializationPending = false;
        builder = canvas as CityBuilderMenuBar; //TODO: check if correct
        var total = 0;
        foreach (var row in numPages) total += row;
        for(int i = 0; i < total; i++) {
            var page = new EditorPanelPage();
            page.Initialize(this, canvas, builder.NotifyChange);
            pages.Add(page);
            if (titleActive) {
                titleFields.Add(page.AddTitleInputField(SM.Get("TITLE_PH"), "", GameEngine.InputContentType.Standard, ReplaceTitle, width, null));
                page.IncreaseRow(0.5f);
            }
            if (total > 1) {
                var prevButtonsDone = 0;
                for (int j = 0; j < numPages.Count; j++) {
                    for (int k = 0; k < numPages[j]; k++) {
                        var l = prevButtonsDone + numPages[j] - 1 - k;
                        var buttonText = pageButtonNames.Count > 0 ? pageButtonNames[l] : "Page " + (l + 1);
                        var button = page.AddButton(buttonText, () => { SetPage(l); }, width / numPages[j]);
                        if (l == i) button.SetInteractable(false);
                    }
                    page.IncreaseRow(0.6f);
                    prevButtonsDone += numPages[j];
                }
                page.IncreaseRow(0.3f);
            }
        }
        pageButtonNames.Clear(); //for subsequent initializations
    }

    protected void AlterTaggedElements(string tag, System.Action<EditorPanelElement> action) {
        foreach (var page in pages) {
            page.AlterTaggedElements(tag, action);
        }
    }

    public virtual void Destroy() {
        conversionButtons.Clear();
        initializationPending = true;
        foreach (var page in pages) {
            page.Destroy();
        }
        pages.Clear();
        foreach (var panel in childPanels) {
            panel.Destroy();
        }
        childPanels.Clear();
        titleFields.Clear();
        objectInstanceParameters.Clear(); //their buttons were in the destroyed pages
        foreach (var elem in complexElements.Values) {
            elem.Destroy();
        }
        parameterMap.Clear();
        parameterOwners.Clear();
        arrayElemPanel = null;
        arrayListPanel = null;
    }

    protected EditorPanelPage GetPage(int i) {
        return pages[i];
    }

    protected virtual void ReadCurValues() {
        for (int i = 0; i < pages.Count; i++) {
            pages[i].ReadCurValues();
        }
        foreach (var elem in complexElements.Values) {
            elem.ReadCurValues();
        }
        CheckFieldsInteractability();
    }

    public void CheckFieldsInteractabilityDelayed() {
        if (objectInstanceParameters.Count == 0) return;
        builder.helper.menuBar.DoDelayed(CheckFieldsInteractability);
    }

    public void CheckFieldsInteractability() {
        if (objectInstanceParameters.Count == 0) return;
        var curObj = getObject?.Invoke();
        foreach (var elem in objectInstanceParameters) {
            var condition = elem.param.objectInstanceSettings.condition;
            var hasCondition = condition != null && condition != "";
            var enabled = true;
            if (hasCondition && curObj != null) {
                if (curObj is IObjectWithStateAndRuntimeType obj) {
                    enabled = obj.GetRuntimeBool(elem.param.objectInstanceSettings.condition);
                }
            }
            elem.btn.SetInteractable(enabled);
        }
    }

    public virtual void BaseExtraAction<T>(T p, System.Action<T> a) {
        a?.Invoke(p);
        foreach (var elem in complexElements.Values) {
            elem.BaseExtraAction();
        }
    }

    public virtual void Update() {
    }

    //shows the values of the fields again, after a change made elsewhere (e.g. by a placer)
    public void RefreshValues() {
        ReadCurValues();
    }

    public void UpdateConversionButtons() {
        foreach (var (button, converter) in conversionButtons) button.SetInteractable(converter.IsEnabled(getObject?.Invoke()));
    }

    //the city element edited from the panel, recorded before each change made from it for undo/redo
    //(the child panels edit a part of the element of their parent)
    public virtual object GetUndoTarget() {
        return parentPanel?.GetUndoTarget();
    }

    public virtual void SetActive(bool active) {
        if (!PrepareActivation(active)) return;
        var disabled = ActiveSelf() && !active;
        if (!active && builder?.arrayPointPlacer != null && builder.arrayPointPlacer.IsPlacingFrom(this)) builder.arrayPointPlacer.Stop();
        if (!active && builder?.referencePicker != null && builder.referencePicker.IsPickingFrom(this)) builder.referencePicker.Stop();
        foreach (var elem in complexElements.Values) {
            elem.SetActive(active);
        }
        if (needsReloading && active && !initializedForActivation) Initialize(lastCanvas);
        initializedForActivation = false;
        if (active) ReadCurValues();
        for (int i = 0; i < pages.Count; i++) {
            pages[i].SetActive(i == 0 ? active : false);
        }
        OnPageChanged();
    }

    public void ShowWithStack(Stack<GameObject> stack) {
        SetActive(true);
        if (stack == null || stack.Count == 0) return;
        var elem = stack.Pop();
        var arrayObject = GS.Cast<ArrayObject>(elem);
        if (arrayObject != null) {
            ShowParameterPage(arrayObject.paramInParent);
            SelectArrayElementByState(arrayObject.state, arrayObject.paramInParent, stack);
        }
    }

    protected virtual void ShowParameterPage(string parameter) { }

    private void SetPage(int page = 0) {
        for (int i = 0; i < pages.Count; i++) {
            pages[i].SetActive(i == page ? true : false);
        }
        OnPageChanged();
    }

    protected virtual void OnPageChanged() {
        builder?.NotifyVisibilityChange();
    }

    protected int ActivePage() {
        for (int i = 0; i < pages.Count; i++) {
            if (pages[i].ActiveSelf()) return i;
        }
        return -1;
    }

    public bool ActiveSelf() {
        return ActivePage() >= 0;
    }

    //the tab shown, or to show again when the child panel hiding it is closed
    public int GetShownPage() {
        var page = ActivePage();
        return page < 0 && keepActive ? lastPage : page;
    }

    public void ShowPage(int page) {
        if (ActiveSelf() && page >= 0 && page < pages.Count) SetPage(page);
    }

    //Reopening after an undo/redo (see OpenEditorState): returns how to reopen this panel from its parent panel, or
    //null if it can't be. The returned function gets the parent panel as reopened (child panels are recreated when
    //their parent is initialized again), identifies what the panel edits by indices and names (the undo can recreate
    //it), changes nothing if it's gone, and returns the reopened panel (null if it's gone).
    public virtual System.Func<EditorPanel, EditorPanel> CaptureReopen() {
        return null;
    }

    //shown, or hidden while one of its child panels is shown
    public bool IsOpen() {
        return ActiveSelf() || keepActive;
    }

    public void Toggle() {
       SetActive(!ActiveSelf());
    }

    public virtual void Hide(bool hide) {
        if (hide) {
            if (keepActive && !ActiveSelf()) return; //a deeper child must not replace the saved tab with -1
            lastPage = ActivePage();
            keepActive = true;
            SetActive(false);
        } else {
            SetActive(true);
            keepActive = false;
            if (pages.Count > 1 && lastPage >= 0) SetPage(lastPage);
        }
    }

    protected virtual void GoUp() {
        SetActive(false);
        parentPanel.Hide(false);
    }

    public virtual void Terminate() {
        if (parentPanel != null) {
            parentPanel.Terminate();
        }
        keepActive = false;
        SetActive(false);
        builder?.DeselectObject();
    }

    protected T AddChildPanel<T>(UIObject canvas) where T: EditorPanel, new() {
        var panel = new T();
        panel.parentPanel = this;
        panel.Initialize(canvas);
        panel.SetActive(false);
        childPanels.Add(panel);
        return panel;
    }

    public void SyncState(ObjectState state) {
        foreach (var element in complexElements) {
            element.Value.SyncState(state);
        }
        CheckFieldsInteractabilityDelayed();
    }

    void FlagParentChange(System.Func<IObjectWithState> getObject) {
        var obj = getObject.Invoke();
        if (obj != null) {
            obj.GetState().FlagAsChanged();
        }
    }

    protected int EditObjectInstanceParameter(string param, List<string> parentParams, EditorPanel returnPanel = null) {
        var obj = getObject.Invoke();
        if (obj != null) {
            if (obj is Road r) {
                var inst = r.GetParametricObject(param, parentParams);
                if (returnPanel == null) Terminate();
                builder.SelectObject(GO.GetObject(inst), true, null);
                return 2;
            } else if (obj is IParametricObjectContainer p) {
                var inst = p.FindParametricObject(param, parentParams);
                if (inst == null) return 0;
                builder.SetTransformPanelParent(returnPanel != null ? returnPanel : this);
                if (returnPanel == null) Hide(true);
                builder.SelectObject(GO.GetObject(inst), true, null);
                return 1;
            } else if (obj is ObjectState) { //note: the key will be the path...
                if (parentParamPrefix != null) parentParams.Add(parentParamPrefix);
                var res = parentPanel.EditObjectInstanceParameter(param, parentParams, returnPanel == null ? this : returnPanel);
                if (res == 1) {
                    Hide(true);
                } else if (res == 2) {
                    Terminate();
                }
                return res;
            } else {
                GameEngine.LogWarning("Unsupported object instance container: " + obj.GetType().ToString());
            }
        }
        return 0;
    }

    //the object holding an array parameter
    IObjectWithState OwnerOf(CityElements.Types.Parameter param) {
        return parameterOwners.TryGetValue(param, out var owner) ? owner?.Invoke() : getObject?.Invoke();
    }

    CityElements.Types.Parameter GetArrayLabelProperty(CityElements.Types.Parameter param) {
        var labelKey = GetArraylabel(param.arrayProperties);
        var elemProperties = GetArrayProperties(param.arrayProperties);
        CityElements.Types.Parameter labelProperty = null;
        foreach(var p in elemProperties ?? new CityElements.Types.Parameter[0]) {
            if (p.name == labelKey) {
                return p;
            }
        }
        return labelProperty;
    }

    CityElements.Types.Parameter GetArrayMeshProperty(CityElements.Types.Parameter param) {
        bool IsMesh(CityElements.Types.Parameter property) => property?.type == "mesh" || property?.type == "objectInstance";
        var label = GetArrayLabelProperty(param);
        if (IsMesh(label)) return label;
        CityElements.Types.Parameter result = null;
        foreach (var property in GetArrayProperties(param.arrayProperties) ?? new CityElements.Types.Parameter[0]) {
            if (!IsMesh(property)) continue;
            if (result != null) return null; //a record with several meshes needs a designated mesh label
            result = property;
        }
        return result;
    }

    List<string> GetArrayMeshes(CityElements.Types.Parameter param, CityElements.Types.Parameter meshProperty) {
        var result = new List<string>();
        var obj = OwnerOf(param);
        if (obj != null) {
            foreach (var elem in GetArrayList(param, obj)) result.Add(GetArrayElementLabel(elem, meshProperty, result.Count));
        }
        return result;
    }

    string GetArrayElementLabel(ObjectState elem, CityElements.Types.Parameter param, int curCount) {
        return Operations.States.GetParameterLabel(elem, param) ?? "Element " + (curCount + 1);
    }

    //an element of an array of props: the name of its preset (marked if it was changed), or the label of its type
    string GetPropsElementLabel(ObjectState elem) {
        if (string.IsNullOrEmpty(elem.Name)) return SM.GetOrKey(Operations.Props.TypeOf(elem)?.typeData.label ?? elem.Str("type"));
        var type = elem.Str("type");
        var preset = PresetManager.GetPresetByName(elem.Name, Operations.Props.PresetCategory(type));
        return elem.Name + (preset != null && !elem.Equals(preset) ? "*" : "");
    }

    List<ObjectState> GetArrayList(CityElements.Types.Parameter param, IObjectWithState obj) {
        return Operations.States.GetArray(obj.GetState(), param);
    }

    List<string> GetArray(CityElements.Types.Parameter param) {
        var obj = OwnerOf(param);
        if (obj != null) {
            var res = new List<string>();
            var arr = GetArrayList(param, obj);
            var labelProperty = GetArrayLabelProperty(param);
            foreach (var elem in arr) {
                res.Add(param.arrayProperties.propsTypes != null ? GetPropsElementLabel(elem) : GetArrayElementLabel(elem, labelProperty, res.Count));
            }
            return res;
        } else {
            return new List<string>();
        }
    }

    //the node with the parametric objects of an element of an array parameter of the element edited, if it has one
    Godot.Node ArrayElementNode(CityElements.Types.Parameter param, int i) {
        var arr = OwnerOf(param)?.GetState()?.Array<ObjectState>(param.fullName());
        if (arr == null || i < 0 || i >= arr.Length) return null;
        return (GetUndoTarget() as IParametricObjectContainer)?.FindArrayObject(arr[i]);
    }

    void AddArrayElement(CityElements.Types.Parameter param) {
        var obj = OwnerOf(param);
        if (obj != null) {
            Operations.States.AddArrayElement(obj.GetState(), param);
            builder.NotifyChange();
        }
    }

    void CloneArrayElement(CityElements.Types.Parameter param, int i) {
        var obj = OwnerOf(param);
        if (obj != null && Operations.States.CloneArrayElement(obj.GetState(), param, i)) {
            builder.NotifyChange();
        }
    }

    void ShiftArrayElement(CityElements.Types.Parameter param, int i, bool positive) {
        var obj = OwnerOf(param);
        if (obj != null && Operations.States.ShiftArrayElement(obj.GetState(), param, i, positive)) {
            builder.NotifyChange();
        }
    }

    void SelectArrayElement(CityElements.Types.Parameter param, int i) {
        var obj = OwnerOf(param);
        if (obj != null) {
            var arr = obj.GetState().Array<ObjectState>(param.fullName());
            if (i < 0 || i >= arr.Length) return;
            if (EditMeshDirectly(param, arr[i])) return;
            ShowArrayElementPanel(param, arr[i], i);
        }
    }

    //the element of an array of props whose only parameter is a mesh: a panel for it would only have a button, so the
    //selector is opened at once
    bool EditMeshDirectly(CityElements.Types.Parameter param, ObjectState elem) {
        if (param.arrayProperties.propsTypes == null || param.arrayProperties.propsTypes.Length != 1) return false;
        var subparams = GetSubparams(param, elem);
        if (subparams.Length != 1 || subparams[0].type != "mesh") return false;
        var name = subparams[0].fullName();
        builder.OpenMeshSelector(this, null, mesh => {
            History.UndoManager.Record(GetString(subparams[0].label), () => {
                History.UndoManager.Touch(GetUndoTarget());
                elem.SetStr(name, mesh);
                builder.NotifyChange();
            });
            ReadCurValues();
        }, null, true, elem.Str(name));
        return true;
    }

    protected CityElements.Types.Parameter[] GetSubparams(CityElements.Types.Parameter param) {
        return Operations.States.GetSubparams(param);
    }

    protected CityElements.Types.Parameter[] GetSubparams(CityElements.Types.Parameter param, ObjectState element) {
        return Operations.States.GetSubparams(param, element);
    }

    void SelectArrayElementByState(ObjectState state, string paramName, Stack<GameObject> stack = null) {
        var fullParamName = "state.properties." + paramName;
        if (!parameterMap.ContainsKey(fullParamName)) fullParamName = "properties." + paramName;
        if (parameterMap.ContainsKey(fullParamName)) { //TODO: not always like this?
            var param = parameterMap[fullParamName];
            if (OpensInSubPanel(param)) {
                OpenArrayList(param).SelectArrayElementByState(state, paramName, stack);
                return;
            }

            //check subparameters and stop if there's only one objectInstance
            //(since in that case it would open the parent which would go straight back to the current object)
            var subparams = GetSubparams(param);
            if (subparams == null || subparams.Length == 0 || (subparams.Length == 1 && subparams[0].type == "objectInstance")) return;

            var obj = OwnerOf(param);
            if (obj != null) {
                if (param.type == "array") {
                    var arr = obj.GetState().Array<ObjectState>(param.fullName());
                    for (int i = 0; i < arr.Length; i++) {
                        if (arr[i] == state) {
                            ShowArrayElementPanel(param, arr[i], i, stack);
                        }
                    }
                } else if (param.type == "customCityProperties") {
                    var subObj = obj.GetState().State(param.fullName());
                    if (subObj == state) {
                        ShowArrayElementPanel(param, subObj, null, stack);
                    }
                } else {
                    GameEngine.LogWarning("selection of parameter not supported: " + param.type);
                }
            }
        } else {
            GameEngine.LogWarning("parameter not found: " + paramName);
        }
    }

    //reopens the panel of an element of an array parameter (index) or of a custom city properties parameter (no index),
    //if it still exists
    public EditorPanel OpenArrayElement(string paramFullName, int? index) {
        CityElements.Types.Parameter param = null;
        foreach (var p in parameterMap.Values) {
            if (p.fullName() == paramFullName) {
                param = p;
                break;
            }
        }
        var obj = param == null ? null : OwnerOf(param);
        if (param == null || obj == null) return null;
        if (index.HasValue) {
            if (param.type != "array") return null;
            if (OpensInSubPanel(param)) return OpenArrayList(param).OpenArrayElement(paramFullName, index);
            var arr = obj.GetState().Array<ObjectState>(param.fullName());
            if (arr == null || index.Value < 0 || index.Value >= arr.Length) return null;
            ShowArrayElementPanel(param, arr[index.Value], index.Value);
        } else {
            if (param.type != "customCityProperties") return null;
            var subObj = obj.GetState().State(param.fullName());
            if (subObj == null) return null;
            ShowArrayElementPanel(param, subObj, null);
        }
        return arrayElemPanel;
    }

    //its panel shows a button instead of its list, the sub-panel shows the list itself
    bool OpensInSubPanel(CityElements.Types.Parameter param) {
        return param.arrayProperties?.subPanel == true && !(this is EditorPanels.ArrayListPanel);
    }

    //reopens the sub-panel of an array parameter, if it still has one
    public EditorPanel OpenArrayList(string paramFullName) {
        foreach (var p in parameterMap.Values) {
            if (p.fullName() == paramFullName) return OpensInSubPanel(p) ? OpenArrayList(p) : null;
        }
        return null;
    }

    EditorPanel OpenArrayList(CityElements.Types.Parameter param) {
        if (arrayListPanel == null) {
            arrayListPanel = new EditorPanels.ArrayListPanel();
            arrayListPanel.parentPanel = this;
            childPanels.Add(arrayListPanel);
        }
        arrayListPanel.InitializeWithData(lastCanvas, param, parameterOwners.TryGetValue(param, out var owner) ? owner : getObject);
        Hide(true);
        arrayListPanel.SetActive(true);
        return arrayListPanel;
    }

    void ShowArrayElementPanel(CityElements.Types.Parameter param, ObjectState elemState, int? i, Stack<GameObject> stack = null) {
        InitArrayElementPanel();
        arrayElemPanel.InitializeWithData(lastCanvas, param, elemState);
        arrayElemPanel.arrayIndex = i;
        if (i.HasValue) {
            arrayElemPanel.parentParamPrefix = param.fullName() + "." + i.ToString();
        } else {
            arrayElemPanel.parentParamPrefix = param.fullName();
        }
        Hide(true);
        arrayElemPanel.ShowWithStack(stack);
    }

    void InitArrayElementPanel() {
        if (arrayElemPanel != null) return;
        arrayElemPanel = new EditorPanels.ArrayElemEditorPanel();
        arrayElemPanel.parentPanel = this;
        childPanels.Add(arrayElemPanel);
    }

    void DeleteArrayElement(CityElements.Types.Parameter param, int i) {
        var obj = OwnerOf(param);
        if (obj != null && Operations.States.DeleteArrayElement(obj.GetState(), param, i)) {
            builder.NotifyVisibilityChange();
            builder.NotifyChange();
        }
    }

    CityElements.Types.Parameter[] GetArrayProperties(CityElements.Types.ArrayProperties type) {
        return Operations.States.GetArrayProperties(type);
    }

    string GetArraylabel(CityElements.Types.ArrayProperties type) {
        return Operations.States.GetArrayLabel(type);
    }

    void SelectCustomCityProperties(CityElements.Types.Parameter param, Stack<GameObject> stack = null) {
        var obj = OwnerOf(param);
        if (obj != null) {
            var subObj = Operations.States.EnsureCustomCityProperties(obj.GetState(), param);
            ShowArrayElementPanel(param, subObj, null, stack);
        }
    }
}

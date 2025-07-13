using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
    protected GameObject lastCanvas = null;
    protected bool needsReloading = false;
    protected int lastPage = -1;
    protected Dictionary<System.Type, ComplexElement> complexElements = new Dictionary<System.Type, ComplexElement>();
    protected List<(EditorPanelElements.Button btn, CityElements.Types.Parameter param)> objectInstanceParameters = new List<(EditorPanelElements.Button btn, CityElements.Types.Parameter param)>();
    protected System.Func<IObjectWithState> getObject = null;
    private EditorPanels.ArrayElemEditorPanel arrayElemPanel = null;
    private Dictionary<string, CityElements.Types.Parameter> parameterMap = new Dictionary<string, CityElements.Types.Parameter>();

    public abstract void Initialize(GameObject canvas);

    protected void Initialize(GameObject canvas, int numPages, float width = 1.5f) {
        Initialize(canvas, new List<int>() { numPages }, width);
    }

    protected bool InitializeWithCustomParameters<T, U>(GameObject canvas, System.Func<IObjectWithState> getObject,
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

    protected void AddParameters(EditorPanelPage p, ref float curW, float totW, CityElements.Types.Parameter[] parameters, CityElements.Types.TabElement elem, System.Func<IObjectWithState> getObject, bool prependstate) {
        //TODO: in practice only processes one parameter, rename to AddParameter and pass directly the correct parameter?
        curW += elem.width;
        if (curW > totW) {
            p.IncreaseRow();
            curW = elem.width;
        }
        foreach (var param in parameters) {
            if (param.fullName() == elem.name) {
                var pFullName = (prependstate ? (param.instanceSpecific ? "instanceState." : "state.") : "") + "properties." + param.fullName();
                parameterMap.Add(pFullName, param);
                switch (param.type) {
                    case "bool":
                        p.AddFieldCheckbox(SM.Get(param.label), getObject, pFullName, null, elem.width, SM.Get(param.tooltip));
                        break;
                    case "float":
                        p.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), InputField.ContentType.DecimalNumber, getObject, pFullName, null, elem.width, SM.Get(param.tooltip));
                        break;
                    case "texture":
                        p.AddFieldTextureField(builder, SM.Get(param.label), SM.Get(param.placeholder), getObject, pFullName, null, elem.width, SM.Get(param.tooltip));
                        break;
                    case "mesh":
                        p.AddFieldMeshField(builder, SM.Get(param.label), SM.Get(param.placeholder), getObject, pFullName, null, true, elem.width, SM.Get(param.tooltip));
                        break;
                    case "string":
                        p.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), InputField.ContentType.Standard, getObject, pFullName, null, elem.width, SM.Get(param.tooltip));
                        break;
                    case "int":
                        p.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), InputField.ContentType.IntegerNumber, getObject, pFullName, null, elem.width, SM.Get(param.tooltip));
                        break;
                    case "enum":
                        var typesNames = new List<string>();
                        foreach (var t in param.enumLabels) typesNames.Add(SM.Get(t));
                        p.AddFieldDropdown(SM.Get(param.label), typesNames, getObject, pFullName, null, elem.width, SM.Get(param.tooltip));
                        break;
                    case "objectInstance":
                        var objBtn = p.AddButton(SM.Get(param.label), delegate { EditObjectInstanceParameter(param.fullName(), new List<string>() { }); }, elem.width, SM.Get(param.tooltip));
                        objectInstanceParameters.Add((objBtn, param));
                        break;
                    case "customElement":
                        p.AddButton(SM.Get(param.label), delegate { SelectCustomElement(param); }, elem.width, SM.Get(param.tooltip), null);
                        break;
                    case "array":
                        if (!complexElements.ContainsKey(typeof(IOList))) {
                            AddComplexElement(new IOList(this));
                        }
                        System.Action<string, int> CloneArrayElementD = (x, y) => { CloneArrayElement(param, y); };
                        System.Action<string, int> MoveUpArrayElementD = (x, y) => { ShiftArrayElement(param, y, false); };
                        System.Action<string, int> MoveDownArrayElementD = (x, y) => { ShiftArrayElement(param, y, true); };
                        System.Action<string> AddArrayElementD = x => { AddArrayElement(param); };
                        System.Action<string, int> SelectArrayElementD = (x, y) => { SelectArrayElement(param, y); };
                        System.Action<string, int> DeleteArrayElementD = (x, y) => { DeleteArrayElement(param, y); };
                        System.Action<string, int> SwitchArrayClickAddModeD = delegate { SwitchArrayClickAddMode(); };
                        System.Func<List<string>> GetArrayD = delegate { return GetArray(param); };
                        var extraButtons = new List<(string, System.Action<string, int>, IOListButtonMode)>() {
                            (SM.Get("ARRAY_CLONE"), CloneArrayElementD, IOListButtonMode.ElementSelectedAndNotFull),
                            (SM.Get("ARRAY_MOVE_UP"), MoveUpArrayElementD, IOListButtonMode.ElementSelected),
                            (SM.Get("ARRAY_MOVE_DOWN"), MoveDownArrayElementD, IOListButtonMode.ElementSelected),
                        };
                        if (GetSubparams(param).Length == 1) { //todo: the placement itself
                            /*extraButtons.Add(
                                (SM.Get("ARRAY_CLICK_TO_ADD"), SwitchArrayClickAddModeD, IOListButtonMode.AlwaysEnabled)
                            );*/
                        }
                        var l = GetComplexElement<IOList>();
                        l.AddFullEditableList(p, param.fullName(), SM.Get(param.label),
                            SM.Get("ARRAY_ADD"), SM.Get("ARRAY_EDIT"), SM.Get("ARRAY_DELETE"), param.arrayProperties.maxElements,
                            AddArrayElementD, SelectArrayElementD, DeleteArrayElementD, GetArrayD, 5.0f, elem.width, SM.Get(param.tooltip), null, extraButtons);
                        curW = 0;
                        break;
                }
                break;
            }
        }
    }

    protected void AddComplexElement<T>(T elem) where T: ComplexElement {
        complexElements.Add(typeof(T), elem);
    }

    protected T GetComplexElement<T>() where T: ComplexElement {
        return (T)complexElements[typeof(T)];
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

    protected void Initialize(GameObject canvas, List<int> numPages, float width = 1.5f) {
        if (canvas == null) return;
        lastCanvas = canvas;
        Destroy();
        builder = canvas.GetComponent<CityBuilderMenuBar>();
        var total = 0;
        foreach (var row in numPages) total += row;
        for(int i = 0; i < total; i++) {
            var page = new EditorPanelPage();
            page.Initialize(this, canvas, builder.NotifyChange);
            pages.Add(page);
            if (titleActive) {
                titleFields.Add(page.AddTitleInputField(SM.Get("TITLE_PH"), "", InputField.ContentType.Standard, ReplaceTitle, width, null));
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
        foreach (var page in pages) {
            page.Destroy();
        }
        pages.Clear();
        foreach (var panel in childPanels) {
            panel.Destroy();
        }
        childPanels.Clear();
        titleFields.Clear();
        foreach (var elem in complexElements.Values) {
            elem.Destroy();
        }
        parameterMap.Clear();
        arrayElemPanel = null;
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

    public virtual void SetActive(bool active) {
        var disabled = ActiveSelf() && !active;
        foreach (var elem in complexElements.Values) {
            elem.SetActive(active);
        }
        if (needsReloading && active) Initialize(lastCanvas);
        if (active) ReadCurValues();
        for (int i = 0; i < pages.Count; i++) {
            pages[i].SetActive(i == 0 ? active : false);
        }
    }

    public void ShowWithStack(Stack<GameObject> stack) {
        SetActive(true);
        if (stack == null || stack.Count == 0) return;
        var elem = stack.Pop();
        var arrayObject = elem.GetComponent<ArrayObject>();
        if (arrayObject != null) {
            SelectArrayElementByState(arrayObject.state, arrayObject.paramInParent, stack);
        }
    }

    private void SetPage(int page = 0) {
        for (int i = 0; i < pages.Count; i++) {
            pages[i].SetActive(i == page ? true : false);
        }
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

    public void Toggle() {
       SetActive(!ActiveSelf());
    }

    public virtual void Hide(bool hide) {
        if (hide) {
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

    protected T AddChildPanel<T>(GameObject canvas) where T: EditorPanel, new() {
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
                builder.SelectObject(inst.gameObject, true, null);
                return 2;
            } else if (obj is CityProperties p) {
                var inst = p.GetParametricObject(param, parentParams);
                builder.SetTransformPanelParent(returnPanel != null ? returnPanel : this);
                if (returnPanel == null) Hide(true);
                builder.SelectObject(inst.gameObject, true, null);
                return 1;
            } else if (obj is ObjectState) { //note: the key will be the path...
                parentParams.Add(parentParamPrefix);
                var res = parentPanel.EditObjectInstanceParameter(param, parentParams, returnPanel == null ? this : returnPanel);
                if (res == 1) {
                    Hide(true);
                } else if (res == 2) {
                    Terminate();
                }
                return res;
            } else {
                Debug.LogWarning("Unsupported object instance container: " + obj.GetType().ToString());
            }
        }
        return 0;
    }

    CityElements.Types.Parameter GetArrayLabelProperty(CityElements.Types.Parameter param) {
        var labelKey = GetArraylabel(param.arrayProperties);
        var elemProperties = GetArrayProperties(param.arrayProperties);
        CityElements.Types.Parameter labelProperty = null;
        foreach(var p in elemProperties) {
            if (p.name == labelKey) {
                return p;
            }
        }
        return labelProperty;
    }

    string GetArrayElementLabel(ObjectState elem, CityElements.Types.Parameter param, int curCount) {
        switch (param?.type) {
            case "bool":
                return elem.Bool(param.fullName()) ? "True" : "False";
            case "float":
                return elem.Float(param.fullName()).ToString();
            case "int":
                return elem.Int(param.fullName()).ToString();
            case "enum":
                return param.enumLabels[elem.Int(param.fullName())];
            case "string":
            case "texture":
            case "mesh":
                return elem.Str(param.fullName());
            case "objectInstance":
                var res = elem.State(param.fullName())?.Str("meshPath");
                if (res == null) res = elem.Str(param.fullName()); //the moment it's initialized it's still a string
                return res;
            default:
                return "Element " + (curCount + 1);
        }
    }

    List<ObjectState> GetArrayList(CityElements.Types.Parameter param, IObjectWithState obj) {
        var arr = obj.GetState().Array<ObjectState>(param.fullName());
        if (arr == null) {
            arr = new ObjectState[0] { };
        }
        return new List<ObjectState>(arr);
    }

    void SetArrayList(CityElements.Types.Parameter param, IObjectWithState obj, List<ObjectState> list) {
        var newArr = list.ToArray();
        obj.GetState().SetArray(param.fullName(), newArr);
        builder.NotifyChange();
    }

    List<string> GetArray(CityElements.Types.Parameter param) {
        var obj = getObject?.Invoke();
        if (obj != null) {
            var res = new List<string>();
            var arr = GetArrayList(param, obj);
            var labelProperty = GetArrayLabelProperty(param);
            foreach (var elem in arr) {
                res.Add(GetArrayElementLabel(elem, labelProperty, res.Count));
            }
            return res;
        } else {
            return new List<string>();
        }
    }

    void AddArrayElement(CityElements.Types.Parameter param) {
        var obj = getObject?.Invoke();
        if (obj != null) {
            var arr = GetArrayList(param, obj);
            var properties = GetArrayProperties(param.arrayProperties);
            var newObj = ObjectState.CreateFromDefaultProperties(properties);
            arr.Add(newObj);
            SetArrayList(param, obj, arr);
        }
    }

    void CloneArrayElement(CityElements.Types.Parameter param, int i) {
        var obj = getObject?.Invoke();
        if (obj != null) {
            var arr = GetArrayList(param, obj);
            if (arr.Count > 0) {
                arr.Add((ObjectState)arr[i].Clone());
                SetArrayList(param, obj, arr);
            } else {
                Debug.LogWarning("Unable to clone, array is empty");
            }
        }
    }

    void ShiftArrayElement(CityElements.Types.Parameter param, int i, bool positive) {
        var obj = getObject?.Invoke();
        if (obj != null) {
            var arr = GetArrayList(param, obj);
            if (arr.Count > 0) {
                if ((positive && i < arr.Count - 1) || (!positive && i > 0)) {
                    var shift = positive ? 1 : -1;
                    var elem1 = arr[i];
                    var elem2 = arr[i + shift];
                    arr[i + shift] = elem1;
                    arr[i] = elem2;
                    SetArrayList(param, obj, arr);
                } else {
                    Debug.LogWarning("Unable to shift");
                }
            } else {
                Debug.LogWarning("Unable to shift, array is empty");
            }
        }
    }

    void SelectArrayElement(CityElements.Types.Parameter param, int i) {
        var obj = getObject?.Invoke();
        if (obj != null) {
            var arr = obj.GetState().Array<ObjectState>(param.fullName());
            if (i < 0 || i >= arr.Length) return;
            ShowArrayElementPanel(param, arr[i], i);
        }
    }

    protected CityElements.Types.Parameter[] GetSubparams(CityElements.Types.Parameter param) {
        //TODO: put this duplicate code in some common place
        CityElements.Types.Parameter[] subparams;
        if (param.arrayProperties != null) {
            if (param.arrayProperties.customElementType != null) {
                var ct = CityElements.Types.Parsers.TypeParser.GetCustomTypes()[param.arrayProperties.customElementType];
                subparams = ct.parameters;
            } else {
                subparams = param.arrayProperties.elementProperties;
            }
        } else if (param.customElementType != null) {
            var ct = CityElements.Types.Parsers.TypeParser.GetCustomTypes()[param.customElementType];
            subparams = ct.parameters;
        } else {
            Debug.LogWarning("invalid subparameters for parameter " + param.fullName());
            subparams = null;
        }
        return subparams;
    }

    void SelectArrayElementByState(ObjectState state, string paramName, Stack<GameObject> stack = null) {
        var fullParamName = "state.properties." + paramName;
        if (!parameterMap.ContainsKey(fullParamName)) fullParamName = "properties." + paramName;
        if (parameterMap.ContainsKey(fullParamName)) { //TODO: not always like this?
            var param = parameterMap[fullParamName];

            //check subparameters and stop if there's only one objectInstance
            //(since in that case it would open the parent which would go straight back to the current object)
            var subparams = GetSubparams(param);
            if (subparams.Length == 0 || (subparams.Length == 1 && subparams[0].type == "objectInstance")) return;

            var obj = getObject?.Invoke();
            if (obj != null) {
                if (param.type == "array") {
                    var arr = obj.GetState().Array<ObjectState>(param.fullName());
                    for (int i = 0; i < arr.Length; i++) {
                        if (arr[i] == state) {
                            ShowArrayElementPanel(param, arr[i], i, stack);
                        }
                    }
                } else if (param.type == "customElement") {
                    var subObj = obj.GetState().State(param.fullName());
                    if (subObj == state) {
                        ShowArrayElementPanel(param, subObj, null, stack);
                    }
                } else {
                    Debug.LogWarning("selection of parameter not supported: " + param.type);
                }
            }
        } else {
            Debug.LogWarning("parameter not found: " + paramName);
        }
    }

    void ShowArrayElementPanel(CityElements.Types.Parameter param, ObjectState elemState, int? i, Stack<GameObject> stack = null) {
        InitArrayElementPanel();
        arrayElemPanel.InitializeWithData(lastCanvas, param, elemState);
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
        var obj = getObject?.Invoke();
        if (obj != null) {
            var arr = GetArrayList(param, obj);
            if (arr.Count > 0) {
                ArrayObject.PruneSelections(arr[i]);
                builder.NotifyVisibilityChange();
                arr.RemoveAt(i);
                SetArrayList(param, obj, arr);
            } else {
                Debug.LogWarning("Unable to delete, array is empty");
            }
        }
    }

    void SwitchArrayClickAddMode() {
        MonoBehaviour.print("test"); //todo
    }

    CityElements.Types.Parameter[] GetArrayProperties(CityElements.Types.ArrayProperties type) {
        if (type == null) return null;
        if (type.customElementType != null) {
            var ct = CityElements.Types.Parsers.TypeParser.GetCustomTypes()[type.customElementType];
            return ct.parameters;
        } else {
            return type.elementProperties;
        }
    }

    string GetArraylabel(CityElements.Types.ArrayProperties type) {
        if (type == null) return null;
        if (type.customElementType != null) {
            var ct = CityElements.Types.Parsers.TypeParser.GetCustomTypes()[type.customElementType];
            return ct.label;
        } else {
            return type.elementLabel;
        }
    }

    void SelectCustomElement(CityElements.Types.Parameter param, Stack<GameObject> stack = null) {
        var obj = getObject?.Invoke();
        if (obj != null) {
            var subObj = obj.GetState().State(param.fullName());
            if (subObj == null) {
                subObj = new ObjectState();
                obj.GetState().SetState(param.fullName(), subObj);
            }
            ShowArrayElementPanel(param, subObj, null, stack);
        }
    }
}

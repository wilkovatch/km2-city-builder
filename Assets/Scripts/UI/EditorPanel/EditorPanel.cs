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
                if (processCustomParts.Invoke(elem, p, pS, tS, type)) continue;
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
        curW += elem.width;
        if (curW > totW) {
            p.IncreaseRow();
            curW = elem.width;
        }
        foreach (var param in parameters) {
            if (param.fullName() == elem.name) {
                var pFullName = (prependstate ? (param.instanceSpecific ? "instanceState." : "state.") : "") + "properties." + param.fullName();
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
        foreach (var elem in complexElements.Values) {
            elem.SetActive(active);
        }
        if (needsReloading && active) Initialize(lastCanvas);
        if (active) ReadCurValues();
        for (int i = 0; i < pages.Count; i++) {
            pages[i].SetActive(i == 0 ? active : false);
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
    }

    void FlagParentChange(System.Func<IObjectWithState> getObject) {
        var obj = getObject.Invoke();
        if (obj != null) {
            obj.GetState().FlagAsChanged();
        }
    }
}

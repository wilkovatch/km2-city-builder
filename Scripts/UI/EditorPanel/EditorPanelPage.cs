using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using GameObject = Godot.Control;
using SM = StringManager;
using GO = GameEngine.GameObject;

public class EditorPanelPage {
    List<List<EditorPanelElement>> elements = new List<List<EditorPanelElement>>();
    public GameObject panel, parentPanel;
    EditorPanel container;
    GameObject tooltipObj;
    Dictionary<EditorPanelElement, FieldStatus> fieldElements = new Dictionary<EditorPanelElement, FieldStatus>();
    int curRow = 0;
    System.Action valueChangedAction;
    List<float> rowsHeights = new List<float>();

    public virtual void Initialize(EditorPanel container, GameObject canvas, System.Action valueChangedAction) {
        InitializeRows(50);
        this.container = container;
        this.valueChangedAction = valueChangedAction;
        // A scroll area ("Panel") anchored to the right edge, from just below the 20px menu bar down to the
        // bottom of the screen. Its content ("ScrollRect") is anchored top-right, has a flat (0.9, 0.9, 0.9) background
        // and is sized to fit the elements (width = widest row + 20, height = rows + 20). A 10px scrollbar overlays the
        // right edge and auto-hides when everything fits.
        parentPanel = new Control { Name = "EditorPanelPage", MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        UILayout.SetRect(parentPanel, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, -MenuBar.Height), new Vector2(0, -MenuBar.Height), new Vector2(1, 1));
        canvas.AddChild(parentPanel);

        scroll = new ScrollContainer {
            Name = "ScrollRect",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            // Pass: the empty area below short panels must not count as UI (UIDetection) so terrain clicks still work there.
            MouseFilter = Control.MouseFilterEnum.Pass
        };
        scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        UILayout.Stretch(scroll);
        parentPanel.AddChild(scroll);

        panel = new Panel { Name = "EditorFields", CustomMinimumSize = new Vector2(0, 50) };
        panel.AddThemeStyleboxOverride("panel", UILayout.Flat(new Color(0.9f, 0.9f, 0.9f)));
        scroll.AddChild(panel);

        scrollbar = new VScrollBar { Name = "PanelScrollbar", Visible = false };
        UILayout.StyleScrollBar(scrollbar);
        UILayout.SetRect(scrollbar, 1, 0, 1, 1, -5, 0, 10, 0);
        parentPanel.AddChild(scrollbar);

        var innerBar = scroll.GetVScrollBar();
        innerBar.Changed += UpdateScrollbar;
        innerBar.ValueChanged += value => { if (GodotObject.IsInstanceValid(scrollbar)) scrollbar.SetValueNoSignal(value); };
        scrollbar.ValueChanged += value => { if (GodotObject.IsInstanceValid(scroll)) scroll.ScrollVertical = (int)value; };
        scroll.Resized += UpdateScrollbar;

        // Texture/mesh selectors hide only the fields (the scroll area content), leaving this page active
        // under the selector. The outer scroll area has no graphic, so it never catches clicks; here the
        // ScrollContainer (and overlay scrollbar) would swallow every click on the selector panel below it.
        var fields = panel;
        fields.VisibilityChanged += () => {
            if (!GodotObject.IsInstanceValid(fields) || scroll == null || !GodotObject.IsInstanceValid(scroll)) return;
            scroll.MouseFilter = fields.Visible ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
            UpdateScrollbar();
        };
    }

    ScrollContainer scroll;
    VScrollBar scrollbar;

    void UpdateScrollbar() {
        if (scroll == null || scrollbar == null || !GodotObject.IsInstanceValid(scroll) || !GodotObject.IsInstanceValid(scrollbar)) return;
        var innerBar = scroll.GetVScrollBar();
        scrollbar.MaxValue = innerBar.MaxValue;
        scrollbar.Page = innerBar.Page;
        scrollbar.Visible = panel != null && GodotObject.IsInstanceValid(panel) && panel.Visible && innerBar.MaxValue - innerBar.Page > 0.5;
        scrollbar.SetValueNoSignal(innerBar.Value);
    }

    public void ReadCurValues() {
        foreach (KeyValuePair<EditorPanelElement, FieldStatus> pair in fieldElements) {
            try {
                if (pair.Value.fieldName != "") {
                    var oldActionEnabled = pair.Key.actionEnabled;
                    pair.Key.actionEnabled = false;
                    pair.Key.SetValue(pair.Value.GetValue());
                    pair.Key.actionEnabled = oldActionEnabled;
                }
            } catch (System.Exception e) {
                GameEngine.Print("Error when reading field " + pair.Value.fieldName + ": " + e);
            }
        }
    }

    public void UpdateFieldName(EditorPanelElement element, string name) {
        fieldElements[element].fieldName = name;
    }

    public virtual void SetActive(bool active) {
        if (active) ReadCurValues();
        GO.SetEnabled(parentPanel, active);
        if (scroll != null && GodotObject.IsInstanceValid(scroll)) scroll.ScrollVertical = 0;
    }

    public bool ActiveSelf() {
        return GO.GetEnabled(parentPanel);
    }

    public void Toggle() {
        GO.SetEnabled(parentPanel, !GO.GetEnabled(parentPanel));
    }

    private void InitializeRows(int rows) {
        for (int i = 0; i < rows; i++) {
            elements.Add(new List<EditorPanelElement>());
            rowsHeights.Add(50);
        }
    }

    private float GetMaxRow() {
        int rows = elements.Count;
        float max = 0;
        for (int i = 0; i < rows; i++) {
            if (elements[i].Count > 0) max = Mathf.Max(max, elements[i][elements[i].Count - 1].GetNextPosition());
        }
        return max;
    }

    public void AlterTaggedElements(string tag, System.Action<EditorPanelElement> action) {
        foreach (var row in elements) {
            foreach (var elem in row) {
                if (elem.GetTag() == tag) action.Invoke(elem);
            }
        }
    }

    //the next elements go in a new row, unless the current one is still empty
    public void EnsureNewRow() {
        if (curRow < elements.Count && elements[curRow].Count > 0) IncreaseRow();
    }

    public void IncreaseRow(float heightFactor = 1.0f) {
        rowsHeights[curRow] = 50 * heightFactor;
        curRow++;
        UpdatePanelHeight(curRow - 1);
    }

    void UpdatePanelHeight(int row) {
        var newPos = GetMaxRow();
        var size = new Vector2(newPos + 20, 20 + GetRowHeight(row + 1));
        panel.CustomMinimumSize = size;
        // The content is anchored to the right edge, so the page is exactly as wide as its content.
        parentPanel.OffsetLeft = -size.X;
    }

    //the height of the rows before the given one: a row whose elements are all hidden takes no space (a row without
    //elements keeps its height, as a spacer)
    float GetRowHeight(int row) {
        float res = 0;
        for (int i = 0; i < row; i++) {
            if (elements[i].Count == 0 || elements[i].Exists(e => e.IsActive())) res += rowsHeights[i];
        }
        return res;
    }

    //moves the rows after an element was shown or hidden, and fits the panel to them
    public void Relayout() {
        if (panel == null || !GodotObject.IsInstanceValid(panel)) return; //destroyed
        for (int row = 0; row < elements.Count; row++) {
            if (elements[row].Count == 0) continue;
            var y = GetRowHeight(row);
            foreach (var element in elements[row]) element.SetY(y);
        }
        UpdatePanelHeight(elements[curRow].Count > 0 ? curRow : curRow - 1);
    }

    private Vector2 GetPosition(int row) {
        var x = elements[row].Count == 0 ? 0 : elements[row][elements[row].Count - 1].GetNextPosition();
        var y = GetRowHeight(row);
        return new Vector2(x, y);
    }

    private EditorPanelElement AddElement(EditorPanelElement element, int row = 0) {
        elements[row].Add(element);
        element.parentPanel = container;
        element.page = this;
        UpdatePanelHeight(row);
        //tooltipObj.transform.SetAsLastSibling();
        return element;
    }

    public void Destroy() {
        // Panel rebuilds can be triggered from a Control signal (for example, the
        // road type dropdown). Freeing its emitting control synchronously makes
        // Godot abort the signal dispatch, so remove the old page at frame end.
        if (GodotObject.IsInstanceValid(parentPanel) && !parentPanel.IsQueuedForDeletion()) {
            parentPanel.QueueFree();
        }
        parentPanel = null;
        panel = null;
        scroll = null;
        scrollbar = null;
    }

    //general purpose elements
    public EditorPanelElements.Label AddLabel(string title, float widthFactor = 1.0f, string tooltip = null, string tag = null, float heightFactor = 1.0f) {
        var element = new EditorPanelElements.Label(title, panel, GetPosition(curRow), widthFactor, tooltip, tag, heightFactor);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.Image AddImage(string title, string image, float widthFactor = 1.0f, string tooltip = null, string tag = null, float heightFactor = 1.0f) {
        var element = new EditorPanelElements.Image(title, panel, GetPosition(curRow), image, widthFactor, tooltip, tag, heightFactor);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.Dropdown AddDropdown(string title, List<string> entries, System.Action<int> action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {
        var element = new EditorPanelElements.Dropdown(title, entries, value => Recorded(title, () => BaseExtraAction(value, action)), panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.Slider AddSlider(string title, float min, float max, System.Action<float> action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {
        EditorPanelElements.Slider element = null;
        element = new EditorPanelElements.Slider(title, min, max, value => Recorded(title, () => BaseExtraAction(value, action), element), panel, GetPosition(curRow), null, widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.SliderWithInputField AddSliderWithInputField(string title, float min, float max, System.Action<float> action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {
        EditorPanelElements.SliderWithInputField element = null;
        element = new EditorPanelElements.SliderWithInputField(title, min, max, value => Recorded(title, () => BaseExtraAction(value, action), element), panel, GetPosition(curRow), null, widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.InputField AddInputField(string title, string placeholder, string defaultValue,
        GameEngine.InputContentType contentType, System.Action<string> action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {

        var element = new EditorPanelElements.InputField(title, placeholder, defaultValue, contentType, value => Recorded(title, () => BaseExtraAction(value, action)), panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.TitleInputField AddTitleInputField(string placeholder, string defaultValue,
    GameEngine.InputContentType contentType, System.Action<string> action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {

        var element = new EditorPanelElements.TitleInputField(placeholder, defaultValue, contentType, value => Recorded(placeholder, () => BaseExtraAction(value, action)), panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.Button AddButton(string title, System.Action action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {
        var element = new EditorPanelElements.Button(title, delegate { Recorded(title, () => { BaseExtraAction(0, null); action.Invoke(); }); }, panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.Checkbox AddCheckbox(string title, bool defaultValue, System.Action<bool> action,float widthFactor = 1.0f, string tooltip = null, string tag = null) {
        var element = new EditorPanelElements.Checkbox(title, defaultValue, value => Recorded(title, () => BaseExtraAction(value, action)), panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.ScrollList AddScrollList(string title, List<string> items, System.Action<int> action,
        float widthFactor = 1.0f, string tooltip = null, string tag = null, string emptyLabel = null) {

        var element = new EditorPanelElements.ScrollList(title, items, value => Recorded(title, () => BaseExtraAction(value, action)), panel, GetPosition(curRow), widthFactor, tooltip, tag, emptyLabel);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.ScrollList AddScrollList(string title, List<string> items, List<string> images, System.Action<int> action,
        float widthFactor = 1.0f, string tooltip = null, string tag = null, string emptyLabel = null) {

        var element = new EditorPanelElements.ScrollList(title, items, images, value => Recorded(title, () => BaseExtraAction(value, action)), panel, GetPosition(curRow), widthFactor, tooltip, tag, emptyLabel);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.TextureField AddTextureField(CityBuilderMenuBar builder, string title, string placeholder, string defaultValue, System.Action<string> valueSetter,
        System.Func<string> valueGetter, System.Action<string> extraAction = null, float widthFactor = 1.0f, string tooltip = null, string tag = null) {

        EditorPanelElements.TextureField element = null;
        element = new EditorPanelElements.TextureField(title, placeholder, defaultValue, value => Recorded(title, () => valueSetter?.Invoke(value)), //TODO: check why the setter does not always work if put only here
            delegate { builder.OpenTextureSelector(panel, valueGetter, value => Recorded(title, () => { valueSetter?.Invoke(value); BaseExtraAction(value, extraAction); element.SetValue(value); }), ReadCurValues); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.MeshField AddMeshField(CityBuilderMenuBar builder, string title, string placeholder, string defaultValue, System.Action<string> valueSetter, bool disableFromObject,
        System.Action<string> extraAction = null, float widthFactor = 1.0f, string tooltip = null, string tag = null) {

        EditorPanelElements.MeshField element = null;
        var pContainer = disableFromObject ? null : container;
        var pPanel = disableFromObject ? panel : null;
        element = new EditorPanelElements.MeshField(title, placeholder, defaultValue, value => Recorded(title, () => valueSetter?.Invoke(value)),
            delegate { builder.OpenMeshSelector(pContainer, pPanel, value => Recorded(title, () => { valueSetter?.Invoke(value); BaseExtraAction(value, extraAction); element.SetValue(value); }), ReadCurValues, true); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.VectorInputField AddVectorInputField(string title, System.Action<Vector3> action, float widthFactor = 1.0f, string tooltip = null) {
        var element = new EditorPanelElements.VectorInputField(title, value => Recorded(title, () => BaseExtraAction(value, action)), panel, GetPosition(curRow), widthFactor, tooltip);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.VectorInputField AddRotationVectorInputField(string title, System.Action<Quaternion> action, float widthFactor = 1.0f, string tooltip = null) {
        var element = new EditorPanelElements.VectorInputField(title, value => Recorded(title, () => BaseExtraAction(value, action)), panel, GetPosition(curRow), widthFactor, tooltip);
        AddElement(element, curRow);
        return element;
    }

    //field setters
    public EditorPanelElements.Slider AddFieldSlider(string title, float min, float max, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<float> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        EditorPanelElements.Slider element = null;
        element = new EditorPanelElements.Slider(title, min, max, value => Recorded(title, () => { status.SetValue(value); BaseExtraAction(value, extraAction); }, element), panel, GetPosition(curRow), 0, widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.InputField AddFieldInputField(string title, string placeholder, GameEngine.InputContentType contentType, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<string> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.InputField(title, placeholder, "", contentType, value => Recorded(title, () => { status.SetInputFieldValue(value, contentType); BaseExtraAction(value, extraAction); }),
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.Checkbox AddFieldCheckbox(string title, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<bool> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.Checkbox(title, false, value => Recorded(title, () => { status.SetValue(value); BaseExtraAction(value, extraAction); }),
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.Dropdown AddFieldDropdown(string title, List<string> entries, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<int> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.Dropdown(title, entries, value => Recorded(title, () => { status.SetValue(value); BaseExtraAction(value, extraAction); }),
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    //a dropdown of names whose field holds the name chosen (e.g. a preset)
    public EditorPanelElements.NamedDropdown AddFieldNamedDropdown(string title, List<string> names, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<string> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.NamedDropdown(title, names, value => Recorded(title, () => { status.SetValue(value); BaseExtraAction(value, extraAction); }),
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.TextureField AddFieldTextureField(CityBuilderMenuBar builder, string title, string placeholder, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<string> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.TextureField(title, placeholder, "", value => Recorded(title, () => { status.SetInputFieldValue(value, GameEngine.InputContentType.Standard); BaseExtraAction(value, extraAction); }),
            delegate { builder.OpenTextureSelector(panel, status.GetValue, value => Recorded(title, () => { status.SetValue(value); BaseExtraAction(value, extraAction); }), ReadCurValues); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    //a reference parameter (see Operations.States.AcceptsReference): owner is the element whose state has it
    public EditorPanelElements.ReferenceField AddFieldReferenceField(CityBuilderMenuBar builder, string title, System.Func<object> objGetter, string fieldName,
        CityElements.Types.Parameter param, System.Func<object> owner, float widthFactor = 1.0f, string tooltip = null, string tag = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, null);
        var picker = builder.referencePicker;
        EditorPanelElements.ReferenceField element = null;
        System.Func<object, string> getName = value => {
            if (!(value is ObjectState reference)) return StringManager.Get("REF_NONE");
            var target = Operations.States.ResolveReference(reference);
            return target != null ? Api.CityApi.GetName(target) : StringManager.Get("REF_MISSING");
        };
        System.Action pick = delegate {
            if (picker.IsPicking(container, element)) {
                picker.Stop();
                return;
            }
            picker.Start(container, element, target => Operations.States.AcceptsReference(param, owner(), target),
                target => Recorded(title, () => status.SetValue(Operations.States.MakeReference(target))), ReadCurValues);
            ReadCurValues();
        };
        System.Action clear = delegate {
            if (picker.IsPicking(container, element)) picker.Stop();
            Recorded(title, () => status.SetValue(null));
            ReadCurValues();
        };
        element = new EditorPanelElements.ReferenceField(title, getName, pick, clear, () => picker.IsPicking(container, element),
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.MeshField AddFieldMeshField(CityBuilderMenuBar builder, string title, string placeholder, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, bool disableFromObject, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<string> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var pContainer = disableFromObject ? null : container;
        var pPanel = disableFromObject ? panel : null;
        var element = new EditorPanelElements.MeshField(title, placeholder, "", value => Recorded(title, () => { status.SetInputFieldValue(value, GameEngine.InputContentType.Standard); BaseExtraAction(value, extraAction); }),
            delegate { builder.OpenMeshSelector(pContainer, pPanel, value => Recorded(title, () => { status.SetValue(value); BaseExtraAction(value, extraAction); }), ReadCurValues, true); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.VectorInputField AddFieldVectorInputField(string title, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<Vector3> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.VectorInputField(title, value => Recorded(title, () => { status.SetValue(value); BaseExtraAction(value, extraAction); }),
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.VectorInputField AddFieldRotationVectorInputField(string title, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<Quaternion> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.VectorInputField(title, value => Recorded(title, () => { status.SetValue(value); BaseExtraAction(value, extraAction); }),
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public virtual void BaseExtraAction<T>(T p, System.Action<T> a) {
        container.BaseExtraAction(p, a);
    }

    //every change made from the panel is an undo step, recording the element edited by the panel before it
    //(continuous controls like sliders pass a coalesce key, so that a drag is a single step,
    //the middle mouse drag of the numeric fields has a key of its own for each drag)
    void Recorded(string label, System.Action action, object coalesceKey = null) {
        coalesceKey = NumericFieldDrag.CurrentKey ?? coalesceKey;
        object target = null;
        try {
            target = container?.GetUndoTarget();
        } catch (System.Exception e) {
            GameEngine.LogWarning("Undo: could not get the element edited by the panel: " + e.Message);
        }
        var key = coalesceKey == null ? null : (object)(coalesceKey, History.ElementRegistry.GetId(target));
        History.UndoManager.Begin(label, key);
        try {
            History.UndoManager.Touch(target);
            action.Invoke();
        } finally {
            History.UndoManager.Commit();
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SM = StringManager;

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
        parentPanel = new GameObject("Panel");
        parentPanel.AddComponent<CanvasRenderer>();
        var scrollRect = parentPanel.AddComponent<ScrollRect>();
        panel = new GameObject("ScrollRect");
        panel.AddComponent<CanvasRenderer>();
        panel.transform.parent = parentPanel.transform;
        tooltipObj = (GameObject)Object.Instantiate(Resources.Load("UIPrefabs/Tooltip"), new Vector3(0, 0, 0), Quaternion.identity);
        tooltipObj.transform.SetParent(panel.transform);
        tooltipObj.name = "Tooltip";
        tooltipObj.SetActive(false);
        Image i = panel.AddComponent<Image>();
        i.color = new Color(0.9f, 0.9f, 0.9f);
        parentPanel.transform.SetParent(canvas.transform, true);
        RectTransform rt = parentPanel.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(0, -20);
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.offsetMin = new Vector2(rt.offsetMin.x, 0);

        RectTransform rt2 = panel.GetComponent<RectTransform>();
        rt2.anchoredPosition = new Vector2(0, -20);
        rt2.anchorMin = new Vector2(1, 1);
        rt2.anchorMax = new Vector2(1, 1);
        rt2.pivot = new Vector2(1, 1);
        rt2.sizeDelta = new Vector2(0, 50);

        scrollRect.content = rt2;
        scrollRect.scrollSensitivity = 10;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.horizontal = false;

        var scrollbarObj = (GameObject)Object.Instantiate(Resources.Load("UIPrefabs/ObjectEditor/PanelScrollbar"), new Vector3(0, 0, 0), Quaternion.identity, parentPanel.transform);
        scrollRect.verticalScrollbar = scrollbarObj.GetComponent<Scrollbar>();
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        RectTransform rt3 = scrollbarObj.GetComponent<RectTransform>();
        rt3.transform.localPosition = new Vector3(-5, 0, 0);
        rt3.offsetMin = new Vector2(rt3.offsetMin.x, 0);
        rt3.offsetMax = new Vector2(rt3.offsetMax.x, 0);
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
                MonoBehaviour.print("Error when reading field " + pair.Value.fieldName + ": " + e);
            }
        }
    }

    public void UpdateFieldName(EditorPanelElement element, string name) {
        fieldElements[element].fieldName = name;
    }

    public virtual void SetActive(bool active) {
        if (active) ReadCurValues();
        parentPanel.SetActive(active);
        panel.transform.localPosition = new Vector3(0, 0, 0);
    }

    public bool ActiveSelf() {
        return parentPanel.activeSelf;
    }

    public void Toggle() {
        parentPanel.SetActive(!parentPanel.activeSelf);
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

    public void IncreaseRow(float heightFactor = 1.0f) {
        rowsHeights[curRow] = 50 * heightFactor;
        curRow++;
        UpdatePanelHeight(curRow - 1);
    }

    void UpdatePanelHeight(int row) {
        var newPos = GetMaxRow();
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(newPos + 20, 20 + GetRowHeight(row + 1));
    }

    float GetRowHeight(int row) {
        float res = 0;
        for (int i = 0; i < row; i++) {
            res += rowsHeights[i];
        }
        return res;
    }

    private Vector2 GetPosition(int row) {
        var x = elements[row].Count == 0 ? 0 : elements[row][elements[row].Count - 1].GetNextPosition();
        var y = GetRowHeight(row);
        return new Vector2(x, y);
    }

    private EditorPanelElement AddElement(EditorPanelElement element, int row = 0) {
        elements[row].Add(element);
        UpdatePanelHeight(row);
        tooltipObj.transform.SetAsLastSibling();
        return element;
    }

    public void Destroy() {
        Object.Destroy(parentPanel);
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
        var element = new EditorPanelElements.Dropdown(title, entries, value => { BaseExtraAction(value, action); }, panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.Slider AddSlider(string title, float min, float max, System.Action<float> action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {
        var element = new EditorPanelElements.Slider(title, min, max, value => { BaseExtraAction(value, action); }, panel, GetPosition(curRow), null, widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.SliderWithInputField AddSliderWithInputField(string title, float min, float max, System.Action<float> action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {
        var element = new EditorPanelElements.SliderWithInputField(title, min, max, value => { BaseExtraAction(value, action); }, panel, GetPosition(curRow), null, widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.InputField AddInputField(string title, string placeholder, string defaultValue,
        InputField.ContentType contentType, System.Action<string> action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {

        var element = new EditorPanelElements.InputField(title, placeholder, defaultValue, contentType, value => { BaseExtraAction(value, action); }, panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.TitleInputField AddTitleInputField(string placeholder, string defaultValue,
    InputField.ContentType contentType, System.Action<string> action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {

        var element = new EditorPanelElements.TitleInputField(placeholder, defaultValue, contentType, value => { BaseExtraAction(value, action); }, panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.Button AddButton(string title, System.Action action, float widthFactor = 1.0f, string tooltip = null, string tag = null) {
        var element = new EditorPanelElements.Button(title, delegate { BaseExtraAction(0, null); action.Invoke(); }, panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.Checkbox AddCheckbox(string title, bool defaultValue, System.Action<bool> action,float widthFactor = 1.0f, string tooltip = null, string tag = null) {
        var element = new EditorPanelElements.Checkbox(title, defaultValue, value => { BaseExtraAction(value, action); }, panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.ScrollList AddScrollList(string title, List<string> items, System.Action<int> action,
        float widthFactor = 1.0f, string tooltip = null, string tag = null, string emptyLabel = null) {

        var element = new EditorPanelElements.ScrollList(title, items, value => { BaseExtraAction(value, action); }, panel, GetPosition(curRow), widthFactor, tooltip, tag, emptyLabel);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.ScrollList AddScrollList(string title, List<string> items, List<string> images, System.Action<int> action,
        float widthFactor = 1.0f, string tooltip = null, string tag = null, string emptyLabel = null) {

        var element = new EditorPanelElements.ScrollList(title, items, images, value => { BaseExtraAction(value, action); }, panel, GetPosition(curRow), widthFactor, tooltip, tag, emptyLabel);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.TextureField AddTextureField(CityBuilderMenuBar builder, string title, string placeholder, string defaultValue, System.Action<string> valueSetter,
        System.Func<string> valueGetter, System.Action<string> extraAction = null, float widthFactor = 1.0f, string tooltip = null, string tag = null) {

        EditorPanelElements.TextureField element = null;
        element = new EditorPanelElements.TextureField(title, placeholder, defaultValue, valueSetter, //TODO: check why the setter does not always work if put only here
            delegate { builder.OpenTextureSelector(panel, valueGetter, value => { valueSetter?.Invoke(value); BaseExtraAction(value, extraAction); element.SetValue(value); }, ReadCurValues); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.VectorInputField AddVectorInputField(string title, System.Action<Vector3> action, float widthFactor = 1.0f, string tooltip = null) {
        var element = new EditorPanelElements.VectorInputField(title, value => { BaseExtraAction(value, action); }, panel, GetPosition(curRow), widthFactor, tooltip);
        AddElement(element, curRow);
        return element;
    }

    public EditorPanelElements.VectorInputField AddRotationVectorInputField(string title, System.Action<Quaternion> action, float widthFactor = 1.0f, string tooltip = null) {
        var element = new EditorPanelElements.VectorInputField(title, value => { BaseExtraAction(value, action); }, panel, GetPosition(curRow), widthFactor, tooltip);
        AddElement(element, curRow);
        return element;
    }

    //field setters
    public EditorPanelElements.Slider AddFieldSlider(string title, float min, float max, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<float> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.Slider(title, min, max, value => { status.SetValue(value); BaseExtraAction(value, extraAction); }, panel, GetPosition(curRow), 0, widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.InputField AddFieldInputField(string title, string placeholder, InputField.ContentType contentType, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<string> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.InputField(title, placeholder, "", contentType, value => { status.SetInputFieldValue(value, contentType); BaseExtraAction(value, extraAction); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.Checkbox AddFieldCheckbox(string title, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<bool> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.Checkbox(title, false, value => { status.SetValue(value); BaseExtraAction(value, extraAction); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.Dropdown AddFieldDropdown(string title, List<string> entries, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<int> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.Dropdown(title, entries, value => { status.SetValue(value); BaseExtraAction(value, extraAction); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.TextureField AddFieldTextureField(CityBuilderMenuBar builder, string title, string placeholder, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<string> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.TextureField(title, placeholder, "", value => { status.SetInputFieldValue(value, InputField.ContentType.Standard); BaseExtraAction(value, extraAction); },
            delegate { builder.OpenTextureSelector(panel, status.GetValue, value => { status.SetValue(value); BaseExtraAction(value, extraAction); }, ReadCurValues); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.VectorInputField AddFieldVectorInputField(string title, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<Vector3> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.VectorInputField(title, value => { status.SetValue(value); BaseExtraAction(value, extraAction); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public EditorPanelElements.VectorInputField AddFieldRotationVectorInputField(string title, System.Func<object> objGetter, string fieldName,
        Dictionary<string, System.Func<string>> indexGetters, float widthFactor = 1.0f, string tooltip = null, string tag = null, System.Action<Quaternion> extraAction = null) {

        var status = new FieldStatus(objGetter, fieldName, valueChangedAction, indexGetters);
        var element = new EditorPanelElements.VectorInputField(title, value => { status.SetValue(value); BaseExtraAction(value, extraAction); },
            panel, GetPosition(curRow), widthFactor, tooltip, tag);
        var fieldElement = AddElement(element, curRow);
        fieldElements.Add(fieldElement, status);
        return element;
    }

    public virtual void BaseExtraAction<T>(T p, System.Action<T> a) {
        container.BaseExtraAction(p, a);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class MenuBarElement {
    GameObject obj;
    float width;
    float pos;
    List<System.Action> actions;
    Dropdown dropdown;

    public MenuBarElement(string text, List<string> options, List<System.Action> actions, GameObject parent, float pos, Color color) {
        this.actions = actions;
        this.pos = pos;
        obj = (GameObject)Object.Instantiate(Resources.Load("UIPrefabs/MenuBar/Dropdown"), new Vector3(0, 0, 0), Quaternion.identity);
        obj.transform.SetParent(parent.transform, true);
        var titleText = obj.transform.Find("Label").GetComponent<Text>();
        titleText.text = text;
        titleText.color = color;
        dropdown = obj.GetComponent<Dropdown>();
        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        var rt = obj.GetComponent<RectTransform>();
        width = obj.GetComponentInChildren<Text>().preferredWidth + 20;
        width = Mathf.Ceil(width);
        if (width % 2 != 0) width -= 1;
        rt.sizeDelta = new Vector2(width, 20);
        rt.anchoredPosition = new Vector2(pos, 0);
        RecalculateWidth(options);
        Unset();
        dropdown.onValueChanged.AddListener(delegate {
            Select();
        });
    }

    public void SetInteractable(bool interactable) {
        dropdown.interactable = interactable;
    }

    public void SetActive(bool active) {
        dropdown.gameObject.SetActive(active);
    }

    public void SetText(string newText) {
        var titleText = obj.transform.Find("Label").GetComponent<Text>();
        titleText.text = newText;
    }

    public void RecalculateWidth(List<string> options) {
        //calculate the width of the widest text
        //to calculate the width for each one need to assign the text to the label first
        var template = obj.transform.Find("Template");
        var labelItem = template.Find("Viewport/Content/Item/Item Label");
        var labelText = labelItem.GetComponent<Text>();
        var widest = 0f;
        foreach (var option in options) {
            labelText.text = option;
            widest = Mathf.Max(labelText.preferredWidth, widest);
        }
        var templRt = obj.transform.Find("Template").GetComponent<RectTransform>();
        templRt.offsetMax = new Vector2(widest - width + 40, 0);
    }

    public void SetEntries(List<string> options, List<System.Action> actions) {
        RecalculateWidth(options);
        this.actions = actions;
        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        Unset();
    }

    public float GetNextPosition() {
        return width + pos;
    }

    void Unset() {
        dropdown.options.Add(new Dropdown.OptionData() { text = "" });
        dropdown.value = dropdown.GetComponent<Dropdown>().options.Count - 1;
        dropdown.options.RemoveAt(dropdown.GetComponent<Dropdown>().options.Count - 1);
    }

    void Select() {
        if (dropdown.value >= actions.Count) return;
        actions[dropdown.value].Invoke();
        Unset();
    }
}

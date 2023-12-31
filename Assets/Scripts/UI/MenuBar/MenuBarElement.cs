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

    public MenuBarElement(string text, List<string> options, List<System.Action> actions, GameObject parent, float pos) {
        this.actions = actions;
        this.pos = pos;
        obj = (GameObject)Object.Instantiate(Resources.Load("UIPrefabs/MenuBar/Dropdown"), new Vector3(0, 0, 0), Quaternion.identity);
        obj.transform.SetParent(parent.transform, true);
        obj.transform.Find("Label").GetComponent<Text>().text = text;
        dropdown = obj.GetComponent<Dropdown>();
        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        var rt = obj.GetComponent<RectTransform>();
        width = obj.GetComponentInChildren<Text>().preferredWidth + 20;
        width = Mathf.Ceil(width);
        if (width % 2 != 0) width -= 1;
        rt.sizeDelta = new Vector2(width, 20);
        rt.anchoredPosition = new Vector2(pos, 0);
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
        Unset();
        dropdown.onValueChanged.AddListener(delegate {
            Select();
        });
    }

    public void SetInteractable(bool interactable) {
        dropdown.interactable = interactable;
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

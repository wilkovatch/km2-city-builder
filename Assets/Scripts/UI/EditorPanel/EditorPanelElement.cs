using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public abstract class EditorPanelElement {
    protected GameObject obj;
    protected float width;
    protected Vector2 pos;
    protected string tag;
    public EditorPanel parentPanel;
    public bool actionEnabled = true;

    protected abstract string TemplateName();

    protected virtual string TitlePath() {
        return "Title";
    }

    public EditorPanelElement(string title, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null, float heightFactor = 1.0f) {
        this.pos = pos;
        this.tag = tag;
        obj = (GameObject)Object.Instantiate(Resources.Load("UIPrefabs/ObjectEditor/" + TemplateName()), new Vector3(0, 0, 0), Quaternion.identity);
        obj.transform.SetParent(parent.transform, true);
        SetTitle(title);
        var rt = obj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(rt.sizeDelta.x * widthFactor, rt.sizeDelta.y * heightFactor);
        width = rt.sizeDelta.x;
        rt.anchoredPosition = new Vector2(-pos.x - rt.sizeDelta.x / 2 - 10, -pos.y - rt.sizeDelta.y / 2 - 10);
        if (tooltip != null) {
            var comp = obj.AddComponent<Tooltip>();
            comp.tooltip = tooltip;
            comp.tooltipObj = parent.transform.Find("Tooltip").gameObject;
        }
    }

    public string GetTag() {
        return tag;
    }

    public virtual void SetInteractable(bool interactable) { }

    public virtual bool GetInteractable() { return true; }

    public virtual void SetValue(object value) { }

    public void SetTitle(string title) {
        if (TitlePath() != "") obj.transform.Find(TitlePath()).GetComponent<Text>().text = title;
    }

    public float GetNextPosition() {
        return width + pos.x;
    }

    public void SetActive(bool active) {
        obj.SetActive(active);
    }
}

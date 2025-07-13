using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MenuBar : MonoBehaviour {
    List<MenuBarElement> elements = new List<MenuBarElement>();
    GameObject panel;

    public void Initialize() {
        Canvas c = GetComponent<Canvas>();
        panel = new GameObject("Panel");
        panel.AddComponent<CanvasRenderer>();
        Image i = panel.AddComponent<Image>();
        i.color = Color.white;
        panel.transform.SetParent(transform, true);
        RectTransform rt = panel.GetComponent<RectTransform>();

        rt.anchoredPosition = new Vector2(0, 0);
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(0, 20);
    }

    public int GetElementCount() {
        return elements.Count;
    }

    public void EnableElement(int i, bool enabled) {
        elements[i].SetInteractable(enabled);
    }

    public void ShowElement(int i, bool enabled) {
        elements[i].SetActive(enabled);
    }

    public void SetText(int i, string newText) {
        elements[i].SetText(newText);
    }

    public void SetEntries(int i, List<string> entries, List<System.Action> actions) {
        elements[i].SetEntries(entries, actions);
    }

    public void AddElement(string title, List<string> entries, List<System.Action> actions, Color color) {
        var pos = elements.Count == 0 ? 0 : elements[elements.Count - 1].GetNextPosition();
        elements.Add(new MenuBarElement(title, entries, actions, panel, pos, color));
    }

    public void SetAsLastSibling() {
        panel.transform.SetAsLastSibling();
    }

    void Update() {
        var eventSystem = EventSystem.current;
        if (eventSystem != null) {
            var cur = EventSystem.current.currentSelectedGameObject;
            if (cur != null && cur.transform.IsChildOf(panel.transform)) {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }
}

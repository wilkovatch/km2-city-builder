using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Tooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    bool mouseOver = false;
    bool wasStill = false;
    float startTime = 0.0f;
    Vector3 lastPos = new Vector3(0, 0, 0);
    public string tooltip = "tooltip";
    bool tooltipActive = false;
    public GameObject tooltipObj;

    void Update() {
        if (mouseOver && !Input.GetMouseButton(0)) {
            var mouseStil = (lastPos - Input.mousePosition).magnitude < 1.0f;
            if (mouseStil && !wasStill) {
                wasStill = true;
                startTime = Time.time;
            } else if (!mouseStil || Input.anyKeyDown || Input.mouseScrollDelta != Vector2.zero) {
                wasStill = false;
                if (tooltipActive) HideTooltip();
            }
            if (mouseStil && wasStill && Time.time - startTime > 0.5f && !tooltipActive) {
                ShowTooltip();
            }
        } else {
            wasStill = false;
            if (tooltipActive) HideTooltip();
        }
        lastPos = Input.mousePosition;
    }

    void HideTooltip() {
        tooltipActive = false;
        tooltipObj.SetActive(false);
    }

    void ShowTooltip() {
        tooltipActive = true;
        var container = tooltipObj.transform.Find("Container");
        container.Find("Message").GetComponent<Text>().text = tooltip;
        tooltipObj.SetActive(true);
        Canvas.ForceUpdateCanvases();
        var rt = container.GetComponent<RectTransform>();
        var xAdd = Input.mousePosition.x + rt.rect.width > Screen.width ? -rt.rect.width : 15;
        var yAdd = Input.mousePosition.y - rt.rect.height < 0 ? rt.rect.height : 0;
        tooltipObj.transform.position = Input.mousePosition + new Vector3(xAdd, yAdd, 0);
    }

    private void OnDisable() {
        mouseOver = false;
    }

    public void OnPointerEnter(PointerEventData eventData) {
        mouseOver = true;
    }

    public void OnPointerExit(PointerEventData eventData) {
        mouseOver = false;
    }
}

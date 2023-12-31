using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIDetection {
    public static bool OnUI() {
        PointerEventData pointerData = new PointerEventData(EventSystem.current) {
            pointerId = -1,
        };
        pointerData.position = Input.mousePosition;
        List<RaycastResult> results = new List<RaycastResult>();
        if (EventSystem.current != null) {
            EventSystem.current.RaycastAll(pointerData, results);
            return results.Count != 0;
        } else {
            return false;
        }
    }
}

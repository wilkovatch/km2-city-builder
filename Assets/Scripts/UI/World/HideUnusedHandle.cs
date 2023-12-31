using System.Collections;
using System.Collections.Generic;
using UnityEngine;

class HideUnusedHandle: MonoBehaviour {
    public bool show = false;
    private void Update() {
        if (!show) {
            gameObject.SetActive(false);
        }
        if (show) {
            show = false;
        }
    }
}

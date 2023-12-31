using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Handle : MonoBehaviour
{
    Vector3 scale = Vector3.one;
    Camera cam = null;

    public void SetColor(Color color) {
        GetComponent<MeshRenderer>().material = MaterialManager.GetHandleMaterial(color);
    }

    public void SetScale(Vector3 scale) {
        this.scale = scale;
    }

    void Start() {
        SetColor(Color.green);
    }

    void UpdateSize() {
        if (cam == null) cam = Camera.main;
        var dist = 1.0f;
        if (cam != null) {
            dist = Vector3.Distance(transform.position, cam.transform.position);
            dist = Mathf.Clamp(dist / 100, 0.5f, 1000);
        }
        gameObject.transform.localScale = scale * dist;
    }

    private void OnWillRenderObject() {
        UpdateSize();
    }
}

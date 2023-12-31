using System.Collections;
using System.Collections.Generic;
using UnityEngine;

class ActionHandlerManager {
    public static ElementManager manager;
    static GameObject handle = null;
    static GameObject discHndle = null;

    static GameObject GetHandle() {
        if (handle == null) {
            handle = Object.Instantiate(Resources.Load<GameObject>("Handle"));
            handle.transform.parent = manager.transform;
            handle.transform.localScale = Vector3.one;
            handle.GetComponent<BoxCollider>().enabled = false;
            handle.AddComponent<HideUnusedHandle>();
        }
        return handle;
    }

    static GameObject GetDiscHandle() {
        if (discHndle == null) {
            discHndle = Object.Instantiate(Resources.Load<GameObject>("DiscHandle"));
            discHndle.transform.parent = manager.transform;
            discHndle.transform.localScale = Vector3.one;
            discHndle.GetComponent<MeshCollider>().enabled = false;
            discHndle.AddComponent<HideUnusedHandle>();
        }
        return discHndle;
    }

    static void SetColor(Color color) {
        handle.GetComponent<MeshRenderer>().material = MaterialManager.GetHandleMaterial(color);
    }

    public static void ShowHandle(Vector3 position, float size, Color color) {
        if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)) return;
        var handle = GetHandle();
        handle.SetActive(true);
        handle.GetComponent<HideUnusedHandle>().show = true;
        handle.transform.position = position;
        var handleComp = handle.GetComponent<Handle>();
        handleComp.SetColor(color);
        handleComp.SetScale(Vector3.one * size);
    }

    public static void ShowDiscHandle(Vector3 position, float size, Color color) {
        if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)) return;
        var handle = GetDiscHandle();
        handle.SetActive(true);
        handle.GetComponent<HideUnusedHandle>().show = true;
        handle.transform.position = position;
        handle.transform.localScale = new Vector3(size, 1, size);
        var mat = handle.GetComponent<MeshRenderer>().material;
        mat.color = color;
    }
}
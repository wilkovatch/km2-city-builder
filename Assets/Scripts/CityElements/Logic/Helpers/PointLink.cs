using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PointLink : MonoBehaviour {
    public GameObject next;
    bool deleted = false;
    Vector3 oldCur, oldNext;
    IGroundable groundable;

    public static PointLink Create(GameObject parent, GameObject next, float size, IGroundable groundable = null) {
        var terrainAnchor = parent.GetComponent<TerrainAnchor>();
        var terrainPoint = parent.GetComponent<TerrainPoint>();
        if (terrainAnchor != null) {
            if (parent.transform.childCount > 0) {
                var oldObj = parent.transform.GetChild(0).gameObject;
                Destroy(oldObj); //to replace it
            }
        } else if (terrainPoint != null) {
            for (int i = 0; i < parent.transform.childCount; i++) {
                var oldObj = parent.transform.GetChild(i).gameObject;
                var oldLink = oldObj.GetComponent<PointLink>();
                if (oldLink.groundable == groundable && oldLink.next != next) {
                    Destroy(oldObj); //to replace it
                } else if (oldLink.groundable == groundable && oldLink.next == next) {
                    return oldLink;
                }
            }
        }
        var newObj = new GameObject();
        newObj.transform.parent = parent.transform;
        newObj.transform.localPosition = Vector3.zero;
        newObj.layer = 12;
        newObj.SetActive(true);
        var res = newObj.AddComponent<PointLink>();
        res.next = next;
        var cc = newObj.AddComponent<CapsuleCollider>();
        cc.radius = size;
        cc.direction = 2;
        res.UpdateParams();
        res.groundable = groundable;
        return res;
    }

    public void UpdateParams() {
        if (next == null) {
            Delete();
            return;
        }
        if (deleted) return;
        var p0 = gameObject.transform.position;
        var p1 = next.transform.position;
        if (p0 == oldCur && p1 == oldNext) return;
        oldCur = p0;
        oldNext = p1;
        transform.position = p0;
        var d = Vector3.Distance(p0, p1);
        var cc = GetComponent<CapsuleCollider>();
        cc.height = d;
        cc.center = new Vector3(0, 0, d * 0.5f);
        gameObject.transform.LookAt(p1);
    }

    private void Update() {
        UpdateParams();
        transform.localScale = new Vector3(1.0f / transform.parent.lossyScale.x, 1.0f / transform.parent.lossyScale.y, 1.0f / transform.parent.lossyScale.z);
    }

    public void Delete() {
        Destroy(gameObject);
        deleted = true;
    }

    public bool IsDeleted() {
        return deleted;
    }
}

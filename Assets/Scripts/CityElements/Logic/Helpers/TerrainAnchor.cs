using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainAnchor : MonoBehaviour, IAnchorable {
    public TerrainAnchor prev, next;
    public Vector3 insideDirection;
    bool moveable = false;
    bool deleted = false;

    public static TerrainAnchor Create(GameObject parent, Vector3 pos, float size) {
        var newObj = Instantiate(Resources.Load<GameObject>("Handle"), parent.transform);
        newObj.transform.position = pos;
        newObj.GetComponent<Handle>().SetScale(Vector3.one * size);
        newObj.layer = 10;
        newObj.SetActive(false);
        var res = newObj.AddComponent<TerrainAnchor>();
        return res;
    }

    public void Initialize(TerrainAnchor prev, TerrainAnchor next, bool moveable) {
        this.prev = prev;
        this.next = next;
        this.moveable = moveable;
        if (next != null) PointLink.Create(gameObject, next.gameObject, 1.0f);
    }

    public bool IsMoveable() {
        return moveable;
    }

    public void Delete() {
        Destroy(gameObject);
        deleted = true;
    }

    public bool IsDeleted() {
        return deleted;
    }

    public void SetActive(bool active) {
        gameObject.SetActive(active);
    }

    bool IsConnected(List<TerrainAnchor> list, TerrainAnchor origCaller, TerrainAnchor other, bool goNext) {
        var caller = origCaller;
        var curThis = this;
        int i = 0;
        while (i < 65535) {
            i++;
            var nextNode = goNext ? curThis.next : curThis.prev;
            if (caller == nextNode) {
                goNext = !goNext;
                nextNode = goNext ? curThis.next : curThis.prev;
            }
            if (origCaller == curThis) {
                list.Add(curThis);
                return origCaller == other;
            } else if (curThis == other) {
                list.Add(curThis);
                return true;
            } else {
                if (nextNode == null) return false;
                list.Add(curThis);
                caller = curThis;
                curThis = nextNode;
            }
        }
        return false;
    }

    public List<TerrainAnchor> GetPointListTo(TerrainAnchor other = null, bool invertChoice = false) {
        var nextList = new List<TerrainAnchor>();
        var prevList = new List<TerrainAnchor>();
        var foundNext = next != null && next.IsConnected(nextList, this, other, true);
        var foundPrev = prev != null && prev.IsConnected(prevList, this, other, false);
        var res = new List<TerrainAnchor>();
        if (foundNext && foundPrev) {
            res = nextList.Count < prevList.Count ? nextList : prevList;
            if (invertChoice) res = res == nextList ? prevList : nextList;
        } else if (foundNext) {
            res = nextList;
        } else if (foundPrev) {
            res = prevList;
        }
        if (res.Count == 0) res = new List<TerrainAnchor>(){ other };
        return res;
    }

    public Vector3 GetPosition() {
        return transform.position;
    }

    public bool IsTheSameAs(TerrainAnchor other) {
        return Vector3.Distance(transform.position, other.transform.position) < 0.01f;
    }
}

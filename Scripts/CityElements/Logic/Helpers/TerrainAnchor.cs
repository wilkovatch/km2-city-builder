using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using PhysicsGameObject = Godot.StaticBody3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

public partial class TerrainAnchor : Scriptable, IAnchorable {
    public TerrainAnchor prev, next;
    public Vector3 insideDirection;
    bool moveable = false;
    bool deleted = false;

    public static TerrainAnchor Create(GameObject parent, Vector3 pos, float size) {
        var newObj = (PhysicsGameObject)GameEngine.Resources.InstantiateObject("Handle", GO.GetObject(parent));
        GO.SetPosition(newObj, pos);
        GS.Cast<Handle>(newObj).SetScale(GameEngine.Vector3.one * size);
        GO.SetLayer(newObj, 10);
        GO.SetEnabled(newObj, false);
        return GS.Create<TerrainAnchor>("TerrainAnchor", newObj);
    }

    public void Initialize(TerrainAnchor prev, TerrainAnchor next, bool moveable) {
        this.prev = prev;
        this.next = next;
        this.moveable = moveable;
        if (!GO.IsNull(next)) PointLink.Create(GetObject(), next.GetObject(), 1.0f);
    }

    public bool IsMoveable() {
        return moveable;
    }

    public void Delete() {
        GO.Destroy(GetObject());
        deleted = true;
    }

    public bool IsDeleted() {
        return deleted;
    }

    public void SetActive(bool active) {
        GO.SetEnabled(GetObject(), active);
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
                if (GO.IsNull(nextNode)) return false;
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
        var foundNext = !GO.IsNull(next) && next.IsConnected(nextList, this, other, true);
        var foundPrev = !GO.IsNull(prev) && prev.IsConnected(prevList, this, other, false);
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

    public Vector3 GetPoint() {
        return GO.GetPosition(GetObject());
    }

    public bool IsTheSameAs(TerrainAnchor other) {
        return GameEngine.Vector3.Distance(GetPoint(), other.GetPoint()) < 0.01f;
    }

    public GameObject GetObject() {
        return GO.GetParent(this);
    }
}

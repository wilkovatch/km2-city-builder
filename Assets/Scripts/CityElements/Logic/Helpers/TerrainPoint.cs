using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainPoint : MonoBehaviour {
    GameObject obj = null;
    public IAnchorable anchor = null;
    Vector3 oldPoint;
    bool isMoveable = false;
    bool big = false;
    public bool dividing = false;
    List<IGroundable> links = new List<IGroundable>();
    bool deleted = false;
    bool selected = true;
    int updatedCount;

    public static TerrainPoint Create(Vector3 point, IGroundable patch, GameObject parent, IAnchorable anchor = null) {
        var obj = Instantiate(Resources.Load<GameObject>("Handle"), parent.transform);
        var comp = obj.AddComponent<TerrainPoint>();
        comp.Setup(point, patch, obj, anchor);
        return comp;
    }

    void Setup(Vector3 point, IGroundable patch, GameObject obj) {
        this.obj = obj;
        obj.transform.position = point;
        obj.GetComponent<Handle>().SetScale(Vector3.one * 1.25f);
        obj.layer = 10;
        oldPoint = point;
        if (patch != null) links.Add(patch);
        SetupAnchorMode();
    }

    void Setup(Vector3 point, IGroundable patch, GameObject obj, IAnchorable anchor = null) {
        this.anchor = anchor;
        Setup(point, patch, obj);
    }

    public void AddLink(IGroundable patch) {
        if (deleted) return;
        links.Add(patch);
        updatedCount = links.Count;
    }

    public void RemoveLink(IGroundable patch) {
        if (deleted) return;
        links.Remove(patch);
        updatedCount = links.Count;
        if (updatedCount <= 0) Delete();
    }

    bool IsAnchorNull() {
        return anchor == null || anchor.Equals(null);
    }

    void SetupAnchorMode() {
        var curScale = 1.0f;
        if (!selected) {
            curScale *= 1.1f;
            obj.GetComponent<Handle>().SetColor(Color.green);
        } else {
            curScale *= 1.25f;
            if (!IsAnchorNull() && anchor.IsDeleted()) {
                anchor = null;
            }
            if (IsAnchorNull() || anchor.IsMoveable()) {
                var lightColor = (anchor is LineAnchor) ? ((Color.yellow + Color.red) * 0.5f) : Color.yellow;
                obj.GetComponent<Handle>().SetColor(((big ? Color.magenta : Color.red) + lightColor) * 0.5f);
                isMoveable = true;
            } else {
                obj.GetComponent<Handle>().SetColor(big ? Color.magenta : Color.red);
                isMoveable = false;
            }
        }
        if (dividing) curScale *= 2.0f;
        obj.GetComponent<Handle>().SetScale(Vector3.one * curScale);
    }

    public void Select(bool selected) {
        if (deleted) return;
        this.selected = selected;
        SetupAnchorMode();
    }

    public void UpdateOlds() {
        updatedCount--;
        if (updatedCount <= 0) {
            updatedCount = links.Count;
            oldPoint = obj.transform.position;
        }
        SetupAnchorMode();
    }

    public void SetBig(bool big) {
        this.big = big;
    }

    public Vector3 GetPoint() {
        return obj.transform.position;
    }

    public bool OnSegment() {
        return anchor is LineAnchor;
    }

    public bool IsDeleted() {
        return deleted;
    }

    public bool UpdatePosition() {
        if (!IsAnchorNull()) {
            if (anchor.IsDeleted()) {
                return true;
            } else if (!GeometryHelper.AreVectorsEqual(anchor.GetPosition(), oldPoint)) {
                var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                var moveable = ctrl || CityBuilderMenuBar.staticGizmo.mainTargetRoot == transform;
                if (anchor is LineAnchor lineAnchor && moveable) lineAnchor.UpdatePercent(obj.transform.position);
                obj.transform.position = anchor.GetPosition();
                return true;
            } else {
                return false;
            }
        } else {
            if (IsProjectedToGround()) obj.transform.position = GeometryHelper.ProjectPoint(obj.transform.position);
            return !GeometryHelper.AreVectorsEqual(obj.transform.position, oldPoint);
        }
    }

    public void SetMoveable(bool moveable) {
        if (!isMoveable) return;
        if (moveable) {
            obj.layer = 8;
        } else {
            obj.layer = 10;
        }
    }

    public bool IsProjectedToGround() {
        var project = false;
        foreach (var patch in links) {
            if (patch.IsProjectedToGround()) {
                project = true;
                break;
            }
        }
        return project;
    }

    public void SetActive(bool active) {
        obj.SetActive(active);
    }

    public void Delete() {
        deleted = true;
        if (obj != null) Destroy(obj);
    }

    public bool DeleteManual(IGroundable link) {
        if (deleted || link == null || !links.Contains(link)) return false;
        var res = link.RemovePoint(this);
        if (res) {
            links.Remove(link);
            if (links.Count == 0) Delete();
        }
        return res;
    }
}

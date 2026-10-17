using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using PhysicsScriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using PhysicsGameObject = Godot.StaticBody3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;

public partial class TerrainPoint : PhysicsScriptable {
    public IAnchorable anchor = null;
    Vector3 oldPoint;
    bool isMoveable = false;
    bool big = false;
    public bool dividing = false;
    List<IGroundable> links = new List<IGroundable>();
    bool deleted = false;
    bool selected = true;
    int updatedCount;

    public static TerrainPoint Create(Vector3 point, IGroundable patch, GameObject parent, IAnchorable anchor = null, int registryId = -1) {
        var obj = (PhysicsGameObject)GameEngine.Resources.InstantiateObject("Handle", GO.GetObject(parent));
        var comp = GS.Create<TerrainPoint>("TerrainPoint", obj);
        comp.Setup(point, patch, anchor);
        //the points of the elements excluded from undo (the preset dummies) are excluded too
        if (patch == null || History.ElementRegistry.IsRegistered(patch)) History.ElementRegistry.Register(comp, registryId);
        return comp;
    }

    void Setup(Vector3 point, IGroundable patch) {
        GO.SetPosition(GetObject(), point);
        GS.Cast<Handle>(GetObject()).SetScale(GameEngine.Vector3.one * 1.25f);
        GO.SetLayer(GetObject(), 10);
        oldPoint = point;
        if (patch != null) links.Add(patch);
        SetupAnchorMode();
    }

    void Setup(Vector3 point, IGroundable patch, IAnchorable anchor = null) {
        this.anchor = anchor;
        Setup(point, patch);
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

    //used by undo/redo, which deletes the points explicitly
    public void RemoveLinkSilently(IGroundable patch) {
        if (deleted) return;
        links.Remove(patch);
        updatedCount = links.Count;
    }

    bool IsAnchorNull() {
        return GO.IsNull(anchor);
    }

    void SetupAnchorMode() {
        var curScale = 1.0f;
        if (!selected) {
            curScale *= 1.1f;
            GS.Cast<Handle>(GetObject()).SetColor(GameEngine.Color.green);
        } else {
            curScale *= 1.25f;
            if (IsAnchorNull() || anchor.IsDeleted()) {
                anchor = null;
            }
            if (IsAnchorNull() || anchor.IsMoveable()) {
                var lightColor = (anchor is LineAnchor) ? ((GameEngine.Color.yellow + GameEngine.Color.red) * 0.5f) : GameEngine.Color.yellow;
                GS.Cast<Handle>(GetObject()).SetColor(((big ? GameEngine.Color.magenta : GameEngine.Color.red) + lightColor) * 0.5f);
                isMoveable = true;
            } else {
                GS.Cast<Handle>(GetObject()).SetColor(big ? GameEngine.Color.magenta : GameEngine.Color.red);
                isMoveable = false;
            }
        }
        if (dividing) curScale *= 2.0f;
        GS.Cast<Handle>(GetObject()).SetScale(GameEngine.Vector3.one * curScale);
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
            oldPoint = GetPoint();
        }
        SetupAnchorMode();
    }

    public void SetBig(bool big) {
        this.big = big;
    }

    public Vector3 GetPoint() {
        return GO.GetPosition(GetObject());
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
            } else if (!GeometryHelper.AreVectorsEqual(anchor.GetPoint(), oldPoint)) {
                var ctrl = Input.GetInputPressed(KeyCommand.Modifier2);
                var moveable = ctrl || GameEngine.Gizmo.GetMainTargetRoot() == GetObject();
                if (anchor is LineAnchor lineAnchor && moveable) lineAnchor.UpdatePercent(GetPoint());
                GO.SetPosition(GetObject(), anchor.GetPoint());
                return true;
            } else {
                return false;
            }
        } else {
            if (IsProjectedToGround()) GO.SetPosition(GetObject(), GeometryHelper.ProjectPoint(GetPoint()));
            return !GeometryHelper.AreVectorsEqual(GetPoint(), oldPoint);
        }
    }

    public void SetMoveable(bool moveable) {
        if (!isMoveable) return;
        if (moveable) {
            GO.SetLayer(GetObject(), 8);
        }
        else {
            GO.SetLayer(GetObject(), 10);
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
        GO.SetEnabled(GetObject(), active);
    }

    public void Delete() {
        History.UndoManager.Touch(this);
        GO.Destroy(GetObject());
        deleted = true;
    }

    public bool DeleteManual(IGroundable link) {
        if (deleted || link == null || !links.Contains(link)) return false;
        History.UndoManager.Touch(this);
        History.UndoManager.Touch(link);
        var res = link.RemovePoint(this);
        if (res) {
            links.Remove(link);
            if (links.Count == 0) Delete();
        }
        return res;
    }

    public PhysicsGameObject GetObject() {
        return (PhysicsGameObject)GO.GetParent(this);
    }
}

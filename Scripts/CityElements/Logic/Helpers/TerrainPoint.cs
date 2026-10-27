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
    Vector3 oldRotation; //the rotation of the handle at the last update (rotatable points of the low-level elements)
    bool isMoveable = false;
    bool detachable = false; //moved anyway when on a fixed anchor, which it then leaves (the points of the low-level elements)
    bool big = false;
    public bool dividing = false;
    List<IGroundable> links = new List<IGroundable>();
    bool deleted = false;
    bool selected = true;
    int updatedCount;
    bool updating = false; //in UpdatePosition (the points it depends on are updated first, see UpdateDependencies)

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
                var lightColor = (anchor is LineAnchor || anchor is ColumnAnchor) ? ((GameEngine.Color.yellow + GameEngine.Color.red) * 0.5f) : GameEngine.Color.yellow;
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
            oldRotation = GetRotation();
        }
        SetupAnchorMode();
    }

    public void SetBig(bool big) {
        this.big = big;
    }

    public Vector3 GetPoint() {
        return GO.GetPosition(GetObject());
    }

    //the last position of a deleted point (its node is freed)
    public Vector3 GetPointSafe() {
        return deleted ? oldPoint : GetPoint();
    }

    //the rotation of the handle (euler angles in degrees), only used by the rotatable points of the low-level elements
    public Vector3 GetRotation() {
        return deleted ? oldRotation : GO.GetRotation(GetObject());
    }

    public void SetRotation(Vector3 rotation) {
        if (!deleted) GO.SetRotation(GetObject(), rotation);
    }

    public bool RotationChanged() {
        return !deleted && !GeometryHelper.AreVectorsEqual(GetRotation(), oldRotation);
    }

    public bool IsLinkedTo(IGroundable element) {
        return links.Contains(element);
    }

    public List<IGroundable> GetLinks() {
        return new List<IGroundable>(links);
    }

    public bool OnSegment() {
        return anchor is LineAnchor;
    }

    public bool IsDeleted() {
        return deleted;
    }

    //the points the anchor follows are updated first (e.g. a point on the vertical line of a point on a segment follows it
    //in the same frame)
    void UpdateDependencies() {
        if (anchor is ColumnAnchor column) {
            if (!column.IsDeleted()) column.master.UpdatePosition();
        } else if (anchor is LineAnchor line) {
            line.StartPoint?.UpdatePosition();
            line.EndPoint?.UpdatePosition();
        }
    }

    public bool UpdatePosition() {
        if (updating || deleted) return false;
        updating = true;
        try {
            UpdateDependencies();
            return UpdatePositionOnAnchor();
        } finally {
            updating = false;
        }
    }

    bool UpdatePositionOnAnchor() {
        if (anchor is ColumnAnchor column && !column.IsDeleted()) {
            //the height is the one of the point (also when changed by the constraints of an element), only x and z follow
            //the master; moved horizontally with the gizmo, the master follows (see ColumnAnchor)
            column.height = GetPoint().Y;
            if (GameEngine.Gizmo.GetMainTargetRoot() == GetObject() && !GeometryHelper.AreVectorsEqual(GetPoint(), column.GetPoint())) {
                History.UndoManager.Touch(this);
                column.MoveTo(GetPoint());
            }
            if (GeometryHelper.AreVectorsEqual(GetPoint(), column.GetPoint())) return !GeometryHelper.AreVectorsEqual(GetPoint(), oldPoint);
            GO.SetPosition(GetObject(), column.GetPoint());
            return true;
        }
        if (!IsAnchorNull()) {
            var ctrl = Input.GetInputPressed(KeyCommand.Modifier2);
            var moveable = ctrl || GameEngine.Gizmo.GetMainTargetRoot() == GetObject();
            if (anchor.IsDeleted()) {
                return true;
            } else if (detachable && moveable && !anchor.IsMoveable() && !GeometryHelper.AreVectorsEqual(GetPoint(), oldPoint)
                && GeometryHelper.AreVectorsEqual(anchor.GetPoint(), oldPoint)) {
                //moved with the gizmo: the point leaves the anchor
                History.UndoManager.Touch(this);
                anchor = null;
                SetupAnchorMode();
                return true;
            } else if (!GeometryHelper.AreVectorsEqual(anchor.GetPoint(), oldPoint)) {
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

    //detachable: a point on a fixed anchor can be moved too, leaving the anchor
    public void SetMoveable(bool moveable, bool detachable = false) {
        this.detachable = moveable && detachable;
        if (!isMoveable && !this.detachable) {
            if (!moveable) GO.SetLayer(GetObject(), 10); //it may have been moveable as a detachable point
            return;
        }
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
        if (deleted) return;
        History.UndoManager.Touch(this);
        oldPoint = GetPoint();
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

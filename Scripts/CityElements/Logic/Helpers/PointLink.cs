using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using PhysicsScriptable = Godot.StaticBody3D;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

public partial class PointLink : PhysicsScriptable, HandleUpdater.IUpdatable {
    public GameObject next;
    bool deleted = false;
    Vector3 oldCur, oldNext;
    IGroundable groundable;

    public static PointLink Create(GameObject parent, GameObject next, float size, IGroundable groundable = null) {
        //cleanup
        var oldLinks = GS.FindMultipleRecursive<PointLink>(parent);
        foreach (var oldLink in oldLinks) {
            if (oldLink.groundable == groundable && oldLink.next != next) {
                oldLink.Delete(); //to replace it
            }
        }
        foreach (var oldLink in oldLinks) {
            if (!oldLink.deleted && oldLink.groundable == groundable && oldLink.next == next) {
                return oldLink;
            }
        }

        //creation
        var newObj = GS.Create<PointLink>("PointLink", parent);
        GO.SetLocalPosition(newObj, GameEngine.Vector3.zero);
        GO.SetLayer(newObj, 12);
        GO.SetEnabled(newObj, true);
        var res = GS.Cast<PointLink>(newObj);
        res.next = next;
        GameEngine.Mesh.CreateCapsuleCollider(newObj, size);
        res.UpdateParams();
        res.groundable = groundable;
        return res;
    }

    public void UpdateParams() {
        if (deleted) return;
        if (GO.IsNull(next)) {
            Delete();
            return;
        }
        var p0 = GO.GetPosition(this);
        var p1 = GO.GetPosition(next);
        if (p0 == oldCur && p1 == oldNext) return;
        oldCur = p0;
        oldNext = p1;
        GO.SetPosition(this, p0);
        var d = GameEngine.Vector3.Distance(p0, p1);
        GameEngine.Mesh.AdjustCapsuleCollider(GO.GetObject(this), new Vector3(0, 0, d * 0.5f), d);
        GO.LookAt(GO.GetObject(this), p1);
    }

    public int UpdaterIndex { get; set; } = -1;
    bool transformChanged = true;
    Handle seenNext = null;
    ulong seenNextVersion = 0;

    //only the enabled links are updated
    public override void _EnterTree() {
        SetNotifyTransform(true);
        transformChanged = true;
        if (CanProcess()) HandleUpdater.Register(this);
    }

    public override void _ExitTree() {
        HandleUpdater.Unregister(this);
    }

    public override void _Notification(int what) {
        if (what == NotificationTransformChanged) {
            transformChanged = true;
        } else if (what == NotificationEnabled) {
            transformChanged = true; //the points could have moved while disabled
            if (IsInsideTree()) HandleUpdater.Register(this);
        } else if (what == NotificationDisabled) {
            HandleUpdater.Unregister(this);
        }
    }

    //called every frame by the HandleUpdater while enabled
    public void UpdateFrame() {
        if (deleted) return;
        //the link depends only on its own transform (that of the point it starts from, its parent) and on the next point,
        //which is checked every frame if it's not a handle
        var nextHandle = next as Handle;
        if (!transformChanged && nextHandle != null && nextHandle == seenNext && nextHandle.TransformVersion == seenNextVersion) return;
        transformChanged = false;
        seenNext = nextHandle;
        if (nextHandle != null) seenNextVersion = nextHandle.TransformVersion;
        Update();
    }

    private void Update() {
        UpdateParams();
        var parentGlobalScale = GO.GetGlobalScale(GO.GetParent(this));
        var newScale = new Vector3(1.0f / parentGlobalScale[0], 1.0f / parentGlobalScale[1], 1.0f / parentGlobalScale[2]);
        //setting the scale propagates the transform to the collider, so it is done only on changes
        if (newScale != Scale) GO.SetLocalScale(this, newScale);
    }

    public void Delete() {
        GO.Destroy(this);
        deleted = true;
    }

    public bool IsDeleted() {
        return deleted;
    }
}

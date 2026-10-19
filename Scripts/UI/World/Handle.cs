using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using PhysicsScriptable = Godot.StaticBody3D;
using GameObject = Godot.Node3D;
using Camera = Godot.Camera3D;
using GO = GameEngine.GameObject;

public partial class Handle : PhysicsScriptable, HandleUpdater.IUpdatable {
    Vector3 scale = GameEngine.Vector3.one;
    Camera cam = null;

    public bool hideIfUnusedMode = false;
    public bool hideIfUnusedShow = false;

    //road control points: the yaw (x) and roll (y) of the sections in degrees, relative to the direction of the curve
    public Vector2 curveRotation = Vector2.Zero;
    //the frame of the curve at the point and the rotation the road gave the handle (program coordinates), to tell when
    //the gizmo rotated it
    public Quaternion curveFrame = Quaternion.Identity;
    public Quaternion? curveOrientation = null;
    MeshInstance3D meshInstance;
    CollisionShape3D collisionShape;

    public override void _Ready() {
        meshInstance = GameEngine.Scriptable.FindRecursive<MeshInstance3D>(this);
        collisionShape = GameEngine.Scriptable.FindRecursive<CollisionShape3D>(this);
        UpdateColor(GameEngine.Color.green);
    }

    public int UpdaterIndex { get; set; } = -1;
    //incremented when the global transform changes or the handle leaves the tree, for the links pointing to it
    public ulong TransformVersion { get; private set; } = 0;
    bool transformChanged = true;
    Vector3 cachedPosition;
    Vector3? visualScale = null; //scale of the mesh and the collider
    ulong seenCamVersion = ulong.MaxValue;

    //only the enabled handles are updated
    public override void _EnterTree() {
        SetNotifyTransform(true);
        transformChanged = true;
        if (CanProcess()) HandleUpdater.Register(this);
    }

    public override void _ExitTree() {
        HandleUpdater.Unregister(this);
        TransformVersion++;
    }

    public override void _Notification(int what) {
        if (what == NotificationTransformChanged) {
            transformChanged = true;
            TransformVersion++;
        } else if (what == NotificationEnabled) {
            transformChanged = true; //it could have moved while disabled
            if (IsInsideTree()) HandleUpdater.Register(this);
        } else if (what == NotificationDisabled) {
            HandleUpdater.Unregister(this);
        }
    }

    //called every frame by the HandleUpdater while enabled
    public void UpdateFrame() {
        if (hideIfUnusedMode) {
            // Like deactivating the object: ShowHandle re-enables the whole object, while
            // SetHandleActive(false) would hide the mesh with nothing to show it again.
            if (!hideIfUnusedShow) GO.SetEnabled(this, false);
            hideIfUnusedShow = false;
        }
        UpdateSize();
    }

    void UpdateColor(Color color) {
        if (meshInstance == null) meshInstance = GameEngine.Scriptable.FindRecursive<MeshInstance3D>(this);
        if (meshInstance?.Mesh == null) return;
        meshInstance.MaterialOverride = MaterialManager.GetHandleMaterial(color);
    }

    public void SetColor(Color color) {
        UpdateColor(color);
    }

    public void SetScale(Vector3 scale) {
        this.scale = scale;
        transformChanged = true;
    }

    public Vector3 HandleScale => scale;

    //shown and clickable (see SetHandleActive)
    public bool IsHandleActive() {
        if (collisionShape == null) collisionShape = GameEngine.Scriptable.FindRecursive<CollisionShape3D>(this);
        return collisionShape == null || !collisionShape.Disabled;
    }

    //the camera position and frustum are shared by all the handles, they are read once per frame (in Godot coordinates)
    static Camera camDataCam = null;
    static ulong camDataFrame = ulong.MaxValue;
    static ulong camVersion = 0; //incremented when the camera changes: the handles are resized only then, or when they move
    static bool camMoving = false;
    const float MovingSizeTolerance = 0.02f; //relative size change skipped while the camera moves
    static Vector3 camPos;
    static Plane[] camFrustum = new Plane[0];

    static void UpdateCameraData(Camera cam) {
        var frame = Engine.GetProcessFrames();
        if (frame == camDataFrame && cam == camDataCam) return;
        var pos = cam.GlobalPosition;
        var planes = cam.GetFrustum();
        var changed = cam != camDataCam || pos != camPos || camFrustum.Length != planes.Count;
        if (camFrustum.Length != planes.Count) camFrustum = new Plane[planes.Count];
        for (int i = 0; i < camFrustum.Length; i++) {
            if (camFrustum[i] != planes[i]) changed = true;
            camFrustum[i] = planes[i];
        }
        if (changed) {
            camVersion++;
            camMoving = true;
        } else if (camMoving) {
            //once the camera stops, the handles are resized again without tolerance
            camVersion++;
            camMoving = false;
        }
        camPos = pos;
        camDataFrame = frame;
        camDataCam = cam;
    }

    static bool IsInFrustum(Vector3 center, float radius) {
        foreach (var plane in camFrustum) {
            if (plane.DistanceTo(center) > radius) return false; //the plane normals point outwards
        }
        return true;
    }

    void UpdateSize() {
        if (cam == null) cam = GameEngine.Camera.Get(this);
        if (cam != null) UpdateCameraData(cam);
        //the size depends only on the camera and the handle transforms
        if (!transformChanged && seenCamVersion == camVersion) return;
        seenCamVersion = camVersion;
        var tolerance = camMoving ? MovingSizeTolerance : 0;
        if (transformChanged) {
            transformChanged = false;
            cachedPosition = GlobalPosition;
            tolerance = 0;
        }
        if (meshInstance == null) meshInstance = GameEngine.Scriptable.FindRecursive<MeshInstance3D>(this);
        if (collisionShape == null) collisionShape = GameEngine.Scriptable.FindRecursive<CollisionShape3D>(this);
        if (!visualScale.HasValue) visualScale = meshInstance != null ? meshInstance.Scale : GameEngine.Vector3.one;
        var curScale = visualScale.Value;
        var dist = 1.0f;
        if (cam != null) {
            //only the handles in view are resized (the box mesh has unit size)
            var radius = 0.87f * Mathf.Max(Mathf.Max(curScale.X, curScale.Y), curScale.Z);
            if (!IsInFrustum(cachedPosition, radius)) return;
            dist = cachedPosition.DistanceTo(camPos);
            dist = Mathf.Clamp(dist / 100, 0.5f, 1000);
        }
        //only the mesh and the collider are scaled: scaling the handle would move the transform of its children
        //(the point links, which would have to compensate it) on every camera movement. It is done only on changes,
        //and while the camera moves only on changes over the tolerance (thousands of handles can be in view).
        var newScale = scale * dist;
        if (newScale != curScale && (newScale - curScale).Length() >= tolerance * curScale.Length()) {
            if (meshInstance != null) meshInstance.Scale = newScale;
            if (collisionShape != null) collisionShape.Scale = newScale;
            visualScale = newScale;
        }
    }

    public void SetHandleActive(bool active) {
        //it can be called before _Ready (e.g. right after instancing, before entering the tree)
        if (meshInstance == null) meshInstance = GameEngine.Scriptable.FindRecursive<MeshInstance3D>(this);
        if (collisionShape == null) collisionShape = GameEngine.Scriptable.FindRecursive<CollisionShape3D>(this);
        if (meshInstance != null) meshInstance.Visible = active;
        if (collisionShape != null) collisionShape.Disabled = !active;
    }
}

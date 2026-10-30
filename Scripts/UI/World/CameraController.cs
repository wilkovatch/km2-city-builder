using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;

public partial class CameraController : Camera3D {
    const float defaultZoomFactor = 20.0f;
    const float minPivotDistance = 1.0f;
    const float dollyStep = 1.2f; //distance ratio of one wheel step in scheme B
    const float keyDollyRate = 10.0f; //wheel steps per second when holding the zoom keys in scheme B
    const float wheelPanStep = 0.1f; //fraction of the view size panned by one wheel step in scheme B
    const float dragZoomSpeed = 3.0f;

    //scheme A: right button turns the view in place, middle button pans
    //scheme B: middle button orbits around a pivot, with Shift pans and with Ctrl zooms
    public enum ControlScheme { SchemeA, SchemeB }
    enum DragMode { None, Orbit, Pan, Zoom }

    Camera3D cam;
    ControlScheme scheme = ControlScheme.SchemeA;
    DragMode dragMode = DragMode.None;
    bool dragFromMiddle = false;
    Vector3 pivot = GameEngine.Vector3.zero;
    float lastPivotDistance = 50.0f;
    Vector3 oldMousePoint = GameEngine.Vector3.zero;
    Vector3 middleClickOrigin = GameEngine.Vector3.zero;
    float rotSpeed = 200.0f;
    float panSpeed = 10.0f;
    float zoomSpeed = 2.0f;
    bool panning = false;
    bool rotating = false;
    Vector3 curRotation = GameEngine.Vector3.zero;
    float zoomFactor = defaultZoomFactor;
    bool zoomingIn = false;
    public bool controlsEnabled = true;
    uint defaultCullingMask;

    public override void _Ready() {
        GameEngine.Input.ReloadKeys(); //TODO: remove after done
        cam = this;
        curRotation = RotationDegrees;
        SetPropsCullingDistance(PreferencesManager.Get("propsCullingDistance", 300.0f));
        cam.CullMask &= ~GameEngine.Camera.RenderOnlyLayer;
        defaultCullingMask = cam.CullMask;
        LoadControls();
    }

    public void LoadControls() {
        scheme = SettingsManager.Get("cameraControls", 0) == 1 ? ControlScheme.SchemeB : ControlScheme.SchemeA;
        Input.emulateMiddleButton = SettingsManager.Get("emulateMiddleButton", false);
        panning = false;
        rotating = false;
        dragMode = DragMode.None;
    }

    public void SetPropsCullingDistance(float dist) {
        var distances = new float[32];
        for (int i = 0; i < 32; i++) {
            distances[i] = cam.Far;
        }
        distances[13] = dist;
        GameEngine.Camera.SetLayerCullDistances(cam, distances);
    }

    Vector3 GetMousePoint() {
        Vector2 mousePosition = GetViewport().GetMousePosition();
        Vector2 viewSize = GetViewport().GetVisibleRect().Size;
        return new Vector3(mousePosition.X / viewSize.X, mousePosition.Y / viewSize.Y, 0);
    }

    float GetTime() {
        return Time.GetTicksUsec() / 1000000.0f;
    }

    bool wireframe = false;

    public bool Wireframe => wireframe;

    public bool GroundOnly => cam.CullMask != defaultCullingMask;

    public bool ToggleWireframe() {
        wireframe = !wireframe;
        GetViewport().DebugDraw = wireframe ? Viewport.DebugDrawEnum.Wireframe : Viewport.DebugDrawEnum.Disabled;
        if (wireframe) {
            //a copy of the world environment (same ambient light) with a plain background instead of the sky
            var env = GetWorld3D()?.Environment?.Duplicate() as Godot.Environment ?? new Godot.Environment();
            env.BackgroundMode = Godot.Environment.BGMode.Color;
            env.BackgroundColor = GameEngine.Camera.WireframeBackground;
            cam.Environment = env;
        } else {
            cam.Environment = null;
        }
        return wireframe;
    }

    public bool ToggleGroundOnly() {
        //render layers mirror the layers (see GameEngine.GameObject.SetLayer), the overlay one is for the gizmo
        if (cam.CullMask == defaultCullingMask) {
            cam.CullMask = 1u << 3 | 1u << 7 | GameEngine.Camera.OverlayLayer;
            return true;
        }
        else {
            cam.CullMask = defaultCullingMask;
            return false;
        }
    }

    void Rotate() {
        var mousePoint = GetMousePoint();
        var delta = (mousePoint - oldMousePoint) * rotSpeed;
        curRotation += new Vector3(-delta.Y, -delta.X, 0);
        RotationDegrees = curRotation;
        oldMousePoint = mousePoint;
    }

    void Pan() {
        var mousePoint = GetMousePoint();
        var delta = panSpeed * zoomFactor * zoomFactor * (mousePoint - oldMousePoint) / 100.0f;
        Position = Position - (Transform.Basis.Y * -delta.Y + Transform.Basis.X * delta.X);
        oldMousePoint = mousePoint;
    }

    void Zoom(float val) {
        zoomFactor -= val;
        zoomFactor = Mathf.Max(zoomFactor, 5);
        Position += val * zoomFactor * zoomSpeed * (-Transform.Basis.Z) / 10.0f;
    }

    bool RaycastScreen(Vector2 screenPos, out Vector3 hitPoint) {
        Vector3 rayOrigin = cam.ProjectRayOrigin(screenPos);
        Vector3 rayDirection = cam.ProjectRayNormal(screenPos);
        var spaceState = GetWorld3D().DirectSpaceState;
        var queryParams = PhysicsRayQueryParameters3D.Create(rayOrigin, rayOrigin + rayDirection * 999999f);
        var result = spaceState.IntersectRay(queryParams);
        if (result.Count > 0) {
            hitPoint = (Vector3)result["position"];
            return true;
        }
        hitPoint = GameEngine.Vector3.zero;
        return false;
    }

    async void ZoomIn() {
        bool onUI = UIDetection.OnUI();
        if (onUI) return;
        if (RaycastScreen(GetViewport().GetMousePosition(), out var hitPoint)) {
            await ZoomInCoroutine(hitPoint);
        }
    }

    Vector3 Forward() {
        return -Transform.Basis.Z.Normalized();
    }

    //the point under the center of the view, or a point at the last pivot distance if there is nothing there
    Vector3 PickPivot() {
        var viewSize = GetViewport().GetVisibleRect().Size;
        if (RaycastScreen(viewSize / 2.0f, out var hitPoint) && (hitPoint - Position).Dot(Forward()) > 0) {
            lastPivotDistance = Mathf.Max(Position.DistanceTo(hitPoint), minPivotDistance);
            return hitPoint;
        }
        return Position + Forward() * lastPivotDistance;
    }

    float DepthOf(Vector3 point) {
        return Mathf.Max((point - Position).Dot(Forward()), minPivotDistance);
    }

    //world size of the view at the given depth
    Vector2 ViewSizeAt(float depth) {
        var viewSize = GetViewport().GetVisibleRect().Size;
        var h = 2.0f * depth * Mathf.Tan(Mathf.DegToRad(Fov) * 0.5f);
        return new Vector2(h * viewSize.X / viewSize.Y, h);
    }

    //keeps the scheme A speeds consistent with the distance reached in scheme B
    void UpdateZoomFactor(float depth) {
        zoomFactor = Mathf.Sqrt(depth) * 4.0f + 5.0f;
    }

    void Orbit(Vector3 mouseDelta) {
        var yaw = Mathf.DegToRad(-mouseDelta.X * rotSpeed);
        var curPitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(Forward().Y, -1.0f, 1.0f)));
        var newPitch = Mathf.Clamp(curPitch - mouseDelta.Y * rotSpeed, -89.0f, 89.0f);
        var pitch = Mathf.DegToRad(newPitch - curPitch);
        var right = Transform.Basis.X.Normalized();
        var rot = new Basis(Vector3.Up, yaw) * new Basis(right, pitch);
        Position = pivot + rot * (Position - pivot);
        Basis = (rot * Transform.Basis).Orthonormalized();
    }

    //the point under the cursor at the depth of the pivot follows the cursor
    void PanAtPivot(Vector3 mouseDelta) {
        var size = ViewSizeAt(DepthOf(pivot));
        var offset = -Transform.Basis.X * mouseDelta.X * size.X + Transform.Basis.Y * mouseDelta.Y * size.Y;
        Position += offset;
        pivot += offset;
    }

    void DragZoom(Vector3 mouseDelta) {
        var d = DepthOf(pivot);
        var newD = Mathf.Max(d * Mathf.Exp(mouseDelta.Y * dragZoomSpeed), minPivotDistance);
        Position += Forward() * (d - newD);
    }

    //moves toward the point under the center of the view, positive steps zoom in
    void Dolly(float steps) {
        var d = DepthOf(PickPivot());
        var newD = Mathf.Max(d / Mathf.Pow(dollyStep, steps), minPivotDistance);
        Position += Forward() * (d - newD);
        lastPivotDistance = newD;
        UpdateZoomFactor(newD);
    }

    void WheelPan(float steps, bool horizontal) {
        var size = ViewSizeAt(DepthOf(PickPivot()));
        if (horizontal) {
            Position += Transform.Basis.X * steps * size.X * wheelPanStep;
        }
        else {
            Position += Transform.Basis.Y * steps * size.Y * wheelPanStep;
        }
    }

    public async void ZoomIn(Vector3 point, float distance) {
        await ZoomInCoroutine(point, true, distance);
    }

    //places the camera at once, looking at the target (Godot coordinates)
    public void SetView(Vector3 position, Vector3 target) {
        Position = position;
        var dir = target - position;
        if (dir.LengthSquared() > 1e-6f) LookAt(target, Mathf.Abs(dir.Normalized().Y) > 0.999f ? Vector3.Forward : Vector3.Up);
        curRotation = RotationDegrees;
        lastPivotDistance = Mathf.Max(dir.Length(), minPivotDistance);
    }

    float EaseOutQuad(float t) {
        return (t - 1) * (t - 1) * (t - 1) + 1;
    }

    public static Vector3 ClosestPointOnPlane(Vector3 point, Vector3 planeNormal, Vector3 planePoint) {
        planeNormal = planeNormal.Normalized();
        float distance = planeNormal.Dot(point - planePoint);
        return point - distance * planeNormal;
    }

    private async Task ZoomInCoroutine(Vector3 hitPoint, bool outOfView = false, float distance = 0.0f) {
        var pos0 = Position;
        var startTime = GetTime();
        var duration = 0.5f;
        zoomingIn = true;
        zoomFactor = defaultZoomFactor;
        var targetPoint = outOfView ? (hitPoint + Transform.Basis.Z * distance) : ClosestPointOnPlane(hitPoint, -Transform.Basis.Z, pos0);
        while (GetTime() - startTime <= duration) {
            var a = (GetTime() - startTime) / duration;
            a = Mathf.Min(a, 1);
            Position = pos0.Lerp(targetPoint, EaseOutQuad(a));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        zoomFactor = Mathf.Sqrt(Position.DistanceTo(hitPoint)) * 4.0f + 5.0f;
        zoomingIn = false;
    }

    bool canZoomIn = false;
    bool middleButtonWasDown = false;

    float GetZoomInput() {
        if (Input.GetInputStart(KeyCommand.ZoomIn2)) {
            return 1;
        }
        else if (Input.GetInputStart(KeyCommand.ZoomOut2)) {
            return -1;
        }
        else if (Input.GetInputPressed(KeyCommand.ZoomIn)) {
            return 1;
        }
        else if (Input.GetInputPressed(KeyCommand.ZoomOut)) {
            return -1;
        }
        return 0;
    }

    bool GetZoomInAndPanInput() {
        return Input.GetInputPressed(KeyCommand.ZoomInAndPan)
            || Input.MiddlePressed();
    }

    bool GetRotateInput() {
        return Input.GetInputPressed(KeyCommand.RotateCamera);
    }

    public override void _Process(double delta) {
        if (zoomingIn || !controlsEnabled) return;
        bool onUI = UIDetection.OnUI();
        if (onUI) return;
        if (scheme == ControlScheme.SchemeB) {
            ProcessSchemeB((float)delta);
        }
        else {
            ProcessSchemeA();
        }
    }

    void ProcessSchemeB(float delta) {
        var wheel = Input.GetInputStart(KeyCommand.ZoomIn2) ? 1 : (Input.GetInputStart(KeyCommand.ZoomOut2) ? -1 : 0);
        if (wheel != 0) {
            if (Input.GetInputPressed(KeyCommand.Modifier2)) WheelPan(wheel, true);
            else if (Input.GetInputPressed(KeyCommand.Modifier1)) WheelPan(wheel, false);
            else Dolly(wheel);
        }
        else if (Input.GetInputPressed(KeyCommand.ZoomIn)) {
            Dolly(delta * keyDollyRate);
        }
        else if (Input.GetInputPressed(KeyCommand.ZoomOut)) {
            Dolly(-delta * keyDollyRate);
        }

        if (dragMode == DragMode.None) {
            bool middleStart = Input.MiddleJustPressed();
            if (middleStart || Input.GetInputPressed(KeyCommand.ZoomInAndPan)) {
                dragFromMiddle = middleStart;
                if (!middleStart || Input.GetInputPressed(KeyCommand.Modifier1)) dragMode = DragMode.Pan;
                else if (Input.GetInputPressed(KeyCommand.Modifier2)) dragMode = DragMode.Zoom;
                else dragMode = DragMode.Orbit;
                oldMousePoint = GetMousePoint();
                middleClickOrigin = oldMousePoint;
                canZoomIn = middleStart;
                pivot = PickPivot();
            }
            return;
        }

        bool held = dragFromMiddle ? Input.MiddlePressed() : Input.GetInputPressed(KeyCommand.ZoomInAndPan);
        if (held) {
            var mousePoint = GetMousePoint();
            if (middleClickOrigin.DistanceTo(mousePoint) > 0.001f) canZoomIn = false;
            var mouseDelta = mousePoint - oldMousePoint;
            switch (dragMode) {
                case DragMode.Orbit: Orbit(mouseDelta); break;
                case DragMode.Pan: PanAtPivot(mouseDelta); break;
                case DragMode.Zoom: DragZoom(mouseDelta); break;
            }
            oldMousePoint = mousePoint;
        }
        else {
            dragMode = DragMode.None;
            UpdateZoomFactor(DepthOf(pivot));
            if (dragFromMiddle && canZoomIn && Input.MiddleJustReleased()) ZoomIn();
        }
    }

    void ProcessSchemeA() {
        var zoomInput = GetZoomInput();
        if (zoomInput != 0) {
            Zoom(zoomInput);
        }
        if (GetZoomInAndPanInput() && !rotating) {
            if (!panning) {
                oldMousePoint = GetMousePoint();
                middleClickOrigin = oldMousePoint;
                panning = true;
                canZoomIn = true;
                middleButtonWasDown = true;
            }
            else {
                if (middleClickOrigin.DistanceTo(GetMousePoint()) > 0.001f) canZoomIn = false;
                Pan();
            }
        }
        else {
            bool middleReleased = middleButtonWasDown && Input.MiddleJustReleased();
            if (middleReleased && panning && canZoomIn) {
                ZoomIn();
            }
            panning = false;
            if (middleReleased) middleButtonWasDown = false;
        }
        if (GetRotateInput() && !panning) {
            if (!rotating) {
                oldMousePoint = GetMousePoint();
                curRotation = RotationDegrees; //the view may have been moved by other means since the last rotation
                rotating = true;
            }
            else {
                Rotate();
            }
        }
        else {
            rotating = false;
        }
    }

}

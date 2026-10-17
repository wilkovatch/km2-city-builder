using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;

public partial class CameraController : Camera3D {
    const float defaultZoomFactor = 20.0f;

    Camera3D cam;
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
        defaultCullingMask = cam.CullMask;
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

    /*public void ToggleWireframe() { //TODO: fix in build
        wireframe = !wireframe;
        cam.clearFlags = wireframe ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
    }*/

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

    /*void OnPreRender() {
        GL.wireframe = wireframe;
    }
    void OnPostRender() {
        GL.wireframe = false;
    }*/

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

    async void ZoomIn() {
        bool onUI = UIDetection.OnUI();
        if (onUI) return;
        Vector2 mousePosition = GetViewport().GetMousePosition();
        Vector3 rayOrigin = cam.ProjectRayOrigin(mousePosition);
        Vector3 rayDirection = cam.ProjectRayNormal(mousePosition);

        var spaceState = GetWorld3D().DirectSpaceState;

        var queryParams = PhysicsRayQueryParameters3D.Create(rayOrigin, rayOrigin + rayDirection * 999999f);

        var result = spaceState.IntersectRay(queryParams);

        if (result.Count > 0) {
            Vector3 hitPoint = (Vector3)result["position"];
            await ZoomInCoroutine(hitPoint);
        }
    }

    public async void ZoomIn(Vector3 point, float distance) {
        await ZoomInCoroutine(point, true, distance);
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
            || Input.GetInputPressed(KeyCommand.ZoomInAndPan2);
    }

    bool GetRotateInput() {
        return Input.GetInputPressed(KeyCommand.RotateCamera);
    }

    public override void _Process(double delta) {
        if (zoomingIn || !controlsEnabled) return;
        bool onUI = UIDetection.OnUI();
        if (onUI) return;
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
            bool middleReleased = middleButtonWasDown
                && Godot.Input.IsActionJustReleased(KeyCommand.ZoomInAndPan2.ToString());
            if (middleReleased && panning && canZoomIn) {
                ZoomIn();
            }
            panning = false;
            if (middleReleased) middleButtonWasDown = false;
        }
        if (GetRotateInput() && !panning) {
            if (!rotating) {
                oldMousePoint = GetMousePoint();
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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour {
    const float defaultZoomFactor = 20.0f;

    Camera cam;
    Vector3 oldMousePoint = Vector3.zero;
    float rotSpeed = 200.0f;
    float panSpeed = 10.0f;
    float zoomSpeed = 2.0f;
    bool panning = false;
    bool rotating = false;
    Vector3 curRotation = Vector3.zero;
    float zoomFactor = defaultZoomFactor;
    bool zoomingIn = false;
    public bool controlsEnabled = true;
    int defaultCullingMask;

    void Start() {
        cam = GetComponent<Camera>();
        curRotation = transform.rotation.eulerAngles;
        SetPropsCullingDistance(PreferencesManager.Get("propsCullingDistance", 300.0f));
        defaultCullingMask = cam.cullingMask;
    }

    public void SetPropsCullingDistance(float dist) {
        var distances = new float[32];
        for (int i = 0; i < 32; i++) {
            distances[i] = cam.farClipPlane;
        }
        distances[13] = dist;
        cam.layerCullDistances = distances;
    }

    Vector3 GetMousePoint() {
        return cam.ScreenToViewportPoint(Input.mousePosition);
    }

    bool wireframe = false;

    public void ToggleWireframe() { //TODO: fix in build
        wireframe = !wireframe;
        cam.clearFlags = wireframe ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
    }

    public bool ToggleGroundOnly() {
        if (cam.cullingMask == defaultCullingMask) {
            cam.cullingMask = 1 << 3 | 1 << 7;
            return true;
        } else {
            cam.cullingMask = defaultCullingMask;
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
        curRotation += new Vector3(-delta.y, delta.x, 0);
        transform.eulerAngles = curRotation;
        oldMousePoint = mousePoint;
    }

    void Pan() {
        var mousePoint = GetMousePoint();
        var delta = panSpeed * zoomFactor * zoomFactor * (mousePoint - oldMousePoint) / 100.0f;
        transform.position = transform.position - (transform.up * delta.y + transform.right * delta.x);
        oldMousePoint = mousePoint;
    }

    void Zoom(float val) {
        zoomFactor -= val;
        zoomFactor = Mathf.Max(zoomFactor, 5);
        transform.position += val * zoomFactor * zoomSpeed * transform.forward / 10.0f;
    }

    void ZoomIn() {
        bool onUI = UIDetection.OnUI();
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!onUI && Physics.Raycast(transform.position, ray.direction, out RaycastHit hit, float.MaxValue)) {
            StartCoroutine(ZoomInCoroutine(hit.point));
        }
    }

    public void ZoomIn(Vector3 point, float distance) {
        StartCoroutine(ZoomInCoroutine(point, true, distance));
    }

    float EaseOutQuad(float t) {
        return (t - 1) * (t - 1) * (t - 1) + 1;
    }

    IEnumerator ZoomInCoroutine(Vector3 hitPoint, bool outOfView = false, float distance = 0.0f) {
        var pos0 = transform.position;
        var startTime = Time.time;
        var duration = 0.5f;
        zoomingIn = true;
        zoomFactor = defaultZoomFactor;
        var cameraPlane = new Plane(transform.forward, pos0);
        var targetPoint = outOfView ? (hitPoint - transform.forward * distance) : cameraPlane.ClosestPointOnPlane(hitPoint);
        while (Time.time - startTime <= duration) {
            var a = (Time.time - startTime) / duration;
            a = Mathf.Min(a, 1);
            transform.position = Vector3.Lerp(pos0, targetPoint, EaseOutQuad(a));
            yield return new WaitForEndOfFrame();
        }
        zoomFactor = Mathf.Sqrt(Vector3.Distance(transform.position, hitPoint)) * 4.0f + 5.0f;
        zoomingIn = false;
    }

    bool canZoomIn = false;

    float GetZoomInput() {
        if (Input.mouseScrollDelta.y != 0) {
            return Input.mouseScrollDelta.y;
        } else if (Input.GetKeyDown(KeyCode.PageUp)) {
            return 1;
        } else if (Input.GetKeyDown(KeyCode.PageDown)) {
            return -1;
        }
        return 0;
    }

    bool GetZoomInAndPanInput() {
        return Input.GetMouseButton(2) || Input.GetKey(KeyCode.Home);
    }

    bool GetRotateInput() {
        return Input.GetMouseButton(1);
    }

    void Update() {
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
                panning = true;
                canZoomIn = true;
            } else {
                if (oldMousePoint != GetMousePoint()) canZoomIn = false;
                Pan();
            }
        } else {
            if (panning && canZoomIn) {
                ZoomIn();
            }
            panning = false;
        }
        if (GetRotateInput() && !panning) {
            if (!rotating) {
                oldMousePoint = GetMousePoint();
                rotating = true;
            } else {
                Rotate();
            }
        } else {
            rotating = false;
        }
    }

}

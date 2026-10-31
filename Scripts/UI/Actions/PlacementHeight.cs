using Godot;
using RaycastHit = GameEngine.Raycast.RaycastHit;

//The height of the points placed by the placers (driven by TerrainClick): moving the mouse while holding Shift sets an
//absolute height (along the vertical line of the point under the cursor when Shift was pressed), tapping Shift (no
//movement, no other input) brings back the placement on the ground
public static class PlacementHeight {
    const float dragThreshold = 4.0f; //pixels before the hold becomes a drag
    const float steepView = 0.97f; //|forward.y| above which the drag follows the vertical mouse movement

    //the points are placed at Height instead of on the ground
    public static bool Raised { get; private set; }
    public static float Height { get; private set; }
    //Raised, and the current action can place raised points this frame (set by TerrainClick, read by the placers)
    public static bool Active { get; set; }
    public static bool Holding => holding;

    //whether a placed point turns off the projection to the ground of its element (which would move it): a raised point
    //(on the ground hit), or the first point of the element (wherever it is, e.g. on an intersection) with the terrain
    //hidden (the hidden terrain, where the projection would put the element, can be at another height than the clicked
    //points; the projection can be turned on again)
    public static bool TurnsOffProjection(bool firstPoint, bool onGround) => (Active && onGround) || (firstPoint && CityGroundHelper.TerrainHidden);
    public static bool Dragging => holding && dragging;

    static bool holding, dragging, cancelled, sessionEnding;
    static Vector3 basePoint;
    static Vector2 startMouse;

    public static void Reset() {
        Raised = false;
        Active = false;
        holding = false;
        dragging = false;
    }

    //back to the selection of objects: the placement goes back to the ground, unless an action placing points takes over
    //in the same frame (creating another element from the menu closes the current editor first), which keeps the height
    public static void EndSession() {
        sessionEnding = true;
    }

    //called by TerrainClick every frame, with its current action
    public static void CheckSessionEnd(TerrainAction action) {
        if (!sessionEnding) return;
        sessionEnding = false;
        if (action == null || !action.SupportsPlacementHeight) Reset();
    }

    //the projection to the ground of the edited element turned on again (in its panel): back to the ground as well
    public static void OnProjectionToggled(bool projected) {
        if (projected) Reset();
    }

    //Shift pressed with the point under the cursor
    public static void BeginHold(Vector3 point, Vector2 mouse) {
        holding = true;
        dragging = false;
        cancelled = false;
        basePoint = point;
        startMouse = mouse;
    }

    //other input while Shift is held (or the cursor left the city view): neither a drag nor a reset, the height reached
    //so far is kept
    public static void Cancel() {
        if (holding) cancelled = true;
    }

    //Shift released: a tap brings back the placement on the ground
    public static void EndHold() {
        if (holding && !cancelled && !dragging) Reset();
        holding = false;
        dragging = false;
    }

    public static void Update(Camera3D cam, Vector2 mouse) {
        if (!holding || cancelled) return;
        if (!dragging) {
            if (mouse.DistanceTo(startMouse) < dragThreshold) return;
            dragging = true;
            Raised = true;
        }
        var height = GetDragHeight(cam, mouse);
        Height = Snapping.Enabled ? Mathf.Round(height) : height;
    }

    //the height of the vertical line through the base point under the cursor, on the plane through it facing the camera
    static float GetDragHeight(Camera3D cam, Vector2 mouse) {
        var ray = GameEngine.Raycast.GetRayFromScreen(cam, mouse);
        var size = cam.GetViewport().GetVisibleRect().Size;
        var forward = GameEngine.Raycast.GetRayFromScreen(cam, size * 0.5f).direction;
        var normal = new Vector3(forward.X, 0, forward.Z);
        if (Mathf.Abs(forward.Y) <= steepView && normal.LengthSquared() > 1e-6f) {
            normal = normal.Normalized();
            var denom = ray.direction.Dot(normal);
            if (Mathf.Abs(denom) > 1e-6f) {
                var t = (basePoint - ray.origin).Dot(normal) / denom;
                if (t > 0) return (ray.origin + ray.direction * t).Y;
            }
        }
        //looking (almost) straight down the plane is edge-on: the vertical mouse movement at the scale of the view there
        float unitsPerPixel;
        if (cam.Projection == Camera3D.ProjectionType.Orthogonal) {
            unitsPerPixel = cam.Size / size.Y;
        } else {
            var dist = basePoint.DistanceTo(ray.origin);
            unitsPerPixel = 2 * dist * Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f) / size.Y;
        }
        return basePoint.Y + (startMouse.Y - mouse.Y) * unitsPerPixel;
    }

    //where a raised point goes: on the vertical line while dragging, otherwise where the mouse ray meets the horizontal
    //plane at Height (null if it doesn't); no collider, as a point on the ground
    public static RaycastHit? GetRaisedHit(Vector3 rayOrigin, Vector3 rayDirection) {
        Vector3 point;
        if (Dragging) {
            point = new Vector3(basePoint.X, Height, basePoint.Z);
        } else {
            if (Mathf.Abs(rayDirection.Y) < 1e-6f) return null;
            var t = (Height - rayOrigin.Y) / rayDirection.Y;
            if (t <= 0) return null;
            point = rayOrigin + rayDirection * t;
            if (Snapping.Enabled) point = new Vector3(Mathf.Round(point.X), point.Y, Mathf.Round(point.Z));
        }
        return new RaycastHit { point = point };
    }
}

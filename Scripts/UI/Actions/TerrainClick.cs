using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;

public partial class TerrainClick : Control {
    // City terrain is only a few thousand units across. Keeping ray lengths bounded
    // avoids constructing physics queries with near-infinite endpoints.
    const float TerrainRayDistance = 100000f;
    const int GroundMask = (1 << 3) | (1 << 7); //the terrain and the generated meshes, under the raised points
    public CityGroundHelper helper;
    RaycastHit hit, projectedHit;
    public TerrainAction modifier = null;
    bool isModifying = false;
    public bool uiEnabled = true;
    int strokeId = 0; //the changes made while the button is held (brush strokes, mesh painting) are a single undo step

    public override void _Ready() {
        //run before the gizmo (on the camera, earlier in the tree): the actions must see the selection
        //from before the click, otherwise the click that selects a road point also inserts a point after it
        ProcessPriority = -1;
    }

    //other input while Shift is held: not a placement height change (e.g. the camera panning with Shift) nor a tap
    //bringing back the placement on the ground (e.g. a Shift-click selection); a click while dragging places the point
    public override void _Input(InputEvent e) {
        if (!PlacementHeight.Holding) return;
        if (e is InputEventMouseButton button && button.Pressed) {
            if (!PlacementHeight.Dragging || button.ButtonIndex != MouseButton.Left) PlacementHeight.Cancel();
        } else if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode != Key.Shift && key.PhysicalKeycode != Key.Shift) {
            PlacementHeight.Cancel();
        }
    }

    public override void _Process(double delta) {
        PlacementHeight.CheckSessionEnd(modifier);
        PlacementHeight.Active = false;
        if (!uiEnabled || modifier == null || (!Input.GetInputPressed(KeyCommand.Select) && modifier is TerrainModifier.Null)) {
            UpdatePlacementHeight(false, null);
            ActionHandlerManager.HideHeightLine();
        }
        if (!uiEnabled) return;
        if (modifier == null || (!Input.GetInputPressed(KeyCommand.Select) && modifier is TerrainModifier.Null)) {
            if (isModifying) GameEngine.Terrain.SyncHeightmap(helper.terrainObj);
            isModifying = false;
            return;
        }
        if (!Input.GetInputPressed(KeyCommand.Select) && isModifying) {
            GameEngine.Terrain.SyncHeightmap(helper.terrainObj);
            isModifying = false;
        }
        var ray = GameEngine.Raycast.GetRayFromScreen(GetViewport().GetCamera3D(), GetViewport().GetMousePosition());
        var masks = modifier.GetLayerMasks();
        var groundIndex = modifier.GroundHitIndex;
        var onUI = OnUI();
        var hits = new List<RaycastHit?>();
        for (int i = 0; i < masks.Count; i++) {
            var mask = masks[i];
            RaycastHit? res = null;
            if (!onUI) {
                if (Raycast(ray.origin, ray.direction, out hit, TerrainRayDistance, mask)) {
                    res = hit;
                } else if (i == groundIndex && CityGroundHelper.TerrainHidden) {
                    //nothing to place on with the terrain hidden: the zero height plane
                    res = GetZeroPlaneHit(ray.origin, ray.direction);
                }
                if (res.HasValue) {
                    if (Snapping.Enabled) res = SnapToGrid(res.Value, mask);
                    isModifying = true;
                }
            }
            hits.Add(res);
        }
        ApplyPlacementHeight(hits, masks, groundIndex, onUI, ray.origin, ray.direction);
        if (Input.GetInputStart(KeyCommand.Select)) strokeId++;
        History.UndoManager.Record(modifier.GetType().Name, () => modifier.Apply(hits), (nameof(TerrainClick), strokeId));
    }

    //the hit on the ground replaced by the raised point (PlacementHeight), with its line from the ground
    void ApplyPlacementHeight(List<RaycastHit?> hits, List<int> masks, int groundIndex, bool onUI, Vector3 rayOrigin, Vector3 rayDirection) {
        var placing = groundIndex >= 0 && groundIndex < hits.Count && !onUI && modifier.SupportsPlacementHeight;
        Vector3? current = null;
        if (placing) current = PlacementHeight.Raised ? PlacementHeight.GetRaisedHit(rayOrigin, rayDirection)?.point : hits[groundIndex]?.point;
        UpdatePlacementHeight(placing && !TextInputFocused() && !NumericFieldDrag.IsDragging && !GizmoTransforming(), current);
        if (!placing || !PlacementHeight.Raised) {
            ActionHandlerManager.HideHeightLine();
            return;
        }
        var raised = PlacementHeight.GetRaisedHit(rayOrigin, rayDirection);
        hits[groundIndex] = raised;
        PlacementHeight.Active = true;
        //no line while a hit before it (an anchor, a road end, ...) is where the point goes instead
        var covered = false;
        for (int i = 0; i < groundIndex; i++) covered |= hits[i].HasValue;
        if (raised.HasValue && !covered) {
            ActionHandlerManager.ShowHeightLine(GetGroundUnder(raised.Value.point), raised.Value.point);
        } else {
            ActionHandlerManager.HideHeightLine();
        }
    }

    //Shift pressed over a point to place starts a hold, moving the mouse while holding it sets the height, releasing it
    //without moving (nor other input) brings back the placement on the ground
    void UpdatePlacementHeight(bool allowed, Vector3? current) {
        var shift = Input.GetInputPressed(KeyCommand.Modifier1);
        if (!allowed) {
            PlacementHeight.Cancel();
            if (!shift) PlacementHeight.EndHold();
            return;
        }
        if (Input.GetInputStart(KeyCommand.Modifier1)) {
            if (current.HasValue) PlacementHeight.BeginHold(current.Value, GetViewport().GetMousePosition());
        } else if (shift) {
            PlacementHeight.Update(GetViewport().GetCamera3D(), GetViewport().GetMousePosition());
        } else if (PlacementHeight.Holding) {
            PlacementHeight.EndHold();
        }
    }

    //the ground (the terrain if visible, the generated meshes) under the point, or the zero height
    Vector3 GetGroundUnder(Vector3 point) {
        var mask = CityGroundHelper.ExcludeHiddenTerrain(GroundMask);
        if (GameEngine.Raycast.DoRaycast(helper, point + Vector3.Up * 0.01f, Vector3.Down, out var below, TerrainRayDistance, mask)) return below.point;
        return new Vector3(point.X, 0, point.Z);
    }

    static RaycastHit? GetZeroPlaneHit(Vector3 origin, Vector3 direction) {
        if (Mathf.Abs(direction.Y) < 1e-6f) return null;
        var t = -origin.Y / direction.Y;
        if (t <= 0 || t > TerrainRayDistance) return null;
        return new RaycastHit { point = origin + direction * t };
    }

    bool OnUI() {
        var pos = GetViewport().GetMousePosition();
        var xOut = pos.X < 0 || pos.X > DisplayServer.WindowGetSize().X;
        var yOut = pos.Y < 0 || pos.Y > DisplayServer.WindowGetSize().Y;
        return UIDetection.OnUI() || xOut || yOut;
    }

    bool TextInputFocused() {
        var focused = GetViewport().GuiGetFocusOwner();
        return focused is LineEdit || focused is TextEdit;
    }

    static bool GizmoTransforming() {
        return CityBuilderMenuBar.staticGizmo?.isTransforming ?? false;
    }

    RaycastHit SnapToGrid(RaycastHit hit, int mask) {
        const float snapRayHeight = 1000000;
        var pos = hit.point + Vector3.Up * snapRayHeight;
        pos = new Vector3(Mathf.Round(pos.X), pos.Y, Mathf.Round(pos.Z));
        // The ray starts snapRayHeight above the hit, so it must be longer than that to reach it.
        var found = Raycast(pos, Vector3.Down, out projectedHit, snapRayHeight * 2, mask);
        if (found) return projectedHit;
        //a point on the zero height plane (nothing under it) stays on it
        if (hit.collider == null) return new RaycastHit { point = new Vector3(pos.X, hit.point.Y, pos.Z) };
        return hit;
    }

    //the terrain can't be clicked while it's hidden (View->Hide terrain grid)
    bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, int mask) {
        return modifier.Raycast(helper, origin, direction, out hit, distance, CityGroundHelper.ExcludeHiddenTerrain(mask));
    }
}

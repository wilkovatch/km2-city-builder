using System.Collections;
using System.Collections.Generic;
using Godot;
using RaycastHit = GameEngine.Raycast.RaycastHit;

public abstract class TerrainAction {
    System.Func<Node3D> rotationTarget;
    System.Func<bool> rotationAllowed;
    Vector2 rotationMouseStart;
    bool rotatingPlacement;

    //The target can be generated (or regenerated) after the placement frame, so resolve it when dragging.
    protected void BeginPlacementRotation(Viewport viewport, System.Func<Node3D> target, System.Func<bool> allowed) {
        rotationTarget = target;
        rotationAllowed = allowed;
        rotationMouseStart = viewport.GetMousePosition();
        rotatingPlacement = false;
        CityBuilderMenuBar.staticGizmo?.ConsumeClick();
    }

    internal void EndPlacementRotation() {
        rotationTarget = null;
        rotationAllowed = null;
        rotatingPlacement = false;
    }

    internal bool HasPlacementRotation => rotationTarget != null;

    //Called inside the placement's undo stroke, independently of surface hits.
    internal bool UpdatePlacementRotation(Vector2 mouse, Vector3 origin, Vector3 direction, bool onUI, System.Action onChanged) {
        if (rotationTarget == null) return false;
        if (!GameEngine.Input.GetInputPressed(GameEngine.Input.KeyCommand.Select) || !rotationAllowed()) {
            EndPlacementRotation();
            return false;
        }
        if (onUI) return true;
        rotatingPlacement |= mouse.DistanceSquaredTo(rotationMouseStart) >= 16;
        if (!rotatingPlacement || Mathf.Abs(direction.Y) < 1e-6f) return true;
        var target = rotationTarget();
        if (target == null || !GodotObject.IsInstanceValid(target) || target.IsQueuedForDeletion()) return true;
        var position = GameEngine.GameObject.GetPosition(target);
        var t = (position.Y - origin.Y) / direction.Y;
        if (t <= 0 || !float.IsFinite(t)) return true;
        var facing = origin + direction * t - position;
        if (new Vector2(facing.X, facing.Z).LengthSquared() < 1e-6f) return true;
        var rotation = GameEngine.GameObject.GetRotation(target);
        var yaw = Mathf.RadToDeg(Mathf.Atan2(facing.X, facing.Z));
        if (Snapping.Enabled) yaw = Mathf.Round(yaw / 15f) * 15f;
        if (Mathf.Abs(Mathf.Wrap(yaw - rotation.Y, -180, 180)) < 0.001f) return true;
        rotation.Y = yaw;
        History.UndoManager.TouchOwner(target);
        GameEngine.GameObject.SetRotation(target, rotation);
        if (target is MeshInstance mesh) mesh.SyncParametricObjectState();
        onChanged();
        return true;
    }

    protected static MeshInstance FindPlacementMesh(Node root, ObjectState state) {
        if (root == null || !GodotObject.IsInstanceValid(root) || root.IsQueuedForDeletion()) return null;
        if (root is MeshInstance mesh && mesh.settings == state) return mesh;
        foreach (var child in root.GetChildren()) {
            var result = FindPlacementMesh(child, state);
            if (result != null) return result;
        }
        return null;
    }

    public abstract void ShowOnMap(List<RaycastHit?> hits);
    public abstract void Apply(List<RaycastHit?> hits);
    public virtual bool Raycast(Node3D parent, Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, int mask) {
        return GameEngine.Raycast.DoRaycast(parent, origin, direction, out hit, distance, mask);
    }
    public virtual List<int> GetLayerMasks() {
        return new List<int> { ~(1 << 7) };
    }
    //the index (in GetLayerMasks) of the hit on the ground, where free points are placed (-1: the action doesn't place
    //points); with the terrain hidden it falls back to the zero height plane
    public virtual int GroundHitIndex => -1;
    //whether the next point can be placed above the ground (PlacementHeight): false while the action places nothing, or
    //when what it places is always put on the ground (by its placement rules)
    public virtual bool SupportsPlacementHeight => false;
    //whether the hits follow the grid snapping (Edit->Snapping): false for the actions picking what was clicked, whose
    //hit would otherwise move to the nearest whole coordinates (onto another element, e.g. the road under a decal)
    public virtual bool SnapsToGrid => true;
}

using System.Collections.Generic;
using Godot;

// Level of detail group. Godot's visibility ranges are not a good fit: they are measured from the center
// of each instance's own AABB (so the LODs of one object would switch at different distances) and ignore the camera
// FOV, and the visibility range end is already used for the per-layer cull distances (see GameEngine.Camera).
// The LOD is chosen from the screen relative height of the group's bounds, and the renderers of the
// other LODs are hidden.
// Meshes are cloned with Node.Duplicate (see MeshManager), so the configuration is kept in exported properties and
// in the metadata of the renderers, which are both copied, instead of in references to the nodes.
// There can be thousands of groups, so they are updated by the LODUpdater instead of their own _Process, with their
// world bounds cached until they move.
public partial class LODGroup : Node3D {
    const string LodMaskMeta = "lod_mask";
    const float LodBias = 1.0f; //bias applied to the screen relative height

    [Export] public float[] Transitions = System.Array.Empty<float>(); //screen relative transition heights, one per LOD
    [Export] public Vector3 LocalReferencePoint; //center of the LOD renderers' bounds, in local space
    [Export] public float Size = 1; //largest dimension of the LOD renderers' bounds, in local space

    List<(GeometryInstance3D renderer, long mask)> renderers;
    int currentLod = int.MinValue;

    //Picking is independent of the camera's current LOD, including for nested groups.
    public static bool IsHighestDetailRenderer(GeometryInstance3D renderer) {
        return !renderer.HasMeta(LodMaskMeta) || (renderer.GetMeta(LodMaskMeta).AsInt64() & 1L) != 0;
    }

    public void SetLODs(float[] transitions, List<GeometryInstance3D>[] lodRenderers) {
        Transitions = transitions;
        var masks = new Dictionary<GeometryInstance3D, long>();
        for (int i = 0; i < lodRenderers.Length && i < 64; i++) {
            foreach (var r in lodRenderers[i]) {
                masks.TryGetValue(r, out var mask);
                masks[r] = mask | (1L << i);
            }
        }
        foreach (var (r, mask) in masks) r.SetMeta(LodMaskMeta, mask);
        RecalculateBounds(masks.Keys);
        renderers = null;
        currentLod = int.MinValue;
        ApplyLod(0); //until the camera is known
        Dirty = true;
    }

    void RecalculateBounds(IEnumerable<GeometryInstance3D> lodRenderers) {
        Aabb? bounds = null;
        foreach (var r in lodRenderers) {
            var aabb = GetTransformRelativeToThis(r) * r.GetAabb();
            bounds = bounds.HasValue ? bounds.Value.Merge(aabb) : aabb;
        }
        if (!bounds.HasValue) return;
        LocalReferencePoint = bounds.Value.GetCenter();
        var s = bounds.Value.Size;
        Size = Mathf.Max(s.X, Mathf.Max(s.Y, s.Z));
    }

    Transform3D GetTransformRelativeToThis(Node node) {
        //the group is usually not in the scene tree yet, so the global transforms can't be used
        var res = Transform3D.Identity;
        while (node != null && node != this) {
            if (node is Node3D n) res = n.Transform * res;
            node = node.GetParent();
        }
        return res;
    }

    void CollectRenderers(Node node) {
        foreach (var child in node.GetChildren()) {
            if (child is LODGroup) continue; //child objects have their own LODs
            if (child is GeometryInstance3D r && r.HasMeta(LodMaskMeta)) {
                renderers.Add((r, r.GetMeta(LodMaskMeta).AsInt64()));
            }
            CollectRenderers(child);
        }
    }

    void ApplyLod(int lod) {
        if (lod == currentLod) return;
        currentLod = lod;
        if (renderers == null) {
            renderers = new List<(GeometryInstance3D renderer, long mask)>();
            CollectRenderers(this);
        }
        var lodMask = lod >= 0 && lod < 64 ? 1L << lod : 0;
        foreach (var (r, mask) in renderers) {
            if (IsInstanceValid(r)) r.Visible = (mask & lodMask) != 0;
        }
    }

    //called by the LODUpdater

    public int UpdaterIndex { get; set; } = -1;
    //set when the group moved, changed visibility or was reconfigured: the cached values below must be read again
    public bool Dirty { get; private set; } = true;
    bool visibleInTree;
    Vector3 worldReferencePoint;
    float worldSize;

    public override void _EnterTree() {
        SetNotifyTransform(true);
        Dirty = true;
        LODUpdater.Register(this);
    }

    public override void _ExitTree() {
        LODUpdater.Unregister(this);
    }

    public override void _Notification(int what) {
        if (what == NotificationTransformChanged || what == NotificationVisibilityChanged) Dirty = true;
    }

    public void RefreshCache() {
        Dirty = false;
        visibleInTree = IsVisibleInTree();
        var transform = GlobalTransform;
        var scale = transform.Basis.Scale.Abs();
        worldSize = Size * Mathf.Max(scale.X, Mathf.Max(scale.Y, scale.Z));
        worldReferencePoint = transform * LocalReferencePoint;
    }

    float GetRelativeHeight(in LODUpdater.CameraData cam) {
        float res;
        if (cam.orthogonal) {
            res = cam.orthoHeight > 0 ? worldSize / cam.orthoHeight : float.PositiveInfinity;
        } else {
            var distance = cam.position.DistanceTo(worldReferencePoint);
            res = distance > 0 ? worldSize * 0.5f / (distance * cam.halfAngleTan) : float.PositiveInfinity;
        }
        return res * LodBias;
    }

    public void UpdateLod(in LODUpdater.CameraData cam) {
        if (Transitions.Length == 0 || !visibleInTree) return;
        var relativeHeight = GetRelativeHeight(cam);
        var lod = -1; //culled if smaller than the last transition
        for (int i = 0; i < Transitions.Length; i++) {
            if (relativeHeight >= Transitions[i]) {
                lod = i;
                break;
            }
        }
        ApplyLod(lod);
    }
}

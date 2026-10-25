using System.Collections.Generic;
using Godot;

//Updates the LOD groups in place of their own _Process: there can be thousands of them, and a script callback per node
//per frame is costly in Godot. The LODs are chosen again only when the camera changed, or for the groups that moved.
public partial class LODUpdater : Node {
    public struct CameraData {
        public bool orthogonal;
        public float orthoHeight; //full visible height, for the orthogonal projection
        public Vector3 position;
        public float halfAngleTan; //tangent of half of the vertical FOV, for the perspective projection
    }

    static LODUpdater instance = null;
    static readonly List<LODGroup> groups = new List<LODGroup>();

    Camera3D lastCam = null;
    CameraData lastCamData;
    //the camera the LODs are chosen for instead of the one of the viewport (the offscreen renders), if any
    static CameraData? overrideData = null;

    public static void SetOverride(CameraData? data) {
        overrideData = data;
    }

    public static void Register(LODGroup group) {
        if (group.UpdaterIndex >= 0) return;
        group.UpdaterIndex = groups.Count;
        groups.Add(group);
        if (!IsInstanceValid(instance)) {
            instance = new LODUpdater { Name = "LODUpdater", ProcessPriority = int.MaxValue };
            //the tree can be busy adding the registering node
            group.GetTree().Root.CallDeferred(Node.MethodName.AddChild, instance);
        }
    }

    //swap with the last one, there can be thousands of removals at once
    public static void Unregister(LODGroup group) {
        var index = group.UpdaterIndex;
        if (index < 0) return;
        var last = groups[groups.Count - 1];
        groups[index] = last;
        last.UpdaterIndex = index;
        groups.RemoveAt(groups.Count - 1);
        group.UpdaterIndex = -1;
    }

    CameraData GetCameraData(Camera3D cam) {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var aspect = viewportSize.Y > 0 ? viewportSize.X / viewportSize.Y : 1;
        var keepWidth = cam.KeepAspect == Camera3D.KeepAspectEnum.Width;
        var res = new CameraData { orthogonal = cam.Projection == Camera3D.ProjectionType.Orthogonal };
        if (res.orthogonal) {
            //Godot's size is the full height (or width), the orthographic size is half of the height
            res.orthoHeight = keepWidth ? cam.Size / aspect : cam.Size;
        } else {
            res.position = cam.GlobalPosition;
            res.halfAngleTan = Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
            if (keepWidth) res.halfAngleTan /= aspect; //the FOV is always vertical
        }
        return res;
    }

    public override void _Process(double delta) {
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        var camData = overrideData ?? GetCameraData(cam);
        var camChanged = cam != lastCam || !camData.Equals(lastCamData);
        lastCam = cam;
        lastCamData = camData;
        for (int i = 0; i < groups.Count; i++) {
            var group = groups[i];
            var dirty = group.Dirty;
            if (dirty) group.RefreshCache();
            if (dirty || camChanged) group.UpdateLod(camData);
        }
    }
}

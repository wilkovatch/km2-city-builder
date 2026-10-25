using System.Collections.Generic;
using Godot;

namespace Rendering {
    //Camera placement shared by the offscreen renders and the guide screenshots. Everything is in Godot coordinates.
    public static class CameraFraming {
        //looks at the point from the given distance, with the given pitch (degrees below the horizon) and yaw
        public static void LookAt(Camera3D cam, Vector3 target, float distance, float pitch = 50, float yaw = 30) {
            cam.GlobalPosition = target + Direction(pitch, yaw) * distance;
            cam.LookAt(target, Mathf.Abs(pitch) > 89.9f ? Vector3.Forward : Vector3.Up);
        }

        //the direction from the target to the camera
        public static Vector3 Direction(float pitch, float yaw) {
            var p = Mathf.DegToRad(pitch);
            var y = Mathf.DegToRad(yaw);
            return new Vector3(Mathf.Cos(p) * Mathf.Sin(y), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Cos(y));
        }

        //the distance at which an area of the given size fills the view (visibleWidth: the part of the width used)
        public static float FitDistance(Camera3D cam, Vector2 viewSize, float size, float margin = 1.2f, float visibleWidth = 1.0f) {
            var halfHeight = Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
            var halfWidth = halfHeight * viewSize.X / viewSize.Y;
            return size * margin * 0.5f / Mathf.Min(halfHeight, halfWidth * visibleWidth);
        }

        //the bounds of the visible meshes of a node (line renderers left out), null if it has none
        public static Aabb? NodeAabb(Node node) {
            Aabb? res = null;
            var nodes = new List<Node>(node.FindChildren("*", nameof(MeshInstance3D), true, false));
            if (node is MeshInstance3D) nodes.Add(node);
            foreach (var child in nodes) {
                var mi = (MeshInstance3D)child;
                if (mi is GameEngine.LineRenderer || !mi.IsVisibleInTree() || mi.Mesh == null) continue;
                var aabb = mi.GlobalTransform * mi.GetAabb();
                res = res == null ? aabb : res.Value.Merge(aabb);
            }
            return res;
        }

        //the center and the horizontal size of the visible meshes of a node
        public static (Vector3 center, float size) NodeBounds(Node node) {
            var aabb = NodeAabb(node);
            if (aabb == null) return (Vector3.Zero, 0);
            return (aabb.Value.GetCenter(), Mathf.Max(aabb.Value.Size.X, aabb.Value.Size.Z));
        }

        public static (Vector3 center, float size) Bounds(IEnumerable<Vector3> points) {
            var first = true;
            var aabb = new Aabb();
            foreach (var point in points) {
                if (first) aabb = new Aabb(point, Vector3.Zero); else aabb = aabb.Expand(point);
                first = false;
            }
            return (aabb.GetCenter(), Mathf.Max(aabb.Size.X, aabb.Size.Z));
        }
    }
}

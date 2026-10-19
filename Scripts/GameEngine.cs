
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using GDExtension.Wrappers;

public static partial class GameEngine {
    public static void Print(string message) {
        //the standard output of a command line session only has the output of its command
        if (Cli.CommandLine.IsCliSession) Cli.CliOutput.Error.WriteLine(message);
        else GD.Print(message);
    }

    public static void LogWarning(string message) {
        GD.PushWarning(message);
    }

    public static void LogError(string message) {
        GD.PushError(message);
    }

    public static float GetTime() {
        return Time.GetTicksUsec() / 1000000.0f;
    }

    public static string GetDataPath() {
        return ProjectSettings.GlobalizePath("res://"); //todo: check
    }

    public static bool IsEditor() {
        return Engine.IsEditorHint();
    }

    public static class Rect {
        public static bool Contains(Godot.Rect2 rect, Godot.Vector2 vec) {
            return rect.HasPoint(vec);
        }

        public static float GetWidth(Godot.Rect2 rect) {
            return rect.Size.X;
        }

        public static float GetHeight(Godot.Rect2 rect) {
            return rect.Size.Y;
        }
    }

    public static class Math {
        public static float pi = (float)System.Math.PI;
        public static float deg2rad = pi / 180f;
        public static float rad2deg = 180f / pi;

        public static float Lerp(float a, float b, float t) { //clamped
            return Mathf.Lerp(a, b, Mathf.Clamp(t, 0f, 1f));
        }

        public static float LerpUnclamped(float a, float b, float t) {
            return Mathf.Lerp(a, b, t);
        }

        public static float Sign(float f) {
            return f >= 0f ? 1f : -1f; //1 for zero (Godot's Mathf.Sign returns 0)
        }
    }

    public static class Vector2 {
        public static Godot.Vector2 zero = Godot.Vector2.Zero;
        public static Godot.Vector2 up = Godot.Vector2.Up;
        public static Godot.Vector2 down = Godot.Vector2.Down;
        public static Godot.Vector2 left = Godot.Vector2.Left;
        public static Godot.Vector2 right = Godot.Vector2.Right;

        public static Godot.Vector2 DecodePosition(Godot.Vector3 r) {
            return new Godot.Vector2(r.X, r.Y);
        }

        public static Godot.Vector2 DecodeScale(Godot.Vector3 r) {
            return new Godot.Vector2(r.X, r.Y);
        }

        public static Godot.Vector2 EncodePosition(Godot.Vector3 r) {
            return new Godot.Vector2(r.X, r.Y);
        }

        public static Godot.Vector2 EncodeScale(Godot.Vector3 r) {
            return new Godot.Vector2(r.X, r.Y);
        }
        public static float Dot(Godot.Vector2 a, Godot.Vector2 b) {
            return a.Dot(b);
        }

        public static float Angle(Godot.Vector2 a, Godot.Vector2 b) {
            //unsigned and in degrees (Godot's AngleTo is signed)
            float denominator = Mathf.Sqrt(a.LengthSquared() * b.LengthSquared());
            if (denominator < 1e-15f) return 0.0f;
            float dot = Mathf.Clamp(a.Dot(b) / denominator, -1.0f, 1.0f);
            return Mathf.RadToDeg(Mathf.Acos(dot));
        }

        public static float SignedAngle(Godot.Vector2 a, Godot.Vector2 b) {
            return Angle(a, b) * Math.Sign(a.Cross(b));
        }

        public static float Distance(Godot.Vector2 a, Godot.Vector2 b) {
            return a.DistanceTo(b);
        }

        public static Godot.Vector2 Lerp(Godot.Vector2 a, Godot.Vector2 b, float v) {
            return a.Lerp(b, Mathf.Clamp(v, 0f, 1f));
        }

        public static Godot.Vector2 Scale(Godot.Vector2 a, Godot.Vector2 b) {
            return a * b;
        }

        public static Godot.Vector2 Min(Godot.Vector2 a, Godot.Vector2 b) {
            return a.Min(b);
        }

        public static Godot.Vector2 Max(Godot.Vector2 a, Godot.Vector2 b) {
            return a.Max(b);
        }

        public static Godot.Vector2 Reflect(Godot.Vector2 a, Godot.Vector2 b) {
            //Godot's Reflect returns the opposite vector and requires a normalized b
            return a - 2f * a.Dot(b) * b;
        }

        public static float Magnitude(Godot.Vector2 a) {
            return a.Length();
        }

        public static float SqrMagnitude(Godot.Vector2 a) {
            return a.LengthSquared();
        }
    }
    public static class Vector3 {
        public static Godot.Vector3 zero = Godot.Vector3.Zero;

        public static Godot.Vector3 one = Godot.Vector3.One;
        public static Godot.Vector3 up = Godot.Vector3.Up;
        public static Godot.Vector3 down = Godot.Vector3.Down;
        public static Godot.Vector3 left = Godot.Vector3.Left;
        public static Godot.Vector3 right = Godot.Vector3.Right;
        // Shared code works in the program's left-handed coordinates (see DecodePosition), where forward is +Z (Godot's Forward is -Z).
        public static Godot.Vector3 forward = new Godot.Vector3(0, 0, 1);
        public static Godot.Vector3 back = new Godot.Vector3(0, 0, -1);

        public static Godot.Vector3 DecodePosition(Godot.Vector3 r) {
            return new Godot.Vector3(r.X, r.Y, -r.Z);
        }

        public static Godot.Vector3 DecodeRotation(Godot.Vector3 r) {
            // Mirroring Z negates the rotations around X and Y (same as Quaternion.Decode/Encode), both engines apply Z, X, Y.
            return new Godot.Vector3(-r.X, -r.Y, r.Z);
        }

        public static Godot.Vector3 DecodeScale(Godot.Vector3 r) {
            return new Godot.Vector3(r.X, r.Y, r.Z);
        }

        public static Godot.Vector3 EncodePosition(Godot.Vector3 r) {
            return new Godot.Vector3(r.X, r.Y, -r.Z);
        }

        public static Godot.Vector3 EncodeRotation(Godot.Vector3 r) {
            // Mirroring Z negates the rotations around X and Y (same as Quaternion.Decode/Encode), both engines apply Z, X, Y.
            return new Godot.Vector3(-r.X, -r.Y, r.Z);
        }

        public static Godot.Vector3 EncodeScale(Godot.Vector3 r) {
            return new Godot.Vector3(r.X, r.Y, r.Z);
        }

        public static float Dot(Godot.Vector3 a, Godot.Vector3 b) {
            return a.Dot(b);
        }

        public static Godot.Vector3 Cross(Godot.Vector3 a, Godot.Vector3 b) {
            return a.Cross(b);
        }

        public static float Angle(Godot.Vector3 a, Godot.Vector3 b) {
            // Clamp the normalized dot product before
            // acos so floating-point error cannot produce an invalid angle.
            float denominator = Mathf.Sqrt(a.LengthSquared() * b.LengthSquared());
            if (denominator < 1e-15f) return 0.0f;
            float dot = Mathf.Clamp(a.Dot(b) / denominator, -1.0f, 1.0f);
            return Mathf.RadToDeg(Mathf.Acos(dot));
        }

        public static float SignedAngle(Godot.Vector3 a, Godot.Vector3 b, Godot.Vector3 axis) {
            //clamped acos and a positive sign for zero (opposite vectors give 180, not 0 or NaN)
            return Angle(a, b) * Math.Sign(axis.Dot(a.Cross(b)));
        }

        public static float Distance(Godot.Vector3 a, Godot.Vector3 b) {
            return a.DistanceTo(b);
        }

        public static float Magnitude(Godot.Vector3 a) {
            return a.Length();
        }

        public static float SqrMagnitude(Godot.Vector3 a) {
            return a.LengthSquared();
        }

        public static Godot.Vector3 Lerp(Godot.Vector3 a, Godot.Vector3 b, float v) {
            return a.Lerp(b, Mathf.Clamp(v, 0f, 1f));
        }

        public static Godot.Vector3 Scale(Godot.Vector3 a, Godot.Vector3 b) {
            return a * b;
        }

        public static Godot.Vector3 Min(Godot.Vector3 a, Godot.Vector3 b) {
            return a.Min(b);
        }

        public static Godot.Vector3 Max(Godot.Vector3 a, Godot.Vector3 b) {
            return a.Max(b);
        }

        public static Godot.Vector3 Reflect(Godot.Vector3 a, Godot.Vector3 b) {
            //Godot's Reflect returns the opposite vector and requires a normalized b
            return a - 2f * a.Dot(b) * b;
        }

        public static Godot.Vector3 Project(Godot.Vector3 a, Godot.Vector3 b) {
            //zero when b is (almost) zero instead of NaN
            float sqrMag = b.LengthSquared();
            if (sqrMag < float.Epsilon) return Godot.Vector3.Zero;
            return b * (a.Dot(b) / sqrMag);
        }

        public static Godot.Vector3 Normalize(Godot.Vector3 a) {
            //zero for (almost) zero vectors
            float mag = a.Length();
            return mag > 1e-5f ? a / mag : Godot.Vector3.Zero;
        }
    }

    public static class Plane {
        // Godot's three point constructor yields the opposite of the expected normal ((b - a) x (c - a)),
        // so the points are swapped to keep that orientation (and therefore the sign of distances).
        public static Godot.Plane Create(Godot.Vector3 a, Godot.Vector3 b, Godot.Vector3 c) {
            return new Godot.Plane(a, c, b);
        }

        // The normal is normalized (otherwise distances get scaled by its length).
        public static Godot.Plane Create(Godot.Vector3 normal, Godot.Vector3 point) {
            return new Godot.Plane(normal.Normalized(), point);
        }

        public static float DistanceToPoint(Godot.Plane plane, Godot.Vector3 point) {
            return plane.DistanceTo(point);
        }

        public static Godot.Vector3 Normal(Godot.Plane plane) {
            return plane.Normal;
        }

        public static bool GetSide(Godot.Plane plane, Godot.Vector3 point) {
            return plane.DistanceTo(point) > 0f; //(Godot's plane equation is n.p = D)
        }

        public static bool SameSide(Godot.Plane plane, Godot.Vector3 a, Godot.Vector3 b) {
            return GetSide(plane, a) == GetSide(plane, b);
        }
    }

    public static class Quaternion {
        public static Godot.Quaternion Decode(Godot.Quaternion q) => new Godot.Quaternion(-q.X, -q.Y, q.Z, q.W).Normalized();
        public static Godot.Quaternion Encode(Godot.Quaternion q) => new Godot.Quaternion(-q.X, -q.Y, q.Z, q.W).Normalized();
        public static Godot.Quaternion AngleAxis(float angle, Godot.Vector3 axis) {
            return new Godot.Quaternion(axis.Normalized(), Mathf.DegToRad(angle));
        }

        public static Godot.Quaternion Euler(float x, float y, float z) {
            return Godot.Quaternion.FromEuler(new Godot.Vector3(Mathf.DegToRad(x), Mathf.DegToRad(y), Mathf.DegToRad(z)));
        }
    }

    public static class GameObject {
        public static Node3D GetObject(Node3D obj) {
            return obj;
        }
        public static Node3D GetParent(Node3D obj) {
            return (Node3D)obj.GetParent();
        }
        // Godot node names can't contain characters such as '.', ':' or '/' and must be unique among
        // siblings, so the object name is stored as metadata; the node name only mirrors it (sanitized)
        // for readability in the remote scene tree and is never read back.
        const string NameMeta = "object_name";

        public static string GetName(Node3D obj) {
            return obj.HasMeta(NameMeta) ? obj.GetMeta(NameMeta).AsString() : obj.Name.ToString();
        }

        public static void SetName(Node3D obj, string name) {
            name ??= "";
            obj.SetMeta(NameMeta, name);
            var nodeName = name.ValidateNodeName();
            if (nodeName != "") obj.Name = nodeName;
        }

        // Godot only provides global transforms inside the scene tree, while the shared code expects
        // one; freshly created nodes (e.g. handles positioned before being parented) need the fallback.
        static Transform3D GetGlobalTransform(Node3D obj) {
            if (obj.IsInsideTree()) return obj.GlobalTransform;
            var transform = obj.Transform;
            for (var parent = obj.GetParent() as Node3D; parent != null; parent = parent.GetParent() as Node3D) {
                transform = parent.Transform * transform;
            }
            return transform;
        }

        public static Godot.Vector3 GetPosition(Node3D obj) {
            if (!obj.IsInsideTree()) return Vector3.EncodePosition(GetGlobalTransform(obj).Origin);
            return Vector3.EncodePosition(obj.GlobalPosition); //position in program coordinates
        }

        public static void SetPosition(Node3D obj, Godot.Vector3 position) {
            if (!obj.IsInsideTree()) {
                var parent = obj.GetParent() as Node3D;
                var parentTransform = parent != null ? GetGlobalTransform(parent) : Transform3D.Identity;
                obj.Position = parentTransform.AffineInverse() * Vector3.DecodePosition(position);
                return;
            }
            obj.GlobalPosition = Vector3.DecodePosition(position);
        }

        //the program's euler angles are in the 0-360 range, godot's in the -180-180 one
        static float WrapAngle360(float a) {
            a %= 360f;
            if (a < 0f) a += 360f;
            if (a >= 360f) a -= 360f;
            return a;
        }

        static Godot.Vector3 WrapAngles360(Godot.Vector3 r) {
            return new Godot.Vector3(WrapAngle360(r.X), WrapAngle360(r.Y), WrapAngle360(r.Z));
        }

        public static Godot.Vector3 GetRotation(Node3D obj) {
            return WrapAngles360(Vector3.EncodeRotation(obj.GlobalRotationDegrees)); //Euler rotation in program coordinates
        }

        public static Godot.Quaternion GetRotationQuaternion(Node3D obj) {
            return Quaternion.Encode(obj.GlobalTransform.Basis.GetRotationQuaternion());
        }

        public static void SetRotation(Node3D obj, Godot.Vector3 rotation) {
            obj.GlobalRotationDegrees = Vector3.DecodeRotation(rotation);
        }

        public static void SetRotation(Node3D obj, Godot.Quaternion rotation) {
            var transform = obj.GlobalTransform;
            //the scale must be applied in local space (R * S), Basis.Scaled would apply it along the global axes
            transform.Basis = new Godot.Basis(Quaternion.Decode(rotation)) * Godot.Basis.FromScale(obj.GlobalTransform.Basis.Scale);
            obj.GlobalTransform = transform;
        }

        public static Godot.Vector3 GetLocalRotation(Node3D obj) {
            return WrapAngles360(Vector3.EncodeRotation(obj.RotationDegrees)); //local Euler rotation in program coordinates
        }

        public static void SetLocalRotation(Node3D obj, Godot.Vector3 rotation) {
            obj.RotationDegrees = Vector3.DecodeRotation(rotation);
        }

        public static void SetLocalRotation(Node3D obj, Godot.Quaternion rotation) {
            obj.Quaternion = Quaternion.Decode(rotation);
        }

        public static Godot.Vector3 GetLocalPosition(Node3D obj) {
            return Vector3.EncodePosition(obj.Position);
        }

        public static void SetLocalPosition(Node3D obj, Godot.Vector3 position) {
            obj.Position = Vector3.DecodePosition(position);
        }

        public static Godot.Vector3 GetLocalScale(Node3D obj) {
            return obj.Scale; //TODO: check Z axis effect
        }

        public static void SetLocalScale(Node3D obj, Godot.Vector3 scale) {
            obj.Scale = scale; //TODO: check Z axis effect
        }

        public static Godot.Vector3 GetGlobalScale(Node3D obj) {
            return obj.GlobalTransform.Basis.Scale; //TODO: check Z axis effect
        }

        public static void SetParent(Node3D obj, Node3D newParent) {
            if (obj == null || newParent == null || obj == newParent) return;
            var oldParent = obj.GetParent();
            if (oldParent == newParent) return;

            // Newly created nodes have no parent and cannot provide a global transform yet.
            // Preserve world space only when both nodes already belong to the scene tree.
            var preserveGlobalTransform = oldParent != null && obj.IsInsideTree() && newParent.IsInsideTree();
            var transform = preserveGlobalTransform ? obj.GlobalTransform : obj.Transform;
            oldParent?.RemoveChild(obj);
            newParent.AddChild(obj);
            if (preserveGlobalTransform) obj.GlobalTransform = transform;
            else obj.Transform = transform;
        }

        public static Node3D Create(string name = null) {
            var res = new Node3D();
            if (name != null) SetName(res, name);
            return res;
        }

        public static StaticBody3D CreateStaticBody(string name = null) {
            var res = new StaticBody3D();
            if (name != null) SetName(res, name);
            return res;
        }

        public static LODGroup CreateLODMesh(string name = null) {
            var res = new LODGroup();
            if (!string.IsNullOrEmpty(name)) SetName(res, name);
            return res;
        }

        public static void ConfigureLODMesh(LODGroup res, int numAllLods, int numLods, string[] lodNames, List<(string[] lodNames, Node3D mesh)> meshes, Dictionary<string, float> transitionValues) {
            var renderers = new Dictionary<string, List<GeometryInstance3D>>();
            for (int i = 0; i < numAllLods; i++) {
                renderers[lodNames[i]] = new List<GeometryInstance3D>();
            }
            for (int i = 0; i < numLods; i++) {
                var meshRenderers = Scriptable.FindMultipleRecursive<GeometryInstance3D>(meshes[i].mesh);
                foreach (var name in meshes[i].lodNames) {
                    renderers[name].AddRange(meshRenderers);
                }
            }
            var transitions = new float[numAllLods];
            var lodRenderers = new List<GeometryInstance3D>[numAllLods];
            for (int i = 0; i < numAllLods; i++) {
                transitions[i] = transitionValues[lodNames[i]];
                lodRenderers[i] = renderers[lodNames[i]];
            }
            res.SetLODs(transitions, lodRenderers);
        }

        public static void SetEnabled(Node3D obj, bool enabled) {
            obj.Visible = enabled;
            // Like an inactive object, a disabled node (and its inheriting children) must not
            // collide: hidden bodies would otherwise still intercept editor raycasts. Disabled
            // CollisionObject3Ds are removed from the physics space (DisableMode.Remove).
            // A disabled process mode also stops _Process, _PhysicsProcess and input. The callbacks are not switched on
            // explicitly: that would subscribe nodes without them (e.g. thousands of handles) to every physics tick and
            // input event, each one a call into the script.
            obj.ProcessMode = enabled ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled;
        }

        public static bool GetEnabled(Node3D obj) {
            return obj.Visible && obj.ProcessMode != Node.ProcessModeEnum.Disabled;
        }

        public static void SetEnabled(Control obj, bool enabled) {
            if (obj is CanvasItem canvasItem)
                canvasItem.Visible = enabled;

            obj.SetProcess(enabled);
            obj.SetPhysicsProcess(enabled);
            obj.SetProcessInput(enabled);
            obj.SetProcessUnhandledInput(enabled);
        }

        public static bool GetEnabled(Control obj) {
            return obj.IsProcessing();
        }

        // The C# wrapper of a freed node stays
        // stays non-null but disposed, and any engine call on it throws ObjectDisposedException.
        public static bool IsNull(object obj) {
            return obj == null || (obj is GodotObject godotObj && !GodotObject.IsInstanceValid(godotObj));
        }

        public static void Destroy(Node3D obj) {
            if (IsNull(obj)) return;
            if (obj.IsInsideTree()) obj.QueueFree();
            else obj.Free();
        }

        // Materials and textures are RefCounted resources in Godot: they can't be freed explicitly and are
        // released once the last reference is dropped, so the caller only has to forget them.
        public static void Destroy(Material mat) {
        }

        public static void Destroy(Texture2D tex) {
        }

        public static void SetLayer(StaticBody3D obj, int layer) {
            // The program uses layer indices; Godot stores a bitmask, so layer n is bit n
            // (layer 0, "Default", is Godot's default layer 1 and is still raycastable).
            obj.CollisionLayer = layer >= 0 && layer < 32 ? 1u << layer : 0u;
            // The direct GeometryInstance3D children stand for the renderers of the same GameObject
            // (e.g. the generated road mesh), which share its layer for the camera culling mask.
            foreach (var child in obj.GetChildren()) {
                if (child is GeometryInstance3D g) SetLayer(g, layer);
            }
        }

        public static int GetLayer(StaticBody3D obj) {
            uint layerMask = obj.CollisionLayer;
            if (layerMask == 0) return 0;
            return System.Numerics.BitOperations.TrailingZeroCount(layerMask);
        }

        // Renderers carry the layer of their GameObject, which the camera uses for its culling mask and
        // per-layer cull distances. Godot keeps render layers separately (only 20 of them), so they are mirrored
        // here and the node joins a per-layer group that Camera.SetLayerCullDistances updates.
        public static void SetLayer(GeometryInstance3D obj, int layer) {
            var mask = layer >= 0 && layer < 20 ? 1u << layer : 1u;
            //unchanged (it is called for thousands of handles at once): the cull distance is kept up to date for the group
            if (obj.Layers == mask && obj.IsInGroup(Camera.LayerGroupPrefix + layer)) return;
            obj.Layers = mask;
            foreach (var group in obj.GetGroups()) {
                if (group.ToString().StartsWith(Camera.LayerGroupPrefix)) obj.RemoveFromGroup(group);
            }
            obj.AddToGroup(Camera.LayerGroupPrefix + layer);
            obj.VisibilityRangeEnd = Camera.GetLayerCullDistance(layer);
        }

        public static int GetChildCount(Node3D obj) {
            return obj.GetChildCount();
        }

        public static Node3D GetChildI(Node3D obj, int i) {
            return obj.GetChild(i) as Node3D;
        }

        public static LineRenderer CreateLineRenderer(Node3D parent, string name = null) {
            //TODO complete with other stuff
            var res = new LineRenderer();
            if (name != null) SetName(res, name);
            SetParent(res, parent);
            return res;
        }

        public static Node3D Instantiate(Node3D obj, Node3D newParent) {
            var copy = obj.Duplicate((int)Node.DuplicateFlags.Scripts) as Node3D;
            if (copy != null && newParent != null) newParent.AddChild(copy);
            return copy;
        }

        // The point is in program coordinates (like GetPosition). The object aims its local +Z axis, which the
        // Z mirroring of the program coordinates turns into Godot's -Z, the axis aimed by LookingAt.
        public static void LookAt(Node3D obj, Godot.Vector3 point) {
            Transform3D transform = GetGlobalTransform(obj);
            var target = Vector3.DecodePosition(point);
            var dir = target - transform.Origin;
            if (dir.IsZeroApprox()) return; //keep the current rotation
            // LookingAt fails when looking along the up vector, another one is picked instead
            var up = Mathf.Abs(dir.Normalized().Dot(Godot.Vector3.Up)) > 0.9999f ? Godot.Vector3.Back : Godot.Vector3.Up;
            var scale = transform.Basis.Scale;
            transform = transform.LookingAt(target, up);
            transform.Basis = transform.Basis * Godot.Basis.FromScale(scale); //the scale is kept, LookingAt resets it
            if (obj.IsInsideTree()) {
                obj.GlobalTransform = transform;
            } else {
                var parent = obj.GetParent() as Node3D;
                obj.Transform = parent != null ? GetGlobalTransform(parent).AffineInverse() * transform : transform;
            }
        }
    }

    public static class Color {
        public static Godot.Color red = Godot.Color.Color8(255, 0, 0);
        public static Godot.Color green = Godot.Color.Color8(0, 255, 0);
        public static Godot.Color blue = Godot.Color.Color8(0, 0, 255);
        public static Godot.Color yellow = Godot.Color.Color8(255, 255, 0);
        public static Godot.Color cyan = Godot.Color.Color8(0, 255, 255);
        public static Godot.Color magenta = Godot.Color.Color8(255, 0, 255);
        public static Godot.Color white = Godot.Color.Color8(255, 255, 255);
        public static Godot.Color black = Godot.Color.Color8(0, 0, 0);
        public static Godot.Color gray = Godot.Color.Color8(127, 127, 127);
    }

    public static class Bounds {
        public static Aabb Create(Godot.Vector3 center, Godot.Vector3 size) {
            var position = center - size * 0.5f;
            return new Aabb(position, size);
        }

        public static bool Contains(Aabb bounds, Godot.Vector3 point) {
            return bounds.HasPoint(point);
        }

        public static Godot.Vector3 GetSize(Aabb bounds) {
            return bounds.Size;
        }
        public static Godot.Vector3 GetMin(Aabb bounds) {
            return bounds.Position;
        }

        public static Godot.Vector3 GetMax(Aabb bounds) {
            return bounds.End;
        }

        public static Godot.Vector3 GetCenter(Aabb bounds) {
            return bounds.GetCenter();
        }

        public static Godot.Vector3 GetExtents(Aabb bounds) {
            return bounds.Size / 2;
        }

        public static Aabb GetFromMesh(Godot.ArrayMesh mesh) {
            return mesh.GetAabb();
        }
    }

    public enum InputContentType {
        Standard,
        Decimal,
        Integer,
    }

    public static class Version {
        public static string GetVersion() {
            var assembly = typeof(Version).Assembly;
            var version = assembly.GetName().Version;
            var text = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "?";
            var hash = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .Cast<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "CommitHash")?.Value;
            return string.IsNullOrEmpty(hash) ? text : $"{text}-{hash}";
        }

        public static string GetEngineVersion() {
            var info = Engine.GetVersionInfo();
            return $"{info["major"]}.{info["minor"]}.{info["patch"]}-{info["status"]}";
        }
    }

    public static class Input { //todo: move elsewhere and customize
        public enum KeyCommand {
            Close,
            Delete,
            Edit,
            CreateRoad,
            CreateTerrainPatch,
            CreateBuildingLine,
            Modifier1,
            Modifier2,
            SaveCity,
            Undo,
            Redo,
            ZoomIn,
            ZoomOut,
            ZoomInAndPan,
            RotateCamera,
            ZoomIn2,
            ZoomOut2,
            ZoomInAndPan2,
            Down,
            Up,
            Select,
            ScrollUp,
            ScrollDown,
        }

        public static Dictionary<KeyCommand, (Key key, Key modifier)> keyMapping = new Dictionary<KeyCommand, (Key, Key)>() {
            { KeyCommand.Close, (Key.Escape, Key.None) },
            { KeyCommand.Delete, (Key.Delete, Key.None) },
            { KeyCommand.Edit, (Key.M, Key.None) },
            { KeyCommand.CreateRoad, (Key.R, Key.Ctrl) },
            { KeyCommand.CreateTerrainPatch, (Key.T, Key.Ctrl) },
            { KeyCommand.CreateBuildingLine, (Key.E, Key.Ctrl) },
            { KeyCommand.Modifier1, (Key.Shift, Key.None) },
            { KeyCommand.Modifier2, (Key.Ctrl, Key.None) },
            { KeyCommand.SaveCity, (Key.S, Key.Ctrl) },
            { KeyCommand.Undo, (Key.Z, Key.Ctrl) },
            { KeyCommand.Redo, (Key.Y, Key.Ctrl) },
            { KeyCommand.ZoomIn, (Key.Pageup, Key.None) },
            { KeyCommand.ZoomOut, (Key.Pagedown, Key.None) },
            { KeyCommand.ZoomInAndPan, (Key.Home, Key.None) },
            { KeyCommand.Down, (Key.Down, Key.None) },
            { KeyCommand.Up, (Key.Up, Key.None) },
        };

        //note: must not overlap with the above
        public static Dictionary<KeyCommand, MouseButton> mouseMapping = new Dictionary<KeyCommand, MouseButton>() {
            { KeyCommand.RotateCamera, MouseButton.Right},
            { KeyCommand.ZoomIn2, MouseButton.WheelUp },
            { KeyCommand.ZoomOut2, MouseButton.WheelDown },
            { KeyCommand.ZoomInAndPan2, MouseButton.Middle },
            { KeyCommand.Select, MouseButton.Left },
            { KeyCommand.ScrollUp, MouseButton.WheelUp },
            { KeyCommand.ScrollDown, MouseButton.WheelDown },
        };

        public static void ReloadKeys() {
            foreach (var entry in keyMapping) {
                ActionEraseEvents(entry.Key.ToString());
                ActionEraseEvents(entry.Key.ToString() + "_m");
            }
            foreach (var entry in mouseMapping) {
                ActionEraseEvents(entry.Key.ToString());
            }
            foreach (var entry in keyMapping) {
                (Key key, Key modifier) = entry.Value;
                var keyEvent = new InputEventKey {
                    PhysicalKeycode = key
                };
                ActionAddEvent(entry.Key.ToString(), keyEvent);
                if (modifier != Key.None) {
                    var keyEvent2 = new InputEventKey {
                        PhysicalKeycode = modifier
                    };
                    ActionAddEvent(entry.Key.ToString() + "_m", keyEvent2);
                }
            }
            foreach (var entry in mouseMapping) {
                var mouseEvent = new InputEventMouseButton {
                    ButtonIndex = entry.Value
                };
                ActionAddEvent(entry.Key.ToString(), mouseEvent);
            }
        }

        //proxy method to manage adding to input map
        static void ActionAddEvent(string action, InputEvent ev) {
            if (!InputMap.HasAction(action)) {
                InputMap.AddAction(action);
            }
            InputMap.ActionAddEvent(action, ev);
        }

        //proxy method to manage adding to input map
        static void ActionEraseEvents(string action) {
            if (!InputMap.HasAction(action)) {
                InputMap.AddAction(action);
            }
            InputMap.ActionEraseEvents(action);
        }

        //when enabled, a left button press with Alt held acts as the middle button until the left button is released
        public static bool emulateMiddleButton = false;
        static bool emulatedMiddleDown = false;
        static ulong emulatedPressFrame = ulong.MaxValue;
        static ulong emulatedReleaseFrame = ulong.MaxValue;

        //world: the press was not consumed by the GUI (releases are always tracked, so the latch is cleared over the UI too)
        public static void TrackMouseEvent(InputEventMouseButton e, bool world) {
            if (e.ButtonIndex != MouseButton.Left) return;
            if (e.Pressed) {
                if (world && emulateMiddleButton && Godot.Input.IsKeyPressed(Key.Alt)) {
                    emulatedMiddleDown = true;
                    emulatedPressFrame = Engine.GetProcessFrames();
                }
            }
            else if (emulatedMiddleDown) {
                emulatedMiddleDown = false;
                emulatedReleaseFrame = Engine.GetProcessFrames();
            }
        }

        //the middle button, physical or emulated
        public static bool MiddlePressed() {
            return Godot.Input.IsActionPressed(KeyCommand.ZoomInAndPan2.ToString()) || emulatedMiddleDown;
        }

        public static bool MiddleJustPressed() {
            return Godot.Input.IsActionJustPressed(KeyCommand.ZoomInAndPan2.ToString())
                || (emulatedMiddleDown && emulatedPressFrame == Engine.GetProcessFrames());
        }

        public static bool MiddleJustReleased() {
            return Godot.Input.IsActionJustReleased(KeyCommand.ZoomInAndPan2.ToString())
                || (!emulatedMiddleDown && emulatedReleaseFrame == Engine.GetProcessFrames());
        }

        public static bool GetInputStart(KeyCommand command) {
            if (command == KeyCommand.Select && emulatedMiddleDown) return false;
            if (mouseMapping.ContainsKey(command)) {
                return Godot.Input.IsActionJustPressed(command.ToString());
            }
            else if (keyMapping.ContainsKey(command)) {
                var (key, modifier) = keyMapping[command];
                return Godot.Input.IsActionJustPressed(command.ToString())
                    && (modifier == Key.None || Godot.Input.IsActionPressed(command.ToString() + "_m"));
            }
            else {
                Print("Warning: unhandled input" + command.ToString());
                return false;
            }
        }

        public static bool GetInputPressed(KeyCommand command) {
            if (command == KeyCommand.Select && emulatedMiddleDown) return false;
            if (mouseMapping.ContainsKey(command)) {
                return Godot.Input.IsActionPressed(command.ToString());
            }
            else if (keyMapping.ContainsKey(command)) {
                var (key, modifier) = keyMapping[command];
                return Godot.Input.IsActionPressed(command.ToString())
                    && (modifier == Key.None || Godot.Input.IsActionPressed(command.ToString() + "_m"));
            }
            else {
                Print("Warning: unhandled input" + command.ToString());
                return false;
            }
        }

    }

    public static class Camera {
        public static Camera3D Get(Node3D obj) {
            return obj.GetViewport().GetCamera3D();
        }

        public static Camera3D Get(Control obj) {
            return obj.GetViewport().GetCamera3D();
        }

        public const string LayerGroupPrefix = "layer_";
        // Render layer 19, unused by the program's layers, for editor overlays (e.g. the transform gizmo) that
        // are drawn outside the culling mask, so that they stay visible with any camera cull mask.
        // The sun is on it too, since lights outside the camera cull mask are culled as well.
        public const uint OverlayLayer = 1u << 18;
        static readonly float[] layerCullDistances = new float[32];

        public static float GetLayerCullDistance(int layer) {
            return layer >= 0 && layer < layerCullDistances.Length ? layerCullDistances[layer] : 0;
        }

        // Godot has no per-layer cull distances, so they are applied as the visibility range of the
        // geometry on each layer (see GameObject.SetLayer). 0 means the far clip plane.
        public static void SetLayerCullDistances(Camera3D cam, float[] distances) {
            for (int i = 0; i < layerCullDistances.Length; i++) {
                var d = i < distances.Length ? distances[i] : 0;
                layerCullDistances[i] = d > 0 && d < cam.Far ? d : 0;
            }
            if (!cam.IsInsideTree()) return;
            for (int i = 0; i < layerCullDistances.Length; i++) {
                foreach (var node in cam.GetTree().GetNodesInGroup(LayerGroupPrefix + i)) {
                    if (node is GeometryInstance3D g) g.VisibilityRangeEnd = layerCullDistances[i];
                }
            }
        }
    }

    public static class Lighting {
        static DirectionalLight3D GetSun(Node obj) {
            if (obj == null || !obj.IsInsideTree()) return null;
            var lights = obj.GetTree().Root.FindChildren("*", nameof(DirectionalLight3D), true, false);
            return lights.Count > 0 ? lights[0] as DirectionalLight3D : null;
        }

        public static void SetAmbientLight(Node obj, Godot.Color color) {
            var env = obj?.GetViewport()?.FindWorld3D()?.Environment;
            if (env == null) return;
            env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            env.AmbientLightColor = color;
        }

        public static void SetSunEnabled(Node obj, bool enabled) {
            var light = GetSun(obj);
            if (light != null) light.Visible = enabled;
        }

        public static void SetSunShadows(Node obj, bool enabled) {
            var light = GetSun(obj);
            if (light != null) light.ShadowEnabled = enabled;
        }
    }

    public static class Gizmo {
        public static void ClearTargets() {
            CityBuilderMenuBar.staticGizmo?.ClearTargets();
        }

        public static Node3D GetMainTargetRoot() {
            return CityBuilderMenuBar.staticGizmo?.mainTargetRoot;
        }

        public static bool IsNearAxis() {
            return CityBuilderMenuBar.staticGizmo?.isNearAxis ?? false;
        }

        public static void ConsumeClick() {
            CityBuilderMenuBar.staticGizmo?.ConsumeClick();
        }
    }

    public static class Scriptable {
        public static T Create<T>(string name = null, Node3D parent = null) where T : Node3D, new() {
            var obj = new T();
            if (name != null) GameObject.SetName(obj, name);
            if (parent != null) parent.AddChild(obj);
            return obj;
        }
        public static T Cast<T>(Node3D obj) where T : Node3D {
            return obj as T;
        }

        public static T Cast<T>(Control obj) where T : Control {
            return obj as T;
        }

        public static T FindRecursive<T>(Node3D obj) where T : Node3D {
            if (obj == null || !GodotObject.IsInstanceValid(obj)) return null;
            T res = Cast<T>(obj);
            if (res == null) {
                foreach (var child in obj.GetChildren()) {
                    if (child is Node3D child3d && GodotObject.IsInstanceValid(child3d)) {
                        var childT = FindRecursive<T>(child3d);
                        if (childT != null) {
                            return childT;
                        }
                    }
                }
            }
            return res;
        }

        public static T FindRecursive<T>(Control obj) where T : Control {
            if (obj == null || !GodotObject.IsInstanceValid(obj)) return null;
            T res = Cast<T>(obj);
            if (res == null) {
                foreach (var child in obj.GetChildren()) {
                    if (child is Control child3d && GodotObject.IsInstanceValid(child3d)) {
                        var childT = FindRecursive<T>(child3d);
                        if (childT != null) {
                            return childT;
                        }
                    }
                }
            }
            return res;
        }

        public static T[] FindMultipleRecursive<T>(Node3D obj) where T : Node3D {
            var res = new List<T>();
            if (obj is T match) res.Add(match);
            foreach (var child in obj.GetChildren()) {
                if (child is Node3D child3d) res.AddRange(FindMultipleRecursive<T>(child3d));
            }
            return res.ToArray();
        }

        public static T[] FindMultiple<T>(Node3D obj) where T : Node3D {
            var res = new List<T>();
            foreach (var child in obj.GetChildren()) {
                if (child is T match) res.Add(match);
            }
            return res.ToArray();
        }

        public static Godot.ArrayMesh FindMeshRecursive(Node3D obj) {
            var mesh = FindRecursive<MeshInstance3D>(obj);
            return (ArrayMesh)mesh.Mesh;
        }

        public static T FindInParent<T>(Node3D obj) where T : Node3D {
            return Cast<T>(obj.GetParentNode3D());
        }
    }

    public static class Mesh {
        sealed class MeshMaterialState {
            public MeshInstance3D Renderer;
            public Material[] Materials = System.Array.Empty<Material>();
        }
        static readonly ConditionalWeakTable<ArrayMesh, MeshMaterialState> meshMaterials = new();

        static void SetMeshMaterials(ArrayMesh mesh, MeshInstance3D renderer, Material[] materials) {
            if (mesh == null || renderer == null) return;
            if (meshMaterials.TryGetValue(mesh, out var state)) {
                state.Renderer = renderer;
                state.Materials = materials ?? System.Array.Empty<Material>();
            } else {
                meshMaterials.Add(mesh, new MeshMaterialState { Renderer = renderer, Materials = materials ?? System.Array.Empty<Material>() });
            }
            ApplySurfaceMaterials(mesh);
        }

        static void ApplySurfaceMaterials(ArrayMesh mesh) {
            if (mesh == null || !meshMaterials.TryGetValue(mesh, out var state) || state.Renderer == null) return;
            if (mesh.GetSurfaceCount() == 0) {
                state.Renderer.MaterialOverride = state.Materials.Length > 0 ? state.Materials[0] : null;
                return;
            }
            state.Renderer.MaterialOverride = null;
            for (var i = 0; i < mesh.GetSurfaceCount(); i++) {
                var material = state.Materials.Length == 0 ? null : state.Materials[System.Math.Min(i, state.Materials.Length - 1)];
                state.Renderer.SetSurfaceOverrideMaterial(i, material);
            }
        }

        static Godot.Vector3[] ToGodotVectors(IReadOnlyList<Godot.Vector3> values) {
            var converted = new Godot.Vector3[values.Count];
            for (var i = 0; i < values.Count; i++) converted[i] = Vector3.DecodePosition(values[i]);
            return converted;
        }

        // Empty submeshes are allowed, but Godot fails to add a surface without indices, which would shift
        // all the following surfaces (and therefore their materials). Empty submeshes are kept as a single
        // degenerate triangle, which renders nothing and is skipped when the indices are read back.
        static readonly int[] emptySubmeshIndices = { 0, 0, 0 };

        static void AddSubmeshSurface(ArrayMesh m, Godot.Collections.Array arrays, IReadOnlyList<int> indices) {
            if (((Godot.Vector3[])arrays[(int)Godot.Mesh.ArrayType.Vertex]).Length == 0) return; //nothing to draw at all
            arrays[(int)Godot.Mesh.ArrayType.Index] = indices.Count >= 3 ? ToGodotIndices(indices) : emptySubmeshIndices;
            m.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        }

        static bool IsDegenerate(int[] indices, int i) {
            return indices[i] == indices[i + 1] && indices[i] == indices[i + 2];
        }

        // The triangle order is kept as is: negating Z (see Vector3.DecodePosition) switches from the program's left-handed
        // to Godot's right-handed axes, which describes the same visual layout, so the on screen winding is unchanged
        // and clockwise triangles stay front faces. Swapping indices here turns every mesh inside out.
        static int[] ToGodotIndices(IReadOnlyList<int> values) {
            return values.ToArray();
        }

        public static List<T[]> AdaptList<T>(List<List<T>> inList) {
            var outList = new List<T[]>(); //todo: rationalize code in method below
            foreach (var l in inList) {
                var a = new T[l.Count];
                for (int i = 0; i < l.Count; i++) {
                    a[i] = l[i];
                }
                outList.Add(a);
            }
            return outList;
        }

        public static void AssignMaterials(Godot.MeshInstance3D m, Material[] materials) {
            if (m?.Mesh is ArrayMesh arrayMesh) SetMeshMaterials(arrayMesh, m, materials);
            else if (m != null) m.MaterialOverride = materials != null && materials.Length > 0 ? materials[0] : null;



            /*//TODO elsewhere
            var material1 = new ShaderMaterial();
            material1.Shader = (Shader)ResourceLoader.Load("res://Shaders/Shader1.shader");

            var material2 = new ShaderMaterial();
            material2.Shader = (Shader)ResourceLoader.Load("res://Shaders/Shader2.shader");

            m.SetSurfaceMaterial(0, material1);
            m.SetSurfaceMaterial(1, material2);
            */
        }

        public static Material[] GetMaterials(MeshInstance3D m) {
            if (m == null) return System.Array.Empty<Material>();
            if (m?.Mesh is ArrayMesh mesh && meshMaterials.TryGetValue(mesh, out var state)) return state.Materials;
            var n = m.GetSurfaceOverrideMaterialCount();
            var res = new Material[n];
            for (int i = 0; i < n; i++) {
                res[i] = m.GetSurfaceOverrideMaterial(i);
            }
            return res;
        }

        public static List<Godot.Vector3> CalculateNormals(List<Godot.Vector3> vertices, List<int[]> indices) {
            var res = new List<Godot.Vector3>();

            for (int i = 0; i < vertices.Count; i++) {
                res.Add(Godot.Vector3.Zero);
            }

            foreach (var mat in indices) {
                for (var i = 0; i < mat.Length; i += 3) {
                    var i1 = mat[i];
                    var i2 = mat[i + 1];
                    var i3 = mat[i + 2];

                    var v1 = vertices[i1];
                    var v2 = vertices[i2];
                    var v3 = vertices[i3];

                    var e1 = v2 - v1;
                    var e2 = v3 - v1;

                    var n = e1.Cross(e2).Normalized();

                    res[i1] += n;
                    res[i2] += n;
                    res[i3] += n;
                }
            }

            for (int i = 0; i < res.Count; i++) {
                res[i] = res[i].Normalized();
            }

            return res;
        }
        public static void AssignData(
            Godot.ArrayMesh m, Material[] materials,
            List<int[]> indices, List<Godot.Vector3> vertices, List<Godot.Vector2> uvs,
            string name = null
        ) {
            m.ClearSurfaces();

            if (materials.Length > 0) {
                var normals = CalculateNormals(vertices, indices);

                Godot.Collections.Array arrays = [];
                arrays.Resize((int)Godot.Mesh.ArrayType.Max);
                arrays[(int)Godot.Mesh.ArrayType.Vertex] = ToGodotVectors(vertices);
                arrays[(int)Godot.Mesh.ArrayType.Normal] = ToGodotVectors(normals);
                arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs.ToArray();

                for (int i = 0; i < indices.Count; i++) {
                    AddSubmeshSurface(m, arrays, indices[i]);
                }
            }
            if (meshMaterials.TryGetValue(m, out var materialState)) {
                if (materials != null && materials.Length > 0) materialState.Materials = materials;
                ApplySurfaceMaterials(m);
            }
            //TODO: check culling bounds
            //no name available on godot meshes

        }

        public static void AssignData(
            Godot.ArrayMesh m, List<int> indices,
            List<Godot.Vector3> vertices, string name = null
        ) {
            m.ClearSurfaces();
            var normals = CalculateNormals(vertices, new List<int[]>() { indices.ToArray() });
            Godot.Collections.Array arrays = [];
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = ToGodotVectors(vertices);
            arrays[(int)Godot.Mesh.ArrayType.Normal] = ToGodotVectors(normals);
            AddSubmeshSurface(m, arrays, indices);
            ApplySurfaceMaterials(m);
            //TODO: check culling bounds
            //no name available on godot meshes
        }


        public static void AssignData(
            Godot.ArrayMesh m, Material[] materials,
            List<int[]> indices, Godot.Vector3[] vertices, Godot.Vector3[] normals, Godot.Vector2[][] uvs, Color32[] colors
        ) {
            m.ClearSurfaces();

            if (materials.Length > 0) {
                Godot.Collections.Array arrays = [];
                arrays.Resize((int)Godot.Mesh.ArrayType.Max);
                arrays[(int)Godot.Mesh.ArrayType.Vertex] = ToGodotVectors(vertices);
                arrays[(int)Godot.Mesh.ArrayType.Normal] = ToGodotVectors(normals);
                if (colors != null) {
                    var gColors = new Godot.Color[colors.Length];
                    for (var i = 0; i < colors.Length; i++) {
                        gColors[i] = (Godot.Color)colors[i];
                    }
                    arrays[(int)Godot.Mesh.ArrayType.Color] = gColors;
                }
                if (uvs != null && uvs.Length > 0) {
                    arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs[0];
                }
                if (uvs != null && uvs.Length > 1) arrays[(int)Godot.Mesh.ArrayType.TexUV2] = uvs[1];
                if (uvs != null && uvs.Length > 2) {
                    LogWarning("Loading mesh with more than two uv channels: " + uvs.Length + ", some will be discarded");
                }

                for (int i = 0; i < indices.Count; i++) {
                    AddSubmeshSurface(m, arrays, indices[i]);
                }
            }
            if (meshMaterials.TryGetValue(m, out var materialState)) {
                if (materials != null && materials.Length > 0) materialState.Materials = materials;
                ApplySurfaceMaterials(m);
            }
            //TODO: check culling bounds
        }

        public static (MeshInstance3D mr, ArrayMesh m) CreateMesh(Node3D parent, Material[] materials) {
            var mr = new MeshInstance3D();
            var m = new ArrayMesh();
            mr.Mesh = m;
            //TODO: shadowCastingMode ON
            SetMeshMaterials(m, mr, materials);
            //the renderer is a component of the parent object, so it has its layer
            if (parent is StaticBody3D body) GameObject.SetLayer(mr, GameObject.GetLayer(body));
            //a null parent leaves the node orphaned (e.g. temporary helper objects), the caller must free it
            parent?.AddChild(mr);
            return (mr, m);
        }

        public static void SetShadowsTwoSidedRecursive(Node3D parent) {
            foreach (var mr in Scriptable.FindMultipleRecursive<GeometryInstance3D>(parent)) {
                mr.CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided;
            }
        }

        public static Node3D CreateMeshObject(Node3D parent, Material[] materials) {
            return CreateMesh(parent, materials).mr;
        }

        public static CollisionShape3D CreateCollider(Node3D obj, Godot.ArrayMesh m) {
            var c = new CollisionShape3D();
            obj.AddChild(c);
            RefreshCollider(m, c, true);
            return c;
        }

        public static MeshInstance3D RetrieveMeshRenderer(Node3D obj) {
            //Components are direct children here: look only at the object and its direct children
            //(not deeper, e.g. lanes/props containers), and skip line renderers, which are MeshInstance3Ds too
            if (obj == null || !GodotObject.IsInstanceValid(obj)) return null;
            if (obj is MeshInstance3D self && obj is not LineRenderer) return self;
            foreach (var child in obj.GetChildren()) {
                if (child is MeshInstance3D mr && child is not LineRenderer && GodotObject.IsInstanceValid(mr)) return mr;
            }
            return null;
        }

        public static Godot.Mesh RetrieveMesh(MeshInstance3D mr) {
            return mr.Mesh;
        }

        public static Shape3D GetColliderMesh(CollisionShape3D mc) {
            return mc.Shape;
        }

        public static Transform3D GetColliderTransform(CollisionShape3D mc) {
            return mc.GlobalTransform;
        }

        //all the surfaces of a mesh share the same vertex arrays (one surface per submesh), read them from the first one
        static Variant GetSurfaceArray(ArrayMesh m, Godot.Mesh.ArrayType type) {
            if (m == null || m.GetSurfaceCount() == 0) return default;
            return m.SurfaceGetArrays(0)[(int)type];
        }

        public static Godot.Vector3[] GetVertices(ArrayMesh m) {
            var arr = GetSurfaceArray(m, Godot.Mesh.ArrayType.Vertex);
            if (arr.VariantType == Variant.Type.Nil) return System.Array.Empty<Godot.Vector3>();
            var res = (Godot.Vector3[])arr;
            for (var i = 0; i < res.Length; i++) res[i] = Vector3.EncodePosition(res[i]);
            return res;
        }

        public static Godot.Vector3[] GetNormals(ArrayMesh m) {
            var arr = GetSurfaceArray(m, Godot.Mesh.ArrayType.Normal);
            if (arr.VariantType == Variant.Type.Nil) return System.Array.Empty<Godot.Vector3>();
            var res = (Godot.Vector3[])arr;
            for (var i = 0; i < res.Length; i++) res[i] = Vector3.EncodePosition(res[i]);
            return res;
        }

        public static Godot.Vector2[] GetUVs(ArrayMesh m) {
            var arr = GetSurfaceArray(m, Godot.Mesh.ArrayType.TexUV);
            if (arr.VariantType == Variant.Type.Nil) return System.Array.Empty<Godot.Vector2>();
            return (Godot.Vector2[])arr;
        }

        public static int[] GetIndices(ArrayMesh m, int i) {
            if (i >= m.GetSurfaceCount()) return System.Array.Empty<int>(); //empty mesh, see GetSubmeshCount
            var surfArrays = m.SurfaceGetArrays(i); //TODO: check
            var godotIndices = (int[])surfArrays[(int)Godot.Mesh.ArrayType.Index];
            var res = new List<int>();
            for (int j = 0; j + 2 < godotIndices.Length; j += 3) {
                if (IsDegenerate(godotIndices, j)) continue; //placeholder of an empty submesh
                res.Add(godotIndices[j]); res.Add(godotIndices[j + 1]); res.Add(godotIndices[j + 2]);
            }
            return res.ToArray();
        }

        // A mesh without vertices has no surfaces at all (see AddSubmeshSurface), but it still counts as having
        // one empty submesh: consumers of the exported data (e.g. the core exporters) expect at least one
        public static int GetSubmeshCount(ArrayMesh m) {
            return System.Math.Max(1, m.GetSurfaceCount());
        }

        public static void CombineMeshes(Godot.Mesh m, CombineInstance[] meshes) {
            if (m is not ArrayMesh combined) return;
            var vertices = new List<Godot.Vector3>();
            var indices = new List<int>();
            foreach (var instance in meshes) {
                if (instance?.mesh is not ConcavePolygonShape3D concave) continue;
                foreach (var godotVertex in concave.GetFaces()) {
                    vertices.Add(Vector3.EncodePosition(instance.transform * godotVertex));
                    indices.Add(indices.Count);
                }
            }
            if (vertices.Count > 0) AssignData(combined, indices, vertices);
        }

        public static CollisionShape3D CreateCapsuleCollider(Node3D obj, float radius) {
            var collisionShape = new CollisionShape3D();
            obj.AddChild(collisionShape);

            var capsuleShape = new CapsuleShape3D();
            capsuleShape.Radius = radius;
            capsuleShape.Height = 2 * radius;
            // Godot capsules lie along Y; the program's capsules lie along the local Z axis.
            collisionShape.RotationDegrees = new Godot.Vector3(90, 0, 0);

            collisionShape.Shape = capsuleShape;

            return collisionShape;
        }

        public static void AdjustCapsuleCollider(Node3D obj, Godot.Vector3 center, float height) {
            var collisionShape = Scriptable.FindRecursive<CollisionShape3D>(obj);
            var capsuleShape = collisionShape.Shape as CapsuleShape3D;
            if (capsuleShape != null) {
                collisionShape.Position = Vector3.DecodePosition(center);
                // Godot shrinks the radius when height < 2 * radius, the height is clamped instead.
                capsuleShape.Height = Mathf.Max(height, 2 * capsuleShape.Radius);
            }
        }

        public static CollisionShape3D CreateBoxCollider(Node3D obj, Godot.Vector3 size, Godot.Vector3 center) {
            var collisionShape = new CollisionShape3D();
            obj.AddChild(collisionShape);

            var boxShape = new BoxShape3D();
            boxShape.Size = size;
            collisionShape.Position = center;
            collisionShape.Shape = boxShape;

            return collisionShape;
        }

        public static Aabb GetEncapsulatingBoundsForChildMeshes(Node3D obj) {
            var mfs = Scriptable.FindMultipleRecursive<MeshInstance3D>(obj);
            if (mfs.Length > 0) {
                // Bounds are expressed in obj's local space (like mesh bounds), since they
                // are used to place colliders that are children of obj.
                var toObjLocal = obj.GlobalTransform.AffineInverse();
                Aabb? res = null;
                foreach (var meshInstance in mfs) {
                    var mesh = meshInstance.Mesh;
                    if (mesh == null) continue;
                    var localAabb = mesh.GetAabb();
                    var objAabb = TransformAabb(localAabb, toObjLocal * meshInstance.GlobalTransform);
                    if (res == null)
                        res = objAabb;
                    else
                        res = res.Value.Merge(objAabb);
                }
                return res ?? new Aabb(Godot.Vector3.Zero, Godot.Vector3.Zero);
            }
            else {
                return new Aabb(Godot.Vector3.Zero, Godot.Vector3.Zero);
            }
        }

        private static Aabb TransformAabb(Aabb aabb, Transform3D transform) {
            var corners = new Godot.Vector3[8];
            var pos = aabb.Position;
            var size = aabb.Size;

            corners[0] = pos;
            corners[1] = pos + new Godot.Vector3(size.X, 0, 0);
            corners[2] = pos + new Godot.Vector3(0, size.Y, 0);
            corners[3] = pos + new Godot.Vector3(0, 0, size.Z);
            corners[4] = pos + new Godot.Vector3(size.X, size.Y, 0);
            corners[5] = pos + new Godot.Vector3(0, size.Y, size.Z);
            corners[6] = pos + new Godot.Vector3(size.X, 0, size.Z);
            corners[7] = pos + size;

            for (int i = 0; i < 8; i++) {
                corners[i] = transform * corners[i];
            }

            var min = corners[0];
            var max = corners[0];
            for (int i = 1; i < 8; i++) {
                min = min.Min(corners[i]);
                max = max.Max(corners[i]);
            }

            return new Aabb(min, max - min);
        }

        public static void RefreshCollider(Godot.ArrayMesh m, CollisionShape3D mc, bool enabled) {
            if (mc == null) return;
            if (!enabled || m == null || m.GetSurfaceCount() == 0) {
                mc.Disabled = true;
                mc.Shape = null;
                return;
            }
            // The collider uses every submesh; each Godot surface holds one submesh.
            var faces = new List<Godot.Vector3>();
            for (int s = 0; s < m.GetSurfaceCount(); s++) {
                var arrays = m.SurfaceGetArrays(s);
                var vertices = (Godot.Vector3[])arrays[(int)Godot.Mesh.ArrayType.Vertex];
                var indices = (int[])arrays[(int)Godot.Mesh.ArrayType.Index];
                if (vertices == null || indices == null) continue;
                for (int i = 0; i + 2 < indices.Length; i += 3) {
                    if (IsDegenerate(indices, i)) continue; //placeholder of an empty submesh
                    faces.Add(vertices[indices[i]]);
                    faces.Add(vertices[indices[i + 1]]);
                    faces.Add(vertices[indices[i + 2]]);
                }
            }

            if (faces.Count == 0) {
                mc.Disabled = true;
                mc.Shape = null;
                return;
            }

            // After the winding conversion to Godot's axes the physics engine considers these faces as
            // facing away from their rendered side, so one-sided collision lets rays from above pass
            // through roads and intersections. Collide on both sides.
            var concaveShape = new ConcavePolygonShape3D { BackfaceCollision = true };
            concaveShape.SetFaces(faces.ToArray());

            mc.Shape = concaveShape;
            mc.Disabled = false;
        }

        /*public static void ClearCollider(CollisionShape3D mc) {
            mc.Disabled = true; 
        }*/

        public static void Clear(Godot.ArrayMesh m) {
            if (m == null) return;
            m.ClearSurfaces();
        }

        public static void Destroy(Godot.ArrayMesh m) {
            m?.ClearSurfaces();
        }

        public static void DestroyCollider(Godot.CollisionShape3D mc) {
            GameObject.Destroy(mc);
        }

    }
    public static class Raycast {
        public struct RaycastHit {
            public Godot.Vector3 point;
            public Node collider;
            public CollisionShape3D shape;
        }
        public static Godot.Vector3 ProjectPoint(Godot.Vector3 pos, int collisionMask = 1 << 3) {
            var godotPos = Vector3.DecodePosition(pos);
            var from = godotPos + Godot.Vector3.Up * 1000000;
            var to = godotPos + Godot.Vector3.Down * 1000000;

            var spaceState = PhysicsServer3D.SpaceGetDirectState( //or obj.GetWorld3D().DirectSpaceState;
                ((SceneTree)Engine.GetMainLoop()).Root.GetWorld3D().Space
            );

            var rayParams = new PhysicsRayQueryParameters3D {
                From = from,
                To = to,
                CollisionMask = (uint)collisionMask
            };

            var result = spaceState.IntersectRay(rayParams);
            if (result.Count > 0 && result.ContainsKey("position")) {
                // The hit position of a ray starting 1000000 units away is only precise to 1/16 of a unit (float),
                // which moved the projected elements by that much every time they were projected again (e.g. after
                // undoing a terrain edit): it's refined with a short ray around the hit.
                var hitPos = (Godot.Vector3)result["position"];
                rayParams.From = hitPos + Godot.Vector3.Up;
                rayParams.To = hitPos + Godot.Vector3.Down;
                var refined = spaceState.IntersectRay(rayParams);
                if (refined.Count > 0 && refined.ContainsKey("position")) hitPos = (Godot.Vector3)refined["position"];
                // The ray is vertical, only the height comes from the hit: its X and Z differ from the point's by a few
                // 1e-5, which moved the projected elements a little more at every update.
                return Vector3.EncodePosition(new Godot.Vector3(godotPos.X, hitPos.Y, godotPos.Z));
            }

            return pos;
        }

        public static bool DoRaycast(Node3D parent, Godot.Vector3 origin, Godot.Vector3 direction, out RaycastHit hit, float distance, int collisionMask) {
            var spaceState = parent.GetWorld3D().DirectSpaceState;
            var godotOrigin = Vector3.DecodePosition(origin);
            var godotDirection = Vector3.DecodePosition(direction);
            var end = godotOrigin + godotDirection * distance;

            var query = PhysicsRayQueryParameters3D.Create(godotOrigin, end);
            query.CollisionMask = unchecked((uint)collisionMask);
            query.CollideWithAreas = false;
            var result = spaceState.IntersectRay(query);

            Godot.Vector3 hitPoint = Godot.Vector3.Zero;
            Node hitCollider = null;
            CollisionShape3D hitShape = null;
            bool res = false;

            if (result.Count > 0) {
                hitPoint = Vector3.EncodePosition((Godot.Vector3)result["position"]);
                hitCollider = (Node)result["collider"];
                if (hitCollider is CollisionObject3D body) {
                    var owner = body.ShapeFindOwner((int)result["shape"]);
                    hitShape = body.ShapeOwnerGetOwner(owner) as CollisionShape3D;
                }
                res = true;
            }

            hit = new RaycastHit {
                point = hitPoint,
                collider = hitCollider,
                shape = hitShape
            };

            return res;
        }

    public static (Godot.Vector3 origin, Godot.Vector3 direction) GetRayFromScreen(Camera3D camera, Godot.Vector2 screenPos)
    {
        Godot.Vector3 origin = camera.ProjectRayOrigin(screenPos);
        Godot.Vector3 direction = camera.ProjectRayNormal(screenPos); // normalized

        return (Vector3.EncodePosition(origin), Vector3.EncodePosition(direction));
    }
    }

    public partial class LineRenderer : MeshInstance3D {
        private ImmediateMesh immediateMesh;
        private float _startWidth = 0.1f;
        private float _endWidth = 0.1f;
        private int _positionCount = 0;

        private bool _enabled = true;

        private Material _material;

        public float startWidth {
            get => _startWidth;
            set {
                if (_startWidth != value) {
                    _startWidth = value;
                    rebuilt = false;
                }
            }
        }

        public float endWidth {
            get => _endWidth;
            set {
                if (_endWidth != value) {
                    _endWidth = value;
                    rebuilt = false;
                }
            }
        }

        public int positionCount {
            get => _positionCount;
            set {
                if (_positionCount != value) {
                    _positionCount = value;
                    var newPositions = new List<Godot.Vector3>();
                    for (int i = 0; i < _positionCount; i++) {
                        newPositions.Add(Vector3.zero);
                    }
                    for (int i = 0; i < Mathf.Min(_positionCount, positions.Count); i++) {
                        newPositions[i] = positions[i];
                    }
                    positions = newPositions;
                    rebuilt = false;
                }
            }
        }

        public bool enabled {
            get => _enabled;
            set {
                if (_enabled != value) {
                    _enabled = value;
                    rebuilt = false;
                }
            }
        }

        public Material material {
            get => _material;
            set {
                if (_material != value) {
                    _material = value;
                    rebuilt = false;
                }
            }
        }

        // Called every frame while the line is enabled, before rebuilding it (e.g. to scale the width with the camera distance).
        public System.Action frameUpdate {
            get => _frameUpdate;
            set {
                _frameUpdate = value;
                UpdateProcessing();
            }
        }

        private System.Action _frameUpdate;
        private bool _rebuilt = false;
        private bool rebuilt {
            get => _rebuilt;
            set {
                _rebuilt = value;
                UpdateProcessing();
            }
        }
        private bool processing = true; //Godot enables it on ready, as _Process is overridden
        private List<Godot.Vector3> positions = new List<Godot.Vector3>();

        // There can be thousands of line renderers, and a script callback per node per frame is costly:
        // _Process runs only while there is something to do.
        private void UpdateProcessing() {
            var p = !_rebuilt || (_enabled && _frameUpdate != null);
            if (p == processing) return;
            processing = p;
            SetProcess(p);
        }

        public LineRenderer() {
            immediateMesh = new ImmediateMesh();
            Mesh = immediateMesh;
            // Line renderers use world space positions and do not cast shadows.
            TopLevel = true;
            CastShadow = ShadowCastingSetting.Off;
            rebuilt = false;
        }

        public void GetPositions(Godot.Vector3[] outPos) {
            for (int i = 0; i < positionCount; i++) {
                outPos[i] = positions[i];
            }
        }

        public Godot.Vector3 GetPosition(int i) {
            return positions[i];
        }

        public void SetPosition(int i, Godot.Vector3 pos) {
            positions[i] = pos;
            rebuilt = false;
        }

        public void SetPositions(Godot.Vector3[] positions) {
            if (positions == null) positions = System.Array.Empty<Godot.Vector3>();
            positionCount = positions.Length;
            for (int i = 0; i < positions.Length; i++) this.positions[i] = positions[i];
            rebuilt = false;
        }

        private void Rebuild() {
            if (rebuilt) return;
            immediateMesh.ClearSurfaces();
            if (!_enabled) {
                rebuilt = true;
                return;
            }
            if (positions.Count < 2) {
                rebuilt = true;
                return;
            }
            MaterialOverride = _material;

            // Positions are in program world coordinates (like the rest of the logic code), so they are
            // converted here. The line is drawn as a flat ribbon of the requested width, which is a
            // good approximation for the (mostly horizontal) lines drawn on roads and terrain.
            var count = positions.Count;
            var points = new Godot.Vector3[count];
            for (int i = 0; i < count; i++) points[i] = Vector3.DecodePosition(positions[i]);
            immediateMesh.SurfaceBegin(Godot.Mesh.PrimitiveType.TriangleStrip);
            for (int i = 0; i < count; i++) {
                var dir = points[System.Math.Min(i + 1, count - 1)] - points[System.Math.Max(i - 1, 0)];
                var side = new Godot.Vector3(dir.X, 0, dir.Z).Cross(Godot.Vector3.Up);
                side = side.LengthSquared() > 1e-12f ? side.Normalized() : Godot.Vector3.Right;
                var halfWidth = Mathf.Lerp(_startWidth, _endWidth, i / (count - 1.0f)) * 0.5f;
                immediateMesh.SurfaceSetNormal(Godot.Vector3.Up);
                immediateMesh.SurfaceAddVertex(points[i] - side * halfWidth);
                immediateMesh.SurfaceSetNormal(Godot.Vector3.Up);
                immediateMesh.SurfaceAddVertex(points[i] + side * halfWidth);
            }
            immediateMesh.SurfaceEnd();
            rebuilt = true;
        }

        public override void _Process(double delta) {
            if (_enabled && _frameUpdate != null) _frameUpdate();
            if (!rebuilt) {
                Rebuild();
            }
        }
    }

    public static class Matrix4x4 {
        public static Transform3D TRS(Godot.Vector3 position, Godot.Quaternion rotation, Godot.Vector3 scale) {
            var basis = new Basis(rotation) * Basis.FromScale(scale); //rotation * scale
            return new Transform3D(basis, position);
        }

        public static Godot.Vector3 MultiplyPoint(Transform3D matrix, Godot.Vector3 point) {
            return matrix * point;
        }

        public static float[] AsArray(Transform3D transform) {
            var res = new float[16];

            var x = transform.Basis.X;
            var y = transform.Basis.Y;
            var z = transform.Basis.Z;
            var origin = transform.Origin;

            res[0] = x.X;
            res[1] = x.Y;
            res[2] = x.Z;
            res[3] = 0;

            res[4] = y.X;
            res[5] = y.Y;
            res[6] = y.Z;
            res[7] = 0;

            res[8] = z.X;
            res[9] = z.Y;
            res[10] = z.Z;
            res[11] = 0;

            res[12] = origin.X;
            res[13] = origin.Y;
            res[14] = origin.Z;
            res[15] = 1;

            return res;
        }
    }

    public struct Color32 {
        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public Color32(byte r, byte g, byte b, byte a) {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static implicit operator Godot.Color(Color32 c) {
            return new Godot.Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        }
    }

    public static class Resources {
        public static ShaderMaterial LoadMaterial(string path) {
            var resourcePath = "res://" + path.TrimStart('/').Replace('\\', '/') + ".tres";
            var material = GD.Load<ShaderMaterial>(resourcePath);
            if (material == null) LogError("Could not load material resource: " + resourcePath);
            return material;
        }

        public static Node3D InstantiateObject(string path, Node3D parent = null) {
            var normalizedPath = path.TrimStart('/').Replace('\\', '/');
            if (!normalizedPath.Contains('/')) normalizedPath = "Scenes/" + normalizedPath;
            var resourcePath = "res://" + normalizedPath + ".tscn";
            var scene = GD.Load<PackedScene>(resourcePath);
            if (scene == null) {
                LogError("Could not load scene resource: " + resourcePath);
                return null;
            }
            var instance = scene.Instantiate<Node3D>();
            if (parent != null) parent.AddChild(instance);
            return instance;
        }
    }

    public static class Texture { //TODO MISSING: aniso level
        public enum Format {
            RGB8 = 3,
            RGBA8 = 4,
            DXT1 = 10,
            DXT5 = 12,
            DXT3 = 1003,
        }

        public static Dictionary<Format, Image.Format> formatMap = new Dictionary<Format, Image.Format>() {
            { Format.RGB8, Image.Format.Rgb8 },
            { Format.RGBA8, Image.Format.Rgba8 },
            { Format.DXT1, Image.Format.Dxt1 },
            { Format.DXT3, Image.Format.Dxt3 },
            { Format.DXT5, Image.Format.Dxt5 },
        };

        public static bool HasAlphaChannel(Texture2D tex) {
            if (tex == null) return false;
            var image = tex.GetImage();
            if (image == null) return false;
            switch (image.GetFormat()) {
                case Image.Format.La8:
                case Image.Format.Rgba8:
                case Image.Format.Rgba4444:
                case Image.Format.Rgbaf:
                case Image.Format.Rgbah:
                case Image.Format.Dxt3:
                case Image.Format.Dxt5:
                case Image.Format.BptcRgba:
                case Image.Format.Etc2Rgba8:
                case Image.Format.Etc2Rgb8A1:
                    return true;
                default:
                    return false;
            }
        }
        public static Texture2D CreateFromData(int width, int height, Format format, bool mipmaps, byte[] bytes) {
            Image img = Image.CreateFromData(width, height, mipmaps, formatMap[format], bytes);
            return ImageTexture.CreateFromImage(img);
        }

        // Pixel data only holds the base level, while Godot's CreateFromData mipmaps flag
        // expects the chain to be part of the data already.
        public static Texture2D CreateFromPixelData(int width, int height, Format format, bool mipmaps, byte[] bytes) {
            Image img = Image.CreateFromData(width, height, false, formatMap[format], bytes);
            if (mipmaps) img.GenerateMipmaps();
            return ImageTexture.CreateFromImage(img);
        }

        static long GetLevelSize(int width, int height, Format format) {
            return format switch {
                Format.RGB8 => (long)width * height * 3,
                Format.RGBA8 => (long)width * height * 4,
                Format.DXT1 => (long)Mathf.Max(1, (width + 3) / 4) * Mathf.Max(1, (height + 3) / 4) * 8,
                Format.DXT3 or Format.DXT5 => (long)Mathf.Max(1, (width + 3) / 4) * Mathf.Max(1, (height + 3) / 4) * 16,
                _ => 0
            };
        }

        static long GetFullChainSize(int width, int height, Format format) {
            long size = 0;
            while (true) {
                size += GetLevelSize(width, height, format);
                if (width == 1 && height == 1) return size;
                width = Mathf.Max(1, width / 2);
                height = Mathf.Max(1, height / 2);
            }
        }

        public static Texture2D CreateFromData(int width, int height, Format format, bool mipmaps, Color32[] pixels) {
            if (format != Format.RGB8 && format != Format.RGBA8) {
                LogError("Invalid texture format: " + format.ToString());
                return null;
            }
            var comp = format == Format.RGB8 ? 3 : 4;
            var bytes = new byte[pixels.Length * comp];
            for (int i = 0; i < pixels.Length; i++) { //TODO: check order
                bytes[i * comp + 0] = pixels[i].r;
                bytes[i * comp + 1] = pixels[i].g;
                bytes[i * comp + 2] = pixels[i].b;
                if (comp == 4) bytes[i * comp + 3] = pixels[i].a;
            }
            return CreateFromPixelData(width, height, format, mipmaps, bytes);
        }

        public static Texture2D CreateFromData(int width, int height, Format format, bool mipmaps, Godot.Color[] pixels) {
            if (format != Format.RGB8 && format != Format.RGBA8) {
                LogError("Invalid texture format: " + format.ToString());
                return null;
            }
            var comp = format == Format.RGB8 ? 3 : 4;
            var bytes = new byte[pixels.Length * comp];
            for (int i = 0; i < pixels.Length; i++) { //TODO: check order
                bytes[i * comp + 0] = (byte)(pixels[i].R * 255f);
                bytes[i * comp + 1] = (byte)(pixels[i].G * 255f);
                bytes[i * comp + 2] = (byte)(pixels[i].B * 255f);
                if (comp == 4) bytes[i * comp + 3] = (byte)(pixels[i].A * 255f);
            }
            return CreateFromPixelData(width, height, format, mipmaps, bytes);
        }

        public static Texture2D CreateFromPtrData(int width, int height, int fmt, int mips, byte[] bytes) {
            if (bytes == null || bytes.Length < 16) {
                LogError($"Invalid texture payload header ({width}x{height}, format {fmt}).");
                return null;
            }
            // The Python helper appends however many mip levels the source contains.
            return CreateFromRawData(width, height, (Format)fmt, mips, bytes.AsSpan(16));
        }

        // Raw data holding the base level followed by mipCount - 1 mip levels (the raw data of the loaded textures).
        // Godot's Image.CreateFromData needs the exact size of either the base level or the complete chain down to 1x1,
        // so trailing data is ignored, a complete chain is used as is and a partial one is rebuilt from the base level.
        public static Texture2D CreateFromRawData(int width, int height, Format format, int mipCount, ReadOnlySpan<byte> data) {
            if (width <= 0 || height <= 0 || !formatMap.TryGetValue(format, out var imageFormat)) {
                LogError($"Invalid texture data header ({width}x{height}, format {format}).");
                return null;
            }

            long levelSize = GetLevelSize(width, height, format);
            if (levelSize <= 0 || levelSize > data.Length || levelSize > int.MaxValue) {
                LogError($"Texture data is truncated for {width}x{height} format {format}: expected {levelSize} base-level bytes, got {data.Length}.");
                return null;
            }

            if (mipCount > 1) {
                long chainSize = GetFullChainSize(width, height, format);
                if (chainSize <= data.Length && chainSize <= int.MaxValue) {
                    Image full = Image.CreateFromData(width, height, true, imageFormat, data.Slice(0, (int)chainSize));
                    if (full != null && !full.IsEmpty()) return ImageTexture.CreateFromImage(full);
                }
            }

            Image img = Image.CreateFromData(width, height, false, imageFormat, data.Slice(0, (int)levelSize));
            if (img == null || img.IsEmpty()) {
                LogError($"Godot could not decode the base level of the {width}x{height} texture (format {format}).");
                return null;
            }
            if (mipCount > 1) {
                // Partial source chain: rebuild it from the base level (compressed images must be decompressed for that).
                // DXT1 decompresses to RGBA8, so it's converted back to RGB8 to stay opaque as before (see HasAlphaChannel).
                if (img.IsCompressed() && img.Decompress() == Error.Ok && format == Format.DXT1) img.Convert(Image.Format.Rgb8);
                if (!img.IsCompressed()) img.GenerateMipmaps();
            }
            return ImageTexture.CreateFromImage(img);
        }

        public static Texture2D CreateFromFile(string name, string ext) {
            byte[] fileData = System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath(name));
            Image image = new Image();
            Error err = ext == "png" ? image.LoadPngFromBuffer(fileData) : image.LoadJpgFromBuffer(fileData);
            if (err != Error.Ok) {
                LogError("Failed to load image: " + err);
                return null;
            }
            // Textures are stored bottom row first (UV v = 0 is the bottom of the image), like the raw data of
            // the other formats, Godot's loaders the top row first: flipped so that all the textures match.
            // Previews in the UI flip them back (TextureRect.FlipV) to show them upright.
            image.FlipY();
            image.GenerateMipmaps();
            return ImageTexture.CreateFromImage(image);
        }

        public static Image GetSampleableImage(Texture2D tex) {
            var res = tex.GetImage();
            //GetPixel does not work on compressed (e.g. DDS) images, so the bilinear lookup works on a decompressed copy
            if (res != null && res.IsCompressed() && res.Decompress() != Error.Ok) {
                LogError("Could not decompress the texture to sample it");
            }
            //TODO: missing wrap mode
            return res;
        }

        public static Godot.Color SampleBilinear(Image img, float u, float v) {
            var w = img.GetWidth();
            var h = img.GetHeight();

            float x = u * (w - 1);
            float y = v * (h - 1); //v = 0 is the bottom of the image, which is the first row (see CreateFromFile)
            int x0 = (int)Mathf.Floor(x);
            int x1 = Mathf.Min(x0 + 1, w - 1);
            int y0 = (int)Mathf.Floor(y);
            int y1 = Mathf.Min(y0 + 1, h - 1);
            float fx = x - x0;
            float fy = y - y0;

            var c00 = img.GetPixel(x0, y0);
            var c10 = img.GetPixel(x1, y0);
            var c01 = img.GetPixel(x0, y1);
            var c11 = img.GetPixel(x1, y1);

            var cx0 = c00.Lerp(c10, fx);
            var cx1 = c01.Lerp(c11, fx);
            return cx0.Lerp(cx1, fy);
        }

        public static float ToGrayscale(Godot.Color color) {
            return 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
        }
    }

    public class CombineInstance {
        public Godot.Shape3D mesh;
        public Transform3D transform;
    }

    public static class Terrain {
        sealed class HeightmapState {
            public float[,] Heights;
            public float MaxHeight;
            public float TerrainSize;
            public int Resolution;
            public ShaderMaterial SurfaceMaterial;
            public Terrain3DData Data;
            public int RegionSize;
            // Terrain3D pixels (flipped rows) edited since the last collision update (empty when Size.X == 0).
            public Rect2I DirtyRect;
        }

        static readonly ConditionalWeakTable<Node3D, HeightmapState> heightmaps = new();

        public static Node3D CreateTerrainGameObject(float[,] heights, int heightmapResolution, int terrainSize, float maxHeight, ShaderMaterial material, Node3D parent) {
            var terrain = Terrain3D.Instantiate();
            terrain.Name = "Terrain3D";
            terrain.VertexSpacing = (double)terrainSize / heightmapResolution;
            parent.AddChild(terrain);
            // Terrain3D initializes its data resource when it enters the scene tree.
            var data = terrain.Data;
            if (data == null) throw new InvalidOperationException("Terrain3D did not initialize its data resource when entering the scene tree.");
            // Collision settings must be applied after entering the tree: Terrain3D ignores them before
            // initializing, leaving the terrain on layer 1 with dynamic collision. The collision
            // must cover the whole terrain, while dynamic mode only builds collision around the camera.
            terrain.CollisionLayer = parent is CollisionObject3D collisionObject ? collisionObject.CollisionLayer : 1;
            terrain.CollisionMask = parent is CollisionObject3D collisionParent ? collisionParent.CollisionMask : 1;
            terrain.CollisionMode = Variant.From((long)Terrain3DCollision.CollisionMode.FullGame);
            // The terrain takes the layer of its object (used by the camera culling mask)
            terrain.RenderLayers = parent is CollisionObject3D renderParent && (renderParent.CollisionLayer & 0xFFFFF) != 0
                ? renderParent.CollisionLayer & 0xFFFFF : 1;
            var state = new HeightmapState {
                Heights = heights,
                MaxHeight = maxHeight,
                TerrainSize = terrainSize,
                Resolution = heightmapResolution,
                SurfaceMaterial = material,
                Data = data
            };
            heightmaps.Add(terrain, state);
            ApplyMaterial(terrain, state, material);
            ApplyHeightmap(terrain, state, heights, maxHeight);
            return terrain;
        }

        public static void SetMaterial(Node3D terrainObj, ShaderMaterial material) {
            if (!heightmaps.TryGetValue(terrainObj, out var state)) return;
            state.SurfaceMaterial = material;
            ApplyMaterial(terrainObj, state, material);
        }

        // Terrain3D renders with its own material, so the parameter changes of the grid or overlay
        // material (e.g. the brush circle) are copied over.
        public static void RefreshMaterial(Node3D terrainObj) {
            if (terrainObj is not Terrain3D terrain || !heightmaps.TryGetValue(terrainObj, out var state)) return;
            if (state.SurfaceMaterial == null || terrain.Material is not Terrain3DMaterial terrainMaterial || !terrainMaterial.ShaderOverrideEnabled) return;
            CopyShaderParameters(state.SurfaceMaterial, terrainMaterial);
        }

        // Terrain3D ignores parameters starting with "_" (reserved for its own uniforms),
        // so GridTerrain.gdshader names them without the leading underscore.
        static void CopyShaderParameters(ShaderMaterial source, Terrain3DMaterial target) {
            if (source?.Shader == null) return;
            foreach (var uniform in source.Shader.GetShaderUniformList()) {
                var name = uniform.AsGodotDictionary()["name"].AsString();
                var value = source.GetShaderParameter(name);
                if (value.VariantType != Variant.Type.Nil) target.SetShaderParam(name.TrimStart('_'), value);
            }
        }

        static void ApplyMaterial(Node3D terrainObj, HeightmapState state, ShaderMaterial material) {
            if (terrainObj is not Terrain3D terrain) return;
            state.SurfaceMaterial = material;
            var terrainMaterial = terrain.Material ??
                GD.Load<Terrain3DMaterial>("res://Materials/Terrain3DMaterial.tres") ??
                Terrain3DMaterial.Instantiate();
            terrainMaterial.ShowCheckered = false;
            // Terrain3D's generated shader samples the height maps to displace the
            // terrain mesh. Replacing it with the legacy flat grid shader removes
            // that displacement. Keep the native shader and use its grid overlay.
            terrainMaterial.AutoShader = true;
            terrainMaterial.ShaderOverrideEnabled = false;
            // The grid (Grid.gdshader) and the ground overlay (Default.gdshader) each have a Terrain3D
            // version of the shader.
            var shaderPath = material?.Shader?.ResourcePath ?? "";
            string overridePath = null;
            if (shaderPath.EndsWith("/Grid.gdshader", System.StringComparison.OrdinalIgnoreCase)) overridePath = "res://Materials/GridTerrain.gdshader";
            else if (shaderPath.EndsWith("/Default.gdshader", System.StringComparison.OrdinalIgnoreCase)) overridePath = "res://Materials/MapTerrain.gdshader";
            // The built-in vertex grid visualizes Terrain3D's mesh topology, which
            // is different from the world-space floor grid used by the editor.
            terrainMaterial.Call("set_show_vertex_grid", false);
            // The terrain must end at its heightmap edge, while Terrain3D's default flat background
            // extends the mesh forever outside the regions.
            terrainMaterial.WorldBackground = Terrain3DMaterial.WorldBackgroundEnum.None;
            if (overridePath != null) {
                var overrideShader = GD.Load<Shader>(overridePath);
                if (overrideShader == null) {
                    GD.PushError("Could not load the Terrain3D shader override " + overridePath + ".");
                    terrainMaterial.ShaderOverrideEnabled = false;
                } else {
                    terrainMaterial.ShaderOverride = overrideShader;
                    terrainMaterial.ShaderOverrideEnabled = true;
                    terrainMaterial.Update();
                    CopyShaderParameters(material, terrainMaterial);
                    // Regions are a whole number of region_size cells, so they still overhang the heightmap.
                    float half = state.TerrainSize * 0.5f;
                    terrainMaterial.SetShaderParam("TerrainBounds", new Godot.Vector4(-half, -half, half, half));
                }
            } else {
                terrainMaterial.ShaderOverrideEnabled = false;
                terrainMaterial.Update();
            }
            terrain.Material = terrainMaterial;
        }

        static void ApplyHeightmap(Node3D terrainObj, HeightmapState state, float[,] heights, float maxHeight) {
            if (terrainObj is not Terrain3D terrain || state.Data == null) return;
            var regions = state.Data.GetRegionsActive();
            foreach (var regionVariant in regions) {
                var nativeRegion = regionVariant.AsGodotObject();
                if (nativeRegion is Terrain3DRegion region) state.Data.RemoveRegion(region, false);
                else if (nativeRegion != null && ClassDB.IsParentClass(nameof(Terrain3DRegion), nativeRegion.GetClass()))
                    state.Data.RemoveRegion(Terrain3DRegion.Bind(nativeRegion), false);
            }

            // Heights are indexed with rows along the program's Z axis. Godot's Z is
            // reversed, so the rows are flipped to place row 0 at Godot Z = TerrainSize / 2.
            int rows = heights.GetLength(0);
            int columns = heights.GetLength(1);
            var absoluteHeights = new float[rows * columns];
            for (int row = 0; row < rows; row++) {
                for (int column = 0; column < columns; column++) {
                    float normalizedHeight = Mathf.Clamp(heights[row, column], 0f, 1f);
                    absoluteHeights[(rows - 1 - row) * columns + column] = (normalizedHeight - 0.5f) * 2f * maxHeight;
                }
            }
            var bytes = new byte[absoluteHeights.Length * sizeof(float)];
            Buffer.BlockCopy(absoluteHeights, 0, bytes, 0, bytes.Length);
            var image = Image.CreateFromData(columns, rows, false, Image.Format.Rf, bytes);
            var images = new Godot.Collections.Array();
            images.Resize(3);
            images[(int)Terrain3DRegion.MapType.Height] = image;
            var pos = new Godot.Vector3(-state.TerrainSize * 0.5f, 0, -state.TerrainSize * 0.5f);
            state.Data.ImportImages(images, pos);
            state.Data.CalcHeightRange(true);
            state.Data.UpdateMaps(Terrain3DRegion.MapType.Height, true, true);
            state.Heights = heights;
            state.MaxHeight = maxHeight;
            state.Resolution = rows - 1;
            state.DirtyRect = new Rect2I();
            if (terrain.IsInsideTree()) terrain.ForceUpdateTransform();
            // Regions were replaced, so the full collision must be rebuilt to match the new heights.
            var collision = terrain.Call("get_collision").AsGodotObject();
            if (collision != null) Terrain3DCollision.Bind(collision).Build();
        }

        public static byte[] GetHeightBytes(Node3D terrainObj) {
            if (!heightmaps.TryGetValue(terrainObj, out var state) || state.Heights == null)
                throw new InvalidOperationException("The terrain has no heightmap data to save.");
            var bytes = new byte[state.Heights.Length * sizeof(float)];
            Buffer.BlockCopy(state.Heights, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        public static Node3D CreateHeightmap(int terrainSize, int heightmapResolution, float maxHeight, float[,] heights, Node3D parent, ShaderMaterial material) {
            return CreateTerrainGameObject(heights, heightmapResolution, terrainSize, maxHeight, material, parent);
        }

        public static void SetHeightmap(Node3D terrainObj, float maxHeight, float[,] heights) {
            if (!heightmaps.TryGetValue(terrainObj, out var state))
                throw new InvalidOperationException("Cannot edit a Terrain3D node that was not created by GameEngine.Terrain.");
            ApplyHeightmap(terrainObj, state, heights, maxHeight);
        }

        public static int GetHeightmapResolution(Node3D terrainObj) {
            if (terrainObj == null || !heightmaps.TryGetValue(terrainObj, out var state) || state.Heights == null) return 0;
            return state.Heights.GetLength(1);
        }

        public static float GetPixelSize(Node3D terrainObj) {
            if (terrainObj == null || !heightmaps.TryGetValue(terrainObj, out var state) || state.Resolution <= 0) return 1;
            return state.TerrainSize / state.Resolution;
        }

        public static float GetSize(Node3D terrainObj) {
            if (terrainObj == null || !heightmaps.TryGetValue(terrainObj, out var state)) return 0;
            return state.TerrainSize;
        }

        // Normalized (0-1) heights, indexed [y, x] (row, column).
        public static float[,] GetHeights(Node3D terrainObj, int xBase, int yBase, int width, int height) {
            var res = new float[height, width];
            if (terrainObj == null || !heightmaps.TryGetValue(terrainObj, out var state) || state.Heights == null) return res;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    res[y, x] = state.Heights[yBase + y, xBase + x];
                }
            }
            return res;
        }

        // Writes normalized (0-1) heights to the terrain mesh and collision. The collision is updated
        // immediately so elements projected to the ground in the same
        // frame (ElementManager.ProcessUpdate) follow the edited terrain.
        public static void SetHeightsDelayLOD(Node3D terrainObj, int xBase, int yBase, float[,] heights) {
            if (terrainObj is not Terrain3D || !heightmaps.TryGetValue(terrainObj, out var state) || state.Heights == null || state.Data == null) return;
            int height = heights.GetLength(0);
            int width = heights.GetLength(1);
            if (width == 0 || height == 0) return;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    state.Heights[yBase + y, xBase + x] = Mathf.Clamp(heights[y, x], 0f, 1f);
                }
            }

            // The heightmap was imported with its first pixel at -TerrainSize / 2, in vertex spacing units,
            // and with its rows flipped (see ApplyHeightmap).
            var spacing = state.TerrainSize / state.Resolution;
            int origin = Mathf.RoundToInt(-state.TerrainSize * 0.5f / spacing);
            int rows = state.Heights.GetLength(0);
            var editedRect = new Rect2I(xBase, rows - yBase - height, width, height);
            int x0 = editedRect.Position.X + origin, y0 = editedRect.Position.Y + origin;
            int x1 = x0 + width - 1, y1 = y0 + height - 1;
            var editedRegions = new List<Terrain3DRegion>();
            int regionSize = 0;
            var firstRegion = state.Data.GetRegionp(new Godot.Vector3(x0 * spacing, 0, y0 * spacing));
            if (firstRegion != null) regionSize = (int)firstRegion.RegionSize;
            if (regionSize <= 0) return;
            state.RegionSize = regionSize;
            for (int ry = FloorDiv(y0, regionSize); ry <= FloorDiv(y1, regionSize); ry++) {
                for (int rx = FloorDiv(x0, regionSize); rx <= FloorDiv(x1, regionSize); rx++) {
                    var location = new Vector2I(rx, ry);
                    var region = state.Data.GetRegion(location);
                    if (region == null) continue;
                    var image = region.GetMap(Terrain3DRegion.MapType.Height);
                    if (image == null) continue;
                    int fromX = System.Math.Max(x0, rx * regionSize), toX = System.Math.Min(x1, (rx + 1) * regionSize - 1);
                    int fromY = System.Math.Max(y0, ry * regionSize), toY = System.Math.Min(y1, (ry + 1) * regionSize - 1);
                    for (int gy = fromY; gy <= toY; gy++) {
                        for (int gx = fromX; gx <= toX; gx++) {
                            var h = (state.Heights[rows - 1 - (gy - origin), gx - origin] - 0.5f) * 2f * state.MaxHeight;
                            image.SetPixel(gx - rx * regionSize, gy - ry * regionSize, new Godot.Color(h, 0, 0, 1));
                        }
                    }
                    region.CalcHeightRange();
                    region.Edited = true;
                    editedRegions.Add(region);
                }
            }
            if (editedRegions.Count == 0) return;
            state.DirtyRect = state.DirtyRect.Size.X == 0 ? editedRect : state.DirtyRect.Merge(editedRect);
            state.Data.CalcHeightRange(false);
            // Only regions flagged as edited are uploaded to the GPU.
            state.Data.UpdateMaps(Terrain3DRegion.MapType.Height, false, false);
            foreach (var region in editedRegions) region.Edited = false;
            SyncHeightmap(terrainObj);
        }

        // Updates the collision of the regions edited by SetHeightsDelayLOD, if not done already.
        // Terrain3DCollision.Update regenerates every region's shape (hundreds of ms on a 4096 terrain),
        // so only the heightmap shapes of the edited regions are rewritten here.
        public static void SyncHeightmap(Node3D terrainObj) {
            if (terrainObj is not Terrain3D terrain || !heightmaps.TryGetValue(terrainObj, out var state) || state.DirtyRect.Size.X == 0) return;
            var dirtyRect = state.DirtyRect;
            state.DirtyRect = new Rect2I();
            var collisionObj = terrain.Call("get_collision").AsGodotObject();
            if (collisionObj == null) return;
            var collision = Terrain3DCollision.Bind(collisionObj);
            if (!collision.IsEnabled()) return;
            if (collision.IsDynamicMode()) {
                collision.Update(true);
                return;
            }

            var body = collision.GetRid();
            var spacing = state.TerrainSize / state.Resolution;
            int origin = Mathf.RoundToInt(-state.TerrainSize * 0.5f / spacing);
            int regionSize = state.RegionSize;
            int shapeSize = regionSize + 1;
            float regionWorldSize = regionSize * spacing;
            int rows = state.Heights.GetLength(0);
            int shapeCount = PhysicsServer3D.BodyGetShapeCount(body);
            for (int i = 0; i < shapeCount; i++) {
                // Shapes are centered on their region.
                var center = PhysicsServer3D.BodyGetShapeTransform(body, i).Origin;
                var location = new Vector2I(Mathf.RoundToInt(center.X / regionWorldSize - 0.5f), Mathf.RoundToInt(center.Z / regionWorldSize - 0.5f));
                // A shape also covers the first row and column of the next regions.
                var shapeRect = new Rect2I(location.X * regionSize - origin, location.Y * regionSize - origin, shapeSize, shapeSize);
                if (!shapeRect.Intersects(dirtyRect)) continue;
                var updateRect = shapeRect.Intersection(dirtyRect);
                var shape = PhysicsServer3D.BodyGetShape(body, i);
                var data = PhysicsServer3D.ShapeGetData(shape).AsGodotDictionary();
                var shapeHeights = data["heights"].AsFloat32Array();
                if (shapeHeights.Length != shapeSize * shapeSize) {
                    collision.Update(false);
                    return;
                }
                for (int row = updateRect.Position.Y; row < updateRect.End.Y; row++) {
                    int z = row - shapeRect.Position.Y;
                    for (int column = updateRect.Position.X; column < updateRect.End.X; column++) {
                        int x = column - shapeRect.Position.X;
                        // Terrain3D stores the array rotated by -90 degrees and rotates the shape by +90 degrees.
                        shapeHeights[shapeSize - 1 - z + x * shapeSize] = (Mathf.Clamp(state.Heights[rows - 1 - row, column], 0f, 1f) - 0.5f) * 2f * state.MaxHeight;
                    }
                }
                float min = float.MaxValue, max = float.MinValue;
                foreach (var h in shapeHeights) {
                    if (float.IsNaN(h)) continue;
                    min = Mathf.Min(min, h);
                    max = Mathf.Max(max, h);
                }
                data["heights"] = shapeHeights;
                data["min_height"] = min;
                data["max_height"] = max;
                PhysicsServer3D.ShapeSetData(shape, data);
            }
        }

        static int FloorDiv(int a, int b) {
            return (int)System.Math.Floor((double)a / b);
        }
    }

    public static class Materials {
        public static Shader GetShader(string name) {
            // Shader names such as "Standard (Specular setup)" map to the base shader.
            var paren = name.IndexOf(" (");
            if (paren > 0) name = name.Substring(0, paren);
            var path = "res://" + (name.Contains('/') ? name : "Materials/" + name) + ".gdshader";
            var shader = ResourceLoader.Exists(path) ? GD.Load<Shader>(path) : null;
            return shader ?? GD.Load<Shader>("res://Materials/Standard.gdshader");
        }

        public static ShaderMaterial CreateMaterial(Shader shader, bool instancing = false) {
            var res = new ShaderMaterial();
            res.Shader = shader;
            //TODO: MISSING instancing
            return res;
        }

        public static void SetShaderParameter(ShaderMaterial material, string name, float value) {
            material.SetShaderParameter(name, value);
        }

        public static void SetShaderParameter(ShaderMaterial material, string name, Godot.Vector4 value) {
            material.SetShaderParameter(name, value);
        }

        public static void SetShaderParameter(ShaderMaterial material, string name, Godot.Vector3 value) {
            material.SetShaderParameter(name, value);
        }

        public static void SetShaderParameter(ShaderMaterial material, string name, Godot.Vector2 value) {
            material.SetShaderParameter(name, value);
        }

        public static void SetShaderParameter(ShaderMaterial material, string name, Godot.Color value) {
            material.SetShaderParameter(name, value);
        }

        public static void SetColor(ShaderMaterial material, Godot.Color value) {
            if (material == null) return;
            material.SetShaderParameter("_Color", value);
            material.SetShaderParameter("albedo", value);
            material.SetShaderParameter("tint", value);
        }

        public static Godot.Color GetColor(ShaderMaterial material) {
            var value = material?.GetShaderParameter("_Color") ?? default;
            return value.VariantType == Variant.Type.Color ? value.AsColor() : Color.white;
        }

        public static void EnableKeyword(ShaderMaterial material, string keyword) {
            material.SetShaderParameter("keyword_" + keyword, true); //TODO: MISSING in shader
        }

        public static void SetShaderParameter(ShaderMaterial material, string name, Texture2D value) {
            material.SetShaderParameter(name, value);
        }

        public static void SetMainTexture(ShaderMaterial material, Texture2D value) {
            if (material == null) return;
            material.SetShaderParameter("_MainTex", value);
            material.SetShaderParameter("texture_albedo", value);
            material.SetShaderParameter("texture", value);
        }

        // The tiling and offset of a texture are the name + "_ST" uniform (scale in xy, offset in zw)
        public static void SetTextureScale(ShaderMaterial material, string name, Godot.Vector2 value) {
            if (material == null) return;
            var current = material.GetShaderParameter(name + "_ST");
            var st = current.VariantType == Variant.Type.Vector4 ? current.AsVector4() : new Godot.Vector4(1, 1, 0, 0);
            material.SetShaderParameter(name + "_ST", new Godot.Vector4(value.X, value.Y, st.Z, st.W));
        }

        public static void SetName(ShaderMaterial material, string value) {
            if (material != null) material.ResourceName = value;
        }

        static readonly string[] standardBlendShaders = { "Standard", "StandardCutout", "StandardFade", "StandardTransparent" };

        public static void SetBlendMode(ShaderMaterial mat, int blendMode) {
            if (mat == null) return;
            // Godot chooses opaque / alpha scissor / alpha blend per shader at compile time,
            // so each rendering mode (0=opaque, 1=cutout, 2=fade, 3=transparent) is its own shader.
            var shader = GetShader(standardBlendShaders[Mathf.Clamp(blendMode, 0, standardBlendShaders.Length - 1)]);
            if (mat.Shader != shader) {
                // Carry over parameters that were set before the mode was chosen (color, main texture, ...).
                var values = new Dictionary<StringName, Variant>();
                if (mat.Shader != null) {
                    foreach (var uniform in mat.Shader.GetShaderUniformList()) {
                        var name = uniform.AsGodotDictionary()["name"].AsStringName();
                        var value = mat.GetShaderParameter(name);
                        if (value.VariantType != Variant.Type.Nil) values[name] = value;
                    }
                }
                mat.Shader = shader;
                foreach (var elem in values) mat.SetShaderParameter(elem.Key, elem.Value);
            }
            if (blendMode == 0) mat.SetShaderParameter("_Opacity", 1.0f);
        }
    }
}

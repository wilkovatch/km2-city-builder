using System.Collections;
using System.Collections.Generic;
using Godot;
using PhysicsScriptable = Godot.StaticBody3D;
using LineRenderer = GameEngine.LineRenderer;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

//The line along the curve of a road, shown (with the free road ends) while placing another road:
//clicking on it connects the other road to the middle of this one, which is split there
public partial class RoadSplitLine : PhysicsScriptable {
    public const int Layer = 14;
    const float MinColliderSize = 1.0f; //half size of the clickable tube around the line, at least the radius of the point links
    const float ColliderRebuildRatio = 0.3f; //change of the line width rebuilding the tube
    const float EndMargin = 1.0f; //minimum length of the road parts left at the ends
    const float HeightOffset = 0.05f; //above the road surface
    const float MinLineWidth = 0.25f;

    Road road;
    LineRenderer lr;
    CollisionShape3D collisionShape;
    bool dirty = true;
    float colliderSize = MinColliderSize; //as large as the line, which gets wider with the camera distance
    //the clipped curve of the road, with the curve segment and the position in it of each point
    readonly List<Vector3> points = new List<Vector3>();
    readonly List<(int segment, float fraction)> curvePositions = new List<(int, float)>();

    public static RoadSplitLine Create(Road road) {
        var res = GS.Create<RoadSplitLine>("SplitLine", road);
        res.road = road;
        res.TopLevel = true; //the collider uses world space positions, like the line
        res.collisionShape = new CollisionShape3D();
        res.AddChild(res.collisionShape);
        res.lr = GO.CreateLineRenderer(res);
        res.lr.material = MaterialManager.GetHandleMaterial(GameEngine.Color.cyan);
        res.lr.startWidth = MinLineWidth;
        res.lr.endWidth = MinLineWidth;
        res.lr.frameUpdate = res.UpdateWidth;
        GO.SetLayer(res, Layer);
        GO.SetEnabled(res, false);
        return res;
    }

    public void SetActive(bool active) {
        GO.SetEnabled(this, active);
        if (active && dirty) Rebuild();
    }

    //the curve changed (it is rebuilt now if the line is shown, otherwise when it is shown)
    public void MarkDirty() {
        dirty = true;
        if (GO.GetEnabled(this)) Rebuild();
    }

    void UpdateWidth() {
        var cam = GameEngine.Camera.Get(this);
        if (cam == null || points.Count < 2) return;
        var camPos = GO.GetPosition(cam);
        var dist = GameEngine.Vector3.Distance(GeometryHelper.ClosestPointToCurve(camPos, points, out _), camPos);
        var width = Mathf.Max(MinLineWidth * (dist / 20.0f), MinLineWidth);
        lr.startWidth = width;
        lr.endWidth = width;
        var size = Mathf.Max(MinColliderSize, width * 0.5f);
        if (Mathf.Abs(size - colliderSize) > colliderSize * ColliderRebuildRatio) {
            colliderSize = size;
            UpdateCollider();
        }
    }

    //the distance from the start of the curve where each end part covered by an intersection (plus the margin) ends
    float GetEndClip(Intersection intersection) {
        var res = EndMargin;
        if (intersection != null) {
            var idx = intersection.roads.IndexOf(road);
            if (idx >= 0 && idx < intersection.sizes.Count) res += intersection.sizes[idx];
        }
        return res;
    }

    void Rebuild() {
        dirty = false;
        points.Clear();
        curvePositions.Clear();
        var curve = road.curvePoints;
        if (curve.Count >= 2) {
            var lengths = new List<float> { 0.0f };
            for (int i = 0; i < curve.Count - 1; i++) {
                lengths.Add(lengths[i] + GameEngine.Vector3.Distance(curve[i], curve[i + 1]));
            }
            var total = lengths[curve.Count - 1];
            var from = GetEndClip(road.startIntersection);
            var to = total - GetEndClip(road.endIntersection);
            if (to > from) {
                void AddAt(int segment, float distance) {
                    var segLength = lengths[segment + 1] - lengths[segment];
                    var fraction = segLength > 0 ? Mathf.Clamp((distance - lengths[segment]) / segLength, 0.0f, 1.0f) : 0.0f;
                    points.Add(GameEngine.Vector3.Lerp(curve[segment], curve[segment + 1], fraction));
                    curvePositions.Add((segment, fraction));
                }
                for (int i = 0; i < curve.Count - 1; i++) {
                    if (lengths[i + 1] < from || lengths[i] > to) continue;
                    if (points.Count == 0) AddAt(i, from);
                    if (lengths[i + 1] < to) AddAt(i, lengths[i + 1]);
                    else {
                        AddAt(i, to);
                        break;
                    }
                }
            }
        }
        if (points.Count < 2) {
            points.Clear();
            curvePositions.Clear();
        }

        var linePoints = new Vector3[points.Count];
        for (int i = 0; i < points.Count; i++) linePoints[i] = points[i] + GameEngine.Vector3.up * HeightOffset;
        lr.SetPositions(linePoints);
        UpdateCollider();
    }

    //a square tube around the line
    void UpdateCollider() {
        if (points.Count < 2) {
            collisionShape.Shape = null;
            collisionShape.Disabled = true;
            return;
        }
        var faces = new List<Vector3>();
        for (int i = 0; i < points.Count - 1; i++) {
            var p0 = GameEngine.Vector3.DecodePosition(points[i]);
            var p1 = GameEngine.Vector3.DecodePosition(points[i + 1]);
            var dir = p1 - p0;
            if (dir.LengthSquared() < 1e-8f) continue;
            dir = dir.Normalized();
            var side = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : dir.Cross(Vector3.Up).Normalized();
            var up = side.Cross(dir).Normalized();
            side *= colliderSize;
            up *= colliderSize;
            var corners = new[] { side + up, -side + up, -side - up, side - up };
            for (int c = 0; c < 4; c++) {
                var a = corners[c];
                var b = corners[(c + 1) % 4];
                faces.Add(p0 + a);
                faces.Add(p1 + a);
                faces.Add(p1 + b);
                faces.Add(p0 + a);
                faces.Add(p1 + b);
                faces.Add(p0 + b);
            }
        }
        if (faces.Count == 0) {
            collisionShape.Shape = null;
            collisionShape.Disabled = true;
            return;
        }
        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(faces.ToArray());
        collisionShape.Shape = shape;
        collisionShape.Disabled = false;
    }

    public Road GetRoad() {
        return road;
    }

    //the point of the line closest to the given one: the curve segment of the road, the position in it and the point
    public (int segment, float fraction, Vector3 point)? Resolve(Vector3 point) {
        if (dirty) Rebuild();
        if (points.Count < 2) return null;
        GeometryHelper.ClosestPointToCurve(point, points, out var minI);
        if (minI < 0) return null;
        var t = GeometryHelper.ClosestPointFactor(point, points[minI], points[minI + 1]);
        var (seg0, f0) = curvePositions[minI];
        var (seg1, f1) = curvePositions[minI + 1];
        //the points after the first one are at the end of their curve segment (or at the clipped end of the line),
        //so a piece of the line starting on a previous segment starts at the beginning of this one
        if (seg1 != seg0) f0 = 0.0f;
        var fraction = Mathf.Lerp(f0, f1, t);
        return (seg1, fraction, GameEngine.Vector3.Lerp(points[minI], points[minI + 1], t));
    }
}

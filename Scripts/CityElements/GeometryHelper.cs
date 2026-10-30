using System.Collections;
using System.Collections.Generic;

using Godot;
using LineRenderer = GameEngine.LineRenderer;

public static class GeometryHelper {
    [System.Serializable]
    public enum CurveType {
        Bezier,
        Hermite,
        LowPoly
    }

    public const float epsilon = 0.0001f;

    public static bool AreVectorsEqual(Vector2 v1, Vector2 v2) {
        return GameEngine.Vector2.SqrMagnitude(v2 - v1) <= epsilon;
    }

    public static bool AreVectorsEqual(Vector3 v1, Vector3 v2) {
        return GameEngine.Vector3.SqrMagnitude(v2 - v1) <= epsilon;
    }

    public static int FindVector(List<Vector3> list, Vector3 item) {
        for (int i = 0; i < list.Count; i++) {
            if (AreVectorsEqual(list[i], item)) return i;
        }
        return -1;
    }

    public static float TriangleArea(Vector3 v1, Vector3 v2, Vector3 v3) {
        var a = GameEngine.Vector3.Magnitude(v1 - v2);
        var b = GameEngine.Vector3.Magnitude(v1 - v3);
        var c = GameEngine.Vector3.Magnitude(v2 - v3);
        var s = (a + b + c) * 0.5f;
        return Mathf.Sqrt(s * (s - a) * (s - b) * (s - c));
    }

    public static Vector3 TriangleNormal(Vector3 v1, Vector3 v2, Vector3 v3) {
        var a = v2 - v1;
        var b = v3 - v1;
        return GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(a, b));
    }

    public static (int[] Triangles, DelaunatorSharp.IPoint[] Points) GetDelaunayTriangulation(List<Vector3> vertices) {
        var dPoints = new List<DelaunatorSharp.IPoint>();
        foreach (var point in vertices) {
            dPoints.Add(new DelaunatorSharp.Point(point[0], point[2]));
        }
        try {
            var res = new DelaunatorSharp.Delaunator(dPoints.ToArray());
            return (res.Triangles, res.Points);
        } catch {
            return (new int[] { }, new DelaunatorSharp.IPoint[] { });
        }
    }

    static float Vector2Cross(Vector2 v1, Vector2 v2) {
        return v1[0] * v2[1] - v1[1] * v2[0];
    }

    public static Vector3[] GetLinePath(LineRenderer lr) {
        var path = new Vector3[lr.positionCount];
        lr.GetPositions(path);
        return path;
    }

    public static float GetPathLength(Vector3[] path) {
        var curPos = 0.0f;
        for (int i = 0; i < path.Length - 1; i++) {
            var p0 = path[i];
            var p1 = path[i + 1];
            var dist = GameEngine.Vector3.Distance(p0, p1);
            curPos += dist;
        }
        return curPos;
    }

    public static Vector3 GetPointOnPath(Vector3[] path, float pos) {
        if (path.Length == 0) return GameEngine.Vector3.zero;
        float lastCurPos;
        var curPos = 0.0f;
        for (int i = 0; i < path.Length - 1; i++) {
            lastCurPos = curPos;
            var p0 = path[i];
            var p1 = path[i + 1];
            var dist = GameEngine.Vector3.Distance(p0, p1);
            curPos += dist;
            if (curPos > pos) {
                var posHere = pos - lastCurPos;
                var alpha = posHere / dist;
                return GameEngine.Vector3.Lerp(p0, p1, alpha);
            }
        }
        return path[path.Length - 1];
    }

    public static (Vector3 pos, Vector3 dir) GetPointAndDirOnSidewalk(Vector3[] pathIn, Vector3[] pathOut, Vector3[] pathMid, float x, float z) {
        var pMid = GetPointOnPath(pathMid, z);
        var pIn = ClosestPointToCurve(pMid, new List<Vector3>(pathIn), out _);
        var pOut = ClosestPointToCurve(pMid, new List<Vector3>(pathOut), out _);
        var dir = GameEngine.Vector3.Normalize(pIn - pOut);
        return (GameEngine.Vector3.Lerp(pIn, pOut, x), dir);
    }

    static bool DoesRayIntersectSegment(Vector2 point, Vector2 direction, Vector2 p1, Vector2 p2) {
        var v1 = point - p1;
        var v2 = p2 - p1;
        var v3 = new Vector2(-direction[1], direction[0]);
        var dot = GameEngine.Vector2.Dot(v2, v3);
        if (Mathf.Abs(dot) < epsilon) return false;
        var t1 = Vector2Cross(v2, v1) / GameEngine.Vector2.Dot(v2, v3);
        var t2 = GameEngine.Vector2.Dot(v1, v3) / GameEngine.Vector2.Dot(v2, v3);
        return t1 >= 0 && t2 >= 0 && t2 < 1; //exclude ==1 because we are testing consecutive segments
    }

    public static bool IsPointInsidePolygon(Vector3 point, List<Vector3> polygon) {
        int count = 0;
        var pointb = new Vector2(point[0], point[2]);
        for (int i = 0; i < polygon.Count; i++) {
            var p1 = polygon[i];
            var p2 = polygon[(i < polygon.Count - 1) ? (i + 1) : 0];
            var p1b = new Vector2(p1[0], p1[2]);
            var p2b = new Vector2(p2[0], p2[2]);
            count += DoesRayIntersectSegment(pointb, GameEngine.Vector2.right, p1b, p2b) ? 1 : 0;
        }
        return count % 2 != 0;
    }


    public static bool IsPolygonClockwise(List<Vector3> polygon) {
        var sum = 0.0f;
        for (int i = 0; i < polygon.Count; i++) {
            var p1 = polygon[i];
            var p2 = polygon[(i < polygon.Count - 1) ? (i + 1) : 0];
            sum += (p2[0] - p1[0]) * (p2[2] + p1[2]);
        }
        return sum > 0;
    }

    static List<Vector3> GetPolygonNormals(List<Vector3> polygon) {
        var clockwise = IsPolygonClockwise(polygon);
        var mult = clockwise ? 1.0f : -1.0f;
        var res = new List<Vector3>();
        for (int i = 0; i < polygon.Count; i++) {
            var p0 = polygon[(i == 0) ? (polygon.Count - 1) : (i - 1)];
            var p1 = polygon[i];
            var p2 = polygon[(i < polygon.Count - 1) ? (i + 1) : 0];
            var d1 = p2 - p1;
            var d2 = p1 - p0;
            var n1 = GameEngine.Vector3.Cross(d1, GameEngine.Vector3.up);
            var n2 = GameEngine.Vector3.Cross(d2, GameEngine.Vector3.up);
            var n = GameEngine.Vector3.Normalize((n1 + n2) * 0.5f) * mult;
            res.Add(n);
        }
        return res;
    }

    public static List<Vector3> GetOuterPolygon(List<Vector3> polygon, float distance) {
        var normals = GetPolygonNormals(polygon);
        var res = new List<Vector3>();
        for (int i = 0; i < polygon.Count; i++) {
            res.Add(polygon[i] + normals[i] * distance);
        }
        return res;
    }

    //the end tangents (along the curve) are imposed to the curve when given: as the tangents of the end spans of a
    //Hermite curve (times the tension, as the tangents between its spans), only their directions for a Bézier curve, with
    //a hidden control point next to the end (at the distance keeping the speed of the curve there), not at all for a
    //low-poly curve
    public static Vector3 GetPointOnCurve(Vector3 start, List<Vector3> controlPoints, Vector3 end, float alpha, int alphaI, CurveType curveType, float hermiteTension = 0.5f, bool subdivideEqually = false,
        Vector3? startTangent = null, Vector3? endTangent = null) {

        if (AreVectorsEqual(start, end)) return start;
        var tempPoints = new List<Vector3> { start };
        tempPoints.AddRange(controlPoints);
        tempPoints.Add(end);
        switch (curveType) {
            case CurveType.Bezier:
                tempPoints = WithHiddenBezierPoints(tempPoints, startTangent, endTangent);
                var intermediatePoints = new List<Vector3>();
                while (tempPoints.Count > 1) {
                    for (int i = 0; i < tempPoints.Count - 1; i++) {
                        intermediatePoints.Add(tempPoints[i] + (tempPoints[i + 1] - tempPoints[i]) * alpha);
                    }
                    tempPoints.Clear();
                    tempPoints.AddRange(intermediatePoints);
                    intermediatePoints.Clear();
                }
                return tempPoints[0];
            case CurveType.Hermite:
                var segmentCount = tempPoints.Count - 1;
                var span = GetHermiteSpan(tempPoints, alpha, subdivideEqually, out var a);
                if (span >= 0) {
                    var a2 = a * a;
                    var a3 = a2 * a;

                    var p0 = span < 1 ? start : tempPoints[span - 1];
                    var p1 = tempPoints[span];
                    var p2 = tempPoints[span + 1];
                    var p3 = span > (segmentCount - 2) ? end : tempPoints[span + 2];

                    var t1 = hermiteTension * (p2 - p0);
                    var t2 = hermiteTension * (p3 - p1);
                    if (span == 0 && startTangent.HasValue) t1 = hermiteTension * startTangent.Value;
                    if (span == segmentCount - 1 && endTangent.HasValue) t2 = hermiteTension * endTangent.Value;

                    var b1 = 2 * a3 - 3 * a2 + 1;
                    var b2 = -2 * a3 + 3 * a2;
                    var b3 = a3 - 2 * a2 + a;
                    var b4 = a3 - a2;

                    var res = b1 * p1 + b2 * p2 + b3 * t1 + b4 * t2;
                    if (float.IsNaN(res[0]) || float.IsNaN(res[1]) || float.IsNaN(res[2])) res = GameEngine.Vector3.zero;
                    return res;
                }
                break;
            case CurveType.LowPoly:
                int fixedAlphaI = Mathf.Clamp(alphaI, 0, tempPoints.Count - 1);
                return tempPoints[fixedAlphaI];
        }
        return GameEngine.Vector3.zero;
    }

    //a value given for each point of a curve (start, control points, end) at the curve parameter, blended as the
    //curve blends the points: Bernstein weights (Bézier), Hermite interpolation through the values (Hermite), the value
    //of the point (low-poly). The Hermite spans are found on the points, so that subdivideEqually uses their distances.
    //The hidden control points of the end tangents (see GetPointOnCurve) take the values of the ends.
    public static Vector2 GetValueOnCurve(Vector3 start, List<Vector3> controlPoints, Vector3 end, List<Vector2> values, float alpha, int alphaI,
        CurveType curveType, float hermiteTension = 0.5f, bool subdivideEqually = false, Vector3? startTangent = null, Vector3? endTangent = null) {

        if (values == null || values.Count == 0) return GameEngine.Vector2.zero;
        if (values.Count != controlPoints.Count + 2) return GameEngine.Vector2.zero;
        if (AreVectorsEqual(start, end)) return values[0];
        switch (curveType) {
            case CurveType.Bezier:
                var level = new List<Vector2>(values);
                if (startTangent.HasValue) level.Insert(1, values[0]);
                if (endTangent.HasValue) level.Insert(level.Count - 1, values[values.Count - 1]);
                while (level.Count > 1) {
                    for (int i = 0; i < level.Count - 1; i++) {
                        level[i] = level[i] + (level[i + 1] - level[i]) * alpha;
                    }
                    level.RemoveAt(level.Count - 1);
                }
                return level[0];
            case CurveType.Hermite:
                var tempPoints = new List<Vector3> { start };
                tempPoints.AddRange(controlPoints);
                tempPoints.Add(end);
                var segmentCount = tempPoints.Count - 1;
                var span = GetHermiteSpan(tempPoints, alpha, subdivideEqually, out var a);
                if (span >= 0) {
                    var a2 = a * a;
                    var a3 = a2 * a;

                    var v0 = span < 1 ? values[0] : values[span - 1];
                    var v1 = values[span];
                    var v2 = values[span + 1];
                    var v3 = span > (segmentCount - 2) ? values[values.Count - 1] : values[span + 2];

                    var t1 = hermiteTension * (v2 - v0);
                    var t2 = hermiteTension * (v3 - v1);

                    var b1 = 2 * a3 - 3 * a2 + 1;
                    var b2 = -2 * a3 + 3 * a2;
                    var b3 = a3 - 2 * a2 + a;
                    var b4 = a3 - a2;

                    return b1 * v1 + b2 * v2 + b3 * t1 + b4 * t2;
                }
                break;
            case CurveType.LowPoly:
                return values[Mathf.Clamp(alphaI, 0, values.Count - 1)];
        }
        return GameEngine.Vector2.zero;
    }

    //the control polygon of a Bézier curve with a hidden control point next to each end with a given tangent: along it,
    //at the distance keeping the speed of the curve at the end (the degree grows with each hidden point)
    static List<Vector3> WithHiddenBezierPoints(List<Vector3> points, Vector3? startTangent, Vector3? endTangent) {
        if (!startTangent.HasValue && !endTangent.HasValue) return points;
        var degree = points.Count - 1;
        var newDegree = degree + (startTangent.HasValue ? 1 : 0) + (endTangent.HasValue ? 1 : 0);
        var factor = degree / (float)newDegree;
        var res = new List<Vector3>(points);
        var first = points[0];
        var last = points[points.Count - 1];
        if (startTangent.HasValue) {
            var dir = GameEngine.Vector3.Normalize(startTangent.Value);
            res.Insert(1, first + dir * (GameEngine.Vector3.Magnitude(points[1] - first) * factor));
        }
        if (endTangent.HasValue) {
            var dir = GameEngine.Vector3.Normalize(endTangent.Value);
            res.Insert(res.Count - 1, last - dir * (GameEngine.Vector3.Magnitude(last - points[points.Count - 2]) * factor));
        }
        return res;
    }

    //the curve parameter each point of a curve (start, control points, end) is associated with: where a Hermite or
    //low-poly curve passes through it, where its Bernstein weight peaks for a Bézier curve
    public static List<float> GetPointParameters(List<Vector3> points, CurveType curveType, bool subdivideEqually) {
        if (curveType == CurveType.Hermite && points.Count > 1) return GetHermiteKnots(points, subdivideEqually);
        var res = new List<float>();
        for (int i = 0; i < points.Count; i++) res.Add(points.Count > 1 ? i / (points.Count - 1.0f) : 0.0f);
        return res;
    }

    public const float MaxSectionYaw = 75.0f;
    public const float MaxSectionRoll = 75.0f;
    public const float MaxIntersectionYaw = 60.0f; //of a road end on an intersection

    //the right and up vectors of a road section rotated by the yaw (degrees, around the vertical axis, positive to the
    //right) and then the roll (degrees, around the horizontal forward direction, positive raises the right side) of
    //its control points. The right vector (pointing left of the direction of the road, as in the generators) keeps its
    //length divided by the cosine of the yaw, so that the road keeps its width measured across it.
    public static (Vector3 right, Vector3 up) RotateSection(Vector3 right, float yaw, float roll) {
        var up = GameEngine.Vector3.up;
        if (yaw == 0 && roll == 0) return (right, up);
        var length = GameEngine.Vector3.Magnitude(right);
        var flatRight = new Vector3(right[0], 0, right[2]);
        if (length <= 0 || GameEngine.Vector3.Magnitude(flatRight) <= 0) return (right, up);
        var r = GameEngine.Vector3.Normalize(flatRight);
        var forward = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(up, r));
        yaw = Mathf.Clamp(yaw, -MaxSectionYaw, MaxSectionYaw);
        roll = Mathf.Clamp(roll, -MaxSectionRoll, MaxSectionRoll);
        var yawRad = yaw * GameEngine.Math.deg2rad;
        var rollRad = roll * GameEngine.Math.deg2rad;
        //roll around the forward direction first, then yaw around the vertical axis (the yawed forward stays horizontal)
        var newRight = r.Rotated(forward, rollRad).Rotated(up, yawRad);
        var newUp = up.Rotated(forward, rollRad).Rotated(up, yawRad);
        return (newRight * (length / Mathf.Cos(yawRad)), newUp);
    }

    //the rotation (program coordinates) of the frame of a section of a road with the given direction: X to the right of
    //the road, Y up, Z forward, all horizontal but Y
    public static Quaternion GetSectionFrame(Vector3 direction) {
        var up = GameEngine.Vector3.up;
        var flat = new Vector3(direction[0], 0, direction[2]);
        if (GameEngine.Vector3.Magnitude(flat) <= epsilon) return Quaternion.Identity;
        var forward = GameEngine.Vector3.Normalize(flat);
        var trueRight = GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(up, forward));
        return new Basis(trueRight, up, forward).GetRotationQuaternion();
    }

    //the span of a Hermite curve through the points containing alpha (-1 if none), and the position in it
    public static int GetHermiteSpan(List<Vector3> points, float alpha, bool subdivideEqually, out float localAlpha) {
        var factor = 1.0f / (points.Count - 1);
        var segmentCount = points.Count - 1;
        var factors = new List<float>();
        var starts = new List<float>();
        var ends = new List<float>();
        var max = 1.0f;
        if (subdivideEqually) {
            max = 0.0f;
            for (int i = 0; i < segmentCount; i++) {
                var f = GameEngine.Vector3.Magnitude(points[i + 1] - points[i]);
                factors.Add(f);
                starts.Add(max);
                max += f;
                ends.Add(max);
            }
            alpha *= max;
        }
        for (int i = 0; i < segmentCount; i++) {
            var startI = subdivideEqually ? starts[i] : ((float)i / segmentCount);
            var endI = subdivideEqually ? ends[i] : ((float)(i + 1) / segmentCount);
            var factorI = subdivideEqually ? factors[i] : factor;
            var match = (alpha <= 0 && i == 0) || (alpha >= max && i == (segmentCount - 1)) || (alpha >= startI && alpha < endI);
            if (match) {
                localAlpha = (alpha - startI) / factorI;
                return i;
            }
        }
        localAlpha = 0.0f;
        return -1;
    }

    //the curve parameter (alpha) of each point of a Hermite curve
    public static List<float> GetHermiteKnots(List<Vector3> points, bool subdivideEqually) {
        var res = new List<float>();
        var segmentCount = points.Count - 1;
        if (!subdivideEqually) {
            for (int i = 0; i <= segmentCount; i++) res.Add((float)i / segmentCount);
            return res;
        }
        var lengths = new List<float> { 0.0f };
        for (int i = 0; i < segmentCount; i++) {
            lengths.Add(lengths[i] + GameEngine.Vector3.Magnitude(points[i + 1] - points[i]));
        }
        var max = lengths[segmentCount];
        for (int i = 0; i <= segmentCount; i++) res.Add(max > 0 ? lengths[i] / max : (float)i / segmentCount);
        return res;
    }

    //splits a Bézier curve at alpha (de Casteljau), the two control polygons describe exactly the two parts of the curve
    public static (List<Vector3> left, List<Vector3> right) SplitBezier(List<Vector3> controlPolygon, float alpha) {
        var left = new List<Vector3>();
        var right = new List<Vector3>();
        var level = new List<Vector3>(controlPolygon);
        while (level.Count > 0) {
            left.Add(level[0]);
            right.Insert(0, level[level.Count - 1]);
            var next = new List<Vector3>();
            for (int i = 0; i < level.Count - 1; i++) {
                next.Add(level[i] + (level[i + 1] - level[i]) * alpha);
            }
            level = next;
        }
        return (left, right);
    }

    public static List<Vector3> LowPassFilter(List<Vector3> curvePoints, int iterations, int segments) {
        iterations = Mathf.Clamp(iterations, 0, segments); //to avoid exceptions when setting an invalid value
        List<Vector3> curvePoints2 = new List<Vector3>(curvePoints);
        for (int i = 1; i < segments - 1; ++i) {
            var p = GameEngine.Vector3.zero;
            var lpfStart = i - iterations;
            var lpfEnd = i + iterations;
            var div = lpfEnd - lpfStart + 1;
            for (int j = lpfStart; j <= lpfEnd; j++) {
                if (j < 0) {
                    var firstP = curvePoints[0];
                    var nextP = curvePoints[-j];
                    p += firstP - (nextP - firstP);
                } else if (j >= segments) {
                    var lastI = segments - 1;
                    var lastP = curvePoints[lastI];
                    var nextP = curvePoints[lastI - (j - lastI)];
                    p += lastP + (lastP - nextP);
                } else {
                    p += curvePoints[j];
                }
            }
            p /= div;
            curvePoints2[i] = p;
        }
        return curvePoints2;
    }

    public static float ClosestPointFactor(Vector3 point, Vector3 lineStart, Vector3 lineEnd) {
        var sqrLength = GameEngine.Vector3.SqrMagnitude(lineEnd - lineStart);
        if (sqrLength == 0.0f) return 0.0f; //degenerate segment, avoid NaN
        return Mathf.Clamp(GameEngine.Vector3.Dot(point - lineStart, lineEnd - lineStart) / sqrLength, 0f, 1f);
    }

    public static Vector3 ClosestPoint(Vector3 point, Vector3 lineStart, Vector3 lineEnd) {
        var t = ClosestPointFactor(point, lineStart, lineEnd);
        return lineStart + t * (lineEnd - lineStart);
    }

    public static Vector3 ClosestPointToCurve(Vector3 point, List<Vector3> curvePoints, out int minI) {
        var minDist = float.MaxValue;
        var minP = GameEngine.Vector3.zero;
        minI = -1;
        for (int i = 0; i < curvePoints.Count - 1; i++) {
            var p0 = curvePoints[i];
            var p1 = curvePoints[i + 1];
            var p = ClosestPoint(point, p0, p1);
            var d = GameEngine.Vector3.SqrMagnitude(point - p);
            if (d < minDist) {
                minI = i;
                minDist = d;
                minP = p;
            }
        }
        return minP;
    }

    public static Vector3 ProjectPoint(Vector3 pos, int layerMask = 1 << 3, CollisionObject3D exclude = null) {
        return ProjectOnSurface(pos, layerMask, exclude);
    }

    //The core can treat the heightmap as a guide: first use city surfaces, even below it, then the heightmap.
    //Explicit heightmap-only queries keep their meaning; additional mesh targets belong to mixed/surface queries.
    static (int primary, int fallback) SurfaceProjectionMasks(int layerMask) {
        const int terrainMask = 1 << 3;
        if ((layerMask & ~terrainMask) == 0) return (layerMask, 0);
        layerMask |= GameEngine.Raycast.ProjectionSurfaceMask;
        if ((layerMask & terrainMask) != 0 && CoreManager.Get("surfaceProjectionTerrainFallbackOnly", false)) {
            return (layerMask & ~terrainMask, terrainMask);
        }
        return (layerMask, 0);
    }

    public static Vector3 ProjectOnSurface(Vector3 pos, int layerMask, CollisionObject3D exclude = null) {
        var masks = SurfaceProjectionMasks(layerMask);
        if (GameEngine.Raycast.TryProjectPoint(pos, out var surface, masks.primary, exclude)) return surface;
        return GameEngine.Raycast.ProjectPoint(pos, masks.fallback, exclude);
    }

    //Use the same policy along the mouse ray, so the preview and click agree even with an oblique camera.
    public static bool RaycastSurface(Node3D parent, Vector3 origin, Vector3 direction, out GameEngine.Raycast.RaycastHit hit,
        float distance, int layerMask, CollisionObject3D exclude = null) {
        var masks = SurfaceProjectionMasks(layerMask);
        if (GameEngine.Raycast.DoRaycast(parent, origin, direction, out hit, distance, masks.primary, true, exclude)) return true;
        return masks.fallback != 0 && GameEngine.Raycast.DoRaycast(parent, origin, direction, out hit, distance, masks.fallback, true, exclude);
    }

    //Sort vertices in clockwise order and return the indices
    public static List<int> SortClockwise(List<Vector3> inputList, Vector3 center) {
        var todo = new List<int>();
        var inputList2 = new List<Vector3>();
        for (int i = 0; i < inputList.Count; i++) {
            inputList2.Add(inputList[i] - center);
            todo.Add(i);
        }
        float GetAngle(int index) {
            var v = inputList2[index];
            return Mathf.Atan2(v[2], v[0]);
        }
        todo.Sort((a, b) => GetAngle(b).CompareTo(GetAngle(a)));
        while (todo[0] != 0) {
            todo = ScrollSortedClockwise(todo);
        }
        return todo;
    }

    static List<int> ScrollSortedClockwise(List<int> input) {
        var res = new List<int>();
        for (int i = 1; i < input.Count; i++) {
            res.Add(input[i]);
        }
        res.Add(input[0]);
        return res;
    }

    public static Vector3 GetLineIntersection(Vector3 p1a, Vector3 p1b, Vector3 p2a, Vector3 p2b) {
        var A1 = p1a[2] - p1b[2];
        var B1 = p1b[0] - p1a[0];
        var C1 = p1a[0] * p1b[2] - p1b[0] * p1a[2];

        var A2 = p2a[2] - p2b[2];
        var B2 = p2b[0] - p2a[0];
        var C2 = p2a[0] * p2b[2] - p2b[0] * p2a[2];

        float delta = A1 * B2 - A2 * B1;

        if (delta == 0) return (p1a + p2a) * 0.5f;

        float x = (B2 * C1 - B1 * C2) / delta;
        float y = (A1 * C2 - A2 * C1) / delta;

        return new Vector3(-x, (p1b[1] + p2b[1]) / 2, -y);
    }

    static Vector2 V3to2(Vector3 v) {
        return new Vector2(v[0], v[2]);
    }

    public static List<Vector3> GetLineWithoutIntersections(List<Vector3> line) {
        var res = new List<Vector3>(line);
        if (res.Count <= 3) return res;
        for (int i = 0; i < res.Count - 1; i++) {
            var intersectionI = -1;
            var intersectionPoint = GameEngine.Vector3.zero;
            for (int j = i + 2; j < res.Count - 1; j++) {
                var intersects = LineSegmentsIntersection.Math2d.LineSegmentsIntersection(V3to2(res[i]), V3to2(res[i + 1]), V3to2(res[j]), V3to2(res[j + 1]), out Vector2 intersectionPoint2);
                if (intersects) {
                    intersectionI = j;
                    intersectionPoint = new Vector3(intersectionPoint2[0], (res[i][1] + res[i + 1][1] + res[j][1] + res[j + 1][1]) / 4, intersectionPoint2[1]);
                    break;
                }
            }
            if (intersectionI > -1) {
                res.RemoveRange(i + 1, intersectionI - i);
                res.Insert(i + 1, intersectionPoint);
            }
        }
        return res;
    }

    public static int[] GetSectionIndices(int i, int n, int sectionIndex = 0) {
        i += sectionIndex * n;
        return new int[] {
            i, i+1, i+1+n,
            i, i+1+n, i+n
        };
    }

    public static int[] GetSectionIndicesRev(int i, int n, int sectionIndex = 0) {
        i += sectionIndex * n;
        return new int[] {
            i+n, i+1+n, i,
            i+1+n, i+1, i
        };
    }

    static float PointSegmentSquaredDistance(Vector2 p, Vector2 s0, Vector2 s1) {
        var length = GameEngine.Vector2.SqrMagnitude(s0 - s1);
        if (length == 0.0) return GameEngine.Vector2.SqrMagnitude(p - s0);
        var dot = GameEngine.Vector2.Dot(p - s0, s1 - s0) / length;
        var t = Mathf.Clamp(dot, 0f, 1f);
        var proj = s0 + t * (s1 - s0);
        return GameEngine.Vector2.SqrMagnitude(p - proj);
    }

    public static float PointPolygonSquaredDistance(Vector3 point, List<Vector3> polygon, bool loop = true) {
        var minDist = float.MaxValue;
        var point_2 = new Vector2(point[0], point[2]);
        for (int i = 0; i < polygon.Count; i++) {
            if (!loop && i == 0) continue;
            var i0 = i == 0 ? polygon.Count - 1 : i - 1;
            var p0 = polygon[i0];
            var p1 = polygon[i];
            var p0_2 = new Vector2(p0[0], p0[2]);
            var p1_2 = new Vector2(p1[0], p1[2]);
            var d = PointSegmentSquaredDistance(point_2, p0_2, p1_2);
            if (d < minDist) minDist = d;
        }
        return minDist;
    }

    public static void SmoothMeshConstrained(List<Vector3> vertices, List<int> tris, List<Vector3> perimeterPoints, int smooth) {
        var neighbors = new List<List<int>>();
        var startI = perimeterPoints.Count;
        for (int i = startI; i < vertices.Count; i++) {
            var list = new List<int>();
            for (int t = 0; t < tris.Count; t += 3) {
                var v1 = tris[t];
                var v2 = tris[t + 1];
                var v3 = tris[t + 2];
                if (i == v1) {
                    if (!list.Contains(v2)) list.Add(v2);
                    if (!list.Contains(v3)) list.Add(v3);
                } else if (i == v2) {
                    if (!list.Contains(v1)) list.Add(v1);
                    if (!list.Contains(v3)) list.Add(v3);
                } else if (i == v3) {
                    if (!list.Contains(v1)) list.Add(v1);
                    if (!list.Contains(v2)) list.Add(v2);
                }
            }
            neighbors.Add(list);
        }
        for (int n = 0; n < smooth; n++) {
            List<Vector3> averages = new List<Vector3>();
            for (int i = startI; i < vertices.Count; i++) {
                var avg = GameEngine.Vector3.zero;
                var list = neighbors[i - startI];
                foreach (var neighbor in list) {
                    avg += vertices[neighbor];
                }
                avg /= list.Count;
                if (list.Count == 0) avg = vertices[i];
                averages.Add(avg);
            }
            for (int i = startI; i < vertices.Count; i++) {
                var v = vertices[i];
                vertices[i] = new Vector3(v[0], averages[i - startI][1], v[2]);
            }
        }
    }

    public static (List<Vector3> vertices, List<int> indices) GetCube(float scale) {
        static int[] GetFace(int a, int b, int c, int d) {
            return new int[6] { a, b, c, a, c, d };
        }
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        var pivot = new Vector3(0.0f, 0.5f, 0.0f) * scale;
        vertices.Add(new Vector3(0.5f, 0.5f, 0.5f) * scale + pivot);
        vertices.Add(new Vector3(0.5f, 0.5f, -0.5f) * scale + pivot);
        vertices.Add(new Vector3(-0.5f, 0.5f, -0.5f) * scale + pivot);
        vertices.Add(new Vector3(-0.5f, 0.5f, 0.5f) * scale + pivot);
        vertices.Add(new Vector3(0.5f, -0.5f, 0.5f) * scale + pivot);
        vertices.Add(new Vector3(0.5f, -0.5f, -0.5f) * scale + pivot);
        vertices.Add(new Vector3(-0.5f, -0.5f, -0.5f) * scale + pivot);
        vertices.Add(new Vector3(-0.5f, -0.5f, 0.5f) * scale + pivot);
        indices.AddRange(GetFace(0, 1, 2, 3));
        indices.AddRange(GetFace(7, 6, 5, 4));
        indices.AddRange(GetFace(4, 5, 1, 0));
        indices.AddRange(GetFace(6, 7, 3, 2));
        indices.AddRange(GetFace(5, 6, 2, 1));
        indices.AddRange(GetFace(7, 4, 0, 3));
        return (vertices, indices);
    }
}

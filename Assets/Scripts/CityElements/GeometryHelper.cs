using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class GeometryHelper {
    [System.Serializable]
    public enum CurveType {
        Bezier,
        Hermite,
        LowPoly
    }

    public const float epsilon = 0.0001f;

    public static bool AreVectorsEqual(Vector2 v1, Vector2 v2) {
        return (v2 - v1).sqrMagnitude <= epsilon;
    }

    public static bool AreVectorsEqual(Vector3 v1, Vector3 v2) {
        return (v2 - v1).sqrMagnitude <= epsilon;
    }

    public static int FindVector(List<Vector3> list, Vector3 item) {
        for (int i = 0; i < list.Count; i++) {
            if (AreVectorsEqual(list[i], item)) return i;
        }
        return -1;
    }

    public static float TriangleArea(Vector3 v1, Vector3 v2, Vector3 v3) {
        var a = (v1 - v2).magnitude;
        var b = (v1 - v3).magnitude;
        var c = (v2 - v3).magnitude;
        var s = (a + b + c) * 0.5f;
        return Mathf.Sqrt(s * (s - a) * (s - b) * (s - c));
    }

    public static Vector3 TriangleNormal(Vector3 v1, Vector3 v2, Vector3 v3) {
        var a = v2 - v1;
        var b = v3 - v1;
        return Vector3.Cross(a, b).normalized;
    }

    public static (int[] Triangles, DelaunatorSharp.IPoint[] Points) GetDelaunayTriangulation(List<Vector3> vertices) {
        var dPoints = new List<DelaunatorSharp.IPoint>();
        foreach (var point in vertices) {
            dPoints.Add(new DelaunatorSharp.Point(point.x, point.z));
        }
        try {
            var res = new DelaunatorSharp.Delaunator(dPoints.ToArray());
            return (res.Triangles, res.Points);
        } catch {
            return (new int[] { }, new DelaunatorSharp.IPoint[] { });
        }
    }

    static float Vector2Cross(Vector2 v1, Vector2 v2) {
        return v1.x * v2.y - v1.y * v2.x;
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
            var dist = Vector3.Distance(p0, p1);
            curPos += dist;
        }
        return curPos;
    }

    public static Vector3 GetPointOnPath(Vector3[] path, float pos) {
        if (path.Length == 0) return Vector3.zero;
        float lastCurPos;
        var curPos = 0.0f;
        for (int i = 0; i < path.Length - 1; i++) {
            lastCurPos = curPos;
            var p0 = path[i];
            var p1 = path[i + 1];
            var dist = Vector3.Distance(p0, p1);
            curPos += dist;
            if (curPos > pos) {
                var posHere = pos - lastCurPos;
                var alpha = posHere / dist;
                return Vector3.Lerp(p0, p1, alpha);
            }
        }
        return path[path.Length - 1];
    }

    public static (Vector3 pos, Vector3 dir) GetPointAndDirOnSidewalk(Vector3[] pathIn, Vector3[] pathOut, Vector3[] pathMid, float x, float z) {
        var pMid = GetPointOnPath(pathMid, z);
        var pIn = ClosestPointToCurve(pMid, new List<Vector3>(pathIn), out _);
        var pOut = ClosestPointToCurve(pMid, new List<Vector3>(pathOut), out _);
        var dir = (pIn - pOut).normalized;
        return (Vector3.Lerp(pIn, pOut, x), dir);
    }

    static bool DoesRayIntersectSegment(Vector2 point, Vector2 direction, Vector2 p1, Vector2 p2) {
        var v1 = point - p1;
        var v2 = p2 - p1;
        var v3 = new Vector2(-direction.y, direction.x);
        var dot = Vector2.Dot(v2, v3);
        if (Mathf.Abs(dot) < epsilon) return false;
        var t1 = Vector2Cross(v2, v1) / Vector2.Dot(v2, v3);
        var t2 = Vector2.Dot(v1, v3) / Vector2.Dot(v2, v3);
        return t1 >= 0 && t2 >= 0 && t2 < 1; //exclude ==1 because we are testing consecutive segments
    }

    public static bool IsPointInsidePolygon(Vector3 point, List<Vector3> polygon) {
        int count = 0;
        var pointb = new Vector2(point.x, point.z);
        for (int i = 0; i < polygon.Count; i++) {
            var p1 = polygon[i];
            var p2 = polygon[(i < polygon.Count - 1) ? (i + 1) : 0];
            var p1b = new Vector2(p1.x, p1.z);
            var p2b = new Vector2(p2.x, p2.z);
            count += DoesRayIntersectSegment(pointb, Vector2.right, p1b, p2b) ? 1 : 0;
        }
        return count % 2 != 0;
    }


    public static bool IsPolygonClockwise(List<Vector3> polygon) {
        var sum = 0.0f;
        for (int i = 0; i < polygon.Count; i++) {
            var p1 = polygon[i];
            var p2 = polygon[(i < polygon.Count - 1) ? (i + 1) : 0];
            sum += (p2.x - p1.x) * (p2.z + p1.z);
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
            var n1 = Vector3.Cross(d1, Vector3.up);
            var n2 = Vector3.Cross(d2, Vector3.up);
            var n = ((n1 + n2) * 0.5f).normalized * mult;
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

    public static Vector3 GetPointOnCurve(Vector3 start, List<Vector3> controlPoints, Vector3 end, float alpha, int alphaI, CurveType curveType, float hermiteTension = 0.5f, bool subdivideEqually = false) {
        if (AreVectorsEqual(start, end)) return start;
        var tempPoints = new List<Vector3> { start };
        tempPoints.AddRange(controlPoints);
        tempPoints.Add(end);
        switch (curveType) {
            case CurveType.Bezier:
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
                var factor = 1.0f / (tempPoints.Count - 1);
                var segmentCount = tempPoints.Count - 1;
                var factors = new List<float>();
                var starts = new List<float>();
                var ends = new List<float>();
                var max = 1.0f;
                if (subdivideEqually) {
                    max = 0.0f;
                    for (int i = 0; i < segmentCount; i++) {
                        var p1 = tempPoints[i];
                        var p2 = tempPoints[i + 1];
                        var f = (p2 - p1).magnitude;
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
                        alpha -= startI;
                        alpha /= factorI;

                        var a = alpha;
                        var a2 = a * a;
                        var a3 = a2 * a;

                        var p0 = i < 1 ? start : tempPoints[i - 1];
                        var p1 = tempPoints[i];
                        var p2 = tempPoints[i + 1];
                        var p3 = i > (segmentCount - 2) ? end : tempPoints[i + 2];

                        var t1 = hermiteTension * (p2 - p0);
                        var t2 = hermiteTension * (p3 - p1);

                        var b1 = 2 * a3 - 3 * a2 + 1;
                        var b2 = -2 * a3 + 3 * a2;
                        var b3 = a3 - 2 * a2 + a;
                        var b4 = a3 - a2;

                        var res = b1 * p1 + b2 * p2 + b3 * t1 + b4 * t2;
                        if (float.IsNaN(res.x) || float.IsNaN(res.y) || float.IsNaN(res.z)) res = Vector3.zero;
                        return res;
                    }
                }
                break;
            case CurveType.LowPoly:
                int fixedAlphaI = Mathf.Clamp(alphaI, 0, tempPoints.Count - 1);
                return tempPoints[fixedAlphaI];
        }
        return Vector3.zero;
    }

    public static List<Vector3> LowPassFilter(List<Vector3> curvePoints, int iterations, int segments) {
        iterations = Mathf.Clamp(iterations, 0, segments); //to avoid exceptions when setting an invalid value
        List<Vector3> curvePoints2 = new List<Vector3>(curvePoints);
        for (int i = 1; i < segments - 1; ++i) {
            var p = Vector3.zero;
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
        return Mathf.Clamp01(Vector3.Dot(point - lineStart, lineEnd - lineStart) / (lineEnd - lineStart).sqrMagnitude);
    }

    public static Vector3 ClosestPoint(Vector3 point, Vector3 lineStart, Vector3 lineEnd) {
        var t = ClosestPointFactor(point, lineStart, lineEnd);
        return lineStart + t * (lineEnd - lineStart);
    }

    public static Vector3 ClosestPointToCurve(Vector3 point, List<Vector3> curvePoints, out int minI) {
        var minDist = float.MaxValue;
        var minP = Vector3.zero;
        minI = -1;
        for (int i = 0; i < curvePoints.Count - 1; i++) {
            var p0 = curvePoints[i];
            var p1 = curvePoints[i + 1];
            var p = ClosestPoint(point, p0, p1);
            var d = (point - p).sqrMagnitude;
            if (d < minDist) {
                minI = i;
                minDist = d;
                minP = p;
            }
        }
        return minP;
    }

    public static Vector3 ProjectPoint(Vector3 pos, int layerMask = 1 << 3) {
        var res = pos;
        var pos2 = pos + Vector3.up * 1000000;
        Ray ray = new Ray(pos2, Vector3.down);
        if (Physics.Raycast(pos2, ray.direction, out RaycastHit hit, Mathf.Infinity, layerMask)) {
            res = hit.point;
        }
        return res;
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
            return Mathf.Atan2(v.z, v.x);
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
        var A1 = p1a.z - p1b.z;
        var B1 = p1b.x - p1a.x;
        var C1 = p1a.x * p1b.z - p1b.x * p1a.z;

        var A2 = p2a.z - p2b.z;
        var B2 = p2b.x - p2a.x;
        var C2 = p2a.x * p2b.z - p2b.x * p2a.z;

        float delta = A1 * B2 - A2 * B1;

        if (delta == 0) return (p1a + p2a) * 0.5f;

        float x = (B2 * C1 - B1 * C2) / delta;
        float y = (A1 * C2 - A2 * C1) / delta;

        return new Vector3(-x, (p1b.y + p2b.y) / 2, -y);
    }

    static Vector2 V3to2(Vector3 v) {
        return new Vector2(v.x, v.z);
    }

    public static List<Vector3> GetLineWithoutIntersections(List<Vector3> line) {
        var res = new List<Vector3>(line);
        if (res.Count <= 3) return res;
        for (int i = 0; i < res.Count - 1; i++) {
            var intersectionI = -1;
            var intersectionPoint = Vector3.zero;
            for (int j = i + 2; j < res.Count - 1; j++) {
                var intersects = LineSegmentsIntersection.Math2d.LineSegmentsIntersection(V3to2(res[i]), V3to2(res[i + 1]), V3to2(res[j]), V3to2(res[j + 1]), out Vector2 intersectionPoint2);
                if (intersects) {
                    intersectionI = j;
                    intersectionPoint = new Vector3(intersectionPoint2.x, (res[i].y + res[i + 1].y + res[j].y + res[j + 1].y) / 4, intersectionPoint2.y);
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
        var length = Vector2.SqrMagnitude(s0 - s1);
        if (length == 0.0) return Vector2.SqrMagnitude(p - s0);
        var dot = Vector2.Dot(p - s0, s1 - s0) / length;
        var t = Mathf.Clamp01(dot);
        var proj = s0 + t * (s1 - s0);
        return Vector2.SqrMagnitude(p - proj);
    }

    public static float PointPolygonSquaredDistance(Vector3 point, List<Vector3> polygon, bool loop = true) {
        var minDist = float.MaxValue;
        var point_2 = new Vector2(point.x, point.z);
        for (int i = 0; i < polygon.Count; i++) {
            if (!loop && i == 0) continue;
            var i0 = i == 0 ? polygon.Count - 1 : i - 1;
            var p0 = polygon[i0];
            var p1 = polygon[i];
            var p0_2 = new Vector2(p0.x, p0.z);
            var p1_2 = new Vector2(p1.x, p1.z);
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
                var avg = Vector3.zero;
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
                vertices[i] = new Vector3(v.x, averages[i - startI].y, v.z);
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

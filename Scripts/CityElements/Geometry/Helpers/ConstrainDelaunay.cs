using System.Collections;
using System.Collections.Generic;
using Godot;
using GH = GeometryHelper;
using V3 = GameEngine.Vector3;
using V2 = GameEngine.Vector2;

public static class ConstrainedDelaunay {
    //Algorithm as described here (the part after obtaining the delaunay triangulation): http://www.geom.uiuc.edu/~samuelp/del_project.html#algorithms
    public static int[] ConstrainDelaunay(int[] triangles, List<Vector3> vertices, List<List<int>> edges) {
        var vertices2 = new List<Vector2>();
        foreach (var point in vertices) {
            vertices2.Add(new Vector2(point[0], point[2]));
        }
        return ConstrainDelaunay(triangles, vertices2, edges);
    }

    static int[] ConstrainDelaunay(int[] triangles, List<Vector2> vertices2, List<List<int>> edges) {
        foreach (var edge in edges) {
            triangles = ConstrainDelaunay(triangles, vertices2, edge);
        }
        return triangles;
    }

    static int[] ConstrainDelaunay(int[] triangles, List<Vector2> vertices2, List<int> edge) {
        if (EdgeExists(triangles, edge)) return triangles;
        var e1 = vertices2[edge[0]];
        var e2 = vertices2[edge[1]];
        var triangles2 = new List<int>();
        var expelledTrianglesVertices = new List<int>();
        for (int i = 0; i < triangles.Length; i += 3) {
            var tri = new List<int> { triangles[i], triangles[i + 1], triangles[i + 2] };
            var t1 = vertices2[triangles[i]];
            var t2 = vertices2[triangles[i + 1]];
            var t3 = vertices2[triangles[i + 2]];
            var intersects = DoesTriangleIntersectEdge(t1, t2, t3, e1, e2);
            if (!intersects) {
                triangles2.AddRange(tri);
            } else {
                for (int j = 0; j < 3; j++) {
                    var v = triangles[i + j];
                    if (v != edge[0] && v != edge[1] && !expelledTrianglesVertices.Contains(v)) expelledTrianglesVertices.Add(v);
                }
            }
        }
        var part1 = new List<int>() { edge[0], edge[1] };
        var part2 = new List<int>() { edge[0], edge[1] };
        foreach (var i in expelledTrianglesVertices) {
            var side = PointLineSide(e1, e2, vertices2[i]);
            if (side) {
                part1.Add(i);
            } else {
                part2.Add(i);
            }
        }

        var verticesA = new List<Vector2>();
        foreach (var i in part1) {
            verticesA.Add(vertices2[i]);
        }
        var verticesB = new List<Vector2>();
        foreach (var i in part2) {
            verticesB.Add(vertices2[i]);
        }
        if (verticesA.Count < 3 && verticesB.Count < 3) return triangles;
        ConstrainDelaunay_Retriangulate(triangles, edge, triangles2, part1, verticesA);
        ConstrainDelaunay_Retriangulate(triangles, edge, triangles2, part2, verticesB);

        return triangles2.ToArray();
    }

    static bool EdgeExists(int[] triangles, List<int> edge) {
        for (int i = 0; i < triangles.Length; i += 3) {
            var tri = new List<int> { triangles[i], triangles[i + 1], triangles[i + 2] };
            var has1 = tri.Contains(edge[0]);
            var has2 = tri.Contains(edge[1]);
            if (has1 && has2) return true;
        }
        return false;
    }

    static bool DoesTriangleIntersectEdge(Vector2 t1, Vector2 t2, Vector2 t3, Vector2 e1, Vector2 e2) {
        if (t1 == e1 || t1 == e2) return DoesTriangleIntersectEdgeFromVertex(t1, t2, t3, e1, e2);
        if (t2 == e1 || t2 == e2) return DoesTriangleIntersectEdgeFromVertex(t2, t3, t1, e1, e2);
        if (t3 == e1 || t3 == e2) return DoesTriangleIntersectEdgeFromVertex(t3, t1, t2, e1, e2);
        var i1 = DoSegmentsIntersect(t1, t2, e1, e2) ? 1 : 0;
        var i2 = DoSegmentsIntersect(t1, t3, e1, e2) ? 1 : 0;
        var i3 = DoSegmentsIntersect(t2, t3, e1, e2) ? 1 : 0;
        return i1 + i2 + i3 >= 2;
    }

    //The sides starting from the vertex v shared with the edge always touch the edge in v (unless they lie on the edge line),
    //so the edge crosses the triangle only if it also crosses the opposite side. The touch itself is not tested: being right
    //on the end of both segments, its result would depend on the rounding of the intersection parameters.
    static bool DoesTriangleIntersectEdgeFromVertex(Vector2 v, Vector2 a, Vector2 b, Vector2 e1, Vector2 e2) {
        return !AreSegmentsParallel(v, a, e1, e2) && !AreSegmentsParallel(v, b, e1, e2) && DoSegmentsIntersect(a, b, e1, e2);
    }

    static bool AreSegmentsParallel(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4) {
        return (p2[0] - p1[0]) * (p4[1] - p3[1]) - (p2[1] - p1[1]) * (p4[0] - p3[0]) == 0.0f;
    }

    static bool DoSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4) {
        return LineSegmentsIntersection.Math2d.LineSegmentsIntersection(p1, p2, p3, p4, out _);
    }

    static bool PointLineSide(Vector2 a, Vector2 b, Vector2 p) {
        var a3 = new Vector3(a[0], 0, a[1]);
        var b3 = new Vector3(b[0], 0, b[1]);
        var p3 = new Vector3(p[0], 0, p[1]);
        var plane = GameEngine.Plane.Create(a3, b3, a3 + GameEngine.Vector3.up);
        return GameEngine.Plane.GetSide(plane, p3);
    }

    static void ConstrainDelaunay_Retriangulate(int[] triangles0, List<int> edge, List<int> outTris, List<int> part, List<Vector2> vertices) {
        if (vertices.Count >= 3) {
            var order = GetPolygonOrder(triangles0, edge, part, vertices);
            var tris = GetDelaunayTriangulation(vertices);
            var vB3 = GetVec3sFromVec2s(vertices);
            //check needed to clean concave parts (to avoid having extra bad triangles)
            for (int i = 0; i < tris.Length; i += 3) {
                var p1 = tris[i];
                var p2 = tris[i + 1];
                var p3 = tris[i + 2];
                if (CheckEdge(p1, p2, vB3, order)
                 && CheckEdge(p2, p3, vB3, order)
                 && CheckEdge(p3, p1, vB3, order)) {
                    outTris.Add(part[p1]);
                    outTris.Add(part[p2]);
                    outTris.Add(part[p3]);
                }
            }
        }
    }

    static List<int> GetPolygonOrder(int[] triangles0, List<int> edge, List<int> part, List<Vector2> vertices) {
        //Get the edges
        var edges = new List<(int, int)>();
        var counts = new List<int>();
        for (int i = 0; i < triangles0.Length; i += 3) {
            var v1 = triangles0[i];
            var v2 = triangles0[i + 1];
            var v3 = triangles0[i + 2];
            var couples = new List<(int a, int b, int c)>() { (v1, v2, v3), (v2, v3, v1), (v3, v1, v2) };
            foreach (var c in couples) {
                if (part.Contains(c.a) && part.Contains(c.b)) {
                    var e = GetEdge(c.a, c.b);
                    if (!edges.Contains(e)) {
                        edges.Add(e);
                        counts.Add(1);
                    } else if (part.Contains(c.c)) {
                        counts[edges.IndexOf(e)] += 1;
                    }
                }
            }
        }

        //detect triangles created by the new edge
        for (int i = 0; i < part.Count; i++) {
            var v = part[i];
            int startJ = -1;
            int endJ = -1;
            for (int j = 0; j < edges.Count; j++) {
                var e = edges[j];
                if (e.Item1 == v || e.Item2 == v) {
                    if (e.Item1 == edge[0] || e.Item2 == edge[0]) {
                        startJ = j;
                    }  else if (e.Item1 == edge[1] || e.Item2 == edge[1]) {
                        endJ = j;
                    }
                }
            }
            if (startJ >= 0 && endJ >= 0) {
                counts[startJ] += 1;
                counts[endJ] += 1;
            }
        }

        //calculate the polygon
        var order = new List<int>() { edge[0], edge[1] };
        int cur = edge[1];
        var found = true;
        while (found && cur != edge[0]) {
            if (edges.Count == 0) found = false;
            for (int i = 0; i < edges.Count; i++) {
                var e = edges[i];
                found = false;
                if (counts[i] > 1) { //skip internal vertices
                    found = true; //to keep searching
                    edges.RemoveAt(i);
                    counts.RemoveAt(i);
                    break;
                }
                if (cur == e.Item1) {
                    cur = e.Item2;
                    found = true;
                } else if (cur == e.Item2) {
                    cur = e.Item1;
                    found = true;
                }
                if (found) {
                    if (cur != edge[0]) order.Add(cur);
                    edges.RemoveAt(i);
                    counts.RemoveAt(i);
                    break;
                }
            }
        }
        for (int i = 0; i < order.Count; i++) {
            order[i] = part.IndexOf(order[i]);
        }
        return order;
    }

    static (int, int) GetEdge(int p1, int p2) {
        return p1 < p2 ? (p1, p2) : (p2, p1);
    }

    static int[] GetDelaunayTriangulation(List<Vector2> vertices2) {
        var dPoints = new List<DelaunatorSharp.IPoint>();
        foreach (var point in vertices2) {
            dPoints.Add(new DelaunatorSharp.Point(point[0], point[1]));
        }
        var dResult = new DelaunatorSharp.Delaunator(dPoints.ToArray());
        return dResult.Triangles;
    }

    static List<Vector3> GetVec3sFromVec2s(List<Vector2> inList) {
        var res = new List<Vector3>();
        foreach (var v in inList) {
            res.Add(new Vector3(v[0], 0, v[1]));
        }
        return res;
    }

    static bool CheckEdge(int p10, int p20, List<Vector3> allPoints, List<int> order) {
        if (allPoints.Count == 3) return true; //a lone triangle cannot be concave
        if (order == null) return false;
        var p1 = order.IndexOf(p10);
        var p2 = order.IndexOf(p20);
        var diff = (p1 > p2) ? (p1 - p2) : (p2 - p1);
        var onEdge = diff == 1 || diff == order.Count - 1;
        if (!onEdge) {
            var m = (allPoints[p10] + allPoints[p20]) * 0.5f;
            var sorted = new List<Vector3>();
            foreach (int i in order) {
                sorted.Add(allPoints[i]);
            }
            return GH.IsPointInsidePolygon(m, sorted);
        } else {
            return true;
        }
    }
}

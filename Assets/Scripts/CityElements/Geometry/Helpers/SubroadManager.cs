using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GeometryHelpers {
    public class SubroadManager {
        public ObjectState state;
        public List<List<Vector3>> subRoadVertices = new List<List<Vector3>>();
        public List<Vector2> uvMults = new List<Vector2>();
        public List<string> subRoadTextures = new List<string>();
        public List<List<List<int>>> subRoadSegments = new List<List<List<int>>>();
        public List<List<bool>> subRoadConvexSegments = new List<List<bool>>();
        public List<int> subRoadTypes = new List<int>();

        public void Clear() {
            subRoadVertices.Clear();
            uvMults.Clear();
            subRoadTextures.Clear();
            subRoadSegments.Clear();
            subRoadConvexSegments.Clear();
            subRoadTypes.Clear();
        }

        public int CreateSubRoad(string texture, bool texIsDefault, bool hasRoads, Vector2 uvMult, string defaultTex, int type = 0) {
            if (texture == "") texture = defaultTex;
            if (texIsDefault && !hasRoads) {
                var res = subRoadTextures.IndexOf(texture);
                if (res != -1) {
                    return res;
                }
            }
            subRoadVertices.Add(new List<Vector3>());
            uvMults.Add(uvMult);
            subRoadTextures.Add(texture);
            subRoadSegments.Add(new List<List<int>>());
            subRoadTypes.Add(type);
            subRoadConvexSegments.Add(new List<bool>());
            return subRoadVertices.Count - 1;
        }

        public void AddSegmentToSubroad(int curSubRoad, List<Vector3> newVertices, Plane plane, bool forceConvex) {
            var vertices = subRoadVertices[curSubRoad];
            var segments = subRoadSegments[curSubRoad];
            var convexSegments = subRoadConvexSegments[curSubRoad];
            var segment = new List<int>();
            foreach (var v in newVertices) {
                var existingV = GeometryHelper.FindVector(vertices, v);
                if (existingV != -1) {
                    segment.Add(existingV);
                } else {
                    vertices.Add(v);
                    segment.Add(vertices.Count - 1);
                }
            }
            segments.Add(segment);

            //check if segment is convex
            var convex = true;
            if (segment.Count > 0 && !forceConvex) {
                var p0 = vertices[segment[0]];
                var p1 = vertices[segment[segment.Count - 1]];
                var d0 = plane.GetDistanceToPoint(p0);
                var d1 = plane.GetDistanceToPoint(p1);
                if (Mathf.Sign(d0) != Mathf.Sign(d1)) convex = false;
            }
            convexSegments.Add(convex);

        }

        public void MergeSubroadsIfNeeded() {
            if (subRoadVertices.Count < 2) return;
            var curI = 0;
            var curJ = 1;
            while (curI < subRoadVertices.Count - 1) {
                var res = MergeSubroadsIfNeeded(curI, curJ);
                if (!res) curJ++;
                if (curJ >= subRoadVertices.Count) {
                    curI++;
                    curJ = curI + 1;
                }
            }
        }

        bool MergeSubroadsIfNeeded(int i0, int i1) {
            if (subRoadTypes[i0] != 0 || subRoadTypes[i1] != 0) return false;
            if (subRoadTextures[i0] != subRoadTextures[i1]) return false;
            var doMerge = false;
            var verts0 = subRoadVertices[i0];
            var verts1 = subRoadVertices[i1];
            for (int i = 0; i < verts0.Count; i++) {
                for (int j = 0; j < verts1.Count; j++) {
                    if (GeometryHelper.AreVectorsEqual(verts0[i], verts1[j])) {
                        doMerge = true;
                        break;
                    }
                }
                if (doMerge) break;
            }
            if (doMerge) {
                subRoadTextures.RemoveAt(i1);
                for (int i = 0; i < subRoadSegments[i1].Count; i++) {
                    var segment = subRoadSegments[i1][i];
                    var newSegment = new List<int>();
                    for (int j = 0; j < segment.Count; j++) {
                        var newV = subRoadVertices[i1][segment[j]];
                        var existingV = GeometryHelper.FindVector(subRoadVertices[i0], newV);
                        if (existingV == -1) {
                            subRoadVertices[i0].Add(newV);
                            newSegment.Add(subRoadVertices[i0].Count - 1);
                        } else {
                            newSegment.Add(existingV);
                        }
                    }
                    subRoadSegments[i0].Add(newSegment);
                    subRoadConvexSegments[i0].Add(subRoadConvexSegments[i1][i]);
                }
                subRoadConvexSegments.RemoveAt(i1);
                subRoadVertices.RemoveAt(i1);
                subRoadSegments.RemoveAt(i1);
                uvMults.RemoveAt(i1);
                subRoadTypes.RemoveAt(i1);
            }
            return doMerge;
        }
    }
}
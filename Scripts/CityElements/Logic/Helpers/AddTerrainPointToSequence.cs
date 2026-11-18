using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using States;

public static class AddTerrainPointToSequence {
    public static List<TerrainAnchor> ConnectedAnchors(List<TerrainPoint> sequence, TerrainAnchor from, TerrainAnchor to) {
        var list = from.GetPointListTo(to);
        if (list.Count > 0 && sequence.Exists(p => AnchorEquals(p.anchor, list[0]))) list = from.GetPointListTo(to, true);
        return list;
    }
    public static bool AnchorEquals(IAnchorable a, TerrainAnchor b) {
        if (a is TerrainAnchor an) {
            return an == b;
        }
        return false;
    }

    static bool CheckIfAlreadyPresent(List<TerrainPoint> sequencePoints, TerrainAnchor anchor) {
        foreach (var point in sequencePoints) {
            if (AnchorEquals(point.anchor, anchor)) return true;
        }
        return false;
    }

    //where a point between two points of the sequence goes (the index to insert it at), -1 if they are not next to
    //each other in it. The segment from the last point to the first one counts (the perimeters are closed): the point
    //is added at the end
    public static int SegmentInsertIndex(List<TerrainPoint> sequencePoints, TerrainPoint a, TerrainPoint b) {
        if (a == null || b == null || a == b) return -1;
        var index1 = sequencePoints.IndexOf(a);
        var index2 = sequencePoints.IndexOf(b);
        if (index1 < 0 || index2 < 0) return -1;
        var count = sequencePoints.Count;
        if ((index1 + 1) % count == index2) return index1 + 1;
        if ((index2 + 1) % count == index1) return index2 + 1;
        return -1;
    }

    //what a click on a segment of the sequence itself inserts: where the clicked link is in it (the index to insert a
    //point at, -1 if the object is not a link between two points next to each other in the sequence) and the point
    //of the segment closest to the click
    public static int ClickedSegmentInsertIndex(List<TerrainPoint> sequencePoints, GameObject obj, ref Vector3 point) {
        var pointLink = obj == null ? null : GS.Cast<PointLink>(obj);
        if (pointLink == null || GO.IsNull(pointLink.next)) return -1;
        var pPrev = GS.FindRecursive<TerrainPoint>(GO.GetParent(pointLink));
        var pNext = GS.FindRecursive<TerrainPoint>(pointLink.next);
        var index = SegmentInsertIndex(sequencePoints, pPrev, pNext);
        if (index >= 0) point = GeometryHelper.ClosestPoint(point, pPrev.GetPoint(), pNext.GetPoint());
        return index;
    }

    //the segment of the sequence closest to the position, as the index to insert a point in it at (-1 with less than
    //two points); closed: with the segment from the last point to the first one
    public static int ClosestSegmentInsertIndex(List<TerrainPoint> sequencePoints, Vector3 position, bool closed) {
        var count = sequencePoints.Count;
        if (count < 2) return -1;
        var best = -1;
        var bestDistance = float.MaxValue;
        for (int i = 0; i < (closed && count > 2 ? count : count - 1); i++) {
            var a = sequencePoints[i].GetPoint();
            var b = sequencePoints[(i + 1) % count].GetPoint();
            var distance = GeometryHelper.ClosestPoint(position, a, b).DistanceSquaredTo(position);
            if (distance < bestDistance) {
                bestDistance = distance;
                best = i + 1;
            }
        }
        return best;
    }

    public static TerrainPoint AddPoint(List<TerrainPoint> sequencePoints, ref TerrainAnchor curAnchor, GameObject container,
        IGroundable gnd, System.Action postAdd, System.Action post, System.Action setGroundable,
        GameObject obj, Vector3 point, bool pointOnly = false) {

        TerrainPoint res = null;
        var anchor = obj == null ? null : GS.FindRecursive<TerrainAnchor>(obj);
        var lastI = sequencePoints.Count - 1;
        var p = obj == null ? null : GS.FindRecursive<TerrainPoint>(obj);
        if (anchor == null) { //check if on anchor
            if (p != null && p.anchor is TerrainAnchor ta && !GO.IsNull(ta)) {
                anchor = ta;
            }
        }
        if (anchor != null) {
            if (CheckIfAlreadyPresent(sequencePoints, anchor)) return null;
            if (!pointOnly && !GO.IsNull(curAnchor)) {
                var list = ConnectedAnchors(sequencePoints, curAnchor, anchor);

                foreach (var item in list) {
                    res = TerrainPoint.Create(item.GetPoint(), gnd, container, item);
                    sequencePoints.Add(res);
                    postAdd?.Invoke();
                }
            } else {
                if (p == null) {
                    res = TerrainPoint.Create(anchor.GetPoint(), gnd, container, anchor);
                    sequencePoints.Add(res);
                    postAdd?.Invoke();
                } else {
                    sequencePoints.Add(p);
                    postAdd?.Invoke();
                    p.Select(true);
                    p.AddLink(gnd);
                    res = p;
                }
            }
        } else {
            if (sequencePoints.Contains(p)) return null;
            if (p != null && !p.IsProjectedToGround()) setGroundable?.Invoke();
            if (p == null) {
                var pointLink = obj == null ? null : GS.Cast<PointLink>(obj);
                LineAnchor newAnchor = null;
                var index = -1;
                if (pointLink != null) {
                    var pPrev = GS.FindRecursive<TerrainPoint>(GO.GetParent(pointLink));
                    var pNext = GS.FindRecursive<TerrainPoint>(pointLink.next);
                    newAnchor = new LineAnchor(GO.GetParent(pointLink), pointLink.next, point);
                    //on a segment of the sequence itself: between its two points (also on the last segment, and at
                    //the end on the one closing a perimeter)
                    index = SegmentInsertIndex(sequencePoints, pPrev, pNext);
                }
                res = TerrainPoint.Create(point, gnd, container, newAnchor);
                if (index >= 0) {
                    sequencePoints.Insert(index, res);
                } else {
                    sequencePoints.Add(res);
                }
                postAdd?.Invoke();
            } else {
                sequencePoints.Add(p);
                postAdd?.Invoke();
                p.Select(true);
                p.AddLink(gnd);
                res = p;
            }
        }
        post?.Invoke();
        return res;
    }
}

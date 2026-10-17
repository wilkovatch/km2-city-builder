using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using States;

public static class AddTerrainPointToSequence {
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
                var list = curAnchor.GetPointListTo(anchor);

                //invert if needed
                if (list.Count > 0) {
                    foreach (var seqPoint in sequencePoints) {
                        if (AnchorEquals(seqPoint.anchor, list[0])) {
                            list = curAnchor.GetPointListTo(anchor, true);
                            break;
                        }
                    }
                }

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
                    newAnchor = new LineAnchor(GO.GetParent(pointLink), pointLink.next, point);
                    var pPrev = GS.FindRecursive<TerrainPoint>(GO.GetParent(pointLink));
                    var pNext = GS.FindRecursive<TerrainPoint>(pointLink.next);
                    if (sequencePoints.Contains(pPrev) && sequencePoints.Contains(pNext)) {
                        var index1 = sequencePoints.IndexOf(pPrev);
                        var index2 = sequencePoints.IndexOf(pNext);
                        index = Mathf.Max(index1, index2);
                    }
                }
                res = TerrainPoint.Create(point, gnd, container, newAnchor);
                if (index >= 0 && index < sequencePoints.Count - 1) {
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
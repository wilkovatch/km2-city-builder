using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
        var anchor = obj == null ? null : obj.GetComponent<TerrainAnchor>();
        var lastI = sequencePoints.Count - 1;
        var p = obj == null ? null : obj.GetComponent<TerrainPoint>();
        if (anchor == null) { //check if on anchor
            if (p != null && p.anchor is TerrainAnchor ta) {
                anchor = ta;
            }
        }
        if (anchor != null) {
            if (CheckIfAlreadyPresent(sequencePoints, anchor)) return null;
            if (!pointOnly && curAnchor != null) {
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
                    res = TerrainPoint.Create(item.gameObject.transform.position, gnd, container, item);
                    sequencePoints.Add(res);
                    postAdd?.Invoke();
                }
            } else {
                if (p == null) {
                    res = TerrainPoint.Create(anchor.gameObject.transform.position, gnd, container, anchor);
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
                var pointLink = obj == null ? null : obj.GetComponent<PointLink>();
                LineAnchor newAnchor = null;
                var index = -1;
                if (pointLink != null) {
                    newAnchor = new LineAnchor(pointLink.transform.parent.gameObject, pointLink.next, point);
                    var pPrev = pointLink.transform.parent.gameObject.GetComponent<TerrainPoint>();
                    var pNext = pointLink.next.GetComponent<TerrainPoint>();
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
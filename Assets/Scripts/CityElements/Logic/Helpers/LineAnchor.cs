using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LineAnchor: IAnchorable {
    public GameObject start, end;
    public float percent;

    public LineAnchor(GameObject start, GameObject end, Vector3 point) {
        this.start = start;
        this.end = end;
        percent = GeometryHelper.ClosestPointFactor(point, start.transform.position, end.transform.position);
    }

    public LineAnchor(GameObject start, GameObject end, float percent) {
        this.start = start;
        this.end = end;
        this.percent = percent;
    }

    public bool IsDeleted() {
        return start == null || end == null;
    }

    public bool IsMoveable() {
        return true;
    }

    public void UpdatePercent(Vector3 point) {
        percent = GeometryHelper.ClosestPointFactor(point, start.transform.position, end.transform.position);
    }

    public Vector3 GetPosition() {
        return Vector3.Lerp(start.transform.position, end.transform.position, percent);
    }
}
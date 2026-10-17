using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;

public class LineAnchor: IAnchorable {
    public GameObject start, end;
    public float percent;

    public LineAnchor(GameObject start, GameObject end, Vector3 point) {
        this.start = start;
        this.end = end;
        percent = GeometryHelper.ClosestPointFactor(point, GO.GetPosition(start), GO.GetPosition(end));
    }

    public LineAnchor(GameObject start, GameObject end, float percent) {
        this.start = start;
        this.end = end;
        this.percent = percent;
    }

    public bool IsDeleted() {
        return GO.IsNull(start) || GO.IsNull(end);
    }

    public bool IsMoveable() {
        return true;
    }

    public void UpdatePercent(Vector3 point) {
        percent = GeometryHelper.ClosestPointFactor(point, GO.GetPosition(start), GO.GetPosition(end));
    }

    public Vector3 GetPoint() {
        return GameEngine.Vector3.Lerp(GO.GetPosition(start), GO.GetPosition(end), percent);
    }
}
using Godot;
using GO = GameEngine.GameObject;

//An attachment to the center of an intersection (the control points of the custom elements whose type has
//intersectionTargets, see CustomElementSplineSettings): the point follows the intersection, and is left free where it
//was if the intersection is deleted
public class IntersectionAnchor : IAnchorable {
    public readonly Intersection intersection;

    public IntersectionAnchor(Intersection intersection) {
        this.intersection = intersection;
    }

    public bool IsDeleted() {
        return intersection == null || intersection.IsDeleted() || GO.IsNull(intersection.point);
    }

    public bool IsMoveable() {
        return false;
    }

    public Vector3 GetPoint() {
        return GO.GetPosition(intersection.point);
    }
}

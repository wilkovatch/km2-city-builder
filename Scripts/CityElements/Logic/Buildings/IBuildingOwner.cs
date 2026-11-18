//an element made of buildings (a building line or a polygonal building): the body their colliders and outlines are
//attached to, and the building type they use
public interface IBuildingOwner {
    //the node of the element (a StaticBody3D, so that the colliders of the buildings select it)
    public Godot.Node3D GetBody();
    //the name of the building type of the element
    public string GetBuildingTypeName();
    public bool IsDeleted();
    //the state of the front side of one of its buildings was replaced (by the side generator)
    public void OnFrontStateSet(Building building, ObjectState state);
}

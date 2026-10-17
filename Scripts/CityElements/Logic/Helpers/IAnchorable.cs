using Godot;

public interface IAnchorable {
    public bool IsDeleted();
    public bool IsMoveable();
    public Vector3 GetPoint();
}
using Godot;

//A fixed attachment to another terrain point, keeping the follower's identity and shared references.
public class PointAnchor : IAnchorable {
    public readonly TerrainPoint master;

    public PointAnchor(TerrainPoint master) {
        this.master = master;
    }

    public bool IsDeleted() {
        return master == null || master.IsDeleted();
    }

    public bool IsMoveable() {
        return false;
    }

    public Vector3 GetPoint() {
        return master.GetPoint();
    }
}

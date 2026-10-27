using Godot;

//An anchor on the vertical line of another terrain point (its master, e.g. the bottom corner of a building under its top
//corners): the anchored point keeps its own height and follows the x and z of the master; moving it horizontally moves
//the master (and so every point on the same vertical line), see TerrainPoint.UpdatePosition
public class ColumnAnchor : IAnchorable {
    public readonly TerrainPoint master;
    public float height; //the y of the anchored point

    public ColumnAnchor(TerrainPoint master, float height) {
        this.master = master;
        this.height = height;
    }

    public bool IsDeleted() {
        return master == null || master.IsDeleted();
    }

    public bool IsMoveable() {
        return true;
    }

    public Vector3 GetPoint() {
        var p = master.GetPoint();
        return new Vector3(p.X, height, p.Z);
    }

    //the anchored point moved to the position (e.g. with the gizmo): the master follows horizontally, the height is the
    //one of the position
    public void MoveTo(Vector3 position) {
        var delta = position - GetPoint();
        delta.Y = 0;
        //as with the gizmo: a master on a fixed anchor doesn't move, one on a segment slides along it
        if (delta.LengthSquared() > 1e-12f) Operations.TerrainPoints.Move(master, master.GetPoint() + delta);
        height = position.Y;
    }
}

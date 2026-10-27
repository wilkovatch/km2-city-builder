using Godot;
using GO = GameEngine.GameObject;

namespace Operations {
    //The points of the terrain patches and building lines
    public static class TerrainPoints {
        public enum MoveResult {
            Moved,
            OnAnchor //a point on an anchor follows it, it can't be moved
        }

        //moves the point as with the gizmo: a free point goes to the position, a point on a segment slides along it to
        //the closest position, a point on the vertical line of another one moves that one horizontally, a point on an
        //anchor can't be moved (unless leaveAnchor: it leaves the anchor, as the points of the low-level elements)
        public static MoveResult Move(TerrainPoint point, Vector3 position, bool leaveAnchor = false) {
            var onAnchor = point.anchor is TerrainAnchor ta && !GO.IsNull(ta) && !ta.IsDeleted();
            if (onAnchor && !leaveAnchor) return MoveResult.OnAnchor;
            History.UndoManager.TouchOwner(point.GetObject());
            if (onAnchor) point.anchor = null;
            if (point.anchor is LineAnchor lineAnchor && !lineAnchor.IsDeleted()) {
                lineAnchor.UpdatePercent(position);
                GO.SetPosition(point.GetObject(), lineAnchor.GetPoint());
            } else if (point.anchor is ColumnAnchor column && !column.IsDeleted()) {
                //on the vertical line of another point: that one follows horizontally
                column.MoveTo(position);
                GO.SetPosition(point.GetObject(), column.GetPoint());
            } else {
                GO.SetPosition(point.GetObject(), position);
            }
            return MoveResult.Moved;
        }
    }
}

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
        //the closest position, a point on an anchor can't be moved
        public static MoveResult Move(TerrainPoint point, Vector3 position) {
            if (point.anchor is TerrainAnchor ta && !GO.IsNull(ta) && !ta.IsDeleted()) return MoveResult.OnAnchor;
            History.UndoManager.TouchOwner(point.GetObject());
            if (point.anchor is LineAnchor lineAnchor && !lineAnchor.IsDeleted()) {
                lineAnchor.UpdatePercent(position);
                GO.SetPosition(point.GetObject(), lineAnchor.GetPoint());
            } else {
                GO.SetPosition(point.GetObject(), position);
            }
            return MoveResult.Moved;
        }
    }
}

using System.Collections.Generic;
using Godot;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace Operations {
    //The points of the terrain patches and building lines
    public static class TerrainPoints {
        //Procedural points use ordinary generated anchors; building points can also follow patch corners and edges.
        //Without generatedOnly (the points of the low-level elements) any anchor, segment or terrain point.
        public static IAnchorable AttachmentFromClick(Node3D obj, Vector3 position, bool generatedOnly = true, TerrainPoint point = null) {
            if (obj == null) return null;
            var buildingPoint = generatedOnly && point != null && point.GetLinks().Exists(e => e is BuildingLine);
            var clicked = GS.FindRecursive<TerrainPoint>(obj);
            if (clicked != null && !clicked.IsDeleted() && (!generatedOnly || (buildingPoint && clicked.GetLinks().Exists(e => e is TerrainPatch)))) {
                return DependsOn(clicked, point) ? null : new PointAnchor(clicked);
            }
            var anchor = GS.FindRecursive<TerrainAnchor>(obj);
            if (anchor == null && generatedOnly) anchor = GS.FindRecursive<TerrainPoint>(obj)?.anchor as TerrainAnchor;
            if (anchor != null && !anchor.IsDeleted() && (!generatedOnly || !anchor.lowLevelOnly)) return anchor;
            var link = GS.Cast<PointLink>(obj);
            if (link == null || GO.IsNull(link.next)) return null;
            var start = GO.GetParent(link);
            if (generatedOnly) {
                var a = GS.FindRecursive<TerrainAnchor>(start);
                var b = GS.FindRecursive<TerrainAnchor>(link.next);
                if (a == null || b == null) {
                    var p = GS.FindRecursive<TerrainPoint>(start);
                    var q = GS.FindRecursive<TerrainPoint>(link.next);
                    if (!buildingPoint || p == null || q == null || p.IsDeleted() || q.IsDeleted()
                        || !p.GetLinks().Exists(e => e is TerrainPatch && q.IsLinkedTo(e))
                        || DependsOn(p, point) || DependsOn(q, point)) return null;
                } else if (a.IsDeleted() || b.IsDeleted() || a.lowLevelOnly || b.lowLevelOnly) return null;
            }
            return new LineAnchor(start, link.next, position);
        }

        //the point itself is kept: every element sharing it (including border meshes) follows the attachment.
        public static bool Attach(TerrainPoint point, Node3D obj, Vector3 position, bool generatedOnly = true) {
            var anchor = AttachmentFromClick(obj, position, generatedOnly, point);
            if (point == null || point.IsDeleted() || anchor == null) return false;
            if (anchor is LineAnchor line && (DependsOn(line.StartPoint, point) || DependsOn(line.EndPoint, point))) return false;
            History.UndoManager.Touch(point);
            point.anchor = anchor;
            GO.SetPosition(point.GetObject(), anchor.GetPoint());
            return true;
        }

        public static void Detach(TerrainPoint point) {
            History.UndoManager.Touch(point);
            point.anchor = null;
        }

        //A point must not follow itself, directly or through fixed, segment or vertical attachments.
        public static bool DependsOn(TerrainPoint point, TerrainPoint target, HashSet<TerrainPoint> visited = null) {
            if (point == null) return false;
            if (point == target) return true;
            visited ??= new HashSet<TerrainPoint>();
            if (!visited.Add(point) || GO.IsNull(point.anchor) || point.anchor.IsDeleted()) return false;
            if (point.anchor is PointAnchor fixedPoint) return DependsOn(fixedPoint.master, target, visited);
            if (point.anchor is ColumnAnchor column) return DependsOn(column.master, target, visited);
            if (point.anchor is LineAnchor line) return DependsOn(line.StartPoint, target, visited) || DependsOn(line.EndPoint, target, visited);
            return false;
        }

        public enum MoveResult {
            Moved,
            OnAnchor //a point on an anchor follows it, it can't be moved
        }

        //moves the point as with the gizmo: a free point goes to the position, a point on a segment slides along it to
        //the closest position, a point on the vertical line of another one moves that one horizontally, a point on an
        //anchor can't be moved (unless leaveAnchor: it leaves the anchor, as the points of the low-level elements)
        public static MoveResult Move(TerrainPoint point, Vector3 position, bool leaveAnchor = false) {
            var onAnchor = !GO.IsNull(point.anchor) && !point.anchor.IsDeleted() && !point.anchor.IsMoveable();
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

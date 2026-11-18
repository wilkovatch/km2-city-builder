using System.Collections.Generic;
using Godot;
using GS = GameEngine.Scriptable;

namespace Operations {
    //result of the automatic closing of terrain patches and building lines
    public enum AutoCloseResult {
        NoPresets = -2, //building lines only: no building presets to split the line with
        CannotClose = -1, //the element must have only its first point, on an anchor
        NoLoopFound = 0,
        Closed = 1
    }

    public static class TerrainPatches {
        public static TerrainPatch Create(ElementManager manager) {
            var name = "Terrain " + Actions.Helpers.GetLatestObjectNumber(manager.GetTerrainContainer(), "Terrain ");
            var patch = GS.Create<TerrainPatch>(name);
            patch.Initialize(manager.GetTerrainPointContainer(), manager.GetTerrainContainer());
            manager.AddPatch(patch);
            return patch;
        }

        //a patch with less than three perimeter points is incomplete: it is deleted when its editor is closed
        public static bool IsIncomplete(TerrainPatch patch) {
            return patch != null && patch.GetPerimeterPointCount() < 3;
        }

        //obj is what was clicked: an anchor, an existing terrain point, a point link or nothing (the terrain).
        //pointOnly: false follows the anchors from the previous anchor to the clicked one
        public static TerrainPoint AddPerimeterPoint(TerrainPatch patch, Node3D obj, Vector3 point, bool pointOnly) {
            return patch.AddPerimeterPoint(obj, point, pointOnly);
        }

        public static void AddInternalPoint(TerrainPatch patch, Node3D obj, Vector3 point) {
            patch.AddInternalPoint(obj, point);
        }

        public static void RemoveLastPerimeterPoint(TerrainPatch patch) {
            patch.RemovePerimeterPoint();
        }

        //removes a perimeter or internal point from the patch (the point is deleted if no other element uses it)
        public static bool RemovePoint(TerrainPatch patch, TerrainPoint point) {
            return point.DeleteManual(patch);
        }

        public static AutoCloseResult AutoClose(TerrainPatch patch) {
            return AutoClose(patch, out _);
        }

        //anchors: the anchors of the loop the perimeter was closed with
        public static AutoCloseResult AutoClose(TerrainPatch patch, out List<TerrainAnchor> anchors) {
            var res = patch.AutoClose();
            anchors = res.points ?? new List<TerrainAnchor>();
            return (AutoCloseResult)res.resultCode;
        }

        //fills the patch with internal points at the distance from each other and from the perimeter (only if it has
        //none yet); returns the new points, null if the patch already has internal points
        public static List<TerrainPoint> GenerateInternalPoints(TerrainPatch patch, float distance) {
            if (patch.GetInternalPointsComponents().Count > 0) return null;
            History.UndoManager.Touch(patch);
            patch.AutoGenerateInternalPoints(distance);
            return patch.GetInternalPointsComponents();
        }

        public static void Delete(TerrainPatch patch) {
            patch.Delete();
        }

        //inserts a free point in the perimeter, in the segment closest to the position (or before the point at the
        //index); null if the patch has less than two points (or the index is out of range)
        public static TerrainPoint InsertPerimeterPoint(TerrainPatch patch, Vector3 position, int? index = null) {
            var at = index ?? patch.ClosestPerimeterInsertIndex(position);
            return at < 0 ? null : patch.InsertPerimeterPoint(at, position);
        }

        //a click on a segment of the perimeter of the patch being edited (obj: what was clicked) inserts a free point
        //there, giving the segment a new corner; null if the click is not on one (it is then a point to add, see
        //AddPerimeterPoint)
        public static TerrainPoint InsertOnClickedSegment(TerrainPatch patch, Node3D obj, Vector3 point) {
            var index = AddTerrainPointToSequence.ClickedSegmentInsertIndex(patch.GetPerimeterPointsComponents(), obj, ref point);
            return index < 0 ? null : patch.InsertPerimeterPoint(index, point);
        }

        //null if the patch can be split along the line between the two points of its perimeter, otherwise why not
        public static string GetSplitError(TerrainPatch patch, TerrainPoint a, TerrainPoint b) {
            if (patch == null || patch.IsDeleted()) return "the terrain patch was deleted";
            var perimeter = patch.GetPerimeterPointsComponents();
            var ia = perimeter.IndexOf(a);
            var ib = perimeter.IndexOf(b);
            if (a == null || b == null || ia < 0 || ib < 0) return "both points must be on the perimeter of the terrain patch";
            var between = Mathf.Abs(ia - ib);
            if (ia == ib || between == 1 || between == perimeter.Count - 1) return "the two points must not be next to each other";
            return null;
        }

        //splits the patch along the line between two points of its perimeter: the patch keeps the perimeter from b
        //to a, a new patch with the same properties gets the one from a to b. The two points are shared by both
        //patches (they stay joined when one moves), and each internal point goes to the patch it is in. Returns the new
        //patch (null if it can't be split, see GetSplitError)
        public static TerrainPatch Split(ElementManager manager, TerrainPatch patch, TerrainPoint a, TerrainPoint b) {
            if (GetSplitError(patch, a, b) != null) return null;
            var perimeter = patch.GetPerimeterPointsComponents();
            List<TerrainPoint> Part(int from, int to) {
                var res = new List<TerrainPoint>();
                for (int i = from; ; i = (i + 1) % perimeter.Count) {
                    res.Add(perimeter[i]);
                    if (i == to) break;
                }
                return res;
            }
            var kept = Part(perimeter.IndexOf(b), perimeter.IndexOf(a));
            var moved = Part(perimeter.IndexOf(a), perimeter.IndexOf(b));
            var movedPolygon = new List<Vector3>();
            foreach (var point in moved) movedPolygon.Add(point.GetPoint());
            var keptInternals = new List<TerrainPoint>();
            var movedInternals = new List<TerrainPoint>();
            foreach (var point in patch.GetInternalPointsComponents()) {
                (GeometryHelper.IsPointInsidePolygon(point.GetPoint(), movedPolygon) ? movedInternals : keptInternals).Add(point);
            }
            History.UndoManager.Touch(patch);
            var newPatch = Create(manager);
            newPatch.state = (ObjectState)patch.state.Clone();
            //the new patch first: the points it gets always belong to a patch
            newPatch.SetPoints(moved, movedInternals);
            patch.SetPoints(kept, keptInternals);
            manager.FlagAsChanged();
            return newPatch;
        }

        static bool SamePoint(TerrainPoint a, TerrainPoint b) {
            if (a == b) return true;
            if (a.anchor is TerrainAnchor anchor && !GameEngine.GameObject.IsNull(anchor) && AddTerrainPointToSequence.AnchorEquals(b.anchor, anchor)) return true;
            return GeometryHelper.AreVectorsEqual(a.GetPoint(), b.GetPoint());
        }

        //the perimeter of the two patches merged (null if they can't be, with why not): they must share one
        //continuous part of their perimeters, of two points or more (the same points, points on the same anchors or
        //at the same positions)
        static List<TerrainPoint> MergedPerimeter(TerrainPatch a, TerrainPatch b, out string error) {
            error = null;
            if (a == null || b == null || a.IsDeleted() || b.IsDeleted()) error = "a terrain patch was deleted";
            else if (a == b) error = "a terrain patch can't be merged with itself";
            if (error != null) return null;
            var pa = a.GetPerimeterPointsComponents();
            var pb = b.GetPerimeterPointsComponents();
            var match = new int[pa.Count];
            var shared = 0;
            for (int i = 0; i < pa.Count; i++) {
                match[i] = pb.FindIndex(p => SamePoint(pa[i], p));
                if (match[i] >= 0) shared++;
            }
            if (shared < 2) {
                error = "the terrain patches must share a side (two points or more of their perimeters)";
                return null;
            }
            if (shared == pa.Count || shared == pb.Count) {
                error = "one terrain patch has no points of its own";
                return null;
            }
            //the shared part of a starts where a shared point follows one that isn't
            var start = -1;
            var runs = 0;
            for (int i = 0; i < pa.Count; i++) {
                if (match[i] >= 0 && match[(i + pa.Count - 1) % pa.Count] < 0) {
                    start = i;
                    runs++;
                }
            }
            if (runs != 1) {
                error = "the terrain patches must share a single continuous side";
                return null;
            }
            var res = new List<TerrainPoint>();
            //from the last shared point, along the points of a only, to the first shared one
            for (int t = 0; t <= pa.Count - shared + 1; t++) res.Add(pa[(start + shared - 1 + t) % pa.Count]);
            //then along the points of b only, from the first shared point to the last one
            var isShared = new bool[pb.Count];
            foreach (var j in match) {
                if (j >= 0) isShared[j] = true;
            }
            var first = match[start];
            var step = isShared[(first + 1) % pb.Count] ? pb.Count - 1 : 1;
            var own = 0;
            for (int j = (first + step) % pb.Count; !isShared[j]; j = (j + step) % pb.Count) {
                res.Add(pb[j]);
                own++;
            }
            if (own != pb.Count - shared) {
                error = "the terrain patches must share a single continuous side";
                return null;
            }
            return res;
        }

        //null if the two patches can be merged, otherwise why not
        public static string GetMergeError(TerrainPatch patch, TerrainPatch other) {
            MergedPerimeter(patch, other, out var error);
            return error;
        }

        //merges the other patch into the patch, which keeps its properties: the side they share is removed (see
        //MergedPerimeter), the internal points of both are kept, the other patch is deleted. False if they can't be
        //merged (see GetMergeError)
        public static bool Merge(ElementManager manager, TerrainPatch patch, TerrainPatch other) {
            var perimeter = MergedPerimeter(patch, other, out _);
            if (perimeter == null) return false;
            History.UndoManager.Touch(patch);
            History.UndoManager.Touch(other);
            var internals = patch.GetInternalPointsComponents();
            foreach (var point in other.GetInternalPointsComponents()) {
                if (!internals.Contains(point)) internals.Add(point);
            }
            //before the other patch is deleted: the points it gives already belong to the patch
            patch.SetPoints(perimeter, internals);
            other.Delete();
            manager.FlagAsChanged();
            return true;
        }
    }
}

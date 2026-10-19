namespace Operations {
    public static class Intersections {
        public static bool CanDelete(Intersection intersection) {
            return intersection != null && !intersection.IsDeleted();
        }

        public static void Delete(Intersection intersection) {
            intersection.Delete();
        }

        //null if the two roads of the intersection can be merged into the given one (see RoadMerger), otherwise why not
        public static string GetMergeError(Intersection intersection, Road keep) {
            return RoadMerger.GetMergeError(intersection, keep);
        }

        public static bool CanMerge(Intersection intersection, Road keep) {
            return GetMergeError(intersection, keep) == null;
        }

        //merges the two roads of the intersection into the given one (keeping its settings), deleting the intersection
        public static Road Merge(ElementManager manager, Intersection intersection, Road keep) {
            return RoadMerger.Merge(manager, intersection, keep);
        }

        //intersections are not created on their own, but by connecting roads (see RoadDrawing)
        public static void Move(Intersection intersection, Godot.Vector3 position) {
            History.UndoManager.Touch(intersection);
            GameEngine.GameObject.SetPosition(intersection.point, position);
        }
    }
}

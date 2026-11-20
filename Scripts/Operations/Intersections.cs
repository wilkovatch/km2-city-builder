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

        //whether the two roads of the intersection join seamlessly (no surface, the same direction at the joint); set
        //with more roads too, but without effect
        public static bool IsSeamless(Intersection intersection) {
            return intersection.IsSeamlessSet();
        }

        public static void SetSeamless(Intersection intersection, bool seamless) {
            History.UndoManager.Touch(intersection);
            intersection.instanceState.SetBool("seamless", seamless);
        }

        //the own segments of the sidewalk starting at a road (index in intersection.roads, see
        //IntersectionGenerator.SidewalkInfo.road): innerSegments, outerSegments, outerSkipsCrosswalks; null if it has
        //those of the intersection
        public static ObjectState GetSidewalkOverride(Intersection intersection, int road) {
            return intersection.GetSidewalkOverride(road);
        }

        //values null: back to the segments of the intersection
        public static void SetSidewalkOverride(Intersection intersection, int road, ObjectState values) {
            History.UndoManager.Touch(intersection);
            intersection.SetSidewalkOverride(road, values);
        }

        //a new override with the current segments of the intersection
        public static ObjectState SidewalkOverrideFromIntersection(Intersection intersection) {
            var s = new ObjectState();
            s.SetInt("innerSegments", intersection.state.Int("sidewalkSegments", 1));
            s.SetInt("outerSegments", intersection.state.Int("sidewalkOuterSegments"));
            s.SetBool("outerSkipsCrosswalks", intersection.state.Bool("sidewalkOuterSkipsCrosswalks"));
            return s;
        }

        //intersections are not created on their own, but by connecting roads (see RoadDrawing)
        public static void Move(Intersection intersection, Godot.Vector3 position) {
            History.UndoManager.Touch(intersection);
            GameEngine.GameObject.SetPosition(intersection.point, position);
        }
    }
}

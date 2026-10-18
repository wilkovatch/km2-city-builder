namespace Operations {
    public static class Intersections {
        public static bool CanDelete(Intersection intersection) {
            return intersection != null && !intersection.IsDeleted();
        }

        public static void Delete(Intersection intersection) {
            intersection.Delete();
        }

        //intersections are not created on their own, but by connecting roads (see RoadDrawing)
        public static void Move(Intersection intersection, Godot.Vector3 position) {
            History.UndoManager.Touch(intersection);
            GameEngine.GameObject.SetPosition(intersection.point, position);
        }
    }
}

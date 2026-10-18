using Godot;

namespace Operations {
    //Editing of roads (the parts not done point by point, see RoadDrawing)
    public static class Roads {
        //a road with less than two points is incomplete: it is deleted when its editor is closed
        public static bool IsIncomplete(Road road) {
            return road != null && road.points.Count < 2;
        }

        //a single control point can be deleted only if the road keeps at least two
        public static bool CanRemoveControlPoint(Road road, StaticBody3D point) {
            return road != null && point != null && road.points.Count > 2 && road.points.Contains(point);
        }

        public static void RemoveControlPoint(Road road, StaticBody3D point) {
            History.UndoManager.Touch(road);
            road.points.Remove(point);
            GameEngine.GameObject.Destroy(point);
        }

        //removes the last control point (detaching the road from its end intersection)
        public static void RemoveLastPoint(Road road) {
            road?.RemovePoint();
        }

        public static void Delete(Road road) {
            road.Delete();
        }

        public static ObjectState GetPropRule(Road road, bool createIfNull) {
            var res = road.state.State("propRule");
            if (res == null && createIfNull) {
                res = new ObjectState(true);
                SetPropRule(road, res);
            }
            return res;
        }

        public static void SetPropRule(Road road, ObjectState rule) {
            road.state.SetState("propRule", rule);
        }

        public static bool IsPropContainerEnabled(Road road, string container) {
            var rule = GetPropRule(road, false);
            return rule != null && rule.State(container) != null;
        }

        public static void EnablePropContainer(Road road, string container, bool enabled) {
            GetPropRule(road, true).SetState(container, enabled ? new ObjectState(true) : null);
            road.state.FlagAsChanged();
        }

        //the props containers available for the type of the road
        public static string[] GetPropContainerNames(Road road) {
            var type = road.GetRoadType();
            return type?.typeData.settings.propsContainers ?? new string[0];
        }
    }
}

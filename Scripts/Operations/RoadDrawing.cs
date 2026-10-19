using System.Collections.Generic;
using Godot;
using GS = GameEngine.Scriptable;
using GO = GameEngine.GameObject;

namespace Operations {
    //Drawing a road point by point: the points added at its end, and its connections to road ends, intersections and the
    //middle of other roads (which are split). Used by the road placer for the clicks and by the API for the same actions.
    public class RoadDrawing {
        readonly ElementManager manager;
        Road curRoad = null;
        Node3D startIntersectionPoint = null;
        //the road started on the middle of another road, which is split when the second point is placed
        (Road road, Vector3 point, Node3D firstPoint)? pendingStartSplit = null;
        const float MinSplitDistance = 1.0f; //between the two ends of a road connected to the middle of the same road

        public RoadDrawing(ElementManager manager) {
            this.manager = manager;
        }

        //the road being drawn, without creating it
        public Road CurrentRoad => curRoad;

        void CreateRoad() {
            curRoad = manager.CreateRoad();
            pendingStartSplit = null;
        }

        public void SetRoad(Road road) {
            var newRoad = GO.IsNull(road) ? null : road;
            if (newRoad != curRoad) pendingStartSplit = null;
            curRoad = newRoad;
        }

        public Road GetRoad(bool createIfNull = true) {
            if (GO.IsNull(curRoad)) curRoad = null;
            if (createIfNull && curRoad == null) CreateRoad();
            return curRoad;
        }

        bool IsPendingStartSplitValid() {
            if (!pendingStartSplit.HasValue) return false;
            var pending = pendingStartSplit.Value;
            var road = GetRoad(false);
            return road != null && road.points.Count > 0 && road.points[0] == pending.firstPoint
                && !GO.IsNull(pending.road) && !pending.road.deleted;
        }

        //splits the road (or the part of it created by a split made with the same click) at the point of its curve closest to the given one
        Intersection SplitRoad(Road road, Vector3 point, ref Node3D otherRoadHandle, Dictionary<Road, Road> splitParts) {
            var candidates = new List<Road> { road };
            if (splitParts.TryGetValue(road, out var part)) candidates.Add(part);
            Road best = null;
            (int segment, float fraction, float distance) bestPos = (-1, 0, float.MaxValue);
            foreach (var candidate in candidates) {
                if (GO.IsNull(candidate) || candidate.deleted) continue;
                candidate.UpdateLine(); //the curve of a road split by the same click is not updated yet
                var pos = RoadSplitter.GetClosestCurvePosition(candidate, point);
                if (pos.segment >= 0 && pos.distance < bestPos.distance) {
                    best = candidate;
                    bestPos = pos;
                }
            }
            if (best == null) return null;
            var oldEnd = best.points[best.points.Count - 1];
            var (intersection, newRoad) = RoadSplitter.Split(manager, best, bestPos.segment, bestPos.fraction);
            if (intersection == null) return null;
            //the road being drawn is connected to it: not a joint of the parts of the split road
            intersection.instanceState.SetBool("seamless", false);
            splitParts[road] = newRoad;
            //the end of the split road is now the end of the new road
            var newEnd = newRoad.points[newRoad.points.Count - 1];
            if (startIntersectionPoint == oldEnd) startIntersectionPoint = newEnd;
            if (otherRoadHandle == oldEnd) otherRoadHandle = newEnd;
            return intersection;
        }

        //the road started on the middle of another road, which is split now that the second point is placed
        void ExecutePendingStartSplit(ref Node3D otherRoadHandle, Dictionary<Road, Road> splitParts) {
            if (!pendingStartSplit.HasValue) return;
            var valid = IsPendingStartSplitValid();
            var pending = pendingStartSplit.Value;
            pendingStartSplit = null;
            if (!valid) return;
            var intersection = SplitRoad(pending.road, pending.point, ref otherRoadHandle, splitParts);
            if (intersection != null) startIntersectionPoint = intersection.point;
        }

        //connects the road to a road end or an intersection
        void ConnectTo(Node3D otherRoadHandle) {
            if (GetRoad().points.Count == 0) {
                GetRoad().AddPoint(GO.GetPosition(otherRoadHandle));
                GS.Cast<Handle>(GetRoad().points[0]).SetScale(GameEngine.Vector3.one * 5);
                startIntersectionPoint = otherRoadHandle;
            } else {
                if (startIntersectionPoint == null) {
                    PlaceIntersection(otherRoadHandle);
                } else {
                    if (GetRoad().points.Count == 1 && startIntersectionPoint == otherRoadHandle) return;
                    GetRoad().AddPoint(GO.GetPosition(otherRoadHandle));
                    PlaceIntersection(startIntersectionPoint, false);
                    startIntersectionPoint = null;
                    GetRoad().RemovePoint(true);
                    PlaceIntersection(otherRoadHandle);
                }
            }
        }

        //connects the road to the middle of another road, splitting it
        public void ConnectToMiddle(Road otherRoad, Vector3 point) {
            var road = GetRoad();
            if (road.points.Count == 0) {
                road.AddPoint(point);
                GS.Cast<Handle>(road.points[0]).SetScale(GameEngine.Vector3.one * 5);
                startIntersectionPoint = null;
                pendingStartSplit = (otherRoad, point, road.points[0]);
                return;
            }
            if (road.points.Count == 1 && IsPendingStartSplitValid()) {
                var pending = pendingStartSplit.Value;
                if (pending.road == otherRoad && GameEngine.Vector3.Distance(pending.point, point) < MinSplitDistance) return;
            }
            var splitParts = new Dictionary<Road, Road>();
            Node3D otherRoadHandle = null;
            ExecutePendingStartSplit(ref otherRoadHandle, splitParts);
            var intersection = SplitRoad(otherRoad, point, ref otherRoadHandle, splitParts);
            if (intersection != null) ConnectTo(intersection.point);
        }

        //connects the road to a road end or an intersection, after splitting the road its start is on
        public void ConnectToHandle(Node3D otherRoadHandle) {
            ExecutePendingStartSplit(ref otherRoadHandle, new Dictionary<Road, Road>());
            ConnectTo(otherRoadHandle);
        }

        //adds a point (at the given index, at the end if -1), connecting the start of the road if it was placed on a road end, an intersection or the middle of a road
        public void AddPoint(Vector3 point, int index = -1) {
            var road = GetRoad();
            road.AddPoint(point, index);
            Node3D noHandle = null;
            ExecutePendingStartSplit(ref noHandle, new Dictionary<Road, Road>());
            if (startIntersectionPoint != null) {
                PlaceIntersection(startIntersectionPoint, false);
                startIntersectionPoint = null;
            }
        }

        //true if the handle is a road end or an intersection point, which the road can be connected to
        public static bool IsConnectionHandle(Node3D handle) {
            if (GO.IsNull(handle)) return false;
            var otherRoad = GS.FindInParent<Road>(handle);
            if (otherRoad != null) {
                return otherRoad.points[0] == handle || otherRoad.points[otherRoad.points.Count - 1] == handle;
            }
            return GS.FindRecursive<Intersection.IntersectionComponent>(handle) != null;
        }

        //connects the current road to the road end or intersection of the handle (at its start if addPoint is false)
        public void PlaceIntersection(Node3D otherRoadHandle, bool addPoint = true) {
            var otherRoad = GS.FindInParent<Road>(otherRoadHandle);
            var intersectionComponent = GS.FindRecursive<Intersection.IntersectionComponent>(otherRoadHandle);
            if (otherRoad != null || intersectionComponent != null) {
                var proj = GO.GetPosition(otherRoadHandle);
                var road = GetRoad();
                //the links are set directly on both roads and on the intersection
                History.UndoManager.Touch(road);
                History.UndoManager.Touch(otherRoad);
                History.UndoManager.Touch(intersectionComponent?.intersection);
                if (otherRoad != null) {
                    History.UndoManager.Touch(otherRoad.startIntersection);
                    History.UndoManager.Touch(otherRoad.endIntersection);
                }
                if (addPoint) road.AddPoint(proj);
                Intersection intersection;
                if (intersectionComponent == null) {
                    var isStart = otherRoadHandle == otherRoad.points[0];
                    if (isStart && otherRoad.startIntersection != null) {
                        intersection = otherRoad.startIntersection;
                    }
                    else if (!isStart && otherRoad.endIntersection != null) {
                        intersection = otherRoad.endIntersection;
                    }
                    else {
                        intersection = manager.CreateIntersection(proj);
                        if (isStart) otherRoad.startIntersection = intersection;
                        if (!isStart) otherRoad.endIntersection = intersection;
                        intersection.roads.Add(otherRoad);
                    }
                } else {
                    intersection = intersectionComponent.intersection;
                }
                intersection.roads.Add(road);
                if (!addPoint) {
                    road.startIntersection = intersection;
                } else {
                    road.endIntersection = intersection;
                }
                if (otherRoad != null) otherRoad.RebuildLine();
                manager.ShowIntersections(true);
                road.SetActive(true, false);
            }
        }
    }
}

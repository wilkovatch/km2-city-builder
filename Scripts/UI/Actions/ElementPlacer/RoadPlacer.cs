using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using GS = GameEngine.Scriptable;
using GO = GameEngine.GameObject;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace ElementPlacer {
    public class RoadPlacer : TerrainAction {
        Road curRoad = null;
        ElementManager manager;
        Node3D startIntersectionPoint = null;
        //the road started on the middle of another road, which is split when the second point is placed
        (Road road, Vector3 point, Node3D firstPoint)? pendingStartSplit = null;
        const float MinSplitDistance = 1.0f; //between the two ends of a road connected to the middle of the same road
        public enum PlacementMode {
            None,
            Add,
            Insert
        }

        public PlacementMode placementMode = PlacementMode.Add;

        public RoadPlacer(ElementManager manager) {
            this.manager = manager;
        }

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

        bool DetectIntersection(List<RaycastHit?> hits) {
            bool placeIntersection = false;
            var otherRoadHandle = (Node3D)hits[0].Value.collider;
            var otherRoad = GS.FindInParent<Road>(otherRoadHandle);
            var intersection = GS.FindRecursive<Intersection.IntersectionComponent>(otherRoadHandle);
            if (otherRoad != null) {
                if (otherRoad.points[0] == otherRoadHandle || otherRoad.points[otherRoad.points.Count - 1] == otherRoadHandle) {
                    placeIntersection = true;
                }
            } else if (intersection != null) {
                placeIntersection = true;
            }
            return placeIntersection;
        }

        //the point on the line along another road under the cursor, to connect the road to the middle of that one
        (Road road, Vector3 point)? GetSplitTarget(List<RaycastHit?> hits) {
            if (hits.Count < 4 || !hits[3].HasValue) return null;
            var line = hits[3].Value.collider as RoadSplitLine;
            if (GO.IsNull(line)) return null;
            var road = line.GetRoad();
            if (GO.IsNull(road) || road.deleted || road == curRoad) return null;
            var res = line.Resolve(hits[3].Value.point);
            if (!res.HasValue) return null;
            return (road, res.Value.point);
        }

        //-1: nothing, 0: point on the terrain, 1: road end or intersection, 2: middle of another road
        int GetPlaceIntersection(List<RaycastHit?> hits) {
            if (placementMode == PlacementMode.Insert) return 0;
            var placeIntersection = false;
            if (!hits[1].HasValue) {
                if (!hits[0].HasValue) return GetSplitTarget(hits).HasValue ? 2 : -1;
                placeIntersection = DetectIntersection(hits);
            } else {
                if (hits[0].HasValue) placeIntersection = DetectIntersection(hits);
            }
            if (placeIntersection) return 1;
            return GetSplitTarget(hits).HasValue ? 2 : 0;
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
        void ConnectToMiddle(Road otherRoad, Vector3 point) {
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

        public override void Apply(List<RaycastHit?> hits) {
            if (placementMode == PlacementMode.None) return;
            var target = GameEngine.Gizmo.GetMainTargetRoot() as StaticBody3D;
            if (placementMode == PlacementMode.Insert && (target == null || Input.GetInputPressed(KeyCommand.Modifier2))) return;
            //clicks on a road point or on the gizmo select or move a point instead
            if (placementMode == PlacementMode.Insert && (hits[2].HasValue || GameEngine.Gizmo.IsNearAxis())) return;
            ShowOnMap(hits);
            if (!Input.GetInputStart(KeyCommand.Select)) return;
            if (GetRoad().endIntersection != null && placementMode != PlacementMode.Insert) return;
            manager.worldChanged = true;

            var placeIntersection = GetPlaceIntersection(hits);
            if (placeIntersection == -1) return;
            if (placeIntersection == 2) {
                //as below, the new intersection point would be picked up by the gizmo
                GameEngine.Gizmo.ConsumeClick();
                var splitTarget = GetSplitTarget(hits).Value;
                ConnectToMiddle(splitTarget.road, splitTarget.point);
            } else if (placeIntersection == 1) {
                //connecting makes the intersection point selectable (layer 8) right under the cursor, the gizmo would pick it up with this same click
                GameEngine.Gizmo.ConsumeClick();
                if (hits[0].HasValue) ConnectToHandle((Node3D)hits[0].Value.collider);
            } else {
                if (hits[1].HasValue) AddTerrainPoint(hits[1].Value.point, target);
            }
        }

        //connects the road to a road end or an intersection, after splitting the road its start is on
        void ConnectToHandle(Node3D otherRoadHandle) {
            ExecutePendingStartSplit(ref otherRoadHandle, new Dictionary<Road, Road>());
            ConnectTo(otherRoadHandle);
        }

        //adds a point (after the selected one, if any), connecting the start of the road if it was placed on a road end, an intersection or the middle of a road
        void AddTerrainPoint(Vector3 point, StaticBody3D selectedPoint) {
            var road = GetRoad();
            road.AddPoint(point, selectedPoint != null ? (road.points.IndexOf(selectedPoint) + 1) : -1);
            Node3D noHandle = null;
            ExecutePendingStartSplit(ref noHandle, new Dictionary<Road, Road>());
            if (startIntersectionPoint != null) {
                PlaceIntersection(startIntersectionPoint, false);
                startIntersectionPoint = null;
            }
        }

        void PlaceIntersection(Node3D otherRoadHandle, bool addPoint = true) {
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

        public override void ShowOnMap(List<RaycastHit?> hits) {
            var placeIntersection = GetPlaceIntersection(hits);
            if (placeIntersection == 1) {
                if (hits[0].HasValue) ActionHandlerManager.ShowHandle(GO.GetPosition((Node3D)hits[0].Value.collider), 4.0f, GameEngine.Color.yellow);
            } else if (placeIntersection == 0) {
                if (hits[1].HasValue) ActionHandlerManager.ShowHandle(hits[1].Value.point, 1.0f, GameEngine.Color.green);
            } else if (placeIntersection == 2) {
                ActionHandlerManager.ShowHandle(GetSplitTarget(hits).Value.point, 4.0f, GameEngine.Color.yellow);
            }
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { 1 << 9, 1 << 3, 1 << 8, 1 << RoadSplitLine.Layer };
        }
    }
}

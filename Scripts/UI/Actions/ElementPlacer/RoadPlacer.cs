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
            var name = "Road " + Actions.Helpers.GetLatestObjectNumber(manager.GetRoadContainer(), "Road ");
            curRoad = GS.Create<Road>(name);
            curRoad.Initialize();
            if (manager != null) {
                manager.AddRoad(curRoad);
                GO.SetParent(curRoad, manager.GetRoadContainer());
            }
        }

        public void SetRoad(Road road) {
            curRoad = GO.IsNull(road) ? null : road;
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

        int GetPlaceIntersection(List<RaycastHit?> hits) {
            if (placementMode == PlacementMode.Insert) return 0;
            var placeIntersection = false;
            if (!hits[1].HasValue) {
                if (!hits[0].HasValue) return -1;
                placeIntersection = DetectIntersection(hits);
            } else {
                if (hits[0].HasValue) placeIntersection = DetectIntersection(hits);
            }
            return placeIntersection ? 1 : 0;
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
            if (placeIntersection == 1) {
                //connecting makes the intersection point selectable (layer 8) right under the cursor, the gizmo would pick it up with this same click
                GameEngine.Gizmo.ConsumeClick();
                if (hits[0].HasValue) {
                    var otherRoadHandle = (Node3D)hits[0].Value.collider;
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
            } else {
                if (hits[1].HasValue) {
                    var road = GetRoad();
                    road.AddPoint(hits[1].Value.point, target != null ? (road.points.IndexOf(target) + 1) : -1);
                    if (startIntersectionPoint != null) {
                        PlaceIntersection(startIntersectionPoint, false);
                        startIntersectionPoint = null;
                    }
                }
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
                        var newObj = (StaticBody3D)GameEngine.Resources.InstantiateObject("Handle");
                        GO.SetPosition(newObj, proj);
                        var handle = GS.Cast<Handle>(newObj);
                        handle.SetScale(GameEngine.Vector3.one * 3);
                        handle.SetHandleActive(false);
                        intersection = new Intersection(newObj, manager.GetRoadContainer());
                        if (isStart) otherRoad.startIntersection = intersection;
                        if (!isStart) otherRoad.endIntersection = intersection;
                        intersection.roads.Add(otherRoad);
                        manager.AddIntersection(intersection);
                        GO.SetParent(intersection.geo, manager.GetRoadContainer());
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
            }
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { 1 << 9, 1 << 3, 1 << 8 };
        }
    }
}

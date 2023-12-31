using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElementPlacer {
    public class RoadPlacer : TerrainAction {
        GameObject curRoad = null;
        ElementManager manager;
        Transform startIntersectionPoint = null;
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
            curRoad = new GameObject();
            curRoad.name = "Road " + Actions.Helpers.GetLatestObjectNumber(manager.GetRoadContainer(), "Road "); ;
            var curve = curRoad.AddComponent<Road>();
            curve.Initialize();
            if (manager != null) {
                manager.roads.Add(curve);
                curRoad.transform.parent = manager.GetRoadContainer().transform;
            }
        }

        public void SetRoad(Road road) {
            curRoad = road != null ? road.gameObject : null;
        }

        public Road GetRoad(bool createIfNull = true) {
            if (createIfNull && curRoad == null) CreateRoad();
            return curRoad == null ? null : curRoad.GetComponent<Road>();
        }

        bool DetectIntersection(List<RaycastHit?> hits) {
            bool placeIntersection = false;
            var otherRoadHandle = hits[0].Value.transform.gameObject;
            var otherRoad = otherRoadHandle.GetComponentInParent<Road>();
            var intersection = otherRoadHandle.GetComponent<Intersection.IntersectionComponent>();
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
            var target = manager.builder.gizmo.mainTargetRoot;
            if (placementMode == PlacementMode.Insert && (target == null || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) return;
            ShowOnMap(hits);
            if (!Input.GetMouseButtonDown(0)) return;
            if (GetRoad().GetComponent<Road>().endIntersection != null && placementMode != PlacementMode.Insert) return;
            manager.worldChanged = true;

            var placeIntersection = GetPlaceIntersection(hits);
            if (placeIntersection == -1) return;
            if (placeIntersection == 1) {
                if (hits[0].HasValue) {
                    var otherRoadHandle = hits[0].Value.transform.gameObject;
                    if (GetRoad().points.Count == 0) {
                        GetRoad().GetComponent<Road>().AddPoint(otherRoadHandle.transform.position);
                        GetRoad().GetComponent<Road>().points[0].GetComponent<Handle>().SetScale(Vector3.one * 5);
                        startIntersectionPoint = otherRoadHandle.transform;
                    } else {
                        if (startIntersectionPoint == null) {
                            PlaceIntersection(otherRoadHandle);
                        } else {
                            if (GetRoad().points.Count == 1 && startIntersectionPoint == otherRoadHandle.transform) return;
                            GetRoad().GetComponent<Road>().AddPoint(otherRoadHandle.transform.position);
                            PlaceIntersection(startIntersectionPoint.gameObject, false);
                            startIntersectionPoint = null;
                            GetRoad().GetComponent<Road>().RemovePoint(true);
                            PlaceIntersection(otherRoadHandle);
                        }
                    }
                }
            } else {
                if (hits[1].HasValue) {
                    var road = GetRoad().GetComponent<Road>();
                    road.AddPoint(hits[1].Value.point, target != null ? (road.points.IndexOf(target.gameObject) + 1) : -1);
                    if (startIntersectionPoint != null) {
                        PlaceIntersection(startIntersectionPoint.gameObject, false);
                        startIntersectionPoint = null;
                    }
                }
            }
        }

        void PlaceIntersection(GameObject otherRoadHandle, bool addPoint = true) {
            var otherRoad = otherRoadHandle.GetComponentInParent<Road>();
            var intersectionComponent = otherRoadHandle.GetComponent<Intersection.IntersectionComponent>();
            if (otherRoad != null || intersectionComponent != null) {
                var proj = otherRoadHandle.transform.position;
                var road = GetRoad().GetComponent<Road>();
                if (addPoint) road.AddPoint(proj);
                Intersection intersection;
                if (intersectionComponent == null) {
                    var isStart = otherRoadHandle == otherRoad.points[0];
                    if (isStart && otherRoad.startIntersection != null) {
                        intersection = otherRoad.startIntersection;
                    } else if (!isStart && otherRoad.endIntersection != null) {
                        intersection = otherRoad.endIntersection;
                    } else {
                        var newObj = Object.Instantiate(Resources.Load<GameObject>("Handle"));
                        newObj.transform.position = proj;
                        newObj.GetComponent<Handle>().SetScale(Vector3.one * 3);
                        newObj.GetComponent<MeshRenderer>().enabled = false;
                        newObj.GetComponent<BoxCollider>().enabled = false;
                        intersection = new Intersection(newObj, manager.GetRoadContainer());
                        if (isStart) otherRoad.startIntersection = intersection;
                        if (!isStart) otherRoad.endIntersection = intersection;
                        intersection.roads.Add(otherRoad);
                        manager.intersections.Add(intersection);
                        intersection.geo.transform.parent = manager.GetRoadContainer().transform;
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
                if (hits[0].HasValue) ActionHandlerManager.ShowHandle(hits[0].Value.transform.position, 4.0f, Color.yellow);
            } else if (placeIntersection == 0) {
                if (hits[1].HasValue) ActionHandlerManager.ShowHandle(hits[1].Value.point, 1.0f, Color.green);
            }
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { 1 << 9, 1 << 3 };
        }
    }
}

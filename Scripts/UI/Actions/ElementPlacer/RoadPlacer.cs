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
        readonly ElementManager manager;
        readonly Operations.RoadDrawing drawing;
        public enum PlacementMode {
            None,
            Add,
            AddBeforeFirst,
            Insert,
            InsertBefore,
            Split
        }

        public PlacementMode placementMode = PlacementMode.Add;

        //the points are added before the first one, and the connections made at the start of the road
        bool AtStart => placementMode == PlacementMode.AddBeforeFirst;

        //the points are inserted next to the selected one
        bool IsInsert => placementMode == PlacementMode.Insert || placementMode == PlacementMode.InsertBefore;

        public RoadPlacer(ElementManager manager) {
            this.manager = manager;
            drawing = new Operations.RoadDrawing(manager);
        }

        public Operations.RoadDrawing Drawing => drawing;

        public void SetRoad(Road road) {
            drawing.SetRoad(road);
        }

        public Road GetRoad(bool createIfNull = true) {
            return drawing.GetRoad(createIfNull);
        }

        bool DetectIntersection(List<RaycastHit?> hits) {
            return Operations.RoadDrawing.IsConnectionHandle((Node3D)hits[0].Value.collider);
        }

        //the point on the line along another road under the cursor, to connect the road to the middle of that one
        //(in split mode, the point on the line along the edited road, to split it there)
        (Road road, Vector3 point)? GetSplitTarget(List<RaycastHit?> hits) {
            if (hits.Count < 4 || !hits[3].HasValue) return null;
            var line = hits[3].Value.collider as RoadSplitLine;
            if (GO.IsNull(line)) return null;
            var road = line.GetRoad();
            if (GO.IsNull(road) || road.deleted || (road == drawing.CurrentRoad) != (placementMode == PlacementMode.Split)) return null;
            var res = line.Resolve(hits[3].Value.point);
            if (!res.HasValue) return null;
            return (road, res.Value.point);
        }

        //-1: nothing, 0: point on the terrain, 1: road end or intersection, 2: middle of another road (of the edited one in split mode)
        int GetPlaceIntersection(List<RaycastHit?> hits) {
            if (IsInsert) return 0;
            if (placementMode == PlacementMode.Split) return GetSplitTarget(hits).HasValue ? 2 : -1;
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

        public override void Apply(List<RaycastHit?> hits) {
            if (placementMode == PlacementMode.None) return;
            if (placementMode == PlacementMode.Split) {
                ApplySplit(hits);
                return;
            }
            var target = GameEngine.Gizmo.GetMainTargetRoot() as StaticBody3D;
            if (IsInsert && (target == null || Input.GetInputPressed(KeyCommand.Modifier2))) return;
            //clicks on a road point or on the gizmo select or move a point instead
            if (IsInsert && (hits[2].HasValue || GameEngine.Gizmo.IsNearAxis())) return;
            ShowOnMap(hits);
            if (!Input.GetInputStart(KeyCommand.Select)) return;
            if (!IsInsert && (AtStart ? GetRoad().startIntersection : GetRoad().endIntersection) != null) return;
            if (IsInsert && !CanInsertNextTo(target)) return;
            manager.worldChanged = true;

            var placeIntersection = GetPlaceIntersection(hits);
            if (placeIntersection == -1) return;
            if (placeIntersection == 2) {
                //as below, the new intersection point would be picked up by the gizmo
                GameEngine.Gizmo.ConsumeClick();
                var splitTarget = GetSplitTarget(hits).Value;
                drawing.ConnectToMiddle(splitTarget.road, splitTarget.point, AtStart);
            } else if (placeIntersection == 1) {
                //connecting makes the intersection point selectable (layer 8) right under the cursor, the gizmo would pick it up with this same click
                GameEngine.Gizmo.ConsumeClick();
                if (hits[0].HasValue) drawing.ConnectToHandle((Node3D)hits[0].Value.collider, AtStart);
            } else {
                if (hits[1].HasValue) drawing.AddPoint(hits[1].Value.point, GetNewPointIndex(target));
            }
        }

        //where a point clicked on the terrain goes: next to the selected point if any, otherwise at the end (or before the start)
        public int GetNewPointIndex(StaticBody3D target) {
            var index = target != null ? GetRoad().points.IndexOf(target) : -1;
            var before = AtStart || placementMode == PlacementMode.InsertBefore;
            if (index < 0) return before ? 0 : -1;
            return before ? index : index + 1;
        }

        //a point inserted next to the selected one can't go beyond an end on an intersection
        public bool CanInsertNextTo(StaticBody3D target) {
            var road = GetRoad();
            var index = road.points.IndexOf(target);
            if (index < 0) return false;
            if (placementMode == PlacementMode.InsertBefore) return index > 0 || road.startIntersection == null;
            return index < road.points.Count - 1 || road.endIntersection == null;
        }

        //split mode: clicking the line along the edited road splits it there, the road keeps the first part
        void ApplySplit(List<RaycastHit?> hits) {
            var road = GetRoad(false);
            if (road == null || road.points.Count < 2) return;
            //the line along the edited road is otherwise hidden (see Road.SetActive)
            road.splitLine.SetActive(true);
            //clicks on a road point or on the gizmo select or move a point instead
            if (hits[2].HasValue || GameEngine.Gizmo.IsNearAxis()) return;
            ShowOnMap(hits);
            if (!Input.GetInputStart(KeyCommand.Select)) return;
            var splitTarget = GetSplitTarget(hits);
            if (!splitTarget.HasValue) return;
            //as when connecting, the new intersection point would be picked up by the gizmo
            GameEngine.Gizmo.ConsumeClick();
            var (intersection, _) = Operations.Roads.Split(manager, road, splitTarget.Value.point);
            if (intersection == null) return;
            //a selected point may now be on the new road, whose points are hidden like the ones of the other roads
            GameEngine.Gizmo.ClearTargets();
            manager.ShowIntersections(false);
            road.SetActive(true);
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

using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using GS = GameEngine.Scriptable;
using GO = GameEngine.GameObject;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace ElementPlacer {
    public class BuildingPlacer : TerrainAction {
        public BuildingLine curBuilding = null;
        public int curRail = -1;
        ElementManager manager;

        public enum PlacementMode {
            None,
            Point,
            DividingPoint
        }

        public PlacementMode placementMode = PlacementMode.Point;

        public BuildingPlacer(ElementManager manager) {
            this.manager = manager;
        }

        public void SetBuildingLine(BuildingLine building) {
            curBuilding = GO.IsNull(building) ? null : building;
        }

        void CreateBuilding() {
            curBuilding = Operations.BuildingLines.Create(manager);
        }

        public BuildingLine GetLine(bool createIfNull = true) {
            if (GO.IsNull(curBuilding) || curBuilding.IsDeleted()) curBuilding = null; //a deleted line stays valid until the end of the frame
            if (createIfNull && curBuilding == null) CreateBuilding();
            return curBuilding;
        }

        bool Guards(List<RaycastHit?> hits) {
            if (placementMode == PlacementMode.None) return false;
            if (!hits[0].HasValue && !hits[1].HasValue && !hits[2].HasValue) return false;
            ShowOnMap(hits);
            if (!Input.GetInputStart(KeyCommand.Select)) return false;
            return true;
        }

        RaycastHit GetHit(List<RaycastHit?> hits) {
            return hits[0].HasValue ? hits[0].Value : hits[1].HasValue ? hits[1].Value : hits[2].Value;
        }

        public override void Apply(List<RaycastHit?> hits) {
            if (!Guards(hits)) return;
            manager.worldChanged = true;
            var hit = GetHit(hits);
            var pointOnly = !Input.GetInputPressed(KeyCommand.Modifier2);
            Operations.BuildingLines.AddPoint(GetLine(), (Node3D)hit.collider, hit.point, pointOnly, placementMode == PlacementMode.DividingPoint);
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            var hit = GetHit(hits);
            (Vector3 pos, float size, Color color) = Actions.Helpers.GetAnchorInfo((Node3D)hit.collider, hit.point);
            ActionHandlerManager.ShowHandle(pos, size, color);
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { 1 << 10, 1 << 12, 1 << 3 };
        }
    }
}

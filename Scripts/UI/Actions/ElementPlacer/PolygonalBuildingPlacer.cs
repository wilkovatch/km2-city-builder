using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using GO = GameEngine.GameObject;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace ElementPlacer {
    //Adds the clicked points to a polygonal building (on the anchors, on existing points or on the terrain), at the end of
    //its outline, or in the edge clicked
    public class PolygonalBuildingPlacer : TerrainAction {
        public PolygonalBuilding curBuilding = null;
        ElementManager manager;

        public enum PlacementMode {
            None,
            Point
        }

        public PlacementMode placementMode = PlacementMode.Point;

        public PolygonalBuildingPlacer(ElementManager manager) {
            this.manager = manager;
        }

        public void SetBuilding(PolygonalBuilding building) {
            curBuilding = GO.IsNull(building) ? null : building;
        }

        public PolygonalBuilding GetBuilding(bool createIfNull = true) {
            if (GO.IsNull(curBuilding) || curBuilding.IsDeleted()) curBuilding = null; //a deleted building stays valid until the end of the frame
            if (createIfNull && curBuilding == null) curBuilding = Operations.PolygonalBuildings.Create(manager);
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
            var building = GetBuilding();
            var firstPoint = building.GetPointCount() == 0;
            //a click on an edge of the building inserts a point in it, any other one adds a point at the end
            if (Operations.PolygonalBuildings.InsertOnClickedSegment(building, (Node3D)hit.collider, hit.point) == null) {
                Operations.PolygonalBuildings.AddPoint(building, (Node3D)hit.collider, hit.point, !Input.GetInputPressed(KeyCommand.Modifier2));
            }
            //a raised point (on the ground hit), or a building started with the terrain hidden, would be put back on the ground
            if (PlacementHeight.TurnsOffProjection(firstPoint && building.GetPointCount() > 0, !hits[0].HasValue && !hits[1].HasValue) && building.IsProjectedToGround()) {
                History.UndoManager.Touch(building);
                building.instanceState.SetBool("projectToGround", false);
                manager.builder.RefreshActivePanelValues();
            }
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            var hit = GetHit(hits);
            (Vector3 pos, float size, Color color) = Actions.Helpers.GetAnchorInfo((Node3D)hit.collider, hit.point);
            ActionHandlerManager.ShowHandle(pos, size, color);
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { 1 << 10, 1 << 12, 1 << 3 };
        }

        public override int GroundHitIndex => 2;

        public override bool SupportsPlacementHeight => placementMode != PlacementMode.None;
    }
}

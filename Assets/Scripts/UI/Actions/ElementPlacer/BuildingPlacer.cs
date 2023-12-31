using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElementPlacer {
    public class BuildingPlacer : TerrainAction {
        public GameObject curBuilding = null;
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
            curBuilding = building != null ? building.gameObject : null;
        }

        void CreateBuilding() {
            curBuilding = new GameObject();
            curBuilding.name = "Building " + Actions.Helpers.GetLatestObjectNumber(manager.GetBuildingContainer(), "Building ");
            curBuilding.transform.parent = manager.GetBuildingContainer().transform;
            var line = curBuilding.AddComponent<BuildingLine>();
            line.Initialize(manager.GetTerrainPointContainer());
            if (manager != null) manager.buildings.Add(line);
        }

        public BuildingLine GetLine(bool createIfNull = true) {
            if (createIfNull && curBuilding == null) CreateBuilding();
            return curBuilding == null ? null : curBuilding.GetComponent<BuildingLine>();
        }

        bool Guards(List<RaycastHit?> hits) {
            if (placementMode == PlacementMode.None) return false;
            if (!hits[0].HasValue && !hits[1].HasValue && !hits[2].HasValue) return false;
            ShowOnMap(hits);
            if (!Input.GetMouseButtonDown(0)) return false;
            return true;
        }

        RaycastHit GetHit(List<RaycastHit?> hits) {
            return hits[0].HasValue ? hits[0].Value : hits[1].HasValue ? hits[1].Value : hits[2].Value;
        }

        public override void Apply(List<RaycastHit?> hits) {
            if (!Guards(hits)) return;
            manager.worldChanged = true;
            var hit = GetHit(hits);
            if (curBuilding == null) CreateBuilding();
            var line = curBuilding.GetComponent<BuildingLine>();
            var pointOnly = !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl);
            var newPoint = line.AddPoint(hit.collider.gameObject, hit.point, pointOnly);
            if (newPoint != null) newPoint.dividing = placementMode == PlacementMode.DividingPoint;
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            var hit = GetHit(hits);
            (Vector3 pos, float size, Color color) = Actions.Helpers.GetAnchorInfo(hit.collider.gameObject, hit.point);
            ActionHandlerManager.ShowHandle(pos, size, color);
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { 1 << 10, 1 << 12, 1 << 3 };
        }
    }
}

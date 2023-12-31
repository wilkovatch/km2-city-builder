using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElementPlacer {
    public class TerrainPlacer : TerrainAction {
        public GameObject curTerrain = null;

        public enum PlacementMode {
            None,
            Perimeter,
            Internal,
            BorderMesh
        }

        public PlacementMode placementMode = PlacementMode.Perimeter;
        public int curBorderMesh = -1;
        ElementManager manager;

        public TerrainPlacer(ElementManager manager) {
            this.manager = manager;
        }

        public void SetTerrainPatch(TerrainPatch patch) {
            curTerrain = patch != null ?  patch.gameObject : null;
        }

        void CreateTerrain() {
            curTerrain = new GameObject();
            curTerrain.name = "Terrain " + Actions.Helpers.GetLatestObjectNumber(manager.GetTerrainContainer(), "Terrain "); ;
            var patch = curTerrain.AddComponent<TerrainPatch>();
            patch.Initialize(manager.GetTerrainPointContainer(), manager.GetTerrainContainer());
            if (manager != null) manager.patches.Add(patch);
        }

        public TerrainPatch GetPatch(bool createIfNull = true) {
            if (createIfNull && curTerrain == null) CreateTerrain();
            return curTerrain == null ? null : curTerrain.GetComponent<TerrainPatch>();
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
            if (curTerrain == null) CreateTerrain();
            var patch = curTerrain.GetComponent<TerrainPatch>();
            switch (placementMode) {
                case PlacementMode.Perimeter:
                    var pointOnly = !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl);
                    patch.AddPerimeterPoint(hit.collider.gameObject, hit.point, pointOnly);
                    break;
                case PlacementMode.Internal:
                    patch.AddInternalPoint(hit.collider.gameObject, hit.point);
                    break;
                case PlacementMode.BorderMesh:
                    var point = hit.collider.gameObject.GetComponent<TerrainPoint>();
                    patch.AddPointToBorderMesh(curBorderMesh, point);
                    patch.HighlightBorderMesh(curBorderMesh);
                    break;
            }
            UnselectPointDelayed();
        }

        void UnselectPointDelayed() {
            manager.builder.DoDelayed(UnselectPoint); //otherwise the immediate deselect gets overridden by the UI select
        }

        void UnselectPoint() {
            var oldDeselect = manager.builder.gizmo.allowDeselect;
            manager.builder.gizmo.allowDeselect = true;
            manager.builder.gizmo.ClearTargets();
            manager.builder.gizmo.allowDeselect = oldDeselect;
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

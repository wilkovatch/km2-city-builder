using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using GS = GameEngine.Scriptable;
using GO = GameEngine.GameObject;

using RaycastHit = GameEngine.Raycast.RaycastHit;
namespace ElementPlacer {
    public class TerrainPlacer : TerrainAction {
        public TerrainPatch curTerrain = null;

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
            curTerrain = GO.IsNull(patch) ? null : patch;
        }

        void CreateTerrain() {
            var name = "Terrain " + Actions.Helpers.GetLatestObjectNumber(manager.GetTerrainContainer(), "Terrain "); ;
            curTerrain = GS.Create<TerrainPatch>(name);
            curTerrain.Initialize(manager.GetTerrainPointContainer(), manager.GetTerrainContainer());
            if (manager != null) manager.AddPatch(curTerrain);
        }

        public TerrainPatch GetPatch(bool createIfNull = true) {
            if (GO.IsNull(curTerrain)) curTerrain = null;
            if (createIfNull && curTerrain == null) CreateTerrain();
            return curTerrain;
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
            if (GO.IsNull(curTerrain)) CreateTerrain();
            var patch = curTerrain;
            switch (placementMode) {
                case PlacementMode.Perimeter:
                    var pointOnly = !Input.GetInputPressed(KeyCommand.Modifier2);
                    patch.AddPerimeterPoint((Node3D)hit.collider, hit.point, pointOnly);
                    break;
                case PlacementMode.Internal:
                    patch.AddInternalPoint((Node3D)hit.collider, hit.point);
                    break;
                case PlacementMode.BorderMesh:
                    var point = GS.FindRecursive<TerrainPoint>((Node3D)hit.collider);
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
            (Vector3 pos, float size, Color color) = Actions.Helpers.GetAnchorInfo((Node3D)hit.collider, hit.point);
            ActionHandlerManager.ShowHandle(pos, size, color);
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { 1 << 10, 1 << 12, 1 << 3 };
        }
    }
}

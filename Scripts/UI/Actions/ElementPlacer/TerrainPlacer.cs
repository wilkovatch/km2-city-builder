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
            curTerrain = GO.IsNull(patch) || patch.IsDeleted() ? null : patch;
        }

        void CreateTerrain() {
            curTerrain = Operations.TerrainPatches.Create(manager);
        }

        public TerrainPatch GetPatch(bool createIfNull = true) {
            //a deleted patch stays valid until the end of the frame (e.g. the empty patch cleared when the editor opens)
            if (GO.IsNull(curTerrain) || curTerrain.IsDeleted()) curTerrain = null;
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
            var patch = GetPatch();
            var firstPoint = patch.GetPerimeterPointCount() == 0;
            switch (placementMode) {
                case PlacementMode.Perimeter:
                    var pointOnly = !Input.GetInputPressed(KeyCommand.Modifier2);
                    Operations.TerrainPatches.AddPerimeterPoint(patch, (Node3D)hit.collider, hit.point, pointOnly);
                    break;
                case PlacementMode.Internal:
                    Operations.TerrainPatches.AddInternalPoint(patch, (Node3D)hit.collider, hit.point);
                    break;
                case PlacementMode.BorderMesh:
                    var point = GS.FindRecursive<TerrainPoint>((Node3D)hit.collider);
                    Operations.TerrainPatches.AddPointToBorderMesh(patch, curBorderMesh, point);
                    patch.HighlightBorderMesh(curBorderMesh);
                    break;
            }
            //a raised point (or one on the hidden terrain, on the ground hit) would be put back on the ground
            if (PlacementHeight.TurnsOffProjection(firstPoint) && placementMode != PlacementMode.BorderMesh && !hits[0].HasValue && !hits[1].HasValue && patch.IsProjectedToGround()) {
                History.UndoManager.Touch(patch);
                patch.state.SetBool("projectToGround", false);
                manager.builder.RefreshActivePanelValues();
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

        public override int GroundHitIndex => 2;

        public override bool SupportsPlacementHeight => placementMode == PlacementMode.Perimeter || placementMode == PlacementMode.Internal;
    }
}

using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace ElementPlacer {
    public class MeshPlacer : TerrainAction {
        ElementManager manager;
        public bool placeEnabled = true;
        public ObjectState settings = null;
        Vector3? curPoint = null;
        public string meshPath;
        public bool multiple = false;
        public float multipleRadius = 1.0f;
        public float multipleIntensity = 1.0f;
        public bool randomRotation = false;
        public bool deleteMode = false;
        float lastTimePlaced = 0.0f;
        float minTime = 1.0f / 10.0f;
        Vector2 lastMousePos = GameEngine.Vector2.zero;
        Color transpGreen = new Color(0, 1, 0, 0.4f);
        Color transpRed = new Color(1, 0, 0, 0.4f);

        public MeshPlacer(ElementManager manager) {
            this.manager = manager;
        }

        public void SetActive(bool active) {
            placeEnabled = active;
        }

        int GetLayerMask() {
            return Operations.Meshes.GetPlacerLayerMask(settings);
        }

        public override void Apply(List<RaycastHit?> hits) {
            ShowOnMap(hits);
            if (settings == null) return;
            //the placement options the type doesn't allow are off
            var type = Operations.Meshes.GetMeshType(settings.Str("type", null));
            if (!(type?.Settings.massPlacement ?? false)) multiple = false;
            if (!(type?.Settings.randomRotation ?? false)) randomRotation = false;
            if (multiple) {
                if (!Input.GetInputPressed(KeyCommand.Select)) lastMousePos = Vector2.Zero;
                var mousePos = manager.GetViewport().GetMousePosition();
                if (!Input.GetInputPressed(KeyCommand.Select) || GameEngine.Vector2.Distance(mousePos, lastMousePos) < 1.0f) return;
                if (curPoint.HasValue) {
                    if (deleteMode) {
                        Operations.Meshes.EraseInRadius(manager, curPoint.Value, multipleRadius, settings);
                        manager.worldChanged = true;
                    } else if (GameEngine.GetTime() - lastTimePlaced >= minTime) {
                        Operations.Meshes.Scatter(manager, meshPath, curPoint.Value, multipleRadius, (int)System.Math.Ceiling(multipleIntensity), settings, randomRotation);
                        lastTimePlaced = GameEngine.GetTime();
                        lastMousePos = manager.GetViewport().GetMousePosition();
                    }
                }
            } else {
                if (!Input.GetInputStart(KeyCommand.Select)) return;
                if (curPoint.HasValue) {
                    Operations.Meshes.Create(manager, meshPath, curPoint.Value, settings, randomRotation);
                }
            }
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            if (placeEnabled && meshPath != "" && meshPath != null && hits.Count > 0 && hits[0].HasValue) {
                var pos = hits[0].Value.point;
                if (multiple) {
                    ActionHandlerManager.ShowDiscHandle(hits[0].Value.point + Vector3.Up * 0.1f, multipleRadius * 0.2f, deleteMode ? transpRed : transpGreen);
                } else {
                    ActionHandlerManager.ShowHandle(hits[0].Value.point, 1.0f, GameEngine.Color.green);
                }
                curPoint = pos;
            } else {
                curPoint = null;
            }
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { GetLayerMask() };
        }

        public override int GroundHitIndex => 0;

        //single placement only, and not for the types put back on the surface after a move
        public override bool SupportsPlacementHeight => placeEnabled && settings != null && !multiple && !deleteMode &&
            !Operations.Meshes.SnapsToSurface(settings);
    }
}

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

        CityElements.Types.Runtime.MeshInstanceSettings GetMeshSettings() {
            var meshSettings = CityElements.Types.Parsers.TypeParser.GetMeshInstanceSettings();
            if (meshSettings == null || settings == null) return null;
            meshSettings.FillInitialVariables(meshSettings.variableContainer, settings);
            return meshSettings;
        }

        int GetLayer() {
            var meshSettings = GetMeshSettings();
            if (meshSettings == null) return -1;
            var layer = (int)meshSettings.rules.layer.GetValue(meshSettings.variableContainer);
            return layer;
        }

        int GetLayerMask() {
            var meshSettings = GetMeshSettings();
            if (meshSettings == null) return -1;
            var mask = (int)meshSettings.rules.placerLayerMask.GetValue(meshSettings.variableContainer);
            return mask;
        }

        public override void Apply(List<RaycastHit?> hits) {
            ShowOnMap(hits);
            if (settings == null) return;
            if (multiple) {
                if (!Input.GetInputPressed(KeyCommand.Select)) lastMousePos = Vector2.Zero;
                var mousePos = manager.GetViewport().GetMousePosition();
                if (!Input.GetInputPressed(KeyCommand.Select) || GameEngine.Vector2.Distance(mousePos, lastMousePos) < 1.0f) return;
                if (curPoint.HasValue) {
                    var cont = manager.GetMeshContainer();
                    if (deleteMode) {
                        var layer = GetLayer();
                        var delList = new List<MeshInstance>();
                        foreach (var mesh in manager.meshes) {
                            if (GameEngine.GameObject.GetLayer(mesh) == layer) {
                                // The root sits on the placement point (only its children are raised by the
                                // y offset), so it is compared as is.
                                // curPoint is in program coordinates, so the position must be read through GO.
                                var dist = GameEngine.Vector3.Distance(GameEngine.GameObject.GetPosition(mesh), curPoint.Value);
                                if (dist < multipleRadius) {
                                    delList.Add(mesh);
                                }
                            }
                        }
                        foreach (var mesh in delList) {
                            mesh.Delete();
                        }
                        manager.worldChanged = true;
                    } else if (GameEngine.GetTime() - lastTimePlaced >= minTime) {
                        var points = new List<Vector3>();
                        var rnd = RandomManager.rnd;
                        for (int i = 0; i < multipleIntensity; i++) {
                            var randomDir = new Vector3((float)rnd.NextDouble() - 0.5f, 0, (float)rnd.NextDouble() - 0.5f).Normalized();
                            var point = curPoint.Value + randomDir * multipleRadius * (float)rnd.NextDouble();
                            points.Add(GeometryHelper.ProjectPoint(point, GetLayerMasks()[0]));
                        }
                        foreach (var point in points) {
                            var mesh = MeshInstance.Create(meshPath, point, cont, settings);
                            if (randomRotation) GameEngine.GameObject.SetRotation(mesh, new Vector3(0, 360 * (float)rnd.NextDouble(), 0));
                            manager.AddMesh(mesh);
                        }
                        lastTimePlaced = GameEngine.GetTime();
                        lastMousePos = manager.GetViewport().GetMousePosition();
                    }
                }
            } else {
                if (!Input.GetInputStart(KeyCommand.Select)) return;
                if (curPoint.HasValue) {
                    var cont = manager.GetMeshContainer();
                    var mesh = MeshInstance.Create(meshPath, curPoint.Value, cont, settings);
                    if (randomRotation) GameEngine.GameObject.SetRotation(mesh, new Vector3(0, 360 * (float)RandomManager.rnd.NextDouble(), 0));
                    manager.AddMesh(mesh);
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
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        Vector3 lastMoustPos = Vector3.zero;
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

        bool GetAutoYOffset() {
            var meshSettings = GetMeshSettings();
            if (meshSettings == null) return false;
            var autoYOffset = meshSettings.rules.autoYOffset.GetValue(meshSettings.variableContainer);
            return autoYOffset;
        }

        public override void Apply(List<RaycastHit?> hits) {
            ShowOnMap(hits);
            if (settings == null) return;
            var autoYOffset = GetAutoYOffset();
            if (multiple) {
                if (!Input.GetMouseButton(0)) lastMoustPos = Vector3.zero;
                if (!Input.GetMouseButton(0) || Vector3.Distance(Input.mousePosition, lastMoustPos) < 1.0f) return;
                if (curPoint.HasValue) {
                    var cont = manager.GetMeshContainer();
                    if (deleteMode) {
                        var layer = GetLayer();
                        var delList = new List<MeshInstance>();
                        foreach (var mesh in manager.meshes) {
                            if (mesh.gameObject.layer == layer) {
                                var truePos = mesh.transform.position - Vector3.up * (autoYOffset ? mesh.GetYOffset() : 0);
                                var dist = Vector3.Distance(truePos, curPoint.Value);
                                if (dist < multipleRadius) {
                                    delList.Add(mesh);
                                }
                            }
                        }
                        foreach (var mesh in delList) {
                            mesh.Delete();
                        }
                        manager.worldChanged = true;
                    } else if (Time.time - lastTimePlaced >= minTime) {
                        var points = new List<Vector3>();
                        var rnd = RandomManager.rnd;
                        for (int i = 0; i < multipleIntensity; i++) {
                            var randomDir = new Vector3((float)rnd.NextDouble() - 0.5f, 0, (float)rnd.NextDouble() - 0.5f).normalized;
                            var point = curPoint.Value + randomDir * multipleRadius * (float)rnd.NextDouble();
                            points.Add(GeometryHelper.ProjectPoint(point, GetLayerMasks()[0]));
                        }
                        foreach (var point in points) {
                            var mesh = MeshInstance.Create(meshPath, point, cont, settings);
                            if (randomRotation) mesh.transform.rotation = Quaternion.Euler(0, 360 * (float)rnd.NextDouble(), 0);
                            manager.meshes.Add(mesh);
                        }
                        lastTimePlaced = Time.time;
                        lastMoustPos = Input.mousePosition;
                    }
                }
            } else {
                if (!Input.GetMouseButtonDown(0)) return;
                if (curPoint.HasValue) {
                    var cont = manager.GetMeshContainer();
                    var mesh = MeshInstance.Create(meshPath, curPoint.Value, cont, settings);
                    if (randomRotation) mesh.transform.rotation = Quaternion.Euler(0, 360 * (float)RandomManager.rnd.NextDouble(), 0);
                    manager.meshes.Add(mesh);
                }
            }
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            if (placeEnabled && meshPath != "" && meshPath != null && hits.Count > 0 && hits[0].HasValue) {
                var pos = hits[0].Value.point;
                if (multiple) {
                    ActionHandlerManager.ShowDiscHandle(hits[0].Value.point + Vector3.up * 0.1f, multipleRadius * 0.2f, deleteMode ? transpRed : transpGreen);
                } else {
                    ActionHandlerManager.ShowHandle(hits[0].Value.point, 1.0f, Color.green);
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

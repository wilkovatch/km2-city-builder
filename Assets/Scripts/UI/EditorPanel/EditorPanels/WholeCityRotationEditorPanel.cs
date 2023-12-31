using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class WholeCityRotationEditorPanel : EditorPanel {
        int curRotation = 0;
        int selectedRotation = 0;
        EditorPanelElements.Dropdown dropdown;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            var entries = new List<string>() {
                SM.Get("CITY_ROTATION_ANGLES_SELECT"),
                SM.Get("CITY_ROTATION_ANGLES_90_CCW"),
                SM.Get("CITY_ROTATION_ANGLES_90_CW"),
                SM.Get("CITY_ROTATION_ANGLES_180"),
            };
            dropdown = p0.AddDropdown(SM.Get("CITY_ROTATION_ANGLES"), entries, SetRotation, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CITY_ROTATION_ROTATE"), Rotate, 0.75f);
            p0.AddButton(SM.Get("CANCEL"), Cancel, 0.75f);
            p0.IncreaseRow();
        }

        void SetRotation(int i) {
            curRotation = i;
        }

        public override void SetActive(bool active) {
            base.SetActive(active);
            dropdown.SetValue(0);
            curRotation = 0;
        }

        void Cancel() {
            SetActive(false);
        }

        void Rotate() {
            if (curRotation == 0) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("CITY_ROTATION_ANGLES_MISSING"), SM.Get("OK"));
            } else {
                builder.StartCoroutine(RotateCityCoroutine());
            }
        }

        Quaternion GetRotation() {
            var y = 0.0f;
            switch (selectedRotation) {
                case 1:
                    y = -90;
                    break;
                case 2:
                    y = 90;
                    break;
                case 3:
                    y = 180;
                    break;
            }
            return Quaternion.Euler(new Vector3(0, y, 0));
        }

        Vector3 GetRotated(Vector3 v) {
            return GetRotation() * v;
        }

        Quaternion GetRotated(Quaternion q) {
            return q * GetRotation();
        }

        void MoveTransform(Transform transform, HashSet<Transform> doneTransforms) {
            if (doneTransforms.Contains(transform)) return;
            transform.position = GetRotated(transform.position);
            doneTransforms.Add(transform);
        }

        void MoveAndRotateTransform(Transform transform, HashSet<Transform> doneTransforms) {
            if (doneTransforms.Contains(transform)) return;
            transform.position = GetRotated(transform.position);
            transform.rotation = GetRotated(transform.rotation);
            doneTransforms.Add(transform);
        }

        IEnumerator RotateCityCoroutine() {
            var doneTransforms = new HashSet<Transform>();
            var manager = builder.helper.elementManager;
            selectedRotation = curRotation;
            SetActive(false);
            var progressBar = manager.builder.helper.curProgressBar;
            progressBar.SetActive(true);
            progressBar.SetProgress(0);
            progressBar.SetText(SM.Get("ROTATING_CITY"));
            for (int i = 0; i < 2; i++) { //once only does not make the panel close
                yield return new WaitForEndOfFrame();
            }

            for (int i = 0; i < manager.meshes.Count; i++) {
                var mesh = manager.meshes[i];
                MoveAndRotateTransform(mesh.transform, doneTransforms);
            }

            for (int i = 0; i < manager.roads.Count; i++) {
                var road = manager.roads[i];
                for (int j = 0; j < road.points.Count; j++) {
                    MoveTransform(road.points[j].transform, doneTransforms);
                }
            }

            for (int i = 0; i < manager.intersections.Count; i++) {
                var intersection = manager.intersections[i];
                MoveTransform(intersection.point.transform, doneTransforms);
            }

            for (int i = 0; i < manager.patches.Count; i++) {
                var patch = manager.patches[i];

                var patchPerimeterPoints = patch.GetPerimeterPointsComponents();
                for (int j = 0; j < patchPerimeterPoints.Count; j++) {
                    MoveTransform(patchPerimeterPoints[j].transform, doneTransforms);
                }

                var patchInternalPoints = patch.GetInternalPointsComponents();
                for (int j = 0; j < patchInternalPoints.Count; j++) {
                    MoveTransform(patchInternalPoints[j].transform, doneTransforms);
                }
            }

            for (int i = 0; i < manager.buildings.Count; i++) {
                var line = manager.buildings[i];

                var linePoints = line.GetPointsComponents();
                for (int j = 0; j < linePoints.Count; j++) {
                    MoveTransform(linePoints[j].transform, doneTransforms);
                }
            }

            var tRes = CityGroundHelper.heightmapResolution + 1;
            var terrain = manager.builder.helper.terrainObj.GetComponent<Terrain>();
            var heights = terrain.terrainData.GetHeights(0, 0, tRes, tRes);
            var heightsRotated = new float[tRes, tRes];
            for (int y = 0; y < tRes; y++) {
                for (int x = 0; x < tRes; x++) {
                    switch (selectedRotation) {
                        case 1: //-90
                            heightsRotated[x, y] = heights[tRes -1 - y, x];
                            break;
                        case 2: //90
                            heightsRotated[x, y] = heights[y, tRes - 1 - x];
                            break;
                        case 3: //180
                            heightsRotated[x, y] = heights[tRes - 1 - x, tRes - 1 - y];
                            break;
                    }
                }
            }
            manager.builder.helper.SetHeights(heightsRotated);

            builder.NotifyChange();
            progressBar.SetActive(false);
            builder.CreateAlert(SM.Get("SUCCESS"), SM.Get("CITY_ROTATED"), SM.Get("OK"));
        }

    }
}
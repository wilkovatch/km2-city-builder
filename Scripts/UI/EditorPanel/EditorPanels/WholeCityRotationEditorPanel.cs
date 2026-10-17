using System.Collections;
using System.Collections.Generic;
using System.IO;
using Godot;
using GameObject = Godot.Control;
using SM = StringManager;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

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
                RotateCityCoroutine(builder);
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
            return GameEngine.Quaternion.Euler(0, y, 0);
        }

        Vector3 GetRotated(Vector3 v) {
            return GetRotation() * v;
        }

        Quaternion GetRotated(Quaternion q) {
            //the city rotates around the world Y axis, so the rotation is applied after the object's own one
            //(q * rotation is wrong for meshes that are not only rotated around Y)
            return GetRotation() * q;
        }

        //positions and rotations go through GO to work in program space and world coordinates
        void MoveTransform(Node3D transform, HashSet<Node3D> doneTransforms) {
            if (doneTransforms.Contains(transform)) return;
            GO.SetPosition(transform, GetRotated(GO.GetPosition(transform)));
            doneTransforms.Add(transform);
        }

        void MoveAndRotateTransform(Node3D transform, HashSet<Node3D> doneTransforms) {
            if (doneTransforms.Contains(transform)) return;
            GO.SetPosition(transform, GetRotated(GO.GetPosition(transform)));
            GO.SetRotation(transform, GetRotated(GO.GetRotationQuaternion(transform)));
            doneTransforms.Add(transform);
        }

        //rotation: 1 = -90, 2 = 90, 3 = 180 degrees
        public static float[,] RotateHeights(float[,] heights, int rotation) {
            var tRes = heights.GetLength(0);
            var heightsRotated = new float[tRes, tRes];
            for (int y = 0; y < tRes; y++) {
                for (int x = 0; x < tRes; x++) {
                    switch (rotation) {
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
            return heightsRotated;
        }

        async void RotateCityCoroutine(Node caller) {
            var doneTransforms = new HashSet<Node3D>();
            var manager = builder.helper.elementManager;
            selectedRotation = curRotation;
            SetActive(false);
            var progressBar = manager.builder.helper.curProgressBar;
            progressBar.SetActive(true);
            progressBar.SetProgress(0);
            progressBar.SetText(SM.Get("ROTATING_CITY"));
            for (int i = 0; i < 2; i++) { //once only does not make the panel close
                await caller.ToSignal(caller.GetTree(), "process_frame");
            }
            History.UndoManager.BeginBusy(SM.Get("ROTATE_CITY"));
            History.UndoManager.TouchAll();

            for (int i = 0; i < manager.meshes.Count; i++) {
                var mesh = manager.meshes[i];
                MoveAndRotateTransform(mesh, doneTransforms);
            }

            for (int i = 0; i < manager.roads.Count; i++) {
                var road = manager.roads[i];
                for (int j = 0; j < road.points.Count; j++) {
                    MoveTransform(road.points[j], doneTransforms);
                }
            }

            for (int i = 0; i < manager.intersections.Count; i++) {
                var intersection = manager.intersections[i];
                MoveTransform(intersection.point, doneTransforms);
            }

            for (int i = 0; i < manager.patches.Count; i++) {
                var patch = manager.patches[i];

                var patchPerimeterPoints = patch.GetPerimeterPointsComponents();
                for (int j = 0; j < patchPerimeterPoints.Count; j++) {
                    MoveTransform(patchPerimeterPoints[j].GetObject(), doneTransforms);
                }

                var patchInternalPoints = patch.GetInternalPointsComponents();
                for (int j = 0; j < patchInternalPoints.Count; j++) {
                    MoveTransform(patchInternalPoints[j].GetObject(), doneTransforms);
                }
            }

            for (int i = 0; i < manager.buildings.Count; i++) {
                var line = manager.buildings[i];

                var linePoints = line.GetPointsComponents();
                for (int j = 0; j < linePoints.Count; j++) {
                    MoveTransform(linePoints[j].GetObject(), doneTransforms);
                }
            }

            var tRes = CityGroundHelper.heightmapResolution + 1;
            var heights = GameEngine.Terrain.GetHeights(manager.builder.helper.terrainObj, 0, 0, tRes, tRes);
            manager.builder.helper.SetHeights(RotateHeights(heights, selectedRotation));
            History.UndoManager.RecordHeightmapRotation(selectedRotation);

            builder.NotifyChange();
            History.UndoManager.EndBusy();
            progressBar.SetActive(false);
            builder.CreateAlert(SM.Get("SUCCESS"), SM.Get("CITY_ROTATED"), SM.Get("OK"));
        }

    }
}
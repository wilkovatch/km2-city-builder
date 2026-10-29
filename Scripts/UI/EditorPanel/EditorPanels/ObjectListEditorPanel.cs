using System.Collections;
using System.Collections.Generic;
using System.IO;
using Godot;
using UIObject = Godot.Control;
using GameObject = Godot.Node3D;
using SM = StringManager;

namespace EditorPanels {
    public class ObjectListEditorPanel : EditorPanel {
        List<GameObject> objects;
        EditorPanelElements.ScrollList lst;
        int curI = -1;

        public override void Initialize(UIObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            lst = p0.AddScrollList(SM.Get("OBJ_LST_TITLE"), GetObjectList(), Select, 1.5f, SM.Get("OBJ_LST_TOOLTIP"));
            p0.IncreaseRow(5.0f);
            p0.AddButton(SM.Get("OBJ_LST_SELECT"), SelectMenu, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("OBJ_LST_ZOOM_TO"), ZoomTo, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CANCEL"), Cancel, 1.5f);
        }

        void Cancel() {
            builder.DeselectObject();
            SetActive(false);
        }

        void Select(int i) {
            builder.SelectObject(objects[i], false, null);
            curI = i;
        }

        void SelectMenu() {
            if (curI >= 0) builder.SelectObject(objects[curI], true, null);
        }

        void ZoomTo() {
            if (curI >= 0) {
                var cameraObj = GameEngine.Camera.Get(builder);
                var component = GameEngine.Scriptable.Cast<CameraController>(cameraObj);
                var selObj = objects[curI];
                var meshInstance = FindMesh(selObj);
                if (meshInstance != null) {
                    var aabb = meshInstance.Mesh.GetAabb();
                    var center = meshInstance.GlobalTransform * aabb.GetCenter();
                    var radius = aabb.Size.Length();
                    component.ZoomIn(center, radius);
                }
            }
        }

        //the object itself or the first descendant with a mesh
        //(line renderers are MeshInstance3Ds too, but had no MeshFilter)
        static MeshInstance3D FindMesh(Node node) {
            if (node is MeshInstance3D mi && node is not GameEngine.LineRenderer && mi.Mesh != null) return mi;
            foreach (var child in node.GetChildren()) {
                var res = FindMesh(child);
                if (res != null) return res;
            }
            return null;
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            if (active) {
                builder.DeselectObject();
                lst.Deselect();
                lst.SetItems(GetObjectList());
                curI = -1;
            }
            base.SetActive(active);
        }

        List<string> GetObjectList() {
            var res = new List<string>();
            objects = builder.helper.elementManager.GetObjectList();
            foreach (var obj in objects) {
                res.Add(GameEngine.GameObject.GetName(obj));
            }
            return res;
        }
    }
}
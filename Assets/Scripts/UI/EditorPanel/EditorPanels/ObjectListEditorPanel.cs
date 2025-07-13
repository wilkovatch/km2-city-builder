using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class ObjectListEditorPanel : EditorPanel {
        List<GameObject> objects;
        EditorPanelElements.ScrollList lst;
        int curI = -1;

        public override void Initialize(GameObject canvas) {
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
                var cameraObj = Camera.main.gameObject;
                var component = cameraObj.GetComponent<CameraController>();
                var selObj = objects[curI];
                var meshFilter = selObj.GetComponentInChildren<MeshFilter>();
                if (meshFilter != null) {
                    var m = meshFilter.sharedMesh;
                    component.ZoomIn(selObj.transform.position + m.bounds.center, m.bounds.size.magnitude);
                }
            }
        }

        public override void SetActive(bool active) {
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
                res.Add(obj.name);
            }
            return res;
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class MeshListEditorPanel : EditorPanel {
        EditorPanelElements.ScrollList lst;
        (string, string)[] fileEntries = null;
        EditorPanelElements.InputField searchBar;
        bool blockSearch = false;
        List<string> paths = new List<string>();
        List<string> absolutePaths = new List<string>();
        Dictionary<string, int> indices = new Dictionary<string, int>();
        EditorPanelElements.InputField curMesh;
        ElementPlacer.MeshPlacer placer = null;
        System.Action<string> valueSetter = null;
        EditorPanel panel = null;
        GameObject panelObj = null;
        System.Action afterSetAction = null;
        string lastQuery = "";
        EditorPanelElements.Button selectBtn;
        string curVal;
        public Props.PropMeshEditorPanel propEditor;
        public bool disablePlacer = false;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            if (PreferencesManager.workingDirectory == "") {
                return;
            }
            var p0 = GetPage(0);
            searchBar = p0.AddInputField(SM.Get("MESH_SEL_SEARCH"), SM.Get("MESH_SEL_SEARCH_PH"), "", UnityEngine.UI.InputField.ContentType.Standard, Search, 1.5f);
            p0.IncreaseRow();
            lst = p0.AddScrollList(SM.Get("MESH_SEL_LST_TITLE"), new List<string>(), Select, 1.5f, SM.Get("MESH_SEL_LST_TOOLTIP"));
            p0.IncreaseRow(5.0f);
            curMesh = p0.AddInputField(SM.Get("MESH_SEL_CURRENT"), SM.Get("NONE"), "", UnityEngine.UI.InputField.ContentType.Standard, null, 1.5f);
            curMesh.SetInteractable(false);
            p0.IncreaseRow();
            selectBtn = p0.AddButton(SM.Get("MESH_SEL_PLACE_MESH"), SelectAction, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CANCEL"), Cancel, 0.75f);
            p0.AddButton(SM.Get("RESCAN"), RefreshList, 0.75f);

            propEditor = AddChildPanel<Props.PropMeshEditorPanel>(canvas);
        }

        void GoBack() {
            SetActive(false);
            if (panel != null) {
                panel.Hide(false);
                panel = null;
            } else if (panelObj != null) {
                panelObj.SetActive(true);
                panelObj = null;
            }
        }

        void Cancel() {
            GoBack();
        }

        void Select(int i) {
            var val = i != -1 ? paths[i] : "";
            curMesh.SetValue(val);
            curVal = val;
            if (placer != null) {
                placer.meshPath = val;
                placer.placeEnabled = false;
            }
            selectBtn.SetInteractable(true);
        }

        void SelectAction() {
            if ((panel != null || panelObj != null) && valueSetter != null) {
                SelectAndGoBack();
            } else {
                propEditor.selectedMesh = curVal;
                Hide(true);
                propEditor.SetActive(true);
            }
        }

        void SelectAndGoBack() {
            valueSetter.Invoke(curVal);
            GoBack();
            valueSetter = null;
            afterSetAction?.Invoke();
        }

        public void Open(EditorPanel panel, System.Action<string> valueSetter, System.Action afterSetAction) {
            SetActive(true);
            placer = null;
            this.panel = panel;
            panelObj = null;
            this.valueSetter = valueSetter;
            this.panel.Hide(true);
            this.afterSetAction = afterSetAction;
            selectBtn.SetTitle(SM.Get("SELECT"));
        }

        public void Open(GameObject panel, System.Action<string> valueSetter, System.Action afterSetAction) {
            SetActive(true);
            placer = null;
            panelObj = panel;
            this.panel = null;
            this.valueSetter = valueSetter;
            panelObj.SetActive(false);
            this.afterSetAction = afterSetAction;
            selectBtn.SetTitle(SM.Get("SELECT"));
        }

        void Search(string val) {
            if (!blockSearch) ReloadList(false, val);
        }

        void RefreshList() {
            lst.Deselect();
            PathHelper.GetFileList(true, lastQuery, MeshImporter.GetSupportedFormats(), ref paths, ref absolutePaths, ref indices, ref fileEntries);
            lst.SetItems(paths);
        }

        public void ResetEntries() {
            fileEntries = null;
        }

        void ReloadList(bool rescan, string query) {
            lastQuery = query;
            lst.Deselect();
            PathHelper.GetFileList(rescan, query, MeshImporter.GetSupportedFormats(), ref paths, ref absolutePaths, ref indices, ref fileEntries);
            lst.SetItems(paths);
        }

        public override void SetActive(bool active) {
            if (active) {
                blockSearch = true;
                searchBar.SetValue("");
                blockSearch = false;
                ReloadList(false, "");
                Select(-1);
                panel = null;
                panelObj = null;
                valueSetter = null;
                afterSetAction = null;
                if (placer == null) {
                    placer = builder.meshPlacer;
                }
                if (!disablePlacer) builder.terrainClick.modifier = placer;
                selectBtn.SetTitle(SM.Get("MESH_SEL_PLACE_MESH"));
                selectBtn.SetInteractable(false);
            } else {
                disablePlacer = false;
            }
            if (placer != null) placer.SetActive(active);
            base.SetActive(active);
        }
    }
}
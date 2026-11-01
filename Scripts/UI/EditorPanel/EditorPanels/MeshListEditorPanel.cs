using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GameObject = Godot.Control;
using SM = StringManager;
using GO = GameEngine.GameObject;

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
        EditorPanelElements.MeshPreview preview;
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
        public string placementType, placementGroup; //the mesh instance type (or group of types) to place the mesh as

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            if (PreferencesManager.workingDirectory == "") {
                return;
            }
            var p0 = GetPage(0);
            searchBar = p0.AddInputField(SM.Get("MESH_SEL_SEARCH"), SM.Get("MESH_SEL_SEARCH_PH"), "", GameEngine.InputContentType.Standard, Search, 1.5f);
            p0.IncreaseRow();
            lst = p0.AddScrollList(SM.Get("MESH_SEL_LST_TITLE"), new List<string>(), new List<string>(), Select, 1.5f, SM.Get("MESH_SEL_LST_TOOLTIP"));
            p0.IncreaseRow(5.0f);
            preview = p0.AddMeshPreview(SM.Get("MESH_SEL_PREVIEW"), 1.5f, 1.8f);
            lst.SetThumbnailProvider(preview.renderer.GetThumbnail);
            p0.IncreaseRow(5.4f);
            curMesh = p0.AddInputField(SM.Get("MESH_SEL_CURRENT"), SM.Get("NONE"), "", GameEngine.InputContentType.Standard, null, 1.5f);
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
                GO.SetEnabled(panelObj, true);
                panelObj = null;
            } else {
                builder.SetSelectObjectMode(); //opened for mesh placement: otherwise the mesh placer stays as the click modifier and selection stops working
            }
        }

        void Cancel() {
            GoBack();
        }

        void Select(int i) {
            var val = i != -1 ? paths[i] : "";
            curMesh.SetValue(val);
            curVal = val;
            preview.SetValue(i >= 0 ? absolutePaths[i] : null);
            if (placer != null) {
                placer.meshPath = val;
                placer.placeEnabled = false;
            }
            selectBtn.SetInteractable(i >= 0);
        }

        void SelectAction() {
            if ((panel != null || panelObj != null) && valueSetter != null) {
                SelectAndGoBack();
            } else {
                propEditor.selectedMesh = curVal;
                propEditor.placementType = placementType;
                propEditor.placementGroup = placementGroup;
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
            disablePlacer = true;
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
            disablePlacer = true;
            SetActive(true);
            placer = null;
            panelObj = panel;
            this.panel = null;
            this.valueSetter = valueSetter;
            GO.SetEnabled(panelObj, false);
            this.afterSetAction = afterSetAction;
            selectBtn.SetTitle(SM.Get("SELECT"));
        }

        public void SetSelection(string value) {
            if (string.IsNullOrEmpty(value)) {
                lst.Deselect();
                Select(-1);
                return;
            }
            var normalized = value.Replace('\\', '/');
            string absolute = null;
            try {
                absolute = Path.GetFullPath(PathHelper.ResolveAssetPath(value)).Replace('\\', '/');
            } catch (System.Exception) {
                //An invalid stored path still belongs in the current-value field, without a selected row.
            }
            for (int i = 0; i < paths.Count; i++) {
                if (string.Equals(paths[i].Replace('\\', '/'), normalized, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(absolutePaths[i].Replace('\\', '/'), absolute, System.StringComparison.OrdinalIgnoreCase)) {
                    lst.SetValue(i);
                    lst.Reveal(i);
                    //Opening the selector must not normalize a saved value when the user confirms it unchanged.
                    curVal = value;
                    curMesh.SetValue(value);
                    return;
                }
            }
            lst.Deselect();
            curVal = value;
            curMesh.SetValue(value);
            selectBtn.SetInteractable(false);
            preview.SetValue(PathHelper.ResolveAssetPath(value));
        }

        //opened from a mesh field to pick a value, rather than for mesh placement
        public bool IsSelector() {
            return panel != null || panelObj != null;
        }

        //the editor panel hidden while picking a mesh for one of its fields
        public EditorPanel GetOpener() {
            return panel;
        }

        void Search(string val) {
            if (!blockSearch) ReloadList(false, val);
        }

        void RefreshList() {
            preview.renderer.ClearCache();
            ReloadList(true, lastQuery);
        }

        public void ResetEntries() {
            fileEntries = null;
            preview?.renderer.ClearCache();
        }

        void ReloadList(bool rescan, string query) {
            lastQuery = query;
            Select(-1);
            lst.Deselect();
            PathHelper.GetFileList(rescan, query, MeshImporter.GetSupportedFormats(), ref paths, ref absolutePaths, ref indices, ref fileEntries);
            lst.SetItems(paths, absolutePaths);
        }

        public override void Destroy() {
            if (preview != null && Godot.GodotObject.IsInstanceValid(preview.renderer)) preview.renderer.Stop();
            preview = null;
            base.Destroy();
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            if (active) {
                //Selector activation must leave the placement tool's current mesh and settings alone.
                placer = disablePlacer ? null : builder.meshPlacer;
                blockSearch = true;
                searchBar.SetValue("");
                blockSearch = false;
                ReloadList(false, "");
                Select(-1);
                panel = null;
                panelObj = null;
                valueSetter = null;
                afterSetAction = null;
                if (!disablePlacer) builder.terrainClick.modifier = placer;
                selectBtn.SetTitle(SM.Get("MESH_SEL_PLACE_MESH"));
                selectBtn.SetInteractable(false);
            } else {
                disablePlacer = false;
                preview?.renderer.Stop();
            }
            if (placer != null) placer.SetActive(active);
            base.SetActive(active);
        }
    }
}

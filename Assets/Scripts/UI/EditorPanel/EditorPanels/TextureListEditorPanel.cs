using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class TextureListEditorPanel : EditorPanel {
        EditorPanelElements.ScrollList lst;
        int curI = -1;
        (string, string)[] fileEntries = null;
        EditorPanelElements.InputField searchBar;
        bool blockSearch = false;
        List<string> paths = new List<string>();
        List<string> absolutePaths = new List<string>();
        Dictionary<string, int> indices = new Dictionary<string, int>();
        GameObject panel = null;
        System.Action afterSetAction = null;
        System.Action<string> valueSetter = null;
        EditorPanelElements.Image image;
        string lastQuery = "";

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            searchBar = p0.AddInputField(SM.Get("TEX_SEL_SEARCH"), SM.Get("TEX_SEL_SEARCH_PH"), "", UnityEngine.UI.InputField.ContentType.Standard, Search, 1.5f);
            p0.IncreaseRow();
            lst = p0.AddScrollList(SM.Get("TEX_SEL_LST_TITLE"), new List<string>(), new List<string>(), Select, 1.5f, SM.Get("TEX_SEL_LST_TOOLTIP"));
            p0.IncreaseRow(5.0f);
            image = p0.AddImage(SM.Get("TEX_SEL_PREVIEW"), null, 1.5f, null, null, 1.8f);
            p0.IncreaseRow(5.4f);
            p0.AddButton(SM.Get("SELECT"), SelectMenu, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CANCEL"), Cancel, 0.75f);
            p0.AddButton(SM.Get("RESCAN"), RefreshList, 0.75f);
        }

        void Cancel() {
            SetActive(false);
            panel.SetActive(true);
            panel = null;
            valueSetter = null;
        }

        void Select(int i) {
            curI = i;
            image.SetValue(absolutePaths[i]);
        }

        public void SetSelection(string val) {
            if (val != null && indices.ContainsKey(val)) {
                lst.SetValue(indices[val]);
            } else {
                lst.Deselect();
                image.SetValue(null);
            }
        }

        void Search(string val) {
            if (!blockSearch) ReloadList(false, val);
        }

        public void Open(GameObject panel, System.Action<string> valueSetter, System.Action afterSetAction) {
            SetActive(true);
            this.panel = panel;
            this.valueSetter = valueSetter;
            this.panel.SetActive(false);
            this.afterSetAction = afterSetAction;
        }

        void SelectMenu() {
            if (curI >= 0 && panel != null && valueSetter != null) {
                valueSetter.Invoke(paths[curI]);
                SetActive(false);
                panel.SetActive(true);
                panel = null;
                valueSetter = null;
                afterSetAction?.Invoke();
            }
        }

        void RefreshList() {
            lst.Deselect();
            PathHelper.GetFileList(true, lastQuery, TextureImporter.GetSupportedFormats(), ref paths, ref absolutePaths, ref indices, ref fileEntries);
            lst.SetItems(paths, absolutePaths);
        }

        void ReloadList(bool rescan, string query) {
            lastQuery = query;
            lst.Deselect();
            PathHelper.GetFileList(rescan, query, TextureImporter.GetSupportedFormats(), ref paths, ref absolutePaths, ref indices, ref fileEntries);
            lst.SetItems(paths, absolutePaths);
            curI = -1;
        }

        public override void SetActive(bool active) {
            if (active) {
                blockSearch = true;
                searchBar.SetValue("");
                blockSearch = false;
                ReloadList(false, "");
            }
            base.SetActive(active);
        }
    }
}
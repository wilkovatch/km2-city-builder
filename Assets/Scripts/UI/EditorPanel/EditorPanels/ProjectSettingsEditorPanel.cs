using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SM = StringManager;

namespace EditorPanels {
    public class ProjectSettingsEditorPanel : EditorPanel {
        EditorPanelElements.ScrollList globalPaths, localPaths;
        EditorPanelElements.TextureField overlayField;
        EditorPanelElements.InputField propsCullingDistance;
        EditorPanelElements.Dropdown renderingModeDropdown;
        EditorPanelElements.Checkbox shadowsCheckbox;
        int curGlobalPathI = -1;
        int curLocalPathI = -1;

        public override void Initialize(GameObject canvas) {
            pageButtonNames.AddRange(new string[] { SM.Get("PREF_TAB_PATHS"), SM.Get("PREF_TAB_GFX") });
            Initialize(canvas, 2);
            var p0 = GetPage(0);
            var p1 = GetPage(1);
            var w = 1.5f;

            //Main
            globalPaths = p0.AddScrollList(SM.Get("PREF_GLOBAL_PATHS"), new List<string>(), x => curGlobalPathI = x, w, SM.Get("PREF_GLOBAL_PATHS_TOOLTIP"));
            p0.IncreaseRow(5.0f);
            p0.AddButton(SM.Get("PREF_GLOBAL_PATHS_DELETE"), delegate {
                DeletePath(ref curGlobalPathI, delegate { return SettingsManager.GetList<string>("extraFolders", null); }, x => SettingsManager.SetList("extraFolders", x), globalPaths);
            }, w * 0.5f);
            p0.AddButton(SM.Get("PREF_GLOBAL_PATHS_ADD"), delegate {
                GetPath(x => { AddPath(x, ref curGlobalPathI, delegate { return SettingsManager.GetList<string>("extraFolders", null); }, x => SettingsManager.SetList("extraFolders", x), globalPaths); });
            }, w * 0.5f);
            p0.IncreaseRow();
            localPaths = p0.AddScrollList(SM.Get("PREF_LOCAL_PATHS"), new List<string>(), x => curLocalPathI = x, w, SM.Get("PREF_GLOBAL_PATHS_TOOLTIP"));
            p0.IncreaseRow(5.0f);
            p0.AddButton(SM.Get("PREF_LOCAL_PATHS_DELETE"), delegate {
                DeletePath(ref curLocalPathI, delegate { return PreferencesManager.GetList<string>("extraFolders", null); }, x => PreferencesManager.SetList("extraFolders", x), localPaths);
            }, w * 0.5f);
            p0.AddButton(SM.Get("PREF_LOCAL_PATHS_ADD"), delegate {
                GetPath(x => { AddPath(x, ref curLocalPathI, delegate { return PreferencesManager.GetList<string>("extraFolders", null); }, x => PreferencesManager.SetList("extraFolders", x), localPaths); });
            }, w * 0.5f);
            p0.IncreaseRow();
            overlayField = p0.AddTextureField(builder, SM.Get("GROUND_OVERLAY"), SM.Get("TEXTURE_PH"), "", x => {
                PreferencesManager.Set("overlayTex", x);
                builder.helper.LoadOverlayTexture(x);
            }, delegate {
                return PreferencesManager.Get("overlayTex", "");
            }, x => PreferencesManager.Set("overlayTex", x), w, SM.Get("OVERLAY_TOOLTIP"));
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CLOSE"), delegate { SetActive(false); }, w);

            //Graphics
            renderingModeDropdown = p1.AddDropdown(SM.Get("PREF_GFX_RENDERING_MODE"), new List<string> { SM.Get("PREF_GFX_RENDERING_MODE_NORMAL"), SM.Get("PREF_GFX_RENDERING_MODE_VERTEX_LIT") }, builder.helper.menuBar.SetCameraRenderingMode, w);
            p1.IncreaseRow();
            shadowsCheckbox = p1.AddCheckbox(SM.Get("PREF_GFX_SHADOWS"), true, builder.helper.menuBar.SetShadows, w);
            p1.IncreaseRow();
            propsCullingDistance = p1.AddInputField(SM.Get("PREF_GFX_PROP_CULL_DIST"), SM.Get("NUMBER_PH"), "" + PreferencesManager.Get("propsCullingDistance", 300.0f), InputField.ContentType.DecimalNumber, builder.helper.menuBar.SetPropsCullingDistance, w);
            p1.IncreaseRow();
            p1.AddButton(SM.Get("CLOSE"), delegate { SetActive(false); }, w);
        }

        void DeletePath(ref int curPathI, System.Func<List<string>> getter, System.Action<List<string>> setter, EditorPanelElements.ScrollList paths) {
            if (curPathI == -1) return;
            var lst = getter.Invoke();
            if (lst != null) {
                lst.RemoveAt(curPathI);
                setter.Invoke(lst);
            }
            ReloadPathList(ref curPathI, getter, paths, true);
        }

        void AddPath(string value, ref int curPathI, System.Func<List<string>> getter, System.Action<List<string>> setter, EditorPanelElements.ScrollList paths) {
            var lst = getter.Invoke();
            if (lst == null) lst = new List<string>();
            lst.Add(value);
            setter.Invoke(lst);
            ReloadPathList(ref curPathI, getter, paths, true);
        }

        protected void GetPath(System.Action<string> saveAction) {
            builder.CreateInput(SM.Get("PREF_PATH_INPUT_TITLE"), SM.Get("PREF_PATH_INPUT_PH"), SM.Get("ADD"), SM.Get("CANCEL"), str => { saveAction.Invoke(str); });
        }

        void ReloadPathList(ref int curPathI, System.Func<List<string>> getter, EditorPanelElements.ScrollList paths, bool deselect) {
            paths.Deselect();
            var lst = getter.Invoke();
            if (lst != null) {
                var items = new List<string>();
                for (int i = 0; i < lst.Count; i++) {
                    items.Add(lst[i]);
                }
                paths.SetItems(items);
            } else {
                paths.SetItems(new List<string>());
            }
            if (deselect) curPathI = -1;
        }

        public override void SetActive(bool active) {
            if (active) {
                ReloadPathList(ref curGlobalPathI, delegate { return SettingsManager.GetList<string>("extraFolders", null); }, globalPaths, true);
                ReloadPathList(ref curLocalPathI, delegate { return PreferencesManager.GetList<string>("extraFolders", null); }, localPaths, true);
                overlayField.SetValue(PreferencesManager.Get("overlayTex", ""));
                renderingModeDropdown.SetValue(PreferencesManager.Get("renderingMode", 0));
                shadowsCheckbox.SetValue(PreferencesManager.Get("shadowsEnabled", true));
            }
            base.SetActive(active);
        }
    }
}

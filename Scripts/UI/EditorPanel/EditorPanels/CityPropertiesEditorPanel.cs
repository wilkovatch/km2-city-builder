using GameObject = Godot.Control;
using PC = Operations.PropertyContainers;
using SM = StringManager;

namespace EditorPanels {
    public class CityPropertiesEditorPanel : EditorPanel {
        ContainerContentsPanel contentsPanel;
        int shownPage = -1;

        protected override void OnPageChanged() {
            var page = ActivePage();
            if (page >= 0 && shownPage >= 0 && page != shownPage && PreferencesManager.workingDirectory != "") {
                var tabs = CityElements.Types.Parsers.TypeParser.GetCitySettings()["city"].typeData.uiInfo.tabs;
                if (shownPage < tabs.Length) PC.LeaveTab(tabs[shownPage].label);
            }
            shownPage = page;
            base.OnPageChanged();
        }

        public void ShowTab(string key) {
            var tabs = CityElements.Types.Parsers.TypeParser.GetCitySettings()["city"].typeData.uiInfo.tabs;
            var index = System.Array.FindIndex(tabs, t => t.label == key);
            if (index >= 0) ShowPage(index);
        }

        public bool ShowsTab(string key) {
            var page = GetShownPage();
            if (page < 0 || PreferencesManager.workingDirectory == "") return false;
            var tabs = CityElements.Types.Parsers.TypeParser.GetCitySettings()["city"].typeData.uiInfo.tabs;
            return page < tabs.Length && tabs[page].label == key;
        }

        protected override void ShowParameterPage(string parameter) {
            var tabs = CityElements.Types.Parsers.TypeParser.GetCitySettings()["city"].typeData.uiInfo.tabs;
            for (int i = 0; i < tabs.Length; i++) {
                if (System.Array.Exists(tabs[i].elements, e => e.name == parameter)) { ShowPage(i); return; }
            }
        }

        public EditorPanel OpenContents(int group) {
            if (contentsPanel == null) return null;
            contentsPanel.containerId = PC.EnsureId(GetCurCity().state);
            contentsPanel.groupIndex = group;
            Hide(true);
            contentsPanel.SetActive(true);
            return contentsPanel;
        }

        public override void Initialize(GameObject canvas) {
            InitializeWithCustomParameters<CityElements.Types.Runtime.CitySettings, CityElements.Types.CitySettings.CitySettings>(canvas, GetCurCity, null,
                "city", CityElements.Types.Parsers.TypeParser.GetCitySettings, (elem, page, ps, ts, type) => {
                    if (!elem.name.StartsWith("content:")) return false;
                    var groups = PC.ContentGroups(type.typeData.settings.elementContainer);
                    var index = System.Array.FindIndex(groups, g => g.label == elem.name.Substring("content:".Length));
                    if (index >= 0) {
                        page.EnsureNewRow();
                        page.AddButton(SM.Get(groups[index].buttonLabel ?? groups[index].label), () => OpenContents(index), elem.width);
                        page.IncreaseRow();
                    }
                    return true;
                }, false, 1.5f, false);
            contentsPanel = AddChildPanel<ContainerContentsPanel>(canvas);
            var p0 = GetPage(0);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CLOSE"), Terminate, 1.5f);
        }

        public override object GetUndoTarget() { return GetCurCity(); }

        CityProperties GetCurCity() { return builder.helper.elementManager.GetDummy<CityProperties>(); }
    }
}

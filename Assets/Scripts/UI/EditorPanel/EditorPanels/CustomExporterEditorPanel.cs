using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class CustomExporterEditorPanel : EditorPanel {

        public FileBrowserHelper.CustomExporter exporter = null;
        public string name;
        List<EditorPanelElements.Checkbox> checkboxes = new List<EditorPanelElements.Checkbox>();
        List<FileBrowserHelper.CustomExporterFlags> flags = new List<FileBrowserHelper.CustomExporterFlags>();

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            checkboxes.Clear();
            flags.Clear();
            var p0 = GetPage(0);
            if (exporter != null) {
                foreach (var flag in exporter.settings.flags) {
                    var cb = p0.AddCheckbox(SM.Get(flag.label), flag.defaultValue, x => { EnableOthers(flag.enables, x); EnableOthers(flag.disables, !x); }, 1.5f, SM.Get(flag.tooltip));
                    checkboxes.Add(cb);
                    flags.Add(flag);
                    p0.IncreaseRow();
                }
            }
            p0.AddButton(SM.Get("CANCEL"), Terminate, 0.75f);
            p0.AddButton(SM.Get("EXPORT"), Export, 0.75f);
        }

        void EnableOthers(string[] others, bool enable) {
            if (others == null) return;
            var list = new List<string>(others);
            for (int i = 0; i < checkboxes.Count; i++) {
                var checkbox = checkboxes[i];
                var flag = flags[i];
                if (list.Contains(flag.id)) {
                    checkbox.SetInteractable(enable);
                }
            }
        }

        public override void SetActive(bool active) {
            if (active) Initialize(lastCanvas);
            base.SetActive(active);
        }

        void Export() {
            var flags = new List<bool>();
            foreach (var cb in checkboxes) {
                flags.Add(cb.GetValue());
            }
            Terminate();
            CityExporter.SaveCityWithExporter(exporter, flags, builder.helper.elementManager, name);
        }
    }
}
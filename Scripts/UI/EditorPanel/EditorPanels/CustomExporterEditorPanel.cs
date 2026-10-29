using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
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
                //the flags with a format are set by the extension of the chosen file, not by the user
                var forced = CityExporter.GetFormatFlags(exporter, PathHelper.GetExtension(name ?? ""));
                foreach (var flag in exporter.settings.flags) {
                    var isForced = flag.id != null && forced.ContainsKey(flag.id);
                    var value = isForced ? forced[flag.id] : flag.defaultValue;
                    var cb = p0.AddCheckbox(SM.Get(flag.label), value, x => { EnableOthers(flag.enables, x); EnableOthers(flag.disables, !x); }, 1.5f, SM.Get(flag.tooltip));
                    checkboxes.Add(cb);
                    flags.Add(flag);
                    p0.IncreaseRow();
                }
                for (int i = 0; i < flags.Count; i++) {
                    var flag = flags[i];
                    if (flag.id == null || !forced.TryGetValue(flag.id, out var value)) continue;
                    EnableOthers(flag.enables, value);
                    EnableOthers(flag.disables, !value);
                }
                for (int i = 0; i < flags.Count; i++) {
                    if (flags[i].id != null && forced.ContainsKey(flags[i].id)) checkboxes[i].SetInteractable(false);
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
            if (!PrepareActivation(active)) return;
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
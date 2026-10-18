using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;

namespace EditorPanels {
    //The inputs of a Python script, from the parameters of its script.json (laid out by its ui, like the panels of the
    //elements), run with the Run button. The values are kept in the preferences of the city for the next run.
    public class ScriptInputEditorPanel : EditorPanel {
        ScriptCatalog.ScriptInfo script = null;
        List<int> selection = new List<int>();
        ObjectState values = new ObjectState();

        public void Open(ScriptCatalog.ScriptInfo script, List<int> selection) {
            this.script = script;
            this.selection = selection;
            values = ScriptCatalog.LoadValues(script);
            Initialize(lastCanvas);
            SetActive(true);
        }

        public override void Initialize(GameObject canvas) {
            if (script == null) {
                Initialize(canvas, 1);
                return;
            }
            getObject = () => values;
            objectInstanceParameters.Clear();
            var ui = script.ui;
            foreach (var tab in ui.tabs) pageButtonNames.Add(GetString(tab.label) ?? "");
            var rows = new List<int>();
            var nTabs = ui.tabs.Length;
            if (nTabs < 4) {
                rows.Add(nTabs);
            } else {
                rows.Add(nTabs - nTabs / 2);
                rows.Add(nTabs / 2);
            }
            var w = ui.menuWidth;
            Initialize(canvas, rows, w);
            for (int i = 0; i < nTabs; i++) {
                var p = GetPage(i);
                p.AddLabel(script.label, w, script.description, null, 0.6f);
                p.IncreaseRow(0.6f);
                if (script.description != null) {
                    p.AddLabel(script.description, w, null, null, 0.8f);
                    p.IncreaseRow(0.8f);
                }
                var curW = 0.0f;
                foreach (var elem in ui.tabs[i].elements ?? new CityElements.Types.TabElement[0]) {
                    AddParameters(p, ref curW, w, script.parameters, elem, getObject, true);
                }
                p.IncreaseRow();
                p.AddButton(SM.Get("CANCEL"), Close, w * 0.5f);
                p.AddButton(SM.Get("SCRIPT_RUN"), Run, w * 0.5f);
            }
        }

        protected override string GetString(string key) {
            return script != null ? script.GetString(key) : base.GetString(key);
        }

        protected override bool AllowsScriptParameterTypes() {
            return true;
        }

        void Close() {
            if (script != null) ScriptCatalog.SaveValues(script, values);
            Terminate();
        }

        void Run() {
            var toRun = script;
            var inputs = ScriptCatalog.ToInputs(values);
            var runSelection = selection;
            Close();
            //after the undo step of the button
            builder.DoDelayed(() => { _ = Api.ScriptRunner.Run(builder, toRun, inputs, runSelection); });
        }
    }
}

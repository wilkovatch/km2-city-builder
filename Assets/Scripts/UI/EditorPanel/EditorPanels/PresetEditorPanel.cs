using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class PresetEditorPanel : EditorPanel {
        public PresetEditorPanel() {
            AddComplexElement(new PresetSelector(this));
        }

        public override void Initialize(GameObject canvas) {
            var w = 1.5f;
            Initialize(canvas, 1, w);
            var p = GetPage(0);
            p.AddButton(SM.Get("EDIT_ROAD_PRESETS"), builder.EditRoadPresets, w);
            p.IncreaseRow();
            p.AddButton(SM.Get("EDIT_INTERSECTION_PRESETS"), builder.EditIntersectionPresets, w);
            p.IncreaseRow();
            p.AddButton(SM.Get("EDIT_BUILDING_PRESETS"), builder.EditBuildingLinePresets, w);
            p.IncreaseRow();
            p.AddButton(SM.Get("CLOSE"), Terminate, w);
        }

        public override void SetActive(bool active) {
            var pS = GetComplexElement<PresetSelector>();
            if (active && !ActiveSelf()) {
                pS.ReloadValues();
                foreach (var key in pS.dropdowns.Keys) {
                    for (int i = 0; i < pS.dropdowns[key].Count; i++) {
                        pS.LoadPreset(-1, key, null, i);
                    }
                }
            }
            base.SetActive(active);
        }
    }
}
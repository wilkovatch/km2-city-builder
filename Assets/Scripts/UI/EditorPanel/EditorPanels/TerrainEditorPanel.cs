using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class TerrainEditorPanel : EditorPanel {
        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            var entries = new List<string> { SM.Get("TE_M_NONE"), SM.Get("TE_M_RAISE"), SM.Get("TE_M_LOWER"), SM.Get("TE_M_LEVEL") };
            p0.AddDropdown(SM.Get("TE_MODIFIER"), entries, SetTerrainModifier, 1.5f);
            p0.IncreaseRow();
            p0.AddSlider(SM.Get("TE_INTENSITY"), 0, 10, SetTerrainIntensity, 1.5f);
            p0.IncreaseRow();
            p0.AddSlider(SM.Get("TE_RANGE"), 0, 30, SetTerrainRange, 1.5f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("CLOSE"), Terminate, 1.5f);
        }

        public override void SetActive(bool active) {
            if (!active && ActiveSelf()) builder.UnsetModifier();
            base.SetActive(active);
        }

        int lastTerrainModifier = 0;
        void SetTerrainModifier(int value) {
            lastTerrainModifier = value;
            ReloadTerrainModifier();
        }

        float terrainRange;
        void SetTerrainRange(float value) {
            terrainRange = value;
            ReloadTerrainModifier();
        }

        float terrainIntensity;
        void SetTerrainIntensity(float value) {
            terrainIntensity = value;
            ReloadTerrainModifier();
        }

        public void ReloadTerrainModifier() {
            switch (lastTerrainModifier) {
                case 1:
                    builder.terrainClick.modifier = new TerrainModifier.RaiseFlat(builder.terrainClick.helper, terrainRange, terrainIntensity);
                    break;
                case 2:
                    builder.terrainClick.modifier = new TerrainModifier.RaiseFlat(builder.terrainClick.helper, terrainRange, -terrainIntensity);
                    break;
                case 3:
                    builder.terrainClick.modifier = new TerrainModifier.Level(builder.terrainClick.helper, terrainRange, terrainIntensity * terrainIntensity);
                    break;
                default:
                    builder.UnsetModifier();
                    break;
            }
        }
    }
}
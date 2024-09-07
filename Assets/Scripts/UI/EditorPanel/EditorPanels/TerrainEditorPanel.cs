using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class TerrainEditorPanel : EditorPanel {
        EditorPanelElements.SliderWithInputField heightSlider;
        EditorPanelElements.Checkbox heightCheckbox;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            var entries = new List<string> { SM.Get("TE_M_NONE"), SM.Get("TE_M_RAISE"), SM.Get("TE_M_LOWER"), SM.Get("TE_M_LEVEL") };
            p0.AddDropdown(SM.Get("TE_MODIFIER"), entries, SetTerrainModifier, 1.5f);
            p0.IncreaseRow();
            p0.AddSliderWithInputField(SM.Get("TE_INTENSITY"), 0, 10, SetTerrainIntensity, 1.5f);
            p0.IncreaseRow();
            p0.AddSliderWithInputField(SM.Get("TE_RANGE"), 0, 30, SetTerrainRange, 1.5f);
            p0.IncreaseRow();
            heightCheckbox = p0.AddCheckbox(SM.Get("TE_LEVEL_TO_HEIGHT"), false, SetTerrainHeightActive, 1.5f);
            heightCheckbox.SetInteractable(false);
            p0.IncreaseRow();
            heightSlider = p0.AddSliderWithInputField(SM.Get("TE_HEIGHT"), -50, 50, SetTerrainHeight, 1.5f);
            heightSlider.SetInteractable(false);
            p0.IncreaseRow();
            p0.AddCheckbox(SM.Get("TE_CONTINUOUS"), true, SetContinuous, 1.5f, SM.Get("TE_CONTINUOUS_TOOLTIP"));
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
            heightSlider.SetInteractable(value == 3 && terrainHeightActive);
            heightCheckbox.SetInteractable(value == 3);
            ReloadTerrainModifier();
        }

        float terrainRange;
        void SetTerrainRange(float value) {
            if (value < 0) value = 0; //else it breaks
            terrainRange = value;
            ReloadTerrainModifier();
        }

        float terrainIntensity;
        void SetTerrainIntensity(float value) {
            terrainIntensity = value;
            ReloadTerrainModifier();
        }

        float terrainHeight;
        void SetTerrainHeight(float value) {
            terrainHeight = value;
            ReloadTerrainModifier();
        }

        bool terrainHeightActive;
        void SetTerrainHeightActive(bool value) {
            terrainHeightActive = value;
            heightSlider.SetInteractable(lastTerrainModifier == 3 && terrainHeightActive);
            ReloadTerrainModifier();
        }

        bool singleStep = false;
        void SetContinuous(bool value) {
            singleStep = !value;
            ReloadTerrainModifier();
        }

        public void ReloadTerrainModifier() {
            switch (lastTerrainModifier) {
                case 1:
                    builder.terrainClick.modifier = new TerrainModifier.RaiseFlat(builder.terrainClick.helper, terrainRange, terrainIntensity, singleStep);
                    break;
                case 2:
                    builder.terrainClick.modifier = new TerrainModifier.RaiseFlat(builder.terrainClick.helper, terrainRange, -terrainIntensity, singleStep);
                    break;
                case 3:
                    builder.terrainClick.modifier = new TerrainModifier.Level(builder.terrainClick.helper, terrainRange, terrainIntensity * terrainIntensity, singleStep, terrainHeightActive, terrainHeight);
                    break;
                default:
                    builder.UnsetModifier();
                    break;
            }
        }
    }
}
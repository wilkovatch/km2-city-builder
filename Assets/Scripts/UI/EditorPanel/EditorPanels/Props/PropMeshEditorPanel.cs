using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels.Props {
    public class PropMeshEditorPanel : EditorPanel {
        ElementPlacer.MeshPlacer placer = null;
        public string selectedMesh = null;
        EditorPanelElements.Slider intSlider, rdSlider;
        EditorPanelElements.Checkbox delCheckbox;
        ObjectState tmpState = new ObjectState();

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            var settings = CityElements.Types.Parsers.TypeParser.GetMeshInstanceSettings(needsReloading);
            if (settings == null) return;
            var width = 1.5f;

            //placement settings
            p0.AddCheckbox(SM.Get("PROP_PLACER_MULTIPLE"), false, SetMultiple, width);
            p0.IncreaseRow();
            intSlider = p0.AddSlider(SM.Get("PROP_PLACER_INTENSITY"), 1, 10, SetTerrainIntensity, width);
            p0.IncreaseRow();
            rdSlider = p0.AddSlider(SM.Get("PROP_PLACER_RADIUS"), 1, 30, SetTerrainRadius, width);
            p0.IncreaseRow();
            delCheckbox = p0.AddCheckbox(SM.Get("PROP_PLACER_DELETE"), false, SetDeleteMode, width);
            p0.IncreaseRow();
            p0.AddCheckbox(SM.Get("PROP_PLACER_RAND_ROT"), false, SetRandomRotation, width);
            p0.IncreaseRow();

            //properties
            var curW = 0.0f;
            for (int i = 0; i < settings.typeData.uiInfo.Length; i++) {
                var uiInfo = settings.typeData.uiInfo[i];
                var parameters = settings.typeData.parametersInfo.parameters;
                AddParameters(p0, ref curW, width, parameters, uiInfo, GetCurElem, false);
                if (i == settings.typeData.uiInfo.Length - 1) p0.IncreaseRow();
            }

            p0.AddButton(SM.Get("BACK"), GoUp, 0.75f);
            p0.AddButton(SM.Get("CLOSE"), Terminate, 0.75f);
            intSlider.SetInteractable(false);
            rdSlider.SetInteractable(false);
            delCheckbox.SetInteractable(false);
        }

        ObjectState GetCurElem() {
            return tmpState;
        }

        void SetDeleteMode(bool enabled) {
            placer.deleteMode = enabled;
        }

        void SetRandomRotation(bool enabled) {
            placer.randomRotation = enabled;
        }

        void SetMultiple(bool multiple) {
            intSlider.SetInteractable(multiple);
            rdSlider.SetInteractable(multiple);
            delCheckbox.SetInteractable(multiple);
            placer.multiple = multiple;
        }

        void SetTerrainIntensity(float val) {
            placer.multipleIntensity = val;
        }

        void SetTerrainRadius(float val) {
            placer.multipleRadius = val;
        }

        public override void SetActive(bool active) {
            if (active) {
                placer = builder.meshPlacer;
                placer.settings = tmpState;
                placer.placeEnabled = true;
                builder.terrainClick.modifier = placer;
                delCheckbox.SetValue(false);
                placer.deleteMode = false;
            } else {
                builder.SetSelectObjectMode();
            }
            base.SetActive(active);
        }
    }
}
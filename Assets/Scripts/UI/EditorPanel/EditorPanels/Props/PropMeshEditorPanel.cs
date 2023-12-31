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
                curW += uiInfo.width;
                if (curW > width) {
                    p0.IncreaseRow();
                    curW = uiInfo.width;
                }
                foreach (var param in settings.typeData.parametersInfo.parameters) {
                    if (param.name == uiInfo.name) {
                        var pFullName = "properties." + param.fullName();
                        switch (param.type) {
                            case "bool":
                                p0.AddFieldCheckbox(SM.Get(param.label), GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "float":
                                p0.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "texture":
                                p0.AddFieldTextureField(builder, SM.Get(param.label), SM.Get(param.placeholder), GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "string":
                                p0.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.Standard, GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "int":
                                p0.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.IntegerNumber, GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "enum":
                                var typesNames = new List<string>();
                                foreach (var t in param.enumLabels) typesNames.Add(SM.Get(t));
                                p0.AddFieldDropdown(SM.Get(param.label), typesNames, GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                        }
                        break;
                    }
                }
                if (i == settings.typeData.uiInfo.Length - 1) p0.IncreaseRow();
            }

            p0.AddButton(SM.Get("BACK"), GoUp, 0.75f);
            p0.AddButton(SM.Get("CLOSE"), Close, 0.75f);
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

        void Close() {
            SetActive(false);
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
                if (ActiveSelf()) builder.UnsetModifier();
            }
            base.SetActive(active);
        }
    }
}
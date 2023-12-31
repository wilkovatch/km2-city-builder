using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class TerrainPatchBorderMeshEditorPanel : EditorPanel {
        public TerrainBorderMesh curMesh = new TerrainBorderMesh();

        public TerrainPatchBorderMeshEditorPanel() {
            AddComplexElement(new PresetSelector(this));
            AddComplexElement(new TypeSelector<CityElements.Types.Runtime.RoadType>(this));
        }

        TypeSelector<CityElements.Types.Runtime.RoadType> TS() {
            return GetComplexElement<TypeSelector<CityElements.Types.Runtime.RoadType>>();
        }

        public override void Initialize(GameObject canvas) {
            titleActive = false;
            var pS = GetComplexElement<PresetSelector>();
            var tS = TS();
            var valid = tS.Initialize(CityElements.Types.Parsers.TypeParser.GetTerrainPatchBorderMeshTypes(), x => { return x.typeData.ui.label; }, true);
            if (!valid) {
                Initialize(canvas, 1, 1.5f);
                return;
            }
            var type = tS.types[tS.curType];
            foreach (var tab in type.typeData.ui.tabs) {
                pageButtonNames.Add(SM.Get(tab.label));
            }
            var rows = new List<int>();
            var nTabs = pageButtonNames.Count;
            if (nTabs < 4) {
                rows.Add(nTabs);
            } else {
                var nTabsDiv = nTabs / 2;
                rows.Add(nTabs - nTabsDiv);
                rows.Add(nTabsDiv);
            }
            var totW = type.typeData.ui.menuWidth;
            Initialize(canvas, rows, totW);

            for (int tabI = 0; tabI < type.typeData.ui.tabs.Length; tabI++) {
                var tab = type.typeData.ui.tabs[tabI];
                var p = GetPage(tabI);
                var curW = 0.0f;
                foreach (var elem in tab.elements) {
                    var w = elem.width;
                    if (elem.name == "mainGroup") {
                        //Main
                        if (!pS.EditingPreset()) {
                            p.AddButton(SM.Get("BACK"), GoUp, w / 2);
                            p.AddButton(SM.Get("CLOSE"), Terminate, w / 2);
                        } else {
                            p.AddButton(SM.Get("END_EDITING"), Terminate, w);
                        }
                        p.IncreaseRow();

                        tS.AddTypeDropdown(p, SM.Get("TP_BORDER_MESH_TYPE"), w);

                        System.Func<ObjectState> getter = delegate { return GetCurMesh().state; };
                        System.Func<ObjectState, ObjectState> setter = x => { GetCurMesh().state = (ObjectState)x.Clone(); return GetCurMesh().state; };
                        pS.AddPresetLoadAndSaveDropdown(p, SM.Get("TP_BORDER_MESH_PRESET"), true, "terrainPatchBorderMesh", setter, getter, false, null, null, null, w);

                    } else if (elem.name.Split('_')[0] == "PRESET") {
                        var parts = elem.name.Split('_');
                        System.Func<ObjectState> getter = delegate { return GetCurMesh().state.GetContainer(parts[1]); };
                        System.Func<ObjectState, ObjectState> setter = x => { GetCurMesh().state.SetContainer(x, parts[1]); return GetCurMesh().state; };
                        pS.AddPresetLoadAndSaveDropdown(p, parts[1].ToUpper() + "_PRESET", true, parts[1], setter, getter, true, x => { var obj = getter.Invoke(); obj.Name = x; setter.Invoke(obj); }, null, null, w);

                    } else {
                        curW += w;
                        if (curW > totW) {
                            p.IncreaseRow();
                            curW = w;
                        }
                        var parameters = type.typeData.parameters.parameters;
                        foreach (var param in parameters) {
                            if (param.fullName() == elem.name) {
                                var pFullName = (param.instanceSpecific ? "instanceState" : "state") + ".properties." + param.fullName();
                                switch (param.type) {
                                    case "bool":
                                        p.AddFieldCheckbox(SM.Get(param.label), GetCurMesh, pFullName, null, elem.width, SM.Get(param.tooltip));
                                        break;
                                    case "float":
                                        p.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurMesh, pFullName, null, elem.width, SM.Get(param.tooltip));
                                        break;
                                    case "texture":
                                        p.AddFieldTextureField(builder, SM.Get(param.label), SM.Get(param.placeholder), GetCurMesh, pFullName, null, elem.width, SM.Get(param.tooltip));
                                        break;
                                    case "string":
                                        p.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.Standard, GetCurMesh, pFullName, null, elem.width, SM.Get(param.tooltip));
                                        break;
                                    case "int":
                                        p.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.IntegerNumber, GetCurMesh, pFullName, null, elem.width, SM.Get(param.tooltip));
                                        break;
                                    case "enum":
                                        var typesNames = new List<string>();
                                        foreach (var t in param.enumLabels) typesNames.Add(SM.Get(t));
                                        p.AddFieldDropdown(SM.Get(param.label), typesNames, GetCurMesh, pFullName, null, elem.width, SM.Get(param.tooltip));
                                        break;
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }

        TerrainBorderMesh GetCurMesh() {
            if (curMesh == null) curMesh = new TerrainBorderMesh();
            return curMesh;
        }
    }
}
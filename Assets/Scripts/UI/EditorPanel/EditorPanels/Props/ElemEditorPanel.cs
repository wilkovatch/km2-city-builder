using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels.Props {
    public class ElemEditorPanel : EditorPanel {
        string tempMesh;

        ObjectState realElem;
        public List<string> allowedTypes = new List<string>();

        public ElemEditorPanel() {
            AddComplexElement(new PresetSelector(this));
            AddComplexElement(new TypeSelector<CityElements.Types.Runtime.PropsElementType>(this));
            AddComplexElement(new IOList(this));
        }

        TypeSelector<CityElements.Types.Runtime.PropsElementType> TS() {
            return GetComplexElement<TypeSelector<CityElements.Types.Runtime.PropsElementType>>();
        }

        public override void Initialize(GameObject canvas) {
            var width = 1.5f;
            Initialize(canvas, 1, width);
            var tS = TS();
            var allTypes = CityElements.Types.Parsers.TypeParser.GetPropsElementTypes();
            var filteredTypes = new Dictionary<string, CityElements.Types.Runtime.PropsElementType>();
            if (allowedTypes.Count == 0) {
                filteredTypes = allTypes;
            } else {
                foreach (var key in allTypes.Keys) {
                    if (allowedTypes.Contains(key)) filteredTypes.Add(key, allTypes[key]);
                }
            }
            var valid = tS.Initialize(filteredTypes, x => { return x.typeData.label; });
            if (!valid) return;
            var type = tS.types[tS.curType];
            var p = GetPage(0);
            var pS = GetComplexElement<PresetSelector>();
            pS.AddPresetLoadAndSaveDropdown(p, SM.Get("PROP_ELEM_PRESET"), true, "propElem", SetCurElem, GetCurElem, true, x => { var obj = GetCurElem().Name = x; }, allowedTypes, null, width);
            tS.AddTypeDropdown(p, SM.Get("PROP_ELEM_TYPE"), width);
            if (type.typeData.maxMeshes == 1) {
                var meshText = p.AddFieldInputField(SM.Get("PROP_ELEM_MESH"), SM.Get("VALUE"), UnityEngine.UI.InputField.ContentType.Standard, GetCurElem, "properties.mesh", null, width);
                meshText.SetInteractable(false);
                p.IncreaseRow();
                var editBtn = p.AddButton(SM.Get("CHANGE"), OpenMeshListForSingleChange, width);
                p.IncreaseRow();
            } else {
                var ioL = GetComplexElement<IOList>();
                var extraButtons = new List<(string, System.Action<string, int>, IOListButtonMode)>();
                extraButtons.Add((SM.Get("ADD_EMPTY"), (x, y) => { AddEmpty(); }, IOListButtonMode.NotFull));
                ioL.AddFullEditableList(p, "propMeshes", SM.Get("PROP_ELEM_MESHES"), SM.Get("ADD"), SM.Get("CHANGE"), SM.Get("DELETE"), type.typeData.maxMeshes,
                    OpenMeshList, OpenMeshListForChange, DeleteMesh, delegate { return GetList(); }, 5.0f, width, null, null, extraButtons);
            }
            var curW = 0.0f;
            for (int i = 0; i < type.typeData.uiInfo.Length; i++) {
                var uiInfo = type.typeData.uiInfo[i];
                curW += uiInfo.width;
                if (curW > width) {
                    p.IncreaseRow();
                    curW = uiInfo.width;
                }
                foreach (var param in type.typeData.parametersInfo.parameters) {
                    if (param.name == uiInfo.name) {
                        var pFullName = "properties." + param.fullName();
                        switch (param.type) {
                            case "bool":
                                p.AddFieldCheckbox(SM.Get(param.label), GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "float":
                                p.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.DecimalNumber, GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "texture":
                                p.AddFieldTextureField(builder, SM.Get(param.label), SM.Get(param.placeholder), GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "mesh":
                                p.AddFieldMeshField(builder, SM.Get(param.label), SM.Get(param.placeholder), GetCurElem, pFullName, null, true, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "string":
                                p.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.Standard, GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "int":
                                p.AddFieldInputField(SM.Get(param.label), SM.Get(param.placeholder), UnityEngine.UI.InputField.ContentType.IntegerNumber, GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                            case "enum":
                                var typesNames = new List<string>();
                                foreach (var t in param.enumLabels) typesNames.Add(SM.Get(t));
                                p.AddFieldDropdown(SM.Get(param.label), typesNames, GetCurElem, pFullName, null, uiInfo.width, SM.Get(param.tooltip));
                                break;
                        }
                        break;
                    }
                }
                if (i == type.typeData.uiInfo.Length - 1) p.IncreaseRow();
            }
            p.AddButton(SM.Get("SAVE"), Save, width / 3);
            p.AddButton(SM.Get("BACK"), GoUp, width / 3);
            p.AddButton(SM.Get("CLOSE"), Terminate, width / 3);
        }

        void NotifyChange() {
            if (parentPanel is ContainerEditorPanel p) {
                p.NotifyChange();
            }
        }

        public ObjectState GetCurElem() {
            return TS().GetState();
        }

        public ObjectState SetCurElem(ObjectState state) {
            TS().SetState(state);
            return state;
        }

        public void SetEditedElem(ObjectState state) {
            realElem = state;
            TS().SetStateDirect((ObjectState)state.Clone());
        }

        public void SetCurElem(ObjectState state, System.Action<ObjectState> newSetter) {
            TS().SetState(state, newSetter);
        }

        void DeleteMesh(string name, int i) {
            var meshes = GetCurElem().Array<string>("meshes");
            var lst = new List<string>();
            if (meshes != null) lst.AddRange(meshes);
            lst.RemoveAt(i);
            var elem = GetCurElem();
            elem.SetArray("meshes", lst.ToArray());
        }

        void ChangeMesh(int i) {
            var meshes = GetCurElem().Array<string>("meshes");
            var lst = new List<string>();
            if (meshes != null) lst.AddRange(meshes);
            lst[i] = tempMesh;
            var elem = GetCurElem();
            elem.SetArray("meshes", lst.ToArray());
            ReadCurValues();
        }

        void OpenMeshList(string name) {
            builder.OpenMeshSelector(this, null, x => tempMesh = x, AddElem, true);
        }

        void OpenMeshListForChange(string name, int i) {
            builder.OpenMeshSelector(this, null, x => tempMesh = x, delegate { ChangeMesh(i); }, true);
        }

        void ChangeSingleMesh() {
            var elem = GetCurElem();
            elem.SetStr("mesh", tempMesh);
            ReadCurValues();
        }

        void OpenMeshListForSingleChange() {
            builder.OpenMeshSelector(this, null, x => tempMesh = x, ChangeSingleMesh, true);
        }

        void AddEmpty() {
            tempMesh = "";
            AddElem();
        }

        void AddElem() {
            var name = tempMesh;
            var meshes = GetCurElem().Array<string>("meshes");
            var lst = new List<string>();
            if (meshes != null) lst.AddRange(meshes);
            lst.Add(name);
            var elem = GetCurElem();
            elem.SetArray("meshes", lst.ToArray());
            ReadCurValues();
            //SetCurElem(elem); //simple?
        }

        List<string> GetList() {
            var meshes = GetCurElem().Array<string>("meshes");
            List<string> lst = new List<string>();
            if (meshes != null) lst.AddRange(meshes);
            return lst;
        }

        public override void Terminate() {
            keepActive = false;
            SetActive(false);
            parentPanel.Terminate();
            base.Terminate();
        }

        void Save() {
            var tempElem = (ObjectState)GetCurElem().Clone();
            realElem.properties = tempElem.properties;
            NotifyChange();
            builder.NotifyChange(true);
            GoUp();
        }
    }
}
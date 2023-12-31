using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;
using EPM = EditorPanelElements;

namespace EditorPanels.Props {
    public class ContainerEditorPanel : EditorPanel {
        public ElemEditorPanel elemEditor = null;
        Dictionary<string, EPM.Button> btnControls = new Dictionary<string, EPM.Button>();
        public ObjectState parentState = null;

        public ContainerEditorPanel() {
            AddComplexElement(new PresetSelector(this));
            AddComplexElement(new TypeSelector<CityElements.Types.PropsContainerType>(this));
            AddComplexElement(new IOList(this));
        }

        TypeSelector<CityElements.Types.PropsContainerType> TS() {
            return GetComplexElement<TypeSelector<CityElements.Types.PropsContainerType>>();
        }

        public override void Initialize(GameObject canvas) {
            var width = 1.5f;
            Initialize(canvas, 1, width);
            var tS = TS();
            var valid = tS.Initialize(CityElements.Types.Parsers.TypeParser.GetPropsContainersTypes(), x => { return x.label; });
            if (!valid) return;
            btnControls.Clear();
            var type = tS.types[tS.curType];
            var p = GetPage(0);
            var pS = GetComplexElement<PresetSelector>();
            pS.AddPresetLoadAndSaveDropdown(p, SM.Get("PROP_CONTAINER_PRESET"), true, "propContainer", SetContainer, GetContainer, true, x => { var obj = GetContainer().Name = x; }, null, null, width);
            tS.AddTypeDropdown(p, SM.Get("PROP_CONTAINER_TYPE"), 1.5f);
            foreach (var param in type.parameters) {
                if (param.maxNumber == 1) {
                    var elemText = p.AddFieldInputField(SM.Get("PROP_ELEM_NAME_" + param.name.ToUpper()), SM.Get("EMPTY"), UnityEngine.UI.InputField.ContentType.Standard, GetContainer, "properties." + param.name + ".properties.name", null, width);
                    elemText.SetInteractable(false);
                    p.IncreaseRow();
                    var editBtn = p.AddButton(SM.Get("PROP_CONTAINER_EDIT_PROPERTY_" + param.name.ToUpper()), delegate { EditSingleElem(param.name); }, width);
                    btnControls.Add(param.name, editBtn);
                    p.IncreaseRow();
                } else {
                    var ioL = GetComplexElement<IOList>();
                    ioL.AddFullEditableList(p, param.name, SM.Get("PROP_ELEM_LIST_" + param.name.ToUpper()), SM.Get("ADD"), SM.Get("EDIT"), SM.Get("DELETE"), param.maxNumber,
                        AddElem, EditElem, DeleteElem, delegate { return GetList(param.name); }, 5.0f, width, "PROP_ELEM_UNNAMED");
                }
            }
            p.AddButton(SM.Get("CLOSE"), Terminate, width  /2);
            p.AddButton(SM.Get("BACK"), GoUp, width / 2);

            if (elemEditor == null) {
                elemEditor = AddChildPanel<ElemEditorPanel>(canvas);
            } else {
                childPanels.Add(elemEditor);
            }
        }

        public void NotifyChange() {
            parentState?.FlagAsChanged();
        }

        public override void Terminate() {
            SetActive(false);
            parentPanel.Terminate();
        }

        public ObjectState GetContainer() {
            return TS().GetState();
        }

        public ObjectState SetContainer(ObjectState state) {
            TS().SetState(state, null);
            parentState?.FlagAsChanged();
            return state;
        }

        public void SetContainer(ObjectState state, System.Action<ObjectState> newSetter) {
            TS().SetState(state, newSetter);
            parentState?.FlagAsChanged();
        }

        void DeleteElem(string name, int i) {
            var list = new List<ObjectState>();
            var array = GetContainer().Array<ObjectState>(name);
            if (array != null) list.AddRange(array);
            else return;
            list.RemoveAt(i);
            GetContainer().SetArray(name, list.ToArray());
            parentState?.FlagAsChanged();
            builder.NotifyChange(true);
            ReadCurValues();
        }

        List<string> GetAllowedTypes(string name) {
            var tS = TS();
            var type = tS.types[tS.curType];
            var types = new List<string>();
            foreach (var parameter in type.parameters) {
                if (parameter.name == name) {
                    if (parameter.allowedTypes != null) types = new List<string>(parameter.allowedTypes);
                    break;
                }
            }
            return types;
        }

        void AddElem(string name) {
            var types = GetAllowedTypes(name);
            var elem = PresetManager.GetFirstPresetByAllowedTypes("propElem", types);
            var list = new List<ObjectState>();
            var array = GetContainer().Array<ObjectState>(name);
            if (array != null) list.AddRange(array);
            list.Add(elem);
            GetContainer().SetArray(name, list.ToArray());
            parentState?.FlagAsChanged();
            builder.NotifyChange(true);
        }

        void EditElem(string name, int i) {
            var elem = GetContainer().Array<ObjectState>(name)[i];
            SetTypesList(name);
            elemEditor.SetEditedElem(elem);
            Hide(true);
            elemEditor.SetActive(true);
        }

        void SetTypesList(string name) {
            var types = GetAllowedTypes(name);
            elemEditor.allowedTypes = types;
        }

        void EditSingleElem(string name) {
            var elem = GetContainer().State(name);
            if (elem == null) {
                elem = new ObjectState();
                GetContainer().SetState(name, elem);
            }
            SetTypesList(name);
            elemEditor.SetEditedElem(elem);
            Hide(true);
            elemEditor.Initialize(lastCanvas);
            elemEditor.SetActive(true);
        }

        List<string> GetList(string name) {
            var lst = GetContainer().Array<ObjectState>(name);
            var items = new List<string>();
            if (lst != null) {
                foreach (var elem in lst) {
                    var preset = PresetManager.GetPresetByName(elem.Name, "propElem");
                    var modified = preset != null && elem.Name != null && elem.Name != "" && !elem.Equals(preset);
                    items.Add(elem.Name + (modified ? "*" : ""));
                }
            }
            return items;
        }

        public void Close() {
            parentState = null;
            SetActive(false);
        }
    }
}
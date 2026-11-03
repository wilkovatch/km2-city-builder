using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;

namespace EditorPanels {
    public class ArrayElemEditorPanel : EditorPanel {
        CityElements.Types.Parameter param = null;
        ObjectState curState;
        public int? arrayIndex = null; //null for custom city properties
        ContainerContentsPanel contentsPanel;
        string OwnedId => curState?.Str(Operations.PropertyContainers.IdKey, "") ?? "";

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            contentsPanel = null;
            if (OwnedId != "" && Operations.PropertyContainers.Find(OwnedId) == null) Operations.PropertyContainers.Sync(builder.helper.elementManager);
            var groups = Operations.PropertyContainers.ContentGroups(Operations.PropertyContainers.Find(OwnedId)?.settings);
            if (groups.Length > 0) contentsPanel = AddChildPanel<ContainerContentsPanel>(canvas);
            var curW = 0.0f;
            var propsTypes = param?.arrayProperties?.propsTypes;
            if (propsTypes != null) AddPropsFields(p0, ref curW, 1.5f, () => curState, x => Operations.Props.ApplyPreset(curState, x), new List<string>(propsTypes), true, true);
            var elemProperties = GetSubparams(param, curState);
            var layout = propsTypes != null ? new CityElements.Types.TabElement[0] : GetCustomType()?.uiInfo ?? System.Array.ConvertAll(elemProperties, x => new CityElements.Types.TabElement { name = x.fullName(), width = 1.5f });
            var placedGroups = new HashSet<int>();
            foreach (var elem in layout) {
                if (elem.name.StartsWith("content:")) {
                    var label = elem.name.Substring("content:".Length);
                    var group = System.Array.FindIndex(groups, x => x.label == label);
                    if (group < 0 || !placedGroups.Add(group)) continue;
                    PlaceInRow(p0, ref curW, 1.5f, elem.width);
                    AddContentsButton(p0, groups, group, elem.width);
                } else {
                    AddParameters(p0, ref curW, 1.5f, elemProperties, elem, GetCurState, false);
                }
            }
            //the groups that the layout doesn't place
            for (int i = 0; i < groups.Length; i++) {
                if (placedGroups.Contains(i)) continue;
                p0.EnsureNewRow();
                AddContentsButton(p0, groups, i, 1.5f);
                p0.IncreaseRow();
            }
            var ei = GetExclusiveEditingInfo();
            if (ei != null && ei.enabled && ei.optional) {
                p0.IncreaseRow();
                p0.AddButton(SM.Get("HIDE_OTHER"), HideOther, 1.5f, SM.Get("HIDE_OTHER_TOOLTIP"));
            }
            p0.EnsureNewRow();
            p0.AddButton(SM.Get("END_EDITING"), Terminate, 0.75f);
            p0.AddButton(SM.Get("ARRAY_ELEM_GO_UP"), GoUp, 0.75f);
        }

        void AddContentsButton(EditorPanelPage p, CityElements.Types.PropertyContainerContentGroup[] groups, int group, float width) {
            p.AddButton(SM.Get(groups[group].buttonLabel ?? groups[group].label), () => OpenContents(group), width);
        }

        CityElements.Types.CustomCityPropertiesType GetCustomType() {
            CityElements.Types.CustomCityPropertiesType ct = null;
            if (param != null) {
                if (param.arrayProperties != null) {
                    if (param.arrayProperties.customCityPropertiesType != null) {
                        ct = CityElements.Types.Parsers.TypeParser.GetCustomCityPropertiesTypes()[param.arrayProperties.customCityPropertiesType];
                    }
                } else if (param.customCityPropertiesType != null) {
                    ct = CityElements.Types.Parsers.TypeParser.GetCustomCityPropertiesTypes()[param.customCityPropertiesType];
                }
            }
            return ct;
        }

        CityElements.Types.CustomCityPropertiesExclusiveEditing GetExclusiveEditingInfo() {
            return GetCustomType()?.exclusiveEditing;
        }

        ObjectState GetCurState() {
            return curState;
        }

        //shows a list of the elements owned by the container, returns its panel (null if it has none)
        public EditorPanel OpenContents(int group) {
            if (contentsPanel == null) return null;
            contentsPanel.containerId = OwnedId;
            contentsPanel.groupIndex = group;
            Hide(true);
            contentsPanel.SetActive(true);
            return contentsPanel;
        }

        public void InitializeWithData(GameObject canvas, CityElements.Types.Parameter newParam, ObjectState newState) {
            getObject = GetCurState;
            param = newParam;
            curState = newState;
            Initialize(canvas);
        }

        void EditDirectlyIfObjectOnly() {
            if (ActiveSelf()) {
                var elemProperties = GetSubparams(param, curState);
                if (param != null && elemProperties.Length == 1) {
                    var subparam = elemProperties[0];
                    if (subparam.type == "objectInstance") {
                        EditObjectInstanceParameter(subparam.fullName(), new List<string>() { }, parentPanel);
                    }
                }
            }
        }

        void HideOther() {
            var info = GetExclusiveEditingInfo();
            if (info.enabled && info.optional) {
                var cur = ArrayObject.activeElements[info.category];
                var newState = cur == null ? curState : null;
                ArrayObject.activeElements[info.category] = newState;
                builder.NotifyVisibilityChange();
            }
        }

        public override System.Func<EditorPanel, EditorPanel> CaptureReopen() {
            var paramName = param?.fullName();
            var index = arrayIndex;
            if (paramName == null) return null;
            return parent => parent.OpenArrayElement(paramName, index);
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            base.SetActive(active);
            var info = GetExclusiveEditingInfo();
            if (active && info != null && info.enabled) {
                var cur = ArrayObject.activeElements[info.category];
                if (!info.optional || (info.optional && cur != null)) {
                    if (cur != curState) ArrayObject.PruneSelections(cur);
                    ArrayObject.activeElements[info.category] = curState;
                    builder.NotifyVisibilityChange();
                }
            }
            EditDirectlyIfObjectOnly();
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

namespace EditorPanels {
    public class ArrayElemEditorPanel : EditorPanel {
        CityElements.Types.Parameter param = null;
        ObjectState curState;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            var curW = 0.0f;
            var elemProperties = GetSubparams(param);
            foreach (var prop in elemProperties) {
                var tabElem = new CityElements.Types.TabElement();
                tabElem.width = 1.5f;
                tabElem.name = prop.fullName();
                AddParameters(p0, ref curW, 1.5f, elemProperties, tabElem, GetCurState, false);
            }
            var ei = GetExclusiveEditingInfo();
            if (ei != null && ei.enabled && ei.optional) {
                p0.IncreaseRow();
                p0.AddButton(SM.Get("HIDE_OTHER"), HideOther, 1.5f, SM.Get("HIDE_OTHER_TOOLTIP"));
            }
            p0.IncreaseRow();
            p0.AddButton(SM.Get("END_EDITING"), Terminate, 0.75f);
            p0.AddButton(SM.Get("ARRAY_ELEM_GO_UP"), GoUp, 0.75f);
        }

        CityElements.Types.CustomElementExclusiveEditing GetExclusiveEditingInfo() {
            CityElements.Types.CustomElement ct = null;
            if (param != null) {
                if (param.arrayProperties != null) {
                    if (param.arrayProperties.customElementType != null) {
                        ct = CityElements.Types.Parsers.TypeParser.GetCustomTypes()[param.arrayProperties.customElementType];
                    }
                } else if (param.customElementType != null) {
                    ct = CityElements.Types.Parsers.TypeParser.GetCustomTypes()[param.customElementType];
                }
            }
            if (ct != null && ct.exclusiveEditing != null) {
                return ct.exclusiveEditing;
            }
            return null;
        }

        ObjectState GetCurState() {
            return curState;
        }

        public void InitializeWithData(GameObject canvas, CityElements.Types.Parameter newParam, ObjectState newState) {
            getObject = GetCurState;
            param = newParam;
            curState = newState;
            Initialize(canvas);
        }

        void EditDirectlyIfObjectOnly() {
            if (ActiveSelf()) {
                var elemProperties = GetSubparams(param);
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

        public override void SetActive(bool active) {
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
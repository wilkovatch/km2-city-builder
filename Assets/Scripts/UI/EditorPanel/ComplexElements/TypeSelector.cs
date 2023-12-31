using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

public abstract partial class EditorPanel {
    protected class TypeSelector<T> : ComplexElement {
        public string curType = null;
        public ObjectState objectState = null;
        public System.Action<ObjectState> setter = null;
        public List<string> typesIndex = new List<string>();
        public List<string> typeNames = new List<string>();
        public Dictionary<string, T> types;
        EditorPanelElements.Dropdown typeDropdown = null;

        public TypeSelector(EditorPanel panel) : base(panel) { }

        public bool Initialize(Dictionary<string, T> types, System.Func<T, string> labelGetter, bool forceOnNullObject = false) {
            panel.needsReloading = true;
            if (types.Count == 0) return false;
            curType = GetCurType();
            if (objectState == null) {
                if (forceOnNullObject) {
                    objectState = new ObjectState();
                } else {
                    return false;
                }
            }
            this.types = types;
            typeNames = new List<string>();
            foreach (var elem in types) {
                typeNames.Add(SM.Get(labelGetter.Invoke(elem.Value)));
                typesIndex.Add(elem.Key);
            }
            if (types.Count == 0) return false;
            if (curType == null || curType == "") {
                foreach (var elem in types) {
                    curType = elem.Key;
                    objectState.SetStr("type", curType);
                    break;
                }
            }
            ReloadTypeDropdown();
            return true;
        }

        public void AddTypeDropdown(EditorPanelPage p, string name, float width) {
            typeDropdown = p.AddDropdown(name, typeNames, SwitchType, width);
            ReloadTypeDropdown();
            p.IncreaseRow();
        }

        string GetCurType() {
            if (objectState != null) {
                return objectState.Str("type");
            }
            return null;
        }

        void SwitchType(int i) {
            var curType = typesIndex[i];
            var s = new ObjectState();
            s.SetStr("type", curType);
            SetState(s);
        }

        public ObjectState GetState() {
            return objectState;
        }

        public void SetState(ObjectState state) {
            SetState(state, null);
        }

        public void SetStateDirect(ObjectState state) {
            objectState = state;
            panel.Initialize(panel.lastCanvas);
            panel.ReadCurValues();
        }

        public void SetState(ObjectState state, System.Action<ObjectState> newSetter) {
            objectState = state;
            if (newSetter != null) {
                setter = newSetter;
            }
            setter?.Invoke(state);
            panel.Initialize(panel.lastCanvas);
            panel.ReadCurValues();
        }

        public override void BaseExtraAction() {
            
        }

        public override void Destroy() {
            
        }

        void ReloadTypeDropdown() {
            if (typeDropdown == null) return;
            typeDropdown.actionEnabled = false;
            typeDropdown.SetValue(typesIndex.IndexOf(curType));
            typeDropdown.actionEnabled = true;
        }

        public override void ReadCurValues() {
            var newType = GetCurType();
            if (newType != curType) {
                curType = newType;
                panel.Initialize(panel.lastCanvas);
                panel.ReadCurValues();
            }
        }

        public override void SetActive(bool active) {
            
        }

        public override void SyncState(ObjectState state) {
            objectState = state;
        }
    }
}
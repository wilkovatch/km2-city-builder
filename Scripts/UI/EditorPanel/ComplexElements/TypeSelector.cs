using System.Collections;
using System.Collections.Generic;
using SM = StringManager;

public abstract partial class EditorPanel {
    protected class TypeSelector<T> : ComplexElement {
        public string curType = null;
        public ObjectState objectState = null;
        public System.Action<ObjectState> setter = null;
        public List<string> typesIndex = new List<string>();
        public List<string> typeNames = new List<string>();
        public Dictionary<string, T> types;
        // objectState is the state of the edited object, so selecting an existing object pre-selects its type
        // and switching type reaches the object.
        // Panels editing objects owned elsewhere (roads, intersections, ...) read the edited object's state through this.
        public System.Func<ObjectState> stateGetter = null;
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
            typesIndex = new List<string>();
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
            var editedState = stateGetter?.Invoke();
            if (editedState != null) objectState = editedState;
            if (objectState != null) {
                return objectState.Str("type");
            }
            return null;
        }

        void SwitchType(int i) {
            var curType = typesIndex[i];
            var editedState = stateGetter?.Invoke();
            if (editedState != null) {
                // Only the type changes: the object keeps its other properties (missing ones read as defaults).
                Operations.States.SetType(editedState, curType);
                objectState = editedState;
                panel.builder?.NotifyChange();
                ReinitializePanel();
                return;
            }
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
            ReinitializePanel();
        }

        public void SetState(ObjectState state, System.Action<ObjectState> newSetter) {
            objectState = state;
            if (newSetter != null) {
                setter = newSetter;
            }
            setter?.Invoke(state);
            ReinitializePanel();
        }

        // Rebuilding the panel creates all its pages visible (the last one ends up on top): stay on the active tab.
        void ReinitializePanel() {
            var page = panel.ActivePage();
            panel.Initialize(panel.lastCanvas);
            panel.ReadCurValues();
            if (page >= 0 && panel.pages.Count > 0) panel.SetPage(System.Math.Min(page, panel.pages.Count - 1));
        }

        public override void BaseExtraAction() {
            
        }

        // The dropdown belongs to the pages being destroyed (freed at frame end): a panel rebuilt by its parent
        // (e.g. the props element editor, when the container editor reinitializes) must not keep using it.
        // Writing to the destroyed dropdown would throw ObjectDisposedException.
        public override void Destroy() {
            typeDropdown = null;
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
                ReinitializePanel();
            }
        }

        public override void SetActive(bool active) {
            
        }

        public override void SyncState(ObjectState state) {
            objectState = state;
        }
    }
}
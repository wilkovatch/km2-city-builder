using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;
using EPM = EditorPanelElements;

public abstract partial class EditorPanel {
    protected class IOList : ComplexElement {
        public class ListTuple {
            public EPM.ScrollList list;
            public EPM.Button addBtn;
            public EPM.Button selectBtn;
            public EPM.Button delBtn;
            public System.Action<string> addAction;
            public System.Action<string, int> selectAction;
            public System.Action<string, int> deleteAction;
            public System.Func<List<string>> listGetter;
            public int maxNumber;

            public ListTuple(EPM.ScrollList list, EPM.Button addBtn, EPM.Button selectBtn, EPM.Button delBtn, int maxNumber,
                System.Action<string> addAction, System.Action<string, int> selectAction, System.Action<string, int> deleteAction, System.Func<List<string>> listGetter) {

                this.list = list;
                this.addBtn = addBtn;
                this.selectBtn = selectBtn;
                this.delBtn = delBtn;
                this.addAction = addAction;
                this.selectAction = selectAction;
                this.deleteAction = deleteAction;
                this.listGetter = listGetter;
                this.maxNumber = maxNumber;
            }
        }

        Dictionary<string, int> selectedIs = new Dictionary<string, int>();
        Dictionary<string, ListTuple> listControls = new Dictionary<string, ListTuple>();
        Dictionary<string, List<string>> curNames = new Dictionary<string, List<string>>();

        public IOList(EditorPanel panel) : base(panel) { }

        public void AddSelectableList(EditorPanelPage p, string key, string listName, string selectText, int maxNumber,
            System.Action<string, int> selectAction, System.Func<List<string>> listGetter, float height, float width, string emptyLabel) {

            AddFullEditableList(p, key, listName, null, selectText, null, maxNumber, null, selectAction, null, listGetter, height, width, emptyLabel, null);
        }

        public void AddSimpleEditableList(EditorPanelPage p, string key, string listName, string addText, string deleteText, int maxNumber,
            System.Action<string> addAction, System.Action<string, int> deleteAction, System.Func<List<string>> listGetter,
            float height, float width, string emptyLabel) {

            AddFullEditableList(p, key, listName, addText, null, deleteText, maxNumber, addAction, null, deleteAction, listGetter, height, width, emptyLabel, null);
        }

        public void AddFullEditableList(EditorPanelPage p, string key, string listName, string addText, string selectText, string deleteText, int maxNumber,
            System.Action<string> addAction, System.Action<string, int> selectAction, System.Action<string, int> deleteAction, System.Func<List<string>> listGetter,
            float height, float width, string emptyLabel = null, List<(string, System.Action)> extraButtons = null) {

            var list = p.AddScrollList(listName, new List<string>(), x => { SetIndex(key, x); }, width, null, null, emptyLabel); ;
            p.IncreaseRow(height);
            var divider = GetDiv(addText) + GetDiv(selectText) + GetDiv(deleteText);
            var partWidth = width / divider;
            var addBtn = addText != null ? p.AddButton(addText, delegate { AddElem(key); }, partWidth) : null;
            var selectBtn = selectText != null ? p.AddButton(selectText, delegate { SelectElem(key); }, partWidth) : null;
            var delBtn = deleteText != null ? p.AddButton(deleteText, delegate { DeleteElem(key); }, partWidth) : null;
            selectBtn?.SetInteractable(false);
            delBtn?.SetInteractable(false);
            listControls.Add(key, new ListTuple(list, addBtn, selectBtn, delBtn, maxNumber, addAction, selectAction, deleteAction, listGetter));
            curNames.Add(key, new List<string>());
            selectedIs.Add(key, -1);
            p.IncreaseRow();
            if (extraButtons !=null && extraButtons.Count > 0) {
                foreach (var elem in extraButtons) {
                    p.AddButton(elem.Item1, elem.Item2, width / extraButtons.Count);
                }
                p.IncreaseRow();
            }
        }

        float GetDiv(string text) {
            return text != null ? 1.0f : 0.0f;
        }

        void SetIndex(string name, int i) {
            selectedIs[name] = i;
            UpdateEditInteractable(name);
        }

        void UpdateEditInteractable(string name) {
            var enabled = selectedIs[name] >= 0;
            var maxNumber = listControls[name].maxNumber;
            var addEnabled = maxNumber > 0 ? curNames[name].Count < maxNumber : true;
            listControls[name].addBtn?.SetInteractable(addEnabled);
            listControls[name].selectBtn?.SetInteractable(enabled);
            listControls[name].delBtn?.SetInteractable(enabled);
        }

        void SelectElem(string key) {
            if (selectedIs[key] < 0) return;
            listControls[key].selectAction.Invoke(key, selectedIs[key]);
        }

        void AddElem(string key) {
            listControls[key].addAction.Invoke(key);
            ReadCurValues();
        }

        void DeleteElem(string key) {
            if (selectedIs[key] < 0 || selectedIs[key] >= curNames[key].Count) return;
            listControls[key].deleteAction.Invoke(key, selectedIs[key]);
            ReadCurValues();
        }

        void ReloadList(string name, bool deselect) {
            var list = listControls[name].list;
            list.Deselect();
            var lst = listControls[name].listGetter.Invoke();
            if (lst != null) {
                var items = new List<string>();
                foreach (var elem in lst) {
                    items.Add(elem);
                }
                list.SetItems(items);
                curNames[name] = items;
            } else {
                list.SetItems(new List<string>());
            }
            if (deselect) selectedIs[name] = -1;
            UpdateEditInteractable(name);
        }

        public override void BaseExtraAction() {

        }

        public override void Destroy() {
            selectedIs.Clear();
            listControls.Clear();
            curNames.Clear();
        }

        public override void ReadCurValues() {
            foreach (var key in listControls.Keys) {
                ReloadList(key, true);
            }
        }

        public override void SetActive(bool active) {

        }

        public override void SyncState(ObjectState state) {

        }
    }
}

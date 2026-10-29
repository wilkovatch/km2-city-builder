using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;
using GO = GameEngine.GameObject;

namespace EditorPanels {
    //A child panel of the low-level element editor listing low-level elements to act on one of them (e.g. the container
    //to merge the element into, or to move it to): the parent is hidden while it's shown, and the action runs after the
    //parent is shown again
    public class LowLevelElementPickerPanel : EditorPanel {
        EditorPanelElements.Label titleLabel, currentLabel;
        EditorPanelElements.ScrollList lst;
        EditorPanelElements.Button actionButton, cancelButton;
        List<LowLevelElement> items = new List<LowLevelElement>(); //null is none
        System.Action<LowLevelElement> action;
        int curI = -1;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            titleLabel = p0.AddLabel("", 1.5f);
            p0.IncreaseRow();
            currentLabel = p0.AddLabel("", 1.5f);
            p0.IncreaseRow();
            lst = p0.AddScrollList("", new List<string>(), Select, 1.5f);
            p0.IncreaseRow(5.0f);
            actionButton = p0.AddButton("", Act, 1.5f);
            p0.IncreaseRow();
            cancelButton = p0.AddButton(SM.Get("CANCEL"), GoUp, 1.5f);
        }

        //currentText: shown above the list (hidden if null)
        public void Open(string title, string listTitle, List<LowLevelElement> items, string currentText, string actionLabel,
            System.Action<LowLevelElement> action) {

            parentPanel.Hide(true);
            SetActive(true);
            this.items = items;
            this.action = action;
            titleLabel.SetValue(title);
            currentLabel.SetValue(currentText ?? "");
            currentLabel.SetActive(currentText != null);
            var names = new List<string>();
            foreach (var item in items) names.Add(item == null ? SM.Get("NONE") : GO.GetName(item));
            lst.SetTitle(listTitle);
            lst.Deselect();
            lst.SetItems(names);
            actionButton.SetTitle(actionLabel);
            curI = -1;
            actionButton.SetInteractable(false);
        }

        void Select(int i) {
            curI = i;
            actionButton.SetInteractable(i >= 0 && i < items.Count);
        }

        void Act() {
            if (curI < 0 || curI >= items.Count) return;
            var picked = items[curI];
            var a = action;
            action = null;
            GoUp();
            a?.Invoke(picked);
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            if (!active) action = null;
            base.SetActive(active);
        }
    }
}

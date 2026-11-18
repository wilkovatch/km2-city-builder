using System.Collections;
using System.Collections.Generic;
using GameObject = Godot.Control;
using SM = StringManager;

namespace EditorPanels {
    //The panel of the multiple selection (see ElementPlacer.MultiSelector): how many elements are selected, the gizmo
    //to transform them with, and what can be done to all of them
    public class MultiSelectionEditorPanel : EditorPanel {
        EditorPanelElements.Label countLabel;
        EditorPanelElements.Button copyButton, deleteButton, clearButton;

        ElementPlacer.MultiSelector Selector => builder.multiSelector;

        public override void Initialize(GameObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            countLabel = p0.AddLabel("", 1.5f);
            p0.IncreaseRow();
            p0.AddLabel(SM.Get("MS_HELP"), 1.5f, null, null, 3.0f);
            p0.IncreaseRow(3.0f);
            p0.AddButton(SM.Get("MS_MOVE"), () => Selector.SetTransformType(RuntimeGizmos.TransformType.Move), 0.5f, SM.Get("MS_TRANSFORM_TOOLTIP"));
            p0.AddButton(SM.Get("MS_ROTATE"), () => Selector.SetTransformType(RuntimeGizmos.TransformType.Rotate), 0.5f, SM.Get("MS_TRANSFORM_TOOLTIP"));
            p0.AddButton(SM.Get("MS_SCALE"), () => Selector.SetTransformType(RuntimeGizmos.TransformType.Scale), 0.5f, SM.Get("MS_TRANSFORM_TOOLTIP"));
            p0.IncreaseRow();
            p0.AddButton(SM.Get("MS_SELECT_ALL"), () => Selector.SelectAll(), 0.75f);
            clearButton = p0.AddButton(SM.Get("MS_CLEAR"), () => Selector.Clear(), 0.75f);
            p0.IncreaseRow();
            copyButton = p0.AddButton(SM.Get("COPY_PROPERTIES_FROM"), CopyProperties, 1.5f, SM.Get("MS_COPY_PROPERTIES_TOOLTIP"));
            p0.IncreaseRow();
            deleteButton = p0.AddButton(SM.Get("MS_DELETE"), Delete, 0.75f);
            p0.AddButton(SM.Get("CLOSE"), Terminate, 0.75f);
            p0.IncreaseRow();
        }

        void Refresh() {
            if (countLabel == null) return;
            var count = Selector.Selection.Count;
            countLabel.SetValue(SM.Get(count == 1 ? "MS_SELECTED_SING" : "MS_SELECTED_PLUR").Replace("$NUMBER", count.ToString()));
            copyButton.SetInteractable(count > 0);
            deleteButton.SetInteractable(count > 0);
            clearButton.SetInteractable(count > 0);
        }

        //the properties of the element clicked go to every selected element of its kind
        public void CopyProperties() {
            var targets = new List<object>(Selector.Selection);
            builder.elementPicker.Start(
                element => targets.Exists(t => Operations.Properties.Check(element, t) == Operations.Properties.CopyResult.Copied),
                element => History.UndoManager.Record(SM.Get("COPY_PROPERTIES"), () => {
                    Operations.Properties.CopyToAll(element, targets);
                    builder.NotifyChange(true);
                }));
        }

        public void Delete() {
            var targets = new List<object>(Selector.Selection);
            if (targets.Count == 0) return;
            builder.CreateAlert(SM.Get("WARNING"), SM.Get("MS_DELETE_WARNING").Replace("$NUMBER", targets.Count.ToString()), SM.Get("YES"), SM.Get("NO"), () => {
                History.UndoManager.Record(SM.Get("DELETE"), () => {
                    Selector.Clear();
                    Operations.GroupTransform.Delete(builder.helper.elementManager, targets);
                    builder.NotifyChange(true);
                });
            });
        }

        public override void SetActive(bool active) {
            if (!PrepareActivation(active)) return;
            var wasActive = ActiveSelf();
            base.SetActive(active);
            if (active) {
                Selector.onSelectionChanged = Refresh;
                Selector.Start();
                Refresh();
            } else if (wasActive) {
                if (builder.elementPicker.IsActive()) builder.elementPicker.Stop();
                Selector.onSelectionChanged = null;
                Selector.Stop();
                if (Selector.IsActive()) builder.SetSelectObjectMode();
            }
        }

        public override void Terminate() {
            SetActive(false);
        }
    }
}

using System.Collections.Generic;
using Godot;
using SM = StringManager;

namespace EditorPanels.Props {
    //A props container of the prop rule of an element: its preset, its type and the fields of its type, applied at once
    public class PropsEditorPanel : EditorPanel {
        ObjectState owner;
        string containerName;
        EditorPanelElements.Label errorLabel;

        public string ContainerName => containerName;

        public void SetContainer(ObjectState ownerState, string name) {
            owner = ownerState;
            containerName = name;
            Initialize(lastCanvas);
        }

        //read every time: a preset or an undo can replace it
        public ObjectState GetContainer() {
            return Operations.Props.GetPropRule(owner, false)?.State(containerName);
        }

        void ApplyPreset(ObjectState preset) {
            try { PropPlacerGeometry.ValidateContainer(owner, containerName, preset); }
            catch (System.ArgumentException) {
                builder.CreateAlert(SM.Get("ERROR"), SM.Get("PP_INVALID_PLACEMENT"), SM.Get("OK"));
                return;
            }
            Operations.Props.GetPropRule(owner, true).SetState(containerName, preset);
            builder.NotifyChange(true);
        }

        public override void Initialize(Control canvas) {
            var width = 1.5f;
            errorLabel = null;
            Initialize(canvas, 1, width);
            var state = GetContainer();
            if (state == null) return;
            Operations.Props.EnsureType(state);
            getObject = GetContainer;
            var p = GetPage(0);
            var curW = 0.0f;
            AddPropsFields(p, ref curW, width, GetContainer, ApplyPreset, Operations.Props.AllowedTypes(owner, containerName), true);
            errorLabel = p.AddLabel("", width);
            p.IncreaseRow();
            p.AddButton(SM.Get("CLOSE"), Terminate, width / 2);
            p.AddButton(SM.Get("BACK"), GoUp, width / 2);
        }

        //the placement error of the element being edited (e.g. a parameter out of range)
        public void ShowError(string error) {
            errorLabel?.SetValue(error ?? "");
        }

        public override void Terminate() {
            keepActive = false;
            SetActive(false);
            parentPanel.Terminate();
            base.Terminate();
        }

        public override System.Func<EditorPanel, EditorPanel> CaptureReopen() {
            var name = containerName;
            if (!(parentPanel is IPropRuleEditor) || name == null) return null;
            return parent => (parent as IPropRuleEditor)?.OpenPropContainer(name);
        }
    }
}

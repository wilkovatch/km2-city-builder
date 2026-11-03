using System.Collections.Generic;
using SM = StringManager;

//the panels editing an element with a prop rule: its props containers can be edited in the props editor (see
//Props.PropsEditorPanel.CaptureReopen)
public interface IPropRuleEditor {
    EditorPanel OpenPropContainer(string name);
}

public abstract partial class EditorPanel {
    //The prop rule of an element (a road, a custom element of a type with props containers), the panel group
    //"propsGroup": the preset of the rule, and for each props container of the type a checkbox enabling it and a button
    //editing it in the props editor
    protected class PropRuleGroup {
        readonly EditorPanel panel;
        readonly System.Func<bool, ObjectState> ownerState; //the state holding the rule (true: the element can be created)
        readonly System.Action changed; //after the rule was changed by the group
        string inlineStrip; //the props container shown directly in the panel, if any
        public readonly Dictionary<string, (EditorPanelElements.Button button, EditorPanelElements.Checkbox checkbox)> controls =
            new Dictionary<string, (EditorPanelElements.Button, EditorPanelElements.Checkbox)>();

        public PropRuleGroup(EditorPanel panel, System.Func<bool, ObjectState> ownerState, System.Action changed) {
            this.panel = panel;
            this.ownerState = ownerState;
            this.changed = changed;
        }

        //presetTypes: the types of the prop rule presets listed (null: all)
        public void Add(EditorPanelPage p, PresetSelector pS, string[] containers, List<string> presetTypes, float w) {
            var owner = ownerState(false);
            controls.Clear();
            inlineStrip = null;
            if (containers?.Length == 1 && PropPlacerGeometry.Strip(owner ?? new ObjectState(), containers[0])?.inline == true) {
                AddInline(p, containers[0], w);
                return;
            }
            var placer = owner != null && CityElements.Types.Parsers.TypeParser.GetCustomElementTypes().TryGetValue(owner.Str("type", ""), out var type)
                && type.typeData.settings.propPlacer != null;
            if (!placer) pS.AddPresetLoadAndSaveDropdown(p, SM.Get("RD_PROP_RULE_PRESET"), true, "roadPropRule", SetRule, GetRule, true,
                x => { var obj = GetRule(true); obj.Name = x; SetRule(obj); }, presetTypes, null, w);
            foreach (var containerType in containers ?? new string[0]) {
                var cb = p.AddCheckbox(SM.Get("RD_PROP_ENABLED"), false, x => { EnableContainer(containerType, x); }, w * 0.3f);
                var labelKey = "EDIT_PROP_CONTAINER_" + containerType.ToUpper();
                var label = SM.GetOrKey(labelKey);
                var strip = PropPlacerGeometry.Strip(ownerState(false) ?? new ObjectState(), containerType);
                if (strip?.label != null) label = SM.Get(strip.label);
                else if (label == labelKey) label = SM.Get("PP_EDIT_STRIP").Replace("$STRIP", containerType);
                var btn = p.AddButton(label, delegate { EditContainer(containerType); }, w * 0.7f);
                controls.Add(containerType, (btn, cb));
                p.IncreaseRow();
            }
        }

        //the props of an inline strip, in the panel of the element (whose preset saves them)
        void AddInline(EditorPanelPage p, string name, float w) {
            inlineStrip = name;
            //always enabled, once here as the fields read the state at any time (also with no element, after a change of click mode)
            var owner = ownerState(false);
            if (owner != null && GetRule()?.State(name) == null) Operations.Props.GetPropRule(owner, true).SetState(name, Operations.Props.NewState(Operations.Props.AllowedTypes(owner, name)[0]));
            ObjectState State() => GetRule()?.State(name);
            var curW = 0.0f;
            panel.AddPropsFields(p, ref curW, w, State, null, Operations.Props.AllowedTypes(ownerState(false), name), false);
        }

        //the type of the props shown in the panel of the element, which is built for it
        public string InlineType() {
            return inlineStrip == null ? null : GetRule()?.State(inlineStrip)?.Str("type", null);
        }

        public ObjectState GetRule() {
            return GetRule(false);
        }

        ObjectState GetRule(bool createIfNull) {
            var res = Operations.Props.GetPropRule(ownerState(true), false);
            if (res == null && createIfNull) {
                res = new ObjectState(true);
                SetRule(res);
            }
            return res;
        }

        ObjectState SetRule(ObjectState state) {
            Operations.Props.SetPropRule(ownerState(true), state);
            changed?.Invoke();
            return state;
        }

        void EnableContainer(string name, bool enabled) {
            Operations.Props.EnablePropContainer(ownerState(true), name, enabled);
            changed?.Invoke();
        }

        //the checkboxes and buttons follow the rule
        public void Read() {
            foreach (var key in controls.Keys) {
                var rule = GetRule();
                var enabled = rule != null && rule.State(key) != null;
                controls[key].button.SetInteractable(enabled);
                var cb = controls[key].checkbox;
                cb.actionEnabled = false;
                cb.SetValue(enabled);
                cb.actionEnabled = true;
            }
        }

        void EditContainer(string name) {
            var propsPanel = panel.builder.propsEditorPanel;
            propsPanel.parentPanel = panel;
            propsPanel.SetContainer(ownerState(false), name);
            panel.Hide(true);
            propsPanel.SetActive(true);
        }

        //the editor of the container (if enabled), as opened by its button
        public EditorPanel OpenContainer(string name) {
            if (inlineStrip != null || ownerState(false) == null || GetRule()?.State(name) == null) return null;
            EditContainer(name);
            return panel.builder.propsEditorPanel;
        }
    }
}

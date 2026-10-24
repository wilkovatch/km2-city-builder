using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using States;
using System;
using RuntimeCalculator;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

//TODO: rename and refactor (it's not just for array objects, but for custom elements too)
public partial class ArrayObject : ParametricObjectContainer<CityElements.Types.ArrayProperties>, IObjectWithState {
    public ObjectState state;
    public string paramInParent;
    public CityElements.Types.Runtime.ArrayProperties runtimeProperties;
    VariableContainer vc;
    public CityElements.Types.CustomElementExclusiveEditing exclusiveEditingInfo;
    public static Dictionary<string, ObjectState> activeElements = new Dictionary<string, ObjectState>();

    public override ObjectState[] GetContainerStateArrayForParametricObjects() {
        return new ObjectState[] { state };
    }

    public override ObjectState GetContainerStateForParametricObjects() {
        return state;
    }

    public override VariableContainer GetParametricObjectVariableContainer() {
        return vc;
    }

    //the parameters of the element
    protected override VariableContainer GetLineVariableContainer() {
        var res = runtimeProperties.variableContainer;
        runtimeProperties.FillInitialVariables(res, state);
        return res;
    }

    //the arrays may have new elements, hidden if the element is
    protected override void OnArraysSynced() {
        UpdateVisibility();
    }

    public override CityElements.Types.Runtime.RuntimeType<CityElements.Types.ArrayProperties> GetRuntimeType() {
        return runtimeProperties;
    }

    public ObjectState GetState() {
        return state;
    }

    public void SetState(ObjectState newState) {
        state = newState;
        UpdateVisibility();
    }

    internal virtual void Initialize() {
        state = new ObjectState();
        vc = new VariableContainer(new List<(string, Type)>());
    }

    public void UpdateVisibility() {
        var i = exclusiveEditingInfo;
        if (i != null && i.enabled) {
            if (!activeElements.ContainsKey(i.category)) {
                activeElements[i.category] = null;
            }
            var activeElement = activeElements[i.category];
            var obj = GO.GetObject(this);
            var active = i.optional ? activeElement == null || activeElement == state : activeElement == state;
            if (obj == null) return;
            if (i.keepLinesVisible) {
                //only the objects (the lines are children too)
                GO.SetEnabled(obj, true);
                foreach (var child in obj.GetChildren()) {
                    if (child is Node3D node && child is not GameEngine.LineRenderer) GO.SetEnabled(node, active);
                }
            } else {
                GO.SetEnabled(obj, active);
            }
        }
    }

    public static ArrayObject Create(GameObject parent, string paramName, string key, CityElements.Types.ArrayProperties properties) {
        var subObj = GS.Create<ArrayObject>(key, parent);
        var res = GS.Cast<ArrayObject>(subObj);
        res.runtimeProperties = new CityElements.Types.Runtime.ArrayProperties(properties);
        res.paramInParent = paramName;
        if (properties.customElementType != null) {
            var ct = CityElements.Types.Parsers.TypeParser.GetCustomTypes()[properties.customElementType];
            if (ct.exclusiveEditing != null) {
                res.exclusiveEditingInfo = ct.exclusiveEditing;
            }
        }
        res.UpdateVisibility();
        return res;
    }

    public static ArrayObject Create(GameObject parent, string paramName, string key, string ctName) {
        var subObj = GS.Create<ArrayObject>(key, parent);
        var res = GS.Cast<ArrayObject>(subObj);
        res.runtimeProperties = CityElements.Types.Runtime.ArrayProperties.FromCustomType(ctName);
        res.paramInParent = paramName;
        var ct = CityElements.Types.Parsers.TypeParser.GetCustomTypes()[ctName];
        if (ct.exclusiveEditing != null) {
            res.exclusiveEditingInfo = ct.exclusiveEditing;
        }
        res.UpdateVisibility();
        return res;
    }

    public static void PruneSelections(ObjectState oldParent) {
        var resetKeys = new List<string>();
        foreach (var entry in activeElements) {
            if (entry.Value != null && entry.Value.IsContainedIn(oldParent)) {
                resetKeys.Add(entry.Key);
            }
        }
        foreach (var key in resetKeys) {
            activeElements[key] = null;
        }
    }

    public static void DeselectAll() {
        var resetKeys = new List<string>();
        foreach (var entry in activeElements) {
            resetKeys.Add(entry.Key);
        }
        foreach (var key in resetKeys) {
            activeElements[key] = null;
        }
    }

    public void Deselect() {
        PruneSelections(state);
    }
}

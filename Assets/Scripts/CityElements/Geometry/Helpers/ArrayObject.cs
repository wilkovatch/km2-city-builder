using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;
using System;
using RuntimeCalculator;

//TODO: rename and refactor (it's not just for array objects, but for custom elements too)
public class ArrayObject : ParametricObjectContainer<CityElements.Types.ArrayProperties>, IObjectWithState {
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
            if (i.optional) {
                var active = activeElement == null || activeElement == state;
                gameObject?.SetActive(active);
            } else {
                gameObject?.SetActive(activeElement == state);
            }
        }
    }

    public static ArrayObject Create(GameObject parent, string paramName, string key, CityElements.Types.ArrayProperties properties) {
        var subObj = new GameObject();
        subObj.name = key;
        subObj.transform.parent = parent.transform;
        var res = subObj.AddComponent<ArrayObject>();
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
        var subObj = new GameObject();
        subObj.name = key;
        subObj.transform.parent = parent.transform;
        var res = subObj.AddComponent<ArrayObject>();
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

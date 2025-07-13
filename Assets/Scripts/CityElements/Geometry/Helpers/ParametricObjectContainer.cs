using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;
using RC = RuntimeCalculator;

public abstract class ParametricObjectContainer<T> : MonoBehaviour {
    public Dictionary<string, GameObject> objectInstances = new Dictionary<string, GameObject>();
    public bool repositionObjects = false;
    public bool resetObjectsPositions = false;
    public Dictionary<string, List<ArrayObject>> arrayObjects = new Dictionary<string, List<ArrayObject>>();

    public abstract RC.VariableContainer GetParametricObjectVariableContainer();

    public abstract ObjectState GetContainerStateForParametricObjects();

    public abstract ObjectState[] GetContainerStateArrayForParametricObjects();

    public abstract CityElements.Types.Runtime.RuntimeType<T> GetRuntimeType();

    public virtual bool VerifyObjectCondition(string key) {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return false;
        var objParams = GetRuntimeType().objectInstanceParams;
        var settings = objParams[key].objectInstanceSettings;
        var condition = settings.condition;
        if (condition == null) return true;
        var vc = GetParametricObjectVariableContainer();
        return runtimeType.boolDefinitions[condition].GetValue(vc);
    }

    public virtual Vector3 GetObjectPosition(string key) {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return Vector3.zero;
        var objParams = GetRuntimeType().objectInstanceParams;
        var settings = objParams[key].objectInstanceSettings;
        var variable = settings.basePosition;
        if (variable == null) return Vector3.zero;
        var vc = GetParametricObjectVariableContainer();
        return runtimeType.vector3Definitions[variable].GetValue(vc);
    }

    public virtual Vector3 GetObjectRotation(string key) {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return Vector3.zero;
        var objParams = GetRuntimeType().objectInstanceParams;
        var settings = objParams[key].objectInstanceSettings;
        var variable = settings.baseRotation;
        if (variable != null) {
            var vc = GetParametricObjectVariableContainer();
            return runtimeType.vector3Definitions[variable].GetValue(vc);
        } else {
            return Vector3.zero;
        }
    }

    public virtual Vector3 GetObjectScale(string key) {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return Vector3.one;
        var objParams = GetRuntimeType().objectInstanceParams;
        var settings = objParams[key].objectInstanceSettings;
        var variable = settings.baseScale;
        if (variable != null) {
            var vc = GetParametricObjectVariableContainer();
            return runtimeType.vector3Definitions[variable].GetValue(vc);
        } else {
            return Vector3.one;
        }
    }

    public List<MeshInstance> GetParametricObjects() {
        var res = new List<MeshInstance>();
        foreach (var param in objectInstances) {
            res.Add(param.Value.GetComponentInChildren<MeshInstance>());
        }
        return res;
    }

    public MeshInstance GetParametricObject(string key, List<string> parentParams) {
        if (parentParams.Count > 0) {
            var parts = parentParams[parentParams.Count - 1].Split(".");
            if (parts.Length >= 2) {
                parentParams.RemoveAt(parentParams.Count - 1);
                return arrayObjects[parts[0]][int.Parse(parts[1])].GetParametricObject(key, parentParams);
            } else if (parts.Length == 1) {
                parentParams.RemoveAt(parentParams.Count - 1);
                return arrayObjects[parts[0]][0].GetParametricObject(key, parentParams);
            } else {
                return null;
            }
        } else {
            return objectInstances[key].GetComponentInChildren<MeshInstance>();
        }
    }

    ObjectState GetParametricObjectState(string key) {
        var state = GetContainerStateForParametricObjects();
        ObjectState instState = state?.State(key);
        if (instState == null) {
            instState = new ObjectState();
            state?.SetState(key, instState);
        }
        return instState;
    }

    private float GetFloat(string name) {
        var vc = GetParametricObjectVariableContainer();
        if (vc.floatIndex.ContainsKey(name)) return vc.floats[vc.floatIndex[name]];
        return GetRuntimeType().numberDefinitions[name].GetValue(vc);
    }

    string GetDefaultModel(CityElements.Types.ObjectInstanceSettings instSettings) {
        var defaultModel = "";
        var defaultModelSettings = instSettings.defaultModel;
        if (defaultModelSettings != null) {
            if (defaultModelSettings.model != null) {
                defaultModel = defaultModelSettings.model;
            } else {
                var modelIndex = 0;
                if (defaultModelSettings.index != null) {
                    modelIndex = (int)GetFloat(defaultModelSettings.index);
                }
                defaultModel = defaultModelSettings.options[modelIndex];
                if (defaultModelSettings.dynamic) {
                    var states = GetContainerStateArrayForParametricObjects();
                    defaultModel = ObjectState.SearchArray<string>(states, defaultModel, null);
                }
            }
        }
        return defaultModel;
    }

    Vector3 GetScale(CityElements.Types.ObjectInstanceSettings instSettings, ObjectState instState) {
        var defaultScaleArr = instSettings.defaultScale;
        if (defaultScaleArr == null) defaultScaleArr = new float[] { 1f, 1f, 1f };
        var defaultScale = new Vector3(defaultScaleArr[0], defaultScaleArr[1], defaultScaleArr[2]);
        var scale = instState.Vector3("localScale", defaultScale);
        return scale;
    }

    void SyncObjectInstanceParamsDict() {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return;
        var objParams = runtimeType.objectInstanceParams;
        var state = GetContainerStateForParametricObjects();

        //remove everything to avoid sync issues
        List<string> keysToDelete = new List<string>();
        foreach (var param in objectInstances) {
            keysToDelete.Add(param.Key);
        }
        foreach (var key in keysToDelete) {
            objectInstances[key].GetComponentInChildren<MeshInstance>().Delete();
            objectInstances.Remove(key);
        }

        //re add the objects
        foreach (var param in objParams) {
            var instSettings = param.Value.objectInstanceSettings;
            var isEnabled = VerifyObjectCondition(param.Key);
            if (isEnabled) {
                var instState = GetParametricObjectState(param.Key);
                string model = instState.Str("meshPath", null);
                if (model == null || !instSettings.allowCustomModel) model = GetDefaultModel(instSettings);
                if (state?.State(param.Key) == null) state?.SetState(param.Key, new ObjectState());
                var obj = MeshInstance.CreateParametric(model, gameObject, true, gameObject.name + " (" + param.Key + ")", param.Value, instState);
                objectInstances.Add(param.Key, obj.transform.parent.gameObject);
                obj.transform.localScale = GetScale(instSettings, instState);
            }
        }
    }

    public void SetParametricObjectsMoveable(bool moveable) {
        foreach (var obj in objectInstances) {
            obj.Value.GetComponentInChildren<MeshInstance>().SetMoveable(moveable);
        }
    }

    void SyncArrays() {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return;
        var arrayParams = runtimeType.arrayParams;
        var state = GetContainerStateForParametricObjects();
        var parentName = gameObject.name;
        if (parentName == "dummy (CityProperties)") parentName = "City Properties";
        foreach (var param in arrayParams) {
            var arr = state?.Array<ObjectState>(param.Key);
            if (arr == null) arr = new ObjectState[0];
            if (!arrayObjects.ContainsKey(param.Key)) arrayObjects[param.Key] = new List<ArrayObject>();
            var aL = arrayObjects[param.Key];
            if (arr.Length > aL.Count) {
                var diff = arr.Length - aL.Count;
                for (int i = 0; i < diff; i++) {
                    var newArrObj = ArrayObject.Create(gameObject, param.Key, parentName + " - " + param.Key + " (" + i + ")", param.Value);
                    aL.Add(newArrObj);
                }
            } else if (arr.Length < aL.Count) {
                var diff = aL.Count - arr.Length;
                for (int i = 0; i < diff; i++) {
                    aL[aL.Count - 1].Deselect();
                    Destroy(aL[aL.Count - 1].gameObject); //todo: remove the ones actually deleted
                    aL.RemoveAt(aL.Count - 1);
                }
            }
            for (int i = 0; i < arr.Length; i++) {
                aL[i].SetState(arr[i]);
            }
        }
        var customElemParams = runtimeType.customElemParams;
        foreach (var param in customElemParams) {
            var objState = state?.State(param.Key);
            if (objState == null) {
                objState = new ObjectState();
                state?.SetState(param.Key, objState);
            }
            if (!arrayObjects.ContainsKey(param.Key)) {
                arrayObjects[param.Key] = new List<ArrayObject>();
                var newArrObj = ArrayObject.Create(gameObject, param.Key, parentName + " - " + param.Key, param.Value);
                arrayObjects[param.Key].Add(newArrObj);
            }
            var aL = arrayObjects[param.Key];
            aL[0].SetState(objState);
        }
    }

    List<string> GetContainersKeys() {
        var res = new List<string>();
        var runtimeType = GetRuntimeType();
        if (runtimeType != null) {
            foreach (var param in runtimeType.arrayParams) {
                res.Add(param.Key);
            }
            foreach (var param in runtimeType.customElemParams) {
                res.Add(param.Key);
            }
        }
        return res;
    }

    public void SyncVisibility() {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return;
        var containersKeys = GetContainersKeys();
        foreach (var paramKey in containersKeys) {
            var state = GetContainerStateForParametricObjects();
            if (arrayObjects.ContainsKey(paramKey)) {
                var aL = arrayObjects[paramKey];
                for (int i = 0; i < aL.Count; i++) {
                    aL[i].UpdateVisibility();
                    aL[i].SyncVisibility();
                }
            }
        }
    }

    public virtual void Delete() {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return;
        var containersKeys = GetContainersKeys();
        foreach (var paramKey in containersKeys) {
            if (arrayObjects.ContainsKey(paramKey)) {
                var aL = arrayObjects[paramKey];
                for (int i = 0; i < aL.Count; i++) {
                    aL[i].Deselect();
                    Destroy(aL[i].gameObject);
                }
            }
        }
        Destroy(gameObject);
    }

    public int UpdateObjects() {
        var state = GetContainerStateForParametricObjects();
        var oldDirty = state.HasChanged();
        var runtimeType = GetRuntimeType();
        int counter = 0;
        if (runtimeType == null) return counter;
        if (repositionObjects) {
            SyncObjectInstanceParamsDict();
            foreach (var obj in objectInstances) {
                //base values
                obj.Value.transform.position = GetObjectPosition(obj.Key);
                obj.Value.transform.rotation = Quaternion.Euler(GetObjectRotation(obj.Key));
                obj.Value.transform.localScale = GetObjectScale(obj.Key);
                var inst = obj.Value.GetComponentInChildren<MeshInstance>();
                inst.SyncParametricObjectState(resetObjectsPositions ? GetParametricObjectState(obj.Key) : null);

                counter += 1;
            }
            //recurse arrays
            SyncArrays();
            var arrayParams = runtimeType.arrayParams;
            foreach (var param in arrayParams) {
                var list = arrayObjects[param.Key];
                foreach (var elem in list) {
                    elem.repositionObjects = true;
                    elem.resetObjectsPositions = resetObjectsPositions;
                    elem.UpdateObjects();
                }
            }
            var customElemParams = runtimeType.customElemParams;
            foreach (var param in customElemParams) {
                var elem = arrayObjects[param.Key][0];
                elem.repositionObjects = true;
                elem.resetObjectsPositions = resetObjectsPositions;
                elem.UpdateObjects();
            }
        }
        repositionObjects = false;
        resetObjectsPositions = false;
        if (state.HasChanged() && !oldDirty) {
            state.FlagAsUnchanged(); //object sync flagged the state as dirty, restore it to avoid issues
        }
        return counter;
    }
}

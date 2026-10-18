using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using States;
using RC = RuntimeCalculator;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using V3 = GameEngine.Vector3;

//non-generic access to the parametric objects of a container, to find one again after they are regenerated
public interface IParametricObjectContainer {
    string FindObjectKey(GameObject holder);
    int IndexOfArrayObject(ArrayObject arrayObject);
    MeshInstance FindParametricObject(string key, List<string> parentParams);
}

public abstract partial class ParametricObjectContainer<T> : Scriptable, IParametricObjectContainer {
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
        if (runtimeType == null) return GameEngine.Vector3.zero;
        var objParams = GetRuntimeType().objectInstanceParams;
        var settings = objParams[key].objectInstanceSettings;
        var variable = settings.basePosition;
        if (variable == null) return GameEngine.Vector3.zero;
        var vc = GetParametricObjectVariableContainer();
        return runtimeType.vector3Definitions[variable].GetValue(vc);
    }

    public virtual Vector3 GetObjectRotation(string key) {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return GameEngine.Vector3.zero;
        var objParams = GetRuntimeType().objectInstanceParams;
        var settings = objParams[key].objectInstanceSettings;
        var variable = settings.baseRotation;
        if (variable != null) {
            var vc = GetParametricObjectVariableContainer();
            return runtimeType.vector3Definitions[variable].GetValue(vc);
        } else {
            return GameEngine.Vector3.zero;
        }
    }

    public virtual Vector3 GetObjectScale(string key) {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return GameEngine.Vector3.one;
        var objParams = GetRuntimeType().objectInstanceParams;
        var settings = objParams[key].objectInstanceSettings;
        var variable = settings.baseScale;
        if (variable != null) {
            var vc = GetParametricObjectVariableContainer();
            return runtimeType.vector3Definitions[variable].GetValue(vc);
        } else {
            return GameEngine.Vector3.one;
        }
    }

    public List<MeshInstance> GetParametricObjects() {
        var res = new List<MeshInstance>();
        foreach (var param in objectInstances) {
            res.Add(GS.FindRecursive<MeshInstance>(param.Value));
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
            return GS.FindRecursive<MeshInstance>(objectInstances[key]);
        }
    }

    //the key of the parametric object kept in the given node
    public string FindObjectKey(GameObject holder) {
        foreach (var entry in objectInstances) {
            if (entry.Value == holder) return entry.Key;
        }
        return null;
    }

    public int IndexOfArrayObject(ArrayObject arrayObject) {
        if (arrayObject?.paramInParent == null || !arrayObjects.TryGetValue(arrayObject.paramInParent, out var list)) return -1;
        return list.IndexOf(arrayObject);
    }

    //like GetParametricObject, but null if the path does not exist anymore
    public MeshInstance FindParametricObject(string key, List<string> parentParams) {
        if (parentParams.Count > 0) {
            var parts = parentParams[parentParams.Count - 1].Split(".");
            parentParams.RemoveAt(parentParams.Count - 1);
            var index = 0;
            if (parts.Length >= 2 && !int.TryParse(parts[1], out index)) return null;
            if (!arrayObjects.TryGetValue(parts[0], out var list) || index < 0 || index >= list.Count) return null;
            var arrayObject = list[index];
            if (arrayObject == null || !IsInstanceValid(arrayObject) || arrayObject.IsQueuedForDeletion()) return null;
            return arrayObject.FindParametricObject(key, parentParams);
        }
        if (key == null || !objectInstances.TryGetValue(key, out var holder)) return null;
        if (holder == null || !IsInstanceValid(holder) || holder.IsQueuedForDeletion()) return null;
        return GS.FindRecursive<MeshInstance>(holder);
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

    //a number definition (evaluated now, like the other hooks) or a variable
    private float GetFloat(string name) {
        var vc = GetParametricObjectVariableContainer();
        if (GetRuntimeType().numberDefinitions.TryGetValue(name, out var definition)) return definition.GetValue(vc);
        return vc.floats[vc.floatIndex[name]];
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
        var defaultScale = V3.DecodeScale(new Vector3(defaultScaleArr[0], defaultScaleArr[1], defaultScaleArr[2]));
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
            GS.FindRecursive<MeshInstance>(objectInstances[key]).Delete();
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
                var obj = MeshInstance.CreateParametric(model, GO.GetObject(this), true, GO.GetName(this) + " (" + param.Key + ")", param.Value, instState);
                objectInstances.Add(param.Key, GO.GetParent(obj));
                GO.SetLocalScale(obj, GetScale(instSettings, instState));
            }
        }
    }

    public void SetParametricObjectsMoveable(bool moveable) {
        foreach (var obj in objectInstances) {
            GS.FindRecursive<MeshInstance>(obj.Value).SetMoveable(moveable);
        }
    }

    void SyncArrays() {
        var runtimeType = GetRuntimeType();
        if (runtimeType == null) return;
        var arrayParams = runtimeType.arrayParams;
        var state = GetContainerStateForParametricObjects();
        var parentName = GO.GetName(this);
        if (parentName == "dummy (CityProperties)") parentName = "City Properties";
        foreach (var param in arrayParams) {
            var arr = state?.Array<ObjectState>(param.Key);
            if (arr == null) arr = new ObjectState[0];
            if (!arrayObjects.ContainsKey(param.Key)) arrayObjects[param.Key] = new List<ArrayObject>();
            var aL = arrayObjects[param.Key];
            if (arr.Length > aL.Count) {
                var diff = arr.Length - aL.Count;
                for (int i = 0; i < diff; i++) {
                    var newArrObj = ArrayObject.Create(GO.GetObject(this), param.Key, parentName + " - " + param.Key + " (" + i + ")", param.Value);
                    aL.Add(newArrObj);
                }
            } else if (arr.Length < aL.Count) {
                var diff = aL.Count - arr.Length;
                for (int i = 0; i < diff; i++) {
                    aL[aL.Count - 1].Deselect();
                    GO.Destroy(GO.GetObject(aL[aL.Count - 1])); //todo: remove the ones actually deleted
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
                var newArrObj = ArrayObject.Create(GO.GetObject(this), param.Key, parentName + " - " + param.Key, param.Value);
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
        if (runtimeType == null) {
            GO.Destroy(GO.GetObject(this));
            return;
        }
        var containersKeys = GetContainersKeys();
        foreach (var paramKey in containersKeys) {
            if (arrayObjects.ContainsKey(paramKey)) {
                var aL = arrayObjects[paramKey];
                for (int i = 0; i < aL.Count; i++) {
                    aL[i].Deselect();
                    GO.Destroy(GO.GetObject(aL[i]));
                }
            }
        }
        GO.Destroy(GO.GetObject(this));
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
                GO.SetPosition(obj.Value, GetObjectPosition(obj.Key));
                GO.SetRotation(obj.Value, GetObjectRotation(obj.Key));
                GO.SetLocalScale(obj.Value, GetObjectScale(obj.Key));
                var inst = GS.FindRecursive<MeshInstance>(obj.Value);
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

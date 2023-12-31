using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectState: ICloneable, IEquatable<ObjectState>, IComparable, IObjectWithState {
    [JsonExtensionData]
    public Dictionary<string, object> properties; //do not access directly, use the other methods

    bool dirty = false;

    [JsonIgnore]
    public string Name { get => GetProperty("name", ""); set => SetProperty("name", value); }

    [JsonIgnore]
    public ObjectState state { get => this; set => ReplaceWith(value); }

    public ObjectState() {
        properties = new Dictionary<string, object>();
    }

    public ObjectState(bool dirty) {
        this.dirty = dirty;
        properties = new Dictionary<string, object>();
    }

    [System.Runtime.Serialization.OnDeserialized]
    internal void OnDeserializedMethod(System.Runtime.Serialization.StreamingContext context) {
        AdjustTypes();
    }

    public ObjectState GetContainer(string name) {
        var res = new ObjectState();
        foreach (var k in properties.Keys) {
            var parts = k.Split('_');
            if (parts[0] == name) {
                res.SetProperty(parts[1], properties[k]);
            }
        }
        return res;
    }

    public void SetContainer(ObjectState input, string name) {
        foreach (var k in input.properties.Keys) {
            properties[name + "_" + k] = input.properties[k];
        }
    }

    public T GetProperty<T>(string name, T defaultValue) {
        if (!properties.ContainsKey(name)) return defaultValue;
        var obj = properties[name];
        if (obj is T objT) return objT;
        else return defaultValue;
    }

    public void SetProperty(string name, object value) {
        properties[name] = value;
        dirty = true;
    }

    object GetAdjusted(object obj) {
        if (obj is double valD) return (float)valD;
        else if (obj is long valI) return (int)valI;
        else if (obj is Newtonsoft.Json.Linq.JObject valJ) return States.Utils.DeJsonify(valJ);
        else if (obj is Newtonsoft.Json.Linq.JArray valJA) return States.Utils.DeJsonify(valJA);
        else if (obj is Dictionary<string, object> dict) return dict;
        else if (obj is object[] arr) return arr;
        else return null;
    }

    static T[] RationalizeArray<T>(object[] origArray) {
        var orArray = origArray;
        var obj = new T[orArray.Length];
        for (int i = 0; i < orArray.Length; i++) {
            var arrElem = orArray[i];
            obj[i] = (T)arrElem;
        }
        return obj;
    }

    void AdjustTypes() {
        var newValues = new List<(string key, object value)>();
        foreach (var k in properties.Keys) {
            var val = GetAdjusted(properties[k]);
            if (val != null) {
                if (val is Dictionary<string, object> dict) {
                    var subState = new ObjectState();
                    subState.properties = dict;
                    subState.AdjustTypes();
                    newValues.Add((k, subState));
                } else if (val is object[] arr) {
                    if (arr.Length > 0) {
                        if (arr[0] is int) newValues.Add((k, RationalizeArray<int>(arr)));
                        else if (arr[0] is float) newValues.Add((k, RationalizeArray<float>(arr)));
                        else if (arr[0] is string) newValues.Add((k, RationalizeArray<string>(arr)));
                        else if (arr[0] is bool) newValues.Add((k, RationalizeArray<bool>(arr)));
                        else if (arr[0] is Dictionary<string, object>) {
                            var newArr = new ObjectState[arr.Length];
                            for (int i = 0; i < arr.Length; i++) {
                                var subObj = (Dictionary<string, object>)arr[i];
                                var subState = new ObjectState();
                                subState.properties = subObj;
                                subState.AdjustTypes();
                                newArr[i] = subState;
                            }
                            newValues.Add((k, newArr));
                        }
                    } else {
                        newValues.Add((k, null));
                    }
                } else {
                    newValues.Add((k, val));
                }
            }
        }
        foreach (var v in newValues) {
            if (v.value != null) {
                properties[v.key] = v.value;
            } else {
                properties.Remove(v.key);
            }
        }
    }

    public bool HasChanged() {
        return dirty;
    }

    public void FlagAsChanged() {
        dirty = true;
    }

    public void FlagAsUnchanged() {
        dirty = false;
    }

    public int Int(string name, int defaultValue = 0) {
        return GetProperty(name, defaultValue);
    }

    public string Str(string name, string defaultValue = "") {
        return GetProperty(name, defaultValue);
    }

    public float Float(string name, float defaultValue = 0.0f) {
        return GetProperty(name, defaultValue);
    }

    public bool Bool(string name, bool defaultValue = false) {
        return GetProperty(name, defaultValue);
    }

    public Vector2 Vector2(string name) {
        return Vector2(name, UnityEngine.Vector2.zero);
    }

    public Vector3 Vector3(string name) {
        return Vector3(name, UnityEngine.Vector3.zero);
    }

    public Vector2 Vector2(string name, Vector2 defaultValue) {
        return GetProperty(name, defaultValue);
    }

    public Vector3 Vector3(string name, Vector3 defaultValue) {
        return GetProperty(name, defaultValue);
    }

    public ObjectState State(string name, bool defaultNull = true) {
        return GetProperty(name, defaultNull ? null : new ObjectState());
    }

    public T[] Array<T>(string name, bool defaultNull = true) {
        return GetProperty(name, defaultNull ? null : new T[0]);
    }

    public void SetInt(string name, int value) {
        SetProperty(name, value);
    }

    public void SetStr(string name, string value) {
        SetProperty(name, value);
    }

    public void SetFloat(string name, float value) {
        SetProperty(name, value);
    }

    public void SetBool(string name, bool value) {
        SetProperty(name, value);
    }

    public void SetVector2(string name, Vector2 value) {
        SetProperty(name, value);
    }

    public void SetVector3(string name, Vector3 value) {
        SetProperty(name, value);
    }

    public void SetState(string name, ObjectState value) {
        SetProperty(name, value);
    }

    public void SetArray<T>(string name, T[] value) {
        SetProperty(name, value);
    }

    static T[] DeepCloneArray<T>(T[] origArray) {
        var orArray = origArray;
        var obj = new T[orArray.Length];
        for (int i = 0; i < orArray.Length; i++) {
            var arrElem = orArray[i];
            obj[i] = (T)DeepCloneObject(arrElem);
        }
        return obj;
    }

    static object DeepCloneObject(object obj) {
        if (obj is ObjectState origState) return origState.Clone();
        else if (obj is int[] intArray) return DeepCloneArray(intArray);
        else if (obj is float[] floatArray) return DeepCloneArray(floatArray);
        else if (obj is string[] strArray) return DeepCloneArray(strArray);
        else if (obj is bool[] boolArray) return DeepCloneArray(boolArray);
        else if (obj is ObjectState[] objArray) return DeepCloneArray(objArray);
        else return obj;
    }

    public object Clone() {
        var res = new ObjectState();
        res.dirty = dirty;
        foreach (var key in properties.Keys) {
            var clonedProperty = DeepCloneObject(properties[key]);
            res.properties.Add(key, clonedProperty);
        }
        return res;
    }

    static bool ArrayDeepEquals<T>(object a, object b) {
        var aA = (T[])a;
        var aB = (T[])b;
        if (aA.Length != aB.Length) return false;
        for (int i = 0; i < aA.Length; i++) {
            if (!DeepEquals(aA[i], aB[i])) return false;
        }
        return true;
    }

    static bool DeepEquals(object a, object b) {
        if (a == null != (b == null)) return false;
        if (a == null) return true;
        var tA = a.GetType();
        var tB = b.GetType();
        if (!tA.Equals(tB)) return false;
        else if (tA == typeof(ObjectState)) return ((ObjectState)a).Equals((ObjectState)b);
        else if (tA == typeof(int[])) return ArrayDeepEquals<int>(a, b);
        else if (tA == typeof(float[])) return ArrayDeepEquals<float>(a, b);
        else if (tA == typeof(string[])) return ArrayDeepEquals<string>(a, b);
        else if (tA == typeof(bool[])) return ArrayDeepEquals<bool>(a, b);
        else if (tA == typeof(ObjectState[])) return ArrayDeepEquals<ObjectState>(a, b);
        else return a.Equals(b);
    }

    public bool Equals(ObjectState other) {
        if (other == null) return false;
        if (properties == null != (other.properties == null)) return false;
        if (properties == null) return true;
        if (properties.Keys.Count != other.properties.Keys.Count) return false;
        foreach (var key in properties.Keys) {
            if (!other.properties.ContainsKey(key)) return false;
            if (!DeepEquals(properties[key], other.properties[key])) return false;
        }
        return true;
    }

    public int CompareTo(object obj) {
        if (obj is ObjectState state) {
            return Name.CompareTo(state.Name);
        } else {
            return -1;
        }
    }

    public ObjectState GetState() {
        return this;
    }

    public void ReplaceWith(ObjectState state) {
        var tmp = (ObjectState)state.Clone();
        properties = tmp.properties;
    }
}
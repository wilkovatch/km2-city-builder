using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SM = StringManager;

public class FieldStatus {
    public System.Func<object> objectGetter;
    public Dictionary<string, System.Func<string>> indexGetters;
    public string fieldName;
    System.Action valueChangedAction;

    public FieldStatus(System.Func<object> getter, string name, System.Action valueChangedAction, Dictionary<string, System.Func<string>> indexGetters = null) {
        objectGetter = getter;
        fieldName = name;
        this.indexGetters = indexGetters;
        this.valueChangedAction = valueChangedAction;
    }

    private string GetArrayInfo(string field0) {
        string arrayIndex = null;
        if (field0.Contains("$")) {
            var indexName = field0.Split('$')[1];
            if (indexGetters != null && indexGetters.ContainsKey(indexName)) {
                arrayIndex = indexGetters[indexName].Invoke();
            } else {
                return indexName;
            }
        }
        return arrayIndex;
    }

    private object GetValueSub(object obj, string fieldName) {
        if (obj == null) return null;
        if (fieldName.Contains(",")) {
            var fields = fieldName.Split(',');
            return GetValueSub(obj, fields[0]);
        } else if (fieldName.Contains(".")) {
            var field0 = fieldName.Split('.')[0];
            var fieldsLeft = fieldName.Substring(field0.Length + 1);
            var arrayIndex = GetArrayInfo(field0);
            object obj2;
            if (arrayIndex != null) {
                if (obj is System.Array arrObj) {
                    obj2 = arrObj.GetValue(int.Parse(arrayIndex));
                } else if (obj is Dictionary<string, string> dictObj) {
                    if (dictObj.ContainsKey(arrayIndex)) {
                        obj2 = dictObj[arrayIndex];
                    } else {
                        obj2 = null;
                    }
                } else {
                    throw new System.Exception("Invalid indexed property");
                }
            } else {
                if (obj is Dictionary<string, object> dict) {
                    obj2 = dict[field0];
                } else {
                    var field = obj.GetType().GetField(field0);
                    if (field != null) {
                        obj2 = field.GetValue(obj);
                    } else {
                        obj2 = obj.GetType().GetProperty(field0).GetValue(obj);
                    }
                }
            }
            return GetValueSub(obj2, fieldsLeft);
        } else {
            var arrayIndex = GetArrayInfo(fieldName);
            if (arrayIndex != null) {
                if (obj is System.Array arrObj) {
                    return arrObj.GetValue(int.Parse(arrayIndex));
                } else if (obj is Dictionary<string, string> dictObj) {
                    if (dictObj.ContainsKey(arrayIndex)) {
                        return dictObj[arrayIndex];
                    } else {
                        return null;
                    }
                } else {
                    throw new System.Exception("Invalid indexed property");
                }
            } else {
                if (obj is Dictionary<string, object> dict) {
                    return dict.ContainsKey(fieldName) ? dict[fieldName] : null;
                } else {
                    var field = obj.GetType().GetField(fieldName);
                    if (field != null) {
                        return field.GetValue(obj);
                    } else {
                        return obj.GetType().GetProperty(fieldName).GetValue(obj);
                    }
                }
            }
        }
    }

    public object GetValue() {
        var obj = objectGetter.Invoke();
        return GetValueSub(obj, fieldName);
    }

    public static object GetValue(object obj, string fieldName) {
        var s = new FieldStatus(delegate { return obj; }, fieldName, null);
        return s.GetValue();
    }

    private object SetValueSub(object obj, string fieldName, object value) {
        if (obj == null) return null;
        if (obj is ObjectState objS) objS.FlagAsChanged();
        if (fieldName.Contains(",")) {
            var fields = fieldName.Split(',');
            foreach (var field in fields) {
                SetValueSub(obj, field, value);
            }
        } else if (fieldName.Contains(".")) {
            var field0 = fieldName.Split('.')[0];
            var fieldsLeft = fieldName.Substring(field0.Length + 1);
            var arrayIndex = GetArrayInfo(field0);
            object obj2;
            if (arrayIndex != null) {
                if (obj is System.Array arrObj) {
                    obj2 = arrObj.GetValue(int.Parse(arrayIndex));
                } else if (obj is Dictionary<string, string> dictObj) {
                    if (dictObj.ContainsKey(arrayIndex)) {
                        obj2 = dictObj[arrayIndex];
                    } else {
                        obj2 = null;
                    }
                } else {
                    throw new System.Exception("Invalid indexed property");
                }
            } else {
                if (obj is Dictionary<string, object> dict) {
                    obj2 = dict[field0];
                } else {
                    var field = obj.GetType().GetField(field0);
                    if (field != null) {
                        obj2 = field.GetValue(obj);
                    } else {
                        obj2 = obj.GetType().GetProperty(field0).GetValue(obj);
                    }
                }
            }
            var obj2b = SetValueSub(obj2, fieldsLeft, value);
            if (arrayIndex != null) {
                ((System.Array)obj).SetValue(obj2b, int.Parse(arrayIndex));
            } else if (obj2.GetType().IsValueType) {
                if (obj is Dictionary<string, object> dict) {
                    dict[field0] = obj2b;
                } else {
                    var field2 = obj.GetType().GetField(field0);
                    if (field2 != null) {
                        field2.SetValue(obj, obj2b);
                    } else {
                        obj.GetType().GetProperty(field0).SetValue(obj, obj2b);
                    }
                }
            }
        } else {
            var arrayIndex = GetArrayInfo(fieldName);
            if (arrayIndex != null) {
                if (obj is System.Array arrObj) {
                    arrObj.SetValue(value, int.Parse(arrayIndex)); //TODO: TEST
                } else if (obj is Dictionary<string, string> dictObj) {
                    dictObj[arrayIndex] = value.ToString();
                } else {
                    throw new System.Exception("Invalid indexed property");
                }
            } else {
                if (obj is Dictionary<string, object> dict) {
                    dict[fieldName] = value;
                } else {
                    var field = obj.GetType().GetField(fieldName);
                    if (field != null) {
                        field.SetValue(obj, value);
                    } else {
                        obj.GetType().GetProperty(fieldName).SetValue(obj, value);
                    }
                }
            }
        }
        return obj;
    }

    public void SetValue(object value) {
        if (valueChangedAction != null) valueChangedAction.Invoke();
        var obj = objectGetter.Invoke();
        SetValueSub(obj, fieldName, value);
    }

    public static void SetValue(object obj, object value, string fieldName) {
        var s = new FieldStatus(delegate { return obj; }, fieldName, null);
        if (value is string str) {
            int intVal;
            float floatVal;
            if (int.TryParse(str, out intVal)) s.SetValue(intVal);
            else if (float.TryParse(str, out floatVal)) s.SetValue(floatVal);
            else if (str == "True") s.SetValue(true);
            else if (str == "False") s.SetValue(false);
            else s.SetValue(str);
        } else {
            s.SetValue(value);
        }

    }

    public void SetInputFieldValue(string value, InputField.ContentType type) {
        try {
            switch (type) {
                case InputField.ContentType.IntegerNumber:
                    SetValue(int.Parse(value));
                    break;
                case InputField.ContentType.DecimalNumber:
                    SetValue(float.Parse(value));
                    break;
                default:
                    SetValue(value);
                    break;
            }
        } catch (System.Exception e) {
            MonoBehaviour.print(e);
        }
    }
}

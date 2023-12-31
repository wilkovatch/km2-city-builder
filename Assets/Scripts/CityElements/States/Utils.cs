using System.Collections.Generic;
using UnityEngine;

namespace States {
    public static class ListSerializer<T> {
        public static List<T> GetRuntimeList(T[] input) {
            var res = new List<T>();
            foreach (var v in input) {
                res.Add(v);
            }
            return res;
        }
        public static T[] GetSerializableList(List<T> input) {
            var res = new T[input.Count];
            for (int i = 0; i < input.Count; i++) {
                res[i] = input[i];
            }
            return res;
        }
    }

    [System.Serializable]
    public struct SerializableVector3 {
        public SerializableVector3(Vector3 v) {
            x = v.x;
            y = v.y;
            z = v.z;
        }
        public Vector3 GetVector() {
            return new Vector3(x, y, z);
        }
        public static List<Vector3> GetRuntimeList(SerializableVector3[] input) {
            var res = new List<Vector3>();
            foreach (var v in input) {
                res.Add(v.GetVector());
            }
            return res;
        }
        public static SerializableVector3[] GetSerializableList(List<Vector3> input) {
            var res = new SerializableVector3[input.Count];
            for (int i = 0; i < input.Count; i++) {
                res[i] = new SerializableVector3(input[i]);
            }
            return res;
        }
        public float x, y, z;
    }

    [System.Serializable]
    public struct SerializableVector2 {
        public SerializableVector2(Vector2 v) {
            x = v.x;
            y = v.y;
        }
        public Vector2 GetVector() {
            return new Vector2(x, y);
        }
        public static List<Vector2> GetRuntimeList(SerializableVector2[] input) {
            var res = new List<Vector2>();
            foreach (var v in input) {
                res.Add(v.GetVector());
            }
            return res;
        }
        public static SerializableVector2[] GetSerializableList(List<Vector2> input) {
            var res = new SerializableVector2[input.Count];
            for (int i = 0; i < input.Count; i++) {
                res[i] = new SerializableVector2(input[i]);
            }
            return res;
        }
        public float x, y;
    }

    [System.Serializable]
    public struct SerializableQuaternion {
        public SerializableQuaternion(Quaternion q) {
            x = q.x;
            y = q.y;
            z = q.z;
            w = q.w;
        }
        public Quaternion GetQuaternion() {
            return new Quaternion(x, y, z, w);
        }
        public float x, y, z, w;
    }

    public static class Utils {
        public static T[] CloneArray<T>(T[] orig) {
            if (orig == null) return null;
            var res = new T[orig.Length];
            for (int i = 0; i < orig.Length; i++) {
                if (orig[i] is System.ICloneable e) {
                    res[i] = (T)e.Clone();
                } else {
                    res[i] = orig[i];
                }
            }
            return res;
        }

        public static bool ArrayEquals<T>(T[] a, T[] b) {
            if (a == null ^ b == null) return false;
            if (a == null && b == null) return true;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) {
                if (!a[i].Equals(b[i])) return false;
            }
            return true;
        }

        public static new bool Equals(object a, object b) {
            if (a == null ^ b == null) return false;
            if (a == null && b == null) return true;
            return a.Equals(b);
        }

        public static object DeJsonify(object input) {
            if (input is Newtonsoft.Json.Linq.JArray jarr) {
                var newArr = new object[jarr.Count];
                int i = 0;
                foreach (var elem in jarr.Children()) {
                    newArr[i++] = DeJsonify(elem);
                }
                return newArr;
            } else if (input is Newtonsoft.Json.Linq.JObject obj) {
                var newDict = new Dictionary<string, object>();
                foreach (var p in obj.Properties()) {
                    newDict.Add(p.Name, DeJsonify(p.Value));
                }
                return newDict;
            } else if (input is Newtonsoft.Json.Linq.JValue val) {
                return val.Value;
            } else {
                return input;
            }
        }

        static string GetEncodedFloatsSingle(float[] a) {
            var data = new byte[a.Length * 4];
            System.Buffer.BlockCopy(a, 0, data, 0, a.Length * 4);
            return System.Convert.ToBase64String(data);
        }

        static string GetEncodedVector3(Vector3 elem) {
            var tmpList = new float[3] { elem.x, elem.y, elem.z };
            return GetEncodedFloatsSingle(tmpList);
        }

        static string GetEncodedVector2(Vector3 elem) {
            var tmpList = new float[2] { elem.x, elem.y };
            return GetEncodedFloatsSingle(tmpList);
        }

        public static ObjectState GetFieldsAsState(object obj) {
            var type = obj.GetType();
            var fields = type.GetFields();
            var res = new ObjectState();
            foreach (var field in fields) {
                var val = field.GetValue(obj);
                if (val is int intVal) {
                    res.SetInt(field.Name, intVal);
                } else if (val is string strVal) {
                    res.SetStr(field.Name, strVal);
                } else if (val is float floatVal) {
                    res.SetFloat(field.Name, floatVal);
                } else if (val is bool boolVal) {
                    res.SetBool(field.Name, boolVal);
                } else if (val is ObjectState stateVal) {
                    res.SetState(field.Name, stateVal);
                } else if (val is Vector3 vec3Val) {
                    res.SetStr("b64v3_" + field.Name, GetEncodedVector3(vec3Val));
                } else if (val is Vector2 vec2Val) {
                    res.SetStr("b64v2_" + field.Name, GetEncodedVector2(vec2Val));
                }
            }
            return res;
        }
    }
}

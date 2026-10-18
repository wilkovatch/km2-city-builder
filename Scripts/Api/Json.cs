using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Api {
    //The JSON conversions of the API: vectors are [x, y, z] arrays (or {"x", "y", "z"} objects when read), in the
    //program coordinates; quaternions are [x, y, z, w]; states are plain objects
    public static class Json {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings {
            Converters = new List<JsonConverter> { new Vector3Converter(), new Vector2Converter(), new QuaternionConverter(), new ObjectStateConverter() },
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Error,
            FloatParseHandling = FloatParseHandling.Double
        };

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static JToken ToToken(object value) {
            return value == null ? JValue.CreateNull() : JToken.FromObject(value, Serializer);
        }

        public static T ToObject<T>(JToken token) {
            return token == null || token.Type == JTokenType.Null ? default : token.ToObject<T>(Serializer);
        }

        public static object ToObject(JToken token, System.Type type) {
            return token == null || token.Type == JTokenType.Null ? null : token.ToObject(type, Serializer);
        }

        static float[] ReadFloats(JToken token, int count, string what) {
            var res = new float[count];
            if (token is JArray arr) {
                if (arr.Count != count) throw ApiException.InvalidValue(what + " needs " + count + " numbers");
                for (int i = 0; i < count; i++) res[i] = arr[i].Value<float>();
            } else if (token is JObject obj) {
                var names = new[] { "x", "y", "z", "w" };
                for (int i = 0; i < count; i++) {
                    var v = obj[names[i]];
                    if (v == null) throw ApiException.InvalidValue(what + " needs the field " + names[i]);
                    res[i] = v.Value<float>();
                }
            } else {
                throw ApiException.InvalidValue("invalid " + what);
            }
            return res;
        }

        public static Vector3 ReadVector3(JToken token) {
            var f = ReadFloats(token, 3, "vector");
            return new Vector3(f[0], f[1], f[2]);
        }

        //a converter of a value type, also used for its nullable version
        abstract class ValueConverter<T> : JsonConverter where T : struct {
            public override bool CanConvert(System.Type objectType) {
                return objectType == typeof(T) || objectType == typeof(T?);
            }

            public override object ReadJson(JsonReader reader, System.Type objectType, object existingValue, JsonSerializer serializer) {
                var token = JToken.Load(reader);
                if (token.Type == JTokenType.Null) {
                    if (objectType == typeof(T?)) return null;
                    throw ApiException.InvalidValue("missing value");
                }
                return Read(token);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
                if (value == null) {
                    writer.WriteNull();
                    return;
                }
                writer.WriteStartArray();
                foreach (var f in Write((T)value)) writer.WriteValue(f);
                writer.WriteEndArray();
            }

            protected abstract T Read(JToken token);
            protected abstract float[] Write(T value);
        }

        class Vector3Converter : ValueConverter<Vector3> {
            protected override Vector3 Read(JToken token) => ReadVector3(token);
            protected override float[] Write(Vector3 v) => new[] { v.X, v.Y, v.Z };
        }

        class Vector2Converter : ValueConverter<Vector2> {
            protected override Vector2 Read(JToken token) {
                var f = ReadFloats(token, 2, "vector");
                return new Vector2(f[0], f[1]);
            }

            protected override float[] Write(Vector2 v) => new[] { v.X, v.Y };
        }

        class QuaternionConverter : ValueConverter<Quaternion> {
            protected override Quaternion Read(JToken token) {
                var f = ReadFloats(token, 4, "quaternion");
                return new Quaternion(f[0], f[1], f[2], f[3]);
            }

            protected override float[] Write(Quaternion q) => new[] { q.X, q.Y, q.Z, q.W };
        }

        //the same JSON as in the saved cities (the properties as a plain object)
        class ObjectStateConverter : JsonConverter<ObjectState> {
            public override ObjectState ReadJson(JsonReader reader, System.Type objectType, ObjectState existingValue, bool hasExistingValue, JsonSerializer serializer) {
                var token = JToken.Load(reader);
                if (token.Type == JTokenType.Null) return null;
                if (token.Type != JTokenType.Object) throw ApiException.InvalidValue("a state must be an object");
                return JsonConvert.DeserializeObject<ObjectState>(token.ToString(Formatting.None));
            }

            public override void WriteJson(JsonWriter writer, ObjectState value, JsonSerializer serializer) {
                if (value == null) {
                    writer.WriteNull();
                    return;
                }
                JToken.Parse(JsonConvert.SerializeObject(value, Formatting.None)).WriteTo(writer);
            }
        }
    }
}

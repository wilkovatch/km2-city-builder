using Godot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Api {
    //A reference to an element or to a part of one. In JSON: the id of an element (an integer), its name (a string),
    //or an object with one of these forms:
    //  {"id": 12} / {"name": "Road 3", "kind": "road"}                        an element
    //  {"line": <ref>, "building": 0}                                         a building of a building line
    //  {"line": <ref>, "building": 0, "side": "front"}                        a side of a building
    //  {"line": <ref>, "building": 0, "side": "front", "block": 1}            a block of the dictionary of a side
    //  {"patch": <ref>, "borderMesh": 0}                                      a border mesh of a terrain patch
    //  {"road": <ref>, "container": "right"}                                  a props container of the prop rule of a road
    //  {"road": <ref>, "container": "right", "list": "props", "index": 0}     an element of a props container (no index:
    //                                                                         the single element of the list)
    //  {"parent": <ref>, "param": "races", "index": 0}                        an element of an array parameter (no index:
    //                                                                         a custom city properties parameter)
    //  {"owner": <ref>, "object": "trafficLight", "path": ["races.0"]}        a parametric object (objectInstance parameter),
    //                                                                         path: the array elements leading to it
    [JsonConverter(typeof(ElementRefConverter))]
    public class ElementRef {
        public int? id;
        public string name;
        public string kind;
        public ElementRef line;
        public int? building;
        public string side;
        public int? block;
        public ElementRef patch;
        public int? borderMesh;
        public ElementRef road;
        public string container;
        public string list;
        public int? index;
        public ElementRef parent;
        public string param;
        public ElementRef owner;
        [JsonProperty("object")] public string objectKey;
        public string[] path;

        public static ElementRef Id(int id) {
            return new ElementRef { id = id };
        }

        public override string ToString() {
            return JsonConvert.SerializeObject(this);
        }
    }

    class ElementRefConverter : JsonConverter<ElementRef> {
        public override bool CanWrite => true;

        public override ElementRef ReadJson(JsonReader reader, System.Type objectType, ElementRef existingValue, bool hasExistingValue, JsonSerializer serializer) {
            var token = JToken.Load(reader);
            return Read(token);
        }

        public static ElementRef Read(JToken token) {
            switch (token.Type) {
                case JTokenType.Null:
                    return null;
                case JTokenType.Integer:
                    return new ElementRef { id = token.Value<int>() };
                case JTokenType.String:
                    return new ElementRef { name = token.Value<string>() };
                case JTokenType.Object:
                    var obj = (JObject)token;
                    var res = new ElementRef();
                    foreach (var prop in obj.Properties()) {
                        var v = prop.Value;
                        switch (prop.Name) {
                            case "id": res.id = v.Value<int?>(); break;
                            case "name": res.name = v.Value<string>(); break;
                            case "kind": res.kind = v.Value<string>(); break;
                            case "line": res.line = Read(v); break;
                            case "building": res.building = v.Value<int?>(); break;
                            case "side": res.side = v.Value<string>(); break;
                            case "block": res.block = v.Value<int?>(); break;
                            case "patch": res.patch = Read(v); break;
                            case "borderMesh": res.borderMesh = v.Value<int?>(); break;
                            case "road": res.road = Read(v); break;
                            case "container": res.container = v.Value<string>(); break;
                            case "list": res.list = v.Value<string>(); break;
                            case "index": res.index = v.Value<int?>(); break;
                            case "parent": res.parent = Read(v); break;
                            case "param": res.param = v.Value<string>(); break;
                            case "owner": res.owner = Read(v); break;
                            case "object": res.objectKey = v.Value<string>(); break;
                            case "path": res.path = v.ToObject<string[]>(); break;
                            default: throw ApiException.InvalidParam("unknown reference field '" + prop.Name + "'");
                        }
                    }
                    return res;
                default:
                    throw ApiException.InvalidParam("invalid reference: " + token.ToString(Formatting.None));
            }
        }

        public override void WriteJson(JsonWriter writer, ElementRef value, JsonSerializer serializer) {
            var obj = new JObject();
            void Add(string key, object v) {
                if (v != null) obj[key] = v is ElementRef r ? Write(r) : JToken.FromObject(v);
            }
            JToken Write(ElementRef r) {
                if (r.id.HasValue && r.name == null && r.kind == null) return new JValue(r.id.Value);
                var w = new JTokenWriter();
                WriteJson(w, r, serializer);
                return w.Token;
            }
            Add("id", value.id);
            Add("name", value.name);
            Add("kind", value.kind);
            Add("line", value.line);
            Add("building", value.building);
            Add("side", value.side);
            Add("block", value.block);
            Add("patch", value.patch);
            Add("borderMesh", value.borderMesh);
            Add("road", value.road);
            Add("container", value.container);
            Add("list", value.list);
            Add("index", value.index);
            Add("parent", value.parent);
            Add("param", value.param);
            Add("owner", value.owner);
            Add("object", value.objectKey);
            Add("path", value.path);
            obj.WriteTo(writer);
        }
    }

    //Where a point is placed: what the click of the editor would have hit. Exactly one of the forms:
    //  {"position": [x, y, z], "onGround": true}          a point on the terrain (projected on the ground if onGround)
    //  {"terrainPoint": id}                               an existing terrain point
    //  {"anchor": {"of": <road or intersection>, "index": i}}   an anchor along a road or intersection edge
    //  {"link": [pointA, pointB], "position": [x, y, z]}  a point on the segment between two terrain points
    //  {"anchorLink": {"of": <ref>, "index": i}, "position": [x, y, z]}  a point on the segment from an anchor to the
    //                                                     next one (no position: its middle)
    //  {"roadEnd": {"road": <ref>, "end": "start"|"end"}} the free end of a road (connections only)
    //  {"intersection": <ref>}                            an intersection (connections only)
    //  {"roadMiddle": {"road": <ref>, "near": [x, y, z]}} the middle of a road, which is split (connections only)
    public class PointTarget {
        public Vector3? position;
        public bool onGround = false;
        public int? terrainPoint;
        public AnchorTarget anchor;
        public int[] link;
        public AnchorTarget anchorLink;
        public RoadEndTarget roadEnd;
        public ElementRef intersection;
        public RoadMiddleTarget roadMiddle;

        public class AnchorTarget {
            [JsonProperty("of")] public ElementRef owner;
            public int index;
        }

        public class RoadEndTarget {
            public ElementRef road;
            public string end = "end";
        }

        public class RoadMiddleTarget {
            public ElementRef road;
            public Vector3 near;
        }

        public int FormCount() {
            var n = 0;
            if (terrainPoint.HasValue) n++;
            if (anchor != null) n++;
            if (link != null) n++;
            if (anchorLink != null) n++;
            if (roadEnd != null) n++;
            if (intersection != null) n++;
            if (roadMiddle != null) n++;
            if (position.HasValue && link == null && anchorLink == null) n++;
            return n;
        }
    }

    public class Transform {
        public Vector3? position;
        public Vector3? rotation; //euler angles in degrees
        public Quaternion? quaternion;
        public Vector3? scale;
    }
}

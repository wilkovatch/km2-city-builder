using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Lua;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RuntimeCalculator.Scripting;
using Settings = CityElements.Types.RoadSettings;

//Core curves are shared by roads and low-level splines. Numeric IDs are persistent, never dropdown indices.
public static class SplineCurves {
    public class Definition {
        public int id;
        public string name, label, prepare, evaluate, controlParameters, interpolateValues, split, merge;
    }

    static Dictionary<int, Definition> definitions;
    public static void Reset() { definitions = null; }
    public static Dictionary<int, Definition> Definitions {
        get {
            if (definitions != null) return definitions;
            var result = new Dictionary<int, Definition>();
            var core = PreferencesManager.Get("core", "");
            var file = Path.Combine(PathHelper.BasePath(), "Files", "cores", core, "splineTypes.json");
            if (core != "" && File.Exists(file)) {
                var names = new HashSet<string>(StringComparer.Ordinal) { "bezier", "hermite", "lowPoly" };
                foreach (var d in JsonConvert.DeserializeObject<Definition[]>(File.ReadAllText(file)) ?? Array.Empty<Definition>()) {
                    if (d == null || d.id < 3 || result.ContainsKey(d.id) || string.IsNullOrWhiteSpace(d.name) || !names.Add(d.name)
                        || string.IsNullOrWhiteSpace(d.label) || string.IsNullOrWhiteSpace(d.evaluate) || string.IsNullOrWhiteSpace(d.controlParameters)) {
                        throw new ArgumentException("Invalid custom spline definition: " + d?.name);
                    }
                    var host = LuaScriptHost.Get();
                    foreach (var script in new[] { d.prepare, d.evaluate, d.controlParameters, d.interpolateValues, d.split, d.merge }) {
                        if (script != null) host.Resolve(script);
                    }
                    result.Add(d.id, d);
                }
            }
            return definitions = result;
        }
    }

    public static int[] Allowed(Settings settings) => settings?.curveTypes ?? new[] { 0, 1, 2 };
    public static void ValidateSettings(Settings settings) {
        var allowed = Allowed(settings);
        if (allowed.Length == 0 || allowed.Distinct().Count() != allowed.Length ||
            allowed.Any(i => i < 0 || (i > 2 && !Definitions.ContainsKey(i))) ||
            !allowed.Contains(settings.defaultCurveType)) {
            throw new ArgumentException("Invalid allowed spline types");
        }
    }
    public static void ValidateState(Settings settings, ObjectState state) {
        if (state.properties.TryGetValue("curveType", out var value) && (value is not int id || !Allowed(settings).Contains(id))) {
            throw new ArgumentException("curve type is not allowed by this element type");
        }
    }
    public static string Name(int id) => id < 3 ? Operations.Builtins.CurveTypes[id] : Definitions[id].name;
    public static string Label(int id) => id < 3 ? new[] { "RD_SP_T_BEZIER", "RD_SP_T_HERMITE", "RD_SP_T_LOWPOLY" }[id] : Definitions[id].label;
    public static int Parse(Settings settings, JToken value) {
        var allowed = Allowed(settings);
        var id = value.Type == JTokenType.Integer ? value.Value<int>() : allowed.FirstOrDefault(i => Name(i) == value.Value<string>(), -1);
        if (!allowed.Contains(id)) throw new ArgumentException("curve type is not allowed by this element type");
        return id;
    }

    internal static LuaValue ToLua(JToken token) {
        if (token == null || token.Type == JTokenType.Null) return LuaValue.Nil;
        if (token is JObject obj) {
            var table = new LuaTable();
            foreach (var p in obj.Properties()) table[p.Name] = ToLua(p.Value);
            return table;
        }
        if (token is JArray arr) {
            var table = new LuaTable();
            for (int i = 0; i < arr.Count; i++) table[i + 1] = ToLua(arr[i]);
            return table;
        }
        if (token.Type == JTokenType.Boolean) return new LuaValue(token.Value<bool>());
        if (token.Type == JTokenType.String) return new LuaValue(token.Value<string>());
        return new LuaValue(token.Value<double>());
    }
    public static LuaTable Table(LuaValue value, string what) => value.TryRead<LuaTable>(out var table) ? table : throw new ArgumentException(what + " must return a table");
    public static Vector3 Vector(LuaValue value) {
        if (!value.TryRead<LuaVec3>(out var v) || !Finite(v.value)) throw new ArgumentException("custom spline returned an invalid vec3");
        return v.value;
    }
    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    public static LuaTable Vectors(IEnumerable<Vector3> points) {
        var table = new LuaTable(); int i = 0;
        foreach (var p in points) { if (!Finite(p)) throw new ArgumentException("non-finite spline point"); table[++i] = LuaVectors.ToLua(p); }
        return table;
    }

    //One prepared detached input per rebuild. No scene nodes, mutable live state or engine objects enter Lua.
    public sealed class Curve {
        public readonly Definition definition;
        public readonly List<Vector3> points;
        public readonly List<float> controlParameters;
        readonly LuaScriptHost host;
        readonly LuaTable context;
        readonly LuaValue variables;
        readonly Dictionary<float, (Vector3 position, Vector3? tangent)> evaluated = new();

        public Curve(Settings settings, ObjectState state, List<Vector3> points, Vector3? start = null, Vector3? end = null) {
            ValidateState(settings, state);
            definition = Definitions[state.Int("curveType", settings.defaultCurveType)];
            this.points = new List<Vector3>(points);
            host = LuaScriptHost.Get();
            variables = ToLua(JObject.FromObject(state));
            context = new LuaTable();
            context["points"] = Vectors(points);
            context["startTangent"] = start.HasValue ? LuaVectors.ToLua(start.Value) : LuaValue.Nil;
            context["endTangent"] = end.HasValue ? LuaVectors.ToLua(end.Value) : LuaValue.Nil;
            context["state"] = variables;
            if (definition.prepare != null) context["data"] = Call(definition.prepare);
            var knots = Table(Call(definition.controlParameters), "controlParameters");
            controlParameters = new List<float>();
            for (int i = 1; i <= knots.ArrayLength; i++) {
                if (!knots[i].TryRead<double>(out var k) || !double.IsFinite(k) || k < 0 || k > 1 ||
                    (i > 1 && k < controlParameters[^1])) throw new ArgumentException("invalid custom spline control parameters");
                controlParameters.Add((float)k);
            }
            if (controlParameters.Count != points.Count || (points.Count > 1 && (controlParameters[0] != 0 || controlParameters[^1] != 1))) {
                throw new ArgumentException("custom spline control parameters must match its points and span 0..1");
            }
        }

        public LuaValue Call(string script, LuaTable input = null) {
            var result = host.Call(host.Resolve(script), variables, input ?? context, definition.name);
            if (result.Type == LuaValueType.Nil) throw new ArgumentException("custom spline callback failed or declined: " + script);
            return result;
        }
        (Vector3 position, Vector3? tangent) Evaluate(float t) {
            t = Math.Clamp(t, 0, 1);
            if (evaluated.TryGetValue(t, out var cached)) return cached;
            context["t"] = t;
            var result = Table(Call(definition.evaluate), "evaluate");
            var position = Vector(result["position"]);
            Vector3? tangent = result["tangent"].Type == LuaValueType.Nil ? null : Vector(result["tangent"]);
            return evaluated[t] = (position, tangent);
        }
        public Vector3 Position(float t) => Evaluate(t).position;
        public Vector3 Tangent(float t) => Evaluate(t).tangent ?? (Position(Math.Min(1, t + 0.0001f)) - Position(Math.Max(0, t - 0.0001f)));
        public Vector2 Interpolate(List<Vector2> values, float t) {
            if (values.Count != points.Count) throw new ArgumentException("custom spline attributes must match its controls");
            if (definition.interpolateValues != null) {
                var table = new LuaTable(); for (int i = 0; i < values.Count; i++) table[i + 1] = LuaVectors.ToLua(values[i]);
                context["values"] = table; context["t"] = t;
                var value = Call(definition.interpolateValues);
                if (!value.TryRead<LuaVec2>(out var v) || !float.IsFinite(v.value.X) || !float.IsFinite(v.value.Y)) throw new ArgumentException("invalid custom spline attribute");
                return v.value;
            }
            for (int i = 1; i < values.Count; i++) {
                if (t <= controlParameters[i] || i == values.Count - 1) {
                    var width = controlParameters[i] - controlParameters[i - 1];
                    return values[i - 1].Lerp(values[i], width > 0 ? Math.Clamp((t - controlParameters[i - 1]) / width, 0, 1) : 0);
                }
            }
            return values.Count > 0 ? values[0] : Vector2.Zero;
        }

        public (List<Vector3> points, List<float> parameters) Sample(int segments, float tolerance = 0, int maxDepth = 8) {
            var positions = new List<Vector3>(); var parameters = new List<float>();
            if (points.Count < 2) return (positions, parameters);
            void Add(float t) { parameters.Add(t); positions.Add(Position(t)); }
            void Span(float a, float b, int depth) {
                var p = Position(a); var q = Position(b);
                bool split = false;
                foreach (var f in new[] { 0.25f, 0.5f, 0.75f }) {
                    if (Position(Mathf.Lerp(a, b, f)).DistanceTo(GeometryHelper.ClosestPoint(Position(Mathf.Lerp(a, b, f)), p, q)) > tolerance) split = true;
                }
                if (split && depth < Math.Clamp(maxDepth, 0, 8)) { var m = (a + b) * 0.5f; Span(a, m, depth + 1); Span(m, b, depth + 1); }
                else Add(b);
            }
            Add(0);
            if (tolerance > 0) {
                for (int i = 1; i < controlParameters.Count; i++) if (controlParameters[i] > controlParameters[i - 1]) Span(controlParameters[i - 1], controlParameters[i], 0);
            } else {
                var samples = new SortedSet<float>(controlParameters);
                for (int i = 1; i <= Math.Clamp(segments, 1, 10000); i++) samples.Add(i / (float)Math.Clamp(segments, 1, 10000));
                foreach (var t in samples) if (t > 0) Add(t);
            }
            return (positions, parameters);
        }
    }

    public static Curve Create(Settings settings, ObjectState state, List<Vector3> points, Vector3? start = null, Vector3? end = null) {
        ValidateState(settings, state);
        return state.Int("curveType", settings?.defaultCurveType ?? 0) > 2 ? new Curve(settings, state, points, start, end) : null;
    }
}

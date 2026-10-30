using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lua;
using Newtonsoft.Json.Linq;
using RuntimeCalculator.Scripting;
using GO = GameEngine.GameObject;

//Callbacks plan topology changes on detached inputs. Nothing is applied until every result is validated.
public static class SplineCurveOperations {
    public class Controls {
        public List<Vector3> points = new();
        public List<Vector2> rotations = new();
        public List<StaticBody3D> sources = new();
        public ObjectState state;
    }

    public static LuaTable Input(Road road) {
        var t = new LuaTable();
        t["points"] = SplineCurves.Vectors(road.points.Select(GO.GetPosition));
        var rotations = new LuaTable(); var values = road.GetPointRotations();
        for (int i = 0; i < values.Count; i++) rotations[i+1] = LuaVectors.ToLua(values[i]);
        t["rotations"] = rotations;
        t["state"] = SplineCurves.ToLua(JObject.FromObject(road.state));
        var tangents = road.GetEndTangents();
        t["startTangent"] = tangents.start.HasValue ? LuaVectors.ToLua(tangents.start.Value) : LuaValue.Nil;
        t["endTangent"] = tangents.end.HasValue ? LuaVectors.ToLua(tangents.end.Value) : LuaValue.Nil;
        t["startIntersection"] = road.startIntersection != null;
        t["endIntersection"] = road.endIntersection != null;
        t["startClearance"] = Clearance(road, road.startIntersection);
        t["endClearance"] = Clearance(road, road.endIntersection);
        return t;
    }
    static float Clearance(Road r, Intersection i) {
        var index = i?.roads.IndexOf(r) ?? -1;
        return index >= 0 && index < i.sizes.Count ? i.sizes[index] : 0;
    }
    static SplineCurves.Curve Curve(Road road) {
        var tangents = road.GetEndTangents();
        return SplineCurves.Create(road.GetRoadType().typeData.settings, road.state, road.points.Select(GO.GetPosition).ToList(), tangents.start, tangents.end);
    }
    static JToken Json(LuaValue value, int depth = 0) {
        if (depth > 32) throw new ArgumentException("custom curve state is too deeply nested");
        if (value.Type == LuaValueType.Nil) return JValue.CreateNull();
        if (value.TryRead<LuaTable>(out var table)) {
            if (table.ArrayLength > 0) {
                var a = new JArray(); for (int i = 1; i <= table.ArrayLength; i++) a.Add(Json(table[i], depth+1)); return a;
            }
            var obj = new JObject();
            foreach (var entry in table) {
                if (!entry.Key.TryRead<string>(out var key)) throw new ArgumentException("custom curve state keys must be strings");
                obj[key] = Json(entry.Value, depth+1);
            }
            return obj;
        }
        if (value.TryRead<bool>(out var b)) return new JValue(b);
        if (value.TryRead<string>(out var s)) return new JValue(s);
        if (value.TryRead<double>(out var n) && double.IsFinite(n)) return new JValue(n);
        throw new ArgumentException("custom curve state must contain JSON values");
    }
    static Controls Read(LuaValue result, Road keep, Road[] sourceRoads) {
        var table = SplineCurves.Table(result, "curve operation");
        var points = SplineCurves.Table(table["points"], "curve operation points");
        var rotations = SplineCurves.Table(table["rotations"], "curve operation rotations");
        if (points.ArrayLength < 2 || points.ArrayLength > 10000 || rotations.ArrayLength != points.ArrayLength) throw new ArgumentException("invalid custom curve operation point count");
        var output = new Controls { state = (ObjectState)keep.state.Clone() };
        var sources = table["sources"].TryRead<LuaTable>(out var src) ? src : null;
        var used = new HashSet<StaticBody3D>();
        for (int i = 1; i <= points.ArrayLength; i++) {
            output.points.Add(SplineCurves.Vector(points[i]));
            if (!rotations[i].TryRead<LuaVec2>(out var r) || !float.IsFinite(r.value.X) || !float.IsFinite(r.value.Y)) throw new ArgumentException("invalid custom curve rotation");
            output.rotations.Add(r.value);
            StaticBody3D source = null;
            if (sources != null && sources[i].Type != LuaValueType.Nil && sources[i].Type != LuaValueType.Boolean) {
                var mapping = SplineCurves.Table(sources[i], "point source");
                if (!mapping["road"].TryRead<double>(out var ri) || ri != Math.Truncate(ri) || ri < 1 || ri > sourceRoads.Length ||
                    !mapping["index"].TryRead<double>(out var pi) || pi != Math.Truncate(pi) || pi < 1 || pi > sourceRoads[(int)ri-1].points.Count) throw new ArgumentException("invalid custom curve point source");
                source = sourceRoads[(int)ri-1].points[(int)pi-1];
                if (!used.Add(source) || GO.GetPosition(source).DistanceTo(output.points[^1]) > 0.0001f) throw new ArgumentException("a retained point must keep its position and occur once");
            }
            output.sources.Add(source);
        }
        if (table["state"].Type != LuaValueType.Nil) {
            var patch = Json(table["state"]) as JObject ?? throw new ArgumentException("curve state must be an object");
            var parameters = keep.GetRoadType().parameterContainer.parameters;
            foreach (var p in patch.Properties()) {
                if (p.Name == "type" || p.Name == "curveType" || !parameters.Any(x => x.fullName() == p.Name)) throw new ArgumentException("curve operation cannot change property " + p.Name);
            }
            var updates = patch.ToObject<ObjectState>();
            Operations.States.Normalize(updates, parameters);
            foreach (var p in updates.properties) output.state.SetProperty(p.Key, p.Value);
        }
        SplineCurves.ValidateState(keep.GetRoadType().typeData.settings, output.state);
        var prepared = SplineCurves.Create(keep.GetRoadType().typeData.settings, output.state, output.points);
        foreach (var t in new[] { 0f, .25f, .5f, .75f, 1f }) { prepared.Position(t); prepared.Interpolate(output.rotations, t); }
        return output;
    }

    public static (Controls first, Controls second) Split(Road road, float parameter) {
        var curve = Curve(road);
        if (curve?.definition.split == null) throw new InvalidOperationException("this custom curve does not support road splitting");
        var context = new LuaTable(); context["road"] = Input(road); context["t"] = parameter;
        var result = SplineCurves.Table(curve.Call(curve.definition.split, context), "split");
        var first = Read(result["first"], road, new[] { road }); var second = Read(result["second"], road, new[] { road });
        if (first.points[^1].DistanceTo(second.points[0]) > .0001f || first.points[0].DistanceTo(GO.GetPosition(road.points[0])) > .0001f ||
            second.points[^1].DistanceTo(GO.GetPosition(road.points[^1])) > .0001f) throw new ArgumentException("custom split changed the ends or disconnected the parts");
        var both = first.sources.Concat(second.sources).Where(p => p != null).ToArray();
        if (both.Distinct().Count() != both.Length) throw new ArgumentException("split parts cannot share a retained road handle");
        return (first, second);
    }
    public static Controls Merge(Road keep, Road other, bool keepAtEnd, bool otherAtStart, Vector3 joint) {
        var curve = Curve(keep);
        if (curve?.definition.merge == null) throw new InvalidOperationException("this custom curve does not support road merging");
        var context = new LuaTable(); context["first"] = Input(keep); context["second"] = Input(other);
        context["keepAtEnd"] = keepAtEnd; context["otherAtStart"] = otherAtStart;
        context["joint"] = LuaVectors.ToLua(joint);
        var output = Read(curve.Call(curve.definition.merge, context), keep, new[] { keep, other });
        var firstEnd = GO.GetPosition((keepAtEnd ? keep : other).points[keepAtEnd ? 0 : (otherAtStart ? other.points.Count-1 : 0)]);
        var lastEnd = GO.GetPosition((keepAtEnd ? other : keep).points[keepAtEnd ? (otherAtStart ? other.points.Count-1 : 0) : keep.points.Count-1]);
        if (output.points[0].DistanceTo(firstEnd) > .0001f || output.points[^1].DistanceTo(lastEnd) > .0001f) throw new ArgumentException("custom merge changed the far ends");
        return output;
    }

    public static void Apply((Road road, Controls controls)[] outputs, IEnumerable<StaticBody3D> original) {
        var retained = outputs.SelectMany(o => o.controls.sources).Where(p => p != null).ToHashSet();
        foreach (var (road, controls) in outputs) {
            road.state.ReplaceWith(controls.state);
            road.points.Clear();
            for (int i = 0; i < controls.points.Count; i++) {
                var p = controls.sources[i];
                if (p == null) p = (StaticBody3D)GameEngine.Resources.InstantiateObject("Handle");
                GO.SetParent(p, road); GO.SetLayer(p, 8); road.points.Add(p);
            }
            road.RestorePoints(controls.points, controls.rotations);
            road.forceRemesh = true;
        }
        foreach (var point in original) if (!retained.Contains(point)) GO.Destroy(point);
    }
}

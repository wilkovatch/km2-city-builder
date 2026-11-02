using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CityElements.Types;
using CustomElements;
using Godot;
using Lua;
using RuntimeCalculator.Scripting;
using TP = CityElements.Types.Parsers.TypeParser;

//All scene and physics access stays on the main thread. Only the authored shape and settings are saved.
public static class PropPlacerGeometry {
    public sealed class Instance {
        public string container, mesh;
        public Vector3 position, rotation, scale = Vector3.One;
    }

    //Stable across runtime versions, independent of the application's shared expression random sequence.
    public sealed class RandomSequence {
        uint state;
        public RandomSequence(int seed) { state = unchecked((uint)seed); }
        public double Next() {
            state = unchecked(state * 1664525u + 1013904223u);
            return state / 4294967296.0;
        }
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PropPlacerStrip, Dictionary<string, CityElements.Types.Runtime.PropsElementType>> profiles = new();

    public static CityElements.Types.Runtime.PropsElementType ScopedType(PropPlacerStrip strip, string name) {
        if (name == null || !TP.GetPropsElementTypes().TryGetValue(name, out var original)) return null;
        if (strip == null || strip.placementRules == null) return original;
        var cache = profiles.GetOrCreateValue(strip);
        if (!cache.TryGetValue(name, out var result)) {
            result = new CityElements.Types.Runtime.PropsElementType(new PropsElementType { label = original.typeData.label,
                maxMeshes = original.typeData.maxMeshes, fixedMeshes = original.typeData.fixedMeshes, exportSlots = original.typeData.exportSlots,
                parametersInfo = strip.parametersInfo ?? original.typeData.parametersInfo,
                uiInfo = strip.uiInfo ?? original.typeData.uiInfo, placementRules = strip.placementRules }, name);
            cache[name] = result;
        }
        return result;
    }

    public static PropPlacerStrip Strip(ObjectState owner, string name) {
        return owner != null && TP.GetCustomElementTypes().TryGetValue(owner.Str("type", ""), out var type)
            ? type.typeData.settings.propPlacer?.strips?.FirstOrDefault(x => x.name == name) : null;
    }

    public static void Validate(ObjectState state, string typeName) {
        var settings = TP.GetCustomElementTypes()[typeName].typeData.settings.propPlacer;
        if (settings == null) return;
        if (settings.type == "polygon") {
            var models = state.Array<ObjectState>("models") ?? Array.Empty<ObjectState>();
            if (settings.maxModels > 0 && models.Length > settings.maxModels) throw new ArgumentException("Too many prop placer models");
            foreach (var model in models) {
                foreach (var key in new[] { "density", "minYaw", "maxYaw" }) {
                    var value = model.properties.TryGetValue(key, out var raw) && raw != null ? Convert.ToSingle(raw) : key == "maxYaw" ? 360 : key == "density" ? 1 : 0;
                    if (!float.IsFinite(value) || (key == "density" && value < 0)) throw new ArgumentException("Invalid prop model " + key);
                }
            }
            return;
        }
        var rule = state.State("propRule");
        if (rule == null) return;
        foreach (var pair in rule.properties) {
            if (pair.Value is not ObjectState container) continue;
            var strip = settings.strips.FirstOrDefault(x => x.name == pair.Key);
            if (strip == null) throw new ArgumentException("Unknown prop placer strip: " + pair.Key);
            var containerType = container.Str("type", null);
            if (containerType == null && strip.allowedContainerTypes?.Length > 0) container.SetStr("type", containerType = strip.allowedContainerTypes[0]);
            if (strip.allowedContainerTypes?.Length > 0 && !strip.allowedContainerTypes.Contains(containerType)) throw new ArgumentException("Container type is not allowed by strip " + pair.Key);
            var definition = Operations.Props.GetContainerType(container) ?? throw new ArgumentException("Unknown prop container");
            foreach (var slot in definition.parameters) {
                var elements = slot.maxNumber == 1 ? new[] { container.State(slot.name) } : container.Array<ObjectState>(slot.name);
                if (slot.maxNumber > 1 && elements?.Length > slot.maxNumber) throw new ArgumentException("Too many prop elements");
                foreach (var element in elements ?? Array.Empty<ObjectState>()) {
                    if (element == null) continue;
                    var id = element.Str("type", "");
                    if (!TP.GetPropsElementTypes().ContainsKey(id) || (slot.allowedTypes?.Length > 0 && !slot.allowedTypes.Contains(id))) throw new ArgumentException("Prop element type is not allowed");
                    foreach (var range in strip.ranges ?? new Dictionary<string, float[]>()) {
                        var value = element.properties.TryGetValue(range.Key, out var raw) ? Convert.ToSingle(raw) : 0;
                        if (range.Value.Length < 2 || range.Value.Length > 3 || !float.IsFinite(value) || value < range.Value[0] || value > range.Value[1]) throw new ArgumentException("Prop parameter out of range: " + range.Key);
                        if (range.Value.Length == 3) {
                            var step = range.Value[2];
                            if (!float.IsFinite(step) || step <= 0 || Math.Abs((value - range.Value[0]) / step - MathF.Round((value - range.Value[0]) / step)) > 0.0001f)
                                throw new ArgumentException("Prop parameter does not match its step: " + range.Key);
                        }
                        var parameter = ScopedType(strip, id).parameterContainer.parameters.FirstOrDefault(p => p.fullName() == range.Key);
                        if (parameter?.type == "int" && value != MathF.Truncate(value)) throw new ArgumentException("Prop parameter must be an integer: " + range.Key);
                    }
                    Operations.States.Normalize(element, ScopedType(strip, id).parameterContainer.parameters);
                }
            }
        }
    }

    public static Vector3 Project(CustomElement element, Vector3 position) {
        return GeometryHelper.AutoProjectOnSurface(position, CityGroundHelper.ExcludeHiddenTerrain(element.Settings.propPlacer.projectionMask), element.generator);
    }

    public static GeometryResult Build(CustomElement element) {
        var settings = element.Settings.propPlacer;
        Validate(element.state, element.TypeName);
        element.propPlacementError = null;
        var geometry = settings.type == "spline" ? Spline(element) : Polygon(element);
        foreach (var line in geometry.lines) line.heightOffset += settings.guideHeightOffset;
        return geometry;
    }

    static GeometryResult Spline(CustomElement element) {
        var geometry = CustomElementSplineGeometry.Build(element, out element.splineData);
        var sections = element.splineData.sections;
        if (sections == null || sections.Length < 2) return geometry;
        var type = element.GetCustomElementType().splineType;
        var variables = type.variableContainer.GetClone();
        type.FillInitialVariables(variables, element.state, null, element.cityPropertiesState, sections[^1].distance, sections.Length);
        foreach (var strip in element.Settings.propPlacer.strips) {
            var start = RuntimeCalculator.Parsers.FloatParser.ParseExpression(strip.start);
            var end = RuntimeCalculator.Parsers.FloatParser.ParseExpression(strip.end);
            start.SetIndices(variables); end.SetIndices(variables);
            var a = start.GetValue(variables); var b = end.GetValue(variables);
            if (!float.IsFinite(a) || !float.IsFinite(b)) throw new ArgumentException("Non-finite prop strip bounds");
            var line = new GeometryPropLine { container = strip.name };
            for (int i = 0; i < sections.Length; i++) {
                var previous = sections[Math.Max(0, i - 1)].Position;
                var next = sections[Math.Min(sections.Length - 1, i + 1)].Position;
                var tangent = next - previous; tangent.Y = 0;
                var right = tangent.LengthSquared() > 1e-8f ? tangent.Normalized().Cross(Vector3.Up) : Vector3.Right;
                line.startBound.Add(sections[i].Position + right * a);
                line.endBound.Add(sections[i].Position + right * b);
            }
            geometry.propLines.Add(line);
            if (Math.Abs(a - b) > 1e-5f) {
                geometry.lines.Add(new GeometryLine { points = line.startBound, always = true, color = Colors.Cyan });
                geometry.lines.Add(new GeometryLine { points = line.endBound, always = true, color = Colors.Cyan });
            }
        }
        return geometry;
    }

    static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    public static int[] Triangulate(IReadOnlyList<Vector3> points) {
        var flat = points.Select(p => new Vector2(p.X, p.Z)).ToArray();
        bool Between(float a, float b, float c) => c >= Math.Min(a, b) - 1e-6f && c <= Math.Max(a, b) + 1e-6f;
        bool On(Vector2 a, Vector2 b, Vector2 p) => Math.Abs(Cross(a, b, p)) < 1e-6f && Between(a.X, b.X, p.X) && Between(a.Y, b.Y, p.Y);
        for (int i = 0; i < flat.Length; i++) {
            if (!points[i].IsFinite() || flat[i].DistanceSquaredTo(flat[(i + 1) % flat.Length]) < 1e-10f) return Array.Empty<int>();
            for (int j = i + 1; j < flat.Length; j++) {
                if (j == i + 1 || (i == 0 && j == flat.Length - 1)) continue;
                var a = flat[i]; var b = flat[(i + 1) % flat.Length]; var c = flat[j]; var d = flat[(j + 1) % flat.Length];
                if ((Cross(a, b, c) * Cross(a, b, d) < 0 && Cross(c, d, a) * Cross(c, d, b) < 0)
                    || On(a, b, c) || On(a, b, d) || On(c, d, a) || On(c, d, b)) return Array.Empty<int>();
            }
        }
        if (flat.Length < 3) return Array.Empty<int>();
        float area = 0;
        for (int i = 0; i < flat.Length; i++) area += flat[i].X * flat[(i + 1) % flat.Length].Y - flat[(i + 1) % flat.Length].X * flat[i].Y;
        if (Math.Abs(area) < 1e-6f) return Array.Empty<int>();
        var winding = Math.Sign(area);
        var ring = Enumerable.Range(0, flat.Length).ToList();
        var triangles = new List<int>();
        while (ring.Count > 2) {
            bool found = false;
            for (int i = 0; i < ring.Count; i++) {
                var a = ring[(i + ring.Count - 1) % ring.Count]; var b = ring[i]; var c = ring[(i + 1) % ring.Count];
                var cross = Cross(flat[a], flat[b], flat[c]) * winding;
                if (Math.Abs(cross) < 1e-7f) { ring.RemoveAt(i); found = true; break; }
                if (cross < 0) continue;
                bool inside = ring.Any(p => p != a && p != b && p != c && Cross(flat[a], flat[b], flat[p]) * winding >= -1e-7f
                    && Cross(flat[b], flat[c], flat[p]) * winding >= -1e-7f && Cross(flat[c], flat[a], flat[p]) * winding >= -1e-7f);
                if (inside) continue;
                triangles.AddRange(new[] { a, b, c }); ring.RemoveAt(i); found = true; break;
            }
            if (!found) return Array.Empty<int>();
        }
        return triangles.ToArray();
    }

    static GeometryResult Polygon(CustomElement element) {
        element.splineData = null;
        var geometry = new GeometryResult();
        var points = ((PointList)element.points.Get("perimeter")).items.Cast<PointLeaf>().Select(x => x.point.GetPoint()).ToList();
        if (points.Count >= 2) geometry.lines.Add(new GeometryLine { points = points, closed = points.Count >= 3, always = true, color = Colors.Cyan });
        if (points.Count < 3) return geometry;
        var indices = Triangulate(points);
        if (indices.Length == 0) { element.propPlacementError = "PP_INVALID_POLYGON"; return geometry; }
        var areas = new List<double>(); double area = 0;
        for (int i = 0; i < indices.Length; i += 3) {
            var a = points[indices[i]]; var b = points[indices[i + 1]]; var c = points[indices[i + 2]];
            area += Math.Abs((b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X)) * 0.5;
            areas.Add(area);
        }
        if (area < 1e-6) { element.propPlacementError = "PP_INVALID_POLYGON"; return geometry; }
        var models = element.state.Array<ObjectState>("models") ?? Array.Empty<ObjectState>();
        var random = new RandomSequence(element.state.Int("seed"));
        if (element.Settings.propPlacer.placement != null) {
            var host = element.GetCustomElementType().scriptHost;
            var context = element.CreateScriptContext(null);
            context["perimeter"] = CustomElementScripting.List(points);
            context["triangles"] = CustomElementScripting.List(indices);
            context["area"] = area;
            context["models"] = StateTable(models);
            context["parameters"] = StateTable(element.state);
            context["random"] = new LuaFunction("random", (ctx, ct) => new ValueTask<int>(ctx.Return(new LuaValue(random.Next()))));
            var errors = host.ErrorCount;
            var result = host.Call(host.Resolve(element.Settings.propPlacer.placement), element.GetPlacementVariables(), context, element.TypeName + " (prop placement)");
            if (!result.TryRead<LuaTable>(out var table) || host.ErrorCount != errors) throw new ArgumentException("Prop placement callback must return a list");
            if (table.ArrayLength > 100000) throw new ArgumentException("Too many generated props");
            for (int i = 1; i <= table.ArrayLength; i++) {
                var item = table[i].Read<LuaTable>();
                var model = item["model"].Read<int>() - 1;
                if (model < 0 || model >= models.Length) throw new ArgumentException("Invalid generated prop model index");
                geometry.props.Add(new Instance { container = "models", mesh = models[model].Str("mesh"), position = item["position"].Read<LuaVec3>().value,
                    rotation = item["rotation"].TryRead<LuaVec3>(out var rotation) ? rotation.value : Vector3.Zero,
                    scale = item["scale"].TryRead<LuaVec3>(out var scale) ? scale.value : Vector3.One });
            }
        } else {
            foreach (var model in models) {
                var count = Math.Floor(area * model.Float("density", 1) / 100);
                if (count + geometry.props.Count > 100000) throw new ArgumentException("Too many generated props");
                for (int i = 0; i < count; i++) {
                    var pick = random.Next() * area;
                    var triangle = areas.BinarySearch(pick); if (triangle < 0) triangle = ~triangle;
                    triangle = Math.Min(triangle, areas.Count - 1) * 3;
                    var u = Math.Sqrt(random.Next()); var v = random.Next();
                    var position = points[indices[triangle]] * (float)(1 - u) + points[indices[triangle + 1]] * (float)(u * (1 - v)) + points[indices[triangle + 2]] * (float)(u * v);
                    geometry.props.Add(new Instance { container = "models", mesh = model.Str("mesh"), position = position,
                        rotation = new Vector3(0, (float)(model.Float("minYaw") + random.Next() * (model.Float("maxYaw", 360) - model.Float("minYaw"))), 0) });
                }
            }
        }
        foreach (var prop in geometry.props) {
            if (!prop.position.IsFinite() || !prop.rotation.IsFinite() || !prop.scale.IsFinite()) throw new ArgumentException("Non-finite generated prop transform");
            if (element.ProjectsPlacement && element.Settings.propPlacer.projectProps) prop.position = Project(element, prop.position);
        }
        return geometry;
    }

    static LuaValue StateTable(object value) {
        if (value is ObjectState state) { var table = new LuaTable(); foreach (var p in state.properties) table[p.Key] = StateTable(p.Value); return table; }
        if (value is System.Collections.IEnumerable items && value is not string) { var table = new LuaTable(); int i = 0; foreach (var item in items) table[++i] = StateTable(item); return table; }
        return value switch { string s => new LuaValue(s), bool b => new LuaValue(b), int n => new LuaValue(n), long n => new LuaValue(n), float n => new LuaValue(n), double n => new LuaValue(n), _ => LuaValue.Nil };
    }
}

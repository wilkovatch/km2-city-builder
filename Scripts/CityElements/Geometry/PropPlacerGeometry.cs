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
    const float GuideStep = 2; //the distance between the points of a guide on the ground, as the cells of the terrain
    const int MaxGuidePoints = 512;

    //Stable across runtime versions, independent of the application's shared expression random sequence.
    public sealed class RandomSequence {
        uint state;
        public RandomSequence(int seed) { state = unchecked((uint)seed); }
        public double Next() {
            state = unchecked(state * 1664525u + 1013904223u);
            return state / 4294967296.0;
        }
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
            if (string.IsNullOrEmpty(container.Str("type", null)) && strip.allowedContainerTypes?.Length > 0) container.SetStr("type", strip.allowedContainerTypes[0]);
            ValidateProps(container, strip.allowedContainerTypes);
        }
    }

    //checks a new state of a props container of a prop placer
    public static void ValidateContainer(ObjectState owner, string name, ObjectState state) {
        if (Strip(owner, name) == null) return;
        var candidate = (ObjectState)owner.Clone();
        Operations.Props.GetPropRule(candidate, true).SetState(name, state);
        Validate(candidate, candidate.Str("type"));
    }

    //a props state and those nested in it: their types and the ranges of their parameters
    static void ValidateProps(ObjectState state, string[] allowedTypes) {
        var id = state.Str("type", "");
        var type = Operations.Props.TypeOf(state) ?? throw new ArgumentException("Unknown props type: " + id);
        if (allowedTypes?.Length > 0 && !allowedTypes.Contains(id)) throw new ArgumentException("Props type is not allowed here: " + id);
        foreach (var parameter in type.parameterContainer.parameters ?? Array.Empty<Parameter>()) {
            if (parameter.range != null) CheckRange(state, parameter);
        }
        foreach (var list in Operations.Props.Lists(state)) {
            var items = state.Array<ObjectState>(list.fullName()) ?? Array.Empty<ObjectState>();
            if (list.arrayProperties.maxElements > 0 && items.Length > list.arrayProperties.maxElements) throw new ArgumentException("Too many props in " + list.fullName());
            foreach (var item in items) ValidateProps(item, list.arrayProperties.propsTypes);
        }
        Operations.States.Normalize(state, type.parameterContainer.parameters);
    }

    public static bool InRange(ObjectState state, Parameter parameter) {
        try {
            CheckRange(state, parameter);
            return true;
        } catch (ArgumentException) {
            return false;
        }
    }

    static void CheckRange(ObjectState state, Parameter parameter) {
        var name = parameter.fullName();
        var range = parameter.range;
        var value = state.properties.TryGetValue(name, out var raw) ? Convert.ToSingle(raw) : 0;
        if (range.Length < 2 || range.Length > 3 || !float.IsFinite(value) || value < range[0] || value > range[1]) throw new ArgumentException("Prop parameter out of range: " + name);
        if (range.Length == 3) {
            var step = range[2];
            if (!float.IsFinite(step) || step <= 0 || Math.Abs((value - range[0]) / step - MathF.Round((value - range[0]) / step)) > 0.0001f)
                throw new ArgumentException("Prop parameter does not match its step: " + name);
        }
        if (parameter.type == "int" && value != MathF.Truncate(value)) throw new ArgumentException("Prop parameter must be an integer: " + name);
    }

    public static Vector3 Project(CustomElement element, Vector3 position) {
        return GeometryHelper.AutoProjectOnSurface(position, CityGroundHelper.ExcludeHiddenTerrain(element.Settings.propPlacer.projectionMask), element.generator);
    }

    public static GeometryResult Build(CustomElement element) {
        var settings = element.Settings.propPlacer;
        Validate(element.state, element.TypeName);
        element.propPlacementError = null;
        var geometry = settings.type == "spline" ? Spline(element) : Polygon(element);
        var lines = new List<GeometryLine>();
        foreach (var line in geometry.lines) {
            line.heightOffset += settings.guideHeightOffset;
            line.wideCollider = true;
            if (element.ProjectsPlacement) {
                line.points = Subdivided(line.points, line.closed).Select(p => Project(element, p)).ToList();
                lines.Add(line);
            } else {
                lines.AddRange(SplitWhereCovered(line, p => Project(element, p)));
            }
        }
        geometry.lines = lines;
        return geometry;
    }

    //the points of a line with enough others to follow the ground (the shapes are projected only at their sections or
    //controls); a closed line doesn't repeat its first point
    static List<Vector3> Subdivided(List<Vector3> points, bool closed) {
        if (points.Count < 2) return points;
        var shape = closed ? points.Append(points[0]).ToList() : points;
        var length = shape.Zip(shape.Skip(1), (a, b) => a.DistanceTo(b)).Sum();
        var step = Math.Max(GuideStep, length / MaxGuidePoints);
        var res = new List<Vector3>();
        for (int i = 0; i < shape.Count - 1; i++) {
            var count = Math.Max(1, (int)Math.Ceiling(shape[i].DistanceTo(shape[i + 1]) / step));
            for (int j = 0; j < count; j++) res.Add(shape[i].Lerp(shape[i + 1], j / (float)count));
        }
        if (!closed) res.Add(shape[^1]);
        return res;
    }

    //a line showing where something is (e.g. a guide of the props): the parts under a surface are drawn through it, also
    //the terrain doesn't hide them by itself as it's a transparent grid. surface gives the point on what covers a point
    internal static List<GeometryLine> SplitWhereCovered(GeometryLine line, Func<Vector3, Vector3> surface) {
        var points = Subdivided(line.points, line.closed);
        if (points.Count < 2) return new List<GeometryLine> { line };
        if (line.closed) points.Add(points[0]);
        var covered = points.Select(p => surface(p).Y > p.Y + line.heightOffset).ToList();
        if (!covered.Contains(true)) return new List<GeometryLine> { line };
        var res = new List<GeometryLine>();
        int start = 0;
        for (int i = 1; i < points.Count; i++) {
            if (covered[i] == covered[start] && i < points.Count - 1) continue;
            //each part ends at the first point of the next one, so that there's no gap between them
            var part = line.WithPoints(points.GetRange(start, i - start + 1));
            part.drawnThrough = covered[start];
            res.Add(part);
            start = i;
        }
        return res;
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
            var propsType = Operations.Props.TypeOf(element.state.State("propRule")?.State(strip.name))?.typeData;
            if (propsType?.lineOnly == true) a = b = 0;
            var positions = sections.Select(s => s.Position).ToList();
            if (propsType?.authoredLine == true && element.splineData.authored?.Count == positions.Count) positions = element.splineData.authored;
            if (!float.IsFinite(a) || !float.IsFinite(b)) throw new ArgumentException("Non-finite prop strip bounds");
            var line = new GeometryPropLine { container = strip.name };
            for (int i = 0; i < sections.Length; i++) {
                var previous = positions[Math.Max(0, i - 1)];
                var next = positions[Math.Min(sections.Length - 1, i + 1)];
                var tangent = next - previous; tangent.Y = 0;
                var right = tangent.LengthSquared() > 1e-8f ? tangent.Normalized().Cross(Vector3.Up) : Vector3.Right;
                line.startBound.Add(positions[i] + right * a);
                line.endBound.Add(positions[i] + right * b);
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
                //the model is an entry of the models of the element, or a mesh
                var modelValue = table[i].Read<LuaTable>()["model"];
                var placement = PropPlacement.ReadPlacement(table[i], "models");
                if (modelValue.Type == LuaValueType.Number) {
                    var model = modelValue.Read<int>() - 1;
                    if (model < 0 || model >= models.Length) throw new ArgumentException("Invalid generated prop model index");
                    placement.mesh = models[model].Str("mesh");
                }
                geometry.props.Add(placement);
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
                    geometry.props.Add(new Placement { container = "models", mesh = model.Str("mesh"), position = position,
                        rotation = new Vector3(0, (float)(model.Float("minYaw") + random.Next() * (model.Float("maxYaw", 360) - model.Float("minYaw"))), 0) });
                }
            }
        }
        foreach (var prop in geometry.props) {
            if (!prop.position.IsFinite() || !prop.rotation.IsFinite() || !prop.scale.IsFinite()) throw new ArgumentException("Non-finite generated prop transform");
            if (element.ProjectsPlacement) prop.position = Project(element, prop.position);
        }
        return geometry;
    }

    public static LuaValue StateTable(object value) {
        if (value is ObjectState state) { var table = new LuaTable(); foreach (var p in state.properties) table[p.Key] = StateTable(p.Value); return table; }
        if (value is System.Collections.IEnumerable items && value is not string) { var table = new LuaTable(); int i = 0; foreach (var item in items) table[++i] = StateTable(item); return table; }
        return value switch { string s => new LuaValue(s), bool b => new LuaValue(b), int n => new LuaValue(n), long n => new LuaValue(n), float n => new LuaValue(n), double n => new LuaValue(n), _ => LuaValue.Nil };
    }
}

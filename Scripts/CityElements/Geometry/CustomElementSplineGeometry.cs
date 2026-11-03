using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using CustomElements;
using GH = GeometryHelper;

//Evaluated on the main thread: projection uses physics. Only control points and state are saved; sections are derived.
public static class CustomElementSplineGeometry {
    public class Section {
        public float[] center, right, up;
        public float[][] vertices;
        public float distance;
        public float parameter;
        internal Vector3 Position => Vec(center);
        internal Vector3 Right => Vec(right);
        internal Vector3 Up => Vec(up);
        internal Vector3[] Vertices => vertices.Select(Vec).ToArray();
    }

    public class Data {
        public Section[] sections = Array.Empty<Section>();
        //Independent component spans, including any expression-driven subdivision. Exporters use these exact edges.
        public Section[][] pieces = Array.Empty<Section[]>();
        //The positions of the sections before their projection.
        [Newtonsoft.Json.JsonIgnore]
        public List<Vector3> authored;
        //A loop: the last section is at the position of the first and the last piece closes it.
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public bool closed;
    }

    static Vector3 Vec(float[] v) => new Vector3(v[0], v[1], v[2]);
    static float[] Pack(Vector3 v) => new[] { v.X, v.Y, v.Z };
    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public static List<Vector3> Sample(List<Vector3> controls, GeometryHelper.CurveType mode, int segments,
        float tension = 0.5f, bool equally = false) {
        var distinct = new List<Vector3>();
        foreach (var p in controls) {
            if (Finite(p) && (distinct.Count == 0 || p.DistanceTo(distinct[^1]) > GH.epsilon)) distinct.Add(p);
        }
        controls = distinct;
        if (controls.Count < 2) return controls;
        if (mode == GH.CurveType.LowPoly) return controls;
        var result = new List<Vector3>();
        var inner = controls.GetRange(1, controls.Count - 2);
        segments = Math.Clamp(segments, 1, 10000);
        for (int i = 0; i <= segments; i++) {
            var p = GH.GetPointOnCurve(controls[0], inner, controls[^1], i / (float)segments, i, mode, tension, equally);
            if (Finite(p) && (result.Count == 0 || p.DistanceTo(result[^1]) > GH.epsilon)) result.Add(p);
        }
        return result;
    }

    //A loop through the controls, ending at the first one again: a cardinal spline (tangents tension * (next - previous), as
    //the open Hermite curve) in the same number of segments per control, or the controls for a low-poly curve
    public static List<Vector3> SampleClosed(List<Vector3> controls, GeometryHelper.CurveType mode, int segments, float tension = 0.5f) {
        var distinct = new List<Vector3>();
        foreach (var p in controls) {
            if (Finite(p) && (distinct.Count == 0 || p.DistanceTo(distinct[^1]) > GH.epsilon)) distinct.Add(p);
        }
        if (distinct.Count > 1 && distinct[0].DistanceTo(distinct[^1]) <= GH.epsilon) distinct.RemoveAt(distinct.Count - 1);
        controls = distinct;
        if (controls.Count < 3 || mode == GH.CurveType.LowPoly) {
            var open = new List<Vector3>(controls);
            if (controls.Count >= 3) open.Add(controls[0]);
            return open;
        }
        int n = controls.Count, perSpan = Math.Clamp((int)Math.Ceiling(Math.Clamp(segments, 1, 10000) / (float)n), 1, 10000);
        var result = new List<Vector3> { controls[0] };
        for (int s = 0; s < n; s++) {
            var p0 = controls[(s + n - 1) % n]; var p1 = controls[s]; var p2 = controls[(s + 1) % n]; var p3 = controls[(s + 2) % n];
            var t1 = tension * (p2 - p0); var t2 = tension * (p3 - p1);
            for (int i = 1; i <= perSpan; i++) {
                var a = i / (float)perSpan; var a2 = a * a; var a3 = a2 * a;
                var p = (2 * a3 - 3 * a2 + 1) * p1 + (-2 * a3 + 3 * a2) * p2 + (a3 - 2 * a2 + a) * t1 + (a3 - a2) * t2;
                result.Add(i == perSpan ? p2 : p);
            }
        }
        result[^1] = controls[0];
        return result;
    }

    public static GeometryResult Build(CustomElement element, out Data data) {
        data = new Data();
        var result = new GeometryResult();
        var settings = element.Settings.spline;
        var type = element.GetCustomElementType().splineType;
        var state = element.state;
        var mode = state.Int("curveType", settings.defaultCurveType);
        if (Array.IndexOf(settings.curveTypes, mode) < 0) throw new ArgumentException("Curve type is not allowed by " + element.TypeName);
        if (element.points.Get("spline") is not PointList list) return result;
        var controls = list.items.Cast<PointLeaf>().Select(p => p.point.GetPoint()).ToList();
        var closed = settings.closed && controls.Count >= 3;
        data.closed = closed;
        var segments = Math.Clamp(state.Int("segments", settings.segments), 1, 10000);
        if (state.Bool("segmentsPer100m", settings.segmentsPer100m)) {
            var length = controls.Zip(controls.Skip(1), (a, b) => a.DistanceTo(b)).Sum() + (closed ? controls[^1].DistanceTo(controls[0]) : 0);
            segments = Math.Clamp((int)(segments * length / 100), 1, 10000);
        }
        var custom = SplineCurves.Create(settings, state, controls, closed: closed);
        var evaluated = custom?.Sample(segments, settings.previewTolerance, settings.previewMaxDepth);
        var samples = evaluated?.points ?? (closed ? SampleClosed(controls, (GH.CurveType)mode, segments, state.Float("hermiteTension", 0.5f))
            : Sample(controls, (GH.CurveType)mode, segments, state.Float("hermiteTension", 0.5f), state.Bool("subdivideEqually")));
        data.authored = samples;
        var projectSections = element.Settings.propPlacer != null ? element.ProjectsPlacement : settings.projectSections;
        if (projectSections) {
            samples = samples.Select(p => element.Settings.propPlacer != null ? PropPlacerGeometry.Project(element, p) : GH.AutoProjectOnSurface(p, settings.projectionMask, element.generator)).ToList();
        }
        if (closed && samples.Count >= 3) samples[^1] = samples[0]; //the loop ends exactly where it starts, even when projected
        var total = samples.Zip(samples.Skip(1), (a, b) => a.DistanceTo(b)).Sum();
        var gen = new RoadLikeGenerator<CityElements.Types.RoadType>(state, null, samples.Count, settings.textures.Length) { owner = element };
        gen.Reset(type, state, null, samples.Count, settings.textures.Length);
        type.FillInitialVariables(gen.variableContainer, state, null, element.cityPropertiesState, total, samples.Count);
        GeometryLine Line(List<Vector3> positions, CityElements.Types.CustomElementSplineLine style) {
            var line = new GeometryLine { points = positions, always = style?.always == true, heightOffset = style?.heightOffset ?? 0 };
            if (style?.arrowDirection != null) {
                var expression = RuntimeCalculator.Parsers.FloatParser.ParseExpression(style.arrowDirection);
                expression.SetIndices(gen.variableContainer);
                line.arrowDirection = expression.GetValue(gen.variableContainer);
            }
            if (style?.color != null) line.color = CustomElementScripting.ReadColor(new Lua.LuaValue(style.color), line.color);
            return line;
        }
        //Only the displayed/pickable line is trimmed; the curve and exported sections keep all controls.
        List<Vector3> LinePositions() {
            if (closed) return samples; //a loop has no ends to trim
            var start = Math.Max(0, settings.line?.trimStart ?? 0);
            var end = controls.Count - 1 - Math.Max(0, settings.line?.trimEnd ?? 0);
            if (start == 0 && end == controls.Count - 1) return samples;
            if (start >= end) return new List<Vector3>();
            List<Vector3> positions;
            if (custom != null) {
                var a = custom.controlParameters[start]; var b = custom.controlParameters[end];
                if (a >= b) return new List<Vector3>();
                positions = new List<Vector3> { custom.Position(a) };
                for (int i = 0; i < evaluated.Value.parameters.Count; i++) {
                    var t = evaluated.Value.parameters[i];
                    if (t > a && t < b) positions.Add(evaluated.Value.points[i]);
                }
                positions.Add(custom.Position(b));
            } else if (mode == (int)GH.CurveType.LowPoly) {
                positions = controls.GetRange(start, end - start + 1);
            } else {
                var knots = GH.GetPointParameters(controls, (GH.CurveType)mode, state.Bool("subdivideEqually"));
                var a = knots[start]; var b = knots[end];
                if (a >= b) return new List<Vector3>();
                var parameters = new SortedSet<float> { a, b };
                for (int i = 1; i < segments; i++) {
                    var t = i / (float)segments;
                    if (t > a && t < b) parameters.Add(t);
                }
                var inner = controls.GetRange(1, controls.Count - 2);
                positions = parameters.Select(t => GH.GetPointOnCurve(controls[0], inner, controls[^1], t, 0,
                    (GH.CurveType)mode, state.Float("hermiteTension", 0.5f), state.Bool("subdivideEqually"))).ToList();
            }
            return projectSections
                ? positions.Select(p => GH.AutoProjectOnSurface(p, settings.projectionMask, element.generator)).ToList() : positions;
        }
        void AddLine(GeometryLine line, CityElements.Types.CustomElementSplineLine style) {
            if (style?.drawWhenCovered != true) {
                result.lines.Add(line);
                return;
            }
            line.wideCollider = true; //also so that it's not a surface covering the other lines
            //the highest surface, not the rule of the projections: under a heightmap used as a guide it's still under the ground
            result.lines.AddRange(PropPlacerGeometry.SplitWhereCovered(line,
                p => GameEngine.Raycast.ProjectPoint(p, settings.projectionMask | GameEngine.Raycast.ProjectionSurfaceMask, element.generator)));
        }
        var linePositions = LinePositions();
        if (linePositions.Count >= 2) AddLine(Line(linePositions, settings.line), settings.line);
        if (settings.controlLine != null && controls.Count >= 2) {
            var controlLine = Line(controls, settings.controlLine);
            controlLine.closed = closed;
            AddLine(controlLine, settings.controlLine);
        }
        if ((settings.components?.Length ?? 0) == 0) {
            var centerSections = new List<Section>(); float d = 0;
            for (int i = 0; i < samples.Count; i++) {
                if (i > 0) d += samples[i].DistanceTo(samples[i-1]);
                centerSections.Add(new Section { center = Pack(samples[i]), right = Pack(Vector3.Right), up = Pack(Vector3.Up),
                    vertices = Array.Empty<float[]>(), distance = d, parameter = evaluated?.parameters[i] ?? i / (float)Math.Max(1, samples.Count-1) });
            }
            data.sections = centerSections.ToArray();
            data.pieces = centerSections.Zip(centerSections.Skip(1), (a,b) => new[] { a,b }).ToArray();
            return result;
        }
        if (samples.Count < 2) return result;
        var sections = new List<Section>();
        float distance = 0;
        for (int i = 0; i < samples.Count; i++) {
            var prev = i > 0 ? samples[i] - samples[i - 1] : closed ? samples[0] - samples[^2] : samples[1] - samples[0];
            var next = i < samples.Count - 1 ? samples[i + 1] - samples[i] : closed ? samples[1] - samples[0] : prev;
            prev.Y = next.Y = 0;
            if (prev.LengthSquared() < GH.epsilon || next.LengthSquared() < GH.epsilon) continue;
            prev = prev.Normalized(); next = next.Normalized();
            var dir = (prev + next).LengthSquared() > GH.epsilon ? (prev + next).Normalized() : next;
            var factor = Math.Min(Math.Max(1, settings.maxMiter), 1 / Math.Max(0.001f, dir.Dot(next)));
            var right = dir.Cross(Vector3.Up).Normalized() * factor;
            if (i > 0) distance += samples[i].DistanceTo(samples[i - 1]);
            if (settings.variableSections) type.FillSegmentVariables(gen.variableContainer, state, null, samples[i], right, Vector3.Up, distance, 0, i + 1,
                Array.Empty<Vector3>(), samples, samples.Count);
            var offsets = type.segmentVerticesCalculators.GetValues(gen.variableContainer);
            var vertices = offsets.Select(x => samples[i] + right * x).ToArray();
            sections.Add(new Section { center = Pack(samples[i]), right = Pack(right), up = Pack(Vector3.Up),
                vertices = vertices.Select(Pack).ToArray(), distance = distance, parameter = evaluated?.parameters[i] ?? i / (float)Math.Max(1, samples.Count-1) });
        }
        data.sections = sections.ToArray();
        if (sections.Count < 2) return result;
        var pieces = new List<Section[]>();
        var split = settings.splitCondition == null ? null : RuntimeCalculator.Parsers.BooleanParser.ParseExpression(settings.splitCondition);
        split?.SetIndices(gen.variableContainer);
        void AddPiece(Section a, Section b, int depth) {
            var length = a.Position.DistanceTo(b.Position);
            if (length < GH.epsilon) return;
            type.FillInitialVariables(gen.variableContainer, state, null, element.cityPropertiesState, length, 2);
            type.FillCommonLateVariables(gen.variableContainer, a.Position, b.Position, a.Vertices, b.Vertices);
            if (depth < Math.Clamp(settings.maxSplits, 0, 8) && split?.GetValue(gen.variableContainer) == true) {
                var middle = Lerp(a, b, 0.5f);
                AddPiece(a, middle, depth + 1); AddPiece(middle, b, depth + 1);
            } else pieces.Add(new[] { a, b });
        }
        for (int i = 1; i < sections.Count; i++) {
            var a = sections[i - 1]; var b = sections[i];
            var count = settings.maxSectionLength > 0 ? Math.Clamp((int)Math.Ceiling(a.Position.DistanceTo(b.Position) / settings.maxSectionLength), 1, 10000) : 1;
            for (int j = 0; j < count; j++) AddPiece(Lerp(a, b, j / (float)count), Lerp(a, b, (j + 1f) / count), 0);
        }
        data.pieces = pieces.ToArray();
        if (settings.independentSegments) {
            for (int i = 0; i < pieces.Count; i++) Generate(pieces[i], i == 0 && !closed, i == pieces.Count - 1 && !closed);
        } else Generate(sections.ToArray(), !closed, !closed);
        return result;

        void Generate(Section[] span, bool startCap, bool endCap) {
            var length = span[^1].distance - span[0].distance;
            gen.Reset(type, state, null, span.Length, settings.textures.Length);
            foreach (var section in span) {
                gen.curvePoints.Add(section.Position); gen.curveRightVectors.Add(section.Right); gen.curveUpVectors.Add(section.Up);
                gen.sectionRights.Add(section.Right); gen.sectionUps.Add(section.Up); gen.sectionVertices.Add(section.Vertices);
                gen.sectionMarkers.Add(section.distance - span[0].distance);
            }
            type.FillInitialVariables(gen.variableContainer, state, null, element.cityPropertiesState, length, span.Length);
            gen.InitBaseSectionsInfo();
            type.FillCommonLateVariables(gen.variableContainer, span[0].Position, span[^1].Position, span[0].Vertices, span[^1].Vertices);
            for (int i = 0; i < span.Length; i++) {
                gen.InitSection(i); gen.AddSection(i, null, startCap: startCap, endCap: endCap);
            }
            for (int i = 0; i < settings.textures.Length; i++) {
                if (gen.tempIndices[i].Count == 0) continue;
                result.meshes.Add(new GeometryMesh { texture = settings.textures[i], vertices = new List<Vector3>(gen.tempVertices),
                    uvs = new List<Vector2>(gen.tempUVs), triangles = new List<int>(gen.tempIndices[i]) });
            }
        }
    }

    static Section Lerp(Section a, Section b, float t) {
        return new Section { center = Pack(a.Position.Lerp(b.Position, t)), right = Pack(a.Right.Lerp(b.Right, t)), up = Pack(a.Up.Lerp(b.Up, t)),
            vertices = a.Vertices.Zip(b.Vertices, (x, y) => Pack(x.Lerp(y, t))).ToArray(), distance = Mathf.Lerp(a.distance, b.distance, t), parameter = Mathf.Lerp(a.parameter, b.parameter, t) };
    }
}

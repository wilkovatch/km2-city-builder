using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LowLevel;
using GH = GeometryHelper;

//Evaluated on the main thread: projection uses physics. Only control points and state are saved; sections are derived.
public static class LowLevelSplineGeometry {
    public class Section {
        public float[] center, right, up;
        public float[][] vertices;
        public float distance;
        internal Vector3 Position => Vec(center);
        internal Vector3 Right => Vec(right);
        internal Vector3 Up => Vec(up);
        internal Vector3[] Vertices => vertices.Select(Vec).ToArray();
    }

    public class Data {
        public Section[] sections = Array.Empty<Section>();
        //Independent component spans, including any expression-driven subdivision. Exporters use these exact edges.
        public Section[][] pieces = Array.Empty<Section[]>();
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

    public static GeometryResult Build(LowLevelElement element, out Data data) {
        data = new Data();
        var result = new GeometryResult();
        var settings = element.Settings.spline;
        var type = element.GetLowLevelType().splineType;
        var state = element.state;
        var mode = state.Int("curveType", settings.defaultCurveType);
        if (Array.IndexOf(settings.curveTypes, mode) < 0) throw new ArgumentException("Curve type is not allowed by " + element.TypeName);
        if (element.points.Get("spline") is not PointList list) return result;
        var controls = list.items.Cast<PointLeaf>().Select(p => p.point.GetPoint()).ToList();
        var segments = Math.Clamp(state.Int("segments", settings.segments), 1, 10000);
        if (state.Bool("segmentsPer100m", settings.segmentsPer100m)) {
            var length = controls.Zip(controls.Skip(1), (a, b) => a.DistanceTo(b)).Sum();
            segments = Math.Clamp((int)(segments * length / 100), 1, 10000);
        }
        var samples = Sample(controls, (GH.CurveType)mode, segments, state.Float("hermiteTension", 0.5f), state.Bool("subdivideEqually"));
        if (settings.projectSections) {
            samples = samples.Select(p => GH.ProjectOnSurface(p, settings.projectionMask, element.generator)).ToList();
        }
        if (samples.Count < 2) return result;
        var total = samples.Zip(samples.Skip(1), (a, b) => a.DistanceTo(b)).Sum();
        var gen = new RoadLikeGenerator<CityElements.Types.RoadType>(state, null, samples.Count, settings.textures.Length) { owner = element };
        gen.Reset(type, state, null, samples.Count, settings.textures.Length);
        type.FillInitialVariables(gen.variableContainer, state, null, null, total, samples.Count);
        var sections = new List<Section>();
        float distance = 0;
        for (int i = 0; i < samples.Count; i++) {
            var prev = i > 0 ? samples[i] - samples[i - 1] : samples[1] - samples[0];
            var next = i < samples.Count - 1 ? samples[i + 1] - samples[i] : prev;
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
                vertices = vertices.Select(Pack).ToArray(), distance = distance });
        }
        data.sections = sections.ToArray();
        if (sections.Count < 2) return result;
        var pieces = new List<Section[]>();
        var split = settings.splitCondition == null ? null : RuntimeCalculator.Parsers.BooleanParser.ParseExpression(settings.splitCondition);
        split?.SetIndices(gen.variableContainer);
        void AddPiece(Section a, Section b, int depth) {
            var length = a.Position.DistanceTo(b.Position);
            if (length < GH.epsilon) return;
            type.FillInitialVariables(gen.variableContainer, state, null, null, length, 2);
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
            for (int i = 0; i < pieces.Count; i++) Generate(pieces[i], i == 0, i == pieces.Count - 1);
        } else Generate(sections.ToArray(), true, true);
        result.lines.Add(new GeometryLine { points = samples });
        return result;

        void Generate(Section[] span, bool startCap, bool endCap) {
            var length = span[^1].distance - span[0].distance;
            gen.Reset(type, state, null, span.Length, settings.textures.Length);
            foreach (var section in span) {
                gen.curvePoints.Add(section.Position); gen.curveRightVectors.Add(section.Right); gen.curveUpVectors.Add(section.Up);
                gen.sectionRights.Add(section.Right); gen.sectionUps.Add(section.Up); gen.sectionVertices.Add(section.Vertices);
                gen.sectionMarkers.Add(section.distance - span[0].distance);
            }
            type.FillInitialVariables(gen.variableContainer, state, null, null, length, span.Length);
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
            vertices = a.Vertices.Zip(b.Vertices, (x, y) => Pack(x.Lerp(y, t))).ToArray(), distance = Mathf.Lerp(a.distance, b.distance, t) };
    }
}

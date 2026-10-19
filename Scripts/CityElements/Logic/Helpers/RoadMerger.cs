using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using GO = GameEngine.GameObject;
using GH = GeometryHelper;

//Merges the two roads of an intersection into one road (the reverse of a split): the kept road gets the shape of the
//other one, which is deleted with the intersection. The kept road keeps its settings (its curve type too), except the
//properties of its end on the intersection, which come from the far end of the other road. If the two roads go opposite
//ways, only the geometry of the other road is reversed (its left and right properties are not swapped).
//The merge is exact when the other road has the same curve (or for two Bézier roads, when they are the parts of a single
//curve, as after a split), otherwise the merged curve follows the shape of both roads as closely as its type allows.
public static class RoadMerger {
    //the words of a property name, separated by underscores or capital letters (offL_end: off, end)
    static readonly Regex WordRegex = new Regex("[A-Z]?[a-z0-9]+");
    //distance of the control points of the parts from the ones of a single curve (Bézier) or from a straight line
    const float Tolerance = 0.05f;
    const int SamplesPerSpan = 16; //of the curves followed by a merged curve of another type
    const float HermiteFitTolerance = 0.5f; //distance of a Hermite curve from the curve it follows, under which no points are added
    const int HermiteFitMaxPoints = 32; //for a curve followed by a Hermite curve
    const float LowPolyFitTolerance = 0.25f; //distance of the points left out of a low-poly curve from it
    const int BezierFitMaxDegree = 12; //of a Bézier curve fitted to two curves (higher ones are unstable)
    const int BezierFitIterations = 4; //of the fit and the update of the curve parameters of the points followed

    //what the merge does, found before changing anything
    class Plan {
        public Road keep, other;
        public Intersection far; //of the other road
        public bool keepAtEnd, otherAtStart;
        public List<Vector3> positions;
        public List<Vector2> rotations;
    }

    //points along a curve, with the rotations of the sections there
    class Samples {
        public List<Vector3> points = new List<Vector3>();
        public List<Vector2> rotations = new List<Vector2>();
    }

    //the road of the intersection other than the given one
    static Road GetOther(Intersection intersection, Road keep) {
        return intersection.roads[0] == keep ? intersection.roads[1] : intersection.roads[0];
    }

    static Intersection GetFarIntersection(Road road, Intersection joint) {
        return road.startIntersection == joint ? road.endIntersection : road.startIntersection;
    }

    //null if the roads of the intersection can be merged into the given one, otherwise the reason why not
    public static string GetMergeError(Intersection intersection, Road keep) {
        if (intersection == null || intersection.IsDeleted()) return "the intersection doesn't exist";
        if (intersection.roads.Count != 2) return "the intersection must connect exactly two roads";
        if (keep == null || !intersection.roads.Contains(keep)) return "the road is not on the intersection";
        var other = GetOther(intersection, keep);
        if (other == keep) return "a road with both ends on the intersection can't be merged with itself";
        foreach (var road in new[] { keep, other }) {
            if (GO.IsNull(road) || road.deleted || road.points.Count < 2) return "both roads need at least two points";
        }
        var keepFar = GetFarIntersection(keep, intersection);
        if (keepFar != null && keepFar == GetFarIntersection(other, intersection)) return "the merged road would start and end on the same intersection";
        return null;
    }

    static Plan Prepare(Intersection intersection, Road keep) {
        var error = GetMergeError(intersection, keep);
        if (error != null) throw new System.InvalidOperationException(error);
        var plan = new Plan { keep = keep, other = GetOther(intersection, keep) };
        plan.keepAtEnd = keep.endIntersection == intersection;
        plan.otherAtStart = plan.other.startIntersection == intersection;
        plan.far = GetFarIntersection(plan.other, intersection);
        JoinPoints(plan);
        return plan;
    }

    // Control points

    static List<Vector3> GetPositions(Road road) {
        var res = new List<Vector3>();
        foreach (var point in road.points) res.Add(GO.GetPosition(point));
        return res;
    }

    static List<Vector3> Pack(List<Vector2> values) {
        return values.ConvertAll(v => new Vector3(v.X, v.Y, 0));
    }

    static List<Vector2> Unpack(List<Vector3> values) {
        return values.ConvertAll(v => new Vector2(v.X, v.Y));
    }

    static bool IsProjected(Road road) {
        return road.state.Bool("project") || road.state.Bool("projectAll");
    }

    static float GetTension(Road road) {
        return road.state.Float("hermiteTension", 0.5f);
    }

    //whether the control points of the other road describe the same curve if they are given to the kept road
    static bool HasSameCurve(Road keep, Road other) {
        var type = keep.GetCurveType();
        if (type != other.GetCurveType()) return false;
        if (type != GH.CurveType.Hermite) return true;
        return GetTension(keep) == GetTension(other) && keep.state.Bool("subdivideEqually") == other.state.Bool("subdivideEqually");
    }

    //reversed, the sections of a road are the same, but its right is the left
    static void Reverse(List<Vector3> positions, List<Vector2> rotations) {
        positions.Reverse();
        rotations.Reverse();
        for (int i = 0; i < rotations.Count; i++) rotations[i] = new Vector2(rotations[i].X, -rotations[i].Y);
    }

    //the control points of the merged road (in the direction of the kept road): the exact ones if possible, otherwise
    //the ones of a curve of the type of the kept road following both roads
    static void JoinPoints(Plan plan) {
        var keep = plan.keep;
        var other = plan.other;
        var reversed = plan.keepAtEnd != plan.otherAtStart;
        var sameCurve = HasSameCurve(keep, other);
        var type = keep.GetCurveType();
        var keepPositions = GetPositions(keep);
        var keepRotations = keep.GetPointRotations();
        var otherPositions = GetPositions(other);
        var otherRotations = other.GetPointRotations();
        if (reversed) Reverse(otherPositions, otherRotations);
        //the control points projected on the terrain are compared horizontally
        var projected = IsProjected(keep) || IsProjected(other);

        if (type == GH.CurveType.Bezier) {
            if (sameCurve) {
                var first = plan.keepAtEnd ? keepPositions : otherPositions;
                var second = plan.keepAtEnd ? otherPositions : keepPositions;
                var firstRotations = plan.keepAtEnd ? keepRotations : otherRotations;
                var secondRotations = plan.keepAtEnd ? otherRotations : keepRotations;
                var joined = JoinBezier(first, second, Pack(firstRotations), Pack(secondRotations), projected);
                if (joined != null) {
                    plan.positions = joined.Value.positions;
                    plan.rotations = Unpack(joined.Value.rotations);
                    return;
                }
            }
            //a single curve following both: with the degrees of both (a corner between two straight roads is rounded)
            var keepSamples = SampleCurve(keep, false);
            var otherSamples = SampleCurve(other, reversed);
            var target = Concatenate(plan.keepAtEnd ? keepSamples : otherSamples, plan.keepAtEnd ? otherSamples : keepSamples);
            var degree = Mathf.Clamp(keepPositions.Count + otherPositions.Count - 2, 1, BezierFitMaxDegree);
            (plan.positions, plan.rotations) = FitBezier(target, degree);
            return;
        }

        //Hermite and low-poly: the control points of the kept road, then the ones of the other road (of a curve of the
        //type of the kept road following it if it has another curve)
        if (!sameCurve) {
            var samples = SampleCurve(other, reversed);
            (otherPositions, otherRotations) = type == GH.CurveType.LowPoly
                ? SimplifyPolyline(samples, LowPolyFitTolerance)
                : FitHermite(samples, GetTension(keep), keep.state.Bool("subdivideEqually"));
        }
        var firstPositions = plan.keepAtEnd ? keepPositions : otherPositions;
        var secondPositions = plan.keepAtEnd ? otherPositions : keepPositions;
        var firstRots = plan.keepAtEnd ? keepRotations : otherRotations;
        var secondRots = plan.keepAtEnd ? otherRotations : keepRotations;
        //two straight roads in line are a straight road (drawn as a line between its ends, as before a split)
        if (firstPositions.Count == 2 && secondPositions.Count == 2) {
            var start = firstPositions[0];
            var end = secondPositions[1];
            var onLine = GH.ClosestPoint(firstPositions[1], start, end);
            if (Distance(onLine, firstPositions[1], projected) <= Tolerance) {
                plan.positions = new List<Vector3> { start, end };
                plan.rotations = new List<Vector2> { firstRots[0], secondRots[1] };
                return;
            }
        }
        //the point on the intersection is shared, with the rotation of the kept road
        plan.positions = new List<Vector3>(firstPositions);
        plan.positions.AddRange(secondPositions.GetRange(1, secondPositions.Count - 1));
        plan.rotations = new List<Vector2>(firstRots);
        plan.rotations.AddRange(secondRots.GetRange(1, secondRots.Count - 1));
        plan.rotations[firstRots.Count - 1] = plan.keepAtEnd ? keepRotations[keepRotations.Count - 1] : keepRotations[0];
    }

    static float Distance(Vector3 a, Vector3 b, bool horizontal) {
        return horizontal ? new Vector2(a[0] - b[0], a[2] - b[2]).Length() : GameEngine.Vector3.Distance(a, b);
    }

    //the control points of the Bézier curve that split at some point gives the two curves (null if there is none):
    //the reverse of GeometryHelper.SplitBezier. The point is where the tangents at the joint have the ratio of the
    //parts, the curve is extended from the longer part (de Casteljau outside of 0..1) and checked on both parts.
    static (List<Vector3> positions, List<Vector3> rotations)? JoinBezier(List<Vector3> first, List<Vector3> second,
        List<Vector3> firstRotations, List<Vector3> secondRotations, bool horizontal) {

        if (first.Count != second.Count) return null;
        var n = first.Count - 1;
        var d1 = Distance(first[n], first[n - 1], horizontal);
        var d2 = Distance(second[1], second[0], horizontal);
        if (d1 + d2 <= GH.epsilon) return null;
        var alpha = d1 / (d1 + d2);
        if (alpha <= GH.epsilon || alpha >= 1.0f - GH.epsilon) return null;
        List<Vector3> Extend(List<Vector3> firstPart, List<Vector3> secondPart) {
            return alpha >= 0.5f
                ? GH.SplitBezier(firstPart, 1.0f / alpha).left
                : GH.SplitBezier(secondPart, -alpha / (1.0f - alpha)).right;
        }
        var positions = Extend(first, second);
        var (left, right) = GH.SplitBezier(positions, alpha);
        for (int i = 0; i <= n; i++) {
            if (Distance(left[i], first[i], horizontal) > Tolerance || Distance(right[i], second[i], horizontal) > Tolerance) return null;
        }
        return (positions, Extend(firstRotations, secondRotations));
    }

    // Following a curve with a curve of another type

    //the curve of a road (as given by its control points and curve type), sampled along each span between them
    static Samples SampleCurve(Road road, bool reversed) {
        var positions = GetPositions(road);
        var rotations = road.GetPointRotations();
        var type = road.GetCurveType();
        var res = new Samples();
        if (type == GH.CurveType.LowPoly) {
            for (int i = 0; i < positions.Count - 1; i++) {
                for (int j = 0; j < SamplesPerSpan; j++) {
                    var f = j / (float)SamplesPerSpan;
                    res.points.Add(positions[i].Lerp(positions[i + 1], f));
                    res.rotations.Add(rotations[i].Lerp(rotations[i + 1], f));
                }
            }
            res.points.Add(positions[positions.Count - 1]);
            res.rotations.Add(rotations[rotations.Count - 1]);
        } else {
            var tension = GetTension(road);
            var subdivideEqually = road.state.Bool("subdivideEqually");
            var cps = positions.GetRange(1, positions.Count - 2);
            var count = SamplesPerSpan * (positions.Count - 1) + 1;
            for (int i = 0; i < count; i++) {
                var t = i / (count - 1.0f);
                res.points.Add(GH.GetPointOnCurve(positions[0], cps, positions[positions.Count - 1], t, i, type, tension, subdivideEqually));
                res.rotations.Add(GH.GetValueOnCurve(positions[0], cps, positions[positions.Count - 1], rotations, t, i, type, tension, subdivideEqually));
            }
        }
        if (reversed) Reverse(res.points, res.rotations);
        return res;
    }

    //the samples of two curves in a row (the second one starts where the first one ends)
    static Samples Concatenate(Samples first, Samples second) {
        var res = new Samples();
        res.points.AddRange(first.points);
        res.rotations.AddRange(first.rotations);
        res.points.AddRange(second.points.GetRange(1, second.points.Count - 1));
        res.rotations.AddRange(second.rotations.GetRange(1, second.rotations.Count - 1));
        return res;
    }

    static (List<Vector3>, List<Vector2>) Pick(Samples samples, List<int> indices) {
        return (indices.ConvertAll(i => samples.points[i]), indices.ConvertAll(i => samples.rotations[i]));
    }

    //the fewest samples for a polyline within the tolerance of all of them (Douglas-Peucker)
    static (List<Vector3>, List<Vector2>) SimplifyPolyline(Samples samples, float tolerance) {
        var keep = new bool[samples.points.Count];
        keep[0] = keep[keep.Length - 1] = true;
        var ranges = new Stack<(int from, int to)>();
        ranges.Push((0, keep.Length - 1));
        while (ranges.Count > 0) {
            var (from, to) = ranges.Pop();
            var worst = -1;
            var worstDistance = tolerance;
            for (int i = from + 1; i < to; i++) {
                var p = samples.points[i];
                var d = p.DistanceTo(GH.ClosestPoint(p, samples.points[from], samples.points[to]));
                if (d > worstDistance) {
                    worst = i;
                    worstDistance = d;
                }
            }
            if (worst < 0) continue;
            keep[worst] = true;
            ranges.Push((from, worst));
            ranges.Push((worst, to));
        }
        var indices = new List<int>();
        for (int i = 0; i < keep.Length; i++) if (keep[i]) indices.Add(i);
        return Pick(samples, indices);
    }

    //a Hermite curve through some of the samples (passing through its control points), adding the farthest one from it
    //until it is close to all of them
    static (List<Vector3>, List<Vector2>) FitHermite(Samples samples, float tension, bool subdivideEqually) {
        var indices = new List<int> { 0, samples.points.Count - 1 };
        while (indices.Count < HermiteFitMaxPoints) {
            var points = indices.ConvertAll(i => samples.points[i]);
            var cps = points.GetRange(1, points.Count - 2);
            var count = SamplesPerSpan * (points.Count - 1) + 1;
            var curve = new List<Vector3>();
            for (int i = 0; i < count; i++) {
                curve.Add(GH.GetPointOnCurve(points[0], cps, points[points.Count - 1], i / (count - 1.0f), i, GH.CurveType.Hermite, tension, subdivideEqually));
            }
            var worst = -1;
            var worstDistance = HermiteFitTolerance;
            for (int i = 1; i < samples.points.Count - 1; i++) {
                if (indices.Contains(i)) continue;
                var p = samples.points[i];
                var d = p.DistanceTo(GH.ClosestPointToCurve(p, curve, out _));
                if (d > worstDistance) {
                    worst = i;
                    worstDistance = d;
                }
            }
            if (worst < 0) break;
            indices.Add(worst);
            indices.Sort();
        }
        return Pick(samples, indices);
    }

    static double[] Bernstein(int degree, double t) {
        var res = new double[degree + 1];
        double binomial = 1;
        for (int j = 0; j <= degree; j++) {
            res[j] = binomial * System.Math.Pow(t, j) * System.Math.Pow(1 - t, degree - j);
            binomial = binomial * (degree - j) / (j + 1);
        }
        return res;
    }

    static Vector3 EvaluateBezier(List<Vector3> controlPoints, float t) {
        return GH.GetPointOnCurve(controlPoints[0], controlPoints.GetRange(1, controlPoints.Count - 2), controlPoints[controlPoints.Count - 1], t, 0, GH.CurveType.Bezier);
    }

    //the Bézier curve of the given degree closest to the samples (least squares, with its ends on the ends of the samples):
    //the samples are first given curve parameters by their distance along the samples, then the ones of the closest
    //points of the curve, and the curve is fitted again. The rotations are fitted with the same weights.
    static (List<Vector3>, List<Vector2>) FitBezier(Samples samples, int degree) {
        var points = samples.points;
        var rotations = samples.rotations;
        var count = points.Count;
        var parameters = new double[count];
        for (int i = 1; i < count; i++) parameters[i] = parameters[i - 1] + points[i].DistanceTo(points[i - 1]);
        var length = parameters[count - 1];
        for (int i = 0; i < count; i++) parameters[i] = length > 0 ? parameters[i] / length : i / (count - 1.0);

        var positions = new List<Vector3> { points[0], points[count - 1] };
        var values = new List<Vector2> { rotations[0], rotations[count - 1] };
        if (degree < 2) return (positions, values);
        for (int iteration = 0; iteration < BezierFitIterations; iteration++) {
            //the normal equations of the inner control points (5 columns: position and rotation)
            var n = degree - 1;
            var matrix = new double[n, n];
            var rhs = new double[n, 5];
            for (int i = 0; i < count; i++) {
                var b = Bernstein(degree, parameters[i]);
                var p = points[i] - (float)b[0] * points[0] - (float)b[degree] * points[count - 1];
                var r = rotations[i] - (float)b[0] * rotations[0] - (float)b[degree] * rotations[count - 1];
                var target = new double[] { p.X, p.Y, p.Z, r.X, r.Y };
                for (int j = 0; j < n; j++) {
                    for (int k = 0; k < n; k++) matrix[j, k] += b[j + 1] * b[k + 1];
                    for (int c = 0; c < 5; c++) rhs[j, c] += b[j + 1] * target[c];
                }
            }
            if (!Solve(matrix, rhs)) break;
            positions = new List<Vector3> { points[0] };
            values = new List<Vector2> { rotations[0] };
            for (int j = 0; j < n; j++) {
                positions.Add(new Vector3((float)rhs[j, 0], (float)rhs[j, 1], (float)rhs[j, 2]));
                values.Add(new Vector2((float)rhs[j, 3], (float)rhs[j, 4]));
            }
            positions.Add(points[count - 1]);
            values.Add(rotations[count - 1]);
            if (iteration == BezierFitIterations - 1) break;
            //the curve parameters of the points of the curve closest to the samples
            var curveSamples = count * 4;
            var curve = new List<Vector3>();
            for (int i = 0; i <= curveSamples; i++) curve.Add(EvaluateBezier(positions, i / (float)curveSamples));
            for (int i = 1; i < count - 1; i++) {
                GH.ClosestPointToCurve(points[i], curve, out var segment);
                if (segment < 0) continue;
                var f = GH.ClosestPointFactor(points[i], curve[segment], curve[segment + 1]);
                parameters[i] = (segment + f) / curveSamples;
            }
        }
        return (positions, values);
    }

    //solves the linear system in place (the solutions in the columns of rhs), false if it is singular
    static bool Solve(double[,] matrix, double[,] rhs) {
        var n = matrix.GetLength(0);
        var columns = rhs.GetLength(1);
        for (int col = 0; col < n; col++) {
            var pivot = col;
            for (int row = col + 1; row < n; row++) {
                if (System.Math.Abs(matrix[row, col]) > System.Math.Abs(matrix[pivot, col])) pivot = row;
            }
            if (System.Math.Abs(matrix[pivot, col]) < 1e-12) return false;
            if (pivot != col) {
                for (int k = 0; k < n; k++) (matrix[col, k], matrix[pivot, k]) = (matrix[pivot, k], matrix[col, k]);
                for (int k = 0; k < columns; k++) (rhs[col, k], rhs[pivot, k]) = (rhs[pivot, k], rhs[col, k]);
            }
            for (int row = 0; row < n; row++) {
                if (row == col) continue;
                var factor = matrix[row, col] / matrix[col, col];
                if (factor == 0) continue;
                for (int k = col; k < n; k++) matrix[row, k] -= factor * matrix[col, k];
                for (int k = 0; k < columns; k++) rhs[row, k] -= factor * rhs[col, k];
            }
        }
        for (int row = 0; row < n; row++) {
            for (int k = 0; k < columns; k++) rhs[row, k] /= matrix[row, row];
        }
        return true;
    }

    // Per-end properties

    //the first word of a property name that is "start" or "end" (any case): the end it refers to, and where the word is
    static (string side, int index, int length)? GetEndWord(string key) {
        foreach (Match m in WordRegex.Matches(key)) {
            var word = m.Value.ToLowerInvariant();
            if (word == "start" || word == "end") return (word, m.Index, m.Length);
        }
        return null;
    }

    //the name of the same property for the other end (endCrosswalkSize: startCrosswalkSize)
    static string GetCounterpart(string key, (string side, int index, int length) word) {
        var other = word.side == "start" ? "end" : "start";
        if (char.IsUpper(key[word.index])) other = char.ToUpperInvariant(other[0]) + other.Substring(1);
        return key.Substring(0, word.index) + other + key.Substring(word.index + word.length);
    }

    //nested states (the prop rule) are settings of the road, not of one of its ends
    static bool IsEndValue(object value) {
        return !(value is ObjectState) && !(value is ObjectState[]);
    }

    //the properties of an end of the road (from) are given to an end of the merged road (to): the old ones are removed
    static void CopyEndProperties(ObjectState from, string fromSide, ObjectState to, string toSide) {
        foreach (var key in new List<string>(to.properties.Keys)) {
            var word = GetEndWord(key);
            if (word.HasValue && word.Value.side == toSide && IsEndValue(to.properties[key])) to.RemoveProperty(key);
        }
        var source = (ObjectState)from.Clone(); //the values are deep copies
        foreach (var key in source.properties.Keys) {
            var word = GetEndWord(key);
            if (!word.HasValue || word.Value.side != fromSide || !IsEndValue(source.properties[key])) continue;
            to.SetProperty(fromSide == toSide ? key : GetCounterpart(key, word.Value), source.properties[key]);
        }
    }

    // Merge

    //merges the roads of the intersection into the given one, deleting the other road and the intersection
    public static Road Merge(ElementManager manager, Intersection joint, Road keep) {
        var plan = Prepare(joint, keep);
        var other = plan.other;
        var far = plan.far;
        History.UndoManager.Touch(keep);
        History.UndoManager.Touch(other);
        History.UndoManager.Touch(joint);
        History.UndoManager.Touch(far);

        //the end of the kept road on the intersection becomes the far end of the other road
        var keepSide = plan.keepAtEnd ? "end" : "start";
        var otherSide = plan.otherAtStart ? "end" : "start";
        CopyEndProperties(other.state, otherSide, keep.state, keepSide);
        CopyEndProperties(other.instanceState, otherSide, keep.instanceState, keepSide);

        //the same number of segments for each part (the low-poly curves have one for each control point)
        if (!keep.state.Bool("segmentsPer100m") && keep.GetCurveType() != GH.CurveType.LowPoly) {
            keep.state.SetInt("segments", keep.GetStateSegments() + other.GetStateSegments());
        }

        Operations.Intersections.Delete(joint); //detaches both roads
        if (far != null) {
            var idx = far.roads.IndexOf(other);
            if (idx >= 0) far.roads[idx] = keep;
            else far.roads.Add(keep);
            if (other.startIntersection == far) other.startIntersection = null;
            if (other.endIntersection == far) other.endIntersection = null;
            if (plan.keepAtEnd) keep.endIntersection = far;
            else keep.startIntersection = far;
            far.forceRemesh = true;
        }

        keep.RestorePoints(plan.positions, plan.rotations);
        //the anchors of the road are recreated, so the terrain points anchored to it stay in place
        keep.anchorManager.Invalidate();
        other.Delete();
        manager.worldChanged = true;
        return keep;
    }
}

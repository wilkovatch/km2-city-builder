using System.Collections;
using System.Collections.Generic;
using Godot;
using States;
using GO = GameEngine.GameObject;
using GH = GeometryHelper;

//Splits a road in two at a point of its curve, connecting the two parts with a new intersection
public static class RoadSplitter {
    const float ParamEpsilon = 0.0001f;
    const float IntersectionMargin = 1.0f; //added to the size of an intersection, where control points would fold the road mesh
    const float HermiteTolerance = 0.1f; //distance of a part from the original curve under which no points are added
    const int HermiteMaxAddedPoints = 3; //for each part
    const int HermiteSamplesPerSpan = 8; //of a part, compared to the original curve
    const int HermiteOriginalSamples = 300; //of the original curve along a part
    const int HermiteSearchWindow = 25; //original curve segments searched around the previous closest one
    const float HermiteMinKnotDistance = 0.001f; //between the points of a part (curve parameter)

    //A part of a split road, whose control points are finished once the sizes of the intersections at its ends are known
    //(the road mesh starts only at the intersection size from its center, control points closer than that would fold it)
    public class Part {
        public Road road;
        public GH.CurveType curveType;
        public List<Vector3> original; //the control points of the road before the splits (Hermite)
        public List<Vector2> originalRotations; //and their rotations (yaw, roll)
        public float tension;
        public bool subdivideEqually;
        public bool projected; //the control points are projected on the terrain, only their horizontal positions matter
        public float from, to; //the curve parameters of the ends of the part on the original curve (Hermite)
        public bool startIsCut, endIsCut; //which ends of the part are on a new intersection
    }

    class SplitPoints {
        public List<Vector3> first, second; //the control points of the two parts
        public List<Vector2> firstRotations, secondRotations; //their rotations (yaw, roll)
        public Vector3 point; //where the parts meet
        public Part firstPart, secondPart; //null for Bézier curves, split exactly
    }

    //the curve parameter of a point of the curve of the road (curve segment and position in it)
    static float GetCurveParameter(Road road, int segment, float fraction) {
        var count = road.curvePoints.Count;
        if (count < 2) return -1;
        return (segment + fraction) / (count - 1.0f);
    }

    //the point of the curve of the road closest to the given one (curve segment, position in it and distance)
    public static (int segment, float fraction, float distance) GetClosestCurvePosition(Road road, Vector3 point) {
        var curve = road.curvePoints;
        if (curve.Count < 2) return (-1, 0, float.MaxValue);
        var closest = GH.ClosestPointToCurve(point, curve, out var minI);
        if (minI < 0) return (-1, 0, float.MaxValue);
        var fraction = GH.ClosestPointFactor(point, curve[minI], curve[minI + 1]);
        return (minI, fraction, GameEngine.Vector3.Distance(point, closest));
    }

    static Vector3 Evaluate(List<Vector3> controlPoints, float alpha, int alphaI, GH.CurveType curveType, float tension, bool subdivideEqually) {
        var cps = controlPoints.GetRange(1, controlPoints.Count - 2);
        return GH.GetPointOnCurve(controlPoints[0], cps, controlPoints[controlPoints.Count - 1], alpha, alphaI, curveType, tension, subdivideEqually);
    }

    static Vector2 EvaluateRotation(List<Vector3> controlPoints, List<Vector2> rotations, float alpha, GH.CurveType curveType, float tension, bool subdivideEqually) {
        var cps = controlPoints.GetRange(1, controlPoints.Count - 2);
        return GH.GetValueOnCurve(controlPoints[0], cps, controlPoints[controlPoints.Count - 1], rotations, alpha, 0, curveType, tension, subdivideEqually);
    }

    static List<Vector3> Pack(List<Vector2> values) {
        return values.ConvertAll(v => new Vector3(v.X, v.Y, 0));
    }

    static List<Vector2> Unpack(List<Vector3> values) {
        return values.ConvertAll(v => new Vector2(v.X, v.Y));
    }

    static float HorizontalDistance(Vector3 a, Vector3 b) {
        return new Vector2(a[0] - b[0], a[2] - b[2]).Length();
    }

    //the curve parameter of the point of the curve between from and to closest to the given one
    static float GetClosestParameter(List<Vector3> controlPoints, float from, float to, Vector3 point, float tension, bool subdivideEqually) {
        var samples = new List<Vector3>();
        for (int i = 0; i <= HermiteOriginalSamples; i++) {
            samples.Add(Evaluate(controlPoints, Mathf.Lerp(from, to, i / (float)HermiteOriginalSamples), 0, GH.CurveType.Hermite, tension, subdivideEqually));
        }
        GH.ClosestPointToCurve(point, samples, out var minI);
        if (minI < 0) return -1;
        var f = GH.ClosestPointFactor(point, samples[minI], samples[minI + 1]);
        return Mathf.Lerp(from, to, (minI + f) / HermiteOriginalSamples);
    }

    //splits the control points: exactly for Bézier and low-poly curves, for Hermite curves the parts get the control
    //points of the original curve on their side (and others are added later, see FinishPart)
    static SplitPoints GetSplitPoints(Road road, Part previousPart, int segment, float fraction) {
        var controlPoints = new List<Vector3>();
        foreach (var p in road.points) controlPoints.Add(GO.GetPosition(p));
        if (controlPoints.Count < 2) return null;
        var rotations = road.GetPointRotations();
        var curveType = road.GetCurveType();
        var tension = road.state.Float("hermiteTension", 0.5f);
        var subdivideEqually = road.state.Bool("subdivideEqually");
        var t = GetCurveParameter(road, segment, fraction);
        if (t <= ParamEpsilon || t >= 1.0f - ParamEpsilon) return null;

        if (curveType == GH.CurveType.Bezier) {
            var (left, right) = GH.SplitBezier(controlPoints, t);
            var (leftRotations, rightRotations) = GH.SplitBezier(Pack(rotations), t);
            return new SplitPoints { first = left, second = right, point = left[left.Count - 1], firstRotations = Unpack(leftRotations), secondRotations = Unpack(rightRotations) };
        }

        var lowPoly = curveType == GH.CurveType.LowPoly;
        //a part of a road split by the same action is split on the original curve, so that the errors don't add up
        var fromOriginal = !lowPoly && previousPart != null && previousPart.curveType == curveType;
        var source = fromOriginal ? previousPart.original : controlPoints;
        var sourceRotations = fromOriginal ? previousPart.originalRotations : rotations;
        var from = fromOriginal ? previousPart.from : 0.0f;
        var to = fromOriginal ? previousPart.to : 1.0f;
        if (fromOriginal) {
            var clicked = GameEngine.Vector3.Lerp(road.curvePoints[segment], road.curvePoints[segment + 1], fraction);
            t = GetClosestParameter(source, from, to, clicked, tension, subdivideEqually);
            if (t <= from + ParamEpsilon || t >= to - ParamEpsilon) return null;
        }

        //the low-poly curve points are the control points (and its parameters are their indices)
        if (lowPoly && road.curvePoints.Count != controlPoints.Count) return null;
        var knots = lowPoly ? GetUniformKnots(controlPoints.Count) : GH.GetHermiteKnots(source, subdivideEqually);
        var point = lowPoly
            ? GameEngine.Vector3.Lerp(controlPoints[segment], controlPoints[segment + 1], fraction)
            : Evaluate(source, t, 0, curveType, tension, subdivideEqually);
        var pointRotation = lowPoly
            ? rotations[segment].Lerp(rotations[segment + 1], fraction)
            : EvaluateRotation(source, sourceRotations, t, curveType, tension, subdivideEqually);
        var first = new List<Vector3> { controlPoints[0] };
        var second = new List<Vector3>();
        var firstRotations = new List<Vector2> { rotations[0] };
        var secondRotations = new List<Vector2>();
        for (int i = 0; i < source.Count; i++) {
            if (knots[i] <= from + ParamEpsilon || knots[i] >= to - ParamEpsilon) continue; //the ends
            if (knots[i] < t - ParamEpsilon) {
                first.Add(source[i]);
                firstRotations.Add(sourceRotations[i]);
            } else if (knots[i] > t + ParamEpsilon) {
                second.Add(source[i]);
                secondRotations.Add(sourceRotations[i]);
            } else {
                point = source[i]; //the split point is a control point
                pointRotation = sourceRotations[i];
            }
        }
        first.Add(point);
        second.Insert(0, point);
        second.Add(controlPoints[controlPoints.Count - 1]);
        firstRotations.Add(pointRotation);
        secondRotations.Insert(0, pointRotation);
        secondRotations.Add(rotations[rotations.Count - 1]);

        Part NewPart(float partFrom, float partTo, bool startIsCut, bool endIsCut) => new Part {
            curveType = curveType, original = source, originalRotations = sourceRotations, tension = tension, subdivideEqually = subdivideEqually,
            projected = road.state.Bool("project") || road.state.Bool("projectAll"),
            from = partFrom, to = partTo, startIsCut = startIsCut, endIsCut = endIsCut
        };
        return new SplitPoints {
            first = first, second = second, point = point, firstRotations = firstRotations, secondRotations = secondRotations,
            firstPart = NewPart(from, t, previousPart?.startIsCut ?? false, true),
            secondPart = NewPart(t, to, true, previousPart?.endIsCut ?? false)
        };
    }

    static List<float> GetUniformKnots(int count) {
        var res = new List<float>();
        for (int i = 0; i < count; i++) res.Add(i / (count - 1.0f));
        return res;
    }

    static List<Vector3> GetPoints(List<(float knot, Vector3 point)> list) {
        var res = new List<Vector3>();
        foreach (var elem in list) res.Add(elem.point);
        return res;
    }

    //the distance from the center of an intersection at the end of the road where its control points would fold its mesh
    static float GetClearance(Road road, Intersection intersection) {
        if (intersection == null) return 0.0f;
        var idx = intersection.roads.IndexOf(road);
        return (idx >= 0 && idx < intersection.sizes.Count ? intersection.sizes[idx] : 0.0f) + IntersectionMargin;
    }

    //whether a control point of the part (not an end) is inside a new intersection at its ends
    public static bool HasPointsInsideCuts(Part part) {
        var road = part.road;
        if (GO.IsNull(road) || road.deleted || road.points.Count < 3) return false;
        var start = GO.GetPosition(road.points[0]);
        var end = GO.GetPosition(road.points[road.points.Count - 1]);
        var startClearance = GetClearance(road, road.startIntersection) - IntersectionMargin;
        var endClearance = GetClearance(road, road.endIntersection) - IntersectionMargin;
        for (int i = 1; i < road.points.Count - 1; i++) {
            var point = GO.GetPosition(road.points[i]);
            if (part.startIsCut && HorizontalDistance(point, start) < startClearance) return true;
            if (part.endIsCut && HorizontalDistance(point, end) < endClearance) return true;
        }
        return false;
    }

    //finishes the control points of a split road part, now that the sizes of the intersections at its ends are known:
    //the control points inside the new intersections are removed, and points of the original curve are added to the
    //Hermite curves outside the intersections. Returns whether the control points changed.
    public static bool FinishPart(Part part) {
        var road = part.road;
        if (GO.IsNull(road) || road.deleted || road.points.Count < 2) return false;
        var start = GO.GetPosition(road.points[0]);
        var end = GO.GetPosition(road.points[road.points.Count - 1]);
        var startClearance = GetClearance(road, road.startIntersection);
        var endClearance = GetClearance(road, road.endIntersection);
        bool InsideCut(Vector3 point) {
            return (part.startIsCut && HorizontalDistance(point, start) < startClearance) || (part.endIsCut && HorizontalDistance(point, end) < endClearance);
        }

        if (part.curveType == GH.CurveType.LowPoly) {
            var toRemove = new List<StaticBody3D>();
            for (int i = 1; i < road.points.Count - 1; i++) {
                if (InsideCut(GO.GetPosition(road.points[i]))) toRemove.Add(road.points[i]);
            }
            if (toRemove.Count == 0) return false;
            road.RemoveControlPoints(toRemove);
            return true;
        }

        var knots = GH.GetHermiteKnots(part.original, part.subdivideEqually);
        var points = new List<(float knot, Vector3 point)> { (part.from, start) };
        for (int i = 0; i < part.original.Count; i++) {
            if (knots[i] <= part.from + ParamEpsilon || knots[i] >= part.to - ParamEpsilon || InsideCut(part.original[i])) continue;
            points.Add((knots[i], part.original[i]));
        }
        points.Add((part.to, end));
        //a Hermite curve through two points is a straight line, split exactly
        if (part.original.Count > 2) RefineHermitePart(points, startClearance, endClearance, part.original, part.tension, part.subdivideEqually, part.projected);
        var currentRotations = road.GetPointRotations();
        var rotations = new List<Vector2>();
        for (int i = 0; i < points.Count; i++) {
            if (i == 0) rotations.Add(currentRotations[0]);
            else if (i == points.Count - 1) rotations.Add(currentRotations[currentRotations.Count - 1]);
            else rotations.Add(EvaluateRotation(part.original, part.originalRotations, points[i].knot, GH.CurveType.Hermite, part.tension, part.subdivideEqually));
        }
        road.RestorePoints(GetPoints(points), rotations);
        return true;
    }

    //the largest distance of a curve from the original one (sampled in order along it), outside the circles around its
    //ends: the closest point is searched around the previous one, since both curves go the same way
    static float GetDeviation(List<Vector3> points, List<Vector3> original, float startRadius, float endRadius, float tension, bool subdivideEqually, bool horizontal) {
        //every span is sampled, however short (short spans can make loops)
        var knots = GH.GetHermiteKnots(points, subdivideEqually);
        var alphas = new List<float>();
        for (int i = 0; i < points.Count - 1; i++) {
            for (int j = 0; j < HermiteSamplesPerSpan; j++) alphas.Add(Mathf.Lerp(knots[i], knots[i + 1], j / (float)HermiteSamplesPerSpan));
        }
        alphas.Add(1.0f);
        var start = points[0];
        var end = points[points.Count - 1];
        var res = 0.0f;
        var lastI = 0;
        for (int i = 0; i < alphas.Count; i++) {
            var sample = Evaluate(points, alphas[i], i, GH.CurveType.Hermite, tension, subdivideEqually);
            if (horizontal) sample[1] = 0.0f;
            var minDist = float.MaxValue;
            var minI = lastI;
            var from = Mathf.Max(0, lastI - HermiteSearchWindow);
            var to = Mathf.Min(original.Count - 2, lastI + HermiteSearchWindow);
            for (int j = from; j <= to; j++) {
                var dist = GameEngine.Vector3.SqrMagnitude(sample - GH.ClosestPoint(sample, original[j], original[j + 1]));
                if (dist < minDist) {
                    minDist = dist;
                    minI = j;
                }
            }
            lastI = minI;
            //the parts inside the intersections are not shown
            if (HorizontalDistance(sample, start) < startRadius || HorizontalDistance(sample, end) < endRadius) continue;
            res = Mathf.Max(res, Mathf.Sqrt(minDist));
        }
        return res;
    }

    //a Hermite curve can't be split exactly (the tangents depend on the neighboring points): up to a few points of the
    //original curve are added to the part where they bring it closest to the original curve, outside the circles around
    //its ends (the intersections, plus a margin), and comparing only the parts outside the intersections
    static void RefineHermitePart(List<(float knot, Vector3 point)> part, float startRadius, float endRadius,
        List<Vector3> controlPoints, float tension, bool subdivideEqually, bool horizontal) {

        var startHidden = Mathf.Max(0.0f, startRadius - IntersectionMargin);
        var endHidden = Mathf.Max(0.0f, endRadius - IntersectionMargin);

        var from = part[0].knot;
        var to = part[part.Count - 1].knot;
        var start = part[0].point;
        var end = part[part.Count - 1].point;
        var original = new List<Vector3>();
        for (int i = 0; i <= HermiteOriginalSamples; i++) {
            var point = Evaluate(controlPoints, Mathf.Lerp(from, to, i / (float)HermiteOriginalSamples), 0, GH.CurveType.Hermite, tension, subdivideEqually);
            if (horizontal) point[1] = 0.0f;
            original.Add(point);
        }
        //the range of curve parameters outside the circles
        var minI = original.FindIndex(x => HorizontalDistance(x, start) >= startRadius);
        var maxI = original.FindLastIndex(x => HorizontalDistance(x, end) >= endRadius);
        if (minI < 0 || maxI < 0) return;
        var minKnot = Mathf.Max(Mathf.Lerp(from, to, minI / (float)HermiteOriginalSamples), from + HermiteMinKnotDistance);
        var maxKnot = Mathf.Min(Mathf.Lerp(from, to, maxI / (float)HermiteOriginalSamples), to - HermiteMinKnotDistance);
        if (maxKnot - minKnot < HermiteMinKnotDistance) return;

        List<(float knot, Vector3 point)> WithExtraPoints(List<float> extra) {
            var res = new List<(float knot, Vector3 point)>(part);
            foreach (var knot in extra) {
                res.Add((knot, Evaluate(controlPoints, knot, 0, GH.CurveType.Hermite, tension, subdivideEqually)));
            }
            res.Sort((a, b) => a.knot.CompareTo(b.knot));
            return res;
        }
        float Cost(List<float> extra) {
            var points = WithExtraPoints(extra);
            for (int i = 0; i < points.Count - 1; i++) {
                if (points[i + 1].knot - points[i].knot < HermiteMinKnotDistance) return float.MaxValue;
            }
            return GetDeviation(GetPoints(points), original, startHidden, endHidden, tension, subdivideEqually, horizontal);
        }
        //coordinate descent on the curve parameters of the extra points
        (float cost, List<float> extra) Descend(List<float> extra) {
            var cost = Cost(extra);
            var step = (maxKnot - minKnot) / 8.0f;
            while (step > HermiteMinKnotDistance * 0.2f) {
                var improved = false;
                for (int i = 0; i < extra.Count; i++) {
                    foreach (var delta in new[] { step, -step }) {
                        var moved = new List<float>(extra);
                        moved[i] = Mathf.Clamp(moved[i] + delta, minKnot, maxKnot);
                        var movedCost = Cost(moved);
                        if (movedCost < cost) {
                            extra = moved;
                            cost = movedCost;
                            improved = true;
                        }
                    }
                }
                if (!improved) step *= 0.5f;
            }
            return (cost, extra);
        }

        var best = (cost: Cost(new List<float>()), extra: new List<float>());
        if (best.cost <= HermiteTolerance) return;
        var gaps = new List<(float size, float middle)>();
        for (int i = 0; i < part.Count - 1; i++) gaps.Add((part[i + 1].knot - part[i].knot, (part[i].knot + part[i + 1].knot) * 0.5f));
        gaps.Sort((a, b) => b.size.CompareTo(a.size));
        for (int n = 1; n <= HermiteMaxAddedPoints && best.cost > HermiteTolerance; n++) {
            //starting from points closer and closer to each end, evenly spaced points and the middles of the largest gaps
            var starts = new List<List<float>> { new List<float>(), new List<float>(), new List<float>() };
            for (int i = 0; i < n; i++) {
                var graded = 0.9f * Mathf.Pow(0.5f, n - 1 - i);
                starts[0].Add(Mathf.Lerp(maxKnot, minKnot, graded));
                starts[1].Add(Mathf.Lerp(minKnot, maxKnot, graded));
                starts[2].Add(Mathf.Lerp(minKnot, maxKnot, (i + 1.0f) / (n + 1.0f)));
            }
            if (gaps.Count >= n) {
                starts.Add(new List<float>());
                for (int i = 0; i < n; i++) starts[3].Add(Mathf.Clamp(gaps[i].middle, minKnot, maxKnot));
            }
            foreach (var startKnots in starts) {
                var res = Descend(startKnots);
                if (res.cost < best.cost) best = res;
            }
        }
        var refined = WithExtraPoints(best.extra);
        part.Clear();
        part.AddRange(refined);
    }

    static void ClearIntersectionFlags(ObjectState instanceState, string prefix) {
        foreach (var name in new[] { prefix + "IntersectionStart", prefix + "IntersectionEnd" }) {
            if (instanceState.Bool(name)) instanceState.SetBool(name, false);
        }
    }

    //splits the road at a point of its curve (curve segment and position in it): the road keeps the first part,
    //a new road with the same properties gets the second one, and a new seamless intersection connects them
    public static (Intersection intersection, Road newRoad) Split(ElementManager manager, Road road, int segment, float fraction) {
        var split = GetSplitPoints(road, manager.GetSplitPart(road), segment, fraction);
        if (split == null) return (null, null);
        var endIntersection = road.endIntersection;
        History.UndoManager.Touch(road);
        History.UndoManager.Touch(endIntersection);

        var newRoad = manager.CreateRoad();
        newRoad.SetState((ObjectState)road.state.Clone());
        newRoad.instanceState = (ObjectState)road.instanceState.Clone();
        for (int i = 0; i < split.second.Count; i++) newRoad.AddPoint(split.second[i], -1, split.secondRotations[i]);
        road.RestorePoints(split.first, split.firstRotations);

        //the through road flags of the old end go to the new road, the new intersection has none
        ClearIntersectionFlags(road.instanceState, "end");
        ClearIntersectionFlags(newRoad.instanceState, "start");

        if (endIntersection != null) {
            var idx = endIntersection.roads.LastIndexOf(road);
            if (idx >= 0) endIntersection.roads[idx] = newRoad;
            else endIntersection.roads.Add(newRoad);
            newRoad.endIntersection = endIntersection;
            endIntersection.forceRemesh = true;
        }

        //the two parts are the same road: they join seamlessly
        var intersection = manager.CreateIntersection(split.point);
        intersection.instanceState.SetBool("seamless", true);
        intersection.roads.Add(road);
        intersection.roads.Add(newRoad);
        road.endIntersection = intersection;
        newRoad.startIntersection = intersection;

        //the anchors of the road are recreated (not moved along the shorter road), so the terrain points anchored to it stay in place
        road.anchorManager.Invalidate();
        if (split.firstPart != null) {
            split.firstPart.road = road;
            split.secondPart.road = newRoad;
            manager.SetSplitPart(road, split.firstPart);
            manager.SetSplitPart(newRoad, split.secondPart);
        }
        manager.worldChanged = true;
        return (intersection, newRoad);
    }
}

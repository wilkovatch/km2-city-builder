using System.Collections.Generic;
using Godot;
using GS = GameEngine.Scriptable;

namespace Operations {
    //result of the automatic closing of terrain patches and building lines
    public enum AutoCloseResult {
        NoPresets = -2, //building lines only: no building presets to split the line with
        CannotClose = -1, //the element must have only its first point, on an anchor
        NoLoopFound = 0,
        Closed = 1
    }

    public static class TerrainPatches {
        public static TerrainPatch Create(ElementManager manager) {
            var name = "Terrain " + Actions.Helpers.GetLatestObjectNumber(manager.GetTerrainContainer(), "Terrain ");
            var patch = GS.Create<TerrainPatch>(name);
            patch.Initialize(manager.GetTerrainPointContainer(), manager.GetTerrainContainer());
            manager.AddPatch(patch);
            return patch;
        }

        //a patch with less than three perimeter points is incomplete: it is deleted when its editor is closed
        public static bool IsIncomplete(TerrainPatch patch) {
            return patch != null && patch.GetPerimeterPointCount() < 3;
        }

        //obj is what was clicked: an anchor, an existing terrain point, a point link or nothing (the terrain).
        //pointOnly: false follows the anchors from the previous anchor to the clicked one
        public static TerrainPoint AddPerimeterPoint(TerrainPatch patch, Node3D obj, Vector3 point, bool pointOnly) {
            return patch.AddPerimeterPoint(obj, point, pointOnly);
        }

        public static void AddInternalPoint(TerrainPatch patch, Node3D obj, Vector3 point) {
            patch.AddInternalPoint(obj, point);
        }

        public static void RemoveLastPerimeterPoint(TerrainPatch patch) {
            patch.RemovePerimeterPoint();
        }

        //removes a perimeter or internal point from the patch (the point is deleted if no other element uses it)
        public static bool RemovePoint(TerrainPatch patch, TerrainPoint point) {
            return point.DeleteManual(patch);
        }

        public static AutoCloseResult AutoClose(TerrainPatch patch) {
            return AutoClose(patch, out _);
        }

        //anchors: the anchors of the loop the perimeter was closed with
        public static AutoCloseResult AutoClose(TerrainPatch patch, out List<TerrainAnchor> anchors) {
            var res = patch.AutoClose();
            anchors = res.points ?? new List<TerrainAnchor>();
            return (AutoCloseResult)res.resultCode;
        }

        //fills the patch with internal points at the distance from each other and from the perimeter (only if it has
        //none yet); returns the new points, null if the patch already has internal points
        public static List<TerrainPoint> GenerateInternalPoints(TerrainPatch patch, float distance) {
            if (patch.GetInternalPointsComponents().Count > 0) return null;
            History.UndoManager.Touch(patch);
            patch.AutoGenerateInternalPoints(distance);
            return patch.GetInternalPointsComponents();
        }

        public static void Delete(TerrainPatch patch) {
            patch.Delete();
        }

        //inserts a free point in the perimeter, in the segment closest to the position (or before the point at the
        //index); null if the patch has less than two points (or the index is out of range)
        public static TerrainPoint InsertPerimeterPoint(TerrainPatch patch, Vector3 position, int? index = null) {
            var at = index ?? patch.ClosestPerimeterInsertIndex(position);
            return at < 0 ? null : patch.InsertPerimeterPoint(at, position);
        }

        //a click on a segment of the perimeter of the patch being edited (obj: what was clicked) inserts a free point
        //there, giving the segment a new corner; null if the click is not on one (it is then a point to add, see
        //AddPerimeterPoint)
        public static TerrainPoint InsertOnClickedSegment(TerrainPatch patch, Node3D obj, Vector3 point) {
            var index = AddTerrainPointToSequence.ClickedSegmentInsertIndex(patch.GetPerimeterPointsComponents(), obj, ref point);
            return index < 0 ? null : patch.InsertPerimeterPoint(index, point);
        }

        // Cuts along a line drawn in the patch

        const float CutTolerance = 0.01f; //closer than this (horizontally) to the perimeter, or to another point, is on it

        static Vector2 Flat(Vector3 v) {
            return new Vector2(v.X, v.Z);
        }

        //where the segment a-b meets the segment c-d (horizontally, touching included), as the fraction of a-b: for
        //segments on the same line, the middle of the part they share. -1 if they don't meet
        static float SegmentHit(Vector3 a3, Vector3 b3, Vector3 c3, Vector3 d3) {
            Vector2 a = Flat(a3), b = Flat(b3), c = Flat(c3), d = Flat(d3);
            var r = b - a;
            var s = d - c;
            var rl = r.Length();
            var sl = Mathf.Max(s.Length(), 1e-6f);
            if (rl < 1e-6f) return -1;
            var denominator = r.Cross(s);
            if (Mathf.Abs(denominator) < 1e-6f * rl * sl) {
                if (Mathf.Abs(r.Cross(c - a)) / rl > CutTolerance) return -1;
                var t0 = (c - a).Dot(r) / (rl * rl);
                var t1 = (d - a).Dot(r) / (rl * rl);
                var from = Mathf.Max(Mathf.Min(t0, t1), 0);
                var to = Mathf.Min(Mathf.Max(t0, t1), 1);
                return from > to + CutTolerance / rl ? -1 : Mathf.Clamp((from + to) * 0.5f, 0, 1);
            }
            var t = (c - a).Cross(s) / denominator;
            var u = (c - a).Cross(r) / denominator;
            if (t < -CutTolerance / rl || t > 1 + CutTolerance / rl || u < -CutTolerance / sl || u > 1 + CutTolerance / sl) return -1;
            return Mathf.Clamp(t, 0, 1);
        }

        //whether the segment a-b meets one of the polyline (a loop if closed) only within the tolerance of its ends
        //where allowed (atStart, atEnd); skip: whether to ignore the segment of the polyline from the point at the index
        static bool MeetsOnlyAtEnds(Vector3 a, Vector3 b, List<Vector3> line, bool closed, bool atStart, bool atEnd, System.Func<int, bool> skip = null) {
            var length = Flat(a).DistanceTo(Flat(b));
            var count = closed ? line.Count : line.Count - 1;
            for (int k = 0; k < count; k++) {
                if (skip != null && skip(k)) continue;
                var t = SegmentHit(a, b, line[k], line[(k + 1) % line.Count]);
                if (t < 0 || (atStart && t * length <= 2 * CutTolerance) || (atEnd && (1 - t) * length <= 2 * CutTolerance)) continue;
                return false;
            }
            return true;
        }

        //whether the position is inside the polygon, farther than the tolerance from its sides
        static bool StrictlyInside(Vector3 position, List<Vector3> polygon) {
            return GeometryHelper.IsPointInsidePolygon(position, polygon) && GeometryHelper.PointPolygonSquaredDistance(position, polygon) > CutTolerance * CutTolerance;
        }

        //why the line can't cut the patch with the perimeter (null if it can): its points on the perimeter (onPerimeter)
        //are where it may meet it, the others must be inside it, its segments must stay inside it and not cross each
        //other or turn back on themselves (closed: the last point is joined to the first one)
        static string LineError(List<Vector3> perimeter, List<Vector3> line, bool closed, System.Func<int, bool> onPerimeter) {
            for (int i = 0; i < line.Count; i++) {
                if (!onPerimeter(i) && !StrictlyInside(line[i], perimeter)) return "the points of the line must be inside the terrain patch";
                for (int j = 0; j < i; j++) {
                    if (Flat(line[i]).DistanceSquaredTo(Flat(line[j])) <= CutTolerance * CutTolerance) return "the line can't pass twice on the same place";
                }
            }
            var segments = closed ? line.Count : line.Count - 1;
            for (int i = 0; i < segments; i++) {
                var next = (i + 1) % line.Count;
                var a = line[i];
                var b = line[next];
                if (!StrictlyInside((a + b) * 0.5f, perimeter) || !MeetsOnlyAtEnds(a, b, perimeter, true, onPerimeter(i), onPerimeter(next))) {
                    return "the line must stay inside the terrain patch";
                }
                for (int j = 0; j < i; j++) {
                    var c = line[j];
                    var d = line[(j + 1) % line.Count];
                    if (j == i - 1 || (closed && j == 0 && i == segments - 1)) {
                        //joined segments only share their point: the line must not go back along itself
                        var shared = j == i - 1 ? a : b;
                        var u = Flat((j == i - 1 ? c : d) - shared);
                        var v = Flat((j == i - 1 ? b : a) - shared);
                        if (u.Dot(v) > 0 && Mathf.Abs(u.Cross(v)) <= CutTolerance * Mathf.Max(u.Length(), v.Length())) return "the line can't turn back on itself";
                    } else if (SegmentHit(a, b, c, d) >= 0) {
                        return "the line can't cross itself";
                    }
                }
            }
            return null;
        }

        //the place of a position of the perimeter along it: the index of the point before it plus the fraction of the
        //segment to the next one
        static float PerimeterOrder(List<Vector3> perimeter, Vector3 position) {
            var best = float.MaxValue;
            var order = 0f;
            for (int i = 0; i < perimeter.Count; i++) {
                var a = perimeter[i];
                var ab = Flat(perimeter[(i + 1) % perimeter.Count] - a);
                var t = ab.LengthSquared() < 1e-8f ? 0 : Mathf.Clamp(Flat(position - a).Dot(ab) / ab.LengthSquared(), 0, 1);
                var distance = (Flat(a) + ab * t).DistanceSquaredTo(Flat(position));
                if (distance >= best) continue;
                best = distance;
                order = t < 1e-4f ? i : t > 1 - 1e-4f ? (i + 1) % perimeter.Count : i + t;
            }
            return order;
        }

        //whether two places of the perimeter (see PerimeterOrder) have points between them on both sides, as the two ends
        //of a straight cut in two parts with three points or more each
        static bool SeparatedOnPerimeter(float a, float b, int count) {
            if (Mathf.Abs(a - b) < 1e-4f) return false;
            bool PointBetween(float from, float to) {
                var end = to > from ? to : to + count;
                return Mathf.Floor(from) + 1 < end - 1e-4f;
            }
            return PointBetween(a, b) && PointBetween(b, a);
        }

        //null if the patch can be cut along the line, otherwise why not. With onPerimeter its first point is on the
        //perimeter, and, if the line is open, its last one too: it splits the patch in two (see Split). A closed line
        //carves the polygon it makes out of the patch (see Carve), with onPerimeter from its first point. The other
        //points must be inside the patch. complete: false checks only the line drawn so far (no end yet, see the patch
        //editor)
        public static string GetCutError(TerrainPatch patch, List<Vector3> line, bool onPerimeter, bool closed, bool complete = true) {
            if (patch == null || patch.IsDeleted()) return "the terrain patch was deleted";
            var perimeter = patch.GetPerimeterPoints();
            if (perimeter.Count < 3) return "the terrain patch has less than three perimeter points";
            if (line == null || line.Count == 0) return "the line has no points";
            var last = line.Count - 1;
            var error = LineError(perimeter, line, complete && closed, i => onPerimeter && (i == 0 || (complete && !closed && i == last)));
            if (error != null || !complete) return error;
            if (closed) {
                if (line.Count < 3) return "a carved polygon needs three points or more";
                if (FindBridges(perimeter, line, onPerimeter) == null) return "no cuts were found to split the terrain patch left around the polygon in two";
            } else {
                if (!onPerimeter) return "the line must start and end on the perimeter of the terrain patch";
                if (line.Count < 2) return "the line needs two points or more";
                if (line.Count == 2 && !SeparatedOnPerimeter(PerimeterOrder(perimeter, line[0]), PerimeterOrder(perimeter, line[1]), perimeter.Count)) {
                    return "the two points must not be next to each other";
                }
            }
            return null;
        }

        //a cut from the point of a carved loop at the index (hole) to the perimeter: to one of its points (vertex), or to
        //a position on one of its segments (segment), where a point is inserted
        class Bridge {
            public int hole;
            public int vertex = -1;
            public int segment = -1;
            public Vector3 position;
            public float length;
        }

        //the cuts from a loop carved in the patch to its perimeter that split the ring left around the loop in two: one
        //when the first point of the loop is on the perimeter (touching), as far as possible from it along the loop,
        //otherwise two, the shortest one and the one as far as possible from it. Cuts to the points of the perimeter are
        //preferred to cuts to positions on its segments. Null if they can't be found
        static List<Bridge> FindBridges(List<Vector3> perimeter, List<Vector3> loop, bool touching) {
            var toPoints = new List<Bridge>();
            var toSegments = new List<Bridge>();
            for (int h = touching ? 1 : 0; h < loop.Count; h++) {
                for (int i = 0; i < perimeter.Count; i++) {
                    toPoints.Add(new Bridge { hole = h, vertex = i, position = perimeter[i] });
                    var a = perimeter[i];
                    var b = perimeter[(i + 1) % perimeter.Count];
                    var ab = Flat(b - a);
                    if (ab.LengthSquared() < 1e-8f) continue;
                    var t = Flat(loop[h] - a).Dot(ab) / ab.LengthSquared();
                    var offset = t * ab.Length();
                    if (offset <= 2 * CutTolerance || offset >= ab.Length() - 2 * CutTolerance) continue; //(a point of the perimeter)
                    toSegments.Add(new Bridge { hole = h, segment = i, position = a.Lerp(b, t) });
                }
            }
            bool Fits(Bridge bridge) {
                var from = loop[bridge.hole];
                bridge.length = Flat(from).DistanceTo(Flat(bridge.position));
                if (bridge.length <= 2 * CutTolerance) return false;
                if (touching && Flat(bridge.position).DistanceSquaredTo(Flat(loop[0])) <= CutTolerance * CutTolerance) return false;
                var middle = (from + bridge.position) * 0.5f;
                //from the loop through the ring around it, not through the polygon carved
                if (!StrictlyInside(middle, perimeter) || GeometryHelper.IsPointInsidePolygon(middle, loop)
                    || GeometryHelper.PointPolygonSquaredDistance(middle, loop) <= CutTolerance * CutTolerance) return false;
                if (!MeetsOnlyAtEnds(from, bridge.position, perimeter, true, false, true)) return false;
                //the two segments of the loop from its point meet the cut where it starts
                var previous = (bridge.hole + loop.Count - 1) % loop.Count;
                if (!MeetsOnlyAtEnds(from, bridge.position, loop, true, true, false, k => k != bridge.hole && k != previous)) return false;
                return MeetsOnlyAtEnds(from, bridge.position, loop, true, false, false, k => k == bridge.hole || k == previous);
            }
            toPoints.RemoveAll(bridge => !Fits(bridge));
            toSegments.RemoveAll(bridge => !Fits(bridge));
            //how far apart two points of the loop are along it (0 to 0.5 of its length)
            var along = new float[loop.Count + 1];
            for (int i = 0; i < loop.Count; i++) along[i + 1] = along[i] + Flat(loop[i]).DistanceTo(Flat(loop[(i + 1) % loop.Count]));
            float Apart(int a, int b) {
                var distance = Mathf.Abs(along[a] - along[b]) / along[loop.Count];
                return Mathf.Min(distance, 1 - distance);
            }
            //the shortest cut of the ones farthest from the first one along the loop (from), as far apart at a quarter
            //less than the farthest
            Bridge Opposite(List<Bridge> candidates, int from, Bridge first) {
                var valid = candidates.FindAll(c => c.hole != from && (first == null || (Flat(c.position).DistanceSquaredTo(Flat(first.position)) > CutTolerance * CutTolerance
                    && SegmentHit(loop[c.hole], c.position, loop[first.hole], first.position) < 0)));
                if (valid.Count == 0) return null;
                var farthest = 0f;
                foreach (var c in valid) farthest = Mathf.Max(farthest, Apart(c.hole, from));
                Bridge best = null;
                foreach (var c in valid) {
                    if (Apart(c.hole, from) >= farthest * 0.75f && (best == null || c.length < best.length)) best = c;
                }
                return best;
            }
            var res = new List<Bridge>();
            Bridge firstBridge = null;
            if (!touching) {
                foreach (var c in toPoints.Count > 0 ? toPoints : toSegments) {
                    if (firstBridge == null || c.length < firstBridge.length) firstBridge = c;
                }
                if (firstBridge == null) return null;
                res.Add(firstBridge);
            }
            var second = Opposite(toPoints, touching ? 0 : firstBridge.hole, firstBridge) ?? Opposite(toSegments, touching ? 0 : firstBridge.hole, firstBridge);
            if (second == null) return null;
            res.Add(second);
            return res;
        }

        //a free point for a cut, used by no patch yet
        static TerrainPoint NewPoint(ElementManager manager, Vector3 position) {
            return TerrainPoint.Create(position, null, manager.GetTerrainPointContainer());
        }

        //gives the parts of the perimeter to the patch (the first one) and to new patches with a copy of its state (the
        //others), with each internal point in the part it is in (the first one if none); returns the new patches
        static List<TerrainPatch> Divide(ElementManager manager, TerrainPatch patch, List<List<TerrainPoint>> parts) {
            var polygons = new List<List<Vector3>>();
            var internals = new List<List<TerrainPoint>>();
            foreach (var part in parts) {
                var polygon = new List<Vector3>();
                foreach (var point in part) polygon.Add(point.GetPoint());
                polygons.Add(polygon);
                internals.Add(new List<TerrainPoint>());
            }
            foreach (var point in patch.GetInternalPointsComponents()) {
                var index = polygons.FindIndex(1, polygon => GeometryHelper.IsPointInsidePolygon(point.GetPoint(), polygon));
                internals[index < 0 ? 0 : index].Add(point);
            }
            History.UndoManager.Touch(patch);
            var res = new List<TerrainPatch>();
            //the new patches first: the points they get always belong to a patch
            for (int i = 1; i < parts.Count; i++) {
                var newPatch = Create(manager);
                newPatch.state = (ObjectState)patch.state.Clone();
                newPatch.SetPoints(parts[i], internals[i]);
                res.Add(newPatch);
            }
            patch.SetPoints(parts[0], internals[0]);
            manager.FlagAsChanged();
            return res;
        }

        //null if the patch can be split along the line from a to b, two points of its perimeter, through the positions
        //(inside the patch, none for a straight line), otherwise why not
        public static string GetSplitError(TerrainPatch patch, TerrainPoint a, TerrainPoint b, List<Vector3> through = null) {
            if (patch == null || patch.IsDeleted()) return "the terrain patch was deleted";
            var perimeter = patch.GetPerimeterPointsComponents();
            if (a == null || b == null || !perimeter.Contains(a) || !perimeter.Contains(b)) return "both points must be on the perimeter of the terrain patch";
            if (a == b) return "the two points must be different (a line back to the same point carves a polygon)";
            var line = new List<Vector3> { a.GetPoint() };
            if (through != null) line.AddRange(through);
            line.Add(b.GetPoint());
            return GetCutError(patch, line, true, false);
        }

        //splits the patch along the line from a to b, two points of its perimeter, through the positions (inside the
        //patch, none for a straight line): the patch keeps the perimeter from b to a, a new patch with the same
        //properties gets the one from a to b. The points of the line (free points added at the positions) are shared by
        //both patches (they stay joined when one moves), and each internal point goes to the patch it is in. Returns the
        //new patch (null if it can't be split, see GetSplitError)
        public static TerrainPatch Split(ElementManager manager, TerrainPatch patch, TerrainPoint a, TerrainPoint b, List<Vector3> through = null) {
            if (GetSplitError(patch, a, b, through) != null) return null;
            History.UndoManager.Touch(patch);
            var perimeter = patch.GetPerimeterPointsComponents();
            List<TerrainPoint> Part(int from, int to) {
                var res = new List<TerrainPoint>();
                for (int i = from; ; i = (i + 1) % perimeter.Count) {
                    res.Add(perimeter[i]);
                    if (i == to) break;
                }
                return res;
            }
            var cut = new List<TerrainPoint>();
            if (through != null) {
                foreach (var position in through) cut.Add(NewPoint(manager, position));
            }
            var kept = Part(perimeter.IndexOf(b), perimeter.IndexOf(a));
            kept.AddRange(cut);
            var moved = Part(perimeter.IndexOf(a), perimeter.IndexOf(b));
            for (int i = cut.Count - 1; i >= 0; i--) moved.Add(cut[i]);
            return Divide(manager, patch, new List<List<TerrainPoint>> { kept, moved })[0];
        }

        //null if the polygon can be carved out of the patch (see Carve), otherwise why not
        public static string GetCarveError(TerrainPatch patch, List<Vector3> outline, TerrainPoint at = null) {
            if (patch == null || patch.IsDeleted()) return "the terrain patch was deleted";
            if (at != null && !patch.GetPerimeterPointsComponents().Contains(at)) return "the point must be on the perimeter of the terrain patch";
            if (outline == null) return "the polygon has no points";
            var line = new List<Vector3>();
            if (at != null) line.Add(at.GetPoint());
            line.AddRange(outline);
            return GetCutError(patch, line, at != null, true);
        }

        //carves the polygon of the outline (positions inside the patch, from at if given: a point of its perimeter) out
        //of the patch. A new patch with the same properties gets the polygon, and the ring left around it is split in two
        //along the cuts from the polygon to the perimeter chosen by FindBridges (one with at, two otherwise; a point is
        //inserted where one ends on a segment): the patch keeps one half, a new patch gets the other one. The points of
        //the polygon (free points added at the positions) and of the cuts are shared, each internal point goes to the
        //patch it is in. Returns the new patches, the one of the polygon first (null if it can't be carved, see
        //GetCarveError)
        public static List<TerrainPatch> Carve(ElementManager manager, TerrainPatch patch, List<Vector3> outline, TerrainPoint at = null) {
            if (GetCarveError(patch, outline, at) != null) return null;
            var touching = at != null;
            var line = new List<Vector3>();
            if (touching) line.Add(at.GetPoint());
            line.AddRange(outline);
            var perimeterPositions = patch.GetPerimeterPoints();
            var bridges = FindBridges(perimeterPositions, line, touching);
            History.UndoManager.Touch(patch);
            var perimeter = patch.GetPerimeterPointsComponents();
            var ends = new TerrainPoint[bridges.Count];
            for (int i = 0; i < bridges.Count; i++) {
                if (bridges[i].vertex >= 0) ends[i] = perimeter[bridges[i].vertex];
            }
            //the ends on segments, from the last one back so that the indexes of the others stay valid
            var inserted = new List<int>();
            for (int i = 0; i < bridges.Count; i++) {
                if (bridges[i].segment >= 0) inserted.Add(i);
            }
            (int, float) Along(Bridge bridge) => (bridge.segment, Flat(bridge.position - perimeterPositions[bridge.segment]).Length());
            inserted.Sort((x, y) => Along(bridges[y]).CompareTo(Along(bridges[x])));
            foreach (var i in inserted) ends[i] = patch.InsertPerimeterPoint(bridges[i].segment + 1, bridges[i].position);
            var loop = new List<TerrainPoint>();
            if (touching) loop.Add(at);
            foreach (var position in outline) loop.Add(NewPoint(manager, position));
            //the polygon turns as the perimeter, the hole in the ring the other way
            List<TerrainPoint> Reversed(List<TerrainPoint> points) {
                var res = new List<TerrainPoint> { points[0] };
                for (int i = points.Count - 1; i > 0; i--) res.Add(points[i]);
                return res;
            }
            var sameWay = GeometryHelper.IsPolygonClockwise(line) == GeometryHelper.IsPolygonClockwise(perimeterPositions);
            var inner = sameWay ? loop : Reversed(loop);
            var hole = sameWay ? Reversed(loop) : loop;
            //the ring as one outline, joined to the hole along the first cut (at the point the loop shares with the
            //perimeter when touching): the perimeter from the end of the cut, the cut, the hole from its start, the cut back
            var start = touching ? at : ends[0];
            var holeStart = touching ? at : loop[bridges[0].hole];
            var currentPerimeter = patch.GetPerimeterPointsComponents();
            var ring = new List<TerrainPoint>();
            var first = currentPerimeter.IndexOf(start);
            for (int i = 0; i < currentPerimeter.Count; i++) ring.Add(currentPerimeter[(first + i) % currentPerimeter.Count]);
            if (!touching) ring.Add(start);
            var holeFirst = hole.IndexOf(holeStart);
            for (int i = 0; i < hole.Count; i++) ring.Add(hole[(holeFirst + i) % hole.Count]);
            if (!touching) ring.Add(holeStart);
            //the last cut splits it in two
            var from = ring.IndexOf(ends[ends.Length - 1]);
            var to = ring.IndexOf(loop[bridges[bridges.Count - 1].hole]);
            var kept = ring.GetRange(from, to - from + 1);
            var other = ring.GetRange(to, ring.Count - to);
            other.AddRange(ring.GetRange(0, from + 1));
            return Divide(manager, patch, new List<List<TerrainPoint>> { kept, inner, other });
        }

        static bool SamePoint(TerrainPoint a, TerrainPoint b) {
            if (a == b) return true;
            if (a.anchor is TerrainAnchor anchor && !GameEngine.GameObject.IsNull(anchor) && AddTerrainPointToSequence.AnchorEquals(b.anchor, anchor)) return true;
            return GeometryHelper.AreVectorsEqual(a.GetPoint(), b.GetPoint());
        }

        //the perimeter of the two patches merged (null if they can't be, with why not): they must share one
        //continuous part of their perimeters, of two points or more (the same points, points on the same anchors or
        //at the same positions)
        static List<TerrainPoint> MergedPerimeter(TerrainPatch a, TerrainPatch b, out string error) {
            error = null;
            if (a == null || b == null || a.IsDeleted() || b.IsDeleted()) error = "a terrain patch was deleted";
            else if (a == b) error = "a terrain patch can't be merged with itself";
            if (error != null) return null;
            var pa = a.GetPerimeterPointsComponents();
            var pb = b.GetPerimeterPointsComponents();
            var match = new int[pa.Count];
            var shared = 0;
            for (int i = 0; i < pa.Count; i++) {
                match[i] = pb.FindIndex(p => SamePoint(pa[i], p));
                if (match[i] >= 0) shared++;
            }
            if (shared < 2) {
                error = "the terrain patches must share a side (two points or more of their perimeters)";
                return null;
            }
            if (shared == pa.Count || shared == pb.Count) {
                error = "one terrain patch has no points of its own";
                return null;
            }
            //the shared part of a starts where a shared point follows one that isn't
            var start = -1;
            var runs = 0;
            for (int i = 0; i < pa.Count; i++) {
                if (match[i] >= 0 && match[(i + pa.Count - 1) % pa.Count] < 0) {
                    start = i;
                    runs++;
                }
            }
            if (runs != 1) {
                error = "the terrain patches must share a single continuous side";
                return null;
            }
            var res = new List<TerrainPoint>();
            //from the last shared point, along the points of a only, to the first shared one
            for (int t = 0; t <= pa.Count - shared + 1; t++) res.Add(pa[(start + shared - 1 + t) % pa.Count]);
            //then along the points of b only, from the first shared point to the last one
            var isShared = new bool[pb.Count];
            foreach (var j in match) {
                if (j >= 0) isShared[j] = true;
            }
            var first = match[start];
            var step = isShared[(first + 1) % pb.Count] ? pb.Count - 1 : 1;
            var own = 0;
            for (int j = (first + step) % pb.Count; !isShared[j]; j = (j + step) % pb.Count) {
                res.Add(pb[j]);
                own++;
            }
            if (own != pb.Count - shared) {
                error = "the terrain patches must share a single continuous side";
                return null;
            }
            return res;
        }

        //null if the two patches can be merged, otherwise why not
        public static string GetMergeError(TerrainPatch patch, TerrainPatch other) {
            MergedPerimeter(patch, other, out var error);
            return error;
        }

        //merges the other patch into the patch, which keeps its properties: the side they share is removed (see
        //MergedPerimeter), the internal points of both are kept, the other patch is deleted. False if they can't be
        //merged (see GetMergeError)
        public static bool Merge(ElementManager manager, TerrainPatch patch, TerrainPatch other) {
            var perimeter = MergedPerimeter(patch, other, out _);
            if (perimeter == null) return false;
            History.UndoManager.Touch(patch);
            History.UndoManager.Touch(other);
            var internals = patch.GetInternalPointsComponents();
            foreach (var point in other.GetInternalPointsComponents()) {
                if (!internals.Contains(point)) internals.Add(point);
            }
            //before the other patch is deleted: the points it gives already belong to the patch
            patch.SetPoints(perimeter, internals);
            other.Delete();
            manager.FlagAsChanged();
            return true;
        }
    }
}

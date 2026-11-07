using System.Collections.Generic;
using System.Linq;
using Godot;
using CustomElements;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace Operations {
    //Road anchor chains and shared perimeter edges, including clicks part way along an edge.
    static class PointSequenceConnections {
        //the terrain points on each anchor (a point on an anchor is a node of the anchor: its perimeter, e.g. of a terrain
        //patch, continues from there), built once for the searches of an update or of a click
        internal sealed class Graph {
            readonly Dictionary<TerrainAnchor, List<TerrainPoint>> onAnchors = new Dictionary<TerrainAnchor, List<TerrainPoint>>();

            public Graph(ElementManager manager) {
                if (manager == null) return;
                foreach (var point in manager.GetTerrainPoints()) {
                    if (point.IsDeleted() || point.anchor is not TerrainAnchor anchor || GO.IsNull(anchor) || anchor.IsDeleted()) continue;
                    if (!onAnchors.TryGetValue(anchor, out var list)) onAnchors[anchor] = list = new List<TerrainPoint>();
                    list.Add(point);
                }
            }

            public List<TerrainPoint> On(TerrainAnchor anchor) => onAnchors.TryGetValue(anchor, out var list) ? list : null;
        }

        static Node3D Canonical(Node3D obj) {
            if (GS.FindComponent<TerrainPoint>(obj)?.anchor is TerrainAnchor anchor && !GO.IsNull(anchor)) return anchor.GetObject();
            return obj;
        }

        static IEnumerable<Node3D> Ends(Node3D obj) {
            if (GS.FindComponent<TerrainPoint>(obj)?.anchor is LineAnchor line && !line.IsDeleted()) {
                yield return Canonical(line.start); yield return Canonical(line.end);
            } else if (GS.Cast<PointLink>(obj) is PointLink link) {
                yield return Canonical(GO.GetParent(link)); yield return Canonical(link.next);
            } else if (!GO.IsNull(obj) && (GS.FindComponent<TerrainPoint>(obj) != null || GS.FindComponent<TerrainAnchor>(obj) != null)) yield return Canonical(obj);
        }

        //within: only along the edges of that element (its anchor chains, or its sequences of points), null for any
        static IEnumerable<Node3D> Neighbors(Node3D obj, CustomElement exclude, Graph graph, object within) {
            if (GS.FindComponent<TerrainAnchor>(obj) is TerrainAnchor anchor) {
                if (within == null || Anchors.OwnerElement(anchor) == within) {
                    if (!GO.IsNull(anchor.prev) && (within == null || Anchors.OwnerElement(anchor.prev) == within)) yield return anchor.prev.GetObject();
                    if (!GO.IsNull(anchor.next) && (within == null || Anchors.OwnerElement(anchor.next) == within)) yield return anchor.next.GetObject();
                }
                var points = graph?.On(anchor);
                if (points == null) yield break;
                foreach (var point in points) {
                    foreach (var neighbor in PointNeighbors(point, exclude, within)) yield return neighbor;
                }
                yield break;
            }
            if (GS.FindComponent<TerrainPoint>(obj) is TerrainPoint free) {
                foreach (var neighbor in PointNeighbors(free, exclude, within)) yield return neighbor;
            }
        }

        //the points before and after the point in the sequences of the elements using it (the perimeters of the terrain
        //patches, the arrays of points of the custom elements)
        static IEnumerable<Node3D> PointNeighbors(TerrainPoint point, CustomElement exclude, object within) {
            foreach (var owner in point.GetLinks()) {
                if (owner == exclude || (within != null && owner != within)) continue;
                var lists = new List<(List<TerrainPoint> points, bool closed)>();
                if (owner is TerrainPatch patch) lists.Add((patch.GetPerimeterPointsComponents(), true));
                //the controls of a spline following the connections dynamically are far apart: they aren't edges
                if (owner is CustomElement element && element.Settings?.spline?.FollowsConnectionsDynamically != true) {
                    foreach (var (_, list) in PointTree.Lists(element.points)) {
                        if (list.def.children == null) lists.Add((list.items.Cast<PointLeaf>().Select(x => x.point).ToList(), list.def.array.closed));
                    }
                }
                foreach (var (points, closed) in lists) {
                    int index = points.IndexOf(point);
                    if (index < 0 || points.Count < 2) continue;
                    if (index > 0 || closed) yield return Canonical(points[(index + points.Count - 1) % points.Count].GetObject());
                    if (index + 1 < points.Count || closed) yield return Canonical(points[(index + 1) % points.Count].GetObject());
                }
            }
        }

        public static List<Node3D> Path(CustomElement element, PointList list, Node3D target, Vector3 position, Graph graph = null, object within = null) {
            var existing = list.items.Cast<PointLeaf>().Select(x => Canonical(x.point.GetObject())).ToHashSet();
            var source = ((PointLeaf)list.items[^1]).point;
            return Between(source.GetObject(), source.GetPoint(), target, position, element, existing, graph, within);
        }

        //the shortest connection (by length) from the source to the target (the nodes of the path, the ends of the segments the source
        //and the target are on included, the existing nodes excluded), null if there is none or none is needed (on the same edge);
        //within: only along the edges of that element
        public static List<Node3D> Between(Node3D source, Vector3 sourcePosition, Node3D target, Vector3 position, CustomElement exclude,
            HashSet<Node3D> existing, Graph graph = null, object within = null) {

            var starts = Ends(source).ToArray(); var ends = Ends(target).ToArray();
            //Moving between two points of the same edge needs no intermediate endpoint.
            if (starts.Length == 2 && ends.Length == 2 && starts.All(ends.Contains)) return null;
            List<Node3D> best = null; float bestLength = float.MaxValue;
            foreach (var start in starts) foreach (var end in ends) {
                //the shortest by length (Dijkstra), not by number of points: around a loop the two ways can have as many
                var previous = new Dictionary<Node3D, Node3D> { [start] = null };
                var distances = new Dictionary<Node3D, float> { [start] = 0 };
                var done = new HashSet<Node3D>();
                var queue = new PriorityQueue<Node3D, float>(); queue.Enqueue(start, 0);
                while (queue.TryDequeue(out var current, out var distance) && done.Count < 65535) {
                    if (!done.Add(current)) continue;
                    if (current == end) break;
                    foreach (var neighbor in Neighbors(current, exclude, graph, within)) {
                        if (GO.IsNull(neighbor) || done.Contains(neighbor) || (existing.Contains(neighbor) && neighbor != end)) continue;
                        var through = distance + GO.GetPosition(current).DistanceTo(GO.GetPosition(neighbor));
                        if (distances.TryGetValue(neighbor, out var known) && known <= through) continue;
                        distances[neighbor] = through; previous[neighbor] = current; queue.Enqueue(neighbor, through);
                    }
                }
                if (!done.Contains(end)) continue;
                var path = new List<Node3D>();
                for (var node = end; node != null; node = previous[node]) path.Add(node);
                path.Reverse();
                float length = sourcePosition.DistanceTo(GO.GetPosition(start)) + GO.GetPosition(end).DistanceTo(position);
                for (int i = 1; i < path.Count; i++) length += GO.GetPosition(path[i - 1]).DistanceTo(GO.GetPosition(path[i]));
                if (length < bestLength) { best = path; bestLength = length; }
            }
            return best?.Where(x => !existing.Contains(x)).ToList();
        }

        //the node a control point stands for in the connections (the anchor it is on, or itself)
        public static Node3D NodeOf(TerrainPoint point) => Canonical(point.GetObject());
    }
}

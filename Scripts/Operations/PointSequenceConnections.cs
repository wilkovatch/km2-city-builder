using System.Collections.Generic;
using System.Linq;
using Godot;
using CustomElements;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace Operations {
    //Road anchor chains and shared perimeter edges, including clicks part way along an edge.
    static class PointSequenceConnections {
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

        static IEnumerable<Node3D> Neighbors(Node3D obj, CustomElement exclude) {
            if (GS.FindComponent<TerrainAnchor>(obj) is TerrainAnchor anchor) {
                if (!GO.IsNull(anchor.prev)) yield return anchor.prev.GetObject();
                if (!GO.IsNull(anchor.next)) yield return anchor.next.GetObject();
            }
            if (GS.FindComponent<TerrainPoint>(obj) is not TerrainPoint point) yield break;
            foreach (var owner in point.GetLinks()) {
                if (owner == exclude) continue;
                var lists = new List<(List<TerrainPoint> points, bool closed)>();
                if (owner is TerrainPatch patch) lists.Add((patch.GetPerimeterPointsComponents(), true));
                if (owner is CustomElement element) {
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

        public static List<Node3D> Path(CustomElement element, PointList list, Node3D target, Vector3 position) {
            var existing = list.items.Cast<PointLeaf>().Select(x => Canonical(x.point.GetObject())).ToHashSet();
            var source = ((PointLeaf)list.items[^1]).point;
            var starts = Ends(source.GetObject()).ToArray(); var ends = Ends(target).ToArray();
            //Moving between two points of the same edge needs no intermediate endpoint.
            if (starts.Length == 2 && ends.Length == 2 && starts.All(ends.Contains)) return null;
            List<Node3D> best = null; float bestLength = float.MaxValue;
            foreach (var start in starts) foreach (var end in ends) {
                var previous = new Dictionary<Node3D, Node3D> { [start] = null };
                var queue = new Queue<Node3D>(); queue.Enqueue(start);
                while (queue.Count > 0 && previous.Count < 65535 && !previous.ContainsKey(end)) {
                    var current = queue.Dequeue();
                    foreach (var neighbor in Neighbors(current, element)) {
                        if (GO.IsNull(neighbor) || previous.ContainsKey(neighbor) || (existing.Contains(neighbor) && neighbor != end)) continue;
                        previous[neighbor] = current; queue.Enqueue(neighbor);
                    }
                }
                if (!previous.ContainsKey(end)) continue;
                var path = new List<Node3D>();
                for (var node = end; node != null; node = previous[node]) path.Add(node);
                path.Reverse();
                float length = source.GetPoint().DistanceTo(GO.GetPosition(start)) + GO.GetPosition(end).DistanceTo(position);
                for (int i = 1; i < path.Count; i++) length += GO.GetPosition(path[i - 1]).DistanceTo(GO.GetPosition(path[i]));
                if (length < bestLength) { best = path; bestLength = length; }
            }
            return best?.Where(x => !existing.Contains(x)).ToList();
        }
    }
}

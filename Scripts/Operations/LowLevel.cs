using System.Collections.Generic;
using Godot;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace Operations {
    //Editing of the low-level elements (see LowLevelElement), and of the ghosts of the procedural elements they replace
    public static class LowLevel {
        //distance within which a point of a converted element is welded to an existing point or anchor
        public const float WeldDistance = 0.01f;

        public static string GetTypeLabel(string type) {
            var types = CityElements.Types.Parsers.TypeParser.GetLowLevelTypes();
            if (type == null || !types.TryGetValue(type, out var t)) return type ?? "";
            var label = t.typeData.ui.label;
            return string.IsNullOrEmpty(label) ? type : StringManager.Get(label);
        }

        //a new element of the type with its default content around the position
        public static LowLevelElement Create(ElementManager manager, string type, Vector3 position, LowLevelElement container = null) {
            var element = manager.CreateLowLevelElement(type);
            element.CreateDefault(position);
            if (container != null) element.SetContainer(container);
            return element;
        }

        //deletes the element; the elements it contains are left without a container. The ghost it replaces is deleted with
        //the last element replacing it (deleteGhost false: it's kept, e.g. to bring it back)
        public static void Delete(ElementManager manager, LowLevelElement element, bool deleteGhost = true) {
            if (element == null || element.IsDeleted()) return;
            foreach (var child in manager.GetLowLevelChildren(element)) child.SetContainer(null);
            var source = element.source;
            element.Delete();
            if (deleteGhost && IsGhost(source) && GetReplacements(manager, source).Count == 0) DeleteGhost(manager, source);
        }

        static void DeleteGhost(ElementManager manager, object ghost) {
            if (ghost is Road road && !road.deleted) {
                Roads.Delete(road);
            } else if (ghost is Intersection intersection && !intersection.IsDeleted()) {
                Intersections.Delete(intersection);
            }
            manager.FlagAsChanged();
        }

        public static bool InsertArrayElement(LowLevelElement element, string arrayPath, int index, bool userEdit = true) {
            return element.InsertArrayElement(arrayPath, index, userEdit);
        }

        public static bool RemoveArrayElement(LowLevelElement element, string arrayPath, int index, bool userEdit = true) {
            return element.RemoveArrayElement(arrayPath, index, userEdit);
        }

        //the element of the editable array containing the point: of the outermost one (e.g. the section of a lane point,
        //what Insert, Remove and Delete act on) or of the innermost one (the lane); null if the point is not in one
        public static (string arrayPath, int index)? ArrayElementOf(LowLevelElement element, TerrainPoint point, bool outermost) {
            var path = point != null && element?.points != null ? global::LowLevel.PointTree.PathOf(element.points, point) : null;
            return path != null ? global::LowLevel.PointTree.FindArrayElement(element.points, path, true, outermost) : null;
        }

        //removes a point from the element (the element of the outermost array containing it, or a vertex of the mesh), the
        //point is deleted if no other element uses it
        public static bool RemovePoint(LowLevelElement element, TerrainPoint point) {
            return point.DeleteManual(element);
        }

        //the containers the element can be put in
        public static List<LowLevelElement> GetPossibleContainers(ElementManager manager, LowLevelElement element) {
            var res = new List<LowLevelElement>();
            foreach (var other in manager.lowLevelElements) {
                if (!other.IsDeleted() && element.CanBeIn(other)) res.Add(other);
            }
            return res;
        }

        //moves the elements of a container into another one (of a type that can contain them all) and deletes it
        public static bool Merge(ElementManager manager, LowLevelElement from, LowLevelElement into) {
            if (from == null || into == null || from == into || from.IsDeleted() || into.IsDeleted()) return false;
            var children = manager.GetLowLevelChildren(from);
            foreach (var child in children) {
                if (child != into && !child.CanBeIn(into)) return false;
            }
            if (into.container == from) into.SetContainer(from.container);
            foreach (var child in children) {
                if (child != into) child.SetContainer(into);
            }
            from.Delete();
            return true;
        }

        // Moving with the contents

        //the element, the elements it contains, the ones they contain and so on
        public static List<LowLevelElement> WithContents(ElementManager manager, LowLevelElement element) {
            var res = new List<LowLevelElement> { element };
            for (int i = 0; i < res.Count; i++) {
                foreach (var child in manager.GetLowLevelChildren(res[i])) {
                    if (!res.Contains(child)) res.Add(child);
                }
            }
            return res;
        }

        //the points of the element and of its contents, each once
        public static List<TerrainPoint> PointsWithContents(ElementManager manager, LowLevelElement element) {
            var res = new List<TerrainPoint>();
            var seen = new HashSet<TerrainPoint>();
            foreach (var e in WithContents(manager, element)) {
                foreach (var p in e.GetAllPoints()) {
                    if (!p.IsDeleted() && seen.Add(p)) res.Add(p);
                }
            }
            return res;
        }

        //the center of the points of the element and of its contents (where its move handle is), null without points
        public static Vector3? CenterWithContents(ElementManager manager, LowLevelElement element) {
            var points = PointsWithContents(manager, element);
            if (points.Count == 0) return null;
            var res = Vector3.Zero;
            foreach (var p in points) res += p.GetPoint();
            return res / points.Count;
        }

        //moves the element with its contents by the offset: their points move (the ones shared with other elements too, which
        //follow), except the ones on the anchors of roads and intersections, which stay there. The anchors of a ghost don't
        //count (they are the ones of the element converted, which nothing else shows): the points leave them and move. The
        //points on a line follow its ends, the ones on another point follow it, and the ones on a vertical line move as with
        //the gizmo (their height changes, the line follows its point). Returns the number of points moved
        public static int Move(ElementManager manager, LowLevelElement element, Vector3 offset) {
            if (element == null || element.IsDeleted() || offset.IsZeroApprox()) return 0;
            var moved = 0;
            foreach (var p in PointsWithContents(manager, element)) {
                var onGhost = OnGhostAnchor(p);
                if (!onGhost && p.anchor is TerrainAnchor ta && !GO.IsNull(ta) && !ta.IsDeleted()) continue;
                if (!onGhost && p.anchor is LineAnchor line && !line.IsDeleted()) continue;
                if (p.anchor is PointAnchor fixedPoint && !fixedPoint.IsDeleted()) continue;
                History.UndoManager.Touch(p);
                if (onGhost) p.anchor = null;
                GO.SetPosition(p.GetObject(), p.GetPoint() + offset);
                moved++;
            }
            return moved;
        }

        //the point is on an anchor of a ghost, or on the segment between two of them
        static bool OnGhostAnchor(TerrainPoint point) {
            bool OfGhost(Node3D obj) => !GO.IsNull(obj) && IsGhostAnchor(GS.FindRecursive<TerrainAnchor>(obj));
            if (point.anchor is TerrainAnchor ta) return !GO.IsNull(ta) && IsGhostAnchor(ta);
            if (point.anchor is LineAnchor line && !line.IsDeleted()) return OfGhost(line.start) && OfGhost(line.end);
            return false;
        }

        // Points from clicks

        //the point for a click on obj (at position): the clicked terrain point itself (shared), a new point on the clicked
        //anchor or segment between two points, or a new free point
        public static TerrainPoint PointFromClick(ElementManager manager, Node3D obj, Vector3 position) {
            var clicked = obj == null ? null : GameEngine.Scriptable.FindRecursive<TerrainPoint>(obj);
            if (clicked != null && !clicked.IsDeleted()) return clicked;
            IAnchorable anchor = obj == null ? null : GameEngine.Scriptable.FindRecursive<TerrainAnchor>(obj);
            if (anchor == null && obj != null) {
                var link = GameEngine.Scriptable.Cast<PointLink>(obj);
                if (link != null) anchor = new LineAnchor(GO.GetParent(link), link.next, position);
            }
            return TerrainPoint.Create(anchor != null ? anchor.GetPoint() : position, null, manager.GetTerrainPointContainer(), anchor);
        }

        //a new element at the start or end of the array, moved so that its center is at the position and turned around the
        //vertical axis as the direction of the array changes (e.g. a section of a road continuing it around a curve)
        public static bool AppendArrayElementAt(LowLevelElement element, string arrayPath, bool atStart, Vector3 position, bool userEdit = true) {
            if (element.points == null || !(global::LowLevel.PointTree.Find(element.points, arrayPath) is global::LowLevel.PointList list)) return false;
            var count = list.items.Count;
            var end = count > 0 ? list.items[atStart ? 0 : count - 1] : null;
            var previous = count > 1 ? list.items[atStart ? 1 : count - 2] : null;
            var index = atStart ? 0 : count;
            if (!element.InsertArrayElement(arrayPath, index, userEdit)) return false;
            var added = list.items[index];
            var newLeaves = global::LowLevel.PointTree.Leaves(added);
            if (end == null) {
                //nothing to continue: the default layout around the position
                var center = NodeCenter(added);
                foreach (var (_, leaf) in newLeaves) GO.SetPosition(leaf.point.GetObject(), leaf.point.GetPoint() - center + position);
                return true;
            }
            var endCenter = NodeCenter(end);
            var angle = 0.0f;
            if (previous != null) {
                var oldDir = endCenter - NodeCenter(previous);
                var newDir = position - endCenter;
                oldDir.Y = 0;
                newDir.Y = 0;
                if (oldDir.LengthSquared() > 1e-8f && newDir.LengthSquared() > 1e-8f) angle = oldDir.SignedAngleTo(newDir, Vector3.Up);
            }
            foreach (var (relPath, leaf) in newLeaves) {
                var model = global::LowLevel.PointTree.Find(end, relPath) as global::LowLevel.PointLeaf;
                if (model?.point == null || model.point.IsDeleted()) continue;
                var offset = (model.point.GetPoint() - endCenter).Rotated(Vector3.Up, angle);
                GO.SetPosition(leaf.point.GetObject(), position + offset);
                if (leaf.def.rotatable) leaf.point.SetRotation(model.point.GetRotation() + new Vector3(0, Mathf.RadToDeg(angle), 0));
            }
            return true;
        }

        static Vector3 NodeCenter(global::LowLevel.PointNode node) {
            var res = Vector3.Zero;
            var n = 0;
            foreach (var (_, leaf) in global::LowLevel.PointTree.Leaves(node)) {
                if (leaf.point == null || leaf.point.IsDeleted()) continue;
                res += leaf.point.GetPoint();
                n++;
            }
            return n > 0 ? res / n : res;
        }

        //a point at the end of the array (of points) from a click (see PointFromClick); in an array of groups, a new
        //element there (see AppendArrayElementAt), its first point is returned.
        //A uniform array gets the point in every instance: the clicked point in the instance whose holder (e.g. the section
        //of an array of lanes) is the nearest to the click, a new point at the same offset from the center of the others
        //(turned as their direction across, from their first to their last point, is)
        public static TerrainPoint AppendPoint(ElementManager manager, LowLevelElement element, string arrayPath, Node3D obj, Vector3 position) {
            if (element.points == null || !(global::LowLevel.PointTree.Find(element.points, arrayPath) is global::LowLevel.PointList list)) return null;
            if (list.def.children != null) {
                if (!AppendArrayElementAt(element, arrayPath, false, position)) return null;
                var leaves = global::LowLevel.PointTree.Leaves(list.items[list.items.Count - 1]);
                return leaves.Count > 0 ? leaves[0].leaf.point : null;
            }
            var holders = UniformHolders(element, arrayPath);
            var nearest = 0;
            for (int i = 1; i < holders.Count; i++) {
                if (holders[i].center.DistanceTo(position) < holders[nearest].center.DistanceTo(position)) nearest = i;
            }
            if (holders.Count > 0) list = holders[nearest].list;
            //a point is in an array of points once (clicking the first point of a closed array closes it instead)
            var clicked = obj == null ? null : GameEngine.Scriptable.FindRecursive<TerrainPoint>(obj);
            if (clicked != null && list.items.Exists(item => item is global::LowLevel.PointLeaf l && l.point == clicked)) return null;
            if (!element.InsertArrayElement(arrayPath, list.items.Count)) return null;
            for (int i = 0; i < holders.Count; i++) {
                if (i == nearest) continue;
                var (other, center, across) = holders[i];
                var angle = 0.0f;
                var from = holders[nearest].across;
                if (from.LengthSquared() > 1e-8f && across.LengthSquared() > 1e-8f) angle = from.SignedAngleTo(across, Vector3.Up);
                var added = (global::LowLevel.PointLeaf)other.items[other.items.Count - 1];
                GO.SetPosition(added.point.GetObject(), center + (position - holders[nearest].center).Rotated(Vector3.Up, angle));
            }
            var point = PointFromClick(manager, obj, position);
            var leaf = (global::LowLevel.PointLeaf)list.items[list.items.Count - 1];
            element.ReplacePoint(leaf.point, point);
            if (point.GetLinks().Count == 0) point.Delete(); //not used after all
            return point.IsDeleted() ? null : point;
        }

        public static TerrainPoint InsertPointAt(ElementManager manager, LowLevelElement element, string path, int index, Node3D obj, Vector3 position) {
            if (!element.InsertArrayElement(path, index)) return null;
            var list = (global::LowLevel.PointList)global::LowLevel.PointTree.Find(element.points, path);
            var point = PointFromClick(manager, obj, position);
            element.ReplacePoint(((global::LowLevel.PointLeaf)list.items[index]).point, point);
            return point;
        }

        //the instances of a uniform array in the elements of another array (none otherwise), with the center of the node
        //holding each one and its horizontal direction across (from its first to its last point)
        static List<(global::LowLevel.PointList list, Vector3 center, Vector3 across)> UniformHolders(LowLevelElement element, string arrayPath) {
            var res = new List<(global::LowLevel.PointList, Vector3, Vector3)>();
            var instances = global::LowLevel.PointTree.UniformInstances(element.points, arrayPath);
            if (instances.Count < 2) return res;
            foreach (var (path, list) in global::LowLevel.PointTree.Lists(element.points)) {
                if (!instances.Contains(list)) continue;
                var dot = path.LastIndexOf('.');
                var holder = global::LowLevel.PointTree.Find(element.points, dot < 0 ? "" : path.Substring(0, dot));
                var leaves = global::LowLevel.PointTree.Leaves(holder).FindAll(l => l.leaf.point != null && !l.leaf.point.IsDeleted());
                var across = leaves.Count > 1 ? leaves[leaves.Count - 1].leaf.point.GetPoint() - leaves[0].leaf.point.GetPoint() : Vector3.Zero;
                across.Y = 0;
                res.Add((list, NodeCenter(holder), across));
            }
            return res;
        }

        //the point of the element is replaced with the clicked terrain point (shared with its elements from now on), or
        //attached to the clicked anchor or segment; returns the point the element now has, null if nothing was clicked
        public static TerrainPoint AttachPoint(ElementManager manager, LowLevelElement element, TerrainPoint point, Node3D obj, Vector3 position) {
            var clicked = obj == null ? null : GameEngine.Scriptable.FindRecursive<TerrainPoint>(obj);
            if (clicked != null && !clicked.IsDeleted()) {
                if (clicked == point) return point;
                element.ReplacePoint(point, clicked);
                return clicked;
            }
            return TerrainPoints.Attach(point, obj, position, false) ? point : null;
        }

        //the point follows the line between two terrain points (where it is closest to it), e.g. a corner between the ends of
        //the side of a building; false if a or b follows the point (directly or not)
        public static bool AttachToLine(TerrainPoint point, TerrainPoint a, TerrainPoint b) {
            if (point == null || a == null || b == null || a == b || a == point || b == point) return false;
            if (a.IsDeleted() || b.IsDeleted() || TerrainPoints.DependsOn(a, point) || TerrainPoints.DependsOn(b, point)) return false;
            History.UndoManager.Touch(point);
            point.anchor = new LineAnchor(a.GetObject(), b.GetObject(), point.GetPoint());
            GO.SetPosition(point.GetObject(), point.anchor.GetPoint());
            point.Select(true);
            return true;
        }

        //the point follows the vertical line of another terrain point (its master: its x and z, keeping its own height);
        //false if the master follows the point (directly or not)
        public static bool AttachToColumn(TerrainPoint point, TerrainPoint master) {
            if (point == null || master == null || master == point || master.IsDeleted() || TerrainPoints.DependsOn(master, point)) return false;
            History.UndoManager.Touch(point);
            point.anchor = new ColumnAnchor(master, point.GetPoint().Y);
            GO.SetPosition(point.GetObject(), point.anchor.GetPoint());
            point.Select(true);
            return true;
        }

        //the point of the element stops being shared with the other elements (a copy of it takes its place in the element),
        //or, if only the element uses it, stops following its anchor
        public static TerrainPoint DetachPoint(ElementManager manager, LowLevelElement element, TerrainPoint point) {
            if (point.GetLinks().Count > 1) {
                var copy = TerrainPoint.Create(point.GetPoint(), null, manager.GetTerrainPointContainer());
                copy.SetRotation(point.GetRotation());
                element.ReplacePoint(point, copy);
                return copy;
            }
            TerrainPoints.Detach(point);
            return point;
        }

        // Ghosts

        public static bool IsGhost(object element) {
            return element is Road road ? road.ghost : element is Intersection intersection && intersection.ghost;
        }

        public static void SetGhost(object element, bool ghost) {
            if (element is Road road) road.SetGhost(ghost);
            else if (element is Intersection intersection) intersection.SetGhost(ghost);
        }

        //the low-level elements replacing a procedural element
        public static List<LowLevelElement> GetReplacements(ElementManager manager, object source) {
            var res = new List<LowLevelElement>();
            if (source == null) return res;
            foreach (var element in manager.lowLevelElements) {
                if (!element.IsDeleted() && element.source == source) res.Add(element);
            }
            return res;
        }

        //brings back a ghost: the low-level elements replacing it are deleted
        public static void RestoreGhost(ElementManager manager, object ghost) {
            foreach (var element in GetReplacements(manager, ghost)) Delete(manager, element, false);
            SetGhost(ghost, false);
        }

        // Welding: a point of a new element takes the place of an existing point at the same position (shared with its
        // elements), or is attached to an anchor there

        public static TerrainPoint FindPointAt(ElementManager manager, Vector3 position, HashSet<TerrainPoint> exclude) {
            foreach (var p in manager.GetTerrainPoints()) {
                if (p.IsDeleted() || (exclude != null && exclude.Contains(p))) continue;
                if (GameEngine.Vector3.Distance(p.GetPoint(), position) < WeldDistance) return p;
            }
            return null;
        }

        //an anchor of a road or intersection at the position (not of a ghost: its anchors don't move with what the user sees)
        public static TerrainAnchor FindAnchorAt(ElementManager manager, Vector3 position) {
            foreach (var anchor in Anchors.GetAll(manager)) {
                if (anchor.IsDeleted() || GameEngine.Vector3.Distance(anchor.GetPoint(), position) >= WeldDistance) continue;
                if (IsGhostAnchor(anchor)) continue;
                return anchor;
            }
            return null;
        }

        //the anchor is of a ghost, which nothing else shows; the anchors across the end of a road (low-level only) of a ghost
        //intersection are not, while the road isn't a ghost: they follow its end (e.g. for the road converted after the
        //intersection, whose points there, as the ones of its dividers, are put on them)
        public static bool IsGhostAnchor(TerrainAnchor anchor) {
            var owner = Anchors.GetOwner(anchor).owner;
            if (!IsGhost(owner)) return false;
            if (!anchor.lowLevelOnly || owner is not Intersection intersection) return true;
            var position = anchor.GetPoint();
            bool OnEnd(List<Vector3> ends) => ends.Exists(p => GameEngine.Vector3.Distance(p, position) < WeldDistance);
            foreach (var road in intersection.roads) {
                if (road == null || road.ghost || road.generator == null) continue;
                if (road.startIntersection == intersection && OnEnd(road.generator.startEndAnchors)) return false;
                if (road.endIntersection == intersection && OnEnd(road.generator.endEndAnchors)) return false;
            }
            return true;
        }
    }
}

using System.Collections.Generic;
using Godot;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace Operations {
    //Several elements of any kind moved, rotated and scaled together around a pivot (the multiple selection of the
    //editor, the elements.transform command of the API).
    //What follows something else stays with it: the points on the anchors of roads and intersections, on a segment or
    //on another point are not moved (they follow their target, which moves if it is in the group), and neither is the
    //end of a road on an intersection that is not in the group. A selected intersection moves the ends of its roads.
    public static class GroupTransform {
        internal class Item {
            public Node3D node;
            public MeshInstance mesh; //null for the points
            public bool rotates; //the node gets the rotation too (meshes, the points of the custom elements)
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        //The group as it was when the transform started: applying a transform always starts from it, so a drag can
        //apply the total transform at every frame
        public class Session {
            internal readonly List<Item> items = new List<Item>();
            public Vector3 pivot;
            public int Count => items.Count;
        }

        //a rotation around the vertical axis, in degrees (positive to the right)
        public static Quaternion Yaw(float degrees) {
            return GameEngine.Quaternion.Euler(0, degrees, 0);
        }

        //the elements a group can contain
        public static bool Accepts(object element) {
            switch (element) {
                case Road road: return !road.deleted && !road.ghost;
                case Intersection intersection: return !intersection.IsDeleted() && !intersection.ghost;
                case TerrainPatch patch: return !patch.IsDeleted();
                case BuildingLine line: return !line.IsDeleted();
                case PolygonalBuilding building: return !building.IsDeleted();
                case CustomElement custom: return !custom.IsDeleted();
                case MeshInstance mesh: return mesh.objectParameter == null && GodotObject.IsInstanceValid(mesh);
                default: return false;
            }
        }

        static bool FollowsItsAnchor(TerrainPoint point) {
            if (GO.IsNull(point.anchor) || point.anchor.IsDeleted()) return false;
            //on the vertical line of another point only x and z follow it: the height is its own
            return !(point.anchor is ColumnAnchor);
        }

        //pivot: by default the center of what moves. The elements are recorded for undo (inside the step of the caller)
        public static Session Begin(ElementManager manager, IEnumerable<object> elements, Vector3? pivot = null, bool record = true) {
            var session = new Session();
            var selected = new HashSet<object>();
            foreach (var element in elements) {
                if (Accepts(element)) selected.Add(element);
            }
            var done = new HashSet<Node3D>();
            void AddNode(Node3D node, bool rotates, MeshInstance mesh = null) {
                if (GO.IsNull(node) || !done.Add(node)) return;
                session.items.Add(new Item {
                    node = node, mesh = mesh, rotates = rotates, position = GO.GetPosition(node),
                    rotation = GO.GetRotationQuaternion(node), scale = GO.GetLocalScale(node)
                });
            }
            void AddPoint(TerrainPoint point, bool rotates) {
                if (point == null || point.IsDeleted() || FollowsItsAnchor(point)) return;
                if (record) History.UndoManager.Touch(point);
                AddNode(point.GetObject(), rotates);
            }
            foreach (var element in selected) {
                switch (element) {
                    case Road road:
                        if (record) History.UndoManager.Touch(road);
                        for (int i = 0; i < road.points.Count; i++) {
                            var end = i == 0 ? road.startIntersection : i == road.points.Count - 1 ? road.endIntersection : null;
                            if (end != null && !selected.Contains(end)) continue; //it stays on its intersection
                            AddNode(road.points[i], false);
                        }
                        break;
                    case Intersection intersection:
                        if (record) History.UndoManager.Touch(intersection);
                        AddNode(intersection.point, false);
                        break;
                    case TerrainPatch patch:
                        foreach (var point in patch.GetPerimeterPointsComponents()) AddPoint(point, false);
                        foreach (var point in patch.GetInternalPointsComponents()) AddPoint(point, false);
                        break;
                    case BuildingLine line:
                        foreach (var point in line.GetPointsComponents()) AddPoint(point, false);
                        break;
                    case PolygonalBuilding building:
                        foreach (var point in building.GetPointsComponents()) AddPoint(point, false);
                        break;
                    case CustomElement custom:
                        foreach (var point in CustomElements.PointsWithContents(manager, custom)) AddPoint(point, true);
                        break;
                    case MeshInstance mesh:
                        if (record) History.UndoManager.Touch(mesh);
                        AddNode(mesh, true, mesh);
                        break;
                }
            }
            session.pivot = pivot ?? Center(session);
            return session;
        }

        static Vector3 Center(Session session) {
            if (session.items.Count == 0) return Vector3.Zero;
            var min = session.items[0].position;
            var max = min;
            foreach (var item in session.items) {
                min = min.Min(item.position);
                max = max.Max(item.position);
            }
            return (min + max) * 0.5f;
        }

        //the center of what a group of the elements would move (null if nothing would)
        public static Vector3? CenterOf(ElementManager manager, IEnumerable<object> elements) {
            var session = Begin(manager, elements, null, false);
            return session.Count == 0 ? (Vector3?)null : session.pivot;
        }

        //the total transform from the start of the session: every position is scaled, then rotated, around the pivot,
        //then moved by the offset. The meshes and the points of the custom elements also get the rotation, and the
        //meshes the scale when it is the same on the three axes
        public static void Apply(Session session, Vector3 offset, Quaternion rotation, Vector3 scale) {
            var uniform = Mathf.IsEqualApprox(scale.X, scale.Y) && Mathf.IsEqualApprox(scale.Y, scale.Z);
            var rotated = !rotation.IsEqualApprox(Quaternion.Identity);
            foreach (var item in session.items) {
                if (GO.IsNull(item.node)) continue;
                var position = session.pivot + rotation * ((item.position - session.pivot) * scale) + offset;
                if (item.mesh != null) {
                    Meshes.SetTransform(item.mesh, position, rotation * item.rotation, uniform ? item.scale * scale.X : item.scale);
                } else {
                    GO.SetPosition(item.node, position);
                    if (item.rotates && rotated) GO.SetRotation(item.node, rotation * item.rotation);
                }
            }
        }

        //transforms the elements in one go, returns how many points and meshes were moved
        public static int Transform(ElementManager manager, IEnumerable<object> elements, Vector3 offset, Quaternion rotation, Vector3 scale, Vector3? pivot = null) {
            var session = Begin(manager, elements, pivot);
            Apply(session, offset, rotation, scale);
            if (session.Count > 0) manager.FlagAsChanged();
            return session.Count;
        }

        //deletes the elements (the roads first: the intersections left with no road go with them), returns how many
        public static int Delete(ElementManager manager, IEnumerable<object> elements) {
            var list = new List<object>(elements);
            list.Sort((a, b) => (a is Road ? 0 : 1).CompareTo(b is Road ? 0 : 1));
            var count = 0;
            foreach (var element in list) {
                if (!Accepts(element) || !History.ElementSnapshot.IsAlive(element)) continue;
                History.UndoManager.Touch(element);
                switch (element) {
                    case Road road: Roads.Delete(road); break;
                    case Intersection intersection: Intersections.Delete(intersection); break;
                    case TerrainPatch patch: TerrainPatches.Delete(patch); break;
                    case BuildingLine line: BuildingLines.Delete(line); break;
                    case PolygonalBuilding building: PolygonalBuildings.Delete(building); break;
                    case CustomElement custom: CustomElements.Delete(manager, custom); break;
                    case MeshInstance mesh: Meshes.Delete(mesh); break;
                }
                count++;
            }
            if (count > 0) manager.FlagAsChanged(true);
            return count;
        }

        //the element of the city a clicked node belongs to (as the selection of the editor finds it), with the mesh
        //instances; null if none
        public static object ElementOf(Node node) {
            for (var obj = node as Node3D; obj != null; obj = obj.GetParent() as Node3D) { //not GetParentNode3D, null for a top level node (e.g. a line)
                var mesh = GS.Cast<MeshInstance>(obj);
                if (mesh != null && mesh.objectParameter == null) return mesh;
            }
            var element = ElementPlacer.ReferencePicker.ElementOf(node);
            return Accepts(element) ? element : null;
        }
    }
}

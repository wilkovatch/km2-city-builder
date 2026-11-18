using System.Collections;
using System.Collections.Generic;
using Godot;
using GO = GameEngine.GameObject;
using V3 = GameEngine.Vector3;

namespace EditorPanels {
    namespace Buildings {
        //The sides of the building being edited that change its size, moved with the gizmo: its top sets its height when
        //moved up or down, its back sets its depth when moved towards or away from the front. Their contours are drawn in
        //their colors (faintly where something covers them), and a click on one of them (or near its contour) selects it,
        //with the gizmo at its middle. They are not points of the line: they are put back where the size of the building
        //says after every change.
        public class BuildingSizeHandles {
            const float MinSize = 0.1f;
            const float LineWidth = 3; //pixels
            const float SelectedLineWidth = 5;
            const float ContourPickWidth = 16; //pixels around the contour that select the side
            const float MinFaceWidth = 0.05f; //the faces narrower than this have no collider

            //a side: the target of the gizmo (at its middle), its contour and its colliders (in program coordinates)
            class Side {
                public StaticBody3D body;
                public PlacementHeightLine lines;
                public List<(Vector3 from, Vector3 to)> contour = new List<(Vector3, Vector3)>();
                public List<Vector3[]> faces = new List<Vector3[]>();
                public List<CollisionShape3D> faceShapes = new List<CollisionShape3D>();
                public List<CollisionShape3D> edgeShapes = new List<CollisionShape3D>();
            }

            readonly CityBuilderMenuBar builder;
            Building building;
            Side height, depth, dragged;
            bool hooked = false;
            //the front of the building, as when the drag started: its middle (at half its length), the direction to its
            //back there, its points with the directions to their back, and the base of the height
            Vector3 middle, normal;
            List<(Vector3 point, Vector3 normal)> front = new List<(Vector3, Vector3)>();
            float baseHeight;

            public BuildingSizeHandles(CityBuilderMenuBar builder) {
                this.builder = builder;
            }

            //the gizmo targets, by name: BuildingHeightHandle and BuildingDepthHandle
            public StaticBody3D HeightHandle => height?.body;
            public StaticBody3D DepthHandle => depth?.body;

            Side Create(string name, Color color) {
                var side = new Side();
                side.body = new StaticBody3D { Name = name };
                builder.helper.elementManager.AddChild(side.body);
                side.body.SetMeta(CityBuilderMenuBar.ToolHandleMeta, true);
                side.lines = new PlacementHeightLine(side.body, MaterialManager.GetHandleMaterial(color));
                side.lines.AddLayer(MaterialManager.GetDrawnThroughLineMaterial(color));
                GO.SetLayer(side.body, 8); //selected by the gizmo, like the points
                return side;
            }

            public void Show(Building building) {
                this.building = building;
                if (!hooked) {
                    builder.gizmo.onTransformStarted += OnTransformStarted;
                    builder.gizmo.onAnyChange += OnTransformChanged;
                    builder.gizmo.onTransformFinished += OnTransformFinished;
                    builder.GetTree().ProcessFrame += OnFrame;
                    hooked = true;
                }
                Update();
            }

            public void Hide() {
                building = null;
                dragged = null;
                SetShown(false);
                if (hooked) {
                    builder.gizmo.onTransformStarted -= OnTransformStarted;
                    builder.gizmo.onAnyChange -= OnTransformChanged;
                    builder.gizmo.onTransformFinished -= OnTransformFinished;
                    builder.GetTree().ProcessFrame -= OnFrame;
                    hooked = false;
                }
            }

            void SetShown(bool shown) {
                foreach (var side in new[] { height, depth }) {
                    if (side == null || GO.IsNull(side.body)) continue;
                    if (!shown && builder.gizmo.HasTarget(side.body)) builder.gizmo.ClearTargets();
                    GO.SetEnabled(side.body, shown);
                }
            }

            //the buildings of the lines with only the front get their height from the line, and have no depth
            bool Available() {
                return building != null && building.line != null && !building.line.IsDeleted() && building.line.buildings.Contains(building)
                    && building.enabled && !building.line.state.Bool("frontOnly") && building.spline != null && building.spline.Count >= 2;
            }

            void ReadFront() {
                var spline = building.spline;
                front = new List<(Vector3, Vector3)>(spline);
                var length = 0.0f;
                for (int i = 1; i < spline.Count; i++) length += V3.Distance(spline[i - 1].point, spline[i].point);
                var remaining = length * 0.5f;
                middle = spline[0].point;
                normal = spline[0].normal;
                for (int i = 1; i < spline.Count; i++) {
                    var segment = V3.Distance(spline[i - 1].point, spline[i].point);
                    if (remaining <= segment && segment > 0) {
                        var t = remaining / segment;
                        middle = V3.Lerp(spline[i - 1].point, spline[i].point, t);
                        normal = V3.Lerp(spline[i - 1].normal, spline[i].normal, t);
                        break;
                    }
                    remaining -= segment;
                }
                normal = new Vector3(normal.X, 0, normal.Z);
                normal = normal.LengthSquared() > 1e-6f ? normal.Normalized() : Vector3.Zero;
                baseHeight = building.height - building.state.Float("height");
            }

            //the two sides where the size of the building puts them (as the building does, see Building.PreUpdateMesh)
            void Place() {
                var top = baseHeight + building.state.Float("height");
                var d = building.state.Float("depth");
                Vector3 Top(Vector3 p) => new Vector3(p.X, top, p.Z);
                var frontTop = new List<Vector3>();
                var back = new List<Vector3>();
                foreach (var (point, direction) in front) {
                    frontTop.Add(Top(point));
                    back.Add(point + direction * d);
                }
                var n = front.Count;

                height.contour.Clear();
                height.faces.Clear();
                for (int i = 0; i < n - 1; i++) {
                    height.contour.Add((frontTop[i], frontTop[i + 1]));
                    height.contour.Add((Top(back[i]), Top(back[i + 1])));
                    height.faces.Add(new[] { frontTop[i], frontTop[i + 1], Top(back[i + 1]), Top(back[i]) });
                }
                height.contour.Add((frontTop[0], Top(back[0])));
                height.contour.Add((frontTop[n - 1], Top(back[n - 1])));

                depth.contour.Clear();
                depth.faces.Clear();
                for (int i = 0; i < n - 1; i++) {
                    depth.contour.Add((back[i], back[i + 1]));
                    depth.contour.Add((Top(back[i]), Top(back[i + 1])));
                    depth.faces.Add(new[] { back[i], back[i + 1], Top(back[i + 1]), Top(back[i]) });
                }
                depth.contour.Add((back[0], Top(back[0])));
                depth.contour.Add((back[n - 1], Top(back[n - 1])));

                GO.SetPosition(height.body, Top(middle + normal * d * 0.5f));
                GO.SetPosition(depth.body, new Vector3(0, (middle.Y + top) * 0.5f - middle.Y, 0) + middle + normal * d);
                UpdateFaceShapes(height);
                UpdateFaceShapes(depth);
                DrawContours();
            }

            //the colliders of the faces of a side (thin, as the faces of a building can be flat)
            void UpdateFaceShapes(Side side) {
                var origin = side.body.GlobalPosition;
                while (side.faceShapes.Count < side.faces.Count) {
                    //its shape is given with its points (an empty convex shape fails to build its hull)
                    var shape = new CollisionShape3D { Disabled = true };
                    side.body.AddChild(shape);
                    side.faceShapes.Add(shape);
                }
                for (int i = 0; i < side.faceShapes.Count; i++) {
                    var shape = side.faceShapes[i];
                    shape.Disabled = i >= side.faces.Count;
                    if (shape.Disabled) continue;
                    var face = side.faces[i];
                    //the normal of the larger of its two triangles: none for a face without width (e.g. with two points
                    //of the front at the same position), which a convex hull can't be built from
                    var n0 = (face[1] - face[0]).Cross(face[2] - face[0]);
                    var n1 = (face[2] - face[0]).Cross(face[3] - face[0]);
                    var thickness = n0.LengthSquared() >= n1.LengthSquared() ? n0 : n1;
                    var longest = 0.0f;
                    for (int j = 0; j < face.Length; j++) longest = Mathf.Max(longest, face[j].DistanceTo(face[(j + 1) % face.Length]));
                    if (longest < 1e-4f || thickness.Length() / longest < MinFaceWidth) {
                        shape.Disabled = true;
                        continue;
                    }
                    thickness = thickness.Normalized() * 0.02f;
                    var points = new Vector3[face.Length * 2];
                    for (int j = 0; j < face.Length; j++) {
                        points[j * 2] = GameEngine.Vector3.DecodePosition(face[j] + thickness) - origin;
                        points[j * 2 + 1] = GameEngine.Vector3.DecodePosition(face[j] - thickness) - origin;
                    }
                    if (shape.Shape is ConvexPolygonShape3D convex) convex.Points = points;
                    else shape.Shape = new ConvexPolygonShape3D { Points = points };
                }
            }

            //the contours, as wide on screen at any distance, and the colliders around them
            void DrawContours() {
                var cam = builder.GetViewport().GetCamera3D();
                if (cam == null) return;
                foreach (var side in new[] { height, depth }) {
                    if (side == null || GO.IsNull(side.body) || !side.body.Visible) continue;
                    var selected = builder.gizmo.HasTarget(side.body);
                    side.lines.ShowSegments(side.contour, cam, selected ? SelectedLineWidth : LineWidth);
                    UpdateEdgeShapes(side, cam);
                }
            }

            void UpdateEdgeShapes(Side side, Camera3D cam) {
                var origin = side.body.GlobalPosition;
                while (side.edgeShapes.Count < side.contour.Count) {
                    var shape = new CollisionShape3D { Shape = new CapsuleShape3D() };
                    side.body.AddChild(shape);
                    side.edgeShapes.Add(shape);
                }
                for (int i = 0; i < side.edgeShapes.Count; i++) {
                    var shape = side.edgeShapes[i];
                    shape.Disabled = i >= side.contour.Count;
                    if (shape.Disabled) continue;
                    var a = GameEngine.Vector3.DecodePosition(side.contour[i].from);
                    var b = GameEngine.Vector3.DecodePosition(side.contour[i].to);
                    var mid = (a + b) * 0.5f;
                    var direction = b - a;
                    var length = direction.Length();
                    if (length < 1e-4f || cam.IsPositionBehind(mid)) {
                        shape.Disabled = true;
                        continue;
                    }
                    direction /= length;
                    if (direction.Y < 0) direction = -direction;
                    var pixelsPerUnit = cam.UnprojectPosition(mid).DistanceTo(cam.UnprojectPosition(mid + cam.GlobalBasis.X));
                    var radius = ContourPickWidth * 0.5f / Mathf.Max(pixelsPerUnit, 1e-3f);
                    var capsule = (CapsuleShape3D)shape.Shape;
                    capsule.Radius = radius;
                    capsule.Height = length + radius * 2;
                    var basis = direction.IsEqualApprox(Vector3.Up) ? Basis.Identity : new Basis(new Quaternion(Vector3.Up, direction));
                    shape.Transform = new Transform3D(basis, mid - origin);
                }
            }

            void OnFrame() {
                if (building != null && height != null && !GO.IsNull(height.body) && height.body.Visible) DrawContours();
            }

            //called after the updates of the city, and when the building edited changes
            public void Update() {
                if (dragged != null) return;
                if (!Available()) {
                    SetShown(false);
                    return;
                }
                if (height == null || GO.IsNull(height.body)) height = Create("BuildingHeightHandle", new Color(0.2f, 0.9f, 1.0f));
                if (depth == null || GO.IsNull(depth.body)) depth = Create("BuildingDepthHandle", new Color(1.0f, 0.4f, 1.0f));
                SetShown(true);
                ReadFront();
                Place();
                if (builder.gizmo.HasTarget(height.body) || builder.gizmo.HasTarget(depth.body)) builder.gizmo.SetPivotPoint();
            }

            void OnTransformStarted(IReadOnlyList<Node3D> targets) {
                dragged = null;
                if (!Available() || targets.Count != 1 || height == null || depth == null) return;
                if (targets[0] == height.body) dragged = height;
                else if (targets[0] == depth.body) dragged = depth;
                if (dragged == null) return;
                ReadFront();
                History.UndoManager.Touch(building.line); //inside the undo step of the gizmo
            }

            void OnTransformChanged() {
                if (dragged == null || building == null) return;
                var position = GO.GetPosition(dragged.body);
                if (dragged == height) {
                    building.State.SetFloat("height", Mathf.Max(MinSize, position.Y - baseHeight));
                } else {
                    building.State.SetFloat("depth", Mathf.Max(MinSize, (position - middle).Dot(normal)));
                }
                building.State.FlagAsChanged();
                Place(); //only the size moves the sides
                builder.NotifyChange();
                builder.RefreshActivePanelValues();
            }

            void OnTransformFinished(IReadOnlyList<Node3D> targets) {
                if (dragged == null) return;
                dragged = null;
                Update();
            }
        }
    }
}

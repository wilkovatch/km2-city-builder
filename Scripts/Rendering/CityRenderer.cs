using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Rendering {
    //Renders the city (or a node in a world of its own) to an image, offscreen: a SubViewport sharing the world of the
    //editor, with a camera of its own, so the view of the editor doesn't change. The overlays (labels, markers, lines,
    //grid) are drawn in 2D over the 3D. Needs a renderer (not with --headless). The renders run one at a time.
    public partial class CityRenderer : Node {
        public const int MaxSize = 4096;

        static CityRenderer instance;
        readonly System.Threading.SemaphoreSlim running = new System.Threading.SemaphoreSlim(1, 1);

        public static bool Available => DisplayServer.GetName() != "headless";

        //the renderer, created under the node the first time
        public static CityRenderer Get(Node owner) {
            if (!IsInstanceValid(instance)) {
                instance = new CityRenderer { Name = "CityRenderer" };
                owner.AddChild(instance);
            }
            return instance;
        }

        partial class OverlayCanvas : Control {
            public System.Action<Control> draw;

            public override void _Draw() {
                draw?.Invoke(this);
            }
        }

        //terrain: the terrain of the city, whose detail follows the camera of the render meanwhile
        public async Task<RenderResult> Render(RenderRequest req, Node3D terrain = null) {
            if (!Available) throw new System.InvalidOperationException("rendering needs a window (the program was started with --headless)");
            var w = Mathf.Clamp(req.width, 16, MaxSize);
            var h = Mathf.Clamp(req.height, 16, MaxSize);
            await running.WaitAsync();
            var tree = GetTree();
            var sv = new SubViewport {
                Name = "Render",
                Size = new Vector2I(w, h),
                Msaa3D = Viewport.Msaa.Msaa4X,
                TransparentBg = req.transparent,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                GuiDisableInput = true
            };
            var isolated = req.isolated != null && req.mode != CameraMode.Editor;
            if (isolated) sv.OwnWorld3D = true;
            else sv.World3D = GetViewport().FindWorld3D();
            var cam = new Camera3D { CullMask = req.cullMask };
            sv.AddChild(cam);
            AddChild(sv);
            cam.MakeCurrent();
            if (isolated) {
                sv.AddChild(req.isolated);
                sv.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), Layers = GameEngine.Camera.RenderOnlyLayer });
                cam.Environment = new Godot.Environment {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = new Color(0.35f, 0.37f, 0.4f),
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = new Color(0.45f, 0.45f, 0.45f)
                };
            } else {
                GameEngine.Lighting.AddSunLayers(this, GameEngine.Camera.RenderOnlyLayer);
            }
            if (req.background != null || req.transparent) {
                var env = (cam.Environment ?? sv.World3D?.Environment ?? sv.World3D?.FallbackEnvironment)?.Duplicate() as Godot.Environment ?? new Godot.Environment();
                env.BackgroundMode = req.transparent ? Godot.Environment.BGMode.ClearColor : Godot.Environment.BGMode.Color;
                env.BackgroundColor = req.background ?? new Color(0, 0, 0, 0);
                cam.Environment = env;
            }
            var res = new RenderResult { mode = req.mode };
            try {
                var frame = req.frame ?? (isolated ? CameraFraming.NodeAabb(req.isolated) : null) ?? new Aabb(new Vector3(-100, 0, -100), new Vector3(200, 0, 200));
                Place(cam, req, frame, w, h, res);
                if (req.bestLod) {
                    //an orthogonal camera of no height sees every group as infinitely big: the most detailed LOD
                    LODUpdater.SetOverride(new LODUpdater.CameraData { orthogonal = true, orthoHeight = 0 });
                    GameEngine.Camera.SuspendLayerCullDistances(tree, true);
                } else {
                    LODUpdater.SetOverride(CameraDataOf(cam, w, h));
                }
                if (!isolated && terrain != null) GameEngine.Terrain.SetCamera(terrain, cam);
                //the LODs and the terrain follow the camera
                for (int i = 0; i < 3; i++) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                AddHighlights(sv, req);
                var canvas = new OverlayCanvas { Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
                canvas.draw = c => DrawOverlays(c, cam, req, frame, w, h);
                sv.AddChild(canvas);
                foreach (var o in req.overlays) {
                    var p = Project(cam, o.points[0]);
                    res.overlays.Add(new PlacedOverlay { overlay = o, pixel = p, inView = p != null && p.Value.X >= 0 && p.Value.Y >= 0 && p.Value.X < w && p.Value.Y < h });
                }
                canvas.QueueRedraw();
                await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                res.image = sv.GetTexture().GetImage();
                res.cameraTransform = cam.GlobalTransform;
                res.fov = cam.Fov;
                res.orthogonal = cam.Projection == Camera3D.ProjectionType.Orthogonal;
                return res;
            } finally {
                LODUpdater.SetOverride(null);
                GameEngine.Camera.SuspendLayerCullDistances(tree, false);
                if (!isolated && terrain != null) GameEngine.Terrain.SetCamera(terrain, GetViewport().GetCamera3D());
                if (isolated) sv.RemoveChild(req.isolated);
                sv.QueueFree();
                running.Release();
            }
        }

        static void Place(Camera3D cam, RenderRequest req, Aabb frame, int w, int h, RenderResult res) {
            var center = frame.GetCenter();
            var aspect = (float)w / h;
            switch (req.mode) {
                case CameraMode.Top: {
                    cam.Projection = Camera3D.ProjectionType.Orthogonal;
                    cam.KeepAspect = Camera3D.KeepAspectEnum.Height;
                    var size = Mathf.Max(frame.Size.Z, frame.Size.X / aspect) * req.margin;
                    cam.Size = size > 0.01f ? size : 10;
                    var top = frame.End.Y + 100;
                    //looking down, with Godot's -Z (the program's +z) up in the image
                    cam.GlobalTransform = new Transform3D(new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0)), new Vector3(center.X, top, center.Z));
                    cam.Near = 0.5f;
                    cam.Far = top - frame.Position.Y + 1000;
                    res.metersPerPixel = cam.Size / h;
                    res.center = center;
                    break;
                }
                case CameraMode.Orbit: {
                    cam.Fov = req.fov;
                    var size = Mathf.Max(Mathf.Max(frame.Size.X, frame.Size.Z), frame.Size.Y);
                    var distance = req.distance ?? CameraFraming.FitDistance(cam, new Vector2(w, h), Mathf.Max(size, 1), req.margin);
                    CameraFraming.LookAt(cam, center, distance, req.pitch, req.yaw);
                    cam.Near = 0.1f;
                    cam.Far = Mathf.Max(5000, distance * 2 + size * 2);
                    break;
                }
                case CameraMode.Custom: {
                    cam.Fov = req.fov;
                    cam.GlobalPosition = req.position;
                    var dir = req.target - req.position;
                    if (dir.LengthSquared() > 1e-6f) cam.LookAt(req.target, Mathf.Abs(dir.Normalized().Y) > 0.999f ? Vector3.Forward : Vector3.Up);
                    cam.Near = 0.1f;
                    cam.Far = 10000;
                    break;
                }
                case CameraMode.Editor: {
                    var main = cam.GetTree().Root.GetCamera3D();
                    if (main == null) break;
                    cam.Projection = main.Projection;
                    cam.KeepAspect = main.KeepAspect;
                    cam.Fov = main.Fov;
                    cam.Size = main.Size;
                    cam.Near = main.Near;
                    cam.Far = main.Far;
                    cam.GlobalTransform = main.GlobalTransform;
                    break;
                }
            }
        }

        static LODUpdater.CameraData CameraDataOf(Camera3D cam, int w, int h) {
            var res = new LODUpdater.CameraData { orthogonal = cam.Projection == Camera3D.ProjectionType.Orthogonal };
            if (res.orthogonal) res.orthoHeight = cam.Size;
            else {
                res.position = cam.GlobalPosition;
                res.halfAngleTan = Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
            }
            return res;
        }

        //copies of the visible meshes of the nodes on the render-only layer, tinted and drawn over everything
        static void AddHighlights(SubViewport sv, RenderRequest req) {
            if (req.highlights.Count == 0) return;
            var material = new StandardMaterial3D {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = req.highlightColor,
                NoDepthTest = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                RenderPriority = 10
            };
            var root = new Node3D { Name = "Highlights" };
            sv.AddChild(root);
            foreach (var node in req.highlights) {
                if (!IsInstanceValid(node)) continue;
                var meshes = new List<Node>(node.FindChildren("*", nameof(MeshInstance3D), true, false));
                if (node is MeshInstance3D) meshes.Add(node);
                foreach (var m in meshes) {
                    var mi = (MeshInstance3D)m;
                    if (mi is GameEngine.LineRenderer || mi.Mesh == null || !mi.IsVisibleInTree()) continue;
                    var copy = new MeshInstance3D {
                        Mesh = mi.Mesh,
                        Layers = GameEngine.Camera.RenderOnlyLayer,
                        MaterialOverride = material,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
                    };
                    root.AddChild(copy);
                    copy.GlobalTransform = mi.GlobalTransform;
                }
            }
        }

        static Vector2? Project(Camera3D cam, Vector3 point) {
            if (cam.IsPositionBehind(point)) return null;
            return cam.UnprojectPosition(point);
        }

        static void DrawOverlays(Control c, Camera3D cam, RenderRequest req, Aabb frame, int w, int h) {
            var font = ThemeDB.FallbackFont;
            var placed = new List<Rect2>();
            if (req.gridSpacing > 0) DrawGrid(c, cam, req, frame, w, h, font, placed);
            //the lines first, so that the labels and markers stay readable over them
            var ordered = new List<Overlay>(req.overlays);
            ordered.Sort((a, b) => (a.kind == Overlay.Kind.Polyline ? 0 : 1).CompareTo(b.kind == Overlay.Kind.Polyline ? 0 : 1));
            foreach (var o in ordered) {
                switch (o.kind) {
                    case Overlay.Kind.Polyline: {
                        var width = o.size > 0 ? o.size : 2;
                        var n = o.points.Length;
                        for (int i = 0; i < (o.closed ? n : n - 1); i++) DrawSegment(c, cam, o.points[i], o.points[(i + 1) % n], o.color, width);
                        if (!string.IsNullOrEmpty(o.text)) {
                            var p = Project(cam, o.points[0]);
                            if (p != null) DrawLabel(c, font, p.Value, o.text, o.color, (int)req.labelSize, placed);
                        }
                        break;
                    }
                    case Overlay.Kind.Marker: {
                        var p = Project(cam, o.points[0]);
                        if (p == null) break;
                        var r = o.size > 0 ? o.size : 6;
                        c.DrawCircle(p.Value, r, o.color);
                        c.DrawArc(p.Value, r, 0, Mathf.Tau, 24, Colors.Black, 1.5f, true);
                        if (!string.IsNullOrEmpty(o.text)) DrawLabel(c, font, p.Value + new Vector2(0, -r - req.labelSize * 0.8f), o.text, Colors.White, (int)req.labelSize, placed);
                        break;
                    }
                    case Overlay.Kind.Label: {
                        var p = Project(cam, o.points[0]);
                        if (p != null) DrawLabel(c, font, p.Value, o.text ?? "", o.color, (int)(o.size > 0 ? o.size : req.labelSize), placed);
                        break;
                    }
                }
            }
            if (req.scaleBar && cam.Projection == Camera3D.ProjectionType.Orthogonal) DrawScaleBar(c, font, cam.Size / h, w, h, (int)req.labelSize);
        }

        static void DrawSegment(Control c, Camera3D cam, Vector3 a, Vector3 b, Color color, float width) {
            var pa = Project(cam, a);
            var pb = Project(cam, b);
            if (pa != null && pb != null) c.DrawLine(pa.Value, pb.Value, color, width, true);
        }

        //a text on a dark box centered on the point, or moved up or down to the nearest place not covering the labels
        //placed before (then with a line to the point)
        static void DrawLabel(Control c, Font font, Vector2 at, string text, Color color, int size, List<Rect2> placed) {
            var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
            var box = new Rect2(at - textSize * 0.5f - new Vector2(3, 2), textSize + new Vector2(6, 4));
            var step = box.Size.Y + 2;
            var moved = box;
            for (int i = 0; i < 9; i++) {
                var offset = (i + 1) / 2 * (i % 2 == 1 ? -step : step);
                var candidate = new Rect2(box.Position + new Vector2(0, offset), box.Size);
                if (!placed.Exists(r => r.Intersects(candidate))) {
                    moved = candidate;
                    break;
                }
            }
            placed.Add(moved);
            if (moved.Position != box.Position) {
                var edge = new Vector2(at.X, moved.Position.Y < at.Y ? moved.End.Y : moved.Position.Y);
                c.DrawLine(at, edge, new Color(0, 0, 0, 0.8f), 1.5f, true);
                c.DrawCircle(at, 2.5f, new Color(0, 0, 0, 0.8f));
            }
            c.DrawRect(moved, new Color(0, 0, 0, 0.65f));
            c.DrawString(font, new Vector2(moved.Position.X + 3, moved.Position.Y + 2 + font.GetAscent(size)), text, HorizontalAlignment.Left, -1, size, color);
        }

        //a line visible on dark and light backgrounds
        static void DrawGridSegment(Control c, Camera3D cam, Vector3 a, Vector3 b) {
            DrawSegment(c, cam, a, b, new Color(0, 0, 0, 0.3f), 2.5f);
            DrawSegment(c, cam, a, b, new Color(1, 1, 1, 0.5f), 1);
        }

        //lines every gridSpacing meters, labeled with their program coordinate (z is the opposite of Godot's)
        static void DrawGrid(Control c, Camera3D cam, RenderRequest req, Aabb frame, int w, int h, Font font, List<Rect2> placed) {
            float minX, maxX, minZ, maxZ;
            var top = cam.Projection == Camera3D.ProjectionType.Orthogonal && req.mode == CameraMode.Top;
            if (top) {
                var halfH = cam.Size * 0.5f;
                var halfW = halfH * w / h;
                minX = cam.GlobalPosition.X - halfW; maxX = cam.GlobalPosition.X + halfW;
                minZ = cam.GlobalPosition.Z - halfH; maxZ = cam.GlobalPosition.Z + halfH;
            } else {
                minX = frame.Position.X; maxX = frame.End.X;
                minZ = frame.Position.Z; maxZ = frame.End.Z;
            }
            var spacing = req.gridSpacing;
            while ((maxX - minX) / spacing > 200 || (maxZ - minZ) / spacing > 200) spacing *= 2;
            var y = req.gridHeight;
            var size = (int)(req.labelSize * 0.85f);
            var labels = new List<(Vector2 at, string text)>();
            for (var x = Mathf.Ceil(minX / spacing) * spacing; x <= maxX; x += spacing) {
                DrawGridSegment(c, cam, new Vector3(x, y, minZ), new Vector3(x, y, maxZ));
                var p = Project(cam, new Vector3(x, y, top ? minZ : maxZ));
                //on the top edge, after the column of the z labels
                if (p != null && (!top || p.Value.X > size * 7)) labels.Add((new Vector2(p.Value.X, top ? size : p.Value.Y), "x " + x.ToString("0")));
            }
            for (var z = Mathf.Ceil(minZ / spacing) * spacing; z <= maxZ; z += spacing) {
                DrawGridSegment(c, cam, new Vector3(minX, y, z), new Vector3(maxX, y, z));
                var p = Project(cam, new Vector3(minX, y, z));
                //on the left edge, below the row of the x labels
                if (p != null && (!top || p.Value.Y > size * 2.5f)) labels.Add((new Vector2(top ? size * 3 : p.Value.X, p.Value.Y), "z " + (-z).ToString("0")));
            }
            //over the lines
            foreach (var (at, text) in labels) DrawLabel(c, font, at, text, Colors.White, size, placed);
        }

        //a bar of a round length (1, 2 or 5 times a power of ten) at the bottom left
        static void DrawScaleBar(Control c, Font font, float metersPerPixel, int w, int h, int size) {
            var target = w * 0.2f * metersPerPixel;
            var power = Mathf.Pow(10, Mathf.Floor(Mathf.Log(target) / Mathf.Log(10)));
            var length = power;
            foreach (var m in new[] { 2, 5, 10 }) if (power * m <= target) length = power * m;
            var pixels = length / metersPerPixel;
            var start = new Vector2(16, h - 20);
            c.DrawRect(new Rect2(start - new Vector2(4, size + 10), new Vector2(pixels + 8, size + 18)), new Color(0, 0, 0, 0.55f));
            c.DrawLine(start, start + new Vector2(pixels, 0), Colors.White, 3);
            c.DrawLine(start + new Vector2(0, -5), start + new Vector2(0, 5), Colors.White, 2);
            c.DrawLine(start + new Vector2(pixels, -5), start + new Vector2(pixels, 5), Colors.White, 2);
            c.DrawString(font, start + new Vector2(0, -8), length.ToString("0.##") + " m", HorizontalAlignment.Left, -1, size, Colors.White);
        }
    }
}

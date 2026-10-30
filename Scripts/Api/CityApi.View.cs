using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Rendering;
using GO = GameEngine.GameObject;
using GV = GameEngine.Vector3;

namespace Api {
    //Something drawn over a render (view.render): a label, a marker or a line, at a position, along points or at an element
    public class ViewOverlay {
        //label, marker or polyline
        public string type;
        public string text;
        public Vector3? position;
        public List<Vector3> points;
        public ElementRef element;
        public string color;
        public float? size;
        public bool closed = false;
    }

    //Images of the city: offscreen renders (Rendering.CityRenderer) and the view of the editor
    public partial class CityApi {
        static readonly string[] LabelKinds = { "roads", "intersections", "patches", "lines", "meshes" };

        void RequireRenderer() {
            if (!CityRenderer.Available) throw new ApiException("no_renderer", "rendering needs a window: run the program without --headless");
        }

        //the node shown for an element (its meshes)
        internal static Node3D NodeOf(object e) {
            switch (e) {
                case Intersection i: return i.geo;
                case Node3D n: return n;
                default: return null;
            }
        }

        //where the label of an element goes (Godot coordinates)
        static Vector3? AnchorOf(object e) {
            switch (e) {
                case Road r when r.curvePoints.Count > 0: return GV.DecodePosition(r.curvePoints[r.curvePoints.Count / 2]);
                case Intersection i: return GV.DecodePosition(GO.GetPosition(i.point));
            }
            var node = NodeOf(e);
            if (node == null) return null;
            return CameraFraming.NodeAabb(node)?.GetCenter() ?? node.GlobalPosition;
        }

        static Aabb? Merge(Aabb? a, Aabb? b) {
            if (a == null) return b;
            if (b == null) return a;
            return a.Value.Merge(b.Value);
        }

        //the bounds of what is shown of an element (Godot coordinates), null if nothing
        internal static Aabb? BoundsOf(object e) {
            var node = NodeOf(e);
            var res = node == null ? null : CameraFraming.NodeAabb(node);
            if (e is Road r) foreach (var p in r.curvePoints) res = Merge(res, new Aabb(GV.DecodePosition(p), Vector3.Zero));
            var anchor = AnchorOf(e);
            if (anchor != null) res = Merge(res, new Aabb(anchor.Value, Vector3.Zero));
            return res;
        }

        //the bounds of every element of the city, or of the terrain if there is none
        Aabb CityBounds() {
            Aabb? res = null;
            foreach (var e in AllElements(M)) {
                if (History.ElementSnapshot.IsAlive(e)) res = Merge(res, BoundsOf(e));
            }
            if (res != null) return res.Value;
            var half = CityGroundHelper.terrainSize * 0.5f;
            return new Aabb(new Vector3(-half, 0, -half), new Vector3(half * 2, 0, half * 2));
        }

        static Color ParseColor(string text, Color fallback) {
            if (string.IsNullOrEmpty(text)) return fallback;
            if (Color.HtmlIsValid(text)) return Color.FromHtml(text);
            var res = Color.FromString(text, new Color(-1, -1, -1));
            if (res.R < 0) throw ApiException.InvalidValue("invalid color '" + text + "' (#rrggbb, #rrggbbaa or a color name)");
            return res;
        }

        static CameraMode ParseCamera(string camera) {
            switch (camera) {
                case "top": return CameraMode.Top;
                case "orbit": return CameraMode.Orbit;
                case "custom": return CameraMode.Custom;
                case "editor": return CameraMode.Editor;
                default: throw ApiException.InvalidValue("camera is top, orbit, custom or editor");
            }
        }

        static void CheckFormat(string format) {
            if (format != "png" && format != "jpg") throw ApiException.InvalidValue("format is png or jpg");
        }

        //the image as base64 (path null) or saved to the path
        static object Encode(Image image, string format, float quality, string path, Dictionary<string, object> res) {
            res["width"] = image.GetWidth();
            res["height"] = image.GetHeight();
            res["format"] = format;
            var bytes = format == "jpg" ? image.SaveJpgToBuffer(quality) : image.SavePngToBuffer();
            if (path != null) {
                CreateParentFolder(path);
                File.WriteAllBytes(path, bytes);
                res["path"] = Path.GetFullPath(path);
            } else {
                res["image"] = System.Convert.ToBase64String(bytes);
            }
            return res;
        }

        static object Mapping(RenderResult r, int w, int h) {
            if (r.orthogonal && r.mode == CameraMode.Top) {
                var mpp = r.metersPerPixel;
                return new {
                    projection = "top",
                    metersPerPixel = mpp,
                    origin = new[] { r.center.X - w * 0.5f * mpp, -r.center.Z + h * 0.5f * mpp },
                    formula = "x = origin[0] + px * metersPerPixel, z = origin[1] - py * metersPerPixel (px, py: pixels from the top left)"
                };
            }
            var forward = -r.cameraTransform.Basis.Z;
            return new {
                projection = r.orthogonal ? "orthogonal" : "perspective",
                position = GV.EncodePosition(r.cameraTransform.Origin),
                forward = GV.EncodePosition(forward),
                up = GV.EncodePosition(r.cameraTransform.Basis.Y),
                fov = r.fov
            };
        }

        [ApiMethod("view.render", "Renders the city offscreen (the view of the editor doesn't change) and returns the image (base64, or saved to path) with " +
            "the mapping between its pixels and the city coordinates, and the pixel positions of the labels and overlays. " +
            "camera: top (orthogonal, x right and z up in the image), orbit (pitch and yaw in degrees, distance), custom (position, target, fov) or editor. " +
            "The area shown: the elements, bounds [minX, minZ, maxX, maxZ], center and size, or the whole city. " +
            "layers: buildings, terrain, geometry (roads, intersections, patches), meshes, props, helpers (default: all but helpers). " +
            "labels: roads, intersections, patches, lines, meshes or all, labeled with name and id. highlight: elements tinted with highlightColor. " +
            "overlays: {type: label|marker|polyline, text, position or points or element, color, size, closed}. grid: line spacing in meters. " +
            "wireframe: the meshes as wireframe, on a plain background unless background or transparent. Needs a window (not --headless)")]
        public async Task<object> Render(string camera = "top", List<ElementRef> elements = null, List<float> bounds = null, Vector3? center = null, float? size = null,
            float margin = 1.1f, float pitch = 50, float yaw = 30, float? distance = null, Vector3? position = null, Vector3? target = null, float fov = 50,
            int width = 1024, int height = 1024, List<string> layers = null, List<string> labels = null, float labelSize = 14,
            List<ElementRef> highlight = null, string highlightColor = null, List<ViewOverlay> overlays = null, float grid = 0, bool scaleBar = false,
            string background = null, bool transparent = false, bool bestLod = true, bool wireframe = false, string format = "png", float quality = 0.9f, string path = null) {
            RequireCity();
            RequireRenderer();
            CheckFormat(format);
            if (width < 16 || height < 16 || width > CityRenderer.MaxSize || height > CityRenderer.MaxSize) {
                throw ApiException.InvalidValue("width and height are between 16 and " + CityRenderer.MaxSize);
            }
            var req = new RenderRequest {
                width = width, height = height, mode = ParseCamera(camera), margin = margin, pitch = pitch, yaw = yaw, distance = distance, fov = fov,
                labelSize = labelSize, gridSpacing = grid, scaleBar = scaleBar, transparent = transparent, bestLod = bestLod,
                wireframe = wireframe
            };
            req.cullMask = RenderLayers.MaskOf(layers, out var unknownLayer);
            if (unknownLayer != null) throw ApiException.InvalidValue("unknown layer '" + unknownLayer + "' (" + string.Join(", ", RenderLayers.Groups.Keys) + ")");
            if (background != null) req.background = ParseColor(background, Colors.Black);
            if (highlightColor != null) req.highlightColor = ParseColor(highlightColor, req.highlightColor);
            if (req.mode == CameraMode.Custom) {
                if (position == null || target == null) throw ApiException.InvalidParam("the custom camera needs position and target");
                req.position = GV.DecodePosition(position.Value);
                req.target = GV.DecodePosition(target.Value);
            }

            //the area shown
            var cityBounds = CityBounds();
            if (elements != null && elements.Count > 0) {
                Aabb? frame = null;
                foreach (var r in elements) {
                    var b = BoundsOf(Resolve(r));
                    if (b == null) throw ApiException.InvalidValue("the element " + r + " has nothing to show");
                    frame = Merge(frame, b);
                }
                req.frame = frame;
            } else if (bounds != null) {
                if (bounds.Count != 4 || bounds[2] <= bounds[0] || bounds[3] <= bounds[1]) throw ApiException.InvalidValue("bounds is [minX, minZ, maxX, maxZ]");
                req.frame = new Aabb(new Vector3(bounds[0], cityBounds.Position.Y, -bounds[3]), new Vector3(bounds[2] - bounds[0], cityBounds.Size.Y, bounds[3] - bounds[1]));
            } else if (center != null) {
                var s = size ?? 200;
                if (s <= 0) throw ApiException.InvalidValue("size must be positive");
                var c = GV.DecodePosition(center.Value);
                req.frame = new Aabb(new Vector3(c.X - s * 0.5f, cityBounds.Position.Y, c.Z - s * 0.5f), new Vector3(s, cityBounds.Size.Y, s));
            } else {
                req.frame = cityBounds;
            }
            //the camera of the top view stays above everything
            if (req.mode == CameraMode.Top) {
                var f = req.frame.Value;
                var top = Mathf.Max(f.End.Y, cityBounds.End.Y);
                var bottom = Mathf.Min(f.Position.Y, cityBounds.Position.Y);
                req.frame = new Aabb(new Vector3(f.Position.X, bottom, f.Position.Z), new Vector3(f.Size.X, top - bottom, f.Size.Z));
            }

            if (highlight != null) {
                foreach (var r in highlight) {
                    var node = NodeOf(Resolve(r));
                    if (node == null) throw ApiException.InvalidValue("the element " + r + " can't be highlighted");
                    req.highlights.Add(node);
                }
            }

            //labels of the elements
            var labeled = new List<(object element, Overlay overlay)>();
            if (labels != null) {
                var kinds = new HashSet<string>(labels);
                if (kinds.Contains("all")) kinds.UnionWith(LabelKinds);
                kinds.Remove("all");
                kinds.Remove("none");
                foreach (var k in kinds) {
                    if (System.Array.IndexOf(LabelKinds, k) < 0) throw ApiException.InvalidValue("labels are " + string.Join(", ", LabelKinds) + " or all");
                }
                void Add<T>(string kind, List<T> list) {
                    if (!kinds.Contains(kind)) return;
                    foreach (var e in list) {
                        if (!History.ElementSnapshot.IsAlive(e)) continue;
                        var anchor = AnchorOf(e);
                        if (anchor == null) continue;
                        var o = new Overlay { kind = Overlay.Kind.Label, points = new[] { anchor.Value }, text = GetName(e) + " #" + IdOf(e), tag = e };
                        labeled.Add((e, o));
                        req.overlays.Add(o);
                    }
                }
                Add("roads", M.roads);
                Add("intersections", M.intersections);
                Add("patches", M.patches);
                Add("lines", M.buildings);
                Add("meshes", M.meshes);
            }

            //the overlays given
            var given = new List<Overlay>();
            if (overlays != null) {
                for (int i = 0; i < overlays.Count; i++) {
                    var ov = overlays[i];
                    var o = new Overlay { text = ov.text, closed = ov.closed, size = ov.size ?? 0, tag = i };
                    switch (ov.type) {
                        case "label": o.kind = Overlay.Kind.Label; break;
                        case "marker": o.kind = Overlay.Kind.Marker; break;
                        case "polyline": o.kind = Overlay.Kind.Polyline; break;
                        default: throw ApiException.InvalidValue("overlay " + i + ": type is label, marker or polyline");
                    }
                    o.color = ParseColor(ov.color, o.kind == Overlay.Kind.Label ? Colors.White : new Color(1, 0.85f, 0.1f));
                    if (ov.points != null && ov.points.Count > 0) {
                        o.points = ov.points.ConvertAll(GV.DecodePosition).ToArray();
                    } else if (ov.position != null) {
                        o.points = new[] { GV.DecodePosition(ov.position.Value) };
                    } else if (ov.element != null) {
                        var e = Resolve(ov.element);
                        if (o.kind == Overlay.Kind.Polyline && e is Road road && road.curvePoints.Count > 1) {
                            o.points = road.curvePoints.ConvertAll(GV.DecodePosition).ToArray();
                        } else {
                            var anchor = AnchorOf(e) ?? throw ApiException.InvalidValue("overlay " + i + ": the element has no position");
                            o.points = new[] { anchor };
                        }
                        if (o.text == null && o.kind == Overlay.Kind.Label) o.text = GetName(e) + " #" + IdOf(e);
                    } else {
                        throw ApiException.InvalidParam("overlay " + i + " needs position, points or element");
                    }
                    if (o.kind == Overlay.Kind.Polyline && o.points.Length < 2) throw ApiException.InvalidValue("overlay " + i + ": a polyline needs two points or more");
                    given.Add(o);
                    req.overlays.Add(o);
                }
            }

            var result = await CityRenderer.Get(builder).Render(req, builder.helper.terrainObj);
            var res = new Dictionary<string, object>();
            res["mapping"] = Mapping(result, width, height);
            var labelList = new List<object>();
            var overlayList = new List<object>();
            foreach (var p in result.overlays) {
                var px = p.pixel == null ? null : new[] { Mathf.Round(p.pixel.Value.X), Mathf.Round(p.pixel.Value.Y) };
                if (p.overlay.tag is int index) overlayList.Add(new { index, pixel = px, inView = p.inView });
                else labelList.Add(new { id = IdOf(p.overlay.tag), kind = KindOf(p.overlay.tag), name = GetName(p.overlay.tag), pixel = px, inView = p.inView });
            }
            if (labels != null) res["labels"] = labelList;
            if (overlays != null) res["overlays"] = overlayList;
            return Encode(result.image, format, quality, path, res);
        }

        [ApiMethod("view.screenshot", "The view of the editor as the user sees it (with its panels), as base64 or saved to path. Needs a window (not --headless)")]
        public async Task<object> Screenshot(string format = "png", float quality = 0.9f, string path = null) {
            RequireRenderer();
            CheckFormat(format);
            await builder.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = builder.GetViewport().GetTexture().GetImage();
            return Encode(image, format, quality, path, new Dictionary<string, object>());
        }

        CameraController EditorCamera() {
            return builder.GetViewport().GetCamera3D() as CameraController ?? throw new ApiException("no_camera", "the editor camera isn't available");
        }

        [ApiMethod("view.camera.get", "The camera of the editor: position, forward direction, fov (vertical, degrees)")]
        public object GetCamera() {
            var cam = EditorCamera();
            return new {
                position = GV.EncodePosition(cam.GlobalPosition),
                forward = GV.EncodePosition(-cam.GlobalBasis.Z),
                fov = cam.Fov
            };
        }

        [ApiMethod("view.camera.set", "Moves the camera of the editor to the position, looking at the target")]
        public object SetCamera(Vector3 position, Vector3 target) {
            EditorCamera().SetView(GV.DecodePosition(position), GV.DecodePosition(target));
            return GetCamera();
        }

        [ApiMethod("view.toggles", "The visibility toggles of the core (in the View menu) and whether they're on")]
        public object GetToggles() {
            RequireCity();
            var res = new List<object>();
            foreach (var t in ViewToggles.List()) res.Add(new { name = t.name, label = StringManager.Get(t.label ?? t.name), on = ViewToggles.IsOn(t.name) });
            return res;
        }

        [ApiMethod("view.setToggle", "Turns a visibility toggle of the core (see view.toggles) on or off; saved with the city preferences, not an undo step")]
        public object SetToggle(string name, bool on) {
            RequireCity();
            if (!M.SetViewToggle(name, on)) throw ApiException.NotFound("view toggle '" + name + "'");
            return on;
        }

        [ApiMethod("view.terrainHidden", "Whether the terrain is hidden (View->Hide terrain grid)")]
        public object GetTerrainHidden() {
            RequireCity();
            return CityGroundHelper.TerrainHidden;
        }

        [ApiMethod("view.setTerrainHidden", "Hides or shows the terrain (View->Hide terrain grid): a hidden terrain isn't drawn and the clicks and the placement snaps skip it, while the roads and the projected geometry still follow it; saved with the city preferences, not an undo step")]
        public object SetTerrainHidden(bool hidden) {
            RequireCity();
            builder.helper.SetTerrainHidden(hidden);
            return hidden;
        }

        [ApiMethod("view.focus","Moves the camera of the editor to show an element (as the zoom of the object list)")]
        public object Focus(ElementRef @ref) {
            RequireCity();
            var b = BoundsOf(Resolve(@ref)) ?? throw ApiException.InvalidValue("the element has nothing to show");
            EditorCamera().ZoomIn(b.GetCenter(), Mathf.Max(b.Size.Length(), 10));
            return null;
        }
    }
}

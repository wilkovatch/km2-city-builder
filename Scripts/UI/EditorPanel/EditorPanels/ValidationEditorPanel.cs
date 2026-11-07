using System.Collections.Generic;
using Godot;
using Newtonsoft.Json.Linq;
using UIObject = Godot.Control;
using SM = StringManager;
using GO = GameEngine.GameObject;

namespace EditorPanels {
    //The warnings and errors of a validation of the city (File > Validate city), of an export or of a check of the core
    //(see Operations.Checks), as reported by the core (see Files/python/ExportReport.py): selecting one highlights its
    //element, if it names one. The lines of their overlays (e.g. the route of a vehicle) are drawn over the city while
    //the panel is open, the ones of the selected item highlighted
    public class ValidationEditorPanel : EditorPanel {
        const int RowLength = 48;
        //the width of the overlay lines 20 m away from the camera (they keep the same width on screen), and of the
        //selected one
        const float OverlayWidth = 0.25f;
        const float OverlaySelectedWidth = 0.6f;
        const float OverlayHeight = 0.3f;

        List<JObject> items = new List<JObject>();
        EditorPanelElements.Label summary;
        EditorPanelElements.ScrollList lst;
        EditorPanelElements.Label message;
        int curI = -1;
        System.Action rerun;
        Node3D overlayRoot;
        //each polyline is a solid line and a faint one drawn through what covers it
        readonly List<(int item, GameEngine.LineRenderer line, GameEngine.LineRenderer covered, Color color, List<Vector3> points)> overlayLines = new();

        public override void Initialize(UIObject canvas) {
            Initialize(canvas, 1);
            var p0 = GetPage(0);
            summary = p0.AddLabel("", 1.5f);
            p0.IncreaseRow();
            lst = p0.AddScrollList(SM.Get("VALIDATION_TITLE"), new List<string>(), Select, 1.5f, SM.Get("VALIDATION_TOOLTIP"));
            p0.IncreaseRow(5.0f);
            message = p0.AddLabel("", 1.5f, null, null, 2.0f);
            p0.IncreaseRow(2.0f);
            p0.AddButton(SM.Get("VALIDATION_SELECT"), SelectMenu, 0.75f);
            p0.AddButton(SM.Get("VALIDATION_ZOOM_TO"), ZoomTo, 0.75f);
            p0.IncreaseRow();
            p0.AddButton(SM.Get("VALIDATION_RERUN"), Rerun, 0.75f);
            p0.AddButton(SM.Get("CLOSE"), Close, 0.75f);
        }

        //shows the items (title: of the validation, of the export or of the check; rerun: runs it again, the validation
        //of the city if null)
        public void Show(List<JObject> newItems, string title, System.Action rerun = null) {
            items = newItems ?? new List<JObject>();
            this.rerun = rerun;
            builder.DeselectObject();
            SetActive(true);
            int errors = 0, warnings = 0;
            var rows = new List<string>();
            var colors = new List<Color>();
            foreach (var item in items) {
                var level = item["level"]?.Value<string>();
                if (level == "error") errors++;
                else if (level == "warning") warnings++;
                var element = item["element"]?["name"]?.Value<string>();
                //the whole message is shown below the list when selected
                var row = (level == "error" ? "[E] " : level == "warning" ? "[W] " : "") + (element != null ? element + ": " : "") + item["message"]?.Value<string>();
                rows.Add(row.Length > RowLength ? row.Substring(0, RowLength - 3) + "..." : row);
                colors.Add(level == "error" ? new Color(0.75f, 0.1f, 0.08f) : level == "warning" ? new Color(0.65f, 0.42f, 0) : Colors.Black);
            }
            lst.SetItems(rows, null, colors);
            lst.Deselect();
            curI = -1;
            summary.SetValue(title + ": " + (items.Count == 0 ? SM.Get("VALIDATION_NONE")
                : SM.Get("VALIDATION_SUMMARY").Replace("$ERRORS", errors.ToString()).Replace("$WARNINGS", warnings.ToString())));
            message.SetValue("");
            BuildOverlay();
        }

        public override void SetActive(bool active) {
            base.SetActive(active);
            if (!active) ClearOverlay();
        }

        //the element named by the item, if it still exists
        object ElementOf(int i) {
            if (i < 0 || i >= items.Count || !(items[i]["element"] is JObject e)) return null;
            try {
                return Api.ApiHost.Instance.Api.Resolve(new Api.ElementRef { name = e["name"]?.Value<string>(), kind = e["kind"]?.Value<string>() });
            } catch (Api.ApiException) {
                return null;
            }
        }

        static Vector3? ReadPoint(JToken token) {
            if (!(token is JArray a) || a.Count != 3) return null;
            try {
                var v = new Vector3((float)a[0], (float)a[1], (float)a[2]);
                return v.IsFinite() ? v : null;
            } catch (System.Exception) {
                return null;
            }
        }

        void Select(int i) {
            curI = i;
            message.SetValue(i >= 0 && i < items.Count ? items[i]["message"]?.Value<string>() ?? "" : "");
            var node = Api.CityApi.NodeOf(ElementOf(i));
            if (node != null) builder.SelectObject(node, false, null);
            else builder.DeselectObject();
            HighlightOverlay();
        }

        void SelectMenu() {
            var node = Api.CityApi.NodeOf(ElementOf(curI));
            if (node != null) builder.SelectObject(node, true, null);
        }

        //to the position of the item (where the problem is), or else its overlay or its element
        void ZoomTo() {
            if (curI < 0 || curI >= items.Count) return;
            Aabb? bounds = null;
            var position = ReadPoint(items[curI]["position"]);
            if (position != null) {
                bounds = new Aabb(GameEngine.Vector3.DecodePosition(position.Value), Vector3.Zero);
            } else {
                foreach (var (item, _, _, _, points) in overlayLines) {
                    if (item != curI) continue;
                    foreach (var p in points) {
                        var b = new Aabb(GameEngine.Vector3.DecodePosition(p), Vector3.Zero);
                        bounds = bounds == null ? b : bounds.Value.Merge(b);
                    }
                }
                var element = ElementOf(curI);
                if (bounds == null && element != null) bounds = Api.CityApi.BoundsOf(element);
            }
            if (bounds == null) return;
            var camera = GameEngine.Scriptable.Cast<CameraController>(GameEngine.Camera.Get(builder));
            camera?.ZoomIn(bounds.Value.GetCenter(), Mathf.Max(bounds.Value.Size.Length(), 10));
        }

        // Overlay

        void ClearOverlay() {
            if (overlayRoot != null && GodotObject.IsInstanceValid(overlayRoot)) overlayRoot.QueueFree();
            overlayRoot = null;
            overlayLines.Clear();
        }

        //the polylines of the overlays of the items: {"type": "polyline", "points": [[x, y, z], ...], "color": "#rrggbb"}
        void BuildOverlay() {
            ClearOverlay();
            for (int i = 0; i < items.Count; i++) {
                if (!(items[i]["overlay"] is JArray overlay)) continue;
                foreach (var entry in overlay) {
                    if (!(entry is JObject o) || (o["type"]?.Value<string>() ?? "polyline") != "polyline" || !(o["points"] is JArray pts)) continue;
                    var points = new List<Vector3>();
                    foreach (var p in pts) {
                        var v = ReadPoint(p);
                        if (v != null) points.Add(v.Value);
                    }
                    if (points.Count < 2) continue;
                    var text = o["color"]?.Value<string>();
                    var color = !string.IsNullOrEmpty(text) && Color.HtmlIsValid(text) ? Color.FromHtml(text) : GameEngine.Color.yellow;
                    if (overlayRoot == null) {
                        overlayRoot = new Node3D { Name = "Validation overlay" };
                        GO.GetObject(builder.helper.elementManager).AddChild(overlayRoot);
                    }
                    GameEngine.LineRenderer Line(string name) {
                        var line = GO.CreateLineRenderer(overlayRoot, name);
                        line.SetPositions(points.ToArray());
                        line.heightOffset = OverlayHeight;
                        line.miterCorners = true;
                        return line;
                    }
                    var lr = Line("Overlay " + i);
                    var covered = Line("Overlay " + i + " (covered)");
                    covered.drawnThrough = true;
                    var entryIndex = overlayLines.Count;
                    lr.frameUpdate = () => UpdateOverlayWidth(entryIndex);
                    overlayLines.Add((i, lr, covered, color, points));
                }
            }
            HighlightOverlay();
        }

        //the lines of the selected item are wider, with arrows along their direction
        void HighlightOverlay() {
            foreach (var (item, lr, covered, color, _) in overlayLines) {
                if (!GodotObject.IsInstanceValid(lr) || !GodotObject.IsInstanceValid(covered)) continue;
                var selected = item == curI;
                var c = selected ? color : color.Darkened(0.3f);
                lr.material = MaterialManager.GetHandleMaterial(c);
                covered.material = MaterialManager.GetDrawnThroughLineMaterial(c);
                lr.arrowDirection = covered.arrowDirection = selected ? 1 : 0;
            }
        }

        //the same width on screen at any distance
        void UpdateOverlayWidth(int index) {
            if (index >= overlayLines.Count) return;
            var (item, lr, covered, _, points) = overlayLines[index];
            if (!GodotObject.IsInstanceValid(lr) || !GodotObject.IsInstanceValid(covered)) return;
            var cam = GameEngine.Camera.Get(builder);
            if (cam == null) return;
            var camPos = GO.GetPosition(cam);
            var dist = camPos.DistanceTo(GeometryHelper.ClosestPointToCurve(camPos, points, out _));
            var width = (item == curI ? OverlaySelectedWidth : OverlayWidth) * Mathf.Max(dist / 20.0f, 1.0f);
            lr.startWidth = lr.endWidth = covered.startWidth = covered.endWidth = width;
        }

        void Rerun() {
            if (rerun != null) rerun.Invoke();
            else builder.ValidateCity();
        }

        void Close() {
            builder.DeselectObject();
            SetActive(false);
        }
    }
}

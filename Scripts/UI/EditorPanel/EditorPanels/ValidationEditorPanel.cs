using System.Collections.Generic;
using Godot;
using Newtonsoft.Json.Linq;
using UIObject = Godot.Control;
using SM = StringManager;

namespace EditorPanels {
    //The warnings and errors of a validation of the city (File > Validate city) or of an export, as reported by the
    //exporter of the core (see CityValidator): selecting one highlights its element, if it names one
    public class ValidationEditorPanel : EditorPanel {
        const int RowLength = 48;

        List<JObject> items = new List<JObject>();
        EditorPanelElements.Label summary;
        EditorPanelElements.ScrollList lst;
        EditorPanelElements.Label message;
        int curI = -1;

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

        //shows the items (title: of the validation or of an export)
        public void Show(List<JObject> newItems, string title) {
            items = newItems ?? new List<JObject>();
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

        void Select(int i) {
            curI = i;
            message.SetValue(i >= 0 && i < items.Count ? items[i]["message"]?.Value<string>() ?? "" : "");
            var node = Api.CityApi.NodeOf(ElementOf(i));
            if (node != null) builder.SelectObject(node, false, null);
            else builder.DeselectObject();
        }

        void SelectMenu() {
            var node = Api.CityApi.NodeOf(ElementOf(curI));
            if (node != null) builder.SelectObject(node, true, null);
        }

        void ZoomTo() {
            var element = ElementOf(curI);
            var bounds = element != null ? Api.CityApi.BoundsOf(element) : null;
            if (bounds == null) return;
            var camera = GameEngine.Scriptable.Cast<CameraController>(GameEngine.Camera.Get(builder));
            camera?.ZoomIn(bounds.Value.GetCenter(), Mathf.Max(bounds.Value.Size.Length(), 10));
        }

        void Rerun() {
            builder.ValidateCity();
        }

        void Close() {
            builder.DeselectObject();
            SetActive(false);
        }
    }
}

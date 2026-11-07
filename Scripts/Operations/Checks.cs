using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Operations {
    //Checks of elements by a check of the core (see CheckCatalog): the check reads the export JSON of the whole city (it
    //may need any element, e.g. the roads a vehicle drives on) with "check": {"name": ..., "targets": [...]} naming the
    //elements to check, and reports what it finds as the exporters do (Files/python/ExportReport.py). The items are
    //listed like the ones of a validation, with the lines they draw over the city (see the Core Checks page of the wiki)
    public static class Checks {
        public class Result {
            public bool success;
            public string error; //unknown_check, not_accepted, check_failed
            public string message;
            public List<JObject> items = new List<JObject>();
            public double durationMs;

            public static Result Fail(string error, string message) {
                return new Result { success = false, error = error, message = message };
            }
        }

        //a target: an element of the city (road, intersection, terrain patch, building line, custom element) or the state
        //of a property container (an object of the city properties with a container identity, e.g. a race)
        public static bool Accepts(CheckCatalog.CheckInfo check, object target) {
            if (check == null || target == null) return false;
            if (target is ObjectState state) {
                var entry = PropertyContainers.Find(state.Str(PropertyContainers.IdKey, ""));
                return entry != null && check.AcceptsContainer(entry.type);
            }
            return check.IsEnabled(target);
        }

        //the reference of a target in the export JSON: the elements by their index (as JSON_Export writes them, with
        //their kind), the property containers by their identity (kind propertyContainer, with their type)
        public static JObject Reference(ElementManager manager, object target) {
            if (target is ObjectState state) {
                var id = state.Str(PropertyContainers.IdKey, "");
                return new JObject { ["kind"] = "propertyContainer", ["id"] = id, ["type"] = PropertyContainers.Find(id)?.type };
            }
            int index = target switch {
                Road r => manager.roads.IndexOf(r),
                Intersection i => manager.intersections.IndexOf(i),
                TerrainPatch p => manager.patches.IndexOf(p),
                BuildingLine l => manager.buildings.IndexOf(l),
                CustomElement c => manager.customElements.IndexOf(c),
                _ => -1
            };
            return new JObject { ["kind"] = History.ElementSnapshot.ReferenceKind(target), ["id"] = index };
        }

        //runs the check on the targets (without blocking the editor): the items it reported, an error if it failed
        //without reporting one
        public static async Task<Result> Run(ElementManager manager, string checkName, List<object> targets, System.IProgress<(float, string)> progress = null) {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var check = CheckCatalog.Get(checkName);
            if (check == null) return Result.Fail("unknown_check", "unknown check " + checkName);
            if (targets == null || targets.Count == 0) return Result.Fail("not_accepted", "nothing to check");
            var references = new JArray();
            foreach (var t in targets) {
                if (!Accepts(check, t)) return Result.Fail("not_accepted", "the check " + checkName + " can't check " + Reference(manager, t)["kind"]);
                references.Add(Reference(manager, t));
            }
            var tempDir = Path.Combine(Path.GetTempPath(), "km2cb_check_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try {
                var inputPath = Path.Combine(tempDir, "input.json");
                var reportPath = Path.Combine(tempDir, "report.jsonl");
                var enc = new CityEncoder.JSON_Export {
                    report = new IO.ExportedCity.ExportReportInfo { file = reportPath, validation = true },
                    check = new JObject { ["name"] = checkName, ["targets"] = references }
                };
                var error = await enc.EncodeCityAsync(manager, inputPath, progress);
                if (error != null) return Result.Fail("check_failed", error);
                var pythonFolder = Path.Combine(PathHelper.FilesPath(true), "python");
                var progressBar = new ProgressReporter(manager.builder.helper.curProgressBar, progress);
                progressBar.SetActive(true);
                progressBar.SetText(StringManager.Get("CHECKING"));
                (int code, string data) run;
                try {
                    run = await PythonManager.RunScriptTask(check.ScriptPath, true, new List<string> { pythonFolder, inputPath });
                } finally {
                    progressBar.SetActive(false);
                }
                var (code, data) = run;
                var res = new Result { success = true };
                if (File.Exists(reportPath)) res.items = CityExporter.ReadReport(reportPath);
                if (code != 0 && !res.items.Exists(i => i["level"]?.Value<string>() == "error")) {
                    return Result.Fail("check_failed", StringManager.Get(data ?? ("exit code " + code)));
                }
                res.durationMs = watch.Elapsed.TotalMilliseconds;
                return res;
            } finally {
                try {
                    Directory.Delete(tempDir, true);
                } catch (System.Exception) { }
            }
        }
    }
}

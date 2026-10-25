using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

//Validates a city with the exporters of its core: the checks are the ones of the exporters, run as a dry export (with
//the flags of the validation section of the settings of the exporter) to a temporary folder deleted afterwards. The
//result is the list of the warnings and errors they reported (see Files/python/ExportReport.py).
public static class CityValidator {
    public class Result {
        public bool ok;
        public int errors;
        public int warnings;
        //as reported, with the format of the exporter that reported it
        public List<JObject> items = new List<JObject>();
        public List<string> exporters = new List<string>();
        public double durationMs;
    }

    //the exporters of the core that can validate a city (only the one of the format if given)
    public static List<FileBrowserHelper.CustomExporter> GetValidators(string format = null) {
        var res = new List<FileBrowserHelper.CustomExporter>();
        foreach (var exp in FileBrowserHelper.GetCustomExporters()) {
            if (exp.settings.validation == null) continue;
            if (format != null && !CityExporter.GetFormats(exp).Exists(f => string.Equals(f, format, System.StringComparison.OrdinalIgnoreCase))) continue;
            res.Add(exp);
        }
        return res;
    }

    public static async Task<Result> ValidateAsync(ElementManager manager, List<FileBrowserHelper.CustomExporter> validators, System.IProgress<(float, string)> progress = null) {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var res = new Result();
        var cityName = Path.GetFileName(PreferencesManager.workingDirectory.TrimEnd('/', '\\'));
        if (string.IsNullOrEmpty(cityName)) cityName = "city";
        foreach (var exp in validators) {
            var format = exp.settings.format;
            res.exporters.Add(format);
            var dir = Path.Combine(Path.GetTempPath(), "km2cb_validate_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                var flags = CityExporter.GetExporterFlags(exp, exp.settings.validation.flags, format);
                var export = await CityExporter.ExportWithExporterAsync(exp, flags, manager, Path.Combine(dir, cityName + "." + format), false, progress, true);
                foreach (var item in export.items) {
                    item["exporter"] = format;
                    res.items.Add(item);
                }
            } finally {
                try {
                    Directory.Delete(dir, true);
                } catch (IOException e) {
                    GameEngine.LogError("could not delete the validation folder " + dir + ": " + e.Message);
                }
            }
        }
        foreach (var item in res.items) {
            var level = item["level"]?.Value<string>();
            if (level == "error") res.errors++;
            else if (level == "warning") res.warnings++;
        }
        res.ok = res.errors == 0;
        res.durationMs = watch.Elapsed.TotalMilliseconds;
        return res;
    }
}

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public static class CityExporter {
    public class ExportResult {
        public bool success;
        public string error; //null on success
        public int exitCode;
        public string output;
        //the warnings and errors reported by the exporter: level (error, warning, info), message, and optionally element
        //({name, kind}), code, position, details (see Files/python/ExportReport.py)
        public List<JObject> items = new List<JObject>();

        public int Count(string level) => items.FindAll(i => i["level"]?.Value<string>() == level).Count;
    }

    public static void SaveCity(ElementManager manager, string name, bool gzipped = false) {
        var ext = PathHelper.GetExtension(name);
        if (ext.ToLower() == name.ToLower()) ext = "";
        var exporter = FindExporter(ext);
        if (exporter != null) {
            manager.builder.ShowCustomExportDialog(exporter, name);
            return;
        }
        switch (ext) {
            case "json":
                if (gzipped) {
                    WriteCity(new CityEncoder.JSON(gzipped), manager, name);
                } else {
                    WriteCity(new CityEncoder.JSON_Export(), manager, name);
                }
                break;
            case "":
                manager.builder.CreateAlert(StringManager.Get("ERROR"), StringManager.Get("NO_FORMAT_SPECIFIED"), StringManager.Get("OK"));
                break;
            default:
                manager.builder.CreateAlert(StringManager.Get("ERROR"), StringManager.Get("UNSUPPORTED_FORMAT"), StringManager.Get("OK"));
                break;
        }
    }

    static bool SameFormat(string a, string b) {
        return !string.IsNullOrEmpty(a) && string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
    }

    //the exporter of the core for the file extension (its format or the format of one of its flags), null if none
    public static FileBrowserHelper.CustomExporter FindExporter(string format) {
        var expList = FileBrowserHelper.GetCustomExporters();
        if (expList == null) return null;
        foreach (var elem in expList) {
            if (SameFormat(elem.settings.format, format)) return elem;
            foreach (var flag in elem.settings.flags ?? new FileBrowserHelper.CustomExporterFlags[0]) {
                if (SameFormat(flag.format, format)) return elem;
            }
        }
        return null;
    }

    //the formats of the exporter: its own and the ones of its flags
    public static List<string> GetFormats(FileBrowserHelper.CustomExporter exporter) {
        var res = new List<string> { exporter.settings.format };
        foreach (var flag in exporter.settings.flags ?? new FileBrowserHelper.CustomExporterFlags[0]) {
            if (!string.IsNullOrEmpty(flag.format)) res.Add(flag.format);
        }
        return res;
    }

    //the path with the extension of the format: the extension of another of the given formats is replaced
    //(e.g. city.psdl or city.psdl.ar to city.ar), any other one is kept as part of the name
    public static string WithFormat(string path, string format, IEnumerable<string> formats) {
        var ext = "." + format;
        var stem = path.EndsWith(ext, System.StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - ext.Length) : path;
        var current = Path.GetExtension(stem).TrimStart('.');
        foreach (var f in formats) {
            if (SameFormat(f.TrimStart('.'), current)) {
                stem = stem.Substring(0, stem.Length - current.Length - 1);
                break;
            }
        }
        return stem + ext;
    }

    //the values of the flags set by the file extension (by flag id): a flag with a format is set only for its format
    public static Dictionary<string, bool> GetFormatFlags(FileBrowserHelper.CustomExporter exporter, string format) {
        var res = new Dictionary<string, bool>();
        foreach (var flag in exporter.settings.flags ?? new FileBrowserHelper.CustomExporterFlags[0]) {
            if (!string.IsNullOrEmpty(flag.format) && flag.id != null) res[flag.id] = SameFormat(flag.format, format);
        }
        return res;
    }

    //the flags of the exporter with their default values, changed by the given ones (by flag id),
    //and the ones set by the file extension if given
    public static List<bool> GetExporterFlags(FileBrowserHelper.CustomExporter exporter, Dictionary<string, bool> values, string format = null) {
        var flags = exporter.settings.flags ?? new FileBrowserHelper.CustomExporterFlags[0];
        var forced = format != null ? GetFormatFlags(exporter, format) : new Dictionary<string, bool>();
        var res = new List<bool>();
        foreach (var flag in flags) {
            if (flag.id != null && forced.TryGetValue(flag.id, out var f)) {
                res.Add(f);
            } else {
                res.Add(values != null && flag.id != null && values.TryGetValue(flag.id, out var v) ? v : flag.defaultValue);
            }
        }
        return res;
    }

    //the ids of the flags that can't be changed with the given values of the others (like the checkboxes of the
    //export dialog: a flag enables or disables others), and the ones set by the file extension
    public static HashSet<string> GetLockedFlags(FileBrowserHelper.CustomExporter exporter, List<bool> values) {
        var flags = exporter.settings.flags ?? new FileBrowserHelper.CustomExporterFlags[0];
        var res = new HashSet<string>();
        for (int i = 0; i < flags.Length && i < values.Count; i++) {
            if (!values[i] && flags[i].enables != null) res.UnionWith(flags[i].enables);
            if (values[i] && flags[i].disables != null) res.UnionWith(flags[i].disables);
            if (!string.IsNullOrEmpty(flags[i].format) && flags[i].id != null) res.Add(flags[i].id);
        }
        return res;
    }

    public static void SaveCityWithExporter(FileBrowserHelper.CustomExporter exporter, List<bool> flags, ElementManager manager, string name) {
        _ = ExportAndShowReport(exporter, flags, manager, name);
    }

    //the export of the GUI: the warnings and errors reported are listed afterwards
    static async Task ExportAndShowReport(FileBrowserHelper.CustomExporter exporter, List<bool> flags, ElementManager manager, string name) {
        var res = await ExportWithExporterAsync(exporter, flags, manager, name, true);
        if (res.items.Count > 0) manager.builder.ShowExportReport(res.items);
    }

    //writes the export JSON next to the output file and runs the exporter on it (in its own console if showConsole);
    //validation: a dry export (to a temporary folder) only collecting the warnings and errors
    public static async Task<ExportResult> ExportWithExporterAsync(FileBrowserHelper.CustomExporter exporter, List<bool> flags, ElementManager manager, string name,
        bool showConsole, System.IProgress<(float, string)> progress = null, bool validation = false) {

        var reportFile = Path.Combine(Path.GetTempPath(), "km2cb_report_" + System.Guid.NewGuid().ToString("N") + ".jsonl");
        var enc = new CityEncoder.JSON_Export { report = new IO.ExportedCity.ExportReportInfo { file = reportFile, validation = validation } };
        //not replacing the format: the file may have the format of a flag
        var jsonFile = Path.ChangeExtension(name, ".json");
        var script = exporter.folder + "/export.py";
        var args = new List<string>();
        args.Add(Path.Combine(PathHelper.FilesPath(true), "python"));
        args.Add(jsonFile);
        args.Add(name);
        foreach (var flag in flags) {
            args.Add(flag ? "1" : "0");
        }
        var error = await enc.EncodeCityAsync(manager, jsonFile, progress);
        if (error != null) {
            if (showConsole) manager.builder.CreateAlert(StringManager.Get("ERROR"), StringManager.Get("CITY_SAVE_ERROR_TEXT") + error, StringManager.Get("OK"));
            return new ExportResult { success = false, error = error, exitCode = -1 };
        }
        var progressBar = new Operations.ProgressReporter(manager.builder.helper.curProgressBar, progress);
        progressBar.SetActive(true);
        progressBar.SetText(StringManager.Get("EXPORTING_CITY"));
        progressBar.SetProgress(0);
        try {
            var res = await PythonManager.RunScriptTask(script, true, args, showConsole);
            var result = new ExportResult { success = res.code == 0, error = res.code == 0 ? null : (res.data ?? "exit code " + res.code), exitCode = res.code, output = res.data };
            if (File.Exists(reportFile)) {
                result.items = ReadReport(reportFile);
            } else if (res.code == 0 && res.data != null) {
                //an exporter not writing a report: its printed warnings
                foreach (var line in res.data.Split('\n')) {
                    var l = line.Trim();
                    if (l.StartsWith("Warning:")) result.items.Add(Item("warning", l.Substring(8).Trim()));
                    else if (l.StartsWith("Error:")) result.items.Add(Item("error", l.Substring(6).Trim()));
                }
            }
            if (res.code != 0 && result.Count("error") == 0) result.items.Add(Item("error", result.error));
            //the errors reported fail the export, even with exit code 0 (the first one is the message)
            if (result.Count("error") > 0) {
                var errors = result.Count("error");
                result.success = false;
                result.error = result.items.Find(i => i["level"]?.Value<string>() == "error")["message"]?.Value<string>()
                    + (errors > 1 ? " (and " + (errors - 1) + " more errors)" : "");
            }
            return result;
        } finally {
            if (File.Exists(jsonFile)) File.Delete(jsonFile);
            if (File.Exists(reportFile)) File.Delete(reportFile);
            progressBar.SetActive(false);
        }
    }

    static JObject Item(string level, string message) {
        return new JObject { ["level"] = level, ["message"] = message };
    }

    //one JSON object per line, the invalid ones as they are (as errors)
    static List<JObject> ReadReport(string file) {
        var res = new List<JObject>();
        foreach (var line in File.ReadAllLines(file)) {
            if (line.Trim().Length == 0) continue;
            try {
                if (JToken.Parse(line) is JObject o && o["level"] != null && o["message"] != null) {
                    res.Add(o);
                    continue;
                }
            } catch (Newtonsoft.Json.JsonReaderException) {
            }
            res.Add(Item("error", "invalid line in the report of the exporter: " + line));
        }
        return res;
    }

    static void WriteCity(CityEncoder.CityEncoder encoder, ElementManager manager, string filename, System.Action post = null) {
        encoder.EncodeCity(manager, filename, post);
    }
}

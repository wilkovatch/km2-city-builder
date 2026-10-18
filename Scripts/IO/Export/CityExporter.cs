using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

public static class CityExporter {
    public class ExportResult {
        public bool success;
        public string error; //null on success
        public int exitCode;
        public string output;
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

    //the exporter of the core for the file extension, null if none
    public static FileBrowserHelper.CustomExporter FindExporter(string format) {
        var expList = FileBrowserHelper.GetCustomExporters();
        if (expList == null) return null;
        foreach (var elem in expList) {
            if (elem.settings.format == format) return elem;
        }
        return null;
    }

    //the flags of the exporter with their default values, changed by the given ones (by flag id)
    public static List<bool> GetExporterFlags(FileBrowserHelper.CustomExporter exporter, Dictionary<string, bool> values) {
        var flags = exporter.settings.flags ?? new FileBrowserHelper.CustomExporterFlags[0];
        var res = new List<bool>();
        foreach (var flag in flags) {
            res.Add(values != null && flag.id != null && values.TryGetValue(flag.id, out var v) ? v : flag.defaultValue);
        }
        return res;
    }

    //the ids of the flags that can't be changed with the given values of the others (like the checkboxes of the
    //export dialog: a flag enables or disables others)
    public static HashSet<string> GetLockedFlags(FileBrowserHelper.CustomExporter exporter, List<bool> values) {
        var flags = exporter.settings.flags ?? new FileBrowserHelper.CustomExporterFlags[0];
        var res = new HashSet<string>();
        for (int i = 0; i < flags.Length && i < values.Count; i++) {
            if (!values[i] && flags[i].enables != null) res.UnionWith(flags[i].enables);
            if (values[i] && flags[i].disables != null) res.UnionWith(flags[i].disables);
        }
        return res;
    }

    public static void SaveCityWithExporter(FileBrowserHelper.CustomExporter exporter, List<bool> flags, ElementManager manager, string name) {
        _ = ExportWithExporterAsync(exporter, flags, manager, name, true);
    }

    //writes the export JSON next to the output file and runs the exporter on it (in its own console if showConsole)
    public static async Task<ExportResult> ExportWithExporterAsync(FileBrowserHelper.CustomExporter exporter, List<bool> flags, ElementManager manager, string name,
        bool showConsole, System.IProgress<(float, string)> progress = null) {

        var enc = new CityEncoder.JSON_Export();
        var jsonFile = name.Replace("." + exporter.settings.format, ".json");
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
            return new ExportResult { success = res.code == 0, error = res.code == 0 ? null : (res.data ?? "exit code " + res.code), exitCode = res.code, output = res.data };
        } finally {
            if (File.Exists(jsonFile)) File.Delete(jsonFile);
            progressBar.SetActive(false);
        }
    }

    static void WriteCity(CityEncoder.CityEncoder encoder, ElementManager manager, string filename, System.Action post = null) {
        encoder.EncodeCity(manager, filename, post);
    }
}

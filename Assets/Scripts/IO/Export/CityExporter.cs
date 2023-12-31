using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

public static class CityExporter {
    public static void SaveCity(ElementManager manager, string name, bool gzipped = false) {
        var ext = PathHelper.GetExtension(name);
        if (ext.ToLower() == name.ToLower()) ext = "";
        var expList = FileBrowserHelper.GetCustomExporters();
        if (expList != null) {
            foreach (var elem in expList) {
                if (elem.settings.format == ext) {
                    manager.builder.ShowCustomExportDialog(elem, name);
                    return;
                }
            }
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

    public static void SaveCityWithExporter(FileBrowserHelper.CustomExporter exporter, List<bool> flags, ElementManager manager, string name) {
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
        WriteCity(enc, manager, jsonFile, delegate { RunCommand(manager, script, args, jsonFile); });
    }

    static void RunCommand(ElementManager manager, string script, List<string> args, string jsonFile) {
        var progressBar = manager.builder.helper.curProgressBar;
        progressBar.SetActive(true);
        progressBar.SetProgress(0);
        progressBar.SetText(StringManager.Get("EXPORTING_CITY"));
        PythonManager.RunScriptAsync(script, true, args, (exitCode, data) => {
            File.Delete(jsonFile);
            progressBar.SetActive(false);
        }, manager, true);
    }

    static void WriteCity(CityEncoder.CityEncoder encoder, ElementManager manager, string filename, System.Action post = null) {
        encoder.EncodeCity(manager, filename, post);
    }
}

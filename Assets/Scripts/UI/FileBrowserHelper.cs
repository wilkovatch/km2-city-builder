using SimpleFileBrowser;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using SM = StringManager;

public class FileBrowserHelper : MonoBehaviour {
    Action<string> post;
    public Action<bool> enableMenuUI;
    EventSystem eventSystem = null;

    public void LoadImageFile(Action<string> post) {
        LoadFile(SM.Get("IMAGES"), new string[] { ".jpg", ".png", ".tga", ".bmp", ".dds" }, ".png", post);
    }

    public void LoadFile(string filterName, string[] extensions, string defaultFilter, Action<string> post) {
        this.post = post;
        FileBrowser.AllFilesFilterText = SM.Get("ALL_FILES") + " (.*)";
        FileBrowser.SetFilters(true, new FileBrowser.Filter(filterName, extensions));
        FileBrowser.SetDefaultFilter(defaultFilter);
        StartCoroutine(ShowLoadFileDialogCoroutine());
    }

    public void LoadFolder(Action<string> post) {
        this.post = post;
        StartCoroutine(ShowLoadFolderDialogCoroutine());
    }

    void EnableOtherUI(bool enabled) {
        if (eventSystem == null) eventSystem = EventSystem.current;
        eventSystem.enabled = enabled;
        if (enableMenuUI != null) enableMenuUI(enabled);
    }

    IEnumerator ShowLoadFileDialogCoroutine() {
        var path = PreferencesManager.workingDirectory != "" ? PreferencesManager.workingDirectory : null;
        EnableOtherUI(false);
        yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Files, true, path, null, SM.Get("SELECT_FILE"));
        EnableOtherUI(true);
        if (FileBrowser.Success) {
            post.Invoke(FileBrowser.Result[0]);
        }
    }

    IEnumerator ShowLoadFolderDialogCoroutine() {
        EnableOtherUI(false);
        yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Folders, true, null, null, SM.Get("SELECT_FOLDER"));
        EnableOtherUI(true);
        if (FileBrowser.Success) {
            post.Invoke(FileBrowser.Result[0]);
        }
    }

    public void SaveFile(Action<string> post) {
        this.post = post;
        StartCoroutine(ShowSaveFileDialogCoroutine());
    }

    [Serializable]
    public struct CustomExporterFlags {
        public string id;
        public string label;
        public string tooltip;
        public string[] disables;
        public bool defaultValue;
    }

    [Serializable]
    public struct CustomExporterSettings {
        public string label;
        public string format;
        public CustomExporterFlags[] flags;
    }

    public class CustomExporter {
        public string folder;
        public CustomExporterSettings settings;
    }

    public static List<CustomExporter> GetCustomExporters() {
        var res = new List<CustomExporter>();
        var parentFolder = PathHelper.FilesPath() + "exporters/";
        var folders = Directory.GetDirectories(parentFolder);
        foreach (var folder in folders) {
            var exp = new CustomExporter();
            exp.folder = folder;
            var settingsFile = folder + "/settings.json";
            var settingsContent = File.ReadAllText(settingsFile);
            var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomExporterSettings>(settingsContent);
            exp.settings = settings;
            res.Add(exp);
        }
        return res;
    }

    IEnumerator ShowSaveFileDialogCoroutine() {
        var filters = new List<FileBrowser.Filter>();
        filters.Add(new FileBrowser.Filter("JSON", ".json"));
        var list = GetCustomExporters();
        if (list != null) {
            foreach (var elem in list) {
                filters.Add(new FileBrowser.Filter(SM.Get(elem.settings.label), "." + elem.settings.format));
            }
        }
        FileBrowser.SetFilters(false, filters);
        EnableOtherUI(false);
        yield return FileBrowser.WaitForSaveDialog(FileBrowser.PickMode.Files, false, null, "city", SM.Get("SELECT_FILE"));
        EnableOtherUI(true);
        if (FileBrowser.Success) {
            post.Invoke(FileBrowser.Result[0]);
        }
    }
}

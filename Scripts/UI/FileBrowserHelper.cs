//using SimpleFileBrowser;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using SM = StringManager;

public partial class FileBrowserHelper : Control {
    public Action<bool> enableMenuUI;
    public CityBuilderMenuBar builder = null;
    Action<string> post;
    FileDialog dialog;
    //last folder browsed by any dialog it must not touch the city working directory
    static string lastDirectory = null;
    //EventSystem eventSystem = null;

    public void LoadImageFile(Action<string> post) {
        LoadFile(SM.Get("IMAGES"), new string[] { ".jpg", ".png", ".tga", ".bmp", ".dds" }, ".png", post);
    }

    public void LoadFile(string filterName, string[] extensions, string defaultFilter, Action<string> post) {
        this.post = post;
        var patterns = new List<string>();
        foreach (var extension in extensions) patterns.Add("*" + extension);
        var filters = new List<string> { string.Join(";", patterns) };
        var startDir = !string.IsNullOrEmpty(PreferencesManager.workingDirectory) ? PreferencesManager.workingDirectory : null;
        ShowDialog(FileDialog.FileModeEnum.OpenFile, SM.Get("SELECT_FILE"), filters, startDir);
    }

    public void LoadFolder(Action<string> post) {
        this.post = post;
        ShowDialog(FileDialog.FileModeEnum.OpenDir, SM.Get("SELECT_FOLDER"), null);
    }

    void EnableOtherUI(bool enabled) {
        if (enableMenuUI != null) enableMenuUI(enabled);
    }

    static string GetStartDirectory(string preferred = null) {
        return preferred ?? lastDirectory ?? PreferencesManager.workingDirectory ?? "";
    }

    void ShowDialog(FileDialog.FileModeEnum mode, string title, List<string> filters, string startDir = null) {
        if (dialog != null && GodotObject.IsInstanceValid(dialog)) dialog.QueueFree();
        dialog = new FileDialog { FileMode = mode, Access = FileDialog.AccessEnum.Filesystem, Title = title,
            UseNativeDialog = true, CurrentDir = GetStartDirectory(startDir) };
        if (filters != null) foreach (var filter in filters) dialog.AddFilter(filter);
        AddChild(dialog);
        EnableOtherUI(false);
        dialog.FileSelected += path => Finish(path);
        dialog.DirSelected += path => Finish(path);
        dialog.Canceled += () => Finish(null);
        dialog.PopupCentered(new Vector2I(800, 600));
    }

    void Finish(string path) {
        EnableOtherUI(true);
        if (!string.IsNullOrEmpty(path)) {
            lastDirectory = Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? path);
            post?.Invoke(path);
        }
        if (dialog != null && GodotObject.IsInstanceValid(dialog)) dialog.QueueFree();
    }

    async void ShowLoadFileDialogCoroutine() {
        /*var path = PreferencesManager.workingDirectory != "" ? PreferencesManager.workingDirectory : null;
        EnableOtherUI(false);
        yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Files, true, path, null, SM.Get("SELECT_FILE"));
        EnableOtherUI(true);
        if (FileBrowser.Success) {
            post.Invoke(FileBrowser.Result[0]);
        }*/
    }

    async void ShowLoadFolderDialogCoroutine() {
        /*EnableOtherUI(false);
        yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Folders, true, null, null, SM.Get("SELECT_FOLDER"));
        EnableOtherUI(true);
        if (FileBrowser.Success) {
            post.Invoke(FileBrowser.Result[0]);
        }*/
    }

    public void SaveFile(Action<string> post) {
        this.post = post;
        var filters = new List<string>();
        var extensions = new List<string>();
        try {
            foreach (var elem in GetCustomExporters()) {
                if (!string.IsNullOrEmpty(elem.settings.format)) {
                    filters.Add("*." + elem.settings.format + ";" + SM.Get(elem.settings.label));
                    extensions.Add("." + elem.settings.format);
                }
            }
        } catch (Exception e) { GameEngine.LogWarning("Could not read custom exporters: " + e.Message); }
        filters.Add("*.json;JSON");
        extensions.Add(".json");
        var defaultFile = GetDefaultFilename() + extensions[0];
        if (DisplayServer.HasFeature(DisplayServer.Feature.NativeDialogFile)) {
            // Godot's FileDialog always prepends an "All Recognized" filter when there is more than one,
            // which makes no sense when saving: call the native dialog directly so the first (core) format is the default
            EnableOtherUI(false);
            DisplayServer.FileDialogShow(SM.Get("SELECT_FILE"), GetStartDirectory(), defaultFile, false,
                DisplayServer.FileDialogMode.SaveFile, filters.ToArray(),
                Callable.From<bool, string[], int>((ok, paths, filterIndex) => {
                    string path = null;
                    if (ok && paths != null && paths.Length > 0) {
                        path = paths[0];
                        if (filterIndex >= 0 && filterIndex < extensions.Count
                            && !path.EndsWith(extensions[filterIndex], StringComparison.OrdinalIgnoreCase)) {
                            path += extensions[filterIndex];
                        }
                    }
                    Callable.From(() => Finish(path)).CallDeferred();
                }));
            return;
        }
        ShowDialog(FileDialog.FileModeEnum.SaveFile, SM.Get("SELECT_FILE"), filters);
        dialog.CurrentFile = defaultFile;
    }

    [Serializable]
    public struct CustomExporterFlags {
        public string id;
        public string label;
        public string tooltip;
        public string[] disables;
        public string[] enables;
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
        var parentFolder = Path.Combine(PathHelper.FilesPath(), "exporters");
        if (!Directory.Exists(parentFolder)) return res;
        var folders = Directory.GetDirectories(parentFolder);
        foreach (var folder in folders) {
            var exp = new CustomExporter();
            exp.folder = folder;
            var settingsFile = folder + "/settings.json";
            if (!File.Exists(settingsFile)) continue;
            var settingsContent = File.ReadAllText(settingsFile);
            var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomExporterSettings>(settingsContent);
            if (settings.format == null) continue;
            exp.settings = settings;
            res.Add(exp);
        }
        return res;
    }

    // characters not allowed in Windows file names (elsewhere only '/' and '\0' are invalid),
    // checked everywhere so that the exported files can be used on Windows too
    static readonly char[] windowsInvalidFileNameChars = "<>:\"/\\|?*".ToCharArray();

    string GetDefaultFilename() {
        var filename = "city";
        if (builder != null) {
            var cityProperties = builder.helper.elementManager.GetDummy<CityProperties>();
            if (cityProperties != null) {
                var newName = cityProperties.GetState().Str("cityFileName");
                if (!string.IsNullOrEmpty(newName)) {
                    if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && newName.IndexOfAny(windowsInvalidFileNameChars) < 0
                        && !newName.Any(char.IsControl)) {
                        filename = newName;
                    } else {
                        GameEngine.LogWarning("Invalid city file name provided in city properties: " + newName);
                    }
                }
            }
        }
        return filename;
    }

    async void ShowSaveFileDialogCoroutine() {
        /*var filters = new List<FileBrowser.Filter>();
        var list = GetCustomExporters();
        if (list != null) {
            foreach (var elem in list) {
                filters.Add(new FileBrowser.Filter(SM.Get(elem.settings.label), "." + elem.settings.format));
            }
        }
        filters.Add(new FileBrowser.Filter("JSON", ".json"));
        FileBrowser.SetFilters(false, filters);
        EnableOtherUI(false);
        var ext = filters[0].defaultExtension;
        yield return FileBrowser.WaitForSaveDialog(FileBrowser.PickMode.Files, false, null, GetDefaultFilename() + ext, SM.Get("SELECT_FILE"));
        EnableOtherUI(true);
        if (FileBrowser.Success) {
            post.Invoke(FileBrowser.Result[0]);
        }*/
    }
}

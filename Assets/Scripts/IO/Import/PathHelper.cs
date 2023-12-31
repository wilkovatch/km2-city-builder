using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

public static class PathHelper {
    public static bool CoreAvailable() {
        return PreferencesManager.Get("core", "") != "";
    }

    public static string FilesPath(bool basePath = false) {
        var core = PreferencesManager.Get("core", "");
        var path = new DirectoryInfo(Application.dataPath);
        if (!basePath && core != "") return Path.Combine(path.Parent.ToString(), "Files", "cores", core) + "/"; //TODO: path.combine if possible
        else return Path.Combine(path.Parent.ToString(), "Files") + "/";
    }

    public static string LocalFilesPath(bool basePath = false) {
        var path = new DirectoryInfo(Application.dataPath);
        if (!basePath && PreferencesManager.workingDirectory != "") return PreferencesManager.workingDirectory + "/";
        else return Path.Combine(path.Parent.ToString(), "Files") + "/";
    }

    static List<string> GetExtraFolders() {
        var extraFolders = SettingsManager.GetList<string>("extraFolders", null);
        var localExtraFolders = PreferencesManager.GetList<string>("extraFolders", null);
        if (extraFolders == null && localExtraFolders != null) {
            extraFolders = localExtraFolders;
        } else if (extraFolders != null && localExtraFolders != null) {
            extraFolders.AddRange(localExtraFolders);
        }
        return extraFolders;
    }

    static string GetTrueFolder(string folder) {
        var trueFolder = folder;
        var lastChar = trueFolder[trueFolder.Length - 1];
        if (lastChar != '\\' && lastChar != '/') trueFolder = trueFolder + "/";
        if (trueFolder[0] == '.') {
            trueFolder = trueFolder.Remove(0, 1);
            trueFolder = FilesPath(true) + "common" + trueFolder;
        }
        return trueFolder;
    }

    public static void CreateCommonFolder() {
        if (!CoreAvailable()) return;
        var core = PreferencesManager.Get("core", "");
        var trueFolder = GetTrueFolder("./" + core);
        if (!Directory.Exists(trueFolder)) DirectoryCopy(Path.Combine(FilesPath(false), "baseCommon"), trueFolder);
    }

    public static string FindInFolders(string name) {
        if (File.Exists(LocalFilesPath() + name)) {
            return LocalFilesPath() + name;
        } else {
            var extraFolders = GetExtraFolders();
            if (extraFolders != null) {
                foreach (var folder in extraFolders) {
                    var trueFolder = GetTrueFolder(folder);
                    if (File.Exists(trueFolder + name)) {
                        return trueFolder + name;
                    }
                }
            }
        }
        return LocalFilesPath() + name;
    }

    public static string BasePath() {
        return new DirectoryInfo(Application.dataPath).Parent.ToString();
    }

    public static string GetExtension(string name) {
        var parts = name.Split('.');
        return parts[parts.Length - 1].ToLower();
    }

    public static void DirectoryCopy(string src, string dst) {
        var dir = new DirectoryInfo(src);
        foreach (var file in dir.GetFiles()) {
            file.CopyTo(Path.Combine(dst, file.Name), false);
        }
        foreach (var subdir in dir.GetDirectories()) {
            var dstSubdir = Path.Combine(dst, subdir.Name);
            Directory.CreateDirectory(dstSubdir);
            DirectoryCopy(subdir.FullName, dstSubdir);
        }
    }

    static (string, string)[] GetFileList(string parentFolder) {
        if (!Directory.Exists(parentFolder)) return new (string, string)[0];
        var normalizedPath = parentFolder.Replace("\\", "/");
        var res = Directory.GetFiles(parentFolder, "*.*", SearchOption.AllDirectories);
        var absRes = new (string, string)[res.Length];
        for (int i = 0; i < res.Length; i++) {
            absRes[i] = (res[i].Replace("\\", "/").Replace(normalizedPath, ""), res[i]);
        }
        return absRes;
    }

    public static void GetFileList(bool rescan, string query, List<string> formats, ref List<string> paths, ref List<string> absPaths, ref Dictionary<string, int> indices, ref (string, string)[] fileEntries) {
        paths.Clear();
        indices.Clear();
        absPaths.Clear();
        if (rescan || fileEntries == null) {
            var filesList = new SortedList<string, (string path, string absPath)>();
            var doneList = new HashSet<string>();
            foreach (var file in GetFileList(LocalFilesPath())) {
                filesList.Add(file.Item1, file);
                doneList.Add(file.Item1);
            }
            var extraFolders = GetExtraFolders();
            if (extraFolders != null) {
                foreach (var folder in extraFolders) {
                    var trueFolder = GetTrueFolder(folder);
                    foreach (var file in GetFileList(trueFolder)) {
                        if (!doneList.Contains(file.Item1)) {
                            filesList.Add(file.Item1, file);
                            doneList.Add(file.Item1);
                        }
                    }
                }
            }
            fileEntries = new (string, string)[filesList.Values.Count];
            var i = 0;
            foreach (var val in filesList.Values) {
                fileEntries[i] = val;
                i++;
            }
        }
        foreach ((string name, string absName) in fileEntries) {
            var part = name.Split('.');
            var extension = part.Length > 1 ? part[part.Length - 1] : "";
            if (formats.Contains(extension)) {
                if (query == null || query == "" || Regex.IsMatch(name, query, RegexOptions.IgnoreCase)) {
                    indices.Add(name, paths.Count);
                    paths.Add(name);
                    absPaths.Add(absName);
                }
            }
        }
    }
}

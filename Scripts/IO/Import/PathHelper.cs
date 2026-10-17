using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

public static class PathHelper {
    public static bool CoreAvailable() {
        return PreferencesManager.Get("core", "") != "";
    }

    public static string FilesPath(bool basePath = false) {
        var core = PreferencesManager.Get("core", "");
        if (!basePath && core != "") return Path.Combine(BasePath(), "Files", "cores", core) + Path.DirectorySeparatorChar;
        return Path.Combine(BasePath(), "Files") + Path.DirectorySeparatorChar;
    }

    public static string LocalFilesPath(bool basePath = false) {
        if (!basePath && PreferencesManager.workingDirectory != "") return PreferencesManager.workingDirectory + "/";
        return Path.Combine(BasePath(), "Files") + Path.DirectorySeparatorChar;
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
        if (string.IsNullOrWhiteSpace(trueFolder)) return FilesPath(true);
        //folders may be stored with backslashes (e.g. ".\MidtownMadness2\"), which are not separators outside Windows
        if (!System.OperatingSystem.IsWindows()) trueFolder = trueFolder.Replace('\\', '/');
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
        if (!Directory.Exists(trueFolder)) {
            Directory.CreateDirectory(trueFolder);
            DirectoryCopy(Path.Combine(FilesPath(false), "baseCommon"), trueFolder);
        }
    }

    public static string FindInFolders(string name) {
        var localName = ResolveFile(LocalFilesPath() + name);
        if (File.Exists(localName)) {
            return localName;
        } else {
            var extraFolders = GetExtraFolders();
            if (extraFolders != null) {
                foreach (var folder in extraFolders) {
                    var trueName = ResolveFile(GetTrueFolder(folder) + name);
                    if (File.Exists(trueName)) {
                        return trueName;
                    }
                }
            }
        }
        return LocalFilesPath() + name;
    }

    //directory -> (last write time, lower case entry name -> actual entry name)
    static readonly Dictionary<string, (System.DateTime time, Dictionary<string, string> entries)> caseCache = new();

    public static void ClearCaseCache() {
        lock (caseCache) caseCache.Clear();
    }

    //Returns the path of an existing file whose name differs only in case from the given one, or the given path if there is none.
    //Asset names do not always match the case of the files (the games read them on case-insensitive file systems),
    //so this is needed where the file system is case sensitive. Nothing to do on Windows.
    public static string ResolveFile(string path) {
        if (string.IsNullOrEmpty(path) || System.OperatingSystem.IsWindows() || File.Exists(path)) return path;
        try {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath)) return fullPath;
            var root = Path.GetPathRoot(fullPath);
            var parts = fullPath.Substring(root.Length).Split(Path.DirectorySeparatorChar, System.StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            for (int i = 0; i < parts.Length; i++) {
                var last = i == parts.Length - 1;
                var candidate = Path.Combine(current, parts[i]);
                if (last ? File.Exists(candidate) : Directory.Exists(candidate)) {
                    current = candidate;
                    continue;
                }
                var entry = FindEntryIgnoreCase(current, parts[i]);
                if (entry == null) return path;
                current = Path.Combine(current, entry);
            }
            return File.Exists(current) ? current : path;
        } catch (System.Exception) {
            return path;
        }
    }

    static string FindEntryIgnoreCase(string directory, string name) {
        var time = Directory.GetLastWriteTimeUtc(directory);
        Dictionary<string, string> entries;
        lock (caseCache) {
            if (!caseCache.TryGetValue(directory, out var cached) || cached.time != time) {
                entries = new Dictionary<string, string>();
                foreach (var entryPath in Directory.EnumerateFileSystemEntries(directory)) {
                    var entryName = Path.GetFileName(entryPath);
                    entries.TryAdd(entryName.ToLowerInvariant(), entryName);
                }
                caseCache[directory] = (time, entries);
            } else {
                entries = cached.entries;
            }
        }
        return entries.TryGetValue(name.ToLowerInvariant(), out var res) ? res : null;
    }

    static string basePath = null;

    public static string BasePath() {
        // The project folder when running from the editor, the executable's folder in an exported build
        if (basePath == null) {
            var path = Godot.OS.HasFeature("editor") ? Godot.ProjectSettings.GlobalizePath("res://") : Path.GetDirectoryName(Godot.OS.GetExecutablePath());
            basePath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }
        return basePath;
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
            ClearCaseCache();
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

#if TOOLS
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Godot;

// Copies the runtime data next to the exported executable, registered by the km2_postprocess_build editor plugin
// For a .zip export the archive is already written when _ExportEnd runs,
// so the data is prepared in a temporary folder and then added to it.
[Tool]
public partial class PostprocessBuild : EditorExportPlugin {
    string outputPath = null;
    bool windowsExport = true;

    public override string _GetName() {
        return "km2PostprocessBuild";
    }

    public override void _ExportBegin(string[] features, bool isDebug, string path, uint flags) {
        outputPath = path;
        windowsExport = System.Array.IndexOf(features, "windows") >= 0;
    }

    public override void _ExportEnd() {
        if (string.IsNullOrEmpty(outputPath)) return;
        var path = ProjectSettings.GlobalizePath(outputPath);
        outputPath = null;
        try {
            if (path.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase)) {
                var stagingPath = Path.Combine(Path.GetTempPath(), "km2PostprocessBuild");
                DeleteFolder(stagingPath);
                Directory.CreateDirectory(stagingPath);
                OnPostprocessBuild(stagingPath);
                AddToZip(path, stagingPath);
                DeleteFolder(stagingPath);
            } else {
                OnPostprocessBuild(Path.GetDirectoryName(path));
            }
        } catch (System.Exception e) {
            GD.PushError("Postprocess build failed: " + e.Message);
        }
    }

    static void DeleteFolder(string path) {
        if (!Directory.Exists(path)) return;
        ClearReadOnly(path);
        Directory.Delete(path, true);
    }

    static void AddToZip(string zipPath, string folder) {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) {
            var entryName = Path.GetRelativePath(folder, file).Replace('\\', '/');
            zip.GetEntry(entryName)?.Delete();
            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
        foreach (var dir in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories)) {
            if (Directory.EnumerateFileSystemEntries(dir).GetEnumerator().MoveNext()) continue;
            var entryName = Path.GetRelativePath(folder, dir).Replace('\\', '/') + "/";
            if (zip.GetEntry(entryName) == null) zip.CreateEntry(entryName);
        }
    }

    void OnPostprocessBuild(string dstPath) {
        var none = new List<string>() { };
        // the installer scripts only run on Windows, elsewhere Python is downloaded by the program itself
        var installerScripts = windowsExport ? none : new List<string>() { "install_packages.bat", "run_installer.bat", "installer.ps1" };
        var files = new List<(string origin, string destination)>() {
            ("coreRepos.json", "coreRepos.json"),
            ("defaultSettings.json", "settings.json")
        };
        var folders = new List<(string origin, string destination, List<string> exceptFiles, List<string> exceptFolders)>() {
            ("pythonInstaller", "pythonInstaller", installerScripts, none),
            ("Files", "Files", none, new List<string> { "common", "cores", "custom", "temp" })
        };
        var srcPath = ProjectSettings.GlobalizePath("res://");
        File.WriteAllText(Path.Combine(dstPath, "License.txt"), ProcessLicense(srcPath));
        BuildQuickstartGuide(srcPath, Path.Combine(dstPath, "Quickstart guide.pdf"));
        foreach (var file in files) {
            var srcFile = Path.Combine(srcPath, file.origin);
            var dstFile = Path.Combine(dstPath, file.destination);
            if (File.Exists(dstFile)) File.Delete(dstFile);
            if (File.Exists(srcFile)) {
                File.Copy(srcFile, dstFile);
            } else {
                GD.PushWarning("Warning: the file " + srcFile + " has not been found, the build will not contain it.");
            }
        }
        foreach (var folder in folders) {
            var srcFolder = Path.Combine(srcPath, folder.origin);
            var dstFolder = Path.Combine(dstPath, folder.destination);
            // the excluded folders hold user data (installed cores, common
            // files, ...) when exporting over a build that has already been used, so they are kept
            if (Directory.Exists(dstFolder)) {
                ClearReadOnly(dstFolder);
                foreach (var subFolder in Directory.GetDirectories(dstFolder)) {
                    if (!folder.exceptFolders.Contains(Path.GetFileName(subFolder))) Directory.Delete(subFolder, true);
                }
                foreach (var file in Directory.GetFiles(dstFolder)) {
                    File.Delete(file);
                }
            }
            CopyFolder(
                srcFolder,
                dstFolder,
                folder.exceptFiles,
                folder.exceptFolders,
                new List<string>() { ".gitignore", ".gdignore" },
                new List<string>() { "__pycache__" }
            );
            foreach (var subFolder in folder.exceptFolders) {
                Directory.CreateDirectory(Path.Combine(dstFolder, subFolder));
            }
        }
    }

    // Converts the quickstart guide wiki page to PDF, requires pandoc and typst in the PATH.
    // The page title is replaced by the full guide title and the date of the last commit that changed the page,
    // the layout and fonts are defined in the template next to the editor plugin, the size of the screenshots in the filter.
    static void BuildQuickstartGuide(string srcPath, string dstFile) {
        var srcFile = Path.Combine(srcPath, "wiki", "Quickstart-Guide.md");
        var templateFile = Path.Combine(srcPath, "addons", "km2_postprocess_build", "quickstart-guide.typst");
        var filterFile = Path.Combine(srcPath, "addons", "km2_postprocess_build", "quickstart-guide.lua");
        if (File.Exists(dstFile)) File.Delete(dstFile);
        var (gitExitCode, date) = RunProcess("git", srcPath, "log", "-1", "--format=%cs", "--", srcFile);
        date = date.Trim();
        if (gitExitCode != 0 || date == "") date = System.DateTime.Now.ToString("yyyy-MM-dd");
        var (exitCode, output) = RunProcess("pandoc", srcPath,
            srcFile, "-o", dstFile,
            "--pdf-engine=typst",
            "--template=" + templateFile,
            "--lua-filter=" + filterFile,
            "--resource-path=" + Path.Combine(srcPath, "wiki"),
            "--shift-heading-level-by=-1",
            "-V", "title=km2 City Builder quickstart guide",
            "-M", "date=Last updated on " + date);
        if (exitCode != 0 || !File.Exists(dstFile)) {
            GD.PushWarning("Warning: the quickstart guide could not be generated (pandoc and typst are required), the build will not contain it. " + output);
        }
    }

    // Returns the exit code (-1 if the program could not be started) and the combined output
    static (int exitCode, string output) RunProcess(string fileName, string workingDirectory, params string[] args) {
        var info = new ProcessStartInfo(fileName) {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        try {
            using var process = Process.Start(info);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, stdout.Result + stderr.Result);
        } catch (System.ComponentModel.Win32Exception e) {
            return (-1, fileName + ": " + e.Message);
        }
    }

    string ProcessLicense(string srcPath) {
        var mainLicFile = Path.Combine(srcPath, "License.txt");
        var licText = File.ReadAllText(mainLicFile);
        licText += "\n\n\n\n\n\nThis program includes software licensed under terms that require to display the following notices:\n\n\n";

        var notices = new SortedDictionary<string, string>();
        foreach (var licFile in Directory.GetFiles(Path.Combine(srcPath, "Licenses"))) {
            notices[Path.GetFileNameWithoutExtension(licFile)] = File.ReadAllText(licFile);
        }
        notices["Godot Engine"] = GetEngineLicense();
        foreach (var (name, subLicText) in notices) {
            var notice = " The below notice is for \"" + name + "\" ";
            var separatorLen = Mathf.Max(2, (90 - notice.Length) / 2);
            for (int i = 0; i < separatorLen; i++) {
                notice = "=" + notice + "=";
            }
            licText += notice + "\n\n" + subLicText + "\n\n";
        }
        return licText;
    }

    // Godot's license plus the notices of the third-party components built into the engine (as in its COPYRIGHT.txt)
    static string GetEngineLicense() {
        var text = Engine.GetLicenseText() + "\n\n\nThe Godot Engine includes the following third-party components:\n";
        foreach (var component in Engine.GetCopyrightInfo()) {
            text += "\n" + component["name"].AsString() + "\n";
            foreach (var part in component["parts"].AsGodotArray<Godot.Collections.Dictionary>()) {
                foreach (var copyright in part["copyright"].AsStringArray()) {
                    text += "  Copyright (c) " + copyright + "\n";
                }
                text += "  License: " + part["license"].AsString() + "\n";
            }
        }
        var licenses = Engine.GetLicenseInfo();
        foreach (var license in licenses.Keys) {
            text += "\n\n----- " + license.AsString() + " -----\n\n" + licenses[license].AsString() + "\n";
        }
        return text;
    }

    static void ClearReadOnly(string dir) {
        var dirInfo = new DirectoryInfo(dir);
        if (dirInfo != null) {
            dirInfo.Attributes = FileAttributes.Normal;
            foreach (FileInfo f in dirInfo.GetFiles()) {
                f.Attributes = FileAttributes.Normal;
            }
            foreach (DirectoryInfo d in dirInfo.GetDirectories()) {
                ClearReadOnly(d.FullName);
            }
        }
    }

    void CopyFolder(string src, string dst, List<string> exceptFiles, List<string> exceptFolders, List<string> alwaysExceptFiles, List<string> alwaysExceptFolders) {
        var dir = new DirectoryInfo(src);
        if (!dir.Exists) throw new System.Exception("folder not found: " + src);
        DirectoryInfo[] dirs = dir.GetDirectories();
        Directory.CreateDirectory(dst);
        foreach (FileInfo file in dir.GetFiles()) {
            if (!exceptFiles.Contains(file.Name) && !alwaysExceptFiles.Contains(file.Name)) {
                file.CopyTo(Path.Combine(dst, file.Name));
            }
        }
        foreach (DirectoryInfo subDir in dirs) {
            string newDestinationDir = Path.Combine(dst, subDir.Name);
            if (!exceptFolders.Contains(subDir.Name) && !alwaysExceptFolders.Contains(subDir.Name)) {
                CopyFolder(subDir.FullName, newDestinationDir, new List<string>(), new List<string>(), alwaysExceptFiles, alwaysExceptFolders);
            }
        }
    }
}
#endif

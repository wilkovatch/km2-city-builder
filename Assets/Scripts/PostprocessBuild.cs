#if (UNITY_EDITOR)
using System.IO;
using UnityEditor.Build;
using UnityEngine;
using UnityEditor.Build.Reporting;
using System.Collections.Generic;

class PostprocessBuild : IPostprocessBuildWithReport {
    public int callbackOrder { get { return 0; } }

    public void OnPostprocessBuild(BuildReport report) {
        var none = new List<string>() { };
        var files = new List<(string origin, string destination)>() {
            ("coreRepos.json", "coreRepos.json"),
            ("defaultSettings.json", "settings.json"),
            ("LicenseFull.txt", "License.txt"),
            ("Quickstart guide.pdf", "Quickstart guide.pdf")
        };
        var folders = new List<(string origin, string destination, List<string> exceptFiles, List<string> exceptFolders)>() {
            ("pythonInstaller", "pythonInstaller", none, none),
            ("Files", "Files", none, new List<string> { "common", "cores", "custom", "temp" })
        };
        var srcPath = Directory.GetParent(Application.dataPath).FullName;
        var dstPath = Path.GetDirectoryName(report.summary.outputPath);
        ProcessLicense(srcPath);
        foreach (var file in files) {
            var srcFile = Path.Combine(srcPath, file.origin);
            var dstFile = Path.Combine(dstPath, file.destination);
            if (File.Exists(dstFile)) File.Delete(dstFile);
            if (File.Exists(srcFile)) {
                File.Copy(srcFile, dstFile);
            } else {
                Debug.LogWarning("Warning: the file " + srcFile + " has not been found, the build will not contain it.");
            }
        }
        ClearTempFiles(srcPath);
        foreach (var folder in folders) {
            var srcFolder = Path.Combine(srcPath, folder.origin);
            var dstFolder = Path.Combine(dstPath, folder.destination);
            if (Directory.Exists(dstFolder)) {
                ClearReadOnly(dstFolder);
                Directory.Delete(dstFolder, true);
            }
            CopyFolder(
                srcFolder,
                dstFolder,
                folder.exceptFiles,
                folder.exceptFolders,
                new List<string>() { ".gitignore" },
                new List<string>() { "__pycache__" }
            );
            foreach (var subFolder in folder.exceptFolders) {
                Directory.CreateDirectory(Path.Combine(dstFolder, subFolder));
            }
        }
    }

    private void ProcessLicense(string srcPath) {
        var mainLicFile = Path.Combine(srcPath, "License.txt");
        var licText = File.ReadAllText(mainLicFile);
        licText += "\n\n\n\n\n\nThis program includes software licensed under terms that require to display the following notices:\n\n\n";

        var licFiles = Directory.GetFiles(Path.Combine(srcPath, "Licenses"));
        foreach (var licFile in licFiles) {
            var name = Path.GetFileNameWithoutExtension(licFile);
            var notice = " The below notice is for \"" + name + "\" ";
            var separatorLen = Mathf.Max(2, (90 - notice.Length) / 2);
            for(int i = 0; i < separatorLen; i++) {
                notice = "=" + notice + "=";
            }
            var subLicText = File.ReadAllText(licFile);
            licText += notice + "\n\n" + subLicText + "\n\n";
        }
        var fullLicFile = Path.Combine(srcPath, "LicenseFull.txt");
        File.WriteAllText(fullLicFile, licText);
    }

    private void ClearTempFiles(string srcPath) {
        var licFile = Path.Combine(srcPath, "LicenseFull.txt");
        if (File.Exists(licFile)) File.Delete(licFile);
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

    private void CopyFolder(string src, string dst, List<string> exceptFiles, List<string> exceptFolders, List<string> alwaysExceptFiles, List<string> alwaysExceptFolders) {
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using SM = StringManager;

class PythonManager { //TODO: make it work for Mac and Linux too
    static TcpClient client;
    static NetworkStream stream;
    static Process serverProcess;
    static bool halt = false;
    const int timeout = 1000;

    enum InstallationStatus {
        Valid,
        DefaultNotAvailable,
        DefaultNotValid,
        UserNotAvailable,
        UserNotValid
    }

    static bool GetPythonDebug() {
        return SettingsManager.Get("pythonDebug", false);
    }

    static string GetPythonPath(bool real = false) {
        var res = SettingsManager.Get<string>("pythonPath", null);
        if ((res == null || res == "") && !real) {
            return Path.Combine(PathHelper.BasePath(), "python");
        } else {
            return res;
        }
    }

    static bool CheckDependencies(List<string> dependencies) {
        try {
            string path = GetPythonPath();
            string pythonExe = Path.Combine(path, "python.exe");
            var startInfo = new ProcessStartInfo();
            startInfo.FileName = pythonExe;
            string imports = "";
            foreach (var d in dependencies) {
                if (d != null && d != "") imports += "import " + d + "; ";
            }
            startInfo.Arguments = "-c \"" + imports + "\"";
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            var process = Process.Start(startInfo);
            process.WaitForExit();
            return process.ExitCode == 0;
        } catch (Exception e) {
            MonoBehaviour.print(e.Message);
            return false;
        }
    }

    static InstallationStatus GetPythonStatus(string core = null) {
        string path = GetPythonPath();
        string realPath = GetPythonPath(true);
        bool specified = (realPath != null && realPath != "");
        bool available = false;
        bool valid = false;
        if (Directory.Exists(path)) {
            string pythonExe = Path.Combine(path, "python.exe");
            if (File.Exists(pythonExe)) {
                available = true;
                string dependenciesTxtFile;
                if (core != null) {
                    dependenciesTxtFile = Path.Combine(PathHelper.BasePath(), "Files", "cores", core, "pythonInfo", "imports.txt");
                } else {
                    dependenciesTxtFile = Path.Combine(PathHelper.BasePath(), "pythonInstaller", "imports.txt");
                }
                var dependenciesTxtContent = File.ReadAllLines(dependenciesTxtFile);
                var dependencies = new List<string>(dependenciesTxtContent);
                valid = CheckDependencies(dependencies);
            }
        }
        if (!specified) {
            if (!available) {
                //not specified, not available, prompt install
                return InstallationStatus.DefaultNotAvailable;
            } else if (!valid) {
                //not specified, but missing dependencies, prompt reinstall
                return InstallationStatus.DefaultNotValid;
            } else {
                //ok
                MonoBehaviour.print("Python checked and valid");
                return InstallationStatus.Valid;
            }
        } else {
            if (!available) {
                //specified, but unavailable, report and prompt install
                return InstallationStatus.UserNotAvailable;
            } else if (!valid) {
                //specified, but missing dependencies, report and prompt install
                return InstallationStatus.UserNotValid;
            } else {
                //ok
                MonoBehaviour.print("Python checked and valid");
                return InstallationStatus.Valid;
            }
        }
    }

    public static bool CheckPython(CityBuilderMenuBar menuBar, bool alreadyCalled = false) {
        var status = GetPythonStatus();
        Action install = delegate { InstallPython(menuBar); };
        if (alreadyCalled) {
            if (status != InstallationStatus.Valid) {
                menuBar.CreateAlert(SM.Get("ERROR"), SM.Get("PY_MANAGE_ERROR"), SM.Get("OK"), null, 200);
                return false;
            } else {
                menuBar.CreateAlert(SM.Get("PY_MANAGE_INSTALL_OK_TITLE"), SM.Get("PY_MANAGE_INSTALL_OK"), SM.Get("OK"));
                return false;
            }
        } else {
            switch (status) {
                case InstallationStatus.Valid:
                    //nothing to do
                    return true;
                case InstallationStatus.DefaultNotAvailable:
                    menuBar.CreateAlert(SM.Get("WARNING"), SM.Get("PY_MANAGE_DEFAULT_NOT_AVAILABLE"), SM.Get("YES"), SM.Get("NO"), install, null, 350);
                    return false;
                case InstallationStatus.DefaultNotValid:
                    menuBar.CreateAlert(SM.Get("WARNING"), SM.Get("PY_MANAGE_DEFAULT_NOT_VALID"), SM.Get("YES"), SM.Get("NO"), install, null, 350);
                    return false;
                case InstallationStatus.UserNotAvailable:
                    menuBar.CreateAlert(SM.Get("WARNING"), SM.Get("PY_MANAGE_USER_NOT_AVAILABLE"), SM.Get("YES"), SM.Get("NO"), install, null, 350);
                    return false;
                case InstallationStatus.UserNotValid:
                    menuBar.CreateAlert(SM.Get("WARNING"), SM.Get("PY_MANAGE_USER_NOT_VALID"), SM.Get("YES"), SM.Get("NO"), install, null, 350);
                    return false;
            }
        }
        return false;
    }

    public static bool CheckPythonForCore(CityBuilderMenuBar menuBar, string core, Action post, bool alreadyCalled = false) {
        var status = GetPythonStatus(core);
        Action install = delegate { InstallPythonPackages(core, menuBar, post); };
        if (alreadyCalled) {
            if (status != InstallationStatus.Valid) {
                menuBar.CreateAlert(SM.Get("ERROR"), SM.Get("PY_MANAGE_ERROR_CORE"), SM.Get("OK"), null, 200);
                return false;
            } else {
                menuBar.CreateAlert(SM.Get("PY_MANAGE_INSTALL_CORE_OK_TITLE"), SM.Get("PY_MANAGE_INSTALL_CORE_OK"), SM.Get("OK"));
                return false;
            }
        } else {
            switch (status) {
                case InstallationStatus.Valid:
                    //nothing to do
                    return true;
                case InstallationStatus.DefaultNotValid:
                    menuBar.CreateAlert(SM.Get("WARNING"), SM.Get("PY_MANAGE_DEFAULT_MODULES_NOT_VALID"), SM.Get("YES"), SM.Get("NO"), install, null, 300);
                    return false;
                case InstallationStatus.UserNotValid:
                    menuBar.CreateAlert(SM.Get("WARNING"), SM.Get("PY_MANAGE_USER_MODULES_NOT_VALID"), SM.Get("OK"), null, 300);
                    return false;
                case InstallationStatus.UserNotAvailable:
                case InstallationStatus.DefaultNotAvailable:
                    menuBar.CreateAlert(SM.Get("ERROR"), SM.Get("PY_MANAGE_USER_NOT_VALID"), SM.Get("OK"), null, 200);
                    return false;
            }
        }
        return false;
    }

    static void InstallPython(CityBuilderMenuBar menuBar) {
        var startInfo = new ProcessStartInfo();
        startInfo.WorkingDirectory = Path.Combine(PathHelper.BasePath(), "pythonInstaller");
        startInfo.FileName = "run_installer.bat";
        startInfo.WindowStyle = ProcessWindowStyle.Normal;
        startInfo.UseShellExecute = true;
        startInfo.CreateNoWindow = false;
        var process = Process.Start(startInfo);
        menuBar.StartCoroutine(WaitForInstallEnd(process, delegate { CheckPython(menuBar, true); }));
    }

    static void InstallPythonPackages(string core, CityBuilderMenuBar menuBar, Action post) {
        var startInfo = new ProcessStartInfo();
        startInfo.WorkingDirectory = Path.Combine(PathHelper.BasePath(), "pythonInstaller");
        startInfo.FileName = "install_packages.bat";
        startInfo.Arguments = "\"" + Path.Combine(PathHelper.BasePath(), "Files", "cores", core, "pythonInfo", "requirements.txt") + "\"";
        startInfo.WindowStyle = ProcessWindowStyle.Normal;
        startInfo.UseShellExecute = true;
        startInfo.CreateNoWindow = false;
        var process = Process.Start(startInfo);
        menuBar.StartCoroutine(WaitForInstallEnd(process, delegate { CheckPythonForCore(menuBar, core, post, true); }));
    }

    static System.Collections.IEnumerator WaitForInstallEnd(Process process, Action post) {
        while (!process.HasExited) {
            yield return new WaitForEndOfFrame();
        }
        post.Invoke();
    }

    static int GetServerPort() {
        return SettingsManager.Get("pythonPort", 5005);
    }

    static Process RunScriptBase(string script, bool absolute, List<string> args, bool useShellExecute = false) {
        var startInfo = new ProcessStartInfo();
        var pythonExe = (GetPythonDebug() || useShellExecute) ? "python.exe" : "pythonw.exe";
        startInfo.FileName = Path.Combine(GetPythonPath(), pythonExe);
        var trueScriptName = absolute ? script : Path.Combine(PathHelper.FilesPath(true), "python", script);
        var argsStr = "\"" + trueScriptName + "\"";
        foreach (var arg in args) {
            argsStr += " \"" + arg + "\"";

        }
        startInfo.Arguments = argsStr;
        startInfo.WindowStyle = useShellExecute ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden;
        if (useShellExecute) {
            startInfo.UseShellExecute = true;
            startInfo.CreateNoWindow = false;
        } else {
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
        }
        var process = Process.Start(startInfo);
        return process;
    }

    static string GetProcessData(Process process) {
        if (!process.StartInfo.RedirectStandardOutput) return null;
        var path = new DirectoryInfo(Application.dataPath);
        if (process.ExitCode != 0) {
            var err = process.StandardError.ReadToEnd();
            var lines = err.Split('\n');
            File.AppendAllText(Path.Combine(path.Parent.ToString(), "pythonLog.txt"), err);
            var output = process.StandardOutput.ReadToEnd();
            File.AppendAllText(Path.Combine(path.Parent.ToString(), "pythonLog.txt"), output);
            return lines.Length > 1 ? lines[lines.Length - 2].Split(':')[1].TrimStart() : "";
        } else {
            var output = process.StandardOutput.ReadToEnd();
            File.AppendAllText(Path.Combine(path.Parent.ToString(), "pythonLog.txt"), output);
            return process.StandardOutput.ReadToEnd();
        }
    }

    public static (int code, string data) RunScript(string script, bool absolute, List<string> args, bool useShellExecute = false) {
        var process = RunScriptBase(script, absolute, args, useShellExecute);
        while (!process.HasExited) { }
        return (process.ExitCode, GetProcessData(process));
    }

    public static void RunScriptAsync(string script, bool absolute, List<string> args, Action<int, string> post, ElementManager manager, bool useShellExecute = false) {
        var process = RunScriptBase(script, absolute, args, useShellExecute);
        manager.builder.StartCoroutine(WaitForScriptEnd(process, post));
    }

    static System.Collections.IEnumerator WaitForScriptEnd(Process process, Action<int, string> post) {
        while (!process.HasExited) {
            yield return new WaitForEndOfFrame();
        }
        post.Invoke(process.ExitCode, GetProcessData(process));
    }

    public static void StartServer() {
        if (halt || !PathHelper.CoreAvailable()) return;
        var customMeshFormats = CoreManager.GetList("customMeshFormats", new List<string>());
        var customTextureFormats = CoreManager.GetList("customTextureFormats", new List<string>());
        if (customMeshFormats.Count == 0 && customTextureFormats.Count == 0) return;
        if (serverProcess != null && serverProcess.HasExited) {
            GetProcessData(serverProcess);
        } else if (serverProcess != null && !serverProcess.HasExited) {
            if (client != null) client.Close();
            serverProcess.CloseMainWindow();
        }
        var serverPath = Path.Combine(PathHelper.FilesPath(true), "python", "server.py");
        var workingDirectory = Path.Combine(PathHelper.FilesPath(), "server");
        var startInfo = new ProcessStartInfo();
        var pythonExe = GetPythonDebug() ? "python.exe" : "pythonw.exe";
        startInfo.FileName = Path.Combine(GetPythonPath(), pythonExe);
        startInfo.Arguments = "\"" + serverPath + "\" " + GetServerPort() + " " + Process.GetCurrentProcess().Id + " \"" + workingDirectory + "\"";
        startInfo.WindowStyle = ProcessWindowStyle.Minimized;
        if (!GetPythonDebug()) {
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
        }
        MonoBehaviour.print("Starting Python server (from: " + startInfo.FileName + ") using as modules path: " + workingDirectory);
        serverProcess = Process.Start(startInfo);
    }

    static void StartClient() {
        if (serverProcess == null || serverProcess.HasExited) {
            StartServer();
        } else {
            if (client != null) client.Close();
            client = new TcpClient();
            client.Client.SendTimeout = timeout;
            client.Client.ReceiveTimeout = timeout;
            try {
                client.Connect("127.0.0.1", GetServerPort());
                stream = client.GetStream();
            } catch (Exception e) {
                MonoBehaviour.print(e.ToString());
            }
        }
    }

    public static void StopServer() {
        if (serverProcess != null && !serverProcess.HasExited) {
            if (client != null) client.Dispose();
            serverProcess.CloseMainWindow();
        }
    }

#if UNITY_EDITOR
    public static void StopServerEditor(UnityEditor.PlayModeStateChange playModeState) {
        if (playModeState == UnityEditor.PlayModeStateChange.ExitingPlayMode) {
            halt = true;
            StopServer();
        }
    }
#endif

    public static byte[] SendRequest(string message) {
        return SendRequestAux(message);
    }

    static byte[] SendRequestAux(string message, int maxRetries = 1) {
        if (halt || !PathHelper.CoreAvailable()) return null;
        try {
            if (client == null || !client.Connected) StartClient();
            var data = Encoding.ASCII.GetBytes(message);
            stream.Write(data, 0, data.Length);

            var lenBytes = new byte[4];
            var lenBytesRead = stream.Read(lenBytes, 0, lenBytes.Length);
            var inLength = BitConverter.ToInt32(lenBytes, 0);

            var buffer = new byte[inLength];

            int offset = 0;
            int read = -1;
            var swa = new Stopwatch();
            swa.Start();
            do {
                read = stream.Read(buffer, offset, buffer.Length - offset);
                offset += read;
            } while (offset < inLength && swa.ElapsedMilliseconds < timeout);
            swa.Stop();

            if (offset == 0) throw new Exception("Empty response");
            if (offset < inLength) throw new Exception("Truncated response");
            if (offset > inLength) throw new Exception("Invalid response length"); //should never happen
            return buffer;
        } catch (Exception e) {
            if (maxRetries > 0) {
                UnityEngine.Debug.LogWarning("Retrying request to Python server after error: " + e.ToString() + "\n\nRequest was: " + message);
                StartClient();
                return SendRequestAux(message, maxRetries - 1);
            } else {
                UnityEngine.Debug.LogError("Error while sending request to Python server: " + e.ToString() + "\n\nRequest was: " + message);
                return null;
            }
        }
    }
}

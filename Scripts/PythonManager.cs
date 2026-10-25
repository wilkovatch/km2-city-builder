using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Godot;
using SM = StringManager;

class PythonManager {
    static TcpClient client;
    static NetworkStream stream;
    static Process serverProcess;
    static bool halt = false;
    const int timeout = 1000;
    //portable build used where there is no installer script (Linux), same Python minor version (3.15) as the Windows installer
    const string portablePythonUrl = "https://github.com/astral-sh/python-build-standalone/releases/download/20261021/cpython-3.15.1%2B20261021-x86_64-unknown-linux-gnu-install_only.tar.gz";

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

    //only the interpreter in pythonPath (or in this program's "python" folder if not set) is used, never the
    //system-wide one: if it is missing the returned path does not exist and the status check reports it
    static string GetPythonExecutable() {
        return GetPythonExecutable(GetPythonPath());
    }

    static string GetPythonExecutable(string path) {
        if (File.Exists(path)) return path; //pythonPath pointing directly to the executable
        var names = OperatingSystem.IsWindows()
            ? new[] { "python.exe", "python3.exe" }
            : new[] { Path.Combine("bin", "python3"), "python3", Path.Combine("bin", "python"), "python" };
        foreach (var name in names) {
            var candidate = Path.Combine(path, name);
            if (File.Exists(candidate)) return candidate;
        }
        return Path.Combine(path, names[0]);
    }

    static bool CheckDependencies(List<string> dependencies) {
        try {
            string pythonExe = GetPythonExecutable();
            var startInfo = new ProcessStartInfo();
            startInfo.FileName = pythonExe;
            string imports = "";
            foreach (var d in dependencies) {
                if (d != null && d != "") imports += "import " + d + "; ";
            }
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(imports);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            var process = Process.Start(startInfo);
            process.WaitForExit();
            return process.ExitCode == 0;
        } catch (Exception e) {
            GameEngine.Print(e.Message);
            return false;
        }
    }

    static InstallationStatus GetPythonStatus(string core = null) {
        string path = GetPythonPath();
        string realPath = GetPythonPath(true);
        bool specified = (realPath != null && realPath != "");
        bool available = false;
        bool valid = false;
        available = File.Exists(GetPythonExecutable()) && CheckDependencies(new List<string>());
        if (available) {
            string dependenciesTxtFile = core != null
                ? Path.Combine(PathHelper.BasePath(), "Files", "cores", core, "pythonInfo", "imports.txt")
                : Path.Combine(PathHelper.BasePath(), "pythonInstaller", "imports.txt");
            if (!File.Exists(dependenciesTxtFile)) return specified ? InstallationStatus.UserNotValid : InstallationStatus.DefaultNotValid;
            valid = CheckDependencies(new List<string>(File.ReadAllLines(dependenciesTxtFile)));
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
                GameEngine.Print("Python checked and valid");
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
                GameEngine.Print("Python checked and valid");
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
        // A core check only verifies its own modules. Check the interpreter and
        // base installer requirements first so a missing local Python gets the
        // same install prompt as the settings action.
        var pythonStatus = GetPythonStatus();
        if (pythonStatus != InstallationStatus.Valid) {
            return CheckPython(menuBar, alreadyCalled);
        }
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

    //the checks without the prompts of CheckPython and CheckPythonForCore
    public static bool IsPythonReady() {
        return GetPythonStatus() == InstallationStatus.Valid;
    }

    public static bool IsReadyForCore(string core) {
        return IsPythonReady() && GetPythonStatus(core) == InstallationStatus.Valid;
    }

    //installs the Python modules required by the core (Python itself must be available)
    public static async System.Threading.Tasks.Task InstallCoreRequirementsAsync(string core) {
        var requirements = Path.Combine(PathHelper.BasePath(), "Files", "cores", core, "pythonInfo", "requirements.txt");
        var startInfo = new ProcessStartInfo {
            FileName = GetPythonExecutable(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(requirements)
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("pip");
        startInfo.ArgumentList.Add("install");
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(requirements);
        try {
            using var process = Process.Start(startInfo);
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            LogProcessOutput(await stdoutTask, await stderrTask);
        } catch (Exception e) {
            GameEngine.LogError("Could not install the Python packages: " + e.Message);
        }
    }

    static void InstallPython(CityBuilderMenuBar menuBar) {
        if (!OperatingSystem.IsWindows()) {
            //no installer script: download a portable Python (same minor version as on Windows) in this program's "python" folder
            GameEngine.Print("Downloading Python from: " + portablePythonUrl);
            var basePath = PathHelper.BasePath();
            var installTask = System.Threading.Tasks.Task.Run(() => InstallPortablePython(basePath));
            WaitForInstallEnd(installTask, delegate {
                if (installTask.IsFaulted) {
                    GameEngine.LogError("Could not install Python: " + installTask.Exception.GetBaseException().Message);
                    CheckPython(menuBar, true);
                    return;
                }
                var pythonPath = Path.Combine(PathHelper.BasePath(), "python");
                InstallRequirements(Path.Combine(PathHelper.BasePath(), "pythonInstaller", "requirements.txt"),
                    delegate { CheckPython(menuBar, true); }, menuBar, GetPythonExecutable(pythonPath));
            }, menuBar);
            return;
        }
        var startInfo = new ProcessStartInfo();
        startInfo.WorkingDirectory = Path.Combine(PathHelper.BasePath(), "pythonInstaller");
        startInfo.FileName = "run_installer.bat";
        startInfo.WindowStyle = ProcessWindowStyle.Normal;
        startInfo.UseShellExecute = true;
        startInfo.CreateNoWindow = false;
        var process = Process.Start(startInfo);
        WaitForInstallEnd(process, delegate { CheckPython(menuBar, true); }, menuBar);
    }

    static void InstallPythonPackages(string core, CityBuilderMenuBar menuBar, Action post) {
        InstallRequirements(Path.Combine(PathHelper.BasePath(), "Files", "cores", core, "pythonInfo", "requirements.txt"),
            delegate { CheckPythonForCore(menuBar, core, post, true); }, menuBar);
    }

    static void InstallRequirements(string requirements, Action completed, Node caller, string pythonExe = null) {
        var startInfo = new ProcessStartInfo {
            FileName = pythonExe ?? GetPythonExecutable(),
            UseShellExecute = false,
            CreateNoWindow = !GetPythonDebug(),
            WorkingDirectory = Path.GetDirectoryName(requirements)
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("pip");
        startInfo.ArgumentList.Add("install");
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(requirements);
        Process process;
        try {
            process = Process.Start(startInfo);
        } catch (Exception e) {
            //e.g. the interpreter is missing: the completion check reports the failure
            GameEngine.LogError("Could not install the Python packages: " + e.Message);
            completed.Invoke();
            return;
        }
        WaitForInstallEnd(process, completed, caller);
    }

    //the archive contains a "python" folder, so it is extracted directly in this program's folder
    static async System.Threading.Tasks.Task InstallPortablePython(string basePath) {
        var pythonPath = Path.Combine(basePath, "python");
        var archivePath = Path.Combine(basePath, "pythonInstaller", "pythonTemp.tar.gz");
        using (var http = new System.Net.Http.HttpClient())
        using (var download = await http.GetStreamAsync(portablePythonUrl))
        using (var file = File.Create(archivePath)) {
            await download.CopyToAsync(file);
        }
        try {
            if (Directory.Exists(pythonPath)) Directory.Delete(pythonPath, true);
            //the system tar is used because System.Formats.Tar misreads some of the entry names in this archive
            var tarInfo = new ProcessStartInfo { FileName = "tar", UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            tarInfo.ArgumentList.Add("-xzf");
            tarInfo.ArgumentList.Add(archivePath);
            tarInfo.ArgumentList.Add("-C");
            tarInfo.ArgumentList.Add(basePath);
            using var tar = Process.Start(tarInfo);
            var tarError = await tar.StandardError.ReadToEndAsync();
            await tar.WaitForExitAsync();
            if (tar.ExitCode != 0) throw new IOException("tar failed: " + tarError.Trim());
        } finally {
            File.Delete(archivePath);
        }
    }

    static async void WaitForInstallEnd(Process process, Action post, Node caller) {
        while (!process.HasExited) {
            await caller.ToSignal(caller.GetTree(), "process_frame");
        }
        post.Invoke();
    }

    static async void WaitForInstallEnd(System.Threading.Tasks.Task task, Action post, Node caller) {
        while (!task.IsCompleted) {
            await caller.ToSignal(caller.GetTree(), "process_frame");
        }
        post.Invoke();
    }

    static int GetServerPort() {
        return SettingsManager.Get("pythonPort", 5005);
    }

    static Process RunScriptBase(string script, bool absolute, List<string> args, bool useShellExecute = false, bool redirectInput = false) {
        var startInfo = new ProcessStartInfo();
        startInfo.FileName = GetPythonExecutable();
        var trueScriptName = absolute ? script : Path.Combine(PathHelper.FilesPath(true), "python", script);
        startInfo.ArgumentList.Add(trueScriptName);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg ?? "");
        startInfo.WindowStyle = useShellExecute ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden;
        if (useShellExecute) {
            //shell execute gives the script its own visible console: without it the script would share the
            //(possibly hidden) console of this process, and scripts waiting for a key press would never end
            startInfo.UseShellExecute = true;
            startInfo.CreateNoWindow = false;
        } else {
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = !GetPythonDebug();
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardInput = redirectInput;
        }
        var process = Process.Start(startInfo);
        return process;
    }

    //starts a script talking to the program on its standard input and output (the scripting API protocol, see
    //Api.ScriptRunner): everything is redirected, the citybuilder module (Files/python) can be imported
    public static Process StartScriptProcess(string script, string workingDirectory, Dictionary<string, string> environment) {
        var utf8 = new UTF8Encoding(false);
        var startInfo = new ProcessStartInfo {
            FileName = GetPythonExecutable(),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(script);
        var libraryPath = Path.Combine(PathHelper.FilesPath(true), "python");
        var pythonPath = startInfo.Environment.TryGetValue("PYTHONPATH", out var current) && !string.IsNullOrEmpty(current)
            ? libraryPath + Path.PathSeparator + current : libraryPath;
        startInfo.Environment["PYTHONPATH"] = pythonPath;
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        if (environment != null) {
            foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;
        }
        return Process.Start(startInfo);
    }

    static string GetProcessData(Process process) {
        if (!process.StartInfo.RedirectStandardOutput) return null;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        LogProcessOutput(stdout, stderr);
        return process.ExitCode == 0 ? stdout : LastError(stderr);
    }

    internal static string LastError(string stderr) {
        if (string.IsNullOrWhiteSpace(stderr)) return "Python process failed without an error message.";
        var lines = stderr.Trim().Split('\n');
        var last = lines[lines.Length - 1].Trim();
        //the last line of a traceback is "ExceptionType: message", the message is often a string key to translate
        var colon = last.IndexOf(':');
        if (colon < 0) return last;
        var message = last.Substring(colon + 1).Trim();
        return message != "" ? message : last;
    }

    internal static void LogProcessOutput(string stdout, string stderr) {
        var log = Path.Combine(PathHelper.BasePath(), "pythonLog.txt");
        if (!string.IsNullOrEmpty(stderr)) File.AppendAllText(log, stderr + System.Environment.NewLine);
        if (!string.IsNullOrEmpty(stdout)) File.AppendAllText(log, stdout + System.Environment.NewLine);
    }

    public static (int code, string data) RunScript(string script, bool absolute, List<string> args, bool useShellExecute = false) {
        var process = RunScriptBase(script, absolute, args, useShellExecute);
        if (process.StartInfo.RedirectStandardOutput) {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            LogProcessOutput(stdout, stderr);
            return (process.ExitCode, process.ExitCode == 0 ? stdout : LastError(stderr));
        }
        process.WaitForExit();
        return (process.ExitCode, null);
    }

    public static void RunScriptAsync(string script, bool absolute, List<string> args, Action<int, string> post, ElementManager manager, bool useShellExecute = false) {
        var process = RunScriptBase(script, absolute, args, useShellExecute);
        WaitForScriptEnd(process, post, manager.builder);
    }

    //runs the script and waits for it to end: the exit code and the output (the last error if it failed)
    public static async System.Threading.Tasks.Task<(int code, string data)> RunScriptTask(string script, bool absolute, List<string> args, bool useShellExecute = false) {
        var process = RunScriptBase(script, absolute, args, useShellExecute, !useShellExecute);
        if (process.StartInfo.RedirectStandardInput) {
            //without a console of their own, the scripts waiting for a key press at the end (e.g. the exporters) get it
            try {
                for (int i = 0; i < 4; i++) process.StandardInput.WriteLine();
                process.StandardInput.Close();
            } catch (IOException) {
                //the script ended without reading it
            }
        }
        var stdoutTask = process.StartInfo.RedirectStandardOutput ? process.StandardOutput.ReadToEndAsync() : System.Threading.Tasks.Task.FromResult("");
        var stderrTask = process.StartInfo.RedirectStandardError ? process.StandardError.ReadToEndAsync() : System.Threading.Tasks.Task.FromResult("");
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        LogProcessOutput(stdout, stderr);
        return (process.ExitCode, process.ExitCode == 0 ? stdout : LastError(stderr));
    }

    static async void WaitForScriptEnd(Process process, Action<int, string> post, Node caller) {
        var stdoutTask = process.StartInfo.RedirectStandardOutput ? process.StandardOutput.ReadToEndAsync() : System.Threading.Tasks.Task.FromResult("");
        var stderrTask = process.StartInfo.RedirectStandardError ? process.StandardError.ReadToEndAsync() : System.Threading.Tasks.Task.FromResult("");
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        LogProcessOutput(stdout, stderr);
        post?.Invoke(process.ExitCode, process.ExitCode == 0 ? stdout : LastError(stderr));
    }

    public static void StartServer() {
        if (halt || !PathHelper.CoreAvailable()) return;
        var customMeshFormats = CoreManager.GetList("customMeshFormats", new List<string>());
        var customTextureFormats = CoreManager.GetList("customTextureFormats", new List<string>());
        if (customMeshFormats.Count == 0 && customTextureFormats.Count == 0) return;
        if (serverProcess != null && serverProcess.HasExited) {
            GetProcessData(serverProcess);
        } else if (serverProcess != null && !serverProcess.HasExited) {
            //restart it, so that the modules of the current core are loaded (e.g. after switching city or core)
            StopServer();
        }
        var serverPath = Path.Combine(PathHelper.FilesPath(true), "python", "server.py");
        var workingDirectory = Path.Combine(PathHelper.FilesPath(), "server");
        var startInfo = new ProcessStartInfo();
        startInfo.FileName = GetPythonExecutable();
        startInfo.ArgumentList.Add(serverPath);
        startInfo.ArgumentList.Add(GetServerPort().ToString());
        startInfo.ArgumentList.Add(Process.GetCurrentProcess().Id.ToString());
        startInfo.ArgumentList.Add(workingDirectory);
        if (GetPythonDebug() && OperatingSystem.IsWindows()) {
            //as in RunScriptBase, shell execute is needed for the server to get its own visible console
            //(elsewhere it opens no console, the output goes to this program's standard output instead)
            startInfo.UseShellExecute = true;
            startInfo.WindowStyle = ProcessWindowStyle.Minimized;
        } else {
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
        }
        GameEngine.Print("Starting Python server (from: " + startInfo.FileName + ") using as modules path: " + workingDirectory);
        serverProcess = Process.Start(startInfo);
    }

    static void StartClient() {
        if (client != null) client.Dispose();
        client = null;
        stream = null;
        if (serverProcess == null || serverProcess.HasExited) StartServer();
        if (serverProcess == null) return;

        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < timeout && !serverProcess.HasExited) {
            var candidate = new TcpClient();
            candidate.SendTimeout = timeout;
            candidate.ReceiveTimeout = timeout;
            try {
                candidate.Connect("127.0.0.1", GetServerPort());
                client = candidate;
                stream = client.GetStream();
                return;
            } catch (SocketException) {
                candidate.Dispose();
                System.Threading.Thread.Sleep(50);
            } catch (Exception e) {
                candidate.Dispose();
                GameEngine.LogWarning("Could not connect to Python server: " + e.Message);
                return;
            }
        }
        GameEngine.LogWarning(serverProcess.HasExited
            ? "Python server exited before accepting a connection."
            : "Timed out waiting for Python server to accept a connection.");
    }

    public static void StopServer() {
        if (serverProcess != null && !serverProcess.HasExited) {
            if (client != null) client.Dispose();
            client = null;
            stream = null;
            //the server has no window to close, so it is terminated directly,
            //otherwise it would keep the port and serve the modules of the previous core
            try {
                serverProcess.Kill(true);
                serverProcess.WaitForExit(timeout);
            } catch (Exception e) {
                GameEngine.LogWarning("Could not stop the Python server: " + e.Message);
            }
        }
    }


    public static byte[] SendRequest(string message) {
        return SendRequestAux(message);
    }

    static byte[] SendRequestAux(string message, int maxRetries = 1) {
        if (halt || !PathHelper.CoreAvailable()) return null;
        try {
            if (client == null || !client.Connected) StartClient();
            if (stream == null) throw new IOException("Python server connection is unavailable.");
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
                GameEngine.LogWarning("Retrying request to Python server after error: " + e.ToString() + "\n\nRequest was: " + message);
                StartClient();
                return SendRequestAux(message, maxRetries - 1);
            } else {
                GameEngine.LogError("Error while sending request to Python server: " + e.ToString() + "\n\nRequest was: " + message);
                return null;
            }
        }
    }
}

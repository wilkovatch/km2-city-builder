using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using SM = StringManager;

namespace Api {
    //Runs a Python script (of the Scripts menu, or given with the script command of the command line). The script talks to the
    //API on its standard input and output, a JSON command per line as in CommandDispatcher: its requests are read on a
    //thread and queued to the dispatcher, the responses and events are written back on another one; its standard error
    //goes to the log. While it runs the editor can't be used (except the camera and the Cancel button of the progress
    //bar). Unless the script sets "transaction": false, its changes are a single undo step, undone if it fails or is
    //cancelled. One script runs at a time.
    public class ScriptRunner {
        public class Result {
            public bool success;
            public bool cancelled;
            public int exitCode;
            public string error;
            public bool undone; //the changes made before failing were undone
            public List<(string title, string text)> messages = new List<(string, string)>();
        }

        public static ScriptRunner Current { get; private set; }
        public static bool IsRunning => Current != null;
        //the transaction of the run is open: the script can't open or close transactions, nor load another city
        public static bool OwnsTransaction => Current != null && Current.ownsTransaction;

        public readonly ScriptCatalog.ScriptInfo script;
        public readonly JToken inputs;
        public readonly List<int> selection;
        readonly CityBuilderMenuBar builder;
        readonly List<(string title, string text)> messages = new List<(string, string)>();
        readonly ConcurrentQueue<string> errorLines = new ConcurrentQueue<string>();
        readonly BlockingCollection<string> replies = new BlockingCollection<string>();
        readonly StringBuilder stderr = new StringBuilder();
        Process process;
        bool ownsTransaction = false;
        volatile bool cancelled = false;
        float progress = 0;
        string progressText;

        ScriptRunner(CityBuilderMenuBar builder, ScriptCatalog.ScriptInfo script, JToken inputs, List<int> selection) {
            this.builder = builder;
            this.script = script;
            this.inputs = inputs;
            this.selection = selection;
            progressText = script.label;
        }

        public void SetProgress(float value, string text) {
            progress = Mathf.Clamp(value, 0, 1);
            if (text != null) progressText = text;
        }

        public void AddMessage(string title, string text) {
            messages.Add((title, text));
        }

        //stops the script: its changes are undone when it has ended
        public void Cancel() {
            cancelled = true;
            try {
                if (process != null && !process.HasExited) process.Kill(true);
            } catch (System.Exception e) {
                GameEngine.LogWarning("Could not stop the script: " + e.Message);
            }
        }

        //runs the script with the inputs (by parameter name) and the ids of the elements selected; interactive: Python
        //missing and the errors are reported with popups
        public static async Task<Result> Run(CityBuilderMenuBar builder, ScriptCatalog.ScriptInfo script, JToken inputs = null, List<int> selection = null, bool interactive = true) {
            if (Current != null) return Failed(builder, interactive, script, SM.Get("SCRIPT_BUSY"));
            if (!PythonManager.IsPythonReady()) {
                if (interactive) {
                    PythonManager.CheckPython(builder);
                    return new Result { error = SM.Get("SCRIPT_NO_PYTHON") };
                }
                return Failed(builder, false, script, SM.Get("SCRIPT_NO_PYTHON"));
            }
            var runner = new ScriptRunner(builder, script, inputs ?? new JObject(), selection ?? new List<int>());
            Current = runner;
            Result res;
            try {
                res = await runner.RunProcess();
            } finally {
                Current = null;
            }
            runner.Report(res, interactive);
            return res;
        }

        static Result Failed(CityBuilderMenuBar builder, bool interactive, ScriptCatalog.ScriptInfo script, string error) {
            var res = new Result { error = error };
            if (interactive) builder.CreateAlert(SM.Get("ERROR"), SM.Get("SCRIPT_FAILED").Replace("$NAME", script.label) + "\n" + error, SM.Get("OK"), null, 220);
            else GameEngine.LogError("Script " + script.label + ": " + error);
            return res;
        }

        async Task<Result> RunProcess() {
            var api = ApiHost.Instance.Api;
            var dispatcher = ApiHost.Instance.Dispatcher;
            var savedHistory = api.recordHistory;
            var savedAutoFlush = api.autoFlush;
            var res = new Result();
            if (api.HasCity) builder.BeginExternalChange(); //the editors are closed, as before an undo
            builder.SetScriptRunning(true);
            var bar = builder.helper.curProgressBar;
            try {
                if (script.transaction && api.HasCity && api.recordHistory && !History.UndoManager.IsGroupOpen) {
                    api.BeginTransaction(script.label);
                    ownsTransaction = true;
                }
                var environment = new Dictionary<string, string> { ["KM2CB_SCRIPT"] = "1" };
                process = PythonManager.StartScriptProcess(script.entry, script.folder, environment);
                process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorLines.Enqueue(e.Data); };
                process.BeginErrorReadLine();
                var reader = new Thread(ReadRequests) { IsBackground = true, Name = "Script requests" };
                var writer = new Thread(WriteReplies) { IsBackground = true, Name = "Script replies" };
                reader.Start();
                writer.Start();
                bar?.SetCancelAction(Cancel);
                while (!process.HasExited) {
                    ShowProgress(api, bar);
                    FlushErrorLines();
                    await builder.ToSignal(builder.GetTree(), "process_frame");
                }
                //the rest of the standard error (not waited for if a process started by the script keeps it open)
                var exited = Task.Run(() => process.WaitForExit());
                await Task.WhenAny(exited, Task.Delay(2000));
                reader.Join(2000);
                //the commands it sent before ending
                while (!dispatcher.IsIdle) await builder.ToSignal(builder.GetTree(), "process_frame");
                replies.CompleteAdding();
                writer.Join(1000);
                res.exitCode = process.ExitCode;
            } catch (ApiException e) {
                res.error = e.Message;
            } catch (System.Exception e) {
                res.error = e.Message;
                GameEngine.LogError("Script " + script.label + ": " + e);
            } finally {
                bar?.SetCancelAction(null);
                bar?.SetActive(false);
                FlushErrorLines();
                ownsTransaction = false;
                res.cancelled = cancelled;
                if (res.error == null && !cancelled && res.exitCode != 0) {
                    //the message can be a string key (of the script, the core or the program)
                    var error = PythonManager.LastError(stderr.ToString());
                    res.error = script.GetString(error) ?? error;
                }
                res.success = res.error == null && !cancelled;
                res.undone = CloseTransaction(api, res.success) && !res.success;
                RestoreSettings(api, savedHistory, savedAutoFlush);
                if (api.HasCity) builder.EndExternalChange();
                builder.SetScriptRunning(false);
                PythonManager.LogProcessOutput(null, stderr.ToString());
                res.messages.AddRange(messages);
                process?.Dispose();
            }
            return res;
        }

        //the transaction of the run (or one the script left open) is committed, or rolled back if the script failed
        static bool CloseTransaction(CityApi api, bool success) {
            if (!api.HasCity || !History.UndoManager.IsGroupOpen) return false;
            try {
                if (success) api.CommitTransaction();
                else api.RollbackTransaction();
                return true;
            } catch (ApiException e) {
                GameEngine.LogWarning("Script: could not close the transaction: " + e.Message);
                return false;
            }
        }

        //the session settings changed by the script (api.configure) are restored, and the city updated
        static void RestoreSettings(CityApi api, bool history, bool autoFlush) {
            try {
                api.Configure(history, autoFlush);
                if (api.HasCity) api.Flush();
            } catch (ApiException e) {
                GameEngine.LogWarning("Script: could not restore the API settings: " + e.Message);
            }
        }

        void ShowProgress(CityApi api, ProgressBar bar) {
            if (bar == null || api.IsLongRunning) return; //a long operation shows its own progress meanwhile
            bar.SetText(progressText);
            bar.SetProgress(progress);
            bar.SetActive(true);
        }

        void FlushErrorLines() {
            while (errorLines.TryDequeue(out var line)) {
                stderr.AppendLine(line);
                GameEngine.Print("[" + script.label + "] " + line);
            }
        }

        //on its own thread: the lines written by the script are the requests
        void ReadRequests() {
            try {
                string line;
                while ((line = process.StandardOutput.ReadLine()) != null) {
                    if (cancelled) continue;
                    var request = line.Trim();
                    if (request.Length == 0) continue;
                    if (request[0] != '{') {
                        //not a command (e.g. written by a module to the original standard output): only logged
                        errorLines.Enqueue(line);
                        continue;
                    }
                    ApiHost.Instance.Dispatcher.Enqueue(request, reply => {
                        try {
                            replies.Add(reply);
                        } catch (System.InvalidOperationException) {
                            //the script has ended
                        }
                    });
                }
            } catch (System.Exception e) when (e is IOException || e is System.ObjectDisposedException) {
                //the script has ended
            }
        }

        //on its own thread: the responses and events are written to the script
        void WriteReplies() {
            try {
                foreach (var reply in replies.GetConsumingEnumerable()) {
                    process.StandardInput.WriteLine(reply);
                    process.StandardInput.Flush();
                }
            } catch (System.Exception e) when (e is IOException || e is System.ObjectDisposedException || e is System.InvalidOperationException) {
                //the script has ended without reading them
            }
        }

        void Report(Result res, bool interactive) {
            var name = script.label;
            if (!interactive) {
                if (res.cancelled) GameEngine.Print("Script " + name + " cancelled");
                else if (!res.success) GameEngine.LogError("Script " + name + " failed: " + res.error);
                foreach (var m in res.messages) GameEngine.Print((m.title != null ? m.title + ": " : "") + m.text);
                return;
            }
            if (res.cancelled) return;
            if (!res.success) {
                var text = SM.Get("SCRIPT_FAILED").Replace("$NAME", name) + "\n" + res.error + (res.undone ? "\n" + SM.Get("SCRIPT_UNDONE") : "");
                builder.CreateAlert(SM.Get("ERROR"), text, SM.Get("OK"), null, 220);
            } else if (res.messages.Count > 0) {
                var title = res.messages[0].title ?? name;
                var texts = new List<string>();
                foreach (var m in res.messages) texts.Add(m.text);
                builder.CreateAlert(title, string.Join("\n", texts), SM.Get("OK"), null, 220);
            }
        }
    }
}

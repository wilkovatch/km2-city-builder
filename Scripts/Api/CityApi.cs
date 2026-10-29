using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Api {
    //Marks the methods of CityApi callable as JSON commands, with their method name
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class ApiMethodAttribute : System.Attribute {
        public readonly string name;
        public readonly string description;

        public ApiMethodAttribute(string name, string description = null) {
            this.name = name;
            this.description = description;
        }
    }

    //The API to load, read, edit and export cities, used by scripts. Every method runs on the main thread. Each
    //change is a single undo step (see ApplyChange); the methods returning a Task last several frames.
    //The JSON commands (CommandDispatcher) map to the methods marked with ApiMethod, by parameter name.
    public partial class CityApi {
        public const int ProtocolVersion = 1;

        public readonly CityBuilderMenuBar builder;
        ElementManager M => builder.helper.elementManager;

        //changes are recorded in the undo history (needed to undo a failed batch)
        public bool recordHistory = true;
        //the city is updated after every change, so that the derived data (anchors, curves, ...) can be read
        public bool autoFlush = true;
        //the progress of the running long operation
        public System.IProgress<(float value, string text)> progress;

        int batchDepth = 0;
        int busy = 0;
        //the roads being drawn, so that a road started on the middle of another one is split by the next point
        readonly Dictionary<Road, Operations.RoadDrawing> drawings = new Dictionary<Road, Operations.RoadDrawing>();

        public CityApi(CityBuilderMenuBar builder) {
            this.builder = builder;
        }

        public bool HasCity => PreferencesManager.workingDirectory != "";

        void RequireCity() {
            if (!HasCity) throw new ApiException("no_city", "no city is loaded");
        }

        //a long operation (see RunLong) or a whole city generation is running
        public bool IsLongRunning => busy > 0 || IsLongOperationRunning();

        static bool IsLongOperationRunning() {
            //the long operations (whole city generation, rotation) keep the history busy while running
            return History.UndoManager.IsBusy;
        }

        //updates the city now (the elements changed are rebuilt) and records the undo steps of the changes
        [ApiMethod("city.flush", "Rebuilds the changed elements now (needed only with autoFlush off)")]
        public void Flush() {
            RequireCity();
            M.worldChanged = true;
            M.ProcessUpdate();
            History.UndoManager.FinalizePending();
        }

        internal Task SettleProjectedGeometry(bool force = false) {
            return autoFlush || force ? M.SettleProjectedSplines() : Task.CompletedTask;
        }

        //runs a change of the city as an undo step: the editors open in the GUI are closed first (like before an undo),
        //and on errors the partial change is undone (when recorded in the history)
        T ApplyChange<T>(string label, System.Func<T> action) {
            RequireCity();
            if (busy > 0 || (batchDepth == 0 && IsLongOperationRunning())) throw new ApiException("busy", "another operation is running");
            if (batchDepth == 0) builder.BeginExternalChange();
            if (!recordHistory) History.UndoManager.Suppress(true);
            var version = History.UndoManager.Version;
            T res;
            History.UndoManager.Begin(label);
            try {
                res = action();
            } catch {
                History.UndoManager.Commit();
                M.FlagAsChanged(true);
                if (batchDepth == 0) {
                    RollBack(version);
                    if (!recordHistory) History.UndoManager.Suppress(false);
                    builder.EndExternalChange();
                }
                throw;
            }
            History.UndoManager.Commit();
            M.FlagAsChanged(true);
            if (autoFlush) Flush();
            if (batchDepth == 0) {
                if (!recordHistory) History.UndoManager.Suppress(false);
                builder.EndExternalChange();
            }
            return res;
        }

        void ApplyChange(string label, System.Action action) {
            ApplyChange<object>(label, () => { action(); return null; });
        }

        //undoes the step recorded since the version, if any
        void RollBack(int version) {
            M.worldChanged = true;
            M.ProcessUpdate();
            History.UndoManager.FinalizePending();
            if (recordHistory && History.UndoManager.Version != version) {
                History.UndoManager.UndoAndForget();
                M.ProcessUpdate();
            }
        }

        //runs the commands as a single undo step; atomic: if one fails, the ones before it are undone
        public async Task<List<object>> RunBatch(List<System.Func<Task<object>>> commands, bool atomic) {
            RequireCity();
            if (busy > 0 || IsLongOperationRunning()) throw new ApiException("busy", "another operation is running");
            builder.BeginExternalChange();
            if (!recordHistory) History.UndoManager.Suppress(true);
            var version = History.UndoManager.Version;
            var results = new List<object>();
            batchDepth++;
            History.UndoManager.Begin("batch");
            try {
                foreach (var command in commands) {
                    results.Add(await command());
                }
            } catch {
                batchDepth--;
                History.UndoManager.Commit();
                M.FlagAsChanged(true);
                if (atomic) {
                    RollBack(version);
                } else {
                    Flush();
                }
                if (!recordHistory) History.UndoManager.Suppress(false);
                builder.EndExternalChange();
                throw;
            }
            batchDepth--;
            History.UndoManager.Commit();
            M.FlagAsChanged(true);
            Flush();
            if (!recordHistory) History.UndoManager.Suppress(false);
            builder.EndExternalChange();
            return results;
        }

        //an operation lasting several frames (loading, saving, generation...): no other command can run meanwhile
        async Task<T> RunLong<T>(System.Func<Task<T>> action) {
            if (busy > 0 || IsLongOperationRunning()) throw new ApiException("busy", "another operation is running");
            if (batchDepth > 0) throw ApiException.InvalidOperation("this command can't be part of a batch");
            busy++;
            try {
                return await action();
            } finally {
                busy--;
            }
        }

        // Settings of the session

        [ApiMethod("api.configure", "Session settings: history (record the changes as undo steps), autoFlush (update the city after every change)")]
        public object Configure(bool? history = null, bool? autoFlush = null) {
            if (history == false && History.UndoManager.IsGroupOpen) throw ApiException.InvalidOperation("the history can't be turned off while a transaction is open");
            if (history.HasValue) recordHistory = history.Value;
            if (autoFlush.HasValue) this.autoFlush = autoFlush.Value;
            return new { history = recordHistory, autoFlush = this.autoFlush };
        }

        [ApiMethod("api.version", "The version of the protocol and of the program")]
        public object GetVersion() {
            return new { protocol = ProtocolVersion, program = GlobalVariables.programVersion, feature = GlobalVariables.featureVersion };
        }

        // History

        void RequireNoTransaction(string what) {
            if (ScriptRunner.OwnsTransaction) throw ApiException.InvalidOperation(what + " is not possible while the changes of the script are a single undo step (\"transaction\": false in its script.json)");
            if (History.UndoManager.IsGroupOpen) throw ApiException.InvalidOperation(what + " is not possible while a transaction is open (history.commit or history.rollback first)");
        }

        //the transaction of a script run is opened and closed by the runner
        void RequireNotScriptTransaction() {
            if (ScriptRunner.OwnsTransaction) throw ApiException.InvalidOperation("the changes of the script are already a single undo step, closed when it ends (\"transaction\": false in its script.json to manage it)");
        }

        [ApiMethod("history.begin", "Opens a transaction: the changes until history.commit become a single undo step (each command still fails alone)")]
        public object BeginTransaction(string label = "script") {
            RequireCity();
            RequireNotScriptTransaction();
            if (busy > 0 || IsLongOperationRunning()) throw new ApiException("busy", "another operation is running");
            if (History.UndoManager.IsGroupOpen) throw ApiException.InvalidOperation("a transaction is already open");
            if (!recordHistory) throw ApiException.InvalidOperation("transactions need the history (api.configure with history: true)");
            Flush();
            History.UndoManager.OpenGroup(label);
            return HistoryState();
        }

        [ApiMethod("history.commit", "Closes the transaction: its changes become a single undo step")]
        public object CommitTransaction() {
            RequireCity();
            RequireNotScriptTransaction();
            if (!History.UndoManager.IsGroupOpen) throw ApiException.InvalidOperation("no transaction is open");
            if (busy > 0 || IsLongOperationRunning()) throw new ApiException("busy", "another operation is running");
            Flush();
            History.UndoManager.CloseGroup();
            return HistoryState();
        }

        [ApiMethod("history.rollback", "Closes the transaction undoing its changes")]
        public object RollbackTransaction() {
            RequireCity();
            RequireNotScriptTransaction();
            if (!History.UndoManager.IsGroupOpen) throw ApiException.InvalidOperation("no transaction is open");
            if (busy > 0 || IsLongOperationRunning()) throw new ApiException("busy", "another operation is running");
            builder.BeginExternalChange();
            Flush();
            History.UndoManager.DiscardGroup();
            M.FlagAsChanged(true);
            Flush();
            builder.EndExternalChange();
            return HistoryState();
        }

        [ApiMethod("history.undo", "Undoes the last change, like Edit > Undo")]
        public object Undo() {
            RequireCity();
            RequireNoTransaction("undo");
            if (!History.UndoManager.CanUndo) throw ApiException.InvalidOperation("nothing to undo");
            builder.PerformUndo();
            M.FlagAsChanged(true);
            if (autoFlush) Flush();
            return HistoryState();
        }

        [ApiMethod("history.redo", "Redoes the last undone change, like Edit > Redo")]
        public object Redo() {
            RequireCity();
            RequireNoTransaction("redo");
            if (!History.UndoManager.CanRedo) throw ApiException.InvalidOperation("nothing to redo");
            builder.PerformRedo();
            M.FlagAsChanged(true);
            if (autoFlush) Flush();
            return HistoryState();
        }

        [ApiMethod("history.state", "Whether there are changes to undo or redo, and the label of the open transaction (null if none)")]
        public object HistoryState() {
            return new { canUndo = History.UndoManager.CanUndo, canRedo = History.UndoManager.CanRedo, transaction = History.UndoManager.GroupLabel };
        }
    }
}

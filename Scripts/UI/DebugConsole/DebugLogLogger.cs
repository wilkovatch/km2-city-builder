using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Godot;

namespace IngameDebugConsole {
    // Receives every message printed
    // through the engine (GD.Print, GD.PrintErr, GD.PushWarning, GD.PushError, engine errors, unhandled
    // C# exceptions) and forwards it to the DebugLogManager. It is called from any thread.
    // Unhandled C# exceptions only reach the loggers when no debugger is attached, see OnFirstChanceException.
    public partial class DebugLogLogger : Logger {
        internal DebugLogManager manager;
        internal string projectPath; // globalized res://, used to shorten the stack trace file paths

        [System.ThreadStatic] private static bool isLogging;
        private static readonly System.Reflection.Assembly godotAssembly = typeof(GodotObject).Assembly;

        // Harmless engine messages raised by third party native code, not shown in the console (they still
        // reach the other loggers, e.g. stdout and the Godot editor)
        private static readonly System.Collections.Generic.HashSet<string> ignoredEngineMessages = new System.Collections.Generic.HashSet<string> {
            // Terrain3D binaries built against godot-cpp 4.4 call this no-op compatibility method (fixed upstream after v1.0.2)
            "instance_reset_physics_interpolation() is deprecated.",
        };

        // The Linux release export templates (built with GCC LTO) fail to match the engine's own method callables,
        // so every embedded popup (menus, tooltips, ...) reports connecting twice or disconnecting a missing
        // connection to its parent windows when it is shown or hidden. The popups work normally.
        // The callable name is empty only in release builds, so these errors are still shown in debug ones.
        private static bool IsPopupConnectionEngineBug(string function, string file, string message) {
            if (!System.OperatingSystem.IsLinux() || file == null || !file.EndsWith("core/object/object.cpp")) return false;
            if (function != "_disconnect" && function != "connect") return false;
            var signal = message.Contains("Signal: 'focus_entered'") || message.Contains("Signal 'focus_entered'")
                || message.Contains("Signal: 'tree_exited'") || message.Contains("Signal 'tree_exited'");
            var callable = message.Contains("callable: ''") || message.Contains("callable ''");
            return signal && callable && (message.StartsWith("Attempt to disconnect a nonexistent connection from ")
                || message.Contains(" is already connected to given callable "));
        }

        public override void _LogMessage(string message, bool error) {
            Forward(TrimMessage(message), null, error ? DebugLogType.Error : DebugLogType.Info);
        }

        public override void _LogError(string function, string file, int line, string code, string rationale,
            bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces) {
            var message = TrimMessage(string.IsNullOrEmpty(rationale) ? code : rationale);
            if (ignoredEngineMessages.Contains(message) || IsPopupConnectionEngineBug(function, file, message)) return;
            var type = errorType == (int)ErrorType.Warning ? DebugLogType.Warning : DebugLogType.Error;

            // Unhandled C# exceptions come with their stack trace in the message, while the current call stack
            // only has the engine callback: the first line is the log and the frames its stack trace
            int exceptionStackStart = message.IndexOf("\n   at ");
            if (exceptionStackStart >= 0) {
                Forward(message.Substring(0, exceptionStackStart), null, type, ExceptionStackTrace(message.Substring(exceptionStackStart + 1)));
                return;
            }

            // Errors raised from C# (GD.PushError/PushWarning) point to a C# file, which the managed stack
            // trace already covers; engine errors also get their native location.
            var nativeLocation = file != null && file.EndsWith(".cs") ? null : function + " (at " + file + ":" + line + ")";
            Forward(message, nativeLocation, type);
        }

        // While a debugger is attached (game started from the editor), Godot sends the C# exceptions escaping
        // to the engine straight to the debugger instead of pushing them as errors, so they never reach the
        // loggers. In that case they are caught here when thrown: an exception is logged if the frame that will
        // catch it is Godot's interop (the engine callback that reports it), not code that handles it.
        [System.ThreadStatic] private static bool inFirstChanceHandler;
        private static readonly ConcurrentDictionary<MethodBase, ExceptionHandlingClause[]> exceptionHandlers = new ConcurrentDictionary<MethodBase, ExceptionHandlingClause[]>();

        internal void OnFirstChanceException(object sender, FirstChanceExceptionEventArgs args) {
            // Exceptions thrown while logging or while inspecting the stack must not be processed again
            if (inFirstChanceHandler || isLogging || manager == null) return;
            inFirstChanceHandler = true;
            try {
                var exception = args.Exception;
                if (IsCaughtByGodot(new StackTrace(1, false).GetFrames(), exception)) {
                    Forward(exception.GetType().FullName + ": " + exception.Message, null, DebugLogType.Error, ThrownExceptionStackTrace(exception));
                }
            } catch {
            } finally {
                inFirstChanceHandler = false;
            }
        }

        private static bool IsCaughtByGodot(StackFrame[] frames, System.Exception exception) {
            foreach (var frame in frames) {
                var method = frame.GetMethod();
                if (method == null || method.DeclaringType == typeof(DebugLogLogger) || IsExceptionDispatchFrame(method.DeclaringType)) continue;
                var clauses = exceptionHandlers.GetOrAdd(method, GetExceptionHandlers);
                if (clauses.Length == 0) continue;
                int ilOffset = frame.GetILOffset();
                // Clauses are ordered from the innermost try block
                foreach (var clause in clauses) {
                    if (clause.Flags != ExceptionHandlingClauseOptions.Clause && clause.Flags != ExceptionHandlingClauseOptions.Filter) continue;
                    // Without the offset the frame is assumed to be inside the try block (i.e. handled)
                    if (ilOffset != StackFrame.OFFSET_UNKNOWN && (ilOffset < clause.TryOffset || ilOffset >= clause.TryOffset + clause.TryLength)) continue;
                    // A filter ("catch when") is assumed to accept the exception
                    if (clause.Flags == ExceptionHandlingClauseOptions.Clause && !clause.CatchType.IsInstanceOfType(exception)) continue;
                    return method.DeclaringType?.Assembly == godotAssembly;
                }
            }
            // Not caught at all: Godot reports it as an unhandled exception through GD.PushError as well
            return false;
        }

        // Runtime frames dispatching a thrown exception, above the frame that threw it
        private static bool IsExceptionDispatchFrame(System.Type type) {
            var name = type?.FullName;
            return name != null && (name.StartsWith("System.Runtime.EH") || name.StartsWith("System.Runtime.ExceptionServices.InternalCalls"));
        }

        private static ExceptionHandlingClause[] GetExceptionHandlers(MethodBase method) {
            try {
                var body = method.GetMethodBody();
                if (body == null) return System.Array.Empty<ExceptionHandlingClause>();
                var clauses = new ExceptionHandlingClause[body.ExceptionHandlingClauses.Count];
                body.ExceptionHandlingClauses.CopyTo(clauses, 0);
                return clauses;
            } catch {
                return System.Array.Empty<ExceptionHandlingClause>();
            }
        }

        private void Forward(string message, string nativeLocation, DebugLogType type, string stackTrace = null) {
            // Printing from a logger is not supported (it would recurse), and a failure here must never
            // produce another log either
            if (isLogging || manager == null) return;
            isLogging = true;
            try {
                stackTrace ??= CaptureStackTrace();
                if (nativeLocation != null) stackTrace = stackTrace.Length > 0 ? nativeLocation + "\n" + stackTrace : nativeLocation;
                manager.ReceivedLog(message, stackTrace, type);
            } catch {
            } finally {
                isLogging = false;
            }
        }

        private static string TrimMessage(string message) {
            return message == null ? "" : message.TrimEnd('\n', '\r');
        }

        // Frames of an exception's stack trace ("   at Type.Method(...) in file:line N"), without the Godot
        // interop frames and with paths relative to the project
        private string ExceptionStackTrace(string frames) {
            var sb = new StringBuilder();
            foreach (var rawLine in frames.Split('\n')) {
                var frame = rawLine.Trim();
                if (frame.Length == 0 || frame.Contains(".InvokeGodotClass") || frame.StartsWith("at Godot.")) continue;
                if (projectPath != null) {
                    int pathStart = frame.IndexOf(" in ");
                    if (pathStart >= 0) {
                        var path = frame.Substring(pathStart + 4).Replace('\\', '/');
                        if (path.StartsWith(projectPath, System.StringComparison.OrdinalIgnoreCase)) path = path.Substring(projectPath.Length);
                        frame = frame.Substring(0, pathStart + 4) + path;
                    }
                }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(frame);
            }
            return sb.ToString();
        }

        // The managed call stack is attached to every log; the frames of the logging machinery itself
        // (this logger, the Godot interop and GD, the GameEngine log wrappers) are left out.
        private string CaptureStackTrace() {
            var sb = new StringBuilder();
            AppendFrames(sb, new StackTrace(2, true).GetFrames(), true);
            return sb.ToString();
        }

        // Stack trace of an exception being thrown: the frames it already went through (where it was
        // originally thrown, when rethrown by ExceptionDispatchInfo, e.g. by an async method on the
        // synchronization context, or by "throw;"), followed by the current call stack from the throw point
        private string ThrownExceptionStackTrace(System.Exception exception) {
            var sb = new StringBuilder();
            var previousFrames = new StackTrace(exception, true).GetFrames();
            var lastPreviousMethod = AppendFrames(sb, previousFrames, false);
            AppendFrames(sb, new StackTrace(2, true).GetFrames(), true, lastPreviousMethod);
            return sb.ToString();
        }

        // Appends the frames and returns the method of the last one appended. With
        // skipLeading the frames of the logging machinery at the top of the stack are left out, and the first
        // frame after them is also left out if its method is skipFirstMethod (already appended).
        private MethodBase AppendFrames(StringBuilder sb, StackFrame[] frames, bool skipLeading, MethodBase skipFirstMethod = null) {
            MethodBase lastMethod = null;
            bool skippingLeadingFrames = skipLeading;
            foreach (var frame in frames) {
                var method = frame.GetMethod();
                if (method == null) continue;
                var type = method.DeclaringType;
                // GodotSharp (GD, the interop, the engine callbacks) and the generated method dispatchers
                if (type == null || type.Assembly == godotAssembly || method.Name.StartsWith("InvokeGodotClass")) continue;
                if (skippingLeadingFrames) {
                    if (type == typeof(DebugLogLogger) || IsExceptionDispatchFrame(type)
                        || (type == typeof(GameEngine) && (method.Name == nameof(GameEngine.Print) || method.Name == nameof(GameEngine.LogWarning) || method.Name == nameof(GameEngine.LogError)))) {
                        continue;
                    }
                    skippingLeadingFrames = false;
                    if (method == skipFirstMethod) continue;
                }

                lastMethod = method;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(type != null ? type.FullName : "?").Append(':').Append(method.Name).Append(" (");
                var parameters = method.GetParameters();
                for (int i = 0; i < parameters.Length; i++) {
                    if (i > 0) sb.Append(',');
                    sb.Append(parameters[i].ParameterType.Name);
                }
                sb.Append(')');

                var fileName = frame.GetFileName();
                if (!string.IsNullOrEmpty(fileName)) {
                    fileName = fileName.Replace('\\', '/');
                    if (projectPath != null && fileName.StartsWith(projectPath, System.StringComparison.OrdinalIgnoreCase)) {
                        fileName = fileName.Substring(projectPath.Length);
                    }
                    sb.Append(" (at ").Append(fileName).Append(':').Append(frame.GetFileLineNumber()).Append(')');
                }
            }
            return lastMethod;
        }
    }
}

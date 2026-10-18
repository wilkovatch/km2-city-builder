using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using Lua.Standard;

namespace RuntimeCalculator.Scripting {
    //An element that gives the scripts of its definitions a context (their second argument)
    public interface IScriptContextProvider {
        LuaValue GetScriptContext();
    }

    //Runs the Lua scripts of the core (its "lua" folder). The scripts are pure functions of the variables of an element:
    //no access to files, the operating system or the scene. Everything runs on the main thread.
    public class LuaScriptHost {
        const int hookInterval = 100000;
        const int maxHookTicks = 200; //a call is stopped after about 20 million instructions

        static LuaScriptHost instance;

        public readonly string folder;
        readonly LuaState state;
        readonly Dictionary<string, LuaFunction> functions = new Dictionary<string, LuaFunction>();
        readonly HashSet<string> reportedErrors = new HashSet<string>();
        public int ErrorCount => reportedErrors.Count;
        readonly LuaValue[] callArgs = new LuaValue[2];
        int hookTicks = 0;

        //The host of the current core, created when first needed
        public static LuaScriptHost Get() {
            var folder = Path.Combine(PathHelper.FilesPath(), "lua");
            if (instance == null || instance.folder != folder) instance = new LuaScriptHost(folder);
            return instance;
        }

        //A host for the scripts of another folder (for the tests)
        public static LuaScriptHost CreateForFolder(string folder) {
            return new LuaScriptHost(folder);
        }

        //The next types parsed use a new host (with the scripts read again), the ones already parsed keep theirs
        public static void Reset() {
            instance = null;
        }

        LuaScriptHost(string folder) {
            this.folder = folder;
            state = LuaState.Create();
            state.OpenBasicLibrary();
            state.OpenMathLibrary();
            state.OpenStringLibrary();
            state.OpenTableLibrary();
            state.OpenBitwiseLibrary();
            state.OpenModuleLibrary();
            state.ModuleLoader = new CoreModuleLoader(folder);

            var env = state.Environment;
            env["dofile"] = LuaValue.Nil;
            env["loadfile"] = LuaValue.Nil;
            env["collectgarbage"] = LuaValue.Nil;
            env["package"].Read<LuaTable>()["searchers"] = new LuaTable(); //only the core modules can be required
            var math = env["math"].Read<LuaTable>();
            math["random"] = LuaValue.Nil; //the results must not change between two evaluations, rnd(min, max, seed) is deterministic
            math["randomseed"] = LuaValue.Nil;
            env["print"] = new LuaFunction("print", (context, ct) => {
                var parts = new List<string>();
                for (int i = 0; i < context.ArgumentCount; i++) parts.Add(ToDisplayString(context.GetArgument(i)));
                GameEngine.Print("[lua] " + string.Join("\t", parts));
                return new ValueTask<int>(context.Return());
            });
            LuaVectors.Register(state);

            state.SetHook(new LuaFunction("budget", (context, ct) => {
                if (++hookTicks > maxHookTicks) throw new LuaRuntimeException(context.State, "the script is taking too long (infinite loop?)");
                return new ValueTask<int>(context.Return());
            }), "", hookInterval);
        }

        static string ToDisplayString(LuaValue value) {
            if (value.Type == LuaValueType.UserData) return value.Read<object>().ToString();
            return value.ToString();
        }

        //The function "<module>.<function>" of the lua folder (modules in subfolders: "<folder>.<module>.<function>")
        public LuaFunction Resolve(string reference) {
            if (functions.TryGetValue(reference, out var res)) return res;
            var dot = reference == null ? -1 : reference.LastIndexOf('.');
            if (dot <= 0 || dot == reference.Length - 1) {
                throw new Exception("invalid script reference '" + reference + "', it must be <module>.<function>");
            }
            var moduleName = reference.Substring(0, dot);
            var functionName = reference.Substring(dot + 1);
            LuaValue module;
            try {
                module = Run(state.Environment["require"].Read<LuaFunction>(), moduleName)[0];
            } catch (Exception e) {
                throw new Exception("Lua module '" + moduleName + "' (" + folder + "): " + e.Message);
            }
            if (!module.TryRead<LuaTable>(out var table)) {
                throw new Exception("Lua module '" + moduleName + "' must return a table of functions");
            }
            if (!table[functionName].TryRead<LuaFunction>(out res)) {
                throw new Exception("Lua module '" + moduleName + "' has no function '" + functionName + "'");
            }
            functions[reference] = res;
            return res;
        }

        LuaValue[] Run(LuaFunction function, params LuaValue[] args) {
            hookTicks = 0;
            var task = state.CallAsync(function, args, CancellationToken.None);
            //no asynchronous function is available to the scripts, so the call is always already completed
            return task.IsCompletedSuccessfully ? task.Result : task.AsTask().GetAwaiter().GetResult();
        }

        //Calls the function of a definition with the variables and the context of the element,
        //returns nil (after logging the error once per definition) if the script fails
        public LuaValue Call(LuaFunction function, LuaValue variables, LuaValue context, string definitionName) {
            try {
                callArgs[0] = variables;
                callArgs[1] = context;
                hookTicks = 0;
                var task = state.CallAsync(function, callArgs, CancellationToken.None);
                var results = task.IsCompletedSuccessfully ? task.Result : task.AsTask().GetAwaiter().GetResult();
                return results.Length > 0 ? results[0] : LuaValue.Nil;
            } catch (Exception e) {
                ReportError(definitionName, e.Message);
                return LuaValue.Nil;
            } finally {
                callArgs[0] = LuaValue.Nil;
                callArgs[1] = LuaValue.Nil;
            }
        }

        public void ReportError(string definitionName, string message) {
            if (reportedErrors.Add(definitionName + "\n" + message)) {
                GameEngine.LogError("Script of the definition '" + definitionName + "': " + message);
            }
        }

        //Loads the modules from the lua folder of the core: "a.b" is the file lua/a/b.lua
        class CoreModuleLoader : ILuaModuleLoader {
            static readonly Regex validName = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$");
            readonly string folder;

            public CoreModuleLoader(string folder) {
                this.folder = folder;
            }

            string GetPath(string moduleName) {
                if (moduleName == null || !validName.IsMatch(moduleName)) return null;
                return Path.Combine(folder, Path.Combine(moduleName.Split('.')) + ".lua");
            }

            public bool Exists(string moduleName) {
                var path = GetPath(moduleName);
                return path != null && File.Exists(path);
            }

            public ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default) {
                var path = GetPath(moduleName);
                if (path == null || !File.Exists(path)) throw new FileNotFoundException("module '" + moduleName + "' not found in " + folder);
                return new ValueTask<LuaModule>(new LuaModule("@" + Path.GetFileName(path), File.ReadAllText(path)));
            }
        }
    }
}

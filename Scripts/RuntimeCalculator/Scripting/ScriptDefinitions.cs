using System.Threading.Tasks;
using Godot;
using Lua;

namespace RuntimeCalculator.Scripting {
    //The read-only view of a variable container given to the scripts: v.name is a number, a boolean, a vec3 or a vec2
    public static class LuaVariableView {
        public static LuaValue Get(VariableContainer vc) {
            if (vc == null) return LuaValue.Nil;
            if (vc.scriptView is LuaTable cached) return cached;
            var meta = new LuaTable();
            meta["__index"] = new LuaFunction("variables", (context, ct) => {
                var name = context.GetArgument(1);
                if (!name.TryRead<string>(out var key) || !TryRead(vc, key, out var value)) {
                    throw new LuaRuntimeException(context.State, "unknown variable '" + name + "'");
                }
                return new ValueTask<int>(context.Return(value));
            });
            meta["__newindex"] = new LuaFunction("variables", (context, ct) => {
                throw new LuaRuntimeException(context.State, "the variables are read-only");
            });
            var view = new LuaTable { Metatable = meta };
            vc.scriptView = view;
            return view;
        }

        static bool TryRead(VariableContainer vc, string name, out LuaValue value) {
            if (vc.floatIndex.TryGetValue(name, out var i)) {
                var f = vc.floats[i];
                value = vc.boolNames.Contains(name) ? new LuaValue(f > 0) : new LuaValue(f);
                return true;
            }
            if (vc.vec3Index.TryGetValue(name, out i)) {
                value = LuaVectors.ToLua(vc.vector3s[i]);
                return true;
            }
            if (vc.vec2Index.TryGetValue(name, out i)) {
                value = LuaVectors.ToLua(vc.vector2s[i]);
                return true;
            }
            value = LuaValue.Nil;
            return false;
        }
    }

    //A definition computed by a function of a Lua script, called with the variables of the element and its context
    public class ScriptFunction {
        readonly LuaScriptHost host;
        readonly LuaFunction function;
        public readonly string definitionName;
        readonly string expectedType;

        public ScriptFunction(string reference, string definitionName, string expectedType, LuaScriptHost host = null) {
            this.host = host ?? LuaScriptHost.Get();
            function = this.host.Resolve(reference);
            this.definitionName = definitionName;
            this.expectedType = expectedType;
        }

        //the result, or nil if the script failed or returned a value of another type (the error is logged once)
        public LuaValue Call(VariableContainer vc) {
            var context = vc?.owner is IScriptContextProvider provider ? provider.GetScriptContext() : LuaValue.Nil;
            var res = host.Call(function, LuaVariableView.Get(vc), context, definitionName);
            if (res.Type == LuaValueType.Nil) return res;
            bool valid = expectedType switch {
                "number" => res.Type == LuaValueType.Number,
                "boolean" => res.Type == LuaValueType.Boolean,
                "vec3" => res.TryRead<LuaVec3>(out _),
                "vec2" => res.TryRead<LuaVec2>(out _),
                _ => false
            };
            if (!valid) {
                host.ReportError(definitionName, "returned a " + res.TypeToString() + " instead of a " + expectedType);
                return LuaValue.Nil;
            }
            return res;
        }
    }

    public class ScriptNumber : Numbers.Number {
        readonly ScriptFunction function;

        public ScriptNumber(string reference, string definitionName, LuaScriptHost host = null) {
            function = new ScriptFunction(reference, definitionName, "number", host);
        }

        public override float GetValue(VariableContainer vc) {
            var res = function.Call(vc);
            return res.Type == LuaValueType.Number ? (float)res.Read<double>() : 0f;
        }

        public override bool IsConstant() {
            return false;
        }
    }

    public class ScriptBoolean : Booleans.Boolean {
        readonly ScriptFunction function;

        public ScriptBoolean(string reference, string definitionName, LuaScriptHost host = null) {
            function = new ScriptFunction(reference, definitionName, "boolean", host);
        }

        public override bool GetValue(VariableContainer vc) {
            var res = function.Call(vc);
            return res.Type == LuaValueType.Boolean && res.Read<bool>();
        }

        public override void SetIndices(VariableContainer vc) { }
    }

    public class ScriptVector3 : Vector3s.Vector {
        readonly ScriptFunction function;

        public ScriptVector3(string reference, string definitionName, LuaScriptHost host = null) {
            function = new ScriptFunction(reference, definitionName, "vec3", host);
        }

        public override Vector3 GetValue(VariableContainer vc) {
            return function.Call(vc).TryRead<LuaVec3>(out var res) ? res.value : Vector3.Zero;
        }

        public override bool IsConstant() {
            return false;
        }
    }

    public class ScriptVector2 : Vector2s.Vector {
        readonly ScriptFunction function;

        public ScriptVector2(string reference, string definitionName, LuaScriptHost host = null) {
            function = new ScriptFunction(reference, definitionName, "vec2", host);
        }

        public override Vector2 GetValue(VariableContainer vc) {
            return function.Call(vc).TryRead<LuaVec2>(out var res) ? res.value : Vector2.Zero;
        }

        public override bool IsConstant() {
            return false;
        }
    }
}

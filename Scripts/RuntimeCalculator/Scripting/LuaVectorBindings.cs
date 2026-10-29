using System;
using System.Threading.Tasks;
using Lua;

namespace RuntimeCalculator.Scripting {
    //Lua-CSharp's generated bindings share one lazily populated metatable across all threads. Even a VM lookup can
    //insert a missing metamethod into that table, so vectors use one table per execution thread instead.
    static class LuaVectorBindings {
        [ThreadStatic] static LuaTable vector3;
        [ThreadStatic] static LuaTable vector2;
        internal static LuaTable Vector3 { get => vector3 ??= Create3(); set => vector3 = value; }
        internal static LuaTable Vector2 { get => vector2 ??= Create2(); set => vector2 = value; }

        static LuaFunction Function(string name, Func<LuaFunctionExecutionContext, LuaValue> call) {
            return new LuaFunction(name, (context, ct) => new ValueTask<int>(context.Return(call(context))));
        }

        static LuaTable Create3() {
            LuaVec3 A(LuaFunctionExecutionContext c) => c.GetArgument<LuaVec3>(0);
            LuaVec3 B(LuaFunctionExecutionContext c) => c.GetArgument<LuaVec3>(1);
            var methods = new LuaTable();
            methods["dot"] = Function("dot", c => A(c).Dot(B(c)));
            methods["cross"] = Function("cross", c => A(c).Cross(B(c)));
            methods["magnitude"] = Function("magnitude", c => A(c).Magnitude());
            methods["normalize"] = Function("normalize", c => A(c).Normalize());
            methods["distance"] = Function("distance", c => A(c).Distance(B(c)));
            methods["angle"] = Function("angle", c => A(c).Angle(B(c)));
            methods["signedAngle"] = Function("signedAngle", c => A(c).SignedAngle(B(c), c.GetArgument<LuaVec3>(2)));
            methods["scale"] = Function("scale", c => A(c).Scale(B(c)));
            methods["min"] = Function("min", c => A(c).Min(B(c)));
            methods["max"] = Function("max", c => A(c).Max(B(c)));
            methods["project"] = Function("project", c => A(c).Project(B(c)));
            methods["reflect"] = Function("reflect", c => A(c).Reflect(B(c)));
            methods["lerp"] = Function("lerp", c => A(c).Lerp(B(c), c.GetArgument<double>(2)));
            methods["rotate"] = Function("rotate", c => A(c).Rotate(B(c), c.GetArgument<double>(2)));
            var meta = new LuaTable();
            meta["__index"] = Function("index", c => {
                var key = c.GetArgument(1);
                if (!key.TryRead<string>(out var name)) return LuaValue.Nil;
                return name switch { "x" => A(c).X, "y" => A(c).Y, "z" => A(c).Z, _ => methods[name] };
            });
            meta["__newindex"] = ReadOnly();
            meta["__add"] = Function("add", c => LuaVec3.Add(A(c), B(c)));
            meta["__sub"] = Function("sub", c => LuaVec3.Sub(A(c), B(c)));
            meta["__mul"] = Function("mul", c => LuaVec3.Mul(c.GetArgument(0), c.GetArgument(1)));
            meta["__div"] = Function("div", c => LuaVec3.Div(c.GetArgument(0), c.GetArgument(1)));
            meta["__unm"] = Function("unm", c => LuaVec3.Unm(A(c)));
            meta["__eq"] = Function("eq", c => LuaVec3.Eq(A(c), B(c)));
            meta["__tostring"] = Function("tostring", c => A(c).ToString());
            return meta;
        }

        static LuaTable Create2() {
            LuaVec2 A(LuaFunctionExecutionContext c) => c.GetArgument<LuaVec2>(0);
            LuaVec2 B(LuaFunctionExecutionContext c) => c.GetArgument<LuaVec2>(1);
            var methods = new LuaTable();
            methods["dot"] = Function("dot", c => A(c).Dot(B(c)));
            methods["magnitude"] = Function("magnitude", c => A(c).Magnitude());
            methods["normalize"] = Function("normalize", c => A(c).Normalize());
            methods["distance"] = Function("distance", c => A(c).Distance(B(c)));
            methods["angle"] = Function("angle", c => A(c).Angle(B(c)));
            methods["signedAngle"] = Function("signedAngle", c => A(c).SignedAngle(B(c)));
            methods["scale"] = Function("scale", c => A(c).Scale(B(c)));
            methods["min"] = Function("min", c => A(c).Min(B(c)));
            methods["max"] = Function("max", c => A(c).Max(B(c)));
            methods["reflect"] = Function("reflect", c => A(c).Reflect(B(c)));
            methods["lerp"] = Function("lerp", c => A(c).Lerp(B(c), c.GetArgument<double>(2)));
            var meta = new LuaTable();
            meta["__index"] = Function("index", c => {
                var key = c.GetArgument(1);
                if (!key.TryRead<string>(out var name)) return LuaValue.Nil;
                return name switch { "x" => A(c).X, "y" => A(c).Y, _ => methods[name] };
            });
            meta["__newindex"] = ReadOnly();
            meta["__add"] = Function("add", c => LuaVec2.Add(A(c), B(c)));
            meta["__sub"] = Function("sub", c => LuaVec2.Sub(A(c), B(c)));
            meta["__mul"] = Function("mul", c => LuaVec2.Mul(c.GetArgument(0), c.GetArgument(1)));
            meta["__div"] = Function("div", c => LuaVec2.Div(c.GetArgument(0), c.GetArgument(1)));
            meta["__unm"] = Function("unm", c => LuaVec2.Unm(A(c)));
            meta["__eq"] = Function("eq", c => LuaVec2.Eq(A(c), B(c)));
            meta["__tostring"] = Function("tostring", c => A(c).ToString());
            return meta;
        }

        static LuaFunction ReadOnly() {
            return new LuaFunction("newindex", (context, ct) => throw new LuaRuntimeException(context.State, "vector members are read-only"));
        }
    }
}

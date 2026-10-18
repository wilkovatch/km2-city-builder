using System.Threading.Tasks;
using Godot;
using Lua;

namespace RuntimeCalculator.Scripting {
    //The vec3 and vec2 values of the scripts, with the same functions (and the same results) as the expressions
    public static class LuaVectors {
        public static void Register(LuaState state) {
            var env = state.Environment;
            env["vec3"] = new LuaFunction("vec3", (context, ct) => {
                var res = new LuaVec3(new Vector3(Float(context, 0), Float(context, 1), Float(context, 2)));
                return new ValueTask<int>(context.Return(res));
            });
            env["vec2"] = new LuaFunction("vec2", (context, ct) => {
                var res = new LuaVec2(new Vector2(Float(context, 0), Float(context, 1)));
                return new ValueTask<int>(context.Return(res));
            });
            env["rnd"] = new LuaFunction("rnd", (context, ct) => {
                var a = Float(context, 0);
                var b = Float(context, 1);
                var r = context.HasArgument(2) ? RandomManager.CalculatorDetRandom((int)Float(context, 2)) : RandomManager.rnd;
                return new ValueTask<int>(context.Return(a + (float)r.NextDouble() * (b - a)));
            });
        }

        static float Float(LuaFunctionExecutionContext context, int index) {
            return context.HasArgument(index) ? (float)context.GetArgument<double>(index) : 0f;
        }

        public static LuaValue ToLua(Vector3 v) {
            return new LuaVec3(v);
        }

        public static LuaValue ToLua(Vector2 v) {
            return new LuaVec2(v);
        }

        internal static (bool ok, Vector3 vec, float num) ReadVec3OrNumber(LuaValue value) {
            if (value.TryRead<LuaVec3>(out var v)) return (true, v.value, 0);
            if (value.TryRead<double>(out var d)) return (false, default, (float)d);
            throw new System.InvalidOperationException("vec3 operation with a " + value.TypeToString());
        }

        internal static (bool ok, Vector2 vec, float num) ReadVec2OrNumber(LuaValue value) {
            if (value.TryRead<LuaVec2>(out var v)) return (true, v.value, 0);
            if (value.TryRead<double>(out var d)) return (false, default, (float)d);
            throw new System.InvalidOperationException("vec2 operation with a " + value.TypeToString());
        }
    }

    [LuaObject("vec3")]
    public partial class LuaVec3 {
        public readonly Vector3 value;

        public LuaVec3(Vector3 value) {
            this.value = value;
        }

        [LuaMember("x")] public double X => value.X;
        [LuaMember("y")] public double Y => value.Y;
        [LuaMember("z")] public double Z => value.Z;

        [LuaMember("dot")] public double Dot(LuaVec3 b) => GameEngine.Vector3.Dot(value, b.value);
        [LuaMember("cross")] public LuaVec3 Cross(LuaVec3 b) => new LuaVec3(GameEngine.Vector3.Cross(value, b.value));
        [LuaMember("magnitude")] public double Magnitude() => GameEngine.Vector3.Magnitude(value);
        [LuaMember("normalize")] public LuaVec3 Normalize() => new LuaVec3(GameEngine.Vector3.Normalize(value));
        [LuaMember("distance")] public double Distance(LuaVec3 b) => GameEngine.Vector3.Distance(value, b.value);
        [LuaMember("angle")] public double Angle(LuaVec3 b) => GameEngine.Vector3.Angle(value, b.value);
        [LuaMember("signedAngle")] public double SignedAngle(LuaVec3 b, LuaVec3 axis) => GameEngine.Vector3.SignedAngle(value, b.value, axis.value);
        [LuaMember("scale")] public LuaVec3 Scale(LuaVec3 b) => new LuaVec3(GameEngine.Vector3.Scale(value, b.value));
        [LuaMember("min")] public LuaVec3 Min(LuaVec3 b) => new LuaVec3(GameEngine.Vector3.Min(value, b.value));
        [LuaMember("max")] public LuaVec3 Max(LuaVec3 b) => new LuaVec3(GameEngine.Vector3.Max(value, b.value));
        [LuaMember("project")] public LuaVec3 Project(LuaVec3 b) => new LuaVec3(GameEngine.Vector3.Project(value, b.value));
        [LuaMember("reflect")] public LuaVec3 Reflect(LuaVec3 b) => new LuaVec3(GameEngine.Vector3.Reflect(value, b.value));
        [LuaMember("lerp")] public LuaVec3 Lerp(LuaVec3 b, double t) => new LuaVec3(GameEngine.Vector3.Lerp(value, b.value, (float)t));
        //rotation of the given angle (in degrees) around the axis, like rotate(v, axis, angle) in the expressions
        [LuaMember("rotate")] public LuaVec3 Rotate(LuaVec3 axis, double angle) => new LuaVec3(GameEngine.Quaternion.AngleAxis((float)angle, axis.value) * value);

        [LuaMetamethod(LuaObjectMetamethod.Add)]
        public static LuaVec3 Add(LuaVec3 a, LuaVec3 b) => new LuaVec3(a.value + b.value);

        [LuaMetamethod(LuaObjectMetamethod.Sub)]
        public static LuaVec3 Sub(LuaVec3 a, LuaVec3 b) => new LuaVec3(a.value - b.value);

        [LuaMetamethod(LuaObjectMetamethod.Mul)]
        public static LuaVec3 Mul(LuaValue a, LuaValue b) {
            var x = LuaVectors.ReadVec3OrNumber(a);
            var y = LuaVectors.ReadVec3OrNumber(b);
            if (x.ok && !y.ok) return new LuaVec3(x.vec * y.num);
            if (!x.ok && y.ok) return new LuaVec3(y.vec * x.num);
            throw new System.InvalidOperationException("a vec3 can only be multiplied by a number (use dot, cross or scale)");
        }

        [LuaMetamethod(LuaObjectMetamethod.Div)]
        public static LuaVec3 Div(LuaValue a, LuaValue b) {
            var x = LuaVectors.ReadVec3OrNumber(a);
            var y = LuaVectors.ReadVec3OrNumber(b);
            if (x.ok && !y.ok) return new LuaVec3(x.vec / y.num);
            throw new System.InvalidOperationException("a vec3 can only be divided by a number");
        }

        [LuaMetamethod(LuaObjectMetamethod.Unm)]
        public static LuaVec3 Unm(LuaVec3 a) => new LuaVec3(-a.value);

        [LuaMetamethod(LuaObjectMetamethod.Eq)]
        public static bool Eq(LuaVec3 a, LuaVec3 b) => a.value == b.value;

        [LuaMetamethod(LuaObjectMetamethod.ToString)]
        public override string ToString() => "vec3(" + value.X + ", " + value.Y + ", " + value.Z + ")";
    }

    [LuaObject("vec2")]
    public partial class LuaVec2 {
        public readonly Vector2 value;

        public LuaVec2(Vector2 value) {
            this.value = value;
        }

        [LuaMember("x")] public double X => value.X;
        [LuaMember("y")] public double Y => value.Y;

        [LuaMember("dot")] public double Dot(LuaVec2 b) => GameEngine.Vector2.Dot(value, b.value);
        [LuaMember("magnitude")] public double Magnitude() => GameEngine.Vector2.Magnitude(value);
        [LuaMember("normalize")] public LuaVec2 Normalize() => new LuaVec2(value.Normalized());
        [LuaMember("distance")] public double Distance(LuaVec2 b) => GameEngine.Vector2.Distance(value, b.value);
        [LuaMember("angle")] public double Angle(LuaVec2 b) => GameEngine.Vector2.Angle(value, b.value);
        [LuaMember("signedAngle")] public double SignedAngle(LuaVec2 b) => GameEngine.Vector2.SignedAngle(value, b.value);
        [LuaMember("scale")] public LuaVec2 Scale(LuaVec2 b) => new LuaVec2(GameEngine.Vector2.Scale(value, b.value));
        [LuaMember("min")] public LuaVec2 Min(LuaVec2 b) => new LuaVec2(GameEngine.Vector2.Min(value, b.value));
        [LuaMember("max")] public LuaVec2 Max(LuaVec2 b) => new LuaVec2(GameEngine.Vector2.Max(value, b.value));
        [LuaMember("reflect")] public LuaVec2 Reflect(LuaVec2 b) => new LuaVec2(GameEngine.Vector2.Reflect(value, b.value));
        [LuaMember("lerp")] public LuaVec2 Lerp(LuaVec2 b, double t) => new LuaVec2(GameEngine.Vector2.Lerp(value, b.value, (float)t));

        [LuaMetamethod(LuaObjectMetamethod.Add)]
        public static LuaVec2 Add(LuaVec2 a, LuaVec2 b) => new LuaVec2(a.value + b.value);

        [LuaMetamethod(LuaObjectMetamethod.Sub)]
        public static LuaVec2 Sub(LuaVec2 a, LuaVec2 b) => new LuaVec2(a.value - b.value);

        [LuaMetamethod(LuaObjectMetamethod.Mul)]
        public static LuaVec2 Mul(LuaValue a, LuaValue b) {
            var x = LuaVectors.ReadVec2OrNumber(a);
            var y = LuaVectors.ReadVec2OrNumber(b);
            if (x.ok && !y.ok) return new LuaVec2(x.vec * y.num);
            if (!x.ok && y.ok) return new LuaVec2(y.vec * x.num);
            throw new System.InvalidOperationException("a vec2 can only be multiplied by a number (use dot or scale)");
        }

        [LuaMetamethod(LuaObjectMetamethod.Div)]
        public static LuaVec2 Div(LuaValue a, LuaValue b) {
            var x = LuaVectors.ReadVec2OrNumber(a);
            var y = LuaVectors.ReadVec2OrNumber(b);
            if (x.ok && !y.ok) return new LuaVec2(x.vec / y.num);
            throw new System.InvalidOperationException("a vec2 can only be divided by a number");
        }

        [LuaMetamethod(LuaObjectMetamethod.Unm)]
        public static LuaVec2 Unm(LuaVec2 a) => new LuaVec2(-a.value);

        [LuaMetamethod(LuaObjectMetamethod.Eq)]
        public static bool Eq(LuaVec2 a, LuaVec2 b) => a.value == b.value;

        [LuaMetamethod(LuaObjectMetamethod.ToString)]
        public override string ToString() => "vec2(" + value.X + ", " + value.Y + ")";
    }
}

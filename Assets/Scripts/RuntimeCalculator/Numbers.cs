using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeCalculator.Numbers {
    public abstract class Number: GenericCalculator<float> {
        public abstract float GetValue(VariableContainer vc);
        public abstract bool IsConstant();
        public Number GetSimplified() {
            if (IsConstant()) {
                return new Constant(GetValue(null));
            } else {
                return this;
            }
        }
        public virtual void SetIndices(VariableContainer vc) { }
    }

    class RegexHelper {
        public static string ListRegex(string[] list) {
            var res = "(";
            for (int i = 0; i < list.Length; i++) {
                res += list[i];
                if (i < list.Length - 1) res += "|";
            }
            res += ")";
            return res;
        }
    }

    //Values and variables
    public class Constant : Number {
        public float value;

        public static string Regex() {
            string[] list = {
                "pi", "deg2rad"
            };
            return RegexHelper.ListRegex(list);
        }

        public Constant(string name) {
            switch (name) {
                case "pi":
                    value = Mathf.PI;
                    break;
                case "deg2rad":
                    value = Mathf.Deg2Rad;
                    break;
            }
        }

        public Constant(float value) {
            this.value = value;
        }

        public override float GetValue(VariableContainer vc) {
            return value;
        }

        public override bool IsConstant() {
            return true;
        }
    }

    public class Variable : Number {
        public string name;
        public int index;

        public Variable(string name) {
            this.name = name;
        }

        public override float GetValue(VariableContainer vc) {
            return vc.floats[index];
        }

        public override bool IsConstant() {
            return false;
        }

        public override void SetIndices(VariableContainer vc) {
            if (!vc.floatIndex.ContainsKey(name)) throw new System.Exception("Invalid float parameter: " + name);
            index = vc.floatIndex[name];
        }
    }

    //Functions
    public class Function : Number {
        public string function;
        public Number[] param;
        Func<VariableContainer, float> func;

        public static string Regex() {
            string[] list = {
                "sin", "cos", "tan", "min", "max", "sign", "abs", "clamp", "ceil", "floor", "round", "sqrt", "lerp", "rnd"
            };
            return RegexHelper.ListRegex(list);
        }

        public Function(string function, Number[] param) {
            this.function = function;
            this.param = new Number[param.Length];
            for (int i = 0; i < param.Length; i++) {
                this.param[i] = param[i].GetSimplified();
            }
            switch (function) {
                case "sin":
                    func = x => { return Mathf.Sin(this.param[0].GetValue(x)); };
                    break;
                case "cos":
                    func = x => { return Mathf.Cos(this.param[0].GetValue(x)); };
                    break;
                case "tan":
                    func = x => { return Mathf.Tan(this.param[0].GetValue(x)); };
                    break;
                case "min":
                    func = x => { return Mathf.Min(param[0].GetValue(x), param[1].GetValue(x)); };
                    break;
                case "max":
                    func = x => { return Mathf.Max(param[0].GetValue(x), param[1].GetValue(x)); };
                    break;
                case "sign":
                    func = x => { return Mathf.Sign(this.param[0].GetValue(x)); };
                    break;
                case "abs":
                    func = x => { return Mathf.Abs(this.param[0].GetValue(x)); };
                    break;
                case "clamp":
                    func = x => { return Mathf.Clamp(this.param[0].GetValue(x), this.param[1].GetValue(x), this.param[2].GetValue(x)); };
                    break;
                case "ceil":
                    func = x => { return Mathf.Ceil(this.param[0].GetValue(x)); };
                    break;
                case "floor":
                    func = x => { return Mathf.Floor(this.param[0].GetValue(x)); };
                    break;
                case "round":
                    func = x => { return Mathf.Round(this.param[0].GetValue(x)); };
                    break;
                case "sqrt":
                    func = x => { return Mathf.Sqrt(this.param[0].GetValue(x)); };
                    break;
                case "lerp":
                    func = x => { return Mathf.Lerp(this.param[0].GetValue(x), this.param[1].GetValue(x), this.param[2].GetValue(x)); };
                    break;
                case "rnd":
                    if (this.param.Length >= 3) {
                        func = x => {
                            var a = this.param[0].GetValue(x);
                            var b = this.param[1].GetValue(x);
                            var c = this.param[2].GetValue(x);
                            return a + (float)RandomManager.CalculatorDetRandom((int)c).NextDouble() * (b - a);
                        };
                    } else {
                        func = x => {
                            var a = this.param[0].GetValue(x);
                            var b = this.param[1].GetValue(x);
                            return a + (float)RandomManager.rnd.NextDouble() * (b - a);
                        };
                    }
                    break;
                default:
                    throw new Exception("unsupported function: " + function);
            }
        }

        public override float GetValue(VariableContainer vc) {
            return func(vc);
        }

        public override bool IsConstant() {
            foreach (var p in param) {
                if (!p.IsConstant()) return false;
            }
            return true;
        }

        public override void SetIndices(VariableContainer vc) {
            foreach (var p in param) {
                p.SetIndices(vc);
            }
        }
    }

    public class Vec3Function : Number {
        public string function;
        public Vector3s.Vector[] param;
        Func<VariableContainer, float> func;

        public static string Regex() {
            string[] list = {
                "dot", "angle", "signedAngle", "magnitude", "distance", "v3x", "v3y", "v3z"
            };
            return RegexHelper.ListRegex(list);
        }

        public Vec3Function(string function, Vector3s.Vector[] param) {
            this.function = function;
            this.param = new Vector3s.Vector[param.Length];
            for (int i = 0; i < param.Length; i++) {
                this.param[i] = param[i].GetSimplified();
            }
            switch (function) {
                case "dot":
                    func = x => { return Vector3.Dot(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                case "angle":
                    func = x => { return Vector3.Angle(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                case "signedAngle":
                    func = x => { return Vector3.SignedAngle(this.param[0].GetValue(x), this.param[1].GetValue(x), this.param[2].GetValue(x)); };
                    break;
                case "magnitude":
                    func = x => { return Vector3.Magnitude(this.param[0].GetValue(x)); };
                    break;
                case "distance":
                    func = x => { return Vector3.Distance(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                case "v3x":
                    func = x => { return this.param[0].GetValue(x).x; };
                    break;
                case "v3y":
                    func = x => { return this.param[0].GetValue(x).y; };
                    break;
                case "v3z":
                    func = x => { return this.param[0].GetValue(x).z; };
                    break;
                default:
                    throw new Exception("unsupported function: " + function);
            }
        }

        public override float GetValue(VariableContainer vc) {
            return func(vc);
        }

        public override bool IsConstant() {
            foreach (var p in param) {
                if (!p.IsConstant()) return false;
            }
            return true;
        }

        public override void SetIndices(VariableContainer vc) {
            foreach (var p in param) {
                p.SetIndices(vc);
            }
        }
    }

    public class Vec2Function : Number {
        public string function;
        public Vector2s.Vector[] param;
        Func<VariableContainer, float> func;

        public static string Regex() {
            string[] list = {
                "dot2", "angle2", "signedAngle2", "distance2", "v2x", "v2y"
            };
            return RegexHelper.ListRegex(list);
        }

        public Vec2Function(string function, Vector2s.Vector[] param) {
            this.function = function;
            this.param = new Vector2s.Vector[param.Length];
            for (int i = 0; i < param.Length; i++) {
                this.param[i] = param[i].GetSimplified();
            }
            switch (function) {
                case "dot2":
                    func = x => { return Vector2.Dot(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                case "angle2":
                    func = x => { return Vector2.Angle(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                case "signedAngle2":
                    func = x => { return Vector2.SignedAngle(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                case "distance2":
                    func = x => { return Vector2.Distance(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                case "v2x":
                    func = x => { return this.param[0].GetValue(x).x; };
                    break;
                case "v2y":
                    func = x => { return this.param[0].GetValue(x).y; };
                    break;
                default:
                    throw new Exception("unsupported function: " + function);
            }
        }

        public override float GetValue(VariableContainer vc) {
            return func(vc);
        }

        public override bool IsConstant() {
            foreach (var p in param) {
                if (!p.IsConstant()) return false;
            }
            return true;
        }

        public override void SetIndices(VariableContainer vc) {
            foreach (var p in param) {
                p.SetIndices(vc);
            }
        }
    }

    public class IfFunction : Number {
        public Booleans.Boolean condition;
        public Number val1, val2;

        public IfFunction(Booleans.Boolean expr, Number[] vals) {
            condition = expr;
            val1 = vals[0].GetSimplified();
            val2 = vals[1].GetSimplified();
        }

        public override float GetValue(VariableContainer vc) {
            return condition.GetValue(vc) ? val1.GetValue(vc) : val2.GetValue(vc);
        }

        public override bool IsConstant() {
            return false;
        }

        public override void SetIndices(VariableContainer vc) {
            condition.SetIndices(vc);
            val1.SetIndices(vc);
            val2.SetIndices(vc);
        }
    }

    //Arithmetic operations
    public abstract class Operation : Number {
        public Number val1;
        public Number val2;

        public Operation(Number val1, Number val2) {
            this.val1 = val1.GetSimplified();
            this.val2 = val2.GetSimplified();
        }

        public override bool IsConstant() {
            return val1.IsConstant() && val2.IsConstant();
        }

        public override void SetIndices(VariableContainer vc) {
            val1.SetIndices(vc);
            val2.SetIndices(vc);
        }
    }

    public class Division : Operation {
        public Division(Number val1, Number val2) : base(val1, val2) { }

        public override float GetValue(VariableContainer vc) {
            return val1.GetValue(vc) / val2.GetValue(vc);
        }
    }

    public class Multiplication : Operation {
        public Multiplication(Number val1, Number val2) : base(val1, val2) { }

        public override float GetValue(VariableContainer vc) {
            return val1.GetValue(vc) * val2.GetValue(vc);
        }
    }

    public class Sum : Operation {
        public Sum(Number val1, Number val2) : base(val1, val2) { }

        public override float GetValue( VariableContainer vc) {
            return val1.GetValue(vc) + val2.GetValue(vc);
        }
    }

    public class Subtraction : Operation {
        public Subtraction(Number val1, Number val2) : base(val1, val2) { }

        public override float GetValue(VariableContainer vc) {
            return val1.GetValue(vc) - val2.GetValue(vc);
        }
    }

    public class Modulo : Operation {
        public Modulo(Number val1, Number val2) : base(val1, val2) { }

        public override float GetValue(VariableContainer vc) {
            return val1.GetValue(vc) % val2.GetValue(vc);
        }
    }

    public class Power : Operation {
        public Power(Number val1, Number val2) : base(val1, val2) { }

        public override float GetValue(VariableContainer vc) {
            return Mathf.Pow(val1.GetValue(vc), val2.GetValue(vc));
        }
    }
}

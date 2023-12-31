using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeCalculator.Vector2s {
    public abstract class Vector : GenericCalculator<Vector2> {
        public abstract Vector2 GetValue(VariableContainer vc);
        public abstract bool IsConstant();
        public Vector GetSimplified() {
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
    public class Constant : Vector {
        public Vector2 value;

        public Constant(Vector2 value) {
            this.value = value;
        }

        public override Vector2 GetValue(VariableContainer vc) {
            return value;
        }

        public override bool IsConstant() {
            return true;
        }
    }

    public class Compound : Vector {
        public Numbers.Number x, y;

        public Compound(Numbers.Number x, Numbers.Number y) {
            this.x = x.GetSimplified();
            this.y = y.GetSimplified();
        }

        public override bool IsConstant() {
            return x.IsConstant() && y.IsConstant();
        }

        public override Vector2 GetValue(VariableContainer vc) {
            var xV = x.GetValue(vc);
            var yV = y.GetValue(vc);
            return new Vector3(xV, yV);
        }

        public override void SetIndices(VariableContainer vc) {
            x.SetIndices(vc);
            y.SetIndices(vc);
        }
    }

    public class Variable : Vector {
        public string name;
        public int index;

        public Variable(string name) {
            this.name = name;
        }

        public override Vector2 GetValue(VariableContainer vc) {
            return vc.vector2s[index];
        }

        public override bool IsConstant() {
            return false;
        }

        public override void SetIndices(VariableContainer vc) {
            if (!vc.vec2Index.ContainsKey(name)) throw new System.Exception("Invalid vec2 parameter: " + name);
            index = vc.vec2Index[name];
        }
    }

    public class Function : Vector {
        public string function;
        public Vector[] param;
        Func<VariableContainer, Vector3> func;

        public static string Regex() {
            string[] list = {
                "scale", "min", "max", "reflect"
            };
            return RegexHelper.ListRegex(list);
        }

        public Function(string function, Vector[] param) {
            this.function = function;
            this.param = new Vector[param.Length];
            for (int i = 0; i < param.Length; i++) {
                this.param[i] = param[i].GetSimplified();
            }
            switch (function) {
                case "scale":
                    func = x => { return Vector2.Scale(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                case "min":
                    func = x => { return Vector2.Min(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                case "max":
                    func = x => { return Vector2.Max(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                case "reflect":
                    func = x => { return Vector2.Reflect(this.param[0].GetValue(x), this.param[1].GetValue(x)); };
                    break;
                default:
                    throw new Exception("unsupported function: " + function);
            }
        }

        public override Vector2 GetValue(VariableContainer vc) {
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

    public class IfFunction : Vector {
        public Booleans.Boolean condition;
        public Vector val1, val2;

        public IfFunction(Booleans.Boolean expr, Vector[] vals) {
            condition = expr;
            val1 = vals[0].GetSimplified();
            val2 = vals[1].GetSimplified();
        }

        public override Vector2 GetValue(VariableContainer vc) {
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

    public class Lerp : Vector {
        public Numbers.Number a;
        public Vector val1, val2;

        public Lerp(Vector val1, Vector val2, Numbers.Number a) {
            this.a = a.GetSimplified();
            this.val1 = val1.GetSimplified();
            this.val2 = val2.GetSimplified();
        }

        public override Vector2 GetValue(VariableContainer vc) {
            return Vector2.Lerp(val1.GetValue(vc), val2.GetValue(vc), a.GetValue(vc));
        }

        public override bool IsConstant() {
            return val1.IsConstant() && val2.IsConstant() && a.IsConstant();
        }

        public override void SetIndices(VariableContainer vc) {
            a.SetIndices(vc);
            val1.SetIndices(vc);
            val2.SetIndices(vc);
        }
    }

    public class Rotate : Vector {
        public Numbers.Number a;
        public Vector val;

        public Rotate(Vector val, Numbers.Number a) {
            this.a = a.GetSimplified();
            this.val = val.GetSimplified();
        }

        Vector2 RotateVector(Vector2 v, float delta) {
            return new Vector2(
                v.x * Mathf.Cos(delta) - v.y * Mathf.Sin(delta),
                v.x * Mathf.Sin(delta) + v.y * Mathf.Cos(delta)
            );
        }

        public override Vector2 GetValue(VariableContainer vc) {
            return RotateVector(val.GetValue(vc), a.GetValue(vc));
        }

        public override bool IsConstant() {
            return val.IsConstant() && a.IsConstant();
        }

        public override void SetIndices(VariableContainer vc) {
            a.SetIndices(vc);
            val.SetIndices(vc);
        }
    }

    //Arithmetic operations
    public abstract class VecOperation : Vector {
        public Vector val1;
        public Vector val2;

        public VecOperation(Vector val1, Vector val2) {
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

    //Arithmetic operations
    public abstract class FloatOperation : Vector {
        public Vector val1;
        public Numbers.Number val2;

        public FloatOperation(Vector val1, Numbers.Number val2) {
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

    public class Division : FloatOperation {
        public Division(Vector val1, Numbers.Number val2) : base(val1, val2) { }

        public override Vector2 GetValue(VariableContainer vc) {
            return val1.GetValue(vc) / val2.GetValue(vc);
        }
    }

    public class Multiplication : FloatOperation {
        public Multiplication(Vector val1, Numbers.Number val2) : base(val1, val2) { }

        public override Vector2 GetValue(VariableContainer vc) {
            return val1.GetValue(vc) * val2.GetValue(vc);
        }
    }

    public class Sum : VecOperation {
        public Sum(Vector val1, Vector val2) : base(val1, val2) { }

        public override Vector2 GetValue(VariableContainer vc) {
            return val1.GetValue(vc) + val2.GetValue(vc);
        }
    }

    public class Subtraction : VecOperation {
        public Subtraction(Vector val1, Vector val2) : base(val1, val2) { }

        public override Vector2 GetValue(VariableContainer vc) {
            return val1.GetValue(vc) - val2.GetValue(vc);
        }
    }
}

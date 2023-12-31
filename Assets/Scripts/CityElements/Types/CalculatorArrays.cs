using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types {
    public abstract class CalculatorArray<T, R> where T: RC.GenericCalculator<R> {
        protected RC.Booleans.Boolean condition = null;
        protected RC.Numbers.Number loopStart = null, loopEnd = null, loopIncrement = null;
        protected T[] elements;
        protected CalculatorArray<T, R>[] subArrays;
        protected string loopIndex = null;

        protected abstract T GetCalculator(object input);

        protected abstract CalculatorArray<T, R> GetSub(object input);

        public CalculatorArray(object input) {
            input = States.Utils.DeJsonify(input);
            if (input is object[] arr) {
                if (arr.Length > 0) {
                    if (arr[0] is Dictionary<string, object> d) {
                        if (d.ContainsKey("if") && d.ContainsKey("append")) {
                            subArrays = new CalculatorArray<T, R>[arr.Length];
                            for (int i = 0; i < arr.Length; i++) {
                                var dI = (Dictionary<string, object>)arr[i];
                                subArrays[i] = GetSub(dI["append"]);
                                subArrays[i].condition = RC.Parsers.BooleanParser.ParseExpression((string)dI["if"]);
                            }
                        } else {
                            throw new System.Exception("invalid object");
                        }
                    } else if (arr[0] is string || arr[0] is int || arr[0] is float || arr[0] is long || arr[0] is double) {
                        elements = new T[arr.Length];
                        for (int i = 0; i < arr.Length; i++) {
                            elements[i] = GetCalculator(arr[i]);
                        }
                    } else {
                        throw new System.Exception("invalid array");
                    }
                } else {
                    elements = new T[0];
                }
            } else if (input is Dictionary<string, object> dict) {
                if (dict.ContainsKey("if")) {
                    condition = RC.Parsers.BooleanParser.ParseExpression((string)dict["if"]);
                }
                var baseExpression = (string)dict["value"];
                var loopInfo = ((string)dict["for"]).Split(';');
                loopIndex = loopInfo[0].Trim();
                loopStart = RC.Parsers.FloatParser.ParseExpression(loopInfo[1].Trim());
                loopEnd = RC.Parsers.FloatParser.ParseExpression(loopInfo[2].Trim());
                loopIncrement = RC.Parsers.FloatParser.ParseExpression(loopInfo.Length == 4 ? loopInfo[3].Trim() : "1");
                elements = new T[1] { GetCalculator(baseExpression) };
            } else {
                throw new System.Exception("invalid array");
            }
        }

        protected T[] GetRealArray(RC.VariableContainer vc) {
            if (condition == null || condition.GetValue(vc)) {
                if (subArrays != null) {
                    var lst = new List<T>();
                    foreach (var a in subArrays) {
                        lst.AddRange(a.GetRealArray(vc));
                    }
                    return lst.ToArray();
                } else {
                    return elements;
                }
            } else {
                return new T[0];
            }
        }

        public R[] GetValues(RC.VariableContainer vc) {
            if (loopIndex != null) {
                var start = (int)loopStart.GetValue(vc);
                var end = (int)loopEnd.GetValue(vc);
                var increment = (int)loopIncrement.GetValue(vc);
                var res = new List<R>();
                if (start < end) {
                    for (int i = start; i < end; i += increment) {
                        vc.SetFloat(loopIndex, i);
                        if (condition == null || condition.GetValue(vc)) res.Add(elements[0].GetValue(vc));
                    }
                } else {
                    for (int i = start; i > end; i += increment) {
                        vc.SetFloat(loopIndex, i);
                        if (condition == null || condition.GetValue(vc)) res.Add(elements[0].GetValue(vc));
                    }
                }
                return res.ToArray();
            } else {
                var arr = GetRealArray(vc);
                var res = new R[arr.Length];
                for (int i = 0; i < arr.Length; i++) {
                    res[i] = arr[i].GetValue(vc);
                }
                return res;
            }
        }

        public void SetIndices(RC.VariableContainer vc) {
            if (condition != null) condition.SetIndices(vc);
            if (subArrays != null) {
                foreach (var e in subArrays) {
                    e.condition.SetIndices(vc);
                    foreach (var ee in e.elements) {
                        ee.SetIndices(vc);
                    }
                }
            } else {
                if (loopStart != null) loopStart.SetIndices(vc);
                if (loopEnd != null) loopEnd.SetIndices(vc);
                if (loopIncrement != null) loopIncrement.SetIndices(vc);
                foreach (var e in elements) {
                    e.SetIndices(vc);
                }
            }
        }
    }

    public class NumberArray : CalculatorArray<RC.Numbers.Number, float> {
        protected override RC.Numbers.Number GetCalculator(object input) {
            if (input is float val) {
                return new RC.Numbers.Constant(val);
            } else if (input is double valD) {
                return new RC.Numbers.Constant((float)valD);
            } else if (input is int valI) {
                return new RC.Numbers.Constant(valI);
            } else if (input is long valL) {
                return new RC.Numbers.Constant(valL);
            } else if (input is string str) {
                return RC.Parsers.FloatParser.ParseExpression(str);
            } else {
                throw new System.Exception("invalid number");
            }
        }

        protected override CalculatorArray<RC.Numbers.Number, float> GetSub(object input) {
            return new NumberArray(input);
        }

        public NumberArray(object input) : base(input) { }
    }

    public class Vector3Array : CalculatorArray<RC.Vector3s.Vector, Vector3> {
        protected override RC.Vector3s.Vector GetCalculator(object input) {
            if (input is string str) {
                return RC.Parsers.Vector3Parser.ParseExpression(str);
            } else {
                throw new System.Exception("invalid vector3");
            }
        }

        protected override CalculatorArray<RC.Vector3s.Vector, Vector3> GetSub(object input) {
            return new Vector3Array(input);
        }

        public Vector3Array(object input) : base(input) { }
    }

    public class Vector2Array : CalculatorArray<RC.Vector2s.Vector, Vector2> {
        protected override RC.Vector2s.Vector GetCalculator(object input) {
            if (input is string str) {
                return RC.Parsers.Vector2Parser.ParseExpression(str);
            } else {
                throw new System.Exception("invalid vector2");
            }
        }

        protected override CalculatorArray<RC.Vector2s.Vector, Vector2> GetSub(object input) {
            return new Vector2Array(input);
        }

        public Vector2Array(object input) : base(input) { }
    }

}

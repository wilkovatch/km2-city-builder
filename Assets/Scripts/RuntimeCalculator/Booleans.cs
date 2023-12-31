using System.Collections.Generic;
using RuntimeCalculator.Numbers;
using UnityEngine;

namespace RuntimeCalculator.Booleans {
    public abstract class Boolean {
        public abstract bool GetValue(VariableContainer vc);
        public abstract void SetIndices(VariableContainer vc);
    }

    public class BooleanVariable : Boolean {
        public string name;
        public int index;

        public BooleanVariable(string name) {
            this.name = name;
        }

        public override bool GetValue(VariableContainer vc) {
            return vc.floats[index] > 0;
        }

        public override void SetIndices(VariableContainer vc) {
            if (!vc.floatIndex.ContainsKey(name)) throw new System.Exception("Invalid float parameter: " + name);
            index = vc.floatIndex[name];
        }
    }

    public class BooleanConstant : Boolean {
        public bool value;

        public BooleanConstant(bool value) {
            this.value = value;
        }

        public override bool GetValue(VariableContainer vc) {
            return value;
        }

        public override void SetIndices(VariableContainer vc) { }
    }

    public class Not : Boolean {
        public Boolean val;

        public Not(Boolean val) {
            this.val = val;
        }

        public override bool GetValue(VariableContainer vc) {
            return !val.GetValue(vc);
        }

        public override void SetIndices(VariableContainer vc) {
            val.SetIndices(vc);
        }
    }

    public abstract class BooleanOperation : Boolean {
        public Boolean val1;
        public Boolean val2;

        public BooleanOperation(Boolean val1, Boolean val2) {
            this.val1 = val1;
            this.val2 = val2;
        }

        public override void SetIndices(VariableContainer vc) {
            val1.SetIndices(vc);
            val2.SetIndices(vc);
        }
    }

    public abstract class Comparison : Boolean {
        public Number val1;
        public Number val2;

        public Comparison(Number val1, Number val2) {
            this.val1 = val1.GetSimplified();
            this.val2 = val2.GetSimplified();
        }

        public override void SetIndices(VariableContainer vc) {
            val1.SetIndices(vc);
            val2.SetIndices(vc);
        }
    }

    //Boolean operations
    public class And : BooleanOperation {
        public And(Boolean val1, Boolean val2) : base(val1, val2) { }

        public override bool GetValue(VariableContainer vc) {
            return val1.GetValue(vc) && val2.GetValue(vc);
        }
    }

    public class Or : BooleanOperation {
        public Or(Boolean val1, Boolean val2) : base(val1, val2) { }

        public override bool GetValue(VariableContainer vc) {
            return val1.GetValue(vc) || val2.GetValue(vc);
        }
    }

    public class Xor : BooleanOperation {
        public Xor(Boolean val1, Boolean val2) : base(val1, val2) { }

        public override bool GetValue(VariableContainer vc) {
            return val1.GetValue(vc) ^ val2.GetValue(vc);
        }
    }

    public class GTE : Comparison {
        public GTE(Number val1, Number val2) : base(val1, val2) { }

        public override bool GetValue(VariableContainer vc) {
            return val1.GetValue(vc) >= val2.GetValue(vc);
        }
    }

    public class GT : Comparison {
        public GT(Number val1, Number val2) : base(val1, val2) { }

        public override bool GetValue(VariableContainer vc) {
            return val1.GetValue(vc) > val2.GetValue(vc);
        }
    }

    public class LTE : Comparison {
        public LTE(Number val1, Number val2) : base(val1, val2) { }

        public override bool GetValue(VariableContainer vc) {
            return val1.GetValue(vc) <= val2.GetValue(vc);
        }
    }

    public class LT : Comparison {
        public LT(Number val1, Number val2) : base(val1, val2) { }

        public override bool GetValue(VariableContainer vc) {
            return val1.GetValue(vc) < val2.GetValue(vc);
        }
    }

    public class EQ : Comparison {
        public EQ(Number val1, Number val2) : base(val1, val2) { }

        public override bool GetValue(VariableContainer vc) {
            return val1.GetValue(vc) == val2.GetValue(vc);
        }
    }
}

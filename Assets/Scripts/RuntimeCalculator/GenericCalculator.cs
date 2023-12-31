using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RuntimeCalculator {
    public interface GenericCalculator<T> {
        public T GetValue(VariableContainer vc);
        public void SetIndices(VariableContainer vc);
    }
}

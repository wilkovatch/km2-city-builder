using System.Collections;
using System.Collections.Generic;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    //the placement rules of a mesh instance: of its mesh instance type, or of its parametric object type
    public interface IMeshPlacementType {
        MeshInstanceSettings.PlacementRules Rules { get; }
        RC.VariableContainer VariableContainer { get; }
        void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state);
    }

    //A mesh instance custom element type (settings.meshInstance): kept apart from the other custom element types
    public class MeshInstanceType : RuntimeType<Types.CustomElementType>, IMeshPlacementType {
        public MeshInstanceSettings.PlacementRules rules;

        public MeshInstanceSettings.PlacementRules Rules => rules;
        public RC.VariableContainer VariableContainer => variableContainer;
        public CustomElementMeshInstanceSettings Settings => typeData.settings.meshInstance;

        public MeshInstanceType(Types.CustomElementType type, string name) : base(type, type.parameters, null, (new List<string>(), new List<string>(), new List<string>()), name) {
            type.settings.meshInstance.compatibleTypes ??= new string[0];
            rules = new MeshInstanceSettings.PlacementRules(type.settings.meshInstance.placementRules);
            SetBaseIndices();
            rules.SetIndices(variableContainer);
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state) {
            FillBaseInitialVariables(variableContainer, state, null);
        }

        //whether an instance of this type can be switched to the other one
        public bool IsCompatibleWith(string other) {
            return System.Array.IndexOf(Settings.compatibleTypes, other) >= 0;
        }

        //the label of the type (a string key of the core), from its ui
        public string Label() {
            var label = typeData.ui.label;
            return string.IsNullOrEmpty(label) ? name : label;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    namespace MeshType {
        public class ComponentMeshCalculatorContainer {
            public string name;
            public RoadLikeType.MeshCalculator mesh;

            public ComponentMeshCalculatorContainer(string name, Types.Buildings.BlockComponent comp) {
                this.name = name;
                mesh = new RoadLikeType.MeshCalculator(comp.mesh);
            }

            public void SetIndices(RC.VariableContainer vc) {
                mesh.SetIndices(vc);
            }
        }

        public abstract class MeshType<T> : RuntimeType<T> {
            public List<ComponentMeshCalculatorContainer> componentMeshes = new List<ComponentMeshCalculatorContainer>();

            public MeshType(T type, ParameterContainer parameterContainer, (SubObjectWithLocalDefinitions obj, string condition)[] components,
                (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) vars, string name) : base(type, parameterContainer, components, vars, name) {
            }

            public abstract void FillVariables(RC.VariableContainer variableContainer, ObjectState state, ObjectState instanceState, Vector3 right);

            public void FillComponentVariables(RC.VariableContainer variableContainer, int c) {
                FillBaseSubVariables(variableContainer, c);
            }

            public abstract Types.Buildings.BlockComponent[] GetComponents();

            public abstract Dictionary<string, int[]> GetTexturesMapping();
        }
    }
}

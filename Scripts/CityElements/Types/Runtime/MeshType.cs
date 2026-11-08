using System.Collections;
using System.Collections.Generic;
using RC = RuntimeCalculator;
using Godot;

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

            //evaluates the active components with the variables already filled: appends their vertices and uvs, and the
            //triangles of each texture to its list (the face indices of a component are relative to its own vertices)
            public void EvaluateComponents(RC.VariableContainer vc, List<Vector3> vertices, List<Vector2> uvs, List<List<int>> indices) {
                var components = GetComponents();
                var texturesMapping = GetTexturesMapping();
                for (int c = 0; c < components.Length; c++) {
                    if (subConditions[c] != null && !subConditions[c].GetValue(vc)) continue;
                    FillComponentVariables(vc, c);
                    if (components[c].mesh == null) continue;
                    var compMesh = componentMeshes[c];
                    var mesh = compMesh.mesh;
                    var indexOffset = vertices.Count;
                    vertices.AddRange(mesh.verticesCalculators.GetValues(vc));
                    uvs.AddRange(mesh.uvsCalculators.GetValues(vc));
                    var faces = mesh.faces.GetValues(vc);
                    var facesTextures = mesh.facesTextures.GetValues(vc);
                    for (int j = 0; j < facesTextures.Length; j++) {
                        var tris = indices[texturesMapping[compMesh.name][(int)facesTextures[j]]];
                        tris.Add(indexOffset + (int)faces[j * 3]);
                        tris.Add(indexOffset + (int)faces[j * 3 + 1]);
                        tris.Add(indexOffset + (int)faces[j * 3 + 2]);
                    }
                }
            }

            public abstract Types.Buildings.BlockComponent[] GetComponents();

            public abstract Dictionary<string, int[]> GetTexturesMapping();
        }
    }
}

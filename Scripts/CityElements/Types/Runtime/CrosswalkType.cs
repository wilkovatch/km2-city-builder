using CityElements.Types.Runtime.MeshType;
using System.Collections.Generic;
using RC = RuntimeCalculator;
using Godot;

namespace CityElements.Types.Runtime {
    //the crosswalks of an intersection type: a mesh evaluated once per crosswalk, with the parameters of the intersection,
    //the parameters imported from the road of the crosswalk and the corners of the crosswalk
    public class CrosswalkType : MeshType<Types.CrosswalkType> {
        Types.Buildings.BlockComponent[] components;

        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists() {
            var floatVars = new List<string>() { "cwSize", "cwWidth", "cwAtRoadStart", "cwUsesRoadTexture" };
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>() {
                "worldUp", "worldRight", "worldForward",
                "cwOuterLeft", "cwInnerLeft", "cwOuterRight", "cwInnerRight"
            };
            return (floatVars, vec2Vars, vec3Vars);
        }

        static (SubObjectWithLocalDefinitions obj, string condition)[] GetComponents(Types.Buildings.BlockComponent[] components, ComponentInfo[] infos) {
            var res = new (SubObjectWithLocalDefinitions obj, string condition)[components.Length];
            for (int i = 0; i < components.Length; i++) {
                res[i] = (components[i], infos[i].condition);
            }
            return res;
        }

        public CrosswalkType(Types.IntersectionType parentType, string name)
            : base(parentType.crosswalk,
                  JunctionType.GetCompoundParameterContainer(parentType.parameters, parentType.crosswalk.importedParameters, parentType.crosswalk.staticDefinitions, null),
                  GetComponents(parentType.crosswalkComponents, parentType.crosswalk.components), GetVarLists(), name) {

            components = parentType.crosswalkComponents;
            AddBuiltInBools("cwAtRoadStart", "cwUsesRoadTexture");
            for (int i = 0; i < components.Length; i++) {
                componentMeshes.Add(new ComponentMeshCalculatorContainer(typeData.components[i].name, components[i]));
            }
            SetIndices();
        }

        protected void SetIndices() {
            SetBaseIndices();
            foreach (var compMesh in componentMeshes) {
                compMesh.SetIndices(variableContainer);
            }
        }

        //corners: outer left, inner left, outer right, inner right (outer: on the side of the road, inner: on the side
        //of the point of the intersection; left: on the left of the road going into the intersection)
        public void FillCrosswalkVariables(RC.VariableContainer vc, ObjectState state, ObjectState instanceState, List<Vector3> corners,
            bool atRoadStart, bool usesRoadTexture) {

            FillVector3(vc, "worldUp", GameEngine.Vector3.up);
            FillVector3(vc, "worldRight", GameEngine.Vector3.right);
            FillVector3(vc, "worldForward", GameEngine.Vector3.forward);
            FillVector3(vc, "cwOuterLeft", corners[0]);
            FillVector3(vc, "cwInnerLeft", corners[1]);
            FillVector3(vc, "cwOuterRight", corners[2]);
            FillVector3(vc, "cwInnerRight", corners[3]);
            FillFloat(vc, "cwSize", GameEngine.Vector3.Magnitude(corners[0] - corners[1]));
            FillFloat(vc, "cwWidth", GameEngine.Vector3.Magnitude(corners[0] - corners[2]));
            FillBool(vc, "cwAtRoadStart", atRoadStart);
            FillBool(vc, "cwUsesRoadTexture", usesRoadTexture);
            FillBaseInitialVariables(vc, state, instanceState);
            FillBaseIterationVariables(vc);
        }

        public override void FillVariables(RC.VariableContainer variableContainer, ObjectState state, ObjectState instanceState, Vector3 right) {
            FillBaseIterationVariables(variableContainer);
        }

        public override Types.Buildings.BlockComponent[] GetComponents() {
            return components;
        }

        public override Dictionary<string, int[]> GetTexturesMapping() {
            return typeData.texturesMapping;
        }
    }
}

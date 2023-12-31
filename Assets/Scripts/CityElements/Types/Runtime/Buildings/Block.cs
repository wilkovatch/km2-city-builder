using CityElements.Types.Runtime.MeshType;
using System;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime.Buildings {
    public class BlockType : MeshType<Types.Buildings.BlockType> {
        public List<string> widths;

        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists(Types.Buildings.BlockType type) {
            var floatVars = new List<string>() { "y0", "y1", "xMult" };
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>() {
                "localUp", "localRight", "localForward",
                "worldUp", "worldRight", "worldForward",
                "localUp0", "localRight0", "localForward0",
                "localUp1", "localRight1", "localForward1",
                "scale"
            };
            /*for (int i = 0; i < type.settings.sectionVertices.Length; i++) {
                floatVars.Add("x" + i);
                floatVars.Add("absX" + i);
                floatVars.Add("absY" + i);
                floatVars.Add("absZ" + i);
                vec3Vars.Add("v" + i);
                vec3Vars.Add("v" + i + "_0");
                vec3Vars.Add("v" + i + "_1");
            }*/
            return (floatVars, vec2Vars, vec3Vars);
        }

        static (SubObjectWithLocalDefinitions obj, string condition)[] GetComponents(Types.Buildings.BlockComponent[] components, ComponentInfo[] infos) {
            var res = new (SubObjectWithLocalDefinitions obj, string condition)[components.Length];
            for (int i = 0; i < components.Length; i++) {
                res[i] = (components[i], infos[i].condition);
            }
            return res;
        }

        public BlockType(Types.Buildings.BlockType type, string name) : base(type, type.parameters, GetComponents(type.components, type.settings.components), GetVarLists(type), name) {

            for (int i = 0; i < type.components.Length; i++) {
                var component = type.components[i];
                var info = type.settings.components[i];
                componentMeshes.Add(new ComponentMeshCalculatorContainer(info.name, component));
            }
            SetIndices();
        }

        protected void SetIndices() {
            SetBaseIndices();
            for (int c = 0; c < typeData.components.Length; c++) {
                var compMesh = componentMeshes[c];
                var vars = variableContainer; //was c
                compMesh.SetIndices(vars);
            }
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state, ObjectState instanceState) {
            FillVector3(variableContainer, "worldUp", Vector3.up);
            FillVector3(variableContainer, "worldRight", Vector3.right);
            FillVector3(variableContainer, "worldForward", Vector3.forward);
            FillBaseInitialVariables(variableContainer, state, instanceState);
        }

        public GeometryHelper.CurveType GetCurveType(ObjectState state) {
            return (GeometryHelper.CurveType)state.Int("curveType");
        }

        public bool NeedsLowPolyFix(ObjectState state, int segments) {
            return segments > 2 && (GetCurveType(state) == GeometryHelper.CurveType.LowPoly || state.Bool("adjustLowPolyWidth"));
        }

        public override void FillVariables(RC.VariableContainer variableContainer, ObjectState state, ObjectState instanceState, Vector3 right) {
            var fwd = Vector3.Cross(Vector3.up, right).normalized; //check if actually reversed
            FillVector3(variableContainer, "localUp", Vector3.up);
            FillVector3(variableContainer, "localRight", right);
            FillVector3(variableContainer, "localForward", fwd);
            /*for (int i = 0; i < sectionVertices.Length; i++) {
                FillFloat(variableContainer, "x" + i, (sectionVertices[i] - pos).magnitude);
                FillVector3(variableContainer, "v" + i, sectionVertices[i]);
                FillFloat(variableContainer, "absX" + i, sectionVertices[i].x);
                FillFloat(variableContainer, "absY" + i, sectionVertices[i].y);
                FillFloat(variableContainer, "absZ" + i, sectionVertices[i].z);
            }*/
            FillBaseIterationVariables(variableContainer);
        }

        public void FillBlockVariables(RC.VariableContainer variableContainer, float y0, float y1, float uMult, float vMult, float xMult, Vector3 scale) {
            FillFloat(variableContainer, "y0", y0);
            FillFloat(variableContainer, "y1", y1);
            FillFloat(variableContainer, "uMult", uMult);
            FillFloat(variableContainer, "vMult", vMult);
            FillFloat(variableContainer, "xMult", xMult);
            FillVector3(variableContainer, "scale", scale);
        }

        public override Types.Buildings.BlockComponent[] GetComponents() {
            return typeData.components;
        }

        public override Dictionary<string, int[]> GetTexturesMapping() {
            return typeData.settings.texturesMapping;
        }
    }
}


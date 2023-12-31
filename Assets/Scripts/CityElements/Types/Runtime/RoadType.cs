using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;
using CityElements.Types.Runtime.RoadLikeType;

namespace CityElements.Types.Runtime {
    public class RoadType: RoadLikeType<Types.RoadType> {
        public List<string> widths;

        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists(Types.RoadType type) {
            var floatVars = new List<string>() { "z", "totalLength", "segment", "segments", "throughIntersection", "startCrosswalkSize", "endCrosswalkSize" };
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>() {
                "localUp", "localRight", "localForward",
                "worldUp", "worldRight", "worldForward",
                "localUp0", "localRight0", "localForward0",
                "localUp1", "localRight1", "localForward1",
            };
            for (int i = 0; i < type.settings.sectionVertices.Length; i++) {
                floatVars.Add("x" + i);
                floatVars.Add("absX" + i);
                floatVars.Add("absY" + i);
                floatVars.Add("absZ" + i);
                vec3Vars.Add("v" + i);
                vec3Vars.Add("v" + i + "_0");
                vec3Vars.Add("v" + i + "_1");
            }
            return (floatVars, vec2Vars, vec3Vars);
        }

        static (SubObjectWithLocalDefinitions obj, string condition)[] GetComponents(RoadComponent[] components, ComponentInfo[] infos) {
            var res = new (SubObjectWithLocalDefinitions obj, string condition)[components.Length];
            for (int i= 0; i < components.Length; i++) {
                res[i] = (components[i], infos[i].condition);
            }
            return res;
        }

        public RoadType(Types.RoadType type, string name) : base(type, type.parameters, GetComponents(type.components, type.settings.components), GetVarLists(type), name) {
            if (type.settings.anchors != null) anchorsCalculators = new Vector3Array(type.settings.anchors);
            if (type.settings.trafficTypes != null) trafficTypes = RoadLikeType.TrafficType.GetArray(type.settings.trafficTypes);
            if (type.settings.trafficLanes != null) trafficLanes = TrafficLaneContainer.GetArray(type.settings.trafficLanes);
            if (type.settings.propsLines != null) propLines = RoadLikeType.PropLine.GetArray(type.settings.propsLines);

            for (int i = 0; i < type.components.Length; i++) {
                var component = type.components[i];
                var info = type.settings.components[i];
                componentMeshes.Add(new ComponentMeshCalculatorContainer(info.name, component));
            }
            segmentVerticesCalculators = new NumberArray(type.settings.sectionVertices);
            if (type.settings.widths != null) widths = new List<string>(type.settings.widths);
            SetIndices();
        }

        float GetLowpolyCurveMult(int i, List<Vector3> curvePoints) {
            if (curvePoints.Count < 3 || i <= 0 || i >= curvePoints.Count - 1) return 1.0f;
            var pos = curvePoints[i];
            var pos0 = curvePoints[i - 1];
            var pos1 = curvePoints[i + 1];
            var ang = 180.0f - Vector3.Angle(pos0 - pos, pos1 - pos);
            return 1.0f / Mathf.Cos((ang * 0.5f) * Mathf.Deg2Rad);
        }

        protected void SetIndices() {
            SetBaseIndices();
            var vc = variableContainer;
            segmentVerticesCalculators.SetIndices(vc);
            anchorsCalculators.SetIndices(vc);
            if (trafficLanes != null) foreach (var tl in trafficLanes) tl.SetIndices(vc);
            if (propLines != null) foreach (var pl in propLines) pl.SetIndices(vc);
            for (int c = 0; c < typeData.components.Length; c++) {
                var compMesh = componentMeshes[c];
                var vars = variableContainer; //was c
                compMesh.SetIndices(vars);
            }
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state, ObjectState instanceState, float totalLength, int segments) {
            FillVector3(variableContainer, "worldUp", Vector3.up);
            FillVector3(variableContainer, "worldRight", Vector3.right);
            FillVector3(variableContainer, "worldForward", Vector3.forward);
            FillFloat(variableContainer, "totalLength", totalLength);
            FillFloat(variableContainer, "segments", segments);
            FillFloat(variableContainer, "throughIntersection", state.Bool("throughIntersection") ? 1.0f : 0.0f);
            FillFloat(variableContainer, "startCrosswalkSize", state.Float("startCrosswalkSize"));
            FillFloat(variableContainer, "endCrosswalkSize", state.Float("endCrosswalkSize"));
            FillBaseInitialVariables(variableContainer, state, instanceState);
        }

        public GeometryHelper.CurveType GetCurveType(ObjectState state) {
            return (GeometryHelper.CurveType)state.Int("curveType");
        }

        public bool NeedsLowPolyFix(ObjectState state, int segments) {
            return segments > 2 && (GetCurveType(state) == GeometryHelper.CurveType.LowPoly || state.Bool("adjustLowPolyWidth"));
        }

        public override void FillInitialSegmentVariables(RC.VariableContainer variableContainer, Vector3 right0, Vector3 right1, Vector3[] section0, Vector3[] section1) {
            var fwd0 = Vector3.Cross(Vector3.up, right0).normalized; //check if actually reversed
            FillVector3(variableContainer, "localUp0", Vector3.up);
            FillVector3(variableContainer, "localRight0", right0);
            FillVector3(variableContainer, "localForward0", fwd0);
            var fwd1 = Vector3.Cross(Vector3.up, right1).normalized; //check if actually reversed
            FillVector3(variableContainer, "localUp1", Vector3.up);
            FillVector3(variableContainer, "localRight1", right1);
            FillVector3(variableContainer, "localForward1", fwd1);
            for (int i = 0; i < section0.Length; i++) {
                FillVector3(variableContainer, "v" + i + "_0", section0[i]);
                FillVector3(variableContainer, "v" + i + "_1", section1[i]);
            }
        }

        public override void FillSegmentVariables(RC.VariableContainer variableContainer, ObjectState state, ObjectState instanceState, Vector3 pos,
            Vector3 curSectionRight, float z, float ground, int section, Vector3[] sectionVertices, List<Vector3> curvePoints, int segments) {

            var fwd = Vector3.Cross(Vector3.up, curSectionRight).normalized; //check if actually reversed
            FillVector3(variableContainer, "localUp", Vector3.up);
            FillVector3(variableContainer, "localRight", curSectionRight);
            FillVector3(variableContainer, "localForward", fwd);
            FillFloat(variableContainer, "z", z);
            FillFloat(variableContainer, "segment", section);
            for (int i = 0; i < sectionVertices.Length; i++) {
                FillFloat(variableContainer, "x" + i, (sectionVertices[i] - pos).magnitude);
                FillVector3(variableContainer, "v" + i, sectionVertices[i]);
                FillFloat(variableContainer, "absX" + i, sectionVertices[i].x);
                FillFloat(variableContainer, "absY" + i, sectionVertices[i].y);
                FillFloat(variableContainer, "absZ" + i, sectionVertices[i].z);
            }
            if (NeedsLowPolyFix(state, segments)) {
                var loyPolyAdjust = GetLowpolyCurveMult(section - 1, curvePoints);
                foreach (var param in typeData.parameters.parameters) {
                    if ((param.type == "float") && widths.Contains(param.fullName())) {
                        var realState = param.instanceSpecific ? instanceState : state;
                        FillFloat(variableContainer, param.fullName(), realState.Float(param.fullName()) * loyPolyAdjust);
                    }
                }
                FillStaticDefinitionVariables(variableContainer); //adjusted widths can alter the result
            }
            FillBaseIterationVariables(variableContainer);
        }

        public override RoadComponent[] GetComponents() {
            return typeData.components;
        }

        public override Dictionary<string, int[]> GetTexturesMapping() {
            return typeData.settings.texturesMapping;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    public class JunctionType : RoadLikeType.RoadLikeType<Types.JunctionType> {
        Types.IntersectionType parentType;
        public Dictionary<string, RC.Vector3s.Vector> extraTerrainSplines = new Dictionary<string, RC.Vector3s.Vector>();
        public Dictionary<string, (string[] options, RC.Numbers.Number index)> textureDefinitions;
        public List<RC.Numbers.Number> sectionVerticesCalculators;
        public RC.Numbers.Number segmentsCalculator;
        public RC.VariableContainer sectionVerticesVS;

        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists(Types.JunctionType type) {
            var floatVars = new List<string>() {
                "z", "totalLength", "segment", "segments", "ground",
                "thisIsEndA", "thisIsEndB", "convex", "selfIntersectingSpline", "notDefaultTex"
            };
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>() {
                "localUp", "localRight", "localForward",
                "worldUp", "worldRight", "worldForward",
                "localUp0", "localRight0", "localForward0",
                "localUp1", "localRight1", "localForward1",
            };
            for (int i = 0; i < type.sectionVertices.Length; i++) {
                floatVars.Add("x" + i);
                floatVars.Add("absX" + i);
                floatVars.Add("absY" + i);
                floatVars.Add("absZ" + i);
                vec3Vars.Add("v" + i);
                vec3Vars.Add("v" + i + "_0");
                vec3Vars.Add("v" + i + "_1");
            }
            foreach (var param in type.importedParameters) {
                switch (param.type) {
                    case "float":
                    case "int":
                    case "bool":
                        floatVars.Add(param.newName);
                        break;
                    case "vec2":
                        vec2Vars.Add(param.newName);
                        break;
                    case "vec3":
                        vec3Vars.Add(param.newName);
                        break;
                }
            }
            return (floatVars, vec2Vars, vec3Vars);
        }

        static (SubObjectWithLocalDefinitions obj, string condition)[] GetComponents(Dictionary<string, RoadComponent> components, ComponentInfo[] infos) {
            var res = new (SubObjectWithLocalDefinitions obj, string condition)[infos.Length];
            for (int i = 0; i < infos.Length; i++) {
                res[i] = (components[infos[i].name], infos[i].condition);
            }
            return res;
        }

        static ParameterContainer GetCompoundParameterContainer(Types.IntersectionType parentType, Types.JunctionType type) {
            var res = new ParameterContainer();
            var pp = parentType.parameters;
            var jp = type.importedParameters;
            //shallow copy arrays (the array elements do not have to be modified so that's enough)
            if (pp.internalParametersSettings != null) res.internalParametersSettings = (InternalParameterSettings[])pp.internalParametersSettings.Clone();
            if (pp.loopVariables != null) res.loopVariables = (string[])pp.loopVariables.Clone();

            //merge parameters
            var lenA = pp.parameters != null ? pp.parameters.Length : 0;
            var lenB = jp != null ? jp.Length : 0;
            res.parameters = new Parameter[lenA + lenB];
            for (int i = 0; i < lenA; i++) {
                res.parameters[i] = pp.parameters[i];
            }
            for (int i = 0; i < lenB; i++) {
                var param = new Parameter();
                param.name = jp[i].newName;
                param.type = jp[i].type;
                res.parameters[lenA + i] = param;
            }

            //merge static definitions
            lenA = pp.staticDefinitions != null ? pp.staticDefinitions.Length : 0;
            lenB = type.staticDefinitions != null ? type.staticDefinitions.Length : 0;
            res.staticDefinitions = new Definition[lenA + lenB];
            for (int i = 0; i < lenA; i++) {
                var def = pp.staticDefinitions[i];
                res.staticDefinitions[i] = def;
            }
            for (int i = 0; i < lenB; i++) {
                var def = type.staticDefinitions[i];
                res.staticDefinitions[lenA + i] = def;
            }

            //merge dynamic definitions
            lenA = pp.dynamicDefinitions != null ? pp.dynamicDefinitions.Length : 0;
            lenB = type.dynamicDefinitions != null ? type.dynamicDefinitions.Length : 0;
            res.dynamicDefinitions = new Definition[lenA + lenB];
            for (int i = 0; i < lenA; i++) {
                var def = pp.dynamicDefinitions[i];
                res.dynamicDefinitions[i] = def;
            }
            for (int i = 0; i < lenB; i++) {
                var def = type.dynamicDefinitions[i];
                res.dynamicDefinitions[lenA + i] = def;
            }

            return res;
        }

        public JunctionType(Types.IntersectionType parentType, Types.JunctionType type, string name)
            : base(type, GetCompoundParameterContainer(parentType, type), GetComponents(parentType.components, type.components), GetVarLists(type), name) {

            this.parentType = parentType;
            if (type.anchors != null) anchorsCalculators = new Vector3Array(type.anchors);
            if (type.trafficLanes != null) trafficLanes = RoadLikeType.TrafficLaneContainer.GetArray(type.trafficLanes);
            if (type.propsLines != null) propLines = RoadLikeType.PropLine.GetArray(type.propsLines);

            for (int i = 0; i < type.components.Length; i++) {
                var info = type.components[i];
                componentMeshes.Add(new RoadLikeType.ComponentMeshCalculatorContainer(info.name, parentType.components[info.name]));
            }
            segmentVerticesCalculators = new NumberArray(new object[] { 0.0f, 1.0f });
            if (type.extraTerrainSplines != null) {
                foreach (var s in type.extraTerrainSplines) {
                    extraTerrainSplines.Add(s.name, RC.Parsers.Vector3Parser.ParseExpression(s.vertex));
                }
            }
            textureDefinitions = new Dictionary<string, (string[] options, RC.Numbers.Number index)>();
            if (type.textureDefinitions != null) {
                foreach (var d in type.textureDefinitions) {
                    textureDefinitions.Add(d.name, (d.options, RC.Parsers.FloatParser.ParseExpression(d.index)));
                }
            }

            var svParamters = new List<(string, System.Type)>();
            svParamters.Add(("segments", typeof(float)));
            svParamters.Add(("notDefaultTex", typeof(float)));
            foreach (var sf in typeData.standardFloatsBoolsAndInts) {
                svParamters.Add((sf, typeof(float)));
            }
            foreach (var sf in typeData.standardVec3s) {
                svParamters.Add((sf, typeof(Vector3)));
            }
            foreach (var sf in typeData.standardVec2s) {
                svParamters.Add((sf, typeof(Vector2)));
            }
            sectionVerticesVS = new RC.VariableContainer(svParamters);
            sectionVerticesCalculators = new List<RC.Numbers.Number>();
            foreach (var sv in typeData.sectionVertices) {
                sectionVerticesCalculators.Add(RC.Parsers.FloatParser.ParseExpression(sv));
            }
            segmentsCalculator = RC.Parsers.FloatParser.ParseExpression(typeData.actualSegments);

            SetIndices();
        }

        protected void SetIndices() {
            SetBaseIndices();
            var vc = variableContainer;
            segmentVerticesCalculators.SetIndices(vc);
            anchorsCalculators.SetIndices(vc);
            foreach (var c in extraTerrainSplines) {
                c.Value.SetIndices(vc);
            }
            foreach (var d in textureDefinitions) {
                d.Value.index.SetIndices(vc);
            }
            if (trafficLanes != null) foreach (var tl in trafficLanes) tl.SetIndices(vc);
            if (propLines != null) foreach (var pl in propLines) pl.SetIndices(vc);
            for (int c = 0; c < typeData.components.Length; c++) {
                var compMesh = componentMeshes[c];
                var vars = variableContainer; //was c
                compMesh.SetIndices(vars);
            }
            foreach (var c in sectionVerticesCalculators) {
                c.SetIndices(sectionVerticesVS);
            }
            segmentsCalculator.SetIndices(vc);
        }

        public void SetExtraTerrainSplineVariableContainer(RC.VariableContainer newSet = null) {
            var vc = newSet != null ? newSet : variableContainer;
            foreach (var c in extraTerrainSplines) {
                c.Value.SetIndices(vc);
            }
        }

        public Vector3 GetExtraTerrainSplinesVertex(string name, RC.VariableContainer variableContainer) {
            return extraTerrainSplines[name].GetValue(variableContainer);
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state, ObjectState instanceState, float totalLength, int segments,
            RoadLikeGenerator<Types.RoadType> startGenerator, RoadLikeGenerator<Types.RoadType> endGenerator) {

            FillVector3(variableContainer, "worldUp", Vector3.up);
            FillVector3(variableContainer, "worldRight", Vector3.right);
            FillVector3(variableContainer, "worldForward", Vector3.forward);
            FillFloat(variableContainer, "totalLength", totalLength);
            FillFloat(variableContainer, "segments", segments);
            FillBool(variableContainer, "convex", state.Bool("convex"));
            FillBool(variableContainer, "selfIntersectingSpline", state.Bool("selfIntersectingSpline"));
            FillBool(variableContainer, "thisIsEndA", state.Bool("thisIsEndA"));
            FillBool(variableContainer, "thisIsEndB", state.Bool("thisIsEndB"));
            FillBool(variableContainer, "notDefaultTex", state.Bool("notDefaultTex"));
            foreach (var param in typeData.importedParameters) {
                var generator = param.fromStart ? startGenerator : endGenerator;
                var vc = generator.variableContainer;
                switch (param.type) {
                    case "float":
                    case "int":
                    case "bool":
                        FillFloat(variableContainer, param.newName, vc.floats[vc.floatIndex[param.name]]);
                        break;
                    case "vec2":
                        FillVector2(variableContainer, param.newName, vc.vector2s[vc.vec2Index[param.name]]);
                        break;
                    case "vec3":
                        FillVector3(variableContainer, param.newName, vc.vector3s[vc.vec3Index[param.name]]);
                        break;
                }
            }
            FillBaseInitialVariables(variableContainer, state, instanceState);
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
            Vector3 curSectionRight, float z, float ground, int section, Vector3[] sectionVertices,List<Vector3> curvePoints, int segments) {

            var fwd = Vector3.Cross(Vector3.up, curSectionRight).normalized; //check if actually reversed
            FillVector3(variableContainer, "localUp", Vector3.up);
            FillVector3(variableContainer, "localRight", curSectionRight);
            FillVector3(variableContainer, "localForward", fwd);
            FillFloat(variableContainer, "z", z);
            FillFloat(variableContainer, "ground", ground);
            FillFloat(variableContainer, "segment", section);
            for (int i = 0; i < sectionVertices.Length; i++) {
                FillFloat(variableContainer, "x" + i, (sectionVertices[i] - pos).magnitude);
                FillVector3(variableContainer, "v" + i, sectionVertices[i]);
                FillFloat(variableContainer, "absX" + i, sectionVertices[i].x);
                FillFloat(variableContainer, "absY" + i, sectionVertices[i].y);
                FillFloat(variableContainer, "absZ" + i, sectionVertices[i].z);
            }
            FillBaseIterationVariables(variableContainer);
        }

        public override RoadComponent[] GetComponents() {
            var res = new List<RoadComponent>();
            foreach (var c in typeData.components) {
                res.Add(parentType.components[c.name]);
            }
            return res.ToArray();
        }

        public override Dictionary<string, int[]> GetTexturesMapping() {
            return typeData.texturesMapping;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    public class PropsElementType : RuntimeType<Types.PropsElementType> {
        public class PlacementRules {
            public RC.Numbers.Number meshIndex = null;
            public RC.Booleans.Boolean whileCondition;
            public RC.Booleans.Boolean ifCondition = null;
            public RC.Numbers.Number xPos;
            public RC.Numbers.Number zPos;
            public RC.Vector3s.Vector forward;

            public PlacementRules(PropsElementPlacementRules type) {
                if (type.meshIndex != null) meshIndex = RC.Parsers.FloatParser.ParseExpression(type.meshIndex);
                whileCondition = RC.Parsers.BooleanParser.ParseExpression(type.whileCondition);
                if (type.ifCondition != null) ifCondition = RC.Parsers.BooleanParser.ParseExpression(type.ifCondition);
                xPos = RC.Parsers.FloatParser.ParseExpression(type.xPos);
                zPos = RC.Parsers.FloatParser.ParseExpression(type.zPos);
                forward = RC.Parsers.Vector3Parser.ParseExpression(type.forward != null ? type.forward : "localForward");
            }

            public void SetIndices(RC.VariableContainer vc) {
                meshIndex?.SetIndices(vc);
                whileCondition.SetIndices(vc);
                ifCondition?.SetIndices(vc);
                xPos.SetIndices(vc);
                zPos.SetIndices(vc);
                forward.SetIndices(vc);
            }
        }

        public PlacementRules rules;

        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists() {
            var floatVars = new List<string>() { "i", "x", "z", "lastX", "lastZ", "totalLength", "meshNum" };
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>() {
                "localUp", "localRight", "localForward",
                "worldUp", "worldRight", "worldForward"
            };
            return (floatVars, vec2Vars, vec3Vars);
        }

        public PropsElementType(Types.PropsElementType type, string name): base(type, type.parametersInfo, null, GetVarLists(), name) {
            rules = new PlacementRules(type.placementRules);
            SetIndices();
        }

        protected void SetIndices() {
            SetBaseIndices();
            var vc = variableContainer;
            rules.SetIndices(vc);
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state, float totalLength, int meshNum) {
            FillVector3(variableContainer, "localUp", Vector3.up); //always the same
            FillVector3(variableContainer, "worldUp", Vector3.up);
            FillVector3(variableContainer, "worldRight", Vector3.right);
            FillVector3(variableContainer, "worldForward", Vector3.forward);
            FillFloat(variableContainer, "totalLength", totalLength);
            FillFloat(variableContainer, "meshNum", meshNum);
            FillBaseInitialVariables(variableContainer, state, null);
        }

        public void FillSegmentVariables(RC.VariableContainer variableContainer,int i, float lastX, float lastZ) {
            FillFloat(variableContainer, "i", i);
            FillFloat(variableContainer, "lastX", lastX);
            FillFloat(variableContainer, "lastZ", lastZ);
            FillBaseIterationVariables(variableContainer);
        }

        public void FillPositionVariables(RC.VariableContainer variableContainer, Vector3 curSectionRight, float x, float z) {
            var fwd = Vector3.Cross(Vector3.down, curSectionRight).normalized;
            FillVector3(variableContainer, "localRight", curSectionRight);
            FillVector3(variableContainer, "localForward", fwd);
            FillFloat(variableContainer, "x", x);
            FillFloat(variableContainer, "z", z);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    public class MeshInstanceSettings : RuntimeType<Types.MeshInstanceSettings> {
        public class PlacementRules {
            public RC.Booleans.Boolean autoYOffset;
            public RC.Numbers.Number layer, placerLayerMask;

            public PlacementRules(Types.MeshInstancePlacementRules type) {
                autoYOffset = RC.Parsers.BooleanParser.ParseExpression(type.autoYOffset);
                layer = RC.Parsers.FloatParser.ParseExpression(type.layer);
                placerLayerMask = RC.Parsers.FloatParser.ParseExpression(type.placerLayerMask);
            }

            public void SetIndices(RC.VariableContainer vc) {
                autoYOffset?.SetIndices(vc);
                layer?.SetIndices(vc);
                placerLayerMask?.SetIndices(vc);
            }
        }

        public PlacementRules rules;

        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists() {
            var floatVars = new List<string>();
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>();
            return (floatVars, vec2Vars, vec3Vars);
        }

        public MeshInstanceSettings(Types.MeshInstanceSettings type) : base(type, type.parametersInfo, null, GetVarLists(), "settings") {
            rules = new PlacementRules(type.placementRules);
            SetIndices();
        }

        protected void SetIndices() {
            SetBaseIndices();
            var vc = variableContainer;
            rules.SetIndices(vc);
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state) {
            FillBaseInitialVariables(variableContainer, state, null);
        }
    }
}

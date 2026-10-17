using System.Collections;
using System.Collections.Generic;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    public class IntersectionType: RuntimeType<Types.IntersectionType> {

        public struct ExtraPieceCalculators {
            public RC.Numbers.Number paramSrcIdx;
            public RC.Numbers.Number uMult;
            public RC.Numbers.Number vMult;
            public RC.Booleans.Boolean condition;

            public ExtraPieceCalculators(RC.Numbers.Number paramSrcIdx, RC.Numbers.Number uMult,
                RC.Numbers.Number vMult, RC.Booleans.Boolean condition) {

                this.paramSrcIdx = paramSrcIdx;
                this.uMult = uMult;
                this.vMult = vMult;
                this.condition = condition;
            }
        }

        public Dictionary<(string, string), JunctionType> junctionTypes = new Dictionary<(string, string), JunctionType>();
        public Dictionary<string, ExtraPieceCalculators> extraPiecesCalculators = new Dictionary<string, ExtraPieceCalculators>();
        Dictionary<(string, string), JunctionType> fallbackJunctionTypes = new Dictionary<(string, string), JunctionType>();

        // Junctions are defined per pair of road types: a pair missing from the core falls back to the junction of either
        // road type with itself (or any junction), reported once per pair.
        public JunctionType GetJunctionType(string roadTypeI, string roadTypeJ) {
            var key = (roadTypeI, roadTypeJ);
            if (junctionTypes.TryGetValue(key, out var junction)) return junction;
            if (fallbackJunctionTypes.TryGetValue(key, out junction)) return junction;
            if (!junctionTypes.TryGetValue((roadTypeI, roadTypeI), out junction) && !junctionTypes.TryGetValue((roadTypeJ, roadTypeJ), out junction)) {
                foreach (var j in junctionTypes.Values) { junction = j; break; }
            }
            fallbackJunctionTypes[key] = junction;
            GameEngine.LogWarning("Intersection type '" + name + "': no junction for road types '" + roadTypeI + "' and '" + roadTypeJ + "'"
                + (junction != null ? ", using '" + junction.name + "'" : ""));
            return junction;
        }

        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists(Types.IntersectionType type) {
            var floatVars = new List<string>();
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>();
            foreach (var piece in type.settings.extraTerrainPieces) {
                floatVars.Add(piece.name + "_index");
                floatVars.Add(piece.name + "_active");
            }
            return (floatVars, vec2Vars, vec3Vars);
        }

        public IntersectionType(Types.IntersectionType type, string name)
            : base(type, type.parameters, new (SubObjectWithLocalDefinitions obj, string condition)[0], GetVarLists(type), name) {

            foreach (var junction in type.junctions) {
                var parts = junction.Key.Split('_');
                var newType = new JunctionType(type, junction.Value, junction.Key);
                junctionTypes.Add((parts[0], parts[1]), newType);
            }
            foreach (var piece in typeData.settings.extraTerrainPieces) {
                var paramSrcIdx = RC.Parsers.FloatParser.ParseExpression(piece.parametersSourceIndex);
                var uMult = RC.Parsers.FloatParser.ParseExpression(piece.uMult);
                var vMult = RC.Parsers.FloatParser.ParseExpression(piece.vMult);
                var condition = RC.Parsers.BooleanParser.ParseExpression(piece.condition);
                extraPiecesCalculators.Add(piece.name, new ExtraPieceCalculators(paramSrcIdx, uMult, vMult, condition));
            }
            SetIndices();
        }

        public void SetIndices() {
            var vc = variableContainer;
            foreach (var epc in extraPiecesCalculators) {
                epc.Value.paramSrcIdx.SetIndices(vc);
                epc.Value.uMult.SetIndices(vc);
                epc.Value.vMult.SetIndices(vc);
                epc.Value.condition.SetIndices(vc);
            }
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state, ObjectState instanceState) {
            FillBaseInitialVariables(variableContainer, state, instanceState);
        }

        public void FillExtraTerrainPiecesConditionVariables(List<int> indices, List<bool> actives) {
            for (int i = 0; i < typeData.settings.extraTerrainPieces.Length; i++) {
                var piece = typeData.settings.extraTerrainPieces[i];
                FillFloat(variableContainer, piece.name + "_index", indices[i]);
                FillBool(variableContainer, piece.name + "_active", actives[i]);
            }
        }
    }
}

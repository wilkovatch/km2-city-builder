using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime.Buildings {
    public class BuildingType {
        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetEmptyVarLists() {
            var floatVars = new List<string>();
            var vec2Vars = new List<string>();
            var vec3Vars = new List<string>();
            return (floatVars, vec2Vars, vec3Vars);
        }

        public class Side: RuntimeType<Types.Buildings.BuildingSideType> {
            //public Dictionary<string, BlockType> blockTypes = new Dictionary<string, BlockType>();

            public Side(Types.Buildings.BuildingSideType type, string name) : base(type, type.parameters, null, GetEmptyVarLists(), name) {
                SetIndices();
                /*foreach (var blockType in type.blockTypes) {
                    blockTypes.Add(blockType.Key, new BlockType(blockType.Value, blockType.Key));
                }*/
            }

            protected void SetIndices() {
                SetBaseIndices();
            }

            public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state) {
                FillBaseInitialVariables(variableContainer, state, null);
            }
        }

        public class Building: RuntimeType<Types.Buildings.BuildingBuildingType> {
            public Building(Types.Buildings.BuildingBuildingType type, string name) : base(type, type.parameters, null, GetEmptyVarLists(), name) {
                SetIndices();
            }

            protected void SetIndices() {
                SetBaseIndices();
            }

            public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state) {
                FillBaseInitialVariables(variableContainer, state, null);
            }
        }

        public class Line: RuntimeType<Types.Buildings.BuildingLineType> {
            public Line(Types.Buildings.BuildingLineType type, string name) : base(type, type.parameters, null, GetEmptyVarLists(), name) {
                SetIndices();
            }

            protected void SetIndices() {
                SetBaseIndices();
            }

            public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state) {
                FillBaseInitialVariables(variableContainer, state, null);
            }
        }

        public Types.Buildings.BuildingType dataType;
        public Side side;
        public Building building;
        public Line line;

        public BuildingType(Types.Buildings.BuildingType type, string name) {
            dataType = type;
            side = new Side(type.sideType, name);
            building = new Building(type.buildingType, name);
            line = new Line(type.lineType, name);
        }
    }
}

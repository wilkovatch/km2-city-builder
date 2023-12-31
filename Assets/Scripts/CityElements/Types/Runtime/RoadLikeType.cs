using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    namespace RoadLikeType {
        public class MeshCalculator {
            public Vector3Array verticesCalculators;
            public Vector2Array uvsCalculators;
            public NumberArray faces;
            public NumberArray facesTextures;

            public MeshCalculator(RoadComponentMesh mesh) {
                verticesCalculators = new Vector3Array(mesh.vertices);
                uvsCalculators = new Vector2Array(mesh.uvs);
                faces = new NumberArray(mesh.faces);
                facesTextures = new NumberArray(mesh.facesTextures);
            }

            public void SetIndices(RC.VariableContainer vc) {
                verticesCalculators.SetIndices(vc);
                uvsCalculators.SetIndices(vc);
                faces.SetIndices(vc);
                facesTextures.SetIndices(vc);
            }
        }

        public class TrafficType {
            public string name;
            public Color color;

            public TrafficType(Types.TrafficType type) {
                name = type.name;
                color = new Color(type.color[0] / 255.0f, type.color[1] / 255.0f, type.color[2] / 255.0f);
            }

            public static TrafficType[] GetArray(Types.TrafficType[] types) {
                var newTypes = new TrafficType[types.Length];
                for (int i = 0; i < newTypes.Length; i++) {
                    newTypes[i] = new TrafficType(types[i]);
                }
                return newTypes;
            }
        }

        public class TrafficLaneContainer {
            public RC.Numbers.Number type;
            public RC.Booleans.Boolean condition;
            public RC.Vector3s.Vector startBound, endBound;
            public NumberArray lanes;

            public TrafficLaneContainer(TrafficLane lane) {
                type = RC.Parsers.FloatParser.ParseExpression(lane.type);
                if (lane.condition != null) condition = RC.Parsers.BooleanParser.ParseExpression(lane.condition);
                startBound = RC.Parsers.Vector3Parser.ParseExpression(lane.bounds[0]);
                endBound = RC.Parsers.Vector3Parser.ParseExpression(lane.bounds[1]);
                lanes = new NumberArray(lane.lanes);
            }

            public static TrafficLaneContainer[] GetArray(TrafficLane[] lanes) {
                var newLanes = new TrafficLaneContainer[lanes.Length];
                for (int i = 0; i < newLanes.Length; i++) {
                    newLanes[i] = new TrafficLaneContainer(lanes[i]);
                }
                return newLanes;
            }

            public void SetIndices(RC.VariableContainer vc) {
                if (condition != null) condition.SetIndices(vc);
                lanes.SetIndices(vc);
                type.SetIndices(vc);
                startBound.SetIndices(vc);
                endBound.SetIndices(vc);
            }
        }

        public class ComponentMeshCalculatorContainer {
            public string name;
            public MeshCalculator mainMesh, startMesh, endMesh;
            public Vector3Array anchorsCalculators;
            public TrafficLaneContainer[] trafficLanes;
            public PropLine[] propLines;

            public ComponentMeshCalculatorContainer(string name, RoadComponent comp) {
                this.name = name;
                if (comp.mainMesh != null) mainMesh = new MeshCalculator(comp.mainMesh);
                if (comp.startMesh != null) startMesh = new MeshCalculator(comp.startMesh);
                if (comp.endMesh != null) endMesh = new MeshCalculator(comp.endMesh);
                if (comp.anchors != null) anchorsCalculators = new Vector3Array(comp.anchors);
                if (comp.trafficLanes != null) trafficLanes = TrafficLaneContainer.GetArray(comp.trafficLanes);
                if (comp.propsLines != null) propLines = PropLine.GetArray(comp.propsLines);
            }

            public void SetIndices(RC.VariableContainer vc) {
                if (mainMesh != null) mainMesh.SetIndices(vc);
                if (startMesh != null) startMesh.SetIndices(vc);
                if (endMesh != null) endMesh.SetIndices(vc);
                if (anchorsCalculators != null) anchorsCalculators.SetIndices(vc);
                if (trafficLanes != null) foreach (var e in trafficLanes) e.SetIndices(vc);
                if (propLines != null) foreach (var e in propLines) e.SetIndices(vc);
            }
        }

        public class PropLine {
            public string containerName;
            public RC.Booleans.Boolean condition;
            public RC.Vector3s.Vector startBound, endBound;

            public PropLine(Types.PropLine line) {
                containerName = line.containerName;
                if (line.condition != null) condition = RC.Parsers.BooleanParser.ParseExpression(line.condition);
                startBound = RC.Parsers.Vector3Parser.ParseExpression(line.bounds[0]);
                endBound = RC.Parsers.Vector3Parser.ParseExpression(line.bounds[1]);
            }

            public static PropLine[] GetArray(Types.PropLine[] lines) {
                var propLines = new PropLine[lines.Length];
                for (int i = 0; i < propLines.Length; i++) {
                    propLines[i] = new PropLine(lines[i]);
                }
                return propLines;
            }

            public void SetIndices(RC.VariableContainer vc) {
                if (condition != null) condition.SetIndices(vc);
                startBound.SetIndices(vc);
                endBound.SetIndices(vc);
            }
        }

        public abstract class RoadLikeType<T> : RuntimeType<T> {
            public NumberArray segmentVerticesCalculators;
            public Vector3Array anchorsCalculators;
            public TrafficType[] trafficTypes;
            public TrafficLaneContainer[] trafficLanes;
            public PropLine[] propLines;
            public List<ComponentMeshCalculatorContainer> componentMeshes = new List<ComponentMeshCalculatorContainer>();

            public RoadLikeType(T type, ParameterContainer parameterContainer, (SubObjectWithLocalDefinitions obj, string condition)[] components,
                (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) vars, string name) : base(type, parameterContainer, components, vars, name) {
            }

            public abstract void FillInitialSegmentVariables(RC.VariableContainer variableContainer, Vector3 right0, Vector3 right1, Vector3[] section0, Vector3[] section1);

            public abstract void FillSegmentVariables(RC.VariableContainer variableContainer, ObjectState state, ObjectState instanceState, Vector3 pos,
                Vector3 curSectionRight, float z, float ground, int section, Vector3[] sectionVertices, List<Vector3> curvePoints, int segments);

            public void FillComponentVariables(RC.VariableContainer variableContainer, int c) {
                FillBaseSubVariables(variableContainer, c);
            }

            public abstract RoadComponent[] GetComponents();

            public abstract Dictionary<string, int[]> GetTexturesMapping();
        }
    }
}

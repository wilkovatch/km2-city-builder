using System.Collections;
using System.Collections.Generic;
using RC = RuntimeCalculator;

namespace CityElements.Types.Runtime {
    public class LowLevelType: RuntimeType<Types.LowLevelType> {
        //the functions of the Lua scripts of the core (null if the type has none)
        public readonly Lua.LuaFunction geometryFunction, constraintsFunction, derivedFunction;
        public readonly RC.Scripting.LuaScriptHost scriptHost;
        public readonly bool canBatchGeometryPreparation;
        public readonly RoadType splineType;

        static (List<string> floatVars, List<string> vec2Vars, List<string> vec3Vars) GetVarLists(Types.LowLevelType type) {
            return type.settings.spline == null ? (new List<string>(), new List<string>(), new List<string>())
                : RoadType.GetVarLists(new Types.RoadType { settings = type.settings.spline });
        }

        //scriptHost: the host of the scripts (by default the one of the core; the tests use their own)
        public LowLevelType(Types.LowLevelType type, string name, RC.Scripting.LuaScriptHost scriptHost = null) : base(type, type.parameters, null, GetVarLists(type), name) {
            //preparing a later element must not advance the shared random sequence or print ahead of an earlier one
            canBatchGeometryPreparation = true;
            foreach (var definition in type.parameters.staticDefinitions ?? new Definition[0]) {
                if (definition.script != null || (definition.value != null && definition.value.Contains("rnd"))) canBatchGeometryPreparation = false;
            }
            var settings = type.settings;
            if (settings.spline != null) {
                var spline = settings.spline;
                if (spline.curveTypes == null || spline.curveTypes.Length == 0 ||
                    System.Array.IndexOf(spline.curveTypes, spline.defaultCurveType) < 0 ||
                    System.Array.Exists(spline.curveTypes, c => c < 0 || c > 2)) throw new System.Exception("Invalid spline curve types: " + name);
                if (spline.sectionVertices == null || spline.sectionVertices.Length == 0) throw new System.Exception("Spline has no section vertices: " + name);
                spline.components ??= new ComponentInfo[0];
                spline.textures ??= new string[0];
                spline.texturesMapping ??= new Dictionary<string, int[]>();
                spline.widths ??= new string[0];
                splineType = new SplineType(new Types.RoadType { settings = spline, parameters = type.parameters,
                    components = type.splineComponents ?? new RoadComponent[0], ui = type.ui }, name);
                canBatchGeometryPreparation = false; //projection and road component evaluation stay on the main thread
            }
            if (settings.geometry != null || settings.constraints != null || settings.derivedPoints != null) {
                this.scriptHost = scriptHost ?? RC.Scripting.LuaScriptHost.Get();
                if (settings.geometry != null) geometryFunction = this.scriptHost.Resolve(settings.geometry);
                if (settings.constraints != null) constraintsFunction = this.scriptHost.Resolve(settings.constraints);
                if (settings.derivedPoints != null) {
                    if (string.IsNullOrEmpty(settings.derivedPoints.flag)) throw new System.Exception("low-level type '" + name + "': derivedPoints has no flag");
                    derivedFunction = this.scriptHost.Resolve(settings.derivedPoints.function);
                }
            }
            CheckPointNodes(settings.controlPoints, name, false);
        }

        sealed class SplineType : RoadType {
            public SplineType(Types.RoadType type, string name) : base(type, name) { }
            //The sampler has already applied its bounded miter to the section frame.
            public override bool NeedsLowPolyFix(ObjectState state, int segments) => false;
        }

        //the names must be unique in a group, and "uniform" only makes sense in an element of another array
        static void CheckPointNodes(LowLevelPointNode[] nodes, string typeName, bool inArray) {
            if (nodes == null) return;
            var names = new HashSet<string>();
            foreach (var node in nodes) {
                if (string.IsNullOrEmpty(node.name)) throw new System.Exception("low-level type '" + typeName + "': a control point has no name");
                if (!names.Add(node.name)) throw new System.Exception("low-level type '" + typeName + "': the control point '" + node.name + "' is defined twice");
                if (node.name.Contains(".")) throw new System.Exception("low-level type '" + typeName + "': the name of the control point '" + node.name + "' contains a dot");
                if (node.array != null && node.array.uniform && !inArray) {
                    GameEngine.LogWarning("Low-level type '" + typeName + "': the array '" + node.name + "' is uniform but not in an element of another array");
                }
                CheckPointNodes(node.children, typeName, inArray || node.array != null);
            }
        }

        public bool HasMesh => typeData.settings.mesh != null;
        public bool IsContainer => typeData.settings.container != null;

        public bool CanContain(string typeName) {
            var contains = typeData.settings.container?.contains;
            return contains != null && System.Array.IndexOf(contains, typeName) >= 0;
        }

        public void FillInitialVariables(RC.VariableContainer variableContainer, ObjectState state) {
            FillBaseInitialVariables(variableContainer, state, null);
        }

        //the parameters referencing other elements (their values are {"kind": ..., "id": ...} states)
        public IEnumerable<string> GetReferenceParameters() {
            if (parameterContainer.parameters == null) yield break;
            foreach (var param in parameterContainer.parameters) {
                if (param.type == "reference") yield return param.fullName();
            }
        }
    }
}

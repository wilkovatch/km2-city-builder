using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Lua;
using GameObject = Godot.Node3D;
using RC = RuntimeCalculator;
using GO = GameEngine.GameObject;
using GH = GeometryHelper;

//A place given by a prop rule or a prop placer, with the model to show there if it has one: the exporters get them all
public sealed class Placement {
    public string container, mesh;
    public Vector3 position, rotation, scale = Vector3.One;
    public object data; //optional values for the exporters (given by the script)
    public bool meshTransform, autoYOffset = true;
    public GameObject obj; //the instance of the model (null: no model, or it wasn't found)
}

//The props of a prop rule ("propRule" of a state: a props container for each name), placed along the lines of each
//container (between a start and an end line with as many points): used by the roads and the custom elements
public static class PropPlacement {
    const int MaxRowProps = 1000; //on a road, more are silently left out
    const int MaxPlacerProps = 100000; //on a prop placer, more are an error: it must not silently preview less than it exports

    static Vector3 PathTangent(Vector3[] path, float distance) {
        var last = GameEngine.Vector3.forward;
        for (int i = 0; i + 1 < path.Length; i++) {
            var delta = path[i + 1] - path[i];
            var length = delta.Length();
            if (length < 0.000001f) continue;
            last = delta / length;
            if (distance <= length) return last;
            distance -= length;
        }
        return last;
    }
    //lines: the start and end lines of a container, null if it has none; the props are created in propsContainer (on the
    //props layer) and listed in props, the placements (also those without a model) in placements; injected: the _GLOBAL_
    //values of the owner, for the rules and the scripts
    public static void Place(ObjectState rule, string[] containerNames, System.Func<string, (Vector3[] inLine, Vector3[] outLine)?> lines,
        GameObject propsContainer, List<(string container, string meshName, GameObject obj)> props, List<Placement> placements, ObjectState injected = null) {

        if (rule == null || containerNames == null) return;
        foreach (var containerType in containerNames) {
            var container = rule.State(containerType);
            if (Operations.Props.TypeOf(container) == null) continue;
            var line = lines(containerType);
            if (!line.HasValue) continue;
            var index = 0;
            foreach (var state in Operations.Props.Placing(container)) PlaceLane(line.Value.inLine, line.Value.outLine, state, index++, containerType, propsContainer, props, placements, injected: injected);
        }
    }

    public static void PlaceLines(ObjectState rule, List<CustomElements.GeometryPropLine> lines, GameObject root,
        List<(string container, string meshName, GameObject obj)> props, List<Placement> placements, CustomElement owner = null) {
        if (rule == null) return;
        foreach (var group in lines.GroupBy(r => r.container)) {
            var container = rule.State(group.Key);
            if (Operations.Props.TypeOf(container) == null) continue;
            var segments = group.ToList();
            var index = 0;
            foreach (var state in Operations.Props.Placing(container)) {
                PlaceLane(segments[0].startBound.ToArray(), segments[0].endBound.ToArray(), state, index++,
                    group.Key, root, props, placements, segments, owner: owner, injected: owner?.cityPropertiesState);
            }
        }
    }

    static (Vector3 pos, Vector3 dir, Vector3 tangent) Sample(List<CustomElements.GeometryPropLine> segments, float x, float z) {
        foreach (var segment in segments) {
            var mid = segment.distanceReference.Count == segment.startBound.Count ? segment.distanceReference
                : segment.startBound.Zip(segment.endBound, (a, b) => (a + b) * 0.5f).ToList();
            for (int i = 0; i + 1 < mid.Count; i++) {
                var length = mid[i].DistanceTo(mid[i + 1]);
                if (length < 0.000001f) continue;
                if (z <= length) {
                    var t = Mathf.Clamp(z / length, 0, 1);
                    var a = segment.startBound[i].Lerp(segment.startBound[i + 1], t);
                    var b = segment.endBound[i].Lerp(segment.endBound[i + 1], t);
                    var tangent = (mid[i + 1] - mid[i]).Normalized();
                    var right = (a - b).Normalized();
                    if (right.LengthSquared() < 0.000001f) right = GameEngine.Vector3.up.Cross(tangent).Normalized(); //the direction of a - b as the bounds get closer
                    return (a.Lerp(b, x), right, tangent);
                }
                z -= length;
            }
        }
        var last = segments[segments.Count - 1];
        var n = last.startBound.Count - 1;
        return (last.startBound[n].Lerp(last.endBound[n], x), GameEngine.Vector3.right, GameEngine.Vector3.forward);
    }

    //a point of the lines: x across them (0 to 1), z along them
    static (Vector3 pos, Vector3 dir, Vector3 tangent) SampleLines(Vector3[] inLine, Vector3[] outLine, Vector3[] midLine, List<CustomElements.GeometryPropLine> segments, float x, float z) {
        if (segments != null) return Sample(segments, x, z);
        var posDir = GH.GetPointAndDirOnSidewalk(inLine, outLine, midLine, x, z);
        return (posDir.pos, posDir.dir, PathTangent(midLine, z));
    }

    static void PlaceLane(Vector3[] inLine, Vector3[] outLine, ObjectState elem, int index, string containerType, GameObject propsContainer,
        List<(string container, string meshName, GameObject obj)> props, List<Placement> placements, List<CustomElements.GeometryPropLine> segments = null, CustomElement owner = null,
        ObjectState injected = null) {

        if (inLine.Length != outLine.Length) {
            GameEngine.Print("Invalid props spline detected");
            return;
        }
        var type = Operations.Props.TypeOf(elem);
        if (type == null || type.IsGroup) return;
        var midLine = new Vector3[inLine.Length];
        for (int i = 0; i < midLine.Length; i++) {
            midLine[i] = (inLine[i] + outLine[i]) * 0.5f;
        }
        var midLength = GH.GetPathLength(midLine);
        if (segments != null) midLength = segments.Sum(segment => GH.GetPathLength(
            segment.distanceReference.Count == segment.startBound.Count ? segment.distanceReference.ToArray()
            : segment.startBound.Zip(segment.endBound, (a, b) => (a + b) * 0.5f).ToArray()));
        PlaceScripted(type, elem, index, inLine, outLine, midLine, midLength, containerType, propsContainer, props, placements, segments, owner, injected);
    }

    //The placements given by the Lua callback of the type (see the wiki, Core-Props-and-Objects): called with the variables
    //of the state and a context to read the lines with
    static void PlaceScripted(CityElements.Types.Runtime.PropsType type, ObjectState elem, int index, Vector3[] inLine, Vector3[] outLine, Vector3[] midLine, float length, string containerType,
        GameObject root, List<(string container, string meshName, GameObject obj)> props, List<Placement> placements, List<CustomElements.GeometryPropLine> segments, CustomElement owner, ObjectState injected) {

        var definition = type.name + " (props placement)";
        var host = type.scriptHost;
        var variables = type.variableContainer.GetClone();
        type.FillInitialVariables(variables, elem, length, injected);
        var random = new PropPlacerGeometry.RandomSequence(Seed(containerType, index));
        var context = new LuaTable();
        var maxProps = owner == null ? MaxRowProps : MaxPlacerProps;
        context["length"] = length;
        context["limit"] = maxProps;
        context["placer"] = owner != null;
        context["projected"] = owner?.ProjectsPlacement == true;
        context["parameters"] = PropPlacerGeometry.StateTable(elem);
        context["random"] = new LuaFunction("random", (ctx, ct) => new ValueTask<int>(ctx.Return(new LuaValue(random.Next()))));
        context["sample"] = SampleFunction(inLine, outLine, midLine, segments);
        //the spans between two points of the lines, each one with its own length and sample
        context["edges"] = new LuaFunction("edges", (ctx, ct) => {
            var res = new LuaTable();
            var count = 0;
            foreach (var segment in segments ?? new List<CustomElements.GeometryPropLine> { new CustomElements.GeometryPropLine { container = containerType, startBound = inLine.ToList(), endBound = outLine.ToList() } }) {
                for (int k = 0; k + 1 < segment.startBound.Count; k++) {
                    var edge = new CustomElements.GeometryPropLine { container = containerType, startBound = segment.startBound.GetRange(k, 2), endBound = segment.endBound.GetRange(k, 2) };
                    if (segment.distanceReference.Count == segment.startBound.Count) edge.distanceReference = segment.distanceReference.GetRange(k, 2);
                    var middle = edge.distanceReference.Count == 2 ? edge.distanceReference : edge.startBound.Zip(edge.endBound, (a, b) => (a + b) * 0.5f).ToList();
                    if (middle[0].DistanceTo(middle[1]) < 0.000001f) continue;
                    var edgeTable = new LuaTable();
                    edgeTable["length"] = GH.GetPathLength(middle.ToArray());
                    edgeTable["sample"] = SampleFunction(edge.startBound.ToArray(), edge.endBound.ToArray(), middle.ToArray(), new List<CustomElements.GeometryPropLine> { edge });
                    res[++count] = edgeTable;
                }
            }
            return new ValueTask<int>(ctx.Return(res));
        });
        List<Placement> read;
        try {
            var result = host.Call(type.placementFunction, RC.Scripting.LuaVariableView.Get(variables), context, definition);
            if (!result.TryRead<LuaTable>(out var table)) throw new System.ArgumentException("the callback must return a list");
            if (owner != null && table.ArrayLength >= maxProps) throw new System.ArgumentException("too many generated props");
            read = new List<Placement>();
            for (int i = 1; i <= table.ArrayLength && i <= maxProps; i++) read.Add(ReadPlacement(table[i], containerType));
        } catch (System.ArgumentException) {
            if (owner != null) throw; //shown as the placement error of the element
            return;
        } catch (System.Exception e) {
            host.ReportError(definition, e.Message);
            if (owner != null) throw new System.ArgumentException(e.Message);
            return;
        }
        foreach (var placement in read) {
            if (owner?.ProjectsPlacement == true) placement.position = PropPlacerGeometry.Project(owner, placement.position);
            placement.meshTransform = type.typeData.meshTransform;
            placement.autoYOffset = type.typeData.autoYOffset;
            Add(placement, root, props, placements);
        }
    }

    //sample(x, z) of the scripts: x across the lines (0 to 1), z along them
    static LuaFunction SampleFunction(Vector3[] inLine, Vector3[] outLine, Vector3[] midLine, List<CustomElements.GeometryPropLine> segments) {
        return new LuaFunction("sample", (ctx, ct) => {
            var sampled = SampleLines(inLine, outLine, midLine, segments, (float)ctx.GetArgument<double>(0), (float)ctx.GetArgument<double>(1));
            var res = new LuaTable();
            res["position"] = RC.Scripting.LuaVectors.ToLua(sampled.pos);
            res["right"] = RC.Scripting.LuaVectors.ToLua(sampled.dir);
            res["forward"] = RC.Scripting.LuaVectors.ToLua(GameEngine.Vector3.Normalize(GameEngine.Vector3.Cross(GameEngine.Vector3.down, sampled.dir)));
            res["tangent"] = RC.Scripting.LuaVectors.ToLua(sampled.tangent);
            return new ValueTask<int>(ctx.Return(res));
        });
    }

    //the seed of the random numbers of the context: the same at every rebuild, whatever else is in the city
    static int Seed(string container, int index) {
        var seed = unchecked((int)2166136261u);
        foreach (var c in container) seed = unchecked((seed ^ c) * 16777619);
        return unchecked((seed ^ index) * 16777619);
    }

    //an entry of the list of a callback: position, rotation (euler degrees) or forward, scale, model (a mesh path), data
    public static Placement ReadPlacement(LuaValue value, string container) {
        if (!value.TryRead<LuaTable>(out var item)) throw new System.InvalidOperationException("a placement must be a table");
        var placement = new Placement { container = container, position = item["position"].Read<RC.Scripting.LuaVec3>().value };
        if (item["rotation"].TryRead<RC.Scripting.LuaVec3>(out var rotation)) placement.rotation = rotation.value;
        else if (item["forward"].TryRead<RC.Scripting.LuaVec3>(out var forward)) placement.rotation = new Vector3(0, GameEngine.Vector3.SignedAngle(GameEngine.Vector3.forward, forward.value, GameEngine.Vector3.up), 0);
        if (item["scale"].TryRead<RC.Scripting.LuaVec3>(out var scale)) placement.scale = scale.value;
        if (item["model"].Type == LuaValueType.String) placement.mesh = item["model"].Read<string>();
        placement.data = ToPlain(item["data"]);
        if (!placement.position.IsFinite() || !placement.rotation.IsFinite() || !placement.scale.IsFinite()) throw new System.ArgumentException("non-finite generated prop transform");
        return placement;
    }

    static object ToPlain(LuaValue value) {
        switch (value.Type) {
            case LuaValueType.Boolean: return value.Read<bool>();
            case LuaValueType.Number: return value.Read<double>();
            case LuaValueType.String: return value.Read<string>();
            case LuaValueType.Table:
                var table = value.Read<LuaTable>();
                if (table.ArrayLength > 0) {
                    var list = new List<object>();
                    for (int i = 1; i <= table.ArrayLength; i++) list.Add(ToPlain(table[i]));
                    return list;
                }
                var dictionary = new Dictionary<string, object>();
                var key = LuaValue.Nil;
                while (table.TryGetNext(key, out var pair)) {
                    key = pair.Key;
                    if (key.TryRead<string>(out var name)) dictionary[name] = ToPlain(pair.Value);
                }
                return dictionary;
            default: return null;
        }
    }

    //creates the instance of the model of the placement, if it has one
    public static void Add(Placement placement, GameObject root, List<(string container, string meshName, GameObject obj)> props, List<Placement> placements) {
        placement.obj = PlaceInstance(placement.container, placement.mesh, placement.position, placement.rotation, placement.scale, placement.meshTransform, placement.autoYOffset, root, props);
        placements.Add(placement);
    }

    //The render mesh and picking shape use the same core placement transform.
    public static GameObject PlaceInstance(string container, string meshName, Vector3 position, Vector3 rotation, Vector3 scale,
        bool useCoreTransform, bool autoYOffset, GameObject root, List<(string container, string meshName, GameObject obj)> props) {
        if (string.IsNullOrEmpty(meshName)) return null;
        var obj = GO.CreateStaticBody(container + ";" + meshName);
        GO.SetParent(obj, root);
        GO.SetPosition(obj, position);
        GO.SetRotation(obj, rotation);
        GO.SetLocalScale(obj, scale);
        var mesh = MeshManager.GetMesh(meshName, obj);
        if (mesh == null) { GO.Destroy(obj); return null; }
        var transform = MeshManager.GetPlacementTransform(meshName, useCoreTransform, autoYOffset);
        transform.ApplyTo(mesh);
        var shape = MeshManager.GetSelectionShape(meshName);
        if (shape != null) transform.ApplyTo(GameEngine.Mesh.CreateShapeCollider(obj, shape));
        else {
            var bounds = GameEngine.Mesh.GetEncapsulatingBoundsForChildMeshes(obj);
            var size = GameEngine.Bounds.GetSize(bounds);
            if (size.LengthSquared() > 0) GameEngine.Mesh.CreateBoxCollider(obj, size, GameEngine.Bounds.GetCenter(bounds));
        }
        Actions.Helpers.SetLayerRecursively(obj, 13);
        props.Add((container, meshName, obj));
        return obj;
    }

}

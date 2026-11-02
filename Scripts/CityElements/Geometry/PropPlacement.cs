using System.Collections.Generic;
using System.Linq;
using Godot;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GH = GeometryHelper;

//The props of a prop rule ("propRule" of a state: a props container for each name), placed along the lines of each
//container (between a start and an end line with as many points): used by the roads and the custom elements
public static class PropPlacement {
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
    //props layer) and listed in props; the iterations of the types with exportSlots are listed in slots; injected: the
    //_GLOBAL_ values of the owner, for the rules
    public static void Place(ObjectState rule, string[] containerNames, System.Func<string, (Vector3[] inLine, Vector3[] outLine)?> lines,
        GameObject propsContainer, List<(string container, string meshName, GameObject obj)> props, List<(string container, Vector3 position, Vector3 forward)> slots = null, ObjectState injected = null) {

        if (rule == null || containerNames == null) return;
        var types = CityElements.Types.Parsers.TypeParser.GetPropsContainersTypes();
        foreach (var containerType in containerNames) {
            var container = rule.State(containerType);
            if (container == null) continue;
            var typeName = container.Str("type");
            if (typeName == null || !types.ContainsKey(typeName)) continue;
            var line = lines(containerType);
            if (!line.HasValue) continue;
            var type = types[typeName];
            foreach (var param in type.parameters) {
                if (param.maxNumber == 1) {
                    var paramState = container.State(param.name);
                    if (paramState != null) PlaceLane(line.Value.inLine, line.Value.outLine, paramState, containerType, propsContainer, props, slots: slots, injected: injected);
                } else {
                    var states = container.Array<ObjectState>(param.name);
                    if (states == null) continue;
                    foreach (var paramState in states) PlaceLane(line.Value.inLine, line.Value.outLine, paramState, containerType, propsContainer, props, slots: slots, injected: injected);
                }
            }
        }
    }

    public static void PlaceLines(ObjectState rule, List<CustomElements.GeometryPropLine> lines, GameObject root,
        List<(string container, string meshName, GameObject obj)> props, CustomElement owner = null, List<(string container, Vector3 position, Vector3 forward)> slots = null) {
        if (rule == null) return;
        foreach (var group in lines.GroupBy(r => r.container)) {
            var container = rule.State(group.Key);
            if (container == null) continue;
            var type = Operations.Props.GetContainerType(container);
            if (type == null) continue;
            var segments = group.ToList();
            foreach (var param in type.parameters) {
                var elements = param.maxNumber == 1 ? new[] { container.State(param.name) } : container.Array<ObjectState>(param.name);
                foreach (var elem in elements ?? new ObjectState[0]) {
                    if (elem != null) PlaceLane(segments[0].startBound.ToArray(), segments[0].endBound.ToArray(), elem,
                        group.Key, root, props, segments, owner: owner, slots: slots, injected: owner?.cityPropertiesState);
                }
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
                    if (right.LengthSquared() < 0.000001f) right = tangent.Cross(GameEngine.Vector3.up).Normalized();
                    return (a.Lerp(b, x), right, tangent);
                }
                z -= length;
            }
        }
        var last = segments[segments.Count - 1];
        var n = last.startBound.Count - 1;
        return (last.startBound[n].Lerp(last.endBound[n], x), GameEngine.Vector3.right, GameEngine.Vector3.forward);
    }

    static void PlaceLane(Vector3[] inLine, Vector3[] outLine, ObjectState elem, string containerType, GameObject propsContainer,
        List<(string container, string meshName, GameObject obj)> props, List<CustomElements.GeometryPropLine> segments = null, bool edgeCall = false, CustomElement owner = null,
        List<(string container, Vector3 position, Vector3 forward)> slots = null, ObjectState injected = null) {

        if (inLine.Length != outLine.Length) {
            GameEngine.Print("Invalid props spline detected");
            return;
        }
        var types = CityElements.Types.Parsers.TypeParser.GetPropsElementTypes();
        var typeName = elem.Str("type");
        if (typeName != null && types.ContainsKey(typeName)) {
            int i;
            var midLine = new Vector3[inLine.Length];
            for (i = 0; i < midLine.Length; i++) {
                midLine[i] = (inLine[i] + outLine[i]) * 0.5f;
            }
            var midLength = GH.GetPathLength(midLine);
            if (segments != null) midLength = segments.Sum(segment => GH.GetPathLength(
                segment.distanceReference.Count == segment.startBound.Count ? segment.distanceReference.ToArray()
                : segment.startBound.Zip(segment.endBound, (a, b) => (a + b) * 0.5f).ToArray()));
            var type = owner == null ? types[typeName] : PropPlacerGeometry.ScopedType(PropPlacerGeometry.Strip(owner.state, containerType), typeName);
            int numElemMeshes = 1;
            var fixedMeshes = type.typeData.fixedMeshes;
            var hasMeshList = fixedMeshes != null || type.typeData.maxMeshes != 1;
            if (fixedMeshes != null) {
                numElemMeshes = fixedMeshes.Length;
            } else if (type.typeData.maxMeshes != 1) {
                var meshesList = elem.Array<string>("meshes");
                if (meshesList == null) {
                    GameEngine.Print("Invalid props elem detected");
                    return;
                }
                numElemMeshes = meshesList.Length;
            }
            if (segments == null && type.rules.iterationScope == "edge") {
                segments = new List<CustomElements.GeometryPropLine> { new CustomElements.GeometryPropLine {
                    container = containerType, startBound = inLine.ToList(), endBound = outLine.ToList()
                } };
            }
            if (segments != null && type.rules.iterationScope == "edge" && !edgeCall) {
                foreach (var segment in segments) {
                    for (int k = 0; k + 1 < segment.startBound.Count; k++) {
                        var edge = new CustomElements.GeometryPropLine { container = containerType,
                            startBound = segment.startBound.GetRange(k, 2), endBound = segment.endBound.GetRange(k, 2) };
                        if (segment.distanceReference.Count == segment.startBound.Count) edge.distanceReference = segment.distanceReference.GetRange(k, 2);
                        var middle = edge.distanceReference.Count == 2 ? edge.distanceReference
                            : edge.startBound.Zip(edge.endBound, (a, b) => (a + b) * 0.5f).ToList();
                        if (middle[0].DistanceTo(middle[1]) < 0.000001f) continue;
                        PlaceLane(edge.startBound.ToArray(), edge.endBound.ToArray(), elem, containerType, propsContainer, props,
                            new List<CustomElements.GeometryPropLine> { edge }, true, owner, slots, injected);
                    }
                }
                return;
            }
            if (numElemMeshes == 0) return;
            var varSet = type.variableContainer.GetClone();
            type.FillInitialVariables(varSet, elem, midLength, numElemMeshes, injected);

            //place the props
            i = 0;
            var x = 0.0f;
            var z = 0.0f;
            var lastX = 0.0f;
            var lastZ = 0.0f;
            var mesh = elem.Str("mesh");
            var meshes = fixedMeshes ?? elem.Array<string>("meshes");
            Vector3 PlacedPosition(Vector3 point) => owner?.ProjectsPlacement == true && owner.Settings.propPlacer.projectProps ? PropPlacerGeometry.Project(owner, point) : point;
            var posDir = GH.GetPointAndDirOnSidewalk(inLine, outLine, midLine, x, z);
            type.FillSegmentVariables(varSet, i, lastX, lastZ);
            type.FillPositionVariables(varSet, posDir.dir, x, z, PathTangent(midLine, z));
            while (true) {
                type.FillSegmentVariables(varSet, i, lastX, lastZ);
                x = type.rules.xPos.GetValue(varSet);
                z = type.rules.zPos.GetValue(varSet);
                posDir = GH.GetPointAndDirOnSidewalk(inLine, outLine, midLine, x, z);
                Vector3? tangent = PathTangent(midLine, z);
                if (segments != null) {
                    var sampled = Sample(segments, x, z);
                    posDir = (sampled.pos, sampled.dir);
                    tangent = sampled.tangent;
                }
                type.FillPositionVariables(varSet, posDir.dir, x, z, tangent);
                if (!type.rules.whileCondition.GetValue(varSet)) break;
                if (i >= (owner == null ? 1000 : 100000) || owner != null && props.Count >= 100000) {
                    //A standalone placer must not silently preview fewer instances than its native export.
                    if (owner != null) throw new System.ArgumentException("Too many generated props");
                    break;
                }
                if (slots != null && type.typeData.exportSlots) slots.Add((containerType, PlacedPosition(posDir.pos), type.rules.forward.GetValue(varSet)));
                if (type.rules.ifCondition == null || type.rules.ifCondition.GetValue(varSet)) {
                    string meshName;
                    if (hasMeshList) {
                        int meshIndex = (int)type.rules.meshIndex.GetValue(varSet);
                        meshIndex = Mathf.Clamp(meshIndex, 0, meshes.Length - 1);
                        meshName = meshes[meshIndex];
                    } else {
                        meshName = mesh;
                    }
                    if (meshName != null && meshName != "") {
                        var forward = type.rules.forward.GetValue(varSet);
                        try {
                            var position = PlacedPosition(posDir.pos);
                            var rot = GameEngine.Vector3.SignedAngle(GameEngine.Vector3.forward, forward, GameEngine.Vector3.up);
                            PlaceInstance(containerType, meshName, position, new Vector3(0, rot, 0), Vector3.One,
                                type.rules.meshTransform?.GetValue(varSet) == true, type.rules.autoYOffset.GetValue(varSet), propsContainer, props);
                        }
                        catch (System.Exception e) {
                            GameEngine.Print(e.ToString());
                        }
                    }
                }
                lastX = x;
                lastZ = z;
                i++;
            }
        }
    }
    //The render mesh and picking shape use the same core placement transform.
    public static void PlaceInstance(string container, string meshName, Vector3 position, Vector3 rotation, Vector3 scale,
        bool useCoreTransform, bool autoYOffset, GameObject root, List<(string container, string meshName, GameObject obj)> props) {
        if (string.IsNullOrEmpty(meshName)) return;
        var obj = GO.CreateStaticBody(container + ";" + meshName);
        GO.SetParent(obj, root);
        GO.SetPosition(obj, position);
        GO.SetRotation(obj, rotation);
        GO.SetLocalScale(obj, scale);
        var mesh = MeshManager.GetMesh(meshName, obj);
        if (mesh == null) { GO.Destroy(obj); return; }
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
    }

}

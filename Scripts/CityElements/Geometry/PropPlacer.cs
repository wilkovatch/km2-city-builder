using System.Collections.Generic;
using System.Linq;
using Godot;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GH = GeometryHelper;

//The props of a prop rule ("propRule" of a state: a props container for each name), placed along the lines of each
//container (between a start and an end line with as many points): used by the roads and the custom elements
public static class PropPlacer {
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
    //props layer) and listed in props
    public static void Place(ObjectState rule, string[] containerNames, System.Func<string, (Vector3[] inLine, Vector3[] outLine)?> lines,
        GameObject propsContainer, List<(string container, string meshName, GameObject obj)> props) {

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
                    if (paramState != null) PlaceLane(line.Value.inLine, line.Value.outLine, paramState, containerType, propsContainer, props);
                } else {
                    var states = container.Array<ObjectState>(param.name);
                    if (states == null) continue;
                    foreach (var paramState in states) PlaceLane(line.Value.inLine, line.Value.outLine, paramState, containerType, propsContainer, props);
                }
            }
        }
    }

    public static void PlaceRibbons(ObjectState rule, List<CustomElements.GeometryPropLine> ribbons, GameObject root,
        List<(string container, string meshName, GameObject obj)> props) {
        if (rule == null) return;
        foreach (var group in ribbons.GroupBy(r => r.container)) {
            var container = rule.State(group.Key);
            if (container == null) continue;
            var type = Operations.Props.GetContainerType(container);
            if (type == null) continue;
            var segments = group.ToList();
            foreach (var param in type.parameters) {
                var elements = param.maxNumber == 1 ? new[] { container.State(param.name) } : container.Array<ObjectState>(param.name);
                foreach (var elem in elements ?? new ObjectState[0]) {
                    if (elem != null) PlaceLane(segments[0].startBound.ToArray(), segments[0].endBound.ToArray(), elem,
                        group.Key, root, props, segments);
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
        List<(string container, string meshName, GameObject obj)> props, List<CustomElements.GeometryPropLine> segments = null, bool edgeCall = false) {

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
            var type = types[typeName];
            int numElemMeshes = 1;
            if (type.typeData.maxMeshes != 1) {
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
                            new List<CustomElements.GeometryPropLine> { edge }, true);
                    }
                }
                return;
            }
            if (numElemMeshes == 0) return;
            var varSet = type.variableContainer.GetClone();
            type.FillInitialVariables(varSet, elem, midLength, numElemMeshes);

            //place the props
            i = 0;
            var x = 0.0f;
            var z = 0.0f;
            var lastX = 0.0f;
            var lastZ = 0.0f;
            var mesh = elem.Str("mesh");
            var meshes = elem.Array<string>("meshes");
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
                if (!type.rules.whileCondition.GetValue(varSet) || i >= 1000) break; //TODO: 1000 is a freeze workaround, check what a sensible max amount could be
                if (type.rules.ifCondition == null || type.rules.ifCondition.GetValue(varSet)) {
                    string meshName;
                    if (type.typeData.maxMeshes != 1) {
                        int meshIndex = (int)type.rules.meshIndex.GetValue(varSet);
                        meshIndex = Mathf.Clamp(meshIndex, 0, meshes.Length - 1);
                        meshName = meshes[meshIndex];
                    } else {
                        meshName = mesh;
                    }
                    if (meshName != null && meshName != "") {
                        var forward = type.rules.forward.GetValue(varSet);
                        try {
                            //the prop is at the placement point (which is exported), its mesh is moved from it as the rules ask
                            var propObj = GO.CreateStaticBody(containerType + ";" + meshName);
                            GO.SetParent(propObj, propsContainer);
                            GO.SetPosition(propObj, posDir.pos);
                            var rot = GameEngine.Vector3.SignedAngle(GameEngine.Vector3.forward, forward, GameEngine.Vector3.up);
                            GO.SetRotation(propObj, new Vector3(0, rot, 0));
                            var meshObj = MeshManager.GetMesh(meshName, propObj);
                            if (meshObj != null) {
                                var useCoreTransform = type.rules.meshTransform != null && type.rules.meshTransform.GetValue(varSet);
                                var meshTransform = MeshManager.GetPlacementTransform(meshName, useCoreTransform, type.rules.autoYOffset.GetValue(varSet));
                                meshTransform.ApplyTo(meshObj);
                                //Share the asset's picking shape, with the same placement offset as its mesh.
                                var shape = MeshManager.GetSelectionShape(meshName);
                                if (shape != null) {
                                    meshTransform.ApplyTo(GameEngine.Mesh.CreateShapeCollider(propObj, shape));
                                } else {
                                    var bounds = GameEngine.Mesh.GetEncapsulatingBoundsForChildMeshes(propObj);
                                    var size = GameEngine.Bounds.GetSize(bounds);
                                    if (size.LengthSquared() > 0) GameEngine.Mesh.CreateBoxCollider(propObj, size, GameEngine.Bounds.GetCenter(bounds));
                                }
                                Actions.Helpers.SetLayerRecursively(propObj, 13);
                                props.Add((containerType, meshName, propObj));
                            } else {
                                GO.Destroy(propObj);
                            }
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
}

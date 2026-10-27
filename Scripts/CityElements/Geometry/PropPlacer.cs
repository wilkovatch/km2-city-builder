using System.Collections.Generic;
using Godot;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GH = GeometryHelper;

//The props of a prop rule ("propRule" of a state: a props container for each name), placed along the lines of each
//container (between a start and an end line with as many points): used by the roads and the low-level elements
public static class PropPlacer {
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

    static void PlaceLane(Vector3[] inLine, Vector3[] outLine, ObjectState elem, string containerType, GameObject propsContainer,
        List<(string container, string meshName, GameObject obj)> props) {

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
            type.FillPositionVariables(varSet, posDir.dir, x, z);
            while (true) {
                type.FillSegmentVariables(varSet, i, lastX, lastZ);
                x = type.rules.xPos.GetValue(varSet);
                z = type.rules.zPos.GetValue(varSet);
                posDir = GH.GetPointAndDirOnSidewalk(inLine, outLine, midLine, x, z);
                type.FillPositionVariables(varSet, posDir.dir, x, z);
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
                            var propObj = MeshManager.GetMesh(meshName, propsContainer);
                            GO.SetName(propObj, containerType + ";" + meshName);
                            Actions.Helpers.SetLayerRecursively(propObj, 13);
                            GO.SetParent(propObj, propsContainer);
                            var newPos = posDir.pos + new Vector3(0, -GameEngine.Bounds.GetMin(MeshManager.GetBounds(meshName))[1], 0); //TODO: make customizable
                            GO.SetPosition(propObj, newPos);
                            var rot = GameEngine.Vector3.SignedAngle(GameEngine.Vector3.forward, forward, GameEngine.Vector3.up);
                            GO.SetRotation(propObj, new Vector3(0, rot, 0));
                            props.Add((containerType, meshName, propObj));
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

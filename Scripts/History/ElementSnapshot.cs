using System.Collections.Generic;
using Godot;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using SVector3 = States.SerializableVector3;
using SQuaternion = States.SerializableQuaternion;

namespace History {
    public enum ElementKind {
        CityProperties,
        Mesh,
        Road,
        Intersection,
        TerrainPoint,
        TerrainPatch,
        BuildingLine
    }

    //Captures single elements in the IO.SavedCity format (shared with the city encoder, with a different id resolution),
    //and recreates/updates/removes them from such a capture for undo and redo.
    public static class ElementSnapshot {
        public static ElementKind? GetKind(object element) {
            switch (element) {
                case CityProperties: return ElementKind.CityProperties;
                case MeshInstance mesh: return mesh.objectParameter == null ? ElementKind.Mesh : null;
                case Road: return ElementKind.Road;
                case Intersection: return ElementKind.Intersection;
                case TerrainPoint: return ElementKind.TerrainPoint;
                case TerrainPatch: return ElementKind.TerrainPatch;
                case BuildingLine: return ElementKind.BuildingLine;
                default: return null;
            }
        }

        static bool IsNodeAlive(Node node) {
            return node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion();
        }

        public static bool IsAlive(object element) {
            switch (element) {
                case CityProperties cp: return IsNodeAlive(cp);
                case MeshInstance mesh: return IsNodeAlive(mesh);
                case Road road: return !road.deleted && IsNodeAlive(road);
                case Intersection intersection: return !intersection.IsDeleted() && IsNodeAlive(intersection.geo);
                case TerrainPoint point: return !point.IsDeleted() && IsNodeAlive(point);
                case TerrainPatch patch: return !patch.IsDeleted() && IsNodeAlive(patch);
                case BuildingLine line: return !line.IsDeleted() && IsNodeAlive(line);
                default: return false;
            }
        }

        // Capture (the id resolvers return -1 for elements that can't be referenced)

        public static IO.SavedCity.MeshInstance CaptureMesh(MeshInstance mesh, int id) {
            var elem = new IO.SavedCity.MeshInstance();
            elem.id = id;
            elem.name = GO.GetName(mesh);
            elem.settings = mesh.settings;
            elem.position = new SVector3(mesh.GetRealPosition());
            elem.rotation = new SQuaternion(GO.GetRotationQuaternion(mesh));
            elem.scale = new SVector3(GO.GetLocalScale(mesh));
            elem.mesh = mesh.meshPath;
            return elem;
        }

        public static IO.SavedCity.Road CaptureRoad(Road road, int id, System.Func<Intersection, int> intersectionId) {
            var elem = new IO.SavedCity.Road();
            elem.id = id;
            elem.name = GO.GetName(road);
            var pointArray = new SVector3[road.points.Count];
            for (int j = 0; j < road.points.Count; j++) {
                pointArray[j] = new SVector3(GO.GetPosition(road.points[j]));
            }
            elem.points = pointArray;
            elem.instanceState = road.instanceState;
            elem.state = road.state;
            elem.startIntersectionId = road.startIntersection == null ? -1 : intersectionId(road.startIntersection);
            elem.endIntersectionId = road.endIntersection == null ? -1 : intersectionId(road.endIntersection);
            return elem;
        }

        public static IO.SavedCity.Intersection CaptureIntersection(Intersection intersection, int id, System.Func<Road, int> roadId) {
            var elem = new IO.SavedCity.Intersection();
            elem.id = id;
            elem.name = GO.GetName(intersection.geo);
            elem.state = intersection.state;
            elem.instanceState = intersection.instanceState;
            elem.center = new SVector3(GO.GetPosition(intersection.point));
            var iRoadsArray = new int[intersection.roads.Count];
            for (int j = 0; j < intersection.roads.Count; j++) {
                iRoadsArray[j] = roadId(intersection.roads[j]);
            }
            elem.roads = iRoadsArray;
            return elem;
        }

        //the id is not set, since the encoder assigns it after the points this one depends on
        public static IO.SavedCity.TerrainPoint CaptureTerrainPoint(TerrainPoint p, System.Func<Road, int> roadId,
            System.Func<Intersection, int> intersectionId, System.Func<TerrainPoint, int> pointId) {

            var newElem = new IO.SavedCity.TerrainPoint();
            newElem.anchorIndex = -1;
            newElem.elementId = -1;
            newElem.elementType = IO.SavedCity.LinkElementType.None;
            newElem.linkType = IO.SavedCity.LinkType.None;

            newElem.dividing = p.dividing;
            newElem.position = new SVector3(p.GetPoint());
            try {
                if (!GO.IsNull(p.anchor) && !p.anchor.IsDeleted()) {
                    Node3D parent = null;
                    TerrainAnchor realAnchor = null;
                    TerrainPoint realPoint1 = null, realPoint2 = null;
                    if (p.anchor is TerrainAnchor terrainAnchor && !GO.IsNull(terrainAnchor)) {
                        newElem.linkType = IO.SavedCity.LinkType.Point;
                        parent = GO.GetParent(terrainAnchor.GetObject());
                        realAnchor = terrainAnchor;
                    } else if (p.anchor is LineAnchor lineAnchor && lineAnchor != null) {
                        newElem.linkType = IO.SavedCity.LinkType.Line;
                        parent = GO.GetParent(lineAnchor.start);
                        realAnchor = GS.FindRecursive<TerrainAnchor>(lineAnchor.start);
                        if (realAnchor == null) {
                            realPoint1 = GS.FindRecursive<TerrainPoint>(lineAnchor.start);
                            realPoint2 = GS.FindRecursive<TerrainPoint>(lineAnchor.end);
                        }
                        newElem.percent = lineAnchor.percent;
                    }
                    if (realAnchor != null && parent != null) {
                        var road = GS.Cast<Road>(parent);
                        var intersection = GS.FindRecursive<Intersection.IntersectionComponent>(parent);
                        if (road != null) {
                            var idx = road.anchorManager.GetTerrainAnchors().IndexOf(realAnchor);
                            var mIdx = roadId(road);
                            if (idx >= 0 && mIdx >= 0) {
                                newElem.anchorIndex = idx;
                                newElem.elementId = mIdx;
                                newElem.elementType = IO.SavedCity.LinkElementType.Road;
                            } else {
                                newElem.linkType = IO.SavedCity.LinkType.None;
                            }
                        } else if (intersection != null) {
                            var idx = intersection.intersection.anchorManager.GetTerrainAnchors().IndexOf(realAnchor);
                            var mIdx = intersectionId(intersection.intersection);
                            if (idx >= 0 && mIdx >= 0) {
                                newElem.anchorIndex = idx;
                                newElem.elementId = mIdx;
                                newElem.elementType = IO.SavedCity.LinkElementType.Intersection;
                            } else {
                                newElem.linkType = IO.SavedCity.LinkType.None;
                            }
                        } else {
                            newElem.linkType = IO.SavedCity.LinkType.None;
                        }
                    } else if (realPoint1 != null && realPoint2 != null) { //only happens with line anchors
                        var id1 = pointId(realPoint1);
                        var id2 = pointId(realPoint2);
                        if (id1 >= 0 && id2 >= 0) {
                            newElem.elementId = id1;
                            newElem.anchorIndex = id2;
                            newElem.elementType = IO.SavedCity.LinkElementType.TerrainPoint;
                        } else {
                            newElem.linkType = IO.SavedCity.LinkType.None;
                        }
                    } else {
                        newElem.linkType = IO.SavedCity.LinkType.None;
                    }
                }
            } catch (System.Exception) {
                GameEngine.LogWarning("Skipping invalid anchor");
            }
            return newElem;
        }

        public static IO.SavedCity.TerrainPatch CaptureTerrainPatch(TerrainPatch patch, int id, System.Func<TerrainPoint, int> pointId) {
            var elem = new IO.SavedCity.TerrainPatch();
            elem.id = id;
            elem.name = GO.GetName(patch);
            elem.state = patch.state;

            var patchPerimeterPoints = patch.GetPerimeterPointsComponents();
            var elemPerimeterPoints = new int[patchPerimeterPoints.Count];
            for (int j = 0; j < patchPerimeterPoints.Count; j++) {
                elemPerimeterPoints[j] = pointId(patchPerimeterPoints[j]);
            }
            elem.perimeterPointsIds = elemPerimeterPoints;

            var patchInternalPoints = patch.GetInternalPointsComponents();
            var elemInternalPoints = new int[patchInternalPoints.Count];
            for (int j = 0; j < patchInternalPoints.Count; j++) {
                elemInternalPoints[j] = pointId(patchInternalPoints[j]);
            }
            elem.internalPointsIds = elemInternalPoints;

            var patchBorderMeshes = patch.GetTerrainBorderMeshes();
            var elemRails = new IO.SavedCity.TerrainBorderMesh[patchBorderMeshes.Count];
            for (int j = 0; j < patchBorderMeshes.Count; j++) {
                var borderMesh = new IO.SavedCity.TerrainBorderMesh();
                borderMesh.state = patchBorderMeshes[j].state;
                borderMesh.instanceState = patchBorderMeshes[j].instanceState;
                borderMesh.segmentPointsIds = new int[patchBorderMeshes[j].segment.Count];
                for (int k = 0; k < patchBorderMeshes[j].segment.Count; k++) {
                    borderMesh.segmentPointsIds[k] = pointId(patchBorderMeshes[j].segment[k]);
                }
                elemRails[j] = borderMesh;
            }
            elem.borderMeshes = elemRails;
            return elem;
        }

        public static IO.SavedCity.BuildingLine CaptureBuildingLine(BuildingLine line, int id, System.Func<TerrainPoint, int> pointId) {
            var elem = new IO.SavedCity.BuildingLine();
            elem.id = id;
            elem.name = GO.GetName(line);
            elem.state = line.state;
            elem.instanceState = line.instanceState;

            var linePoints = line.GetPointsComponents();
            var elemPoints = new int[linePoints.Count];
            for (int j = 0; j < linePoints.Count; j++) {
                elemPoints[j] = pointId(linePoints[j]);
            }
            elem.linePoints = elemPoints;

            var elemBuildings = new ObjectState[line.buildings.Count];
            for (int j = 0; j < line.buildings.Count; j++) {
                elemBuildings[j] = (ObjectState)line.buildings[j].state.Clone();
            }
            elem.buildings = elemBuildings;

            var elemSides = new ObjectState[line.buildingSides.Count];
            for (int j = 0; j < line.buildingSides.Count; j++) {
                elemSides[j] = (ObjectState)line.buildingSides[j].state.Clone();
            }
            elem.sides = elemSides;
            return elem;
        }

        // Undo mementos (the captures serialized as JSON, with registry ids as references)

        //an element being deleted is still registered (until its transaction is finalized), so that the elements
        //captured during its deletion keep referencing it
        static int RegistryId(object element) {
            return element == null ? -1 : ElementRegistry.GetId(element);
        }

        public static string Capture(object element, ElementKind kind, int id) {
            object data;
            switch (kind) {
                case ElementKind.CityProperties:
                    data = ((CityProperties)element).GetState();
                    break;
                case ElementKind.Mesh:
                    data = CaptureMesh((MeshInstance)element, id);
                    break;
                case ElementKind.Road:
                    data = CaptureRoad((Road)element, id, RegistryId);
                    break;
                case ElementKind.Intersection:
                    data = CaptureIntersection((Intersection)element, id, RegistryId);
                    break;
                case ElementKind.TerrainPoint:
                    var point = CaptureTerrainPoint((TerrainPoint)element, RegistryId, RegistryId, RegistryId);
                    point.id = id;
                    data = point;
                    break;
                case ElementKind.TerrainPatch:
                    data = CaptureTerrainPatch((TerrainPatch)element, id, RegistryId);
                    break;
                case ElementKind.BuildingLine:
                    data = CaptureBuildingLine((BuildingLine)element, id, RegistryId);
                    break;
                default:
                    return null;
            }
            return Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.None);
        }

        static T Deserialize<T>(string json) {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
        }

        static string NameOrDefault(string name, string fallback) {
            return string.IsNullOrEmpty(name) ? fallback : name;
        }

        //creates an empty element with the given id, filled later by Apply (the elements it references may not exist yet)
        public static object CreateSkeleton(ElementKind kind, int id, string json, ElementManager manager) {
            switch (kind) {
                case ElementKind.Mesh: {
                    var m = Deserialize<IO.SavedCity.MeshInstance>(json);
                    var settings = m.settings ?? new ObjectState();
                    var mesh = MeshInstance.Create(m.mesh, m.position.GetVector(), manager.GetMeshContainer(), settings);
                    if (!string.IsNullOrEmpty(m.name)) GO.SetName(mesh, m.name);
                    mesh.Initialize();
                    manager.AddMesh(mesh, id);
                    return mesh;
                }
                case ElementKind.Road: {
                    var r = Deserialize<IO.SavedCity.Road>(json);
                    var road = GS.Create<Road>(null, manager.GetRoadContainer());
                    GO.SetName(road, NameOrDefault(r.name, "Road"));
                    road.Initialize();
                    manager.AddRoad(road, id);
                    return road;
                }
                case ElementKind.Intersection: {
                    var i = Deserialize<IO.SavedCity.Intersection>(json);
                    var newObj = (StaticBody3D)GameEngine.Resources.InstantiateObject("Handle");
                    var handle = GS.Cast<Handle>(newObj);
                    handle.SetScale(GameEngine.Vector3.one * 3);
                    handle.SetHandleActive(false);
                    GO.SetPosition(newObj, i.center.GetVector());
                    var intersection = new Intersection(newObj, null);
                    GO.SetParent(intersection.geo, manager.GetRoadContainer());
                    manager.AddIntersection(intersection, id);
                    return intersection;
                }
                case ElementKind.TerrainPoint: {
                    var p = Deserialize<IO.SavedCity.TerrainPoint>(json);
                    return TerrainPoint.Create(p.position.GetVector(), null, manager.GetTerrainPointContainer(), null, id);
                }
                case ElementKind.TerrainPatch: {
                    var t = Deserialize<IO.SavedCity.TerrainPatch>(json);
                    var patch = GS.Create<TerrainPatch>();
                    GO.SetName(patch, NameOrDefault(t.name, "Terrain"));
                    patch.Initialize(manager.GetTerrainPointContainer(), manager.GetTerrainContainer());
                    manager.AddPatch(patch, id);
                    return patch;
                }
                case ElementKind.BuildingLine: {
                    var l = Deserialize<IO.SavedCity.BuildingLine>(json);
                    var line = GS.Create<BuildingLine>(null, manager.GetBuildingContainer());
                    GO.SetName(line, NameOrDefault(l.name, "Building"));
                    line.Initialize(manager.GetTerrainPointContainer());
                    manager.AddBuildingLine(line, id);
                    return line;
                }
                default:
                    return null;
            }
        }

        static T GetAlive<T>(int id) where T : class {
            if (id < 0) return null;
            var res = ElementRegistry.Get<T>(id);
            return res != null && IsAlive(res) ? res : null;
        }

        static List<TerrainPoint> GetPoints(int[] ids) {
            var res = new List<TerrainPoint>();
            if (ids == null) return res;
            foreach (var id in ids) {
                var p = GetAlive<TerrainPoint>(id);
                if (p != null && !res.Contains(p)) res.Add(p);
            }
            return res;
        }

        static ObjectState Changed(ObjectState state) {
            if (state == null) state = new ObjectState();
            state.FlagAsChanged();
            return state;
        }

        //the sub states are flagged too, like Building.SetState does
        static ObjectState ChangedBuildingState(ObjectState state) {
            state = Changed(state);
            foreach (var side in new[] { "frontState", "leftState", "rightState", "backState" }) {
                state.State(side)?.FlagAsChanged();
            }
            return state;
        }

        //updates an existing element in place, so that the references from the other elements stay valid
        public static object Apply(object element, ElementKind kind, string json, ElementManager manager) {
            switch (kind) {
                case ElementKind.CityProperties: {
                    var cp = (CityProperties)element;
                    cp.SetState(Changed(Deserialize<ObjectState>(json)));
                    return cp;
                }
                case ElementKind.Mesh: {
                    var m = Deserialize<IO.SavedCity.MeshInstance>(json);
                    var mesh = (MeshInstance)element;
                    if (mesh.meshPath != m.mesh) mesh = mesh.ReplaceMesh(m.mesh, manager);
                    if (!string.IsNullOrEmpty(m.name)) GO.SetName(mesh, m.name);
                    GO.SetPosition(mesh, m.position.GetVector());
                    GO.SetRotation(mesh, m.rotation.GetQuaternion());
                    GO.SetLocalScale(mesh, m.scale.GetVector());
                    mesh.settings = Changed(m.settings);
                    return mesh;
                }
                case ElementKind.Road: {
                    var r = Deserialize<IO.SavedCity.Road>(json);
                    var road = (Road)element;
                    if (!string.IsNullOrEmpty(r.name)) GO.SetName(road, r.name);
                    var points = new List<Vector3>();
                    if (r.points != null) {
                        foreach (var p in r.points) points.Add(p.GetVector());
                    }
                    road.RestorePoints(points);
                    road.SetState(Changed(r.state));
                    road.instanceState = Changed(r.instanceState);
                    road.startIntersection = GetAlive<Intersection>(r.startIntersectionId);
                    road.endIntersection = GetAlive<Intersection>(r.endIntersectionId);
                    road.forceRemesh = true;
                    return road;
                }
                case ElementKind.Intersection: {
                    var i = Deserialize<IO.SavedCity.Intersection>(json);
                    var intersection = (Intersection)element;
                    if (!string.IsNullOrEmpty(i.name)) GO.SetName(intersection.geo, i.name);
                    GO.SetPosition(intersection.point, i.center.GetVector());
                    intersection.state = Changed(i.state);
                    intersection.instanceState = Changed(i.instanceState);
                    intersection.roads.Clear();
                    if (i.roads != null) {
                        foreach (var roadId in i.roads) {
                            var road = GetAlive<Road>(roadId);
                            if (road != null && !intersection.roads.Contains(road)) intersection.roads.Add(road);
                        }
                    }
                    intersection.forceRemesh = true;
                    return intersection;
                }
                case ElementKind.TerrainPoint: {
                    var p = Deserialize<IO.SavedCity.TerrainPoint>(json);
                    var point = (TerrainPoint)element;
                    point.dividing = p.dividing;
                    point.anchor = ResolveAnchor(p);
                    GO.SetPosition(point.GetObject(), p.position.GetVector());
                    return point;
                }
                case ElementKind.TerrainPatch: {
                    var t = Deserialize<IO.SavedCity.TerrainPatch>(json);
                    var patch = (TerrainPatch)element;
                    if (!string.IsNullOrEmpty(t.name)) GO.SetName(patch, t.name);
                    var borderMeshes = new List<(ObjectState, ObjectState, List<TerrainPoint>)>();
                    if (t.borderMeshes != null) {
                        foreach (var bm in t.borderMeshes) {
                            borderMeshes.Add((Changed(bm.state), bm.instanceState ?? new ObjectState(), GetPoints(bm.segmentPointsIds)));
                        }
                    }
                    patch.RestoreFromSnapshot(GetPoints(t.perimeterPointsIds), GetPoints(t.internalPointsIds), borderMeshes, Changed(t.state));
                    return patch;
                }
                case ElementKind.BuildingLine: {
                    var l = Deserialize<IO.SavedCity.BuildingLine>(json);
                    var line = (BuildingLine)element;
                    if (!string.IsNullOrEmpty(l.name)) GO.SetName(line, l.name);
                    var buildingStates = new List<ObjectState>();
                    if (l.buildings != null) {
                        foreach (var b in l.buildings) buildingStates.Add(ChangedBuildingState(b));
                    }
                    var sideStates = new List<ObjectState>();
                    if (l.sides != null) {
                        foreach (var s in l.sides) sideStates.Add(Changed(s));
                    }
                    line.RestoreFromSnapshot(GetPoints(l.linePoints), Changed(l.state), Changed(l.instanceState), buildingStates, sideStates);
                    return line;
                }
                default:
                    return element;
            }
        }

        //same logic as the city decoder, with the registry ids as references
        static IAnchorable ResolveAnchor(IO.SavedCity.TerrainPoint inPoint) {
            IAnchorable anchor = null;
            if (inPoint.linkType != IO.SavedCity.LinkType.None) {
                if (inPoint.elementType != IO.SavedCity.LinkElementType.TerrainPoint) {
                    List<TerrainAnchor> anchors = null;
                    if (inPoint.elementType == IO.SavedCity.LinkElementType.Road) {
                        anchors = GetAlive<Road>(inPoint.elementId)?.anchorManager.GetTerrainAnchors();
                    } else if (inPoint.elementType == IO.SavedCity.LinkElementType.Intersection) {
                        anchors = GetAlive<Intersection>(inPoint.elementId)?.anchorManager.GetTerrainAnchors();
                    }
                    if (anchors != null) {
                        if (inPoint.anchorIndex == -1) anchor = TerrainAnchorLineManager.FindMatching(inPoint.position.GetVector(), anchors);
                        else if (inPoint.anchorIndex < anchors.Count) anchor = anchors[inPoint.anchorIndex];
                    }
                } else {
                    var linkedPoint1 = GetAlive<TerrainPoint>(inPoint.elementId);
                    var linkedPoint2 = GetAlive<TerrainPoint>(inPoint.anchorIndex);
                    if (linkedPoint1 != null && linkedPoint2 != null) {
                        anchor = new LineAnchor(linkedPoint1.GetObject(), linkedPoint2.GetObject(), inPoint.percent);
                    }
                }
            }
            if (inPoint.linkType == IO.SavedCity.LinkType.Line && anchor != null && anchor is TerrainAnchor ta) {
                anchor = GO.IsNull(ta.next) ? null : new LineAnchor(ta.GetObject(), ta.next.GetObject(), inPoint.percent);
            }
            return anchor;
        }

        //removes an element without the cascading effects of the normal deletion (the other affected elements
        //are part of the same change, and are restored explicitly)
        public static void Remove(object element, ElementKind kind) {
            switch (kind) {
                case ElementKind.Mesh:
                    ((MeshInstance)element).Delete();
                    break;
                case ElementKind.Road:
                    var road = (Road)element;
                    road.startIntersection = null;
                    road.endIntersection = null;
                    road.Delete();
                    break;
                case ElementKind.Intersection:
                    var intersection = (Intersection)element;
                    intersection.roads.Clear();
                    intersection.Delete();
                    break;
                case ElementKind.TerrainPoint:
                    ((TerrainPoint)element).Delete();
                    break;
                case ElementKind.TerrainPatch:
                    ((TerrainPatch)element).RemoveForUndo();
                    break;
                case ElementKind.BuildingLine:
                    ((BuildingLine)element).RemoveForUndo();
                    break;
            }
        }
    }
}

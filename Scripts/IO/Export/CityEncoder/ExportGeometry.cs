using System.Collections;
using System.Collections.Generic;
using Godot;
using GameObject = Godot.Node3D;
using LineRenderer = GameEngine.LineRenderer;
using Mesh = Godot.ArrayMesh;
using MeshRenderer = Godot.MeshInstance3D;
using MeshCollider = Godot.CollisionShape3D;
using CombineInstance = GameEngine.CombineInstance;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace CityEncoder {
    //The elements of the city with their generated geometry, in the format of the export JSON (read by the exporters
    //of the cores): meshes as base64 arrays, with the ids of the materials and of the meshes of the dictionaries.
    //Dispose it when done (it owns a temporary mesh object).
    public class ExportGeometry : System.IDisposable {
        //the id a road or intersection is referenced with (-1 for none)
        readonly System.Func<object, int> idOf;
        readonly Dictionary<Material, int> materialLookup = new Dictionary<Material, int>();
        readonly Dictionary<string, int> meshLookup = new Dictionary<string, int>();
        GameObject tmp;

        public readonly List<IO.ExportedCity.Material> materials = new List<IO.ExportedCity.Material>();
        public readonly List<IO.ExportedCity.DictMesh> meshes = new List<IO.ExportedCity.DictMesh>();

        public ExportGeometry(System.Func<object, int> idOf) {
            this.idOf = idOf;
            tmp = GameEngine.Mesh.CreateMeshObject(null, null);

            //Materials
            var curI = 0;
            var allMaterials = MaterialManager.GetInstance().GetAllMaterials();
            foreach (var m in allMaterials) {
                var mat = GetMaterial(m.Item1, m.Item2);
                mat.id = curI;
                materials.Add(mat);
                materialLookup[m.Item2] = curI; //needed to get the index from the material for other objects
                curI++;
            }

            //Meshes (props and instances)
            var allMeshes = MeshManager.GetInstance().GetAllMeshes();
            curI = 0;
            foreach (var m in allMeshes) {
                var mesh = new IO.ExportedCity.DictMesh();
                //the mesh bounds are in godot space (z flipped), so the z extremes swap when converted back
                var bMin = GameEngine.Bounds.GetMin(m.Item2);
                var bMax = GameEngine.Bounds.GetMax(m.Item2);
                mesh.b64v3_boundsMin = GetEncodedVector3(new Vector3(bMin.X, bMin.Y, -bMax.Z));
                mesh.b64v3_boundsMax = GetEncodedVector3(new Vector3(bMax.X, bMax.Y, -bMin.Z));
                mesh.name = m.Item1;
                meshes.Add(mesh);
                meshLookup[m.Item1] = curI; //as materialLookup
                curI++;
            }
        }

        public void Dispose() {
            if (tmp != null) GO.Destroy(tmp);
            tmp = null;
        }

        string GetEncodedInts(int[] a) {
            var data = new byte[4 + a.Length * 4];
            System.Buffer.BlockCopy(new int[1] { a.Length }, 0, data, 0, 4);
            System.Buffer.BlockCopy(a, 0, data, 4, a.Length * 4);
            return System.Convert.ToBase64String(data);
        }

        string GetEncodedFloats(float[] a) {
            var data = new byte[4 + a.Length * 4];
            System.Buffer.BlockCopy(new int[1] { a.Length }, 0, data, 0, 4);
            System.Buffer.BlockCopy(a, 0, data, 4, a.Length * 4);
            return System.Convert.ToBase64String(data);
        }

        string GetEncodedFloatsSingle(float[] a) {
            var data = new byte[a.Length * 4];
            System.Buffer.BlockCopy(a, 0, data, 0, a.Length * 4);
            return System.Convert.ToBase64String(data);
        }

        string GetEncodedVector3s(Vector3[] list) {
            var tmpList = new float[list.Length * 3];
            for (int i = 0; i < list.Length; i++) {
                tmpList[i * 3] = list[i][0];
                tmpList[i * 3 + 1] = list[i][1];
                tmpList[i * 3 + 2] = list[i][2];
            }
            return GetEncodedFloats(tmpList);
        }

        string GetEncodedVector3(Vector3 elem) {
            var tmpList = new float[3] { elem[0], elem[1], elem[2] };
            return GetEncodedFloatsSingle(tmpList);
        }

        string GetEncodedQuaternion(Quaternion elem) {
            var tmpList = new float[4] { elem[0], elem[1], elem[2], elem[3] };
            return GetEncodedFloatsSingle(tmpList);
        }

        string GetEncodedVector2s(Vector2[] list) {
            var tmpList = new float[list.Length * 2];
            for (int i = 0; i < list.Length; i++) {
                tmpList[i * 2] = list[i][0];
                tmpList[i * 2 + 1] = list[i][1];
            }
            return GetEncodedFloats(tmpList);
        }

        IO.ExportedCity.MeshData GetMeshData(Mesh m, MeshRenderer mr) {
            var res = new IO.ExportedCity.MeshData();
            res.b64v3a_vertices = GetEncodedVector3s(GameEngine.Mesh.GetVertices(m));
            res.b64v3a_normals = GetEncodedVector3s(GameEngine.Mesh.GetNormals(m));
            res.b64v2a_uvs = GetEncodedVector2s(GameEngine.Mesh.GetUVs(m));
            var nSubmeshes = GameEngine.Mesh.GetSubmeshCount(m);
            res.submeshes = new IO.ExportedCity.SubmeshData[nSubmeshes];
            for (int i = 0; i < nSubmeshes; i++) {
                var indices = GameEngine.Mesh.GetIndices(m, i);
                var subRes = new IO.ExportedCity.SubmeshData();
                subRes.b64ia_indices = GetEncodedInts(indices);
                var materials = GameEngine.Mesh.GetMaterials(mr);
                var matI = i < materials.Length ? materials[i] : null;
                if (mr != null && matI != null && materialLookup.ContainsKey(matI)) {
                    subRes.materialId = materialLookup[matI];
                }
                else {
                    subRes.materialId = -1;
                }
                res.submeshes[i] = subRes;
            }
            return res;
        }

        //elements without triangles (e.g. a terrain patch whose perimeter collapsed) are exported with an empty mesh
        void WarnIfMeshEmpty(GameObject obj, string elementType, string name) {
            var mr = GameEngine.Mesh.RetrieveMeshRenderer(obj);
            if (mr == null || GameEngine.Mesh.RetrieveMesh(mr) is not Mesh m) return;
            string reason = null;
            if (GameEngine.Mesh.GetVertices(m).Length == 0) {
                reason = "no vertices";
            } else {
                reason = "no triangles";
                for (int i = 0; i < GameEngine.Mesh.GetSubmeshCount(m); i++) {
                    if (GameEngine.Mesh.GetIndices(m, i).Length > 0) {
                        reason = null;
                        break;
                    }
                }
            }
            if (reason != null) GameEngine.LogWarning(elementType + " \"" + name + "\" has " + reason + ", its mesh will be exported empty");
        }

        IO.ExportedCity.MeshData GetMeshData(GameObject obj) {
            var mr = GameEngine.Mesh.RetrieveMeshRenderer(obj);
            if (mr == null) return null;
            var m = GameEngine.Mesh.RetrieveMesh(mr) as Mesh;
            if (m == null) return null;
            return GetMeshData(m, mr);
        }

        IO.ExportedCity.MeshData GetMeshColliderData(GameObject obj) {
            var meshes = GS.FindMultiple<MeshCollider>(obj);
            if (meshes == null || meshes.Length == 0) return null;
            var sharedMeshes = new CombineInstance[meshes.Length];
            for (int i = 0; i < meshes.Length; i++) {
                sharedMeshes[i] = new CombineInstance {
                    mesh = GameEngine.Mesh.GetColliderMesh(meshes[i]),
                    transform = GameEngine.Mesh.GetColliderTransform(meshes[i])
                };
            }
            var m = new Mesh();
            GameEngine.Mesh.CombineMeshes(m, sharedMeshes);
            var res = GetMeshData(m, null);
            GameEngine.Mesh.Destroy(m);
            return res;
        }

        IO.ExportedCity.MeshData GetMeshData(GeometryHelpers.SubMesh.SubMeshData smd) {
            var mr = GameEngine.Mesh.RetrieveMeshRenderer(tmp);
            var m = (Mesh)GameEngine.Mesh.RetrieveMesh(mr);
            GeometryHelpers.SubMesh.SetMeshData(smd, m, mr);
            return GetMeshData(tmp);
        }

        IO.ExportedCity.Material GetMaterial(string texture, Material material) {
            var mat = new IO.ExportedCity.Material();
            var matData = new ObjectState();
            matData.SetStr("texture", texture);
            //TODO: other material parameters
            mat.data = matData;
            return mat;
        }

        int IdOf(object element) {
            return element == null ? -1 : idOf(element);
        }

        // Elements

        public IO.ExportedCity.MeshInstance GetMeshInstance(MeshInstance mesh, int id) {
            var elem = new IO.ExportedCity.MeshInstance();
            elem.id = id;
            elem.name = GO.GetName(mesh);
            elem.settings = (ObjectState)mesh.settings.Clone();
            if (mesh.objectParameter != null) {
                elem.settings.SetStr("_parameterName", mesh.objectParameter.name);
                var parentObj = GO.GetParent(GO.GetParent(mesh));
                var road = GS.Cast<Road>(parentObj);
                if (road != null) {
                    elem.settings.SetStr("_parentObjectType", "Road");
                    elem.settings.SetInt("_parentObjectId", IdOf(road));
                } else {
                    //todo
                }
            }
            var reference = new IO.ExportedCity.MeshReference();
            reference.meshId = (mesh.meshPath != null && meshLookup.ContainsKey(mesh.meshPath)) ? meshLookup[mesh.meshPath] : -1;
            reference.b64v3_position = GetEncodedVector3(mesh.GetRealPosition());
            reference.b64q_rotation = GetEncodedQuaternion(GO.GetRotationQuaternion(mesh));
            reference.b64v3_scale = GetEncodedVector3(GO.GetLocalScale(mesh));
            elem.reference = reference;
            return elem;
        }

        public IO.ExportedCity.Road GetRoad(Road road, int id) {
            var elem = GetRoad(road, road.generator);
            elem.id = id;
            return elem;
        }

        IO.ExportedCity.Road GetRoad(Road road, RoadGenerator generator) {
            var elem = new IO.ExportedCity.Road();
            var baseData = new IO.ExportedCity.BaseData();
            baseData.fields = States.Utils.GetFieldsAsState(generator);
            baseData.mesh = GetMeshData(GO.GetObject(generator));
            if (road != null) {
                baseData.name = GO.GetName(road);
                var pointArray = new Vector3[road.points.Count];
                for (int j = 0; j < road.points.Count; j++) {
                    pointArray[j] = GO.GetPosition(road.points[j]);
                }
                elem.b64v3a_points = GetEncodedVector3s(pointArray);
                elem.startIntersectionId = IdOf(road.startIntersection);
                elem.endIntersectionId = IdOf(road.endIntersection);
                elem.ghost = road.ghost;

                //props and traffic lines
                var lanes = new List<ObjectState>();
                if (road.generator != null && road.generator.lanesRenderersContainer != null) {
                    var lanesTransf = road.generator.lanesRenderersContainer;
                    for (int si = 0; si < GO.GetChildCount(lanesTransf); si++) {
                        var subObj = GO.GetChildI(lanesTransf, si);
                        var subObjState = new ObjectState();
                        subObjState.SetStr("name", GO.GetName(subObj));
                        var lr = GS.Cast<LineRenderer>(subObj);
                        var points = new Vector3[lr.positionCount];
                        for (int pi = 0; pi < points.Length; pi++) {
                            points[pi] = lr.GetPosition(pi);
                        }
                        subObjState.SetStr("b64v3a_points", GetEncodedVector3s(points));
                        lanes.Add(subObjState);
                    }
                }
                baseData.fields.SetArray("lanes", lanes.ToArray());
            }
            baseData.propLines = GetPropLines(generator.props);
            elem.data = baseData;
            return elem;
        }

        //the props placed by a prop rule (see PropPlacer), by props container
        IO.ExportedCity.PropLine[] GetPropLines(List<(string container, string meshName, GameObject obj)> props) {
            var propLanes = new Dictionary<string, List<IO.ExportedCity.MeshReference>>();
            foreach (var (container, meshName, prop) in props) {
                if (!propLanes.ContainsKey(container)) propLanes[container] = new List<IO.ExportedCity.MeshReference>();
                var mr = new IO.ExportedCity.MeshReference();
                mr.meshId = meshLookup.ContainsKey(meshName) ? meshLookup[meshName] : -1;
                mr.b64v3_position = GetEncodedVector3(GO.GetPosition(prop));
                mr.b64q_rotation = GetEncodedQuaternion(GO.GetRotationQuaternion(prop));
                mr.b64v3_scale = GetEncodedVector3(GO.GetLocalScale(prop));
                propLanes[container].Add(mr);
            }
            var res = new IO.ExportedCity.PropLine[propLanes.Count];
            int laneI = 0;
            foreach (var key in propLanes.Keys) {
                var tmp = new IO.ExportedCity.PropLine();
                tmp.name = key;
                tmp.props = propLanes[key].ToArray();
                res[laneI] = tmp;
                laneI++;
            }
            return res;
        }

        public IO.ExportedCity.Intersection GetIntersection(Intersection intersection, int id) {
            var elem = new IO.ExportedCity.Intersection();
            var baseData = new IO.ExportedCity.BaseData();
            baseData.name = GO.GetName(intersection.geo);
            baseData.fields = States.Utils.GetFieldsAsState(intersection);
            baseData.fields.SetArray("partsInfo", intersection.generator.partsInfo.ToArray());
            baseData.fields.SetArray("sortOrder", intersection.generator.sortOrder.ToArray());
            baseData.mesh = GetMeshData(GO.GetObject(intersection.generator));
            elem.data = baseData;
            elem.b64v3_center = GetEncodedVector3(GO.GetPosition(intersection.point));
            var iRoadsArray = new int[intersection.roads.Count];
            for (int j = 0; j < intersection.roads.Count; j++) {
                iRoadsArray[j] = IdOf(intersection.roads[j]);
            }
            elem.roads = iRoadsArray;
            elem.id = id;
            elem.ghost = intersection.ghost;
            var roadsThrough = intersection.GetRoadsThrough();
            elem.roadsThrough = new IO.ExportedCity.Road[roadsThrough.Count];
            for (int j = 0; j < roadsThrough.Count; j++) {
                elem.roadsThrough[j] = GetRoad(null, GS.FindRecursive<RoadGenerator>(roadsThrough[j]));
            }
            return elem;
        }

        public IO.ExportedCity.TerrainPatch GetTerrainPatch(TerrainPatch patch, int id) {
            var elem = new IO.ExportedCity.TerrainPatch();
            var baseData = new IO.ExportedCity.BaseData();
            baseData.name = GO.GetName(patch);
            baseData.fields = States.Utils.GetFieldsAsState(patch);
            baseData.mesh = GetMeshData(GO.GetObject(patch.generator));
            WarnIfMeshEmpty(GO.GetObject(patch.generator), "Terrain patch", baseData.name);
            elem.data = baseData;

            var patchPerimeterPoints = patch.GetPerimeterPointsComponents();
            var elemPerimeterPoints = new Vector3[patchPerimeterPoints.Count];
            for (int j = 0; j < patchPerimeterPoints.Count; j++) {
                elemPerimeterPoints[j] = patchPerimeterPoints[j].GetPoint();
            }
            elem.b64v3a_perimeterPoints = GetEncodedVector3s(elemPerimeterPoints);

            var patchInternalPoints = patch.GetInternalPointsComponents();
            var elemInternalPoints = new Vector3[patchInternalPoints.Count];
            for (int j = 0; j < patchInternalPoints.Count; j++) {
                elemInternalPoints[j] = patchInternalPoints[j].GetPoint();
            }
            elem.b64v3a_internalPoints = GetEncodedVector3s(elemInternalPoints);

            var patchBorderMeshes = patch.GetTerrainBorderMeshes();
            var elemRails = new IO.ExportedCity.TerrainBorderMesh[patchBorderMeshes.Count];
            for (int j = 0; j < patchBorderMeshes.Count; j++) {
                var borderMesh = new IO.ExportedCity.TerrainBorderMesh();
                borderMesh.fields = States.Utils.GetFieldsAsState(patchBorderMeshes[j]);
                var tmpSegment = new Vector3[patchBorderMeshes[j].segment.Count];
                for (int k = 0; k < patchBorderMeshes[j].segment.Count; k++) {
                    tmpSegment[k] = patchBorderMeshes[j].segment[k].GetPoint();
                }
                borderMesh.b64v3a_segment = GetEncodedVector3s(tmpSegment);
                elemRails[j] = borderMesh;
            }
            elem.borderMeshes = elemRails;
            elem.id = id;
            return elem;
        }

        Newtonsoft.Json.Linq.JObject GetLowLevelPoint(TerrainPoint p, bool rotatable) {
            var res = new Newtonsoft.Json.Linq.JObject();
            res["id"] = History.ElementRegistry.GetId(p);
            var pos = p.GetPoint();
            res["position"] = new Newtonsoft.Json.Linq.JArray(pos.X, pos.Y, pos.Z);
            if (rotatable) {
                var rot = p.GetRotation();
                res["rotation"] = new Newtonsoft.Json.Linq.JArray(rot.X, rot.Y, rot.Z);
            }
            return res;
        }

        IO.ExportedCity.ElementReference GetReference(object element) {
            var kind = History.ElementSnapshot.ReferenceKind(element);
            var id = IdOf(element);
            return kind != null && id >= 0 ? new IO.ExportedCity.ElementReference { kind = kind, id = id } : null;
        }

        static Newtonsoft.Json.Linq.JToken GetLowLevelPoints(LowLevel.PointNode node, System.Func<LowLevel.PointLeaf, Newtonsoft.Json.Linq.JToken> leafValue) {
            switch (node) {
                case LowLevel.PointLeaf leaf:
                    return leafValue(leaf);
                case LowLevel.PointGroup group: {
                    var obj = new Newtonsoft.Json.Linq.JObject();
                    foreach (var (name, child) in group.children) obj[name] = GetLowLevelPoints(child, leafValue);
                    return obj;
                }
                case LowLevel.PointList list: {
                    var arr = new Newtonsoft.Json.Linq.JArray();
                    foreach (var item in list.items) arr.Add(GetLowLevelPoints(item, leafValue));
                    return arr;
                }
            }
            return null;
        }

        public IO.ExportedCity.LowLevelElement GetLowLevelElement(LowLevelElement element, int id) {
            var elem = new IO.ExportedCity.LowLevelElement();
            elem.id = id;
            elem.type = element.TypeName;
            var baseData = new IO.ExportedCity.BaseData();
            baseData.name = GO.GetName(element);
            baseData.fields = new ObjectState();
            baseData.fields.SetState("state", History.ElementSnapshot.MapReferences(element.state, (kind, refId) => {
                var target = History.ElementRegistry.Get(refId);
                return target == null ? -1 : IdOf(target);
            }));
            baseData.mesh = GetMeshData(GO.GetObject(element.generator));
            baseData.propLines = GetPropLines(element.generator.props);
            elem.data = baseData;
            elem.points = GetLowLevelPoints(element.points, leaf => GetLowLevelPoint(leaf.point, leaf.def.rotatable));
            elem.vertices = new Newtonsoft.Json.Linq.JArray();
            foreach (var v in element.meshVertices) elem.vertices.Add(GetLowLevelPoint(v, false));
            elem.triangles = element.triangles.ToArray();
            var uvMode = element.Settings?.mesh?.uvs ?? "none";
            var uvs = uvMode == "editable" ? element.uvs : uvMode == "script" ? element.scriptUVs : null;
            if (uvs != null) elem.uvs = uvs.ConvertAll(uv => new[] { uv.X, uv.Y }).ToArray();
            elem.containerId = element.container == null || element.container.IsDeleted() ? -1 : IdOf(element.container);
            elem.source = element.source != null && History.ElementSnapshot.IsAlive(element.source) ? GetReference(element.source) : null;
            return elem;
        }

        IO.ExportedCity.Facade GetFacade(BuildingSideGenerator.Facade facade, Dictionary<int, int> tmpDict) {
            var res = new IO.ExportedCity.Facade();
            res.b64f_instances = new Dictionary<int, string[][]>();
            foreach (var key0 in facade.instances.Keys) {
                var key = tmpDict[key0];
                var instance = facade.instances[key0];
                res.b64f_instances[key] = new string[instance.Count][];
                for (int i = 0; i < instance.Count; i++) {
                    res.b64f_instances[key][i] = new string[instance[i].Count];
                    for (int j = 0; j < instance[i].Count; j++) {
                        var m2 = new float[16];
                        var m = instance[i][j];
                        var mA = GameEngine.Matrix4x4.AsArray(m);
                        for (int k = 0; k < 16; k++) {
                            m2[k] = mA[k];
                        }
                        res.b64f_instances[key][i][j] = GetEncodedFloats(m2);
                    }
                }
            }
            return res;
        }

        IO.ExportedCity.BuildingSide GetSide(BuildingSideGenerator side) {
            if (side == null) return null;
            var res = new IO.ExportedCity.BuildingSide();
            var baseData = new IO.ExportedCity.BaseData();
            baseData.fields = States.Utils.GetFieldsAsState(side);
            baseData.mesh = GetMeshData(side.GetSubmesh());
            baseData.collider = GetMeshData(side.colliderMesh, null);
            res.data = baseData;
            res.meshDict = new IO.ExportedCity.MeshData[side.meshDict.Count];
            var tmpDict = new Dictionary<int, int>();
            var tmpDictInv = new Dictionary<int, int>();
            var j = 0;
            foreach (var i in side.meshDict.Keys) {
                tmpDict[j] = i;
                tmpDictInv[i] = j;
                j++;
            }
            foreach (var i in tmpDict.Keys) {
                res.meshDict[i] = GetMeshData(side.meshDict[tmpDict[i]]);
            }
            res.facades = new IO.ExportedCity.Facade[side.facades.Count];
            for (int i = 0; i < side.facades.Count; i++) {
                res.facades[i] = GetFacade(side.facades[i], tmpDictInv);
            }
            res.paramMeshes = new IO.ExportedCity.MeshData[side.finalParamMeshes.Count];
            for (int i = 0; i < side.finalParamMeshes.Count; i++) {
                res.paramMeshes[i] = GetMeshData(side.finalParamMeshes[i]);
            }
            return res;
        }

        public IO.ExportedCity.BuildingLine GetBuildingLine(BuildingLine line, int id) {
            var elem = new IO.ExportedCity.BuildingLine();
            var baseData = new IO.ExportedCity.BaseData();
            baseData.name = GO.GetName(line);
            baseData.fields = States.Utils.GetFieldsAsState(line);
            baseData.mesh = GetMeshData(GO.GetObject(line));
            WarnIfMeshEmpty(GO.GetObject(line), "Building line", baseData.name);
            baseData.collider = GetMeshColliderData(GO.GetObject(line));
            elem.data = baseData;

            var linePoints = line.GetPointsComponents();
            var elemPoints = new Vector3[linePoints.Count];
            for (int j = 0; j < linePoints.Count; j++) {
                elemPoints[j] = linePoints[j].GetPoint();
            }
            elem.b64v3a_linePoints = GetEncodedVector3s(elemPoints);

            var elemBuildings = new IO.ExportedCity.Building[line.buildings.Count];
            for (int j = 0; j < line.buildings.Count; j++) {
                var elemJ = new IO.ExportedCity.Building();
                var b = line.buildings[j];
                var baseDataJ = new IO.ExportedCity.BaseData();
                baseDataJ.fields = States.Utils.GetFieldsAsState(b);
                elemJ.data = baseDataJ;
                var tmpSpline = new Vector3[b.spline.Count];
                var tmpSplineNormals = new Vector3[b.spline.Count];
                var tmpSpline2Normals = new Vector3[b.spline.Count];
                for (int k = 0; k < b.spline.Count; k++) {
                    tmpSpline[k] = b.spline[k].point;
                    tmpSplineNormals[k] = b.spline[k].normal;
                    tmpSpline2Normals[k] = b.splineActualNormals[k];
                }
                elemJ.b64v3a_spline = GetEncodedVector3s(tmpSpline);
                elemJ.b64v3a_splineNormals = GetEncodedVector3s(tmpSplineNormals);
                elemJ.b64v3a_splineActualNormals = GetEncodedVector3s(tmpSpline2Normals);
                if (b.roof != null) elemJ.roof = GetMeshData(b.roof.GetSubmesh());
                if (b.front != null) elemJ.front = GetSide(b.front);
                if (b.left != null) elemJ.left = GetSide(b.left);
                if (b.right != null) elemJ.right = GetSide(b.right);
                if (b.back != null) elemJ.back = GetSide(b.back);
                elemBuildings[j] = elemJ;
            }
            elem.buildings = elemBuildings;

            var elemSides = new IO.ExportedCity.BuildingSide[line.buildingSides.Count];
            for (int j = 0; j < line.buildingSides.Count; j++) {
                elemSides[j] = GetSide(line.buildingSides[j]);
            }
            elem.sides = elemSides;

            if (line.roof != null) elem.roof = GetMeshData(line.roof.GetSubmesh());
            elem.id = id;
            return elem;
        }
    }
}

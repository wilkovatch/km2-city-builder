using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityEncoder {
    public class JSON_Export : CityEncoder {
        Dictionary<Material, int> materialLookup;
        Dictionary<string, int> meshLookup;
        GameObject tmp;

        public void EncodeCity(ElementManager manager, string filename, System.Action post = null) {
            manager.builder.StartCoroutine(EncodeCityCoroutine(manager, filename, post == null ? delegate {
                manager.builder.CreateAlert(StringManager.Get("SUCCESS"), StringManager.Get("CITY_EXPORTED_TEXT"), StringManager.Get("OK"));
            }
            : post));
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
                tmpList[i * 3] = list[i].x;
                tmpList[i * 3 + 1] = list[i].y;
                tmpList[i * 3 + 2] = list[i].z;
            }
            return GetEncodedFloats(tmpList);
        }

        string GetEncodedVector3(Vector3 elem) {
            var tmpList = new float[3] { elem.x, elem.y, elem.z };
            return GetEncodedFloatsSingle(tmpList);
        }

        string GetEncodedQuaternion(Quaternion elem) {
            var tmpList = new float[4] { elem.x, elem.y, elem.z, elem.w };
            return GetEncodedFloatsSingle(tmpList);
        }

        string GetEncodedVector2s(Vector2[] list) {
            var tmpList = new float[list.Length * 2];
            for (int i = 0; i < list.Length; i++) {
                tmpList[i * 2] = list[i].x;
                tmpList[i * 2 + 1] = list[i].y;
            }
            return GetEncodedFloats(tmpList);
        }

        IO.ExportedCity.MeshData GetMeshData(Mesh m,  MeshRenderer mr) {
            var res = new IO.ExportedCity.MeshData();
            res.b64v3a_vertices = GetEncodedVector3s(m.vertices);
            res.b64v3a_normals = GetEncodedVector3s(m.normals);
            res.b64v2a_uvs = GetEncodedVector2s(m.uv);
             res.submeshes = new IO.ExportedCity.SubmeshData[m.subMeshCount];
            for (int i = 0; i < m.subMeshCount; i++) {
                var indices = m.GetIndices(i);
                var subRes = new IO.ExportedCity.SubmeshData();
                subRes.b64ia_indices = GetEncodedInts(indices);
                if (mr != null && mr.sharedMaterials[i] != null && materialLookup.ContainsKey(mr.sharedMaterials[i])) {
                    subRes.materialId = materialLookup[mr.sharedMaterials[i]];
                } else {
                    subRes.materialId = -1;
                }
                res.submeshes[i] = subRes;
            }
            return res;
        }

        IO.ExportedCity.MeshData GetMeshData(GameObject obj) {
            var mesh = obj.GetComponent<MeshFilter>();
            var mr = obj.GetComponent<MeshRenderer>();
            if (mesh == null) return null;
            var m = mesh.sharedMesh;
            return GetMeshData(m, mr);
        }

        IO.ExportedCity.MeshData GetMeshColliderData(GameObject obj) {
            var mesh = obj.GetComponent<MeshCollider>();
            if (mesh == null) return null;
            var m = mesh.sharedMesh;
            return GetMeshData(m, null);
        }

        IO.ExportedCity.MeshData GetMeshData(GeometryHelpers.SubMesh.SubMeshData smd) {
            GeometryHelpers.SubMesh.SetMeshData(smd, tmp.GetComponent<MeshFilter>().sharedMesh, tmp.GetComponent<MeshRenderer>());
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

        IO.ExportedCity.Road GetRoad(ElementManager manager, Road road, RoadGenerator generator) {
            var elem = new IO.ExportedCity.Road();
            var baseData = new IO.ExportedCity.BaseData();
            baseData.fields = States.Utils.GetFieldsAsState(generator);
            baseData.mesh = GetMeshData(generator.gameObject);
            if (road != null) {
                baseData.name = road.gameObject.name;
                var pointArray = new Vector3[road.points.Count];
                for (int j = 0; j < road.points.Count; j++) {
                    pointArray[j] = road.points[j].transform.position;
                }
                elem.b64v3a_points = GetEncodedVector3s(pointArray);
                elem.startIntersectionId = manager.intersections.IndexOf(road.startIntersection);
                elem.endIntersectionId = manager.intersections.IndexOf(road.endIntersection);

                //props and traffic lines
                var lanes = new List<ObjectState>();
                if (road.generator != null && road.generator.lanesRenderersContainer != null) {
                    var lanesTransf = road.generator.lanesRenderersContainer.transform;
                    for (int si = 0; si < lanesTransf.childCount; si++) {
                        var subObj = lanesTransf.GetChild(si);
                        var subObjState = new ObjectState();
                        subObjState.SetStr("name", subObj.name);
                        var lr = subObj.GetComponent<LineRenderer>();
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
            var propLanes = new Dictionary<string, List<IO.ExportedCity.MeshReference>>();
            var pc = generator.propsContainer.transform;
            for (int pi = 0; pi < pc.childCount; pi++) {
                var prop = pc.GetChild(pi);
                var container = prop.name.Split(';')[0];
                if (!propLanes.ContainsKey(container)) propLanes[container] = new List<IO.ExportedCity.MeshReference>();
                var meshName = prop.name.Substring(container.Length + 1);
                var mr = new IO.ExportedCity.MeshReference();
                mr.meshId = meshLookup.ContainsKey(meshName) ? meshLookup[meshName] : -1;
                mr.b64v3_position = GetEncodedVector3(prop.transform.position);
                mr.b64q_rotation = GetEncodedQuaternion(prop.transform.rotation);
                mr.b64v3_scale = GetEncodedVector3(prop.transform.localScale);
                propLanes[container].Add(mr);
            }
            baseData.propLines = new IO.ExportedCity.PropLine[propLanes.Count];
            int laneI = 0;
            foreach (var key in propLanes.Keys) {
                var tmp = new IO.ExportedCity.PropLine();
                tmp.name = key;
                tmp.props = propLanes[key].ToArray();
                baseData.propLines[laneI] = tmp;
                laneI++;
            }
            elem.data = baseData;
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
                        for (int k = 0; k < 16; k++) {
                            m2[k] = m[k];
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
            baseData.collider = GetMeshData(side.mc.sharedMesh, null);
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

        IEnumerator EncodeCityCoroutine(ElementManager manager, string filename, System.Action post) {
            yield return null;
            var progressBar = manager.builder.helper.curProgressBar;
            progressBar.SetActive(true);
            progressBar.SetProgress(0);
            progressBar.SetText(StringManager.Get("EXPORTING_CITY"));
            for (int i = 0; i < 2; i++) { //once only does not make the file dialog close
                yield return new WaitForEndOfFrame();
            }

            tmp = new GameObject("temp");
            var mf = tmp.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh();
            tmp.AddComponent<MeshRenderer>();
            var savedCity = new IO.ExportedCity.ExportedCity();

            //Materials
            var materialDict = new List<IO.ExportedCity.Material>();
            materialLookup = new Dictionary<Material, int>(); //needed to get the index from the material for other objects
            var allMaterials = MaterialManager.GetInstance().GetAllMaterials();
            int curI = 0;
            foreach (var m in allMaterials) {
                var mat = GetMaterial(m.Item1, m.Item2);
                mat.id = curI;
                materialDict.Add(mat);
                materialLookup[m.Item2] = curI;
                curI++;
            }
            savedCity.materialDict = materialDict.ToArray();

            //Meshes (props and instances)
            var meshDict = new List<IO.ExportedCity.DictMesh>();
            meshLookup = new Dictionary<string, int>(); //as materialLookup
            var allMeshes = MeshManager.GetInstance().GetAllMeshes();
            curI = 0;
            foreach (var m in allMeshes) {
                var mesh = new IO.ExportedCity.DictMesh();
                mesh.b64v3_boundsMin = GetEncodedVector3(m.Item2.min);
                mesh.b64v3_boundsMax = GetEncodedVector3(m.Item2.max);
                mesh.name = m.Item1;
                meshDict.Add(mesh);
                meshLookup[m.Item1] = curI;
                curI++;
            }
            savedCity.meshDict = meshDict.ToArray();

            //Mesh instances
            var meshArray = new IO.ExportedCity.MeshInstance[manager.meshes.Count];
            for (int i = 0; i < manager.meshes.Count; i++) {
                var mesh = manager.meshes[i];
                var elem = new IO.ExportedCity.MeshInstance();
                elem.id = i;
                elem.name = mesh.gameObject.name;
                elem.settings = mesh.settings;
                var reference = new IO.ExportedCity.MeshReference();
                reference.meshId = meshLookup.ContainsKey(mesh.meshPath) ?  meshLookup[mesh.meshPath] : -1;
                reference.b64v3_position = GetEncodedVector3(mesh.GetRealPosition());
                reference.b64q_rotation = GetEncodedQuaternion(mesh.transform.rotation);
                reference.b64v3_scale = GetEncodedVector3(mesh.transform.localScale);
                elem.reference = reference;
                meshArray[i] = elem;

                if (i % 100 == 0 || i == manager.meshes.Count - 1) {
                    progressBar.SetProgress(0.05f * ((float)(i + 1) / manager.meshes.Count));
                    yield return new WaitForEndOfFrame();
                }
            }
            savedCity.meshInstances = meshArray;

            //Roads
            var roadArray = new IO.ExportedCity.Road[manager.roads.Count];
            for (int i = 0; i < manager.roads.Count; i++) {
                var road = manager.roads[i];
                var elem = GetRoad(manager, road, road.generator);
                elem.id = i;
                roadArray[i] = elem;

                if (i % 100 == 0 || i == manager.roads.Count - 1) {
                    progressBar.SetProgress(0.05f + 0.2f * ((float)(i + 1) / manager.roads.Count));
                    yield return new WaitForEndOfFrame();
                }
            }
            savedCity.roads = roadArray;

            //Intersections
            var intersectionArray = new IO.ExportedCity.Intersection[manager.intersections.Count];
            for (int i = 0; i < manager.intersections.Count; i++) {
                var intersection = manager.intersections[i];
                var elem = new IO.ExportedCity.Intersection();
                var baseData = new IO.ExportedCity.BaseData();
                baseData.name = intersection.geo.name;
                baseData.fields = States.Utils.GetFieldsAsState(intersection);
                baseData.fields.SetArray("partsInfo", intersection.generator.partsInfo.ToArray());
                baseData.fields.SetArray("sortOrder", intersection.generator.sortOrder.ToArray());
                baseData.mesh = GetMeshData(intersection.generator.gameObject);
                elem.data = baseData;
                elem.b64v3_center = GetEncodedVector3(intersection.point.transform.position);
                var iRoadsArray = new int[intersection.roads.Count];
                for (int j = 0; j < intersection.roads.Count; j++) {
                    iRoadsArray[j] = manager.roads.IndexOf(intersection.roads[j]);
                }
                elem.roads = iRoadsArray;
                elem.id = i;
                intersectionArray[i] = elem;
                var roadsThrough = intersection.GetRoadsThrough();
                elem.roadsThrough = new IO.ExportedCity.Road[roadsThrough.Count];
                for (int j = 0; j < roadsThrough.Count; j++) {
                    elem.roadsThrough[j] = GetRoad(manager, null, roadsThrough[j].GetComponent<RoadGenerator>());
                }

                if (i % 100 == 0 || i == manager.intersections.Count - 1) {
                    progressBar.SetProgress(0.25f + 0.2f * ((float)(i + 1) / manager.intersections.Count));
                    yield return new WaitForEndOfFrame();
                }
            }
            savedCity.intersections = intersectionArray;

            //Terrain patches
            var terrainArray = new IO.ExportedCity.TerrainPatch[manager.patches.Count];
            for (int i = 0; i < manager.patches.Count; i++) {
                var patch = manager.patches[i];
                var elem = new IO.ExportedCity.TerrainPatch();
                var baseData = new IO.ExportedCity.BaseData();
                baseData.name = patch.gameObject.name;
                baseData.fields = States.Utils.GetFieldsAsState(patch);
                baseData.mesh = GetMeshData(patch.generator.gameObject);
                elem.data = baseData;

                var patchPerimeterPoints = patch.GetPerimeterPointsComponents();
                var elemPerimeterPoints = new Vector3[patchPerimeterPoints.Count];
                for (int j = 0; j < patchPerimeterPoints.Count; j++) {
                    elemPerimeterPoints[j] =patchPerimeterPoints[j].GetPoint();
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
                elem.id = i;
                terrainArray[i] = elem;

                if (i % 100 == 0 || i == manager.patches.Count - 1) {
                    progressBar.SetProgress(0.45f + 0.2f * ((float)(i + 1) / manager.patches.Count));
                    yield return new WaitForEndOfFrame();
                }
            }
            savedCity.terrainPatches = terrainArray;

            //Buildings
            var linesArray = new IO.ExportedCity.BuildingLine[manager.buildings.Count];
            for (int i = 0; i < manager.buildings.Count; i++) {
                var line = manager.buildings[i];
                var elem = new IO.ExportedCity.BuildingLine();
                var baseData = new IO.ExportedCity.BaseData();
                baseData.name = line.gameObject.name;
                baseData.fields = States.Utils.GetFieldsAsState(line);
                baseData.mesh = GetMeshData(line.gameObject);
                baseData.collider = GetMeshColliderData(line.gameObject);
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
                elem.id = i;
                linesArray[i] = elem;

                if (i % 10 == 0 || i == manager.buildings.Count - 1) {
                    progressBar.SetProgress(0.65f + 0.25f * ((float)(i + 1) / manager.buildings.Count));
                    yield return new WaitForEndOfFrame();
                }
            }
            savedCity.buildingLines = linesArray;
            Object.Destroy(tmp);
            tmp = null;

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(savedCity, Newtonsoft.Json.Formatting.None);
            File.WriteAllText(filename, json);

            progressBar.SetActive(false);
            post.Invoke();
        }
    }
}

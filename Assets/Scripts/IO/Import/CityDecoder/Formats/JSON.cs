using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using SM = StringManager;

namespace CityDecoder {
    public class JSON : CityDecoder {

        IO.SavedCity.SavedCity loadedCity;
        ElementManager manager;
        string filename;
        Dictionary<int, (Intersection, IO.SavedCity.Intersection)> loadedIntersections = new Dictionary<int, (Intersection, IO.SavedCity.Intersection)>();
        Dictionary<int, (Road, IO.SavedCity.Road)> loadedRoads = new Dictionary<int, (Road, IO.SavedCity.Road)>();
        ProgressBar progressBar;

        public void DecodeCity(ElementManager manager, string filename) {
            this.manager = manager;
            this.filename = filename;
            progressBar = manager.builder.helper.curProgressBar;
            progressBar.SetText(SM.Get("LOADING_CITY"));
            manager.builder.helper.StartCoroutine(DecodeCityRoutine());
        }

        (int code, string message) Migrate(bool confirm) {
            var res = PythonManager.RunScript("migrate.py", false, new List<string> { PreferencesManager.workingDirectory + "/", "" + GlobalVariables.programVersion, "" + GlobalVariables.featureVersion, confirm ? "1" : "0" });
            return (res.code != 0 ? 2 : 1, res.data);
        }

        (int code, string message) CheckVersion() {
            //Statuses: 0=ok, 1=needs migration, 2=error
            LoadPreferences(false);
            var cores = CoreManager.GetCores();
            var core = PreferencesManager.Get("core", "");
            var coreVersion = PreferencesManager.Get("coreVersion", 0);
            var coreFeatureVersion = PreferencesManager.Get("coreFeatureVersion", 0);
            var curCoreVersion = CoreManager.GetCoreVersion(core);
            var curCoreFeatureVersion = CoreManager.GetCoreFeatureVersion(core);
            if (core == "") {
                var res = Migrate(false);
                if (res.code == 1) {
                    return (1, "MIGRATION_NEEDED_TEXT");
                } else {
                    return (2, res.message);
                }
            } else if (!cores.folders.Contains(core)) {
                return (2, "CORE_NOT_FOUND");
            } else {
                if (coreVersion != curCoreVersion) {
                    var res = Migrate(false);
                    if (res.code == 1) {
                        return (1, "MIGRATION_NEEDED_TEXT");
                    } else {
                        return (2, res.message);
                    }
                } else if (coreFeatureVersion > curCoreFeatureVersion) {
                    return (2, "MIGRATION_ERROR_CITY_IS_FOR_NEWER_CORE");
                }
            }
            return (0, null);
        }

        void MigrateCityDelayed() {
            //it has to be delayed by one frame, otherwise the error alert doesn't work
            //(since the previous one is still active)
            manager.builder.DoDelayed(MigrateCity);
        }

        void MigrateCity() {
            var res = Migrate(true);
            if (res.code == 1) {
                manager.builder.helper.StartCoroutine(DecodeCityRoutine());
            } else {
                manager.builder.CreateAlert(SM.Get("ERROR"), SM.Get(res.message), SM.Get("OK"), delegate { progressBar.SetActive(false); }, 250);
            }
        }

        void LoadPreferences(bool full = true) {
            if (PreferencesManager.workingDirectory == "") return;
            PreferencesManager.Load(full);
            manager.builder.helper.LoadOverlayTexture(PreferencesManager.Get("overlayTex", ""));
        }

        System.Collections.IEnumerator DecodeCityRoutine() {
            //setup
            manager.EraseCity();

            //check
            var checkResult = CheckVersion();
            if (checkResult.code != 0) {
                if (checkResult.code == 1) {
                    manager.builder.CreateAlert(SM.Get("MIGRATION_NEEDED_TITLE"), SM.Get(checkResult.message), SM.Get("YES"), SM.Get("NO"), MigrateCityDelayed, delegate { progressBar.SetActive(false); }, 250);
                } else {
                    manager.builder.CreateAlert(SM.Get("ERROR"), SM.Get(checkResult.message), SM.Get("OK"), delegate { progressBar.SetActive(false); }, 250);
                }
                yield break;
            }

            //check python
            var cores = CoreManager.GetCores();
            var core = PreferencesManager.Get("core", "");
            var pyRes = PythonManager.CheckPythonForCore(manager.builder, core, delegate { DecodeCity(manager, filename); });
            if (!pyRes) {
                manager.CloseCity();
                manager.builder.Reload();
                yield break;
            }

            //show progress bar
            progressBar.SetActive(true);
            yield return new WaitForEndOfFrame();
 
            //load
            LoadPreferences();
            yield return new WaitForEndOfFrame();
            var json = "";
            using (Stream fS = File.OpenRead(filename), zS = new GZipStream(fS, CompressionMode.Decompress)) {
                using (var r = new StreamReader(zS)) {
                    var readTask = r.ReadToEndAsync();
                    while (!readTask.IsCompleted) {
                        var pos = (float)fS.Position / fS.Length;
                        progressBar.SetProgress(pos * 0.1f);
                        yield return new WaitForEndOfFrame();
                    }
                    json = readTask.Result;
                }
            }
            loadedCity = Newtonsoft.Json.JsonConvert.DeserializeObject<IO.SavedCity.SavedCity>(json);

            CityGroundHelper.heightmapResolution = loadedCity.heightmapResolution;
            CityGroundHelper.terrainSize = loadedCity.terrainSize;
            if (loadedCity.heightMap != "") {
                manager.builder.helper.StartCoroutine(manager.builder.helper.SetHeightsCoroutine(GetDecodedHeights(loadedCity.heightMap), Post));
            } else {
                manager.builder.helper.StartCoroutine(manager.builder.helper.SetDefaultHeights(Post));
            }
        }

        System.Collections.IEnumerator Post() {
            if (loadedCity.meshes == null) loadedCity.meshes = new IO.SavedCity.MeshInstance[0];
            if (loadedCity.roads == null) loadedCity.roads = new IO.SavedCity.Road[0];
            if (loadedCity.intersections == null) loadedCity.intersections = new IO.SavedCity.Intersection[0];
            if (loadedCity.terrainPatches == null) loadedCity.terrainPatches = new IO.SavedCity.TerrainPatch[0];
            if (loadedCity.buildingLines == null) loadedCity.buildingLines = new IO.SavedCity.BuildingLine[0];
            if (loadedCity.terrainPoints == null) loadedCity.terrainPoints = new IO.SavedCity.TerrainPoint[0];
            if (loadedCity.genericObjects == null) loadedCity.genericObjects = new IO.SavedCity.GenericObject[0];

            float totalNum = loadedCity.meshes.Length + loadedCity.roads.Length +
                loadedCity.intersections.Length + loadedCity.terrainPatches.Length
                + loadedCity.buildingLines.Length;

            int reachedNum = 0;
            int n = 0;

            foreach (var mesh in loadedCity.meshes) {
                n++;
                var cont = manager.GetMeshContainer();
                var newMesh = MeshInstance.Create(mesh.mesh, mesh.position.GetVector(), cont, mesh.settings);
                if (mesh.name != null && mesh.name != "") newMesh.name = mesh.name;
                newMesh.Initialize();
                newMesh.transform.rotation = mesh.rotation.GetQuaternion();
                newMesh.transform.localScale = mesh.scale.GetVector();
                manager.meshes.Add(newMesh);
                if (reachedNum % 50 == 0) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    yield return new WaitForEndOfFrame();
                }
                reachedNum++;
            }
            n = 0;
            foreach (var road in loadedCity.roads) {
                n++;
                var roadObj = new GameObject();
                var roadComp = roadObj.AddComponent<Road>();
                if (road.name != null && road.name != "") roadObj.name = road.name;
                else roadObj.name = "Road " + n;
                roadComp.Initialize();
                manager.roads.Add(roadComp);
                roadObj.transform.parent = manager.GetRoadContainer().transform;
                foreach (var point in road.points) {
                    roadComp.AddPoint(point.GetVector());
                }
                roadComp.state = road.state;
                roadComp.instanceState = road.instanceState;
                loadedRoads.Add(road.id, (roadComp, road));
                if (reachedNum % 50 == 0) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    yield return new WaitForEndOfFrame();
                }
                reachedNum++;
            }
            n = 0;
            foreach (var intersection in loadedCity.intersections) {
                n++;
                var newObj = Object.Instantiate(Resources.Load<GameObject>("Handle"));
                newObj.GetComponent<Handle>().SetScale(Vector3.one * 3);
                newObj.GetComponent<MeshRenderer>().enabled = false;
                newObj.GetComponent<BoxCollider>().enabled = false;
                newObj.transform.position = intersection.center.GetVector();
                var intersectionElem = new Intersection(newObj, null);
                manager.intersections.Add(intersectionElem);
                intersectionElem.geo.transform.parent = manager.GetRoadContainer().transform;
                intersectionElem.state = intersection.state;
                if (intersection.name != null && intersection.name != "") intersectionElem.geo.name = intersection.name;
                else intersectionElem.geo.name = "Intersection " + n;
                foreach (var r in intersection.roads) {
                    var lr = loadedRoads[r];
                    intersectionElem.roads.Add(lr.Item1);
                    if (lr.Item2.startIntersectionId == intersection.id) {
                        lr.Item1.startIntersection = intersectionElem;
                    } else if (lr.Item2.endIntersectionId == intersection.id) {
                        lr.Item1.endIntersection = intersectionElem;
                    }
                }
                loadedIntersections.Add(intersection.id, (intersectionElem, intersection));
                if (reachedNum % 50 == 0) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    yield return new WaitForEndOfFrame();
                }
                reachedNum++;
            }
            manager.worldChanged = true;
            manager.ShowAnchors(false);
            manager.ShowIntersections(false);
            manager.ProcessUpdate();
            var loadedTerrainPoints = new Dictionary<int, IO.SavedCity.TerrainPoint>();
            foreach (var point in loadedCity.terrainPoints) {
                loadedTerrainPoints.Add(point.id, point);
            }
            var pointDict = new Dictionary<int, TerrainPoint>();
            n = 0;
            foreach (var patch in loadedCity.terrainPatches) {
                n++;
                var curTerrain = new GameObject();
                if (patch.name != null && patch.name != "") curTerrain.name = patch.name;
                else curTerrain.name = "Terrain " + n;
                var newPatch = curTerrain.AddComponent<TerrainPatch>();
                newPatch.Initialize(manager.GetTerrainPointContainer(), manager.GetTerrainContainer());
                foreach (var point in patch.perimeterPointsIds) {
                    var tp = GetTerrainPoint(loadedTerrainPoints[point], pointDict);
                    var res = newPatch.AddPerimeterPoint(tp.gameObject, tp.GetPoint(), true);
                    if (res == null) tp.Delete(); //duplicate point for some reason, delete it (otherwise it remains dangling)
                }
                foreach (var point in patch.internalPointsIds) {
                    var tp = GetTerrainPoint(loadedTerrainPoints[point], pointDict);
                    newPatch.AddInternalPoint(tp.gameObject, tp.GetPoint(), true);
                }
                var ri = 0;
                foreach (var borderMesh in patch.borderMeshes) {
                    newPatch.AddBorderMesh(borderMesh.state);
                    foreach (var p in borderMesh.segmentPointsIds) {
                        newPatch.AddPointToBorderMesh(ri, GetTerrainPoint(loadedTerrainPoints[p], pointDict));
                    }
                    ri++;
                }
                newPatch.state = patch.state;
                manager.patches.Add(newPatch);
                if (reachedNum % 50 == 0) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    yield return new WaitForEndOfFrame();
                }
                reachedNum++;
            }
            n = 0;
            foreach (var line in loadedCity.buildingLines) {
                n++;
                var curBuilding = new GameObject();
                if (line.name != null && line.name != "") curBuilding.name = line.name;
                else curBuilding.name = "Building " + n;
                curBuilding.transform.parent = manager.GetBuildingContainer().transform;
                var newLine = curBuilding.AddComponent<BuildingLine>();
                newLine.Initialize(manager.GetTerrainPointContainer());
                newLine.state = line.state;
                foreach (var p in line.linePoints) {
                    var tp = GetTerrainPoint(loadedTerrainPoints[p], pointDict);
                    var res = newLine.AddPoint(tp.gameObject, tp.GetPoint(), true);
                    if (res == null) tp.Delete(); //duplicate point for some reason, delete it (otherwise it remains dangling)
                }
                newLine.forcedBuildingStates = new List<ObjectState>();
                newLine.forcedSideStates = new List<ObjectState>();
                foreach (var b in line.buildings) {
                    newLine.forcedBuildingStates.Add(b);
                }
                foreach (var s in line.sides) {
                    newLine.forcedSideStates.Add(s);
                }

                manager.buildings.Add(newLine);
                if (reachedNum % 50 == 0) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    yield return new WaitForEndOfFrame();
                }
                reachedNum++;
            }

            foreach (var obj in loadedCity.genericObjects) {
                //For future use
            }
            manager.worldChanged = true;
            manager.ShowAnchors(false);
            manager.ShowIntersections(false);
            progressBar.SetActive(false);
            manager.builder.Reload();
            PresetManager.loaded = false;
            SettingsManager.Set("LastCity", filename);
        }

        TerrainPoint GetTerrainPoint(IO.SavedCity.TerrainPoint inPoint, Dictionary<int, TerrainPoint> dict) {
            if (dict.ContainsKey(inPoint.id)) {
                return dict[inPoint.id];
            } else {
                IAnchorable anchor = null;
                if (inPoint.linkType != IO.SavedCity.LinkType.None) {
                    if (inPoint.elementType != IO.SavedCity.LinkElementType.TerrainPoint) {
                        List<TerrainAnchor> anchors = new List<TerrainAnchor>();
                        if (inPoint.elementType == IO.SavedCity.LinkElementType.Road) {
                            anchors = loadedRoads[inPoint.elementId].Item1.anchorManager.GetTerrainAnchors();
                        } else if (inPoint.elementType == IO.SavedCity.LinkElementType.Intersection) {
                            anchors = loadedIntersections[inPoint.elementId].Item1.anchorManager.GetTerrainAnchors();
                        }
                        if (inPoint.anchorIndex == -1) anchor = TerrainAnchorLineManager.FindMatching(inPoint.position.GetVector(), anchors);
                        else anchor = anchors[inPoint.anchorIndex];
                    } else {
                        var linkedPoint1 = dict[inPoint.elementId];
                        var linkedPoint2 = dict[inPoint.anchorIndex];
                        foreach (var link in linkedPoint1.gameObject.transform.GetComponentsInChildren<PointLink>()) {
                            if (link.next == linkedPoint2.gameObject) {
                                anchor = new LineAnchor(linkedPoint1.gameObject, linkedPoint2.gameObject, inPoint.percent);
                                break;
                            }
                        }
                    }
                }
                if (inPoint.linkType == IO.SavedCity.LinkType.Line && anchor != null && anchor is TerrainAnchor ta) {
                    anchor = new LineAnchor(ta.gameObject, ta.next.gameObject, inPoint.percent);
                }
                var newPoint = TerrainPoint.Create(inPoint.position.GetVector(), null, manager.GetTerrainPointContainer(), anchor);
                newPoint.dividing = inPoint.dividing;
                dict[inPoint.id] = newPoint;
                return newPoint;
            }
        }

        byte[] Decompress(byte[] data) {
            var inData = new MemoryStream(data);
            var res = new MemoryStream();
            using (var stream = new DeflateStream(inData, CompressionMode.Decompress)) {
                stream.CopyTo(res);
            }
            return res.ToArray();
        }

        float[,] GetDecodedHeights(string str) {
            var compressedHeights = System.Convert.FromBase64String(str);
            var heights1D = Decompress(compressedHeights);
            var tRes = CityGroundHelper.heightmapResolution + 1;
            var heights = new float[tRes, tRes];
            System.Buffer.BlockCopy(heights1D, 0, heights, 0, heights.Length * 4);
            return heights;
        }
    }
}

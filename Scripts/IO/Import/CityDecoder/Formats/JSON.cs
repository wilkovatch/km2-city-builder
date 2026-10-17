using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Godot;
using GameObject = Godot.Node3D;
using PhysicsGameObject = Godot.StaticBody3D;
using SM = StringManager;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

//TODO: fix decoding for Godot
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
            DecodeCityRoutine(manager.builder.helper);
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
                }
                else {
                    return (2, res.message);
                }
            }
            else if (!cores.folders.Contains(core)) {
                return (2, "CORE_NOT_FOUND");
            }
            else {
                if (coreVersion != curCoreVersion) {
                    var res = Migrate(false);
                    if (res.code == 1) {
                        return (1, "MIGRATION_NEEDED_TEXT");
                    }
                    else {
                        return (2, res.message);
                    }
                }
                else if (coreFeatureVersion > curCoreFeatureVersion) {
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
                DecodeCityRoutine(manager.builder.helper);
            }
            else {
                manager.builder.CreateAlert(SM.Get("ERROR"), SM.Get(res.message), SM.Get("OK"), delegate { progressBar.SetActive(false); }, 250);
            }
        }

        void LoadPreferences(bool full = true) {
            if (PreferencesManager.workingDirectory == "") return;
            PreferencesManager.Load(full);
            manager.builder.helper.LoadOverlayTexture(PreferencesManager.Get("overlayTex", ""));
        }

        async void DecodeCityRoutine(Node caller) {
            //setup
            manager.EraseCity();
            History.UndoManager.SetLoading(true);

            //check
            var checkResult = CheckVersion();
            if (checkResult.code != 0) {
                if (checkResult.code == 1) {
                    manager.builder.CreateAlert(SM.Get("MIGRATION_NEEDED_TITLE"), SM.Get(checkResult.message), SM.Get("YES"), SM.Get("NO"), MigrateCityDelayed, delegate { progressBar.SetActive(false); }, 250);
                }
                else {
                    manager.builder.CreateAlert(SM.Get("ERROR"), SM.Get(checkResult.message), SM.Get("OK"), delegate { progressBar.SetActive(false); manager.CloseCity(); }, 250);
                }
                return;
            }

            //check python
            var cores = CoreManager.GetCores();
            var core = PreferencesManager.Get("core", "");
            var pyRes = PythonManager.CheckPythonForCore(manager.builder, core, delegate { DecodeCity(manager, filename); });
            if (!pyRes) {
                manager.CloseCity();
                manager.builder.Reload();
                return;
            }

            //show progress bar
            progressBar.SetActive(true);
            await caller.ToSignal(caller.GetTree(), "process_frame");

            //load
            LoadPreferences();
            await caller.ToSignal(caller.GetTree(), "process_frame");
            var json = "";
            using (Stream fS = File.OpenRead(filename), zS = new GZipStream(fS, CompressionMode.Decompress)) {
                using (var r = new StreamReader(zS)) {
                    var readTask = r.ReadToEndAsync();
                    while (!readTask.IsCompleted) {
                        var pos = (float)fS.Position / fS.Length;
                        progressBar.SetProgress(pos * 0.1f);
                        await caller.ToSignal(caller.GetTree(), "process_frame");
                    }
                    json = readTask.Result;
                }
            }
            loadedCity = Newtonsoft.Json.JsonConvert.DeserializeObject<IO.SavedCity.SavedCity>(json);
            if (loadedCity.maxHeight == 0.0f) loadedCity.maxHeight = 1000.0f; //default value

            CityGroundHelper.heightmapResolution = loadedCity.heightmapResolution;
            CityGroundHelper.terrainSize = loadedCity.terrainSize;
            if (loadedCity.heightMap != "") {
                manager.builder.helper.SetHeightsCoroutine(GetDecodedHeights(loadedCity.heightMap), loadedCity.maxHeight, Post, manager.builder.helper);
            }
            else {
                manager.builder.helper.SetDefaultHeights(loadedCity.maxHeight, Post, manager.builder.helper);

            }
        }

        async void Post(Node caller) {

            if (loadedCity.cityProperties == null) loadedCity.cityProperties = new ObjectState();
            if (loadedCity.meshes == null) loadedCity.meshes = new IO.SavedCity.MeshInstance[0];
            if (loadedCity.roads == null) loadedCity.roads = new IO.SavedCity.Road[0];
            if (loadedCity.intersections == null) loadedCity.intersections = new IO.SavedCity.Intersection[0];
            if (loadedCity.terrainPatches == null) loadedCity.terrainPatches = new IO.SavedCity.TerrainPatch[0];
            if (loadedCity.buildingLines == null) loadedCity.buildingLines = new IO.SavedCity.BuildingLine[0];
            if (loadedCity.terrainPoints == null) loadedCity.terrainPoints = new IO.SavedCity.TerrainPoint[0];

            float totalNum = loadedCity.meshes.Length + loadedCity.roads.Length +
                loadedCity.intersections.Length + loadedCity.terrainPatches.Length
                + loadedCity.buildingLines.Length;

            int reachedNum = 0;
            int n = 0;

            loadedCity.cityProperties.FlagAsChanged();
            manager.GetDummy<CityProperties>().SetState(loadedCity.cityProperties);

            foreach (var mesh in loadedCity.meshes) {
                n++;
                var cont = manager.GetMeshContainer();
                var newMesh = MeshInstance.Create(mesh.mesh, mesh.position.GetVector(), cont, mesh.settings);
                if (mesh.name != null && mesh.name != "") GO.SetName(newMesh, mesh.name);
                newMesh.Initialize();
                GO.SetRotation(newMesh, mesh.rotation.GetQuaternion());
                GO.SetLocalScale(newMesh, mesh.scale.GetVector());
                manager.AddMesh(newMesh);
                if (reachedNum % 50 == 0) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
                reachedNum++;
            }
            n = 0;
            foreach (var road in loadedCity.roads) {
                n++;
                var roadObj = GS.Create<Road>(null, manager.GetRoadContainer());
                var roadComp = GS.Cast<Road>(roadObj);
                if (road.name != null && road.name != "") GO.SetName(roadObj, road.name);
                else GO.SetName(roadObj, "Road " + n);
                roadComp.Initialize();
                manager.AddRoad(roadComp);
                foreach (var point in road.points) {
                    roadComp.AddPoint(point.GetVector());
                }
                roadComp.state = road.state;
                roadComp.instanceState = road.instanceState;
                loadedRoads.Add(road.id, (roadComp, road));
                if (reachedNum % 50 == 0) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
                reachedNum++;
            }
            n = 0;
            foreach (var intersection in loadedCity.intersections) {
                n++;
                var newObj = (PhysicsGameObject)GameEngine.Resources.InstantiateObject("Handle");
                var handle = GS.Cast<Handle>(newObj);
                handle.SetScale(GameEngine.Vector3.one * 3);
                handle.SetHandleActive(false);
                GO.SetPosition(newObj, intersection.center.GetVector());
                var intersectionElem = new Intersection(newObj, null);
                manager.AddIntersection(intersectionElem);
                GO.SetParent(intersectionElem.geo, manager.GetRoadContainer());
                intersectionElem.state = intersection.state;
                intersectionElem.instanceState = intersection.instanceState;
                if (intersectionElem.instanceState == null) intersectionElem.instanceState = new ObjectState(); //for backwards compatibility
                if (intersection.name != null && intersection.name != "") GO.SetName(intersectionElem.geo, intersection.name);
                else GO.SetName(intersectionElem.geo, "Intersection " + n);
                foreach (var r in intersection.roads) {
                    var lr = loadedRoads[r];
                    intersectionElem.roads.Add(lr.Item1);
                    if (lr.Item2.startIntersectionId == intersection.id) {
                        lr.Item1.startIntersection = intersectionElem;
                    }
                    else if (lr.Item2.endIntersectionId == intersection.id) {
                        lr.Item1.endIntersection = intersectionElem;
                    }
                }
                loadedIntersections.Add(intersection.id, (intersectionElem, intersection));
                if (reachedNum % 50 == 0) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
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
                var curTerrain = GS.Create<TerrainPatch>();
                if (patch.name != null && patch.name != "") GO.SetName(curTerrain, patch.name);
                else GO.SetName(curTerrain, "Terrain " + n);
                var newPatch = GS.Cast<TerrainPatch>(curTerrain);
                newPatch.Initialize(manager.GetTerrainPointContainer(), manager.GetTerrainContainer());
                foreach (var point in patch.perimeterPointsIds) {
                    var tp = GetTerrainPoint(loadedTerrainPoints[point], pointDict, loadedTerrainPoints);
                    var res = newPatch.AddPerimeterPoint(tp.GetObject(), tp.GetPoint(), true);
                    if (res == null) tp.Delete(); //duplicate point for some reason, delete it (otherwise it remains dangling)
                }
                foreach (var point in patch.internalPointsIds) {
                    var tp = GetTerrainPoint(loadedTerrainPoints[point], pointDict, loadedTerrainPoints);
                    newPatch.AddInternalPoint(tp.GetObject(), tp.GetPoint(), true);
                }
                var ri = 0;
                foreach (var borderMesh in patch.borderMeshes) {
                    newPatch.AddBorderMesh(borderMesh.state, borderMesh.instanceState);
                    foreach (var p in borderMesh.segmentPointsIds) {
                        newPatch.AddPointToBorderMesh(ri, GetTerrainPoint(loadedTerrainPoints[p], pointDict, loadedTerrainPoints));
                    }
                    ri++;
                }
                newPatch.state = patch.state;
                manager.AddPatch(newPatch);
                if (reachedNum % 50 == 0) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
                reachedNum++;
            }
            n = 0;
            foreach (var line in loadedCity.buildingLines) {
                n++;
                var curBuilding = GS.Create<BuildingLine>(null, manager.GetBuildingContainer());
                if (line.name != null && line.name != "") GO.SetName(curBuilding, line.name);
                else GO.SetName(curBuilding, "Building " + n);
                var newLine = GS.Cast<BuildingLine>(curBuilding);
                newLine.Initialize(manager.GetTerrainPointContainer());
                newLine.state = line.state;
                newLine.instanceState = line.instanceState;
                if (newLine.instanceState == null) newLine.instanceState = new ObjectState(); //for backwards compatibility
                foreach (var p in line.linePoints) {
                    var tp = GetTerrainPoint(loadedTerrainPoints[p], pointDict, loadedTerrainPoints);
                    var res = newLine.AddPoint(tp.GetObject(), tp.GetPoint(), true);
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

                manager.AddBuildingLine(newLine);
                if (reachedNum % 50 == 0) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
                reachedNum++;
            }

            manager.worldChanged = true;
            manager.ShowAnchors(false);
            manager.ShowIntersections(false);
            progressBar.SetActive(false);
            manager.builder.Reload();
            PresetManager.loaded = false;
            SettingsManager.Set("LastCity", filename);
            History.UndoManager.Clear(); //anything recorded while loading is part of the loaded city
        }

        void CreateTerrainPoint(IO.SavedCity.TerrainPoint inPoint, Dictionary<int, TerrainPoint> dict, Dictionary<int, IO.SavedCity.TerrainPoint> loadedTerrainPoints) {
            IAnchorable anchor = null;
            if (inPoint.linkType != IO.SavedCity.LinkType.None) {
                if (inPoint.elementType != IO.SavedCity.LinkElementType.TerrainPoint) {
                    List<TerrainAnchor> anchors = new List<TerrainAnchor>();
                    if (inPoint.elementType == IO.SavedCity.LinkElementType.Road) {
                        anchors = loadedRoads[inPoint.elementId].Item1.anchorManager.GetTerrainAnchors();
                    }
                    else if (inPoint.elementType == IO.SavedCity.LinkElementType.Intersection) {
                        anchors = loadedIntersections[inPoint.elementId].Item1.anchorManager.GetTerrainAnchors();
                    }
                    if (inPoint.anchorIndex == -1) anchor = TerrainAnchorLineManager.FindMatching(inPoint.position.GetVector(), anchors);
                    else anchor = anchors[inPoint.anchorIndex];
                }
                else {
                    if (!dict.ContainsKey(inPoint.elementId)) {
                        CreateTerrainPoint(loadedTerrainPoints[inPoint.elementId], dict, loadedTerrainPoints);
                    }
                    if (!dict.ContainsKey(inPoint.anchorIndex)) {
                        CreateTerrainPoint(loadedTerrainPoints[inPoint.anchorIndex], dict, loadedTerrainPoints);
                    }
                    var linkedPoint1 = dict[inPoint.elementId];
                    var linkedPoint2 = dict[inPoint.anchorIndex];
                    anchor = new LineAnchor(linkedPoint1.GetObject(), linkedPoint2.GetObject(), inPoint.percent);
                }
            }
            if (inPoint.linkType == IO.SavedCity.LinkType.Line && anchor != null && anchor is TerrainAnchor ta) {
                anchor = new LineAnchor(ta.GetObject(), ta.next.GetObject(), inPoint.percent);
            }
            var newPoint = TerrainPoint.Create(inPoint.position.GetVector(), null, manager.GetTerrainPointContainer(), anchor);
            newPoint.dividing = inPoint.dividing;
            dict[inPoint.id] = newPoint;
        }

        TerrainPoint GetTerrainPoint(IO.SavedCity.TerrainPoint inPoint, Dictionary<int, TerrainPoint> dict, Dictionary<int, IO.SavedCity.TerrainPoint> loadedTerrainPoints) {
            if (!dict.ContainsKey(inPoint.id)) {
                CreateTerrainPoint(inPoint, dict, loadedTerrainPoints);
            }
            return dict[inPoint.id];
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

using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Godot;
using GameObject = Godot.Node3D;
using PhysicsGameObject = Godot.StaticBody3D;
using SM = StringManager;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace CityDecoder {
    public class LoadOptions {
        public Operations.IInteraction interaction; //the editor popups if null
        public System.IProgress<(float value, string text)> progress;
        public bool rememberAsLastCity = true; //loaded again when the program starts
    }

    public class LoadResult {
        public bool success;
        public string error; //migration_required, migration_failed, core_not_found, city_for_newer_core, python_missing, io_error
        public string message;

        public static LoadResult Ok() {
            return new LoadResult { success = true };
        }

        public static LoadResult Fail(string error, string message) {
            return new LoadResult { success = false, error = error, message = message };
        }
    }

    public class JSON : CityDecoder {

        IO.SavedCity.SavedCity loadedCity;
        ElementManager manager;
        string filename;
        LoadOptions options;
        Dictionary<int, (Intersection, IO.SavedCity.Intersection)> loadedIntersections = new Dictionary<int, (Intersection, IO.SavedCity.Intersection)>();
        Dictionary<int, (Road, IO.SavedCity.Road)> loadedRoads = new Dictionary<int, (Road, IO.SavedCity.Road)>();
        Operations.ProgressReporter progressBar;
        const int LoadBatchMilliseconds = 50;

        public void DecodeCity(ElementManager manager, string filename) {
            Run(manager, filename, new LoadOptions());
        }

        //the editor shows the errors itself, an unexpected one is logged
        async void Run(ElementManager manager, string filename, LoadOptions options) {
            await DecodeCityAsync(manager, filename, options);
        }

        public async Task<LoadResult> DecodeCityAsync(ElementManager manager, string filename, LoadOptions options) {
            this.manager = manager;
            this.filename = filename;
            this.options = options;
            if (options.interaction == null) options.interaction = new Operations.UiInteraction(manager.builder);
            loadedIntersections.Clear();
            loadedRoads.Clear();
            progressBar = new Operations.ProgressReporter(manager.builder.helper.curProgressBar, options.progress);
            progressBar.SetText(SM.Get("LOADING_CITY"));
            try {
                return await DecodeCityRoutine(manager.builder.helper);
            } catch (System.Exception e) {
                GameEngine.LogError(e.ToString());
                progressBar.SetActive(false);
                History.UndoManager.SetLoading(false);
                await options.interaction.ShowError(SM.Get("CITY_LOAD_ERROR_TEXT") + e.Message);
                manager.CloseCity();
                manager.builder.Reload();
                return LoadResult.Fail("io_error", e.Message);
            }
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

        static string GetErrorCode(string message) {
            switch (message) {
                case "CORE_NOT_FOUND": return "core_not_found";
                case "MIGRATION_ERROR_CITY_IS_FOR_NEWER_CORE": return "city_for_newer_core";
                default: return "migration_failed";
            }
        }

        void LoadPreferences(bool full = true) {
            if (PreferencesManager.workingDirectory == "") return;
            PreferencesManager.Load(full);
            manager.builder.helper.LoadOverlayTexture(PreferencesManager.Get("overlayTex", ""));
        }

        //the city is closed when it can't be loaded
        async Task<LoadResult> Fail(string error, string message, bool show) {
            progressBar.SetActive(false);
            History.UndoManager.SetLoading(false);
            if (show) await options.interaction.ShowError(message);
            manager.CloseCity();
            manager.builder.Reload();
            return LoadResult.Fail(error, message);
        }

        async Task<LoadResult> DecodeCityRoutine(Node caller) {
            //setup
            manager.EraseCity();
            History.UndoManager.SetLoading(true);

            //check
            var checkResult = CheckVersion();
            if (checkResult.code == 1) {
                if (!await options.interaction.Confirm("MIGRATION_NEEDED_TITLE", checkResult.message)) {
                    return await Fail("migration_required", SM.Get(checkResult.message), false);
                }
                //the alert of the question must be closed before an error can be shown
                await caller.ToSignal(caller.GetTree(), "process_frame");
                var res = Migrate(true);
                if (res.code != 1) return await Fail("migration_failed", SM.GetOrKey(res.message), true);
                return await DecodeCityRoutine(caller);
            } else if (checkResult.code != 0) {
                return await Fail(GetErrorCode(checkResult.message), SM.GetOrKey(checkResult.message), true);
            }

            //check python
            var core = PreferencesManager.Get("core", "");
            var pyRes = await options.interaction.EnsurePythonForCore(core, delegate { Run(manager, filename, options); });
            if (!pyRes) {
                return await Fail("python_missing", "Python or the Python modules required by the core " + core + " are not available", false);
            }

            //show progress bar
            progressBar.SetActive(true);
            await caller.ToSignal(caller.GetTree(), "process_frame");

            //load
            LoadPreferences();
            CityElements.Types.Parsers.TypeParser.Reset();
            await caller.ToSignal(caller.GetTree(), "process_frame");
            //only managed data on the worker; the terrain and scene nodes are still created on the main thread
            var readTask = Task.Run(() => ReadCity(filename));
            while (!readTask.IsCompleted) await caller.ToSignal(caller.GetTree(), "process_frame");
            var decoded = await readTask;
            loadedCity = decoded.city;
            progressBar.SetProgress(0.1f);
            if (loadedCity.maxHeight == 0.0f) loadedCity.maxHeight = 1000.0f; //default value

            CityGroundHelper.heightmapResolution = loadedCity.heightmapResolution;
            CityGroundHelper.terrainSize = loadedCity.terrainSize;
            if (decoded.heights != null) {
                await manager.builder.helper.SetHeightsAsync(decoded.heights, loadedCity.maxHeight, progressBar);
            }
            else {
                await manager.builder.helper.SetDefaultHeightsAsync(loadedCity.maxHeight, progressBar);
            }
            progressBar.SetText(SM.Get("LOADING_CITY"));
            await caller.ToSignal(caller.GetTree(), "process_frame");
            await Post(caller);
            return LoadResult.Ok();
        }

        async Task Post(Node caller) {

            if (loadedCity.cityProperties == null) loadedCity.cityProperties = new ObjectState();
            if (loadedCity.meshes == null) loadedCity.meshes = new IO.SavedCity.MeshInstance[0];
            if (loadedCity.roads == null) loadedCity.roads = new IO.SavedCity.Road[0];
            if (loadedCity.intersections == null) loadedCity.intersections = new IO.SavedCity.Intersection[0];
            if (loadedCity.terrainPatches == null) loadedCity.terrainPatches = new IO.SavedCity.TerrainPatch[0];
            if (loadedCity.buildingLines == null) loadedCity.buildingLines = new IO.SavedCity.BuildingLine[0];
            if (loadedCity.terrainPoints == null) loadedCity.terrainPoints = new IO.SavedCity.TerrainPoint[0];
            loadedCity.customElements = (loadedCity.customElements ?? new IO.SavedCity.CustomElement[0])
                .Concat(loadedCity.lowLevelElements ?? new IO.SavedCity.CustomElement[0])
                .Concat(loadedCity.propPaths ?? new IO.SavedCity.CustomElement[0]).ToArray();
            loadedCity.lowLevelElements = null;

            float totalNum = loadedCity.meshes.Length + loadedCity.roads.Length +
                loadedCity.intersections.Length + loadedCity.terrainPatches.Length
                + loadedCity.buildingLines.Length + loadedCity.customElements.Length;

            int reachedNum = 0;
            int n = 0;
            var batchWatch = System.Diagnostics.Stopwatch.StartNew(); //yield by time: inexpensive elements can share a larger batch

            loadedCity.cityProperties.FlagAsChanged();
            manager.GetDummy<CityProperties>().SetState(loadedCity.cityProperties);

            foreach (var mesh in loadedCity.meshes) {
                n++;
                var cont = manager.GetMeshContainer();
                var newMesh = MeshInstance.Create(mesh.mesh, mesh.position.GetVector(), cont, mesh.settings, mesh.name);
                newMesh.Initialize();
                GO.SetRotation(newMesh, mesh.rotation.GetQuaternion());
                GO.SetLocalScale(newMesh, mesh.scale.GetVector());
                manager.AddMesh(newMesh);
                if (batchWatch.ElapsedMilliseconds >= LoadBatchMilliseconds) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                    batchWatch.Restart();
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
                for (int pi = 0; pi < road.points.Length; pi++) {
                    var rotation = road.pointRotations != null && pi < road.pointRotations.Length ? road.pointRotations[pi].GetVector() : GameEngine.Vector2.zero;
                    roadComp.AddPoint(road.points[pi].GetVector(), -1, rotation);
                }
                roadComp.state = road.state;
                roadComp.instanceState = road.instanceState;
                roadComp.ghost = road.ghost;
                loadedRoads.Add(road.id, (roadComp, road));
                if (batchWatch.ElapsedMilliseconds >= LoadBatchMilliseconds) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                    batchWatch.Restart();
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
                intersectionElem.ghost = intersection.ghost;
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
                if (batchWatch.ElapsedMilliseconds >= LoadBatchMilliseconds) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                    batchWatch.Restart();
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
                if (batchWatch.ElapsedMilliseconds >= LoadBatchMilliseconds) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                    batchWatch.Restart();
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
                if (batchWatch.ElapsedMilliseconds >= LoadBatchMilliseconds) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                    batchWatch.Restart();
                }
                reachedNum++;
            }

            //custom elements (their containers, sources and references once they all exist)
            var loadedCustomElements = new Dictionary<int, CustomElement>();
            var loadedPatches = manager.patches;
            n = 0;
            foreach (var e in loadedCity.customElements) {
                n++;
                var type = e.state?.Str("type", null);
                var element = manager.CreateCustomElement(type, string.IsNullOrEmpty(e.name) ? null : e.name);
                element.state = e.state ?? new ObjectState();
                var points = History.ElementSnapshot.CustomElementPoints(e.points, element.state, element,
                    token => GetTerrainPoint(loadedTerrainPoints[(int)token], pointDict, loadedTerrainPoints), Vector3.Zero);
                element.SetPoints(points);
                if (e.meshVertices != null) {
                    var vertices = new List<TerrainPoint>();
                    foreach (var p in e.meshVertices) vertices.Add(GetTerrainPoint(loadedTerrainPoints[p], pointDict, loadedTerrainPoints));
                    element.SetMesh(vertices, new List<int>(e.triangles ?? new int[0]), e.uvs != null ? States.SerializableVector2.GetRuntimeList(e.uvs) : null);
                }
                loadedCustomElements[e.id] = element;
                if (batchWatch.ElapsedMilliseconds >= LoadBatchMilliseconds) {
                    progressBar.SetProgress(0.8f + 0.2f * (reachedNum / totalNum));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                    batchWatch.Restart();
                }
                reachedNum++;
            }
            object LoadedElement(string kind, int id) {
                switch (kind) {
                    case "road": return loadedRoads.TryGetValue(id, out var r) ? r.Item1 : null;
                    case "intersection": return loadedIntersections.TryGetValue(id, out var i) ? i.Item1 : null;
                    case "terrainPatch": return id >= 0 && id < loadedPatches.Count ? loadedPatches[id] : null;
                    case "buildingLine": return id >= 0 && id < manager.buildings.Count ? manager.buildings[id] : null;
                    case "propPath":
                    case "lowLevel": //saved before the rename
                    case "customElement": return loadedCustomElements.TryGetValue(id, out var l) ? l : null;
                    default: return null;
                }
            }
            foreach (var e in loadedCity.customElements) {
                var element = loadedCustomElements[e.id];
                if (e.containerId >= 0 && loadedCustomElements.TryGetValue(e.containerId, out var container)) element.container = container;
                if (e.source.HasValue) element.source = LoadedElement(e.source.Value.kind, e.source.Value.id);
                element.state = History.ElementSnapshot.MapReferences(element.state, (kind, id) => History.ElementRegistry.GetId(LoadedElement(kind, id)));
            }

            manager.worldChanged = true;
            manager.ShowAnchors(false);
            manager.ShowIntersections(false);
            progressBar.SetActive(false);
            manager.builder.Reload();
            PresetManager.loaded = false;
            if (options.rememberAsLastCity) SettingsManager.Set("LastCity", filename);
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
                else if (inPoint.linkType == IO.SavedCity.LinkType.Point) {
                    if (!dict.ContainsKey(inPoint.elementId)) {
                        CreateTerrainPoint(loadedTerrainPoints[inPoint.elementId], dict, loadedTerrainPoints);
                    }
                    anchor = new PointAnchor(dict[inPoint.elementId]);
                }
                else if (inPoint.linkType == IO.SavedCity.LinkType.Column) {
                    if (!dict.ContainsKey(inPoint.elementId)) {
                        CreateTerrainPoint(loadedTerrainPoints[inPoint.elementId], dict, loadedTerrainPoints);
                    }
                    anchor = new ColumnAnchor(dict[inPoint.elementId], inPoint.position.GetVector().Y);
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
            var newPoint = TerrainPoint.Create(inPoint.position.GetVector(), null, manager.GetTerrainPointContainer(), anchor, active: false);
            newPoint.dividing = inPoint.dividing;
            if (inPoint.rotation.HasValue) newPoint.SetRotation(inPoint.rotation.Value.GetVector());
            dict[inPoint.id] = newPoint;
        }

        TerrainPoint GetTerrainPoint(IO.SavedCity.TerrainPoint inPoint, Dictionary<int, TerrainPoint> dict, Dictionary<int, IO.SavedCity.TerrainPoint> loadedTerrainPoints) {
            if (!dict.ContainsKey(inPoint.id)) {
                CreateTerrainPoint(inPoint, dict, loadedTerrainPoints);
            }
            return dict[inPoint.id];
        }

        static (IO.SavedCity.SavedCity city, float[,] heights) ReadCity(string filename) {
            using var file = File.OpenRead(filename);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            using var json = new Newtonsoft.Json.JsonTextReader(reader);
            var city = Newtonsoft.Json.JsonSerializer.CreateDefault().Deserialize<IO.SavedCity.SavedCity>(json);
            var heights = string.IsNullOrEmpty(city.heightMap) ? null : GetDecodedHeights(city.heightMap, city.heightmapResolution);
            return (city, heights);
        }

        static byte[] Decompress(byte[] data) {
            using var inData = new MemoryStream(data);
            using var res = new MemoryStream();
            using (var stream = new DeflateStream(inData, CompressionMode.Decompress)) {
                stream.CopyTo(res);
            }
            return res.ToArray();
        }

        static float[,] GetDecodedHeights(string str, int resolution) {
            var compressedHeights = System.Convert.FromBase64String(str);
            var heights1D = Decompress(compressedHeights);
            var tRes = resolution + 1;
            var heights = new float[tRes, tRes];
            System.Buffer.BlockCopy(heights1D, 0, heights, 0, heights.Length * 4);
            return heights;
        }
    }
}

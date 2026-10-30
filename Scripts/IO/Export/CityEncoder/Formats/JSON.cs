using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Godot;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace CityEncoder {
    public class JSON : CityEncoder {
        bool gzip;

        public JSON(bool gzip = true) {
            this.gzip = gzip;
        }

        public void EncodeCity(ElementManager manager, string filename, System.Action post = null) {
            Run(manager, filename, post);
        }

        async void Run(ElementManager manager, string filename, System.Action post) {
            var error = await EncodeCityAsync(manager, filename);
            if (error != null) {
                manager.builder.CreateAlert(StringManager.Get("ERROR"), StringManager.Get("CITY_SAVE_ERROR_TEXT") + error, StringManager.Get("OK"));
            } else if (post != null) {
                post.Invoke();
            } else {
                manager.builder.CreateAlert(StringManager.Get("SUCCESS"), StringManager.Get("CITY_SAVED_TEXT"), StringManager.Get("OK"));
            }
        }

        //writes the file, returns null on success or the error message
        public async System.Threading.Tasks.Task<string> EncodeCityAsync(ElementManager manager, string filename, System.IProgress<(float, string)> progress = null) {
            Node caller = manager.builder;
            await caller.ToSignal(caller.GetTree(), "process_frame");
            var progressBar = new Operations.ProgressReporter(manager.builder.helper.curProgressBar, progress);
            progressBar.SetActive(true);
            progressBar.SetProgress(0);
            progressBar.SetText(StringManager.Get("SAVING_CITY"));
            for (int i = 0; i < 2; i++) { //once only does not make the file dialog close
                await caller.ToSignal(caller.GetTree(), "process_frame");
            }

            var savedCity = new IO.SavedCity.SavedCity();
            try {
                savedCity.heightmapResolution = CityGroundHelper.heightmapResolution;
                savedCity.terrainSize = CityGroundHelper.terrainSize;
                savedCity.heightMap = GetEncodedHeights(manager);
                savedCity.maxHeight = CityGroundHelper.maxHeight;
            } catch (System.Exception e) {
                GameEngine.LogError(e.ToString());
                progressBar.SetActive(false);
                return e.Message;
            }

            var meshArray = new IO.SavedCity.MeshInstance[manager.meshes.Count];
            for (int i = 0; i < manager.meshes.Count; i++) {
                try {
                    meshArray[i] = History.ElementSnapshot.CaptureMesh(manager.meshes[i], i);
                } catch (System.Exception e) {
                    GameEngine.LogError(e.ToString());
                    progressBar.SetActive(false);
                    return e.Message;
                }

                if (i % 100 == 0 || i == manager.meshes.Count - 1) {
                    progressBar.SetProgress(0.05f * ((float)(i + 1) / manager.meshes.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.meshes = meshArray;

            var roadArray = new IO.SavedCity.Road[manager.roads.Count];
            for (int i = 0; i < manager.roads.Count; i++) {
                try {
                    roadArray[i] = History.ElementSnapshot.CaptureRoad(manager.roads[i], i, manager.intersections.IndexOf);
                } catch (System.Exception e) {
                    GameEngine.LogError(e.ToString());
                    progressBar.SetActive(false);
                    return e.Message;
                }

                if (i % 100 == 0 || i == manager.roads.Count - 1) {
                    progressBar.SetProgress(0.05f + 0.2f * ((float)(i + 1) / manager.roads.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.roads = roadArray;

            var intersectionArray = new IO.SavedCity.Intersection[manager.intersections.Count];
            for (int i = 0; i < manager.intersections.Count; i++) {
                try {
                    intersectionArray[i] = History.ElementSnapshot.CaptureIntersection(manager.intersections[i], i, manager.roads.IndexOf);
                } catch (System.Exception e) {
                    GameEngine.LogError(e.ToString());
                    progressBar.SetActive(false);
                    return e.Message;
                }

                if (i % 100 == 0 || i == manager.intersections.Count - 1) {
                    progressBar.SetProgress(0.25f + 0.2f * ((float)(i + 1) / manager.intersections.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.intersections = intersectionArray;

            var pointsList = new Dictionary<TerrainPoint, (int, IO.SavedCity.TerrainPoint)>();

            var terrainArray = new IO.SavedCity.TerrainPatch[manager.patches.Count];
            for (int i = 0; i < manager.patches.Count; i++) {
                try {
                    terrainArray[i] = History.ElementSnapshot.CaptureTerrainPatch(manager.patches[i], i, p => GetTerrainPoint(p, pointsList, manager));
                } catch (System.Exception e) {
                    GameEngine.LogError(e.ToString());
                    progressBar.SetActive(false);
                    return e.Message;
                }

                if (i % 100 == 0 || i == manager.patches.Count - 1) {
                    progressBar.SetProgress(0.45f + 0.2f * ((float)(i + 1) / manager.patches.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.terrainPatches = terrainArray;

            var linesArray = new IO.SavedCity.BuildingLine[manager.buildings.Count];
            for (int i = 0; i < manager.buildings.Count; i++) {
                try {
                    linesArray[i] = History.ElementSnapshot.CaptureBuildingLine(manager.buildings[i], i, p => GetTerrainPoint(p, pointsList, manager));
                } catch (System.Exception e) {
                    GameEngine.LogError(e.ToString());
                    progressBar.SetActive(false);
                    return e.Message;
                }

                if (i % 10 == 0 || i == manager.buildings.Count - 1) {
                    progressBar.SetProgress(0.65f + 0.25f * ((float)(i + 1) / manager.buildings.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.buildingLines = linesArray;

            //the elements are referenced by their index in their list
            int IndexOf(object element) {
                switch (element) {
                    case Road r: return manager.roads.IndexOf(r);
                    case Intersection i: return manager.intersections.IndexOf(i);
                    case TerrainPatch p: return manager.patches.IndexOf(p);
                    case BuildingLine l: return manager.buildings.IndexOf(l);
                    case CustomElement e: return manager.customElements.IndexOf(e);
                    default: return -1;
                }
            }
            var customElementArray = new IO.SavedCity.CustomElement[manager.customElements.Count];
            for (int i = 0; i < manager.customElements.Count; i++) {
                try {
                    customElementArray[i] = History.ElementSnapshot.CaptureCustomElement(manager.customElements[i], i, p => GetTerrainPoint(p, pointsList, manager),
                        IndexOf, (kind, id) => IndexOf(History.ElementRegistry.Get(id)));
                } catch (System.Exception e) {
                    GameEngine.LogError(e.ToString());
                    progressBar.SetActive(false);
                    return e.Message;
                }

                if (i % 100 == 0 || i == manager.customElements.Count - 1) {
                    progressBar.SetProgress(0.9f + 0.05f * ((float)(i + 1) / manager.customElements.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.customElements = System.Array.FindAll(customElementArray, e => e.state.Str("type", "") != PropPath.TypeId);
            savedCity.propPaths = System.Array.FindAll(customElementArray, e => e.state.Str("type", "") == PropPath.TypeId);

            try {
                savedCity.terrainPoints = GetTerrainPointsArray(pointsList);

                savedCity.cityProperties = manager.GetDummy<CityProperties>().GetState();

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(savedCity, Newtonsoft.Json.Formatting.Indented);
                if (gzip) {
                    var bytes = System.Text.Encoding.ASCII.GetBytes(json);
                    var stream = new MemoryStream(bytes);
                    using (var compressedFileStream = File.Create(filename + ".gz.temp")) {
                        using (var compressor = new GZipStream(compressedFileStream, CompressionMode.Compress)) {
                            stream.CopyTo(compressor);
                        }
                    }

                    //backup management
                    if (File.Exists(filename + ".gz.backup")) File.Move(filename + ".gz.backup", filename + ".gz.backup.temp");
                    if (File.Exists(filename + ".gz")) File.Move(filename + ".gz", filename + ".gz.backup");
                    if (File.Exists(filename + ".gz.backup.temp")) File.Delete(filename + ".gz.backup.temp");
                    File.Move(filename + ".gz.temp", filename + ".gz");
                } else {
                    File.WriteAllText(filename, json);
                }
            } catch (System.Exception e) {
                GameEngine.LogError(e.ToString());
                progressBar.SetActive(false);
                return e.Message;
            }

            progressBar.SetActive(false);
            return null;
        }

        IO.SavedCity.TerrainPoint[] GetTerrainPointsArray(Dictionary<TerrainPoint, (int, IO.SavedCity.TerrainPoint)> dict) {
            var auxDict = new Dictionary<int, IO.SavedCity.TerrainPoint>();
            foreach (var elem in dict) {
                auxDict[elem.Value.Item1] = elem.Value.Item2;
            }
            var res = new IO.SavedCity.TerrainPoint[dict.Keys.Count];
            for (int i = 0; i < res.Length; i++) {
                res[i] = auxDict[i];
            }
            return res;
        }

        void AddTerrainPointToDict(TerrainPoint p, Dictionary<TerrainPoint, (int, IO.SavedCity.TerrainPoint)> dict, ElementManager manager) {
            //the points referenced by line anchors are added first
            var newElem = History.ElementSnapshot.CaptureTerrainPoint(p, manager.roads.IndexOf, manager.intersections.IndexOf, x => GetTerrainPoint(x, dict, manager));
            newElem.id = dict.Keys.Count;
            dict[p] = (dict.Keys.Count, newElem);
        }

        int GetTerrainPoint(TerrainPoint p, Dictionary<TerrainPoint, (int, IO.SavedCity.TerrainPoint)> dict, ElementManager manager) {
            var exists = dict.ContainsKey(p);
            if (!exists) {
                AddTerrainPointToDict(p, dict, manager);
            }
            return dict[p].Item1;
        }

        string GetEncodedHeights(ElementManager manager) {
            var heights1D = GameEngine.Terrain.GetHeightBytes(manager.builder.helper.terrainObj);
            var compressedHeights = Compress(heights1D);
            return System.Convert.ToBase64String(compressedHeights);
        }

        byte[] Compress(byte[] data) {
            var res = new MemoryStream();
            using (var stream = new DeflateStream(res, System.IO.Compression.CompressionLevel.Optimal)) {
                stream.Write(data, 0, data.Length);
            }
            return res.ToArray();
        }
    }
}

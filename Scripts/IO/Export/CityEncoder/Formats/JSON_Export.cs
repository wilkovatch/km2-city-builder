using System.Collections;
using System.Collections.Generic;
using System.IO;
using Godot;
using GO = GameEngine.GameObject;

namespace CityEncoder {
    public class JSON_Export : CityEncoder {
        //where the exporter reading the JSON writes its warnings and errors (null: not written for an exporter)
        public IO.ExportedCity.ExportReportInfo report;

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
                manager.builder.CreateAlert(StringManager.Get("SUCCESS"), StringManager.Get("CITY_EXPORTED_TEXT"), StringManager.Get("OK"));
            }
        }

        //writes the file, returns null on success or the error message
        public async System.Threading.Tasks.Task<string> EncodeCityAsync(ElementManager manager, string filename, System.IProgress<(float, string)> progress = null) {
            Node caller = manager.builder;
            await caller.ToSignal(caller.GetTree(), "process_frame");
            await manager.SettleProjectedSplines();
            var progressBar = new Operations.ProgressReporter(manager.builder.helper.curProgressBar, progress);
            progressBar.SetActive(true);
            progressBar.SetProgress(0);
            progressBar.SetText(StringManager.Get("EXPORTING_CITY"));
            for (int i = 0; i < 2; i++) { //once only does not make the file dialog close
                await caller.ToSignal(caller.GetTree(), "process_frame");
            }

            //the roads and intersections are referenced by their index
            ExportGeometry geometry = null;
            string Fail(System.Exception e) {
                GameEngine.LogError(e.ToString());
                progressBar.SetActive(false);
                geometry?.Dispose();
                return e.Message;
            }
            var savedCity = new IO.ExportedCity.ExportedCity();

            //Materials and meshes (props and instances)
            try {
                geometry = new ExportGeometry(x => {
                    switch (x) {
                        case Road r: return manager.roads.IndexOf(r);
                        case Intersection i: return manager.intersections.IndexOf(i);
                        case TerrainPatch p: return manager.patches.IndexOf(p);
                        case BuildingLine l: return manager.buildings.IndexOf(l);
                        case CustomElement e: return manager.customElements.IndexOf(e);
                        default: return -1;
                    }
                });
                savedCity.materialDict = geometry.materials.ToArray();
                savedCity.meshDict = geometry.meshes.ToArray();
            } catch (System.Exception e) {
                return Fail(e);
            }

            //Mesh instances
            var actualMeshes = new List<MeshInstance>(manager.meshes); //we need to add parametric meshes too
            foreach (var road in manager.roads) {
                actualMeshes.AddRange(road.GetParametricObjects());
            }
            var meshArray = new IO.ExportedCity.MeshInstance[actualMeshes.Count];
            for (int i = 0; i < actualMeshes.Count; i++) {
                try {
                    meshArray[i] = geometry.GetMeshInstance(actualMeshes[i], i);
                } catch (System.Exception e) {
                    return Fail(e);
                }

                if (i % 100 == 0 || i == actualMeshes.Count - 1) {
                    progressBar.SetProgress(0.05f * ((float)(i + 1) / actualMeshes.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.meshInstances = meshArray;

            //Roads
            var roadArray = new IO.ExportedCity.Road[manager.roads.Count];
            for (int i = 0; i < manager.roads.Count; i++) {
                try {
                    roadArray[i] = geometry.GetRoad(manager.roads[i], i);
                } catch (System.Exception e) {
                    return Fail(e);
                }

                if (i % 100 == 0 || i == manager.roads.Count - 1) {
                    progressBar.SetProgress(0.05f + 0.2f * ((float)(i + 1) / manager.roads.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.roads = roadArray;

            //Intersections
            var intersectionArray = new IO.ExportedCity.Intersection[manager.intersections.Count];
            for (int i = 0; i < manager.intersections.Count; i++) {
                try {
                    intersectionArray[i] = geometry.GetIntersection(manager.intersections[i], i);
                } catch (System.Exception e) {
                    return Fail(e);
                }

                if (i % 100 == 0 || i == manager.intersections.Count - 1) {
                    progressBar.SetProgress(0.25f + 0.2f * ((float)(i + 1) / manager.intersections.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.intersections = intersectionArray;

            //Terrain patches
            var terrainArray = new IO.ExportedCity.TerrainPatch[manager.patches.Count];
            for (int i = 0; i < manager.patches.Count; i++) {
                try {
                    terrainArray[i] = geometry.GetTerrainPatch(manager.patches[i], i);
                } catch (System.Exception e) {
                    return Fail(e);
                }

                if (i % 100 == 0 || i == manager.patches.Count - 1) {
                    progressBar.SetProgress(0.45f + 0.2f * ((float)(i + 1) / manager.patches.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.terrainPatches = terrainArray;

            //Buildings
            var linesArray = new IO.ExportedCity.BuildingLine[manager.buildings.Count];
            for (int i = 0; i < manager.buildings.Count; i++) {
                try {
                    linesArray[i] = geometry.GetBuildingLine(manager.buildings[i], i);
                } catch (System.Exception e) {
                    return Fail(e);
                }

                if (i % 10 == 0 || i == manager.buildings.Count - 1) {
                    progressBar.SetProgress(0.65f + 0.25f * ((float)(i + 1) / manager.buildings.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            //Custom elements
            var customElementArray = new IO.ExportedCity.CustomElement[manager.customElements.Count];
            for (int i = 0; i < manager.customElements.Count; i++) {
                try {
                    customElementArray[i] = geometry.GetCustomElement(manager.customElements[i], i);
                } catch (System.Exception e) {
                    return Fail(e);
                }

                if (i % 100 == 0 || i == manager.customElements.Count - 1) {
                    progressBar.SetProgress(0.9f + 0.05f * ((float)(i + 1) / manager.customElements.Count));
                    await caller.ToSignal(caller.GetTree(), "process_frame");
                }
            }
            savedCity.customElements = System.Array.FindAll(customElementArray, e => e.type != PropPath.TypeId);
            savedCity.propPaths = System.Array.FindAll(customElementArray, e => e.type == PropPath.TypeId);

            try {
                savedCity.buildingLines = linesArray;

                savedCity.cityProperties = manager.GetDummy<CityProperties>().GetState();

                var ancestries = new List<IO.ExportedCity.TypeAncestry>();
                foreach (var (category, name, ancestry) in CityElements.Types.Parsers.TypeParser.GetAllAncestries()) {
                    ancestries.Add(new IO.ExportedCity.TypeAncestry { category = category, name = name, ancestry = ancestry });
                }
                savedCity.typeAncestry = ancestries.ToArray();

                savedCity.cityFolder = PreferencesManager.workingDirectory;
                savedCity.report = report;

                geometry.Dispose();

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(savedCity, Newtonsoft.Json.Formatting.None);
                File.WriteAllText(filename, json);
            } catch (System.Exception e) {
                return Fail(e);
            }

            progressBar.SetActive(false);
            return null;
        }
    }
}

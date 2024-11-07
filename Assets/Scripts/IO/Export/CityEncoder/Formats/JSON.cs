using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace CityEncoder {
    public class JSON : CityEncoder {
        bool gzip;

        public JSON(bool gzip = true) {
            this.gzip = gzip;
        }

        public void EncodeCity(ElementManager manager, string filename, System.Action post = null) {
            manager.builder.StartCoroutine(EncodeCityCoroutine(manager, filename, post == null ? delegate {
                manager.builder.CreateAlert(StringManager.Get("SUCCESS"), StringManager.Get("CITY_SAVED_TEXT"), StringManager.Get("OK"));
            } : post));
        }

        IEnumerator EncodeCityCoroutine(ElementManager manager, string filename, System.Action post) {
            yield return null;
            var progressBar = manager.builder.helper.curProgressBar;
            progressBar.SetActive(true);
            progressBar.SetProgress(0);
            progressBar.SetText(StringManager.Get("SAVING_CITY"));
            for (int i = 0; i < 2; i++) { //once only does not make the file dialog close
                yield return new WaitForEndOfFrame();
            }

            var savedCity = new IO.SavedCity.SavedCity();
            savedCity.heightmapResolution = CityGroundHelper.heightmapResolution;
            savedCity.terrainSize = CityGroundHelper.terrainSize;
            savedCity.heightMap = GetEncodedHeights(manager);
            savedCity.maxHeight = CityGroundHelper.maxHeight;

            var meshArray = new IO.SavedCity.MeshInstance[manager.meshes.Count];
            for (int i = 0; i < manager.meshes.Count; i++) {
                var mesh = manager.meshes[i];
                var elem = new IO.SavedCity.MeshInstance();
                elem.id = i;
                elem.name = mesh.gameObject.name;
                elem.settings = mesh.settings;
                elem.position = new States.SerializableVector3(mesh.GetRealPosition());
                elem.rotation = new States.SerializableQuaternion(mesh.transform.rotation);
                elem.scale = new States.SerializableVector3(mesh.transform.localScale);
                elem.mesh = mesh.meshPath;
                meshArray[i] = elem;

                if (i % 100 == 0 || i == manager.meshes.Count - 1) {
                    progressBar.SetProgress(0.05f * ((float)(i + 1) / manager.meshes.Count));
                    yield return new WaitForEndOfFrame();
                }
            }
            savedCity.meshes = meshArray;

            var roadArray = new IO.SavedCity.Road[manager.roads.Count];
            for (int i = 0; i < manager.roads.Count; i++) {
                var road = manager.roads[i];
                var elem = new IO.SavedCity.Road();
                elem.id = i;
                elem.name = road.gameObject.name;
                var pointArray = new States.SerializableVector3[road.points.Count];
                for (int j = 0; j < road.points.Count; j++) {
                    pointArray[j] = new States.SerializableVector3(road.points[j].transform.position);
                }
                elem.points = pointArray;
                elem.instanceState = road.instanceState;
                elem.state = road.state;
                elem.startIntersectionId = manager.intersections.IndexOf(road.startIntersection);
                elem.endIntersectionId = manager.intersections.IndexOf(road.endIntersection);
                roadArray[i] = elem;

                if (i % 100 == 0 || i == manager.roads.Count - 1) {
                    progressBar.SetProgress(0.05f + 0.2f * ((float)(i + 1) / manager.roads.Count));
                    yield return new WaitForEndOfFrame();
                }
            }
            savedCity.roads = roadArray;

            var intersectionArray = new IO.SavedCity.Intersection[manager.intersections.Count];
            for (int i = 0; i < manager.intersections.Count; i++) {
                var intersection = manager.intersections[i];
                var elem = new IO.SavedCity.Intersection();
                elem.id = i;
                elem.name = intersection.geo.name;
                elem.state = intersection.state;
                elem.instanceState = intersection.instanceState;
                elem.center = new States.SerializableVector3(intersection.point.transform.position);
                var iRoadsArray = new int[intersection.roads.Count];
                for (int j = 0; j < intersection.roads.Count; j++) {
                    iRoadsArray[j] = manager.roads.IndexOf(intersection.roads[j]);
                }
                elem.roads = iRoadsArray;
                intersectionArray[i] = elem;

                if (i % 100 == 0 || i == manager.intersections.Count - 1) {
                    progressBar.SetProgress(0.25f + 0.2f * ((float)(i + 1) / manager.intersections.Count));
                    yield return new WaitForEndOfFrame();
                }
            }
            savedCity.intersections = intersectionArray;

            var pointsList = new Dictionary<TerrainPoint, (int, IO.SavedCity.TerrainPoint)>();

            var terrainArray = new IO.SavedCity.TerrainPatch[manager.patches.Count];
            for (int i = 0; i < manager.patches.Count; i++) {
                var patch = manager.patches[i];
                var elem = new IO.SavedCity.TerrainPatch();
                elem.id = i;
                elem.name = patch.gameObject.name;
                elem.state = patch.state;

                var patchPerimeterPoints = patch.GetPerimeterPointsComponents();
                var elemPerimeterPoints = new int[patchPerimeterPoints.Count];
                for (int j = 0; j < patchPerimeterPoints.Count; j++) {
                    elemPerimeterPoints[j] = GetTerrainPoint(patchPerimeterPoints[j], pointsList, manager);
                }
                elem.perimeterPointsIds = elemPerimeterPoints;

                var patchInternalPoints = patch.GetInternalPointsComponents();
                var elemInternalPoints = new int[patchInternalPoints.Count];
                for (int j = 0; j < patchInternalPoints.Count; j++) {
                    elemInternalPoints[j] = GetTerrainPoint(patchInternalPoints[j], pointsList, manager);
                }
                elem.internalPointsIds = elemInternalPoints;

                var patchBorderMeshes = patch.GetTerrainBorderMeshes();
                var elemRails = new IO.SavedCity.TerrainBorderMesh[patchBorderMeshes.Count];
                for (int j = 0; j < patchBorderMeshes.Count; j++) {
                    var borderMesh = new IO.SavedCity.TerrainBorderMesh();
                    borderMesh.state = patchBorderMeshes[j].state;
                    borderMesh.segmentPointsIds = new int[patchBorderMeshes[j].segment.Count];
                    for (int k = 0; k < patchBorderMeshes[j].segment.Count; k++) {
                        borderMesh.segmentPointsIds[k] = GetTerrainPoint(patchBorderMeshes[j].segment[k], pointsList, manager);
                    }
                    elemRails[j] = borderMesh;
                }
                elem.borderMeshes = elemRails;

                terrainArray[i] = elem;

                if (i % 100 == 0 || i == manager.patches.Count - 1) {
                    progressBar.SetProgress(0.45f + 0.2f * ((float)(i + 1) / manager.patches.Count));
                    yield return new WaitForEndOfFrame();
                }
            }
            savedCity.terrainPatches = terrainArray;

            var linesArray = new IO.SavedCity.BuildingLine[manager.buildings.Count];
            for (int i = 0; i < manager.buildings.Count; i++) {
                var line = manager.buildings[i];
                var elem = new IO.SavedCity.BuildingLine();
                elem.id = i;
                elem.name = line.gameObject.name;
                elem.state = line.state;

                var linePoints = line.GetPointsComponents();
                var elemPoints = new int[linePoints.Count];
                for (int j = 0; j < linePoints.Count; j++) {
                    elemPoints[j] = GetTerrainPoint(linePoints[j], pointsList, manager);
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

                linesArray[i] = elem;

                if (i % 10 == 0 || i == manager.buildings.Count - 1) {
                    progressBar.SetProgress(0.65f + 0.25f * ((float)(i + 1) / manager.buildings.Count));
                    yield return new WaitForEndOfFrame();
                }
            }
            savedCity.buildingLines = linesArray;

            savedCity.terrainPoints = GetTerrainPointsArray(pointsList);

            var genericObjects = new List<IO.SavedCity.GenericObject>();
            //reserved for future use
            savedCity.genericObjects = genericObjects.ToArray();

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

            progressBar.SetActive(false);
            post.Invoke();
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
            var newElem = new IO.SavedCity.TerrainPoint();
            newElem.anchorIndex = -1;
            newElem.elementId = -1;
            newElem.elementType = IO.SavedCity.LinkElementType.None;
            newElem.linkType = IO.SavedCity.LinkType.None;

            newElem.dividing = p.dividing;
            newElem.position = new States.SerializableVector3(p.GetPoint());
            if (p.anchor != null) {
                GameObject parent = null;
                TerrainAnchor realAnchor = null;
                TerrainPoint realPoint1 = null, realPoint2 = null;
                if (p.anchor is TerrainAnchor terrainAnchor && terrainAnchor != null) {
                    newElem.linkType = IO.SavedCity.LinkType.Point;
                    parent = terrainAnchor.transform.parent.gameObject;
                    realAnchor = terrainAnchor;
                } else if (p.anchor is LineAnchor lineAnchor && lineAnchor != null) {
                    newElem.linkType = IO.SavedCity.LinkType.Line;
                    parent = lineAnchor.start.transform.parent.gameObject;
                    realAnchor = lineAnchor.start.GetComponent<TerrainAnchor>();
                    if (realAnchor == null) {
                        realPoint1 = lineAnchor.start.GetComponent<TerrainPoint>();
                        realPoint2 = lineAnchor.end.GetComponent<TerrainPoint>();
                    }
                    newElem.percent = lineAnchor.percent;
                }
                if (realAnchor != null && parent != null) {
                    var road = parent.GetComponent<Road>();
                    var intersection = parent.GetComponentInChildren<Intersection.IntersectionComponent>();
                    if (road != null) {
                        var idx = road.anchorManager.GetTerrainAnchors().IndexOf(realAnchor);
                        var mIdx = manager.roads.IndexOf(road);
                        if (idx >= 0 && mIdx >= 0) {
                            newElem.anchorIndex = idx;
                            newElem.elementId = mIdx;
                            newElem.elementType = IO.SavedCity.LinkElementType.Road;
                        } else {
                            newElem.linkType = IO.SavedCity.LinkType.None;
                        }
                    } else if (intersection != null) {
                        var idx = intersection.intersection.anchorManager.GetTerrainAnchors().IndexOf(realAnchor);
                        var mIdx = manager.intersections.IndexOf(intersection.intersection);
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
                } else if (realPoint1 != null) { //only happens with line anchors
                    if (!dict.ContainsKey(realPoint1)) {
                        AddTerrainPointToDict(realPoint1, dict, manager);
                    }
                    if (!dict.ContainsKey(realPoint2)) {
                        AddTerrainPointToDict(realPoint2, dict, manager);
                    }
                    newElem.elementId = dict[realPoint1].Item1;
                    newElem.anchorIndex = dict[realPoint2].Item1;
                    newElem.elementType = IO.SavedCity.LinkElementType.TerrainPoint;
                } else {
                    newElem.linkType = IO.SavedCity.LinkType.None;
                }
            }
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
            var tRes = CityGroundHelper.heightmapResolution + 1;
            var terrain = manager.builder.helper.terrainObj.GetComponent<Terrain>();
            var heights = terrain.terrainData.GetHeights(0, 0, tRes, tRes);

            var heights1D = new byte[heights.GetLength(0) * heights.GetLength(1) * 4];
            System.Buffer.BlockCopy(heights, 0, heights1D, 0, heights1D.Length);
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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;
using SubMeshData = GeometryHelpers.SubMesh.SubMeshData;
using GH = GeometryHelper;

namespace GeometryHelpers {
    public class TerrainGeneration {
        public struct TerrainGenerationResult {
            public int res;
            public List<Material> materials;
            public List<string> materialNames;
            public int numMeshes;
            public List<Vector3> linePoints;
            public List<Vector2> uvs;
            public List<Vector3> vertices;
            public List<int> tris;
            public List<List<(List<int>, int)>> extraTris;

            public List<int[]> GetIndices() {
                var indices = new List<int[]> { tris.ToArray() };
                var tmpIndices = new List<List<int>>();
                for (int i = 1; i < numMeshes; i++) {
                    tmpIndices.Add(new List<int>());
                }
                for (int j = 0; j < extraTris.Count; j++) {
                    var tris2 = extraTris[j];
                    for (int k = 0; k < tris2.Count; k++) {
                        if (tris2[k].Item1.Count > 0) {
                            tmpIndices[tris2[k].Item2 - 1].AddRange(tris2[k].Item1);
                        }
                    }
                }
                for (int i = 1; i < numMeshes; i++) {
                    indices.Add(tmpIndices[i - 1].ToArray());
                }
                return indices;
            }
        }

        static bool CheckEdge(int p1, int p2, List<Vector3> allPoints, List<Vector3> perimeterPoints) {
            var diff = (p1 > p2) ? (p1 - p2) : (p2 - p1);
            var onEdge = diff == 1 || diff == perimeterPoints.Count - 1;
            onEdge = onEdge && p1 < perimeterPoints.Count && p2 < perimeterPoints.Count; //onEdge as calculated above assumes the points belong to the perimeter
            if (!onEdge) {
                var m = (allPoints[p1] + allPoints[p2]) * 0.5f;
                return GH.IsPointInsidePolygon(m, perimeterPoints);
            } else {
                return true;
            }

        }

        static List<Vector3> CleanPerimeterPoints(List<Vector3> perimeterPoints) {
            var res = new List<Vector3>() { perimeterPoints[0] };
            for (int i = 1; i < perimeterPoints.Count; i++) {
                if (!GH.AreVectorsEqual(perimeterPoints[i], res[res.Count - 1])) {
                    res.Add(perimeterPoints[i]);
                }
            }
            return res;
        }

        static CityElements.Types.Runtime.RoadType GetRoadType(ObjectState state) {
            var dict = CityElements.Types.Parsers.TypeParser.GetTerrainPatchBorderMeshTypes();
            return dict[state.Str("type", null)];
        }

        public static TerrainGenerationResult GetMesh(List<Vector3> perimeterPoints, List<Vector3> internalPoints,
            List<(List<Vector3> segment, List<Vector3> lefts, ObjectState state, ObjectState instanceState)> borderMeshes, string tex, int smooth, float uMult, float vMult) {

            perimeterPoints = CleanPerimeterPoints(perimeterPoints);
            var res = new TerrainGenerationResult();

            //create polygon line
            var linePoints = new List<Vector3>();
            linePoints.AddRange(perimeterPoints);
            linePoints.Add(perimeterPoints[0]);

            var allPoints = new List<Vector3>();
            allPoints.AddRange(perimeterPoints);
            if (internalPoints != null) allPoints.AddRange(internalPoints);
            if (allPoints.Count < 3) {
                res.res = 0;
                return res;
            }

            var (trisFull, points2) = GH.GetDelaunayTriangulation(allPoints);

            //constrain delaunay triangulation
            var edges = new List<List<int>>();
            for (int i = 0; i < perimeterPoints.Count; i++) {
                var edge = new List<int> {
                    i,
                    (i == perimeterPoints.Count - 1) ? 0 : (i + 1)
                };
                edges.Add(edge);
            }
            trisFull = ConstrainedDelaunay.ConstrainDelaunay(trisFull, allPoints, edges);

            var tris = new List<int>();

            //keep only triangles fully inside the polygon
            for (int i = 0; i < trisFull.Length; i += 3) {
                var p1 = trisFull[i];
                var p2 = trisFull[i + 1];
                var p3 = trisFull[i + 2];
                if (CheckEdge(p1, p2, allPoints, perimeterPoints)
                 && CheckEdge(p2, p3, allPoints, perimeterPoints)
                 && CheckEdge(p3, p1, allPoints, perimeterPoints)) {
                    tris.AddRange(new int[] { p1, p2, p3 });
                }
            }
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int i = 0; i < points2.Length; i++) {
                var p2 = points2[i];
                var point = new Vector3((float)p2.X, allPoints[i].y, (float)p2.Y);
                vertices.Add(point);
                uvs.Add(new Vector2(point.x, point.z));
            }

            if (smooth > 0) {
                GH.SmoothMeshConstrained(vertices, tris, perimeterPoints, smooth);
            }

            //adjust uvs
            for (int i = 0; i < uvs.Count; i++) {
                uvs[i] = new Vector2(uvs[i].x * uMult, uvs[i].y * vMult);
            }

            //border meshes (e.g. walls)
            var extraTris = new List<List<(List<int>, int)>>();
            var materials = new List<Material>();
            var materialNames = new List<string>();
            var invisible = tex == "" || tex == null;
            materials.Add(MaterialManager.GetMaterial(invisible ? "_TRANSPARENT_" : tex, invisible));
            materialNames.Add(invisible ? "_TRANSPARENT_" : tex);
            if (borderMeshes != null && borderMeshes.Count > 0) {
                foreach (var borderMesh in borderMeshes) {
                    if (borderMesh.segment.Count == 0) continue;
                    var thisExtraTris = new List<(List<int>, int)>();
                    var curType = GetRoadType(borderMesh.state);
                    var baseVertex = vertices.Count;
                    var subGen = new RoadLikeGenerator<CityElements.Types.RoadType>(borderMesh.state, null, borderMesh.segment.Count, curType.typeData.settings.textures.Length);
                    subGen.Reset(curType, borderMesh.state, null, borderMesh.segment.Count, curType.typeData.settings.textures.Length);
                    subGen.curvePoints.AddRange(borderMesh.segment);
                    subGen.curveRightVectors.AddRange(borderMesh.lefts);
                    subGen.sectionRights.AddRange(borderMesh.lefts);
                    var curMats = new List<int>();
                    var texIndexMap = new Dictionary<int, int>();
                    for (int i = 0; i < curType.typeData.settings.textures.Length; i++) {
                        var mat = curType.typeData.settings.textures[i];
                        var realMat = borderMesh.state.Str(mat);
                        if (!materialNames.Contains(realMat)) {
                            materials.Add(MaterialManager.GetMaterial(realMat));
                            materialNames.Add(realMat);
                        }
                        var index = materialNames.IndexOf(realMat);
                        curMats.Add(index);
                        texIndexMap.Add(i, index);
                    }
                    var len = GH.GetPathLength(borderMesh.segment.ToArray());
                    curType.FillInitialVariables(subGen.variableContainer, borderMesh.state, borderMesh.instanceState, len, borderMesh.segment.Count);
                    var z = 0.0f;
                    for (int i = 0; i < borderMesh.segment.Count; i++) {
                        if (i > 0) z += Vector3.Distance(borderMesh.segment[i - 1], borderMesh.segment[i]);
                        subGen.sectionMarkers.Add(z);
                        subGen.sectionVertices.Add(subGen.GetRawSectionVertices(i).Item1);
                    }
                    subGen.InitBaseSectionsInfo();
                    for (int i = 0; i < borderMesh.segment.Count; i++) {
                        subGen.InitSection(i);
                        subGen.AddSection(i, null);
                    }
                    for (int i = 0; i < subGen.tempIndices.Count; i++) {
                        for (int j = 0; j < subGen.tempIndices[i].Count; j++) {
                            subGen.tempIndices[i][j] += baseVertex;
                        }
                        thisExtraTris.Add((subGen.tempIndices[i], texIndexMap[i]));
                    }
                    extraTris.Add(thisExtraTris);
                    vertices.AddRange(subGen.tempVertices);
                    uvs.AddRange(subGen.tempUVs);
                }
            }
            var numMeshes = materialNames.Count;

            res.res = 1;
            res.materials = materials;
            res.materialNames = materialNames;
            res.numMeshes = numMeshes;
            res.linePoints = linePoints;
            res.uvs = uvs;
            res.vertices = vertices;
            res.tris = tris;
            res.extraTris = extraTris;
            return res;
        }

        public static SubMeshData GetSubmesh(List<Vector3> perimeterPoints, List<Vector3> internalPoints, List<(List<Vector3> segment,
            List<Vector3> lefts, ObjectState state, ObjectState instanceState)> borderMeshes, string tex, bool invisible, int smooth, float uMult, float vMult) {

            var terrain = GetMesh(perimeterPoints, internalPoints, borderMeshes, tex, smooth, uMult, vMult);
            var m = new SubMeshData();
            if (terrain.res == 0) return m;
            m.materials = terrain.materialNames.ToArray();

            m.vertices = terrain.vertices.ToArray();
            m.uvs = terrain.uvs.ToArray();
            var indices = terrain.GetIndices();
            m.indices = indices.ToArray();
            m.pos = Vector3.zero;
            m.scale = Vector3.one;
            return m;
        }
    }
}

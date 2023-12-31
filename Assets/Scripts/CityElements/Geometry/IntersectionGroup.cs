using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GH = GeometryHelper;
using States;

namespace RoadParts {
    public static class IntersectionGroup {

        public static void Generate(IntersectionGenerator.GeneratorState g, List<Vector3> vertices, List<Vector2> uvs,
            List<List<int>> outTris, List<string> outPartsInfo, bool discordRoads, CityElements.Types.Runtime.IntersectionType curType,
            List<List<Vector3>> sidePoints, List<object> sidePointStartRoads, List<object> sidePointEndRoads) {

            var subGen = new RoadLikeGenerator<CityElements.Types.JunctionType>(g.state, null, 0, 1);
            var vc = curType.variableContainer;

            //extra terrain pieces
            var lNeeded = new List<bool>();
            var paramSrcIndices = new List<int>();
            var subRoadCount0 = g.srManager.subRoadVertices.Count;
            var extraPiecesNames = new List<string>();
            foreach (var piece in curType.typeData.settings.extraTerrainPieces) {
                //determine if needed
                var matchingIndices = new List<int>();
                for (int swi = 0; swi < g.sidewalkVertices.Count; swi++) {
                    var junction = g.GetJunctionType(curType, swi);
                    var tempState = g.GetJunctionState(curType, swi);
                    subGen.Reset(junction, tempState, null, 1, junction.typeData.textures.Length);
                    var ends = g.sidewalkEnds[swi];
                    junction.FillInitialVariables(subGen.variableContainer, tempState, null, 0, 1, ends.Item1.generator.subGen, ends.Item2.generator.subGen);
                    var jvc = subGen.variableContainer;
                    if (jvc.floats[jvc.floatIndex[piece.junctionCondition]] > 0) {
                        matchingIndices.Add(swi);
                    }
                }
                var needed = false;
                switch(piece.junctionConditionMode) {
                    case "or":
                        needed = matchingIndices.Count > 0;
                        break;
                    case "and":
                        needed = matchingIndices.Count == g.sidewalkVertices.Count;
                        break;
                    case "xor":
                        needed = matchingIndices.Count == 1;
                        break;
                }
                var paramSrcIdx = -1;
                if (needed) {
                    var epc = curType.extraPiecesCalculators[piece.name];
                    paramSrcIdx = matchingIndices[(int)epc.paramSrcIdx.GetValue(vc)];
                }
                lNeeded.Add(needed);
                paramSrcIndices.Add(paramSrcIdx);
            }
            curType.FillExtraTerrainPiecesConditionVariables(paramSrcIndices, lNeeded);
            for (int pi = 0; pi < lNeeded.Count; pi++) {
                var piece = curType.typeData.settings.extraTerrainPieces[pi];
                var epc = curType.extraPiecesCalculators[piece.name];
                var paramSrcIdx = paramSrcIndices[pi];
                bool enabled = epc.condition.GetValue(curType.variableContainer);
                if (enabled) {
                    //get the junction splines for this piece
                    if (paramSrcIdx < 0) paramSrcIdx = 0;
                    ObjectState srcJState = null;
                    if (paramSrcIdx >= 0) {
                        srcJState = g.GetJunctionState(curType, paramSrcIdx);
                    }
                    var pieceTex = "NONE";
                    if (piece.textureSource == "junction" && srcJState != null) {
                        pieceTex = srcJState.Str(piece.texture);
                    } else if (piece.textureSource == "intersection") {
                        pieceTex = g.state.Str(piece.texture);
                    }
                    var pieceUVmult = new Vector2(epc.uMult.GetValue(vc), epc.vMult.GetValue(vc));
                    var pieceSubroad = g.srManager.CreateSubRoad(pieceTex, false, false, pieceUVmult, null, piece.facingUp ? 2 : 1);
                    for (int swi = 0; swi < g.sidewalkVertices.Count; swi++) {
                        var tempState = (srcJState != null && piece.splineOverride) ? srcJState : g.GetJunctionState(curType, swi);
                        var junctionInfo = GetPreparedJunction(swi, g, curType, subGen, tempState);
                        var spline = new List<Vector3>();
                        for (int i = 0; i < junctionInfo.segments; i++) {
                            subGen.InitSection(i);
                            spline.Add(junctionInfo.junction.GetExtraTerrainSplinesVertex(piece.name, subGen.variableContainer));
                        }
                        g.srManager.AddSegmentToSubroad(pieceSubroad, spline, g.planes[swi], false);
                    }
                    extraPiecesNames.Add(piece.name);
                }
            }

            //roads
            var subRoadCount = g.srManager.subRoadVertices.Count;
            for (int vi = 0; vi < subRoadCount; vi++) {
                var roadVertices = g.srManager.subRoadVertices[vi];
                var uvMult = g.srManager.uvMults[vi];
                var roadSegments = g.srManager.subRoadSegments[vi];
                var convexSegments = g.srManager.subRoadConvexSegments[vi];
                var roadType = g.srManager.subRoadTypes[vi];

                //road
                var trisFull = GH.GetDelaunayTriangulation(roadVertices).Triangles;
                var roadI0 = vertices.Count;
                var tris = new List<int>();

                //constrain delaunay triangulation to include road segments
                var edges = new List<List<int>>();
                foreach (var segment in roadSegments) {
                    for (int i = 0; i < segment.Count - 1; i++) {
                        var edge = new List<int> {
                            segment[i],
                            segment[i + 1]
                        };
                        edges.Add(edge);
                    }
                }
                trisFull = ConstrainedDelaunay.ConstrainDelaunay(trisFull, roadVertices, edges);

                //keep only triangles fully inside the polygon
                for (int i = 0; i < trisFull.Length; i += 3) {
                    var invalid = false;
                    for (int iS = 0; iS < roadSegments.Count; iS++) {
                        if (!convexSegments[iS]) continue;
                        var segment = roadSegments[iS];
                        var borderOnly = segment.Contains(trisFull[i]) && segment.Contains(trisFull[i + 1]) && segment.Contains(trisFull[i + 2]);
                        if (borderOnly) {
                            invalid = true;
                            break;
                        }
                    }
                    if (!invalid && discordRoads) {
                        var normal = GH.TriangleNormal(roadVertices[trisFull[i]], roadVertices[trisFull[i + 1]], roadVertices[trisFull[i + 2]]);
                        if (Vector3.Angle(normal, Vector3.up) > 85.0f) invalid = true;
                    }
                    if (!invalid) tris.AddRange(new List<int> { trisFull[i] + roadI0, trisFull[i + 1] + roadI0, trisFull[i + 2] + roadI0 });
                }

                for (int i = 0; i < roadVertices.Count; i++) {
                    var p2 = roadVertices[i];
                    vertices.Add(p2);
                    uvs.Add(new Vector2(p2.x * uvMult.x, p2.z * uvMult.y));
                }
                if (roadType == 1) {
                    tris.Reverse();
                }
                outTris.Add(tris);
                outPartsInfo.Add(vi < subRoadCount0 ? "terrain" : extraPiecesNames[vi - subRoadCount0]);
            }

            //adjust uvs
            for (int i = 0; i < uvs.Count; i++) {
                uvs[i] = new Vector2(uvs[i].x * vc.floats[vc.floatIndex["uMult"]], uvs[i].y * vc.floats[vc.floatIndex["vMult"]]); //TODO: redo?
            }

            //junctions
            int i0;
            for (int swi = 0; swi < g.sidewalkVertices.Count; swi++) {
                var tempState = g.GetJunctionState(curType, swi);
                var junctionInfo = GetPreparedJunction(swi, g, curType, subGen, tempState);
                var junction = junctionInfo.junction;
                i0 = vertices.Count;
                var anchors = junction.anchorsCalculators.GetValues(subGen.variableContainer);
                var startAnchorLine = sidePoints.Count;
                foreach (var a in anchors) {
                    sidePoints.Add(new List<Vector3>());
                    sidePointStartRoads.Add(junctionInfo.ends.Item1);
                    sidePointEndRoads.Add(junctionInfo.ends.Item2);
                }

                for (int c = 0; c < junction.typeData.components.Length; c++) {
                    var compMesh = junction.componentMeshes[c];
                    if (junction.subConditions[c] == null || junction.subConditions[c].GetValue(subGen.variableContainer) && compMesh.anchorsCalculators != null) {
                        junction.FillComponentVariables(subGen.variableContainer, c);
                        var subAnchors = compMesh.anchorsCalculators.GetValues(subGen.variableContainer);
                        foreach (var a in subAnchors) {
                            sidePoints.Add(new List<Vector3>());
                            sidePointStartRoads.Add(junctionInfo.ends.Item1);
                            sidePointEndRoads.Add(junctionInfo.ends.Item2);
                        }
                    }
                }
                for (int i = 0; i < junctionInfo.segments; i++) {
                    subGen.InitSection(i);
                    //TODO (maybe): fill the lanes
                    /*for (int j = 0; j < lanes.Count; j++) {
                        var p = lanesInfo[j];
                        if (p.component >= 0) curType.FillComponentVariables(subGen.variableContainer, p.component);
                        lanes[j].Add(Vector3.Lerp(p.startBound.GetValue(p.vars), p.endBound.GetValue(p.vars), p.pos));
                    }*/
                    subGen.AddSection(i, sidePoints, startAnchorLine);
                }
                vertices.AddRange(subGen.tempVertices);
                uvs.AddRange(subGen.tempUVs);

                for (int trisRawI = 0; trisRawI < subGen.tempIndices.Count; trisRawI++) {
                    var trisRaw = subGen.tempIndices[trisRawI];
                    var tris = new List<int>();
                    foreach (var i in trisRaw) {
                        tris.Add(i + i0);
                    }
                    outTris.Add(new List<int>(tris));
                    outPartsInfo.Add("junction_" + trisRawI + "." + swi);
                }
            }

            //crosswalk
            i0 = vertices.Count;
            for (int i = 0; i < g.crosswalkVertices.Count; i++) { //TODO: make parametric?
                var cw = g.crosswalkVertices[i];
                var length = (cw[0] - cw[2]).magnitude;
                var width = (cw[0] - cw[1]).magnitude;

                var totalLength = length / width;
                var adjLength = Mathf.Max(1, Mathf.Round(totalLength));

                var cwUvs = new List<Vector2>() { new Vector2(0, adjLength), new Vector2(1, adjLength), new Vector2(0, 0), new Vector2(1, 0) };
                vertices.AddRange(cw);
                uvs.AddRange(cwUvs);
                outTris.Add(new List<int>(GH.GetSectionIndices(i0, 2)));
                outPartsInfo.Add("crosswalk");
                i0 += 4;
            }
        }

        static (CityElements.Types.Runtime.JunctionType junction, int segments, (Road, Road) ends) GetPreparedJunction(int swi, IntersectionGenerator.GeneratorState g,
            CityElements.Types.Runtime.IntersectionType curType, RoadLikeGenerator<CityElements.Types.JunctionType> subGen, ObjectState tempState) {

            var junction = g.GetJunctionType(curType, swi);
            var vn = junction.typeData.sectionVertices.Length;
            var segments = g.sidewalkVertices[swi].Count / vn;
            subGen.Reset(junction, tempState, null, segments, junction.typeData.textures.Length);
            var lastVM = Vector3.zero;
            var totLength = 0.0f;
            var z = 0.0f;
            for (int i = 0; i < segments; i++) {
                var verts = new List<Vector3>();
                for (int j = 0; j < vn; j++) {
                    var vJ = g.sidewalkVertices[swi][i * vn + j];
                    verts.Add(vJ);
                }
                var vM = (verts[0] + verts[1]) * 0.5f;
                subGen.sectionVertices.Add(verts.ToArray());
                subGen.sectionRights.Add((verts[0] - verts[1]).normalized);
                z += i == 0 ? 0.0f : Vector3.Distance(vM, lastVM);
                subGen.sectionMarkers.Add(z);
                subGen.groundHeights.Add(g.groundHeights[swi][i]);
                lastVM = vM;
                totLength = z;
                //below lists unused here but needed for initialization
                subGen.curveDirections.Add(Vector3.zero);
                subGen.curvePoints.Add(Vector3.zero);
                subGen.curveRightVectors.Add(Vector3.zero);
            }
            var ends = g.sidewalkEnds[swi];
            junction.FillInitialVariables(subGen.variableContainer, tempState, null, totLength, segments, ends.Item1.generator.subGen, ends.Item2.generator.subGen);
            subGen.InitBaseSectionsInfo();
            return (junction, segments, ends);
        }
    }
}

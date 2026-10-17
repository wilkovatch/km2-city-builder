using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using SM = StringManager;

class CitywideTerrainGenerator {
    public async void CreateCityTerrainCoroutine(float distance, float segmentLength, float vertexFusionDistance, int smooth, float internalDistance, ElementManager manager, Node caller) {
        var progressBar = manager.builder.helper.curProgressBar;
        progressBar.SetText(SM.Get("GENERATING_PATCHES"));
        progressBar.SetActive(true);
        History.UndoManager.BeginBusy(SM.Get("GENERATING_PATCHES")); //the whole generation is a single undo step
        await caller.ToSignal(caller.GetTree(), "process_frame");
        var (anchors, doneAnchors) = manager.InitRoadAnchorsListForCitywideGeneration(manager.patches, x => { return x.GetPerimeterPointsComponents(); });
        var origAnchors = new List<TerrainAnchor>();
        origAnchors.AddRange(anchors);
        var lastShownPercent = 0.0f;
        var tmpPatches = new List<TerrainPatch>();
        var patches = new List<TerrainPatch>();
        var anchorSet = new HashSet<TerrainAnchor>(anchors);
        var doneAnchorSet = new HashSet<TerrainAnchor>(doneAnchors);
        while (anchors.Count > 0) {
            var anchor = anchors[0];
            var placer = new ElementPlacer.TerrainPlacer(manager);
            var patch = placer.GetPatch();
            patch.AddPerimeterPoint(anchor.GetObject(), anchor.GetPoint());
            var res = patch.AutoClose();
            if (res.resultCode == 1) {
                var toDelete = false;
                foreach (var point in res.points) {
                    if (anchorSet.Contains(point)) {
                        anchors.Remove(point);
                        anchorSet.Remove(point);
                        doneAnchors.Add(point);
                        doneAnchorSet.Add(point);
                    }
                    else if (doneAnchorSet.Contains(point)) {
                        toDelete = true;
                        break;
                    }
                }
                if (!toDelete) {
                    patches.Add(patch);
                    tmpPatches.Add(patch);
                }
                else {
                    patch.Delete();
                }
            }
            else {
                patch.Delete();
            }
            anchors.Remove(anchor);
            anchorSet.Remove(anchor);
            var percent = 1.0f - ((float)anchors.Count / origAnchors.Count);
            if (percent > lastShownPercent + 0.1f) {
                lastShownPercent = percent;
                progressBar.SetProgress(percent * 0.5f);
                await caller.ToSignal(caller.GetTree(), "process_frame");
            }
        }
        lastShownPercent = 0.0f;
        //detect and redo outer patches
        for (int i = 0; i < tmpPatches.Count; i++) {
            var patch = tmpPatches[i];
            foreach (var anchor in doneAnchors) {
                if (patch.IsPointInside(anchor)) {
                    //Generate outer vertices
                    var origPointsCount = patch.GetPerimeterPointCount();
                    var outerPoints = GeometryHelper.GetOuterPolygon(patch.GetPerimeterPoints(), distance);
                    outerPoints.Reverse();
                    foreach (var p in outerPoints) {
                        patch.AddPerimeterPoint(null, p);
                    }
                    outerPoints.Reverse();

                    //Generate last patch
                    var placer2 = new ElementPlacer.TerrainPlacer(manager);
                    var patch2 = placer2.GetPatch();
                    var points = patch.GetPerimeterPoints();
                    var comps = patch.GetPerimeterPointsComponents();
                    var ip1 = patch2.AddPerimeterPoint(comps[0].GetObject(), points[0]);
                    var op1 = patch2.AddPerimeterPoint(comps[origPointsCount - 1].GetObject(), points[origPointsCount - 1]);

                    //subdivide beginning split
                    var initialMidPoints = new List<TerrainPoint>();
                    if (internalDistance > 0) {
                        var p0 = points[origPointsCount - 1];
                        var p1 = points[origPointsCount];
                        var dir = GameEngine.Vector3.Normalize(p1 - p0);
                        var pT = p0;
                        while (GameEngine.Vector3.Distance(pT, p1) > 2 * internalDistance) {
                            pT += dir * internalDistance;
                            initialMidPoints.Add(patch2.AddPerimeterPoint(null, pT));
                        }
                    }

                    var op2 = patch2.AddPerimeterPoint(comps[origPointsCount].GetObject(), points[origPointsCount]);
                    var ip2 = patch2.AddPerimeterPoint(comps[comps.Count - 1].GetObject(), points[points.Count - 1]);

                    //subdivide ending split
                    var finalMidPoints = new List<TerrainPoint>();
                    if (internalDistance > 0) {
                        var p0 = points[points.Count - 1];
                        var p1 = points[0];
                        var dir = GameEngine.Vector3.Normalize(p1 - p0);
                        var pT = p0;
                        while (GameEngine.Vector3.Distance(pT, p1) > 2 * internalDistance) {
                            pT += dir * internalDistance;
                            finalMidPoints.Add(patch2.AddPerimeterPoint(null, pT));
                        }
                    }

                    patches.Add(patch2);

                    //Split in multiple patches and remove excess vertices
                    var curPos = 0.0f;
                    var startJ = 0;
                    GameObject lastOuterPoint = ip2.GetObject();
                    var lastMidPoints = finalMidPoints;
                    lastMidPoints.Reverse();
                    Vector3 lastInsertedPoint = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    for (int j = 1; j < origPointsCount; j++) {
                        var dist = GameEngine.Vector3.Distance(points[j], points[j - 1]);
                        curPos += dist;
                        if (curPos > segmentLength || j == origPointsCount - 1) {
                            var subOuterPoints = outerPoints.GetRange(startJ, j - startJ + 1);
                            subOuterPoints.Reverse();
                            var placer3 = new ElementPlacer.TerrainPlacer(manager);
                            var patch3 = placer3.GetPatch();
                            for (int k = startJ; k <= j; k++) {
                                patch3.AddPerimeterPoint(comps[k].anchor is TerrainAnchor ta && !GO.IsNull(ta) ? ta.GetObject() : null, points[k]);
                            }
                            //subdivide beginning split
                            var newLastMidPoints = new List<TerrainPoint>();
                            if (internalDistance > 0) {
                                if (j == origPointsCount - 1) {
                                    //finalMidPoints.Reverse();
                                    foreach (var mp in initialMidPoints) {
                                        patch3.AddPerimeterPoint(mp.GetObject(), mp.GetPoint());
                                    }
                                }
                                else {
                                    var p0 = points[j];
                                    var p1 = subOuterPoints[0];
                                    var dir = GameEngine.Vector3.Normalize(p1 - p0);
                                    var pT = p0;
                                    while (GameEngine.Vector3.Distance(pT, p1) > 2 * internalDistance) {
                                        pT += dir * internalDistance;
                                        newLastMidPoints.Add(patch3.AddPerimeterPoint(null, pT));
                                    }
                                }
                            }
                            GameObject existingPoint = null;
                            GameObject firstOuterP = null;
                            for (int k = 0; k < j - startJ; k++) {
                                existingPoint = (j == origPointsCount - 1 && k == 0) ? op2.GetObject() : null;
                                if (k == 0 || (GameEngine.Vector3.Distance(lastInsertedPoint, subOuterPoints[k]) > vertexFusionDistance)
                                     && GameEngine.Vector3.Distance(subOuterPoints[j - startJ], subOuterPoints[k]) > vertexFusionDistance) {
                                    var newP = patch3.AddPerimeterPoint(existingPoint, subOuterPoints[k]);
                                    if (firstOuterP == null) firstOuterP = newP.GetObject();
                                    lastInsertedPoint = subOuterPoints[k];
                                }
                            }
                            patch3.AddPerimeterPoint(lastOuterPoint, subOuterPoints[subOuterPoints.Count - 1]);
                            lastOuterPoint = firstOuterP;

                            //subdivide ending split
                            if (internalDistance > 0) {
                                lastMidPoints.Reverse();
                                foreach (var mp in lastMidPoints) {
                                    patch3.AddPerimeterPoint(mp.GetObject(), mp.GetPoint());
                                }
                                lastMidPoints = newLastMidPoints;
                            }

                            patches.Add(patch3);
                            curPos = 0;
                            startJ = j;
                        }
                    }
                    patch.Delete();
                    break;
                }
            }
            var percent = (float)i / tmpPatches.Count;
            if (percent > lastShownPercent + 0.1f) {
                lastShownPercent = percent;
                progressBar.SetProgress(0.5f + percent * 0.5f);
                await caller.ToSignal(caller.GetTree(), "process_frame");
            }
        }
        foreach (var patch in patches) {
            if (internalDistance > 0) patch.AutoGenerateInternalPoints(internalDistance);
            patch.state.SetInt("smooth", smooth);
            //TODO: custom parameters
            if (!manager.patches.Contains(patch)) manager.AddPatch(patch);
        }
        manager.worldChanged = true;
        await caller.ToSignal(caller.GetTree(), "process_frame");
        manager.ShowAnchors(false);
        progressBar.SetActive(false);
        History.UndoManager.EndBusy();
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

class CitywideBuildingGenerator {
    (PointLink, TerrainAnchor) GetLongestLink(List<TerrainAnchor> anchors) {
        PointLink maxLink = null;
        TerrainAnchor maxAnchor = null;
        float maxDist = 0.0f;
        foreach (var anchor in anchors) {
            var link = anchor.gameObject.GetComponentInChildren<PointLink>();
            if (link != null) {
                var dist = Vector3.Distance(link.transform.parent.position, link.next.transform.position);
                if (dist > maxDist) {
                    maxDist = dist;
                    maxLink = link;
                    maxAnchor = anchor;
                }
            }
        }
        return (maxLink, maxAnchor);
    }

    Dictionary<TerrainAnchor, List<TerrainPatch>> GetAnchorPatchDict(ElementManager manager, List<TerrainAnchor> anchors) {
        var res = new Dictionary<TerrainAnchor, List<TerrainPatch>>();
        foreach (var a in anchors) {
            res.Add(a, new List<TerrainPatch>());
        }
        foreach (var t in manager.patches) {
            foreach (var p in t.GetPerimeterPointsComponents()) {
                if (p.anchor != null && p.anchor is TerrainAnchor ta) {
                    if (res.ContainsKey(ta)) {
                        res[ta].Add(t);
                    }
                }
            }
        }
        return res;
    }

    bool HasValidTerrainPatch(Dictionary<TerrainAnchor, List<TerrainPatch>>  dict, TerrainAnchor anchor, List<string> texturesToPlaceOn) {
        if (!dict.ContainsKey(anchor)) return false;
        var lst = dict[anchor];
        foreach (var t in lst) {
            if (texturesToPlaceOn.Contains(t.state.Str("texture"))) return true;
        }
        return false;
    }

    public IEnumerator CreateCityBuildingsCoroutine(GameObject parent, float minLength, float maxLength, List<ObjectState> states, 
        bool subdivide, ElementManager manager, ObjectState lineState, List<string> texturesToPlaceOn) {

        var progressBar = manager.builder.helper.curProgressBar;
        progressBar.SetText(SM.Get("GENERATING_BUILDINGS"));
        progressBar.SetActive(true);
        yield return new WaitForEndOfFrame();
        var anchors = new List<TerrainAnchor>();
        var doneAnchors = new List<TerrainAnchor>();
        var container = manager.GetRoadContainer();
        for (int i = 0; i < container.transform.childCount; i++) {
            for (int j = 0; j < container.transform.GetChild(i).childCount; j++) {
                var anchor = container.transform.GetChild(i).GetChild(j).GetComponent<TerrainAnchor>();
                if (anchor != null) {
                    var notUsed = true;
                    foreach (var line in manager.buildings) {
                        foreach (var point in line.GetPointsComponents()) {
                            if (anchor.Equals(point.anchor)) {
                                notUsed = false;
                                break;
                            }
                        }
                        if (!notUsed) break;
                    }
                    if (notUsed) anchors.Add(anchor);
                    else doneAnchors.Add(anchor);
                }
            }
        }
        var dict = GetAnchorPatchDict(manager, anchors);
        var origAnchors = new List<TerrainAnchor>();
        origAnchors.AddRange(anchors);
        var lastShownPercent = 0.0f;
        var tmpPatches = new List<BuildingLine>();
        var lines = new List<BuildingLine>();
        var anchorSet = new HashSet<TerrainAnchor>(anchors);
        var doneAnchorSet = new HashSet<TerrainAnchor>(doneAnchors);
        while (anchors.Count > 0) {
            var (link, anchor) = GetLongestLink(anchors);
            if (link != null) {
                var loopPoints = anchor.GetPointListTo(anchor);
                if (loopPoints.Count > 1) {
                    var validLoop = texturesToPlaceOn == null || HasValidTerrainPatch(dict, anchor, texturesToPlaceOn);
                    if (validLoop) {
                        var placer = new ElementPlacer.BuildingPlacer(manager);
                        var line = placer.GetLine();
                        line.state = lineState;
                        line.AddPoint(link.gameObject, (link.transform.parent.position + link.next.transform.position) * 0.5f);
                        var res = line.AutoClose(minLength, maxLength, states, subdivide);
                        if (res.resultCode == 1) {
                            var toDelete = false;
                            foreach (var point in res.points) {
                                if (anchorSet.Contains(point)) {
                                    anchors.Remove(point);
                                    anchorSet.Remove(point);
                                    doneAnchors.Add(point);
                                    doneAnchorSet.Add(point);
                                } else if (doneAnchorSet.Contains(point)) {
                                    toDelete = true;
                                    break;
                                }
                            }
                            if (!toDelete) {
                                lines.Add(line);
                                tmpPatches.Add(line);
                            } else {
                                line.Delete();
                            }
                        } else {
                            line.Delete();
                        }
                        anchors.Remove(anchor);
                        anchorSet.Remove(anchor);
                        var percent = 1.0f - ((float)anchors.Count / origAnchors.Count);
                        if (percent > lastShownPercent + 0.1f) {
                            lastShownPercent = percent;
                            progressBar.SetProgress(percent);
                            yield return new WaitForEndOfFrame();
                        }
                    } else {
                        foreach (var point in loopPoints) {
                            anchors.Remove(point);
                            anchorSet.Remove(point);
                        }
                        anchors.Remove(anchor);
                        anchorSet.Remove(anchor);
                    }
                } else {
                    anchors.Remove(anchor);
                    anchorSet.Remove(anchor);
                }
            } else {
                if (anchor == null) break;
                anchors.Remove(anchor);
                anchorSet.Remove(anchor);
            }
        }
        manager.worldChanged = true;
        yield return new WaitForEndOfFrame();
        manager.ShowAnchors(false);
        progressBar.SetActive(false);
        yield return null;
    }
}
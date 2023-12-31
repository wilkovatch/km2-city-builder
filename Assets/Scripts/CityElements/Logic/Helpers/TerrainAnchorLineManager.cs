using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainAnchorLineManager {
    List<TerrainAnchor> terrainAnchors = new List<TerrainAnchor>();
    List<List<Vector3>> oldAnchors = new List<List<Vector3>>();
    bool recycleAnchors = false;
    public List<TerrainAnchor> startAnchors = new List<TerrainAnchor>();
    public List<TerrainAnchor> endAnchors = new List<TerrainAnchor>();
    public List<object> startConnections = new List<object>();
    public List<object> endConnections = new List<object>();
    public object parentObj;

    public TerrainAnchorLineManager(object parent) {
        parentObj = parent;
    }

    public void ShowTerrainAnchors(bool active) {
        foreach (var anchor in terrainAnchors) {
            anchor.SetActive(active);
        }
    }

    public List<TerrainAnchor> GetTerrainAnchors() {
        var res = new List<TerrainAnchor>();
        res.AddRange(terrainAnchors);
        return res;
    }

    public void Update(GameObject geo, List<List<Vector3>> generatedAnchorLines, List<object> startConns, List<object> endConns, List<List<Vector3>> centerLines) {
        CleanTerrainAnchors(generatedAnchorLines, startConns, endConns);
        if (recycleAnchors) {
            var allPoints = new List<Vector3>();
            var allCenters = new List<Vector3>();
            int i = 0;
            foreach (var line in generatedAnchorLines) {
                allPoints.AddRange(line);
                allCenters.AddRange(centerLines[i++]);
            }
            UpdateTerrainAnchors(terrainAnchors, allPoints, allCenters);
        } else {
            for (int i = 0; i < generatedAnchorLines.Count; i++) {
                CreateTerrainAnchorLine(geo, terrainAnchors, generatedAnchorLines[i], centerLines[i]);
                startConnections.Add(startConns[i]);
                endConnections.Add(endConns[i]);
            }
        }
    }

    public static TerrainAnchor FindMatching(Vector3 point, List<TerrainAnchor> list) {
        for (int j = 0; j < list.Count; j++) {
            var otherAnchor = list[j];
            var match = Vector3.Distance(point, otherAnchor.transform.position) < 0.01f;
            if (match) {
                return otherAnchor;
            }
        }
        return null;
    }

    TerrainAnchor FindMatching(TerrainAnchor thisAnchor, List<TerrainAnchor> list) {
        for (int j = 0; j < list.Count; j++) {
            var otherAnchor = list[j];
            if (thisAnchor.IsTheSameAs(otherAnchor)) {
                return otherAnchor;
            }
        }
        return null;
    }

    public void ConnectTo(TerrainAnchorLineManager other) {
        if (parentObj is Intersection intersection && other.parentObj is Road road) { //sanity check
            if (other.startAnchors.Count < 2 || other.endAnchors.Count < 2) return;
            var intersectionIsStart = road.startIntersection == intersection;
            for (int i = 0; i < startAnchors.Count; i++) {
                var intersectionAnchor = startAnchors[i];
                if (startConnections[i] != other.parentObj) continue;
                TerrainAnchor roadAnchor;
                if (intersectionIsStart) {
                    roadAnchor = FindMatching(intersectionAnchor, other.startAnchors);
                    if (roadAnchor != null) roadAnchor.prev = intersectionAnchor;
                } else {
                    roadAnchor = FindMatching(intersectionAnchor, other.endAnchors);
                    if (roadAnchor != null) roadAnchor.next = intersectionAnchor;
                }
                intersectionAnchor.prev = roadAnchor;
            }
            for (int i = 0; i < endAnchors.Count; i++) {
                var intersectionAnchor = endAnchors[i];
                if (endConnections[i] != other.parentObj) continue;
                TerrainAnchor roadAnchor;
                if (intersectionIsStart) {
                    roadAnchor = FindMatching(intersectionAnchor, other.startAnchors);
                    if (roadAnchor != null) roadAnchor.prev = intersectionAnchor;
                } else {
                    roadAnchor = FindMatching(intersectionAnchor, other.endAnchors);
                    if (roadAnchor != null) roadAnchor.next = intersectionAnchor;
                }
                intersectionAnchor.next = roadAnchor;
            }
        }
    }

    void UpdateTerrainAnchors(List<TerrainAnchor> terrainAnchors, List<Vector3> points, List<Vector3> centers) {
        if (points.Count == 0) return;
        for (int i = 0; i < points.Count; i++) {
            terrainAnchors[i].transform.position = points[i];
            terrainAnchors[i].insideDirection = (centers[i] - points[i]).normalized;
        }
    }

    void CreateTerrainAnchorLine(GameObject gameObject, List<TerrainAnchor> terrainAnchors, List<Vector3> points, List<Vector3> centers) {
        if (points.Count == 0) return;
        var newList = new List<TerrainAnchor>();
        for (int i = 0; i < points.Count; i++) {
            var newAnchor = TerrainAnchor.Create(gameObject, points[i], 1);
            newList.Add(newAnchor);
        }
        for (int i = 0; i < points.Count; i++) {
            var anchor = newList[i];
            var prev = i == 0 ? null : newList[i - 1];
            var next = (i == points.Count - 1) ? null : newList[i + 1];
            anchor.Initialize(prev, next, false);
            if (prev != null && next != null) {
                anchor.gameObject.GetComponent<Handle>().SetScale(Vector3.one);
            }
            anchor.insideDirection = (centers[i] - points[i]).normalized;
        }
        terrainAnchors.AddRange(newList);
        startAnchors.Add(newList[0]);
        endAnchors.Add(newList[newList.Count - 1]);
    }

    bool CompareAnchors(List<List<Vector3>> anchors, List<object> startConns, List<object> endConns) {
        if (anchors.Count != oldAnchors.Count) return true;
        for (int i = 0; i < anchors.Count; i++) {
            if (anchors[i].Count != oldAnchors[i].Count) {
                return true;
            }
        }
        if (startConnections.Count != startConns.Count) return true;
        for (int i = 0; i < startConns.Count; i++) {
            if (startConnections[i] != startConns[i]) return true;
        }
        if (endConnections.Count != endConns.Count) return true;
        for (int i = 0; i < endConns.Count; i++) {
            if (endConnections[i] != endConns[i]) return true;
        }
        return false;
    }

    List<List<Vector3>> CloneList(List<List<Vector3>> list) {
        var res = new List<List<Vector3>>();
        for (int i = 0; i < list.Count; i++) {
            var item = new List<Vector3>();
            item.AddRange(list[i]);
            res.Add(item);
        }
        return res;
    }

    void CleanTerrainAnchors(List<List<Vector3>> anchors, List<object> startConns, List<object> endConns) {
        var changed = CompareAnchors(anchors, startConns, endConns);
        if (!changed) {
            recycleAnchors = true;
            return;
        }
        recycleAnchors = false;
        foreach (var anchor in terrainAnchors) {
            anchor.Delete();
        }
        terrainAnchors.Clear();
        startAnchors.Clear();
        endAnchors.Clear();
        startConnections.Clear();
        endConnections.Clear();

        oldAnchors = CloneList(anchors);
    }
}

using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

public class TerrainAnchorLineManager {
    List<TerrainAnchor> terrainAnchors = new List<TerrainAnchor>();
    List<List<Vector3>> oldAnchors = new List<List<Vector3>>();
    bool recycleAnchors = false;
    int customElementLinesStart = int.MaxValue, oldCustomElementLinesStart = int.MaxValue; //the lines from this one on have custom element only anchors
    public List<TerrainAnchor> startAnchors = new List<TerrainAnchor>();
    public List<TerrainAnchor> endAnchors = new List<TerrainAnchor>();
    public List<object> startConnections = new List<object>();
    public List<object> endConnections = new List<object>();
    public object parentObj;
    const float RoadEndAnchorScale = 2.0f;
    //the anchors of the two ends of a road, after the others so that the indices of those don't change. They aren't recreated
    //with the others (when the number of the sections or the connections change), so a point stays on an end while the road changes
    TerrainAnchor startRoadEnd, endRoadEnd;

    public TerrainAnchorLineManager(object parent) {
        parentObj = parent;
    }

    //the custom element only anchors are shown only with forCustomElements, the ends of a road only with roadEnds, while they're free
    public void ShowTerrainAnchors(bool active, bool forCustomElements = false, bool roadEnds = false) {
        foreach (var anchor in terrainAnchors) {
            anchor.SetActive(active && (forCustomElements || !anchor.customElementsOnly));
        }
        if (parentObj is not Road road) return;
        var showEnds = active && forCustomElements && roadEnds && !road.ghost;
        if (!GO.IsNull(startRoadEnd)) startRoadEnd.SetActive(showEnds && road.startIntersection == null);
        if (!GO.IsNull(endRoadEnd)) endRoadEnd.SetActive(showEnds && road.endIntersection == null);
    }

    //the next update recreates the anchors instead of moving the existing ones (and the terrain points anchored to them)
    public void Invalidate() {
        oldAnchors = new List<List<Vector3>>();
        if (!GO.IsNull(startRoadEnd)) startRoadEnd.Delete();
        if (!GO.IsNull(endRoadEnd)) endRoadEnd.Delete();
        startRoadEnd = endRoadEnd = null;
    }

    public List<TerrainAnchor> GetTerrainAnchors() {
        var res = new List<TerrainAnchor>();
        res.AddRange(terrainAnchors);
        if (!GO.IsNull(startRoadEnd)) res.Add(startRoadEnd);
        if (!GO.IsNull(endRoadEnd)) res.Add(endRoadEnd);
        return res;
    }

    //inside: a point along the road from that end
    public void UpdateRoadEnds(GameObject geo, Vector3 start, Vector3 startInside, Vector3 end, Vector3 endInside) {
        startRoadEnd = UpdateRoadEnd(startRoadEnd, geo, start, startInside);
        endRoadEnd = UpdateRoadEnd(endRoadEnd, geo, end, endInside);
    }

    static TerrainAnchor UpdateRoadEnd(TerrainAnchor anchor, GameObject geo, Vector3 position, Vector3 inside) {
        if (GO.IsNull(anchor)) {
            anchor = TerrainAnchor.Create(geo, position, RoadEndAnchorScale);
            anchor.Initialize(null, null, false);
            anchor.customElementsOnly = true;
            anchor.roadEnd = true;
            GS.Cast<Handle>(anchor.GetObject()).SetColor(GameEngine.Color.cyan);
        } else {
            GO.SetPosition(anchor.GetObject(), position);
        }
        anchor.insideDirection = GameEngine.Vector3.Normalize(inside - position);
        return anchor;
    }

    public void Update(GameObject geo, List<List<Vector3>> generatedAnchorLines, List<object> startConns, List<object> endConns, List<List<Vector3>> centerLines,
        int customElementLinesStart = int.MaxValue) {
        this.customElementLinesStart = customElementLinesStart;
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
                CreateTerrainAnchorLine(geo, terrainAnchors, generatedAnchorLines[i], centerLines[i], i >= customElementLinesStart);
                startConnections.Add(startConns[i]);
                endConnections.Add(endConns[i]);
            }
        }
    }

    public static TerrainAnchor FindMatching(Vector3 point, List<TerrainAnchor> list) {
        for (int j = 0; j < list.Count; j++) {
            var otherAnchor = list[j];
            var match = GameEngine.Vector3.Distance(point, otherAnchor.GetPoint()) < 0.01f;
            if (match) {
                return otherAnchor;
            }
        }
        return null;
    }

    TerrainAnchor FindMatching(TerrainAnchor thisAnchor, List<TerrainAnchor> list) {
        for (int j = 0; j < list.Count; j++) {
            var otherAnchor = list[j];
            if (!otherAnchor.customElementsOnly && thisAnchor.IsTheSameAs(otherAnchor)) {
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
            GO.SetPosition(terrainAnchors[i].GetObject(), points[i]);
            terrainAnchors[i].insideDirection = GameEngine.Vector3.Normalize(centers[i] - points[i]);
        }
    }

    void CreateTerrainAnchorLine(GameObject gameObject, List<TerrainAnchor> terrainAnchors, List<Vector3> points, List<Vector3> centers, bool customElementsOnly) {
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
                GS.Cast<Handle>(anchor.GetObject()).SetScale(GameEngine.Vector3.one);
            }
            anchor.insideDirection = GameEngine.Vector3.Normalize(centers[i] - points[i]);
            anchor.customElementsOnly = customElementsOnly;
        }
        terrainAnchors.AddRange(newList);
        startAnchors.Add(newList[0]);
        endAnchors.Add(newList[newList.Count - 1]);
    }

    bool CompareAnchors(List<List<Vector3>> anchors, List<object> startConns, List<object> endConns) {
        if (anchors.Count != oldAnchors.Count || customElementLinesStart != oldCustomElementLinesStart) return true;
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
        oldCustomElementLinesStart = customElementLinesStart;
    }
}

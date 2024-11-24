using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;

[System.Serializable]
public class TerrainBorderMesh {
    public ObjectState state, instanceState;
    public List<TerrainPoint> segment = new List<TerrainPoint>();
    public List<Vector3> oldSegment = new List<Vector3>();

    public void UpdateOlds() {
        oldSegment = new List<Vector3>();
        foreach (var point in segment) {
            oldSegment.Add(point.GetPoint());
        }
    }

    public bool DidChange() {
        if (state.HasChanged()) return true;
        if (segment.Count != oldSegment.Count) return true;
        for (int i = 0; i < segment.Count; i++) {
            if (!GeometryHelper.AreVectorsEqual(segment[i].GetPoint(), oldSegment[i])) return true;
        }
        return false;
    }
}
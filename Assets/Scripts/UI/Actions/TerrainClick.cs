using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class TerrainClick : MonoBehaviour {
    public CityGroundHelper helper;
    RaycastHit hit, projectedHit;
    public TerrainAction modifier = null;
    public bool editEnabled = false;
    bool isModifying = false;
    public bool uiEnabled = true;

    void Update() {
        if (!uiEnabled) return;
        if (modifier == null || (!Input.GetMouseButton(0) && modifier is TerrainModifier.Null)) {
            if (isModifying) helper.terrainObj.GetComponent<Terrain>().terrainData.SyncHeightmap();
            isModifying = false;
            return;
        }
        if (!Input.GetMouseButton(0) && isModifying) {
            helper.terrainObj.GetComponent<Terrain>().terrainData.SyncHeightmap();
            isModifying = false;
        }
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var masks = modifier.GetLayerMasks();
        var hits = new List<RaycastHit?>();
        foreach (var mask in masks) {
            var foundHit = false;
            if (!OnUI() && editEnabled) {
                var found = Physics.Raycast(transform.position, ray.direction, out hit, float.MaxValue, mask, QueryTriggerInteraction.Ignore);
                if (found) {
                    foundHit = true;
                    if (Input.GetKey(KeyCode.LeftShift)) {
                        SnapToGrid(hit, hits, mask);
                    } else {
                        hits.Add(hit);
                    }
                    isModifying = true;
                }
            }
            if (!foundHit) hits.Add(null);
        }
        modifier.Apply(hits);
    }

    bool OnUI() {
        var pos = Input.mousePosition;
        var xOut = pos.x < 0 || pos.x > Screen.width;
        var yOut = pos.y < 0 || pos.y > Screen.height;
        return UIDetection.OnUI() || xOut || yOut;
    }

    void SnapToGrid(RaycastHit hit, List<RaycastHit?> hits, int mask) {
        var pos = hit.point + Vector3.up * 1000000;
        pos = new Vector3(Mathf.Round(pos.x), pos.y, Mathf.Round(pos.z));
        var found = Physics.Raycast(pos, Vector3.down, out projectedHit, float.MaxValue, mask, QueryTriggerInteraction.Ignore);
        if (found) {
            hits.Add(projectedHit);
        } else {
            hits.Add(hit);
        }
    }
}
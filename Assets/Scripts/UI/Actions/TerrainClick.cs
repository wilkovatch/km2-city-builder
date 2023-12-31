using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class TerrainClick : MonoBehaviour {
    public CityGroundHelper helper;
    RaycastHit hit;
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
        bool onUI = UIDetection.OnUI();
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var masks = modifier.GetLayerMasks();
        var hits = new List<RaycastHit?>();
        foreach (var mask in masks) {
            if (!onUI && editEnabled && Physics.Raycast(transform.position, ray.direction, out hit, float.MaxValue, mask, QueryTriggerInteraction.Ignore)) {
                hits.Add(hit);
                isModifying = true;
            } else {
                hits.Add(null);
            }
        }
        modifier.Apply(hits);
    }
}
using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;

public partial class TerrainClick : Control {
    // City terrain is only a few thousand units across. Keeping ray lengths bounded
    // avoids constructing physics queries with near-infinite endpoints.
    const float TerrainRayDistance = 100000f;
    public CityGroundHelper helper;
    RaycastHit hit, projectedHit;
    public TerrainAction modifier = null;
    bool isModifying = false;
    public bool uiEnabled = true;
    int strokeId = 0; //the changes made while the button is held (brush strokes, mesh painting) are a single undo step

    public override void _Ready() {
        //run before the gizmo (on the camera, earlier in the tree): the actions must see the selection
        //from before the click, otherwise the click that selects a road point also inserts a point after it
        ProcessPriority = -1;
    }

    public override void _Process(double delta) {
        if (!uiEnabled) return;
        if (modifier == null || (!Input.GetInputPressed(KeyCommand.Select) && modifier is TerrainModifier.Null)) {
            if (isModifying) GameEngine.Terrain.SyncHeightmap(helper.terrainObj);
            isModifying = false;
            return;
        }
        if (!Input.GetInputPressed(KeyCommand.Select) && isModifying) {
            GameEngine.Terrain.SyncHeightmap(helper.terrainObj);
            isModifying = false;
        }
        var ray = GameEngine.Raycast.GetRayFromScreen(GetViewport().GetCamera3D(), GetViewport().GetMousePosition());
        var masks = modifier.GetLayerMasks();
        var hits = new List<RaycastHit?>();
        foreach (var mask in masks) {
            var foundHit = false;
            if (!OnUI()) {
                var found = Raycast(ray.origin, ray.direction, out hit, TerrainRayDistance, mask);
                if (found) {
                    foundHit = true;
                    if (Input.GetInputPressed(KeyCommand.Modifier1)) {
                        SnapToGrid(hit, hits, mask);
                    } else {
                        hits.Add(hit);
                    }
                    isModifying = true;
                }
            }
            if (!foundHit) hits.Add(null);
        }
        if (Input.GetInputStart(KeyCommand.Select)) strokeId++;
        History.UndoManager.Record(modifier.GetType().Name, () => modifier.Apply(hits), (nameof(TerrainClick), strokeId));
    }

    bool OnUI() {
        var pos = GetViewport().GetMousePosition();
        var xOut = pos.X < 0 || pos.X > DisplayServer.WindowGetSize().X;
        var yOut = pos.Y < 0 || pos.Y > DisplayServer.WindowGetSize().Y;
        return UIDetection.OnUI() || xOut || yOut;
    }

    void SnapToGrid(RaycastHit hit, List<RaycastHit?> hits, int mask) {
        const float snapRayHeight = 1000000;
        var pos = hit.point + Vector3.Up * snapRayHeight;
        pos = new Vector3(Mathf.Round(pos.X), pos.Y, Mathf.Round(pos.Z));
        // The ray starts snapRayHeight above the hit, so it must be longer than that to reach it.
        var found = Raycast(pos, Vector3.Down, out projectedHit, snapRayHeight * 2, mask);
        if (found) {
            hits.Add(projectedHit);
        } else {
            hits.Add(hit);
        }
    }

    //the terrain can't be clicked while it's hidden (View->Hide terrain grid)
    bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, int mask) {
        return modifier.Raycast(helper, origin, direction, out hit, distance, CityGroundHelper.ExcludeHiddenTerrain(mask));
    }
}

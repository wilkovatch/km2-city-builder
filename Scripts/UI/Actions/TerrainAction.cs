using System.Collections;
using System.Collections.Generic;
using Godot;
using RaycastHit = GameEngine.Raycast.RaycastHit;

public abstract class TerrainAction {
    public abstract void ShowOnMap(List<RaycastHit?> hits);
    public abstract void Apply(List<RaycastHit?> hits);
    public virtual bool Raycast(Node3D parent, Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, int mask) {
        return GameEngine.Raycast.DoRaycast(parent, origin, direction, out hit, distance, mask);
    }
    public virtual List<int> GetLayerMasks() {
        return new List<int> { ~(1 << 7) };
    }
    //the index (in GetLayerMasks) of the hit on the ground, where free points are placed (-1: the action doesn't place
    //points); with the terrain hidden it falls back to the zero height plane
    public virtual int GroundHitIndex => -1;
    //whether the next point can be placed above the ground (PlacementHeight): false while the action places nothing, or
    //when what it places is always put on the ground (by its placement rules)
    public virtual bool SupportsPlacementHeight => false;
}
